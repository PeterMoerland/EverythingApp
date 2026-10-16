using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een bestelling die op levering wacht (Access: lijst Purchase_order_select in Orders_incoming_goods).</summary>
public record OntvangstOrder(int OrderId, string Leverancier, string Werkorder, string Project, string Klant,
                             string BesteldDoor, DateTime? Besteld, int OpenRegels, string Opmerking);

/// <summary>Een orderregel bij het aftekenen. Toeslag/correctie = hoeft niet geleverd te worden.</summary>
public record OntvangstRegel(int RegelId, int? ArtikelId, string Artikel, string Omschrijving, string Eenheid,
                             double Besteld, double Geleverd, bool Toeslag)
{
    public double Open => Math.Max(0, Besteld - Geleverd);
}

/// <summary>Een eerdere levering van deze bestelling (OrderItemsDelivered).</summary>
public record OntvangstLevering(DateTime? Op, string Pakbon, double Aantal, string Artikel, string Omschrijving, string Door, string Opmerking);

/// <summary>Uitkomst van aftekenen: is de bestelling nu compleet, en wie hoort dat (en wil hij een mail).</summary>
public record OntvangstUitkomst(int Regels, bool Compleet, int? Ontvanger, bool MailGevraagd);

/// <summary>Alles voor de mail "PO-… is compleet geleverd" (Access SendMail_Orderdelivered).</summary>
public record GeleverdMailGegevens(int OrderId, string Leverancier, string Klant, string Project, string NaamOntvanger,
                                   string MailOntvanger, string Opmerking, List<(double Aantal, string Eenheid, string Artikel, string Omschrijving)> Regels);

/// <summary>
/// Materiaal inboeken (Bestellingen ▸ Ontvangst). Vervangt Orders_incoming_goods + _signoff + MateriaalGeleverd uit
/// Access. Per geleverde regel: OrderItemsDelivered (pakbon, wanneer, door wie, opmerking) en bij een artikel een
/// voorraadmutatie "geleverd op werkorder" (VoorraadMutaties, event 4, status 1, werkorder van de bestelling, zoals
/// Access). Is alles binnen, dan zoals OrderComplete: status Gereed (6), AllesIsGeleverd, Delivered date/by en een regel
/// in Log_Orderchanges. Toeslag- en correctieregels hoeven niet geleverd te worden (Access telde ze wel mee).
/// Weggelaten (besluit gebruiker 16-10-2026): projectkrat scannen (nooit gebruikt) en voorraadorders verdelen.
/// </summary>
public interface IOntvangstRepository
{
    bool Beschikbaar { get; }
    Task<List<OntvangstOrder>> TeOntvangenAsync(string? zoek);
    Task<List<OntvangstRegel>> RegelsAsync(int orderId);
    Task<List<OntvangstLevering>> LeveringenAsync(int orderId);
    Task<OntvangstUitkomst> AftekenenAsync(int orderId, string pakbon, string opmerking, int door, IReadOnlyList<(int RegelId, double Aantal)> regels);
    Task<GeleverdMailGegevens?> GeleverdMailAsync(int orderId, int ontvanger);
    /// <summary>
    /// Oude bestellingen opruimen (Directie): status Gereed, Delivered date/by en een logregel, zonder leveringen te
    /// registreren (er komt dus geen voorraadmutatie en AllesIsGeleverd blijft zoals het was). Alleen status 4/5.
    /// </summary>
    Task<int> OpruimenAsync(IReadOnlyCollection<int> orderIds, int door);
}

public class GeenOntvangstRepository : IOntvangstRepository
{
    public bool Beschikbaar => false;
    public Task<List<OntvangstOrder>> TeOntvangenAsync(string? zoek) => Task.FromResult(new List<OntvangstOrder>());
    public Task<List<OntvangstRegel>> RegelsAsync(int orderId) => Task.FromResult(new List<OntvangstRegel>());
    public Task<List<OntvangstLevering>> LeveringenAsync(int orderId) => Task.FromResult(new List<OntvangstLevering>());
    public Task<OntvangstUitkomst> AftekenenAsync(int orderId, string pakbon, string opmerking, int door, IReadOnlyList<(int RegelId, double Aantal)> regels)
        => Task.FromResult(new OntvangstUitkomst(0, false, null, false));
    public Task<GeleverdMailGegevens?> GeleverdMailAsync(int orderId, int ontvanger) => Task.FromResult<GeleverdMailGegevens?>(null);
    public Task<int> OpruimenAsync(IReadOnlyCollection<int> orderIds, int door) => Task.FromResult(0);
}

public class MySqlOntvangstRepository : IOntvangstRepository
{
    private readonly string _cs;
    public MySqlOntvangstRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    /// <summary>Status 4 (wacht op levering) en 5 (wacht op facturatie), zoals Access.</summary>
    private const string OpenStatus = "o.`Status` IN (4, 5)";
    private const string TeLeveren = "COALESCE(i.`Toeslag`, 0) = 0 AND COALESCE(i.`Correction`, 0) = 0";

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : (r.GetValue(i).ToString() ?? "").Trim();

    public async Task<List<OntvangstOrder>> TeOntvangenAsync(string? zoek)
    {
        var lijst = new List<OntvangstOrder>();
        await using var cn = await OpenAsync();
        var filter = "";
        if (!string.IsNullOrWhiteSpace(zoek))
            filter = " AND (CAST(o.`Order-id` AS CHAR) LIKE @z OR r.`name` LIKE @z OR p.`Projectdescription` LIKE @z " +
                     "      OR CONCAT(p.`Projectnumber`, '-', RIGHT(CONCAT('00', w.`Werkbonnummer`), 2)) LIKE @z OR k.`name` LIKE @z " +
                     "      OR b.`NaamVoluit` LIKE @z)";
        await using var cmd = new MySqlCommand(
            "SELECT o.`Order-id`, COALESCE(r.`name`, ''), " +
            "       COALESCE(CONCAT(p.`Projectnumber`, '-', RIGHT(CONCAT('00', w.`Werkbonnummer`), 2)), ''), " +
            "       COALESCE(p.`Projectdescription`, ''), COALESCE(k.`name`, ''), COALESCE(b.`NaamVoluit`, ''), o.`Ordered date`, " +
            "       (SELECT COUNT(*) FROM `Orderitems` i WHERE i.`Order-id` = o.`Order-id` AND " + TeLeveren +
            "          AND COALESCE(i.`Quantity ordered`, 0) > COALESCE((SELECT SUM(d.`Quantity delivered`) FROM `OrderItemsDelivered` d " +
            "                                                             WHERE d.`OrderItem-id` = i.`Orderitem-id`), 0)), " +
            "       COALESCE(o.`Remark`, '') " +
            "  FROM `Orders` o " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            "  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = o.`Workorder-id` " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            "  LEFT JOIN `Relations` k ON k.`id_supplier` = p.`Customer-id` " +
            "  LEFT JOIN `Werknemers` b ON b.`Werknemer-id` = o.`Created by` " +
            " WHERE " + OpenStatus + filter +
            " ORDER BY o.`Order-id` DESC LIMIT 1000", cn);
        if (filter.Length > 0) cmd.Parameters.AddWithValue("@z", "%" + zoek!.Trim().TrimStart('P', 'O', 'p', 'o', '-') + "%");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new OntvangstOrder(r.GetInt32(0), S(r, 1), S(r, 2), S(r, 3), S(r, 4), S(r, 5),
                r.IsDBNull(6) ? null : Convert.ToDateTime(r.GetValue(6)), Convert.ToInt32(r.GetValue(7)), S(r, 8)));
        return lijst;
    }

    public async Task<List<OntvangstRegel>> RegelsAsync(int orderId)
    {
        var lijst = new List<OntvangstRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT i.`Orderitem-id`, i.`MDE_ID`, COALESCE(i.`Partnumber`, ''), COALESCE(i.`Description`, ''), COALESCE(ve.`Units`, ''), " +
            "       COALESCE(i.`Quantity ordered`, 0), " +
            "       COALESCE((SELECT SUM(d.`Quantity delivered`) FROM `OrderItemsDelivered` d WHERE d.`OrderItem-id` = i.`Orderitem-id`), 0), " +
            "       (COALESCE(i.`Toeslag`, 0) <> 0 OR COALESCE(i.`Correction`, 0) <> 0) " +
            "  FROM `Orderitems` i LEFT JOIN `VE` ve ON ve.`VE-ID` = i.`VE-id` " +
            " WHERE i.`Order-id` = @id ORDER BY i.`Orderitem-id`", cn);
        cmd.Parameters.AddWithValue("@id", orderId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new OntvangstRegel(r.GetInt32(0), r.IsDBNull(1) || Convert.ToInt64(r.GetValue(1)) == 0 ? null : Convert.ToInt32(r.GetValue(1)),
                S(r, 2), S(r, 3), S(r, 4), Convert.ToDouble(r.GetValue(5)), Convert.ToDouble(r.GetValue(6)), Convert.ToInt64(r.GetValue(7)) != 0));
        return lijst;
    }

    public async Task<List<OntvangstLevering>> LeveringenAsync(int orderId)
    {
        var lijst = new List<OntvangstLevering>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT d.`Pakbon signed at`, COALESCE(d.`Pakbonnummer`, ''), COALESCE(d.`Quantity delivered`, 0), " +
            "       COALESCE(i.`Partnumber`, ''), COALESCE(i.`Description`, ''), COALESCE(w.`NaamVoluit`, ''), COALESCE(d.`Remark`, '') " +
            "  FROM `OrderItemsDelivered` d " +
            "  LEFT JOIN `Orderitems` i ON i.`Orderitem-id` = d.`OrderItem-id` " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = d.`Pakbon signed by` " +
            " WHERE d.`Order-id` = @id ORDER BY d.`OrderItemsDelivered` DESC", cn);
        cmd.Parameters.AddWithValue("@id", orderId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new OntvangstLevering(r.IsDBNull(0) ? null : Convert.ToDateTime(r.GetValue(0)), S(r, 1),
                Convert.ToDouble(r.GetValue(2)), S(r, 3), S(r, 4), S(r, 5), S(r, 6)));
        return lijst;
    }

    public async Task<OntvangstUitkomst> AftekenenAsync(int orderId, string pakbon, string opmerking, int door,
                                                        IReadOnlyList<(int RegelId, double Aantal)> regels)
    {
        var mee = regels.Where(r => r.Aantal > 0).ToList();
        if (mee.Count == 0) throw new InvalidOperationException("Er is niets om af te tekenen.");
        pakbon = pakbon.Trim();
        if (pakbon.Length == 0) throw new InvalidOperationException("Vul het pakbonnummer in (of Onbekend).");
        if (pakbon.Length > 30) throw new InvalidOperationException("Pakbonnummer te lang (maximaal 30 tekens).");

        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        // bestelling: werkorder (voor de voorraadmutatie), status, wie hoort het
        int? werkorder = null, besteller = null, notifyTo = null; var mailGevraagd = false; var status = 0;
        await using (var cmd = new MySqlCommand(
            "SELECT o.`Workorder-id`, o.`Created by`, o.`NotifyTo`, COALESCE(o.`Notify_when_delivered`, 0), COALESCE(o.`Status`, 0) " +
            "  FROM `Orders` o WHERE o.`Order-id` = @id FOR UPDATE", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", orderId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) throw new InvalidOperationException($"PO-{orderId} niet gevonden.");
            int? N(int i) => r.IsDBNull(i) || Convert.ToInt64(r.GetValue(i)) == 0 ? null : Convert.ToInt32(r.GetValue(i));
            werkorder = N(0); besteller = N(1); notifyTo = N(2); mailGevraagd = Convert.ToInt64(r.GetValue(3)) != 0;
            status = Convert.ToInt32(r.GetValue(4));
        }
        if (status is not (4 or 5)) throw new InvalidOperationException($"PO-{orderId} wacht niet (meer) op levering.");

        foreach (var (regelId, aantal) in mee)
        {
            int? artikel;
            await using (var cmd = new MySqlCommand(
                "SELECT `MDE_ID` FROM `Orderitems` WHERE `Orderitem-id` = @r AND `Order-id` = @o", cn, tx))
            {
                cmd.Parameters.AddWithValue("@r", regelId);
                cmd.Parameters.AddWithValue("@o", orderId);
                var w = await cmd.ExecuteScalarAsync();
                if (w is null) throw new InvalidOperationException("Regel hoort niet bij deze bestelling.");
                artikel = w is DBNull || Convert.ToInt64(w) == 0 ? null : Convert.ToInt32(w);
            }
            await using (var cmd = new MySqlCommand(
                "INSERT INTO `OrderItemsDelivered` (`OrderItem-id`, `Order-id`, `Quantity delivered`, `Pakbonnummer`, " +
                "                                   `Pakbon signed at`, `Pakbon signed by`, `Remark`) " +
                "VALUES (@r, @o, @n, @p, NOW(), @d, @opm)", cn, tx))
            {
                cmd.Parameters.AddWithValue("@r", regelId);
                cmd.Parameters.AddWithValue("@o", orderId);
                cmd.Parameters.AddWithValue("@n", aantal);
                cmd.Parameters.AddWithValue("@p", pakbon);
                cmd.Parameters.AddWithValue("@d", door);
                cmd.Parameters.AddWithValue("@opm", opmerking.Trim().Length > 0 ? opmerking.Trim() : DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            // artikel: voorraadmutatie "geleverd op werkorder" (Access MateriaalGeleverd: event 4, status 1)
            if (artikel is int a)
            {
                await using var cmd = new MySqlCommand(
                    "INSERT INTO `VoorraadMutaties` (`ArtikelID`, `Mutatie`, `MutatieOp`, `MutatieDoor`, `WerkorderID`, " +
                    "                                `VoorraadMutatieEventID`, `VoorraadStatusID`) " +
                    "VALUES (@a, @n, NOW(), @d, @w, 4, 1)", cn, tx);
                cmd.Parameters.AddWithValue("@a", a);
                cmd.Parameters.AddWithValue("@n", aantal);
                cmd.Parameters.AddWithValue("@d", door);
                cmd.Parameters.AddWithValue("@w", (object?)werkorder ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // compleet? (alle te leveren regels binnen) → zoals OrderComplete: Gereed, AllesIsGeleverd, Delivered date/by, log
        long open;
        await using (var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM `Orderitems` i WHERE i.`Order-id` = @o AND COALESCE(i.`Toeslag`, 0) = 0 AND COALESCE(i.`Correction`, 0) = 0 " +
            "   AND COALESCE(i.`Quantity ordered`, 0) > COALESCE((SELECT SUM(d.`Quantity delivered`) FROM `OrderItemsDelivered` d " +
            "                                                      WHERE d.`OrderItem-id` = i.`Orderitem-id`), 0)", cn, tx))
        {
            cmd.Parameters.AddWithValue("@o", orderId);
            open = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        var compleet = open == 0;
        if (compleet)
        {
            await using (var cmd = new MySqlCommand(
                "UPDATE `Orders` SET `Status` = 6, `AllesIsGeleverd` = 1, `Delivered date` = NOW(), `Delivered by` = @d " +
                " WHERE `Order-id` = @o", cn, tx))
            {
                cmd.Parameters.AddWithValue("@d", door);
                cmd.Parameters.AddWithValue("@o", orderId);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = new MySqlCommand(
                "INSERT INTO `Log_Orderchanges` (`Order-ID`, `Toegevoegd`, `LogTekst`, `Groep`, `Door`) " +
                "VALUES (@o, NOW(), 'Status: 6 door aftekenen van de goederen.', 'Status', @d)", cn, tx))
            {
                cmd.Parameters.AddWithValue("@o", orderId);
                cmd.Parameters.AddWithValue("@d", door);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        await tx.CommitAsync();
        return new OntvangstUitkomst(mee.Count, compleet, notifyTo ?? besteller, mailGevraagd);
    }

    public async Task<int> OpruimenAsync(IReadOnlyCollection<int> orderIds, int door)
    {
        var aantal = 0;
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        foreach (var id in orderIds.Distinct())
        {
            int n;
            await using (var cmd = new MySqlCommand(
                "UPDATE `Orders` SET `Status` = 6, `Delivered date` = NOW(), `Delivered by` = @d " +
                " WHERE `Order-id` = @o AND `Status` IN (4, 5)", cn, tx))
            {
                cmd.Parameters.AddWithValue("@d", door);
                cmd.Parameters.AddWithValue("@o", id);
                n = await cmd.ExecuteNonQueryAsync();
            }
            if (n == 0) continue;
            aantal++;
            await using var log = new MySqlCommand(
                "INSERT INTO `Log_Orderchanges` (`Order-ID`, `Toegevoegd`, `LogTekst`, `Groep`, `Door`) " +
                "VALUES (@o, NOW(), 'Status: 6 bij opruimen oude bestellingen (niet afgetekend).', 'Status', @d)", cn, tx);
            log.Parameters.AddWithValue("@o", id);
            log.Parameters.AddWithValue("@d", door);
            await log.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return aantal;
    }

    public async Task<GeleverdMailGegevens?> GeleverdMailAsync(int orderId, int ontvanger)
    {
        await using var cn = await OpenAsync();
        string lev, klant, project, opm;
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(r.`name`, ''), COALESCE(k.`name`, ''), COALESCE(p.`Projectdescription`, ''), COALESCE(o.`Remark`, '') " +
            "  FROM `Orders` o LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            "  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = o.`Workorder-id` LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            "  LEFT JOIN `Relations` k ON k.`id_supplier` = p.`Customer-id` WHERE o.`Order-id` = @o", cn))
        {
            cmd.Parameters.AddWithValue("@o", orderId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            lev = S(r, 0); klant = S(r, 1); project = S(r, 2); opm = S(r, 3);
        }
        string naam, mail;
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(`Voornaam`, ''), COALESCE(`Mail`, '') FROM `Werknemers` WHERE `Werknemer-id` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", ontvanger);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            naam = S(r, 0); mail = S(r, 1);
        }
        var regels = new List<(double, string, string, string)>();
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(i.`Quantity ordered`, 0), COALESCE(ve.`Units`, ''), COALESCE(i.`Partnumber`, ''), COALESCE(i.`Description`, '') " +
            "  FROM `Orderitems` i LEFT JOIN `VE` ve ON ve.`VE-ID` = i.`VE-id` WHERE i.`Order-id` = @o ORDER BY i.`Orderitem-id`", cn))
        {
            cmd.Parameters.AddWithValue("@o", orderId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) regels.Add((Convert.ToDouble(r.GetValue(0)), S(r, 1), S(r, 2), S(r, 3)));
        }
        return new GeleverdMailGegevens(orderId, lev, klant, project, naam, mail, opm, regels);
    }
}
