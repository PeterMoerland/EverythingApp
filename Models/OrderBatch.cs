namespace EverythingApp.Models;

/// <summary>Eén regel in de orderbatch: dit wil iemand bestellen.</summary>
public class OrderBatchRegel
{
    public int Id { get; set; }
    public int Aantal { get; set; }

    public int ArtikelId { get; set; }
    public string Code { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string Eenheid { get; set; } = "";

    public int? LeverancierId { get; set; }
    public string Leverancier { get; set; } = "";

    public int? OrderId { get; set; }
    public string Werkorder { get; set; } = "";

    public string ToegevoegdDoor { get; set; } = "";
    public DateTime? ToegevoegdOp { get; set; }
    public bool ViaScanner { get; set; }
    public string Opmerking { get; set; } = "";

    /// <summary>Kostprijs per stuk uit het artikel; alleen ter indicatie van het totaal.</summary>
    public decimal? StuksPrijs { get; set; }

    public decimal Totaal => Aantal * (StuksPrijs ?? 0);
}

/// <summary>
/// Wat de scanner nodig heeft nadat er een barcode is gelezen: het artikel,
/// de voorkeursleverancier en de verpakkingsgrootte waarop het aantal afrondt.
/// </summary>
public class ScanArtikel
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string Eenheid { get; set; } = "";
    public bool Samenstelling { get; set; }

    public int? LeverancierId { get; set; }
    public string Leverancier { get; set; } = "";

    /// <summary>StuksPerVE van de voorkeursleverancier; 1 als er niets bekend is.</summary>
    public int PerVerpakking { get; set; } = 1;

    public double? Voorraad { get; set; }
    public double? MinimumVoorraad { get; set; }
}
