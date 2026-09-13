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

    /// <summary>
    /// Haalt de hele bibliotheek op en slaat hem op; bestaande C-nummers
    /// worden bijgewerkt. Geeft het aantal verwerkte componenten terug,
    /// plus een foutmelding als het onderweg strandde (wat al verwerkt is,
    /// blijft dan gewoon staan).
    /// </summary>
    public async Task<(int Totaal, string? Fout)> SyncAsync(IProgress<string>? voortgang = null)
    {
        if (!Beschikbaar)
            return (0, "JLCPCB-sleutels of database ontbreken.");

        string? lastKey = null;
        var totaal = 0;

        // Ruime bovengrens: 10.000 pagina's van 1.000 = 10 miljoen componenten.
        for (var ronde = 0; ronde < 10_000; ronde++)
        {
            var body = JsonSerializer.Serialize(new { lastKey, pageSize = 1000 });
            var (antwoord, fout) = await jlc.PostAsync(
                "/overseas/openapi/component/getComponentLibraryList", body);
            if (fout is not null) return (totaal, fout);

            var tekst = antwoord ?? "";
            using var doc = JsonDocument.Parse(tekst);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data) ||
                data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                var melding = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                return (totaal, "JLCPCB meldt: " + (melding ?? tekst[..Math.Min(200, tekst.Length)]));
            }

            var lading = new List<JlcComponent>();
            if (data.TryGetProperty("componentLibraryInfoVOS", out var lijst) &&
                lijst.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in lijst.EnumerateArray())
                {
                    static string S(JsonElement e, string naam)
                        => e.TryGetProperty(naam, out var v) && v.ValueKind == JsonValueKind.String
                            ? v.GetString() ?? "" : "";

                    var code = S(c, "componentCode");
                    if (code.Length == 0) continue;
                    var spec = S(c, "componentSpecification");
                    lading.Add(new JlcComponent(code, S(c, "componentModel"),
                                                spec.Length == 0 ? null : spec));
                }
            }

            if (lading.Count == 0) break;   // klaar: lege pagina

            await repo.BewaarBatchAsync(lading);
            totaal += lading.Count;
            voortgang?.Report($"{totaal:N0} componenten…");

            var nieuweKey = data.TryGetProperty("lastKey", out var k) &&
                            k.ValueKind == JsonValueKind.String ? k.GetString() : null;
            if (string.IsNullOrEmpty(nieuweKey) || nieuweKey == lastKey) break;
            lastKey = nieuweKey;
        }

        return (totaal, null);
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
