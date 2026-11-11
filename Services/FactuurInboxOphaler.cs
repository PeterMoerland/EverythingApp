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
                        if (!bekend.Contains(b.Id)) nieuw += (await VerwerkAsync(mail, b, stop)).Aantal;
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

    private const long MinIngebedFoto = 100_000;      // een foto in de tekst zelf: kleiner is een banner of logo

    /// <summary>Wat een onderdeel is: op de extensie, anders op het MIME-type (een PDF zonder extensie, een geplakte foto).</summary>
    private static string Soort(MailBijlage b)
    {
        var ext = Path.GetExtension(b.Naam).ToLowerInvariant();
        var mime = b.Soort.ToLowerInvariant();
        if (ext == ".pdf" || mime == "application/pdf") return "pdf";
        if (ext is ".jpg" or ".jpeg" || mime is "image/jpeg" or "image/jpg" or "image/pjpeg") return "jpg";
        if (ext == ".png" || mime == "image/png") return "png";
        if (ext == ".webp" || mime == "image/webp") return "webp";
        if (ext is ".heic" or ".heif" || mime is "image/heic" or "image/heif") return "heic";
        if (ext == ".gif" || mime == "image/gif") return "gif";
        return "";
    }

    private static bool IsLeesbaar(string soort) => soort is "pdf" or "jpg" or "png" or "webp";

    /// <summary>Naam met de juiste extensie (de lezer kijkt naar de extensie); zonder naam: "bijlage.pdf".</summary>
    private static string NaamMetExtensie(MailBijlage b, string soort)
    {
        var naam = string.IsNullOrWhiteSpace(b.Naam) ? (b.Inline ? "foto in de mail" : "bijlage") : b.Naam.Trim();
        var ext = "." + soort;
        var huidig = Path.GetExtension(naam).ToLowerInvariant();
        if (huidig == ext || (soort == "jpg" && huidig == ".jpeg")) return naam;
        return naam + ext;
    }

    /// <summary>Een tijdelijke fout van Bedrock (503, te druk, time-out): dan nog eens proberen.</summary>
    private static bool Tijdelijk(string? fout)
        => fout is not null && (fout.Contains("503") || fout.Contains("429") || fout.Contains("unable to process", StringComparison.OrdinalIgnoreCase)
                                || fout.Contains("throttl", StringComparison.OrdinalIgnoreCase) || fout.Contains("too many", StringComparison.OrdinalIgnoreCase)
                                || fout.Contains("timeout", StringComparison.OrdinalIgnoreCase) || fout.Contains("time-out", StringComparison.OrdinalIgnoreCase)
                                || fout.Contains("overloaded", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Uitlezen met tot drie nieuwe pogingen bij een tijdelijke fout (wens gebruiker 11-11-2026: na een 503 van Bedrock bleef
    /// de regel voor altijd "mislukt"). Geeft wat gelezen is als JSON, of de fout.
    /// </summary>
    private async Task<(string Gelezen, string? Fout)> LeesMetHerhalingAsync(byte[] bytes, string naam, CancellationToken stop)
    {
        var wachten = new[] { 5, 30, 120 };
        string? fout = null;
        for (var poging = 0; ; poging++)
        {
            try
            {
                var (uit, f) = await _lezer.LeesAsync(bytes, naam, null, stop);
                if (uit is not null) return (InboxGelezen.NaarJson(uit), null);
                fout = f;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { fout = ex.Message; }
            if (poging >= wachten.Length || !Tijdelijk(fout)) return ("", fout);
            _log.LogInformation("Factuurinbox: {Naam} uitlezen tijdelijk mislukt ({Fout}); over {S} s opnieuw.", naam, fout, wachten[poging]);
            await Task.Delay(TimeSpan.FromSeconds(wachten[poging]), stop);
        }
    }

    /// <summary>Kort overzicht van de onderdelen van een mail, voor een melding als er niets bruikbaars in zit.</summary>
    private static string Onderdelen(MailBericht bericht)
    {
        var alle = bericht.Bijlagen.Concat(bericht.Ingebed).ToList();
        if (alle.Count == 0) return "De mail heeft geen bijlagen.";
        return "Onderdelen: " + string.Join(", ", alle.Take(6).Select(b =>
            $"{(b.Naam.Length > 0 ? b.Naam : "(zonder naam)")} ({(b.Soort.Length > 0 ? b.Soort : "?")}{(b.Inline ? ", in de tekst" : "")}, {Math.Max(1, b.Grootte / 1024)} kB)"))
            + (alle.Count > 6 ? $" en nog {alle.Count - 6}" : "") + ".";
    }

    /// <summary>
    /// Eén bericht: per PDF of foto een regel; anders één regel "Geen factuur gevonden". Geeft het aantal regels.
    /// Foto's en PDF's in de tekst zelf tellen ook mee (geplakt op een telefoon, Apple Mail); kleine plaatjes (logo's,
    /// banners in een handtekening) niet. Met <paramref name="geenFactuurRegel"/> false komt er bij niets bruikbaars geen
    /// regel bij (opnieuw verwerken); <paramref name="geenReden"/> zegt dan wat er in de mail zat.
    /// </summary>
    private async Task<(int Aantal, string GeenReden)> VerwerkAsync(MailService mail, MailBericht kop, CancellationToken stop,
                                                                    bool geenFactuurRegel = true)
    {
        var inhoud = await mail.InhoudAsync(Bus, kop.Id);
        if (inhoud is null && !geenFactuurRegel) return (0, "De mail kon niet worden opgehaald (verwijderd uit de postbus?).");
        var bericht = inhoud?.Bericht ?? kop;
        var tekst = inhoud?.Tekst ?? kop.Samenvatting;

        var leesbaar = new List<(string Naam, byte[] Inhoud)>();
        var overig = new List<(string Naam, byte[] Inhoud)>();
        var meldingen = new List<string>();

        var kandidaten = bericht.Bijlagen
            .Concat(bericht.Ingebed.Where(i => Soort(i) is var so && (so == "pdf" || (so is "jpg" or "png" or "webp" or "heic" && i.Grootte >= MinIngebedFoto))))
            .ToList();
        foreach (var bijlage in kandidaten)
        {
            var soort = Soort(bijlage);
            if (bijlage.Grootte > MaxBestand)
            {
                meldingen.Add($"{bijlage.Naam} is te groot ({bijlage.Grootte / 1_000_000} MB) en niet meegenomen.");
                continue;
            }
            if (soort is "jpg" or "png" or "webp" or "gif" or "heic" && bijlage.Grootte < MinAfbeelding) continue;

            byte[]? bytes;
            try { bytes = await mail.BijlageAsync(Bus, bericht.Id, bijlage.Id); }
            catch (Exception ex)
            {
                meldingen.Add($"{bijlage.Naam} kon niet worden opgehaald: {ex.Message}");
                continue;
            }
            if (bytes is null) continue;
            if (soort == "heic")
                meldingen.Add($"{(bijlage.Naam.Length > 0 ? bijlage.Naam : "De foto")} is een iPhone-foto (HEIC) en kan niet worden gelezen; " +
                              "stuur hem als JPG (iPhone: Instellingen ▸ Camera ▸ Formaten ▸ Meest compatibel).");
            var naam = IsLeesbaar(soort) ? NaamMetExtensie(bijlage, soort) : (bijlage.Naam.Length > 0 ? bijlage.Naam : "bijlage");
            // Twee bijlagen met dezelfde naam zouden op de unieke sleutel (MailId, BijlageNaam) stuklopen.
            var basisNaam = naam;
            for (var n = 2; leesbaar.Concat(overig).Any(x => x.Naam.Equals(naam, StringComparison.OrdinalIgnoreCase)); n++)
                naam = $"{Path.GetFileNameWithoutExtension(basisNaam)} ({n}){Path.GetExtension(basisNaam)}";
            (IsLeesbaar(soort) ? leesbaar : overig).Add((naam, bytes));
        }

        var basis = new InboxStuk
        {
            Bron = "mail", Ontvangen = bericht.Datum ?? DateTime.Now, MailId = bericht.Id,
            MailVan = bericht.Van, MailOnderwerp = bericht.Onderwerp,
            MailTekst = tekst.Length > 4000 ? tekst[..4000] : tekst,
        };

        if (leesbaar.Count == 0)
        {
            var reden = Onderdelen(bericht);
            if (!geenFactuurRegel) return (0, reden + (meldingen.Count > 0 ? " " + string.Join(" ", meldingen) : ""));
            meldingen.Insert(0, "Geen factuur gevonden (geen PDF of foto als bijlage).");
            var stuk = Kopie(basis, "", Kort500(string.Join(" ", meldingen)), "");
            await _repo.VoegToeAsync(stuk, overig);
            _log.LogInformation("Factuurinbox: mail {Id} van {Van} zonder factuur. {Reden}", bericht.Id, bericht.Van, reden);
            return (1, reden);
        }

        var aantal = 0;
        foreach (var (naam, bytes) in leesbaar)
        {
            stop.ThrowIfCancellationRequested();
            var melding = new List<string>(meldingen);
            var (gelezen, fout) = await LeesMetHerhalingAsync(bytes, naam, stop);
            if (fout is not null) melding.Add("Uitlezen mislukt: " + fout);

            // De overige bijlagen (bv. een UBL-xml of de voorwaarden) gaan mee met de eerste factuur uit deze mail.
            var bestanden = new List<(string, byte[])> { (naam, bytes) };
            if (aantal == 0) bestanden.AddRange(overig);
            await _repo.VoegToeAsync(Kopie(basis, naam, Kort500(string.Join(" ", melding)), gelezen), bestanden);
            aantal++;
        }
        _log.LogInformation("Factuurinbox: mail {Id} van {Van}: {Aantal} factuur/facturen.", bericht.Id, bericht.Van, aantal);
        return (aantal, "");
    }

    private static string Kort500(string s) => s.Length > 500 ? s[..497] + "…" : s;

    /// <summary>
    /// Een regel waarvan het uitlezen mislukte opnieuw laten lezen (knop in Te beoordelen), met de bewaarde bijlage.
    /// Geeft null als het lukte, anders de fout.
    /// </summary>
    public async Task<string?> OpnieuwUitlezenAsync(InboxStuk stuk, CancellationToken stop = default)
    {
        var bestanden = await _repo.BestandenAsync(stuk.Id);
        var (naam, bytes) = bestanden.FirstOrDefault(b => b.Naam.Equals(stuk.BijlageNaam, StringComparison.OrdinalIgnoreCase));
        if (bytes is null) (naam, bytes) = bestanden.FirstOrDefault();
        if (bytes is null) return "De bijlage van deze regel is niet bewaard.";
        var (gelezen, fout) = await LeesMetHerhalingAsync(bytes, naam, stop);
        // de oude melding zonder "Uitlezen mislukt", en de nieuwe fout als het weer niet lukte
        var rest = System.Text.RegularExpressions.Regex.Replace(stuk.Melding, @"Uitlezen mislukt:.*$", "").Trim();
        var melding = fout is null ? rest : (rest + " Uitlezen mislukt: " + fout).Trim();
        await _repo.BijwerkenGelezenAsync(stuk.Id, gelezen, Kort500(melding));
        return fout;
    }

    /// <summary>
    /// Een mail met "Geen factuur gevonden" opnieuw verwerken met de huidige regels (foto's in de tekst, op soort). Komen er
    /// nu facturen uit, dan worden dat nieuwe regels en gaat de oude regel uit de lijst. Geeft een tekst voor het scherm.
    /// </summary>
    public async Task<(bool Gevonden, string Tekst)> OpnieuwVerwerkenAsync(InboxStuk stuk, int? door, CancellationToken stop = default)
    {
        if (stuk.MailId.Length == 0) return (false, "Deze regel komt niet uit de mail.");
        using var scope = _scopes.CreateScope();
        var mail = scope.ServiceProvider.GetRequiredService<MailService>();
        if (!mail.Beschikbaar(Bus)) return (false, $"Kan {Postbus} niet openen: {mail.Beschrijf(Bus)}");
        var kop = new MailBericht(stuk.MailId, "", stuk.MailVan, "", stuk.MailOnderwerp, stuk.Ontvangen, "", false, new());
        var (aantal, reden) = await VerwerkAsync(mail, kop, stop, geenFactuurRegel: false);
        if (aantal == 0) return (false, "Nog steeds geen factuur. " + reden);
        await _repo.AfhandelenAsync(stuk.Id, 2, null, door);
        Bijgewerkt?.Invoke();
        return (true, aantal == 1 ? "1 factuur gevonden." : $"{aantal} facturen gevonden.");
    }

    private static InboxStuk Kopie(InboxStuk b, string bijlage, string melding, string gelezen) => new()
    {
        Bron = b.Bron, Ontvangen = b.Ontvangen, MailId = b.MailId, MailVan = b.MailVan, MailOnderwerp = b.MailOnderwerp,
        MailTekst = b.MailTekst, BijlageNaam = bijlage, Melding = melding, Gelezen = gelezen,
    };
}
