using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een melding voor één werknemer (bel in de bovenbalk), bv. "PO-123 is goedgekeurd: je kunt bestellen".</summary>
public record Melding(int Id, string Soort, int? OrderId, string Tekst, DateTime Aangemaakt)
{
    /// <summary>Wanneer gelezen; null = ongelezen.</summary>
    public DateTime? Gelezen { get; init; }
    /// <summary>Afzender van een bericht van een collega (Soort 'bericht'); null bij meldingen van de app.</summary>
    public int? VanId { get; init; }
    public string Van { get; init; } = "";
}

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

    /// <summary>De berichtenbox: gelezen en ongelezen, nieuwste eerst (gelezen blijven bewaard tot de eigenaar ze wist).</summary>
    Task<List<Melding>> AlleAsync(int werknemerId, int max = 200);
    /// <summary>Weer op ongelezen (of gelezen) zetten; alleen van jezelf.</summary>
    Task ZetGelezenAsync(int meldingId, int werknemerId, bool gelezen);
    /// <summary>Wissen; alleen de eigenaar (WerknemerID) kan zijn eigen berichten wissen.</summary>
    Task WisAsync(int meldingId, int werknemerId);
    /// <summary>Kan de tabel berichten tussen collega's aan (kolom VanWerknemerID, Meldingen_Berichten_MariaDB.sql)?</summary>
    Task<bool> BerichtenMogelijkAsync();
    /// <summary>Een bericht aan een collega (Soort 'bericht'); OrderId = het bericht waarop dit een antwoord is.</summary>
    Task StuurBerichtAsync(int naar, int van, string tekst, int? antwoordOp = null);
}

public class GeenMeldingRepository : IMeldingRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<Melding>> OngelezenAsync(int werknemerId) => Task.FromResult(new List<Melding>());
    public Task MaakAsync(int werknemerId, string soort, int? orderId, string tekst) => Task.CompletedTask;
    public Task GelezenAsync(int meldingId, int werknemerId) => Task.CompletedTask;
    public Task AllesGelezenAsync(int werknemerId) => Task.CompletedTask;
    public Task<List<Melding>> AlleAsync(int werknemerId, int max = 200) => Task.FromResult(new List<Melding>());
    public Task ZetGelezenAsync(int meldingId, int werknemerId, bool gelezen) => Task.CompletedTask;
    public Task WisAsync(int meldingId, int werknemerId) => Task.CompletedTask;
    public Task<bool> BerichtenMogelijkAsync() => Task.FromResult(false);
    public Task StuurBerichtAsync(int naar, int van, string tekst, int? antwoordOp = null) => Task.CompletedTask;
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
        var max = await BerichtenMogelijkAsync() ? 1000 : 250;
        cmd.Parameters.AddWithValue("@t", tekst.Length > max ? tekst[..max] : tekst);
        await cmd.ExecuteNonQueryAsync();
    }

    private bool? _berichten;

    public async Task<bool> BerichtenMogelijkAsync()
    {
        if (_berichten == true) return true;
        if (!await TabelAanwezigAsync()) return false;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "   AND TABLE_NAME = 'Meldingen' AND COLUMN_NAME = 'VanWerknemerID'", cn);
            _berichten = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { _berichten = false; }
        return _berichten.Value;
    }

    public async Task<List<Melding>> AlleAsync(int werknemerId, int max = 200)
    {
        var lijst = new List<Melding>();
        if (!await TabelAanwezigAsync()) return lijst;
        var metVan = await BerichtenMogelijkAsync();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT m.`MeldingID`, m.`Soort`, m.`OrderID`, m.`Tekst`, m.`Aangemaakt`, m.`Gelezen`, " +
            (metVan ? "m.`VanWerknemerID`, COALESCE(w.`NaamVoluit`, '') FROM `Meldingen` m " +
                      "LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = m.`VanWerknemerID` "
                    : "NULL, '' FROM `Meldingen` m ") +
            $" WHERE m.`WerknemerID` = @wn ORDER BY m.`Aangemaakt` DESC, m.`MeldingID` DESC LIMIT {Math.Clamp(max, 1, 1000)}", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Melding(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.GetString(3), r.GetDateTime(4))
            {
                Gelezen = r.IsDBNull(5) ? null : r.GetDateTime(5),
                VanId = r.IsDBNull(6) ? null : Convert.ToInt32(r.GetValue(6)),
                Van = r.IsDBNull(7) ? "" : r.GetString(7),
            });
        return lijst;
    }

    public async Task ZetGelezenAsync(int meldingId, int werknemerId, bool gelezen)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Meldingen` SET `Gelezen` = " + (gelezen ? "COALESCE(`Gelezen`, NOW())" : "NULL") +
            " WHERE `MeldingID` = @id AND `WerknemerID` = @wn", cn);
        cmd.Parameters.AddWithValue("@id", meldingId);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task WisAsync(int meldingId, int werknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `Meldingen` WHERE `MeldingID` = @id AND `WerknemerID` = @wn", cn);
        cmd.Parameters.AddWithValue("@id", meldingId);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task StuurBerichtAsync(int naar, int van, string tekst, int? antwoordOp = null)
    {
        if (!await BerichtenMogelijkAsync()) throw new InvalidOperationException("Voer eerst Meldingen_Berichten_MariaDB.sql uit.");
        tekst = tekst.Trim();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `Meldingen` (`WerknemerID`, `Soort`, `OrderID`, `Tekst`, `Aangemaakt`, `VanWerknemerID`) " +
            "VALUES (@naar, 'bericht', @op, @t, NOW(), @van)", cn);
        cmd.Parameters.AddWithValue("@naar", naar);
        cmd.Parameters.AddWithValue("@op", (object?)antwoordOp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", tekst.Length > 1000 ? tekst[..1000] : tekst);
        cmd.Parameters.AddWithValue("@van", van);
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
