using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Eén tegel in de snelbalk bovenin (Snelkoppelingen_MariaDB.sql).</summary>
public class Snelkoppeling
{
    public int Id { get; set; }
    public string Naam { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>Lucide-naam; leeg = automatisch (het icoon van de module waar de Url bij hoort).</summary>
    public string? Icoon { get; set; }
    /// <summary>Eigen plaatje (PNG, 64x64); gaat voor het icoon.</summary>
    public byte[]? Plaatje { get; set; }
    public int Volgorde { get; set; }

    public Snelkoppeling Kopie() => new() { Id = Id, Naam = Naam, Url = Url, Icoon = Icoon, Plaatje = Plaatje, Volgorde = Volgorde };
}

/// <summary>Snelkoppelingen per gebruiker ('w:&lt;werknemer-id&gt;' of 'e:&lt;e-mail&gt;'), maximaal <see cref="Max"/>.</summary>
public interface ISnelkoppelingRepository
{
    const int Max = 10;
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();
    Task<List<Snelkoppeling>> GetAsync(string gebruiker);
    /// <summary>Achteraan toevoegen; geeft het id, of 0 als er al <see cref="Max"/> zijn.</summary>
    Task<int> VoegToeAsync(string gebruiker, Snelkoppeling s);
    Task BewaarAsync(string gebruiker, Snelkoppeling s);
    Task VerwijderAsync(string gebruiker, int id);
    Task ZetVolgordeAsync(string gebruiker, IReadOnlyList<int> ids);
}

public class GeenSnelkoppelingRepository : ISnelkoppelingRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<Snelkoppeling>> GetAsync(string gebruiker) => Task.FromResult(new List<Snelkoppeling>());
    public Task<int> VoegToeAsync(string gebruiker, Snelkoppeling s) => Task.FromResult(0);
    public Task BewaarAsync(string gebruiker, Snelkoppeling s) => Task.CompletedTask;
    public Task VerwijderAsync(string gebruiker, int id) => Task.CompletedTask;
    public Task ZetVolgordeAsync(string gebruiker, IReadOnlyList<int> ids) => Task.CompletedTask;
}

/// <summary>In het geheugen, alleen om lokaal te testen (Snelbalk:Geheugen = true).</summary>
public class GeheugenSnelkoppelingRepository : ISnelkoppelingRepository
{
    private readonly object _slot = new();
    private readonly List<(string Gebruiker, Snelkoppeling S)> _lijst = new();
    private int _volgend;
    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);

    public Task<List<Snelkoppeling>> GetAsync(string gebruiker)
    {
        lock (_slot)
            return Task.FromResult(_lijst.Where(x => x.Gebruiker == gebruiker).Select(x => x.S.Kopie()).OrderBy(s => s.Volgorde).ToList());
    }

    public Task<int> VoegToeAsync(string gebruiker, Snelkoppeling s)
    {
        lock (_slot)
        {
            var eigen = _lijst.Where(x => x.Gebruiker == gebruiker).ToList();
            if (eigen.Count >= ISnelkoppelingRepository.Max) return Task.FromResult(0);
            var n = s.Kopie();
            n.Id = ++_volgend;
            n.Volgorde = eigen.Count == 0 ? 0 : eigen.Max(x => x.S.Volgorde) + 1;
            _lijst.Add((gebruiker, n));
            return Task.FromResult(n.Id);
        }
    }

    public Task BewaarAsync(string gebruiker, Snelkoppeling s)
    {
        lock (_slot)
        {
            var i = _lijst.FindIndex(x => x.Gebruiker == gebruiker && x.S.Id == s.Id);
            if (i >= 0) { var k = s.Kopie(); k.Volgorde = _lijst[i].S.Volgorde; _lijst[i] = (gebruiker, k); }
        }
        return Task.CompletedTask;
    }

    public Task VerwijderAsync(string gebruiker, int id)
    { lock (_slot) _lijst.RemoveAll(x => x.Gebruiker == gebruiker && x.S.Id == id); return Task.CompletedTask; }

    public Task ZetVolgordeAsync(string gebruiker, IReadOnlyList<int> ids)
    {
        lock (_slot)
            for (var i = 0; i < ids.Count; i++)
                if (_lijst.FirstOrDefault(x => x.Gebruiker == gebruiker && x.S.Id == ids[i]).S is { } s) s.Volgorde = i;
        return Task.CompletedTask;
    }
}

public class MySqlSnelkoppelingRepository : ISnelkoppelingRepository
{
    private readonly string _cs;
    private bool _tabel;
    public MySqlSnelkoppelingRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    /// <summary>Alleen 'ja' wordt onthouden: na het script uitvoeren werkt het zonder herstart.</summary>
    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Snelkoppelingen'", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _tabel;
    }

    public async Task<List<Snelkoppeling>> GetAsync(string gebruiker)
    {
        var lijst = new List<Snelkoppeling>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ID`, `Naam`, `Url`, `Icoon`, `Plaatje`, `Volgorde` FROM `Snelkoppelingen` WHERE `Gebruiker` = @g " +
            $" ORDER BY `Volgorde`, `ID` LIMIT {ISnelkoppelingRepository.Max}", cn);
        cmd.Parameters.AddWithValue("@g", gebruiker);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Snelkoppeling
            {
                Id = r.GetInt32(0), Naam = r.IsDBNull(1) ? "" : r.GetString(1), Url = r.IsDBNull(2) ? "" : r.GetString(2),
                Icoon = r.IsDBNull(3) ? null : r.GetString(3), Plaatje = r.IsDBNull(4) ? null : (byte[])r.GetValue(4),
                Volgorde = r.GetInt32(5),
            });
        return lijst;
    }

    public async Task<int> VoegToeAsync(string gebruiker, Snelkoppeling s)
    {
        await using var cn = await OpenAsync();
        await using (var tel = new MySqlCommand("SELECT COUNT(*) FROM `Snelkoppelingen` WHERE `Gebruiker` = @g", cn))
        {
            tel.Parameters.AddWithValue("@g", gebruiker);
            if (Convert.ToInt32(await tel.ExecuteScalarAsync()) >= ISnelkoppelingRepository.Max) return 0;
        }
        await using var cmd = new MySqlCommand(
            "INSERT INTO `Snelkoppelingen` (`Gebruiker`, `Volgorde`, `Naam`, `Url`, `Icoon`, `Plaatje`) " +
            "SELECT @g, COALESCE(MAX(`Volgorde`), -1) + 1, @n, @u, @i, @p FROM `Snelkoppelingen` WHERE `Gebruiker` = @g; " +
            "SELECT LAST_INSERT_ID();", cn);
        Vul(cmd, gebruiker, s);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task BewaarAsync(string gebruiker, Snelkoppeling s)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Snelkoppelingen` SET `Naam` = @n, `Url` = @u, `Icoon` = @i, `Plaatje` = @p WHERE `ID` = @id AND `Gebruiker` = @g", cn);
        Vul(cmd, gebruiker, s);
        cmd.Parameters.AddWithValue("@id", s.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task VerwijderAsync(string gebruiker, int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `Snelkoppelingen` WHERE `ID` = @id AND `Gebruiker` = @g", cn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@g", gebruiker);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetVolgordeAsync(string gebruiker, IReadOnlyList<int> ids)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        for (var i = 0; i < ids.Count; i++)
        {
            await using var cmd = new MySqlCommand(
                "UPDATE `Snelkoppelingen` SET `Volgorde` = @v WHERE `ID` = @id AND `Gebruiker` = @g", cn, tx);
            cmd.Parameters.AddWithValue("@v", i);
            cmd.Parameters.AddWithValue("@id", ids[i]);
            cmd.Parameters.AddWithValue("@g", gebruiker);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    private static void Vul(MySqlCommand cmd, string gebruiker, Snelkoppeling s)
    {
        cmd.Parameters.AddWithValue("@g", gebruiker);
        cmd.Parameters.AddWithValue("@n", s.Naam.Trim().Length > 40 ? s.Naam.Trim()[..40] : s.Naam.Trim());
        cmd.Parameters.AddWithValue("@u", s.Url.Length > 500 ? s.Url[..500] : s.Url);
        cmd.Parameters.AddWithValue("@i", (object?)s.Icoon ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@p", (object?)s.Plaatje ?? DBNull.Value);
    }
}
