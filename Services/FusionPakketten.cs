using System.Collections.Concurrent;

namespace EverythingApp.Services;

/// <summary>
/// Kortstondige bewaarplaats voor de zip-pakketten van Fusion → CAM.
/// De pagina zet het pakket hier neer en toont een gewone downloadlink naar
/// /api/fusion-pakket/{id}; de browser haalt hem daar zelf op. Downloads
/// via JS (blob + klik) bleken in de praktijk geblokkeerd te worden; een
/// echte link met een echt HTTP-antwoord werkt altijd.
/// </summary>
public class FusionPakketten
{
    private readonly ConcurrentDictionary<Guid, (string Naam, byte[] Inhoud, DateTime Sinds)> _kast = new();

    /// <summary>Zet een pakket klaar en geeft het id voor de downloadlink.</summary>
    public Guid Bewaar(string naam, byte[] inhoud)
    {
        // Oude pakketten opruimen; een uur is ruim genoeg om te downloaden.
        foreach (var (id, p) in _kast)
            if (p.Sinds < DateTime.UtcNow.AddHours(-1))
                _kast.TryRemove(id, out _);

        var nieuw = Guid.NewGuid();
        _kast[nieuw] = (naam, inhoud, DateTime.UtcNow);
        return nieuw;
    }

    public (string Naam, byte[] Inhoud)? Pak(Guid id)
        => _kast.TryGetValue(id, out var p) ? (p.Naam, p.Inhoud) : null;
}
