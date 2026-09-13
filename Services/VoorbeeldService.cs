namespace EverythingApp.Services;

/// <summary>
/// Maakt van een bestand uit de filestore iets dat een browser kan tonen.
///
/// PDF's en afbeeldingen gaan er ongewijzigd doorheen. Word, Excel, PowerPoint en
/// een paar oudere formaten worden met LibreOffice omgezet naar PDF — dezelfde
/// LibreOffice die de NDA's en de laserlabels al maakt.
///
/// Het resultaat wordt bewaard in een tijdelijke map: een tweede keer openen is
/// dan meteen klaar. De naam bevat de datum van het bronbestand, dus na een
/// wijziging wordt er vanzelf opnieuw omgezet.
/// </summary>
public class VoorbeeldService
{
    private readonly FilestoreService _store;
    private readonly NdaDocumentService _omzetter;
    private readonly ILogger<VoorbeeldService> _log;

    private static readonly string[] OmzetBaar =
    {
        ".doc", ".docx", ".odt", ".rtf",
        ".xls", ".xlsx", ".ods", ".csv",
        ".ppt", ".pptx", ".odp",
    };

    public VoorbeeldService(FilestoreService store, NdaDocumentService omzetter,
                            ILogger<VoorbeeldService> log)
    {
        _store = store;
        _omzetter = omzetter;
        _log = log;
    }

    /// <summary>Kan dit soort bestand in beeld gebracht worden?</summary>
    public static bool Toonbaar(string bestandsnaam)
    {
        var ext = Path.GetExtension(bestandsnaam).ToLowerInvariant();
        return ext is ".pdf" or ".jpg" or ".jpeg" or ".png" or ".gif" or ".svg"
            || OmzetBaar.Contains(ext);
    }

    /// <summary>Moet er eerst omgezet worden voordat de browser het kan tonen?</summary>
    public static bool MoetOmgezet(string bestandsnaam)
        => OmzetBaar.Contains(Path.GetExtension(bestandsnaam).ToLowerInvariant());

    /// <summary>
    /// Pad en mediatype van iets dat de browser kan tonen, of null als dat niet lukt.
    /// </summary>
    public async Task<(string Pad, string Mime)?> VoorbeeldAsync(int fileStoreId)
    {
        var gevonden = await _store.ZoekAsync(fileStoreId);
        if (gevonden is null) return null;

        var (pad, naam, _) = gevonden.Value;

        if (!MoetOmgezet(naam))
            return (pad, FilestoreService.MimeVoor(naam));

        // omgezette versie hergebruiken zolang het bronbestand niet wijzigde
        var stempel = File.GetLastWriteTimeUtc(pad).Ticks;
        var map = Path.Combine(Path.GetTempPath(), "mde-voorbeeld");
        Directory.CreateDirectory(map);
        var doel = Path.Combine(map, $"{fileStoreId}-{stempel}.pdf");

        if (File.Exists(doel)) return (doel, "application/pdf");

        try
        {
            var pdf = await _omzetter.NaarPdfAsync(await File.ReadAllBytesAsync(pad), naam);
            if (pdf is null) return null;

            await File.WriteAllBytesAsync(doel, pdf);
            OpruimenOud(map);
            return (doel, "application/pdf");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Voorbeeld van bestand {Id} maken mislukte", fileStoreId);
            return null;
        }
    }

    /// <summary>Houdt de tijdelijke map klein: alles ouder dan een week eruit.</summary>
    private static void OpruimenOud(string map)
    {
        try
        {
            var grens = DateTime.UtcNow.AddDays(-7);
            foreach (var bestand in Directory.EnumerateFiles(map, "*.pdf"))
                if (File.GetLastWriteTimeUtc(bestand) < grens)
                    try { File.Delete(bestand); } catch { }
        }
        catch { /* opruimen is nooit belangrijk genoeg om iets te laten mislukken */ }
    }
}
