namespace EverythingApp.Services;

/// <summary>
/// De kop van het bestelformulier, nagebouwd uit 'Order main form' met de echte
/// kolomnamen uit Orders. De regels staan niet hier maar in een eigen tabel op
/// het scherm, want die hebben hun eigen bewerkingen.
/// </summary>
public static class BestelVelden
{
    public static readonly VeldTab[] Tabs =
    {
        new("Algemeen", null,
            new Veld("Leverancier", VeldType.Keuze, new[] { "Supplier" }, Lijst: "Leverancier"),
            new Veld("Contactpersoon", VeldType.Keuze, new[] { "Contact" }, Lijst: "Contact"),
            new Veld("Taal", VeldType.Keuze, new[] { "Language" }, Lijst: "Taal"),
            new Veld("Werkorder", VeldType.Keuze, new[] { "Workorder-id" }, Lijst: "Werkorder"),
            new Veld("Omschrijving", VeldType.Tekst, new[] { "Description" }),
            new Veld("Interne opmerking", VeldType.Meerregelig, new[] { "Remark" }),
            new Veld("Melding op orderformulier", VeldType.Meerregelig, new[] { "Purchasenote" }))
            { EenKolom = true },

        new("Afleveradres", null,
            new Veld("Afleveren bij", VeldType.Keuze, new[] { "AfleverKlant-id" }, Lijst: "Leverancier"),
            new Veld("Ter attentie van", VeldType.Keuze, new[] { "AfleverTAV-id" }, Lijst: "Contact"))
            { EenKolom = true },

        new("Opties", null,
            new Veld("Gewenste leverdatum", VeldType.Datum, new[] { "DefaultLeverDatum" }),
            new Veld("Blokkeer betalingen", VeldType.JaNee, new[] { "Block payments" }),
            new Veld("Mail als de order geheel gereed is", VeldType.JaNee, new[] { "Notify_when_delivered" }),
            new Veld("Mail naar", VeldType.Keuze, new[] { "NotifyTo" }, Lijst: "Werknemer"),
            new Veld("Mail bij nieuwe documenten", VeldType.JaNee, new[] { "NotifyOnDocumentation" }),
            new Veld("Documentmail naar", VeldType.Keuze, new[] { "NotificationTo" }, Lijst: "Werknemer"),
            new Veld("Automatische voorraadorder", VeldType.JaNee, new[] { "AutoVoorraadOrder" }))
            { EenKolom = true },

        new("Voortgang", null,
            new Veld("Aangevraagd op", VeldType.Datum, new[] { "Request date" }, AlleenLezen: true),
            new Veld("Goedgekeurd (1) op", VeldType.Datum, new[] { "Approved1 date" }, AlleenLezen: true),
            new Veld("Goedgekeurd (2) op", VeldType.Datum, new[] { "Approved2 date" }, AlleenLezen: true),
            new Veld("Besteld op", VeldType.Datum, new[] { "Ordered date" }, AlleenLezen: true),
            new Veld("Geleverd op", VeldType.Datum, new[] { "Delivered date" }, AlleenLezen: true),
            new Veld("Gefactureerd op", VeldType.Datum, new[] { "Invoiced date" }, AlleenLezen: true),
            new Veld("Geannuleerd op", VeldType.Datum, new[] { "Canceled date" }, AlleenLezen: true),
            new Veld("Alles is geleverd", VeldType.JaNee, new[] { "AllesIsGeleverd" }),
            new Veld("Alles is gefactureerd", VeldType.JaNee, new[] { "AllesIsGefactureerd" }),
            new Veld("Klaar voor status gereed", VeldType.JaNee, new[] { "KlaarVoorStatusGereed" }))
            { EenKolom = true },
    };

    /// <summary>StoreType in de filestore, zoals de VBA die gebruikt.</summary>
    public const int StoreTypeOrder = 4;        // MasterID = Order-id
    public const int StoreTypeOrderregel = 3;   // MasterID = Orderitem-id

    /// <summary>
    /// Welk datum- en wie-veld hoort bij het VERLATEN van een stap. Zo doet Access
    /// het ook: ga je van 'wacht op goedkeuring' naar de volgende stap, dan wordt
    /// op dat moment 'Approved1 date' gezet — niet bij het binnenkomen.
    ///
    ///   0 = nieuw                → Request date / Requested by
    ///   1 = wacht op goedkeuring → Approved1
    ///   2 = tweede goedkeuring   → Approved2
    ///   3 = te bestellen         → Ordered
    ///   4 = wacht op levering    → Delivered
    ///
    /// 'Invoiced' staat er bewust niet bij: dat vult de facturenmodule.
    /// </summary>
    public static readonly Dictionary<int, (string Datum, string Door)> StatusVelden = new()
    {
        [0] = ("Request date", "Requested by"),
        [1] = ("Approved1 date", "Approved1 by"),
        [2] = ("Approved2 date", "Approved2 by"),
        [3] = ("Ordered date", "Ordered by"),
        [4] = ("Delivered date", "Delivered by"),
    };

    /// <summary>Statusnummer waarmee een bestelling geannuleerd is.</summary>
    public const int StatusGeannuleerd = 7;
}
