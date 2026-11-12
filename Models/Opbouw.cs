namespace EverythingApp.Models;

/// <summary>
/// Eén item in de opbouwboom van een producttype (tab Opbouw; ProductOpbouwBoom_MariaDB.sql). Een item met iets eronder is
/// een fase of subfase; een item zonder iets eronder is een stap die je bij een exemplaar afvinkt.
/// </summary>
public class OpbouwStap
{
    public int Id { get; set; }
    public int ProductNameId { get; set; }
    /// <summary>Het item waar dit onder hangt; null = bovenste niveau.</summary>
    public int? ParentId { get; set; }
    /// <summary>Volgorde binnen de items met dezelfde ouder.</summary>
    public int Volgorde { get; set; }
    /// <summary>Alleen nog herkomst: OpbouwSoort.Opbouw of OpbouwSoort.Controle (de vroegere eindchecks).</summary>
    public string Soort { get; set; } = OpbouwSoort.Opbouw;
    public string Naam { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public bool Verplicht { get; set; } = true;
    /// <summary>(Sub)fase: wat eronder hangt moet van boven naar beneden worden afgewerkt.</summary>
    public bool OpVolgorde { get; set; }
    public int AantalDocumenten { get; set; }

    // Bij een exemplaar: de status van dit item (stap afgevinkt, of hoofdfase afgerond).
    public bool Klaar { get; set; }
    /// <summary>Hoofdfase overgeslagen in plaats van afgerond (dan is Klaar ook waar).</summary>
    public bool Overgeslagen { get; set; }
    public DateTime? KlaarOp { get; set; }
    public string KlaarDoor { get; set; } = "";
    public string Opmerking { get; set; } = "";

    /// <summary>Moet deze stap af zijn? (Een eindcheck van vóór de boom was altijd verplicht.)</summary>
    public bool Moet => Verplicht || Soort == OpbouwSoort.Controle;
}

/// <summary>
/// De opbouwboom van een producttype, met de regels voor fases (wens gebruiker 12-11-2026, voorstel Sjoerd Quist):
/// <list type="bullet">
/// <item>Staat er op het bovenste niveau een item met iets eronder, dan is het een type met fases: alle items op het
/// bovenste niveau zijn hoofdfases. Die gaan op volgorde; de eerste die niet is afgerond is de actieve fase.</item>
/// <item>Een hoofdfase is af als hij met de knop is afgerond (of overgeslagen). Een subfase is af als alles eronder af is;
/// een stap als hij is afgevinkt of niet verplicht is.</item>
/// <item>Werken kan alleen in de actieve hoofdfase; staat bij een (sub)fase "op volgorde" aan, dan ook pas als alles
/// erboven binnen die (sub)fase af is.</item>
/// </list>
/// Zonder fases (alleen losse stappen) is alles vrij, zoals voorheen.
/// </summary>
public sealed class OpbouwBoom
{
    private static readonly IReadOnlyList<OpbouwStap> Leeg = Array.Empty<OpbouwStap>();
    private readonly Dictionary<int, OpbouwStap> _opId;
    private readonly Dictionary<int, List<OpbouwStap>> _kinderen;

    public OpbouwBoom(IEnumerable<OpbouwStap> items)
    {
        Alle = items.OrderBy(s => s.Volgorde).ThenBy(s => s.Id).ToList();
        _opId = Alle.ToDictionary(s => s.Id);
        // een ouder die niet bestaat of een kringverwijzing: het item hoort dan op het bovenste niveau
        var wortel = new HashSet<int>();
        foreach (var s in Alle)
        {
            var gezien = new HashSet<int> { s.Id };
            var p = s.ParentId;
            while (p is int id)
            {
                if (!_opId.TryGetValue(id, out var ouder) || !gezien.Add(id)) { wortel.Add(s.Id); break; }
                p = ouder.ParentId;
            }
        }
        OpId = s => s.ParentId is int p && !wortel.Contains(s.Id) && _opId.TryGetValue(p, out var o) ? o : null;
        _kinderen = Alle.Where(s => OpId(s) is not null).GroupBy(s => s.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        Wortels = Alle.Where(s => OpId(s) is null).ToList();
        MetFases = Wortels.Any(IsGroep);
    }

    private Func<OpbouwStap, OpbouwStap?> OpId { get; }

    public List<OpbouwStap> Alle { get; }
    /// <summary>De items op het bovenste niveau, op volgorde.</summary>
    public List<OpbouwStap> Wortels { get; }
    /// <summary>Type met fases: op het bovenste niveau staat minstens één item met iets eronder.</summary>
    public bool MetFases { get; }
    /// <summary>De hoofdfases (leeg bij een type zonder fases).</summary>
    public IReadOnlyList<OpbouwStap> Hoofdfases => MetFases ? Wortels : Leeg;
    /// <summary>De eerste hoofdfase die niet is afgerond; null = alles af of geen fases.</summary>
    public OpbouwStap? ActieveFase => MetFases ? Wortels.FirstOrDefault(w => !w.Klaar) : null;

    public OpbouwStap? Ouder(OpbouwStap s) => OpId(s);
    public IReadOnlyList<OpbouwStap> Kinderen(OpbouwStap s) => _kinderen.TryGetValue(s.Id, out var l) ? l : Leeg;
    public IReadOnlyList<OpbouwStap> Broers(OpbouwStap s) => Ouder(s) is { } o ? Kinderen(o) : Wortels;
    public bool IsGroep(OpbouwStap s) => Kinderen(s).Count > 0;
    public bool IsHoofdfase(OpbouwStap s) => MetFases && Ouder(s) is null;
    public int Diepte(OpbouwStap s) => Ouder(s) is { } o ? Diepte(o) + 1 : 0;
    /// <summary>Het volgnummer van een hoofdfase (1, 2, …); 0 als het geen hoofdfase is.</summary>
    public int FaseNummer(OpbouwStap s) => IsHoofdfase(s) ? Wortels.IndexOf(s) + 1 : 0;

    /// <summary>Alles wat onder een item hangt, op elke diepte.</summary>
    public IEnumerable<OpbouwStap> Nakomelingen(OpbouwStap s) => Kinderen(s).SelectMany(k => new[] { k }.Concat(Nakomelingen(k)));
    /// <summary>De stappen (items zonder iets eronder) onder een item.</summary>
    public IEnumerable<OpbouwStap> Stappen(OpbouwStap s) => Nakomelingen(s).Where(n => !IsGroep(n));

    /// <summary>Is dit item af? Zie de regels bij de klasse.</summary>
    public bool Af(OpbouwStap s) => IsHoofdfase(s) ? s.Klaar : IsGroep(s) ? Kinderen(s).All(Af) : s.Klaar || !s.Moet;
    /// <summary>Is alles onder deze (hoofd)fase af (voor de knop Fase klaar)?</summary>
    public bool InhoudAf(OpbouwStap s) => Kinderen(s).All(Af);

    /// <summary>Mag hier nu gewerkt worden (afvinken)?</summary>
    public bool Vrij(OpbouwStap s)
    {
        var ouder = Ouder(s);
        if (ouder is null) return !MetFases || s.Id == ActieveFase?.Id;
        if (!Vrij(ouder)) return false;
        if (!ouder.OpVolgorde) return true;
        foreach (var broer in Kinderen(ouder))
        {
            if (broer.Id == s.Id) return true;
            if (!Af(broer)) return false;      // iets erboven is nog niet af
        }
        return true;
    }

    /// <summary>Alles af: alle hoofdfases afgerond, of (zonder fases) alle verplichte stappen afgevinkt.</summary>
    public bool AllesAf => Alle.Count > 0 && Wortels.All(Af);
}

/// <summary>
/// Een fout tijdens de opbouw van een exemplaar (voorstel Sjoerd Quist deel 3, ProductOpbouwDeel3_MariaDB.sql): zolang
/// hij open staat is de opbouw op slot. Oplossen gebeurt met de hand, met wat er gedaan is en eventueel foto's
/// (FileStore, OpbouwStore.Blokkade). Iets anders dan de status FAILED: dat is definitief afgekeurd.
/// </summary>
public class OpbouwBlokkade
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    /// <summary>De hoofdfase waarin de fout is gemeld; null bij een type zonder fases.</summary>
    public int? FaseStapId { get; set; }
    public string Melding { get; set; } = "";
    public DateTime GemeldOp { get; set; }
    public string GemeldDoor { get; set; } = "";
    public string Oplossing { get; set; } = "";
    public DateTime? OpgelostOp { get; set; }
    public string OpgelostDoor { get; set; } = "";
    public int AantalFotos { get; set; }
    public bool Open => OpgelostOp is null;
}

public static class OpbouwSoort
{
    public const string Opbouw = "opbouw";
    public const string Controle = "controle";
}

public static class OpbouwStore
{
    /// <summary>
    /// FileStore.StoreType voor hands-on documenten bij een opbouwstap (MasterID = OpbouwStapID). Was 330, maar dat is
    /// de soort van de bezoekverslagen (BezoekVenster.StoreTypeBezoek): een document bij stap 7 zou dan ook bij
    /// bezoekverslag 7 staan. Sinds 12-11-2026 een eigen soort; er stonden toen nog geen bestanden met 330.
    /// </summary>
    public const int Stap = 331;
    /// <summary>FileStore.StoreType voor foto's bij een fout in de opbouw (MasterID = BlokkadeID).</summary>
    public const int Blokkade = 332;
}
