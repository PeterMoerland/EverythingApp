namespace EverythingApp.Models;

/// <summary>
/// Eén standaardcomponent van Eurocircuits (weerstand of condensator),
/// zoals opgeslagen in de tabel EurocircuitsComponenten. Het Gpn is de
/// unieke sleutel; WaardeBasis is de waarde in ohm of farad zodat er
/// te sorteren valt, WaardeTekst is de schrijfwijze uit de export.
/// </summary>
public class EcComponent
{
    public int Id { get; set; }
    public string Gpn { get; set; } = "";
    public char Soort { get; set; }              // 'R' of 'C'
    public string Grootte { get; set; } = "";    // 0402, 0603, ...
    public double WaardeBasis { get; set; }
    public string WaardeTekst { get; set; } = "";
    public string? Tolerantie { get; set; }
    public string? Vermogen { get; set; }
    public string? Spanning { get; set; }
    public string? Dielectricum { get; set; }
    public string? Omschrijving { get; set; }

    public string SoortNaam => Soort switch
    {
        'R' => "Weerstand",
        'C' => "Condensator",
        _ => Soort.ToString(),
    };
}
