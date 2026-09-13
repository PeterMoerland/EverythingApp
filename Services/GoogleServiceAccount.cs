using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EverythingApp.Services;

/// <summary>
/// Haalt tokens op namens een gedeelde postbus, met een servicedescriptor
/// (service account) en domeinbrede delegatie.
///
/// Wat er eenmalig moet gebeuren:
///   1. In Google Cloud Console een serviceaccount maken en een JSON-sleutel
///      downloaden. Pad daarvan in appsettings.json onder Google:ServiceAccountJson.
///   2. In de Google Workspace-beheerconsole: Beveiliging → Toegangsbeheer voor API's
///      → Domeinbrede delegatie. Daar de client-ID van het serviceaccount toevoegen
///      met de scopes voor Gmail (gmail.readonly en, als je wilt versturen, gmail.send).
///
/// Daarna kan de app namens inkoop@ lezen zonder dat iemand zijn wachtwoord geeft,
/// en zonder dat het aan één persoon hangt.
///
/// Techniek: we ondertekenen zelf een JWT met de privésleutel en wisselen die bij
/// Google om voor een access-token. Dat scheelt een extra pakket.
/// </summary>
public class GoogleServiceAccount
{
    private record Sleutel(string Email, string PrivateKeyPem);
    private record Token(string Access, DateTimeOffset Verloopt);

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleServiceAccount> _log;
    private readonly ConcurrentDictionary<string, Token> _tokens = new(StringComparer.OrdinalIgnoreCase);

    private Sleutel? _sleutel;

    public GoogleServiceAccount(IHttpClientFactory http, IConfiguration config,
                                ILogger<GoogleServiceAccount> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    /// <summary>Is er een sleutelbestand ingesteld en leesbaar?</summary>
    public bool Beschikbaar => Lees() is not null;

    /// <summary>Korte uitleg voor het scherm als het niet werkt.</summary>
    public string Beschrijf()
    {
        var pad = _config["Google:ServiceAccountJson"];
        if (string.IsNullOrWhiteSpace(pad))
            return "Google:ServiceAccountJson staat niet in appsettings.json.";
        if (!File.Exists(pad))
            return $"Het sleutelbestand is niet gevonden: {pad}";
        return Lees() is null
            ? "Het sleutelbestand kon niet worden gelezen (client_email of private_key ontbreekt)."
            : "Serviceaccount ingesteld.";
    }

    /// <summary>
    /// Leest het sleutelbestand. Alleen een geslaagde poging wordt onthouden, zodat
    /// het invullen van het pad geen herstart van de app vereist.
    /// </summary>
    private Sleutel? Lees()
    {
        if (_sleutel is not null) return _sleutel;

        var pad = _config["Google:ServiceAccountJson"];
        if (string.IsNullOrWhiteSpace(pad) || !File.Exists(pad)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pad));
            var email = doc.RootElement.TryGetProperty("client_email", out var e) ? e.GetString() : null;
            var key = doc.RootElement.TryGetProperty("private_key", out var k) ? k.GetString() : null;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(key)) return null;
            return _sleutel = new Sleutel(email!, key!);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Sleutelbestand van het serviceaccount kon niet worden gelezen");
            return null;
        }
    }

    /// <summary>
    /// Access-token om namens 'postbusAdres' te werken. Tokens worden een uur
    /// hergebruikt, dus dit mag rustig per pagina worden aangeroepen.
    /// </summary>
    public async Task<string?> TokenAsync(string postbusAdres, string scope)
    {
        var sleutel = Lees();
        if (sleutel is null) return null;

        var cacheSleutel = postbusAdres + "|" + scope;
        if (_tokens.TryGetValue(cacheSleutel, out var bestaand)
            && DateTimeOffset.UtcNow < bestaand.Verloopt)
            return bestaand.Access;

        try
        {
            var jwt = MaakJwt(sleutel, postbusAdres, scope);

            var client = _http.CreateClient();
            var antwoord = await client.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = jwt,
                }));

            var inhoud = await antwoord.Content.ReadAsStringAsync();
            if (!antwoord.IsSuccessStatusCode)
            {
                _log.LogWarning("Token voor {Postbus} mislukte: {Status} {Body}",
                                postbusAdres, antwoord.StatusCode, inhoud);
                throw new InvalidOperationException(
                    $"Google gaf {(int)antwoord.StatusCode} bij het openen van {postbusAdres}. " +
                    "Controleer de domeinbrede delegatie in de Workspace-beheerconsole " +
                    "(client-ID van het serviceaccount plus de Gmail-scopes).");
            }

            using var doc = JsonDocument.Parse(inhoud);
            var token = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
            var geldig = doc.RootElement.TryGetProperty("expires_in", out var g) ? g.GetInt32() : 3600;
            if (string.IsNullOrWhiteSpace(token)) return null;

            _tokens[cacheSleutel] = new Token(token!, DateTimeOffset.UtcNow.AddSeconds(geldig - 60));
            return token;
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Token voor {Postbus} mislukte", postbusAdres);
            throw new InvalidOperationException($"Kon {postbusAdres} niet openen: {ex.Message}");
        }
    }

    /// <summary>Zelfondertekende JWT, zoals Google die verwacht voor delegatie.</summary>
    private static string MaakJwt(Sleutel sleutel, string postbusAdres, string scope)
    {
        var nu = DateTimeOffset.UtcNow;

        var kop = new { alg = "RS256", typ = "JWT" };
        var inhoud = new Dictionary<string, object?>
        {
            ["iss"] = sleutel.Email,
            ["sub"] = postbusAdres,          // namens wie we werken
            ["scope"] = scope,
            ["aud"] = "https://oauth2.googleapis.com/token",
            ["iat"] = nu.ToUnixTimeSeconds(),
            ["exp"] = nu.AddMinutes(50).ToUnixTimeSeconds(),
        };

        var teOndertekenen =
            Base64Url(JsonSerializer.SerializeToUtf8Bytes(kop)) + "." +
            Base64Url(JsonSerializer.SerializeToUtf8Bytes(inhoud));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(sleutel.PrivateKeyPem);

        var handtekening = rsa.SignData(Encoding.UTF8.GetBytes(teOndertekenen),
                                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return teOndertekenen + "." + Base64Url(handtekening);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
