using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Eén budgetregel van een project (tabel BudgetProject, uit Access: Projecten ▸ tab Budget).</summary>
public class BudgetRegel
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Omschrijving { get; set; } = "";
    public decimal Bedrag { get; set; }
    /// <summary>PO_Inkomend.PO_inkomendID (in Access: POklant).</summary>
    public int? PoId { get; set; }
    public string PoNummer { get; set; } = "";
    public decimal? PoBedrag { get; set; }
    /// <summary>De PDF van de PO in de filestore (StoreType 20, MasterID = PO), als die er is.</summary>
    public int? PoPdfId { get; set; }
    public string Opmerking { get; set; } = "";
    public DateTime? FactuurAangemaakt { get; set; }
    public string FactuurNummer { get; set; } = "";

    /// <summary>Bedrag als deel van de PO; null = geen PO of PO zonder bedrag ("NVT" in Access).</summary>
    public decimal? DeelVanPo => PoBedrag is decimal po && po != 0 ? Bedrag / po : null;
}

/// <summary>Een PO van de klant om aan een budgetregel te hangen.</summary>
public record KlantPo(int Id, string Nummer, string Omschrijving, decimal? Bedrag, DateTime? Datum);

/// <summary>Een werkorder met zijn eigen budget (Workorders.Budget, Access: tab Budget verdeling).</summary>
public class WerkorderBudget
{
    public int WerkbonId { get; set; }
    public string Nummer { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public int? Doorbelasten { get; set; }
    public decimal Budget { get; set; }
    public bool Gesloten { get; set; }
}

public interface IProjectBudgetRepository
{
    bool Beschikbaar { get; }
    Task<List<BudgetRegel>> BudgetRegelsAsync(int projectId);
    /// <summary>Nieuw (Id 0) of bijwerken; geeft het Id terug.</summary>
    Task<int> BewaarBudgetRegelAsync(BudgetRegel r);
    Task VerwijderBudgetRegelAsync(int id);
    /// <summary>PO's van de klant (PO_Inkomend.Customer-id), nieuwste eerst; zonder klant: leeg.</summary>
    Task<List<KlantPo>> PosVanKlantAsync(int? klantId);
    Task<List<WerkorderBudget>> WerkorderBudgettenAsync(int projectId);
    Task ZetWerkorderBudgetAsync(int werkbonId, decimal budget);
}

public class GeenProjectBudgetRepository : IProjectBudgetRepository
{
    public bool Beschikbaar => false;
    public Task<List<BudgetRegel>> BudgetRegelsAsync(int projectId) => Task.FromResult(new List<BudgetRegel>());
    public Task<int> BewaarBudgetRegelAsync(BudgetRegel r) => throw new InvalidOperationException("Er is geen database.");
    public Task VerwijderBudgetRegelAsync(int id) => Task.CompletedTask;
    public Task<List<KlantPo>> PosVanKlantAsync(int? klantId) => Task.FromResult(new List<KlantPo>());
    public Task<List<WerkorderBudget>> WerkorderBudgettenAsync(int projectId) => Task.FromResult(new List<WerkorderBudget>());
    public Task ZetWerkorderBudgetAsync(int werkbonId, decimal budget) => Task.CompletedTask;
}

public class MySqlProjectBudgetRepository : IProjectBudgetRepository
{
    private const int StoreTypePoKlant = 20;      // zoals in Access: de PDF van een inkomende PO
    private readonly string _cs;
    public MySqlProjectBudgetRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<List<BudgetRegel>> BudgetRegelsAsync(int projectId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT b.`BudgetProjectID`, b.`ProjectID`, COALESCE(b.`Omschrijving`, ''), COALESCE(b.`Bedrag`, 0), b.`POklant`, " +
            "       COALESCE(p.`PO_Klant`, ''), p.`Bedrag`, " +
            $"      (SELECT MAX(f.`FileStoreID`) FROM `FileStore` f WHERE f.`MasterID` = b.`POklant` AND f.`StoreType` = {StoreTypePoKlant}), " +
            "       COALESCE(b.`Opmerking`, ''), b.`FactuurAangemaakt`, COALESCE(b.`FactuurNummer`, '') " +
            "  FROM `BudgetProject` b LEFT JOIN `PO_Inkomend` p ON p.`PO_inkomendID` = b.`POklant` " +
            " WHERE b.`ProjectID` = @p ORDER BY b.`BudgetProjectID`", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        var lijst = new List<BudgetRegel>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new BudgetRegel
            {
                Id = r.GetInt32(0), ProjectId = r.GetInt32(1), Omschrijving = r.GetString(2), Bedrag = Convert.ToDecimal(r.GetValue(3)),
                PoId = r.IsDBNull(4) || r.GetInt32(4) == 0 ? null : r.GetInt32(4), PoNummer = r.GetString(5),
                PoBedrag = r.IsDBNull(6) ? null : Convert.ToDecimal(r.GetValue(6)),
                PoPdfId = r.IsDBNull(7) ? null : Convert.ToInt32(r.GetValue(7)), Opmerking = r.GetString(8),
                FactuurAangemaakt = r.IsDBNull(9) ? null : r.GetDateTime(9), FactuurNummer = r.GetString(10),
            });
        return lijst;
    }

    public async Task<int> BewaarBudgetRegelAsync(BudgetRegel b)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(b.Id == 0
            ? "INSERT INTO `BudgetProject` (`ProjectID`, `Omschrijving`, `Bedrag`, `POklant`, `Opmerking`) " +
              "VALUES (@p, @oms, @bedrag, @po, @opm); SELECT LAST_INSERT_ID();"
            : "UPDATE `BudgetProject` SET `Omschrijving` = @oms, `Bedrag` = @bedrag, `POklant` = @po, `Opmerking` = @opm " +
              " WHERE `BudgetProjectID` = @id AND `ProjectID` = @p; SELECT @id;", cn);
        cmd.Parameters.AddWithValue("@id", b.Id);
        cmd.Parameters.AddWithValue("@p", b.ProjectId);
        cmd.Parameters.AddWithValue("@oms", Kort(b.Omschrijving, 100));
        cmd.Parameters.AddWithValue("@bedrag", (double)b.Bedrag);
        cmd.Parameters.AddWithValue("@po", (object?)b.PoId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@opm", b.Opmerking.Trim().Length == 0 ? DBNull.Value : Kort(b.Opmerking, 400));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task VerwijderBudgetRegelAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `BudgetProject` WHERE `BudgetProjectID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<KlantPo>> PosVanKlantAsync(int? klantId)
    {
        var lijst = new List<KlantPo>();
        if (klantId is null) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `PO_inkomendID`, COALESCE(`PO_Klant`, ''), COALESCE(`Description`, ''), `Bedrag`, `PO_Date` " +
            "  FROM `PO_Inkomend` WHERE `Customer-id` = @k ORDER BY COALESCE(`PO_Date`, `DatumInvoer`) DESC, `PO_inkomendID` DESC LIMIT 300", cn);
        cmd.Parameters.AddWithValue("@k", klantId.Value);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new KlantPo(r.GetInt32(0), r.GetString(1), r.GetString(2),
                                  r.IsDBNull(3) ? null : Convert.ToDecimal(r.GetValue(3)), r.IsDBNull(4) ? null : r.GetDateTime(4)));
        return lijst;
    }

    public async Task<List<WerkorderBudget>> WerkorderBudgettenAsync(int projectId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT w.`Werkbon-id`, CONCAT(COALESCE(p.`Projectnumber`, ''), '-', LPAD(COALESCE(w.`Werkbonnummer`, 0), 2, '0')), " +
            "       COALESCE(w.`Werkbonomschrijving`, ''), w.`Doorbelasten`, COALESCE(w.`Budget`, 0), COALESCE(w.`WorkOrderStatus`, 1) = 0 " +
            "  FROM `Workorders` w JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            " WHERE w.`Project-id` = @p AND COALESCE(w.`Deleted`, 0) = 0 ORDER BY w.`Werkbonnummer`", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        var lijst = new List<WerkorderBudget>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new WerkorderBudget
            {
                WerkbonId = r.GetInt32(0), Nummer = r.GetString(1), Omschrijving = r.GetString(2),
                Doorbelasten = r.IsDBNull(3) ? null : r.GetInt32(3), Budget = Convert.ToDecimal(r.GetValue(4)),
                Gesloten = Convert.ToInt64(r.GetValue(5)) != 0,
            });
        return lijst;
    }

    public async Task ZetWerkorderBudgetAsync(int werkbonId, decimal budget)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `Workorders` SET `Budget` = @b WHERE `Werkbon-id` = @id", cn);
        cmd.Parameters.AddWithValue("@b", (double)budget);
        cmd.Parameters.AddWithValue("@id", werkbonId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string Kort(string s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
}
