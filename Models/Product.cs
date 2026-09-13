namespace EverythingApp.Models;

/// <summary>Een producttype uit ProductNames — 'MPR-1 · M-Pede robot type 1'.</summary>
public record ProductNaam(int Id, string Naam, string Omschrijving, string TypeCode, int? TypeId)
{
    public string Volledig => string.IsNullOrWhiteSpace(Omschrijving) ? Naam : $"{Naam} · {Omschrijving}";
}

/// <summary>Eén exemplaar uit Producten, zoals het in de serienummerlijst staat.</summary>
public record ProductRegel(int Id, string Serienummer, string Klant, string StatusKort,
                           string Opmerking, bool Gewist);

/// <summary>Een regel uit ProductHistorie.</summary>
public record ProductGebeurtenis(int Id, string Event, string Soort, DateTime? Wanneer, string Door);

/// <summary>
/// Een regel in de samenstelling: een product dat in een ander product zit.
/// Zowel gebruikt voor 'Gebruikt' (de onderdelen) als voor 'Wordt gebruikt in'.
/// </summary>
public record SamenstellingRegel(int ProductId, int ProductNameId, string Code, string Serienummer,
                                 string Omschrijving, string TypeCode);

/// <summary>
/// Een attribuut. Dezelfde vorm voor beide tabellen: bij een producttype komt het
/// uit ProductNameAttributes (dan is Waarde leeg en telt VraagBijAanmaak), bij een
/// exemplaar uit ProductAttributes (dan staat er een waarde in).
/// </summary>
public class ProductAttribuut
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Attribuut { get; set; } = "";
    public string Waarde { get; set; } = "";
    public string Eenheid { get; set; } = "";
    public bool Verplicht { get; set; }
    public bool VraagBijAanmaak { get; set; }
    public string Opmerking { get; set; } = "";
}
