using System.Text.Json;
using System.Text.RegularExpressions;

namespace EverythingApp.Services;

/// <summary>
/// Iconen voor de snelbalk: de Lucide-set (ISC, v0.460.0) uit wwwroot/iconen, één keer ingelezen. Het SVG-bestand
/// bevat alleen vormen (path, circle, rect, line, polyline, polygon, ellipse); de inhoud van een symbool wordt
/// daarom rechtstreeks als markup getoond. Zoeken op naam en Engelse trefwoorden, plus een rijtje Nederlandse woorden.
/// </summary>
public class SnelIconen
{
    private readonly Dictionary<string, string> _svg = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _tags = new(StringComparer.Ordinal);

    /// <summary>Nederlandse zoekwoorden → Engelse naam of trefwoord van Lucide.</summary>
    private static readonly Dictionary<string, string[]> NL = new(StringComparer.OrdinalIgnoreCase)
    {
        ["doos"] = new[] { "box", "package" }, ["pakket"] = new[] { "package" }, ["robot"] = new[] { "bot" },
        ["kalender"] = new[] { "calendar" }, ["agenda"] = new[] { "calendar" }, ["map"] = new[] { "folder" },
        ["mensen"] = new[] { "users" }, ["persoon"] = new[] { "user" }, ["werknemer"] = new[] { "id-card", "user" },
        ["gereedschap"] = new[] { "wrench", "hammer" }, ["sleutel"] = new[] { "key", "wrench" }, ["bliksem"] = new[] { "zap" },
        ["stroom"] = new[] { "zap", "plug" }, ["fabriek"] = new[] { "factory" }, ["productie"] = new[] { "factory", "zap" },
        ["vrachtwagen"] = new[] { "truck" }, ["levering"] = new[] { "truck" }, ["lijst"] = new[] { "list" },
        ["geld"] = new[] { "banknote", "euro" }, ["bank"] = new[] { "landmark" }, ["factuur"] = new[] { "receipt" },
        ["gebouw"] = new[] { "building" }, ["huis"] = new[] { "house" }, ["lamp"] = new[] { "lightbulb" },
        ["licht"] = new[] { "lightbulb" }, ["instellingen"] = new[] { "settings" }, ["winkelwagen"] = new[] { "shopping-cart" },
        ["bestelling"] = new[] { "shopping-cart" }, ["klok"] = new[] { "clock" }, ["uren"] = new[] { "clock", "timer" },
        ["tijd"] = new[] { "clock", "timer" }, ["post"] = new[] { "mail" }, ["chip"] = new[] { "cpu", "microchip" },
        ["print"] = new[] { "cpu", "circuit-board" }, ["printplaat"] = new[] { "circuit-board" }, ["tandwiel"] = new[] { "cog" },
        ["motor"] = new[] { "cog", "rotate-cw" }, ["grafiek"] = new[] { "chart" }, ["test"] = new[] { "flask-conical", "test-tube" },
        ["magazijn"] = new[] { "warehouse" }, ["voorraad"] = new[] { "archive", "warehouse" }, ["kaart"] = new[] { "map" },
        ["zoeken"] = new[] { "search" }, ["ster"] = new[] { "star" }, ["hart"] = new[] { "heart" }, ["telefoon"] = new[] { "phone" },
        ["foto"] = new[] { "image", "camera" }, ["plaatje"] = new[] { "image" }, ["document"] = new[] { "file-text" },
        ["bestand"] = new[] { "file" }, ["kabel"] = new[] { "cable" }, ["stekker"] = new[] { "plug" }, ["meten"] = new[] { "ruler" },
        ["liniaal"] = new[] { "ruler" }, ["weegschaal"] = new[] { "scale" }, ["bord"] = new[] { "kanban" }, ["taak"] = new[] { "list-todo" },
        ["vinkje"] = new[] { "check" }, ["waarschuwing"] = new[] { "triangle-alert" }, ["zon"] = new[] { "sun" }, ["wolk"] = new[] { "cloud" },
        ["computer"] = new[] { "monitor", "computer" }, ["netwerk"] = new[] { "network" }, ["project"] = new[] { "folder-kanban", "folder" },
        ["klant"] = new[] { "handshake", "building-2" }, ["leverancier"] = new[] { "truck" }, ["lucht"] = new[] { "wind" },
        ["vacuum"] = new[] { "wind" }, ["water"] = new[] { "droplet" }, ["druk"] = new[] { "gauge" }, ["snelheid"] = new[] { "gauge" },
        ["muziek"] = new[] { "music", "volume-2" }, ["geluid"] = new[] { "volume-2" }, ["schroef"] = new[] { "wrench" },
        ["auto"] = new[] { "car" }, ["slot"] = new[] { "lock" }, ["beveiliging"] = new[] { "shield" }, ["camera"] = new[] { "camera" },
        ["tekening"] = new[] { "pencil-ruler", "drafting-compass" }, ["3d"] = new[] { "box", "cuboid" }, ["helpen"] = new[] { "life-buoy" },
    };

    /// <summary>Wat het kiesvenster toont zonder zoekterm.</summary>
    private static readonly string[] Voorgesteld =
    {
        "box", "package", "archive", "warehouse", "factory", "truck", "shopping-cart", "receipt", "banknote", "euro", "landmark",
        "folder", "folder-kanban", "kanban", "list-todo", "clipboard-list", "calendar", "clock", "timer", "mail", "users", "user",
        "id-card", "handshake", "building", "house", "lightbulb", "sun", "volume-2", "zap", "plug", "cpu", "circuit-board", "bot",
        "cog", "wrench", "hammer", "cable", "gauge", "flask-conical", "thermometer", "ruler", "printer", "monitor", "server",
        "database", "network", "chart-line", "chart-column", "file-text", "image", "camera", "star", "heart", "flag", "rocket",
        "shield", "lock", "settings", "life-buoy",
    };

    public SnelIconen(IWebHostEnvironment env)
    {
        try
        {
            var sprite = File.ReadAllText(Path.Combine(env.WebRootPath, "iconen", "lucide-sprite.svg"));
            foreach (Match m in Regex.Matches(sprite, "<symbol id=\"([a-z0-9-]+)\">(.*?)</symbol>", RegexOptions.Singleline))
                _svg[m.Groups[1].Value] = Regex.Replace(m.Groups[2].Value, @"\s+", " ").Trim();
            using var tags = JsonDocument.Parse(File.ReadAllText(Path.Combine(env.WebRootPath, "iconen", "lucide-tags.json")));
            foreach (var p in tags.RootElement.EnumerateObject())
                _tags[p.Name] = p.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        }
        catch { /* zonder iconen werkt de snelbalk nog met eigen plaatjes en het standaardicoon */ }
    }

    public bool Bestaat(string? naam) => naam is not null && _svg.ContainsKey(naam);

    /// <summary>Het hele &lt;svg&gt;-element voor een icoon, of null.</summary>
    public string? Svg(string? naam)
        => naam is not null && _svg.TryGetValue(naam, out var binnen)
            ? "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" " +
              "stroke-linejoin=\"round\" aria-hidden=\"true\">" + binnen + "</svg>"
            : null;

    /// <summary>Iconen die bij de zoekterm passen (naam, Engels trefwoord of Nederlands woord); leeg = de voorgestelde.</summary>
    public List<string> Zoek(string? zoek, int max = 72)
    {
        var q = (zoek ?? "").Trim().ToLowerInvariant();
        if (q.Length == 0) return Voorgesteld.Where(_svg.ContainsKey).Take(max).ToList();
        var woorden = new List<string> { q };
        foreach (var (nl, en) in NL)
            if ((q.Length >= 3 && nl.StartsWith(q, StringComparison.OrdinalIgnoreCase)) || q.StartsWith(nl, StringComparison.OrdinalIgnoreCase)) woorden.AddRange(en);
        int Score(string naam)
        {
            var best = 0;
            foreach (var w in woorden)
            {
                if (naam == w) best = Math.Max(best, 5);
                else if (naam.StartsWith(w)) best = Math.Max(best, 4);
                else if (naam.Contains(w)) best = Math.Max(best, 3);
                else if (_tags.TryGetValue(naam, out var t) && t.Any(x => x.Contains(w, StringComparison.OrdinalIgnoreCase))) best = Math.Max(best, 2);
            }
            return best;
        }
        return _svg.Keys.Select(n => (n, s: Score(n))).Where(x => x.s > 0)
            .OrderByDescending(x => x.s).ThenBy(x => x.n.Length).ThenBy(x => x.n).Select(x => x.n).Take(max).ToList();
    }

    /// <summary>De naam van het menu-item met precies dit adres (of anders van de module), voor een pagina zonder eigen titel.</summary>
    public static string? MenuNaam(string url)
    {
        var pad = "/" + url.TrimStart('/').Split('?', '#')[0].TrimEnd('/');
        string? Zoek(IEnumerable<MenuModule> modules)
        {
            foreach (var m in modules)
            {
                foreach (var i in m.Groepen.SelectMany(g => g.Items).Where(i => i.Klaar))
                    if ("/" + i.Url.TrimStart('/').TrimEnd('/') == pad) return i.Label;
                if (m.Url is { Length: > 0 } u && "/" + u.TrimStart('/').TrimEnd('/') == pad) return m.Naam;
                if (Zoek(m.Onder) is { } o) return o;
            }
            return null;
        }
        return Zoek(Menu.Modules);
    }

    /// <summary>Menu-iconen die geen Lucide-naam zijn, vertaald.</summary>
    private static readonly Dictionary<string, string> MenuNaarLucide = new()
    {
        ["id-badge"] = "id-card", ["cash"] = "banknote", ["bulb"] = "lightbulb", ["volume"] = "volume-2",
    };

    /// <summary>Het icoon van de module waar dit adres bij hoort (Menu.cs), anders een algemeen icoon.</summary>
    public string AutoIcoon(string url)
    {
        var pad = "/" + url.TrimStart('/').Split('?', '#')[0];
        string? gevonden = null;
        var langste = -1;
        void Kijk(MenuModule m)
        {
            var adressen = m.Groepen.SelectMany(g => g.Items).Where(i => i.Klaar).Select(i => i.Url)
                .Append(m.Url ?? "").Append("/module/" + m.Sleutel).Where(a => a.Length > 0);
            foreach (var a in adressen)
            {
                var b = "/" + a.TrimStart('/');
                var past = b == "/" ? pad == "/" : pad == b || pad.StartsWith(b + "/");
                if (past && b.Length > langste) { langste = b.Length; gevonden = m.Icoon; }
            }
            foreach (var o in m.Onder) Kijk(o);
        }
        foreach (var m in Menu.Modules) Kijk(m);
        if (pad == "/") gevonden = "clock";                           // Ureningave
        else if (pad.StartsWith("/mail")) gevonden = "mail";
        else if (pad.StartsWith("/agenda")) gevonden = "calendar";
        if (gevonden is not null && MenuNaarLucide.TryGetValue(gevonden, out var vertaald)) gevonden = vertaald;
        return Bestaat(gevonden) ? gevonden! : "link";
    }
}
