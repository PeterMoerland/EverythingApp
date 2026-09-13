namespace EverythingApp.Models;

/// <summary>Eén regel in de artikelenlijst.</summary>
public record ArtikelRegel(int Id, string Code, string Omschrijving, string Merk);

/// <summary>Alle velden van een artikel die het scherm laat zien.</summary>
public class Artikel
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string CodeFabrikant { get; set; } = "";
    public string Merk { get; set; } = "";
    public int? MerkId { get; set; }

    public string OmschrijvingNl { get; set; } = "";
    public string OmschrijvingEn { get; set; } = "";
    public string OmschrijvingFr { get; set; } = "";
    public string Opmerking { get; set; } = "";

    public string Eenheid { get; set; } = "";
    public string Ean { get; set; } = "";
    public string OcCodeOmron { get; set; } = "";

    public bool Samenstelling { get; set; }
    public bool Voorraadartikel { get; set; }
    public bool Standaard { get; set; }
    public bool InventorPart { get; set; }
    public bool IsProduct { get; set; }
    public bool Webshop { get; set; }
    public bool Uitleenbaar { get; set; }
    public bool CheckNodig { get; set; }

    // prijzen
    public decimal? Kostprijs { get; set; }
    public decimal? VerkoopprijsProject { get; set; }
    public decimal? VerkoopprijsWebshop { get; set; }
    public decimal? Schattingsprijs { get; set; }

    // voorraad
    public double? MinimumVoorraad { get; set; }
    public double? Bestelhoeveelheid { get; set; }
    public double? AantalInVoorraad { get; set; }
    public double? Gewicht { get; set; }
    public int? AantalUitgeleend { get; set; }

    // maakdeel
    public bool Maakdeel { get; set; }
    public int? Bewerking1 { get; set; }
    public int? Bewerking2 { get; set; }
    public int? Bewerking3 { get; set; }
    public double? PlaatdikteMm { get; set; }
    public string Materiaal { get; set; } = "";
    public string InventorReferentie { get; set; } = "";
    public string InventorBestand { get; set; } = "";
    public bool InventorInkoopdeel { get; set; }

    // wie en wanneer
    public DateTime? Toegevoegd { get; set; }
    public string ToegevoegdDoor { get; set; } = "";
    public DateTime? Gewijzigd { get; set; }
    public string GewijzigdDoor { get; set; } = "";
}

/// <summary>Eén onderdeel in de samenstelling van een artikel.</summary>
public class Samenstellingregel
{
    public int Id { get; set; }
    public int ArtikelId { get; set; }
    public double Aantal { get; set; }
    public string Code { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string Opmerking { get; set; } = "";

    /// <summary>
    /// Bij een gewoon onderdeel de vastgestelde kostprijs; bij een samenstelling
    /// de prijs die uit zijn eigen onderdelen volgt.
    /// </summary>
    public decimal? Kostprijs { get; set; }

    public bool IsSamenstelling { get; set; }

    public decimal Totaal => (decimal)Aantal * (Kostprijs ?? 0);
}

/// <summary>Een groep waar een artikel in zit, met het volledige pad.</summary>
public record ArtikelGroep(int Id, string Naam, string Pad)
{
    public string Volledig => string.IsNullOrWhiteSpace(Pad) ? Naam : Pad;
}

/// <summary>
/// Eén tak in de boom met artikelgroepen. De boom wordt in één keer uit de
/// database gehaald en daarna hier in elkaar gezet; zo is er per klik op een
/// pijltje geen nieuwe vraag aan de database nodig.
/// </summary>
public class Groepknoop
{
    public int Id { get; set; }
    public string Naam { get; set; } = "";

    /// <summary>Het volledige pad, zoals 'Elektro \ PLC \ Omron'.</summary>
    public string Pad { get; set; } = "";

    public int? OuderId { get; set; }
    public int Aantal { get; set; }
    public List<Groepknoop> Kinderen { get; } = new();
}
