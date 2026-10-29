using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Rol in een projectteam (tabel ProjectRollen, te onderhouden in Beheer ▸ Projectrollen).</summary>
public record ProjectRol(int Id, string Naam, int Volgorde, bool Actief);

/// <summary>Een werknemer met een rol in een project (tabel ProjectTeam).</summary>
public record TeamLid(int Id, int RolId, int WerknemerId, string Naam);

/// <summary>
/// Projectteam (Projecten ▸ tab Team, wens gebruiker 28-10-2026): per project per rol een of meer werknemers.
/// Tabellen uit ProjectTeam_MariaDB.sql; zolang dat script niet gedraaid is geven de lees-methodes null.
/// </summary>
public interface IProjectTeamRepository
{
    bool Beschikbaar { get; }
    /// <summary>Alle rollen op volgorde (ook niet-actieve); null = de tabellen bestaan nog niet.</summary>
    Task<List<ProjectRol>?> RollenAsync();
    Task<List<TeamLid>> TeamAsync(int projectId);
    /// <summary>Geeft het nieuwe Id terug; staat de werknemer al met die rol in het team, dan het bestaande.</summary>
    Task<int> VoegToeAsync(int projectId, int rolId, int werknemerId, int? door);
    Task VerwijderAsync(int teamLidId);
}

public class GeenProjectTeamRepository : IProjectTeamRepository
{
    public bool Beschikbaar => false;
    public Task<List<ProjectRol>?> RollenAsync() => Task.FromResult<List<ProjectRol>?>(null);
    public Task<List<TeamLid>> TeamAsync(int projectId) => Task.FromResult(new List<TeamLid>());
    public Task<int> VoegToeAsync(int projectId, int rolId, int werknemerId, int? door) => throw new InvalidOperationException("Er is geen database.");
    public Task VerwijderAsync(int teamLidId) => Task.CompletedTask;
}

/// <summary>
/// Lokaal testen zonder in de database te schrijven (ProjectTeam__Geheugen=true): rollen uit de database als die er
/// zijn, anders de standaardrollen uit het script; het team alleen in het geheugen, namen uit de echte werknemers.
/// </summary>
public class GeheugenProjectTeamRepository : IProjectTeamRepository
{
    private readonly IProjectTeamRepository? _echt;
    private readonly IProjectRepository _projecten;
    private readonly List<(int Id, int ProjectId, int RolId, int WerknemerId)> _team = new();
    private int _volgend = 1;

    public GeheugenProjectTeamRepository(IProjectTeamRepository? echt, IProjectRepository projecten)
        => (_echt, _projecten) = (echt, projecten);

    public bool Beschikbaar => true;

    public async Task<List<ProjectRol>?> RollenAsync()
    {
        try { if (_echt is not null && await _echt.RollenAsync() is { } r) return r; } catch { }
        string[] namen = { "Projectleider", "Sales", "Mechanical lead", "Electro", "Software lead", "Software support", "Montage", "Testleider" };
        return namen.Select((n, i) => new ProjectRol(i + 1, n, (i + 1) * 10, true)).ToList();
    }

    public async Task<List<TeamLid>> TeamAsync(int projectId)
    {
        var namen = (await _projecten.GetWerknemersAsync()).ToDictionary(k => k.Id, k => k.Naam);
        lock (_team)
            return _team.Where(t => t.ProjectId == projectId)
                        .Select(t => new TeamLid(t.Id, t.RolId, t.WerknemerId, namen.GetValueOrDefault(t.WerknemerId, $"#{t.WerknemerId}")))
                        .ToList();
    }

    public Task<int> VoegToeAsync(int projectId, int rolId, int werknemerId, int? door)
    {
        lock (_team)
        {
            var al = _team.FirstOrDefault(t => t.ProjectId == projectId && t.RolId == rolId && t.WerknemerId == werknemerId);
            if (al.Id != 0) return Task.FromResult(al.Id);
            _team.Add((_volgend, projectId, rolId, werknemerId));
            return Task.FromResult(_volgend++);
        }
    }

    public Task VerwijderAsync(int teamLidId)
    {
        lock (_team) _team.RemoveAll(t => t.Id == teamLidId);
        return Task.CompletedTask;
    }
}

public class MySqlProjectTeamRepository : IProjectTeamRepository
{
    private readonly string _cs;
    private bool _tabellen;
    public MySqlProjectTeamRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private async Task<bool> TabellenAsync(MySqlConnection cn)
    {
        if (_tabellen) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('ProjectRollen', 'ProjectTeam')", cn);
        _tabellen = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 2;
        return _tabellen;
    }

    public async Task<List<ProjectRol>?> RollenAsync()
    {
        await using var cn = await OpenAsync();
        if (!await TabellenAsync(cn)) return null;
        await using var cmd = new MySqlCommand(
            "SELECT `RolID`, `Naam`, `Volgorde`, `Actief` FROM `ProjectRollen` ORDER BY `Volgorde`, `Naam`", cn);
        var lijst = new List<ProjectRol>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ProjectRol(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetBoolean(3)));
        return lijst;
    }

    public async Task<List<TeamLid>> TeamAsync(int projectId)
    {
        await using var cn = await OpenAsync();
        if (!await TabellenAsync(cn)) return new();
        await using var cmd = new MySqlCommand(
            "SELECT t.`ProjectTeamID`, t.`RolID`, t.`WerknemerID`, " +
            "       COALESCE(w.`NaamVoluit`, CONCAT_WS(' ', w.`Voornaam`, w.`Achternaam`), CONCAT('#', t.`WerknemerID`)) " +
            "  FROM `ProjectTeam` t LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = t.`WerknemerID` " +
            " WHERE t.`ProjectID` = @p ORDER BY t.`ProjectTeamID`", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        var lijst = new List<TeamLid>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new TeamLid(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3)));
        return lijst;
    }

    public async Task<int> VoegToeAsync(int projectId, int rolId, int werknemerId, int? door)
    {
        await using var cn = await OpenAsync();
        if (!await TabellenAsync(cn))
            throw new InvalidOperationException("Het projectteam kan pas na ProjectTeam_MariaDB.sql.");
        // dubbel (zelfde project, rol en werknemer) blokkeert de unieke sleutel; dan het bestaande Id
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProjectTeam` (`ProjectID`, `RolID`, `WerknemerID`, `AangemaaktOp`, `AangemaaktDoor`) " +
            "VALUES (@p, @r, @w, NOW(), @door) ON DUPLICATE KEY UPDATE `ProjectTeamID` = LAST_INSERT_ID(`ProjectTeamID`); " +
            "SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        cmd.Parameters.AddWithValue("@r", rolId);
        cmd.Parameters.AddWithValue("@w", werknemerId);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task VerwijderAsync(int teamLidId)
    {
        await using var cn = await OpenAsync();
        if (!await TabellenAsync(cn)) return;
        await using var cmd = new MySqlCommand("DELETE FROM `ProjectTeam` WHERE `ProjectTeamID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", teamLidId);
        await cmd.ExecuteNonQueryAsync();
    }
}
