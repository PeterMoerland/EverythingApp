using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// De opbouwboom: items (fases, subfases, stappen) per producttype en de afvinkstatus per exemplaar
/// (ProductOpbouw_MariaDB.sql, ProductOpbouwBoom_MariaDB.sql). De regels van de boom staan in <see cref="OpbouwBoom"/>.
/// </summary>
public interface IOpbouwRepository
{
    bool Beschikbaar { get; }
    /// <summary>Kolom Soort (opbouw/controle) aanwezig? Zonder die kolom valt alles onder 'opbouw'.</summary>
    Task<bool> HeeftSoortAsync();
    /// <summary>Is de boom er (ProductOpbouwBoom_MariaDB.sql)? Zonder: alleen losse stappen, zonder fases.</summary>
    Task<bool> HeeftBoomAsync();

    /// <summary>De items van een producttype; met <paramref name="productId"/> ook de status bij dat exemplaar.</summary>
    Task<List<OpbouwStap>> GetStappenAsync(int productNameId, int? productId = null);
    Task<int> BewaarStapAsync(OpbouwStap stap, int? door);
    /// <summary>Item weghalen, met alles wat eronder hangt.</summary>
    Task VerwijderStapAsync(int stapId);
    /// <summary>Volgorde vastleggen: de id's (van items met dezelfde ouder) in de gewenste volgorde.</summary>
    Task ZetVolgordeAsync(IReadOnlyList<int> stapIdsOpVolgorde);

    /// <summary>Vinkje zetten of weghalen bij een exemplaar; bij een hoofdfase: afgerond of overgeslagen.</summary>
    Task ZetStatusAsync(int productId, int stapId, bool klaar, int? door, string? opmerking = null, bool overgeslagen = false);
    /// <summary>
    /// Terug naar een fase: de vinkjes van deze items vervallen. Alleen Klaar gaat uit; de regel blijft staan, zodat te
    /// zien is wie het eerder afvinkte. Geeft het aantal vervallen vinkjes.
    /// </summary>
    Task<int> ZetTerugAsync(int productId, IReadOnlyCollection<int> stapIds);

    /// <summary>Is de tabel voor fouten in de opbouw er (ProductOpbouwDeel3_MariaDB.sql)?</summary>
    Task<bool> HeeftBlokkadesAsync();
    /// <summary>De fouten bij een exemplaar: open eerst, daarna de opgeloste (nieuwste boven).</summary>
    Task<List<OpbouwBlokkade>> GetBlokkadesAsync(int productId);
    Task<int> MeldBlokkadeAsync(int productId, int? faseStapId, string melding, int? door);
    /// <summary>Een open fout oplossen; false als hij al opgelost was.</summary>
    Task<bool> LosBlokkadeOpAsync(int blokkadeId, string oplossing, int? door);
    /// <summary>De exemplaren van een type met een open fout (teken in de exemplarenlijst).</summary>
    Task<HashSet<int>> GeblokkeerdeExemplarenAsync(int productNameId);
}

public class NullOpbouwRepository : IOpbouwRepository
{
    public bool Beschikbaar => false;
    public Task<bool> HeeftSoortAsync() => Task.FromResult(false);
    public Task<bool> HeeftBoomAsync() => Task.FromResult(false);
    public Task<List<OpbouwStap>> GetStappenAsync(int productNameId, int? productId = null) => Task.FromResult(new List<OpbouwStap>());
    public Task<int> BewaarStapAsync(OpbouwStap stap, int? door) => Task.FromResult(0);
    public Task VerwijderStapAsync(int stapId) => Task.CompletedTask;
    public Task ZetVolgordeAsync(IReadOnlyList<int> stapIdsOpVolgorde) => Task.CompletedTask;
    public Task ZetStatusAsync(int productId, int stapId, bool klaar, int? door, string? opmerking = null, bool overgeslagen = false) => Task.CompletedTask;
    public Task<int> ZetTerugAsync(int productId, IReadOnlyCollection<int> stapIds) => Task.FromResult(0);
    public Task<bool> HeeftBlokkadesAsync() => Task.FromResult(false);
    public Task<List<OpbouwBlokkade>> GetBlokkadesAsync(int productId) => Task.FromResult(new List<OpbouwBlokkade>());
    public Task<int> MeldBlokkadeAsync(int productId, int? faseStapId, string melding, int? door) => Task.FromResult(0);
    public Task<bool> LosBlokkadeOpAsync(int blokkadeId, string oplossing, int? door) => Task.FromResult(false);
    public Task<HashSet<int>> GeblokkeerdeExemplarenAsync(int productNameId) => Task.FromResult(new HashSet<int>());
}

public class MySqlOpbouwRepository : IOpbouwRepository
{
    private readonly string _cs;
    public MySqlOpbouwRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    // Kolom Soort aanwezig? (tweede versie van ProductOpbouw_MariaDB.sql) Alleen 'ja' onthouden.
    private bool? _heeftSoort;
    public async Task<bool> HeeftSoortAsync()
    {
        if (_heeftSoort == true) return true;
        try { await using var cn = await OpenAsync(); return await HeeftSoortAsync(cn); }
        catch { return false; }
    }
    private async Task<bool> HeeftSoortAsync(MySqlConnection cn)
    {
        if (_heeftSoort == true) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = 'ProductOpbouwStappen' AND LOWER(column_name) = 'soort'", cn);
        _heeftSoort = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        return _heeftSoort.Value;
    }

    // De boom aanwezig? (ProductOpbouwBoom_MariaDB.sql: ParentStapID en OpVolgorde bij de items, Overgeslagen bij de
    // status.) Alleen 'ja' onthouden: het script mag later gedraaid worden.
    private bool _heeftBoom;
    public async Task<bool> HeeftBoomAsync()
    {
        if (_heeftBoom) return true;
        try { await using var cn = await OpenAsync(); return await HeeftBoomAsync(cn); }
        catch { return false; }
    }
    private async Task<bool> HeeftBoomAsync(MySqlConnection cn)
    {
        if (_heeftBoom) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND " +
            " ((table_name = 'ProductOpbouwStappen' AND LOWER(column_name) IN ('parentstapid', 'opvolgorde')) " +
            "  OR (table_name = 'ProductOpbouwStatus' AND LOWER(column_name) = 'overgeslagen'))", cn);
        return _heeftBoom = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 3;
    }

    public async Task<List<OpbouwStap>> GetStappenAsync(int productNameId, int? productId = null)
    {
        var lijst = new List<OpbouwStap>();
        await using var cn = await OpenAsync();
        var soort = await HeeftSoortAsync(cn) ? "s.`Soort`" : "'opbouw'";
        var boom = await HeeftBoomAsync(cn);
        await using var cmd = new MySqlCommand(
            "SELECT s.`OpbouwStapID`, s.`ProductNameID`, s.`Volgorde`, s.`Naam`, COALESCE(s.`Omschrijving`, ''), s.`Verplicht`, " +
            "       (SELECT COUNT(*) FROM `FileStore` f WHERE f.`StoreType` = @st AND f.`MasterID` = s.`OpbouwStapID`), " +
            $"       COALESCE(x.`Klaar`, 0), x.`KlaarOp`, COALESCE(w.`NaamVoluit`, ''), COALESCE(x.`Opmerking`, ''), {soort}, " +
            (boom ? "s.`ParentStapID`, s.`OpVolgorde`, COALESCE(x.`Overgeslagen`, 0) " : "NULL, 0, 0 ") +
            "  FROM `ProductOpbouwStappen` s " +
            "  LEFT JOIN `ProductOpbouwStatus` x ON x.`OpbouwStapID` = s.`OpbouwStapID` AND x.`ProductID` = @p " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = x.`KlaarDoor` " +
            " WHERE s.`ProductNameID` = @n ORDER BY s.`Volgorde`, s.`OpbouwStapID`", cn);
        cmd.Parameters.AddWithValue("@n", productNameId);
        cmd.Parameters.AddWithValue("@p", (object?)productId ?? -1);
        cmd.Parameters.AddWithValue("@st", OpbouwStore.Stap);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new OpbouwStap
            {
                Id = r.GetInt32(0), ProductNameId = r.GetInt32(1), Volgorde = r.GetInt32(2),
                Naam = r.GetString(3), Omschrijving = r.GetString(4), Verplicht = Convert.ToInt32(r.GetValue(5)) != 0,
                AantalDocumenten = Convert.ToInt32(r.GetValue(6)),
                Klaar = Convert.ToInt32(r.GetValue(7)) != 0,
                KlaarOp = r.IsDBNull(8) ? null : Convert.ToDateTime(r.GetValue(8)),
                KlaarDoor = r.GetString(9).Trim(), Opmerking = r.GetString(10),
                Soort = r.IsDBNull(11) ? OpbouwSoort.Opbouw : r.GetString(11),
                ParentId = r.IsDBNull(12) ? null : Convert.ToInt32(r.GetValue(12)),
                OpVolgorde = Convert.ToInt32(r.GetValue(13)) != 0,
                Overgeslagen = Convert.ToInt32(r.GetValue(14)) != 0,
            });
        return lijst;
    }

    public async Task<int> BewaarStapAsync(OpbouwStap s, int? door)
    {
        await using var cn = await OpenAsync();
        var metSoort = await HeeftSoortAsync(cn);
        var boom = await HeeftBoomAsync(cn);
        if (s.Id == 0 && s.Volgorde == 0)
        {
            // achteraan bij de items met dezelfde ouder (zonder boom: binnen de soort, zoals voorheen)
            await using var max = new MySqlCommand(
                "SELECT COALESCE(MAX(`Volgorde`), 0) + 1 FROM `ProductOpbouwStappen` WHERE `ProductNameID` = @n" +
                (boom ? " AND `ParentStapID` <=> @ouder" : metSoort ? " AND `Soort` = @soort" : ""), cn);
            max.Parameters.AddWithValue("@n", s.ProductNameId);
            if (boom) max.Parameters.AddWithValue("@ouder", (object?)s.ParentId ?? DBNull.Value);
            else if (metSoort) max.Parameters.AddWithValue("@soort", s.Soort);
            s.Volgorde = Convert.ToInt32(await max.ExecuteScalarAsync());
        }
        var velden = "`ProductNameID` = @n, `Volgorde` = @v, `Naam` = @naam, `Omschrijving` = @oms, `Verplicht` = @verpl"
                   + (metSoort ? ", `Soort` = @soort" : "") + (boom ? ", `ParentStapID` = @ouder, `OpVolgorde` = @opvolgorde" : "");
        var sql = s.Id == 0
            ? $"INSERT INTO `ProductOpbouwStappen` SET {velden}, `AddedBy` = @door, `AddedAt` = NOW()"
            : $"UPDATE `ProductOpbouwStappen` SET {velden} WHERE `OpbouwStapID` = @id";
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@n", s.ProductNameId);
        cmd.Parameters.AddWithValue("@v", s.Volgorde);
        cmd.Parameters.AddWithValue("@naam", s.Naam.Trim().Length > 80 ? s.Naam.Trim()[..80] : s.Naam.Trim());
        cmd.Parameters.AddWithValue("@oms", s.Omschrijving.Trim());
        cmd.Parameters.AddWithValue("@verpl", s.Verplicht ? 1 : 0);
        if (metSoort) cmd.Parameters.AddWithValue("@soort", string.IsNullOrWhiteSpace(s.Soort) ? OpbouwSoort.Opbouw : s.Soort);
        if (boom)
        {
            cmd.Parameters.AddWithValue("@ouder", (object?)s.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@opvolgorde", s.OpVolgorde ? 1 : 0);
        }
        if (s.Id == 0) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        else cmd.Parameters.AddWithValue("@id", s.Id);
        await cmd.ExecuteNonQueryAsync();
        return s.Id == 0 ? (int)cmd.LastInsertedId : s.Id;
    }

    public async Task VerwijderStapAsync(int stapId)
    {
        await using var cn = await OpenAsync();
        // het item en alles wat eronder hangt (laag voor laag; een boom is hooguit een paar niveaus diep)
        var alle = new List<int> { stapId };
        if (await HeeftBoomAsync(cn))
        {
            var laag = new List<int> { stapId };
            for (var diepte = 0; diepte < 20 && laag.Count > 0; diepte++)
            {
                var volgende = new List<int>();
                await using var kind = new MySqlCommand(
                    $"SELECT `OpbouwStapID` FROM `ProductOpbouwStappen` WHERE `ParentStapID` IN ({string.Join(", ", laag)})", cn);
                await using (var r = await kind.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                        if (!alle.Contains(r.GetInt32(0))) volgende.Add(r.GetInt32(0));
                alle.AddRange(volgende);
                laag = volgende;
            }
        }
        await using var cmd = new MySqlCommand(
            $"DELETE FROM `ProductOpbouwStappen` WHERE `OpbouwStapID` IN ({string.Join(", ", alle)})", cn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetVolgordeAsync(IReadOnlyList<int> ids)
    {
        await using var cn = await OpenAsync();
        for (var i = 0; i < ids.Count; i++)
        {
            await using var cmd = new MySqlCommand("UPDATE `ProductOpbouwStappen` SET `Volgorde` = @v WHERE `OpbouwStapID` = @id", cn);
            cmd.Parameters.AddWithValue("@v", i + 1);
            cmd.Parameters.AddWithValue("@id", ids[i]);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task ZetStatusAsync(int productId, int stapId, bool klaar, int? door, string? opmerking = null, bool overgeslagen = false)
    {
        await using var cn = await OpenAsync();
        var boom = await HeeftBoomAsync(cn);
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProductOpbouwStatus` (`ProductID`, `OpbouwStapID`, `Klaar`, `KlaarOp`, `KlaarDoor`, `Opmerking`" + (boom ? ", `Overgeslagen`" : "") + ") " +
            "VALUES (@p, @s, @k, @op, @door, @opm" + (boom ? ", @over" : "") + ") " +
            "ON DUPLICATE KEY UPDATE `Klaar` = VALUES(`Klaar`), `KlaarOp` = VALUES(`KlaarOp`), `KlaarDoor` = VALUES(`KlaarDoor`), " +
            "  `Opmerking` = COALESCE(VALUES(`Opmerking`), `Opmerking`)" + (boom ? ", `Overgeslagen` = VALUES(`Overgeslagen`)" : ""), cn);
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.Parameters.AddWithValue("@s", stapId);
        cmd.Parameters.AddWithValue("@k", klaar ? 1 : 0);
        cmd.Parameters.AddWithValue("@op", klaar ? DateTime.Now : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@door", klaar ? (object?)door ?? DBNull.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@opm", (object?)opmerking ?? DBNull.Value);
        if (boom) cmd.Parameters.AddWithValue("@over", klaar && overgeslagen ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> ZetTerugAsync(int productId, IReadOnlyCollection<int> stapIds)
    {
        var ids = stapIds.Distinct().ToList();
        if (ids.Count == 0) return 0;
        await using var cn = await OpenAsync();
        var boom = await HeeftBoomAsync(cn);
        // KlaarOp en KlaarDoor blijven staan: zo is nog te zien wie het eerder afvinkte
        await using var cmd = new MySqlCommand(
            "UPDATE `ProductOpbouwStatus` SET `Klaar` = 0" + (boom ? ", `Overgeslagen` = 0" : "") +
            $" WHERE `ProductID` = @p AND `Klaar` <> 0 AND `OpbouwStapID` IN ({string.Join(", ", ids)})", cn);
        cmd.Parameters.AddWithValue("@p", productId);
        return await cmd.ExecuteNonQueryAsync();
    }

    // ---- fouten in de opbouw (ProductOpbouwDeel3_MariaDB.sql); alleen 'ja' onthouden ----
    private bool _heeftBlokkades;
    public async Task<bool> HeeftBlokkadesAsync()
    {
        if (_heeftBlokkades) return true;
        try { await using var cn = await OpenAsync(); return await HeeftBlokkadesAsync(cn); }
        catch { return false; }
    }
    private async Task<bool> HeeftBlokkadesAsync(MySqlConnection cn)
    {
        if (_heeftBlokkades) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'ProductOpbouwBlokkades'", cn);
        return _heeftBlokkades = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    public async Task<List<OpbouwBlokkade>> GetBlokkadesAsync(int productId)
    {
        var lijst = new List<OpbouwBlokkade>();
        await using var cn = await OpenAsync();
        if (!await HeeftBlokkadesAsync(cn)) return lijst;
        await using var cmd = new MySqlCommand(
            "SELECT b.`BlokkadeID`, b.`ProductID`, b.`FaseStapID`, b.`Melding`, b.`GemeldOp`, COALESCE(wg.`NaamVoluit`, ''), " +
            "       COALESCE(b.`Oplossing`, ''), b.`OpgelostOp`, COALESCE(wo.`NaamVoluit`, ''), " +
            "       (SELECT COUNT(*) FROM `FileStore` f WHERE f.`StoreType` = @st AND f.`MasterID` = b.`BlokkadeID`) " +
            "  FROM `ProductOpbouwBlokkades` b " +
            "  LEFT JOIN `Werknemers` wg ON wg.`Werknemer-id` = b.`GemeldDoor` " +
            "  LEFT JOIN `Werknemers` wo ON wo.`Werknemer-id` = b.`OpgelostDoor` " +
            " WHERE b.`ProductID` = @p ORDER BY (b.`OpgelostOp` IS NOT NULL), b.`GemeldOp` DESC, b.`BlokkadeID` DESC", cn);
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.Parameters.AddWithValue("@st", OpbouwStore.Blokkade);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new OpbouwBlokkade
            {
                Id = r.GetInt32(0), ProductId = r.GetInt32(1),
                FaseStapId = r.IsDBNull(2) ? null : Convert.ToInt32(r.GetValue(2)),
                Melding = r.GetString(3), GemeldOp = Convert.ToDateTime(r.GetValue(4)), GemeldDoor = r.GetString(5).Trim(),
                Oplossing = r.GetString(6), OpgelostOp = r.IsDBNull(7) ? null : Convert.ToDateTime(r.GetValue(7)),
                OpgelostDoor = r.GetString(8).Trim(), AantalFotos = Convert.ToInt32(r.GetValue(9)),
            });
        return lijst;
    }

    private static string Kort(string tekst, int max)
    {
        tekst = (tekst ?? "").Trim();
        return tekst.Length <= max ? tekst : tekst[..max];
    }

    public async Task<int> MeldBlokkadeAsync(int productId, int? faseStapId, string melding, int? door)
    {
        await using var cn = await OpenAsync();
        if (!await HeeftBlokkadesAsync(cn))
            throw new InvalidOperationException("De tabel ProductOpbouwBlokkades bestaat nog niet (ProductOpbouwDeel3_MariaDB.sql).");
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProductOpbouwBlokkades` (`ProductID`, `FaseStapID`, `Melding`, `GemeldOp`, `GemeldDoor`) " +
            "VALUES (@p, @fase, @melding, NOW(), @door)", cn);
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.Parameters.AddWithValue("@fase", (object?)faseStapId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@melding", Kort(melding, 1000));
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    public async Task<bool> LosBlokkadeOpAsync(int blokkadeId, string oplossing, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `ProductOpbouwBlokkades` SET `Oplossing` = @opl, `OpgelostOp` = NOW(), `OpgelostDoor` = @door " +
            " WHERE `BlokkadeID` = @id AND `OpgelostOp` IS NULL", cn);
        cmd.Parameters.AddWithValue("@id", blokkadeId);
        cmd.Parameters.AddWithValue("@opl", Kort(oplossing, 1000));
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    public async Task<HashSet<int>> GeblokkeerdeExemplarenAsync(int productNameId)
    {
        var uit = new HashSet<int>();
        await using var cn = await OpenAsync();
        if (!await HeeftBlokkadesAsync(cn)) return uit;
        await using var cmd = new MySqlCommand(
            "SELECT DISTINCT b.`ProductID` FROM `ProductOpbouwBlokkades` b JOIN `Producten` p ON p.`ProductenID` = b.`ProductID` " +
            " WHERE b.`OpgelostOp` IS NULL AND p.`ProductNameID` = @n", cn);
        cmd.Parameters.AddWithValue("@n", productNameId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit.Add(r.GetInt32(0));
        return uit;
    }
}
