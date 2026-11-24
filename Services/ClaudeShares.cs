using System.Text;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een versie van een document; de inhoud zelf komt apart (<see cref="ClaudeShares.InhoudAsync"/>).</summary>
public record ClaudeShareVersie(int Id, int Versie, string Bestandsnaam, string Opmerking, int? DoorId, string Door, DateTime Op, int Grootte)
{
    public bool IsTekst => !Bestandsnaam.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Een document bij een project, met zijn versies (nieuwste eerst).</summary>
public class ClaudeShare
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Naam { get; set; } = "";
    public bool Vervallen { get; set; }
    public int? DoorId { get; set; }
    public List<ClaudeShareVersie> Versies { get; set; } = new();
    public ClaudeShareVersie? Huidig => Versies.FirstOrDefault();
}

/// <summary>Een project waar documenten bij staan.</summary>
public record ClaudeShareProject(int ProjectId, string Tekst, int Aantal);

/// <summary>
/// Tools ▸ Claude Shares (wens van Arnold, 24-11-2026): .md- en .txt-bestanden bij een project, zodat een collega met de
/// kennis uit een Claude-gesprek (PLC, HMI, Aurora) verder kan. Elk document heeft versies; de hoogste is de geldende,
/// de rest is het archief. Terugzetten maakt van een oude versie een nieuwe (er verdwijnt nooit iets), en verwijderen is
/// "vervallen" zetten. Elke versie is een bestand in de filestore (soort 345, MasterID = het document; keuze gebruiker
/// 24-11-2026); de tabellen (ClaudeShares_MariaDB.sql) bewaren naam, versienummer, opmerking en de verwijzing. Met
/// ClaudeShares:Geheugen staat alles in het geheugen (lokaal testen zonder de tabellen en zonder de filestore).
/// </summary>
public class ClaudeShares
{
    /// <summary>FileStore.StoreType van een versie van een document (MasterID = ShareID). Moet in FileStoreType staan.</summary>
    public const int StoreType = 345;

    /// <summary>Grootste bestand: 2 MB tekst is al een heel boek.</summary>
    public const int MaxGrootte = 2 * 1024 * 1024;

    private readonly string? _cs;
    private readonly bool _geheugen;
    private readonly FilestoreService _store;
    private bool _aanwezig;

    private readonly object _slot = new();
    private readonly List<ClaudeShare> _shares = new();
    private readonly Dictionary<int, string> _inhoud = new();          // versie-id -> tekst
    private readonly Dictionary<int, string> _projectTekst = new();    // alleen in het geheugen: wat de pagina van het project wist
    private int _volgend = 1;

    public ClaudeShares(IConfiguration config, FilestoreService store)
    {
        _cs = config.GetConnectionString("MDE");
        _geheugen = config.GetValue<bool>("ClaudeShares:Geheugen");
        _store = store;
    }

    private bool Live => !_geheugen && !string.IsNullOrWhiteSpace(_cs) && !_cs!.Contains("REPLACE_");
    private static string Kort(string? s, int max) { s = (s ?? "").Trim(); return s.Length <= max ? s : s[..max]; }

    public static bool IsToegestaan(string bestandsnaam)
        => Path.GetExtension(bestandsnaam).ToLowerInvariant() is ".md" or ".txt";

    /// <summary>De bytes van een upload als tekst (UTF-8, zonder BOM).</summary>
    public static string Tekst(byte[] bytes)
    {
        var s = Encoding.UTF8.GetString(bytes);
        return s.Length > 0 && s[0] == '﻿' ? s[1..] : s;
    }

    /// <summary>De naam die we voorstellen bij een nieuw bestand: de bestandsnaam zonder extensie.</summary>
    public static string NaamUit(string bestandsnaam) => Kort(Path.GetFileNameWithoutExtension(bestandsnaam).Replace('_', ' '), 150);

    public async Task<bool> AanwezigAsync()
    {
        if (_geheugen) return true;
        if (_aanwezig || !Live) return _aanwezig;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ClaudeShareVersies' AND COLUMN_NAME = 'FileStoreID'", cn);
        return _aanwezig = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    /// <summary>In het geheugen is er geen tabel Projects om de naam uit te halen; de pagina geeft hem mee.</summary>
    public void OnthoudProject(int projectId, string tekst)
    {
        if (_geheugen) lock (_slot) _projectTekst[projectId] = tekst;
    }

    // ------------------------------------------------------------------ lezen

    /// <summary>De projecten waar (niet vervallen) documenten bij staan, hoogste projectnummer eerst.</summary>
    public async Task<List<ClaudeShareProject>> ProjectenAsync()
    {
        if (_geheugen)
            lock (_slot)
                return _shares.Where(s => !s.Vervallen).GroupBy(s => s.ProjectId)
                    .Select(g => new ClaudeShareProject(g.Key, _projectTekst.GetValueOrDefault(g.Key, "project " + g.Key), g.Count()))
                    .OrderByDescending(p => p.Tekst).ToList();
        var lijst = new List<ClaudeShareProject>();
        if (!await AanwezigAsync()) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT s.`ProjectID`, p.`Projectnumber`, COALESCE(p.`Projectdescription`, ''), COUNT(*) " +
            "  FROM `ClaudeShares` s LEFT JOIN `Projects` p ON p.`Project-Id` = s.`ProjectID` " +
            " WHERE s.`Vervallen` = 0 GROUP BY s.`ProjectID`, p.`Projectnumber`, p.`Projectdescription` " +
            " ORDER BY p.`Projectnumber` DESC", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new ClaudeShareProject(r.GetInt32(0),
                ((r.IsDBNull(1) ? "" : r.GetValue(1).ToString()) + " " + r.GetString(2)).Trim(), Convert.ToInt32(r.GetValue(3))));
        return lijst;
    }

    /// <summary>De documenten van een project met al hun versies (zonder de tekst), op naam.</summary>
    public async Task<List<ClaudeShare>> VanProjectAsync(int projectId, bool ookVervallen)
    {
        if (_geheugen)
            lock (_slot)
                return _shares.Where(s => s.ProjectId == projectId && (ookVervallen || !s.Vervallen)).OrderBy(s => s.Naam).Select(Kopie).ToList();
        var lijst = new List<ClaudeShare>();
        if (!await AanwezigAsync()) return lijst;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using (var cmd = new MySqlCommand(
            "SELECT `ShareID`, `Naam`, `Vervallen`, `AangemaaktDoor` FROM `ClaudeShares` " +
            " WHERE `ProjectID` = @p" + (ookVervallen ? "" : " AND `Vervallen` = 0") + " ORDER BY `Naam`", cn))
        {
            cmd.Parameters.AddWithValue("@p", projectId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                lijst.Add(new ClaudeShare
                {
                    Id = r.GetInt32(0), ProjectId = projectId, Naam = r.GetString(1),
                    Vervallen = Convert.ToInt32(r.GetValue(2)) != 0, DoorId = r.IsDBNull(3) ? null : r.GetInt32(3),
                });
        }
        if (lijst.Count == 0) return lijst;
        var opId = lijst.ToDictionary(s => s.Id);
        var ids = string.Join(",", opId.Keys);      // getallen uit de database zelf
        await using (var cmd = new MySqlCommand(
            "SELECT v.`VersieID`, v.`ShareID`, v.`Versie`, v.`Bestandsnaam`, COALESCE(v.`Opmerking`, ''), v.`Door`, " +
            "       COALESCE(w.`NaamVoluit`, ''), v.`Op`, v.`Grootte` " +
            "  FROM `ClaudeShareVersies` v LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = v.`Door` " +
            $" WHERE v.`ShareID` IN ({ids}) ORDER BY v.`Versie` DESC", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                opId[r.GetInt32(1)].Versies.Add(new ClaudeShareVersie(r.GetInt32(0), r.GetInt32(2), r.GetString(3), r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetInt32(5), r.GetString(6), r.GetDateTime(7), r.IsDBNull(8) ? 0 : Convert.ToInt32(r.GetValue(8))));
        return lijst;
    }

    /// <summary>De tekst van een versie, met de bestandsnaam; null als hij niet bestaat.</summary>
    public async Task<(string Inhoud, string Bestandsnaam)?> InhoudAsync(int versieId)
    {
        if (_geheugen)
            lock (_slot)
            {
                var v = _shares.SelectMany(s => s.Versies).FirstOrDefault(x => x.Id == versieId);
                return v is null ? null : (_inhoud.GetValueOrDefault(versieId, ""), v.Bestandsnaam);
            }
        if (!await AanwezigAsync()) return null;
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        int fileStoreId;
        string bestandsnaam;
        await using (var cmd = new MySqlCommand("SELECT `FileStoreID`, `Bestandsnaam` FROM `ClaudeShareVersies` WHERE `VersieID` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", versieId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            fileStoreId = r.IsDBNull(0) ? 0 : r.GetInt32(0);
            bestandsnaam = r.GetString(1);
        }
        // alleen een bestand van onze eigen soort: een verkeerd nummer in de tabel mag nooit een ander bestand tonen
        if (await _store.ZoekAsync(fileStoreId) is not { } f || f.StoreType != StoreType)
            throw new InvalidOperationException("Het bestand van deze versie staat niet (meer) in de filestore.");
        return (Tekst(await File.ReadAllBytesAsync(f.Pad)), bestandsnaam);
    }

    private static ClaudeShare Kopie(ClaudeShare s) => new()
    {
        Id = s.Id, ProjectId = s.ProjectId, Naam = s.Naam, Vervallen = s.Vervallen, DoorId = s.DoorId, Versies = s.Versies.ToList(),
    };

    // ------------------------------------------------------------------ schrijven

    /// <summary>Een nieuw document met zijn eerste versie; geeft het id van het document.</summary>
    public async Task<int> NieuwAsync(int projectId, string naam, string bestandsnaam, string inhoud, string opmerking, int? door)
    {
        naam = Kort(naam, 150);
        if (naam.Length == 0) naam = NaamUit(bestandsnaam);
        if (_geheugen)
        {
            int id;
            lock (_slot)
            {
                id = _volgend++;
                _shares.Add(new ClaudeShare { Id = id, ProjectId = projectId, Naam = naam, DoorId = door });
            }
            await NieuweVersieAsync(id, bestandsnaam, inhoud, opmerking, door);
            return id;
        }
        if (!await AanwezigAsync()) throw new InvalidOperationException("ClaudeShares_MariaDB.sql is nog niet (opnieuw) uitgevoerd.");
        if (door is null) throw new InvalidOperationException("Niet bekend wie je bent; meld je (opnieuw) aan en probeer het nog eens.");
        int shareId;
        await using (var cn = new MySqlConnection(_cs))
        {
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "INSERT INTO `ClaudeShares` (`ProjectID`, `Naam`, `Vervallen`, `AangemaaktDoor`, `AangemaaktOp`) VALUES (@p, @n, 0, @d, NOW()); SELECT LAST_INSERT_ID();", cn);
            cmd.Parameters.AddWithValue("@p", projectId);
            cmd.Parameters.AddWithValue("@n", naam);
            cmd.Parameters.AddWithValue("@d", (object?)door ?? DBNull.Value);
            shareId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        try { await NieuweVersieAsync(shareId, bestandsnaam, inhoud, opmerking, door); }
        catch
        {
            // zonder eerste versie is het geen document: niet als lege regel laten staan
            try { await DoeAsync("DELETE FROM `ClaudeShares` WHERE `ShareID` = @id AND @w = 0 AND NOT EXISTS (SELECT 1 FROM `ClaudeShareVersies` v WHERE v.`ShareID` = @id)", shareId, 0); } catch { }
            throw;
        }
        return shareId;
    }

    /// <summary>Een nieuwe versie bij een bestaand document: die wordt de geldende, de vorige gaat het archief in. Geeft het versienummer.</summary>
    public async Task<int> NieuweVersieAsync(int shareId, string bestandsnaam, string inhoud, string opmerking, int? door)
    {
        bestandsnaam = Kort(Path.GetFileName(bestandsnaam), 255);
        opmerking = Kort(opmerking, 500);
        if (_geheugen)
            lock (_slot)
            {
                var s = _shares.First(x => x.Id == shareId);
                var nr = (s.Versies.Count == 0 ? 0 : s.Versies.Max(v => v.Versie)) + 1;
                var id = _volgend++;
                s.Versies.Insert(0, new ClaudeShareVersie(id, nr, bestandsnaam, opmerking, door, "", DateTime.Now, inhoud.Length));
                _inhoud[id] = inhoud;
                return nr;
            }
        if (!await AanwezigAsync()) throw new InvalidOperationException("ClaudeShares_MariaDB.sql is nog niet (opnieuw) uitgevoerd.");
        // de filestore wil weten van wie het bestand is (FK ToegevoegdDoor -> Werknemers)
        if (door is not int wie) throw new InvalidOperationException("Niet bekend wie je bent; meld je (opnieuw) aan en probeer het nog eens.");

        // eerst het bestand (UTF-8 zonder BOM), dan de regel die ernaar verwijst
        int fileStoreId;
        using (var m = new MemoryStream(Encoding.UTF8.GetBytes(inhoud)))
            fileStoreId = await _store.ToevoegenAsync(m, bestandsnaam, StoreType, shareId, wie);
        try
        {
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            // het nummer in dezelfde opdracht bepalen; UNIQUE (ShareID, Versie) vangt twee gelijktijdige uploads (dan nog een keer)
            for (var poging = 0; ; poging++)
            {
                try
                {
                    await using var cmd = new MySqlCommand(
                        "INSERT INTO `ClaudeShareVersies` (`ShareID`, `Versie`, `Bestandsnaam`, `FileStoreID`, `Grootte`, `Opmerking`, `Door`, `Op`) " +
                        "SELECT @s, COALESCE(MAX(`Versie`), 0) + 1, @b, @f, @g, @o, @d, NOW() FROM `ClaudeShareVersies` WHERE `ShareID` = @s; " +
                        "SELECT MAX(`Versie`) FROM `ClaudeShareVersies` WHERE `ShareID` = @s;", cn);
                    cmd.Parameters.AddWithValue("@s", shareId);
                    cmd.Parameters.AddWithValue("@b", bestandsnaam);
                    cmd.Parameters.AddWithValue("@f", fileStoreId);
                    cmd.Parameters.AddWithValue("@g", inhoud.Length);
                    cmd.Parameters.AddWithValue("@o", opmerking);
                    cmd.Parameters.AddWithValue("@d", wie);
                    return Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }
                catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry && poging < 3) { }
            }
        }
        catch
        {
            // de regel is er niet gekomen: het bestand niet los in de filestore laten staan
            try { await _store.VerwijderAsync(fileStoreId); } catch { }
            throw;
        }
    }

    /// <summary>Een oude versie terugzetten: zijn tekst wordt een nieuwe versie (de tussenliggende blijven in het archief).</summary>
    public async Task<int> TerugzettenAsync(int shareId, ClaudeShareVersie oud, int? door)
    {
        var x = await InhoudAsync(oud.Id) ?? throw new InvalidOperationException("Deze versie bestaat niet meer.");
        return await NieuweVersieAsync(shareId, x.Bestandsnaam, x.Inhoud, $"Teruggezet van v{oud.Versie}", door);
    }

    public async Task HernoemAsync(int shareId, string naam)
    {
        naam = Kort(naam, 150);
        if (naam.Length == 0) return;
        if (_geheugen) { lock (_slot) _shares.First(x => x.Id == shareId).Naam = naam; return; }
        await DoeAsync("UPDATE `ClaudeShares` SET `Naam` = @w WHERE `ShareID` = @id", shareId, naam);
    }

    /// <summary>Vervallen zetten (of terughalen): het document verdwijnt uit de lijst maar blijft bestaan.</summary>
    public async Task ZetVervallenAsync(int shareId, bool vervallen)
    {
        if (_geheugen) { lock (_slot) _shares.First(x => x.Id == shareId).Vervallen = vervallen; return; }
        await DoeAsync("UPDATE `ClaudeShares` SET `Vervallen` = @w WHERE `ShareID` = @id", shareId, vervallen ? 1 : 0);
    }

    private async Task DoeAsync(string sql, int id, object waarde)
    {
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@w", waarde);
        await cmd.ExecuteNonQueryAsync();
    }
}

public static class ClaudeSharesApi
{
    /// <summary>Een versie downloaden als bestand (de knop Downloaden op Tools ▸ Claude Shares).</summary>
    public static RouteHandlerBuilder MapClaudeShares(this WebApplication app)
        => app.MapGet("/claude-shares/versie/{id:int}", async (int id, ClaudeShares shares, HuidigeGebruiker ik) =>
        {
            await ik.ZorgGeladenAsync();
            if (!ik.MagEngineering) return Results.StatusCode(403);
            if (await shares.InhoudAsync(id) is not { } x) return Results.NotFound();
            // met BOM, zodat Kladblok en Word de letters met accenten goed lezen
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(x.Inhoud)).ToArray();
            return Results.File(bytes, "text/plain; charset=utf-8", x.Bestandsnaam);
        });
}
