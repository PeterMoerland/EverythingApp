using MySqlConnector;

namespace EverythingApp.Services;

// =====================================================================================================================
//  Nacalculatie (Access: formulier _Nacalc_Analyse met de rapporten _Nacalc_Totaal_Overview, _DirectePO, _Materials,
//  _Hours, _Hours_Jaar en _Hours_Details). De rekenregels zitten in de MariaDB-views _Nacalc_* (ook door Access gebruikt);
//  hier worden de "bladeren" daarvan gefilterd op werkorder opgevraagd. De totaalview _Nacalc_Totaal rekent altijd alles
//  uit (± 3,5 s), gefilterd op de groepeersleutel is elke opvraging < 0,1 s.
// =====================================================================================================================

public record NacalcProject(int Id, string Nummer, string Omschrijving, string Klant, int? Status);

/// <param name="OuderId">Bovenliggende werkorder (Workorders.ParentWerkbonID, altijd van hetzelfde project); null = direct onder het project.</param>
public record NacalcWerkorder(int Id, int ProjectId, string Nummer, string Omschrijving, string ProjectNummer,
                              string Project, string Klant, bool MateriaalNietMeenemen, int? OuderId = null);

/// <summary>Werkorderboom voor de nacalculatie (zie <see cref="WerkorderBoom"/>).</summary>
public static class NacalcBoom
{
    public static List<(NacalcWerkorder Werkorder, int Diepte)> Ordenen(IReadOnlyCollection<NacalcWerkorder> lijst)
        => WerkorderBoom.Ordenen(lijst, w => w.Id, w => w.OuderId, w => w.Nummer);

    public static HashSet<int> Nakomelingen(IReadOnlyCollection<NacalcWerkorder> lijst, int id)
        => WerkorderBoom.Nakomelingen(lijst, w => w.Id, w => w.OuderId, id);

    public static HashSet<int> MetNakomelingen(IReadOnlyCollection<NacalcWerkorder> lijst, IEnumerable<int> gekozen)
        => WerkorderBoom.MetNakomelingen(lijst, w => w.Id, w => w.OuderId, gekozen);
}

/// <summary>Uurkosten per werkorder, werknemer, tariefsoort, jaar en kostprijs (view _Nacalc_Uurkosten_Overview).</summary>
/// <param name="LaatstBekend">Geen kostprijstarief voor die datum: gerekend met het laatst bekende tarief van de werknemer.</param>
public record NacalcUren(int WerkorderId, int WerknemerId, string Werknemer, int TariefsoortId, string Tariefsoort, int Jaar,
                         decimal KostprijsWerkuur, decimal KostprijsReisuur, decimal KostprijsKm,
                         decimal Uren, decimal Km, decimal WerkKosten, decimal ReisKosten, decimal KmKosten, bool LaatstBekend = false)
{
    public decimal Totaal => WerkKosten + ReisKosten + KmKosten;
}

/// <summary>Uren die de nacalculatie níet meetelt: de werknemer heeft helemaal geen kostprijstarief (WerknemersTarieven).</summary>
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

    // De rekenregels van de nacalculatie (tab Totaal): inkoop = per bestelling gefactureerd, zonder factuur besteld;
    // totaal = inkoop + materiaal + uren. Ook gebruikt door de voortgang van een project (ProjectBudget, Bepaal zelf).
    public decimal Besteld(int wo) => Pos.Where(p => p.WerkorderId == wo).Sum(p => p.Besteld);
    public decimal Gefactureerd(int wo) => Pos.Where(p => p.WerkorderId == wo).Sum(p => p.Gefactureerd);
    public decimal Inkoop(int wo) => Pos.Where(p => p.WerkorderId == wo).Sum(p => p.Facturen > 0 ? p.Gefactureerd : p.Besteld);
    public decimal Materiaalkosten(int wo) => Materiaal.Where(m => m.WerkorderId == wo).Sum(m => m.Kosten);
    public decimal Uurkosten(int wo) => Uren.Where(u => u.WerkorderId == wo).Sum(u => u.Totaal);
    public decimal Totaal(int wo) => Inkoop(wo) + Materiaalkosten(wo) + Uurkosten(wo);
    public decimal TotaalKosten(IEnumerable<NacalcWerkorder> wos) => wos.Sum(w => Totaal(w.Id));
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
    /// <summary>
    /// Werknemers die de laatste 30 dagen uren schreven, maar voor vandaag geen kostprijstarief hebben (voor de melding
    /// bovenin), met de einddatum van hun laatste tarief.
    /// </summary>
    Task<List<(string Naam, DateTime? LaatsteEind)>> ZonderTariefVandaagAsync();
}

public class GeenNacalculatieRepository : INacalculatieRepository
{
    public bool Beschikbaar => false;
    public Task<List<NacalcProject>> ProjectenAsync(string klant, string project, int max) => Task.FromResult(new List<NacalcProject>());
    public Task<List<NacalcWerkorder>> WerkordersAsync(IReadOnlyCollection<int> projectIds) => Task.FromResult(new List<NacalcWerkorder>());
    public Task<NacalcWerkorder?> WerkorderAsync(int werkorderId) => Task.FromResult<NacalcWerkorder?>(null);
    public Task<NacalcData> LaadAsync(IReadOnlyCollection<NacalcWerkorder> werkorders) => Task.FromResult(new NacalcData());
    public Task<List<NacalcUurRegel>> UurRegelsAsync(int werkorderId, int werknemerId, int? jaar) => Task.FromResult(new List<NacalcUurRegel>());
    public Task<List<(string Naam, DateTime? LaatsteEind)>> ZonderTariefVandaagAsync() => Task.FromResult(new List<(string, DateTime?)>());
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

    private bool _ouderKolom;

    /// <summary>Workorders.ParentWerkbonID (Werkorders_Boom_MariaDB.sql); alleen 'ja' onthouden.</summary>
    private async Task<bool> HeeftOuderKolomAsync(MySqlConnection cn)
    {
        if (_ouderKolom) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
            "   AND TABLE_NAME = 'Workorders' AND COLUMN_NAME = 'ParentWerkbonID'", cn);
        return _ouderKolom = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private async Task<string> WerkorderSelectAsync(MySqlConnection cn)
    {
        var ouder = await HeeftOuderKolomAsync(cn);
        return "SELECT v.`Werkbon-id`, v.`Project-Id`, v.`Werkordernummer`, COALESCE(v.`Werkbonomschrijving`, ''), CAST(v.`ProjectNumber` AS CHAR), " +
               "       COALESCE(v.`Projectdescription`, ''), COALESCE(v.`Klant`, ''), v.`MateriaalKostenNietMeenemen`, " +
               (ouder ? "wb.`ParentWerkbonID` " : "NULL ") +
               "  FROM `@VW_Full_OrderData` v " +
               (ouder ? "  LEFT JOIN `Workorders` wb ON wb.`Werkbon-id` = v.`Werkbon-id` " : "");
    }

    private static NacalcWerkorder LeesWerkorder(MySqlDataReader r)
        => new(r.GetInt32(0), r.GetInt32(1), S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6), B(r, 7),
               r.IsDBNull(8) || I(r, 8) == 0 ? null : I(r, 8));

    public async Task<List<NacalcWerkorder>> WerkordersAsync(IReadOnlyCollection<int> projectIds)
    {
        var lijst = new List<NacalcWerkorder>();
        if (projectIds.Count == 0) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(await WerkorderSelectAsync(cn) + $" WHERE v.`Project-Id` IN ({Lijst(projectIds)}) ORDER BY v.`Werkordernummer`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(LeesWerkorder(r));
        return lijst;
    }

    public async Task<NacalcWerkorder?> WerkorderAsync(int werkorderId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(await WerkorderSelectAsync(cn) + " WHERE v.`Werkbon-id` = @id", cn);
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

        // Uren zonder kostprijstarief voor die datum (de view laat ze weg; zelfde datumregel: > Begindatum en <= Einddatum).
        // Verbeterverzoek 24-10-2026: die tellen mee tegen het laatst bekende tarief van de werknemer (de periode met de
        // laatste begindatum op of voor die dag; ligt de dag vóór alle periodes, dan de eerste). Alleen wie helemaal
        // geen tarief heeft, blijft "niet meegeteld".
        var los = new List<(int Wo, int Wn, string Naam, int Soort, string SoortNaam, DateTime Datum, decimal Uren, decimal Km, int Regels)>();
        await using (var cmd = new MySqlCommand(
            "SELECT x.`werkorder`, x.`Werknemer-id`, COALESCE(w.`NaamVoluit`, ''), x.`Tariefsoort-id`, COALESCE(t.`Tariefsoort`, ''), " +
            "       x.`StartDatum`, SUM(x.`Uren`), SUM(x.`Kilometers`), COUNT(*) " +
            "  FROM `Uren` x LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = x.`Werknemer-id` " +
            "  LEFT JOIN `Tariefsoort` t ON t.`Tariefsoort-id` = x.`Tariefsoort-id` " +
            $" WHERE x.`werkorder` IN ({ids}) AND x.`StartDatum` IS NOT NULL " +
            "   AND NOT EXISTS (SELECT 1 FROM `WerknemersTarieven` k WHERE k.`WerknemerID` = x.`Werknemer-id` " +
            "                      AND x.`StartDatum` > k.`Begindatum` AND x.`StartDatum` <= k.`Einddatum`) " +
            " GROUP BY x.`werkorder`, x.`Werknemer-id`, w.`NaamVoluit`, x.`Tariefsoort-id`, t.`Tariefsoort`, x.`StartDatum`", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                los.Add((I(r, 0), I(r, 1), S(r, 2), I(r, 3), S(r, 4), r.GetDateTime(5), M(r, 6), M(r, 7), I(r, 8)));

        var tarieven = new Dictionary<int, List<(DateTime Begin, decimal Werk, decimal Reis, decimal Km)>>();
        if (los.Count > 0)
        {
            await using var cmd = new MySqlCommand(
                "SELECT `WerknemerID`, `Begindatum`, COALESCE(`KostprijsWerkuur`, 0), COALESCE(`KostprijsReisuur`, 0), COALESCE(`KostprijsKM`, 0) " +
                $"  FROM `WerknemersTarieven` WHERE `Begindatum` IS NOT NULL AND `WerknemerID` IN ({Lijst(los.Select(l => l.Wn))})", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var wn = I(r, 0);
                if (!tarieven.TryGetValue(wn, out var l)) tarieven[wn] = l = new();
                l.Add((r.GetDateTime(1), M(r, 2), M(r, 3), M(r, 4)));
            }
        }

        var laatstBekend = new List<NacalcUren>();
        foreach (var x in los)
        {
            if (tarieven.GetValueOrDefault(x.Wn) is not { Count: > 0 } perioden)
            {
                data.ZonderTarief.Add(new NacalcUrenZonderTarief(x.Wo, x.Wn, x.Naam, x.Datum.Year, x.Uren, x.Regels));
                continue;
            }
            var k = perioden.Where(p => p.Begin.Date <= x.Datum.Date).OrderByDescending(p => p.Begin).Select(p => ((DateTime, decimal, decimal, decimal)?)p).FirstOrDefault()
                    ?? perioden.OrderBy(p => p.Begin).First();
            var (_, werk, reis, km) = k;
            // zelfde regels als de view: tariefsoort 1 = reisuren + km, 2-5 = werkuren, overige 0
            laatstBekend.Add(new NacalcUren(x.Wo, x.Wn, x.Naam, x.Soort, x.SoortNaam, x.Datum.Year, werk, reis, km, x.Uren, x.Km,
                x.Soort is > 1 and <= 5 ? x.Uren * werk : 0, x.Soort == 1 ? x.Uren * reis : 0, x.Soort == 1 ? x.Km * km : 0, LaatstBekend: true));
        }
        // per werkorder, werknemer, soort, jaar en kostprijs samen (zoals de view per dag, hier al opgeteld)
        data.Uren.AddRange(laatstBekend
            .GroupBy(u => (u.WerkorderId, u.WerknemerId, u.Werknemer, u.TariefsoortId, u.Tariefsoort, u.Jaar, u.KostprijsWerkuur, u.KostprijsReisuur, u.KostprijsKm))
            .Select(g => new NacalcUren(g.Key.WerkorderId, g.Key.WerknemerId, g.Key.Werknemer, g.Key.TariefsoortId, g.Key.Tariefsoort, g.Key.Jaar,
                                        g.Key.KostprijsWerkuur, g.Key.KostprijsReisuur, g.Key.KostprijsKm, g.Sum(u => u.Uren), g.Sum(u => u.Km),
                                        g.Sum(u => u.WerkKosten), g.Sum(u => u.ReisKosten), g.Sum(u => u.KmKosten), LaatstBekend: true)));
        // per werkorder, werknemer en jaar samen (het blok "niet meegeteld")
        var zonder = data.ZonderTarief.GroupBy(z => (z.WerkorderId, z.WerknemerId, z.Werknemer, z.Jaar))
            .Select(g => new NacalcUrenZonderTarief(g.Key.WerkorderId, g.Key.WerknemerId, g.Key.Werknemer, g.Key.Jaar, g.Sum(z => z.Uren), g.Sum(z => z.Regels)))
            .ToList();
        data.ZonderTarief.Clear();
        data.ZonderTarief.AddRange(zonder);

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

    public async Task<List<(string Naam, DateTime? LaatsteEind)>> ZonderTariefVandaagAsync()
    {
        var lijst = new List<(string, DateTime?)>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(w.`NaamVoluit`, CONCAT('#', w.`Werknemer-id`)), (SELECT MAX(t.`Einddatum`) FROM `WerknemersTarieven` t WHERE t.`WerknemerID` = w.`Werknemer-id`) " +
            "  FROM `Werknemers` w " +
            " WHERE EXISTS (SELECT 1 FROM `Uren` u WHERE u.`Werknemer-id` = w.`Werknemer-id` AND u.`StartDatum` >= CURDATE() - INTERVAL 30 DAY) " +
            "   AND NOT EXISTS (SELECT 1 FROM `WerknemersTarieven` t WHERE t.`WerknemerID` = w.`Werknemer-id` " +
            "                      AND CURDATE() >= DATE(t.`Begindatum`) AND CURDATE() <= DATE(t.`Einddatum`)) " +
            " ORDER BY 1", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add((S(r, 0), r.IsDBNull(1) ? null : r.GetDateTime(1)));
        return lijst;
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
