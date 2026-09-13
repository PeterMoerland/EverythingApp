using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EverythingApp.Services;

/// <summary>Eén afspraak uit de agenda.</summary>
public record AgendaItem(string Id, string Titel, DateTime Start, DateTime Eind,
                         bool HeleDag, string Locatie, string? Link);

/// <summary>
/// Praat met de Google Calendar API namens de ingelogde gebruiker.
/// Gebruikt de gewone REST-API met het access-token; geen extra pakket nodig.
/// </summary>
public class AgendaService
{
    private const string Basis = "https://www.googleapis.com/calendar/v3/calendars/primary/events";

    private readonly IHttpClientFactory _http;
    private readonly AgendaTokens _tokens;
    private readonly ILogger<AgendaService> _log;

    public AgendaService(IHttpClientFactory http, AgendaTokens tokens, ILogger<AgendaService> log)
    {
        _http = http;
        _tokens = tokens;
        _log = log;
    }

    public bool Beschikbaar(string? email) => _tokens.HeeftToegang(email);

    private async Task<HttpClient?> ClientAsync(string? email)
    {
        var token = await _tokens.TokenAsync(email);
        if (token is null) return null;
        var client = _http.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Afspraken tussen twee momenten, op tijd gesorteerd.</summary>
    public async Task<List<AgendaItem>> AfsprakenAsync(string? email, DateTime van, DateTime tot)
    {
        var lijst = new List<AgendaItem>();
        var client = await ClientAsync(email);
        if (client is null) return lijst;

        var url = $"{Basis}?singleEvents=true&orderBy=startTime&maxResults=100" +
                  $"&timeMin={Uri.EscapeDataString(van.ToUniversalTime().ToString("o"))}" +
                  $"&timeMax={Uri.EscapeDataString(tot.ToUniversalTime().ToString("o"))}";

        try
        {
            var antwoord = await client.GetAsync(url);
            var inhoud = await antwoord.Content.ReadAsStringAsync();

            if (!antwoord.IsSuccessStatusCode)
            {
                _log.LogWarning("Agenda ophalen mislukte: {Status} {Body}", antwoord.StatusCode, inhoud);
                throw new InvalidOperationException(Foutmelding(antwoord.StatusCode, inhoud));
            }

            using var doc = JsonDocument.Parse(inhoud);
            if (!doc.RootElement.TryGetProperty("items", out var items)) return lijst;

            foreach (var item in items.EnumerateArray())
            {
                var id = Tekst(item, "id") ?? "";
                var titel = Tekst(item, "summary") ?? "(geen titel)";
                var locatie = Tekst(item, "location") ?? "";
                var link = Tekst(item, "htmlLink");

                var (start, heleDag) = Moment(item, "start");
                var (eind, _) = Moment(item, "end");
                if (start is null) continue;

                lijst.Add(new AgendaItem(id, titel, start.Value, eind ?? start.Value, heleDag, locatie, link));
            }
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Agenda ophalen mislukte");
            throw new InvalidOperationException("Agenda niet bereikbaar: " + ex.Message);
        }

        return lijst.OrderBy(a => a.Start).ToList();
    }

    /// <summary>Zet de foutmelding van Google om in iets waar je wat mee kunt.</summary>
    private static string Foutmelding(System.Net.HttpStatusCode status, string inhoud)
    {
        var detail = "";
        try
        {
            using var doc = JsonDocument.Parse(inhoud);
            if (doc.RootElement.TryGetProperty("error", out var fout))
                detail = Tekst(fout, "message") ?? "";
        }
        catch { /* geen JSON */ }

        var tip = (int)status switch
        {
            403 when detail.Contains("has not been used", StringComparison.OrdinalIgnoreCase)
                  || detail.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                => " Zet de Google Calendar API aan in Google Cloud Console (APIs & Services → Library).",
            403 => " Log uit en opnieuw in: de app heeft nog geen toestemming voor je agenda.",
            401 => " Log uit en opnieuw in; de toegang is verlopen.",
            _ => "",
        };

        return $"Google gaf {(int)status} terug. {detail}{tip}".Trim();
    }

    /// <summary>Maakt een afspraak. Geeft de link naar het item terug, of null.</summary>
    public async Task<string?> MaakAfspraakAsync(string? email, string titel, DateTime start, DateTime eind,
                                                 string? locatie = null, string? omschrijving = null,
                                                 IEnumerable<string>? deelnemers = null)
    {
        var client = await ClientAsync(email);
        if (client is null) return null;

        var body = new Dictionary<string, object?>
        {
            ["summary"] = titel,
            ["start"] = new { dateTime = start.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "Europe/Amsterdam" },
            ["end"] = new { dateTime = eind.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "Europe/Amsterdam" },
        };
        if (!string.IsNullOrWhiteSpace(locatie)) body["location"] = locatie;
        if (!string.IsNullOrWhiteSpace(omschrijving)) body["description"] = omschrijving;

        var gasten = deelnemers?.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => new { email = d }).ToArray();
        if (gasten is { Length: > 0 }) body["attendees"] = gasten;

        try
        {
            var json = JsonSerializer.Serialize(body);
            var antwoord = await client.PostAsync(Basis + "?sendUpdates=all",
                new StringContent(json, Encoding.UTF8, "application/json"));

            var tekst = await antwoord.Content.ReadAsStringAsync();
            if (!antwoord.IsSuccessStatusCode)
            {
                _log.LogWarning("Afspraak maken mislukte: {Status} {Body}", antwoord.StatusCode, tekst);
                throw new InvalidOperationException($"Google gaf {(int)antwoord.StatusCode} terug.");
            }

            using var doc = JsonDocument.Parse(tekst);
            return Tekst(doc.RootElement, "htmlLink");
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Afspraak maken mislukte");
            throw;
        }
    }

    // ---- hulpjes ----
    private static string? Tekst(JsonElement e, string naam) =>
        e.TryGetProperty(naam, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Leest 'start' of 'end': dateTime (met tijd) of date (hele dag).</summary>
    private static (DateTime? Moment, bool HeleDag) Moment(JsonElement item, string naam)
    {
        if (!item.TryGetProperty(naam, out var deel)) return (null, false);

        var metTijd = Tekst(deel, "dateTime");
        if (metTijd is not null && DateTimeOffset.TryParse(metTijd, out var dto))
            return (dto.LocalDateTime, false);

        var alleenDatum = Tekst(deel, "date");
        if (alleenDatum is not null &&
            DateTime.TryParseExact(alleenDatum, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var d))
            return (d, true);

        return (null, false);
    }
}
