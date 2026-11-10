using System.Collections.Concurrent;
using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

// ============================================================================================================
//  Verkooporders (VO), wens gebruiker 9-11-2026: simpele orders van een klant (Michiel neemt de bestelling op),
//  orderbevestiging, daarna (stap 2 en 3) bestellen bij de leverancier, pakbon en factuur.
//  Tabellen: VerkoopOrders, VerkoopOrderRegels, VerkoopOrderKoppelingen (VerkoopOrders_MariaDB.sql).
//  Elke order hangt aan een klant; inkoop gaat op de werkorder -01 van het project "Verkoop <jaar>" van die klant.
// ============================================================================================================

public static class VerkoopOrderStatus
{
    public const string Open = "open", Afgerond = "afgerond", Geannuleerd = "geannuleerd";
    public static readonly string[] Alle = { Open, Afgerond, Geannuleerd };
    public static string Kleur(string s) => s switch { Open => "s-blauw", Afgerond => "s-groen", _ => "s-grijs" };

    /// <summary>FileStore.StoreType van de verstuurde orderbevestiging (MasterID = VerkoopOrderID).</summary>
    public const int StoreTypeBevestiging = 350;

    /// <summary>VerkoopOrderKoppelingen.Soort: bestelregel (Orderitems), pakbonregel (PakbonItem), factuurregel (InvoiceHours).</summary>
    public const string SoortInkoop = "inkoop", SoortPakbon = "pakbon", SoortFactuur = "factuur";
}

public class VerkoopOrder
{
    public int Id { get; set; }
    public string Ordernummer { get; set; } = "";
    public int KlantId { get; set; }
    public string Klant { get; set; } = "";
    public int? ContactId { get; set; }
    public string Contact { get; set; } = "";
    public int BvId { get; set; }
    public int? WerkorderId { get; set; }
    /// <summary>Bv. 226041-01, met de omschrijving van het project.</summary>
    public string Werkorder { get; set; } = "";
    /// <summary>Het project van de werkorder ("Verkoop jjjj" van de klant), voor de factuur.</summary>
    public int? ProjectId { get; set; }
    /// <summary>"226095 · Verkoop 2026", zoals het projectveld op de factuur.</summary>
    public string Project { get; set; } = "";
    public string PoKlant { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public DateOnly? Leverdatum { get; set; }
    public bool Engels { get; set; }
    public bool BtwVerlegd { get; set; }
    public string Status { get; set; } = VerkoopOrderStatus.Open;
    public string Opmerking { get; set; } = "";
    public DateTime AangemaaktOp { get; set; }
    public string AangemaaktDoor { get; set; } = "";
    public DateTime? BevestigdOp { get; set; }
    public string BevestigdDoor { get; set; } = "";
    public string BevestigdAan { get; set; } = "";
    public List<VerkoopOrderRegel> Regels { get; set; } = new();

    /// <summary>Alleen in de lijst (zonder regels geladen).</summary>
    public decimal LijstTotaal { get; set; }
    public int LijstRegels { get; set; }

    public decimal Totaal => Regels.Count > 0 ? Regels.Sum(r => r.Totaal) : LijstTotaal;
    public decimal InkoopTotaal => Regels.Sum(r => r.Aantal * (r.Inkoopprijs ?? 0));
}

public class VerkoopOrderRegel
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int Volgorde { get; set; }
    public int? ArtikelId { get; set; }
    public string Code { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public decimal Aantal { get; set; } = 1;
    public decimal? Inkoopprijs { get; set; }
    public decimal? Verkoopprijs { get; set; }
    public int? LeverancierId { get; set; }
    public string Leverancier { get; set; } = "";

    /// <summary>Bestellingen bij de leverancier voor deze regel (stap 2), zonder geannuleerde of gewiste.</summary>
    public List<VerkoopInkoop> Inkoop { get; set; } = new();
    /// <summary>Pakbonregels voor deze regel (stap 3), zonder gewiste pakbonnen.</summary>
    public List<VerkoopKoppeling> Leveringen { get; set; } = new();
    /// <summary>Factuurregels voor deze regel (stap 3), zolang de factuur bestaat.</summary>
    public List<VerkoopKoppeling> Facturen { get; set; } = new();

    public decimal Totaal => Math.Round(Aantal * (Verkoopprijs ?? 0), 2);
    public decimal Besteld => Inkoop.Sum(i => i.Aantal);
    /// <summary>Nog te bestellen (nooit negatief).</summary>
    public decimal TeBestellen => Math.Max(0, Aantal - Besteld);
    /// <summary>Wat de leverancier(s) al bij MDE geleverd hebben.</summary>
    public decimal Binnen => Inkoop.Sum(i => Math.Min(i.Geleverd, i.Aantal));
    public decimal Geleverd => Leveringen.Sum(l => l.Aantal);
    public decimal TeLeveren => Math.Max(0, Aantal - Geleverd);
    public decimal Gefactureerd => Facturen.Sum(f => f.Aantal);
    /// <summary>Wat nog gefactureerd kan worden (tot het orderaantal).</summary>
    public decimal TeFactureren => Math.Max(0, Aantal - Gefactureerd);
    /// <summary>Een regel zonder artikel (bv. verzendkosten) gaat niet over een pakbon.</summary>
    public bool Levert => ArtikelId is not null;
    public bool HeeftKoppelingen => Inkoop.Count + Leveringen.Count + Facturen.Count > 0;
    /// <summary>Alles geleverd (of niets te leveren) en alles gefactureerd.</summary>
    public bool Klaar => (!Levert || Geleverd >= Aantal) && Gefactureerd >= Aantal;
}

/// <summary>
/// Een pakbonregel of factuurregel bij een verkooporderregel (VerkoopOrderKoppelingen, Soort "pakbon" of "factuur").
/// KopId = PakbonID of Invoice-id, Nummer = "PS-1705" of het factuurnummer; Aantal = wat nu op die regel staat.
/// </summary>
public record VerkoopKoppeling(int KoppelingId, int RegelId, string Soort, int DoelId, int KopId, string Nummer, decimal Aantal);

/// <summary>
/// Een bestelregel (Orderitems) die voor een verkooporderregel besteld is, via VerkoopOrderKoppelingen (Soort "inkoop").
/// Aantal = wat nu op de bestelregel staat (wijzigt iemand het in Bestellingen, dan telt dat), Geleverd = wat de
/// leverancier daarvan geleverd heeft (OrderItemsDelivered).
/// </summary>
public record VerkoopInkoop(int KoppelingId, int RegelId, int OrderitemId, int BestellingId, int Status, string Leverancier,
                            decimal Aantal, decimal Geleverd);

/// <summary>Filter van de lijst: status "open", "afgerond", "geannuleerd" of "alles"; zoek in nummer, klant, PO en omschrijving.</summary>
public record VerkoopOrderFilter(string Status, string Zoek, int? KlantId = null);

public interface IVerkoopOrderRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();
    Task<List<VerkoopOrder>> LijstAsync(VerkoopOrderFilter filter, int max = 300);
    Task<VerkoopOrder?> GetAsync(int id);

    /// <summary>
    /// Nieuwe order voor deze klant: volgend ordernummer (VO-jjjj-nnn) en de werkorder van het project "Verkoop jjjj" van de
    /// klant (maakt project en werkorder -01 aan als ze er nog niet zijn). Geeft het VerkoopOrderID.
    /// </summary>
    Task<int> NieuwAsync(VerkoopOrder kop, int? door);

    /// <summary>Kop bewaren (klant en nummer blijven zoals ze zijn).</summary>
    Task BewaarKopAsync(VerkoopOrder kop);
    Task<int> BewaarRegelAsync(VerkoopOrderRegel regel);
    Task WisRegelAsync(int regelId);

    /// <summary>Wissen kan alleen zolang er niets aan hangt (geen bevestiging, geen koppelingen). Geeft een reden als het niet mag.</summary>
    Task<string?> WisAsync(int id);

    Task ZetBevestigdAsync(int id, string aan, int? door);

    /// <summary>
    /// Een bestelregel, pakbonregel of factuurregel aan een verkooporderregel koppelen (VerkoopOrderStatus.Soort*).
    /// <paramref name="kopId"/> en <paramref name="nummer"/> gebruikt alleen de geheugenversie (de database zoekt ze op).
    /// </summary>
    Task KoppelAsync(int regelId, string soort, int doelId, decimal aantal, int? door, int kopId = 0, string nummer = "");

    /// <summary>Het gewone adres van de klant voor de orderbevestiging (niet het factuuradres).</summary>
    Task<FactuurAdres> KlantAdresAsync(int klantId);
    Task<FactuurAfzender> AfzenderAsync(int bvId);
}

public class GeenVerkoopOrderRepository : IVerkoopOrderRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<VerkoopOrder>> LijstAsync(VerkoopOrderFilter filter, int max = 300) => Task.FromResult(new List<VerkoopOrder>());
    public Task<VerkoopOrder?> GetAsync(int id) => Task.FromResult<VerkoopOrder?>(null);
    public Task<int> NieuwAsync(VerkoopOrder kop, int? door) => throw new InvalidOperationException("Er is geen database.");
    public Task BewaarKopAsync(VerkoopOrder kop) => throw new InvalidOperationException("Er is geen database.");
    public Task<int> BewaarRegelAsync(VerkoopOrderRegel regel) => throw new InvalidOperationException("Er is geen database.");
    public Task WisRegelAsync(int regelId) => Task.CompletedTask;
    public Task<string?> WisAsync(int id) => Task.FromResult<string?>("Er is geen database.");
    public Task ZetBevestigdAsync(int id, string aan, int? door) => Task.CompletedTask;
    public Task KoppelAsync(int regelId, string soort, int doelId, decimal aantal, int? door, int kopId = 0, string nummer = "")
        => throw new InvalidOperationException("Er is geen database.");
    public Task<FactuurAdres> KlantAdresAsync(int klantId) => Task.FromResult(new FactuurAdres());
    public Task<FactuurAfzender> AfzenderAsync(int bvId) => Task.FromResult(new FactuurAfzender());
}

/// <summary>
/// Alleen om lokaal te testen zonder de tabellen: Verkoop__Geheugen=true. Maakt geen project of werkorder aan (alleen een
/// nepwerkorder); klantadres en BV komen wel uit de database (alleen lezen) als die er is.
/// </summary>
public class GeheugenVerkoopOrderRepository : IVerkoopOrderRepository
{
    private readonly ConcurrentDictionary<int, VerkoopOrder> _orders = new();
    private readonly IVerkoopOrderRepository? _lezen;
    private int _volgende, _volgendeRegel;

    public GeheugenVerkoopOrderRepository(IVerkoopOrderRepository? lezen = null) => _lezen = lezen;

    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);

    public Task<List<VerkoopOrder>> LijstAsync(VerkoopOrderFilter filter, int max = 300)
        => Task.FromResult(_orders.Values
            .Where(o => filter.Status == "alles" || o.Status == filter.Status)
            .Where(o => filter.KlantId is null || o.KlantId == filter.KlantId)
            .Where(o => filter.Zoek.Trim().Length == 0
                        || $"{o.Ordernummer} {o.Klant} {o.PoKlant} {o.Omschrijving}".Contains(filter.Zoek.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.AangemaaktOp).Take(max)
            .Select(o => { o.LijstTotaal = o.Regels.Sum(r => r.Totaal); o.LijstRegels = o.Regels.Count; return o; })
            .ToList());

    public Task<VerkoopOrder?> GetAsync(int id) => Task.FromResult(_orders.TryGetValue(id, out var o) ? Kopie(o) : null);

    private static VerkoopOrder Kopie(VerkoopOrder o)
    {
        var k = new VerkoopOrder
        {
            Id = o.Id, Ordernummer = o.Ordernummer, KlantId = o.KlantId, Klant = o.Klant, ContactId = o.ContactId, Contact = o.Contact,
            BvId = o.BvId, WerkorderId = o.WerkorderId, Werkorder = o.Werkorder, PoKlant = o.PoKlant, Omschrijving = o.Omschrijving,
            Leverdatum = o.Leverdatum, Engels = o.Engels, BtwVerlegd = o.BtwVerlegd, Status = o.Status, Opmerking = o.Opmerking,
            AangemaaktOp = o.AangemaaktOp, AangemaaktDoor = o.AangemaaktDoor, BevestigdOp = o.BevestigdOp,
            BevestigdDoor = o.BevestigdDoor, BevestigdAan = o.BevestigdAan, ProjectId = o.ProjectId, Project = o.Project,
        };
        k.Regels = o.Regels.OrderBy(r => r.Volgorde).ThenBy(r => r.Id).Select(r => new VerkoopOrderRegel
        {
            Id = r.Id, OrderId = r.OrderId, Volgorde = r.Volgorde, ArtikelId = r.ArtikelId, Code = r.Code, Omschrijving = r.Omschrijving,
            Aantal = r.Aantal, Inkoopprijs = r.Inkoopprijs, Verkoopprijs = r.Verkoopprijs, LeverancierId = r.LeverancierId, Leverancier = r.Leverancier,
            Inkoop = r.Inkoop.ToList(), Leveringen = r.Leveringen.ToList(), Facturen = r.Facturen.ToList(),
        }).ToList();
        return k;
    }

    public Task<int> NieuwAsync(VerkoopOrder kop, int? door)
    {
        var id = Interlocked.Increment(ref _volgende);
        var o = Kopie(kop);
        o.Id = id;
        o.Ordernummer = $"VO-{DateTime.Today.Year}-{id:000}";
        o.WerkorderId = -1;
        o.Werkorder = $"(test) Verkoop {DateTime.Today.Year} van {kop.Klant}";
        o.AangemaaktOp = DateTime.Now;
        o.AangemaaktDoor = door is null ? "" : $"werknemer {door}";
        o.Regels = new();
        _orders[id] = o;
        return Task.FromResult(id);
    }

    public Task BewaarKopAsync(VerkoopOrder kop)
    {
        if (!_orders.TryGetValue(kop.Id, out var o)) throw new InvalidOperationException("Order bestaat niet meer.");
        o.ContactId = kop.ContactId; o.Contact = kop.Contact; o.BvId = kop.BvId; o.PoKlant = kop.PoKlant;
        o.Omschrijving = kop.Omschrijving; o.Leverdatum = kop.Leverdatum; o.Engels = kop.Engels; o.BtwVerlegd = kop.BtwVerlegd;
        o.Status = kop.Status; o.Opmerking = kop.Opmerking;
        return Task.CompletedTask;
    }

    public Task<int> BewaarRegelAsync(VerkoopOrderRegel regel)
    {
        if (!_orders.TryGetValue(regel.OrderId, out var o)) throw new InvalidOperationException("Order bestaat niet meer.");
        lock (o)
        {
            if (regel.Id <= 0)
            {
                regel.Id = Interlocked.Increment(ref _volgendeRegel);
                o.Regels.Add(regel);
            }
            else
            {
                var i = o.Regels.FindIndex(r => r.Id == regel.Id);
                if (i >= 0) o.Regels[i] = regel; else o.Regels.Add(regel);
            }
        }
        return Task.FromResult(regel.Id);
    }

    public Task WisRegelAsync(int regelId)
    {
        foreach (var o in _orders.Values) lock (o) o.Regels.RemoveAll(r => r.Id == regelId);
        return Task.CompletedTask;
    }

    public Task<string?> WisAsync(int id)
    {
        if (_orders.TryGetValue(id, out var o) && o.BevestigdOp is not null) return Task.FromResult<string?>("De orderbevestiging is al verstuurd; zet de order op geannuleerd.");
        _orders.TryRemove(id, out _);
        return Task.FromResult<string?>(null);
    }

    public Task ZetBevestigdAsync(int id, string aan, int? door)
    {
        if (_orders.TryGetValue(id, out var o)) { o.BevestigdOp = DateTime.Now; o.BevestigdAan = aan; o.BevestigdDoor = door is null ? "" : $"werknemer {door}"; }
        return Task.CompletedTask;
    }

    public Task KoppelAsync(int regelId, string soort, int doelId, decimal aantal, int? door, int kopId = 0, string nummer = "")
    {
        foreach (var o in _orders.Values)
            lock (o)
                if (o.Regels.FirstOrDefault(r => r.Id == regelId) is { } r)
                {
                    var k = new VerkoopKoppeling(r.Inkoop.Count + r.Leveringen.Count + r.Facturen.Count + 1, regelId, soort, doelId, kopId, nummer, aantal);
                    if (soort == VerkoopOrderStatus.SoortInkoop)
                        r.Inkoop.Add(new VerkoopInkoop(k.KoppelingId, regelId, doelId, kopId, 3, r.Leverancier, aantal, 0));
                    else if (soort == VerkoopOrderStatus.SoortPakbon) r.Leveringen.Add(k);
                    else r.Facturen.Add(k);
                }
        return Task.CompletedTask;
    }

    public async Task<FactuurAdres> KlantAdresAsync(int klantId)
        => _lezen is not null ? await _lezen.KlantAdresAsync(klantId) : new FactuurAdres { Naam = "Testklant", Adres = "Straat 1", Postcode = "1234 AB", Plaats = "Plaats", Landnummer = 31 };

    public async Task<FactuurAfzender> AfzenderAsync(int bvId)
        => _lezen is not null ? await _lezen.AfzenderAsync(bvId) : new FactuurAfzender { Id = bvId, Naam = "MDE (test)" };
}

public class MySqlVerkoopOrderRepository : IVerkoopOrderRepository
{
    private readonly string _cs;
    private readonly IProjectRepository _projecten;
    private bool _tabel;

    public MySqlVerkoopOrderRepository(string connectionString, IProjectRepository projecten)
    {
        _cs = connectionString;
        _projecten = projecten;
    }

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : (r.GetValue(i).ToString() ?? "").Trim();
    private static int? IN(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static decimal? DN(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
    private static bool B(MySqlDataReader r, int i) => !r.IsDBNull(i) && Convert.ToInt64(r.GetValue(i)) != 0;
    private static string Kort(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();

    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() " +
                "AND TABLE_NAME IN ('VerkoopOrders', 'VerkoopOrderRegels')", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 2;
        }
        catch { }
        return _tabel;
    }

    private const string Kop =
        "SELECT o.`VerkoopOrderID`, o.`Ordernummer`, o.`KlantID`, COALESCE(k.`name`, ''), o.`ContactID`, " +
        "       o.`BvID`, o.`WerkorderID`, " +
        "       CASE WHEN w.`Werkbon-id` IS NULL THEN '' ELSE CONCAT(COALESCE(p.`Projectnumber`, ''), '-', LPAD(COALESCE(w.`Werkbonnummer`, 0), 2, '0')) END, " +
        "       COALESCE(o.`PoKlant`, ''), COALESCE(o.`Omschrijving`, ''), o.`Leverdatum`, o.`Engels`, o.`BtwVerlegd`, o.`Status`, " +
        "       COALESCE(o.`Opmerking`, ''), o.`AangemaaktOp`, COALESCE(a.`NaamVoluit`, ''), o.`BevestigdOp`, COALESCE(b.`NaamVoluit`, ''), " +
        "       COALESCE(o.`BevestigdAan`, ''), p.`Project-Id`, " +
        "       CONCAT_WS(' · ', NULLIF(TRIM(p.`Projectnumber`), ''), NULLIF(TRIM(p.`Projectdescription`), '')) " +
        "  FROM `VerkoopOrders` o " +
        "  LEFT JOIN `Relations` k ON k.`id_supplier` = o.`KlantID` " +
        "  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = o.`WerkorderID` " +
        "  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
        "  LEFT JOIN `Werknemers` a ON a.`Werknemer-id` = o.`AangemaaktDoor` " +
        "  LEFT JOIN `Werknemers` b ON b.`Werknemer-id` = o.`BevestigdDoor` ";

    private static VerkoopOrder LeesKop(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0), Ordernummer = S(r, 1), KlantId = r.GetInt32(2), Klant = S(r, 3), ContactId = IN(r, 4),
        BvId = r.GetInt32(5), WerkorderId = IN(r, 6), Werkorder = S(r, 7), PoKlant = S(r, 8), Omschrijving = S(r, 9),
        Leverdatum = r.IsDBNull(10) ? null : DateOnly.FromDateTime(r.GetDateTime(10)), Engels = B(r, 11), BtwVerlegd = B(r, 12),
        Status = S(r, 13), Opmerking = S(r, 14), AangemaaktOp = r.GetDateTime(15), AangemaaktDoor = S(r, 16),
        BevestigdOp = r.IsDBNull(17) ? null : r.GetDateTime(17), BevestigdDoor = S(r, 18), BevestigdAan = S(r, 19),
        ProjectId = IN(r, 20), Project = S(r, 21),
    };

    public async Task<List<VerkoopOrder>> LijstAsync(VerkoopOrderFilter filter, int max = 300)
    {
        var waar = new List<string>();
        if (filter.Status != "alles") waar.Add("o.`Status` = @status");
        if (filter.KlantId is not null) waar.Add("o.`KlantID` = @klant");
        if (filter.Zoek.Trim().Length > 0)
            waar.Add("CONCAT_WS(' ', o.`Ordernummer`, k.`name`, o.`PoKlant`, o.`Omschrijving`) LIKE @zoek");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            Kop +
            (waar.Count > 0 ? " WHERE " + string.Join(" AND ", waar) : "") +
            " ORDER BY o.`AangemaaktOp` DESC, o.`VerkoopOrderID` DESC LIMIT @max", cn);
        cmd.Parameters.AddWithValue("@status", filter.Status);
        cmd.Parameters.AddWithValue("@klant", filter.KlantId ?? 0);
        cmd.Parameters.AddWithValue("@zoek", "%" + filter.Zoek.Trim() + "%");
        cmd.Parameters.AddWithValue("@max", max);
        var lijst = new List<VerkoopOrder>();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) lijst.Add(LeesKop(r));
        if (lijst.Count == 0) return lijst;

        // totalen per order in één keer
        await using var tot = new MySqlCommand(
            "SELECT `VerkoopOrderID`, COUNT(*), SUM(ROUND(`Aantal` * COALESCE(`Verkoopprijs`, 0), 2)) FROM `VerkoopOrderRegels` " +
            $" WHERE `VerkoopOrderID` IN ({string.Join(",", lijst.Select(o => o.Id))}) GROUP BY `VerkoopOrderID`", cn);
        var perId = lijst.ToDictionary(o => o.Id);
        await using (var r = await tot.ExecuteReaderAsync())
            while (await r.ReadAsync())
                if (perId.TryGetValue(r.GetInt32(0), out var o)) { o.LijstRegels = Convert.ToInt32(r.GetValue(1)); o.LijstTotaal = DN(r, 2) ?? 0; }
        return lijst;
    }

    public async Task<VerkoopOrder?> GetAsync(int id)
    {
        await using var cn = await OpenAsync();
        VerkoopOrder? o = null;
        await using (var cmd = new MySqlCommand(Kop + " WHERE o.`VerkoopOrderID` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) o = LeesKop(r);
        }
        if (o is null) return null;

        await using (var cmd = new MySqlCommand(
            "SELECT g.`RegelID`, g.`VerkoopOrderID`, g.`Volgorde`, g.`ArtikelID`, COALESCE(g.`Code`, ''), COALESCE(g.`Omschrijving`, ''), " +
            "       g.`Aantal`, g.`Inkoopprijs`, g.`Verkoopprijs`, g.`LeverancierID`, COALESCE(l.`name`, '') " +
            "  FROM `VerkoopOrderRegels` g LEFT JOIN `Relations` l ON l.`id_supplier` = g.`LeverancierID` " +
            " WHERE g.`VerkoopOrderID` = @id ORDER BY g.`Volgorde`, g.`RegelID`", cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                o.Regels.Add(new VerkoopOrderRegel
                {
                    Id = r.GetInt32(0), OrderId = r.GetInt32(1), Volgorde = r.GetInt32(2), ArtikelId = IN(r, 3), Code = S(r, 4),
                    Omschrijving = S(r, 5), Aantal = DN(r, 6) ?? 0, Inkoopprijs = DN(r, 7), Verkoopprijs = DN(r, 8),
                    LeverancierId = IN(r, 9), Leverancier = S(r, 10),
                });
        }

        // Inkoop per regel: alleen bestelregels die nog bestaan, in een bestelling die niet geannuleerd is.
        if (o.Regels.Count > 0)
        {
            try
            {
                await using var cmd = new MySqlCommand(
                    "SELECT k.`KoppelingID`, k.`RegelID`, i.`Orderitem-id`, i.`Order-id`, COALESCE(b.`Status`, 0), COALESCE(l.`name`, ''), " +
                    "       COALESCE(i.`Quantity ordered`, k.`Aantal`), " +
                    "       COALESCE((SELECT SUM(d.`Quantity delivered`) FROM `OrderItemsDelivered` d WHERE d.`OrderItem-id` = i.`Orderitem-id`), 0) " +
                    "  FROM `VerkoopOrderKoppelingen` k " +
                    "  JOIN `VerkoopOrderRegels` g ON g.`RegelID` = k.`RegelID` " +
                    "  JOIN `Orderitems` i ON i.`Orderitem-id` = k.`DoelID` AND (i.`ToBeDeleted` = 0 OR i.`ToBeDeleted` IS NULL) " +
                    "  JOIN `Orders` b ON b.`Order-id` = i.`Order-id` " +
                    "  LEFT JOIN `Relations` l ON l.`id_supplier` = b.`Supplier` " +
                    " WHERE g.`VerkoopOrderID` = @id AND k.`Soort` = 'inkoop' AND COALESCE(b.`Status`, 0) <> @geannuleerd " +
                    " ORDER BY k.`KoppelingID`", cn);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@geannuleerd", BestelVelden.StatusGeannuleerd);
                var perRegel = o.Regels.ToDictionary(x => x.Id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    if (perRegel.TryGetValue(r.GetInt32(1), out var regel))
                        regel.Inkoop.Add(new VerkoopInkoop(r.GetInt32(0), regel.Id, r.GetInt32(2), r.GetInt32(3), Convert.ToInt32(r.GetValue(4)),
                                                           S(r, 5), DN(r, 6) ?? 0, DN(r, 7) ?? 0));
            }
            catch (MySqlException) { /* koppeltabel ontbreekt nog: geen inkoop tonen */ }

            // Leveringen (pakbonregels, pakbon niet gewist) en facturen (factuurregels die nog bestaan).
            try
            {
                await using var cmd = new MySqlCommand(
                    "SELECT k.`KoppelingID`, k.`RegelID`, k.`Soort`, k.`DoelID`, pi.`PakbonID`, CONCAT('PS-', pi.`PakbonID`), COALESCE(pi.`Aantal`, 0) " +
                    "  FROM `VerkoopOrderKoppelingen` k " +
                    "  JOIN `VerkoopOrderRegels` g ON g.`RegelID` = k.`RegelID` " +
                    "  JOIN `PakbonItem` pi ON pi.`PakbonItemID` = k.`DoelID` " +
                    "  JOIN `Pakbonnen` pb ON pb.`PakbonID` = pi.`PakbonID` AND COALESCE(pb.`Deleted`, 0) = 0 " +
                    " WHERE g.`VerkoopOrderID` = @id AND k.`Soort` = 'pakbon' " +
                    "UNION ALL " +
                    "SELECT k.`KoppelingID`, k.`RegelID`, k.`Soort`, k.`DoelID`, h.`InvoiceID`, COALESCE(f.`Invoicenumber`, ''), COALESCE(h.`Quantity`, 0) " +
                    "  FROM `VerkoopOrderKoppelingen` k " +
                    "  JOIN `VerkoopOrderRegels` g ON g.`RegelID` = k.`RegelID` " +
                    "  JOIN `InvoiceHours` h ON h.`InvoiceHoursID` = k.`DoelID` " +
                    "  JOIN `Invoices` f ON f.`Invoice-id` = h.`InvoiceID` " +
                    " WHERE g.`VerkoopOrderID` = @id AND k.`Soort` = 'factuur' " +
                    "ORDER BY 1", cn);
                cmd.Parameters.AddWithValue("@id", id);
                var perRegel = o.Regels.ToDictionary(x => x.Id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    if (!perRegel.TryGetValue(r.GetInt32(1), out var regel)) continue;
                    var k = new VerkoopKoppeling(r.GetInt32(0), regel.Id, S(r, 2), r.GetInt32(3), r.GetInt32(4), S(r, 5), DN(r, 6) ?? 0);
                    (k.Soort == VerkoopOrderStatus.SoortPakbon ? regel.Leveringen : regel.Facturen).Add(k);
                }
            }
            catch (MySqlException) { }
        }

        if (o.ContactId is int c)
        {
            await using var cmd = new MySqlCommand("SELECT `Naam` FROM `Contacts` WHERE `ID_contact` = @c", cn);
            cmd.Parameters.AddWithValue("@c", c);
            try { o.Contact = (await cmd.ExecuteScalarAsync())?.ToString()?.Trim() ?? ""; } catch { }
        }
        return o;
    }

    public async Task<int> NieuwAsync(VerkoopOrder kop, int? door)
    {
        var jaar = DateTime.Today.Year;
        var werkorder = await VerkoopWerkorderAsync(kop.KlantId, jaar, kop.BvId, door);

        await using var cn = await OpenAsync();
        // Ordernummer: hoogste van dit jaar + 1; bij een botsing (twee tegelijk) nog een keer.
        for (var poging = 0; ; poging++)
        {
            int volgend;
            await using (var max = new MySqlCommand(
                "SELECT MAX(CAST(SUBSTRING(`Ordernummer`, 9) AS UNSIGNED)) FROM `VerkoopOrders` WHERE `Ordernummer` LIKE @pre", cn))
            {
                max.Parameters.AddWithValue("@pre", $"VO-{jaar}-%");
                var res = await max.ExecuteScalarAsync();
                volgend = res is null or DBNull ? 1 : Convert.ToInt32(res) + 1;
            }
            try
            {
                await using var cmd = new MySqlCommand(
                    "INSERT INTO `VerkoopOrders` (`Ordernummer`, `KlantID`, `ContactID`, `BvID`, `WerkorderID`, `PoKlant`, `Omschrijving`, " +
                    "  `Leverdatum`, `Engels`, `BtwVerlegd`, `Status`, `Opmerking`, `AangemaaktOp`, `AangemaaktDoor`) " +
                    "VALUES (@nr, @klant, @contact, @bv, @wo, @po, @oms, @lever, @en, @verlegd, 'open', @opm, NOW(), @door); " +
                    "SELECT LAST_INSERT_ID();", cn);
                cmd.Parameters.AddWithValue("@nr", $"VO-{jaar}-{volgend:000}");
                cmd.Parameters.AddWithValue("@klant", kop.KlantId);
                cmd.Parameters.AddWithValue("@contact", (object?)kop.ContactId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@bv", kop.BvId);
                cmd.Parameters.AddWithValue("@wo", (object?)werkorder ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@po", Kort(kop.PoKlant, 100));
                cmd.Parameters.AddWithValue("@oms", Kort(kop.Omschrijving, 300));
                cmd.Parameters.AddWithValue("@lever", kop.Leverdatum is DateOnly d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
                cmd.Parameters.AddWithValue("@en", kop.Engels ? 1 : 0);
                cmd.Parameters.AddWithValue("@verlegd", kop.BtwVerlegd ? 1 : 0);
                cmd.Parameters.AddWithValue("@opm", Kort(kop.Opmerking, 2000));
                cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
                return Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
            catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry && poging < 3) { }
        }
    }

    /// <summary>
    /// De werkorder voor verkooporders van deze klant in dit jaar: project "Verkoop jjjj" met deze klant, werkorder -01
    /// (de laagste). Ontbreekt het project of de werkorder, dan worden ze aangemaakt (zoals Projecten ▸ Nieuw).
    /// </summary>
    /// <summary>
    /// Per klant én per BV een project (besluit gebruiker 10-11-2026: de BV van een bestelling volgt het project): voor
    /// MDE Automation "Verkoop jjjj", voor een andere BV "Verkoop jjjj AFK" (bv. MPR), met Werkmaatschappij = die BV.
    /// Een bestaand project zonder Werkmaatschappij telt als MDE Automation (zo zijn de eerste aangemaakt).
    /// </summary>
    private async Task<int?> VerkoopWerkorderAsync(int klantId, int jaar, int bvId, int? door)
    {
        const int Standaard = 5254;     // MDE Automation
        var naam = $"Verkoop {jaar}";
        int? projectId = null, werkorderId = null;
        await using (var cn = await OpenAsync())
        {
            if (bvId != Standaard)
            {
                await using var afk = new MySqlCommand(
                    "SELECT COALESCE(NULLIF(TRIM(`Afkorting`), ''), `name`) FROM `Relations` WHERE `id_supplier` = @bv", cn);
                afk.Parameters.AddWithValue("@bv", bvId);
                if (await afk.ExecuteScalarAsync() is string a && a.Trim().Length > 0) naam += " " + a.Trim();
            }
            await using var cmd = new MySqlCommand(
                "SELECT p.`Project-Id`, (SELECT w.`Werkbon-id` FROM `Workorders` w WHERE w.`Project-id` = p.`Project-Id` " +
                "                         ORDER BY w.`Werkbonnummer`, w.`Werkbon-id` LIMIT 1) " +
                "  FROM `Projects` p WHERE p.`Customer-id` = @k AND p.`Projectdescription` = @naam " +
                "   AND COALESCE(NULLIF(p.`Werkmaatschappij`, 0), @std) = @bv " +
                " ORDER BY p.`Project-Id` LIMIT 1", cn);
            cmd.Parameters.AddWithValue("@k", klantId);
            cmd.Parameters.AddWithValue("@naam", naam);
            cmd.Parameters.AddWithValue("@std", Standaard);
            cmd.Parameters.AddWithValue("@bv", bvId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) { projectId = r.GetInt32(0); werkorderId = IN(r, 1); }
        }
        if (werkorderId is not null) return werkorderId;
        if (projectId is null)
        {
            // het nieuwe project hoort bij de BV van de verkooporder
            projectId = await _projecten.NieuwProjectAsync(await _projecten.VolgendProjectnummerAsync(), naam, klantId, door, bvId);
        }
        if (projectId is not int pid || pid <= 0) return null;
        var wo = await _projecten.NieuweWerkorderAsync(pid, naam, door);
        return wo > 0 ? wo : null;
    }

    public async Task BewaarKopAsync(VerkoopOrder kop)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `VerkoopOrders` SET `ContactID` = @contact, `BvID` = @bv, `PoKlant` = @po, `Omschrijving` = @oms, " +
            "       `Leverdatum` = @lever, `Engels` = @en, `BtwVerlegd` = @verlegd, `Status` = @status, `Opmerking` = @opm, " +
            "       `GewijzigdOp` = NOW() WHERE `VerkoopOrderID` = @id", cn);
        cmd.Parameters.AddWithValue("@contact", (object?)kop.ContactId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bv", kop.BvId);
        cmd.Parameters.AddWithValue("@po", Kort(kop.PoKlant, 100));
        cmd.Parameters.AddWithValue("@oms", Kort(kop.Omschrijving, 300));
        cmd.Parameters.AddWithValue("@lever", kop.Leverdatum is DateOnly d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@en", kop.Engels ? 1 : 0);
        cmd.Parameters.AddWithValue("@verlegd", kop.BtwVerlegd ? 1 : 0);
        cmd.Parameters.AddWithValue("@status", VerkoopOrderStatus.Alle.Contains(kop.Status) ? kop.Status : VerkoopOrderStatus.Open);
        cmd.Parameters.AddWithValue("@opm", Kort(kop.Opmerking, 2000));
        cmd.Parameters.AddWithValue("@id", kop.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> BewaarRegelAsync(VerkoopOrderRegel g)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(g.Id <= 0
            ? "INSERT INTO `VerkoopOrderRegels` (`VerkoopOrderID`, `Volgorde`, `ArtikelID`, `Code`, `Omschrijving`, `Aantal`, " +
              "  `Inkoopprijs`, `Verkoopprijs`, `LeverancierID`) " +
              "VALUES (@order, @volg, @art, @code, @oms, @aantal, @inkoop, @verkoop, @lev); SELECT LAST_INSERT_ID();"
            : "UPDATE `VerkoopOrderRegels` SET `Volgorde` = @volg, `ArtikelID` = @art, `Code` = @code, `Omschrijving` = @oms, " +
              "       `Aantal` = @aantal, `Inkoopprijs` = @inkoop, `Verkoopprijs` = @verkoop, `LeverancierID` = @lev " +
              " WHERE `RegelID` = @id; SELECT @id;", cn);
        cmd.Parameters.AddWithValue("@order", g.OrderId);
        cmd.Parameters.AddWithValue("@volg", g.Volgorde);
        cmd.Parameters.AddWithValue("@art", (object?)g.ArtikelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@code", Kort(g.Code, 100));
        cmd.Parameters.AddWithValue("@oms", Kort(g.Omschrijving, 400));
        cmd.Parameters.AddWithValue("@aantal", g.Aantal);
        cmd.Parameters.AddWithValue("@inkoop", (object?)g.Inkoopprijs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@verkoop", (object?)g.Verkoopprijs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lev", (object?)g.LeverancierId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", g.Id);
        g.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return g.Id;
    }

    public async Task WisRegelAsync(int regelId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `VerkoopOrderRegels` WHERE `RegelID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", regelId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> WisAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using (var cmd = new MySqlCommand(
            "SELECT o.`BevestigdOp` IS NOT NULL, " +
            "       (SELECT COUNT(*) FROM `VerkoopOrderKoppelingen` k JOIN `VerkoopOrderRegels` g ON g.`RegelID` = k.`RegelID` " +
            "         WHERE g.`VerkoopOrderID` = o.`VerkoopOrderID`) " +
            "  FROM `VerkoopOrders` o WHERE o.`VerkoopOrderID` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            if (B(r, 0)) return "De orderbevestiging is al verstuurd; zet de order op geannuleerd in plaats van wissen.";
            if (Convert.ToInt32(r.GetValue(1)) > 0) return "Er is al iets besteld, geleverd of gefactureerd; zet de order op geannuleerd.";
        }
        await using (var cmd = new MySqlCommand("DELETE FROM `VerkoopOrders` WHERE `VerkoopOrderID` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        return null;
    }

    public async Task ZetBevestigdAsync(int id, string aan, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `VerkoopOrders` SET `BevestigdOp` = NOW(), `BevestigdDoor` = @door, `BevestigdAan` = @aan WHERE `VerkoopOrderID` = @id", cn);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@aan", Kort(aan, 300));
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task KoppelAsync(int regelId, string soort, int doelId, decimal aantal, int? door, int kopId = 0, string nummer = "")
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `VerkoopOrderKoppelingen` (`RegelID`, `Soort`, `DoelID`, `Aantal`, `Op`, `Door`) " +
            "VALUES (@regel, @soort, @doel, @aantal, NOW(), @door)", cn);
        cmd.Parameters.AddWithValue("@regel", regelId);
        cmd.Parameters.AddWithValue("@soort", soort);
        cmd.Parameters.AddWithValue("@doel", doelId);
        cmd.Parameters.AddWithValue("@aantal", aantal);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<FactuurAdres> KlantAdresAsync(int klantId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT r.`name`, r.`address`, r.`PO_box`, r.`town`, COALESCE(c.`Country`, ''), COALESCE(c.`Landnummer`, 31), r.`VAT number` " +
            "  FROM `Relations` r LEFT JOIN `Country` c ON c.`Country-id` = r.`country-id` WHERE r.`id_supplier` = @k", cn);
        cmd.Parameters.AddWithValue("@k", klantId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return new FactuurAdres();
        return new FactuurAdres
        {
            Naam = S(r, 0), Adres = S(r, 1), Postcode = S(r, 2), Plaats = S(r, 3), Land = S(r, 4),
            Landnummer = r.IsDBNull(5) ? 31 : Convert.ToInt32(r.GetValue(5)), BtwNummer = S(r, 6),
        };
    }

    public async Task<FactuurAfzender> AfzenderAsync(int bvId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `id_supplier`, `Afkorting`, `name`, `address`, `PO_box`, `town`, `email`, " +
            "       `VAT number`, `KvK`, `IBAN`, `BIC` FROM `Relations` WHERE `id_supplier` = @bv", cn);
        cmd.Parameters.AddWithValue("@bv", bvId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return new FactuurAfzender { Id = bvId };
        return new FactuurAfzender
        {
            Id = r.GetInt32(0), Afkorting = S(r, 1), Naam = S(r, 2), Adres = S(r, 3), Postcode = S(r, 4), Plaats = S(r, 5),
            Email = S(r, 6), Btw = S(r, 7), Kvk = S(r, 8), Iban = S(r, 9), Bic = S(r, 10),
        };
    }
}
