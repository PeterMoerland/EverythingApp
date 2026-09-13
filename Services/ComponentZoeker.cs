using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EverythingApp.Services;

/// <summary>Eén prijsstaffel: de stukprijs vanaf dit aantal.</summary>
public record PrijsStaffel(int Aantal, decimal Stukprijs);

/// <summary>Eén zoekresultaat uit de onderdelendatabase.</summary>
public record OnderdeelTref(
    string Mpn,
    string Fabrikant,
    string Omschrijving,
    string Categorie,
    string Voorraad,
    string Prijs,
    string Datasheet,
    string Pagina,
    IReadOnlyList<PrijsStaffel> Staffels,
    string Valuta);

/// <summary>
/// Zoekt een MPN (manufacturer part number) op in de onderdelendatabase van
/// Mouser, om de gegevens uit een Fusion-BOM te controleren. Gebruikt de
/// gratis Mouser Search API: maak een account op mouser.com, vraag onder
/// 'Services → Zoek-API' een sleutel aan en zet die in appsettings als
/// Mouser:Sleutel (of netter: omgevingsvariabele Mouser__Sleutel).
/// </summary>
public class ComponentZoeker(IConfiguration config)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private string Sleutel => config["Mouser:Sleutel"] ?? "";
    public bool Beschikbaar => Sleutel.Length > 0;

    /// <summary>Antwoorden onthouden zolang de app draait: de BOM-controle
    /// vraagt dezelfde MPN anders bij elke run opnieuw op, en de daglimiet
    /// van de gratis sleutel is eindig.</summary>
    private readonly ConcurrentDictionary<string, (List<OnderdeelTref>, string?)> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Zoekt op MPN; geeft de treffers of een leesbare foutmelding.</summary>
    public async Task<(List<OnderdeelTref> Treffers, string? Fout)> ZoekAsync(string mpn)
    {
        if (_cache.TryGetValue(mpn, out var bekend)) return bekend;

        var vers = await ZoekEchtAsync(mpn);

        // Alleen echte antwoorden onthouden; een transportfout kan zo weer weg zijn.
        if (vers.Fout is null || vers.Fout.StartsWith("Geen treffers"))
            _cache[mpn] = vers;
        return vers;
    }

    private async Task<(List<OnderdeelTref> Treffers, string? Fout)> ZoekEchtAsync(string mpn)
    {
        if (!Beschikbaar)
            return (new(), "Er is nog geen Mouser-sleutel ingesteld (Mouser:Sleutel in appsettings; " +
                           "gratis aan te vragen op mouser.com onder Services → Search API).");

        try
        {
            var adres = "https://api.mouser.com/api/v1/search/keyword?apiKey=" +
                        Uri.EscapeDataString(Sleutel);
            var antwoord = await Http.PostAsJsonAsync(adres, new
            {
                SearchByKeywordRequest = new { keyword = mpn, records = 8, startingRecord = 0 }
            });
            var tekst = await antwoord.Content.ReadAsStringAsync();
            if (!antwoord.IsSuccessStatusCode)
                return (new(), $"Mouser antwoordt met {(int)antwoord.StatusCode}.");

            using var doc = JsonDocument.Parse(tekst);

            if (doc.RootElement.TryGetProperty("Errors", out var fouten) &&
                fouten.ValueKind == JsonValueKind.Array && fouten.GetArrayLength() > 0)
            {
                var eerste = fouten[0];
                var melding = eerste.TryGetProperty("Message", out var m) ? m.GetString() : null;
                return (new(), "Mouser meldt: " + (melding ?? fouten[0].ToString()));
            }

            var treffers = new List<OnderdeelTref>();
            if (doc.RootElement.TryGetProperty("SearchResults", out var res) &&
                res.ValueKind == JsonValueKind.Object &&
                res.TryGetProperty("Parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    static string S(JsonElement e, string naam)
                        => e.TryGetProperty(naam, out var v) && v.ValueKind == JsonValueKind.String
                            ? v.GetString() ?? "" : "";

                    // Alle prijsstaffels, plus de eerste als leesbare indicatie
                    var prijs = "";
                    var valuta = "";
                    var lijst = new List<PrijsStaffel>();
                    if (part.TryGetProperty("PriceBreaks", out var staffels) &&
                        staffels.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in staffels.EnumerateArray())
                        {
                            var aantal = p.TryGetProperty("Quantity", out var q) &&
                                         q.TryGetInt32(out var qa) ? qa : 1;
                            var bedrag = PrijsGetal(S(p, "Price"));
                            if (bedrag is null) continue;
                            lijst.Add(new PrijsStaffel(aantal, bedrag.Value));
                            if (valuta.Length == 0) valuta = S(p, "Currency");
                        }
                        lijst.Sort((a, b) => a.Aantal.CompareTo(b.Aantal));
                        if (staffels.GetArrayLength() > 0)
                        {
                            var eerste = staffels[0];
                            prijs = $"{S(eerste, "Price")} per {(eerste.TryGetProperty("Quantity", out var q) ? q.ToString() : "1")}";
                        }
                    }

                    treffers.Add(new OnderdeelTref(
                        S(part, "ManufacturerPartNumber"),
                        S(part, "Manufacturer"),
                        S(part, "Description"),
                        S(part, "Category"),
                        S(part, "Availability"),
                        prijs,
                        S(part, "DataSheetUrl"),
                        S(part, "ProductDetailUrl"),
                        lijst,
                        valuta));
                }
            }

            return (treffers, treffers.Count == 0 ? "Geen treffers voor deze MPN." : null);
        }
        catch (Exception ex)
        {
            return (new(), "Zoeken mislukt: " + ex.Message);
        }
    }

    /// <summary>
    /// De stukprijs bij dit aantal: de hoogste staffel die je haalt, of de
    /// laagste staffel als het aantal daar nog onder ligt. Null = geen prijs.
    /// </summary>
    public static decimal? PrijsVoor(OnderdeelTref tref, int aantal)
    {
        if (tref.Staffels.Count == 0) return null;
        var beste = tref.Staffels[0];
        foreach (var s in tref.Staffels)
            if (s.Aantal <= aantal) beste = s;
        return beste.Stukprijs;
    }

    /// <summary>
    /// Mouser geeft prijzen als tekst in de landinstelling van de sleutel
    /// ("0,092 €" of "$0.10"); dit haalt daar het kale bedrag uit.
    /// </summary>
    private static decimal? PrijsGetal(string tekst)
    {
        var sb = new StringBuilder();
        foreach (var c in tekst)
            if (char.IsDigit(c) || c is ',' or '.') sb.Append(c);
        var s = sb.ToString();
        if (s.Length == 0) return null;

        var komma = s.LastIndexOf(',');
        var punt = s.LastIndexOf('.');
        if (komma >= 0 && punt >= 0)
            s = komma > punt ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (komma >= 0)
            s = s.Replace(',', '.');

        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d : null;
    }
}
