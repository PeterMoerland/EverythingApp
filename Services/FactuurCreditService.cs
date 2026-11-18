using System.Globalization;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>Een net gemaakte credit: voor de lijst (kiezen) en de melding.</summary>
public record CreditUitkomst(int Id, string Nummer, string Melding);

/// <summary>
/// Een verstuurde uitgaande factuur in één keer crediteren (verzoek Edwin 18-11-2026): een nieuwe factuur met dezelfde
/// klant, BV, project, PO en regels, maar met het bedrag negatief. Een credit is hier gewoon een factuur met een negatief
/// totaal (VerkoopFactuurRegel.IsCreditnota, de PDF heet dan "Creditfactuur"). De credit krijgt een eigen nummer, staat
/// als concept (niet verstuurd) in Uitgaande facturen en is daar na te kijken, aan te passen en te versturen.
/// </summary>
public class FactuurCreditService
{
    private readonly IVerkoopFactuurRepository _repo;

    public FactuurCreditService(IVerkoopFactuurRepository repo) => _repo = repo;

    /// <summary>Waarom deze factuur niet gecrediteerd kan worden; null = het mag. Alleen een verstuurde, gewone factuur.</summary>
    public static string? MagNiet(bool verstuurd, bool credit, bool proforma)
        => proforma ? "Een proforma kan niet gecrediteerd worden."
         : credit ? "Dit is zelf een creditfactuur."
         : !verstuurd ? "Crediteren kan pas als de factuur verstuurd is; een factuur die nog niet verstuurd is pas je gewoon aan."
         : null;

    public static string? MagNiet(VerkoopFactuurRegel f) => MagNiet(f.Verstuurd, f.IsCreditnota, f.IsProforma);
    public static string? MagNiet(VerkoopFactuur f) => MagNiet(f.Verstuurd, f.BedragExBtw < 0, f.Herinnering == 4);

    /// <summary>De credit van deze factuur, nog niet opgeslagen (Id 0; het nummer komt bij het opslaan).</summary>
    public static VerkoopFactuur Maak(VerkoopFactuur o)
    {
        var cultuur = new CultureInfo(o.Engels ? "en-IE" : "nl-NL");
        var c = new VerkoopFactuur
        {
            AdministratieId = o.AdministratieId,
            KlantId = o.KlantId,
            Klant = o.Klant,
            Factuurdatum = DateOnly.FromDateTime(DateTime.Today),
            Betalingstermijn = o.Betalingstermijn,
            Referentie = o.Referentie,
            PoKlant = o.PoKlant,
            ProjectId = o.ProjectId,
            Project = o.Project,
            // staat op de factuur: de klant ziet welke factuur gecrediteerd wordt
            Omschrijving = (o.Engels ? $"Credit note for invoice {o.Factuurnummer}" : $"Credit factuur {o.Factuurnummer}")
                           + (o.Omschrijving.Trim().Length > 0 ? ": " + o.Omschrijving.Trim() : ""),
            Opmerking = $"Credit van factuur {o.Factuurnummer}.",
            BtwVerlegd = o.BtwVerlegd,
            Engels = o.Engels,
        };

        foreach (var r in o.Regels)
        {
            var bedrag = r.Bereken();
            if (r.Stuksprijs is null && bedrag == 0)
            {
                // een tekstregel: gaat ongewijzigd mee
                c.Regels.Add(new VerkoopFactuurItem { Omschrijving = r.Tekst });
            }
            else if (r.IsUren)
            {
                // Een urenregel wordt een gewone regel met het bedrag van die uren: de uren zelf blijven bij de
                // oorspronkelijke factuur gereserveerd (een nieuwe regel zonder herkomst reserveert niets).
                var wie = string.Join(" · ", new[]
                {
                    r.Werkorder, r.Tekst, r.Naam, r.Uurtype,
                    r.Uren is decimal u ? u.ToString("#,0.##", cultuur) + (o.Engels ? " hrs" : " uur") : "",
                }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
                c.Regels.Add(new VerkoopFactuurItem { Omschrijving = wie, Aantal = -1, Stuksprijs = r.Stuksprijs, Korting = r.Korting });
            }
            else if (r.Stuksprijs is decimal prijs)
            {
                // min het aantal, zelfde prijs en korting
                c.Regels.Add(new VerkoopFactuurItem { Omschrijving = r.Omschrijving, Aantal = -(r.Aantal ?? 0), Stuksprijs = prijs, Korting = r.Korting });
            }
            else
            {
                // een oude regel met alleen een totaal
                c.Regels.Add(new VerkoopFactuurItem { Omschrijving = r.Tekst, Aantal = -1, Stuksprijs = r.Totaal });
            }
        }
        return c;
    }

    /// <summary>Is er al een credit voor deze factuur? (een creditfactuur van dezelfde klant die het nummer noemt). Het nummer, of null.</summary>
    public async Task<string?> BestaandeCreditAsync(VerkoopFactuur o)
    {
        if (o.Factuurnummer.Length == 0) return null;
        var treffers = await _repo.ZoekAsync(new VerkoopFactuurFilter { Zoek = o.Factuurnummer }, 50);
        return treffers.FirstOrDefault(t => t.Id != o.Id && t.IsCreditnota && t.KlantId == o.KlantId &&
                                            t.Omschrijving.Contains(o.Factuurnummer, StringComparison.OrdinalIgnoreCase))?.Factuurnummer;
    }

    /// <summary>De vraag vooraf ("Factuur … crediteren?"), met een waarschuwing als er al een credit lijkt te zijn; of de
    /// reden waarom het niet kan.</summary>
    public async Task<(string? Vraag, string? Fout)> VraagAsync(int factuurId)
    {
        var o = await _repo.GetAsync(factuurId);
        if (o is null) return (null, "Deze factuur bestaat niet meer.");
        if (MagNiet(o) is { } reden) return (null, reden);
        if (o.Regels.Count == 0) return (null, "Deze factuur heeft geen regels om te crediteren.");
        var al = await BestaandeCreditAsync(o);
        return ($"Factuur {o.Factuurnummer} van {o.Klant} crediteren ({o.BedragInclBtw.ToString("C2", Nl)} incl. btw)?\n\n" +
                "Er komt een creditfactuur met een eigen nummer in Uitgaande facturen. Hij is nog niet verstuurd: je kunt hem nakijken, aanpassen en versturen." +
                (al is not null ? $"\n\nLET OP: creditfactuur {al} noemt deze factuur al. Mogelijk is hij al gecrediteerd." : ""), null);
    }

    private static readonly CultureInfo Nl = new("nl-NL");

    /// <summary>De melding na het maken.</summary>
    public static CreditUitkomst Uitkomst(VerkoopFactuur c, decimal verschil)
        => new(c.Id, c.Factuurnummer,
               $"Creditfactuur {c.Factuurnummer} gemaakt ({c.BedragInclBtw.ToString("C2", Nl)} incl. btw); nog niet verstuurd." +
               (verschil != 0 ? $" Let op: het totaal wijkt {Math.Abs(verschil).ToString("C2", Nl)} af van de factuur, door de afronding van de btw." : ""));

    /// <summary>
    /// De credit maken en opslaan. Geeft de credit (met Id en nummer), of de reden waarom het niet kan. Verschil = hoeveel
    /// het totaal incl. btw afwijkt van min de factuur (de btw wordt naar beneden afgekapt, zoals Access deed: bij een
    /// negatief bedrag kan dat een cent schelen).
    /// </summary>
    public async Task<(VerkoopFactuur? Credit, string? Fout, decimal Verschil)> MaakAsync(int factuurId, int? doorWerknemerId)
    {
        var o = await _repo.GetAsync(factuurId);
        if (o is null) return (null, "Deze factuur bestaat niet meer.", 0);
        if (MagNiet(o) is { } reden) return (null, reden, 0);
        if (o.Regels.Count == 0) return (null, "Deze factuur heeft geen regels om te crediteren.", 0);
        var c = Maak(o);
        await _repo.BewaarAsync(c, doorWerknemerId);
        return (c, null, c.BedragInclBtw + o.BedragInclBtw);
    }
}
