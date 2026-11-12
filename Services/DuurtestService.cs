using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using FluentFTP;

namespace EverythingApp.Services;

/// <summary>
/// Duurtest van een product in de testrig. De PLC van de rig (Omron, FTP-server) schrijft
/// tijdens de test een logmap op zijn geheugenkaart; de PLC maakt die map leeg bij het starten
/// van een test. Na de test haalt de app-server de map op via FTP, zipt hem en zet hem als
/// document bij het exemplaar (StoreType product). Instellingen: Productie:Duurtest in
/// appsettings.json. Later komt hier ook de bediening van de rig bij (EtherNet/IP, zie OmronPlc).
///
/// Serienummercontrole (voorstel Sjoerd Quist deel 4, keuze 11-11-2026): de PLC gaat het serienummer van de robot in de
/// download van de geheugenkaart zetten. De app zoekt het in de tekstbestanden van de log (<see cref="ZoekSerienummer"/>,
/// patroon instelbaar met Productie:Duurtest:SerienummerPatroon) en vergelijkt het met het gekozen exemplaar: een log van
/// een ander serienummer wordt niet opgeslagen. Staat er (nog) geen serienummer in, dan gaat het door zoals voorheen,
/// tenzij Productie:Duurtest:SerienummerVerplicht aan staat.
/// </summary>
public class DuurtestService
{
    private readonly string _host;
    private readonly int _poort;
    private readonly string _gebruiker;
    private readonly string _wachtwoord;
    private readonly string _map;
    private readonly Regex? _snPatroon;
    private readonly bool _snVerplicht;
    private readonly FilestoreService _store;
    private readonly IProductRepository _repo;
    private readonly ILogger<DuurtestService> _log;

    public DuurtestService(IConfiguration config, FilestoreService store, IProductRepository repo, ILogger<DuurtestService> log)
    {
        var s = config.GetSection("Productie:Duurtest");
        _host = s["FtpHost"] ?? "";
        _poort = s.GetValue<int?>("FtpPoort") ?? 21;
        _gebruiker = s["FtpGebruiker"] ?? "";
        _wachtwoord = s["FtpWachtwoord"] ?? "";
        _map = (s["Map"] ?? "/").Replace('\\', '/');
        _store = store; _repo = repo; _log = log;
        _snVerplicht = s.GetValue<bool?>("SerienummerVerplicht") ?? false;
        // leeg ingesteld = niet controleren; niet ingesteld = het standaardpatroon
        var patroon = s["SerienummerPatroon"] ?? StandaardPatroon;
        if (patroon.Trim().Length > 0)
        {
            try { _snPatroon = new Regex(patroon, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)); }
            catch (Exception ex) { log.LogWarning("Productie:Duurtest:SerienummerPatroon is geen geldig patroon ({Fout}); het standaardpatroon geldt.", ex.Message); _snPatroon = new Regex(StandaardPatroon, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)); }
        }
    }

    /// <summary>
    /// Een regel die begint met Serienummer, Serienr of Serial number, dan een scheidingsteken en de waarde, bv.
    /// "Serienummer: 32", "Serienummer=32", "Serial number;32" of "serienummer": "32". De waarde moet een cijfer
    /// bevatten, zodat een kopregel als "Serienummer;Datum" niet telt. Een los "SN" telt bewust niet: zo kan een log ook
    /// de serienummers van drives noemen. Schrijft de PLC het anders, stel dan Productie:Duurtest:SerienummerPatroon in.
    /// </summary>
    public const string StandaardPatroon =
        @"^[^\w\r\n]*(?:serie(?:nummer|nr)|serial[ _-]?(?:number|no|nr))[^\w\r\n]+([A-Za-z0-9._-]*\d[A-Za-z0-9._-]*)";

    /// <summary>Het serienummer in de bestanden van een log: het eerste dat het patroon vindt, met het bestand erbij.</summary>
    public static (string Serienummer, string Bestand)? ZoekSerienummer(IEnumerable<(string Naam, byte[] Inhoud)> bestanden, Regex patroon)
    {
        foreach (var (naam, inhoud) in bestanden)
        {
            // alleen tekst: niet te groot en geen nul-bytes aan het begin
            if (inhoud.Length == 0 || inhoud.Length > 4 * 1024 * 1024 || inhoud.AsSpan(0, Math.Min(inhoud.Length, 2048)).IndexOf((byte)0) >= 0) continue;
            string tekst;
            try { tekst = Encoding.UTF8.GetString(inhoud); } catch { continue; }
            try
            {
                var m = patroon.Match(tekst);
                if (m.Success && m.Groups.Count > 1 && m.Groups[1].Value.Trim().Length > 0) return (m.Groups[1].Value.Trim(), naam);
            }
            catch (RegexMatchTimeoutException) { }
        }
        return null;
    }

    /// <summary>
    /// Zelfde serienummer? Getallen als getal (032 = 32), anders letters en cijfers zonder hoofdletterverschil. Is het
    /// serienummer van het exemplaar een getal en noemt de log er het type voor ("MPR1-0032"), dan telt het getal achteraan.
    /// </summary>
    public static bool ZelfdeSerienummer(string inLog, string vanExemplaar)
    {
        static string Kaal(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var (log, ex) = (Kaal(inLog), Kaal(vanExemplaar));
        if (log.Length == 0 || ex.Length == 0) return false;
        if (log == ex) return true;
        if (!long.TryParse(ex, out var nummer)) return false;
        if (long.TryParse(log, out var heel)) return heel == nummer;
        // het laatste groepje cijfers in de logwaarde, alleen als er een scheiding voor staat (MPR1-0032, niet MPR10032)
        var m = Regex.Match(inLog.Trim(), @"[^0-9A-Za-z](\d+)$");
        return m.Success && long.TryParse(m.Groups[1].Value, out var staart) && staart == nummer;
    }

    /// <summary>Stap Duurtest tonen? Alleen met een ingestelde PLC en een werkende filestore.</summary>
    public bool Beschikbaar => _host.Length > 0 && _store.Beschikbaar;

    public string Omschrijving => $"{_host}:{_map}";

    public record Resultaat(int FileStoreId, string Bestandsnaam, int Bestanden, long Bytes);

    /// <summary>
    /// De logmap ophalen, zippen en als document bij het exemplaar zetten; met historieregel.
    /// <paramref name="log"/> krijgt de voortgang regel voor regel (voor het scherm).
    /// </summary>
    public async Task<Resultaat> HaalLogAsync(int productId, string productNaam, string serienummer,
                                              int werknemerId, Action<string>? log = null, CancellationToken ct = default)
    {
        if (!Beschikbaar) throw new InvalidOperationException("Duurtest is niet ingesteld (Productie:Duurtest:FtpHost) of de filestore ontbreekt.");
        void Log(string t) { log?.Invoke(t); _log.LogInformation("Duurtest {Product} {Sn}: {Tekst}", productNaam, serienummer, t); }

        Log($"Verbinden met {_host}:{_poort} …");
        await using var ftp = new AsyncFtpClient(_host, _gebruiker, _wachtwoord, _poort);
        ftp.Config.ConnectTimeout = 10_000;
        ftp.Config.ReadTimeout = 30_000;
        ftp.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        ftp.Config.RetryAttempts = 3;
        await ftp.AutoConnect(ct);
        if (!ftp.IsConnected) throw new IOException($"Geen FTP-verbinding met {_host}. Staat de PLC aan en is poort {_poort} open?");

        if (!await ftp.DirectoryExists(_map, ct))
            throw new DirectoryNotFoundException($"Map '{_map}' bestaat niet op de PLC.");

        Log($"Map {_map} lezen …");
        var items = await ftp.GetListing(_map, FtpListOption.Recursive, ct);
        var bestanden = items.Where(i => i.Type == FtpObjectType.File).OrderBy(i => i.FullName).ToList();
        if (bestanden.Count == 0) throw new InvalidOperationException($"Map '{_map}' is leeg: is de test wel gelopen?");
        Log($"{bestanden.Count} bestand(en) gevonden, samen {Omvang(bestanden.Sum(b => Math.Max(0, b.Size)))}.");

        // Zip in het geheugen (logmap ≈ 1 MB); paden in de zip relatief aan de logmap.
        var zip = new MemoryStream();
        var gelezen = new List<(string Naam, byte[] Inhoud)>();
        long bytes = 0;
        using (var archief = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            var basis = _map.TrimEnd('/') + "/";
            foreach (var b in bestanden)
            {
                ct.ThrowIfCancellationRequested();
                var relatief = b.FullName.StartsWith(basis, StringComparison.OrdinalIgnoreCase)
                    ? b.FullName[basis.Length..] : b.Name;
                var inhoud = await ftp.DownloadBytes(b.FullName, ct);
                if (inhoud is null) throw new IOException($"'{b.FullName}' kon niet worden gedownload.");
                var regel = archief.CreateEntry(relatief, CompressionLevel.Optimal);
                if (b.Modified != DateTime.MinValue) regel.LastWriteTime = b.Modified;
                await using (var uit = regel.Open()) await uit.WriteAsync(inhoud, ct);
                gelezen.Add((relatief, inhoud));
                bytes += inhoud.Length;
                Log($"  {relatief} ({Omvang(inhoud.Length)})");
            }
        }
        await ftp.Disconnect(ct);

        // Is dit de log van het gekozen exemplaar? (er is nog niets opgeslagen)
        var controle = "";
        if (_snPatroon is not null)
        {
            if (ZoekSerienummer(gelezen, _snPatroon) is { } sn)
            {
                if (!ZelfdeSerienummer(sn.Serienummer, serienummer))
                    throw new InvalidOperationException(
                        $"De log op de testrig is van serienummer {sn.Serienummer} (in {sn.Bestand}), niet van {productNaam} {serienummer}. " +
                        "Er is niets opgeslagen. Kies het juiste product, of draai de test opnieuw.");
                Log($"Serienummer in de log: {sn.Serienummer} ({sn.Bestand}) ✓");
                controle = $"; serienummer {sn.Serienummer} in de log gecontroleerd";
            }
            else if (_snVerplicht)
            {
                throw new InvalidOperationException(
                    $"In de log op de testrig staat geen serienummer, dus niet te controleren of hij van {productNaam} {serienummer} is. Er is niets opgeslagen.");
            }
            else
            {
                Log("Geen serienummer in de log gevonden: niet gecontroleerd of dit de log van dit exemplaar is.");
                controle = "; geen serienummer in de log, niet gecontroleerd";
            }
        }

        zip.Position = 0;
        var veilig = string.Concat($"{productNaam}_{serienummer}".Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c));
        var naam = $"Duurtest_{veilig}_{DateTime.Now:yyyyMMdd-HHmm}.zip";
        Log($"Opslaan als document {naam} ({Omvang(zip.Length)}) …");
        var fileId = await _store.ToevoegenAsync(zip, naam, ProductVelden.StoreTypeProduct, productId, werknemerId);

        await _repo.VoegHistorieToeAsync(productId,
            $"Duurtest: log opgehaald van testrig ({_host}{_map}), {bestanden.Count} bestand(en), {Omvang(bytes)} → {naam}{controle}",
            ProductVelden.HistorieLevensloop, werknemerId, uitkomst: true);
        Log("Klaar.");
        return new Resultaat(fileId, naam, bestanden.Count, bytes);
    }

    private static string Omvang(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.#} kB",
        _ => $"{b / 1024.0 / 1024.0:0.##} MB",
    };
}
