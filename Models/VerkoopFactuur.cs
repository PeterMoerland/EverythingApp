namespace EverythingApp.Models;

/// <summary>Eén regel in het overzicht van uitgaande (verkoop)facturen, tabel Invoices.</summary>
public class VerkoopFactuurRegel
{
    public int Id { get; set; }

    public string Factuurnummer { get; set; } = "";
    public DateTime? Factuurdatum { get; set; }
    public int Betalingstermijn { get; set; }

    public int? KlantId { get; set; }
    public string Klant { get; set; } = "";

    public string Omschrijving { get; set; } = "";
    public string PoKlant { get; set; } = "";
    public string Opmerking { get; set; } = "";

    /// <summary>Gekoppelde werkorder (Invoices.Workorder-id): nummer, en voor het wolkje klant/project/omschrijving.</summary>
    public string Werkordernummer { get; set; } = "";
    public string WerkorderKlant { get; set; } = "";
    public string WerkorderProject { get; set; } = "";
    public string WerkorderOmschrijving { get; set; } = "";
    public string WerkorderWolkje => Werkordernummer.Length == 0 ? ""
        : $"Klant: {WerkorderKlant}\nProject: {WerkorderProject}\nWerkorder: {WerkorderOmschrijving}";

    public decimal BedragExBtw { get; set; }
    public decimal Btw { get; set; }

    /// <summary>Zoals opgeslagen in Invoices; bij btw verlegd gelijk aan ex.</summary>
    public decimal BedragInclBtw { get; set; }

    /// <summary>De afkorting van de eigen BV die factureert.</summary>
    public string Administratie { get; set; } = "";
    public int? AdministratieId { get; set; }

    public bool Verstuurd { get; set; }
    public DateTime? DatumVerstuurd { get; set; }
    public bool Betaald { get; set; }
    public DateTime? DatumBetaald { get; set; }
    public bool NaarBasecone { get; set; }
    public bool Oninbaar { get; set; }

    /// <summary>0 = gewoon, 1-3 = herinnering, 4 = proforma.</summary>
    public int Herinnering { get; set; }

    /// <summary>
    /// Dagen te laat: positief is over de vervaldatum, negatief is nog te gaan.
    /// Een betaalde factuur staat op 0, net als in Access.
    /// </summary>
    public int Overdue { get; set; }

    /// <summary>Rood in de lijst: verstuurd, niet betaald en de vervaldag voorbij.</summary>
    public bool TeLaat => Verstuurd && !Betaald && !Oninbaar && Overdue > 0;

    public bool IsCreditnota => BedragExBtw < 0;
    public bool IsProforma => Herinnering == 4;
}

/// <summary>Alles wat het overzicht filtert. Null of Alles betekent: geen beperking.</summary>
public class VerkoopFactuurFilter
{
    public DateOnly? Van { get; set; }
    public DateOnly? Tot { get; set; }

    /// <summary>
    /// Alleen facturen met meer dagen te laat dan dit. Null = geen beperking.
    /// 0 is 'te laat', -7 is 'vervalt binnen een week of is te laat' (zoals in Access).
    /// </summary>
    public int? MeerDanOverdue { get; set; }

    public string Klant { get; set; } = "";
    public string Zoek { get; set; } = "";

    /// <summary>Id van de eigen BV; null = alle.</summary>
    public int? AdministratieId { get; set; }

    public Drieluik Verstuurd { get; set; } = Drieluik.Alles;
    public Drieluik Betaald { get; set; } = Drieluik.Alles;
}

/// <summary>De vinkjes die in het overzicht direct te zetten zijn.</summary>
public enum VerkoopFactuurVlag { Betaald, Basecone, Oninbaar }

/// <summary>
/// Een regel van een uitgaande factuur (InvoiceHours). Bij het opslaan worden alleen
/// de velden hieronder geschreven; de rest van de rij (de urenselectie, project,
/// werknemer) blijft staan zoals Access of de urenimport hem zette.
/// </summary>
public class VerkoopFactuurItem
{
    /// <summary>0 = nieuwe regel.</summary>
    public int Id { get; set; }
    public int Volgorde { get; set; }
    public string Werkorder { get; set; } = "";

    /// <summary>ItemDescription: wat er op de factuur staat.</summary>
    public string Omschrijving { get; set; } = "";

    /// <summary>De werkorderomschrijving van een urenregel; alleen tonen.</summary>
    public string WerkorderOmschrijving { get; set; } = "";

    /// <summary>De projectomschrijving van een urenregel; alleen tonen (kopje op de PDF).</summary>
    public string ProjectOmschrijving { get; set; } = "";

    public string Naam { get; set; } = "";
    public string Uurtype { get; set; } = "";
    public decimal? Uren { get; set; }
    public decimal? Kilometers { get; set; }
    public decimal? Aantal { get; set; }
    public decimal? Stuksprijs { get; set; }
    public decimal? Korting { get; set; }
    public decimal Totaal { get; set; }

    /// <summary>
    /// Alleen gevuld bij een regel die net uit de urenimport komt; wordt bij het invoegen
    /// weggeschreven. Met de selectie reserveert het opslaan later precies deze uren.
    /// </summary>
    public UrenHerkomst? Herkomst { get; set; }

    public string Tekst => Omschrijving.Length > 0 ? Omschrijving : WerkorderOmschrijving;

    /// <summary>Een urenregel: de stuksprijs is dan het bedrag van al die uren samen.</summary>
    public bool IsUren => Uren is not null;

    /// <summary>
    /// Het regeltotaal, zoals Access het rekende: uren = prijs × (100 − korting)%,
    /// anders aantal × prijs × (100 − korting)%. Zonder prijs telt het ingevulde totaal.
    /// </summary>
    public decimal Bereken()
    {
        if (Stuksprijs is not decimal prijs) return Totaal;
        var factor = (100 - (Korting ?? 0)) / 100m;
        return IsUren ? prijs * factor : (Aantal ?? 0) * prijs * factor;
    }
}

/// <summary>Waar een geïmporteerde urenregel vandaan komt (de kolommen Selectie… in InvoiceHours).</summary>
public record UrenHerkomst(int ProjectId, string ProjectOmschrijving, int WerkbonId, int WerknemerId,
                           int TariefsoortId, DateOnly Van, DateOnly Tot);

/// <summary>Alle velden van één uitgaande factuur, voor het venster Nieuw/Aanpassen.</summary>
public class VerkoopFactuur
{
    /// <summary>0 = nog niet opgeslagen.</summary>
    public int Id { get; set; }

    public int? AdministratieId { get; set; }

    /// <summary>Bij een nieuwe factuur een voorstel; het echte nummer komt bij het opslaan.</summary>
    public string Factuurnummer { get; set; } = "";

    public int? KlantId { get; set; }
    public string Klant { get; set; } = "";

    public DateOnly? Factuurdatum { get; set; }
    public DateTime? Aangemaakt { get; set; }
    public string AangemaaktDoor { get; set; } = "";
    public int Betalingstermijn { get; set; } = 30;

    public string Referentie { get; set; } = "";
    public string PoKlant { get; set; } = "";

    /// <summary>Gekoppelde werkorder (Invoices.Workorder-id, FactuurWerkorder_MariaDB.sql); intern, niet op de factuur.</summary>
    public int? WerkorderId { get; set; }
    /// <summary>Weergave van de werkorder: nummer · project · omschrijving.</summary>
    public string Werkorder { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string Opmerking { get; set; } = "";

    public bool BtwVerlegd { get; set; }
    public bool Engels { get; set; }

    /// <summary>0 = gewoon, 1-3 = herinnering, 4 = proforma.</summary>
    public int Herinnering { get; set; }

    public bool Betaald { get; set; }
    public DateOnly? DatumBetaald { get; set; }
    public bool Oninbaar { get; set; }
    public bool Verstuurd { get; set; }
    public DateTime? DatumVerstuurd { get; set; }
    public bool NaarBasecone { get; set; }
    public DateTime? BaseconeOp { get; set; }

    public List<VerkoopFactuurItem> Regels { get; set; } = new();

    // ---- de bedragen, zoals Access ze wegschreef ----

    private decimal Som => Regels.Sum(r => r.Bereken());

    public decimal BedragExBtw => Math.Round(Som, 2);

    /// <summary>
    /// Access: Round(Int(totaal * 21) / 100, 2). Int kapt af naar beneden (ook bij
    /// een creditfactuur), dus geen gewone afronding. Zo blijven app en Access gelijk.
    /// </summary>
    public decimal Btw => BtwVerlegd ? 0 : Math.Floor(Som * 21) / 100;

    public decimal BedragInclBtw => BtwVerlegd ? BedragExBtw : Math.Floor(Som * 121) / 100;
}

/// <summary>Wat een klant meebrengt naar een nieuwe factuur.</summary>
public record KlantFactuurStandaard(int Betalingstermijn, bool BtwVerlegd, bool Engels);

/// <summary>Een artikel zoals het op een factuurregel komt.</summary>
public record FactuurArtikel(int Id, string Code, string Omschrijving, decimal Verkoopprijs);

/// <summary>Een banktransactie die aan een uitgaande factuur gekoppeld is.</summary>
public record VerkoopFactuurBetaling(int BanktransactieId, DateTime? Datum, decimal Bedrag,
                                     string Tegenpartij, string Omschrijving);

/// <summary>Een nieuwe uitgaande factuur, alvast ingevuld (bv. vanuit een termijn van een PO van een klant).</summary>
public record FactuurVoorstel(int? BvId, int? KlantId, string Klant, string PoKlant, string Omschrijving,
                              string Regel, decimal Bedrag);
