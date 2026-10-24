using System.Globalization;
using System.Text.Json.Nodes;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// De niet-financiële opvragingen voor Vragen aan AI: projecten, werkorders, uren, bestellingen, voorraad, producten en
/// hun todo's; plus de nacalculatie per project (alleen met recht Financial/Directie, want kostprijzen).
/// Alleen lezen, begrensd, en waar het kan via de bestaande repositories (zelfde regels als de schermen).
/// Uren van anderen per medewerker: alleen met Financial/Directie; anders alleen de eigen uren.
/// </summary>
public class AlgemeneAiTools
{
    private readonly IVoorraadRepository _voorraad;
    private readonly IProductTodoRepository _todos;
    private readonly INacalculatieRepository _nacalc;
    private readonly string? _cs;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const int MaxRijen = FinancieleAiTools.MaxRijen;

    /// <summary>Alleen met Financial/Directie (en niet op een werkplek).</summary>
    public static readonly HashSet<string> Financieel = new() { "nacalculatie_project" };

    public AlgemeneAiTools(IVoorraadRepository voorraad, IProductTodoRepository todos, INacalculatieRepository nacalc, IConfiguration config)
    {
        _voorraad = voorraad; _todos = todos; _nacalc = nacalc;
        _cs = config.GetConnectionString("MDE");
    }

    // ================================================================
    //  Beschrijving voor het model
    // ================================================================
    private static JsonObject Tool(string naam, string beschrijving, JsonObject eigenschappen)
        => new()
        {
            ["toolSpec"] = new JsonObject
            {
                ["name"] = naam,
                ["description"] = beschrijving,
                ["inputSchema"] = new JsonObject
                {
                    ["json"] = new JsonObject { ["type"] = "object", ["properties"] = eigenschappen, ["required"] = new JsonArray() },
                },
            },
        };

    private static JsonObject P(string type, string beschrijving) => new() { ["type"] = type, ["description"] = beschrijving };
    private static JsonObject Periode(JsonObject extra, string standaard)
    {
        extra["van"] = P("string", $"Begindatum jjjj-mm-dd (inclusief). Standaard {standaard}.");
        extra["tot"] = P("string", "Einddatum jjjj-mm-dd (inclusief). Standaard vandaag.");
        return extra;
    }

    /// <summary>De tools; <paramref name="financieel"/> = ook de nacalculatie.</summary>
    public IEnumerable<JsonObject> Tools(bool financieel)
    {
        yield return Tool("projecten_zoeken",
            "Projecten zoeken op nummer, omschrijving of klant, met status (Open/Pending/Gesloten), projectleider en aantal werkorders.",
            new JsonObject
            {
                ["zoek"] = P("string", "Deel van projectnummer of omschrijving (optioneel)."),
                ["klant"] = P("string", "Deel van de klantnaam (optioneel)."),
                ["status"] = P("string", "'open' (standaard), 'pending', 'gesloten' of 'alle'."),
            });
        yield return Tool("werkorders_van_project",
            "De werkorders van een project (nummer, omschrijving, status, budget en de uren van ALLE jaren samen). " +
            "Voor uren in een periode: gebruik uren_per_project.",
            new JsonObject { ["project"] = P("string", "Projectnummer, bv. 223159.") });
        yield return Tool("uren_per_project",
            "Geschreven uren op een project in een periode: één regel per werkorder, al opgeteld (uren, km, aantal medewerkers). " +
            "Alleen als er naar medewerkers gevraagd wordt: per_medewerker=true (per werkorder, medewerker en soort).",
            Periode(new JsonObject
            {
                ["project"] = P("string", "Projectnummer, bv. 223159."),
                ["per_medewerker"] = P("boolean", "Alleen als de vraag over medewerkers gaat: uitsplitsen per medewerker en soort."),
            }, "alles"));
        yield return Tool("uren_per_medewerker",
            "Geschreven uren per medewerker, verdeeld over projecten. Zonder recht Financial/Directie alleen de eigen uren.",
            Periode(new JsonObject { ["medewerker"] = P("string", "Deel van de naam (optioneel; leeg = iedereen).") }, "1 januari van dit jaar"));
        yield return Tool("bestellingen",
            "Bestellingen (PO's) met leverancier, status, besteld op/door, werkorder en totaalbedrag ex btw.",
            Periode(new JsonObject
            {
                ["project"] = P("string", "Projectnummer (optioneel)."),
                ["leverancier"] = P("string", "Deel van de naam van de leverancier (optioneel)."),
                ["status"] = P("string", "'open' (nog niet gereed, standaard), 'wacht_op_levering' of 'alle'."),
            }, "1 januari van vorig jaar"));
        yield return Tool("voorraad",
            "Artikelen op voorraad zoeken: code, omschrijving, totaal op voorraad en aantal locaties.",
            new JsonObject { ["zoek"] = P("string", "Artikelcode, omschrijving of MDE-nummer.") });
        yield return Tool("producten_overzicht",
            "Aantal exemplaren (robots, prints, motoren, ...) per producttype en status.",
            new JsonObject { ["type"] = P("string", "Deel van de typenaam, bv. MPR (optioneel).") });
        yield return Tool("product_todos",
            "Open todo-punten bij producten (bv. Doorsolderen), met het product waar ze in zitten en de klant.",
            new JsonObject { ["wat"] = P("string", "Alleen dit soort punt, bv. Doorsolderen (optioneel).") });
        if (financieel)
            yield return Tool("nacalculatie_project",
                "Nacalculatie van een project per werkorder: budget, inkoop (gefactureerd, anders besteld), MDE-materiaal en " +
                "uurkosten tegen kostprijs, totaal; plus uren zonder kostprijstarief (niet meegeteld). Bedragen ex btw.",
                new JsonObject { ["project"] = P("string", "Projectnummer, bv. 223159.") });
    }

    public bool Kent(string naam) => naam is "projecten_zoeken" or "werkorders_van_project" or "uren_per_project" or "uren_per_medewerker"
        or "bestellingen" or "voorraad" or "producten_overzicht" or "product_todos" or "nacalculatie_project";

    // ================================================================
    //  Uitvoeren
    // ================================================================
    public async Task<AiGegevens> VoerUitAsync(string naam, JsonObject invoer, int werknemerId, bool financieel)
    {
        var g = new AiGegevens { Naam = naam, Invoer = invoer.ToJsonString() };
        try
        {
            if (Financieel.Contains(naam) && !financieel) { g.Titel = naam; g.Opmerking = "Geen toegang: alleen met het recht Financial of Directie."; return g; }
            switch (naam)
            {
                case "projecten_zoeken": await Projecten(g, invoer); break;
                case "werkorders_van_project": await Werkorders(g, invoer); break;
                case "uren_per_project": await UrenProject(g, invoer); break;
                case "uren_per_medewerker": await UrenMedewerker(g, invoer, werknemerId, financieel); break;
                case "bestellingen": await Bestellingen(g, invoer); break;
                case "voorraad": await Voorraad(g, invoer); break;
                case "producten_overzicht": await Producten(g, invoer); break;
                case "product_todos": await Todos(g, invoer); break;
                case "nacalculatie_project": await Nacalculatie(g, invoer); break;
                default: g.Titel = naam; g.Opmerking = "Onbekende opvraging."; break;
            }
        }
        catch (Exception ex)
        {
            g.Titel = g.Titel.Length > 0 ? g.Titel : naam;
            g.Opmerking = "Ophalen mislukt: " + ex.Message;
        }
        return g;
    }

    // ---- hulp ----
    private static string? Tekst(JsonObject i, string naam)
        => i[naam] is JsonValue v && (v.TryGetValue<string>(out var s) ? s : v.ToString()) is { } t && t.Trim().Length > 0 ? t.Trim() : null;
    private static string B(decimal d) => d.ToString("0.00", Inv);
    private static string U(decimal d) => d.ToString("0.##", Inv);
    private static string D(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "";
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static (DateOnly Van, DateOnly Tot) Periode2(JsonObject i, DateOnly standaardVan)
    {
        var vandaag = DateOnly.FromDateTime(DateTime.Today);
        var van = DateOnly.TryParse(Tekst(i, "van"), Inv, out var a) ? a : standaardVan;
        var tot = DateOnly.TryParse(Tekst(i, "tot"), Inv, out var b) ? b : vandaag;
        if (tot < van) (van, tot) = (tot, van);
        return (van, tot);
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        if (string.IsNullOrEmpty(_cs)) throw new InvalidOperationException("Geen database.");
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static decimal M(MySqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToDecimal(r.GetValue(i));

    private static void Afkappen(AiGegevens g, int totaal)
    {
        if (g.Rijen.Count > MaxRijen) g.Rijen = g.Rijen.Take(MaxRijen).ToList();
        if (totaal > g.Rijen.Count) g.Opmerking = (g.Opmerking + $" Afgekapt: {g.Rijen.Count} van {totaal} regels getoond.").Trim();
    }

    private static string StatusProject(string s) => s switch { "1" => "Open", "2" => "Pending", "3" => "Gesloten", _ => s };
    private static string StatusWerkorder(string s) => s switch { "1" => "Open", "2" => "Pending", "0" => "Gesloten", _ => s };

    /// <summary>Werkorder-ids van een project (op nummer).</summary>
    private async Task<int?> ProjectId(MySqlConnection cn, string nummer)
    {
        await using var cmd = new MySqlCommand("SELECT `Project-Id` FROM `Projects` WHERE CAST(`Projectnumber` AS CHAR) = @n LIMIT 1", cn);
        cmd.Parameters.AddWithValue("@n", nummer.Trim());
        var o = await cmd.ExecuteScalarAsync();
        return o is null or DBNull ? null : Convert.ToInt32(o);
    }

    // ---- projecten ----
    private async Task Projecten(AiGegevens g, JsonObject i)
    {
        var zoek = Tekst(i, "zoek"); var klant = Tekst(i, "klant");
        var status = (Tekst(i, "status") ?? "open").ToLowerInvariant();
        var waar = new List<string>();
        if (zoek is not null) waar.Add("(CAST(p.`Projectnumber` AS CHAR) LIKE @zoek OR p.`Projectdescription` LIKE @zoek)");
        if (klant is not null) waar.Add("r.`name` LIKE @klant");
        var st = status switch { "pending" => 2, "gesloten" => 3, "alle" => 0, _ => 1 };
        if (st != 0) waar.Add("p.`ProjectStatus` = " + st);
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT CAST(p.`Projectnumber` AS CHAR), COALESCE(p.`Projectdescription`, ''), COALESCE(r.`name`, ''), p.`ProjectStatus`, " +
            "       COALESCE(w.`NaamVoluit`, ''), (SELECT COUNT(*) FROM `Workorders` wo WHERE wo.`Project-id` = p.`Project-Id` AND COALESCE(wo.`Deleted`, 0) = 0) " +
            "  FROM `Projects` p LEFT JOIN `Relations` r ON r.`id_supplier` = p.`Customer-id` " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = p.`ProjectResponsible` " +
            (waar.Count == 0 ? "" : " WHERE " + string.Join(" AND ", waar)) +
            " ORDER BY p.`Projectnumber` DESC LIMIT 1000", cn);
        if (zoek is not null) cmd.Parameters.AddWithValue("@zoek", "%" + zoek + "%");
        if (klant is not null) cmd.Parameters.AddWithValue("@klant", "%" + klant + "%");
        g.Titel = "Projecten" + (zoek is null ? "" : $" '{zoek}'") + (klant is null ? "" : $", klant '{klant}'") + $", status {status}";
        g.Kolommen = new() { "Project", "Omschrijving", "Klant", "Status", "Projectleider", "Werkorders" };
        var n = 0;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) { n++; g.Rijen.Add(new() { S(r, 0), S(r, 1), S(r, 2), StatusProject(S(r, 3)), S(r, 4), S(r, 5) }); }
        g.Opmerking = $"{n} projecten gevonden.";
        Afkappen(g, n);
    }

    private async Task Werkorders(AiGegevens g, JsonObject i)
    {
        var nummer = Tekst(i, "project") ?? "";
        await using var cn = await OpenAsync();
        var pid = await ProjectId(cn, nummer);
        g.Titel = $"Werkorders van project {nummer}";
        g.Kolommen = new() { "Werkorder", "Omschrijving", "Status", "Budget", "Uren alle jaren", "Inkoop toegestaan" };
        if (pid is null) { g.Opmerking = "Project niet gevonden."; return; }
        await using var cmd = new MySqlCommand(
            "SELECT v.`Werkordernummer`, COALESCE(v.`Werkbonomschrijving`, ''), v.`WorkOrderStatus`, COALESCE(wo.`Budget`, 0), " +
            "       (SELECT COALESCE(SUM(u.`Uren`), 0) FROM `Uren` u WHERE u.`werkorder` = v.`Werkbon-id`), v.`InkoopToegestaan` " +
            "  FROM `@VW_Full_OrderData` v JOIN `Workorders` wo ON wo.`Werkbon-id` = v.`Werkbon-id` " +
            " WHERE v.`Project-Id` = @p ORDER BY v.`Werkordernummer`", cn);
        cmd.Parameters.AddWithValue("@p", pid);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            g.Rijen.Add(new() { S(r, 0), S(r, 1), StatusWerkorder(S(r, 2)), B(M(r, 3)), U(M(r, 4)), M(r, 5) != 0 ? "ja" : "nee" });
        g.Opmerking = $"{g.Rijen.Count} werkorders. Budget in euro's (0 = niet ingevuld). De uren zijn ALLE jaren samen, niet een periode.";
    }

    // ---- uren ----
    private async Task UrenProject(AiGegevens g, JsonObject i)
    {
        var nummer = Tekst(i, "project") ?? "";
        var (van, tot) = Periode2(i, new DateOnly(2000, 1, 1));
        await using var cn = await OpenAsync();
        var pid = await ProjectId(cn, nummer);
        var detail = i["per_medewerker"] is JsonValue dv && dv.TryGetValue<bool>(out var dd) && dd;
        g.Titel = $"Uren op project {nummer}" + (van.Year > 2000 ? $", {D(van)} t/m {D(tot)}" : "") + (detail ? ", per medewerker en soort" : ", per werkorder");
        g.Kolommen = detail ? new() { "Werkorder", "Medewerker", "Soort", "Uren", "Km" } : new() { "Werkorder", "Omschrijving", "Uren", "Km", "Medewerkers" };
        if (pid is null) { g.Opmerking = "Project niet gevonden."; return; }
        await using var cmd = new MySqlCommand(detail
            ? "SELECT v.`Werkordernummer`, COALESCE(w.`NaamVoluit`, ''), COALESCE(t.`Tariefsoort`, ''), SUM(u.`Uren`), SUM(COALESCE(u.`Kilometers`, 0)) " +
            "  FROM `Uren` u JOIN `@VW_Full_OrderData` v ON v.`Werkbon-id` = u.`werkorder` " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = u.`Werknemer-id` " +
            "  LEFT JOIN `Tariefsoort` t ON t.`Tariefsoort-id` = u.`Tariefsoort-id` " +
            " WHERE v.`Project-Id` = @p AND u.`StartDatum` >= @van AND u.`StartDatum` < @tot " +
            " GROUP BY v.`Werkordernummer`, w.`NaamVoluit`, t.`Tariefsoort` ORDER BY v.`Werkordernummer`, SUM(u.`Uren`) DESC"
            : "SELECT v.`Werkordernummer`, COALESCE(v.`Werkbonomschrijving`, ''), SUM(u.`Uren`), SUM(COALESCE(u.`Kilometers`, 0)), " +
              "       COUNT(DISTINCT u.`Werknemer-id`) " +
              "  FROM `Uren` u JOIN `@VW_Full_OrderData` v ON v.`Werkbon-id` = u.`werkorder` " +
              " WHERE v.`Project-Id` = @p AND u.`StartDatum` >= @van AND u.`StartDatum` < @tot " +
              " GROUP BY v.`Werkordernummer`, v.`Werkbonomschrijving` ORDER BY v.`Werkordernummer`", cn);
        cmd.Parameters.AddWithValue("@p", pid);
        cmd.Parameters.AddWithValue("@van", van.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tot", tot.AddDays(1).ToDateTime(TimeOnly.MinValue));
        decimal totaal = 0; var n = 0;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            n++;
            if (detail) { totaal += M(r, 3); g.Rijen.Add(new() { S(r, 0), S(r, 1), S(r, 2), U(M(r, 3)), U(M(r, 4)) }); }
            else { totaal += M(r, 2); g.Rijen.Add(new() { S(r, 0), S(r, 1), U(M(r, 2)), U(M(r, 3)), S(r, 4) }); }
        }
        g.Opmerking = $"Totaal {U(totaal)} uur in deze periode." + (detail ? " Totalen per werkorder: vraag op zonder per_medewerker." : "");
        Afkappen(g, n);
    }

    private async Task UrenMedewerker(AiGegevens g, JsonObject i, int werknemerId, bool financieel)
    {
        var naam = Tekst(i, "medewerker");
        var (van, tot) = Periode2(i, new DateOnly(DateTime.Today.Year, 1, 1));
        var waar = "u.`StartDatum` >= @van AND u.`StartDatum` < @tot";
        if (!financieel) waar += " AND u.`Werknemer-id` = @ik";                 // zonder recht alleen de eigen uren
        else if (naam is not null) waar += " AND w.`NaamVoluit` LIKE @naam";
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(w.`NaamVoluit`, ''), CONCAT(v.`ProjectNumber`, ' ', COALESCE(v.`Projectdescription`, '')), SUM(u.`Uren`) " +
            "  FROM `Uren` u LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = u.`Werknemer-id` " +
            "  LEFT JOIN `@VW_Full_OrderData` v ON v.`Werkbon-id` = u.`werkorder` " +
            $" WHERE {waar} GROUP BY w.`NaamVoluit`, v.`ProjectNumber`, v.`Projectdescription` ORDER BY w.`NaamVoluit`, SUM(u.`Uren`) DESC LIMIT 2000", cn);
        cmd.Parameters.AddWithValue("@van", van.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tot", tot.AddDays(1).ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@ik", werknemerId);
        if (naam is not null) cmd.Parameters.AddWithValue("@naam", "%" + naam + "%");
        g.Titel = $"Uren per medewerker {D(van)} t/m {D(tot)}" + (!financieel ? " (alleen eigen uren)" : naam is null ? "" : $", '{naam}'");
        g.Kolommen = new() { "Medewerker", "Project", "Uren" };
        var per = new Dictionary<string, decimal>();
        var n = 0;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            n++;
            per[S(r, 0)] = per.GetValueOrDefault(S(r, 0)) + M(r, 2);
            g.Rijen.Add(new() { S(r, 0), S(r, 1).Trim(), U(M(r, 2)) });
        }
        g.Opmerking = "Totaal per medewerker: " + string.Join("; ", per.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {U(p.Value)}")) + "." +
                      (!financieel ? " Zonder recht Financial/Directie alleen de eigen uren." : "");
        Afkappen(g, n);
    }

    // ---- bestellingen ----
    private async Task Bestellingen(AiGegevens g, JsonObject i)
    {
        var project = Tekst(i, "project"); var lev = Tekst(i, "leverancier");
        var status = (Tekst(i, "status") ?? "open").ToLowerInvariant();
        var (van, tot) = Periode2(i, new DateOnly(DateTime.Today.Year - 1, 1, 1));
        var waar = new List<string> { "COALESCE(o.`Creation date`, o.`Ordered date`) >= @van", "COALESCE(o.`Creation date`, o.`Ordered date`) < @tot" };
        waar.Add(status switch
        {
            "alle" => "1 = 1",
            "wacht_op_levering" => "o.`Status` = 4",
            _ => "COALESCE(o.`Status`, 0) < 6",
        });
        if (project is not null) waar.Add("CAST(v.`ProjectNumber` AS CHAR) = @project");
        if (lev is not null) waar.Add("l.`name` LIKE @lev");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT o.`Order-id`, COALESCE(o.`Ordered date`, o.`Creation date`), COALESCE(l.`name`, ''), COALESCE(st.`Omschrijving`, ''), " +
            "       COALESCE(wb.`NaamVoluit`, wc.`NaamVoluit`, ''), COALESCE(v.`Werkordernummer`, ''), COALESCE(o.`Description`, ''), " +
            "       (SELECT COALESCE(SUM(oi.`Totalprice`), 0) FROM `Orderitems` oi WHERE oi.`Order-id` = o.`Order-id`) " +
            "  FROM `Orders` o LEFT JOIN `Relations` l ON l.`id_supplier` = o.`Supplier` " +
            "  LEFT JOIN `OrderState` st ON st.`Id` = o.`Status` " +
            "  LEFT JOIN `Werknemers` wb ON wb.`Werknemer-id` = o.`Ordered by` " +
            "  LEFT JOIN `Werknemers` wc ON wc.`Werknemer-id` = o.`Created by` " +
            "  LEFT JOIN `@VW_Full_OrderData` v ON v.`Werkbon-id` = o.`Workorder-id` " +
            " WHERE " + string.Join(" AND ", waar) + " ORDER BY o.`Order-id` DESC LIMIT 2000", cn);
        cmd.Parameters.AddWithValue("@van", van.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tot", tot.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (project is not null) cmd.Parameters.AddWithValue("@project", project);
        if (lev is not null) cmd.Parameters.AddWithValue("@lev", "%" + lev + "%");
        g.Titel = $"Bestellingen {D(van)} t/m {D(tot)}, status {status}" + (project is null ? "" : $", project {project}") + (lev is null ? "" : $", leverancier '{lev}'");
        g.Kolommen = new() { "PO", "Datum", "Leverancier", "Status", "Door", "Werkorder", "Omschrijving", "Totaal ex btw" };
        decimal totaal = 0; var n = 0;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            n++; totaal += M(r, 7);
            g.Rijen.Add(new() { "PO-" + S(r, 0), D(r.IsDBNull(1) ? null : r.GetDateTime(1)), S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6), B(M(r, 7)) });
        }
        g.Opmerking = $"{n} bestellingen, samen {B(totaal)} ex btw.";
        Afkappen(g, n);
    }

    // ---- voorraad, producten ----
    private async Task Voorraad(AiGegevens g, JsonObject i)
    {
        var zoek = Tekst(i, "zoek") ?? "";
        var lijst = await _voorraad.ArtikelenAsync(zoek, null, null, MaxRijen);
        g.Titel = $"Voorraad '{zoek}'";
        g.Kolommen = new() { "Artikel", "Code", "Omschrijving", "Op voorraad", "Locaties" };
        foreach (var a in lijst) g.Rijen.Add(new() { a.ArtikelId.ToString(Inv), a.Code, a.Omschrijving, U(a.Totaal), a.Posities.ToString(Inv) });
        g.Opmerking = lijst.Count == 0 ? "Niets gevonden." : $"{lijst.Count} artikelen.";
    }

    private async Task Producten(AiGegevens g, JsonObject i)
    {
        var type = Tekst(i, "type");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(n.`ProductName`, '?'), COALESCE(n.`Omschrijving`, ''), COALESCE(s.`Status`, s.`StatusShort`, '(geen)'), COUNT(*) " +
            "  FROM `Producten` p LEFT JOIN `ProductNames` n ON n.`ProductNameID` = p.`ProductNameID` " +
            "  LEFT JOIN `ProductStatus` s ON s.`ProductStatusID` = p.`ProductStatusID` " +
            " WHERE (p.`isDeleted` = 0 OR p.`isDeleted` IS NULL) " + (type is null ? "" : " AND n.`ProductName` LIKE @type ") +
            " GROUP BY n.`ProductName`, n.`Omschrijving`, s.`Status`, s.`StatusShort` ORDER BY n.`ProductName`, COUNT(*) DESC LIMIT 1000", cn);
        if (type is not null) cmd.Parameters.AddWithValue("@type", "%" + type + "%");
        g.Titel = "Exemplaren per type en status" + (type is null ? "" : $" ('{type}')");
        g.Kolommen = new() { "Type", "Omschrijving", "Status", "Aantal" };
        var n = 0; var som = 0;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) { n++; som += Convert.ToInt32(r.GetValue(3)); g.Rijen.Add(new() { S(r, 0), S(r, 1), S(r, 2), S(r, 3) }); }
        g.Opmerking = $"Samen {som} exemplaren (zonder gewiste).";
        Afkappen(g, n);
    }

    private async Task Todos(AiGegevens g, JsonObject i)
    {
        var wat = Tekst(i, "wat");
        var lijst = await _todos.OverzichtAsync(wat, false, 2000);
        g.Titel = "Open todo-punten bij producten" + (wat is null ? "" : $" ('{wat}')");
        g.Kolommen = new() { "Product", "Onderdeel", "Wat", "Toelichting", "Klant", "Ingevoerd" };
        foreach (var t in lijst)
            g.Rijen.Add(new() { t.WortelId is null ? t.Exemplaar : t.Wortel, t.InOnderdeel ? t.Exemplaar : "", t.Wat, t.Toelichting, t.Klant,
                                t.Aangemaakt.ToString("yyyy-MM-dd") + " " + t.Door });
        g.Opmerking = lijst.Count == 0 ? (await _todos.TabelAanwezigAsync() ? "Geen open punten." : "De tabel ProductTodo bestaat niet.")
            : "Per soort: " + string.Join("; ", lijst.GroupBy(t => t.Wat).Select(x => $"{x.Key} {x.Count()}")) + ".";
        Afkappen(g, lijst.Count);
    }

    // ---- nacalculatie (Financial) ----
    private async Task Nacalculatie(AiGegevens g, JsonObject i)
    {
        var nummer = Tekst(i, "project") ?? "";
        var project = (await _nacalc.ProjectenAsync("", nummer, 20)).FirstOrDefault(p => p.Nummer == nummer);
        g.Titel = $"Nacalculatie project {nummer}";
        g.Kolommen = new() { "Werkorder", "Omschrijving", "Budget", "Inkoop", "Materiaal", "Uurkosten", "Totaal kosten", "Uren zonder tarief" };
        if (project is null) { g.Opmerking = "Project niet gevonden."; return; }
        var wos = await _nacalc.WerkordersAsync(new[] { project.Id });
        var data = await _nacalc.LaadAsync(wos);
        var budget = new Dictionary<int, decimal>();
        await using (var cn = await OpenAsync())
        await using (var cmd = new MySqlCommand("SELECT `Werkbon-id`, COALESCE(`Budget`, 0) FROM `Workorders` WHERE `Project-id` = @p", cn))
        {
            cmd.Parameters.AddWithValue("@p", project.Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) budget[r.GetInt32(0)] = M(r, 1);
        }
        decimal tb = 0, ti = 0, tm = 0, tu = 0, tz = 0;
        foreach (var w in data.Werkorders)
        {
            var inkoop = data.Pos.Where(p => p.WerkorderId == w.Id).Sum(p => p.Facturen > 0 ? p.Gefactureerd : p.Besteld);
            var mat = data.Materiaal.Where(m => m.WerkorderId == w.Id).Sum(m => m.Kosten);
            var uur = data.Uren.Where(u => u.WerkorderId == w.Id).Sum(u => u.Totaal);
            var zonder = data.ZonderTarief.Where(z => z.WerkorderId == w.Id).Sum(z => z.Uren);
            var b = budget.GetValueOrDefault(w.Id);
            tb += b; ti += inkoop; tm += mat; tu += uur; tz += zonder;
            g.Rijen.Add(new() { w.Nummer, w.Omschrijving, B(b), B(inkoop), B(mat), B(uur), B(inkoop + mat + uur), U(zonder) });
        }
        g.Opmerking = $"Project {project.Nummer} {project.Omschrijving} ({project.Klant}). Totaal: budget {B(tb)}, inkoop {B(ti)}, materiaal {B(tm)}, " +
                      $"uurkosten {B(tu)}, totaal kosten {B(ti + tm + tu)}. {U(tz)} uur van werknemers zonder enig kostprijstarief is niet meegeteld; uren zonder tarief voor die datum tellen tegen het laatst bekende tarief. " +
                      "Inkoop = per bestelling het gefactureerde bedrag, anders het bestelde. Budget 0 = niet ingevuld. Geen omzet in deze opvraging.";
    }
}
