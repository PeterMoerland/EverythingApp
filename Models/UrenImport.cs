namespace EverythingApp.Models;

/// <summary>Welke uren er op een factuur moeten: klant, of project, of een paar werkorders.</summary>
public class UrenSelectie
{
    public int KlantId { get; set; }

    /// <summary>Null = alle projecten van de klant.</summary>
    public int? ProjectId { get; set; }

    /// <summary>Leeg = alle werkorders (van het project, of van de klant).</summary>
    public List<int> WerkbonIds { get; set; } = new();

    public DateOnly Van { get; set; }
    public DateOnly Tot { get; set; }
}

public record ProjectKeuze(int Id, string Nummer, string Omschrijving);

public record WerkorderKeuzeUren(int WerkbonId, string Nummer, string Omschrijving, string Soort);

/// <summary>Eén regel uit Uren, met wat nodig is om het tarief te rekenen en te factureren.</summary>
public class UurRegel
{
    public int UrenId { get; set; }
    public DateOnly Datum { get; set; }

    public int WerknemerId { get; set; }
    public string Werknemer { get; set; } = "";

    public int WerkbonId { get; set; }

    /// <summary>Projectnummer-werkbon, zoals Access: "226084-02".</summary>
    public string Wo { get; set; } = "";
    public string WerkorderOmschrijving { get; set; } = "";
    public int ProjectId { get; set; }
    public string ProjectOmschrijving { get; set; } = "";

    public int TariefsoortId { get; set; }
    public string Tariefsoort { get; set; } = "";

    public decimal Uren { get; set; }
    public decimal Kilometers { get; set; }
    public decimal VastBedrag { get; set; }

    /// <summary>0 = ex btw, 1 = incl 9%, 2 = incl 21%, 3 = één op één; null telt niet mee (zoals Access).</summary>
    public int? VastType { get; set; }

    /// <summary>Total_Amount en Total_Cost zoals ze nu in de database staan.</summary>
    public decimal Bedrag { get; set; }
    public decimal Kostprijs { get; set; }

    /// <summary>Id en nummer van de factuur waarop dit uur al gefactureerd of gereserveerd is.</summary>
    public int? GefactureerdOp { get; set; }
    public string GefactureerdNr { get; set; } = "";
    public int? GereserveerdOp { get; set; }
    public string GereserveerdNr { get; set; } = "";

    // ---- na het rekenen ----
    public decimal? NieuwBedrag { get; set; }
    public decimal? NieuweKostprijs { get; set; }

    /// <summary>Waarom het tarief niet gerekend kon worden; dan blijft het oude bedrag staan.</summary>
    public string Melding { get; set; } = "";

    public decimal BedragVoorFactuur => NieuwBedrag ?? Bedrag;
}

/// <summary>Een tariefafspraak met een klant (tabel Tariefafspraken).</summary>
public record TariefafspraakPeriode(DateOnly Start, DateOnly Stop, decimal Basis, decimal Reis, decimal Kilometer,
                             decimal? ProcentOverwerk, decimal? ProcentOverwerkZon);

/// <summary>Verkoopprijzen voor één werknemer op één werkorder (WorkorderTeam); 0 = niet ingevuld.</summary>
public record TeamTarief(decimal Werkuur, decimal Reisuur, decimal Kilometer);

/// <summary>Kostprijs van een werknemer in een periode (WerknemersTarieven).</summary>
public record Kostprijstarief(DateOnly Begin, DateOnly Eind, decimal? Werkuur, decimal? Reisuur, decimal? Kilometer);

/// <summary>Doorbelastpercentages van een werkorder.</summary>
public record Doorbelasting(decimal Uren, decimal Onkosten);
