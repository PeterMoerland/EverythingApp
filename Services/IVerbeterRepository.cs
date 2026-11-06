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
    /// <summary>Wie de status of reactie het laatst wijzigde (Werknemers.Werknemer-id); krijgt een melding bij een nieuw gespreksbericht.</summary>
    public int? GewijzigdDoorId { get; set; }
    /// <summary>Meegestuurde schermafdrukken: FileStore-id's (StoreType 340, MasterID = dit verzoek).</summary>
    public List<int> Schermafdrukken { get; set; } = new();
}

/// <summary>Een bericht in het gesprek onder een verbeterverzoek (tabel VerbeterverzoekBerichten, VerbeterverzoekBerichten_MariaDB.sql).</summary>
public class VerzoekBericht
{
    public int Id { get; set; }
    public int VerzoekId { get; set; }
    public DateTime Op { get; set; }
    public int? DoorId { get; set; }
    public string Door { get; set; } = "";
    public string Werkplek { get; set; } = "";
    public string Tekst { get; set; } = "";
}

public static class VerzoekStatus
{
    /// <summary>FileStore.StoreType van een schermafdruk bij een verbeterverzoek (MasterID = VerzoekID).</summary>
    public const int StoreTypeSchermafdruk = 340;

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

    // Gesprek per verzoek (wens collega 6-11-2026).
    /// <summary>Bestaat de tabel VerbeterverzoekBerichten?</summary>
    Task<bool> GesprekMogelijkAsync();
    /// <summary>Aantal berichten per verzoek (alleen verzoeken met berichten).</summary>
    Task<Dictionary<int, int>> AantalBerichtenAsync();
    /// <summary>Het gesprek bij een verzoek, oudste eerst.</summary>
    Task<List<VerzoekBericht>> BerichtenAsync(int verzoekId);
    /// <summary>Een bericht toevoegen; geeft het BerichtID.</summary>
    Task<int> BerichtToevoegenAsync(int verzoekId, string tekst, int? door, string werkplek);
}

public class GeenVerbeterRepository : IVerbeterRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<Verbeterverzoek>> LijstAsync() => Task.FromResult(new List<Verbeterverzoek>());
    public Task<int> AantalNieuwAsync() => Task.FromResult(0);
    public Task<int> IndienenAsync(string tekst, string pagina, int? door, string werkplek) => throw new InvalidOperationException("Er is geen database.");
    public Task BewaarAsync(Verbeterverzoek v, int? door) => throw new InvalidOperationException("Er is geen database.");
    public Task<bool> GesprekMogelijkAsync() => Task.FromResult(false);
    public Task<Dictionary<int, int>> AantalBerichtenAsync() => Task.FromResult(new Dictionary<int, int>());
    public Task<List<VerzoekBericht>> BerichtenAsync(int verzoekId) => Task.FromResult(new List<VerzoekBericht>());
    public Task<int> BerichtToevoegenAsync(int verzoekId, string tekst, int? door, string werkplek) => throw new InvalidOperationException("Er is geen database.");
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
        v.GewijzigdDoorId = door;
        _items[v.Id] = v;
        return Task.CompletedTask;
    }

    private readonly ConcurrentDictionary<int, VerzoekBericht> _berichten = new();
    private int _volgendBericht;

    public Task<bool> GesprekMogelijkAsync() => Task.FromResult(true);

    public Task<Dictionary<int, int>> AantalBerichtenAsync()
        => Task.FromResult(_berichten.Values.GroupBy(b => b.VerzoekId).ToDictionary(g => g.Key, g => g.Count()));

    public Task<List<VerzoekBericht>> BerichtenAsync(int verzoekId)
        => Task.FromResult(_berichten.Values.Where(b => b.VerzoekId == verzoekId).OrderBy(b => b.Op).ThenBy(b => b.Id).ToList());

    public Task<int> BerichtToevoegenAsync(int verzoekId, string tekst, int? door, string werkplek)
    {
        var b = new VerzoekBericht
        {
            Id = Interlocked.Increment(ref _volgendBericht), VerzoekId = verzoekId, Op = DateTime.Now, DoorId = door,
            Door = door is null ? "" : $"werknemer {door}", Werkplek = werkplek, Tekst = tekst.Trim(),
        };
        _berichten[b.Id] = b;
        return Task.FromResult(b.Id);
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
            "       COALESCE(v.`Werkplek`, ''), v.`Status`, COALESCE(v.`Reactie`, ''), v.`GewijzigdOp`, COALESCE(g.`NaamVoluit`, ''), " +
            $"      (SELECT GROUP_CONCAT(f.`FileStoreID` ORDER BY f.`FileStoreID`) FROM `FileStore` f " +
            $"        WHERE f.`StoreType` = {VerzoekStatus.StoreTypeSchermafdruk} AND f.`MasterID` = v.`VerzoekID`), v.`GewijzigdDoor` " +
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
                Schermafdrukken = r.IsDBNull(11) ? new() : Convert.ToString(r.GetValue(11))!.Split(',')
                    .Select(s => int.TryParse(s, out var id) ? id : 0).Where(id => id > 0).ToList(),
                GewijzigdDoorId = r.IsDBNull(12) ? null : r.GetInt32(12),
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
        v.GewijzigdDoorId = door;
    }

    private bool _gesprek;

    public async Task<bool> GesprekMogelijkAsync()
    {
        if (_gesprek) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'VerbeterverzoekBerichten'", cn);
            _gesprek = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _gesprek;
    }

    public async Task<Dictionary<int, int>> AantalBerichtenAsync()
    {
        var uit = new Dictionary<int, int>();
        if (!await GesprekMogelijkAsync()) return uit;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT `VerzoekID`, COUNT(*) FROM `VerbeterverzoekBerichten` GROUP BY `VerzoekID`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit[r.GetInt32(0)] = Convert.ToInt32(r.GetValue(1));
        return uit;
    }

    public async Task<List<VerzoekBericht>> BerichtenAsync(int verzoekId)
    {
        var lijst = new List<VerzoekBericht>();
        if (!await GesprekMogelijkAsync()) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT b.`BerichtID`, b.`VerzoekID`, b.`Op`, b.`Door`, COALESCE(w.`NaamVoluit`, ''), COALESCE(b.`Werkplek`, ''), b.`Tekst` " +
            "  FROM `VerbeterverzoekBerichten` b LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = b.`Door` " +
            " WHERE b.`VerzoekID` = @id ORDER BY b.`Op`, b.`BerichtID`", cn);
        cmd.Parameters.AddWithValue("@id", verzoekId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new VerzoekBericht
            {
                Id = r.GetInt32(0), VerzoekId = r.GetInt32(1), Op = r.GetDateTime(2), DoorId = r.IsDBNull(3) ? null : r.GetInt32(3),
                Door = r.GetString(4), Werkplek = r.GetString(5), Tekst = r.GetString(6),
            });
        return lijst;
    }

    public async Task<int> BerichtToevoegenAsync(int verzoekId, string tekst, int? door, string werkplek)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `VerbeterverzoekBerichten` (`VerzoekID`, `Op`, `Door`, `Werkplek`, `Tekst`) " +
            "VALUES (@verzoek, NOW(), @door, @werkplek, @tekst); SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@verzoek", verzoekId);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@werkplek", werkplek.Length == 0 ? DBNull.Value : Kort(werkplek, 100));
        cmd.Parameters.AddWithValue("@tekst", Kort(tekst, 2000));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static string Kort(string s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
}
