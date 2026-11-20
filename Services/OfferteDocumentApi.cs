using System.Text.Json.Nodes;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// De adressen waar het bewerkscherm van het offertedocument (wwwroot/offertedocument.html) zijn gegevens haalt en
/// bewaart, onder <c>/offertedocument/api</c>. Lezen: aangemeld. Maken, wijzigen, vastleggen en weer bewerkbaar maken: een
/// gekoppelde werknemer (zoals bij offertes en calculaties) of Directie.
/// </summary>
public static class OfferteDocumentApi
{
    private static bool Mag(HuidigeGebruiker ik) => ik.Gekoppeld || ik.Mag("Directie");
    private static IResult Fout(int status, string tekst) => Results.Json(new { fout = tekst }, statusCode: status);

    public static RouteGroupBuilder MapOfferteDocumentApi(this WebApplication app)
    {
        var api = app.MapGroup("/offertedocument/api");

        api.MapGet("/{offerteId:int}", Stand);

        // Een document maken uit de vaste opzet (OfferteDocumentData.Sjabloon).
        api.MapPost("/{offerteId:int}/maak", async (int offerteId, IOfferteDocumentRepository repo, IOfferteRepository offertes,
                                                    ICalculatieRepository calc, OfferteDocumentService dienst, HuidigeGebruiker ik) =>
        {
            await ik.ZorgGeladenAsync();
            if (!Mag(ik)) return Fout(403, "Je mag voor deze offerte geen document maken.");
            if (!repo.Beschikbaar || !await repo.TabelAanwezigAsync()) return Fout(503, "Draai eerst OfferteDocumenten_MariaDB.sql op de database.");
            var o = await offertes.GetAsync(offerteId);
            if (o is null) return Fout(404, "Deze offerte bestaat niet (meer).");
            try { await repo.MaakAsync(offerteId, OfferteDocumentData.Sjabloon(o), ik.WerknemerId); }
            catch (InvalidOperationException) { /* er is er al een: gewoon tonen */ }
            return await Stand(offerteId, repo, offertes, dienst, ik);
        });

        // Opslaan met het versienummer; wat de browser stuurt gaat eerst door OfferteDocumentData.Schoon.
        api.MapPut("/{offerteId:int}", async (int offerteId, HttpRequest req, IOfferteDocumentRepository repo, HuidigeGebruiker ik) =>
        {
            await ik.ZorgGeladenAsync();
            if (!Mag(ik)) return Fout(403, "Je mag dit document niet wijzigen.");
            JsonNode? body;
            try { body = await JsonNode.ParseAsync(req.Body); } catch { body = null; }
            int versie;
            try { versie = body!["versie"]!.GetValue<int>(); } catch { return Fout(400, "Geen geldige versie ontvangen."); }
            var (data, reden) = OfferteDocumentData.Schoon(body["data"]);
            if (data is null) return Fout(400, reden ?? "Geen geldig document ontvangen.");
            try { return Results.Json(new { versie = await repo.BewaarAsync(offerteId, versie, data, ik.WerknemerId) }); }
            catch (OfferteDocumentConflictException ex) { return Fout(409, ex.Message); }
        });

        // De gekoppelde blokken opnieuw opmaken met wat er nu in de browser staat (na een keuze in een prijstabel, en als
        // je terugkomt van de calculatie): er wordt niets bewaard.
        api.MapPost("/{offerteId:int}/weergave", async (int offerteId, HttpRequest req, OfferteDocumentService dienst, HuidigeGebruiker ik) =>
        {
            await ik.ZorgGeladenAsync();
            JsonNode? body;
            try { body = await JsonNode.ParseAsync(req.Body); } catch { body = null; }
            var (data, reden) = OfferteDocumentData.Schoon(body?["data"]);
            if (data is null) return Fout(400, reden ?? "Geen geldig document ontvangen.");
            try { return Results.Json(WeergaveJson(await dienst.WeergaveAsync(offerteId, data, ik.WerknemerId), metStijl: false)); }
            catch (InvalidOperationException ex) { return Fout(404, ex.Message); }
        });

        api.MapPost("/{offerteId:int}/vastleggen", async (int offerteId, IOfferteDocumentRepository repo, OfferteDocumentService dienst, HuidigeGebruiker ik) =>
        {
            await ik.ZorgGeladenAsync();
            if (!Mag(ik)) return Fout(403, "Je mag dit document niet vastleggen.");
            if (await dienst.LegVastAsync(offerteId, ik.WerknemerId) is { } reden) return Fout(409, "Vastleggen kan nog niet. " + reden);
            return Results.Json(new { ok = true });
        });

        api.MapPost("/{offerteId:int}/vrijgeven", async (int offerteId, IOfferteDocumentRepository repo, HuidigeGebruiker ik) =>
        {
            await ik.ZorgGeladenAsync();
            if (!Mag(ik)) return Fout(403, "Je mag dit document niet weer bewerkbaar maken.");
            if (await repo.GetAsync(offerteId) is null) return Fout(404, "Deze offerte heeft geen document.");
            await repo.ZetStatusAsync(offerteId, 0, null, ik.WerknemerId);
            return Results.Json(new { ok = true });
        });

        api.MapPost("/{offerteId:int}/wis", async (int offerteId, IOfferteDocumentRepository repo, HuidigeGebruiker ik) =>
        {
            await ik.ZorgGeladenAsync();
            if (!Mag(ik)) return Fout(403, "Je mag dit document niet wissen.");
            return await repo.WisAsync(offerteId) ? Results.Json(new { ok = true })
                 : Fout(409, "Wissen kan alleen bij een document dat nog niet is vastgelegd.");
        });

        // De PDF: vastgelegd = zoals bewaard, anders met de cijfers van nu. ?download=1 = als bestand.
        api.MapGet("/{offerteId:int}/pdf", async (int offerteId, bool? download, IOfferteRepository offertes, OfferteDocumentService dienst,
                                                  HuidigeGebruiker ik, CancellationToken stop) =>
        {
            await ik.ZorgGeladenAsync();
            var o = await offertes.GetAsync(offerteId);
            if (o is null) return Results.NotFound("Deze offerte bestaat niet (meer).");
            string? html;
            try { html = await dienst.HtmlVoorPdfAsync(offerteId, ik.WerknemerId); }
            catch (Exception ex) { return Results.Problem("Het document opmaken is mislukt: " + ex.Message); }
            if (html is null) return Results.NotFound("Deze offerte heeft geen document.");
            byte[] pdf;
            try { pdf = await dienst.NaarPdfAsync(html, stop); }
            catch (Exception ex) { return Results.Problem("De PDF maken is mislukt: " + ex.Message); }
            return download == true ? Results.File(pdf, "application/pdf", OffertePdfService.Bestandsnaam(o))
                                    : Results.File(pdf, "application/pdf");
        });

        return api;
    }

    private static object WeergaveJson(DocumentWeergave w, bool metStijl) => new
    {
        stijl = metStijl ? w.Stijl : null, kop = w.Kop, blokken = w.Blokken, slot = w.Slot, totaal = w.Totaal,
        problemen = w.Problemen,
    };

    private static async Task<IResult> Stand(int offerteId, IOfferteDocumentRepository repo, IOfferteRepository offertes,
                                             OfferteDocumentService dienst, HuidigeGebruiker ik)
    {
        await ik.ZorgGeladenAsync();
        var o = await offertes.GetAsync(offerteId);
        if (o is null) return Fout(404, "Deze offerte bestaat niet (meer).");
        var info = new { offertenr = o.OfferteNr, offerte = o.Omschrijving, klant = o.Klant, bv = o.Bv, engels = o.Taal == OfferteTaal.Engels };
        var mag = Mag(ik);
        if (!repo.Beschikbaar) return Results.Json(new { info, tabel = false, bestaat = false, mag = false });
        if (!await repo.TabelAanwezigAsync()) return Results.Json(new { info, tabel = false, bestaat = false, mag });

        var d = await repo.GetAsync(offerteId);
        // ook zonder document: de calculatie van nu (voor de melding "nog geen calculatie")
        DocumentWeergave nu;
        try { nu = await dienst.WeergaveAsync(offerteId, d?.Data ?? """{"blokken":[]}""", ik.WerknemerId); }
        catch (Exception ex) { return Fout(500, "Het document opmaken is mislukt: " + ex.Message); }
        if (d is null) return Results.Json(new { info, tabel = true, bestaat = false, mag, calculatie = nu.Calculatie });

        var vast = d.Vastgelegd ? OfferteDocumentService.UitVast(d.Vast) : null;
        var toon = vast ?? nu;
        var wanneer = d.Vastgelegd && d.VastOp is DateTime v
            ? $"Vastgelegd {v:dd-MM-yyyy HH:mm}{(d.VastDoor.Length > 0 ? " door " + d.VastDoor : "")}"
            : d.GewijzigdOp is DateTime g ? $"Opgeslagen {g:dd-MM-yyyy HH:mm}{(d.GewijzigdDoor.Length > 0 ? " door " + d.GewijzigdDoor : "")}" : "";
        return Results.Json(new
        {
            info, tabel = true, bestaat = true, mag,
            calculatie = nu.Calculatie,
            versie = d.Versie, status = d.Status, gewijzigd = wanneer,
            data = JsonNode.Parse(d.Data),
            weergave = WeergaveJson(toon, metStijl: true),
            groepen = nu.Groepen.Select(g => new { id = g.Id, naam = g.Naam, diepte = g.Diepte, verkoop = g.Verkoop }),
            // vastgelegd en de calculatie is daarna veranderd: het document toont nog het bedrag van toen
            verschil = vast is not null && vast.Totaal != nu.Totaal ? new { toen = vast.Totaal, nu = nu.Totaal } : null,
            velden = OfferteDocumentData.Velden.Select(x => new { veld = x.Veld, uitleg = x.Uitleg }),
        });
    }
}
