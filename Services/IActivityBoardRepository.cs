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
    /// <summary>Nieuw vrij board met alleen een naam.</summary>
    Task<int> NieuwVrijBoardAsync(string naam, int? door);
    /// <summary>Eigen naam van het board; leeg = de naam van het project of de klant.</summary>
    Task HernoemBoardAsync(int boardId, string naam);
    /// <summary>
    /// Het board aan een project of een klant hangen (of los: beide null). Geeft een melding als dat project of die
    /// klant al een ander board heeft (één per project en per klant), anders null.
    /// </summary>
    Task<string?> KoppelBoardAsync(int boardId, int? projectId, int? relatieId);
    Task<ActBoardData?> LaadAsync(int boardId);
    Task<List<ActPersoon>> GetPersonenAsync();

    Task<int> VoegProbleemToeAsync(int boardId, string naam, string kleur);
    Task HernoemProbleemAsync(int probleemId, string naam);
    /// <summary>Nieuwe volgorde van alle problemen van het board: positie 1..n.</summary>
    Task ZetProbleemVolgordeAsync(int boardId, IReadOnlyList<int> probleemIds);
    Task VerwijderProbleemAsync(int probleemId);

    Task<int> VoegTypeToeAsync(int boardId, string naam, string kleur);
    Task HernoemTypeAsync(int typeId, string naam);
    Task VerwijderTypeAsync(int typeId);

    /// <summary>Nieuwe activiteit met koppelingen; geeft het id.</summary>
    Task<int> NieuweActiviteitAsync(int boardId, Activiteit a, int? door);
    /// <summary>Eén veld: Titel, Prioriteit, Vervaldatum of een van de tekstvelden.</summary>
    Task ZetVeldAsync(int activiteitId, string kolom, object? waarde, int? door);
    Task ZetStatusAsync(int activiteitId, string status, DateTime? geslotenOp, int? door);
    /// <summary>Koppelingen vervangen; soort = "problemen", "types" of "personen".</summary>
    Task ZetKoppelingenAsync(int activiteitId, string soort, IReadOnlyCollection<int> ids, int? door);
    /// <summary>Slepen: status, problemen en de nieuwe volgorde van het doelvak in één transactie.</summary>
    Task VerplaatsAsync(int activiteitId, string status, DateTime? geslotenOp, IReadOnlyCollection<int> problemen,
                        IReadOnlyList<int> vakVolgorde, int? door);
    Task VerwijderActiviteitAsync(int activiteitId);
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
    public Task<int> NieuwVrijBoardAsync(string naam, int? door) => Task.FromResult(0);
    public Task HernoemBoardAsync(int boardId, string naam) => Task.CompletedTask;
    public Task<string?> KoppelBoardAsync(int boardId, int? projectId, int? relatieId) => Task.FromResult<string?>(null);
    public Task<ActBoardData?> LaadAsync(int boardId) => Task.FromResult<ActBoardData?>(null);
    public Task<List<ActPersoon>> GetPersonenAsync() => Task.FromResult(new List<ActPersoon>());
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

    /// <summary>Kolom en join voor de klant, of niets als het script nog niet gedraaid heeft.</summary>
    private async Task<(string Kolommen, string Join)> RelatieDeelAsync()
        => await SoortenAanwezigAsync()
            ? (", b.`RelatieID`, COALESCE(r.`name`, '')", " LEFT JOIN `Relations` r ON r.`id_supplier` = b.`RelatieID` ")
            : (", NULL, ''", " ");

    public async Task<List<ActBoardRegel>> GetBoardsAsync()
    {
        var lijst = new List<ActBoardRegel>();
        var (rk, rj) = await RelatieDeelAsync();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT b.`BoardID`, b.`ProjectID`, p.`Projectnumber`, COALESCE(p.`Projectdescription`, ''), COALESCE(b.`Naam`, ''), " +
            "       SUM(a.`ActiviteitID` IS NOT NULL AND a.`Status` NOT IN ('opgelost','nietopgelost')), " +
            "       SUM(a.`Status` IN ('opgelost','nietopgelost')), " +
            "       GREATEST(COALESCE(MAX(a.`last_updated`), b.`last_updated`), b.`last_updated`)" + rk +
            "  FROM `ActivityBoards` b " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = b.`ProjectID` " + rj +
            "  LEFT JOIN `ActivityBoardActiviteiten` a ON a.`BoardID` = b.`BoardID` " +
            " GROUP BY b.`BoardID` ORDER BY 8 DESC", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ActBoardRegel(r.GetInt32(0), I(r, 1), I(r, 2), S(r, 3), S(r, 4),
                                        I(r, 5) ?? 0, I(r, 6) ?? 0, D(r, 7), I(r, 8), S(r, 9)));
        return lijst;
    }

    public Task<int> ZorgVoorBoardAsync(int projectId, int? door) => ZorgVoorAsync("ProjectID", projectId, door);
    public Task<int> ZorgVoorKlantBoardAsync(int relatieId, int? door) => ZorgVoorAsync("RelatieID", relatieId, door);

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

    public async Task<string?> KoppelBoardAsync(int boardId, int? projectId, int? relatieId)
    {
        await using var cn = await OpenAsync();
        // één board per project en per klant
        if (projectId is not null || relatieId is not null)
        {
            await using var bezet = new MySqlCommand(
                $"SELECT COUNT(*) FROM `ActivityBoards` WHERE `BoardID` <> @b AND `{(projectId is not null ? "ProjectID" : "RelatieID")}` = @x", cn);
            bezet.Parameters.AddWithValue("@b", boardId);
            bezet.Parameters.AddWithValue("@x", projectId ?? relatieId!.Value);
            if (Convert.ToInt32(await bezet.ExecuteScalarAsync()) > 0)
                return projectId is not null ? "Dit project heeft al een board." : "Deze klant heeft al een board.";
        }
        await using var cmd = new MySqlCommand(
            "UPDATE `ActivityBoards` SET `ProjectID` = @p, `RelatieID` = @r WHERE `BoardID` = @b", cn);
        cmd.Parameters.AddWithValue("@p", (object?)projectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@r", (object?)(projectId is null ? relatieId : null) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@b", boardId);
        await cmd.ExecuteNonQueryAsync();
        return null;
    }

    public async Task<ActBoardData?> LaadAsync(int boardId)
    {
        var data = new ActBoardData();
        await using var cn = await OpenAsync();

        var (rk, rj) = await RelatieDeelAsync();
        await using (var cmd = new MySqlCommand(
            "SELECT b.`BoardID`, b.`ProjectID`, p.`Projectnumber`, COALESCE(p.`Projectdescription`, ''), COALESCE(b.`Naam`, '')" + rk +
            "  FROM `ActivityBoards` b LEFT JOIN `Projects` p ON p.`Project-Id` = b.`ProjectID` " + rj + " WHERE b.`BoardID` = @b", cn))
        {
            cmd.Parameters.AddWithValue("@b", boardId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            data.Board = new ActBoard(r.GetInt32(0), I(r, 1), I(r, 2), S(r, 3), S(r, 4), I(r, 5), S(r, 6));
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
        await using (var cmd = new MySqlCommand(
            "SELECT `ActiviteitID`, `Titel`, `Status`, `Prioriteit`, `Vervaldatum`, `GemaaktOp`, `GeslotenOp`, `Volgorde`, " +
            "       `Samenvatting`, `Details`, `Impact`, `GenomenStappen`, `VolgendeStappen`, `Opmerkingen` " +
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
        return data;
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
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        int id;
        await using (var cmd = new MySqlCommand(
            "INSERT INTO `ActivityBoardActiviteiten` (`BoardID`, `Titel`, `Status`, `Prioriteit`, `Vervaldatum`, `GemaaktOp`, " +
            "   `GeslotenOp`, `Volgorde`, `GemaaktDoor`, `GewijzigdDoor`, " +
            string.Join(", ", ActTekst.Velden.Select(v => $"`{v.Kolom}`")) + ") " +
            "VALUES (@b, @t, @s, @p, @v, @gm, @gs, @vo, @door, @door, " +
            string.Join(", ", ActTekst.Velden.Select(v => "@x" + v.Kolom)) + "); SELECT LAST_INSERT_ID();", cn, tx))
        {
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
        await tx.CommitAsync();
        return id;
    }

    public Task ZetVeldAsync(int activiteitId, string kolom, object? waarde, int? door)
    {
        // alleen deze kolommen; de naam komt nooit van buiten in de query
        if (kolom is not ("Titel" or "Prioriteit" or "Vervaldatum") && !ActTekst.IsKolom(kolom))
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
                "UPDATE `ActivityBoardActiviteiten` SET `Volgorde` = @v WHERE `ActiviteitID` = @id", cn, tx);
            cmd.Parameters.AddWithValue("@v", i);
            cmd.Parameters.AddWithValue("@id", vakVolgorde[i]);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public Task VerwijderActiviteitAsync(int activiteitId)
        => UitvoerenAsync("DELETE FROM `ActivityBoardActiviteiten` WHERE `ActiviteitID` = @id", ("@id", activiteitId));

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
