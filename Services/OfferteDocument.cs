using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EverythingApp.Models;
using MySqlConnector;
using static System.Net.WebUtility;

namespace EverythingApp.Services;

// Offertedocument (wens gebruiker 20-11-2026): "een offerte maken op basis van een calculatie, in een Word-achtig document
// waar (delen van) de calculatie als blok in staan en gekoppeld blijven". Stap 1: een document uit blokken per offerte
// (OfferteDocumenten_MariaDB.sql), gemaakt uit een vaste opzet; tekst, koppen en plaatjes zijn vrij, de prijstabel, de
// voorwaarden en de ondertekening komen uit de calculatie en de offerte. Een prijstabel bewaart alleen WELK deel van de
// calculatie hij toont en HOE; de cijfers worden bij tonen en bij de PDF opnieuw uitgerekend. Bij vastleggen (versturen)
// gaat de opmaak met de cijfers van dat moment in Vast en verandert het document niet meer mee.
//
// Wat de klant ziet: alleen verkoopbedragen (en desgewenst uren en aantallen). Kostprijs, overhead en marge komen in dit
// bestand nergens in de opmaak voor; een blok kan er dus ook niet om vragen.

/// <summary>Het document van een offerte (tabel OfferteDocumenten): de blokken als JSON, en na vastleggen de opmaak.</summary>
public record OfferteDocument(int Id, int OfferteId, int Status, int Versie, string Data, string Vast,
                              DateTime? GewijzigdOp, string GewijzigdDoor, DateTime? VastOp, string VastDoor)
{
    public bool Vastgelegd => Status == 1;
}

/// <summary>
/// Een sjabloon (tabel OfferteSjablonen): een document om een volgende offerte mee te beginnen. BvId null = voor alle
/// BV's. In een lijst is Data leeg.
/// </summary>
public record OfferteSjabloon(int Id, string Naam, int? BvId, string Data, DateTime? Op, string Door);

/// <summary>Iemand anders heeft het document intussen opgeslagen (versie klopt niet meer).</summary>
public class OfferteDocumentConflictException(string bericht) : Exception(bericht);

public static class OfferteDocumentData
{
    /// <summary>Zoveel tekens mag het document zijn (plaatjes staan erin als data-URI).</summary>
    public const int MaxTekens = 6_000_000;

    public static readonly string[] Soorten = { "tekst", "kop", "prijs", "plaatje", "tabel", "voorwaarden", "ondertekening", "pagina" };

    /// <summary>Een blok dat niet de hele breedte neemt (stap 2): blokken die samen een regel vullen staan naast elkaar.</summary>
    public static readonly string[] Delen = { "half", "derde", "tweederde" };

    /// <summary>Welk deel van de breedte een blok neemt.</summary>
    public static decimal Breedte(string deel) => deel switch { "half" => 0.5m, "derde" => 1m / 3, "tweederde" => 2m / 3, _ => 1 };

    /// <summary>Velden die in een tekst of kop worden ingevuld (bij tonen en in de PDF).</summary>
    public static readonly (string Veld, string Uitleg)[] Velden =
    {
        ("{klant}", "naam van de klant"), ("{contactpersoon}", "contactpersoon van de offerte"),
        ("{offertenummer}", "bijvoorbeeld Q224112-01"), ("{omschrijving}", "omschrijving van de offerte"),
        ("{project}", "projectnummer en -naam"), ("{datum}", "datum van de offerte"),
        ("{totaal}", "totaal van de calculatie, excl. btw"),
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private static string Tekst(JsonObject o, string veld)
    {
        try { return o[veld] is JsonValue v && v.TryGetValue<string>(out var s) ? s : ""; } catch { return ""; }
    }
    private static long? Heel(JsonNode? n)
    {
        try { return n is JsonValue v ? (long)v.GetValue<decimal>() : null; } catch { return null; }
    }
    private static bool Waar(JsonNode? n)
    {
        try { return n is JsonValue v && v.TryGetValue<bool>(out var b) && b; } catch { return false; }
    }
    private static string Kort(string t, int max) { t = t.Replace("\r", "").Replace("\n", " ").Trim(); return t.Length <= max ? t : t[..max]; }

    /// <summary>
    /// Het document zoals de app het bewaart: alleen bekende blokken en velden, tekst zonder opmaak die niet mag, een
    /// plaatje alleen als ingesloten png/jpeg/gif/webp. Wat de browser stuurt gaat hier altijd doorheen (ook omdat een
    /// ander het document daarna in zijn browser opent).
    /// </summary>
    public static (string? Data, string? Fout) Schoon(JsonNode? bron)
    {
        if (bron is not JsonObject o || o["blokken"] is not JsonArray lijst) return (null, "Geen geldig document (de blokken ontbreken).");
        if (lijst.Count > 200) return (null, "Het document heeft te veel blokken (meer dan 200).");
        var uit = new JsonArray();
        var ids = new HashSet<string>();
        foreach (var n in lijst)
        {
            if (n is not JsonObject b) continue;
            var soort = Tekst(b, "soort");
            if (!Soorten.Contains(soort)) continue;
            var id = Regex.Replace(Tekst(b, "id"), "[^A-Za-z0-9]", "");
            if (id.Length is 0 or > 24 || !ids.Add(id))
                do id = "b" + Guid.NewGuid().ToString("N")[..10]; while (!ids.Add(id));
            var s = new JsonObject { ["id"] = id, ["soort"] = soort };
            if (soort != "pagina" && Delen.Contains(Tekst(b, "deel"))) s["deel"] = Tekst(b, "deel");
            switch (soort)
            {
                case "tabel":
                    // een eigen tabel: platte tekst per cel, hooguit 8 kolommen en 100 rijen, elke rij even lang
                    var bron2 = (b["rijen"] as JsonArray)?.OfType<JsonArray>().Take(100).ToList() ?? new();
                    var kolommen = Math.Clamp(bron2.Count == 0 ? 2 : bron2.Max(x => x.Count), 1, 8);
                    var rijen = new JsonArray();
                    foreach (var rij in bron2)
                    {
                        var cellen = new JsonArray();
                        for (var k = 0; k < kolommen; k++)
                        {
                            string cel;
                            try { cel = k < rij.Count && rij[k] is JsonValue cv && cv.TryGetValue<string>(out var ct) ? ct : ""; } catch { cel = ""; }
                            cellen.Add((JsonNode?)JsonValue.Create(Kort(cel, 500)));   // niet Add<T>: die maakt een waarde die de opties hieronder niet kunnen schrijven
                        }
                        rijen.Add(cellen);
                    }
                    if (rijen.Count == 0) rijen.Add(new JsonArray(Enumerable.Range(0, kolommen).Select(_ => (JsonNode?)JsonValue.Create("")).ToArray()));
                    s["rijen"] = rijen;
                    s["kop"] = Waar(b["kop"]);
                    s["rechts"] = new JsonArray(((b["rechts"] as JsonArray)?.Select(Heel).OfType<long>().Where(x => x >= 0 && x < kolommen).Distinct()
                                                 ?? Enumerable.Empty<long>()).Select(x => (JsonNode?)JsonValue.Create((int)x)).ToArray());
                    break;
                case "tekst": s["html"] = SchoonHtml(Tekst(b, "html")); break;
                case "kop": s["tekst"] = Kort(Tekst(b, "tekst"), 300); break;
                case "prijs":
                    if (Heel(b["groep"]) is long g) s["groep"] = g;
                    s["diepte"] = (int)Math.Clamp(Heel(b["diepte"]) ?? 1, 0, 9);
                    var r = Tekst(b, "regels");
                    s["regels"] = r is "omschrijving" or "prijs" ? r : "geen";
                    s["aantallen"] = Waar(b["aantallen"]);
                    s["uren"] = Waar(b["uren"]);
                    break;
                case "plaatje":
                    var src = Tekst(b, "src");
                    if (src.Length > 4_000_000 || !Regex.IsMatch(src, "^data:image/(png|jpeg|gif|webp);base64,[A-Za-z0-9+/=]+$")) continue;
                    s["src"] = src;
                    s["breedte"] = (int)Math.Clamp(Heel(b["breedte"]) ?? 60, 10, 100);
                    s["onderschrift"] = Kort(Tekst(b, "onderschrift"), 300);
                    break;
            }
            uit.Add(s);
        }
        var json = new JsonObject { ["blokken"] = uit }.ToJsonString(Json);
        return json.Length > MaxTekens ? (null, "Het document is te groot; gebruik minder of kleinere plaatjes.") : (json, null);
    }

    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex Toegestaan = new(@"^<\s*(/?)\s*(b|strong|i|em|u|br|p|div|ul|ol|li)\b[^>]*>$",
                                                   RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Weg = new(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// Tekst met alleen vet, cursief, onderstreept, alinea's en lijsten: elke toegestane tag komt kaal terug (zonder
    /// attributen), elke andere tag vervalt, en een los &lt; of &gt; wordt tekst. Er kan dus geen script of stijl in.
    /// </summary>
    public static string SchoonHtml(string html)
    {
        if (html.Length > 200_000) html = html[..200_000];
        html = Weg.Replace(html, "");
        var sb = new StringBuilder();
        var pos = 0;
        foreach (Match m in Tag.Matches(html))
        {
            sb.Append(Los(html[pos..m.Index]));
            var t = Toegestaan.Match(m.Value);
            if (t.Success)
            {
                var naam = t.Groups[2].Value.ToLowerInvariant();
                sb.Append(naam == "br" ? "<br>" : t.Groups[1].Value == "/" ? $"</{naam}>" : $"<{naam}>");
            }
            pos = m.Index + m.Length;
        }
        sb.Append(Los(html[pos..]));
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Het document als sjabloon: een prijstabel van een deel wordt een prijstabel van de hele calculatie, want het
    /// nummer van een groep geldt alleen in de calculatie waar het uit komt.
    /// </summary>
    public static string AlsSjabloon(string data)
    {
        if (JsonNode.Parse(data) is not JsonObject o || o["blokken"] is not JsonArray lijst) return """{"blokken":[]}""";
        foreach (var b in lijst.OfType<JsonObject>()) b.Remove("groep");
        return o.ToJsonString(Json);
    }

    private static string Los(string t) => t.Replace("<", "&lt;").Replace(">", "&gt;");

    private static string AlsHtml(string tekst) => HtmlEncode(tekst.Trim()).Replace("\r\n", "\n").Replace("\n", "<br>");

    /// <summary>
    /// Het begin van een nieuw document: aanhef (de koptekst van de offerte als die er is), de prijsopbouw uit de
    /// calculatie op hoofdgroepen, de voorwaarden, de voettekst en de ondertekening. Alles is daarna te wijzigen.
    /// </summary>
    public static string Sjabloon(Offerte o)
    {
        var en = o.Taal == OfferteTaal.Engels;
        var blokken = new JsonArray();
        var nr = 0;
        void Voeg(string soort, Action<JsonObject>? vul = null)
        {
            var b = new JsonObject { ["id"] = "b" + (++nr), ["soort"] = soort };
            vul?.Invoke(b);
            blokken.Add(b);
        }
        Voeg("tekst", b => b["html"] = o.OfferteTekst.Trim().Length > 0 ? AlsHtml(o.OfferteTekst)
            : en ? "Dear Sir, Madam,<br><br>Thank you for your enquiry. Please find our quotation for {omschrijving} below."
                 : "Geachte heer, mevrouw,<br><br>Hartelijk dank voor uw aanvraag. Hierbij ontvangt u onze offerte voor {omschrijving}.");
        Voeg("kop", b => b["tekst"] = en ? "Price breakdown" : "Prijsopbouw");
        Voeg("prijs", b => { b["diepte"] = 1; b["regels"] = "geen"; b["aantallen"] = true; b["uren"] = false; });
        Voeg("kop", b => b["tekst"] = en ? "Terms" : "Voorwaarden");
        Voeg("voorwaarden");
        if (o.Voettekst.Trim().Length > 0) Voeg("tekst", b => b["html"] = AlsHtml(o.Voettekst));
        Voeg("ondertekening");
        return new JsonObject { ["blokken"] = blokken }.ToJsonString(Json);
    }
}

// ============================================================================================
//  De calculatie zoals de klant hem mag zien: groepen en regels met alleen verkoop en uren
// ============================================================================================

public sealed record PrijsRegel(string Oms, bool Uur, decimal Verkoop, decimal Uren);

public sealed class PrijsGroep
{
    public long Id;
    public string Naam = "";
    public long? Ouder;
    public decimal Aantal = 1;
    /// <summary>Verkoop en uren van de groep met alles eronder.</summary>
    public decimal Verkoop, Uren;
    public int Diepte;
    public readonly List<PrijsRegel> Regels = new();
    public readonly List<PrijsGroep> Kinderen = new();
}

/// <summary>
/// De boom van een calculatie met per groep en regel de verkoop, uitgerekend zoals de boompagina (bereken() in
/// calculatieboom.html; zelfde regels als <see cref="CalculatieData.Totalen"/>). De volgorde is die van de boom.
/// </summary>
public sealed class Prijsboom
{
    public readonly List<PrijsGroep> Wortels = new();
    /// <summary>Alle groepen in de volgorde van de boom (ouder vóór kind).</summary>
    public readonly List<PrijsGroep> Volgorde = new();
    public readonly Dictionary<long, PrijsGroep> Groepen = new();
    public decimal Totaal, Uren;

    public static Prijsboom Bouw(string json, IReadOnlyDictionary<int, string>? activiteiten = null)
    {
        var boom = new Prijsboom();
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o || o["groepen"] is not JsonArray groepen || o["regels"] is not JsonArray regels) return boom;
            static decimal? G(JsonNode? n) { try { return n is JsonValue v ? v.GetValue<decimal>() : null; } catch { return null; } }
            static string S(JsonNode? n) { try { return n is JsonValue v && v.TryGetValue<string>(out var s) ? s : ""; } catch { return ""; } }
            var inst = o["inst"] as JsonObject;
            decimal Inst(string k, decimal standaard) => G(inst?[k]) ?? standaard;
            decimal ohUur = Inst("ohUur", 22), ohMat = Inst("ohMat", 22), mUur = Inst("mUur", 30), mMat = Inst("mMat", 30);

            var lijst = new List<PrijsGroep>();
            foreach (var g in groepen.OfType<JsonObject>())
            {
                if (G(g["id"]) is not decimal id || boom.Groepen.ContainsKey((long)id)) continue;
                var pg = new PrijsGroep
                {
                    Id = (long)id, Naam = Regex.Replace(S(g["naam"]), @"\s+", " ").Trim(),
                    Ouder = G(g["ouder"]) is decimal ou ? (long)ou : null, Aantal = G(g["aantal"]) ?? 1,
                };
                boom.Groepen[pg.Id] = pg;
                lijst.Add(pg);
            }

            decimal Factor(long id)
            {
                var f = 1m;
                for (var n = 0; n < 20 && boom.Groepen.TryGetValue(id, out var g); n++)
                {
                    f *= g.Aantal;
                    if (g.Ouder is not long ouder) break;
                    id = ouder;
                }
                return f;
            }

            foreach (var r in regels.OfType<JsonObject>())
            {
                if (G(r["groep"]) is not decimal gid || !boom.Groepen.TryGetValue((long)gid, out var groep)) continue;
                var uur = S(r["soort"]) == "uur";
                decimal? detail = r["detail"] is JsonArray d ? d.OfType<JsonObject>().Sum(x => (G(x["aantal"]) ?? 0) * (G(x["waarde"]) ?? 0)) : null;
                var aantal = (detail is not null && uur ? detail : G(r["aantal"])) ?? 0;
                var prijs = (detail is not null && !uur ? detail : G(r["kost"])) ?? 0;
                var keer = G(r["keer"]) ?? Factor(groep.Id);
                var k = aantal * prijs * keer;
                var v = G(r["eigen"]) is decimal eigen
                    ? k * (1 + eigen / 100)
                    : (k + k * (uur ? ohUur : ohMat) / 100) * (1 + (uur ? mUur : mMat) / 100);
                var uren = uur ? aantal * keer : 0;

                var oms = Regex.Replace(S(r["oms"]), @"\s+", " ").Trim();
                if (oms.Length == 0 && uur && G(r["act"]) is decimal act && activiteiten?.TryGetValue((int)act, out var naam) == true) oms = naam;
                if (oms.Length == 0) oms = uur ? "Uren" : "Materiaal";
                groep.Regels.Add(new PrijsRegel(oms, uur, v, uren));

                boom.Totaal += v; boom.Uren += uren;
                var x = groep;
                for (var n = 0; n < 20 && x is not null; n++)
                {
                    x.Verkoop += v; x.Uren += uren;
                    x = x.Ouder is long ou && boom.Groepen.TryGetValue(ou, out var op) && op != x ? op : null;
                }
            }

            foreach (var g in lijst)
                if (g.Ouder is long ou && ou != g.Id && boom.Groepen.TryGetValue(ou, out var ouder)) ouder.Kinderen.Add(g);
            var gezien = new HashSet<long>();
            void Loop(PrijsGroep g, int diepte)
            {
                if (!gezien.Add(g.Id)) return;
                g.Diepte = diepte;
                boom.Volgorde.Add(g);
                foreach (var k in g.Kinderen) Loop(k, diepte + 1);
            }
            foreach (var g in lijst.Where(g => g.Ouder is not long ou || ou == g.Id || !boom.Groepen.ContainsKey(ou))) { boom.Wortels.Add(g); Loop(g, 0); }
            foreach (var g in lijst.Where(g => !gezien.Contains(g.Id))) { boom.Wortels.Add(g); Loop(g, 0); }   // een kring in de boom
        }
        catch { /* een onleesbare calculatie geeft een lege boom */ }
        return boom;
    }
}

// ============================================================================================
//  Opslag
// ============================================================================================

public interface IOfferteDocumentRepository
{
    bool Beschikbaar { get; }
    /// <summary>Is OfferteDocumenten_MariaDB.sql gedraaid?</summary>
    Task<bool> TabelAanwezigAsync();
    Task<OfferteDocument?> GetAsync(int offerteId);
    /// <summary>Nieuw document bij een offerte; heeft de offerte er al een, dan een fout.</summary>
    Task MaakAsync(int offerteId, string data, int? door);
    /// <summary>Opslaan als de versie nog klopt en het document niet vastgelegd is; geeft de nieuwe versie.</summary>
    Task<int> BewaarAsync(int offerteId, int versie, string data, int? door);
    /// <summary>Vastleggen (status 1, met de opmaak van dat moment) of weer bewerkbaar maken (status 0); geeft de nieuwe versie.</summary>
    Task<int> ZetStatusAsync(int offerteId, int status, string? vast, int? door);
    /// <summary>Een concept wissen (om opnieuw te beginnen). False = er was niets te wissen of het is vastgelegd.</summary>
    Task<bool> WisAsync(int offerteId);

    /// <summary>Is OfferteSjablonen_MariaDB.sql gedraaid?</summary>
    Task<bool> SjabloonTabelAanwezigAsync();
    /// <summary>De sjablonen voor deze BV en die voor alle BV's, op naam (zonder Data).</summary>
    Task<List<OfferteSjabloon>> SjablonenAsync(int? bvId);
    Task<OfferteSjabloon?> SjabloonAsync(int id);
    /// <summary>
    /// Een sjabloon bewaren. Bestaat er al een met deze naam voor dezelfde BV, dan wordt die vervangen als
    /// <paramref name="vervang"/> aan staat; anders null (de aanroeper vraagt het dan eerst).
    /// </summary>
    Task<int?> BewaarSjabloonAsync(string naam, int? bvId, string data, int? door, bool vervang);
    Task<bool> WisSjabloonAsync(int id);
}

public class GeenOfferteDocumentRepository : IOfferteDocumentRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<OfferteDocument?> GetAsync(int offerteId) => Task.FromResult<OfferteDocument?>(null);
    public Task MaakAsync(int offerteId, string data, int? door) => Task.CompletedTask;
    public Task<int> BewaarAsync(int offerteId, int versie, string data, int? door) => Task.FromResult(versie);
    public Task<int> ZetStatusAsync(int offerteId, int status, string? vast, int? door) => Task.FromResult(0);
    public Task<bool> WisAsync(int offerteId) => Task.FromResult(false);
    public Task<bool> SjabloonTabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<OfferteSjabloon>> SjablonenAsync(int? bvId) => Task.FromResult(new List<OfferteSjabloon>());
    public Task<OfferteSjabloon?> SjabloonAsync(int id) => Task.FromResult<OfferteSjabloon?>(null);
    public Task<int?> BewaarSjabloonAsync(string naam, int? bvId, string data, int? door, bool vervang) => Task.FromResult<int?>(null);
    public Task<bool> WisSjabloonAsync(int id) => Task.FromResult(false);
}

public class MySqlOfferteDocumentRepository : IOfferteDocumentRepository
{
    private readonly string _cs;
    private bool _tabel;

    public MySqlOfferteDocumentRepository(string cs) => _cs = cs;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;   // eenmaal gevonden blijft het zo
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'OfferteDocumenten'", cn);
        return _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    public async Task<OfferteDocument?> GetAsync(int offerteId)
    {
        if (!await TabelAanwezigAsync()) return null;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT d.`DocumentID`, d.`OfferteID`, d.`Status`, d.`Versie`, d.`Data`, COALESCE(d.`Vast`, ''), " +
            "       COALESCE(d.`GewijzigdOp`, d.`AangemaaktOp`), COALESCE(w.`NaamVoluit`, ''), d.`VastOp`, COALESCE(v.`NaamVoluit`, '') " +
            "  FROM `OfferteDocumenten` d " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = COALESCE(d.`GewijzigdDoor`, d.`AangemaaktDoor`) " +
            "  LEFT JOIN `Werknemers` v ON v.`Werknemer-id` = d.`VastDoor` " +
            " WHERE d.`OfferteID` = @o", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new OfferteDocument(r.GetInt32(0), r.GetInt32(1), Convert.ToInt32(r.GetValue(2)), Convert.ToInt32(r.GetValue(3)),
            r.GetString(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetDateTime(6), r.GetString(7),
            r.IsDBNull(8) ? null : r.GetDateTime(8), r.GetString(9));
    }

    public async Task MaakAsync(int offerteId, string data, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `OfferteDocumenten` (`OfferteID`, `Status`, `Versie`, `Data`, `AangemaaktOp`, `AangemaaktDoor`) " +
            "VALUES (@o, 0, 1, @d, NOW(), @door)", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        cmd.Parameters.AddWithValue("@d", data);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        try { await cmd.ExecuteNonQueryAsync(); }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new InvalidOperationException("Deze offerte heeft al een document.");
        }
    }

    public async Task<int> BewaarAsync(int offerteId, int versie, string data, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `OfferteDocumenten` SET `Data` = @d, `Versie` = `Versie` + 1, `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door " +
            " WHERE `OfferteID` = @o AND `Versie` = @versie AND `Status` = 0", cn);
        cmd.Parameters.AddWithValue("@d", data);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o", offerteId);
        cmd.Parameters.AddWithValue("@versie", versie);
        if (await cmd.ExecuteNonQueryAsync() == 1) return versie + 1;
        throw new OfferteDocumentConflictException(
            "Het document is intussen door iemand anders opgeslagen of vastgelegd. Vernieuw de pagina; je laatste wijziging is niet opgeslagen.");
    }

    public async Task<int> ZetStatusAsync(int offerteId, int status, string? vast, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `OfferteDocumenten` SET `Status` = @s, `Versie` = `Versie` + 1, `Vast` = @vast, " +
            "       `VastOp` = IF(@s = 1, NOW(), NULL), `VastDoor` = IF(@s = 1, @door, NULL) " +
            " WHERE `OfferteID` = @o", cn);
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@vast", status == 1 ? (object?)vast ?? DBNull.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o", offerteId);
        await cmd.ExecuteNonQueryAsync();
        await using var v = new MySqlCommand("SELECT `Versie` FROM `OfferteDocumenten` WHERE `OfferteID` = @o", cn);
        v.Parameters.AddWithValue("@o", offerteId);
        return Convert.ToInt32(await v.ExecuteScalarAsync());
    }

    public async Task<bool> WisAsync(int offerteId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `OfferteDocumenten` WHERE `OfferteID` = @o AND `Status` = 0", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    // ---- sjablonen ----
    private bool _sjabloonTabel;

    public async Task<bool> SjabloonTabelAanwezigAsync()
    {
        if (_sjabloonTabel) return true;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'OfferteSjablonen'", cn);
        return _sjabloonTabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static OfferteSjabloon LeesSjabloon(MySqlDataReader r, bool metData) => new(
        r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : Convert.ToInt32(r.GetValue(2)), metData ? r.GetString(5) : "",
        r.IsDBNull(3) ? null : r.GetDateTime(3), r.GetString(4));

    private const string SjabloonSelect =
        "SELECT s.`SjabloonID`, s.`Naam`, s.`BvID`, COALESCE(s.`GewijzigdOp`, s.`AangemaaktOp`), COALESCE(w.`NaamVoluit`, '')";
    private const string SjabloonVan =
        "  FROM `OfferteSjablonen` s LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = COALESCE(s.`GewijzigdDoor`, s.`AangemaaktDoor`) ";

    public async Task<List<OfferteSjabloon>> SjablonenAsync(int? bvId)
    {
        var lijst = new List<OfferteSjabloon>();
        if (!await SjabloonTabelAanwezigAsync()) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(SjabloonSelect + SjabloonVan + " WHERE s.`BvID` IS NULL OR s.`BvID` = @bv ORDER BY s.`Naam`", cn);
        cmd.Parameters.AddWithValue("@bv", (object?)bvId ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(LeesSjabloon(r, metData: false));
        return lijst;
    }

    public async Task<OfferteSjabloon?> SjabloonAsync(int id)
    {
        if (!await SjabloonTabelAanwezigAsync()) return null;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(SjabloonSelect + ", s.`Data`" + SjabloonVan + " WHERE s.`SjabloonID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? LeesSjabloon(r, metData: true) : null;
    }

    public async Task<int?> BewaarSjabloonAsync(string naam, int? bvId, string data, int? door, bool vervang)
    {
        await using var cn = await OpenAsync();
        int? bestaand;
        await using (var zoek = new MySqlCommand("SELECT `SjabloonID` FROM `OfferteSjablonen` WHERE `Naam` = @n AND `BvID` <=> @bv LIMIT 1", cn))
        {
            zoek.Parameters.AddWithValue("@n", naam);
            zoek.Parameters.AddWithValue("@bv", (object?)bvId ?? DBNull.Value);
            var res = await zoek.ExecuteScalarAsync();
            bestaand = res is null or DBNull ? null : Convert.ToInt32(res);
        }
        if (bestaand is int id)
        {
            if (!vervang) return null;
            await using var upd = new MySqlCommand(
                "UPDATE `OfferteSjablonen` SET `Data` = @d, `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door WHERE `SjabloonID` = @id", cn);
            upd.Parameters.AddWithValue("@d", data);
            upd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            upd.Parameters.AddWithValue("@id", id);
            await upd.ExecuteNonQueryAsync();
            return id;
        }
        await using var cmd = new MySqlCommand(
            "INSERT INTO `OfferteSjablonen` (`Naam`, `BvID`, `Data`, `AangemaaktOp`, `AangemaaktDoor`) VALUES (@n, @bv, @d, NOW(), @door)", cn);
        cmd.Parameters.AddWithValue("@n", naam);
        cmd.Parameters.AddWithValue("@bv", (object?)bvId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@d", data);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    public async Task<bool> WisSjabloonAsync(int id)
    {
        if (!await SjabloonTabelAanwezigAsync()) return false;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `OfferteSjablonen` WHERE `SjabloonID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }
}

/// <summary>Lokaal testen zonder in de database te schrijven (OfferteDocumenten__Geheugen=true): alles weg na herstart.</summary>
public class GeheugenOfferteDocumentRepository : IOfferteDocumentRepository
{
    private readonly Dictionary<int, OfferteDocument> _doc = new();
    private readonly object _slot = new();
    private int _volgende = 1;

    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);

    public Task<OfferteDocument?> GetAsync(int offerteId)
    {
        lock (_slot) return Task.FromResult(_doc.TryGetValue(offerteId, out var d) ? d : null);
    }

    public Task MaakAsync(int offerteId, string data, int? door)
    {
        lock (_slot)
        {
            if (_doc.ContainsKey(offerteId)) throw new InvalidOperationException("Deze offerte heeft al een document.");
            _doc[offerteId] = new OfferteDocument(_volgende++, offerteId, 0, 1, data, "", DateTime.Now, "geheugen", null, "");
        }
        return Task.CompletedTask;
    }

    public Task<int> BewaarAsync(int offerteId, int versie, string data, int? door)
    {
        lock (_slot)
        {
            if (!_doc.TryGetValue(offerteId, out var d) || d.Versie != versie || d.Vastgelegd)
                throw new OfferteDocumentConflictException("Het document is intussen door iemand anders opgeslagen of vastgelegd (geheugen).");
            _doc[offerteId] = d with { Data = data, Versie = versie + 1, GewijzigdOp = DateTime.Now };
            return Task.FromResult(versie + 1);
        }
    }

    public Task<int> ZetStatusAsync(int offerteId, int status, string? vast, int? door)
    {
        lock (_slot)
        {
            var d = _doc[offerteId];
            _doc[offerteId] = d with { Status = status, Versie = d.Versie + 1, Vast = status == 1 ? vast ?? "" : "",
                                       VastOp = status == 1 ? DateTime.Now : null, VastDoor = status == 1 ? "geheugen" : "" };
            return Task.FromResult(d.Versie + 1);
        }
    }

    public Task<bool> WisAsync(int offerteId)
    {
        lock (_slot) return Task.FromResult(_doc.TryGetValue(offerteId, out var d) && !d.Vastgelegd && _doc.Remove(offerteId));
    }

    private readonly List<OfferteSjabloon> _sjablonen = new();
    private int _volgendSjabloon = 1;

    public Task<bool> SjabloonTabelAanwezigAsync() => Task.FromResult(true);

    public Task<List<OfferteSjabloon>> SjablonenAsync(int? bvId)
    {
        lock (_slot) return Task.FromResult(_sjablonen.Where(s => s.BvId is null || s.BvId == bvId).OrderBy(s => s.Naam).Select(s => s with { Data = "" }).ToList());
    }

    public Task<OfferteSjabloon?> SjabloonAsync(int id)
    {
        lock (_slot) return Task.FromResult(_sjablonen.FirstOrDefault(s => s.Id == id));
    }

    public Task<int?> BewaarSjabloonAsync(string naam, int? bvId, string data, int? door, bool vervang)
    {
        lock (_slot)
        {
            var i = _sjablonen.FindIndex(s => s.BvId == bvId && string.Equals(s.Naam, naam, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
            {
                if (!vervang) return Task.FromResult<int?>(null);
                _sjablonen[i] = _sjablonen[i] with { Data = data, Op = DateTime.Now };
                return Task.FromResult<int?>(_sjablonen[i].Id);
            }
            var nieuw = new OfferteSjabloon(_volgendSjabloon++, naam, bvId, data, DateTime.Now, "geheugen");
            _sjablonen.Add(nieuw);
            return Task.FromResult<int?>(nieuw.Id);
        }
    }

    public Task<bool> WisSjabloonAsync(int id)
    {
        lock (_slot) return Task.FromResult(_sjablonen.RemoveAll(s => s.Id == id) > 0);
    }
}

// ============================================================================================
//  Opmaak: van blokken naar HTML (voor het bewerkscherm en de PDF)
// ============================================================================================

/// <summary>Een groep van de calculatie in de keuzelijst van een prijstabel.</summary>
public sealed record DocumentGroep(long Id, string Naam, int Diepte, decimal Verkoop);

/// <summary>
/// Het document opgemaakt: de stijl, het briefhoofd, per blok de HTML (plaatjes niet: die tekent de browser zelf), het
/// slot (disclaimer), het geheel als pagina voor de PDF, en wat er niet klopt (een prijstabel zonder calculatie of van
/// een deel dat niet meer bestaat).
/// </summary>
public sealed record DocumentWeergave(string Stijl, string Kop, Dictionary<string, string> Blokken, string Slot, string Html,
                                      decimal Totaal, bool Calculatie, List<DocumentGroep> Groepen, List<string> Problemen);

public class OfferteDocumentService
{
    private readonly OffertePdfService _pdf;
    private readonly ICalculatieRepository _calc;
    private readonly IOfferteDocumentRepository _repo;

    public OfferteDocumentService(OffertePdfService pdf, ICalculatieRepository calc, IOfferteDocumentRepository repo)
    {
        _pdf = pdf; _calc = calc; _repo = repo;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };
    private static string H(string? t) => HtmlEncode(t ?? "");

    /// <summary>De opmaak die bij het document hoort, bovenop die van de offerte-PDF.</summary>
    private const string DocStijl = """
        .doc-tekst { margin-top: 4mm; line-height: 1.45; overflow-wrap: anywhere; }
        .doc-tekst p, .doc-tekst div { margin: 0; }
        .doc-tekst ul, .doc-tekst ol { margin: 1mm 0; padding-left: 6mm; }
        h3.doc-kop { font-size: 12pt; font-weight: 600; margin: 8mm 0 0; padding-bottom: 1mm; border-bottom: 1px solid var(--kleur);
                     page-break-after: avoid; }
        .doc-plaatje { margin: 5mm 0 0; text-align: center; page-break-inside: avoid; }
        .doc-plaatje img { max-width: 100%; height: auto; }
        .doc-plaatje figcaption { font-size: 8pt; color: #666; margin-top: 1mm; }
        .doc-pagina { page-break-after: always; height: 0; }
        .prijsblok { margin-top: 4mm; }
        .prijsblok col.w-uren { width: 20mm; }
        .prijsblok td.n1 { padding-left: 6mm; } .prijsblok td.n2 { padding-left: 10.5mm; }
        .prijsblok td.n3 { padding-left: 15mm; } .prijsblok td.n4 { padding-left: 19.5mm; }
        .prijsblok tr.hoofd td { font-weight: 600; }
        .prijsblok tr.toel td { color: #666; font-size: 8.5pt; border-bottom: none; padding-top: .3mm; padding-bottom: .3mm; }
        .doc-rij { display: flex; gap: 6mm; align-items: flex-start; page-break-inside: avoid; }
        .doc-rij > div { min-width: 0; }
        .doc-half { width: calc(50% - 3mm); } .doc-derde { width: calc(33.333% - 4mm); } .doc-tweederde { width: calc(66.667% - 2mm); }
        .regels.eigen { margin-top: 4mm; }
        .eigen td, .eigen th { height: 6mm; }
        .doc-mist { margin-top: 4mm; padding: 2mm 3mm; border: 1px dashed #c0392b; color: #c0392b; font-size: 9pt; }
        .voorwaarden.in-doc { margin-top: 4mm; }
        """;

    /// <summary>
    /// Het document opmaken met de cijfers van nu. <paramref name="ondertekenaarId"/> = wie de offerte ondertekent (de
    /// gebruiker die hem maakt of verstuurt), zoals bij de gewone offerte-PDF.
    /// </summary>
    public async Task<DocumentWeergave> WeergaveAsync(int offerteId, string data, int? ondertekenaarId)
    {
        var g = await _pdf.LaadAsync(offerteId, ondertekenaarId);
        var o = g.Offerte;
        var t = new OffertePdfService.Teksten(o.Taal == OfferteTaal.Engels);
        var datum = DateOnly.FromDateTime(DateTime.Today);

        Calculatie? c = null;
        Dictionary<int, string> activiteiten = new();
        try
        {
            if (_calc.Beschikbaar && await _calc.TabelAanwezigAsync())
            {
                c = await _calc.GetAsync(offerteId);
                if (c is not null) activiteiten = (await _calc.TarievenAsync()).ToDictionary(x => x.ActiviteitId, x => x.Naam);
            }
        }
        catch { c = null; }
        var boom = Prijsboom.Bouw(c?.Data ?? CalculatieData.Leeg, activiteiten);
        var totaal = Math.Round(boom.Totaal, 2);

        var velden = new Dictionary<string, string>
        {
            ["{klant}"] = H(g.Klant.Naam), ["{contactpersoon}"] = H(g.Klant.Tav), ["{offertenummer}"] = H(o.OfferteNr),
            ["{omschrijving}"] = H(o.Omschrijving), ["{project}"] = H($"{o.Projectnummer} {o.Project}".Trim()),
            ["{datum}"] = t.Datum(datum), ["{totaal}"] = t.Geld(totaal),
        };
        string Vul(string html)
        {
            foreach (var (veld, waarde) in velden) html = html.Replace(veld, waarde, StringComparison.OrdinalIgnoreCase);
            return html;
        }

        var problemen = new List<string>();
        var blokken = new Dictionary<string, string>();
        var geheel = new StringBuilder();
        var delen = new List<(string Deel, string Html)>();
        var lijst = (JsonNode.Parse(data) as JsonObject)?["blokken"] as JsonArray ?? new JsonArray();
        foreach (var b in lijst.OfType<JsonObject>())
        {
            string S(string veld) { try { return b[veld] is JsonValue v && v.TryGetValue<string>(out var s) ? s : ""; } catch { return ""; } }
            decimal? G(string veld) { try { return b[veld] is JsonValue v ? v.GetValue<decimal>() : null; } catch { return null; } }
            bool W(string veld) { try { return b[veld] is JsonValue v && v.TryGetValue<bool>(out var w) && w; } catch { return false; } }
            var id = S("id");
            string html;
            switch (S("soort"))
            {
                case "tekst": html = $"<div class='doc-tekst'>{Vul(S("html"))}</div>"; break;
                case "kop": html = $"<h3 class='doc-kop'>{Vul(H(S("tekst")))}</h3>"; break;
                case "plaatje":
                    var onder = S("onderschrift");
                    delen.Add((S("deel"), $"<figure class='doc-plaatje'><img src='{S("src")}' style='width:{(int)(G("breedte") ?? 60)}%' alt=''>" +
                                          (onder.Length > 0 ? $"<figcaption>{H(onder)}</figcaption>" : "") + "</figure>"));
                    continue;   // het plaatje zelf gaat niet nog eens los naar de browser
                case "tabel":
                    var rechts = (b["rechts"] as JsonArray)?.Select(x => { try { return x!.GetValue<int>(); } catch { return -1; } }).ToHashSet() ?? new();
                    var metKop = W("kop");
                    var tb = new StringBuilder("<table class='regels eigen'>");
                    var nr = 0;
                    foreach (var rij in (b["rijen"] as JsonArray)?.OfType<JsonArray>() ?? Enumerable.Empty<JsonArray>())
                    {
                        var isKop = metKop && nr++ == 0;
                        tb.Append(isKop ? "<tr class='kopjes'>" : "<tr>");
                        for (var k = 0; k < rij.Count; k++)
                        {
                            string cel;
                            try { cel = rij[k]?.GetValue<string>() ?? ""; } catch { cel = ""; }
                            var tag = isKop ? "th" : "td";
                            tb.Append($"<{tag}{(rechts.Contains(k) ? " class='r'" : "")}>{Vul(H(cel))}</{tag}>");
                        }
                        tb.Append("</tr>");
                    }
                    html = tb.Append("</table>").ToString();
                    break;
                case "pagina": html = "<div class='doc-pagina'></div>"; break;
                case "voorwaarden":
                    html = _pdf.Voorwaarden(g, c is not null ? totaal : o.Totalen.Totaal).Replace("class='voorwaarden'", "class='voorwaarden in-doc'");
                    break;
                case "ondertekening": html = _pdf.Ondertekening(g); break;
                case "prijs":
                    if (c is null)
                    {
                        problemen.Add("Er staat een prijstabel in, maar deze offerte heeft nog geen calculatie.");
                        html = $"<div class='doc-mist'>{H(t.En ? "No calculation yet for this quotation." : "Deze offerte heeft nog geen calculatie; de prijstabel is leeg.")}</div>";
                    }
                    else html = Prijstabel(boom, G("groep") is decimal gr ? (long)gr : null, (int)(G("diepte") ?? 1), S("regels"), W("aantallen"), W("uren"), t, problemen);
                    break;
                default: continue;
            }
            blokken[id] = html;
            delen.Add((S("deel"), html));
        }

        // blokken die niet de hele breedte nemen staan naast elkaar, zolang ze samen op één regel passen
        var naast = new List<(string Deel, string Html)>();
        var som = 0m;
        void Sluit()
        {
            if (naast.Count == 0) return;
            geheel.Append("<div class='doc-rij'>");
            foreach (var (dl, h) in naast) geheel.Append($"<div class='doc-{dl}'>{h}</div>");
            geheel.Append("</div>");
            naast.Clear(); som = 0;
        }
        foreach (var (deel, h) in delen)
        {
            var breedte = OfferteDocumentData.Breedte(deel);
            if (breedte >= 1) { Sluit(); geheel.Append(h); continue; }
            if (som + breedte > 1.001m) Sluit();
            naast.Add((deel, h)); som += breedte;
        }
        Sluit();

        var kleur = OffertePdfService.Kleur(g.Afzender);
        var stijl = OffertePdfService.Stijl(kleur, $"{o.OfferteNr}  ·  {t.Datum(datum)}  ·  {OffertePdfService.VoetBv(g.Afzender)}") + DocStijl;
        var kop = _pdf.Briefhoofd(g, datum);
        var slot = o.Disclaimer.Trim().Length > 0 ? $"<div class='disclaimer'>{OffertePdfService.Alinea(o.Disclaimer)}</div>" : "";
        var pagina = "<!doctype html><html><head><meta charset='utf-8'>" +
                     $"<title>{H($"{t.Offerte} {o.OfferteNr}")}</title><style>{stijl}</style></head><body>{kop}{geheel}{slot}</body></html>";

        return new DocumentWeergave(stijl, kop, blokken, slot, pagina, totaal, c is not null,
            boom.Volgorde.Select(x => new DocumentGroep(x.Id, x.Naam, x.Diepte, Math.Round(x.Verkoop, 2))).ToList(),
            problemen.Distinct().ToList());
    }

    // ---- de prijstabel -------------------------------------------------------------------------

    private sealed class Rij
    {
        public string Tekst = "";
        public decimal? Bedrag, Uren;
        public bool Toelichting;
        public List<Rij> Kind = new();
    }

    /// <summary>Heeft de groep iets om te tonen (een bedrag, of regels met een bedrag ergens eronder)? Een lege groep komt niet op de offerte.</summary>
    private static bool Gevuld(PrijsGroep g) => g.Verkoop != 0 || g.Regels.Any(r => r.Verkoop != 0) || g.Kinderen.Any(Gevuld);

    /// <summary>
    /// Een prijstabel uit (een deel van) de calculatie. <paramref name="diepte"/> = hoeveel niveaus groepen eronder
    /// getoond worden (0 = alleen het totaal); <paramref name="regels"/> = geen, omschrijving (zonder bedrag) of prijs.
    /// Wat niet getoond wordt maar wel meetelt, komt als een regel "Overig", zodat de tabel altijd optelt tot het totaal.
    /// </summary>
    private static string Prijstabel(Prijsboom boom, long? groepId, int diepte, string regels, bool aantallen, bool uren,
                                     OffertePdfService.Teksten t, List<string> problemen)
    {
        PrijsGroep? groep = null;
        if (groepId is long gid && !boom.Groepen.TryGetValue(gid, out groep))
        {
            problemen.Add("Een prijstabel verwijst naar een deel van de calculatie dat niet meer bestaat.");
            return $"<div class='doc-mist'>{H(t.En ? "This part of the calculation no longer exists." : "Dit deel van de calculatie bestaat niet meer. Kies een ander deel of verwijder dit blok.")}</div>";
        }

        Rij VoorGroep(PrijsGroep g, int rest) => new()
        {
            Tekst = g.Naam + (aantallen && g.Aantal != 1 ? $" ({t.Getal(g.Aantal)}×)" : ""),
            Bedrag = g.Verkoop, Uren = g.Uren, Kind = Inhoud(g, rest),
        };
        List<Rij> Inhoud(PrijsGroep g, int rest)
        {
            var uit = new List<Rij>();
            if (regels == "prijs")
                uit.AddRange(g.Regels.Where(r => r.Verkoop != 0).Select(r => new Rij { Tekst = r.Oms, Bedrag = r.Verkoop, Uren = r.Uur ? r.Uren : null }));
            else if (regels == "omschrijving")
                uit.AddRange(g.Regels.Where(r => r.Verkoop != 0).Select(r => r.Oms).Distinct().Select(oms => new Rij { Tekst = oms, Toelichting = true }));
            if (rest > 0) uit.AddRange(g.Kinderen.Where(Gevuld).Select(k => VoorGroep(k, rest - 1)));
            var metBedrag = uit.Where(x => x.Bedrag is not null).ToList();
            if (metBedrag.Count > 0)
            {
                var restant = g.Verkoop - metBedrag.Sum(x => x.Bedrag!.Value);
                if (Math.Abs(restant) >= 0.005m)
                    uit.Add(new Rij { Tekst = t.OverigRegel, Bedrag = restant, Uren = g.Uren - metBedrag.Sum(x => x.Uren ?? 0) });
            }
            return uit;
        }

        var rijen = diepte <= 0 ? new List<Rij>()
                  : groep is null ? boom.Wortels.Where(Gevuld).Select(w => VoorGroep(w, diepte - 1)).ToList()
                  : Inhoud(groep, diepte);
        var totaal = Math.Round(groep?.Verkoop ?? boom.Totaal, 2);
        RondAf(rijen, totaal);

        var kolommen = uren ? 3 : 2;
        var sb = new StringBuilder("<table class='regels prijsblok'><colgroup><col>");
        if (uren) sb.Append("<col class='w-uren'>");
        sb.Append("<col class='w-geld'></colgroup>");
        if (rijen.Count > 0)
        {
            sb.Append($"<tr class='kopjes'><th>{H(t.Omschrijving)}</th>");
            if (uren) sb.Append($"<th class='r'>{H(t.UrenKop)}</th>");
            sb.Append($"<th class='r'>{H(t.Bedrag)}</th></tr>");
        }
        void Schrijf(List<Rij> lijst, int niveau)
        {
            foreach (var r in lijst)
            {
                var n = Math.Min(niveau, 4);
                if (r.Toelichting) { sb.Append($"<tr class='toel'><td class='n{Math.Min(n + 1, 4)}' colspan='{kolommen}'>{H(r.Tekst)}</td></tr>"); continue; }
                sb.Append($"<tr class='{(r.Kind.Any(k => k.Bedrag is not null) ? "hoofd" : "")}'><td class='n{n}'>{H(r.Tekst)}</td>");
                if (uren) sb.Append($"<td class='r'>{(r.Uren is decimal u && u != 0 ? t.Getal(Math.Round(u, 1)) : "")}</td>");
                sb.Append($"<td class='r'>{t.Geld(r.Bedrag ?? 0)}</td></tr>");
                Schrijf(r.Kind, niveau + 1);
            }
        }
        Schrijf(rijen, 0);
        var label = groep is null ? t.TotaalExcl : $"{t.Totaal} {groep.Naam}";
        sb.Append($"<tr class='sectietotaal'><td colspan='{kolommen - 1}' class='r'>{H(label)}</td><td class='r'>{t.Geld(totaal)}</td></tr></table>");
        return sb.ToString();
    }

    /// <summary>
    /// Bedragen op centen, zo dat de regels onder een bedrag er samen precies op uitkomen: een verschil van een paar cent
    /// door het afronden gaat naar de grootste regel.
    /// </summary>
    private static void RondAf(List<Rij> rijen, decimal doel)
    {
        var met = rijen.Where(r => r.Bedrag is not null).ToList();
        foreach (var r in met) r.Bedrag = Math.Round(r.Bedrag!.Value, 2);
        if (met.Count > 0)
        {
            var verschil = doel - met.Sum(r => r.Bedrag!.Value);
            if (verschil != 0 && Math.Abs(verschil) <= 0.01m * met.Count)
            {
                var grootste = met.OrderByDescending(r => Math.Abs(r.Bedrag!.Value)).First();
                grootste.Bedrag += verschil;
            }
        }
        foreach (var r in met) RondAf(r.Kind, r.Bedrag!.Value);
    }

    // ---- vastleggen en de PDF --------------------------------------------------------------------

    /// <summary>De opmaak die bij het vastleggen is bewaard; null = niet (goed) bewaard.</summary>
    public static DocumentWeergave? UitVast(string vast)
    {
        try
        {
            if (vast.Length == 0 || JsonNode.Parse(vast) is not JsonObject o) return null;
            string S(string k) => o[k]?.GetValue<string>() ?? "";
            var blokken = (o["blokken"] as JsonObject)?.ToDictionary(kv => kv.Key, kv => kv.Value?.GetValue<string>() ?? "") ?? new();
            return new DocumentWeergave(S("stijl"), S("kop"), blokken, S("slot"), S("html"), o["totaal"]?.GetValue<decimal>() ?? 0, true, new(), new());
        }
        catch { return null; }
    }

    private static string NaarVast(DocumentWeergave w) => new JsonObject
    {
        ["stijl"] = w.Stijl, ["kop"] = w.Kop, ["slot"] = w.Slot, ["html"] = w.Html, ["totaal"] = w.Totaal,
        ["blokken"] = new JsonObject(w.Blokken.Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key, kv.Value))),
    }.ToJsonString(Json);

    /// <summary>
    /// Het document vastleggen met de cijfers van nu. Geeft de reden terug als het niet kan (een prijstabel zonder
    /// calculatie of van een deel dat niet meer bestaat); null = vastgelegd (of het was al vastgelegd).
    /// </summary>
    public async Task<string?> LegVastAsync(int offerteId, int? door)
    {
        var d = await _repo.GetAsync(offerteId);
        if (d is null) return "Deze offerte heeft geen document.";
        if (d.Vastgelegd) return null;
        var w = await WeergaveAsync(offerteId, d.Data, door);
        if (w.Problemen.Count > 0) return string.Join(" ", w.Problemen);
        await _repo.ZetStatusAsync(offerteId, 1, NaarVast(w), door);
        return null;
    }

    /// <summary>
    /// De pagina voor de PDF: vastgelegd = zoals bewaard, anders met de cijfers van nu. Null = deze offerte heeft geen
    /// document (dan geldt de gewone offerte-PDF).
    /// </summary>
    public async Task<string?> HtmlVoorPdfAsync(int offerteId, int? ondertekenaarId)
    {
        if (!_repo.Beschikbaar || !await _repo.TabelAanwezigAsync()) return null;
        var d = await _repo.GetAsync(offerteId);
        if (d is null) return null;
        if (d.Vastgelegd && UitVast(d.Vast) is { Html.Length: > 0 } vast) return vast.Html;
        return (await WeergaveAsync(offerteId, d.Data, ondertekenaarId)).Html;
    }

    public Task<byte[]> NaarPdfAsync(string html, CancellationToken stop = default) => _pdf.NaarPdfAsync(html, stop);
}
