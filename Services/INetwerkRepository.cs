using System.Net;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace EverythingApp.Services;

// Netwerk (wens gebruiker 23-11-2026): de vaste IP-adressen per groep, zoals in Access (tabellen IPgroep en
// NetworkTopologie, ongewijzigd). Zien en bijwerken: het recht Netwerk of Directie (HuidigeGebruiker.MagNetwerk).
// Wachtwoorden: alleen Directie. Daarom leest de lijst het wachtwoord nooit mee (alleen óf er een is); het wachtwoord
// zelf wordt per adres opgehaald als iemand van de Directie erom vraagt.

/// <summary>Een groep adressen (IPgroep): naam en het bereik waar de adressen in horen.</summary>
public record IpGroep(int Id, string Omschrijving, string Van, string Tot);

/// <summary>Een vast adres (NetworkTopologie). Het wachtwoord zit er niet in: alleen of er een is.</summary>
public class IpAdres
{
    public int Id { get; set; }
    public string Ip { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public int? GroepId { get; set; }
    public string LoginUser { get; set; } = "";
    public bool HeeftWachtwoord { get; set; }
    public string DomainName { get; set; } = "";
    public int? Poort { get; set; }
    public string Opmerking { get; set; } = "";

    public IpAdres Kopie() => (IpAdres)MemberwiseClone();
}

public static class Netwerk
{
    /// <summary>Een IPv4-adres als getal (om te sorteren en met een bereik te vergelijken); null = geen geldig adres.</summary>
    public static uint? Getal(string? ip)
    {
        var t = (ip ?? "").Trim();
        if (t.Count(c => c == '.') != 3 || !IPAddress.TryParse(t, out var a) || a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return null;
        var b = a.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    public static string Tekst(uint getal) => $"{getal >> 24}.{(getal >> 16) & 255}.{(getal >> 8) & 255}.{getal & 255}";

    /// <summary>Ligt het adres in het bereik van de groep? Zonder (geldig) bereik: ja.</summary>
    public static bool InBereik(string ip, IpGroep? g)
        => g is null || Getal(g.Van) is not uint van || Getal(g.Tot) is not uint tot || (Getal(ip) is uint x && x >= van && x <= tot);

    /// <summary>Het eerste adres in het bereik van de groep dat nog niet in de lijst staat, of leeg.</summary>
    public static string EersteVrije(IpGroep g, IEnumerable<IpAdres> adressen)
    {
        if (Getal(g.Van) is not uint van || Getal(g.Tot) is not uint tot || tot < van || tot - van > 70000) return "";
        var bezet = adressen.Select(a => Getal(a.Ip)).OfType<uint>().ToHashSet();
        for (var x = van; x <= tot; x++)
        {
            if (!bezet.Contains(x) && (x & 255) is not (0 or 255)) return Tekst(x);
            if (x == uint.MaxValue) break;
        }
        return "";
    }

    private static readonly Regex Geheim = new(@"\b(pass|password|passwd|pwd|wachtwoord|ww|sleutel|pin|pincode|code|key|token)\b",
                                               RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Lijkt er in deze opmerking een wachtwoord, sleutel of code te staan? In Access stonden die soms in de opmerking
    /// ("Wifi pass …", "ww …", "Sleutel: …"). Zo'n opmerking is alleen voor Directie te lezen, net als het wachtwoord.
    /// Een vuistregel op woorden; een opmerking zonder zo'n woord wordt niet herkend.
    /// </summary>
    public static bool LijktGeheim(string? opmerking) => !string.IsNullOrEmpty(opmerking) && Geheim.IsMatch(opmerking);
}

public interface INetwerkRepository
{
    bool Beschikbaar { get; }
    Task<List<IpGroep>> GroepenAsync();
    /// <summary>Alle adressen, op adres gesorteerd. Zonder wachtwoorden.</summary>
    Task<List<IpAdres>> AdressenAsync();
    /// <summary>Het wachtwoord van één adres. Alleen aanroepen voor Directie; de aanroeper controleert dat.</summary>
    Task<string> WachtwoordAsync(int id);
    /// <summary>
    /// Bewaren (Id 0 = nieuw); geeft het id. <paramref name="wachtwoord"/>: null = het wachtwoord blijft zoals het is,
    /// anders wordt het vervangen (leeg = weg). Alleen Directie geeft hier iets anders dan null mee.
    /// </summary>
    Task<int> BewaarAdresAsync(IpAdres a, string? wachtwoord);
    Task VerwijderAdresAsync(int id);
    Task<int> BewaarGroepAsync(IpGroep g);
    /// <summary>Een groep weg; alleen als er geen adressen meer in staan. False = er staan nog adressen in.</summary>
    Task<bool> VerwijderGroepAsync(int id);
}

public class GeenNetwerkRepository : INetwerkRepository
{
    public bool Beschikbaar => false;
    public Task<List<IpGroep>> GroepenAsync() => Task.FromResult(new List<IpGroep>());
    public Task<List<IpAdres>> AdressenAsync() => Task.FromResult(new List<IpAdres>());
    public Task<string> WachtwoordAsync(int id) => Task.FromResult("");
    public Task<int> BewaarAdresAsync(IpAdres a, string? wachtwoord) => Task.FromResult(0);
    public Task VerwijderAdresAsync(int id) => Task.CompletedTask;
    public Task<int> BewaarGroepAsync(IpGroep g) => Task.FromResult(0);
    public Task<bool> VerwijderGroepAsync(int id) => Task.FromResult(false);
}

public class MySqlNetwerkRepository : INetwerkRepository
{
    private readonly string _cs;
    public MySqlNetwerkRepository(string cs) => _cs = cs;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static int? I(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static object Of(string s, int max) { s = s.Trim(); return s.Length == 0 ? DBNull.Value : s.Length <= max ? s : s[..max]; }

    public async Task<List<IpGroep>> GroepenAsync()
    {
        var lijst = new List<IpGroep>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `IPgroepID`, COALESCE(`Omschrijving`, ''), COALESCE(`FromRange`, ''), COALESCE(`ToRange`, '') FROM `IPgroep` " +
            " ORDER BY INET_ATON(`FromRange`), `Omschrijving`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new IpGroep(r.GetInt32(0), S(r, 1), S(r, 2), S(r, 3)));
        return lijst;
    }

    public async Task<List<IpAdres>> AdressenAsync()
    {
        var lijst = new List<IpAdres>();
        await using var cn = await OpenAsync();
        // het wachtwoord zelf komt hier bewust niet mee: alleen of er een is
        await using var cmd = new MySqlCommand(
            "SELECT `NetworkTopologieID`, COALESCE(`IPadres`, ''), COALESCE(`Omschrijving`, ''), `IPgroepID`, COALESCE(`LoginUser`, ''), " +
            "       COALESCE(`LoginPassword`, '') <> '', COALESCE(`DomainName`, ''), `WebserverPoort`, COALESCE(`Opmerking`, '') " +
            "  FROM `NetworkTopologie` ORDER BY INET_ATON(`IPadres`), `IPadres`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new IpAdres
            {
                Id = r.GetInt32(0), Ip = S(r, 1).Trim(), Omschrijving = S(r, 2), GroepId = I(r, 3), LoginUser = S(r, 4),
                HeeftWachtwoord = Convert.ToInt32(r.GetValue(5)) != 0, DomainName = S(r, 6), Poort = I(r, 7) is > 0 and var p ? p : null,
                Opmerking = S(r, 8),
            });
        return lijst;
    }

    public async Task<string> WachtwoordAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT COALESCE(`LoginPassword`, '') FROM `NetworkTopologie` WHERE `NetworkTopologieID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteScalarAsync() as string ?? "";
    }

    public async Task<int> BewaarAdresAsync(IpAdres a, string? wachtwoord)
    {
        await using var cn = await OpenAsync();
        var nieuw = a.Id == 0;
        await using var cmd = new MySqlCommand(nieuw
            ? "INSERT INTO `NetworkTopologie` (`IPadres`, `Omschrijving`, `IPgroepID`, `LoginUser`, `LoginPassword`, `DomainName`, `WebserverPoort`, `Opmerking`) " +
              "VALUES (@ip, @oms, @groep, @user, @ww, @domein, @poort, @opm); SELECT LAST_INSERT_ID();"
            : "UPDATE `NetworkTopologie` SET `IPadres` = @ip, `Omschrijving` = @oms, `IPgroepID` = @groep, `LoginUser` = @user, " +
              (wachtwoord is null ? "" : "`LoginPassword` = @ww, ") +
              "`DomainName` = @domein, `WebserverPoort` = @poort, `Opmerking` = @opm WHERE `NetworkTopologieID` = @id; SELECT @id;", cn);
        cmd.Parameters.AddWithValue("@id", a.Id);
        cmd.Parameters.AddWithValue("@ip", Of(a.Ip, 50));
        cmd.Parameters.AddWithValue("@oms", Of(a.Omschrijving, 200));
        cmd.Parameters.AddWithValue("@groep", (object?)a.GroepId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@user", Of(a.LoginUser, 50));
        cmd.Parameters.AddWithValue("@ww", Of(wachtwoord ?? "", 50));
        cmd.Parameters.AddWithValue("@domein", Of(a.DomainName, 50));
        cmd.Parameters.AddWithValue("@poort", (object?)a.Poort ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@opm", Of(a.Opmerking, 200));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task VerwijderAdresAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `NetworkTopologie` WHERE `NetworkTopologieID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> BewaarGroepAsync(IpGroep g)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(g.Id == 0
            ? "INSERT INTO `IPgroep` (`Omschrijving`, `FromRange`, `ToRange`) VALUES (@oms, @van, @tot); SELECT LAST_INSERT_ID();"
            : "UPDATE `IPgroep` SET `Omschrijving` = @oms, `FromRange` = @van, `ToRange` = @tot WHERE `IPgroepID` = @id; SELECT @id;", cn);
        cmd.Parameters.AddWithValue("@id", g.Id);
        cmd.Parameters.AddWithValue("@oms", Of(g.Omschrijving, 50));
        cmd.Parameters.AddWithValue("@van", Of(g.Van, 50));
        cmd.Parameters.AddWithValue("@tot", Of(g.Tot, 50));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<bool> VerwijderGroepAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `IPgroep` WHERE `IPgroepID` = @id AND NOT EXISTS (SELECT 1 FROM `NetworkTopologie` WHERE `IPgroepID` = @id)", cn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }
}

/// <summary>
/// Lokaal testen zonder in de database te schrijven (Netwerk__Geheugen=true): de lijst wordt één keer echt gelezen
/// (zonder wachtwoorden) en daarna alleen in het geheugen bijgewerkt. Een wachtwoord tonen geeft een neptekst.
/// </summary>
public class GeheugenNetwerkRepository : INetwerkRepository
{
    private readonly INetwerkRepository _echt;
    private readonly object _slot = new();
    private List<IpGroep>? _groepen;
    private List<IpAdres>? _adressen;
    private readonly Dictionary<int, string> _ww = new();
    private int _volgende = 900_000;

    public GeheugenNetwerkRepository(INetwerkRepository echt) => _echt = echt;
    public bool Beschikbaar => true;

    private async Task LaadAsync()
    {
        if (_groepen is not null) return;
        var g = await _echt.GroepenAsync();
        var a = await _echt.AdressenAsync();
        lock (_slot) { _groepen ??= g; _adressen ??= a; }
    }

    public async Task<List<IpGroep>> GroepenAsync() { await LaadAsync(); lock (_slot) return _groepen!.ToList(); }
    public async Task<List<IpAdres>> AdressenAsync()
    {
        await LaadAsync();
        lock (_slot) return _adressen!.OrderBy(a => Netwerk.Getal(a.Ip) ?? uint.MaxValue).Select(a => a.Kopie()).ToList();
    }

    public Task<string> WachtwoordAsync(int id)
    {
        lock (_slot) return Task.FromResult(_ww.TryGetValue(id, out var w) ? w : "(proef: niet uit de database gelezen)");
    }

    public async Task<int> BewaarAdresAsync(IpAdres a, string? wachtwoord)
    {
        await LaadAsync();
        lock (_slot)
        {
            var k = a.Kopie();
            if (k.Id == 0) k.Id = _volgende++; else _adressen!.RemoveAll(x => x.Id == k.Id);
            if (wachtwoord is not null) { _ww[k.Id] = wachtwoord; k.HeeftWachtwoord = wachtwoord.Length > 0; }
            _adressen!.Add(k);
            return k.Id;
        }
    }

    public async Task VerwijderAdresAsync(int id) { await LaadAsync(); lock (_slot) _adressen!.RemoveAll(x => x.Id == id); }

    public async Task<int> BewaarGroepAsync(IpGroep g)
    {
        await LaadAsync();
        lock (_slot)
        {
            if (g.Id == 0) g = g with { Id = _volgende++ }; else _groepen!.RemoveAll(x => x.Id == g.Id);
            _groepen!.Add(g);
            return g.Id;
        }
    }

    public async Task<bool> VerwijderGroepAsync(int id)
    {
        await LaadAsync();
        lock (_slot) return !_adressen!.Any(a => a.GroepId == id) && _groepen!.RemoveAll(x => x.Id == id) > 0;
    }
}
