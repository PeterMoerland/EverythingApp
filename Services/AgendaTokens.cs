using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace EverythingApp.Services;

/// <summary>
/// Bewaart de Google-tokens per gebruiker, zodat de app namens die persoon in de
/// agenda en de mail kan lezen en schrijven.
///
/// Waarom hier en niet in de cookie: bij Blazor Server loopt alles via een
/// websocket, en dan is er geen HttpContext meer om de tokens uit te halen. Ze
/// worden daarom bij het inloggen opgevangen en hier onthouden.
///
/// Het access-token zit alleen in het geheugen (een uur geldig). Het refresh-token wordt sinds 11-11-2026 ook
/// versleuteld weggeschreven (C:\ProgramData\MDE\google-tokens.json, versleuteld met de Data Protection-sleutels van
/// de app): na een update of herstart haalt de app daarmee zelf een nieuw access-token, in plaats van dat iedereen
/// opnieuw moet inloggen en een half verstuurde mail kwijt is (melding gebruiker).
/// </summary>
public class AgendaTokens
{
    private record TokenSet(string Access, string? Refresh, DateTimeOffset Verloopt);

    private readonly ConcurrentDictionary<string, TokenSet> _tokens = new(StringComparer.OrdinalIgnoreCase);
    // de bewaarde refresh-tokens (onversleuteld in het geheugen, versleuteld op schijf)
    private readonly ConcurrentDictionary<string, string> _refresh = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<AgendaTokens> _log;
    private readonly IDataProtector? _protector;
    private readonly string _bestand;
    private readonly object _schrijfslot = new();

    public AgendaTokens(IHttpClientFactory http, IConfiguration config, ILogger<AgendaTokens> log,
                        IDataProtectionProvider? bescherming = null)
    {
        _http = http;
        _config = config;
        _log = log;
        _protector = bescherming?.CreateProtector("MDE.GoogleTokens.v1");
        _bestand = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MDE", "google-tokens.json");
        LeesBestand();
    }

    // ---- bewaren op schijf ----
    private void LeesBestand()
    {
        if (_protector is null) return;
        try
        {
            if (!File.Exists(_bestand)) return;
            var opgeslagen = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_bestand)) ?? new();
            foreach (var (email, versleuteld) in opgeslagen)
            {
                try { _refresh[email] = _protector.Unprotect(versleuteld); }
                catch { /* andere sleutels (bv. na het wissen van de sleutelmap): dit token is niet meer te lezen */ }
            }
            _log.LogInformation("Google: {Aantal} bewaarde aanmelding(en) ingelezen.", _refresh.Count);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Google: bewaarde aanmeldingen niet te lezen ({Bestand})", _bestand); }
    }

    private void SchrijfBestand()
    {
        if (_protector is null) return;
        try
        {
            lock (_schrijfslot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_bestand)!);
                var versleuteld = _refresh.ToDictionary(k => k.Key, k => _protector.Protect(k.Value));
                var tijdelijk = _bestand + ".nieuw";
                File.WriteAllText(tijdelijk, JsonSerializer.Serialize(versleuteld));
                File.Move(tijdelijk, _bestand, overwrite: true);
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Google: aanmelding bewaren mislukt ({Bestand})", _bestand); }
    }

    /// <summary>Bij het inloggen opslaan.</summary>
    public void Bewaar(string email, string? access, string? refresh, int? geldigSeconden)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(access)) return;

        // Bestaand refresh-token bewaren: Google stuurt die alleen bij (nieuwe) toestemming mee.
        string? bestaandRefresh = null;
        if (_tokens.TryGetValue(email, out var oud)) bestaandRefresh = oud.Refresh;
        if (bestaandRefresh is null && _refresh.TryGetValue(email, out var bewaard)) bestaandRefresh = bewaard;

        var nieuwRefresh = string.IsNullOrWhiteSpace(refresh) ? bestaandRefresh : refresh;
        _tokens[email] = new TokenSet(
            access!,
            nieuwRefresh,
            DateTimeOffset.UtcNow.AddSeconds((geldigSeconden ?? 3600) - 60));

        if (!string.IsNullOrWhiteSpace(nieuwRefresh) && (!_refresh.TryGetValue(email, out var was) || was != nieuwRefresh))
        {
            _refresh[email] = nieuwRefresh!;
            SchrijfBestand();
        }
    }

    /// <summary>
    /// Is er een refresh-token (in het geheugen of bewaard)? Zonder werkt mail en agenda maar een uur na het inloggen;
    /// het inloggen vraagt dan één keer opnieuw toestemming (Program.cs, OnCreatingTicket).
    /// </summary>
    public bool HeeftRefresh(string? email)
        => !string.IsNullOrWhiteSpace(email)
           && (_refresh.ContainsKey(email!) || (_tokens.TryGetValue(email!, out var s) && !string.IsNullOrWhiteSpace(s.Refresh)));

    public bool HeeftToegang(string? email) =>
        !string.IsNullOrWhiteSpace(email) && (_tokens.ContainsKey(email!) || _refresh.ContainsKey(email!));

    /// <summary>Korte beschrijving van wat er bekend is — voor diagnose op het scherm.</summary>
    public string Beschrijf(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "geen e-mailadres bekend";
        if (!_tokens.TryGetValue(email!, out var set))
            return _refresh.ContainsKey(email!) ? "bewaarde aanmelding (wordt bij gebruik vernieuwd)" : "geen token bekend (opnieuw inloggen)";

        var over = set.Verloopt - DateTimeOffset.UtcNow;
        var geldig = over > TimeSpan.Zero
            ? $"nog {(int)over.TotalMinutes} min geldig"
            : "verlopen";

        return $"token {set.Access.Length} tekens, {geldig}, " +
               (string.IsNullOrWhiteSpace(set.Refresh) ? "géén refresh-token" : "refresh-token aanwezig");
    }

    /// <summary>Vergeet het token (ook het bewaarde), zodat de volgende inlog opnieuw begint.</summary>
    public void Vergeet(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        _tokens.TryRemove(email!, out _);
        if (_refresh.TryRemove(email!, out _)) SchrijfBestand();
    }

    /// <summary>Geldig access-token, indien nodig vernieuwd. Null als er niets bekend is.</summary>
    public async Task<string?> TokenAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        _tokens.TryGetValue(email!, out var set);
        if (set is not null && DateTimeOffset.UtcNow < set.Verloopt) return set.Access;

        // verlopen of na een herstart nog niet in het geheugen: vernieuwen met het refresh-token
        var refresh = !string.IsNullOrWhiteSpace(set?.Refresh) ? set!.Refresh
                    : _refresh.TryGetValue(email!, out var bewaard) ? bewaard : null;
        if (string.IsNullOrWhiteSpace(refresh)) return null;

        try
        {
            var client = _http.CreateClient();
            var antwoord = await client.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _config["Google:ClientId"] ?? "",
                    ["client_secret"] = _config["Google:ClientSecret"] ?? "",
                    ["refresh_token"] = refresh!,
                    ["grant_type"] = "refresh_token",
                }));

            if (!antwoord.IsSuccessStatusCode)
            {
                var inhoud = await antwoord.Content.ReadAsStringAsync();
                _log.LogWarning("Vernieuwen van het Google-token mislukte: {Status} {Inhoud}", antwoord.StatusCode, inhoud);
                // ingetrokken of verlopen (invalid_grant): weggooien, de volgende inlog vraagt opnieuw toestemming
                if (inhoud.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
                {
                    _tokens.TryRemove(email!, out _);
                    if (_refresh.TryRemove(email!, out _)) SchrijfBestand();
                }
                return null;
            }

            using var doc = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync());
            var nieuw = doc.RootElement.GetProperty("access_token").GetString();
            var geldig = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

            if (string.IsNullOrWhiteSpace(nieuw)) return null;
            _tokens[email!] = new TokenSet(nieuw!, refresh, DateTimeOffset.UtcNow.AddSeconds(geldig - 60));
            return nieuw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Vernieuwen van het Google-token mislukte");
            return null;
        }
    }
}
