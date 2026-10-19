using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EverythingApp.Services;

/// <summary>Een antwoord van de Converse-API van Amazon Bedrock.</summary>
public record BedrockAntwoord(string StopReden, JsonArray Inhoud, int TokensIn, int TokensUit, string Laag);

/// <summary>Een fout van Bedrock, met de HTTP-status en de melding van AWS.</summary>
public class BedrockFout : Exception
{
    public int Status { get; }
    public BedrockFout(int status, string melding) : base(melding) => Status = status;
}

/// <summary>
/// Praat met de Converse-API van Amazon Bedrock (POST /model/{model}/converse op bedrock-runtime.{regio}).
/// Zonder AWS-SDK: gewoon HTTP. Aanmelden op een van twee manieren (appsettings, sectie Bedrock):
///   - Sleutel: een Bedrock API-sleutel, als 'Authorization: Bearer'. Volgens AWS bedoeld om mee te beginnen.
///   - AccessKey + SecretKey (+ SessionToken): IAM-sleutels, ondertekend met AWS Signature Version 4. Aanbevolen
///     voor de server.
/// Laag (serviceTier): default, flex (goedkoper, mag langer duren) of priority.
/// </summary>
public class BedrockClient
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http;

    public BedrockClient(IConfiguration config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };   // flex mag even duren
    }

    /// <summary>
    /// Optioneel: het endpoint zoals AWS het geeft (bv. https://bedrock-runtime.eu-central-1.amazonaws.com, of een
    /// privé VPC-endpoint). Leeg = afgeleid van de regio.
    /// </summary>
    private string Endpoint => (_config["Bedrock:Endpoint"] ?? "").Trim().TrimEnd('/');

    private string Regio
    {
        get
        {
            if ((_config["Bedrock:Regio"] ?? "").Trim() is { Length: > 0 } r) return r;
            // regio uit het endpoint halen (...bedrock-runtime.eu-central-1.amazonaws.com of ...bedrock-runtime.eu-central-1.vpce.amazonaws.com)
            var m = System.Text.RegularExpressions.Regex.Match(Endpoint, @"bedrock-runtime[.-]([a-z]{2}(?:-[a-z]+)+-\d)");
            return m.Success ? m.Groups[1].Value : "eu-central-1";
        }
    }
    public string Model => (_config["Bedrock:Model"] ?? "").Trim();
    private string Sleutel => (_config["Bedrock:Sleutel"] ?? "").Trim();
    private string AccessKey => (_config["Bedrock:AccessKey"] ?? "").Trim();
    private string SecretKey => (_config["Bedrock:SecretKey"] ?? "").Trim();
    private string SessionToken => (_config["Bedrock:SessionToken"] ?? "").Trim();

    /// <summary>
    /// Alleen om lokaal te testen zonder sleutel (Bedrock__Nep=true): geen verbinding met AWS. De eerste ronde vraagt
    /// de omzet per maand van dit jaar op (echte opvraging, alleen lezen), de tweede geeft een vast testantwoord.
    /// </summary>
    private bool Nep => _config.GetValue<bool>("Bedrock:Nep");

    /// <summary>Model en een manier van aanmelden ingevuld?</summary>
    public bool Ingesteld => Nep || (Model.Length > 0 && (Sleutel.Length > 0 || (AccessKey.Length > 0 && SecretKey.Length > 0)));

    /// <summary>Wat er ontbreekt, voor de melding op het scherm.</summary>
    public string Ontbreekt => Model.Length == 0 ? "Bedrock:Model is niet ingevuld."
        : Sleutel.Length == 0 && (AccessKey.Length == 0 || SecretKey.Length == 0) ? "Bedrock:Sleutel of Bedrock:AccessKey/SecretKey is niet ingevuld."
        : "";

    /// <summary>Eén ronde Converse. <paramref name="verzoek"/> bevat messages, system, toolConfig enz.</summary>
    public async Task<BedrockAntwoord> ConverseAsync(JsonObject verzoek, string laag, CancellationToken stop)
    {
        if (Nep) return await NepAsync(verzoek, laag, stop);
        if (!Ingesteld) throw new BedrockFout(0, "Bedrock is niet ingesteld. " + Ontbreekt);

        var body = (JsonObject)verzoek.DeepClone();
        if (!string.IsNullOrWhiteSpace(laag) && laag != "default")
            body["serviceTier"] = new JsonObject { ["type"] = laag };
        var json = body.ToJsonString();

        var basis = Endpoint.Length > 0
            ? new Uri(Endpoint.Contains("://") ? Endpoint : "https://" + Endpoint)
            : new Uri($"https://bedrock-runtime.{Regio}.amazonaws.com");
        var host = basis.IsDefaultPort ? basis.Host : $"{basis.Host}:{basis.Port}";
        var voorPad = basis.AbsolutePath.TrimEnd('/');               // meestal leeg
        var modelPad = Uri.EscapeDataString(Model);                 // ':' in de model-id wordt %3A
        var url = $"{basis.Scheme}://{host}{voorPad}/model/{modelPad}/converse";

        using var post = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        post.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        post.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (Sleutel.Length > 0)
        {
            post.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Sleutel);
        }
        else
        {
            var nu = DateTime.UtcNow;
            var amzDatum = nu.ToString("yyyyMMdd'T'HHmmss'Z'");
            // canoniek pad: elk segment nog eens gecodeerd (alle diensten behalve S3)
            var canoniekPad = voorPad + "/model/" + Uri.EscapeDataString(modelPad) + "/converse";
            var koppen = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["content-type"] = "application/json",
                ["host"] = host,
                ["x-amz-date"] = amzDatum,
            };
            if (SessionToken.Length > 0) koppen["x-amz-security-token"] = SessionToken;

            var autorisatie = SigV4.Autorisatie("POST", canoniekPad, "", koppen, Encoding.UTF8.GetBytes(json),
                                                AccessKey, SecretKey, Regio, "bedrock", nu);
            post.Headers.TryAddWithoutValidation("X-Amz-Date", amzDatum);
            if (SessionToken.Length > 0) post.Headers.TryAddWithoutValidation("X-Amz-Security-Token", SessionToken);
            post.Headers.TryAddWithoutValidation("Authorization", autorisatie);
        }

        using var antwoord = await _http.SendAsync(post, stop);
        var tekst = await antwoord.Content.ReadAsStringAsync(stop);
        if (!antwoord.IsSuccessStatusCode)
            throw new BedrockFout((int)antwoord.StatusCode, Melding((int)antwoord.StatusCode, tekst));

        var root = JsonNode.Parse(tekst) as JsonObject ?? throw new BedrockFout(0, "Leeg antwoord van Bedrock.");
        var inhoud = root["output"]?["message"]?["content"] as JsonArray ?? new JsonArray();
        var usage = root["usage"];
        return new BedrockAntwoord(
            root["stopReason"]?.GetValue<string>() ?? "",
            (JsonArray)inhoud.DeepClone(),
            usage?["inputTokens"]?.GetValue<int>() ?? 0,
            usage?["outputTokens"]?.GetValue<int>() ?? 0,
            root["serviceTier"]?["type"]?.GetValue<string>() ?? (string.IsNullOrWhiteSpace(laag) ? "default" : laag));
    }

    private static async Task<BedrockAntwoord> NepAsync(JsonObject verzoek, string laag, CancellationToken stop)
    {
        await Task.Delay(1500, stop);
        var berichten = verzoek["messages"] as JsonArray ?? new JsonArray();
        var heeftResultaat = berichten.OfType<JsonObject>().Any(m => (m["content"] as JsonArray)?.OfType<JsonObject>().Any(c => c["toolResult"] is not null) == true);
        if (!heeftResultaat)
            return new BedrockAntwoord("tool_use", new JsonArray(
                new JsonObject { ["text"] = "Ik vraag de omzet op." },
                new JsonObject
                {
                    ["toolUse"] = new JsonObject
                    {
                        ["toolUseId"] = "nep-1", ["name"] = "omzet_per_maand",
                        ["input"] = new JsonObject { ["van"] = $"{DateTime.Today.Year}-01-01" },
                    },
                }), 900, 40, laag);
        return new BedrockAntwoord("end_turn", new JsonArray(new JsonObject
        {
            ["text"] = "**Testantwoord (`Bedrock__Nep`):** er is geen verbinding met AWS gemaakt. De omzet per maand van dit jaar is " +
                       "wel echt opgevraagd; zie *Gebruikte gegevens* hieronder.\n\n| Kolom | Waarde |\n|---|---|\n| test | 1 |",
        }), 1500, 120, laag);
    }

    /// <summary>
    /// Een afbeelding of PDF met een opdracht naar het model; geeft de tekst van het antwoord. Een PDF gaat als document
    /// met citations, zodat Claude ook het beeld van de pagina's bekijkt (zonder citations alleen de tekstlaag).
    /// </summary>
    public async Task<(string Tekst, int TokensIn, int TokensUit)> LeesBeeldAsync(byte[] inhoud, string mediaSoort, string opdracht,
                                                                                int maxTokens, CancellationToken stop)
    {
        var base64 = Convert.ToBase64String(inhoud);
        JsonObject blok = mediaSoort == "application/pdf"
            ? new JsonObject
            {
                ["document"] = new JsonObject
                {
                    ["format"] = "pdf", ["name"] = "document",
                    ["source"] = new JsonObject { ["bytes"] = base64 },
                    ["citations"] = new JsonObject { ["enabled"] = true },
                },
            }
            : new JsonObject
            {
                ["image"] = new JsonObject
                {
                    ["format"] = mediaSoort switch { "image/png" => "png", "image/gif" => "gif", "image/webp" => "webp", _ => "jpeg" },
                    ["source"] = new JsonObject { ["bytes"] = base64 },
                },
            };
        var verzoek = new JsonObject
        {
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonArray(blok, new JsonObject { ["text"] = opdracht }) }),
            ["inferenceConfig"] = new JsonObject { ["maxTokens"] = maxTokens, ["temperature"] = 0 },
        };
        var a = await ConverseAsync(verzoek, "default", stop);
        var tekst = new StringBuilder();
        foreach (var b in a.Inhoud.OfType<JsonObject>())
        {
            if (b["text"] is JsonValue tv) tekst.Append(tv.GetValue<string>());
            if (b["citationsContent"]?["content"] is JsonArray delen)
                foreach (var d in delen.OfType<JsonObject>())
                    if (d["text"] is JsonValue dv) tekst.Append(dv.GetValue<string>());
        }
        return (tekst.ToString(), a.TokensIn, a.TokensUit);
    }

    private static string Melding(int status, string ruw)
    {
        string? bericht = null;
        try
        {
            var j = JsonNode.Parse(ruw);
            bericht = j?["message"]?.GetValue<string>() ?? j?["Message"]?.GetValue<string>();   // AWS schrijft beide
        }
        catch { }
        bericht ??= ruw.Length > 300 ? ruw[..300] : ruw;
        return status switch
        {
            401 or 403 => "Geen toegang tot Bedrock (sleutel of rechten): " + bericht,
            404 when bericht.Contains("use case", StringComparison.OrdinalIgnoreCase)
                => "Het Anthropic-formulier (use case details) is in de Bedrock-console nog niet ingevuld: " + bericht,
            404 => "Model niet gevonden in deze regio (Bedrock:Model / Bedrock:Regio): " + bericht,
            429 => "Bedrock is even te druk of het quotum is op: " + bericht,
            _ => $"Bedrock gaf {status}: {bericht}",
        };
    }
}

/// <summary>AWS Signature Version 4 (alleen wat Bedrock nodig heeft). Los, zodat het met de AWS-voorbeelden te toetsen is.</summary>
public static class SigV4
{
    public static string Autorisatie(string methode, string canoniekPad, string canoniekeQuery,
                                     SortedDictionary<string, string> koppen, byte[] body,
                                     string accessKey, string secretKey, string regio, string dienst, DateTime utc)
    {
        var datum = utc.ToString("yyyyMMdd");
        var amzDatum = utc.ToString("yyyyMMdd'T'HHmmss'Z'");
        var getekend = string.Join(";", koppen.Keys);
        var canoniek = new StringBuilder()
            .Append(methode).Append('\n')
            .Append(canoniekPad).Append('\n')
            .Append(canoniekeQuery).Append('\n');
        foreach (var (naam, waarde) in koppen) canoniek.Append(naam).Append(':').Append(waarde.Trim()).Append('\n');
        canoniek.Append('\n').Append(getekend).Append('\n').Append(Hex(SHA256.HashData(body)));

        var bereik = $"{datum}/{regio}/{dienst}/aws4_request";
        var teTekenen = $"AWS4-HMAC-SHA256\n{amzDatum}\n{bereik}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canoniek.ToString())))}";
        var handtekening = Hex(HMACSHA256.HashData(Sleutel(secretKey, datum, regio, dienst), Encoding.UTF8.GetBytes(teTekenen)));
        return $"AWS4-HMAC-SHA256 Credential={accessKey}/{bereik}, SignedHeaders={getekend}, Signature={handtekening}";
    }

    public static byte[] Sleutel(string secretKey, string datum, string regio, string dienst)
    {
        var k = HMACSHA256.HashData(Encoding.UTF8.GetBytes("AWS4" + secretKey), Encoding.UTF8.GetBytes(datum));
        k = HMACSHA256.HashData(k, Encoding.UTF8.GetBytes(regio));
        k = HMACSHA256.HashData(k, Encoding.UTF8.GetBytes(dienst));
        return HMACSHA256.HashData(k, Encoding.UTF8.GetBytes("aws4_request"));
    }

    public static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();
}
