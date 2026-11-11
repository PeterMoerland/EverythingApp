using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Een bankrekening van een relatie (tabel RelatieIbans). Standaard = de rekening die ook in Relations.IBAN/BIC staat
/// (SEPA-betalingen, fraudecontrole bij factuur inlezen, Access). Bic/Omschrijving/Standaard pas na RelatieRekeningen_MariaDB.sql.
/// </summary>
public record RelatieIban(int Id, string Iban, string Bron, DateTime? Op, string Door,
                          string Bic = "", string Omschrijving = "", bool Standaard = false);

/// <summary>
/// Bekende rekeningen van relaties (RelatieIbans_MariaDB.sql, wens gebruiker 11-11-2026). Bij een koppeling van een
/// banktransactie aan een factuur komt de tegenrekening bij de relatie van die factuur. Sinds RelatieRekeningen_MariaDB.sql
/// is het ook de lijst die je in Relaties ▸ Bankrekeningen beheert. Zonder de tabel doet alles niets, zodat koppelen gewoon
/// blijft werken.
/// </summary>
public static class RelatieIbans
{
    private static bool _tabel, _lijst;   // alleen "ja" onthouden: na het script werkt het zonder herstart

    public static async Task<bool> TabelAsync(MySqlConnection cn, MySqlTransaction? tx = null)
    {
        if (_tabel) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'RelatieIbans'", cn, tx);
        return _tabel = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    /// <summary>Is de tabel uitgebreid tot de lijst met BIC en standaardrekening (RelatieRekeningen_MariaDB.sql)?</summary>
    public static async Task<bool> LijstAsync(MySqlConnection cn, MySqlTransaction? tx = null)
    {
        if (_lijst) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'RelatieIbans' " +
            "   AND COLUMN_NAME = 'Standaard'", cn, tx);
        return _lijst = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    /// <summary>Zonder spaties en streepjes, in hoofdletters.</summary>
    public static string Schoon(string? iban) => Bankgegevens.Schoon(iban);

    /// <summary>
    /// Zet de tegenrekening van de transactie bij de relatie van de factuur (leverancier bij inkomend, klant bij uitgaand),
    /// als het een IBAN is, de relatie hem nog niet kent en het niet al Relations.IBAN is. Geeft true als er iets bijkwam.
    /// </summary>
    public static async Task<bool> LeerAsync(MySqlConnection cn, int transactieId, bool verkoop, int factuurId, int? door, string bron)
    {
        if (!await TabelAsync(cn)) return false;
        var factuur = verkoop
            ? "SELECT `Customer-id` FROM `Invoices` WHERE `Invoice-id` = @f"
            : "SELECT `Bedrijf` FROM `FacturenInkomend` WHERE `FacturenInkomend-ID` = @f";
        await using var cmd = new MySqlCommand(
            "INSERT IGNORE INTO `RelatieIbans` (`RelatieID`, `IBAN`, `Bron`, `AangemaaktOp`, `AangemaaktDoor`) " +
            "SELECT p.`rel`, p.`iban`, @bron, NOW(), @door FROM (" +
            $"  SELECT ({factuur}) AS `rel`, UPPER(REPLACE(COALESCE(t.`Tegenrekening`, ''), ' ', '')) AS `iban` " +
            "     FROM `banktransacties` t WHERE t.`BankTransacties_ID` = @t) p " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = p.`rel` " +
            " WHERE COALESCE(p.`rel`, 0) <> 0 AND p.`iban` REGEXP '^[A-Z]{2}[0-9]{2}[A-Z0-9]{8,30}$' " +
            "   AND UPPER(REPLACE(COALESCE(r.`IBAN`, ''), ' ', '')) <> p.`iban`", cn);
        cmd.Parameters.AddWithValue("@t", transactieId);
        cmd.Parameters.AddWithValue("@f", factuurId);
        cmd.Parameters.AddWithValue("@bron", bron);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Hetzelfde voor een reeks koppelingen (na het verwerken van betalingen); een fout stopt de rest niet.</summary>
    public static async Task LeerAllesAsync(MySqlConnection cn, IEnumerable<(int Transactie, int Factuur)> koppelingen, bool verkoop,
                                            int? door, string bron)
    {
        foreach (var (t, f) in koppelingen.Distinct())
        {
            try { await LeerAsync(cn, t, verkoop, f, door, bron); }
            catch { /* de koppeling zelf staat er; de rekening leren is extra */ }
        }
    }

    /// <summary>
    /// Na een wijziging van Relations.IBAN/BIC buiten de lijst om (factuur inlezen vult een lege IBAN): die rekening als
    /// standaard in de lijst zetten. Alleen met de uitgebreide lijst; doet niets als de relatie geen IBAN heeft.
    /// </summary>
    public static async Task SyncVanRelatieAsync(MySqlConnection cn, int relatieId, string bron, int? door)
    {
        if (!await LijstAsync(cn)) return;
        await using (var cmd = new MySqlCommand(
            "INSERT IGNORE INTO `RelatieIbans` (`RelatieID`, `IBAN`, `BIC`, `Standaard`, `Bron`, `AangemaaktOp`, `AangemaaktDoor`) " +
            "SELECT r.`id_supplier`, UPPER(REPLACE(REPLACE(r.`IBAN`, ' ', ''), '-', '')), " +
            "       NULLIF(UPPER(REPLACE(COALESCE(r.`BIC`, ''), ' ', '')), ''), 0, @bron, NOW(), @door " +
            "  FROM `Relations` r WHERE r.`id_supplier` = @r AND COALESCE(TRIM(r.`IBAN`), '') <> ''", cn))
        {
            cmd.Parameters.AddWithValue("@r", relatieId);
            cmd.Parameters.AddWithValue("@bron", bron);
            cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new MySqlCommand(
            "UPDATE `RelatieIbans` ri JOIN `Relations` r ON r.`id_supplier` = ri.`RelatieID` " +
            "   SET ri.`Standaard` = (ri.`IBAN` = UPPER(REPLACE(REPLACE(r.`IBAN`, ' ', ''), '-', ''))), " +
            "       ri.`BIC` = IF(ri.`IBAN` = UPPER(REPLACE(REPLACE(r.`IBAN`, ' ', ''), '-', '')), " +
            "                     COALESCE(ri.`BIC`, NULLIF(UPPER(REPLACE(COALESCE(r.`BIC`, ''), ' ', '')), '')), ri.`BIC`) " +
            " WHERE ri.`RelatieID` = @r AND COALESCE(TRIM(r.`IBAN`), '') <> ''", cn))
        {
            cmd.Parameters.AddWithValue("@r", relatieId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>SQL-voorwaarde: de relatie (kolom <paramref name="relatieKolom"/>) kent de rekening in parameter @iban.</summary>
    public static string KentRekening(string relatieKolom, bool tabel)
        => tabel ? $" OR EXISTS (SELECT 1 FROM `RelatieIbans` ri WHERE ri.`RelatieID` = {relatieKolom} AND ri.`IBAN` = @iban)" : "";
}
