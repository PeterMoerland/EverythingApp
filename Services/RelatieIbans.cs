using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een rekening die bij een relatie bekend is, naast Relations.IBAN (tabel RelatieIbans).</summary>
public record RelatieIban(int Id, string Iban, string Bron, DateTime? Op, string Door);

/// <summary>
/// Bekende rekeningen van relaties (RelatieIbans_MariaDB.sql, wens gebruiker 11-11-2026). Bij een koppeling van een
/// banktransactie aan een factuur komt de tegenrekening bij de relatie van die factuur. Zonder de tabel doet alles
/// niets, zodat koppelen gewoon blijft werken.
/// </summary>
public static class RelatieIbans
{
    private static bool _tabel;   // alleen "ja" onthouden: na het script werkt het zonder herstart

    public static async Task<bool> TabelAsync(MySqlConnection cn, MySqlTransaction? tx = null)
    {
        if (_tabel) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'RelatieIbans'", cn, tx);
        return _tabel = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    /// <summary>Zonder spaties, in hoofdletters.</summary>
    public static string Schoon(string? iban) => (iban ?? "").Replace(" ", "").Trim().ToUpperInvariant();

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

    /// <summary>SQL-voorwaarde: de relatie (kolom <paramref name="relatieKolom"/>) kent de rekening in parameter @iban.</summary>
    public static string KentRekening(string relatieKolom, bool tabel)
        => tabel ? $" OR EXISTS (SELECT 1 FROM `RelatieIbans` ri WHERE ri.`RelatieID` = {relatieKolom} AND ri.`IBAN` = @iban)" : "";
}
