using System.Globalization;
using System.Text.RegularExpressions;

namespace EverythingApp.Services;

/// <summary>Een regel uit CHANGELOG.md: onderdeel (Bestellingen), soort (Nieuw/Verbeterd/Opgelost) en de tekst.</summary>
public record ChangelogRegel(string Onderdeel, string Soort, string Tekst);

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
                dag.Regels.Add(new ChangelogRegel(m.Groups["onderdeel"].Value.Trim(), m.Groups["soort"].Value, m.Groups["tekst"].Value.Trim()));
        }
        return dagen.Where(d => d.Regels.Count > 0).OrderByDescending(d => d.Datum).ToList();
    }
}
