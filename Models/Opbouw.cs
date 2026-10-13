namespace EverythingApp.Models;

/// <summary>Eén stap in de opbouwvolgorde van een producttype (tab Opbouw).</summary>
public class OpbouwStap
{
    public int Id { get; set; }
    public int ProductNameId { get; set; }
    public int Volgorde { get; set; }
    /// <summary>OpbouwSoort.Opbouw of OpbouwSoort.Controle.</summary>
    public string Soort { get; set; } = OpbouwSoort.Opbouw;
    public string Naam { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public bool Verplicht { get; set; } = true;
    public int AantalDocumenten { get; set; }

    // Bij een exemplaar: de status van deze stap.
    public bool Klaar { get; set; }
    public DateTime? KlaarOp { get; set; }
    public string KlaarDoor { get; set; } = "";
    public string Opmerking { get; set; } = "";
}

public static class OpbouwSoort
{
    public const string Opbouw = "opbouw";
    public const string Controle = "controle";
}

public static class OpbouwStore
{
    /// <summary>FileStore.StoreType voor hands-on documenten bij een opbouwstap (MasterID = OpbouwStapID).</summary>
    public const int Stap = 330;
}
