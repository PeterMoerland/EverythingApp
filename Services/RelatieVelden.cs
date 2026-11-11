namespace EverythingApp.Services;

/// <summary>
/// Indeling van het relatiescherm — nagebouwd uit het Access-formulier 'Relations',
/// met de echte kolomnamen uit de database.
/// Kolommen die hier nergens staan verschijnen op het scherm onder 'Overige velden'.
/// </summary>
public static class RelatieVelden
{
    public static readonly VeldTab[] Tabs =
    {
        new("Adres", null,
            new Veld("Naam", VeldType.Tekst, new[] { "name" }),
            new Veld("Alias", VeldType.Tekst, new[] { "Alias" }),
            new Veld("Adres", VeldType.Tekst, new[] { "address" }),
            new Veld("Postcode", VeldType.Tekst, new[] { "PO_box" }),
            new Veld("Plaats", VeldType.Tekst, new[] { "town" }),
            new Veld("Land", VeldType.Keuze, new[] { "country-id" }, Lijst: "Land"),
            new Veld("Telefoon", VeldType.Tekst, new[] { "telephone" }),
            new Veld("Mobiel", VeldType.Tekst, new[] { "telephone_mobile" }),
            new Veld("Fax", VeldType.Tekst, new[] { "fax" }),
            new Veld("Opmerking", VeldType.Meerregelig, new[] { "remark" }))
            { EenKolom = true },

        new("Factuuradres", null,
            new Veld("Apart factuuradres gebruiken", VeldType.JaNee, new[] { "UseInvoiceAddress" }),
            new Veld("Naam", VeldType.Tekst, new[] { "InvoiceName" }),
            new Veld("T.a.v.", VeldType.Tekst, new[] { "InvoiceContactperson1" }),
            new Veld("Adres", VeldType.Tekst, new[] { "InvoiceAddress" }),
            new Veld("Postcode", VeldType.Tekst, new[] { "InvoicePO_box" }),
            new Veld("Plaats", VeldType.Tekst, new[] { "InvoiceTown" }),
            new Veld("Land", VeldType.Keuze, new[] { "InvoiceCountry-id" }, Lijst: "Land"),
            new Veld("Factuur in het Engels", VeldType.JaNee, new[] { "FactuurEngels" }),
            new Veld("Factuur per e-mail (e-invoicing)", VeldType.JaNee, new[] { "UseMailInvoice" }),
            new Veld("Factuur-e-mailadres", VeldType.Tekst, new[] { "InvoiceMailAddress" }))
            { EenKolom = true },

        new("Opties", null,
            new Veld("Is klant", VeldType.JaNee, new[] { "Customer" }),
            new Veld("Extern klantnummer", VeldType.Tekst, new[] { "Klantnummer" }),
            new Veld("Is leverancier", VeldType.JaNee, new[] { "Supplier" }),
            new Veld("Voorkeursleverancier", VeldType.JaNee, new[] { "preferred" }),
            new Veld("Betalingstermijn (dagen)", VeldType.Getal, new[] { "BetalingstermijnKlant" }),
            new Veld("BTW verlegd", VeldType.JaNee, new[] { "BTWverlegd" }),
            new Veld("BTW-nummer", VeldType.Tekst, new[] { "VAT number" }),
            new Veld("KvK-nummer", VeldType.Tekst, new[] { "KvK" }),
            new Veld("E-mailadres algemeen", VeldType.Tekst, new[] { "email" }),
            new Veld("Website", VeldType.Meerregelig, new[] { "internet" }),
            new Veld("IBAN", VeldType.Tekst, new[] { "IBAN" }),
            new Veld("BIC", VeldType.Tekst, new[] { "BIC" }),
            new Veld("E-mail Basecone", VeldType.Tekst, new[] { "BaseconeEmailAdres" }),
            new Veld("Automatisch facturen inlezen", VeldType.JaNee, new[] { "AutomatischeFacturen" }),
            new Veld("Genereer automatisch inkoopopdracht", VeldType.JaNee, new[] { "GenereerOfficieleOrder" }))
            { EenKolom = true },

        // Eigen BV's (wens gebruiker 9-11-2026): de vinkjes met een duidelijke naam, en vervallen BV's. Aandeelhouders
        // en de hele boom staan in Financiën > BV-structuur.
        new("BV", null,
            new Veld("Eigen BV (factureert, eigen administratie)", VeldType.JaNee, new[] { "Werkmaatschappij" }),
            new Veld("Mag bestellen (inkoop)", VeldType.JaNee, new[] { "MagBestellen" }),
            new Veld("Hoort bij de MDE-groep (telt mee in Liquiditeit)", VeldType.JaNee, new[] { "MDEBV" }),
            new Veld("Afkorting", VeldType.Tekst, new[] { "Afkorting" }),
            new Veld("Opgeheven per", VeldType.Datum, new[] { "Opgeheven" }))
            { EenKolom = true },

        new("Inkoop", null,
            // verwijzingen naar een contactpersoon van deze relatie (ID_contact): keuzelijst in plaats van het nummer (11-11-2026)
            new Veld("Aanvraag contactpersoon", VeldType.Keuze, new[] { "OfferteContactID" }, Lijst: "Contactpersoon"),
            new Veld("Inkoop contactpersoon", VeldType.Keuze, new[] { "OrderContact-id" }, Lijst: "Contactpersoon"),
            new Veld("Taal inkooporder", VeldType.Keuze, new[] { "OrderLanguage" }, Lijst: "Taal"),
            new Veld("E-mailadres orders", VeldType.Tekst, new[] { "OrderMailAddress" }),
            new Veld("Gebruik OC-codes", VeldType.JaNee, new[] { "GebruikOCcodes" }),
            new Veld("Minimum orderbedrag", VeldType.Bedrag, new[] { "Minimum order" }),
            new Veld("Minimum ordertoeslag", VeldType.Bedrag, new[] { "Orderkosten" }),
            new Veld("Default levertijd (werkdagen)", VeldType.Getal, new[] { "OrderTime" }),
            new Veld("Next-day toeslag", VeldType.Bedrag, new[] { "NextDayDeliveryFee" }),
            new Veld("Automatische incasso", VeldType.JaNee, new[] { "AutomatischIncasso" }),
            new Veld("Direct betalen bij bestellen", VeldType.JaNee, new[] { "DirectBetalenBijBestellen" }),
            new Veld("Direct geleverd bij bestellen", VeldType.JaNee, new[] { "DirectGeleverdBijBestellen" }),
            new Veld("Default maakdelen", VeldType.JaNee, new[] { "DefaultMaakdelen" }),
            new Veld("Default afleveradres", VeldType.Keuze, new[] { "DefaultVerzendAdres" }, Lijst: "Relatie"),
            new Veld("Default afleveradres contactpersoon", VeldType.Keuze, new[] { "DefaultVerzendContact" }, Lijst: "Contactpersoon"),
            new Veld("CC bij bestelling", VeldType.JaNee, new[] { "CCbijBestelling" }),
            new Veld("CC-e-mailadressen", VeldType.Tekst, new[] { "CCmailadressBijBestelling" }),
            new Veld("Kiloprijs RVS", VeldType.Bedrag, new[] { "KiloprijsRVS" }),
            new Veld("Kiloprijs aluminium", VeldType.Bedrag, new[] { "KiloprijsALU" }),
            new Veld("Kiloprijs staal", VeldType.Bedrag, new[] { "KiloprijsStaal" }),
            new Veld("Instelkosten", VeldType.Bedrag, new[] { "Instelkosten" }),
            new Veld("Gewenst formaat PDF", VeldType.JaNee, new[] { "ExportPDF" }),
            new Veld("Gewenst formaat STP", VeldType.JaNee, new[] { "ExportSTP" }),
            new Veld("Gewenst formaat DXF", VeldType.JaNee, new[] { "ExportDXF" }),
            new Veld("Gewenst formaat STL", VeldType.JaNee, new[] { "ExportSTL" }),
            new Veld("Webpagina-import", VeldType.JaNee, new[] { "WebpageScraping" })),

        new("Kenmerken", null,
            new Veld("Transporteur", VeldType.JaNee, new[] { "Transporter" }),
            new Veld("Handelaar", VeldType.JaNee, new[] { "Trader" }),
            new Veld("Groothandel/voorraad", VeldType.JaNee, new[] { "Warehouse" }),
            new Veld("Samenwerkingsovereenkomst", VeldType.JaNee, new[] { "Cooperate agreement" }),
            new Veld("Zwarte lijst", VeldType.JaNee, new[] { "blacklist" }),
            new Veld("Verbergen in lijsten", VeldType.JaNee, new[] { "Verberg" }),
            new Veld("Uitsluiten bij verlopen", VeldType.JaNee, new[] { "exclude in expired" }),
            new Veld("Eigen apparatuurmap", VeldType.JaNee, new[] { "MakeEquipmentDir" }),
            new Veld("Verantwoordelijke (werknemer)", VeldType.Keuze, new[] { "Responsible" }, Lijst: "Werknemer"),
            // Business_id stond overal op 0; de echte activiteiten staan in ActivityPerRelation (vinkjes, 11-11-2026)
            new Veld("Activiteiten", VeldType.Keuze, new[] { "Business_id" }, Lijst: "Activiteiten"))
            { EenKolom = true },

        new("Reizen", null,
            new Veld("Afstand (km)", VeldType.Getal, new[] { "TravelDistance" }),
            new Veld("Reistijd (min)", VeldType.Getal, new[] { "TravelTime" }))
            { EenKolom = true },
    };

    /// <summary>
    /// Kolommen die niet op het scherm horen: sleutels, technische velden en de
    /// oude losse contactpersoonvelden (die vervangen we door de lijst Contactpersonen).
    /// </summary>
    public static readonly string[] Verbergen =
    {
        "id_supplier",
        "Changed", "last_updated",
        "contactperson1", "contactperson2", "contactperson3",
    };
}
