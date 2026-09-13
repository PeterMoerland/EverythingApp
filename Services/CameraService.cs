using System.Net;

namespace EverythingApp.Services;

/// <summary>Eén camera zoals die in appsettings staat.</summary>
public class Camera
{
    public string Naam { get; set; } = "";

    /// <summary>Host en poort, bijvoorbeeld 172.16.6.1:80.</summary>
    public string Adres { get; set; } = "";

    public string Gebruiker { get; set; } = "";
    public string Wachtwoord { get; set; } = "";

    /// <summary>
    /// Het pad naar een stilstaand beeld. Leeg laten is het handigst: de app
    /// probeert dan de bekende paden tot er een foto uit komt. Zelf invullen kan
    /// ook; {gebruiker} en {wachtwoord} worden daarin vervangen.
    /// </summary>
    public string Snapshot { get; set; } = "";

    /// <summary>Https in plaats van http naar de camera.</summary>
    public bool Beveiligd { get; set; }
}

/// <summary>
/// Haalt camerabeelden op namens de browser.
///
/// Waarom via de server en niet rechtstreeks: het wachtwoord van de camera zou
/// anders bij iedere gebruiker in beeld komen, een browser weigert beelden van een
/// http-camera zodra de app op https draait, en zo hoeven de camera's niet
/// bereikbaar te zijn vanaf de werkplekken zelf.
///
/// Merken verschillen in twee dingen: het pad naar een foto, en hoe ze inloggen.
/// Dahua en Hikvision willen Digest-authenticatie, Foscam en Reolink zetten de
/// inloggegevens juist in het adres. De app probeert de bekende paden één keer en
/// onthoudt per camera welke werkte.
/// </summary>
public class CameraService
{
    /// <summary>
    /// De paden die we proberen, op volgorde. Dahua staat vooraan omdat dat hier
    /// hangt; de rest is er voor als er ooit een ander merk bij komt.
    /// </summary>
    private static readonly (string Merk, string Pad)[] BekendePaden =
    {
        ("Dahua",     "/cgi-bin/snapshot.cgi?channel=1"),
        ("Hikvision", "/ISAPI/Streaming/channels/101/picture"),
        ("Foscam",    "/cgi-bin/CGIProxy.fcgi?cmd=snapPicture2&usr={gebruiker}&pwd={wachtwoord}"),
        ("Axis",      "/axis-cgi/jpg/image.cgi"),
        ("Reolink",   "/cgi-bin/api.cgi?cmd=Snap&channel=0&user={gebruiker}&password={wachtwoord}"),
    };

    private readonly List<Camera> _cameras;
    private readonly HttpClient _http;
    private readonly ILogger<CameraService> _log;

    /// <summary>Het pad dat bij deze camera bleek te werken, op volgnummer.</summary>
    private readonly Dictionary<int, string> _werkendPad = new();

    public CameraService(IConfiguration config, ILogger<CameraService> log)
    {
        _log = log;
        _cameras = config.GetSection("Cameras").Get<List<Camera>>() ?? new List<Camera>();

        // Digest-inloggegevens per camera. Een CredentialCache kiest zelf de juiste
        // op basis van het adres, dus één HttpClient volstaat voor alle camera's.
        var kluis = new CredentialCache();
        foreach (var camera in _cameras)
        {
            if (camera.Gebruiker.Length == 0) continue;
            try
            {
                var basis = new Uri((camera.Beveiligd ? "https://" : "http://")
                                    + camera.Adres.TrimEnd('/') + "/");
                var inlog = new NetworkCredential(camera.Gebruiker, camera.Wachtwoord);

                // Dahua en Hikvision doen Digest; Basic staat erbij voor de rest.
                kluis.Add(basis, "Digest", inlog);
                kluis.Add(basis, "Basic", inlog);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Camera {Naam}: adres '{Adres}' is niet te lezen ({Fout}).",
                                camera.Naam, camera.Adres, ex.Message);
            }
        }

        // Camera's hebben vrijwel altijd een zelfondertekend certificaat en zijn
        // traag; een korte tijdslimiet voorkomt dat een dode camera het scherm ophoudt.
        var handler = new HttpClientHandler
        {
            Credentials = kluis,
            PreAuthenticate = true,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
    }

    /// <summary>De camera's uit de instellingen, in de volgorde waarin ze daar staan.</summary>
    public IReadOnlyList<Camera> Cameras => _cameras;

    public bool Beschikbaar => _cameras.Count > 0;

    /// <summary>
    /// Eén beeld van de camera met dit volgnummer, of null als het niet lukt.
    /// Het scherm laat het vorige beeld dan vervagen; de volgende poging volgt vanzelf.
    /// </summary>
    public async Task<byte[]?> BeeldAsync(int nummer, CancellationToken stop = default)
    {
        if (nummer < 0 || nummer >= _cameras.Count) return null;
        var camera = _cameras[nummer];

        // Werkte er eerder een pad, dan meteen dat gebruiken.
        if (_werkendPad.TryGetValue(nummer, out var bekend))
        {
            var (foto, _) = await ProbeerAsync(camera, bekend, stop);
            if (foto is not null) return foto;
            _werkendPad.Remove(nummer);      // camera veranderd? opnieuw zoeken
        }

        foreach (var pad in TeProberen(camera))
        {
            var (foto, waarom) = await ProbeerAsync(camera, pad, stop);
            if (foto is not null)
            {
                _werkendPad[nummer] = pad;
                _log.LogInformation("Camera {Naam} werkt via {Pad}.", camera.Naam, pad);
                return foto;
            }
            if (stop.IsCancellationRequested) return null;
            _log.LogDebug("Camera {Naam}, pad {Pad}: {Waarom}", camera.Naam, pad, waarom);
        }

        _log.LogInformation("Camera {Naam} ({Adres}) gaf op geen enkel pad een foto.",
                            camera.Naam, camera.Adres);
        return null;
    }

    /// <summary>Welke paden er voor deze camera geprobeerd worden.</summary>
    private static IEnumerable<string> TeProberen(Camera camera)
        => string.IsNullOrWhiteSpace(camera.Snapshot)
            ? BekendePaden.Select(p => p.Pad)
            : new[] { camera.Snapshot };

    /// <summary>Eén poging. Geeft de foto terug, of null met de reden erbij.</summary>
    private async Task<(byte[]? Foto, string Waarom)> ProbeerAsync(Camera camera, string pad,
                                                                   CancellationToken stop)
    {
        var ingevuld = pad.Replace("{gebruiker}", Uri.EscapeDataString(camera.Gebruiker))
                          .Replace("{wachtwoord}", Uri.EscapeDataString(camera.Wachtwoord));

        var adres = (camera.Beveiligd ? "https://" : "http://") + camera.Adres.TrimEnd('/') + ingevuld;

        try
        {
            using var antwoord = await _http.GetAsync(adres, stop);
            if (!antwoord.IsSuccessStatusCode)
                return (null, $"gaf {(int)antwoord.StatusCode} {antwoord.ReasonPhrase}");

            var bytes = await antwoord.Content.ReadAsByteArrayAsync(stop);

            // Een camera geeft bij een verkeerd wachtwoord vaak netjes een foutpagina
            // met code 200. Een echte foto begint met de JPEG-markering.
            if (bytes.Length < 100)
                return (null, $"stuurde maar {bytes.Length} bytes");
            if (bytes[0] != 0xFF || bytes[1] != 0xD8)
                return (null, "stuurde geen JPEG (waarschijnlijk een fout- of inlogpagina)");

            return (bytes, "");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return (null, "afgebroken");
        }
        catch (OperationCanceledException)
        {
            return (null, "te traag (meer dan 6 seconden)");
        }
        catch (Exception ex)
        {
            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Wat er precies gebeurt bij het ophalen van een beeld: welk adres, welk
    /// antwoord. Voor als er niets in beeld komt en je wilt weten waarom.
    /// </summary>
    public async Task<string> UitlegAsync(int nummer, CancellationToken stop = default)
    {
        var r = new System.Text.StringBuilder();

        if (nummer < 0 || nummer >= _cameras.Count)
            return $"Er is geen camera met volgnummer {nummer}. Er zijn er {_cameras.Count} ingesteld.";

        var camera = _cameras[nummer];
        r.AppendLine($"Camera {nummer}: {camera.Naam}");
        r.AppendLine($"Adres      : {(camera.Beveiligd ? "https" : "http")}://{camera.Adres}");
        r.AppendLine($"Gebruiker  : {(camera.Gebruiker.Length == 0 ? "(leeg)" : camera.Gebruiker)}");
        r.AppendLine($"Wachtwoord : {(camera.Wachtwoord.Length == 0 ? "(leeg)" : "ingevuld")}");
        r.AppendLine();
        r.AppendLine("Geprobeerde paden");
        r.AppendLine(new string('-', 40));

        foreach (var pad in TeProberen(camera))
        {
            var (foto, waarom) = await ProbeerAsync(camera, pad, stop);
            r.AppendLine(foto is not null
                ? $"  GELUKT  {pad}  ({foto.Length / 1024} kB)"
                : $"  nee     {pad}  → {waarom}");

            if (foto is not null)
            {
                _werkendPad[nummer] = pad;
                r.AppendLine();
                r.AppendLine("Dit pad wordt vanaf nu gebruikt.");
                return r.ToString();
            }
        }

        r.AppendLine();
        r.AppendLine("Geen enkel pad gaf een foto. Kijk of het adres en de poort kloppen,");
        r.AppendLine("of vul het juiste pad zelf in bij Snapshot in appsettings.json.");
        return r.ToString();
    }
}
