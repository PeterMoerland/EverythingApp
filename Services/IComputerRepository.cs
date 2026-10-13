using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Wat een computer over zichzelf meldt bij het starten van de app
/// (computer-registreren.ps1). De namen komen overeen met de JSON uit dat script.
/// </summary>
public sealed record ComputerMelding(
    string Computernaam,
    string Gebruiker,          // Windows-aanmeldnaam
    string? GebruikerNaam,     // volledige naam uit Windows, als die er is
    string ServiceTag,
    string MerkType,           // "Dell Inc. Latitude 5540"
    string Processor,
    int? GeheugenGb,
    string? Schijven,          // "Samsung PM9A1 (512 GB); …"
    int? SchijvenGb,
    string? GrafischeKaarten);

/// <summary>Eén regel uit de tabel Computers, voor Beheer ▸ Computers.</summary>
public sealed class ComputerRegel
{
    public int Id { get; set; }
    public string ServiceTag { get; set; } = "";
    public string Computernaam { get; set; } = "";
    public string MerkType { get; set; } = "";
    public string Processor { get; set; } = "";
    public string Gebruiker { get; set; } = "";
    public string GebruikerNaam { get; set; } = "";
    public int? GeheugenGb { get; set; }
    public string Schijven { get; set; } = "";
    public int? SchijvenGb { get; set; }
    public string GrafischeKaarten { get; set; } = "";
    public DateTime? Aangemaakt { get; set; }
    public DateTime? Bijgewerkt { get; set; }
}

/// <summary>De tabel Computers: welke pc/laptop is er, en wie zit erachter.</summary>
public interface IComputerRepository
{
    bool Beschikbaar { get; }

    /// <summary>Verwerkt een melding: bestaande computer (op service tag, anders
    /// computernaam) bijwerken, anders toevoegen. Nooit een dubbele regel.</summary>
    Task MeldAsync(ComputerMelding melding);

    Task<List<ComputerRegel>> GetAllesAsync();
    Task VerwijderAsync(int id);
}

public class GeenComputerRepository : IComputerRepository
{
    public bool Beschikbaar => false;
    public Task MeldAsync(ComputerMelding melding) => Task.CompletedTask;
    public Task<List<ComputerRegel>> GetAllesAsync() => Task.FromResult(new List<ComputerRegel>());
    public Task VerwijderAsync(int id) => Task.CompletedTask;
}

public class MySqlComputerRepository : IComputerRepository
{
    private readonly string _cs;
    public MySqlComputerRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string Kort(string? tekst, int max)
    {
        var s = (tekst ?? "").Trim();
        return s.Length <= max ? s : s[..max];
    }

    public async Task MeldAsync(ComputerMelding m)
    {
        var computer = Kort(m.Computernaam, 64);
        var tag = Kort(m.ServiceTag, 64);
        if (computer.Length == 0) return;

        await using var cn = await OpenAsync();

        // Bestaat hij al? Service tag is het echte kenmerk van het apparaat; zonder
        // tag (virtuele machine, oud moederbord) valt het terug op de computernaam.
        int? id = null;
        await using (var zoek = new MySqlCommand(tag.Length > 0
            ? "SELECT `Id` FROM `Computers` WHERE `ServiceTag` = @tag ORDER BY `Id` LIMIT 1"
            : "SELECT `Id` FROM `Computers` WHERE `Computernaam` = @computer ORDER BY `Id` LIMIT 1", cn))
        {
            zoek.Parameters.AddWithValue("@tag", tag);
            zoek.Parameters.AddWithValue("@computer", computer);
            var res = await zoek.ExecuteScalarAsync();
            if (res is not null and not DBNull) id = Convert.ToInt32(res);
        }

        const string velden =
            "`ServiceTag` = @tag, `Computernaam` = @computer, `MerkType` = @merktype, `Processor` = @cpu, " +
            "`Gebruiker` = @gebruiker, `GebruikerNaam` = @naam, `GeheugenGb` = @gb, " +
            "`Schijven` = @schijven, `SchijvenGb` = @schijvengb, `GrafischeKaarten` = @gpu, `Bijgewerkt` = @nu";
        var sql = id is null
            ? $"INSERT INTO `Computers` SET {velden}, `Aangemaakt` = @nu"
            : $"UPDATE `Computers` SET {velden} WHERE `Id` = @id";

        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@tag", tag);
        cmd.Parameters.AddWithValue("@computer", computer);
        cmd.Parameters.AddWithValue("@merktype", Kort(m.MerkType, 128));
        cmd.Parameters.AddWithValue("@cpu", Kort(m.Processor, 128));
        cmd.Parameters.AddWithValue("@gebruiker", Kort(m.Gebruiker, 128));
        cmd.Parameters.AddWithValue("@naam", Kort(string.IsNullOrWhiteSpace(m.GebruikerNaam) ? m.Gebruiker : m.GebruikerNaam, 128));
        cmd.Parameters.AddWithValue("@gb", (object?)m.GeheugenGb ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@schijven", Kort(m.Schijven, 512));
        cmd.Parameters.AddWithValue("@schijvengb", (object?)m.SchijvenGb ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gpu", Kort(m.GrafischeKaarten, 512));
        cmd.Parameters.AddWithValue("@nu", DateTime.Now);
        if (id is not null) cmd.Parameters.AddWithValue("@id", id.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ComputerRegel>> GetAllesAsync()
    {
        var lijst = new List<ComputerRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Id`, `ServiceTag`, `Computernaam`, `MerkType`, `Processor`, `Gebruiker`, `GebruikerNaam`, " +
            "       `GeheugenGb`, `Schijven`, `SchijvenGb`, `GrafischeKaarten`, `Aangemaakt`, `Bijgewerkt` " +
            "  FROM `Computers` ORDER BY `Computernaam`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string S(int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
            lijst.Add(new ComputerRegel
            {
                Id = r.GetInt32(0), ServiceTag = S(1), Computernaam = S(2), MerkType = S(3), Processor = S(4),
                Gebruiker = S(5), GebruikerNaam = S(6),
                GeheugenGb = r.IsDBNull(7) ? null : Convert.ToInt32(r.GetValue(7)),
                Schijven = S(8),
                SchijvenGb = r.IsDBNull(9) ? null : Convert.ToInt32(r.GetValue(9)),
                GrafischeKaarten = S(10),
                Aangemaakt = r.IsDBNull(11) ? null : Convert.ToDateTime(r.GetValue(11)),
                Bijgewerkt = r.IsDBNull(12) ? null : Convert.ToDateTime(r.GetValue(12)),
            });
        }
        return lijst;
    }

    public async Task VerwijderAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `Computers` WHERE `Id` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
