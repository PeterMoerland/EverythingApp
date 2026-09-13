using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Eén regel uit de JLCPCB-bibliotheek: C-nummer, MPN, behuizing.</summary>
public sealed record JlcComponent(string Code, string Model, string? Specification);

/// <summary>
/// De lokale kopie van de JLCPCB-onderdelenbibliotheek (tabel JlcComponenten,
/// zie JlcComponenten_MariaDB.sql), gevuld door JlcOnderdelen.SyncAsync en
/// gebruikt om bij de JLCPCB-export C-nummers op te zoeken per MPN.
/// </summary>
public interface IJlcComponentRepository
{
    bool Beschikbaar { get; }

    Task<int> AantalAsync();

    /// <summary>Slaat een lading componenten op; bestaande C-nummers worden
    /// bijgewerkt, dus opnieuw synchroniseren geeft geen dubbelen.</summary>
    Task BewaarBatchAsync(IReadOnlyList<JlcComponent> componenten);

    /// <summary>Zoekt per MPN alle bekende componenten (er kunnen meerdere
    /// C-nummers per MPN bestaan; de behuizing is dan de scheidsrechter).</summary>
    Task<Dictionary<string, List<JlcComponent>>> ZoekOpModellenAsync(IEnumerable<string> modellen);

    /// <summary>Vrij zoeken op MPN of C-nummer, voor het bladerscherm.</summary>
    Task<List<JlcComponent>> ZoekAsync(string zoek, int maxAantal);
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenJlcComponentRepository : IJlcComponentRepository
{
    public bool Beschikbaar => false;
    public Task<int> AantalAsync() => Task.FromResult(0);
    public Task BewaarBatchAsync(IReadOnlyList<JlcComponent> componenten) => Task.CompletedTask;
    public Task<Dictionary<string, List<JlcComponent>>> ZoekOpModellenAsync(IEnumerable<string> modellen)
        => Task.FromResult(new Dictionary<string, List<JlcComponent>>(StringComparer.OrdinalIgnoreCase));
    public Task<List<JlcComponent>> ZoekAsync(string zoek, int maxAantal)
        => Task.FromResult(new List<JlcComponent>());
}

public class MySqlJlcComponentRepository : IJlcComponentRepository
{
    private readonly string _cs;

    public MySqlJlcComponentRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<int> AantalAsync()
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM JlcComponenten", cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task BewaarBatchAsync(IReadOnlyList<JlcComponent> componenten)
    {
        if (componenten.Count == 0) return;

        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        foreach (var c in componenten)
        {
            await using var cmd = new MySqlCommand(
                @"INSERT INTO JlcComponenten (ComponentCode, Model, Specification)
                  VALUES (@code, @model, @spec)
                  ON DUPLICATE KEY UPDATE Model = @model, Specification = @spec", cn, tx);
            cmd.Parameters.AddWithValue("@code", c.Code);
            cmd.Parameters.AddWithValue("@model", c.Model);
            cmd.Parameters.AddWithValue("@spec", (object?)c.Specification ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task<List<JlcComponent>> ZoekAsync(string zoek, int maxAantal)
    {
        var lijst = new List<JlcComponent>();
        await using var cn = await OpenAsync();

        var sql = zoek.Length == 0
            ? "SELECT ComponentCode, Model, Specification FROM JlcComponenten ORDER BY Model LIMIT @max"
            : @"SELECT ComponentCode, Model, Specification FROM JlcComponenten
                WHERE Model LIKE @z OR ComponentCode LIKE @z
                ORDER BY Model LIMIT @max";
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@max", maxAantal);
        if (zoek.Length > 0) cmd.Parameters.AddWithValue("@z", "%" + zoek + "%");

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new JlcComponent(r.GetString(0), r.GetString(1),
                                       r.IsDBNull(2) ? null : r.GetString(2)));
        return lijst;
    }

    public async Task<Dictionary<string, List<JlcComponent>>> ZoekOpModellenAsync(IEnumerable<string> modellen)
    {
        var uitkomst = new Dictionary<string, List<JlcComponent>>(StringComparer.OrdinalIgnoreCase);
        var lijst = modellen.Where(m => m.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (lijst.Count == 0) return uitkomst;

        await using var cn = await OpenAsync();

        // In hapjes van 500, zodat de IN-lijst hanteerbaar blijft.
        foreach (var deel in lijst.Chunk(500))
        {
            var namen = deel.Select((_, i) => $"@m{i}").ToArray();
            await using var cmd = new MySqlCommand(
                $"SELECT ComponentCode, Model, Specification FROM JlcComponenten " +
                $"WHERE Model IN ({string.Join(",", namen)})", cn);
            for (var i = 0; i < deel.Length; i++)
                cmd.Parameters.AddWithValue($"@m{i}", deel[i]);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var component = new JlcComponent(
                    r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2));
                if (!uitkomst.TryGetValue(component.Model, out var lijstje))
                    uitkomst[component.Model] = lijstje = new List<JlcComponent>();
                lijstje.Add(component);
            }
        }
        return uitkomst;
    }
}
