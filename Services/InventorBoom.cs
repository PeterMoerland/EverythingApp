using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Een export uit Inventor: een regel in de hoofdtabel InventorAssemblyExport (sinds 26-11-2026; elke export blijft
/// bewaard). Artikel = code en omschrijving van de hoofdsamenstelling, Samenstelling = de bestandsnaam in Inventor.
/// </summary>
public record InventorExport(int Id, int? ArtikelId, string Artikel, string Samenstelling, string Commentaar, int Regels,
                             string Door, DateTime? Op, string Computer, string Uitgave);

/// <summary>Een regel van de boom. <see cref="Ouder"/> = het volgnummer van de regel erboven (leeg = de samenstelling zelf).</summary>
public record InventorRegel(int Volgnummer, int? Ouder, int Niveau, int Aantal, int? ArtikelId, bool Samenstelling,
                            string Partnummer, string Omschrijving, string Materiaal, bool? InVault, int? VaultVersie,
                            string ArtikelCode, string ArtikelOmschrijving,
                            int Id = 0, DateTime? Gemaakt = null, DateTime? Bijgewerkt = null)
{
    /// <summary>Het artikel komt niet uit de export zelf maar uit de aliaslijst (MDEalias): de naam van het part is een alias van dat artikel.</summary>
    public bool ViaAlias { get; init; }
}

/// <summary>
/// De samenstellingen die uit Inventor zijn geëxporteerd (knop Stuklijst van de Inventor-add-in), om te bekijken in
/// Artikelen ▸ Inventor-boom (wens gebruiker 25-11-2026). Twee tabellen (26-11-2026): InventorAssemblyExport = een
/// regel per export (wie, wanneer, welke samenstelling, commentaar) en InventorAssemblyTree = de boom, met
/// InventorAssemblyExportID als verwijzing. Alleen lezen: artikelsamenstellingen hieruit maken komt later.
/// </summary>
public class InventorBoom
{
    private readonly string? _cs;
    public InventorBoom(IConfiguration config) => _cs = config.GetConnectionString("MDE");

    public bool Beschikbaar => !string.IsNullOrWhiteSpace(_cs) && !_cs!.Contains("REPLACE_");

    private bool _hoofdtabel;

    /// <summary>Staat de hoofdtabel InventorAssemblyExport er (het script van de Inventor-add-in, 26-11-2026)?</summary>
    public async Task<bool> HoofdtabelAanwezigAsync()
    {
        if (_hoofdtabel || !Beschikbaar) return _hoofdtabel;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'InventorAssemblyExport') " +
            "     + (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'InventorAssemblyTree' AND COLUMN_NAME = 'InventorAssemblyExportID')", cn);
        return _hoofdtabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 2;
    }

    /// <summary>De exports uit de hoofdtabel, nieuwste eerst.</summary>
    public async Task<List<InventorExport>> ExportsAsync()
    {
        var lijst = new List<InventorExport>();
        if (!await HoofdtabelAanwezigAsync()) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT e.`InventorAssemblyExportID`, e.`ArtikelID`, TRIM(CONCAT_WS(' ', a.`ArtikelCodeMDE`, a.`OmschrijvingNL`)), COALESCE(e.`Samenstelling`, ''), " +
            "       COALESCE(e.`Commentaar`, ''), COALESCE(e.`AantalRegels`, 0), COALESCE(w.`NaamVoluit`, CONCAT_WS(' ', w.`Voornaam`, w.`Achternaam`), ''), " +
            "       e.`CreatedAt`, COALESCE(e.`Computer`, ''), COALESCE(e.`Uitgave`, '') " +
            "  FROM `InventorAssemblyExport` e " +
            "  LEFT JOIN `Artikelen` a ON a.`ArtikelID` = e.`ArtikelID` " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = e.`CreatedBy` " +
            " ORDER BY e.`InventorAssemblyExportID` DESC LIMIT 500", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new InventorExport(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? "" : r.GetString(2), r.GetString(3), r.GetString(4),
                                         Convert.ToInt32(r.GetValue(5)), r.IsDBNull(6) ? "" : r.GetString(6), r.IsDBNull(7) ? null : r.GetDateTime(7),
                                         r.GetString(8), r.GetString(9)));
        return lijst;
    }

    /// <summary>De boom van één export, in de volgorde van de boom (ouders voor kinderen).</summary>
    public async Task<List<InventorRegel>> RegelsAsync(int exportId)
    {
        var lijst = new List<InventorRegel>();
        if (!Beschikbaar) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            // Een regel zonder ArtikelID waarvan de naam (Partnumber) als alias bij een artikel staat, telt als dat artikel
            // (kolom 16 = via de alias). De export zelf wordt daarvoor niet gewijzigd.
            "SELECT t.`Volgnummer`, t.`ParentVolgnummer`, COALESCE(t.`Level`, 0), COALESCE(t.`Quantity`, 0), COALESCE(t.`ArtikelID`, al.`MDE_ID`), COALESCE(t.`isAssy`, 0), " +
            "       COALESCE(t.`Partnumber`, ''), COALESCE(t.`Description`, ''), COALESCE(t.`Material`, ''), t.`inInVault`, t.`VaultVersion`, " +
            "       COALESCE(a.`ArtikelCodeMDE`, ''), COALESCE(a.`OmschrijvingNL`, ''), t.`InventorAssemblyTreeID`, t.`CreatedAt`, t.`last_updated`, " +
            "       (t.`ArtikelID` IS NULL AND al.`MDE_ID` IS NOT NULL) " +
            "  FROM `InventorAssemblyTree` t " +
            "  LEFT JOIN `MDEalias` al ON t.`ArtikelID` IS NULL AND COALESCE(t.`Partnumber`, '') <> '' AND al.`MDETranslatorID` = " +
            "       (SELECT MAX(m.`MDETranslatorID`) FROM `MDEalias` m WHERE m.`Alias` = t.`Partnumber`) " +
            "  LEFT JOIN `Artikelen` a ON a.`ArtikelID` = COALESCE(t.`ArtikelID`, al.`MDE_ID`) " +
            " WHERE t.`InventorAssemblyExportID` = @e ORDER BY t.`Volgnummer`, t.`InventorAssemblyTreeID`", cn);
        cmd.Parameters.AddWithValue("@e", exportId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new InventorRegel(
                r.IsDBNull(0) ? 0 : r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), Convert.ToInt32(r.GetValue(2)), Convert.ToInt32(r.GetValue(3)),
                r.IsDBNull(4) ? null : r.GetInt32(4), Convert.ToInt32(r.GetValue(5)) != 0, r.GetString(6), r.GetString(7), r.GetString(8),
                r.IsDBNull(9) ? null : Convert.ToInt32(r.GetValue(9)) != 0, r.IsDBNull(10) ? null : r.GetInt32(10), r.GetString(11), r.GetString(12),
                r.GetInt32(13), r.IsDBNull(14) ? null : r.GetDateTime(14), r.IsDBNull(15) ? null : r.GetDateTime(15))
                { ViaAlias = Convert.ToInt32(r.GetValue(16)) != 0 });
        return lijst;
    }

    /// <summary>
    /// De naam van een part uit Inventor als alias bij een artikel zetten (tabel MDEalias, BronToevoeging 'INV', zoals
    /// de oude koppeling in Access). Staat de alias al bij dit artikel, dan gebeurt er niets; staat hij bij een ander
    /// artikel, dan een fout (een alias hoort bij één artikel). Geeft de melding voor de gebruiker.
    /// </summary>
    public async Task<string> AliasToevoegenAsync(int artikelId, string alias, int? door)
    {
        alias = (alias ?? "").Trim();
        if (alias.Length == 0) throw new InvalidOperationException("Deze regel heeft geen naam om als alias te gebruiken.");
        if (alias.Length > 100) throw new InvalidOperationException("De naam is langer dan 100 tekens en past niet in de aliaslijst.");
        if (!Beschikbaar) throw new InvalidOperationException("Er is geen database.");
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using (var bestaat = new MySqlCommand("SELECT COUNT(*) FROM `Artikelen` WHERE `ArtikelID` = @a", cn))
        {
            bestaat.Parameters.AddWithValue("@a", artikelId);
            if (Convert.ToInt32(await bestaat.ExecuteScalarAsync()) == 0) throw new InvalidOperationException($"Artikel {artikelId} bestaat niet.");
        }
        await using (var al = new MySqlCommand("SELECT `MDE_ID` FROM `MDEalias` WHERE `Alias` = @x ORDER BY (`MDE_ID` = @a) DESC LIMIT 1", cn))
        {
            al.Parameters.AddWithValue("@x", alias);
            al.Parameters.AddWithValue("@a", artikelId);
            var gevonden = await al.ExecuteScalarAsync();
            if (gevonden is not null and not DBNull)
            {
                var bij = Convert.ToInt32(gevonden);
                if (bij == artikelId) return $"\"{alias}\" stond al als alias bij dit artikel.";
                throw new InvalidOperationException($"\"{alias}\" staat al als alias bij artikel {bij}. Haal die eerst weg als hij daar niet hoort.");
            }
        }
        await using var cmd = new MySqlCommand(
            "INSERT INTO `MDEalias` (`MDE_ID`, `Alias`, `ToegevoegdDoor`, `ToegevoegdOp`, `BronToevoeging`) VALUES (@a, @x, @d, NOW(), 'INV')", cn);
        cmd.Parameters.AddWithValue("@a", artikelId);
        cmd.Parameters.AddWithValue("@x", alias);
        cmd.Parameters.AddWithValue("@d", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return $"\"{alias}\" is als alias toegevoegd.";
    }
}
