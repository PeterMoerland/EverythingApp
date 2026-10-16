using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een melding voor één werknemer (bel in de bovenbalk), bv. "PO-123 is goedgekeurd: je kunt bestellen".</summary>
public record Melding(int Id, string Soort, int? OrderId, string Tekst, DateTime Aangemaakt);

/// <summary>
/// Meldingen per werknemer (tabel Meldingen, Meldingen_MariaDB.sql). Eerste gebruik: de besteller hoort dat zijn
/// bestelling is goedgekeurd. Wat op goedkeuring wacht staat niet in deze tabel: dat volgt uit de status van de
/// bestellingen zelf (IBestelRepository.TeKeurenAsync). Algemeen opgezet, zodat er later meer soorten bij kunnen.
/// </summary>
public interface IMeldingRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();
    Task<List<Melding>> OngelezenAsync(int werknemerId);
    Task MaakAsync(int werknemerId, string soort, int? orderId, string tekst);
    Task GelezenAsync(int meldingId, int werknemerId);
    Task AllesGelezenAsync(int werknemerId);
}

public class GeenMeldingRepository : IMeldingRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<Melding>> OngelezenAsync(int werknemerId) => Task.FromResult(new List<Melding>());
    public Task MaakAsync(int werknemerId, string soort, int? orderId, string tekst) => Task.CompletedTask;
    public Task GelezenAsync(int meldingId, int werknemerId) => Task.CompletedTask;
    public Task AllesGelezenAsync(int werknemerId) => Task.CompletedTask;
}

public class MySqlMeldingRepository : IMeldingRepository
{
    private readonly string _cs;
    private bool _tabel;
    public MySqlMeldingRepository(string connectionString) => _cs = connectionString;
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
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Meldingen'", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _tabel;
    }

    public async Task<List<Melding>> OngelezenAsync(int werknemerId)
    {
        var lijst = new List<Melding>();
        if (!await TabelAanwezigAsync()) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `MeldingID`, `Soort`, `OrderID`, `Tekst`, `Aangemaakt` FROM `Meldingen` " +
            " WHERE `WerknemerID` = @wn AND `Gelezen` IS NULL ORDER BY `Aangemaakt` DESC LIMIT 50", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Melding(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.GetString(3), r.GetDateTime(4)));
        return lijst;
    }

    public async Task MaakAsync(int werknemerId, string soort, int? orderId, string tekst)
    {
        if (!await TabelAanwezigAsync()) return;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `Meldingen` (`WerknemerID`, `Soort`, `OrderID`, `Tekst`, `Aangemaakt`) VALUES (@wn, @s, @o, @t, NOW())", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        cmd.Parameters.AddWithValue("@s", soort);
        cmd.Parameters.AddWithValue("@o", (object?)orderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", tekst.Length > 250 ? tekst[..250] : tekst);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task GelezenAsync(int meldingId, int werknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Meldingen` SET `Gelezen` = NOW() WHERE `MeldingID` = @id AND `WerknemerID` = @wn AND `Gelezen` IS NULL", cn);
        cmd.Parameters.AddWithValue("@id", meldingId);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AllesGelezenAsync(int werknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Meldingen` SET `Gelezen` = NOW() WHERE `WerknemerID` = @wn AND `Gelezen` IS NULL", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Seintje binnen de server dat er iets veranderde aan bestellingen of meldingen: elke open bel (MeldingBel) haalt dan
/// meteen opnieuw op, in plaats van te wachten op zijn eigen minuut.
/// </summary>
public class MeldingSignaal
{
    public event Action? Gewijzigd;
    public void Geef() => Gewijzigd?.Invoke();
}
