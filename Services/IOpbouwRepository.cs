using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Opbouwvolgorde: stappen per producttype en de afvinkstatus per exemplaar (ProductOpbouw_MariaDB.sql).</summary>
public interface IOpbouwRepository
{
    bool Beschikbaar { get; }
    /// <summary>Kolom Soort (opbouw/controle) aanwezig? Zonder die kolom valt alles onder 'opbouw'.</summary>
    Task<bool> HeeftSoortAsync();

    /// <summary>De stappen van een producttype, op volgorde; met <paramref name="productId"/> ook de status van dat exemplaar.</summary>
    Task<List<OpbouwStap>> GetStappenAsync(int productNameId, int? productId = null);
    Task<int> BewaarStapAsync(OpbouwStap stap, int? door);
    Task VerwijderStapAsync(int stapId);
    /// <summary>Volgorde vastleggen: de id's in de gewenste volgorde.</summary>
    Task ZetVolgordeAsync(IReadOnlyList<int> stapIdsOpVolgorde);

    /// <summary>Vinkje zetten of weghalen bij een exemplaar.</summary>
    Task ZetStatusAsync(int productId, int stapId, bool klaar, int? door, string? opmerking = null);

    // ---- fases (ProductFases_MariaDB.sql) ----
    /// <summary>Zijn de fase-tabellen er (ProductFases, ProductFaseStatus, kolom FaseID bij de stappen)?</summary>
    Task<bool> HeeftFasesAsync();
    /// <summary>De fases van een producttype, op volgorde; met <paramref name="productId"/> ook de stand bij dat exemplaar.</summary>
    Task<List<ProductFase>> GetFasesAsync(int productNameId, int? productId = null);
    Task<int> BewaarFaseAsync(ProductFase fase);
    /// <summary>Fase weghalen; de stappen van die fase raken hun fase kwijt, de stand bij exemplaren vervalt.</summary>
    Task VerwijderFaseAsync(int faseId);
    Task ZetFaseVolgordeAsync(IReadOnlyList<int> faseIdsOpVolgorde);
    /// <summary>Fase bij een exemplaar klaar of overgeslagen zetten (FaseStand).</summary>
    Task ZetFaseStandAsync(int productId, int faseId, string stand, int? door, string? opmerking = null);
}

public class NullOpbouwRepository : IOpbouwRepository
{
    public bool Beschikbaar => false;
    public Task<bool> HeeftSoortAsync() => Task.FromResult(false);
    public Task<List<OpbouwStap>> GetStappenAsync(int productNameId, int? productId = null) => Task.FromResult(new List<OpbouwStap>());
    public Task<int> BewaarStapAsync(OpbouwStap stap, int? door) => Task.FromResult(0);
    public Task VerwijderStapAsync(int stapId) => Task.CompletedTask;
    public Task ZetVolgordeAsync(IReadOnlyList<int> stapIdsOpVolgorde) => Task.CompletedTask;
    public Task ZetStatusAsync(int productId, int stapId, bool klaar, int? door, string? opmerking = null) => Task.CompletedTask;
    public Task<bool> HeeftFasesAsync() => Task.FromResult(false);
    public Task<List<ProductFase>> GetFasesAsync(int productNameId, int? productId = null) => Task.FromResult(new List<ProductFase>());
    public Task<int> BewaarFaseAsync(ProductFase fase) => Task.FromResult(0);
    public Task VerwijderFaseAsync(int faseId) => Task.CompletedTask;
    public Task ZetFaseVolgordeAsync(IReadOnlyList<int> faseIdsOpVolgorde) => Task.CompletedTask;
    public Task ZetFaseStandAsync(int productId, int faseId, string stand, int? door, string? opmerking = null) => Task.CompletedTask;
}

public class MySqlOpbouwRepository : IOpbouwRepository
{
    private readonly string _cs;
    public MySqlOpbouwRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    // Kolom Soort aanwezig? (tweede versie van ProductOpbouw_MariaDB.sql) Alleen 'ja' onthouden.
    private bool? _heeftSoort;
    public async Task<bool> HeeftSoortAsync()
    {
        if (_heeftSoort == true) return true;
        try { await using var cn = await OpenAsync(); return await HeeftSoortAsync(cn); }
        catch { return false; }
    }
    private async Task<bool> HeeftSoortAsync(MySqlConnection cn)
    {
        if (_heeftSoort == true) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = 'ProductOpbouwStappen' AND LOWER(column_name) = 'soort'", cn);
        _heeftSoort = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        return _heeftSoort.Value;
    }

    public async Task<List<OpbouwStap>> GetStappenAsync(int productNameId, int? productId = null)
    {
        var lijst = new List<OpbouwStap>();
        await using var cn = await OpenAsync();
        var soort = await HeeftSoortAsync(cn) ? "s.`Soort`" : "'opbouw'";
        var fase = await HeeftFasesAsync(cn) ? "s.`FaseID`" : "NULL";
        await using var cmd = new MySqlCommand(
            "SELECT s.`OpbouwStapID`, s.`ProductNameID`, s.`Volgorde`, s.`Naam`, COALESCE(s.`Omschrijving`, ''), s.`Verplicht`, " +
            "       (SELECT COUNT(*) FROM `FileStore` f WHERE f.`StoreType` = @st AND f.`MasterID` = s.`OpbouwStapID`), " +
            $"       COALESCE(x.`Klaar`, 0), x.`KlaarOp`, COALESCE(w.`NaamVoluit`, ''), COALESCE(x.`Opmerking`, ''), {soort}, {fase} " +
            "  FROM `ProductOpbouwStappen` s " +
            "  LEFT JOIN `ProductOpbouwStatus` x ON x.`OpbouwStapID` = s.`OpbouwStapID` AND x.`ProductID` = @p " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = x.`KlaarDoor` " +
            " WHERE s.`ProductNameID` = @n ORDER BY s.`Volgorde`, s.`OpbouwStapID`", cn);
        cmd.Parameters.AddWithValue("@n", productNameId);
        cmd.Parameters.AddWithValue("@p", (object?)productId ?? -1);
        cmd.Parameters.AddWithValue("@st", OpbouwStore.Stap);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new OpbouwStap
            {
                Id = r.GetInt32(0), ProductNameId = r.GetInt32(1), Volgorde = r.GetInt32(2),
                Naam = r.GetString(3), Omschrijving = r.GetString(4), Verplicht = Convert.ToInt32(r.GetValue(5)) != 0,
                AantalDocumenten = Convert.ToInt32(r.GetValue(6)),
                Klaar = Convert.ToInt32(r.GetValue(7)) != 0,
                KlaarOp = r.IsDBNull(8) ? null : Convert.ToDateTime(r.GetValue(8)),
                KlaarDoor = r.GetString(9), Opmerking = r.GetString(10),
                Soort = r.IsDBNull(11) ? OpbouwSoort.Opbouw : r.GetString(11),
                FaseId = r.IsDBNull(12) ? null : Convert.ToInt32(r.GetValue(12)),
            });
        return lijst;
    }

    public async Task<int> BewaarStapAsync(OpbouwStap s, int? door)
    {
        await using var cn = await OpenAsync();
        var metSoort = await HeeftSoortAsync(cn);
        if (s.Id == 0 && s.Volgorde == 0)
        {
            await using var max = new MySqlCommand(
                "SELECT COALESCE(MAX(`Volgorde`), 0) + 1 FROM `ProductOpbouwStappen` WHERE `ProductNameID` = @n" +
                (metSoort ? " AND `Soort` = @soort" : ""), cn);
            max.Parameters.AddWithValue("@n", s.ProductNameId);
            if (metSoort) max.Parameters.AddWithValue("@soort", s.Soort);
            s.Volgorde = Convert.ToInt32(await max.ExecuteScalarAsync());
        }
        var metFase = await HeeftFasesAsync(cn);
        var velden = "`ProductNameID` = @n, `Volgorde` = @v, `Naam` = @naam, `Omschrijving` = @oms, `Verplicht` = @verpl"
                   + (metSoort ? ", `Soort` = @soort" : "") + (metFase ? ", `FaseID` = @fase" : "");
        var sql = s.Id == 0
            ? $"INSERT INTO `ProductOpbouwStappen` SET {velden}, `AddedBy` = @door, `AddedAt` = NOW()"
            : $"UPDATE `ProductOpbouwStappen` SET {velden} WHERE `OpbouwStapID` = @id";
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@n", s.ProductNameId);
        cmd.Parameters.AddWithValue("@v", s.Volgorde);
        cmd.Parameters.AddWithValue("@naam", s.Naam.Trim());
        cmd.Parameters.AddWithValue("@oms", s.Omschrijving.Trim());
        cmd.Parameters.AddWithValue("@verpl", s.Verplicht ? 1 : 0);
        if (metSoort) cmd.Parameters.AddWithValue("@soort", string.IsNullOrWhiteSpace(s.Soort) ? OpbouwSoort.Opbouw : s.Soort);
        if (metFase) cmd.Parameters.AddWithValue("@fase", (object?)s.FaseId ?? DBNull.Value);
        if (s.Id == 0) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        else cmd.Parameters.AddWithValue("@id", s.Id);
        await cmd.ExecuteNonQueryAsync();
        return s.Id == 0 ? (int)cmd.LastInsertedId : s.Id;
    }

    public async Task VerwijderStapAsync(int stapId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `ProductOpbouwStappen` WHERE `OpbouwStapID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", stapId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetVolgordeAsync(IReadOnlyList<int> ids)
    {
        await using var cn = await OpenAsync();
        for (var i = 0; i < ids.Count; i++)
        {
            await using var cmd = new MySqlCommand("UPDATE `ProductOpbouwStappen` SET `Volgorde` = @v WHERE `OpbouwStapID` = @id", cn);
            cmd.Parameters.AddWithValue("@v", i + 1);
            cmd.Parameters.AddWithValue("@id", ids[i]);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task ZetStatusAsync(int productId, int stapId, bool klaar, int? door, string? opmerking = null)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProductOpbouwStatus` (`ProductID`, `OpbouwStapID`, `Klaar`, `KlaarOp`, `KlaarDoor`, `Opmerking`) " +
            "VALUES (@p, @s, @k, @op, @door, @opm) " +
            "ON DUPLICATE KEY UPDATE `Klaar` = VALUES(`Klaar`), `KlaarOp` = VALUES(`KlaarOp`), `KlaarDoor` = VALUES(`KlaarDoor`), " +
            "  `Opmerking` = COALESCE(VALUES(`Opmerking`), `Opmerking`)", cn);
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.Parameters.AddWithValue("@s", stapId);
        cmd.Parameters.AddWithValue("@k", klaar ? 1 : 0);
        cmd.Parameters.AddWithValue("@op", klaar ? DateTime.Now : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@door", klaar ? (object?)door ?? DBNull.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@opm", (object?)opmerking ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Fases (ProductFases_MariaDB.sql)
    // ============================================================
    private bool _heeftFases;      // alleen 'ja' onthouden: het script mag later gedraaid worden

    public async Task<bool> HeeftFasesAsync()
    {
        if (_heeftFases) return true;
        try { await using var cn = await OpenAsync(); return await HeeftFasesAsync(cn); }
        catch { return false; }
    }

    private async Task<bool> HeeftFasesAsync(MySqlConnection cn)
    {
        if (_heeftFases) return true;
        await using var cmd = new MySqlCommand(
            "SELECT (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'ProductFases') " +
            "     + (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'ProductFaseStatus') " +
            "     + (SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
            "           AND table_name = 'ProductOpbouwStappen' AND LOWER(column_name) = 'faseid')", cn);
        return _heeftFases = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 3;
    }

    public async Task<List<ProductFase>> GetFasesAsync(int productNameId, int? productId = null)
    {
        var lijst = new List<ProductFase>();
        await using var cn = await OpenAsync();
        if (!await HeeftFasesAsync(cn)) return lijst;
        await using var cmd = new MySqlCommand(
            "SELECT f.`FaseID`, f.`ProductNameID`, f.`Volgorde`, f.`Naam`, f.`Omschrijving`, " +
            "       COALESCE(x.`Stand`, ''), x.`Op`, COALESCE(w.`NaamVoluit`, ''), COALESCE(x.`Opmerking`, '') " +
            "  FROM `ProductFases` f " +
            "  LEFT JOIN `ProductFaseStatus` x ON x.`FaseID` = f.`FaseID` AND x.`ProductID` = @p " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = x.`Door` " +
            " WHERE f.`ProductNameID` = @n ORDER BY f.`Volgorde`, f.`FaseID`", cn);
        cmd.Parameters.AddWithValue("@n", productNameId);
        cmd.Parameters.AddWithValue("@p", (object?)productId ?? -1);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ProductFase
            {
                Id = r.GetInt32(0), ProductNameId = r.GetInt32(1), Volgorde = r.GetInt32(2),
                Naam = r.GetString(3), Omschrijving = r.GetString(4),
                Stand = r.GetString(5), Op = r.IsDBNull(6) ? null : Convert.ToDateTime(r.GetValue(6)),
                Door = r.GetString(7).Trim(), Opmerking = r.GetString(8),
            });
        return lijst;
    }

    public async Task<int> BewaarFaseAsync(ProductFase f)
    {
        await using var cn = await OpenAsync();
        if (f.Id == 0 && f.Volgorde == 0)
        {
            await using var max = new MySqlCommand(
                "SELECT COALESCE(MAX(`Volgorde`), 0) + 1 FROM `ProductFases` WHERE `ProductNameID` = @n", cn);
            max.Parameters.AddWithValue("@n", f.ProductNameId);
            f.Volgorde = Convert.ToInt32(await max.ExecuteScalarAsync());
        }
        const string velden = "`ProductNameID` = @n, `Volgorde` = @v, `Naam` = @naam, `Omschrijving` = @oms";
        await using var cmd = new MySqlCommand(f.Id == 0
            ? $"INSERT INTO `ProductFases` SET {velden}"
            : $"UPDATE `ProductFases` SET {velden} WHERE `FaseID` = @id", cn);
        cmd.Parameters.AddWithValue("@n", f.ProductNameId);
        cmd.Parameters.AddWithValue("@v", f.Volgorde);
        cmd.Parameters.AddWithValue("@naam", f.Naam.Trim().Length > 60 ? f.Naam.Trim()[..60] : f.Naam.Trim());
        cmd.Parameters.AddWithValue("@oms", f.Omschrijving.Trim().Length > 250 ? f.Omschrijving.Trim()[..250] : f.Omschrijving.Trim());
        if (f.Id != 0) cmd.Parameters.AddWithValue("@id", f.Id);
        await cmd.ExecuteNonQueryAsync();
        return f.Id == 0 ? (int)cmd.LastInsertedId : f.Id;
    }

    public async Task VerwijderFaseAsync(int faseId)
    {
        await using var cn = await OpenAsync();
        foreach (var sql in new[]
        {
            "UPDATE `ProductOpbouwStappen` SET `FaseID` = NULL WHERE `FaseID` = @id",
            "DELETE FROM `ProductFaseStatus` WHERE `FaseID` = @id",
            "DELETE FROM `ProductFases` WHERE `FaseID` = @id",
        })
        {
            await using var cmd = new MySqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@id", faseId);
            await cmd.ExecuteNonQueryAsync();
        }
        // historieregels houden hun FaseID; die wijst dan nergens meer naar en wordt als 'geen fase' getoond
    }

    public async Task ZetFaseVolgordeAsync(IReadOnlyList<int> ids)
    {
        await using var cn = await OpenAsync();
        for (var i = 0; i < ids.Count; i++)
        {
            await using var cmd = new MySqlCommand("UPDATE `ProductFases` SET `Volgorde` = @v WHERE `FaseID` = @id", cn);
            cmd.Parameters.AddWithValue("@v", i + 1);
            cmd.Parameters.AddWithValue("@id", ids[i]);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task ZetFaseStandAsync(int productId, int faseId, string stand, int? door, string? opmerking = null)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProductFaseStatus` (`ProductID`, `FaseID`, `Stand`, `Op`, `Door`, `Opmerking`) " +
            "VALUES (@p, @f, @stand, NOW(), @door, @opm) " +
            "ON DUPLICATE KEY UPDATE `Stand` = VALUES(`Stand`), `Op` = VALUES(`Op`), `Door` = VALUES(`Door`), `Opmerking` = VALUES(`Opmerking`)", cn);
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.Parameters.AddWithValue("@f", faseId);
        cmd.Parameters.AddWithValue("@stand", stand);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@opm", opmerking ?? "");
        await cmd.ExecuteNonQueryAsync();
    }
}
