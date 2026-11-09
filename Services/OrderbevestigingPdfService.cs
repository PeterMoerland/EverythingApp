using System.Globalization;
using System.Text;
using EverythingApp.Models;
using static System.Net.WebUtility;

namespace EverythingApp.Services;

/// <summary>
/// De orderbevestiging van een verkooporder (wens gebruiker 9-11-2026) als PDF, in dezelfde opmaak als de factuur:
/// zelfde kop, logo en kleur per BV (Templates/Factuur), zelfde voet en dezelfde Edge-afdruk (FactuurPdfService).
/// Nederlands of Engels volgens de order (standaard: Relations.FactuurEngels van de klant).
/// </summary>
public class OrderbevestigingPdfService
{
    private readonly FactuurPdfService _factuur;
    private readonly IVerkoopOrderRepository _repo;

    public OrderbevestigingPdfService(FactuurPdfService factuur, IVerkoopOrderRepository repo)
    {
        _factuur = factuur;
        _repo = repo;
    }

    public static string Bestandsnaam(VerkoopOrder o) => $"{o.Ordernummer}_{(o.Engels ? "Order confirmation" : "Orderbevestiging")}.pdf";

    public static string Onderwerp(VerkoopOrder o, string afzender) => o.Engels
        ? $"Order confirmation {o.Ordernummer}" + (o.PoKlant.Length > 0 ? $" (your PO {o.PoKlant})" : "") + $" - {afzender}"
        : $"Orderbevestiging {o.Ordernummer}" + (o.PoKlant.Length > 0 ? $" (uw ordernummer {o.PoKlant})" : "") + $" - {afzender}";

    public static string MailTekst(VerkoopOrder o, string contact)
    {
        var aanhef = o.Engels ? "Dear" : "Beste";
        var naam = contact.Trim().Length > 0 ? " " + contact.Trim() : (o.Engels ? " Sir or Madam" : "");
        return o.Engels
            ? $"{aanhef}{naam},\n\nThank you for your order. Please find attached our order confirmation {o.Ordernummer}." +
              (o.Leverdatum is DateOnly d ? $"\nExpected delivery date: {d:dd-MM-yyyy}." : "") +
              "\n\nPlease let us know if anything is not correct.\n\nKind regards,"
            : $"{aanhef}{naam},\n\nHartelijk dank voor uw bestelling. In de bijlage vindt u onze orderbevestiging {o.Ordernummer}." +
              (o.Leverdatum is DateOnly d2 ? $"\nVerwachte leverdatum: {d2:dd-MM-yyyy}." : "") +
              "\n\nKlopt er iets niet, laat het ons dan weten.\n\nMet vriendelijke groet,";
    }

    public async Task<(byte[] Pdf, FactuurAfzender Afzender)> MaakAsync(VerkoopOrder o, string contact, CancellationToken stop = default)
    {
        var afzender = await _repo.AfzenderAsync(o.BvId);
        _factuur.VulAfzender(afzender);
        var klant = await _repo.KlantAdresAsync(o.KlantId);
        klant.Tav = contact;
        return (await _factuur.NaarPdfAsync(Html(o, klant, afzender), stop), afzender);
    }

    public string Html(VerkoopOrder o, FactuurAdres k, FactuurAfzender a)
    {
        var en = o.Engels;
        string T(string nl, string eng) => en ? eng : nl;
        var cultuur = new CultureInfo(en ? "en-IE" : "nl-NL");
        string Geld(decimal w) => w.ToString("C2", cultuur);
        string Getal(decimal w) => w.ToString("#,0.###", cultuur);

        var titel = T("Orderbevestiging", "Order confirmation");
        var sb = new StringBuilder();
        FactuurPdfService.Begin(sb, a, $"{titel} {o.Ordernummer}", liggend: false, Voet(a, T("Btw", "VAT")));

        // ---- kop: logo en afzender ----
        sb.Append("<header class='kop'>");
        sb.Append(a.LogoDataUri.Length > 0 ? $"<img class='logo' src='{a.LogoDataUri}' alt=''>" : $"<div class='logo-tekst'>{H(a.Naam)}</div>");
        sb.Append("<div class='afzender'>");
        sb.Append($"<div class='afz-naam'>{H(a.Naam)}</div>");
        sb.Append($"<div>{H(a.Adres)}</div><div>{H(FactuurPdfService.Plaats(a.Postcode, a.Plaats))}</div>");
        foreach (var r in a.Regels) sb.Append($"<div>{H(r)}</div>");
        if (a.Email.Length > 0 && !a.Regels.Any(r => r.Contains('@'))) sb.Append($"<div>{H(a.Email)}</div>");
        sb.Append("</div></header>");

        // ---- adres en ordergegevens ----
        sb.Append("<section class='adresrij'><div class='adres'>");
        sb.Append($"<div class='klantnaam'>{H(k.Naam)}</div>");
        if (k.Tav.Length > 0) sb.Append($"<div>{T("t.a.v.", "Attn.")} {H(k.Tav)}</div>");
        sb.Append($"<div>{H(k.Adres)}</div><div>{H(FactuurPdfService.Plaats(k.Postcode, k.Plaats.ToUpperInvariant()))}</div>");
        if (k.Landnummer != 31 && k.Land.Length > 0) sb.Append($"<div>{H(k.Land.ToUpperInvariant())}</div>");
        sb.Append("</div><table class='meta'>");
        FactuurPdfService.Meta(sb, T("Ordernummer", "Order number"), o.Ordernummer, vet: true);
        FactuurPdfService.Meta(sb, T("Datum", "Date"), DateTime.Today.ToString("dd-MM-yyyy"));
        FactuurPdfService.Meta(sb, T("Uw ordernummer", "Your PO number"), o.PoKlant);
        if (o.Leverdatum is DateOnly d) FactuurPdfService.Meta(sb, T("Verwachte levering", "Expected delivery"), d.ToString("dd-MM-yyyy"));
        sb.Append("</table></section>");

        sb.Append($"<div class='titelrij'><h1>{H(titel)}</h1></div>");
        if (o.Omschrijving.Length > 0) sb.Append($"<div class='onderwerp'>{H(o.Omschrijving)}</div>");

        // ---- regels ----
        sb.Append("<table class='regels'><thead><tr>");
        sb.Append($"<th>{T("Omschrijving", "Description")}</th><th class='r'>{T("Aantal", "Qty")}</th>");
        sb.Append($"<th class='r'>{T("Prijs", "Price")}</th><th class='r'>{T("Bedrag", "Amount")}</th></tr></thead><tbody>");
        foreach (var r in o.Regels)
        {
            var oms = H(r.Omschrijving).Replace("\n", "<br>");
            if (r.Code.Length > 0) oms = $"<div class='klein'>{H(r.Code)}</div>" + oms;
            if (r.Verkoopprijs is null && r.Aantal == 0)
            {
                sb.Append($"<tr class='tekst'><td colspan='4'>{oms}</td></tr>");
                continue;
            }
            sb.Append($"<tr><td>{oms}</td><td class='r'>{Getal(r.Aantal)}</td>" +
                      $"<td class='r'>{(r.Verkoopprijs is decimal p ? Geld(p) : "")}</td><td class='r'>{Geld(r.Totaal)}</td></tr>");
        }
        sb.Append("</tbody></table>");

        // ---- totalen (btw zoals de factuur: afgekapt op centen) ----
        var ex = o.Regels.Sum(r => r.Totaal);
        sb.Append("<table class='totalen'>");
        if (o.BtwVerlegd)
        {
            sb.Append($"<tr><td>{T("Btw", "VAT")} </td><td class='r'>{T("verlegd", "reverse charged")}</td></tr>");
            sb.Append($"<tr class='eind'><td>{T("Totaal", "Total")}</td><td class='r'>{Geld(ex)}</td></tr>");
        }
        else
        {
            var btw = Math.Floor(ex * 21m) / 100m;
            sb.Append($"<tr><td>{T("Subtotaal", "Subtotal")}</td><td class='r'>{Geld(ex)}</td></tr>");
            sb.Append($"<tr><td>{T("Btw", "VAT")} 21%</td><td class='r'>{Geld(btw)}</td></tr>");
            sb.Append($"<tr class='eind'><td>{T("Totaal", "Total")}</td><td class='r'>{Geld(ex + btw)}</td></tr>");
        }
        sb.Append("</table>");

        sb.Append("<div class='betalen'>" + T(
            "Hartelijk dank voor uw bestelling. Wij leveren volgens bovenstaande gegevens; de factuur volgt na levering. " +
            "Klopt er iets niet, neem dan binnen twee werkdagen contact met ons op.",
            "Thank you for your order. We will deliver as specified above; the invoice follows after delivery. " +
            "If anything is not correct, please contact us within two working days.") + "</div>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Voet(FactuurAfzender a, string btw)
    {
        var delen = new List<string> { a.Naam };
        if (a.Kvk.Length > 0) delen.Add($"KvK {a.Kvk}");
        if (a.Btw.Length > 0) delen.Add($"{btw} {a.Btw}");
        return string.Join("  ·  ", delen);
    }

    private static string H(string? tekst) => HtmlEncode(tekst ?? "");
}
