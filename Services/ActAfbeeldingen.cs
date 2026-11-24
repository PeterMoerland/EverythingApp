using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een afbeelding bij een tekstveld van een activiteit op het Activity board.</summary>
public record ActAfbeelding(int Id, int ActiviteitId, string Veld, int FileStoreId, string Titel, string Commentaar, int? Door, DateTime Op);

/// <summary>
/// Afbeeldingen bij de tekstvelden van een activiteit (punt van Arnold, 24-11-2026). Het bestand staat in de filestore
/// (soort 344, MasterID = de activiteit); de tabel ActivityBoardAfbeeldingen (ActivityBoard_Afbeeldingen_MariaDB.sql)
/// zegt bij welk veld het hoort en bewaart titel en commentaar. Met ActivityBoard:Geheugen staat alles in het geheugen
/// (lokaal testen zonder database en zonder de filestore).
/// </summary>
public class ActAfbeeldingen
{
    public const int StoreType = 344;
    public const long MaxGrootte = 20_000_000;
    private static readonly string[] Soorten = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    public static bool IsAfbeelding(string naam) => Soorten.Contains(Path.GetExtension(naam ?? "").ToLowerInvariant());

    private readonly string? _cs;
    private readonly bool _geheugen;
    private readonly FilestoreService _store;
    private bool _aanwezig;

    private readonly object _slot = new();
    private readonly List<ActAfbeelding> _lijst = new();
    private readonly Dictionary<int, (byte[] Inhoud, string Naam)> _inhoud = new();
    private int _volgend = 1;

    public ActAfbeeldingen(IConfiguration config, FilestoreService store)
    {
        _cs = config.GetConnectionString("MDE");
        _geheugen = config.GetValue<bool>("ActivityBoard:Geheugen");
        _store = store;
    }

    private bool Live => !_geheugen && !string.IsNullOrWhiteSpace(_cs) && !_cs!.Contains("REPLACE_");

    /// <summary>Is het script uitgevoerd? Eenmaal ja blijft ja; nee wordt elke keer opnieuw bekeken.</summary>
    public async Task<bool> AanwezigAsync()
    {
        if (_geheugen) return true;
        if (_aanwezig || !Live) return _aanwezig;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ActivityBoardAfbeeldingen'", cn);
        return _aanwezig = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static ActAfbeelding Lees(MySqlDataReader r) => new(
        r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetInt32(3), r.GetString(4), r.IsDBNull(5) ? "" : r.GetString(5),
        r.IsDBNull(6) ? null : r.GetInt32(6), r.GetDateTime(7));

    private const string Kolommen = "`AfbeeldingID`, `ActiviteitID`, `Veld`, `FileStoreID`, `Titel`, `Commentaar`, `GemaaktDoor`, `GemaaktOp`";

    /// <summary>Alle afbeeldingen van deze activiteiten, op volgorde van toevoegen.</summary>
    public async Task<List<ActAfbeelding>> VanActiviteitenAsync(IReadOnlyCollection<int> activiteitIds)
    {
        var ids = activiteitIds.Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0) return new();
        if (_geheugen) lock (_slot) return _lijst.Where(x => ids.Contains(x.ActiviteitId)).OrderBy(x => x.Id).ToList();
        if (!await AanwezigAsync()) return new();

        var lijst = new List<ActAfbeelding>();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        // getallen uit ons eigen board, dus veilig om zo in de query te zetten
        await using var cmd = new MySqlCommand(
            $"SELECT {Kolommen} FROM `ActivityBoardAfbeeldingen` WHERE `ActiviteitID` IN ({string.Join(",", ids)}) ORDER BY `AfbeeldingID`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(Lees(r));
        return lijst;
    }

    public async Task<ActAfbeelding?> GetAsync(int id)
    {
        if (_geheugen) lock (_slot) return _lijst.FirstOrDefault(x => x.Id == id);
        if (!await AanwezigAsync()) return null;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand($"SELECT {Kolommen} FROM `ActivityBoardAfbeeldingen` WHERE `AfbeeldingID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Lees(r) : null;
    }

    /// <summary>Een afbeelding bij een veld zetten; de titel is de bestandsnaam zonder extensie. Geeft het nummer.</summary>
    public async Task<int> ToevoegenAsync(int activiteitId, string veld, string naam, byte[] inhoud, int? door)
    {
        var bestand = Path.GetFileName(naam ?? "");
        var titel = Path.GetFileNameWithoutExtension(bestand);
        if (titel.Length > 200) titel = titel[..200];

        if (_geheugen)
            lock (_slot)
            {
                var id = _volgend++;
                _lijst.Add(new ActAfbeelding(id, activiteitId, veld, 0, titel, "", door, DateTime.Now));
                _inhoud[id] = (inhoud, bestand);
                return id;
            }

        if (!await AanwezigAsync()) throw new InvalidOperationException("ActivityBoard_Afbeeldingen_MariaDB.sql is nog niet uitgevoerd.");
        int fileStoreId;
        using (var m = new MemoryStream(inhoud))
            fileStoreId = await _store.ToevoegenAsync(m, bestand, StoreType, activiteitId, door ?? 0);

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ActivityBoardAfbeeldingen` (`ActiviteitID`, `Veld`, `FileStoreID`, `Titel`, `GemaaktDoor`) VALUES (@a, @v, @f, @t, @d); SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@a", activiteitId);
        cmd.Parameters.AddWithValue("@v", veld);
        cmd.Parameters.AddWithValue("@f", fileStoreId);
        cmd.Parameters.AddWithValue("@t", titel);
        cmd.Parameters.AddWithValue("@d", (object?)door ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task ZetAsync(int id, string titel, string commentaar)
    {
        titel = (titel ?? "").Trim();
        if (titel.Length > 200) titel = titel[..200];
        commentaar = (commentaar ?? "").Trim();
        if (_geheugen)
        {
            lock (_slot)
            {
                var i = _lijst.FindIndex(x => x.Id == id);
                if (i >= 0) _lijst[i] = _lijst[i] with { Titel = titel, Commentaar = commentaar };
            }
            return;
        }
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `ActivityBoardAfbeeldingen` SET `Titel` = @t, `Commentaar` = @c WHERE `AfbeeldingID` = @id", cn);
        cmd.Parameters.AddWithValue("@t", titel);
        cmd.Parameters.AddWithValue("@c", commentaar.Length == 0 ? DBNull.Value : commentaar);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>De afbeelding weghalen: de regel en het bestand in de filestore.</summary>
    public async Task VerwijderAsync(int id)
    {
        if (_geheugen) { lock (_slot) { _lijst.RemoveAll(x => x.Id == id); _inhoud.Remove(id); } return; }
        var a = await GetAsync(id);
        if (a is null) return;
        await using (var cn = new MySqlConnection(_cs))
        {
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand("DELETE FROM `ActivityBoardAfbeeldingen` WHERE `AfbeeldingID` = @id", cn);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        try { await _store.VerwijderAsync(a.FileStoreId); } catch { /* de regel is weg; het bestand blijft dan in de filestore staan */ }
    }

    /// <summary>De afbeelding zelf: uit het geheugen (Inhoud) of het pad in de filestore.</summary>
    public async Task<(byte[]? Inhoud, string? Pad, string Naam)?> InhoudAsync(int id)
    {
        if (_geheugen)
            lock (_slot) return _inhoud.TryGetValue(id, out var x) ? (x.Inhoud, null, x.Naam) : null;
        var a = await GetAsync(id);
        if (a is null) return null;
        var f = await _store.ZoekAsync(a.FileStoreId);
        return f is { } gevonden && gevonden.StoreType == StoreType ? (null, gevonden.Pad, gevonden.Naam) : null;
    }
}

public static class ActAfbeeldingenApi
{
    /// <summary>Uploaden (slepen in het board, wwwroot/activityboard.js) en tonen van de afbeeldingen.</summary>
    public static RouteGroupBuilder MapActAfbeeldingen(this WebApplication app)
    {
        var api = app.MapGroup("/activityboard/afbeelding");

        api.MapPost("", async (int activiteit, string veld, HttpRequest req, ActAfbeeldingen afb, IActivityBoardRepository repo, HuidigeGebruiker ik) =>
        {
            // Buiten een Blazor-circuit is de gebruiker nog niet geladen; zonder dit bleef WerknemerId leeg en
            // weigerde de filestore (FK ToegevoegdDoor -> Werknemers). Melding van Arnold 24-11-2026.
            await ik.ZorgGeladenAsync();
            if (ik.WerknemerId is null) return Results.BadRequest("Niet bekend wie je bent; meld je (opnieuw) aan en probeer het nog eens.");
            if (activiteit <= 0 || !ActTekst.IsKolom(veld)) return Results.BadRequest("Onbekende activiteit of onbekend veld.");
            if (await repo.BoardVanActiviteitAsync(activiteit) is null) return Results.NotFound("Deze activiteit bestaat niet (meer).");
            if (!req.HasFormContentType) return Results.BadRequest("Geen bestanden ontvangen.");
            var form = await req.ReadFormAsync();
            var toegevoegd = 0;
            var geweigerd = new List<string>();
            foreach (var f in form.Files)
            {
                if (!ActAfbeeldingen.IsAfbeelding(f.FileName)) { geweigerd.Add($"{f.FileName}: geen afbeelding (png, jpg, gif, webp of bmp)"); continue; }
                if (f.Length == 0 || f.Length > ActAfbeeldingen.MaxGrootte) { geweigerd.Add($"{f.FileName}: leeg of groter dan 20 MB"); continue; }
                try
                {
                    using var m = new MemoryStream();
                    await f.CopyToAsync(m);
                    await afb.ToevoegenAsync(activiteit, veld, f.FileName, m.ToArray(), ik.WerknemerId);
                    toegevoegd++;
                }
                catch (Exception ex) { geweigerd.Add($"{f.FileName}: {ex.GetBaseException().Message}"); }
            }
            return Results.Json(new { toegevoegd, geweigerd });
        }).DisableAntiforgery();

        api.MapGet("/{id:int}", async (int id, ActAfbeeldingen afb, HttpContext http) =>
        {
            var x = await afb.InhoudAsync(id);
            if (x is not { } b) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=3600";
            var mime = FilestoreService.MimeVoor(b.Naam);
            return b.Inhoud is not null ? Results.File(b.Inhoud, mime) : Results.File(b.Pad!, mime);
        });

        return api;
    }
}
