using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Drivetypen, firmwareversies en parametersets (tabellen uit DriveTypen_MariaDB.sql).
/// Bestanden zelf staan in de FileStore; hier alleen de FileStoreID's.
/// </summary>
public interface IDriveRepository
{
    bool Beschikbaar { get; }

    // --- typen ---
    Task<List<DriveSoort>> GetTypenAsync();
    /// <summary>
    /// Type bij wat een slave meldt. Met <paramref name="vendorId"/> telt eerst de combinatie fabrikant +
    /// productcode; een type zonder vastgelegde fabrikantcode wordt op productcode alleen herkend.
    /// </summary>
    Task<DriveSoort?> ZoekTypeOpCodeAsync(string productCode, string vendorId = "");
    Task<int> BewaarTypeAsync(DriveSoort t, int? door);
    Task VerwijderTypeAsync(int id);

    // --- firmwares ---
    Task<List<DriveFirmware>> GetFirmwaresAsync(int driveTypeId);
    Task<DriveFirmware?> ZoekFirmwareAsync(int driveTypeId, string versie);
    Task<int> BewaarFirmwareAsync(DriveFirmware f, int? door);
    Task ZetFirmwareStatusAsync(int firmwareId, string status, int? door);
    Task VerwijderFirmwareAsync(int id);

    // --- posities en parametersets per halffabricaat ---
    Task<List<DrivePositie>> GetPositiesAsync(int productNameId);
    Task<int> BewaarPositieAsync(DrivePositie p);
    Task VerwijderPositieAsync(int id);
    Task<List<DriveParameterset>> GetParametersetsAsync(int productNameId);
    Task<int> BewaarParametersetAsync(DriveParameterset p, int? door);
    Task VerwijderParametersetAsync(int id);

    /// <summary>De actuele parameterset voor halffabricaat × positie × firmwareversie; null als die er niet is.</summary>
    Task<DriveParameterset?> ZoekParametersetAsync(int productNameId, int positie, int driveFirmwareId);

    // --- testrecepten per type (DriveRecepten_MariaDB.sql) ---
    Task<List<DriveRecept>> GetReceptenAsync(int driveTypeId);
    /// <summary>Recepten van meerdere typen in één keer; leeg (geen fout) als de tabel nog ontbreekt.</summary>
    Task<List<DriveRecept>> GetReceptenVoorTypenAsync(IEnumerable<int> driveTypeIds);
    Task<int> BewaarReceptAsync(DriveRecept r, int? door);
    Task VerwijderReceptAsync(int id);
}

public class NullDriveRepository : IDriveRepository
{
    public bool Beschikbaar => false;
    public Task<List<DriveSoort>> GetTypenAsync() => Task.FromResult(new List<DriveSoort>());
    public Task<DriveSoort?> ZoekTypeOpCodeAsync(string productCode, string vendorId = "") => Task.FromResult<DriveSoort?>(null);
    public Task<int> BewaarTypeAsync(DriveSoort t, int? door) => Task.FromResult(0);
    public Task VerwijderTypeAsync(int id) => Task.CompletedTask;
    public Task<List<DriveFirmware>> GetFirmwaresAsync(int driveTypeId) => Task.FromResult(new List<DriveFirmware>());
    public Task<DriveFirmware?> ZoekFirmwareAsync(int driveTypeId, string versie) => Task.FromResult<DriveFirmware?>(null);
    public Task<int> BewaarFirmwareAsync(DriveFirmware f, int? door) => Task.FromResult(0);
    public Task ZetFirmwareStatusAsync(int firmwareId, string status, int? door) => Task.CompletedTask;
    public Task VerwijderFirmwareAsync(int id) => Task.CompletedTask;
    public Task<List<DrivePositie>> GetPositiesAsync(int productNameId) => Task.FromResult(new List<DrivePositie>());
    public Task<int> BewaarPositieAsync(DrivePositie p) => Task.FromResult(0);
    public Task VerwijderPositieAsync(int id) => Task.CompletedTask;
    public Task<List<DriveParameterset>> GetParametersetsAsync(int productNameId) => Task.FromResult(new List<DriveParameterset>());
    public Task<int> BewaarParametersetAsync(DriveParameterset p, int? door) => Task.FromResult(0);
    public Task VerwijderParametersetAsync(int id) => Task.CompletedTask;
    public Task<DriveParameterset?> ZoekParametersetAsync(int productNameId, int positie, int driveFirmwareId) => Task.FromResult<DriveParameterset?>(null);
    public Task<List<DriveRecept>> GetReceptenAsync(int driveTypeId) => Task.FromResult(new List<DriveRecept>());
    public Task<List<DriveRecept>> GetReceptenVoorTypenAsync(IEnumerable<int> driveTypeIds) => Task.FromResult(new List<DriveRecept>());
    public Task<int> BewaarReceptAsync(DriveRecept r, int? door) => Task.FromResult(0);
    public Task VerwijderReceptAsync(int id) => Task.CompletedTask;
}

public class MySqlDriveRepository : IDriveRepository
{
    private readonly string _cs;
    public MySqlDriveRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static int? N(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static DateTime? D(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));

    /// <summary>Productcode altijd als '0x' + 8 hexcijfers, hoe hij ook is ingevoerd.</summary>
    public static string NormaliseerCode(string code)
    {
        var t = code.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        if (long.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var hex) &&
            !long.TryParse(code.Trim(), out _)) return $"0x{hex:X8}";
        if (long.TryParse(code.Trim(), out var dec)) return $"0x{dec:X8}";
        return code.Trim();
    }

    // ------------------------------------------------------------------ typen
    // Latere kolommen (Controllers_MariaDB.sql: Protocol; DriveTypen_Vendor_MariaDB.sql: VendorID).
    // Alleen 'ja' onthouden, zodat het scherm na het uitvoeren van het script meteen werkt.
    private readonly Dictionary<string, bool> _kolommen = new(StringComparer.OrdinalIgnoreCase);
    private async Task<bool> HeeftKolomAsync(MySqlConnection cn, string kolom)
    {
        if (_kolommen.TryGetValue(kolom, out var ja) && ja) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = 'DriveTypen' AND LOWER(column_name) = @k", cn);
        cmd.Parameters.AddWithValue("@k", kolom.ToLowerInvariant());
        ja = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        _kolommen[kolom] = ja;
        return ja;
    }

    public async Task<List<DriveSoort>> GetTypenAsync()
    {
        var lijst = new List<DriveSoort>();
        await using var cn = await OpenAsync();
        var protocol = await HeeftKolomAsync(cn, "Protocol") ? "t.`Protocol`" : "'ethercat'";
        var vendor = await HeeftKolomAsync(cn, "VendorID") ? "t.`VendorID`" : "''";
        await using var cmd = new MySqlCommand(
            "SELECT t.`DriveTypeID`, t.`ProductCode`, t.`Naam`, t.`Fabrikant`, t.`Opmerking`, " +
            $"       (SELECT COUNT(*) FROM `DriveFirmwares` f WHERE f.`DriveTypeID` = t.`DriveTypeID`), {protocol}, {vendor} " +
            "  FROM `DriveTypen` t ORDER BY t.`Naam`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new DriveSoort
            {
                Id = r.GetInt32(0), ProductCode = S(r, 1), Naam = S(r, 2), Fabrikant = S(r, 3),
                Opmerking = S(r, 4), AantalFirmwares = Convert.ToInt32(r.GetValue(5)),
                Protocol = S(r, 6) is { Length: > 0 } p ? p : DriveProtocol.EtherCat,
                VendorId = S(r, 7),
            });
        return lijst;
    }

    public async Task<DriveSoort?> ZoekTypeOpCodeAsync(string productCode, string vendorId = "")
    {
        var code = NormaliseerCode(productCode);
        var vendor = string.IsNullOrWhiteSpace(vendorId) ? "" : NormaliseerCode(vendorId);
        var typen = await GetTypenAsync();

        bool CodeKlopt(DriveSoort t) =>
            string.Equals(t.ProductCode, productCode.Trim(), StringComparison.OrdinalIgnoreCase) ||
            (DriveProtocol.OpBus(t.Protocol) && string.Equals(NormaliseerCode(t.ProductCode), code, StringComparison.OrdinalIgnoreCase));

        // Eerst de combinatie fabrikant + productcode; daarna een type waarvan de fabrikant nog niet is
        // vastgelegd (alleen productcode). Een type met een ándere fabrikantcode telt nooit.
        if (vendor.Length > 0)
        {
            var exact = typen.FirstOrDefault(t => CodeKlopt(t) && string.Equals(NormaliseerCode(t.VendorId), vendor, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            return typen.FirstOrDefault(t => CodeKlopt(t) && t.VendorId.Trim().Length == 0);
        }
        return typen.FirstOrDefault(CodeKlopt);
    }

    public async Task<int> BewaarTypeAsync(DriveSoort t, int? door)
    {
        await using var cn = await OpenAsync();
        var metProtocol = await HeeftKolomAsync(cn, "Protocol");
        var metVendor = await HeeftKolomAsync(cn, "VendorID");
        var velden = "`ProductCode` = @code, `Naam` = @naam, `Fabrikant` = @fab, `Opmerking` = @opm"
                   + (metProtocol ? ", `Protocol` = @protocol" : "")
                   + (metVendor ? ", `VendorID` = @vendor" : "");
        var sql = t.Id == 0
            ? $"INSERT INTO `DriveTypen` SET {velden}, `AddedBy` = @door, `AddedAt` = NOW()"
            : $"UPDATE `DriveTypen` SET {velden} WHERE `DriveTypeID` = @id";
        await using var cmd = new MySqlCommand(sql, cn);
        // Op de bus is de code een hex-productcode; bij andere protocollen een modelnaam.
        var opBus = DriveProtocol.OpBus(t.Protocol);
        cmd.Parameters.AddWithValue("@code", opBus ? NormaliseerCode(t.ProductCode) : t.ProductCode.Trim());
        if (metProtocol) cmd.Parameters.AddWithValue("@protocol", string.IsNullOrWhiteSpace(t.Protocol) ? DriveProtocol.EtherCat : t.Protocol);
        if (metVendor) cmd.Parameters.AddWithValue("@vendor", opBus && !string.IsNullOrWhiteSpace(t.VendorId) ? NormaliseerCode(t.VendorId) : "");
        cmd.Parameters.AddWithValue("@naam", t.Naam.Trim());
        cmd.Parameters.AddWithValue("@fab", t.Fabrikant.Trim());
        cmd.Parameters.AddWithValue("@opm", t.Opmerking.Trim());
        if (t.Id == 0) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        else cmd.Parameters.AddWithValue("@id", t.Id);
        await cmd.ExecuteNonQueryAsync();
        return t.Id == 0 ? (int)cmd.LastInsertedId : t.Id;
    }

    public async Task VerwijderTypeAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `DriveTypen` WHERE `DriveTypeID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // -------------------------------------------------------------- firmwares
    private const string FirmwareSelect =
        "SELECT f.`DriveFirmwareID`, f.`DriveTypeID`, f.`Versie`, f.`Status`, " +
        "       f.`FirmwareFileID`, COALESCE(ff.`FileOnly`, ''), f.`DictionaryFileID`, COALESCE(fd.`FileOnly`, ''), " +
        "       f.`Vrijgegeven`, f.`Opmerking`, COALESCE(w.`NaamVoluit`, ''), f.`StatusOp`, " +
        "       f.`EsiFileID`, COALESCE(fe.`FileOnly`, '') " +
        "  FROM `DriveFirmwares` f " +
        "  LEFT JOIN `FileStore` ff ON ff.`FileStoreID` = f.`FirmwareFileID` " +
        "  LEFT JOIN `FileStore` fd ON fd.`FileStoreID` = f.`DictionaryFileID` " +
        "  LEFT JOIN `FileStore` fe ON fe.`FileStoreID` = f.`EsiFileID` " +
        "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = f.`StatusDoor` ";

    private static DriveFirmware LeesFirmware(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0), DriveTypeId = r.GetInt32(1), Versie = S(r, 2), Status = S(r, 3),
        FirmwareFileId = N(r, 4), FirmwareBestand = S(r, 5), DictionaryFileId = N(r, 6), DictionaryBestand = S(r, 7),
        Vrijgegeven = D(r, 8), Opmerking = S(r, 9), StatusDoor = S(r, 10), StatusOp = D(r, 11),
        EsiFileId = N(r, 12), EsiBestand = S(r, 13),
    };

    public async Task<List<DriveFirmware>> GetFirmwaresAsync(int driveTypeId)
    {
        var lijst = new List<DriveFirmware>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(FirmwareSelect + " WHERE f.`DriveTypeID` = @t", cn);
        cmd.Parameters.AddWithValue("@t", driveTypeId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(LeesFirmware(r));
        // Nieuwste boven, op echte versienummers: 2.10.0 komt na 2.9.0, niet ervoor.
        return lijst.OrderByDescending(f => f.Versie, VersieVergelijker.Instantie).ToList();
    }

    /// <summary>Vergelijkt versies als reeksen getallen ("2.10.0.000" > "2.9.1"); tekst valt terug op alfabetisch.</summary>
    public sealed class VersieVergelijker : IComparer<string>
    {
        public static readonly VersieVergelijker Instantie = new();

        public int Compare(string? a, string? b)
        {
            var xa = Delen(a); var xb = Delen(b);
            for (var i = 0; i < Math.Max(xa.Length, xb.Length); i++)
            {
                var da = i < xa.Length ? xa[i] : "";
                var db = i < xb.Length ? xb[i] : "";
                var na = long.TryParse(da, out var va);
                var nb = long.TryParse(db, out var vb);
                var c = na && nb ? va.CompareTo(vb) : string.Compare(da, db, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
            }
            return 0;
        }

        private static string[] Delen(string? s)
            => (s ?? "").Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>Gelijk op inhoud: nullen aan het eind tellen niet ('2.10.0' == '2.10.0.000', '2.10' == '2.10.0').</summary>
        public static bool Gelijk(string? a, string? b)
        {
            static List<long> Kaal(string? s)
            {
                var d = Delen(s).Select(x => long.TryParse(x, out var n) ? n : -1).ToList();
                while (d.Count > 0 && d[^1] == 0) d.RemoveAt(d.Count - 1);
                return d;
            }
            var xa = Kaal(a); var xb = Kaal(b);
            return xa.Count == xb.Count && !xa.Contains(-1) && !xb.Contains(-1) && xa.SequenceEqual(xb);
        }
    }

    /// <summary>Versie opzoeken; '2.10.0' en '2.10.0.000' gelden als dezelfde versie.</summary>
    public async Task<DriveFirmware?> ZoekFirmwareAsync(int driveTypeId, string versie)
    {
        var lijst = await GetFirmwaresAsync(driveTypeId);
        return lijst.FirstOrDefault(f => string.Equals(f.Versie.Trim(), versie.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? lijst.FirstOrDefault(f => VersieVergelijker.Gelijk(f.Versie, versie));
    }

    public async Task<int> BewaarFirmwareAsync(DriveFirmware f, int? door)
    {
        const string velden =
            "`DriveTypeID` = @t, `Versie` = @v, `FirmwareFileID` = @ff, `DictionaryFileID` = @fd, `EsiFileID` = @fe, " +
            "`Vrijgegeven` = @vrij, `Opmerking` = @opm";
        var sql = f.Id == 0
            ? $"INSERT INTO `DriveFirmwares` SET {velden}, `Status` = @status, `StatusDoor` = @door, `StatusOp` = NOW(), `AddedBy` = @door, `AddedAt` = NOW()"
            : $"UPDATE `DriveFirmwares` SET {velden} WHERE `DriveFirmwareID` = @id";
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@t", f.DriveTypeId);
        cmd.Parameters.AddWithValue("@v", f.Versie.Trim());
        cmd.Parameters.AddWithValue("@ff", (object?)f.FirmwareFileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fd", (object?)f.DictionaryFileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fe", (object?)f.EsiFileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vrij", (object?)f.Vrijgegeven ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@opm", f.Opmerking.Trim());
        if (f.Id == 0)
        {
            cmd.Parameters.AddWithValue("@status", f.Status);
            cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        }
        else cmd.Parameters.AddWithValue("@id", f.Id);
        await cmd.ExecuteNonQueryAsync();
        var id = f.Id == 0 ? (int)cmd.LastInsertedId : f.Id;
        if (f.Id == 0 && f.Status == DriveStatus.Actueel) await ZetFirmwareStatusAsync(id, DriveStatus.Actueel, door);
        return id;
    }

    /// <summary>Status zetten; 'actueel' is er maar één per type, de vorige wordt 'toegestaan'.</summary>
    public async Task ZetFirmwareStatusAsync(int firmwareId, string status, int? door)
    {
        await using var cn = await OpenAsync();
        if (status == DriveStatus.Actueel)
        {
            await using var vorige = new MySqlCommand(
                "UPDATE `DriveFirmwares` SET `Status` = 'toegestaan', `StatusDoor` = @door, `StatusOp` = NOW() " +
                " WHERE `Status` = 'actueel' AND `DriveFirmwareID` <> @id " +
                "   AND `DriveTypeID` = (SELECT `DriveTypeID` FROM (SELECT `DriveTypeID` FROM `DriveFirmwares` WHERE `DriveFirmwareID` = @id) x)", cn);
            vorige.Parameters.AddWithValue("@id", firmwareId);
            vorige.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            await vorige.ExecuteNonQueryAsync();
        }
        await using var cmd = new MySqlCommand(
            "UPDATE `DriveFirmwares` SET `Status` = @s, `StatusDoor` = @door, `StatusOp` = NOW() WHERE `DriveFirmwareID` = @id", cn);
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", firmwareId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task VerwijderFirmwareAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `DriveFirmwares` WHERE `DriveFirmwareID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // --------------------------------------------------------------- posities
    public async Task<List<DrivePositie>> GetPositiesAsync(int productNameId)
    {
        var lijst = new List<DrivePositie>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT p.`DrivePositieID`, p.`ProductNameID`, p.`Positie`, p.`Naam`, p.`DriveTypeID`, COALESCE(t.`Naam`, '') " +
            "  FROM `DrivePosities` p LEFT JOIN `DriveTypen` t ON t.`DriveTypeID` = p.`DriveTypeID` " +
            " WHERE p.`ProductNameID` = @n ORDER BY p.`Positie`", cn);
        cmd.Parameters.AddWithValue("@n", productNameId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new DrivePositie
            {
                Id = r.GetInt32(0), ProductNameId = r.GetInt32(1), Positie = r.GetInt32(2),
                Naam = S(r, 3), DriveTypeId = N(r, 4), DriveTypeNaam = S(r, 5),
            });
        return lijst;
    }

    public async Task<int> BewaarPositieAsync(DrivePositie p)
    {
        const string velden = "`ProductNameID` = @n, `Positie` = @pos, `Naam` = @naam, `DriveTypeID` = @t";
        var sql = p.Id == 0
            ? $"INSERT INTO `DrivePosities` SET {velden}"
            : $"UPDATE `DrivePosities` SET {velden} WHERE `DrivePositieID` = @id";
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@n", p.ProductNameId);
        cmd.Parameters.AddWithValue("@pos", p.Positie);
        cmd.Parameters.AddWithValue("@naam", p.Naam.Trim());
        cmd.Parameters.AddWithValue("@t", (object?)p.DriveTypeId ?? DBNull.Value);
        if (p.Id != 0) cmd.Parameters.AddWithValue("@id", p.Id);
        await cmd.ExecuteNonQueryAsync();
        return p.Id == 0 ? (int)cmd.LastInsertedId : p.Id;
    }

    public async Task VerwijderPositieAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `DrivePosities` WHERE `DrivePositieID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // ---------------------------------------------------------- parametersets
    private const string ParamSelect =
        "SELECT p.`DriveParameterID`, p.`ProductNameID`, p.`Positie`, p.`DriveFirmwareID`, f.`Versie`, COALESCE(t.`Naam`, ''), " +
        "       p.`Omschrijving`, p.`ParameterFileID`, COALESCE(fs.`FileOnly`, ''), p.`Status`, p.`Opmerking` " +
        "  FROM `DriveParameters` p " +
        "  JOIN `DriveFirmwares` f ON f.`DriveFirmwareID` = p.`DriveFirmwareID` " +
        "  LEFT JOIN `DriveTypen` t ON t.`DriveTypeID` = f.`DriveTypeID` " +
        "  LEFT JOIN `FileStore` fs ON fs.`FileStoreID` = p.`ParameterFileID` ";

    private static DriveParameterset LeesParam(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0), ProductNameId = r.GetInt32(1), Positie = r.GetInt32(2), DriveFirmwareId = r.GetInt32(3),
        FirmwareVersie = S(r, 4), DriveTypeNaam = S(r, 5), Omschrijving = S(r, 6), ParameterFileId = N(r, 7),
        ParameterBestand = S(r, 8), Status = S(r, 9), Opmerking = S(r, 10),
    };

    public async Task<List<DriveParameterset>> GetParametersetsAsync(int productNameId)
    {
        var lijst = new List<DriveParameterset>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(ParamSelect +
            " WHERE p.`ProductNameID` = @n", cn);
        cmd.Parameters.AddWithValue("@n", productNameId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(LeesParam(r));
        // Vaste, voorspelbare volgorde: positie, nieuwste firmware eerst, en bij gelijke stand het ID —
        // zonder dat laatste wisselden gelijke regels van plaats bij elke herlading.
        return lijst.OrderBy(p => p.Positie)
                    .ThenByDescending(p => p.FirmwareVersie, VersieVergelijker.Instantie)
                    .ThenBy(p => p.Id)
                    .ToList();
    }

    public async Task<DriveParameterset?> ZoekParametersetAsync(int productNameId, int positie, int driveFirmwareId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(ParamSelect +
            " WHERE p.`ProductNameID` = @n AND p.`Positie` = @pos AND p.`DriveFirmwareID` = @f AND p.`Status` = 'actueel' " +
            " ORDER BY p.`DriveParameterID` DESC LIMIT 1", cn);
        cmd.Parameters.AddWithValue("@n", productNameId);
        cmd.Parameters.AddWithValue("@pos", positie);
        cmd.Parameters.AddWithValue("@f", driveFirmwareId);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? LeesParam(r) : null;
    }

    public async Task<int> BewaarParametersetAsync(DriveParameterset p, int? door)
    {
        const string velden =
            "`ProductNameID` = @n, `Positie` = @pos, `DriveFirmwareID` = @f, `Omschrijving` = @oms, " +
            "`ParameterFileID` = @file, `Status` = @s, `Opmerking` = @opm";
        var sql = p.Id == 0
            ? $"INSERT INTO `DriveParameters` SET {velden}, `AddedBy` = @door, `AddedAt` = NOW()"
            : $"UPDATE `DriveParameters` SET {velden} WHERE `DriveParameterID` = @id";
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@n", p.ProductNameId);
        cmd.Parameters.AddWithValue("@pos", p.Positie);
        cmd.Parameters.AddWithValue("@f", p.DriveFirmwareId);
        cmd.Parameters.AddWithValue("@oms", p.Omschrijving.Trim());
        cmd.Parameters.AddWithValue("@file", (object?)p.ParameterFileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s", p.Status);
        cmd.Parameters.AddWithValue("@opm", p.Opmerking.Trim());
        if (p.Id == 0) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        else cmd.Parameters.AddWithValue("@id", p.Id);
        await cmd.ExecuteNonQueryAsync();
        return p.Id == 0 ? (int)cmd.LastInsertedId : p.Id;
    }

    public async Task VerwijderParametersetAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `DriveParameters` WHERE `DriveParameterID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // --------------------------------------------------------------- recepten
    private const string ReceptSelect =
        "SELECT `DriveReceptID`, `DriveTypeID`, `Volgorde`, `Naam`, COALESCE(`Omschrijving`, ''), `Stappen`, `Verplicht` FROM `DriveRecepten` ";

    private static DriveRecept LeesRecept(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0), DriveTypeId = r.GetInt32(1), Volgorde = Convert.ToInt32(r.GetValue(2)), Naam = S(r, 3),
        Omschrijving = S(r, 4), Stappen = S(r, 5), Verplicht = !r.IsDBNull(6) && Convert.ToInt32(r.GetValue(6)) != 0,
    };

    public async Task<List<DriveRecept>> GetReceptenAsync(int driveTypeId)
    {
        var lijst = new List<DriveRecept>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(ReceptSelect + "WHERE `DriveTypeID` = @t ORDER BY `Volgorde`, `DriveReceptID`", cn);
        cmd.Parameters.AddWithValue("@t", driveTypeId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(LeesRecept(r));
        return lijst;
    }

    public async Task<List<DriveRecept>> GetReceptenVoorTypenAsync(IEnumerable<int> driveTypeIds)
    {
        var ids = driveTypeIds.Distinct().ToList();
        var lijst = new List<DriveRecept>();
        if (ids.Count == 0) return lijst;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(ReceptSelect + $"WHERE `DriveTypeID` IN ({string.Join(",", ids)}) ORDER BY `DriveTypeID`, `Volgorde`, `DriveReceptID`", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) lijst.Add(LeesRecept(r));
        }
        catch (MySqlException ex) when (ex.Message.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            // DriveRecepten_MariaDB.sql nog niet uitgevoerd: Test PCB werkt dan zonder recepten.
        }
        return lijst;
    }

    public async Task<int> BewaarReceptAsync(DriveRecept x, int? door)
    {
        await using var cn = await OpenAsync();
        const string velden = "`DriveTypeID` = @t, `Volgorde` = @v, `Naam` = @n, `Omschrijving` = @o, `Stappen` = @s, `Verplicht` = @p";
        var sql = x.Id == 0
            ? $"INSERT INTO `DriveRecepten` SET {velden}, `AddedBy` = @door, `AddedAt` = NOW()"
            : $"UPDATE `DriveRecepten` SET {velden} WHERE `DriveReceptID` = @id";
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@t", x.DriveTypeId);
        cmd.Parameters.AddWithValue("@v", x.Volgorde);
        cmd.Parameters.AddWithValue("@n", x.Naam.Trim());
        cmd.Parameters.AddWithValue("@o", x.Omschrijving.Trim());
        cmd.Parameters.AddWithValue("@s", x.Stappen);
        cmd.Parameters.AddWithValue("@p", x.Verplicht ? 1 : 0);
        if (x.Id == 0) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        else cmd.Parameters.AddWithValue("@id", x.Id);
        await cmd.ExecuteNonQueryAsync();
        return x.Id == 0 ? (int)cmd.LastInsertedId : x.Id;
    }

    public async Task VerwijderReceptAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `DriveRecepten` WHERE `DriveReceptID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
