using System.Text.RegularExpressions;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Een ruimte van de wiki (MDE Automation, Mechanica, Software …) met het aantal actieve artikelen in de ruimte zelf.
/// <see cref="OuderId"/> = de ruimte waar deze onder hangt (leeg = bovenaan); zo vormen de ruimtes een boom.
/// </summary>
public record WikiRuimte(int Id, string Naam, string Icoon, string Omschrijving, int Aantal, int? OuderId = null);

/// <summary>Een artikel; in lijsten is <see cref="Inhoud"/> leeg.</summary>
public class WikiArtikel
{
    public int Id { get; set; }
    public int RuimteId { get; set; }
    public string Titel { get; set; } = "";
    /// <summary>Het kopje waaronder het artikel in de ruimte staat; leeg = "Algemeen".</summary>
    public string Sectie { get; set; } = "";
    /// <summary>Tags, gescheiden door komma's.</summary>
    public string Tags { get; set; } = "";
    public string Inhoud { get; set; } = "";
    public int Versie { get; set; }
    public bool Actief { get; set; } = true;
    public int? ProjectId { get; set; }
    public string ProjectTekst { get; set; } = "";
    public int? GewijzigdDoorId { get; set; }
    public string GewijzigdDoor { get; set; } = "";
    public DateTime GewijzigdOp { get; set; }

    public IEnumerable<string> TagLijst => Wiki.SplitsTags(Tags);
}

public record WikiVersie(int Id, int Versie, string Titel, string Opmerking, string Door, DateTime Op);
public record WikiBijlage(int FileStoreId, string Naam)
{
    public bool IsAfbeelding => ActAfbeeldingen.IsAfbeelding(Naam);
    /// <summary>Een HTML-bestand: te bekijken in een afgeschermd kader (zie <see cref="WikiApi"/>).</summary>
    public bool IsHtml => Wiki.IsHtml(Naam);
    /// <summary>De verwijzing om in de tekst te zetten: een plaatje of een link naar het bestand.</summary>
    public string Verwijzing => $"{(IsAfbeelding ? "!" : "")}[{Naam.Replace("[", "(").Replace("]", ")")}]({Wiki.BijlageAdres}{FileStoreId})";
}
public record WikiTreffer(WikiArtikel Artikel, string Fragment);

/// <summary>Een ander heeft het artikel intussen gewijzigd; opslaan zou diens werk overschrijven.</summary>
public class WikiConflictException : Exception
{
    public WikiConflictException(string tekst) : base(tekst) { }
}

/// <summary>
/// De wiki (opzet van Freek, fase 1; besluiten gebruiker 25-11-2026: de database is leidend, review komt later).
/// Ruimtes met artikelen in markdown. Elke keer opslaan maakt een versie; een oude versie terugzetten maakt er een nieuwe
/// van, en weghalen is "inactief" zetten: er verdwijnt nooit iets. De tekst staat in de database (WikiArtikelen.Inhoud
/// en WikiVersies), zodat zoeken een gewone query is; bijlagen en plaatjes staan in de filestore (soort 346,
/// MasterID = het artikel). Een artikel kan aan een project hangen. Verwijzen naar een ander artikel: [[Titel]].
/// Tabellen: Wiki_MariaDB.sql. Met <c>Wiki:Geheugen</c> staat alles in het geheugen (lokaal testen).
/// </summary>
public class Wiki
{
    public const int StoreType = 346;
    public const long MaxBijlage = 25_000_000;
    /// <summary>Het adres van een bijlage, ten opzichte van de basis van de app; het nummer komt erachter.</summary>
    public const string BijlageAdres = "wiki/bijlage/";
    public const string ArtikelAdres = "wiki?artikel=";
    /// <summary>
    /// Hoe diep de boom van ruimtes mag gaan (wens gebruiker 25-11-2026: meer dan drie lagen). Een grens blijft nodig:
    /// de zijbalk springt per laag in, en zo kan een fout nooit een eindeloze boom maken.
    /// </summary>
    public const int MaxDiepte = 8;
    /// <summary>Grootste HTML-bestand dat we in het kader tonen; groter is alleen te downloaden.</summary>
    public const long MaxVoorbeeld = 10_000_000;

    public static bool IsHtml(string? naam) => Path.GetExtension(naam ?? "").ToLowerInvariant() is ".html" or ".htm";

    private readonly string? _cs;
    private readonly bool _geheugen;
    private readonly FilestoreService _store;
    private bool _aanwezig;
    private bool _boom;         // heeft WikiRuimtes de kolom OuderID (Wiki_MariaDB.sql opnieuw gedraaid)?

    private readonly object _slot = new();
    private readonly List<WikiRuimte> _ruimtes = new();
    private readonly List<WikiArtikel> _artikelen = new();
    private readonly List<(int ArtikelId, WikiVersie V, string Inhoud)> _versies = new();
    private readonly List<(int ArtikelId, WikiBijlage B, byte[] Inhoud)> _bijlagen = new();
    private int _volgend = 1;

    public Wiki(IConfiguration config, FilestoreService store)
    {
        _cs = config.GetConnectionString("MDE");
        _geheugen = config.GetValue<bool>("Wiki:Geheugen");
        _store = store;
        if (_geheugen)
            foreach (var (n, i) in new[] { ("MDE Automation", "🏭"), ("Projecten", "📄"), ("Mechanica", "⚙️"), ("Software", "💻"),
                                           ("Elektro", "⚡"), ("Werkplaats", "🔧"), ("Financieel", "💵"), ("Management", "👔") })
                _ruimtes.Add(new WikiRuimte(_volgend++, n, i, "", 0));
    }

    private bool Live => !_geheugen && !string.IsNullOrWhiteSpace(_cs) && !_cs!.Contains("REPLACE_");
    public bool Beschikbaar => _geheugen || Live;
    private static string Kort(string? s, int max) { s = (s ?? "").Trim(); return s.Length <= max ? s : s[..max]; }

    public static IEnumerable<string> SplitsTags(string? tags)
        => (tags ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().TrimStart('#'))
                       .Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tags netjes: zonder #, ontdubbeld, hoogstens 20, met ", " ertussen.</summary>
    public static string NetteTags(string? tags) => Kort(string.Join(", ", SplitsTags(tags).Take(20)), 500);

    public async Task<bool> AanwezigAsync()
    {
        if (_geheugen) return true;
        if (_aanwezig || !Live) return _aanwezig;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('WikiRuimtes', 'WikiArtikelen', 'WikiVersies')", cn);
        return _aanwezig = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 3;
    }

    /// <summary>Kunnen ruimtes onder elkaar hangen? Zonder de kolom OuderID werkt de wiki gewoon, met een platte lijst.</summary>
    public async Task<bool> BoomMogelijkAsync()
    {
        if (_geheugen) return true;
        if (_boom || !await AanwezigAsync()) return _boom;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'WikiRuimtes' AND COLUMN_NAME = 'OuderID'", cn);
        return _boom = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    // ------------------------------------------------------------------ lezen

    public async Task<List<WikiRuimte>> RuimtesAsync()
    {
        if (_geheugen)
            lock (_slot) return _ruimtes.Select(r => r with { Aantal = _artikelen.Count(a => a.RuimteId == r.Id && a.Actief) }).ToList();
        var lijst = new List<WikiRuimte>();
        if (!await AanwezigAsync()) return lijst;
        var boom = await BoomMogelijkAsync();
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT r.`RuimteID`, r.`Naam`, COALESCE(r.`Icoon`, ''), COALESCE(r.`Omschrijving`, ''), " +
            "       (SELECT COUNT(*) FROM `WikiArtikelen` a WHERE a.`RuimteID` = r.`RuimteID` AND a.`Actief` <> 0), " +
            (boom ? "r.`OuderID`" : "NULL") +
            "  FROM `WikiRuimtes` r WHERE r.`Actief` <> 0 ORDER BY r.`Volgorde`, r.`Naam`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new WikiRuimte(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), Convert.ToInt32(r.GetValue(4)),
                                     r.IsDBNull(5) ? null : r.GetInt32(5)));
        // een ruimte waarvan de ouder niet (meer) in de lijst staat, komt bovenaan in plaats van te verdwijnen
        var ids = lijst.Select(x => x.Id).ToHashSet();
        return lijst.Select(x => x.OuderId is int o && !ids.Contains(o) ? x with { OuderId = null } : x).ToList();
    }

    /// <summary>
    /// Een ruimte (met alles eronder) onder een andere ruimte hangen; <paramref name="nieuweOuderId"/> leeg = bovenaan.
    /// Niet in zichzelf of in een van zijn subruimtes, niet dieper dan <see cref="MaxDiepte"/>, en de naam mag op de
    /// nieuwe plek nog niet voorkomen. Hij komt achteraan bij zijn nieuwe buren.
    /// </summary>
    public async Task VerplaatsRuimteAsync(int id, int? nieuweOuderId)
    {
        var alle = await RuimtesAsync();
        var deze = alle.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Deze ruimte bestaat niet (meer).");
        if (deze.OuderId == nieuweOuderId) return;
        if (nieuweOuderId is int doel)
        {
            if (alle.All(x => x.Id != doel)) throw new InvalidOperationException("De ruimte waar je hem in wilt zetten bestaat niet (meer).");
            for (var r = alle.FirstOrDefault(x => x.Id == doel); r is not null; r = alle.FirstOrDefault(x => x.Id == r.OuderId))
                if (r.Id == id) throw new InvalidOperationException("Een ruimte kan niet in zichzelf of in een van zijn eigen subruimtes.");
        }
        int Hoogte(int ruimte, int stop = 0)
            => stop > 20 ? 1 : 1 + alle.Where(x => x.OuderId == ruimte).Select(x => Hoogte(x.Id, stop + 1)).DefaultIfEmpty(0).Max();
        if (Diepte(alle, nieuweOuderId) + Hoogte(id) > MaxDiepte)
            throw new InvalidOperationException($"Daar wordt de boom dieper dan {MaxDiepte} lagen.");
        if (alle.Any(x => x.Id != id && x.OuderId == nieuweOuderId && string.Equals(x.Naam, deze.Naam, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Op die plek is er al een ruimte met de naam {deze.Naam}.");

        if (_geheugen)
        {
            lock (_slot)
            {
                _ruimtes.RemoveAll(x => x.Id == id);
                _ruimtes.Add(deze with { OuderId = nieuweOuderId, Aantal = 0 });
            }
            return;
        }
        if (!await BoomMogelijkAsync()) throw new InvalidOperationException("Draai Wiki_MariaDB.sql nog een keer: de kolom voor subruimtes ontbreekt.");
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        int volgorde;
        await using (var max = new MySqlCommand("SELECT COALESCE(MAX(`Volgorde`), 0) + 10 FROM `WikiRuimtes` WHERE `OuderID` <=> @p AND `RuimteID` <> @id", cn))
        {
            max.Parameters.AddWithValue("@p", (object?)nieuweOuderId ?? DBNull.Value);
            max.Parameters.AddWithValue("@id", id);
            volgorde = Convert.ToInt32(await max.ExecuteScalarAsync());
        }
        await using var cmd = new MySqlCommand("UPDATE `WikiRuimtes` SET `OuderID` = @p, `Volgorde` = @v WHERE `RuimteID` = @id", cn);
        cmd.Parameters.AddWithValue("@p", (object?)nieuweOuderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@v", volgorde);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Een lege ruimte wissen: echt weg. Leeg = geen subruimtes en geen enkel artikel, ook geen inactief (dat is terug te
    /// halen en zou anders zijn ruimte kwijt zijn). Anders een melding met wat er nog in staat.
    /// </summary>
    public async Task VerwijderRuimteAsync(int id)
    {
        int artikelen, subs;
        if (_geheugen)
            lock (_slot)
            {
                artikelen = _artikelen.Count(a => a.RuimteId == id);
                subs = _ruimtes.Count(r => r.OuderId == id);
                if (artikelen == 0 && subs == 0) { _ruimtes.RemoveAll(r => r.Id == id); return; }
            }
        else
        {
            var boom = await BoomMogelijkAsync();
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using (var tel = new MySqlCommand(
                "SELECT (SELECT COUNT(*) FROM `WikiArtikelen` WHERE `RuimteID` = @id), " +
                (boom ? "(SELECT COUNT(*) FROM `WikiRuimtes` WHERE `OuderID` = @id)" : "0"), cn))
            {
                tel.Parameters.AddWithValue("@id", id);
                await using var r = await tel.ExecuteReaderAsync();
                await r.ReadAsync();
                artikelen = Convert.ToInt32(r.GetValue(0));
                subs = Convert.ToInt32(r.GetValue(1));
            }
            if (artikelen == 0 && subs == 0)
            {
                await using var weg = new MySqlCommand("DELETE FROM `WikiRuimtes` WHERE `RuimteID` = @id", cn);
                weg.Parameters.AddWithValue("@id", id);
                await weg.ExecuteNonQueryAsync();
                return;
            }
        }
        var wat = new List<string>();
        if (subs > 0) wat.Add(subs == 1 ? "1 subruimte" : $"{subs} subruimtes");
        if (artikelen > 0) wat.Add(artikelen == 1 ? "1 artikel (ook een inactief artikel telt)" : $"{artikelen} artikelen (ook inactieve tellen)");
        throw new InvalidOperationException($"Deze ruimte is niet leeg: er {(subs + artikelen == 1 ? "staat" : "staan")} nog {string.Join(" en ", wat)} in. Verplaats die eerst.");
    }

    /// <summary>Hoe diep staat deze ruimte in de boom (1 = bovenaan)?</summary>
    public static int Diepte(IReadOnlyCollection<WikiRuimte> ruimtes, int? id)
    {
        var d = 0;
        for (var r = ruimtes.FirstOrDefault(x => x.Id == id); r is not null && d < 20; r = ruimtes.FirstOrDefault(x => x.Id == r.OuderId)) d++;
        return d;
    }

    /// <summary>
    /// Een ruimte toevoegen (Id 0; <paramref name="ouderId"/> = waaronder, leeg = bovenaan) of naam, icoon en
    /// omschrijving wijzigen. Onder dezelfde ouder mag een naam maar een keer voorkomen. Geeft het nummer.
    /// </summary>
    public async Task<int> BewaarRuimteAsync(int id, int? ouderId, string naam, string icoon, string omschrijving)
    {
        naam = Kort(naam, 100); icoon = Kort(icoon, 16); omschrijving = Kort(omschrijving, 300);
        if (naam.Length == 0) throw new InvalidOperationException("Geef de ruimte een naam.");
        var alle = await RuimtesAsync();
        if (id != 0) ouderId = alle.FirstOrDefault(x => x.Id == id)?.OuderId;      // verplaatsen gaat met VerplaatsRuimteAsync
        else if (ouderId is not null && Diepte(alle, ouderId) >= MaxDiepte)
            throw new InvalidOperationException($"Dieper dan {MaxDiepte} lagen kan niet.");
        if (alle.Any(x => x.Id != id && x.OuderId == ouderId && string.Equals(x.Naam, naam, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Op deze plek is er al een ruimte met die naam.");

        if (_geheugen)
            lock (_slot)
            {
                var i = _ruimtes.FindIndex(x => x.Id == id);
                if (i >= 0) { _ruimtes[i] = _ruimtes[i] with { Naam = naam, Icoon = icoon, Omschrijving = omschrijving }; return id; }
                var nieuw = new WikiRuimte(_volgend++, naam, icoon, omschrijving, 0, ouderId);
                _ruimtes.Add(nieuw);
                return nieuw.Id;
            }
        if (id == 0 && ouderId is not null && !await BoomMogelijkAsync())
            throw new InvalidOperationException("Draai Wiki_MariaDB.sql nog een keer: de kolom voor subruimtes ontbreekt.");
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        if (id != 0)
        {
            await using var bij = new MySqlCommand("UPDATE `WikiRuimtes` SET `Naam` = @n, `Icoon` = @i, `Omschrijving` = @o WHERE `RuimteID` = @id", cn);
            bij.Parameters.AddWithValue("@n", naam);
            bij.Parameters.AddWithValue("@i", icoon);
            bij.Parameters.AddWithValue("@o", omschrijving);
            bij.Parameters.AddWithValue("@id", id);
            await bij.ExecuteNonQueryAsync();
            return id;
        }
        // achteraan bij zijn broers: de hoogste volgorde plus tien
        await using var cmd = new MySqlCommand(
            (await BoomMogelijkAsync()
                ? "INSERT INTO `WikiRuimtes` (`Naam`, `Icoon`, `Omschrijving`, `Volgorde`, `Actief`, `OuderID`) " +
                  "SELECT @n, @i, @o, COALESCE(MAX(`Volgorde`), 0) + 10, 1, @p FROM `WikiRuimtes` WHERE `OuderID` <=> @p; "
                : "INSERT INTO `WikiRuimtes` (`Naam`, `Icoon`, `Omschrijving`, `Volgorde`, `Actief`) " +
                  "SELECT @n, @i, @o, COALESCE(MAX(`Volgorde`), 0) + 10, 1 FROM `WikiRuimtes`; ") +
            "SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@n", naam);
        cmd.Parameters.AddWithValue("@i", icoon);
        cmd.Parameters.AddWithValue("@o", omschrijving);
        cmd.Parameters.AddWithValue("@p", (object?)ouderId ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private const string Kolommen =
        "a.`ArtikelID`, a.`RuimteID`, a.`Titel`, COALESCE(a.`Sectie`, ''), COALESCE(a.`Tags`, ''), a.`Versie`, a.`Actief`, a.`ProjectID`, " +
        "TRIM(CONCAT(COALESCE(p.`Projectnumber`, ''), ' ', COALESCE(p.`Projectdescription`, ''))), a.`GewijzigdDoor`, COALESCE(w.`NaamVoluit`, ''), a.`GewijzigdOp`";
    private const string Van =
        " FROM `WikiArtikelen` a LEFT JOIN `Projects` p ON p.`Project-Id` = a.`ProjectID` LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = a.`GewijzigdDoor` ";

    private static WikiArtikel Lees(MySqlDataReader r, int inhoudKolom = -1) => new()
    {
        Id = r.GetInt32(0), RuimteId = r.GetInt32(1), Titel = r.GetString(2), Sectie = r.GetString(3), Tags = r.GetString(4),
        Versie = r.GetInt32(5), Actief = Convert.ToInt32(r.GetValue(6)) != 0, ProjectId = r.IsDBNull(7) ? null : r.GetInt32(7),
        ProjectTekst = r.IsDBNull(7) ? "" : r.GetString(8), GewijzigdDoorId = r.IsDBNull(9) ? null : r.GetInt32(9),
        GewijzigdDoor = r.GetString(10), GewijzigdOp = r.GetDateTime(11),
        Inhoud = inhoudKolom < 0 || r.IsDBNull(inhoudKolom) ? "" : r.GetString(inhoudKolom),
    };

    private static WikiArtikel Kopie(WikiArtikel a, bool metInhoud) => new()
    {
        Id = a.Id, RuimteId = a.RuimteId, Titel = a.Titel, Sectie = a.Sectie, Tags = a.Tags, Inhoud = metInhoud ? a.Inhoud : "", Versie = a.Versie,
        Actief = a.Actief, ProjectId = a.ProjectId, ProjectTekst = a.ProjectTekst, GewijzigdDoorId = a.GewijzigdDoorId,
        GewijzigdDoor = a.GewijzigdDoor, GewijzigdOp = a.GewijzigdOp,
    };

    private async Task<List<WikiArtikel>> LijstAsync(string waar, string volgorde, Action<MySqlCommand>? vul = null)
    {
        var lijst = new List<WikiArtikel>();
        if (!await AanwezigAsync()) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand($"SELECT {Kolommen}{Van} WHERE {waar} ORDER BY {volgorde}", cn);
        vul?.Invoke(cmd);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(Lees(r));
        return lijst;
    }

    /// <summary>De artikelen van een ruimte (zonder tekst), op sectie en titel.</summary>
    public async Task<List<WikiArtikel>> ArtikelenAsync(int ruimteId, bool ookInactief)
    {
        if (_geheugen)
            lock (_slot)
                return _artikelen.Where(a => a.RuimteId == ruimteId && (ookInactief || a.Actief))
                                 .OrderBy(a => a.Sectie).ThenBy(a => a.Titel).Select(a => Kopie(a, false)).ToList();
        return await LijstAsync("a.`RuimteID` = @r" + (ookInactief ? "" : " AND a.`Actief` <> 0"), "a.`Sectie`, a.`Titel`",
                                c => c.Parameters.AddWithValue("@r", ruimteId));
    }

    /// <summary>De laatst gewijzigde artikelen, over alle ruimtes.</summary>
    public async Task<List<WikiArtikel>> RecentAsync(int aantal = 12)
    {
        if (_geheugen) lock (_slot) return _artikelen.Where(a => a.Actief).OrderByDescending(a => a.GewijzigdOp).Take(aantal).Select(a => Kopie(a, false)).ToList();
        return await LijstAsync("a.`Actief` <> 0", "a.`GewijzigdOp` DESC LIMIT " + Math.Clamp(aantal, 1, 100));
    }

    /// <summary>De artikelen die bij een project horen.</summary>
    public async Task<List<WikiArtikel>> VanProjectAsync(int projectId)
    {
        if (_geheugen) lock (_slot) return _artikelen.Where(a => a.Actief && a.ProjectId == projectId).OrderBy(a => a.Titel).Select(a => Kopie(a, false)).ToList();
        return await LijstAsync("a.`Actief` <> 0 AND a.`ProjectID` = @p", "a.`Titel`", c => c.Parameters.AddWithValue("@p", projectId));
    }

    /// <summary>Een artikel met zijn tekst; null als het niet bestaat.</summary>
    public async Task<WikiArtikel?> ArtikelAsync(int id)
    {
        if (_geheugen) lock (_slot) return _artikelen.FirstOrDefault(a => a.Id == id) is { } g ? Kopie(g, true) : null;
        if (!await AanwezigAsync()) return null;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand($"SELECT {Kolommen}, a.`Inhoud`{Van} WHERE a.`ArtikelID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Lees(r, 12) : null;
    }

    /// <summary>Titel naar nummer van alle actieve artikelen, voor de verwijzingen [[Titel]].</summary>
    public async Task<Dictionary<string, int>> TitelsAsync()
    {
        var uit = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (_geheugen) { lock (_slot) foreach (var a in _artikelen.Where(a => a.Actief)) uit.TryAdd(a.Titel, a.Id); return uit; }
        if (!await AanwezigAsync()) return uit;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT `Titel`, `ArtikelID` FROM `WikiArtikelen` WHERE `Actief` <> 0 ORDER BY `ArtikelID`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit.TryAdd(r.GetString(0), r.GetInt32(1));
        return uit;
    }

    /// <summary>Bestaande secties van een ruimte en alle gebruikte tags, als voorstel bij het schrijven.</summary>
    public async Task<(List<string> Secties, List<string> Tags)> VoorstellenAsync(int ruimteId)
    {
        List<(int Ruimte, string Sectie, string Tags)> rijen = new();
        if (_geheugen) lock (_slot) rijen = _artikelen.Where(a => a.Actief).Select(a => (a.RuimteId, a.Sectie, a.Tags)).ToList();
        else if (await AanwezigAsync())
        {
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand("SELECT `RuimteID`, COALESCE(`Sectie`, ''), COALESCE(`Tags`, '') FROM `WikiArtikelen` WHERE `Actief` <> 0", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) rijen.Add((r.GetInt32(0), r.GetString(1), r.GetString(2)));
        }
        return (rijen.Where(x => x.Ruimte == ruimteId && x.Sectie.Length > 0).Select(x => x.Sectie).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList(),
                rijen.SelectMany(x => SplitsTags(x.Tags)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList());
    }

    /// <summary>Een stukje tekst rond de eerste plek waar de zoektekst staat.</summary>
    public static string Fragment(string tekst, string q)
    {
        // zonder de opmaaktekens van markdown, zodat het fragment gewone tekst is
        tekst = Regex.Replace(tekst ?? "", @"!?\[\[?|\]\]?(\([^)\s]*\))?|[*#`|>]+|^\s*[-+]\s", " ", RegexOptions.Multiline);
        tekst = Regex.Replace(tekst, @"\s+", " ").Trim();
        var i = q.Length == 0 ? -1 : tekst.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return tekst.Length <= 160 ? tekst : tekst[..160] + "…";
        var begin = Math.Max(0, i - 60);
        var eind = Math.Min(tekst.Length, i + q.Length + 100);
        return (begin > 0 ? "…" : "") + tekst[begin..eind] + (eind < tekst.Length ? "…" : "");
    }

    /// <summary>Zoeken in titel, tekst, sectie en tags van de actieve artikelen; een treffer in de titel staat bovenaan.</summary>
    public async Task<List<WikiTreffer>> ZoekAsync(string q, int max = 40)
    {
        q = (q ?? "").Trim();
        if (q.Length < 2) return new();
        bool In(string s) => s.Contains(q, StringComparison.OrdinalIgnoreCase);
        List<WikiArtikel> gevonden;
        if (_geheugen)
            lock (_slot) gevonden = _artikelen.Where(a => a.Actief && (In(a.Titel) || In(a.Inhoud) || In(a.Sectie) || In(a.Tags))).Select(a => Kopie(a, true)).ToList();
        else
        {
            gevonden = new();
            if (!await AanwezigAsync()) return new();
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand(
                $"SELECT {Kolommen}, a.`Inhoud`{Van} WHERE a.`Actief` <> 0 AND (a.`Titel` LIKE @q OR a.`Inhoud` LIKE @q OR a.`Sectie` LIKE @q OR a.`Tags` LIKE @q) " +
                " ORDER BY (a.`Titel` LIKE @q) DESC, a.`GewijzigdOp` DESC LIMIT @max", cn);
            cmd.Parameters.AddWithValue("@q", "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
            cmd.Parameters.AddWithValue("@max", Math.Clamp(max, 1, 200));
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) gevonden.Add(Lees(r, 12));
        }
        return gevonden.OrderByDescending(a => In(a.Titel)).ThenByDescending(a => a.GewijzigdOp).Take(max)
                       .Select(a => { var f = Fragment(a.Inhoud, q); a.Inhoud = ""; return new WikiTreffer(a, f); }).ToList();
    }

    // ------------------------------------------------------------------ schrijven

    /// <summary>
    /// Een artikel opslaan (Id 0 = nieuw); elke keer komt er een versie bij. <paramref name="a"/>.Versie is de versie
    /// waarop de schrijver begon: heeft een ander intussen opgeslagen, dan een <see cref="WikiConflictException"/>.
    /// Geeft het nummer van het artikel.
    /// </summary>
    public async Task<int> BewaarAsync(WikiArtikel a, string opmerking, int? door)
    {
        var titel = Kort(a.Titel, 200);
        if (titel.Length == 0) throw new InvalidOperationException("Geef het artikel een titel.");
        var sectie = Kort(a.Sectie, 100);
        var tags = NetteTags(a.Tags);
        var inhoud = a.Inhoud ?? "";
        if (inhoud.Length > 500_000) throw new InvalidOperationException("De tekst is te lang (meer dan 500.000 tekens).");
        opmerking = Kort(opmerking, 500);
        var nu = DateTime.Now;

        if (_geheugen)
            lock (_slot)
            {
                var g = _artikelen.FirstOrDefault(x => x.Id == a.Id);
                if (g is null) _artikelen.Add(g = new WikiArtikel { Id = _volgend++ });
                else if (g.Versie != a.Versie) throw new WikiConflictException(Conflicttekst(g.GewijzigdDoor, g.GewijzigdOp));
                g.RuimteId = a.RuimteId; g.Titel = titel; g.Sectie = sectie; g.Tags = tags; g.Inhoud = inhoud; g.Versie++;
                g.ProjectId = a.ProjectId; g.ProjectTekst = a.ProjectTekst; g.GewijzigdDoorId = door; g.GewijzigdOp = nu;
                _versies.Add((g.Id, new WikiVersie(_volgend++, g.Versie, titel, opmerking, "", nu), inhoud));
                return g.Id;
            }

        if (!await AanwezigAsync()) throw new InvalidOperationException("Wiki_MariaDB.sql is nog niet uitgevoerd.");
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        int id = a.Id, versie;
        if (id == 0)
        {
            versie = 1;
            await using var cmd = new MySqlCommand(
                "INSERT INTO `WikiArtikelen` (`RuimteID`, `Titel`, `Sectie`, `Tags`, `Inhoud`, `Versie`, `Actief`, `ProjectID`, `AangemaaktDoor`, `AangemaaktOp`, `GewijzigdDoor`, `GewijzigdOp`) " +
                "VALUES (@r, @t, @s, @g, @i, 1, 1, @p, @d, @nu, @d, @nu); SELECT LAST_INSERT_ID();", cn, tx);
            Vul(cmd);
            id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        else
        {
            versie = a.Versie + 1;
            // alleen als de versie nog die van de schrijver is: zo overschrijft niemand ongemerkt het werk van een ander
            await using var cmd = new MySqlCommand(
                "UPDATE `WikiArtikelen` SET `RuimteID` = @r, `Titel` = @t, `Sectie` = @s, `Tags` = @g, `Inhoud` = @i, `Versie` = `Versie` + 1, " +
                "`ProjectID` = @p, `GewijzigdDoor` = @d, `GewijzigdOp` = @nu WHERE `ArtikelID` = @id AND `Versie` = @v", cn, tx);
            Vul(cmd);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@v", a.Versie);
            if (await cmd.ExecuteNonQueryAsync() == 0)
            {
                await tx.RollbackAsync();
                var nuStaat = await ArtikelAsync(id);
                throw new WikiConflictException(nuStaat is null ? "Dit artikel bestaat niet meer." : Conflicttekst(nuStaat.GewijzigdDoor, nuStaat.GewijzigdOp));
            }
        }
        await using (var cmd = new MySqlCommand(
            "INSERT INTO `WikiVersies` (`ArtikelID`, `Versie`, `Titel`, `Inhoud`, `Opmerking`, `Door`, `Op`) VALUES (@id, @v, @t, @i, @o, @d, @nu)", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@v", versie);
            cmd.Parameters.AddWithValue("@t", titel);
            cmd.Parameters.AddWithValue("@i", inhoud);
            cmd.Parameters.AddWithValue("@o", opmerking);
            cmd.Parameters.AddWithValue("@d", (object?)door ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@nu", nu);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return id;

        void Vul(MySqlCommand cmd)
        {
            cmd.Parameters.AddWithValue("@r", a.RuimteId);
            cmd.Parameters.AddWithValue("@t", titel);
            cmd.Parameters.AddWithValue("@s", sectie);
            cmd.Parameters.AddWithValue("@g", tags);
            cmd.Parameters.AddWithValue("@i", inhoud);
            cmd.Parameters.AddWithValue("@p", (object?)a.ProjectId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@d", (object?)door ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@nu", nu);
        }
    }

    private static string Conflicttekst(string door, DateTime op)
        => $"{(door.Length > 0 ? door : "Iemand")} heeft dit artikel om {op:HH:mm} opgeslagen terwijl jij schreef. " +
           "Kopieer je tekst, open het artikel opnieuw en zet je wijziging erin.";

    public async Task<List<WikiVersie>> VersiesAsync(int artikelId)
    {
        if (_geheugen) lock (_slot) return _versies.Where(v => v.ArtikelId == artikelId).Select(v => v.V).OrderByDescending(v => v.Versie).ToList();
        var lijst = new List<WikiVersie>();
        if (!await AanwezigAsync()) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT v.`VersieID`, v.`Versie`, v.`Titel`, COALESCE(v.`Opmerking`, ''), COALESCE(w.`NaamVoluit`, ''), v.`Op` " +
            "  FROM `WikiVersies` v LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = v.`Door` WHERE v.`ArtikelID` = @id ORDER BY v.`Versie` DESC", cn);
        cmd.Parameters.AddWithValue("@id", artikelId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new WikiVersie(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetDateTime(5)));
        return lijst;
    }

    /// <summary>De tekst van een oude versie; null als hij niet bestaat.</summary>
    public async Task<string?> VersieInhoudAsync(int versieId)
    {
        if (_geheugen) lock (_slot) return _versies.Where(v => v.V.Id == versieId).Select(v => v.Inhoud).FirstOrDefault();
        if (!await AanwezigAsync()) return null;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT COALESCE(`Inhoud`, '') FROM `WikiVersies` WHERE `VersieID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", versieId);
        return await cmd.ExecuteScalarAsync() as string;
    }

    /// <summary>Een oude versie terugzetten: titel en tekst van toen worden een nieuwe versie.</summary>
    public async Task TerugzettenAsync(int artikelId, WikiVersie oud, int? door)
    {
        var a = await ArtikelAsync(artikelId) ?? throw new InvalidOperationException("Dit artikel bestaat niet meer.");
        a.Inhoud = await VersieInhoudAsync(oud.Id) ?? throw new InvalidOperationException("Deze versie bestaat niet meer.");
        a.Titel = oud.Titel;
        await BewaarAsync(a, $"Teruggezet van v{oud.Versie}", door);
    }

    /// <summary>
    /// Een artikel naar een andere ruimte zetten (slepen op het scherm). Geen nieuwe versie: de tekst verandert niet;
    /// wie en wanneer blijven die van de laatste echte wijziging.
    /// </summary>
    public async Task VerplaatsAsync(int artikelId, int ruimteId)
    {
        if ((await RuimtesAsync()).All(r => r.Id != ruimteId)) throw new InvalidOperationException("Deze ruimte bestaat niet (meer).");
        if (_geheugen) { lock (_slot) if (_artikelen.FirstOrDefault(x => x.Id == artikelId) is { } g) g.RuimteId = ruimteId; return; }
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `WikiArtikelen` SET `RuimteID` = @r WHERE `ArtikelID` = @id", cn);
        cmd.Parameters.AddWithValue("@r", ruimteId);
        cmd.Parameters.AddWithValue("@id", artikelId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Inactief zetten (of terughalen): het artikel verdwijnt uit de lijsten en het zoeken, maar blijft bestaan.</summary>
    public async Task ZetActiefAsync(int artikelId, bool actief)
    {
        if (_geheugen) { lock (_slot) if (_artikelen.FirstOrDefault(x => x.Id == artikelId) is { } g) g.Actief = actief; return; }
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `WikiArtikelen` SET `Actief` = @a WHERE `ArtikelID` = @id", cn);
        cmd.Parameters.AddWithValue("@a", actief ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", artikelId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ------------------------------------------------------------------ bijlagen (filestore, soort 346)

    public async Task<List<WikiBijlage>> BijlagenAsync(int artikelId)
    {
        if (_geheugen) lock (_slot) return _bijlagen.Where(b => b.ArtikelId == artikelId).Select(b => b.B).ToList();
        var lijst = new List<WikiBijlage>();
        if (!await AanwezigAsync()) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT `FileStoreID`, COALESCE(`FileOnly`, `FileNaam`) FROM `FileStore` WHERE `StoreType` = {StoreType} AND `MasterID` = @id ORDER BY `FileStoreID`", cn);
        cmd.Parameters.AddWithValue("@id", artikelId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new WikiBijlage(r.GetInt32(0), r.GetString(1)));
        return lijst;
    }

    public async Task<WikiBijlage> BijlageToevoegenAsync(int artikelId, string naam, byte[] inhoud, int? door)
    {
        naam = Path.GetFileName(naam ?? "");
        if (naam.Length == 0) naam = "bijlage";
        if (_geheugen)
            lock (_slot)
            {
                var b = new WikiBijlage(_volgend++, naam);
                _bijlagen.Add((artikelId, b, inhoud));
                return b;
            }
        // de filestore wil weten van wie het bestand is (FK ToegevoegdDoor -> Werknemers)
        if (door is not int wie) throw new InvalidOperationException("Niet bekend wie je bent; meld je (opnieuw) aan en probeer het nog eens.");
        using var m = new MemoryStream(inhoud);
        return new WikiBijlage(await _store.ToevoegenAsync(m, naam, StoreType, artikelId, wie), naam);
    }

    /// <summary>
    /// Een bijlage echt wissen: het bestand uit de filestore en de regel erbij (wens gebruiker 25-11-2026; het enige in
    /// de wiki dat wel verdwijnt). Alleen als hij bij dit artikel hoort. False = hij stond er niet (meer).
    /// </summary>
    public async Task<bool> BijlageVerwijderAsync(int artikelId, int fileStoreId)
    {
        if (_geheugen) lock (_slot) return _bijlagen.RemoveAll(b => b.ArtikelId == artikelId && b.B.FileStoreId == fileStoreId) > 0;
        if ((await BijlagenAsync(artikelId)).All(b => b.FileStoreId != fileStoreId)) return false;
        await _store.VerwijderAsync(fileStoreId);
        return true;
    }

    /// <summary>Een bijlage om te tonen of te downloaden: uit het geheugen of het pad in de filestore.</summary>
    public async Task<(byte[]? Inhoud, string? Pad, string Naam)?> BijlageAsync(int fileStoreId)
    {
        if (_geheugen)
            lock (_slot) return _bijlagen.Where(b => b.B.FileStoreId == fileStoreId).Select(b => ((byte[]?)b.Inhoud, (string?)null, b.B.Naam)).Cast<(byte[]?, string?, string)?>().FirstOrDefault();
        // alleen onze eigen soort: dit adres mag geen andere bestanden uit de filestore tonen
        return await _store.ZoekAsync(fileStoreId) is { } f && f.StoreType == StoreType ? (null, f.Pad, f.Naam) : null;
    }

    // ------------------------------------------------------------------ verwijzingen [[Titel]]

    private static readonly Regex Verwijzing = new(@"\[\[([^\[\]\|\r\n]{1,200})(?:\|([^\[\]\r\n]{1,200}))?\]\]", RegexOptions.Compiled);

    /// <summary>
    /// [[Titel]] en [[Titel|andere tekst]] worden een link naar dat artikel; bestaat het (nog) niet, dan blijft de tekst
    /// staan met een vraagteken erachter.
    /// </summary>
    public static string MetVerwijzingen(string markdown, IReadOnlyDictionary<string, int> titels)
        => Verwijzing.Replace(markdown ?? "", m =>
        {
            var titel = m.Groups[1].Value.Trim();
            var tekst = (m.Groups[2].Success ? m.Groups[2].Value : titel).Trim().Replace("[", "(").Replace("]", ")");
            return titels.TryGetValue(titel, out var id) ? $"[{tekst}]({ArtikelAdres}{id})" : $"{tekst} *(?)*";
        });
}

public static class WikiApi
{
    /// <summary>
    /// Wat een HTML-bijlage in het voorbeeld mag (kop Content-Security-Policy). <c>sandbox allow-scripts</c> geeft het
    /// bestand een eigen, afgesloten herkomst: het eigen script werkt (veel van deze bestanden zijn klikbare proefopzetten),
    /// maar het kan niet bij de app, de aanmelding of de gegevens. Verder niets van buiten laden en niets versturen.
    /// De kop geldt ook als iemand het adres los in een tabblad opent.
    /// </summary>
    public const string VoorbeeldBeleid =
        "sandbox allow-scripts; default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; " +
        "img-src data: blob:; font-src data:; media-src data: blob:; connect-src 'none'; form-action 'none'; base-uri 'none'";

    /// <summary>Een bijlage of plaatje van een artikel tonen of downloaden, en een HTML-bijlage bekijken.</summary>
    public static RouteGroupBuilder MapWiki(this WebApplication app)
    {
        var api = app.MapGroup("/wiki/bijlage");

        api.MapGet("/{id:int}", async (int id, Wiki wiki, HttpContext http) =>
        {
            if (await wiki.BijlageAsync(id) is not { } b) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=3600";
            var plaatje = ActAfbeeldingen.IsAfbeelding(b.Naam);
            var mime = plaatje ? FilestoreService.MimeVoor(b.Naam) : "application/octet-stream";
            // geen plaatje = altijd als download, zodat een geüpload html-bestand nooit als pagina van de app opent
            var naam = plaatje ? null : b.Naam;
            return b.Inhoud is not null ? Results.File(b.Inhoud, mime, naam) : Results.File(b.Pad!, mime, naam);
        });

        // het voorbeeld van een HTML-bijlage: wel als pagina, maar afgeschermd (VoorbeeldBeleid)
        api.MapGet("/{id:int}/voorbeeld", async (int id, Wiki wiki, HttpContext http) =>
        {
            if (await wiki.BijlageAsync(id) is not { } b || !Wiki.IsHtml(b.Naam)) return Results.NotFound();
            var grootte = b.Inhoud?.LongLength ?? new FileInfo(b.Pad!).Length;
            if (grootte > Wiki.MaxVoorbeeld) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            http.Response.Headers.ContentSecurityPolicy = VoorbeeldBeleid;
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.CacheControl = "private, max-age=300";
            const string mime = "text/html; charset=utf-8";
            return b.Inhoud is not null ? Results.File(b.Inhoud, mime) : Results.File(b.Pad!, mime);
        });

        return api;
    }
}
