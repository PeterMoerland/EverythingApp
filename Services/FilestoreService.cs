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
        if (!File.Exists(pad))
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
