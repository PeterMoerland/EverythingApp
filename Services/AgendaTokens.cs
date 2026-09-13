using System.Collections.Concurrent;
using System.Text.Json;

namespace EverythingApp.Services;

/// <summary>
/// Bewaart de Google-tokens per gebruiker, zodat de app namens die persoon in de
/// agenda kan lezen en schrijven.
///
/// Waarom hier en niet in de cookie: bij Blazor Server loopt alles via een
/// websocket, en dan is er geen HttpContext meer om de tokens uit te halen. Ze
/// worden daarom bij het inloggen opgevangen en hier onthouden.
///
/// Let op: dit zit in het geheugen. Na een herstart van de app moet iemand één
/// keer opnieuw inloggen voordat de agenda weer werkt. Wil je dat niet, dan
/// kunnen de refresh-tokens in de database — dat is een kleine uitbreiding.
/// </summary>
public class AgendaTokens
{
    private record TokenSet(string Access, string? Refresh, DateTimeOffset Verloopt);

    private readonly ConcurrentDictionary<string, TokenSet> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<AgendaTokens> _log;

    public AgendaTokens(IHttpClientFactory http, IConfiguration config, ILogger<AgendaTokens> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    /// <summary>Bij het inloggen opslaan.</summary>
    public void Bewaar(string email, string? access, string? refresh, int? geldigSeconden)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(access)) return;

        // Bestaand refresh-token bewaren: Google stuurt die alleen de eerste keer mee.
        string? bestaandRefresh = null;
        if (_tokens.TryGetValue(email, out var oud)) bestaandRefresh = oud.Refresh;

        _tokens[email] = new TokenSet(
            access!,
            string.IsNullOrWhiteSpace(refresh) ? bestaandRefresh : refresh,
            DateTimeOffset.UtcNow.AddSeconds((geldigSeconden ?? 3600) - 60));
    }

    public bool HeeftToegang(string? email) =>
        !string.IsNullOrWhiteSpace(email) && _tokens.ContainsKey(email!);

    /// <summary>Korte beschrijving van wat er bekend is — voor diagnose op het scherm.</summary>
    public string Beschrijf(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "geen e-mailadres bekend";
        if (!_tokens.TryGetValue(email!, out var set)) return "geen token bekend (opnieuw inloggen)";

        var over = set.Verloopt - DateTimeOffset.UtcNow;
        var geldig = over > TimeSpan.Zero
            ? $"nog {(int)over.TotalMinutes} min geldig"
            : "verlopen";

        return $"token {set.Access.Length} tekens, {geldig}, " +
               (string.IsNullOrWhiteSpace(set.Refresh) ? "géén refresh-token" : "refresh-token aanwezig");
    }

    /// <summary>Vergeet het token, zodat de volgende inlog opnieuw begint.</summary>
    public void Vergeet(string? email)
    {
        if (!string.IsNullOrWhiteSpace(email)) _tokens.TryRemove(email!, out _);
    }

    /// <summary>Geldig access-token, indien nodig vernieuwd. Null als er niets bekend is.</summary>
    public async Task<string?> TokenAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        if (!_tokens.TryGetValue(email!, out var set)) return null;
        if (DateTimeOffset.UtcNow < set.Verloopt) return set.Access;

        // verlopen: vernieuwen met het refresh-token
        if (string.IsNullOrWhiteSpace(set.Refresh)) return null;

        try
        {
            var client = _http.CreateClient();
            var antwoord = await client.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _config["Google:ClientId"] ?? "",
                    ["client_secret"] = _config["Google:ClientSecret"] ?? "",
                    ["refresh_token"] = set.Refresh!,
                    ["grant_type"] = "refresh_token",
                }));

            if (!antwoord.IsSuccessStatusCode)
            {
                _log.LogWarning("Vernieuwen van het Google-token mislukte: {Status}", antwoord.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync());
            var nieuw = doc.RootElement.GetProperty("access_token").GetString();
            var geldig = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

            if (string.IsNullOrWhiteSpace(nieuw)) return null;
            _tokens[email!] = set with { Access = nieuw!, Verloopt = DateTimeOffset.UtcNow.AddSeconds(geldig - 60) };
            return nieuw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Vernieuwen van het Google-token mislukte");
            return null;
        }
    }
}
