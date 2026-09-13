namespace EverythingApp.Models;

/// <summary>Eén bestelling in de lijst links.</summary>
public record BestelRegel(int Id, string Nummer, string Leverancier, string Omschrijving,
                          int Status, string StatusNaam, DateTime? Aangemaakt, decimal Totaal);

/// <summary>Eén regel van een bestelling (Orderitems).</summary>
public class Orderregel
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    public double Aantal { get; set; }
    public int? EenheidId { get; set; }
    public string Artikelcode { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string MdeCode { get; set; } = "";
    public DateOnly? Leverdatum { get; set; }
    public bool Asap { get; set; }

    public decimal? Schatprijs { get; set; }
    public decimal? Prijs { get; set; }
    public decimal? Korting { get; set; }
    public decimal? Totaal { get; set; }

    public string Opmerking { get; set; } = "";
    public bool MailBijLevering { get; set; }
    public int? WerkorderId { get; set; }

    /// <summary>Aantal bijlagen uit de filestore (StoreType 3).</summary>
    public int Bijlagen { get; set; }

    /// <summary>Wat er van deze regel al binnen is (OrderItemsDelivered).</summary>
    public double Geleverd { get; set; }

    public bool Compleet => Geleverd >= Aantal && Aantal > 0;

    /// <summary>
    /// Totaal zoals de database het rekent, of zelf uitgerekend als het leeg is.
    ///
    /// Let op: Discount staat als breuk in de tabel — 0,15 betekent 15% korting.
    /// Is er nog geen echte prijs, dan rekent de VBA met de schattingsprijs, en
    /// dat doen we hier ook.
    /// </summary>
    public decimal Regeltotaal
    {
        get
        {
            if (Totaal is not null) return Totaal.Value;
            return Bereken(Aantal, Prijs ?? Schatprijs, Korting);
        }
    }

    /// <summary>Eén plek voor de rekensom, zodat scherm en database niet uiteenlopen.</summary>
    public static decimal Bereken(double aantal, decimal? prijs, decimal? korting)
        => (decimal)aantal * (prijs ?? 0) * (1 - (korting ?? 0));

    /// <summary>Korting als percentage, want zo praat een mens erover.</summary>
    public decimal? KortingProcent
    {
        get => Korting is null ? null : Korting * 100m;
        set => Korting = value is null ? null : value / 100m;
    }
}

/// <summary>Een stap uit OrderState.</summary>
public record Bestelstatus(int Id, string Omschrijving, string Status, string Status2);
