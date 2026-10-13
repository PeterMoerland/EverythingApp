namespace EverythingApp.Models;

/// <summary>Alles wat op de factuur-PDF en de urenbijlage komt.</summary>
public class FactuurPdfGegevens
{
    public VerkoopFactuur Factuur { get; set; } = new();
    public FactuurAdres Klant { get; set; } = new();
    public FactuurAfzender Afzender { get; set; } = new();

    /// <summary>De losse uren achter de urenregels; leeg = geen bijlage.</summary>
    public List<BijlageUur> Uren { get; set; } = new();

    /// <summary>E-mailadres voor facturen van de klant (Relations.InvoiceMailAddress).</summary>
    public string MailAdres { get; set; } = "";
}

/// <summary>Het factuuradres van de klant (de Invoice…-velden van Relations, anders het gewone adres).</summary>
public class FactuurAdres
{
    public string Naam { get; set; } = "";
    public string Tav { get; set; } = "";
    public string Adres { get; set; } = "";
    public string Postcode { get; set; } = "";
    public string Plaats { get; set; } = "";
    public string Land { get; set; } = "";

    /// <summary>31 = Nederland: dan geen land op de factuur (zoals Access).</summary>
    public int Landnummer { get; set; }
    public string BtwNummer { get; set; } = "";
}

/// <summary>De eigen BV die factureert.</summary>
public class FactuurAfzender
{
    public int Id { get; set; }
    public string Afkorting { get; set; } = "";
    public string Naam { get; set; } = "";
    public string Adres { get; set; } = "";
    public string Postcode { get; set; } = "";
    public string Plaats { get; set; } = "";
    public string Email { get; set; } = "";
    public string Btw { get; set; } = "";
    public string Kvk { get; set; } = "";
    public string Iban { get; set; } = "";
    public string Bic { get; set; } = "";

    /// <summary>Uit Templates/Factuur/bedrijven.json: telefoon, website en dergelijke.</summary>
    public List<string> Regels { get; set; } = new();

    /// <summary>Huiskleur voor lijnen en kopjes, als #rrggbb.</summary>
    public string Kleur { get; set; } = "#333333";

    /// <summary>Het logo als data-URI, zodat de PDF geen bestanden van buiten nodig heeft.</summary>
    public string LogoDataUri { get; set; } = "";
}

/// <summary>Eén uur in de urenbijlage.</summary>
public class BijlageUur
{
    public string Project { get; set; } = "";
    public string Werkorder { get; set; } = "";
    public string WerkorderOmschrijving { get; set; } = "";
    public DateOnly SelectieVan { get; set; }
    public DateOnly SelectieTot { get; set; }
    public string KlantReferentie { get; set; } = "";
    public string KlantContact { get; set; } = "";

    public DateOnly Datum { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? Eind { get; set; }
    public string Medewerker { get; set; } = "";
    public string Uurtype { get; set; } = "";
    public decimal Uren { get; set; }
    public decimal Kilometers { get; set; }
    public string Omschrijving { get; set; } = "";
    public decimal Bedrag { get; set; }
}
