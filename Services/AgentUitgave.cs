using System.Text.RegularExpressions;

namespace EverythingApp.Services;

/// <summary>
/// De nieuwste uitgegeven versie van de MDE Werkplek-agent: de hoogste MdeAgent-setup-&lt;versie&gt;.exe die
/// publiceer-z.bat op de share zet (Productie:AgentMap, standaard \\172.16.1.4\data\software\MDE Apps\MDE Agent\uitvoer;
/// de submap oud telt niet). Geen versie om bij te houden: wat op de share staat, is de nieuwste.
/// Tien minuten onthouden; is de share niet bereikbaar, dan null (en geen melding in de app).
/// </summary>
public class AgentUitgave
{
    private static readonly Regex Naam = new(@"^MdeAgent-setup-(\d+(?:\.\d+){1,3})\.exe$", RegexOptions.IgnoreCase);
    private readonly string _map;
    private readonly ILogger<AgentUitgave> _log;
    private readonly object _slot = new();
    private (DateTime Tijd, Version? Versie) _cache;

    public AgentUitgave(IConfiguration config, ILogger<AgentUitgave> log)
    {
        _map = (config["Productie:AgentMap"] ?? "").Trim();
        if (_map.Length == 0) _map = @"\\172.16.1.4\data\software\MDE Apps\MDE Agent\uitvoer";
        _log = log;
    }

    /// <summary>De map waar de installer staat (voor de melding aan de gebruiker: één niveau hoger staat installeer.bat).</summary>
    public string Map => _map;

    public Version? Nieuwste()
    {
        lock (_slot)
        {
            if (_cache.Tijd > DateTime.UtcNow.AddMinutes(-10)) return _cache.Versie;
        }
        Version? hoogste = null;
        try
        {
            foreach (var pad in Directory.EnumerateFiles(_map, "MdeAgent-setup-*.exe", SearchOption.TopDirectoryOnly))
            {
                var m = Naam.Match(Path.GetFileName(pad));
                if (m.Success && Version.TryParse(m.Groups[1].Value, out var v) && (hoogste is null || v > hoogste)) hoogste = v;
            }
        }
        catch (Exception ex)
        {
            _log.LogInformation("Nieuwste agentversie niet te lezen uit {Map}: {Fout}", _map, ex.Message);
        }
        lock (_slot) _cache = (DateTime.UtcNow, hoogste);
        return hoogste;
    }

    /// <summary>True als <paramref name="draaiend"/> (van /health) ouder is dan wat er op de share staat.</summary>
    public bool IsVerouderd(string draaiend, out Version? nieuwste)
    {
        nieuwste = Nieuwste();
        return nieuwste is not null && Version.TryParse(draaiend.Trim().TrimStart('v', 'V'), out var v) && Normaal(v) < Normaal(nieuwste);
    }

    // 1.20 en 1.20.0 gelijk behandelen (Version zet ontbrekende delen op -1)
    private static Version Normaal(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
}
