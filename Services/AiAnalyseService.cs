using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EverythingApp.Services;

/// <summary>Seintje aan open schermen: een vraag is veranderd (in de wachtrij, bezig, klaar).</summary>
public class AiSignaal
{
    public event Action<int>? Gewijzigd;
    public void Geef(int vraagId) => Gewijzigd?.Invoke(vraagId);
}

/// <summary>
/// Vragen aan het taalmodel (Amazon Bedrock) over projecten, uren, bestellingen, voorraad, producten en (met recht
/// Financial/Directie, niet op een werkplek) de financiën, met een wachtrij in de tabel AiVragen.
///   - 'direct': meteen, laag Bedrock:LaagDirect (standaard 'default').
///   - 'wachten': alleen in de daluren (Bedrock:DalurenVan/-Tot, en het weekend als Bedrock:WeekendDal), laag
///     Bedrock:LaagWachten (standaard 'flex': goedkoper, mag langer duren). Klaar = melding bij de bel.
/// Het model krijgt de vraag plus de opvragingen uit <see cref="FinancieleAiTools"/> en vraagt zelf de cijfers op die het
/// nodig heeft (tool use); alles wat het opvroeg wordt bij het antwoord bewaard.
/// </summary>
public class AiAnalyseService : BackgroundService
{
    private readonly IAiVraagRepository _repo;
    private readonly BedrockClient _bedrock;
    private readonly FinancieleAiTools _tools;
    private readonly AlgemeneAiTools _algemeen;
    private readonly IMeldingRepository _meldingen;
    private readonly MeldingSignaal _meldingSignaal;
    private readonly AiSignaal _signaal;
    private readonly IConfiguration _config;
    private readonly ILogger<AiAnalyseService> _log;

    private readonly SemaphoreSlim _wekker = new(0);
    private readonly SemaphoreSlim _tegelijk = new(3, 3);
    private readonly ConcurrentDictionary<int, byte> _lopend = new();

    public AiAnalyseService(IAiVraagRepository repo, BedrockClient bedrock, FinancieleAiTools tools, AlgemeneAiTools algemeen, IMeldingRepository meldingen,
                            MeldingSignaal meldingSignaal, AiSignaal signaal, IConfiguration config, ILogger<AiAnalyseService> log)
    {
        _repo = repo; _bedrock = bedrock; _tools = tools; _algemeen = algemeen; _meldingen = meldingen; _meldingSignaal = meldingSignaal;
        _signaal = signaal; _config = config; _log = log;
    }

    public bool Ingesteld => _bedrock.Ingesteld;
    public string Ontbreekt => _bedrock.Ontbreekt;
    public string Model => _bedrock.Model;

    private string LaagDirect => (_config["Bedrock:LaagDirect"] ?? "").Trim() is { Length: > 0 } l ? l : "default";
    private string LaagWachten => (_config["Bedrock:LaagWachten"] ?? "").Trim() is { Length: > 0 } l ? l : "flex";
    private int MaxTokens => Math.Clamp(_config.GetValue<int?>("Bedrock:MaxTokens") ?? 4000, 500, 16000);
    private const int MaxRondes = 8;

    // ---- daluren ----
    private TimeOnly DalVan => TimeOnly.TryParse(_config["Bedrock:DalurenVan"], out var t) ? t : new TimeOnly(18, 0);
    private TimeOnly DalTot => TimeOnly.TryParse(_config["Bedrock:DalurenTot"], out var t) ? t : new TimeOnly(7, 0);
    private bool WeekendDal => _config.GetValue<bool?>("Bedrock:WeekendDal") ?? true;

    public bool IsDaluren(DateTime nu)
    {
        if (WeekendDal && nu.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return true;
        var t = TimeOnly.FromDateTime(nu);
        return DalVan <= DalTot ? t >= DalVan && t < DalTot : t >= DalVan || t < DalTot;   // over middernacht heen
    }

    public string DalurenTekst => $"tussen {DalVan:HH\\:mm} en {DalTot:HH\\:mm}" + (WeekendDal ? " en in het weekend" : "");

    // ---- vragen ----
    /// <param name="financieel">Mag de vraagsteller financiële gegevens zien (Financial/Directie en niet op een werkplek)?</param>
    /// <param name="gesprekId">Doorvragen: de eerste vraag van het gesprek; het model krijgt het hele gesprek mee.</param>
    public async Task<int> StelAsync(int werknemerId, string vraag, string prioriteit, bool financieel, int? gesprekId = null)
    {
        var id = await _repo.MaakAsync(werknemerId, vraag.Trim(), prioriteit == AiVraag.Wachten ? AiVraag.Wachten : AiVraag.Direct, financieel, gesprekId);
        _signaal.Geef(id);
        _wekker.Release();
        return id;
    }

    public async Task<bool> AnnuleerAsync(int id, int werknemerId)
    {
        var ok = await _repo.AnnuleerAsync(id, werknemerId);
        if (ok) _signaal.Geef(id);
        return ok;
    }

    /// <summary>Een wachtende vraag toch meteen uitvoeren (tegen het tarief van 'direct').</summary>
    public async Task NuUitvoerenAsync(AiVraag v)
    {
        if (v.Status != AiVraag.Wacht || !await _repo.ClaimAsync(v.Id)) return;
        _signaal.Geef(v.Id);
        _ = Task.Run(() => VoerUitAsync(v, LaagDirect, CancellationToken.None));
    }

    // ---- wachtrij ----
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(10), stop); } catch (OperationCanceledException) { return; }
        try
        {
            var hersteld = await _repo.HerstelHangendAsync(TimeSpan.FromMinutes(30));
            if (hersteld > 0) _log.LogInformation("{Aantal} AI-vragen hingen op 'bezig' en staan weer in de wachtrij.", hersteld);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Herstellen van hangende AI-vragen mislukt."); }

        while (!stop.IsCancellationRequested)
        {
            try { await Ronde(stop); }
            catch (Exception ex) { _log.LogWarning(ex, "Wachtrij AI-vragen: ronde mislukt."); }
            try { await _wekker.WaitAsync(TimeSpan.FromMinutes(1), stop); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task Ronde(CancellationToken stop)
    {
        if (!_bedrock.Ingesteld || !_repo.Beschikbaar || !await _repo.TabelAanwezigAsync()) return;
        var dal = IsDaluren(DateTime.Now);
        // direct eerst, dan de wachtende (alleen in de daluren)
        foreach (var v in (await _repo.WachtendAsync()).OrderBy(v => v.Prioriteit == AiVraag.Direct ? 0 : 1).ThenBy(v => v.Id))
        {
            if (v.Prioriteit == AiVraag.Wachten && !dal) continue;
            if (_lopend.ContainsKey(v.Id)) continue;
            if (!await _tegelijk.WaitAsync(0, stop)) break;                    // vol: volgende ronde
            if (!await _repo.ClaimAsync(v.Id)) { _tegelijk.Release(); continue; }
            _lopend[v.Id] = 0;
            _signaal.Geef(v.Id);
            var laag = v.Prioriteit == AiVraag.Wachten ? LaagWachten : LaagDirect;
            _ = Task.Run(async () =>
            {
                try { await VoerUitAsync(v, laag, stop); }
                finally { _lopend.TryRemove(v.Id, out _); _tegelijk.Release(); _wekker.Release(); }
            });
        }
    }

    // ---- uitvoeren ----
    private string Systeem(bool financieel) =>
        $$"""
        Je bent de assistent van MDE Automation, een machinebouwer met meerdere eigen BV's. Vandaag is {{DateTime.Today:yyyy-MM-dd}} ({{DateTime.Today:dddd}}).
        Je beantwoordt vragen over projecten, werkorders, uren, bestellingen, voorraad en producten{{(financieel ? ", en over de financiën" : "")}}, in het Nederlands.
        {{(financieel ? "" : "Financiële gegevens (omzet, kosten, facturen, bank, liquiditeit, kostprijzen) zijn voor deze gebruiker niet beschikbaar; zeg dat als ernaar gevraagd wordt.")}}
        Regels:
        - Haal elk getal op met de beschikbare opvragingen; verzin nooit cijfers. Kun je iets niet opvragen, zeg dat dan duidelijk.
        - Vraag gericht op (periode, klant, leverancier) in plaats van alles.
        - Noem bij elk getal waar het vandaan komt (welke opvraging en periode) en of het in- of exclusief btw is.
        - Gebruik de totalen uit de opmerking of de kolom Cumulatief van een opvraging; tel NOOIT zelf regels op. Heb je een
          totaal nodig dat er niet staat, vraag de gegevens dan opnieuw op met een indeling die dat totaal geeft.
        - Vergelijk je perioden (bv. dit jaar met vorig jaar), vraag dan beide met even lange datums op (1 jan t/m vandaag en
          dezelfde datums vorig jaar), zodat de totalen vergelijkbaar zijn. Een lopende maand is nog niet compleet: zeg dat.
        - Schrijf bedragen als € 12.345,67.
        - Begin met een korte conclusie (een of twee zinnen). Daarna zo nodig een tabel in markdown.
        - Je kunt niets wijzigen in de administratie. Breng aannames en onzekerheden eerlijk.
        - In een gesprek mag je voortbouwen op eerdere antwoorden en gegevens; heb je andere cijfers nodig (andere periode,
          andere indeling, of gegevens die zijn weggelaten), vraag ze dan opnieuw op.
        - Vraagt de gebruiker om een grafiek, of maakt een grafiek het antwoord duidelijker, zet dan in je antwoord een blok:
          ```grafiek
          {"type": "staaf", "titel": "Omzet per maand", "eenheid": "€", "labels": ["jan", "feb"], "reeksen": [{"naam": "2026", "waarden": [613756.39, 228644.98]}]}
          ```
          type is "staaf" (vergelijken) of "lijn" (verloop in de tijd); hoogstens 4 reeksen; eenheid "€", "%" of leeg.
          Neem de waarden letterlijk over uit de opgevraagde gegevens: getallen met een punt, zonder € en zonder duizendtallen.
          Het scherm tekent de grafiek zelf en controleert de waarden; beschrijf de grafiek niet in tekst na.
        """;

    private async Task VoerUitAsync(AiVraag v, string laag, CancellationToken stop)
    {
        var gegevens = new List<AiGegevens>();
        int tin = 0, tuit = 0;
        var gebruikteLaag = laag;
        var gezieneLaag = laag;
        var berichten = await Voorgeschiedenis(v);
        berichten.Add(new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["text"] = v.Vraag }) });
        var verzoek = new JsonObject
        {
            ["system"] = new JsonArray(new JsonObject { ["text"] = Systeem(v.Financieel) }),
            ["messages"] = berichten,
            ["inferenceConfig"] = new JsonObject { ["maxTokens"] = MaxTokens, ["temperature"] = 0.2 },
            ["toolConfig"] = ToolConfig(v.Financieel),
        };

        string Json() => gegevens.Count == 0 ? "" : JsonSerializer.Serialize(gegevens);

        try
        {
            for (var ronde = 0; ronde < MaxRondes; ronde++)
            {
                BedrockAntwoord a;
                try { a = await _bedrock.ConverseAsync(verzoek, gebruikteLaag, stop); }
                catch (BedrockFout f) when (f.Status == 400 && gebruikteLaag != "default"
                                            && f.Message.Contains("tier", StringComparison.OrdinalIgnoreCase))
                {
                    // dit model kent de gevraagde laag (bv. flex) niet: dan gewoon standaard
                    _log.LogInformation("Laag {Laag} niet beschikbaar voor {Model}; standaard gebruikt.", gebruikteLaag, _bedrock.Model);
                    gebruikteLaag = "default";
                    a = await _bedrock.ConverseAsync(verzoek, gebruikteLaag, stop);
                }
                tin += a.TokensIn; tuit += a.TokensUit; gezieneLaag = a.Laag;
                berichten.Add(new JsonObject { ["role"] = "assistant", ["content"] = a.Inhoud.DeepClone() });

                var opvragingen = a.Inhoud.OfType<JsonObject>().Select(b => b["toolUse"] as JsonObject).Where(t => t is not null).ToList();
                if (a.StopReden == "tool_use" && opvragingen.Count > 0)
                {
                    var resultaten = new JsonArray();
                    foreach (var t in opvragingen)
                    {
                        var naam = t!["name"]?.GetValue<string>() ?? "";
                        var invoer = t["input"] as JsonObject ?? new JsonObject();
                        var g = _algemeen.Kent(naam)
                            ? await _algemeen.VoerUitAsync(naam, invoer, v.WerknemerId, v.Financieel)
                            : v.Financieel
                                ? await _tools.VoerUitAsync(naam, invoer, v.WerknemerId)
                                : new AiGegevens { Naam = naam, Titel = naam, Opmerking = "Geen toegang: alleen met het recht Financial of Directie." };
                        gegevens.Add(g);
                        resultaten.Add(new JsonObject
                        {
                            ["toolResult"] = new JsonObject
                            {
                                ["toolUseId"] = t["toolUseId"]?.GetValue<string>() ?? "",
                                ["content"] = new JsonArray(new JsonObject { ["json"] = FinancieleAiTools.AlsJson(g) }),
                                ["status"] = g.Opmerking.StartsWith("Ophalen mislukt") ? "error" : "success",
                            },
                        });
                    }
                    berichten.Add(new JsonObject { ["role"] = "user", ["content"] = resultaten });
                    continue;
                }

                var tekst = string.Join("\n", a.Inhoud.OfType<JsonObject>()
                                                   .Select(b => b["text"]?.GetValue<string>()).Where(s => !string.IsNullOrWhiteSpace(s)));
                if (a.StopReden == "max_tokens") tekst += "\n\n*(Het antwoord is afgebroken: de maximale lengte is bereikt.)*";
                if (tekst.Trim().Length == 0) tekst = "*(Het model gaf geen tekst terug.)*";
                try { await _repo.BewaarBerichtenAsync(v.Id, berichten.ToJsonString()); }      // voor een volgende vraag in dit gesprek
                catch (Exception ex) { _log.LogWarning(ex, "Gesprek van AI-vraag {Id} niet bewaard.", v.Id); }
                await _repo.KlaarAsync(v.Id, tekst, Json(), _bedrock.Model, gezieneLaag, tin, tuit, Kosten(gezieneLaag, tin, tuit));
                await Afgerond(v, true);
                return;
            }
            await _repo.FoutAsync(v.Id, $"Na {MaxRondes} rondes opvragen nog geen antwoord. Stel de vraag specifieker.",
                                  Json(), _bedrock.Model, gezieneLaag, tin, tuit, Kosten(gezieneLaag, tin, tuit));
            await Afgerond(v, false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AI-vraag {Id} mislukt.", v.Id);
            try
            {
                await _repo.FoutAsync(v.Id, ex.Message, Json(), _bedrock.Model, gezieneLaag, tin, tuit, Kosten(gezieneLaag, tin, tuit));
                await Afgerond(v, false);
            }
            catch (Exception ex2) { _log.LogWarning(ex2, "AI-vraag {Id}: fout kon niet worden opgeslagen.", v.Id); }
        }
    }

    /// <summary>
    /// Bij een vervolgvraag: het gesprek tot nu toe, zoals het naar het model ging (met de opgevraagde gegevens), uit de
    /// laatste beantwoorde vraag van het gesprek. Ontbreekt dat, dan alleen de vragen en antwoorden als tekst.
    /// </summary>
    private async Task<JsonArray> Voorgeschiedenis(AiVraag v)
    {
        var uit = new JsonArray();
        if (v.GesprekId is not int gid || gid == v.Id) return uit;
        var eerder = (await _repo.GesprekAsync(gid)).Where(x => x.Id < v.Id && x.Status == AiVraag.KlaarStatus).ToList();
        if (eerder.Count == 0) return uit;
        try
        {
            if (JsonNode.Parse(await _repo.BerichtenAsync(eerder[^1].Id)) is JsonArray vorige && vorige.Count > 0)
            {
                Inkorten(vorige);
                foreach (var m in vorige) uit.Add(m!.DeepClone());
                return uit;
            }
        }
        catch { /* dan als tekst */ }
        foreach (var x in eerder)
        {
            uit.Add(new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["text"] = x.Vraag }) });
            uit.Add(new JsonObject { ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["text"] = x.Antwoord }) });
        }
        return uit;
    }

    /// <summary>
    /// Lange gesprekken: de gegevens (toolResult) van oudere beurten vervangen door een korte notitie; het model vraagt ze
    /// zo nodig opnieuw op. De laatste 6 berichten blijven heel.
    /// </summary>
    private static void Inkorten(JsonArray berichten)
    {
        if (berichten.ToJsonString().Length <= 120_000) return;
        for (var i = 0; i < berichten.Count - 6; i++)
            if (berichten[i]?["content"] is JsonArray inhoud)
                foreach (var blok in inhoud.OfType<JsonObject>())
                    if (blok["toolResult"] is JsonObject tr)
                        tr["content"] = new JsonArray(new JsonObject { ["text"] = "(gegevens van een eerdere beurt weggelaten; vraag ze opnieuw op als je ze nodig hebt)" });
    }

    /// <summary>De opvragingen voor deze vraag: altijd de algemene, de financiële alleen met recht.</summary>
    private JsonObject ToolConfig(bool financieel)
    {
        var tools = new JsonArray(_algemeen.Tools(financieel).Select(t => (JsonNode)t).ToArray());
        if (financieel && _tools.ToolConfig()["tools"] is JsonArray fin)
            foreach (var t in fin.ToList()) tools.Add(t!.DeepClone());
        return new JsonObject { ["tools"] = tools };
    }

    /// <summary>Schatting in euro's met de prijzen per miljoen tokens uit appsettings (0 = onbekend).</summary>
    private decimal? Kosten(string laag, int tin, int tuit)
    {
        var flex = laag == "flex";
        var pin = _config.GetValue<decimal?>(flex ? "Bedrock:PrijsInvoerFlex" : "Bedrock:PrijsInvoer") ?? 0;
        var puit = _config.GetValue<decimal?>(flex ? "Bedrock:PrijsUitvoerFlex" : "Bedrock:PrijsUitvoer") ?? 0;
        if (pin == 0 && puit == 0) return null;
        return Math.Round(tin / 1_000_000m * pin + tuit / 1_000_000m * puit, 4);
    }

    private async Task Afgerond(AiVraag v, bool gelukt)
    {
        _signaal.Geef(v.Id);
        if (v.Prioriteit != AiVraag.Wachten) return;     // bij 'direct' kijkt de vraagsteller mee
        try
        {
            var kort = v.Vraag.Length > 70 ? v.Vraag[..70] + "…" : v.Vraag;
            // Meldingen.OrderID is hier het nummer van de vraag (soort 'analyse'); de bel linkt naar /financien/analyse
            await _meldingen.MaakAsync(v.WerknemerId, "analyse", v.Id, (gelukt ? "Je vraag is beantwoord: " : "Je vraag is mislukt: ") + kort);
            _meldingSignaal.Geef();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Melding voor AI-vraag {Id} niet gemaakt.", v.Id); }
    }
}
