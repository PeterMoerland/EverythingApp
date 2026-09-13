namespace EverythingApp.Services;

/// <summary>
/// Indeling van het productenscherm, nagebouwd uit het Access-formulier
/// 'Producten_Main' met de echte kolomnamen uit de tabel Producten.
/// </summary>
public static class ProductVelden
{
    public static readonly VeldTab[] Tabs =
    {
        new("Algemeen", null,
            new Veld("Producttype", VeldType.Keuze, new[] { "ProductNameID" }, Lijst: "ProductNaam"),
            new Veld("Status", VeldType.Keuze, new[] { "ProductStatusID" }, Lijst: "Status"),
            new Veld("Serienummer", VeldType.Tekst, new[] { "Serienummer", "SerieNummer" }),
            new Veld("Bouwjaar", VeldType.Getal, new[] { "Bouwjaar" }),
            new Veld("Werkorder", VeldType.Keuze, new[] { "WorkorderID" }, Lijst: "Werkorder"),
            new Veld("Artikel", VeldType.Keuze, new[] { "ArtikelID" }, Lijst: "Artikel"),
            new Veld("Klant", VeldType.Keuze, new[] { "KlantID" }, Lijst: "Klant"),
            new Veld("Opmerking", VeldType.Meerregelig, new[] { "Opmerking" }))
            { EenKolom = true },
    };

    /// <summary>StoreType in de filestore (afgesproken nummers).</summary>
    public const int StoreTypeProductgroep = 10;   // MasterID = ProductNameID
    public const int StoreTypeProduct = 11;        // MasterID = ProductenID

    /// <summary>
    /// Soorten historieregels uit HistorieTypes. Alleen de twee die de app zelf
    /// gebruikt staan hier; de rest (Programmeren, Storing, Firmware update, Test)
    /// kiest de gebruiker met de hand.
    /// </summary>
    public const int HistorieLevensloop = 1;
    public const int HistorieAttributen = 6;
}
