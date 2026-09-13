namespace EverythingApp.Models;

/// <summary>Maps to SQL Server table [Uren] (columns taken from the Access front-end).</summary>
public class UrenEntry
{
    public int UrenId { get; set; }                  // [Uren-id]
    public DateOnly StartDatum { get; set; }
    public TimeOnly? StartTijd { get; set; }
    public DateOnly? EindDatum { get; set; }
    public TimeOnly? EindTijd { get; set; }
    public int WerknemerId { get; set; }             // [Werknemer-id]
    public decimal Uren { get; set; }
    public int? Werkorder { get; set; }
    public decimal Kilometers { get; set; }
    public bool PriveKMs { get; set; }
    public string Omschrijving { get; set; } = "";
    public int TariefsoortId { get; set; }           // [Tariefsoort-id]
    public decimal FixedAmount { get; set; }
    public int FixedTypeId { get; set; }             // [FixedType-ID]
    public bool Invoiced { get; set; }
    public int? UrenActiviteitId { get; set; }
    public int? SourceUrenId { get; set; }
    public bool GeneratedByRule { get; set; }
}

/// <summary>Maps to [Werknemers].</summary>
public class Werknemer
{
    public int WerknemerId { get; set; }             // [Werknemer-id]
    public string Naam { get; set; } = "";
    public bool Directie { get; set; }
    public int? Hoofdactiviteit { get; set; }
    public decimal WoonWerkKilometers { get; set; }
    public string TextWoonWerk { get; set; } = "Woon-werk";
    public string TextWerkWoon { get; set; } = "Werk-woon";
    public bool Actief { get; set; } = true;
}

/// <summary>Maps to [Workorders].</summary>
public class Workorder
{
    public int WerkbonId { get; set; }               // [Werkbon-id] — interne sleutel
    public string Omschrijving { get; set; } = "";
    public string Klant { get; set; } = "";
    public int WorkorderStatus { get; set; } = 1;    // 0 = gesloten/gefactureerd

    /// <summary>
    /// Werkordernummer zoals gebruikers het kennen: projectnummer-werkbonnummer,
    /// werkbonnummer met 2 posities. VBA: [projectnummer] &amp; "-" &amp; Right("00" &amp; [werkbonnummer], 2)
    /// </summary>
    public string Nummer { get; set; } = "";

    /// <summary>Projectnaam/-omschrijving uit Projects (Projectdescription).</summary>
    public string Project { get; set; } = "";

    /// <summary>Wat op het scherm komt: het werkordernummer, of de interne id als dat nummer ontbreekt.</summary>
    public string Weergave => string.IsNullOrWhiteSpace(Nummer) ? WerkbonId.ToString() : Nummer;

    /// <summary>Volledige regel voor keuzelijst en overzicht: nummer — project — werkbonomschrijving.</summary>
    public string VolledigeTekst
    {
        get
        {
            var delen = new List<string> { Weergave };
            if (!string.IsNullOrWhiteSpace(Project)) delen.Add(Project);
            if (!string.IsNullOrWhiteSpace(Omschrijving)) delen.Add(Omschrijving);
            return string.Join(" — ", delen);
        }
    }
}

/// <summary>Maps to [Tariefsoort]. In the VBA: 1 = Travel, 2 = Normaal, 5 = Fixed.</summary>
public class Tariefsoort
{
    public int TariefsoortId { get; set; }
    public string Naam { get; set; } = "";
}

/// <summary>Maps to [UrenActiviteit]. Id 13 = woon-werk verkeer (per VBA).</summary>
public class UrenActiviteit
{
    public int UrenActiviteitId { get; set; }
    public string Naam { get; set; } = "";
}

/// <summary>Maps to [GeneralSettingsSpecialOrders] ("Vrije uren inboeken").</summary>
public class SpecialOrder
{
    public int Id { get; set; }                      // GeneralSettingsSpecialOrders_id
    public int Order { get; set; }                   // workorder to book on
    public string Remark { get; set; } = "";
}

/// <summary>Maps to [General_settings] (only the parts this form needs).</summary>
public class GeneralSettings
{
    public int ManagementKilometerOrder { get; set; }
}

// ============================================================
//  Werknemersmodule: tijdlijn, relaties, documenten
// ============================================================

public class GebeurtenisSoort
{
    public int Id { get; set; }
    public string Naam { get; set; } = "";
    public string Groep { get; set; } = "Overig";
    public int WijzigtDienst { get; set; }       // 1 = in dienst, -1 = uit dienst
    public bool VraagtDocument { get; set; }
    public bool StandaardPrive { get; set; }
    public bool Jubileum { get; set; }
}

public class Gebeurtenis
{
    public int Id { get; set; }
    public int WerknemerId { get; set; }
    public int SoortId { get; set; }
    public string Soort { get; set; } = "";
    public string Groep { get; set; } = "";
    public DateTime Moment { get; set; } = DateTime.Today;
    public string Toelichting { get; set; } = "";
    public int? FunctieId { get; set; }
    public decimal? Contracturen { get; set; }
    public int? WerkzaamBij { get; set; }
    public decimal? Bedrag { get; set; }
    public DateOnly? TotDatum { get; set; }
    public bool Prive { get; set; }
    public int AantalDocumenten { get; set; }

    // arbeidsvoorwaarden zoals ze vanaf dit moment gelden
    public decimal? BrutoSalaris { get; set; }
    public bool? DertiendeMaand { get; set; }
    public decimal? VrijeDagen { get; set; }
    public decimal? AtvDagen { get; set; }
}

public class RelatieSoort
{
    public int Id { get; set; }
    public string Naam { get; set; } = "";
}

public class Relatie
{
    public int Id { get; set; }
    public int WerknemerId { get; set; }
    public int SoortId { get; set; }
    public string Soort { get; set; } = "";
    public string Naam { get; set; } = "";
    public DateOnly? Geboortedatum { get; set; }
    public string Telefoon { get; set; } = "";
    public string Opmerking { get; set; } = "";
    public bool Noodcontact { get; set; }
}

/// <summary>Tariefafspraak met een klant, geldig in een periode.</summary>
public class Tariefafspraak
{
    public int Id { get; set; }
    public int RelatieId { get; set; }
    public int? ContactpersoonId { get; set; }
    public int? FunctieId { get; set; }
    public DateOnly? Van { get; set; }
    public DateOnly? Tot { get; set; }

    public decimal? Basistarief { get; set; }
    public decimal? LaagTarief { get; set; }
    public decimal? MinimumTarief { get; set; }
    public decimal? Reistarief { get; set; }        // Travel
    public decimal? Kilometer { get; set; }

    public decimal? OverwerkBasis { get; set; }     // Procent Overwerk Basis
    public decimal? OverwerkZon { get; set; }       // Procent Overwerk Zon
    public decimal? Buitenland { get; set; }        // Procent Buitenland

    public int? Betalingstermijn { get; set; }
    public decimal? ToeslagUren { get; set; }       // DefaultToeslagHours
    public decimal? ToeslagExpenses { get; set; }   // DefaultToeslagExpenses
    public decimal? ToeslagMateriaal { get; set; }  // DefaultToeslagMaterials

    public string Opmerking { get; set; } = "";

    /// <summary>Geldt deze afspraak vandaag?</summary>
    public bool IsActueel
    {
        get
        {
            var vandaag = DateOnly.FromDateTime(DateTime.Today);
            return (Van is null || Van <= vandaag) && (Tot is null || Tot >= vandaag);
        }
    }
}

/// <summary>Contactpersoon bij een relatie (tabel Contacts).</summary>
public class Contactpersoon
{
    public int Id { get; set; }                  // ID_contact
    public int RelatieId { get; set; }           // id_supplier
    public string Naam { get; set; } = "";
    public string Titel { get; set; } = "";      // Title (Dhr./Mevr.)
    public string Afdeling { get; set; } = "";
    public string Functie { get; set; } = "";    // Funktie
    public string Telefoon { get; set; } = "";
    public string Mobiel { get; set; } = "";
    public string Fax { get; set; } = "";
    public string Mail { get; set; } = "";
    public string Aanhef { get; set; } = "";
    public string Opmerking { get; set; } = "";
    public bool GeenPersoon { get; set; }        // NotAPerson
    public bool UitDienst { get; set; }          // OutOfCompany
}

/// <summary>Geboortedatum en datum in dienst van een werknemer, voor de kalenders.</summary>
public class Mijlpalen
{
    public int WerknemerId { get; set; }
    public string Naam { get; set; } = "";
    public DateOnly? Geboortedatum { get; set; }
    public DateOnly? InDienst { get; set; }
}

/// <summary>Functie uit Werknemers_functie, met tarieven en gebruik.</summary>
public class Functie
{
    public int Id { get; set; }
    public string Naam { get; set; } = "";
    public decimal? InternTarief { get; set; }
    public decimal? ExternTarief { get; set; }
    public decimal? KostTarief { get; set; }
    public int AantalWerknemers { get; set; }
}

/// <summary>Pas van een werknemer (laadpas, betaalpas, creditcard).</summary>
public class Pas
{
    public int Id { get; set; }
    public int WerknemerId { get; set; }
    public int PasTypeId { get; set; }
    public int? BankrekeningId { get; set; }
    public string Soort { get; set; } = "";       // PasType.Omschrijving
    public string Pasnummer { get; set; } = "";
    public string BV { get; set; } = "";          // via Bankrekeningen -> Relations.name
    public bool Opgeheven { get; set; }
}

/// <summary>Regel uit de bestaande FileStore-tabel.</summary>
public class Bestand
{
    public int FileStoreId { get; set; }
    public string Bestandsnaam { get; set; } = "";
    public DateTime? Toegevoegd { get; set; }
    public string ToegevoegdDoor { get; set; } = "";
}

/// <summary>
/// Planning module (new — has no Access counterpart yet).
/// Would become a new SQL Server table, e.g. [ProjectPlanning].
/// </summary>
public class PlanningItem
{
    public int Id { get; set; }
    public int WerknemerId { get; set; }
    public int Werkorder { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly Eind { get; set; }
    public decimal GeplandeUren { get; set; }
    public string Notitie { get; set; } = "";
}
