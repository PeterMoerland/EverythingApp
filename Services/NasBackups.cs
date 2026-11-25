using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een backuptaak van Hyper Backup die we in de gaten houden.</summary>
public class NasBackupTaak
{
    public int Id { get; set; }
    /// <summary>De naam zoals hij in Hyper Backup staat; daaraan herkennen we de berichten van de NAS.</summary>
    public string Naam { get; set; } = "";
    /// <summary>Na zoveel uur zonder geslaagde backup komt er een melding.</summary>
    public int MaxUren { get; set; } = NasBackups.StandaardUren;
    public bool Actief { get; set; } = true;
    public DateTime Aangemaakt { get; set; } = DateTime.Now;
    public DateTime? LaatstGelukt { get; set; }
    public DateTime? LaatsteBericht { get; set; }
    public string LaatsteUitkomst { get; set; } = "";
    public string LaatsteTekst { get; set; } = "";
    /// <summary>Wanneer we voor het laatst gemeld hebben dat de backup uitblijft (één keer per dag zolang het duurt).</summary>
    public DateTime? UitblijvenGemeld { get; set; }

    /// <summary>Het moment vanaf waar we tellen: de laatste geslaagde backup, of anders sinds we de taak kennen.</summary>
    public DateTime Sinds => LaatstGelukt ?? Aangemaakt;
    public bool TeLaat(DateTime nu) => Actief && nu - Sinds > TimeSpan.FromHours(MaxUren);
}

/// <summary>Een bericht dat de NAS naar de app stuurde.</summary>
public record NasBackupBericht(int Id, DateTime Op, int? TaakId, string Uitkomst, string Tekst);

/// <summary>Wat een bericht van de NAS betekent.</summary>
public static class NasUitkomst
{
    public const string Gelukt = "gelukt", Mislukt = "mislukt", Deels = "deels", Onderbroken = "onderbroken",
                        Gestart = "gestart", Controle = "controle", Onbekend = "onbekend", Overig = "overig";

    /// <summary>Is dit een uitkomst waar de Directie een melding van krijgt?</summary>
    public static bool IsFout(string u) => u is Mislukt or Deels or Onderbroken;

    public static string Naam(string u) => u switch
    {
        Gelukt => "Gelukt", Mislukt => "Mislukt", Deels => "Gedeeltelijk gelukt", Onderbroken => "Onderbroken",
        Gestart => "Gestart", Controle => "Controle", Overig => "Ander bericht", _ => "Onbekend",
    };

    public static string Kleur(string u) => u switch
    {
        Gelukt => "s-groen", Mislukt or Deels or Onderbroken => "s-rood", Gestart => "s-oranje", _ => "s-grijs",
    };

    private static bool Heeft(string t, params string[] woorden) => woorden.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// De uitkomst uit de tekst van DSM halen (Nederlands of Engels). De volgorde telt: "niet gelukt" en "gedeeltelijk
    /// voltooid" bevatten ook de woorden voor gelukt. Een integriteitscontrole of het opruimen van oude versies die
    /// slaagt is geen geslaagde backup; mislukt zo'n controle, dan is dat wel een fout.
    /// </summary>
    public static string Lees(string tekst, bool hoortBijTaak)
    {
        var t = tekst ?? "";
        var overBackup = hoortBijTaak || Heeft(t, "back-up", "backup", "hyper backup");
        if (!overBackup) return Overig;
        if (Heeft(t, "mislukt", "niet gelukt", "failed", "failure", "fout opgetreden", "error", "kan niet", "unable", "afgebroken", "aborted", "beschadigd", "corrupt"))
            return Mislukt;
        if (Heeft(t, "gedeeltelijk", "partial")) return Deels;
        if (Heeft(t, "geannuleerd", "cancel", "onderbroken", "opgeschort", "suspend", "interrupted")) return Onderbroken;
        var bijzaak = Heeft(t, "integrit", "integrity", "versies", "version rotation", "rotatie", "verwijder", "delet");
        if (Heeft(t, "gestart", "started", "begonnen", "is bezig")) return bijzaak ? Controle : Gestart;
        if (Heeft(t, "voltooid", "completed", "geslaagd", "gelukt", "successful", "succesvol", "finished", "klaar"))
            return bijzaak ? Controle : Gelukt;
        return Onbekend;
    }
}

/// <summary>
/// De backups van de Synology NAS (Hyper Backup) in de gaten houden (wens Directie, 25-11-2026). De NAS meldt elke
/// uitkomst zelf aan de app (DSM ▸ Configuratiescherm ▸ Melding ▸ Push-service ▸ webhook naar <c>/api/nas-melding</c>);
/// hier wordt het bericht bewaard, bij de taak gezet en, als het mis is, in de bel van de Directie gemeld.
/// <see cref="NasBackupWaker"/> meldt daarnaast als een taak te lang geen geslaagde backup heeft: een NAS die uit staat
/// stuurt immers niets. Tabellen: NasBackups_MariaDB.sql. Instellingen: <c>NasBackup:Sleutel</c> (leeg = meldpunt uit).
/// Met <c>NasBackup:Geheugen</c> staat alles in het geheugen en gaan er geen meldingen de database in (lokaal testen).
/// </summary>
public class NasBackups
{
    public const int StandaardUren = 30;
    /// <summary>Soort van de melding in de bel (tabel Meldingen); OrderID = de taak.</summary>
    public const string MeldingSoort = "backup";
    public const string Pagina = "beheer/backups";

    private readonly string? _cs;
    private readonly bool _geheugen;
    private readonly IConfiguration _config;
    private readonly IMeldingRepository _meldingen;
    private readonly MeldingSignaal _signaal;
    private readonly ILogger<NasBackups> _log;
    private bool _aanwezig;

    private readonly object _slot = new();
    private readonly List<NasBackupTaak> _taken = new();
    private readonly List<NasBackupBericht> _berichten = new();
    private int _volgend = 1;

    /// <summary>Gaat af als er een bericht binnenkomt of een taak wijzigt; het scherm ververst zich dan.</summary>
    public event Action? Gewijzigd;

    /// <summary>In het geheugen: de meldingen die anders in de bel waren gekomen (om lokaal te zien wat er gebeurt).</summary>
    public List<string> Proefmeldingen { get; } = new();

    public NasBackups(IConfiguration config, IMeldingRepository meldingen, MeldingSignaal signaal, ILogger<NasBackups> log)
    {
        _config = config; _meldingen = meldingen; _signaal = signaal; _log = log;
        _cs = config.GetConnectionString("MDE");
        _geheugen = config.GetValue<bool>("NasBackup:Geheugen");
        if (_geheugen)
            foreach (var n in new[] { "BackupNaarLabNas", "BackupNaarRackNas", "Synology C2" })
                _taken.Add(new NasBackupTaak { Id = _volgend++, Naam = n });
    }

    private bool Live => !_geheugen && !string.IsNullOrWhiteSpace(_cs) && !_cs!.Contains("REPLACE_");
    public bool Beschikbaar => _geheugen || Live;
    private string Sleutel => (_config["NasBackup:Sleutel"] ?? "").Trim();
    /// <summary>Is het meldpunt ingesteld (NasBackup:Sleutel gevuld en geen voorbeeldwaarde)?</summary>
    public bool MeldpuntAan => Sleutel.Length >= 12 && !Sleutel.Contains("VUL-IN", StringComparison.OrdinalIgnoreCase);

    /// <summary>Klopt de sleutel die de NAS meestuurt? Vergelijken in vaste tijd.</summary>
    public bool SleutelKlopt(string? gekregen)
        => MeldpuntAan && gekregen is { Length: > 0 }
           && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(gekregen.Trim()), Encoding.UTF8.GetBytes(Sleutel));

    private static string Kort(string? s, int max) { s = (s ?? "").Trim(); return s.Length <= max ? s : s[..max]; }

    public async Task<bool> AanwezigAsync()
    {
        if (_geheugen) return true;
        if (_aanwezig || !Live) return _aanwezig;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('NasBackupTaken', 'NasBackupBerichten')", cn);
        return _aanwezig = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 2;
    }

    // ------------------------------------------------------------------ lezen

    private const string TaakKolommen =
        "`TaakID`, `Naam`, `MaxUren`, `Actief`, `Aangemaakt`, `LaatstGelukt`, `LaatsteBericht`, `LaatsteUitkomst`, `LaatsteTekst`, `UitblijvenGemeld`";

    private static NasBackupTaak LeesTaak(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0), Naam = r.GetString(1), MaxUren = r.GetInt32(2), Actief = Convert.ToInt32(r.GetValue(3)) != 0,
        Aangemaakt = r.GetDateTime(4), LaatstGelukt = r.IsDBNull(5) ? null : r.GetDateTime(5),
        LaatsteBericht = r.IsDBNull(6) ? null : r.GetDateTime(6), LaatsteUitkomst = r.IsDBNull(7) ? "" : r.GetString(7),
        LaatsteTekst = r.IsDBNull(8) ? "" : r.GetString(8), UitblijvenGemeld = r.IsDBNull(9) ? null : r.GetDateTime(9),
    };

    private static NasBackupTaak Kopie(NasBackupTaak t) => new()
    {
        Id = t.Id, Naam = t.Naam, MaxUren = t.MaxUren, Actief = t.Actief, Aangemaakt = t.Aangemaakt, LaatstGelukt = t.LaatstGelukt,
        LaatsteBericht = t.LaatsteBericht, LaatsteUitkomst = t.LaatsteUitkomst, LaatsteTekst = t.LaatsteTekst, UitblijvenGemeld = t.UitblijvenGemeld,
    };

    /// <summary>Alle taken op naam; standaard ook de taken die niet meer bewaakt worden.</summary>
    public async Task<List<NasBackupTaak>> TakenAsync(bool alleenActief = false)
    {
        if (_geheugen) lock (_slot) return _taken.Where(t => !alleenActief || t.Actief).OrderBy(t => t.Naam).Select(Kopie).ToList();
        var lijst = new List<NasBackupTaak>();
        if (!await AanwezigAsync()) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT {TaakKolommen} FROM `NasBackupTaken`" + (alleenActief ? " WHERE `Actief` <> 0" : "") + " ORDER BY `Naam`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(LeesTaak(r));
        return lijst;
    }

    /// <summary>De laatst binnengekomen berichten, nieuwste eerst.</summary>
    public async Task<List<NasBackupBericht>> BerichtenAsync(int max = 60)
    {
        if (_geheugen) lock (_slot) return _berichten.OrderByDescending(b => b.Id).Take(max).ToList();
        var lijst = new List<NasBackupBericht>();
        if (!await AanwezigAsync()) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `BerichtID`, `Op`, `TaakID`, `Uitkomst`, `Tekst` FROM `NasBackupBerichten` ORDER BY `BerichtID` DESC LIMIT @max", cn);
        cmd.Parameters.AddWithValue("@max", Math.Clamp(max, 1, 500));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new NasBackupBericht(r.GetInt32(0), r.GetDateTime(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.GetString(3), r.GetString(4)));
        return lijst;
    }

    // ------------------------------------------------------------------ een bericht van de NAS

    /// <summary>
    /// Een bericht van de NAS verwerken: bewaren, bij de taak zetten (de langste naam die in de tekst staat) en bij een
    /// fout de Directie melden. Geeft wat ervan gemaakt is.
    /// </summary>
    public async Task<NasBackupBericht> OntvangAsync(string tekst)
    {
        tekst = Kort(tekst, 1000);
        var nu = DateTime.Now;
        var taken = await TakenAsync();
        var taak = taken.Where(t => t.Naam.Length > 0 && tekst.Contains(t.Naam, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(t => t.Naam.Length).FirstOrDefault();
        var uitkomst = NasUitkomst.Lees(tekst, taak is not null);
        // van de bijzaken (controle, start) en van onbekende tekst wordt de stand van de taak niet anders
        var telt = taak is not null && uitkomst is not (NasUitkomst.Controle or NasUitkomst.Overig);

        int id;
        if (_geheugen)
            lock (_slot)
            {
                id = _volgend++;
                _berichten.Add(new NasBackupBericht(id, nu, taak?.Id, uitkomst, tekst));
                if (telt && _taken.FirstOrDefault(x => x.Id == taak!.Id) is { } t)
                {
                    t.LaatsteBericht = nu; t.LaatsteUitkomst = uitkomst; t.LaatsteTekst = tekst;
                    if (uitkomst == NasUitkomst.Gelukt) { t.LaatstGelukt = nu; t.UitblijvenGemeld = null; }
                }
            }
        else
        {
            if (!await AanwezigAsync()) throw new InvalidOperationException("NasBackups_MariaDB.sql is nog niet uitgevoerd.");
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using (var cmd = new MySqlCommand(
                "INSERT INTO `NasBackupBerichten` (`Op`, `TaakID`, `Uitkomst`, `Tekst`) VALUES (@op, @t, @u, @x); SELECT LAST_INSERT_ID();", cn))
            {
                cmd.Parameters.AddWithValue("@op", nu);
                cmd.Parameters.AddWithValue("@t", (object?)taak?.Id ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@u", uitkomst);
                cmd.Parameters.AddWithValue("@x", tekst);
                id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
            if (telt)
            {
                await using var cmd = new MySqlCommand(
                    "UPDATE `NasBackupTaken` SET `LaatsteBericht` = @op, `LaatsteUitkomst` = @u, `LaatsteTekst` = @x" +
                    (uitkomst == NasUitkomst.Gelukt ? ", `LaatstGelukt` = @op, `UitblijvenGemeld` = NULL" : "") + " WHERE `TaakID` = @id", cn);
                cmd.Parameters.AddWithValue("@op", nu);
                cmd.Parameters.AddWithValue("@u", uitkomst);
                cmd.Parameters.AddWithValue("@x", tekst);
                cmd.Parameters.AddWithValue("@id", taak!.Id);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        if (NasUitkomst.IsFout(uitkomst) && (taak is null || taak.Actief))
            await MeldDirectieAsync(taak?.Id,
                $"Backup {(taak is null ? "op de NAS" : taak.Naam)}: {NasUitkomst.Naam(uitkomst).ToLowerInvariant()}. De NAS meldt: {Kort(tekst, 300)}");
        Gewijzigd?.Invoke();
        return new NasBackupBericht(id, nu, taak?.Id, uitkomst, tekst);
    }

    /// <summary>Een melding in de bel van iedereen met het recht Directie. Geeft het aantal ontvangers.</summary>
    public async Task<int> MeldDirectieAsync(int? taakId, string tekst)
    {
        if (_geheugen) { lock (_slot) Proefmeldingen.Insert(0, $"{DateTime.Now:HH:mm:ss} {tekst}"); return 0; }
        if (!_meldingen.Beschikbaar || !await _meldingen.TabelAanwezigAsync()) return 0;
        var wie = await _meldingen.WerknemersMetRechtAsync("Directie");
        foreach (var w in wie)
            try { await _meldingen.MaakAsync(w, MeldingSoort, taakId, tekst); }
            catch (Exception ex) { _log.LogWarning(ex, "NAS-backup: melding voor werknemer {Id} mislukte.", w); }
        _signaal.Geef();
        _log.LogWarning("NAS-backup gemeld aan {Aantal} werknemer(s): {Tekst}", wie.Count, tekst);
        return wie.Count;
    }

    // ------------------------------------------------------------------ uitblijven (NasBackupWaker)

    /// <summary>
    /// Taken die te lang geen geslaagde backup hebben: één melding per dag zolang het duurt. Geeft het aantal meldingen.
    /// </summary>
    public async Task<int> KijkUitblijvenAsync()
    {
        if (!await AanwezigAsync()) return 0;
        var nu = DateTime.Now;
        var gemeld = 0;
        foreach (var t in await TakenAsync(alleenActief: true))
        {
            if (!t.TeLaat(nu)) continue;
            if (t.UitblijvenGemeld is DateTime g && nu - g < TimeSpan.FromHours(24)) continue;
            var uren = (int)(nu - t.Sinds).TotalHours;
            var tekst = t.LaatstGelukt is DateTime l
                ? $"Backup {t.Naam}: al {uren} uur geen geslaagde backup (de laatste was {l:d-M HH:mm})."
                : $"Backup {t.Naam}: nog nooit een bericht \"gelukt\" van de NAS gekregen ({uren} uur sinds de taak in de app staat). Staat de webhook op de NAS goed?";
            await ZetGemeldAsync(t.Id, nu);
            await MeldDirectieAsync(t.Id, tekst);
            gemeld++;
        }
        if (gemeld > 0) Gewijzigd?.Invoke();
        return gemeld;
    }

    private async Task ZetGemeldAsync(int taakId, DateTime nu)
    {
        if (_geheugen) { lock (_slot) if (_taken.FirstOrDefault(x => x.Id == taakId) is { } t) t.UitblijvenGemeld = nu; return; }
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `NasBackupTaken` SET `UitblijvenGemeld` = @nu WHERE `TaakID` = @id", cn);
        cmd.Parameters.AddWithValue("@nu", nu);
        cmd.Parameters.AddWithValue("@id", taakId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ------------------------------------------------------------------ taken beheren

    /// <summary>Een taak toevoegen (Id 0) of naam, uren en actief bijwerken. Taken worden niet verwijderd, alleen uitgezet.</summary>
    public async Task BewaarTaakAsync(int id, string naam, int maxUren, bool actief)
    {
        naam = Kort(naam, 100);
        if (naam.Length == 0) throw new InvalidOperationException("Vul de naam in zoals hij in Hyper Backup staat.");
        maxUren = Math.Clamp(maxUren, 1, 24 * 60);
        if (_geheugen)
        {
            lock (_slot)
            {
                if (_taken.Any(x => x.Id != id && string.Equals(x.Naam, naam, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Er is al een taak met deze naam.");
                var t = _taken.FirstOrDefault(x => x.Id == id);
                if (t is null) _taken.Add(t = new NasBackupTaak { Id = _volgend++ });
                t.Naam = naam; t.MaxUren = maxUren; t.Actief = actief;
            }
            Gewijzigd?.Invoke();
            return;
        }
        if (!await AanwezigAsync()) throw new InvalidOperationException("NasBackups_MariaDB.sql is nog niet uitgevoerd.");
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        try
        {
            await using var cmd = new MySqlCommand(id == 0
                ? "INSERT INTO `NasBackupTaken` (`Naam`, `MaxUren`, `Actief`, `Aangemaakt`) VALUES (@n, @u, @a, NOW())"
                : "UPDATE `NasBackupTaken` SET `Naam` = @n, `MaxUren` = @u, `Actief` = @a WHERE `TaakID` = @id", cn);
            cmd.Parameters.AddWithValue("@n", naam);
            cmd.Parameters.AddWithValue("@u", maxUren);
            cmd.Parameters.AddWithValue("@a", actief ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new InvalidOperationException("Er is al een taak met deze naam.");
        }
        Gewijzigd?.Invoke();
    }

    // ------------------------------------------------------------------ de tekst uit het verzoek van de NAS

    /// <summary>Alle tekst uit een JSON-waarde achter elkaar (DSM laat zelf kiezen hoe het veld heet).</summary>
    private static void Verzamel(JsonElement e, List<string> uit)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: if (e.GetString() is { Length: > 0 } s) uit.Add(s); break;
            case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) Verzamel(p.Value, uit); break;
            case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) Verzamel(x, uit); break;
        }
    }

    /// <summary>
    /// De tekst van de melding uit het verzoek: uit de vraagtekens-parameters (alles behalve de sleutel), uit een
    /// formulier, uit JSON (elk tekstveld) of anders de kale inhoud. Zo maakt het niet uit hoe de webhook in DSM is ingevuld.
    /// </summary>
    public static async Task<string> TekstUitAsync(HttpRequest req)
    {
        var delen = new List<string>();
        foreach (var q in req.Query)
            if (!q.Key.Equals("sleutel", StringComparison.OrdinalIgnoreCase)) delen.AddRange(q.Value.Where(v => !string.IsNullOrWhiteSpace(v))!);
        if (req.ContentLength is > 0 or null && !HttpMethods.IsGet(req.Method))
        {
            if (req.HasFormContentType)
            {
                var form = await req.ReadFormAsync();
                foreach (var f in form)
                    if (!f.Key.Equals("sleutel", StringComparison.OrdinalIgnoreCase)) delen.AddRange(f.Value.Where(v => !string.IsNullOrWhiteSpace(v))!);
            }
            else
            {
                var buffer = new char[16 * 1024];      // een melding is een paar regels; meer lezen we niet
                using var lezer = new StreamReader(req.Body, Encoding.UTF8);
                var n = await lezer.ReadBlockAsync(buffer, 0, buffer.Length);
                var inhoud = new string(buffer, 0, n).Trim();
                if (inhoud.StartsWith('{') || inhoud.StartsWith('['))
                {
                    try { using var doc = JsonDocument.Parse(inhoud); Verzamel(doc.RootElement, delen); }
                    catch (JsonException) { delen.Add(inhoud); }
                }
                else if (inhoud.Length > 0) delen.Add(inhoud);
            }
        }
        return string.Join(" ", delen.Select(d => d.Trim()).Where(d => d.Length > 0).Distinct());
    }
}

/// <summary>
/// Kijkt elk kwartier of een backuptaak te lang geen "gelukt" heeft gemeld (de NAS staat uit, de taak hangt, of de
/// webhook werkt niet) en zet dan een melding in de bel van de Directie; zie <see cref="NasBackups.KijkUitblijvenAsync"/>.
/// Uit te zetten met <c>NasBackup:Aan</c> = false; bij lokaal ontwikkelen staat hij standaard uit, behalve in het geheugen.
/// </summary>
public sealed class NasBackupWaker : BackgroundService
{
    private readonly NasBackups _backups;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<NasBackupWaker> _log;

    public NasBackupWaker(NasBackups backups, IConfiguration config, IHostEnvironment env, ILogger<NasBackupWaker> log)
    {
        _backups = backups; _config = config; _env = env; _log = log;
    }

    private bool Aan => _config.GetValue("NasBackup:Aan", !_env.IsDevelopment() || _config.GetValue<bool>("NasBackup:Geheugen"));

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stop); } catch { return; }
        while (!stop.IsCancellationRequested)
        {
            try { if (Aan && _backups.Beschikbaar) await _backups.KijkUitblijvenAsync(); }
            catch (Exception ex) { _log.LogWarning(ex, "NAS-backups nakijken mislukte."); }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stop); } catch { return; }
        }
    }
}

public static class NasBackupsApi
{
    /// <summary>
    /// Het meldpunt voor de webhook van de NAS: GET of POST op <c>/api/nas-melding</c>. Geen login (de NAS heeft geen
    /// browser) maar een gedeelde sleutel: in de kop <c>X-Sleutel</c> of als <c>?sleutel=</c>. Sleutel leeg = meldpunt uit.
    /// </summary>
    public static void MapNasBackups(this WebApplication app)
    {
        async Task<IResult> Ontvang(HttpContext ctx, NasBackups backups, ILogger<NasBackups> log)
        {
            if (!backups.MeldpuntAan || !backups.Beschikbaar) return Results.NotFound();
            var sleutel = ctx.Request.Headers["X-Sleutel"].FirstOrDefault() ?? ctx.Request.Query["sleutel"].FirstOrDefault();
            if (!backups.SleutelKlopt(sleutel)) return Results.Unauthorized();
            string tekst;
            try { tekst = await NasBackups.TekstUitAsync(ctx.Request); }
            catch { return Results.BadRequest("Het bericht is niet te lezen."); }
            if (tekst.Length == 0) return Results.BadRequest("Geen tekst ontvangen.");
            try
            {
                var b = await backups.OntvangAsync(tekst);
                return Results.Json(new { ontvangen = true, uitkomst = b.Uitkomst, taak = b.TaakId });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Bericht van de NAS verwerken mislukte.");
                return Results.Problem("Het bericht is niet verwerkt.");
            }
        }
        app.MapPost("/api/nas-melding", Ontvang).DisableAntiforgery();
        app.MapGet("/api/nas-melding", Ontvang);
    }
}
