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
/// <param name="Mail">Werknemers.Mail, voor de knop ✉ (een storing doorzetten).</param>
public record ServiceWerknemer(int Id, string Naam, bool UitDienst, decimal Uren, decimal Software, decimal ZonderActiviteit,
                               DateTime? Eerst, DateTime? Laatst, string Mail = "");

/// <summary>Een vastgelegde storing of verzoek (tabel Servicemeldingen, Servicemeldingen_MariaDB.sql).</summary>
public record Servicemelding(int Id, int? ProjectId, string ProjectNummer, string Project, int? KlantId, string Klant,
                             string Beller, string Telefoon, DateTime? GebeldOp, string Omschrijving, string Aan, bool Gemaild,
                             DateTime AangemaaktOp, string AangemaaktDoor, bool Afgehandeld, DateTime? AfgehandeldOp,
                             string AfgehandeldDoor);

public interface IServiceRepository
{
    bool Beschikbaar { get; }
    /// <summary>Klanten met minstens één project, op naam (of projectnummer); de klant met het nieuwste project eerst.</summary>
    Task<List<ServiceKlant>> KlantenAsync(string zoek, int max);
    /// <summary>De projecten van de klant, nieuwste eerst, met de geschreven uren en de laatste urendag.</summary>
    Task<List<ServiceProject>> ProjectenAsync(int klantId);
    /// <summary>Wie er op het project uren schreef (alle werkorders), meeste uren eerst.</summary>
    Task<List<ServiceWerknemer>> WerknemersAsync(int projectId);

    // ---- meldingen ----
    /// <summary>Bestaat de tabel Servicemeldingen (Servicemeldingen_MariaDB.sql)?</summary>
    Task<bool> MeldingenAanwezigAsync();
    /// <summary>Legt een melding vast; geeft het nieuwe id.</summary>
    Task<int> BewaarMeldingAsync(int? projectId, int? klantId, string beller, string telefoon, DateTime gebeldOp,
                                 string omschrijving, string aan, int? door);
    Task ZetGemaildAsync(int id);
    Task ZetAfgehandeldAsync(int id, bool afgehandeld, int? door);
    /// <summary>Meldingen van een project (alle), of zonder project de open meldingen; nieuwste eerst.</summary>
    Task<List<Servicemelding>> MeldingenAsync(int? projectId, int max);
}

public class GeenServiceRepository : IServiceRepository
{
    public bool Beschikbaar => false;
    public Task<List<ServiceKlant>> KlantenAsync(string zoek, int max) => Task.FromResult(new List<ServiceKlant>());
    public Task<List<ServiceProject>> ProjectenAsync(int klantId) => Task.FromResult(new List<ServiceProject>());
    public Task<List<ServiceWerknemer>> WerknemersAsync(int projectId) => Task.FromResult(new List<ServiceWerknemer>());
    public Task<bool> MeldingenAanwezigAsync() => Task.FromResult(false);
    public Task<int> BewaarMeldingAsync(int? p, int? k, string b, string t, DateTime g, string o, string a, int? d) => Task.FromResult(0);
    public Task ZetGemaildAsync(int id) => Task.CompletedTask;
    public Task ZetAfgehandeldAsync(int id, bool afgehandeld, int? door) => Task.CompletedTask;
    public Task<List<Servicemelding>> MeldingenAsync(int? projectId, int max) => Task.FromResult(new List<Servicemelding>());
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
            "       MIN(u.`StartDatum`), MAX(u.`StartDatum`), COALESCE(w.`Mail`, '') " +
            "  FROM `Uren` u " +
            "  JOIN `Workorders` wo ON wo.`Werkbon-id` = u.`werkorder` " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = u.`Werknemer-id` " +
            " WHERE wo.`Project-id` = @p AND COALESCE(u.`GeneratedByRule`, 0) = 0 " +
            " GROUP BY u.`Werknemer-id`, w.`NaamVoluit`, w.`Uit dienst`, w.`Mail` " +
            "HAVING SUM(u.`Uren`) > 0 " +
            " ORDER BY SUM(u.`Uren`) DESC", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ServiceWerknemer(r.GetInt32(0), S(r, 1).Trim(), Convert.ToInt32(r.GetValue(2)) != 0,
                                           M(r, 3), M(r, 4), M(r, 5), D(r, 6), D(r, 7), S(r, 8).Trim()));
        return lijst;
    }

    // ---- meldingen ----
    private bool _meldingen;      // alleen 'ja' onthouden: het script mag later gedraaid worden

    public async Task<bool> MeldingenAanwezigAsync()
    {
        if (_meldingen) return true;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Servicemeldingen'", cn);
        return _meldingen = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static string Kort(string tekst, int lengte) => tekst.Length <= lengte ? tekst : tekst[..lengte];

    public async Task<int> BewaarMeldingAsync(int? projectId, int? klantId, string beller, string telefoon, DateTime gebeldOp,
                                              string omschrijving, string aan, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `Servicemeldingen` (`ProjectID`, `KlantID`, `Beller`, `Telefoon`, `GebeldOp`, `Omschrijving`, `Aan`, " +
            "                                `Gemaild`, `AangemaaktOp`, `AangemaaktDoor`) " +
            "VALUES (@p, @k, @b, @t, @g, @o, @a, 0, NOW(), @d)", cn);
        cmd.Parameters.AddWithValue("@p", (object?)projectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@k", (object?)klantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@b", Kort(beller.Trim(), 150));
        cmd.Parameters.AddWithValue("@t", Kort(telefoon.Trim(), 50));
        cmd.Parameters.AddWithValue("@g", gebeldOp);
        cmd.Parameters.AddWithValue("@o", omschrijving.Trim());
        cmd.Parameters.AddWithValue("@a", Kort(aan, 500));
        cmd.Parameters.AddWithValue("@d", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    public async Task ZetGemaildAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `Servicemeldingen` SET `Gemaild` = 1 WHERE `ServicemeldingID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetAfgehandeldAsync(int id, bool afgehandeld, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Servicemeldingen` SET `Afgehandeld` = @af, " +
            "       `AfgehandeldOp` = CASE WHEN @af = 1 THEN NOW() ELSE NULL END, " +
            "       `AfgehandeldDoor` = CASE WHEN @af = 1 THEN @d ELSE NULL END " +
            " WHERE `ServicemeldingID` = @id", cn);
        cmd.Parameters.AddWithValue("@af", afgehandeld ? 1 : 0);
        cmd.Parameters.AddWithValue("@d", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Servicemelding>> MeldingenAsync(int? projectId, int max)
    {
        var lijst = new List<Servicemelding>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT m.`ServicemeldingID`, m.`ProjectID`, COALESCE(CAST(p.`Projectnumber` AS CHAR), ''), COALESCE(p.`Projectdescription`, ''), " +
            "       m.`KlantID`, COALESCE(r.`name`, ''), m.`Beller`, m.`Telefoon`, m.`GebeldOp`, COALESCE(m.`Omschrijving`, ''), m.`Aan`, " +
            "       m.`Gemaild`, m.`AangemaaktOp`, COALESCE(wa.`NaamVoluit`, ''), m.`Afgehandeld`, m.`AfgehandeldOp`, COALESCE(wb.`NaamVoluit`, '') " +
            "  FROM `Servicemeldingen` m " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = m.`ProjectID` " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = m.`KlantID` " +
            "  LEFT JOIN `Werknemers` wa ON wa.`Werknemer-id` = m.`AangemaaktDoor` " +
            "  LEFT JOIN `Werknemers` wb ON wb.`Werknemer-id` = m.`AfgehandeldDoor` " +
            (projectId is null ? " WHERE m.`Afgehandeld` = 0 " : " WHERE m.`ProjectID` = @p ") +
            " ORDER BY COALESCE(m.`GebeldOp`, m.`AangemaaktOp`) DESC, m.`ServicemeldingID` DESC LIMIT @max", cn);
        if (projectId is int pid) cmd.Parameters.AddWithValue("@p", pid);
        cmd.Parameters.AddWithValue("@max", max);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Servicemelding(
                r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), S(r, 2), S(r, 3), r.IsDBNull(4) ? null : r.GetInt32(4), S(r, 5),
                S(r, 6), S(r, 7), D(r, 8), S(r, 9), S(r, 10), Convert.ToInt32(r.GetValue(11)) != 0, Convert.ToDateTime(r.GetValue(12)),
                S(r, 13).Trim(), Convert.ToInt32(r.GetValue(14)) != 0, D(r, 15), S(r, 16).Trim()));
        return lijst;
    }
}
