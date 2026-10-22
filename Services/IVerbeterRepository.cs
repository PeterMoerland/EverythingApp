using System.Collections.Concurrent;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een idee of wens van een collega voor de app (tabel Verbeterverzoeken, Verbeterverzoeken_MariaDB.sql).</summary>
public class Verbeterverzoek
{
    public int Id { get; set; }
    public string Tekst { get; set; } = "";
    public string Pagina { get; set; } = "";
    public DateTime IngediendOp { get; set; }
    public int? IngediendDoorId { get; set; }
    public string IngediendDoor { get; set; } = "";
    public string Werkplek { get; set; } = "";
    public string Status { get; set; } = VerzoekStatus.Nieuw;
    public string Reactie { get; set; } = "";
    public DateTime? GewijzigdOp { get; set; }
    public string GewijzigdDoor { get; set; } = "";
}

public static class VerzoekStatus
{
    public const string Nieuw = "nieuw", Gepland = "gepland", Bezig = "bezig", Gedaan = "gedaan", Afgewezen = "afgewezen";
    public static readonly string[] Alle = { Nieuw, Gepland, Bezig, Gedaan, Afgewezen };
    public static bool IsOpen(string status) => status is Nieuw or Gepland or Bezig;
    public static string Kleur(string status) => status switch
    {
        Nieuw => "s-geel", Gepland => "s-blauw", Bezig => "s-oranje", Gedaan => "s-groen", _ => "s-grijs",
    };
}

public interface IVerbeterRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();
    /// <summary>Alle verzoeken, nieuwste eerst.</summary>
    Task<List<Verbeterverzoek>> LijstAsync();
    Task<int> AantalNieuwAsync();
    Task<int> IndienenAsync(string tekst, string pagina, int? door, string werkplek);
    /// <summary>Tekst, status en reactie bijwerken (Directie).</summary>
    Task BewaarAsync(Verbeterverzoek v, int? door);
}

public class GeenVerbeterRepository : IVerbeterRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<Verbeterverzoek>> LijstAsync() => Task.FromResult(new List<Verbeterverzoek>());
    public Task<int> AantalNieuwAsync() => Task.FromResult(0);
    public Task<int> IndienenAsync(string tekst, string pagina, int? door, string werkplek) => throw new InvalidOperationException("Er is geen database.");
    public Task BewaarAsync(Verbeterverzoek v, int? door) => throw new InvalidOperationException("Er is geen database.");
}

/// <summary>Alleen om lokaal te testen zonder de tabel: Verbeter__Geheugen=true.</summary>
public class GeheugenVerbeterRepository : IVerbeterRepository
{
    private readonly ConcurrentDictionary<int, Verbeterverzoek> _items = new();
    private int _volgende;
    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);
    public Task<List<Verbeterverzoek>> LijstAsync() => Task.FromResult(_items.Values.OrderByDescending(v => v.IngediendOp).ToList());
    public Task<int> AantalNieuwAsync() => Task.FromResult(_items.Values.Count(v => v.Status == VerzoekStatus.Nieuw));

    public Task<int> IndienenAsync(string tekst, string pagina, int? door, string werkplek)
    {
        var v = new Verbeterverzoek
        {
            Id = Interlocked.Increment(ref _volgende), Tekst = tekst, Pagina = pagina, IngediendOp = DateTime.Now,
            IngediendDoorId = door, IngediendDoor = door is null ? "" : $"werknemer {door}", Werkplek = werkplek,
        };
        _items[v.Id] = v;
        return Task.FromResult(v.Id);
    }

    public Task BewaarAsync(Verbeterverzoek v, int? door)
    {
        v.GewijzigdOp = DateTime.Now;
        _items[v.Id] = v;
        return Task.CompletedTask;
    }
}

public class MySqlVerbeterRepository : IVerbeterRepository
{
    private readonly string _cs;
    private bool _tabel;
    public MySqlVerbeterRepository(string connectionString) => _cs = connectionString;
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
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Verbeterverzoeken'", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _tabel;
    }

    public async Task<List<Verbeterverzoek>> LijstAsync()
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT v.`VerzoekID`, v.`Tekst`, COALESCE(v.`Pagina`, ''), v.`IngediendOp`, v.`IngediendDoor`, COALESCE(i.`NaamVoluit`, ''), " +
            "       COALESCE(v.`Werkplek`, ''), v.`Status`, COALESCE(v.`Reactie`, ''), v.`GewijzigdOp`, COALESCE(g.`NaamVoluit`, '') " +
            "  FROM `Verbeterverzoeken` v " +
            "  LEFT JOIN `Werknemers` i ON i.`Werknemer-id` = v.`IngediendDoor` " +
            "  LEFT JOIN `Werknemers` g ON g.`Werknemer-id` = v.`GewijzigdDoor` " +
            " ORDER BY v.`IngediendOp` DESC, v.`VerzoekID` DESC LIMIT 1000", cn);
        var lijst = new List<Verbeterverzoek>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Verbeterverzoek
            {
                Id = r.GetInt32(0), Tekst = r.GetString(1), Pagina = r.GetString(2), IngediendOp = r.GetDateTime(3),
                IngediendDoorId = r.IsDBNull(4) ? null : r.GetInt32(4), IngediendDoor = r.GetString(5), Werkplek = r.GetString(6),
                Status = r.GetString(7), Reactie = r.GetString(8), GewijzigdOp = r.IsDBNull(9) ? null : r.GetDateTime(9),
                GewijzigdDoor = r.GetString(10),
            });
        return lijst;
    }

    public async Task<int> AantalNieuwAsync()
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM `Verbeterverzoeken` WHERE `Status` = 'nieuw'", cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<int> IndienenAsync(string tekst, string pagina, int? door, string werkplek)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `Verbeterverzoeken` (`Tekst`, `Pagina`, `IngediendOp`, `IngediendDoor`, `Werkplek`, `Status`) " +
            "VALUES (@tekst, @pagina, NOW(), @door, @werkplek, 'nieuw'); SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@tekst", Kort(tekst, 2000));
        cmd.Parameters.AddWithValue("@pagina", Kort(pagina, 300));
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@werkplek", werkplek.Length == 0 ? DBNull.Value : Kort(werkplek, 100));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task BewaarAsync(Verbeterverzoek v, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Verbeterverzoeken` SET `Tekst` = @tekst, `Status` = @status, `Reactie` = @reactie, " +
            "       `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door WHERE `VerzoekID` = @id", cn);
        cmd.Parameters.AddWithValue("@tekst", Kort(v.Tekst, 2000));
        cmd.Parameters.AddWithValue("@status", VerzoekStatus.Alle.Contains(v.Status) ? v.Status : VerzoekStatus.Nieuw);
        cmd.Parameters.AddWithValue("@reactie", v.Reactie.Trim().Length == 0 ? DBNull.Value : Kort(v.Reactie, 2000));
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", v.Id);
        await cmd.ExecuteNonQueryAsync();
        v.GewijzigdOp = DateTime.Now;
    }

    private static string Kort(string s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
}
