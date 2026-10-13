using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Instellingen uit appsettings.json, sectie "Rabobank". Zie appsettings.voorbeeld.json.
/// </summary>
public class RabobankInstellingen
{
    public bool Aan { get; set; }

    /// <summary>"Sandbox" of "Productie".</summary>
    public string Omgeving { get; set; } = "Sandbox";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>Clientcertificaat voor de TLS-verbinding: .pem (met Sleutel) of .pfx/.p12 (met Wachtwoord).</summary>
    public string Certificaat { get; set; } = "";
    public string Sleutel { get; set; } = "";
    public string Wachtwoord { get; set; } = "";

    /// <summary>Certificaat waarmee de verzoeken ondertekend worden. Leeg = hetzelfde als hierboven.</summary>
    public string OndertekenCertificaat { get; set; } = "";
    public string OndertekenSleutel { get; set; } = "";
    public string OndertekenWachtwoord { get; set; } = "";

    /// <summary>Zoals geregistreerd bij de app op developer.rabobank.nl, bv. https://everything.mde-automation.nl/mde/rabobank/terug.</summary>
    public string RedirectUri { get; set; } = "";

    public int IntervalMinuten { get; set; } = 60;

    /// <summary>Hoeveel dagen terug er elke keer opgehaald wordt (dubbele regels worden overgeslagen).</summary>
    public int DagenTerug { get; set; } = 7;

    /// <summary>
    /// Alleen in Productie: pas met true komen de regels echt in banktransacties.
    /// Zo kun je eerst kijken wat er binnen zou komen. In de sandbox wordt nooit geschreven.
    /// </summary>
    public bool Wegschrijven { get; set; }

    public bool Sandbox => !string.Equals(Omgeving, "Productie", StringComparison.OrdinalIgnoreCase);

    public bool Ingesteld => !string.IsNullOrWhiteSpace(ClientId) && !ClientId.Contains("VUL-IN")
                             && !string.IsNullOrWhiteSpace(ClientSecret)
                             && !string.IsNullOrWhiteSpace(Certificaat);

    public string OAuthBasis => Sandbox
        ? "https://oauth-sandbox.rabobank.nl/openapi/sandbox/oauth2-premium"
        : "https://oauth.rabobank.nl/openapi/oauth2-premium";

    public string ApiBasis => Sandbox
        ? "https://api-sandbox.rabobank.nl/openapi/sandbox/payments/insight"
        : "https://api.rabobank.nl/openapi/payments/insight";
}

/// <summary>
/// Rabobank Business Account Insight: de rekeningen, saldi en transacties van MDE, rechtstreeks
/// bij de bank. Toegang via OAuth2 (eenmalig toestemming geven in Rabo Internetbankieren
/// Professional), een clientcertificaat (mTLS) en ondertekende verzoeken (HTTP Signatures).
/// De tokens staan versleuteld in C:\ProgramData\MDE (DataProtection); het refresh-token is
/// eenmalig bruikbaar, dus elk nieuw token wordt meteen weggeschreven.
/// </summary>
public class RabobankKoppeling
{
    private const string Scope = "bai.accountinformation.read";

    private readonly IConfiguration _config;
    private readonly IDataProtector _beschermer;
    private readonly string _map;
    private readonly ILogger<RabobankKoppeling> _log;
    private readonly SemaphoreSlim _tokenSlot = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> _states = new();

    private HttpClient? _http;
    private string _httpSleutel = "";

    public RabobankKoppeling(IConfiguration config, IDataProtectionProvider dp, ILogger<RabobankKoppeling> log)
    {
        _config = config;
        _beschermer = dp.CreateProtector("MDE.Rabobank.Tokens.v1");
        _log = log;
        _map = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MDE");
    }

    public RabobankInstellingen Instellingen
    {
        get
        {
            var i = new RabobankInstellingen();
            _config.GetSection("Rabobank").Bind(i);
            return i;
        }
    }

    // ============================================================
    //  Tokens
    // ============================================================
    private class Tokens
    {
        public string AccessToken { get; set; } = "";
        public DateTime AccessVerloopt { get; set; }
        public string RefreshToken { get; set; } = "";
        public DateTime RefreshVerloopt { get; set; }
        public DateTime GekoppeldOp { get; set; }
    }

    private string TokenBestand(RabobankInstellingen i)
        => Path.Combine(_map, i.Sandbox ? "rabobank-sandbox.dat" : "rabobank-productie.dat");

    private Tokens? LeesTokens(RabobankInstellingen i)
    {
        try
        {
            var pad = TokenBestand(i);
            if (!File.Exists(pad)) return null;
            return JsonSerializer.Deserialize<Tokens>(_beschermer.Unprotect(File.ReadAllText(pad)));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Rabobank: tokenbestand niet te lezen");
            return null;
        }
    }

    private void BewaarTokens(RabobankInstellingen i, Tokens t)
    {
        Directory.CreateDirectory(_map);
        var pad = TokenBestand(i);
        var tijdelijk = pad + ".nieuw";
        File.WriteAllText(tijdelijk, _beschermer.Protect(JsonSerializer.Serialize(t)));
        File.Move(tijdelijk, pad, overwrite: true);
    }

    /// <summary>Gekoppeld = er is een refresh-token dat nog geldig is.</summary>
    public (bool Gekoppeld, DateTime? Op, DateTime? VerlooptOp) Koppelstand()
    {
        var t = LeesTokens(Instellingen);
        if (t is null || t.RefreshToken.Length == 0 || t.RefreshVerloopt < DateTime.Now) return (false, t?.GekoppeldOp, null);
        return (true, t.GekoppeldOp, t.RefreshVerloopt);
    }

    /// <summary>Vergeet de tokens (bv. om met een andere gebruiker opnieuw te koppelen).</summary>
    public void Ontkoppel()
    {
        var pad = TokenBestand(Instellingen);
        if (File.Exists(pad)) File.Move(pad, pad + ".oud", overwrite: true);
    }

    // ============================================================
    //  Koppelen (OAuth2 authorization code)
    // ============================================================

    /// <summary>Het adres waar de gebruiker toestemming geeft bij de Rabobank.</summary>
    public string AutorisatieAdres(string redirectUri)
    {
        var i = Instellingen;
        foreach (var oud in _states.Where(s => s.Value < DateTime.UtcNow).Select(s => s.Key).ToList())
            _states.TryRemove(oud, out _);

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _states[state] = DateTime.UtcNow.AddMinutes(15);

        return $"{i.OAuthBasis}/authorize?client_id={Uri.EscapeDataString(i.ClientId)}" +
               $"&response_type=code&scope={Uri.EscapeDataString(Scope)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={state}";
    }

    /// <summary>Terug van de Rabobank: de code inwisselen voor tokens.</summary>
    public async Task<string?> RondKoppelingAfAsync(string? code, string? state, string redirectUri)
    {
        if (string.IsNullOrEmpty(state) || !_states.TryRemove(state, out var geldig) || geldig < DateTime.UtcNow)
            return "De koppeling is verlopen of niet hier begonnen. Probeer het opnieuw.";
        if (string.IsNullOrEmpty(code))
            return "De Rabobank gaf geen code terug (toestemming geweigerd?).";

        var i = Instellingen;
        await _tokenSlot.WaitAsync();
        try
        {
            var t = await VraagTokensAsync(i, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
            }, null);
            t.GekoppeldOp = DateTime.Now;
            BewaarTokens(i, t);
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Rabobank: code inwisselen mislukt");
            return "Inwisselen bij de Rabobank mislukt: " + ex.Message;
        }
        finally { _tokenSlot.Release(); }
    }

    private async Task<Tokens> VraagTokensAsync(RabobankInstellingen i, Dictionary<string, string> velden, Tokens? oud)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, i.OAuthBasis + "/token")
        {
            Content = new FormUrlEncodedContent(velden),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(i.ClientId + ":" + i.ClientSecret)));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var res = await Http(i).SendAsync(req);
        var tekst = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new RabobankFout($"token: {(int)res.StatusCode} {Kort(tekst)}",
                                   tekst.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase));

        var j = JsonNode.Parse(tekst)!;
        var nu = DateTime.Now;
        var accessSec = Getal(j["expires_in"]) ?? 3600;
        var refreshSec = Getal(j["refresh_token_expires_in"]) ?? 30 * 24 * 3600;
        return new Tokens
        {
            AccessToken = j["access_token"]?.GetValue<string>() ?? "",
            AccessVerloopt = nu.AddSeconds(accessSec - 120),
            RefreshToken = j["refresh_token"]?.GetValue<string>() ?? oud?.RefreshToken ?? "",
            RefreshVerloopt = nu.AddSeconds(refreshSec - 3600),
            GekoppeldOp = oud?.GekoppeldOp ?? nu,
        };
    }

    private async Task<string> AccessTokenAsync(RabobankInstellingen i)
    {
        await _tokenSlot.WaitAsync();
        try
        {
            var t = LeesTokens(i) ?? throw new RabobankFout("Nog niet gekoppeld.", true);
            if (t.AccessToken.Length > 0 && t.AccessVerloopt > DateTime.Now) return t.AccessToken;
            if (t.RefreshToken.Length == 0 || t.RefreshVerloopt < DateTime.Now)
                throw new RabobankFout("De toestemming is verlopen; opnieuw koppelen.", true);

            var nieuw = await VraagTokensAsync(i, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = t.RefreshToken,
            }, t);
            BewaarTokens(i, nieuw);       // meteen: het oude refresh-token werkt niet meer
            return nieuw.AccessToken;
        }
        finally { _tokenSlot.Release(); }
    }

    // ============================================================
    //  API
    // ============================================================
    public async Task<List<RabobankRekening>> RekeningenAsync()
    {
        var i = Instellingen;
        var j = await GetAsync(i, i.ApiBasis + "/accounts");
        var lijst = new List<RabobankRekening>();
        foreach (var a in j["accounts"]?.AsArray() ?? new JsonArray())
        {
            if (a is null) continue;
            lijst.Add(new RabobankRekening(
                Tekst(a["resourceId"]) is { Length: > 0 } id ? id : Tekst(a["id"]),
                Tekst(a["iban"]).Replace(" ", "").ToUpperInvariant(),
                Tekst(a["name"]) is { Length: > 0 } n ? n : Tekst(a["ownerName"])));
        }
        return lijst;
    }

    /// <summary>Het actuele saldo (interimBooked, anders het eerste dat de bank geeft).</summary>
    public async Task<decimal?> SaldoAsync(RabobankRekening rekening)
    {
        var i = Instellingen;
        var j = await GetAsync(i, $"{i.ApiBasis}/accounts/{Uri.EscapeDataString(rekening.Id)}/balances");
        var saldi = j["balances"]?.AsArray();
        if (saldi is null || saldi.Count == 0) return null;
        var keuze = saldi.FirstOrDefault(s => Tekst(s?["balanceType"]).Equals("interimBooked", StringComparison.OrdinalIgnoreCase))
                    ?? saldi.FirstOrDefault(s => Tekst(s?["balanceType"]).Equals("closingBooked", StringComparison.OrdinalIgnoreCase))
                    ?? saldi[0];
        return Bedrag(keuze?["balanceAmount"]);
    }

    /// <summary>
    /// Geboekte transacties van een rekening tussen twee tijdstippen (UTC), alle pagina's.
    /// De bank wil een volledig UTC-tijdstip (2026-02-22T06:00:00.000001Z), geen kale datum,
    /// en raadt aan dateTo vijf minuten voor nu te leggen.
    /// </summary>
    public async Task<List<BankRegel>> TransactiesAsync(RabobankRekening rekening, DateTime vanUtc, DateTime totUtc)
    {
        var i = Instellingen;
        var regels = new List<BankRegel>();
        string? adres = $"{i.ApiBasis}/accounts/{Uri.EscapeDataString(rekening.Id)}/transactions" +
                        $"?bookingStatus=booked&dateFrom={Uri.EscapeDataString(Utc(vanUtc))}" +
                        $"&dateTo={Uri.EscapeDataString(Utc(totUtc))}&size=500";
        var paginas = 0;

        while (adres is not null && paginas++ < 200)
        {
            var j = await GetAsync(i, adres);
            if (paginas == 1) BewaarVoorbeeld(i, j);

            var t = j["transactions"];
            foreach (var x in t?["booked"]?.AsArray() ?? new JsonArray())
                if (x is not null) regels.Add(NaarRegel(x, rekening.Iban));

            adres = VolgendePagina(i, t?["_links"] ?? j["_links"]);
        }
        return regels;
    }

    private static string? VolgendePagina(RabobankInstellingen i, JsonNode? links)
    {
        var next = links?["next"];
        if (next is null) return null;
        var href = next is JsonValue ? Tekst(next) : Tekst(next["href"]);
        if (href.Length == 0) return null;
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return href;
        var basis = new Uri(i.ApiBasis);
        if (href.StartsWith('/')) return $"{basis.Scheme}://{basis.Host}{href}";
        return i.ApiBasis.TrimEnd('/') + "/" + href;
    }

    /// <summary>Een transactie uit de API in de vorm van de tabel banktransacties.</summary>
    internal static BankRegel NaarRegel(JsonNode x, string eigenIban)
    {
        var bedrag = Bedrag(x["transactionAmount"]) ?? 0;
        var debiteurIban = Tekst(x["debtorAccount"]?["iban"]).Replace(" ", "").ToUpperInvariant();

        // Het bedrag hoort met teken te komen; staat het er zonder, dan beslist wie betaalde.
        if (bedrag > 0 && debiteurIban == eigenIban) bedrag = -bedrag;
        var af = bedrag < 0;

        string Wie(string veld) => Tekst(x[(af ? "creditor" : "debtor") + veld]);
        var tegen = af ? x["creditorAccount"] : x["debtorAccount"];
        var agent = af ? x["creditorAgent"] : x["debtorAgent"];

        var instr = x["instructedAmount"];
        var volgnr = long.TryParse(Tekst(x["entryReference"]), out var vn) ? vn : 0;

        return new BankRegel
        {
            Rekening = eigenIban,
            Munt = Tekst(x["transactionAmount"]?["currency"]) is { Length: > 0 } m ? m : "EUR",
            Bic = "RABONL2U",
            Volgnummer = volgnr,
            Datum = Datum(x["bookingDate"]) ?? DateTime.Today,
            Rentedatum = Datum(x["valueDate"]),
            Bedrag = bedrag,
            SaldoNa = Bedrag(x["balanceAfterBooking"]?["balanceAmount"]) ?? 0,
            Tegenrekening = Tekst(tegen?["iban"]).Replace(" ", "").ToUpperInvariant(),
            NaamTegenpartij = Wie("Name"),
            NaamUiteindelijkePartij = Tekst(x[af ? "ultimateCreditor" : "ultimateDebtor"]),
            NaamInitierendePartij = Tekst(x["initiatingPartyName"]),
            BicTegenpartij = agent is JsonValue ? Tekst(agent) : Tekst(agent?["bic"] ?? agent?["bicFi"]),
            Code = Tekst(x["raboTransactionTypeName"]),
            BatchId = Tekst(x["batchEntryReference"]),
            Referentie = Tekst(x["endToEndId"]),
            Machtiging = Tekst(x["mandateId"]),
            Incassant = Tekst(x["creditorId"]),
            Betalingskenmerk = Tekst(x["remittanceInformationStructured"]),
            Omschrijving = Tekst(x["remittanceInformationUnstructured"]),
            RedenRetour = Tekst(x["reasonCode"]),
            OorspronkelijkBedrag = Bedrag(instr) ?? 0,
            // instructedAmount komt als {"amount": 0.84, "sourceCurrency": "USD"} (sandbox 13-10-2026).
            OorspronkelijkeMunt = Tekst(instr?["sourceCurrency"]) is { Length: > 0 } om ? om : Tekst(instr?["currency"]),
        };
    }

    /// <summary>
    /// De eerste pagina van de laatste ophaalronde, voor als de velden anders blijken te heten
    /// dan verwacht. Alleen in de sandbox: echte bankgegevens horen niet los op schijf.
    /// </summary>
    private void BewaarVoorbeeld(RabobankInstellingen i, JsonNode j)
    {
        if (!i.Sandbox) return;
        try
        {
            Directory.CreateDirectory(_map);
            File.WriteAllText(Path.Combine(_map, "rabobank-sandbox-voorbeeld.json"),
                j.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* alleen hulp bij uitzoeken */ }
    }

    // ============================================================
    //  HTTP: mTLS en ondertekenen
    // ============================================================
    private async Task<JsonNode> GetAsync(RabobankInstellingen i, string adres)
    {
        var token = await AccessTokenAsync(i);

        using var req = new HttpRequestMessage(HttpMethod.Get, adres);
        var datum = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
        var digest = "sha-512=" + Convert.ToBase64String(SHA512.HashData(Array.Empty<byte>()));
        var requestId = Guid.NewGuid().ToString();

        var (cert, sleutel) = Ondertekening(i);
        var tekenen = $"date: {datum}\ndigest: {digest}\nx-request-id: {requestId}";
        var handtekening = Convert.ToBase64String(
            sleutel.SignData(Encoding.UTF8.GetBytes(tekenen), HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1));
        var keyId = BigInteger.Parse("0" + cert.SerialNumber, NumberStyles.HexNumber).ToString(CultureInfo.InvariantCulture);

        req.Headers.TryAddWithoutValidation("Date", datum);
        req.Headers.TryAddWithoutValidation("Digest", digest);
        req.Headers.TryAddWithoutValidation("X-Request-ID", requestId);
        req.Headers.TryAddWithoutValidation("Signature",
            $"keyId=\"{keyId}\",algorithm=\"rsa-sha512\",headers=\"date digest x-request-id\",signature=\"{handtekening}\"");
        req.Headers.TryAddWithoutValidation("Signature-Certificate", Convert.ToBase64String(cert.RawData));
        req.Headers.TryAddWithoutValidation("X-IBM-Client-Id", i.ClientId);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var res = await Http(i).SendAsync(req);
        var tekst = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new RabobankFout($"{(int)res.StatusCode} op {new Uri(adres).AbsolutePath}: {Kort(tekst)}",
                                   res.StatusCode == System.Net.HttpStatusCode.Unauthorized);
        return JsonNode.Parse(tekst) ?? new JsonObject();
    }

    private (X509Certificate2 Cert, RSA Sleutel)? _onderteken;
    private string _ondertekenSleutel = "";

    private (X509Certificate2, RSA) Ondertekening(RabobankInstellingen i)
    {
        var certPad = string.IsNullOrWhiteSpace(i.OndertekenCertificaat) ? i.Certificaat : i.OndertekenCertificaat;
        var sleutelPad = string.IsNullOrWhiteSpace(i.OndertekenCertificaat) ? i.Sleutel : i.OndertekenSleutel;
        var wachtwoord = string.IsNullOrWhiteSpace(i.OndertekenCertificaat) ? i.Wachtwoord : i.OndertekenWachtwoord;
        var sleutel = certPad + "|" + sleutelPad;

        if (_onderteken is null || _ondertekenSleutel != sleutel)
        {
            var c = LaadCertificaat(certPad, sleutelPad, wachtwoord);
            var rsa = c.GetRSAPrivateKey() ?? throw new RabobankFout("Het ondertekencertificaat heeft geen RSA-sleutel.", false);
            _onderteken = (c, rsa);
            _ondertekenSleutel = sleutel;
        }
        return _onderteken.Value;
    }

    private HttpClient Http(RabobankInstellingen i)
    {
        var sleutel = i.Certificaat + "|" + i.Sleutel;
        if (_http is not null && _httpSleutel == sleutel) return _http;

        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
        handler.SslOptions.ClientCertificates = new X509CertificateCollection
        {
            LaadCertificaat(i.Certificaat, i.Sleutel, i.Wachtwoord),
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        _httpSleutel = sleutel;
        return _http;
    }

    /// <summary>
    /// .pfx/.p12 direct; .pem/.crt met een losse sleutel. Een PEM-sleutel wordt via PKCS#12
    /// omgezet, anders weigert Windows hem voor een TLS-verbinding.
    /// </summary>
    private static X509Certificate2 LaadCertificaat(string certPad, string sleutelPad, string wachtwoord)
    {
        if (string.IsNullOrWhiteSpace(certPad) || !File.Exists(certPad))
            throw new RabobankFout($"Certificaat niet gevonden: '{certPad}'.", false);

        var ext = Path.GetExtension(certPad).ToLowerInvariant();
        if (ext is ".pfx" or ".p12")
            return new X509Certificate2(certPad, string.IsNullOrEmpty(wachtwoord) ? null : wachtwoord,
                                        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);

        if (string.IsNullOrWhiteSpace(sleutelPad) || !File.Exists(sleutelPad))
            throw new RabobankFout($"Sleutel bij het certificaat niet gevonden: '{sleutelPad}'.", false);

        using var pem = string.IsNullOrEmpty(wachtwoord)
            ? X509Certificate2.CreateFromPemFile(certPad, sleutelPad)
            : X509Certificate2.CreateFromEncryptedPemFile(certPad, wachtwoord, sleutelPad);
        return new X509Certificate2(pem.Export(X509ContentType.Pkcs12), (string?)null,
                                    X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
    }

    // ============================================================
    //  JSON-hulpjes
    // ============================================================
    private static string Tekst(JsonNode? n)
    {
        if (n is null) return "";
        if (n is JsonValue v)
            return v.TryGetValue<string>(out var s) ? s.Trim() : v.ToJsonString().Trim('"');
        if (n is JsonArray a) return string.Join(" ", a.Select(Tekst)).Trim();
        return n["name"] is { } naam ? Tekst(naam) : "";
    }

    private static decimal? Bedrag(JsonNode? n)
    {
        if (n is null) return null;
        var w = n is JsonValue ? Tekst(n) : Tekst(n["value"] ?? n["amount"]);
        return decimal.TryParse(w, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static DateTime? Datum(JsonNode? n)
        => DateTime.TryParse(Tekst(n), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;

    private static string Utc(DateTime t)
        => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    private static long? Getal(JsonNode? n) => long.TryParse(Tekst(n), out var l) ? l : null;

    private static string Kort(string s) => s.Length <= 300 ? s : s[..300] + "…";
}

/// <summary>Fout van de Rabobank; <see cref="OpnieuwKoppelen"/> = de toestemming moet opnieuw.</summary>
public class RabobankFout(string bericht, bool opnieuwKoppelen) : Exception(bericht)
{
    public bool OpnieuwKoppelen { get; } = opnieuwKoppelen;
}
