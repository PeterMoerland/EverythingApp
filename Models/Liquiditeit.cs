namespace EverythingApp.Models;

/// <summary>Een eigen BV voor de keuzeknoppen. MdeBv = telt standaard mee (Relations.MDEBV).</summary>
public record LiquiditeitBv(int Id, string Afkorting, string Naam, bool MdeBv);

/// <summary>Het laatste saldo van één bankrekening.</summary>
public record Banksaldo(string Rekening, string Omschrijving, int BvId, decimal Saldo, DateOnly? Datum);

/// <summary>Een openstaande factuur: verkoop (geld komt binnen) of inkoop (geld gaat eruit).</summary>
public record OpenstaandeFactuur(int Id, bool Verkoop, string Nummer, string Relatie, int BvId, string Bv,
                                 DateOnly Vervaldatum, decimal Bedrag, bool Verstuurd);

/// <summary>
/// Eén verwachte geldbeweging. Bedrag positief = erbij, negatief = eraf.
/// Relatie = klant, leverancier, of de naam van de vaste last; Kenmerk = factuurnummer of soort.
/// </summary>
public record LiquiditeitPost(DateOnly Datum, string Soort, string Relatie, string Kenmerk, decimal Bedrag,
                              int? FactuurId = null, bool Verkoop = false, int? VasteLastId = null);

/// <summary>Eén dag in de prognose.</summary>
public record LiquiditeitDag(DateOnly Datum, decimal Erbij, decimal Eraf, decimal Saldo,
                             decimal SaldoNaTeLaatLeveranciers, decimal SaldoInclTeLaatKlanten);

/// <summary>De hele prognose, zoals het scherm hem toont.</summary>
public class LiquiditeitPrognose
{
    /// <summary>Datum van het banksaldo: de laatste dag met banktransacties. Daarna begint de prognose.</summary>
    public DateOnly Start { get; set; }
    public decimal Beginsaldo { get; set; }
    public List<Banksaldo> Rekeningen { get; set; } = new();

    /// <summary>Al vervallen op de startdatum: apart, niet in de dagen.</summary>
    public List<OpenstaandeFactuur> TeLaatKlanten { get; set; } = new();
    public List<OpenstaandeFactuur> TeLaatLeveranciers { get; set; } = new();
    public decimal TeLaatVanKlanten => TeLaatKlanten.Sum(f => f.Bedrag);
    public decimal TeLaatAanLeveranciers => TeLaatLeveranciers.Sum(f => f.Bedrag);

    public List<LiquiditeitPost> Posten { get; set; } = new();
    public List<LiquiditeitDag> Dagen { get; set; } = new();

    /// <summary>Het saldo aan het eind van de dag die zoveel dagen na de start ligt.</summary>
    public LiquiditeitDag? Na(int dagen) => Dagen.FirstOrDefault(d => d.Datum == Start.AddDays(dagen)) ?? Dagen.LastOrDefault();
}
