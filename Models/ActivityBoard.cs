namespace EverythingApp.Models;

// ===========================================================================
//  Activity board (spec "Design specificatie MDE Activity Board", prototype v0.5).
//  Een project heeft een board; het board heeft problemen (volgorde = prioriteit),
//  types en activiteiten. Een activiteit staat één keer op het bord: in de baan van
//  het gekoppelde probleem met de hoogste prioriteit, anders in "Geen probleem".
// ===========================================================================

/// <summary>Waar een board bij hoort: een project, een werkorder, een klant (relatie) of niets (vrij board).</summary>
public static class ActBoardSoort
{
    public const string Project = "project", Werkorder = "werkorder", Klant = "klant", Vrij = "vrij";
    public static string Van(int? projectId, int? relatieId, int? werkorderId = null)
        => werkorderId is not null ? Werkorder : projectId is not null ? Project : relatieId is not null ? Klant : Vrij;
    public static string Naam(string soort) => soort switch { Project => "Project", Werkorder => "Werkorder", Klant => "Klant", _ => "Vrij" };
}

/// <summary>Een board met het project of de klant waar het bij hoort.</summary>
public record ActBoard(int Id, int? ProjectId, int? ProjectNummer, string ProjectNaam, string Naam,
                       int? RelatieId = null, string RelatieNaam = "",
                       int? WerkorderId = null, string WerkorderNummer = "", string WerkorderNaam = "")
{
    public string Soort => ActBoardSoort.Van(ProjectId, RelatieId, WerkorderId);

    /// <summary>Titel zonder eigen naam: "224091 · Paprika", de klantnaam, of "Naamloos board".</summary>
    public string StandaardTitel => Soort switch
    {
        ActBoardSoort.Project => $"{(ProjectNummer is int n ? n + " · " : "")}{ProjectNaam}".Trim(),
        ActBoardSoort.Werkorder => $"{(WerkorderNummer.Length > 0 ? WerkorderNummer + " · " : "")}{WerkorderNaam}".Trim(),
        ActBoardSoort.Klant => RelatieNaam,
        _ => "Naamloos board",
    };

    /// <summary>De eigen naam van het board, anders de standaardtitel.</summary>
    public string Titel => Naam.Length > 0 ? Naam : StandaardTitel;
}

/// <summary>Regel in de lijst met boards (startscherm).</summary>
public record ActBoardRegel(int BoardId, int? ProjectId, int? ProjectNummer, string ProjectNaam, string Naam,
                            int Open, int Klaar, DateTime? Gewijzigd, int? RelatieId = null, string RelatieNaam = "",
                            int? WerkorderId = null, string WerkorderNummer = "", string WerkorderNaam = "")
{
    public string Soort => ActBoardSoort.Van(ProjectId, RelatieId, WerkorderId);
    public string Titel => new ActBoard(BoardId, ProjectId, ProjectNummer, ProjectNaam, Naam, RelatieId, RelatieNaam,
                                        WerkorderId, WerkorderNummer, WerkorderNaam).Titel;

    // ---- voor de boom op het startscherm (Klant ▸ Project ▸ Werkorder ▸ subwerkorder) ----
    /// <summary>De klant waar het board onder valt: zelf (klantboard), van het project, of van het project van de werkorder.</summary>
    public int? KlantId { get; init; }
    public string KlantNaam { get; init; } = "";
    /// <summary>Bij een werkorderboard: het project van die werkorder.</summary>
    public int? HoofdProjectId { get; init; }
    public int? HoofdProjectNummer { get; init; }
    public string HoofdProjectNaam { get; init; } = "";
    /// <summary>Bij een werkorderboard: de werkorder waar deze onder valt (Workorders.ParentWerkbonID), één niveau.</summary>
    public int? OuderWerkorderId { get; init; }
    public string OuderWerkorderNummer { get; init; } = "";
    public string OuderWerkorderNaam { get; init; } = "";
}

public class ActProbleem
{
    public int Id { get; set; }
    public string Naam { get; set; } = "";
    public string Kleur { get; set; } = "default";
    public int Positie { get; set; }
}

public class ActType
{
    public int Id { get; set; }
    public string Naam { get; set; } = "";
    public string Kleur { get; set; } = "default";
}

public class Activiteit
{
    public int Id { get; set; }
    public string Titel { get; set; } = "";
    public string Status { get; set; } = ActStatus.Backlog;
    /// <summary>1 = Hoog, 2 = Gemiddeld, 3 = Laag, null = geen.</summary>
    public int? Prioriteit { get; set; }
    public DateTime? Vervaldatum { get; set; }
    public DateTime GemaaktOp { get; set; }
    public DateTime? GeslotenOp { get; set; }
    public int Volgorde { get; set; }
    public List<int> Problemen { get; set; } = new();
    public List<int> Types { get; set; } = new();
    public List<int> Personen { get; set; } = new();
    /// <summary>De zes tekstvelden, op sleutel uit <see cref="ActTekst.Velden"/>.</summary>
    public Dictionary<string, string> Tekst { get; set; } = new();

    public Activiteit Kopie() => new()
    {
        Id = Id, Titel = Titel, Status = Status, Prioriteit = Prioriteit, Vervaldatum = Vervaldatum,
        GemaaktOp = GemaaktOp, GeslotenOp = GeslotenOp, Volgorde = Volgorde,
        Problemen = new(Problemen), Types = new(Types), Personen = new(Personen), Tekst = new(Tekst),
    };

    /// <summary>Hele dagen tussen gemaakt en gesloten, of tot vandaag zolang hij open is; null = sluitmoment onbekend.</summary>
    public int? DagenOpen
    {
        get
        {
            DateTime? eind = GeslotenOp ?? (ActStatus.Groep(Status) != ActStatus.GroepKlaar ? DateTime.Now : null);
            return eind is DateTime e ? Math.Max(0, (int)Math.Floor((e - GemaaktOp).TotalDays)) : null;
        }
    }
}

/// <summary>Alles van één board, in één keer geladen.</summary>
public class ActBoardData
{
    public ActBoard Board { get; set; } = new(0, 0, null, "", "");
    public List<ActProbleem> Problemen { get; set; } = new();
    public List<ActType> Types { get; set; } = new();
    public List<Activiteit> Activiteiten { get; set; } = new();
}

/// <summary>De zeven statussen (spec §5) met hun groep en kleur uit het prototype.</summary>
public static class ActStatus
{
    public const string Backlog = "backlog", Next = "next", Bezig = "bezig", Testen = "testen",
                        Beoordeling = "beoordeling", Opgelost = "opgelost", NietOpgelost = "nietopgelost";
    public const string GroepTodo = "todo", GroepBezig = "bezig", GroepKlaar = "klaar";

    public record Def(string Code, string Naam, string Groep, string Kleur);

    public static readonly Def[] Alle =
    {
        new(Backlog, "Backlog", GroepTodo, "gray"),
        new(Next, "Next", GroepTodo, "default"),
        new(Bezig, "In uitvoering", GroepBezig, "blue"),
        new(Testen, "Testen", GroepBezig, "purple"),
        new(Beoordeling, "Ter beoordeling", GroepBezig, "brown"),
        new(Opgelost, "Opgelost", GroepKlaar, "green"),
        new(NietOpgelost, "Wordt niet opgelost", GroepKlaar, "red"),
    };

    public static Def Van(string code) => Alle.FirstOrDefault(s => s.Code == code) ?? Alle[0];
    public static string Naam(string code) => Van(code).Naam;
    public static string Groep(string code) => Van(code).Groep;
    public static bool IsGeldig(string code) => Alle.Any(s => s.Code == code);
}

public static class ActPrioriteit
{
    public static readonly (int Waarde, string Naam, string Kleur)[] Alle =
        { (1, "Hoog", "red"), (2, "Gemiddeld", "yellow"), (3, "Laag", "default") };

    public static string Naam(int? p) => Alle.FirstOrDefault(x => x.Waarde == p).Naam ?? "";
    public static string Kleur(int? p) => Alle.FirstOrDefault(x => x.Waarde == p).Kleur ?? "default";
}

/// <summary>De zes tekstvelden van een activiteit: sleutel = kolomnaam, naam, hulptekst.</summary>
public static class ActTekst
{
    public static readonly (string Kolom, string Naam, string Hulp)[] Velden =
    {
        ("Samenvatting", "Samenvatting", "Beschrijf het probleem kort."),
        ("Details", "Details", "Beschrijf het probleem in detail, inclusief relevante achtergrondinformatie."),
        ("Impact", "Impact", "Leg de impact van het issue op projecten of bedrijfsvoering uit."),
        ("GenomenStappen", "Genomen stappen", "Vermeld wat er al is gedaan om het probleem op te lossen."),
        ("VolgendeStappen", "Volgende stappen", "Geef aan welke verdere stappen nodig zijn om het probleem op te lossen."),
        ("Opmerkingen", "Opmerkingen", "Voeg eventueel aanvullende opmerkingen of notities toe."),
    };

    public static bool IsKolom(string k) => Velden.Any(v => v.Kolom == k);
}

/// <summary>Het kleurenpalet van het prototype: achtergrond, tekst en stip per naam.</summary>
public static class ActKleur
{
    public static readonly string[] Problemen = { "blue", "red", "yellow", "green", "brown", "purple", "pink", "orange", "gray" };
    public static readonly string[] Types = { "green", "yellow", "blue", "orange", "pink", "purple", "brown", "gray", "red" };
    public static readonly string[] Alle = { "default", "gray", "brown", "orange", "yellow", "green", "blue", "purple", "pink", "red" };

    public static string Geldig(string? k) => k is not null && Alle.Contains(k) ? k : "default";

    /// <summary>Eerste kleur die nog niet gebruikt is, anders rondgaand.</summary>
    public static string Volgende(string[] palet, IEnumerable<string> gebruikt, int aantal)
    {
        var g = gebruikt.ToHashSet();
        return palet.FirstOrDefault(k => !g.Contains(k)) ?? palet[aantal % palet.Length];
    }

    private static readonly string[] AvKleuren = { "#d13228", "#337ea9", "#448361", "#9065b0", "#d9730d", "#a27763", "#c14c8a", "#262c31" };

    /// <summary>Vaste kleur per naam voor het rondje met initialen (zelfde hash als het prototype).</summary>
    public static string Avatar(string naam)
    {
        uint h = 0;
        foreach (var c in naam) h = unchecked(h * 31 + c);
        return AvKleuren[h % AvKleuren.Length];
    }

    public static string Initialen(string naam)
    {
        var w = new string(naam.Select(c => char.IsLetter(c) || c == '.' || char.IsWhiteSpace(c) ? c : ' ').ToArray())
            .Split(new[] { ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0) return "?";
        return (w.Length == 1 ? w[0][..Math.Min(2, w[0].Length)] : $"{w[0][0]}{w[^1][0]}").ToUpperInvariant();
    }
}
