using System.Globalization;
using System.Text;
using System.Xml;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// SEPA-overboekingen als pain.001.001.03-bestand, zoals ExportBetalingen_Click in het Access-formulier
/// BetalingenKlaarzetten: per BV één bestand, één PmtInf met uitvoerdatum vandaag, per factuur een
/// CdtTrfTxInf met EndToEndId "non ref". Los van het scherm, zodat dit later vervangen kan worden door
/// het aanbieden via de betaal-API van de Rabobank.
/// </summary>
public static class Sepa
{
    /// <summary>
    /// Maakt het bestand. <paramref name="regels"/> zijn de te betalen facturen van deze BV; ze moeten
    /// volledig zijn (zie <see cref="BetaalRegel.Ontbreekt"/>).
    /// </summary>
    public static string Maak(BetaalBv bv, IReadOnlyList<BetaalRegel> regels, DateTime nu)
    {
        if (regels.Count == 0) throw new ArgumentException("Geen betalingen.");

        var totaal = regels.Sum(r => r.Bedrag);
        var stempel = nu.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        // MsgId en PmtInfId mogen maximaal 35 tekens zijn
        var naam = Kort(new string(bv.Naam.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray()), 35 - stempel.Length - 5);

        var sb = new StringBuilder();
        var instellingen = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), OmitXmlDeclaration = true };
        using (var w = XmlWriter.Create(sb, instellingen))
        {
            const string ns = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.03";
            w.WriteStartElement("Document", ns);
            w.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
            w.WriteStartElement("CstmrCdtTrfInitn");

            w.WriteStartElement("GrpHdr");
            w.WriteElementString("MsgId", naam + stempel + "DWTCH");
            w.WriteElementString("CreDtTm", nu.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
            w.WriteElementString("NbOfTxs", regels.Count.ToString(CultureInfo.InvariantCulture));
            w.WriteElementString("CtrlSum", Geld(totaal));
            w.WriteStartElement("InitgPty");
            w.WriteElementString("Nm", Tekst(bv.Naam.ToUpperInvariant(), 70));
            w.WriteEndElement();
            w.WriteEndElement(); // GrpHdr

            w.WriteStartElement("PmtInf");
            w.WriteElementString("PmtInfId", naam + stempel + "DPJYR");
            w.WriteElementString("PmtMtd", "TRF");
            w.WriteElementString("BtchBookg", "false");
            w.WriteElementString("NbOfTxs", regels.Count.ToString(CultureInfo.InvariantCulture));
            w.WriteElementString("CtrlSum", Geld(totaal));
            w.WriteElementString("ReqdExctnDt", nu.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            w.WriteStartElement("Dbtr");
            w.WriteElementString("Nm", Tekst(bv.Naam.ToUpperInvariant(), 70));
            w.WriteEndElement();
            w.WriteStartElement("DbtrAcct"); w.WriteStartElement("Id");
            w.WriteElementString("IBAN", Schoon(bv.Iban));
            w.WriteEndElement(); w.WriteEndElement();
            w.WriteStartElement("DbtrAgt"); w.WriteStartElement("FinInstnId");
            w.WriteElementString("BIC", Schoon(bv.Bic));
            w.WriteEndElement(); w.WriteEndElement();

            foreach (var r in regels)
            {
                w.WriteStartElement("CdtTrfTxInf");
                w.WriteStartElement("PmtId");
                w.WriteElementString("EndToEndId", "non ref");
                w.WriteEndElement();
                w.WriteStartElement("Amt");
                w.WriteStartElement("InstdAmt");
                w.WriteAttributeString("Ccy", "EUR");
                w.WriteString(Geld(r.Bedrag));
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteElementString("ChrgBr", "SLEV");
                w.WriteStartElement("CdtrAgt"); w.WriteStartElement("FinInstnId");
                w.WriteElementString("BIC", Schoon(r.Bic));
                w.WriteEndElement(); w.WriteEndElement();
                w.WriteStartElement("Cdtr");
                w.WriteElementString("Nm", Tekst(r.Tenaamstelling, 70));
                w.WriteEndElement();
                w.WriteStartElement("CdtrAcct"); w.WriteStartElement("Id");
                w.WriteElementString("IBAN", Schoon(r.Iban));
                w.WriteEndElement(); w.WriteEndElement();
                w.WriteStartElement("RmtInf");
                w.WriteElementString("Ustrd", Tekst(r.Omschrijving.ToUpperInvariant(), 140));
                w.WriteEndElement();
                w.WriteEndElement(); // CdtTrfTxInf
            }

            w.WriteEndElement(); // PmtInf
            w.WriteEndElement(); // CstmrCdtTrfInitn
            w.WriteEndElement(); // Document
        }
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" + sb + "\r\n";
    }

    /// <summary>Bestandsnaam zoals Access: AFK-SEPA2026-10-13-14-05-09 (PM).XML</summary>
    public static string Bestandsnaam(BetaalBv bv, DateTime nu, string medewerker)
        => $"{bv.Afkorting}-SEPA{nu:yyyy-MM-dd-HH-mm-ss} ({medewerker}).XML".ToUpperInvariant();

    private static string Geld(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Schoon(string s) => s.Replace(" ", "").Trim().ToUpperInvariant();

    private static string Kort(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>
    /// Alleen tekens die SEPA toestaat: Access verving &amp; door + en haalde de trema's weg; wat verder
    /// niet in de SEPA-tekenset zit wordt een spatie.
    /// </summary>
    public static string Tekst(string s, int max)
    {
        s = (s ?? "").Replace("&", "+").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsAsciiLetterOrDigit(c) || "/-?:().,'+ ".Contains(c) ? c : ' ');
        }
        var t = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return Kort(t, max);
    }

    // IBAN-lengtes per land (de landen die bij MDE voorkomen plus de rest van de SEPA-zone)
    private static readonly Dictionary<string, int> IbanLengte = new()
    {
        ["NL"] = 18, ["BE"] = 16, ["DE"] = 22, ["FR"] = 27, ["GB"] = 22, ["LU"] = 20, ["AT"] = 20,
        ["CH"] = 21, ["IT"] = 27, ["ES"] = 24, ["PT"] = 25, ["IE"] = 22, ["DK"] = 18, ["SE"] = 24,
        ["NO"] = 15, ["FI"] = 18, ["PL"] = 28, ["CZ"] = 24, ["SK"] = 24, ["HU"] = 28, ["SI"] = 19,
        ["HR"] = 21, ["RO"] = 24, ["BG"] = 22, ["GR"] = 27, ["CY"] = 28, ["MT"] = 31, ["EE"] = 20,
        ["LV"] = 21, ["LT"] = 20, ["IS"] = 26, ["LI"] = 21, ["MC"] = 27, ["SM"] = 27, ["AD"] = 24,
        ["VA"] = 22, ["GI"] = 23,
    };

    /// <summary>IBAN-controle zoals ValidateIban in Access: tekens, lengte per land en de 97-proef.</summary>
    public static bool IbanKlopt(string? iban)
    {
        var s = (iban ?? "").Replace(" ", "").ToUpperInvariant();
        if (s.Length < 15 || s.Length > 34 || !s.All(char.IsAsciiLetterOrDigit)) return false;
        if (IbanLengte.TryGetValue(s[..2], out var lengte) && lengte != s.Length) return false;

        var rest = 0;
        foreach (var c in s[4..] + s[..4])
        {
            var w = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            rest = w < 10 ? (rest * 10 + w) % 97 : (rest * 100 + w) % 97;
        }
        return rest == 1;
    }

    /// <summary>BIC: 8 of 11 tekens, eerste 6 letters.</summary>
    public static bool BicKlopt(string? bic)
    {
        var s = (bic ?? "").Replace(" ", "").ToUpperInvariant();
        return (s.Length == 8 || s.Length == 11) && s[..6].All(char.IsAsciiLetter) && s.All(char.IsAsciiLetterOrDigit);
    }
}
