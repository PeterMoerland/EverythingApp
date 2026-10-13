using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Schrijft en leest bestanden in de bestaande filestore, met exact dezelfde
/// afspraken als de VBA-code:
///
///   map          = (FileStoreID / 1000) + 1, als 6 cijfers   -> '000013'
///   FileNaam     = jjjjmmdduummss + 2-cijferig werknemernummer + '.EXT'
///   FileOnly     = originele bestandsnaam
///   MasterID     = het record waar het bij hoort
///   StoreType    = soort record (301 = werknemer-gebeurtenis)
///
/// De hoofdmap staat in appsettings.json onder Filestore:Pad, zodat de map kan
/// verhuizen zonder codewijziging.
/// </summary>
public class FilestoreService
{
    /// <summary>StoreType voor bestanden die rechtstreeks bij een relatie horen.</summary>
    public const int StoreTypeRelatie = 311;

    private readonly string? _hoofdmap;
    private readonly string? _fotomap;
    private readonly string? _cs;
    private readonly ILogger<FilestoreService> _log;

    public FilestoreService(IConfiguration config, ILogger<FilestoreService> log)
    {
        _hoofdmap = config["Filestore:Pad"];
        _fotomap = config["Filestore:FotoPad"];
        _cs = config.GetConnectionString("MDE");
        _log = log;
    }

    public bool Beschikbaar =>
        !string.IsNullOrWhiteSpace(_hoofdmap) && !_hoofdmap!.Contains("REPLACE_")
        && !string.IsNullOrWhiteSpace(_cs) && !_cs!.Contains("REPLACE_");

    /// <summary>Submap voor een FileStoreID: (id / 1000) + 1, zes cijfers.</summary>
    public static string MapVoor(int fileStoreId) => ((fileStoreId / 1000) + 1).ToString("D6");

    /// <summary>
    /// File.Exists op een netwerkpad zegt "nee" zonder fout als de SMB-sessie net
    /// weg is (bijvoorbeeld vlak na een herstart van de service). Eén korte
    /// herkansing vangt dat op zonder dat een echt ontbrekend bestand traag wordt.
    /// </summary>
    private bool Bestaat(string pad)
    {
        if (File.Exists(pad)) return true;
        if (!pad.StartsWith(@"\\")) return false;
        foreach (var wacht in new[] { 300, 1000, 2500 })
        {
            Thread.Sleep(wacht);
            if (File.Exists(pad))
            {
                _log.LogWarning("Netwerkpad pas bij herkansing gevonden ({Wacht} ms): {Pad}", wacht, pad);
                return true;
            }
        }
        return false;
    }

    /// <summary>Bestandsnaam zoals de VBA die maakt.</summary>
    private static string NieuweBestandsnaam(string origineleNaam, int werknemerId)
    {
        var ext = Path.GetExtension(origineleNaam).TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(ext)) ext = "DAT";
        return DateTime.Now.ToString("yyyyMMddHHmmss")
             + (werknemerId % 100).ToString("D2")
             + "." + ext;
    }

    /// <summary>
    /// Voegt een bestand toe: eerst de regel in FileStore (voor het ID), dan het
    /// bestand op schijf in de juiste submap.
    /// </summary>
    public async Task<int> ToevoegenAsync(Stream inhoud, string origineleNaam,
                                          int storeType, int masterId, int werknemerId)
    {
        if (!Beschikbaar) throw new InvalidOperationException("Filestore is niet ingesteld (Filestore:Pad).");

        var bestandsnaam = NieuweBestandsnaam(origineleNaam, werknemerId);
        var alleenNaam = Path.GetFileName(origineleNaam);

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();

        int id;
        await using (var cmd = new MySqlCommand(
            "INSERT INTO `FileStore` SET `DatumToegevoegd` = @nu, `ToegevoegdDoor` = @door, " +
            "`MasterID` = @master, `StoreType` = @type, `FileNaam` = @naam, " +
            "`FileOnly` = @only, `OrigineleFileNaam` = @orig", cn))
        {
            cmd.Parameters.AddWithValue("@nu", DateTime.Now);
            cmd.Parameters.AddWithValue("@door", werknemerId);
            cmd.Parameters.AddWithValue("@master", masterId);
            cmd.Parameters.AddWithValue("@type", storeType);
            cmd.Parameters.AddWithValue("@naam", bestandsnaam);
            cmd.Parameters.AddWithValue("@only", alleenNaam);
            cmd.Parameters.AddWithValue("@orig", alleenNaam);
            await cmd.ExecuteNonQueryAsync();
            id = (int)cmd.LastInsertedId;
        }

        try
        {
            var map = Path.Combine(_hoofdmap!, MapVoor(id));
            Directory.CreateDirectory(map);
            var doelpad = Path.Combine(map, bestandsnaam);

            // De naam bevat de tijd tot op de seconde. Zetten we meerdere bestanden
            // in dezelfde seconde weg (bijvoorbeeld bij het overnemen van een oude
            // map), dan zou het tweede het eerste overschrijven. Daarom een
            // volgnummer erbij, en de nieuwe naam ook in de tabel bijwerken.
            if (File.Exists(doelpad))
            {
                var kaal = Path.GetFileNameWithoutExtension(bestandsnaam);
                var ext = Path.GetExtension(bestandsnaam);
                for (var nr = 1; nr < 1000 && File.Exists(doelpad); nr++)
                {
                    bestandsnaam = $"{kaal}_{nr}{ext}";
                    doelpad = Path.Combine(map, bestandsnaam);
                }

                await using var bij = new MySqlCommand(
                    "UPDATE `FileStore` SET `FileNaam` = @naam WHERE `FileStoreID` = @id", cn);
                bij.Parameters.AddWithValue("@naam", bestandsnaam);
                bij.Parameters.AddWithValue("@id", id);
                await bij.ExecuteNonQueryAsync();
            }

            await using var doel = File.Create(doelpad);
            await inhoud.CopyToAsync(doel);
        }
        catch (Exception ex)
        {
            // Bestand kon niet worden weggeschreven: regel weer weghalen, anders
            // staat er een verwijzing naar iets dat niet bestaat.
            _log.LogError(ex, "Bestand wegschrijven mislukt, FileStore-regel {Id} wordt teruggedraaid", id);
            await using var del = new MySqlCommand("DELETE FROM `FileStore` WHERE `FileStoreID` = @id", cn);
            del.Parameters.AddWithValue("@id", id);
            await del.ExecuteNonQueryAsync();
            throw;
        }

        return id;
    }

    /// <summary>Verwijdert een bestand: eerst van schijf, dan de regel in FileStore.</summary>
    public async Task VerwijderAsync(int fileStoreId)
    {
        if (!Beschikbaar) throw new InvalidOperationException("Filestore is niet ingesteld.");

        var gevonden = await ZoekAsync(fileStoreId);
        if (gevonden is not null)
        {
            try { File.Delete(gevonden.Value.Pad); }
            catch (Exception ex) { _log.LogWarning(ex, "Bestand {Id} kon niet van schijf worden verwijderd", fileStoreId); }
        }

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `FileStore` WHERE `FileStoreID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", fileStoreId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Pad, weergavenaam en StoreType van een bestand; null als het niet bestaat.</summary>
    public async Task<(string Pad, string Naam, int StoreType)?> ZoekAsync(int fileStoreId)
    {
        if (!Beschikbaar) return null;

        string? opslagnaam = null, weergave = null;
        var storeType = 0;

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using (var cmd = new MySqlCommand(
            "SELECT `FileNaam`, `FileOnly`, `StoreType` FROM `FileStore` WHERE `FileStoreID` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", fileStoreId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            opslagnaam = r.IsDBNull(0) ? null : r.GetString(0);
            weergave = r.IsDBNull(1) ? null : r.GetString(1);
            storeType = r.IsDBNull(2) ? 0 : r.GetInt32(2);
        }
        if (string.IsNullOrWhiteSpace(opslagnaam)) return null;

        var pad = Path.Combine(_hoofdmap!, MapVoor(fileStoreId), opslagnaam);
        if (!Bestaat(pad))
        {
            // Oudere bestanden kunnen in een andere submap staan; buurmappen proberen.
            foreach (var verschuiving in new[] { -1, 1 })
            {
                var alt = Path.Combine(_hoofdmap!,
                    ((fileStoreId / 1000) + 1 + verschuiving).ToString("D6"), opslagnaam);
                if (File.Exists(alt)) { pad = alt; break; }
            }
        }
        if (!File.Exists(pad)) return null;

        return (pad, string.IsNullOrWhiteSpace(weergave) ? opslagnaam : weergave, storeType);
    }

    /// <summary>
    /// Zoekt een bestand waarvan alleen een (mogelijk verouderd) pad in de database staat,
    /// bijvoorbeeld de pasfoto die naar Z: verwijst. Probeert op volgorde:
    ///   1. het pad zoals het er staat
    ///   2. dezelfde bestandsnaam in Filestore:FotoPad
    ///   3. dezelfde bestandsnaam in Filestore:Pad
    /// Zo werkt het zowel met het oude pad als na verhuizen van de bestanden.
    /// </summary>
    public string? ZoekOpPad(string? opgeslagen)
    {
        if (string.IsNullOrWhiteSpace(opgeslagen)) return null;
        var waarde = opgeslagen.Trim().Trim('"');

        try
        {
            if (File.Exists(waarde)) return waarde;

            var naam = Path.GetFileName(waarde.Replace('/', '\\'));
            if (string.IsNullOrWhiteSpace(naam)) return null;

            foreach (var map in new[] { _fotomap, _hoofdmap })
            {
                if (string.IsNullOrWhiteSpace(map)) continue;
                var kandidaat = Path.Combine(map!, naam);
                if (File.Exists(kandidaat)) return kandidaat;
            }
        }
        catch { /* ongeldig pad */ }
        return null;
    }

    // ------------------------------------------------------------------
    //  Beheer ▸ Filestore: zoeken en controleren (de FilestoreViewer uit Access)
    // ------------------------------------------------------------------

    /// <summary>Eén regel uit FileStore, met wat de viewer wil tonen.</summary>
    public record Regel(int Id, DateTime? Toegevoegd, string OrigineleNaam, string OpslagNaam,
                        int StoreType, string StoreTypeNaam, int MasterId, int? DoorId, string Door)
    {
        public string Extensie => Path.GetExtension(OrigineleNaam.Length > 0 ? OrigineleNaam : OpslagNaam).TrimStart('.').ToUpperInvariant();
    }

    /// <summary>De soorten uit de tabel filestoretype (id + omschrijving), plus hoeveel bestanden erbij horen.</summary>
    public async Task<List<(int Id, string Naam, int Aantal)>> StoreTypesAsync()
    {
        var lijst = new List<(int, string, int)>();
        if (string.IsNullOrWhiteSpace(_cs)) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();

        // Welke tabel? Via de foreign key van FileStore.StoreType, anders op naam zoeken.
        // Is er geen opzoektabel, dan tonen we alleen de nummers met hun aantal.
        string? tabel = null, idKolom = null;
        await using (var fk = new MySqlCommand(
            "SELECT referenced_table_name, referenced_column_name FROM information_schema.key_column_usage " +
            "WHERE table_schema = DATABASE() AND table_name = 'FileStore' AND column_name = 'StoreType' " +
            "  AND referenced_table_name IS NOT NULL LIMIT 1", cn))
        await using (var r0 = await fk.ExecuteReaderAsync())
            if (await r0.ReadAsync()) { tabel = r0.GetString(0); idKolom = r0.GetString(1); }

        if (tabel is null)
        {
            await using var zoek = new MySqlCommand(
                "SELECT table_name FROM information_schema.tables " +
                "WHERE table_schema = DATABASE() AND LOWER(table_name) LIKE '%storetype%' LIMIT 1", cn);
            tabel = (await zoek.ExecuteScalarAsync())?.ToString();
        }

        if (tabel is null)
        {
            await using var los = new MySqlCommand(
                "SELECT `StoreType`, COUNT(*) FROM `FileStore` GROUP BY `StoreType` ORDER BY `StoreType`", cn);
            await using var r1 = await los.ExecuteReaderAsync();
            while (await r1.ReadAsync())
                lijst.Add((Convert.ToInt32(r1.GetValue(0)), "", Convert.ToInt32(r1.GetValue(1))));
            return lijst;
        }

        // Id-kolom en omschrijvingskolom heten niet overal hetzelfde: opzoeken.
        string? omsKolom = null;
        await using (var kol = new MySqlCommand(
            "SELECT column_name, data_type FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = @t ORDER BY ordinal_position", cn))
        {
            kol.Parameters.AddWithValue("@t", tabel);
            await using var r2 = await kol.ExecuteReaderAsync();
            while (await r2.ReadAsync())
            {
                var naam = r2.GetString(0);
                var type = r2.GetString(1).ToLowerInvariant();
                if (idKolom is null && (type.Contains("int") || naam.EndsWith("ID", StringComparison.OrdinalIgnoreCase))) idKolom = naam;
                if (omsKolom is null && (type is "varchar" or "char" or "text" or "tinytext")) omsKolom = naam;
            }
        }
        if (idKolom is null) return lijst;

        var oms = omsKolom is null ? "''" : $"t.`{omsKolom}`";
        await using var cmd = new MySqlCommand(
            $"SELECT t.`{idKolom}`, {oms}, " +
            $"       (SELECT COUNT(*) FROM `FileStore` f WHERE f.`StoreType` = t.`{idKolom}`) " +
            $"  FROM `{tabel}` t ORDER BY t.`{idKolom}`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add((Convert.ToInt32(r.GetValue(0)), r.IsDBNull(1) ? "" : r.GetValue(1).ToString() ?? "", Convert.ToInt32(r.GetValue(2))));
        return lijst;
    }

    /// <summary>Werknemers die ooit iets hebben toegevoegd (voor het filter).</summary>
    public async Task<List<(int Id, string Naam)>> ToevoegersAsync()
    {
        var lijst = new List<(int, string)>();
        if (string.IsNullOrWhiteSpace(_cs)) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT DISTINCT w.`Werknemer-id`, COALESCE(w.`NaamVoluit`, CONCAT('#', w.`Werknemer-id`)) " +
            "  FROM `FileStore` f JOIN `Werknemers` w ON w.`Werknemer-id` = f.`ToegevoegdDoor` ORDER BY 2", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add((r.GetInt32(0), r.GetString(1)));
        return lijst;
    }

    /// <summary>Zoeken in FileStore. Nieuwste eerst, maximaal <paramref name="max"/> regels.</summary>
    public async Task<(List<Regel> Regels, int Totaal)> ZoekRegelsAsync(
        int? storeType, int? doorId, string? extensie, string? zoek, int? masterId, int max = 300)
    {
        var regels = new List<Regel>();
        if (string.IsNullOrWhiteSpace(_cs)) return (regels, 0);

        var waar = new List<string>();
        if (storeType is not null) waar.Add("f.`StoreType` = @st");
        if (doorId is not null) waar.Add("f.`ToegevoegdDoor` = @door");
        if (!string.IsNullOrWhiteSpace(extensie)) waar.Add("(f.`FileOnly` LIKE @ext OR f.`FileNaam` LIKE @ext)");
        if (!string.IsNullOrWhiteSpace(zoek)) waar.Add("(f.`FileOnly` LIKE @z OR f.`FileNaam` LIKE @z OR f.`FileStoreID` = @zid)");
        if (masterId is not null) waar.Add("f.`MasterID` = @master");
        var where = waar.Count > 0 ? " WHERE " + string.Join(" AND ", waar) : "";

        void Vul(MySqlCommand c)
        {
            if (storeType is not null) c.Parameters.AddWithValue("@st", storeType.Value);
            if (doorId is not null) c.Parameters.AddWithValue("@door", doorId.Value);
            if (!string.IsNullOrWhiteSpace(extensie)) c.Parameters.AddWithValue("@ext", "%." + extensie.Trim().TrimStart('.'));
            if (!string.IsNullOrWhiteSpace(zoek))
            {
                c.Parameters.AddWithValue("@z", "%" + zoek.Trim() + "%");
                c.Parameters.AddWithValue("@zid", int.TryParse(zoek.Trim(), out var zi) ? zi : -1);
            }
            if (masterId is not null) c.Parameters.AddWithValue("@master", masterId.Value);
        }

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();

        int totaal;
        await using (var tel = new MySqlCommand($"SELECT COUNT(*) FROM `FileStore` f{where}", cn))
        {
            Vul(tel);
            totaal = Convert.ToInt32(await tel.ExecuteScalarAsync());
        }

        await using var cmd = new MySqlCommand(
            "SELECT f.`FileStoreID`, f.`DatumToegevoegd`, COALESCE(f.`FileOnly`, ''), COALESCE(f.`FileNaam`, ''), " +
            "       COALESCE(f.`StoreType`, 0), COALESCE(f.`MasterID`, 0), f.`ToegevoegdDoor`, COALESCE(w.`NaamVoluit`, '') " +
            "  FROM `FileStore` f LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = f.`ToegevoegdDoor`" +
            where + " ORDER BY f.`FileStoreID` DESC LIMIT @max", cn);
        Vul(cmd);
        cmd.Parameters.AddWithValue("@max", max);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            regels.Add(new Regel(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : Convert.ToDateTime(r.GetValue(1)),
                r.GetString(2), r.GetString(3), Convert.ToInt32(r.GetValue(4)), "",
                Convert.ToInt32(r.GetValue(5)),
                r.IsDBNull(6) ? null : Convert.ToInt32(r.GetValue(6)),
                r.GetString(7)));
        return (regels, totaal);
    }

    /// <summary>Bestaat het bestand op schijf, en hoe groot/oud is het? Voor het detailpaneel en de controle.</summary>
    public (bool Bestaat, long Grootte, DateTime? Gewijzigd, string Pad, string Reden) OpSchijf(int fileStoreId, string opslagNaam)
    {
        if (string.IsNullOrWhiteSpace(_hoofdmap)) return (false, 0, null, "", "Filestore:Pad is niet ingesteld");
        if (string.IsNullOrWhiteSpace(opslagNaam)) return (false, 0, null, "", "geen bestandsnaam in de FileStore-regel");

        foreach (var verschuiving in new[] { 0, -1, 1 })
        {
            var pad = Path.Combine(_hoofdmap!, ((fileStoreId / 1000) + 1 + verschuiving).ToString("D6"), opslagNaam);
            if (verschuiving == 0 ? Bestaat(pad) : File.Exists(pad))
            {
                var info = new FileInfo(pad);
                return (true, info.Length, info.LastWriteTime, pad, "");
            }
        }

        // Niet gevonden: uitzoeken waarom, zodat de beheerder niet hoeft te raden.
        var verwacht = Path.Combine(_hoofdmap!, MapVoor(fileStoreId), opslagNaam);
        var map = Path.GetDirectoryName(verwacht)!;
        string reden;
        try
        {
            if (!Directory.Exists(_hoofdmap!)) reden = "hoofdmap niet bereikbaar voor het serviceaccount";
            else if (!Directory.Exists(map)) reden = $"submap {Path.GetFileName(map)} bestaat niet";
            else
            {
                // Zelfde naam, andere hoofdletters? (Linux-NAS is hoofdlettergevoelig.)
                var kaal = Path.GetFileNameWithoutExtension(opslagNaam);
                var gelijkend = Directory.EnumerateFiles(map, kaal + ".*").FirstOrDefault();
                reden = gelijkend is not null
                    ? $"wel gevonden als '{Path.GetFileName(gelijkend)}' (hoofdletters/extensie verschillen)"
                    : "submap bestaat, bestand staat er niet in";
            }
        }
        catch (UnauthorizedAccessException) { reden = "geen leesrechten voor het serviceaccount"; }
        catch (Exception ex) { reden = ex.Message; }
        return (false, 0, null, verwacht, reden);
    }

    /// <summary>De hoofdmap zoals ingesteld — voor de kop van het beheerscherm.</summary>
    public string Hoofdmap => _hoofdmap ?? "";

    public static string MimeVoor(string naam) => Path.GetExtension(naam).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".doc" or ".docx" => "application/msword",
        ".xls" or ".xlsx" => "application/vnd.ms-excel",
        ".dwg" => "image/vnd.dwg",
        ".step" or ".stp" => "model/step",
        _ => "application/octet-stream",
    };
}
