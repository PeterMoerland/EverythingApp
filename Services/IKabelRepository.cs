using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een opgeslagen kabel in de keuzelijst van de kabelconfigurator (tabel Kabelconfiguraties).</summary>
public record KabelOverzicht(int Id, string Naam, string LeveranciersNummer, DateTime? GewijzigdOp);

/// <summary>
/// Opslag van kabelconfiguraties (Tools ▸ Kabelconfigurator). De hele spec staat als JSON in de kolom Spec
/// (Models/Kabel.cs, KabelSpec.NaarJson); naam en LDZY-nummer staan er los naast om te zoeken en te tonen.
/// </summary>
public interface IKabelRepository
{
    bool Beschikbaar { get; }
    /// <summary>Bestaat de tabel Kabelconfiguraties al (Kabelconfiguraties_MariaDB.sql)?</summary>
    Task<bool> TabelAanwezigAsync();
    Task<List<KabelOverzicht>> LijstAsync();
    Task<KabelSpec?> GetAsync(int id);
    /// <summary>Nieuw (Id 0) of wijzigen; geeft het Id.</summary>
    Task<int> BewaarAsync(KabelSpec k, int? door, string doorNaam);
    Task VerwijderAsync(int id);
    /// <summary>De actieve connectortypes uit de tabel KabelConnectoren (KabelConnectoren_MariaDB.sql), op familie en
    /// pinnen. Bestaat de tabel nog niet, dan een lege lijst: de configurator werkt dan met vrije tekst.</summary>
    Task<List<KabelConnector>> ConnectorenAsync();
}

public class GeenKabelRepository : IKabelRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<KabelOverzicht>> LijstAsync() => Task.FromResult(new List<KabelOverzicht>());
    public Task<KabelSpec?> GetAsync(int id) => Task.FromResult<KabelSpec?>(null);
    public Task<int> BewaarAsync(KabelSpec k, int? door, string doorNaam) => Task.FromResult(0);
    public Task VerwijderAsync(int id) => Task.CompletedTask;
    public Task<List<KabelConnector>> ConnectorenAsync() => Task.FromResult(new List<KabelConnector>());
}

public class MySqlKabelRepository : IKabelRepository
{
    private readonly string _cs;
    public MySqlKabelRepository(string cs) => _cs = cs;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<bool> TabelAanwezigAsync()
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Kabelconfiguraties'", cn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    public async Task<List<KabelOverzicht>> LijstAsync()
    {
        var lijst = new List<KabelOverzicht>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `KabelID`, COALESCE(`Naam`, ''), COALESCE(`LeveranciersNummer`, ''), COALESCE(`GewijzigdOp`, `GemaaktOp`) " +
            "  FROM `Kabelconfiguraties` ORDER BY `Naam`, `KabelID`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new KabelOverzicht(r.GetInt32(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetDateTime(3)));
        return lijst;
    }

    public async Task<KabelSpec?> GetAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Spec`, `GemaaktOp`, `GewijzigdOp` FROM `Kabelconfiguraties` WHERE `KabelID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var k = KabelSpec.UitJson(r.IsDBNull(0) ? "" : r.GetString(0));
        if (k is null) return null;
        k.Id = id;
        k.GemaaktOp = r.IsDBNull(1) ? k.GemaaktOp : r.GetDateTime(1);
        k.GewijzigdOp = r.IsDBNull(2) ? k.GewijzigdOp : r.GetDateTime(2);
        return k;
    }

    public async Task<int> BewaarAsync(KabelSpec k, int? door, string doorNaam)
    {
        var nu = DateTime.Now;
        if (k.Id == 0) { k.GemaaktOp = nu; k.GemaaktDoor = doorNaam; }
        k.GewijzigdOp = nu;
        k.GewijzigdDoor = doorNaam;
        const string velden = "`Naam` = @naam, `Leverancier` = @lev, `LeveranciersNummer` = @nr, `Spec` = @spec, " +
                              "`GewijzigdOp` = @nu, `GewijzigdDoor` = @door";
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(k.Id == 0
            ? $"INSERT INTO `Kabelconfiguraties` SET {velden}, `GemaaktOp` = @nu, `GemaaktDoor` = @door"
            : $"UPDATE `Kabelconfiguraties` SET {velden} WHERE `KabelID` = @id", cn);
        var naam = k.Naam.Trim();
        cmd.Parameters.AddWithValue("@naam", naam.Length > 200 ? naam[..200] : naam);
        cmd.Parameters.AddWithValue("@lev", k.Leverancier.Trim());
        cmd.Parameters.AddWithValue("@nr", k.LeveranciersNummer);
        cmd.Parameters.AddWithValue("@spec", k.NaarJson());
        cmd.Parameters.AddWithValue("@nu", nu);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        if (k.Id != 0) cmd.Parameters.AddWithValue("@id", k.Id);
        await cmd.ExecuteNonQueryAsync();
        if (k.Id == 0) k.Id = (int)cmd.LastInsertedId;
        return k.Id;
    }

    public async Task VerwijderAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `Kabelconfiguraties` WHERE `KabelID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<KabelConnector>> ConnectorenAsync()
    {
        var lijst = new List<KabelConnector>();
        await using var cn = await OpenAsync();
        await using (var bestaat = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'KabelConnectoren'", cn))
            if (Convert.ToInt64(await bestaat.ExecuteScalarAsync()) == 0) return lijst;
        await using var cmd = new MySqlCommand(
            "SELECT `KabelConnectorID`, COALESCE(`Familie`, ''), COALESCE(`Pinnen`, 0), COALESCE(`Steek`, ''), " +
            "       COALESCE(`Housing`, ''), COALESCE(`Terminal`, ''), COALESCE(`Opmerking`, '') " +
            "  FROM `KabelConnectoren` WHERE COALESCE(`Actief`, 1) <> 0 " +
            " ORDER BY `Familie`, `Steek`, `Pinnen`, `Housing`", cn);      // per familie eerst de steek (Pico-Lock 1,5 en 2,0), dan de pinnen
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new KabelConnector(r.GetInt32(0), r.GetString(1).Trim(), Convert.ToInt32(r.GetValue(2)),
                                         KabelConnector.LeesSteek(r.GetString(3)), r.GetString(4).Trim(), r.GetString(5).Trim(), r.GetString(6).Trim()));
        return lijst;
    }
}
