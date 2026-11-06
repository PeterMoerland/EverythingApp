using System.Text.RegularExpressions;

namespace EverythingApp.Services;

/// <summary>
/// De Inventor-add-in (MDE InventorTools) als download vanuit de app (wens gebruiker 6-11-2026, net als de Fusion-add-in).
/// InventorTools\publish.ps1 zet elke uitgave in een map met datum en tijd (2026-11-04-0726) en kopieert die naar 'huidig',
/// met Installeer.cmd, install.ps1 en payload\InventorTools.dll enz.; die map wordt hier als zip uitgegeven. Na uitpakken
/// dubbelklikt de gebruiker op Installeer.cmd. Map instelbaar met InventorAddIn:Map.
/// </summary>
public class InventorAddInUitgave
{
    private readonly string _map;
    private readonly ILogger<InventorAddInUitgave> _log;

    public InventorAddInUitgave(IConfiguration config, ILogger<InventorAddInUitgave> log)
    {
        _map = (config["InventorAddIn:Map"] ?? "").Trim();
        if (_map.Length == 0) _map = @"\\172.16.1.4\Data\INVENTOR\Apps\InventorTools\huidig";
        _log = log;
    }

    public string Map => _map;

    private string Payload => Path.Combine(_map, "payload");

    /// <summary>Staat er een bruikbare uitgave (installer en add-in)?</summary>
    public bool Beschikbaar()
    {
        try { return File.Exists(Path.Combine(_map, "Installeer.cmd")) && File.Exists(Path.Combine(Payload, "InventorTools.dll")); }
        catch { return false; }
    }

    private static readonly Regex Stempel = new(@"^\d{4}-\d{2}-\d{2}-\d{4}$");

    /// <summary>
    /// Naam van de uitgave (bv. "2026-11-04-0726"): uit payload\uitgave.txt, anders de nieuwste map met een datum naast
    /// 'huidig' (huidig is een kopie van de laatste). Leeg als het niet te bepalen is.
    /// </summary>
    public string Uitgave()
    {
        try
        {
            var pad = Path.Combine(Payload, "uitgave.txt");
            if (File.Exists(pad) && File.ReadAllText(pad).Trim().TrimStart('\uFEFF') is { Length: > 0 } tekst) return tekst;
            var boven = Path.GetDirectoryName(_map.TrimEnd('\\', '/'));
            if (boven is null || !Directory.Exists(boven)) return "";
            return Directory.EnumerateDirectories(boven).Select(Path.GetFileName).OfType<string>()
                .Where(n => Stempel.IsMatch(n)).OrderDescending().FirstOrDefault() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>De uitgave als zip, alles onder één map (MdeInventorTools-2026-11-04-0726\Installeer.cmd enz.); null als het niet lukt.</summary>
    public byte[]? Zip()
    {
        try
        {
            if (!Beschikbaar()) return null;
            return FusionAddInUitgave.ZipVanMap(_map, "MdeInventorTools" + (Uitgave() is { Length: > 0 } u ? "-" + u : ""));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Inventor-add-in inpakken mislukt ({Map})", _map);
            return null;
        }
    }
}
