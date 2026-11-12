using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Waar een projectfoto bij hoort: een project, of een werkorder van een project.</summary>
public record FotoDoel(int ProjectId, int? WerkbonId, string Nummer, string Omschrijving, string Klant)
{
    public bool IsWerkorder => WerkbonId is not null;
}

/// <summary>
/// Een foto bij een project (tabel ProjectFotos, ProjectFotos_MariaDB.sql). De foto zelf staat in de FileStore
/// (<see cref="ProjectFotoStore.Type"/>, MasterID = Id); het kleine voorbeeld komt via /projectfoto/{id}/klein.
/// </summary>
public class ProjectFoto
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public int? WerkbonId { get; init; }
    /// <summary>Werkordernummer (projectnummer-volgnummer) als de foto bij een werkorder is gemaakt.</summary>
    public string Werkorder { get; init; } = "";
    public string Onderwerp { get; init; } = "";
    public string Notitie { get; set; } = "";
    public int? FileStoreId { get; init; }
    public DateTime GemaaktOp { get; init; }
    public int? GemaaktDoorId { get; init; }
    public string GemaaktDoor { get; init; } = "";
}

public static class ProjectFotoStore
{
    /// <summary>FileStore.StoreType voor projectfoto's (MasterID = ProjectFotoID).</summary>
    public const int Type = 370;
}

/// <summary>
/// Projectfoto's (wens gebruiker 12-11-2026): op de telefoon een werkorder of project kiezen, een onderwerp ingeven en
/// foto's maken (Components/Pages/MobielProjectFoto.razor); in de grote app staan ze bij het project (ProjectFotoLijst).
/// </summary>
public interface IProjectFotoRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();

    /// <summary>Open werkorders zoeken op nummer, omschrijving, project of klant (alle woorden).</summary>
    Task<List<FotoDoel>> ZoekWerkordersAsync(string zoek, int max);
    /// <summary>Open projecten zoeken op nummer, omschrijving of klant (alle woorden).</summary>
    Task<List<FotoDoel>> ZoekProjectenAsync(string zoek, int max);
    /// <summary>Deze werkorders als doel (voor "recent door jou gebruikt"), in de gegeven volgorde.</summary>
    Task<List<FotoDoel>> DoelenVanWerkordersAsync(IReadOnlyList<int> werkbonIds);
    /// <summary>De onderwerpen die bij dit project al gebruikt zijn, het laatst gebruikte eerst.</summary>
    Task<List<string>> OnderwerpenAsync(int projectId);

    /// <summary>Een foto vastleggen (nog zonder bestand); geeft het nieuwe id.</summary>
    Task<int> VoegToeAsync(int projectId, int? werkbonId, string onderwerp, byte[] miniatuur, int? door);
    Task ZetBestandAsync(int fotoId, int fileStoreId);
    Task ZetNotitieAsync(int fotoId, string? notitie);
    Task<ProjectFoto?> GetAsync(int fotoId);
    Task VerwijderAsync(int fotoId);

    /// <summary>De foto's van een project, nieuwste eerst.</summary>
    Task<List<ProjectFoto>> VanProjectAsync(int projectId);
    Task<int> AantalAsync(int projectId);
    Task<byte[]?> MiniatuurAsync(int fotoId);
}

public class GeenProjectFotoRepository : IProjectFotoRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<FotoDoel>> ZoekWerkordersAsync(string zoek, int max) => Task.FromResult(new List<FotoDoel>());
    public Task<List<FotoDoel>> ZoekProjectenAsync(string zoek, int max) => Task.FromResult(new List<FotoDoel>());
    public Task<List<FotoDoel>> DoelenVanWerkordersAsync(IReadOnlyList<int> werkbonIds) => Task.FromResult(new List<FotoDoel>());
    public Task<List<string>> OnderwerpenAsync(int projectId) => Task.FromResult(new List<string>());
    public Task<int> VoegToeAsync(int projectId, int? werkbonId, string onderwerp, byte[] miniatuur, int? door) => Task.FromResult(0);
    public Task ZetBestandAsync(int fotoId, int fileStoreId) => Task.CompletedTask;
    public Task ZetNotitieAsync(int fotoId, string? notitie) => Task.CompletedTask;
    public Task<ProjectFoto?> GetAsync(int fotoId) => Task.FromResult<ProjectFoto?>(null);
    public Task VerwijderAsync(int fotoId) => Task.CompletedTask;
    public Task<List<ProjectFoto>> VanProjectAsync(int projectId) => Task.FromResult(new List<ProjectFoto>());
    public Task<int> AantalAsync(int projectId) => Task.FromResult(0);
    public Task<byte[]?> MiniatuurAsync(int fotoId) => Task.FromResult<byte[]?>(null);
}

public class MySqlProjectFotoRepository : IProjectFotoRepository
{
    private readonly string _cs;
    private bool _tabel;
    public MySqlProjectFotoRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    // Alleen 'ja' onthouden: het script mag gedraaid worden terwijl de app draait.
    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ProjectFotos'", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _tabel;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";

    // Werkordernummer zoals gebruikers het kennen: projectnummer-volgnummer (2 posities)
    private const string WoNummer = "CONCAT(CAST(p.`Projectnumber` AS CHAR), '-', LPAD(CAST(COALESCE(w.`Werkbonnummer`, 0) AS CHAR), 2, '0'))";

    private const string WerkorderSelect =
        "SELECT p.`Project-Id`, w.`Werkbon-id`, " + WoNummer + ", " +
        "       CONCAT_WS(' · ', NULLIF(TRIM(COALESCE(w.`Werkbonomschrijving`, '')), ''), NULLIF(TRIM(COALESCE(p.`Projectdescription`, '')), '')), " +
        "       COALESCE(r.`name`, '') " +
        "  FROM `Workorders` w " +
        "  JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
        "  LEFT JOIN `Relations` r ON r.`id_supplier` = p.`Customer-id` ";

    private const string ProjectSelect =
        "SELECT p.`Project-Id`, NULL, CAST(p.`Projectnumber` AS CHAR), COALESCE(p.`Projectdescription`, ''), COALESCE(r.`name`, '') " +
        "  FROM `Projects` p " +
        "  LEFT JOIN `Relations` r ON r.`id_supplier` = p.`Customer-id` ";

    private static async Task<List<FotoDoel>> LeesDoelen(MySqlCommand cmd)
    {
        var lijst = new List<FotoDoel>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new FotoDoel(Convert.ToInt32(r.GetValue(0)), r.IsDBNull(1) ? null : Convert.ToInt32(r.GetValue(1)), S(r, 2),
                                   // de omschrijving van de werkorder is vaak gelijk aan die van het project: dan één keer
                                   string.Join(" · ", S(r, 3).Split(" · ").Distinct(StringComparer.OrdinalIgnoreCase)), S(r, 4)));
        return lijst;
    }

    private static List<string> Woorden(string zoek)
        => (zoek ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(6).ToList();

    public async Task<List<FotoDoel>> ZoekWerkordersAsync(string zoek, int max)
    {
        var woorden = Woorden(zoek);
        if (woorden.Count == 0) return new();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("", cn);
        var waar = new List<string> { "COALESCE(w.`WorkOrderStatus`, 1) <> 0", "COALESCE(w.`Deleted`, 0) = 0" };
        for (var i = 0; i < woorden.Count; i++)
        {
            // elk woord moet ergens voorkomen: nummer, omschrijving, project of klant
            waar.Add($"({WoNummer} LIKE @w{i} OR w.`Werkbonomschrijving` LIKE @w{i} OR p.`Projectdescription` LIKE @w{i} OR r.`name` LIKE @w{i})");
            cmd.Parameters.AddWithValue($"@w{i}", "%" + woorden[i] + "%");
        }
        cmd.CommandText = WerkorderSelect + " WHERE " + string.Join(" AND ", waar) +
                          $" ORDER BY p.`Projectnumber` DESC, w.`Werkbonnummer` LIMIT {Math.Clamp(max, 1, 100)}";
        return await LeesDoelen(cmd);
    }

    public async Task<List<FotoDoel>> ZoekProjectenAsync(string zoek, int max)
    {
        var woorden = Woorden(zoek);
        if (woorden.Count == 0) return new();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("", cn);
        var waar = new List<string> { "COALESCE(p.`ProjectStatus`, 1) <> 0" };
        for (var i = 0; i < woorden.Count; i++)
        {
            waar.Add($"(CAST(p.`Projectnumber` AS CHAR) LIKE @w{i} OR p.`Projectdescription` LIKE @w{i} OR r.`name` LIKE @w{i})");
            cmd.Parameters.AddWithValue($"@w{i}", "%" + woorden[i] + "%");
        }
        cmd.CommandText = ProjectSelect + " WHERE " + string.Join(" AND ", waar) +
                          $" ORDER BY p.`Projectnumber` DESC LIMIT {Math.Clamp(max, 1, 100)}";
        return await LeesDoelen(cmd);
    }

    public async Task<List<FotoDoel>> DoelenVanWerkordersAsync(IReadOnlyList<int> werkbonIds)
    {
        var ids = werkbonIds.Distinct().Take(50).ToList();
        if (ids.Count == 0) return new();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(WerkorderSelect + $" WHERE w.`Werkbon-id` IN ({string.Join(", ", ids)})", cn);
        var gevonden = await LeesDoelen(cmd);
        // in de volgorde waarin ze gevraagd zijn (meest recent gebruikt eerst)
        return ids.Select(id => gevonden.FirstOrDefault(d => d.WerkbonId == id)).OfType<FotoDoel>().ToList();
    }

    public async Task<List<string>> OnderwerpenAsync(int projectId)
    {
        var lijst = new List<string>();
        if (!await TabelAanwezigAsync()) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Onderwerp` FROM `ProjectFotos` WHERE `ProjectID` = @p GROUP BY `Onderwerp` ORDER BY MAX(`GemaaktOp`) DESC LIMIT 30", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(S(r, 0));
        return lijst;
    }

    private static string Kort(string? tekst, int max)
    {
        tekst = (tekst ?? "").Trim();
        return tekst.Length <= max ? tekst : tekst[..max];
    }

    public async Task<int> VoegToeAsync(int projectId, int? werkbonId, string onderwerp, byte[] miniatuur, int? door)
    {
        if (!await TabelAanwezigAsync()) throw new InvalidOperationException("De tabel ProjectFotos bestaat nog niet (ProjectFotos_MariaDB.sql).");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProjectFotos` (`ProjectID`, `WerkbonID`, `Onderwerp`, `Miniatuur`, `GemaaktOp`, `GemaaktDoor`) " +
            "VALUES (@p, @w, @onderwerp, @klein, NOW(), @door)", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        cmd.Parameters.AddWithValue("@w", (object?)werkbonId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@onderwerp", Kort(onderwerp, 100));
        cmd.Parameters.AddWithValue("@klein", miniatuur);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    public async Task ZetBestandAsync(int fotoId, int fileStoreId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `ProjectFotos` SET `FileStoreID` = @f WHERE `ProjectFotoID` = @id", cn);
        cmd.Parameters.AddWithValue("@f", fileStoreId);
        cmd.Parameters.AddWithValue("@id", fotoId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetNotitieAsync(int fotoId, string? notitie)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `ProjectFotos` SET `Notitie` = @n WHERE `ProjectFotoID` = @id", cn);
        cmd.Parameters.AddWithValue("@n", string.IsNullOrWhiteSpace(notitie) ? DBNull.Value : Kort(notitie, 500));
        cmd.Parameters.AddWithValue("@id", fotoId);
        await cmd.ExecuteNonQueryAsync();
    }

    private const string FotoSelect =
        "SELECT f.`ProjectFotoID`, f.`ProjectID`, f.`WerkbonID`, " +
        "       CASE WHEN w.`Werkbon-id` IS NULL THEN '' ELSE " + WoNummer + " END, " +
        "       f.`Onderwerp`, COALESCE(f.`Notitie`, ''), f.`FileStoreID`, f.`GemaaktOp`, f.`GemaaktDoor`, COALESCE(wn.`NaamVoluit`, '') " +
        "  FROM `ProjectFotos` f " +
        "  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = f.`WerkbonID` " +
        "  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
        "  LEFT JOIN `Werknemers` wn ON wn.`Werknemer-id` = f.`GemaaktDoor` ";

    private static ProjectFoto Lees(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0), ProjectId = Convert.ToInt32(r.GetValue(1)),
        WerkbonId = r.IsDBNull(2) ? null : Convert.ToInt32(r.GetValue(2)), Werkorder = S(r, 3),
        Onderwerp = S(r, 4), Notitie = S(r, 5),
        FileStoreId = r.IsDBNull(6) ? null : Convert.ToInt32(r.GetValue(6)),
        GemaaktOp = Convert.ToDateTime(r.GetValue(7)),
        GemaaktDoorId = r.IsDBNull(8) ? null : Convert.ToInt32(r.GetValue(8)), GemaaktDoor = S(r, 9).Trim(),
    };

    public async Task<ProjectFoto?> GetAsync(int fotoId)
    {
        if (!await TabelAanwezigAsync()) return null;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(FotoSelect + " WHERE f.`ProjectFotoID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", fotoId);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Lees(r) : null;
    }

    public async Task VerwijderAsync(int fotoId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `ProjectFotos` WHERE `ProjectFotoID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", fotoId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ProjectFoto>> VanProjectAsync(int projectId)
    {
        var lijst = new List<ProjectFoto>();
        if (!await TabelAanwezigAsync()) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(FotoSelect + " WHERE f.`ProjectID` = @p ORDER BY f.`GemaaktOp` DESC, f.`ProjectFotoID` DESC LIMIT 2000", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(Lees(r));
        return lijst;
    }

    public async Task<int> AantalAsync(int projectId)
    {
        if (!await TabelAanwezigAsync()) return 0;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM `ProjectFotos` WHERE `ProjectID` = @p", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<byte[]?> MiniatuurAsync(int fotoId)
    {
        if (!await TabelAanwezigAsync()) return null;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT `Miniatuur` FROM `ProjectFotos` WHERE `ProjectFotoID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", fotoId);
        return await cmd.ExecuteScalarAsync() as byte[];
    }
}

/// <summary>
/// Alleen om de projectfoto's lokaal te proberen zonder de tabel (ProjectFotos__Geheugen=true): werkorders en projecten
/// zoeken gaat echt (alleen lezen), de foto's zelf blijven in het geheugen.
/// </summary>
public class GeheugenProjectFotoRepository : IProjectFotoRepository
{
    private readonly IProjectFotoRepository _echt;
    private readonly List<(ProjectFoto Foto, byte[] Klein)> _lijst = new();
    private int _volgende = 1;
    public GeheugenProjectFotoRepository(IProjectFotoRepository echt) => _echt = echt;

    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);
    public Task<List<FotoDoel>> ZoekWerkordersAsync(string zoek, int max) => _echt.ZoekWerkordersAsync(zoek, max);
    public Task<List<FotoDoel>> ZoekProjectenAsync(string zoek, int max) => _echt.ZoekProjectenAsync(zoek, max);
    public Task<List<FotoDoel>> DoelenVanWerkordersAsync(IReadOnlyList<int> werkbonIds) => _echt.DoelenVanWerkordersAsync(werkbonIds);
    public Task<List<string>> OnderwerpenAsync(int projectId)
    {
        lock (_lijst) return Task.FromResult(_lijst.Where(x => x.Foto.ProjectId == projectId).OrderByDescending(x => x.Foto.GemaaktOp)
            .Select(x => x.Foto.Onderwerp).Distinct().ToList());
    }
    public Task<int> VoegToeAsync(int projectId, int? werkbonId, string onderwerp, byte[] miniatuur, int? door)
    {
        lock (_lijst)
        {
            var id = _volgende++;
            _lijst.Add((new ProjectFoto { Id = id, ProjectId = projectId, WerkbonId = werkbonId, Werkorder = werkbonId is null ? "" : "werkorder",
                                          Onderwerp = onderwerp.Trim(), GemaaktOp = DateTime.Now, GemaaktDoorId = door, GemaaktDoor = "Tester" }, miniatuur));
            return Task.FromResult(id);
        }
    }
    public Task ZetBestandAsync(int fotoId, int fileStoreId) => Task.CompletedTask;
    public Task ZetNotitieAsync(int fotoId, string? notitie)
    {
        lock (_lijst) { if (_lijst.FirstOrDefault(x => x.Foto.Id == fotoId).Foto is { } f) f.Notitie = (notitie ?? "").Trim(); }
        return Task.CompletedTask;
    }
    public Task<ProjectFoto?> GetAsync(int fotoId) { lock (_lijst) return Task.FromResult<ProjectFoto?>(_lijst.FirstOrDefault(x => x.Foto.Id == fotoId).Foto); }
    public Task VerwijderAsync(int fotoId) { lock (_lijst) _lijst.RemoveAll(x => x.Foto.Id == fotoId); return Task.CompletedTask; }
    public Task<List<ProjectFoto>> VanProjectAsync(int projectId)
    {
        lock (_lijst) return Task.FromResult(_lijst.Where(x => x.Foto.ProjectId == projectId).OrderByDescending(x => x.Foto.GemaaktOp).Select(x => x.Foto).ToList());
    }
    public Task<int> AantalAsync(int projectId) { lock (_lijst) return Task.FromResult(_lijst.Count(x => x.Foto.ProjectId == projectId)); }
    public Task<byte[]?> MiniatuurAsync(int fotoId) { lock (_lijst) return Task.FromResult<byte[]?>(_lijst.FirstOrDefault(x => x.Foto.Id == fotoId).Klein); }
}
