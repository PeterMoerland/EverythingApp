using System.Text.Json;
using EverythingApp.Models;
using Microsoft.JSInterop;

namespace EverythingApp.Services;

/// <summary>
/// Welk exemplaar hangt er aan de EtherCAT-adapter? (wens gebruiker 5-11-2026: auto-detect bij Producten, en het juiste
/// exemplaar herkennen als in de app het verkeerde gekozen is.)
/// Werkwijze: de serienummers van de slaves opzoeken bij de halffabricaten (attribuut drive/can:n:serienummer), dan per
/// halffabricaat de samenstelling omhoog. Van de gemeenschappelijke exemplaren wint het hoogste waarvan het type precies
/// zoveel driveposities heeft als er slaves zijn: een robot aan de bus = de robot, een losse RZ-print = de print, ook als
/// die print in een robot geboekt staat.
/// </summary>
public class ExemplaarHerkenning
{
    private readonly IProductRepository _repo;
    private readonly IDriveRepository _drives;
    private readonly WerkplekAgent _agent;
    private List<DriveSoort>? _typen;

    public ExemplaarHerkenning(IProductRepository repo, IDriveRepository drives, WerkplekAgent agent)
    {
        _repo = repo; _drives = drives; _agent = agent;
    }

    /// <param name="Regel">Het herkende exemplaar; null = niet te bepalen (zie Melding).</param>
    /// <param name="Zeker">Het aantal slaves klopt met de driveposities van het type.</param>
    public sealed record Uitkomst(SamenstellingRegel? Regel, string Melding, bool Zeker);

    /// <summary>De bus uitlezen via de werkplek-agent (/drives) en het exemplaar bepalen.</summary>
    public async Task<Uitkomst> HerkenAsync(IJSRuntime js)
    {
        var a = await _agent.VraagAsync(js, "/drives", 60);
        if (a.Status == 409) return new(null, "De agent is met een andere taak bezig; probeer het zo opnieuw.", false);
        if (a.Status == 401) return new(null, "De agent weigert: token klopt niet (Productie:AgentToken).", false);
        if (a.Status != 200) return new(null, a.Status == 0 ? a.Tekst : $"De agent antwoordde {a.Status}.", false);

        var slaves = new List<(int Positie, string Serienummer)>();
        try
        {
            using var doc = JsonDocument.Parse(a.Tekst);
            if (doc.RootElement.TryGetProperty("drives", out var d) && d.ValueKind == JsonValueKind.Array)
                foreach (var x in d.EnumerateArray())
                {
                    var pos = x.TryGetProperty("slave_position", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : slaves.Count;
                    var sn = x.TryGetProperty("serial_number", out var s)
                        ? s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : s.ValueKind == JsonValueKind.Number ? s.GetRawText() : ""
                        : "";
                    slaves.Add((pos, sn));
                }
        }
        catch (Exception ex) { return new(null, "Antwoord van de agent niet te lezen: " + ex.Message, false); }

        if (slaves.Count == 0) return new(null, "Geen EtherCAT-slaves gevonden. Adapter aangesloten en spanning aan?", false);
        return await BepaalAsync(slaves);
    }

    /// <summary>Het exemplaar bij deze slaves (positie, serienummer).</summary>
    public async Task<Uitkomst> BepaalAsync(IReadOnlyList<(int Positie, string Serienummer)> slaves)
    {
        var halven = new List<SamenstellingRegel>();
        foreach (var (_, ruw) in slaves)
        {
            var sn = ruw.Trim();
            if (sn.Length == 0 || sn is "0" or "None") continue;
            SamenstellingRegel? h = null;
            try { h = await _repo.ZoekOpDriveSerienummerAsync(sn); } catch { }
            if (h is not null && halven.All(x => x.ProductId != h.ProductId)) halven.Add(h);
        }
        if (halven.Count == 0)
            return new(null, $"{slaves.Count} slave(s) gevonden, maar geen bekend serienummer: deze print is nog niet eerder uitgelezen.", false);

        // per halffabricaat de keten omhoog (zelf, ouder, ouder van de ouder, ...)
        var ketens = new List<List<SamenstellingRegel>>();
        foreach (var h in halven)
        {
            var keten = new List<SamenstellingRegel> { h };
            var id = h.ProductId;
            for (var i = 0; i < 8; i++)
            {
                SamenstellingRegel? o = null;
                try { o = await _repo.GetGebruiktInAsync(id); } catch { }
                if (o is null || keten.Any(x => x.ProductId == o.ProductId)) break;
                keten.Add(o);
                id = o.ProductId;
            }
            ketens.Add(keten);
        }

        // exemplaren die in elke keten zitten, van laag naar hoog
        var samen = ketens[0].Where(c => ketens.All(k => k.Any(x => x.ProductId == c.ProductId))).ToList();
        if (samen.Count == 0)
            return new(null, "De serienummers horen bij exemplaren die niet in hetzelfde product zitten: " +
                             string.Join(", ", halven.Select(Naam)) + ".", false);

        for (var i = samen.Count - 1; i >= 0; i--)
            if (await AantalPosities(samen[i].ProductNameId) == slaves.Count)
                return new(samen[i], $"Herkend: {Naam(samen[i])}" +
                                     (samen[i].ProductId != halven[0].ProductId || halven.Count > 1
                                         ? $" (via {string.Join(", ", halven.Select(Naam))})" : "") + ".", true);

        return new(samen[0], $"Waarschijnlijk {Naam(samen[0])}: {slaves.Count} slave(s) gevonden, dat past bij geen enkel type in de samenstelling.", false);
    }

    private static string Naam(SamenstellingRegel r) => $"{r.Code} {r.Serienummer}".Trim();

    /// <summary>Driveposities van een type, zonder chipposities (ESP32/STM32 zitten niet op de bus).</summary>
    private async Task<int> AantalPosities(int productNameId)
    {
        try
        {
            _typen ??= await _drives.GetTypenAsync();
            var posities = await _drives.GetPositiesAsync(productNameId);
            return posities.Count(p => !(p.DriveTypeId is int t && _typen.FirstOrDefault(x => x.Id == t) is { } dt && DriveProtocol.IsChip(dt.Protocol)));
        }
        catch { return -1; }
    }
}
