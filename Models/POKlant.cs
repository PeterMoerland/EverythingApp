namespace EverythingApp.Models;

/// <summary>
/// Een opdracht (PO) van een klant aan MDE: tabel PO_Inkomend, Access-formulier POInkomend.
/// </summary>
public class POKlant
{
    public int Id { get; set; }                       // PO_inkomendID
    public DateOnly? Datum { get; set; }              // PO_Date
    public int? BvId { get; set; }                    // Werkmaatschappij
    public string Bv { get; set; } = "";              // afkorting
    public int? KlantId { get; set; }                 // Customer-id
    public string Klant { get; set; } = "";
    public int? ContactId { get; set; }               // Contactpersoon (Contacts.ID_contact)
    public string Contact { get; set; } = "";
    public string PoNummer { get; set; } = "";        // PO_Klant
    public decimal Bedrag { get; set; }
    public string Omschrijving { get; set; } = "";    // Description (100)
    public string Opmerking { get; set; } = "";       // 400
    /// <summary>Project van de PO (PO_Inkomend.Project-id, PoProject_MariaDB.sql); verplicht. Het PO-bedrag komt als
    /// budgetregel bij dit project (BudgetProject.POklant = deze PO).</summary>
    public int? ProjectId { get; set; }
    public string Project { get; set; } = "";         // nummer · omschrijving
    public DateTime? Ingevoerd { get; set; }          // DatumInvoer
    public string IngevoerdDoor { get; set; } = "";   // CreatedBy → naam

    // voor de lijst
    public int AantalTermijnen { get; set; }
    public decimal Gefactureerd { get; set; }         // som van de termijnen met vinkje 'gefactureerd'

    public List<POTermijn> Termijnen { get; set; } = new();

    public decimal TotaalTermijnen => Termijnen.Sum(t => t.Bedrag);

    /// <summary>Nog niet gefactureerd deel (lijst: op basis van de som uit de database).</summary>
    public decimal Open => Bedrag - Gefactureerd;
}

/// <summary>Een betaaltermijn van een PO: tabel Payments.</summary>
public class POTermijn
{
    public int Id { get; set; }                       // Payment-id; 0 = nieuw
    public string Omschrijving { get; set; } = "";    // Term (50)
    public decimal Bedrag { get; set; }               // Amount (ex BTW)
    public DateOnly? VerwachtOp { get; set; }         // EstimateInvoiceDate
    public bool Gefactureerd { get; set; }            // Invoiced
    public DateOnly? GefactureerdOp { get; set; }     // Invoiced at
    public int? GecontroleerdDoor { get; set; }       // Checked_invoiced (werknemer)
    public bool TeFactureren { get; set; }            // To be invoiced (in Access nergens zichtbaar; blijft bewaard)
    public string Opmerking { get; set; } = "";       // Remark
    public int? FactuurId { get; set; }               // InvoiceID → Invoices
    public string Factuurnummer { get; set; } = "";
}

/// <summary>Een termijn in de facturatie-agenda (Access-rapport Payments_due).</summary>
public record POAgendaRegel(int PoId, int TermijnId, string Klant, string Bv, string PoNummer, string PoOmschrijving,
                            string Termijn, decimal Bedrag, DateOnly? VerwachtOp, string Opmerking);

/// <summary>Een uitgaande factuur om aan een termijn te koppelen.</summary>
public record POFactuurKeuze(int Id, string Nummer, DateOnly? Datum, decimal BedragEx, string Omschrijving, string PoKlant);

public class POFilter
{
    public int? BvId { get; set; } = 5254;            // Access: standaard AUTO
    public string Klant { get; set; } = "";
    public string Zoek { get; set; } = "";
    public DateOnly? Van { get; set; }
    public DateOnly? Tot { get; set; }
    public bool AlleenOpen { get; set; }              // 'Niet volledig gefactureerd' (knop zonder code in Access)
    public int? ProjectId { get; set; }               // alleen de PO's van dit project (Projecten ▸ PO klant)
}
