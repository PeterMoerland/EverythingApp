using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace EverythingApp.Services;

/// <summary>
/// Bouwt de Eurocircuits-bestanden op vanuit een Fusion CAM-outputmap.
/// Eén-op-één geport uit de eerdere Windows-tool (FusionToCam,
/// EurocircuitsExport.vb) zodat het gedrag exact gelijk blijft:
///
///   &lt;map&gt;\GerberFiles\*  →  &lt;map&gt;\@Eurocircuits\&lt;project&gt;.zip   (plat gezipt)
///   &lt;map&gt;\Assembly\*CPL* →  &lt;map&gt;\@Eurocircuits\&lt;project&gt; CPL.csv (front+back
///                            samengevoegd, extra kolom Side: Top/Bottom)
///   &lt;map&gt;\Assembly\*BOM* →  &lt;map&gt;\@Eurocircuits\&lt;project&gt; BOM.csv (kopie)
///
/// De projectnaam wordt uit de bestandsnamen in Assembly afgeleid, zoals de
/// tool dat deed ('M-Pede Display ECAT-BOM.csv' → 'M-Pede Display ECAT').
/// </summary>
public static class EurocircuitsExport
{
    public const string OutputFolderName = "@Eurocircuits";
    public const string GerberFolderName = "GerberFiles";
    public const string AssemblyFolderName = "Assembly";

    /// <summary>Gebruikt als de projectnaam niet uit de Assembly-files te halen is.</summary>
    public const string FallbackProjectName = "Project";

    /// <summary>Resultaat van één exportstap.</summary>
    public sealed class ExportResult
    {
        public string StepName { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public int ItemCount { get; set; }
    }

    /// <summary>Voert alle exportstappen uit op de opgegeven CAM-outputmap.</summary>
    public static List<ExportResult> RunAll(string rootFolder)
    {
        var project = GetProjectName(rootFolder);
        var mde = MdeDesignators(rootFolder);

        return new List<ExportResult>
        {
            CreateGerberZip(rootFolder, project),
            CreateCplFile(rootFolder, project, mde),
            CopyBomFile(rootFolder, project),
        };
    }

    // ================================================================
    //  MDE-componenten: maken we zelf, plaatst de leverancier niet
    // ================================================================

    /// <summary>Kolomnamen waaronder de designator in een CPL-file kan staan.</summary>
    internal static readonly string[] DesignatorKandidaten =
        { "Name", "Designator", "RefDes", "REFDES", "Ref", "Component" };

    internal static int DesignatorIndex(List<string> kop)
    {
        foreach (var naam in DesignatorKandidaten)
        {
            var i = kop.FindIndex(k => KolomGelijk(k, naam));
            if (i >= 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// Kolomnaamvergelijking: hoofdletterongevoelig, en een underscore telt
    /// als spatie — Fusion schrijft soms 'Manufacturer_Part_Number' waar
    /// 'Manufacturer Part Number' bedoeld is.
    /// </summary>
    internal static bool KolomGelijk(string kolom, string naam)
        => kolom.Replace('_', ' ').Trim()
                .Equals(naam.Replace('_', ' ').Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// De designators (C1, R2, …) van componenten met Manufacturer 'MDE',
    /// uit de BOM-file. Daarmee filteren de CPL-stappen (Eurocircuits én
    /// JLCPCB) diezelfde componenten uit het plaatsingsbestand.
    /// </summary>
    internal static HashSet<string> MdeDesignators(string rootFolder)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var assemblyDir = Path.Combine(rootFolder, AssemblyFolderName);
        if (!Directory.Exists(assemblyDir)) return set;
        var bomFile = FindBomFile(assemblyDir);
        if (bomFile is null) return set;

        try
        {
            var bom = LeesBom(bomFile);
            if (bom is null) return set;
            var (kop, rijen) = bom.Value;

            var fabrikant = kop.FindIndex(k => KolomGelijk(k, "Manufacturer"));
            var parts = kop.FindIndex(k => KolomGelijk(k, "Parts"));
            if (parts < 0)
                parts = kop.FindIndex(k => KolomGelijk(k, "Reference Designator"));
            if (fabrikant < 0 || parts < 0) return set;

            foreach (var velden in rijen)
            {
                if (fabrikant >= velden.Count || parts >= velden.Count) continue;
                if (!velden[fabrikant].Equals("MDE", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var d in velden[parts]
                             .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    set.Add(d);
            }
        }
        catch { /* geen filter is vervelend, maar geen reden de export te blokkeren */ }

        return set;
    }

    /// <summary>Zorgt dat &lt;root&gt;\@Eurocircuits bestaat en geeft het pad terug.</summary>
    public static string EnsureOutputFolder(string rootFolder)
    {
        var outputDir = Path.Combine(rootFolder, OutputFolderName);
        Directory.CreateDirectory(outputDir);
        return outputDir;
    }

    // ================================================================
    //  Projectnaam
    // ================================================================

    /// <summary>
    /// Leidt de PCB-projectnaam af uit de bestandsnamen in &lt;root&gt;\Assembly.
    /// 'M-Pede Display ECAT-BOM.csv' en 'PnP_M-Pede Display ECAT_CPL_front.csv'
    /// geven beide 'M-Pede Display ECAT'.
    /// </summary>
    public static string GetProjectName(string rootFolder)
    {
        var assemblyDir = Path.Combine(rootFolder, AssemblyFolderName);
        if (!Directory.Exists(assemblyDir)) return FallbackProjectName;

        // Eerst uit de BOM-naam, die het minst versierd is.
        var bom = FindBomFile(assemblyDir);
        if (bom is not null)
        {
            var naam = StripBomSuffix(Path.GetFileNameWithoutExtension(bom));
            if (naam.Length > 0) return naam;
        }

        // Anders uit een van de CPL-files.
        foreach (var side in new[] { "front", "back" })
        {
            var cpl = FindCplFile(assemblyDir, side);
            if (cpl is not null)
            {
                var naam = StripCplDecoration(Path.GetFileNameWithoutExtension(cpl));
                if (naam.Length > 0) return naam;
            }
        }

        return FallbackProjectName;
    }

    /// <summary>Haalt '-BOM' / '_BOM' / ' BOM' van het eind van de naam af.</summary>
    private static string StripBomSuffix(string naam)
    {
        var idx = naam.LastIndexOf("BOM", StringComparison.OrdinalIgnoreCase);
        if (idx > 0 && idx + 3 == naam.Length) naam = naam[..idx];
        return naam.Trim(' ', '-', '_');
    }

    /// <summary>Haalt de 'PnP_'-prefix en de '_CPL_front/back'-suffix van de naam af.</summary>
    private static string StripCplDecoration(string naam)
    {
        if (naam.StartsWith("PnP_", StringComparison.OrdinalIgnoreCase)) naam = naam[4..];

        var idx = naam.LastIndexOf("CPL", StringComparison.OrdinalIgnoreCase);
        if (idx > 0) naam = naam[..idx];

        return naam.Trim(' ', '-', '_');
    }

    /// <summary>Verwijdert tekens die niet in een bestandsnaam mogen.</summary>
    internal static string SafeFileName(string naam)
    {
        var sb = new StringBuilder();
        var ongeldig = Path.GetInvalidFileNameChars();

        foreach (var teken in naam)
            sb.Append(Array.IndexOf(ongeldig, teken) >= 0 ? '_' : teken);

        var schoon = sb.ToString().Trim();
        return schoon.Length == 0 ? FallbackProjectName : schoon;
    }

    // ================================================================
    //  De drie stappen
    // ================================================================

    /// <summary>
    /// Maakt &lt;root&gt;\@Eurocircuits\&lt;project&gt;.zip met de inhoud van
    /// &lt;root&gt;\GerberFiles. De bestanden staan in de root van de zip,
    /// zoals Eurocircuits verwacht.
    /// </summary>
    public static ExportResult CreateGerberZip(string rootFolder, string projectName)
    {
        var result = new ExportResult { StepName = "Gerber zip" };

        var gerberDir = Path.Combine(rootFolder, GerberFolderName);
        if (!Directory.Exists(gerberDir))
        {
            result.Message = $"Submap '{GerberFolderName}' niet gevonden";
            return result;
        }

        var files = Directory.GetFiles(gerberDir, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            result.Message = $"Submap '{GerberFolderName}' bevat geen bestanden";
            return result;
        }

        try
        {
            var zipName = $"{SafeFileName(projectName)}.zip";
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

    /// <summary>
    /// Voegt de CPL-files uit &lt;root&gt;\Assembly (..._front.csv en ..._back.csv)
    /// samen tot &lt;root&gt;\@Eurocircuits\&lt;project&gt; CPL.csv met een extra kolom
    /// 'Side' (Top voor front, Bottom voor back). Regels worden ongewijzigd
    /// overgenomen, zodat aanhalingstekens in velden intact blijven — behalve
    /// componenten uit <paramref name="weglaten"/> (MDE-componenten, die de
    /// leverancier niet plaatst): die regels vervallen.
    /// </summary>
    public static ExportResult CreateCplFile(string rootFolder, string projectName,
                                             HashSet<string>? weglaten = null)
    {
        var result = new ExportResult { StepName = "CPL" };

        var assemblyDir = Path.Combine(rootFolder, AssemblyFolderName);
        if (!Directory.Exists(assemblyDir))
        {
            result.Message = $"Submap '{AssemblyFolderName}' niet gevonden";
            return result;
        }

        var frontFile = FindCplFile(assemblyDir, "front");
        var backFile = FindCplFile(assemblyDir, "back");

        if (frontFile is null && backFile is null)
        {
            result.Message = $"Geen CPL-files (front/back) gevonden in '{AssemblyFolderName}'";
            return result;
        }

        try
        {
            string? header = null;
            var rows = new List<string>();
            var warnings = new List<string>();
            var overgeslagen = 0;

            // Front eerst (Top), daarna back (Bottom).
            foreach (var (cplPathIn, side) in new[] { (frontFile, "Top"), (backFile, "Bottom") })
            {
                if (cplPathIn is null)
                {
                    warnings.Add($"geen '{side.ToLowerInvariant()}'-file");
                    continue;
                }

                var lines = File.ReadAllLines(cplPathIn)
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .ToList();

                if (lines.Count == 0)
                {
                    warnings.Add($"{Path.GetFileName(cplPathIn)} is leeg");
                    continue;
                }

                var fileHeader = lines[0].TrimEnd(',');

                if (header is null)
                    header = fileHeader + ",Side";
                else if (!string.Equals(header, fileHeader + ",Side", StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"header van {Path.GetFileName(cplPathIn)} wijkt af");

                // De designator-kolom opzoeken, alleen nodig voor het MDE-filter.
                var iNaam = -1;
                if (weglaten is { Count: > 0 })
                {
                    iNaam = DesignatorIndex(SplitsCsv(fileHeader, ',').Select(Kaal).ToList());
                    if (iNaam < 0)
                        warnings.Add($"{Path.GetFileName(cplPathIn)}: geen designator-kolom, MDE-filter niet toegepast");
                }

                // Regel 1 is de header en wordt overgeslagen.
                for (var i = 1; i < lines.Count; i++)
                {
                    if (iNaam >= 0)
                    {
                        var velden = SplitsCsv(lines[i], ',');
                        if (iNaam < velden.Count && weglaten!.Contains(Kaal(velden[iNaam])))
                        {
                            overgeslagen++;
                            continue;
                        }
                    }
                    rows.Add(lines[i].TrimEnd() + "," + side);
                }
            }

            if (rows.Count == 0)
            {
                result.Message = "Geen CPL-regels gevonden";
                return result;
            }

            var cplName = $"{SafeFileName(projectName)} CPL.csv";
            var cplPath = Path.Combine(EnsureOutputFolder(rootFolder), cplName);

            using (var writer = new StreamWriter(cplPath, append: false,
                                                 encoding: new UTF8Encoding(false)))
            {
                writer.WriteLine(header);
                foreach (var row in rows) writer.WriteLine(row);
            }

            result.Success = true;
            result.OutputPath = cplPath;
            result.ItemCount = rows.Count;
            result.Message = $"{cplName}: {rows.Count} component(en)"
                           + (overgeslagen > 0 ? $", {overgeslagen} MDE-component(en) overgeslagen" : "");
            if (warnings.Count > 0)
                result.Message += $" (let op: {string.Join("; ", warnings)})";
        }
        catch (Exception ex)
        {
            result.Message = $"CPL maken mislukt: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Bouwt uit de BOM-file in &lt;root&gt;\Assembly de Eurocircuits-BOM
    /// &lt;root&gt;\@Eurocircuits\&lt;project&gt; BOM.csv, met precies de kolommen
    /// die Eurocircuits wil (zie VerbouwBom); alle andere kolommen vervallen.
    /// </summary>
    public static ExportResult CopyBomFile(string rootFolder, string projectName)
    {
        var result = new ExportResult { StepName = "BOM" };

        var assemblyDir = Path.Combine(rootFolder, AssemblyFolderName);
        if (!Directory.Exists(assemblyDir))
        {
            result.Message = $"Submap '{AssemblyFolderName}' niet gevonden";
            return result;
        }

        var bomFile = FindBomFile(assemblyDir);
        if (bomFile is null)
        {
            result.Message = $"Geen BOM-file gevonden in '{AssemblyFolderName}'";
            return result;
        }

        try
        {
            var bomName = $"{SafeFileName(projectName)} BOM.csv";
            var target = Path.Combine(EnsureOutputFolder(rootFolder), bomName);

            var bom = LeesBom(bomFile);
            if (bom is null)
            {
                result.Message = $"{Path.GetFileName(bomFile)} is leeg of onbruikbaar";
                return result;
            }
            var (regels, melding) = VerbouwBom(bom.Value.Kop, bom.Value.Rijen);
            File.WriteAllLines(target, regels);

            result.Success = true;
            result.OutputPath = target;
            result.ItemCount = 1;
            result.Message = $"{bomName}: van {Path.GetFileName(bomFile)}, {melding}";
        }
        catch (Exception ex)
        {
            result.Message = $"BOM kopiëren mislukt: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Bouwt de Eurocircuits-BOM op uit de Fusion-BOM: alleen de kolommen
    /// MPN, Reference Designator (uit 'Parts'), Description, Value en
    /// Quantity (uit 'Qty'), in die volgorde. Eurocircuits doet alleen iets
    /// met de MPN-kolom, dus daar komt het GPN in te staan als de add-in er
    /// een koos (dat herkent hun systeem als eigen standaardcomponent), en
    /// anders de gewone keten 'Manufacturer Part Number' → 'MPN' → 'Device'.
    /// Alle andere kolommen (GPN, Device, REFDES, PACKAGE, …) vervallen.
    /// De waardes onder MPN staan altijd tussen aanhalingstekens;
    /// de rest alleen als het CSV-technisch moet (komma in de waarde).
    /// Regels met Manufacturer 'MDE' vervallen: eigen componenten die de
    /// leverancier niet plaatst.
    /// </summary>
    private static (string[] Regels, string Melding) VerbouwBom(List<string> kopVelden,
                                                                List<List<string>> rijen)
    {
        // Wat Eurocircuits krijgt, in deze volgorde: (kolomnaam in de output,
        // namen waaronder hij in de bron kan staan, altijd quotes?, verplicht?)
        var doel = new (string Naam, string[] Bron, bool Quotes, bool Verplicht)[]
        {
            ("MPN", new[] { "Manufacturer Part Number", "MPN" }, true, true),
            ("Reference Designator", new[] { "Parts", "Reference Designator" }, false, true),
            ("Description", new[] { "Description" }, false, true),
            ("Value", new[] { "Value" }, false, true),
            ("Quantity", new[] { "Qty", "Quantity" }, false, true),
        };

        var bronIndex = new int[doel.Length];
        var mist = new List<string>();
        for (var d = 0; d < doel.Length; d++)
        {
            bronIndex[d] = -1;
            foreach (var naam in doel[d].Bron)
            {
                var i = kopVelden.FindIndex(k => KolomGelijk(k, naam));
                if (i >= 0) { bronIndex[d] = i; break; }
            }
            if (bronIndex[d] < 0 && doel[d].Verplicht) mist.Add(doel[d].Naam);
        }
        if (mist.Count > 0)
            throw new InvalidOperationException(
                $"in de BOM is geen kolom gevonden voor: {string.Join(", ", mist)}. " +
                $"De kopregel bevat: {string.Join(", ", kopVelden)}");

        // Componenten met Manufacturer 'MDE' maken we zelf; die hoeft de
        // leverancier niet te plaatsen en gaan dus niet mee in de BOM.
        var fabrikant = kopVelden.FindIndex(k => KolomGelijk(k, "Manufacturer"));
        var overgeslagen = 0;

        // Positie in 'doel' hierboven: 0 = MPN. De MPN-kolom krijgt het GPN
        // uit de bron als dat gevuld is (Eurocircuits herkent zijn eigen
        // nummers alleen in de MPN-kolom), anders de eerste gevulde van
        // 'Manufacturer Part Number', 'MPN', en als redmiddel 'Device'.
        const int MpnDoel = 0;
        var gpnBron = kopVelden.FindIndex(k => KolomGelijk(k, "GPN"));
        var deviceBron = kopVelden.FindIndex(k => KolomGelijk(k, "Device"));
        var mpnBronnen = new List<int>();
        foreach (var naam in new[] { "Manufacturer Part Number", "MPN" })
        {
            var i = kopVelden.FindIndex(k => KolomGelijk(k, naam));
            if (i >= 0 && !mpnBronnen.Contains(i)) mpnBronnen.Add(i);
        }
        var mpnUitDevice = 0;

        var nieuw = new List<string> { string.Join(",", doel.Select(d => d.Naam)) };
        foreach (var velden in rijen)
        {
            if (fabrikant >= 0 && fabrikant < velden.Count &&
                velden[fabrikant].Equals("MDE", StringComparison.OrdinalIgnoreCase))
            {
                overgeslagen++;
                continue;
            }

            var uit = new string[doel.Length];
            for (var d = 0; d < doel.Length; d++)
            {
                var waarde = bronIndex[d] < velden.Count ? velden[bronIndex[d]] : "";

                // MPN-kolom: het GPN als de add-in er een koos, anders de
                // keten 'Manufacturer Part Number' → 'MPN' → 'Device'.
                if (d == MpnDoel)
                {
                    waarde = gpnBron >= 0 && gpnBron < velden.Count
                        ? velden[gpnBron] : "";
                    if (waarde.Length == 0)
                    {
                        foreach (var bron in mpnBronnen)
                        {
                            if (bron < velden.Count) waarde = velden[bron];
                            if (waarde.Length > 0) break;
                        }
                        if (waarde.Length == 0)
                        {
                            waarde = deviceBron >= 0 && deviceBron < velden.Count
                                ? velden[deviceBron] : "";
                            if (waarde.Length > 0) mpnUitDevice++;
                        }
                    }
                }

                uit[d] = doel[d].Quotes
                    ? "\"" + waarde.Replace("\"", "\"\"") + "\""
                    : CsvVeilig(waarde);
            }
            nieuw.Add(string.Join(",", uit));
        }

        return (nieuw.ToArray(),
                $"{nieuw.Count - 1} regel(s)"
                + (overgeslagen > 0 ? $", {overgeslagen} MDE-regel(s) overgeslagen" : "")
                + (mpnUitDevice > 0 ? $", {mpnUitDevice}× MPN gevuld met de Device-waarde (bron leeg)" : "")
                + $", MPN uit: {string.Join(" → ", mpnBronnen.Select(i => $"'{kopVelden[i]}'"))} → Device");
    }

    /// <summary>Zet quotes om een waarde zodra dat voor de CSV nodig is.</summary>
    private static string CsvVeilig(string waarde)
        => waarde.IndexOfAny(new[] { ',', '"' }) < 0
            ? waarde
            : "\"" + waarde.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// Splitst één CSV-regel op de scheider, maar niet binnen
    /// aanhalingstekens. Geeft de velden letterlijk terug, inclusief hun
    /// eventuele aanhalingstekens.
    /// </summary>
    internal static List<string> SplitsCsv(string regel, char scheider)
    {
        var velden = new List<string>();
        var start = 0;
        var binnenQuotes = false;
        for (var i = 0; i < regel.Length; i++)
        {
            if (regel[i] == '"') binnenQuotes = !binnenQuotes;
            else if (regel[i] == scheider && !binnenQuotes)
            {
                velden.Add(regel[start..i]);
                start = i + 1;
            }
        }
        velden.Add(regel[start..]);
        return velden;
    }

    /// <summary>De kale waarde van een CSV-veld: zonder buitenste
    /// aanhalingstekens, en dubbele quotes daarbinnen weer enkel.</summary>
    internal static string Kaal(string veld)
    {
        veld = veld.Trim();
        if (veld.Length >= 2 && veld[0] == '"' && veld[^1] == '"')
            veld = veld[1..^1].Replace("\"\"", "\"");
        return veld;
    }

    // ================================================================
    //  Zoeken in de Assembly-map
    // ================================================================

    /// <summary>De CSV in de Assembly-map waarvan de naam eindigt op de zijde.</summary>
    internal static string? FindCplFile(string assemblyDir, string side)
        => Directory.GetFiles(assemblyDir, "*.csv")
                    .Where(f => Path.GetFileNameWithoutExtension(f)
                                    .EndsWith(side, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f)
                    .FirstOrDefault();

    /// <summary>
    /// De BOM in de Assembly-map. De HTML-export gaat vóór de CSV: Fusion
    /// zet in de CSV niet alle waardes netjes tussen aanhalingstekens,
    /// waardoor komma's in waardes de kolommen verschuiven. In de HTML is
    /// elke cel een eigen &lt;td&gt; en is die fout te herstellen.
    /// </summary>
    internal static string? FindBomFile(string assemblyDir)
    {
        static string? Zoek(string map, params string[] extensies)
            => Directory.GetFiles(map)
                        .Where(f => extensies.Contains(Path.GetExtension(f),
                                                       StringComparer.OrdinalIgnoreCase))
                        .Where(f => Path.GetFileNameWithoutExtension(f)
                                        .Contains("BOM", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f)
                        .FirstOrDefault();

        return Zoek(assemblyDir, ".html", ".htm") ?? Zoek(assemblyDir, ".csv");
    }

    // ================================================================
    //  De BOM inlezen: HTML of CSV → kopregel + rijen met kale waardes
    // ================================================================

    /// <summary>Leest de BOM als (kopregel, datarijen); null = leeg/onbruikbaar.</summary>
    internal static (List<string> Kop, List<List<string>> Rijen)? LeesBom(string bomFile)
        => bomFile.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
        || bomFile.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? LeesBomHtml(bomFile)
            : LeesBomCsv(bomFile);

    private static (List<string>, List<List<string>>)? LeesBomCsv(string bomFile)
    {
        var lines = File.ReadAllLines(bomFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count < 2) return null;

        var scheider = !lines[0].Contains(',') && lines[0].Contains(';') ? ';' : ',';
        var kop = SplitsCsv(lines[0], scheider).Select(Kaal).ToList();
        var rijen = lines.Skip(1)
                         .Select(l => SplitsCsv(l, scheider).Select(Kaal).ToList())
                         .ToList();
        return (kop, rijen);
    }

    private static (List<string>, List<List<string>>)? LeesBomHtml(string bomFile)
    {
        var html = File.ReadAllText(bomFile);

        var rijen = new List<List<string>>();
        foreach (Match tr in Regex.Matches(html, "<tr[^>]*>(.*?)</tr>",
                                           RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var cellen = new List<string>();
            foreach (Match td in Regex.Matches(tr.Groups[1].Value, "<td[^>]*>(.*?)</td>",
                                               RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var inhoud = Regex.Replace(td.Groups[1].Value, "<[^>]*>", "");
                cellen.Add(System.Net.WebUtility.HtmlDecode(inhoud));
            }
            if (cellen.Count > 0) rijen.Add(cellen);
        }
        if (rijen.Count < 2) return null;

        var kop = rijen[0].Select(c => c.Trim()).ToList();

        var data = new List<List<string>>();
        foreach (var rij in rijen.Skip(1))
        {
            // Fusion splitst een waarde met komma's soms alsnog over meerdere
            // cellen; het vervolg begint dan met een spatie (van ', '). Zolang
            // de rij langer is dan de kopregel plakken we zo'n cel terug aan
            // zijn voorganger — daarmee is de oorspronkelijke waarde hersteld.
            while (rij.Count > kop.Count)
            {
                var i = rij.FindIndex(1, c => c.StartsWith(' '));
                if (i < 1) break;
                rij[i - 1] += "," + rij[i];
                rij.RemoveAt(i);
            }
            data.Add(rij.Select(c => c.Trim()).ToList());
        }
        return (kop, data);
    }
}
