using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using EverythingApp.Models;
using static System.Net.WebUtility;

namespace EverythingApp.Services;

/// <summary>
/// Maakt de factuur-PDF en de urenbijlage: Access' rapporten Invoice_General en
/// Invoice_Bijlage, in een nieuwe opmaak. De pagina's worden als HTML opgebouwd en door
/// Microsoft Edge (headless) als PDF afgedrukt; Edge staat op elke Windows-machine.
///
/// Instellingen: Facturen:EdgePad (anders de standaardplek van Edge).
/// Logo's en extra regels per BV: Templates/Factuur/logos en bedrijven.json.
/// </summary>
public class FactuurPdfService
{
    private readonly IVerkoopFactuurRepository _repo;
    private readonly IConfiguration _config;
    private readonly ILogger<FactuurPdfService> _log;
    private readonly string _sjablonen;

    public FactuurPdfService(IVerkoopFactuurRepository repo, IConfiguration config, ILogger<FactuurPdfService> log)
    {
        _repo = repo;
        _config = config;
        _log = log;
        // Zoals het NDA-sjabloon: de map Templates naast de app (gaat mee bij publiceren).
        _sjablonen = Path.Combine(AppContext.BaseDirectory, "Templates", "Factuur");
    }

    // ============================================================
    //  Gegevens
    // ============================================================

    public async Task<FactuurPdfGegevens> LaadAsync(int factuurId)
    {
        var g = await _repo.GetPdfGegevensAsync(factuurId)
                ?? throw new InvalidOperationException("Deze factuur bestaat niet meer.");
        VulAfzender(g.Afzender);
        return g;
    }

    /// <summary>Aanvullingen uit bedrijven.json en het logo.</summary>
    private void VulAfzender(FactuurAfzender a)
    {
        try
        {
            var pad = Path.Combine(_sjablonen, "bedrijven.json");
            if (File.Exists(pad))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pad));
                if (doc.RootElement.TryGetProperty(a.Id.ToString(), out var bv))
                {
                    string? Tekst(string naam) => bv.TryGetProperty(naam, out var w) ? w.GetString() : null;
                    a.Kleur = Tekst("kleur") ?? a.Kleur;
                    if (Tekst("adres") is { Length: > 0 } adres) a.Adres = adres;
                    if (a.Kvk.Length == 0) a.Kvk = Tekst("kvk") ?? "";
                    if (a.Btw.Length == 0) a.Btw = Tekst("btw") ?? "";
                    if (bv.TryGetProperty("regels", out var regels))
                        a.Regels = regels.EnumerateArray().Select(r => r.GetString() ?? "").Where(r => r.Length > 0).ToList();
                }
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "bedrijven.json niet te lezen"); }

        foreach (var ext in new[] { "png", "jpg" })
        {
            var logo = Path.Combine(_sjablonen, "logos", $"{a.Id}.{ext}");
            if (!File.Exists(logo)) continue;
            var mime = ext == "png" ? "image/png" : "image/jpeg";
            a.LogoDataUri = $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(logo))}";
            break;
        }
    }

    // ============================================================
    //  Namen, zoals Access ze gaf
    // ============================================================

    public static string Bestandsnaam(VerkoopFactuur f, bool bijlage)
    {
        var soort = bijlage ? "Attach" : f.BedragExBtw < 0 ? "Credit" : "Invoice";
        var toevoeging = f.Herinnering switch
        {
            1 => " 1e herinnering",
            2 => " 2e herinnering",
            3 => " 3e herinnering",
            4 => bijlage ? " Proforma" : "",
            _ => "",
        };
        if (f.Herinnering == 4 && !bijlage) soort = "Proforma";
        return $"{f.Factuurnummer}_{soort}{toevoeging}.PDF";
    }

    // ============================================================
    //  De factuur
    // ============================================================

    public string FactuurHtml(FactuurPdfGegevens g)
    {
        var f = g.Factuur;
        var en = f.Engels;
        var t = new Teksten(en);
        var a = g.Afzender;
        var k = g.Klant;
        var datum = f.Factuurdatum ?? DateOnly.FromDateTime(DateTime.Today);
        var termijn = f.Betalingstermijn > 0 ? f.Betalingstermijn : 30;
        var credit = f.BedragExBtw < 0;
        var metKorting = f.Regels.Any(r => r.Korting is > 0);
        var metPrijs = f.Regels.Any(r => !r.IsUren && r.Stuksprijs is not null);

        var titel = f.Herinnering == 4 ? t.Proforma : credit ? t.Creditfactuur : t.Factuur;
        var etiket = f.Herinnering switch { 1 => t.Herinnering1, 2 => t.Herinnering2, 3 => t.Herinnering3, _ => "" };

        var sb = new StringBuilder();
        Begin(sb, a, $"{titel} {f.Factuurnummer}", liggend: false, VoetTekst(a, t));

        // ---- kop: logo en afzender ----
        sb.Append("<header class='kop'>");
        sb.Append(a.LogoDataUri.Length > 0 ? $"<img class='logo' src='{a.LogoDataUri}' alt=''>" : $"<div class='logo-tekst'>{H(a.Naam)}</div>");
        sb.Append("<div class='afzender'>");
        sb.Append($"<div class='afz-naam'>{H(a.Naam)}</div>");
        sb.Append($"<div>{H(a.Adres)}</div><div>{H(Plaats(a.Postcode, a.Plaats))}</div>");
        foreach (var r in a.Regels) sb.Append($"<div>{H(r)}</div>");
        if (a.Email.Length > 0 && !a.Regels.Any(r => r.Contains('@'))) sb.Append($"<div>{H(a.Email)}</div>");
        sb.Append("</div></header>");

        // ---- adres en factuurgegevens ----
        sb.Append("<section class='adresrij'><div class='adres'>");
        sb.Append($"<div class='klantnaam'>{H(k.Naam)}</div>");
        if (k.Tav.Length > 0) sb.Append($"<div>{t.Tav} {H(k.Tav)}</div>");
        sb.Append($"<div>{H(k.Adres)}</div><div>{H(Plaats(k.Postcode, k.Plaats.ToUpperInvariant()))}</div>");
        if (k.Landnummer != 31 && k.Land.Length > 0) sb.Append($"<div>{H(k.Land.ToUpperInvariant())}</div>");
        sb.Append("</div><table class='meta'>");
        Meta(sb, t.Factuurnummer, f.Factuurnummer, vet: true);
        Meta(sb, t.Factuurdatum, t.Datum(datum));
        if (!credit && f.Herinnering != 4) Meta(sb, t.Vervaldatum, t.Datum(datum.AddDays(termijn)));
        Meta(sb, t.Referentie, f.Referentie);
        Meta(sb, t.PoKlant, f.PoKlant);
        if (f.BtwVerlegd || k.Landnummer != 31) Meta(sb, t.BtwKlant, k.BtwNummer);
        sb.Append("</table></section>");

        // ---- titel ----
        sb.Append($"<div class='titelrij'><h1>{H(titel)}</h1>");
        if (etiket.Length > 0) sb.Append($"<span class='etiket'>{H(etiket)}</span>");
        sb.Append("</div>");
        if (f.Omschrijving.Length > 0) sb.Append($"<div class='onderwerp'>{H(f.Omschrijving)}</div>");

        // ---- regels ----
        sb.Append("<table class='regels'><thead><tr>");
        sb.Append($"<th>{t.Omschrijving}</th><th class='r'>{t.Aantal}</th>");
        if (metPrijs) sb.Append($"<th class='r'>{t.Prijs}</th>");
        if (metKorting) sb.Append($"<th class='r'>{t.Korting}</th>");
        sb.Append($"<th class='r'>{t.Bedrag}</th></tr></thead><tbody>");
        var kolommen = 3 + (metPrijs ? 1 : 0) + (metKorting ? 1 : 0);

        string? vorigeWo = null;
        foreach (var r in OpWerkorder(f.Regels))
        {
            // Urenregels van dezelfde werkorder staan bij elkaar onder één kopje.
            if (r.Werkorder.Length > 0 && r.Werkorder != vorigeWo)
            {
                var kopje = string.Join(" – ", new[] { r.ProjectOmschrijving, r.WerkorderOmschrijving }.Where(s => s.Length > 0));
                sb.Append($"<tr class='groep'><td colspan='{kolommen}'>{H(r.Werkorder)}" +
                          (kopje.Length > 0 ? $" · {H(kopje)}" : "") + "</td></tr>");
            }
            vorigeWo = r.Werkorder.Length > 0 ? r.Werkorder : null;

            var bedrag = r.Bereken();
            string oms, aantal, prijs;
            if (r.IsUren)
            {
                var wie = string.Join(" · ", new[] { r.Naam, t.Uurtype(r.Uurtype) }.Where(s => s.Length > 0));
                oms = H(r.Omschrijving.Length > 0 ? r.Omschrijving : wie);
                if (r.Omschrijving.Length > 0 && wie.Length > 0) oms += $"<div class='klein'>{H(wie)}</div>";
                if (r.Kilometers is > 0) oms += $"<div class='klein'>{t.Getal(r.Kilometers.Value)} km</div>";
                aantal = $"{t.Getal(r.Uren ?? 0)} {t.Uur}";
                prijs = "";
            }
            else
            {
                oms = H(r.Omschrijving).Replace("\n", "<br>");
                aantal = r.Aantal is decimal n && r.Stuksprijs is not null ? t.Getal(n) : "";
                prijs = r.Stuksprijs is decimal p ? t.Geld(p) : "";
            }

            // Een tekstregel zonder bedrag: alleen de omschrijving.
            if (r.Stuksprijs is null && bedrag == 0)
            {
                sb.Append($"<tr class='tekst'><td colspan='{kolommen}'>{oms}</td></tr>");
                continue;
            }

            sb.Append($"<tr><td>{oms}</td><td class='r'>{aantal}</td>");
            if (metPrijs) sb.Append($"<td class='r'>{prijs}</td>");
            if (metKorting) sb.Append($"<td class='r'>{(r.Korting is > 0 ? t.Getal(r.Korting.Value) + "%" : "")}</td>");
            sb.Append($"<td class='r'>{t.Geld(bedrag)}</td></tr>");
        }
        sb.Append("</tbody></table>");

        // ---- totalen ----
        sb.Append("<table class='totalen'>");
        if (f.BtwVerlegd)
        {
            sb.Append($"<tr><td>{t.Btw} </td><td class='r'>{t.Verlegd}</td></tr>");
            sb.Append($"<tr class='eind'><td>{t.TeBetalen}</td><td class='r'>{t.Geld(f.BedragExBtw)}</td></tr>");
        }
        else
        {
            sb.Append($"<tr><td>{t.Subtotaal}</td><td class='r'>{t.Geld(f.BedragExBtw)}</td></tr>");
            sb.Append($"<tr><td>{t.Btw} 21%</td><td class='r'>{t.Geld(f.Btw)}</td></tr>");
            sb.Append($"<tr class='eind'><td>{(credit ? t.Totaal : t.TeBetalen)}</td><td class='r'>{t.Geld(f.BedragInclBtw)}</td></tr>");
        }
        sb.Append("</table>");
        if (f.BtwVerlegd) sb.Append($"<p class='verlegd'>{H(t.VerlegdUitleg)}</p>");

        // ---- betalen ----
        var iban = IbanLeesbaar(a.Iban);
        var betaal = f.Herinnering == 4 ? t.BetaalProforma(iban, a.Naam, f.Factuurnummer)
                   : credit ? t.BetaalCredit
                   : f.Herinnering is >= 1 and <= 3 ? t.BetaalHerinnering(iban, a.Naam, f.Factuurnummer)
                   : t.Betaal(termijn, t.Datum(datum.AddDays(termijn)), iban, a.Naam, f.Factuurnummer);
        sb.Append($"<div class='betalen'>{betaal}</div>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    // ============================================================
    //  De urenbijlage (liggend)
    // ============================================================

    public string BijlageHtml(FactuurPdfGegevens g)
    {
        var f = g.Factuur;
        var t = new Teksten(f.Engels);
        var a = g.Afzender;
        var sb = new StringBuilder();
        Begin(sb, a, $"{t.Urenspecificatie} {f.Factuurnummer}", liggend: true, VoetTekst(a, t));

        sb.Append("<header class='kop'>");
        if (a.LogoDataUri.Length > 0) sb.Append($"<img class='logo' src='{a.LogoDataUri}' alt=''>");
        sb.Append($"<div class='bijlagekop'><h1>{t.Urenspecificatie}</h1>");
        sb.Append($"<div>{t.BijFactuur} <b>{H(f.Factuurnummer)}</b> · {H(g.Klant.Naam)}</div></div></header>");

        foreach (var project in g.Uren.GroupBy(u => u.Project))
        {
            sb.Append($"<h2>{t.Project}: {H(project.Key)}</h2>");
            foreach (var wo in project.GroupBy(u => (u.Werkorder, u.WerkorderOmschrijving)))
            {
                var eerste = wo.First();
                sb.Append($"<div class='wokop'><b>{H(wo.Key.Werkorder)}</b> {H(wo.Key.WerkorderOmschrijving)}" +
                          $"<span class='klein'> · {t.Datum(wo.Min(u => u.SelectieVan))} – {t.Datum(wo.Max(u => u.SelectieTot))}");
                if (eerste.KlantReferentie.Length > 0) sb.Append($" · {t.Referentie} {H(eerste.KlantReferentie)}");
                if (eerste.KlantContact.Length > 0) sb.Append($" · {t.Contact} {H(eerste.KlantContact)}");
                sb.Append("</span></div>");

                sb.Append("<table class='regels uren'><thead><tr>");
                sb.Append($"<th>{t.DatumKop}</th><th>{t.Medewerker}</th><th>{t.Soort}</th><th>{t.Tijd}</th>");
                sb.Append($"<th class='r'>{t.Uren}</th><th class='r'>Km</th><th>{t.Omschrijving}</th><th class='r'>{t.Bedrag}</th>");
                sb.Append("</tr></thead><tbody>");
                foreach (var u in wo)
                {
                    var tijd = u.Start is TimeOnly s && u.Eind is TimeOnly e && (s != e) ? $"{s:HH\\:mm}–{e:HH\\:mm}" : "";
                    sb.Append($"<tr><td>{t.Datum(u.Datum)}</td><td>{H(u.Medewerker)}</td><td>{H(t.Uurtype(u.Uurtype))}</td>" +
                              $"<td>{tijd}</td><td class='r'>{t.Getal(u.Uren)}</td>" +
                              $"<td class='r'>{(u.Kilometers == 0 ? "" : t.Getal(u.Kilometers))}</td>" +
                              $"<td>{H(u.Omschrijving)}</td><td class='r'>{t.Geld(u.Bedrag)}</td></tr>");
                }
                sb.Append($"<tr class='sub'><td colspan='4'>{t.Totaal} {H(wo.Key.Werkorder)}</td>" +
                          $"<td class='r'>{t.Getal(wo.Sum(u => u.Uren))}</td><td class='r'>{t.Getal(wo.Sum(u => u.Kilometers))}</td>" +
                          $"<td></td><td class='r'>{t.Geld(wo.Sum(u => u.Bedrag))}</td></tr>");
                sb.Append("</tbody></table>");
            }
        }

        sb.Append("<table class='totalen breed'>");
        sb.Append($"<tr class='eind'><td>{t.Totaal}: {t.Getal(g.Uren.Sum(u => u.Uren))} {t.Uur}, " +
                  $"{t.Getal(g.Uren.Sum(u => u.Kilometers))} km</td><td class='r'>{t.Geld(g.Uren.Sum(u => u.Bedrag))}</td></tr>");
        sb.Append("</table>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    // ============================================================
    //  Gedeelde opmaak
    // ============================================================

    private static void Begin(StringBuilder sb, FactuurAfzender a, string titel, bool liggend, string voet)
    {
        var kleur = System.Text.RegularExpressions.Regex.IsMatch(a.Kleur, "^#[0-9A-Fa-f]{6}$") ? a.Kleur : "#333333";
        sb.Append("<!doctype html><html><head><meta charset='utf-8'>");
        sb.Append($"<title>{H(titel)}</title><style>");

        // De voet staat in de paginamarge: zo herhaalt hij op elke pagina zonder over de
        // inhoud te vallen, met het paginanummer ernaast.
        var voetCss = voet.Replace("\\", "\\\\").Replace("'", "\\'");
        sb.Append($"@page {{ size: A4 {(liggend ? "landscape" : "portrait")}; margin: 14mm 16mm 20mm 16mm; ");
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
            .adresrij { display: flex; justify-content: space-between; align-items: flex-start; margin-top: 16mm; min-height: 32mm; }
            .adres { font-size: 10.5pt; line-height: 1.4; padding-top: 2mm; }
            .klantnaam { font-weight: 600; }
            table { border-collapse: collapse; }
            .meta td { padding: .6mm 0 .6mm 6mm; font-size: 9pt; vertical-align: top; }
            .meta td:first-child { color: #777; padding-left: 0; }
            .titelrij { display: flex; align-items: baseline; gap: 4mm; margin-top: 8mm;
                        border-bottom: 2px solid var(--kleur); padding-bottom: 1.5mm; }
            h1 { font-size: 20pt; font-weight: 600; margin: 0; letter-spacing: .3px; }
            .etiket { background: var(--kleur); color: #fff; font-size: 8.5pt; font-weight: 600;
                      padding: .8mm 2.5mm; border-radius: 1mm; }
            .onderwerp { margin-top: 2mm; font-size: 10.5pt; color: #444; }
            .regels { width: 100%; margin-top: 6mm; }
            .regels th { text-align: left; font-size: 8pt; font-weight: 600; color: #666; text-transform: uppercase;
                         letter-spacing: .4px; border-bottom: 1px solid #bbb; padding: 1.5mm 2mm; }
            .regels th.r { text-align: right; }
            .regels td { padding: 1.6mm 2mm; border-bottom: 1px solid #eee; vertical-align: top; }
            .regels tr { page-break-inside: avoid; }
            .regels .groep td { padding-top: 3mm; font-weight: 600; color: var(--kleur); border-bottom: none; }
            .regels .tekst td { color: #444; }
            .regels .sub td { font-weight: 600; border-bottom: 1px solid #bbb; }
            .r { text-align: right; white-space: nowrap; }
            .nb { white-space: nowrap; }
            .klein { font-size: 8pt; color: #777; }
            .totalen { margin: 5mm 0 0 auto; min-width: 70mm; page-break-inside: avoid; }
            .totalen.breed { width: 100%; margin-top: 8mm; }
            .totalen td { padding: 1.2mm 2mm; }
            .totalen .eind td { font-weight: 700; font-size: 11pt; border-top: 2px solid var(--kleur); padding-top: 2mm; }
            .verlegd { text-align: right; font-size: 8.5pt; color: #555; margin: 1mm 2mm 0 0; }
            .betalen { margin-top: 10mm; padding: 3mm 4mm; background: #f5f5f5; border-left: 3px solid var(--kleur);
                       font-size: 9pt; line-height: 1.5; page-break-inside: avoid; }
            .bijlagekop { text-align: right; }
            .bijlagekop h1 { font-size: 16pt; }
            h2 { font-size: 11pt; margin: 8mm 0 0; padding: 1.5mm 2mm; background: #f2f2f2; }
            .wokop { margin-top: 4mm; padding: 0 2mm; }
            .uren { margin-top: 1.5mm; font-size: 8.5pt; }
            """);
        sb.Append("</style></head><body>");
    }

    private static string VoetTekst(FactuurAfzender a, Teksten t)
    {
        var delen = new List<string> { a.Naam };
        if (a.Kvk.Length > 0) delen.Add($"KvK {a.Kvk}");
        if (a.Btw.Length > 0) delen.Add($"{t.BtwKort} {a.Btw}");
        if (a.Iban.Length > 0) delen.Add($"IBAN {IbanLeesbaar(a.Iban)}" + (a.Bic.Length > 0 ? $" · BIC {a.Bic}" : ""));
        return string.Join("  ·  ", delen);
    }

    /// <summary>
    /// De regels in factuurvolgorde, maar alle urenregels van één werkorder bij elkaar, op de
    /// plek van de eerste. Artikel- en tekstregels blijven waar ze staan.
    /// </summary>
    private static IEnumerable<VerkoopFactuurItem> OpWerkorder(List<VerkoopFactuurItem> regels)
    {
        var gehad = new HashSet<string>();
        foreach (var r in regels)
        {
            if (r.Werkorder.Length == 0) { yield return r; continue; }
            if (!gehad.Add(r.Werkorder)) continue;
            foreach (var zelfde in regels.Where(x => x.Werkorder == r.Werkorder)) yield return zelfde;
        }
    }

    private static void Meta(StringBuilder sb, string label, string waarde, bool vet = false)
    {
        if (waarde.Trim().Length == 0) return;
        sb.Append($"<tr><td>{H(label)}</td><td>{(vet ? "<b>" : "")}{H(waarde)}{(vet ? "</b>" : "")}</td></tr>");
    }

    private static string Plaats(string postcode, string plaats) => $"{postcode}  {plaats}".Trim();

    /// <summary>NL46RABO0314367594 → NL46 RABO 0314 3675 94.</summary>
    private static string IbanLeesbaar(string iban)
    {
        var kaal = new string(iban.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return string.Join(" ", Enumerable.Range(0, (kaal.Length + 3) / 4)
            .Select(i => kaal.Substring(i * 4, Math.Min(4, kaal.Length - i * 4))));
    }

    private static string H(string? tekst) => HtmlEncode(tekst ?? "");

    // ============================================================
    //  HTML naar PDF met Edge
    // ============================================================

    public async Task<byte[]> NaarPdfAsync(string html, CancellationToken stop = default)
    {
        var edge = EdgePad() ?? throw new InvalidOperationException(
            "Microsoft Edge is niet gevonden. Zet het pad in appsettings (Facturen:EdgePad).");

        var map = Path.Combine(Path.GetTempPath(), "mde-factuur-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(map);
        var invoer = Path.Combine(map, "pagina.html");
        var uitvoer = Path.Combine(map, "pagina.pdf");
        await File.WriteAllTextAsync(invoer, html, new UTF8Encoding(false), stop);

        try
        {
            var start = new ProcessStartInfo(edge)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (var arg in new[]
            {
                "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
                "--disable-extensions", "--no-pdf-header-footer", "--run-all-compositor-stages-before-draw",
                $"--user-data-dir={Path.Combine(map, "profiel")}",
                $"--print-to-pdf={uitvoer}",
                new Uri(invoer).AbsoluteUri,
            }) start.ArgumentList.Add(arg);

            using var proces = Process.Start(start) ?? throw new InvalidOperationException("Edge start niet.");
            using var tijd = CancellationTokenSource.CreateLinkedTokenSource(stop);
            tijd.CancelAfter(TimeSpan.FromSeconds(60));
            try { await proces.WaitForExitAsync(tijd.Token); }
            catch (OperationCanceledException)
            {
                try { proces.Kill(true); } catch { }
                throw new InvalidOperationException("Edge deed er langer dan een minuut over; geen PDF gemaakt.");
            }

            if (!File.Exists(uitvoer) || new FileInfo(uitvoer).Length == 0)
            {
                var fout = await proces.StandardError.ReadToEndAsync();
                _log.LogWarning("Edge maakte geen PDF (exit {Code}): {Fout}", proces.ExitCode, fout);
                throw new InvalidOperationException("Edge heeft geen PDF gemaakt.");
            }
            return await File.ReadAllBytesAsync(uitvoer, stop);
        }
        finally
        {
            try { Directory.Delete(map, true); } catch { /* Edge kan het profiel nog even vasthouden */ }
        }
    }

    private string? EdgePad()
    {
        var ingesteld = _config["Facturen:EdgePad"];
        var kandidaten = new[]
        {
            ingesteld,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
        };
        return kandidaten.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
    }

    // ============================================================
    //  Teksten in het Nederlands of Engels
    // ============================================================

    private sealed class Teksten
    {
        private readonly bool _en;
        private readonly CultureInfo _cultuur;

        public Teksten(bool engels)
        {
            _en = engels;
            // Engels met euro's: Iers-Engels geeft € 1,174.30.
            _cultuur = new CultureInfo(engels ? "en-IE" : "nl-NL");
        }

        private string T(string nl, string en) => _en ? en : nl;

        public string Geld(decimal w) => w.ToString("C2", _cultuur);
        public string Getal(decimal w) => w.ToString("#,0.##", _cultuur);
        public string Datum(DateOnly d) => d.ToString("dd-MM-yyyy");

        public string Factuur => T("Factuur", "Invoice");
        public string Creditfactuur => T("Creditfactuur", "Credit note");
        public string Proforma => T("Proformafactuur", "Proforma invoice");
        public string Herinnering1 => T("1e herinnering", "First reminder");
        public string Herinnering2 => T("2e herinnering", "Second reminder");
        public string Herinnering3 => T("3e herinnering", "Third reminder");
        public string Factuurnummer => T("Factuurnummer", "Invoice number");
        public string Factuurdatum => T("Factuurdatum", "Invoice date");
        public string Vervaldatum => T("Vervaldatum", "Due date");
        public string Referentie => T("Referentie", "Reference");
        public string PoKlant => T("Uw ordernummer", "Your PO number");
        public string BtwKlant => T("Btw-nummer klant", "Customer VAT number");
        public string Tav => T("t.a.v.", "Attn.");
        public string Omschrijving => T("Omschrijving", "Description");
        public string Aantal => T("Aantal", "Qty");
        public string Prijs => T("Prijs", "Price");
        public string Korting => T("Korting", "Discount");
        public string Bedrag => T("Bedrag", "Amount");
        public string Uur => T("uur", "hrs");
        public string Subtotaal => T("Subtotaal", "Subtotal");
        public string Btw => T("Btw", "VAT");
        public string BtwKort => T("Btw", "VAT");
        public string Verlegd => T("verlegd", "reverse charged");
        public string VerlegdUitleg => T("Btw verlegd naar de afnemer.", "VAT reverse charged to the customer.");
        public string Totaal => T("Totaal", "Total");
        public string TeBetalen => T("Te betalen", "Amount due");
        public string Urenspecificatie => T("Urenspecificatie", "Specification of hours");
        public string BijFactuur => T("Bij factuur", "With invoice");
        public string Project => T("Project", "Project");
        public string Contact => T("contact", "contact");
        public string DatumKop => T("Datum", "Date");
        public string Medewerker => T("Medewerker", "Employee");
        public string Soort => T("Soort", "Type");
        public string Tijd => T("Tijd", "Time");
        public string Uren => T("Uren", "Hours");

        public string Uurtype(string soort) => _en ? soort : soort switch
        {
            "Normal" => "Normaal",
            "Travel" => "Reistijd",
            "Overtime" => "Overwerk",
            "Overtime+" => "Overwerk zon-/feestdag",
            "Fixed" => "Vast bedrag",
            _ => soort,
        };

        public string Betaal(int dagen, string uiterlijk, string iban, string naam, string nr) => _en
            ? $"Thank you for your order. Please transfer the amount due within {dagen} days, before <b>{uiterlijk}</b>, " +
              $"to <b class='nb'>{HtmlEncode(iban)}</b> in the name of {HtmlEncode(naam)}, quoting invoice number <b>{HtmlEncode(nr)}</b>."
            : $"Wij danken u voor uw opdracht. Wij verzoeken u het bedrag binnen {dagen} dagen, uiterlijk <b>{uiterlijk}</b>, " +
              $"over te maken op <b class='nb'>{HtmlEncode(iban)}</b> t.n.v. {HtmlEncode(naam)}, onder vermelding van factuurnummer <b>{HtmlEncode(nr)}</b>.";

        public string BetaalHerinnering(string iban, string naam, string nr) => _en
            ? $"According to our records this invoice has not been paid yet. We kindly request that you transfer the amount " +
              $"to <b class='nb'>{HtmlEncode(iban)}</b> in the name of {HtmlEncode(naam)} without delay, quoting invoice number <b>{HtmlEncode(nr)}</b>. " +
              "If you have already paid, please disregard this reminder."
            : $"Volgens onze administratie is deze factuur nog niet betaald. Wij verzoeken u het bedrag per omgaande over te maken op " +
              $"<b class='nb'>{HtmlEncode(iban)}</b> t.n.v. {HtmlEncode(naam)}, onder vermelding van factuurnummer <b>{HtmlEncode(nr)}</b>. " +
              "Heeft u inmiddels betaald, dan kunt u deze herinnering als niet verzonden beschouwen.";

        public string BetaalProforma(string iban, string naam, string nr) => _en
            ? $"This is a proforma invoice. Please transfer the amount to <b class='nb'>{HtmlEncode(iban)}</b> in the name of " +
              $"{HtmlEncode(naam)}, quoting <b>{HtmlEncode(nr)}</b>."
            : $"Dit is een proformafactuur. Wij verzoeken u het bedrag over te maken op <b class='nb'>{HtmlEncode(iban)}</b> t.n.v. " +
              $"{HtmlEncode(naam)}, onder vermelding van <b>{HtmlEncode(nr)}</b>.";

        public string BetaalCredit => T("Het bedrag van deze creditfactuur wordt met u verrekend of teruggestort.",
                                        "The amount of this credit note will be settled or refunded.");
    }
}
