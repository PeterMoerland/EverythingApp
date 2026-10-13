using System.Text.RegularExpressions;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Zoekt bij openstaande uitgaande facturen de ontvangsten op de bank. Vervangt Access'
/// 'Check betalingen in transacties', maar dan per transactie in plaats van per factuur:
/// klanten betalen vaak meerdere facturen in één keer, en dan klopt het bedrag pas als je
/// alle genoemde nummers optelt (gemeten op 2025-2026: 96% van de ontvangsten).
///
/// Verbeteringen ten opzichte van Access:
/// - alleen ontvangsten op de rekening van de BV die de factuur stuurde (nummers bestaan per BV);
/// - het nummer moet los staan: 2026001 telt niet in "000100002230402026001";
/// - deelbetalingen (1/2, 2/2) worden samen bekeken;
/// - niets wordt zomaar weggeschreven: het zijn voorstellen.
/// Geen database; zo is het los te toetsen.
/// </summary>
public static class Betalingsmatcher
{
    /// <summary>Bedragen mogen 1 euro schelen, zoals in Access (afronding, bankkosten).</summary>
    public const decimal Marge = 1m;

    public static List<BetalingVoorstel> Zoek(IReadOnlyList<OpenFactuur> facturen, IReadOnlyList<Ontvangst> ontvangsten)
    {
        var voorstellen = new List<BetalingVoorstel>();
        var factuurGebruikt = new HashSet<int>();
        var ontvangstGebruikt = new HashSet<int>();

        // Welke facturen noemt elke ontvangst? Alleen facturen van dezelfde BV, en niet
        // van ruim na de betaling (een week speling voor vooruitbetaalde proforma's).
        var genoemd = ontvangsten.ToDictionary(o => o.Id, o => facturen
            .Where(f => f.BvId == o.BvId && f.Datum <= o.Datum.AddDays(7) && Noemt(o.Omschrijving, f.Nummer))
            .ToList());

        // 1. Eén ontvangst, het bedrag klopt met alles wat hij noemt.
        foreach (var o in ontvangsten)
        {
            var lijst = genoemd[o.Id];
            if (lijst.Count == 0 || Math.Abs(o.Bedrag - lijst.Sum(f => f.Bedrag)) >= Marge) continue;
            Voeg(VoorstelSoort.Zeker, new() { o }, lijst,
                 lijst.Count == 1 ? "nummer en bedrag kloppen" : $"{lijst.Count} facturen in één betaling");
        }

        // 2. Meerdere ontvangsten die allemaal (alleen) dezelfde factuur noemen en samen kloppen.
        foreach (var groep in ontvangsten
                     .Where(o => !ontvangstGebruikt.Contains(o.Id) && genoemd[o.Id].Count == 1)
                     .GroupBy(o => genoemd[o.Id][0].Id))
        {
            var factuur = genoemd[groep.First().Id][0];
            if (factuurGebruikt.Contains(factuur.Id) || groep.Count() < 2) continue;
            if (Math.Abs(groep.Sum(o => o.Bedrag) - factuur.Bedrag) >= Marge) continue;
            Voeg(VoorstelSoort.Deelbetalingen, groep.ToList(), new() { factuur }, $"{groep.Count()} deelbetalingen");
        }

        // 3. Wel een nummer, maar het bedrag klopt niet: laten zien, niet voorstellen.
        foreach (var o in ontvangsten.Where(o => !ontvangstGebruikt.Contains(o.Id) && genoemd[o.Id].Count > 0))
        {
            var lijst = genoemd[o.Id].Where(f => !factuurGebruikt.Contains(f.Id)).ToList();
            if (lijst.Count == 0) continue;
            voorstellen.Add(new BetalingVoorstel
            {
                Soort = VoorstelSoort.Afwijkend,
                Ontvangsten = new() { o },
                Facturen = lijst,
                Uitleg = o.Bedrag < lijst.Sum(f => f.Bedrag) ? "minder ontvangen dan gefactureerd" : "meer ontvangen dan gefactureerd",
            });
            ontvangstGebruikt.Add(o.Id);
        }

        // 4. Geen nummer: precies het bedrag van één openstaande factuur van die BV, en geen
        //    andere ontvangst met hetzelfde bedrag. Niet aangevinkt; zelf beoordelen.
        var rest = ontvangsten.Where(o => !ontvangstGebruikt.Contains(o.Id) && genoemd[o.Id].Count == 0).ToList();
        foreach (var o in rest)
        {
            var zelfdeBedrag = facturen.Where(f => !factuurGebruikt.Contains(f.Id) && f.BvId == o.BvId
                                                   && f.Datum <= o.Datum && Math.Abs(f.Bedrag - o.Bedrag) < 0.01m).ToList();
            if (zelfdeBedrag.Count != 1) continue;
            if (rest.Count(x => x.BvId == o.BvId && Math.Abs(x.Bedrag - o.Bedrag) < 0.01m) > 1) continue;

            var f = zelfdeBedrag[0];
            var naamLijkt = Lijkt(o.Tegenpartij, f.Klant);
            Voeg(VoorstelSoort.AlleenBedrag, new() { o }, new() { f },
                 naamLijkt ? "zelfde bedrag en de naam lijkt op de klant" : "alleen het bedrag is gelijk", aanvinken: false);
        }

        return voorstellen
            .OrderBy(v => v.Soort)
            .ThenBy(v => v.Ontvangsten.Min(o => o.Datum))
            .ToList();

        void Voeg(VoorstelSoort soort, List<Ontvangst> os, List<OpenFactuur> fs, string uitleg, bool aanvinken = true)
        {
            // Een factuur of ontvangst komt maar in één voorstel voor.
            if (fs.Any(f => factuurGebruikt.Contains(f.Id)) || os.Any(o => ontvangstGebruikt.Contains(o.Id))) return;
            voorstellen.Add(new BetalingVoorstel
            {
                Soort = soort, Ontvangsten = os, Facturen = fs, Uitleg = uitleg, Aangevinkt = aanvinken,
            });
            foreach (var f in fs) factuurGebruikt.Add(f.Id);
            foreach (var o in os) ontvangstGebruikt.Add(o.Id);
        }
    }

    /// <summary>Staat het factuurnummer los in de tekst, dus niet midden in een langer getal?</summary>
    public static bool Noemt(string omschrijving, string nummer)
        => nummer.Length >= 4 && Regex.IsMatch(omschrijving, $@"(?<!\d){Regex.Escape(nummer)}(?!\d)");

    /// <summary>Grof: deelt de naam van de tegenpartij een woord van 4+ letters met de klantnaam?</summary>
    private static bool Lijkt(string tegenpartij, string klant)
    {
        static IEnumerable<string> Woorden(string s) => Regex.Split(s.ToLowerInvariant(), @"[^a-z0-9]+").Where(w => w.Length >= 4);
        var k = Woorden(klant).ToHashSet();
        return Woorden(tegenpartij).Any(k.Contains);
    }
}
