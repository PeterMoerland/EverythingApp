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
    private bool _geladen;
    private Dictionary<string, object?> _velden = new(StringComparer.OrdinalIgnoreCase);

    public HuidigeGebruiker(AuthenticationStateProvider auth, IUrenRepository repo)
    {
        _auth = auth;
        _repo = repo;
    }

    /// <summary>Is er iemand ingelogd (Google)?</summary>
    public bool Ingelogd { get; private set; }

    /// <summary>E-mailadres uit het Google-account.</summary>
    public string Email { get; private set; } = "";

    /// <summary>Naam zoals Google die kent (valt terug als er geen werknemer gevonden is).</summary>
    public string GoogleNaam { get; private set; } = "";

    /// <summary>De gekoppelde werknemer, of null als het account niet aan een werknemer hangt.</summary>
    public int? WerknemerId { get; private set; }

    /// <summary>Naam van de gekoppelde werknemer (NaamVoluit), anders de Google-naam.</summary>
    public string Naam { get; private set; } = "";

    /// <summary>Is het account aan een werknemer gekoppeld?</summary>
    public bool Gekoppeld => WerknemerId is not null;

    /// <summary>Alle velden van de werknemer — handig om te controleren welke rechten er zijn.</summary>
    public IReadOnlyDictionary<string, object?> Velden => _velden;

    /// <summary>Eén keer per sessie ophalen. Veilig om vaker aan te roepen.</summary>
    public async Task ZorgGeladenAsync()
    {
        if (_geladen) return;
        _geladen = true;
        try
        {
            var status = await _auth.GetAuthenticationStateAsync();
            var gebruiker = status.User;
            if (gebruiker?.Identity?.IsAuthenticated != true) return;

            Ingelogd = true;
            GoogleNaam = gebruiker.Identity.Name ?? "";
            Naam = GoogleNaam;
            Email = gebruiker.FindFirst(ClaimTypes.Email)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(Email)) return;

            var id = await _repo.VindWerknemerOpEmailAsync(Email);
            if (id is null) return;

            WerknemerId = id;
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
        if (!_velden.TryGetValue(recht, out var waarde) || waarde is null) return false;
        if (waarde is bool b) return b;
        try { return Convert.ToInt64(waarde) != 0; }
        catch { return false; }
    }

    /// <summary>Mag minstens één van deze rechten.</summary>
    public bool MagEenVan(params string[] rechten) => rechten.Any(Mag);
}
