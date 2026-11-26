using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Persoon voor "Toegewezen aan": alle werknemers, ook uit dienst of geen persoon (dan alleen nog tonen, niet kiezen).
/// Kiesbaar = in dienst (`Uit dienst` = nee) en een echt persoon (`Persoon` = ja, dus niet "MDE Server" of "Niemand").
/// </summary>
public record ActPersoon(int Id, string Naam, bool InDienst, bool Persoon = true)
{
    public bool Kiesbaar => InDienst && Persoon;
}

/// <summary>
/// Activity board (ActivityBoard_MariaDB.sql). Elke wijziging gaat direct naar de database; het scherm
/// laadt na een wijziging van een collega het hele board opnieuw (zie <see cref="ActivityBoardLive"/>).
/// Iedereen met toegang mag alles (spec §7: geen rollen).
/// </summary>
public interface IActivityBoardRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabellenAanwezigAsync();

    Task<List<ActBoardRegel>> GetBoardsAsync();
    /// <summary>Kunnen boards bij een klant of vrij zijn? (ActivityBoard_Soorten_MariaDB.sql uitgevoerd)</summary>
    Task<bool> SoortenAanwezigAsync();
    /// <summary>Het board van dit project; bestaat er nog geen, dan wordt het aangemaakt (release 1: één per project).</summary>
    Task<int> ZorgVoorBoardAsync(int projectId, int? door);
    /// <summary>Het board van deze klant (één per klant); zo nodig aangemaakt.</summary>
    Task<int> ZorgVoorKlantBoardAsync(int relatieId, int? door);
    /// <summary>Kunnen boards bij een werkorder? (ActivityBoard_Werkorder_Bijlagen_MariaDB.sql uitgevoerd)</summary>
    Task<bool> WerkorderAanwezigAsync();
    /// <summary>Het board van deze werkorder (één per werkorder); zo nodig aangemaakt.</summary>
    Task<int> ZorgVoorWerkorderBoardAsync(int werkbonId, int? door);
    /// <summary>Nieuw vrij board met alleen een naam.</summary>
    Task<int> NieuwVrijBoardAsync(string naam, int? door);
    /// <summary>Eigen naam van het board; leeg = de naam van het project of de klant.</summary>
    Task HernoemBoardAsync(int boardId, string naam);
    /// <summary>
    /// Het board aan een project, een werkorder of een klant hangen (of los: alle null). Geeft een melding als dat al
    /// een ander board heeft (één per project, per werkorder en per klant), anders null.
    /// </summary>
    Task<string?> KoppelBoardAsync(int boardId, int? projectId, int? relatieId, int? werkorderId = null);
    /// <summary>
    /// Het hele board wissen (Directie of wie het board aanmaakte; de controle zit in het scherm): onderwerpen, types, activiteiten en hun
    /// koppelingen gaan mee via ON DELETE CASCADE. Bijlagen van onderwerpen blijven in de filestore (soort 342).
    /// </summary>
    Task VerwijderBoardAsync(int boardId);
    Task<ActBoardData?> LaadAsync(int boardId);
    /// <summary>Het board waar deze activiteit op staat (link uit de bel), of null als hij niet meer bestaat.</summary>
    Task<int?> BoardVanActiviteitAsync(int activiteitId);
    Task<List<ActPersoon>> GetPersonenAsync();
    /// <summary>Kan een activiteit een benodigde tijd hebben? (ActivityBoard_Tijd_MariaDB.sql uitgevoerd)</summary>
    Task<bool> TijdAanwezigAsync();

    Task<int> VoegProbleemToeAsync(int boardId, string naam, string kleur);
    Task HernoemProbleemAsync(int probleemId, string naam);
    /// <summary>Nieuwe volgorde van alle problemen van het board: positie 1..n.</summary>
    Task ZetProbleemVolgordeAsync(int boardId, IReadOnlyList<int> probleemIds);
    Task VerwijderProbleemAsync(int probleemId);

    Task<int> VoegTypeToeAsync(int boardId, string naam, string kleur);
    Task HernoemTypeAsync(int typeId, string naam);
    Task VerwijderTypeAsync(int typeId);

    /// <summary>Nieuwe activiteit met koppelingen; geeft het id. De initiator is <c>a.GemaaktDoor</c>, anders <paramref name="door"/>.</summary>
    Task<int> NieuweActiviteitAsync(int boardId, Activiteit a, int? door);
    /// <summary>Eén veld: Titel, Prioriteit, Vervaldatum, BenodigdeUren of een van de tekstvelden.</summary>
    Task ZetVeldAsync(int activiteitId, string kolom, object? waarde, int? door);
    Task ZetStatusAsync(int activiteitId, string status, DateTime? geslotenOp, int? door);
    /// <summary>Koppelingen vervangen; soort = "problemen", "types" of "personen".</summary>
    Task ZetKoppelingenAsync(int activiteitId, string soort, IReadOnlyCollection<int> ids, int? door);
    /// <summary>Slepen: status, problemen en de nieuwe volgorde van het doelvak in één transactie.</summary>
    Task VerplaatsAsync(int activiteitId, string status, DateTime? geslotenOp, IReadOnlyCollection<int> problemen,
                        IReadOnlyList<int> vakVolgorde, int? door);
    Task VerwijderActiviteitAsync(int activiteitId);

    // ---- opmaak in de tekstvelden (ActivityBoard_Opmaak_MariaDB.sql) ----
    /// <summary>Kan een activiteit opmaak (Markdown) in de tekstvelden hebben? (het script is uitgevoerd)</summary>
    Task<bool> OpmaakAanwezigAsync();
    Task ZetOpmaakAsync(int activiteitId, bool opmaak, int? door);

    // ---- koppelingen tussen activiteiten (ActivityBoard_Koppelingen_MariaDB.sql) ----
    /// <summary>Kunnen activiteiten aan elkaar gekoppeld worden? (het script is uitgevoerd)</summary>
    Task<bool> KoppelingenAanwezigAsync();
    /// <summary>
    /// Een koppeling van <paramref name="vanId"/> naar <paramref name="naarId"/>; geeft het id (bestaat hij al, dan dat
    /// id). Een koppeling met zichzelf, of dezelfde soort andersom, geeft <see cref="InvalidOperationException"/> met
    /// de reden in gewone taal.
    /// </summary>
    Task<int> NieuweKoppelingAsync(int vanId, int naarId, string soort, int? door);
    Task VerwijderKoppelingAsync(int koppelingId, int? door);
    /// <summary>De twee activiteiten van een koppeling, of null als hij niet (meer) bestaat.</summary>
    Task<(int VanId, int NaarId)?> KoppelingAsync(int koppelingId);
    /// <summary>Activiteiten zoeken op nummer (123 of #123) of op woorden uit de titel, over alle boards.</summary>
    Task<List<ActKoppelDoel>> ZoekActiviteitenAsync(string zoek, int max);
    /// <summary>Alle activiteiten van deze werknemer die nog niet klaar zijn, over alle boards; eerst wat het eerst vervalt.</summary>
    Task<List<ActMijn>> MijnOpenAsync(int werknemerId);

    // ---- afvinklijst (ActivityBoard_Checklist_MariaDB.sql) ----
    /// <summary>Kan een activiteit een afvinklijst hebben? (het script is uitgevoerd)</summary>
    Task<bool> ChecklistAanwezigAsync();
    /// <summary>
    /// Een punt of kopje erbij; geeft het id (en zet het in <paramref name="punt"/>). <paramref name="naPuntId"/>: null =
    /// onderaan, 0 = bovenaan, anders direct na die regel.
    /// </summary>
    Task<int> NieuwPuntAsync(int activiteitId, ActPunt punt, int? naPuntId, int? door);
    Task ZetPuntTekstAsync(int puntId, string tekst, int? door);
    /// <summary>Afvinken of het vinkje weghalen; bij afvinken worden wie en wanneer bewaard.</summary>
    Task ZetPuntKlaarAsync(int puntId, bool klaar, int? door);
    /// <summary>De nieuwe volgorde van alle regels van de lijst.</summary>
    Task ZetPuntVolgordeAsync(int activiteitId, IReadOnlyList<int> puntIds, int? door);
    /// <summary>Een regel weg. Bij een kopje blijven de punten staan; ze horen dan bij het kopje erboven.</summary>
    Task VerwijderPuntAsync(int puntId, int? door);
    /// <summary>De activiteit van deze regel, of null als hij niet (meer) bestaat.</summary>
    Task<int?> ActiviteitVanPuntAsync(int puntId);
}

/// <summary>Demo-modus zonder database.</summary>
public class GeenActivityBoardRepository : IActivityBoardRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabellenAanwezigAsync() => Task.FromResult(false);
    public Task<List<ActBoardRegel>> GetBoardsAsync() => Task.FromResult(new List<ActBoardRegel>());
    public Task<bool> SoortenAanwezigAsync() => Task.FromResult(false);
    public Task<int> ZorgVoorBoardAsync(int projectId, int? door) => Task.FromResult(0);
    public Task<int> ZorgVoorKlantBoardAsync(int relatieId, int? door) => Task.FromResult(0);
    public Task<bool> WerkorderAanwezigAsync() => Task.FromResult(false);
    public Task<int> ZorgVoorWerkorderBoardAsync(int werkbonId, int? door) => Task.FromResult(0);
    public Task<int> NieuwVrijBoardAsync(string naam, int? door) => Task.FromResult(0);
    public Task HernoemBoardAsync(int boardId, string naam) => Task.CompletedTask;
    public Task<string?> KoppelBoardAsync(int boardId, int? projectId, int? relatieId, int? werkorderId = null) => Task.FromResult<string?>(null);
    public Task VerwijderBoardAsync(int boardId) => Task.CompletedTask;
    public Task<ActBoardData?> LaadAsync(int boardId) => Task.FromResult<ActBoardData?>(null);
    public Task<int?> BoardVanActiviteitAsync(int activiteitId) => Task.FromResult<int?>(null);
    public Task<List<ActPersoon>> GetPersonenAsync() => Task.FromResult(new List<ActPersoon>());
    public Task<bool> TijdAanwezigAsync() => Task.FromResult(false);
    public Task<int> VoegProbleemToeAsync(int boardId, string naam, string kleur) => Task.FromResult(0);
    public Task HernoemProbleemAsync(int probleemId, string naam) => Task.CompletedTask;
    public Task ZetProbleemVolgordeAsync(int boardId, IReadOnlyList<int> probleemIds) => Task.CompletedTask;
    public Task VerwijderProbleemAsync(int probleemId) => Task.CompletedTask;
    public Task<int> VoegTypeToeAsync(int boardId, string naam, string kleur) => Task.FromResult(0);
    public Task HernoemTypeAsync(int typeId, string naam) => Task.CompletedTask;
    public Task VerwijderTypeAsync(int typeId) => Task.CompletedTask;
    public Task<int> NieuweActiviteitAsync(int boardId, Activiteit a, int? door) => Task.FromResult(0);
    public Task ZetVeldAsync(int activiteitId, string kolom, object? waarde, int? door) => Task.CompletedTask;
    public Task ZetStatusAsync(int activiteitId, string status, DateTime? geslotenOp, int? door) => Task.CompletedTask;
    public Task ZetKoppelingenAsync(int activiteitId, string soort, IReadOnlyCollection<int> ids, int? door) => Task.CompletedTask;
    public Task VerplaatsAsync(int activiteitId, string status, DateTime? geslotenOp, IReadOnlyCollection<int> problemen,
                               IReadOnlyList<int> vakVolgorde, int? door) => Task.CompletedTask;
    public Task VerwijderActiviteitAsync(int activiteitId) => Task.CompletedTask;
    public Task<bool> OpmaakAanwezigAsync() => Task.FromResult(false);
    public Task ZetOpmaakAsync(int activiteitId, bool opmaak, int? door) => Task.CompletedTask;
    public Task<bool> KoppelingenAanwezigAsync() => Task.FromResult(false);
    public Task<int> NieuweKoppelingAsync(int vanId, int naarId, string soort, int? door) => Task.FromResult(0);
    public Task VerwijderKoppelingAsync(int koppelingId, int? door) => Task.CompletedTask;
    public Task<(int VanId, int NaarId)?> KoppelingAsync(int koppelingId) => Task.FromResult<(int, int)?>(null);
    public Task<List<ActKoppelDoel>> ZoekActiviteitenAsync(string zoek, int max) => Task.FromResult(new List<ActKoppelDoel>());
    public Task<List<ActMijn>> MijnOpenAsync(int werknemerId) => Task.FromResult(new List<ActMijn>());
    public Task<bool> ChecklistAanwezigAsync() => Task.FromResult(false);
    public Task<int> NieuwPuntAsync(int activiteitId, ActPunt punt, int? naPuntId, int? door) => Task.FromResult(0);
    public Task ZetPuntTekstAsync(int puntId, string tekst, int? door) => Task.CompletedTask;
    public Task ZetPuntKlaarAsync(int puntId, bool klaar, int? door) => Task.CompletedTask;
    public Task ZetPuntVolgordeAsync(int activiteitId, IReadOnlyList<int> puntIds, int? door) => Task.CompletedTask;
    public Task VerwijderPuntAsync(int puntId, int? door) => Task.CompletedTask;
    public Task<int?> ActiviteitVanPuntAsync(int puntId) => Task.FromResult<int?>(null);
}

public class MySqlActivityBoardRepository : IActivityBoardRepository
{
    private readonly string _cs;
    public MySqlActivityBoardRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static int? I(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static DateTime? D(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));
    private static string Kort(string s, int max) => s.Length <= max ? s : s[..max];

    public async Task<bool> TabellenAanwezigAsync()
    {
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN " +
                "('ActivityBoards','ActivityBoardProblemen','ActivityBoardTypes','ActivityBoardActiviteiten'," +
                " 'ActivityBoardActProblemen','ActivityBoardActTypes','ActivityBoardActPersonen')", cn);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 7;
        }
        catch { return false; }
    }

    // ------------------------------------------------------------------ boards

    // Kolom RelatieID aanwezig? Alleen 'ja' wordt onthouden, zodat het script uitvoeren zonder herstart werkt.
    private bool _metRelatie;
    public async Task<bool> SoortenAanwezigAsync()
    {
        if (_metRelatie) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "   AND TABLE_NAME = 'ActivityBoards' AND COLUMN_NAME = 'RelatieID'", cn);
            _metRelatie = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _metRelatie;
    }

    // Kolom WerkorderID aanwezig? Alleen 'ja' onthouden (ActivityBoard_Werkorder_Bijlagen_MariaDB.sql).
    private bool _metWerkorder;
    public async Task<bool> WerkorderAanwezigAsync()
    {
        if (_metWerkorder) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "   AND TABLE_NAME = 'ActivityBoards' AND COLUMN_NAME = 'WerkorderID'", cn);
            _metWerkorder = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _metWerkorder;
    }

    // Kolom BenodigdeUren aanwezig? Alleen 'ja' onthouden (ActivityBoard_Tijd_MariaDB.sql).
    private bool _metTijd;
    public async Task<bool> TijdAanwezigAsync()
    {
        if (_metTijd) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "   AND TABLE_NAME = 'ActivityBoardActiviteiten' AND COLUMN_NAME = 'BenodigdeUren'", cn);
            _metTijd = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _metTijd;
    }

    /// <summary>Kolommen (id, nummer 218183-03, omschrijving) en join voor de werkorder, of lege kolommen zonder het script.</summary>
    private async Task<(string Kolommen, string Join)> WerkorderDeelAsync()
        => await WerkorderAanwezigAsync()
            ? (", b.`WerkorderID`, COALESCE(CONCAT(wp.`Projectnumber`, '-', RIGHT(CONCAT('00', wo.`Werkbonnummer`), 2)), ''), " +
               "COALESCE(wo.`Werkbonomschrijving`, '')",
               " LEFT JOIN `Workorders` wo ON wo.`Werkbon-id` = b.`WerkorderID` LEFT JOIN `Projects` wp ON wp.`Project-Id` = wo.`Project-id` ")
            : (", NULL, '', ''", " ");

    /// <summary>Kolom en join voor de klant, of niets als het script nog niet gedraaid heeft.</summary>
    private async Task<(string Kolommen, string Join)> RelatieDeelAsync()
        => await SoortenAanwezigAsync()
            ? (", b.`RelatieID`, COALESCE(r.`name`, '')", " LEFT JOIN `Relations` r ON r.`id_supplier` = b.`RelatieID` ")
            : (", NULL, ''", " ");

    /// <summary>De statussen van de groep Klaar als SQL-lijst ('opgelost','nietopgelost','geannuleerd'); vaste codes, geen invoer.</summary>
    private static readonly string KlaarStatussen =
        string.Join(",", ActStatus.Alle.Where(s => s.Groep == ActStatus.GroepKlaar).Select(s => "'" + s.Code + "'"));

    public async Task<List<ActBoardRegel>> GetBoardsAsync()
    {
        var lijst = new List<ActBoardRegel>();
        var (rk, rj) = await RelatieDeelAsync();
        var (wk, wj) = await WerkorderDeelAsync();
        // voor de boom: klant van het project (projectboard), en bij een werkorder het project, de klant en de ouderwerkorder
        var metWo = await WerkorderAanwezigAsync();
        var bk = ", p.`Customer-id`, COALESCE(pr.`name`, '')" + (metWo
            ? ", wp.`Project-Id`, wp.`Projectnumber`, COALESCE(wp.`Projectdescription`, ''), wp.`Customer-id`, COALESCE(wr.`name`, ''), " +
              "NULLIF(wo.`ParentWerkbonID`, 0), COALESCE(CONCAT(wp2.`Projectnumber`, '-', RIGHT(CONCAT('00', wo2.`Werkbonnummer`), 2)), ''), " +
              "COALESCE(wo2.`Werkbonomschrijving`, '')"
            : ", NULL, NULL, '', NULL, '', NULL, '', ''");
        var bj = " LEFT JOIN `Relations` pr ON pr.`id_supplier` = p.`Customer-id` " + (metWo
            ? " LEFT JOIN `Relations` wr ON wr.`id_supplier` = wp.`Customer-id` " +
              " LEFT JOIN `Workorders` wo2 ON wo2.`Werkbon-id` = NULLIF(wo.`ParentWerkbonID`, 0) " +
              " LEFT JOIN `Projects` wp2 ON wp2.`Project-Id` = wo2.`Project-id` "
            : " ");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT b.`BoardID`, b.`ProjectID`, p.`Projectnumber`, COALESCE(p.`Projectdescription`, ''), COALESCE(b.`Naam`, ''), " +
            "       SUM(a.`ActiviteitID` IS NOT NULL AND a.`Status` NOT IN (" + KlaarStatussen + ")), " +
            "       SUM(a.`Status` IN (" + KlaarStatussen + ")), " +
            "       GREATEST(COALESCE(MAX(a.`last_updated`), b.`last_updated`), b.`last_updated`)" + rk + wk + bk +
            "  FROM `ActivityBoards` b " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = b.`ProjectID` " + rj + wj + bj +
            "  LEFT JOIN `ActivityBoardActiviteiten` a ON a.`BoardID` = b.`BoardID` " +
            " GROUP BY b.`BoardID` ORDER BY 8 DESC", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var relatie = I(r, 8);
            var werkorder = I(r, 10);
            lijst.Add(new ActBoardRegel(r.GetInt32(0), I(r, 1), I(r, 2), S(r, 3), S(r, 4),
                                        I(r, 5) ?? 0, I(r, 6) ?? 0, D(r, 7), relatie, S(r, 9), werkorder, S(r, 11), S(r, 12))
            {
                // klant: klantboard = zichzelf; werkorderboard = klant van zijn project; projectboard = klant van het project
                KlantId = relatie ?? (werkorder is not null ? I(r, 18) : I(r, 13)),
                KlantNaam = relatie is not null ? S(r, 9) : werkorder is not null ? S(r, 19) : S(r, 14),
                HoofdProjectId = I(r, 15), HoofdProjectNummer = I(r, 16), HoofdProjectNaam = S(r, 17),
                OuderWerkorderId = I(r, 20), OuderWerkorderNummer = S(r, 21), OuderWerkorderNaam = S(r, 22),
            });
        }
        return lijst;
    }

    public Task<int> ZorgVoorBoardAsync(int projectId, int? door) => ZorgVoorAsync("ProjectID", projectId, door);
    public Task<int> ZorgVoorKlantBoardAsync(int relatieId, int? door) => ZorgVoorAsync("RelatieID", relatieId, door);
    public Task<int> ZorgVoorWerkorderBoardAsync(int werkbonId, int? door) => ZorgVoorAsync("WerkorderID", werkbonId, door);

    private async Task<int> ZorgVoorAsync(string kolom, int waarde, int? door)
    {
        await using var cn = await OpenAsync();
        await using (var zoek = new MySqlCommand(
            $"SELECT `BoardID` FROM `ActivityBoards` WHERE `{kolom}` = @p ORDER BY `Volgorde`, `BoardID` LIMIT 1", cn))
        {
            zoek.Parameters.AddWithValue("@p", waarde);
            if (await zoek.ExecuteScalarAsync() is { } id and not DBNull) return Convert.ToInt32(id);
        }
        await using var cmd = new MySqlCommand(
            $"INSERT INTO `ActivityBoards` (`{kolom}`, `AangemaaktDoor`, `AangemaaktOp`) VALUES (@p, @door, NOW()); " +
            "SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@p", waarde);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<int> NieuwVrijBoardAsync(string naam, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ActivityBoards` (`ProjectID`, `Naam`, `AangemaaktDoor`, `AangemaaktOp`) VALUES (NULL, @n, @door, NOW()); " +
            "SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@n", Kort(naam.Trim(), 200));
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public Task HernoemBoardAsync(int boardId, string naam)
        => UitvoerenAsync("UPDATE `ActivityBoards` SET `Naam` = @n WHERE `BoardID` = @b",
                          ("@n", naam.Trim().Length > 0 ? Kort(naam.Trim(), 200) : DBNull.Value), ("@b", boardId));

    public Task VerwijderBoardAsync(int boardId)
        => UitvoerenAsync("DELETE FROM `ActivityBoards` WHERE `BoardID` = @b", ("@b", boardId));

    public async Task<string?> KoppelBoardAsync(int boardId, int? projectId, int? relatieId, int? werkorderId = null)
    {
        var metWerkorder = await WerkorderAanwezigAsync();
        if (werkorderId is not null && !metWerkorder) return "Boards bij een werkorder kan pas na ActivityBoard_Werkorder_Bijlagen_MariaDB.sql.";
        // precies één koppeling (werkorder gaat voor project, project voor klant)
        if (werkorderId is not null) { projectId = null; relatieId = null; }
        else if (projectId is not null) relatieId = null;
        await using var cn = await OpenAsync();
        // één board per project, per werkorder en per klant
        var (kolom, waarde, melding) = werkorderId is not null ? ("WerkorderID", werkorderId, "Deze werkorder heeft al een board.")
                                     : projectId is not null ? ("ProjectID", projectId, "Dit project heeft al een board.")
                                     : ("RelatieID", relatieId, "Deze klant heeft al een board.");
        if (waarde is not null)
        {
            await using var bezet = new MySqlCommand(
                $"SELECT COUNT(*) FROM `ActivityBoards` WHERE `BoardID` <> @b AND `{kolom}` = @x", cn);
            bezet.Parameters.AddWithValue("@b", boardId);
            bezet.Parameters.AddWithValue("@x", waarde.Value);
            if (Convert.ToInt32(await bezet.ExecuteScalarAsync()) > 0) return melding;
        }
        await using var cmd = new MySqlCommand(
            "UPDATE `ActivityBoards` SET `ProjectID` = @p, `RelatieID` = @r" + (metWerkorder ? ", `WerkorderID` = @w" : "") +
            " WHERE `BoardID` = @b", cn);
        cmd.Parameters.AddWithValue("@p", (object?)projectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@r", (object?)relatieId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@w", (object?)werkorderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@b", boardId);
        await cmd.ExecuteNonQueryAsync();
        return null;
    }

    public async Task<ActBoardData?> LaadAsync(int boardId)
    {
        var data = new ActBoardData();
        await using var cn = await OpenAsync();

        var (rk, rj) = await RelatieDeelAsync();
        var (wk, wj) = await WerkorderDeelAsync();
        await using (var cmd = new MySqlCommand(
            "SELECT b.`BoardID`, b.`ProjectID`, p.`Projectnumber`, COALESCE(p.`Projectdescription`, ''), COALESCE(b.`Naam`, '')" + rk + wk +
            ", b.`AangemaaktDoor`" +
            "  FROM `ActivityBoards` b LEFT JOIN `Projects` p ON p.`Project-Id` = b.`ProjectID` " + rj + wj + " WHERE b.`BoardID` = @b", cn))
        {
            cmd.Parameters.AddWithValue("@b", boardId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            data.Board = new ActBoard(r.GetInt32(0), I(r, 1), I(r, 2), S(r, 3), S(r, 4), I(r, 5), S(r, 6), I(r, 7), S(r, 8), S(r, 9))
            { AangemaaktDoor = I(r, 10) };
        }

        await using (var cmd = new MySqlCommand(
            "SELECT `ProbleemID`, `Naam`, `Kleur`, `Positie` FROM `ActivityBoardProblemen` WHERE `BoardID` = @b " +
            " ORDER BY `Positie`, `ProbleemID`", cn))
        {
            cmd.Parameters.AddWithValue("@b", boardId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                data.Problemen.Add(new ActProbleem { Id = r.GetInt32(0), Naam = S(r, 1), Kleur = ActKleur.Geldig(S(r, 2)), Positie = r.GetInt32(3) });
        }

        await using (var cmd = new MySqlCommand(
            "SELECT `TypeID`, `Naam`, `Kleur` FROM `ActivityBoardTypes` WHERE `BoardID` = @b ORDER BY `Positie`, `TypeID`", cn))
        {
            cmd.Parameters.AddWithValue("@b", boardId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                data.Types.Add(new ActType { Id = r.GetInt32(0), Naam = S(r, 1), Kleur = ActKleur.Geldig(S(r, 2)) });
        }

        var perId = new Dictionary<int, Activiteit>();
        var metTijd = await TijdAanwezigAsync();
        var metOpmaak = await OpmaakAanwezigAsync();
        await using (var cmd = new MySqlCommand(
            "SELECT `ActiviteitID`, `Titel`, `Status`, `Prioriteit`, `Vervaldatum`, `GemaaktOp`, `GeslotenOp`, `Volgorde`, " +
            "       `Samenvatting`, `Details`, `Impact`, `GenomenStappen`, `VolgendeStappen`, `Opmerkingen`, " +
            (metTijd ? "`BenodigdeUren`" : "NULL") + ", `GemaaktDoor`, " + (metOpmaak ? "`Opmaak`" : "0") + ", `last_updated`, `GewijzigdDoor`" +
            "  FROM `ActivityBoardActiviteiten` WHERE `BoardID` = @b", cn))
        {
            cmd.Parameters.AddWithValue("@b", boardId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                // beschadigde gegevens mogen het scherm niet blokkeren (spec §7): opschonen en doorwerken
                var a = new Activiteit
                {
                    Id = r.GetInt32(0),
                    Titel = S(r, 1).Trim() is { Length: > 0 } t ? t : "Zonder titel",
                    Status = ActStatus.IsGeldig(S(r, 2)) ? S(r, 2) : ActStatus.Backlog,
                    Prioriteit = I(r, 3) is int p && p is >= 1 and <= 3 ? p : null,
                    Vervaldatum = D(r, 4)?.Date,
                    GemaaktOp = D(r, 5) ?? DateTime.Now,
                    GeslotenOp = D(r, 6),
                    Volgorde = I(r, 7) ?? 0,
                    BenodigdeUren = r.IsDBNull(14) ? null : Convert.ToDecimal(r.GetValue(14)) is > 0 and var bu ? bu : null,
                    GemaaktDoor = I(r, 15),
                    Opmaak = Convert.ToInt32(r.GetValue(16)) != 0,
                    GewijzigdOp = D(r, 17), GewijzigdDoor = I(r, 18),
                };
                for (var i = 0; i < ActTekst.Velden.Length; i++)
                    if (S(r, 8 + i) is { Length: > 0 } tekst) a.Tekst[ActTekst.Velden[i].Kolom] = tekst;
                data.Activiteiten.Add(a);
                perId[a.Id] = a;
            }
        }

        async Task Koppel(string tabel, string kolom, Func<Activiteit, List<int>> lijst)
        {
            await using var cmd = new MySqlCommand(
                $"SELECT k.`ActiviteitID`, k.`{kolom}` FROM `{tabel}` k " +
                "  JOIN `ActivityBoardActiviteiten` a ON a.`ActiviteitID` = k.`ActiviteitID` WHERE a.`BoardID` = @b", cn);
            cmd.Parameters.AddWithValue("@b", boardId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                if (perId.TryGetValue(r.GetInt32(0), out var a)) lijst(a).Add(r.GetInt32(1));
        }
        await Koppel("ActivityBoardActProblemen", "ProbleemID", a => a.Problemen);
        await Koppel("ActivityBoardActTypes", "TypeID", a => a.Types);
        await Koppel("ActivityBoardActPersonen", "WerknemerID", a => a.Personen);

        if (await ChecklistAanwezigAsync())
        {
            await using var cmd = new MySqlCommand(
                "SELECT c.`ActiviteitID`, c.`PuntID`, c.`IsKop`, c.`Tekst`, c.`Klaar`, c.`KlaarOp`, c.`KlaarDoor` " +
                "  FROM `ActivityBoardChecklist` c JOIN `ActivityBoardActiviteiten` a ON a.`ActiviteitID` = c.`ActiviteitID` " +
                " WHERE a.`BoardID` = @b ORDER BY c.`ActiviteitID`, c.`Positie`, c.`PuntID`", cn);
            cmd.Parameters.AddWithValue("@b", boardId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                if (perId.TryGetValue(r.GetInt32(0), out var a))
                    a.Checklist.Add(new ActPunt
                    {
                        Id = r.GetInt32(1), Kop = Convert.ToInt32(r.GetValue(2)) != 0, Tekst = S(r, 3),
                        Klaar = Convert.ToInt32(r.GetValue(4)) != 0, KlaarOp = D(r, 5), KlaarDoor = I(r, 6),
                    });
        }

        if (await KoppelingenAanwezigAsync())
        {
            // van beide kanten: eerst waar een activiteit van dit board de "van"-kant is, dan de "naar"-kant. De andere
            // activiteit kan op een ander board staan; de naam van dat board komt mee.
            foreach (var heen in new[] { true, false })
            {
                var (eigen, ander) = heen ? ("VanID", "NaarID") : ("NaarID", "VanID");
                await using var cmd = new MySqlCommand(
                    $"SELECT k.`KoppelingID`, k.`Soort`, k.`{eigen}`, x.`ActiviteitID`, x.`Titel`, x.`Status`, x.`Prioriteit`, " + BoardKolommen + rk + wk +
                    $"  FROM `ActivityBoardKoppelingen` k JOIN `ActivityBoardActiviteiten` e ON e.`ActiviteitID` = k.`{eigen}` " +
                    $"  JOIN `ActivityBoardActiviteiten` x ON x.`ActiviteitID` = k.`{ander}` " +
                    "  JOIN `ActivityBoards` b ON b.`BoardID` = x.`BoardID` LEFT JOIN `Projects` p ON p.`Project-Id` = b.`ProjectID` " + rj + wj +
                    " WHERE e.`BoardID` = @b ORDER BY k.`KoppelingID`", cn);
                cmd.Parameters.AddWithValue("@b", boardId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    if (perId.TryGetValue(r.GetInt32(2), out var a) && ActKoppelSoort.IsGeldig(S(r, 1)))
                        a.Koppelingen.Add(new ActKoppeling(r.GetInt32(0), S(r, 1), heen, r.GetInt32(3), S(r, 4),
                            ActStatus.IsGeldig(S(r, 5)) ? S(r, 5) : ActStatus.Backlog, r.GetInt32(7), LeesBoard(r, 7).Titel, I(r, 6)));
            }
        }
        ActPrioriteit.Afleiden(data.Activiteiten);
        return data;
    }

    public async Task<int?> BoardVanActiviteitAsync(int activiteitId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT `BoardID` FROM `ActivityBoardActiviteiten` WHERE `ActiviteitID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", activiteitId);
        return await cmd.ExecuteScalarAsync() is { } b and not DBNull ? Convert.ToInt32(b) : null;
    }

    public async Task<List<ActPersoon>> GetPersonenAsync()
    {
        var lijst = new List<ActPersoon>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            // `In dienst` is de datum van indiensttreding; het vlaggetje is `Uit dienst`
            "SELECT `Werknemer-id`, COALESCE(`NaamVoluit`, CONCAT_WS(' ', `Voornaam`, `Achternaam`)) AS naam, COALESCE(`Uit dienst`, 0) = 0, COALESCE(`Persoon`, 0) <> 0 " +
            "  FROM `Werknemers` ORDER BY naam", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ActPersoon(r.GetInt32(0), S(r, 1) is { Length: > 0 } n ? n : "(geen naam)",
                                     Convert.ToInt32(r.GetValue(2)) != 0, Convert.ToInt32(r.GetValue(3)) != 0));
        return lijst;
    }

    // ------------------------------------------------------------------ problemen en types

    public async Task<int> VoegProbleemToeAsync(int boardId, string naam, string kleur)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ActivityBoardProblemen` (`BoardID`, `Naam`, `Kleur`, `Positie`) " +
            "SELECT @b, @n, @k, COALESCE(MAX(`Positie`), 0) + 1 FROM `ActivityBoardProblemen` WHERE `BoardID` = @b; " +
            "SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@b", boardId);
        cmd.Parameters.AddWithValue("@n", Kort(naam.Trim(), 200));
        cmd.Parameters.AddWithValue("@k", ActKleur.Geldig(kleur));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public Task HernoemProbleemAsync(int probleemId, string naam)
        => UitvoerenAsync("UPDATE `ActivityBoardProblemen` SET `Naam` = @n WHERE `ProbleemID` = @id",
                          ("@n", Kort(naam.Trim(), 200)), ("@id", probleemId));

    public async Task ZetProbleemVolgordeAsync(int boardId, IReadOnlyList<int> probleemIds)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        for (var i = 0; i < probleemIds.Count; i++)
        {
            await using var cmd = new MySqlCommand(
                "UPDATE `ActivityBoardProblemen` SET `Positie` = @p WHERE `ProbleemID` = @id AND `BoardID` = @b", cn, tx);
            cmd.Parameters.AddWithValue("@p", i + 1);
            cmd.Parameters.AddWithValue("@id", probleemIds[i]);
            cmd.Parameters.AddWithValue("@b", boardId);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public Task VerwijderProbleemAsync(int probleemId)
        => UitvoerenAsync("DELETE FROM `ActivityBoardProblemen` WHERE `ProbleemID` = @id", ("@id", probleemId));

    public async Task<int> VoegTypeToeAsync(int boardId, string naam, string kleur)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ActivityBoardTypes` (`BoardID`, `Naam`, `Kleur`, `Positie`) " +
            "SELECT @b, @n, @k, COALESCE(MAX(`Positie`), 0) + 1 FROM `ActivityBoardTypes` WHERE `BoardID` = @b; " +
            "SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@b", boardId);
        cmd.Parameters.AddWithValue("@n", Kort(naam.Trim(), 100));
        cmd.Parameters.AddWithValue("@k", ActKleur.Geldig(kleur));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public Task HernoemTypeAsync(int typeId, string naam)
        => UitvoerenAsync("UPDATE `ActivityBoardTypes` SET `Naam` = @n WHERE `TypeID` = @id",
                          ("@n", Kort(naam.Trim(), 100)), ("@id", typeId));

    public Task VerwijderTypeAsync(int typeId)
        => UitvoerenAsync("DELETE FROM `ActivityBoardTypes` WHERE `TypeID` = @id", ("@id", typeId));

    // ------------------------------------------------------------------ activiteiten

    public async Task<int> NieuweActiviteitAsync(int boardId, Activiteit a, int? door)
    {
        var metTijd = await TijdAanwezigAsync();
        var metLijst = a.Checklist.Count > 0 && await ChecklistAanwezigAsync();
        var metOpmaak = await OpmaakAanwezigAsync();
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        int id;
        await using (var cmd = new MySqlCommand(
            "INSERT INTO `ActivityBoardActiviteiten` (`BoardID`, `Titel`, `Status`, `Prioriteit`, `Vervaldatum`, `GemaaktOp`, " +
            "   `GeslotenOp`, `Volgorde`, `GemaaktDoor`, `GewijzigdDoor`, " + (metTijd ? "`BenodigdeUren`, " : "") + (metOpmaak ? "`Opmaak`, " : "") +
            string.Join(", ", ActTekst.Velden.Select(v => $"`{v.Kolom}`")) + ") " +
            "VALUES (@b, @t, @s, @p, @v, @gm, @gs, @vo, @maker, @door, " + (metTijd ? "@uren, " : "") + (metOpmaak ? "@opmaak, " : "") +
            string.Join(", ", ActTekst.Velden.Select(v => "@x" + v.Kolom)) + "); SELECT LAST_INSERT_ID();", cn, tx))
        {
            if (metOpmaak) cmd.Parameters.AddWithValue("@opmaak", a.Opmaak ? 1 : 0);
            if (metTijd) cmd.Parameters.AddWithValue("@uren", (object?)a.BenodigdeUren ?? DBNull.Value);
            // de initiator: wie hem nu aanmaakt, of (terugzetten na verwijderen) wie hem oorspronkelijk aanmaakte
            cmd.Parameters.AddWithValue("@maker", (object?)(a.GemaaktDoor ?? door) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@b", boardId);
            cmd.Parameters.AddWithValue("@t", Kort(a.Titel.Trim(), 300));
            cmd.Parameters.AddWithValue("@s", ActStatus.IsGeldig(a.Status) ? a.Status : ActStatus.Backlog);
            cmd.Parameters.AddWithValue("@p", (object?)a.Prioriteit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@v", (object?)a.Vervaldatum?.Date ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gm", a.GemaaktOp);
            cmd.Parameters.AddWithValue("@gs", (object?)a.GeslotenOp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vo", a.Volgorde);
            cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            foreach (var v in ActTekst.Velden)
                cmd.Parameters.AddWithValue("@x" + v.Kolom, a.Tekst.TryGetValue(v.Kolom, out var x) && x.Length > 0 ? x : DBNull.Value);
            id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        foreach (var soort in new[] { "problemen", "types", "personen" })
            await KoppelingenAsync(cn, tx, id, soort, soort == "problemen" ? a.Problemen : soort == "types" ? a.Types : a.Personen);
        // de afvinklijst mee (nieuw via de API, of terugzetten na verwijderen: dan met de vinkjes zoals ze waren)
        if (metLijst)
            for (var i = 0; i < a.Checklist.Count; i++)
                a.Checklist[i].Id = await VoegPuntInAsync(cn, tx, id, a.Checklist[i], i, door);
        await tx.CommitAsync();
        return id;
    }

    public Task ZetVeldAsync(int activiteitId, string kolom, object? waarde, int? door)
    {
        // alleen deze kolommen; de naam komt nooit van buiten in de query
        if (kolom is not ("Titel" or "Prioriteit" or "Vervaldatum" or "BenodigdeUren") && !ActTekst.IsKolom(kolom))
            throw new ArgumentException($"Onbekend veld {kolom}");
        if (kolom == "Titel" && waarde is string t) waarde = Kort(t.Trim(), 300);
        if (waarde is string s && string.IsNullOrEmpty(s) && kolom != "Titel") waarde = null;
        return UitvoerenAsync($"UPDATE `ActivityBoardActiviteiten` SET `{kolom}` = @w, `GewijzigdDoor` = @door WHERE `ActiviteitID` = @id",
                              ("@w", waarde ?? DBNull.Value), ("@door", (object?)door ?? DBNull.Value), ("@id", activiteitId));
    }

    public Task ZetStatusAsync(int activiteitId, string status, DateTime? geslotenOp, int? door)
        => UitvoerenAsync("UPDATE `ActivityBoardActiviteiten` SET `Status` = @s, `GeslotenOp` = @g, `GewijzigdDoor` = @door " +
                          " WHERE `ActiviteitID` = @id",
                          ("@s", status), ("@g", (object?)geslotenOp ?? DBNull.Value), ("@door", (object?)door ?? DBNull.Value),
                          ("@id", activiteitId));

    public async Task ZetKoppelingenAsync(int activiteitId, string soort, IReadOnlyCollection<int> ids, int? door)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await KoppelingenAsync(cn, tx, activiteitId, soort, ids, wis: true);
        await GewijzigdAsync(cn, tx, activiteitId, door);
        await tx.CommitAsync();
    }

    public async Task VerplaatsAsync(int activiteitId, string status, DateTime? geslotenOp, IReadOnlyCollection<int> problemen,
                                     IReadOnlyList<int> vakVolgorde, int? door)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await using (var cmd = new MySqlCommand(
            "UPDATE `ActivityBoardActiviteiten` SET `Status` = @s, `GeslotenOp` = @g, `GewijzigdDoor` = @door WHERE `ActiviteitID` = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@s", status);
            cmd.Parameters.AddWithValue("@g", (object?)geslotenOp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", activiteitId);
            await cmd.ExecuteNonQueryAsync();
        }
        await KoppelingenAsync(cn, tx, activiteitId, "problemen", problemen, wis: true);
        for (var i = 0; i < vakVolgorde.Count; i++)
        {
            await using var cmd = new MySqlCommand(
                "UPDATE `ActivityBoardActiviteiten` SET `Volgorde` = @v, `last_updated` = `last_updated` WHERE `ActiviteitID` = @id", cn, tx);
            cmd.Parameters.AddWithValue("@v", i);
            cmd.Parameters.AddWithValue("@id", vakVolgorde[i]);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public Task VerwijderActiviteitAsync(int activiteitId)
        => UitvoerenAsync("DELETE FROM `ActivityBoardActiviteiten` WHERE `ActiviteitID` = @id", ("@id", activiteitId));

    // ------------------------------------------------------------------ koppelingen tussen activiteiten

    /// <summary>De kolommen van een board (alias b, project p) waarmee <see cref="LeesBoard"/> de titel maakt; daarna rk en wk.</summary>
    private const string BoardKolommen = "b.`BoardID`, b.`ProjectID`, p.`Projectnumber`, COALESCE(p.`Projectdescription`, ''), COALESCE(b.`Naam`, '')";

    private static ActBoard LeesBoard(MySqlDataReader r, int i)
        => new(r.GetInt32(i), I(r, i + 1), I(r, i + 2), S(r, i + 3), S(r, i + 4), I(r, i + 5), S(r, i + 6), I(r, i + 7), S(r, i + 8), S(r, i + 9));

    // Tabel aanwezig? Alleen 'ja' wordt onthouden, zodat het script uitvoeren zonder herstart werkt.
    private bool _metKoppelingen;
    public async Task<bool> KoppelingenAanwezigAsync()
    {
        if (_metKoppelingen) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ActivityBoardKoppelingen'", cn);
            _metKoppelingen = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _metKoppelingen;
    }

    public async Task<int> NieuweKoppelingAsync(int vanId, int naarId, string soort, int? door)
    {
        if (!ActKoppelSoort.IsGeldig(soort)) throw new ArgumentException($"Onbekende soort koppeling {soort}");
        if (vanId == naarId) throw new InvalidOperationException("Een activiteit kan niet aan zichzelf gekoppeld worden.");
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        // bestaat hij al (bij "zie ook" telt ook andersom), dan is dat hem; dezelfde soort andersom kan niet
        await using (var zoek = new MySqlCommand(
            "SELECT `KoppelingID`, `VanID` FROM `ActivityBoardKoppelingen` " +
            " WHERE `Soort` = @s AND ((`VanID` = @v AND `NaarID` = @n) OR (`VanID` = @n AND `NaarID` = @v)) LIMIT 1", cn, tx))
        {
            zoek.Parameters.AddWithValue("@s", soort);
            zoek.Parameters.AddWithValue("@v", vanId);
            zoek.Parameters.AddWithValue("@n", naarId);
            await using var r = await zoek.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                if (r.GetInt32(1) == vanId || soort == ActKoppelSoort.ZieOok) return r.GetInt32(0);
                throw new InvalidOperationException(soort == ActKoppelSoort.HangtAf
                    ? "Deze twee activiteiten zijn al andersom gekoppeld: de andere hangt af van deze."
                    : "Deze twee activiteiten zijn al andersom gekoppeld: de andere is afgesplitst van deze.");
            }
        }
        int id;
        await using (var cmd = new MySqlCommand(
            "INSERT INTO `ActivityBoardKoppelingen` (`VanID`, `NaarID`, `Soort`, `AangemaaktDoor`) VALUES (@v, @n, @s, @door); SELECT LAST_INSERT_ID();", cn, tx))
        {
            cmd.Parameters.AddWithValue("@v", vanId);
            cmd.Parameters.AddWithValue("@n", naarId);
            cmd.Parameters.AddWithValue("@s", soort);
            cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        await GewijzigdAsync(cn, tx, vanId, door);
        await GewijzigdAsync(cn, tx, naarId, door);
        await tx.CommitAsync();
        return id;
    }

    public async Task<(int VanId, int NaarId)?> KoppelingAsync(int koppelingId)
    {
        if (!await KoppelingenAanwezigAsync()) return null;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT `VanID`, `NaarID` FROM `ActivityBoardKoppelingen` WHERE `KoppelingID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", koppelingId);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? (r.GetInt32(0), r.GetInt32(1)) : null;
    }

    public async Task VerwijderKoppelingAsync(int koppelingId, int? door)
    {
        if (await KoppelingAsync(koppelingId) is not { } k) return;
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await using (var cmd = new MySqlCommand("DELETE FROM `ActivityBoardKoppelingen` WHERE `KoppelingID` = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", koppelingId);
            await cmd.ExecuteNonQueryAsync();
        }
        await GewijzigdAsync(cn, tx, k.VanId, door);
        await GewijzigdAsync(cn, tx, k.NaarId, door);
        await tx.CommitAsync();
    }

    public async Task<List<ActKoppelDoel>> ZoekActiviteitenAsync(string zoek, int max)
    {
        var lijst = new List<ActKoppelDoel>();
        var z = (zoek ?? "").Trim();
        if (z.Length == 0) return lijst;
        var (rk, rj) = await RelatieDeelAsync();
        var (wk, wj) = await WerkorderDeelAsync();
        var nummer = int.TryParse(z.TrimStart('#'), out var n) ? n : (int?)null;
        var woorden = z.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6).ToList();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand { Connection = cn };
        var waar = new List<string>();
        for (var i = 0; i < woorden.Count; i++) { waar.Add($"x.`Titel` LIKE @w{i}"); cmd.Parameters.AddWithValue($"@w{i}", "%" + woorden[i].Replace("%", "\\%").Replace("_", "\\_") + "%"); }
        cmd.Parameters.AddWithValue("@nr", (object?)nummer ?? DBNull.Value);
        cmd.CommandText =
            "SELECT x.`ActiviteitID`, x.`Titel`, x.`Status`, " + BoardKolommen + rk + wk +
            "  FROM `ActivityBoardActiviteiten` x JOIN `ActivityBoards` b ON b.`BoardID` = x.`BoardID` " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = b.`ProjectID` " + rj + wj +
            $" WHERE x.`ActiviteitID` = @nr OR ({string.Join(" AND ", waar)}) " +
            $" ORDER BY (x.`ActiviteitID` <=> @nr) DESC, x.`last_updated` DESC LIMIT {Math.Clamp(max, 1, 50)}";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ActKoppelDoel(r.GetInt32(0), S(r, 1), ActStatus.IsGeldig(S(r, 2)) ? S(r, 2) : ActStatus.Backlog, r.GetInt32(3), LeesBoard(r, 3).Titel));
        return lijst;
    }

    public async Task<List<ActMijn>> MijnOpenAsync(int werknemerId)
    {
        var lijst = new List<ActMijn>();
        var (rk, rj) = await RelatieDeelAsync();
        var (wk, wj) = await WerkorderDeelAsync();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT x.`ActiviteitID`, x.`Titel`, x.`Status`, x.`Prioriteit`, x.`Vervaldatum`, " + BoardKolommen + rk + wk +
            "  FROM `ActivityBoardActiviteiten` x " +
            "  JOIN `ActivityBoardActPersonen` ap ON ap.`ActiviteitID` = x.`ActiviteitID` AND ap.`WerknemerID` = @w " +
            "  JOIN `ActivityBoards` b ON b.`BoardID` = x.`BoardID` " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = b.`ProjectID` " + rj + wj +
            " WHERE x.`Status` NOT IN (" + KlaarStatussen + ") " +
            " ORDER BY (x.`Vervaldatum` IS NULL), x.`Vervaldatum`, COALESCE(x.`Prioriteit`, 9), b.`BoardID`, x.`ActiviteitID` LIMIT 500", cn);
        cmd.Parameters.AddWithValue("@w", werknemerId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ActMijn(r.GetInt32(0), S(r, 1).Trim() is { Length: > 0 } t ? t : "Zonder titel",
                                  ActStatus.IsGeldig(S(r, 2)) ? S(r, 2) : ActStatus.Backlog,
                                  I(r, 3) is int pr && pr is >= 1 and <= 3 ? pr : null, D(r, 4)?.Date, r.GetInt32(5), LeesBoard(r, 5).Titel));
        return lijst;
    }

    // ------------------------------------------------------------------ opmaak in de tekstvelden

    // Kolom Opmaak aanwezig? Alleen 'ja' wordt onthouden, zodat het script uitvoeren zonder herstart werkt.
    private bool _metOpmaak;
    public async Task<bool> OpmaakAanwezigAsync()
    {
        if (_metOpmaak) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "   AND TABLE_NAME = 'ActivityBoardActiviteiten' AND COLUMN_NAME = 'Opmaak'", cn);
            _metOpmaak = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _metOpmaak;
    }

    public Task ZetOpmaakAsync(int activiteitId, bool opmaak, int? door)
        => UitvoerenAsync("UPDATE `ActivityBoardActiviteiten` SET `Opmaak` = @o, `GewijzigdDoor` = @door WHERE `ActiviteitID` = @id",
                          ("@o", opmaak ? 1 : 0), ("@door", (object?)door ?? DBNull.Value), ("@id", activiteitId));

    // ------------------------------------------------------------------ afvinklijst

    // Tabel aanwezig? Alleen 'ja' wordt onthouden, zodat het script uitvoeren zonder herstart werkt.
    private bool _metChecklist;
    public async Task<bool> ChecklistAanwezigAsync()
    {
        if (_metChecklist) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ActivityBoardChecklist'", cn);
            _metChecklist = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _metChecklist;
    }

    private static async Task<int> VoegPuntInAsync(MySqlConnection cn, MySqlTransaction tx, int activiteitId, ActPunt p, int positie, int? door)
    {
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ActivityBoardChecklist` (`ActiviteitID`, `IsKop`, `Tekst`, `Klaar`, `KlaarOp`, `KlaarDoor`, `Positie`, `AangemaaktDoor`) " +
            "VALUES (@a, @kop, @t, @k, @op, @kd, @pos, @door); SELECT LAST_INSERT_ID();", cn, tx);
        var klaar = p.Klaar && !p.Kop;
        cmd.Parameters.AddWithValue("@a", activiteitId);
        cmd.Parameters.AddWithValue("@kop", p.Kop ? 1 : 0);
        cmd.Parameters.AddWithValue("@t", Kort(p.Tekst.Trim(), ActPunt.MaxTekst));
        cmd.Parameters.AddWithValue("@k", klaar ? 1 : 0);
        cmd.Parameters.AddWithValue("@op", klaar ? p.KlaarOp ?? DateTime.Now : DBNull.Value);
        cmd.Parameters.AddWithValue("@kd", klaar ? (object?)(p.KlaarDoor ?? door) ?? DBNull.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@pos", positie);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task ZetPositiesAsync(MySqlConnection cn, MySqlTransaction tx, int activiteitId, IReadOnlyList<int> ids)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            await using var cmd = new MySqlCommand(
                "UPDATE `ActivityBoardChecklist` SET `Positie` = @p WHERE `PuntID` = @id AND `ActiviteitID` = @a", cn, tx);
            cmd.Parameters.AddWithValue("@p", i);
            cmd.Parameters.AddWithValue("@id", ids[i]);
            cmd.Parameters.AddWithValue("@a", activiteitId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<int> NieuwPuntAsync(int activiteitId, ActPunt punt, int? naPuntId, int? door)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        var ids = new List<int>();
        await using (var cmd = new MySqlCommand(
            "SELECT `PuntID` FROM `ActivityBoardChecklist` WHERE `ActiviteitID` = @a ORDER BY `Positie`, `PuntID` FOR UPDATE", cn, tx))
        {
            cmd.Parameters.AddWithValue("@a", activiteitId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
        }
        if (ids.Count >= ActPunt.MaxPerActiviteit)
            throw new InvalidOperationException($"Een afvinklijst heeft hooguit {ActPunt.MaxPerActiviteit} regels.");
        var plek = naPuntId is null ? ids.Count : naPuntId == 0 ? 0 : ids.IndexOf(naPuntId.Value) is >= 0 and var i ? i + 1 : ids.Count;
        punt.Id = await VoegPuntInAsync(cn, tx, activiteitId, punt, plek, door);
        ids.Insert(plek, punt.Id);
        await ZetPositiesAsync(cn, tx, activiteitId, ids);
        await GewijzigdAsync(cn, tx, activiteitId, door);
        await tx.CommitAsync();
        return punt.Id;
    }

    /// <summary>Een wijziging aan één regel, en de activiteit telt als gewijzigd.</summary>
    private async Task PuntAsync(int puntId, int? door, string sql, params (string Naam, object Waarde)[] par)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        int? act;
        await using (var zoek = new MySqlCommand("SELECT `ActiviteitID` FROM `ActivityBoardChecklist` WHERE `PuntID` = @id", cn, tx))
        {
            zoek.Parameters.AddWithValue("@id", puntId);
            act = await zoek.ExecuteScalarAsync() is { } a and not DBNull ? Convert.ToInt32(a) : null;
        }
        if (act is null) return;      // net door een collega verwijderd
        await using (var cmd = new MySqlCommand(sql, cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", puntId);
            foreach (var (n, w) in par) cmd.Parameters.AddWithValue(n, w);
            await cmd.ExecuteNonQueryAsync();
        }
        await GewijzigdAsync(cn, tx, act.Value, door);
        await tx.CommitAsync();
    }

    public Task ZetPuntTekstAsync(int puntId, string tekst, int? door)
        => PuntAsync(puntId, door, "UPDATE `ActivityBoardChecklist` SET `Tekst` = @t WHERE `PuntID` = @id", ("@t", Kort(tekst.Trim(), ActPunt.MaxTekst)));

    public Task ZetPuntKlaarAsync(int puntId, bool klaar, int? door)
        => PuntAsync(puntId, door,
            "UPDATE `ActivityBoardChecklist` SET `Klaar` = @k, `KlaarOp` = IF(@k = 1, NOW(), NULL), `KlaarDoor` = IF(@k = 1, @door, NULL) " +
            " WHERE `PuntID` = @id AND `IsKop` = 0", ("@k", klaar ? 1 : 0), ("@door", (object?)door ?? DBNull.Value));

    public Task VerwijderPuntAsync(int puntId, int? door)
        => PuntAsync(puntId, door, "DELETE FROM `ActivityBoardChecklist` WHERE `PuntID` = @id");

    public async Task ZetPuntVolgordeAsync(int activiteitId, IReadOnlyList<int> puntIds, int? door)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await ZetPositiesAsync(cn, tx, activiteitId, puntIds);
        await GewijzigdAsync(cn, tx, activiteitId, door);
        await tx.CommitAsync();
    }

    public async Task<int?> ActiviteitVanPuntAsync(int puntId)
    {
        if (!await ChecklistAanwezigAsync()) return null;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT `ActiviteitID` FROM `ActivityBoardChecklist` WHERE `PuntID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", puntId);
        return await cmd.ExecuteScalarAsync() is { } a and not DBNull ? Convert.ToInt32(a) : null;
    }

    // ------------------------------------------------------------------ hulp

    private static (string Tabel, string Kolom) KoppelTabel(string soort) => soort switch
    {
        "problemen" => ("ActivityBoardActProblemen", "ProbleemID"),
        "types" => ("ActivityBoardActTypes", "TypeID"),
        "personen" => ("ActivityBoardActPersonen", "WerknemerID"),
        _ => throw new ArgumentException($"Onbekende koppeling {soort}"),
    };

    private static async Task KoppelingenAsync(MySqlConnection cn, MySqlTransaction tx, int activiteitId, string soort,
                                               IEnumerable<int> ids, bool wis = false)
    {
        var (tabel, kolom) = KoppelTabel(soort);
        if (wis)
        {
            await using var del = new MySqlCommand($"DELETE FROM `{tabel}` WHERE `ActiviteitID` = @id", cn, tx);
            del.Parameters.AddWithValue("@id", activiteitId);
            await del.ExecuteNonQueryAsync();
        }
        foreach (var x in ids.Distinct())
        {
            // INSERT IGNORE: een probleem of type dat net door een collega is verwijderd, valt stil weg (FK)
            await using var cmd = new MySqlCommand(
                soort == "personen"
                    ? $"INSERT IGNORE INTO `{tabel}` (`ActiviteitID`, `{kolom}`) VALUES (@a, @x)"
                    : $"INSERT IGNORE INTO `{tabel}` (`ActiviteitID`, `{kolom}`) SELECT @a, @x FROM DUAL " +
                      $" WHERE EXISTS (SELECT 1 FROM `{(soort == "problemen" ? "ActivityBoardProblemen" : "ActivityBoardTypes")}` WHERE `{kolom}` = @x)",
                cn, tx);
            cmd.Parameters.AddWithValue("@a", activiteitId);
            cmd.Parameters.AddWithValue("@x", x);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task GewijzigdAsync(MySqlConnection cn, MySqlTransaction tx, int activiteitId, int? door)
    {
        // last_updated loopt mee via ON UPDATE; GewijzigdDoor = wie de koppeling aanpaste
        await using var cmd = new MySqlCommand(
            "UPDATE `ActivityBoardActiviteiten` SET `GewijzigdDoor` = @door, `last_updated` = CURRENT_TIMESTAMP WHERE `ActiviteitID` = @id", cn, tx);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", activiteitId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task UitvoerenAsync(string sql, params (string Naam, object Waarde)[] par)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        foreach (var (n, w) in par) cmd.Parameters.AddWithValue(n, w);
        await cmd.ExecuteNonQueryAsync();
    }
}
