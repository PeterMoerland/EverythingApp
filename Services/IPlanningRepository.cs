using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// De planning (Gantt): balken, soorten en de pijlen ertussen. Zie
/// Planning_Schema_MariaDB.sql voor de tabellen en de gedachte erachter.
/// </summary>
public interface IPlanningRepository
{
    bool Beschikbaar { get; }

    Task<List<PlanningSoort>> GetSoortenAsync();

    /// <summary>Alle balken die (deels) in de periode vallen.</summary>
    Task<List<PlanningBalk>> GetBalkenAsync(DateOnly van, DateOnly tot);

    /// <summary>Nieuw of bestaand; geeft het id terug.</summary>
    Task<int> BewaarAsync(PlanningBalk balk, int? doorWerknemerId);

    Task VerwijderAsync(int id);

    /// <summary>Alle pijlen; het scherm toont alleen die waarvan beide balken in beeld zijn.</summary>
    Task<List<PlanningPijl>> GetPijlenAsync();

    /// <summary>Legt een pijl; bestond hij al, dan wordt alleen de speling bijgewerkt.</summary>
    Task VoegPijlToeAsync(int vanId, int naarId, int speling);

    Task VerwijderPijlAsync(int vanId, int naarId);
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenPlanningRepository : IPlanningRepository
{
    public bool Beschikbaar => false;
    public Task<List<PlanningSoort>> GetSoortenAsync() => Task.FromResult(new List<PlanningSoort>());
    public Task<List<PlanningBalk>> GetBalkenAsync(DateOnly v, DateOnly t) => Task.FromResult(new List<PlanningBalk>());
    public Task<int> BewaarAsync(PlanningBalk balk, int? door) => Task.FromResult(0);
    public Task VerwijderAsync(int id) => Task.CompletedTask;
    public Task<List<PlanningPijl>> GetPijlenAsync() => Task.FromResult(new List<PlanningPijl>());
    public Task VoegPijlToeAsync(int van, int naar, int speling) => Task.CompletedTask;
    public Task VerwijderPijlAsync(int van, int naar) => Task.CompletedTask;
}

public class MySqlPlanningRepository : IPlanningRepository
{
    private readonly string _cs;

    public MySqlPlanningRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<List<PlanningSoort>> GetSoortenAsync()
    {
        var lijst = new List<PlanningSoort>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Soort`, `Naam`, `Kleur`, `Volgorde` FROM `PlanningSoorten` " +
            " WHERE `Actief` <> 0 ORDER BY `Volgorde`, `Naam`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new PlanningSoort(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));
        return lijst;
    }

    public async Task<List<PlanningBalk>> GetBalkenAsync(DateOnly van, DateOnly tot)
    {
        var lijst = new List<PlanningBalk>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `PlanningID`, `Soort`, `WerknemerID`, `WerkbonID`, `Start`, `Einde`, " +
            "       COALESCE(`Omschrijving`, ''), `GeplandeUren` " +
            "  FROM `Planning` " +
            " WHERE `Einde` >= @van AND `Start` <= @tot " +
            " ORDER BY `Start`, `PlanningID`", cn);
        cmd.Parameters.AddWithValue("@van", van.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tot", tot.ToDateTime(TimeOnly.MinValue));

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new PlanningBalk
            {
                Id = r.GetInt32(0),
                Soort = r.GetInt32(1),
                WerknemerId = r.IsDBNull(2) ? null : r.GetInt32(2),
                WerkbonId = r.IsDBNull(3) ? null : r.GetInt32(3),
                Start = DateOnly.FromDateTime(r.GetDateTime(4)),
                Einde = DateOnly.FromDateTime(r.GetDateTime(5)),
                Omschrijving = r.GetString(6),
                GeplandeUren = r.IsDBNull(7) ? null : Convert.ToDecimal(r.GetValue(7)),
            });
        return lijst;
    }

    public async Task<int> BewaarAsync(PlanningBalk balk, int? doorWerknemerId)
    {
        await using var cn = await OpenAsync();

        var nieuw = balk.Id == 0;
        var sql = nieuw
            ? "INSERT INTO `Planning` SET `Soort` = @soort, `WerknemerID` = @wn, " +
              " `WerkbonID` = @wb, `Start` = @start, `Einde` = @einde, " +
              " `Omschrijving` = @oms, `GeplandeUren` = @uren, " +
              " `AangemaaktOp` = @nu, `AangemaaktDoor` = @door"
            : "UPDATE `Planning` SET `Soort` = @soort, `WerknemerID` = @wn, " +
              " `WerkbonID` = @wb, `Start` = @start, `Einde` = @einde, " +
              " `Omschrijving` = @oms, `GeplandeUren` = @uren, " +
              " `GewijzigdOp` = @nu, `GewijzigdDoor` = @door " +
              " WHERE `PlanningID` = @id";

        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@soort", balk.Soort);
        cmd.Parameters.AddWithValue("@wn", (object?)balk.WerknemerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wb", (object?)balk.WerkbonId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@start", balk.Start.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@einde", balk.Einde.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@oms", balk.Omschrijving.Length > 200
            ? balk.Omschrijving[..200] : balk.Omschrijving);
        cmd.Parameters.AddWithValue("@uren", (object?)balk.GeplandeUren ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nu", DateTime.Now);
        cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
        if (!nieuw) cmd.Parameters.AddWithValue("@id", balk.Id);

        await cmd.ExecuteNonQueryAsync();
        return nieuw ? (int)cmd.LastInsertedId : balk.Id;
    }

    public async Task VerwijderAsync(int id)
    {
        await using var cn = await OpenAsync();
        // De pijlen van deze balk gaan door de ON DELETE CASCADE vanzelf mee.
        await using var cmd = new MySqlCommand(
            "DELETE FROM `Planning` WHERE `PlanningID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<PlanningPijl>> GetPijlenAsync()
    {
        var lijst = new List<PlanningPijl>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `VanID`, `NaarID`, COALESCE(`Speling`, 0) " +
            "  FROM `PlanningAfhankelijkheden`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new PlanningPijl(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)));
        return lijst;
    }

    public async Task VoegPijlToeAsync(int vanId, int naarId, int speling)
    {
        if (vanId == naarId) throw new InvalidOperationException("Een balk kan niet op zichzelf wachten.");

        await using var cn = await OpenAsync();

        // De omgekeerde pijl bestaat al? Dan zouden beide balken voorgoed op
        // elkaar wachten en allebei rood kleuren.
        await using (var check = new MySqlCommand(
            "SELECT COUNT(*) FROM `PlanningAfhankelijkheden` " +
            " WHERE `VanID` = @naar AND `NaarID` = @van", cn))
        {
            check.Parameters.AddWithValue("@naar", naarId);
            check.Parameters.AddWithValue("@van", vanId);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0)
                throw new InvalidOperationException(
                    "De pijl in de andere richting bestaat al; twee balken kunnen niet op elkaar wachten.");
        }
        await using var cmd = new MySqlCommand(
            "INSERT INTO `PlanningAfhankelijkheden` (`VanID`, `NaarID`, `Speling`) " +
            "VALUES (@van, @naar, @speling) " +
            "ON DUPLICATE KEY UPDATE `Speling` = @speling", cn);
        cmd.Parameters.AddWithValue("@van", vanId);
        cmd.Parameters.AddWithValue("@naar", naarId);
        cmd.Parameters.AddWithValue("@speling", speling);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task VerwijderPijlAsync(int vanId, int naarId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `PlanningAfhankelijkheden` WHERE `VanID` = @van AND `NaarID` = @naar", cn);
        cmd.Parameters.AddWithValue("@van", vanId);
        cmd.Parameters.AddWithValue("@naar", naarId);
        await cmd.ExecuteNonQueryAsync();
    }
}
