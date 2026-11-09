using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Verkooporders stap 2 (wens gebruiker 9-11-2026): de regels van een verkooporder bestellen bij de leverancier.
/// Per leverancier één bestelling (Orders) op de werkorder van de verkooporder, met de standaardwaarden van de leverancier
/// (contactpersoon, taal, leverdatum) en zonder ander afleveradres: leveringen gaan altijd via MDE. Wie verkooporders mag
/// invoeren mag direct bestellen, dus de bestelling gaat meteen naar In bestelling (geen goedkeuring PM/Directie).
/// Mailen naar de leverancier en Volgende (Wacht op levering) gebeuren daarna gewoon in Bestellingen.
/// Elke bestelregel wordt via VerkoopOrderKoppelingen (Soort "inkoop") aan de verkooporderregel gekoppeld.
/// </summary>
public class VerkoopBestellen
{
    private readonly IBestelRepository _bestel;
    private readonly IVerkoopOrderRepository _vo;
    private readonly IArtikelRepository _artikelen;
    private static readonly System.Globalization.CultureInfo Nl = new("nl-NL");

    public VerkoopBestellen(IBestelRepository bestel, IVerkoopOrderRepository vo, IArtikelRepository artikelen)
    {
        _bestel = bestel; _vo = vo; _artikelen = artikelen;
    }

    /// <summary>Wat er van een regel besteld wordt (aantal kan lager zijn dan wat nog openstaat: deelbestelling).</summary>
    public sealed record Keuze(VerkoopOrderRegel Regel, decimal Aantal);

    /// <summary>Een aangemaakte bestelling: PO-nummer (Order-id), leverancier, aantal regels, totaal en eventuele opmerking.</summary>
    public sealed record Gemaakt(int BestellingId, string Leverancier, int Regels, decimal Totaal, string? Let);

    /// <summary>
    /// Bestelt de gekozen regels, per leverancier een bestelling. Regels zonder leverancier worden overgeslagen (de
    /// aanroeper toont die niet als keuze). Gooit een fout als er geen werkorder is of de database niet beschikbaar is.
    /// </summary>
    public async Task<List<Gemaakt>> BestelAsync(VerkoopOrder vo, IReadOnlyList<Keuze> keuzes, int? door)
    {
        if (!_bestel.Beschikbaar) throw new InvalidOperationException("Bestellen kan alleen met de live database.");
        if (vo.WerkorderId is not int wo || wo <= 0)
            throw new InvalidOperationException("Deze verkooporder heeft geen werkorder; daar moet de inkoop op komen.");

        var uitkomst = new List<Gemaakt>();
        var vandaag = DateOnly.FromDateTime(DateTime.Today);
        foreach (var groep in keuzes.Where(k => k.Aantal > 0 && k.Regel.LeverancierId is not null)
                                    .GroupBy(k => k.Regel.LeverancierId!.Value))
        {
            var lev = groep.Key;
            var id = await _bestel.NieuweBestellingAsync(lev, wo, door);
            if (id <= 0) throw new InvalidOperationException("De bestelling kon niet aangemaakt worden.");

            var standaard = await _bestel.LeverancierStandaardAsync(lev);
            if (standaard is not null) await _bestel.VulStandaardAsync(id, standaard, afleverenBijStandaard: false);
            var leverdatum = standaard?.Leverdatum(vandaag);
            await _bestel.VoegOpmerkingToeAsync(id, $"Voor verkooporder {vo.Ordernummer} van {vo.Klant}" +
                                                    (vo.PoKlant.Length > 0 ? $" (PO klant {vo.PoKlant})" : "") + ".");

            decimal totaal = 0;
            foreach (var (regel, aantal) in groep)
            {
                // zoals Bestellingen (ArtikelGekozen): bestelnummer bij deze leverancier, prijs per stuk en korting van
                // deze leverancier; anders de code van de regel en de inkoopprijs van de verkooporder als schatting.
                ArtikelLeverancier? al = null;
                Artikel? art = null;
                if (regel.ArtikelId is int aid)
                {
                    try { al = (await _artikelen.LeveranciersAsync(aid)).FirstOrDefault(x => x.LeverancierId == lev); } catch { }
                    try { art = await _artikelen.GetAsync(aid); } catch { }
                }
                var bestelnr = (al?.Artikelnummer ?? "").Trim();
                var fabriek = (art?.CodeFabrikant ?? "").Trim();
                var code = bestelnr.Length > 0 ? bestelnr : regel.Code;
                var oms = regel.Omschrijving;
                if (bestelnr.Length > 0 && fabriek.Length > 0 && fabriek != bestelnr && !oms.StartsWith(fabriek)) oms = fabriek + " " + oms;

                var orderregel = new Orderregel
                {
                    OrderId = id,
                    Aantal = (double)aantal,
                    EenheidId = 2,      // stuks
                    Artikelcode = code.Length > 100 ? code[..100] : code,
                    Omschrijving = oms.Length > 400 ? oms[..400] : oms,
                    MdeCode = regel.ArtikelId?.ToString() ?? "",
                    Leverdatum = leverdatum,
                    WerkorderId = wo,
                };
                if (al is not null && al.PrijsPerVe > 0)
                {
                    orderregel.Prijs = al.StuksPerVe == 0 ? al.PrijsPerVe : Math.Round(al.PrijsPerVe / al.StuksPerVe, 4);
                    orderregel.Korting = al.Korting == 0 ? null : al.Korting;
                }
                else orderregel.Schatprijs = regel.Inkoopprijs;

                var itemId = await _bestel.BewaarRegelAsync(orderregel);
                await _vo.KoppelInkoopAsync(regel.Id, itemId, aantal, door);
                totaal += Orderregel.Bereken(orderregel.Aantal, orderregel.Prijs ?? orderregel.Schatprijs, orderregel.Korting);
            }

            // Geen goedkeuring: meteen In bestelling (vult "aangevraagd op/door", zoals Volgende vanuit Aanmaken).
            await _bestel.ZetStatusAsync(id, BestelFlow.Aanmaken, BestelFlow.InBestelling, door);

            string? let = null;
            try
            {
                if (await _bestel.FlowInfoAsync(id) is { } info && info.MinimumOrder > 0 && totaal < info.MinimumOrder)
                    let = $"onder het minimum orderbedrag van {info.MinimumOrder.ToString("C2", Nl)}" +
                          (info.Orderkosten > 0 ? $" (toeslag {info.Orderkosten.ToString("C2", Nl)} niet toegevoegd)" : "");
            }
            catch { }
            uitkomst.Add(new Gemaakt(id, groep.First().Regel.Leverancier, groep.Count(), totaal, let));
        }
        return uitkomst;
    }
}
