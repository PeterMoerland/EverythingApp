using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Verkooporders stap 3 (wens gebruiker 9-11-2026): leveren met een pakbon en factureren, ook in delen.
/// - Pakbon: een nieuwe pakbon voor de klant (Pakbonnen ▸ status Aanmaken) met de gekozen regels en aantallen; de PO van de
///   klant als referentie (staat op de afdruk). Afdrukken en verzenden gaan daarna gewoon in Pakbonnen.
/// - Factuur: de pagina opent het gewone factuurvenster met een voorstel (verkoopprijs × aantal); na opslaan koppelt
///   <see cref="KoppelFactuurAsync"/> de opgeslagen factuurregels aan de orderregels.
/// Elke pakbon- en factuurregel komt in VerkoopOrderKoppelingen, zodat de order bijhoudt wat geleverd en gefactureerd is.
/// </summary>
public class VerkoopAfhandeling
{
    private readonly IPakbonRepository _pakbonnen;
    private readonly IVerkoopFactuurRepository _facturen;
    private readonly IVerkoopOrderRepository _vo;

    public VerkoopAfhandeling(IPakbonRepository pakbonnen, IVerkoopFactuurRepository facturen, IVerkoopOrderRepository vo)
    {
        _pakbonnen = pakbonnen; _facturen = facturen; _vo = vo;
    }

    /// <summary>Een regel met het aantal dat nu geleverd of gefactureerd wordt.</summary>
    public sealed record Keuze(VerkoopOrderRegel Regel, decimal Aantal);

    /// <summary>Maakt de pakbon en koppelt de regels; geeft het PakbonID.</summary>
    public async Task<int> MaakPakbonAsync(VerkoopOrder vo, IReadOnlyList<Keuze> keuzes, int? door)
    {
        if (!_pakbonnen.Beschikbaar) throw new InvalidOperationException("Pakbonnen werken alleen met de live database.");
        var lijst = keuzes.Where(k => k.Aantal > 0).ToList();
        if (lijst.Count == 0) throw new InvalidOperationException("Er is niets gekozen om te leveren.");

        var id = await _pakbonnen.NieuwAsync(vo.KlantId, vo.ContactId, door);
        if (id <= 0) throw new InvalidOperationException("De pakbon kon niet aangemaakt worden.");
        try
        {
            if (await _pakbonnen.GetAsync(id) is { } kop)
            {
                kop.Opmerking = $"Verkooporder {vo.Ordernummer}";
                await _pakbonnen.BewaarKopAsync(kop);
            }
        }
        catch { /* opmerking is bijzaak */ }

        var referentie = vo.PoKlant.Length > 0 ? vo.PoKlant : vo.Ordernummer;
        foreach (var (regel, aantal) in lijst)
        {
            decimal gewicht = 0;
            if (regel.ArtikelId is int a)
                try { gewicht = (await _pakbonnen.ArtikelAsync(a))?.Gewicht ?? 0; } catch { }
            var item = new PakbonItem
            {
                PakbonId = id, Aantal = aantal, Artikelnummer = Kort(regel.Code, 100), Omschrijving = Kort(regel.Omschrijving, 255),
                MdeId = regel.ArtikelId, Gewicht = gewicht, ReferentieKlant = Kort(referentie, 100),
            };
            var itemId = await _pakbonnen.VoegRegelToeAsync(item);
            await _vo.KoppelAsync(regel.Id, VerkoopOrderStatus.SoortPakbon, itemId, aantal, door, id, "PS-" + id);
        }
        return id;
    }

    /// <summary>Een factuurregel van het voorstel: de tekst zoals die op de factuur komt, met de orderregel erbij.</summary>
    public sealed record FactuurRegel(VerkoopOrderRegel Regel, string Tekst);

    /// <summary>De tekst van een orderregel op de factuur: code en omschrijving.</summary>
    public static string FactuurTekst(VerkoopOrderRegel r)
        => string.Join(" ", new[] { r.Code.Trim(), r.Omschrijving.Trim() }.Where(t => t.Length > 0));

    /// <summary>
    /// Na het opslaan van de factuur: de opgeslagen regels opzoeken op hun tekst (in volgorde, elke regel één keer) en
    /// koppelen met het aantal dat er nu op staat. Een regel die in het venster is weggehaald of hernoemd wordt niet
    /// gekoppeld; dat staat in de teruggegeven tekst. Geeft (gekoppeld, melding).
    /// </summary>
    public async Task<(int Gekoppeld, string? Let)> KoppelFactuurAsync(int factuurId, IReadOnlyList<FactuurRegel> voorstel, int? door)
    {
        var f = await _facturen.GetAsync(factuurId);
        if (f is null) return (0, "De factuur is niet teruggevonden; niets gekoppeld.");
        var vrij = f.Regels.Where(r => r.Id > 0).ToList();
        var nummer = f.Factuurnummer;
        int n = 0;
        var mis = new List<string>();
        foreach (var v in voorstel)
        {
            var item = vrij.FirstOrDefault(r => string.Equals(r.Omschrijving.Trim(), v.Tekst, StringComparison.OrdinalIgnoreCase));
            if (item is null) { mis.Add(v.Regel.Code.Length > 0 ? v.Regel.Code : v.Tekst); continue; }
            vrij.Remove(item);
            await _vo.KoppelAsync(v.Regel.Id, VerkoopOrderStatus.SoortFactuur, item.Id, item.Aantal ?? 0, door, factuurId, nummer);
            n++;
        }
        return (n, mis.Count == 0 ? null
            : $"Niet gekoppeld (in de factuur weggehaald of de tekst gewijzigd): {string.Join(", ", mis)}. Die regels blijven als nog te factureren staan.");
    }

    private static string Kort(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
}
