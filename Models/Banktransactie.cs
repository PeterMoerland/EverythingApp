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
    /// <summary>Aantal gekoppelde facturen (banktransacties_facturen_crossref), inkomend en uitgaand.</summary>
    public int Facturen { get; set; }
    /// <summary>De grootboekrekening (banktransacties.GrootboekID, Grootboekrekeningen_MariaDB.sql) en de code ervan.</summary>
    public int? GrootboekId { get; set; }
    public string Grootboek { get; set; } = "";
    public string GrootboekNaam { get; set; } = "";

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
    /// <summary>Relatie(s) met de tegenrekening als IBAN (Relations.IBAN), leeg = onbekend.</summary>
    public string TegenrekeningRelatie { get; set; } = "";
    /// <summary>De BV van de eigen rekening (afkorting).</summary>
    public string RekeningBv { get; set; } = "";
    /// <summary>De grootboekrekening van dit afschriftrecord (leeg = geen of nog geen kolom).</summary>
    public int? GrootboekId { get; set; }
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
/// <summary>Een rekening in het beheerscherm Bankrekeningen, met wat er aan hangt.</summary>
public class BankrekeningBeheer
{
    public int Id { get; set; }
    public string Nummer { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public int? BvId { get; set; }
    public string Bv { get; set; } = "";
    public int Transacties { get; set; }
    public int Passen { get; set; }
    public int Negeren { get; set; }               // BankReferentieNegeren
    public List<int> Rechten { get; set; } = new(); // werknemers die de rekening mogen zien

    /// <summary>Wissen kan alleen zonder transacties, passen en genegeerde referenties (sleutels in de database).</summary>
    public bool Wisbaar => Transacties == 0 && Passen == 0 && Negeren == 0;
}

/// <summary>Een rekening waar iemand bij mag; Bv = afkorting (of naam) van de BV (Bankrekeningen.BVid), leeg = geen BV.</summary>
public record Bankrekening(int Id, string Nummer, string Omschrijving, int? BvId, string Bv = "")
{
    public string Volledig => string.IsNullOrWhiteSpace(Nummer) ? $"{Omschrijving} · nog geen nummer"
                            : string.IsNullOrWhiteSpace(Omschrijving) ? Nummer : $"{Omschrijving} · {Nummer}";
}

/// <summary>De cijfers onder het overzicht.</summary>
public record BankTotalen(decimal Totaal, int Aantal, decimal? SaldoRekening,
                          decimal? SaldoAlle, DateTime? Laatste)
{
    public int? DagenOud => Laatste is null ? null : (int)(DateTime.Today - Laatste.Value.Date).TotalDays;
}
