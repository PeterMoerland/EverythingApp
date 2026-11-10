using System.Text.Json.Nodes;
using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een calculatie (tabel Calculaties, Calculaties_MariaDB.sql): de boom als JSON, per offerte één.</summary>
public record Calculatie(int Id, int OfferteId, int Status, int Versie, string Data, decimal Kostprijs, decimal Verkoop,
                         decimal Uren, DateTime? GewijzigdOp, string GewijzigdDoor, DateTime? BevrorenOp, string BevrorenDoor)
{
    public bool Bevroren => Status == CalculatieStatus.Bevroren;
}

/// <summary>Een calculatie in de lijst (met de offerte erbij).</summary>
public record CalculatieRegel(int OfferteId, int ProjectId, string OfferteNr, string Offerte, string Project, string Klant,
                              int OfferteStatus, int Status, decimal Verkoop, decimal Uren, DateTime? GewijzigdOp, string GewijzigdDoor);

/// <summary>Kostprijs per uur van een activiteit (UrenActiviteit), standaard voor nieuwe urenregels.</summary>
public record CalculatieTarief(int ActiviteitId, string Naam, decimal? Kostprijs);

public static class CalculatieStatus
{
    public const int Concept = 0, Bevroren = 1;
    public static string Naam(int s) => s == Bevroren ? "Bevroren" : "Concept";
}

/// <summary>Iemand anders heeft de calculatie intussen opgeslagen (versie klopt niet meer).</summary>
public class CalculatieConflictException(string bericht) : Exception(bericht);

public static class CalculatieData
{
    /// <summary>Een lege calculatie: opslagen zoals in FIT Clean Calculatie.xlsm (22 % overhead, 30 % marge).</summary>
    public const string Leeg = """{"inst":{"ohUur":22,"ohMat":22,"mUur":30,"mMat":30,"doel":null},"groepen":[],"regels":[]}""";

    /// <summary>Beginwaarden van de kostprijstarieven (blad Uurtarief), zoals in het script.</summary>
    public static readonly Dictionary<int, decimal> StandaardTarieven = new()
    {
        [1] = 63.33m, [2] = 46.39m, [3] = 85m, [5] = 43.46m, [6] = 43.46m, [7] = 43.46m, [8] = 56.09m,
        [9] = 63.33m, [10] = 65m, [11] = 63.33m, [12] = 63.33m, [13] = 65m, [14] = 63.33m,
    };

    /// <summary>Is dit een bruikbare boom (object met de lijsten groepen en regels)? Null = in orde, anders de reden.</summary>
    public static string? Controleer(string json)
    {
        if (json.Length > 8_000_000) return "De calculatie is te groot om op te slaan.";
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return "Geen geldige calculatie.";
            if (o["groepen"] is not JsonArray || o["regels"] is not JsonArray) return "Geen geldige calculatie (groepen en regels ontbreken).";
            return null;
        }
        catch (Exception ex) { return "Geen geldige calculatie: " + ex.Message; }
    }

    /// <summary>
    /// De inhoud zonder notities en zonder wat alleen de weergave is (in- en uitgeklapt). Na bevriezen mag alleen nog wat
    /// hier wegvalt veranderen.
    /// </summary>
    public static string Kern(string json)
    {
        var n = JsonNode.Parse(json);
        Strip(n);
        return n?.ToJsonString() ?? "";
    }

    private static readonly string[] NietInKern = { "notes", "open", "detailOpen", "bevroren" };

    private static void Strip(JsonNode? n)
    {
        if (n is JsonObject o)
        {
            foreach (var k in NietInKern) o.Remove(k);
            foreach (var kv in o.ToList()) Strip(kv.Value);
        }
        else if (n is JsonArray a)
            foreach (var x in a) Strip(x);
    }

    /// <summary>Kopie voor een andere offerte: niet bevroren, notities en uitsplitsingen gaan mee.</summary>
    public static string Kopie(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject o) return Leeg;
        o.Remove("bevroren");
        return o.ToJsonString();
    }
}

public interface ICalculatieRepository
{
    bool Beschikbaar { get; }
    /// <summary>Is Calculaties_MariaDB.sql gedraaid?</summary>
    Task<bool> TabelAanwezigAsync();
    /// <summary>De laatst gewijzigde calculaties.</summary>
    Task<List<CalculatieRegel>> RecentAsync(int max);
    /// <summary>De calculaties van deze offertes (offerte-id → regel); offertes zonder calculatie ontbreken.</summary>
    Task<Dictionary<int, CalculatieRegel>> VoorOffertesAsync(IReadOnlyCollection<int> offerteIds);
    /// <summary>De calculaties van de offertes van dit project (om er een kopie van te maken).</summary>
    Task<List<CalculatieRegel>> InProjectAsync(int projectId);
    Task<Calculatie?> GetAsync(int offerteId);
    /// <summary>Nieuwe calculatie bij een offerte (geeft het id); heeft de offerte er al een, dan een fout.</summary>
    Task<int> MaakAsync(int offerteId, string data, int? door);
    /// <summary>Opslaan als de versie nog klopt; geeft de nieuwe versie. Anders <see cref="CalculatieConflictException"/>.</summary>
    Task<int> BewaarAsync(int offerteId, int versie, string data, decimal kostprijs, decimal verkoop, decimal uren, int? door);
    /// <summary>Concept of bevroren zetten; geeft de nieuwe versie.</summary>
    Task<int> ZetStatusAsync(int offerteId, int status, int? door);
    /// <summary>Alle werkorder-activiteiten met hun kostprijstarief (null = nog geen tarief).</summary>
    Task<List<CalculatieTarief>> TarievenAsync();
    Task BewaarTariefAsync(int activiteitId, decimal? kostprijs, int? door);
}

public class GeenCalculatieRepository : ICalculatieRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<CalculatieRegel>> RecentAsync(int max) => Task.FromResult(new List<CalculatieRegel>());
    public Task<Dictionary<int, CalculatieRegel>> VoorOffertesAsync(IReadOnlyCollection<int> offerteIds) => Task.FromResult(new Dictionary<int, CalculatieRegel>());
    public Task<List<CalculatieRegel>> InProjectAsync(int projectId) => Task.FromResult(new List<CalculatieRegel>());
    public Task<Calculatie?> GetAsync(int offerteId) => Task.FromResult<Calculatie?>(null);
    public Task<int> MaakAsync(int offerteId, string data, int? door) => Task.FromResult(0);
    public Task<int> BewaarAsync(int offerteId, int versie, string data, decimal kostprijs, decimal verkoop, decimal uren, int? door) => Task.FromResult(versie);
    public Task<int> ZetStatusAsync(int offerteId, int status, int? door) => Task.FromResult(0);
    public Task<List<CalculatieTarief>> TarievenAsync() => Task.FromResult(new List<CalculatieTarief>());
    public Task BewaarTariefAsync(int activiteitId, decimal? kostprijs, int? door) => Task.CompletedTask;
}

public class MySqlCalculatieRepository : ICalculatieRepository
{
    private readonly string _cs;
    private bool _tabel;

    public MySqlCalculatieRepository(string cs) => _cs = cs;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static int? I(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static decimal M(MySqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToDecimal(r.GetValue(i));
    private static DateTime? D(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));

    private static async Task<bool> BestaatAsync(MySqlConnection cn, string tabel)
    {
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t", cn);
        cmd.Parameters.AddWithValue("@t", tabel);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;   // eenmaal gevonden blijft het zo
        await using var cn = await OpenAsync();
        return _tabel = await BestaatAsync(cn, "Calculaties");
    }

    private const string RegelSelect =
        "SELECT o.`OfferteID`, o.`ProjectID`, p.`Projectnumber`, o.`OfferteNummer`, COALESCE(o.`Omschrijving`, ''), " +
        "       CONCAT_WS(' · ', p.`Projectnumber`, p.`Projectdescription`), COALESCE(k.`name`, ''), COALESCE(o.`Status`, 1), " +
        "       c.`Status`, c.`Verkoop`, c.`Uren`, c.`GewijzigdOp`, COALESCE(w.`NaamVoluit`, '') " +
        "  FROM `Calculaties` c JOIN `Offertes` o ON o.`OfferteID` = c.`OfferteID` " +
        "  JOIN `Projects` p ON p.`Project-Id` = o.`ProjectID` " +
        "  LEFT JOIN `Relations` k ON k.`id_supplier` = p.`Customer-id` " +
        "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = COALESCE(c.`GewijzigdDoor`, c.`AangemaaktDoor`) " +
        " WHERE COALESCE(o.`Deleted`, 0) = 0 ";

    private static CalculatieRegel LeesRegel(MySqlDataReader r) => new(
        r.GetInt32(0), r.GetInt32(1), Offertes.Nummer(I(r, 2), Convert.ToInt32(r.GetValue(3))), S(r, 4), S(r, 5), S(r, 6),
        Convert.ToInt32(r.GetValue(7)), Convert.ToInt32(r.GetValue(8)), M(r, 9), M(r, 10), D(r, 11), S(r, 12));

    private async Task<List<CalculatieRegel>> RegelsAsync(string extra, Action<MySqlCommand> vul)
    {
        var lijst = new List<CalculatieRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(RegelSelect + extra, cn);
        vul(cmd);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(LeesRegel(r));
        return lijst;
    }

    public Task<List<CalculatieRegel>> RecentAsync(int max)
        => RegelsAsync($" ORDER BY COALESCE(c.`GewijzigdOp`, c.`AangemaaktOp`) DESC LIMIT {Math.Clamp(max, 1, 500)}", _ => { });

    public async Task<Dictionary<int, CalculatieRegel>> VoorOffertesAsync(IReadOnlyCollection<int> offerteIds)
    {
        if (offerteIds.Count == 0) return new();
        var lijst = await RegelsAsync($" AND c.`OfferteID` IN ({string.Join(",", offerteIds.Distinct().Select(i => i.ToString()))})", _ => { });
        return lijst.ToDictionary(x => x.OfferteId);
    }

    public Task<List<CalculatieRegel>> InProjectAsync(int projectId)
        => RegelsAsync(" AND o.`ProjectID` = @p ORDER BY o.`OfferteNummer` DESC", c => c.Parameters.AddWithValue("@p", projectId));

    public async Task<Calculatie?> GetAsync(int offerteId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT c.`CalculatieID`, c.`OfferteID`, c.`Status`, c.`Versie`, c.`Data`, c.`Kostprijs`, c.`Verkoop`, c.`Uren`, " +
            "       COALESCE(c.`GewijzigdOp`, c.`AangemaaktOp`), COALESCE(w.`NaamVoluit`, ''), c.`BevrorenOp`, COALESCE(b.`NaamVoluit`, '') " +
            "  FROM `Calculaties` c " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = COALESCE(c.`GewijzigdDoor`, c.`AangemaaktDoor`) " +
            "  LEFT JOIN `Werknemers` b ON b.`Werknemer-id` = c.`BevrorenDoor` " +
            " WHERE c.`OfferteID` = @o", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new Calculatie(r.GetInt32(0), r.GetInt32(1), Convert.ToInt32(r.GetValue(2)), Convert.ToInt32(r.GetValue(3)), S(r, 4),
            M(r, 5), M(r, 6), M(r, 7), D(r, 8), S(r, 9), D(r, 10), S(r, 11));
    }

    public async Task<int> MaakAsync(int offerteId, string data, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `Calculaties` (`OfferteID`, `Status`, `Versie`, `Data`, `AangemaaktOp`, `AangemaaktDoor`) " +
            "VALUES (@o, 0, 1, @d, NOW(), @door)", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        cmd.Parameters.AddWithValue("@d", data);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        try { await cmd.ExecuteNonQueryAsync(); }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new InvalidOperationException("Deze offerte heeft al een calculatie.");
        }
        return (int)cmd.LastInsertedId;
    }

    public async Task<int> BewaarAsync(int offerteId, int versie, string data, decimal kostprijs, decimal verkoop, decimal uren, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Calculaties` SET `Data` = @d, `Kostprijs` = @k, `Verkoop` = @v, `Uren` = @u, `Versie` = `Versie` + 1, " +
            "       `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door " +
            " WHERE `OfferteID` = @o AND `Versie` = @versie", cn);
        cmd.Parameters.AddWithValue("@d", data);
        cmd.Parameters.AddWithValue("@k", Math.Round(kostprijs, 2));
        cmd.Parameters.AddWithValue("@v", Math.Round(verkoop, 2));
        cmd.Parameters.AddWithValue("@u", Math.Round(uren, 2));
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o", offerteId);
        cmd.Parameters.AddWithValue("@versie", versie);
        if (await cmd.ExecuteNonQueryAsync() == 1) return versie + 1;
        throw new CalculatieConflictException(
            "Iemand anders heeft deze calculatie intussen opgeslagen. Vernieuw de pagina om zijn versie te zien; je laatste wijziging is niet opgeslagen.");
    }

    public async Task<int> ZetStatusAsync(int offerteId, int status, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Calculaties` SET `Status` = @s, `Versie` = `Versie` + 1, " +
            "       `BevrorenOp` = IF(@s = 1, NOW(), NULL), `BevrorenDoor` = IF(@s = 1, @door, NULL), " +
            "       `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door " +
            " WHERE `OfferteID` = @o", cn);
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o", offerteId);
        await cmd.ExecuteNonQueryAsync();
        await using var v = new MySqlCommand("SELECT `Versie` FROM `Calculaties` WHERE `OfferteID` = @o", cn);
        v.Parameters.AddWithValue("@o", offerteId);
        return Convert.ToInt32(await v.ExecuteScalarAsync());
    }

    public async Task<List<CalculatieTarief>> TarievenAsync()
    {
        var lijst = new List<CalculatieTarief>();
        await using var cn = await OpenAsync();
        var metTarief = await BestaatAsync(cn, "CalculatieTarieven");
        await using var cmd = new MySqlCommand(
            "SELECT a.`UrenActiviteitID`, COALESCE(a.`UrenActiviteit`, ''), " + (metTarief ? "t.`Kostprijs`" : "NULL") +
            "  FROM `UrenActiviteit` a " +
            (metTarief ? "  LEFT JOIN `CalculatieTarieven` t ON t.`UrenActiviteitID` = a.`UrenActiviteitID` " : "") +
            " WHERE COALESCE(a.`WerkorderActiviteit`, 0) <> 0 ORDER BY a.`UrenActiviteitID`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new CalculatieTarief(r.GetInt32(0), S(r, 1), r.IsDBNull(2) ? null : Convert.ToDecimal(r.GetValue(2))));
        return lijst;
    }

    public async Task BewaarTariefAsync(int activiteitId, decimal? kostprijs, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(kostprijs is null
            ? "DELETE FROM `CalculatieTarieven` WHERE `UrenActiviteitID` = @a"
            : "INSERT INTO `CalculatieTarieven` (`UrenActiviteitID`, `Kostprijs`, `GewijzigdOp`, `GewijzigdDoor`) VALUES (@a, @k, NOW(), @door) " +
              "ON DUPLICATE KEY UPDATE `Kostprijs` = @k, `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door", cn);
        cmd.Parameters.AddWithValue("@a", activiteitId);
        if (kostprijs is decimal k) cmd.Parameters.AddWithValue("@k", Math.Round(k, 2));
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Lokaal testen zonder in de database te schrijven (Calculaties__Geheugen=true): de offertes en activiteiten komen echt
/// uit de database, de calculaties en tarieven staan alleen in het geheugen (ook als het script nog niet gedraaid is).
/// </summary>
public class GeheugenCalculatieRepository : ICalculatieRepository
{
    private readonly ICalculatieRepository _echt;
    private readonly IOfferteRepository _offertes;
    private readonly Dictionary<int, Calculatie> _calc = new();
    private readonly Dictionary<int, decimal?> _tarief = new();
    private readonly object _slot = new();
    private int _volgende = 1;

    public GeheugenCalculatieRepository(ICalculatieRepository echt, IOfferteRepository offertes) { _echt = echt; _offertes = offertes; }
    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);

    private async Task<CalculatieRegel?> RegelAsync(Calculatie c)
    {
        var o = await _offertes.GetAsync(c.OfferteId);
        if (o is null) return null;
        return new CalculatieRegel(o.Id, o.ProjectId, o.OfferteNr, o.Omschrijving, $"{o.Projectnummer} · {o.Project}", o.Klant,
            o.Status, c.Status, c.Verkoop, c.Uren, c.GewijzigdOp, c.GewijzigdDoor);
    }

    private async Task<List<CalculatieRegel>> AlleAsync(Func<Calculatie, bool> filter)
    {
        List<Calculatie> lijst;
        lock (_slot) lijst = _calc.Values.Where(filter).OrderByDescending(c => c.GewijzigdOp).ToList();
        var uit = new List<CalculatieRegel>();
        foreach (var c in lijst) if (await RegelAsync(c) is { } r) uit.Add(r);
        return uit;
    }

    public Task<List<CalculatieRegel>> RecentAsync(int max) => AlleAsync(_ => true);
    public async Task<Dictionary<int, CalculatieRegel>> VoorOffertesAsync(IReadOnlyCollection<int> offerteIds)
        => (await AlleAsync(c => offerteIds.Contains(c.OfferteId))).ToDictionary(r => r.OfferteId);
    public async Task<List<CalculatieRegel>> InProjectAsync(int projectId)
        => (await AlleAsync(_ => true)).Where(r => r.ProjectId == projectId).ToList();

    public Task<Calculatie?> GetAsync(int offerteId)
    {
        lock (_slot) return Task.FromResult(_calc.TryGetValue(offerteId, out var c) ? c : null);
    }

    public Task<int> MaakAsync(int offerteId, string data, int? door)
    {
        lock (_slot)
        {
            if (_calc.ContainsKey(offerteId)) throw new InvalidOperationException("Deze offerte heeft al een calculatie.");
            var id = _volgende++;
            _calc[offerteId] = new Calculatie(id, offerteId, 0, 1, data, 0, 0, 0, DateTime.Now, "geheugen", null, "");
            return Task.FromResult(id);
        }
    }

    public Task<int> BewaarAsync(int offerteId, int versie, string data, decimal kostprijs, decimal verkoop, decimal uren, int? door)
    {
        lock (_slot)
        {
            if (!_calc.TryGetValue(offerteId, out var c) || c.Versie != versie)
                throw new CalculatieConflictException("Iemand anders heeft deze calculatie intussen opgeslagen (geheugen).");
            _calc[offerteId] = c with { Data = data, Kostprijs = kostprijs, Verkoop = verkoop, Uren = uren, Versie = versie + 1, GewijzigdOp = DateTime.Now };
            return Task.FromResult(versie + 1);
        }
    }

    public Task<int> ZetStatusAsync(int offerteId, int status, int? door)
    {
        lock (_slot)
        {
            var c = _calc[offerteId];
            _calc[offerteId] = c with { Status = status, Versie = c.Versie + 1, BevrorenOp = status == 1 ? DateTime.Now : null };
            return Task.FromResult(c.Versie + 1);
        }
    }

    public async Task<List<CalculatieTarief>> TarievenAsync()
    {
        var echt = await _echt.TarievenAsync();
        lock (_slot)
            return echt.Select(t => t with
            {
                Kostprijs = _tarief.TryGetValue(t.ActiviteitId, out var k) ? k
                          : t.Kostprijs ?? (CalculatieData.StandaardTarieven.TryGetValue(t.ActiviteitId, out var s) ? s : null),
            }).ToList();
    }

    public Task BewaarTariefAsync(int activiteitId, decimal? kostprijs, int? door)
    {
        lock (_slot) _tarief[activiteitId] = kostprijs;
        return Task.CompletedTask;
    }
}
