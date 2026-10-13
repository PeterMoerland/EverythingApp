using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een artikel dat ergens op voorraad ligt (Access ArtikelInVoorraad, lijst links).</summary>
public record VoorraadArtikel(int ArtikelId, string Code, string Omschrijving, decimal Totaal, int Posities);

/// <summary>Wat er van een artikel op één plek ligt, per status (Nieuw, Gebruikt, Incompleet).</summary>
public record VoorraadPositie(int LokatieId, int? StatusId, string Status, decimal Aantal);

public record VoorraadStatusKeuze(int Id, string Naam);

/// <summary>Een regel uit VoorraadMutaties, voor de historie.</summary>
public record VoorraadMutatieRegel(DateTime? Op, decimal Mutatie, string Soort, int? LokatieId, string Status,
                                   string Door, string Werknemer, string Werkorder);

/// <summary>
/// Voorraad per artikel en plek (tabel VoorraadMutaties: elke mutatie is een regel met + of -; de stand is de som).
/// Overgezet van ArtikelInVoorraad, ArtikelInboeken, ArtikelUitboeken en ArtikelVerplaatsen.
/// </summary>
public interface IVoorraadRepository
{
    bool Beschikbaar { get; }
    Task<List<VoorraadArtikel>> ArtikelenAsync(string? zoek, IReadOnlyCollection<int>? groepIds, int? lokatieId, int max);
    Task<List<VoorraadPositie>> PositiesAsync(int artikelId);
    Task<List<VoorraadStatusKeuze>> StatussenAsync();
    /// <summary>Alle artikelen (ook zonder voorraad), om in te boeken.</summary>
    Task<List<VoorraadArtikel>> ZoekArtikelenAsync(string zoek, int max);
    /// <summary>Een gescande code: MDE123 (artikellabel), het artikelnummer of een EAN-code.</summary>
    Task<int?> ArtikelVoorScanAsync(string code);
    Task<List<VoorraadMutatieRegel>> HistorieAsync(int artikelId, int max);
    Task<List<(int Id, string Naam)>> PersonenAsync();

    Task InboekenAsync(int artikelId, int lokatieId, int statusId, decimal aantal, int? door);
    Task UitboekenAsync(int artikelId, int lokatieId, int? statusId, decimal aantal, int? door, int? werknemerId, int? werkorderId);
    Task VerplaatsAsync(int artikelId, int vanLokatieId, int? statusId, int naarLokatieId, decimal aantal, int? door, int? werknemerId);
    /// <summary>Telling: zet de stand op een plek op <paramref name="nieuwAantal"/> met één correctiemutatie.</summary>
    Task CorrigeerAsync(int artikelId, int lokatieId, int? statusId, decimal nieuwAantal, int? door);
    /// <summary>De persoonlijke lokatie van een werknemer (onder "Werknemer"); maakt hem aan als hij er nog niet is.</summary>
    Task<int> PersoonLokatieAsync(int werknemerId, int? door);
}

public class GeenVoorraadRepository : IVoorraadRepository
{
    public bool Beschikbaar => false;
    public Task<List<VoorraadArtikel>> ArtikelenAsync(string? zoek, IReadOnlyCollection<int>? groepIds, int? lokatieId, int max) => Task.FromResult(new List<VoorraadArtikel>());
    public Task<List<VoorraadPositie>> PositiesAsync(int artikelId) => Task.FromResult(new List<VoorraadPositie>());
    public Task<List<VoorraadStatusKeuze>> StatussenAsync() => Task.FromResult(new List<VoorraadStatusKeuze>());
    public Task<List<VoorraadArtikel>> ZoekArtikelenAsync(string zoek, int max) => Task.FromResult(new List<VoorraadArtikel>());
    public Task<int?> ArtikelVoorScanAsync(string code) => Task.FromResult<int?>(null);
    public Task<List<VoorraadMutatieRegel>> HistorieAsync(int artikelId, int max) => Task.FromResult(new List<VoorraadMutatieRegel>());
    public Task<List<(int Id, string Naam)>> PersonenAsync() => Task.FromResult(new List<(int, string)>());
    public Task InboekenAsync(int artikelId, int lokatieId, int statusId, decimal aantal, int? door) => Task.CompletedTask;
    public Task UitboekenAsync(int artikelId, int lokatieId, int? statusId, decimal aantal, int? door, int? werknemerId, int? werkorderId) => Task.CompletedTask;
    public Task VerplaatsAsync(int artikelId, int vanLokatieId, int? statusId, int naarLokatieId, decimal aantal, int? door, int? werknemerId) => Task.CompletedTask;
    public Task CorrigeerAsync(int artikelId, int lokatieId, int? statusId, decimal nieuwAantal, int? door) => Task.CompletedTask;
    public Task<int> PersoonLokatieAsync(int werknemerId, int? door) => Task.FromResult(0);
}

public class MySqlVoorraadRepository : IVoorraadRepository
{
    /// <summary>VoorraadMutatieEvent 2 = Handcorrectie (HAND); zo boekt Access alles vanuit deze schermen.</summary>
    private const int EventHand = 2;
    /// <summary>De lokatie "Werknemer" waaronder de persoonlijke lokaties hangen (Access: AddLocationPerson(1065, ...)).</summary>
    private const int WerknemersLokatie = 1065;
    private const int TypeWerknemer = 20;

    private readonly string _cs;
    public MySqlVoorraadRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static int? I(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static decimal Dec(MySqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Math.Round(Convert.ToDecimal(r.GetValue(i)), 3);

    // De stand telt ALLE mutaties op een plek. Access (ArtikelInVoorraad) liet de mutaties met een werkorder weg,
    // waardoor wat naar een project was uitgeboekt nog op voorraad leek te liggen.
    private const string Stand = "ABS(SUM(m.`Mutatie`)) > 0.0005";

    public async Task<List<VoorraadArtikel>> ArtikelenAsync(string? zoek, IReadOnlyCollection<int>? groepIds, int? lokatieId, int max)
    {
        var waar = new List<string> { "m.`LocationID` IS NOT NULL" };
        if (lokatieId is not null) waar.Add("m.`LocationID` = @l");
        var buiten = new List<string>();
        if (!string.IsNullOrWhiteSpace(zoek))
            buiten.Add("CONCAT_WS(' ', a.`ArtikelID`, a.`ArtikelCodeMDE`, a.`OmschrijvingNL`, a.`ArtikelCodeFactory`) LIKE @z");
        var ids = (groepIds ?? Array.Empty<int>()).Where(i => i > 0).Distinct().Take(2000).ToList();
        if (ids.Count > 0)
            buiten.Add("EXISTS (SELECT 1 FROM `ArtikelGroepenLink` g WHERE g.`ArtikelID` = a.`ArtikelID` AND g.`GroupID` IN (" + string.Join(",", ids) + "))");

        var lijst = new List<VoorraadArtikel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT a.`ArtikelID`, COALESCE(a.`ArtikelCodeMDE`, ''), COALESCE(a.`OmschrijvingNL`, ''), SUM(p.`stand`), COUNT(*) " +
            "  FROM (SELECT m.`ArtikelID`, m.`LocationID`, m.`VoorraadStatusID`, SUM(m.`Mutatie`) AS `stand` " +
            "          FROM `VoorraadMutaties` m WHERE " + string.Join(" AND ", waar) +
            "         GROUP BY m.`ArtikelID`, m.`LocationID`, m.`VoorraadStatusID` HAVING " + Stand + ") p " +
            "  JOIN `Artikelen` a ON a.`ArtikelID` = p.`ArtikelID` " +
            (buiten.Count > 0 ? " WHERE " + string.Join(" AND ", buiten) : "") +
            " GROUP BY a.`ArtikelID`, a.`ArtikelCodeMDE`, a.`OmschrijvingNL` " +
            $" ORDER BY a.`ArtikelCodeMDE` LIMIT {Math.Clamp(max, 1, 5000)}", cn);
        if (lokatieId is not null) cmd.Parameters.AddWithValue("@l", lokatieId);
        if (!string.IsNullOrWhiteSpace(zoek)) cmd.Parameters.AddWithValue("@z", "%" + zoek.Trim() + "%");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new VoorraadArtikel(r.GetInt32(0), S(r, 1), S(r, 2), Dec(r, 3), Convert.ToInt32(r.GetValue(4))));
        return lijst;
    }

    public async Task<List<VoorraadPositie>> PositiesAsync(int artikelId)
    {
        var lijst = new List<VoorraadPositie>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT m.`LocationID`, m.`VoorraadStatusID`, COALESCE(s.`Status`, ''), SUM(m.`Mutatie`) " +
            "  FROM `VoorraadMutaties` m LEFT JOIN `VoorraadStatus` s ON s.`VoorraadStatusID` = m.`VoorraadStatusID` " +
            " WHERE m.`ArtikelID` = @a AND m.`LocationID` IS NOT NULL " +
            " GROUP BY m.`LocationID`, m.`VoorraadStatusID`, s.`Status` HAVING " + Stand, cn);
        cmd.Parameters.AddWithValue("@a", artikelId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new VoorraadPositie(Convert.ToInt32(r.GetValue(0)), I(r, 1), S(r, 2), Dec(r, 3)));
        return lijst;
    }

    public async Task<List<VoorraadStatusKeuze>> StatussenAsync()
    {
        var lijst = new List<VoorraadStatusKeuze>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT `VoorraadStatusID`, COALESCE(`Status`, '') FROM `VoorraadStatus` ORDER BY `VoorraadStatusID`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new VoorraadStatusKeuze(r.GetInt32(0), S(r, 1)));
        return lijst;
    }

    public async Task<List<VoorraadArtikel>> ZoekArtikelenAsync(string zoek, int max)
    {
        var lijst = new List<VoorraadArtikel>();
        if (string.IsNullOrWhiteSpace(zoek)) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT a.`ArtikelID`, COALESCE(a.`ArtikelCodeMDE`, ''), COALESCE(a.`OmschrijvingNL`, ''), " +
            "       COALESCE((SELECT SUM(m.`Mutatie`) FROM `VoorraadMutaties` m WHERE m.`ArtikelID` = a.`ArtikelID` AND m.`LocationID` IS NOT NULL), 0), 0 " +
            "  FROM `Artikelen` a " +
            " WHERE CONCAT_WS(' ', a.`ArtikelID`, a.`ArtikelCodeMDE`, a.`OmschrijvingNL`, a.`ArtikelCodeFactory`) LIKE @z " +
            $" ORDER BY (a.`ArtikelCodeMDE` = @exact) DESC, a.`ArtikelCodeMDE` LIMIT {Math.Clamp(max, 1, 200)}", cn);
        cmd.Parameters.AddWithValue("@z", "%" + zoek.Trim() + "%");
        cmd.Parameters.AddWithValue("@exact", zoek.Trim());
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new VoorraadArtikel(r.GetInt32(0), S(r, 1), S(r, 2), Dec(r, 3), 0));
        return lijst;
    }

    public async Task<int?> ArtikelVoorScanAsync(string code)
    {
        var c = (code ?? "").Trim();
        if (c.Length == 0) return null;
        await using var cn = await OpenAsync();
        // MDE123 = het artikellabel (LabelService.BarcodeVoor), anders artikelnummer of EAN (Access ArtikelInboeken)
        var nr = c.StartsWith("MDE", StringComparison.OrdinalIgnoreCase) ? c[3..] : c;
        if (int.TryParse(nr, out var id))
        {
            await using var cmd = new MySqlCommand("SELECT `ArtikelID` FROM `Artikelen` WHERE `ArtikelID` = @i", cn);
            cmd.Parameters.AddWithValue("@i", id);
            if (await cmd.ExecuteScalarAsync() is { } w and not DBNull) return Convert.ToInt32(w);
        }
        await using (var cmd = new MySqlCommand("SELECT `ArtikelID` FROM `Artikelen` WHERE `EANcode` = @e LIMIT 2", cn))
        {
            cmd.Parameters.AddWithValue("@e", c);
            var ids = new List<int>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
            if (ids.Count == 1) return ids[0];
        }
        return null;
    }

    public async Task<List<VoorraadMutatieRegel>> HistorieAsync(int artikelId, int max)
    {
        var lijst = new List<VoorraadMutatieRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT m.`MutatieOp`, m.`Mutatie`, COALESCE(e.`EventNaam`, ''), m.`LocationID`, COALESCE(s.`Status`, ''), " +
            "       COALESCE(d.`NaamVoluit`, ''), COALESCE(w.`NaamVoluit`, ''), " +
            "       CASE WHEN o.`Werkbon-id` IS NULL THEN '' ELSE CONCAT(COALESCE(p.`Projectnumber`, '?'), '-', " +
            "            RIGHT(CONCAT('00', COALESCE(o.`Werkbonnummer`, 0)), 2), ' ', COALESCE(o.`Werkbonomschrijving`, '')) END " +
            "  FROM `VoorraadMutaties` m " +
            "  LEFT JOIN `VoorraadMutatieEvent` e ON e.`VoorraadMutatieEventID` = m.`VoorraadMutatieEventID` " +
            "  LEFT JOIN `VoorraadStatus` s ON s.`VoorraadStatusID` = m.`VoorraadStatusID` " +
            "  LEFT JOIN `Werknemers` d ON d.`Werknemer-id` = m.`MutatieDoor` " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = m.`WerknemerID` " +
            "  LEFT JOIN `Workorders` o ON o.`Werkbon-id` = m.`WerkorderID` " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = o.`Project-id` " +
            " WHERE m.`ArtikelID` = @a ORDER BY m.`MutatieOp` DESC, m.`VoorraadMutatiesID` DESC " +
            $" LIMIT {Math.Clamp(max, 1, 1000)}", cn);
        cmd.Parameters.AddWithValue("@a", artikelId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new VoorraadMutatieRegel(r.IsDBNull(0) ? null : Convert.ToDateTime(r.GetValue(0)), Dec(r, 1), S(r, 2),
                I(r, 3), S(r, 4), S(r, 5), S(r, 6), S(r, 7).Trim()));
        return lijst;
    }

    public async Task<List<(int Id, string Naam)>> PersonenAsync()
    {
        var lijst = new List<(int, string)>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Werknemer-id`, COALESCE(`NaamVoluit`, '') FROM `Werknemers` " +
            " WHERE COALESCE(`Persoon`, 0) <> 0 AND COALESCE(`Uit dienst`, 0) = 0 ORDER BY `NaamVoluit`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add((r.GetInt32(0), S(r, 1)));
        return lijst;
    }

    // ================= boeken =================

    private static async Task MutatieAsync(MySqlConnection cn, MySqlTransaction tx, int artikelId, decimal mutatie, int? lokatieId,
                                           int? statusId, int? door, int? werknemerId, int? werkorderId)
    {
        await using var cmd = new MySqlCommand(
            "INSERT INTO `VoorraadMutaties` (`ArtikelID`, `Mutatie`, `MutatieOp`, `MutatieDoor`, `VoorraadMutatieEventID`, " +
            "                                `LocationID`, `VoorraadStatusID`, `WerknemerID`, `WerkorderID`) " +
            "VALUES (@a, @m, NOW(), @d, @e, @l, @s, @w, @o)", cn, tx);
        cmd.Parameters.AddWithValue("@a", artikelId);
        cmd.Parameters.AddWithValue("@m", (double)mutatie);
        cmd.Parameters.AddWithValue("@d", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@e", EventHand);
        cmd.Parameters.AddWithValue("@l", (object?)lokatieId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s", (object?)statusId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@w", (object?)werknemerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o", (object?)werkorderId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>De stand van een artikel op een plek en status, met de regels vergrendeld tot het einde van de transactie.</summary>
    private static async Task<decimal> StandAsync(MySqlConnection cn, MySqlTransaction tx, int artikelId, int lokatieId, int? statusId)
    {
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(SUM(`Mutatie`), 0) FROM `VoorraadMutaties` " +
            " WHERE `ArtikelID` = @a AND `LocationID` = @l AND `VoorraadStatusID` <=> @s FOR UPDATE", cn, tx);
        cmd.Parameters.AddWithValue("@a", artikelId);
        cmd.Parameters.AddWithValue("@l", lokatieId);
        cmd.Parameters.AddWithValue("@s", (object?)statusId ?? DBNull.Value);
        return Math.Round(Convert.ToDecimal(await cmd.ExecuteScalarAsync()), 3);
    }

    private static void ControleerAantal(decimal aantal, decimal stand)
    {
        if (aantal <= 0) throw new InvalidOperationException("Geen juist aantal ingevoerd.");
        if (aantal > stand) throw new InvalidOperationException($"Er ligt maar {stand:0.###} op deze plek.");
    }

    public async Task InboekenAsync(int artikelId, int lokatieId, int statusId, decimal aantal, int? door)
    {
        if (aantal <= 0) throw new InvalidOperationException("Geen juist aantal ingevoerd.");
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await MutatieAsync(cn, tx, artikelId, aantal, lokatieId, statusId, door, null, null);
        await tx.CommitAsync();
    }

    public async Task UitboekenAsync(int artikelId, int lokatieId, int? statusId, decimal aantal, int? door, int? werknemerId, int? werkorderId)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        ControleerAantal(aantal, await StandAsync(cn, tx, artikelId, lokatieId, statusId));
        await MutatieAsync(cn, tx, artikelId, -aantal, lokatieId, statusId, door, werknemerId, werkorderId);

        // Naar een werkorder: ook als projectmateriaal, en meteen geleverd (Access ArtikelUitboeken).
        // Access deed dit ook zonder werkorder; dat gaf projectmateriaal zonder project.
        if (werkorderId is not null)
        {
            await using (var cmd = new MySqlCommand(
                "INSERT INTO `ProjectMaterials` (`WerkorderID`, `ProjectMaterialsImportID`, `Aantal`, `IngevoerdOp`, `IngevoerdDoor`, " +
                "                                `ArtikelID`, `SeeFunktie`, `TimeStamp`, `VrijgegevenVoorBestellen`) " +
                "VALUES (@o, 1, @n, NOW(), @d, @a, '', NOW(), 1)", cn, tx))
            {
                cmd.Parameters.AddWithValue("@o", werkorderId);
                cmd.Parameters.AddWithValue("@n", (double)aantal);
                cmd.Parameters.AddWithValue("@d", (object?)door ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@a", artikelId);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = new MySqlCommand(
                "INSERT INTO `ProjectMaterialsDelivered` (`Aantal`, `ToegevoegdOp`, `ToegevoegdDoor`, `ArtikelID`, `WorkorderID`) " +
                "VALUES (@n, NOW(), @d, @a, @o)", cn, tx))
            {
                cmd.Parameters.AddWithValue("@o", werkorderId);
                cmd.Parameters.AddWithValue("@n", (double)aantal);
                cmd.Parameters.AddWithValue("@d", (object?)door ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@a", artikelId);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        await tx.CommitAsync();
    }

    public async Task VerplaatsAsync(int artikelId, int vanLokatieId, int? statusId, int naarLokatieId, decimal aantal, int? door, int? werknemerId)
    {
        if (vanLokatieId == naarLokatieId) throw new InvalidOperationException("De nieuwe plek is dezelfde als de oude.");
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        ControleerAantal(aantal, await StandAsync(cn, tx, artikelId, vanLokatieId, statusId));
        await MutatieAsync(cn, tx, artikelId, -aantal, vanLokatieId, statusId, door, werknemerId, null);
        await MutatieAsync(cn, tx, artikelId, aantal, naarLokatieId, statusId, door, werknemerId, null);
        await tx.CommitAsync();
    }

    public async Task CorrigeerAsync(int artikelId, int lokatieId, int? statusId, decimal nieuwAantal, int? door)
    {
        if (nieuwAantal < 0) throw new InvalidOperationException("Een negatieve voorraad kan niet.");
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        var verschil = nieuwAantal - await StandAsync(cn, tx, artikelId, lokatieId, statusId);
        if (verschil != 0) await MutatieAsync(cn, tx, artikelId, verschil, lokatieId, statusId, door, null, null);
        await tx.CommitAsync();
    }

    public async Task<int> PersoonLokatieAsync(int werknemerId, int? door)
    {
        await using var cn = await OpenAsync();
        await using (var cmd = new MySqlCommand("SELECT `LocationID` FROM `Locations` WHERE `WerknemerID` = @w ORDER BY `LocationID` LIMIT 1", cn))
        {
            cmd.Parameters.AddWithValue("@w", werknemerId);
            if (await cmd.ExecuteScalarAsync() is { } w and not DBNull) return Convert.ToInt32(w);
        }
        // Access AddLocationPerson: type Werknemer onder "Werknemer", naam en afkorting van de werknemer.
        await using (var cmd = new MySqlCommand(
            "INSERT INTO `Locations` (`LocationTypeID`, `ParentLocationID`, `AangemaaktDoor`, `AangemaaktOp`, `Omschrijving`, " +
            "                         `Afkorting`, `Opmerking`, `GUID`, `Voorraad`, `Index`, `WerknemerID`) " +
            "SELECT @t, @p, @d, NOW(), LEFT(COALESCE(`NaamVoluit`, ''), 50), LEFT(COALESCE(`Afkorting`, ''), 10), '', " +
            "       REPLACE(UUID(), '-', ''), 1, 0, `Werknemer-id` FROM `Werknemers` WHERE `Werknemer-id` = @w; " +
            "SELECT LAST_INSERT_ID();", cn))
        {
            cmd.Parameters.AddWithValue("@t", TypeWerknemer);
            cmd.Parameters.AddWithValue("@p", WerknemersLokatie);
            cmd.Parameters.AddWithValue("@d", (object?)door ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@w", werknemerId);
            var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (id == 0) throw new InvalidOperationException("Werknemer niet gevonden.");
            return id;
        }
    }
}
