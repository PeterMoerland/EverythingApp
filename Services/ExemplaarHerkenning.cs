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
    /// <param name="toegestaan">Alleen deze producttypen (ProductNameId) mogen de uitkomst zijn, bv. de halffabricaten op de werkplek.</param>
    public async Task<Uitkomst> HerkenAsync(IJSRuntime js, Func<int, bool>? toegestaan = null)
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
        return await BepaalAsync(slaves, toegestaan);
    }

    /// <summary>
    /// Het exemplaar bij deze slaves (positie, serienummer).
    /// Extra slaves die niet bij het product horen (melding Sjoerd 9-11-2026: een swivel als 5e slave aan een MPR-1 met 4
    /// driveposities) mogen het herkennen niet blokkeren: het product is het exemplaar waar de meeste herkende slaves onder
    /// vallen, en het aantal posities mag dan kleiner zijn dan het aantal slaves (de rest wordt in de melding genoemd).
    /// </summary>
    public async Task<Uitkomst> BepaalAsync(IReadOnlyList<(int Positie, string Serienummer)> slaves, Func<int, bool>? toegestaan = null)
    {
        // per slave het halffabricaat waar dat serienummer bij hoort (null = onbekend of leeg)
        var perSn = new Dictionary<string, SamenstellingRegel?>(StringComparer.OrdinalIgnoreCase);
        var perSlave = new List<SamenstellingRegel?>();
        foreach (var (_, ruw) in slaves)
        {
            var sn = ruw.Trim();
            if (sn.Length == 0 || sn is "0" or "None") { perSlave.Add(null); continue; }
            if (!perSn.TryGetValue(sn, out var h))
            {
                try { h = await _repo.ZoekOpDriveSerienummerAsync(sn); } catch { h = null; }
                perSn[sn] = h;
            }
            perSlave.Add(h);
        }
        var halven = perSlave.OfType<SamenstellingRegel>().GroupBy(h => h.ProductId).Select(g => g.First()).ToList();
        if (halven.Count == 0)
            return new(null, $"{slaves.Count} slave(s) gevonden, maar geen bekend serienummer: deze print is nog niet eerder uitgelezen.", false);

        // per halffabricaat de keten omhoog (zelf, ouder, ouder van de ouder, ...)
        var ketens = new Dictionary<int, List<SamenstellingRegel>>();
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
            ketens[h.ProductId] = keten;
        }

        // Kandidaten: elk exemplaar in een keten, met het aantal slaves dat eronder valt. De beste groep = de meeste
        // slaves; daarbinnen van laag naar hoog (plek in de keten).
        bool Onder(SamenstellingRegel? h, int productId) => h is not null && ketens[h.ProductId].Any(x => x.ProductId == productId);
        var kandidaten = ketens.Values.SelectMany(k => k.Select((c, plek) => (c, plek)))
            .GroupBy(x => x.c.ProductId)
            .Select(g => (Regel: g.First().c, Plek: g.Max(x => x.plek), Dekking: perSlave.Count(h => Onder(h, g.Key))))
            .ToList();
        var maxDekking = kandidaten.Max(k => k.Dekking);
        var samen = kandidaten.Where(k => k.Dekking == maxDekking).OrderBy(k => k.Plek).Select(k => k.Regel).ToList();
        var vreemd = halven.Where(h => !Onder(h, samen[0].ProductId)).ToList();     // herkend, maar in een ander product

        // Het exemplaar dat er echt aan hangt, los van wat dit scherm toont: het hoogste met precies zoveel posities als er
        // slaves zijn; anders het hoogste met minder posities dan slaves (maar minstens de herkende), met extra slaves.
        SamenstellingRegel? aanDeBus = null;
        var extra = 0;
        for (var i = samen.Count - 1; i >= 0 && aanDeBus is null; i--)
            if (await AantalPosities(samen[i].ProductNameId) == slaves.Count) aanDeBus = samen[i];
        for (var i = samen.Count - 1; i >= 0 && aanDeBus is null; i--)
        {
            var p = await AantalPosities(samen[i].ProductNameId);
            if (p > 0 && p < slaves.Count && p >= maxDekking) { aanDeBus = samen[i]; extra = slaves.Count - p; }
        }
        var extraTekst = extra == 0 ? ""
            : $" Er {(extra == 1 ? "hangt" : "hangen")} {extra} slave{(extra == 1 ? "" : "s")} meer aan de bus dan {Naam(aanDeBus!)} driveposities heeft" +
              (vreemd.Count > 0 ? $" ({string.Join(", ", vreemd.Select(Naam))})" : " (bv. een swivel)") + "; die tellen niet mee.";

        if (toegestaan is not null)
        {
            var mag = samen.Where(c => toegestaan(c.ProductNameId)).ToList();
            if (mag.Count == 0)
                return new(null, $"Aan de bus hangt {Naam(aanDeBus ?? samen[^1])}; dat hoort niet op dit scherm.", false);
            if (aanDeBus is null || !toegestaan(aanDeBus.ProductNameId))
                return new(mag[^1], $"Herkend: {Naam(mag[^1])}" +
                                    (aanDeBus is not null ? $" (zit in {Naam(aanDeBus)}, dat hangt aan de bus)." : "."), aanDeBus is not null);
        }

        if (aanDeBus is not null)
        {
            var via = halven.Where(h => Onder(h, aanDeBus.ProductId)).ToList();
            return new(aanDeBus, $"Herkend: {Naam(aanDeBus)}" +
                                 (via.Count > 1 || via.Count == 1 && via[0].ProductId != aanDeBus.ProductId
                                     ? $" (via {string.Join(", ", via.Select(Naam))})" : "") + "." + extraTekst, true);
        }

        return new(samen[0], $"Waarschijnlijk {Naam(samen[0])}: {slaves.Count} slave(s) gevonden, dat past bij geen enkel type in de samenstelling" +
                             (vreemd.Count > 0 ? $"; ook herkend in een ander product: {string.Join(", ", vreemd.Select(Naam))}" : "") + ".", false);
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
