using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een treffer in de artikellijst met zijn kostprijs. Termen/VanTermen: hoeveel zoektermen het artikel raakt.</summary>
/// <param name="Bron">"kostprijs" = Artikelen.VastgesteldeKostprijs, "leverancier" = de duurste leverancier
/// (Access-query Artikel_MaxInkoopprijs), "" = geen prijs bekend.</param>
public record ArtikelPrijsTreffer(int Id, string Code, string Fabrikantcode, string Omschrijving, string Merk,
                                  decimal Kostprijs, string Bron, string Leverancier, int Termen, int VanTermen);

/// <summary>
/// Slim zoeken in de artikellijst voor een materiaalregel van een calculatie (wens gebruiker 18-11-2026): de tekst van
/// de regel ("NX-PF0630", "kabel 3x1,5 ölflex") wordt ook in losse delen gezocht (gescheiden door spaties, streepjes en
/// dergelijke). Een artikel hoeft niet alle delen te raken; wat het best past staat bovenaan: eerst de hele tekst als
/// code, dan alle delen, dan een deel ervan. Maakdelen doen mee. Geeft de kostprijs erbij (VastgesteldeKostprijs, anders
/// de duurste leverancier, zoals de offertes rekenen). Alleen lezen.
/// </summary>
public class ArtikelPrijsZoeker
{
    private readonly string? _cs;

    public ArtikelPrijsZoeker(string? connectionString) => _cs = connectionString;

    public bool Beschikbaar => !string.IsNullOrWhiteSpace(_cs);

    private static readonly char[] Scheiders = { ' ', '\t', '\r', '\n', '-', '_', '/', '\\', ',', ';', '+', '(', ')', '[', ']', ':', '|' };

    /// <summary>De losse zoektermen: delen van minstens twee tekens, elk één keer, hooguit zes (de langste eerst weg bij meer).
    /// Een komma tussen twee cijfers is een decimaalteken en splitst niet ("3x1,5" blijft heel).</summary>
    public static List<string> Termen(string? tekst)
    {
        const char decimaal = (char)1;   // tijdelijke plaatshouder voor de decimale komma
        var bron = System.Text.RegularExpressions.Regex.Replace(tekst ?? "", @"(?<=\d),(?=\d)", decimaal.ToString());
        var delen = bron.Split(Scheiders, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Replace(decimaal, ','))
            .Where(d => d.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // meer dan zes: de zes langste houden (korte stukjes zeggen het minst), in de getypte volgorde
        if (delen.Count > 6)
        {
            var houd = delen.OrderByDescending(d => d.Length).Take(6).ToHashSet(StringComparer.OrdinalIgnoreCase);
            delen = delen.Where(houd.Contains).ToList();
        }
        return delen;
    }

    /// <summary>Alleen letters en cijfers: "NX-PF 0630" en "NXPF0630" zijn zo hetzelfde.</summary>
    public static string Compact(string? tekst) => new((tekst ?? "").Where(char.IsLetterOrDigit).ToArray());

    /// <summary>% en _ zijn in LIKE jokers: letterlijk zoeken.</summary>
    private static string Like(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public async Task<List<ArtikelPrijsTreffer>> ZoekAsync(string? tekst, int max)
    {
        var uit = new List<ArtikelPrijsTreffer>();
        var heel = (tekst ?? "").Trim();
        if (!Beschikbaar || heel.Length < 2) return uit;
        max = Math.Clamp(max, 1, 200);

        // "#4103" of alleen cijfers: ook op het MDE-nummer
        var termen = Termen(heel.TrimStart('#'));
        if (termen.Count == 0) termen.Add(heel);
        var compact = Compact(heel);

        const string code = "COALESCE(a.`ArtikelCodeMDE`, '')", fab = "COALESCE(a.`ArtikelCodeFactory`, '')", oms = "COALESCE(a.`OmschrijvingNL`, '')";
        const string merk = "COALESCE(b.`Name`, '')";
        string Kaal(string kolom) => $"REPLACE(REPLACE(REPLACE(REPLACE(REPLACE({kolom}, '-', ''), ' ', ''), '.', ''), '/', ''), '_', '')";

        // Een term met een decimaal ("3x1,5", "1.5mm"): komma en punt zijn hetzelfde, in de term en in het artikel.
        bool Decimaal(string t) => System.Text.RegularExpressions.Regex.IsMatch(t, @"\d[.,]\d");
        string K(string kolom, int i) => Decimaal(termen[i]) ? $"REPLACE({kolom}, ',', '.')" : kolom;

        // per term: raakt het artikel hem ergens? (1 of 0)
        var raakt = termen.Select((_, i) =>
            $"({K(code, i)} LIKE @t{i} OR {K(fab, i)} LIKE @t{i} OR {K(oms, i)} LIKE @t{i} OR {merk} LIKE @t{i} OR CAST(a.`ArtikelID` AS CHAR) = @g{i})").ToList();
        var inCode = termen.Select((_, i) => $"({K(code, i)} LIKE @t{i} OR {K(fab, i)} LIKE @t{i})").ToList();
        var aantal = string.Join(" + ", raakt);
        var kaal = compact.Length >= 3 ? $"({Kaal(code)} LIKE @kaal OR {Kaal(fab)} LIKE @kaal)" : "0";

        // Punten: de hele tekst als code > alle termen > begin van de code > ergens in de code > zonder streepjes gelijk >
        // in de omschrijving (alleen als de code hem niet heeft: anders wint een kit die het artikel noemt van het artikel
        // zelf); daarna telt elke term (in de code zwaarder dan elders). Bij gelijke punten de kortste code eerst.
        var punten =
            $"(({code} = @heel OR {fab} = @heel) * 1000) + (({aantal}) = {termen.Count}) * 500 + " +
            $"(({code} LIKE @begin OR {fab} LIKE @begin) * 300) + (({code} LIKE @in OR {fab} LIKE @in) * 200) + " +
            $"({kaal} * 150) + (({oms} LIKE @in AND NOT ({code} LIKE @in OR {fab} LIKE @in)) * 100) + " +
            string.Join(" + ", inCode.Select(c => $"({c} * 30)")) + $" + (({aantal}) * 10)";

        var sql =
            $"SELECT a.`ArtikelID`, {code}, {fab}, {oms}, {merk}, COALESCE(a.`VastgesteldeKostprijs`, 0), ({aantal}) AS n, ({punten}) AS punten, " +
            // de duurste leverancier (Access: Artikel_MaxInkoopprijs), voor wie geen vastgestelde kostprijs heeft en voor de kolom Leverancier
            "       (SELECT CONCAT(TRUNCATE(COALESCE(s.`PrijsPerVE`, 0) * (1 - COALESCE(s.`Korting`, 0)) / IF(COALESCE(s.`StuksPerVE`, 0) = 0, 1, s.`StuksPerVE`), 3), '|', COALESCE(r.`name`, '')) " +
            "          FROM `ArtikelSupplier` s LEFT JOIN `Relations` r ON r.`id_supplier` = s.`SupplierID` " +
            "         WHERE s.`ArtikelID` = a.`ArtikelID` AND COALESCE(s.`SupplierID`, 0) <> 0 " +
            "         ORDER BY COALESCE(s.`PrijsPerVE`, 0) * (1 - COALESCE(s.`Korting`, 0)) / IF(COALESCE(s.`StuksPerVE`, 0) = 0, 1, s.`StuksPerVE`) DESC LIMIT 1) AS lev " +
            "  FROM `Artikelen` a LEFT JOIN `Brands` b ON b.`BrandID` = a.`BrandID` " +
            $" WHERE ({string.Join(" OR ", raakt)}{(compact.Length >= 3 ? " OR " + kaal : "")}) " +
            $" ORDER BY punten DESC, CHAR_LENGTH({code}), {code} LIMIT {max}";

        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@heel", heel);
        cmd.Parameters.AddWithValue("@begin", Like(heel) + "%");
        cmd.Parameters.AddWithValue("@in", "%" + Like(heel) + "%");
        if (compact.Length >= 3) cmd.Parameters.AddWithValue("@kaal", "%" + Like(compact) + "%");
        for (var i = 0; i < termen.Count; i++)
        {
            cmd.Parameters.AddWithValue("@t" + i, "%" + Like(Decimaal(termen[i]) ? termen[i].Replace(',', '.') : termen[i]) + "%");
            cmd.Parameters.AddWithValue("@g" + i, termen[i]);
        }

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            string S(int i) => rd.IsDBNull(i) ? "" : (rd.GetValue(i).ToString() ?? "").Trim();
            var vast = rd.IsDBNull(5) ? 0m : Convert.ToDecimal(rd.GetValue(5));
            decimal inkoop = 0; var leverancier = "";
            var lev = S(8);
            var streep = lev.IndexOf('|');
            if (streep > 0)
            {
                decimal.TryParse(lev[..streep], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out inkoop);
                leverancier = lev[(streep + 1)..];
            }
            var prijs = vast > 0 ? vast : inkoop;
            uit.Add(new ArtikelPrijsTreffer(rd.GetInt32(0), S(1), S(2), S(3), S(4), Math.Round(prijs, 4),
                vast > 0 ? "kostprijs" : inkoop > 0 ? "leverancier" : "", leverancier,
                Math.Min(Convert.ToInt32(rd.GetValue(6)), termen.Count), termen.Count));
        }
        return uit;
    }
}
