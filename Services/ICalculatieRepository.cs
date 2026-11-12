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

/// <summary>Een deel van een werkorder uit het plan (een urenregel, samengevoegd op naam).</summary>
public record WerkorderPlanDeel(string Naam, decimal Uren, decimal Kosten, int? ActiviteitId);

/// <summary>
/// Een werkorder uit het plan van de boom (werkorderPlan in calculatieboom.html): de groep, de groep van de werkorder
/// erboven (subwerkorder), naam, budget (verkoop incl. aandeel korting), begrote kosten en uren, en de delen.
/// </summary>
public record WerkorderPlanRij(int Groep, int? Ouder, string Naam, decimal Budget, decimal KostUur, decimal KostMat, decimal Uren,
                               List<WerkorderPlanDeel> Delen);

/// <summary>Een werkorder die een calculatie heeft gemaakt (CalculatieWerkorders, CalculatieWerkorders_MariaDB.sql).</summary>
public record CalculatieWerkorder(int GroepId, int WerkbonId, string Nummer, string Omschrijving, decimal Budget,
                                  DateTime? AangemaaktOp, string AangemaaktDoor);

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

    /// <summary>
    /// Een calculatie die offline in de calculatieboom is gemaakt en als JSON is geëxporteerd (wens gebruiker 12-11-2026,
    /// bestand van Edwin): <c>{"soort":"mde-calculatieboom","versie":1,"calculatie":{meta, inst, groepen, regels}}</c>. De
    /// app bewaart alleen inst, groepen en regels; een bestand met alleen die drie mag ook. Geeft de boom zoals de app hem
    /// opslaat en de naam uit het bestand, of de reden waarom het niet kan.
    /// </summary>
    public static (string? Data, string Naam, string? Fout) UitExport(string json)
    {
        if (json.Length > 8_000_000) return (null, "", "Het bestand is te groot.");
        JsonObject? bron;
        try { bron = JsonNode.Parse(json, documentOptions: new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip }) as JsonObject; }
        catch (Exception ex) { return (null, "", "Dit is geen geldig JSON-bestand: " + ex.Message); }
        if (bron is null) return (null, "", "Dit is geen calculatie uit de calculatieboom.");

        var soort = bron["soort"] is JsonValue sv && sv.TryGetValue<string>(out var st) ? st : "";
        if (soort.Length > 0 && soort != "mde-calculatieboom") return (null, "", $"Dit bestand is van de soort '{soort}', geen calculatieboom.");
        var calc = bron["calculatie"] as JsonObject ?? bron;
        if (calc["groepen"] is not JsonArray groepen || calc["regels"] is not JsonArray regels)
            return (null, "", "Dit is geen calculatie uit de calculatieboom (groepen en regels ontbreken).");

        // de opbouw moet kloppen: elke groep een eigen nummer, elke regel in een bestaande groep
        var ids = new HashSet<long>();
        foreach (var g in groepen)
        {
            if (g is not JsonObject go || !Heel(go["id"], out var id)) return (null, "", "Een groep in het bestand heeft geen nummer (id).");
            if (!ids.Add(id)) return (null, "", $"Groep {id} staat twee keer in het bestand.");
        }
        var regelIds = new HashSet<long>();
        foreach (var r in regels)
        {
            if (r is not JsonObject ro || !Heel(ro["id"], out var id)) return (null, "", "Een regel in het bestand heeft geen nummer (id).");
            if (!regelIds.Add(id) || ids.Contains(id)) return (null, "", $"Nummer {id} komt twee keer voor in het bestand.");
            if (!Heel(ro["groep"], out var groep) || !ids.Contains(groep)) return (null, "", $"Regel {id} hoort bij een groep die niet in het bestand staat.");
        }

        var uit = new JsonObject
        {
            ["inst"] = (calc["inst"] as JsonObject)?.DeepClone() ?? JsonNode.Parse(Leeg)!["inst"]!.DeepClone(),
            ["groepen"] = groepen.DeepClone(),
            ["regels"] = regels.DeepClone(),
        };
        var naam = calc["meta"]?["naam"] is JsonValue nv && nv.TryGetValue<string>(out var n) ? n.Trim() : "";
        return (uit.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), naam, null);
    }

    private static bool Heel(JsonNode? n, out long waarde)
    {
        waarde = 0;
        try { if (n is JsonValue v) { waarde = (long)v.GetValue<decimal>(); return true; } } catch { }
        return false;
    }

    /// <summary>
    /// Kostprijs, verkoop en uren van een boom, zoals de boompagina ze uitrekent (bereken() in calculatieboom.html): per
    /// regel aantal × kostprijs × het aantal van de groep en de groepen erboven (of het eigen "keer"); een uitsplitsing
    /// levert bij uren het aantal en bij materiaal de kostprijs; verkoop = (kost + overhead) × (1 + marge), of met een
    /// eigen percentage kost × (1 + eigen). Voor de lijst, bij importeren (de boom zelf rekent pas bij opslaan).
    /// </summary>
    public static (decimal Kost, decimal Verkoop, decimal Uren) Totalen(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o || o["groepen"] is not JsonArray groepen || o["regels"] is not JsonArray regels) return (0, 0, 0);
            static decimal? G(JsonNode? n) { try { return n is JsonValue v ? v.GetValue<decimal>() : null; } catch { return null; } }
            var inst = o["inst"] as JsonObject;
            decimal Inst(string k, decimal standaard) => G(inst?[k]) ?? standaard;
            decimal ohUur = Inst("ohUur", 22), ohMat = Inst("ohMat", 22), mUur = Inst("mUur", 30), mMat = Inst("mMat", 30);

            var groep = groepen.OfType<JsonObject>().Where(g => G(g["id"]) is not null).ToDictionary(g => (long)G(g["id"])!.Value);
            decimal Factor(long id)
            {
                var f = 1m;
                for (var n = 0; n < 20 && groep.TryGetValue(id, out var g); n++)
                {
                    f *= G(g["aantal"]) ?? 1;
                    if (G(g["ouder"]) is not decimal ouder) break;
                    id = (long)ouder;
                }
                return f;
            }

            decimal kost = 0, verkoop = 0, uren = 0;
            foreach (var r in regels.OfType<JsonObject>())
            {
                if (G(r["groep"]) is not decimal gid || !groep.ContainsKey((long)gid)) continue;
                var uur = r["soort"] is JsonValue sv && sv.TryGetValue<string>(out var soort) && soort == "uur";
                decimal? detail = r["detail"] is JsonArray d ? d.OfType<JsonObject>().Sum(x => (G(x["aantal"]) ?? 0) * (G(x["waarde"]) ?? 0)) : null;
                var aantal = (detail is not null && uur ? detail : G(r["aantal"])) ?? 0;
                var prijs = (detail is not null && !uur ? detail : G(r["kost"])) ?? 0;
                var keer = G(r["keer"]) ?? Factor((long)gid);
                var k = aantal * prijs * keer;
                var v = G(r["eigen"]) is decimal eigen
                    ? k * (1 + eigen / 100)
                    : (k + k * (uur ? ohUur : ohMat) / 100) * (1 + (uur ? mUur : mMat) / 100);
                kost += k; verkoop += v;
                if (uur) uren += aantal * keer;
            }
            return (kost, verkoop, uren);
        }
        catch { return (0, 0, 0); }
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
    /// <summary>Is CalculatieWerkorders_MariaDB.sql gedraaid?</summary>
    Task<bool> WerkorderTabelAanwezigAsync();
    /// <summary>De werkorders die deze calculatie heeft gemaakt (leeg = nog niet).</summary>
    Task<List<CalculatieWerkorder>> WerkordersAsync(int offerteId);
    /// <summary>Het nummer dat de volgende werkorder in dit project krijgt (Werkbonnummer).</summary>
    Task<int> VolgendWerkbonnummerAsync(int projectId);
    /// <summary>
    /// Maakt in één transactie de werkorders uit het plan (met budget en subwerkorders), de delen, de kostenbegroting
    /// van het project (uren en materiaal per werkorder) en de koppeling met de calculatie. Alleen als de calculatie
    /// bevroren is, de versie klopt en er nog geen werkorders zijn; anders <see cref="InvalidOperationException"/>.
    /// </summary>
    Task<List<CalculatieWerkorder>> MaakWerkordersAsync(int offerteId, int versie, IReadOnlyList<WerkorderPlanRij> plan, int? door);
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
    public Task<bool> WerkorderTabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<CalculatieWerkorder>> WerkordersAsync(int offerteId) => Task.FromResult(new List<CalculatieWerkorder>());
    public Task<int> VolgendWerkbonnummerAsync(int projectId) => Task.FromResult(1);
    public Task<List<CalculatieWerkorder>> MaakWerkordersAsync(int offerteId, int versie, IReadOnlyList<WerkorderPlanRij> plan, int? door)
        => Task.FromResult(new List<CalculatieWerkorder>());
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

    private bool _woTabel;

    public async Task<bool> WerkorderTabelAanwezigAsync()
    {
        if (_woTabel) return true;
        await using var cn = await OpenAsync();
        return _woTabel = await BestaatAsync(cn, "CalculatieWerkorders") && await BestaatAsync(cn, "WerkorderDelen");
    }

    public async Task<List<CalculatieWerkorder>> WerkordersAsync(int offerteId)
    {
        var lijst = new List<CalculatieWerkorder>();
        if (!await WerkorderTabelAanwezigAsync()) return lijst;
        await using var cn = await OpenAsync();
        await LeesWerkordersAsync(cn, null, offerteId, lijst);
        return lijst;
    }

    private static async Task LeesWerkordersAsync(MySqlConnection cn, MySqlTransaction? tx, int offerteId, List<CalculatieWerkorder> lijst)
    {
        await using var cmd = new MySqlCommand(
            "SELECT cw.`GroepID`, cw.`WerkbonID`, p.`Projectnumber`, w.`Werkbonnummer`, COALESCE(w.`Werkbonomschrijving`, ''), " +
            "       cw.`Budget`, cw.`AangemaaktOp`, COALESCE(wn.`NaamVoluit`, '') " +
            "  FROM `CalculatieWerkorders` cw JOIN `Calculaties` c ON c.`CalculatieID` = cw.`CalculatieID` " +
            "  JOIN `Workorders` w ON w.`Werkbon-id` = cw.`WerkbonID` " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            "  LEFT JOIN `Werknemers` wn ON wn.`Werknemer-id` = cw.`AangemaaktDoor` " +
            " WHERE c.`OfferteID` = @o ORDER BY cw.`ID`", cn, tx);
        cmd.Parameters.AddWithValue("@o", offerteId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new CalculatieWerkorder(r.GetInt32(0), r.GetInt32(1), WerkbonNummer(I(r, 2), I(r, 3)), S(r, 4), M(r, 5), D(r, 6), S(r, 7)));
    }

    /// <summary>Zoals de app een werkorder toont: projectnummer-volgnummer (224112-05).</summary>
    public static string WerkbonNummer(int? project, int? nummer) => $"{project}-{(nummer ?? 0):00}";

    public async Task<int> VolgendWerkbonnummerAsync(int projectId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT MAX(`Werkbonnummer`) FROM `Workorders` WHERE `Project-id` = @p", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        var res = await cmd.ExecuteScalarAsync();
        return res is null or DBNull ? 1 : Convert.ToInt32(res) + 1;
    }

    public async Task<List<CalculatieWerkorder>> MaakWerkordersAsync(int offerteId, int versie, IReadOnlyList<WerkorderPlanRij> plan, int? door)
    {
        if (!await WerkorderTabelAanwezigAsync())
            throw new InvalidOperationException("Draai eerst CalculatieWerkorders_MariaDB.sql op de database.");
        await using var cn = await OpenAsync();
        var metBegroting = await BestaatAsync(cn, "ProjectKostenbegroting");
        await using var tx = await cn.BeginTransactionAsync();

        // de calculatie vastzetten (twee keer tegelijk op de knop drukken maakt geen dubbele werkorders)
        int calcId, status, huidig, projectId; int? projectnr; string offerteNr;
        await using (var c = new MySqlCommand(
            "SELECT c.`CalculatieID`, c.`Status`, c.`Versie`, o.`ProjectID`, p.`Projectnumber`, o.`OfferteNummer` " +
            "  FROM `Calculaties` c JOIN `Offertes` o ON o.`OfferteID` = c.`OfferteID` " +
            "  JOIN `Projects` p ON p.`Project-Id` = o.`ProjectID` WHERE c.`OfferteID` = @o FOR UPDATE", cn, tx))
        {
            c.Parameters.AddWithValue("@o", offerteId);
            await using var r = await c.ExecuteReaderAsync();
            if (!await r.ReadAsync()) throw new InvalidOperationException("Deze calculatie bestaat niet (meer).");
            calcId = r.GetInt32(0); status = Convert.ToInt32(r.GetValue(1)); huidig = Convert.ToInt32(r.GetValue(2));
            projectId = r.GetInt32(3); projectnr = I(r, 4); offerteNr = Offertes.Nummer(projectnr, Convert.ToInt32(r.GetValue(5)));
        }
        if (status != CalculatieStatus.Bevroren) throw new InvalidOperationException("Werkorders aanmaken kan pas als de calculatie bevroren is (klant akkoord).");
        if (huidig != versie) throw new InvalidOperationException("De calculatie is intussen gewijzigd; vernieuw de pagina en probeer opnieuw.");
        await using (var c = new MySqlCommand("SELECT COUNT(*) FROM `CalculatieWerkorders` WHERE `CalculatieID` = @c", cn, tx))
        {
            c.Parameters.AddWithValue("@c", calcId);
            if (Convert.ToInt32(await c.ExecuteScalarAsync()) > 0) throw new InvalidOperationException("Deze calculatie heeft de werkorders al gemaakt.");
        }

        // volgend nummer in het project (de werkorders van het project even vast, tegen dubbele nummers)
        int nr;
        await using (var c = new MySqlCommand("SELECT MAX(`Werkbonnummer`) FROM `Workorders` WHERE `Project-id` = @p FOR UPDATE", cn, tx))
        {
            c.Parameters.AddWithValue("@p", projectId);
            var res = await c.ExecuteScalarAsync();
            nr = res is null or DBNull ? 0 : Convert.ToInt32(res);
        }

        var perGroep = new Dictionary<int, int>();
        foreach (var rij in plan)
        {
            nr++;
            int? ouder = rij.Ouder is int og && perGroep.TryGetValue(og, out var ow) ? ow : null;
            int werkbon;
            await using (var w = new MySqlCommand(
                "INSERT INTO `Workorders` (`Project-id`, `Werkbonnummer`, `Werkbonomschrijving`, `WorkOrderStatus`, `Aangemaakt`, " +
                "       `Aangemaakt_door`, `Doorbelast deel`, `InkoopToegestaan`, `Budget`, `ParentWerkbonID`) " +
                "VALUES (@p, @nr, @oms, @st, NOW(), @door, 100, 1, @b, @ouder)", cn, tx))
            {
                w.Parameters.AddWithValue("@p", projectId);
                w.Parameters.AddWithValue("@nr", nr);
                w.Parameters.AddWithValue("@oms", Kort(rij.Naam, 100));
                w.Parameters.AddWithValue("@st", ProjectVelden.WerkorderStatussen[0].Id);   // Open
                w.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
                w.Parameters.AddWithValue("@b", (double)Math.Round(rij.Budget, 2));
                w.Parameters.AddWithValue("@ouder", (object?)ouder ?? DBNull.Value);
                await w.ExecuteNonQueryAsync();
                werkbon = (int)w.LastInsertedId;
            }
            perGroep[rij.Groep] = werkbon;

            await using (var k = new MySqlCommand(
                "INSERT INTO `CalculatieWerkorders` (`CalculatieID`, `GroepID`, `WerkbonID`, `Budget`, `KostenUren`, `KostenMateriaal`, " +
                "       `Uren`, `AangemaaktOp`, `AangemaaktDoor`) VALUES (@c, @g, @w, @b, @ku, @km, @u, NOW(), @door)", cn, tx))
            {
                k.Parameters.AddWithValue("@c", calcId);
                k.Parameters.AddWithValue("@g", rij.Groep);
                k.Parameters.AddWithValue("@w", werkbon);
                k.Parameters.AddWithValue("@b", Math.Round(rij.Budget, 2));
                k.Parameters.AddWithValue("@ku", Math.Round(rij.KostUur, 2));
                k.Parameters.AddWithValue("@km", Math.Round(rij.KostMat, 2));
                k.Parameters.AddWithValue("@u", Math.Round(rij.Uren, 2));
                k.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
                await k.ExecuteNonQueryAsync();
            }

            var volg = 0;
            foreach (var deel in rij.Delen)
            {
                await using var d = new MySqlCommand(
                    "INSERT INTO `WerkorderDelen` (`WerkbonID`, `Naam`, `BegroteUren`, `BegroteKosten`, `UrenActiviteitID`, `Volgorde`, " +
                    "       `CalculatieID`, `Actief`, `AangemaaktOp`, `AangemaaktDoor`) VALUES (@w, @n, @u, @k, @a, @v, @c, 1, NOW(), @door)", cn, tx);
                d.Parameters.AddWithValue("@w", werkbon);
                d.Parameters.AddWithValue("@n", Kort(deel.Naam, 200));
                d.Parameters.AddWithValue("@u", Math.Round(deel.Uren, 2));
                d.Parameters.AddWithValue("@k", Math.Round(deel.Kosten, 2));
                d.Parameters.AddWithValue("@a", (object?)deel.ActiviteitId ?? DBNull.Value);
                d.Parameters.AddWithValue("@v", ++volg);
                d.Parameters.AddWithValue("@c", calcId);
                d.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
                await d.ExecuteNonQueryAsync();
            }

            if (metBegroting)
                foreach (var (soort, bedrag) in new[] { ("uren", rij.KostUur), ("materiaal", rij.KostMat) })
                {
                    if (Math.Round(bedrag, 2) == 0) continue;
                    await using var b = new MySqlCommand(
                        "INSERT INTO `ProjectKostenbegroting` (`ProjectID`, `Soort`, `Omschrijving`, `Bedrag`, `AangemaaktOp`, `AangemaaktDoor`) " +
                        "VALUES (@p, @s, @o, @b, NOW(), @door)", cn, tx);
                    b.Parameters.AddWithValue("@p", projectId);
                    b.Parameters.AddWithValue("@s", soort);
                    b.Parameters.AddWithValue("@o", Kort($"{WerkbonNummer(projectnr, nr)} {rij.Naam} (calculatie {offerteNr})", 150));
                    b.Parameters.AddWithValue("@b", Math.Round(bedrag, 2));
                    b.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
                    await b.ExecuteNonQueryAsync();
                }
        }

        var lijst = new List<CalculatieWerkorder>();
        await LeesWerkordersAsync(cn, tx, offerteId, lijst);
        await tx.CommitAsync();
        return lijst;
    }

    private static string Kort(string t, int max) { t = (t ?? "").Trim(); return t.Length <= max ? t : t[..max]; }

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

    private readonly Dictionary<int, List<CalculatieWerkorder>> _wo = new();
    private int _volgendWerkbon = 9_000_000;

    public Task<bool> WerkorderTabelAanwezigAsync() => Task.FromResult(true);

    public Task<List<CalculatieWerkorder>> WerkordersAsync(int offerteId)
    {
        lock (_slot) return Task.FromResult(_wo.TryGetValue(offerteId, out var l) ? l.ToList() : new List<CalculatieWerkorder>());
    }

    public Task<int> VolgendWerkbonnummerAsync(int projectId) => _echt.VolgendWerkbonnummerAsync(projectId);

    /// <summary>Maakt niets in de database: alleen nummers en ids in het geheugen (om het scherm te kunnen testen).</summary>
    public async Task<List<CalculatieWerkorder>> MaakWerkordersAsync(int offerteId, int versie, IReadOnlyList<WerkorderPlanRij> plan, int? door)
    {
        var o = await _offertes.GetAsync(offerteId) ?? throw new InvalidOperationException("Offerte niet gevonden.");
        var nr = await _echt.VolgendWerkbonnummerAsync(o.ProjectId) - 1;
        lock (_slot)
        {
            var c = _calc[offerteId];
            if (c.Status != CalculatieStatus.Bevroren) throw new InvalidOperationException("Werkorders aanmaken kan pas als de calculatie bevroren is (klant akkoord).");
            if (c.Versie != versie) throw new InvalidOperationException("De calculatie is intussen gewijzigd; vernieuw de pagina en probeer opnieuw.");
            if (_wo.ContainsKey(offerteId)) throw new InvalidOperationException("Deze calculatie heeft de werkorders al gemaakt.");
            var lijst = plan.Select(r => new CalculatieWerkorder(r.Groep, _volgendWerkbon++, MySqlCalculatieRepository.WerkbonNummer(o.Projectnummer, ++nr),
                r.Naam, Math.Round(r.Budget, 2), DateTime.Now, "geheugen")).ToList();
            _wo[offerteId] = lijst;
            return lijst;
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
