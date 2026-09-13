namespace EverythingApp.Services;

/// <summary>Eén menu-item (knop uit het Access-lint).</summary>
/// <param name="Label">Wat de gebruiker ziet.</param>
/// <param name="Url">Adres in de app; leeg = nog niet gemigreerd.</param>
/// <param name="Recht">Kolomnaam in Werknemers die dit item vrijgeeft; leeg = voor iedereen.</param>
public record MenuItem(string Label, string Url = "", string? Recht = null)
{
    public bool Klaar => !string.IsNullOrWhiteSpace(Url);
}

/// <summary>Groep binnen een module (de vakjes in het Access-lint).</summary>
public record MenuGroep(string Naam, params MenuItem[] Items);

/// <summary>Module = tabblad uit het Access-lint.</summary>
public record MenuModule(string Sleutel, string Naam, string Icoon, params MenuGroep[] Groepen)
{
    /// <summary>
    /// Onderliggende modules. Heeft een module die, dan wordt hij in de zijbalk een
    /// klapper met een pijltje in plaats van een link: je kiest een van de kinderen.
    /// Elk kind heeft zijn eigen landingspagina met tegels.
    /// </summary>
    public MenuModule[] Onder { get; init; } = Array.Empty<MenuModule>();

    public bool HeeftOnder => Onder.Length > 0;

    /// <summary>
    /// Rechtstreeks adres in plaats van een landingspagina met tegels. Voor modules
    /// die uit één scherm bestaan dat je vaak gebruikt — dan scheelt het een klik.
    /// </summary>
    public string? Url { get; init; }

    public string Adres => string.IsNullOrWhiteSpace(Url) ? $"module/{Sleutel}" : Url!.TrimStart('/');
}

/// <summary>
/// De navigatie van de app, één-op-één afgeleid van het Access-lint.
/// Nieuwe schermen aanzetten = hier een Url invullen. Rechten regelen = Recht invullen
/// (kolomnaam in Werknemers); zonder dat recht is het item onzichtbaar.
/// </summary>
public static class Menu
{
    public static readonly MenuModule[] Modules =
    {
        new("werknemers", "Werknemers", "id-badge",
            new MenuGroep("Onderhoud",
                new MenuItem("Werknemers bekijken/wijzigen", "/werknemers")),
            new MenuGroep("Overzichten",
                new MenuItem("Verjaardagen", "/werknemers/kalender/verjaardagen"),
                new MenuItem("Jubilea", "/werknemers/kalender/jubilea"))),

        new("relaties", "Relaties", "users",
            new MenuGroep("Onderhoud",
                new MenuItem("Relaties (klanten en leveranciers)", "/relaties"),
                new MenuItem("Tariefafspraken", "/relaties/tarieven"),
                new MenuItem("Geheimhoudingsovereenkomsten", "/relaties/nda"))),

        new("projecten", "Projecten", "folder",
            new MenuGroep("Formulieren",
                new MenuItem("Projecten en werkorders", "/projecten"),
                new MenuItem("Urenregistratie", "/"),
                new MenuItem("Uren omboeken"),
                new MenuItem("Declaraties"),
                new MenuItem("Kilometers"),
                new MenuItem("Materialen"),
                new MenuItem("Todo")),
            new MenuGroep("Offertes",
                new MenuItem("Offertes wijzigen")),
            new MenuGroep("Planning",
                new MenuItem("Projectplanning", "/planning")),
            new MenuGroep("Rapporten",
                new MenuItem("Overzicht per periode"),
                new MenuItem("Weekoverzicht uren per werknemer"),
                new MenuItem("Project uren overzicht"),
                new MenuItem("Urenoverzicht kruistabel"),
                new MenuItem("WBSO-uren"),
                new MenuItem("Nacalculatie"))),

        new("artikelen", "Artikelen", "package",
            new MenuGroep("Onderhoud",
                new MenuItem("Artikelen", "/artikelen"),
                new MenuItem("Maakdelen", "/maakdelen"),
                new MenuItem("Artikelgroepen"),
                new MenuItem("Artikelsamenstellingen"),
                new MenuItem("Prijslijsten leveranciers")),
            new MenuGroep("Voorraad",
                new MenuItem("Inboeken"),
                new MenuItem("Uitboeken"),
                new MenuItem("Verplaatsen"),
                new MenuItem("Locaties")),
            new MenuGroep("Rapporten",
                new MenuItem("Voorraadstand"),
                new MenuItem("Laatst bestelde artikelen"))),

        new("bestellingen", "Bestellingen", "shopping-cart",
            new MenuGroep("Inkoop",
                new MenuItem("Bestellingen", "/bestellingen"),
                new MenuItem("Orderbatch", "/orderbatch"),
                new MenuItem("Scannen (telefoon)", "/scannen")),
            new MenuGroep("Ontvangst",
                new MenuItem("Materiaal inboeken"),
                new MenuItem("Scan bestelling")),
            new MenuGroep("Rapporten",
                new MenuItem("Openstaande bestellingen"))),

        new("producten", "Producten", "box",
            new MenuGroep("Onderhoud",
                new MenuItem("Producten en serienummers", "/producten"),
                new MenuItem("Opbouw"),
                new MenuItem("Samenstelling"))),

        // Tools is zelf geen pagina maar een klapper; de drie pakketten eronder
        // hebben elk hun eigen landingspagina met tegels.
        new("tools", "Tools", "wrench")
        {
            Onder = new[]
            {
                new MenuModule("inventor", "Inventor", "ruler",
                    new MenuGroep("Onderhoud",
                        new MenuItem("Behandelingen", "/beheer/tabel/inventor-behandelingen"),
                        new MenuItem("Bewerkingen", "/beheer/tabel/inventor-bewerkingen"),
                        new MenuItem("BOM")),
                    new MenuGroep("Weergave",
                        new MenuItem("3D-viewer (STEP)", "/viewer"))),

                new MenuModule("see-electrical", "See Electrical", "zap",
                    new MenuGroep("Onderhoud",
                        new MenuItem("Nog in te vullen"))),

                new MenuModule("fusion", "Fusion", "cpu",
                    new MenuGroep("Bewerkingen",
                        new MenuItem("Converteer naar CAM", "/tools/fusion-cam")),
                    new MenuGroep("Componenten",
                        new MenuItem("Eurocircuits", "/tools/ec-componenten"),
                        new MenuItem("JLCPCB", "/tools/jlc-componenten"))),
            }
        },

        new("financien", "Financiën", "cash",
            new MenuGroep("Facturen",
                new MenuItem("Inkomende facturen", "/financien/facturen", "Financial"),
                new MenuItem("Uitgaande facturen"),
                new MenuItem("Basecone", "/financien/basecone", "Financial"),
                new MenuItem("Tarieven berekenen"),
                new MenuItem("PO klant"),
                new MenuItem("Nacalculatie"),
                new MenuItem("Maandoverzicht")),
            new MenuGroep("Goedkeuren",
                new MenuItem("Facturen goedkeuren", "", "MagFacturenGoedkeuren")),
            new MenuGroep("Bank",
                new MenuItem("Banktransacties", "/financien/banktransacties"),
                new MenuItem("Betalingen klaarzetten"),
                new MenuItem("Bankgegevens exporteren")),
            new MenuGroep("Rapporten",
                new MenuItem("Rapportage facturatie"),
                new MenuItem("Liquiditeitsoverzicht"),
                new MenuItem("Voorraadstand"))),

        // Gebouwbeheer praat met de PLC: licht, zonwering, compressor en verbruik.
        new("gebouw", "Gebouwbeheer", "building")
        {
            Onder = new[]
            {
                new MenuModule("verlichting", "Verlichting", "bulb") { Url = "/gebouw/verlichting" },

                new MenuModule("zonwering", "Zonwering", "sun") { Url = "/gebouw/zonwering" },

                new MenuModule("compressor", "Compressor", "gauge") { Url = "/gebouw/compressor" },

                new MenuModule("cameras", "Camera's", "camera") { Url = "/gebouw/cameras" },

                new MenuModule("alarm", "Alarm", "shield") { Url = "/gebouw/alarm" },

                new MenuModule("energie", "Energie", "flash",
                    new MenuGroep("Overzichten",
                        new MenuItem("Verbruik per dag"),
                        new MenuItem("Verbruik per maand"))),
            }
        },

        new("beheer", "Beheer", "settings",
            new MenuGroep("Tabellen",
                new MenuItem("Functies", "/beheer/functies"),
                new MenuItem("Activiteiten", "/beheer/tabel/activiteiten"),
                new MenuItem("Gebeurtenissoorten", "/beheer/tabel/gebeurtenissoorten"),
                new MenuItem("Pastypes", "/beheer/tabel/pastypes"),
                new MenuItem("Tariefsoorten")),   // staat voorlopig vast
            new MenuGroep("Instellingen",
                new MenuItem("Algemene instellingen", "/beheer/instellingen")),
            new MenuGroep("Systeem",
                new MenuItem("Verbinding & schema", "/diag"),
                new MenuItem("Laptops", "/beheer/laptops"))),
    };

    /// <summary>Alle modules, inclusief de onderliggende (Tools ▸ Inventor enzovoort).</summary>
    public static IEnumerable<MenuModule> Alle()
        => Modules.SelectMany(m => new[] { m }.Concat(m.Onder));

    public static MenuModule? Zoek(string? sleutel) =>
        Alle().FirstOrDefault(m => string.Equals(m.Sleutel, sleutel, StringComparison.OrdinalIgnoreCase));

    /// <summary>De module waaronder deze hangt, of null als het er zelf een is.</summary>
    public static MenuModule? Boven(string? sleutel) =>
        Modules.FirstOrDefault(m => m.Onder.Any(o =>
            string.Equals(o.Sleutel, sleutel, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Alle items met een werkend adres — voor het Ctrl+K-zoekpalet.</summary>
    public static IEnumerable<(string Module, MenuItem Item)> WerkendeItems() =>
        Alle().SelectMany(m => m.Groepen.SelectMany(g => g.Items)
                                        .Where(i => i.Klaar)
                                        .Select(i => (m.Naam, i)));
}
