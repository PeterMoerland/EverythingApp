using System.IO.Compression;
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
    private (DateTime Tijd, Version? Versie, string? Pad) _cache;

    public AgentUitgave(IConfiguration config, ILogger<AgentUitgave> log)
    {
        _map = (config["Productie:AgentMap"] ?? "").Trim();
        if (_map.Length == 0) _map = @"\\172.16.1.4\data\software\MDE Apps\MDE Agent\uitvoer";
        _log = log;
    }

    /// <summary>De map waar de installer staat (voor de melding aan de gebruiker: één niveau hoger staat installeer.bat).</summary>
    public string Map => _map;

    /// <summary>De map met installeer.bat, LEESMIJ.txt en mde-root.crt (één niveau boven <see cref="Map"/>).</summary>
    public string InstallMap => Path.GetDirectoryName(_map.TrimEnd('\\', '/')) ?? _map;

    private static readonly string[] Losse = { "installeer.bat", "LEESMIJ.txt", "mde-root.crt" };
    private readonly SemaphoreSlim _zipSlot = new(1, 1);

    /// <summary>
    /// De installatie als zip voor Tools ▸ MDE Agent (wens gebruiker 10-11-2026): MdeAgent-&lt;versie&gt;\installeer.bat,
    /// LEESMIJ.txt, mde-root.crt en uitvoer\MdeAgent-setup-&lt;versie&gt;.exe, zodat installeer.bat na uitpakken precies zo
    /// werkt als vanaf de share. Het token zit er bewust niet in (installeer.bat vraagt het op een nieuwe pc).
    /// Eén keer ingepakt in de tijdelijke map en hergebruikt tot er op de share iets nieuwers staat; null als het niet lukt.
    /// </summary>
    public async Task<string?> ZipAsync()
    {
        var (_, versie, installer) = Zoek();
        if (versie is null || installer is null) return null;
        var bat = Path.Combine(InstallMap, "installeer.bat");
        try { if (!File.Exists(installer) || !File.Exists(bat)) return null; }
        catch { return null; }

        var map = Path.Combine(Path.GetTempPath(), "mde-agent-download");
        var doel = Path.Combine(map, $"MdeAgent-{versie}.zip");
        await _zipSlot.WaitAsync();
        try
        {
            var bronnen = Losse.Select(n => Path.Combine(InstallMap, n)).Where(File.Exists).Append(installer).ToList();
            var nieuwste = bronnen.Max(File.GetLastWriteTimeUtc);
            if (File.Exists(doel) && File.GetLastWriteTimeUtc(doel) >= nieuwste) return doel;

            Directory.CreateDirectory(map);
            var tijdelijk = doel + ".tmp";
            var wortel = $"MdeAgent-{versie}/";
            await Task.Run(() =>
            {
                using var fs = File.Create(tijdelijk);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
                foreach (var n in Losse)
                {
                    var pad = Path.Combine(InstallMap, n);
                    if (File.Exists(pad)) zip.CreateEntryFromFile(pad, wortel + n, CompressionLevel.Optimal);
                }
                // de installer is al ingepakt (Inno Setup): niet nog eens comprimeren
                zip.CreateEntryFromFile(installer, wortel + "uitvoer/" + Path.GetFileName(installer), CompressionLevel.NoCompression);
            });
            File.Move(tijdelijk, doel, overwrite: true);
            _log.LogInformation("MDE Agent {Versie} ingepakt voor download: {Doel}", versie, doel);
            return doel;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "MDE Agent inpakken mislukt ({Map})", InstallMap);
            return null;
        }
        finally { _zipSlot.Release(); }
    }

    public Version? Nieuwste() => Zoek().Versie;

    /// <summary>Volledig pad van de nieuwste installer (voor de downloadknop in de melding, /agent/installer), of null.</summary>
    public string? NieuwstePad() => Zoek().Pad;

    private (DateTime Tijd, Version? Versie, string? Pad) Zoek()
    {
        lock (_slot)
        {
            if (_cache.Tijd > DateTime.UtcNow.AddMinutes(-10)) return _cache;
        }
        Version? hoogste = null;
        string? hoogstePad = null;
        try
        {
            foreach (var pad in Directory.EnumerateFiles(_map, "MdeAgent-setup-*.exe", SearchOption.TopDirectoryOnly))
            {
                var m = Naam.Match(Path.GetFileName(pad));
                if (m.Success && Version.TryParse(m.Groups[1].Value, out var v) && (hoogste is null || v > hoogste))
                {
                    hoogste = v;
                    hoogstePad = pad;
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogInformation("Nieuwste agentversie niet te lezen uit {Map}: {Fout}", _map, ex.Message);
        }
        lock (_slot) _cache = (DateTime.UtcNow, hoogste, hoogstePad);
        return _cache;
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
