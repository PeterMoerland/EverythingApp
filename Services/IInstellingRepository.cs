using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een regel uit GeneralSettingsSpecialOrders (vrije dag, ziek, dokter).</summary>
public class SpeciaalUur
{
    public int Id { get; set; }
    public string Omschrijving { get; set; } = "";
    public int? WerkorderId { get; set; }
    public string Opmerking { get; set; } = "";
}

/// <summary>Een regel uit VoorraadOrders.</summary>
public class VoorraadOrder
{
    public int Id { get; set; }
    public int? OrderId { get; set; }
    public string Omschrijving { get; set; } = "";
    public string Help { get; set; } = "";

    /// <summary>De standaardkeuze, onder meer voor de scanner op de telefoon.</summary>
    public bool Standaard { get; set; }
}

/// <summary>Een regel uit Milestones.</summary>
public class Mijlpaal
{
    public int Id { get; set; }
    public string Naam { get; set; } = "";
    public string Opmerking { get; set; } = "";
    public bool PerWerkorder { get; set; }
}

public interface IInstellingRepository
{
    bool Beschikbaar { get; }

    /// <summary>Alle velden van de ene regel in General_settings, op kolomnaam.</summary>
    Task<Dictionary<string, object?>> GetAsync();
    Task ZetAsync(string kolom, object? waarde);

    /// <summary>Het opslagpercentage van inkoop naar verkoop, bijvoorbeeld 43.</summary>
    Task<decimal> ToeslagAsync();

    Task<List<SpeciaalUur>> GetSpecialeUrenAsync();
    Task<int> BewaarSpeciaalUurAsync(SpeciaalUur regel);
    Task VerwijderSpeciaalUurAsync(int id);

    Task<List<VoorraadOrder>> GetVoorraadOrdersAsync();
    Task<int> BewaarVoorraadOrderAsync(VoorraadOrder regel);
    Task VerwijderVoorraadOrderAsync(int id);

    Task<List<Mijlpaal>> GetMijlpalenAsync();
    Task<int> BewaarMijlpaalAsync(Mijlpaal regel);
    Task VerwijderMijlpaalAsync(int id);
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenInstellingRepository : IInstellingRepository
{
    public bool Beschikbaar => false;
    public Task<Dictionary<string, object?>> GetAsync() => Task.FromResult(new Dictionary<string, object?>());
    public Task ZetAsync(string kolom, object? waarde) => Task.CompletedTask;
    public Task<decimal> ToeslagAsync() => Task.FromResult(0m);
    public Task<List<SpeciaalUur>> GetSpecialeUrenAsync() => Task.FromResult(new List<SpeciaalUur>());
    public Task<int> BewaarSpeciaalUurAsync(SpeciaalUur regel) => Task.FromResult(0);
    public Task VerwijderSpeciaalUurAsync(int id) => Task.CompletedTask;
    public Task<List<VoorraadOrder>> GetVoorraadOrdersAsync() => Task.FromResult(new List<VoorraadOrder>());
    public Task<int> BewaarVoorraadOrderAsync(VoorraadOrder regel) => Task.FromResult(0);
    public Task VerwijderVoorraadOrderAsync(int id) => Task.CompletedTask;
    public Task<List<Mijlpaal>> GetMijlpalenAsync() => Task.FromResult(new List<Mijlpaal>());
    public Task<int> BewaarMijlpaalAsync(Mijlpaal regel) => Task.FromResult(0);
    public Task VerwijderMijlpaalAsync(int id) => Task.CompletedTask;
}

/// <summary>
/// De algemene instellingen. General_settings heeft één regel; die wordt hier
/// als woordenboek gelezen en per veld bijgewerkt, zodat een extra kolom in de
/// database vanzelf meekomt zonder codewijziging.
/// </summary>
public class MySqlInstellingRepository : IInstellingRepository
{
    private readonly string _cs;
    private int? _settingsId;

    public MySqlInstellingRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<Dictionary<string, object?>> GetAsync()
    {
        var uit = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT * FROM `General_settings` ORDER BY `SettingsID` LIMIT 1", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return uit;

        for (var i = 0; i < r.FieldCount; i++)
            uit[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);

        if (uit.TryGetValue("SettingsID", out var id) && id is not null)
            _settingsId = Convert.ToInt32(id);

        return uit;
    }

    public async Task ZetAsync(string kolom, object? waarde)
    {
        if (_settingsId is null) await GetAsync();
        if (_settingsId is null) throw new InvalidOperationException("Er staat geen regel in General_settings.");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"UPDATE `General_settings` SET `{kolom}` = @w WHERE `SettingsID` = @id", cn);
        cmd.Parameters.AddWithValue("@w", waarde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", _settingsId.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Het percentage dat inkoop naar verkoop brengt: 43 betekent maal 1,43.
    /// </summary>
    public async Task<decimal> ToeslagAsync()
    {
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT `ToeslagInkoopVerkoop` FROM `General_settings` ORDER BY `SettingsID` LIMIT 1", cn);
            var res = await cmd.ExecuteScalarAsync();
            return res is null or DBNull ? 0 : Convert.ToDecimal(res);
        }
        catch { return 0; }
    }

    // ============================================================
    //  Speciale uren
    // ============================================================
    public async Task<List<SpeciaalUur>> GetSpecialeUrenAsync()
    {
        var lijst = new List<SpeciaalUur>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `GeneralSettingsSpecialOrders_id`, COALESCE(`Description`, ''), `Order`, " +
            "       COALESCE(`Remark`, '') FROM `GeneralSettingsSpecialOrders` ORDER BY `Description`", cn);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new SpeciaalUur
            {
                Id = r.GetInt32(0),
                Omschrijving = r.GetString(1),
                WerkorderId = r.IsDBNull(2) ? null : Convert.ToInt32(r.GetValue(2)),
                Opmerking = r.GetString(3),
            });
        return lijst;
    }

    public async Task<int> BewaarSpeciaalUurAsync(SpeciaalUur regel)
    {
        const string velden = "`Description` = @oms, `Order` = @order, `Remark` = @opm";
        var sql = regel.Id == 0
            ? $"INSERT INTO `GeneralSettingsSpecialOrders` SET {velden}"
            : $"UPDATE `GeneralSettingsSpecialOrders` SET {velden} " +
              "WHERE `GeneralSettingsSpecialOrders_id` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@oms", Kort(regel.Omschrijving, 20));
        cmd.Parameters.AddWithValue("@order", (object?)regel.WerkorderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@opm", Kort(regel.Opmerking, 200));
        if (regel.Id != 0) cmd.Parameters.AddWithValue("@id", regel.Id);

        await cmd.ExecuteNonQueryAsync();
        return regel.Id == 0 ? (int)cmd.LastInsertedId : regel.Id;
    }

    public async Task VerwijderSpeciaalUurAsync(int id) => await VerwijderAsync(
        "GeneralSettingsSpecialOrders", "GeneralSettingsSpecialOrders_id", id);

    // ============================================================
    //  Voorraadorders
    // ============================================================
    public async Task<List<VoorraadOrder>> GetVoorraadOrdersAsync()
    {
        var lijst = new List<VoorraadOrder>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `VoorraadOrderID`, `OrderID`, COALESCE(`Omschrijving`, ''), COALESCE(`Help`, ''), " +
            "       COALESCE(`Default`, 0) " +
            "  FROM `VoorraadOrders` ORDER BY `Omschrijving`", cn);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new VoorraadOrder
            {
                Id = r.GetInt32(0),
                OrderId = r.IsDBNull(1) ? null : Convert.ToInt32(r.GetValue(1)),
                Omschrijving = r.GetString(2),
                Help = r.GetString(3),
                Standaard = !r.IsDBNull(4) && Convert.ToInt64(r.GetValue(4)) != 0,
            });
        return lijst;
    }

    public async Task<int> BewaarVoorraadOrderAsync(VoorraadOrder regel)
    {
        const string velden = "`OrderID` = @order, `Omschrijving` = @oms, `Help` = @help, " +
                              "`Default` = @standaard";
        var sql = regel.Id == 0
            ? $"INSERT INTO `VoorraadOrders` SET {velden}"
            : $"UPDATE `VoorraadOrders` SET {velden} WHERE `VoorraadOrderID` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@order", (object?)regel.OrderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@oms", Kort(regel.Omschrijving, 45));
        cmd.Parameters.AddWithValue("@help", Kort(regel.Help, 200));
        cmd.Parameters.AddWithValue("@standaard", regel.Standaard ? 1 : 0);
        if (regel.Id != 0) cmd.Parameters.AddWithValue("@id", regel.Id);

        await cmd.ExecuteNonQueryAsync();
        return regel.Id == 0 ? (int)cmd.LastInsertedId : regel.Id;
    }

    public async Task VerwijderVoorraadOrderAsync(int id)
        => await VerwijderAsync("VoorraadOrders", "VoorraadOrderID", id);

    // ============================================================
    //  Mijlpalen
    // ============================================================
    public async Task<List<Mijlpaal>> GetMijlpalenAsync()
    {
        var lijst = new List<Mijlpaal>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `MilestonesID`, COALESCE(`Milestone`, ''), COALESCE(`Opmerking`, ''), " +
            "       COALESCE(`WorkorderSpecific`, 0) FROM `Milestones` ORDER BY `MilestonesID`", cn);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Mijlpaal
            {
                Id = r.GetInt32(0),
                Naam = r.GetString(1),
                Opmerking = r.GetString(2),
                PerWerkorder = !r.IsDBNull(3) && Convert.ToInt64(r.GetValue(3)) != 0,
            });
        return lijst;
    }

    public async Task<int> BewaarMijlpaalAsync(Mijlpaal regel)
    {
        const string velden = "`Milestone` = @naam, `Opmerking` = @opm, `WorkorderSpecific` = @wo";
        var sql = regel.Id == 0
            ? $"INSERT INTO `Milestones` SET {velden}"
            : $"UPDATE `Milestones` SET {velden} WHERE `MilestonesID` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@naam", Kort(regel.Naam, 45));
        cmd.Parameters.AddWithValue("@opm", Kort(regel.Opmerking, 250));
        cmd.Parameters.AddWithValue("@wo", regel.PerWerkorder ? 1 : 0);
        if (regel.Id != 0) cmd.Parameters.AddWithValue("@id", regel.Id);

        await cmd.ExecuteNonQueryAsync();
        return regel.Id == 0 ? (int)cmd.LastInsertedId : regel.Id;
    }

    public async Task VerwijderMijlpaalAsync(int id) => await VerwijderAsync("Milestones", "MilestonesID", id);

    // ---- hulpjes ----
    private async Task VerwijderAsync(string tabel, string sleutel, int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand($"DELETE FROM `{tabel}` WHERE `{sleutel}` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string Kort(string? tekst, int lengte)
        => (tekst ?? "").Length > lengte ? tekst![..lengte] : tekst ?? "";
}
