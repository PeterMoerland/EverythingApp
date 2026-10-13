using EverythingApp.Services;

namespace EverythingApp.Models;

/// <summary>
/// Een goedgekeurde, nog niet betaalde inkomende factuur die klaargezet kan worden
/// (Access: query BetalingenKlaarzetten, tabel BankExport).
/// </summary>
public class BetaalRegel
{
    public int FactuurId { get; init; }                 // FacturenInkomend-ID
    public int RelatieId { get; init; }                 // leverancier (Relations.id_supplier)
    public string Leverancier { get; init; } = "";
    public string Factuurnummer { get; init; } = "";
    public DateTime? Factuurdatum { get; init; }
    public int Betalingstermijn { get; init; }
    public decimal Bedrag { get; init; }                 // incl. BTW, zoals Access: afgerond ex + afgerond BTW
    public bool OnHold { get; init; }
    public int BvId { get; init; }                       // FacturenInkomend.Admin
    public string Bv { get; init; } = "";                // afkorting van de BV

    // aanpasbaar op het scherm; IBAN en BIC gaan terug naar de relatie
    public string Iban { get; set; } = "";
    public string Bic { get; set; } = "";
    public string Tenaamstelling { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public bool Selectie { get; set; }

    public DateTime? Betaaldatum => Factuurdatum?.AddDays(Betalingstermijn);

    /// <summary>Dagen over de betaaldatum heen (negatief = nog zoveel dagen). Access: NogDagen.</summary>
    public int NogDagen => Betaaldatum is DateTime b ? (DateTime.Today - b.Date).Days : 0;

    public bool IsCredit => Bedrag <= 0;

    /// <summary>Wat er ontbreekt om te kunnen betalen; leeg = volledig.</summary>
    public List<string> Ontbreekt()
    {
        var lijst = new List<string>();
        if (string.IsNullOrWhiteSpace(Omschrijving)) lijst.Add("omschrijving");
        if (string.IsNullOrWhiteSpace(Iban)) lijst.Add("IBAN");
        else if (!Sepa.IbanKlopt(Iban)) lijst.Add("geldig IBAN");
        if (string.IsNullOrWhiteSpace(Bic)) lijst.Add("BIC");
        if (string.IsNullOrWhiteSpace(Tenaamstelling)) lijst.Add("tenaamstelling");
        return lijst;
    }

    public bool Volledig => Ontbreekt().Count == 0;
}

/// <summary>Een MDE-BV als betaler: naam, eigen rekening en BIC (uit Relations).</summary>
public record BetaalBv(int Id, string Naam, string Afkorting, string Iban, string Bic);
