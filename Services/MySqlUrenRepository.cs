using System.Data.Common;
using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Leest/schrijft de MDE-gegevens rechtstreeks uit MySQL (of MariaDB).
///
/// ROBUUST TEGEN KOLOMNAMEN: bij het eerste gebruik leest de repository de echte
/// tabel- en kolomnamen uit information_schema. Voor elk logisch veld worden
/// meerdere schrijfwijzen geprobeerd (bijv. "Werknemer-id", "werknemer_id",
/// "werknemerid"). Zo werkt de app of de kopie nu de originele Access-namen heeft
/// behouden óf genormaliseerde namen gekregen heeft — zonder handmatig aanpassen.
///
/// Klopt er iets niet? Open /diag in de app: die toont de echte tabellen en
/// kolommen. Ontbreekt een naam in de kandidatenlijsten hieronder, dan voeg je
/// hem daar toe.
/// </summary>
public class MySqlUrenRepository : IUrenRepository
{
    private readonly string _cs;
    private readonly object _lock = new();
    private Dictionary<string, Dictionary<string, string>>? _schema; // tabel -> (kolom -> echte kolomnaam)
    private Dictionary<string, string>? _tabellen;                    // tabelnaam -> echte tabelnaam

    public MySqlUrenRepository(string connectionString) => _cs = connectionString;

    // ---------- schema-ontdekking ----------
    private void EnsureSchema()
    {
        if (_schema != null) return;
        lock (_lock)
        {
            if (_schema != null) return;
            var schema = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var tabellen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var cn = new MySqlConnection(_cs);
            cn.Open();
            using var cmd = new MySqlCommand(
                "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = DATABASE()", cn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var t = r.GetString(0);
                var c = r.GetString(1);
                tabellen[t] = t;
                if (!schema.TryGetValue(t, out var cols))
                {
                    cols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    schema[t] = cols;
                }
                cols[c] = c;
            }
            _tabellen = tabellen;
            _schema = schema;
        }
    }

    private string Tbl(params string[] kandidaten)
    {
        EnsureSchema();
        foreach (var k in kandidaten)
            if (_tabellen!.TryGetValue(k, out var echt)) return echt;
        throw new InvalidOperationException(
            $"Tabel niet gevonden (gezocht: {string.Join(", ", kandidaten)}). Open /diag om de echte namen te zien.");
    }

    /// <summary>Zoekt een tabel op; geeft null als die niet bestaat (i.p.v. een fout).</summary>
    private string? TblOpt(params string[] kandidaten)
    {
        EnsureSchema();
        foreach (var k in kandidaten)
            if (_tabellen!.TryGetValue(k, out var echt)) return echt;
        return null;
    }

    private string? Col(string tabel, params string[] kandidaten)
    {
        if (_schema!.TryGetValue(tabel, out var cols))
            foreach (var k in kandidaten)
                if (cols.TryGetValue(k, out var echt)) return echt;
        return null;
    }

    private string ColReq(string tabel, params string[] kandidaten)
        => Col(tabel, kandidaten) ?? throw new InvalidOperationException(
            $"Kolom niet gevonden in '{tabel}' (gezocht: {string.Join(", ", kandidaten)}). Open /diag.");

    private MySqlConnection Verbind()
    {
        var cn = new MySqlConnection(_cs);
        cn.Open();
        return cn;
    }

    // ---------- conversie-helpers ----------
    private static bool Leeg(object? v) => v is null || v is DBNull;
    private static DateOnly D(object? v) => DateOnly.FromDateTime(Convert.ToDateTime(v));
    private static DateOnly? DN(object? v) => Leeg(v) ? null : DateOnly.FromDateTime(Convert.ToDateTime(v));
    private static TimeOnly? TN(object? v)
    {
        if (Leeg(v)) return null;
        if (v is TimeSpan ts) return TimeOnly.FromTimeSpan(ts);
        if (v is DateTime dt) return TimeOnly.FromDateTime(dt);
        return TimeOnly.TryParse(v!.ToString(), out var t) ? t : null;
    }
    private static decimal Dec(object? v) => Leeg(v) ? 0m : Convert.ToDecimal(v);
    private static int I(object? v) => Leeg(v) ? 0 : Convert.ToInt32(v);
    private static int? IN(object? v) => Leeg(v) ? null : Convert.ToInt32(v);
    private static bool B(object? v)
    {
        if (Leeg(v)) return false;
        try { return Convert.ToBoolean(v); } catch { return Convert.ToInt64(v) != 0; }
    }
    private static string S(object? v) => Leeg(v) ? "" : v!.ToString()!;

    // ============================================================
    //  WERKNEMERS
    // ============================================================
    /// <summary>Standaardlijst: in dienst, geen ingeleenden, alleen personen.</summary>
    public Task<List<Werknemer>> GetWerknemersAsync()
        => WerknemersAsync(inclusiefIngeleend: false, inclusiefUitDienst: false, alleenPersonen: true, zoek: null);

    public Task<List<Werknemer>> ZoekWerknemersAsync(bool inclusiefIngeleend, bool inclusiefUitDienst,
                                                     bool alleenPersonen, string? zoek)
        => WerknemersAsync(inclusiefIngeleend, inclusiefUitDienst, alleenPersonen, zoek);

    private async Task<List<Werknemer>> WerknemersAsync(bool inclusiefIngeleend, bool inclusiefUitDienst,
                                                        bool alleenPersonen, string? zoek)
    {
        var t = Tbl("Werknemers", "werknemers");
        var id = ColReq(t, "Werknemer-id", "werknemer_id", "werknemerid", "id");
        var naam = Col(t, "Naam", "naam", "Volledige_naam", "volledige_naam", "Werknemer", "werknemer", "Voornaam");
        var dir = Col(t, "Directie", "directie");
        var hoofd = Col(t, "Hoofdactiviteit", "hoofdactiviteit");
        var wwk = Col(t, "woonwerkkilometers", "WoonWerkKilometers", "woonwerk_km");
        var tww = Col(t, "TextWoonWerk", "textwoonwerk", "text_woon_werk");
        var twh = Col(t, "TextWerkWoon", "textwerkwoon", "text_werk_woon");

        // Naam voluit: losse kolommen voor voor-/tussen-/achternaam samenvoegen.
        // Bestaat er al één kolom met de volledige naam, dan wordt die gebruikt.
        var volledig = Col(t, "NaamVoluit", "Naamvoluit", "naamvoluit",   // zo heet het in de MDE-database
                              "Volledige_naam", "volledige_naam", "VolledigeNaam",
                              "Naam_voluit", "naam_voluit", "Weergavenaam", "DisplayName");
        var voornaam = Col(t, "Voornaam", "voornaam", "Firstname", "firstname", "Voorletters", "voorletters");
        var tussen = Col(t, "Tussenvoegsel", "tussenvoegsel", "Voorvoegsel", "voorvoegsel", "Middlename");
        var achter = Col(t, "Achternaam", "achternaam", "Familienaam", "familienaam", "Lastname", "lastname");

        var sel = new List<string> { $"`{id}` AS id" };
        sel.Add(NaamExpressie(volledig, voornaam, tussen, achter, naam) + " AS naam");
        sel.Add(dir != null ? $"`{dir}` AS dir" : "0 AS dir");
        sel.Add(hoofd != null ? $"`{hoofd}` AS hoofd" : "NULL AS hoofd");
        sel.Add(wwk != null ? $"`{wwk}` AS wwk" : "0 AS wwk");
        sel.Add(tww != null ? $"`{tww}` AS tww" : "NULL AS tww");
        sel.Add(twh != null ? $"`{twh}` AS twh" : "NULL AS twh");

        // Filters (zoals de vinkjes op het Access-formulier).
        // ( <> 0 / = 0 zodat zowel 1/0 als Access-stijl -1/0 correct werken )
        var uitDienst = Col(t, "Uit dienst", "uit dienst", "UitDienst", "uitdienst", "uit_dienst");
        var persoon = Col(t, "Persoon", "persoon", "IsPersoon", "is_persoon");
        var ingeleend = Col(t, "Inleen", "inleen", "Ingeleend", "ingeleend");   // in MDE: 'Inleen'

        var filters = new List<string>();
        if (!inclusiefUitDienst && uitDienst != null)
            filters.Add($"(`{uitDienst}` = 0 OR `{uitDienst}` IS NULL)");
        if (!inclusiefIngeleend && ingeleend != null)
            filters.Add($"(`{ingeleend}` = 0 OR `{ingeleend}` IS NULL)");
        if (alleenPersonen && persoon != null)
            filters.Add($"`{persoon}` <> 0");
        if (!string.IsNullOrWhiteSpace(zoek))
            filters.Add($"{NaamExpressie(volledig, voornaam, tussen, achter, naam)} LIKE @zoek");

        var where = filters.Count > 0 ? " WHERE " + string.Join(" AND ", filters) : "";

        var list = new List<Werknemer>();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand($"SELECT {string.Join(",", sel)} FROM `{t}`{where}", cn);
        if (!string.IsNullOrWhiteSpace(zoek)) cmd.Parameters.AddWithValue("@zoek", "%" + zoek.Trim() + "%");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var wid = I(r["id"]);
            list.Add(new Werknemer
            {
                WerknemerId = wid,
                Naam = Leeg(r["naam"]) ? $"Werknemer {wid}" : S(r["naam"]),
                Directie = B(r["dir"]),
                Hoofdactiviteit = IN(r["hoofd"]),
                WoonWerkKilometers = Dec(r["wwk"]),
                TextWoonWerk = Leeg(r["tww"]) ? "Woon-werk" : S(r["tww"]),
                TextWerkWoon = Leeg(r["twh"]) ? "Werk-woon" : S(r["twh"]),
                Actief = true
            });
        }
        return list.OrderBy(w => w.Naam).ToList();
    }

    public async Task<Werknemer?> GetWerknemerAsync(int id)
        => (await GetWerknemersAsync()).FirstOrDefault(w => w.WerknemerId == id);

    /// <summary>
    /// Koppelt een ingelogd Google-account aan een werknemer. Zoekt in Werknemers naar
    /// een e-mail- of loginkolom; de vergelijking is ongevoelig voor hoofdletters.
    /// Bestaat er geen geschikte kolom, dan komt er null terug en valt de app terug op
    /// de knop 'dit ben ik'.
    /// </summary>
    public async Task<int?> VindWerknemerOpEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        try
        {
            var t = Tbl("Werknemers", "werknemers");
            var id = ColReq(t, "Werknemer-id", "werknemer_id", "werknemerid", "id");
            var mail = Col(t, "Email", "email", "E-mail", "e-mail", "Emailadres", "emailadres",
                              "EmailAdres", "Mail", "mail", "Loginnaam", "loginnaam",
                              "Login", "login", "Gebruikersnaam", "gebruikersnaam");
            if (mail == null) return null;

            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand(
                $"SELECT `{id}` FROM `{t}` WHERE LOWER(TRIM(`{mail}`)) = @m LIMIT 1", cn);
            cmd.Parameters.AddWithValue("@m", email.Trim().ToLowerInvariant());
            var res = await cmd.ExecuteScalarAsync();
            return Leeg(res) ? null : Convert.ToInt32(res);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Alle kolommen van één werknemer. Basis voor de rechten: voegen jullie later een
    /// kolom toe (bijv. MagFacturenGoedkeuren), dan is die meteen bruikbaar.
    /// </summary>
    public async Task<Dictionary<string, object?>> GetWerknemerVeldenAsync(int werknemerId)
    {
        var velden = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var t = Tbl("Werknemers", "werknemers");
            var id = ColReq(t, "Werknemer-id", "werknemer_id", "werknemerid", "id");

            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand($"SELECT * FROM `{t}` WHERE `{id}` = @id LIMIT 1", cn);
            cmd.Parameters.AddWithValue("@id", werknemerId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                for (var i = 0; i < r.FieldCount; i++)
                    velden[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        }
        catch { /* geen rechten beschikbaar: alles staat dan op 'niet toegestaan' */ }
        return velden;
    }

    public Task<HashSet<string>> WerknemerKolommenAsync()
    {
        var t = Tbl("Werknemers", "werknemers");
        return Task.FromResult(_schema![t].Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    public async Task UpdateWerknemerVeldAsync(int werknemerId, string kolom, object? waarde)
    {
        var t = Tbl("Werknemers", "werknemers");
        // Alleen kolommen die echt bestaan — voorkomt fouten en SQL-injectie
        var echt = Col(t, kolom) ?? throw new InvalidOperationException($"Kolom '{kolom}' bestaat niet in {t}.");
        var id = ColReq(t, "Werknemer-id", "werknemer_id", "werknemerid", "id");

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand($"UPDATE `{t}` SET `{echt}` = @w WHERE `{id}` = @id", cn);
        cmd.Parameters.AddWithValue("@w", waarde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", werknemerId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Bouwt de SQL-expressie voor de naam. Voorkeur: één kolom met de volledige naam.
    /// Anders voornaam + tussenvoegsel + achternaam netjes aan elkaar (lege delen worden
    /// overgeslagen, geen dubbele spaties). Als laatste redmiddel de losse 'naam'-kolom.
    /// </summary>
    private static string NaamExpressie(string? volledig, string? voornaam, string? tussen,
                                        string? achter, string? naam)
    {
        if (volledig != null) return $"`{volledig}`";

        var delen = new List<string>();
        if (voornaam != null) delen.Add($"`{voornaam}`");
        else if (naam != null) delen.Add($"`{naam}`");   // 'Naam' bevat vaak de voornaam
        if (tussen != null) delen.Add($"`{tussen}`");
        if (achter != null) delen.Add($"`{achter}`");

        if (delen.Count == 0) return "NULL";
        if (delen.Count == 1) return delen[0];

        // CONCAT_WS negeert NULL-waarden; NULLIF(...,'') maakt ook lege tekst tot NULL,
        // zodat er geen dubbele spaties ontstaan.
        var args = string.Join(", ", delen.Select(d => $"NULLIF(TRIM({d}), '')"));
        return $"TRIM(CONCAT_WS(' ', {args}))";
    }

    // ============================================================
    //  WORKORDERS  (Workorders + Projects)
    //
    //  Structuur volgens de Access-queries:
    //    Workorders.[Project-id] = Projects.[Project-Id]
    //    werkordernummer = Projects.Projectnumber & "-" & Right("00" & Workorders.WerkbonNummer, 2)
    //    omschrijving    = Workorders.Werkbonomschrijving
    // ============================================================

    /// <summary>Bouwt SELECT-onderdelen en FROM/JOIN voor werkorders (met projectnummer).</summary>
    private (string sel, string from, string? statusCol, string idCol, string zoekExpr) WorkorderQuery()
    {
        var w = Tbl("Workorders", "workorders", "Werkorders", "werkorders");
        var id = ColReq(w, "Werkbon-id", "werkbon_id", "werkbonid", "Werkorder-id", "werkorder_id", "id");
        var oms = Col(w, "Werkbonomschrijving", "werkbonomschrijving", "WerkbonOmschrijving",
                         "Omschrijving", "omschrijving", "Beschrijving", "beschrijving");
        var klant = Col(w, "Klant", "klant", "Klantnaam", "klantnaam", "Debiteur", "debiteur");
        var status = Col(w, "WorkorderStatus", "workorderstatus", "workorder_status", "Status", "status");
        var bon = Col(w, "WerkbonNummer", "Werkbonnummer", "werkbonnummer", "Werkbonnr", "werkbonnr",
                         "Bonnummer", "bonnummer");
        var projFk = Col(w, "Project-id", "Project-Id", "project_id", "projectid", "Projectid");
        var inkoop = Col(w, "InkoopToegestaan", "inkooptoegestaan");

        // Projecttabel (optioneel) voor het projectnummer
        var p = TblOpt("Projects", "projects", "Projecten", "projecten");
        string? projNr = null, projOms = null, projPk = null, projKlant = null;
        if (p != null)
        {
            projKlant = Col(p, "Customer-id", "Customer-Id", "customer_id");
            projNr = Col(p, "Projectnumber", "projectnumber", "Projectnummer", "projectnummer",
                            "Project_number", "project_number", "Projectnr", "projectnr");
            projOms = Col(p, "Projectdescription", "projectdescription", "Projectomschrijving",
                             "projectomschrijving", "Omschrijving", "omschrijving");
            projPk = Col(p, "Project-Id", "Project-id", "project_id", "projectid", "id");
        }

        var kanJoinen = p != null && projFk != null && projPk != null;

        // nummer-expressie (letterlijke vertaling van de Access-uitdrukking)
        string nummer;
        if (kanJoinen && projNr != null && bon != null)
            nummer = $"CONCAT(p.`{projNr}`, '-', RIGHT(CONCAT('00', w.`{bon}`), 2))";
        else if (bon != null)
            nummer = $"RIGHT(CONCAT('00', w.`{bon}`), 2)";
        else
            nummer = "NULL";

        // projectnaam (voor weergave bij de werkorder)
        string projectExpr = (kanJoinen && projOms != null) ? $"p.`{projOms}`" : "NULL";
        // Klant: een eigen kolom in Workorders, anders via het project (Projects.Customer-id -> Relations.name)
        var r = kanJoinen && klant == null && projKlant != null ? TblOpt("Relations", "relations") : null;
        string klantExpr = klant != null ? $"w.`{klant}`" : r != null ? "k.`name`" : "NULL";

        var sel = string.Join(",", new[]
        {
            $"w.`{id}` AS id",
            oms != null ? $"w.`{oms}` AS oms" : "NULL AS oms",
            $"{klantExpr} AS klant",
            $"{projectExpr} AS project",
            status != null ? $"w.`{status}` AS status" : "1 AS status",
            $"{nummer} AS nummer",
            inkoop != null ? $"COALESCE(w.`{inkoop}`, 0) AS inkoop" : "1 AS inkoop"
        });

        var from = kanJoinen
            ? $"`{w}` w LEFT JOIN `{p}` p ON w.`{projFk}` = p.`{projPk}`"
              + (r != null ? $" LEFT JOIN `{r}` k ON k.`id_supplier` = p.`{projKlant}`" : "")
            : $"`{w}` w";

        // Alles waarop gezocht mag worden, in één tekst (nummer, omschrijving, project, klant)
        var zoekDelen = new List<string> { nummer };
        if (oms != null) zoekDelen.Add($"w.`{oms}`");
        if (kanJoinen && projOms != null) zoekDelen.Add($"p.`{projOms}`");
        if (kanJoinen && projNr != null) zoekDelen.Add($"p.`{projNr}`");
        if (klant != null) zoekDelen.Add($"w.`{klant}`");
        else if (r != null) zoekDelen.Add("k.`name`");
        var zoekExpr = $"CONCAT_WS(' ', {string.Join(", ", zoekDelen)})";

        return (sel, from, status, id, zoekExpr);
    }

    /// <summary>Haalt werkorders op bij een lijst id's (volgorde blijft die van de lijst).</summary>
    private async Task<List<Workorder>> WerkordersBijIdsAsync(List<int> ids)
    {
        var resultaat = new List<Workorder>();
        if (ids.Count == 0) return resultaat;

        var q = WorkorderQuery();
        var pars = string.Join(",", ids.Select((_, i) => $"@i{i}"));

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT {q.sel} FROM {q.from} WHERE w.`{q.idCol}` IN ({pars})", cn);
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue($"@i{i}", ids[i]);

        var gevonden = new Dictionary<int, Workorder>();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var wo = MapWorkorder(r);
                gevonden[wo.WerkbonId] = wo;
            }

        foreach (var id in ids)
            if (gevonden.TryGetValue(id, out var wo)) resultaat.Add(wo);
        return resultaat;
    }

    public async Task<List<Workorder>> ZoekWerkordersAsync(string zoek, bool alleenOpen, int max)
    {
        var q = WorkorderQuery();
        var voorwaarden = new List<string>();
        if (alleenOpen && q.statusCol != null) voorwaarden.Add($"w.`{q.statusCol}` <> 0");
        // alle woorden moeten voorkomen (bv. "tembo robot"), in willekeurige volgorde
        var woorden = (zoek ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6).ToArray();
        for (var i = 0; i < woorden.Length; i++) voorwaarden.Add($"{q.zoekExpr} LIKE @z{i}");
        var where = voorwaarden.Count > 0 ? "WHERE " + string.Join(" AND ", voorwaarden) : "";

        var list = new List<Workorder>();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT {q.sel} FROM {q.from} {where} ORDER BY w.`{q.idCol}` DESC LIMIT {Math.Clamp(max, 1, 500)}", cn);
        for (var i = 0; i < woorden.Length; i++)
            cmd.Parameters.AddWithValue($"@z{i}", "%" + woorden[i] + "%");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(MapWorkorder(r));
        return list;
    }

    public async Task<List<Workorder>> WerkordersVanKlantAsync(int klantId, bool alleenOpen, int max)
    {
        var q = WorkorderQuery();
        var list = new List<Workorder>();
        if (!q.from.Contains(" p ON ")) return list;          // geen projecttabel: klant onbekend
        var where = "WHERE p.`Customer-id` = @k" + (alleenOpen && q.statusCol != null ? $" AND w.`{q.statusCol}` <> 0" : "");
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT {q.sel} FROM {q.from} {where} ORDER BY w.`{q.idCol}` DESC LIMIT {Math.Clamp(max, 1, 500)}", cn);
        cmd.Parameters.AddWithValue("@k", klantId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(MapWorkorder(r));
        return list;
    }

    public async Task<List<Workorder>> RecentGebruikteWerkordersAsync(int werknemerId, int dagen, int max)
    {
        var ids = new List<int>();
        try
        {
            var t = Tbl("Uren", "uren");
            var wn = ColReq(t, "Werknemer-id", "werknemer_id", "werknemerid");
            var sd = ColReq(t, "StartDatum", "startdatum", "start_datum");
            var wo = Col(t, "werkorder", "Werkorder", "werkorder_id", "Werkbon-id", "werkbon_id");
            if (wo == null) return new List<Workorder>();

            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand(
                $"SELECT `{wo}` AS id, MAX(`{sd}`) AS laatst FROM `{t}` " +
                $"WHERE `{wn}` = @wn AND `{wo}` IS NOT NULL AND `{sd}` >= @vanaf " +
                $"GROUP BY `{wo}` ORDER BY laatst DESC LIMIT {Math.Clamp(max, 1, 200)}", cn);
            cmd.Parameters.AddWithValue("@wn", werknemerId);
            cmd.Parameters.AddWithValue("@vanaf", DateTime.Today.AddDays(-Math.Abs(dagen)));
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) ids.Add(I(r["id"]));
        }
        catch { return new List<Workorder>(); }

        return await WerkordersBijIdsAsync(ids);
    }

    public async Task<List<Workorder>> PersoonlijkeWerkordersAsync(int werknemerId)
    {
        var ids = new List<int>();
        try
        {
            var t = Tbl("Personal_Orders", "personal_orders", "PersonalOrders", "personalorders");
            var orderCol = ColReq(t, "order-id", "Order-id", "order_id", "orderid", "Werkbon-id", "werkbon_id");
            var mens = ColReq(t, "Employee-id", "employee-id", "employee_id", "employeeid",
                                 "Werknemer-id", "werknemer_id");

            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand(
                $"SELECT `{orderCol}` AS id FROM `{t}` WHERE `{mens}` = @wn", cn);
            cmd.Parameters.AddWithValue("@wn", werknemerId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) if (!Leeg(r["id"])) ids.Add(I(r["id"]));
        }
        catch { return new List<Workorder>(); }

        return await WerkordersBijIdsAsync(ids);
    }

    public async Task ZetPersoonlijkeWerkorderAsync(int werknemerId, int werkbonId, bool toevoegen)
    {
        var t = Tbl("Personal_Orders", "personal_orders", "PersonalOrders", "personalorders");
        var orderCol = ColReq(t, "order-id", "Order-id", "order_id", "orderid", "Werkbon-id", "werkbon_id");
        var mens = ColReq(t, "Employee-id", "employee-id", "employee_id", "employeeid",
                             "Werknemer-id", "werknemer_id");

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();

        if (toevoegen)
        {
            await using var check = new MySqlCommand(
                $"SELECT COUNT(*) FROM `{t}` WHERE `{mens}` = @wn AND `{orderCol}` = @wo", cn);
            check.Parameters.AddWithValue("@wn", werknemerId);
            check.Parameters.AddWithValue("@wo", werkbonId);
            if (Convert.ToInt64(await check.ExecuteScalarAsync()) > 0) return;   // stond er al

            await using var ins = new MySqlCommand(
                $"INSERT INTO `{t}` (`{mens}`, `{orderCol}`) VALUES (@wn, @wo)", cn);
            ins.Parameters.AddWithValue("@wn", werknemerId);
            ins.Parameters.AddWithValue("@wo", werkbonId);
            await ins.ExecuteNonQueryAsync();
        }
        else
        {
            await using var del = new MySqlCommand(
                $"DELETE FROM `{t}` WHERE `{mens}` = @wn AND `{orderCol}` = @wo", cn);
            del.Parameters.AddWithValue("@wn", werknemerId);
            del.Parameters.AddWithValue("@wo", werkbonId);
            await del.ExecuteNonQueryAsync();
        }
    }

    private static Workorder MapWorkorder(DbDataReader r) => new()
    {
        WerkbonId = I(r["id"]),
        Omschrijving = S(r["oms"]),
        Klant = S(r["klant"]),
        WorkorderStatus = I(r["status"]),
        Nummer = S(r["nummer"]),
        Project = S(r["project"]),
        InkoopToegestaan = I(r["inkoop"]) != 0
    };

    public async Task<List<Workorder>> GetOpenWorkordersAsync()
    {
        var q = WorkorderQuery();
        var where = q.statusCol != null ? $"WHERE w.`{q.statusCol}` <> 0" : "";
        var order = $"ORDER BY nummer";

        var list = new List<Workorder>();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand($"SELECT {q.sel} FROM {q.from} {where} {order}", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(MapWorkorder(r));
        return list;
    }

    public Task<List<Workorder>> GetWorkordersAsync(IReadOnlyCollection<int> ids) => WerkordersBijIdsAsync(ids.Distinct().ToList());

    public async Task<Workorder?> GetWorkorderAsync(int id)
    {
        var q = WorkorderQuery();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT {q.sel} FROM {q.from} WHERE w.`{q.idCol}` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return MapWorkorder(r);
    }

    // ============================================================
    //  UREN
    // ============================================================
    private (string cols, string t, string wnCol, string sdCol) UrenMeta()
    {
        var t = Tbl("Uren", "uren");
        var uid = ColReq(t, "Uren-id", "uren_id", "urenid", "id");
        var sd = ColReq(t, "StartDatum", "startdatum", "start_datum");
        var wn = ColReq(t, "Werknemer-id", "werknemer_id", "werknemerid");
        string? st = Col(t, "StartTijd", "starttijd", "start_tijd");
        string? ed = Col(t, "EindDatum", "einddatum", "eind_datum");
        string? et = Col(t, "Eindtijd", "eindtijd", "eind_tijd");
        string? uren = Col(t, "Uren", "uren", "aantal_uren");
        string? wo = Col(t, "werkorder", "Werkorder", "werkorder_id", "Werkbon-id", "werkbon_id");
        string? km = Col(t, "Kilometers", "kilometers");
        string? pkm = Col(t, "PriveKMs", "privekms", "prive_kms");
        string? oms = Col(t, "Omschrijving", "omschrijving");
        string? ts = Col(t, "Tariefsoort-id", "tariefsoort_id", "tariefsoortid");
        string? fa = Col(t, "Fixed_Amount", "fixed_amount", "fixedamount");
        string? ft = Col(t, "FixedType-ID", "fixedtype_id", "fixedtypeid");
        string? inv = Col(t, "Invoiced", "invoiced");
        string? act = Col(t, "UrenActiviteitID", "urenactiviteitid", "uren_activiteit_id");
        string? src = Col(t, "SourceUrenID", "sourceurenid", "source_uren_id");
        string? gbr = Col(t, "GeneratedByRule", "generatedbyrule", "generated_by_rule");

        string F(string? c, string alias, string leeg) => c != null ? $"`{c}` AS {alias}" : $"{leeg} AS {alias}";
        var cols = string.Join(",", new[]
        {
            $"`{uid}` AS uid", $"`{sd}` AS sd", F(st,"st","NULL"), F(ed,"ed","NULL"), F(et,"et","NULL"),
            $"`{wn}` AS wn", F(uren,"uren","0"), F(wo,"wo","NULL"), F(km,"km","0"), F(pkm,"pkm","0"),
            F(oms,"oms","NULL"), F(ts,"ts","0"), F(fa,"fa","0"), F(ft,"ft","0"), F(inv,"inv","0"),
            F(act,"act","NULL"), F(src,"src","NULL"), F(gbr,"gbr","0")
        });
        return (cols, t, wn, sd);
    }

    private static UrenEntry MapUren(DbDataReader r) => new()
    {
        UrenId = I(r["uid"]),
        StartDatum = D(r["sd"]),
        StartTijd = TN(r["st"]),
        EindDatum = DN(r["ed"]),
        EindTijd = TN(r["et"]),
        WerknemerId = I(r["wn"]),
        Uren = Dec(r["uren"]),
        Werkorder = IN(r["wo"]),
        Kilometers = Dec(r["km"]),
        PriveKMs = B(r["pkm"]),
        Omschrijving = S(r["oms"]),
        TariefsoortId = I(r["ts"]),
        FixedAmount = Dec(r["fa"]),
        FixedTypeId = I(r["ft"]),
        Invoiced = B(r["inv"]),
        UrenActiviteitId = IN(r["act"]),
        SourceUrenId = IN(r["src"]),
        GeneratedByRule = B(r["gbr"])
    };

    public async Task<List<UrenEntry>> GetUrenAsync(int werknemerId, DateOnly van, DateOnly tot)
    {
        var m = UrenMeta();
        var list = new List<UrenEntry>();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT {m.cols} FROM `{m.t}` WHERE `{m.wnCol}` = @wn AND `{m.sdCol}` BETWEEN @van AND @tot ORDER BY `{m.sdCol}`", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        cmd.Parameters.AddWithValue("@van", van.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tot", tot.ToDateTime(TimeOnly.MinValue));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(MapUren(r));
        return list.OrderBy(u => u.StartDatum).ThenBy(u => u.StartTijd).ToList();
    }

    public Task<List<UrenEntry>> GetUrenVanDagAsync(int werknemerId, DateOnly dag)
        => GetUrenAsync(werknemerId, dag, dag);

    public async Task<List<UrenEntry>> GetAlleUrenAsync(DateOnly van, DateOnly tot)
    {
        var m = UrenMeta();
        var list = new List<UrenEntry>();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT {m.cols} FROM `{m.t}` WHERE `{m.sdCol}` BETWEEN @van AND @tot", cn);
        cmd.Parameters.AddWithValue("@van", van.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tot", tot.ToDateTime(TimeOnly.MinValue));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(MapUren(r));
        return list;
    }

    // schrijfvelden: alleen kolommen die bestaan worden meegenomen
    private List<(string col, object? val)> UrenSchrijfVelden(string t, UrenEntry e)
    {
        var defs = new (string[] cands, object? val)[]
        {
            (new[]{"StartDatum","startdatum","start_datum"}, e.StartDatum.ToDateTime(TimeOnly.MinValue)),
            (new[]{"StartTijd","starttijd","start_tijd"}, (object?)e.StartTijd?.ToTimeSpan()),
            (new[]{"EindDatum","einddatum","eind_datum"}, (object?)e.EindDatum?.ToDateTime(TimeOnly.MinValue)),
            (new[]{"Eindtijd","eindtijd","eind_tijd"}, (object?)e.EindTijd?.ToTimeSpan()),
            (new[]{"Werknemer-id","werknemer_id","werknemerid"}, e.WerknemerId),
            (new[]{"Uren","uren","aantal_uren"}, e.Uren),
            (new[]{"werkorder","Werkorder","werkorder_id","Werkbon-id","werkbon_id"}, (object?)e.Werkorder),
            (new[]{"Kilometers","kilometers"}, e.Kilometers),
            (new[]{"PriveKMs","privekms","prive_kms"}, e.PriveKMs),
            (new[]{"Omschrijving","omschrijving"}, e.Omschrijving),
            (new[]{"Tariefsoort-id","tariefsoort_id","tariefsoortid"}, e.TariefsoortId),
            (new[]{"Fixed_Amount","fixed_amount","fixedamount"}, e.FixedAmount),
            (new[]{"FixedType-ID","fixedtype_id","fixedtypeid"}, e.FixedTypeId),
            (new[]{"UrenActiviteitID","urenactiviteitid","uren_activiteit_id"}, (object?)e.UrenActiviteitId),
            (new[]{"GeneratedByRule","generatedbyrule","generated_by_rule"}, e.GeneratedByRule),
        };
        var list = new List<(string, object?)>();
        foreach (var d in defs)
        {
            var c = Col(t, d.cands);
            if (c != null) list.Add((c, d.val));
        }
        return list;
    }

    public async Task<UrenEntry> AddAsync(UrenEntry e)
    {
        var t = Tbl("Uren", "uren");
        var velden = UrenSchrijfVelden(t, e);
        var kolommen = string.Join(",", velden.Select(f => $"`{f.col}`"));
        var pars = string.Join(",", velden.Select((_, i) => $"@p{i}"));

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"INSERT INTO `{t}` ({kolommen}) VALUES ({pars})", cn);
        for (int i = 0; i < velden.Count; i++)
            cmd.Parameters.AddWithValue($"@p{i}", velden[i].val ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        e.UrenId = (int)cmd.LastInsertedId;
        return e;
    }

    public async Task UpdateAsync(UrenEntry e)
    {
        var t = Tbl("Uren", "uren");
        var uid = ColReq(t, "Uren-id", "uren_id", "urenid", "id");
        var velden = UrenSchrijfVelden(t, e);
        var set = string.Join(",", velden.Select((f, i) => $"`{f.col}` = @p{i}"));

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand($"UPDATE `{t}` SET {set} WHERE `{uid}` = @id", cn);
        for (int i = 0; i < velden.Count; i++)
            cmd.Parameters.AddWithValue($"@p{i}", velden[i].val ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", e.UrenId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int urenId)
    {
        var t = Tbl("Uren", "uren");
        var uid = ColReq(t, "Uren-id", "uren_id", "urenid", "id");
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand($"DELETE FROM `{t}` WHERE `{uid}` = @id", cn);
        cmd.Parameters.AddWithValue("@id", urenId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  LOOKUPS — live proberen, bij afwijkend schema terugvallen op standaardwaarden
    //  (zodat de dropdowns blijven werken; namen sluiten aan op de VBA-logica)
    // ============================================================
    public async Task<List<Tariefsoort>> GetTariefsoortenAsync()
    {
        try
        {
            var t = Tbl("Tariefsoort", "tariefsoort", "Tariefsoorten", "tariefsoorten");
            var id = ColReq(t, "Tariefsoort-id", "tariefsoort_id", "tariefsoortid", "id");
            // In de MDE-database heet de naamkolom óók 'Tariefsoort'
            var naam = Col(t, "Tariefsoort", "tariefsoort", "Naam", "naam", "Omschrijving", "omschrijving");
            var list = new List<Tariefsoort>();
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand(
                $"SELECT `{id}` AS id, {(naam != null ? $"`{naam}`" : "NULL")} AS naam FROM `{t}`", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new Tariefsoort { TariefsoortId = I(r["id"]), Naam = S(r["naam"]) });
            if (list.Count > 0) return list;
        }
        catch { /* val terug op standaard */ }

        return new()
        {
            new() { TariefsoortId = 1, Naam = "Travel" },
            new() { TariefsoortId = 2, Naam = "Normaal" },
            new() { TariefsoortId = 3, Naam = "Overuren 125%" },
            new() { TariefsoortId = 4, Naam = "Overuren 150%" },
            new() { TariefsoortId = 5, Naam = "Fixed" },
        };
    }

    public async Task<List<UrenActiviteit>> GetActiviteitenAsync()
    {
        try
        {
            var t = Tbl("UrenActiviteit", "urenactiviteit", "UrenActiviteiten", "urenactiviteiten");
            var id = ColReq(t, "UrenActiviteitID", "urenactiviteitid", "uren_activiteit_id", "id");
            // In de MDE-database heet de naamkolom óók 'UrenActiviteit'
            var naam = Col(t, "UrenActiviteit", "urenactiviteit", "Naam", "naam",
                              "Omschrijving", "omschrijving", "Activiteit", "activiteit");
            var list = new List<UrenActiviteit>();
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand(
                $"SELECT `{id}` AS id, {(naam != null ? $"`{naam}`" : "NULL")} AS naam FROM `{t}`", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new UrenActiviteit { UrenActiviteitId = I(r["id"]), Naam = S(r["naam"]) });
            if (list.Count > 0) return list;
        }
        catch { }

        return new()
        {
            new() { UrenActiviteitId = 1, Naam = "Management" },
            new() { UrenActiviteitId = 2, Naam = "Paneelbouw" },
            new() { UrenActiviteitId = 3, Naam = "Engineering" },
            new() { UrenActiviteitId = 4, Naam = "Service" },
            new() { UrenActiviteitId = 13, Naam = "Woon-werk verkeer" },
        };
    }

    public Task<List<SpecialOrder>> GetSpecialOrdersAsync()
        => Task.FromResult(new List<SpecialOrder>
        {
            new() { Id = 1, Order = 90003, Remark = "Verlof" },
            new() { Id = 2, Order = 90004, Remark = "Ziek" },
        });

    public Task<GeneralSettings> GetGeneralSettingsAsync()
        => Task.FromResult(new GeneralSettings { ManagementKilometerOrder = 90002 });

    // ============================================================
    //  PLANNING — leest uit Planning_Schema.sql als die tabellen bestaan;
    //  anders lege lijst (dan draait de planning nog op niets, geen crash).
    // ============================================================
    public async Task<List<PlanningItem>> GetPlanningAsync()
    {
        try
        {
            var t = Tbl("Planning", "planning");
            var id = ColReq(t, "PlanningId", "planningid", "planning_id", "id");
            var wn = ColReq(t, "WerknemerId", "werknemerid", "werknemer_id", "Werknemer-id");
            var wo = Col(t, "WerkorderId", "werkorderid", "werkorder_id", "Werkbon-id");
            var sd = ColReq(t, "Startdatum", "startdatum", "start_datum");
            var ed = ColReq(t, "Einddatum", "einddatum", "eind_datum");
            var uren = Col(t, "GeplandeUren", "geplandeuren", "geplande_uren");
            var not = Col(t, "Notitie", "notitie");

            var sel = new List<string>
            {
                $"`{id}` AS id", $"`{wn}` AS wn",
                wo != null ? $"`{wo}` AS wo" : "NULL AS wo",
                $"`{sd}` AS sd", $"`{ed}` AS ed",
                uren != null ? $"`{uren}` AS uren" : "0 AS uren",
                not != null ? $"`{not}` AS notitie" : "'' AS notitie"
            };
            var list = new List<PlanningItem>();
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand($"SELECT {string.Join(",", sel)} FROM `{t}`", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new PlanningItem
                {
                    Id = I(r["id"]),
                    WerknemerId = I(r["wn"]),
                    Werkorder = I(r["wo"]),
                    Start = D(r["sd"]),
                    Eind = D(r["ed"]),
                    GeplandeUren = Dec(r["uren"]),
                    Notitie = S(r["notitie"])
                });
            return list.OrderBy(p => p.WerknemerId).ThenBy(p => p.Start).ToList();
        }
        catch
        {
            return new List<PlanningItem>(); // Planning_Schema.sql nog niet uitgevoerd
        }
    }

    public async Task<PlanningItem> AddPlanningAsync(PlanningItem item)
    {
        var t = Tbl("Planning", "planning");
        var wn = ColReq(t, "WerknemerId", "werknemerid", "werknemer_id");
        var wo = ColReq(t, "WerkorderId", "werkorderid", "werkorder_id");
        var sd = ColReq(t, "Startdatum", "startdatum", "start_datum");
        var ed = ColReq(t, "Einddatum", "einddatum", "eind_datum");
        var uren = ColReq(t, "GeplandeUren", "geplandeuren", "geplande_uren");
        var not = ColReq(t, "Notitie", "notitie");
        var typ = Col(t, "PlanningTypeId", "planningtypeid", "planning_type_id");

        var cols = new List<string> { wn, wo, sd, ed, uren, not };
        var vals = new List<object?> { item.WerknemerId, item.Werkorder,
            item.Start.ToDateTime(TimeOnly.MinValue), item.Eind.ToDateTime(TimeOnly.MinValue),
            item.GeplandeUren, item.Notitie };
        if (typ != null) { cols.Add(typ); vals.Add(1); }

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"INSERT INTO `{t}` ({string.Join(",", cols.Select(c => $"`{c}`"))}) " +
            $"VALUES ({string.Join(",", cols.Select((_, i) => $"@p{i}"))})", cn);
        for (int i = 0; i < vals.Count; i++) cmd.Parameters.AddWithValue($"@p{i}", vals[i] ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        item.Id = (int)cmd.LastInsertedId;
        return item;
    }

    public async Task UpdatePlanningAsync(PlanningItem item)
    {
        var t = Tbl("Planning", "planning");
        var id = ColReq(t, "PlanningId", "planningid", "planning_id", "id");
        var wn = ColReq(t, "WerknemerId", "werknemerid", "werknemer_id");
        var wo = ColReq(t, "WerkorderId", "werkorderid", "werkorder_id");
        var sd = ColReq(t, "Startdatum", "startdatum", "start_datum");
        var ed = ColReq(t, "Einddatum", "einddatum", "eind_datum");
        var uren = ColReq(t, "GeplandeUren", "geplandeuren", "geplande_uren");
        var not = ColReq(t, "Notitie", "notitie");

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"UPDATE `{t}` SET `{wn}`=@wn, `{wo}`=@wo, `{sd}`=@sd, `{ed}`=@ed, `{uren}`=@uren, `{not}`=@not WHERE `{id}`=@id", cn);
        cmd.Parameters.AddWithValue("@wn", item.WerknemerId);
        cmd.Parameters.AddWithValue("@wo", item.Werkorder);
        cmd.Parameters.AddWithValue("@sd", item.Start.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@ed", item.Eind.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@uren", item.GeplandeUren);
        cmd.Parameters.AddWithValue("@not", item.Notitie);
        cmd.Parameters.AddWithValue("@id", item.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeletePlanningAsync(int id)
    {
        var t = Tbl("Planning", "planning");
        var idCol = ColReq(t, "PlanningId", "planningid", "planning_id", "id");
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand($"DELETE FROM `{t}` WHERE `{idCol}` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
