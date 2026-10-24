using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EverythingApp.Services;

/// <summary>
/// Haalt het assortiment van het Kaeswienkeltje van hun webshop (WordPress/WooCommerce, openbare Store API, alleen lezen)
/// en zet het in BroodjesAssortiment. Vanzelf als het ouder is dan een week (bij het openen van /broodjes), of met ↻.
/// Alleen wat in een lunchcategorie staat (broodjes, salades, drinken …) komt mee; de winkel (vleeswaren per ons, kaas,
/// noten, wijn, jam) blijft weg. Welke categorieën: Broodjes:Categorieen.
/// </summary>
public class BroodjesAssortimentService
{
    private readonly IBroodjesRepository _repo;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _cfg;
    private readonly ILogger<BroodjesAssortimentService> _log;
    private readonly SemaphoreSlim _bezig = new(1, 1);

    public BroodjesAssortimentService(IBroodjesRepository repo, IHttpClientFactory http, IConfiguration cfg,
                                      ILogger<BroodjesAssortimentService> log)
    {
        _repo = repo; _http = http; _cfg = cfg; _log = log;
    }

    public string Webshop => (_cfg["Broodjes:Webshop"] is { Length: > 0 } w ? w : "https://kaeswienkeltje.nl").TrimEnd('/');
    public static readonly TimeSpan Verversen = TimeSpan.FromDays(7);

    /// <summary>Stukjes van categorienamen die lunch zijn (hoofdletters maken niet uit); een product in minstens één ervan doet mee.</summary>
    private IReadOnlyList<string> Lunch =>
        (_cfg["Broodjes:Categorieen"] is { Length: > 0 } c ? c
            : "broodje;broodjes;stokbrood;clubsandwich;wraps;pizza;salades;hartig;tapas;drankjes;frisdrank;warm-drank;smoothie;" +
              "energie;gebak;appelflap;kids;gastenlunch;duurbeleg")
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Volgorde van de groepen in de keuzelijst; de eerste categorie die erin past, wint.</summary>
    private static readonly (string Groep, string[] Woorden)[] Groepen =
    {
        ("Warme broodjes", new[] { "warme broodjes", "warm broodje" }),
        ("Italiaanse broodjes", new[] { "italiaans" }),
        ("Koude broodjes", new[] { "koud broodje", "nieuw broodje", "stokbrood", "clubsandwich", "belegde broodjes", "duurbeleg" }),
        ("Wraps, pizza & tosti", new[] { "wraps", "pizza", "tosti" }),
        ("Salades", new[] { "salades" }),
        ("Hartig & tapas", new[] { "hartig", "tapas" }),
        ("Drinken", new[] { "drank", "warm-drank", "smoothie", "sap" }),
        ("Gebak & zoet", new[] { "gebak", "koek", "appelflap", "bonbons" }),
        ("Kids", new[] { "kids" }),
    };

    /// <summary>
    /// Bijwerken als het assortiment leeg of ouder dan een week is. Is een ander scherm al bezig, dan wachten tot dat
    /// klaar is (anders ziet het tweede scherm een lege lijst). Fouten alleen in het logboek.
    /// </summary>
    public async Task ZorgVersAsync()
    {
        try
        {
            if (!_repo.Beschikbaar || !await _repo.TabellenAanwezigAsync()) return;
            await _bezig.WaitAsync();
            try
            {
                var bij = await _repo.AssortimentBijgewerktAsync();
                if (bij is DateTime d && DateTime.Now - d < Verversen) return;
                await HaalEnBewaarAsync();
            }
            finally { _bezig.Release(); }
        }
        catch (Exception ex) { _log.LogWarning("Broodjes: assortiment vanzelf bijwerken mislukt: {Fout}", ex.Message); }
    }

    /// <summary>Alles van de site halen en opslaan (knop ↻). Geeft een korte uitkomst voor de melding.</summary>
    public async Task<string> BijwerkenAsync()
    {
        if (!await _bezig.WaitAsync(0)) return "Het assortiment wordt al bijgewerkt.";
        try { return await HaalEnBewaarAsync(); }
        finally { _bezig.Release(); }
    }

    private async Task<string> HaalEnBewaarAsync()
    {
        var vanSite = await HaalOpAsync();
        if (vanSite.Count == 0) return "Geen producten gevonden op de site; er is niets veranderd.";
        var (nieuw, bij, weg) = await _repo.BewaarAssortimentAsync(vanSite);
        _log.LogInformation("Broodjes: assortiment bijgewerkt, {Aantal} van de site ({Nieuw} nieuw, {Weg} niet meer leverbaar)",
                            vanSite.Count, nieuw, weg);
        return $"Assortiment bijgewerkt: {vanSite.Count} producten ({nieuw} nieuw, {bij} bijgewerkt, {weg} niet meer op de site).";
    }

    private async Task<List<BroodjeArtikel>> HaalOpAsync()
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MDE-EverythingApp/1.0 (broodjeslijst)");
        var lunch = Lunch;
        var lijst = new List<BroodjeArtikel>();
        for (var pagina = 1; pagina <= 20; pagina++)
        {
            using var antwoord = await client.GetAsync($"{Webshop}/wp-json/wc/store/v1/products?per_page=100&page={pagina}");
            antwoord.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) break;
            foreach (var p in doc.RootElement.EnumerateArray())
                if (Lees(p, lunch) is { } a) lijst.Add(a);

            var totaal = antwoord.Headers.TryGetValues("X-WP-TotalPages", out var t) && int.TryParse(t.FirstOrDefault(), out var n) ? n : 1;
            if (pagina >= totaal) break;
        }
        return lijst;
    }

    private static BroodjeArtikel? Lees(JsonElement p, IReadOnlyList<string> lunch)
    {
        if (!p.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id)) return null;
        var naam = Tekst(Str(p, "name"));
        if (naam.Length == 0) return null;

        var cats = p.TryGetProperty("categories", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().Select(x => Tekst(Str(x, "name"))).Where(x => x.Length > 0).ToList()
            : new List<string>();
        if (!cats.Any(k => lunch.Any(l => k.Contains(l, StringComparison.OrdinalIgnoreCase)))) return null;

        decimal? prijs = null;
        if (p.TryGetProperty("prices", out var pr) && decimal.TryParse(Str(pr, "price"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ruw))
        {
            var minor = pr.TryGetProperty("currency_minor_unit", out var m) && m.TryGetInt32(out var mm) ? mm : 2;
            prijs = ruw / (decimal)Math.Pow(10, minor);
        }

        var afbeelding = p.TryGetProperty("images", out var im) && im.ValueKind == JsonValueKind.Array && im.GetArrayLength() > 0
            ? (Str(im[0], "thumbnail") is { Length: > 0 } th ? th : Str(im[0], "src"))
            : "";
        var leverbaar = Bool(p, "is_in_stock", true) && Bool(p, "is_purchasable", true);

        return new BroodjeArtikel
        {
            ExternId = id,
            Naam = naam,
            Omschrijving = Tekst(Str(p, "short_description")),
            Categorie = string.Join(" / ", cats),
            Groep = GroepVan(cats),
            Prijs = prijs,
            Afbeelding = afbeelding,
            Leverbaar = leverbaar,
        };
    }

    private static string GroepVan(List<string> cats)
    {
        foreach (var (groep, woorden) in Groepen)
            if (cats.Any(k => woorden.Any(w => k.Contains(w, StringComparison.OrdinalIgnoreCase)))) return groep;
        return "Overig";
    }

    private static string Str(JsonElement e, string naam)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(naam, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string naam, bool standaard)
        => e.TryGetProperty(naam, out var v) ? v.ValueKind == JsonValueKind.True || (v.ValueKind != JsonValueKind.False && standaard) : standaard;

    /// <summary>HTML eraf, entiteiten (&amp;amp;) terug, zacht afbreekstreepje (U+00AD) weg, witruimte samen.</summary>
    public static string Tekst(string html)
    {
        var t = Regex.Replace(html ?? "", "<[^>]+>", " ");
        t = WebUtility.HtmlDecode(t).Replace("­", "").Replace(' ', ' ');
        return Regex.Replace(t, @"\s+", " ").Trim();
    }
}
