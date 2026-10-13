namespace EverythingApp.Services;

/// <summary>
/// De foto van een artikel in de fotomap (Artikelen:FotoPad), zoals Access hem verwacht:
/// submap = artikelnummer / 1000 (acht cijfers), bestand = artikelnummer (acht cijfers) + extensie.
/// Gebruikt door het scherm Artikelen (klembord of bestand) en door POST api/artikelfoto/{nummer}.
/// </summary>
public class ArtikelFotoService
{
    private readonly string? _hoofdmap;
    private readonly ILogger<ArtikelFotoService> _log;

    public ArtikelFotoService(IConfiguration config, ILogger<ArtikelFotoService> log)
    {
        _hoofdmap = config["Artikelen:FotoPad"];
        _log = log;
    }

    public bool Beschikbaar => !string.IsNullOrWhiteSpace(_hoofdmap);

    public static string? Extensie(string? mime) => (mime ?? "").Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        _ => null,
    };

    /// <summary>Foto opslaan (oude foto's met dit nummer gaan weg). Levert een korte melding.</summary>
    public async Task<string> BewaarAsync(int nummer, string? mime, byte[] inhoud, string? door = null)
    {
        if (!Beschikbaar) throw new InvalidOperationException("Artikelen:FotoPad staat niet in appsettings.json.");
        var ext = Extensie(mime) ?? throw new ArgumentException($"Geen afbeelding ontvangen (type '{mime}').");
        if (inhoud.Length < 100) throw new ArgumentException("De afbeelding is leeg.");
        if (inhoud.Length > 20 * 1024 * 1024) throw new ArgumentException("De afbeelding is groter dan 20 MB.");

        var map = Path.Combine(_hoofdmap!, (nummer / 1000).ToString("D8"));
        var basis = nummer.ToString("D8");
        Directory.CreateDirectory(map);
        foreach (var oud in Directory.EnumerateFiles(map, basis + ".*").ToList())
            File.Delete(oud);
        await File.WriteAllBytesAsync(Path.Combine(map, basis + ext), inhoud);
        _log.LogInformation("Foto {Nummer} vervangen ({Bytes} bytes, {Ext}) door {Wie}", nummer, inhoud.Length, ext, door);
        return $"Foto opgeslagen ({Math.Max(1, inhoud.Length / 1024)} kB).";
    }
}
