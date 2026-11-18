using System.Globalization;
using System.Text;
using EverythingApp.Models;
using static System.Net.WebUtility;

namespace EverythingApp.Services;

/// <summary>
/// De officiële inkoopopdracht van een bestelling als PDF (wens gebruiker 18-11-2026): het document dat naar de
/// leverancier kan. Vervangt het Access-rapport Orderdoc_Main / Orderdoc_Main_EN, dat Access bij het mailen van de
/// bestelling als PO-n.pdf meestuurde als Relations.GenereerOfficieleOrder aan stond.
///
/// Inhoud zoals Access (leverancier met t.a.v., datum, onze referentie PO-n, aanhef, "Graag bestellen wij …", de regels
/// met prijs, korting en totaal, ordertotaal, de melding op het orderformulier, groet met naam, functie, mail en
/// telefoon, de handtekening uit Werknemers en de voorwaarden). De opmaak is die van de factuur en de orderbevestiging:
/// zelfde kop, logo en kleur per BV (Templates/Factuur), zelfde voet en dezelfde Edge-afdruk (FactuurPdfService).
/// Erbij t.o.v. Access: eenheid, OC-code (als de leverancier die gebruikt), afleveradres en het factuuradres van een
/// andere BV, zoals in de mail (BestelMail). Weggelaten: de e-maildisclaimer, die in een document niets zegt.
/// </summary>
public class InkoopOrderPdfService
{
    private readonly FactuurPdfService _factuur;
    private readonly IVerkoopOrderRepository _relaties;
    private readonly IBestelRepository _bestel;
    private readonly IWerknemerRepository _bestanden;
    private readonly FilestoreService _store;
    private readonly IConfiguration _config;

    public InkoopOrderPdfService(FactuurPdfService factuur, IVerkoopOrderRepository relaties, IBestelRepository bestel,
                                 IWerknemerRepository bestanden, FilestoreService store, IConfiguration config)
    {
        _factuur = factuur;
        _relaties = relaties;
        _bestel = bestel;
        _bestanden = bestanden;
        _store = store;
        _config = config;
    }

    /// <summary>Zoals Access: PO-15826.pdf.</summary>
    public static string Bestandsnaam(int orderId) => $"PO-{orderId}.pdf";

    // De voorwaarden onder het document, uit het Access-rapport; aan te passen met Bestellingen:InkoopVoorwaarden
    // (en …En) in appsettings.json. Een spatie als waarde = geen voorwaarden op het document. Access schreef "Uw
    // inkoopvoorwaarden wijzen wij … van de hand" (overgenomen van de offerte); op een inkooporder zijn dat de
    // verkoopvoorwaarden van de leverancier (besluit gebruiker 18-11-2026).
    private const string VoorwaardenNl =
        "Op deze bestelling en op alle daaruit voortvloeiende overeenkomst(en) zijn de METAALUNIEVOORWAARDEN van toepassing, " +
        "gedeponeerd ter Griffie van de Rechtbank te Rotterdam, zoals deze luiden volgens de laatstelijk aldaar neergelegde tekst. " +
        "De leveringsvoorwaarden worden u op verzoek toegezonden. Uw verkoopvoorwaarden wijzen wij uitdrukkelijk van de hand.";
    private const string VoorwaardenEn =
        "This order and all agreements arising from it are subject to the METAALUNIE CONDITIONS, filed with the Registry of the " +
        "Court of Rotterdam, in the version most recently filed there. The conditions will be sent to you on request. " +
        "We expressly reject your terms and conditions of sale.";

    /// <summary>De PDF van deze bestelling; aanhef zonder komma (zoals in de mail).</summary>
    public async Task<byte[]> MaakAsync(BestelMailGegevens g, string aanhef, CancellationToken stop = default)
    {
        var afzender = await _relaties.AfzenderAsync(g.BvId);
        _factuur.VulAfzender(afzender);
        var lev = g.LeverancierId is int l ? await _relaties.KlantAdresAsync(l) : new FactuurAdres();
        if (lev.Naam.Length == 0) lev.Naam = g.Leverancier;
        lev.Tav = g.ContactNaam;
        var handtekening = "";
        if (g.Afzender.Id > 0)
            try { handtekening = HandtekeningDataUri(await _bestel.HandtekeningAsync(g.Afzender.Id)); } catch { }
        return await _factuur.NaarPdfAsync(Html(g, aanhef, lev, afzender, handtekening), stop);
    }

    /// <summary>
    /// De PDF bij de documenten van de bestelling zetten (FileStore StoreType 4). vervang = de vorige PO-n.pdf gaat weg
    /// (zolang de bestelling nog niet besteld is: het is dan nog een concept); anders komt er een versie naast,
    /// "PO-n (2).pdf", zodat wat de leverancier kreeg bewaard blijft. Geeft het FileStoreID.
    /// </summary>
    public async Task<int> BewaarAsync(int orderId, byte[] pdf, bool vervang, int werknemerId)
    {
        var naam = Bestandsnaam(orderId);
        var bestaand = await _bestanden.GetBestandenAsync(BestelVelden.StoreTypeOrder, orderId);
        if (vervang)
        {
            foreach (var oud in bestaand.Where(b => b.Bestandsnaam.Equals(naam, StringComparison.OrdinalIgnoreCase)))
                await _store.VerwijderAsync(oud.FileStoreId);
        }
        else
        {
            var namen = bestaand.Select(b => b.Bestandsnaam).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var n = 2; namen.Contains(naam); n++) naam = $"PO-{orderId} ({n}).pdf";
        }
        using var stroom = new MemoryStream(pdf);
        return await _store.ToevoegenAsync(stroom, naam, BestelVelden.StoreTypeOrder, orderId, werknemerId);
    }

    public string Html(BestelMailGegevens g, string aanhef, FactuurAdres lev, FactuurAfzender a, string handtekening)
    {
        var en = g.Engels;
        string T(string nl, string eng) => en ? eng : nl;
        var cultuur = new CultureInfo(en ? "en-IE" : "nl-NL");
        string Geld(decimal w) => w.ToString("C2", cultuur);
        string Getal(double w) => w.ToString("#,0.###", cultuur);
        var po = $"PO-{g.OrderId}";

        var titel = T("Inkoopopdracht", "Purchase order");
        var sb = new StringBuilder();
        FactuurPdfService.Begin(sb, a, $"{titel} {po}", liggend: false, Voet(a, T("Btw", "VAT")));

        // ---- kop: logo en afzender ----
        sb.Append("<header class='kop'>");
        sb.Append(a.LogoDataUri.Length > 0 ? $"<img class='logo' src='{a.LogoDataUri}' alt=''>" : $"<div class='logo-tekst'>{H(a.Naam)}</div>");
        sb.Append("<div class='afzender'>");
        sb.Append($"<div class='afz-naam'>{H(a.Naam)}</div>");
        sb.Append($"<div>{H(a.Adres)}</div><div>{H(FactuurPdfService.Plaats(a.Postcode, a.Plaats))}</div>");
        foreach (var r in a.Regels) sb.Append($"<div>{H(r)}</div>");
        if (a.Email.Length > 0 && !a.Regels.Any(r => r.Contains('@'))) sb.Append($"<div>{H(a.Email)}</div>");
        sb.Append("</div></header>");

        // ---- leverancier en ordergegevens ----
        sb.Append("<section class='adresrij'><div class='adres'>");
        sb.Append($"<div class='klantnaam'>{H(lev.Naam)}</div>");
        if (lev.Tav.Length > 0) sb.Append($"<div>{T("t.a.v.", "Attn.")} {H(lev.Tav)}</div>");
        if (lev.Adres.Length > 0) sb.Append($"<div>{H(lev.Adres)}</div>");
        if ((lev.Postcode + lev.Plaats).Length > 0) sb.Append($"<div>{H(FactuurPdfService.Plaats(lev.Postcode, lev.Plaats.ToUpperInvariant()))}</div>");
        if (lev.Landnummer != 31 && lev.Land.Length > 0) sb.Append($"<div>{H(lev.Land.ToUpperInvariant())}</div>");
        sb.Append("</div><table class='meta'>");
        FactuurPdfService.Meta(sb, T("Onze referentie", "Our reference"), po, vet: true);
        FactuurPdfService.Meta(sb, T("Datum", "Date"), DateTime.Today.ToString("dd-MM-yyyy"));
        FactuurPdfService.Meta(sb, T("Contactpersoon", "Contact"), g.Afzender.Naam);
        sb.Append("</table></section>");

        sb.Append($"<div class='titelrij'><h1>{H(titel)}</h1></div>");

        // ---- aanhef en inleiding (zoals Access) ----
        sb.Append($"<p style='margin:6mm 0 0;'>{H(aanhef.TrimEnd(',', ' '))},</p>");
        sb.Append("<p style='margin:3mm 0 0;'>").Append(T(
            $"Graag bestellen wij onderstaande artikelen bij u onder vermelding van ordernummer <b>{po}</b>.",
            $"We would like to order the following items under purchase order <b>{po}</b>.")).Append("</p>");

        // ---- regels ----
        // negen kolommen passen net niet in de factuuropmaak: iets kleiner, en de eenheid achter het aantal (zoals Access)
        sb.Append("<style>.regels.po { font-size: 8.5pt; } .regels.po th, .regels.po td { padding-left: 1.4mm; padding-right: 1.4mm; }</style>");
        sb.Append("<table class='regels po'><thead><tr>");
        sb.Append($"<th class='r'>{T("Aantal", "Qty")}</th><th>{T("Artikel", "Item")}</th>");
        if (g.OcCodes) sb.Append("<th>OC</th>");
        sb.Append($"<th>{T("Omschrijving", "Description")}</th><th>{T("Leverdatum", "Delivery")}</th>");
        sb.Append($"<th class='r'>{T("Prijs/st", "Unit price")}</th><th class='r'>{T("Korting", "Discount")}</th><th class='r'>{T("Totaal", "Total")}</th>");
        sb.Append("</tr></thead><tbody>");
        decimal totaal = 0;
        foreach (var r in g.Regels)
        {
            var regel = (decimal)r.Aantal * r.Prijs * (1 - r.Korting);
            totaal += regel;
            sb.Append($"<tr><td class='r'>{Getal(r.Aantal)}{(r.Eenheid.Length > 0 ? " " + H(r.Eenheid) : "")}</td><td{(r.Artikel.Length <= 24 ? " class='nb'" : "")}><b>{H(r.Artikel)}</b></td>");
            if (g.OcCodes) sb.Append($"<td class='nb'>{H(r.Oc)}</td>");
            sb.Append($"<td>{H(r.Omschrijving)}</td><td class='nb'>{(r.Leverdatum is { } d ? d.ToString("dd-MM-yyyy") : "")}</td>");
            sb.Append($"<td class='r'>{Geld(r.Prijs)}</td><td class='r'>{(r.Korting == 0 ? "" : (r.Korting * 100).ToString("0.##", cultuur) + "%")}</td>");
            sb.Append($"<td class='r'>{Geld(regel)}</td></tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append("<table class='totalen'>");
        sb.Append($"<tr class='eind'><td>{T("Ordertotaal (excl. btw)", "Order total (excl. VAT)")}</td><td class='r'>{Geld(totaal)}</td></tr>");
        sb.Append("</table>");

        // ---- melding op het orderformulier, afleveradres, factuuradres ----
        const string blok = "margin-top:6mm;padding:3mm 4mm;background:#f5f5f5;border-left:3px solid var(--kleur);line-height:1.5;page-break-inside:avoid;";
        const string kopje = "font-weight:600;color:var(--kleur);margin-bottom:1mm;";
        if (g.Opmerking.Trim().Length > 0)
            sb.Append($"<div style='{blok}'><div style='{kopje}'>{T("Opmerking", "Remark")}</div>")
              .Append(H(g.Opmerking.Trim()).Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>")).Append("</div>");
        if (g.AfleverId is not null || g.Factuuradres.Length > 0)
        {
            sb.Append("<div style='display:flex;gap:6mm;page-break-inside:avoid;'>");
            if (g.AfleverId is not null)
            {
                sb.Append($"<div style='{blok}flex:1;'><div style='{kopje}'>{T("Afleveradres", "Delivery address")}</div>{H(g.AfleverNaam)}<br>");
                if (g.AfleverTav.Length > 0) sb.Append($"{T("t.a.v.", "Attn.")} {H(g.AfleverTav)}<br>");
                sb.Append($"{H(g.AfleverAdres)}<br>{H($"{g.AfleverPostcode} {g.AfleverPlaats}".Trim())}</div>");
            }
            if (g.Factuuradres.Length > 0)
                sb.Append($"<div style='{blok}flex:1;'><div style='{kopje}'>{T("Graag de factuur richten aan", "Please send the invoice to")}</div>")
                  .Append(H(g.Factuuradres).Replace("\n", "<br>")).Append("</div>");
            sb.Append("</div>");
        }

        // ---- groet en ondertekening ----
        sb.Append("<div style='margin-top:9mm;line-height:1.45;page-break-inside:avoid;'>");
        sb.Append($"<div>{T("Met vriendelijke groet,", "Kind regards,")}</div>");
        sb.Append($"<div style='font-weight:600;margin-top:1mm;'>{H(a.Naam.Length > 0 ? a.Naam : g.BvNaam)}</div>");
        sb.Append(handtekening.Length > 0
            ? $"<img src='{handtekening}' alt='' style='display:block;max-height:24mm;max-width:70mm;margin:2mm 0;'>"
            : "<div style='height:6mm;'></div>");
        if (g.Afzender.Naam.Length > 0) sb.Append($"<div>{H(g.Afzender.Naam)}</div>");
        if (g.Afzender.Functie.Length > 0) sb.Append($"<div>{H(g.Afzender.Functie)}</div>");
        if (g.Afzender.Mail.Length > 0) sb.Append($"<div>{H(g.Afzender.Mail)}</div>");
        if (g.Afzender.Mobiel.Length > 0) sb.Append($"<div>{H(g.Afzender.Mobiel)}</div>");
        sb.Append("</div>");

        // ---- voorwaarden ----
        var voorwaarden = _config[en ? "Bestellingen:InkoopVoorwaardenEn" : "Bestellingen:InkoopVoorwaarden"] is { Length: > 0 } eigen
            ? eigen.Trim()
            : en ? VoorwaardenEn : VoorwaardenNl;
        if (voorwaarden.Length > 0)
            sb.Append($"<div style='margin-top:9mm;font-size:7.5pt;line-height:1.45;color:#777;page-break-inside:avoid;'>{H(voorwaarden)}</div>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Werknemers.Handtekening als data-URI. Access bewaarde het plaatje als OLE-object "Afbeelding": een kop, dan "DIB",
    /// breedte, hoogte en lengte, en daarna de bitmap zonder bestandskop. Die kop (14 bytes "BM…") komt er hier voor,
    /// zodat de browser hem als .bmp toont. Een gewoon PNG/JPG/BMP gaat ongewijzigd door; iets anders = geen plaatje.
    /// </summary>
    internal static string HandtekeningDataUri(byte[]? b)
    {
        if (b is null || b.Length < 60) return "";
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "data:image/png;base64," + Convert.ToBase64String(b);
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "data:image/jpeg;base64," + Convert.ToBase64String(b);
        if (b[0] == 0x42 && b[1] == 0x4D) return "data:image/bmp;base64," + Convert.ToBase64String(b);

        var merk = -1;
        for (var i = 0; i < Math.Min(b.Length - 60, 400); i++)
            if (b[i] == 0x44 && b[i + 1] == 0x49 && b[i + 2] == 0x42 && b[i + 3] == 0x00) { merk = i; break; }
        if (merk < 0) return "";
        var begin = merk + 16;                                        // "DIB\0" + breedte + hoogte + lengte
        var lengte = BitConverter.ToInt32(b, merk + 12);
        if (lengte < 40 || begin + lengte > b.Length) lengte = b.Length - begin;
        var kop = BitConverter.ToInt32(b, begin);                     // BITMAPINFOHEADER = 40 (of een nieuwere, grotere)
        if (kop is not (40 or 52 or 56 or 108 or 124) || lengte <= kop) return "";
        int bits = BitConverter.ToInt16(b, begin + 14), compressie = BitConverter.ToInt32(b, begin + 16), kleuren = BitConverter.ToInt32(b, begin + 32);
        var extra = kop == 40 && compressie == 3 ? 12 : 0;            // BI_BITFIELDS: drie maskers na de kop
        if (bits <= 8) extra += (kleuren > 0 ? kleuren : 1 << bits) * 4;
        var pixels = 14 + kop + extra;
        if (pixels >= 14 + lengte) return "";

        var bmp = new byte[14 + lengte];
        bmp[0] = 0x42; bmp[1] = 0x4D;
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(pixels).CopyTo(bmp, 10);
        Buffer.BlockCopy(b, begin, bmp, 14, lengte);
        return "data:image/bmp;base64," + Convert.ToBase64String(bmp);
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
