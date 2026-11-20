using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een soort melding die een werknemer zelf aan of uit kan zetten (Werknemers ▸ tabblad Meldingen).</summary>
/// <param name="Code">De waarde van Meldingen.Soort waar het om gaat.</param>
/// <param name="Standaard">Wat geldt zolang de werknemer niets gekozen heeft.</param>
public record MeldingSoort(string Code, string Naam, string Uitleg, bool Standaard = true);

/// <summary>
/// De meldingen waar je zelf over beslist (wens gebruiker 19-11-2026: "de optie om meldingen te krijgen op diverse
/// gebieden; de eerste is Activity board toewijzing"). Een soort die hier niet staat, wordt altijd verstuurd.
/// Een soort erbij = een regel hier; het tabblad en de controle bij het versturen volgen vanzelf.
/// </summary>
public static class MeldingSoorten
{
    public static readonly MeldingSoort[] Instelbaar =
    {
        new("activiteit", "Activity board toewijzing",
            "Een collega wijst je toe aan een activiteit op een Activity board, of maakt er een voor je aan."),
        // staat standaard uit: alleen wie het aanzet hoort wat de wekelijkse taak deed (SolarPrijsWaker)
        new(SolarPrijsWaker.MeldingSoort, "Solar-prijzen bijgewerkt",
            "Na de wekelijkse controle van de Solar-prijzen: hoeveel prijzen zijn bijgewerkt en hoeveel er na te kijken zijn.", Standaard: false),
    };

    public static MeldingSoort? Van(string code) => Instelbaar.FirstOrDefault(s => s.Code == code);
}

/// <summary>
/// De keuzes per werknemer (tabel MeldingVoorkeuren, MeldingVoorkeuren_MariaDB.sql). Alleen wat iemand zelf heeft
/// gekozen staat erin; de rest volgt <see cref="MeldingSoort.Standaard"/>. Zonder tabel geldt overal de standaard.
/// </summary>
public interface IMeldingVoorkeurRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();
    /// <summary>De eigen keuzes van deze werknemer: soort → aan/uit.</summary>
    Task<Dictionary<string, bool>> GetAsync(int werknemerId);
    Task ZetAsync(int werknemerId, string soort, bool aan);
    /// <summary>
    /// Wil deze werknemer deze soort melding? Een soort die niet instelbaar is: altijd ja. Lukt het opzoeken niet, dan
    /// de standaard: een storing hier mag geen melding tegenhouden.
    /// </summary>
    Task<bool> WilAsync(int werknemerId, string soort);
    /// <summary>
    /// Wie deze soort uitdrukkelijk heeft aangezet. Voor een soort die standaard uit staat is dat de lijst van
    /// ontvangers (een melding aan iedereen maken en laten wegfilteren zou voor elke werknemer een vraag kosten).
    /// </summary>
    Task<List<int>> WieWilAsync(string soort);
}

public class GeenMeldingVoorkeurRepository : IMeldingVoorkeurRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<Dictionary<string, bool>> GetAsync(int werknemerId) => Task.FromResult(new Dictionary<string, bool>());
    public Task ZetAsync(int werknemerId, string soort, bool aan) => Task.CompletedTask;
    public Task<bool> WilAsync(int werknemerId, string soort) => Task.FromResult(MeldingSoorten.Van(soort)?.Standaard ?? true);
    public Task<List<int>> WieWilAsync(string soort) => Task.FromResult(new List<int>());
}

/// <summary>In het geheugen, alleen om het tabblad lokaal te proberen (MeldingVoorkeuren:Geheugen = true); weg na een herstart.</summary>
public class GeheugenMeldingVoorkeurRepository : IMeldingVoorkeurRepository
{
    private readonly Dictionary<(int, string), bool> _keuzes = new();
    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);

    public Task<Dictionary<string, bool>> GetAsync(int werknemerId)
    {
        lock (_keuzes) return Task.FromResult(_keuzes.Where(k => k.Key.Item1 == werknemerId).ToDictionary(k => k.Key.Item2, k => k.Value));
    }

    public Task ZetAsync(int werknemerId, string soort, bool aan)
    {
        lock (_keuzes) _keuzes[(werknemerId, soort)] = aan;
        return Task.CompletedTask;
    }

    public Task<bool> WilAsync(int werknemerId, string soort)
    {
        if (MeldingSoorten.Van(soort) is not { } s) return Task.FromResult(true);
        lock (_keuzes) return Task.FromResult(_keuzes.TryGetValue((werknemerId, soort), out var aan) ? aan : s.Standaard);
    }

    public Task<List<int>> WieWilAsync(string soort)
    {
        lock (_keuzes) return Task.FromResult(_keuzes.Where(k => k.Key.Item2 == soort && k.Value).Select(k => k.Key.Item1).ToList());
    }
}

public class MySqlMeldingVoorkeurRepository : IMeldingVoorkeurRepository
{
    private readonly string _cs;
    private bool _tabel;
    public MySqlMeldingVoorkeurRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'MeldingVoorkeuren'", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _tabel;
    }

    public async Task<Dictionary<string, bool>> GetAsync(int werknemerId)
    {
        var uit = new Dictionary<string, bool>();
        if (!await TabelAanwezigAsync()) return uit;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT `Soort`, `Aan` FROM `MeldingVoorkeuren` WHERE `WerknemerID` = @wn", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit[r.GetString(0)] = Convert.ToInt32(r.GetValue(1)) != 0;
        return uit;
    }

    public async Task ZetAsync(int werknemerId, string soort, bool aan)
    {
        if (MeldingSoorten.Van(soort) is null) throw new ArgumentException($"Onbekende soort melding {soort}");
        if (!await TabelAanwezigAsync()) throw new InvalidOperationException("De tabel MeldingVoorkeuren ontbreekt (MeldingVoorkeuren_MariaDB.sql).");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `MeldingVoorkeuren` (`WerknemerID`, `Soort`, `Aan`) VALUES (@wn, @s, @a) " +
            "ON DUPLICATE KEY UPDATE `Aan` = @a", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        cmd.Parameters.AddWithValue("@s", soort);
        cmd.Parameters.AddWithValue("@a", aan ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> WilAsync(int werknemerId, string soort)
    {
        if (MeldingSoorten.Van(soort) is not { } s) return true;
        try
        {
            if (!await TabelAanwezigAsync()) return s.Standaard;
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand("SELECT `Aan` FROM `MeldingVoorkeuren` WHERE `WerknemerID` = @wn AND `Soort` = @s", cn);
            cmd.Parameters.AddWithValue("@wn", werknemerId);
            cmd.Parameters.AddWithValue("@s", soort);
            return await cmd.ExecuteScalarAsync() is { } v and not DBNull ? Convert.ToInt32(v) != 0 : s.Standaard;
        }
        catch { return s.Standaard; }
    }

    public async Task<List<int>> WieWilAsync(string soort)
    {
        var lijst = new List<int>();
        if (!await TabelAanwezigAsync()) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT v.`WerknemerID` FROM `MeldingVoorkeuren` v JOIN `Werknemers` w ON w.`Werknemer-id` = v.`WerknemerID` " +
            " WHERE v.`Soort` = @s AND v.`Aan` <> 0 AND COALESCE(w.`Uit dienst`, 0) = 0", cn);
        cmd.Parameters.AddWithValue("@s", soort);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(r.GetInt32(0));
        return lijst;
    }
}
