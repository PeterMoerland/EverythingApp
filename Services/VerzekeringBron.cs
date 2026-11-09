using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// De verzekering is de enige bron van de premie (besluit gebruiker 9-11-2026). Bij elk opslaan maakt of werkt dit de
/// bijbehorende vaste last bij (categorie Verzekeringen: omschrijving, BV, bedrag, termijn, betaaldag), zodat Vaste lasten
/// en Liquiditeit hem gewoon meenemen. Opgezegd of beëindigd = de vaste last krijgt een einddatum (de dag vóór de
/// vervaldatum; zonder vervaldatum vandaag). Geen premie meer = de vaste last gaat weg.
/// Werkt pas als VerzekeringenBron_MariaDB.sql gedraaid is (PremieDag/PremieMaand, VasteLasten.Einddatum); daarvóór
/// blijft het zoals het was (de premie komt dan uit de gekoppelde vaste last).
/// </summary>
public class VerzekeringBron
{
    private readonly IVerzekeringRepository _repo;
    private readonly IVasteLastenRepository _vl;

    public VerzekeringBron(IVerzekeringRepository repo, IVasteLastenRepository vl)
    {
        _repo = repo; _vl = vl;
    }

    public async Task<bool> ActiefAsync() => _vl.Beschikbaar && await _repo.BronKolommenAsync();

    /// <summary>Opslaan en de vaste last bijwerken.</summary>
    public async Task BewaarAsync(Verzekering v, int? door)
    {
        await _repo.BewaarAsync(v, door);
        if (!await ActiefAsync()) return;
        var was = v.VasteLastId;
        await WerkVasteLastBij(v, null);
        if (v.VasteLastId != was) await _repo.BewaarAsync(v, door);
    }

    /// <summary>Wissen; de vaste last die bij de verzekering hoort gaat mee. Geeft een reden als het niet mag.</summary>
    public async Task<string?> WisAsync(Verzekering v)
    {
        if (await _repo.WisAsync(v.Id) is { } reden) return reden;
        if (v.VasteLastId is int id && await ActiefAsync()) await _vl.WisAsync(id);
        return null;
    }

    /// <summary>
    /// Na het script: de premie, termijn en betaaldag van de al gekoppelde vaste lasten overnemen in de verzekering
    /// (alleen waar de verzekering nog geen premie heeft). Geeft het aantal.
    /// </summary>
    public async Task<int> NeemPremieOverAsync(int? door)
    {
        if (!await ActiefAsync()) return 0;
        var lasten = await _vl.LijstAsync();
        var n = 0;
        foreach (var v in await _repo.LijstAsync())
        {
            if (v.Premie is not null || v.VasteLastId is not int id || lasten.FirstOrDefault(l => l.Id == id) is not { } last) continue;
            NeemOver(v, last);
            await _repo.BewaarAsync(v, door);
            await WerkVasteLastBij(v, lasten);
            n++;
        }
        return n;
    }

    /// <summary>Van elke nog niet gekoppelde vaste last in de categorie Verzekeringen een verzekering maken.</summary>
    public async Task<int> OvernemenAsync(int? standaardBv, int? door)
    {
        var lasten = await _vl.LijstAsync();
        var n = 0;
        foreach (var vl in await _repo.NietGekoppeldAsync())
        {
            var v = new Verzekering
            {
                Omschrijving = vl.Omschrijving, Soort = VerzekeringSoort.Raad(vl.Omschrijving), VasteLastId = vl.VasteLastId,
                BvId = vl.BvId ?? standaardBv,
            };
            if (lasten.FirstOrDefault(l => l.Id == vl.VasteLastId) is { } last) NeemOver(v, last);
            await _repo.BewaarAsync(v, door);
            n++;
        }
        return n;
    }

    private static void NeemOver(Verzekering v, VasteLast last)
    {
        v.Premie = last.Bedrag;
        v.PremieInterval = last.Interval;
        v.PremieDag = last.Dag;
        v.PremieMaand = last.Maand;
        v.BvId ??= last.BvId;
    }

    /// <summary>De einddatum van de premie: actief = geen; opgezegd/beëindigd = de dag vóór de vervaldatum (anders vandaag).</summary>
    public static DateOnly? Einddatum(Verzekering v)
    {
        if (v.Status == VerzekeringStatus.Actief) return null;
        var vandaag = DateOnly.FromDateTime(DateTime.Today);
        return v.VolgendeVervaldatum(vandaag) is DateOnly verval ? verval.AddDays(-1) : vandaag;
    }

    private async Task WerkVasteLastBij(Verzekering v, List<VasteLast>? lasten)
    {
        if (v.Premie is not decimal premie || premie <= 0 || v.PremieInterval is not VasteLastInterval interval)
        {
            // geen premie (meer): de vaste last hoort er niet meer bij
            if (v.VasteLastId is int weg) { await _vl.WisAsync(weg); v.VasteLastId = null; }
            return;
        }
        lasten ??= await _vl.LijstAsync();
        var id = v.VasteLastId is int bestaand && lasten.Any(l => l.Id == bestaand) ? bestaand : 0;   // weg = opnieuw maken
        var omschrijving = v.Omschrijving.Trim();
        var last = new VasteLast
        {
            Id = id,
            Omschrijving = omschrijving.Length > 45 ? omschrijving[..45] : omschrijving,
            Bedrag = premie,
            Interval = interval,
            CategorieId = await CategorieAsync(),
            Dag = v.PremieDag,
            Maand = v.PremieMaand,
            BvId = v.BvId,
            Einddatum = Einddatum(v),
        };
        await _vl.BewaarAsync(last);
        v.VasteLastId = last.Id;
    }

    private int? _categorie;

    /// <summary>De categorie "Verzekeringen" van de vaste lasten (zoekt op "verzeker"; maakt hem anders aan).</summary>
    private async Task<int?> CategorieAsync()
    {
        if (_categorie is int c) return c;
        try
        {
            var bestaand = (await _vl.CategorieenAsync()).FirstOrDefault(x => x.Omschrijving.Contains("verzeker", StringComparison.OrdinalIgnoreCase));
            _categorie = bestaand?.Id ?? await _vl.CategorieToevoegenAsync("Verzekeringen");
        }
        catch { }
        return _categorie;
    }
}
