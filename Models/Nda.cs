namespace EverythingApp.Models;

/// <summary>Een geheimhoudingsovereenkomst. Vervangt een regel uit nda_register.json.</summary>
public class Nda
{
    public int Id { get; set; }
    public int? RelatieId { get; set; }
    public int? ContactpersoonId { get; set; }
    public int? ProjectId { get; set; }

    /// <summary>Naam van de relatie zoals die nu in Relations staat (alleen om te tonen).</summary>
    public string RelatieNu { get; set; } = "";

    // momentopname zoals in het document
    public string RelatieNaam { get; set; } = "";
    public string Rechtsvorm { get; set; } = "B.V.";
    public string Adres { get; set; } = "";
    public string KvK { get; set; } = "";
    public string ToepasselijkRecht { get; set; } = "Nederlands";

    public string Projectomschrijving { get; set; } = "";
    public string Projectnummer { get; set; } = "";

    public DateOnly? DatumDocument { get; set; }
    public DateTime? DatumAangemaakt { get; set; }
    public int? AangemaaktDoor { get; set; }
    public DateTime? DatumVerzonden { get; set; }
    public DateTime? DatumOndertekend { get; set; }

    public DateOnly? Einddatum { get; set; }
    public bool Onbepaald { get; set; }

    public string Status { get; set; } = NdaStatus.Concept;
    public string Soort { get; set; } = "eigen";
    public string Notities { get; set; } = "";

    /// <summary>Bestanden bij deze NDA, nieuwste versie eerst.</summary>
    public List<NdaBestand> Bestanden { get; set; } = new();

    public bool Geimporteerd => string.Equals(Soort, "geimporteerd", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dagen tot de einddatum; null bij onbepaalde tijd of geen datum.</summary>
    public int? DagenTotEinde
    {
        get
        {
            if (Onbepaald || Einddatum is null) return null;
            return Einddatum.Value.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
        }
    }

    public bool Verlopen => DagenTotEinde is < 0;

    /// <summary>Hoogste versienummer dat al bestaat (v1 → 1).</summary>
    public int HoogsteVersie => Bestanden
        .Select(b => int.TryParse(b.Versie.TrimStart('v', 'V'), out var n) ? n : 0)
        .DefaultIfEmpty(0)
        .Max();
}

public class NdaBestand
{
    public int Id { get; set; }
    public int NdaId { get; set; }
    public string Versie { get; set; } = "";
    /// <summary>word, pdf of getekend.</summary>
    public string Soort { get; set; } = "word";
    public int? FileStoreId { get; set; }
    public DateTime? Datum { get; set; }
    public int? ToegevoegdDoor { get; set; }
    public string Notitie { get; set; } = "";

    /// <summary>Naam zoals in de filestore staat (voor het knopje).</summary>
    public string Bestandsnaam { get; set; } = "";
}

public static class NdaStatus
{
    public const string Concept = "Concept";
    public const string Verzonden = "Verzonden";
    public const string Ondertekend = "Ondertekend";
    public const string Vervallen = "Vervallen";

    public static readonly string[] Alle = { Concept, Verzonden, Ondertekend, Vervallen };
}
