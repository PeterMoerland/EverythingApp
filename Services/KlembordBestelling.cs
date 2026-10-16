using System.Globalization;

namespace EverythingApp.Services;

/// <summary>Eén geplakte regel, zoals hij op het klembord stond (nog niet opgezocht).</summary>
public record PlakInvoer(double Aantal, string Nummer, string Omschrijving, string Ean, decimal? Bruto, decimal? Netto);

/// <summary>
/// Leest een tabel met tabs van het klembord (Bestellingen ▸ Plakken uit klembord). Vervangt het lezen in
/// PasteFromClipboard_Click uit het Access-formulier "Order sub form".
///
/// Bronnen: de Inventor-add-in (Collect Parts ▸ stuklijst naar klembord, BomExport: kopregel
/// "Aantal[tab]Artikelnummer[tab]Omschrijving"), maar ook Excel of een offerte van een leverancier. De kolommen worden
/// herkend aan de kopwoorden; die stonden in Access in de lokale tabel General_Settings (Keyword…) en staan nu hier.
/// </summary>
public static class KlembordBestelling
{
    public static readonly string[] KopArtikelnummer = { "PARTNUMMER", "ARTIKELNUMMER", "PART NUMBER", "ARTIKEL", "ARTIKELCODE", "ARTNO", "PARTNUMBER" };
    public static readonly string[] KopOmschrijving = { "DESCRIPTION", "OMSCHRIJVING" };
    public static readonly string[] KopAantal = { "QTY", "AANTAL", "QUANTITY" };
    public static readonly string[] KopEan = { "EAN", "BARCODE" };
    public static readonly string[] KopStukprijs = { "BRUTO PRIJS", "STUKPRIJS", "BRUTO" };
    public static readonly string[] KopNettoprijs = { "NETTO PRIJS", "NETTO" };

    /// <summary>De regels, of een foutmelding als de kopregel niet klopt.</summary>
    public static (List<PlakInvoer> Regels, string? Fout) Lees(string? tekst)
    {
        var regels = new List<PlakInvoer>();
        if (string.IsNullOrWhiteSpace(tekst)) return (regels, "Het klembord is leeg.");

        var lijnen = tekst.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                          .Where(l => l.Trim().Length > 0).ToList();
        if (lijnen.Count == 0) return (regels, "Het klembord is leeg.");

        var kop = lijnen[0].Split('\t').Select(k => k.Trim().TrimEnd(':').ToUpperInvariant()).ToArray();
        int Kolom(string[] woorden) => Array.FindIndex(kop, k => k.Length > 0 && woorden.Contains(k));

        var nr = Kolom(KopArtikelnummer);
        var oms = Kolom(KopOmschrijving);
        var aantal = Kolom(KopAantal);
        var ean = Kolom(KopEan);
        var bruto = Kolom(KopStukprijs);
        var netto = Kolom(KopNettoprijs);

        if (nr < 0) return (regels, "Kop 'Artikelnummer' niet gevonden in de eerste regel van het klembord.");
        if (oms < 0) return (regels, "Kop 'Omschrijving' niet gevonden in de eerste regel van het klembord.");
        if (aantal < 0) return (regels, "Kop 'Aantal' niet gevonden in de eerste regel van het klembord.");
        if (lijnen.Count < 2) return (regels, "Er staat alleen een kopregel op het klembord, geen onderdelen.");

        foreach (var lijn in lijnen.Skip(1))
        {
            var veld = lijn.Split('\t');
            string V(int i) => i >= 0 && i < veld.Length ? veld[i].Trim() : "";

            // zoals Access: een regel zonder aantal (of 0) doet niet mee
            if (Getal(V(aantal)) is not decimal a || a == 0) continue;
            regels.Add(new PlakInvoer((double)a, V(nr), V(oms), V(ean), Getal(V(bruto)), Getal(V(netto))));
        }
        if (regels.Count == 0) return (regels, "Geen regels met een aantal gevonden.");
        return (regels, null);
    }

    /// <summary>
    /// Een getal uit een cel: "2", "2,5", "1.234,50", "€ 12.50", "3 st". Komma én punt: de laatste is het
    /// decimaalteken. Alleen een komma: decimaalteken. Null als er geen getal in staat.
    /// </summary>
    public static decimal? Getal(string? tekst)
    {
        if (string.IsNullOrWhiteSpace(tekst)) return null;
        var t = new string(tekst.Trim().TakeWhile(c => char.IsDigit(c) || c is ',' or '.' or '-' or '€' or ' ').ToArray())
                    .Replace("€", "").Replace(" ", "");
        if (t.Length == 0 || !t.Any(char.IsDigit)) return null;
        int komma = t.LastIndexOf(','), punt = t.LastIndexOf('.');
        if (komma >= 0 && punt >= 0)
            t = komma > punt ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", "");
        else if (komma >= 0)
            t = t.Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>Zoals de view @ArtikelSearchlist (kolom Filtered): hoofdletters, zonder spaties, streepjes en punten.</summary>
    public static string Gefilterd(string nummer)
        => nummer.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();

    /// <summary>
    /// Schatting per stuk als er geen prijs is (GetSchatting in Access): de schattingsprijs van het artikel, anders
    /// gewicht × kiloprijs van de leverancier voor ALU, RVS of staal (bij meer treffers telt de laatste, zoals in
    /// Access); daarna de instelkosten verdeeld over het aantal, afgekapt op 3 decimalen.
    /// </summary>
    public static decimal Schatting(decimal schattingArtikel, string? materiaal, decimal gewicht,
                                    decimal kiloAlu, decimal kiloRvs, decimal kiloStaal, decimal instelkosten, double aantal)
    {
        decimal stuk = 0;
        if (schattingArtikel > 0) stuk = schattingArtikel;
        else
        {
            var m = (materiaal ?? "").ToUpperInvariant();
            if (m.Contains("ALU")) stuk = kiloAlu * gewicht;
            if (m.Contains("RVS") || m.Contains("STAIN")) stuk = kiloRvs * gewicht;
            if (m.Contains("STAAL") || m.Contains("STEEL")) stuk = kiloStaal * gewicht;
        }
        if (aantal > 0) stuk += instelkosten / (decimal)aantal;
        return Math.Truncate(stuk * 1000) / 1000;
    }
}
