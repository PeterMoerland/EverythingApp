using System.Globalization;
using System.Text;
using EverythingApp.Models;
using static System.Net.WebUtility;

namespace EverythingApp.Services;

/// <summary>
/// De offerte als PDF: Access-rapport OfferteUitgaand (met de subrapporten Uren, Materiaal, Overigen, Voorwaarden en
/// Signature), in de opmaak van de factuur-PDF. HTML → PDF via Edge (<see cref="FactuurPdfService.NaarPdfAsync"/>);
/// logo, kleur en extra regels per BV uit Templates/Factuur (bedrijven.json, logos).
///
/// Instellingen (appsettings, sectie Offertes): Map (PDF-map, standaard \\172.16.1.4\data\Offertes),
/// Netwerkschijf (vervangt Z: in paden uit Access) en Handtekeningen (map met &lt;werknemer-id&gt;.png).
/// </summary>
public class OffertePdfService
{
    private readonly IOfferteRepository _repo;
    private readonly FactuurPdfService _factuur;
    private readonly IConfiguration _config;
    private readonly ILogger<OffertePdfService> _log;

    public OffertePdfService(IOfferteRepository repo, FactuurPdfService factuur, IConfiguration config, ILogger<OffertePdfService> log)
    {
        _repo = repo;
        _factuur = factuur;
        _config = config;
        _log = log;
    }

    private string Netwerkschijf => Instelling("Netwerkschijf", @"\\172.16.1.4\data");

    /// <summary>De map voor offerte-PDF's (Access: GetDirectory("Offertes") = &lt;Netwerkfolder&gt;\Offertes).</summary>
    public string Map => Instelling("Map", Path.Combine(Netwerkschijf, "Offertes"));

    private string HandtekeningMap => Instelling("Handtekeningen", Path.Combine(Netwerkschijf, "General", "Handtekeningen"));

    private string Instelling(string naam, string standaard)
        => _config[$"Offertes:{naam}"] is { } w && !string.IsNullOrWhiteSpace(w) ? w : standaard;

    /// <summary>Een pad uit Access (Z:\General\...) bruikbaar maken voor de dienst op de server.</summary>
    public string Pad(string accessPad)
        => accessPad.StartsWith("Z:", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Netwerkschijf, accessPad[2..].TrimStart('\\', '/'))
            : accessPad;

    public async Task<OffertePdfGegevens> LaadAsync(int offerteId, int? ondertekenaarId)
    {
        var g = await _repo.PdfGegevensAsync(offerteId, ondertekenaarId)
                ?? throw new InvalidOperationException("Deze offerte bestaat niet meer.");
        if (g.Afzender.Id != 0) _factuur.VulAfzender(g.Afzender);
        return g;
    }

    public static string Bestandsnaam(Offerte o) => $"{o.OfferteNr}.PDF";

    public Task<byte[]> NaarPdfAsync(string html, CancellationToken stop = default) => _factuur.NaarPdfAsync(html, stop);

    // ============================================================
    //  De offerte
    // ============================================================

    public string Html(OffertePdfGegevens g, DateOnly datum)
    {
        var o = g.Offerte;
        var t = new Teksten(o.Taal == OfferteTaal.Engels);
        var a = g.Afzender;
        var k = g.Klant;
        var kleur = System.Text.RegularExpressions.Regex.IsMatch(a.Kleur, "^#[0-9A-Fa-f]{6}$") ? a.Kleur : "#ED1C24";
        var sb = new StringBuilder();

        Begin(sb, kleur, $"{t.Offerte} {o.OfferteNr}", $"{o.OfferteNr}  ·  {t.Datum(datum)}  ·  {VoetBv(a)}");

        // ---- kop: logo en afzender (Access: logo per BV links, adresrapport rechts) ----
        sb.Append("<header class='kop'>");
        sb.Append(a.LogoDataUri.Length > 0 ? $"<img class='logo' src='{a.LogoDataUri}' alt=''>" : $"<div class='logo-tekst'>{H(a.Naam)}</div>");
        sb.Append("<div class='afzender'>");
        sb.Append($"<div class='afz-naam'>{H(a.Naam)}</div>");
        sb.Append($"<div>{H(a.Adres)}</div><div>{H($"{a.Postcode}  {a.Plaats}".Trim())}</div>");
        foreach (var r in a.Regels) sb.Append($"<div>{H(r)}</div>");
        if (a.Email.Length > 0 && !a.Regels.Any(r => r.Contains('@'))) sb.Append($"<div>{H(a.Email)}</div>");
        sb.Append("</div></header>");

        // ---- adres, titel, nummer en datum ----
        sb.Append("<section class='adresrij'><div class='adres'>");
        sb.Append($"<div class='klantnaam'>{H(k.Naam)}</div>");
        if (k.Tav.Length > 0) sb.Append($"<div>{t.Tav} {H(k.Tav)}</div>");
        sb.Append($"<div>{H(k.Adres)}</div><div>{H($"{k.Postcode}  {k.Plaats.ToUpperInvariant()}".Trim())}</div>");
        if (k.Landnummer != 31 && k.Land.Length > 0) sb.Append($"<div>{H(k.Land.ToUpperInvariant())}</div>");
        sb.Append("</div><table class='meta'>");
        sb.Append($"<tr><td>{t.Offertenummer}</td><td><b>{H(o.OfferteNr)}</b></td></tr>");
        sb.Append($"<tr><td>{t.DatumLabel}</td><td>{t.Datum(datum)}</td></tr>");
        sb.Append("</table></section>");

        sb.Append($"<div class='titelrij'><h1>{t.Offerte.ToUpperInvariant()}</h1></div>");
        if (o.Omschrijving.Length > 0) sb.Append($"<div class='onderwerp'>{H(o.Omschrijving)}</div>");
        if (o.OfferteTekst.Trim().Length > 0) sb.Append($"<div class='koptekst'>{Alinea(o.OfferteTekst)}</div>");

        // ---- secties (een sectie zonder regels valt weg, zoals een leeg subrapport) ----
        var cats = g.Regels.Categorieen.ToDictionary(c => c.Id, c => c.Naam);
        string Cat(int? id) => id is int i && cats.TryGetValue(i, out var n) ? n : "";
        var tarieven = g.Regels.Tarieven.ToDictionary(x => x.Id, x => x.Tarief);

        // Uren: alleen regels met een tarief (Access: INNER JOIN op tarieven)
        var uren = g.Regels.Uren.Where(u => u.TariefId is int ti && tarieven.ContainsKey(ti)).ToList();
        if (uren.Count > 0)
        {
            Sectie(sb, t.Uren, new[] { (t.UrenKop, "c w-aantal"), (t.Omschrijving, ""), (t.Tarief, "r w-geld"), (t.Totaal, "r w-geld") },
                uren.GroupBy(u => Cat(u.CategorieId)).OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase),
                u => new[] { t.Getal(u.Uren), Alinea(u.Omschrijving), t.Geld(tarieven[u.TariefId!.Value]), t.Geld(u.Uren * tarieven[u.TariefId!.Value]) },
                t.TotaalUren, uren.Sum(u => u.Uren * tarieven[u.TariefId!.Value]), t);
        }

        if (g.Regels.Materiaal.Count > 0)
        {
            // Access verborg de kolom Korting als geen enkele regel korting heeft
            var metKorting = g.Regels.Materiaal.Any(m => m.Korting != 0);
            var koppen = new List<(string, string)> { (t.Aantal, "c w-aantal"), (t.Artikelnummer, "w-art"), (t.Omschrijving, ""), (t.Prijs, "r w-geld") };
            if (metKorting) koppen.Add((t.Korting, "r w-korting"));
            koppen.Add((t.Totaal, "r w-geld"));
            Sectie(sb, t.Materialen, koppen.ToArray(),
                g.Regels.Materiaal.GroupBy(m => Cat(m.CategorieId)).OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase),
                m =>
                {
                    var cellen = new List<string> { t.Getal(m.Aantal), H(m.ArtikelNr), Alinea(m.Omschrijving), t.Geld(m.Prijs) };
                    if (metKorting) cellen.Add(m.Korting != 0 ? (m.Korting * 100).ToString("0.0", t.Cultuur) + "%" : "");
                    cellen.Add(t.Geld(m.Totaal));
                    return cellen.ToArray();
                },
                t.TotaalMateriaal, g.Regels.Materiaal.Sum(m => m.Totaal), t);
        }

        if (g.Regels.Overigen.Count > 0)
        {
            Sectie(sb, t.Overigen, new[] { (t.Aantal, "c w-aantal"), (t.Omschrijving, ""), (t.Prijs, "r w-geld"), (t.Totaal, "r w-geld") },
                g.Regels.Overigen.GroupBy(v => Cat(v.CategorieId)).OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase),
                v => new[] { t.Getal(v.Aantal), Alinea(v.Omschrijving), t.Geld(v.Prijs), t.Geld(v.Totaal) },
                t.TotaalOverigen, g.Regels.Overigen.Sum(v => v.Totaal), t);
        }

        // ---- voorwaarden (Access: Prijs = offertetotaal, Levertijd, Geldigheid, Betalingstermijn, Betaling) ----
        sb.Append("<table class='voorwaarden'>");
        sb.Append($"<tr class='prijs'><td>{t.PrijsLabel}</td><td>{t.Geld(o.Totalen.Totaal)}</td></tr>");
        Voorwaarde(sb, t.Levertijd, o.Levertijd);
        Voorwaarde(sb, t.Geldigheid, o.Geldigheid);
        Voorwaarde(sb, t.Betalingstermijn, o.Betalingstermijn);
        Voorwaarde(sb, t.Betaling, o.Betaling);
        sb.Append("</table>");

        if (o.Voettekst.Trim().Length > 0) sb.Append($"<div class='voettekst'>{Alinea(o.Voettekst)}</div>");

        // ---- groet en ondertekenaar ----
        var w = g.Ondertekenaar;
        sb.Append("<div class='groet'>");
        sb.Append($"<div>{t.Groet}</div><div class='bv'>{H(a.Naam)}</div>");
        if (w.Naam.Length > 0) sb.Append($"<div>{H(w.Naam)}</div>");
        if (w.Functie.Length > 0) sb.Append($"<div class='klein'>{H(w.Functie)}</div>");
        if (w.Mail.Length > 0) sb.Append($"<div class='klein'>E. {H(w.Mail)}</div>");
        if (w.Mobiel.Length > 0) sb.Append($"<div class='klein'>T. {H(w.Mobiel)}</div>");
        sb.Append("</div>");

        // ---- handtekeningblok (Access: alleen met contactpersoon; handtekening van de ondertekenaar) ----
        if (o.Ondertekening)
        {
            sb.Append("<section class='akkoord'>");
            sb.Append($"<p>{t.Akkoord}</p><div class='tekenen'>");
            sb.Append($"<div class='vak'><div class='streep'></div><div>{H(k.Tav)}</div><div class='klein'>{H(k.Naam)}</div></div>");
            var handtekening = Handtekening(w.Id);
            sb.Append("<div class='vak'>");
            sb.Append(handtekening.Length > 0 ? $"<img class='handtekening' src='{handtekening}' alt=''>" : "");
            sb.Append($"<div class='streep'></div><div>{H(w.Naam)}</div><div class='klein'>{H(a.Naam)}</div></div>");
            sb.Append("</div></section>");
        }

        // ---- disclaimer: in Access onderaan de laatste pagina ----
        if (o.Disclaimer.Trim().Length > 0) sb.Append($"<div class='disclaimer'>{Alinea(o.Disclaimer)}</div>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void Sectie<T>(StringBuilder sb, string titel, (string Kop, string Klasse)[] koppen,
                                  IEnumerable<IGrouping<string, T>> groepen, Func<T, string[]> cellen,
                                  string totaalLabel, decimal totaal, Teksten t)
    {
        sb.Append($"<h2>{H(titel)}</h2><table class='regels'><colgroup>");
        foreach (var (_, klasse) in koppen) sb.Append($"<col class='{klasse}'>");
        sb.Append("</colgroup>");
        foreach (var groep in groepen)
        {
            // Access: categorienaam met de kolomkoppen op dezelfde regel, per categorie herhaald
            sb.Append("<tr class='kopjes'>");
            foreach (var (kop, klasse) in koppen) sb.Append($"<th class='{klasse}'>{H(kop)}</th>");
            sb.Append("</tr>");
            if (groep.Key.Length > 0) sb.Append($"<tr class='groep'><td colspan='{koppen.Length}'>{H(groep.Key)}</td></tr>");
            foreach (var regel in groep)
            {
                sb.Append("<tr>");
                var c = cellen(regel);
                for (var i = 0; i < c.Length; i++) sb.Append($"<td class='{koppen[i].Klasse}'>{c[i]}</td>");
                sb.Append("</tr>");
            }
        }
        sb.Append($"<tr class='sectietotaal'><td colspan='{koppen.Length - 1}' class='r'>{H(totaalLabel)}</td><td class='r'>{t.Geld(totaal)}</td></tr>");
        sb.Append("</table>");
    }

    private static void Voorwaarde(StringBuilder sb, string label, string waarde)
    {
        if (waarde.Trim().Length == 0) return;
        sb.Append($"<tr><td>{H(label)}</td><td>{H(waarde)}</td></tr>");
    }

    /// <summary>Handtekening als data-URI uit de map Handtekeningen (&lt;id&gt;.png); leeg als die er niet is.</summary>
    private string Handtekening(int? werknemerId)
    {
        if (werknemerId is not int id) return "";
        try
        {
            foreach (var ext in new[] { "PNG", "png", "jpg" })
            {
                var pad = Path.Combine(HandtekeningMap, $"{id}.{ext}");
                if (!File.Exists(pad)) continue;
                var mime = ext.Equals("jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png";
                return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(pad))}";
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Handtekening van {Id} niet te lezen", id); }
        return "";
    }

    private static string VoetBv(FactuurAfzender a)
    {
        var delen = new List<string> { a.Naam };
        if (a.Kvk.Length > 0) delen.Add($"KvK {a.Kvk}");
        if (a.Btw.Length > 0) delen.Add($"VAT {a.Btw}");
        return string.Join("  ·  ", delen.Where(d => d.Length > 0));
    }

    private static string Alinea(string tekst) => H(tekst.TrimEnd()).Replace("\r\n", "\n").Replace("\n", "<br>");
    private static string H(string? tekst) => HtmlEncode(tekst ?? "");

    private static void Begin(StringBuilder sb, string kleur, string titel, string voet)
    {
        sb.Append("<!doctype html><html><head><meta charset='utf-8'>");
        sb.Append($"<title>{H(titel)}</title><style>");
        var voetCss = voet.Replace("\\", "\\\\").Replace("'", "\\'");
        sb.Append("@page { size: A4 portrait; margin: 14mm 15mm 20mm 15mm; ");
        sb.Append($"@bottom-left {{ content: '{voetCss}'; font: 7.5pt 'Segoe UI', Arial, sans-serif; color: #888; " +
                  $"border-top: 1px solid {kleur}; vertical-align: top; padding-top: 2mm; width: 85%; }} ");
        sb.Append($"@bottom-right {{ content: counter(page) ' / ' counter(pages); font: 7.5pt 'Segoe UI', Arial, sans-serif; " +
                  $"color: #888; border-top: 1px solid {kleur}; vertical-align: top; padding-top: 2mm; }} }}");
        sb.Append($":root {{ --kleur: {kleur}; }}");
        sb.Append("""
            * { box-sizing: border-box; }
            body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 9.5pt; color: #222; margin: 0; }
            .kop { display: flex; justify-content: space-between; align-items: flex-start; gap: 10mm; }
            .logo { max-height: 20mm; max-width: 72mm; object-fit: contain; }
            .logo-tekst { font-size: 16pt; font-weight: 700; color: var(--kleur); }
            .afzender { font-size: 8pt; line-height: 1.45; color: #555; text-align: right; }
            .afz-naam { font-weight: 600; color: var(--kleur); font-size: 8.5pt; }
            .adresrij { display: flex; justify-content: space-between; align-items: flex-start; margin-top: 14mm; min-height: 28mm; }
            .adres { font-size: 10.5pt; line-height: 1.4; padding-top: 2mm; }
            .klantnaam { font-weight: 600; }
            table { border-collapse: collapse; }
            .meta td { padding: .6mm 0 .6mm 6mm; font-size: 9pt; vertical-align: top; }
            .meta td:first-child { color: #777; padding-left: 0; }
            .titelrij { margin-top: 6mm; border-bottom: 2px solid var(--kleur); padding-bottom: 1.5mm; }
            h1 { font-size: 20pt; font-weight: 600; margin: 0; letter-spacing: .5px; }
            .onderwerp { margin-top: 2mm; font-size: 11pt; font-weight: 600; color: #333; }
            .koptekst { margin-top: 4mm; line-height: 1.45; }
            h2 { font-size: 10pt; margin: 7mm 0 0; padding: 1.4mm 2mm; background: #e6e6e6; text-align: center;
                 page-break-after: avoid; }
            .regels { width: 100%; table-layout: fixed; }
            .regels col.w-aantal { width: 13mm; } .regels col.w-art { width: 32mm; }
            .regels col.w-geld { width: 25mm; } .regels col.w-korting { width: 17mm; }
            .regels th { text-align: left; font-size: 7.5pt; font-weight: 600; color: #666; text-transform: uppercase;
                         letter-spacing: .3px; border-bottom: 1px solid #bbb; padding: 2.5mm 1.5mm 1mm; }
            .regels th.r, .regels td.r { text-align: right; } .regels th.c, .regels td.c { text-align: center; }
            .regels td { padding: 1.3mm 1.5mm; border-bottom: 1px solid #eee; vertical-align: top; overflow-wrap: anywhere; }
            .regels tr { page-break-inside: avoid; }
            .regels .groep td { font-weight: 700; color: var(--kleur); border-bottom: none; padding-top: 1.5mm; }
            .regels .sectietotaal td { font-weight: 700; border-top: 1px solid #999; border-bottom: none; padding-top: 1.8mm; }
            .r { text-align: right; white-space: nowrap; }
            .klein { font-size: 8pt; color: #666; }
            .voorwaarden { margin-top: 8mm; page-break-inside: avoid; }
            .voorwaarden td { padding: 1mm 0; vertical-align: top; }
            .voorwaarden td:first-child { width: 48mm; font-weight: 600; }
            .voorwaarden tr.prijs td { font-size: 11pt; font-weight: 700; border-bottom: 2px solid var(--kleur); padding-bottom: 1.5mm; }
            .voettekst { margin-top: 6mm; line-height: 1.45; }
            .groet { margin-top: 8mm; line-height: 1.45; page-break-inside: avoid; }
            .groet .bv { font-weight: 600; margin-top: 1mm; }
            .akkoord { margin-top: 10mm; padding-top: 3mm; border-top: 1px solid #bbb; page-break-inside: avoid; }
            .tekenen { display: flex; justify-content: space-between; gap: 20mm; margin-top: 6mm; }
            .tekenen .vak { flex: 1; display: flex; flex-direction: column; justify-content: flex-end; min-height: 38mm; }
            .tekenen .streep { border-top: 1px solid #333; margin: 2mm 0 1mm; }
            .handtekening { max-height: 26mm; max-width: 60mm; object-fit: contain; }
            .disclaimer { margin-top: 10mm; font-size: 8pt; color: #666; text-align: center; line-height: 1.4; page-break-inside: avoid; }
            """);
        sb.Append("</style></head><body>");
    }

    // ============================================================
    //  Teksten (Nederlands / Engels)
    // ============================================================

    private sealed class Teksten
    {
        private readonly bool _en;
        public CultureInfo Cultuur { get; }

        public Teksten(bool engels)
        {
            _en = engels;
            Cultuur = new CultureInfo(engels ? "en-GB" : "nl-NL");
        }

        private string T(string nl, string en) => _en ? en : nl;

        public string Geld(decimal w) => "€ " + w.ToString("#,##0.00", Cultuur);
        public string Getal(decimal w) => w.ToString("#,0.##", Cultuur);
        public string Datum(DateOnly d) => d.ToString("dd-MM-yyyy");

        public string Offerte => T("Offerte", "Quotation");
        public string Offertenummer => T("Offertenummer", "Quotation number");
        public string DatumLabel => T("Datum", "Date");
        public string Tav => T("t.a.v.", "Attn.");
        public string Uren => T("Uren", "Hours");
        public string Materialen => T("Materialen", "Materials");
        public string Overigen => T("Overigen", "Miscellaneous");
        public string UrenKop => T("Uren", "Hours");
        public string Aantal => T("Aantal", "Qty");
        public string Artikelnummer => T("Artikelnummer", "Part number");
        public string Omschrijving => T("Omschrijving", "Description");
        public string Tarief => T("Tarief", "Rate");
        public string Prijs => T("Prijs", "Price");
        public string Korting => T("Korting", "Discount");
        public string Totaal => T("Totaal", "Total");
        public string TotaalUren => T("Totaal uren", "Total hours");
        public string TotaalMateriaal => T("Totaal materiaal", "Total materials");
        public string TotaalOverigen => T("Totaal overigen", "Total miscellaneous");
        public string PrijsLabel => T("Prijs", "Price");
        public string Levertijd => T("Levertijd", "Delivery");
        public string Geldigheid => T("Geldigheid offerte", "Validity of quotation");
        public string Betalingstermijn => T("Betalingstermijn", "Payment terms");
        public string Betaling => T("Betaling", "Payment");
        public string Groet => T("Met vriendelijke groet,", "Best regards,");
        public string Akkoord => T("Indien u akkoord gaat met deze offerte ontvangen wij graag 1 getekend exemplaar retour.",
                                   "If you agree with this quotation, please return one signed copy.");
    }
}
