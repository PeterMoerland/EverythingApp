using MySqlConnector;

namespace EverythingApp.Services;

// =====================================================================================================================
//  Broodjes bestellen bij het Kaeswienkeltje (Tholen), wens gebruiker 24-10-2026: 🥖 in de bovenbalk op donderdag en
//  vrijdag, pagina /broodjes. Iedereen mag voor iedereen bestellen en de lijst mailen; MDE betaalt. De vrijdaglijst sluit
//  vrijdag 11:00 (daarna telefonisch). Losse lijsten voor bijvoorbeeld een klantbezoek. Broodjes_MariaDB.sql.
// =====================================================================================================================

public class BroodjeArtikel
{
    public int Id { get; set; }
    public int? ExternId { get; set; }
    public string Naam { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string Categorie { get; set; } = "";
    public string Groep { get; set; } = "";
    public decimal? Prijs { get; set; }
    public string Afbeelding { get; set; } = "";
    public bool Leverbaar { get; set; } = true;
    public bool Verborgen { get; set; }
}

public class BroodjesLijst
{
    public int Id { get; set; }
    public DateOnly Datum { get; set; }
    public string Soort { get; set; } = BroodjesRegels.Vrijdag;
    public string Omschrijving { get; set; } = "";
    public DateTime? SluitOp { get; set; }
    public DateTime? GemaildOp { get; set; }
    public string GemaildDoor { get; set; } = "";

    public string Titel => Soort == BroodjesRegels.Vrijdag
        ? $"Vrijdag {Datum:d-M}"
        : $"{(Omschrijving.Length > 0 ? Omschrijving : "Losse lijst")} · {Datum:d-M}";
    public bool Gesloten => SluitOp is DateTime s && DateTime.Now >= s;
}

public class BroodjeBestelling
{
    public int Id { get; set; }
    public int LijstId { get; set; }
    public int? WerknemerId { get; set; }
    public string Naam { get; set; } = "";
    public int? ArtikelId { get; set; }
    public string Product { get; set; } = "";
    public decimal? Prijs { get; set; }
    public int Aantal { get; set; } = 1;
    public string Opmerking { get; set; } = "";
    public DateTime AangemaaktOp { get; set; }
    public string AangemaaktDoor { get; set; } = "";
}

public static class BroodjesRegels
{
    public const string Vrijdag = "vrijdag", Los = "los";
    public const string Telefoon = "0166 603 018";
    public const string StandaardMail = "info@kaeswienkeltje.nl";
    /// <summary>Tot wanneer de vrijdaglijst openstaat (daarna telefonisch).</summary>
    public static readonly TimeOnly Sluitingstijd = new(11, 0);

    /// <summary>De vrijdag van deze week (maandag t/m zondag); op zaterdag en zondag die van volgende week.</summary>
    public static DateOnly VrijdagVan(DateOnly d)
    {
        var dag = ((int)d.DayOfWeek + 6) % 7;          // maandag 0 … zondag 6
        var vrijdag = d.AddDays(4 - dag);
        return dag > 4 ? vrijdag.AddDays(7) : vrijdag;
    }

    /// <summary>Donderdag en vrijdag: het stokbroodje in de bovenbalk.</summary>
    public static bool IsBroodjesdag(DateTime nu) => nu.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Friday;
}

public interface IBroodjesRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabellenAanwezigAsync();

    Task<List<BroodjeArtikel>> AssortimentAsync();
    Task<DateTime?> AssortimentBijgewerktAsync();
    /// <summary>Het assortiment van de webshop overnemen: nieuw erbij, bestaand bijgewerkt (eigen Verborgen blijft), wat weg is niet meer leverbaar.</summary>
    Task<(int Nieuw, int Bijgewerkt, int Weg)> BewaarAssortimentAsync(IReadOnlyList<BroodjeArtikel> vanSite);

    /// <summary>De vrijdaglijst van die vrijdag; bestaat hij nog niet, dan aangemaakt (sluit om 11:00).</summary>
    Task<BroodjesLijst> VrijdagLijstAsync(DateOnly vrijdag, int? door);
    /// <summary>Lijsten van de afgelopen week en later, nieuwste eerst.</summary>
    Task<List<BroodjesLijst>> RecenteLijstenAsync();
    Task<int> NieuweLijstAsync(DateOnly datum, string omschrijving, int? door);

    Task<List<BroodjeBestelling>> BestellingenAsync(int lijstId);
    Task<int> VoegToeAsync(BroodjeBestelling b, int? door);
    Task VerwijderAsync(int bestellingId);
    Task ZetGemaildAsync(int lijstId, int? door);
    /// <summary>Heeft deze werknemer al iets op de vrijdaglijst van die vrijdag (voor de herinnering)?</summary>
    Task<bool> HeeftBesteldAsync(DateOnly vrijdag, int werknemerId);
}

public class GeenBroodjesRepository : IBroodjesRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabellenAanwezigAsync() => Task.FromResult(false);
    public Task<List<BroodjeArtikel>> AssortimentAsync() => Task.FromResult(new List<BroodjeArtikel>());
    public Task<DateTime?> AssortimentBijgewerktAsync() => Task.FromResult<DateTime?>(null);
    public Task<(int Nieuw, int Bijgewerkt, int Weg)> BewaarAssortimentAsync(IReadOnlyList<BroodjeArtikel> vanSite) => Task.FromResult((0, 0, 0));
    public Task<BroodjesLijst> VrijdagLijstAsync(DateOnly vrijdag, int? door) => Task.FromResult(new BroodjesLijst { Datum = vrijdag });
    public Task<List<BroodjesLijst>> RecenteLijstenAsync() => Task.FromResult(new List<BroodjesLijst>());
    public Task<int> NieuweLijstAsync(DateOnly datum, string omschrijving, int? door) => Task.FromResult(0);
    public Task<List<BroodjeBestelling>> BestellingenAsync(int lijstId) => Task.FromResult(new List<BroodjeBestelling>());
    public Task<int> VoegToeAsync(BroodjeBestelling b, int? door) => Task.FromResult(0);
    public Task VerwijderAsync(int bestellingId) => Task.CompletedTask;
    public Task ZetGemaildAsync(int lijstId, int? door) => Task.CompletedTask;
    public Task<bool> HeeftBesteldAsync(DateOnly vrijdag, int werknemerId) => Task.FromResult(false);
}

/// <summary>Alleen om lokaal te testen zonder de tabellen: Broodjes__Geheugen=true.</summary>
public class GeheugenBroodjesRepository : IBroodjesRepository
{
    private readonly object _slot = new();
    private readonly List<BroodjeArtikel> _artikelen = new();
    private readonly List<BroodjesLijst> _lijsten = new();
    private readonly List<BroodjeBestelling> _bestellingen = new();
    private DateTime? _bijgewerkt;
    private int _volgend = 1;

    public bool Beschikbaar => true;
    public Task<bool> TabellenAanwezigAsync() => Task.FromResult(true);
    public Task<List<BroodjeArtikel>> AssortimentAsync() { lock (_slot) return Task.FromResult(_artikelen.ToList()); }
    public Task<DateTime?> AssortimentBijgewerktAsync() => Task.FromResult(_bijgewerkt);

    public Task<(int Nieuw, int Bijgewerkt, int Weg)> BewaarAssortimentAsync(IReadOnlyList<BroodjeArtikel> vanSite)
    {
        lock (_slot)
        {
            int nieuw = 0, bij = 0;
            foreach (var a in vanSite)
            {
                var b = _artikelen.FirstOrDefault(x => x.ExternId == a.ExternId);
                if (b is null) { a.Id = _volgend++; _artikelen.Add(a); nieuw++; continue; }
                b.Naam = a.Naam; b.Omschrijving = a.Omschrijving; b.Categorie = a.Categorie; b.Groep = a.Groep; b.Prijs = a.Prijs;
                b.Afbeelding = a.Afbeelding; b.Leverbaar = a.Leverbaar; bij++;
            }
            var weg = 0;
            foreach (var b in _artikelen.Where(x => x.ExternId is not null && vanSite.All(a => a.ExternId != x.ExternId) && x.Leverbaar))
            { b.Leverbaar = false; weg++; }
            _bijgewerkt = DateTime.Now;
            return Task.FromResult((nieuw, bij, weg));
        }
    }

    public Task<BroodjesLijst> VrijdagLijstAsync(DateOnly vrijdag, int? door)
    {
        lock (_slot)
        {
            var l = _lijsten.FirstOrDefault(x => x.Soort == BroodjesRegels.Vrijdag && x.Datum == vrijdag);
            if (l is null)
            {
                l = new BroodjesLijst { Id = _volgend++, Datum = vrijdag, SluitOp = vrijdag.ToDateTime(BroodjesRegels.Sluitingstijd) };
                _lijsten.Add(l);
            }
            return Task.FromResult(l);
        }
    }

    public Task<List<BroodjesLijst>> RecenteLijstenAsync()
    {
        var grens = DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
        lock (_slot) return Task.FromResult(_lijsten.Where(l => l.Datum >= grens).OrderByDescending(l => l.Datum).ToList());
    }

    public Task<int> NieuweLijstAsync(DateOnly datum, string omschrijving, int? door)
    {
        lock (_slot)
        {
            var l = new BroodjesLijst { Id = _volgend++, Datum = datum, Soort = BroodjesRegels.Los, Omschrijving = omschrijving };
            _lijsten.Add(l);
            return Task.FromResult(l.Id);
        }
    }

    public Task<List<BroodjeBestelling>> BestellingenAsync(int lijstId)
    { lock (_slot) return Task.FromResult(_bestellingen.Where(b => b.LijstId == lijstId).ToList()); }

    public Task<int> VoegToeAsync(BroodjeBestelling b, int? door)
    {
        lock (_slot) { b.Id = _volgend++; b.AangemaaktOp = DateTime.Now; b.AangemaaktDoor = "ik"; _bestellingen.Add(b); return Task.FromResult(b.Id); }
    }

    public Task VerwijderAsync(int bestellingId) { lock (_slot) _bestellingen.RemoveAll(b => b.Id == bestellingId); return Task.CompletedTask; }

    public Task ZetGemaildAsync(int lijstId, int? door)
    {
        lock (_slot) { var l = _lijsten.FirstOrDefault(x => x.Id == lijstId); if (l is not null) { l.GemaildOp = DateTime.Now; l.GemaildDoor = "ik"; } }
        return Task.CompletedTask;
    }

    public Task<bool> HeeftBesteldAsync(DateOnly vrijdag, int werknemerId)
    {
        lock (_slot)
            return Task.FromResult(_lijsten.Where(l => l.Soort == BroodjesRegels.Vrijdag && l.Datum == vrijdag)
                .Any(l => _bestellingen.Any(b => b.LijstId == l.Id && b.WerknemerId == werknemerId)));
    }
}

public class MySqlBroodjesRepository : IBroodjesRepository
{
    private readonly string _cs;
    private bool _tabellen;
    public MySqlBroodjesRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static string Kort(string s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();

    /// <summary>Alleen 'ja' onthouden: werkt meteen na Broodjes_MariaDB.sql.</summary>
    public async Task<bool> TabellenAanwezigAsync()
    {
        if (_tabellen) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() " +
                "   AND TABLE_NAME IN ('BroodjesAssortiment', 'BroodjesLijsten', 'BroodjesBestellingen')", cn);
            _tabellen = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 3;
        }
        catch { }
        return _tabellen;
    }

    // ---------------------------------------------------------------- assortiment

    public async Task<List<BroodjeArtikel>> AssortimentAsync()
    {
        var lijst = new List<BroodjeArtikel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ArtikelID`, `ExternID`, `Naam`, `Omschrijving`, `Categorie`, `Groep`, `Prijs`, COALESCE(`Afbeelding`, ''), " +
            "       `Leverbaar`, `Verborgen` FROM `BroodjesAssortiment` ORDER BY `Groep`, `Naam`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new BroodjeArtikel
            {
                Id = r.GetInt32(0), ExternId = r.IsDBNull(1) ? null : r.GetInt32(1), Naam = S(r, 2), Omschrijving = S(r, 3),
                Categorie = S(r, 4), Groep = S(r, 5), Prijs = r.IsDBNull(6) ? null : r.GetDecimal(6), Afbeelding = S(r, 7),
                Leverbaar = Convert.ToInt64(r.GetValue(8)) != 0, Verborgen = Convert.ToInt64(r.GetValue(9)) != 0,
            });
        return lijst;
    }

    public async Task<DateTime?> AssortimentBijgewerktAsync()
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT MAX(`Bijgewerkt`) FROM `BroodjesAssortiment`", cn);
        return await cmd.ExecuteScalarAsync() is DateTime d ? d : null;
    }

    public async Task<(int Nieuw, int Bijgewerkt, int Weg)> BewaarAssortimentAsync(IReadOnlyList<BroodjeArtikel> vanSite)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        var bekend = new HashSet<int>();
        await using (var cmd = new MySqlCommand("SELECT `ExternID` FROM `BroodjesAssortiment` WHERE `ExternID` IS NOT NULL", cn, tx))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) bekend.Add(r.GetInt32(0));

        int nieuw = 0, bij = 0;
        foreach (var a in vanSite)
        {
            // Verborgen (eigen keuze) wordt niet aangeraakt
            await using var cmd = new MySqlCommand(
                "INSERT INTO `BroodjesAssortiment` (`ExternID`, `Naam`, `Omschrijving`, `Categorie`, `Groep`, `Prijs`, `Afbeelding`, `Leverbaar`, `Bijgewerkt`) " +
                "VALUES (@e, @n, @o, @c, @g, @p, @a, @l, NOW()) " +
                "ON DUPLICATE KEY UPDATE `Naam` = @n, `Omschrijving` = @o, `Categorie` = @c, `Groep` = @g, `Prijs` = @p, " +
                "                        `Afbeelding` = @a, `Leverbaar` = @l, `Bijgewerkt` = NOW()", cn, tx);
            cmd.Parameters.AddWithValue("@e", (object?)a.ExternId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", Kort(a.Naam, 150));
            cmd.Parameters.AddWithValue("@o", Kort(a.Omschrijving, 400));
            cmd.Parameters.AddWithValue("@c", Kort(a.Categorie, 300));
            cmd.Parameters.AddWithValue("@g", Kort(a.Groep, 60));
            cmd.Parameters.AddWithValue("@p", (object?)a.Prijs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@a", a.Afbeelding.Length > 0 ? Kort(a.Afbeelding, 400) : DBNull.Value);
            cmd.Parameters.AddWithValue("@l", a.Leverbaar ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
            if (a.ExternId is int id && bekend.Contains(id)) bij++; else nieuw++;
        }

        // wat niet meer op de site staat: niet wissen, wel niet meer leverbaar
        var weg = 0;
        var opSite = vanSite.Where(a => a.ExternId is not null).Select(a => a.ExternId!.Value).ToList();
        if (opSite.Count > 0)
        {
            await using var cmd = new MySqlCommand(
                $"UPDATE `BroodjesAssortiment` SET `Leverbaar` = 0, `Bijgewerkt` = NOW() WHERE `ExternID` IS NOT NULL AND `Leverbaar` = 1 " +
                $"   AND `ExternID` NOT IN ({string.Join(",", opSite)})", cn, tx);
            weg = await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return (nieuw, bij, weg);
    }

    // ---------------------------------------------------------------- lijsten

    private const string LijstSelect =
        "SELECT l.`LijstID`, l.`Datum`, l.`Soort`, l.`Omschrijving`, l.`SluitOp`, l.`GemaildOp`, COALESCE(w.`NaamVoluit`, '') " +
        "  FROM `BroodjesLijsten` l LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = l.`GemaildDoor` ";

    private static async Task<List<BroodjesLijst>> LeesLijsten(MySqlCommand cmd)
    {
        var lijst = new List<BroodjesLijst>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new BroodjesLijst
            {
                Id = r.GetInt32(0), Datum = DateOnly.FromDateTime(r.GetDateTime(1)), Soort = S(r, 2), Omschrijving = S(r, 3),
                SluitOp = r.IsDBNull(4) ? null : r.GetDateTime(4), GemaildOp = r.IsDBNull(5) ? null : r.GetDateTime(5), GemaildDoor = S(r, 6),
            });
        return lijst;
    }

    public async Task<BroodjesLijst> VrijdagLijstAsync(DateOnly vrijdag, int? door)
    {
        await using var cn = await OpenAsync();
        await using (var zoek = new MySqlCommand(LijstSelect + " WHERE l.`Soort` = 'vrijdag' AND l.`Datum` = @d ORDER BY l.`LijstID` LIMIT 1", cn))
        {
            zoek.Parameters.AddWithValue("@d", vrijdag.ToDateTime(TimeOnly.MinValue));
            if ((await LeesLijsten(zoek)).FirstOrDefault() is { } l) return l;
        }
        await using (var cmd = new MySqlCommand(
            "INSERT INTO `BroodjesLijsten` (`Datum`, `Soort`, `SluitOp`, `AangemaaktOp`, `AangemaaktDoor`) VALUES (@d, 'vrijdag', @s, NOW(), @door)", cn))
        {
            cmd.Parameters.AddWithValue("@d", vrijdag.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@s", vrijdag.ToDateTime(BroodjesRegels.Sluitingstijd));
            cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        await using var opnieuw = new MySqlCommand(LijstSelect + " WHERE l.`Soort` = 'vrijdag' AND l.`Datum` = @d ORDER BY l.`LijstID` LIMIT 1", cn);
        opnieuw.Parameters.AddWithValue("@d", vrijdag.ToDateTime(TimeOnly.MinValue));
        return (await LeesLijsten(opnieuw)).First();
    }

    public async Task<List<BroodjesLijst>> RecenteLijstenAsync()
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(LijstSelect + " WHERE l.`Datum` >= CURDATE() - INTERVAL 7 DAY ORDER BY l.`Datum` DESC, l.`LijstID` DESC", cn);
        return await LeesLijsten(cmd);
    }

    public async Task<int> NieuweLijstAsync(DateOnly datum, string omschrijving, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `BroodjesLijsten` (`Datum`, `Soort`, `Omschrijving`, `AangemaaktOp`, `AangemaaktDoor`) " +
            "VALUES (@d, 'los', @o, NOW(), @door); SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@d", datum.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@o", Kort(omschrijving, 150));
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // ---------------------------------------------------------------- bestellingen

    public async Task<List<BroodjeBestelling>> BestellingenAsync(int lijstId)
    {
        var lijst = new List<BroodjeBestelling>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT b.`BestellingID`, b.`LijstID`, b.`WerknemerID`, b.`Naam`, b.`ArtikelID`, b.`Product`, b.`Prijs`, b.`Aantal`, " +
            "       b.`Opmerking`, b.`AangemaaktOp`, COALESCE(w.`NaamVoluit`, '') " +
            "  FROM `BroodjesBestellingen` b LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = b.`AangemaaktDoor` " +
            " WHERE b.`LijstID` = @l ORDER BY b.`Naam`, b.`BestellingID`", cn);
        cmd.Parameters.AddWithValue("@l", lijstId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new BroodjeBestelling
            {
                Id = r.GetInt32(0), LijstId = r.GetInt32(1), WerknemerId = r.IsDBNull(2) ? null : r.GetInt32(2), Naam = S(r, 3),
                ArtikelId = r.IsDBNull(4) ? null : r.GetInt32(4), Product = S(r, 5), Prijs = r.IsDBNull(6) ? null : r.GetDecimal(6),
                Aantal = r.GetInt32(7), Opmerking = S(r, 8), AangemaaktOp = r.GetDateTime(9), AangemaaktDoor = S(r, 10),
            });
        return lijst;
    }

    public async Task<int> VoegToeAsync(BroodjeBestelling b, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `BroodjesBestellingen` (`LijstID`, `WerknemerID`, `Naam`, `ArtikelID`, `Product`, `Prijs`, `Aantal`, `Opmerking`, " +
            "  `AangemaaktOp`, `AangemaaktDoor`) VALUES (@l, @w, @n, @a, @p, @pr, @aa, @o, NOW(), @door); SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@l", b.LijstId);
        cmd.Parameters.AddWithValue("@w", (object?)b.WerknemerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@n", Kort(b.Naam, 100));
        cmd.Parameters.AddWithValue("@a", (object?)b.ArtikelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@p", Kort(b.Product, 150));
        cmd.Parameters.AddWithValue("@pr", (object?)b.Prijs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@aa", Math.Clamp(b.Aantal, 1, 50));
        cmd.Parameters.AddWithValue("@o", Kort(b.Opmerking, 200));
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task VerwijderAsync(int bestellingId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `BroodjesBestellingen` WHERE `BestellingID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", bestellingId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetGemaildAsync(int lijstId, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `BroodjesLijsten` SET `GemaildOp` = NOW(), `GemaildDoor` = @door WHERE `LijstID` = @l", cn);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@l", lijstId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> HeeftBesteldAsync(DateOnly vrijdag, int werknemerId)
    {
        if (!await TabellenAanwezigAsync()) return true;     // geen tabellen: geen herinnering
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT EXISTS (SELECT 1 FROM `BroodjesBestellingen` b JOIN `BroodjesLijsten` l ON l.`LijstID` = b.`LijstID` " +
            "                WHERE l.`Soort` = 'vrijdag' AND l.`Datum` = @d AND b.`WerknemerID` = @w)", cn);
        cmd.Parameters.AddWithValue("@d", vrijdag.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@w", werknemerId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 0;
    }
}
