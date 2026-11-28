using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace EverythingApp.Services;

/// <summary>Een ruimte (zone) van Tado zoals het scherm hem toont.</summary>
public record TadoRuimte(int Id, string Naam, string Soort, double? Temperatuur, double? Vocht, double? Ingesteld,
                         bool Aan, int? Vraag, bool RaamOpen, bool Handmatig, bool Verbonden);

/// <summary>De laatst gelezen stand van alle ruimtes. AanvragenOver = wat Tado vandaag nog toelaat (null = onbekend).</summary>
public record TadoStand(DateTime Gelezen, List<TadoRuimte> Ruimtes, int? AanvragenOver);

/// <summary>Een blok uit het weekschema: van-tot (HH:mm; een dag eindigt op 00:00), verwarming aan met een temperatuur, of uit.</summary>
public record TadoBlok(string Dag, string Van, string Tot, bool Aan, double? Graden, bool Geo);

/// <summary>Het actieve weekschema van een ruimte. Soort: 0 = alle dagen gelijk, 1 = ma-vr / za / zo, 2 = elke dag apart.</summary>
public record TadoSchema(int Ruimte, int Soort, List<TadoBlok> Blokken);

/// <summary>De code waarmee iemand de koppeling in zijn eigen browser bij Tado goedkeurt.</summary>
public record TadoKoppelcode(string Code, string Url, DateTime Verloopt);

/// <summary>
/// De koppeling met de Tado-thermostaten (Gebouwbeheer ▸ Verwarming ▸ Tado; verzoek van Peter 27-11-2026).
///
/// Tado heeft geen lokale API; alles loopt via de cloud (my.tado.com). Aanmelden gaat sinds maart 2025 alleen nog met
/// een eenmalige koppeling: de app vraagt een code aan, iemand met het Tado-account keurt die in zijn eigen browser
/// goed, en de app krijgt een sleutel (refresh-token). Het wachtwoord van Tado komt dus nooit in de app. De sleutel
/// wisselt bij elk gebruik en is 30 dagen geldig; hij staat versleuteld in C:\ProgramData\MDE\tado-token.json
/// (Data Protection, zoals de Google-aanmeldingen in AgendaTokens).
///
/// Tado staat maar een beperkt aantal aanvragen per dag toe (zonder abonnement 100). Daarom wordt alleen gelezen als
/// iemand het scherm open heeft, en dan hooguit eens per Tado:VerversMinuten (standaard 20); alle kijkers delen
/// dezelfde stand. Tado:Geheugen = true geeft een vaste proefstand zonder Tado te raken (lokaal testen).
/// </summary>
public class TadoKoppeling
{
    // De vaste client-id die Tado voor deze aanmeldmethode voorschrijft (openbaar, geen geheim).
    private const string ClientId = "1bb50063-6b0c-4d11-bd99-387f4a91cc46";
    private const string Aanmelden = "https://login.tado.com/oauth2/";
    private const string Api = "https://my.tado.com/api/v2/";

    private record Bewaard(string Refresh, int? HomeId);

    private readonly IHttpClientFactory _http;
    private readonly ILogger<TadoKoppeling> _log;
    private readonly IDataProtector? _protector;
    private readonly string _bestand;
    private readonly bool _geheugen;
    private readonly TimeSpan _ververs;
    private readonly SemaphoreSlim _slot = new(1, 1);

    private string? _refresh;
    private int? _homeId;
    private string? _access;
    private DateTime _accessTot = DateTime.MinValue;
    private Dictionary<int, (string Naam, string Soort)> _zones = new();
    private DateTime _zonesGelezen = DateTime.MinValue;
    private TadoStand? _stand;
    private DateTime _laatstePoging = DateTime.MinValue;
    private int? _over;
    private CancellationTokenSource? _koppelStop;

    public TadoKoppeling(IHttpClientFactory http, IConfiguration config, ILogger<TadoKoppeling> log,
                         IDataProtectionProvider? bescherming = null)
    {
        _http = http;
        _log = log;
        _geheugen = config.GetValue<bool>("Tado:Geheugen");
        _ververs = TimeSpan.FromMinutes(Math.Clamp(config.GetValue<int?>("Tado:VerversMinuten") ?? 20, 2, 240));
        _protector = bescherming?.CreateProtector("MDE.Tado.v1");
        _bestand = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MDE", "tado-token.json");
        if (!_geheugen) LeesBestand();
    }

    /// <summary>Er is een bewaarde koppeling (of de proefstand staat aan).</summary>
    public bool Gekoppeld => _geheugen || _refresh is not null;

    /// <summary>De code die nu op goedkeuring wacht; null = er loopt geen koppeling.</summary>
    public TadoKoppelcode? Koppelcode { get; private set; }

    /// <summary>De laatste fout in gewone taal; leeg = geen.</summary>
    public string Fout { get; private set; } = "";

    public int VerversMinuten => (int)_ververs.TotalMinutes;

    /// <summary>De koppeling of de stand is veranderd (schermen tekenen opnieuw).</summary>
    public event Action? Gewijzigd;

    // ---- bewaren op schijf ----
    private void LeesBestand()
    {
        if (_protector is null) return;
        try
        {
            if (!File.Exists(_bestand)) return;
            var b = JsonSerializer.Deserialize<Bewaard>(_protector.Unprotect(File.ReadAllText(_bestand)));
            if (b is null || string.IsNullOrEmpty(b.Refresh)) return;
            _refresh = b.Refresh; _homeId = b.HomeId;
            _log.LogInformation("Tado: bewaarde koppeling ingelezen.");
        }
        catch (Exception ex) { _log.LogWarning(ex, "Tado: bewaarde koppeling niet te lezen ({Bestand})", _bestand); }
    }

    private void SchrijfBestand()
    {
        if (_protector is null) return;
        try
        {
            if (_refresh is null) { if (File.Exists(_bestand)) File.WriteAllText(_bestand, ""); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(_bestand)!);
            File.WriteAllText(_bestand, _protector.Protect(JsonSerializer.Serialize(new Bewaard(_refresh, _homeId))));
        }
        catch (Exception ex) { _log.LogWarning(ex, "Tado: koppeling niet te bewaren ({Bestand})", _bestand); }
    }

    // ---- koppelen ----

    /// <summary>Vraagt bij Tado een code aan en wacht op de achtergrond tot iemand die goedkeurt.</summary>
    public async Task<TadoKoppelcode?> StartKoppelenAsync()
    {
        if (_geheugen) return null;
        _koppelStop?.Cancel();
        Fout = "";
        try
        {
            var client = _http.CreateClient();
            using var antwoord = await client.PostAsync(Aanmelden + "device_authorize", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId, ["scope"] = "offline_access",
            }));
            if (!antwoord.IsSuccessStatusCode) { Fout = $"Tado gaf geen code ({(int)antwoord.StatusCode})."; return null; }
            using var doc = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync());
            var r = doc.RootElement;
            var apparaat = Tekst(r, "device_code");
            var code = Tekst(r, "user_code");
            var url = Tekst(r, "verification_uri_complete");
            if (url.Length == 0) url = Tekst(r, "verification_uri");
            if (apparaat.Length == 0 || code.Length == 0 || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            { Fout = "Tado gaf een onverwacht antwoord op de aanvraag."; return null; }
            var geldig = r.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 300;
            var wacht = r.TryGetProperty("interval", out var i) && i.TryGetInt32(out var w) ? Math.Clamp(w, 3, 30) : 5;
            Koppelcode = new TadoKoppelcode(code, url, DateTime.Now.AddSeconds(geldig));
            _koppelStop = new CancellationTokenSource(TimeSpan.FromSeconds(geldig));
            _ = WachtOpGoedkeuringAsync(apparaat, wacht, _koppelStop.Token);
            return Koppelcode;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tado: code aanvragen mislukt");
            Fout = "Tado is niet bereikbaar: " + ex.Message;
            return null;
        }
        finally { Gewijzigd?.Invoke(); }
    }

    private async Task WachtOpGoedkeuringAsync(string apparaat, int wacht, CancellationToken stop)
    {
        try
        {
            var client = _http.CreateClient();
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(wacht), stop);
                using var antwoord = await client.PostAsync(Aanmelden + "token", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId, ["device_code"] = apparaat,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                }), stop);
                using var doc = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync(stop));
                if (antwoord.IsSuccessStatusCode)
                {
                    await _slot.WaitAsync(stop);
                    try
                    {
                        NeemTokens(doc.RootElement);
                        _homeId = null; _stand = null; _zones = new(); _zonesGelezen = DateTime.MinValue; _laatstePoging = DateTime.MinValue;
                        SchrijfBestand();
                    }
                    finally { _slot.Release(); }
                    _log.LogInformation("Tado: gekoppeld.");
                    Fout = "";
                    return;
                }
                var fout = Tekst(doc.RootElement, "error");
                if (fout is "authorization_pending" or "slow_down") { if (fout == "slow_down") wacht += 5; continue; }
                Fout = fout == "access_denied" ? "De koppeling is bij Tado geweigerd." : "De koppeling is niet gelukt (" + fout + ").";
                return;
            }
        }
        catch (OperationCanceledException) { if (Koppelcode is not null && Koppelcode.Verloopt <= DateTime.Now.AddSeconds(2)) Fout = "De code is verlopen. Vraag een nieuwe aan."; }
        catch (Exception ex) { _log.LogWarning(ex, "Tado: wachten op goedkeuring mislukt"); Fout = "De koppeling is niet gelukt: " + ex.Message; }
        finally { Koppelcode = null; Gewijzigd?.Invoke(); }
    }

    /// <summary>Stopt een lopende koppeling (de code vervalt).</summary>
    public void StopKoppelen() => _koppelStop?.Cancel();

    /// <summary>Vergeet de bewaarde koppeling; daarna moet opnieuw gekoppeld worden.</summary>
    public async Task OntkoppelAsync()
    {
        if (_geheugen) return;
        await _slot.WaitAsync();
        try { _refresh = null; _access = null; _homeId = null; _stand = null; _zones = new(); SchrijfBestand(); }
        finally { _slot.Release(); }
        _log.LogInformation("Tado: ontkoppeld.");
        Gewijzigd?.Invoke();
    }

    private void NeemTokens(JsonElement r)
    {
        _access = Tekst(r, "access_token");
        var nieuw = Tekst(r, "refresh_token");
        if (nieuw.Length > 0) _refresh = nieuw;     // de oude is vanaf nu ongeldig: altijd de nieuwe bewaren
        var geldig = r.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 600;
        _accessTot = DateTime.UtcNow.AddSeconds(Math.Max(30, geldig - 60));
    }

    /// <summary>Haalt met de bewaarde sleutel een nieuw toegangsbewijs. Binnen _slot aanroepen.</summary>
    private async Task<bool> VernieuwAsync(HttpClient client)
    {
        if (_refresh is null) return false;
        using var antwoord = await client.PostAsync(Aanmelden + "token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId, ["grant_type"] = "refresh_token", ["refresh_token"] = _refresh,
        }));
        var tekst = await antwoord.Content.ReadAsStringAsync();
        if (antwoord.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(tekst);
            NeemTokens(doc.RootElement);
            SchrijfBestand();
            return true;
        }
        if (antwoord.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            // de sleutel is verlopen (30 dagen niet gebruikt) of ingetrokken: opnieuw koppelen
            _log.LogWarning("Tado: de bewaarde koppeling is niet meer geldig ({Status}).", (int)antwoord.StatusCode);
            _refresh = null; _access = null; _stand = null; SchrijfBestand();
            Fout = "De koppeling met Tado is verlopen. Koppel opnieuw.";
            return false;
        }
        Fout = $"Tado gaf een fout bij het aanmelden ({(int)antwoord.StatusCode}).";
        return false;
    }

    /// <summary>Een GET op de API. Binnen _slot aanroepen. Null = mislukt (Fout is gezet).</summary>
    private Task<JsonDocument?> HaalAsync(HttpClient client, string pad) => StuurAsync(client, HttpMethod.Get, pad, null);

    /// <summary>Een aanroep van de API, met inhoud (JSON) bij schrijven. Binnen _slot aanroepen. Null = mislukt (Fout is gezet).</summary>
    private async Task<JsonDocument?> StuurAsync(HttpClient client, HttpMethod methode, string pad, string? inhoud)
    {
        for (var poging = 0; poging < 2; poging++)
        {
            if (_access is null || DateTime.UtcNow >= _accessTot) { if (!await VernieuwAsync(client)) return null; }
            using var vraag = new HttpRequestMessage(methode, Api + pad);
            if (inhoud is not null) vraag.Content = new StringContent(inhoud, System.Text.Encoding.UTF8, "application/json");
            vraag.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _access);
            using var antwoord = await client.SendAsync(vraag);
            // "perday";r=87 = vandaag nog 87 aanvragen
            if (antwoord.Headers.TryGetValues("ratelimit", out var limiet))
                foreach (var deel in string.Join(";", limiet).Split(';'))
                    if (deel.Trim().StartsWith("r=") && int.TryParse(deel.Trim()[2..], out var over)) _over = over;
            if (antwoord.StatusCode == HttpStatusCode.Unauthorized && poging == 0) { _access = null; continue; }
            if (antwoord.StatusCode == HttpStatusCode.TooManyRequests) { _over = 0; Fout = "Tado laat vandaag geen aanvragen meer toe (daglimiet bereikt). Morgen werkt het weer."; return null; }
            if (!antwoord.IsSuccessStatusCode) { Fout = $"Tado gaf een fout ({(int)antwoord.StatusCode}) op {pad}."; return null; }
            var tekst = await antwoord.Content.ReadAsStringAsync();
            return JsonDocument.Parse(tekst.Length > 0 ? tekst : "{}");     // DELETE geeft geen inhoud
        }
        Fout = "Tado weigert het toegangsbewijs.";
        return null;
    }

    // ---- lezen ----

    /// <summary>
    /// De stand van alle ruimtes. Uit het geheugen zolang die jonger is dan VerversMinuten; nu = meteen opnieuw lezen
    /// (hooguit eens per twee minuten, vanwege de daglimiet van Tado). Null = niet gekoppeld of nog niets gelezen.
    /// </summary>
    public async Task<TadoStand?> StandAsync(bool nu = false)
    {
        if (_geheugen) return Proefstand();
        if (_refresh is null) return null;
        var oud = DateTime.Now - _laatstePoging;
        if (oud < (nu ? TimeSpan.FromMinutes(2) : _ververs)) return _stand;
        await _slot.WaitAsync();
        try
        {
            oud = DateTime.Now - _laatstePoging;
            if (_refresh is null || oud < (nu ? TimeSpan.FromMinutes(2) : _ververs)) return _stand;
            _laatstePoging = DateTime.Now;
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);

            if (_homeId is null)
            {
                using var ik = await HaalAsync(client, "me");
                if (ik is null) return _stand;
                if (ik.RootElement.TryGetProperty("homes", out var homes) && homes.ValueKind == JsonValueKind.Array && homes.GetArrayLength() > 0
                    && homes[0].TryGetProperty("id", out var hid) && hid.TryGetInt32(out var h)) { _homeId = h; SchrijfBestand(); }
                else { Fout = "Bij dit Tado-account hoort geen woning."; return _stand; }
            }

            // de namen van de ruimtes veranderen zelden: eens per dag
            if (_zones.Count == 0 || DateTime.Now - _zonesGelezen > TimeSpan.FromHours(24))
            {
                using var zones = await HaalAsync(client, $"homes/{_homeId}/zones");
                if (zones is null) return _stand;
                var lijst = new Dictionary<int, (string, string)>();
                if (zones.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var z in zones.RootElement.EnumerateArray())
                        if (z.TryGetProperty("id", out var zid) && zid.TryGetInt32(out var id))
                            lijst[id] = (Tekst(z, "name"), Tekst(z, "type"));
                _zones = lijst; _zonesGelezen = DateTime.Now;
            }

            using var standen = await HaalAsync(client, $"homes/{_homeId}/zoneStates");
            if (standen is null) return _stand;
            var ruimtes = new List<TadoRuimte>();
            if (standen.RootElement.TryGetProperty("zoneStates", out var zs) && zs.ValueKind == JsonValueKind.Object)
                foreach (var p in zs.EnumerateObject())
                {
                    if (!int.TryParse(p.Name, out var id)) continue;
                    var (naam, soort) = _zones.TryGetValue(id, out var z) ? z : ($"Ruimte {id}", "");
                    var s = p.Value;
                    var instelling = Onder(s, "setting");
                    var sensoren = Onder(s, "sensorDataPoints");
                    ruimtes.Add(new TadoRuimte(id, naam, soort,
                        Temperatuur: Getal(Onder(sensoren, "insideTemperature"), "celsius"),
                        Vocht: Getal(Onder(sensoren, "humidity"), "percentage"),
                        Ingesteld: Getal(Onder(instelling, "temperature"), "celsius"),
                        Aan: Tekst(instelling, "power") == "ON",
                        Vraag: Getal(Onder(Onder(s, "activityDataPoints"), "heatingPower"), "percentage") is { } v ? (int)Math.Round(v) : null,
                        RaamOpen: Onder(s, "openWindow").ValueKind == JsonValueKind.Object || (s.TryGetProperty("openWindowDetected", out var ow) && ow.ValueKind == JsonValueKind.True),
                        Handmatig: Tekst(s, "overlayType") == "MANUAL",
                        Verbonden: Tekst(Onder(s, "link"), "state") != "OFFLINE"));
                }
            Fout = "";
            _stand = new TadoStand(DateTime.Now, ruimtes.OrderBy(r => r.Soort == "HOT_WATER").ThenBy(r => r.Naam).ToList(), _over);
            return _stand;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tado: stand lezen mislukt");
            Fout = "Tado is niet bereikbaar: " + ex.Message;
            return _stand;
        }
        finally { _slot.Release(); }
    }

    // ---- instellen ----

    /// <summary>De grenzen waarbinnen de app een temperatuur instelt (Tado zelf staat 5 tot 25 graden toe).</summary>
    public const double Laagste = 5, Hoogste = 25;

    /// <summary>
    /// Stelt de temperatuur van een ruimte met de hand in (in halve graden). De waarde geldt tot het volgende blok van
    /// het weekschema; daarna volgt de ruimte weer het schema. Kost één aanvraag bij Tado. False = niet gelukt (zie Fout).
    /// </summary>
    public async Task<bool> ZetTemperatuurAsync(int ruimte, double graden, string door)
    {
        graden = Math.Clamp(Math.Round(graden * 2, MidpointRounding.AwayFromZero) / 2, Laagste, Hoogste);
        var celsius = graden.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        var inhoud = "{\"setting\":{\"type\":\"HEATING\",\"power\":\"ON\",\"temperature\":{\"celsius\":" + celsius + "}},"
                   + "\"termination\":{\"typeSkillBasedApp\":\"NEXT_TIME_BLOCK\"}}";
        var ok = await SchrijfAsync(ruimte, HttpMethod.Put, inhoud, r => r with { Ingesteld = graden, Aan = true, Handmatig = true });
        if (ok) _log.LogInformation("Tado: ruimte {Ruimte} op {Graden} graden gezet door {Door}.", ruimte, celsius, door);
        return ok;
    }

    /// <summary>Haalt de handmatige instelling van een ruimte weg: de ruimte volgt weer het weekschema.</summary>
    public async Task<bool> TerugNaarSchemaAsync(int ruimte, string door)
    {
        var ok = await SchrijfAsync(ruimte, HttpMethod.Delete, null, r => r with { Handmatig = false });
        if (ok)
        {
            _log.LogInformation("Tado: ruimte {Ruimte} terug naar het schema door {Door}.", ruimte, door);
            _laatstePoging = DateTime.MinValue;     // wat het schema nu voorschrijft weten we niet: bij de volgende keer kijken opnieuw lezen
        }
        return ok;
    }

    private async Task<bool> SchrijfAsync(int ruimte, HttpMethod methode, string? inhoud, Func<TadoRuimte, TadoRuimte> na)
    {
        if (!_geheugen && (_refresh is null || _homeId is null)) { Fout = "De app is niet met Tado gekoppeld."; return false; }
        await _slot.WaitAsync();
        try
        {
            var stand = _geheugen ? Proefstand() : _stand;
            if (stand?.Ruimtes.FirstOrDefault(r => r.Id == ruimte) is not { Soort: "HEATING" }) { Fout = "Deze ruimte is niet in te stellen."; return false; }
            if (!_geheugen)
            {
                var client = _http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(20);
                using var antwoord = await StuurAsync(client, methode, $"homes/{_homeId}/zones/{ruimte}/overlay", inhoud);
                if (antwoord is null) return false;
            }
            // de stand in het geheugen meteen bijwerken; opnieuw lezen zou een extra aanvraag kosten
            var nieuw = new TadoStand(stand.Gelezen, stand.Ruimtes.Select(r => r.Id == ruimte ? na(r) : r).ToList(), _geheugen ? stand.AanvragenOver : _over);
            if (_geheugen) _proef = nieuw; else _stand = nieuw;
            Fout = "";
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tado: instellen mislukt");
            Fout = "Tado is niet bereikbaar: " + ex.Message;
            return false;
        }
        finally { _slot.Release(); Gewijzigd?.Invoke(); }
    }

    /// <summary>De proefstand (Tado:Geheugen) is aan: er gaat niets naar Tado.</summary>
    public bool Proef => _geheugen;

    private TadoStand? _proef;
    private TadoStand Proefstand() => _proef ??= new(DateTime.Now, new List<TadoRuimte>
    {
        new(1, "Kantoor", "HEATING", 20.6, 44, 21, true, 35, false, false, true),
        new(2, "Kantine", "HEATING", 19.2, 48, 20, true, 60, false, true, true),
        new(3, "Werkplaats", "HEATING", 16.8, 52, 17, true, 0, true, false, true),
        new(4, "Vergaderruimte", "HEATING", 18.4, 46, null, false, 0, false, false, false),
    }, 83);

    // ---- weekschema ----

    /// <summary>De dagsoorten van een indeling, in de volgorde van het scherm.</summary>
    public static string[] Dagen(int soort) => soort switch
    {
        0 => new[] { "MONDAY_TO_SUNDAY" },
        1 => new[] { "MONDAY_TO_FRIDAY", "SATURDAY", "SUNDAY" },
        _ => new[] { "MONDAY", "TUESDAY", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY", "SUNDAY" },
    };

    private readonly Dictionary<int, (DateTime Gelezen, TadoSchema Schema)> _schemas = new();

    /// <summary>
    /// Het actieve weekschema van een ruimte. Kost twee aanvragen bij Tado; daarna een half uur uit het geheugen.
    /// Null = niet gelukt (zie Fout).
    /// </summary>
    public async Task<TadoSchema?> SchemaAsync(int ruimte)
    {
        if (!_geheugen && (_refresh is null || _homeId is null)) { Fout = "De app is niet met Tado gekoppeld."; return null; }
        await _slot.WaitAsync();
        try
        {
            if (_schemas.TryGetValue(ruimte, out var bewaard) && (_geheugen || DateTime.Now - bewaard.Gelezen < TimeSpan.FromMinutes(30))) return bewaard.Schema;
            if (_geheugen)
            {
                var proef = new TadoSchema(ruimte, 1, new List<TadoBlok>
                {
                    new("MONDAY_TO_FRIDAY", "00:00", "06:30", true, 16, false), new("MONDAY_TO_FRIDAY", "06:30", "17:30", true, 20.5, false),
                    new("MONDAY_TO_FRIDAY", "17:30", "00:00", true, 16, false),
                    new("SATURDAY", "00:00", "00:00", true, 15, false), new("SUNDAY", "00:00", "00:00", false, null, false),
                });
                _schemas[ruimte] = (DateTime.Now, proef);
                return proef;
            }
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            using var actief = await HaalAsync(client, $"homes/{_homeId}/zones/{ruimte}/schedule/activeTimetable");
            if (actief is null) return null;
            if (!(actief.RootElement.ValueKind == JsonValueKind.Object && actief.RootElement.TryGetProperty("id", out var sid) && sid.TryGetInt32(out var soort) && soort is >= 0 and <= 2))
            { Fout = "Tado gaf een onbekende indeling van het weekschema terug."; return null; }
            using var blokken = await HaalAsync(client, $"homes/{_homeId}/zones/{ruimte}/schedule/timetables/{soort}/blocks");
            if (blokken is null) return null;
            var lijst = new List<TadoBlok>();
            if (blokken.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var b in blokken.RootElement.EnumerateArray())
                {
                    var instelling = Onder(b, "setting");
                    var aan = Tekst(instelling, "power") == "ON";
                    lijst.Add(new TadoBlok(Tekst(b, "dayType"), Tekst(b, "start"), Tekst(b, "end"), aan,
                        aan ? Getal(Onder(instelling, "temperature"), "celsius") : null,
                        Onder(b, "geolocationOverride").ValueKind == JsonValueKind.True));
                }
            Fout = "";
            var schema = new TadoSchema(ruimte, soort, lijst);
            _schemas[ruimte] = (DateTime.Now, schema);
            return schema;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tado: schema lezen mislukt");
            Fout = "Tado is niet bereikbaar: " + ex.Message;
            return null;
        }
        finally { _slot.Release(); }
    }

    /// <summary>
    /// Vervangt de blokken van één dagsoort in het weekschema van een ruimte (één aanvraag bij Tado). De blokken moeten
    /// de dag precies vullen: het eerste begint om 00:00, elk volgend blok begint waar het vorige eindigt, het laatste
    /// eindigt om 00:00. False = niet opgeslagen (zie Fout).
    /// </summary>
    public async Task<bool> BewaarSchemaDagAsync(int ruimte, int soort, string dag, List<TadoBlok> blokken, string door)
    {
        if (!_geheugen && (_refresh is null || _homeId is null)) { Fout = "De app is niet met Tado gekoppeld."; return false; }
        if (!Dagen(soort).Contains(dag)) { Fout = "Deze dag hoort niet bij de indeling van het schema."; return false; }
        if (blokken.Count == 0 || blokken[0].Van != "00:00" || blokken[^1].Tot != "00:00") { Fout = "Het schema moet de hele dag vullen (van 00:00 tot 00:00)."; return false; }
        for (var i = 0; i < blokken.Count; i++)
        {
            var b = blokken[i];
            if (!TimeOnly.TryParseExact(b.Van, "HH:mm", out var van) || !TimeOnly.TryParseExact(b.Tot, "HH:mm", out var tot)) { Fout = "Een tijd in het schema is niet geldig."; return false; }
            if (i + 1 < blokken.Count && (blokken[i + 1].Van != b.Tot || tot <= van)) { Fout = "De blokken sluiten niet op elkaar aan."; return false; }
            if (b.Aan && (b.Graden is not { } g || g < Laagste || g > Hoogste)) { Fout = $"Een temperatuur moet tussen {Laagste} en {Hoogste} graden liggen."; return false; }
        }
        await _slot.WaitAsync();
        try
        {
            if (!_geheugen)
            {
                var inhoud = JsonSerializer.Serialize(blokken.Select(b => new Dictionary<string, object?>
                {
                    ["dayType"] = dag, ["start"] = b.Van, ["end"] = b.Tot, ["geolocationOverride"] = b.Geo,
                    ["setting"] = new Dictionary<string, object?>
                    {
                        ["type"] = "HEATING", ["power"] = b.Aan ? "ON" : "OFF",
                        ["temperature"] = b.Aan ? new Dictionary<string, object?> { ["celsius"] = b.Graden } : null,
                    },
                }));
                var client = _http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(20);
                using var antwoord = await StuurAsync(client, HttpMethod.Put, $"homes/{_homeId}/zones/{ruimte}/schedule/timetables/{soort}/blocks/{dag}", inhoud);
                if (antwoord is null) return false;
            }
            if (_schemas.TryGetValue(ruimte, out var bewaard) && bewaard.Schema.Soort == soort)
                _schemas[ruimte] = (bewaard.Gelezen, bewaard.Schema with { Blokken = bewaard.Schema.Blokken.Where(b => b.Dag != dag).Concat(blokken.Select(b => b with { Dag = dag })).ToList() });
            _laatstePoging = DateTime.MinValue;     // de ingestelde waarde kan nu anders zijn: bij de volgende keer kijken opnieuw lezen
            Fout = "";
            _log.LogInformation("Tado: schema van ruimte {Ruimte} ({Dag}, {Aantal} blokken) opgeslagen door {Door}.", ruimte, dag, blokken.Count, door);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tado: schema opslaan mislukt");
            Fout = "Tado is niet bereikbaar: " + ex.Message;
            return false;
        }
        finally { _slot.Release(); }
    }

    // ---- JSON-hulp: ontbrekende velden geven een lege waarde in plaats van een fout ----
    private static JsonElement Onder(JsonElement e, string naam)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(naam, out var v) ? v : default;
    private static string Tekst(JsonElement e, string naam)
        => Onder(e, naam) is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : "";
    private static double? Getal(JsonElement e, string naam)
        => Onder(e, naam) is { ValueKind: JsonValueKind.Number } v && v.TryGetDouble(out var d) ? d : null;
}
