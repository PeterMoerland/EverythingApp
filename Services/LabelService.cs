using System.Text;
using System.Xml.Linq;
using ZXing.Datamatrix;

namespace EverythingApp.Services;

/// <summary>
/// Maakt het typeplaatje van een product: producttype, serienummer, bouwjaar en
/// een DataMatrix met 'PN' + het zescijferige ProductenID — precies zoals het
/// Access-rapport Typeplaatje_Mpede dat deed.
///
/// Het label wordt als SVG opgebouwd en met LibreOffice omgezet naar PDF. Alles
/// blijft daardoor vector, ook de DataMatrix: die bestaat uit echte vierkantjes
/// in plaats van een plaatje. Dat is precies wat een lasersnijder wil zien.
/// </summary>
public class LabelService
{
    private readonly IConfiguration _config;
    private readonly NdaDocumentService _pdf;
    private readonly ILogger<LabelService> _log;

    public LabelService(IConfiguration config, NdaDocumentService pdf, ILogger<LabelService> log)
    {
        _config = config;
        _pdf = pdf;
        _log = log;
    }

    /// <summary>Afmeting van het label in millimeters (instelbaar in appsettings).</summary>
    private double BreedteMm => Getal("Laser:BreedteMm", 60);
    private double HoogteMm => Getal("Laser:HoogteMm", 30);

    private double Getal(string sleutel, double standaard)
        => double.TryParse(_config[sleutel], System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : standaard;

    /// <summary>Map waar de laser het bestand ophaalt.</summary>
    public string LaserPad => _config["Laser:Pad"] ?? @"Z:\Production\Laser\Label.pdf";

    /// <summary>De code in de DataMatrix: PN plus het ProductenID, zes cijfers.</summary>
    public static string CodeVoor(int productId) => "PN" + productId.ToString("D6");

    // ============================================================
    //  Het label als SVG
    // ============================================================

    /// <summary>
    /// Bouwt het typeplaatje. Links onder elkaar het type, het serienummer en het
    /// bouwjaar; rechts de DataMatrix, vierkant en even hoog als het tekstblok.
    /// </summary>
    public string MaakSvg(string type, string serienummer, string bouwjaar, string code)
    {
        var b = BreedteMm;
        var h = HoogteMm;

        // marges en indeling, alles in mm
        var marge = h * 0.10;
        var matrixZijde = h - 2 * marge;
        var matrixLinks = b - marge - matrixZijde;

        var regelhoogte = (h - 2 * marge) / 3.0;
        var tekstgrootte = regelhoogte * 0.78;

        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" ")
           .Append($"width=\"{Mm(b)}mm\" height=\"{Mm(h)}mm\" viewBox=\"0 0 {Mm(b)} {Mm(h)}\">");

        // witte achtergrond, zodat de PDF niet doorzichtig is
        svg.Append($"<rect x=\"0\" y=\"0\" width=\"{Mm(b)}\" height=\"{Mm(h)}\" fill=\"#ffffff\"/>");

        // ---- tekstregels ----
        var regels = new[] { type, serienummer, bouwjaar };
        for (var i = 0; i < regels.Length; i++)
        {
            var tekst = Veilig(regels[i]);
            if (tekst.Length == 0) continue;

            // basislijn van de regel: bovenmarge + regel + een beetje voor de staart
            var y = marge + regelhoogte * (i + 1) - regelhoogte * 0.22;

            // Een lange productnaam mag niet over de DataMatrix heen lopen, dus
            // krimpt de letter mee. 0,62 is de gemiddelde tekenbreedte van vet Arial.
            var beschikbaar = matrixLinks - 2 * marge;
            var grootte = Math.Min(tekstgrootte, beschikbaar / Math.Max(1, tekst.Length * 0.62));

            svg.Append(
                $"<text x=\"{Mm(marge)}\" y=\"{Mm(y)}\" " +
                $"font-family=\"Arial, Helvetica, sans-serif\" font-weight=\"bold\" " +
                $"font-size=\"{Mm(grootte)}\" fill=\"#000000\">{tekst}</text>");
        }

        // ---- DataMatrix ----
        var matrix = Modules(code);
        if (matrix is not null)
        {
            var n = matrix.GetLength(0);
            var module = matrixZijde / n;

            svg.Append($"<g fill=\"#000000\" stroke=\"none\">");
            for (var rij = 0; rij < n; rij++)
                for (var kolom = 0; kolom < n; kolom++)
                {
                    if (!matrix[rij, kolom]) continue;
                    var x = matrixLinks + kolom * module;
                    var y = marge + rij * module;
                    // 0,02 mm overlap voorkomt haarlijntjes tussen de vierkantjes
                    svg.Append($"<rect x=\"{Mm(x)}\" y=\"{Mm(y)}\" " +
                               $"width=\"{Mm(module + 0.02)}\" height=\"{Mm(module + 0.02)}\"/>");
                }
            svg.Append("</g>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    /// <summary>
    /// De DataMatrix als vierkant raster van aan/uit. Vierkant houden we zelf aan:
    /// de encoder mag een rechthoekig symbool kiezen, maar het label heeft een
    /// vierkant vakje, en de scanner leest beide.
    /// </summary>
    private bool[,]? Modules(string code)
    {
        try
        {
            var writer = new DataMatrixWriter();
            var bits = writer.encode(code, ZXing.BarcodeFormat.DATA_MATRIX, 0, 0);

            var n = Math.Max(bits.Width, bits.Height);
            var uit = new bool[n, n];
            for (var rij = 0; rij < bits.Height; rij++)
                for (var kolom = 0; kolom < bits.Width; kolom++)
                    uit[rij, kolom] = bits[kolom, rij];
            return uit;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "DataMatrix voor '{Code}' maken mislukte", code);
            return null;
        }
    }

    /// <summary>
    /// Meerdere labels onder elkaar op één vel, met een dunne snijlijn ertussen.
    /// Handig als je vijf robots achter elkaar wilt graveren.
    /// </summary>
    public string MaakVelSvg(IEnumerable<(string Type, string Serienummer, string Bouwjaar, string Code)> labels)
    {
        var lijst = labels.ToList();
        if (lijst.Count == 0) return MaakSvg("", "", "", "");
        if (lijst.Count == 1)
            return MaakSvg(lijst[0].Type, lijst[0].Serienummer, lijst[0].Bouwjaar, lijst[0].Code);

        var b = BreedteMm;
        var h = HoogteMm;
        var totaal = h * lijst.Count;

        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" ")
           .Append($"width=\"{Mm(b)}mm\" height=\"{Mm(totaal)}mm\" viewBox=\"0 0 {Mm(b)} {Mm(totaal)}\">");
        svg.Append($"<rect x=\"0\" y=\"0\" width=\"{Mm(b)}\" height=\"{Mm(totaal)}\" fill=\"#ffffff\"/>");

        for (var i = 0; i < lijst.Count; i++)
        {
            var (type, serienummer, bouwjaar, code) = lijst[i];

            // het losse label hergebruiken en op zijn plek zetten
            var enkel = MaakSvg(type, serienummer, bouwjaar, code);
            var binnenste = Binnenwerk(enkel);

            svg.Append($"<g transform=\"translate(0,{Mm(h * i)})\">").Append(binnenste).Append("</g>");

            if (i > 0)
                svg.Append($"<line x1=\"0\" y1=\"{Mm(h * i)}\" x2=\"{Mm(b)}\" y2=\"{Mm(h * i)}\" " +
                           "stroke=\"#cccccc\" stroke-width=\"0.1\"/>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    /// <summary>Haalt de inhoud tussen de svg-tags eruit, zodat die verplaatst kan worden.</summary>
    private static string Binnenwerk(string svg)
    {
        var begin = svg.IndexOf('>');
        var eind = svg.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
        if (begin < 0 || eind <= begin) return "";
        return svg[(begin + 1)..eind];
    }

    // ============================================================
    //  Dymo-labels
    //
    //  De app maakt de labeltekening (DieCutLabel-XML) en DYMO Connect op de pc
    //  print hem. Maten in twips: 1440 per inch, dus 1 mm = 56,7.
    // ============================================================
    private static int Twips(double mm) => (int)Math.Round(mm * 1440 / 25.4);

    /// <summary>
    /// Het complete label voor een artikel, klein of groot. Bij het grote label
    /// wordt de foto uit de artikelenmap meegenomen als die er is.
    /// </summary>
    public async Task<string> ArtikelLabelAsync(int artikelId, string artikelcode,
                                                string omschrijving, bool groot)
    {
        if (!groot) return KleinLabelXml(artikelId, artikelcode);

        return GrootLabelXml(artikelId, artikelcode, omschrijving, await FotoVoorLabelAsync(artikelId));
    }

    // DYMO neemt in het label alleen PNG en JPEG aan; bmp of gif zou hij weigeren.
    private static readonly string[] Fotosoorten = { ".jpg", ".jpeg", ".png" };

    // Wanneer de fotomap er voor het laatst uit lag. Als 'long' omdat de service
    // door meerdere schermen tegelijk wordt gebruikt.
    private long _fotomapStuk;

    /// <summary>
    /// De foto van een artikel uit de aparte fotomap, of null. Faalt dit, dan komt
    /// het label er gewoon zonder foto uit — maar wel met een regel in het logboek.
    /// Met <paramref name="verslag"/> erbij schrijft hij elke stap uit; dat is de
    /// tekst achter /dymolabel?uitleg=true.
    /// </summary>
    private async Task<byte[]?> FotoVoorLabelAsync(int artikelId, StringBuilder? verslag = null)
    {
        void Zeg(string tekst) => verslag?.AppendLine(tekst);

        var map = _config["Artikelen:FotoPad"];
        if (string.IsNullOrWhiteSpace(map))
        {
            Zeg("Artikelen:FotoPad staat niet in appsettings.json — geen foto.");
            return null;
        }
        Zeg($"Fotomap: {map}");

        // Bleek de map onbereikbaar, dan heel even niet opnieuw proberen: printen zonder
        // foto is beter dan een knop die blijft hangen. Kort (30 s), want de NAS heeft
        // af en toe een hik van een paar seconden — geen reden om vijf minuten lang
        // labels zonder foto te maken. De uitleg-pagina (verslag) probeert het altijd.
        var stuk = new DateTime(Interlocked.Read(ref _fotomapStuk), DateTimeKind.Utc);
        if (verslag is null && DateTime.UtcNow - stuk < TimeSpan.FromSeconds(30))
        {
            _log.LogInformation("Foto van artikel {Id} overgeslagen: fotomap lag er om {Tijd:HH:mm:ss} uit.", artikelId, stuk.ToLocalTime());
            return null;
        }
        if (verslag is not null && DateTime.UtcNow - stuk < TimeSpan.FromSeconds(30))
            Zeg($"De map lag er om {stuk.ToLocalTime():HH:mm:ss} uit (labels gaan 30 s zonder foto); deze uitleg probeert het toch.");

        try
        {
            var submap = Path.Combine(map, (artikelId / 1000).ToString("D8"));

            // Netwerkpad: bij een hik van de SMB-verbinding faalt de eerste aanroep
            // zonder duidelijke fout. Eén korte herkansing voordat we opgeven.
            if (map.StartsWith(@"\\") && !Directory.Exists(submap))
            {
                await Task.Delay(400);
                if (Directory.Exists(submap)) Zeg("Fotomap pas bij herkansing bereikbaar (netwerk-hik).");
            }

            // Bewust geen Directory.Exists: die geeft bij élke fout "bestaat niet"
            // terug en verzwijgt waarom. Meteen opsommen geeft bij een probleem een
            // echte foutmelding, en dat is precies wat we willen weten.
            string? bestand = null;
            try
            {
                bestand = Directory.EnumerateFiles(submap, artikelId.ToString("D8") + ".*")
                                   .FirstOrDefault(f => Fotosoorten.Contains(
                                       Path.GetExtension(f).ToLowerInvariant()));
            }
            catch (DirectoryNotFoundException)
            {
                Zeg($"Submap {submap}: bestaat niet.");
                return null;
            }

            if (bestand is null)
            {
                Zeg($"Submap {submap}: bestaat, maar er ligt geen {artikelId:D8}.jpg/.jpeg/.png.");
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(bestand);
            Zeg($"Gevonden: {bestand} ({bytes.Length / 1024} kB)");

            // De foto gaat als base64 mee in de labeltekening. Het plaatje op het
            // label is maar zo'n 2,5 cm breed, dus een miniatuur volstaat — en een
            // foto van enkele megabytes zou de tekening onwerkbaar groot maken.
            // Verkleinen kan alleen op Windows (System.Drawing); daar draait de app.
            var klein = OperatingSystem.IsWindows() ? VerkleinNaarPng(bytes, 450) : null;
            if (klein is not null)
            {
                Zeg($"Verkleind naar PNG: {klein.Length / 1024} kB — die gaat mee op het label.");
                return klein;
            }

            Zeg("Verkleinen is MISLUKT (zie het logboek voor de reden).");
            if (bytes.Length > 400_000)
            {
                Zeg($"Het origineel is {bytes.Length / 1024} kB — te groot om onverkleind mee te "
                  + "sturen, dus het label gaat zonder foto.");
                _log.LogInformation("Foto van artikel {Id} is {Kb} kB, kon niet worden verkleind " +
                                    "en gaat daarom niet mee op het label.", artikelId, bytes.Length / 1024);
                return null;
            }
            Zeg("Het origineel is klein genoeg en gaat onverkleind mee.");
            return bytes;
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _fotomapStuk, DateTime.UtcNow.Ticks);
            Zeg($"FOUT bij het lezen: {ex.GetType().Name}: {ex.Message}");
            _log.LogWarning(ex, "Foto van artikel {Id} kon niet gelezen worden ({Map}).", artikelId, map);
            return null;
        }
    }

    /// <summary>Stap-voor-stapverslag van de foto-zoektocht, voor /dymolabel?uitleg=true.</summary>
    public async Task<string> FotoUitlegAsync(int artikelId)
    {
        var verslag = new StringBuilder();
        verslag.AppendLine($"Foto voor het grote label van artikel {artikelId}");
        verslag.AppendLine($"De app draait als {Environment.UserDomainName}\\{Environment.UserName}");
        verslag.AppendLine(new string('-', 50));

        var foto = await FotoVoorLabelAsync(artikelId, verslag);

        verslag.AppendLine();
        verslag.AppendLine(foto is null
            ? "Uitkomst: GEEN foto op het label."
            : $"Uitkomst: foto gaat mee ({foto.Length / 1024} kB).");
        return verslag.ToString();
    }

    /// <summary>
    /// Verkleint een foto tot een PNG van hoogstens <paramref name="maxPx"/> beeldpunten
    /// aan de lange kant, op een witte ondergrond (voor foto's met doorzichtige delen).
    /// PNG omdat DYMO dat zeker begrijpt; bij JPEG is dat minder gewis.
    /// Geeft null als het bestand geen leesbare afbeelding is.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private byte[]? VerkleinNaarPng(byte[] bron, int maxPx)
    {
        try
        {
            using var invoer = new MemoryStream(bron);
            using var origineel = System.Drawing.Image.FromStream(invoer);

            var schaal = Math.Min(1.0, (double)maxPx / Math.Max(origineel.Width, origineel.Height));
            var breed = Math.Max(1, (int)Math.Round(origineel.Width * schaal));
            var hoog = Math.Max(1, (int)Math.Round(origineel.Height * schaal));

            using var doek = new System.Drawing.Bitmap(breed, hoog);
            using (var g = System.Drawing.Graphics.FromImage(doek))
            {
                g.Clear(System.Drawing.Color.White);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(origineel, 0, 0, breed, hoog);
            }

            using var uitvoer = new MemoryStream();
            doek.Save(uitvoer, System.Drawing.Imaging.ImageFormat.Png);
            return uitvoer.ToArray();
        }
        catch (Exception ex)
        {
            _log.LogInformation(ex, "Foto verkleinen mislukt; het origineel gaat mee als het klein genoeg is.");
            return null;
        }
    }

    /// <summary>De tekst die in de barcode komt: MDE plus het artikelnummer.</summary>
    public static string BarcodeVoor(int artikelId) => "MDE" + artikelId;

    /// <summary>
    /// Eén ding op een label: tekst, een streepjescode of een plaatje. De plaats
    /// staat in millimeters vanaf de linkerbovenhoek; het uitschrijven naar XML
    /// gebeurt later, want elke versie van DYMO wil een ander formaat.
    /// </summary>
    private sealed class Vlak
    {
        public string Naam = "";
        public string Soort = "tekst";          // tekst | barcode | foto
        public string Tekst = "";
        public byte[]? Foto;
        public double X, Y, Breed, Hoog;        // mm
        public int Punten = 9;
        public bool Vet;
        public string Uitlijning = "Left";      // Left | Center | Right
        public bool BarcodeTekst = true;
    }

    /// <summary>
    /// Klein label (Dymo 99017, 12 × 50 mm): barcode met daaronder de artikelcode.
    /// </summary>
    public string KleinLabelXml(int artikelId, string artikelcode)
    {
        var uitSjabloon = Sjabloon("Klein", artikelId, artikelcode, "", null);
        if (uitSjabloon is not null) return uitSjabloon;

        const double breedMm = 50, hoogMm = 12;
        const double rand = 1.2;
        var breed = breedMm - 2 * rand;

        var vlakken = new List<Vlak>
        {
            new() { Naam = "BARCODE", Soort = "barcode", Tekst = BarcodeVoor(artikelId),
                    X = rand, Y = 0.8, Breed = breed, Hoog = 7.0, BarcodeTekst = false },
            new() { Naam = "ARTIKELCODE", Tekst = artikelcode,
                    X = rand, Y = 7.9, Breed = breed, Hoog = 3.3,
                    Punten = 9, Vet = true, Uitlijning = "Center" },
        };

        return Teken(Papier("Klein", "SuspensionFileS0722460"), breedMm, hoogMm, vlakken);
    }

    /// <summary>
    /// Groot label (Dymo 11356, 41 × 89 mm): artikelcode met de barcode ernaast,
    /// daaronder de omschrijving en rechts eventueel de foto.
    /// </summary>
    public string GrootLabelXml(int artikelId, string artikelcode, string omschrijving,
                                byte[]? foto = null)
    {
        var uitSjabloon = Sjabloon("Groot", artikelId, artikelcode, omschrijving, foto);
        if (uitSjabloon is not null) return uitSjabloon;

        const double breedMm = 89, hoogMm = 41;
        const double rand = 2;
        var breed = breedMm - 2 * rand;

        // met foto blijft er rechts 24 mm vrij voor het plaatje
        var fotoBreed = foto is null ? 0 : 24;
        var tekstBreed = breed - fotoBreed - (foto is null ? 0 : 2);

        var vlakken = new List<Vlak>
        {
            new() { Naam = "ARTIKELCODE", Tekst = artikelcode,
                    X = rand, Y = 1.5, Breed = tekstBreed / 2, Hoog = 7,
                    Punten = 14, Vet = true },
            new() { Naam = "BARCODE", Soort = "barcode", Tekst = BarcodeVoor(artikelId),
                    X = rand + tekstBreed / 2, Y = 1.5, Breed = tekstBreed / 2, Hoog = 9 },
            new() { Naam = "OMSCHRIJVING", Tekst = omschrijving,
                    X = rand, Y = 12, Breed = tekstBreed, Hoog = 26, Punten = 10 },
        };

        if (foto is not null)
            vlakken.Add(new Vlak { Naam = "FOTO", Soort = "foto", Foto = foto,
                                   X = rand + tekstBreed + 2, Y = 2,
                                   Breed = fotoBreed, Hoog = hoogMm - 4 });

        return Teken(Papier("Groot", "WhiteNameBadgeS0722560"), breedMm, hoogMm, vlakken);
    }

    /// <summary>
    /// Lokatie- of dragerlabel (klein label, 12 × 50 mm): barcode @L plus acht cijfers, daaronder de naam
    /// (Access: PrintLokatieLabel met de sjablonen Lokatie en Lokatiedrager). De scanner leest dezelfde code.
    /// </summary>
    public string LokatieLabelXml(string barcode, string naam)
    {
        const double breedMm = 50, hoogMm = 12;
        const double rand = 1.2;
        var breed = breedMm - 2 * rand;

        var vlakken = new List<Vlak>
        {
            new() { Naam = "BARCODE", Soort = "barcode", Tekst = barcode,
                    X = rand, Y = 0.8, Breed = breed, Hoog = 7.0, BarcodeTekst = false },
            new() { Naam = "NAAM", Tekst = naam,
                    X = rand, Y = 7.9, Breed = breed, Hoog = 3.3,
                    Punten = 9, Vet = true, Uitlijning = "Center" },
        };

        return Teken(Papier("Klein", "SuspensionFileS0722460"), breedMm, hoogMm, vlakken);
    }

    private string Papier(string welk, string standaard) => _config["Labels:" + welk + "Papier"] ?? standaard;

    // ============================================================
    //  Labels uit een sjabloon
    //
    //  De opmaak komt uit een .dymo-bestand dat in DYMO Connect zelf is gemaakt.
    //  Dat is de zekerste manier: het formaat klopt dan per definitie, en wie de
    //  indeling wil wijzigen doet dat in DYMO Connect en vervangt het bestand.
    //
    //  Wat er wordt ingevuld, op volgorde van voorkomen in het sjabloon:
    //    de barcode      → MDE + het artikelnummer
    //    het 1e tekstvak → de artikelcode
    //    het 2e tekstvak → de omschrijving, over meerdere regels verdeeld
    //    de afbeelding   → de foto; is die er niet, dan valt het vak weg
    // ============================================================
    private string? Sjabloon(string welk, int artikelId, string artikelcode,
                             string omschrijving, byte[]? foto)
    {
        // Een sjabloon is altijd DYMO Connect-formaat; draait er nog het oude
        // DYMO Label v8, dan moet de app het label zelf tekenen.
        if (OudFormaat) return null;

        var pad = _config["Labels:" + welk + "Sjabloon"];
        if (string.IsNullOrWhiteSpace(pad))
            pad = Path.Combine(AppContext.BaseDirectory, "Templates", "Dymo" + welk + ".dymo");

        if (!File.Exists(pad)) return null;

        try
        {
            var doc = XDocument.Load(pad);
            var objecten = doc.Descendants("LabelObjects").FirstOrDefault();
            if (objecten is null)
            {
                _log.LogWarning("Labelsjabloon {Pad} bevat geen LabelObjects.", pad);
                return null;
            }

            var barcode = objecten.Elements("BarcodeObject").FirstOrDefault();
            var streepjes = barcode?.Element("Data")?.Element("DataString");
            if (streepjes is null)
                _log.LogWarning("Labelsjabloon {Pad} heeft geen barcode; het label komt er zonder.", pad);
            else
                streepjes.SetValue(BarcodeVoor(artikelId));

            // Op volgorde in het sjabloon: eerst de artikelcode, dan de omschrijving.
            // Heeft iemand maar één tekstvak getekend, dan zetten we allebei daarin.
            var teksten = objecten.Elements("TextObject").ToList();
            var heeftOms = !string.IsNullOrWhiteSpace(omschrijving);

            if (teksten.Count == 0)
                _log.LogWarning("Labelsjabloon {Pad} heeft geen tekstvak.", pad);
            else if (teksten.Count == 1)
                ZetTekst(teksten[0], heeftOms
                    ? new[] { artikelcode }.Concat(Regels(teksten[0], omschrijving)).ToList()
                    : new List<string> { artikelcode });
            else
            {
                var regels = Regels(teksten[1], omschrijving);
                ZetTekst(teksten[0], new List<string> { artikelcode });
                ZetTekst(teksten[1], regels);
            }

            var plaatje = objecten.Elements("ImageObject").FirstOrDefault();
            if (plaatje is not null)
            {
                if (foto is null) plaatje.Remove();
                else plaatje.Element("Data")?.SetValue(Convert.ToBase64String(foto));
            }

            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                 + doc.Root!.ToString(SaveOptions.DisableFormatting);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Labelsjabloon {Pad} kon niet worden gebruikt.", pad);
            return null;
        }
    }

    /// <summary>
    /// Zet de regels in een tekstvak. Het lettertype van de eerste regel in het
    /// sjabloon geldt voor alle regels — zo blijft de opmaak van DYMO Connect staan.
    /// </summary>
    private static void ZetTekst(XElement tekstvak, IEnumerable<string> regels)
    {
        var opmaak = tekstvak.Element("FormattedText");
        var voorbeeld = opmaak?.Elements("LineTextSpan").FirstOrDefault()?.Element("TextSpan");
        if (opmaak is null || voorbeeld is null) return;

        opmaak.Elements("LineTextSpan").Remove();

        foreach (var regel in regels)
        {
            var span = new XElement(voorbeeld);
            var tekst = span.Element("Text");
            if (tekst is null) span.AddFirst(new XElement("Text", regel));
            else tekst.SetValue(regel);

            opmaak.Add(new XElement("LineTextSpan", span));
        }
    }

    /// <summary>
    /// Verdeelt een omschrijving over regels die in het tekstvak passen. DYMO breekt
    /// een regel niet zelf af, dus dat doen we hier — op hele woorden.
    /// </summary>
    private static List<string> Regels(XElement tekstvak, string tekst)
    {
        tekst = (tekst ?? "").Trim();
        if (tekst.Length == 0) return new List<string> { "" };

        var breedDuim = Waarde(tekstvak, "ObjectLayout", "Size", "Width") ?? 1.5;
        var punten = tekstvak.Descendants("FontSize").Select(e => Getal(e.Value)).FirstOrDefault() ?? 11;

        // Een letter is bij Segoe UI gemiddeld ongeveer een halve punt breed per
        // puntgrootte. Komt het er net niet in, dan krimpt DYMO de tekst zelf nog
        // (FitMode staat op AlwaysFit), dus een schatting volstaat.
        var perRegel = Math.Max(8, (int)(breedDuim * 72 / (punten * 0.50)));

        var regels = new List<string>();
        var regel = new StringBuilder();

        foreach (var woord in tekst.Split(new[] { ' ', '\t', '\r', '\n' },
                                          StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = woord;

            // Een woord dat zelf al te lang is (een lang artikelnummer bijvoorbeeld)
            // knippen we hard door; anders krimpt DYMO de hele omschrijving.
            while (rest.Length > perRegel)
            {
                if (regel.Length > 0) { regels.Add(regel.ToString()); regel.Clear(); }
                regels.Add(rest[..perRegel]);
                rest = rest[perRegel..];
            }

            if (regel.Length > 0 && regel.Length + 1 + rest.Length > perRegel)
            {
                regels.Add(regel.ToString());
                regel.Clear();
            }
            if (regel.Length > 0) regel.Append(' ');
            regel.Append(rest);
        }
        if (regel.Length > 0) regels.Add(regel.ToString());

        return regels;
    }

    private static double? Waarde(XElement vanaf, params string[] pad)
    {
        XElement? e = vanaf;
        foreach (var stap in pad)
        {
            e = e?.Element(stap);
            if (e is null) return null;
        }
        return Getal(e.Value);
    }

    private static double? Getal(string tekst)
        => double.TryParse(tekst, System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>
    /// Schrijft het label uit in het formaat dat op de pc draait. DYMO Connect
    /// werkt met 'DesktopLabel', het oudere DYMO Label v8 met 'DieCutLabel'. Welke
    /// het is staat in appsettings onder Labels:Formaat.
    /// </summary>
    private bool OudFormaat
        => (_config["Labels:Formaat"] ?? "connect").Trim().ToLowerInvariant()
           is "dls8" or "v8" or "diecut";

    private string Teken(string papier, double breedMm, double hoogMm, List<Vlak> vlakken)
        => OudFormaat
            ? DieCutLabel(papier, breedMm, hoogMm, vlakken)
            : DesktopLabel(papier, breedMm, hoogMm, vlakken);

    // ------------------------------------------------------------
    //  Formaat 1: DYMO Connect ('DesktopLabel'). Maten in inches.
    // ------------------------------------------------------------
    private static string Duim(double mm)
        => (mm / 25.4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Hoeveel van het label de printer niet kan raken. DYMO zet daarom een
    /// 'DYMORect' die kleiner is dan het papier; wat daarbuiten valt komt er niet
    /// op. De vlakken worden hieronder in dat kleinere gebied gepast.
    /// </summary>
    private const double KantMm = 5.8, KantMmBoven = 1.0;

    private static string DesktopLabel(string papier, double breedMm, double hoogMm, List<Vlak> vlakken)
    {
        // 'Id|PaperName': DYMO Connect wil hier de korte naam van het label.
        var naam = papier.Split('|', 2)[0].Trim();

        var vlakBreed = Math.Max(1, breedMm - 2 * KantMm);
        var vlakHoog = Math.Max(1, hoogMm - 2 * KantMmBoven);
        var schaalX = vlakBreed / breedMm;
        var schaalY = vlakHoog / hoogMm;

        var inhoud = new System.Text.StringBuilder();
        foreach (var v in vlakken)
            inhoud.Append(DesktopVlak(v, KantMm, KantMmBoven, schaalX, schaalY));

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
               "<DesktopLabel Version=\"1\">" +
               "<DYMOLabel Version=\"4\">" +
               "<Description>MDE</Description>" +
               "<Orientation>Landscape</Orientation>" +
               $"<LabelName>{Veilig(naam)}</LabelName>" +
               "<InitialLength>0</InitialLength>" +
               "<BorderStyle>SolidLine</BorderStyle>" +
               Rechthoek(KantMm, KantMmBoven, vlakBreed, vlakHoog) +
               "<BorderColor><SolidColorBrush><Color A=\"1\" R=\"0\" G=\"0\" B=\"0\"/></SolidColorBrush></BorderColor>" +
               "<BorderThickness>1</BorderThickness>" +
               "<Show_Border>False</Show_Border>" +
               "<HasFixedLength>False</HasFixedLength>" +
               "<FixedLengthValue>0</FixedLengthValue>" +
               "<DynamicLayoutManager>" +
               "<RotationBehavior>ClearObjects</RotationBehavior>" +
               "<LabelObjects>" + inhoud + "</LabelObjects>" +
               "</DynamicLayoutManager>" +
               "</DYMOLabel>" +
               "<LabelApplication>Blank</LabelApplication>" +
               "<DataTable><Columns/><Rows/></DataTable>" +
               "</DesktopLabel>";
    }

    private static string Rechthoek(double xMm, double yMm, double breedMm, double hoogMm) =>
        "<DYMORect>" +
        $"<DYMOPoint><X>{Duim(xMm)}</X><Y>{Duim(yMm)}</Y></DYMOPoint>" +
        $"<Size><Width>{Duim(breedMm)}</Width><Height>{Duim(hoogMm)}</Height></Size>" +
        "</DYMORect>";

    private static string Plaats(Vlak v, double kantX, double kantY, double schaalX, double schaalY) =>
        "<ObjectLayout>" +
        $"<DYMOPoint><X>{Duim(kantX + v.X * schaalX)}</X>" +
        $"<Y>{Duim(kantY + v.Y * schaalY)}</Y></DYMOPoint>" +
        $"<Size><Width>{Duim(v.Breed * schaalX)}</Width>" +
        $"<Height>{Duim(v.Hoog * schaalY)}</Height></Size>" +
        "</ObjectLayout>";

    /// <param name="wit">
    /// Een streepjescode heeft een witte ondergrond en zwarte vulling nodig;
    /// tekst en plaatjes staan juist doorzichtig op het label.
    /// </param>
    private static string Penselen(bool wit) =>
        "<Brushes>" +
        (wit
            ? "<BackgroundBrush><SolidColorBrush><Color A=\"1\" R=\"1\" G=\"1\" B=\"1\"/></SolidColorBrush></BackgroundBrush>"
            : "<BackgroundBrush><SolidColorBrush><Color A=\"0\" R=\"0\" G=\"0\" B=\"0\"/></SolidColorBrush></BackgroundBrush>") +
        "<BorderBrush><SolidColorBrush><Color A=\"1\" R=\"0\" G=\"0\" B=\"0\"/></SolidColorBrush></BorderBrush>" +
        "<StrokeBrush><SolidColorBrush><Color A=\"1\" R=\"0\" G=\"0\" B=\"0\"/></SolidColorBrush></StrokeBrush>" +
        (wit
            ? "<FillBrush><SolidColorBrush><Color A=\"1\" R=\"0\" G=\"0\" B=\"0\"/></SolidColorBrush></FillBrush>"
            : "<FillBrush><SolidColorBrush><Color A=\"0\" R=\"0\" G=\"0\" B=\"0\"/></SolidColorBrush></FillBrush>") +
        "</Brushes>";

    private static string Lettertype(string naam, double punten, bool vet) =>
        "<FontInfo>" +
        $"<FontName>{naam}</FontName>" +
        $"<FontSize>{punten.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}</FontSize>" +
        $"<IsBold>{(vet ? "True" : "False")}</IsBold>" +
        "<IsItalic>False</IsItalic>" +
        "<IsUnderline>False</IsUnderline>" +
        "<FontBrush><SolidColorBrush><Color A=\"1\" R=\"0\" G=\"0\" B=\"0\"/></SolidColorBrush></FontBrush>" +
        "</FontInfo>";

    /// <summary>De eigenschappen die elk element op een DYMO Connect-label heeft.</summary>
    private static string Gemeen(string naam, bool wit = false) =>
        $"<Name>{Veilig(naam)}</Name>" + Penselen(wit) +
        "<Rotation>Rotation0</Rotation>" +
        "<OutlineThickness>1</OutlineThickness>" +
        "<IsOutlined>False</IsOutlined>" +
        "<BorderStyle>SolidLine</BorderStyle>" +
        "<Margin><DYMOThickness Left=\"0\" Top=\"0\" Right=\"0\" Bottom=\"0\"/></Margin>";

    private static string DesktopVlak(Vlak v, double kantX, double kantY,
                                      double schaalX, double schaalY) => v.Soort switch
    {
        "barcode" =>
            "<BarcodeObject>" + Gemeen(v.Naam, wit: true) +
            "<BarcodeFormat>Code128Ean</BarcodeFormat>" +
            $"<Data><DataString>{Veilig(v.Tekst)}</DataString></Data>" +
            "<HorizontalAlignment>Center</HorizontalAlignment>" +
            "<VerticalAlignment>Middle</VerticalAlignment>" +
            "<Size>AutoFit</Size>" +
            $"<TextPosition>{(v.BarcodeTekst ? "Bottom" : "None")}</TextPosition>" +
            Lettertype("Arial", 12, false) +
            Plaats(v, kantX, kantY, schaalX, schaalY) + "</BarcodeObject>",

        "foto" =>
            "<ImageObject>" + Gemeen(v.Naam) +
            $"<Data>{Convert.ToBase64String(v.Foto ?? Array.Empty<byte>())}</Data>" +
            "<ScaleMode>Uniform</ScaleMode>" +
            "<HorizontalAlignment>Center</HorizontalAlignment>" +
            "<VerticalAlignment>Middle</VerticalAlignment>" +
            Plaats(v, kantX, kantY, schaalX, schaalY) + "</ImageObject>",

        _ =>
            "<TextObject>" + Gemeen(v.Naam) +
            $"<HorizontalAlignment>{v.Uitlijning}</HorizontalAlignment>" +
            "<VerticalAlignment>Middle</VerticalAlignment>" +
            "<FitMode>AlwaysFit</FitMode>" +
            "<IsVertical>False</IsVertical>" +
            "<FormattedText>" +
            "<FitMode>AlwaysFit</FitMode>" +
            $"<HorizontalAlignment>{v.Uitlijning}</HorizontalAlignment>" +
            "<VerticalAlignment>Middle</VerticalAlignment>" +
            "<IsVertical>False</IsVertical>" +
            "<LineTextSpan><TextSpan>" +
            $"<Text>{Veilig(v.Tekst)}</Text>" +
            Lettertype("Segoe UI", v.Punten, v.Vet) +
            "</TextSpan></LineTextSpan>" +
            "</FormattedText>" +
            Plaats(v, kantX, kantY, schaalX, schaalY) + "</TextObject>",
    };

    // ------------------------------------------------------------
    //  Formaat 2: DYMO Label v8 ('DieCutLabel'). Maten in twips.
    // ------------------------------------------------------------

    /// <summary>
    /// De oude labeltekening. <paramref name="papier"/> mag "Id|PaperName" zijn;
    /// staat er maar één naam, dan wordt die voor allebei gebruikt.
    /// </summary>
    private static string DieCutLabel(string papier, double breedMm, double hoogMm, List<Vlak> vlakken)
    {
        var delen = papier.Split('|', 2);
        var id = delen[0].Trim();
        var naam = (delen.Length > 1 ? delen[1] : delen[0]).Trim();

        var inhoud = new System.Text.StringBuilder();
        foreach (var v in vlakken)
        {
            int x = Twips(v.X), y = Twips(v.Y), b = Twips(v.Breed), h = Twips(v.Hoog);
            inhoud.Append(v.Soort switch
            {
                "barcode" => Barcode(v.Naam, v.Tekst, x, y, b, h, v.BarcodeTekst),
                "foto" => Afbeelding(v.Naam, v.Foto ?? Array.Empty<byte>(), x, y, b, h),
                _ => Tekst(v.Naam, v.Tekst, x, y, b, h, v.Punten, v.Vet, v.Uitlijning),
            });
        }

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
               "<DieCutLabel Version=\"8.0\" Units=\"twips\">" +
               "<PaperOrientation>Landscape</PaperOrientation>" +
               $"<Id>{Veilig(id)}</Id>" +
               $"<PaperName>{Veilig(naam)}</PaperName>" +
               "<DrawCommands/>" +
               inhoud +
               "</DieCutLabel>";
    }

    private static string Kleuren =>
        "<ForeColor Alpha=\"255\" Red=\"0\" Green=\"0\" Blue=\"0\"/>" +
        "<BackColor Alpha=\"0\" Red=\"255\" Green=\"255\" Blue=\"255\"/>" +
        "<LinkedObjectName></LinkedObjectName><Rotation>Rotation0</Rotation>" +
        // IsVariable hoort bij het printen van een hele reeks labels uit één tekening.
        // Wij vullen de tekst zelf al in, dus False — anders laat Dymo het veld leeg.
        "<IsMirrored>False</IsMirrored><IsVariable>False</IsVariable>";

    private static string Tekst(string naam, string waarde, int x, int y, int breedte, int hoogte,
                                int punten, bool vet, string uitlijning) =>
        "<ObjectInfo><TextObject>" +
        $"<Name>{naam}</Name>{Kleuren}" +
        $"<HorizontalAlignment>{uitlijning}</HorizontalAlignment>" +
        "<VerticalAlignment>Middle</VerticalAlignment>" +
        "<TextFitMode>ShrinkToFit</TextFitMode><UseFullFontHeight>True</UseFullFontHeight>" +
        "<Verticalized>False</Verticalized><StyledText><Element>" +
        $"<String>{Veilig(waarde)}</String><Attributes>" +
        $"<Font Family=\"Arial\" Size=\"{punten}\" Bold=\"{(vet ? "True" : "False")}\" " +
        "Italic=\"False\" Underline=\"False\" Strikeout=\"False\"/>" +
        "<ForeColor Alpha=\"255\" Red=\"0\" Green=\"0\" Blue=\"0\"/>" +
        "</Attributes></Element></StyledText></TextObject>" +
        $"<Bounds X=\"{x}\" Y=\"{y}\" Width=\"{breedte}\" Height=\"{hoogte}\"/></ObjectInfo>";

    /// <param name="tekstEronder">
    /// Zet de leesbare tekst onder de streepjes. Op het kleine label staat de
    /// artikelcode er toch al onder, dus daar is die tweede regel alleen verlies
    /// van hoogte voor de streepjes zelf.
    /// </param>
    private static string Barcode(string naam, string waarde, int x, int y, int breedte, int hoogte,
                                  bool tekstEronder = true) =>
        "<ObjectInfo><BarcodeObject>" +
        $"<Name>{naam}</Name>{Kleuren}" +
        $"<Text>{Veilig(waarde)}</Text><Type>Code128Auto</Type><Size>Medium</Size>" +
        $"<TextPosition>{(tekstEronder ? "Bottom" : "None")}</TextPosition>" +
        "<TextFont Family=\"Arial\" Size=\"6\" Bold=\"False\" Italic=\"False\" " +
        "Underline=\"False\" Strikeout=\"False\"/>" +
        "<CheckSumFont Family=\"Arial\" Size=\"6\" Bold=\"False\" Italic=\"False\" " +
        "Underline=\"False\" Strikeout=\"False\"/>" +
        "<TextEmbedding>None</TextEmbedding><ECLevel>0</ECLevel>" +
        "<HorizontalAlignment>Center</HorizontalAlignment>" +
        // Een streepjescode heeft links en rechts rust nodig, anders vindt de scanner
        // het begin niet. Een paar tienden millimeter is genoeg.
        "<QuietZonesPadding Left=\"20\" Right=\"20\" Top=\"0\" Bottom=\"0\"/></BarcodeObject>" +
        $"<Bounds X=\"{x}\" Y=\"{y}\" Width=\"{breedte}\" Height=\"{hoogte}\"/></ObjectInfo>";

    private static string Afbeelding(string naam, byte[] inhoud, int x, int y, int breedte, int hoogte) =>
        "<ObjectInfo><ImageObject>" +
        $"<Name>{naam}</Name>{Kleuren}" +
        $"<Image>{Convert.ToBase64String(inhoud)}</Image>" +
        "<ScaleMode>Uniform</ScaleMode><BorderWidth>0</BorderWidth>" +
        "<BorderColor Alpha=\"255\" Red=\"0\" Green=\"0\" Blue=\"0\"/>" +
        "<HorizontalAlignment>Center</HorizontalAlignment>" +
        // Bij een afbeelding heet het midden 'Center' (bij tekst 'Middle').
        "<VerticalAlignment>Center</VerticalAlignment></ImageObject>" +
        $"<Bounds X=\"{x}\" Y=\"{y}\" Width=\"{breedte}\" Height=\"{hoogte}\"/></ObjectInfo>";

    // ============================================================
    //  PDF
    // ============================================================

    /// <summary>Het label als PDF; null als LibreOffice ontbreekt.</summary>
    public async Task<byte[]?> MaakPdfAsync(string svg)
        => await _pdf.NaarPdfAsync(Encoding.UTF8.GetBytes(svg), "label.svg");

    /// <summary>
    /// Schrijft het label naar de plek waar de laser het ophaalt. Levert het pad
    /// terug, zodat het scherm kan tonen waar het is neergezet.
    /// </summary>
    public async Task<string> ExporteerAsync(string svg)
    {
        var pdf = await MaakPdfAsync(svg)
            ?? throw new InvalidOperationException(
                "De PDF kon niet gemaakt worden. Staat LibreOffice op de server? " +
                "Zie Nda:LibreOffice in appsettings.json.");

        var pad = LaserPad;
        try
        {
            var map = Path.GetDirectoryName(pad);
            if (!string.IsNullOrWhiteSpace(map)) Directory.CreateDirectory(map);

            await File.WriteAllBytesAsync(pad, pdf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or DirectoryNotFoundException)
        {
            throw new InvalidOperationException(
                $"Kon niet schrijven naar {pad}. " +
                "Draait de app als Windows-service, dan bestaat een schijfletter als Z: daar niet; " +
                "zet in appsettings.json onder Laser:Pad een volledig netwerkpad (\\\\server\\share\\...). " +
                "Oorspronkelijke melding: " + ex.Message);
        }
        return pad;
    }

    // ---- hulpjes ----

    /// <summary>Getal met een punt als scheidingsteken; SVG kent geen komma's.</summary>
    private static string Mm(double waarde)
        => waarde.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Tekst veilig in XML zetten.</summary>
    private static string Veilig(string? tekst)
        => (tekst ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
