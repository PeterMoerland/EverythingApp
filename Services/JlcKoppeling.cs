using System.Security.Cryptography;
using System.Text;

namespace EverythingApp.Services;

/// <summary>
/// De koppeling met het JLCPCB Open Platform (open.jlcpcb.com), gebruikt om
/// per MPN het JLCPCB/LCSC-onderdeelnummer (C-nummer) op te zoeken voor de
/// kolom 'JLCPCB Part #' in de BOM-export.
///
/// Elke aanvraag wordt ondertekend volgens hun 'JOP'-schema: een
/// HMAC-SHA256-handtekening (Base64) over vijf regels — methode, pad,
/// unix-timestamp, nonce en de JSON-body, elk afgesloten met \n — gezet in
/// de Authorization-header samen met appid, accesskey, nonce en timestamp.
///
/// Sleutels maak je aan op api.jlcpcb.com (console ▸ app aanmaken) en zet je
/// in appsettings onder Jlcpcb:AppId / AccessKey / SecretKey — of netter als
/// omgevingsvariabelen Jlcpcb__SecretKey enzovoort. Let op: het platform kan
/// een IP-whitelist eisen; zet daar het uitgaande IP van de server in.
/// </summary>
public class JlcKoppeling(IConfiguration config)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string Basis = "https://open.jlcpcb.com";

    private string AppId => config["Jlcpcb:AppId"] ?? "";
    private string AccessKey => config["Jlcpcb:AccessKey"] ?? "";
    private string SecretKey => config["Jlcpcb:SecretKey"] ?? "";

    public bool Beschikbaar => AppId.Length > 0 && AccessKey.Length > 0 && SecretKey.Length > 0;

    /// <summary>
    /// Ondertekende POST naar het platform; geeft de antwoordtekst (JSON)
    /// of een leesbare fout. De aanroepende kant kent de paden en velden
    /// van de specifieke interface.
    /// </summary>
    public async Task<(string? Antwoord, string? Fout)> PostAsync(string pad, string jsonBody)
    {
        if (!Beschikbaar)
            return (null, "Geen JLCPCB-sleutels ingesteld (Jlcpcb:AppId, AccessKey en SecretKey).");

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nonce = Nonce();

            // Vijf regels, elk afgesloten met \n — exact zoals de documentatie.
            var teTekenen = $"POST\n{pad}\n{timestamp}\n{nonce}\n{jsonBody}\n";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SecretKey));
            var handtekening = Convert.ToBase64String(
                hmac.ComputeHash(Encoding.UTF8.GetBytes(teTekenen)));

            using var vraag = new HttpRequestMessage(HttpMethod.Post, Basis + pad);
            vraag.Headers.TryAddWithoutValidation("Authorization",
                $"JOP appid=\"{AppId}\",accesskey=\"{AccessKey}\",nonce=\"{nonce}\"," +
                $"timestamp=\"{timestamp}\",signature=\"{handtekening}\"");
            vraag.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var antwoord = await Http.SendAsync(vraag);
            var tekst = await antwoord.Content.ReadAsStringAsync();
            if (!antwoord.IsSuccessStatusCode)
                return (null, $"JLCPCB antwoordt {(int)antwoord.StatusCode}: " +
                              tekst[..Math.Min(300, tekst.Length)]);
            return (tekst, null);
        }
        catch (Exception ex)
        {
            return (null, "JLCPCB-aanvraag mislukt: " + ex.Message);
        }
    }

    /// <summary>32 willekeurige tekens A-Z a-z 0-9, zoals het platform eist.</summary>
    private static string Nonce()
    {
        const string tekens = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Range(0, 32)
            .Select(_ => tekens[Random.Shared.Next(tekens.Length)]).ToArray());
    }
}
