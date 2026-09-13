using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Wat een laptop over zichzelf meldt bij het starten van de app
/// (laptop-registreren.ps1). De namen komen overeen met de JSON uit dat script.
/// </summary>
public sealed record LaptopMelding(
    string Computernaam,
    string Gebruikersnaam,
    string ServiceTag,
    string Merk,
    string Model,
    string Processor,
    int? GeheugenGB);

/// <summary>Eén regel uit het laptopregister, voor het overzichtsscherm.</summary>
public sealed class LaptopRegel
{
    public int Id { get; set; }
    public string Computernaam { get; set; } = "";
    public string Gebruikersnaam { get; set; } = "";
    public string ServiceTag { get; set; } = "";
    public string Merk { get; set; } = "";
    public string Model { get; set; } = "";
    public string Processor { get; set; } = "";
    public int? GeheugenGB { get; set; }
    public int? WerknemerId { get; set; }
    public DateTime? EersteKeer { get; set; }
    public DateTime? LaatsteKeer { get; set; }
    public int Meldingen { get; set; }
}

/// <summary>Het laptopregister: wie gebruikt welke laptop.</summary>
public interface ILaptopRepository
{
    bool Beschikbaar { get; }

    /// <summary>
    /// Verwerkt een melding. Bestaat de combinatie computernaam + gebruikersnaam
    /// al, dan wordt alleen de hardware-informatie en 'laatste keer' bijgewerkt;
    /// er komt nooit een dubbele regel bij.
    /// </summary>
    Task MeldAsync(LaptopMelding melding);

    Task<List<LaptopRegel>> GetAllesAsync();

    /// <summary>Koppelt (of ontkoppelt, met null) de regel aan een werknemer.</summary>
    Task KoppelWerknemerAsync(int id, int? werknemerId);

    Task VerwijderAsync(int id);
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenLaptopRepository : ILaptopRepository
{
    public bool Beschikbaar => false;
    public Task MeldAsync(LaptopMelding melding) => Task.CompletedTask;
    public Task<List<LaptopRegel>> GetAllesAsync() => Task.FromResult(new List<LaptopRegel>());
    public Task KoppelWerknemerAsync(int id, int? werknemerId) => Task.CompletedTask;
    public Task VerwijderAsync(int id) => Task.CompletedTask;
}

public class MySqlLaptopRepository : ILaptopRepository
{
    private readonly string _cs;

    public MySqlLaptopRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task MeldAsync(LaptopMelding melding)
    {
        // Kort en schoon: lege of veel te lange waarden mogen het register
        // niet vervuilen (het script stuurt netjes, maar het is een open deur).
        static string Kort(string? tekst, int max)
        {
            var s = (tekst ?? "").Trim();
            return s.Length <= max ? s : s[..max];
        }

        var computer = Kort(melding.Computernaam, 64);
        var gebruiker = Kort(melding.Gebruikersnaam, 64);
        if (computer.Length == 0 || gebruiker.Length == 0) return;

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `LaptopGebruik` " +
            " (`Computernaam`, `Gebruikersnaam`, `ServiceTag`, `Merk`, `Model`, " +
            "  `Processor`, `GeheugenGB`, `EersteKeer`, `LaatsteKeer`) " +
            "VALUES (@computer, @gebruiker, @tag, @merk, @model, @cpu, @gb, @nu, @nu) " +
            // Bestond de combinatie al, dan alleen bijwerken: de hardware kan
            // veranderd zijn (geheugen erbij) en 'laatste keer' schuift op.
            "ON DUPLICATE KEY UPDATE " +
            " `ServiceTag` = @tag, `Merk` = @merk, `Model` = @model, " +
            " `Processor` = @cpu, `GeheugenGB` = @gb, " +
            " `LaatsteKeer` = @nu, `Meldingen` = `Meldingen` + 1", cn);

        cmd.Parameters.AddWithValue("@computer", computer);
        cmd.Parameters.AddWithValue("@gebruiker", gebruiker);
        cmd.Parameters.AddWithValue("@tag", Kort(melding.ServiceTag, 32));
        cmd.Parameters.AddWithValue("@merk", Kort(melding.Merk, 64));
        cmd.Parameters.AddWithValue("@model", Kort(melding.Model, 64));
        cmd.Parameters.AddWithValue("@cpu", Kort(melding.Processor, 128));
        cmd.Parameters.AddWithValue("@gb", (object?)melding.GeheugenGB ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nu", DateTime.Now);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<LaptopRegel>> GetAllesAsync()
    {
        var lijst = new List<LaptopRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `LaptopGebruikID`, `Computernaam`, `Gebruikersnaam`, `ServiceTag`, " +
            "       `Merk`, `Model`, `Processor`, `GeheugenGB`, `WerknemerID`, " +
            "       `EersteKeer`, `LaatsteKeer`, `Meldingen` " +
            "  FROM `LaptopGebruik` " +
            " ORDER BY `Computernaam`, `Gebruikersnaam`", cn);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new LaptopRegel
            {
                Id = r.GetInt32(0),
                Computernaam = r.GetString(1),
                Gebruikersnaam = r.GetString(2),
                ServiceTag = r.GetString(3),
                Merk = r.GetString(4),
                Model = r.GetString(5),
                Processor = r.GetString(6),
                GeheugenGB = r.IsDBNull(7) ? null : r.GetInt32(7),
                WerknemerId = r.IsDBNull(8) ? null : r.GetInt32(8),
                EersteKeer = r.IsDBNull(9) ? null : r.GetDateTime(9),
                LaatsteKeer = r.IsDBNull(10) ? null : r.GetDateTime(10),
                Meldingen = r.GetInt32(11),
            });
        return lijst;
    }

    public async Task KoppelWerknemerAsync(int id, int? werknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `LaptopGebruik` SET `WerknemerID` = @wn WHERE `LaptopGebruikID` = @id", cn);
        cmd.Parameters.AddWithValue("@wn", (object?)werknemerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task VerwijderAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `LaptopGebruik` WHERE `LaptopGebruikID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
