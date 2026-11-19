using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// API voor het Activity board (punt van Gerard, 19-11-2026): een programma buiten de browser, in de praktijk Claude Code
/// op een pc binnen het MDE-netwerk, kan boards lezen en activiteiten aanmaken of wijzigen. De afspraken voor de
/// gebruiker staan in ActivityBoard-API.md.
///
/// - Geen Google-login (er is geen browser) maar een sleutel per persoon in de kop <c>X-Sleutel</c>
///   (config <c>ActivityBoardApi:Sleutels</c>: Sleutel + WerknemerId). Geen sleutels ingesteld = de API bestaat niet (404).
///   Alles wat een sleutel doet, staat op naam van die werknemer (initiator, GewijzigdDoor, melding in de bel).
/// - Dezelfde laag als het scherm (<see cref="IActivityBoardRepository"/>) en dezelfde regels: sluitdatum bij een
///   klaar-status, een nieuwe activiteit onderaan haar vak, een melding voor wie wordt toegewezen, en
///   <see cref="ActivityBoardLive"/> zodat een open board bij een collega meteen ververst.
/// - Bewust niet: verwijderen (activiteit, onderwerp, type, board), boards maken of koppelen, en slepen (volgorde).
/// </summary>
public static class ActivityBoardApi
{
    /// <summary>Een kortere sleutel telt niet mee: te raden.</summary>
    public const int MinSleutelLengte = 32;
    private const int MaxPerKeer = 50, MaxTekst = 20000, MaxTitel = 300, MaxZoek = 100;
    private const StringComparison Los = StringComparison.OrdinalIgnoreCase;

    /// <summary>"Bron" voor <see cref="ActivityBoardLive"/>: geen scherm heeft deze sleutel, dus elk open board ververst.</summary>
    private static readonly Guid Bron = Guid.NewGuid();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] Velden =
        new[] { "titel", "status", "prioriteit", "vervaldatum", "benodigdeTijd", "onderwerpen", "types", "personen" }
            .Concat(ActTekst.Velden.Select(v => Klein(v.Kolom))).ToArray();
    private static readonly HashSet<string> Bekend =
        new(Velden.Concat(new[] { "benodigdeUren", "tekst" }), StringComparer.OrdinalIgnoreCase);

    private static string Klein(string kolom) => char.ToLowerInvariant(kolom[0]) + kolom[1..];

    // ------------------------------------------------------------------ adressen

    public static void MapActivityBoardApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/activityboard");

        // Uitleg: wie je bent volgens de sleutel, en welke waarden en adressen er zijn
        api.MapGet("/", Route(ctx => Met(ctx, v => Task.FromResult(Goed(new
        {
            ik = new { id = v.Wie.Id, naam = v.Wie.Naam },
            statussen = ActStatus.Alle.Select(s => new { code = s.Code, naam = s.Naam, groep = s.Groep }),
            prioriteiten = ActPrioriteit.Alle.Select(p => new { waarde = p.Waarde, naam = p.Naam }),
            tekstvelden = ActTekst.Velden.Select(t => new { veld = Klein(t.Kolom), naam = t.Naam, uitleg = t.Hulp }),
            kleuren = ActKleur.Alle,
            velden = Velden,
            adressen = new[]
            {
                "GET   /api/activityboard/boards?zoek=tekst",
                "GET   /api/activityboard/boards/{boardId}?open=true&mijn=true&status=code&tekst=true",
                "GET   /api/activityboard/mijn?persoon=naam",
                "GET   /api/activityboard/zoek?q=tekst&open=true&board=id&overal=true",
                "GET   /api/activityboard/personen",
                "GET   /api/activityboard/activiteiten/{id}",
                "POST  /api/activityboard/boards/{boardId}/activiteiten   (één object of een lijst, hoogstens " + MaxPerKeer + ")",
                "PATCH /api/activityboard/activiteiten/{id}                (alleen de velden die je meegeeft veranderen)",
                "POST  /api/activityboard/boards/{boardId}/onderwerpen    { naam, kleur? }",
                "POST  /api/activityboard/boards/{boardId}/types          { naam, kleur? }",
            },
            nietMogelijk = "Verwijderen, boards maken of koppelen en de volgorde binnen een vak: dat doe je in de app.",
        })))));

        api.MapGet("/personen", Route(ctx => Met(ctx, v => Task.FromResult(Goed(new
        {
            personen = v.Personen.Where(p => p.Kiesbaar).Select(p => new { id = p.Id, naam = p.Naam }),
        })))));

        api.MapGet("/boards", (HttpContext ctx, string? zoek) => Met(ctx, async v =>
        {
            var woorden = (zoek ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var boards = (await v.Repo.GetBoardsAsync())
                .Where(b => woorden.All(w => $"{b.Titel} {b.ProjectNummer} {b.ProjectNaam} {b.RelatieNaam} {b.KlantNaam} {b.WerkorderNummer} {b.WerkorderNaam}".Contains(w, Los)))
                .OrderByDescending(b => b.Gewijzigd)
                .Select(b => new
                {
                    id = b.BoardId, titel = b.Titel, soort = b.Soort, klant = Leeg(b.KlantNaam), open = b.Open, klaar = b.Klaar,
                    gewijzigd = b.Gewijzigd?.ToString("yyyy-MM-dd HH:mm"), link = v.Link(b.BoardId),
                });
            return Goed(new { boards });
        }));

        api.MapGet("/boards/{boardId:int}", (HttpContext ctx, int boardId, bool? open, bool? mijn, string? status, bool? tekst) => Met(ctx, async v =>
        {
            var d = await v.Repo.LaadAsync(boardId);
            if (d is null) return Fout(404, $"Board {boardId} bestaat niet.");
            string? code = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                code = ZoekStatus(status)?.Code;
                if (code is null) return Fout(400, StatusFout(status));
            }
            var volgorde = ActStatus.Alle.Select((s, i) => (s.Code, i)).ToDictionary(x => x.Code, x => x.i);
            var lijst = d.Activiteiten
                .Where(a => open != true || ActStatus.Groep(a.Status) != ActStatus.GroepKlaar)
                .Where(a => mijn != true || a.Personen.Contains(v.Wie.Id))
                .Where(a => code is null || a.Status == code)
                .OrderBy(a => volgorde.GetValueOrDefault(a.Status)).ThenBy(a => a.Volgorde)
                .Select(a => Uit(a, d, v, tekst == true));
            return Goed(new
            {
                board = new { id = d.Board.Id, titel = d.Board.Titel, soort = d.Board.Soort, link = v.Link(d.Board.Id) },
                onderwerpen = d.Problemen.Select(p => new { id = p.Id, naam = p.Naam, prioriteit = p.Positie, kleur = p.Kleur }),
                types = d.Types.Select(t => new { id = t.Id, naam = t.Naam, kleur = t.Kleur }),
                activiteiten = lijst,
            });
        }));

        // Wat staat er open voor mij (of voor ?persoon=), over alle boards
        api.MapGet("/mijn", (HttpContext ctx, string? persoon) => Met(ctx, async v =>
        {
            var wie = v.Wie.Id;
            if (!string.IsNullOrWhiteSpace(persoon))
            {
                var fout = ZoekPersoon(JsonSerializer.SerializeToElement(persoon), v, null, out wie);
                if (fout is not null) return Fout(400, fout);
            }
            var uit = new List<object>();
            foreach (var b in (await v.Repo.GetBoardsAsync()).Where(b => b.Open > 0))
            {
                var d = await v.Repo.LaadAsync(b.BoardId);
                if (d is null) continue;
                uit.AddRange(d.Activiteiten
                    .Where(a => a.Personen.Contains(wie) && ActStatus.Groep(a.Status) != ActStatus.GroepKlaar)
                    .OrderBy(a => a.Prioriteit ?? 9).ThenBy(a => a.Vervaldatum ?? DateTime.MaxValue)
                    .Select(a => Uit(a, d, v, metTekst: false, metBoard: true)));
            }
            return Goed(new { persoon = new { id = wie, naam = v.Naam(wie) }, activiteiten = uit });
        }));

        // Activiteiten zoeken op titel (met ?overal=true ook in de tekstvelden), over alle boards of één (?board=12).
        // Laadt elk board in zijn geheel: prima zolang er tientallen boards zijn; bij honderden hoort hier een eigen query.
        api.MapGet("/zoek", (HttpContext ctx, string? q, int? board, bool? open, bool? overal) => Met(ctx, async v =>
        {
            var woorden = (q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if ((q ?? "").Trim().Length < 2) return Fout(400, "Geef een zoektekst van minstens twee tekens mee: ?q=kabelgoot.");
            var ids = board is int een ? new List<int> { een } : (await v.Repo.GetBoardsAsync()).Select(b => b.BoardId).ToList();
            var uit = new List<object>();
            var meer = false;
            foreach (var boardId in ids)
            {
                var d = await v.Repo.LaadAsync(boardId);
                if (d is null) { if (board is not null) return Fout(404, $"Board {boardId} bestaat niet."); continue; }
                foreach (var a in d.Activiteiten.OrderByDescending(a => a.GemaaktOp))
                {
                    if (open == true && ActStatus.Groep(a.Status) == ActStatus.GroepKlaar) continue;
                    var waarin = overal == true ? a.Titel + " " + string.Join(" ", a.Tekst.Values) : a.Titel;
                    if (!woorden.All(w => waarin.Contains(w, Los))) continue;
                    if (uit.Count >= MaxZoek) { meer = true; break; }
                    uit.Add(Uit(a, d, v, metTekst: false, metBoard: true));
                }
                if (meer) break;
            }
            return Goed(new { activiteiten = uit, meer = meer ? $"Er zijn meer dan {MaxZoek} treffers; zoek preciezer." : null });
        }));

        api.MapGet("/activiteiten/{id:int}", (HttpContext ctx, int id) => Met(ctx, async v =>
        {
            var (d, a) = await Zoek(v, id);
            return d is null || a is null ? Fout(404, $"Activiteit {id} bestaat niet.") : Goed(new { activiteit = Uit(a, d, v, metTekst: true, metBoard: true) });
        }));

        api.MapPost("/boards/{boardId:int}/activiteiten", (HttpContext ctx, int boardId) => Met(ctx, async v =>
        {
            var body = await Body(ctx);
            if (body is not { } json) return Fout(400, "De inhoud is geen geldige JSON in UTF-8 (of is groter dan 1 MB).");
            var items = json.ValueKind == JsonValueKind.Array ? json.EnumerateArray().ToList() : new List<JsonElement> { json };
            if (items.Count == 0) return Fout(400, "Er staat niets in de lijst.");
            if (items.Count > MaxPerKeer) return Fout(400, $"Hoogstens {MaxPerKeer} activiteiten per keer.");

            var d = await v.Repo.LaadAsync(boardId);
            if (d is null) return Fout(404, $"Board {boardId} bestaat niet.");
            var metTijd = await v.Repo.TijdAanwezigAsync();

            // eerst alles nalopen, dan pas schrijven: een fout in nummer 7 laat geen zes losse kaartjes achter
            var klaar = new List<Wijziging>();
            for (var i = 0; i < items.Count; i++)
            {
                var fout = Lees(items[i], d, v, null, metTijd, out var w);
                if (fout is not null) return Fout(400, items.Count > 1 ? $"Activiteit {i + 1}: {fout}" : fout);
                klaar.Add(w);
            }

            var idx = d.Problemen.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
            var gemaakt = new List<Activiteit>();
            foreach (var w in klaar)
            {
                var a = new Activiteit
                {
                    Titel = w.Titel!, Status = w.Status ?? ActStatus.Backlog, Prioriteit = w.Prio, Vervaldatum = w.Datum,
                    BenodigdeUren = w.Uren, GemaaktOp = DateTime.Now, GemaaktDoor = v.Wie.Id,
                    Problemen = w.Onderwerpen ?? new(), Types = w.Types ?? new(), Personen = w.Personen ?? new(),
                    Tekst = w.Tekst.Where(t => t.Value.Length > 0).ToDictionary(t => t.Key, t => t.Value),
                };
                if (ActStatus.Groep(a.Status) == ActStatus.GroepKlaar) a.GeslotenOp = a.GemaaktOp;
                var baan = Baan(a, idx);
                a.Volgorde = d.Activiteiten.Where(x => x.Status == a.Status && Baan(x, idx) == baan)
                                           .Select(x => x.Volgorde).DefaultIfEmpty(-1).Max() + 1;      // onderaan het vak
                a.Id = await v.Repo.NieuweActiviteitAsync(boardId, a, v.Wie.Id);
                d.Activiteiten.Add(a);
                gemaakt.Add(a);
            }

            v.Log($"{gemaakt.Count} activiteit(en) aangemaakt op board {boardId}: {string.Join(", ", gemaakt.Select(a => a.Id))}");
            v.Ververs(boardId);
            foreach (var a in gemaakt) await MeldToewijzing(v, d, a, a.Personen, nieuw: true);
            return Results.Json(new { aangemaakt = gemaakt.Select(a => Uit(a, d, v, metTekst: true)) }, Json, statusCode: 201);
        }));

        api.MapMethods("/activiteiten/{id:int}", new[] { "PATCH" }, (HttpContext ctx, int id) => Met(ctx, async v =>
        {
            var body = await Body(ctx);
            if (body is not { } json) return Fout(400, "De inhoud is geen geldige JSON in UTF-8 (of is groter dan 1 MB).");
            var (d, a) = await Zoek(v, id);
            if (d is null || a is null) return Fout(404, $"Activiteit {id} bestaat niet.");
            var fout = Lees(json, d, v, a, await v.Repo.TijdAanwezigAsync(), out var w);
            if (fout is not null) return Fout(400, fout);

            var door = v.Wie.Id;
            var gewijzigd = new List<string>();
            if (w.Titel is { } titel && titel != a.Titel)
            { await v.Repo.ZetVeldAsync(id, "Titel", titel, door); a.Titel = titel; gewijzigd.Add("titel"); }
            if (w.HeeftPrio && w.Prio != a.Prioriteit)
            { await v.Repo.ZetVeldAsync(id, "Prioriteit", w.Prio, door); a.Prioriteit = w.Prio; gewijzigd.Add("prioriteit"); }
            if (w.HeeftDatum && w.Datum != a.Vervaldatum)
            { await v.Repo.ZetVeldAsync(id, "Vervaldatum", w.Datum, door); a.Vervaldatum = w.Datum; gewijzigd.Add("vervaldatum"); }
            if (w.HeeftUren && w.Uren != a.BenodigdeUren)
            { await v.Repo.ZetVeldAsync(id, "BenodigdeUren", w.Uren, door); a.BenodigdeUren = w.Uren; gewijzigd.Add("benodigdeTijd"); }
            foreach (var (kolom, tekst) in w.Tekst)
            {
                if (tekst == a.Tekst.GetValueOrDefault(kolom, "")) continue;
                await v.Repo.ZetVeldAsync(id, kolom, tekst, door);
                if (tekst.Length > 0) a.Tekst[kolom] = tekst; else a.Tekst.Remove(kolom);
                gewijzigd.Add(Klein(kolom));
            }
            if (w.Status is { } status && status != a.Status)
            {
                // zelfde regel als het scherm: naar een klaar-status = nu gesloten, terug naar open = sluitdatum weg
                var naar = ActStatus.Groep(status);
                if (naar == ActStatus.GroepKlaar && ActStatus.Groep(a.Status) != ActStatus.GroepKlaar) a.GeslotenOp = DateTime.Now;
                if (naar != ActStatus.GroepKlaar) a.GeslotenOp = null;
                await v.Repo.ZetStatusAsync(id, status, a.GeslotenOp, door);
                a.Status = status; gewijzigd.Add("status");
            }
            if (w.Onderwerpen is { } onderwerpen && !onderwerpen.ToHashSet().SetEquals(a.Problemen))
            { await v.Repo.ZetKoppelingenAsync(id, "problemen", onderwerpen, door); a.Problemen = onderwerpen; gewijzigd.Add("onderwerpen"); }
            if (w.Types is { } types && !types.ToHashSet().SetEquals(a.Types))
            { await v.Repo.ZetKoppelingenAsync(id, "types", types, door); a.Types = types; gewijzigd.Add("types"); }
            var erbij = new List<int>();
            if (w.Personen is { } personen && !personen.ToHashSet().SetEquals(a.Personen))
            {
                erbij = personen.Except(a.Personen).ToList();
                await v.Repo.ZetKoppelingenAsync(id, "personen", personen, door); a.Personen = personen; gewijzigd.Add("personen");
            }

            if (gewijzigd.Count > 0)
            {
                v.Log($"activiteit {id} op board {d.Board.Id} gewijzigd: {string.Join(", ", gewijzigd)}");
                v.Ververs(d.Board.Id);
                await MeldToewijzing(v, d, a, erbij, nieuw: false);
            }
            return Goed(new { gewijzigd, activiteit = Uit(a, d, v, metTekst: true, metBoard: true) });
        }));

        api.MapPost("/boards/{boardId:int}/onderwerpen", (HttpContext ctx, int boardId) => NieuwLabel(ctx, boardId, onderwerp: true));
        api.MapPost("/boards/{boardId:int}/types", (HttpContext ctx, int boardId) => NieuwLabel(ctx, boardId, onderwerp: false));
    }

    /// <summary>Onderwerp of type toevoegen; bestaat de naam al op dit board, dan komt die terug (bestond = true).</summary>
    private static Task<IResult> NieuwLabel(HttpContext ctx, int boardId, bool onderwerp) => Met(ctx, async v =>
    {
        var wat = onderwerp ? "onderwerp" : "type";
        var body = await Body(ctx);
        if (body is not { ValueKind: JsonValueKind.Object } json) return Fout(400, "Verwacht een JSON-object: { \"naam\": \"…\" }.");
        foreach (var p in json.EnumerateObject())
            if (!p.Name.Equals("naam", Los) && !p.Name.Equals("kleur", Los)) return Fout(400, $"Onbekend veld '{p.Name}'. Bekende velden: naam, kleur.");
        var naam = Heeft(json, "naam", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim().ReplaceLineEndings(" ") : "";
        var max = onderwerp ? 200 : 100;
        if (naam.Length == 0) return Fout(400, "naam ontbreekt.");
        if (naam.Length > max) return Fout(400, $"naam is langer dan {max} tekens.");
        string? kleur = null;
        if (Heeft(json, "kleur", out var k) && k.ValueKind != JsonValueKind.Null)
        {
            kleur = k.ValueKind == JsonValueKind.String ? k.GetString()!.Trim().ToLowerInvariant() : "";
            if (!ActKleur.Alle.Contains(kleur)) return Fout(400, $"kleur moet een van deze zijn: {string.Join(", ", ActKleur.Alle)}.");
        }

        var d = await v.Repo.LaadAsync(boardId);
        if (d is null) return Fout(404, $"Board {boardId} bestaat niet.");
        if (onderwerp)
        {
            if (d.Problemen.FirstOrDefault(p => p.Naam.Equals(naam, Los)) is { } er)
                return Goed(new { bestond = true, onderwerp = new { id = er.Id, naam = er.Naam, prioriteit = er.Positie, kleur = er.Kleur } });
            kleur ??= ActKleur.Volgende(ActKleur.Problemen, d.Problemen.Select(p => p.Kleur), d.Problemen.Count);
            var id = await v.Repo.VoegProbleemToeAsync(boardId, naam, kleur);
            v.Log($"onderwerp {id} toegevoegd aan board {boardId}");
            v.Ververs(boardId);
            return Results.Json(new { bestond = false, onderwerp = new { id, naam, prioriteit = d.Problemen.Count + 1, kleur } }, Json, statusCode: 201);
        }
        else
        {
            if (d.Types.FirstOrDefault(t => t.Naam.Equals(naam, Los)) is { } er)
                return Goed(new { bestond = true, type = new { id = er.Id, naam = er.Naam, kleur = er.Kleur } });
            kleur ??= ActKleur.Volgende(ActKleur.Types, d.Types.Select(t => t.Kleur), d.Types.Count);
            var id = await v.Repo.VoegTypeToeAsync(boardId, naam, kleur);
            v.Log($"type {id} toegevoegd aan board {boardId}");
            v.Ververs(boardId);
            return Results.Json(new { bestond = false, type = new { id, naam, kleur } }, Json, statusCode: 201);
        }
    });

    /// <summary>
    /// Een adres met alleen HttpContext: als Delegate doorgeven. Als losse lambda kiest C# de RequestDelegate-vorm van
    /// MapGet, en die gooit het antwoord (Task&lt;IResult&gt;) weg: de aanroeper krijgt dan een lege 200 (ASP0016).
    /// </summary>
    private static Delegate Route(Func<HttpContext, Task<IResult>> werk) => werk;

    // ------------------------------------------------------------------ toegang

    private sealed record Beller(int Id, string Naam);

    /// <summary>Alles van één verzoek: wie het doet en de diensten van de app.</summary>
    private sealed class Verzoek
    {
        public required HttpContext Http { get; init; }
        public required IActivityBoardRepository Repo { get; init; }
        public required Beller Wie { get; init; }
        public required List<ActPersoon> Personen { get; init; }
        public required Dictionary<int, ActPersoon> OpId { get; init; }

        public string Naam(int id) => OpId.TryGetValue(id, out var p) ? p.Naam : $"werknemer {id}";
        public T Dienst<T>() where T : notnull => Http.RequestServices.GetRequiredService<T>();
        public string Link(int boardId, int? kaart = null)
            => $"{Http.Request.Scheme}://{Http.Request.Host}{Http.Request.PathBase}/activityboard/{boardId}" + (kaart is int k ? $"?kaart={k}" : "");
        /// <summary>Open boards bij collega's laden opnieuw.</summary>
        public void Ververs(int boardId) => Dienst<ActivityBoardLive>().Meld(boardId, Bron, Wie.Naam);
        public void Log(string wat)
            => Dienst<ILoggerFactory>().CreateLogger("ActivityBoardApi").LogInformation("Activity board API, {Wie} ({Id}): {Wat}", Wie.Naam, Wie.Id, wat);
    }

    /// <summary>Controleert de sleutel en voert het werk uit; een fout in de app wordt een nette 500 zonder details.</summary>
    private static async Task<IResult> Met(HttpContext ctx, Func<Verzoek, Task<IResult>> werk)
    {
        var repo = ctx.RequestServices.GetRequiredService<IActivityBoardRepository>();
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var sleutels = config.GetSection("ActivityBoardApi:Sleutels").GetChildren()
            .Select(s => (Sleutel: (s["Sleutel"] ?? "").Trim(), Werknemer: int.TryParse(s["WerknemerId"], out var w) ? w : 0))
            .Where(s => s.Sleutel.Length >= MinSleutelLengte && s.Werknemer > 0)
            .ToList();
        if (sleutels.Count == 0 || !repo.Beschikbaar) return Results.NotFound();      // API uit: het adres bestaat niet

        var gegeven = ctx.Request.Headers["X-Sleutel"].FirstOrDefault()?.Trim() ?? "";
        if (gegeven.Length == 0 && ctx.Request.Headers.Authorization.FirstOrDefault() is { } auth && auth.StartsWith("Bearer ", Los))
            gegeven = auth[7..].Trim();
        // op hash vergelijken, in vaste tijd en zonder vroege uitgang: de tijd verraadt niets over de sleutel
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(gegeven));
        int? werknemer = null;
        foreach (var s in sleutels)
            if (CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(Encoding.UTF8.GetBytes(s.Sleutel)))) werknemer ??= s.Werknemer;
        if (werknemer is null)
        {
            await Task.Delay(400);      // raden vertragen
            return Fout(401, "Onbekende of ontbrekende sleutel. Stuur je sleutel mee in de kop X-Sleutel.");
        }

        try
        {
            if (!await repo.TabellenAanwezigAsync()) return Fout(503, "Het Activity board is in deze app nog niet ingericht.");
            var personen = await repo.GetPersonenAsync();
            var opId = personen.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
            if (!opId.TryGetValue(werknemer.Value, out var ik) || !ik.Kiesbaar)
                return Fout(403, "De werknemer van deze sleutel bestaat niet of is uit dienst.");
            return await werk(new Verzoek { Http = ctx, Repo = repo, Wie = new Beller(ik.Id, ik.Naam), Personen = personen, OpId = opId });
        }
        catch (Exception ex)
        {
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ActivityBoardApi")
               .LogError(ex, "Activity board API: {Methode} {Pad} mislukt", ctx.Request.Method, ctx.Request.Path);
            return Fout(500, "Er ging iets mis in de app. De oorzaak staat in het logboek van de app.");
        }
    }

    private static IResult Goed(object inhoud) => Results.Json(inhoud, Json);
    private static IResult Fout(int code, string tekst) => Results.Json(new { fout = tekst }, Json, statusCode: code);
    private static string? Leeg(string s) => s.Length > 0 ? s : null;

    private const int MaxInhoud = 1_000_000;
    private static readonly UTF8Encoding StriktUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// De inhoud als JSON, of null als het geen geldige JSON in UTF-8 is (of te groot). Streng op UTF-8: de gewone lezer
    /// neemt verkeerde bytes aan en struikelt pas bij het uitlezen van een tekst, wat een 500 gaf in plaats van een 400
    /// (gevonden bij het proberen: curl en Windows PowerShell 5 sturen é zonder moeite als één byte).
    /// </summary>
    private static async Task<JsonElement?> Body(HttpContext ctx)
    {
        try
        {
            using var ms = new MemoryStream();
            var buf = new byte[16384];
            int n;
            while ((n = await ctx.Request.Body.ReadAsync(buf)) > 0)
            {
                if (ms.Length + n > MaxInhoud) return null;
                ms.Write(buf, 0, n);
            }
            var tekst = StriktUtf8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            if (tekst.Length > 0 && tekst[0] == '\uFEFF') tekst = tekst[1..];
            using var doc = JsonDocument.Parse(tekst);
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }

    private static bool Heeft(JsonElement o, string naam, out JsonElement waarde)
    {
        foreach (var p in o.EnumerateObject())
            if (p.Name.Equals(naam, Los)) { waarde = p.Value; return true; }
        waarde = default;
        return false;
    }

    private static async Task<(ActBoardData? Data, Activiteit? Act)> Zoek(Verzoek v, int activiteitId)
    {
        if (await v.Repo.BoardVanActiviteitAsync(activiteitId) is not int board) return (null, null);
        var d = await v.Repo.LaadAsync(board);
        return (d, d?.Activiteiten.FirstOrDefault(a => a.Id == activiteitId));
    }

    // ------------------------------------------------------------------ uitvoer

    private static Dictionary<string, object?> Uit(Activiteit a, ActBoardData d, Verzoek v, bool metTekst, bool metBoard = false)
    {
        var uit = new Dictionary<string, object?>
        {
            ["id"] = a.Id,
            ["titel"] = a.Titel,
            ["status"] = a.Status,
            ["statusNaam"] = ActStatus.Naam(a.Status),
            ["prioriteit"] = a.Prioriteit is int p ? ActPrioriteit.Naam(p).ToLowerInvariant() : null,
            ["vervaldatum"] = a.Vervaldatum?.ToString("yyyy-MM-dd"),
            ["benodigdeUren"] = a.BenodigdeUren,
            ["onderwerpen"] = d.Problemen.Where(x => a.Problemen.Contains(x.Id)).Select(x => new { id = x.Id, naam = x.Naam }).ToList(),
            ["types"] = d.Types.Where(x => a.Types.Contains(x.Id)).Select(x => new { id = x.Id, naam = x.Naam }).ToList(),
            ["personen"] = a.Personen.Select(x => new { id = x, naam = v.Naam(x) }).ToList(),
            ["initiator"] = a.GemaaktDoor is int g ? new { id = g, naam = v.Naam(g) } : null,
            ["gemaaktOp"] = a.GemaaktOp.ToString("yyyy-MM-dd HH:mm"),
            ["geslotenOp"] = a.GeslotenOp?.ToString("yyyy-MM-dd HH:mm"),
        };
        if (metBoard) uit["board"] = new { id = d.Board.Id, titel = d.Board.Titel };
        if (metTekst)
            foreach (var t in ActTekst.Velden)
                if (a.Tekst.TryGetValue(t.Kolom, out var tekst) && tekst.Length > 0) uit[Klein(t.Kolom)] = tekst;
        uit["link"] = v.Link(d.Board.Id, a.Id);
        // wat niet is ingevuld niet meesturen: de lezer is meestal een taalmodel en elke regel kost
        foreach (var leeg in uit.Where(x => x.Value is null).Select(x => x.Key).ToList()) uit.Remove(leeg);
        return uit;
    }

    // ------------------------------------------------------------------ invoer

    /// <summary>Wat er in een verzoek stond; alleen wat genoemd is verandert (PATCH) of wordt gezet (nieuw).</summary>
    private sealed class Wijziging
    {
        public string? Titel, Status;
        public bool HeeftPrio, HeeftDatum, HeeftUren;
        public int? Prio;
        public DateTime? Datum;
        public decimal? Uren;
        public List<int>? Onderwerpen, Types, Personen;
        /// <summary>Kolom → tekst; een lege tekst maakt het veld leeg.</summary>
        public Dictionary<string, string> Tekst = new();
    }

    private static ActStatus.Def? ZoekStatus(string tekst)
        => ActStatus.Alle.FirstOrDefault(s => s.Code.Equals(tekst.Trim(), Los) || s.Naam.Equals(tekst.Trim(), Los));
    private static string StatusFout(string tekst)
        => $"status '{tekst}' bestaat niet. Kies uit: {string.Join(", ", ActStatus.Alle.Select(s => $"{s.Code} ({s.Naam})"))}.";

    /// <summary>Leest één activiteit uit de JSON en controleert alles; geeft de fout in gewone taal, of null als het goed is.</summary>
    private static string? Lees(JsonElement o, ActBoardData d, Verzoek v, Activiteit? bestaand, bool metTijd, out Wijziging w)
    {
        w = new Wijziging();
        if (o.ValueKind != JsonValueKind.Object) return "Verwacht een JSON-object met de velden van de activiteit.";
        foreach (var p in o.EnumerateObject())
            if (!Bekend.Contains(p.Name)) return $"Onbekend veld '{p.Name}'. Bekende velden: {string.Join(", ", Velden)}.";

        if (Heeft(o, "titel", out var t))
        {
            var titel = t.ValueKind == JsonValueKind.String ? t.GetString()!.Trim().ReplaceLineEndings(" ") : "";
            if (titel.Length == 0) return "titel moet een tekst zijn en mag niet leeg zijn.";
            if (titel.Length > MaxTitel) return $"titel is langer dan {MaxTitel} tekens.";
            w.Titel = titel;
        }
        else if (bestaand is null) return "titel ontbreekt.";

        if (Heeft(o, "status", out var s))
        {
            var tekst = s.ValueKind == JsonValueKind.String ? s.GetString()! : s.ToString();
            if (ZoekStatus(tekst) is not { } def) return StatusFout(tekst);
            w.Status = def.Code;
        }

        if (Heeft(o, "prioriteit", out var pr))
        {
            const string fout = "prioriteit moet hoog, gemiddeld, laag (of 1, 2, 3) zijn; null of \"geen\" haalt hem weg.";
            w.HeeftPrio = true;
            if (pr.ValueKind == JsonValueKind.Null) w.Prio = null;
            else if (pr.ValueKind == JsonValueKind.Number) { if (pr.TryGetInt32(out var n) && n is >= 1 and <= 3) w.Prio = n; else return fout; }
            else if (pr.ValueKind == JsonValueKind.String)
            {
                var x = pr.GetString()!.Trim();
                if (x.Length == 0 || x.Equals("geen", Los)) w.Prio = null;
                else
                {
                    var gevonden = ActPrioriteit.Alle.FirstOrDefault(q => q.Naam.Equals(x, Los) || q.Waarde.ToString() == x);
                    if (gevonden.Waarde == 0) return fout;
                    w.Prio = gevonden.Waarde;
                }
            }
            else return fout;
        }

        if (Heeft(o, "vervaldatum", out var vd))
        {
            w.HeeftDatum = true;
            var x = vd.ValueKind == JsonValueKind.String ? vd.GetString()!.Trim() : vd.ValueKind == JsonValueKind.Null ? "" : "?";
            if (x.Length == 0) w.Datum = null;
            else if (DateTime.TryParseExact(x, new[] { "yyyy-MM-dd", "d-M-yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dag)) w.Datum = dag.Date;
            else return "vervaldatum moet een datum zijn als \"2026-12-31\"; null haalt hem weg.";
        }

        if (Heeft(o, "benodigdeTijd", out var bt) || Heeft(o, "benodigdeUren", out bt))
        {
            const string fout = "benodigdeTijd is een aantal uren (1.5) of een tekst als \"45m\", \"1:30\", \"2u\" of \"2d\" (een dag is 8 uur); null haalt hem weg.";
            if (!metTijd) return "De benodigde tijd kan in deze app nog niet worden bewaard.";
            w.HeeftUren = true;
            if (bt.ValueKind == JsonValueKind.Null) w.Uren = null;
            else if (bt.ValueKind == JsonValueKind.Number)
            {
                if (!bt.TryGetDecimal(out var u) || u < 0 || u > ActTijd.Max) return fout;
                u = Math.Round(u, 2, MidpointRounding.AwayFromZero);
                w.Uren = u > 0 ? u : null;
            }
            else if (bt.ValueKind == JsonValueKind.String) { if (!ActTijd.Lees(bt.GetString(), out w.Uren)) return fout; }
            else return fout;
        }

        if (Heeft(o, "onderwerpen", out var ond))
        {
            var fout = Lijst(ond, "onderwerpen", x => ZoekLabel(x, "Onderwerp", d.Problemen.Select(p => (p.Id, p.Naam))), out w.Onderwerpen);
            if (fout is not null) return fout;
        }
        if (Heeft(o, "types", out var typ))
        {
            var fout = Lijst(typ, "types", x => ZoekLabel(x, "Type", d.Types.Select(p => (p.Id, p.Naam))), out w.Types);
            if (fout is not null) return fout;
        }
        if (Heeft(o, "personen", out var per))
        {
            var fout = Lijst(per, "personen", x => { var f = ZoekPersoon(x, v, bestaand, out var id); return (id, f); }, out w.Personen);
            if (fout is not null) return fout;
        }

        JsonElement? blok = null;
        if (Heeft(o, "tekst", out var tk))
        {
            if (tk.ValueKind != JsonValueKind.Object) return "tekst moet een object zijn met tekstvelden, bv. { \"samenvatting\": \"…\" }.";
            foreach (var p in tk.EnumerateObject())
                if (!ActTekst.Velden.Any(x => x.Kolom.Equals(p.Name, Los)))
                    return $"Onbekend tekstveld '{p.Name}'. Tekstvelden: {string.Join(", ", ActTekst.Velden.Select(x => Klein(x.Kolom)))}.";
            blok = tk;
        }
        foreach (var veld in ActTekst.Velden)
        {
            if (!Heeft(o, veld.Kolom, out var x) && !(blok is { } b && Heeft(b, veld.Kolom, out x))) continue;
            if (x.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return $"{Klein(veld.Kolom)} moet een tekst zijn.";
            var tekst = (x.ValueKind == JsonValueKind.String ? x.GetString()! : "").Trim();
            if (tekst.Length > MaxTekst) return $"{Klein(veld.Kolom)} is langer dan {MaxTekst} tekens.";
            w.Tekst[veld.Kolom] = tekst;
        }
        return null;
    }

    /// <summary>Een lijst (of één losse waarde) van ids of namen; null = lege lijst.</summary>
    private static string? Lijst(JsonElement el, string veld, Func<JsonElement, (int Id, string? Fout)> zoek, out List<int>? ids)
    {
        ids = new List<int>();
        var items = el.ValueKind switch
        {
            JsonValueKind.Null => new List<JsonElement>(),
            JsonValueKind.Array => el.EnumerateArray().ToList(),
            JsonValueKind.String or JsonValueKind.Number => new List<JsonElement> { el },
            _ => null,
        };
        if (items is null) return $"{veld} moet een lijst van namen of ids zijn.";
        foreach (var x in items)
        {
            var (id, fout) = zoek(x);
            if (fout is not null) return fout;
            if (!ids.Contains(id)) ids.Add(id);
        }
        return null;
    }

    /// <summary>Onderwerp of type van dit board: een getal is het id, een tekst de naam (hoofdletters tellen niet).</summary>
    private static (int Id, string? Fout) ZoekLabel(JsonElement x, string soort, IEnumerable<(int Id, string Naam)> aanwezig)
    {
        var lijst = aanwezig.ToList();
        string Aanwezig() => lijst.Count == 0 ? "Dit board heeft er nog geen; voeg het eerst toe." : "Aanwezig: " + string.Join(", ", lijst.Select(l => $"{l.Naam} ({l.Id})")) + ".";
        if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var id))
            return lijst.Any(l => l.Id == id) ? (id, null) : (0, $"{soort} {id} staat niet op dit board. {Aanwezig()}");
        if (x.ValueKind == JsonValueKind.String)
        {
            var naam = x.GetString()!.Trim();
            var op = lijst.Where(l => l.Naam.Trim().Equals(naam, Los)).ToList();
            if (op.Count == 1) return (op[0].Id, null);
            if (int.TryParse(naam, out var alsId) && lijst.Any(l => l.Id == alsId)) return (alsId, null);
            return (0, $"{soort} '{naam}' staat niet op dit board. {Aanwezig()}");
        }
        return (0, $"{soort}: verwacht een naam of een id.");
    }

    /// <summary>
    /// Een persoon: id, "ik", de hele naam, of een deel van de naam als dat er maar één oplevert. Alleen wie in dienst is;
    /// wie al op de kaart staat mag blijven staan, ook als hij inmiddels uit dienst is.
    /// </summary>
    private static string? ZoekPersoon(JsonElement x, Verzoek v, Activiteit? bestaand, out int id)
    {
        id = 0;
        bool Mag(ActPersoon p) => p.Kiesbaar || bestaand?.Personen.Contains(p.Id) == true;
        if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var nr))
        {
            if (!v.OpId.TryGetValue(nr, out var p)) return $"Persoon {nr} bestaat niet.";
            if (!Mag(p)) return $"{p.Naam} is uit dienst en kan niet worden toegewezen.";
            id = nr; return null;
        }
        if (x.ValueKind != JsonValueKind.String) return "personen: verwacht een naam, een id of \"ik\".";
        var naam = x.GetString()!.Trim();
        if (naam.Equals("ik", Los) || naam.Equals("mij", Los)) { id = v.Wie.Id; return null; }
        var kies = v.Personen.Where(Mag).ToList();
        var treffers = kies.Where(p => p.Naam.Trim().Equals(naam, Los)).ToList();
        if (treffers.Count == 0 && naam.Length >= 2) treffers = kies.Where(p => p.Naam.Contains(naam, Los)).ToList();
        if (treffers.Count == 1) { id = treffers[0].Id; return null; }
        if (treffers.Count > 1) return $"'{naam}' past bij meer personen: {string.Join(", ", treffers.Take(8).Select(p => $"{p.Naam} ({p.Id})"))}. Geef de hele naam of het id.";
        if (int.TryParse(naam, out var alsId) && v.OpId.TryGetValue(alsId, out var pid) && Mag(pid)) { id = alsId; return null; }
        return $"Persoon '{naam}' is niet gevonden. De lijst staat op /api/activityboard/personen.";
    }

    /// <summary>De baan van een activiteit: het gekoppelde onderwerp met de hoogste prioriteit, of 0 (geen onderwerp).</summary>
    private static int Baan(Activiteit a, Dictionary<int, int> idx)
    {
        var best = 0; var bi = int.MaxValue;
        foreach (var id in a.Problemen)
            if (idx.TryGetValue(id, out var i) && i < bi) { bi = i; best = id; }
        return best;
    }

    // ------------------------------------------------------------------ melding in de bel

    /// <summary>
    /// Zelfde melding als het scherm (ActivityBoard.razor, MeldToewijzing): wie wordt toegewezen krijgt een melding in de
    /// bel met een link naar de kaart. Niet aan jezelf, alleen aan wie in dienst is.
    /// </summary>
    private static async Task MeldToewijzing(Verzoek v, ActBoardData d, Activiteit a, IEnumerable<int> personen, bool nieuw)
    {
        var meldingen = v.Dienst<IMeldingRepository>();
        if (!meldingen.Beschikbaar) return;
        var wie = personen.Distinct().Where(p => p != v.Wie.Id && v.OpId.GetValueOrDefault(p)?.Kiesbaar == true).ToList();
        if (wie.Count == 0) return;
        var titel = a.Titel.Length > 90 ? a.Titel[..87] + "…" : a.Titel;
        var tekst = (nieuw ? $"{v.Wie.Naam} heeft een activiteit voor je aangemaakt: ‘{titel}’" : $"{v.Wie.Naam} heeft je toegewezen aan ‘{titel}’")
                    + $" (Activity board {d.Board.Titel})";
        if (tekst.Length > 250) tekst = tekst[..249] + "…";
        var gelukt = 0;
        foreach (var p in wie)
        {
            try { await meldingen.MaakAsync(p, "activiteit", a.Id, tekst); gelukt++; }
            catch { /* de activiteit zelf is gelukt; de melding is een extra */ }
        }
        if (gelukt > 0) v.Dienst<MeldingSignaal>().Geef();
    }
}
