namespace EverythingApp.Services;

/// <summary>Kolom in een beheertabel.</summary>
public record BeheerVeld(string Label, string Kolom, VeldType Type = VeldType.Tekst, bool Breed = false);

/// <summary>Verwijzing die verwijderen blokkeert: 'tabel.kolom wijst hiernaar'.</summary>
public record Verwijzing(string Tabel, string Kolom, string Omschrijving);

/// <summary>Eén beheerbare tabel.</summary>
public record BeheerTabel(
    string Sleutel,
    string Titel,
    string Tabel,
    string IdKolom,
    BeheerVeld[] Velden,
    Verwijzing[] Verwijzingen,
    string? Uitleg = null,
    string? SorteerOp = null);     // getalkolom waarop de lijst staat (bv. Volgorde); anders op het eerste veld

/// <summary>
/// Tabellen die via Beheer te onderhouden zijn. Een nieuwe tabel toevoegen =
/// hier een regel bijzetten; het scherm werkt dan direct.
/// </summary>
public static class BeheerTabellen
{
    public static readonly BeheerTabel[] Alle =
    {
        new("activiteiten", "Activiteiten", "UrenActiviteit", "UrenActiviteitID",
            new BeheerVeld[]
            {
                new("Activiteit", "UrenActiviteit", VeldType.Tekst, Breed: true),
                new("Werkorderactiviteit", "WerkorderActiviteit", VeldType.JaNee),
            },
            new Verwijzing[]
            {
                new("Uren", "UrenActiviteitID", "urenregel(s)"),
                new("Werknemers", "Hoofdactiviteit", "werknemer(s) als hoofdactiviteit"),
                new("WerkorderActiviteiten", "UrenActiviteitID", "werkorderactiviteit(en)"),
            },
            "Activiteiten worden gekozen bij het boeken van uren en als hoofdactiviteit van een werknemer."),

        new("gebeurtenissoorten", "Gebeurtenissoorten", "Gebeurtenis_Soort", "GebeurtenisSoortId",
            new BeheerVeld[]
            {
                new("Naam", "Naam", VeldType.Tekst, Breed: true),
                new("Groep", "Groep"),
                new("Wijzigt dienst (1 = in, -1 = uit)", "WijzigtDienst", VeldType.Getal),
                new("Vraagt document", "VraagtDocument", VeldType.JaNee),
                new("Standaard privé", "StandaardPrive", VeldType.JaNee),
                new("Telt als jubileum", "Jubileum", VeldType.JaNee),
                new("Actief", "Actief", VeldType.JaNee),
            },
            new Verwijzing[]
            {
                new("Werknemer_Gebeurtenis", "GebeurtenisSoortId", "loopbaangebeurtenis(sen)"),
            },
            "Soorten gebeurtenissen op de loopbaantijdlijn. 'Wijzigt dienst' bepaalt of iemand " +
            "hiermee in of uit dienst gaat; 'standaard privé' zet het privé-vinkje al aan."),

        new("inventor-behandelingen", "Inventor behandelingen", "InventorTreatments", "Treatment-ID",
            new BeheerVeld[]
            {
                new("Behandeling", "Treatment", VeldType.Tekst, Breed: true),
                new("Opmerking", "Comment", VeldType.Tekst, Breed: true),
            },
            Array.Empty<Verwijzing>(),
            "Oppervlaktebehandelingen die Inventor op een onderdeel kan zetten, " +
            "zoals poedercoaten of hard-anodiseren."),

        new("inventor-bewerkingen", "Inventor bewerkingen", "InventorBewerkingen", "InventorBewerkingen_id",
            new BeheerVeld[]
            {
                new("Bewerking", "InventorBewerkingsNaam", VeldType.Tekst, Breed: true),
                new("Afkorting", "Afkorting"),
            },
            Array.Empty<Verwijzing>(),
            "Bewerkingen voor maakdelen uit Inventor, met de afkorting die in " +
            "de bestandsnamen en op tekeningen terugkomt (DRA, FRE, PLA…)."),

        new("projectrollen", "Projectrollen", "ProjectRollen", "RolID",
            new BeheerVeld[]
            {
                new("Rol", "Naam", VeldType.Tekst, Breed: true),
                new("Volgorde", "Volgorde", VeldType.Getal),
                new("Actief", "Actief", VeldType.JaNee),
            },
            new Verwijzing[]
            {
                new("ProjectTeam", "RolID", "teamlid/teamleden in projecten"),
            },
            "Rollen in een projectteam (Projecten ▸ tab Team), in deze volgorde onder elkaar; een rol mag meerdere " +
            "personen hebben. Een rol die nog in een team staat kan niet weg: zet hem dan op niet actief. " +
            "Tabel uit ProjectTeam_MariaDB.sql.",
            SorteerOp: "Volgorde"),

        new("pastypes", "Pastypes", "PasType", "PasTypeID",
            new BeheerVeld[]
            {
                new("Omschrijving", "Omschrijving", VeldType.Tekst, Breed: true),
            },
            new Verwijzing[]
            {
                new("Passen", "PasTypeID", "pas(sen)"),
            },
            "Soorten passen: laadpas, betaalpas, creditcard."),
    };

    public static BeheerTabel? Zoek(string? sleutel) =>
        Alle.FirstOrDefault(t => string.Equals(t.Sleutel, sleutel, StringComparison.OrdinalIgnoreCase));
}
