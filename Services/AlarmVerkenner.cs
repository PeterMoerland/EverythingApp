using System.Net.Sockets;

namespace EverythingApp.Services;

/// <summary>Wat er op één poort te vinden was.</summary>
public record Poortbevinding(int Poort, string Waarvoor, bool Open, string Antwoord);

/// <summary>
/// Kijkt wat een apparaat op het netwerk aanbiedt, zodat we weten wat ermee kan.
///
/// Bij de alarminstallatie weten we het merk (nog) niet, en dat bepaalt alles:
/// het ene paneel heeft een webinterface, het andere praat Modbus, en sommige
/// kunnen alleen naar een meldkamer bellen. In plaats van te gokken kloppen we op
/// de bekende deuren en kijken we wie er opendoet.
/// </summary>
public class AlarmVerkenner
{
    /// <summary>
    /// De poorten die de moeite waard zijn, met waar ze bij horen. Bewust een korte
    /// lijst: dit is geen netwerkscanner, alleen een blik op wat gangbaar is bij
    /// alarmpanelen en gebouwapparatuur.
    /// </summary>
    private static readonly (int Poort, string Waarvoor)[] Poorten =
    {
        (80,    "webinterface (http)"),
        (443,   "webinterface (https)"),
        (8080,  "webinterface, tweede poort"),
        (8000,  "webinterface of videostroom"),
        (22,    "ssh"),
        (23,    "telnet — bij oudere panelen de beheerpoort"),
        (502,   "Modbus TCP — hiermee zijn statussen rechtstreeks uit te lezen"),
        (44818, "EtherNet/IP — zelfde taal als de gebouw-PLC"),
        (161,   "SNMP (wordt hier niet getest, alleen genoemd)"),
        (10001, "seriële brug (Lantronix en Texecom gebruiken deze)"),
        (10005, "Honeywell Galaxy, ethernetmodule"),
        (10000, "Paradox IP150"),
        (3001,  "Risco / Aritech"),
        (3072,  "DSC"),
        (7094,  "Satel"),
        (7700,  "Bosch"),
        (50000, "Vanderbilt SPC (FlexC)"),
        (51000, "Vanderbilt SPC Connect"),

        // Alphatronics UNii: de 'Generieke Interface'. De poort stelt de
        // installateur zelf in, dus we proberen de gangbare keuzes.
        (6502,  "Alphatronics UNii — generieke interface"),
        (6503,  "Alphatronics UNii — generieke interface, tweede keuze"),
        (1234,  "Alphatronics UNii — soms zo ingesteld"),
    };

    private readonly ILogger<AlarmVerkenner> _log;
    private readonly HttpClient _http;

    public AlarmVerkenner(ILogger<AlarmVerkenner> log)
    {
        _log = log;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };
    }

    /// <summary>
    /// Loopt de poorten langs. Antwoordt er een webinterface, dan halen we ook de
    /// titel en de servernaam op — daaraan is het merk vaak al te herkennen.
    /// </summary>
    public async Task<List<Poortbevinding>> VerkenAsync(string host, CancellationToken stop = default)
    {
        var uitkomst = new List<Poortbevinding>();

        foreach (var (poort, waarvoor) in Poorten)
        {
            if (stop.IsCancellationRequested) break;

            // SNMP gaat over UDP; daar zegt "poort open" niets over, dus die slaan we over.
            if (poort == 161)
            {
                uitkomst.Add(new(poort, waarvoor, false, "niet getest (UDP)"));
                continue;
            }

            var open = await PoortOpenAsync(host, poort, stop);
            var antwoord = "";

            if (open && poort is 80 or 443 or 8080 or 8000)
                antwoord = await WebkaartjeAsync(host, poort, poort == 443, stop);

            uitkomst.Add(new(poort, waarvoor, open, antwoord));
        }

        return uitkomst;
    }

    /// <summary>Doet er iemand open op deze poort? Kort wachten; een gesloten poort weigert direct.</summary>
    private static async Task<bool> PoortOpenAsync(string host, int poort, CancellationToken stop)
    {
        try
        {
            using var klant = new TcpClient();
            using var klok = CancellationTokenSource.CreateLinkedTokenSource(stop);
            klok.CancelAfter(TimeSpan.FromSeconds(2));

            await klant.ConnectAsync(host, poort, klok.Token);
            return klant.Connected;
        }
        catch { return false; }
    }

    /// <summary>
    /// De servernaam en de paginatitel van een webinterface. Daar staat vaak
    /// letterlijk het merk in, en dat is precies wat we willen weten.
    /// </summary>
    private async Task<string> WebkaartjeAsync(string host, int poort, bool beveiligd,
                                               CancellationToken stop)
    {
        var adres = $"{(beveiligd ? "https" : "http")}://{host}:{poort}/";
        try
        {
            using var antwoord = await _http.GetAsync(adres, stop);

            var delen = new List<string> { $"{(int)antwoord.StatusCode} {antwoord.ReasonPhrase}" };

            if (antwoord.Headers.TryGetValues("Server", out var server))
                delen.Add("Server: " + string.Join(" ", server));

            if (antwoord.Headers.WwwAuthenticate.Count > 0)
                delen.Add("inloggen: " + string.Join(" ", antwoord.Headers.WwwAuthenticate));

            var pagina = await antwoord.Content.ReadAsStringAsync(stop);
            var titel = TitelUit(pagina);
            if (titel.Length > 0) delen.Add($"titel: \"{titel}\"");

            return string.Join(" · ", delen);
        }
        catch (Exception ex)
        {
            _log.LogDebug("Webinterface {Adres}: {Fout}", adres, ex.Message);
            return "open, maar geen leesbare webpagina";
        }
    }

    private static string TitelUit(string html)
    {
        var start = html.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return "";

        start = html.IndexOf('>', start);
        if (start < 0) return "";

        var eind = html.IndexOf("</title", start, StringComparison.OrdinalIgnoreCase);
        if (eind < 0) return "";

        var titel = html[(start + 1)..eind].Trim();
        return titel.Length > 80 ? titel[..80] : titel;
    }
}
