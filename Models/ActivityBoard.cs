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

    /// <summary>
    /// Wie het board heeft aangemaakt (Werknemers.Werknemer-id; null = onbekend). Die mag het ook wissen, naast Directie
    /// (besluit gebruiker 19-11-2026).
    /// </summary>
    public int? AangemaaktDoor { get; init; }

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
    /// <summary>Hoeveel werk de activiteit is, in uren (schatting); null = niet ingevuld. Zie <see cref="ActTijd"/>.</summary>
    public decimal? BenodigdeUren { get; set; }
    public DateTime GemaaktOp { get; set; }
    /// <summary>
    /// De initiator: wie de activiteit aanmaakte (Werknemers.Werknemer-id, kolom GemaaktDoor); null = onbekend (gedeelde
    /// werkplek). Punt van Gerard, 19-11-2026: de uitvoerder weet dan bij wie hij met vragen terecht kan.
    /// </summary>
    public int? GemaaktDoor { get; set; }
    public DateTime? GeslotenOp { get; set; }
    /// <summary>
    /// Wanneer en door wie de activiteit het laatst is aangepast (punt van Kees, 23-11-2026): de kolommen last_updated
    /// en GewijzigdDoor. Alleen een andere plek in het vak door slepen telt niet. Null = onbekend.
    /// </summary>
    public DateTime? GewijzigdOp { get; set; }
    public int? GewijzigdDoor { get; set; }
    public int Volgorde { get; set; }
    public List<int> Problemen { get; set; } = new();
    public List<int> Types { get; set; } = new();
    public List<int> Personen { get; set; } = new();
    /// <summary>De zes tekstvelden, op sleutel uit <see cref="ActTekst.Velden"/>.</summary>
    public Dictionary<string, string> Tekst { get; set; } = new();
    /// <summary>
    /// De tekstvelden tonen opmaak (Markdown; ActivityBoard_Opmaak_MariaDB.sql). Kaartjes van vóór die tijd staan op
    /// uit en blijven platte tekst, precies zoals ze waren (besluit gebruiker 20-11-2026); nieuwe kaartjes staan op aan.
    /// </summary>
    public bool Opmaak { get; set; }
    /// <summary>De afvinklijst, op volgorde: kopjes en punten door elkaar (zie <see cref="ActPunt"/>).</summary>
    public List<ActPunt> Checklist { get; set; } = new();
    /// <summary>De koppelingen met andere activiteiten, van beide kanten gezien (zie <see cref="ActKoppeling"/>).</summary>
    public List<ActKoppeling> Koppelingen { get; set; } = new();
    /// <summary>Hangt af van een activiteit die nog niet klaar is.</summary>
    public bool Geblokkeerd => Koppelingen.Any(k => k.Blokkeert);
    /// <summary>
    /// Afgeleide prioriteit (punt van Kees, 23-11-2026): deze activiteit blokkeert een activiteit met een hogere
    /// prioriteit die nog niet klaar is, en telt daarom zelf ook zo zwaar. Null = geen (de eigen prioriteit is al
    /// minstens zo hoog). Gezet door <see cref="ActPrioriteit.Afleiden"/>; niet bewaard.
    /// </summary>
    public int? AfgeleidePrioriteit { get; set; }
    /// <summary>Het nummer van de wachtende activiteit waar de afgeleide prioriteit vandaan komt.</summary>
    public int? AfgeleidVan { get; set; }
    /// <summary>De prioriteit die telt: de afgeleide als die er is, anders de eigen.</summary>
    public int? TelPrioriteit => AfgeleidePrioriteit ?? Prioriteit;
    public int PuntenTotaal => Checklist.Count(p => !p.Kop);
    public int PuntenKlaar => Checklist.Count(p => !p.Kop && p.Klaar);

    public Activiteit Kopie() => new()
    {
        Id = Id, Titel = Titel, Status = Status, Prioriteit = Prioriteit, Vervaldatum = Vervaldatum, BenodigdeUren = BenodigdeUren, GemaaktDoor = GemaaktDoor,
        GemaaktOp = GemaaktOp, GeslotenOp = GeslotenOp, Volgorde = Volgorde, Opmaak = Opmaak, GewijzigdOp = GewijzigdOp, GewijzigdDoor = GewijzigdDoor,
        AfgeleidePrioriteit = AfgeleidePrioriteit, AfgeleidVan = AfgeleidVan,
        Problemen = new(Problemen), Types = new(Types), Personen = new(Personen), Tekst = new(Tekst),
        Checklist = Checklist.Select(p => p.Kopie()).ToList(), Koppelingen = new(Koppelingen),
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

/// <summary>
/// Een regel van de afvinklijst van een activiteit (ActivityBoard_Checklist_MariaDB.sql; vraag Gerard 20-11-2026): een
/// kopje of een punt. De punten onder een kopje zijn de regels tot het volgende kopje; wat vóór het eerste kopje staat
/// hoort bij geen kopje. Bij een afgevinkt punt staat wie het deed en wanneer.
/// </summary>
public class ActPunt
{
    public const int MaxTekst = 500, MaxPerActiviteit = 300;

    public int Id { get; set; }
    public bool Kop { get; set; }
    public string Tekst { get; set; } = "";
    public bool Klaar { get; set; }
    public DateTime? KlaarOp { get; set; }
    public int? KlaarDoor { get; set; }

    public ActPunt Kopie() => (ActPunt)MemberwiseClone();

    /// <summary>De lijst in groepen: het kopje (null = zonder kopje, alleen vooraan) met zijn punten.</summary>
    public static List<(ActPunt? Kop, List<ActPunt> Punten)> Groepen(IEnumerable<ActPunt> lijst)
    {
        var uit = new List<(ActPunt? Kop, List<ActPunt> Punten)> { (null, new List<ActPunt>()) };
        foreach (var p in lijst)
        {
            if (p.Kop) uit.Add((p, new List<ActPunt>()));
            else uit[^1].Punten.Add(p);
        }
        return uit;
    }
}

/// <summary>
/// De soorten koppelingen tussen activiteiten (ActivityBoard_Koppelingen_MariaDB.sql; vraag Gerard 20-11-2026). Een
/// koppeling loopt van de ene activiteit naar de andere; elke kant heeft zijn eigen naam.
/// </summary>
public static class ActKoppelSoort
{
    public const string Afgesplitst = "afgesplitst", HangtAf = "hangtaf", ZieOok = "zieook";

    /// <summary>De keuzes zoals je ze vanaf één kaartje ziet: naam, soort, en of dit kaartje de "van"-kant is.</summary>
    public static readonly (string Naam, string Soort, bool Heen)[] Keuzes =
    {
        ("afgesplitst van", Afgesplitst, true), ("opgesplitst in", Afgesplitst, false),
        ("hangt af van", HangtAf, true), ("blokkeert", HangtAf, false),
        ("zie ook", ZieOok, true),
    };

    public static bool IsGeldig(string soort) => soort is Afgesplitst or HangtAf or ZieOok;
    public static string Naam(string soort, bool heen)
        => soort == ZieOok ? "zie ook" : Keuzes.FirstOrDefault(k => k.Soort == soort && k.Heen == heen).Naam ?? soort;
}

/// <summary>Een activiteit om aan te koppelen (zoeken op nummer of titel, over alle boards).</summary>
public record ActKoppelDoel(int Id, string Titel, string Status, int BoardId, string Board);

/// <summary>
/// Een koppeling gezien vanaf één activiteit: de soort, of deze activiteit de "van"-kant is (<see cref="Heen"/>), en de
/// andere activiteit met titel, status en board.
/// </summary>
public record ActKoppeling(int Id, string Soort, bool Heen, int AnderId, string AnderTitel, string AnderStatus, int AnderBoardId, string AnderBoard,
                           int? AnderPrioriteit = null)
{
    public string Naam => ActKoppelSoort.Naam(Soort, Heen);
    /// <summary>Deze activiteit hangt af van de andere, en die is nog niet klaar.</summary>
    public bool Blokkeert => Soort == ActKoppelSoort.HangtAf && Heen && ActStatus.Groep(AnderStatus) != ActStatus.GroepKlaar;
}

/// <summary>Alles van één board, in één keer geladen.</summary>
public class ActBoardData
{
    public ActBoard Board { get; set; } = new(0, 0, null, "", "");
    public List<ActProbleem> Problemen { get; set; } = new();
    public List<ActType> Types { get; set; } = new();
    public List<Activiteit> Activiteiten { get; set; } = new();
}

/// <summary>
/// De statussen met hun groep en kleur: de zeven uit de spec (§5, kleuren van het prototype) en Geannuleerd (wens
/// gebruiker 19-11-2026; hoort bij Klaar, dus krijgt een sluitdatum en staat bij Voltooid). De eerste twee heten op het
/// scherm Voorstel en Geaccepteerd (punt van Gerard 19-11-2026, zoals in Notion); de codes backlog en next zijn gebleven,
/// want die staan in de database.
/// </summary>
public static class ActStatus
{
    public const string Backlog = "backlog", Next = "next", Bezig = "bezig", Testen = "testen",
                        Beoordeling = "beoordeling", Opgelost = "opgelost", NietOpgelost = "nietopgelost",
                        Geannuleerd = "geannuleerd";
    public const string GroepTodo = "todo", GroepBezig = "bezig", GroepKlaar = "klaar";

    public record Def(string Code, string Naam, string Groep, string Kleur);

    public static readonly Def[] Alle =
    {
        new(Backlog, "Voorstel", GroepTodo, "gray"),
        new(Next, "Geaccepteerd", GroepTodo, "default"),
        new(Bezig, "In uitvoering", GroepBezig, "blue"),
        new(Testen, "Testen", GroepBezig, "purple"),
        new(Beoordeling, "Ter beoordeling", GroepBezig, "brown"),
        new(Opgelost, "Opgelost", GroepKlaar, "green"),
        new(NietOpgelost, "Wordt niet opgelost", GroepKlaar, "red"),
        new(Geannuleerd, "Geannuleerd", GroepKlaar, "orange"),
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

    /// <summary>
    /// Zet de afgeleide prioriteit van alle activiteiten van een board (punt van Kees, 23-11-2026): wie een activiteit
    /// blokkeert die nog niet klaar is, telt minstens zo zwaar als die activiteit. Werkt door over een keten (A blokkeert
    /// B, B blokkeert C). Van een activiteit op dit board gelden status en prioriteit van nu; van een ander board die
    /// uit de koppeling. Een activiteit die zelf klaar is krijgt niets.
    /// </summary>
    public static void Afleiden(IReadOnlyCollection<Activiteit> alle)
    {
        var opId = new Dictionary<int, Activiteit>();
        foreach (var a in alle) { a.AfgeleidePrioriteit = null; a.AfgeleidVan = null; if (a.Id != 0) opId[a.Id] = a; }
        for (var ronde = 0; ronde < 25; ronde++)          // elke ronde een schakel verder; de grens vangt een kring op
        {
            var veranderd = false;
            foreach (var a in alle)
            {
                if (ActStatus.Groep(a.Status) == ActStatus.GroepKlaar) continue;
                foreach (var k in a.Koppelingen)
                {
                    if (k.Soort != ActKoppelSoort.HangtAf || k.Heen) continue;      // alleen "blokkeert"
                    var (status, prio) = opId.TryGetValue(k.AnderId, out var b) ? (b.Status, b.TelPrioriteit) : (k.AnderStatus, k.AnderPrioriteit);
                    if (ActStatus.Groep(status) == ActStatus.GroepKlaar || prio is not int p || p >= (a.TelPrioriteit ?? 9)) continue;
                    a.AfgeleidePrioriteit = p; a.AfgeleidVan = k.AnderId; veranderd = true;
                }
            }
            if (!veranderd) break;
        }
    }
}

/// <summary>
/// Benodigde tijd van een activiteit (verzoek Edwin, 19-11-2026): bewaard in uren. Invoer mag als uren ("1,5"), als
/// uren:minuten ("1:30"), met een eenheid ("45m", "2u", "1u30", "2d"; een dag = <see cref="UrenPerDag"/> uur).
/// </summary>
public static class ActTijd
{
    public const decimal UrenPerDag = 8m;
    public const decimal Max = 9999m;
    private static readonly System.Globalization.CultureInfo Nl = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>Leest de invoer; leeg of 0 = geen tijd (null). False = niet te begrijpen of te groot.</summary>
    public static bool Lees(string? tekst, out decimal? uren)
    {
        uren = null;
        var t = (tekst ?? "").Trim().ToLowerInvariant();
        if (t.Length == 0) return true;
        decimal u;
        System.Text.RegularExpressions.Match m;
        if ((m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d{1,4}):([0-5]?\d)$")).Success)
            u = int.Parse(m.Groups[1].Value) + int.Parse(m.Groups[2].Value) / 60m;
        else if ((m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d{1,4})\s*(?:u|uur|uren|h)\s*([0-5]?\d)\s*(?:m|min|minuten)?$")).Success)
            u = int.Parse(m.Groups[1].Value) + int.Parse(m.Groups[2].Value) / 60m;
        else if ((m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d+(?:[.,]\d+)?)\s*(m|min|minuut|minuten|u|uur|uren|h|d|dag|dagen)?$")).Success)
        {
            if (!decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint,
                                  System.Globalization.CultureInfo.InvariantCulture, out u)) return false;
            var eenheid = m.Groups[2].Value;
            if (eenheid.StartsWith('m')) u /= 60m;
            else if (eenheid.StartsWith('d')) u *= UrenPerDag;
        }
        else return false;
        u = Math.Round(u, 2, MidpointRounding.AwayFromZero);
        if (u > Max) return false;
        uren = u > 0 ? u : null;
        return true;
    }

    /// <summary>Voor het invoervak: de uren als getal ("1,5"), leeg zonder tijd.</summary>
    public static string Invoer(decimal? uren) => uren is decimal u ? u.ToString("0.##", Nl) : "";

    /// <summary>Om te tonen: "45 min" onder het uur, anders "1,5 u"; leeg zonder tijd.</summary>
    public static string Tekst(decimal? uren)
        => uren is not decimal u || u <= 0 ? "" : u < 1 ? $"{Math.Round(u * 60m, MidpointRounding.AwayFromZero):0} min" : u.ToString("0.##", Nl) + " u";
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
