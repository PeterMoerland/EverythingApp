using System.Globalization;
using System.Text;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Wat het zoeken op een naam van een klant of leverancier opleverde.</summary>
public sealed class NaamTreffer
{
    /// <summary>Zoals het gevraagd is ("VA Imaging").</summary>
    public string Gevraagd { get; init; } = "";

    /// <summary>De namen die erop lijken ("VA-Imaging"), als er niets letterlijk zo heet. Leeg = letterlijk gevonden, of niets.</summary>
    public List<string> Lijkt { get; init; } = new();

    /// <summary>Niets gevonden: de namen die het dichtst in de buurt komen, om aan de gebruiker voor te leggen.</summary>
    public List<string> Suggesties { get; init; } = new();

    /// <summary>Er heet iets letterlijk zo (de naam bevat de gevraagde tekst).</summary>
    public bool Letterlijk { get; init; }

    /// <summary>Er leken meer namen op dan er gebruikt zijn (zoveel zijn er weggelaten).</summary>
    public int Afgekapt { get; init; }

    public bool Gevonden => Letterlijk || Lijkt.Count > 0;

    /// <summary>Waarop de opvraging moet zoeken (elk als "naam bevat"): de gevraagde tekst, of de namen die erop lijken.</summary>
    public IReadOnlyList<string> Patronen => Lijkt.Count > 0 ? Lijkt : new[] { Gevraagd };

    /// <summary>Voor de titel van de opvraging: 'VA-Imaging' (gevraagd: 'VA Imaging').</summary>
    public string Titel => Lijkt.Count > 0 ? $"'{string.Join("', '", Lijkt)}' (gevraagd: '{Gevraagd}')" : $"'{Gevraagd}'";

    /// <summary>Wat het model moet weten; leeg als de naam gewoon letterlijk gevonden is. soort = "klant" of "leverancier".</summary>
    public string Noot(string soort)
    {
        if (Lijkt.Count > 0)
            return $"Let op: geen {soort} heeft letterlijk '{Gevraagd}' in de naam; er is gezocht op wat erop lijkt: {string.Join(", ", Lijkt)}" +
                   (Afgekapt > 0 ? $" (en {Afgekapt} andere namen die erop lijken zijn weggelaten: vraag om een preciezere naam)" : "") +
                   ". Noem in je antwoord de naam waarop gezocht is. ";
        if (!Letterlijk)
            return $"Let op: er is geen {soort} gevonden die op '{Gevraagd}' lijkt, dus deze lege uitkomst zegt niets. " +
                   (Suggesties.Count > 0 ? $"Namen die in de buurt komen: {string.Join(", ", Suggesties)}. " : "") +
                   $"Zeg dat de naam niet gevonden is en vraag hoe de {soort} precies heet; concludeer niet dat er niets is. ";
        return "";
    }
}

/// <summary>
/// Slim zoeken op de naam van een klant of leverancier, voor Vragen aan AI (wens gebruiker 18-11-2026: "VA Imaging" vond
/// niets, "VA-Imaging" wel). De opvragingen zochten letterlijk op een stukje van de naam. Nu, alleen als er niets
/// letterlijk zo heet: eerst zonder spaties, streepjes en leestekens (en zonder accenten), dan op de losse woorden in
/// willekeurige volgorde, dan met een tikfout verbeterd (per woord, of in de hele naam). Het antwoord zegt op welke
/// naam gezocht is; lijkt er niets op, dan komen de namen in de buurt als voorstel mee.
/// De namen uit Relaties worden tien minuten onthouden. Alleen lezen.
/// </summary>
public class RelatieNamen
{
    private readonly string? _cs;
    private List<string>? _namen;
    private DateTime _gelezen;
    private readonly SemaphoreSlim _slot = new(1, 1);

    public RelatieNamen(IConfiguration config) => _cs = config.GetConnectionString("MDE");

    /// <summary>Null als er geen naam gevraagd is. Zonder database: letterlijk, zoals het was.</summary>
    public async Task<NaamTreffer?> ZoekAsync(string? gevraagd)
    {
        if (string.IsNullOrWhiteSpace(gevraagd)) return null;
        List<string> namen;
        try { namen = await NamenAsync(); }
        catch { return new NaamTreffer { Gevraagd = gevraagd.Trim(), Letterlijk = true }; }
        return namen.Count == 0 ? new NaamTreffer { Gevraagd = gevraagd.Trim(), Letterlijk = true } : Zoek(gevraagd, namen);
    }

    private async Task<List<string>> NamenAsync()
    {
        if (_namen is not null && DateTime.UtcNow - _gelezen < TimeSpan.FromMinutes(10)) return _namen;
        if (string.IsNullOrWhiteSpace(_cs)) return new();
        await _slot.WaitAsync();
        try
        {
            if (_namen is not null && DateTime.UtcNow - _gelezen < TimeSpan.FromMinutes(10)) return _namen;
            var lijst = new List<string>();
            await using var cn = new MySqlConnection(_cs);
            await cn.OpenAsync();
            await using var cmd = new MySqlCommand("SELECT DISTINCT TRIM(`name`) FROM `Relations` WHERE `name` IS NOT NULL AND TRIM(`name`) <> ''", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) lijst.Add(r.GetString(0));
            _namen = lijst; _gelezen = DateTime.UtcNow;
            return lijst;
        }
        finally { _slot.Release(); }
    }

    // ================================================================
    //  Het zoeken zelf (zonder database, los te toetsen)
    // ================================================================

    private const int MaxNamen = 8;

    /// <summary>Kleine letters, zonder accenten, alleen letters en cijfers: "VA-Imaging B.V." → "vaimagingbv".</summary>
    public static string Kaal(string? tekst)
    {
        var sb = new StringBuilder();
        foreach (var c in (tekst ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // rechtsvormen zeggen niets over welke relatie het is
    private static readonly HashSet<string> Rechtsvorm = new() { "bv", "nv", "vof", "gmbh", "ltd", "inc", "bvba", "ag", "sa", "srl", "co" };

    private static List<string> Woorden(string tekst)
    {
        var woorden = new List<string>();
        var sb = new StringBuilder();
        foreach (var c in tekst.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0) { woorden.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) woorden.Add(sb.ToString());
        return woorden;
    }

    public static NaamTreffer Zoek(string gevraagd, IReadOnlyList<string> namen)
    {
        gevraagd = gevraagd.Trim();
        // 1. letterlijk: zoals de opvragingen altijd al zochten
        if (namen.Any(n => n.Contains(gevraagd, StringComparison.OrdinalIgnoreCase)))
            return new NaamTreffer { Gevraagd = gevraagd, Letterlijk = true };

        var kaal = Kaal(gevraagd);
        var kalen = namen.Select(n => (Naam: n, Woorden: Woorden(n))).Where(x => x.Woorden.Count > 0)
                         .Select(x => (x.Naam, Kaal: string.Concat(x.Woorden), x.Woorden)).ToList();
        NaamTreffer Lijkt(IEnumerable<string> treffers)
        {
            var alle = treffers.Distinct(StringComparer.OrdinalIgnoreCase)
                               .OrderBy(n => Math.Abs(Kaal(n).Length - kaal.Length)).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            return new NaamTreffer { Gevraagd = gevraagd, Lijkt = alle.Take(MaxNamen).ToList(), Afgekapt = Math.Max(0, alle.Count - MaxNamen) };
        }

        // 2 en 3, ook gebruikt na het verbeteren van een tikfout: zonder spaties/streepjes/leestekens ("VA Imaging" =
        // "VA-Imaging" = "va imaging b.v."), en anders de losse woorden in willekeurige volgorde ("Imaging VA"). Een woord
        // telt als het aan het begin van een woord in de naam staat, of (vanaf 4 tekens) ergens in de naam.
        List<string> Ruim(string tekst)
        {
            var k = Kaal(tekst);
            if (k.Length >= 3)
            {
                var zonder = kalen.Where(x => x.Kaal.Contains(k)).Select(x => x.Naam).ToList();
                if (zonder.Count > 0) return zonder;
            }
            var woorden = Woorden(tekst).Where(w => w.Length >= 2).ToList();
            var kern = woorden.Where(w => !Rechtsvorm.Contains(w)).ToList();
            if (kern.Count > 0) woorden = kern;
            if (woorden.Count > 1 && woorden.Sum(w => w.Length) >= 4)
                return kalen.Where(x => woorden.All(w => x.Woorden.Any(nw => nw.StartsWith(w)) || (w.Length >= 4 && x.Kaal.Contains(w))))
                            .Select(x => x.Naam).ToList();
            return new();
        }
        var ruim = Ruim(gevraagd);
        if (ruim.Count > 0) return Lijkt(ruim);

        // 4. een tikfout in een woord ("Omrom", "Festoo Nederland"): elk woord dat in geen enkele naam voorkomt vervangen
        //    door het woord uit de namen dat er het meest op lijkt (zelfde beginletter, een teken anders, vanaf 8 tekens
        //    twee), en dan opnieuw zoeken. Bij één woord wordt dat woord het zoekpatroon, zodat alle relaties met die naam
        //    meedoen (alle Omron-bedrijven), net als bij letterlijk zoeken.
        var telling = new Dictionary<string, int>();
        foreach (var x in kalen) foreach (var w in x.Woorden.Distinct()) telling[w] = telling.GetValueOrDefault(w) + 1;
        var verbeterd = new List<string>();
        var gewijzigd = false;
        foreach (var w in Woorden(gevraagd))
        {
            if (w.Length < 4 || telling.ContainsKey(w) || telling.Keys.Any(v => v.StartsWith(w))) { verbeterd.Add(w); continue; }
            var grensW = w.Length >= 8 ? 2 : 1;
            var beste = telling.Keys.Where(v => v[0] == w[0] && Math.Abs(v.Length - w.Length) <= grensW)
                                    .Select(v => (Woord: v, Afstand: Levenshtein(w, v))).Where(x => x.Afstand <= grensW)
                                    .OrderBy(x => x.Afstand).ThenByDescending(x => telling[x.Woord]).ThenBy(x => x.Woord).FirstOrDefault();
            if (beste.Woord is null) { verbeterd.Add(w); continue; }
            verbeterd.Add(beste.Woord); gewijzigd = true;
        }
        if (gewijzigd)
        {
            var tekst = string.Join(" ", verbeterd);
            if (namen.Any(n => n.Contains(tekst, StringComparison.OrdinalIgnoreCase)))
                return new NaamTreffer { Gevraagd = gevraagd, Lijkt = new() { tekst } };
            var na = Ruim(tekst);
            if (na.Count > 0) return Lijkt(na);
        }

        // 5. de hele tekst met een tikfout of aan elkaar geschreven ("Kaas winkeltje" = "Kaeswienkeltje"): vergeleken met
        //    een even lang stuk van de naam dat aan het begin van een woord begint, met dezelfde beginletter.
        if (kaal.Length >= 4)
        {
            var afstanden = kalen.Select(x => (x.Naam, Afstand: Afstand(kaal, x.Woorden, zelfdeLetter: true))).ToList();
            var grens = kaal.Length >= 8 ? 2 : 1;
            var raak = afstanden.Where(x => x.Afstand <= grens).ToList();
            if (raak.Count > 0)
            {
                var min = raak.Min(x => x.Afstand);
                return Lijkt(raak.Where(x => x.Afstand == min).Select(x => x.Naam));
            }
            // niets: wat in de buurt komt, als voorstel aan de gebruiker
            var wijd = Math.Max(2, kaal.Length / 3);
            return new NaamTreffer
            {
                Gevraagd = gevraagd,
                Suggesties = kalen.Select(x => (x.Naam, Afstand: Afstand(kaal, x.Woorden, zelfdeLetter: false)))
                                  .Where(x => x.Afstand <= wijd).OrderBy(x => x.Afstand).ThenBy(x => x.Naam.Length)
                                  .Select(x => x.Naam).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList(),
            };
        }
        return new NaamTreffer { Gevraagd = gevraagd };
    }

    /// <summary>De kleinste bewerkingsafstand tussen de gevraagde tekst en een stuk van de naam dat aan het begin van een
    /// woord begint (even lang, of een teken korter of langer; of de rest van de naam als die korter is).</summary>
    internal static int Afstand(string gevraagd, IReadOnlyList<string> naamWoorden, bool zelfdeLetter)
    {
        var naam = string.Concat(naamWoorden);
        var beste = int.MaxValue;
        var begin = 0;
        foreach (var woord in naamWoorden)
        {
            if (!zelfdeLetter || naam[begin] == gevraagd[0])
            {
                var rest = naam.Length - begin;
                for (var lengte = Math.Max(1, gevraagd.Length - 1); lengte <= gevraagd.Length + 1; lengte++)
                {
                    var d = Levenshtein(gevraagd, naam.AsSpan(begin, Math.Min(lengte, rest)));
                    if (d < beste) beste = d;
                    if (lengte >= rest) break;
                }
            }
            begin += woord.Length;
        }
        return beste;
    }

    private static int Levenshtein(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        Span<int> vorige = stackalloc int[b.Length + 1];
        Span<int> rij = stackalloc int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) vorige[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            rij[0] = i;
            for (var j = 1; j <= b.Length; j++)
                rij[j] = Math.Min(Math.Min(rij[j - 1] + 1, vorige[j] + 1), vorige[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            var t = vorige; vorige = rij; rij = t;
        }
        return vorige[b.Length];
    }

    // ================================================================
    //  Hulp voor de opvragingen met eigen SQL
    // ================================================================

    /// <summary>"(kolom LIKE @p0 OR kolom LIKE @p1)" voor de patronen van een treffer.</summary>
    public static string Voorwaarde(string kolom, string parameter, NaamTreffer t)
        => "(" + string.Join(" OR ", t.Patronen.Select((_, i) => $"{kolom} LIKE @{parameter}{i}")) + ")";

    public static void Vul(MySqlCommand cmd, string parameter, NaamTreffer t)
    {
        for (var i = 0; i < t.Patronen.Count; i++) cmd.Parameters.AddWithValue($"@{parameter}{i}", "%" + t.Patronen[i] + "%");
    }
}
