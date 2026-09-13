using System.Collections.Concurrent;

namespace EverythingApp.Services;

/// <summary>
/// De verbinding met de gebouw-PLC. Bewust klein gehouden: lezen, schrijven en
/// een puls. De schermen kennen alleen namen uit Gebouw.cs, nooit adressen, zodat
/// een fout in een pagina nooit ergens anders in het geheugen kan schrijven.
///
/// Er zijn twee uitvoeringen: een demoversie die alleen onthoudt wat je aanklikt,
/// en straks een echte over EtherNet/IP. De schermen merken het verschil niet.
/// </summary>
public interface IPlc
{
    /// <summary>Praat de app echt met een PLC, of draait dit op de demoversie?</summary>
    bool Echt { get; }

    /// <summary>Korte beschrijving van de verbinding, voor onderaan het scherm.</summary>
    string Beschrijf();

    Task<bool> LeesAsync(string tag);
    Task<Dictionary<string, bool>> LeesAsync(IEnumerable<string> tags);
    Task SchrijfAsync(string tag, bool waarde);

    /// <summary>Even aan en weer uit — voor knoppen als Omhoog, Stop en Alles uit.</summary>
    Task PulsAsync(string tag, int milliseconden = 300);
}

/// <summary>
/// Demoversie: onthoudt de standen in het geheugen. Zo is het scherm te bouwen en
/// te bekijken voordat de PLC eraan hangt, en blijft de app bruikbaar als de PLC
/// even niet bereikbaar is.
/// </summary>
public class DemoPlc : IPlc
{
    private readonly ConcurrentDictionary<string, bool> _standen = new(StringComparer.OrdinalIgnoreCase);

    public bool Echt => false;

    public string Beschrijf()
        => "Demostand: de knoppen werken, maar er is nog geen verbinding met de PLC. " +
           "Zet Plc:Adres in appsettings.json om dat te veranderen.";

    public Task<bool> LeesAsync(string tag)
        => Task.FromResult(_standen.TryGetValue(tag, out var aan) && aan);

    public Task<Dictionary<string, bool>> LeesAsync(IEnumerable<string> tags)
    {
        var uit = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
            uit[tag] = _standen.TryGetValue(tag, out var aan) && aan;
        return Task.FromResult(uit);
    }

    public Task SchrijfAsync(string tag, bool waarde)
    {
        _standen[tag] = waarde;
        return Task.CompletedTask;
    }

    /// <summary>
    /// In de demo doet een puls wat de PLC zou doen: de bijbehorende _Status-tag
    /// omklappen, zodat het lampje op het scherm meebeweegt. 'Alles uit' zet alle
    /// verlichtingsstatussen uit.
    /// </summary>
    public async Task PulsAsync(string tag, int milliseconden = 300)
    {
        await SchrijfAsync(tag, true);
        await Task.Delay(Math.Clamp(milliseconden, 50, 2000));
        await SchrijfAsync(tag, false);

        if (tag.EndsWith("All_Off", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var sleutel in _standen.Keys.Where(k => k.EndsWith("_Status", StringComparison.OrdinalIgnoreCase)))
                _standen[sleutel] = false;
            return;
        }

        var status = tag + "_Status";
        _standen[status] = !(_standen.TryGetValue(status, out var aan) && aan);
    }
}
