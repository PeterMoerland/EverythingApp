using System.Globalization;
using System.Text;
using EverythingApp.Models;
using static System.Net.WebUtility;

namespace EverythingApp.Services;

/// <summary>
/// De pakbon als PDF (Access: rapporten Pakbon en PakbonPlaatjes) en het verzendlabel op A5 (rapport Verzendlabel).
/// Zelfde opmaak en route als de factuur: HTML, door Edge headless naar PDF (FactuurPdfService.NaarPdfAsync).
/// Afzender: de eigen BV uit Pakbonnen:Afzender (standaard 5254, MDE Automation B.V.), met logo en regels uit
/// Templates/Factuur. Datum op de bon = vandaag, zoals in Access.
/// </summary>
public class PakbonPdfService
{
    private readonly IPakbonRepository _repo;
    private readonly FactuurPdfService _factuurPdf;
    private readonly LabelService _labels;
    private readonly IConfiguration _config;

    public PakbonPdfService(IPakbonRepository repo, FactuurPdfService factuurPdf, LabelService labels, IConfiguration config)
    {
        _repo = repo;
        _factuurPdf = factuurPdf;
        _labels = labels;
        _config = config;
    }

    private static readonly CultureInfo Nl = new("nl-NL");
    private static string H(string? t) => HtmlEncode(t ?? "");
    private static string Getal(decimal w) => w.ToString("#,0.###", Nl);

    private async Task<FactuurAfzender> AfzenderAsync()
    {
        var bv = _config.GetValue("Pakbonnen:Afzender", 5254);
        var a = await _repo.AfzenderAsync(bv) ?? new FactuurAfzender { Id = bv, Naam = "MDE Automation B.V." };
        _factuurPdf.VulAfzender(a);
        return a;
    }

    /// <summary>De pakbon als PDF; met plaatjes = het artikelplaatje bij elke regel (Access: PakbonPlaatjes).</summary>
    public async Task<byte[]> PakbonAsync(int pakbonId, bool metPlaatjes, CancellationToken stop = default)
    {
        var p = await _repo.AfdrukAsync(pakbonId) ?? throw new InvalidOperationException("Deze pakbon bestaat niet meer.");
        var a = await AfzenderAsync();
        var fotos = new Dictionary<int, string>();
        if (metPlaatjes)
            foreach (var id in p.Regels.Where(r => r.MdeId is not null).Select(r => r.MdeId!.Value).Distinct())
                if (await _labels.ArtikelFotoAsync(id) is { } foto)
                    fotos[id] = $"data:{(foto.Length > 3 && foto[0] == 0x89 && foto[1] == 0x50 ? "image/png" : "image/jpeg")};base64,{Convert.ToBase64String(foto)}";
        return await _factuurPdf.NaarPdfAsync(PakbonHtml(p, a, metPlaatjes, fotos), stop);
    }

    public string PakbonHtml(PakbonAfdruk p, FactuurAfzender a, bool metPlaatjes, IReadOnlyDictionary<int, string> fotos)
    {
        var sb = new StringBuilder();
        var voet = string.Join("  ·  ", new[] { a.Naam, a.Kvk.Length > 0 ? "KvK " + a.Kvk : "", a.Btw.Length > 0 ? "btw " + a.Btw : "" }
                                         .Where(s => s.Length > 0));
        FactuurPdfService.Begin(sb, a, "Pakbon PS-" + p.Id, liggend: false, voet);

        // ---- kop: logo en afzender ----
        sb.Append("<header class='kop'>");
        sb.Append(a.LogoDataUri.Length > 0 ? $"<img class='logo' src='{a.LogoDataUri}' alt=''>" : $"<div class='logo-tekst'>{H(a.Naam)}</div>");
        sb.Append("<div class='afzender'>");
        sb.Append($"<div class='afz-naam'>{H(a.Naam)}</div>");
        sb.Append($"<div>{H(a.Adres)}</div><div>{H(FactuurPdfService.Plaats(a.Postcode, a.Plaats))}</div>");
        foreach (var r in a.Regels) sb.Append($"<div>{H(r)}</div>");
        sb.Append("</div></header>");

        // ---- adres en gegevens ----
        sb.Append("<section class='adresrij'><div class='adres'>");
        sb.Append($"<div class='klantnaam'>{H(p.Klant)}</div>");
        if (p.Tav.Length > 0) sb.Append($"<div>t.a.v. {H(p.Tav)}</div>");
        sb.Append($"<div>{H(p.Adres)}</div><div>{H(FactuurPdfService.Plaats(p.Postcode, p.Plaats.ToUpperInvariant()))}</div>");
        if (p.Landnummer != 31 && p.Land.Length > 0) sb.Append($"<div>{H(p.Land.ToUpperInvariant())}</div>");
        sb.Append("</div><table class='meta'>");
        FactuurPdfService.Meta(sb, "Pakbon", "PS-" + p.Id, vet: true);
        FactuurPdfService.Meta(sb, "Datum", DateTime.Today.ToString("dd-MM-yyyy"));
        var refs = p.Referenties;
        FactuurPdfService.Meta(sb, refs.Contains(',') ? "Referenties" : "Referentie", refs);
        sb.Append("</table></section>");

        sb.Append("<div class='titelrij'><h1>Pakbon</h1></div>");

        // ---- regels ----
        var metRef = p.Regels.Any(r => r.ReferentieKlant.Trim().Length > 0);
        var metKg = p.Regels.Any(r => r.Gewicht != 0);
        sb.Append("<table class='regels'><thead><tr>");
        if (metPlaatjes) sb.Append("<th style='width:22mm'></th>");
        sb.Append("<th class='r'>Aantal</th><th>Artikelnummer</th><th>Omschrijving</th>");
        if (metRef) sb.Append("<th>Referentie</th>");
        sb.Append(metKg ? "<th class='r'>kg</th></tr></thead><tbody>" : "</tr></thead><tbody>");
        foreach (var r in p.Regels)
        {
            sb.Append("<tr>");
            if (metPlaatjes)
                sb.Append(r.MdeId is int id && fotos.TryGetValue(id, out var foto)
                    ? $"<td><img src='{foto}' alt='' style='max-width:20mm;max-height:16mm;object-fit:contain'></td>"
                    : "<td></td>");
            sb.Append($"<td class='r'>{Getal(r.Aantal)}</td><td class='nb'>{H(r.Artikelnummer)}</td>");
            sb.Append($"<td>{H(r.Omschrijving).Replace("\n", "<br>")}</td>");
            if (metRef) sb.Append($"<td>{H(r.ReferentieKlant)}</td>");
            sb.Append(metKg ? $"<td class='r'>{(r.Gewicht == 0 ? "" : Getal(r.Gewicht * r.Aantal))}</td></tr>" : "</tr>");
        }
        sb.Append("</tbody></table>");

        var totaal = p.Regels.Sum(r => r.Gewicht * r.Aantal);
        if (totaal > 0)
            sb.Append($"<table class='totalen'><tr class='eind'><td>Totaal (kg)</td><td class='r'>{Getal(totaal)}</td></tr></table>");
        if (p.MeldingOpBon.Trim().Length > 0)
            sb.Append($"<div class='betalen'><b>Opmerking:</b> {H(p.MeldingOpBon).Replace("\n", "<br>")}</div>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Het verzendlabel op A5 liggend: groot het adres, klein de afzender (Access: rapport Verzendlabel).</summary>
    public async Task<byte[]> VerzendlabelAsync(int pakbonId, CancellationToken stop = default)
    {
        var p = await _repo.AfdrukAsync(pakbonId) ?? throw new InvalidOperationException("Deze pakbon bestaat niet meer.");
        var a = await AfzenderAsync();
        return await _factuurPdf.NaarPdfAsync(VerzendlabelHtml(p, a), stop);
    }

    public static IEnumerable<string> AdresRegels(PakbonAfdruk p)
    {
        yield return p.Klant;
        if (p.Tav.Length > 0) yield return "t.a.v. " + p.Tav;
        if (p.Adres.Length > 0) yield return p.Adres;
        yield return FactuurPdfService.Plaats(p.Postcode, p.Plaats.ToUpperInvariant());
        if (p.Landnummer != 31 && p.Land.Length > 0) yield return p.Land.ToUpperInvariant();
    }

    public string VerzendlabelHtml(PakbonAfdruk p, FactuurAfzender a)
    {
        var afzender = string.Join(" - ", new[] { a.Naam, a.Adres, a.Postcode, a.Plaats, "Nederland" }.Where(s => s.Trim().Length > 0));
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>Verzendlabel PS-").Append(p.Id).Append("</title><style>");
        sb.Append("""
            @page { size: A5 landscape; margin: 12mm; }
            * { box-sizing: border-box; }
            body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; color: #111; }
            .afz { font-size: 9pt; color: #555; border-bottom: 1px solid #999; padding-bottom: 2mm; }
            .aan { margin-top: 18mm; margin-left: 40mm; font-size: 18pt; line-height: 1.35; }
            .aan div:first-child { font-weight: 700; }
            .ps { position: fixed; right: 0; bottom: 0; font-size: 9pt; color: #777; }
            """);
        sb.Append("</style></head><body>");
        sb.Append($"<div class='afz'>Afzender: {H(afzender)}</div><div class='aan'>");
        foreach (var r in AdresRegels(p).Where(r => r.Trim().Length > 0)) sb.Append($"<div>{H(r)}</div>");
        sb.Append($"</div><div class='ps'>PS-{p.Id}</div></body></html>");
        return sb.ToString();
    }
}
