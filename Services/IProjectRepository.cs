using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Eén regel in de projectenlijst.</summary>
public record ProjectRegel(int Id, int? Nummer, string Omschrijving);

/// <summary>Eén regel in de werkorderlijst; Nummer is al 'projectnr-02'.</summary>
/// <summary>Eén werkorder in de lijst. Ouder is leeg voor het bovenste niveau,
/// of het Werkbon-id van de werkorder waar deze onder hangt (de boom).</summary>
public record WerkorderRegel(int Id, string Nummer, string Omschrijving, bool Gesloten,
                             DateTime? Deadline, int? Ouder = null);

/// <summary>De percentages uit de tariefafspraak van de klant (de grijze vakjes in Access).</summary>
public record KlantAfspraak(decimal? Uren, decimal? Expenses, decimal? Materialen);

public interface IProjectRepository
{
    bool Beschikbaar { get; }

    Task<HashSet<string>> ProjectKolommenAsync();
    Task<HashSet<string>> WerkorderKolommenAsync();

    Task<int> TelProjectenAsync(string? klant, string? project, int? status);
    Task<List<ProjectRegel>> ZoekProjectenAsync(string? klant, string? project, int? status,
                                                int begin, int aantal);

    Task<Dictionary<string, object?>> GetProjectVeldenAsync(int projectId);
    Task UpdateProjectVeldAsync(int projectId, string kolom, object? waarde);
    Task<int> VolgendProjectnummerAsync();
    Task<int> NieuwProjectAsync(int nummer, string omschrijving, int? klantId, int? door);
    Task<string?> ProjectInGebruikAsync(int projectId);
    Task VerwijderProjectAsync(int projectId);

    Task<List<WerkorderRegel>> GetWerkordersAsync(int projectId);
    Task<Dictionary<string, object?>> GetWerkorderVeldenAsync(int werkbonId);
    Task UpdateWerkorderVeldAsync(int werkbonId, string kolom, object? waarde);
    Task<int> NieuweWerkorderAsync(int projectId, string omschrijving, int? door);
    Task VerwijderWerkorderAsync(int werkbonId, int? door);

    /// <summary>
    /// Hangt een werkorder onder een andere werkorder (of terug op het bovenste
    /// niveau met null). Bewaakt dat beide bij hetzelfde project horen en dat
    /// er geen kring ontstaat.
    /// </summary>
    Task ZetWerkorderOuderAsync(int werkbonId, int? ouderId);

    /// <summary>Percentages uit de geldige tariefafspraak van de klant van dit project.</summary>
    Task<KlantAfspraak> GetKlantAfspraakAsync(int projectId);

    // keuzelijsten
    Task<List<Keuze>> GetWerkmaatschappijenAsync();
    Task<List<Keuze>> GetKlantenAsync();
    Task<List<Keuze>> GetWerknemersAsync();
    Task<List<Keuze>> GetProjectsoortenAsync();
    Task<List<Keuze>> GetWerkordersoortenAsync();

    /// <summary>Is deze werknemer projectleider (Projects.ProjectResponsible) van minstens één open project?</summary>
    Task<bool> IsProjectleiderAsync(int werknemerId);
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenProjectRepository : IProjectRepository
{
    public bool Beschikbaar => false;
    private static readonly HashSet<string> Leeg = new();

    public Task<HashSet<string>> ProjectKolommenAsync() => Task.FromResult(Leeg);
    public Task<HashSet<string>> WerkorderKolommenAsync() => Task.FromResult(Leeg);
    public Task<int> TelProjectenAsync(string? klant, string? project, int? status) => Task.FromResult(0);
    public Task<List<ProjectRegel>> ZoekProjectenAsync(string? klant, string? project, int? status, int begin, int aantal)
        => Task.FromResult(new List<ProjectRegel>());
    public Task<Dictionary<string, object?>> GetProjectVeldenAsync(int projectId)
        => Task.FromResult(new Dictionary<string, object?>());
    public Task UpdateProjectVeldAsync(int projectId, string kolom, object? waarde) => Task.CompletedTask;
    public Task<int> VolgendProjectnummerAsync() => Task.FromResult(0);
    public Task<int> NieuwProjectAsync(int nummer, string omschrijving, int? klantId, int? door) => Task.FromResult(0);
    public Task<string?> ProjectInGebruikAsync(int projectId) => Task.FromResult<string?>("Geen live database.");
    public Task VerwijderProjectAsync(int projectId) => Task.CompletedTask;
    public Task<List<WerkorderRegel>> GetWerkordersAsync(int projectId) => Task.FromResult(new List<WerkorderRegel>());
    public Task<Dictionary<string, object?>> GetWerkorderVeldenAsync(int werkbonId)
        => Task.FromResult(new Dictionary<string, object?>());
    public Task UpdateWerkorderVeldAsync(int werkbonId, string kolom, object? waarde) => Task.CompletedTask;
    public Task<int> NieuweWerkorderAsync(int projectId, string omschrijving, int? door) => Task.FromResult(0);
    public Task VerwijderWerkorderAsync(int werkbonId, int? door) => Task.CompletedTask;
    public Task ZetWerkorderOuderAsync(int werkbonId, int? ouderId) => Task.CompletedTask;
    public Task<KlantAfspraak> GetKlantAfspraakAsync(int projectId) => Task.FromResult(new KlantAfspraak(null, null, null));
    public Task<List<Keuze>> GetWerkmaatschappijenAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetKlantenAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetWerknemersAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetProjectsoortenAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetWerkordersoortenAsync() => Task.FromResult(new List<Keuze>());
    public Task<bool> IsProjectleiderAsync(int werknemerId) => Task.FromResult(false);
}

/// <summary>
/// Projecten en werkorders uit de tabellen Projects en Workorders.
/// Kolomnamen komen rechtstreeks uit het schema; wat er niet is wordt overgeslagen,
/// zodat een afwijkende database geen fouten geeft maar hoogstens een leeg veld.
/// </summary>
public class MySqlProjectRepository : IProjectRepository
{
    private readonly string _cs;
    private HashSet<string>? _projectKolommen;
    private HashSet<string>? _werkorderKolommen;
    private HashSet<string>? _relatieKolommen;

    public MySqlProjectRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private async Task<HashSet<string>> KolommenVan(string tabel)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = @t ORDER BY ordinal_position", cn);
        cmd.Parameters.AddWithValue("@t", tabel);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) set.Add(r.GetString(0));
        return set;
    }

    public async Task<HashSet<string>> ProjectKolommenAsync()
        => _projectKolommen ??= await KolommenVan("Projects");

    public async Task<HashSet<string>> WerkorderKolommenAsync()
        => _werkorderKolommen ??= await KolommenVan("Workorders");

    // ============================================================
    //  Projectenlijst
    // ============================================================

    /// <summary>
    /// Eén plek voor de zoekvoorwaarden, gebruikt door zowel tellen als ophalen.
    /// Op klantnaam zoeken gaat via een subquery op Relations, zodat we geen JOIN
    /// nodig hebben en de kolomnaam van de relatietabel losstaat.
    /// </summary>
    private async Task<string> ZoekVoorwaarden(string? klant, string? project, int? status)
    {
        var kolommen = await ProjectKolommenAsync();
        var delen = new List<string>();

        if (!string.IsNullOrWhiteSpace(project))
            delen.Add("(p.`Projectdescription` LIKE @p OR CAST(p.`Projectnumber` AS CHAR) LIKE @p)");

        if (!string.IsNullOrWhiteSpace(klant))
        {
            // Zoeken op klantnaam via een subquery, zodat we geen JOIN nodig hebben.
            // Op alias alleen zoeken als die kolom bestaat.
            var rel = _relatieKolommen ??= await KolommenVan("Relations");
            var opAlias = rel.Contains("Alias") ? " OR `Alias` LIKE @k" : "";
            delen.Add("p.`Customer-id` IN (SELECT `id_supplier` FROM `Relations` " +
                      $"                    WHERE `name` LIKE @k{opAlias})");
        }

        if (status is not null && kolommen.Contains("ProjectStatus"))
            delen.Add("p.`ProjectStatus` = @s");

        if (kolommen.Contains("Deleted"))
            delen.Add("(p.`Deleted` = 0 OR p.`Deleted` IS NULL)");

        return delen.Count > 0 ? "WHERE " + string.Join(" AND ", delen) : "";
    }

    private static void VulZoek(MySqlCommand cmd, string? klant, string? project, int? status)
    {
        if (!string.IsNullOrWhiteSpace(project)) cmd.Parameters.AddWithValue("@p", "%" + project.Trim() + "%");
        if (!string.IsNullOrWhiteSpace(klant)) cmd.Parameters.AddWithValue("@k", "%" + klant.Trim() + "%");
        if (status is not null) cmd.Parameters.AddWithValue("@s", status.Value);
    }

    public async Task<int> TelProjectenAsync(string? klant, string? project, int? status)
    {
        var where = await ZoekVoorwaarden(klant, project, status);

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand($"SELECT COUNT(*) FROM `Projects` p {where}", cn);
        VulZoek(cmd, klant, project, status);

        var res = await cmd.ExecuteScalarAsync();
        return res is null or DBNull ? 0 : Convert.ToInt32(res);
    }

    public async Task<List<ProjectRegel>> ZoekProjectenAsync(string? klant, string? project, int? status,
                                                             int begin, int aantal)
    {
        var where = await ZoekVoorwaarden(klant, project, status);

        var lijst = new List<ProjectRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT p.`Project-Id`, p.`Projectnumber`, p.`Projectdescription` " +
            $"  FROM `Projects` p {where} " +
            "  ORDER BY p.`Projectnumber` DESC " +
            $" LIMIT {Math.Clamp(aantal, 1, 500)} OFFSET {Math.Max(begin, 0)}", cn);
        VulZoek(cmd, klant, project, status);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ProjectRegel(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : Convert.ToInt32(r.GetValue(1)),
                r.IsDBNull(2) ? "" : r.GetValue(2).ToString() ?? ""));
        return lijst;
    }

    public async Task<Dictionary<string, object?>> GetProjectVeldenAsync(int projectId)
        => await VeldenAsync("Projects", "Project-Id", projectId);

    public async Task UpdateProjectVeldAsync(int projectId, string kolom, object? waarde)
        => await UpdateVeldAsync("Projects", "Project-Id", await ProjectKolommenAsync(), projectId, kolom, waarde);

    private async Task<Dictionary<string, object?>> VeldenAsync(string tabel, string idKolom, int id)
    {
        var velden = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand($"SELECT * FROM `{tabel}` WHERE `{idKolom}` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
            for (var i = 0; i < r.FieldCount; i++)
                velden[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return velden;
    }

    private async Task UpdateVeldAsync(string tabel, string idKolom, HashSet<string> kolommen,
                                      int id, string kolom, object? waarde)
    {
        var echt = kolommen.FirstOrDefault(k => string.Equals(k, kolom, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Kolom '{kolom}' bestaat niet in {tabel}.");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"UPDATE `{tabel}` SET `{echt}` = @w WHERE `{idKolom}` = @id", cn);
        cmd.Parameters.AddWithValue("@w", waarde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Volgend projectnummer volgens jullie eigen reeks: de eerste drie cijfers horen
    /// bij het jaar (2026 → 226) en daarachter een oplopend nummer. Bestaat die reeks
    /// dit jaar nog niet, dan begint hij op 001.
    /// </summary>
    public async Task<int> VolgendProjectnummerAsync()
    {
        var reeks = 200 + DateTime.Today.Year % 100;   // 2026 → 226
        var onder = reeks * 1000;
        var boven = onder + 999;

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT MAX(`Projectnumber`) FROM `Projects` " +
            "WHERE `Projectnumber` BETWEEN @o AND @b", cn);
        cmd.Parameters.AddWithValue("@o", onder);
        cmd.Parameters.AddWithValue("@b", boven);

        var res = await cmd.ExecuteScalarAsync();
        if (res is null or DBNull) return onder + 1;
        return Convert.ToInt32(res) + 1;
    }

    public async Task<int> NieuwProjectAsync(int nummer, string omschrijving, int? klantId, int? door)
    {
        var kolommen = await ProjectKolommenAsync();
        var sets = new List<string> { "`Projectnumber` = @nr", "`Projectdescription` = @oms" };

        if (klantId is not null) sets.Add("`Customer-id` = @klant");
        if (kolommen.Contains("ProjectStatus")) sets.Add("`ProjectStatus` = @status");
        if (kolommen.Contains("Created at")) sets.Add("`Created at` = @nu");
        if (kolommen.Contains("Created by")) sets.Add("`Created by` = @door");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"INSERT INTO `Projects` SET {string.Join(", ", sets)}", cn);
        cmd.Parameters.AddWithValue("@nr", nummer);
        cmd.Parameters.AddWithValue("@oms", omschrijving ?? "");
        if (klantId is not null) cmd.Parameters.AddWithValue("@klant", klantId.Value);
        if (kolommen.Contains("ProjectStatus"))
            cmd.Parameters.AddWithValue("@status", ProjectVelden.ProjectStatussen[0].Id);   // Open
        if (kolommen.Contains("Created at")) cmd.Parameters.AddWithValue("@nu", DateTime.Now);
        if (kolommen.Contains("Created by")) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    public async Task<string?> ProjectInGebruikAsync(int projectId)
    {
        await using var cn = await OpenAsync();

        // werkorders (niet-gewiste)
        try
        {
            var wk = await WerkorderKolommenAsync();
            var filter = wk.Contains("Deleted") ? "AND (`Deleted` = 0 OR `Deleted` IS NULL)" : "";
            await using var cmd = new MySqlCommand(
                $"SELECT COUNT(*) FROM `Workorders` WHERE `Project-id` = @id {filter}", cn);
            cmd.Parameters.AddWithValue("@id", projectId);
            var res = await cmd.ExecuteScalarAsync();
            var aantal = res is null or DBNull ? 0 : Convert.ToInt64(res);
            if (aantal > 0)
                return $"Er hangen nog {aantal} werkorder(s) aan dit project. Wis die eerst.";
        }
        catch { }

        // geboekte uren
        foreach (var (tabel, kolom) in new[] { ("Uren", "Project-id"), ("Uren", "Projectid") })
        {
            try
            {
                await using var cmd = new MySqlCommand(
                    $"SELECT COUNT(*) FROM `{tabel}` WHERE `{kolom}` = @id", cn);
                cmd.Parameters.AddWithValue("@id", projectId);
                var res = await cmd.ExecuteScalarAsync();
                var aantal = res is null or DBNull ? 0 : Convert.ToInt64(res);
                if (aantal > 0) return $"Er staan {aantal} urenregel(s) op dit project.";
                break;
            }
            catch { }
        }

        return null;
    }

    public async Task VerwijderProjectAsync(int projectId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `Projects` WHERE `Project-Id` = @id", cn);
        cmd.Parameters.AddWithValue("@id", projectId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Werkorders
    // ============================================================

    public async Task<List<WerkorderRegel>> GetWerkordersAsync(int projectId)
    {
        var kolommen = await WerkorderKolommenAsync();
        var filter = kolommen.Contains("Deleted") ? "AND (w.`Deleted` = 0 OR w.`Deleted` IS NULL)" : "";

        // De boomkolom bestaat pas na Werkorders_Boom_MariaDB.sql; zonder die
        // kolom blijft alles een platte lijst.
        var ouder = kolommen.Contains("ParentWerkbonID") ? "w.`ParentWerkbonID`" : "NULL";

        var lijst = new List<WerkorderRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT w.`Werkbon-id`, " +
            "       CONCAT(p.`Projectnumber`, '-', RIGHT(CONCAT('00', w.`Werkbonnummer`), 2)), " +
            $"      w.`Werkbonomschrijving`, w.`WorkOrderStatus`, w.`Deadline`, {ouder} " +
            "  FROM `Workorders` w " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            $" WHERE w.`Project-id` = @id {filter} " +
            "  ORDER BY w.`Werkbonnummer`", cn);
        cmd.Parameters.AddWithValue("@id", projectId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var gesloten = !r.IsDBNull(3) && Convert.ToInt32(r.GetValue(3)) == 0;
            lijst.Add(new WerkorderRegel(
                r.GetInt32(0),
                r.IsDBNull(1) ? "" : r.GetValue(1).ToString() ?? "",
                r.IsDBNull(2) ? "" : r.GetValue(2).ToString() ?? "",
                gesloten,
                r.IsDBNull(4) ? null : Convert.ToDateTime(r.GetValue(4)),
                r.IsDBNull(5) ? null : Convert.ToInt32(r.GetValue(5))));
        }
        return lijst;
    }

    public async Task ZetWerkorderOuderAsync(int werkbonId, int? ouderId)
    {
        var kolommen = await WerkorderKolommenAsync();
        if (!kolommen.Contains("ParentWerkbonID"))
            throw new InvalidOperationException(
                "De kolom ParentWerkbonID ontbreekt nog; draai Werkorders_Boom_MariaDB.sql.");

        if (ouderId == werkbonId)
            throw new InvalidOperationException("Een werkorder kan niet onder zichzelf hangen.");

        await using var cn = await OpenAsync();

        if (ouderId is int doel)
        {
            // Ouder en kind moeten bij hetzelfde project horen, en de nieuwe
            // ouder mag geen nakomeling van het kind zijn (kring). Daarvoor
            // halen we de hele boom van het project op en lopen we omhoog.
            var ouders = new Dictionary<int, int?>();
            int? projectVan = null, projectDoel = null;

            await using (var cmd = new MySqlCommand(
                "SELECT `Werkbon-id`, `ParentWerkbonID`, `Project-id` FROM `Workorders` " +
                " WHERE `Project-id` = (SELECT `Project-id` FROM `Workorders` " +
                "                        WHERE `Werkbon-id` = @kind) " +
                "    OR `Werkbon-id` IN (@kind, @doel)", cn))
            {
                cmd.Parameters.AddWithValue("@kind", werkbonId);
                cmd.Parameters.AddWithValue("@doel", doel);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var id = r.GetInt32(0);
                    ouders[id] = r.IsDBNull(1) ? null : r.GetInt32(1);
                    var project = r.IsDBNull(2) ? (int?)null : r.GetInt32(2);
                    if (id == werkbonId) projectVan = project;
                    if (id == doel) projectDoel = project;
                }
            }

            if (projectVan is null || projectDoel is null)
                throw new InvalidOperationException("Werkorder niet gevonden.");
            if (projectVan != projectDoel)
                throw new InvalidOperationException(
                    "Ouder en kind moeten bij hetzelfde project horen.");

            // Omhoog lopen vanaf de beoogde ouder: komen we het kind tegen,
            // dan zou er een kring ontstaan.
            var boven = (int?)doel;
            var stap = 0;
            while (boven is int b && stap++ < 100)
            {
                if (b == werkbonId)
                    throw new InvalidOperationException(
                        "Dat kan niet: die werkorder hangt zelf (via de boom) onder deze.");
                boven = ouders.GetValueOrDefault(b);
            }
        }

        await using var upd = new MySqlCommand(
            "UPDATE `Workorders` SET `ParentWerkbonID` = @ouder WHERE `Werkbon-id` = @id", cn);
        upd.Parameters.AddWithValue("@ouder", (object?)ouderId ?? DBNull.Value);
        upd.Parameters.AddWithValue("@id", werkbonId);
        await upd.ExecuteNonQueryAsync();
    }

    public async Task<Dictionary<string, object?>> GetWerkorderVeldenAsync(int werkbonId)
        => await VeldenAsync("Workorders", "Werkbon-id", werkbonId);

    public async Task UpdateWerkorderVeldAsync(int werkbonId, string kolom, object? waarde)
        => await UpdateVeldAsync("Workorders", "Werkbon-id", await WerkorderKolommenAsync(),
                                 werkbonId, kolom, waarde);

    public async Task<int> NieuweWerkorderAsync(int projectId, string omschrijving, int? door)
    {
        var kolommen = await WerkorderKolommenAsync();

        await using var cn = await OpenAsync();

        // volgend werkbonnummer binnen dit project
        int volgend;
        await using (var max = new MySqlCommand(
            "SELECT MAX(`Werkbonnummer`) FROM `Workorders` WHERE `Project-id` = @id", cn))
        {
            max.Parameters.AddWithValue("@id", projectId);
            var res = await max.ExecuteScalarAsync();
            volgend = res is null or DBNull ? 1 : Convert.ToInt32(res) + 1;
        }

        var sets = new List<string>
        {
            "`Project-id` = @proj",
            "`Werkbonnummer` = @nr",
            "`Werkbonomschrijving` = @oms",
        };
        if (kolommen.Contains("WorkOrderStatus")) sets.Add("`WorkOrderStatus` = @status");
        if (kolommen.Contains("Aangemaakt")) sets.Add("`Aangemaakt` = @nu");
        if (kolommen.Contains("Aangemaakt_door")) sets.Add("`Aangemaakt_door` = @door");
        if (kolommen.Contains("Doorbelast deel")) sets.Add("`Doorbelast deel` = 100");
        if (kolommen.Contains("InkoopToegestaan")) sets.Add("`InkoopToegestaan` = 1");

        await using var cmd = new MySqlCommand(
            $"INSERT INTO `Workorders` SET {string.Join(", ", sets)}", cn);
        cmd.Parameters.AddWithValue("@proj", projectId);
        cmd.Parameters.AddWithValue("@nr", volgend);
        cmd.Parameters.AddWithValue("@oms", omschrijving ?? "");
        if (kolommen.Contains("WorkOrderStatus"))
            cmd.Parameters.AddWithValue("@status", ProjectVelden.WerkorderStatussen[0].Id);   // Open
        if (kolommen.Contains("Aangemaakt")) cmd.Parameters.AddWithValue("@nu", DateTime.Now);
        if (kolommen.Contains("Aangemaakt_door")) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    /// <summary>
    /// Wissen gaat 'zacht': de tabel heeft Deleted, DeletedBy en DeletedAt, dus de
    /// werkorder blijft bestaan voor de urenhistorie maar verdwijnt uit de lijsten.
    /// Alleen als die kolommen er niet zijn wordt de regel echt verwijderd.
    /// </summary>
    public async Task VerwijderWerkorderAsync(int werkbonId, int? door)
    {
        var kolommen = await WerkorderKolommenAsync();
        await using var cn = await OpenAsync();

        if (kolommen.Contains("Deleted"))
        {
            var sets = new List<string> { "`Deleted` = 1" };
            if (kolommen.Contains("DeletedBy")) sets.Add("`DeletedBy` = @door");
            if (kolommen.Contains("DeletedAt")) sets.Add("`DeletedAt` = @nu");

            await using var cmd = new MySqlCommand(
                $"UPDATE `Workorders` SET {string.Join(", ", sets)} WHERE `Werkbon-id` = @id", cn);
            cmd.Parameters.AddWithValue("@id", werkbonId);
            if (kolommen.Contains("DeletedBy")) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            if (kolommen.Contains("DeletedAt")) cmd.Parameters.AddWithValue("@nu", DateTime.Now);
            await cmd.ExecuteNonQueryAsync();
            return;
        }

        await using var del = new MySqlCommand("DELETE FROM `Workorders` WHERE `Werkbon-id` = @id", cn);
        del.Parameters.AddWithValue("@id", werkbonId);
        await del.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Klantafspraak (de grijze percentages, voor de << knop)
    // ============================================================
    public async Task<KlantAfspraak> GetKlantAfspraakAsync(int projectId)
    {
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT t.`DefaultToeslagHours`, t.`DefaultToeslagExpenses`, t.`DefaultToeslagMaterials` " +
                "  FROM `Tariefafspraken` t " +
                "  JOIN `Projects` p ON p.`Customer-id` = t.`Relation-id` " +
                " WHERE p.`Project-Id` = @id " +
                "   AND (t.`Geldigheid start` IS NULL OR t.`Geldigheid start` <= CURDATE()) " +
                "   AND (t.`Geldigheid stop`  IS NULL OR t.`Geldigheid stop`  >= CURDATE()) " +
                " ORDER BY t.`Geldigheid start` DESC LIMIT 1", cn);
            cmd.Parameters.AddWithValue("@id", projectId);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                decimal? D(int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
                return new KlantAfspraak(D(0), D(1), D(2));
            }
        }
        catch { /* geen tariefafspraken of afwijkend schema */ }

        return new KlantAfspraak(null, null, null);
    }

    // ============================================================
    //  Keuzelijsten
    // ============================================================
    public async Task<List<Keuze>> GetWerkmaatschappijenAsync()
    {
        var lijst = new List<Keuze>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `id_supplier`, `name` FROM `Relations` " +
            "WHERE `werkmaatschappij` <> 0 ORDER BY `name`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Keuze(r.GetInt32(0), r.IsDBNull(1) ? "(geen naam)" : r.GetString(1)));
        return lijst;
    }

    public async Task<List<Keuze>> GetKlantenAsync()
    {
        var lijst = new List<Keuze>();
        var kolommen = _relatieKolommen ??= await KolommenVan("Relations");
        var klant = new[] { "IsKlant", "Isklant", "Klant", "Customer" }.FirstOrDefault(kolommen.Contains);
        var verberg = kolommen.Contains("Verberg") ? "AND (`Verberg` = 0 OR `Verberg` IS NULL) " : "";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `id_supplier`, `name` FROM `Relations` " +
            $"WHERE 1 = 1 {(klant is null ? "" : $"AND `{klant}` <> 0 ")}{verberg}" +
            "ORDER BY `name` LIMIT 5000", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Keuze(r.GetInt32(0), r.IsDBNull(1) ? "(geen naam)" : r.GetString(1)));
        return lijst;
    }

    public async Task<List<Keuze>> GetWerknemersAsync()
    {
        var lijst = new List<Keuze>();
        var kolommen = await KolommenVan("Werknemers");
        var inDienst = kolommen.Contains("in dienst") ? "WHERE `in dienst` <> 0 " : "";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Werknemer-id`, COALESCE(`NaamVoluit`, CONCAT_WS(' ', `Voornaam`, `Achternaam`)) AS naam " +
            $"  FROM `Werknemers` {inDienst}ORDER BY naam", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Keuze(r.GetInt32(0), r.IsDBNull(1) ? "(geen naam)" : r.GetString(1)));
        return lijst;
    }

    public Task<List<Keuze>> GetProjectsoortenAsync()
        => LijstAsync(new[] { "Project_type", "Projecttype", "ProjectTypes", "Project_types", "Projectsoort" },
                      new[] { "Project_type_id", "ProjectTypeId", "id" },
                      new[] { "Omschrijving", "Description", "Naam", "name", "Type" });

    public Task<List<Keuze>> GetWerkordersoortenAsync()
        => LijstAsync(new[] { "Workorder_type", "WorkorderType", "WorkorderTypes", "Werkbontype", "Werkbonsoort" },
                      new[] { "Workorder type", "Workorder_type_id", "WorkorderTypeId", "id" },
                      new[] { "Omschrijving", "Description", "Naam", "name", "Type" });

    /// <summary>
    /// Kleine hulplijst voor opzoektabellen waarvan ik de precieze naam niet weet:
    /// probeer een paar gangbare namen, en pak daarin de eerste passende id- en
    /// naamkolom. Lukt het niet, dan blijft de lijst leeg en toont het scherm het nummer.
    /// </summary>
    private async Task<List<Keuze>> LijstAsync(string[] tabellen, string[] idNamen, string[] naamNamen)
    {
        foreach (var tabel in tabellen)
        {
            var lijst = new List<Keuze>();
            try
            {
                await using var cn = await OpenAsync();
                await using var cmd = new MySqlCommand($"SELECT * FROM `{tabel}` LIMIT 500", cn);
                await using var r = await cmd.ExecuteReaderAsync();

                while (await r.ReadAsync())
                {
                    int? id = null;
                    string? naam = null;
                    for (var i = 0; i < r.FieldCount; i++)
                    {
                        var kolom = r.GetName(i);
                        if (id is null && idNamen.Any(n => string.Equals(n, kolom, StringComparison.OrdinalIgnoreCase))
                            && !r.IsDBNull(i) && int.TryParse(r.GetValue(i).ToString(), out var v)) id = v;
                        if (naam is null && naamNamen.Any(n => string.Equals(n, kolom, StringComparison.OrdinalIgnoreCase))
                            && !r.IsDBNull(i)) naam = r.GetValue(i).ToString();
                    }
                    if (id is not null) lijst.Add(new Keuze(id.Value, naam ?? $"#{id}"));
                }
                if (lijst.Count > 0) return lijst.OrderBy(k => k.Naam).ToList();
            }
            catch { /* volgende tabelnaam proberen */ }
        }
        return new List<Keuze>();
    }

    public async Task<bool> IsProjectleiderAsync(int werknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT EXISTS (SELECT 1 FROM `Projects` WHERE `ProjectResponsible` = @wn AND `ProjectStatus` = @open)", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        cmd.Parameters.AddWithValue("@open", ProjectVelden.ProjectStatussen[0].Id);      // Open
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 0;
    }
}
