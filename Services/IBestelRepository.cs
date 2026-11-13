using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

public interface IBestelRepository
{
    bool Beschikbaar { get; }

    Task<HashSet<string>> KolommenAsync();

    /// <summary>Bestellingen zoeken op nummer, leverancier, omschrijving of besteller.</summary>
    /// <param name="metGeannuleerd">false = geannuleerde bestellingen (Cancel) weglaten, tenzij op die status gefilterd wordt.</param>
    Task<List<BestelRegel>> ZoekAsync(string? zoek, int? status, int? leverancierId,
                                      int? besteldDoor, int max, bool metGeannuleerd = true, int? bvId = null);

    /// <summary>
    /// Zoeken door de bestelregels van alle bestellingen (artikelnummer, omschrijving, MDE-nummer, opmerking,
    /// leverancier, PO-nummer). Alle woorden moeten voorkomen; nieuwste bestelling eerst.
    /// </summary>
    Task<List<BestelregelTreffer>> ZoekRegelsAsync(string zoek, int max, bool metGeannuleerd = false);

    Task<Dictionary<string, object?>> GetVeldenAsync(int orderId);
    Task UpdateVeldAsync(int orderId, string kolom, object? waarde);

    /// <param name="bvId">De BV die bestelt; leeg = de BV van het project van de werkorder (anders MDE Automation).</param>
    Task<int> NieuweBestellingAsync(int? leverancierId, int? werkorderId, int? door, int? bvId = null);

    // ---- BV die bestelt (Orders.BvID, BestellingBv_MariaDB.sql) ----
    /// <summary>De BV van het project van deze werkorder (Projects.Werkmaatschappij); null = onbekend.</summary>
    Task<int?> ProjectBvAsync(int werkorderId);
    /// <summary>De eigen BV's (Werkmaatschappij, niet opgeheven), voor de naam en het filter; leeg = kolom BvID ontbreekt nog.</summary>
    Task<List<Keuze>> EigenBvsAsync();
    /// <summary>Mag deze BV bestellen (Relations.MagBestellen, BvMagBestellen_MariaDB.sql)? Zonder de kolom: ja.</summary>
    Task<bool> MagBestellenAsync(int bvId);
    /// <summary>De BV's die mogen bestellen (voor het filter); zonder de kolom alle eigen BV's.</summary>
    Task<List<Keuze>> BestelBvsAsync();
    /// <summary>Werkorders waarvan het project bij een BV hoort die niet mag bestellen (niet aanbieden bij bestellen).</summary>
    Task<HashSet<int>> WerkordersZonderBestelrechtAsync();
    /// <summary>Per open werkorder de BV van zijn project (zonder Werkmaatschappij: MDE Automation).</summary>
    Task<Dictionary<int, int>> WerkorderBvsAsync();

    /// <summary>
    /// Status verzetten. Bij een stap vooruit wordt het datum/wie-veld van de stap
    /// die je verlaat ingevuld, zoals Access dat ook doet.
    /// </summary>
    Task ZetStatusAsync(int orderId, int vanStatus, int naarStatus, int? door);

    Task<List<Bestelstatus>> GetStatussenAsync();

    // ---- Volgende en goedkeuren (Services/BestelFlow.cs) ----
    Task<BestelFlowInfo?> FlowInfoAsync(int orderId);
    Task<Goedkeurder?> GoedkeurderAsync(int werknemerId);
    /// <summary>Toeslagregel (minimum orderbedrag, next-day): 1 stuks, partnummer "-", Toeslag = 1.</summary>
    Task VoegToeslagToeAsync(int orderId, string omschrijving, decimal bedrag, string neededBy);
    /// <summary>Bestellingen die op deze werknemer wachten: Goedkeuring PM als projectverantwoordelijke, en bij
    /// <paramref name="directie"/> ook Goedkeuring Directie.</summary>
    Task<List<TeKeurenOrder>> TeKeurenAsync(int werknemerId, bool directie);

    /// <summary>De inkomende facturen die aan deze bestelling gekoppeld zijn (Facturen_Orders_Link), oudste eerst.
    /// Een factuur die twee keer aan dezelfde bestelling hangt staat er één keer in, met de bedragen opgeteld.</summary>
    Task<List<BestelFactuur>> FacturenAsync(int orderId);

    /// <summary>Bestellingen die langer dan <paramref name="dagen"/> dagen op Aanmaken (0) of In bestelling (3) staan:
    /// van één besteller (Created by), of van iedereen (null). Nieuwste eerst.</summary>
    Task<List<HangendeBestelling>> NietDoorgezetAsync(int dagen, int? bestellerId);
    /// <summary>Hoeveel dat er zijn voor één besteller (voor de tegel).</summary>
    Task<int> AantalNietDoorgezetAsync(int dagen, int bestellerId);
    Task ZetMeldingBijLeveringAsync(int orderId, int werknemerId);
    /// <summary>Een regel onder de interne opmerking (Orders.Remark) zetten, bv. de reden van afkeuren.</summary>
    Task VoegOpmerkingToeAsync(int orderId, string regel);

    // ---- mail naar de leverancier (Create_order_request_Mail / Create_order_Mail in Access) ----
    /// <param name="afzenderId">Wie verstuurt (ondertekening); een niet-persoon valt terug op de besteller.</param>
    /// <param name="eigenRelatieId">Relations-id van MDE zelf, voor de adresregel onder de mail.</param>
    Task<BestelMailGegevens?> BestelMailAsync(int orderId, int? afzenderId, int eigenRelatieId);
    /// <summary>De aanhef van een contactpersoon opslaan (zoals Access vroeg als die leeg was).</summary>
    Task ZetAanhefAsync(int contactId, string aanhef);
    /// <summary>Na versturen: Orders.MailAangemaakt = nu, en een regel in Log_Orderchanges.</summary>
    Task MailVerstuurdAsync(int orderId, int? door, string logtekst);

    // --- regels ---
    Task<List<Orderregel>> GetRegelsAsync(int orderId);
    /// <summary>
    /// Bestellingen om regels uit over te nemen (Importeren ▸ Uit andere bestelling): elk woord moet passen bij het PO-nummer,
    /// de leverancier, de omschrijving of de besteller ("Jansen Leon"), geannuleerde niet; nieuwste eerst.
    /// </summary>
    Task<List<BestelRegel>> ZoekVoorImportAsync(string? zoek, int max);
    Task<int> BewaarRegelAsync(Orderregel regel);
    /// <summary>
    /// Prijzen van bestelregels bijwerken uit een factuur (Bestellingen ▸ tab Facturen ▸ Prijzen bijwerken): per regel
    /// alleen stuksprijs, korting (breuk; null = geen) en het regeltotaal, in één transactie, met één regel in
    /// Log_Orderchanges (groep Prijs) waarin oud en nieuw staan. Geeft het aantal bijgewerkte regels.
    /// </summary>
    Task<int> PrijzenBijwerkenAsync(int orderId, IReadOnlyList<(int RegelId, decimal Prijs, decimal? Korting)> regels, int? door, string factuur);
    Task VerwijderRegelAsync(int orderitemId);

    // --- keuzelijsten ---
    Task<List<Keuze>> GetLeveranciersAsync();
    /// <summary>Leveranciers en werkorders van de laatste bestellingen van deze werknemer, nieuwste eerst (popup Nieuwe bestelling).</summary>
    Task<(List<int> Leveranciers, List<int> Werkorders)> RecentGebruiktAsync(int werknemerId, int max);

    // ---- standaardwaarden van de leverancier (FillInDefaults_Order in Access) ----
    Task<LeverancierStandaard?> LeverancierStandaardAsync(int leverancierId);
    /// <summary>Telefoon en mobiel van de leverancier en van zijn contactpersonen (alleen lezen).</summary>
    Task<LeverancierBereik?> LeverancierBereikAsync(int leverancierId);
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
    /// <summary>Relations.DefaultMaakdelen: opent het artikelzoekvenster bij deze leverancier eerst op Maakdelen?</summary>
    Task<bool> DefaultMaakdelenAsync(int? leverancierId);
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
    public Task<int?> ProjectBvAsync(int werkorderId) => Task.FromResult<int?>(null);
    public Task<List<Keuze>> EigenBvsAsync() => Task.FromResult(new List<Keuze>());
    public Task<bool> MagBestellenAsync(int bvId) => Task.FromResult(true);
    public Task<List<Keuze>> BestelBvsAsync() => Task.FromResult(new List<Keuze>());
    public Task<HashSet<int>> WerkordersZonderBestelrechtAsync() => Task.FromResult(new HashSet<int>());
    public Task<Dictionary<int, int>> WerkorderBvsAsync() => Task.FromResult(new Dictionary<int, int>());
    public Task<List<BestelRegel>> ZoekAsync(string? zoek, int? status, int? leverancierId,
                                             int? besteldDoor, int max, bool metGeannuleerd = true, int? bvId = null)
        => Task.FromResult(new List<BestelRegel>());
    public Task<List<BestelregelTreffer>> ZoekRegelsAsync(string zoek, int max, bool metGeannuleerd = false)
        => Task.FromResult(new List<BestelregelTreffer>());
    public Task<Dictionary<string, object?>> GetVeldenAsync(int orderId)
        => Task.FromResult(new Dictionary<string, object?>());
    public Task UpdateVeldAsync(int orderId, string kolom, object? waarde) => Task.CompletedTask;
    public Task<int> NieuweBestellingAsync(int? leverancierId, int? werkorderId, int? door, int? bvId = null) => Task.FromResult(0);
    public Task ZetStatusAsync(int orderId, int vanStatus, int naarStatus, int? door) => Task.CompletedTask;
    public Task<List<Bestelstatus>> GetStatussenAsync() => Task.FromResult(new List<Bestelstatus>());
    public Task<BestelFlowInfo?> FlowInfoAsync(int orderId) => Task.FromResult<BestelFlowInfo?>(null);
    public Task<Goedkeurder?> GoedkeurderAsync(int werknemerId) => Task.FromResult<Goedkeurder?>(null);
    public Task VoegToeslagToeAsync(int orderId, string omschrijving, decimal bedrag, string neededBy) => Task.CompletedTask;
    public Task<List<TeKeurenOrder>> TeKeurenAsync(int werknemerId, bool directie) => Task.FromResult(new List<TeKeurenOrder>());
    public Task<List<BestelFactuur>> FacturenAsync(int orderId) => Task.FromResult(new List<BestelFactuur>());
    public Task<List<HangendeBestelling>> NietDoorgezetAsync(int dagen, int? bestellerId) => Task.FromResult(new List<HangendeBestelling>());
    public Task<int> AantalNietDoorgezetAsync(int dagen, int bestellerId) => Task.FromResult(0);
    public Task ZetMeldingBijLeveringAsync(int orderId, int werknemerId) => Task.CompletedTask;
    public Task VoegOpmerkingToeAsync(int orderId, string regel) => Task.CompletedTask;
    public Task<BestelMailGegevens?> BestelMailAsync(int orderId, int? afzenderId, int eigenRelatieId) => Task.FromResult<BestelMailGegevens?>(null);
    public Task ZetAanhefAsync(int contactId, string aanhef) => Task.CompletedTask;
    public Task MailVerstuurdAsync(int orderId, int? door, string logtekst) => Task.CompletedTask;
    public Task<List<Orderregel>> GetRegelsAsync(int orderId) => Task.FromResult(new List<Orderregel>());
    public Task<List<BestelRegel>> ZoekVoorImportAsync(string? zoek, int max) => Task.FromResult(new List<BestelRegel>());
    public Task<int> BewaarRegelAsync(Orderregel regel) => Task.FromResult(0);
    public Task<int> PrijzenBijwerkenAsync(int orderId, IReadOnlyList<(int RegelId, decimal Prijs, decimal? Korting)> regels, int? door, string factuur)
        => Task.FromResult(0);
    public Task VerwijderRegelAsync(int orderitemId) => Task.CompletedTask;
    public Task<List<Keuze>> GetLeveranciersAsync() => Task.FromResult(new List<Keuze>());
    public Task<(List<int> Leveranciers, List<int> Werkorders)> RecentGebruiktAsync(int werknemerId, int max)
        => Task.FromResult((new List<int>(), new List<int>()));
    public Task<LeverancierStandaard?> LeverancierStandaardAsync(int leverancierId) => Task.FromResult<LeverancierStandaard?>(null);
    public Task<LeverancierBereik?> LeverancierBereikAsync(int leverancierId) => Task.FromResult<LeverancierBereik?>(null);
    public Task VulStandaardAsync(int orderId, LeverancierStandaard s, bool afleverenBijStandaard) => Task.CompletedTask;
    public Task<PlakArtikel?> ZoekPlakArtikelAsync(string nummer, int? leverancierId) => Task.FromResult<PlakArtikel?>(null);
    public Task<PlakLeverancier> PlakLeverancierAsync(int? leverancierId) => Task.FromResult(PlakLeverancier.Geen);
    public Task<bool> DefaultMaakdelenAsync(int? leverancierId) => Task.FromResult(false);
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
    public async Task<int?> ProjectBvAsync(int werkorderId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT NULLIF(p.`Werkmaatschappij`, 0) FROM `Workorders` w JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            " WHERE w.`Werkbon-id` = @w", cn);
        cmd.Parameters.AddWithValue("@w", werkorderId);
        var r = await cmd.ExecuteScalarAsync();
        return r is null or DBNull ? null : Convert.ToInt32(r);
    }

    private async Task<bool> MagBestellenKolomAsync(MySqlConnection cn)
    {
        await using var k = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Relations' AND COLUMN_NAME = 'MagBestellen'", cn);
        return Convert.ToInt32(await k.ExecuteScalarAsync()) > 0;
    }

    public async Task<List<Keuze>> BestelBvsAsync()
    {
        var alle = await EigenBvsAsync();
        if (alle.Count == 0) return alle;
        await using var cn = await OpenAsync();
        if (!await MagBestellenKolomAsync(cn)) return alle;
        var mag = new HashSet<int>();
        await using var cmd = new MySqlCommand("SELECT `id_supplier` FROM `Relations` WHERE COALESCE(`MagBestellen`, 0) <> 0", cn);
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) mag.Add(r.GetInt32(0));
        return alle.Where(b => mag.Contains(b.Id)).ToList();
    }

    public async Task<HashSet<int>> WerkordersZonderBestelrechtAsync()
    {
        var ids = new HashSet<int>();
        await using var cn = await OpenAsync();
        if (!await MagBestellenKolomAsync(cn)) return ids;
        // een project zonder Werkmaatschappij telt als MDE Automation (5254), zoals bij een nieuwe bestelling
        await using var cmd = new MySqlCommand(
            "SELECT w.`Werkbon-id` FROM `Workorders` w JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = COALESCE(NULLIF(p.`Werkmaatschappij`, 0), 5254) " +
            " WHERE COALESCE(r.`MagBestellen`, 0) = 0", cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) ids.Add(rd.GetInt32(0));
        return ids;
    }

    public async Task<Dictionary<int, int>> WerkorderBvsAsync()
    {
        var map = new Dictionary<int, int>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT w.`Werkbon-id`, COALESCE(NULLIF(p.`Werkmaatschappij`, 0), 5254) FROM `Workorders` w " +
            "  JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` WHERE COALESCE(w.`WorkOrderStatus`, 0) <> 0", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) map[r.GetInt32(0)] = Convert.ToInt32(r.GetValue(1));
        return map;
    }

    public async Task<bool> MagBestellenAsync(int bvId)
    {
        await using var cn = await OpenAsync();
        if (!await MagBestellenKolomAsync(cn)) return true;
        await using var cmd = new MySqlCommand("SELECT COALESCE(`MagBestellen`, 0) FROM `Relations` WHERE `id_supplier` = @bv", cn);
        cmd.Parameters.AddWithValue("@bv", bvId);
        return await cmd.ExecuteScalarAsync() is { } v and not DBNull && Convert.ToInt64(v) != 0;
    }

    public async Task<List<Keuze>> EigenBvsAsync()
    {
        var lijst = new List<Keuze>();
        if (!(await KolommenAsync()).Contains("BvID")) return lijst;
        await using var cn = await OpenAsync();
        var opg = await BvOpgeheven.KolomAsync(cn);
        await using var cmd = new MySqlCommand(
            "SELECT `id_supplier`, CONCAT(COALESCE(NULLIF(TRIM(`Afkorting`), ''), `name`), ' — ', COALESCE(`name`, '')) FROM `Relations` " +
            $" WHERE COALESCE(`Werkmaatschappij`, 0) <> 0 AND NOT {BvOpgeheven.Is(opg)} ORDER BY `Afkorting`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new Keuze(r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1)));
        return lijst;
    }

    public async Task<List<BestelRegel>> ZoekVoorImportAsync(string? zoek, int max)
    {
        var woorden = (zoek ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(6).ToList();
        var delen = new List<string> { $"COALESCE(o.`Status`, 0) <> {BestelVelden.StatusGeannuleerd}" };
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("", cn);
        for (var i = 0; i < woorden.Count; i++)
        {
            var w = woorden[i];
            if (w.StartsWith("PO-", StringComparison.OrdinalIgnoreCase)) w = w[3..];
            delen.Add($"(CAST(o.`Order-id` AS CHAR) LIKE @w{i} OR o.`Description` LIKE @w{i} OR r.`name` LIKE @w{i} " +
                      $" OR wn.`NaamVoluit` LIKE @w{i} OR wn.`Afkorting` LIKE @w{i})");
            cmd.Parameters.AddWithValue("@w" + i, "%" + w + "%");
        }
        cmd.CommandText =
            "SELECT o.`Order-id`, COALESCE(r.`name`, ''), COALESCE(o.`Description`, ''), COALESCE(o.`Status`, 0), " +
            "       COALESCE(st.`Omschrijving`, ''), o.`Creation date`, " +
            "       COALESCE((SELECT SUM(i.`Totalprice`) FROM `Orderitems` i WHERE i.`Order-id` = o.`Order-id`), 0), " +
            "       COALESCE(wn.`NaamVoluit`, '') " +
            "  FROM `Orders` o " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            "  LEFT JOIN `OrderState` st ON st.`Id` = o.`Status` " +
            "  LEFT JOIN `Werknemers` wn ON wn.`Werknemer-id` = o.`Created by` " +
            $" WHERE {string.Join(" AND ", delen)} ORDER BY o.`Order-id` DESC LIMIT {Math.Clamp(max, 1, 200)}";
        var lijst = new List<BestelRegel>();
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var id = rd.GetInt32(0);
            lijst.Add(new BestelRegel(id, "PO-" + id, rd.GetString(1), rd.GetString(2), Convert.ToInt32(rd.GetValue(3)), rd.GetString(4),
                rd.IsDBNull(5) ? null : Convert.ToDateTime(rd.GetValue(5)), Convert.ToDecimal(rd.GetValue(6)), "", rd.GetString(7)));
        }
        return lijst;
    }

    public async Task<List<BestelRegel>> ZoekAsync(string? zoek, int? status, int? leverancierId,
                                                   int? besteldDoor, int max, bool metGeannuleerd = true, int? bvId = null)
    {
        var delen = new List<string>();
        var bvKolom = (await KolommenAsync()).Contains("BvID");
        if (bvId is not null && bvKolom) delen.Add("o.`BvID` = @bv");

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
        if (!metGeannuleerd && status != BestelVelden.StatusGeannuleerd)
            delen.Add($"COALESCE(o.`Status`, 0) <> {BestelVelden.StatusGeannuleerd}");

        var where = delen.Count > 0 ? "WHERE " + string.Join(" AND ", delen) : "";

        var lijst = new List<BestelRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT o.`Order-id`, COALESCE(r.`name`, ''), COALESCE(o.`Description`, ''), " +
            "       COALESCE(o.`Status`, 0), COALESCE(st.`Omschrijving`, ''), o.`Creation date`, " +
            "       COALESCE((SELECT SUM(i.`Totalprice`) FROM `Orderitems` i " +
            "                  WHERE i.`Order-id` = o.`Order-id`), 0), " +
            (bvKolom ? "COALESCE(NULLIF(TRIM(bv.`Afkorting`), ''), bv.`name`, '') " : "'' ") +
            "  FROM `Orders` o " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            "  LEFT JOIN `OrderState` st ON st.`Id` = o.`Status` " +
            (bvKolom ? "  LEFT JOIN `Relations` bv ON bv.`id_supplier` = o.`BvID` " : "") +
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
        if (bvId is not null) cmd.Parameters.AddWithValue("@bv", bvId.Value);

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
                rd.IsDBNull(6) ? 0 : Convert.ToDecimal(rd.GetValue(6)),
                rd.IsDBNull(7) ? "" : rd.GetValue(7).ToString() ?? ""));
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

    public async Task<int> NieuweBestellingAsync(int? leverancierId, int? werkorderId, int? door, int? bvId = null)
    {
        var kolommen = await KolommenAsync();
        var sets = new List<string> { "`GUID` = @guid", "`Status` = 0" };
        // de BV die bestelt: meegegeven, anders die van het project van de werkorder, anders MDE Automation
        int? bv = null;
        if (kolommen.Contains("BvID"))
        {
            bv = bvId ?? (werkorderId is int wid ? await ProjectBvAsync(wid) : null) ?? 5254;
            if (!await MagBestellenAsync(bv.Value))
                throw new InvalidOperationException("Op een project van deze BV mag niet besteld worden (Relaties > tab BV > Mag bestellen).");
            sets.Add("`BvID` = @bv");
        }

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
        if (bv is not null) cmd.Parameters.AddWithValue("@bv", bv.Value);

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

    // ============================================================
    //  Volgende en goedkeuren
    // ============================================================
    private const string ProjectJoin =
        "  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = o.`Workorder-id` " +
        "  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` ";

    public async Task<BestelFlowInfo?> FlowInfoAsync(int orderId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(o.`Status`, 0), o.`Workorder-id`, o.`Supplier`, COALESCE(r.`name`, ''), o.`Created by`, " +
            "       p.`ProjectResponsible`, COALESCE(pm.`NaamVoluit`, ''), COALESCE(r.`Minimum order`, 0), " +
            "       COALESCE(r.`Orderkosten`, 0), COALESCE(r.`NextDayDeliveryFee`, 0), " +
            "       COALESCE(r.`DirectGeleverdBijBestellen`, 0), COALESCE(o.`Notify_when_delivered`, 0) " +
            "  FROM `Orders` o " + ProjectJoin +
            "  LEFT JOIN `Werknemers` pm ON pm.`Werknemer-id` = p.`ProjectResponsible` " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            " WHERE o.`Order-id` = @id", cn);
        cmd.Parameters.AddWithValue("@id", orderId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        int? N(int i) => r.IsDBNull(i) || Convert.ToInt64(r.GetValue(i)) == 0 ? null : Convert.ToInt32(r.GetValue(i));
        decimal D(int i) => Convert.ToDecimal(r.GetValue(i));
        bool B(int i) => Convert.ToInt64(r.GetValue(i)) != 0;
        return new BestelFlowInfo(Convert.ToInt32(r.GetValue(0)), N(1), N(2), r.GetValue(3).ToString() ?? "", N(4), N(5),
                                  r.GetValue(6).ToString()?.Trim() ?? "", D(7), D(8), D(9), B(10), B(11));
    }

    public async Task<Goedkeurder?> GoedkeurderAsync(int werknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(`NaamVoluit`, ''), COALESCE(`Afkorting`, ''), COALESCE(`GoedkeurTarief`, 0), COALESCE(`Directie`, 0) " +
            "  FROM `Werknemers` WHERE `Werknemer-id` = @id", cn);
        cmd.Parameters.AddWithValue("@id", werknemerId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new Goedkeurder(werknemerId, r.GetValue(0).ToString()?.Trim() ?? "", r.GetValue(1).ToString()?.Trim() ?? "",
                               Convert.ToDecimal(r.GetValue(2)), Convert.ToInt64(r.GetValue(3)) != 0);
    }

    public async Task VoegToeslagToeAsync(int orderId, string omschrijving, decimal bedrag, string neededBy)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `Orderitems` SET `Order-id` = @o, `Quantity ordered` = 1, `VE-id` = 2, `Description` = @d, " +
            "  `Partnumber` = '-', `ASAP` = 0, `Needed-by` = @nb, `Grossprice per VE` = @b, `Discount` = 0, " +
            "  `Totalprice` = @b, `Toeslag` = 1, `GUID` = @guid, `Timestamp` = NOW()", cn);
        cmd.Parameters.AddWithValue("@o", orderId);
        cmd.Parameters.AddWithValue("@d", omschrijving);
        cmd.Parameters.AddWithValue("@nb", neededBy);
        cmd.Parameters.AddWithValue("@b", bedrag);
        cmd.Parameters.AddWithValue("@guid", Guid.NewGuid().ToString("N"));
        await cmd.ExecuteNonQueryAsync();
    }

    // ---- gekoppelde facturen (Bestellingen ▸ tab Facturen) ----
    public async Task<List<BestelFactuur>> FacturenAsync(int orderId)
    {
        var lijst = new List<BestelFactuur>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT f.`FacturenInkomend-ID`, COALESCE(f.`FactuurNummer`, ''), f.`DatumFactuur`, COALESCE(f.`Omschrijving`, ''), " +
            "       COALESCE(SUM(l.`Bedrag`), 0), COALESCE(f.`BedragExBTW`, 0), COALESCE(f.`BTW`, 0), " +
            "       COALESCE(f.`Goedgekeurd`, 0), COALESCE(f.`Betaald`, 0) " +
            "  FROM `Facturen_Orders_Link` l " +
            "  JOIN `FacturenInkomend` f ON f.`FacturenInkomend-ID` = l.`FactuurID` " +
            " WHERE l.`OrderID` = @id " +
            " GROUP BY f.`FacturenInkomend-ID`, f.`FactuurNummer`, f.`DatumFactuur`, f.`Omschrijving`, f.`BedragExBTW`, f.`BTW`, " +
            "          f.`Goedgekeurd`, f.`Betaald` " +
            " ORDER BY f.`DatumFactuur`, f.`FacturenInkomend-ID`", cn);
        cmd.Parameters.AddWithValue("@id", orderId);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lijst.Add(new BestelFactuur(
                rd.GetInt32(0), rd.GetString(1), rd.IsDBNull(2) ? null : Convert.ToDateTime(rd.GetValue(2)), rd.GetString(3),
                Convert.ToDecimal(rd.GetValue(4)), Convert.ToDecimal(rd.GetValue(5)), Convert.ToDecimal(rd.GetValue(6)),
                Convert.ToInt64(rd.GetValue(7)) != 0, Convert.ToInt64(rd.GetValue(8)) != 0));
        return lijst;
    }

    // ---- niet doorgezet (Bestellingen ▸ Niet doorgezet) ----
    /// <summary>Sinds wanneer een bestelling in zijn status staat: Aanmaken sinds het aanmaken; In bestelling sinds de
    /// laatste stap ervoor (de datumvelden worden gezet bij het verlaten van een stap, zie BestelVelden.StatusVelden).</summary>
    private const string SindsSql = "CASE WHEN COALESCE(o.`Status`, 0) = 3 THEN COALESCE(o.`Approved2 date`, o.`Approved1 date`, o.`Request date`, o.`Creation date`) ELSE o.`Creation date` END";
    private const string NietDoorgezetWaar =
        " COALESCE(o.`Status`, 0) IN (0, 3) AND DATEDIFF(CURDATE(), " + SindsSql + ") > @dagen ";

    public async Task<List<HangendeBestelling>> NietDoorgezetAsync(int dagen, int? bestellerId)
    {
        var gemaild = (await KolommenAsync()).Contains("MailAangemaakt") ? "o.`MailAangemaakt` IS NOT NULL" : "0";
        var lijst = new List<HangendeBestelling>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT o.`Order-id`, COALESCE(r.`name`, ''), COALESCE(o.`Description`, ''), COALESCE(o.`Status`, 0), " +
            "       COALESCE(st.`Omschrijving`, ''), o.`Creation date`, " + SindsSql + ", " +
            "       DATEDIFF(CURDATE(), " + SindsSql + "), " +
            "       COALESCE((SELECT SUM(i.`Totalprice`) FROM `Orderitems` i WHERE i.`Order-id` = o.`Order-id`), 0), " +
            "       o.`Created by`, COALESCE(wn.`NaamVoluit`, ''), " +
            "       (SELECT COUNT(*) FROM `Orderitems` i WHERE i.`Order-id` = o.`Order-id`), " +
            "       (SELECT COUNT(*) FROM `Facturen_Orders_Link` l WHERE l.`OrderID` = o.`Order-id`), " + gemaild +
            "  FROM `Orders` o " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            "  LEFT JOIN `OrderState` st ON st.`Id` = o.`Status` " +
            "  LEFT JOIN `Werknemers` wn ON wn.`Werknemer-id` = o.`Created by` " +
            " WHERE" + NietDoorgezetWaar + (bestellerId is not null ? " AND o.`Created by` = @wie " : "") +
            " ORDER BY o.`Order-id` DESC LIMIT 2000", cn);
        cmd.Parameters.AddWithValue("@dagen", dagen);
        if (bestellerId is not null) cmd.Parameters.AddWithValue("@wie", bestellerId.Value);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lijst.Add(new HangendeBestelling(
                rd.GetInt32(0), rd.GetString(1), rd.GetString(2), Convert.ToInt32(rd.GetValue(3)), rd.GetString(4),
                rd.IsDBNull(5) ? null : Convert.ToDateTime(rd.GetValue(5)),
                rd.IsDBNull(6) ? null : Convert.ToDateTime(rd.GetValue(6)),
                rd.IsDBNull(7) ? 0 : Convert.ToInt32(rd.GetValue(7)),
                Convert.ToDecimal(rd.GetValue(8)),
                rd.IsDBNull(9) ? null : Convert.ToInt32(rd.GetValue(9)), rd.GetString(10),
                Convert.ToInt32(rd.GetValue(11)), Convert.ToInt32(rd.GetValue(12)), Convert.ToInt32(rd.GetValue(13)) != 0));
        return lijst;
    }

    public async Task<int> AantalNietDoorgezetAsync(int dagen, int bestellerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM `Orders` o WHERE" + NietDoorgezetWaar + " AND o.`Created by` = @wie", cn);
        cmd.Parameters.AddWithValue("@dagen", dagen);
        cmd.Parameters.AddWithValue("@wie", bestellerId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<List<TeKeurenOrder>> TeKeurenAsync(int werknemerId, bool directie)
    {
        var lijst = new List<TeKeurenOrder>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT o.`Order-id`, o.`Status`, COALESCE(r.`name`, ''), COALESCE(o.`Description`, ''), " +
            "       COALESCE((SELECT SUM(i.`Totalprice`) FROM `Orderitems` i WHERE i.`Order-id` = o.`Order-id`), 0), " +
            "       COALESCE(b.`NaamVoluit`, ''), " +
            "       COALESCE(CONCAT(p.`Projectnumber`, '-', RIGHT(CONCAT('00', w.`Werkbonnummer`), 2)), ''), " +
            "       COALESCE(p.`Projectdescription`, ''), COALESCE(o.`Approved1 date`, o.`Request date`) " +
            "  FROM `Orders` o " + ProjectJoin +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            "  LEFT JOIN `Werknemers` b ON b.`Werknemer-id` = o.`Created by` " +
            " WHERE (o.`Status` = 1 AND p.`ProjectResponsible` = @wn) OR (@dir = 1 AND o.`Status` = 2) " +
            " ORDER BY o.`Order-id`", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        cmd.Parameters.AddWithValue("@dir", directie ? 1 : 0);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new TeKeurenOrder(r.GetInt32(0), Convert.ToInt32(r.GetValue(1)), r.GetValue(2).ToString() ?? "",
                r.GetValue(3).ToString() ?? "", Convert.ToDecimal(r.GetValue(4)), r.GetValue(5).ToString()?.Trim() ?? "",
                r.GetValue(6).ToString() ?? "", r.GetValue(7).ToString() ?? "", r.IsDBNull(8) ? null : Convert.ToDateTime(r.GetValue(8))));
        return lijst;
    }

    public async Task ZetMeldingBijLeveringAsync(int orderId, int werknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Orders` SET `Notify_when_delivered` = 1, `NotifyTo` = @wn WHERE `Order-id` = @id", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        cmd.Parameters.AddWithValue("@id", orderId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Mail naar de leverancier
    // ============================================================
    public async Task<BestelMailGegevens?> BestelMailAsync(int orderId, int? afzenderId, int eigenRelatieId)
    {
        await using var cn = await OpenAsync();
        string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : (r.GetValue(i).ToString() ?? "").Trim();
        int? N(MySqlDataReader r, int i) => r.IsDBNull(i) || Convert.ToInt64(r.GetValue(i)) == 0 ? null : Convert.ToInt32(r.GetValue(i));
        bool B(MySqlDataReader r, int i) => !r.IsDBNull(i) && Convert.ToInt64(r.GetValue(i)) != 0;

        // kop: bestelling, contactpersoon, leverancier, afleveradres
        int status = 0, taal = 0; int? contactId, levId, offerteContact, orderContact, afleverId, createdBy;
        string contactNaam, contactMail, aanhef, leverancier, ccAdres, opmerking, aflNaam, aflAdres, aflPostcode, aflPlaats, aflTav;
        bool oc, cc;
        await using (var cmd = new MySqlCommand(
            "SELECT o.`Status`, o.`Language`, o.`Contact`, COALESCE(c.`Naam`, ''), COALESCE(c.`mail`, ''), c.`Aanhef`, " +
            "       o.`Supplier`, COALESCE(r.`name`, ''), r.`GebruikOCcodes`, r.`OfferteContactID`, r.`OrderContact-id`, " +
            "       r.`CCbijBestelling`, COALESCE(r.`CCmailadressBijBestelling`, ''), COALESCE(o.`Purchasenote`, ''), " +
            "       o.`AfleverKlant-id`, COALESCE(a.`name`, ''), COALESCE(a.`address`, ''), COALESCE(a.`PO_box`, ''), COALESCE(a.`town`, ''), " +
            "       COALESCE(t.`Naam`, ''), o.`Created by` " +
            "  FROM `Orders` o " +
            "  LEFT JOIN `Contacts` c ON c.`ID_contact` = o.`Contact` " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            "  LEFT JOIN `Relations` a ON a.`id_supplier` = o.`AfleverKlant-id` " +
            "  LEFT JOIN `Contacts` t ON t.`ID_contact` = o.`AfleverTAV-id` " +
            " WHERE o.`Order-id` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", orderId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            status = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0));
            taal = r.IsDBNull(1) ? 1 : Convert.ToInt32(r.GetValue(1));       // Access: leeg = Engels (alleen 0 is Nederlands)
            contactId = N(r, 2); contactNaam = S(r, 3); contactMail = S(r, 4); aanhef = S(r, 5);
            levId = N(r, 6); leverancier = S(r, 7); oc = B(r, 8); offerteContact = N(r, 9); orderContact = N(r, 10);
            cc = B(r, 11); ccAdres = S(r, 12); opmerking = S(r, 13);
            afleverId = N(r, 14); aflNaam = S(r, 15); aflAdres = S(r, 16); aflPostcode = S(r, 17); aflPlaats = S(r, 18); aflTav = S(r, 19);
            createdBy = N(r, 20);
        }

        // regels (Order_items), met eenheid en OC-code van het artikel
        var regels = new List<BestelMailRegel>();
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(i.`Quantity ordered`, 0), COALESCE(ve.`Units`, ''), COALESCE(i.`Partnumber`, ''), " +
            "       COALESCE(ar.`OCcodeOmron`, ''), COALESCE(i.`Description`, ''), i.`Agreed date of delivery`, " +
            "       COALESCE(i.`Grossprice per VE`, 0), COALESCE(i.`Discount`, 0) " +
            "  FROM `Orderitems` i " +
            "  LEFT JOIN `VE` ve ON ve.`VE-ID` = i.`VE-id` " +
            "  LEFT JOIN `Artikelen` ar ON ar.`ArtikelID` = i.`MDE_ID` " +
            " WHERE i.`Order-id` = @id ORDER BY i.`Orderitem-id`", cn))
        {
            cmd.Parameters.AddWithValue("@id", orderId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                regels.Add(new BestelMailRegel(Convert.ToDouble(r.GetValue(0)), S(r, 1), S(r, 2), S(r, 3), S(r, 4),
                    r.IsDBNull(5) ? null : DateOnly.FromDateTime(Convert.ToDateTime(r.GetValue(5))),
                    Convert.ToDecimal(r.GetValue(6)), Convert.ToDecimal(r.GetValue(7))));
        }

        // bijlagen van de regels (StoreType 3); verouderd = bij het artikel staat een nieuwere versie (StoreType 2, zelfde
        // bestandsnaam, meer dan een minuut nieuwer). Access keek alleen naar de naam; hier ook naar het artikel.
        var bijlagen = new List<BestelMailBijlage>();
        await using (var cmd = new MySqlCommand(
            "SELECT f.`FileStoreID`, COALESCE(f.`FileOnly`, ''), COALESCE(i.`Partnumber`, ''), COALESCE(f.`FileSize`, 0), " +
            "       EXISTS (SELECT 1 FROM `FileStore` n WHERE n.`StoreType` = 2 AND n.`MasterID` = i.`MDE_ID` " +
            "               AND n.`FileOnly` = f.`FileOnly` AND n.`DatumToegevoegd` > f.`DatumToegevoegd` + INTERVAL 1 MINUTE) " +
            "  FROM `FileStore` f JOIN `Orderitems` i ON i.`Orderitem-id` = f.`MasterID` " +
            " WHERE f.`StoreType` = 3 AND i.`Order-id` = @id AND COALESCE(f.`ToBeDeleted`, 0) = 0 " +
            " ORDER BY i.`Orderitem-id`, f.`FileOnly`", cn))
        {
            cmd.Parameters.AddWithValue("@id", orderId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                bijlagen.Add(new BestelMailBijlage(r.GetInt32(0), S(r, 1), S(r, 2), Convert.ToInt64(r.GetValue(3)), B(r, 4)));
        }

        // ondertekening: wie verstuurt; een niet-persoon (werkplek) valt terug op de besteller
        var afzender = new BestelMailAfzender("", "", "");
        foreach (var id in new[] { afzenderId, createdBy }.OfType<int>().Distinct())
        {
            await using var cmd = new MySqlCommand(
                "SELECT COALESCE(`Persoon`, 0), COALESCE(`Voornaam`, ''), COALESCE(`Tussenvoegsel`, ''), COALESCE(`Achternaam`, ''), " +
                "       COALESCE(`Mobiel`, ''), COALESCE(`Mail`, '') FROM `Werknemers` WHERE `Werknemer-id` = @id", cn);
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync() || Convert.ToInt64(r.GetValue(0)) == 0) continue;
            var naam = string.Join(" ", new[] { S(r, 1), S(r, 2), S(r, 3) }.Where(x => x.Length > 0));
            afzender = new BestelMailAfzender(naam, S(r, 4), S(r, 5));
            break;
        }

        // de BV die bestelt (Orders.BvID); zonder kolom of leeg de vaste eigen BV (instelling, standaard MDE Automation)
        var bvId = eigenRelatieId;
        if ((await KolommenAsync()).Contains("BvID"))
            await using (var cmd = new MySqlCommand("SELECT `BvID` FROM `Orders` WHERE `Order-id` = @id", cn))
            {
                cmd.Parameters.AddWithValue("@id", orderId);
                if (await cmd.ExecuteScalarAsync() is { } v and not DBNull) bvId = Convert.ToInt32(v);
            }

        // adresregel van die BV onder de mail, en bij een andere BV dan de standaard het factuuradres
        var bedrijf = "MDE Automation B.V.";
        var bvNaam = "MDE Automation B.V.";
        var factuuradres = "";
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(`name`, ''), COALESCE(`address`, ''), COALESCE(`PO_box`, ''), COALESCE(`town`, ''), " +
            "       COALESCE(`telephone`, ''), COALESCE(`email`, ''), COALESCE(`VAT number`, '') FROM `Relations` WHERE `id_supplier` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", bvId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                bedrijf = string.Join(" · ", new[] { S(r, 0), S(r, 1), (S(r, 2) + " " + S(r, 3)).Trim(), S(r, 4), S(r, 5) }.Where(x => x.Length > 0));
                if (S(r, 0).Length > 0) bvNaam = S(r, 0);
                if (bvId != eigenRelatieId)
                    factuuradres = string.Join("\n", new[] { S(r, 0), S(r, 1), (S(r, 2) + " " + S(r, 3)).Trim(), S(r, 6).Length > 0 ? "Btw " + S(r, 6) : "" }
                        .Where(x => x.Length > 0));
            }
        }

        return new BestelMailGegevens(orderId, status, taal != 0, contactId, contactNaam, contactMail, aanhef,
            levId, leverancier, oc, offerteContact, orderContact, cc, ccAdres, opmerking,
            afleverId, aflNaam, aflAdres, aflPostcode, aflPlaats, aflTav, regels, bijlagen, afzender, bedrijf, bvNaam, factuuradres);
    }

    public async Task ZetAanhefAsync(int contactId, string aanhef)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `Contacts` SET `Aanhef` = @a WHERE `ID_contact` = @id", cn);
        cmd.Parameters.AddWithValue("@a", aanhef);
        cmd.Parameters.AddWithValue("@id", contactId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MailVerstuurdAsync(int orderId, int? door, string logtekst)
    {
        await using var cn = await OpenAsync();
        await using (var cmd = new MySqlCommand("UPDATE `Orders` SET `MailAangemaakt` = NOW() WHERE `Order-id` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", orderId);
            await cmd.ExecuteNonQueryAsync();
        }
        try
        {
            await using var log = new MySqlCommand(
                "INSERT INTO `Log_Orderchanges` (`Order-ID`, `Toegevoegd`, `LogTekst`, `Groep`, `Door`) VALUES (@id, NOW(), @t, 'Mail', @door)", cn);
            log.Parameters.AddWithValue("@id", orderId);
            log.Parameters.AddWithValue("@t", logtekst.Length > 250 ? logtekst[..250] : logtekst);
            log.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            await log.ExecuteNonQueryAsync();
        }
        catch { /* logboek is bijzaak */ }
    }

    public async Task VoegOpmerkingToeAsync(int orderId, string regel)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Orders` SET `Remark` = CONCAT_WS('\n', NULLIF(TRIM(`Remark`), ''), @r) WHERE `Order-id` = @id", cn);
        cmd.Parameters.AddWithValue("@r", regel);
        cmd.Parameters.AddWithValue("@id", orderId);
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
    //  Zoeken door de regels (popup op Bestellingen)
    // ============================================================
    public async Task<List<BestelregelTreffer>> ZoekRegelsAsync(string zoek, int max, bool metGeannuleerd = false)
    {
        var lijst = new List<BestelregelTreffer>();
        var woorden = (zoek ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                  .Take(8).ToList();
        if (woorden.Count == 0) return lijst;

        // elk woord moet ergens in de regel of de bestelling voorkomen (AND over de woorden)
        var delen = new List<string>();
        for (var n = 0; n < woorden.Count; n++)
        {
            var w = woorden[n];
            if (w.StartsWith("PO-", StringComparison.OrdinalIgnoreCase)) w = w[3..];
            woorden[n] = w;
            delen.Add($"(i.`Partnumber` LIKE @w{n} OR i.`Description` LIKE @w{n} OR i.`MDE_ID` LIKE @w{n} " +
                      $" OR i.`Remark` LIKE @w{n} OR r.`name` LIKE @w{n} OR o.`Description` LIKE @w{n} " +
                      $" OR CAST(o.`Order-id` AS CHAR) LIKE @w{n})");
        }
        if (!metGeannuleerd) delen.Add($"COALESCE(o.`Status`, 0) <> {BestelVelden.StatusGeannuleerd}");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT o.`Order-id`, COALESCE(r.`name`, ''), COALESCE(o.`Description`, ''), COALESCE(o.`Status`, 0), " +
            "       COALESCE(st.`Omschrijving`, ''), o.`Creation date`, " +
            "       i.`Orderitem-id`, i.`Quantity ordered`, i.`Partnumber`, i.`Description`, i.`MDE_ID`, " +
            "       i.`Grossprice per VE`, i.`Totalprice`, i.`Remark`, i.`Workorder-ID` " +
            "  FROM `Orderitems` i " +
            "  JOIN `Orders` o ON o.`Order-id` = i.`Order-id` " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            "  LEFT JOIN `OrderState` st ON st.`Id` = o.`Status` " +
            " WHERE (i.`ToBeDeleted` = 0 OR i.`ToBeDeleted` IS NULL) " +
            "   AND " + string.Join(" AND ", delen) +
            $" ORDER BY o.`Order-id` DESC, i.`Orderitem-id` LIMIT {Math.Clamp(max, 1, 1000)}", cn);
        for (var n = 0; n < woorden.Count; n++) cmd.Parameters.AddWithValue($"@w{n}", "%" + woorden[n] + "%");

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string S(int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
            decimal? M(int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
            lijst.Add(new BestelregelTreffer(
                r.GetInt32(0), S(1), S(2),
                r.IsDBNull(3) ? 0 : Convert.ToInt32(r.GetValue(3)), S(4),
                r.IsDBNull(5) ? null : Convert.ToDateTime(r.GetValue(5)),
                r.GetInt32(6),
                r.IsDBNull(7) ? 0 : Convert.ToDouble(r.GetValue(7)),
                S(8), S(9), S(10), M(11), M(12), S(13),
                r.IsDBNull(14) ? null : Convert.ToInt32(r.GetValue(14))));
        }
        return lijst;
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

    public async Task<int> PrijzenBijwerkenAsync(int orderId, IReadOnlyList<(int RegelId, decimal Prijs, decimal? Korting)> regels,
                                                 int? door, string factuur)
    {
        if (regels.Count == 0) return 0;
        var nl = new System.Globalization.CultureInfo("nl-NL");
        var wat = new List<string>();
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        foreach (var (regelId, prijs, korting) in regels)
        {
            double aantal; decimal? oud; string code;
            await using (var cmd = new MySqlCommand(
                "SELECT COALESCE(`Quantity ordered`, 0), `Grossprice per VE`, COALESCE(`Partnumber`, ''), COALESCE(`Description`, '') " +
                "  FROM `Orderitems` WHERE `Orderitem-id` = @r AND `Order-id` = @o FOR UPDATE", cn, tx))
            {
                cmd.Parameters.AddWithValue("@r", regelId);
                cmd.Parameters.AddWithValue("@o", orderId);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) continue;                       // regel intussen weg, of van een andere bestelling
                aantal = Convert.ToDouble(r.GetValue(0));
                oud = r.IsDBNull(1) ? null : Convert.ToDecimal(r.GetValue(1));
                code = r.GetString(2).Trim() is { Length: > 0 } c ? c : r.GetString(3).Trim();
            }
            await using (var cmd = new MySqlCommand(
                "UPDATE `Orderitems` SET `Grossprice per VE` = @p, `Discount` = @k, `Totalprice` = @t " +
                " WHERE `Orderitem-id` = @r AND `Order-id` = @o", cn, tx))
            {
                cmd.Parameters.AddWithValue("@p", prijs);
                cmd.Parameters.AddWithValue("@k", (object?)korting ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@t", Orderregel.Bereken(aantal, prijs, korting));   // zelfde som als BewaarRegelAsync
                cmd.Parameters.AddWithValue("@r", regelId);
                cmd.Parameters.AddWithValue("@o", orderId);
                await cmd.ExecuteNonQueryAsync();
            }
            wat.Add($"{(code.Length > 30 ? code[..30] : code)}: {(oud is decimal o ? o.ToString("0.00##", nl) : "geen prijs")} -> {prijs.ToString("0.00##", nl)}" +
                    (korting is decimal k && k != 0 ? $" (-{(k * 100m).ToString("0.##", nl)}%)" : ""));
        }
        if (wat.Count > 0)
        {
            var tekst = $"Prijzen bijgewerkt uit factuur {factuur} ({wat.Count} regel(s)): " + string.Join("; ", wat);
            await using var log = new MySqlCommand(
                "INSERT INTO `Log_Orderchanges` (`Order-ID`, `Toegevoegd`, `LogTekst`, `Groep`, `Door`) VALUES (@o, NOW(), @t, 'Prijs', @d)", cn, tx);
            log.Parameters.AddWithValue("@o", orderId);
            log.Parameters.AddWithValue("@t", tekst.Length > 500 ? tekst[..497] + "..." : tekst);
            log.Parameters.AddWithValue("@d", (object?)door ?? DBNull.Value);
            await log.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return wat.Count;
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

    public async Task<LeverancierBereik?> LeverancierBereikAsync(int leverancierId)
    {
        await using var cn = await OpenAsync();
        string tel, mobiel;
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(`telephone`, ''), COALESCE(`telephone_mobile`, '') FROM `Relations` WHERE `id_supplier` = @l LIMIT 1", cn))
        {
            cmd.Parameters.AddWithValue("@l", leverancierId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            tel = r.GetString(0).Trim(); mobiel = r.GetString(1).Trim();
        }
        var contacten = new Dictionary<int, (string, string)>();
        await using (var cmd = new MySqlCommand(
            "SELECT `ID_contact`, COALESCE(`Telefoon`, ''), COALESCE(`Mobiel`, '') FROM `Contacts` WHERE `id_supplier` = @l", cn))
        {
            cmd.Parameters.AddWithValue("@l", leverancierId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                contacten[r.GetInt32(0)] = (r.GetString(1).Trim(), r.GetString(2).Trim());
        }
        return new LeverancierBereik(tel, mobiel, contacten);
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

    public async Task<bool> DefaultMaakdelenAsync(int? leverancierId)
    {
        if (leverancierId is not int lev) return false;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT COALESCE(`DefaultMaakdelen`, 0) FROM `Relations` WHERE `id_supplier` = @l LIMIT 1", cn);
        cmd.Parameters.AddWithValue("@l", lev);
        var r = await cmd.ExecuteScalarAsync();
        return r is not null and not DBNull && Convert.ToInt64(r) != 0;
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
