using System.Globalization;
using System.Net;
using System.Text;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// De mail aan de leverancier vanuit Bestellingen: een offerteaanvraag (status Aanmaken, "PI-") of de bestelling zelf
/// (status In bestelling, "PO-"). Vervangt Create_order_request_Mail en Create_order_Mail uit het Access-formulier
/// "Order main form"; Access stelde een Outlook-mail samen, nu een voorbeeld in de app en versturen via Gmail.
///
/// Inhoud zoals Access (taal: 0 = Nederlands, anders Engels; onderwerp, inleiding, tabel met aantal/eenheid/artikel/
/// [OC-code]/omschrijving/leverdatum, bij de bestelling met prijzen en totaal, "LET OP: DIT IS GEEN OPDRACHT" bij de
/// aanvraag, de melding op het orderformulier, het afleveradres, groet en ondertekening). De opmaak is nieuw (wens
/// gebruiker: de Access-mail was lelijk): inline-stijlen (mailprogramma's negeren stylesheets), het logo als ingebed
/// plaatje (cid:logo-mde) en onder de mail de adresregel van MDE.
/// Bewust weggelaten t.o.v. Access: de lege prijskolommen in de aanvraag, en de voettekst-bestanden uit Google Drive.
/// </summary>
public static class BestelMail
{
    public const string LogoCid = "logo-mde";
    public const string StandaardCc = "admin@mde-automation.nl";

    public static bool IsAanvraag(BestelMailGegevens g) => g.Status == BestelFlow.Aanmaken;

    public static string Onderwerp(BestelMailGegevens g)
        => IsAanvraag(g)
            ? (g.Engels ? $"REQUEST price and delivery time PI-{g.OrderId}" : $"AANVRAAG prijs levertijd PI-{g.OrderId}")
            : $"Order: PO-{g.OrderId}";

    /// <summary>De aanhef zoals Access: die van de contactpersoon, anders "Geachte heer/mevrouw" / "Dear Sir/Madam".</summary>
    public static string StandaardAanhef(BestelMailGegevens g)
        => g.Aanhef.Length > 0 ? g.Aanhef.TrimEnd(',') : g.Engels ? "Dear Sir/Madam" : "Geachte heer/mevrouw";

    /// <param name="logoSrc">Waar het logo vandaan komt: "cid:logo-mde" in de mail, een gewone url in het voorbeeld.</param>
    public static string Html(BestelMailGegevens g, string aanhef, string logoSrc)
    {
        var en = g.Engels;
        var aanvraag = IsAanvraag(g);
        var cultuur = new CultureInfo(en ? "en-IE" : "nl-NL");
        string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        string Geld(decimal d) => d.ToString("C2", cultuur);
        string Aantal(double d) => d.ToString("0.##", cultuur);

        const string font = "font-family:Calibri,'Segoe UI',Arial,sans-serif;";
        const string blauw = "#1f4e8c";
        var h = new StringBuilder();
        h.Append($"<div style=\"{font}font-size:14px;line-height:1.45;color:#222;max-width:860px;\">");

        h.Append($"<p style=\"margin:0 0 12px;\">{E(aanhef)},</p>");
        h.Append("<p style=\"margin:0 0 14px;\">")
         .Append(aanvraag
             ? (en ? "We kindly ask you to give us price and delivery times for the following items."
                   : "Graag ontvangen wij prijs en levertijd van de onderstaande artikelen.")
             : (en ? $"We would like to order the following items under purchase order <b>PO-{g.OrderId}</b>."
                   : $"Graag bestellen wij onderstaande artikelen bij u onder vermelding van ordernummer <b>PO-{g.OrderId}</b>."))
         .Append("</p>");

        if (aanvraag)
            h.Append($"<p style=\"margin:0 0 14px;padding:8px 12px;border-left:4px solid #c62828;background:#fdecea;color:#b71c1c;font-weight:700;\">")
             .Append(en ? "NOTE: THIS IS NOT AN ORDER" : "LET OP: DIT IS GEEN OPDRACHT").Append("</p>");

        // ---- tabel ----
        var kop = new List<(string Tekst, bool Rechts)>
        {
            (en ? "Qty" : "Aantal", true), (en ? "Unit" : "Eenheid", false), (en ? "Item" : "Artikel", false),
        };
        if (g.OcCodes) kop.Add(("OC", false));
        kop.Add((en ? "Description" : "Omschrijving", false));
        kop.Add((aanvraag ? (en ? "Req. delivery" : "Gewenste levertijd") : (en ? "Delivery" : "Leverdatum"), false));
        if (!aanvraag)
        {
            kop.Add((en ? "Gross price" : "Bruto prijs", true));
            kop.Add((en ? "Discount" : "Korting", true));
            kop.Add((en ? "Total" : "Totaal", true));
        }

        const string cel = "padding:6px 10px;border-bottom:1px solid #e3e8ef;vertical-align:top;";
        h.Append("<table cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;width:100%;font-size:13px;margin:0 0 16px;\">");
        h.Append("<tr>");
        foreach (var (tekst, rechts) in kop)
            h.Append($"<th style=\"padding:7px 10px;background:{blauw};color:#fff;font-weight:600;text-align:{(rechts ? "right" : "left")};white-space:nowrap;\">{E(tekst)}</th>");
        h.Append("</tr>");

        decimal totaal = 0;
        var i = 0;
        foreach (var r in g.Regels)
        {
            var achter = i++ % 2 == 1 ? "background:#f5f8fc;" : "";
            h.Append("<tr>");
            h.Append($"<td style=\"{cel}{achter}text-align:right;white-space:nowrap;\">{Aantal(r.Aantal)}</td>");
            h.Append($"<td style=\"{cel}{achter}white-space:nowrap;\">{E(r.Eenheid)}</td>");
            h.Append($"<td style=\"{cel}{achter}white-space:nowrap;font-weight:600;\">{E(r.Artikel)}</td>");
            if (g.OcCodes) h.Append($"<td style=\"{cel}{achter}white-space:nowrap;\">{E(r.Oc)}</td>");
            h.Append($"<td style=\"{cel}{achter}\">{E(r.Omschrijving)}</td>");
            h.Append($"<td style=\"{cel}{achter}white-space:nowrap;\">{(r.Leverdatum is { } d ? d.ToString("dd-MM-yyyy") : "")}</td>");
            if (!aanvraag)
            {
                var regel = (decimal)r.Aantal * r.Prijs * (1 - r.Korting);
                totaal += regel;
                h.Append($"<td style=\"{cel}{achter}text-align:right;white-space:nowrap;\">{Geld(r.Prijs)}</td>");
                h.Append($"<td style=\"{cel}{achter}text-align:right;white-space:nowrap;\">{(r.Korting == 0 ? "" : r.Korting.ToString("P0", cultuur))}</td>");
                h.Append($"<td style=\"{cel}{achter}text-align:right;white-space:nowrap;\">{Geld(regel)}</td>");
            }
            h.Append("</tr>");
        }
        if (!aanvraag)
            h.Append($"<tr><td colspan=\"{kop.Count - 1}\" style=\"padding:8px 10px;text-align:right;font-weight:700;border-top:2px solid {blauw};\">{(en ? "Total" : "Totaal")}</td>")
             .Append($"<td style=\"padding:8px 10px;text-align:right;font-weight:700;white-space:nowrap;border-top:2px solid {blauw};\">{Geld(totaal)}</td></tr>");
        h.Append("</table>");

        // ---- melding op het orderformulier en afleveradres ----
        const string blok = "margin:0 0 14px;padding:10px 14px;border-radius:6px;";
        if (g.Opmerking.Trim().Length > 0)
            h.Append($"<div style=\"{blok}background:#fff8e1;border-left:4px solid #e0a800;\">")
             .Append(E(g.Opmerking.Trim()).Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>"))
             .Append("</div>");
        if (g.AfleverId is not null)
        {
            h.Append($"<div style=\"{blok}background:#eef4fb;border-left:4px solid {blauw};\">")
             .Append($"<div style=\"font-weight:700;color:{blauw};margin-bottom:4px;\">{(en ? "Delivery address" : "Afleveradres")}</div>")
             .Append(E(g.AfleverNaam)).Append("<br>");
            if (g.AfleverTav.Length > 0) h.Append(en ? "Attn: " : "T.a.v. ").Append(E(g.AfleverTav)).Append("<br>");
            h.Append(E(g.AfleverAdres)).Append("<br>").Append(E($"{g.AfleverPostcode} {g.AfleverPlaats}".Trim())).Append("</div>");
        }

        // ---- groet, ondertekening, logo, adresregel ----
        h.Append($"<p style=\"margin:18px 0 4px;\">{(en ? "Kind regards," : "Met vriendelijke groet,")}</p>");
        h.Append("<p style=\"margin:0 0 14px;\">");
        if (g.Afzender.Naam.Length > 0) h.Append($"<b>{E(g.Afzender.Naam)}</b><br>");
        h.Append("MDE Automation B.V.<br>");
        if (g.Afzender.Mobiel.Length > 0) h.Append(E(g.Afzender.Mobiel)).Append("<br>");
        if (g.Afzender.Mail.Length > 0) h.Append($"<a href=\"mailto:{E(g.Afzender.Mail)}\" style=\"color:{blauw};\">{E(g.Afzender.Mail)}</a>");
        h.Append("</p>");
        h.Append($"<img src=\"{E(logoSrc)}\" alt=\"MDE Automation\" width=\"170\" style=\"display:block;width:170px;height:auto;margin:0 0 10px;\">");
        h.Append($"<p style=\"margin:0;padding-top:8px;border-top:1px solid #e3e8ef;font-size:11px;color:#888;\">{E(g.Bedrijfsregel)}</p>");
        h.Append("</div>");
        return h.ToString();
    }

    /// <summary>
    /// Mail aan de besteller als zijn bestelling compleet binnen is (Access SendMail_Orderdelivered, nu in dezelfde opmaak
    /// als de mail aan de leverancier). Onderwerp: "PO-n van LEVERANCIER is compleet geleverd."
    /// </summary>
    public static string GeleverdOnderwerp(GeleverdMailGegevens g) => $"PO-{g.OrderId} van {g.Leverancier.ToUpperInvariant()} is compleet geleverd.";

    public static string GeleverdHtml(GeleverdMailGegevens g, string ondertekenaar, string logoSrc)
    {
        string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        const string font = "font-family:Calibri,'Segoe UI',Arial,sans-serif;";
        const string blauw = "#1f4e8c";
        const string cel = "padding:6px 10px;border-bottom:1px solid #e3e8ef;vertical-align:top;";
        var nl = new CultureInfo("nl-NL");
        var h = new StringBuilder();
        h.Append($"<div style=\"{font}font-size:14px;line-height:1.45;color:#222;max-width:760px;\">");
        h.Append($"<p style=\"margin:0 0 12px;\">Beste {E(g.NaamOntvanger)},</p>");
        h.Append($"<p style=\"margin:0 0 12px;\">De door jou bestelde artikelen van <b>{E(g.Leverancier.ToUpperInvariant())}</b> op <b>PO-{g.OrderId}</b> zijn binnen.</p>");
        h.Append("<p style=\"margin:0 0 14px;color:#555;\">");
        if (g.Klant.Length > 0) h.Append($"Projectklant: {E(g.Klant)}<br>");
        if (g.Project.Length > 0) h.Append($"Project: {E(g.Project)}<br>");
        h.Append($"Leverancier: {E(g.Leverancier)}</p>");
        h.Append("<table cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;width:100%;font-size:13px;margin:0 0 16px;\"><tr>");
        foreach (var (k, rechts) in new[] { ("Aantal", true), ("Eenheid", false), ("Artikel", false), ("Omschrijving", false) })
            h.Append($"<th style=\"padding:7px 10px;background:{blauw};color:#fff;font-weight:600;text-align:{(rechts ? "right" : "left")};\">{k}</th>");
        h.Append("</tr>");
        var i = 0;
        foreach (var (aantal, eenheid, artikel, oms) in g.Regels)
        {
            var achter = i++ % 2 == 1 ? "background:#f5f8fc;" : "";
            h.Append($"<tr><td style=\"{cel}{achter}text-align:right;\">{aantal.ToString("0.##", nl)}</td><td style=\"{cel}{achter}\">{E(eenheid)}</td>")
             .Append($"<td style=\"{cel}{achter}font-weight:600;white-space:nowrap;\">{E(artikel)}</td><td style=\"{cel}{achter}\">{E(oms)}</td></tr>");
        }
        h.Append("</table>");
        if (g.Opmerking.Trim().Length > 0)
            h.Append("<div style=\"margin:0 0 14px;padding:10px 14px;border-radius:6px;background:#fff8e1;border-left:4px solid #e0a800;\">")
             .Append(E(g.Opmerking.Trim()).Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>")).Append("</div>");
        h.Append("<p style=\"margin:18px 0 4px;\">Met vriendelijke groet,</p>");
        h.Append($"<p style=\"margin:0 0 14px;\">{(ondertekenaar.Length > 0 ? $"<b>{E(ondertekenaar)}</b><br>" : "")}MDE Automation B.V.</p>");
        h.Append($"<img src=\"{E(logoSrc)}\" alt=\"MDE Automation\" width=\"150\" style=\"display:block;width:150px;height:auto;\">");
        h.Append("</div>");
        return h.ToString();
    }

    /// <summary>Waarschuwingen vooraf (Access: Mail_Click en de controles in de mailfuncties). Blokkerend = niet versturen.</summary>
    public static List<(string Tekst, bool Blokkerend)> Controles(BestelMailGegevens g)
    {
        var l = new List<(string, bool)>();
        var aanvraag = IsAanvraag(g);
        if (g.ContactId is null) l.Add(("Geen contactpersoon gekozen (tabblad Algemeen).", true));
        if (g.Regels.Count == 0) l.Add(("Er staan geen regels op deze bestelling.", true));
        if (g.AfleverId is not null)
        {
            if (g.AfleverAdres.Length == 0) l.Add(("Geen adres ingevuld bij het afleveradres (Relaties).", true));
            if (g.AfleverPostcode.Length == 0) l.Add(("Geen postcode ingevuld bij het afleveradres (Relaties).", true));
            if (g.AfleverPlaats.Length == 0) l.Add(("Geen plaats ingevuld bij het afleveradres (Relaties).", true));
        }
        var standaard = aanvraag ? g.OfferteContactId : g.OrderContactId;
        if (standaard is not null && g.ContactId is not null && standaard != g.ContactId)
            l.Add(($"Er is een andere contactpersoon gekozen dan standaard bij deze leverancier voor {(aanvraag ? "offertes" : "opdrachten")}.", false));
        var vandaag = DateOnly.FromDateTime(DateTime.Today);
        if (g.Regels.Any(r => r.Leverdatum is { } d && d < vandaag))
            l.Add(("De leverdatum van sommige artikelen ligt voor vandaag.", false));
        var oud = g.Bijlagen.Where(b => b.Verouderd).Select(b => b.Naam).Distinct().ToList();
        if (oud.Count > 0)
            l.Add(($"Niet de laatste versie: {string.Join(", ", oud)}. Vervang ze via 📎 bij de regel, of verstuur toch.", false));
        if (!aanvraag && g.Regels.Any(r => r.Aantal != 0 && r.Prijs == 0))
            l.Add(("Niet alle regels hebben een prijs.", false));
        return l;
    }
}
