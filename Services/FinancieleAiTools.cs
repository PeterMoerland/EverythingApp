using System.Globalization;
using System.Text.Json.Nodes;
using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// De gegevens die het taalmodel mag opvragen bij een financiële vraag ("tools" in de Converse-API).
/// Alleen lezen, altijd begrensd, en zoveel mogelijk via de bestaande repositories, zodat de regels gelijk zijn aan
/// de schermen (geen proforma's in de omzet, banktransacties alleen van rekeningen waar de vraagsteller bij mag, ...).
/// Elke opvraging wordt een <see cref="AiGegevens"/>-tabel: die gaat naar het model én wordt bij het antwoord bewaard,
/// zodat de gebruiker kan narekenen. Getallen in de tabellen staan met een punt (2 decimalen), zodat het model kan rekenen.
/// </summary>
public class FinancieleAiTools
{
    private readonly ILiquiditeitRepository _liq;
    private readonly IVasteLastenRepository _vaste;
    private readonly IVerkoopFactuurRepository _verkoop;
    private readonly IFactuurRepository _inkoop;
    private readonly IBankRepository _bank;
    private readonly string? _cs;

    public const int MaxRijen = 200;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public FinancieleAiTools(ILiquiditeitRepository liq, IVasteLastenRepository vaste, IVerkoopFactuurRepository verkoop,
                             IFactuurRepository inkoop, IBankRepository bank, IConfiguration config)
    {
        _liq = liq; _vaste = vaste; _verkoop = verkoop; _inkoop = inkoop; _bank = bank;
        _cs = config.GetConnectionString("MDE");
    }

    // ================================================================
    //  Beschrijving voor het model (toolConfig)
    // ================================================================
    private static JsonObject Tool(string naam, string beschrijving, JsonObject eigenschappen, params string[] verplicht)
        => new()
        {
            ["toolSpec"] = new JsonObject
            {
                ["name"] = naam,
                ["description"] = beschrijving,
                ["inputSchema"] = new JsonObject
                {
                    ["json"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = eigenschappen,
                        ["required"] = new JsonArray(verplicht.Select(v => (JsonNode)v).ToArray()),
                    },
                },
            },
        };

    private static JsonObject P(string type, string beschrijving) => new() { ["type"] = type, ["description"] = beschrijving };
    private static JsonObject Periode(JsonObject extra)
    {
        extra["van"] = P("string", "Begindatum, formaat jjjj-mm-dd (inclusief). Standaard 1 januari van dit jaar.");
        extra["tot"] = P("string", "Einddatum, formaat jjjj-mm-dd (inclusief). Standaard vandaag.");
        return extra;
    }

    public JsonObject ToolConfig() => new()
    {
        ["tools"] = new JsonArray(
            Tool("liquiditeit_prognose",
                 "Liquiditeitsprognose van de MDE-BV's vanaf het laatste banksaldo: per week wat erbij komt (klanten), eraf gaat " +
                 "(leveranciers, vaste lasten, btw) en het saldo. Te late posten staan apart in de opmerking.",
                 new JsonObject { ["dagen"] = P("integer", "Hoeveel dagen vooruit, 7 t/m 180. Standaard 60.") }),
            Tool("verkoopfacturen",
                 "Uitgaande facturen (verkoop) in een periode, op factuurdatum. Bedragen in euro's. Zonder proforma's. " +
                 "Creditnota's hebben een negatief bedrag. Maximaal 200 regels; de opmerking geeft de totalen van de hele selectie.",
                 Periode(new JsonObject
                 {
                     ["klant"] = P("string", "Deel van de klantnaam (optioneel)."),
                     ["alleen_open"] = P("boolean", "Alleen nog niet betaalde facturen (optioneel)."),
                 })),
            Tool("inkoopfacturen",
                 "Inkomende facturen (inkoop) in een periode, op factuurdatum. Bedragen in euro's. Maximaal 200 regels; de " +
                 "opmerking geeft de totalen van de hele selectie.",
                 Periode(new JsonObject
                 {
                     ["leverancier"] = P("string", "Deel van de naam van de leverancier (optioneel)."),
                     ["alleen_open"] = P("boolean", "Alleen nog niet betaalde facturen (optioneel)."),
                 })),
            Tool("omzet_per_maand",
                 "Gefactureerde omzet exclusief btw per maand (op factuurdatum, zonder proforma's), eventueel per klant en per BV. " +
                 "Standaard zonder facturen aan eigen BV's (interne doorbelasting).",
                 Periode(new JsonObject
                 {
                     ["per_klant"] = P("boolean", "Ook uitsplitsen per klant (optioneel)."),
                     ["met_intern"] = P("boolean", "Ook facturen aan eigen BV's meetellen (optioneel, standaard niet)."),
                     ["klant"] = P("string", "Alleen klanten waarvan de naam dit bevat (optioneel)."),
                     ["bv"] = P("string", "Afkorting van de eigen BV (optioneel)."),
                 })),
            Tool("kosten_per_maand",
                 "Inkoopkosten exclusief btw per maand (inkomende facturen, op factuurdatum), eventueel per leverancier en per BV. " +
                 "Standaard zonder facturen van eigen BV's (interne doorbelasting).",
                 Periode(new JsonObject
                 {
                     ["per_leverancier"] = P("boolean", "Ook uitsplitsen per leverancier (optioneel)."),
                     ["met_intern"] = P("boolean", "Ook facturen van eigen BV's meetellen (optioneel, standaard niet)."),
                     ["leverancier"] = P("string", "Alleen leveranciers waarvan de naam dit bevat (optioneel)."),
                     ["bv"] = P("string", "Afkorting van de eigen BV (optioneel)."),
                 })),
            Tool("banktransacties",
                 "Banktransacties in een periode, van de rekeningen waar de vraagsteller bij mag. Bedrag positief = bij, negatief = af. " +
                 "Maximaal 200 regels; de opmerking geeft het totaal en het aantal van de hele selectie.",
                 Periode(new JsonObject
                 {
                     ["zoek"] = P("string", "Zoekterm in tegenpartij of omschrijving (optioneel)."),
                     ["richting"] = P("string", "'bij', 'af' of 'beide' (standaard)."),
                 })),
            Tool("vaste_lasten",
                 "De vaste lasten (huur, leasing, verzekeringen, ...): bedrag per keer, interval en totaal per jaar.",
                 new JsonObject())),
    };

    // ================================================================
    //  Uitvoeren
    // ================================================================
    public async Task<AiGegevens> VoerUitAsync(string naam, JsonObject invoer, int werknemerId)
    {
        var g = new AiGegevens { Naam = naam, Invoer = invoer.ToJsonString() };
        try
        {
            switch (naam)
            {
                case "liquiditeit_prognose": await Liquiditeit(g, invoer); break;
                case "verkoopfacturen": await Verkoop(g, invoer); break;
                case "inkoopfacturen": await Inkoop(g, invoer); break;
                case "omzet_per_maand": await OmzetPerMaand(g, invoer); break;
                case "kosten_per_maand": await KostenPerMaand(g, invoer); break;
                case "banktransacties": await Bank(g, invoer, werknemerId); break;
                case "vaste_lasten": await VasteLasten(g); break;
                default: g.Titel = naam; g.Opmerking = "Onbekende opvraging."; break;
            }
        }
        catch (Exception ex)
        {
            g.Titel = g.Titel.Length > 0 ? g.Titel : naam;
            g.Opmerking = "Ophalen mislukt: " + ex.Message;
        }
        return g;
    }

    /// <summary>Wat het model terugkrijgt: kolommen, rijen en de opmerking.</summary>
    public static JsonObject AlsJson(AiGegevens g) => new()
    {
        ["titel"] = g.Titel,
        ["kolommen"] = new JsonArray(g.Kolommen.Select(k => (JsonNode)k).ToArray()),
        ["rijen"] = new JsonArray(g.Rijen.Select(r => (JsonNode)new JsonArray(r.Select(c => (JsonNode)c).ToArray())).ToArray()),
        ["opmerking"] = g.Opmerking,
    };

    // ---- hulp ----
    private static string? Tekst(JsonObject i, string naam)
        => i[naam] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    private static bool Waar(JsonObject i, string naam)
        => i[naam] is JsonValue v && ((v.TryGetValue<bool>(out var b) && b) || (v.TryGetValue<string>(out var s) && s.Equals("true", StringComparison.OrdinalIgnoreCase)));

    private static int Getal(JsonObject i, string naam, int standaard)
        => i[naam] is JsonValue v && (v.TryGetValue<int>(out var n) || (v.TryGetValue<string>(out var s) && int.TryParse(s, out n))) ? n : standaard;

    private static (DateOnly Van, DateOnly Tot) LeesPeriode(JsonObject i)
    {
        var vandaag = DateOnly.FromDateTime(DateTime.Today);
        var van = DateOnly.TryParse(Tekst(i, "van"), Inv, out var a) ? a : new DateOnly(vandaag.Year, 1, 1);
        var tot = DateOnly.TryParse(Tekst(i, "tot"), Inv, out var b) ? b : vandaag;
        if (tot < van) (van, tot) = (tot, van);
        if (tot.DayNumber - van.DayNumber > 366 * 6) van = tot.AddYears(-6);       // begrenzen: hoogstens 6 jaar
        return (van, tot);
    }

    private static string B(decimal d) => d.ToString("0.00", Inv);
    private static string D(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "";
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static string JN(bool b) => b ? "ja" : "nee";

    private static void Afkappen(AiGegevens g, int totaal)
    {
        if (g.Rijen.Count > MaxRijen) g.Rijen = g.Rijen.Take(MaxRijen).ToList();
        if (totaal > g.Rijen.Count)
            g.Opmerking = (g.Opmerking + $" Afgekapt: {g.Rijen.Count} van {totaal} regels getoond.").Trim();
    }

    // ---- liquiditeit ----
    private async Task Liquiditeit(AiGegevens g, JsonObject i)
    {
        var dagen = Math.Clamp(Getal(i, "dagen", 60), 7, 180);
        var bvs = (await _liq.BvsAsync()).Where(b => b.MdeBv).Select(b => b.Id).ToList();
        var rekeningen = await _liq.BanksaldiAsync(bvs);
        var start = rekeningen.Where(r => r.Datum is not null).Select(r => r.Datum!.Value).DefaultIfEmpty(DateOnly.FromDateTime(DateTime.Today)).Max();
        var p = Liquiditeitsrekenaar.Bereken(start, dagen, rekeningen, await _liq.OpenstaandAsync(bvs), await _vaste.LijstAsync(),
                                             await _liq.BtwPerKwartaalAsync(bvs, start.Year - 1));
        g.Titel = $"Liquiditeitsprognose {dagen} dagen vanaf {D(p.Start)}";
        g.Kolommen = new() { "Week vanaf", "Erbij", "Eraf", "Saldo eind week", "Laagste saldo in week" };
        foreach (var week in p.Dagen.GroupBy(d => (d.Datum.DayNumber - p.Start.DayNumber) / 7))
        {
            var w = week.ToList();
            g.Rijen.Add(new() { D(w[0].Datum), B(w.Sum(d => d.Erbij)), B(w.Sum(d => d.Eraf)), B(w[^1].Saldo), B(w.Min(d => d.Saldo)) });
        }
        g.Opmerking = $"Beginsaldo {B(p.Beginsaldo)} op {D(p.Start)} (laatste banksaldo). Al te laat van klanten: {B(p.TeLaatVanKlanten)} " +
                      $"({p.TeLaatKlanten.Count} facturen, niet in de weken meegeteld). Al te laat aan leveranciers: {B(p.TeLaatAanLeveranciers)} " +
                      $"({p.TeLaatLeveranciers.Count} facturen). Bedragen inclusief btw.";
    }

    // ---- verkoopfacturen ----
    private async Task Verkoop(AiGegevens g, JsonObject i)
    {
        var (van, tot) = LeesPeriode(i);
        var filter = new VerkoopFactuurFilter
        {
            Van = van, Tot = tot, Klant = Tekst(i, "klant") ?? "",
            Betaald = Waar(i, "alleen_open") ? Drieluik.Nee : Drieluik.Alles,
        };
        var lijst = (await _verkoop.ZoekAsync(filter, 5000)).Where(f => !f.IsProforma).ToList();
        g.Titel = $"Verkoopfacturen {D(van)} t/m {D(tot)}" + (filter.Klant.Length > 0 ? $", klant '{filter.Klant}'" : "") + (Waar(i, "alleen_open") ? ", alleen open" : "");
        g.Kolommen = new() { "Nummer", "Datum", "Klant", "BV", "Ex btw", "Incl btw", "Verstuurd", "Betaald", "Dagen te laat", "Omschrijving" };
        foreach (var f in lijst)
            g.Rijen.Add(new() { f.Factuurnummer, D(f.Factuurdatum), f.Klant, f.Administratie, B(f.BedragExBtw), B(f.BedragInclBtw),
                                JN(f.Verstuurd), JN(f.Betaald), f.Betaald ? "" : f.Overdue.ToString(Inv), f.Omschrijving });
        g.Opmerking = $"Totaal {lijst.Count} facturen: ex btw {B(lijst.Sum(f => f.BedragExBtw))}, incl btw {B(lijst.Sum(f => f.BedragInclBtw))}; " +
                      $"open (niet betaald) incl btw {B(lijst.Where(f => !f.Betaald).Sum(f => f.BedragInclBtw))}.";
        if (lijst.Count >= 5000) g.Opmerking += " Let op: meer dan 5000 facturen, totalen onvolledig; kies een kortere periode.";
        Afkappen(g, lijst.Count);
    }

    // ---- inkoopfacturen ----
    private async Task Inkoop(AiGegevens g, JsonObject i)
    {
        var (van, tot) = LeesPeriode(i);
        var filter = new FactuurFilter
        {
            Van = van, Tot = tot, Bedrijf = Tekst(i, "leverancier") ?? "",
            Betaald = Waar(i, "alleen_open") ? Drieluik.Nee : Drieluik.Alles,
        };
        var lijst = await _inkoop.ZoekAsync(filter, 5000);
        g.Titel = $"Inkoopfacturen {D(van)} t/m {D(tot)}" + (filter.Bedrijf.Length > 0 ? $", leverancier '{filter.Bedrijf}'" : "") + (Waar(i, "alleen_open") ? ", alleen open" : "");
        g.Kolommen = new() { "Nummer", "Datum", "Leverancier", "BV", "Ex btw", "Btw", "Totaal", "Betaald", "Dagen te laat", "Omschrijving" };
        foreach (var f in lijst)
            g.Rijen.Add(new() { f.Factuurnummer, D(f.Factuurdatum), f.Bedrijf, f.Administratie, B(f.BedragExBtw), B(f.Btw), B(f.Totaal),
                                JN(f.Betaald), f.Betaald ? "" : f.Overdue.ToString(Inv), f.Omschrijving });
        g.Opmerking = $"Totaal {lijst.Count} facturen: ex btw {B(lijst.Sum(f => f.BedragExBtw))}, incl btw {B(lijst.Sum(f => f.Totaal))}; " +
                      $"open incl btw {B(lijst.Where(f => !f.Betaald).Sum(f => f.Totaal))}.";
        if (lijst.Count >= 5000) g.Opmerking += " Let op: meer dan 5000 facturen, totalen onvolledig; kies een kortere periode.";
        Afkappen(g, lijst.Count);
    }

    // ---- omzet en kosten per maand (eigen query: nergens anders gegroepeerd) ----
    private async Task OmzetPerMaand(AiGegevens g, JsonObject i)
    {
        var (van, tot) = LeesPeriode(i);
        var perKlant = Waar(i, "per_klant");
        var intern = Waar(i, "met_intern");
        var klant = Tekst(i, "klant");
        var bv = Tekst(i, "bv");
        var sql =
            "SELECT DATE_FORMAT(f.`Date invoice`, '%Y-%m') AS maand, " + (perKlant ? "COALESCE(k.`name`, '?')" : "''") + " AS klant, " +
            "       COALESCE(b.`Afkorting`, '') AS bv, SUM(f.`Total ex`), COUNT(*) " +
            "  FROM `Invoices` f " +
            "  LEFT JOIN `Relations` k ON k.`id_supplier` = f.`Customer-id` " +
            "  LEFT JOIN `Relations` b ON b.`id_supplier` = f.`Werkmaatschappij` " +
            " WHERE f.`Date invoice` >= @van AND f.`Date invoice` < @tot " +
            "   AND COALESCE(f.`Betalingsherinnering`, 0) <> 4 " +
            (intern ? "" : " AND COALESCE(k.`Werkmaatschappij`, 0) = 0 AND COALESCE(k.`MDEBV`, 0) = 0 ") +
            (klant is null ? "" : " AND k.`name` LIKE @klant ") +
            (bv is null ? "" : " AND b.`Afkorting` LIKE @bv ") +
            " GROUP BY maand, klant, bv ORDER BY maand, SUM(f.`Total ex`) DESC LIMIT 1000";
        g.Titel = $"Omzet ex btw per maand {D(van)} t/m {D(tot)}" + (intern ? ", inclusief eigen BV's" : "") + (perKlant ? ", per klant" : "") + (klant is null ? "" : $", klant '{klant}'") + (bv is null ? "" : $", BV {bv}");
        g.Kolommen = perKlant ? new() { "Maand", "Klant", "BV", "Omzet ex btw", "Facturen" } : new() { "Maand", "Omzet ex btw", "Facturen", "Cumulatief" };
        var totaal = await Groepeer(g, sql.Replace("AS bv,", "AS bv_,").Replace("GROUP BY maand, klant, bv", perKlant ? "GROUP BY maand, klant, bv_" : "GROUP BY maand"),
                                    van, tot, klant, bv, perKlant);
        g.Opmerking += $"Totaal omzet ex btw over de hele opgevraagde periode ({D(van)} t/m {D(tot)}): {B(totaal)}. Zonder proforma's; creditnota's tellen negatief mee. " +
                      (intern ? "Inclusief facturen aan eigen BV's." : "Zonder facturen aan eigen BV's (interne doorbelasting).");
    }

    private async Task KostenPerMaand(AiGegevens g, JsonObject i)
    {
        var (van, tot) = LeesPeriode(i);
        var perLev = Waar(i, "per_leverancier");
        var intern = Waar(i, "met_intern");
        var lev = Tekst(i, "leverancier");
        var bv = Tekst(i, "bv");
        var sql =
            "SELECT DATE_FORMAT(f.`DatumFactuur`, '%Y-%m') AS maand, " + (perLev ? "COALESCE(l.`name`, '?')" : "''") + " AS lev, " +
            "       COALESCE(b.`Afkorting`, '') AS bv, SUM(f.`BedragExBTW`), COUNT(*) " +
            "  FROM `FacturenInkomend` f " +
            "  LEFT JOIN `Relations` l ON l.`id_supplier` = f.`Bedrijf` " +
            "  LEFT JOIN `Relations` b ON b.`id_supplier` = f.`Admin` " +
            " WHERE f.`DatumFactuur` >= @van AND f.`DatumFactuur` < @tot " +
            (intern ? "" : " AND COALESCE(l.`Werkmaatschappij`, 0) = 0 AND COALESCE(l.`MDEBV`, 0) = 0 ") +
            (lev is null ? "" : " AND l.`name` LIKE @klant ") +
            (bv is null ? "" : " AND b.`Afkorting` LIKE @bv ") +
            " GROUP BY maand, lev, bv ORDER BY maand, SUM(f.`BedragExBTW`) DESC LIMIT 1000";
        g.Titel = $"Inkoopkosten ex btw per maand {D(van)} t/m {D(tot)}" + (intern ? ", inclusief eigen BV's" : "") + (perLev ? ", per leverancier" : "") + (lev is null ? "" : $", leverancier '{lev}'") + (bv is null ? "" : $", BV {bv}");
        g.Kolommen = perLev ? new() { "Maand", "Leverancier", "BV", "Kosten ex btw", "Facturen" } : new() { "Maand", "Kosten ex btw", "Facturen", "Cumulatief" };
        var totaal = await Groepeer(g, sql.Replace("AS bv,", "AS bv_,").Replace("GROUP BY maand, lev, bv", perLev ? "GROUP BY maand, lev, bv_" : "GROUP BY maand"),
                                    van, tot, lev, bv, perLev);
        g.Opmerking += $"Totaal inkoopkosten ex btw over de hele opgevraagde periode ({D(van)} t/m {D(tot)}): {B(totaal)}. Alleen wat als inkomende factuur is ingevoerd (geen salarissen of bankkosten). " +
                      (intern ? "Inclusief facturen van eigen BV's." : "Zonder facturen van eigen BV's (interne doorbelasting).");
    }

    private async Task<decimal> Groepeer(AiGegevens g, string sql, DateOnly van, DateOnly tot, string? naam, string? bv, bool metNaam)
    {
        if (string.IsNullOrEmpty(_cs)) throw new InvalidOperationException("Geen database.");
        await using var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@van", van.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tot", tot.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (naam is not null) cmd.Parameters.AddWithValue("@klant", "%" + naam + "%");
        if (bv is not null) cmd.Parameters.AddWithValue("@bv", bv + "%");
        decimal totaal = 0;
        var aantal = 0;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            aantal++;
            var bedrag = r.IsDBNull(3) ? 0 : r.GetDecimal(3);
            totaal += bedrag;
            var maand = r.IsDBNull(0) ? "?" : r.GetString(0);
            var bvTekst = r.IsDBNull(2) ? "" : r.GetString(2);
            var n = Convert.ToInt32(r.GetValue(4)).ToString(Inv);
            // zonder uitsplitsing: per maand met het lopende totaal, zodat "januari t/m oktober" niet uitgerekend hoeft te worden
            g.Rijen.Add(metNaam ? new() { maand, r.IsDBNull(1) ? "" : r.GetString(1), bvTekst, B(bedrag), n }
                                : new() { maand, B(bedrag), n, B(totaal) });
        }
        Afkappen(g, aantal);
        var vandaag = DateOnly.FromDateTime(DateTime.Today);
        if (tot >= vandaag && tot.Year == vandaag.Year && tot.Month == vandaag.Month && vandaag.Day < DateTime.DaysInMonth(vandaag.Year, vandaag.Month))
            g.Opmerking = $"Let op: {vandaag:yyyy-MM} is nog niet compleet (t/m {D(vandaag)}); vergelijk die maand niet met een hele maand. ";
        else
            g.Opmerking = "";
        return totaal;
    }

    // ---- bank ----
    private async Task Bank(AiGegevens g, JsonObject i, int werknemerId)
    {
        var (van, tot) = LeesPeriode(i);
        var zoek = Tekst(i, "zoek");
        var richting = (Tekst(i, "richting") ?? "beide").ToLowerInvariant() switch { "bij" => BijAf.Bij, "af" => BijAf.Af, _ => BijAf.Beide };
        var rekeningen = (await _bank.GetRekeningenAsync(werknemerId)).Select(r => r.Nummer).ToList();
        g.Titel = $"Banktransacties {D(van)} t/m {D(tot)}" + (zoek is null ? "" : $", zoek '{zoek}'") + (richting == BijAf.Beide ? "" : $", alleen {richting.ToString().ToLowerInvariant()}");
        g.Kolommen = new() { "Datum", "Rekening", "Bedrag", "Tegenpartij", "Omschrijving" };
        if (rekeningen.Count == 0) { g.Opmerking = "De vraagsteller heeft geen rechten op bankrekeningen."; return; }
        var lijst = await _bank.ZoekAsync(rekeningen, van, tot, zoek, richting, null, MaxRijen);
        var tot_ = await _bank.GetTotalenAsync(rekeningen, rekeningen, van, tot, zoek, richting, null);
        foreach (var t in lijst)
            g.Rijen.Add(new() { D(t.Datum), t.Rekening, B(t.Bedrag), t.Tenaamstelling, t.Omschrijving.Length > 160 ? t.Omschrijving[..160] : t.Omschrijving });
        g.Opmerking = $"Totaal van de hele selectie: {tot_.Aantal} transacties, saldo bij min af {B(tot_.Totaal)}.";
        Afkappen(g, tot_.Aantal);
    }

    // ---- vaste lasten ----
    private async Task VasteLasten(AiGegevens g)
    {
        var lijst = await _vaste.LijstAsync();
        g.Titel = "Vaste lasten";
        g.Kolommen = new() { "Omschrijving", "Categorie", "Bedrag per keer", "Interval", "Per jaar" };
        foreach (var v in lijst.OrderByDescending(v => v.TotaalPerJaar))
            g.Rijen.Add(new() { v.Omschrijving, v.Categorie, B(v.Bedrag), v.Interval.ToString(), B(v.TotaalPerJaar) });
        g.Opmerking = $"Totaal per jaar: {B(lijst.Sum(v => v.TotaalPerJaar))}. Niet per BV uitgesplitst.";
        Afkappen(g, lijst.Count);
    }
}
