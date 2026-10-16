using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

public interface IBestelRepository
{
    bool Beschikbaar { get; }

    Task<HashSet<string>> KolommenAsync();

    /// <summary>Bestellingen zoeken op nummer, leverancier, omschrijving of besteller.</summary>
    Task<List<BestelRegel>> ZoekAsync(string? zoek, int? status, int? leverancierId,
                                      int? besteldDoor, int max);

    Task<Dictionary<string, object?>> GetVeldenAsync(int orderId);
    Task UpdateVeldAsync(int orderId, string kolom, object? waarde);

    Task<int> NieuweBestellingAsync(int? leverancierId, int? werkorderId, int? door);

    /// <summary>
    /// Status verzetten. Bij een stap vooruit wordt het datum/wie-veld van de stap
    /// die je verlaat ingevuld, zoals Access dat ook doet.
    /// </summary>
    Task ZetStatusAsync(int orderId, int vanStatus, int naarStatus, int? door);

    Task<List<Bestelstatus>> GetStatussenAsync();

    // --- regels ---
    Task<List<Orderregel>> GetRegelsAsync(int orderId);
    Task<int> BewaarRegelAsync(Orderregel regel);
    Task VerwijderRegelAsync(int orderitemId);

    // --- keuzelijsten ---
    Task<List<Keuze>> GetLeveranciersAsync();
    /// <summary>Leveranciers en werkorders van de laatste bestellingen van deze werknemer, nieuwste eerst (popup Nieuwe bestelling).</summary>
    Task<(List<int> Leveranciers, List<int> Werkorders)> RecentGebruiktAsync(int werknemerId, int max);

    // ---- standaardwaarden van de leverancier (FillInDefaults_Order in Access) ----
    Task<LeverancierStandaard?> LeverancierStandaardAsync(int leverancierId);
    /// <summary>
    /// Zet de standaardwaarden op een bestelling: contactpersoon, taal, leverdatum (alleen als die leeg is) en het
    /// afleveradres: <paramref name="afleverenBijStandaard"/> = het standaard afleveradres van de leverancier, anders leeg.
    /// </summary>
    Task VulStandaardAsync(int orderId, LeverancierStandaard s, bool afleverenBijStandaard);

    // ---- Plakken uit klembord (PasteFromClipboard_Click in Access) ----
    /// <summary>
    /// Artikel bij een geplakt nummer, in de volgorde van Access: bestelnummer bij deze leverancier (ArtikelSupplier),
    /// MDE-code, ArtikelID (als het een getal is), @ArtikelSearchlist.Filtered, fabriekscode. Null = niet gevonden.
    /// </summary>
    Task<PlakArtikel?> ZoekPlakArtikelAsync(string nummer, int? leverancierId);
    Task<PlakLeverancier> PlakLeverancierAsync(int? leverancierId);
    /// <summary>Komt het artikel uit Inventor (@ArtikelListInventor)? Alleen dan gaan er bijlages mee.</summary>
    Task<bool> IsInventorArtikelAsync(int artikelId);
    Task ZetEanAsync(int regelId, string ean);
    Task<List<Keuze>> GetContactenAsync(int? leverancierId);
    Task<List<Keuze>> GetEenhedenAsync();
    Task<List<Keuze>> GetTalenAsync();
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenBestelRepository : IBestelRepository
{
    public bool Beschikbaar => false;
    public Task<HashSet<string>> KolommenAsync() => Task.FromResult(new HashSet<string>());
    public Task<List<BestelRegel>> ZoekAsync(string? zoek, int? status, int? leverancierId,
                                             int? besteldDoor, int max)
        => Task.FromResult(new List<BestelRegel>());
    public Task<Dictionary<string, object?>> GetVeldenAsync(int orderId)
        => Task.FromResult(new Dictionary<string, object?>());
    public Task UpdateVeldAsync(int orderId, string kolom, object? waarde) => Task.CompletedTask;
    public Task<int> NieuweBestellingAsync(int? leverancierId, int? werkorderId, int? door) => Task.FromResult(0);
    public Task ZetStatusAsync(int orderId, int vanStatus, int naarStatus, int? door) => Task.CompletedTask;
    public Task<List<Bestelstatus>> GetStatussenAsync() => Task.FromResult(new List<Bestelstatus>());
    public Task<List<Orderregel>> GetRegelsAsync(int orderId) => Task.FromResult(new List<Orderregel>());
    public Task<int> BewaarRegelAsync(Orderregel regel) => Task.FromResult(0);
    public Task VerwijderRegelAsync(int orderitemId) => Task.CompletedTask;
    public Task<List<Keuze>> GetLeveranciersAsync() => Task.FromResult(new List<Keuze>());
    public Task<(List<int> Leveranciers, List<int> Werkorders)> RecentGebruiktAsync(int werknemerId, int max)
        => Task.FromResult((new List<int>(), new List<int>()));
    public Task<LeverancierStandaard?> LeverancierStandaardAsync(int leverancierId) => Task.FromResult<LeverancierStandaard?>(null);
    public Task VulStandaardAsync(int orderId, LeverancierStandaard s, bool afleverenBijStandaard) => Task.CompletedTask;
    public Task<PlakArtikel?> ZoekPlakArtikelAsync(string nummer, int? leverancierId) => Task.FromResult<PlakArtikel?>(null);
    public Task<PlakLeverancier> PlakLeverancierAsync(int? leverancierId) => Task.FromResult(PlakLeverancier.Geen);
    public Task<bool> IsInventorArtikelAsync(int artikelId) => Task.FromResult(false);
    public Task ZetEanAsync(int regelId, string ean) => Task.CompletedTask;
    public Task<List<Keuze>> GetContactenAsync(int? leverancierId) => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetEenhedenAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetTalenAsync() => Task.FromResult(new List<Keuze>());
}

/// <summary>Bestellingen uit Orders, Orderitems, OrderItemsDelivered en OrderState.</summary>
public class MySqlBestelRepository : IBestelRepository
{
    private readonly string _cs;
    private HashSet<string>? _kolommen;
    private List<Bestelstatus>? _statussen;
    private List<Keuze>? _eenheden;

    public MySqlBestelRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<HashSet<string>> KolommenAsync()
    {
        if (_kolommen is not null) return _kolommen;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = 'Orders' ORDER BY ordinal_position", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) set.Add(r.GetString(0));
        return _kolommen = set;
    }

    // ============================================================
    //  Zoeken
    // ============================================================
    public async Task<List<BestelRegel>> ZoekAsync(string? zoek, int? status, int? leverancierId,
                                                   int? besteldDoor, int max)
    {
        var delen = new List<string>();

        if (!string.IsNullOrWhiteSpace(zoek))
        {
            // een getal is bijna altijd het PO-nummer
            delen.Add(int.TryParse(zoek.Trim().TrimStart('P', 'O', 'p', 'o', '-'), out _)
                ? "(CAST(o.`Order-id` AS CHAR) LIKE @z OR o.`Description` LIKE @z OR r.`name` LIKE @z)"
                : "(o.`Description` LIKE @z OR r.`name` LIKE @z OR o.`Remark` LIKE @z)");
        }
        if (status is not null) delen.Add("o.`Status` = @s");
        if (leverancierId is not null) delen.Add("o.`Supplier` = @lev");
        if (besteldDoor is not null) delen.Add("o.`Created by` = @door");

        var where = delen.Count > 0 ? "WHERE " + string.Join(" AND ", delen) : "";

        var lijst = new List<BestelRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT o.`Order-id`, COALESCE(r.`name`, ''), COALESCE(o.`Description`, ''), " +
            "       COALESCE(o.`Status`, 0), COALESCE(st.`Omschrijving`, ''), o.`Creation date`, " +
            "       COALESCE((SELECT SUM(i.`Totalprice`) FROM `Orderitems` i " +
            "                  WHERE i.`Order-id` = o.`Order-id`), 0) " +
            "  FROM `Orders` o " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            "  LEFT JOIN `OrderState` st ON st.`Id` = o.`Status` " +
            $" {where} " +
            $" ORDER BY o.`Order-id` DESC LIMIT {Math.Clamp(max, 1, 500)}", cn);

        if (!string.IsNullOrWhiteSpace(zoek))
        {
            var schoon = zoek.Trim();
            if (schoon.StartsWith("PO-", StringComparison.OrdinalIgnoreCase)) schoon = schoon[3..];
            cmd.Parameters.AddWithValue("@z", "%" + schoon + "%");
        }
        if (status is not null) cmd.Parameters.AddWithValue("@s", status.Value);
        if (leverancierId is not null) cmd.Parameters.AddWithValue("@lev", leverancierId.Value);
        if (besteldDoor is not null) cmd.Parameters.AddWithValue("@door", besteldDoor.Value);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var id = rd.GetInt32(0);
            lijst.Add(new BestelRegel(
                id,
                "PO-" + id,
                rd.IsDBNull(1) ? "" : rd.GetString(1),
                rd.IsDBNull(2) ? "" : rd.GetString(2),
                rd.IsDBNull(3) ? 0 : Convert.ToInt32(rd.GetValue(3)),
                rd.IsDBNull(4) ? "" : rd.GetString(4),
                rd.IsDBNull(5) ? null : Convert.ToDateTime(rd.GetValue(5)),
                rd.IsDBNull(6) ? 0 : Convert.ToDecimal(rd.GetValue(6))));
        }
        return lijst;
    }

    public async Task<Dictionary<string, object?>> GetVeldenAsync(int orderId)
    {
        var velden = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT * FROM `Orders` WHERE `Order-id` = @id", cn);
        cmd.Parameters.AddWithValue("@id", orderId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
            for (var i = 0; i < r.FieldCount; i++)
                velden[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return velden;
    }

    public async Task UpdateVeldAsync(int orderId, string kolom, object? waarde)
    {
        var kolommen = await KolommenAsync();
        var echt = kolommen.FirstOrDefault(k => string.Equals(k, kolom, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Kolom '{kolom}' bestaat niet in Orders.");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"UPDATE `Orders` SET `{echt}` = @w WHERE `Order-id` = @id", cn);
        cmd.Parameters.AddWithValue("@w", waarde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", orderId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> NieuweBestellingAsync(int? leverancierId, int? werkorderId, int? door)
    {
        var kolommen = await KolommenAsync();
        var sets = new List<string> { "`GUID` = @guid", "`Status` = 0" };

        if (leverancierId is not null) sets.Add("`Supplier` = @lev");
        if (werkorderId is not null) sets.Add("`Workorder-id` = @wo");
        if (kolommen.Contains("Extension")) sets.Add("`Extension` = 999");
        if (kolommen.Contains("Language")) sets.Add("`Language` = 0");
        if (kolommen.Contains("Creation date")) sets.Add("`Creation date` = NOW()");
        if (kolommen.Contains("Created by")) sets.Add("`Created by` = @door");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"INSERT INTO `Orders` SET {string.Join(", ", sets)}", cn);
        cmd.Parameters.AddWithValue("@guid", Guid.NewGuid().ToString("N"));
        if (leverancierId is not null) cmd.Parameters.AddWithValue("@lev", leverancierId.Value);
        if (werkorderId is not null) cmd.Parameters.AddWithValue("@wo", werkorderId.Value);
        if (kolommen.Contains("Created by")) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    /// <summary>
    /// Status verzetten. Hoort er bij die stap een datum- en wie-kolom (aangevraagd,
    /// goedgekeurd, besteld…), dan wordt die meteen ingevuld — zo blijft zichtbaar
    /// wie wat wanneer heeft gedaan.
    /// </summary>
    public async Task ZetStatusAsync(int orderId, int vanStatus, int naarStatus, int? door)
    {
        var kolommen = await KolommenAsync();
        var sets = new List<string> { "`Status` = @s" };

        // Vooruit: de stap die je verlaat krijgt zijn datum en naam.
        // Terug: niets invullen, anders overschrijf je wat er al stond.
        if (naarStatus > vanStatus && BestelVelden.StatusVelden.TryGetValue(vanStatus, out var paar))
        {
            if (kolommen.Contains(paar.Datum)) sets.Add($"`{paar.Datum}` = NOW()");
            if (kolommen.Contains(paar.Door)) sets.Add($"`{paar.Door}` = @door");
        }

        if (naarStatus == BestelVelden.StatusGeannuleerd)
        {
            if (kolommen.Contains("Canceled date")) sets.Add("`Canceled date` = NOW()");
            if (kolommen.Contains("Canceled by")) sets.Add("`Canceled by` = @door");
        }

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"UPDATE `Orders` SET {string.Join(", ", sets)} WHERE `Order-id` = @id", cn);
        cmd.Parameters.AddWithValue("@s", naarStatus);
        cmd.Parameters.AddWithValue("@id", orderId);
        if (sets.Any(s => s.Contains("@door"))) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Bestelstatus>> GetStatussenAsync()
    {
        if (_statussen is not null) return _statussen;

        var lijst = new List<Bestelstatus>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Id`, COALESCE(`Omschrijving`, ''), COALESCE(`Status`, ''), COALESCE(`Status2`, '') " +
            "  FROM `OrderState` ORDER BY `Id`", cn);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Bestelstatus(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3)));

        return _statussen = lijst;
    }

    // ============================================================
    //  Regels
    // ============================================================
    public async Task<List<Orderregel>> GetRegelsAsync(int orderId)
    {
        var lijst = new List<Orderregel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT i.`Orderitem-id`, i.`Quantity ordered`, i.`VE-id`, i.`Partnumber`, i.`Description`, " +
            "       i.`MDE_ID`, i.`Agreed date of delivery`, i.`ASAP`, i.`Schattingsprijs per VE`, " +
            "       i.`Grossprice per VE`, i.`Discount`, i.`Totalprice`, i.`Remark`, " +
            "       i.`Notify_when_delivered`, i.`Workorder-ID`, " +
            "       COALESCE((SELECT SUM(d.`Quantity delivered`) FROM `OrderItemsDelivered` d " +
            "                  WHERE d.`OrderItem-id` = i.`Orderitem-id`), 0), " +
            "       COALESCE((SELECT COUNT(*) FROM `FileStore` f " +
            "                  WHERE f.`StoreType` = 3 AND f.`MasterID` = i.`Orderitem-id`), 0) " +
            "  FROM `Orderitems` i " +
            " WHERE i.`Order-id` = @id " +
            "   AND (i.`ToBeDeleted` = 0 OR i.`ToBeDeleted` IS NULL) " +
            " ORDER BY i.`Orderitem-id`", cn);
        cmd.Parameters.AddWithValue("@id", orderId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string S(int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
            int? I(int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
            double D(int i) => r.IsDBNull(i) ? 0 : Convert.ToDouble(r.GetValue(i));
            decimal? M(int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
            bool B(int i)
            {
                if (r.IsDBNull(i)) return false;
                try { return Convert.ToInt64(r.GetValue(i)) != 0; } catch { return false; }
            }

            lijst.Add(new Orderregel
            {
                Id = r.GetInt32(0),
                OrderId = orderId,
                Aantal = D(1),
                EenheidId = I(2),
                Artikelcode = S(3),
                Omschrijving = S(4),
                MdeCode = S(5),
                Leverdatum = r.IsDBNull(6) ? null : DateOnly.FromDateTime(Convert.ToDateTime(r.GetValue(6))),
                Asap = B(7),
                Schatprijs = M(8),
                Prijs = M(9),
                Korting = M(10),
                Totaal = M(11),
                Opmerking = S(12),
                MailBijLevering = B(13),
                WerkorderId = I(14),
                Geleverd = D(15),
                Bijlagen = r.IsDBNull(16) ? 0 : Convert.ToInt32(r.GetValue(16)),
            });
        }
        return lijst;
    }

    /// <summary>
    /// Regel opslaan. Het totaal wordt hier uitgerekend, zodat het altijd klopt met
    /// aantal, prijs en korting — ook als iemand alleen de korting wijzigt.
    /// </summary>
    public async Task<int> BewaarRegelAsync(Orderregel regel)
    {
        // Zelfde som als de Access-code: aantal × prijs × (1 − korting), waarbij
        // korting een breuk is (0,15 = 15%) en de schattingsprijs invalt zolang
        // er nog geen echte prijs is.
        var totaal = Orderregel.Bereken(regel.Aantal, regel.Prijs ?? regel.Schatprijs, regel.Korting);

        const string velden =
            "`Order-id` = @order, `Quantity ordered` = @aantal, `VE-id` = @ve, " +
            "`Partnumber` = @code, `Description` = @oms, `MDE_ID` = @mde, " +
            "`Agreed date of delivery` = @datum, `ASAP` = @asap, " +
            "`Schattingsprijs per VE` = @schat, `Grossprice per VE` = @prijs, " +
            "`Discount` = @korting, `Totalprice` = @totaal, `Remark` = @opm, " +
            "`Notify_when_delivered` = @mail, `Workorder-ID` = @wo";

        var sql = regel.Id == 0
            ? $"INSERT INTO `Orderitems` SET {velden}, `GUID` = @guid, `Timestamp` = NOW()"
            : $"UPDATE `Orderitems` SET {velden} WHERE `Orderitem-id` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        object Of(object? v) => v ?? DBNull.Value;

        cmd.Parameters.AddWithValue("@order", regel.OrderId);
        cmd.Parameters.AddWithValue("@aantal", regel.Aantal);
        cmd.Parameters.AddWithValue("@ve", Of(regel.EenheidId));
        cmd.Parameters.AddWithValue("@code", regel.Artikelcode ?? "");
        cmd.Parameters.AddWithValue("@oms", regel.Omschrijving ?? "");
        cmd.Parameters.AddWithValue("@mde", Of(string.IsNullOrWhiteSpace(regel.MdeCode)
            ? null : int.TryParse(regel.MdeCode, out var mde) ? mde : null));
        cmd.Parameters.AddWithValue("@datum", regel.Leverdatum is null
            ? DBNull.Value : regel.Leverdatum.Value.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@asap", regel.Asap ? 1 : 0);
        cmd.Parameters.AddWithValue("@schat", Of(regel.Schatprijs));
        cmd.Parameters.AddWithValue("@prijs", Of(regel.Prijs));
        cmd.Parameters.AddWithValue("@korting", Of(regel.Korting));
        cmd.Parameters.AddWithValue("@totaal", totaal);
        cmd.Parameters.AddWithValue("@opm", (regel.Opmerking ?? "").Length > 50
            ? regel.Opmerking![..50] : regel.Opmerking ?? "");
        cmd.Parameters.AddWithValue("@mail", regel.MailBijLevering ? 1 : 0);
        cmd.Parameters.AddWithValue("@wo", Of(regel.WerkorderId));

        if (regel.Id == 0) cmd.Parameters.AddWithValue("@guid", Guid.NewGuid().ToString("N"));
        else cmd.Parameters.AddWithValue("@id", regel.Id);

        await cmd.ExecuteNonQueryAsync();
        return regel.Id == 0 ? (int)cmd.LastInsertedId : regel.Id;
    }

    public async Task VerwijderRegelAsync(int orderitemId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `Orderitems` WHERE `Orderitem-id` = @id", cn);
        cmd.Parameters.AddWithValue("@id", orderitemId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Keuzelijsten
    // ============================================================
    public async Task<(List<int> Leveranciers, List<int> Werkorders)> RecentGebruiktAsync(int werknemerId, int max)
    {
        var lev = new List<int>();
        var wo = new List<int>();
        var kolommen = await KolommenAsync();
        if (!kolommen.Contains("Created by")) return (lev, wo);
        max = Math.Clamp(max, 1, 50);
        await using var cn = await OpenAsync();
        foreach (var (kolom, lijst) in new[] { ("Supplier", lev), ("Workorder-id", wo) })
        {
            await using var cmd = new MySqlCommand(
                $"SELECT `{kolom}` FROM `Orders` WHERE `Created by` = @door AND `{kolom}` IS NOT NULL AND `{kolom}` <> 0 " +
                $" GROUP BY `{kolom}` ORDER BY MAX(`Order-id`) DESC LIMIT {max}", cn);
            cmd.Parameters.AddWithValue("@door", werknemerId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) lijst.Add(Convert.ToInt32(r.GetValue(0)));
        }
        return (lev, wo);
    }

    public async Task<List<Keuze>> GetLeveranciersAsync()
    {
        var lijst = new List<Keuze>();
        await using var cn = await OpenAsync();

        var sql = "SELECT `id_supplier`, `name` FROM `Relations` " +
                  "WHERE (`Supplier` <> 0 OR `Supplier` IS NULL) ORDER BY `name` LIMIT 5000";
        try
        {
            await using var cmd = new MySqlCommand(sql, cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                lijst.Add(new Keuze(r.GetInt32(0), r.IsDBNull(1) ? "(geen naam)" : r.GetString(1)));
        }
        catch
        {
            await using var cmd = new MySqlCommand(
                "SELECT `id_supplier`, `name` FROM `Relations` ORDER BY `name` LIMIT 5000", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                lijst.Add(new Keuze(r.GetInt32(0), r.IsDBNull(1) ? "(geen naam)" : r.GetString(1)));
        }
        return lijst;
    }

    public async Task<List<Keuze>> GetContactenAsync(int? leverancierId)
    {
        var lijst = new List<Keuze>();
        if (leverancierId is null) return lijst;

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ID_contact`, `Naam` FROM `Contacts` WHERE `id_supplier` = @id " +
            "  AND (`OutOfCompany` = 0 OR `OutOfCompany` IS NULL) ORDER BY `Naam`", cn);
        cmd.Parameters.AddWithValue("@id", leverancierId.Value);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Keuze(r.GetInt32(0), r.IsDBNull(1) ? "(geen naam)" : r.GetString(1)));
        return lijst;
    }

    /// <summary>Verpakkingseenheden (st, m, doos). Tabelnaam niet zeker, dus proberen.</summary>
    public async Task<List<Keuze>> GetEenhedenAsync()
    {
        if (_eenheden is not null) return _eenheden;

        foreach (var tabel in new[] { "VE", "Eenheden", "Eenheid", "VerpakkingsEenheid", "Units" })
        {
            var lijst = new List<Keuze>();
            try
            {
                await using var cn = await OpenAsync();
                await using var cmd = new MySqlCommand($"SELECT * FROM `{tabel}` LIMIT 200", cn);
                await using var r = await cmd.ExecuteReaderAsync();

                // De tabel VE heeft kolommen VE-ID en Units ('st', 'm', 'doos')
                var idNamen = new[] { "VE-ID", "VE-id", "VE_id", "VEid", "EenheidID", "id" };
                var naamNamen = new[] { "Units", "VE", "Eenheid", "Omschrijving", "Naam", "name", "Code" };

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
                if (lijst.Count > 0) return _eenheden = lijst.OrderBy(k => k.Naam).ToList();
            }
            catch { /* volgende naam proberen */ }
        }
        return _eenheden = new List<Keuze>();
    }

    // ============================================================
    //  Standaardwaarden van de leverancier (FillInDefaults_Order)
    // ============================================================
    public async Task<LeverancierStandaard?> LeverancierStandaardAsync(int leverancierId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT r.`OrderContact-id`, r.`OrderLanguage`, COALESCE(r.`OrderTime`, 0), r.`DefaultVerzendAdres`, " +
            "       COALESCE(a.`name`, ''), COALESCE(a.`town`, ''), r.`DefaultVerzendContact`, " +
            "       COALESCE(r.`CCbijBestelling`, 0), COALESCE(r.`CCmailadressBijBestelling`, '') " +
            "  FROM `Relations` r LEFT JOIN `Relations` a ON a.`id_supplier` = r.`DefaultVerzendAdres` " +
            " WHERE r.`id_supplier` = @l LIMIT 1", cn);
        cmd.Parameters.AddWithValue("@l", leverancierId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        int? N(int i) => r.IsDBNull(i) || Convert.ToInt64(r.GetValue(i)) == 0 ? null : Convert.ToInt32(r.GetValue(i));
        var cc = Convert.ToInt64(r.GetValue(7)) != 0;
        var ccAdres = r.GetValue(8).ToString()?.Trim() ?? "";
        return new LeverancierStandaard(N(0), r.IsDBNull(1) ? null : Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2)),
                                        N(3), r.GetValue(4).ToString() ?? "", r.GetValue(5).ToString() ?? "", N(6),
                                        cc && ccAdres.Length > 0 && ccAdres != "0", ccAdres);
    }

    public async Task VulStandaardAsync(int orderId, LeverancierStandaard s, bool afleverenBijStandaard)
    {
        var kolommen = await KolommenAsync();
        var sets = new List<string>();
        if (kolommen.Contains("Contact")) sets.Add("`Contact` = @contact");
        if (kolommen.Contains("Language")) sets.Add("`Language` = @taal");
        if (kolommen.Contains("DefaultLeverDatum")) sets.Add("`DefaultLeverDatum` = COALESCE(`DefaultLeverDatum`, @datum)");
        if (kolommen.Contains("AfleverKlant-id")) sets.Add("`AfleverKlant-id` = @aflever");
        if (kolommen.Contains("AfleverTAV-id")) sets.Add("`AfleverTAV-id` = @tav");
        if (sets.Count == 0) return;

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand($"UPDATE `Orders` SET {string.Join(", ", sets)} WHERE `Order-id` = @id", cn);
        object Of(object? w) => w ?? DBNull.Value;
        cmd.Parameters.AddWithValue("@contact", Of(s.ContactId));
        cmd.Parameters.AddWithValue("@taal", s.Taal ?? 0);
        cmd.Parameters.AddWithValue("@datum", s.Leverdatum(DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@aflever", Of(afleverenBijStandaard ? s.AfleverId : null));
        cmd.Parameters.AddWithValue("@tav", Of(afleverenBijStandaard ? s.AfleverContactId : null));
        cmd.Parameters.AddWithValue("@id", orderId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Plakken uit klembord
    // ============================================================
    public async Task<PlakArtikel?> ZoekPlakArtikelAsync(string nummer, int? leverancierId)
    {
        nummer = (nummer ?? "").Trim();
        if (nummer.Length == 0) return null;
        await using var cn = await OpenAsync();

        async Task<int?> Eerste(string sql, params (string, object)[] pars)
        {
            await using var cmd = new MySqlCommand(sql + " LIMIT 1", cn);
            foreach (var (n, w) in pars) cmd.Parameters.AddWithValue(n, w);
            var r = await cmd.ExecuteScalarAsync();
            return r is null or DBNull ? null : Convert.ToInt32(r);
        }

        int? id = null;
        if (leverancierId is int lev)
            id = await Eerste("SELECT `ArtikelID` FROM `ArtikelSupplier` WHERE `ArtikelNummerSupplier` = @n AND `SupplierID` = @l",
                              ("@n", nummer), ("@l", lev));
        id ??= await Eerste("SELECT `ArtikelID` FROM `Artikelen` WHERE `ArtikelCodeMDE` = @n", ("@n", nummer));
        if (id is null && nummer.All(char.IsAsciiDigit) && int.TryParse(nummer, out var nr))
            id = await Eerste("SELECT `ArtikelID` FROM `Artikelen` WHERE `ArtikelID` = @n", ("@n", nr));
        id ??= await Eerste("SELECT `ArtikelID` FROM `@ArtikelSearchlist` WHERE `Filtered` = @n",
                            ("@n", KlembordBestelling.Gefilterd(nummer)));
        id ??= await Eerste("SELECT `ArtikelID` FROM `Artikelen` WHERE `ArtikelCodeFactory` = @n", ("@n", nummer));
        if (id is null) return null;

        await using var lees = new MySqlCommand(
            "SELECT COALESCE(`ArtikelCodeFactory`, ''), COALESCE(`OmschrijvingNL`, ''), COALESCE(`Material`, ''), " +
            "       COALESCE(`Gewicht`, 0), COALESCE(`SchattingsKostprijsStuk`, 0) FROM `Artikelen` WHERE `ArtikelID` = @id", cn);
        lees.Parameters.AddWithValue("@id", id.Value);
        await using var r = await lees.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new PlakArtikel(id.Value, r.GetValue(0).ToString() ?? "", r.GetValue(1).ToString() ?? "", r.GetValue(2).ToString() ?? "",
                               Convert.ToDecimal(r.GetValue(3)), Convert.ToDecimal(r.GetValue(4)));
    }

    public async Task<PlakLeverancier> PlakLeverancierAsync(int? leverancierId)
    {
        if (leverancierId is not int lev) return PlakLeverancier.Geen;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(`ExportPDF`, 0), COALESCE(`ExportDXF`, 0), COALESCE(`ExportSTP`, 0), COALESCE(`ExportSTL`, 0), " +
            "       COALESCE(`KiloprijsALU`, 0), COALESCE(`KiloprijsRVS`, 0), COALESCE(`KiloprijsStaal`, 0), COALESCE(`Instelkosten`, 0) " +
            "  FROM `Relations` WHERE `id_supplier` = @l LIMIT 1", cn);
        cmd.Parameters.AddWithValue("@l", lev);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return PlakLeverancier.Geen;
        bool B(int i) => Convert.ToInt64(r.GetValue(i)) != 0;
        decimal D(int i) => Convert.ToDecimal(r.GetValue(i));
        return new PlakLeverancier(B(0), B(1), B(2), B(3), D(4), D(5), D(6), D(7));
    }

    public async Task<bool> IsInventorArtikelAsync(int artikelId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT 1 FROM `@ArtikelListInventor` WHERE `ArtikelID` = @id LIMIT 1", cn);
        cmd.Parameters.AddWithValue("@id", artikelId);
        return await cmd.ExecuteScalarAsync() is not (null or DBNull);
    }

    public async Task ZetEanAsync(int regelId, string ean)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `Orderitems` SET `EAN` = @e WHERE `Orderitem-id` = @id", cn);
        cmd.Parameters.AddWithValue("@e", ean.Length > 45 ? ean[..45] : ean);
        cmd.Parameters.AddWithValue("@id", regelId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Talen van het orderformulier; 0 = Nederlands, zoals de VBA het zet.</summary>
    public Task<List<Keuze>> GetTalenAsync() => Task.FromResult(new List<Keuze>
    {
        new(0, "Nederlands"),
        new(1, "Engels"),
        new(2, "Duits"),
    });
}
