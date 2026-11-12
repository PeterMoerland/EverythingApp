namespace EverythingApp.Models;

/// <summary>Eén stap in de opbouwvolgorde van een producttype (tab Opbouw).</summary>
public class OpbouwStap
{
    public int Id { get; set; }
    public int ProductNameId { get; set; }
    public int Volgorde { get; set; }
    /// <summary>OpbouwSoort.Opbouw of OpbouwSoort.Controle.</summary>
    public string Soort { get; set; } = OpbouwSoort.Opbouw;
    /// <summary>De fase waar de stap bij hoort (ProductFases_MariaDB.sql); null = geen fase.</summary>
    public int? FaseId { get; set; }
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

/// <summary>
/// Een opbouwfase van een producttype (ProductFases_MariaDB.sql, voorstel Sjoerd Quist 11-2026). Bij een exemplaar ook de
/// stand: klaar of overgeslagen, door wie en wanneer. De eerste fase zonder stand is de actieve fase.
/// </summary>
public class ProductFase
{
    public int Id { get; set; }
    public int ProductNameId { get; set; }
    public int Volgorde { get; set; }
    public string Naam { get; set; } = "";
    public string Omschrijving { get; set; } = "";

    // Bij een exemplaar
    /// <summary>FaseStand.Klaar, FaseStand.Overgeslagen of leeg (nog niet af).</summary>
    public string Stand { get; set; } = "";
    public DateTime? Op { get; set; }
    public string Door { get; set; } = "";
    public string Opmerking { get; set; } = "";

    public bool Af => Stand.Length > 0;
}

public static class FaseStand
{
    public const string Klaar = "klaar";
    public const string Overgeslagen = "overgeslagen";
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
