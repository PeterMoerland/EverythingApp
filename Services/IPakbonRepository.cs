using System.Collections.Concurrent;
using MySqlConnector;

namespace EverythingApp.Services;

// ============================================================
//  Pakbonnen (Access: formulier Pakbonnen met PakbonSub, PakbonSub2, PakbonNieuw; rapporten Pakbon en
//  PakbonPlaatjes). Tabellen Pakbonnen, PakbonItem en PakbonStatus bestonden al in MariaDB.
//  Wissen = Deleted op 1 (zoals Access), nooit echt verwijderen.
// ============================================================

/// <summary>Een pakbon in de lijst links.</summary>
public record PakbonLijstRegel(int Id, string Klant, DateTime? Datum, string Status, int Regels)
{
    public string Nummer => "PS-" + Id;
}

/// <summary>De kop van een pakbon (Access: PakbonSub2).</summary>
public class Pakbon
{
    public int Id { get; set; }
    public int? RelatieId { get; set; }
    public string Klant { get; set; } = "";
    public int? ContactId { get; set; }
    public DateTime? AanmaakDatum { get; set; }
    public string AangemaaktDoor { get; set; } = "";
    public int? StatusId { get; set; }
    public string Opmerking { get; set; } = "";
    public string MeldingOpBon { get; set; } = "";
    public string TrackNTrace { get; set; } = "";
    public string Nummer => "PS-" + Id;
}

/// <summary>Eén regel van een pakbon (tabel PakbonItem; Access: PakbonSub).</summary>
public class PakbonItem
{
    public int Id { get; set; }
    public int PakbonId { get; set; }
    public decimal Aantal { get; set; }
    public string Artikelnummer { get; set; } = "";     // Partnumber
    public string Omschrijving { get; set; } = "";      // Description
    public string Opmerking { get; set; } = "";         // Remark
    public int? MdeId { get; set; }                     // MDE_ID (Artikelen.ArtikelID)
    public decimal Gewicht { get; set; }                // kg per stuk
    public string ReferentieKlant { get; set; } = "";   // InkooporderKlant
}

/// <summary>Een PO om regels uit over te nemen (Access: Parts Search History met "PAKBON").</summary>
public record PakbonPoKeuze(int OrderId, string Leverancier, DateTime? Datum, string Omschrijving)
{
    public string Nummer => "PO-" + OrderId;
}

/// <summary>Een regel van zo'n PO.</summary>
public record PakbonPoRegel(int OrderItemId, decimal Aantal, string Artikelnummer, string Omschrijving, int? MdeId);

/// <summary>Alles wat de afdruk nodig heeft (Access: rapport Pakbon, regels samengevoegd zoals daar).</summary>
public class PakbonAfdruk
{
    public int Id { get; set; }
    public string Klant { get; set; } = "";
    public string Adres { get; set; } = "";
    public string Postcode { get; set; } = "";
    public string Plaats { get; set; } = "";
    public string Land { get; set; } = "";
    public int Landnummer { get; set; } = 31;
    public string Tav { get; set; } = "";
    public string MeldingOpBon { get; set; } = "";
    public List<PakbonItem> Regels { get; set; } = new();

    /// <summary>De referenties van de klant, zonder dubbele (Access: GetOrderRef).</summary>
    public string Referenties => string.Join(", ", Regels.Select(r => r.ReferentieKlant.Trim())
                                                       .Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
}

public interface IPakbonRepository
{
    bool Beschikbaar { get; }
    /// <summary>Pakbonnen (niet gewist), nieuwste eerst; klant = deel van de naam (leeg = alles).</summary>
    Task<List<PakbonLijstRegel>> ZoekAsync(string klant, int max);
    Task<Pakbon?> GetAsync(int id);
    Task<List<PakbonItem>> RegelsAsync(int pakbonId);
    Task<List<Keuze>> StatussenAsync();
    Task<List<Keuze>> ContactenAsync(int relatieId);
    /// <summary>Nieuwe pakbon met status 1 (Aanmaken), zoals Access; geeft het id.</summary>
    Task<int> NieuwAsync(int relatieId, int? contactId, int? doorWerknemerId);
    Task BewaarKopAsync(Pakbon p);
    /// <summary>Deleted = 1 (Access: Wis). De pakbon blijft in de tabel staan.</summary>
    Task WisAsync(int id);
    Task<int> VoegRegelToeAsync(PakbonItem r);
    Task BewaarRegelAsync(PakbonItem r);
    Task VerwijderRegelAsync(int itemId);
    Task<List<PakbonPoKeuze>> ZoekPosAsync(string zoek, int max);
    Task<List<PakbonPoRegel>> PoRegelsAsync(int orderId);
    /// <summary>Artikelcode, omschrijving (NL) en gewicht per stuk uit Artikelen.</summary>
    Task<(string Code, string Omschrijving, decimal Gewicht)?> ArtikelAsync(int artikelId);
    Task<PakbonAfdruk?> AfdrukAsync(int id);
    /// <summary>De afzender op de afdruk (een eigen BV uit Relations).</summary>
    Task<Models.FactuurAfzender?> AfzenderAsync(int bvId);
}

public class GeenPakbonRepository : IPakbonRepository
{
    public bool Beschikbaar => false;
    public Task<List<PakbonLijstRegel>> ZoekAsync(string klant, int max) => Task.FromResult(new List<PakbonLijstRegel>());
    public Task<Pakbon?> GetAsync(int id) => Task.FromResult<Pakbon?>(null);
    public Task<List<PakbonItem>> RegelsAsync(int pakbonId) => Task.FromResult(new List<PakbonItem>());
    public Task<List<Keuze>> StatussenAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> ContactenAsync(int relatieId) => Task.FromResult(new List<Keuze>());
    public Task<int> NieuwAsync(int relatieId, int? contactId, int? door) => throw new InvalidOperationException("Er is geen database.");
    public Task BewaarKopAsync(Pakbon p) => Task.CompletedTask;
    public Task WisAsync(int id) => Task.CompletedTask;
    public Task<int> VoegRegelToeAsync(PakbonItem r) => throw new InvalidOperationException("Er is geen database.");
    public Task BewaarRegelAsync(PakbonItem r) => Task.CompletedTask;
    public Task VerwijderRegelAsync(int itemId) => Task.CompletedTask;
    public Task<List<PakbonPoKeuze>> ZoekPosAsync(string zoek, int max) => Task.FromResult(new List<PakbonPoKeuze>());
    public Task<List<PakbonPoRegel>> PoRegelsAsync(int orderId) => Task.FromResult(new List<PakbonPoRegel>());
    public Task<(string Code, string Omschrijving, decimal Gewicht)?> ArtikelAsync(int artikelId)
        => Task.FromResult<(string, string, decimal)?>(null);
    public Task<PakbonAfdruk?> AfdrukAsync(int id) => Task.FromResult<PakbonAfdruk?>(null);
    public Task<Models.FactuurAfzender?> AfzenderAsync(int bvId) => Task.FromResult<Models.FactuurAfzender?>(null);
}

public class MySqlPakbonRepository : IPakbonRepository
{
    private readonly string _cs;
    public MySqlPakbonRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i)) ?? "";
    private static int? I(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static decimal D(MySqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToDecimal(r.GetValue(i));
    private static object Db(string? s, int max) => s is null ? "" : s.Length > max ? s[..max] : s;
    private static object Leeg(string? s, int max) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : Db(s.Trim(), max);

    public async Task<List<PakbonLijstRegel>> ZoekAsync(string klant, int max)
    {
        var uit = new List<PakbonLijstRegel>();
        klant = klant.Trim();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT p.`PakbonID`, COALESCE(r.`name`, ''), p.`AanmaakDatum`, COALESCE(s.`Status`, ''), " +
            "       (SELECT COUNT(*) FROM `PakbonItem` i WHERE i.`PakbonID` = p.`PakbonID`) " +
            "  FROM `Pakbonnen` p " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = p.`RelationID` " +
            "  LEFT JOIN `PakbonStatus` s ON s.`StatusID` = p.`StatusID` " +
            " WHERE COALESCE(p.`Deleted`, 0) = 0 " +
            (klant.Length == 0 ? "" : "   AND (r.`name` LIKE @k OR CONCAT('PS-', p.`PakbonID`) LIKE @k OR CAST(p.`PakbonID` AS CHAR) LIKE @k) ") +
            " ORDER BY p.`PakbonID` DESC LIMIT " + Math.Clamp(max, 1, 2000), cn);
        if (klant.Length > 0) cmd.Parameters.AddWithValue("@k", "%" + klant + "%");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            uit.Add(new PakbonLijstRegel(r.GetInt32(0), S(r, 1), r.IsDBNull(2) ? null : r.GetDateTime(2), S(r, 3), Convert.ToInt32(r.GetValue(4))));
        return uit;
    }

    public async Task<Pakbon?> GetAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT p.`PakbonID`, p.`RelationID`, COALESCE(r.`name`, ''), p.`ContactID`, p.`AanmaakDatum`, " +
            "       COALESCE(w.`NaamVoluit`, ''), p.`StatusID`, COALESCE(p.`Opmerking`, ''), COALESCE(p.`MeldingOpBon`, ''), " +
            "       COALESCE(p.`TrackNTrace`, '') " +
            "  FROM `Pakbonnen` p " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = p.`RelationID` " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = p.`AangemaaktDoorID` " +
            " WHERE p.`PakbonID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new Pakbon
        {
            Id = r.GetInt32(0), RelatieId = I(r, 1), Klant = S(r, 2), ContactId = I(r, 3),
            AanmaakDatum = r.IsDBNull(4) ? null : r.GetDateTime(4), AangemaaktDoor = S(r, 5), StatusId = I(r, 6),
            Opmerking = S(r, 7), MeldingOpBon = S(r, 8), TrackNTrace = S(r, 9),
        };
    }

    public async Task<List<PakbonItem>> RegelsAsync(int pakbonId)
    {
        var uit = new List<PakbonItem>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `PakbonItemID`, `PakbonID`, COALESCE(`Aantal`, 0), COALESCE(`Partnumber`, ''), COALESCE(`Description`, ''), " +
            "       COALESCE(`Remark`, ''), `MDE_ID`, COALESCE(`Gewicht`, 0), COALESCE(`InkooporderKlant`, '') " +
            "  FROM `PakbonItem` WHERE `PakbonID` = @id ORDER BY `PakbonItemID`", cn);
        cmd.Parameters.AddWithValue("@id", pakbonId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            uit.Add(new PakbonItem
            {
                Id = r.GetInt32(0), PakbonId = r.GetInt32(1), Aantal = D(r, 2), Artikelnummer = S(r, 3), Omschrijving = S(r, 4),
                Opmerking = S(r, 5), MdeId = I(r, 6), Gewicht = D(r, 7), ReferentieKlant = S(r, 8),
            });
        return uit;
    }

    public async Task<List<Keuze>> StatussenAsync()
    {
        var uit = new List<Keuze>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT `StatusID`, COALESCE(`Status`, '') FROM `PakbonStatus` ORDER BY `StatusID`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit.Add(new Keuze(r.GetInt32(0), S(r, 1)));
        return uit;
    }

    public async Task<List<Keuze>> ContactenAsync(int relatieId)
    {
        var uit = new List<Keuze>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ID_contact`, COALESCE(`Naam`, '') FROM `Contacts` WHERE `id_supplier` = @r " +
            "   AND COALESCE(`OutOfCompany`, 0) = 0 ORDER BY `Naam`", cn);
        cmd.Parameters.AddWithValue("@r", relatieId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit.Add(new Keuze(r.GetInt32(0), S(r, 1)));
        return uit;
    }

    public async Task<int> NieuwAsync(int relatieId, int? contactId, int? doorWerknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `Pakbonnen` (`RelationID`, `ContactID`, `AanmaakDatum`, `AangemaaktDoorID`, `StatusID`, `sGUID`, `WerkorderID`, `Deleted`) " +
            "VALUES (@r, @c, NOW(), @door, 1, @guid, NULL, 0)", cn);
        cmd.Parameters.AddWithValue("@r", relatieId);
        cmd.Parameters.AddWithValue("@c", (object?)contactId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@guid", Guid.NewGuid().ToString());
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    public async Task BewaarKopAsync(Pakbon p)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Pakbonnen` SET `RelationID` = @r, `ContactID` = @c, `StatusID` = @s, `Opmerking` = @o, " +
            "       `MeldingOpBon` = @m, `TrackNTrace` = @t WHERE `PakbonID` = @id", cn);
        cmd.Parameters.AddWithValue("@r", (object?)p.RelatieId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@c", (object?)p.ContactId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s", (object?)p.StatusId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o", Leeg(p.Opmerking, 500));
        cmd.Parameters.AddWithValue("@m", Leeg(p.MeldingOpBon, 500));
        cmd.Parameters.AddWithValue("@t", Leeg(p.TrackNTrace, 100));
        cmd.Parameters.AddWithValue("@id", p.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task WisAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `Pakbonnen` SET `Deleted` = 1 WHERE `PakbonID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void RegelParameters(MySqlCommand cmd, PakbonItem r)
    {
        cmd.Parameters.AddWithValue("@pb", r.PakbonId);
        cmd.Parameters.AddWithValue("@a", r.Aantal);
        cmd.Parameters.AddWithValue("@pn", Db(r.Artikelnummer, 45));
        cmd.Parameters.AddWithValue("@d", Db(r.Omschrijving, 400));
        cmd.Parameters.AddWithValue("@rem", Db(r.Opmerking, 100));
        cmd.Parameters.AddWithValue("@mde", (object?)r.MdeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@g", r.Gewicht);
        cmd.Parameters.AddWithValue("@ref", Db(r.ReferentieKlant, 45));
    }

    public async Task<int> VoegRegelToeAsync(PakbonItem r)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `PakbonItem` (`PakbonID`, `Aantal`, `Partnumber`, `Description`, `Remark`, `MDE_ID`, `Gewicht`, `InkooporderKlant`) " +
            "VALUES (@pb, @a, @pn, @d, @rem, @mde, @g, @ref)", cn);
        RegelParameters(cmd, r);
        await cmd.ExecuteNonQueryAsync();
        r.Id = (int)cmd.LastInsertedId;
        return r.Id;
    }

    public async Task BewaarRegelAsync(PakbonItem r)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `PakbonItem` SET `PakbonID` = @pb, `Aantal` = @a, `Partnumber` = @pn, `Description` = @d, `Remark` = @rem, " +
            "       `MDE_ID` = @mde, `Gewicht` = @g, `InkooporderKlant` = @ref WHERE `PakbonItemID` = @id", cn);
        RegelParameters(cmd, r);
        cmd.Parameters.AddWithValue("@id", r.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task VerwijderRegelAsync(int itemId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `PakbonItem` WHERE `PakbonItemID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", itemId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<PakbonPoKeuze>> ZoekPosAsync(string zoek, int max)
    {
        var uit = new List<PakbonPoKeuze>();
        zoek = zoek.Trim();
        var nummer = zoek.StartsWith("PO-", StringComparison.OrdinalIgnoreCase) ? zoek[3..] : zoek;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT o.`Order-id`, COALESCE(r.`name`, ''), o.`Creation date`, COALESCE(o.`Description`, '') " +
            "  FROM `Orders` o LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            (zoek.Length == 0 ? "" :
             " WHERE CAST(o.`Order-id` AS CHAR) LIKE @n OR r.`name` LIKE @z OR o.`Description` LIKE @z ") +
            " ORDER BY o.`Order-id` DESC LIMIT " + Math.Clamp(max, 1, 200), cn);
        if (zoek.Length > 0)
        {
            cmd.Parameters.AddWithValue("@n", nummer + "%");
            cmd.Parameters.AddWithValue("@z", "%" + zoek + "%");
        }
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            uit.Add(new PakbonPoKeuze(r.GetInt32(0), S(r, 1), r.IsDBNull(2) ? null : r.GetDateTime(2), S(r, 3)));
        return uit;
    }

    public async Task<List<PakbonPoRegel>> PoRegelsAsync(int orderId)
    {
        var uit = new List<PakbonPoRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Orderitem-id`, COALESCE(`Quantity ordered`, 0), COALESCE(`Partnumber`, ''), COALESCE(`Description`, ''), `MDE_ID` " +
            "  FROM `Orderitems` WHERE `Order-id` = @o AND COALESCE(`ToBeDeleted`, 0) = 0 ORDER BY `Orderitem-id`", cn);
        cmd.Parameters.AddWithValue("@o", orderId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            uit.Add(new PakbonPoRegel(r.GetInt32(0), D(r, 1), S(r, 2), S(r, 3), I(r, 4)));
        return uit;
    }

    public async Task<(string Code, string Omschrijving, decimal Gewicht)?> ArtikelAsync(int artikelId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(`ArtikelCodeMDE`, ''), COALESCE(`OmschrijvingNL`, ''), COALESCE(`Gewicht`, 0) FROM `Artikelen` WHERE `ArtikelID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", artikelId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (S(r, 0), S(r, 1), D(r, 2));
    }

    public async Task<PakbonAfdruk?> AfdrukAsync(int id)
    {
        PakbonAfdruk? a = null;
        await using var cn = await OpenAsync();
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(r.`name`, ''), COALESCE(r.`address`, ''), COALESCE(r.`PO_box`, ''), COALESCE(r.`town`, ''), " +
            "       COALESCE(l.`Country`, ''), COALESCE(l.`Landnummer`, 31), COALESCE(c.`Naam`, ''), COALESCE(p.`MeldingOpBon`, '') " +
            "  FROM `Pakbonnen` p " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = p.`RelationID` " +
            "  LEFT JOIN `Country` l ON l.`Country-id` = r.`country-id` " +
            "  LEFT JOIN `Contacts` c ON c.`ID_contact` = p.`ContactID` " +
            " WHERE p.`PakbonID` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                a = new PakbonAfdruk
                {
                    Id = id, Klant = S(r, 0), Adres = S(r, 1), Postcode = S(r, 2), Plaats = S(r, 3), Land = S(r, 4),
                    Landnummer = Convert.ToInt32(r.GetValue(5)), Tav = S(r, 6), MeldingOpBon = S(r, 7),
                };
        }
        if (a is null) return null;

        // Zoals het Access-rapport: gelijke regels (artikel, omschrijving, gewicht, referentie) samen, aantallen opgeteld.
        await using (var cmd = new MySqlCommand(
            "SELECT SUM(COALESCE(`Aantal`, 0)), COALESCE(`Partnumber`, ''), COALESCE(`Description`, ''), COALESCE(`Gewicht`, 0), " +
            "       COALESCE(`InkooporderKlant`, ''), MIN(`MDE_ID`), MIN(`PakbonItemID`) " +
            "  FROM `PakbonItem` WHERE `PakbonID` = @id " +
            " GROUP BY COALESCE(`Partnumber`, ''), COALESCE(`Description`, ''), COALESCE(`Gewicht`, 0), COALESCE(`InkooporderKlant`, '') " +
            " ORDER BY MIN(`PakbonItemID`)", cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                a.Regels.Add(new PakbonItem
                {
                    Aantal = D(r, 0), Artikelnummer = S(r, 1), Omschrijving = S(r, 2), Gewicht = D(r, 3),
                    ReferentieKlant = S(r, 4), MdeId = I(r, 5),
                });
        }
        return a;
    }

    public async Task<Models.FactuurAfzender?> AfzenderAsync(int bvId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `id_supplier`, COALESCE(`Afkorting`, ''), COALESCE(`name`, ''), COALESCE(`address`, ''), COALESCE(`PO_box`, ''), " +
            "       COALESCE(`town`, ''), COALESCE(`email`, ''), COALESCE(`VAT number`, ''), COALESCE(`KvK`, '') " +
            "  FROM `Relations` WHERE `id_supplier` = @bv", cn);
        cmd.Parameters.AddWithValue("@bv", bvId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new Models.FactuurAfzender
        {
            Id = r.GetInt32(0), Afkorting = S(r, 1), Naam = S(r, 2), Adres = S(r, 3), Postcode = S(r, 4),
            Plaats = S(r, 5), Email = S(r, 6), Btw = S(r, 7), Kvk = S(r, 8),
        };
    }
}

/// <summary>
/// Alleen om het scherm lokaal te testen zonder in de echte pakbontabellen te schrijven: Pakbon__Geheugen=true.
/// Leest de echte pakbonnen (en PO's, artikelen, afzender) via de gewone repository; alles wat schrijft blijft in
/// het geheugen, en pakbonnen die hier zijn gemaakt of gewijzigd komen uit het geheugen.
/// </summary>
public class GeheugenPakbonRepository : IPakbonRepository
{
    private readonly IPakbonRepository _echt;
    private readonly ConcurrentDictionary<int, Pakbon> _koppen = new();
    private readonly ConcurrentDictionary<int, List<PakbonItem>> _regels = new();
    private readonly HashSet<int> _gewist = new();
    private int _volgendePakbon = 900000, _volgendeRegel = 900000;

    public GeheugenPakbonRepository(IPakbonRepository echt) => _echt = echt;
    public bool Beschikbaar => _echt.Beschikbaar;

    public async Task<List<PakbonLijstRegel>> ZoekAsync(string klant, int max)
    {
        var lijst = (await _echt.ZoekAsync(klant, max)).Where(p => !_gewist.Contains(p.Id) && !_koppen.ContainsKey(p.Id)).ToList();
        foreach (var k in _koppen.Values.Where(k => !_gewist.Contains(k.Id)))
            if (klant.Length == 0 || k.Klant.Contains(klant, StringComparison.OrdinalIgnoreCase) || ("PS-" + k.Id).Contains(klant, StringComparison.OrdinalIgnoreCase))
                lijst.Add(new PakbonLijstRegel(k.Id, k.Klant, k.AanmaakDatum, "Aanmaken", (await RegelsAsync(k.Id)).Count));
        return lijst.OrderByDescending(p => p.Id).Take(max).ToList();
    }

    public async Task<Pakbon?> GetAsync(int id)
    {
        if (_koppen.TryGetValue(id, out var k)) return k;
        var p = await _echt.GetAsync(id);
        if (p is not null) _koppen[id] = p;   // vanaf nu wijzigingen in het geheugen
        return p;
    }

    public async Task<List<PakbonItem>> RegelsAsync(int pakbonId)
    {
        if (_regels.TryGetValue(pakbonId, out var l)) return l.ToList();
        var echt = pakbonId >= 900000 ? new List<PakbonItem>() : await _echt.RegelsAsync(pakbonId);
        _regels[pakbonId] = echt;
        return echt.ToList();
    }

    public Task<List<Keuze>> StatussenAsync() => _echt.StatussenAsync();
    public Task<List<Keuze>> ContactenAsync(int relatieId) => _echt.ContactenAsync(relatieId);

    public Task<int> NieuwAsync(int relatieId, int? contactId, int? door)
    {
        var id = Interlocked.Increment(ref _volgendePakbon);
        _koppen[id] = new Pakbon { Id = id, RelatieId = relatieId, ContactId = contactId, AanmaakDatum = DateTime.Now, StatusId = 1,
                                   Klant = "(relatie " + relatieId + ")", AangemaaktDoor = "test" };
        _regels[id] = new();
        return Task.FromResult(id);
    }

    public Task BewaarKopAsync(Pakbon p) { _koppen[p.Id] = p; return Task.CompletedTask; }
    public Task WisAsync(int id) { lock (_gewist) _gewist.Add(id); return Task.CompletedTask; }

    public async Task<int> VoegRegelToeAsync(PakbonItem r)
    {
        await RegelsAsync(r.PakbonId);
        r.Id = Interlocked.Increment(ref _volgendeRegel);
        _regels[r.PakbonId].Add(r);
        return r.Id;
    }

    public async Task BewaarRegelAsync(PakbonItem r)
    {
        if (!_regels.ContainsKey(r.PakbonId)) await RegelsAsync(r.PakbonId);
        var l = _regels[r.PakbonId];
        var i = l.FindIndex(x => x.Id == r.Id);
        if (i >= 0) l[i] = r;
    }

    public Task VerwijderRegelAsync(int itemId)
    {
        foreach (var l in _regels.Values) l.RemoveAll(x => x.Id == itemId);
        return Task.CompletedTask;
    }

    public Task<List<PakbonPoKeuze>> ZoekPosAsync(string zoek, int max) => _echt.ZoekPosAsync(zoek, max);
    public Task<List<PakbonPoRegel>> PoRegelsAsync(int orderId) => _echt.PoRegelsAsync(orderId);
    public Task<(string Code, string Omschrijving, decimal Gewicht)?> ArtikelAsync(int artikelId) => _echt.ArtikelAsync(artikelId);
    public Task<Models.FactuurAfzender?> AfzenderAsync(int bvId) => _echt.AfzenderAsync(bvId);

    public async Task<PakbonAfdruk?> AfdrukAsync(int id)
    {
        if (!_koppen.ContainsKey(id)) return await _echt.AfdrukAsync(id);
        var k = _koppen[id];
        var a = (k.Id < 900000 ? await _echt.AfdrukAsync(id) : null) ?? new PakbonAfdruk { Id = id, Klant = k.Klant };
        a.MeldingOpBon = k.MeldingOpBon;
        a.Regels = (await RegelsAsync(id))
            .GroupBy(r => (r.Artikelnummer, r.Omschrijving, r.Gewicht, r.ReferentieKlant))
            .Select(g => new PakbonItem { Aantal = g.Sum(x => x.Aantal), Artikelnummer = g.Key.Artikelnummer, Omschrijving = g.Key.Omschrijving,
                                          Gewicht = g.Key.Gewicht, ReferentieKlant = g.Key.ReferentieKlant, MdeId = g.First().MdeId })
            .ToList();
        return a;
    }
}
