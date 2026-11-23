using System.Collections.Concurrent;

namespace EverythingApp.Services;

/// <summary>
/// Wie heeft de app nu open, en op welke pagina (wens gebruiker 23-11-2026: een knop in de bovenbalk voor de
/// verantwoordelijke van de app; te zien voor Directie). Elk open tabblad (de layout van een Blazor-verbinding) meldt
/// zich hier met een eigen sleutel, bij het openen en bij elke stap naar een andere pagina, en meldt zich af als het
/// tabblad sluit. Alleen in het geheugen: na een herstart van de app is de lijst leeg tot iedereen weer verbonden is.
///
/// Sluit iemand zijn browser, dan blijft de verbinding nog een paar minuten bestaan voordat de app hem opruimt; zo lang
/// staat hij er nog bij. "Laatste stap" laat zien hoe lang iemand al op dezelfde pagina staat.
/// </summary>
public class Aanwezigheid
{
    /// <summary>Eén open tabblad: wie, waar, sinds wanneer open en wanneer de laatste stap naar een pagina was.</summary>
    public sealed record Scherm(Guid Sleutel, string Naam, int? WerknemerId, bool Werkplek, string Pad, DateTime Sinds, DateTime Laatst);

    private readonly ConcurrentDictionary<Guid, Scherm> _schermen = new();

    /// <summary>Er kwam iemand bij, ging iemand weg of wisselde iemand van pagina.</summary>
    public event Action? Gewijzigd;

    public void Meld(Guid sleutel, string naam, int? werknemerId, bool werkplek, string pad)
    {
        var nu = DateTime.Now;
        _schermen.AddOrUpdate(sleutel,
            _ => new Scherm(sleutel, naam, werknemerId, werkplek, pad, nu, nu),
            (_, oud) => oud with { Naam = naam, WerknemerId = werknemerId, Werkplek = werkplek, Pad = pad, Laatst = nu });
        Seintje();
    }

    public void Weg(Guid sleutel)
    {
        if (_schermen.TryRemove(sleutel, out _)) Seintje();
    }

    public List<Scherm> Lijst() => _schermen.Values.OrderBy(s => s.Naam, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Sinds).ToList();

    /// <summary>Het aantal verschillende mensen (een werkplek en "niet ingelogd" tellen op naam).</summary>
    public int Aantal => _schermen.Values.Select(s => s.WerknemerId is int id && !s.Werkplek ? "w" + id : "n" + s.Naam).Distinct().Count();

    private void Seintje()
    {
        try { Gewijzigd?.Invoke(); }
        catch { /* een scherm dat net sluit mag de rest niet tegenhouden */ }
    }
}
