namespace EverythingApp.Services;

/// <summary>
/// Herinnering voor verzekeringen (wens gebruiker 9-11-2026): vanaf <see cref="Verzekering.HerinneringDagen"/> dagen
/// vóór de uiterste opzegdatum (volgende vervaldatum min de opzegtermijn) een melding in de bel van de Directie en van
/// de beheerder van de verzekering. Eén keer per vervaldatum (Verzekeringen.GemeldVoor). Kijkt elke 6 uur, de eerste
/// keer 2 minuten na het starten. Alleen actieve verzekeringen met een contractvervaldatum.
/// </summary>
public sealed class VerzekeringWaker : BackgroundService
{
    private readonly IVerzekeringRepository _repo;
    private readonly IMeldingRepository _meldingen;
    private readonly ILogger<VerzekeringWaker> _log;

    public VerzekeringWaker(IVerzekeringRepository repo, IMeldingRepository meldingen, ILogger<VerzekeringWaker> log)
    {
        _repo = repo; _meldingen = meldingen; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stop); } catch { return; }
        while (!stop.IsCancellationRequested)
        {
            try { await KijkAsync(); }
            catch (Exception ex) { _log.LogWarning(ex, "Verzekeringen nakijken voor herinneringen mislukte."); }
            try { await Task.Delay(TimeSpan.FromHours(6), stop); } catch { return; }
        }
    }

    public async Task<int> KijkAsync()
    {
        if (!_repo.Beschikbaar || !await _repo.TabelAanwezigAsync() || !await _meldingen.TabelAanwezigAsync()) return 0;
        var vandaag = DateOnly.FromDateTime(DateTime.Today);
        var verzonden = 0;
        List<int>? directie = null;
        foreach (var v in await _repo.LijstAsync())
        {
            if (v.Status != VerzekeringStatus.Actief) continue;
            if (v.VolgendeVervaldatum(vandaag) is not DateOnly verval || v.UitersteOpzegdatum(vandaag) is not DateOnly opzeg) continue;
            if (vandaag < opzeg.AddDays(-Verzekering.HerinneringDagen) || v.GemeldVoor == verval) continue;

            directie ??= await _repo.DirectieAsync();
            var wie = directie.ToHashSet();
            if (v.BeheerderId is int b) wie.Add(b);
            var tekst = $"Verzekering {v.Omschrijving}" + (v.Polisnummer.Length > 0 ? $" (polis {v.Polisnummer})" : "") +
                        $" verlengt op {verval:dd-MM-yyyy}; " +
                        (vandaag <= opzeg ? $"opzeggen of opnieuw onderhandelen kan tot {opzeg:dd-MM-yyyy}." : $"de opzegdatum ({opzeg:dd-MM-yyyy}) is al voorbij.");
            foreach (var w in wie)
                try { await _meldingen.MaakAsync(w, "verzekering", v.Id, tekst); } catch { }
            await _repo.ZetGemeldAsync(v.Id, verval);
            verzonden++;
            _log.LogInformation("Herinnering verzekering {Id} ({Oms}) voor vervaldatum {Verval} aan {Aantal} werknemer(s).", v.Id, v.Omschrijving, verval, wie.Count);
        }
        return verzonden;
    }
}
