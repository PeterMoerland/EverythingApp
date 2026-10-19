using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een punt dat bij een exemplaar nog gedaan moet worden (tabel ProductTodo, ProductTodo_MariaDB.sql).</summary>
public class ProductTodo
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public int ProductNameId { get; init; }
    public string Type { get; init; } = "";             // ProductNames.ProductName, bv. MPR-PCBA-RZ
    public string Serienummer { get; init; } = "";
    public string Wat { get; init; } = "";
    public string Toelichting { get; init; } = "";
    public DateTime Aangemaakt { get; init; }
    public string Door { get; init; } = "";
    public DateTime? Gereed { get; init; }
    public string GereedDoor { get; init; } = "";

    // Alleen in het overzicht: het product waar dit exemplaar (uiteindelijk) in zit, bv. de robot.
    public int? WortelId { get; init; }
    public int? WortelNameId { get; init; }
    public string WortelType { get; init; } = "";
    public string WortelSerienummer { get; init; } = "";
    public string Klant { get; init; } = "";
    public string Project { get; init; } = "";
    public string Status { get; init; } = "";

    public bool Open => Gereed is null;
    public string Exemplaar => $"{Type} {Serienummer}".Trim();
    public string Wortel => $"{WortelType} {WortelSerienummer}".Trim();
    /// <summary>True als het punt bij een onderdeel hoort (niet bij het product zelf).</summary>
    public bool InOnderdeel => WortelId is not null && WortelId != ProductId;
}

/// <summary>Hoe vaak een 'Wat' voorkomt (keuzelijst en filter van het overzicht).</summary>
public record TodoWat(string Wat, int Open, int Totaal);

public interface IProductTodoRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();
    /// <summary>Punten van dit exemplaar én van alles wat erin zit (prints in een robot), open eerst.</summary>
    Task<List<ProductTodo>> VanExemplaarAsync(int productId);
    /// <summary>Open punten per exemplaar van dit type, inclusief die van hun onderdelen (stipje in de lijst).</summary>
    Task<Dictionary<int, int>> OpenPerExemplaarAsync(int productNameId);
    /// <summary>Alle punten (of alleen open), met het product waar ze in zitten. Wat leeg = alles.</summary>
    Task<List<ProductTodo>> OverzichtAsync(string? wat, bool ookGereed, int max);
    Task<List<TodoWat>> WatLijstAsync();
    Task<int> MaakAsync(int productId, string wat, string? toelichting, int? door);
    Task ZetGereedAsync(int todoId, bool gereed, int? door);
    Task VerwijderAsync(int todoId);
    /// <summary>
    /// Punten (wat + toelichting) toevoegen bij andere exemplaren. Staat hetzelfde punt daar al open, dan niet nog eens.
    /// Geeft per doel-exemplaar de punten die echt zijn toegevoegd.
    /// </summary>
    Task<Dictionary<int, List<string>>> KopieerAsync(IReadOnlyList<(string Wat, string Toelichting)> punten,
                                                     IReadOnlyList<int> doelen, int? door);
}

public class GeenProductTodoRepository : IProductTodoRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<ProductTodo>> VanExemplaarAsync(int productId) => Task.FromResult(new List<ProductTodo>());
    public Task<Dictionary<int, int>> OpenPerExemplaarAsync(int productNameId) => Task.FromResult(new Dictionary<int, int>());
    public Task<List<ProductTodo>> OverzichtAsync(string? wat, bool ookGereed, int max) => Task.FromResult(new List<ProductTodo>());
    public Task<List<TodoWat>> WatLijstAsync() => Task.FromResult(new List<TodoWat>());
    public Task<int> MaakAsync(int productId, string wat, string? toelichting, int? door) => Task.FromResult(0);
    public Task ZetGereedAsync(int todoId, bool gereed, int? door) => Task.CompletedTask;
    public Task VerwijderAsync(int todoId) => Task.CompletedTask;
    public Task<Dictionary<int, List<string>>> KopieerAsync(IReadOnlyList<(string Wat, string Toelichting)> punten,
                                                            IReadOnlyList<int> doelen, int? door)
        => Task.FromResult(new Dictionary<int, List<string>>());
}

public class MySqlProductTodoRepository : IProductTodoRepository
{
    private readonly string _cs;
    private bool _tabel;
    public MySqlProductTodoRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ProductTodo'", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _tabel;
    }

    private const string Naam = "COALESCE(NULLIF({0}.`Afkorting`, ''), {0}.`Voornaam`, '')";

    // Kolommen 0-10, gelijk voor beide lijsten
    private static string BasisSelect =>
        "SELECT t.`ProductTodoID`, t.`ProductenID`, p.`ProductNameID`, COALESCE(n.`ProductName`, ''), " +
        "       COALESCE(CAST(p.`SerieNummer` AS CHAR), ''), t.`Wat`, COALESCE(t.`Toelichting`, ''), t.`AangemaaktOp`, " +
        $"      {string.Format(Naam, "wa")}, t.`GereedOp`, {string.Format(Naam, "wg")} ";

    private static string BasisJoins =>
        "  FROM `ProductTodo` t " +
        "  JOIN `Producten` p ON p.`ProductenID` = t.`ProductenID` " +
        "  LEFT JOIN `ProductNames` n ON n.`ProductNameID` = p.`ProductNameID` " +
        "  LEFT JOIN `Werknemers` wa ON wa.`Werknemer-id` = t.`AangemaaktDoor` " +
        "  LEFT JOIN `Werknemers` wg ON wg.`Werknemer-id` = t.`GereedDoor` ";

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static int? I(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static DateTime? D(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);

    public async Task<List<ProductTodo>> VanExemplaarAsync(int productId)
    {
        var lijst = new List<ProductTodo>();
        if (!await TabelAanwezigAsync()) return lijst;
        await using var cn = await OpenAsync();
        // dit exemplaar en alles wat erin zit (tot 8 niveaus diep)
        await using var cmd = new MySqlCommand(
            "WITH RECURSIVE boom AS ( " +
            "   SELECT `ProductenID`, 0 AS diepte FROM `Producten` WHERE `ProductenID` = @id " +
            "   UNION ALL " +
            "   SELECT k.`ProductenID`, b.diepte + 1 FROM `Producten` k JOIN boom b ON k.`ParentProductenID` = b.`ProductenID` " +
            "    WHERE b.diepte < 8) " +
            BasisSelect + BasisJoins +
            "  JOIN boom b ON b.`ProductenID` = t.`ProductenID` " +
            " ORDER BY (t.`GereedOp` IS NOT NULL), b.diepte, t.`AangemaaktOp` DESC LIMIT 500", cn);
        cmd.Parameters.AddWithValue("@id", productId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ProductTodo
            {
                Id = r.GetInt32(0), ProductId = r.GetInt32(1), ProductNameId = I(r, 2) ?? 0, Type = S(r, 3),
                Serienummer = S(r, 4), Wat = S(r, 5), Toelichting = S(r, 6), Aangemaakt = r.GetDateTime(7),
                Door = S(r, 8), Gereed = D(r, 9), GereedDoor = S(r, 10),
                WortelId = productId,
            });
        return lijst;
    }

    // Per open punt: het exemplaar zelf en al zijn ouders (robot boven de print)
    private const string KetenOpen =
        "WITH RECURSIVE keten AS ( " +
        "   SELECT t.`ProductTodoID` AS tid, p.`ProductenID` AS id, p.`ParentProductenID` AS ouder, 0 AS d " +
        "     FROM `ProductTodo` t JOIN `Producten` p ON p.`ProductenID` = t.`ProductenID` {0} " +
        "   UNION ALL " +
        "   SELECT k.tid, p.`ProductenID`, p.`ParentProductenID`, k.d + 1 " +
        "     FROM keten k JOIN `Producten` p ON p.`ProductenID` = k.ouder WHERE k.d < 8) ";

    public async Task<Dictionary<int, int>> OpenPerExemplaarAsync(int productNameId)
    {
        var uit = new Dictionary<int, int>();
        if (!await TabelAanwezigAsync()) return uit;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            string.Format(KetenOpen, "WHERE t.`GereedOp` IS NULL") +
            "SELECT k.id, COUNT(*) FROM keten k JOIN `Producten` p ON p.`ProductenID` = k.id " +
            " WHERE p.`ProductNameID` = @naam GROUP BY k.id", cn);
        cmd.Parameters.AddWithValue("@naam", productNameId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit[r.GetInt32(0)] = Convert.ToInt32(r.GetValue(1));
        return uit;
    }

    public async Task<List<ProductTodo>> OverzichtAsync(string? wat, bool ookGereed, int max)
    {
        var lijst = new List<ProductTodo>();
        if (!await TabelAanwezigAsync()) return lijst;
        var waar = new List<string>();
        if (!ookGereed) waar.Add("t.`GereedOp` IS NULL");
        if (!string.IsNullOrWhiteSpace(wat)) waar.Add("t.`Wat` = @wat");
        var filter = waar.Count == 0 ? "" : "WHERE " + string.Join(" AND ", waar);

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            string.Format(KetenOpen, filter) +
            BasisSelect + ", w.`ProductenID`, w.`ProductNameID`, COALESCE(wn.`ProductName`, ''), " +
            "       COALESCE(CAST(w.`SerieNummer` AS CHAR), ''), COALESCE(kl.`name`, ''), " +
            "       COALESCE(NULLIF(w.`Project`, ''), p.`Project`, ''), COALESCE(st.`StatusShort`, st.`Status`, '') " +
            BasisJoins +
            "  LEFT JOIN (SELECT tid, MAX(id) AS wortel FROM keten WHERE ouder IS NULL OR d = 8 GROUP BY tid) kw ON kw.tid = t.`ProductTodoID` " +
            "  LEFT JOIN `Producten` w ON w.`ProductenID` = kw.wortel " +
            "  LEFT JOIN `ProductNames` wn ON wn.`ProductNameID` = w.`ProductNameID` " +
            "  LEFT JOIN `Relations` kl ON kl.`id_supplier` = COALESCE(w.`KlantID`, p.`KlantID`) " +
            "  LEFT JOIN `ProductStatus` st ON st.`ProductStatusID` = w.`ProductStatusID` " +
            filter.Replace("WHERE", "WHERE (p.`isDeleted` = 0 OR p.`isDeleted` IS NULL) AND") +
            (filter.Length == 0 ? " WHERE (p.`isDeleted` = 0 OR p.`isDeleted` IS NULL) " : "") +
            " ORDER BY t.`Wat`, (t.`GereedOp` IS NOT NULL), wn.`ProductName`, w.`SerieNummer`, t.`AangemaaktOp` " +
            " LIMIT @max", cn);
        if (!string.IsNullOrWhiteSpace(wat)) cmd.Parameters.AddWithValue("@wat", wat.Trim());
        cmd.Parameters.AddWithValue("@max", max);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ProductTodo
            {
                Id = r.GetInt32(0), ProductId = r.GetInt32(1), ProductNameId = I(r, 2) ?? 0, Type = S(r, 3),
                Serienummer = S(r, 4), Wat = S(r, 5), Toelichting = S(r, 6), Aangemaakt = r.GetDateTime(7),
                Door = S(r, 8), Gereed = D(r, 9), GereedDoor = S(r, 10),
                WortelId = I(r, 11), WortelNameId = I(r, 12), WortelType = S(r, 13), WortelSerienummer = S(r, 14),
                Klant = S(r, 15), Project = S(r, 16), Status = S(r, 17),
            });
        return lijst;
    }

    public async Task<List<TodoWat>> WatLijstAsync()
    {
        var lijst = new List<TodoWat>();
        if (!await TabelAanwezigAsync()) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Wat`, SUM(`GereedOp` IS NULL), COUNT(*) FROM `ProductTodo` " +
            " GROUP BY `Wat` ORDER BY SUM(`GereedOp` IS NULL) DESC, COUNT(*) DESC, `Wat` LIMIT 200", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new TodoWat(r.GetString(0), Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2))));
        return lijst;
    }

    public async Task<int> MaakAsync(int productId, string wat, string? toelichting, int? door)
    {
        if (!await TabelAanwezigAsync()) throw new InvalidOperationException("De tabel ProductTodo bestaat nog niet (ProductTodo_MariaDB.sql).");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProductTodo` (`ProductenID`, `Wat`, `Toelichting`, `AangemaaktOp`, `AangemaaktDoor`) " +
            "VALUES (@p, @wat, @toel, NOW(), @door); SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.Parameters.AddWithValue("@wat", Kort(wat, 60));
        cmd.Parameters.AddWithValue("@toel", string.IsNullOrWhiteSpace(toelichting) ? DBNull.Value : Kort(toelichting, 250));
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task ZetGereedAsync(int todoId, bool gereed, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(gereed
            ? "UPDATE `ProductTodo` SET `GereedOp` = NOW(), `GereedDoor` = @door WHERE `ProductTodoID` = @id AND `GereedOp` IS NULL"
            : "UPDATE `ProductTodo` SET `GereedOp` = NULL, `GereedDoor` = NULL WHERE `ProductTodoID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", todoId);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Dictionary<int, List<string>>> KopieerAsync(IReadOnlyList<(string Wat, string Toelichting)> punten,
                                                                   IReadOnlyList<int> doelen, int? door)
    {
        var uit = new Dictionary<int, List<string>>();
        if (punten.Count == 0 || doelen.Count == 0) return uit;
        if (!await TabelAanwezigAsync()) throw new InvalidOperationException("De tabel ProductTodo bestaat nog niet (ProductTodo_MariaDB.sql).");
        await using var cn = await OpenAsync();
        await using var tr = await cn.BeginTransactionAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProductTodo` (`ProductenID`, `Wat`, `Toelichting`, `AangemaaktOp`, `AangemaaktDoor`) " +
            "SELECT @p, @wat, @toel, NOW(), @door FROM DUAL " +
            " WHERE NOT EXISTS (SELECT 1 FROM `ProductTodo` x WHERE x.`ProductenID` = @p AND x.`GereedOp` IS NULL " +
            "                      AND x.`Wat` = @wat AND COALESCE(x.`Toelichting`, '') = COALESCE(@toel, ''))", cn, tr);
        var p = cmd.Parameters.Add("@p", MySqlDbType.Int32);
        var wat = cmd.Parameters.Add("@wat", MySqlDbType.VarChar);
        var toel = cmd.Parameters.Add("@toel", MySqlDbType.VarChar);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        foreach (var doel in doelen.Distinct())
            foreach (var (w, t) in punten)
            {
                p.Value = doel;
                wat.Value = Kort(w, 60);
                toel.Value = string.IsNullOrWhiteSpace(t) ? DBNull.Value : Kort(t, 250);
                if (await cmd.ExecuteNonQueryAsync() == 1)
                    (uit.TryGetValue(doel, out var l) ? l : uit[doel] = new()).Add(Kort(w, 60));
            }
        await tr.CommitAsync();
        return uit;
    }

    public async Task VerwijderAsync(int todoId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `ProductTodo` WHERE `ProductTodoID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", todoId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string Kort(string tekst, int max)
    {
        tekst = tekst.Trim();
        return tekst.Length <= max ? tekst : tekst[..max];
    }
}

/// <summary>Alleen voor lokaal bekijken (ProductTodo__Geheugen=true): twee voorbeeldpunten, in het geheugen.</summary>
public class GeheugenProductTodoRepository : IProductTodoRepository
{
    private readonly List<ProductTodo> _lijst = new()
    {
        new() { Id = 1, ProductId = 586, ProductNameId = 39, Type = "MPR-CAB-CA1", Serienummer = "27", Wat = "Doorsolderen",
                Toelichting = "Connector J2, pin 3 en 4", Aangemaakt = DateTime.Today.AddDays(-2), Door = "ED",
                WortelId = 123, WortelNameId = 31, WortelType = "MPR-1", WortelSerienummer = "16", Klant = "Viscon", Status = "OK" },
        new() { Id = 2, ProductId = 123, ProductNameId = 31, Type = "MPR-1", Serienummer = "16", Wat = "Eindtest herhalen",
                Aangemaakt = DateTime.Today, Door = "PM", WortelId = 123, WortelNameId = 31, WortelType = "MPR-1",
                WortelSerienummer = "16", Klant = "Viscon", Status = "OK" },
    };
    private int _volgende = 3;

    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);
    public Task<List<ProductTodo>> VanExemplaarAsync(int productId)
        => Task.FromResult(_lijst.Where(t => t.ProductId == productId || t.WortelId == productId).OrderBy(t => !t.Open).ToList());
    public Task<Dictionary<int, int>> OpenPerExemplaarAsync(int productNameId)
        => Task.FromResult(_lijst.Where(t => t.Open && t.WortelNameId == productNameId)
                                 .GroupBy(t => t.WortelId!.Value).ToDictionary(g => g.Key, g => g.Count()));
    public Task<List<ProductTodo>> OverzichtAsync(string? wat, bool ookGereed, int max)
        => Task.FromResult(_lijst.Where(t => (ookGereed || t.Open) && (wat is null || t.Wat == wat)).Take(max).ToList());
    public Task<List<TodoWat>> WatLijstAsync()
        => Task.FromResult(_lijst.GroupBy(t => t.Wat).Select(g => new TodoWat(g.Key, g.Count(t => t.Open), g.Count())).ToList());
    public Task<int> MaakAsync(int productId, string wat, string? toelichting, int? door)
    {
        var id = _volgende++;
        _lijst.Add(new() { Id = id, ProductId = productId, Wat = wat, Toelichting = toelichting ?? "", Aangemaakt = DateTime.Now, WortelId = productId });
        return Task.FromResult(id);
    }
    public Task ZetGereedAsync(int todoId, bool gereed, int? door)
    {
        var i = _lijst.FindIndex(t => t.Id == todoId);
        if (i >= 0)
        {
            var o = _lijst[i];
            _lijst[i] = new ProductTodo
            {
                Id = o.Id, ProductId = o.ProductId, ProductNameId = o.ProductNameId, Type = o.Type, Serienummer = o.Serienummer,
                Wat = o.Wat, Toelichting = o.Toelichting, Aangemaakt = o.Aangemaakt, Door = o.Door,
                Gereed = gereed ? DateTime.Now : null, GereedDoor = gereed ? "ik" : "",
                WortelId = o.WortelId, WortelNameId = o.WortelNameId, WortelType = o.WortelType,
                WortelSerienummer = o.WortelSerienummer, Klant = o.Klant, Project = o.Project, Status = o.Status,
            };
        }
        return Task.CompletedTask;
    }
    public Task VerwijderAsync(int todoId) { _lijst.RemoveAll(t => t.Id == todoId); return Task.CompletedTask; }
    public Task<Dictionary<int, List<string>>> KopieerAsync(IReadOnlyList<(string Wat, string Toelichting)> punten,
                                                            IReadOnlyList<int> doelen, int? door)
    {
        var uit = new Dictionary<int, List<string>>();
        foreach (var doel in doelen.Distinct())
            foreach (var (w, t) in punten)
            {
                if (_lijst.Any(x => x.ProductId == doel && x.Open && x.Wat == w && x.Toelichting == t)) continue;
                _lijst.Add(new() { Id = _volgende++, ProductId = doel, Wat = w, Toelichting = t, Aangemaakt = DateTime.Now, WortelId = doel });
                (uit.TryGetValue(doel, out var l) ? l : uit[doel] = new()).Add(w);
            }
        return Task.FromResult(uit);
    }
}
