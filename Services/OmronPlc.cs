using System.Collections.Concurrent;
using libplctag;
using libplctag.DataTypes;

namespace EverythingApp.Services;

/// <summary>
/// Praat over EtherNet/IP met de Omron NJ van het gebouw.
///
/// De variabelen in de PLC staan op 'Publish Only' (netwerkpublicatie), waardoor
/// ze met hun naam op te vragen zijn — geen adressen dus, gewoon 'Up1' of
/// 'HMI_Light_Office1'.
///
/// Instellen in appsettings.json:
///   "Plc": { "Adres": "172.16.3.3" }
/// Blijft dat leeg, dan draait de app op DemoPlc en gebeurt er niets in het gebouw.
/// </summary>
public class OmronPlc : IPlc
{
    private readonly string _adres;
    private readonly int _timeoutSeconden;
    private readonly int _pulsMs;
    private readonly ILogger<OmronPlc> _log;

    // Eén tag-object per variabele hergebruiken: dat scheelt verbindingen opbouwen.
    private readonly ConcurrentDictionary<string, Tag<BoolPlcMapper, bool>> _tags = new(StringComparer.OrdinalIgnoreCase);

    public OmronPlc(IConfiguration config, ILogger<OmronPlc> log)
    {
        _adres = config["Plc:Adres"] ?? "";
        _timeoutSeconden = int.TryParse(config["Plc:TimeoutSeconden"], out var t) ? Math.Clamp(t, 1, 30) : 5;
        _pulsMs = int.TryParse(config["Plc:PulsMs"], out var p) ? Math.Clamp(p, 50, 2000) : 300;
        _log = log;
    }

    public bool Echt => true;

    public string Beschrijf() => $"Verbonden met de gebouw-PLC op {_adres} (Omron NJ, EtherNet/IP).";

    private Tag<BoolPlcMapper, bool> Tag(string naam)
        => _tags.GetOrAdd(naam, n => new Tag<BoolPlcMapper, bool>
        {
            Name = n,
            Gateway = _adres,
            // Voor de NJ/NX-serie loopt de route via poort 18 naar hetzelfde adres.
            Path = "18," + _adres,
            PlcType = PlcType.Omron,
            Protocol = Protocol.ab_eip,
            Timeout = TimeSpan.FromSeconds(_timeoutSeconden),
        });

    public async Task<bool> LeesAsync(string tag)
    {
        try
        {
            var t = Tag(tag);
            await t.ReadAsync();
            return t.Value;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Lezen van {Tag} mislukte", tag);
            throw new InvalidOperationException(Uitleg(tag, ex));
        }
    }

    public async Task<Dictionary<string, bool>> LeesAsync(IEnumerable<string> tags)
    {
        var uit = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Naast elkaar lezen; anders duurt een scherm met twintig lampen te lang.
        var lijst = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var taken = lijst.Select(async naam =>
        {
            try
            {
                var t = Tag(naam);
                await t.ReadAsync();
                return (naam, waarde: t.Value, gelukt: true);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Lezen van {Tag} mislukte", naam);
                return (naam, waarde: false, gelukt: false);
            }
        });

        var uitkomsten = await Task.WhenAll(taken);
        foreach (var (naam, waarde, _) in uitkomsten) uit[naam] = waarde;

        // Lukt er helemaal niets, dan is de PLC niet bereikbaar en moet dat blijken
        if (uitkomsten.Length > 0 && uitkomsten.All(u => !u.gelukt))
            throw new InvalidOperationException(
                $"Geen contact met de PLC op {_adres}. Staat hij aan en is hij bereikbaar vanaf de server?");

        return uit;
    }

    public async Task SchrijfAsync(string tag, bool waarde)
    {
        try
        {
            var t = Tag(tag);
            t.Value = waarde;
            await t.WriteAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Schrijven van {Tag} mislukte", tag);
            throw new InvalidOperationException(Uitleg(tag, ex));
        }
    }

    /// <summary>
    /// Even aan en weer uit, net als het indrukken en loslaten van een knop op het
    /// HMI-paneel. De PLC reageert op de opgaande flank.
    ///
    /// De bit gaat altijd weer laag, ook als er tussendoor iets misgaat: bleef hij
    /// hoog, dan ziet de PLC de volgende druk niet als nieuwe flank.
    /// </summary>
    public async Task PulsAsync(string tag, int milliseconden = 300)
    {
        var duur = milliseconden == 300 ? _pulsMs : milliseconden;

        await SchrijfAsync(tag, true);
        try
        {
            await Task.Delay(Math.Clamp(duur, 50, 2000));
        }
        finally
        {
            try { await SchrijfAsync(tag, false); }
            catch (Exception ex) { _log.LogError(ex, "Bit {Tag} kon niet laag gemaakt worden", tag); }
        }
    }

    private string Uitleg(string tag, Exception ex)
    {
        var melding = ex.Message ?? "";

        if (melding.Contains("ErrorNotFound", StringComparison.OrdinalIgnoreCase)
            || melding.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return $"De PLC kent de variabele '{tag}' niet. Staat hij op 'Publish Only' " +
                   "in de variabelentabel van Sysmac Studio?";

        if (melding.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return $"Geen antwoord van de PLC op {_adres} binnen {_timeoutSeconden} seconden. " +
                   "Controleer of de server bij dat netwerk kan.";

        return $"Fout bij '{tag}': {melding}";
    }
}
