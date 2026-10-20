using System.Collections.Concurrent;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Eén welkom op het scherm in de hal (tabel Welkomstbord, Welkomstbord_MariaDB.sql).</summary>
public class WelkomItem
{
    public int Id { get; set; }
    public DateOnly DatumVan { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly DatumTot { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public TimeOnly? TijdVan { get; set; }
    public TimeOnly? TijdTot { get; set; }
    public int? RelatieId { get; set; }
    public string Relatie { get; set; } = "";
    public string Tekst { get; set; } = "";
    public int? GastheerId { get; set; }
    public string Gastheer { get; set; } = "";
    public string AangemaaktDoor { get; set; } = "";

    /// <summary>Staat dit welkom op <paramref name="nu"/> op het scherm?</summary>
    public bool Actief(DateTime nu)
    {
        var dag = DateOnly.FromDateTime(nu);
        var tijd = TimeOnly.FromDateTime(nu);
        return dag >= DatumVan && dag <= DatumTot
            && (TijdVan is null || tijd >= TijdVan) && (TijdTot is null || tijd < TijdTot);
    }

    public bool Verlopen(DateTime nu)
        => DateOnly.FromDateTime(nu) > DatumTot
           || (DateOnly.FromDateTime(nu) == DatumTot && TijdTot is { } t && TimeOnly.FromDateTime(nu) >= t);
}

public interface IWelkomRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();
    /// <summary>Welkomsten die nog komen of nu lopen (met verlopen: ook de laatste 90 dagen), oplopend op datum.</summary>
    Task<List<WelkomItem>> LijstAsync(bool ookVerlopen);
    /// <summary>Alles wat vandaag geldt (de tv kiest zelf wat op dit moment actief is).</summary>
    Task<List<WelkomItem>> VandaagAsync(DateOnly vandaag);
    Task<WelkomItem?> GetAsync(int id);
    Task<int> BewaarAsync(WelkomItem w, int? door);
    Task VerwijderAsync(int id);
}

public class GeenWelkomRepository : IWelkomRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<WelkomItem>> LijstAsync(bool ookVerlopen) => Task.FromResult(new List<WelkomItem>());
    public Task<List<WelkomItem>> VandaagAsync(DateOnly vandaag) => Task.FromResult(new List<WelkomItem>());
    public Task<WelkomItem?> GetAsync(int id) => Task.FromResult<WelkomItem?>(null);
    public Task<int> BewaarAsync(WelkomItem w, int? door) => throw new InvalidOperationException("Er is geen database.");
    public Task VerwijderAsync(int id) => Task.CompletedTask;
}

/// <summary>Alleen om lokaal te testen zonder de tabel Welkomstbord: Welkom__Geheugen=true (namen via de echte relaties).</summary>
public class GeheugenWelkomRepository : IWelkomRepository
{
    private readonly ConcurrentDictionary<int, WelkomItem> _items = new();
    private int _volgende;
    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);

    public Task<List<WelkomItem>> LijstAsync(bool ookVerlopen)
        => Task.FromResult(_items.Values.Where(w => ookVerlopen || !w.Verlopen(DateTime.Now))
                                 .OrderBy(w => w.DatumVan).ThenBy(w => w.TijdVan).ToList());

    public Task<List<WelkomItem>> VandaagAsync(DateOnly vandaag)
        => Task.FromResult(_items.Values.Where(w => w.DatumVan <= vandaag && w.DatumTot >= vandaag).OrderBy(w => w.TijdVan).ToList());

    public Task<WelkomItem?> GetAsync(int id) => Task.FromResult(_items.TryGetValue(id, out var w) ? w : null);

    public Task<int> BewaarAsync(WelkomItem w, int? door)
    {
        if (w.Id == 0) w.Id = Interlocked.Increment(ref _volgende);
        _items[w.Id] = w;
        return Task.FromResult(w.Id);
    }

    public Task VerwijderAsync(int id) { _items.TryRemove(id, out _); return Task.CompletedTask; }
}

public class MySqlWelkomRepository : IWelkomRepository
{
    private readonly string _cs;
    private bool _tabel;
    public MySqlWelkomRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Welkomstbord'", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _tabel;
    }

    private const string Select =
        "SELECT b.`WelkomID`, b.`DatumVan`, b.`DatumTot`, b.`TijdVan`, b.`TijdTot`, b.`RelatieID`, COALESCE(r.`name`, ''), " +
        "       COALESCE(b.`Tekst`, ''), b.`GastheerID`, COALESCE(g.`NaamVoluit`, ''), COALESCE(a.`NaamVoluit`, '') " +
        "  FROM `Welkomstbord` b " +
        "  LEFT JOIN `Relations` r ON r.`id_supplier` = b.`RelatieID` " +
        "  LEFT JOIN `Werknemers` g ON g.`Werknemer-id` = b.`GastheerID` " +
        "  LEFT JOIN `Werknemers` a ON a.`Werknemer-id` = b.`AangemaaktDoor` ";

    private static TimeOnly? Tijd(MySqlDataReader r, int i)
        => r.IsDBNull(i) ? null : r.GetValue(i) is TimeSpan ts ? TimeOnly.FromTimeSpan(ts) : TimeOnly.FromDateTime(Convert.ToDateTime(r.GetValue(i)));

    private static async Task<List<WelkomItem>> LeesAsync(MySqlCommand cmd)
    {
        var lijst = new List<WelkomItem>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new WelkomItem
            {
                Id = r.GetInt32(0), DatumVan = DateOnly.FromDateTime(r.GetDateTime(1)), DatumTot = DateOnly.FromDateTime(r.GetDateTime(2)),
                TijdVan = Tijd(r, 3), TijdTot = Tijd(r, 4), RelatieId = r.IsDBNull(5) ? null : r.GetInt32(5), Relatie = r.GetString(6),
                Tekst = r.GetString(7), GastheerId = r.IsDBNull(8) ? null : r.GetInt32(8), Gastheer = r.GetString(9),
                AangemaaktDoor = r.GetString(10),
            });
        return lijst;
    }

    public async Task<List<WelkomItem>> LijstAsync(bool ookVerlopen)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(Select +
            " WHERE b.`DatumTot` >= @vanaf ORDER BY b.`DatumVan`, b.`TijdVan`, b.`WelkomID` LIMIT 500", cn);
        cmd.Parameters.AddWithValue("@vanaf", ookVerlopen ? DateTime.Today.AddDays(-90) : DateTime.Today);
        var lijst = await LeesAsync(cmd);
        return ookVerlopen ? lijst : lijst.Where(w => !w.Verlopen(DateTime.Now)).ToList();
    }

    public async Task<List<WelkomItem>> VandaagAsync(DateOnly vandaag)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(Select +
            " WHERE b.`DatumVan` <= @d AND b.`DatumTot` >= @d ORDER BY b.`TijdVan`, b.`WelkomID`", cn);
        cmd.Parameters.AddWithValue("@d", vandaag.ToDateTime(TimeOnly.MinValue));
        return await LeesAsync(cmd);
    }

    public async Task<WelkomItem?> GetAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(Select + " WHERE b.`WelkomID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        return (await LeesAsync(cmd)).FirstOrDefault();
    }

    public async Task<int> BewaarAsync(WelkomItem w, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(w.Id == 0
            ? "INSERT INTO `Welkomstbord` (`DatumVan`, `DatumTot`, `TijdVan`, `TijdTot`, `RelatieID`, `Tekst`, `GastheerID`, `AangemaaktOp`, `AangemaaktDoor`) " +
              "VALUES (@van, @tot, @tv, @tt, @rel, @tekst, @gh, NOW(), @door)"
            : "UPDATE `Welkomstbord` SET `DatumVan` = @van, `DatumTot` = @tot, `TijdVan` = @tv, `TijdTot` = @tt, `RelatieID` = @rel, " +
              "       `Tekst` = @tekst, `GastheerID` = @gh, `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door WHERE `WelkomID` = @id", cn);
        cmd.Parameters.AddWithValue("@van", w.DatumVan.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tot", (w.DatumTot < w.DatumVan ? w.DatumVan : w.DatumTot).ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tv", w.TijdVan is { } tv ? tv.ToTimeSpan() : DBNull.Value);
        cmd.Parameters.AddWithValue("@tt", w.TijdTot is { } tt ? tt.ToTimeSpan() : DBNull.Value);
        cmd.Parameters.AddWithValue("@rel", (object?)w.RelatieId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tekst", string.IsNullOrWhiteSpace(w.Tekst) ? DBNull.Value : w.Tekst.Trim()[..Math.Min(1000, w.Tekst.Trim().Length)]);
        cmd.Parameters.AddWithValue("@gh", (object?)w.GastheerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        if (w.Id != 0) cmd.Parameters.AddWithValue("@id", w.Id);
        await cmd.ExecuteNonQueryAsync();
        if (w.Id == 0) w.Id = (int)cmd.LastInsertedId;
        return w.Id;
    }

    public async Task VerwijderAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `Welkomstbord` WHERE `WelkomID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
