using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// De tariefberekening van één urenregel: Access' calctarief (VBA_Source\Global.bas), zonder
/// database. Alles wat nodig is wordt meegegeven, zodat de regels los te toetsen zijn.
///
/// Volgorde zoals Access:
/// 1. verkoopprijs per uur/reisuur/km uit het werkorderteam (0 = niet ingevuld);
/// 2. wat daar 0 is, uit de tariefafspraak met de klant op die datum, anders uit die van
///    MDE Automation B.V.; daar komen ook de overwerkpercentages vandaan;
/// 3. kostprijs uit WerknemersTarieven op die datum (ontbreekt die, dan geen nieuw bedrag);
/// 4. doorbelastpercentages van de werkorder (standaard 100% uren, 130% onkosten).
/// </summary>
public static class Tariefregels
{
    public static void Reken(
        UurRegel u,
        TeamTarief? team,
        IReadOnlyList<TariefafspraakPeriode> klantAfspraken,
        IReadOnlyList<TariefafspraakPeriode> standaardAfspraken,
        IReadOnlyList<Kostprijstarief> kostprijzen,
        Doorbelasting? doorbelasting)
    {
        u.NieuwBedrag = null;
        u.NieuweKostprijs = null;
        u.Melding = "";

        // Access sloeg regels zonder werkorder of uurtype stil over.
        if (u.WerkbonId == 0 || u.Tariefsoort.Length == 0)
        {
            u.Melding = "geen werkorder of uurtype";
            return;
        }

        decimal basis = team?.Werkuur ?? 0, reis = team?.Reisuur ?? 0, km = team?.Kilometer ?? 0;

        var afspraak = Geldig(klantAfspraken, u.Datum);
        if (afspraak is null)
        {
            afspraak = Geldig(standaardAfspraken, u.Datum);
            if (afspraak is null)
            {
                u.Melding = "geen tariefafspraak met de klant en geen standaardtarief van MDE op deze datum";
                return;
            }
            u.Melding = "geen tariefafspraak met de klant: tarieven van MDE Automation gebruikt";
        }

        if (basis == 0) basis = afspraak.Basis;
        if (reis == 0) reis = afspraak.Reis;
        if (km == 0) km = afspraak.Kilometer;

        var kost = kostprijzen.FirstOrDefault(k => k.Begin <= u.Datum && k.Eind >= u.Datum);
        if (kost is null)
        {
            // Zonder datum: zo telt de samenvatting per medewerker; de dag staat in de regel zelf.
            u.Melding = $"geen kostprijs bekend voor {u.Werknemer} (Werknemers > tarieven)";
            return;
        }

        var doorUren = doorbelasting?.Uren ?? 100m;
        var doorOnkosten = doorbelasting?.Onkosten ?? 130m;

        decimal bedrag = 0, kostprijs = 0;
        var kostWerk = kost.Werkuur ?? 0;

        switch (u.Tariefsoort)
        {
            case "Travel":
                bedrag = u.Uren * reis * (doorUren / 100);
                kostprijs = u.Uren * (kost.Reisuur ?? 0);
                break;
            case "Normal":
                bedrag = u.Uren * basis * (doorUren / 100);
                kostprijs = u.Uren * kostWerk;
                break;
            case "Overtime":
                bedrag = u.Uren * basis * ((afspraak.ProcentOverwerk ?? 0) / 100) * (doorUren / 100);
                kostprijs = u.Uren * kostWerk * ((afspraak.ProcentOverwerk ?? 0) / 100);
                break;
            case "Overtime+":
                bedrag = u.Uren * basis * ((afspraak.ProcentOverwerkZon ?? 0) / 100) * (doorUren / 100);
                kostprijs = u.Uren * kostWerk * ((afspraak.ProcentOverwerkZon ?? 0) / 100);
                break;
            // "Fixed" en andere soorten: geen uurbedrag, alleen kilometers en vast bedrag.
        }

        bedrag += u.Kilometers * km;
        kostprijs += u.Kilometers * (kost.Kilometer ?? 0);

        // Het vaste bedrag (onkosten) naar ex btw, afhankelijk van hoe het is ingevoerd.
        switch (u.VastType)
        {
            case 0:
                bedrag += u.VastBedrag * (doorOnkosten / 100);
                kostprijs += u.VastBedrag;
                break;
            case 1:
                bedrag += u.VastBedrag / 1.09m * (doorOnkosten / 100);
                kostprijs += u.VastBedrag / 1.09m;
                break;
            case 2:
                bedrag += u.VastBedrag / 1.21m * (doorOnkosten / 100);
                kostprijs += u.VastBedrag / 1.21m;
                break;
            case 3:
                bedrag += u.VastBedrag;
                kostprijs += u.VastBedrag;
                break;
        }

        // VBA Round is bankiersafronding; MidpointRounding.ToEven doet hetzelfde.
        u.NieuwBedrag = Math.Round(bedrag, 2, MidpointRounding.ToEven);
        u.NieuweKostprijs = Math.Round(kostprijs, 2, MidpointRounding.ToEven);
    }

    /// <summary>De afspraak die op die dag geldt; bij overlap de nieuwste.</summary>
    private static TariefafspraakPeriode? Geldig(IReadOnlyList<TariefafspraakPeriode> afspraken, DateOnly dag)
        => afspraken.Where(a => a.Start <= dag && a.Stop >= dag).MaxBy(a => a.Start);
}
