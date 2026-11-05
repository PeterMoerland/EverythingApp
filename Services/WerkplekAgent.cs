using System.Text.Json;
using Microsoft.JSInterop;

namespace EverythingApp.Services;

/// <summary>
/// Is er op déze pc (de browser) een MDE Werkplek-agent? De agent draait lokaal
/// (Productie:UitleesAgent, meestal http://127.0.0.1:5055) en is alleen vanuit de browser
/// te bereiken, dus de controle loopt via JS (mdeAgent.haal → /health, zonder token).
/// Per circuit één keer gecontroleerd en onthouden; bepaalt of het menu Productie en
/// de tab Programmeren zichtbaar zijn.
/// </summary>
public class WerkplekAgent
{
    public string Url { get; }
    /// <summary>null = nog niet gecontroleerd.</summary>
    public bool? Aanwezig { get; private set; }
    public string Versie { get; private set; } = "";
    public string Hostnaam { get; private set; } = "";
    public string Reden { get; private set; } = "";
    public event Action? Gewijzigd;

    private readonly string _token;

    public WerkplekAgent(IConfiguration config)
    {
        Url = (config["Productie:UitleesAgent"] ?? "").Trim().TrimEnd('/');
        _token = (config["Productie:AgentToken"] ?? "").Trim();
    }

    /// <summary>Een willekeurige aanroep van de agent (met token), bv. /drives. Status 0 = niet bereikt (Tekst zegt waarom).</summary>
    public async Task<(int Status, string Tekst)> VraagAsync(IJSRuntime js, string pad, int seconden, string? body = null)
    {
        if (Url.Length == 0) return (0, "Productie:UitleesAgent is niet ingesteld.");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconden + 15));
            var r = await js.InvokeAsync<Antwoord>("mdeAgent.haal", cts.Token, Url + pad, seconden, _token, body);
            return (r.status, r.tekst);
        }
        catch (OperationCanceledException) { return (0, $"time-out: de agent antwoordde niet binnen {seconden} s"); }
        catch (JSException ex) { return (0, "aanroep van de agent mislukt: " + ex.Message); }
    }

    private sealed class Antwoord
    {
        public int status { get; set; }
        public string tekst { get; set; } = "";
    }

    /// <summary>De agent aanroepen (/health). Onthoudt de uitkomst; met <paramref name="opnieuw"/> nog eens proberen.</summary>
    public async Task<bool> ControleerAsync(IJSRuntime js, bool opnieuw = false)
    {
        if (Aanwezig is not null && !opnieuw) return Aanwezig.Value;
        if (Url.Length == 0)
        {
            Aanwezig = false; Reden = "Productie:UitleesAgent is niet ingesteld.";
            Gewijzigd?.Invoke();
            return false;
        }
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var r = await js.InvokeAsync<Antwoord>("mdeAgent.haal", cts.Token, Url + "/health", 3, "", null);
            Aanwezig = r.status == 200;
            Reden = Aanwezig.Value ? "" : (r.status == 0 ? r.tekst : $"agent antwoordde {r.status}");
            if (Aanwezig.Value)
            {
                try
                {
                    using var doc = JsonDocument.Parse(r.tekst);
                    Versie = doc.RootElement.TryGetProperty("versie", out var v) ? v.GetString() ?? "" : "";
                    Hostnaam = doc.RootElement.TryGetProperty("hostnaam", out var h) ? h.GetString() ?? ""
                             : doc.RootElement.TryGetProperty("pc", out var pc) ? pc.GetString() ?? "" : "";
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Aanwezig = false;
            Reden = "controle mislukt: " + ex.Message;
        }
        Gewijzigd?.Invoke();
        return Aanwezig.Value;
    }
}
