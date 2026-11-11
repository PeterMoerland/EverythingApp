using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MySqlConnector;

namespace EverythingApp.Services;

// Grootboekrekeningen (wens gebruiker 11-11-2026, Grootboekrekeningen_MariaDB.sql): per administratie (BV) de rekeningen,
// te onderhouden in Financiën ▸ Grootboekrekeningen en in te lezen uit Excel (de export uit de boekhouding met de kolommen
// NaamAdministratie, CodeGrootboekrekening, NaamGrootboekrekening, CodeRelatiekostenplaats). Elk afschriftrecord
// (banktransacties.GrootboekID) kan er een krijgen.

/// <summary>Een grootboekrekening. Bv = afkorting van de eigen BV (leeg als de administratie niet als BV herkend is).</summary>
public record Grootboekrekening(int Id, int? BvId, string Bv, string Administratie, string Code, string Naam,
                                string Kostenplaats, bool Actief)
{
    public string Weergave => $"{Code} {Naam}";
}

/// <summary>Leest het Excel-bestand met grootboekrekeningen en vergelijkt het met wat er al is.</summary>
public static class GrootboekExcel
{
    public record Regel(int Rij, string Administratie, string Code, string Naam, string Kostenplaats);

    /// <summary>Een cel: de tekst en of hij als getal in het bestand stond (dan kan een voorloopnul ontbreken).</summary>
    private readonly record struct Cel(string Tekst, bool Getal);

    /// <summary>
    /// Het eerste blad van een .xlsx, een echte .xls (oud Excel), of een "xls" die eigenlijk een HTML-tabel of een CSV is
    /// (exports uit boekhoudpakketten); de soort wordt aan de inhoud herkend, niet aan de naam (wens gebruiker 11-11-2026).
    /// Daarin een kopregel met (delen van) de kolomnamen, daaronder de rekeningen. Een code die als getal stond, verloor zijn
    /// voorloopnul: die wordt aangevuld tot de gebruikelijke lengte van de codes.
    /// </summary>
    public static (List<Regel> Regels, List<string> Meldingen) Lees(Stream bestand)
    {
        using var geheugen = new MemoryStream();
        bestand.CopyTo(geheugen);
        var bytes = geheugen.ToArray();
        var (tabel, soort) = LeesTabel(bytes);
        var (regels, meldingen) = Verwerk(tabel);
        if (soort != "xlsx") meldingen.Insert(0, $"Gelezen als {soort}.");
        return (regels, meldingen);
    }

    private static (List<List<Cel>> Tabel, string Soort) LeesTabel(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B) return (LeesXlsx(bytes), "xlsx");
        if (bytes.Length >= 8 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0) return (LeesXls(bytes), "Excel 97-2003 (.xls)");
        var tekst = Tekst(bytes);
        var begin = tekst.TrimStart().ToLowerInvariant();
        if (begin.StartsWith("<") && (begin.Contains("<table") || tekst.Contains("<tr", StringComparison.OrdinalIgnoreCase)))
            return (LeesHtml(tekst), "HTML-tabel");
        if (begin.StartsWith("<?xml") || begin.StartsWith("<workbook"))
            return (LeesSpreadsheetXml(tekst), "Excel 2003 XML");
        return (LeesCsv(tekst), "tekst (CSV)");
    }

    /// <summary>UTF-8 (met of zonder BOM), UTF-16 met BOM, anders Windows-1252 (oude exports).</summary>
    private static string Tekst(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try { return new System.Text.UTF8Encoding(false, true).GetString(bytes); }
        catch (System.Text.DecoderFallbackException)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private static List<List<Cel>> LeesXlsx(byte[] bytes)
    {
        var tabel = new List<List<Cel>>();
        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var wb = doc.WorkbookPart ?? throw new InvalidDataException("Dit is geen Excel-bestand (.xlsx).");
        var blad = wb.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault() ?? throw new InvalidDataException("Het bestand heeft geen blad.");
        var ws = (WorksheetPart)wb.GetPartById(blad.Id!);
        var teksten = wb.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? new();

        Cel Waarde(Cell c)
        {
            if (c.DataType?.Value == CellValues.SharedString)
                return new(int.TryParse(c.CellValue?.Text, out var i) && i >= 0 && i < teksten.Count ? teksten[i] : "", false);
            if (c.DataType?.Value == CellValues.InlineString) return new(c.InlineString?.InnerText ?? "", false);
            if (c.DataType?.Value == CellValues.String) return new(c.CellValue?.Text ?? "", false);
            return new(c.CellValue?.Text ?? "", c.DataType is null);
        }
        static int Kolom(string verwijzing)
        {
            var k = 0;
            foreach (var t in verwijzing)
            {
                if (!char.IsLetter(t)) break;
                k = k * 26 + (char.ToUpperInvariant(t) - 'A' + 1);
            }
            return k - 1;
        }

        foreach (var rij in ws.Worksheet?.Descendants<Row>() ?? Enumerable.Empty<Row>())
        {
            // een cel zonder verwijzing (A1) mag; dan is het de kolom na de vorige
            var cellen = new List<Cel>();
            var vorige = -1;
            foreach (var c in rij.Elements<Cell>())
            {
                var k = c.CellReference?.Value is { Length: > 0 } v ? Kolom(v) : vorige + 1;
                while (cellen.Count < k) cellen.Add(new("", false));
                if (k == cellen.Count) cellen.Add(Waarde(c)); else cellen[k] = Waarde(c);
                vorige = k;
            }
            tabel.Add(cellen);
        }
        return tabel;
    }

    private static List<List<Cel>> LeesXls(byte[] bytes)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);   // nodig voor oude .xls
        var tabel = new List<List<Cel>>();
        using var lezer = ExcelDataReader.ExcelReaderFactory.CreateReader(new MemoryStream(bytes));
        while (lezer.Read())
        {
            var rij = new List<Cel>();
            for (var i = 0; i < lezer.FieldCount; i++)
            {
                var v = lezer.GetValue(i);
                rij.Add(v switch
                {
                    null => new("", false),
                    double d => new(d.ToString(System.Globalization.CultureInfo.InvariantCulture), true),
                    DateTime dt => new(dt.ToString("yyyy-MM-dd"), false),
                    _ => new(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "", false),
                });
            }
            tabel.Add(rij);
        }
        return tabel;
    }

    private static List<List<Cel>> LeesHtml(string html)
    {
        var tabel = new List<List<Cel>>();
        foreach (System.Text.RegularExpressions.Match tr in System.Text.RegularExpressions.Regex.Matches(html, @"<tr\b[^>]*>(.*?)</tr>",
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            var rij = new List<Cel>();
            foreach (System.Text.RegularExpressions.Match td in System.Text.RegularExpressions.Regex.Matches(tr.Groups[1].Value, @"<t[dh]\b[^>]*>(.*?)</t[dh]>",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline))
            {
                var t = System.Text.RegularExpressions.Regex.Replace(td.Groups[1].Value, "<[^>]+>", "");
                rij.Add(new(System.Net.WebUtility.HtmlDecode(t).Replace('\u00A0', ' ').Trim(), false));
            }
            tabel.Add(rij);
        }
        return tabel;
    }

    /// <summary>Excel 2003 XML (SpreadsheetML): Row/Cell/Data.</summary>
    private static List<List<Cel>> LeesSpreadsheetXml(string xml)
    {
        var tabel = new List<List<Cel>>();
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var blad = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Worksheet") ?? doc.Root!;
        foreach (var rij in blad.Descendants().Where(e => e.Name.LocalName == "Row"))
        {
            var cellen = new List<Cel>();
            foreach (var c in rij.Elements().Where(e => e.Name.LocalName == "Cell"))
            {
                var index = c.Attributes().FirstOrDefault(a => a.Name.LocalName == "Index")?.Value;
                if (int.TryParse(index, out var k)) while (cellen.Count < k - 1) cellen.Add(new("", false));
                var data = c.Elements().FirstOrDefault(e => e.Name.LocalName == "Data");
                var type = data?.Attributes().FirstOrDefault(a => a.Name.LocalName == "Type")?.Value;
                cellen.Add(new(data?.Value ?? "", type == "Number"));
            }
            tabel.Add(cellen);
        }
        return tabel;
    }

    /// <summary>CSV of TSV: scheidingsteken uit de eerste regel (tab, puntkomma of komma), velden tussen aanhalingstekens.</summary>
    private static List<List<Cel>> LeesCsv(string tekst)
    {
        var regels = tekst.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var eerste = regels.FirstOrDefault(r => r.Trim().Length > 0) ?? "";
        var sep = eerste.Count(c => c == '\t') > 0 ? '\t' : eerste.Count(c => c == ';') >= eerste.Count(c => c == ',') ? ';' : ',';
        var tabel = new List<List<Cel>>();
        foreach (var r in regels)
        {
            var rij = new List<Cel>();
            var veld = new System.Text.StringBuilder();
            var tussen = false;
            for (var i = 0; i < r.Length; i++)
            {
                var c = r[i];
                if (c == '"') { if (tussen && i + 1 < r.Length && r[i + 1] == '"') { veld.Append('"'); i++; } else tussen = !tussen; }
                else if (c == sep && !tussen) { rij.Add(new(veld.ToString().Trim(), false)); veld.Clear(); }
                else veld.Append(c);
            }
            rij.Add(new(veld.ToString().Trim(), false));
            tabel.Add(rij);
        }
        return tabel;
    }

    /// <summary>Kopregel zoeken, regels maken, voorloopnullen aanvullen en dubbele codes melden.</summary>
    private static (List<Regel> Regels, List<string> Meldingen) Verwerk(List<List<Cel>> tabel)
    {
        var regels = new List<Regel>();
        var meldingen = new List<string>();
        int adm = -1, code = -1, naam = -1, kp = -1;
        var codeAlsGetal = new List<int>();     // index in regels
        for (var nr = 0; nr < tabel.Count; nr++)
        {
            var cellen = tabel[nr];
            string In(int k) => k >= 0 && k < cellen.Count ? cellen[k].Tekst.Trim() : "";

            if (code < 0)
            {
                // de kopregel zoeken
                for (var k = 0; k < cellen.Count; k++)
                {
                    var kop = cellen[k].Tekst.Trim().ToLowerInvariant().Replace(" ", "");
                    if (kop.Contains("administratie")) adm = k;
                    else if (kop.Contains("kostenplaats")) kp = k;
                    else if (kop.StartsWith("code") || kop == "rekening" || kop == "grootboek" || kop == "grootboekrekening") code = k;
                    else if (kop.StartsWith("naam") || kop == "omschrijving") naam = k;
                }
                if (code < 0) { adm = naam = kp = -1; continue; }
                if (naam < 0) throw new InvalidDataException("Geen kolom met de naam van de rekening gevonden (bijvoorbeeld NaamGrootboekrekening).");
                if (adm < 0) meldingen.Add("Geen kolom NaamAdministratie: alle rekeningen komen onder de administratie die je kiest.");
                continue;
            }

            var c0 = In(code);
            if (c0.Length == 0) continue;
            if (code < cellen.Count && cellen[code].Getal && double.TryParse(c0, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var getal) && getal == Math.Floor(getal))
            {
                c0 = ((long)getal).ToString(System.Globalization.CultureInfo.InvariantCulture);
                codeAlsGetal.Add(regels.Count);
            }
            regels.Add(new Regel(nr + 1, In(adm), c0, In(naam), In(kp)));
        }
        if (code < 0) throw new InvalidDataException("Geen kopregel met CodeGrootboekrekening gevonden.");

        // voorloopnullen terug bij codes die als getal in het bestand stonden
        if (codeAlsGetal.Count > 0)
        {
            // de gebruikelijke lengte: van de codes die als tekst stonden, anders van alle codes (01100 en 11000 -> 5)
            var basis = regels.Where((r, i) => !codeAlsGetal.Contains(i) && r.Code.All(char.IsDigit)).ToList();
            if (basis.Count == 0) basis = regels.Where(r => r.Code.All(char.IsDigit)).ToList();
            var lengte = basis.GroupBy(r => r.Code.Length).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key)
                              .Select(g => g.Key).FirstOrDefault();
            if (lengte > 0)
            {
                var aangevuld = 0;
                foreach (var i in codeAlsGetal)
                    if (regels[i].Code.Length < lengte) { regels[i] = regels[i] with { Code = regels[i].Code.PadLeft(lengte, '0') }; aangevuld++; }
                if (aangevuld > 0) meldingen.Add($"{aangevuld} code(s) stonden als getal in het bestand; de voorloopnul is aangevuld tot {lengte} cijfers.");
            }
        }

        var dubbel = regels.GroupBy(r => (r.Administratie.ToLowerInvariant(), r.Code)).Where(g => g.Count() > 1).ToList();
        if (dubbel.Count > 0)
            meldingen.Add($"{dubbel.Count} code(s) staan dubbel in het bestand (bijv. {dubbel[0].Key.Item2}); de laatste telt.");
        return (regels, meldingen);
    }

    public enum Wijziging { Nieuw, Gewijzigd, Gelijk }

    /// <summary>Per regel: nieuw, gewijzigd (naam, kostenplaats of weer actief) of gelijk; plus de actieve die ontbreken.</summary>
    public static (List<(Regel Regel, Wijziging Wat, Grootboekrekening? Oud)> Per, List<Grootboekrekening> Ontbrekend)
        Vergelijk(IReadOnlyList<Regel> regels, IReadOnlyList<Grootboekrekening> bestaand)
    {
        var index = bestaand.GroupBy(g => Sleutel(g.Administratie, g.Code)).ToDictionary(g => g.Key, g => g.First());
        var per = new List<(Regel, Wijziging, Grootboekrekening?)>();
        foreach (var r in regels.GroupBy(r => Sleutel(r.Administratie, r.Code)).Select(g => g.Last()))
        {
            if (!index.TryGetValue(Sleutel(r.Administratie, r.Code), out var oud)) per.Add((r, Wijziging.Nieuw, null));
            else if (oud.Naam != r.Naam || oud.Kostenplaats != r.Kostenplaats || !oud.Actief) per.Add((r, Wijziging.Gewijzigd, oud));
            else per.Add((r, Wijziging.Gelijk, oud));
        }
        var inBestand = regels.Select(r => Sleutel(r.Administratie, r.Code)).ToHashSet();
        var administraties = regels.Select(r => r.Administratie.ToLowerInvariant()).ToHashSet();
        var ontbrekend = bestaand.Where(g => g.Actief && administraties.Contains(g.Administratie.ToLowerInvariant())
                                             && !inBestand.Contains(Sleutel(g.Administratie, g.Code))).ToList();
        return (per, ontbrekend);
    }

    public static string Sleutel(string administratie, string code) => administratie.Trim().ToLowerInvariant() + "|" + code.Trim();
}

public interface IGrootboekRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();
    /// <summary>Alle rekeningen, ook niet-actieve; op administratie en code.</summary>
    Task<List<Grootboekrekening>> LijstAsync();
    /// <summary>Nieuw (Id 0) of wijzigen; null = gelukt, anders de reden.</summary>
    Task<string?> BewaarAsync(Grootboekrekening g, int? door);
    Task ZetActiefAsync(int id, bool actief, int? door);
    /// <summary>Het Excel-bestand verwerken: nieuw toevoegen, gewijzigd bijwerken; ontbrekende eventueel op niet-actief.</summary>
    Task<(int Nieuw, int Gewijzigd, int Uit)> ImporteerAsync(IReadOnlyList<GrootboekExcel.Regel> regels, bool ontbrekendUit, int? door);
    /// <summary>Heeft banktransacties de kolom GrootboekID (Grootboekrekeningen_MariaDB.sql)?</summary>
    Task<bool> KoppelingAanwezigAsync();
    /// <summary>De grootboekrekening van een afschriftrecord (null = geen).</summary>
    Task ZetTransactieAsync(int transactieId, int? grootboekId);
}

public class GeenGrootboekRepository : IGrootboekRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<Grootboekrekening>> LijstAsync() => Task.FromResult(new List<Grootboekrekening>());
    public Task<string?> BewaarAsync(Grootboekrekening g, int? door) => Task.FromResult<string?>("Geen live database.");
    public Task ZetActiefAsync(int id, bool actief, int? door) => Task.CompletedTask;
    public Task<(int Nieuw, int Gewijzigd, int Uit)> ImporteerAsync(IReadOnlyList<GrootboekExcel.Regel> regels, bool ontbrekendUit, int? door)
        => Task.FromResult((0, 0, 0));
    public Task<bool> KoppelingAanwezigAsync() => Task.FromResult(false);
    public Task ZetTransactieAsync(int transactieId, int? grootboekId) => Task.CompletedTask;
}

/// <summary>Lokaal testen zonder de tabel (Grootboek__Geheugen=true): alles in het geheugen, weg na herstart.</summary>
public class GeheugenGrootboekRepository : IGrootboekRepository
{
    private readonly List<Grootboekrekening> _lijst = new();
    private readonly object _slot = new();
    private int _volgende = 1;
    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);
    public Task<List<Grootboekrekening>> LijstAsync()
    {
        lock (_slot) return Task.FromResult(_lijst.OrderBy(g => g.Administratie).ThenBy(g => g.Code).ToList());
    }
    public Task<string?> BewaarAsync(Grootboekrekening g, int? door)
    {
        lock (_slot)
        {
            if (_lijst.Any(x => x.Id != g.Id && GrootboekExcel.Sleutel(x.Administratie, x.Code) == GrootboekExcel.Sleutel(g.Administratie, g.Code)))
                return Task.FromResult<string?>($"Code {g.Code} bestaat al in {g.Administratie}.");
            if (g.Id == 0) _lijst.Add(g with { Id = _volgende++ });
            else { var i = _lijst.FindIndex(x => x.Id == g.Id); if (i >= 0) _lijst[i] = g; }
            return Task.FromResult<string?>(null);
        }
    }
    public Task ZetActiefAsync(int id, bool actief, int? door)
    {
        lock (_slot) { var i = _lijst.FindIndex(x => x.Id == id); if (i >= 0) _lijst[i] = _lijst[i] with { Actief = actief }; }
        return Task.CompletedTask;
    }
    public Task<(int Nieuw, int Gewijzigd, int Uit)> ImporteerAsync(IReadOnlyList<GrootboekExcel.Regel> regels, bool ontbrekendUit, int? door)
    {
        lock (_slot)
        {
            var (per, ontbrekend) = GrootboekExcel.Vergelijk(regels, _lijst);
            int nieuw = 0, gewijzigd = 0;
            foreach (var (r, wat, oud) in per)
            {
                if (wat == GrootboekExcel.Wijziging.Nieuw) { _lijst.Add(new(_volgende++, null, "", r.Administratie, r.Code, r.Naam, r.Kostenplaats, true)); nieuw++; }
                else if (wat == GrootboekExcel.Wijziging.Gewijzigd && oud is not null)
                { var i = _lijst.FindIndex(x => x.Id == oud.Id); _lijst[i] = oud with { Naam = r.Naam, Kostenplaats = r.Kostenplaats, Actief = true }; gewijzigd++; }
            }
            var uit = 0;
            if (ontbrekendUit)
                foreach (var o in ontbrekend) { var i = _lijst.FindIndex(x => x.Id == o.Id); _lijst[i] = o with { Actief = false }; uit++; }
            return Task.FromResult((nieuw, gewijzigd, uit));
        }
    }
    public Task<bool> KoppelingAanwezigAsync() => Task.FromResult(false);
    public Task ZetTransactieAsync(int transactieId, int? grootboekId) => Task.CompletedTask;
}

public class MySqlGrootboekRepository : IGrootboekRepository
{
    private readonly string _cs;
    public MySqlGrootboekRepository(string cs) => _cs = cs;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<bool> TabelAanwezigAsync()
    {
        await using var cn = await OpenAsync();
        return await GrootboekKoppeling.TabelAsync(cn);
    }

    public async Task<bool> KoppelingAanwezigAsync()
    {
        await using var cn = await OpenAsync();
        return await GrootboekKoppeling.AanwezigAsync(cn);
    }

    /// <summary>De eigen BV met deze naam of afkorting (punten en spaties tellen niet mee).</summary>
    private const string BvVanAdministratie =
        "(SELECT r.`id_supplier` FROM `Relations` r WHERE COALESCE(r.`Werkmaatschappij`, 0) <> 0 " +
        "   AND (REPLACE(REPLACE(LOWER(r.`name`), '.', ''), ' ', '') = REPLACE(REPLACE(LOWER(@adm), '.', ''), ' ', '') " +
        "        OR LOWER(COALESCE(r.`Afkorting`, '')) = LOWER(@adm)) LIMIT 1)";

    public async Task<List<Grootboekrekening>> LijstAsync()
    {
        var lijst = new List<Grootboekrekening>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT g.`GrootboekID`, g.`BvID`, COALESCE(NULLIF(b.`Afkorting`, ''), b.`name`, ''), g.`Administratie`, g.`Code`, g.`Naam`, " +
            "       COALESCE(g.`CodeRelatieKostenplaats`, ''), g.`Actief` " +
            "  FROM `Grootboekrekeningen` g LEFT JOIN `Relations` b ON b.`id_supplier` = g.`BvID` " +
            " ORDER BY g.`Administratie`, g.`Code`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Grootboekrekening(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.GetString(2), r.GetString(3),
                                            r.GetString(4), r.GetString(5), r.GetString(6), Convert.ToInt64(r.GetValue(7)) != 0));
        return lijst;
    }

    public async Task<string?> BewaarAsync(Grootboekrekening g, int? door)
    {
        var adm = g.Administratie.Trim(); var code = g.Code.Trim(); var naam = g.Naam.Trim();
        if (adm.Length == 0 || code.Length == 0 || naam.Length == 0) return "Administratie, code en naam zijn verplicht.";
        await using var cn = await OpenAsync();
        await using (var dub = new MySqlCommand(
            "SELECT COUNT(*) FROM `Grootboekrekeningen` WHERE `Administratie` = @adm AND `Code` = @code AND `GrootboekID` <> @id", cn))
        {
            dub.Parameters.AddWithValue("@adm", adm); dub.Parameters.AddWithValue("@code", code); dub.Parameters.AddWithValue("@id", g.Id);
            if (Convert.ToInt64(await dub.ExecuteScalarAsync()) > 0) return $"Code {code} bestaat al in {adm}.";
        }
        await using var cmd = new MySqlCommand(g.Id == 0
            ? "INSERT INTO `Grootboekrekeningen` (`BvID`, `Administratie`, `Code`, `Naam`, `CodeRelatieKostenplaats`, `Actief`, `GewijzigdOp`, `GewijzigdDoor`) " +
              $"VALUES ({BvVanAdministratie}, @adm, @code, @naam, NULLIF(@kp, ''), @actief, NOW(), @door)"
            : $"UPDATE `Grootboekrekeningen` SET `BvID` = {BvVanAdministratie}, `Administratie` = @adm, `Code` = @code, `Naam` = @naam, " +
              "       `CodeRelatieKostenplaats` = NULLIF(@kp, ''), `Actief` = @actief, `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door " +
              " WHERE `GrootboekID` = @id", cn);
        cmd.Parameters.AddWithValue("@adm", adm); cmd.Parameters.AddWithValue("@code", code); cmd.Parameters.AddWithValue("@naam", naam);
        cmd.Parameters.AddWithValue("@kp", g.Kostenplaats.Trim()); cmd.Parameters.AddWithValue("@actief", g.Actief ? 1 : 0);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value); cmd.Parameters.AddWithValue("@id", g.Id);
        await cmd.ExecuteNonQueryAsync();
        return null;
    }

    public async Task ZetActiefAsync(int id, bool actief, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Grootboekrekeningen` SET `Actief` = @a, `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door WHERE `GrootboekID` = @id", cn);
        cmd.Parameters.AddWithValue("@a", actief ? 1 : 0);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(int Nieuw, int Gewijzigd, int Uit)> ImporteerAsync(IReadOnlyList<GrootboekExcel.Regel> regels, bool ontbrekendUit, int? door)
    {
        var bestaand = await LijstAsync();
        var (per, ontbrekend) = GrootboekExcel.Vergelijk(regels, bestaand);
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        int nieuw = 0, gewijzigd = 0, uit = 0;
        foreach (var (r, wat, oud) in per)
        {
            if (wat == GrootboekExcel.Wijziging.Gelijk) continue;
            await using var cmd = new MySqlCommand(wat == GrootboekExcel.Wijziging.Nieuw
                ? "INSERT INTO `Grootboekrekeningen` (`BvID`, `Administratie`, `Code`, `Naam`, `CodeRelatieKostenplaats`, `Actief`, `GewijzigdOp`, `GewijzigdDoor`) " +
                  $"VALUES ({BvVanAdministratie}, @adm, @code, @naam, NULLIF(@kp, ''), 1, NOW(), @door)"
                : $"UPDATE `Grootboekrekeningen` SET `Naam` = @naam, `CodeRelatieKostenplaats` = NULLIF(@kp, ''), `Actief` = 1, " +
                  $"       `BvID` = COALESCE(`BvID`, {BvVanAdministratie}), `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door WHERE `GrootboekID` = @id", cn, tx);
            cmd.Parameters.AddWithValue("@adm", r.Administratie.Trim()); cmd.Parameters.AddWithValue("@code", r.Code.Trim());
            cmd.Parameters.AddWithValue("@naam", r.Naam.Trim()); cmd.Parameters.AddWithValue("@kp", r.Kostenplaats.Trim());
            cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value); cmd.Parameters.AddWithValue("@id", oud?.Id ?? 0);
            await cmd.ExecuteNonQueryAsync();
            if (wat == GrootboekExcel.Wijziging.Nieuw) nieuw++; else gewijzigd++;
        }
        if (ontbrekendUit)
            foreach (var o in ontbrekend)
            {
                await using var cmd = new MySqlCommand(
                    "UPDATE `Grootboekrekeningen` SET `Actief` = 0, `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door WHERE `GrootboekID` = @id", cn, tx);
                cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", o.Id);
                await cmd.ExecuteNonQueryAsync();
                uit++;
            }
        await tx.CommitAsync();
        return (nieuw, gewijzigd, uit);
    }

    public async Task ZetTransactieAsync(int transactieId, int? grootboekId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `banktransacties` SET `GrootboekID` = @g WHERE `BankTransacties_ID` = @t", cn);
        cmd.Parameters.AddWithValue("@g", (object?)grootboekId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", transactieId);
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>Bestaan de tabel en de kolom banktransacties.GrootboekID? Alleen "ja" wordt onthouden (na het script zonder herstart).</summary>
public static class GrootboekKoppeling
{
    private static bool _tabel, _kolom;

    public static async Task<bool> TabelAsync(MySqlConnection cn)
    {
        if (_tabel) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Grootboekrekeningen'", cn);
        return _tabel = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    public static async Task<bool> AanwezigAsync(MySqlConnection cn)
    {
        if (_kolom) return true;
        if (!await TabelAsync(cn)) return false;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'banktransacties' " +
            "   AND COLUMN_NAME = 'GrootboekID'", cn);
        return _kolom = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }
}
