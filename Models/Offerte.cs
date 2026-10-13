namespace EverythingApp.Models;

/// <summary>
/// De kop van een offerte (tabel Offertes, Access-formulier Offertes ▸ Algemeen, Voorwaarden, Aantekeningen).
/// Zie ..\Offertes_analyse.md.
/// </summary>
public class Offerte
{
    public int Id { get; set; }                        // OfferteID
    public int Nummer { get; set; }                    // OfferteNummer (volgnummer binnen het project)
    public int ProjectId { get; set; }
    public int? Projectnummer { get; set; }
    public string Project { get; set; } = "";          // Projectdescription
    public int? KlantId { get; set; }                  // Projects.Customer-id
    public string Klant { get; set; } = "";
    public int? BvId { get; set; }                     // Projects.Werkmaatschappij
    public string Bv { get; set; } = "";               // afkorting

    public string Omschrijving { get; set; } = "";
    public int Status { get; set; } = OfferteStatus.Aanmaken;
    public int? Taal { get; set; } = OfferteTaal.Nederlands;
    public int? ContactId { get; set; }
    public string OfferteTekst { get; set; } = "";     // koptekst (5000)
    public string Voettekst { get; set; } = "";        // 2000

    // voorwaarden
    public string Geldigheid { get; set; } = "";
    public string Levertijd { get; set; } = "";
    public string Betalingstermijn { get; set; } = "";
    public string Betaling { get; set; } = "";
    public bool AlgemeneVoorwaarden { get; set; } = true;   // Metaalunie-bijlage bij de mail
    public bool Ondertekening { get; set; }
    public string Disclaimer { get; set; } = "";

    public string Aantekeningen { get; set; } = "";    // intern (2000)

    public DateTime? AanmaakDatum { get; set; }
    public string AangemaaktDoor { get; set; } = "";
    public DateTime? GemaildOp { get; set; }
    public string GemaildDoor { get; set; } = "";
    public string Bronofferte { get; set; } = "";

    public OfferteTotalen Totalen { get; set; } = new();

    /// <summary>Zoals @OfferteList: Q223093-32.</summary>
    public string OfferteNr => Offertes.Nummer(Projectnummer, Nummer);

    /// <summary>Vanaf 'Aangeboden' staat alles op slot, behalve de aantekeningen (Access: RefreshSubforms).</summary>
    public bool OpSlot => Status > OfferteStatus.Aanmaken;
}

public class OfferteTotalen
{
    public decimal Uren { get; set; }                  // som uren × tarief
    public decimal AantalUren { get; set; }
    public decimal Materiaal { get; set; }
    public decimal Overigen { get; set; }
    public decimal Totaal => Uren + Materiaal + Overigen;
}

/// <summary>Een offerte in een lijst (bij een project, of als zoekresultaat).</summary>
public record OfferteRegel(int Id, string OfferteNr, string Omschrijving, int Status, DateTime? AanmaakDatum,
                           string Maker, decimal Totaal, int ProjectId, string Project, string Klant);

/// <summary>Een project in de keuzelijst links.</summary>
public record OfferteProject(int Id, int? Nummer, string Omschrijving, int? KlantId, string Klant, int AantalOffertes, bool Open);

public record OfferteContact(int Id, string Naam, string Mail, bool UitDienst);

public static class OfferteStatus
{
    public const int Aanmaken = 1, Aangeboden = 2, Afgewezen = 3, Opdracht = 4;

    public static string Naam(int status) => status switch
    {
        Aanmaken => "Aanmaken",
        Aangeboden => "Aangeboden",
        Afgewezen => "Afgewezen",
        Opdracht => "Opdracht",
        _ => status.ToString(),
    };

    public static string Kleur(int status) => status switch
    {
        Aangeboden => "s-blauw",
        Afgewezen => "s-rood",
        Opdracht => "s-groen",
        _ => "s-grijs",
    };
}

public static class OfferteTaal
{
    public const int Nederlands = 1, Engels = 2;
}

/// <summary>De vier keuzelijsten van Voorwaarden; elk een eigen tabel met (tekst, Taal).</summary>
public enum OfferteLijst { Geldigheid, Levertijd, Betalingstermijn, Betaling }

public static class Offertes
{
    /// <summary>Zoals @OfferteList: 'Q' + projectnummer + '-' + RIGHT('00' + nummer, 2). Vanaf 100 drie cijfers (Access kapte af).</summary>
    public static string Nummer(int? projectnummer, int nummer)
        => $"Q{projectnummer}-{(nummer < 100 ? nummer.ToString("00") : nummer.ToString())}";
}

// ===================== Fase 2: de regels =====================

/// <summary>Alles onder een offerte, in één keer geladen.</summary>
public class OfferteRegels
{
    public List<OfferteCategorie> Categorieen { get; set; } = new();
    public List<OfferteTarief> Tarieven { get; set; } = new();
    public List<OfferteUur> Uren { get; set; } = new();
    public List<OfferteMateriaalRegel> Materiaal { get; set; } = new();
    public List<OfferteOverig> Overigen { get; set; } = new();
}

/// <summary>OfferteCatagorie: groep waarin regels op de PDF staan (alfabetisch op naam).</summary>
public class OfferteCategorie
{
    public int Id { get; set; }
    public string Naam { get; set; } = "";             // Catagorie (45)
    public string Omschrijving { get; set; } = "";     // 250, alleen intern
}

public class OfferteTarief
{
    public int Id { get; set; }
    public string Omschrijving { get; set; } = "";     // 45
    public string Afkorting { get; set; } = "";        // 15, sleutel bij importeren en kopiëren
    public decimal Tarief { get; set; }
}

public class OfferteUur
{
    public int Id { get; set; }
    public int? CategorieId { get; set; }
    public decimal Uren { get; set; }
    public string Omschrijving { get; set; } = "";     // 200
    public int? TariefId { get; set; }
}

public class OfferteMateriaalRegel
{
    public int Id { get; set; }
    public int? CategorieId { get; set; }
    public decimal Aantal { get; set; } = 1;
    public int? ArtikelId { get; set; }                // MDEID → Artikelen.ArtikelID
    public string ArtikelNr { get; set; } = "";        // 45
    public string Omschrijving { get; set; } = "";     // 260
    public decimal Prijs { get; set; }                 // stuksprijs op de offerte
    public decimal Korting { get; set; }               // fractie (0,1 = 10%)
    public int Methode { get; set; }                   // 0 Standaard, 1 Bruto, 2 Vast
    public decimal? InkoopNonMde { get; set; }         // kostprijs bij een regel zonder artikel
    public decimal? ToeslagNonMde { get; set; }        // opslag (fractie) bij een regel zonder artikel

    /// <summary>Alleen lezen: Artikelen.VastgesteldeKostprijs, of anders InkoopNonMde (Access: OfferteMaterialenWinst).</summary>
    public decimal? Kostprijs { get; set; }

    /// <summary>Winst op deze regel: (netto verkoop − kostprijs) × aantal; null zonder kostprijs.</summary>
    public decimal? Winst => Kostprijs is decimal k && k != 0 ? (Prijs * (1 - Korting) - k) * Aantal : null;

    /// <summary>Regeltotaal zoals @OfferteTotaalMateriaal: aantal × prijs × (1 − korting).</summary>
    public decimal Totaal => Aantal * Prijs * (1 - Korting);

    public static string MethodeNaam(int m) => m switch { 1 => "Bruto", 2 => "Vast", _ => "Standaard" };
}

public class OfferteOverig
{
    public int Id { get; set; }
    public int? CategorieId { get; set; }
    public decimal Aantal { get; set; } = 1;
    public string Omschrijving { get; set; } = "";     // 200
    public decimal Prijs { get; set; }
    public decimal Totaal => Aantal * Prijs;
}

/// <summary>Een standaardtarief van de BV dat afwijkt van het tarief in de offerte (Access: "Tarief 'X' updaten?").</summary>
public record TariefVerschil(int OfferteTariefId, string Afkorting, string Omschrijving, decimal Oud, decimal Nieuw);

/// <summary>Wat er bij kopiëren mee gaat; categorieën en tarieven gaan altijd mee (zoals Access).</summary>
public class OfferteKopieOpties
{
    public bool Algemeen { get; set; } = true;
    public bool Uren { get; set; } = true;
    public bool Materiaal { get; set; } = true;
    public bool Overigen { get; set; } = true;
    public bool Voorwaarden { get; set; } = true;
}

// ===================== Fase 3: artikelen en prijzen =====================

/// <summary>Prijsgegevens van een artikel voor de prijsdialoog (Access: OfferteMateriaalSpecialeprijs).</summary>
public class OffertePrijsInfo
{
    public int ArtikelId { get; set; }
    public string Code { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string Leverancier { get; set; } = "";      // leeg = geen leverancier bekend
    public decimal Bruto { get; set; }                 // PrijsPerVE / StuksPerVE
    public decimal KortingLeverancier { get; set; }    // fractie
    public decimal Inkoop { get; set; }                // bruto × (1 − korting leverancier)
    public decimal Kostprijs { get; set; }             // Artikelen.VastgesteldeKostprijs (winstoverzicht)
    public decimal VastePrijs { get; set; }            // Artikelen.[Verkoopprijs project]
}

/// <summary>
/// De prijsberekening (Access: Global.bas GetVerkoopPrijs). Besluit gebruiker: de klantkorting telt één keer.
/// Daarom is 'Prijs' altijd de prijs vóór klantkorting en staat de korting alleen in het regeltotaal
/// (aantal × prijs × (1 − korting)). Access zette bij Standaard/Vast de korting óók al in de prijs.
/// </summary>
public static class OffertePrijs
{
    public const int Standaard = 0, Bruto = 1, Vast = 2;

    /// <summary>Stuksprijs op de offerte, vóór klantkorting. Access rondde af op 2 decimalen.</summary>
    public static decimal Prijs(int methode, decimal inkoop, decimal opslag, decimal bruto, decimal vast) => methode switch
    {
        Bruto => Math.Round(bruto, 2),
        Vast => Math.Round(vast, 2),
        _ => Math.Round(inkoop * (1 + opslag), 2),
    };
}

/// <summary>Een gekozen artikel uit de zoeker: code, omschrijving, kostprijs en verkoopprijs project.</summary>
public record OfferteArtikelKeuze(int Id, string Code, string Omschrijving, decimal Inkoop, decimal Verkoop);

// ===================== Fase 4: PDF en mail =====================

/// <summary>Alles wat op de offerte-PDF komt (Access-rapport OfferteUitgaand).</summary>
public class OffertePdfGegevens
{
    public Offerte Offerte { get; set; } = new();
    public OfferteRegels Regels { get; set; } = new();
    public FactuurAdres Klant { get; set; } = new();        // Relations van de projectklant; Tav = contactpersoon
    public FactuurAfzender Afzender { get; set; } = new();  // de BV van het project
    public string ContactMail { get; set; } = "";
    public OfferteOndertekenaar Ondertekenaar { get; set; } = new();
}

/// <summary>Wie de offerte ondertekent: de gebruiker die hem verstuurt (zoals Access' GetSignOff).</summary>
public class OfferteOndertekenaar
{
    public int? Id { get; set; }
    public string Naam { get; set; } = "";
    public string Functie { get; set; } = "";
    public string Mail { get; set; } = "";
    public string Mobiel { get; set; } = "";
}

/// <summary>Mailteksten en bijlagen uit General_settings (tabblad Offertes in Instellingen).</summary>
public record OfferteMailInstellingen(string TekstNl, string TekstEn, string VoorwaardenNl, string VoorwaardenEn);
