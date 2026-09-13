using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace EverythingApp.Services;

/// <summary>
/// Bouwt de JLCPCB-bestanden op vanuit een Fusion CAM-outputmap, naast de
/// bestaande Eurocircuits-omzetting (EurocircuitsExport.cs):
///
///   &lt;map&gt;\GerberFiles\*  →  &lt;map&gt;\@JLCPCB\&lt;project&gt;.zip      (zelfde zip
///                            als voor Eurocircuits: plat gezipt)
///   &lt;map&gt;\Assembly\*CPL* →  &lt;map&gt;\@JLCPCB\&lt;project&gt; CPL.csv  (kolommen
///                            Designator, Mid X, Mid Y, Layer, Rotation;
///                            front = T, back = B, maten met 'mm' erachter)
///   &lt;map&gt;\Assembly\*BOM* →  &lt;map&gt;\@JLCPCB\&lt;project&gt; BOM.csv  (kolommen
///                            Comment, Designator, Footprint, JLCPCB Part #)
///
/// De Fusion-kolommen worden op naam opgezocht (met een lijstje gangbare
/// namen per doelkolom). Ontbreekt er een, dan zegt de resultaatregel
/// precies welke — en welke kolommen er wél in het bestand staan.
/// </summary>
public static class JlcpcbExport
{
    public const string OutputFolderName = "@JLCPCB";

    /// <summary>Per doelkolom de namen waaronder Fusion hem kan exporteren.</summary>
    private static readonly string[] KolomDesignator = { "Name", "Designator", "RefDes", "REFDES", "Ref", "Component" };
    private static readonly string[] KolomX = { "Mid X", "MidX", "X", "X(mm)", "X (mm)", "PosX", "Pos X", "Center-X(mm)", "Center-X" };
    private static readonly string[] KolomY = { "Mid Y", "MidY", "Y", "Y(mm)", "Y (mm)", "PosY", "Pos Y", "Center-Y(mm)", "Center-Y" };
    private static readonly string[] KolomRotatie = { "Rotation", "Rot", "Angle" };
    private static readonly string[] KolomComment = { "Value", "Comment", "DEVICE", "Device" };
    private static readonly string[] KolomParts = { "Parts", "Reference Designator", "Designator", "REFDES" };
    private static readonly string[] KolomFootprint = { "PACKAGE", "Package", "Footprint" };
    private static readonly string[] KolomJlcNummer = { "JLCPCB Part #", "JLCPCB Part", "JLCPCB", "LCSC Part #", "LCSC Part", "LCSC" };

    /// <summary>Voert alle exportstappen uit op de opgegeven CAM-outputmap.
    /// <paramref name="cNummers"/>: MPN → C-nummer uit de lokale kopie van de
    /// JLCPCB-bibliotheek, om de kolom 'JLCPCB Part #' te vullen.</summary>
    public static List<EurocircuitsExport.ExportResult> RunAll(
        string rootFolder, IReadOnlyDictionary<string, string>? cNummers = null)
    {
        var project = EurocircuitsExport.GetProjectName(rootFolder);

        // Componenten met Manufacturer 'MDE' plaatst de leverancier niet:
        // die vervallen in de BOM én (op designator) in de CPL.
        var mde = EurocircuitsExport.MdeDesignators(rootFolder);

        return new List<EurocircuitsExport.ExportResult>
        {
            CreateGerberZip(rootFolder, project),
            CreateCplFile(rootFolder, project, mde),
            CreateBomFile(rootFolder, project, cNummers),
        };
    }

    private static string EnsureOutputFolder(string rootFolder)
    {
        var outputDir = Path.Combine(rootFolder, OutputFolderName);
        Directory.CreateDirectory(outputDir);
        return outputDir;
    }

    // ================================================================
    //  Gerber: dezelfde platte zip als voor Eurocircuits
    // ================================================================

    public static EurocircuitsExport.ExportResult CreateGerberZip(string rootFolder, string projectName)
    {
        var result = new EurocircuitsExport.ExportResult { StepName = "Gerber zip" };

        var gerberDir = Path.Combine(rootFolder, EurocircuitsExport.GerberFolderName);
        if (!Directory.Exists(gerberDir))
        {
            result.Message = $"Submap '{EurocircuitsExport.GerberFolderName}' niet gevonden";
            return result;
        }

        var files = Directory.GetFiles(gerberDir, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            result.Message = $"Submap '{EurocircuitsExport.GerberFolderName}' bevat geen bestanden";
            return result;
        }

        try
        {
            var zipName = $"{EurocircuitsExport.SafeFileName(projectName)}.zip";
            var zipPath = Path.Combine(EnsureOutputFolder(rootFolder), zipName);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            ZipFile.CreateFromDirectory(gerberDir, zipPath, CompressionLevel.Optimal,
                                        includeBaseDirectory: false);

            result.Success = true;
            result.OutputPath = zipPath;
            result.ItemCount = files.Length;
            result.Message = $"{zipName}: {files.Length} bestand(en)";
        }
        catch (Exception ex)
        {
            result.Message = $"Zip maken mislukt: {ex.Message}";
        }

        return result;
    }

    // ================================================================
    //  CPL: Designator, Mid X, Mid Y, Layer, Rotation
    // ================================================================

    public static EurocircuitsExport.ExportResult CreateCplFile(string rootFolder, string projectName,
                                                                HashSet<string>? weglaten = null)
    {
        var result = new EurocircuitsExport.ExportResult { StepName = "CPL" };

        var assemblyDir = Path.Combine(rootFolder, EurocircuitsExport.AssemblyFolderName);
        if (!Directory.Exists(assemblyDir))
        {
            result.Message = $"Submap '{EurocircuitsExport.AssemblyFolderName}' niet gevonden";
            return result;
        }

        var frontFile = EurocircuitsExport.FindCplFile(assemblyDir, "front");
        var backFile = EurocircuitsExport.FindCplFile(assemblyDir, "back");
        if (frontFile is null && backFile is null)
        {
            result.Message = $"Geen CPL-files (front/back) gevonden in '{EurocircuitsExport.AssemblyFolderName}'";
            return result;
        }

        try
        {
            var rows = new List<string> { "Designator,Mid X,Mid Y,Layer,Rotation" };
            var warnings = new List<string>();
            var overgeslagen = 0;

            foreach (var (pad, laag) in new[] { (frontFile, "T"), (backFile, "B") })
            {
                if (pad is null)
                {
                    warnings.Add(laag == "T" ? "geen 'front'-file" : "geen 'back'-file");
                    continue;
                }

                var lines = File.ReadAllLines(pad).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (lines.Count < 2) { warnings.Add($"{Path.GetFileName(pad)} is leeg"); continue; }

                var scheider = Scheider(lines[0]);
                var kop = EurocircuitsExport.SplitsCsv(lines[0], scheider)
                                            .Select(EurocircuitsExport.Kaal).ToList();

                var iNaam = Vind(kop, KolomDesignator);
                var iX = Vind(kop, KolomX);
                var iY = Vind(kop, KolomY);
                var iRot = Vind(kop, KolomRotatie);
                if (iNaam < 0 || iX < 0 || iY < 0 || iRot < 0)
                {
                    result.Message = MistKolommen(Path.GetFileName(pad), kop,
                        (iNaam, "designator"), (iX, "X"), (iY, "Y"), (iRot, "rotatie"));
                    return result;
                }

                for (var i = 1; i < lines.Count; i++)
                {
                    var velden = EurocircuitsExport.SplitsCsv(lines[i], scheider);
                    if (velden.Count <= Math.Max(Math.Max(iNaam, iRot), Math.Max(iX, iY))) continue;

                    // MDE-componenten plaatst de leverancier niet
                    if (weglaten is { Count: > 0 } &&
                        weglaten.Contains(EurocircuitsExport.Kaal(velden[iNaam])))
                    {
                        overgeslagen++;
                        continue;
                    }

                    // Alle waardes tussen aanhalingstekens, net als in de BOM
                    rows.Add(string.Join(",",
                        Q(EurocircuitsExport.Kaal(velden[iNaam])),
                        Q(MetMm(EurocircuitsExport.Kaal(velden[iX]))),
                        Q(MetMm(EurocircuitsExport.Kaal(velden[iY]))),
                        Q(laag),
                        Q(EurocircuitsExport.Kaal(velden[iRot]))));
                }
            }

            if (rows.Count == 1) { result.Message = "Geen CPL-regels gevonden"; return result; }

            var cplName = $"{EurocircuitsExport.SafeFileName(projectName)} CPL.csv";
            var cplPath = Path.Combine(EnsureOutputFolder(rootFolder), cplName);
            File.WriteAllLines(cplPath, rows, new UTF8Encoding(false));

            result.Success = true;
            result.OutputPath = cplPath;
            result.ItemCount = rows.Count - 1;
            result.Message = $"{cplName}: {rows.Count - 1} component(en)"
                           + (overgeslagen > 0 ? $", {overgeslagen} MDE-component(en) overgeslagen" : "");
            if (warnings.Count > 0) result.Message += $" (let op: {string.Join("; ", warnings)})";
        }
        catch (Exception ex)
        {
            result.Message = $"CPL maken mislukt: {ex.Message}";
        }

        return result;
    }

    // ================================================================
    //  BOM: Comment, Designator, Footprint, JLCPCB Part #
    // ================================================================

    public static EurocircuitsExport.ExportResult CreateBomFile(string rootFolder, string projectName,
                                                                IReadOnlyDictionary<string, string>? cNummers = null)
    {
        var result = new EurocircuitsExport.ExportResult { StepName = "BOM" };

        var assemblyDir = Path.Combine(rootFolder, EurocircuitsExport.AssemblyFolderName);
        if (!Directory.Exists(assemblyDir))
        {
            result.Message = $"Submap '{EurocircuitsExport.AssemblyFolderName}' niet gevonden";
            return result;
        }

        var bomFile = EurocircuitsExport.FindBomFile(assemblyDir);
        if (bomFile is null)
        {
            result.Message = $"Geen BOM-file gevonden in '{EurocircuitsExport.AssemblyFolderName}'";
            return result;
        }

        try
        {
            // Dezelfde lezer als Eurocircuits: HTML gaat vóór CSV en herstelt
            // Fusion's komma-splitsing (zie EurocircuitsExport.LeesBom).
            var gelezen = EurocircuitsExport.LeesBom(bomFile);
            if (gelezen is null) { result.Message = "De BOM-file is leeg"; return result; }
            var (kop, rijen) = gelezen.Value;

            // Comment krijgt de MPN (keten 'Manufacturer Part Number' → 'MPN');
            // alleen als die leeg is valt hij terug op de Value/Device.
            var iMpnLang = Vind(kop, new[] { "Manufacturer Part Number" });
            var iMpnKort = Vind(kop, new[] { "MPN" });
            var iValue = Vind(kop, KolomComment);
            var iParts = Vind(kop, KolomParts);
            var iFoot = Vind(kop, KolomFootprint);
            var iJlc = Vind(kop, KolomJlcNummer);   // optioneel; -1 = kolom leeg laten
            var iFabrikant = Vind(kop, new[] { "Manufacturer" });   // voor het MDE-filter
            if ((iMpnLang < 0 && iMpnKort < 0 && iValue < 0) || iParts < 0 || iFoot < 0)
            {
                result.Message = MistKolommen(Path.GetFileName(bomFile), kop,
                    (Math.Max(iMpnLang, Math.Max(iMpnKort, iValue)), "comment/MPN"),
                    (iParts, "designators"), (iFoot, "footprint"));
                return result;
            }

            var rows = new List<string> { "Comment,Designator,Footprint,JLCPCB Part #" };
            var overgeslagen = 0;
            var uitBieb = 0;
            foreach (var velden in rijen)
            {
                string Veld(int i) => i >= 0 && i < velden.Count ? velden[i] : "";

                if (Veld(iParts).Length == 0) continue;

                // MDE-componenten plaatst de leverancier niet
                if (Veld(iFabrikant).Equals("MDE", StringComparison.OrdinalIgnoreCase))
                {
                    overgeslagen++;
                    continue;
                }

                var comment = Veld(iMpnLang);
                if (comment.Length == 0) comment = Veld(iMpnKort);
                if (comment.Length == 0) comment = Veld(iValue);

                // Designators netjes als 'R5,R6' — zonder spaties, zoals het voorbeeld
                var designators = string.Join(",",
                    Veld(iParts).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));

                // JLCPCB Part #: uit de bron als die kolom er is, anders
                // opgezocht in de lokale kopie van de JLCPCB-bibliotheek.
                var cNummer = Veld(iJlc);
                if (cNummer.Length == 0 && cNummers is not null &&
                    cNummers.TryGetValue(comment, out var gevonden))
                {
                    cNummer = gevonden;
                    uitBieb++;
                }

                // Alle waardes tussen aanhalingstekens, ook zonder komma erin
                rows.Add(string.Join(",",
                    Q(comment), Q(designators), Q(Veld(iFoot)), Q(cNummer)));
            }

            var bomName = $"{EurocircuitsExport.SafeFileName(projectName)} BOM.csv";
            var target = Path.Combine(EnsureOutputFolder(rootFolder), bomName);
            File.WriteAllLines(target, rows, new UTF8Encoding(false));

            result.Success = true;
            result.OutputPath = target;
            result.ItemCount = rows.Count - 1;
            result.Message = $"{bomName}: {rows.Count - 1} regel(s)"
                           + (overgeslagen > 0 ? $", {overgeslagen} MDE-regel(s) overgeslagen" : "")
                           + (uitBieb > 0 ? $", {uitBieb} C-nummer(s) uit de bibliotheek gevuld" : "");
        }
        catch (Exception ex)
        {
            result.Message = $"BOM maken mislukt: {ex.Message}";
        }

        return result;
    }

    // ================================================================
    //  Hulpjes
    // ================================================================

    private static char Scheider(string kop) => !kop.Contains(',') && kop.Contains(';') ? ';' : ',';

    /// <summary>Index van de eerste kandidaat die in de kopregel voorkomt, anders -1.
    /// Vergelijkt via KolomGelijk: hoofdletter- en underscore-ongevoelig.</summary>
    private static int Vind(List<string> kop, string[] kandidaten)
    {
        foreach (var naam in kandidaten)
        {
            var i = kop.FindIndex(k => EurocircuitsExport.KolomGelijk(k, naam));
            if (i >= 0) return i;
        }
        return -1;
    }

    private static string MistKolommen(string bestand, List<string> kop,
                                       params (int Index, string Naam)[] kolommen)
    {
        var mist = kolommen.Where(k => k.Index < 0).Select(k => k.Naam);
        return $"In {bestand} is geen kolom gevonden voor: {string.Join(", ", mist)}. " +
               $"De kopregel bevat: {string.Join(", ", kop)}.";
    }

    /// <summary>Kale meetwaarde → '12.3456mm'; staat er al een eenheid, dan blijft die staan.</summary>
    private static string MetMm(string waarde)
    {
        waarde = waarde.Trim();
        return double.TryParse(waarde, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? waarde + "mm" : waarde;
    }

    /// <summary>Zet een waarde CSV-veilig neer: quotes eromheen zodra er een
    /// komma, quote of puntkomma in zit.</summary>
    private static string Csv(string waarde)
        => waarde.IndexOfAny(new[] { ',', '"', ';' }) < 0
            ? waarde
            : "\"" + waarde.Replace("\"", "\"\"") + "\"";

    /// <summary>Altijd tussen aanhalingstekens — zo wil JLCPCB de BOM.</summary>
    private static string Q(string waarde)
        => "\"" + waarde.Replace("\"", "\"\"") + "\"";
}
