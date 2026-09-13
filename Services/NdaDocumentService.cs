using System.Diagnostics;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace EverythingApp.Services;

/// <summary>
/// Maakt het NDA-document uit het bestaande Word-sjabloon en zet het desgewenst
/// om naar PDF. Dit is de C#-versie van wat de Python-app deed; het sjabloon
/// MDE_NDA_Template.docx blijft ongewijzigd, inclusief de plaatshouders
/// tussen blokhaken.
/// </summary>
public class NdaDocumentService
{
    private readonly IConfiguration _config;
    private readonly ILogger<NdaDocumentService> _log;

    public NdaDocumentService(IConfiguration config, ILogger<NdaDocumentService> log)
    {
        _config = config;
        _log = log;
    }

    // ============================================================
    //  Sjabloon
    // ============================================================

    /// <summary>Pad naar het sjabloon: uit appsettings, anders de meegeleverde kopie.</summary>
    public string? TemplatePad()
    {
        var uitConfig = _config["Nda:TemplatePad"];
        if (!string.IsNullOrWhiteSpace(uitConfig) && File.Exists(uitConfig)) return uitConfig;

        var naast = Path.Combine(AppContext.BaseDirectory, "Templates", "MDE_NDA_Template.docx");
        return File.Exists(naast) ? naast : null;
    }

    /// <summary>
    /// Vult het sjabloon en levert het Word-bestand als bytes.
    /// De sleutels zijn de plaatshouders inclusief blokhaken.
    /// </summary>
    public byte[] MaakWord(IDictionary<string, string> vervangingen)
    {
        var template = TemplatePad()
            ?? throw new FileNotFoundException(
                "NDA-sjabloon niet gevonden. Zet Nda:TemplatePad in appsettings.json of " +
                "plaats MDE_NDA_Template.docx in de map Templates naast de app.");

        var stroom = new MemoryStream();
        using (var bron = File.OpenRead(template)) bron.CopyTo(stroom);
        stroom.Position = 0;

        using (var doc = WordprocessingDocument.Open(stroom, true))
        {
            var hoofd = doc.MainDocumentPart?.Document?.Body;
            if (hoofd is not null) VervangIn(hoofd, vervangingen);

            // ook in kop- en voetteksten (daar staat vaak de partijnaam)
            foreach (var deel in doc.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
                if (deel.Header is not null) VervangIn(deel.Header, vervangingen);
            foreach (var deel in doc.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>())
                if (deel.Footer is not null) VervangIn(deel.Footer, vervangingen);
        }

        return stroom.ToArray();
    }

    /// <summary>
    /// Vervangt per alinea. Word knipt tekst vaak op in stukjes (runs), waardoor
    /// een plaatshouder over meerdere stukjes verdeeld kan staan. Daarom voegen we
    /// de tekst van de alinea samen, vervangen daarin, zetten het resultaat in het
    /// eerste stukje en maken de rest leeg — precies zoals de Python-versie deed.
    /// </summary>
    private static void VervangIn(OpenXmlElement wortel, IDictionary<string, string> vervangingen)
    {
        foreach (var alinea in wortel.Descendants<Paragraph>())
        {
            var stukjes = alinea.Descendants<Text>().ToList();
            if (stukjes.Count == 0) continue;

            var heel = string.Concat(stukjes.Select(t => t.Text));
            if (!vervangingen.Keys.Any(heel.Contains)) continue;

            var nieuw = heel;
            foreach (var (plaatshouder, waarde) in vervangingen)
                nieuw = nieuw.Replace(plaatshouder, waarde ?? "");

            stukjes[0].Text = nieuw;
            stukjes[0].Space = SpaceProcessingModeValues.Preserve;
            for (var i = 1; i < stukjes.Count; i++) stukjes[i].Text = "";

            // De plaatshouders staan in het sjabloon rood en vet; die opmaak
            // hoort niet bij de ingevulde tekst.
            if (stukjes[0].Parent is Run run && run.RunProperties is { } opmaak)
            {
                opmaak.RemoveAllChildren<Color>();
                opmaak.RemoveAllChildren<Bold>();
            }
        }
    }

    // ============================================================
    //  PDF via LibreOffice
    // ============================================================

    /// <summary>Pad naar soffice.exe, of null als LibreOffice niet gevonden is.</summary>
    public string? LibreOfficePad()
    {
        var uitConfig = _config["Nda:LibreOffice"];
        if (!string.IsNullOrWhiteSpace(uitConfig))
            return File.Exists(uitConfig) ? uitConfig : null;

        foreach (var kandidaat in new[]
        {
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
            "/usr/bin/soffice",
            "/usr/bin/libreoffice",
        })
            if (File.Exists(kandidaat)) return kandidaat;

        return null;
    }

    public bool PdfMogelijk => LibreOfficePad() is not null;

    /// <summary>
    /// Zet een bestand om naar PDF met LibreOffice. Werkt voor alles wat LibreOffice
    /// kan openen — een Word-document, maar ook een SVG (het typeplaatje). De
    /// extensie van 'bestandsnaam' bepaalt hoe LibreOffice het leest.
    ///
    /// Levert null als LibreOffice ontbreekt of de omzetting mislukt.
    /// </summary>
    public async Task<byte[]?> NaarPdfAsync(byte[] inhoud, string bestandsnaam)
    {
        var soffice = LibreOfficePad();
        if (soffice is null)
        {
            _log.LogInformation("Geen LibreOffice gevonden; omzetten naar PDF wordt overgeslagen.");
            return null;
        }

        var werkmap = Path.Combine(Path.GetTempPath(), "mde-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(werkmap);
        try
        {
            var naam = Path.GetFileNameWithoutExtension(bestandsnaam);
            if (string.IsNullOrWhiteSpace(naam)) naam = "document";
            var ext = Path.GetExtension(bestandsnaam);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".docx";

            var bronPad = Path.Combine(werkmap, naam + ext);
            await File.WriteAllBytesAsync(bronPad, inhoud);

            // Eigen gebruikersprofiel: anders weigert LibreOffice te starten als
            // er al een sessie van dezelfde gebruiker open staat.
            var profiel = Path.Combine(werkmap, "profiel");
            var start = new ProcessStartInfo(soffice)
            {
                WorkingDirectory = werkmap,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("-env:UserInstallation=file:///" + profiel.Replace('\\', '/'));
            start.ArgumentList.Add("--headless");
            start.ArgumentList.Add("--norestore");
            start.ArgumentList.Add("--invisible");
            start.ArgumentList.Add("--convert-to");
            start.ArgumentList.Add("pdf");
            start.ArgumentList.Add("--outdir");
            start.ArgumentList.Add(werkmap);
            start.ArgumentList.Add(bronPad);

            using var proces = Process.Start(start);
            if (proces is null) return null;

            using var afbreken = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await proces.WaitForExitAsync(afbreken.Token); }
            catch (OperationCanceledException)
            {
                try { proces.Kill(true); } catch { }
                _log.LogWarning("LibreOffice reageerde niet binnen twee minuten; PDF overgeslagen.");
                return null;
            }

            var pdfPad = Path.Combine(werkmap, naam + ".pdf");
            if (!File.Exists(pdfPad))
            {
                var fout = await proces.StandardError.ReadToEndAsync();
                _log.LogWarning("PDF-omzetting mislukte. LibreOffice zei: {Fout}", fout);
                return null;
            }

            return await File.ReadAllBytesAsync(pdfPad);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "PDF-omzetting mislukt");
            return null;
        }
        finally
        {
            try { Directory.Delete(werkmap, true); } catch { }
        }
    }
}
