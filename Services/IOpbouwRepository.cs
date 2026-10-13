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
        await using var cmd = new MySqlCommand(
            "SELECT s.`OpbouwStapID`, s.`ProductNameID`, s.`Volgorde`, s.`Naam`, COALESCE(s.`Omschrijving`, ''), s.`Verplicht`, " +
            "       (SELECT COUNT(*) FROM `FileStore` f WHERE f.`StoreType` = @st AND f.`MasterID` = s.`OpbouwStapID`), " +
            $"       COALESCE(x.`Klaar`, 0), x.`KlaarOp`, COALESCE(w.`NaamVoluit`, ''), COALESCE(x.`Opmerking`, ''), {soort} " +
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
        var velden = "`ProductNameID` = @n, `Volgorde` = @v, `Naam` = @naam, `Omschrijving` = @oms, `Verplicht` = @verpl"
                   + (metSoort ? ", `Soort` = @soort" : "");
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
}
