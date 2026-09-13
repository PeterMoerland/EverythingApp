using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>Eén te melden verjaardag of jubileum.</summary>
public record Mijlpaalmelding(
    int WerknemerId,
    string Naam,
    bool IsJubileum,
    DateOnly Datum,        // de dag waarop het valt (dit jaar)
    int Jaren,
    string Wanneer);       // 'vandaag', 'afgelopen zaterdag', 'donderdag 31-07'

/// <summary>
/// Bepaalt welke verjaardagen en jubilea gemeld moeten worden — dezelfde regels
/// als het opstartscherm in Access:
///
///   • Voor iedereen: wie is er vandaag jarig / vandaag zoveel jaar in dienst.
///   • Op maandag ook: wat er afgelopen zaterdag en zondag was.
///   • Voor HRM/Directie daarnaast: wat er de komende zeven dagen aankomt.
///
/// Losgekoppeld van het scherm, zodat de regels op zichzelf te controleren zijn.
/// </summary>
public static class Mijlpaalmeldingen
{
    public static List<Mijlpaalmelding> Bepaal(IEnumerable<Mijlpalen> mensen, DateOnly vandaag, bool vooruitkijken)
    {
        // Welke dagen bekijken we?
        var van = vandaag;
        if (vandaag.DayOfWeek == DayOfWeek.Monday) van = vandaag.AddDays(-2);   // za + zo
        var tot = vooruitkijken ? vandaag.AddDays(7) : vandaag;

        var meldingen = new List<Mijlpaalmelding>();

        foreach (var mens in mensen)
        {
            Voeg(mens.Geboortedatum, false);
            Voeg(mens.InDienst, true);

            void Voeg(DateOnly? datum, bool jubileum)
            {
                if (datum is null || datum.Value.Year < 1900) return;

                for (var dag = van; dag <= tot; dag = dag.AddDays(1))
                {
                    if (!ValtOp(datum.Value, dag)) continue;

                    var jaren = dag.Year - datum.Value.Year;
                    if (jaren <= 0) continue;                    // eerste dag zelf is geen jubileum
                    if (jubileum && dag < datum.Value) continue; // nog niet in dienst

                    meldingen.Add(new Mijlpaalmelding(
                        mens.WerknemerId, mens.Naam, jubileum, dag, jaren, WanneerTekst(dag, vandaag)));
                }
            }
        }

        // vandaag eerst, dan het afgelopen weekend, dan wat er aankomt
        return meldingen
            .OrderBy(m => m.Datum == vandaag ? 0 : m.Datum < vandaag ? 1 : 2)
            .ThenBy(m => m.Datum)
            .ThenBy(m => m.Naam)
            .ToList();
    }

    /// <summary>Valt de jaarlijkse herhaling van 'datum' op 'dag'?</summary>
    private static bool ValtOp(DateOnly datum, DateOnly dag)
    {
        if (datum.Month == dag.Month && datum.Day == dag.Day) return true;

        // 29 februari in een niet-schrikkeljaar: dan op 28 februari
        return datum is { Month: 2, Day: 29 } && dag is { Month: 2, Day: 28 }
               && !DateTime.IsLeapYear(dag.Year);
    }

    private static string WanneerTekst(DateOnly dag, DateOnly vandaag)
    {
        if (dag == vandaag) return "vandaag";
        if (dag == vandaag.AddDays(-1)) return "gisteren";
        if (dag == vandaag.AddDays(1)) return "morgen";

        var naam = Dagnaam(dag.DayOfWeek);
        return dag < vandaag
            ? $"afgelopen {naam}"
            : $"{naam} {dag:dd-MM}";
    }

    private static string Dagnaam(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "maandag",
        DayOfWeek.Tuesday => "dinsdag",
        DayOfWeek.Wednesday => "woensdag",
        DayOfWeek.Thursday => "donderdag",
        DayOfWeek.Friday => "vrijdag",
        DayOfWeek.Saturday => "zaterdag",
        _ => "zondag",
    };
}
