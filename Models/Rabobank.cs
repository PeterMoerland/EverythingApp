namespace EverythingApp.Models;

/// <summary>
/// Eén banktransactie zoals hij in de tabel banktransacties komt, los van waar hij vandaan
/// komt (CSV van rabobank.nl of de Rabobank-API). Kolomnamen tussen haakjes.
/// </summary>
public class BankRegel
{
    public string Rekening { get; set; } = "";            // Rekening (IBAN, zonder spaties)
    public string Munt { get; set; } = "EUR";
    public string Bic { get; set; } = "";
    public long Volgnummer { get; set; }                   // VolgNr; 0 = onbekend
    public DateTime Datum { get; set; }
    public DateTime? Rentedatum { get; set; }
    public decimal Bedrag { get; set; }
    public decimal SaldoNa { get; set; }                   // Saldo na trn
    public string Tegenrekening { get; set; } = "";
    public string NaamTegenpartij { get; set; } = "";
    public string NaamUiteindelijkePartij { get; set; } = "";
    public string NaamInitierendePartij { get; set; } = "";
    public string BicTegenpartij { get; set; } = "";
    public string Code { get; set; } = "";
    public string BatchId { get; set; } = "";
    public string Referentie { get; set; } = "";           // Transactiereferentie
    public string Machtiging { get; set; } = "";
    public string Incassant { get; set; } = "";
    public string Betalingskenmerk { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string RedenRetour { get; set; } = "";
    public decimal OorspronkelijkBedrag { get; set; }
    public string OorspronkelijkeMunt { get; set; } = "";

    /// <summary>Komt in de kolom Opmerking; 'Rabobank-API' voor wat automatisch is opgehaald.</summary>
    public string Opmerking { get; set; } = "";
}

/// <summary>Een rekening uit de toestemming bij de Rabobank.</summary>
public record RabobankRekening(string Id, string Iban, string Naam);

/// <summary>Wat er bij het laatste ophalen per rekening gebeurde.</summary>
public record RabobankRekeningResultaat(string Iban, int Opgehaald, int Toegevoegd, int AlAanwezig, decimal? Saldo);

/// <summary>De stand van de koppeling, voor het scherm.</summary>
public class RabobankStand
{
    public bool Ingesteld { get; set; }
    public bool Sandbox { get; set; }
    public bool Gekoppeld { get; set; }
    public DateTime? GekoppeldOp { get; set; }
    public DateTime? LaatstOpgehaald { get; set; }
    public DateTime? VolgendeKeer { get; set; }
    public bool Bezig { get; set; }
    public string Fout { get; set; } = "";
    public List<RabobankRekeningResultaat> Rekeningen { get; set; } = new();

    /// <summary>In de sandbox: wat er ingelezen zou worden (er wordt niets weggeschreven).</summary>
    public List<BankRegel> Proef { get; set; } = new();
}
