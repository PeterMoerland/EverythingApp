using System.IO.Compression;
using FluentFTP;

namespace EverythingApp.Services;

/// <summary>
/// Duurtest van een product in de testrig. De PLC van de rig (Omron, FTP-server) schrijft
/// tijdens de test een logmap op zijn geheugenkaart; de PLC maakt die map leeg bij het starten
/// van een test. Na de test haalt de app-server de map op via FTP, zipt hem en zet hem als
/// document bij het exemplaar (StoreType product). Instellingen: Productie:Duurtest in
/// appsettings.json. Later komt hier ook de bediening van de rig bij (EtherNet/IP, zie OmronPlc).
/// </summary>
public class DuurtestService
{
    private readonly string _host;
    private readonly int _poort;
    private readonly string _gebruiker;
    private readonly string _wachtwoord;
    private readonly string _map;
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
                bytes += inhoud.Length;
                Log($"  {relatief} ({Omvang(inhoud.Length)})");
            }
        }
        await ftp.Disconnect(ct);

        zip.Position = 0;
        var veilig = string.Concat($"{productNaam}_{serienummer}".Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c));
        var naam = $"Duurtest_{veilig}_{DateTime.Now:yyyyMMdd-HHmm}.zip";
        Log($"Opslaan als document {naam} ({Omvang(zip.Length)}) …");
        var fileId = await _store.ToevoegenAsync(zip, naam, ProductVelden.StoreTypeProduct, productId, werknemerId);

        await _repo.VoegHistorieToeAsync(productId,
            $"Duurtest: log opgehaald van testrig ({_host}{_map}), {bestanden.Count} bestand(en), {Omvang(bytes)} → {naam}",
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
