namespace EverythingApp.Models;

/// <summary>Eén regel in het transactieoverzicht.</summary>
public class Banktransactie
{
    public int Id { get; set; }
    public string Rekening { get; set; } = "";
    public DateTime? Datum { get; set; }
    public decimal Bedrag { get; set; }
    public string Tegenrekening { get; set; } = "";
    public string Tenaamstelling { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string Opmerking { get; set; } = "";
    public int? Pasnummer { get; set; }

    public bool Bij => Bedrag > 0;
}

/// <summary>Alle velden van één transactie, voor het detailvenster.</summary>
public class BanktransactieDetail
{
    public int Id { get; set; }
    public string Rekening { get; set; } = "";
    public string Bic { get; set; } = "";
    public string Munt { get; set; } = "";
    public int? Volgnummer { get; set; }

    public DateTime? Datum { get; set; }
    public DateTime? Rentedatum { get; set; }
    public DateTime? Verwerkingsdatum { get; set; }
    public DateTime? Importdatum { get; set; }
    public string GeimporteerdDoor { get; set; } = "";

    public decimal Bedrag { get; set; }
    public decimal? SaldoNa { get; set; }
    public decimal? Sombedrag { get; set; }
    public decimal? OorspronkelijkBedrag { get; set; }
    public string OorspronkelijkeMunt { get; set; } = "";

    public string Omschrijving { get; set; } = "";
    public string Tegenrekening { get; set; } = "";
    public string NaamTegenpartij { get; set; } = "";
    public string NaamUiteindelijkePartij { get; set; } = "";
    public string NaamInitierendePartij { get; set; } = "";
    public string BicTegenpartij { get; set; } = "";

    public string Code { get; set; } = "";
    public string BatchId { get; set; } = "";
    public string Transactiereferentie { get; set; } = "";
    public string MachtigingsKenmerk { get; set; } = "";
    public string IncassantId { get; set; } = "";
    public string Betalingskenmerk { get; set; } = "";
    public string RedenRetour { get; set; } = "";

    public string Opmerking { get; set; } = "";
    public int? Pasnummer { get; set; }
}

/// <summary>Een bankrekening waar de ingelogde gebruiker bij mag.</summary>
public record Bankrekening(int Id, string Nummer, string Omschrijving, int? BvId)
{
    public string Volledig => string.IsNullOrWhiteSpace(Omschrijving) ? Nummer : $"{Omschrijving} · {Nummer}";
}

/// <summary>De cijfers onder het overzicht.</summary>
public record BankTotalen(decimal Totaal, int Aantal, decimal? SaldoRekening,
                          decimal? SaldoAlle, DateTime? Laatste)
{
    public int? DagenOud => Laatste is null ? null : (int)(DateTime.Today - Laatste.Value.Date).TotalDays;
}
