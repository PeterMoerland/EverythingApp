using System.Collections.Concurrent;
using System.Security.Cryptography;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Een werkplek: pc waar meerdere mensen aan werken (Werkplekken_MariaDB.sql). Geen Google-account; rechten en de naam
/// in de historie komen van <see cref="WerknemerId"/> (een niet-persoon in Werknemers).
/// </summary>
public class Werkplek
{
    public int Id { get; set; }
    public string Naam { get; set; } = "";
    public int WerknemerId { get; set; }
    public bool Productie { get; set; } = true;
    public string Startpagina { get; set; } = "";
    public string MailAfzender { get; set; } = "";
    public string MailAccount { get; set; } = "";
    public bool Actief { get; set; } = true;
    public string KoppelCode { get; set; } = "";
    public DateTime? KoppelCodeTot { get; set; }

    public Werkplek Kopie() => (Werkplek)MemberwiseClone();
}

public record WerkplekApparaat(int Id, int WerkplekId, string Omschrijving, DateTime GekoppeldOp, DateTime? LaatstGezien);

public interface IWerkplekRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabellenAanwezigAsync();
    Task<List<Werkplek>> LijstAsync();
    Task<Werkplek?> GetAsync(int id);
    /// <summary>Nieuw (Id 0) of bijwerken; geeft het id.</summary>
    Task<int> BewaarAsync(Werkplek w);
    Task<List<WerkplekApparaat>> ApparatenAsync(int werkplekId);
    /// <summary>Nieuwe koppelcode, 30 minuten geldig; een oude vervalt.</summary>
    Task<(string Code, DateTime Tot)> MaakKoppelCodeAsync(int werkplekId);
    /// <summary>Code inwisselen: een nieuw apparaat bij die werkplek (de code vervalt). Null = code onbekend of verlopen.</summary>
    Task<(Werkplek Werkplek, int ApparaatId)?> KoppelAsync(string code, string omschrijving);
    /// <summary>Hoort dit apparaat nog bij een actieve werkplek? Werkt ook 'laatst gezien' bij. Null = ontkoppeld.</summary>
    Task<Werkplek?> ControleerApparaatAsync(int apparaatId);
    Task OntkoppelAsync(int apparaatId);
    /// <summary>Werknemers die geen persoon zijn (Persoon = nee): de keuze voor een werkplek.</summary>
    Task<List<Keuze>> NietPersonenAsync();

    // ---- wie werkt er aan de pc ("Wie ben jij?" + pincode) ----
    /// <summary>
    /// De keuze bij "Wie ben jij?": personen in dienst met het vinkje Werkplek gebruiker (Werknemers ▸ Status). Zolang
    /// die kolom er nog niet is: alle personen in dienst.
    /// </summary>
    Task<List<Keuze>> PersonenAsync();
    /// <summary>Wie er op dit apparaat werkt en wanneer er voor het laatst iets gebeurde; null = niemand.</summary>
    Task<(int MedewerkerId, DateTime LaatsteActiviteit)?> MedewerkerAsync(int apparaatId);
    /// <summary>Iemand aanmelden (met nu als laatste activiteit) of afmelden (null).</summary>
    Task ZetMedewerkerAsync(int apparaatId, int? werknemerId);
    /// <summary>Er gebeurt iets op de pc: de minuten tot afmelden beginnen opnieuw.</summary>
    Task ActiviteitAsync(int apparaatId);
    /// <summary>
    /// Na hoeveel minuten zonder activiteit deze werknemer op een werkplek weer afgemeld wordt (Werknemers.WerkplekMinuten,
    /// Werknemers_WerkplekMinuten_MariaDB.sql); null = de standaard <see cref="Pincode.Duur"/>.
    /// </summary>
    Task<int?> MinutenAsync(int werknemerId);
    Task<bool> HeeftPincodeAsync(int werknemerId);
    /// <summary>Opgeslagen hash, of null als er geen pincode is.</summary>
    Task<string?> PincodeHashAsync(int werknemerId);
    Task ZetPincodeAsync(int werknemerId, string pincode);
    Task WisPincodeAsync(int werknemerId);
    /// <summary>Wie heeft er een pincode (voor Beheer).</summary>
    Task<List<Keuze>> MetPincodeAsync();
}

/// <summary>
/// Pincodes van 4 cijfers: opgeslagen als PBKDF2-SHA256 met zout (nooit de code zelf). Na 5 foute pogingen voor een
/// werknemer 5 minuten geen nieuwe poging (in het geheugen van de server), tegen raden.
/// </summary>
public static class Pincode
{
    public const int Duur = 30;      // minuten zonder activiteit tot er weer niemand aan de pc werkt (standaard)

    /// <summary>De duur voor een werknemer: eigen instelling (1 tot 720 minuten) of de standaard.</summary>
    public static int DuurVan(int? minuten) => minuten is int m && m > 0 ? Math.Min(m, 720) : Duur;

    public static bool Geldig(string? pin) => pin is { Length: 4 } && pin.All(char.IsAsciiDigit);

    public static string Hash(string pin)
    {
        var zout = RandomNumberGenerator.GetBytes(16);
        var h = Rfc2898DeriveBytes.Pbkdf2(pin, zout, 100_000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2$100000${Convert.ToBase64String(zout)}${Convert.ToBase64String(h)}";
    }

    public static bool Klopt(string pin, string? opgeslagen)
    {
        var d = (opgeslagen ?? "").Split('$');
        if (d.Length != 4 || d[0] != "pbkdf2" || !int.TryParse(d[1], out var n)) return false;
        try
        {
            var h = Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(d[2]), n, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(h, Convert.FromBase64String(d[3]));
        }
        catch { return false; }
    }

    private static readonly ConcurrentDictionary<int, (int Fout, DateTime Tot)> _pogingen = new();

    /// <summary>Hoe lang deze werknemer nog moet wachten na te veel foute pogingen; null = mag proberen.</summary>
    public static TimeSpan? Geblokkeerd(int werknemerId)
        => _pogingen.TryGetValue(werknemerId, out var p) && p.Fout >= 5 && p.Tot > DateTime.UtcNow ? p.Tot - DateTime.UtcNow : null;

    public static void Fout(int werknemerId)
        => _pogingen.AddOrUpdate(werknemerId, _ => (1, DateTime.UtcNow.AddMinutes(5)),
                                 (_, p) => (p.Tot < DateTime.UtcNow ? 1 : p.Fout + 1, DateTime.UtcNow.AddMinutes(5)));

    public static void Goed(int werknemerId) => _pogingen.TryRemove(werknemerId, out _);
}

/// <summary>Gemeenschappelijk: de koppelcode (zonder letters die op elkaar lijken).</summary>
public static class WerkplekCode
{
    private const string Tekens = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public static string Nieuw() => new(Enumerable.Range(0, 6).Select(_ => Tekens[RandomNumberGenerator.GetInt32(Tekens.Length)]).ToArray());
    public static string Schoon(string? code) => new((code ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}

public class GeenWerkplekRepository : IWerkplekRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabellenAanwezigAsync() => Task.FromResult(false);
    public Task<List<Werkplek>> LijstAsync() => Task.FromResult(new List<Werkplek>());
    public Task<Werkplek?> GetAsync(int id) => Task.FromResult<Werkplek?>(null);
    public Task<int> BewaarAsync(Werkplek w) => Task.FromResult(0);
    public Task<List<WerkplekApparaat>> ApparatenAsync(int werkplekId) => Task.FromResult(new List<WerkplekApparaat>());
    public Task<(string Code, DateTime Tot)> MaakKoppelCodeAsync(int werkplekId) => Task.FromResult(("", DateTime.Now));
    public Task<(Werkplek Werkplek, int ApparaatId)?> KoppelAsync(string code, string omschrijving) => Task.FromResult<(Werkplek, int)?>(null);
    public Task<Werkplek?> ControleerApparaatAsync(int apparaatId) => Task.FromResult<Werkplek?>(null);
    public Task OntkoppelAsync(int apparaatId) => Task.CompletedTask;
    public Task<List<Keuze>> NietPersonenAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> PersonenAsync() => Task.FromResult(new List<Keuze>());
    public Task<(int MedewerkerId, DateTime LaatsteActiviteit)?> MedewerkerAsync(int apparaatId) => Task.FromResult<(int, DateTime)?>(null);
    public Task ZetMedewerkerAsync(int apparaatId, int? werknemerId) => Task.CompletedTask;
    public Task ActiviteitAsync(int apparaatId) => Task.CompletedTask;
    public Task<bool> HeeftPincodeAsync(int werknemerId) => Task.FromResult(false);
    public Task<string?> PincodeHashAsync(int werknemerId) => Task.FromResult<string?>(null);
    public Task ZetPincodeAsync(int werknemerId, string pincode) => Task.CompletedTask;
    public Task WisPincodeAsync(int werknemerId) => Task.CompletedTask;
    public Task<List<Keuze>> MetPincodeAsync() => Task.FromResult(new List<Keuze>());
    public Task<int?> MinutenAsync(int werknemerId) => Task.FromResult<int?>(null);
}

/// <summary>In het geheugen, alleen om lokaal te testen (Werkplekken:Geheugen = true).</summary>
public class GeheugenWerkplekRepository : IWerkplekRepository
{
    private readonly object _slot = new();
    private readonly List<Werkplek> _werkplekken = new();
    private readonly List<WerkplekApparaat> _apparaten = new();
    private int _volgend;
    public bool Beschikbaar => true;
    public Task<bool> TabellenAanwezigAsync() => Task.FromResult(true);
    public Task<List<Werkplek>> LijstAsync() { lock (_slot) return Task.FromResult(_werkplekken.Select(w => w.Kopie()).OrderBy(w => w.Naam).ToList()); }
    public Task<Werkplek?> GetAsync(int id) { lock (_slot) return Task.FromResult(_werkplekken.FirstOrDefault(w => w.Id == id)?.Kopie()); }

    public Task<int> BewaarAsync(Werkplek w)
    {
        lock (_slot)
        {
            if (_werkplekken.Any(x => x.Id != w.Id && x.Naam.Equals(w.Naam, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Er is al een werkplek met deze naam.");
            var k = w.Kopie();
            if (k.Id == 0) { k.Id = ++_volgend; _werkplekken.Add(k); }
            else
            {
                var i = _werkplekken.FindIndex(x => x.Id == k.Id);
                k.KoppelCode = _werkplekken[i].KoppelCode; k.KoppelCodeTot = _werkplekken[i].KoppelCodeTot;
                _werkplekken[i] = k;
            }
            return Task.FromResult(k.Id);
        }
    }

    public Task<List<WerkplekApparaat>> ApparatenAsync(int werkplekId) { lock (_slot) return Task.FromResult(_apparaten.Where(a => a.WerkplekId == werkplekId).ToList()); }

    public Task<(string Code, DateTime Tot)> MaakKoppelCodeAsync(int werkplekId)
    {
        lock (_slot)
        {
            var w = _werkplekken.First(x => x.Id == werkplekId);
            w.KoppelCode = WerkplekCode.Nieuw(); w.KoppelCodeTot = DateTime.Now.AddMinutes(30);
            return Task.FromResult((w.KoppelCode, w.KoppelCodeTot.Value));
        }
    }

    public Task<(Werkplek Werkplek, int ApparaatId)?> KoppelAsync(string code, string omschrijving)
    {
        lock (_slot)
        {
            code = WerkplekCode.Schoon(code);
            var w = _werkplekken.FirstOrDefault(x => x.Actief && x.KoppelCode.Length > 0 && x.KoppelCode == code && x.KoppelCodeTot > DateTime.Now);
            if (w is null) return Task.FromResult<(Werkplek, int)?>(null);
            w.KoppelCode = ""; w.KoppelCodeTot = null;
            var a = new WerkplekApparaat(++_volgend, w.Id, omschrijving, DateTime.Now, DateTime.Now);
            _apparaten.Add(a);
            return Task.FromResult<(Werkplek, int)?>((w.Kopie(), a.Id));
        }
    }

    public Task<Werkplek?> ControleerApparaatAsync(int apparaatId)
    {
        lock (_slot)
        {
            var a = _apparaten.FirstOrDefault(x => x.Id == apparaatId);
            var w = a is null ? null : _werkplekken.FirstOrDefault(x => x.Id == a.WerkplekId && x.Actief);
            return Task.FromResult(w?.Kopie());
        }
    }

    public Task OntkoppelAsync(int apparaatId) { lock (_slot) _apparaten.RemoveAll(a => a.Id == apparaatId); return Task.CompletedTask; }

    public Task<List<Keuze>> NietPersonenAsync()
        => Task.FromResult(new List<Keuze> { new(9001, "Magazijn Boven"), new(9002, "Robotplek"), new(9003, "MDE Server") });

    private static readonly List<Keuze> _personen = new()
    {
        new(1, "Arian Duine"), new(2, "Niek Verschuure"), new(3, "Freek Moerland"), new(4, "Edwin Denissen"),
        new(5, "Kees Blok"),
    };
    private readonly Dictionary<int, (int Id, DateTime Laatst)> _medewerkers = new();
    // alleen voor lokaal testen: Kees Blok heeft testpincode 2580
    private readonly Dictionary<int, string> _pincodes = new() { [5] = Pincode.Hash("2580") };

    public Task<List<Keuze>> PersonenAsync() => Task.FromResult(_personen.ToList());

    public Task<(int MedewerkerId, DateTime LaatsteActiviteit)?> MedewerkerAsync(int apparaatId)
    { lock (_slot) return Task.FromResult<(int, DateTime)?>(_medewerkers.TryGetValue(apparaatId, out var m) ? (m.Id, m.Laatst) : null); }

    public Task ZetMedewerkerAsync(int apparaatId, int? werknemerId)
    {
        lock (_slot) { if (werknemerId is int w) _medewerkers[apparaatId] = (w, DateTime.Now); else _medewerkers.Remove(apparaatId); }
        return Task.CompletedTask;
    }

    public Task ActiviteitAsync(int apparaatId)
    {
        lock (_slot) if (_medewerkers.TryGetValue(apparaatId, out var m)) _medewerkers[apparaatId] = (m.Id, DateTime.Now);
        return Task.CompletedTask;
    }

    public Task<bool> HeeftPincodeAsync(int werknemerId) { lock (_slot) return Task.FromResult(_pincodes.ContainsKey(werknemerId)); }
    public Task<string?> PincodeHashAsync(int werknemerId) { lock (_slot) return Task.FromResult(_pincodes.GetValueOrDefault(werknemerId)); }
    public Task ZetPincodeAsync(int werknemerId, string pincode) { lock (_slot) _pincodes[werknemerId] = Pincode.Hash(pincode); return Task.CompletedTask; }
    public Task WisPincodeAsync(int werknemerId) { lock (_slot) _pincodes.Remove(werknemerId); return Task.CompletedTask; }
    public Task<List<Keuze>> MetPincodeAsync() { lock (_slot) return Task.FromResult(_personen.Where(p => _pincodes.ContainsKey(p.Id)).ToList()); }
    public Task<int?> MinutenAsync(int werknemerId) => Task.FromResult<int?>(null);
}

public class MySqlWerkplekRepository : IWerkplekRepository
{
    private readonly string _cs;
    private bool _tabellen;
    private bool _heeftMinuten;    // kolom Werknemers.WerkplekMinuten gezien (alleen "ja" onthouden: werkt meteen na het script)
    public MySqlWerkplekRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";

    public async Task<bool> TabellenAanwezigAsync()
    {
        if (_tabellen) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() " +
                "          AND TABLE_NAME IN ('Werkplekken', 'WerkplekApparaten', 'WerknemerPincodes')) " +
                "     + (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "          AND TABLE_NAME = 'WerkplekApparaten' AND COLUMN_NAME = 'LaatsteActiviteit')", cn);
            // 3 tabellen + de kolommen van "wie werkt er" (die kwamen later; een oudere versie van het script had ze niet)
            _tabellen = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 4;
        }
        catch { }
        return _tabellen;
    }

    private const string Kolommen =
        "`WerkplekID`, `Naam`, `WerknemerID`, `Productie`, `Startpagina`, `MailAfzender`, `MailAccount`, `Actief`, `KoppelCode`, `KoppelCodeTot`";

    private static Werkplek Lees(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0), Naam = S(r, 1), WerknemerId = r.GetInt32(2), Productie = Convert.ToInt32(r.GetValue(3)) != 0,
        Startpagina = S(r, 4), MailAfzender = S(r, 5), MailAccount = S(r, 6), Actief = Convert.ToInt32(r.GetValue(7)) != 0,
        KoppelCode = S(r, 8), KoppelCodeTot = r.IsDBNull(9) ? null : Convert.ToDateTime(r.GetValue(9)),
    };

    public async Task<List<Werkplek>> LijstAsync()
    {
        var lijst = new List<Werkplek>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand($"SELECT {Kolommen} FROM `Werkplekken` ORDER BY `Naam`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(Lees(r));
        return lijst;
    }

    public async Task<Werkplek?> GetAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand($"SELECT {Kolommen} FROM `Werkplekken` WHERE `WerkplekID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Lees(r) : null;
    }

    public async Task<int> BewaarAsync(Werkplek w)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(w.Id == 0
            ? "INSERT INTO `Werkplekken` (`Naam`, `WerknemerID`, `Productie`, `Startpagina`, `MailAfzender`, `MailAccount`, `Actief`, `AangemaaktOp`) " +
              "VALUES (@n, @wn, @p, @s, @ma, @mc, @a, NOW()); SELECT LAST_INSERT_ID();"
            : "UPDATE `Werkplekken` SET `Naam` = @n, `WerknemerID` = @wn, `Productie` = @p, `Startpagina` = @s, `MailAfzender` = @ma, " +
              "`MailAccount` = @mc, `Actief` = @a WHERE `WerkplekID` = @id; SELECT @id;", cn);
        cmd.Parameters.AddWithValue("@id", w.Id);
        cmd.Parameters.AddWithValue("@n", w.Naam.Trim());
        cmd.Parameters.AddWithValue("@wn", w.WerknemerId);
        cmd.Parameters.AddWithValue("@p", w.Productie ? 1 : 0);
        cmd.Parameters.AddWithValue("@s", w.Startpagina.Trim().Length > 0 ? w.Startpagina.Trim() : DBNull.Value);
        cmd.Parameters.AddWithValue("@ma", w.MailAfzender.Trim().Length > 0 ? w.MailAfzender.Trim() : DBNull.Value);
        cmd.Parameters.AddWithValue("@mc", w.MailAccount.Trim().Length > 0 ? w.MailAccount.Trim() : DBNull.Value);
        cmd.Parameters.AddWithValue("@a", w.Actief ? 1 : 0);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<List<WerkplekApparaat>> ApparatenAsync(int werkplekId)
    {
        var lijst = new List<WerkplekApparaat>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ApparaatID`, `WerkplekID`, `Omschrijving`, `GekoppeldOp`, `LaatstGezien` FROM `WerkplekApparaten` " +
            " WHERE `WerkplekID` = @w ORDER BY `GekoppeldOp` DESC", cn);
        cmd.Parameters.AddWithValue("@w", werkplekId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new WerkplekApparaat(r.GetInt32(0), r.GetInt32(1), S(r, 2), Convert.ToDateTime(r.GetValue(3)),
                                           r.IsDBNull(4) ? null : Convert.ToDateTime(r.GetValue(4))));
        return lijst;
    }

    public async Task<(string Code, DateTime Tot)> MaakKoppelCodeAsync(int werkplekId)
    {
        var code = WerkplekCode.Nieuw();
        var tot = DateTime.Now.AddMinutes(30);
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Werkplekken` SET `KoppelCode` = @c, `KoppelCodeTot` = @t WHERE `WerkplekID` = @w", cn);
        cmd.Parameters.AddWithValue("@c", code);
        cmd.Parameters.AddWithValue("@t", tot);
        cmd.Parameters.AddWithValue("@w", werkplekId);
        await cmd.ExecuteNonQueryAsync();
        return (code, tot);
    }

    public async Task<(Werkplek Werkplek, int ApparaatId)?> KoppelAsync(string code, string omschrijving)
    {
        code = WerkplekCode.Schoon(code);
        if (code.Length != 6) return null;
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        Werkplek? w;
        await using (var zoek = new MySqlCommand(
            $"SELECT {Kolommen} FROM `Werkplekken` WHERE `KoppelCode` = @c AND `KoppelCodeTot` > NOW() AND `Actief` = 1 FOR UPDATE", cn, tx))
        {
            zoek.Parameters.AddWithValue("@c", code);
            await using var r = await zoek.ExecuteReaderAsync();
            w = await r.ReadAsync() ? Lees(r) : null;
        }
        if (w is null) return null;
        await using (var weg = new MySqlCommand(
            "UPDATE `Werkplekken` SET `KoppelCode` = NULL, `KoppelCodeTot` = NULL WHERE `WerkplekID` = @w", cn, tx))
        {
            weg.Parameters.AddWithValue("@w", w.Id);
            await weg.ExecuteNonQueryAsync();
        }
        int id;
        await using (var nieuw = new MySqlCommand(
            "INSERT INTO `WerkplekApparaten` (`WerkplekID`, `Omschrijving`, `GekoppeldOp`, `LaatstGezien`) VALUES (@w, @o, NOW(), NOW()); " +
            "SELECT LAST_INSERT_ID();", cn, tx))
        {
            nieuw.Parameters.AddWithValue("@w", w.Id);
            nieuw.Parameters.AddWithValue("@o", omschrijving.Length > 250 ? omschrijving[..250] : omschrijving);
            id = Convert.ToInt32(await nieuw.ExecuteScalarAsync());
        }
        await tx.CommitAsync();
        return (w, id);
    }

    public async Task<Werkplek?> ControleerApparaatAsync(int apparaatId)
    {
        await using var cn = await OpenAsync();
        Werkplek? w;
        await using (var cmd = new MySqlCommand(
            $"SELECT {string.Join(", ", Kolommen.Split(", ").Select(k => "w." + k))} FROM `WerkplekApparaten` a " +
            "  JOIN `Werkplekken` w ON w.`WerkplekID` = a.`WerkplekID` WHERE a.`ApparaatID` = @a AND w.`Actief` = 1", cn))
        {
            cmd.Parameters.AddWithValue("@a", apparaatId);
            await using var r = await cmd.ExecuteReaderAsync();
            w = await r.ReadAsync() ? Lees(r) : null;
        }
        if (w is not null)
        {
            await using var gezien = new MySqlCommand(
                "UPDATE `WerkplekApparaten` SET `LaatstGezien` = NOW() WHERE `ApparaatID` = @a", cn);
            gezien.Parameters.AddWithValue("@a", apparaatId);
            await gezien.ExecuteNonQueryAsync();
        }
        return w;
    }

    public async Task OntkoppelAsync(int apparaatId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `WerkplekApparaten` WHERE `ApparaatID` = @a", cn);
        cmd.Parameters.AddWithValue("@a", apparaatId);
        await cmd.ExecuteNonQueryAsync();
    }

    private bool _kolomWerkplekgebruiker;

    public async Task<List<Keuze>> PersonenAsync()
    {
        var lijst = new List<Keuze>();
        await using var cn = await OpenAsync();
        if (!_kolomWerkplekgebruiker)
        {
            await using var k = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "   AND TABLE_NAME = 'Werknemers' AND COLUMN_NAME = 'Werkplekgebruiker'", cn);
            _kolomWerkplekgebruiker = Convert.ToInt32(await k.ExecuteScalarAsync()) > 0;
        }
        await using var cmd = new MySqlCommand(
            "SELECT `Werknemer-id`, COALESCE(`NaamVoluit`, CONCAT_WS(' ', `Voornaam`, `Achternaam`)) AS naam FROM `Werknemers` " +
            " WHERE COALESCE(`Persoon`, 0) <> 0 AND COALESCE(`Uit dienst`, 0) = 0" +
            (_kolomWerkplekgebruiker ? " AND COALESCE(`Werkplekgebruiker`, 0) <> 0" : "") + " ORDER BY naam", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new Keuze(r.GetInt32(0), S(r, 1) is { Length: > 0 } n ? n : "(geen naam)"));
        return lijst;
    }

    public async Task<(int MedewerkerId, DateTime LaatsteActiviteit)?> MedewerkerAsync(int apparaatId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `MedewerkerID`, COALESCE(`LaatsteActiviteit`, `MedewerkerSinds`) FROM `WerkplekApparaten` " +
            " WHERE `ApparaatID` = @a AND `MedewerkerID` IS NOT NULL", cn);
        cmd.Parameters.AddWithValue("@a", apparaatId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync() || r.IsDBNull(1)) return null;
        return (r.GetInt32(0), Convert.ToDateTime(r.GetValue(1)));
    }

    public async Task ZetMedewerkerAsync(int apparaatId, int? werknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `WerkplekApparaten` SET `MedewerkerID` = @w, `MedewerkerSinds` = IF(@w IS NULL, NULL, NOW()), " +
            "       `LaatsteActiviteit` = IF(@w IS NULL, NULL, NOW()) WHERE `ApparaatID` = @a", cn);
        cmd.Parameters.AddWithValue("@w", (object?)werknemerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@a", apparaatId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ActiviteitAsync(int apparaatId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `WerkplekApparaten` SET `LaatsteActiviteit` = NOW() WHERE `ApparaatID` = @a AND `MedewerkerID` IS NOT NULL", cn);
        cmd.Parameters.AddWithValue("@a", apparaatId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> HeeftPincodeAsync(int werknemerId) => await PincodeHashAsync(werknemerId) is not null;

    public async Task<string?> PincodeHashAsync(int werknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT `Hash` FROM `WerknemerPincodes` WHERE `WerknemerID` = @w", cn);
        cmd.Parameters.AddWithValue("@w", werknemerId);
        return await cmd.ExecuteScalarAsync() is string h ? h : null;
    }

    public async Task ZetPincodeAsync(int werknemerId, string pincode)
    {
        if (!Pincode.Geldig(pincode)) throw new ArgumentException("Een pincode is 4 cijfers.");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `WerknemerPincodes` (`WerknemerID`, `Hash`, `Gewijzigd`) VALUES (@w, @h, NOW()) " +
            "ON DUPLICATE KEY UPDATE `Hash` = VALUES(`Hash`), `Gewijzigd` = NOW()", cn);
        cmd.Parameters.AddWithValue("@w", werknemerId);
        cmd.Parameters.AddWithValue("@h", Pincode.Hash(pincode));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task WisPincodeAsync(int werknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `WerknemerPincodes` WHERE `WerknemerID` = @w", cn);
        cmd.Parameters.AddWithValue("@w", werknemerId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int?> MinutenAsync(int werknemerId)
    {
        await using var cn = await OpenAsync();
        if (!_heeftMinuten)
        {
            await using var k = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "   AND TABLE_NAME = 'Werknemers' AND COLUMN_NAME = 'WerkplekMinuten'", cn);
            _heeftMinuten = Convert.ToInt32(await k.ExecuteScalarAsync()) > 0;
            if (!_heeftMinuten) return null;
        }
        await using var cmd = new MySqlCommand("SELECT `WerkplekMinuten` FROM `Werknemers` WHERE `Werknemer-id` = @w", cn);
        cmd.Parameters.AddWithValue("@w", werknemerId);
        var v = await cmd.ExecuteScalarAsync();
        return v is null or DBNull ? null : Convert.ToInt32(v);
    }

    public async Task<List<Keuze>> MetPincodeAsync()
    {
        var lijst = new List<Keuze>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT p.`WerknemerID`, COALESCE(w.`NaamVoluit`, CONCAT_WS(' ', w.`Voornaam`, w.`Achternaam`), CONCAT('#', p.`WerknemerID`)) AS naam " +
            "  FROM `WerknemerPincodes` p LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = p.`WerknemerID` ORDER BY naam", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new Keuze(r.GetInt32(0), S(r, 1)));
        return lijst;
    }

    public async Task<List<Keuze>> NietPersonenAsync()
    {
        var lijst = new List<Keuze>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Werknemer-id`, COALESCE(`NaamVoluit`, CONCAT_WS(' ', `Voornaam`, `Achternaam`)) AS naam FROM `Werknemers` " +
            " WHERE COALESCE(`Persoon`, 0) = 0 ORDER BY naam", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new Keuze(r.GetInt32(0), S(r, 1) is { Length: > 0 } n ? n : "(geen naam)"));
        return lijst;
    }
}
