namespace EverythingApp.Models;

/// <summary>Een lokatietype (tabel LocationTypes): Gebouw, Ruimte, Stelling, Krat, …</summary>
public class LokatieType
{
    public int Id { get; set; }
    public string Omschrijving { get; set; } = "";
    public bool StartMetIndex1 { get; set; }        // StartWithIndex1
    public bool UniekeNummering { get; set; }       // UniqueNumbering: nummer over het hele bedrijf (Krat 1, Krat 2, …)
    public bool GebruikNummering { get; set; }      // UseNumbering
    public bool Verplaatsbaar { get; set; }         // Moveable: een drager (krat, pallet)
    public bool Systeem { get; set; }               // System: niet wijzigen of wissen
    public int AantalLokaties { get; set; }
}

/// <summary>Een regel uit LocationChilds: onder een lokatie van type Ouder mag een lokatie van type Kind.</summary>
public record LokatieTypeRegel(int Id, int OuderTypeId, int KindTypeId);

/// <summary>Een soort inhoud (LocationContentsTypes): Voorraad, Projecten, Administratie ME, …</summary>
public record InhoudSoort(int Id, string Omschrijving);

/// <summary>Een lokatie of drager (tabel Locations).</summary>
public class Lokatie
{
    public int Id { get; set; }
    public int? OuderId { get; set; }
    public int? TypeId { get; set; }
    public string Omschrijving { get; set; } = "";
    public string Afkorting { get; set; } = "";
    public string Opmerking { get; set; } = "";
    public int? Index { get; set; }
    public bool Voorraad { get; set; }
    public bool Systeemlokatie { get; set; }
    public int? InhoudSoortId { get; set; }
    public int? WerkorderId { get; set; }
    public string Werkorder { get; set; } = "";     // nummer en omschrijving, voor de weergave
    public string AangemaaktDoor { get; set; } = "";
    public DateTime? AangemaaktOp { get; set; }
    public DateTime? GeprintOp { get; set; }

    public bool Verplaatsbaar { get; set; }         // uit het type

    /// <summary>De code op het label en de scanner: @L plus acht cijfers (Access: PrintLokatieLabel).</summary>
    public string Barcode => "@L" + Id.ToString("D8");
}

/// <summary>Artikelvoorraad op één lokatie (som van VoorraadMutaties, Access: VoorraadPerArtikelPerMagazijn).</summary>
public record LokatieArtikel(int ArtikelId, string Code, string Omschrijving, string Status, decimal Aantal);

/// <summary>Een regel "globale inhoud" van een drager (LocationContents).</summary>
public class LokatieInhoud
{
    public int Id { get; set; }
    public string Omschrijving { get; set; } = "";
    public string Door { get; set; } = "";
    public DateTime? Op { get; set; }
}

/// <summary>
/// Alle lokaties in het geheugen (het zijn er een paar honderd), met de namen en paden zoals Access ze maakt.
/// </summary>
public class LokatieBoom
{
    public Dictionary<int, Lokatie> PerId { get; }
    public Dictionary<int, List<Lokatie>> Kinderen { get; } = new();
    public List<Lokatie> Wortels { get; } = new();

    public LokatieBoom(IEnumerable<Lokatie> lokaties)
    {
        PerId = lokaties.ToDictionary(l => l.Id);
        foreach (var l in PerId.Values.OrderBy(l => l.Index ?? 0).ThenBy(l => l.Omschrijving, StringComparer.CurrentCultureIgnoreCase))
        {
            if (l.OuderId is int o && PerId.ContainsKey(o))
            {
                if (!Kinderen.TryGetValue(o, out var lijst)) Kinderen[o] = lijst = new();
                lijst.Add(l);
            }
            else Wortels.Add(l);
        }
    }

    public List<Lokatie> KinderenVan(int id) => Kinderen.TryGetValue(id, out var k) ? k : new();

    /// <summary>De lokatie en zijn ouders, van de wortel naar beneden (max. 20 niveaus, tegen kringlopen).</summary>
    public List<Lokatie> Keten(int id)
    {
        var keten = new List<Lokatie>();
        var gezien = new HashSet<int>();
        int? huidig = id;
        while (huidig is int h && PerId.TryGetValue(h, out var l) && gezien.Add(h) && keten.Count < 20)
        {
            keten.Insert(0, l);
            huidig = l.OuderId;
        }
        return keten;
    }

    /// <summary>
    /// Korte naam zoals op het label (Access-view LocationNames): een drager heet naar zijn omschrijving,
    /// een vaste lokatie naar de afkortingen van boven naar beneden, met streepjes (MDE-S48-H-B-1-3).
    /// </summary>
    public string Naam(int id)
    {
        if (!PerId.TryGetValue(id, out var l)) return "";
        if (l.Verplaatsbaar) return l.Omschrijving;
        return Afkortingen(Keten(id));
    }

    /// <summary>
    /// Het pad zoals in de details (Access: LocationPath): bij een drager "Krat 12 @ " plus de afkortingen
    /// van de plek waar hij staat, anders alleen de afkortingen.
    /// </summary>
    public string Pad(int id)
    {
        if (!PerId.TryGetValue(id, out var l)) return "";
        var keten = Keten(id);
        if (!l.Verplaatsbaar) return Afkortingen(keten);
        return l.Omschrijving + " @ " + Afkortingen(keten.Take(keten.Count - 1));
    }

    /// <summary>De omschrijvingen van boven naar beneden, voor de broodkruimel.</summary>
    public string Leesbaar(int id, bool zelfErbij = true)
    {
        var keten = Keten(id);
        if (!zelfErbij && keten.Count > 0) keten.RemoveAt(keten.Count - 1);
        return string.Join(" › ", keten.Select(k => k.Omschrijving));
    }

    private static string Afkortingen(IEnumerable<Lokatie> keten)
        => string.Join("-", keten.Select(k => k.Afkorting.Trim()).Where(a => a.Length > 0));

    /// <summary>Ligt <paramref name="kandidaat"/> onder <paramref name="id"/> (of is het dezelfde)?</summary>
    public bool IsOnder(int kandidaat, int id) => Keten(kandidaat).Any(k => k.Id == id);
}
