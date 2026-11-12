using System.Text;
using ZXing.QrCode;

namespace EverythingApp.Services;

/// <summary>Een foto die nog niet is opgeslagen: de bytes (JPEG, in de browser verkleind) en een klein voorbeeld als data-URL.</summary>
public sealed record FotoStuk(string Naam, byte[] Inhoud, string Voorbeeld);

/// <summary>
/// Een scherm op de pc dat op foto's van een telefoon wacht (Components/FotoVak.razor). De telefoon vindt de vraag via de
/// QR-code (het Id) of, als dezelfde medewerker op de telefoon is aangemeld, vanzelf (MDE mobiel ▸ Foto).
/// </summary>
public sealed class FotoVraag
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public int? WerknemerId { get; init; }
    public string Vrager { get; init; } = "";
    /// <summary>Waar de foto voor is, bv. "Fout bij MPR-1 · 32".</summary>
    public string Titel { get; init; } = "";
    public DateTime Begin { get; } = DateTime.Now;
    internal Func<FotoStuk, string, Task>? OpFoto { get; init; }
    internal Func<string, Task>? OpVerbonden { get; init; }
}

/// <summary>
/// De telefoon als camera voor een scherm op de pc (wens gebruiker 12-11-2026): de pc opent een vraag, de telefoon
/// (Components/Pages/MobielFoto.razor) levert foto's af en die staan meteen op het pc-scherm. Alles in het geheugen van
/// de server: er wordt hier niets opgeslagen, dat doet het scherm dat de foto's vroeg. Na een herstart zijn open vragen weg.
/// </summary>
public class FotoBrug
{
    private static readonly TimeSpan Houdbaar = TimeSpan.FromHours(4);
    private readonly object _slot = new();
    private readonly Dictionary<string, FotoVraag> _open = new();
    private readonly ILogger<FotoBrug> _log;

    public FotoBrug(ILogger<FotoBrug> log) => _log = log;

    /// <summary>Er is een vraag bijgekomen of gesloten (de telefoonpagina kijkt dan opnieuw).</summary>
    public event Action? Gewijzigd;

    /// <param name="opFoto">Krijgt elke foto en de naam van wie hem maakte; wordt aangeroepen vanaf de verbinding van de telefoon.</param>
    /// <param name="opVerbonden">De telefoon heeft de vraag geopend (naam van wie).</param>
    public FotoVraag Open(int? werknemerId, string vrager, string titel, Func<FotoStuk, string, Task> opFoto, Func<string, Task>? opVerbonden = null)
    {
        var vraag = new FotoVraag { WerknemerId = werknemerId, Vrager = vrager, Titel = titel, OpFoto = opFoto, OpVerbonden = opVerbonden };
        lock (_slot)
        {
            foreach (var oud in _open.Values.Where(v => DateTime.Now - v.Begin > Houdbaar).Select(v => v.Id).ToList()) _open.Remove(oud);
            _open[vraag.Id] = vraag;
        }
        Meld();
        return vraag;
    }

    public void Sluit(string id)
    {
        bool weg;
        lock (_slot) weg = _open.Remove(id);
        if (weg) Meld();
    }

    public FotoVraag? Zoek(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_slot) return _open.TryGetValue(id.Trim(), out var v) ? v : null;
    }

    /// <summary>De nieuwste open vraag van deze medewerker (de telefoon zonder QR-code).</summary>
    public FotoVraag? NieuwsteVan(int werknemerId)
    {
        lock (_slot) return _open.Values.Where(v => v.WerknemerId == werknemerId).OrderByDescending(v => v.Begin).FirstOrDefault();
    }

    /// <summary>De telefoon heeft de vraag in beeld. False = de vraag is al gesloten.</summary>
    public async Task<bool> VerbondenAsync(string id, string wie)
    {
        var vraag = Zoek(id);
        if (vraag is null) return false;
        if (vraag.OpVerbonden is not null) await Veilig(() => vraag.OpVerbonden(wie));
        return true;
    }

    /// <summary>Een foto afleveren bij het scherm dat erom vroeg. False = de vraag is al gesloten.</summary>
    public async Task<bool> LeverAsync(string id, FotoStuk foto, string wie)
    {
        var vraag = Zoek(id);
        if (vraag?.OpFoto is null) return false;
        return await Veilig(() => vraag.OpFoto(foto, wie));
    }

    private void Meld()
    {
        try { Gewijzigd?.Invoke(); }
        catch (Exception ex) { _log.LogInformation("Fotobrug: melden mislukt: {Fout}", ex.Message); }
    }

    private async Task<bool> Veilig(Func<Task> actie)
    {
        try { await actie(); return true; }
        catch (Exception ex) { _log.LogInformation("Fotobrug: scherm niet bereikbaar: {Fout}", ex.Message); return false; }
    }
}

/// <summary>Een QR-code als SVG (zwart op wit, ook in het donkere thema: een camera leest hem zo het best).</summary>
public static class QrSvg
{
    public static string Maak(string tekst)
    {
        try
        {
            var bits = new QRCodeWriter().encode(tekst, ZXing.BarcodeFormat.QR_CODE, 0, 0);
            var pad = new StringBuilder();
            for (var y = 0; y < bits.Height; y++)
                for (var x = 0; x < bits.Width; x++)
                    if (bits[x, y]) pad.Append('M').Append(x).Append(' ').Append(y).Append("h1v1h-1z");
            return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {bits.Width} {bits.Height}\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"QR-code\">" +
                   $"<rect width=\"{bits.Width}\" height=\"{bits.Height}\" fill=\"#fff\"/><path d=\"{pad}\" fill=\"#000\"/></svg>";
        }
        catch { return ""; }
    }
}
