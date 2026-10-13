using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// De rekenregels van het liquiditeitsoverzicht (Access: Report_LiquiditeitsOverzicht), zonder database.
///
/// Start = de laatste dag met banktransacties; het saldo is dat van die dag. Daarna per dag:
/// + verwachte ontvangsten (verkoopfacturen op hun vervaldatum), − betalingen aan leveranciers
/// (inkoopfacturen op hun vervaldatum, nog niet via SEPA), − vaste lasten op hun echte betaaldagen,
/// − btw op de uiterste betaaldatum van het kwartaal. Wat op de startdatum al vervallen is, telt
/// apart als 'te laat'. Net als Access twee lijnen: saldo minus wat we leveranciers al te laat
/// schuldig zijn, en saldo plus wat klanten ons te laat schuldig zijn.
///
/// Verschillen met Access (die daar fouten waren): vaste lasten per kwartaal en per jaar komen op
/// de goede dag (Access zag ze als dag van de maand en miste de meeste); te laat en prognose worden
/// op dezelfde datum gesplitst, zodat niets dubbel telt; alle btw-termijnen in de periode tellen mee.
/// </summary>
public static class Liquiditeitsrekenaar
{
    public static LiquiditeitPrognose Bereken(
        DateOnly start, int dagen,
        IReadOnlyList<Banksaldo> rekeningen,
        IReadOnlyList<OpenstaandeFactuur> facturen,
        IReadOnlyList<VasteLast> vasteLasten,
        IReadOnlyDictionary<(int Jaar, int Kwartaal), decimal> btwPerKwartaal)
    {
        var eind = start.AddDays(dagen);
        var p = new LiquiditeitPrognose
        {
            Start = start,
            Rekeningen = rekeningen.ToList(),
            Beginsaldo = rekeningen.Sum(r => r.Saldo),
        };

        // ---- facturen: vervallen = te laat, anders op de vervaldatum in de prognose ----
        foreach (var f in facturen)
        {
            if (f.Vervaldatum <= start)
            {
                // Zoals Access: een nog niet verstuurde verkoopfactuur is niet 'te laat'.
                if (f.Verkoop && f.Verstuurd) p.TeLaatKlanten.Add(f);
                else if (!f.Verkoop) p.TeLaatLeveranciers.Add(f);
                continue;
            }
            if (f.Vervaldatum > eind) continue;

            p.Posten.Add(new LiquiditeitPost(f.Vervaldatum, f.Verkoop ? "Klant" : "Leverancier",
                f.Relatie, $"{f.Bv} {f.Nummer}", f.Verkoop ? f.Bedrag : -f.Bedrag, f.Id, f.Verkoop));
        }

        // ---- vaste lasten op hun echte betaaldagen ----
        foreach (var v in vasteLasten)
            foreach (var d in v.Betaaldagen(start.Year).Concat(v.Betaaldagen(start.Year + 1)))
                if (d > start && d <= eind)
                    p.Posten.Add(new LiquiditeitPost(d, "Vaste last", v.Omschrijving,
                        "vaste last" + (v.Categorie.Length > 0 ? " · " + v.Categorie : ""), -v.Bedrag, VasteLastId: v.Id));

        // ---- btw: per kwartaal te betalen voor het eind van de maand erna ----
        foreach (var (betaaldag, jaar, kwartaal) in BtwTermijnen(start, eind))
        {
            var bedrag = btwPerKwartaal.TryGetValue((jaar, kwartaal), out var b) ? b : 0;
            if (bedrag == 0) continue;
            var kwartaalEinde = new DateOnly(jaar, kwartaal * 3, 1).AddMonths(1).AddDays(-1);
            var voorlopig = kwartaalEinde > start ? " (voorlopig, kwartaal loopt nog)" : "";
            p.Posten.Add(new LiquiditeitPost(betaaldag, "Btw", "Belastingdienst", $"btw Q{kwartaal} {jaar}{voorlopig}", -bedrag));
        }

        // ---- per dag optellen ----
        var saldo = p.Beginsaldo;
        for (var d = start.AddDays(1); d <= eind; d = d.AddDays(1))
        {
            var vandaag = p.Posten.Where(x => x.Datum == d).ToList();
            var erbij = vandaag.Where(x => x.Bedrag > 0).Sum(x => x.Bedrag);
            var eraf = -vandaag.Where(x => x.Bedrag < 0).Sum(x => x.Bedrag);
            saldo += erbij - eraf;
            p.Dagen.Add(new LiquiditeitDag(d, erbij, eraf, saldo,
                saldo - p.TeLaatAanLeveranciers, saldo + p.TeLaatVanKlanten));
        }

        p.Posten = p.Posten.OrderBy(x => x.Datum).ThenByDescending(x => x.Bedrag).ToList();
        return p;
    }

    /// <summary>De btw-betaaldagen in (start, eind]: Q1 op 30 april, Q2 op 31 juli, Q3 op 31 oktober, Q4 op 31 januari.</summary>
    public static IEnumerable<(DateOnly Betaaldag, int Jaar, int Kwartaal)> BtwTermijnen(DateOnly start, DateOnly eind)
    {
        for (var jaar = start.Year - 1; jaar <= eind.Year; jaar++)
            for (var kwartaal = 1; kwartaal <= 4; kwartaal++)
            {
                var betaaldag = new DateOnly(jaar, kwartaal * 3, 1).AddMonths(2).AddDays(-1);  // eind van de maand na het kwartaal
                if (betaaldag > start && betaaldag <= eind) yield return (betaaldag, jaar, kwartaal);
            }
    }

    /// <summary>Het kwartaal (1-4) van een datum.</summary>
    public static int Kwartaal(DateOnly d) => (d.Month - 1) / 3 + 1;
}
