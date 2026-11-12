using System.Globalization;
using System.Text.RegularExpressions;

namespace EverythingApp.Services;

/// <summary>Een regel uit CHANGELOG.md: onderdeel (Bestellingen), soort (Nieuw/Verbeterd/Opgelost) en de tekst.</summary>
/// <param name="Sleutel">Korte vingerafdruk van de regel (dag + inhoud), om bij te houden welke regels iemand al zag.</param>
public record ChangelogRegel(string Onderdeel, string Soort, string Tekst, string Sleutel = "");

/// <summary>Alle regels van één dag.</summary>
public record ChangelogDag(DateOnly Datum, List<ChangelogRegel> Regels);

/// <summary>
/// Leest CHANGELOG.md (wens gebruiker 11-11-2026: per datum bijhouden wat er veranderd is, ook voor collega's) voor de
/// pagina Wat is er nieuw (/nieuw) en het knopje in de bovenbalk. Opbouw van het bestand: "## 11-11-2026" per dag en
/// daaronder "- **Onderdeel** — Nieuw: tekst". Het bestand wordt opnieuw gelezen als het gewijzigd is.
/// </summary>
public class Changelog
{
    private readonly string[] _paden;
    private readonly object _slot = new();
    private List<ChangelogDag> _dagen = new();
    private DateTime _gelezen = DateTime.MinValue;
    private string? _pad;

    public Changelog(IWebHostEnvironment env)
    {
        // gepubliceerd staat hij naast de dll; lokaal (dotnet run) in de projectmap
        _paden = new[] { Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md"), Path.Combine(env.ContentRootPath, "CHANGELOG.md") };
    }

    private static readonly Regex Kop = new(@"^##\s+(\d{1,2}-\d{1,2}-\d{4})\s*$", RegexOptions.Compiled);
    // "- **Bestellingen** — Nieuw: tekst" (streepje mag ook - of –; soort is optioneel)
    private static readonly Regex Regel = new(@"^-\s+\*\*(?<onderdeel>[^*]+)\*\*\s*[—–-]\s*(?:(?<soort>Nieuw|Verbeterd|Opgelost)\s*:\s*)?(?<tekst>.+)$",
                                              RegexOptions.Compiled);

    /// <summary>De dagen, nieuwste eerst; leeg als het bestand er niet is.</summary>
    public List<ChangelogDag> Dagen
    {
        get
        {
            lock (_slot)
            {
                try
                {
                    var pad = _pad ?? _paden.FirstOrDefault(File.Exists);
                    if (pad is null) return _dagen;
                    var tijd = File.GetLastWriteTimeUtc(pad);
                    if (pad == _pad && tijd == _gelezen) return _dagen;
                    _dagen = Lees(File.ReadAllLines(pad));
                    _pad = pad;
                    _gelezen = tijd;
                }
                catch { /* laat de vorige stand staan */ }
                return _dagen;
            }
        }
    }

    /// <summary>De nieuwste datum in de changelog, of null.</summary>
    public DateOnly? Nieuwste => Dagen.Count > 0 ? Dagen[0].Datum : null;

    // ---- wat is nieuw voor deze gebruiker (wens gebruiker 12-11-2026: bij elke publicatie met iets nieuws, niet eens per dag) ----

    /// <summary>Sleutel in de browseropslag: de vingerafdrukken van de regels die al gezien zijn (gescheiden door komma's).</summary>
    public const string OpslagGezien = "mde-nieuw-regels";
    /// <summary>De oude sleutel (alleen de datum van het laatste bezoek); nog gelezen voor wie de nieuwe nog niet heeft.</summary>
    public const string OpslagGezienOud = "mde-nieuw-gezien";
    private const int DagenBijhouden = 90;      // oudere regels tellen nooit als nieuw (houdt de opslag klein)

    private IEnumerable<(ChangelogDag Dag, ChangelogRegel Regel)> Recent()
    {
        var vanaf = DateOnly.FromDateTime(DateTime.Today).AddDays(-DagenBijhouden);
        return Dagen.Where(d => d.Datum >= vanaf).SelectMany(d => d.Regels.Select(r => (d, r)));
    }

    /// <summary>Wat er na het bekijken in de browseropslag hoort: alle regels van de laatste 90 dagen.</summary>
    public string AllesGezien() => Recent().Select(x => x.Regel.Sleutel).ToList() is { Count: > 0 } l ? string.Join(",", l) : "-";

    /// <summary>
    /// De regels die deze gebruiker nog niet zag. <paramref name="gezien"/> = de waarde van <see cref="OpslagGezien"/>;
    /// ontbreekt die, dan telt de oude datum (<paramref name="gezienOud"/>): alles van latere dagen is nieuw. Nooit op de
    /// pagina geweest: <c>EersteKeer</c> (het knopje toont dan, maar de pagina markeert niets).
    /// </summary>
    public (HashSet<string> Nieuw, bool EersteKeer) NieuwVoor(string? gezien, string? gezienOud)
    {
        if (!string.IsNullOrWhiteSpace(gezien))
        {
            var al = gezien.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
            return (Recent().Where(x => !al.Contains(x.Regel.Sleutel)).Select(x => x.Regel.Sleutel).ToHashSet(), false);
        }
        if (DateOnly.TryParse(gezienOud, CultureInfo.InvariantCulture, out var datum))
            return (Recent().Where(x => x.Dag.Datum > datum).Select(x => x.Regel.Sleutel).ToHashSet(), false);
        return (new HashSet<string>(), true);
    }

    private static string Vingerafdruk(DateOnly dag, string onderdeel, string soort, string tekst)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes($"{dag:yyyy-MM-dd}|{onderdeel}|{soort}|{tekst}"));
        return Convert.ToHexString(bytes, 0, 5).ToLowerInvariant();     // 10 tekens is ruim genoeg
    }

    public static List<ChangelogDag> Lees(IEnumerable<string> regels)
    {
        var dagen = new List<ChangelogDag>();
        ChangelogDag? dag = null;
        foreach (var ruw in regels)
        {
            var r = ruw.Trim();
            var k = Kop.Match(r);
            if (k.Success)
            {
                dag = DateOnly.TryParseExact(k.Groups[1].Value, "d-M-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? new ChangelogDag(d, new()) : null;
                if (dag is not null) dagen.Add(dag);
                continue;
            }
            if (dag is null) continue;
            var m = Regel.Match(r);
            if (m.Success)
            {
                string onderdeel = m.Groups["onderdeel"].Value.Trim(), soort = m.Groups["soort"].Value, tekst = m.Groups["tekst"].Value.Trim();
                dag.Regels.Add(new ChangelogRegel(onderdeel, soort, tekst, Vingerafdruk(dag.Datum, onderdeel, soort, tekst)));
            }
        }
        return dagen.Where(d => d.Regels.Count > 0).OrderByDescending(d => d.Datum).ToList();
    }
}
