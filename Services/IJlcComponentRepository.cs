using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Eén onderdeel uit de JLCPCB-lijst.</summary>
/// <param name="Code">C-nummer (LCSC), bv. C1002.</param>
/// <param name="Model">De MPN van de fabrikant.</param>
/// <param name="Specification">Behuizing, bv. 0603.</param>
/// <param name="Soort">'base' = basic part (geen extra opstartkosten bij
/// assemblage), 'extended' = wel.</param>
public sealed record JlcComponent(
    string Code,
    string Model,
    string? Specification,
    string? Fabrikant = null,
    string? Soort = null,
    int? Voorraad = null,
    string? Categorie = null)
{
    public bool IsBasic => string.Equals(Soort, "base", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// De lokale kopie van de JLCPCB-onderdelenbibliotheek (tabel JlcComponenten,
/// zie JlcComponenten_MariaDB.sql), gevuld door JlcOnderdelen.ZoekOpMpnAsync en
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

    /// <summary>Haalt één koppeling weg uit de vertaaltabel.</summary>
    Task VerwijderAsync(string code);
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
    public Task VerwijderAsync(string code) => Task.CompletedTask;
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

    /// <summary>
    /// Slaat een lading op met zo min mogelijk heen-en-weer: per honderd
    /// onderdelen één INSERT met meerdere regels. Bij een miljoen onderdelen
    /// scheelt dat uren.
    /// </summary>
    public async Task BewaarBatchAsync(IReadOnlyList<JlcComponent> componenten)
    {
        if (componenten.Count == 0) return;

        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        foreach (var blok in componenten.Chunk(100))
        {
            var regels = new List<string>(blok.Length);
            var cmd = new MySqlCommand { Connection = cn, Transaction = tx };
            for (var i = 0; i < blok.Length; i++)
            {
                regels.Add($"(@c{i}, @m{i}, @s{i}, @f{i}, @t{i}, @v{i}, @k{i})");
                cmd.Parameters.AddWithValue($"@c{i}", blok[i].Code);
                cmd.Parameters.AddWithValue($"@m{i}", Kort(blok[i].Model, 150));
                cmd.Parameters.AddWithValue($"@s{i}", (object?)Kort(blok[i].Specification, 150) ?? DBNull.Value);
                cmd.Parameters.AddWithValue($"@f{i}", (object?)Kort(blok[i].Fabrikant, 100) ?? DBNull.Value);
                cmd.Parameters.AddWithValue($"@t{i}", (object?)Kort(blok[i].Soort, 20) ?? DBNull.Value);
                cmd.Parameters.AddWithValue($"@v{i}", (object?)blok[i].Voorraad ?? DBNull.Value);
                cmd.Parameters.AddWithValue($"@k{i}", (object?)Kort(blok[i].Categorie, 150) ?? DBNull.Value);
            }
            cmd.CommandText =
                @"INSERT INTO JlcComponenten
                      (ComponentCode, Model, Specification, Fabrikant, Soort, Voorraad, Categorie)
                  VALUES " + string.Join(",", regels) + @"
                  ON DUPLICATE KEY UPDATE
                      Model = VALUES(Model), Specification = VALUES(Specification),
                      Fabrikant = VALUES(Fabrikant), Soort = VALUES(Soort),
                      Voorraad = VALUES(Voorraad), Categorie = VALUES(Categorie)";
            await using (cmd) await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    /// <summary>Knipt een waarde af op de kolombreedte; JLCPCB stuurt soms
    /// langere teksten dan verwacht en dan zou de hele lading stranden.</summary>
    private static string? Kort(string? waarde, int max)
        => waarde is null ? null : waarde.Length <= max ? waarde : waarde[..max];

    public async Task<List<JlcComponent>> ZoekAsync(string zoek, int maxAantal)
    {
        var lijst = new List<JlcComponent>();
        await using var cn = await OpenAsync();

        const string velden = "ComponentCode, Model, Specification, Fabrikant, Soort, Voorraad, Categorie";
        var sql = zoek.Length == 0
            ? $"SELECT {velden} FROM JlcComponenten ORDER BY Model LIMIT @max"
            : $@"SELECT {velden} FROM JlcComponenten
                 WHERE Model LIKE @z OR ComponentCode LIKE @z
                 ORDER BY Model LIMIT @max";
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@max", maxAantal);
        if (zoek.Length > 0) cmd.Parameters.AddWithValue("@z", "%" + zoek + "%");

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(Lees(r));
        return lijst;
    }

    private static JlcComponent Lees(MySqlDataReader r) => new(
        r.GetString(0), r.GetString(1),
        r.IsDBNull(2) ? null : r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetInt32(5),
        r.IsDBNull(6) ? null : r.GetString(6));

    public async Task VerwijderAsync(string code)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM JlcComponenten WHERE ComponentCode = @code", cn);
        cmd.Parameters.AddWithValue("@code", code);
        await cmd.ExecuteNonQueryAsync();
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
                "SELECT ComponentCode, Model, Specification, Fabrikant, Soort, Voorraad, Categorie " +
                $"FROM JlcComponenten WHERE Model IN ({string.Join(",", namen)})", cn);
            for (var i = 0; i < deel.Length; i++)
                cmd.Parameters.AddWithValue($"@m{i}", deel[i]);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var component = Lees(r);
                if (!uitkomst.TryGetValue(component.Model, out var lijstje))
                    uitkomst[component.Model] = lijstje = new List<JlcComponent>();
                lijstje.Add(component);
            }
        }
        return uitkomst;
    }
}
