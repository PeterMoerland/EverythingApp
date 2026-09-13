namespace EverythingApp.Models;

/// <summary>Waarop de periode wordt afgebakend: de invoerdatum of de factuurdatum.</summary>
public enum FactuurDatumsoort { Invoer, Factuur }

/// <summary>Een ja/nee-filter met "maakt niet uit" ertussen.</summary>
public enum Drieluik { Alles, Nee, Ja }

/// <summary>Eén regel in het overzicht van inkomende facturen.</summary>
public class FactuurRegel
{
    public int Id { get; set; }

    public DateTime? Invoerdatum { get; set; }
    public DateTime? Factuurdatum { get; set; }
    public int Betalingstermijn { get; set; }

    public int? BedrijfId { get; set; }
    public string Bedrijf { get; set; } = "";

    public string Factuurnummer { get; set; } = "";
    public string Omschrijving { get; set; } = "";

    public decimal BedragExBtw { get; set; }
    public decimal Btw { get; set; }
    public decimal Totaal => BedragExBtw + Btw;

    /// <summary>De afkorting van de eigen BV waarop de factuur staat.</summary>
    public string Administratie { get; set; } = "";
    public int? AdministratieId { get; set; }

    /// <summary>Aantal gekoppelde inkooporders.</summary>
    public int PoKoppelingen { get; set; }

    /// <summary>De samengevatte PO-regel, zoals 'PO-123 + PO-124'.</summary>
    public string PoSamenvatting { get; set; } = "";

    public bool Goedgekeurd { get; set; }
    public bool Betaald { get; set; }
    public bool SepaVerzonden { get; set; }
    public bool NaarBasecone { get; set; }
    public bool OpHold { get; set; }

    /// <summary>
    /// Dagen te laat: positief is over de vervaldatum, negatief is nog te gaan.
    /// Een betaalde factuur staat op 0, net als in Access.
    /// </summary>
    public int Overdue { get; set; }

    /// <summary>Rood in de lijst: nog niet betaald en de vervaldag is bereikt.</summary>
    public bool TeLaat => !Betaald && Overdue >= 0;

    public bool IsCreditnota => Totaal < 0;
}

/// <summary>Alles wat het overzicht filtert. Null of Alles betekent: geen beperking.</summary>
public class FactuurFilter
{
    public FactuurDatumsoort Datumsoort { get; set; } = FactuurDatumsoort.Factuur;
    public DateOnly? Van { get; set; }
    public DateOnly? Tot { get; set; }

    /// <summary>
    /// Alleen facturen met meer dagen te laat dan dit. Null = geen beperking.
    /// 0 betekent 'te laat', -7 betekent 'vervalt binnen een week of is te laat'.
    /// </summary>
    public int? VanafOverdue { get; set; }

    public string Bedrijf { get; set; } = "";
    public string Workorder { get; set; } = "";
    public string Zoek { get; set; } = "";

    /// <summary>Afkorting van de eigen BV; leeg = alle administraties.</summary>
    public string Administratie { get; set; } = "";

    public Drieluik Goedgekeurd { get; set; } = Drieluik.Alles;
    public Drieluik Sepa { get; set; } = Drieluik.Alles;
    public Drieluik Betaald { get; set; } = Drieluik.Alles;
}

/// <summary>De cijfers onder de lijst, over de hele selectie.</summary>
public record FactuurTotalen(decimal ExBtw, decimal Btw, int Aantal)
{
    public decimal Totaal => ExBtw + Btw;
    public decimal Gemiddeld => Aantal == 0 ? 0 : ExBtw / Aantal;
}

/// <summary>Eén gekoppelde inkooporder bij een factuur.</summary>
public class FactuurPo
{
    public int LinkId { get; set; }
    public int OrderId { get; set; }

    /// <summary>Het deel van de factuur dat aan deze PO hangt (ex btw).</summary>
    public decimal Bedrag { get; set; }

    /// <summary>Waar de PO zelf op staat: de som van zijn orderregels.</summary>
    public decimal PoBedrag { get; set; }

    public string Project { get; set; } = "";
    public string Werkorder { get; set; } = "";
    public string Door { get; set; } = "";

    /// <summary>Het echte PO-nummer: projectnummer-werkbon-extensie, zoals Access
    /// hem overal opbouwt. Leeg als de order niet meer te vinden is.</summary>
    public string NummerTekst { get; set; } = "";

    public string Nummer => NummerTekst.Length > 0 ? NummerTekst : "PO-" + OrderId;
}

/// <summary>Eén regel van een inkooporder, voor het PO-venster.</summary>
public class PoRegel
{
    public decimal Aantal { get; set; }
    public string Eenheid { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string Artikelcode { get; set; } = "";
    public decimal PrijsPerEenheid { get; set; }
    public decimal Korting { get; set; }
    public decimal Totaal { get; set; }
    public string Voor { get; set; } = "";
}

/// <summary>Een factuur die aan dezelfde PO gekoppeld is.</summary>
public record PoFactuur(int FactuurId, string Factuurnummer, decimal Bedrag, bool Betaald);

/// <summary>Een hele inkooporder, alleen-lezen. Voor het klikken op een PO-nummer.</summary>
public class PoInfo
{
    public int OrderId { get; set; }
    public string Nummer { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string Leverancier { get; set; } = "";
    public string Project { get; set; } = "";
    public string Werkorder { get; set; } = "";
    public int Status { get; set; }
    public string StatusTekst { get; set; } = "";
    public DateTime? Aangemaakt { get; set; }
    public string AangemaaktDoor { get; set; } = "";
    public bool BetalingenGeblokkeerd { get; set; }
    public List<PoRegel> Regels { get; set; } = new();
    public List<PoFactuur> Facturen { get; set; } = new();

    public decimal Totaal => Regels.Sum(x => x.Totaal);
}

/// <summary>Een inkooporder in de keuzelijst van het koppelscherm.</summary>
public record PoKeuze(int OrderId, string Nummer, string Omschrijving, string Werkorder,
                      decimal Totaal, int Status, DateTime? Aangemaakt);

/// <summary>Een werkorder in de keuzelijst (alleen inkoop-werkorders).</summary>
public record WerkorderKeuze(int WerkbonId, string Nummer, string Omschrijving,
                             string Project, bool Open);

/// <summary>Alle velden van één factuur, voor het detailvenster.</summary>
public class Factuur
{
    public int Id { get; set; }

    public int? AdministratieId { get; set; }
    public int? BedrijfId { get; set; }
    public string Bedrijf { get; set; } = "";

    public DateOnly? Factuurdatum { get; set; }
    public DateTime? Invoerdatum { get; set; }
    public string IngevoerdDoor { get; set; } = "";

    public string Factuurnummer { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string ReferentieMde { get; set; } = "";
    public int Betalingstermijn { get; set; } = 30;

    public decimal BedragExBtw { get; set; }
    public decimal Btw { get; set; }
    public decimal Totaal => BedragExBtw + Btw;

    public double Doorbelasten { get; set; } = 100;

    public bool OpHold { get; set; }

    public bool Betaald { get; set; }
    public DateOnly? BetaaldOp { get; set; }
    public string BetaaldDoor { get; set; } = "";

    public bool Goedgekeurd { get; set; }
    public DateTime? GoedgekeurdOp { get; set; }
    public string GoedgekeurdDoor { get; set; } = "";

    public bool SepaVerzonden { get; set; }
    public DateTime? SepaOp { get; set; }
    public string SepaDoor { get; set; } = "";

    public bool NaarBasecone { get; set; }
    public DateTime? BaseconeOp { get; set; }
    public string BaseconeDoor { get; set; } = "";

    /// <summary>Wordt door de app zelf volgeschreven (SEPA-notities, automatisch betaald gezet).</summary>
    public string Opmerking { get; set; } = "";

    public string PoSamenvatting { get; set; } = "";
}

/// <summary>Een eigen werkmaatschappij: waarop staat de factuur.</summary>
public record Administratie(int Id, string Afkorting, string Naam);
