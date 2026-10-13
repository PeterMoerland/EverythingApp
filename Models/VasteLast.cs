namespace EverythingApp.Models;

/// <summary>Hoe vaak een vaste last afgaat. De getallen zijn die van Access (kolom interval).</summary>
public enum VasteLastInterval { Wekelijks = 1, Maandelijks = 2, Kwartaal = 3, Jaarlijks = 4 }

public record VasteLastCategorie(int Id, string Omschrijving);

/// <summary>
/// Eén vaste last (tabel VasteLasten).
///
/// Incassodatum is één getal dat per interval iets anders betekent, zoals Access het opslaat:
/// wekelijks = weekdag (1 = maandag), maandelijks = dag van de maand, kwartaal en jaarlijks =
/// dag van het jaar (berekend in het lopende jaar). Bij kwartaal ligt die dag in de eerste drie
/// maanden: januari = jan/apr/jul/okt, februari = feb/mei/aug/nov, maart = mrt/jun/sep/dec.
/// Dag en Maand hieronder zijn de leesbare vorm; Incassodatum wordt daaruit gerekend.
/// </summary>
public class VasteLast
{
    public int Id { get; set; }
    public string Omschrijving { get; set; } = "";
    public decimal Bedrag { get; set; }
    public VasteLastInterval Interval { get; set; } = VasteLastInterval.Maandelijks;
    public int? CategorieId { get; set; }
    public string Categorie { get; set; } = "";
    public DateTime? Gewijzigd { get; set; }

    /// <summary>Weekdag (1-7) bij wekelijks, anders dag van de maand (1-31).</summary>
    public int Dag { get; set; } = 1;

    /// <summary>Maand 1-12 bij jaarlijks; 1-3 (welke maand van het kwartaal) bij kwartaal; anders niet gebruikt.</summary>
    public int Maand { get; set; } = 1;

    private static readonly string[] Maanden =
        { "januari", "februari", "maart", "april", "mei", "juni", "juli", "augustus", "september", "oktober", "november", "december" };
    private static readonly string[] Weekdagen = { "maandag", "dinsdag", "woensdag", "donderdag", "vrijdag", "zaterdag", "zondag" };
    private static readonly string[] Kwartaalmaanden = { "jan/apr/jul/okt", "feb/mei/aug/nov", "mrt/jun/sep/dec" };

    public static string MaandNaam(int maand) => Maanden[Math.Clamp(maand, 1, 12) - 1];
    public static string WeekdagNaam(int dag) => Weekdagen[Math.Clamp(dag, 1, 7) - 1];
    public static string KwartaalNaam(int maand) => Kwartaalmaanden[Math.Clamp(maand, 1, 3) - 1];

    /// <summary>Zoals Access: bedrag × 52, × 12, × 4 of × 1.</summary>
    public decimal TotaalPerJaar => Bedrag * Interval switch
    {
        VasteLastInterval.Wekelijks => 52,
        VasteLastInterval.Maandelijks => 12,
        VasteLastInterval.Kwartaal => 4,
        _ => 1,
    };

    /// <summary>Leesbaar: "elke dinsdag", "de 25e van elke maand", "15 jan/apr/jul/okt", "15 december".</summary>
    public string Wanneer => Interval switch
    {
        VasteLastInterval.Wekelijks => "elke " + WeekdagNaam(Dag),
        VasteLastInterval.Maandelijks => $"de {Dag}e van elke maand",
        VasteLastInterval.Kwartaal => $"{Dag} {KwartaalNaam(Maand)}",
        _ => $"{Dag} {MaandNaam(Maand)}",
    };

    public static string IntervalNaam(VasteLastInterval i) => i switch
    {
        VasteLastInterval.Wekelijks => "Wekelijks",
        VasteLastInterval.Maandelijks => "Maandelijks",
        VasteLastInterval.Kwartaal => "Per kwartaal",
        _ => "Jaarlijks",
    };

    // ============================================================
    //  Van en naar het Access-getal
    // ============================================================

    /// <summary>Het getal voor de kolom Incassodatum, zoals Access het schreef (dag van het jaar in het lopende jaar).</summary>
    public int NaarIncassodatum(int jaar)
    {
        switch (Interval)
        {
            case VasteLastInterval.Wekelijks: return Math.Clamp(Dag, 1, 7);
            case VasteLastInterval.Maandelijks: return Math.Clamp(Dag, 1, 31);
            default:
                var maand = Interval == VasteLastInterval.Kwartaal ? Math.Clamp(Maand, 1, 3) : Math.Clamp(Maand, 1, 12);
                return Datum(jaar, maand, Dag).DayOfYear;
        }
    }

    /// <summary>Vult Dag en Maand vanuit het Access-getal.</summary>
    public void VanIncassodatum(int? incassodatum, int jaar)
    {
        var waarde = incassodatum ?? 1;
        switch (Interval)
        {
            case VasteLastInterval.Wekelijks: Dag = Math.Clamp(waarde, 1, 7); Maand = 1; break;
            case VasteLastInterval.Maandelijks: Dag = Math.Clamp(waarde, 1, 31); Maand = 1; break;
            default:
                var dag = new DateOnly(jaar, 1, 1).AddDays(Math.Clamp(waarde, 1, 366) - 1);
                Dag = dag.Day;
                // Kwartaal: welke maand van het kwartaal (Access deed hier maand/3, wat niet klopte).
                Maand = Interval == VasteLastInterval.Kwartaal ? (dag.Month - 1) % 3 + 1 : dag.Month;
                break;
        }
    }

    // ============================================================
    //  Wanneer gaat hij af
    // ============================================================

    /// <summary>Alle betaaldagen in een jaar. Een dag die niet bestaat (31 april) wordt de laatste van de maand.</summary>
    public IEnumerable<DateOnly> Betaaldagen(int jaar)
    {
        switch (Interval)
        {
            case VasteLastInterval.Wekelijks:
                // DayOfWeek: zondag = 0; hier maandag = 1 … zondag = 7.
                var eerste = new DateOnly(jaar, 1, 1);
                var verschuiving = (Math.Clamp(Dag, 1, 7) % 7 - (int)eerste.DayOfWeek + 7) % 7;
                for (var d = eerste.AddDays(verschuiving); d.Year == jaar; d = d.AddDays(7)) yield return d;
                break;
            case VasteLastInterval.Maandelijks:
                for (var m = 1; m <= 12; m++) yield return Datum(jaar, m, Dag);
                break;
            case VasteLastInterval.Kwartaal:
                for (var m = Math.Clamp(Maand, 1, 3); m <= 12; m += 3) yield return Datum(jaar, m, Dag);
                break;
            default:
                yield return Datum(jaar, Math.Clamp(Maand, 1, 12), Dag);
                break;
        }
    }

    /// <summary>De eerstvolgende betaaldag vanaf vandaag (of de gegeven dag).</summary>
    public DateOnly Volgende(DateOnly vanaf)
        => Betaaldagen(vanaf.Year).Concat(Betaaldagen(vanaf.Year + 1)).First(d => d >= vanaf);

    private static DateOnly Datum(int jaar, int maand, int dag)
        => new(jaar, maand, Math.Clamp(dag, 1, DateTime.DaysInMonth(jaar, maand)));
}
