using System.Globalization;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een uitgegeven licentiecode voor een M-Pede-robot.</summary>
/// <param name="ProductId">Het exemplaar in Producten (de robot) waar de code voor is; leeg bij een met de hand ingevulde regel.</param>
public record MPedeLicentie(int Id, DateTime Datum, long Serienummer, string Code, string Robot, string Notitie,
                            int? DoorId, string Door, bool Vervallen, int? ProductId = null)
{
    /// <summary>De regel voor de PLC, om in het programma te plakken.</summary>
    public string StRegel => MPedeLicenties.StRegel(Code);
}

/// <summary>Een robot (exemplaar in Producten) om een licentie voor te maken.</summary>
public record MPedeRobot(int ProductId, string Serienummer);

/// <summary>
/// Wat we van een robot weten voor de licentie: het serienummer van de vierde drive in de EtherCAT-keten, of waarom
/// dat er niet is.
/// </summary>
public record MPedeRobotDrive(long? Serienummer, string Uitleg);

/// <summary>Wat het maken van een code opleverde.</summary>
/// <param name="Toegevoegd">Is er een regel bijgekomen?</param>
/// <param name="Bestaand">De regels die al bij dit serienummer staan (bij niet toegevoegd).</param>
/// <param name="Precies">Er staat al een regel met hetzelfde serienummer, robotnummer en dezelfde notitie: nooit een tweede keer.</param>
public record MPedeLicentieUitkomst(long Serienummer, string Code, bool Toegevoegd, int? Id, List<MPedeLicentie> Bestaand, bool Precies)
{
    public string StRegel => MPedeLicenties.StRegel(Code);
}

/// <summary>
/// Tools ▸ Licenties ▸ M-Pede Robotics (wens gebruiker 25-11-2026): de licentiecodes voor de MPR-bibliotheek
/// (<c>Robot.Config.LicenseKey</c>), overgenomen uit het Python-programma van Gerard (MPR_DebugTool.pyw; de rest van dat
/// programma blijft een los programma). De code is FNV-1a 32 bit over het serienummer van de J3-drive (4 bytes, laagste
/// eerst) en een geheime sleutel van 24 bytes; in de PLC rekent <c>nMPR\nService\mprLicenseCode</c> hetzelfde uit.
///
/// De sleutel staat in appsettings.json (<c>Licenties:MPedeSleutel</c>, 48 hex-tekens, de inhoud van mpr_license.key) en
/// komt nooit in beeld, in het logboek of in Git. Uitgegeven codes staan in de tabel LicentiesMPede (Licenties_MariaDB.sql)
/// in plaats van in licenses_issued.csv; dat bestand is een keer over te nemen (<see cref="ImporteerAsync"/>), waarbij
/// elke code opnieuw wordt uitgerekend: klopt hij niet met het bestand, dan is de sleutel in de app een andere.
/// Een regel wordt niet verwijderd maar vervallen gezet. Toegang: het recht Licenties of Directie.
/// Met <c>Licenties:Geheugen</c> staat alles in het geheugen, met een testsleutel als er geen echte is (lokaal testen).
/// </summary>
public class MPedeLicenties
{
    private readonly string? _cs;
    private readonly bool _geheugen;
    private readonly byte[]? _sleutel;
    private bool _aanwezig;
    private bool _metProduct;       // heeft LicentiesMPede de kolom ProductID (Licenties_MariaDB.sql opnieuw gedraaid)?

    /// <summary>
    /// Waar het serienummer voor de licentie in Producten staat. Op het scherm heet het "Joint3 (Drive B)" (zo noemen de
    /// gebruikers het, 25-11-2026): de vierde drive in de EtherCAT-keten van de robot is de
    /// tweede drive op de RZ-print (MPR-PCBA-RZ), en die print heeft het als attribuut met deze bron (gevuld bij het
    /// uitlezen op de testplek). Nagekeken op de bestaande licenties (25-11-2026): hun serienummers staan precies daar.
    /// </summary>
    public const string DriveBron = "drive:2:serienummer";
    /// <summary>De hoeveelste drive in de keten het is; een producttype met zo'n positie is een robot waar een licentie bij hoort.</summary>
    public const int DrivePositie = 4;

    private readonly object _slot = new();
    private readonly List<MPedeLicentie> _lijst = new();
    private int _volgend = 1;

    public MPedeLicenties(IConfiguration config)
    {
        _cs = config.GetConnectionString("MDE");
        _geheugen = config.GetValue<bool>("Licenties:Geheugen");
        _sleutel = LeesSleutel(config["Licenties:MPedeSleutel"]);
        // lokaal testen zonder de echte sleutel: een vaste testsleutel, alleen in het geheugen
        if (_sleutel is null && _geheugen) { _sleutel = Enumerable.Range(1, 24).Select(i => (byte)i).ToArray(); Testsleutel = true; }
    }

    /// <summary>In het geheugen en zonder echte sleutel: de codes zijn dan niet geldig voor een robot.</summary>
    public bool Testsleutel { get; }
    public bool Geheugen => _geheugen;
    private bool Live => !_geheugen && !string.IsNullOrWhiteSpace(_cs) && !_cs!.Contains("REPLACE_");
    public bool Beschikbaar => _geheugen || Live;
    /// <summary>Staat er een bruikbare sleutel in de instellingen (24 bytes)?</summary>
    public bool SleutelAanwezig => _sleutel is not null;

    /// <summary>De 24 bytes uit de instelling: 48 hex-tekens (spaties en een # met commentaar mogen), anders null.</summary>
    private static byte[]? LeesSleutel(string? tekst)
    {
        var t = (tekst ?? "").Split('#')[0].Replace(" ", "").Trim();
        if (t.Length != 48) return null;
        try { return Convert.FromHexString(t); } catch (FormatException) { return null; }
    }

    private static string Kort(string? s, int max)
    {
        s = (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length <= max ? s : s[..max];
    }

    // ------------------------------------------------------------------ de berekening

    /// <summary>FNV-1a 32 bit over het serienummer (4 bytes, laagste eerst) en de sleutel; zoals mprLicenseCode in de PLC.</summary>
    public static uint Bereken(uint serienummer, ReadOnlySpan<byte> sleutel)
    {
        uint h = 0x811C9DC5;
        Span<byte> s = stackalloc byte[4];
        BitConverter.TryWriteBytes(s, serienummer);
        if (!BitConverter.IsLittleEndian) s.Reverse();
        foreach (var b in s) h = unchecked((h ^ b) * 0x01000193);
        foreach (var b in sleutel) h = unchecked((h ^ b) * 0x01000193);
        return h;
    }

    public static string CodeTekst(uint code) => "16#" + code.ToString("X8", CultureInfo.InvariantCulture);
    public static string StRegel(string code) => $"Robot.Config.LicenseKey := DWORD#{code};";

    /// <summary>Het serienummer uit wat er getypt is: decimaal, 16#hex of 0xhex, tussen 1 en 4294967295. Anders een melding.</summary>
    public static (uint? Nummer, string Fout) LeesSerienummer(string? tekst)
    {
        var t = (tekst ?? "").Replace(" ", "").Trim();
        if (t.Length == 0) return (null, "Vul het serienummer van Joint3 (Drive B) in.");
        ulong v;
        bool ok;
        if (t.StartsWith("16#", StringComparison.OrdinalIgnoreCase)) ok = ulong.TryParse(t[3..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        else if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) ok = ulong.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        else ok = ulong.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out v);
        if (!ok) return (null, "Het serienummer is een getal: decimaal of 16#hex.");
        if (v is 0 or > uint.MaxValue) return (null, "Een serienummer ligt tussen 1 en 4294967295.");
        return ((uint)v, "");
    }

    private string CodeVoor(uint serienummer)
        => CodeTekst(Bereken(serienummer, _sleutel ?? throw new InvalidOperationException("De sleutel voor de licenties is niet ingesteld (Licenties:MPedeSleutel).")));

    // ------------------------------------------------------------------ lezen

    public async Task<bool> AanwezigAsync()
    {
        if (_geheugen) return true;
        if (_aanwezig || !Live) return _aanwezig;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'LicentiesMPede'", cn);
        return _aanwezig = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private async Task<bool> MetProductAsync()
    {
        if (_geheugen) return true;
        if (_metProduct || !await AanwezigAsync()) return _metProduct;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'LicentiesMPede' AND COLUMN_NAME = 'ProductID'", cn);
        return _metProduct = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    // ------------------------------------------------------------------ de robot uit Producten

    /// <summary>De producttypes waar een licentie bij hoort: types met een vierde drive in de keten (nu MPR-1).</summary>
    public async Task<List<(int Id, string Naam)>> RobotTypesAsync()
    {
        if (_geheugen) return new() { (31, "MPR-1") };
        var lijst = new List<(int, string)>();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT n.`ProductNameID`, n.`ProductName` FROM `ProductNames` n " +
            $" WHERE EXISTS (SELECT 1 FROM `DrivePosities` p WHERE p.`ProductNameID` = n.`ProductNameID` AND p.`Positie` = {DrivePositie}) ORDER BY n.`ProductName`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add((r.GetInt32(0), r.GetString(1)));
        return lijst;
    }

    /// <summary>De exemplaren van een type, nieuwste eerst.</summary>
    public async Task<List<MPedeRobot>> RobotsAsync(int productNameId)
    {
        if (_geheugen) return new() { new(1057, "57"), new(1056, "56"), new(1055, "055"), new(1058, "58") };
        var lijst = new List<MPedeRobot>();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ProductenID`, TRIM(COALESCE(`SerieNummer`, '')) FROM `Producten` WHERE `ProductNameID` = @t AND COALESCE(`isDeleted`, 0) = 0 ORDER BY `ProductenID` DESC", cn);
        cmd.Parameters.AddWithValue("@t", productNameId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new MPedeRobot(r.GetInt32(0), r.GetString(1)));
        return lijst;
    }

    /// <summary>De kern van een serienummer om te vergelijken: het laatste getal zonder voorloopnullen ("MPR1-0032" = "032" = "32").</summary>
    public static string Kern(string? serienummer)
    {
        var t = (serienummer ?? "").Trim();
        var m = System.Text.RegularExpressions.Regex.Matches(t, @"\d+");
        if (m.Count == 0) return t.ToLowerInvariant();
        var n = m[^1].Value.TrimStart('0');
        return n.Length == 0 ? "0" : n;
    }

    /// <summary>De robot bij wat er getypt of gescand is: eerst precies het serienummer, anders op de kern. Null = niet gevonden of niet eenduidig.</summary>
    public static MPedeRobot? ZoekRobot(IReadOnlyCollection<MPedeRobot> robots, string? tekst)
    {
        var t = (tekst ?? "").Trim();
        if (t.Length == 0) return null;
        var precies = robots.Where(x => string.Equals(x.Serienummer, t, StringComparison.OrdinalIgnoreCase)).ToList();
        if (precies.Count == 1) return precies[0];
        var kern = Kern(t);
        var opKern = robots.Where(x => Kern(x.Serienummer) == kern).ToList();
        return opKern.Count == 1 ? opKern[0] : null;
    }

    /// <summary>Het serienummer van de vierde drive van deze robot, zoals het bij zijn RZ-print in Producten staat.</summary>
    public async Task<MPedeRobotDrive> DriveVanRobotAsync(int productId)
    {
        var gevonden = new List<string>();
        if (_geheugen)
        {
            if (productId is 1057) gevonden.Add("1234567890");
            else if (productId is 1056) gevonden.Add("6699");
            else if (productId is 1055) { gevonden.Add("111"); gevonden.Add("222"); }
        }
        else
        {
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT DISTINCT TRIM(a.`AttributeValue`) FROM `Producten` k " +
                "  JOIN `ProductAttributes` a ON a.`ProductID` = k.`ProductenID` " +
                "  JOIN `ProductNameAttributes` na ON na.`ProductNameAttributeID` = a.`ProductNameAttributeID` " +
                " WHERE k.`ParentProductenID` = @r AND COALESCE(k.`isDeleted`, 0) = 0 AND na.`Bron` = @b AND TRIM(COALESCE(a.`AttributeValue`, '')) <> ''", cn);
            cmd.Parameters.AddWithValue("@r", productId);
            cmd.Parameters.AddWithValue("@b", DriveBron);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) gevonden.Add(r.GetString(0));
        }
        if (gevonden.Count == 0)
            return new(null, "Bij deze robot is het serienummer van Joint3 (Drive B) nog niet uitgelezen (Producten ▸ Programmeren en testen). Vul het hieronder met de hand in.");
        if (gevonden.Count > 1)
            return new(null, $"Bij deze robot staan {gevonden.Count} verschillende serienummers voor Joint3 (Drive B) ({string.Join(", ", gevonden)}). Kijk dat na in Producten en vul hier het goede in.");
        var (nummer, fout) = LeesSerienummer(gevonden[0]);
        return nummer is uint n ? new(n, "") : new(null, $"Het serienummer in Producten ({gevonden[0]}) is niet bruikbaar: {fout}");
    }

    /// <summary>De uitgegeven codes, nieuwste eerst.</summary>
    public async Task<List<MPedeLicentie>> LijstAsync(bool ookVervallen)
    {
        if (_geheugen) lock (_slot) return _lijst.Where(x => ookVervallen || !x.Vervallen).OrderByDescending(x => x.Datum).ThenByDescending(x => x.Id).ToList();
        var lijst = new List<MPedeLicentie>();
        if (!await AanwezigAsync()) return lijst;
        var metProduct = await MetProductAsync();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT l.`LicentieID`, l.`Datum`, l.`Serienummer`, l.`Code`, COALESCE(l.`Robot`, ''), COALESCE(l.`Notitie`, ''), l.`Door`, " +
            "       COALESCE(w.`NaamVoluit`, ''), l.`Vervallen`, " + (metProduct ? "l.`ProductID` " : "NULL ") +
            "  FROM `LicentiesMPede` l LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = l.`Door` " +
            (ookVervallen ? "" : " WHERE l.`Vervallen` = 0 ") + " ORDER BY l.`Datum` DESC, l.`LicentieID` DESC", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new MPedeLicentie(r.GetInt32(0), r.GetDateTime(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5),
                                        r.IsDBNull(6) ? null : r.GetInt32(6), r.GetString(7), Convert.ToInt32(r.GetValue(8)) != 0,
                                        r.IsDBNull(9) ? null : r.GetInt32(9)));
        return lijst;
    }

    private static string Norm(string? t) => string.Join(' ', (t ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    private static bool Zelfde(MPedeLicentie l, long serie, string robot, string notitie)
        => l.Serienummer == serie && Norm(l.Robot) == Norm(robot) && Norm(l.Notitie) == Norm(notitie);

    // ------------------------------------------------------------------ schrijven

    /// <summary>
    /// Een code maken en in de lijst zetten. Staat het serienummer er al en is <paramref name="toch"/> niet gezet, dan
    /// komt er niets bij: de uitkomst geeft de regels die er staan, zodat het scherm kan vragen of het toch moet.
    /// Precies dezelfde regel (serienummer, robotnummer en notitie) komt er nooit een tweede keer.
    /// </summary>
    public async Task<MPedeLicentieUitkomst> MaakAsync(uint serienummer, string robot, string notitie, int? door, bool toch, int? productId = null)
    {
        robot = Kort(robot, 40);
        if (robot.Length == 0) throw new InvalidOperationException("Vul het robotnummer in.");
        notitie = Kort(notitie, 120);
        var code = CodeVoor(serienummer);
        var bestaand = (await LijstAsync(ookVervallen: false)).Where(x => x.Serienummer == serienummer).ToList();
        var precies = bestaand.Any(x => Zelfde(x, serienummer, robot, notitie));
        if (precies || (bestaand.Count > 0 && !toch))
            return new MPedeLicentieUitkomst(serienummer, code, false, null, bestaand, precies);
        var id = await VoegToeAsync(DateTime.Now, serienummer, code, robot, notitie, door, productId);
        return new MPedeLicentieUitkomst(serienummer, code, true, id, bestaand, false);
    }

    private async Task<int> VoegToeAsync(DateTime datum, long serienummer, string code, string robot, string notitie, int? door, int? productId = null)
    {
        if (_geheugen)
            lock (_slot)
            {
                var id = _volgend++;
                _lijst.Add(new MPedeLicentie(id, datum, serienummer, code, robot, notitie, door, "", false, productId));
                return id;
            }
        if (!await AanwezigAsync()) throw new InvalidOperationException("Licenties_MariaDB.sql is nog niet uitgevoerd.");
        var metProduct = await MetProductAsync();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `LicentiesMPede` (`Datum`, `Serienummer`, `Code`, `Robot`, `Notitie`, `Door`, `Vervallen`" + (metProduct ? ", `ProductID`" : "") + ") " +
            "VALUES (@d, @s, @c, @r, @n, @w, 0" + (metProduct ? ", @p" : "") + "); SELECT LAST_INSERT_ID();", cn);
        if (metProduct) cmd.Parameters.AddWithValue("@p", (object?)productId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@d", datum);
        cmd.Parameters.AddWithValue("@s", serienummer);
        cmd.Parameters.AddWithValue("@c", code);
        cmd.Parameters.AddWithValue("@r", robot);
        cmd.Parameters.AddWithValue("@n", notitie);
        cmd.Parameters.AddWithValue("@w", (object?)door ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Robotnummer en notitie van een regel wijzigen; serienummer, code en datum blijven.</summary>
    public async Task WijzigAsync(int id, string robot, string notitie)
    {
        robot = Kort(robot, 40);
        if (robot.Length == 0) throw new InvalidOperationException("Vul het robotnummer in.");
        notitie = Kort(notitie, 120);
        var alle = await LijstAsync(ookVervallen: true);
        var deze = alle.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Deze regel bestaat niet meer.");
        if (alle.Any(x => x.Id != id && !x.Vervallen && Zelfde(x, deze.Serienummer, robot, notitie)))
            throw new InvalidOperationException($"Er staat al een regel met dit serienummer, robotnummer {robot} en deze notitie.");
        if (_geheugen)
        {
            lock (_slot) { var i = _lijst.FindIndex(x => x.Id == id); if (i >= 0) _lijst[i] = _lijst[i] with { Robot = robot, Notitie = notitie }; }
            return;
        }
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `LicentiesMPede` SET `Robot` = @r, `Notitie` = @n WHERE `LicentieID` = @id", cn);
        cmd.Parameters.AddWithValue("@r", robot);
        cmd.Parameters.AddWithValue("@n", notitie);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Een regel wissen (of terughalen). Op het scherm heet het wissen; de regel blijft in de tabel staan met
    /// Vervallen = 1, zodat altijd na te gaan is welke codes er ooit zijn uitgegeven.
    /// </summary>
    public async Task ZetVervallenAsync(int id, bool vervallen)
    {
        if (_geheugen)
        {
            lock (_slot) { var i = _lijst.FindIndex(x => x.Id == id); if (i >= 0) _lijst[i] = _lijst[i] with { Vervallen = vervallen }; }
            return;
        }
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `LicentiesMPede` SET `Vervallen` = @v WHERE `LicentieID` = @id", cn);
        cmd.Parameters.AddWithValue("@v", vervallen ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // ------------------------------------------------------------------ licenses_issued.csv overnemen

    /// <summary>
    /// NIET MEER IN GEBRUIK op het scherm (de knop is op 25-11-2026 weggehaald: de lijst is overgenomen); bewaard voor
    /// als er ooit nog een bestand van het Python-programma bij moet.
    /// De regels uit licenses_issued.csv van het Python-programma overnemen (date;serial;code;note;robot). Elke code
    /// wordt opnieuw uitgerekend; wijkt hij af van het bestand, dan komt de regel er niet in (de sleutel in de app is dan
    /// een andere dan die van het bestand). Wat er al staat (zelfde serienummer, robotnummer en notitie) wordt overgeslagen.
    /// </summary>
    public async Task<(int Toegevoegd, int Overgeslagen, List<string> Fouten)> ImporteerAsync(string csv, int? door)
    {
        var fouten = new List<string>();
        int toegevoegd = 0, overgeslagen = 0, nr = 0;
        var staat = await LijstAsync(ookVervallen: true);
        foreach (var ruw in (csv ?? "").Replace("\r", "").Split('\n'))
        {
            var regel = ruw.Trim().TrimStart('﻿');
            if (regel.Length == 0) continue;
            nr++;
            var v = regel.Split(';').Select(x => x.Trim().Trim('"')).ToArray();
            if (nr == 1 && v[0].Equals("date", StringComparison.OrdinalIgnoreCase)) continue;      // de kopregel
            if (v.Length < 4) { fouten.Add($"regel {nr}: te weinig velden"); continue; }
            var (serie, fout) = LeesSerienummer(v[1]);
            if (serie is not uint s) { fouten.Add($"regel {nr}: {fout}"); continue; }
            var code = CodeVoor(s);
            if (!string.Equals(code, v[2], StringComparison.OrdinalIgnoreCase))
            {
                fouten.Add($"regel {nr} (serienummer {s}): de code in het bestand is een andere dan de app uitrekent. Is de sleutel in appsettings.json dezelfde als mpr_license.key?");
                continue;
            }
            var robot = Kort(v.Length > 4 ? v[4] : "", 40);
            if (robot.Length == 0) robot = "?";
            var notitie = Kort(v[3], 120);
            if (staat.Any(x => Zelfde(x, s, robot, notitie))) { overgeslagen++; continue; }
            var datum = DateTime.TryParseExact(v[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.Now;
            var id = await VoegToeAsync(datum, s, code, robot, notitie, door);
            staat.Add(new MPedeLicentie(id, datum, s, code, robot, notitie, door, "", false));
            toegevoegd++;
        }
        return (toegevoegd, overgeslagen, fouten);
    }
}
