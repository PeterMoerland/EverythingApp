using System.Collections.Concurrent;
using MySqlConnector;

namespace EverythingApp.Services;

// ============================================================================================================
//  BV-structuur (wens gebruiker 9-11-2026): welke eigen BV's er zijn, wie welk deel bezit, en welke vervallen zijn.
//  Tabellen: Relations (vinkjes Werkmaatschappij en MDEBV, kolom Opgeheven) en BvAandeelhouders (BvStructuur_MariaDB.sql).
//  Scherm: Financiën > BV-structuur (Components/Pages/BvStructuur.razor).
// ============================================================================================================

/// <summary>Een eigen BV (Relations met Werkmaatschappij of MDEBV), met wat je op het overzicht wilt zien.</summary>
public class EigenBv
{
    public int Id { get; set; }
    public string Naam { get; set; } = "";
    public string Afkorting { get; set; } = "";
    /// <summary>Relations.Werkmaatschappij: eigen BV die factureert (eigen administratie, bankrekeningen).</summary>
    public bool Werkmaatschappij { get; set; }
    /// <summary>Relations.MDEBV: hoort bij de MDE-groep (telt standaard mee in Liquiditeit).</summary>
    public bool MdeGroep { get; set; }
    public DateOnly? Opgeheven { get; set; }
    public string KvK { get; set; } = "";
    public string Btw { get; set; } = "";
    public int Werknemers { get; set; }
    public int Rekeningen { get; set; }
    public DateTime? LaatsteFactuur { get; set; }
    public int Facturen { get; set; }

    public bool IsOpgeheven => Opgeheven is DateOnly d && d <= DateOnly.FromDateTime(DateTime.Today);
    public string Kort => Afkorting.Length > 0 ? Afkorting : Naam;
}

/// <summary>Een aandeelhouder van een BV (BvAandeelhouders). Tot leeg = nog steeds.</summary>
public class BvAandeel
{
    public int Id { get; set; }
    public int BvId { get; set; }
    public int AandeelhouderId { get; set; }
    public decimal Percentage { get; set; }
    public DateOnly? Vanaf { get; set; }
    public DateOnly? Tot { get; set; }
    public string Opmerking { get; set; } = "";

    public bool Actueel => Tot is not DateOnly t || t > DateOnly.FromDateTime(DateTime.Today);
}

/// <summary>
/// Kolom Relations.Opgeheven in SQL, ook als het script nog niet gedraaid is (dan zonder): een opgeheven BV krijgt
/// "(opgeheven)" achter de naam en komt achteraan in de keuzelijsten. Niet weglaten: een oude factuur of een oud project
/// moet zijn BV blijven tonen.
/// </summary>
public static class BvOpgeheven
{
    private static volatile bool _er;
    private static DateTime _laatst = DateTime.MinValue;

    /// <summary>Bestaat de kolom? Eenmaal gevonden blijft het zo; anders hooguit eens per 5 minuten opnieuw kijken.</summary>
    public static async Task<bool> KolomAsync(MySqlConnection cn)
    {
        if (_er) return true;
        if (DateTime.UtcNow - _laatst < TimeSpan.FromMinutes(5)) return false;
        _laatst = DateTime.UtcNow;
        try
        {
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "AND TABLE_NAME = 'Relations' AND COLUMN_NAME = 'Opgeheven'", cn);
            _er = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _er;
    }

    /// <summary>SQL-voorwaarde "deze BV is opgeheven"; <paramref name="a"/> = tabelalias met punt, bv. "a.".</summary>
    public static string Is(bool kolom, string a = "") => kolom ? $"({a}`Opgeheven` IS NOT NULL AND {a}`Opgeheven` <= CURDATE())" : "FALSE";

    /// <summary>De naam, met " (opgeheven)" erachter als dat zo is.</summary>
    public static string Naam(bool kolom, string a = "")
        => kolom ? $"CONCAT(COALESCE({a}`name`, ''), IF({Is(true, a)}, ' (opgeheven)', ''))" : $"COALESCE({a}`name`, '')";

    /// <summary>Voor in ORDER BY: opgeheven BV's achteraan.</summary>
    public static string Achteraan(bool kolom, string a = "") => kolom ? $"{Is(true, a)}, " : "";
}

public interface IBvStructuurRepository
{
    bool Beschikbaar { get; }
    /// <summary>Kolom Opgeheven en tabel BvAandeelhouders aanwezig (script gedraaid)?</summary>
    Task<bool> TabelAanwezigAsync();
    Task<List<EigenBv>> BvsAsync();
    Task<List<BvAandeel>> AandelenAsync();
    Task<int> BewaarAandeelAsync(BvAandeel a, int? door);
    Task WisAandeelAsync(int id);
    Task ZetOpgehevenAsync(int bvId, DateOnly? datum);
}

public class GeenBvStructuurRepository : IBvStructuurRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<EigenBv>> BvsAsync() => Task.FromResult(new List<EigenBv>());
    public Task<List<BvAandeel>> AandelenAsync() => Task.FromResult(new List<BvAandeel>());
    public Task<int> BewaarAandeelAsync(BvAandeel a, int? door) => throw new InvalidOperationException("Er is geen database.");
    public Task WisAandeelAsync(int id) => Task.CompletedTask;
    public Task ZetOpgehevenAsync(int bvId, DateOnly? datum) => throw new InvalidOperationException("Er is geen database.");
}

/// <summary>
/// Lokaal testen zonder het script: BvStructuur__Geheugen=true. Leest de BV's echt (alleen lezen); aandeelhouders en
/// opgeheven blijven in het geheugen.
/// </summary>
public class GeheugenBvStructuurRepository : IBvStructuurRepository
{
    private readonly IBvStructuurRepository? _lezen;
    private readonly ConcurrentDictionary<int, BvAandeel> _aandelen = new();
    private readonly ConcurrentDictionary<int, DateOnly?> _opgeheven = new();
    private int _volgende;

    public GeheugenBvStructuurRepository(IBvStructuurRepository? lezen = null) => _lezen = lezen;

    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);

    public async Task<List<EigenBv>> BvsAsync()
    {
        var lijst = _lezen is null
            ? new List<EigenBv> { new() { Id = 1, Naam = "MDE Holding B.V. (test)", Afkorting = "HOLD", Werkmaatschappij = true, MdeGroep = true },
                                  new() { Id = 2, Naam = "MDE Automation B.V. (test)", Afkorting = "AUTO", Werkmaatschappij = true, MdeGroep = true } }
            : await _lezen.BvsAsync();
        foreach (var b in lijst)
            if (_opgeheven.TryGetValue(b.Id, out var d)) b.Opgeheven = d;
        return lijst;
    }

    public Task<List<BvAandeel>> AandelenAsync() => Task.FromResult(_aandelen.Values.OrderBy(a => a.Id).Select(Kopie).ToList());

    private static BvAandeel Kopie(BvAandeel a) => new()
    {
        Id = a.Id, BvId = a.BvId, AandeelhouderId = a.AandeelhouderId, Percentage = a.Percentage, Vanaf = a.Vanaf, Tot = a.Tot, Opmerking = a.Opmerking,
    };

    public Task<int> BewaarAandeelAsync(BvAandeel a, int? door)
    {
        if (a.Id <= 0) a.Id = Interlocked.Increment(ref _volgende);
        _aandelen[a.Id] = Kopie(a);
        return Task.FromResult(a.Id);
    }

    public Task WisAandeelAsync(int id) { _aandelen.TryRemove(id, out _); return Task.CompletedTask; }

    public Task ZetOpgehevenAsync(int bvId, DateOnly? datum) { _opgeheven[bvId] = datum; return Task.CompletedTask; }
}

public class MySqlBvStructuurRepository : IBvStructuurRepository
{
    private readonly string _cs;
    private bool _tabel;

    public MySqlBvStructuurRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : (r.GetValue(i).ToString() ?? "").Trim();
    private static DateOnly? D(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : DateOnly.FromDateTime(Convert.ToDateTime(r.GetValue(i)));

    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'BvAandeelhouders') " +
                "     + (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Relations' AND COLUMN_NAME = 'Opgeheven')", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 2;
        }
        catch { }
        return _tabel;
    }

    public async Task<List<EigenBv>> BvsAsync()
    {
        await using var cn = await OpenAsync();
        var opg = await BvOpgeheven.KolomAsync(cn);
        var aandelen = await TabelAanwezigAsync();
        await using var cmd = new MySqlCommand(
            "SELECT r.`id_supplier`, COALESCE(r.`name`, ''), COALESCE(r.`Afkorting`, ''), COALESCE(r.`Werkmaatschappij`, 0) <> 0, " +
            "       COALESCE(r.`MDEBV`, 0) <> 0, " + (opg ? "r.`Opgeheven`" : "NULL") + ", COALESCE(r.`KvK`, ''), COALESCE(r.`VAT number`, ''), " +
            "       (SELECT COUNT(*) FROM `Werknemers` w WHERE w.`WerkzaamBij` = r.`id_supplier` AND COALESCE(w.`Uit dienst`, 0) = 0), " +
            "       (SELECT COUNT(*) FROM `Bankrekeningen` b WHERE b.`BVid` = r.`id_supplier`), " +
            "       (SELECT MAX(f.`Date invoice`) FROM `Invoices` f WHERE f.`Werkmaatschappij` = r.`id_supplier`), " +
            "       (SELECT COUNT(*) FROM `Invoices` f WHERE f.`Werkmaatschappij` = r.`id_supplier`) " +
            "  FROM `Relations` r " +
            " WHERE COALESCE(r.`Werkmaatschappij`, 0) <> 0 OR COALESCE(r.`MDEBV`, 0) <> 0 " +
            (aandelen ? " OR r.`id_supplier` IN (SELECT `BvID` FROM `BvAandeelhouders`) OR r.`id_supplier` IN (SELECT `AandeelhouderID` FROM `BvAandeelhouders`) " : "") +
            " ORDER BY r.`name`", cn);
        var lijst = new List<EigenBv>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new EigenBv
            {
                Id = r.GetInt32(0), Naam = S(r, 1), Afkorting = S(r, 2), Werkmaatschappij = Convert.ToInt64(r.GetValue(3)) != 0,
                MdeGroep = Convert.ToInt64(r.GetValue(4)) != 0, Opgeheven = D(r, 5), KvK = S(r, 6), Btw = S(r, 7),
                Werknemers = Convert.ToInt32(r.GetValue(8)), Rekeningen = Convert.ToInt32(r.GetValue(9)),
                LaatsteFactuur = r.IsDBNull(10) ? null : Convert.ToDateTime(r.GetValue(10)), Facturen = Convert.ToInt32(r.GetValue(11)),
            });
        return lijst;
    }

    public async Task<List<BvAandeel>> AandelenAsync()
    {
        var lijst = new List<BvAandeel>();
        if (!await TabelAanwezigAsync()) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `AandeelID`, `BvID`, `AandeelhouderID`, `Percentage`, `Vanaf`, `Tot`, COALESCE(`Opmerking`, '') " +
            "  FROM `BvAandeelhouders` ORDER BY `BvID`, `Percentage` DESC, `AandeelID`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new BvAandeel
            {
                Id = r.GetInt32(0), BvId = r.GetInt32(1), AandeelhouderId = r.GetInt32(2), Percentage = Convert.ToDecimal(r.GetValue(3)),
                Vanaf = D(r, 4), Tot = D(r, 5), Opmerking = S(r, 6),
            });
        return lijst;
    }

    public async Task<int> BewaarAandeelAsync(BvAandeel a, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(a.Id <= 0
            ? "INSERT INTO `BvAandeelhouders` (`BvID`, `AandeelhouderID`, `Percentage`, `Vanaf`, `Tot`, `Opmerking`, `GewijzigdOp`, `GewijzigdDoor`) " +
              "VALUES (@bv, @houder, @p, @vanaf, @tot, @opm, NOW(), @door); SELECT LAST_INSERT_ID();"
            : "UPDATE `BvAandeelhouders` SET `BvID` = @bv, `AandeelhouderID` = @houder, `Percentage` = @p, `Vanaf` = @vanaf, `Tot` = @tot, " +
              "       `Opmerking` = @opm, `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door WHERE `AandeelID` = @id; SELECT @id;", cn);
        cmd.Parameters.AddWithValue("@bv", a.BvId);
        cmd.Parameters.AddWithValue("@houder", a.AandeelhouderId);
        cmd.Parameters.AddWithValue("@p", a.Percentage);
        cmd.Parameters.AddWithValue("@vanaf", a.Vanaf is DateOnly v ? v.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@tot", a.Tot is DateOnly t ? t.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@opm", a.Opmerking.Length > 300 ? a.Opmerking[..300] : a.Opmerking);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", a.Id);
        a.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return a.Id;
    }

    public async Task WisAandeelAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `BvAandeelhouders` WHERE `AandeelID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetOpgehevenAsync(int bvId, DateOnly? datum)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `Relations` SET `Opgeheven` = @d WHERE `id_supplier` = @id", cn);
        cmd.Parameters.AddWithValue("@d", datum is DateOnly d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@id", bvId);
        await cmd.ExecuteNonQueryAsync();
    }
}
