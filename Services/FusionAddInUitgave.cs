using System.IO.Compression;

namespace EverythingApp.Services;

/// <summary>
/// De Fusion-add-in (MdeFusionTools) als download vanuit de app (wens gebruiker 6-11-2026: ook voor wie niet bij de share
/// kan, zoals een inlener). FusionTools\publish.ps1 zet elke uitgave op de share en ververst de map 'laatste' met
/// Installeer.cmd, install.ps1 en payload\MdeFusionTools; die map wordt hier als zip uitgegeven. Na uitpakken
/// dubbelklikt de gebruiker op Installeer.cmd. Map instelbaar met FusionAddIn:Map.
/// </summary>
public class FusionAddInUitgave
{
    private readonly string _map;
    private readonly ILogger<FusionAddInUitgave> _log;

    public FusionAddInUitgave(IConfiguration config, ILogger<FusionAddInUitgave> log)
    {
        _map = (config["FusionAddIn:Map"] ?? "").Trim();
        if (_map.Length == 0) _map = @"\\172.16.1.4\data\Fusion PCB\Apps\MdeFusionTools\laatste";
        _log = log;
    }

    public string Map => _map;

    private string Payload => Path.Combine(_map, "payload", "MdeFusionTools");

    /// <summary>Staat er een bruikbare uitgave (installer en add-in)?</summary>
    public bool Beschikbaar()
    {
        try { return File.Exists(Path.Combine(_map, "Installeer.cmd")) && File.Exists(Path.Combine(Payload, "MdeFusionTools.py")); }
        catch { return false; }
    }

    /// <summary>Versie uit versie.txt (bv. "1.2.4 (2026-10-15-0949)"), of leeg.</summary>
    public string Versie()
    {
        try
        {
            var pad = Path.Combine(Payload, "versie.txt");
            return File.Exists(pad) ? File.ReadAllText(pad).Trim().TrimStart('\uFEFF') : "";
        }
        catch { return ""; }
    }

    /// <summary>Kort versienummer voor de bestandsnaam: "1.2.4".</summary>
    public string KorteVersie() => Versie().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    /// <summary>De uitgave als zip, alles onder één map (MdeFusionTools-1.2.4\Installeer.cmd enz.); null als het niet lukt.</summary>
    public byte[]? Zip()
    {
        try
        {
            if (!Beschikbaar()) return null;
            return ZipVanMap(_map, "MdeFusionTools" + (KorteVersie() is { Length: > 0 } v ? "-" + v : ""));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Fusion-add-in inpakken mislukt ({Map})", _map);
            return null;
        }
    }

    /// <summary>
    /// Een map als zip, alles onder één map <paramref name="wortel"/> (ook gebruikt door <see cref="InventorAddInUitgave"/>).
    /// __pycache__ gaat niet mee.
    /// </summary>
    public static byte[] ZipVanMap(string map, string wortel)
    {
        using var geheugen = new MemoryStream();
        using (var zip = new ZipArchive(geheugen, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var bestand in Directory.EnumerateFiles(map, "*", SearchOption.AllDirectories))
            {
                var relatief = Path.GetRelativePath(map, bestand);
                if (relatief.Split(Path.DirectorySeparatorChar).Contains("__pycache__")) continue;
                zip.CreateEntryFromFile(bestand, wortel + "/" + relatief.Replace('\\', '/'), CompressionLevel.Optimal);
            }
        }
        return geheugen.ToArray();
    }
}
