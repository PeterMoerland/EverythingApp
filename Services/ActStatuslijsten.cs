using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>De vaste statussen van een cel in een statuslijst (keuze gebruiker 24-11-2026: vast, niet per lijst).</summary>
public static class ActCelStatus
{
    public const string Todo = "todo", Bezig = "bezig", Testen = "testen", Klaar = "klaar";
    public static readonly (string Code, string Naam, string Kleur)[] Alle =
        { (Todo, "todo", "gray"), (Bezig, "bezig", "blue"), (Testen, "testen", "yellow"), (Klaar, "klaar", "green") };
    public static bool IsGeldig(string s) => Alle.Any(x => x.Code == s);
    public static string Naam(string s) => Alle.FirstOrDefault(x => x.Code == s).Naam ?? "";
    public static string Kleur(string s) => Alle.FirstOrDefault(x => x.Code == s).Kleur ?? "default";
}

/// <summary>Een cel: de status, en wie hem het laatst zette en wanneer.</summary>
public record ActStatusCel(string Status, int? Door, DateTime? Op);

public class ActStatusKolom { public int Id { get; set; } public string Naam { get; set; } = ""; }

/// <summary>Een rij of (<see cref="Kop"/>) een kopje; de rijen eronder horen bij dat kopje.</summary>
public class ActStatusRij
{
    public int Id { get; set; }
    public bool Kop { get; set; }
    public string Naam { get; set; } = "";
    public string Opmerking { get; set; } = "";
    /// <summary>Per kolom-id de cel; ontbreekt hij, dan is er nog niets ingevuld.</summary>
    public Dictionary<int, ActStatusCel> Cellen { get; set; } = new();
}

public class ActStatuslijst
{
    public int Id { get; set; }
    public int BoardId { get; set; }
    public string Naam { get; set; } = "";
    public List<ActStatusKolom> Kolommen { get; set; } = new();
    public List<ActStatusRij> Rijen { get; set; } = new();
    public int Totaal => Rijen.Count(r => !r.Kop) * Kolommen.Count;
    public int KlaarAantal => Rijen.Where(r => !r.Kop).Sum(r => Kolommen.Count(k => r.Cellen.GetValueOrDefault(k.Id)?.Status == ActCelStatus.Klaar));
}

/// <summary>
/// Statuslijsten op een Activity board (idee van Arnold, 24-11-2026): een tabel met kolommen (bv. machine 1 t/m 8),
/// rijen onder kopjes en per cel een status. Tabellen: ActivityBoard_Statuslijsten_MariaDB.sql. Met
/// ActivityBoard:Geheugen staat alles in het geheugen (lokaal testen zonder database).
/// </summary>
public class ActStatuslijsten
{
    private readonly string? _cs;
    private readonly bool _geheugen;
    private bool _aanwezig;

    private readonly object _slot = new();
    private readonly List<ActStatuslijst> _lijsten = new();
    private int _volgend = 1;

    public ActStatuslijsten(IConfiguration config)
    {
        _cs = config.GetConnectionString("MDE");
        _geheugen = config.GetValue<bool>("ActivityBoard:Geheugen");
    }

    private bool Live => !_geheugen && !string.IsNullOrWhiteSpace(_cs) && !_cs!.Contains("REPLACE_");
    private static string Kort(string? s, int max) { s = (s ?? "").Trim(); return s.Length <= max ? s : s[..max]; }

    public async Task<bool> AanwezigAsync()
    {
        if (_geheugen) return true;
        if (_aanwezig || !Live) return _aanwezig;
        return _aanwezig = Convert.ToInt32(await WaardeAsync(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ActivityBoardStatusCellen'")) > 0;
    }

    private async Task<object?> WaardeAsync(string sql, params (string Naam, object? Waarde)[] p)
    {
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        foreach (var (naam, waarde) in p) cmd.Parameters.AddWithValue(naam, waarde ?? DBNull.Value);
        return await cmd.ExecuteScalarAsync();
    }

    private async Task<int> NieuwAsync(string sql, params (string Naam, object? Waarde)[] p)
        => Convert.ToInt32(await WaardeAsync(sql + "; SELECT LAST_INSERT_ID();", p));

    // ------------------------------------------------------------------ lezen

    /// <summary>Alle statuslijsten van een board, compleet met kolommen, rijen en cellen.</summary>
    public async Task<List<ActStatuslijst>> VanBoardAsync(int boardId)
    {
        if (_geheugen) lock (_slot) return _lijsten.Where(l => l.BoardId == boardId).Select(Kopie).ToList();
        if (!await AanwezigAsync()) return new();

        var lijsten = new List<ActStatuslijst>();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using (var cmd = new MySqlCommand("SELECT `LijstID`, `Naam` FROM `ActivityBoardStatuslijsten` WHERE `BoardID` = @b ORDER BY `LijstID`", cn))
        {
            cmd.Parameters.AddWithValue("@b", boardId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) lijsten.Add(new ActStatuslijst { Id = r.GetInt32(0), BoardId = boardId, Naam = r.GetString(1) });
        }
        if (lijsten.Count == 0) return lijsten;
        var opId = lijsten.ToDictionary(l => l.Id);
        var ids = string.Join(",", opId.Keys);      // getallen uit de database zelf

        await using (var cmd = new MySqlCommand($"SELECT `KolomID`, `LijstID`, `Naam` FROM `ActivityBoardStatusKolommen` WHERE `LijstID` IN ({ids}) ORDER BY `Positie`, `KolomID`", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) opId[r.GetInt32(1)].Kolommen.Add(new ActStatusKolom { Id = r.GetInt32(0), Naam = r.GetString(2) });

        var rijen = new Dictionary<int, ActStatusRij>();
        await using (var cmd = new MySqlCommand($"SELECT `RijID`, `LijstID`, `IsKop`, `Naam`, `Opmerking` FROM `ActivityBoardStatusRijen` WHERE `LijstID` IN ({ids}) ORDER BY `Positie`, `RijID`", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var rij = new ActStatusRij { Id = r.GetInt32(0), Kop = Convert.ToInt32(r.GetValue(2)) != 0, Naam = r.GetString(3), Opmerking = r.IsDBNull(4) ? "" : r.GetString(4) };
                opId[r.GetInt32(1)].Rijen.Add(rij);
                rijen[rij.Id] = rij;
            }
        if (rijen.Count == 0) return lijsten;

        await using (var cmd = new MySqlCommand(
            "SELECT c.`RijID`, c.`KolomID`, c.`Status`, c.`GewijzigdDoor`, c.`GewijzigdOp` FROM `ActivityBoardStatusCellen` c " +
            $"  JOIN `ActivityBoardStatusRijen` r ON r.`RijID` = c.`RijID` WHERE r.`LijstID` IN ({ids})", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                if (rijen.TryGetValue(r.GetInt32(0), out var rij) && ActCelStatus.IsGeldig(r.GetString(2)))
                    rij.Cellen[r.GetInt32(1)] = new ActStatusCel(r.GetString(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.GetDateTime(4));
        return lijsten;
    }

    private static ActStatuslijst Kopie(ActStatuslijst l) => new()
    {
        Id = l.Id, BoardId = l.BoardId, Naam = l.Naam,
        Kolommen = l.Kolommen.Select(k => new ActStatusKolom { Id = k.Id, Naam = k.Naam }).ToList(),
        Rijen = l.Rijen.Select(r => new ActStatusRij { Id = r.Id, Kop = r.Kop, Naam = r.Naam, Opmerking = r.Opmerking, Cellen = new(r.Cellen) }).ToList(),
    };

    // ------------------------------------------------------------------ lijst

    /// <summary>Een nieuwe lijst met alvast de kolommen (namen); geeft het nummer.</summary>
    public async Task<int> NieuweLijstAsync(int boardId, string naam, IReadOnlyList<string> kolommen, int? door)
    {
        naam = Kort(naam, 200);
        if (_geheugen)
            lock (_slot)
            {
                var l = new ActStatuslijst { Id = _volgend++, BoardId = boardId, Naam = naam };
                foreach (var k in kolommen) l.Kolommen.Add(new ActStatusKolom { Id = _volgend++, Naam = Kort(k, 100) });
                _lijsten.Add(l);
                return l.Id;
            }
        var id = await NieuwAsync("INSERT INTO `ActivityBoardStatuslijsten` (`BoardID`, `Naam`, `GemaaktDoor`) VALUES (@b, @n, @d)", ("@b", boardId), ("@n", naam), ("@d", door));
        for (var i = 0; i < kolommen.Count; i++)
            await NieuwAsync("INSERT INTO `ActivityBoardStatusKolommen` (`LijstID`, `Naam`, `Positie`) VALUES (@l, @n, @p)", ("@l", id), ("@n", Kort(kolommen[i], 100)), ("@p", i + 1));
        return id;
    }

    public async Task HernoemLijstAsync(int lijstId, string naam)
    {
        naam = Kort(naam, 200);
        if (_geheugen) { lock (_slot) { if (_lijsten.FirstOrDefault(l => l.Id == lijstId) is { } l) l.Naam = naam; } return; }
        await WaardeAsync("UPDATE `ActivityBoardStatuslijsten` SET `Naam` = @n WHERE `LijstID` = @id", ("@n", naam), ("@id", lijstId));
    }

    public async Task VerwijderLijstAsync(int lijstId)
    {
        if (_geheugen) { lock (_slot) _lijsten.RemoveAll(l => l.Id == lijstId); return; }
        await WaardeAsync(
            "DELETE c FROM `ActivityBoardStatusCellen` c JOIN `ActivityBoardStatusRijen` r ON r.`RijID` = c.`RijID` WHERE r.`LijstID` = @id; " +
            "DELETE FROM `ActivityBoardStatusRijen` WHERE `LijstID` = @id; DELETE FROM `ActivityBoardStatusKolommen` WHERE `LijstID` = @id; " +
            "DELETE FROM `ActivityBoardStatuslijsten` WHERE `LijstID` = @id", ("@id", lijstId));
    }

    // ------------------------------------------------------------------ kolommen

    public async Task<int> NieuweKolomAsync(int lijstId, string naam)
    {
        naam = Kort(naam, 100);
        if (_geheugen)
            lock (_slot)
            {
                var k = new ActStatusKolom { Id = _volgend++, Naam = naam };
                _lijsten.FirstOrDefault(l => l.Id == lijstId)?.Kolommen.Add(k);
                return k.Id;
            }
        return await NieuwAsync(
            "INSERT INTO `ActivityBoardStatusKolommen` (`LijstID`, `Naam`, `Positie`) " +
            "SELECT @l, @n, COALESCE(MAX(`Positie`), 0) + 1 FROM `ActivityBoardStatusKolommen` WHERE `LijstID` = @l", ("@l", lijstId), ("@n", naam));
    }

    public async Task HernoemKolomAsync(int kolomId, string naam)
    {
        naam = Kort(naam, 100);
        if (_geheugen) { lock (_slot) { foreach (var k in _lijsten.SelectMany(l => l.Kolommen).Where(k => k.Id == kolomId)) k.Naam = naam; } return; }
        await WaardeAsync("UPDATE `ActivityBoardStatusKolommen` SET `Naam` = @n WHERE `KolomID` = @id", ("@n", naam), ("@id", kolomId));
    }

    public async Task VerwijderKolomAsync(int kolomId)
    {
        if (_geheugen)
        {
            lock (_slot) foreach (var l in _lijsten) { l.Kolommen.RemoveAll(k => k.Id == kolomId); foreach (var r in l.Rijen) r.Cellen.Remove(kolomId); }
            return;
        }
        await WaardeAsync("DELETE FROM `ActivityBoardStatusCellen` WHERE `KolomID` = @id; DELETE FROM `ActivityBoardStatusKolommen` WHERE `KolomID` = @id", ("@id", kolomId));
    }

    // ------------------------------------------------------------------ rijen

    /// <summary>Een rij of kopje erbij: onderaan, of (naRijId) direct na die rij.</summary>
    public async Task<int> NieuweRijAsync(int lijstId, string naam, bool kop, int? naRijId = null)
    {
        naam = Kort(naam, 200);
        if (_geheugen)
            lock (_slot)
            {
                var rij = new ActStatusRij { Id = _volgend++, Kop = kop, Naam = naam };
                if (_lijsten.FirstOrDefault(l => l.Id == lijstId) is { } l)
                {
                    var i = naRijId is int na ? l.Rijen.FindIndex(r => r.Id == na) : -1;
                    if (i >= 0) l.Rijen.Insert(i + 1, rij); else l.Rijen.Add(rij);
                }
                return rij.Id;
            }
        int positie;
        if (naRijId is int naId && await WaardeAsync("SELECT `Positie` FROM `ActivityBoardStatusRijen` WHERE `RijID` = @id AND `LijstID` = @l", ("@id", naId), ("@l", lijstId)) is { } p and not DBNull)
        {
            positie = Convert.ToInt32(p) + 1;
            await WaardeAsync("UPDATE `ActivityBoardStatusRijen` SET `Positie` = `Positie` + 1 WHERE `LijstID` = @l AND `Positie` >= @p", ("@l", lijstId), ("@p", positie));
        }
        else positie = Convert.ToInt32(await WaardeAsync("SELECT COALESCE(MAX(`Positie`), 0) + 1 FROM `ActivityBoardStatusRijen` WHERE `LijstID` = @l", ("@l", lijstId)));
        return await NieuwAsync("INSERT INTO `ActivityBoardStatusRijen` (`LijstID`, `IsKop`, `Naam`, `Positie`) VALUES (@l, @k, @n, @p)",
            ("@l", lijstId), ("@k", kop ? 1 : 0), ("@n", naam), ("@p", positie));
    }

    public async Task ZetRijAsync(int rijId, string naam, string opmerking)
    {
        naam = Kort(naam, 200);
        opmerking = (opmerking ?? "").Trim();
        if (_geheugen)
        {
            lock (_slot) foreach (var r in _lijsten.SelectMany(l => l.Rijen).Where(r => r.Id == rijId)) { r.Naam = naam; r.Opmerking = opmerking; }
            return;
        }
        await WaardeAsync("UPDATE `ActivityBoardStatusRijen` SET `Naam` = @n, `Opmerking` = @o WHERE `RijID` = @id",
            ("@n", naam), ("@o", opmerking.Length == 0 ? null : opmerking), ("@id", rijId));
    }

    public async Task VerwijderRijAsync(int rijId)
    {
        if (_geheugen) { lock (_slot) foreach (var l in _lijsten) l.Rijen.RemoveAll(r => r.Id == rijId); return; }
        await WaardeAsync("DELETE FROM `ActivityBoardStatusCellen` WHERE `RijID` = @id; DELETE FROM `ActivityBoardStatusRijen` WHERE `RijID` = @id", ("@id", rijId));
    }

    // ------------------------------------------------------------------ cellen

    /// <summary>De status van een cel zetten; leeg = de cel weer leegmaken.</summary>
    public async Task ZetCelAsync(int rijId, int kolomId, string status, int? door)
    {
        var leeg = string.IsNullOrEmpty(status);
        if (!leeg && !ActCelStatus.IsGeldig(status)) throw new ArgumentException("Onbekende status.");
        if (_geheugen)
        {
            lock (_slot)
                foreach (var r in _lijsten.SelectMany(l => l.Rijen).Where(r => r.Id == rijId))
                    if (leeg) r.Cellen.Remove(kolomId); else r.Cellen[kolomId] = new ActStatusCel(status, door, DateTime.Now);
            return;
        }
        if (leeg) await WaardeAsync("DELETE FROM `ActivityBoardStatusCellen` WHERE `RijID` = @r AND `KolomID` = @k", ("@r", rijId), ("@k", kolomId));
        else await WaardeAsync(
            "INSERT INTO `ActivityBoardStatusCellen` (`RijID`, `KolomID`, `Status`, `GewijzigdDoor`, `GewijzigdOp`) VALUES (@r, @k, @s, @d, NOW()) " +
            "ON DUPLICATE KEY UPDATE `Status` = @s, `GewijzigdDoor` = @d, `GewijzigdOp` = NOW()", ("@r", rijId), ("@k", kolomId), ("@s", status), ("@d", door));
    }
}
