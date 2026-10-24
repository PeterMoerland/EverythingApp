namespace EverythingApp.Services;

/// <summary>
/// Werkorders als boom (Workorders.ParentWerkbonID, Werkorders_Boom_MariaDB.sql): subwerkorders onder hun ouder, altijd
/// van hetzelfde project. Gebruikt door de nacalculatie en het uren importeren (wens gebruiker 24-10-2026). Werkt op elke
/// lijst met een id en een ouder-id; een ouder die niet in de lijst staat telt als "geen ouder".
/// </summary>
public static class WerkorderBoom
{
    /// <summary>Diepte-eerst op sleutel (nummer): ouder, dan zijn subwerkorders, dan de volgende.</summary>
    public static List<(T Item, int Diepte)> Ordenen<T>(IEnumerable<T> lijst, Func<T, int> id, Func<T, int?> ouder, Func<T, string> sleutel)
    {
        var alle = lijst.ToList();
        var ids = alle.Select(id).ToHashSet();
        bool HeeftOuder(T w) => ouder(w) is int o && o != id(w) && ids.Contains(o);
        var kinderen = alle.Where(HeeftOuder).GroupBy(w => ouder(w)!.Value)
                           .ToDictionary(g => g.Key, g => g.OrderBy(sleutel, StringComparer.OrdinalIgnoreCase).ToList());
        var uit = new List<(T, int)>();
        var gezien = new HashSet<int>();
        void Voeg(T w, int d)
        {
            if (!gezien.Add(id(w))) return;                      // kring in de data: niet eindeloos
            uit.Add((w, d));
            foreach (var k in kinderen.GetValueOrDefault(id(w)) ?? new List<T>()) Voeg(k, d + 1);
        }
        foreach (var w in alle.Where(w => !HeeftOuder(w)).OrderBy(sleutel, StringComparer.OrdinalIgnoreCase)) Voeg(w, 0);
        foreach (var w in alle) Voeg(w, 0);                      // wat in een kring zat, toch tonen
        return uit;
    }

    /// <summary>Alle werkorders onder deze (kinderen, kleinkinderen …), zonder de werkorder zelf.</summary>
    public static HashSet<int> Nakomelingen<T>(IEnumerable<T> lijst, Func<T, int> id, Func<T, int?> ouder, int van)
    {
        var alle = lijst.ToList();
        var uit = new HashSet<int>();
        var rij = new Queue<int>(new[] { van });
        while (rij.Count > 0)
        {
            var o = rij.Dequeue();
            foreach (var k in alle.Where(w => ouder(w) == o && id(w) != van))
                if (uit.Add(id(k))) rij.Enqueue(id(k));
        }
        return uit;
    }

    /// <summary>De gekozen werkorders plus al hun subwerkorders.</summary>
    public static HashSet<int> MetNakomelingen<T>(IEnumerable<T> lijst, Func<T, int> id, Func<T, int?> ouder, IEnumerable<int> gekozen)
    {
        var alle = lijst.ToList();
        var uit = new HashSet<int>();
        foreach (var g in gekozen) { uit.Add(g); uit.UnionWith(Nakomelingen(alle, id, ouder, g)); }
        return uit;
    }

    /// <summary>De keten van ouders boven deze werkorder, dichtstbijzijnde eerst (voor het teamtarief van de ouder).</summary>
    public static List<int> Voorouders(IReadOnlyDictionary<int, int?> ouderVan, int van)
    {
        var uit = new List<int>();
        var gezien = new HashSet<int> { van };
        while (ouderVan.GetValueOrDefault(van) is int o && gezien.Add(o)) { uit.Add(o); van = o; }
        return uit;
    }
}
