using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace EverythingApp.Services;

/// <summary>Eén Sonos-speaker (of een stereopaar/set: dan telt de hoofdspeaker).</summary>
public record SonosSpeaker(string Naam, string Adres, string Uuid, int Volume, bool Gedempt, bool Bereikbaar)
{
    /// <summary>Wat er speelt (van de hoofdspeaker van zijn groep); null als het niet op te vragen was.</summary>
    public SonosSpeelt? Speelt { get; init; }
    /// <summary>De groep als hij samen met andere speakers speelt ("Fablab + Mancave"), anders leeg.</summary>
    public string Groep { get; init; } = "";
}

/// <summary>Wat een speaker(groep) afspeelt. Staat: PLAYING, PAUSED_PLAYBACK, STOPPED, TRANSITIONING.</summary>
/// <param name="Bron">Radiozender, "TV" of "Line-in"; leeg bij gewone muziek.</param>
/// <param name="HeeftHoes">Er is een albumhoes; op te halen via /sonos/hoes/{uuid}.</param>
public record SonosSpeelt(string Staat, string Titel, string Artiest, string Album, string Bron, bool HeeftHoes)
{
    public bool Afspelen => Staat is "PLAYING" or "TRANSITIONING";
}

/// <summary>
/// Volume en "wat speelt er" van de Sonos-speakers, lokaal over het netwerk (UPnP/SOAP op poort 1400, geen account
/// of cloud nodig). In appsettings staat onder Sonos:Adressen minstens één IP-adres; de andere speakers komen uit de
/// groepsindeling die elke speaker kent (ZoneGroupTopology). Bedienen: alleen volume en dempen, op verzoek van de
/// gebruiker; afspelen zelf wordt alleen gelezen.
/// </summary>
public class SonosService
{
    private const string AvTransport = "urn:schemas-upnp-org:service:AVTransport:1";
    private const string AvPad = "/MediaRenderer/AVTransport/Control";

    private readonly HttpClient _http;
    private readonly ILogger<SonosService> _log;
    private readonly string[] _adressen;

    /// <summary>Een speaker uit de groepsindeling, met de uuid van de hoofdspeaker van zijn groep.</summary>
    private record Lid(string Naam, string Adres, string Uuid, string Coordinator);

    // De gevonden speakers een minuut onthouden; de indeling verandert zelden.
    private List<Lid>? _indeling;
    private DateTime _indelingOp;

    // Laatst bekende hoes per hoofdspeaker: een pad op de speaker (/getaa?...) of een volledige link.
    private readonly ConcurrentDictionary<string, string> _hoezen = new();

    public SonosService(IConfiguration config, ILogger<SonosService> log)
    {
        _log = log;
        _adressen = (config.GetSection("Sonos:Adressen").Get<string[]>()
                     ?? (config["Sonos:Adressen"] ?? "").Split(',', ';'))
                    .Select(a => a.Trim()).Where(a => a.Length > 0).ToArray();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    }

    public bool Ingesteld => _adressen.Length > 0;

    /// <summary>Alle speakers met volume en wat er speelt. Een speaker die niet antwoordt komt erin met Bereikbaar = false.</summary>
    public async Task<List<SonosSpeaker>> SpeakersAsync(bool opnieuwZoeken = false)
    {
        var indeling = await IndelingAsync(opnieuwZoeken);

        // Wat er speelt vraag je aan de hoofdspeaker (coordinator): één keer per groep.
        var spelen = new Dictionary<string, SonosSpeelt?>();
        foreach (var c in indeling.Select(l => l.Coordinator).Distinct())
        {
            var hoofd = indeling.FirstOrDefault(l => l.Uuid == c);
            if (hoofd is null) continue;
            try { spelen[c] = await SpeeltAsync(hoofd); }
            catch (Exception ex) { _log.LogInformation("Sonos {Naam}: afspeelstatus mislukt: {Fout}", hoofd.Naam, ex.Message); }
        }

        var taken = indeling.Select(async s =>
        {
            var groep = indeling.Where(l => l.Coordinator == s.Coordinator).Select(l => l.Naam).ToList();
            try
            {
                var vol = await VolumeAsync(s.Adres);
                var demp = await GedemptAsync(s.Adres);
                return new SonosSpeaker(s.Naam, s.Adres, s.Uuid, vol, demp, true)
                {
                    Speelt = spelen.GetValueOrDefault(s.Coordinator),
                    Groep = groep.Count > 1 ? string.Join(" + ", groep) : "",
                };
            }
            catch (Exception ex)
            {
                _log.LogInformation("Sonos {Naam} ({Adres}) antwoordt niet: {Fout}", s.Naam, s.Adres, ex.Message);
                return new SonosSpeaker(s.Naam, s.Adres, s.Uuid, 0, false, false);
            }
        });
        return (await Task.WhenAll(taken)).OrderBy(s => s.Naam, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task ZetVolumeAsync(string adres, int volume)
    {
        Controleer(adres);
        await RenderingAsync(adres, "SetVolume",
            $"<InstanceID>0</InstanceID><Channel>Master</Channel><DesiredVolume>{Math.Clamp(volume, 0, 100)}</DesiredVolume>");
    }

    public async Task ZetGedemptAsync(string adres, bool gedempt)
    {
        Controleer(adres);
        await RenderingAsync(adres, "SetMute",
            $"<InstanceID>0</InstanceID><Channel>Master</Channel><DesiredMute>{(gedempt ? 1 : 0)}</DesiredMute>");
    }

    /// <summary>
    /// De albumhoes van wat een speaker(groep) speelt. Via de server, want de speaker geeft meestal een pad op
    /// zichzelf (/getaa?...) en streamingdiensten een eigen link; zo werkt het ook als de browser de speaker niet ziet.
    /// </summary>
    public async Task<(byte[] Data, string Soort)?> HoesAsync(string uuid)
    {
        var lid = _indeling?.FirstOrDefault(l => l.Uuid == uuid);
        if (lid is null || !_hoezen.TryGetValue(lid.Coordinator, out var hoes)) return null;
        var hoofd = _indeling!.FirstOrDefault(l => l.Uuid == lid.Coordinator) ?? lid;

        string url;
        if (hoes.StartsWith('/')) url = $"http://{hoofd.Adres}:1400{hoes}";
        else if (hoes.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || hoes.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) url = hoes;
        else return null;

        using var antw = await _http.GetAsync(url);
        if (!antw.IsSuccessStatusCode) return null;
        var soort = antw.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        if (!soort.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
        return (await antw.Content.ReadAsByteArrayAsync(), soort);
    }

    // ---- intern ----

    /// <summary>Alleen adressen die we zelf gevonden hebben; de pagina kan zo niet naar willekeurige adressen sturen.</summary>
    private void Controleer(string adres)
    {
        if (_indeling is null || _indeling.All(s => s.Adres != adres))
            throw new InvalidOperationException("Onbekende speaker; ververs de lijst.");
    }

    private async Task<int> VolumeAsync(string adres)
    {
        var x = await RenderingAsync(adres, "GetVolume", "<InstanceID>0</InstanceID><Channel>Master</Channel>");
        return int.Parse(Waarde(x, "CurrentVolume") ?? "0", CultureInfo.InvariantCulture);
    }

    private async Task<bool> GedemptAsync(string adres)
    {
        var x = await RenderingAsync(adres, "GetMute", "<InstanceID>0</InstanceID><Channel>Master</Channel>");
        return Waarde(x, "CurrentMute") == "1";
    }

    private async Task<SonosSpeelt> SpeeltAsync(Lid hoofd)
    {
        var staat = Waarde(await SoapAsync(hoofd.Adres, AvPad, AvTransport, "GetTransportInfo", "<InstanceID>0</InstanceID>"),
                           "CurrentTransportState") ?? "";
        var positie = await SoapAsync(hoofd.Adres, AvPad, AvTransport, "GetPositionInfo", "<InstanceID>0</InstanceID>");
        var uri = Waarde(positie, "TrackURI") ?? "";
        var meta = Didl(Waarde(positie, "TrackMetaData"));

        string titel = Veld(meta, "title"), artiest = Veld(meta, "creator"), album = Veld(meta, "album");
        var stroom = Veld(meta, "streamContent");           // radio: meestal "Artiest - Titel"
        var hoes = Veld(meta, "albumArtURI");
        var bron = "";

        if (uri.StartsWith("x-sonos-htastream", StringComparison.OrdinalIgnoreCase)) { bron = "TV"; titel = "TV-geluid"; }
        else if (uri.StartsWith("x-rincon-stream", StringComparison.OrdinalIgnoreCase)) { bron = "Line-in"; titel = "Line-in"; }
        else if (stroom.Length > 0 || uri.StartsWith("x-rincon-mp3radio", StringComparison.OrdinalIgnoreCase)
                 || uri.StartsWith("x-sonosapi-stream", StringComparison.OrdinalIgnoreCase)
                 || uri.StartsWith("x-sonosapi-radio", StringComparison.OrdinalIgnoreCase)
                 || uri.StartsWith("aac:", StringComparison.OrdinalIgnoreCase))
        {
            // radio: de zendernaam staat bij de media, het nummer in streamContent
            try
            {
                var media = Didl(Waarde(await SoapAsync(hoofd.Adres, AvPad, AvTransport, "GetMediaInfo", "<InstanceID>0</InstanceID>"),
                                        "CurrentURIMetaData"));
                bron = Veld(media, "title");
            }
            catch { /* zonder zendernaam ook goed */ }
            if (stroom.Length > 0 && !stroom.StartsWith("ZPSTR_", StringComparison.Ordinal))
            {
                var delen = stroom.Split(" - ", 2);
                (artiest, titel) = delen.Length == 2 ? (delen[0].Trim(), delen[1].Trim()) : ("", stroom);
            }
            else titel = "";
            album = "";
        }
        if (titel.StartsWith("x-", StringComparison.OrdinalIgnoreCase)) titel = "";

        if (hoes.Length > 0) _hoezen[hoofd.Uuid] = hoes; else _hoezen.TryRemove(hoofd.Uuid, out _);
        return new SonosSpeelt(staat, titel, artiest, album, bron, hoes.Length > 0);
    }

    private static XDocument? Didl(string? tekst)
    {
        if (string.IsNullOrWhiteSpace(tekst) || tekst == "NOT_IMPLEMENTED") return null;
        try { return XDocument.Parse(tekst); } catch { return null; }
    }

    private static string Veld(XDocument? x, string naam)
        => x?.Descendants().FirstOrDefault(e => e.Name.LocalName == naam)?.Value.Trim() ?? "";

    private Task<XDocument> RenderingAsync(string adres, string actie, string argumenten)
        => SoapAsync(adres, "/MediaRenderer/RenderingControl/Control", "urn:schemas-upnp-org:service:RenderingControl:1", actie, argumenten);

    private async Task<List<Lid>> IndelingAsync(bool opnieuw)
    {
        if (!opnieuw && _indeling is not null && DateTime.UtcNow - _indelingOp < TimeSpan.FromMinutes(1)) return _indeling;

        Exception? laatste = null;
        foreach (var adres in _adressen)
        {
            try
            {
                var x = await SoapAsync(adres, "/ZoneGroupTopology/Control", "urn:schemas-upnp-org:service:ZoneGroupTopology:1",
                                        "GetZoneGroupState", "");
                var staat = XDocument.Parse(Waarde(x, "ZoneGroupState") ?? "<ZoneGroupState/>");
                // Alleen de leden direct onder een groep; Satellite (surround, sub) en de onzichtbare helft van een
                // stereopaar niet.
                var lijst = staat.Descendants("ZoneGroup").SelectMany(g => g.Elements("ZoneGroupMember")
                        .Where(m => (string?)m.Attribute("Invisible") != "1")
                        .Select(m => new Lid((string?)m.Attribute("ZoneName") ?? "?",
                                             new Uri((string?)m.Attribute("Location") ?? $"http://{adres}:1400/").Host,
                                             (string?)m.Attribute("UUID") ?? "",
                                             (string?)g.Attribute("Coordinator") ?? "")))
                    .GroupBy(m => m.Uuid).Select(g => g.First())
                    .ToList();
                if (lijst.Count == 0) continue;
                _indeling = lijst;
                _indelingOp = DateTime.UtcNow;
                return lijst;
            }
            catch (Exception ex) { laatste = ex; }
        }
        if (_indeling is not null) return _indeling;   // liever de vorige lijst dan niets
        throw new InvalidOperationException("Geen Sonos-speaker bereikbaar" + (laatste is null ? "." : ": " + laatste.Message));
    }

    private async Task<XDocument> SoapAsync(string adres, string pad, string service, string actie, string argumenten)
    {
        var body =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
            $"<s:Body><u:{actie} xmlns:u=\"{service}\">{argumenten}</u:{actie}></s:Body></s:Envelope>";
        using var req = new HttpRequestMessage(HttpMethod.Post, $"http://{adres}:1400{pad}")
        {
            Content = new StringContent(body, Encoding.UTF8, "text/xml"),
        };
        req.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{service}#{actie}\"");
        using var antw = await _http.SendAsync(req);
        var tekst = await antw.Content.ReadAsStringAsync();
        if (antw.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"{actie} gaf {(int)antw.StatusCode}");
        return XDocument.Parse(tekst);
    }

    private static string? Waarde(XDocument x, string naam) => x.Descendants().FirstOrDefault(e => e.Name.LocalName == naam)?.Value;
}
