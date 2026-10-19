namespace EverythingApp.Services;

/// <summary>
/// Leest op de achtergrond de factuurpostbus (sectie "FactuurInbox" in appsettings, bv. invoice@mde-automation.nl) en
/// zet elke PDF- of foto-bijlage als regel in de FactuurInbox, al uitgelezen door de AI (FactuurLezer, via Bedrock).
/// Een mail zonder bruikbare bijlage komt er ook in, met "Geen factuur gevonden" (besluit gebruiker 19-10-2026).
/// Er wordt nooit een factuur aangemaakt: dat doet de beoordelaar (Financiën ▸ Inkomende facturen ▸ Te beoordelen).
///
/// Wat al gezien is: een Gmail-label (standaard "EverythingApp/Verwerkt") en, als vangnet, het bericht-id in de tabel.
/// Er wordt niets uit de postbus verwijderd of verplaatst. Werkt alleen met het Google-serviceaccount
/// (domeinbrede delegatie, Google:ServiceAccountJson) en als de postbus een echte gebruiker is, geen groep.
/// </summary>
public class FactuurInboxOphaler : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopes;   // MailService is scoped; per ronde een eigen scope
    private readonly IFactuurInboxRepository _repo;
    private readonly FactuurLezer _lezer;
    private readonly ILogger<FactuurInboxOphaler> _log;
    private readonly SemaphoreSlim _nu = new(0);
    private readonly SemaphoreSlim _bezig = new(1, 1);
    private readonly HashSet<string> _mislukt = new();

    public FactuurInboxOphaler(IConfiguration config, IServiceScopeFactory scopes, IFactuurInboxRepository repo,
                               FactuurLezer lezer, ILogger<FactuurInboxOphaler> log)
    {
        _config = config;
        _scopes = scopes;
        _repo = repo;
        _lezer = lezer;
        _log = log;
    }

    private const long MaxBestand = 15_000_000;       // past in max_allowed_packet (16 MB)
    private const long MinAfbeelding = 20_000;        // kleiner = een logo of icoontje uit de handtekening

    public string Postbus => (_config["FactuurInbox:Postbus"] ?? "").Trim();
    public bool Aan => _config.GetValue("FactuurInbox:Aan", false) && Postbus.Length > 0;
    private int Minuten => Math.Clamp(_config.GetValue("FactuurInbox:Minuten", 10), 2, 240);
    private string Label => string.IsNullOrWhiteSpace(_config["FactuurInbox:Label"]) ? "EverythingApp/Verwerkt" : _config["FactuurInbox:Label"]!.Trim();

    /// <summary>Voor het scherm: wanneer er voor het laatst gekeken is en wat eruit kwam, of waarom het niet lukte.</summary>
    public string Stand { get; private set; } = "";
    public bool StandFout { get; private set; }
    public DateTime? LaatsteKeer { get; private set; }

    /// <summary>Gaat af als er nieuwe regels in de inbox zijn gezet (het scherm ververst dan zijn teller).</summary>
    public event Action? Bijgewerkt;

    /// <summary>Meteen kijken (knop "Nu ophalen"), in plaats van op de volgende ronde te wachten.</summary>
    public void NuOphalen() => _nu.Release();

    private MailPostbus Bus => new("Factuurinbox", Postbus, null, Eigen: false);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!Aan)
        {
            Stand = "Staat uit (appsettings, sectie FactuurInbox: Aan en Postbus).";
            _log.LogInformation("Factuurinbox: {Stand}", Stand);
        }

        try { await Task.Delay(TimeSpan.FromSeconds(30), stop); } catch (OperationCanceledException) { return; }

        while (!stop.IsCancellationRequested)
        {
            if (Aan)
            {
                try { await RondeAsync(stop); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Factuurinbox: ophalen mislukt.");
                    Zet("Ophalen mislukt: " + ex.Message, true);
                }
            }

            try { await _nu.WaitAsync(TimeSpan.FromMinutes(Minuten), stop); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void Zet(string stand, bool fout)
    {
        Stand = stand;
        StandFout = fout;
        LaatsteKeer = DateTime.Now;
    }

    private async Task RondeAsync(CancellationToken stop)
    {
        if (!await _bezig.WaitAsync(0, stop)) return;
        using var scope = _scopes.CreateScope();
        var mail = scope.ServiceProvider.GetRequiredService<MailService>();
        try
        {
            if (!_repo.Beschikbaar || !await _repo.TabelAanwezigAsync())
            {
                Zet("De tabel FactuurInbox ontbreekt (FactuurInbox_MariaDB.sql).", true);
                return;
            }
            if (!mail.Beschikbaar(Bus))
            {
                Zet($"Kan {Postbus} niet openen: {mail.Beschrijf(Bus)}", true);
                return;
            }

            var zoek = $"in:inbox -label:\"{Label}\"";
            if (DateOnly.TryParse(_config["FactuurInbox:Vanaf"], System.Globalization.CultureInfo.InvariantCulture, out var vanaf))
                zoek += $" after:{vanaf:yyyy'/'MM'/'dd}";

            var nieuw = 0;
            var fouten = 0;
            // Per keer hoogstens 4 x 25 berichten; de rest komt de volgende ronde.
            for (var ronde = 0; ronde < 4; ronde++)
            {
                var berichten = await mail.ZoekAsync(Bus, zoek, 25);
                if (berichten.Count == 0) break;

                var bekend = await _repo.BekendeMailsAsync(berichten.Select(b => b.Id).ToList());
                var verder = false;
                foreach (var b in berichten.OrderBy(b => b.Datum ?? DateTime.MinValue))
                {
                    stop.ThrowIfCancellationRequested();
                    if (_mislukt.Contains(b.Id)) continue;
                    verder = true;
                    try
                    {
                        if (!bekend.Contains(b.Id)) nieuw += await VerwerkAsync(mail, b, stop);
                        // Pas na het wegschrijven het label: gaat het label mis, dan houdt het bericht-id in de tabel
                        // een tweede keer tegen.
                        await mail.VoegLabelToeAsync(Bus, new[] { b.Id }, Label);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // Niet elke ronde opnieuw proberen (en opnieuw laten uitlezen); na een herstart wel.
                        _mislukt.Add(b.Id);
                        fouten++;
                        _log.LogWarning(ex, "Factuurinbox: mail {Id} ({Onderwerp}) overgeslagen.", b.Id, b.Onderwerp);
                    }
                }
                if (berichten.Count < 25 || !verder) break;
            }

            var tekst = nieuw == 0 ? "Geen nieuwe facturen." : nieuw == 1 ? "1 nieuwe regel." : $"{nieuw} nieuwe regels.";
            if (fouten > 0) tekst += $" {fouten} mail(s) overgeslagen door een fout (zie het logboek); die blijven in de postbus staan.";
            Zet(tekst, fouten > 0);
            if (nieuw > 0) Bijgewerkt?.Invoke();
        }
        finally { _bezig.Release(); }
    }

    private static bool IsLeesbaar(string naam)
        => Path.GetExtension(naam).ToLowerInvariant() is ".pdf" or ".jpg" or ".jpeg" or ".png" or ".webp";

    private static bool IsAfbeelding(string naam)
        => Path.GetExtension(naam).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif";

    /// <summary>Eén bericht: per PDF of foto een regel; anders één regel "Geen factuur gevonden". Geeft het aantal regels.</summary>
    private async Task<int> VerwerkAsync(MailService mail, MailBericht kop, CancellationToken stop)
    {
        var inhoud = await mail.InhoudAsync(Bus, kop.Id);
        var bericht = inhoud?.Bericht ?? kop;
        var tekst = inhoud?.Tekst ?? kop.Samenvatting;

        var leesbaar = new List<(string Naam, byte[] Inhoud)>();
        var overig = new List<(string Naam, byte[] Inhoud)>();
        var meldingen = new List<string>();

        foreach (var bijlage in bericht.Bijlagen)
        {
            if (bijlage.Grootte > MaxBestand)
            {
                meldingen.Add($"{bijlage.Naam} is te groot ({bijlage.Grootte / 1_000_000} MB) en niet meegenomen.");
                continue;
            }
            if (IsAfbeelding(bijlage.Naam) && bijlage.Grootte < MinAfbeelding) continue;

            byte[]? bytes;
            try { bytes = await mail.BijlageAsync(Bus, bericht.Id, bijlage.Id); }
            catch (Exception ex)
            {
                meldingen.Add($"{bijlage.Naam} kon niet worden opgehaald: {ex.Message}");
                continue;
            }
            if (bytes is null) continue;
            // Twee bijlagen met dezelfde naam zouden op de unieke sleutel (MailId, BijlageNaam) stuklopen.
            var naam = bijlage.Naam;
            for (var n = 2; leesbaar.Concat(overig).Any(x => x.Naam.Equals(naam, StringComparison.OrdinalIgnoreCase)); n++)
                naam = $"{Path.GetFileNameWithoutExtension(bijlage.Naam)} ({n}){Path.GetExtension(bijlage.Naam)}";
            (IsLeesbaar(naam) ? leesbaar : overig).Add((naam, bytes));
        }

        var basis = new InboxStuk
        {
            Bron = "mail", Ontvangen = bericht.Datum ?? DateTime.Now, MailId = bericht.Id,
            MailVan = bericht.Van, MailOnderwerp = bericht.Onderwerp,
            MailTekst = tekst.Length > 4000 ? tekst[..4000] : tekst,
        };

        if (leesbaar.Count == 0)
        {
            meldingen.Insert(0, "Geen factuur gevonden (geen PDF of foto als bijlage).");
            var stuk = Kopie(basis, "", string.Join(" ", meldingen), "");
            await _repo.VoegToeAsync(stuk, overig);
            _log.LogInformation("Factuurinbox: mail {Id} van {Van} zonder factuur.", bericht.Id, bericht.Van);
            return 1;
        }

        var aantal = 0;
        foreach (var (naam, bytes) in leesbaar)
        {
            stop.ThrowIfCancellationRequested();
            var melding = new List<string>(meldingen);
            var gelezen = "";
            try
            {
                var (uit, fout) = await _lezer.LeesAsync(bytes, naam, null, stop);
                if (uit is null) melding.Add("Uitlezen mislukt: " + fout);
                else gelezen = InboxGelezen.NaarJson(uit);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { melding.Add("Uitlezen mislukt: " + ex.Message); }

            // De overige bijlagen (bv. een UBL-xml of de voorwaarden) gaan mee met de eerste factuur uit deze mail.
            var bestanden = new List<(string, byte[])> { (naam, bytes) };
            if (aantal == 0) bestanden.AddRange(overig);
            await _repo.VoegToeAsync(Kopie(basis, naam, string.Join(" ", melding), gelezen), bestanden);
            aantal++;
        }
        _log.LogInformation("Factuurinbox: mail {Id} van {Van}: {Aantal} factuur/facturen.", bericht.Id, bericht.Van, aantal);
        return aantal;
    }

    private static InboxStuk Kopie(InboxStuk b, string bijlage, string melding, string gelezen) => new()
    {
        Bron = b.Bron, Ontvangen = b.Ontvangen, MailId = b.MailId, MailVan = b.MailVan, MailOnderwerp = b.MailOnderwerp,
        MailTekst = b.MailTekst, BijlageNaam = bijlage, Melding = melding, Gelezen = gelezen,
    };
}
