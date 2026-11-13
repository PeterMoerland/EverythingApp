using System.Globalization;
using EverythingApp.Models;

namespace EverythingApp.Services;

// ============================================================================================================
//  Te laat betaalde uitgaande facturen (wens gebruiker 13-11-2026): één keer per dag een melding in de bel van
//  Financial en Directie, en een overzicht als eigen pagina (Financiën ▸ Te laat betaald, /financien/te-laat) dat
//  vanuit de bel in een nieuw venster opent. Geen eigen tabel: 'vandaag al gemeld' volgt uit de tabel Meldingen.
// ============================================================================================================

/// <summary>Een te late factuur met wat er al op binnen is (deelbetalingen).</summary>
public sealed record TeLaatFactuur(VerkoopFactuurRegel Factuur, decimal Ontvangen)
{
    public decimal Open => Factuur.BedragInclBtw - Ontvangen;
    public DateTime? Vervaldatum => Factuur.Factuurdatum?.AddDays(Factuur.Betalingstermijn);
    /// <summary>Dagen over de vervaldatum.</summary>
    public int Dagen => Factuur.Overdue;
}

public static class TeLaatFacturen
{
    /// <summary>Soort van de melding in de bel (tabel Meldingen).</summary>
    public const string Soort = "telaat";
    /// <summary>De pagina met het overzicht (zonder schuine streep vooraan: ten opzichte van de basis van de app).</summary>
    public const string Pagina = "financien/te-laat";

    private static readonly CultureInfo Nl = new("nl-NL");

    /// <summary>
    /// Verstuurd, niet betaald, niet oninbaar en over de vervaldatum: dezelfde facturen als de knop "Te laat" op
    /// Uitgaande facturen (zonder oninbaar). Langst open eerst.
    /// </summary>
    public static async Task<List<TeLaatFactuur>> LaadAsync(IVerkoopFactuurRepository repo, IFactuurBetalingRepository betalingen)
    {
        var filter = new VerkoopFactuurFilter { Verstuurd = Drieluik.Ja, Betaald = Drieluik.Nee, MeerDanOverdue = 0 };
        var lijst = (await repo.ZoekAsync(filter, 2000)).Where(f => f.TeLaat).ToList();

        var ontvangen = new Dictionary<int, decimal>();
        if (lijst.Count > 0)
        {
            try { ontvangen = await betalingen.OntvangenAsync(lijst.Select(f => f.Id).ToList()); }
            catch { /* zonder de tabel met deelbetalingen telt het hele bedrag als open */ }
        }

        return lijst.Select(f => new TeLaatFactuur(f, ontvangen.GetValueOrDefault(f.Id)))
                    .OrderByDescending(t => t.Dagen).ThenBy(t => t.Factuur.Factuurnummer, StringComparer.Ordinal)
                    .ToList();
    }

    /// <summary>De tekst van de dagelijkse melding; leeg als er niets te laat is.</summary>
    public static string Meldtekst(IReadOnlyList<TeLaatFactuur> lijst)
    {
        if (lijst.Count == 0) return "";
        var klanten = lijst.Select(t => t.Factuur.Klant).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var oudste = lijst.OrderByDescending(t => t.Dagen).First();
        var tekst = (lijst.Count == 1 ? "1 factuur aan een klant is te laat" : $"{lijst.Count} facturen aan klanten zijn te laat")
                  + $": samen {lijst.Sum(t => t.Open).ToString("C2", Nl)} open"
                  + (lijst.Count > 1 ? $" bij {klanten} klant{(klanten == 1 ? "" : "en")}" : "") + ". ";
        tekst += (lijst.Count == 1 ? "Het is " : "Langst open: ")
               + $"{oudste.Factuur.Factuurnummer} van {oudste.Factuur.Klant} ({oudste.Dagen} dag{(oudste.Dagen == 1 ? "" : "en")}).";
        return tekst;
    }

    /// <summary>Is het tijd voor de melding van vandaag? Op werkdagen, vanaf het ingestelde uur, één keer per dag.</summary>
    public static bool MoetNu(DateTime nu, int uur, DateOnly? laatstGedaan)
        => nu.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
           && nu.Hour >= uur
           && laatstGedaan != DateOnly.FromDateTime(nu);
}

/// <summary>
/// Maakt op werkdagen één keer per dag (vanaf <c>TeLaatFacturen:Uur</c>, standaard 7 uur) een melding in de bel van
/// iedereen met het recht Financial of Directie, als er facturen te laat zijn. Kijkt elke 10 minuten of het zover
/// is; na een herstart ziet hij aan de tabel Meldingen dat het vandaag al gedaan is. Uit te zetten met
/// <c>TeLaatFacturen:Aan</c> = false; bij lokaal ontwikkelen staat hij standaard uit (anders zou een testversie
/// meldingen in de echte database zetten).
/// </summary>
public sealed class TeLaatWaker : BackgroundService
{
    private readonly IVerkoopFactuurRepository _repo;
    private readonly IFactuurBetalingRepository _betalingen;
    private readonly IMeldingRepository _meldingen;
    private readonly MeldingSignaal _signaal;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<TeLaatWaker> _log;
    private DateOnly? _laatst;

    public TeLaatWaker(IVerkoopFactuurRepository repo, IFactuurBetalingRepository betalingen, IMeldingRepository meldingen,
                       MeldingSignaal signaal, IConfiguration config, IHostEnvironment env, ILogger<TeLaatWaker> log)
    {
        _repo = repo; _betalingen = betalingen; _meldingen = meldingen; _signaal = signaal; _config = config; _env = env; _log = log;
    }

    private bool Aan => _config.GetValue("TeLaatFacturen:Aan", !_env.IsDevelopment());
    private int Uur => Math.Clamp(_config.GetValue("TeLaatFacturen:Uur", 7), 0, 23);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), stop); } catch { return; }
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (Aan && TeLaatFacturen.MoetNu(DateTime.Now, Uur, _laatst)) await MeldAsync();
            }
            catch (Exception ex) { _log.LogWarning(ex, "Te late facturen melden mislukte."); }
            try { await Task.Delay(TimeSpan.FromMinutes(10), stop); } catch { return; }
        }
    }

    /// <summary>De melding van vandaag maken (als dat nog niet gebeurd is). Geeft het aantal ontvangers.</summary>
    public async Task<int> MeldAsync()
    {
        if (!_repo.Beschikbaar || !_meldingen.Beschikbaar || !await _meldingen.TabelAanwezigAsync()) return 0;
        var vandaag = DateOnly.FromDateTime(DateTime.Now);
        if (await _meldingen.VandaagGemaaktAsync(TeLaatFacturen.Soort)) { _laatst = vandaag; return 0; }

        var lijst = await TeLaatFacturen.LaadAsync(_repo, _betalingen);
        _laatst = vandaag;
        if (lijst.Count == 0)
        {
            _log.LogInformation("Te late facturen: vandaag niets te melden.");
            return 0;
        }

        var tekst = TeLaatFacturen.Meldtekst(lijst);
        var wie = await _meldingen.WerknemersMetRechtAsync("Financial", "Directie");
        foreach (var w in wie)
            try { await _meldingen.MaakAsync(w, TeLaatFacturen.Soort, lijst.Count, tekst); }
            catch (Exception ex) { _log.LogWarning(ex, "Te late facturen: melding voor werknemer {Id} mislukte.", w); }
        _signaal.Geef();
        _log.LogInformation("Te late facturen: {Aantal} factu(u)r(en) gemeld aan {Wie} werknemer(s).", lijst.Count, wie.Count);
        return wie.Count;
    }
}
