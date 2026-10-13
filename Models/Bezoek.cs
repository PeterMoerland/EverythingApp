namespace EverythingApp.Models;

/// <summary>
/// Een verslag van een contactmoment met een relatie (tabel Bezoekverslagen, Bezoekverslagen_MariaDB.sql).
/// </summary>
public class Bezoek
{
    public int Id { get; set; }
    public DateTime Datum { get; set; } = DateTime.Today.AddHours(DateTime.Now.Hour);
    public string Titel { get; set; } = "";
    public string Soort { get; set; } = BezoekSoort.BijRelatie;
    public int? RelatieId { get; set; }
    public string Relatie { get; set; } = "";
    public int? ProjectId { get; set; }
    public string Project { get; set; } = "";          // "1234 · omschrijving"
    public string Locatie { get; set; } = "";
    public string Verslag { get; set; } = "";
    public DateOnly? Vervolg { get; set; }
    public bool Vertrouwelijk { get; set; }
    public int? AangemaaktDoorId { get; set; }
    public string AangemaaktDoor { get; set; } = "";
    public DateTime? AangemaaktOp { get; set; }
    public string GewijzigdDoor { get; set; } = "";
    public DateTime? GewijzigdOp { get; set; }

    public List<BezoekDeelnemer> Deelnemers { get; set; } = new();
    public List<BezoekActie> Acties { get; set; } = new();

    // voor de lijst
    public int AantalBijlagen { get; set; }

    public IEnumerable<BezoekDeelnemer> Collegas => Deelnemers.Where(d => d.WerknemerId is not null);
    public IEnumerable<BezoekDeelnemer> Gasten => Deelnemers.Where(d => d.WerknemerId is null);
}

/// <summary>Een collega (WerknemerId), een contactpersoon van de relatie (ContactId) of iemand anders (alleen Naam).</summary>
public class BezoekDeelnemer
{
    public int Id { get; set; }
    public int? WerknemerId { get; set; }
    public int? ContactId { get; set; }
    public string Naam { get; set; } = "";
}

public class BezoekActie
{
    public int Id { get; set; }
    public int BezoekId { get; set; }
    public string Omschrijving { get; set; } = "";
    public int? WerknemerId { get; set; }
    public string Wie { get; set; } = "";
    public DateOnly? Deadline { get; set; }
    public bool Gereed { get; set; }
    public DateTime? GereedOp { get; set; }

    // voor het overzicht van open acties
    public string BezoekTitel { get; set; } = "";
    public DateTime? BezoekDatum { get; set; }
    public string Relatie { get; set; } = "";
}

public static class BezoekSoort
{
    public const string BijRelatie = "Bij relatie";
    public const string BijMde = "Bij MDE";
    public const string Online = "Online";
    public const string Telefonisch = "Telefonisch";
    public const string Beurs = "Beurs of evenement";

    public static readonly string[] Alle = { BijRelatie, BijMde, Online, Telefonisch, Beurs };

    public static string Pictogram(string soort) => soort switch
    {
        BijRelatie => "🚗",
        BijMde => "🏢",
        Online => "💻",
        Telefonisch => "📞",
        Beurs => "🎪",
        _ => "•",
    };
}

public class BezoekFilter
{
    public string Zoek { get; set; } = "";
    public string Soort { get; set; } = "";
    public int? RelatieId { get; set; }
    public int? WerknemerId { get; set; }          // deelnemer of maker
    public DateOnly? Van { get; set; }
    public DateOnly? Tot { get; set; }
}
