namespace EverythingApp.Services;

/// <summary>Eén menu-item (knop uit het Access-lint).</summary>
/// <param name="Label">Wat de gebruiker ziet.</param>
/// <param name="Url">Adres in de app; leeg = nog niet gemigreerd.</param>
/// <param name="Recht">Kolomnaam in Werknemers die dit item vrijgeeft; leeg = voor iedereen.</param>
/// <param name="Icoon">Lucide-icoon (wwwroot/iconen) voor de tegel; leeg = het icoon van de module.</param>
public record MenuItem(string Label, string Url = "", string? Recht = null, string? Icoon = null)
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

    /// <summary>
    /// Opent als zwevend venster boven het huidige scherm in plaats van een eigen pagina (Gebouwbeheer: even
    /// tussendoor het licht of het volume regelen zonder je werk te verlaten). De pagina op Url blijft bestaan.
    /// </summary>
    public bool Venster { get; init; }

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
                new MenuItem("Werknemers bekijken/wijzigen", "/werknemers", Icoon: "users"),
                new MenuItem("Mijn pincode (werkplekken)", "/mijn-pincode", Icoon: "key-round")),
            new MenuGroep("Overzichten",
                new MenuItem("Verjaardagen", "/werknemers/kalender/verjaardagen", Icoon: "cake"),
                new MenuItem("Jubilea", "/werknemers/kalender/jubilea", Icoon: "award")),
            new MenuGroep("Lunch",
                new MenuItem("Broodjes bestellen (Kaeswienkeltje)", "/broodjes", Icoon: "sandwich"))),

        new("relaties", "Relaties", "users",
            new MenuGroep("Onderhoud",
                new MenuItem("Relaties (klanten en leveranciers)", "/relaties", Icoon: "building-2"),
                new MenuItem("Bezoekverslagen", "/relaties/bezoekverslagen", Icoon: "notebook-pen"),
                new MenuItem("Welkomstscherm hal", "/relaties/welkomstscherm", "Budgetverantwoordelijke", Icoon: "monitor"),
                new MenuItem("Tariefafspraken", "/relaties/tarieven", Icoon: "handshake"),
                new MenuItem("Geheimhoudingsovereenkomsten", "/relaties/nda", Icoon: "file-lock"))),

        new("projecten", "Projecten", "folder",
            new MenuGroep("Formulieren",
                new MenuItem("Projecten en werkorders", "/projecten", Icoon: "folder-kanban"),
                new MenuItem("Urenregistratie", "/", Icoon: "clock"),
                new MenuItem("Uren omboeken", Icoon: "arrow-left-right"),
                new MenuItem("Declaraties", Icoon: "receipt"),
                new MenuItem("Kilometers", Icoon: "car"),
                new MenuItem("Materialen", Icoon: "package"),
                new MenuItem("Todo", Icoon: "list-todo")),
            new MenuGroep("Offertes en calculaties",
                new MenuItem("Offertes wijzigen", "/projecten/offertes", Icoon: "file-pen"),
                new MenuItem("Calculaties", "/projecten/calculaties", Icoon: "calculator"),
                // stond tot 13-11-2026 onder Bestellingen; het adres is gebleven (links en favorieten)
                new MenuItem("Verkooporders", "/bestellingen/verkooporders", "Verkoop", Icoon: "receipt-text")),
            new MenuGroep("Planning",
                new MenuItem("Projectplanning", "/planning", Icoon: "chart-gantt")),
            new MenuGroep("Rapporten",
                new MenuItem("Overzicht per periode", Icoon: "calendar-range"),
                new MenuItem("Weekoverzicht uren per werknemer", Icoon: "calendar-days"),
                new MenuItem("Project uren overzicht", Icoon: "chart-column"),
                new MenuItem("Urenoverzicht kruistabel", Icoon: "table"),
                new MenuItem("WBSO-uren", Icoon: "flask-conical"),
                new MenuItem("Nacalculatie", "/financien/nacalculatie", "Financial", Icoon: "calculator"))),

        // Service: bij een storing zien wie aan het project werkte (verzoek van Michiel, 11-11-2026).
        new MenuModule("service", "Service", "life-buoy") { Url = "/service" },

        // Activity board: per project problemen op prioriteit en activiteiten op een kanbanbord (spec Freek Moerland).
        new MenuModule("activityboard", "Activity board", "kanban") { Url = "/activityboard" },

        new("artikelen", "Artikelen", "package",
            new MenuGroep("Onderhoud",
                new MenuItem("Artikelen", "/artikelen", Icoon: "package"),
                new MenuItem("Samenstellingen", "/artikelen/samenstellingen", Icoon: "boxes"),
                new MenuItem("Maakdelen", "/maakdelen", Icoon: "cog"),
                new MenuItem("Artikelgroepen", Icoon: "folder-tree"),
                new MenuItem("Artikelsamenstellingen", Icoon: "layers"),
                new MenuItem("Prijslijsten leveranciers", Icoon: "tags")),
            new MenuGroep("Rapporten",
                new MenuItem("Laatst bestelde artikelen", Icoon: "history"))),

        // Voorraad: lokaties en dragers (krat, pallet) als boom, en straks de mutaties.
        new("voorraad", "Voorraad", "archive",
            new MenuGroep("Voorraad",
                // Inboeken, uitboeken en verplaatsen gebeuren in Artikelvoorraad zelf; de losse tegels (die naar
                // hetzelfde scherm wezen) zijn op verzoek van de gebruiker weggehaald (19-10-2026).
                new MenuItem("Artikelvoorraad", "/voorraad/artikelen", Icoon: "warehouse")),
            new MenuGroep("Lokaties",
                new MenuItem("Dragers", "/voorraad/dragers", Icoon: "container"),
                new MenuItem("Lokatiestructuur", "/voorraad/lokatiestructuur", Icoon: "network"),
                new MenuItem("Lokaties", "/voorraad/lokaties", Icoon: "map-pin"))),
            // Voorraadstand staat in Financiën ▸ Rapporten (kostprijzen); de grijze tegel hier is weg (19-10-2026).

        new("bestellingen", "Bestellingen", "shopping-cart",
            new MenuGroep("Inkoop",
                new MenuItem("Bestellingen", "/bestellingen", Icoon: "shopping-cart"),
                new MenuItem("Orderbatch", "/orderbatch", Icoon: "package-plus"),
                new MenuItem("Scannen (telefoon)", "/scannen", Icoon: "scan-barcode"),
                // je eigen bestellingen die te lang op Aanmaken of In bestelling staan (verzoek 13-11-2026)
                new MenuItem("Niet doorgezet", "/bestellingen/niet-doorgezet", Icoon: "hourglass")),
            new MenuGroep("Ontvangst",
                new MenuItem("Materiaal inboeken", "/bestellingen/ontvangst", Icoon: "package-check"),
                new MenuItem("Scan bestelling", Icoon: "scan-line")),
            new MenuGroep("Verzenden",
                new MenuItem("Pakbonnen", "/bestellingen/pakbonnen", Icoon: "truck")),
            new MenuGroep("Rapporten",
                new MenuItem("Openstaande bestellingen", Icoon: "clipboard-list"))),

        new("producten", "Producten", "box",
            new MenuGroep("Onderhoud",
                new MenuItem("Producten en serienummers", "/producten", Icoon: "barcode"),
                new MenuItem("Opbouw", Icoon: "hammer"),
                new MenuItem("Samenstelling", Icoon: "component")),
            new MenuGroep("Overzichten",
                new MenuItem("Todo-overzicht", "/producten/todo", Icoon: "list-checks"))),

        // Productie: halffabricaten (prints, motoren, kabels) opbouwen, uitlezen,
        // programmeren en testen; producten opbouwen en duurtesten — gekoppeld aan
        // de exemplaren in Producten. (Motoren gaan via Half-fabricaten; het scherm
        // /productie/motor bestaat nog als rechtstreekse ingang.)
        new("productie", "Productie", "zap",
            new MenuGroep("Opbouw en test",
                new MenuItem("Half-fabricaten", "/productie/test-pcb", Icoon: "circuit-board"),
                new MenuItem("Producten", "/productie/product", Icoon: "bot"),
                new MenuItem("EtherCAT IO", "/productie/ethercat-io", Icoon: "cable")),
            new MenuGroep("Onderhoud",
                new MenuItem("Controllertypen en firmware", "/productie/drivetypen", Icoon: "cpu"))),

        // Tools is zelf geen pagina maar een klapper; de drie pakketten eronder
        // hebben elk hun eigen landingspagina met tegels.
        new("tools", "Tools", "wrench")
        {
            Onder = new[]
            {
                new MenuModule("inventor", "Inventor", "ruler",
                    new MenuGroep("Onderhoud",
                        new MenuItem("Behandelingen", "/beheer/tabel/inventor-behandelingen", Icoon: "paint-bucket"),
                        new MenuItem("Bewerkingen", "/beheer/tabel/inventor-bewerkingen", Icoon: "wrench"),
                        new MenuItem("BOM", Icoon: "list-tree")),
                    new MenuGroep("Weergave",
                        new MenuItem("3D-viewer (STEP)", "/viewer", Icoon: "box")),
                    new MenuGroep("Installeren",
                        new MenuItem("Add-in installeren", "/tools/inventor-addin", Icoon: "download"))),

                // de werkplek-agent (uitlezen, programmeren, testen); hier en niet onder Productie, want dat menu
                // verschijnt pas als er al een agent draait
                new MenuModule("mde-agent", "MDE Agent", "plug-zap",
                    new MenuGroep("Installeren",
                        new MenuItem("MDE Agent installeren", "/tools/mde-agent", Icoon: "download")))
                { Url = "/tools/mde-agent" },

                new MenuModule("see-electrical", "See Electrical", "zap",
                    new MenuGroep("Onderhoud",
                        new MenuItem("Nog in te vullen", Icoon: "circle-help"))),

                // kabels op maat samenstellen voor de kabelleverancier LDZY (wens gebruiker 13-11-2026).
                // Met een eigen adres, anders verbergt het menu hem (een onderdeel zonder werkend scherm is onzichtbaar).
                new MenuModule("kabelconfigurator", "Kabelconfigurator", "cable",
                    new MenuGroep("Kabels",
                        new MenuItem("Kabelconfigurator", "/tools/kabelconfigurator", Icoon: "cable"),
                        // de eigen lijst van connectortypes (algemene beheertabel); ook te openen vanuit de configurator
                        new MenuItem("Connectortypes (kabels)", "/beheer/tabel/kabel-connectoren", Icoon: "plug-zap")))
                { Url = "/tools/kabelconfigurator" },

                new MenuModule("fusion", "Fusion", "cpu",
                    new MenuGroep("Bewerkingen",
                        new MenuItem("Converteer naar CAM", "/tools/fusion-cam", "Fusion", Icoon: "drill")),
                    new MenuGroep("Componenten",
                        new MenuItem("Eurocircuits", "/tools/ec-componenten", "Fusion", Icoon: "circuit-board"),
                        new MenuItem("JLCPCB", "/tools/jlc-componenten", "Fusion", Icoon: "microchip")),
                    new MenuGroep("Installeren",
                        new MenuItem("Add-in installeren", "/tools/fusion-addin", "Fusion", Icoon: "download"))),
            }
        },

        new("financien", "Financiën", "cash",
            new MenuGroep("Facturen",
                new MenuItem("Inkomende facturen", "/financien/facturen", "Financial", Icoon: "file-down"),
                new MenuItem("Uitgaande facturen", "/financien/uitgaande-facturen", "Financial", Icoon: "file-up"),
                new MenuItem("Te laat betaald", "/financien/te-laat", "Financial", Icoon: "clock-alert"),
                new MenuItem("Basecone", "/financien/basecone", "Financial", Icoon: "cloud-upload"),
                new MenuItem("Tarieven berekenen", "/financien/tarieven", "Financial", Icoon: "calculator"),
                new MenuItem("PO klant", "/financien/po-klant", "Financial", Icoon: "file-check"),
                new MenuItem("Nacalculatie", "/financien/nacalculatie", "Financial", Icoon: "calculator"),
                new MenuItem("Maandoverzicht", Icoon: "calendar")),
            new MenuGroep("Goedkeuren",
                new MenuItem("Facturen goedkeuren", "", "MagFacturenGoedkeuren", Icoon: "badge-check")),
            new MenuGroep("Bank",
                new MenuItem("Banktransacties", "/financien/banktransacties", Icoon: "landmark"),
                new MenuItem("Bankrekeningen", "/financien/bankrekeningen", "Financial", Icoon: "wallet"),
                new MenuItem("Betalingen klaarzetten", "/financien/betalingen-klaarzetten", "Financial", Icoon: "send"),
                new MenuItem("Grootboekrekeningen", "/financien/grootboekrekeningen", "Financial", Icoon: "list-tree")),
            new MenuGroep("Rapporten",
                new MenuItem("Rapportage facturatie", Icoon: "chart-line"),
                new MenuItem("Vaste lasten", "/financien/vaste-lasten", "Financial", Icoon: "repeat"),
                new MenuItem("Voorraadstand", "/financien/voorraadstand", "Financial", Icoon: "warehouse")),
            // Analyse: overzichten en hulpmiddelen om mee te rekenen; hier komen ook de tools van een collega bij
            new MenuGroep("Analyse",
                new MenuItem("Liquiditeitsoverzicht", "/financien/liquiditeit", "Financial", Icoon: "trending-up"),
                new MenuItem("Vragen aan AI", "/financien/analyse", "Financial", Icoon: "sparkles")),
            // Eigen BV's en hun aandeelhouders (9-11-2026, voorbereiding op meerdere BV's)
            new MenuGroep("Organisatie",
                new MenuItem("BV-structuur", "/financien/bv-structuur", "Financial", Icoon: "network"),
                new MenuItem("Verzekeringen", "/financien/verzekeringen", "Financial", Icoon: "shield-check"))),

        // Gebouwbeheer praat met de PLC: licht, zonwering, compressor en verbruik.
        new("gebouw", "Gebouwbeheer", "building")
        {
            Onder = new[]
            {
                new MenuModule("verlichting", "Verlichting", "bulb") { Url = "/gebouw/verlichting", Venster = true },

                new MenuModule("zonwering", "Zonwering", "sun") { Url = "/gebouw/zonwering", Venster = true },

                new MenuModule("compressor", "Compressor", "gauge") { Url = "/gebouw/compressor", Venster = true },

                new MenuModule("sonos", "Sonos", "volume") { Url = "/gebouw/sonos", Venster = true },

                new MenuModule("cameras", "Camera's", "camera") { Url = "/gebouw/cameras", Venster = true },

                new MenuModule("alarm", "Alarm", "shield") { Url = "/gebouw/alarm", Venster = true },

                new MenuModule("energie", "Energie", "flash",
                    new MenuGroep("Overzichten",
                        new MenuItem("Verbruik per dag", Icoon: "chart-column"),
                        new MenuItem("Verbruik per maand", Icoon: "chart-column-big"))),
            }
        },

        new("beheer", "Beheer", "settings",
            new MenuGroep("Tabellen",
                new MenuItem("Functies", "/beheer/functies", Icoon: "briefcase"),
                new MenuItem("Activiteiten", "/beheer/tabel/activiteiten", Icoon: "activity"),
                new MenuItem("Gebeurtenissoorten", "/beheer/tabel/gebeurtenissoorten", Icoon: "tags"),
                new MenuItem("Pastypes", "/beheer/tabel/pastypes", Icoon: "credit-card"),
                new MenuItem("Projectrollen", "/beheer/tabel/projectrollen", Icoon: "user-cog"),
                new MenuItem("Tariefsoorten", Icoon: "euro")),   // staat voorlopig vast
            new MenuGroep("Instellingen",
                new MenuItem("Algemene instellingen", "/beheer/instellingen", Icoon: "settings"),
                new MenuItem("Labelprinter (deze pc)", "/beheer/labelprinter", Icoon: "printer"),
                // gesproken meldingen op de Sonos-speakers: nu (PA) en op vaste tijden (wens gebruiker 13-11-2026)
                new MenuItem("Omroep (Sonos)", "/beheer/omroep", Icoon: "bell-ring")),
            new MenuGroep("Verbeteren",
                new MenuItem("Verbeterverzoeken (💡)", "/verbeterverzoeken", Icoon: "lightbulb"),
                new MenuItem("Wat is er nieuw", "/nieuw", Icoon: "star")),
            new MenuGroep("Systeem",
                new MenuItem("Verbinding & schema", "/diag", Icoon: "database"),
                new MenuItem("Filestore", "/beheer/filestore", Icoon: "hard-drive"),
                new MenuItem("Computers", "/beheer/computers", Icoon: "computer"),
                new MenuItem("Werkplekken", "/beheer/werkplekken", Icoon: "laptop"),
                new MenuItem("Nieuwe versie aankondigen", "/beheer/onderhoud", Icoon: "megaphone"))),
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
