using System.Text.Json;

namespace EverythingApp.Services;

/// <summary>Eén prijsstaffel van JLCPCB: stukprijs vanaf dit aantal.</summary>
public sealed record JlcPrijs(int Vanaf, decimal Stukprijs);

/// <summary>Detailgegevens van één JLCPCB-onderdeel (getComponentDetailByCode).</summary>
public sealed record JlcDetail(
    string Code,
    string Mpn,
    string Merk,
    string Specificatie,
    string Omschrijving,
    string Categorie,
    string Voorraad,
    IReadOnlyList<JlcPrijs> Prijzen,
    string Datasheet,
    string Bibliotheek);

/// <summary>
/// Synchroniseert de JLCPCB-onderdelenbibliotheek naar de lokale tabel
/// JlcComponenten. Het platform kent geen zoeken-op-MPN; wel een dump van de
/// hele bibliotheek via getComponentLibraryList (cursor met lastKey), met per
/// component precies wat wij nodig hebben: C-nummer, MPN en behuizing.
/// Eén keer (en daarna af en toe) verversen, en de export zoekt lokaal.
/// </summary>
public class JlcOnderdelen(JlcKoppeling jlc, IJlcComponentRepository repo)
{
    public bool Beschikbaar => jlc.Beschikbaar && repo.Beschikbaar;

    private static string? Leeg(string s) => s.Length == 0 ? null : s;

    /// <summary>
    /// Zoekt het C-nummer bij één MPN — precies één onderdeel, geen miljoen.
    ///
    /// De officiële JLCPCB-interface kan alleen op C-nummer opvragen of de
    /// hele lijst doorbladeren; zoeken op MPN kan hij niet. Daarvoor gebruiken
    /// we jlcsearch (jlcsearch.tscircuit.com), een open zoekdienst over de
    /// voorraad van JLCPCB. Het gevonden C-nummer verifiëren we vervolgens
    /// bij JLCPCB zelf — die levert de actuele voorraad en of het een
    /// 'basic' onderdeel is. De uitkomst gaat in de vertaaltabel, zodat
    /// dezelfde MPN daarna zonder internet gevonden wordt.
    /// </summary>
    public async Task<(JlcComponent? Gevonden, string? Fout)> ZoekOpMpnAsync(string mpn)
    {
        mpn = mpn.Trim();
        if (mpn.Length == 0) return (null, null);

        // Is het zelf al een C-nummer? Dan rechtstreeks bij JLCPCB opvragen.
        var isCnummer = mpn.Length > 1 && (mpn[0] is 'C' or 'c') && mpn.Skip(1).All(char.IsDigit);
        var code = isCnummer ? mpn.ToUpperInvariant() : null;
        string? zoekFout = null;

        if (code is null)
        {
            var (gevonden, fout) = await ZoekViaJlcsearchAsync(mpn);
            code = gevonden;
            zoekFout = fout;
        }
        if (code is null) return (null, zoekFout);

        // Verifiëren en aanvullen bij JLCPCB zelf (voorraad, basic/extended).
        JlcComponent component;
        var (detail, _) = jlc.Beschikbaar ? await DetailAsync(code) : (null, null);
        if (detail is not null)
        {
            component = new JlcComponent(
                detail.Code.Length > 0 ? detail.Code : code,
                detail.Mpn.Length > 0 ? detail.Mpn : mpn,
                Leeg(detail.Specificatie), Leeg(detail.Merk), Leeg(detail.Bibliotheek),
                int.TryParse(detail.Voorraad, out var v) ? v : null,
                Leeg(detail.Categorie));
        }
        else
        {
            component = new JlcComponent(code, mpn, null);
        }

        if (repo.Beschikbaar)
        {
            try { await repo.BewaarBatchAsync(new[] { component }); } catch { /* geen ramp */ }
        }
        return (component, null);
    }

    /// <summary>
    /// Vraagt jlcsearch om het LCSC-nummer bij een MPN. Alleen een treffer
    /// waarvan het fabrikantsnummer exact overeenkomt telt mee — anders
    /// zouden we het verkeerde onderdeel koppelen.
    /// </summary>
    private async Task<(string? Code, string? Fout)> ZoekViaJlcsearchAsync(string mpn)
    {
        try
        {
            var adres = "https://jlcsearch.tscircuit.com/api/search?limit=20&q=" +
                        Uri.EscapeDataString(mpn);
            var antwoord = await Zoeker.GetAsync(adres);
            if (!antwoord.IsSuccessStatusCode)
                return (null, $"jlcsearch antwoordt {(int)antwoord.StatusCode}.");

            using var doc = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("components", out var lijst) ||
                lijst.ValueKind != JsonValueKind.Array)
                return (null, null);

            foreach (var c in lijst.EnumerateArray())
            {
                var mfr = c.TryGetProperty("mfr", out var m) ? m.GetString() ?? "" : "";
                if (!Gelijk(mfr, mpn)) continue;
                if (!c.TryGetProperty("lcsc", out var l)) continue;

                var nummer = l.ValueKind == JsonValueKind.Number ? l.GetInt64().ToString()
                                                                 : l.GetString() ?? "";
                if (nummer.Length == 0) continue;
                return (nummer.StartsWith('C') ? nummer : "C" + nummer, null);
            }
            return (null, null);   // geen exacte treffer
        }
        catch (Exception ex)
        {
            return (null, "jlcsearch niet bereikbaar: " + ex.Message);
        }
    }

    /// <summary>Onderdeelnummers vergelijken zonder streepjes en spaties:
    /// fabrikanten schrijven ze niet altijd hetzelfde.</summary>
    private static bool Gelijk(string a, string b)
    {
        static string Kaal(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());
        return Kaal(a).Equals(Kaal(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Aparte client voor jlcsearch; die vraagt geen ondertekening.</summary>
    private static readonly HttpClient Zoeker = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Proefaanvraag op 'Component information interface'
    /// (getComponentInfos): haalt één pagina op en vertelt wat erin zit —
    /// hoeveel onderdelen, of er een vervolgpagina is (lastKey) en hoe het
    /// eerste onderdeel eruitziet. Daarmee kunnen we bepalen of het zinvol
    /// is die lijst binnen te halen, of dat het er te veel zijn.
    /// </summary>
    public async Task<string> ProefAsync(int paginaGrootte = 100)
    {
        if (!jlc.Beschikbaar) return "Geen JLCPCB-sleutels ingesteld.";

        var body = JsonSerializer.Serialize(new { lastKey = (string?)null, pageSize = paginaGrootte });
        var (antwoord, fout) = await jlc.PostAsync(
            "/overseas/openapi/component/getComponentInfos", body);
        if (fout is not null) return "Aanvraag mislukt: " + fout;

        var tekst = antwoord ?? "";
        try
        {
            using var doc = JsonDocument.Parse(tekst);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data) ||
                data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return "Antwoord zonder gegevens: " + tekst[..Math.Min(400, tekst.Length)];

            var aantal = data.TryGetProperty("componentInfos", out var lijst) &&
                         lijst.ValueKind == JsonValueKind.Array ? lijst.GetArrayLength() : 0;
            var lastKey = data.TryGetProperty("lastKey", out var k) ? k.ToString() : "(geen)";
            var eerste = aantal > 0 ? lijst[0].ToString() : "(leeg)";

            return $"{aantal} onderdelen in deze pagina · vervolgsleutel: {lastKey}\n\n" +
                   "Eerste onderdeel:\n" + eerste[..Math.Min(800, eerste.Length)];
        }
        catch (Exception ex)
        {
            return "Antwoord niet te lezen (" + ex.Message + "): " + tekst[..Math.Min(400, tekst.Length)];
        }
    }

    /// <summary>Haalt de detailgegevens van één C-nummer op: voorraad,
    /// prijsstaffels, datasheet — voor de popup in de BOM-kijker.</summary>
    public async Task<(JlcDetail? Detail, string? Fout)> DetailAsync(string code)
    {
        if (!jlc.Beschikbaar) return (null, "Geen JLCPCB-sleutels ingesteld.");

        var body = JsonSerializer.Serialize(new { componentCodes = new[] { code } });
        var (antwoord, fout) = await jlc.PostAsync(
            "/overseas/openapi/component/getComponentDetailByCode", body);
        if (fout is not null) return (null, fout);

        try
        {
            using var doc = JsonDocument.Parse(antwoord ?? "");
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            {
                var melding = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                return (null, melding ?? "Geen gegevens voor dit C-nummer.");
            }

            var d = data[0];
            static string S(JsonElement e, string naam)
                => e.TryGetProperty(naam, out var v)
                    ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()
                    : "";

            var prijzen = new List<JlcPrijs>();
            if (d.TryGetProperty("priceRanges", out var reeks) &&
                reeks.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in reeks.EnumerateArray())
                {
                    var vanaf = p.TryGetProperty("startQuantity", out var q) &&
                                q.TryGetInt32(out var qa) ? qa : 1;
                    if (p.TryGetProperty("unitPrice", out var up) &&
                        up.TryGetDecimal(out var prijs))
                        prijzen.Add(new JlcPrijs(vanaf, prijs));
                }
                prijzen.Sort((a, b) => a.Vanaf.CompareTo(b.Vanaf));
            }

            var categorie = S(d, "firstTypeName");
            var tweede = S(d, "secondTypeName");
            if (tweede.Length > 0) categorie += (categorie.Length > 0 ? " › " : "") + tweede;

            return (new JlcDetail(
                S(d, "componentCode"),
                S(d, "componentModel"),
                S(d, "componentBrandEn"),
                S(d, "componentSpecification"),
                S(d, "description"),
                categorie,
                S(d, "stockCount"),
                prijzen,
                S(d, "datasheetUrl"),
                S(d, "libraryType")), null);
        }
        catch (Exception ex)
        {
            return (null, "Antwoord niet te lezen: " + ex.Message);
        }
    }
}
