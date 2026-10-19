using MySqlConnector;

namespace EverythingApp.Services;

// =====================================================================================================================
//  Nacalculatie (Access: formulier _Nacalc_Analyse met de rapporten _Nacalc_Totaal_Overview, _DirectePO, _Materials,
//  _Hours, _Hours_Jaar en _Hours_Details). De rekenregels zitten in de MariaDB-views _Nacalc_* (ook door Access gebruikt);
//  hier worden de "bladeren" daarvan gefilterd op werkorder opgevraagd. De totaalview _Nacalc_Totaal rekent altijd alles
//  uit (± 3,5 s), gefilterd op de groepeersleutel is elke opvraging < 0,1 s.
// =====================================================================================================================

public record NacalcProject(int Id, string Nummer, string Omschrijving, string Klant, int? Status);

public record NacalcWerkorder(int Id, int ProjectId, string Nummer, string Omschrijving, string ProjectNummer,
                              string Project, string Klant, bool MateriaalNietMeenemen);

/// <summary>Uurkosten per werkorder, werknemer, tariefsoort, jaar en kostprijs (view _Nacalc_Uurkosten_Overview).</summary>
public record NacalcUren(int WerkorderId, int WerknemerId, string Werknemer, int TariefsoortId, string Tariefsoort, int Jaar,
                         decimal KostprijsWerkuur, decimal KostprijsReisuur, decimal KostprijsKm,
                         decimal Uren, decimal Km, decimal WerkKosten, decimal ReisKosten, decimal KmKosten)
{
    public decimal Totaal => WerkKosten + ReisKosten + KmKosten;
}

/// <summary>Uren die de nacalculatie níet meetelt: er is voor die datum geen kostprijstarief (WerknemersTarieven).</summary>
public record NacalcUrenZonderTarief(int WerkorderId, int WerknemerId, string Werknemer, int Jaar, decimal Uren, int Regels);

/// <summary>MDE-materiaal (ProjectMaterials, vrijgegeven voor bestellen) tegen kostprijs (view _Nacalc_ProjectMaterials_TotalPerSupplier_Ordered).</summary>
public record NacalcMateriaal(int WerkorderId, int ArtikelId, string Code, string Omschrijving, string Merk,
                              decimal Aantal, decimal Kostprijs, bool Schatting, decimal Kosten);

/// <summary>Een bestelling op de werkorder (view _Nacalc_Orders_Overview): besteld = regels bij status 4-6, gefactureerd = gekoppelde inkoopfacturen.</summary>
public record NacalcPo(int OrderId, int WerkorderId, string Leverancier, int Status, string StatusNaam, DateTime? BesteldOp,
                       string BesteldDoor, decimal Besteld, decimal Gefactureerd, int Facturen);

public record NacalcUurRegel(int UrenId, DateTime? Datum, int Week, decimal Uren, decimal Km, string Tariefsoort, string Omschrijving);

public class NacalcData
{
    public List<NacalcWerkorder> Werkorders { get; init; } = new();
    public List<NacalcUren> Uren { get; init; } = new();
    public List<NacalcUrenZonderTarief> ZonderTarief { get; init; } = new();
    public List<NacalcMateriaal> Materiaal { get; init; } = new();
    public List<NacalcPo> Pos { get; init; } = new();
}

public interface INacalculatieRepository
{
    bool Beschikbaar { get; }
    /// <summary>Projecten op klantnaam en/of projectnummer of -omschrijving (zoals Access: KlantZoek en ProjectZoek).</summary>
    Task<List<NacalcProject>> ProjectenAsync(string klant, string project, int max);
    Task<List<NacalcWerkorder>> WerkordersAsync(IReadOnlyCollection<int> projectIds);
    Task<NacalcWerkorder?> WerkorderAsync(int werkorderId);
    Task<NacalcData> LaadAsync(IReadOnlyCollection<NacalcWerkorder> werkorders);
    Task<List<NacalcUurRegel>> UurRegelsAsync(int werkorderId, int werknemerId, int? jaar);
}

public class GeenNacalculatieRepository : INacalculatieRepository
{
    public bool Beschikbaar => false;
    public Task<List<NacalcProject>> ProjectenAsync(string klant, string project, int max) => Task.FromResult(new List<NacalcProject>());
    public Task<List<NacalcWerkorder>> WerkordersAsync(IReadOnlyCollection<int> projectIds) => Task.FromResult(new List<NacalcWerkorder>());
    public Task<NacalcWerkorder?> WerkorderAsync(int werkorderId) => Task.FromResult<NacalcWerkorder?>(null);
    public Task<NacalcData> LaadAsync(IReadOnlyCollection<NacalcWerkorder> werkorders) => Task.FromResult(new NacalcData());
    public Task<List<NacalcUurRegel>> UurRegelsAsync(int werkorderId, int werknemerId, int? jaar) => Task.FromResult(new List<NacalcUurRegel>());
}

public class MySqlNacalculatieRepository : INacalculatieRepository
{
    private readonly string _cs;
    public MySqlNacalculatieRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static int I(MySqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
    private static decimal M(MySqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToDecimal(r.GetValue(i));
    private static bool B(MySqlDataReader r, int i) => !r.IsDBNull(i) && Convert.ToInt64(r.GetValue(i)) != 0;

    /// <summary>Getallen als lijst in de SQL (alleen ints, dus veilig); leeg wordt (0).</summary>
    private static string Lijst(IEnumerable<int> ids) { var s = string.Join(",", ids.Distinct()); return s.Length == 0 ? "0" : s; }

    public async Task<List<NacalcProject>> ProjectenAsync(string klant, string project, int max)
    {
        var lijst = new List<NacalcProject>();
        var waar = new List<string>();
        if (!string.IsNullOrWhiteSpace(klant)) waar.Add("r.`name` LIKE @klant");
        if (!string.IsNullOrWhiteSpace(project))
            waar.Add("(CAST(p.`Projectnumber` AS CHAR) LIKE @proj OR p.`Projectdescription` LIKE @projOms)");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT p.`Project-Id`, CAST(p.`Projectnumber` AS CHAR), COALESCE(p.`Projectdescription`, ''), COALESCE(r.`name`, ''), p.`ProjectStatus` " +
            "  FROM `Projects` p LEFT JOIN `Relations` r ON r.`id_supplier` = p.`Customer-id` " +
            (waar.Count == 0 ? "" : " WHERE " + string.Join(" AND ", waar)) +
            " ORDER BY p.`Projectnumber` DESC LIMIT @max", cn);
        cmd.Parameters.AddWithValue("@klant", "%" + klant.Trim() + "%");
        cmd.Parameters.AddWithValue("@proj", project.Trim() + "%");
        cmd.Parameters.AddWithValue("@projOms", "%" + project.Trim() + "%");
        cmd.Parameters.AddWithValue("@max", max);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new NacalcProject(r.GetInt32(0), S(r, 1), S(r, 2), S(r, 3), r.IsDBNull(4) ? null : I(r, 4)));
        return lijst;
    }

    private const string WerkorderSelect =
        "SELECT v.`Werkbon-id`, v.`Project-Id`, v.`Werkordernummer`, COALESCE(v.`Werkbonomschrijving`, ''), CAST(v.`ProjectNumber` AS CHAR), " +
        "       COALESCE(v.`Projectdescription`, ''), COALESCE(v.`Klant`, ''), v.`MateriaalKostenNietMeenemen` " +
        "  FROM `@VW_Full_OrderData` v ";

    private static NacalcWerkorder LeesWerkorder(MySqlDataReader r)
        => new(r.GetInt32(0), r.GetInt32(1), S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6), B(r, 7));

    public async Task<List<NacalcWerkorder>> WerkordersAsync(IReadOnlyCollection<int> projectIds)
    {
        var lijst = new List<NacalcWerkorder>();
        if (projectIds.Count == 0) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(WerkorderSelect + $" WHERE v.`Project-Id` IN ({Lijst(projectIds)}) ORDER BY v.`Werkordernummer`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(LeesWerkorder(r));
        return lijst;
    }

    public async Task<NacalcWerkorder?> WerkorderAsync(int werkorderId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(WerkorderSelect + " WHERE v.`Werkbon-id` = @id", cn);
        cmd.Parameters.AddWithValue("@id", werkorderId);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? LeesWerkorder(r) : null;
    }

    public async Task<NacalcData> LaadAsync(IReadOnlyCollection<NacalcWerkorder> werkorders)
    {
        var data = new NacalcData { Werkorders = werkorders.ToList() };
        if (werkorders.Count == 0) return data;
        var ids = Lijst(werkorders.Select(w => w.Id));
        await using var cn = await OpenAsync();

        // uurkosten (zelfde regels als Access: tariefsoort 1 = reisuren + km, 2-5 = werkuren)
        await using (var cmd = new MySqlCommand(
            "SELECT u.`werkorder`, u.`Werknemer-id`, COALESCE(w.`NaamVoluit`, ''), u.`Tariefsoort-id`, COALESCE(t.`Tariefsoort`, ''), u.`Jaar`, " +
            "       u.`KostprijsWerkuur`, u.`KostprijsReisuur`, u.`KostprijsKM`, SUM(u.`SomVanUren`), SUM(u.`SomVanKilometers`), " +
            "       SUM(u.`TotaalWerkuurKosten`), SUM(u.`TotaalReisuurKosten`), SUM(u.`TotaalKMKosten`) " +
            "  FROM `_Nacalc_Uurkosten_Overview` u " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = u.`Werknemer-id` " +
            "  LEFT JOIN `Tariefsoort` t ON t.`Tariefsoort-id` = u.`Tariefsoort-id` " +
            $" WHERE u.`werkorder` IN ({ids}) " +
            " GROUP BY u.`werkorder`, u.`Werknemer-id`, w.`NaamVoluit`, u.`Tariefsoort-id`, t.`Tariefsoort`, u.`Jaar`, " +
            "          u.`KostprijsWerkuur`, u.`KostprijsReisuur`, u.`KostprijsKM`", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                data.Uren.Add(new NacalcUren(I(r, 0), I(r, 1), S(r, 2), I(r, 3), S(r, 4), I(r, 5), M(r, 6), M(r, 7), M(r, 8),
                                             M(r, 9), M(r, 10), M(r, 11), M(r, 12), M(r, 13)));

        // uren zonder kostprijstarief (de view laat ze weg; zelfde datumregel: > Begindatum en <= Einddatum)
        await using (var cmd = new MySqlCommand(
            "SELECT x.`werkorder`, x.`Werknemer-id`, COALESCE(w.`NaamVoluit`, ''), YEAR(x.`StartDatum`), SUM(x.`Uren`), COUNT(*) " +
            "  FROM `Uren` x LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = x.`Werknemer-id` " +
            $" WHERE x.`werkorder` IN ({ids}) " +
            "   AND NOT EXISTS (SELECT 1 FROM `WerknemersTarieven` t WHERE t.`WerknemerID` = x.`Werknemer-id` " +
            "                      AND x.`StartDatum` > t.`Begindatum` AND x.`StartDatum` <= t.`Einddatum`) " +
            " GROUP BY x.`werkorder`, x.`Werknemer-id`, w.`NaamVoluit`, YEAR(x.`StartDatum`)", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                data.ZonderTarief.Add(new NacalcUrenZonderTarief(I(r, 0), I(r, 1), S(r, 2), I(r, 3), M(r, 4), I(r, 5)));

        // MDE-materiaal
        await using (var cmd = new MySqlCommand(
            "SELECT m.`WerkorderID`, m.`ArtikelID`, COALESCE(a.`ArtikelCodeMDE`, ''), COALESCE(a.`OmschrijvingNL`, ''), COALESCE(m.`Name`, ''), " +
            "       m.`SomVanAantal`, m.`VastgesteldeKostprijs`, m.`Schatting`, m.`SomVanKostenMDEmateriaal` " +
            "  FROM `_Nacalc_ProjectMaterials_TotalPerSupplier_Ordered` m " +
            "  LEFT JOIN `Artikelen` a ON a.`ArtikelID` = m.`ArtikelID` " +
            $" WHERE m.`WerkorderID` IN ({ids}) ORDER BY m.`WerkorderID`, m.`Name`, a.`ArtikelCodeMDE`", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                data.Materiaal.Add(new NacalcMateriaal(I(r, 0), I(r, 1), S(r, 2), S(r, 3), S(r, 4), M(r, 5), M(r, 6), B(r, 7), M(r, 8)));

        // bestellingen
        await using (var cmd = new MySqlCommand(
            "SELECT n.`Order-id`, n.`Werkbon-id`, COALESCE(l.`name`, ''), COALESCE(o.`Status`, 0), COALESCE(st.`Omschrijving`, ''), " +
            "       o.`Ordered date`, COALESCE(w.`NaamVoluit`, ''), n.`TotaalOrdered`, n.`TotaalFactuur`, " +
            "       (SELECT COUNT(*) FROM `Facturen_Orders_Link` f WHERE f.`OrderID` = n.`Order-id`) " +
            "  FROM `_Nacalc_Orders_Overview` n " +
            "  JOIN `Orders` o ON o.`Order-id` = n.`Order-id` " +
            "  LEFT JOIN `Relations` l ON l.`id_supplier` = n.`Supplier` " +
            "  LEFT JOIN `OrderState` st ON st.`Id` = o.`Status` " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = o.`Ordered by` " +
            $" WHERE n.`Werkbon-id` IN ({ids}) ORDER BY n.`Werkbon-id`, n.`Order-id`", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                data.Pos.Add(new NacalcPo(I(r, 0), I(r, 1), S(r, 2), I(r, 3), S(r, 4), r.IsDBNull(5) ? null : r.GetDateTime(5),
                                          S(r, 6), M(r, 7), M(r, 8), I(r, 9)));
        return data;
    }

    public async Task<List<NacalcUurRegel>> UurRegelsAsync(int werkorderId, int werknemerId, int? jaar)
    {
        var lijst = new List<NacalcUurRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT u.`Uren-id`, u.`StartDatum`, WEEK(u.`StartDatum`, 3), u.`Uren`, u.`Kilometers`, COALESCE(t.`Tariefsoort`, ''), COALESCE(u.`Omschrijving`, '') " +
            "  FROM `Uren` u LEFT JOIN `Tariefsoort` t ON t.`Tariefsoort-id` = u.`Tariefsoort-id` " +
            " WHERE u.`werkorder` = @wo AND u.`Werknemer-id` = @wn " +
            (jaar is null ? "" : " AND YEAR(u.`StartDatum`) = @jaar ") +
            " ORDER BY u.`StartDatum`, u.`Uren-id` LIMIT 2000", cn);
        cmd.Parameters.AddWithValue("@wo", werkorderId);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        if (jaar is not null) cmd.Parameters.AddWithValue("@jaar", jaar);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new NacalcUurRegel(I(r, 0), r.IsDBNull(1) ? null : r.GetDateTime(1), I(r, 2), M(r, 3), M(r, 4), S(r, 5), S(r, 6)));
        return lijst;
    }
}
