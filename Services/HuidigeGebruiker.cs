using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace EverythingApp.Services;

/// <summary>
/// Wie is er ingelogd, welke werknemer is dat, en wat mag die?
///
/// Werkwijze:
///   1. Google-login levert het e-mailadres.
///   2. Dat adres wordt in Werknemers opgezocht -> Werknemer-id.
///   3. Alle kolommen van die werknemer worden ingelezen als rechten.
///
/// Rechten uitbreiden kan zonder de app aan te passen: voeg een ja/nee-kolom toe aan
/// Werknemers (bijv. MagFacturenGoedkeuren) en gebruik in een pagina:
///     @if (Ik.Mag("MagFacturenGoedkeuren")) { ... }
///
/// Vervangt de VBA-functie Get_user_id (die de gebruiker per pc uit
/// PersonalSettings.mdb haalde).
/// </summary>
public class HuidigeGebruiker
{
    private readonly AuthenticationStateProvider _auth;
    private readonly IUrenRepository _repo;
    private readonly IWerkplekRepository _werkplekken;
    private Dictionary<string, object?> _velden = new(StringComparer.OrdinalIgnoreCase);

    public HuidigeGebruiker(AuthenticationStateProvider auth, IUrenRepository repo, IWerkplekRepository werkplekken)
    {
        _auth = auth;
        _repo = repo;
        _werkplekken = werkplekken;
    }

    /// <summary>
    /// Ingelogd als werkplek (gekoppelde pc, geen Google-account): Mail lezen en Agenda staan dan uit, mail versturen
    /// gaat vanaf de alias van de werkplek. Rechten en historie via <see cref="Werkplek.WerknemerId"/>.
    /// </summary>
    public Werkplek? Werkplek { get; private set; }
    public bool IsWerkplek => Werkplek is not null;
    /// <summary>Het gekoppelde apparaat (pc) van een werkplek.</summary>
    public int? ApparaatId { get; private set; }

    // ---- wie werkt er aan de werkplek ("Wie ben jij?" + pincode; na Pincode.Duur minuten zonder activiteit niemand) ----
    private int? _medewerkerId;
    private string _medewerkerNaam = "";
    private DateTime _laatsteActiviteit;
    private int _duur = Pincode.Duur;   // minuten voor de aangemelde medewerker (Werknemers.WerkplekMinuten)
    private DateTime _laatstGemeld;
    private int? _doorgegevenId;   // laatst doorgegeven via MedewerkerGewijzigd (de 30 minuten verlopen zonder gebeurtenis)

    /// <summary>Wie er op deze werkplek aangemeld is (en nog actief), of null.</summary>
    public (int Id, string Naam)? Medewerker
        => IsWerkplek && _medewerkerId is int m && DateTime.Now - _laatsteActiviteit < TimeSpan.FromMinutes(_duur)
            ? (m, _medewerkerNaam) : null;

    /// <summary>Na hoeveel minuten zonder activiteit de aangemelde medewerker weer afgemeld wordt (per werknemer instelbaar).</summary>
    public int WerkplekDuur => _duur;

    /// <summary>Er meldde zich iemand aan of af (ook vanuit een ander tabblad op deze pc).</summary>
    public event Action? MedewerkerGewijzigd;

    private async Task LeesMedewerkerAsync()
    {
        if (ApparaatId is not int a) return;
        var oud = Medewerker?.Id;
        try
        {
            var m = await _werkplekken.MedewerkerAsync(a);
            if (m is { } x)
            {
                if (x.MedewerkerId != _medewerkerId)
                {
                    _medewerkerNaam = (await _werkplekken.PersonenAsync()).FirstOrDefault(p => p.Id == x.MedewerkerId)?.Naam
                                      ?? (await _repo.GetWerknemerAsync(x.MedewerkerId))?.Naam ?? $"#{x.MedewerkerId}";
                    _duur = await DuurVanAsync(x.MedewerkerId);
                    _medewerkerFusion = await HeeftRechtAsync(x.MedewerkerId, "Fusion");
                }
                _medewerkerId = x.MedewerkerId;
                _laatsteActiviteit = x.LaatsteActiviteit;
            }
            else { _medewerkerId = null; _medewerkerNaam = ""; _medewerkerFusion = false; }
        }
        catch { }
        if (oud != Medewerker?.Id) MedewerkerGewijzigd?.Invoke();
    }

    /// <summary>Aanmelden met naam en pincode. Geeft een foutmelding, of null als het gelukt is.</summary>
    public async Task<string?> MeldAanAsync(int werknemerId, string naam, string pincode)
    {
        if (ApparaatId is not int a) return "Deze pc is geen gekoppelde werkplek.";
        if (Pincode.Geblokkeerd(werknemerId) is { } wacht)
            return $"Te vaak een verkeerde pincode. Probeer het over {Math.Ceiling(wacht.TotalMinutes)} minuten opnieuw.";
        if (!(await _werkplekken.PersonenVoorAsync(Werkplek?.Id)).Any(p => p.Id == werknemerId))
            return "Je staat niet in de lijst van deze werkplek (Beheer ▸ Werkplekken, of Werknemers ▸ Status).";
        var hash = await _werkplekken.PincodeHashAsync(werknemerId);
        if (hash is null) return "Je hebt nog geen pincode. Stel die in op je eigen pc: Werknemers ▸ Mijn pincode.";
        if (!Pincode.Klopt(pincode, hash)) { Pincode.Fout(werknemerId); return "Die pincode klopt niet."; }
        Pincode.Goed(werknemerId);
        await _werkplekken.ZetMedewerkerAsync(a, werknemerId);
        _medewerkerId = werknemerId;
        _medewerkerNaam = naam;
        _laatsteActiviteit = _laatstGemeld = DateTime.Now;
        _duur = await DuurVanAsync(werknemerId);
        _medewerkerFusion = await HeeftRechtAsync(werknemerId, "Fusion");
        MedewerkerGewijzigd?.Invoke();
        return null;
    }

    /// <summary>Het Fusion-vinkje van de aangemelde medewerker zelf (zie <see cref="MagFusion"/>).</summary>
    private bool _medewerkerFusion;

    private async Task<bool> HeeftRechtAsync(int werknemerId, string recht)
    {
        try { return (await _repo.GetWerknemerVeldenAsync(werknemerId)).TryGetValue(recht, out var w) && IsWaar(w); }
        catch { return false; }
    }

    /// <summary>
    /// Startcode van een beheerder controleren (Beheer ▸ Werkplekken): voor wie geen Google-account heeft of zijn pincode
    /// vergat. Telt mee in dezelfde blokkade als de pincode (5 fout = 5 minuten wachten). Null = goed.
    /// </summary>
    public async Task<string?> ControleerStartcodeAsync(int werknemerId, string code)
    {
        if (ApparaatId is null) return "Deze pc is geen gekoppelde werkplek.";
        if (Pincode.Geblokkeerd(werknemerId) is { } wacht)
            return $"Te vaak een verkeerde code. Probeer het over {Math.Ceiling(wacht.TotalMinutes)} minuten opnieuw.";
        if (!(await _werkplekken.PersonenVoorAsync(Werkplek?.Id)).Any(p => p.Id == werknemerId))
            return "Je staat niet in de lijst van deze werkplek (Beheer ▸ Werkplekken, of Werknemers ▸ Status).";
        var hash = await _werkplekken.StartcodeHashAsync(werknemerId);
        if (hash is null) return "Er is geen geldige startcode voor jou. Vraag de beheerder om een nieuwe (Beheer ▸ Werkplekken).";
        if (!Pincode.Klopt(code, hash)) { Pincode.Fout(werknemerId); return "Die startcode klopt niet."; }
        Pincode.Goed(werknemerId);
        return null;
    }

    /// <summary>Met een geldige startcode zelf een pincode kiezen; daarna is de startcode op en ben je aangemeld.</summary>
    public async Task<string?> StelPincodeInAsync(int werknemerId, string naam, string startcode, string pincode)
    {
        if (await ControleerStartcodeAsync(werknemerId, startcode) is { } fout) return fout;
        if (!Pincode.Geldig(pincode)) return "Een pincode is 4 cijfers.";
        await _werkplekken.ZetPincodeAsync(werknemerId, pincode);
        await _werkplekken.WisStartcodeAsync(werknemerId);
        return await MeldAanAsync(werknemerId, naam, pincode);
    }

    private async Task<int> DuurVanAsync(int werknemerId)
    {
        try { return Pincode.DuurVan(await _werkplekken.MinutenAsync(werknemerId)); }
        catch { return Pincode.Duur; }
    }

    public async Task MeldAfAsync()
    {
        if (ApparaatId is int a) await _werkplekken.ZetMedewerkerAsync(a, null);
        _medewerkerId = null;
        _medewerkerNaam = "";
        _medewerkerFusion = false;
        MedewerkerGewijzigd?.Invoke();
    }

    /// <summary>
    /// Elke minuut vanuit de browser (wwwroot/werkplek.js): <paramref name="actief"/> = er is in die minuut geklikt of
    /// getypt. Dan schuiven de 30 minuten op; daarna wordt gekeken of een ander tabblad op deze pc iemand anders
    /// aanmeldde, of dat de tijd om is.
    /// </summary>
    public async Task TikAsync(bool actief)
    {
        if (ApparaatId is not int a) return;
        if (actief && Medewerker is not null && DateTime.Now - _laatstGemeld > TimeSpan.FromSeconds(50))
        {
            _laatstGemeld = DateTime.Now;
            try { await _werkplekken.ActiviteitAsync(a); } catch { }
        }
        await LeesMedewerkerAsync();
        // Tijd om (of anders veranderd): doorgeven, zodat de werkplek op slot gaat (WerkplekMedewerker, Slot)
        if (_doorgegevenId != Medewerker?.Id)
        {
            _doorgegevenId = Medewerker?.Id;
            MedewerkerGewijzigd?.Invoke();
        }
    }

    /// <summary>Is er iemand ingelogd (Google)?</summary>
    public bool Ingelogd { get; private set; }

    /// <summary>E-mailadres uit het Google-account.</summary>
    public string Email { get; private set; } = "";

    /// <summary>Naam zoals Google die kent (valt terug als er geen werknemer gevonden is).</summary>
    public string GoogleNaam { get; private set; } = "";

    /// <summary>
    /// De gekoppelde werknemer, of null als het account niet aan een werknemer hangt. Op een werkplek is dat de
    /// aangemelde medewerker ("Wie ben jij?") zolang die actief is, anders de werknemer van de werkplek zelf; zo komt
    /// werk in de historie op naam van wie het deed. De rechten (<see cref="Mag"/>) blijven die van de werkplek.
    /// </summary>
    public int? WerknemerId => Medewerker?.Id ?? _werknemerId;
    private int? _werknemerId;

    /// <summary>Naam van de gekoppelde werknemer (NaamVoluit), anders de Google-naam.</summary>
    public string Naam { get; private set; } = "";

    /// <summary>Is het account aan een werknemer gekoppeld?</summary>
    public bool Gekoppeld => WerknemerId is not null;

    /// <summary>Alle velden van de werknemer — handig om te controleren welke rechten er zijn.</summary>
    public IReadOnlyDictionary<string, object?> Velden => _velden;

    /// <summary>
    /// Eén keer per sessie ophalen. Veilig om vaker aan te roepen: wie tijdens het laden komt (meerdere onderdelen
    /// starten tegelijk), wacht op hetzelfde laden in plaats van meteen terug te krijgen wat er nog niet is.
    /// </summary>
    public Task ZorgGeladenAsync() => _laden ??= LaadAsync();
    private Task? _laden;

    private async Task LaadAsync()
    {
        try
        {
            var status = await _auth.GetAuthenticationStateAsync();
            var gebruiker = status.User;
            if (gebruiker?.Identity?.IsAuthenticated != true) return;

            Ingelogd = true;
            GoogleNaam = gebruiker.Identity.Name ?? "";
            Naam = GoogleNaam;

            // Werkplek: de pc is gekoppeld (WerkplekLogin), niet iemand met Google ingelogd
            if (WerkplekLogin.WerkplekId(gebruiker) is int wid)
            {
                var w = await _werkplekken.GetAsync(wid);
                if (w is null || !w.Actief) { Ingelogd = false; return; }
                Werkplek = w;
                Naam = w.Naam;
                _werknemerId = w.WerknemerId;
                ApparaatId = WerkplekLogin.ApparaatId(gebruiker);
                _velden = await _repo.GetWerknemerVeldenAsync(w.WerknemerId);
                await LeesMedewerkerAsync();
                return;
            }

            Email = gebruiker.FindFirst(ClaimTypes.Email)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(Email)) return;

            var id = await _repo.VindWerknemerOpEmailAsync(Email);
            if (id is null) return;

            _werknemerId = id;
            _velden = await _repo.GetWerknemerVeldenAsync(id.Value);

            var werknemer = await _repo.GetWerknemerAsync(id.Value);
            if (werknemer is not null && !string.IsNullOrWhiteSpace(werknemer.Naam))
                Naam = werknemer.Naam;
        }
        catch
        {
            // Login nog niet ingesteld of database onbereikbaar: app blijft werken,
            // maar zonder gekoppelde werknemer en zonder rechten.
        }
    }

    /// <summary>
    /// Mag deze gebruiker iets? Kijkt naar de kolom met die naam in Werknemers.
    /// Waar = 1 of -1 (Access-stijl) of true. Onbekende kolom = niet toegestaan.
    /// </summary>
    public bool Mag(string recht)
    {
        if (string.IsNullOrWhiteSpace(recht)) return false;
        if (IsWerkplek && NietOpWerkplek.Contains(recht)) return false;
        return _velden.TryGetValue(recht, out var waarde) && IsWaar(waarde);
    }

    private static bool IsWaar(object? waarde)
    {
        if (waarde is null || waarde is DBNull) return false;
        if (waarde is bool b) return b;
        try { return Convert.ToInt64(waarde) != 0; }
        catch { return false; }
    }

    /// <summary>
    /// Rechten die op een werkplek (gedeelde pc) nooit gelden, ook niet als de werknemer van de werkplek ze heeft:
    /// financiën, personeelszaken (HRM), en Directie (die geeft overal ook toegang tot financiën en personeelsgegevens).
    /// Zie ook <see cref="DichtOpWerkplek"/>.
    /// </summary>
    private static readonly HashSet<string> NietOpWerkplek = new(StringComparer.OrdinalIgnoreCase)
        { "Financial", "MagFacturenGoedkeuren", "HRM", "Directie" };

    /// <summary>Modules die op een werkplek helemaal uit staan (menu, zoekvenster en adres), ook met rechten.</summary>
    public static readonly string[] ModulesNietOpWerkplek = { "financien", "werknemers" };

    /// <summary>
    /// Naam van de module als dit adres (binnen de app) op een werkplek dicht is (Financiën, Werknemers), anders null.
    /// </summary>
    public static string? DichtOpWerkplek(string pad)
    {
        pad = pad.Split('?', '#')[0].Trim('/');
        foreach (var m in Menu.Modules.Where(m => ModulesNietOpWerkplek.Contains(m.Sleutel)))
        {
            if (pad.Equals(m.Sleutel, StringComparison.OrdinalIgnoreCase)
                || pad.StartsWith(m.Sleutel + "/", StringComparison.OrdinalIgnoreCase)
                || pad.Equals("module/" + m.Sleutel, StringComparison.OrdinalIgnoreCase))
                return m.Naam;
        }
        return null;
    }

    /// <summary>
    /// Financiën (het menu, alle schermen onder /financien en de AI op /ai) alleen met het recht Financial of Directie
    /// (besluit gebruiker 19-10-2026). Op een werkplek nooit: <see cref="Mag"/> geeft daar voor beide false.
    /// </summary>
    public bool MagFinancien => Mag("Financial") || Mag("Directie");

    /// <summary>Hoort dit adres (binnen de app) bij Financiën, inclusief de AI?</summary>
    /// <summary>De schermen onder Tools ▸ Fusion: alleen met het recht Fusion (Werknemers_Fusion_MariaDB.sql).</summary>
    public static bool IsFusionPad(string pad)
    {
        pad = pad.Split('?', '#')[0].Trim('/');
        return pad is "tools/fusion-cam" or "tools/ec-componenten" or "tools/jlc-componenten" or "tools/fusion-addin" or "module/fusion";
    }

    /// <summary>
    /// Mag Fusion gebruiken: het recht, of Directie; zonder gekoppelde werknemer (lokaal testen) ook. Op een werkplek telt
    /// ook het Fusion-vinkje van wie er aangemeld is ("Wie ben jij?"), want de werknemer van de werkplek is meestal een
    /// gedeelde als "Niemand" (Thomas, inlener zonder account, 6-11-2026).
    /// </summary>
    public bool MagFusion => !Gekoppeld || Mag("Fusion") || Mag("Directie") || (Medewerker is not null && _medewerkerFusion);

    /// <summary>Verkooporders invoeren (Bestellingen ▸ Verkooporders): het recht Verkoop, of Directie (VerkoopOrders_MariaDB.sql).</summary>
    public bool MagVerkoop => Mag("Verkoop") || Mag("Directie");

    public static bool IsFinancienPad(string pad)
    {
        pad = pad.Split('?', '#')[0].Trim('/');
        return pad.Equals("financien", StringComparison.OrdinalIgnoreCase)
            || pad.StartsWith("financien/", StringComparison.OrdinalIgnoreCase)
            || pad.Equals("module/financien", StringComparison.OrdinalIgnoreCase)
            || pad.Equals("ai", StringComparison.OrdinalIgnoreCase)
            || pad.StartsWith("ai/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Mag minstens één van deze rechten.</summary>
    public bool MagEenVan(params string[] rechten) => rechten.Any(Mag);
}
