using System.Globalization;
using System.Text;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Een rij van de planning: een groep of een activiteit. De rijen vormen een boom (<see cref="OuderId"/>); een rij met
/// rijen eronder is een groep. Een groep kan later aan een klant, project of werkorder hangen, zoals een Activity board.
/// </summary>
public class PlanRij
{
    public int Id { get; set; }
    public int? OuderId { get; set; }
    public int Volgorde { get; set; }
    public string Naam { get; set; } = "";
    /// <summary>Wie het doet, als vrije tekst zoals in Tom's Planner ("Gerard", "Franke, Verschuure", "Mechanical Vacancy").</summary>
    public string Resource { get; set; } = "";
    /// <summary>Het rijnummer uit Tom's Planner ("24.3.1"); alleen om terug te vinden waar een rij vandaan kwam.</summary>
    public string Nummer { get; set; } = "";
    public int? RelatieId { get; set; }
    public int? ProjectId { get; set; }
    public int? WerkorderId { get; set; }
    /// <summary>Waar de rij aan gekoppeld is, kort (projectnummer, werkordernummer of de naam van de klant); leeg = niet gekoppeld.</summary>
    public string KoppelKort { get; set; } = "";
    /// <summary>Hetzelfde voluit: "Project 223159 · Multipede".</summary>
    public string KoppelTekst { get; set; } = "";
    /// <summary>Het project om naartoe te springen: het gekoppelde project, of dat van de gekoppelde werkorder.</summary>
    public int? KoppelProjectId { get; set; }
    public bool Gekoppeld => RelatieId is not null || ProjectId is not null || WerkorderId is not null;
    /// <summary>
    /// De werknemers die aan de rij gekoppeld zijn (stap 4, tabel PlanbordRijWerknemers). <see cref="Resource"/> is
    /// daarnaast de vrije tekst voor wie geen werknemer is ("Werkplaats", "Software Vacancy", "Omron").
    /// </summary>
    public List<int> Werknemers { get; set; } = new();
    /// <summary>De kleur van de rij (een van <see cref="Planbord.Rijkleuren"/>, "#rrggbb"); leeg = geen.</summary>
    public string Kleur { get; set; } = "";
}

/// <summary>Een werknemer om een rij aan te koppelen. <see cref="Kort"/> = de naam in de kolom Wie: de voornaam, met de
/// eerste letter van de achternaam als er meer collega's zo heten; wie uit dienst is voluit.</summary>
public record PlanPersoon(int Id, string Voornaam, string Achternaam, bool InDienst)
{
    public string Kort { get; init; } = "";
    public string Naam => (Voornaam + " " + Achternaam).Trim();
}

/// <summary>Een balk (van–tot) of een symbool (één moment, <see cref="Einde"/> leeg) op een rij.</summary>
/// <param name="Legenda">De soort uit de legenda: Done, To Do, Periode, Out of office, Vertraging, Milestone, Delivery …</param>
/// <param name="Kleur">Bij een balk de kleur (#rrggbb) uit Tom's Planner; bij een symbool het nummer van het symbool.</param>
public record PlanItem(int Id, int RijId, bool Symbool, DateTime Start, DateTime? Einde, string Legenda, string Kleur, string Label, string Opmerking);

/// <summary>Wat er in een exportbestand van Tom's Planner staat, klaar om over te zetten.</summary>
public class PlanImport
{
    public List<PlanRij> Rijen { get; } = new();
    public List<PlanItem> Items { get; } = new();
    public List<string> Opmerkingen { get; } = new();
    public int Groepen => Rijen.Count(r => r.OuderId is null);
    public int Balken => Items.Count(i => !i.Symbool);
    public int Symbolen => Items.Count(i => i.Symbool);
    public DateTime? Van => Items.Count == 0 ? null : Items.Min(i => i.Start);
    public DateTime? Tot => Items.Count == 0 ? null : Items.Max(i => i.Einde ?? i.Start);
}

/// <summary>
/// Planning (wens gebruiker 25-11-2026; menu Planning, <c>/planbord</c>): groepen met daaronder activiteiten, elk met
/// balken en symbolen op een tijdbalk, zoals Tom's Planner. Stap 1: de export van Tom's Planner eenmalig overzetten en
/// de planning bekijken. Stap 2: bewerken (rijen en balken toevoegen, wijzigen, verslepen, verwijderen), voor wie het
/// recht Planner heeft of Directie. Het koppelen van een groep aan klant, project of werkorder komt later (de kolommen
/// staan er al). Vervangt de platte Projectplanning (Planning.razor, tabel Planning), die nooit gevuld is.
/// Tabellen: Planbord_MariaDB.sql. Met <c>Planbord:Geheugen</c> staat alles in het geheugen (lokaal testen).
/// </summary>
public class Planbord
{
    private readonly string? _cs;
    private readonly bool _geheugen;
    private bool _aanwezig;

    private bool _wieAanwezig, _kleurAanwezig;

    /// <summary>De tien kleuren waaruit je voor een rij kunt kiezen (wens gebruiker 25-11-2026).</summary>
    public static readonly (string Naam, string Hex)[] Rijkleuren =
    {
        ("Rood", "#e5484d"), ("Oranje", "#f08c2e"), ("Geel", "#e8c21a"), ("Groen", "#46a758"), ("Turquoise", "#12a594"),
        ("Blauw", "#3e8ee0"), ("Paars", "#8e5bd0"), ("Roze", "#d6589f"), ("Bruin", "#a07653"), ("Grijs", "#8b949e"),
    };

    /// <summary>Staat de kolom PlanbordRijen.Kleur er (Planbord_MariaDB.sql opnieuw gedraaid)? Zonder is er geen kleurkeuze.</summary>
    public async Task<bool> KleurAanwezigAsync()
    {
        if (_geheugen) return true;
        if (_kleurAanwezig || !Live) return _kleurAanwezig;
        return _kleurAanwezig = await ScalarAsync(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'PlanbordRijen' AND COLUMN_NAME = 'Kleur'") > 0;
    }

    /// <summary>Een rij een kleur geven; leeg = geen kleur. Alleen een kleur uit <see cref="Rijkleuren"/>.</summary>
    public async Task KleurAsync(int id, string? kleur) { await ZetKleurAsync(id, kleur); Seintje(); }

    private async Task ZetKleurAsync(int id, string? kleur)
    {
        kleur = Rijkleuren.FirstOrDefault(k => string.Equals(k.Hex, kleur?.Trim(), StringComparison.OrdinalIgnoreCase)).Hex ?? "";
        if (_geheugen) { lock (_slot) if (_rijen.FirstOrDefault(r => r.Id == id) is { } r) r.Kleur = kleur; return; }
        if (!await KleurAanwezigAsync()) return;
        await DoeAsync("UPDATE `PlanbordRijen` SET `Kleur` = @k WHERE `RijID` = @id", ("@k", kleur.Length == 0 ? null : kleur), ("@id", id));
    }
    private (DateTime Tot, List<PlanPersoon> Lijst) _personen;

    private readonly object _slot = new();
    private List<PlanRij> _rijen = new();
    private List<PlanItem> _items = new();

    public Planbord(IConfiguration config)
    {
        _cs = config.GetConnectionString("MDE");
        _geheugen = config.GetValue<bool>("Planbord:Geheugen");
        // lokaal testen met een echte export, zonder database: Planbord:Proefbestand = het pad van het CSV-bestand
        if (_geheugen && config["Planbord:Proefbestand"] is { Length: > 0 } pad && File.Exists(pad))
        {
            var import = LeesExport(File.ReadAllText(pad, Encoding.UTF8));
            _rijen = import.Rijen;
            _items = import.Items;
        }
    }

    private bool Live => !_geheugen && !string.IsNullOrWhiteSpace(_cs) && !_cs!.Contains("REPLACE_");
    public bool Beschikbaar => _geheugen || Live;

    public async Task<bool> AanwezigAsync()
    {
        if (_geheugen) return true;
        if (_aanwezig || !Live) return _aanwezig;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('PlanbordRijen', 'PlanbordItems')", cn);
        return _aanwezig = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 2;
    }

    // ------------------------------------------------------------------ lezen

    /// <summary>De hele planning: alle rijen (op volgorde) en alle balken en symbolen. Het zijn een paar duizend regels.</summary>
    public async Task<(List<PlanRij> Rijen, List<PlanItem> Items)> AllesAsync()
    {
        if (_geheugen) lock (_slot) return (_rijen.OrderBy(r => r.Volgorde).ThenBy(r => r.Id).ToList(), _items.ToList());
        var rijen = new List<PlanRij>();
        var items = new List<PlanItem>();
        if (!await AanwezigAsync()) return (rijen, items);
        var metKleur = await KleurAanwezigAsync();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using (var cmd = new MySqlCommand(
            "SELECT x.`RijID`, x.`OuderID`, x.`Volgorde`, x.`Naam`, COALESCE(x.`Resource`, ''), COALESCE(x.`Nummer`, ''), x.`RelatieID`, x.`ProjectID`, x.`WerkorderID`, " +
            // de namen van waar de rij aan gekoppeld is (stap 3); het werkordernummer zoals overal: 218183-03
            "       COALESCE(CAST(p.`Projectnumber` AS CHAR), ''), COALESCE(p.`Projectdescription`, ''), " +
            "       COALESCE(CONCAT(wp.`Projectnumber`, '-', RIGHT(CONCAT('00', wo.`Werkbonnummer`), 2)), ''), COALESCE(wo.`Werkbonomschrijving`, ''), wo.`Project-id`, " +
            "       COALESCE(kl.`name`, ''), " + (metKleur ? "COALESCE(x.`Kleur`, '')" : "''") + " " +
            "  FROM `PlanbordRijen` x " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = x.`ProjectID` " +
            "  LEFT JOIN `Workorders` wo ON wo.`Werkbon-id` = x.`WerkorderID` LEFT JOIN `Projects` wp ON wp.`Project-Id` = wo.`Project-id` " +
            "  LEFT JOIN `Relations` kl ON kl.`id_supplier` = x.`RelatieID` " +
            " ORDER BY x.`Volgorde`, x.`RijID`", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var rij = new PlanRij
                {
                    Id = r.GetInt32(0), OuderId = r.IsDBNull(1) ? null : r.GetInt32(1), Volgorde = r.GetInt32(2), Naam = r.GetString(3),
                    Resource = r.GetString(4), Nummer = r.GetString(5), RelatieId = r.IsDBNull(6) ? null : r.GetInt32(6),
                    ProjectId = r.IsDBNull(7) ? null : r.GetInt32(7), WerkorderId = r.IsDBNull(8) ? null : r.GetInt32(8),
                    Kleur = r.GetString(15),
                };
                if (rij.ProjectId is not null) ZetKoppel(rij, "Project", r.GetString(9), r.GetString(10), rij.ProjectId);
                else if (rij.WerkorderId is not null) ZetKoppel(rij, "Werkorder", r.GetString(11), r.GetString(12), r.IsDBNull(13) ? null : Convert.ToInt32(r.GetValue(13)));
                else if (rij.RelatieId is not null) ZetKoppel(rij, "Klant", "", r.GetString(14), null);
                rijen.Add(rij);
            }
        if (await WieAanwezigAsync())
        {
            var opId = rijen.ToDictionary(x => x.Id);
            await using var cmd = new MySqlCommand("SELECT `RijID`, `WerknemerID` FROM `PlanbordRijWerknemers` ORDER BY `RijID`, `WerknemerID`", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                if (opId.TryGetValue(r.GetInt32(0), out var rij)) rij.Werknemers.Add(r.GetInt32(1));
        }
        await using (var cmd = new MySqlCommand(
            "SELECT `ItemID`, `RijID`, `Symbool`, `Start`, `Einde`, COALESCE(`Legenda`, ''), COALESCE(`Kleur`, ''), COALESCE(`Label`, ''), COALESCE(`Opmerking`, '') " +
            "  FROM `PlanbordItems` ORDER BY `RijID`, `Start`", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                items.Add(new PlanItem(r.GetInt32(0), r.GetInt32(1), Convert.ToInt32(r.GetValue(2)) != 0, r.GetDateTime(3),
                                       r.IsDBNull(4) ? null : r.GetDateTime(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8)));
        return (rijen, items);
    }

    // ------------------------------------------------------------------ de export van Tom's Planner lezen

    /// <summary>Een regel CSV in velden: komma's ertussen, een veld met een komma of aanhalingsteken staat tussen "…" ("" = ").</summary>
    private static List<List<string>> LeesCsv(string tekst)
    {
        var regels = new List<List<string>>();
        var regel = new List<string>();
        var veld = new StringBuilder();
        var inAanhaling = false;
        for (var i = 0; i < tekst.Length; i++)
        {
            var c = tekst[i];
            if (inAanhaling)
            {
                if (c == '"' && i + 1 < tekst.Length && tekst[i + 1] == '"') { veld.Append('"'); i++; }
                else if (c == '"') inAanhaling = false;
                else veld.Append(c);
            }
            else if (c == '"') inAanhaling = true;
            else if (c == ',') { regel.Add(veld.ToString()); veld.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < tekst.Length && tekst[i + 1] == '\n') i++;
                regel.Add(veld.ToString()); veld.Clear();
                if (regel.Count > 1 || regel[0].Length > 0) regels.Add(regel);
                regel = new List<string>();
            }
            else veld.Append(c);
        }
        regel.Add(veld.ToString());
        if (regel.Count > 1 || regel[0].Length > 0) regels.Add(regel);
        return regels;
    }

    private static string Kort(string? s, int max) { s = (s ?? "").Trim(); return s.Length <= max ? s : s[..max]; }

    private static DateTime? Datum(string? s)
        => DateTime.TryParseExact((s ?? "").Trim(), new[] { "M/d/yyyy h:mm:ss tt", "M/d/yyyy H:mm:ss", "M/d/yyyy" }, CultureInfo.InvariantCulture,
                                  DateTimeStyles.None, out var d) ? d : null;

    /// <summary>
    /// De export van Tom's Planner (Projectenplanning.csv) lezen. Kolommen: row number, Activity, Resource, start row,
    /// end row, duration row, (leeg), period/symbol, start, type, legend, comment, end period, duration, label. Een rij
    /// die meer balken heeft staat er zo vaak in; het rijnummer ("24.3.1") zegt waar hij in de boom hangt.
    /// Er wordt niets opgeslagen: de uitkomst is voor de voorvertoning en daarna voor <see cref="ZetOverAsync"/>.
    /// </summary>
    public static PlanImport LeesExport(string csv)
    {
        var uit = new PlanImport();
        var regels = LeesCsv((csv ?? "").TrimStart('﻿'));
        if (regels.Count == 0 || !regels[0][0].Trim().Equals("row number", StringComparison.OrdinalIgnoreCase))
        {
            uit.Opmerkingen.Add("Dit lijkt geen export van Tom's Planner: de eerste kolom hoort \"row number\" te heten.");
            return uit;
        }
        var opNummer = new Dictionary<string, PlanRij>(StringComparer.Ordinal);
        int zonderOuder = 0, onleesbaar = 0, itemId = 0;
        foreach (var v in regels.Skip(1))
        {
            string Veld(int i) => i < v.Count ? v[i].Trim() : "";
            var nummer = Veld(0);
            if (nummer.Length == 0) continue;
            if (!opNummer.TryGetValue(nummer, out var rij))
            {
                rij = new PlanRij { Id = opNummer.Count + 1, Volgorde = opNummer.Count + 1, Nummer = Kort(nummer, 40), Naam = Kort(Veld(1), 200), Resource = Kort(Veld(2), 100) };
                var punt = nummer.LastIndexOf('.');
                if (punt > 0)
                {
                    if (opNummer.TryGetValue(nummer[..punt], out var ouder)) rij.OuderId = ouder.Id;
                    else zonderOuder++;       // de ouder staat niet (eerder) in het bestand: dan komt de rij bovenaan
                }
                if (rij.Naam.Length == 0) rij.Naam = "(zonder naam)";
                opNummer[nummer] = rij;
                uit.Rijen.Add(rij);
            }
            var soort = Veld(7).TrimEnd(':').ToLowerInvariant();
            if (soort.Length == 0) continue;
            var start = Datum(Veld(8));
            var einde = Datum(Veld(12));
            var symbool = soort == "symbol";
            if (start is null || (!symbool && einde is null)) { onleesbaar++; continue; }
            if (!symbool && einde < start) (start, einde) = (einde, start);
            uit.Items.Add(new PlanItem(++itemId, rij.Id, symbool, start.Value, symbool ? null : einde, Kort(Veld(10), 60), Kort(Veld(9), 20), Kort(Veld(14), 100), Kort(Veld(11), 500)));
        }
        if (zonderOuder > 0) uit.Opmerkingen.Add($"{zonderOuder} rij(en) hebben een nummer waarvan de bovenliggende rij ontbreekt; die staan bovenaan.");
        if (onleesbaar > 0) uit.Opmerkingen.Add($"{onleesbaar} balk(en) of symbool/symbolen zonder leesbare datum zijn overgeslagen.");
        if (uit.Rijen.Count == 0) uit.Opmerkingen.Add("Er staan geen rijen in dit bestand.");
        return uit;
    }

    /// <summary>
    /// De gelezen export in de planning zetten. <b>Vervangt alles wat er stond</b>: het is bedoeld als eenmalige overzet
    /// (besluit gebruiker 25-11-2026), en zolang er in de app nog niet bewerkt wordt kan het ook nog een keer.
    /// </summary>
    public async Task ZetOverAsync(PlanImport import)
    {
        if (import.Rijen.Count == 0) throw new InvalidOperationException("Er is niets om over te zetten.");
        if (_geheugen) { lock (_slot) { _rijen = import.Rijen.ToList(); _items = import.Items.ToList(); } Seintje(); return; }
        if (!await AanwezigAsync()) throw new InvalidOperationException("Planbord_MariaDB.sql is nog niet uitgevoerd.");
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        var tabellen = new List<string> { "PlanbordItems", "PlanbordRijen" };
        if (await WieAanwezigAsync()) tabellen.Add("PlanbordRijWerknemers");
        foreach (var tabel in tabellen)
            await using (var weg = new MySqlCommand($"DELETE FROM `{tabel}`", cn, tx)) await weg.ExecuteNonQueryAsync();

        // in blokken, met de nummers uit de import (1, 2, 3 …): de ouder van een rij verwijst daarnaar
        foreach (var blok in import.Rijen.Chunk(200))
        {
            var sql = new StringBuilder("INSERT INTO `PlanbordRijen` (`RijID`, `OuderID`, `Volgorde`, `Naam`, `Resource`, `Nummer`) VALUES ");
            await using var cmd = new MySqlCommand { Connection = cn, Transaction = tx };
            for (var i = 0; i < blok.Length; i++)
            {
                sql.Append(i == 0 ? "" : ", ").Append($"(@i{i}, @o{i}, @v{i}, @n{i}, @r{i}, @m{i})");
                cmd.Parameters.AddWithValue($"@i{i}", blok[i].Id);
                cmd.Parameters.AddWithValue($"@o{i}", (object?)blok[i].OuderId ?? DBNull.Value);
                cmd.Parameters.AddWithValue($"@v{i}", blok[i].Volgorde);
                cmd.Parameters.AddWithValue($"@n{i}", blok[i].Naam);
                cmd.Parameters.AddWithValue($"@r{i}", blok[i].Resource);
                cmd.Parameters.AddWithValue($"@m{i}", blok[i].Nummer);
            }
            cmd.CommandText = sql.ToString();
            await cmd.ExecuteNonQueryAsync();
        }
        foreach (var blok in import.Items.Chunk(200))
        {
            var sql = new StringBuilder("INSERT INTO `PlanbordItems` (`RijID`, `Symbool`, `Start`, `Einde`, `Legenda`, `Kleur`, `Label`, `Opmerking`) VALUES ");
            await using var cmd = new MySqlCommand { Connection = cn, Transaction = tx };
            for (var i = 0; i < blok.Length; i++)
            {
                sql.Append(i == 0 ? "" : ", ").Append($"(@r{i}, @s{i}, @b{i}, @e{i}, @l{i}, @k{i}, @t{i}, @o{i})");
                cmd.Parameters.AddWithValue($"@r{i}", blok[i].RijId);
                cmd.Parameters.AddWithValue($"@s{i}", blok[i].Symbool ? 1 : 0);
                cmd.Parameters.AddWithValue($"@b{i}", blok[i].Start);
                cmd.Parameters.AddWithValue($"@e{i}", (object?)blok[i].Einde ?? DBNull.Value);
                cmd.Parameters.AddWithValue($"@l{i}", blok[i].Legenda);
                cmd.Parameters.AddWithValue($"@k{i}", blok[i].Kleur);
                cmd.Parameters.AddWithValue($"@t{i}", blok[i].Label);
                cmd.Parameters.AddWithValue($"@o{i}", blok[i].Opmerking);
            }
            cmd.CommandText = sql.ToString();
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        Seintje();
    }

    // ------------------------------------------------------------------ bewerken (stap 2, 25-11-2026)

    /// <summary>Er is iets aan de planning veranderd; elk open scherm leest dan opnieuw.</summary>
    public event Action? Gewijzigd;
    private void Seintje() { try { Gewijzigd?.Invoke(); } catch { /* een scherm dat net sluit mag de rest niet tegenhouden */ } }

    /// <summary>De soorten uit de legenda van Tom's Planner: naam, kleur van de balk of nummer van het symbool.</summary>
    public static readonly (string Naam, string Kleur)[] Balksoorten =
    {
        ("To Do", "#5fb5d5"), ("Done", "#2775ac"), ("Periode", "#9a9a99"), ("Out of office", "#aa374f"), ("Vertraging", "#d90c0d"),
    };
    public static readonly (string Naam, string Kleur)[] Symboolsoorten =
    {
        ("Milestone", "21"), ("Delivery", "8"), ("Customer dependency", "9"), ("M-Pede Robotics bestellen", "49"), ("M-Pede Robot benodigd", "19"),
    };

    private async Task<int> ScalarAsync(string sql, params (string Naam, object? Waarde)[] p)
    {
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        foreach (var (n, w) in p) cmd.Parameters.AddWithValue(n, w ?? DBNull.Value);
        var uit = await cmd.ExecuteScalarAsync();
        return uit is null or DBNull ? 0 : Convert.ToInt32(uit);
    }

    private async Task DoeAsync(string sql, params (string Naam, object? Waarde)[] p)
    {
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        foreach (var (n, w) in p) cmd.Parameters.AddWithValue(n, w ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>De buren onder een ouder opnieuw nummeren in de gegeven volgorde (10, 20, …).</summary>
    private async Task NummerAsync(IReadOnlyList<int> ids)
    {
        if (_geheugen)
        {
            lock (_slot) for (var i = 0; i < ids.Count; i++) if (_rijen.FirstOrDefault(r => r.Id == ids[i]) is { } r) r.Volgorde = (i + 1) * 10;
            return;
        }
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        for (var i = 0; i < ids.Count; i++)
        {
            await using var cmd = new MySqlCommand("UPDATE `PlanbordRijen` SET `Volgorde` = @v WHERE `RijID` = @id", cn, tx);
            cmd.Parameters.AddWithValue("@v", (i + 1) * 10);
            cmd.Parameters.AddWithValue("@id", ids[i]);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    private static List<int> Buren(IEnumerable<PlanRij> rijen, int? ouderId, int zonder)
        => rijen.Where(r => r.OuderId == ouderId && r.Id != zonder).OrderBy(r => r.Volgorde).ThenBy(r => r.Id).Select(r => r.Id).ToList();

    /// <summary>
    /// Een rij toevoegen onder <paramref name="ouderId"/> (leeg = bovenaan). Met <paramref name="naId"/> komt hij direct
    /// na die buur, anders achteraan. Geeft het nummer van de nieuwe rij.
    /// </summary>
    public async Task<int> NieuweRijAsync(int? ouderId, int? naId, string naam, string resource, IReadOnlyCollection<int>? werknemers = null, string? kleur = null)
    {
        naam = Kort(naam, 200); resource = Kort(resource, 100);
        if (naam.Length == 0) throw new InvalidOperationException("Geef de rij een naam.");
        int id;
        if (_geheugen)
            lock (_slot)
            {
                id = (_rijen.Count == 0 ? 0 : _rijen.Max(r => r.Id)) + 1;
                _rijen.Add(new PlanRij { Id = id, OuderId = ouderId, Naam = naam, Resource = resource, Volgorde = int.MaxValue });
            }
        else
            id = await ScalarAsync(
                "INSERT INTO `PlanbordRijen` (`OuderID`, `Volgorde`, `Naam`, `Resource`) VALUES (@o, 2000000000, @n, @r); SELECT LAST_INSERT_ID();",
                ("@o", ouderId), ("@n", naam), ("@r", resource));
        if (werknemers is { Count: > 0 }) await ZetWieAsync(id, werknemers);
        if (!string.IsNullOrWhiteSpace(kleur)) await ZetKleurAsync(id, kleur);
        var (rijen, _) = await AllesAsync();
        var buren = Buren(rijen, ouderId, id);
        var plek = naId is int na ? buren.IndexOf(na) : -1;
        if (plek >= 0) buren.Insert(plek + 1, id); else buren.Add(id);
        await NummerAsync(buren);
        Seintje();
        return id;
    }

    /// <summary>Naam en wie van een rij wijzigen. <paramref name="werknemers"/> leeg gelaten = de gekoppelde werknemers blijven zoals ze zijn.</summary>
    public async Task WijzigRijAsync(int id, string naam, string resource, IReadOnlyCollection<int>? werknemers = null, string? kleur = null)
    {
        naam = Kort(naam, 200); resource = Kort(resource, 100);
        if (naam.Length == 0) throw new InvalidOperationException("Geef de rij een naam.");
        if (_geheugen) { lock (_slot) if (_rijen.FirstOrDefault(r => r.Id == id) is { } r) { r.Naam = naam; r.Resource = resource; } }
        else await DoeAsync("UPDATE `PlanbordRijen` SET `Naam` = @n, `Resource` = @r WHERE `RijID` = @id", ("@n", naam), ("@r", resource), ("@id", id));
        if (werknemers is not null) await ZetWieAsync(id, werknemers);
        if (kleur is not null) await ZetKleurAsync(id, kleur);      // null = de kleur blijft zoals hij is
        Seintje();
    }

    // ------------------------------------------------------------------ wie: werknemers (stap 4, 25-11-2026)

    /// <summary>Staat de koppeltabel PlanbordRijWerknemers er (Planbord_MariaDB.sql opnieuw gedraaid)? Zonder blijft Wie vrije tekst.</summary>
    public async Task<bool> WieAanwezigAsync()
    {
        if (_geheugen) return true;
        if (_wieAanwezig || !Live) return _wieAanwezig;
        return _wieAanwezig = await ScalarAsync(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'PlanbordRijWerknemers'") > 0;
    }

    /// <summary>
    /// De werknemers (alleen personen) om uit te kiezen, met hun korte naam. Vijf minuten onthouden. De testversie in
    /// het geheugen leest ze ook uit de database als die er is (alleen lezen), en heeft anders een paar proefnamen.
    /// </summary>
    public async Task<List<PlanPersoon>> PersonenAsync()
    {
        if (_personen.Lijst is { } al && _personen.Tot > DateTime.UtcNow) return al;
        var ruw = new List<PlanPersoon>();
        if (!string.IsNullOrWhiteSpace(_cs) && !_cs!.Contains("REPLACE_"))
        {
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT `Werknemer-id`, COALESCE(`Voornaam`, ''), COALESCE(`Achternaam`, ''), COALESCE(`NaamVoluit`, ''), COALESCE(`Uit dienst`, 0) = 0 " +
                "  FROM `Werknemers` WHERE COALESCE(`Persoon`, 0) <> 0", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var voor = r.GetString(1).Trim(); var achter = r.GetString(2).Trim();
                if (voor.Length == 0 && achter.Length == 0) voor = r.GetString(3).Trim();
                if (voor.Length == 0 && achter.Length == 0) continue;
                ruw.Add(new PlanPersoon(r.GetInt32(0), voor, achter, Convert.ToInt32(r.GetValue(4)) != 0));
            }
        }
        else if (_geheugen)
            ruw = new() { new(1, "Gerard", "Proef", true), new(2, "Jan", "Een", true), new(3, "Jan", "Twee", true), new(4, "Thomas", "Verschuure", true), new(5, "Oud", "Collega", false) };
        var lijst = MetKorteNaam(ruw);
        _personen = (DateTime.UtcNow.AddMinutes(5), lijst);
        return lijst;
    }

    /// <summary>De korte namen: voornaam; heten er meer collega's in dienst zo, dan met de eerste letter van de achternaam
    /// ("Jan B."), en is ook dat gelijk de hele naam. Wie uit dienst is staat voluit.</summary>
    public static List<PlanPersoon> MetKorteNaam(IEnumerable<PlanPersoon> personen)
    {
        var alle = personen.ToList();
        var inDienst = alle.Where(p => p.InDienst).ToList();
        string Letter(PlanPersoon p) => p.Achternaam.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() is { Length: > 0 } a ? p.Voornaam + " " + char.ToUpperInvariant(a[0]) + "." : p.Voornaam;
        var perVoornaam = inDienst.GroupBy(p => p.Voornaam, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var perLetter = inDienst.GroupBy(Letter, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return alle.Select(p => p with
        {
            Kort = !p.InDienst || p.Voornaam.Length == 0 ? p.Naam : perVoornaam[p.Voornaam] == 1 ? p.Voornaam : perLetter[Letter(p)] == 1 ? Letter(p) : p.Naam,
        }).OrderBy(p => p.Kort, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>De losse namen in de vrije tekst van Wie: "Franke, Verschuure" en "Kees/Gerard" zijn elk twee namen.</summary>
    public static List<string> Namen(string resource)
        => resource.Split(new[] { ',', '/', ';', '&', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(n => n.Length > 0).ToList();

    /// <summary>
    /// Welke werknemer (in dienst) bij een naam uit de vrije tekst hoort, als dat er precies één is: op korte naam, hele
    /// naam, voornaam of achternaam. Niets = zelf kiezen (of het is geen werknemer: "Werkplaats", "Software Vacancy").
    /// </summary>
    public static int? Voorstel(string naam, IReadOnlyList<PlanPersoon> personen)
    {
        naam = naam.Trim().TrimEnd('?').Trim();
        if (naam.Length == 0) return null;
        bool Is(string a) => string.Equals(a, naam, StringComparison.OrdinalIgnoreCase);
        var inDienst = personen.Where(p => p.InDienst).ToList();
        foreach (var proef in new Func<PlanPersoon, bool>[] { p => Is(p.Kort), p => Is(p.Naam), p => Is(p.Voornaam), p => Is(p.Achternaam),
                                                              p => Is(p.Achternaam.Split(' ').Last()) })
        {
            var treffers = inDienst.Where(proef).ToList();
            if (treffers.Count == 1) return treffers[0].Id;
            if (treffers.Count > 1) return null;
        }
        return null;
    }

    /// <summary>De gekoppelde werknemers van een rij vervangen. Zonder de koppeltabel gebeurt er niets.</summary>
    private async Task ZetWieAsync(int id, IReadOnlyCollection<int> werknemers)
    {
        var ids = werknemers.Distinct().ToList();
        if (_geheugen) { lock (_slot) if (_rijen.FirstOrDefault(r => r.Id == id) is { } r) r.Werknemers = ids; return; }
        if (!await WieAanwezigAsync()) return;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await using (var weg = new MySqlCommand("DELETE FROM `PlanbordRijWerknemers` WHERE `RijID` = @id", cn, tx))
        {
            weg.Parameters.AddWithValue("@id", id);
            await weg.ExecuteNonQueryAsync();
        }
        foreach (var w in ids)
        {
            await using var erbij = new MySqlCommand("INSERT IGNORE INTO `PlanbordRijWerknemers` (`RijID`, `WerknemerID`) VALUES (@id, @w)", cn, tx);
            erbij.Parameters.AddWithValue("@id", id);
            erbij.Parameters.AddWithValue("@w", w);
            await erbij.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    /// <summary>
    /// De namen uit de vrije tekst (Tom's Planner) in één keer aan werknemers koppelen: <paramref name="keuze"/> = naam
    /// → werknemer. Op elke rij worden de gekozen namen een koppeling en verdwijnen ze uit de vrije tekst; de rest van
    /// de tekst ("Werkplaats", een naam die niet gekozen is) blijft staan. Alles of niets. Geeft het aantal gewijzigde rijen.
    /// </summary>
    public async Task<int> KoppelNamenAsync(IReadOnlyDictionary<string, int> keuze)
    {
        var op = new Dictionary<string, int>(keuze, StringComparer.OrdinalIgnoreCase);
        if (op.Count == 0) return 0;
        if (!await WieAanwezigAsync()) throw new InvalidOperationException("Draai eerst Planbord_MariaDB.sql opnieuw (tabel PlanbordRijWerknemers).");
        var (rijen, _) = await AllesAsync();
        var werk = new List<(int Id, string Tekst, List<int> Erbij)>();
        foreach (var r in rijen)
        {
            var namen = Namen(r.Resource);
            var erbij = namen.Where(op.ContainsKey).Select(n => op[n]).Distinct().ToList();
            if (erbij.Count == 0) continue;
            werk.Add((r.Id, string.Join(", ", namen.Where(n => !op.ContainsKey(n))), erbij));
        }
        if (werk.Count == 0) return 0;
        if (_geheugen)
        {
            lock (_slot)
                foreach (var (id, rest, erbij) in werk)
                    if (_rijen.FirstOrDefault(r => r.Id == id) is { } r) { r.Resource = rest; r.Werknemers = r.Werknemers.Union(erbij).ToList(); }
        }
        else
        {
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var tx = await cn.BeginTransactionAsync();
            foreach (var blok in werk.SelectMany(w => w.Erbij.Select(e => (w.Id, Wie: e))).Chunk(300))
            {
                // getallen uit de database en uit de keuzelijst zelf
                var sql = "INSERT IGNORE INTO `PlanbordRijWerknemers` (`RijID`, `WerknemerID`) VALUES " + string.Join(",", blok.Select(b => $"({b.Id},{b.Wie})"));
                await using var erbij = new MySqlCommand(sql, cn, tx);
                await erbij.ExecuteNonQueryAsync();
            }
            foreach (var (id, rest, _) in werk)
            {
                await using var tekst = new MySqlCommand("UPDATE `PlanbordRijen` SET `Resource` = @r WHERE `RijID` = @id", cn, tx);
                tekst.Parameters.AddWithValue("@r", rest);
                tekst.Parameters.AddWithValue("@id", id);
                await tekst.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        Seintje();
        return werk.Count;
    }

    /// <summary>De tekst bij een koppeling: kort voor in de boom, voluit voor het wolkje. Bestaat het doel niet meer, dan "onbekend".</summary>
    private static void ZetKoppel(PlanRij rij, string soort, string nummer, string naam, int? projectId)
    {
        nummer = nummer.Trim(); naam = naam.Trim();
        rij.KoppelKort = nummer.Length > 0 ? nummer : naam.Length > 0 ? naam : "?";
        rij.KoppelTekst = soort + " " + (nummer.Length > 0 && naam.Length > 0 ? nummer + " · " + naam : nummer.Length > 0 ? nummer : naam.Length > 0 ? naam : "(bestaat niet meer)");
        rij.KoppelProjectId = projectId;
    }

    /// <summary>
    /// Een rij koppelen aan een project, een klant of een werkorder (stap 3, 25-11-2026; zoals een Activity board): één
    /// van de drie, de andere twee worden leeg. Alle drie leeg = ontkoppelen. <paramref name="nummer"/> en
    /// <paramref name="naam"/> zijn alleen voor de testversie in het geheugen, die de namen niet kan opzoeken.
    /// </summary>
    public async Task KoppelAsync(int id, int? projectId, int? relatieId, int? werkorderId, string nummer = "", string naam = "", int? springProjectId = null)
    {
        if (projectId is not null) { relatieId = null; werkorderId = null; }
        else if (werkorderId is not null) relatieId = null;
        if (_geheugen)
        {
            lock (_slot)
                if (_rijen.FirstOrDefault(r => r.Id == id) is { } r)
                {
                    r.ProjectId = projectId; r.RelatieId = relatieId; r.WerkorderId = werkorderId;
                    r.KoppelKort = ""; r.KoppelTekst = ""; r.KoppelProjectId = null;
                    if (r.Gekoppeld) ZetKoppel(r, projectId is not null ? "Project" : werkorderId is not null ? "Werkorder" : "Klant", nummer, naam, projectId ?? springProjectId);
                }
        }
        else
            await DoeAsync("UPDATE `PlanbordRijen` SET `ProjectID` = @p, `RelatieID` = @k, `WerkorderID` = @w WHERE `RijID` = @id",
                           ("@p", (object?)projectId ?? DBNull.Value), ("@k", (object?)relatieId ?? DBNull.Value), ("@w", (object?)werkorderId ?? DBNull.Value), ("@id", id));
        Seintje();
    }

    /// <summary>
    /// De rijen van de planning die bij een project horen: gekoppeld aan het project zelf of aan een werkorder ervan
    /// (voor de sprong vanuit Projecten, 25-11-2026). Zonder tabellen of bij een fout: niets.
    /// </summary>
    public async Task<List<(int Id, string Naam)>> VoorProjectAsync(int projectId)
    {
        var uit = new List<(int, string)>();
        if (_geheugen)
        {
            lock (_slot) uit.AddRange(_rijen.Where(r => r.ProjectId == projectId || (r.WerkorderId is not null && r.KoppelProjectId == projectId))
                                            .OrderBy(r => r.Volgorde).Select(r => (r.Id, r.Naam)));
            return uit;
        }
        return await VoorAsync("LEFT JOIN `Workorders` wo ON wo.`Werkbon-id` = x.`WerkorderID`", "x.`ProjectID` = @id OR wo.`Project-id` = @id", projectId);
    }

    /// <summary>De rijen die aan deze werkorder gekoppeld zijn (zijblok Planning bij een werkorder in Projecten).</summary>
    public async Task<List<(int Id, string Naam)>> VoorWerkorderAsync(int werkbonId)
    {
        if (_geheugen) lock (_slot) return _rijen.Where(r => r.WerkorderId == werkbonId).OrderBy(r => r.Volgorde).Select(r => (r.Id, r.Naam)).ToList();
        return await VoorAsync("", "x.`WerkorderID` = @id", werkbonId);
    }

    /// <summary>
    /// De rijen van een klant: gekoppeld aan de klant zelf, aan een project van die klant of aan een werkorder van zo'n
    /// project (zijblok Planning in Relaties). In de testversie in het geheugen alleen de rijen op de klant zelf.
    /// </summary>
    public async Task<List<(int Id, string Naam)>> VoorKlantAsync(int relatieId)
    {
        if (_geheugen) lock (_slot) return _rijen.Where(r => r.RelatieId == relatieId).OrderBy(r => r.Volgorde).Select(r => (r.Id, r.Naam)).ToList();
        return await VoorAsync(
            "LEFT JOIN `Projects` p ON p.`Project-Id` = x.`ProjectID` LEFT JOIN `Workorders` wo ON wo.`Werkbon-id` = x.`WerkorderID` " +
            "LEFT JOIN `Projects` wp ON wp.`Project-Id` = wo.`Project-id`",
            "x.`RelatieID` = @id OR p.`Customer-id` = @id OR wp.`Customer-id` = @id", relatieId);
    }

    /// <summary>De gekoppelde rijen voor een van de drie soorten; hooguit twintig, in de volgorde van de planning.</summary>
    private async Task<List<(int Id, string Naam)>> VoorAsync(string join, string waar, int id)
    {
        var uit = new List<(int, string)>();
        if (!await AanwezigAsync()) return uit;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT x.`RijID`, x.`Naam` FROM `PlanbordRijen` x {join} WHERE {waar} ORDER BY x.`Volgorde`, x.`RijID` LIMIT 20", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit.Add((r.GetInt32(0), r.GetString(1)));
        return uit;
    }

    /// <summary>
    /// Een rij (met alles eronder) verplaatsen: onder <paramref name="ouderId"/> (leeg = bovenaan), met
    /// <paramref name="voorId"/> vóór die buur en anders achteraan. Niet in zichzelf of in iets wat eronder hangt.
    /// </summary>
    public async Task VerplaatsRijAsync(int id, int? ouderId, int? voorId)
    {
        if (voorId == id || ouderId == id) return;
        var (rijen, _) = await AllesAsync();
        var opId = rijen.ToDictionary(r => r.Id);
        if (!opId.ContainsKey(id)) throw new InvalidOperationException("Deze rij bestaat niet (meer).");
        var stappen = 0;
        for (var x = ouderId; x is int o && opId.TryGetValue(o, out var ouder) && stappen++ < 50; x = ouder.OuderId)
            if (o == id) throw new InvalidOperationException("Een rij kan niet in zichzelf of in iets wat eronder hangt.");
        if (_geheugen) { lock (_slot) if (_rijen.FirstOrDefault(r => r.Id == id) is { } r) r.OuderId = ouderId; }
        else await DoeAsync("UPDATE `PlanbordRijen` SET `OuderID` = @o WHERE `RijID` = @id", ("@o", ouderId), ("@id", id));
        var buren = Buren(rijen, ouderId, id);
        var plek = voorId is int v ? buren.IndexOf(v) : -1;
        if (plek >= 0) buren.Insert(plek, id); else buren.Add(id);
        await NummerAsync(buren);
        Seintje();
    }

    /// <summary>Een rij verwijderen, met alle rijen eronder en al hun balken en symbolen. Geeft het aantal rijen dat weg is.</summary>
    public async Task<int> VerwijderRijAsync(int id)
    {
        var (rijen, _) = await AllesAsync();
        var weg = new List<int> { id };
        for (var i = 0; i < weg.Count && weg.Count < 100_000; i++)
            weg.AddRange(rijen.Where(r => r.OuderId == weg[i]).Select(r => r.Id));
        if (_geheugen)
        {
            lock (_slot) { _rijen.RemoveAll(r => weg.Contains(r.Id)); _items.RemoveAll(i => weg.Contains(i.RijId)); }
        }
        else
        {
            var metWie = await WieAanwezigAsync();
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var tx = await cn.BeginTransactionAsync();
            foreach (var blok in weg.Chunk(500))
            {
                var lijst = string.Join(",", blok);      // getallen uit de database zelf
                if (metWie) await using (var c0 = new MySqlCommand($"DELETE FROM `PlanbordRijWerknemers` WHERE `RijID` IN ({lijst})", cn, tx)) await c0.ExecuteNonQueryAsync();
                await using (var c1 = new MySqlCommand($"DELETE FROM `PlanbordItems` WHERE `RijID` IN ({lijst})", cn, tx)) await c1.ExecuteNonQueryAsync();
                await using (var c2 = new MySqlCommand($"DELETE FROM `PlanbordRijen` WHERE `RijID` IN ({lijst})", cn, tx)) await c2.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        Seintje();
        return weg.Count;
    }

    /// <summary>Een balk of symbool toevoegen (Id 0) of wijzigen. Een balk waarvan het einde voor het begin ligt wordt omgedraaid.</summary>
    public async Task<int> BewaarItemAsync(PlanItem it)
    {
        var start = it.Start;
        DateTime? einde = it.Symbool ? null : it.Einde ?? it.Start;
        if (einde is DateTime e && e < start) (start, einde) = (e, start);
        it = it with { Start = start, Einde = einde, Legenda = Kort(it.Legenda, 60), Kleur = Kort(it.Kleur, 20), Label = Kort(it.Label, 100), Opmerking = Kort(it.Opmerking, 500) };
        var id = it.Id;
        if (_geheugen)
            lock (_slot)
            {
                if (id == 0) { id = (_items.Count == 0 ? 0 : _items.Max(x => x.Id)) + 1; _items.Add(it with { Id = id }); }
                else { var i = _items.FindIndex(x => x.Id == id); if (i >= 0) _items[i] = it; }
            }
        else if (id == 0)
            id = await ScalarAsync(
                "INSERT INTO `PlanbordItems` (`RijID`, `Symbool`, `Start`, `Einde`, `Legenda`, `Kleur`, `Label`, `Opmerking`) VALUES (@r, @s, @b, @e, @l, @k, @t, @o); SELECT LAST_INSERT_ID();",
                ("@r", it.RijId), ("@s", it.Symbool ? 1 : 0), ("@b", it.Start), ("@e", it.Einde), ("@l", it.Legenda), ("@k", it.Kleur), ("@t", it.Label), ("@o", it.Opmerking));
        else
            await DoeAsync(
                "UPDATE `PlanbordItems` SET `RijID` = @r, `Symbool` = @s, `Start` = @b, `Einde` = @e, `Legenda` = @l, `Kleur` = @k, `Label` = @t, `Opmerking` = @o WHERE `ItemID` = @id",
                ("@r", it.RijId), ("@s", it.Symbool ? 1 : 0), ("@b", it.Start), ("@e", it.Einde), ("@l", it.Legenda), ("@k", it.Kleur), ("@t", it.Label), ("@o", it.Opmerking), ("@id", id));
        Seintje();
        return id;
    }

    public async Task VerwijderItemAsync(int id)
    {
        if (_geheugen) { lock (_slot) _items.RemoveAll(x => x.Id == id); }
        else await DoeAsync("DELETE FROM `PlanbordItems` WHERE `ItemID` = @id", ("@id", id));
        Seintje();
    }
}
