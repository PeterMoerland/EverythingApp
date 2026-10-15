using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EverythingApp.Services;

/// <summary>
/// Inloggen als werkplek: een pc wordt met een eenmalige code (Beheer ▸ Werkplekken) gekoppeld en krijgt dan een
/// inlogcookie met de werkplek en het apparaat erin, een jaar geldig (glijdend). Bij elk verzoek controleert
/// <see cref="ControleerAsync"/> of het apparaat nog gekoppeld en de werkplek actief is (één keer per minuut naar de
/// database); ontkoppelen in het beheerscherm sluit de pc dus buiten bij het volgende laden van een pagina.
/// </summary>
public static class WerkplekLogin
{
    public const string ClaimWerkplek = "mde:werkplek";
    public const string ClaimApparaat = "mde:apparaat";

    public static int? WerkplekId(ClaimsPrincipal p) => int.TryParse(p.FindFirst(ClaimWerkplek)?.Value, out var i) ? i : null;
    public static int? ApparaatId(ClaimsPrincipal p) => int.TryParse(p.FindFirst(ClaimApparaat)?.Value, out var i) ? i : null;

    public static ClaimsPrincipal Maak(Werkplek w, int apparaatId)
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "werkplek:" + w.Id),
            new Claim(ClaimTypes.Name, w.Naam),
            new Claim(ClaimWerkplek, w.Id.ToString()),
            new Claim(ClaimApparaat, apparaatId.ToString()),
        }, CookieAuthenticationDefaults.AuthenticationScheme));

    public static AuthenticationProperties Eigenschappen() => new()
    {
        IsPersistent = true,
        IssuedUtc = DateTimeOffset.UtcNow,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(365),      // glijdend: de looptijd van een jaar blijft bij verlengen
    };

    // apparaat-id → (nog gekoppeld, wanneer gecontroleerd)
    private static readonly ConcurrentDictionary<int, (bool Ok, DateTime Op)> _gecontroleerd = new();

    /// <summary>CookieAuthenticationEvents.OnValidatePrincipal: een ontkoppeld apparaat wordt buitengesloten.</summary>
    public static async Task ControleerAsync(CookieValidatePrincipalContext ctx)
    {
        if (ctx.Principal is null || ApparaatId(ctx.Principal) is not int id) return;
        if (!_gecontroleerd.TryGetValue(id, out var c) || DateTime.UtcNow - c.Op > TimeSpan.FromMinutes(1))
        {
            var repo = ctx.HttpContext.RequestServices.GetRequiredService<IWerkplekRepository>();
            bool ok;
            try { ok = await repo.ControleerApparaatAsync(id) is not null; }
            catch { ok = c.Op == default || c.Ok; }               // database even weg: de vorige uitkomst houden
            c = (ok, DateTime.UtcNow);
            _gecontroleerd[id] = c;
        }
        if (!c.Ok)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    /// <summary>Na ontkoppelen meteen opnieuw controleren (in plaats van na hoogstens een minuut).</summary>
    public static void Vergeet(int apparaatId) => _gecontroleerd.TryRemove(apparaatId, out _);
}
