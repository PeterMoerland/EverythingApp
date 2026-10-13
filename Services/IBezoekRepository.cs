using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Bezoekverslagen met deelnemers en actiepunten (Bezoekverslagen_MariaDB.sql). Een vertrouwelijk
/// verslag is alleen zichtbaar voor de maker, de collega's die erbij waren en Directie; dat filter zit
/// in elke query (parameter ik/directie), niet alleen op het scherm.
/// </summary>
public interface IBezoekRepository
{
    bool Beschikbaar { get; }

    /// <summary>Bestaan de tabellen al (is het SQL-script uitgevoerd)?</summary>
    Task<bool> TabellenAanwezigAsync();

    Task<List<Bezoek>> ZoekAsync(BezoekFilter filter, int? ik, bool directie, int max);
    Task<Bezoek?> GetAsync(int id, int? ik, bool directie);

    /// <summary>Nieuw (Id 0) of bijwerken, met deelnemers en acties; geeft het id terug.</summary>
    Task<int> BewaarAsync(Bezoek bezoek, int? door);

    Task WisAsync(int id);

    /// <summary>Open actiepunten (van één collega, of van iedereen bij null).</summary>
    Task<List<BezoekActie>> OpenActiesAsync(int? werknemerId, int? ik, bool directie);

    Task ZetActieGereedAsync(int actieId, bool gereed);

    /// <summary>Verslagen met een vervolgdatum vanaf <paramref name="vanaf"/>.</summary>
    Task<List<Bezoek>> VervolgAsync(DateOnly vanaf, int? ik, bool directie);
}

public class GeenBezoekRepository : IBezoekRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabellenAanwezigAsync() => Task.FromResult(false);
    public Task<List<Bezoek>> ZoekAsync(BezoekFilter filter, int? ik, bool directie, int max) => Task.FromResult(new List<Bezoek>());
    public Task<Bezoek?> GetAsync(int id, int? ik, bool directie) => Task.FromResult<Bezoek?>(null);
    public Task<int> BewaarAsync(Bezoek bezoek, int? door) => Task.FromResult(0);
    public Task WisAsync(int id) => Task.CompletedTask;
    public Task<List<BezoekActie>> OpenActiesAsync(int? werknemerId, int? ik, bool directie) => Task.FromResult(new List<BezoekActie>());
    public Task ZetActieGereedAsync(int actieId, bool gereed) => Task.CompletedTask;
    public Task<List<Bezoek>> VervolgAsync(DateOnly vanaf, int? ik, bool directie) => Task.FromResult(new List<Bezoek>());
}

public class MySqlBezoekRepository : IBezoekRepository
{
    private readonly string _cs;

    public MySqlBezoekRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static int? I(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static string Kort(string s, int max) => s.Length <= max ? s : s[..max];

    public async Task<bool> TabellenAanwezigAsync()
    {
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() " +
                "   AND TABLE_NAME IN ('Bezoekverslagen', 'BezoekDeelnemers', 'BezoekActies')", cn);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 3;
        }
        catch { return false; }
    }

    /// <summary>Wat deze gebruiker mag zien: niet vertrouwelijk, of zelf gemaakt, of erbij geweest, of Directie.</summary>
    private static string Zichtbaar(MySqlCommand cmd, int? ik, bool directie)
    {
        if (directie) return "1 = 1";
        cmd.Parameters.AddWithValue("@ik", (object?)ik ?? -1);
        return "(b.`Vertrouwelijk` = 0 OR b.`AangemaaktDoor` = @ik OR EXISTS (SELECT 1 FROM `BezoekDeelnemers` z " +
               "  WHERE z.`BezoekID` = b.`BezoekID` AND z.`WerknemerID` = @ik))";
    }

    private const string Basis =
        "SELECT b.`BezoekID`, b.`Datum`, b.`Titel`, b.`Soort`, b.`RelatieID`, COALESCE(r.`name`, ''), " +
        "       b.`ProjectID`, CONCAT_WS(' · ', p.`Projectnumber`, p.`Projectdescription`), COALESCE(b.`Locatie`, ''), " +
        "       COALESCE(b.`Verslag`, ''), b.`Vervolg`, b.`Vertrouwelijk`, b.`AangemaaktDoor`, COALESCE(wa.`NaamVoluit`, ''), " +
        "       b.`AangemaaktOp`, COALESCE(wg.`NaamVoluit`, ''), b.`last_updated`, " +
        "       (SELECT COUNT(*) FROM `FileStore` f WHERE f.`StoreType` = 330 AND f.`MasterID` = b.`BezoekID`) " +
        "  FROM `Bezoekverslagen` b " +
        "  LEFT JOIN `Relations` r ON r.`id_supplier` = b.`RelatieID` " +
        "  LEFT JOIN `Projects` p ON p.`Project-Id` = b.`ProjectID` " +
        "  LEFT JOIN `Werknemers` wa ON wa.`Werknemer-id` = b.`AangemaaktDoor` " +
        "  LEFT JOIN `Werknemers` wg ON wg.`Werknemer-id` = b.`GewijzigdDoor` ";

    private static Bezoek Lees(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Datum = Convert.ToDateTime(r.GetValue(1)),
        Titel = S(r, 2),
        Soort = S(r, 3),
        RelatieId = I(r, 4),
        Relatie = S(r, 5),
        ProjectId = I(r, 6),
        Project = S(r, 7),
        Locatie = S(r, 8),
        Verslag = S(r, 9),
        Vervolg = r.IsDBNull(10) ? null : DateOnly.FromDateTime(Convert.ToDateTime(r.GetValue(10))),
        Vertrouwelijk = !r.IsDBNull(11) && Convert.ToInt64(r.GetValue(11)) != 0,
        AangemaaktDoorId = I(r, 12),
        AangemaaktDoor = S(r, 13),
        AangemaaktOp = r.IsDBNull(14) ? null : Convert.ToDateTime(r.GetValue(14)),
        GewijzigdDoor = S(r, 15),
        GewijzigdOp = r.IsDBNull(16) ? null : Convert.ToDateTime(r.GetValue(16)),
        AantalBijlagen = Convert.ToInt32(r.GetValue(17)),
    };

    public async Task<List<Bezoek>> ZoekAsync(BezoekFilter f, int? ik, bool directie, int max)
    {
        var lijst = new List<Bezoek>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand { Connection = cn };
        var waar = new List<string> { Zichtbaar(cmd, ik, directie) };

        if (f.Soort.Length > 0) { waar.Add("b.`Soort` = @soort"); cmd.Parameters.AddWithValue("@soort", f.Soort); }
        if (f.RelatieId is int rel) { waar.Add("b.`RelatieID` = @rel"); cmd.Parameters.AddWithValue("@rel", rel); }
        if (f.WerknemerId is int wn)
        {
            waar.Add("(b.`AangemaaktDoor` = @wn OR EXISTS (SELECT 1 FROM `BezoekDeelnemers` d WHERE d.`BezoekID` = b.`BezoekID` AND d.`WerknemerID` = @wn))");
            cmd.Parameters.AddWithValue("@wn", wn);
        }
        if (f.Van is DateOnly van) { waar.Add("b.`Datum` >= @van"); cmd.Parameters.AddWithValue("@van", van.ToDateTime(TimeOnly.MinValue)); }
        if (f.Tot is DateOnly tot) { waar.Add("b.`Datum` < @tot"); cmd.Parameters.AddWithValue("@tot", tot.AddDays(1).ToDateTime(TimeOnly.MinValue)); }
        if (f.Zoek.Trim().Length > 0)
        {
            waar.Add("CONCAT_WS(' ', b.`Titel`, b.`Verslag`, b.`Locatie`, r.`name`, p.`Projectnumber`, p.`Projectdescription`) LIKE @zoek");
            cmd.Parameters.AddWithValue("@zoek", "%" + f.Zoek.Trim() + "%");
        }

        cmd.CommandText = Basis + " WHERE " + string.Join(" AND ", waar) +
                          $" ORDER BY b.`Datum` DESC, b.`BezoekID` DESC LIMIT {Math.Clamp(max, 1, 1000)}";
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) lijst.Add(Lees(r));

        await VulDeelnemersAsync(cn, lijst);
        return lijst;
    }

    /// <summary>Deelnemers (met naam) voor een reeks verslagen in één query.</summary>
    private static async Task VulDeelnemersAsync(MySqlConnection cn, List<Bezoek> bezoeken)
    {
        if (bezoeken.Count == 0) return;
        var perId = bezoeken.ToDictionary(b => b.Id);
        await using var cmd = new MySqlCommand(
            "SELECT d.`ID`, d.`BezoekID`, d.`WerknemerID`, d.`ContactID`, " +
            "       COALESCE(w.`NaamVoluit`, c.`Naam`, d.`Naam`, '') " +
            "  FROM `BezoekDeelnemers` d " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = d.`WerknemerID` " +
            "  LEFT JOIN `Contacts` c ON c.`ID_contact` = d.`ContactID` " +
            $" WHERE d.`BezoekID` IN ({string.Join(",", perId.Keys)}) ORDER BY d.`ID`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            if (perId.TryGetValue(r.GetInt32(1), out var b))
                b.Deelnemers.Add(new BezoekDeelnemer { Id = r.GetInt32(0), WerknemerId = I(r, 2), ContactId = I(r, 3), Naam = S(r, 4) });
    }

    public async Task<Bezoek?> GetAsync(int id, int? ik, bool directie)
    {
        await using var cn = await OpenAsync();
        Bezoek? b;
        await using (var cmd = new MySqlCommand { Connection = cn })
        {
            var zicht = Zichtbaar(cmd, ik, directie);
            cmd.CommandText = Basis + " WHERE b.`BezoekID` = @id AND " + zicht;
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            b = await r.ReadAsync() ? Lees(r) : null;
        }
        if (b is null) return null;

        await VulDeelnemersAsync(cn, new List<Bezoek> { b });
        b.Acties = await ActiesAsync(cn, "a.`BezoekID` = @id", c => c.Parameters.AddWithValue("@id", id));
        return b;
    }

    private static async Task<List<BezoekActie>> ActiesAsync(MySqlConnection cn, string waar, Action<MySqlCommand> vul,
                                                             string extraWaar = "")
    {
        var lijst = new List<BezoekActie>();
        await using var cmd = new MySqlCommand(
            "SELECT a.`ID`, a.`BezoekID`, a.`Omschrijving`, a.`WerknemerID`, COALESCE(w.`NaamVoluit`, ''), a.`Deadline`, " +
            "       a.`Gereed`, a.`GereedOp`, b.`Titel`, b.`Datum`, COALESCE(r.`name`, '') " +
            "  FROM `BezoekActies` a JOIN `Bezoekverslagen` b ON b.`BezoekID` = a.`BezoekID` " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = a.`WerknemerID` " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = b.`RelatieID` " +
            $" WHERE {waar} {extraWaar} ORDER BY a.`Gereed`, a.`Deadline` IS NULL, a.`Deadline`, a.`ID`", cn);
        vul(cmd);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new BezoekActie
            {
                Id = r.GetInt32(0),
                BezoekId = r.GetInt32(1),
                Omschrijving = S(r, 2),
                WerknemerId = I(r, 3),
                Wie = S(r, 4),
                Deadline = r.IsDBNull(5) ? null : DateOnly.FromDateTime(Convert.ToDateTime(r.GetValue(5))),
                Gereed = !r.IsDBNull(6) && Convert.ToInt64(r.GetValue(6)) != 0,
                GereedOp = r.IsDBNull(7) ? null : Convert.ToDateTime(r.GetValue(7)),
                BezoekTitel = S(r, 8),
                BezoekDatum = r.IsDBNull(9) ? null : Convert.ToDateTime(r.GetValue(9)),
                Relatie = S(r, 10),
            });
        return lijst;
    }

    public async Task<int> BewaarAsync(Bezoek b, int? door)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        var nieuw = b.Id == 0;
        await using (var cmd = new MySqlCommand(nieuw
            ? "INSERT INTO `Bezoekverslagen` (`Datum`, `Titel`, `Soort`, `RelatieID`, `ProjectID`, `Locatie`, `Verslag`, " +
              "  `Vervolg`, `Vertrouwelijk`, `AangemaaktDoor`, `AangemaaktOp`, `GewijzigdDoor`) " +
              "VALUES (@datum, @titel, @soort, @rel, @proj, @loc, @verslag, @vervolg, @vertr, @door, NOW(), @door); SELECT LAST_INSERT_ID();"
            : "UPDATE `Bezoekverslagen` SET `Datum` = @datum, `Titel` = @titel, `Soort` = @soort, `RelatieID` = @rel, " +
              "  `ProjectID` = @proj, `Locatie` = @loc, `Verslag` = @verslag, `Vervolg` = @vervolg, `Vertrouwelijk` = @vertr, " +
              "  `GewijzigdDoor` = @door WHERE `BezoekID` = @id; SELECT @id;", cn, tx))
        {
            cmd.Parameters.AddWithValue("@datum", b.Datum);
            cmd.Parameters.AddWithValue("@titel", Kort(b.Titel.Trim(), 150));
            cmd.Parameters.AddWithValue("@soort", Kort(b.Soort, 30));
            cmd.Parameters.AddWithValue("@rel", (object?)b.RelatieId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@proj", (object?)b.ProjectId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@loc", Kort(b.Locatie.Trim(), 150));
            cmd.Parameters.AddWithValue("@verslag", b.Verslag);
            cmd.Parameters.AddWithValue("@vervolg", (object?)b.Vervolg?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vertr", b.Vertrouwelijk ? 1 : 0);
            cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", b.Id);
            b.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // deelnemers: eenvoudig opnieuw neerzetten
        await using (var cmd = new MySqlCommand("DELETE FROM `BezoekDeelnemers` WHERE `BezoekID` = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", b.Id);
            await cmd.ExecuteNonQueryAsync();
        }
        foreach (var d in b.Deelnemers)
        {
            await using var cmd = new MySqlCommand(
                "INSERT INTO `BezoekDeelnemers` (`BezoekID`, `WerknemerID`, `ContactID`, `Naam`) VALUES (@id, @w, @c, @n)", cn, tx);
            cmd.Parameters.AddWithValue("@id", b.Id);
            cmd.Parameters.AddWithValue("@w", (object?)d.WerknemerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@c", (object?)d.ContactId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", d.WerknemerId is null && d.ContactId is null ? Kort(d.Naam.Trim(), 100) : DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        // acties: bijwerken en toevoegen, alleen wissen wat er niet meer staat
        var blijven = b.Acties.Where(a => a.Id > 0).Select(a => a.Id).ToList();
        await using (var cmd = new MySqlCommand(
            "DELETE FROM `BezoekActies` WHERE `BezoekID` = @id" +
            (blijven.Count > 0 ? $" AND `ID` NOT IN ({string.Join(",", blijven)})" : ""), cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", b.Id);
            await cmd.ExecuteNonQueryAsync();
        }
        foreach (var a in b.Acties.Where(a => a.Omschrijving.Trim().Length > 0))
        {
            await using var cmd = new MySqlCommand(a.Id == 0
                ? "INSERT INTO `BezoekActies` (`BezoekID`, `Omschrijving`, `WerknemerID`, `Deadline`, `Gereed`, `GereedOp`) " +
                  "VALUES (@id, @oms, @w, @dl, @g, @gop); SELECT LAST_INSERT_ID();"
                : "UPDATE `BezoekActies` SET `Omschrijving` = @oms, `WerknemerID` = @w, `Deadline` = @dl, `Gereed` = @g, " +
                  "  `GereedOp` = @gop WHERE `ID` = @aid AND `BezoekID` = @id; SELECT @aid;", cn, tx);
            cmd.Parameters.AddWithValue("@id", b.Id);
            cmd.Parameters.AddWithValue("@aid", a.Id);
            cmd.Parameters.AddWithValue("@oms", Kort(a.Omschrijving.Trim(), 250));
            cmd.Parameters.AddWithValue("@w", (object?)a.WerknemerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dl", (object?)a.Deadline?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@g", a.Gereed ? 1 : 0);
            cmd.Parameters.AddWithValue("@gop", a.Gereed ? (object)(a.GereedOp ?? DateTime.Now) : DBNull.Value);
            a.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        await tx.CommitAsync();
        return b.Id;
    }

    public async Task WisAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `Bezoekverslagen` WHERE `BezoekID` = @id", cn);   // deelnemers en acties via cascade
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<BezoekActie>> OpenActiesAsync(int? werknemerId, int? ik, bool directie)
    {
        await using var cn = await OpenAsync();
        await using var zicht = new MySqlCommand();
        var zichtbaar = Zichtbaar(zicht, ik, directie);
        return await ActiesAsync(cn, "a.`Gereed` = 0", c =>
        {
            foreach (MySqlParameter p in zicht.Parameters) c.Parameters.AddWithValue(p.ParameterName, p.Value);
            if (werknemerId is int w) c.Parameters.AddWithValue("@wie", w);
        }, " AND " + zichtbaar + (werknemerId is null ? "" : " AND a.`WerknemerID` = @wie"));
    }

    public async Task ZetActieGereedAsync(int actieId, bool gereed)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `BezoekActies` SET `Gereed` = @g, `GereedOp` = CASE WHEN @g = 1 THEN NOW() ELSE NULL END WHERE `ID` = @id", cn);
        cmd.Parameters.AddWithValue("@g", gereed ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", actieId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Bezoek>> VervolgAsync(DateOnly vanaf, int? ik, bool directie)
    {
        var lijst = new List<Bezoek>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand { Connection = cn };
        var zicht = Zichtbaar(cmd, ik, directie);
        cmd.CommandText = Basis + " WHERE b.`Vervolg` >= @vanaf AND " + zicht + " ORDER BY b.`Vervolg`, b.`Datum` DESC";
        cmd.Parameters.AddWithValue("@vanaf", vanaf.ToDateTime(TimeOnly.MinValue));
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) lijst.Add(Lees(r));
        await VulDeelnemersAsync(cn, lijst);
        return lijst;
    }
}
