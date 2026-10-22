namespace EverythingApp.Services;

/// <summary>
/// Eén scherm (tabblad) dat kan bellen en gebeld worden: Components/Layout/Videobellen.razor. De centrale roept deze
/// methoden aan vanuit de verbinding van de ándere kant, dus de component moet zelf naar zijn eigen thread (InvokeAsync).
/// </summary>
public interface IBelDeelnemer
{
    int WerknemerId { get; }
    Task InkomendAsync(BelGesprek gesprek);
    /// <summary>Het gesprek is opgenomen (bij de beller: nu het aanbod maken; bij andere tabbladen van de ontvanger: stoppen met overgaan).</summary>
    Task OpgenomenAsync(BelGesprek gesprek, bool doorJou);
    Task BeeindigdAsync(BelGesprek gesprek, string reden);
    /// <summary>WebRTC-signaal van de andere kant (offer, answer, ice) doorgeven aan de browser.</summary>
    Task SignaalAsync(BelGesprek gesprek, string soort, string data);
}

public enum BelStand { GaatOver, Actief, Klaar }

public class BelGesprek
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public int Beller { get; init; }
    public string BellerNaam { get; init; } = "";
    public int Ontvanger { get; init; }
    public string OntvangerNaam { get; init; } = "";
    public BelStand Stand { get; set; } = BelStand.GaatOver;
    public DateTime Begin { get; } = DateTime.Now;
    public DateTime? Opgenomen { get; set; }
    internal IBelDeelnemer? BellerScherm { get; init; }
    internal IBelDeelnemer? OntvangerScherm { get; set; }

    /// <summary>De naam van de ander, gezien vanaf <paramref name="wn"/>.</summary>
    public string Ander(int wn) => wn == Beller ? OntvangerNaam : BellerNaam;
}

/// <summary>
/// Videobellen op het intranet (wens gebruiker 22-10-2026): camera en microfoon gaan rechtstreeks van browser naar
/// browser (WebRTC, wwwroot/videobellen.js); deze centrale regelt alleen wie er online is, het overgaan, opnemen en
/// ophangen, en geeft de verbindingsgegevens (SDP en ICE) door tussen de twee schermen. In het geheugen van de server:
/// na een herstart van de app is er niemand online en zijn lopende gesprekken weg.
/// </summary>
public class BelCentrale
{
    private static readonly TimeSpan OvergaanMax = TimeSpan.FromSeconds(40);
    private readonly object _slot = new();
    private readonly List<IBelDeelnemer> _schermen = new();
    private readonly Dictionary<string, BelGesprek> _gesprekken = new();
    private readonly ILogger<BelCentrale> _log;
    private readonly IMeldingRepository _meldingen;
    private readonly MeldingSignaal _signaal;

    public BelCentrale(ILogger<BelCentrale> log, IMeldingRepository meldingen, MeldingSignaal signaal)
    {
        _log = log;
        _meldingen = meldingen;
        _signaal = signaal;
    }

    /// <summary>Wie er nu met een tabblad van de app online is (voor het groene bolletje in de berichtenbox).</summary>
    public event Action? OnlineGewijzigd;

    public IReadOnlySet<int> Online()
    {
        lock (_slot) return _schermen.Select(s => s.WerknemerId).ToHashSet();
    }

    public void Aanmelden(IBelDeelnemer scherm)
    {
        lock (_slot) { if (!_schermen.Contains(scherm)) _schermen.Add(scherm); }
        OnlineGewijzigd?.Invoke();
    }

    /// <summary>Tabblad dicht (of andere medewerker op de werkplek): een gesprek van dit scherm wordt beëindigd.</summary>
    public void Afmelden(IBelDeelnemer scherm)
    {
        List<BelGesprek> weg;
        lock (_slot)
        {
            _schermen.Remove(scherm);
            weg = _gesprekken.Values.Where(g => g.BellerScherm == scherm || g.OntvangerScherm == scherm).ToList();
        }
        foreach (var g in weg) _ = BeeindigAsync(g.Id, scherm, "De verbinding van de ander is verbroken.");
        // overgaan bij de ontvanger terwijl die zijn laatste tabblad sluit: niemand meer om op te nemen
        lock (_slot)
        {
            var zonder = _gesprekken.Values.Where(g => g.Stand == BelStand.GaatOver && g.OntvangerScherm is null
                                                         && !_schermen.Any(s => s.WerknemerId == g.Ontvanger)).ToList();
            foreach (var g in zonder) _ = BeeindigAsync(g.Id, null, "Niet bereikbaar.");
        }
        OnlineGewijzigd?.Invoke();
    }

    /// <summary>Bellen. Geeft het gesprek, of een reden waarom het niet kan.</summary>
    public async Task<(BelGesprek? Gesprek, string? Reden)> BelAsync(IBelDeelnemer beller, string bellerNaam, int naar, string naarNaam)
    {
        BelGesprek g;
        List<IBelDeelnemer> doel;
        lock (_slot)
        {
            if (naar == beller.WerknemerId) return (null, "Jezelf bellen kan niet.");
            doel = _schermen.Where(s => s.WerknemerId == naar).ToList();
            if (doel.Count == 0) return (null, $"{naarNaam} is nu niet online in de app.");
            if (_gesprekken.Values.Any(x => x.Stand != BelStand.Klaar && (x.Beller == naar || x.Ontvanger == naar)))
                return (null, $"{naarNaam} is al in gesprek.");
            if (_gesprekken.Values.Any(x => x.Stand != BelStand.Klaar && (x.BellerScherm == beller || x.OntvangerScherm == beller)))
                return (null, "Je bent al in gesprek.");
            g = new BelGesprek { Beller = beller.WerknemerId, BellerNaam = bellerNaam, Ontvanger = naar, OntvangerNaam = naarNaam, BellerScherm = beller };
            _gesprekken[g.Id] = g;
        }
        _log.LogInformation("Videobellen: {Van} belt {Naar} ({Id})", g.Beller, g.Ontvanger, g.Id);
        foreach (var s in doel) await Veilig(() => s.InkomendAsync(g));
        _ = NietOpgenomenNa(g);
        return (g, null);
    }

    private async Task NietOpgenomenNa(BelGesprek g)
    {
        await Task.Delay(OvergaanMax);
        if (g.Stand != BelStand.GaatOver) return;
        await BeeindigAsync(g.Id, null, "Niet opgenomen.");
        // gemiste oproep in de berichtenbox van de ontvanger (soort 'gemist', van = de beller)
        try
        {
            if (_meldingen.Beschikbaar && await _meldingen.TabelAanwezigAsync())
            {
                await _meldingen.MaakAsync(g.Ontvanger, "gemist", null, $"Gemiste videooproep van {g.BellerNaam} om {g.Begin:HH:mm}.");
                _signaal.Geef();
            }
        }
        catch (Exception ex) { _log.LogInformation("Videobellen: gemiste oproep niet opgeslagen: {Fout}", ex.Message); }
    }

    public async Task<string?> NeemOpAsync(string id, IBelDeelnemer scherm)
    {
        BelGesprek? g;
        List<IBelDeelnemer> anderen;
        lock (_slot)
        {
            if (!_gesprekken.TryGetValue(id, out g) || g.Stand != BelStand.GaatOver) return "Het gesprek is er niet meer.";
            if (scherm.WerknemerId != g.Ontvanger) return "Dit gesprek is niet voor jou.";
            g.OntvangerScherm = scherm;
            g.Stand = BelStand.Actief;
            g.Opgenomen = DateTime.Now;
            anderen = _schermen.Where(s => s.WerknemerId == g.Ontvanger && s != scherm).ToList();
        }
        foreach (var s in anderen) await Veilig(() => s.OpgenomenAsync(g, false));      // ander tabblad: stoppen met overgaan
        if (g.BellerScherm is { } b) await Veilig(() => b.OpgenomenAsync(g, true));      // beller: nu het aanbod maken
        return null;
    }

    /// <summary>Ophangen, weigeren, annuleren of verlopen: beide kanten (en overgaande tabbladen) horen het.</summary>
    public async Task BeeindigAsync(string id, IBelDeelnemer? door, string reden)
    {
        BelGesprek? g;
        List<IBelDeelnemer> melden;
        lock (_slot)
        {
            if (!_gesprekken.Remove(id, out g)) return;
            g.Stand = BelStand.Klaar;
            melden = new List<IBelDeelnemer>();
            if (g.BellerScherm is { } b) melden.Add(b);
            if (g.OntvangerScherm is { } o) melden.Add(o);
            else melden.AddRange(_schermen.Where(s => s.WerknemerId == g.Ontvanger));    // ging nog over
            if (door is not null) melden.Remove(door);
        }
        _log.LogInformation("Videobellen: gesprek {Id} beëindigd ({Reden})", g.Id, reden);
        foreach (var s in melden.Distinct()) await Veilig(() => s.BeeindigdAsync(g, reden));
    }

    /// <summary>offer / answer / ice van het ene scherm naar het andere.</summary>
    public async Task SignaalAsync(string id, IBelDeelnemer van, string soort, string data)
    {
        IBelDeelnemer? naar;
        BelGesprek? g;
        lock (_slot)
        {
            if (!_gesprekken.TryGetValue(id, out g)) return;
            naar = van == g.BellerScherm ? g.OntvangerScherm : van == g.OntvangerScherm ? g.BellerScherm : null;
        }
        if (naar is not null) await Veilig(() => naar.SignaalAsync(g, soort, data));
    }

    private async Task Veilig(Func<Task> actie)
    {
        try { await actie(); }
        catch (Exception ex) { _log.LogInformation("Videobellen: scherm niet bereikbaar: {Fout}", ex.Message); }
    }
}

/// <summary>Per verbinding: "bel deze collega" vanuit bv. de berichtenbox naar het belvenster (Videobellen.razor).</summary>
public class BelVerzoek
{
    public event Func<int, string, Task>? Gevraagd;
    public Task BelAsync(int werknemerId, string naam) => Gevraagd?.Invoke(werknemerId, naam) ?? Task.CompletedTask;
}
