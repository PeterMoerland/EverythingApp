using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een export uit Inventor in InventorAssemblyTree: één per werknemer (een nieuwe export vervangt de vorige).</summary>
public record InventorExport(int WerknemerId, string Door, DateTime? Op, int Regels, string Samenstelling);

/// <summary>Een regel van de boom. <see cref="Ouder"/> = het volgnummer van de regel erboven (leeg = de samenstelling zelf).</summary>
public record InventorRegel(int Volgnummer, int? Ouder, int Niveau, int Aantal, int? ArtikelId, bool Samenstelling,
                            string Partnummer, string Omschrijving, string Materiaal, bool? InVault, int? VaultVersie,
                            string ArtikelCode, string ArtikelOmschrijving);

/// <summary>
/// De samenstellingen die uit Inventor zijn geëxporteerd (tabel InventorAssemblyTree; knop Stuklijst van de
/// Inventor-add-in, voorheen de VBA-macro ExportTREE), om te bekijken in Artikelen ▸ Inventor-boom (wens gebruiker
/// 25-11-2026). Alleen lezen: het maken van artikelsamenstellingen hieruit komt later.
/// </summary>
public class InventorBoom
{
    private readonly string? _cs;
    public InventorBoom(IConfiguration config) => _cs = config.GetConnectionString("MDE");

    public bool Beschikbaar => !string.IsNullOrWhiteSpace(_cs) && !_cs!.Contains("REPLACE_");

    /// <summary>De exports die er staan, nieuwste eerst. Samenstelling = het artikel van de bovenste regel (of zijn partnummer).</summary>
    public async Task<List<InventorExport>> ExportsAsync()
    {
        var lijst = new List<InventorExport>();
        if (!Beschikbaar) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(t.`CreatedBy`, 0), COALESCE(w.`NaamVoluit`, CONCAT_WS(' ', w.`Voornaam`, w.`Achternaam`), ''), MAX(t.`CreatedAt`), COUNT(*), " +
            "       (SELECT COALESCE(NULLIF(TRIM(CONCAT_WS(' ', a.`ArtikelCodeMDE`, a.`OmschrijvingNL`)), ''), NULLIF(x.`Partnumber`, ''), CONCAT('artikel ', x.`ArtikelID`), '') " +
            "          FROM `InventorAssemblyTree` x LEFT JOIN `Artikelen` a ON a.`ArtikelID` = x.`ArtikelID` " +
            "         WHERE COALESCE(x.`CreatedBy`, 0) = COALESCE(t.`CreatedBy`, 0) ORDER BY x.`Volgnummer` LIMIT 1) " +
            "  FROM `InventorAssemblyTree` t LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = t.`CreatedBy` " +
            " GROUP BY COALESCE(t.`CreatedBy`, 0), 2 ORDER BY MAX(t.`CreatedAt`) DESC", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new InventorExport(Convert.ToInt32(r.GetValue(0)), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? null : r.GetDateTime(2),
                                         Convert.ToInt32(r.GetValue(3)), r.IsDBNull(4) ? "" : r.GetString(4)));
        return lijst;
    }

    /// <summary>De regels van de export van één werknemer, in de volgorde van de boom (ouders voor kinderen).</summary>
    public async Task<List<InventorRegel>> RegelsAsync(int werknemerId)
    {
        var lijst = new List<InventorRegel>();
        if (!Beschikbaar) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT t.`Volgnummer`, t.`ParentVolgnummer`, COALESCE(t.`Level`, 0), COALESCE(t.`Quantity`, 0), t.`ArtikelID`, COALESCE(t.`isAssy`, 0), " +
            "       COALESCE(t.`Partnumber`, ''), COALESCE(t.`Description`, ''), COALESCE(t.`Material`, ''), t.`inInVault`, t.`VaultVersion`, " +
            "       COALESCE(a.`ArtikelCodeMDE`, ''), COALESCE(a.`OmschrijvingNL`, '') " +
            "  FROM `InventorAssemblyTree` t LEFT JOIN `Artikelen` a ON a.`ArtikelID` = t.`ArtikelID` " +
            " WHERE COALESCE(t.`CreatedBy`, 0) = @w ORDER BY t.`Volgnummer`, t.`InventorAssemblyTreeID`", cn);
        cmd.Parameters.AddWithValue("@w", werknemerId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new InventorRegel(
                r.IsDBNull(0) ? 0 : r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), Convert.ToInt32(r.GetValue(2)), Convert.ToInt32(r.GetValue(3)),
                r.IsDBNull(4) ? null : r.GetInt32(4), Convert.ToInt32(r.GetValue(5)) != 0, r.GetString(6), r.GetString(7), r.GetString(8),
                r.IsDBNull(9) ? null : Convert.ToInt32(r.GetValue(9)) != 0, r.IsDBNull(10) ? null : r.GetInt32(10), r.GetString(11), r.GetString(12)));
        return lijst;
    }
}
