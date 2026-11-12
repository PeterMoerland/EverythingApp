using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Wat er met een bericht uit de inkooppostbus is gedaan (tabel InkoopBevestigingen).</summary>
public class InkoopBevestiging
{
    public int Id { get; set; }
    public string MailId { get; set; } = "";
    public DateTime? Ontvangen { get; set; }
    public string Van { get; set; } = "";
    public string Onderwerp { get; set; } = "";
    public string Uitkomst { get; set; } = "";
    /// <summary>De gevonden PO-nummers, met komma's.</summary>
    public string Orders { get; set; } = "";
    public int? OrderId { get; set; }
    public int Bestanden { get; set; }
    public string Melding { get; set; } = "";
    public DateTime VerwerktOp { get; set; }
    public string VerwerktDoor { get; set; } = "";

    /// <summary>Vraagt nog om een handeling: toewijzen aan een bestelling, of negeren.</summary>
    public bool Open => Uitkomst is BevestigingUitkomst.GeenPo or BevestigingUitkomst.Onbekend;
}

public static class BevestigingUitkomst
{
    /// <summary>De PDF's staan bij de bestelling.</summary>
    public const string Gekoppeld = "gekoppeld";
    /// <summary>Een bericht met een PDF, maar zonder herkenbaar PO-nummer.</summary>
    public const string GeenPo = "geenpo";
    /// <summary>Er staat een PO-nummer in, maar die bestelling bestaat niet (of is van lang geleden).</summary>
    public const string Onbekend = "onbekend";
    /// <summary>De bestelling is gevonden, maar het bericht heeft geen PDF.</summary>
    public const string GeenBijlage = "geenbijlage";
    /// <summary>Met de hand weggezet: hoort nergens bij.</summary>
    public const string Genegeerd = "genegeerd";

    public static string Tekst(string uitkomst) => uitkomst switch
    {
        Gekoppeld => "gekoppeld",
        GeenPo => "geen PO-nummer",
        Onbekend => "PO onbekend",
        GeenBijlage => "geen PDF",
        Genegeerd => "genegeerd",
        _ => uitkomst,
    };
}

/// <summary>Een bestelling waar een bevestiging bij kan horen.</summary>
public record BevestigingOrder(int OrderId, string Leverancier, DateTime? Aangemaakt, int Status);

public interface IInkoopBevestigingRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();
    /// <summary>Welke van deze bericht-id's al verwerkt zijn.</summary>
    Task<HashSet<string>> BekendeMailsAsync(IReadOnlyList<string> mailIds);
    /// <summary>De bestellingen met deze nummers die bestaan.</summary>
    Task<List<BevestigingOrder>> OrdersAsync(IReadOnlyList<int> orderIds);
    Task<int> LegVastAsync(InkoopBevestiging regel);
    Task<InkoopBevestiging?> GetAsync(int id);
    /// <summary>Met de hand gekoppeld of genegeerd.</summary>
    Task WerkBijAsync(int id, string uitkomst, int? orderId, int bestanden, string? melding, int? door);
    /// <summary>De laatste berichten: alleen wat nog open staat, of alles.</summary>
    Task<List<InkoopBevestiging>> LijstAsync(bool alleenOpen, int max);
    Task<int> AantalOpenAsync();
}

public class GeenInkoopBevestigingRepository : IInkoopBevestigingRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<HashSet<string>> BekendeMailsAsync(IReadOnlyList<string> mailIds) => Task.FromResult(new HashSet<string>());
    public Task<List<BevestigingOrder>> OrdersAsync(IReadOnlyList<int> orderIds) => Task.FromResult(new List<BevestigingOrder>());
    public Task<int> LegVastAsync(InkoopBevestiging regel) => Task.FromResult(0);
    public Task<InkoopBevestiging?> GetAsync(int id) => Task.FromResult<InkoopBevestiging?>(null);
    public Task WerkBijAsync(int id, string uitkomst, int? orderId, int bestanden, string? melding, int? door) => Task.CompletedTask;
    public Task<List<InkoopBevestiging>> LijstAsync(bool alleenOpen, int max) => Task.FromResult(new List<InkoopBevestiging>());
    public Task<int> AantalOpenAsync() => Task.FromResult(0);
}

public class MySqlInkoopBevestigingRepository : IInkoopBevestigingRepository
{
    private readonly string _cs;
    private bool _tabel;
    public MySqlInkoopBevestigingRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    // Alleen 'ja' onthouden: het script mag gedraaid worden terwijl de app draait.
    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'InkoopBevestigingen'", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _tabel;
    }

    public async Task<HashSet<string>> BekendeMailsAsync(IReadOnlyList<string> mailIds)
    {
        var uit = new HashSet<string>();
        if (mailIds.Count == 0 || !await TabelAanwezigAsync()) return uit;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("", cn);
        var namen = new List<string>();
        for (var i = 0; i < mailIds.Count; i++) { namen.Add("@m" + i); cmd.Parameters.AddWithValue("@m" + i, mailIds[i]); }
        cmd.CommandText = $"SELECT `MailId` FROM `InkoopBevestigingen` WHERE `MailId` IN ({string.Join(", ", namen)})";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit.Add(r.GetString(0));
        return uit;
    }

    public async Task<List<BevestigingOrder>> OrdersAsync(IReadOnlyList<int> orderIds)
    {
        var lijst = new List<BevestigingOrder>();
        var ids = orderIds.Distinct().Take(50).ToList();
        if (ids.Count == 0) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT o.`Order-id`, COALESCE(r.`name`, ''), o.`Creation date`, COALESCE(o.`Status`, 0) " +
            "  FROM `Orders` o LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            $" WHERE o.`Order-id` IN ({string.Join(", ", ids)})", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new BevestigingOrder(Convert.ToInt32(r.GetValue(0)), r.GetString(1),
                                           r.IsDBNull(2) ? null : Convert.ToDateTime(r.GetValue(2)), Convert.ToInt32(r.GetValue(3))));
        return lijst;
    }

    private static string Kort(string? tekst, int max)
    {
        tekst = (tekst ?? "").Trim();
        return tekst.Length <= max ? tekst : tekst[..max];
    }

    public async Task<int> LegVastAsync(InkoopBevestiging b)
    {
        await using var cn = await OpenAsync();
        // hetzelfde bericht nog eens (twee rondes tegelijk): de eerste regel blijft staan
        await using var cmd = new MySqlCommand(
            "INSERT INTO `InkoopBevestigingen` (`MailId`, `Ontvangen`, `Van`, `Onderwerp`, `Uitkomst`, `Orders`, `OrderID`, `Bestanden`, `Melding`, `VerwerktOp`) " +
            "VALUES (@mail, @ontvangen, @van, @onderwerp, @uitkomst, @orders, @order, @bestanden, @melding, NOW()) " +
            "ON DUPLICATE KEY UPDATE `MailId` = `MailId`", cn);
        cmd.Parameters.AddWithValue("@mail", b.MailId);
        cmd.Parameters.AddWithValue("@ontvangen", (object?)b.Ontvangen ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@van", Kort(b.Van, 200));
        cmd.Parameters.AddWithValue("@onderwerp", Kort(b.Onderwerp, 300));
        cmd.Parameters.AddWithValue("@uitkomst", b.Uitkomst);
        cmd.Parameters.AddWithValue("@orders", b.Orders.Length == 0 ? DBNull.Value : Kort(b.Orders, 100));
        cmd.Parameters.AddWithValue("@order", (object?)b.OrderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bestanden", b.Bestanden);
        cmd.Parameters.AddWithValue("@melding", b.Melding.Length == 0 ? DBNull.Value : Kort(b.Melding, 500));
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    private const string Select =
        "SELECT b.`BevestigingID`, b.`MailId`, b.`Ontvangen`, COALESCE(b.`Van`, ''), COALESCE(b.`Onderwerp`, ''), b.`Uitkomst`, " +
        "       COALESCE(b.`Orders`, ''), b.`OrderID`, b.`Bestanden`, COALESCE(b.`Melding`, ''), b.`VerwerktOp`, COALESCE(w.`NaamVoluit`, '') " +
        "  FROM `InkoopBevestigingen` b LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = b.`VerwerktDoor` ";

    private static InkoopBevestiging Lees(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0), MailId = r.GetString(1), Ontvangen = r.IsDBNull(2) ? null : Convert.ToDateTime(r.GetValue(2)),
        Van = r.GetString(3), Onderwerp = r.GetString(4), Uitkomst = r.GetString(5), Orders = r.GetString(6),
        OrderId = r.IsDBNull(7) ? null : Convert.ToInt32(r.GetValue(7)), Bestanden = Convert.ToInt32(r.GetValue(8)),
        Melding = r.GetString(9), VerwerktOp = Convert.ToDateTime(r.GetValue(10)), VerwerktDoor = r.GetString(11).Trim(),
    };

    public async Task<InkoopBevestiging?> GetAsync(int id)
    {
        if (!await TabelAanwezigAsync()) return null;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(Select + " WHERE b.`BevestigingID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Lees(r) : null;
    }

    public async Task WerkBijAsync(int id, string uitkomst, int? orderId, int bestanden, string? melding, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `InkoopBevestigingen` SET `Uitkomst` = @uitkomst, `OrderID` = @order, `Bestanden` = @bestanden, `Melding` = @melding, " +
            "       `VerwerktOp` = NOW(), `VerwerktDoor` = @door WHERE `BevestigingID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@uitkomst", uitkomst);
        cmd.Parameters.AddWithValue("@order", (object?)orderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bestanden", bestanden);
        cmd.Parameters.AddWithValue("@melding", string.IsNullOrWhiteSpace(melding) ? DBNull.Value : Kort(melding, 500));
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<InkoopBevestiging>> LijstAsync(bool alleenOpen, int max)
    {
        var lijst = new List<InkoopBevestiging>();
        if (!await TabelAanwezigAsync()) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(Select +
            (alleenOpen ? $" WHERE b.`Uitkomst` IN ('{BevestigingUitkomst.GeenPo}', '{BevestigingUitkomst.Onbekend}') " : "") +
            $" ORDER BY COALESCE(b.`Ontvangen`, b.`VerwerktOp`) DESC, b.`BevestigingID` DESC LIMIT {Math.Clamp(max, 1, 500)}", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(Lees(r));
        return lijst;
    }

    public async Task<int> AantalOpenAsync()
    {
        if (!await TabelAanwezigAsync()) return 0;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT COUNT(*) FROM `InkoopBevestigingen` WHERE `Uitkomst` IN ('{BevestigingUitkomst.GeenPo}', '{BevestigingUitkomst.Onbekend}')", cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}

/// <summary>Alleen om het overzicht lokaal te bekijken zonder de tabel (InkoopBevestigingen__Geheugen=true): een paar voorbeeldregels.</summary>
public class GeheugenInkoopBevestigingRepository : IInkoopBevestigingRepository
{
    private readonly List<InkoopBevestiging> _lijst = new()
    {
        new() { Id = 1, MailId = "a1", Ontvangen = DateTime.Now.AddHours(-2), Van = "Schur B.V. <verkoop@voorbeeld.nl>", Onderwerp = "RE: Order: PO-18544",
                Uitkomst = BevestigingUitkomst.Gekoppeld, Orders = "18544", OrderId = 18544, Bestanden = 1, VerwerktOp = DateTime.Now.AddHours(-2) },
        new() { Id = 2, MailId = "a2", Ontvangen = DateTime.Now.AddHours(-5), Van = "Omron Electronics <orders@voorbeeld.nl>", Onderwerp = "Orderbevestiging 4500087158",
                Uitkomst = BevestigingUitkomst.GeenPo, Melding = "Geen PO-nummer gevonden; 1 PDF: Orderbevestiging 4500087158.pdf", VerwerktOp = DateTime.Now.AddHours(-5) },
        new() { Id = 3, MailId = "a3", Ontvangen = DateTime.Now.AddDays(-1), Van = "Drabbe BV <info@voorbeeld.nl>", Onderwerp = "Bevestiging PO-99999",
                Uitkomst = BevestigingUitkomst.Onbekend, Orders = "99999", Melding = "PO-99999 bestaat niet; 1 PDF: bevestiging.pdf", VerwerktOp = DateTime.Now.AddDays(-1) },
        new() { Id = 4, MailId = "a4", Ontvangen = DateTime.Now.AddDays(-2), Van = "SMC Pneumatics <nl@voorbeeld.nl>", Onderwerp = "RE: Order: PO-18545",
                Uitkomst = BevestigingUitkomst.GeenBijlage, Orders = "18545", OrderId = 18545, Melding = "Het bericht heeft geen PDF.", VerwerktOp = DateTime.Now.AddDays(-2) },
    };

    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);
    public Task<HashSet<string>> BekendeMailsAsync(IReadOnlyList<string> mailIds) => Task.FromResult(_lijst.Select(b => b.MailId).Where(mailIds.Contains).ToHashSet());
    public Task<List<BevestigingOrder>> OrdersAsync(IReadOnlyList<int> orderIds)
        => Task.FromResult(orderIds.Where(id => id != 99999).Select(id => new BevestigingOrder(id, "Leverancier", DateTime.Today.AddDays(-5), 3)).ToList());
    public Task<int> LegVastAsync(InkoopBevestiging regel) { regel.Id = _lijst.Count + 1; _lijst.Add(regel); return Task.FromResult(regel.Id); }
    public Task<InkoopBevestiging?> GetAsync(int id) => Task.FromResult(_lijst.FirstOrDefault(b => b.Id == id));
    public Task WerkBijAsync(int id, string uitkomst, int? orderId, int bestanden, string? melding, int? door)
    {
        if (_lijst.FirstOrDefault(b => b.Id == id) is { } b)
        { b.Uitkomst = uitkomst; b.OrderId = orderId; b.Bestanden = bestanden; b.Melding = melding ?? ""; b.VerwerktOp = DateTime.Now; b.VerwerktDoor = "ik"; }
        return Task.CompletedTask;
    }
    public Task<List<InkoopBevestiging>> LijstAsync(bool alleenOpen, int max)
        => Task.FromResult(_lijst.Where(b => !alleenOpen || b.Open).OrderByDescending(b => b.Ontvangen).Take(max).ToList());
    public Task<int> AantalOpenAsync() => Task.FromResult(_lijst.Count(b => b.Open));
}
