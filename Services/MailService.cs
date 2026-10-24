using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EverythingApp.Services;

/// <summary>Eén bericht uit Gmail, zoals het in een lijstje past.</summary>
public record MailBericht(string Id, string ThreadId, string Van, string Naar, string Onderwerp,
                          DateTime? Datum, string Samenvatting, bool Ongelezen,
                          List<MailBijlage> Bijlagen)
{
    /// <summary>Naam zonder e-mailadres: 'Jan Jansen &lt;j@x.nl&gt;' → 'Jan Jansen'.</summary>
    public string VanKort
    {
        get
        {
            var haak = Van.IndexOf('<');
            var naam = haak > 0 ? Van[..haak].Trim().Trim('"') : Van;
            return string.IsNullOrWhiteSpace(naam) ? Van : naam;
        }
    }
}

public record MailBijlage(string Id, string Naam, long Grootte);

/// <summary>De uitgeklapte inhoud van een bericht.</summary>
public record MailInhoud(MailBericht Bericht, string Tekst);

/// <summary>
/// Praat met de Gmail-API. Welke postbus, bepaalt hoe we aan het token komen:
///
///   eigen postbus    → het token dat de gebruiker bij het inloggen zelf gaf
///   gedeelde postbus → een serviceaccount met domeinbrede delegatie
///
/// De rest van de aanroepen is identiek, dus het scherm merkt er niets van.
/// </summary>
public class MailService
{
    private const string Basis = "https://gmail.googleapis.com/gmail/v1/users/me";

    private const string Scopes = "https://www.googleapis.com/auth/gmail.readonly " +
                                  "https://www.googleapis.com/auth/gmail.send " +
                                  "https://www.googleapis.com/auth/gmail.modify";

    private readonly IHttpClientFactory _http;
    private readonly AgendaTokens _tokens;
    private readonly GoogleServiceAccount _serviceaccount;
    private readonly ILogger<MailService> _log;

    public MailService(IHttpClientFactory http, AgendaTokens tokens,
                       GoogleServiceAccount serviceaccount, ILogger<MailService> log)
    {
        _http = http;
        _tokens = tokens;
        _serviceaccount = serviceaccount;
        _log = log;
    }

    /// <summary>De eigen postbus van een gebruiker.</summary>
    public static MailPostbus Eigen(string email) => new("Mijn mail", email, null, Eigen: true);

    /// <summary>Kan deze postbus geopend worden?</summary>
    public bool Beschikbaar(MailPostbus postbus)
        => postbus.Eigen ? _tokens.HeeftToegang(postbus.Adres) : _serviceaccount.Beschikbaar;

    /// <summary>Uitleg als een postbus niet open kan — voor het scherm.</summary>
    public string Beschrijf(MailPostbus postbus)
        => postbus.Eigen
            ? "Log uit en opnieuw in; dan vraagt Google toestemming voor je mail."
            : _serviceaccount.Beschrijf();

    private async Task<HttpClient?> ClientAsync(MailPostbus postbus)
    {
        var token = postbus.Eigen
            ? await _tokens.TokenAsync(postbus.Adres)
            : await _serviceaccount.TokenAsync(postbus.Adres, Scopes);

        if (token is null) return null;

        var client = _http.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpClient> ClientOfFoutAsync(MailPostbus postbus)
        => await ClientAsync(postbus)
           ?? throw new InvalidOperationException($"Kon {postbus.Naam} niet openen. {Beschrijf(postbus)}");

    // ============================================================
    //  Lijst en zoeken
    // ============================================================

    /// <summary>
    /// Berichten met een Gmail-zoekopdracht — dezelfde taal als het zoekvak in
    /// Gmail, bijvoorbeeld 'in:inbox is:unread' of 'from:@klant.nl'.
    /// </summary>
    public async Task<List<MailBericht>> ZoekAsync(MailPostbus postbus, string zoekopdracht, int aantal = 25)
    {
        if (string.IsNullOrWhiteSpace(zoekopdracht)) return new List<MailBericht>();

        var client = await ClientOfFoutAsync(postbus);

        var url = $"{Basis}/messages?maxResults={Math.Clamp(aantal, 1, 100)}" +
                  $"&q={Uri.EscapeDataString(zoekopdracht)}";

        var antwoord = await client.GetAsync(url);
        var inhoud = await antwoord.Content.ReadAsStringAsync();
        if (!antwoord.IsSuccessStatusCode)
        {
            _log.LogWarning("Gmail zoeken mislukte: {Status} {Body}", antwoord.StatusCode, inhoud);
            throw new InvalidOperationException(Foutmelding(antwoord.StatusCode, inhoud, postbus));
        }

        using var doc = JsonDocument.Parse(inhoud);
        if (!doc.RootElement.TryGetProperty("messages", out var berichten)) return new List<MailBericht>();

        // Gmail geeft bij zoeken alleen id's terug; de kopregels moeten er per bericht
        // bij. Dat doen we naast elkaar, anders duurt een lijst van 25 te lang.
        var taken = berichten.EnumerateArray()
            .Select(k => Tekst(k, "id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => HaalOpAsync(client, id!))
            .ToList();

        var uit = new List<MailBericht>();
        foreach (var bericht in await Task.WhenAll(taken))
            if (bericht is not null) uit.Add(bericht.Bericht);

        return uit.OrderByDescending(b => b.Datum ?? DateTime.MinValue).ToList();
    }

    /// <summary>Eén bericht inclusief de tekst.</summary>
    public async Task<MailInhoud?> InhoudAsync(MailPostbus postbus, string berichtId)
    {
        var client = await ClientOfFoutAsync(postbus);
        return await HaalOpAsync(client, berichtId);
    }

    /// <summary>Haalt het volledige bericht op en pelt het uit.</summary>
    private async Task<MailInhoud?> HaalOpAsync(HttpClient client, string berichtId)
    {
        try
        {
            // format=full: nodig voor de tekst én voor de bijlagen. Met 'metadata'
            // krijg je alleen kopregels en zie je geen bijlagen.
            var antwoord = await client.GetAsync($"{Basis}/messages/{berichtId}?format=full");
            if (!antwoord.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync());
            var wortel = doc.RootElement;

            var threadId = Tekst(wortel, "threadId") ?? berichtId;
            var samenvatting = Tekst(wortel, "snippet") ?? "";

            var ongelezen = false;
            if (wortel.TryGetProperty("labelIds", out var labels) && labels.ValueKind == JsonValueKind.Array)
                ongelezen = labels.EnumerateArray().Any(l => l.GetString() == "UNREAD");

            string van = "", naar = "", onderwerp = "";
            DateTime? datum = null;

            if (wortel.TryGetProperty("payload", out var payload)
                && payload.TryGetProperty("headers", out var koppen))
            {
                foreach (var kop in koppen.EnumerateArray())
                {
                    var naam = Tekst(kop, "name") ?? "";
                    var waarde = Tekst(kop, "value") ?? "";
                    if (naam.Equals("From", StringComparison.OrdinalIgnoreCase)) van = waarde;
                    else if (naam.Equals("To", StringComparison.OrdinalIgnoreCase)) naar = waarde;
                    else if (naam.Equals("Subject", StringComparison.OrdinalIgnoreCase)) onderwerp = waarde;
                    else if (naam.Equals("Date", StringComparison.OrdinalIgnoreCase)
                             && DateTimeOffset.TryParse(waarde, out var dto)) datum = dto.LocalDateTime;
                }
            }

            if (datum is null && wortel.TryGetProperty("internalDate", out var intern)
                && long.TryParse(intern.GetString(), out var ms))
                datum = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

            var bijlagen = new List<MailBijlage>();
            var tekst = "";
            if (wortel.TryGetProperty("payload", out var deel))
            {
                ZoekBijlagen(deel, bijlagen);
                tekst = ZoekTekst(deel) ?? "";
            }

            var bericht = new MailBericht(berichtId, threadId, van, naar,
                                          string.IsNullOrWhiteSpace(onderwerp) ? "(geen onderwerp)" : onderwerp,
                                          datum, samenvatting, ongelezen, bijlagen);

            return new MailInhoud(bericht, string.IsNullOrWhiteSpace(tekst) ? samenvatting : tekst);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Bericht {Id} ophalen mislukte", berichtId);
            return null;
        }
    }

    /// <summary>Zoekt de leesbare tekst: eerst text/plain, anders HTML zonder opmaak.</summary>
    private static string? ZoekTekst(JsonElement deel)
    {
        var soort = Tekst(deel, "mimeType") ?? "";

        if (soort.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
            return Lichaam(deel);

        if (deel.TryGetProperty("parts", out var delen) && delen.ValueKind == JsonValueKind.Array)
        {
            // Bijlagen zijn geen berichttekst; die overslaan, anders lees je bij een
            // mail met een .txt-bijlage die bijlage in plaats van het bericht.
            var echteDelen = delen.EnumerateArray()
                .Where(d => string.IsNullOrWhiteSpace(Tekst(d, "filename")))
                .ToList();

            // Eerst de platte tekst; pas als die er niet is de HTML-versie.
            foreach (var onderdeel in echteDelen)
                if ((Tekst(onderdeel, "mimeType") ?? "").StartsWith("text/plain", StringComparison.OrdinalIgnoreCase)
                    && Lichaam(onderdeel) is { Length: > 0 } plat) return plat;

            foreach (var onderdeel in echteDelen)
                if (ZoekTekst(onderdeel) is { Length: > 0 } gevonden) return gevonden;
        }

        if (soort.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            var html = Lichaam(deel);
            return html is null ? null : ZonderHtml(html);
        }

        return null;
    }

    private static string? Lichaam(JsonElement deel)
    {
        if (!deel.TryGetProperty("body", out var body)) return null;
        var data = Tekst(body, "data");
        if (string.IsNullOrWhiteSpace(data)) return null;
        try { return Encoding.UTF8.GetString(VanBase64Url(data!)); }
        catch { return null; }
    }

    /// <summary>Heel eenvoudige opschoning: tags eruit, entiteiten terug, regels netjes.</summary>
    private static string ZonderHtml(string html)
    {
        var zonder = System.Text.RegularExpressions.Regex.Replace(
            html, @"<(script|style)[^>]*>.*?</\1>", " ",
            System.Text.RegularExpressions.RegexOptions.Singleline |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        zonder = System.Text.RegularExpressions.Regex.Replace(zonder, @"<br\s*/?>|</p>|</div>|</tr>", "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        zonder = System.Text.RegularExpressions.Regex.Replace(zonder, "<[^>]+>", "");
        zonder = System.Net.WebUtility.HtmlDecode(zonder);
        zonder = System.Text.RegularExpressions.Regex.Replace(zonder, @"[ \t]+", " ");
        zonder = System.Text.RegularExpressions.Regex.Replace(zonder, @"\n{3,}", "\n\n");
        return zonder.Trim();
    }

    /// <summary>
    /// Zit dit onderdeel in de tekst zelf (zoals een logo in een ondertekening)?
    /// Die willen we niet als bijlage tonen, anders heeft bijna elke mail er een.
    /// </summary>
    private static bool IsInline(JsonElement deel)
    {
        if (!deel.TryGetProperty("headers", out var koppen) || koppen.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var kop in koppen.EnumerateArray())
            if ((Tekst(kop, "name") ?? "").Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase))
                return (Tekst(kop, "value") ?? "").TrimStart()
                       .StartsWith("inline", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    /// <summary>Loopt door de onderdelen van een bericht en verzamelt de bijlagen.</summary>
    private static void ZoekBijlagen(JsonElement deel, List<MailBijlage> uit)
    {
        var naam = Tekst(deel, "filename");
        if (!string.IsNullOrWhiteSpace(naam) && !IsInline(deel)
            && deel.TryGetProperty("body", out var body)
            && body.TryGetProperty("attachmentId", out var bijlageId))
        {
            var grootte = body.TryGetProperty("size", out var s) && s.TryGetInt64(out var g) ? g : 0;
            uit.Add(new MailBijlage(bijlageId.GetString() ?? "", naam!, grootte));
        }

        if (deel.TryGetProperty("parts", out var delen) && delen.ValueKind == JsonValueKind.Array)
            foreach (var onderdeel in delen.EnumerateArray()) ZoekBijlagen(onderdeel, uit);
    }

    /// <summary>Inhoud van een bijlage, zodat die de filestore in kan.</summary>
    public async Task<byte[]?> BijlageAsync(MailPostbus postbus, string berichtId, string bijlageId)
    {
        var client = await ClientOfFoutAsync(postbus);

        var antwoord = await client.GetAsync($"{Basis}/messages/{berichtId}/attachments/{bijlageId}");
        var inhoud = await antwoord.Content.ReadAsStringAsync();
        if (!antwoord.IsSuccessStatusCode)
            throw new InvalidOperationException(Foutmelding(antwoord.StatusCode, inhoud, postbus));

        using var doc = JsonDocument.Parse(inhoud);
        var data = Tekst(doc.RootElement, "data");
        return data is null ? null : VanBase64Url(data);
    }

    /// <summary>Link om het bericht in Gmail zelf te openen, in de juiste postbus.</summary>
    public static string GmailLink(MailPostbus postbus, MailBericht bericht)
        => $"https://mail.google.com/mail/?authuser={Uri.EscapeDataString(postbus.Adres)}#all/{bericht.Id}";

    // ============================================================
    //  Opruimen: prullenbak, archiveren, gelezen
    //
    //  Wissen gaat altijd naar de prullenbak van Gmail, nooit definitief. Daar
    //  blijft het dertig dagen staan, dus een misklik is te herstellen — en de
    //  app hoeft geen machtiging te hebben om iets echt te verwijderen.
    // ============================================================

    /// <summary>Naar de prullenbak. Levert het aantal dat gelukt is.</summary>
    public async Task<int> NaarPrullenbakAsync(MailPostbus postbus, IEnumerable<string> berichtIds)
    {
        var client = await ClientOfFoutAsync(postbus);
        var gedaan = 0;

        foreach (var id in berichtIds.Where(i => !string.IsNullOrWhiteSpace(i)))
        {
            var antwoord = await client.PostAsync($"{Basis}/messages/{id}/trash", null);
            if (antwoord.IsSuccessStatusCode) { gedaan++; continue; }

            var inhoud = await antwoord.Content.ReadAsStringAsync();
            _log.LogWarning("Bericht {Id} naar de prullenbak mislukte: {Status} {Body}",
                            id, antwoord.StatusCode, inhoud);
            throw new InvalidOperationException(Foutmelding(antwoord.StatusCode, inhoud, postbus));
        }
        return gedaan;
    }

    /// <summary>Uit het postvak IN halen, maar bewaren (het label INBOX eraf).</summary>
    public Task ArchiveerAsync(MailPostbus postbus, IEnumerable<string> berichtIds)
        => LabelsAsync(postbus, berichtIds, verwijder: new[] { "INBOX" });

    /// <summary>Als gelezen markeren.</summary>
    public Task MarkeerGelezenAsync(MailPostbus postbus, IEnumerable<string> berichtIds)
        => LabelsAsync(postbus, berichtIds, verwijder: new[] { "UNREAD" });

    /// <summary>Een eigen label op berichten zetten (bv. "EverythingApp/Verwerkt"); bestaat het label niet, dan wordt het gemaakt.</summary>
    public async Task VoegLabelToeAsync(MailPostbus postbus, IEnumerable<string> berichtIds, string labelNaam)
    {
        var id = await LabelIdAsync(postbus, labelNaam);
        await LabelsAsync(postbus, berichtIds, voegToe: new[] { id });
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _labelIds = new();

    private async Task<string> LabelIdAsync(MailPostbus postbus, string naam)
    {
        var sleutel = postbus.Adres.ToLowerInvariant() + "|" + naam;
        if (_labelIds.TryGetValue(sleutel, out var bekend)) return bekend;

        var client = await ClientOfFoutAsync(postbus);
        var antwoord = await client.GetAsync($"{Basis}/labels");
        var inhoud = await antwoord.Content.ReadAsStringAsync();
        if (!antwoord.IsSuccessStatusCode)
            throw new InvalidOperationException(Foutmelding(antwoord.StatusCode, inhoud, postbus));

        using (var doc = JsonDocument.Parse(inhoud))
            if (doc.RootElement.TryGetProperty("labels", out var labels))
                foreach (var l in labels.EnumerateArray())
                    if (string.Equals(Tekst(l, "name"), naam, StringComparison.OrdinalIgnoreCase) && Tekst(l, "id") is { } gevonden)
                        return _labelIds[sleutel] = gevonden;

        var body = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["name"] = naam, ["labelListVisibility"] = "labelShow", ["messageListVisibility"] = "show",
        });
        antwoord = await client.PostAsync($"{Basis}/labels", new StringContent(body, Encoding.UTF8, "application/json"));
        inhoud = await antwoord.Content.ReadAsStringAsync();
        if (!antwoord.IsSuccessStatusCode)
            throw new InvalidOperationException(Foutmelding(antwoord.StatusCode, inhoud, postbus));
        using var nieuw = JsonDocument.Parse(inhoud);
        return _labelIds[sleutel] = Tekst(nieuw.RootElement, "id")
            ?? throw new InvalidOperationException("Gmail gaf geen id voor het nieuwe label.");
    }

    private async Task LabelsAsync(MailPostbus postbus, IEnumerable<string> berichtIds,
                                   string[]? voegToe = null, string[]? verwijder = null)
    {
        var ids = berichtIds.Where(i => !string.IsNullOrWhiteSpace(i)).ToArray();
        if (ids.Length == 0) return;

        var client = await ClientOfFoutAsync(postbus);

        var body = new Dictionary<string, object?> { ["ids"] = ids };
        if (voegToe is { Length: > 0 }) body["addLabelIds"] = voegToe;
        if (verwijder is { Length: > 0 }) body["removeLabelIds"] = verwijder;

        var json = JsonSerializer.Serialize(body);
        var antwoord = await client.PostAsync($"{Basis}/messages/batchModify",
            new StringContent(json, Encoding.UTF8, "application/json"));

        if (antwoord.IsSuccessStatusCode) return;

        var inhoud = await antwoord.Content.ReadAsStringAsync();
        _log.LogWarning("Labels wijzigen mislukte: {Status} {Body}", antwoord.StatusCode, inhoud);
        throw new InvalidOperationException(Foutmelding(antwoord.StatusCode, inhoud, postbus));
    }

    // ============================================================
    //  Versturen
    // ============================================================

    /// <summary>
    /// Verstuurt een bericht vanuit de opgegeven postbus, eventueel met bijlagen.
    /// De mail komt daarna ook in de map Verzonden van die postbus te staan.
    /// </summary>
    /// <param name="html">De tekst is HTML (bv. de bestelling met een tabel); anders platte tekst.</param>
    public async Task VerstuurAsync(MailPostbus postbus, string naar, string onderwerp, string tekst,
                                    IEnumerable<(string Naam, byte[] Inhoud)>? bijlagen = null,
                                    string? cc = null, bool html = false,
                                    IEnumerable<(string Cid, string Naam, byte[] Inhoud)>? ingebed = null)
    {
        if (string.IsNullOrWhiteSpace(naar))
            throw new InvalidOperationException("Geen ontvanger opgegeven.");

        var client = await ClientOfFoutAsync(postbus);

        var raw = BouwBericht(postbus.Afzender ?? postbus.Adres, naar, cc, onderwerp, tekst, bijlagen, html, ingebed);
        var json = JsonSerializer.Serialize(new { raw = NaarBase64Url(Encoding.UTF8.GetBytes(raw)) });

        var antwoord = await client.PostAsync($"{Basis}/messages/send",
            new StringContent(json, Encoding.UTF8, "application/json"));

        var inhoud = await antwoord.Content.ReadAsStringAsync();
        if (!antwoord.IsSuccessStatusCode)
        {
            _log.LogWarning("Mail versturen mislukte: {Status} {Body}", antwoord.StatusCode, inhoud);
            throw new InvalidOperationException(Foutmelding(antwoord.StatusCode, inhoud, postbus));
        }
    }

    /// <summary>
    /// De handtekening die de gebruiker in Gmail heeft ingesteld (Instellingen ▸ Algemeen ▸ Handtekening), als HTML; leeg
    /// als er geen is of als hij niet te lezen is. Versturen via de API zet hem er niet vanzelf onder (Outlook deed dat
    /// vroeger wel), dus het venster plakt hem zelf onder de tekst. Gmail-scope readonly/modify volstaat (sendAs.get).
    /// Bij een werkplek-alias de handtekening van dat afzenderadres.
    /// </summary>
    public async Task<string> HandtekeningAsync(MailPostbus postbus)
    {
        try
        {
            var client = await ClientAsync(postbus);
            if (client is null) return "";
            var adres = postbus.Afzender ?? postbus.Adres;
            if (adres.IndexOf('<') is var b and >= 0 && adres.IndexOf('>', b) is var e and > 0) adres = adres[(b + 1)..e];   // "Naam <adres>"
            var antwoord = await client.GetAsync($"{Basis}/settings/sendAs/{Uri.EscapeDataString(adres.Trim())}");
            if (!antwoord.IsSuccessStatusCode) return "";
            using var doc = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("signature", out var s) && s.ValueKind == JsonValueKind.String ? (s.GetString() ?? "").Trim() : "";
        }
        catch (Exception ex)
        {
            _log.LogInformation("Gmail-handtekening van {Adres} niet te lezen: {Fout}", postbus.Adres, ex.Message);
            return "";
        }
    }

    /// <summary>Platte tekst als HTML (regels behouden) met de Gmail-handtekening eronder, zoals Gmail het zelf doet.</summary>
    public static string MetHandtekening(string tekst, string handtekening)
        => "<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:13px\">"
           + System.Net.WebUtility.HtmlEncode(tekst).Replace("\r\n", "\n").Replace("\n", "<br>")
           + "<br><br><div class=\"gmail_signature\">" + handtekening + "</div></div>";

    /// <summary>
    /// Zet een bericht als concept klaar in de map Concepten van de postbus (Gmail drafts), zodat de gebruiker het
    /// in Gmail nakijkt en zelf verstuurt. Geeft het id van het bericht terug (voor een link naar het concept).
    /// </summary>
    public async Task<string> MaakConceptAsync(MailPostbus postbus, string naar, string onderwerp, string tekst,
                                               IEnumerable<(string Naam, byte[] Inhoud)>? bijlagen = null,
                                               string? cc = null, bool html = false)
    {
        var client = await ClientOfFoutAsync(postbus);

        var raw = BouwBericht(postbus.Afzender ?? postbus.Adres, naar ?? "", cc, onderwerp, tekst, bijlagen, html);
        var json = JsonSerializer.Serialize(new { message = new { raw = NaarBase64Url(Encoding.UTF8.GetBytes(raw)) } });

        var antwoord = await client.PostAsync($"{Basis}/drafts",
            new StringContent(json, Encoding.UTF8, "application/json"));

        var inhoud = await antwoord.Content.ReadAsStringAsync();
        if (!antwoord.IsSuccessStatusCode)
        {
            _log.LogWarning("Mailconcept maken mislukte: {Status} {Body}", antwoord.StatusCode, inhoud);
            throw new InvalidOperationException(Foutmelding(antwoord.StatusCode, inhoud, postbus));
        }

        try
        {
            using var doc = JsonDocument.Parse(inhoud);
            return doc.RootElement.GetProperty("message").GetProperty("id").GetString() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Bouwt een gewoon RFC-822-bericht. Zonder bijlagen is dat één tekstdeel;
    /// met bijlagen een multipart/mixed met een scheidingsregel.
    /// </summary>
    private static string BouwBericht(string van, string naar, string? cc, string onderwerp, string tekst,
                                     IEnumerable<(string Naam, byte[] Inhoud)>? bijlagen, bool html = false,
                                     IEnumerable<(string Cid, string Naam, byte[] Inhoud)>? ingebed = null)
    {
        // Regelovergangen uit de kopregels houden: anders kan iemand met een
        // geplakte regel extra kopregels toevoegen.
        static string Kop(string? waarde)
            => (waarde ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

        var lijst = bijlagen?.ToList() ?? new List<(string Naam, byte[] Inhoud)>();
        var bericht = new StringBuilder();

        bericht.Append("From: ").Append(Kop(van)).Append("\r\n");
        bericht.Append("To: ").Append(Kop(naar)).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(cc)) bericht.Append("Cc: ").Append(Kop(cc)).Append("\r\n");
        bericht.Append("Subject: ").Append(KopRegel(Kop(onderwerp))).Append("\r\n");
        bericht.Append("MIME-Version: 1.0\r\n");

        var soort = html ? "text/html" : "text/plain";
        var plaatjes = html ? ingebed?.ToList() ?? new() : new();

        // Het tekstdeel; met ingebedde plaatjes (cid:...) een multipart/related om de HTML en de plaatjes heen.
        string Tekstdeel()
        {
            var d = new StringBuilder();
            if (plaatjes.Count == 0)
            {
                d.Append("Content-Type: ").Append(soort).Append("; charset=UTF-8\r\n");
                d.Append("Content-Transfer-Encoding: 8bit\r\n\r\n");
                d.Append(tekst).Append("\r\n");
                return d.ToString();
            }
            var rel = "rel-" + Guid.NewGuid().ToString("N");
            d.Append("Content-Type: multipart/related; boundary=\"").Append(rel).Append("\"\r\n\r\n");
            d.Append("--").Append(rel).Append("\r\n");
            d.Append("Content-Type: ").Append(soort).Append("; charset=UTF-8\r\n");
            d.Append("Content-Transfer-Encoding: 8bit\r\n\r\n");
            d.Append(tekst).Append("\r\n");
            foreach (var (cid, naam, inhoud) in plaatjes)
            {
                d.Append("--").Append(rel).Append("\r\n");
                d.Append("Content-Type: ").Append(FilestoreService.MimeVoor(naam)).Append("; name=\"").Append(KopRegel(Kop(naam))).Append("\"\r\n");
                d.Append("Content-ID: <").Append(Kop(cid)).Append(">\r\n");
                d.Append("Content-Disposition: inline; filename=\"").Append(KopRegel(Kop(naam))).Append("\"\r\n");
                d.Append("Content-Transfer-Encoding: base64\r\n\r\n");
                var b64 = Convert.ToBase64String(inhoud);
                for (var i = 0; i < b64.Length; i += 76) d.Append(b64, i, Math.Min(76, b64.Length - i)).Append("\r\n");
            }
            d.Append("--").Append(rel).Append("--\r\n");
            return d.ToString();
        }

        if (lijst.Count == 0 && plaatjes.Count > 0)
        {
            bericht.Append(Tekstdeel());
            return bericht.ToString();
        }
        if (lijst.Count == 0)
        {
            bericht.Append("Content-Type: ").Append(soort).Append("; charset=UTF-8\r\n");
            bericht.Append("Content-Transfer-Encoding: 8bit\r\n\r\n");
            bericht.Append(tekst);
            return bericht.ToString();
        }

        var grens = "mde-" + Guid.NewGuid().ToString("N");
        bericht.Append("Content-Type: multipart/mixed; boundary=\"").Append(grens).Append("\"\r\n\r\n");

        bericht.Append("--").Append(grens).Append("\r\n");
        bericht.Append(Tekstdeel());

        foreach (var (naam, inhoud) in lijst)
        {
            var veilig = KopRegel(Kop(naam));
            bericht.Append("--").Append(grens).Append("\r\n");
            bericht.Append("Content-Type: ").Append(FilestoreService.MimeVoor(naam))
                   .Append("; name=\"").Append(veilig).Append("\"\r\n");
            bericht.Append("Content-Disposition: attachment; filename=\"").Append(veilig).Append("\"\r\n");
            bericht.Append("Content-Transfer-Encoding: base64\r\n\r\n");

            // base64 in regels van 76 tekens, zoals het hoort
            var basis64 = Convert.ToBase64String(inhoud);
            for (var i = 0; i < basis64.Length; i += 76)
                bericht.Append(basis64, i, Math.Min(76, basis64.Length - i)).Append("\r\n");
        }

        bericht.Append("--").Append(grens).Append("--");
        return bericht.ToString();
    }

    /// <summary>Kopregel met accenten of andere niet-ASCII-tekens veilig coderen.</summary>
    private static string KopRegel(string waarde)
    {
        if (waarde.All(c => c < 128)) return waarde;
        return "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(waarde)) + "?=";
    }

    // ---- hulpjes ----
    private static string? Tekst(JsonElement e, string naam) =>
        e.TryGetProperty(naam, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string NaarBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] VanBase64Url(string tekst)
    {
        var recht = tekst.Replace('-', '+').Replace('_', '/');
        switch (recht.Length % 4)
        {
            case 2: recht += "=="; break;
            case 3: recht += "="; break;
        }
        return Convert.FromBase64String(recht);
    }

    private string Foutmelding(System.Net.HttpStatusCode status, string inhoud, MailPostbus postbus)
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
                => " Zet de Gmail API aan in Google Cloud Console (APIs & Services → Library).",
            403 or 401 => " " + Beschrijf(postbus),
            _ => "",
        };

        return $"Gmail gaf {(int)status} terug voor {postbus.Naam}. {detail}{tip}".Trim();
    }
}
