using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EverythingApp.Services;

/// <summary>Een artikel dat wij bij Solar kopen: onze gegevens uit ArtikelSupplier.</summary>
/// <param name="Id">ArtikelSupplier.ArtikelSupplierID: de regel die bij het overnemen van een prijs wordt bijgewerkt.</param>
public record LeverancierArtikel(int ArtikelId, string Code, string Omschrijving, string Artikelnummer,
                                 decimal StuksPerVe, decimal PrijsPerVe, decimal Korting, int Id = 0)
{
    /// <summary>Onze brutoprijs per stuk (PrijsPerVE gedeeld door StuksPerVE).</summary>
    public decimal BrutoPerStuk => StuksPerVe > 0 ? PrijsPerVe / StuksPerVe : PrijsPerVe;
    /// <summary>Onze nettoprijs per stuk: bruto min de korting (Korting is een fractie, 0,5 = 50%).</summary>
    public decimal NettoPerStuk => BrutoPerStuk * (1 - Korting);
}

/// <summary>Wat Solar over één artikel teruggeeft.</summary>
public class SolarPrijs
{
    /// <summary>Solar kent het artikel onder ons nummer.</summary>
    public bool Gevonden { get; set; }
    public string SapNummer { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    /// <summary>Status van het product bij Solar (10 = gewoon leverbaar; andere waarden laten we zien).</summary>
    public string Status { get; set; } = "";
    public decimal? BrutoPerStuk { get; set; }
    public decimal? NettoPerStuk { get; set; }
    public string Eenheid { get; set; } = "";
    /// <summary>Waarom er geen (volledige) prijs is.</summary>
    public string Opmerking { get; set; } = "";
}

/// <summary>
/// Solar Procurement API (apiportal.solar.eu), alleen lezen: prijzen van onze Solar-artikelen ophalen om ze met onze
/// eigen prijzen te vergelijken (Artikelen ▸ Solar-prijzen). Uitgezocht en geprobeerd op 20-11-2026:
///
/// - Aanmelden: OAuth2 client credentials bij de identiteitsdienst van Solar (Azure B2C), token 60 minuten geldig.
/// - Het nummer dat wij in ArtikelSupplier hebben (7 cijfers) is bij Solar het electricalNumber (elektrotechnisch) of het
///   hwsNumber (installatie); niet elk artikel heeft beide. Prijzen geeft Solar alleen op zijn eigen SAP-nummer (10
///   cijfers): eerst het product ophalen (eerst op electricalNumber, dan op hwsNumber), dan de prijzen op
///   SAPMaterialNumber. Alleen op hwsNumber zoeken vond 425 van de 721 nummers.
/// - Een artikel dat niet meer leverbaar is (status 40) komt terug met prijs 0: dat tonen we als "geen prijs".
/// - Het prijzen-adres wil een accountId (zonder: 401); /accounts geeft het ene account van MDE, dus Solar:AccountId
///   mag leeg blijven.
/// - Een prijs komt als bedrag per PriceQuantity eenheden (bv. 883 per 100 meter); wij rekenen terug naar één eenheid.
///
/// De SAP-nummers worden in het geheugen onthouden zolang de app draait: de tweede keer scheelt dat een aanroep per artikel.
/// </summary>
public class SolarKoppeling
{
    private const string TokenAdres = "https://sgidp.b2clogin.com/sgidp.onmicrosoft.com/b2c_1a_client_credentials/oauth2/v2.0/token";
    private const string Scope = "https://sgidp.onmicrosoft.com/api.procurement/.default";
    private const string Basis = "https://api.solar.eu/procurement/V1";
    private const int PrijzenPerKeer = 50, Tegelijk = 4;

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<SolarKoppeling> _log;
    private readonly SemaphoreSlim _tokenSlot = new(1, 1);
    private string? _token;
    private DateTime _tokenTot;
    private string? _account;
    /// <summary>Ons nummer → wat Solar erover zei (zonder prijs); null = Solar kent het nummer niet.</summary>
    private readonly ConcurrentDictionary<string, (SolarPrijs? Product, DateTime Op)> _producten = new();

    public SolarKoppeling(IHttpClientFactory http, IConfiguration config, ILogger<SolarKoppeling> log)
    { _http = http; _config = config; _log = log; }

    private string ClientId => (_config["Solar:ClientId"] ?? "").Trim();
    private string ClientSecret => (_config["Solar:ClientSecret"] ?? "").Trim();
    private string Land => (_config["Solar:Landcode"] ?? "NL").Trim() is { Length: > 0 } l ? l : "NL";
    private string Catalogus => (_config["Solar:CatalogId"] ?? "").Trim();

    public bool Ingesteld => ClientId.Length > 0 && ClientId != "VUL-IN" && ClientSecret.Length > 0 && Catalogus.Length > 0;

    /// <summary>Relations.id_supplier van Solar Nederland.</summary>
    public int LeverancierId => int.TryParse(_config["Solar:LeverancierId"], out var id) ? id : 0;

    private async Task<string> TokenAsync(CancellationToken stop)
    {
        if (_token is not null && DateTime.Now < _tokenTot) return _token;
        await _tokenSlot.WaitAsync(stop);
        try
        {
            if (_token is not null && DateTime.Now < _tokenTot) return _token;
            using var vraag = new HttpRequestMessage(HttpMethod.Post, TokenAdres)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials", ["client_id"] = ClientId, ["client_secret"] = ClientSecret, ["scope"] = Scope,
                }),
            };
            using var antwoord = await _http.CreateClient().SendAsync(vraag, stop);
            var tekst = await antwoord.Content.ReadAsStringAsync(stop);
            if (!antwoord.IsSuccessStatusCode)
            {
                // de foutcode van de identiteitsdienst (bv. invalid_client), nooit het secret
                var code = "";
                try { code = JsonDocument.Parse(tekst).RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "" : ""; } catch { }
                throw new InvalidOperationException($"Aanmelden bij Solar mislukt ({(int)antwoord.StatusCode}{(code.Length > 0 ? " " + code : "")}). Kloppen Solar:ClientId en Solar:ClientSecret in appsettings?");
            }
            using var doc = JsonDocument.Parse(tekst);
            _token = doc.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Solar gaf geen token terug.");
            var seconden = doc.RootElement.TryGetProperty("expires_in", out var s) && s.TryGetInt32(out var n) ? n : 3600;
            _tokenTot = DateTime.Now.AddSeconds(Math.Max(60, seconden - 120));
            return _token;
        }
        finally { _tokenSlot.Release(); }
    }

    /// <summary>Een aanroep van de API; bij 429 (te druk) een paar keer opnieuw.</summary>
    private async Task<(HttpStatusCode Code, string Tekst)> RoepAsync(HttpMethod methode, string pad, string? body, CancellationToken stop)
    {
        for (var poging = 0; ; poging++)
        {
            using var vraag = new HttpRequestMessage(methode, Basis + pad);
            vraag.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(stop));
            vraag.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null) vraag.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var antwoord = await _http.CreateClient().SendAsync(vraag, stop);
            var tekst = await antwoord.Content.ReadAsStringAsync(stop);
            if ((antwoord.StatusCode == HttpStatusCode.TooManyRequests || (int)antwoord.StatusCode >= 500) && poging < 4)
            {      // te druk, of een storing aan de kant van Solar (503 kwam bij het proberen een keer voor): even wachten
                await Task.Delay(TimeSpan.FromSeconds(2 * (poging + 1)), stop);
                continue;
            }
            if (antwoord.StatusCode == HttpStatusCode.Unauthorized && poging == 0 && !tekst.Contains("access to the account"))
            {
                _token = null;      // token verlopen of ingetrokken: één keer opnieuw aanmelden
                continue;
            }
            return (antwoord.StatusCode, tekst);
        }
    }

    private static string Tekst(JsonElement o, string naam)
    {
        foreach (var p in o.EnumerateObject())
            if (p.Name.Equals(naam, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ValueKind == JsonValueKind.Null ? "" : p.Value.ToString();
        return "";
    }

    private static decimal? Getal(JsonElement o, string naam)
    {
        foreach (var p in o.EnumerateObject())
            if (p.Name.Equals(naam, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out var d) ? d
                     : decimal.TryParse(p.Value.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null;
        return null;
    }

    /// <summary>Het accountnummer van MDE bij Solar: uit de instellingen, anders het eerste dat Solar teruggeeft.</summary>
    public async Task<string> AccountAsync(CancellationToken stop = default)
    {
        if ((_config["Solar:AccountId"] ?? "").Trim() is { Length: > 0 } ingesteld) return ingesteld;
        if (_account is not null) return _account;
        var (code, tekst) = await RoepAsync(HttpMethod.Get, $"/accounts?countrycode={Land}", null, stop);
        if (code != HttpStatusCode.OK) throw new InvalidOperationException($"Solar gaf geen accounts terug ({(int)code}).");
        using var doc = JsonDocument.Parse(tekst);
        var eerste = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0 ? Tekst(doc.RootElement[0], "AccountId") : "";
        if (eerste.Length == 0) throw new InvalidOperationException("Er hangt geen account aan deze Solar-aanmelding.");
        return _account = eerste;
    }

    /// <summary>Het product bij ons nummer (HwsNumber); null = Solar kent het nummer niet. Een dag onthouden.</summary>
    private async Task<SolarPrijs?> ProductAsync(string nummer, CancellationToken stop)
    {
        if (_producten.TryGetValue(nummer, out var bekend) && DateTime.Now - bekend.Op < TimeSpan.FromHours(24))
            return bekend.Product is null ? null : Kopie(bekend.Product);
        SolarPrijs? product = null;
        foreach (var soort in new[] { "electricalNumber", "hwsNumber" })
        {
            var (code, tekst) = await RoepAsync(HttpMethod.Get,
                $"/catalogs/{Uri.EscapeDataString(Catalogus)}/products/{Uri.EscapeDataString(nummer)}?countrycode={Land}&productIdentifier={soort}", null, stop);
            if (code == HttpStatusCode.OK)
            {
                using var doc = JsonDocument.Parse(tekst);
                var o = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().FirstOrDefault() : doc.RootElement;
                if (o.ValueKind == JsonValueKind.Object && Tekst(o, "SapMaterialNumber") is { Length: > 0 } sap)
                    product = new SolarPrijs { Gevonden = true, SapNummer = sap, Omschrijving = Tekst(o, "MaterialDescription"), Status = Tekst(o, "ProductStatus") };
            }
            else if (code is not (HttpStatusCode.BadRequest or HttpStatusCode.NotFound))
                throw new InvalidOperationException($"Solar gaf een fout bij artikel {nummer} ({(int)code}).");
            if (product is not null) break;
        }
        _producten[nummer] = (product, DateTime.Now);
        return product is null ? null : Kopie(product);
    }

    private static SolarPrijs Kopie(SolarPrijs p) => new() { Gevonden = p.Gevonden, SapNummer = p.SapNummer, Omschrijving = p.Omschrijving, Status = p.Status };

    /// <summary>
    /// De prijzen van Solar voor deze artikelnummers (onze nummers). Geeft per nummer wat Solar zei; een nummer dat Solar
    /// niet kent komt terug met Gevonden = false. <paramref name="voortgang"/> krijgt (klaar, totaal).
    /// </summary>
    public async Task<Dictionary<string, SolarPrijs>> PrijzenAsync(IReadOnlyCollection<string> nummers, IProgress<(int Klaar, int Totaal)>? voortgang = null,
                                                                 CancellationToken stop = default)
    {
        if (!Ingesteld) throw new InvalidOperationException("De Solar-koppeling is niet ingesteld (appsettings, sectie Solar).");
        var uniek = nummers.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct().ToList();
        var uit = new ConcurrentDictionary<string, SolarPrijs>();
        var account = await AccountAsync(stop);
        var klaar = 0;
        var totaal = uniek.Count;

        // 1. per nummer het SAP-nummer opzoeken, een paar tegelijk
        using var poort = new SemaphoreSlim(Tegelijk);
        await Task.WhenAll(uniek.Select(async nummer =>
        {
            await poort.WaitAsync(stop);
            try
            {
                // alleen cijfers kan Solar opzoeken; "2170938/100" en dergelijke zijn eigen notaties
                var product = nummer.All(char.IsDigit) ? await ProductAsync(nummer, stop) : null;
                uit[nummer] = product ?? new SolarPrijs
                {
                    Opmerking = nummer.All(char.IsDigit) ? "Solar kent dit artikelnummer niet (meer)" : "geen gewoon Solar-artikelnummer",
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Solar: artikel {Nummer} opzoeken mislukt", nummer);
                uit[nummer] = new SolarPrijs { Opmerking = "opzoeken mislukt: " + ex.Message };
            }
            finally
            {
                poort.Release();
                voortgang?.Report((Interlocked.Increment(ref klaar), totaal));
            }
        }));

        // 2. de prijzen, in groepen, op het SAP-nummer
        var metSap = uit.Values.Where(p => p.Gevonden).GroupBy(p => p.SapNummer).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var groep in metSap.Keys.Chunk(PrijzenPerKeer))
        {
            stop.ThrowIfCancellationRequested();
            var body = JsonSerializer.Serialize(new { productIdentifier = "SAPMaterialNumber", products = groep.Select(s => new { productId = s }) });
            var (code, tekst) = await RoepAsync(HttpMethod.Post, $"/products/prices?accountId={Uri.EscapeDataString(account)}&countrycode={Land}", body, stop);
            if (code != HttpStatusCode.OK)
            {
                _log.LogWarning("Solar: prijzen ophalen gaf {Code}", (int)code);
                foreach (var s in groep) foreach (var p in metSap[s]) p.Opmerking = $"prijs ophalen mislukt ({(int)code})";
                continue;
            }
            using var doc = JsonDocument.Parse(tekst);
            foreach (var regel in doc.RootElement.EnumerateArray())
            {
                if (!metSap.TryGetValue(Tekst(regel, "ProductId"), out var doelen)) continue;
                decimal? bruto = null, netto = null;
                var eenheid = "";
                foreach (var p in regel.EnumerateObject().Where(x => x.Name.Equals("Prices", StringComparison.OrdinalIgnoreCase) && x.Value.ValueKind == JsonValueKind.Array)
                                       .SelectMany(x => x.Value.EnumerateArray()))
                {
                    var per = Getal(p, "PriceQuantity") is decimal q && q > 0 ? q : 1;
                    var prijs = Getal(p, "Price") is decimal v ? v / per : (decimal?)null;
                    var soort = Tekst(p, "PriceType");
                    if (soort.Equals("ListPrice", StringComparison.OrdinalIgnoreCase)) bruto = prijs;
                    else if (soort.Equals("NetPrice", StringComparison.OrdinalIgnoreCase)) netto = prijs;
                    if (eenheid.Length == 0) eenheid = Tekst(p, "UnitCode");
                }
                // prijs 0 = Solar levert het niet (meer): geen prijs, in plaats van "100% goedkoper"
                if (bruto is <= 0) bruto = null;
                if (netto is <= 0) netto = null;
                foreach (var d in doelen)
                {
                    d.BrutoPerStuk = bruto; d.NettoPerStuk = netto; d.Eenheid = eenheid;
                    if (netto is null) d.Opmerking = "Solar geeft geen prijs" + (d.Status is { Length: > 0 } st && st != "10" ? $" (status {st}: niet meer leverbaar?)" : "");
                }
            }
        }
        _log.LogInformation("Solar: prijzen opgehaald voor {Aantal} artikelnummers, {Gevonden} bekend bij Solar", totaal, metSap.Values.Sum(l => l.Count));
        return new Dictionary<string, SolarPrijs>(uit);
    }
}
