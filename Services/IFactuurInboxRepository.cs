using System.Collections.Concurrent;
using System.Text.Json;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Eén factuur of bonnetje dat nog beoordeeld moet worden (tabel FactuurInbox, FactuurInbox_MariaDB.sql).
/// Bron 'mail' = uit de postbus invoice@ (FactuurInboxOphaler), 'bon' = gescand op de telefoon (MDE mobiel ▸ Bonnetje).
/// </summary>
public class InboxStuk
{
    public int Id { get; set; }
    public string Bron { get; set; } = "";
    public DateTime Ontvangen { get; set; }
    public int? DoorWerknemer { get; set; }
    public string DoorNaam { get; set; } = "";
    public string MailId { get; set; } = "";
    public string BijlageNaam { get; set; } = "";
    public string MailVan { get; set; } = "";
    public string MailOnderwerp { get; set; } = "";
    public string MailTekst { get; set; } = "";
    public string Soort { get; set; } = "";
    public int? WerkbonId { get; set; }
    /// <summary>Werkorder als tekst (nummer · omschrijving), alleen bij het lezen gevuld.</summary>
    public string Werkorder { get; set; } = "";
    public string Opmerking { get; set; } = "";
    public string Melding { get; set; } = "";
    /// <summary>Wat de AI las, als JSON (<see cref="InboxGelezen"/>).</summary>
    public string Gelezen { get; set; } = "";
    public List<string> Bestanden { get; set; } = new();

    public bool IsBon => Bron == "bon";
}

/// <summary>Een uitgelezen factuur (FactuurUitPdf) bewaren en terughalen; Gevonden heeft geen setter, dus apart.</summary>
public static class InboxGelezen
{
    private sealed class Opslag
    {
        public FactuurUitPdf Uit { get; set; } = new();
        public List<string> Gevonden { get; set; } = new();
    }

    public static string NaarJson(FactuurUitPdf? uit)
        => uit is null ? "" : JsonSerializer.Serialize(new Opslag { Uit = uit, Gevonden = uit.Gevonden.ToList() });

    public static FactuurUitPdf? VanJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var o = JsonSerializer.Deserialize<Opslag>(json);
            if (o is null) return null;
            foreach (var g in o.Gevonden) o.Uit.Gevonden.Add(g);
            return o.Uit;
        }
        catch { return null; }
    }
}

public interface IFactuurInboxRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();
    /// <summary>Wat nog beoordeeld moet worden, oudste eerst.</summary>
    Task<List<InboxStuk>> TeBeoordelenAsync(int max);
    Task<int> AantalTeBeoordelenAsync();
    Task<List<(string Naam, byte[] Inhoud)>> BestandenAsync(int inboxId);
    /// <summary>Welke van deze Gmail-berichten staan er al in (ook verwerkt of genegeerd)?</summary>
    Task<HashSet<string>> BekendeMailsAsync(IReadOnlyCollection<string> mailIds);
    Task<int> VoegToeAsync(InboxStuk stuk, IReadOnlyList<(string Naam, byte[] Inhoud)> bestanden);
    /// <summary>Status 1 = verwerkt (met de factuur die eruit ontstond), 2 = genegeerd.</summary>
    Task AfhandelenAsync(int inboxId, int status, int? factuurId, int? door);
}

public class GeenFactuurInboxRepository : IFactuurInboxRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<InboxStuk>> TeBeoordelenAsync(int max) => Task.FromResult(new List<InboxStuk>());
    public Task<int> AantalTeBeoordelenAsync() => Task.FromResult(0);
    public Task<List<(string Naam, byte[] Inhoud)>> BestandenAsync(int inboxId) => Task.FromResult(new List<(string, byte[])>());
    public Task<HashSet<string>> BekendeMailsAsync(IReadOnlyCollection<string> mailIds) => Task.FromResult(new HashSet<string>());
    public Task<int> VoegToeAsync(InboxStuk stuk, IReadOnlyList<(string Naam, byte[] Inhoud)> bestanden)
        => throw new InvalidOperationException("Er is geen database.");
    public Task AfhandelenAsync(int inboxId, int status, int? factuurId, int? door) => Task.CompletedTask;
}

/// <summary>Alleen om lokaal te testen zonder de tabel FactuurInbox: FactuurInbox__Geheugen=true.</summary>
public class GeheugenFactuurInboxRepository : IFactuurInboxRepository
{
    private readonly ConcurrentDictionary<int, (InboxStuk Stuk, List<(string Naam, byte[] Inhoud)> Bestanden, int Status)> _rijen = new();
    private int _volgende;

    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);

    public Task<List<InboxStuk>> TeBeoordelenAsync(int max)
        => Task.FromResult(_rijen.Values.Where(r => r.Status == 0).Select(r => r.Stuk)
                                 .OrderBy(s => s.Ontvangen).Take(max).ToList());

    public Task<int> AantalTeBeoordelenAsync() => Task.FromResult(_rijen.Values.Count(r => r.Status == 0));

    public Task<List<(string Naam, byte[] Inhoud)>> BestandenAsync(int inboxId)
        => Task.FromResult(_rijen.TryGetValue(inboxId, out var r) ? r.Bestanden.ToList() : new List<(string, byte[])>());

    public Task<HashSet<string>> BekendeMailsAsync(IReadOnlyCollection<string> mailIds)
        => Task.FromResult(_rijen.Values.Select(r => r.Stuk.MailId).Where(mailIds.Contains).ToHashSet());

    public Task<int> VoegToeAsync(InboxStuk stuk, IReadOnlyList<(string Naam, byte[] Inhoud)> bestanden)
    {
        var id = Interlocked.Increment(ref _volgende);
        stuk.Id = id;
        stuk.Bestanden = bestanden.Select(b => b.Naam).ToList();
        if (stuk.WerkbonId is not null && stuk.Werkorder.Length == 0) stuk.Werkorder = "werkorder " + stuk.WerkbonId;
        _rijen[id] = (stuk, bestanden.ToList(), 0);
        return Task.FromResult(id);
    }

    public Task AfhandelenAsync(int inboxId, int status, int? factuurId, int? door)
    {
        if (_rijen.TryGetValue(inboxId, out var r)) _rijen[inboxId] = (r.Stuk, r.Bestanden, status);
        return Task.CompletedTask;
    }
}

public class MySqlFactuurInboxRepository : IFactuurInboxRepository
{
    private readonly string _cs;
    private bool _tabel;
    public MySqlFactuurInboxRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() " +
                "   AND TABLE_NAME IN ('FactuurInbox', 'FactuurInboxBestand')", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 2;
        }
        catch { }
        return _tabel;
    }

    public async Task<List<InboxStuk>> TeBeoordelenAsync(int max)
    {
        var uit = new List<InboxStuk>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT i.`FactuurInboxID`, i.`Bron`, i.`Ontvangen`, i.`DoorWerknemer`, COALESCE(n.`NaamVoluit`, ''), " +
            "       COALESCE(i.`MailId`, ''), COALESCE(i.`BijlageNaam`, ''), COALESCE(i.`MailVan`, ''), " +
            "       COALESCE(i.`MailOnderwerp`, ''), COALESCE(i.`MailTekst`, ''), COALESCE(i.`Soort`, ''), i.`WerkbonID`, " +
            "       CASE WHEN w.`Werkbon-id` IS NULL THEN '' ELSE CONCAT(COALESCE(p.`Projectnumber`, '?'), '-', " +
            "            RIGHT(CONCAT('00', COALESCE(w.`Werkbonnummer`, 0)), 2), ' · ', COALESCE(w.`Werkbonomschrijving`, '')) END, " +
            "       COALESCE(i.`Opmerking`, ''), COALESCE(i.`Melding`, ''), COALESCE(i.`Gelezen`, ''), " +
            "       (SELECT GROUP_CONCAT(b.`Naam` ORDER BY b.`FactuurInboxBestandID` SEPARATOR '|') " +
            "          FROM `FactuurInboxBestand` b WHERE b.`FactuurInboxID` = i.`FactuurInboxID`) " +
            "  FROM `FactuurInbox` i " +
            "  LEFT JOIN `Werknemers` n ON n.`Werknemer-id` = i.`DoorWerknemer` " +
            "  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = i.`WerkbonID` " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            " WHERE i.`Status` = 0 ORDER BY i.`Ontvangen`, i.`FactuurInboxID` LIMIT " + Math.Clamp(max, 1, 1000), cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            uit.Add(new InboxStuk
            {
                Id = r.GetInt32(0), Bron = r.GetString(1), Ontvangen = r.GetDateTime(2),
                DoorWerknemer = r.IsDBNull(3) ? null : r.GetInt32(3), DoorNaam = r.GetString(4),
                MailId = r.GetString(5), BijlageNaam = r.GetString(6), MailVan = r.GetString(7),
                MailOnderwerp = r.GetString(8), MailTekst = r.GetString(9), Soort = r.GetString(10),
                WerkbonId = r.IsDBNull(11) ? null : r.GetInt32(11), Werkorder = r.GetString(12),
                Opmerking = r.GetString(13), Melding = r.GetString(14), Gelezen = r.GetString(15),
                Bestanden = r.IsDBNull(16) ? new() : r.GetString(16).Split('|').ToList(),
            });
        return uit;
    }

    public async Task<int> AantalTeBeoordelenAsync()
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM `FactuurInbox` WHERE `Status` = 0", cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<List<(string Naam, byte[] Inhoud)>> BestandenAsync(int inboxId)
    {
        var uit = new List<(string, byte[])>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Naam`, `Inhoud` FROM `FactuurInboxBestand` WHERE `FactuurInboxID` = @id " +
            " ORDER BY `FactuurInboxBestandID`", cn);
        cmd.Parameters.AddWithValue("@id", inboxId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit.Add((r.GetString(0), (byte[])r.GetValue(1)));
        return uit;
    }

    public async Task<HashSet<string>> BekendeMailsAsync(IReadOnlyCollection<string> mailIds)
    {
        var uit = new HashSet<string>();
        if (mailIds.Count == 0) return uit;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("", cn);
        var namen = new List<string>();
        var i = 0;
        foreach (var id in mailIds)
        {
            namen.Add("@m" + i);
            cmd.Parameters.AddWithValue("@m" + i, id);
            i++;
        }
        cmd.CommandText = "SELECT DISTINCT `MailId` FROM `FactuurInbox` WHERE `MailId` IN (" + string.Join(",", namen) + ")";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit.Add(r.GetString(0));
        return uit;
    }

    public async Task<int> VoegToeAsync(InboxStuk stuk, IReadOnlyList<(string Naam, byte[] Inhoud)> bestanden)
    {
        static object Leeg(string s, int max) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : (s.Length > max ? s[..max] : s);

        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `FactuurInbox` (`Bron`, `Ontvangen`, `DoorWerknemer`, `MailId`, `BijlageNaam`, `MailVan`, " +
            "       `MailOnderwerp`, `MailTekst`, `Soort`, `WerkbonID`, `Opmerking`, `Melding`, `Gelezen`, `Status`) " +
            "VALUES (@bron, @ontv, @door, @mail, @bijl, @van, @ond, @tekst, @soort, @wb, @opm, @meld, @gel, 0); " +
            "SELECT LAST_INSERT_ID();", cn, tx);
        cmd.Parameters.AddWithValue("@bron", stuk.Bron);
        cmd.Parameters.AddWithValue("@ontv", stuk.Ontvangen);
        cmd.Parameters.AddWithValue("@door", (object?)stuk.DoorWerknemer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mail", Leeg(stuk.MailId, 64));
        // Uniek op (MailId, BijlageNaam). NULL telt in MariaDB nooit als gelijk, dus bij een mail zonder
        // bijlage een lege tekst in plaats van NULL; bij een bonnetje (geen MailId) wel NULL.
        object bijlage = stuk.MailId.Length == 0 ? DBNull.Value
                       : stuk.BijlageNaam.Length > 255 ? stuk.BijlageNaam[..255] : stuk.BijlageNaam;
        cmd.Parameters.AddWithValue("@bijl", bijlage);
        cmd.Parameters.AddWithValue("@van", Leeg(stuk.MailVan, 255));
        cmd.Parameters.AddWithValue("@ond", Leeg(stuk.MailOnderwerp, 500));
        cmd.Parameters.AddWithValue("@tekst", Leeg(stuk.MailTekst, 4000));
        cmd.Parameters.AddWithValue("@soort", Leeg(stuk.Soort, 40));
        cmd.Parameters.AddWithValue("@wb", (object?)stuk.WerkbonId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@opm", Leeg(stuk.Opmerking, 1000));
        cmd.Parameters.AddWithValue("@meld", Leeg(stuk.Melding, 500));
        cmd.Parameters.AddWithValue("@gel", Leeg(stuk.Gelezen, 1_000_000));
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        foreach (var (naam, inhoud) in bestanden)
        {
            await using var b = new MySqlCommand(
                "INSERT INTO `FactuurInboxBestand` (`FactuurInboxID`, `Naam`, `Inhoud`) VALUES (@id, @naam, @inhoud)", cn, tx);
            b.Parameters.AddWithValue("@id", id);
            b.Parameters.AddWithValue("@naam", naam.Length > 255 ? naam[..255] : naam);
            b.Parameters.AddWithValue("@inhoud", inhoud);
            await b.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        stuk.Id = id;
        return id;
    }

    public async Task AfhandelenAsync(int inboxId, int status, int? factuurId, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `FactuurInbox` SET `Status` = @st, `FactuurID` = @f, `AfgehandeldOp` = NOW(), `AfgehandeldDoor` = @door " +
            " WHERE `FactuurInboxID` = @id", cn);
        cmd.Parameters.AddWithValue("@st", status);
        cmd.Parameters.AddWithValue("@f", (object?)factuurId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", inboxId);
        await cmd.ExecuteNonQueryAsync();
    }
}
