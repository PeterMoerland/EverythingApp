using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Betalingen klaarzetten (Access-formulier BetalingenKlaarzetten). Access kopieerde de facturen eerst
/// naar een lokale hulptabel BankExport; hier staan ze alleen in het geheugen van het scherm.
/// </summary>
public interface IBetalingRepository
{
    bool Beschikbaar { get; }

    /// <summary>
    /// Goedgekeurde, onbetaalde inkomende facturen van een MDE-BV, nog niet in een SEPA-bestand, van
    /// leveranciers zonder automatische incasso (Access-query BetalingenKlaarzetten).
    /// </summary>
    Task<List<BetaalRegel>> TeBetalenAsync();

    /// <summary>De BV's als betaler: naam, eigen IBAN en BIC uit Relations.</summary>
    Task<List<BetaalBv>> BvsAsync(IEnumerable<int> ids);

    /// <summary>IBAN of BIC van een leverancier bijwerken in Relations (null = niet wijzigen).</summary>
    Task ZetBankgegevensAsync(int relatieId, string? iban, string? bic);

    /// <summary>
    /// Facturen op "SEPA verzonden" zetten, met wie en wanneer, en een regel in de opmerking.
    /// Facturen die intussen al verzonden waren worden overgeslagen. Geeft het aantal bijgewerkte terug.
    /// </summary>
    Task<int> MarkeerSepaAsync(IEnumerable<int> factuurIds, int? doorWerknemerId);
}

public class GeenBetalingRepository : IBetalingRepository
{
    public bool Beschikbaar => false;
    public Task<List<BetaalRegel>> TeBetalenAsync() => Task.FromResult(new List<BetaalRegel>());
    public Task<List<BetaalBv>> BvsAsync(IEnumerable<int> ids) => Task.FromResult(new List<BetaalBv>());
    public Task ZetBankgegevensAsync(int relatieId, string? iban, string? bic) => Task.CompletedTask;
    public Task<int> MarkeerSepaAsync(IEnumerable<int> factuurIds, int? doorWerknemerId) => Task.FromResult(0);
}

public class MySqlBetalingRepository : IBetalingRepository
{
    private readonly string _cs;

    public MySqlBetalingRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<List<BetaalRegel>> TeBetalenAsync()
    {
        var lijst = new List<BetaalRegel>();
        await using var cn = await OpenAsync();
        // Access-booleans staan als -1 of 1: altijd op <> 0 testen.
        await using var cmd = new MySqlCommand(
            "SELECT f.`FacturenInkomend-ID`, f.`Bedrijf`, COALESCE(r.`name`, ''), COALESCE(f.`FactuurNummer`, ''), " +
            "       f.`DatumFactuur`, COALESCE(f.`Betalingstermijn_dagen`, 0), " +
            "       COALESCE(f.`BedragExBTW`, 0), COALESCE(f.`BTW`, 0), COALESCE(f.`BetalingOpHold`, 0) <> 0, " +
            "       f.`Admin`, COALESCE(b.`Afkorting`, ''), COALESCE(r.`IBAN`, ''), COALESCE(r.`BIC`, '') " +
            "  FROM `FacturenInkomend` f " +
            "  JOIN `Relations` r ON r.`id_supplier` = f.`Bedrijf` " +
            "  JOIN `Relations` b ON b.`id_supplier` = f.`Admin` " +
            " WHERE COALESCE(f.`Betaald`, 0) = 0 AND COALESCE(b.`MDEBV`, 0) <> 0 " +
            "   AND COALESCE(f.`Goedgekeurd`, 0) <> 0 AND COALESCE(f.`SEPAverzonden`, 0) = 0 " +
            "   AND COALESCE(r.`AutomatischIncasso`, 0) = 0", cn);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var nummer = rd.GetString(3).Trim();
            var bedrag = Math.Round(Convert.ToDecimal(rd.GetValue(6)), 2) + Math.Round(Convert.ToDecimal(rd.GetValue(7)), 2);
            var hold = Convert.ToInt64(rd.GetValue(8)) != 0;
            lijst.Add(new BetaalRegel
            {
                FactuurId = rd.GetInt32(0),
                RelatieId = rd.GetInt32(1),
                Leverancier = rd.GetString(2),
                Factuurnummer = nummer,
                Factuurdatum = rd.IsDBNull(4) ? null : rd.GetDateTime(4),
                Betalingstermijn = Convert.ToInt32(rd.GetValue(5)),
                Bedrag = bedrag,
                OnHold = hold,
                BvId = rd.GetInt32(9),
                Bv = rd.GetString(10),
                Iban = rd.GetString(11).Replace(" ", "").ToUpperInvariant(),
                Bic = rd.GetString(12).Trim().ToUpperInvariant(),
                Tenaamstelling = rd.GetString(2),
                Omschrijving = nummer.Length > 0 ? "Invoice: " + nummer : "",
                // zoals Access: alles geselecteerd, behalve on hold en creditnota's
                Selectie = !hold && bedrag > 0,
            });
        }
        return lijst;
    }

    public async Task<List<BetaalBv>> BvsAsync(IEnumerable<int> ids)
    {
        var lijst = new List<BetaalBv>();
        var idLijst = ids.Distinct().ToList();
        if (idLijst.Count == 0) return lijst;

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `id_supplier`, COALESCE(`name`, ''), COALESCE(`Afkorting`, ''), COALESCE(`IBAN`, ''), COALESCE(`BIC`, '') " +
            $"  FROM `Relations` WHERE `id_supplier` IN ({string.Join(",", idLijst.Select((_, i) => "@b" + i))})", cn);
        for (var i = 0; i < idLijst.Count; i++) cmd.Parameters.AddWithValue("@b" + i, idLijst[i]);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lijst.Add(new BetaalBv(rd.GetInt32(0), rd.GetString(1), rd.GetString(2),
                                   rd.GetString(3).Replace(" ", "").ToUpperInvariant(), rd.GetString(4).Trim().ToUpperInvariant()));
        return lijst;
    }

    public async Task ZetBankgegevensAsync(int relatieId, string? iban, string? bic)
    {
        var delen = new List<string>();
        if (iban is not null) delen.Add("`IBAN` = @iban");
        if (bic is not null) delen.Add("`BIC` = @bic");
        if (delen.Count == 0) return;

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"UPDATE `Relations` SET {string.Join(", ", delen)} WHERE `id_supplier` = @id", cn);
        if (iban is not null) cmd.Parameters.AddWithValue("@iban", iban.Replace(" ", "").ToUpperInvariant());
        if (bic is not null) cmd.Parameters.AddWithValue("@bic", bic.Replace(" ", "").ToUpperInvariant());
        cmd.Parameters.AddWithValue("@id", relatieId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> MarkeerSepaAsync(IEnumerable<int> factuurIds, int? doorWerknemerId)
    {
        var ids = factuurIds.Distinct().ToList();
        if (ids.Count == 0) return 0;

        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        // Opmerking is varchar(200): de regel achteraan toevoegen zoals Access; past hij er niet meer
        // bij, dan blijft de opmerking zoals hij is (SEPAop en SEPADoor leggen het toch vast).
        var regel = $" (SEPA aangemaakt op {DateTime.Now:d-M-yyyy HH:mm:ss})";
        await using var cmd = new MySqlCommand(
            "UPDATE `FacturenInkomend` " +
            "   SET `SEPAverzonden` = -1, `SEPADoor` = @door, `SEPAop` = CURDATE(), " +
            "       `Opmerking` = CASE WHEN `Opmerking` IS NULL OR `Opmerking` = '' THEN @regel " +
            "                          WHEN CHAR_LENGTH(`Opmerking`) + 2 + CHAR_LENGTH(@regel) <= 200 " +
            "                               THEN CONCAT(`Opmerking`, '\\r\\n', @regel) " +
            "                          ELSE `Opmerking` END " +
            $" WHERE `FacturenInkomend-ID` IN ({string.Join(",", ids.Select((_, i) => "@f" + i))}) " +
            "   AND COALESCE(`SEPAverzonden`, 0) = 0", cn, tx);
        cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@regel", regel);
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue("@f" + i, ids[i]);
        var aantal = await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return aantal;
    }
}
