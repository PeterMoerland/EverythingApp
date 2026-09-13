using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// De standaardcomponenten van Eurocircuits (tabel EurocircuitsComponenten,
/// zie EurocircuitsComponenten_MariaDB.sql). Gevuld via de CSV-import in
/// Tools ▸ Fusion ▸ Standaardcomponenten; het GPN is de unieke sleutel.
/// </summary>
public interface IEcComponentRepository
{
    bool Beschikbaar { get; }

    Task<List<EcComponent>> GetAllesAsync();

    /// <summary>
    /// Slaat componenten op: bestaande GPN's worden bijgewerkt, nieuwe
    /// toegevoegd — dubbelen kunnen dus niet ontstaan.
    /// </summary>
    Task<(int Nieuw, int Bijgewerkt)> ImporteerAsync(IReadOnlyList<EcComponent> componenten);

    Task VerwijderAsync(int id);
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenEcComponentRepository : IEcComponentRepository
{
    public bool Beschikbaar => false;
    public Task<List<EcComponent>> GetAllesAsync() => Task.FromResult(new List<EcComponent>());
    public Task<(int, int)> ImporteerAsync(IReadOnlyList<EcComponent> componenten)
        => Task.FromResult((0, 0));
    public Task VerwijderAsync(int id) => Task.CompletedTask;
}

public class MySqlEcComponentRepository : IEcComponentRepository
{
    private readonly string _cs;

    public MySqlEcComponentRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<List<EcComponent>> GetAllesAsync()
    {
        var lijst = new List<EcComponent>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            @"SELECT EcComponentID, Gpn, Soort, Grootte, WaardeBasis, WaardeTekst,
                     Tolerantie, Vermogen, Spanning, Dielectricum, Omschrijving
              FROM EurocircuitsComponenten
              ORDER BY Soort, Grootte, WaardeBasis", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            lijst.Add(new EcComponent
            {
                Id = r.GetInt32(0),
                Gpn = r.GetString(1),
                Soort = r.GetString(2).FirstOrDefault(),
                Grootte = r.GetString(3),
                WaardeBasis = r.GetDouble(4),
                WaardeTekst = r.GetString(5),
                Tolerantie = r.IsDBNull(6) ? null : r.GetString(6),
                Vermogen = r.IsDBNull(7) ? null : r.GetString(7),
                Spanning = r.IsDBNull(8) ? null : r.GetString(8),
                Dielectricum = r.IsDBNull(9) ? null : r.GetString(9),
                Omschrijving = r.IsDBNull(10) ? null : r.GetString(10),
            });
        }
        return lijst;
    }

    public async Task<(int Nieuw, int Bijgewerkt)> ImporteerAsync(IReadOnlyList<EcComponent> componenten)
    {
        await using var cn = await OpenAsync();

        // Eerst kijken welke GPN's er al zijn: dan kunnen we eerlijk tellen
        // wat 'nieuw' en wat 'bijgewerkt' is, los van hoe MySQL telt.
        var bestaand = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var lees = new MySqlCommand("SELECT Gpn FROM EurocircuitsComponenten", cn))
        await using (var r = await lees.ExecuteReaderAsync())
            while (await r.ReadAsync())
                bestaand.Add(r.GetString(0));

        var nieuw = 0;
        var bijgewerkt = 0;

        await using var tx = await cn.BeginTransactionAsync();
        foreach (var c in componenten)
        {
            await using var cmd = new MySqlCommand(
                @"INSERT INTO EurocircuitsComponenten
                      (Gpn, Soort, Grootte, WaardeBasis, WaardeTekst,
                       Tolerantie, Vermogen, Spanning, Dielectricum, Omschrijving)
                  VALUES (@gpn, @soort, @grootte, @basis, @tekst,
                          @tol, @vermogen, @spanning, @diel, @oms)
                  ON DUPLICATE KEY UPDATE
                      Soort = @soort, Grootte = @grootte, WaardeBasis = @basis,
                      WaardeTekst = @tekst, Tolerantie = @tol, Vermogen = @vermogen,
                      Spanning = @spanning, Dielectricum = @diel, Omschrijving = @oms",
                cn, tx);
            cmd.Parameters.AddWithValue("@gpn", c.Gpn);
            cmd.Parameters.AddWithValue("@soort", c.Soort.ToString());
            cmd.Parameters.AddWithValue("@grootte", c.Grootte);
            cmd.Parameters.AddWithValue("@basis", c.WaardeBasis);
            cmd.Parameters.AddWithValue("@tekst", c.WaardeTekst);
            cmd.Parameters.AddWithValue("@tol", (object?)c.Tolerantie ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vermogen", (object?)c.Vermogen ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@spanning", (object?)c.Spanning ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@diel", (object?)c.Dielectricum ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@oms", (object?)c.Omschrijving ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();

            if (bestaand.Add(c.Gpn)) nieuw++;
            else bijgewerkt++;
        }
        await tx.CommitAsync();

        return (nieuw, bijgewerkt);
    }

    public async Task VerwijderAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM EurocircuitsComponenten WHERE EcComponentID = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
