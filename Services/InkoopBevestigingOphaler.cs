using System.Text.RegularExpressions;

namespace EverythingApp.Services;

/// <summary>PO-nummers herkennen in een onderwerp, een bestandsnaam of de tekst van een mail.</summary>
public static class PoHerkenning
{
    // "PO-18544", "PO 18544", "PO18544", "P.O. 18544", "PO#18544", "PO_18544": de letters PO, hooguit een paar
    // scheidingstekens en 4 tot 6 cijfers. Niet midden in een woord (EXPO…) en niet het begin van een langer getal
    // (PO 4500087158 is het nummer van de leverancier). Een los getal zonder "PO" telt bewust niet.
    private static readonly Regex Patroon = new(@"(?<![A-Za-z0-9])P\.?O\.?[\s\-_:#]{0,3}(\d{4,6})(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>De PO-nummers in een tekst, zonder dubbele, in de volgorde waarin ze staan.</summary>
    public static List<int> Zoek(string? tekst)
    {
        var uit = new List<int>();
        if (string.IsNullOrWhiteSpace(tekst)) return uit;
        try
        {
            foreach (Match m in Patroon.Matches(tekst))
                if (int.TryParse(m.Groups[1].Value, out var n) && !uit.Contains(n)) uit.Add(n);
        }
        catch (RegexMatchTimeoutException) { }
        return uit;
    }

    /// <summary>
    /// De nummers van één bericht: eerst het onderwerp, dan de namen van de bijlagen, dan de tekst. De eerste plek die
    /// iets oplevert telt; zo haalt een lange mailwisseling met oude PO-nummers in de tekst geen verkeerde bestelling erbij.
    /// </summary>
    public static List<int> VanBericht(string onderwerp, IEnumerable<string> bijlagen, string tekst)
    {
        var nummers = Zoek(onderwerp);
        if (nummers.Count > 0) return nummers;
        nummers = bijlagen.SelectMany(Zoek).Distinct().ToList();
        return nummers.Count > 0 ? nummers : Zoek(tekst);
    }
}

/// <summary>
/// Orderbevestigingen uit de inkooppostbus (wens gebruiker 12-11-2026; vervangt het VB-programmaatje van vroeger).
/// Leest op de achtergrond de postbus uit de sectie "InkoopBevestigingen" (inkoop@mde-automation.nl), zoekt in elk
/// binnengekomen bericht een PO-nummer en zet de PDF-bijlagen als document bij die bestelling (FileStore soort 4, zoals
/// Bestellingen ▸ Documenten). Alleen koppelen: er wordt niets uitgelezen en de bestelling zelf verandert niet.
///
/// Wat al gezien is: een Gmail-label (standaard "EverythingApp/Bevestiging") en het bericht-id in de tabel
/// InkoopBevestigingen. Er wordt niets uit de postbus verwijderd of verplaatst. Werkt alleen met het
/// Google-serviceaccount (Google:ServiceAccountJson) en als de postbus een echte gebruiker is, geen groep.
/// Wat niet gekoppeld kon worden staat in Bestellingen ▸ Bevestigingen (Components/InkoopBevestigingenPaneel.razor).
/// </summary>
public class InkoopBevestigingOphaler : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopes;   // MailService is scoped; per ronde een eigen scope
    private readonly IInkoopBevestigingRepository _repo;
    private readonly IWerknemerRepository _bestanden;
    private readonly FilestoreService _store;
    private readonly ILogger<InkoopBevestigingOphaler> _log;
    private readonly SemaphoreSlim _nu = new(0);
    private readonly SemaphoreSlim _bezig = new(1, 1);
    private readonly HashSet<string> _mislukt = new();

    public InkoopBevestigingOphaler(IConfiguration config, IServiceScopeFactory scopes, IInkoopBevestigingRepository repo,
                                    IWerknemerRepository bestanden, FilestoreService store, ILogger<InkoopBevestigingOphaler> log)
    {
        _config = config;
        _scopes = scopes;
        _repo = repo;
        _bestanden = bestanden;
        _store = store;
        _log = log;
    }

    private const long MaxBestand = 25_000_000;
    /// <summary>Hoe ver een bestelling vóór de mail mag liggen: een ouder PO-nummer in een mail is bijna zeker iets anders.</summary>
    private static readonly TimeSpan MaxOud = TimeSpan.FromDays(400);

    public string Postbus => (_config["InkoopBevestigingen:Postbus"] ?? "").Trim();
    public bool Aan => _config.GetValue("InkoopBevestigingen:Aan", false) && Postbus.Length > 0;
    private int Minuten => Math.Clamp(_config.GetValue("InkoopBevestigingen:Minuten", 15), 2, 240);
    private string Label => string.IsNullOrWhiteSpace(_config["InkoopBevestigingen:Label"]) ? "EverythingApp/Bevestiging" : _config["InkoopBevestigingen:Label"]!.Trim();
    /// <summary>Vanaf welke datum berichten meetellen: ingesteld, of anders drie maanden terug.</summary>
    private DateOnly Vanaf => DateOnly.TryParse(_config["InkoopBevestigingen:Vanaf"], System.Globalization.CultureInfo.InvariantCulture, out var d)
        ? d : DateOnly.FromDateTime(DateTime.Today.AddMonths(-3));

    /// <summary>Voor het scherm: wanneer er voor het laatst gekeken is en wat eruit kwam, of waarom het niet lukte.</summary>
    public string Stand { get; private set; } = "";
    public bool StandFout { get; private set; }
    public DateTime? LaatsteKeer { get; private set; }

    /// <summary>Gaat af als er iets is verwerkt (het scherm ververst dan zijn lijst).</summary>
    public event Action? Bijgewerkt;

    /// <summary>Meteen kijken (knop "Nu ophalen"), in plaats van op de volgende ronde te wachten.</summary>
    public void NuOphalen() => _nu.Release();

    private MailPostbus Bus => new("Inkoop", Postbus, null, Eigen: false);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!Aan)
        {
            Stand = "Staat uit (appsettings, sectie InkoopBevestigingen: Aan en Postbus).";
            _log.LogInformation("Inkoopbevestigingen: {Stand}", Stand);
        }

        try { await Task.Delay(TimeSpan.FromSeconds(45), stop); } catch (OperationCanceledException) { return; }

        while (!stop.IsCancellationRequested)
        {
            if (Aan)
            {
                try { await RondeAsync(stop); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Inkoopbevestigingen: ophalen mislukt.");
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
                Zet("De tabel InkoopBevestigingen ontbreekt (InkoopBevestigingen_MariaDB.sql).", true);
                return;
            }
            if (!_store.Beschikbaar) { Zet("De filestore is niet ingesteld (Filestore:Pad).", true); return; }
            if (!mail.Beschikbaar(Bus))
            {
                Zet($"Kan {Postbus} niet openen: {mail.Beschrijf(Bus)}", true);
                return;
            }

            // Alles wat is binnengekomen (ook als iemand het al in een map heeft gezet), niet wat wij zelf verstuurden:
            // in onze eigen bestelmail staat het PO-nummer ook.
            var zoek = $"-label:\"{Label}\" -in:sent -in:drafts -in:spam -in:trash after:{Vanaf:yyyy'/'MM'/'dd}";

            int gekoppeld = 0, open = 0, gezien = 0, fouten = 0;
            // Per keer hoogstens 6 x 50 berichten; de rest (de eerste keer: drie maanden post) komt de volgende rondes.
            for (var ronde = 0; ronde < 6; ronde++)
            {
                var berichten = await mail.ZoekAsync(Bus, zoek, 50);
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
                        if (!bekend.Contains(b.Id))
                        {
                            var uitkomst = await VerwerkAsync(mail, b, stop);
                            gezien++;
                            if (uitkomst == BevestigingUitkomst.Gekoppeld) gekoppeld++;
                            else if (uitkomst is BevestigingUitkomst.GeenPo or BevestigingUitkomst.Onbekend) open++;
                        }
                        // Pas na het wegschrijven het label: gaat het label mis, dan houdt het bericht-id in de tabel
                        // een tweede keer tegen (voor wat is vastgelegd).
                        await mail.VoegLabelToeAsync(Bus, new[] { b.Id }, Label);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // Niet elke ronde opnieuw proberen; na een herstart wel.
                        _mislukt.Add(b.Id);
                        fouten++;
                        _log.LogWarning(ex, "Inkoopbevestigingen: mail {Id} ({Onderwerp}) overgeslagen.", b.Id, b.Onderwerp);
                    }
                }
                if (berichten.Count < 50 || !verder) break;
            }

            var tekst = gezien == 0 ? "Geen nieuwe berichten."
                : $"{gezien} bericht{(gezien == 1 ? "" : "en")} bekeken: {gekoppeld} gekoppeld" + (open > 0 ? $", {open} om zelf te bekijken" : "") + ".";
            if (fouten > 0) tekst += $" {fouten} bericht(en) overgeslagen door een fout (zie het logboek).";
            Zet(tekst, fouten > 0);
            if (gekoppeld + open > 0) Bijgewerkt?.Invoke();
        }
        finally { _bezig.Release(); }
    }

    private static bool IsPdf(MailBijlage b)
        => Path.GetExtension(b.Naam).Equals(".pdf", StringComparison.OrdinalIgnoreCase) || b.Soort.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    private static string PdfNaam(MailBijlage b)
    {
        var naam = string.IsNullOrWhiteSpace(b.Naam) ? "bijlage" : b.Naam.Trim();
        return Path.GetExtension(naam).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? naam : naam + ".pdf";
    }

    /// <summary>
    /// Eén bericht: PO-nummer zoeken, de bestelling opzoeken, de PDF's erbij zetten en vastleggen wat er gebeurd is.
    /// Een bericht zonder PO-nummer en zonder PDF (nieuwsbrief, gewone mail) wordt niet vastgelegd; het krijgt alleen het label.
    /// </summary>
    private async Task<string> VerwerkAsync(MailService mail, MailBericht kop, CancellationToken stop)
    {
        var inhoud = await mail.InhoudAsync(Bus, kop.Id);
        var bericht = inhoud?.Bericht ?? kop;
        var tekst = inhoud?.Tekst ?? kop.Samenvatting;

        // PDF's: de bijlagen, en een PDF die in de tekst zelf zit (Apple Mail)
        var pdfs = bericht.Bijlagen.Concat(bericht.Ingebed).Where(IsPdf).ToList();
        var nummers = PoHerkenning.VanBericht(bericht.Onderwerp, pdfs.Select(p => p.Naam), tekst);
        if (nummers.Count == 0 && pdfs.Count == 0) return "";

        var regel = new InkoopBevestiging
        {
            MailId = bericht.Id, Ontvangen = bericht.Datum, Van = bericht.Van, Onderwerp = bericht.Onderwerp,
            Orders = string.Join(",", nummers.Take(10)),
        };
        var namen = pdfs.Count == 0 ? "" : $"{pdfs.Count} PDF{(pdfs.Count == 1 ? "" : "'s")}: {string.Join(", ", pdfs.Take(4).Select(PdfNaam))}";

        if (nummers.Count == 0)
        {
            regel.Uitkomst = BevestigingUitkomst.GeenPo;
            regel.Melding = "Geen PO-nummer gevonden; " + namen;
            await _repo.LegVastAsync(regel);
            return regel.Uitkomst;
        }

        // De bestelling moet bestaan en bij de mail passen in de tijd (niet een oud nummer dat toevallig ook ergens staat)
        var moment = bericht.Datum ?? DateTime.Now;
        var orders = (await _repo.OrdersAsync(nummers))
            .Where(o => o.Aangemaakt is not DateTime a || (a <= moment.AddDays(2) && a >= moment - MaxOud))
            .OrderBy(o => nummers.IndexOf(o.OrderId)).ToList();
        if (orders.Count == 0)
        {
            regel.Uitkomst = BevestigingUitkomst.Onbekend;
            regel.Melding = $"PO-{string.Join(", PO-", nummers.Take(5))} bestaat niet of is van lang voor deze mail" + (namen.Length > 0 ? "; " + namen : "");
            if (pdfs.Count == 0) return "";            // zonder PDF valt er ook niets toe te wijzen
            await _repo.LegVastAsync(regel);
            return regel.Uitkomst;
        }

        regel.OrderId = orders[0].OrderId;
        if (pdfs.Count == 0)
        {
            regel.Uitkomst = BevestigingUitkomst.GeenBijlage;
            regel.Melding = "Het bericht heeft geen PDF.";
            await _repo.LegVastAsync(regel);
            return regel.Uitkomst;
        }

        var (aantal, meldingen) = await ZetBijOrdersAsync(mail, bericht.Id, pdfs, orders.Select(o => o.OrderId).ToList(), 0, stop);
        regel.Bestanden = aantal;
        regel.Uitkomst = BevestigingUitkomst.Gekoppeld;
        regel.Melding = string.Join(" ", meldingen);
        await _repo.LegVastAsync(regel);
        _log.LogInformation("Inkoopbevestigingen: {Aantal} PDF('s) uit '{Onderwerp}' bij PO-{Order}.", aantal, bericht.Onderwerp, string.Join(", PO-", orders.Select(o => o.OrderId)));
        return regel.Uitkomst;
    }

    /// <summary>
    /// De PDF's van een bericht bij een of meer bestellingen zetten. Een bestand dat er met dezelfde naam al staat (eerder
    /// met de hand toegevoegd, of dezelfde bijlage in een volgend antwoord) komt er niet nog eens bij.
    /// </summary>
    private async Task<(int Aantal, List<string> Meldingen)> ZetBijOrdersAsync(MailService mail, string berichtId, List<MailBijlage> pdfs,
                                                                             List<int> orderIds, int werknemerId, CancellationToken stop)
    {
        var aantal = 0;
        var meldingen = new List<string>();
        var inhoud = new Dictionary<string, byte[]?>();
        foreach (var orderId in orderIds)
        {
            var bestaand = (await _bestanden.GetBestandenAsync(BestelVelden.StoreTypeOrder, orderId))
                .Select(d => d.Bestandsnaam).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var pdf in pdfs)
            {
                stop.ThrowIfCancellationRequested();
                var naam = PdfNaam(pdf);
                if (bestaand.Contains(naam)) { meldingen.Add($"{naam} stond al bij PO-{orderId}."); continue; }
                if (pdf.Grootte > MaxBestand) { meldingen.Add($"{naam} is te groot ({pdf.Grootte / 1_000_000} MB)."); continue; }
                if (!inhoud.TryGetValue(pdf.Id, out var bytes))
                {
                    try { bytes = await mail.BijlageAsync(Bus, berichtId, pdf.Id); }
                    catch (Exception ex) { bytes = null; meldingen.Add($"{naam} kon niet worden opgehaald: {ex.Message}"); }
                    inhoud[pdf.Id] = bytes;
                }
                if (bytes is null || bytes.Length == 0) continue;
                using var stroom = new MemoryStream(bytes);
                await _store.ToevoegenAsync(stroom, naam, BestelVelden.StoreTypeOrder, orderId, werknemerId);
                bestaand.Add(naam);
                aantal++;
            }
        }
        return (aantal, meldingen);
    }

    /// <summary>
    /// Een bericht dat niet vanzelf gekoppeld kon worden met de hand bij een bestelling zetten (Bestellingen ▸
    /// Bevestigingen). Geeft een melding voor het scherm; leeg = gelukt.
    /// </summary>
    public async Task<string> KoppelMetDeHandAsync(int bevestigingId, int orderId, int? door, CancellationToken stop = default)
    {
        var regel = await _repo.GetAsync(bevestigingId);
        if (regel is null) return "Dit bericht staat niet meer in de lijst.";
        if ((await _repo.OrdersAsync(new[] { orderId })).Count == 0) return $"PO-{orderId} bestaat niet.";
        if (!_store.Beschikbaar) return "De filestore is niet ingesteld (Filestore:Pad).";

        using var scope = _scopes.CreateScope();
        var mail = scope.ServiceProvider.GetRequiredService<MailService>();
        if (!mail.Beschikbaar(Bus)) return $"Kan {Postbus} niet openen: {mail.Beschrijf(Bus)}";
        var inhoud = await mail.InhoudAsync(Bus, regel.MailId);
        if (inhoud is null) return "Het bericht staat niet meer in de postbus.";
        var pdfs = inhoud.Bericht.Bijlagen.Concat(inhoud.Bericht.Ingebed).Where(IsPdf).ToList();
        if (pdfs.Count == 0) return "Het bericht heeft geen PDF.";

        var (aantal, meldingen) = await ZetBijOrdersAsync(mail, regel.MailId, pdfs, new List<int> { orderId }, door ?? 0, stop);
        await _repo.WerkBijAsync(bevestigingId, BevestigingUitkomst.Gekoppeld, orderId, aantal, string.Join(" ", meldingen), door);
        Bijgewerkt?.Invoke();
        return "";
    }

    /// <summary>Een bericht wegzetten dat nergens bij hoort (een offerte, een factuur, reclame).</summary>
    public async Task NegeerAsync(int bevestigingId, int? door)
    {
        await _repo.WerkBijAsync(bevestigingId, BevestigingUitkomst.Genegeerd, null, 0, null, door);
        Bijgewerkt?.Invoke();
    }
}
