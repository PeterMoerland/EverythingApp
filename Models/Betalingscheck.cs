namespace EverythingApp.Models;

/// <summary>Een openstaande uitgaande factuur, zoals de betalingscheck hem nodig heeft.</summary>
public record OpenFactuur(int Id, string Nummer, int BvId, string Bv, string Klant, DateOnly Datum, decimal Bedrag);

/// <summary>Een ontvangst op een rekening van een eigen BV die nog aan geen uitgaande factuur hangt.</summary>
public record Ontvangst(int Id, int BvId, DateOnly Datum, decimal Bedrag, string Tegenpartij, string Omschrijving);

public enum VoorstelSoort
{
    /// <summary>Factuurnummer(s) in de omschrijving en het bedrag klopt.</summary>
    Zeker,
    /// <summary>Meerdere betalingen met hetzelfde factuurnummer die samen het bedrag vormen.</summary>
    Deelbetalingen,
    /// <summary>Geen nummer, wel precies het bedrag van één openstaande factuur van die BV.</summary>
    AlleenBedrag,
    /// <summary>Nummer gevonden, maar het bedrag klopt niet: zelf bekijken.</summary>
    Afwijkend,
}

/// <summary>Een voorgestelde koppeling: één of meer ontvangsten tegen één of meer facturen.</summary>
public class BetalingVoorstel
{
    public VoorstelSoort Soort { get; set; }
    public List<Ontvangst> Ontvangsten { get; set; } = new();
    public List<OpenFactuur> Facturen { get; set; } = new();
    public string Uitleg { get; set; } = "";

    /// <summary>Wordt verwerkt als je op Verwerken drukt. Standaard alleen de zekere.</summary>
    public bool Aangevinkt { get; set; }

    public decimal Ontvangen => Ontvangsten.Sum(o => o.Bedrag);
    public decimal Gefactureerd => Facturen.Sum(f => f.Bedrag);
    public decimal Verschil => Ontvangen - Gefactureerd;

    /// <summary>Afwijkend is alleen informatie; dat koppel je in Banktransacties of met de hand.</summary>
    public bool KanVerwerken => Soort != VoorstelSoort.Afwijkend;
}
