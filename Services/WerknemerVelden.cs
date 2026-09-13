namespace EverythingApp.Services;

public enum VeldType
{
    Tekst, Meerregelig, Getal, Bedrag, Datum, JaNee, Keuze,
    /// <summary>Afbeelding die als bytes in de kolom staat (bijv. handtekening).</summary>
    Afbeelding,
    /// <summary>Afbeelding waarvan alleen het pad in de kolom staat (bijv. pasfoto op Z:).</summary>
    Fotopad
}

/// <summary>Eén regel in een keuzelijst.</summary>
public record Keuze(int Id, string Naam);

/// <summary>
/// Eén veld op het werknemersscherm. 'Kolommen' zijn kandidaat-kolomnamen in de
/// Werknemers-tabel; de eerste die bestaat wordt gebruikt. Bestaat er geen enkele,
/// dan wordt het veld niet getoond (dus geen fouten bij een afwijkend schema).
/// </summary>
public record Veld(string Label, VeldType Type, string[] Kolommen,
                   bool AlleenLezen = false, string? Lijst = null)
{
    /// <summary>Toont achter een datumveld de berekende leeftijd.</summary>
    public bool ToonLeeftijd { get; init; }
}

/// <summary>Tabblad met velden; Recht bepaalt wie het mag zien (kolomnaam in Werknemers).</summary>
public record VeldTab(string Naam, string? Recht, params Veld[] Velden)
{
    /// <summary>Velden onder elkaar in één kolom (zoals het Access-formulier).</summary>
    public bool EenKolom { get; init; }
}

/// <summary>
/// De indeling van het werknemersscherm — nagebouwd uit het Access-formulier
/// 'Werknemers'. Aanpassen = hier een regel wijzigen.
/// </summary>
public static class WerknemerVelden
{
    public static readonly VeldTab[] Tabs =
    {
        new("Basisgegevens", null,
            new Veld("ID", VeldType.Getal, new[] { "Werknemer-id", "werknemer_id", "werknemerid" }, AlleenLezen: true),
            new Veld("Voornaam", VeldType.Tekst, new[] { "voornaam", "Voornaam" }),
            new Veld("Tussenvoegsel", VeldType.Tekst, new[] { "tussenvoegsel", "Tussenvoegsel" }),
            new Veld("Achternaam", VeldType.Tekst, new[] { "achternaam", "Achternaam" }),
            new Veld("Naam voluit", VeldType.Tekst, new[] { "NaamVoluit", "Naamvoluit", "naamvoluit" }),
            new Veld("Initialen", VeldType.Tekst, new[] { "Afkorting", "afkorting", "Initials", "initialen" }),
            new Veld("Mobiel", VeldType.Tekst, new[] { "Mobiel", "mobiel", "Telefoon mobiel", "gsm" }),
            new Veld("E-mailadres", VeldType.Tekst, new[] { "mail", "Mail", "Email", "email", "Emailadres" }),
            new Veld("Geboortedatum", VeldType.Datum, new[] { "Geboortedatum", "geboortedatum", "Geboorte_datum" })
                { ToonLeeftijd = true },
            new Veld("Pasfoto", VeldType.Fotopad,
                     new[] { "Foto", "foto", "Pasfoto", "pasfoto", "FotoPad", "Afbeelding", "Photo" }))
            { EenKolom = true },

        new("Privéadres", "HRM",
            new Veld("Adres", VeldType.Tekst, new[] { "Adres", "adres", "Straat", "straat" }),
            new Veld("Postcode", VeldType.Tekst, new[] { "Postcode", "postcode" }),
            new Veld("Stad", VeldType.Tekst, new[] { "Stad", "stad", "Woonplaats", "woonplaats", "Plaats" }),
            new Veld("Telefoon privé", VeldType.Tekst, new[] { "Telefoon prive", "TelefoonPrive", "telefoon_prive", "Telefoon" })),

        new("Status", null,
            new Veld("Werkzaam bij", VeldType.Keuze, new[] { "WerkzaamBij", "werkzaambij", "BV", "bv" }, Lijst: "BV"),
            new Veld("Functie", VeldType.Keuze, new[] { "id_functie", "Id_functie", "functie_id" }, Lijst: "Functie"),
            new Veld("Hoofdactiviteit", VeldType.Keuze, new[] { "Hoofdactiviteit", "hoofdactiviteit" }, Lijst: "Activiteit"),
            new Veld("Persoon", VeldType.JaNee, new[] { "Persoon", "persoon" }),
            new Veld("Ingeleend", VeldType.JaNee, new[] { "Inleen", "inleen", "Ingeleend", "ingeleend" }),
            new Veld("Stagiair", VeldType.JaNee, new[] { "Stagiaire", "stagiaire", "Stagiair" }),
            new Veld("Datum in dienst", VeldType.Datum,
                     new[] { "in dienst", "In dienst", "Datum in-dienst", "DatumInDienst", "datum_in_dienst" }),
            new Veld("Uit dienst", VeldType.JaNee, new[] { "Uit dienst", "UitDienst", "uitdienst", "uit_dienst" }),
            new Veld("Datum uit dienst", VeldType.Datum, new[] { "Datum uit-dienst", "DatumUitDienst", "datum_uit_dienst" }),
            new Veld("Bedrijfsauto", VeldType.JaNee, new[] { "CompanyCar", "companycar", "Bedrijfsauto" }),
            new Veld("Betaalpas van de zaak", VeldType.JaNee, new[] { "CompanyCard", "companycard", "Betaalpas" }),
            new Veld("Directie", VeldType.JaNee, new[] { "Directie", "directie" }),
            new Veld("Contracturen", VeldType.Getal, new[] { "Contracturen", "contracturen", "Contract_uren" }),
            new Veld("Burgerlijke staat", VeldType.Tekst, new[] { "Burgerlijke staat", "BurgerlijkeStaat", "burgerlijke_staat" }),
            new Veld("Trouwdatum", VeldType.Datum, new[] { "Trouwdatum", "trouwdatum" })),

        new("Rechten", "HRM",
            new Veld("Budgetverantwoordelijk", VeldType.JaNee, new[] { "Budgetverantwoordelijke", "budgetverantwoordelijke" }),
            new Veld("Aanmaken werkorders", VeldType.JaNee, new[] { "aanmaken_werkorders", "Aanmaken_werkorders" }),
            new Veld("Inkoop toegestaan", VeldType.JaNee, new[] { "Allow purchasing", "AllowPurchasing", "allow_purchasing", "InkoopToegestaan" }),
            new Veld("Mag inkopen tot", VeldType.Bedrag, new[] { "Maginkopentot", "MagInkopenTot", "mag_inkopen_tot", "Inkooplimiet" }),
            new Veld("Authorisator", VeldType.JaNee, new[] { "Authorisator", "authorisator" }),
            new Veld("HRM verantwoordelijk", VeldType.JaNee, new[] { "HRM", "hrm" }),
            new Veld("Financial", VeldType.JaNee, new[] { "Financial", "financial" }),
            new Veld("Mag facturen goedkeuren", VeldType.JaNee, new[] { "GoedkeurenFacturen", "goedkeurenfacturen", "MagFacturenGoedkeuren" }),
            new Veld("ERP developer", VeldType.JaNee, new[] { "ERP_developer", "erp_developer", "ERPdeveloper" }),
            new Veld("Inventor gebruiker", VeldType.JaNee, new[] { "InventorGebruiker", "Inventor_gebruiker", "inventor" }),
            new Veld("Voorraadbeheer", VeldType.JaNee, new[] { "Voorraadbeheer", "voorraadbeheer" }),
            new Veld("Bestellingen", VeldType.JaNee, new[] { "Bestellingen", "bestellingen" }),
            new Veld("Poolmember", VeldType.JaNee, new[] { "Poolmember", "poolmember" })),

        new("Financieel", "HRM",
            new Veld("IBAN", VeldType.Tekst, new[] { "IBAN", "iban", "Bankrekening" }),
            new Veld("BIC", VeldType.Tekst, new[] { "BIC", "bic" }),
            new Veld("Mag management km's declareren", VeldType.JaNee, new[] { "MagManagementKM", "ManagementKM", "mag_management_km" }),
            new Veld("Kilometers woon/werk", VeldType.Getal, new[] { "woonwerkkilometers", "WoonWerkKilometers", "woonwerk_km" }),
            new Veld("Kilometers bijgewerkt tot", VeldType.Datum, new[] { "KilometersBijgewerkt", "Kilometers bijgewerkt tot datum", "km_bijgewerkt" }),
            new Veld("Tekst woon → werk", VeldType.Tekst, new[] { "TextWoonWerk", "textwoonwerk", "text_woon_werk" }),
            new Veld("Tekst werk → woon", VeldType.Tekst, new[] { "TextWerkWoon", "textwerkwoon", "text_werk_woon" })),

        new("Overige", null,
            new Veld("Alarmcode", VeldType.Tekst, new[] { "Alarmcode", "alarmcode", "Alarm code", "AlarmCode" }),
            new Veld("Eigen werkorderlijst gebruiken", VeldType.JaNee, new[] { "personal_orders", "PersonalOrders", "UsesDefaultPersonalWorkorders" }),
            new Veld("Opmerking", VeldType.Meerregelig, new[] { "Opmerking", "opmerking", "Notitie", "notitie" }),
            new Veld("Handtekening", VeldType.Afbeelding,
                     new[] { "handtekening", "Handtekening", "Signature", "signature" })),
    };
}
