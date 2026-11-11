using MySqlConnector;

namespace EverythingApp.Services;

// =====================================================================================================================
//  Service: wie werkte aan een project (verzoek van Michiel, 11-11-2026). Bij een storingsmelding een klant kiezen, dan
//  een project, en zien wie er uren op schreef, met het aandeel in de uren en hoeveel daarvan software was
//  (UrenActiviteit 8 = Software Engineering; die activiteit wordt pas sinds medio 2023 ingevuld).
// =====================================================================================================================

public record ServiceKlant(int Id, string Naam, int Projecten);

public record ServiceProject(int Id, string Nummer, string Omschrijving, decimal Uren, DateTime? Laatst);

/// <param name="Software">Uren met activiteit Software Engineering.</param>
/// <param name="ZonderActiviteit">Uren zonder activiteit (van vóór medio 2023): daar is niet te zien of het software was.</param>
public record ServiceWerknemer(int Id, string Naam, bool UitDienst, decimal Uren, decimal Software, decimal ZonderActiviteit,
                               DateTime? Eerst, DateTime? Laatst);

public interface IServiceRepository
{
    bool Beschikbaar { get; }
    /// <summary>Klanten met minstens één project, op naam (of projectnummer); de klant met het nieuwste project eerst.</summary>
    Task<List<ServiceKlant>> KlantenAsync(string zoek, int max);
    /// <summary>De projecten van de klant, nieuwste eerst, met de geschreven uren en de laatste urendag.</summary>
    Task<List<ServiceProject>> ProjectenAsync(int klantId);
    /// <summary>Wie er op het project uren schreef (alle werkorders), meeste uren eerst.</summary>
    Task<List<ServiceWerknemer>> WerknemersAsync(int projectId);
}

public class GeenServiceRepository : IServiceRepository
{
    public bool Beschikbaar => false;
    public Task<List<ServiceKlant>> KlantenAsync(string zoek, int max) => Task.FromResult(new List<ServiceKlant>());
    public Task<List<ServiceProject>> ProjectenAsync(int klantId) => Task.FromResult(new List<ServiceProject>());
    public Task<List<ServiceWerknemer>> WerknemersAsync(int projectId) => Task.FromResult(new List<ServiceWerknemer>());
}

public class MySqlServiceRepository : IServiceRepository
{
    private const int SoftwareActiviteit = 8;      // UrenActiviteit "Software Engineering"

    private readonly string _cs;
    public MySqlServiceRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static decimal M(MySqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToDecimal(r.GetValue(i));
    private static DateTime? D(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));

    public async Task<List<ServiceKlant>> KlantenAsync(string zoek, int max)
    {
        var lijst = new List<ServiceKlant>();
        zoek = zoek.Trim();
        if (zoek.Length == 0) return lijst;
        await using var cn = await OpenAsync();
        // op naam, of de klant van een project met dit nummer (wie het projectnummer al weet)
        await using var cmd = new MySqlCommand(
            "SELECT r.`id_supplier`, COALESCE(r.`name`, ''), COUNT(*) " +
            "  FROM `Relations` r JOIN `Projects` p ON p.`Customer-id` = r.`id_supplier` " +
            " GROUP BY r.`id_supplier`, r.`name` " +
            "HAVING r.`name` LIKE @z OR SUM(CAST(p.`Projectnumber` AS CHAR) LIKE @nr) > 0 " +
            " ORDER BY MAX(p.`Projectnumber`) DESC LIMIT @max", cn);
        cmd.Parameters.AddWithValue("@z", "%" + zoek + "%");
        cmd.Parameters.AddWithValue("@nr", zoek + "%");
        cmd.Parameters.AddWithValue("@max", max);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ServiceKlant(r.GetInt32(0), S(r, 1), Convert.ToInt32(r.GetValue(2))));
        return lijst;
    }

    public async Task<List<ServiceProject>> ProjectenAsync(int klantId)
    {
        var lijst = new List<ServiceProject>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT p.`Project-Id`, CAST(p.`Projectnumber` AS CHAR), COALESCE(p.`Projectdescription`, ''), " +
            "       COALESCE(SUM(u.`Uren`), 0), MAX(u.`StartDatum`) " +
            "  FROM `Projects` p " +
            "  LEFT JOIN `Workorders` wo ON wo.`Project-id` = p.`Project-Id` " +
            "  LEFT JOIN `Uren` u ON u.`werkorder` = wo.`Werkbon-id` AND COALESCE(u.`GeneratedByRule`, 0) = 0 " +
            " WHERE p.`Customer-id` = @k " +
            " GROUP BY p.`Project-Id`, p.`Projectnumber`, p.`Projectdescription` " +
            " ORDER BY p.`Projectnumber` DESC", cn);
        cmd.Parameters.AddWithValue("@k", klantId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ServiceProject(r.GetInt32(0), S(r, 1), S(r, 2), M(r, 3), D(r, 4)));
        return lijst;
    }

    public async Task<List<ServiceWerknemer>> WerknemersAsync(int projectId)
    {
        var lijst = new List<ServiceWerknemer>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT u.`Werknemer-id`, COALESCE(w.`NaamVoluit`, CONCAT('#', u.`Werknemer-id`)), COALESCE(w.`Uit dienst`, 0), " +
            "       SUM(u.`Uren`), " +
            $"      SUM(CASE WHEN u.`UrenActiviteitID` = {SoftwareActiviteit} THEN u.`Uren` ELSE 0 END), " +
            "       SUM(CASE WHEN u.`UrenActiviteitID` IS NULL THEN u.`Uren` ELSE 0 END), " +
            "       MIN(u.`StartDatum`), MAX(u.`StartDatum`) " +
            "  FROM `Uren` u " +
            "  JOIN `Workorders` wo ON wo.`Werkbon-id` = u.`werkorder` " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = u.`Werknemer-id` " +
            " WHERE wo.`Project-id` = @p AND COALESCE(u.`GeneratedByRule`, 0) = 0 " +
            " GROUP BY u.`Werknemer-id`, w.`NaamVoluit`, w.`Uit dienst` " +
            "HAVING SUM(u.`Uren`) > 0 " +
            " ORDER BY SUM(u.`Uren`) DESC", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ServiceWerknemer(r.GetInt32(0), S(r, 1).Trim(), Convert.ToInt32(r.GetValue(2)) != 0,
                                           M(r, 3), M(r, 4), M(r, 5), D(r, 6), D(r, 7)));
        return lijst;
    }
}
