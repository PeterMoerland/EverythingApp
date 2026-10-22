namespace EverythingApp.Services;

/// <summary>
/// Indeling van het projecten- en werkorderscherm, nagebouwd uit het Access-formulier
/// 'Project/werkorder registratie', met de echte kolomnamen.
///
/// De nummers voor status en doorbelasting staan hieronder op één plek. Blijken ze
/// bij jullie anders te liggen, dan is dat hier één regel aanpassen.
/// </summary>
public static class ProjectVelden
{
    // ---- vaste keuzelijsten (controleer de nummers zo nodig) ----
    public static readonly Keuze[] ProjectStatussen =
    {
        new(1, "Open"), new(2, "Pending"), new(3, "Gesloten"),
    };

    public static readonly Keuze[] DoorbelastBasis =
    {
        new(1, "Budget"), new(2, "Nacalculatie"), new(3, "Geen"),
    };

    /// <summary>Bij werkorders geldt: 0 = gesloten (zo filtert de bestaande code ook).</summary>
    public static readonly Keuze[] WerkorderStatussen =
    {
        new(1, "Open"), new(2, "Pending"), new(0, "Gesloten"),
    };

    // ---- project ----
    public static readonly VeldTab[] ProjectTabs =
    {
        new("Project", null,
            new Veld("Projectnummer", VeldType.Getal, new[] { "Projectnumber" }) { Links = true },
            new Veld("Werkmaatschappij", VeldType.Keuze, new[] { "Werkmaatschappij" }, Lijst: "BV"),
            new Veld("Omschrijving", VeldType.Tekst, new[] { "Projectdescription" }),
            new Veld("Projectleider", VeldType.Keuze, new[] { "ProjectResponsible" }, Lijst: "Werknemer"),
            new Veld("Klant", VeldType.Keuze, new[] { "Customer-id" }, Lijst: "Klant"),
            new Veld("Referentie klant", VeldType.Tekst, new[] { "CustomerReference" }),
            new Veld("Status", VeldType.Keuze, new[] { "ProjectStatus" }, Lijst: "ProjectStatus"),
            new Veld("Doorbelast basis", VeldType.Keuze, new[] { "Doorbelasten" }, Lijst: "DoorbelastBasis"),
            new Veld("Projectsoort", VeldType.Keuze, new[] { "Project_type_id" }, Lijst: "ProjectType"),
            new Veld("Opmerking", VeldType.Meerregelig, new[] { "Remark" }))
            { EenKolom = true },

        new("Inventor", null,
            new Veld("Inventor-project", VeldType.JaNee, new[] { "InventorProject" }),
            new Veld("Exportmap", VeldType.Tekst, new[] { "InventorExportFolder" }),
            new Veld("DWG-pad", VeldType.Tekst, new[] { "InventorDWGpath" }))
            { EenKolom = true },
    };

    // ---- werkorder ----
    public static readonly VeldTab[] WerkorderTabs =
    {
        new("Werkorder", null,
            new Veld("Omschrijving", VeldType.Tekst, new[] { "Werkbonomschrijving" }),
            new Veld("Doel", VeldType.Meerregelig, new[] { "Purpose" }),
            new Veld("Deadline", VeldType.Datum, new[] { "Deadline" }),
            new Veld("Status", VeldType.Keuze, new[] { "WorkOrderStatus" }, Lijst: "WerkorderStatus"),
            new Veld("Werkorderstatus (workflow)", VeldType.Getal, new[] { "Status" }),
            new Veld("Soort werkorder", VeldType.Keuze, new[] { "Workorder type" }, Lijst: "WerkorderType"),
            new Veld("Referentie klant", VeldType.Tekst, new[] { "Klant referentienummer" }),
            new Veld("Referentie medewerker", VeldType.Tekst, new[] { "Klant represenative" }),
            new Veld("Opmerking", VeldType.Meerregelig, new[] { "Opmerking" }))
            { EenKolom = true },

        new("Doorbelasting", null,
            new Veld("Doorbelasting", VeldType.Keuze, new[] { "Doorbelasten" }, Lijst: "DoorbelastBasis"),
            new Veld("Budget", VeldType.Bedrag, new[] { "Budget" }),
            new Veld("Doorbelasten uren %", VeldType.Getal, new[] { "Doorbelast deel" }),
            new Veld("Doorbelasten expenses %", VeldType.Getal, new[] { "Doorbelast_deel_expense_claims" }),
            new Veld("Doorbelasten materialen %", VeldType.Getal, new[] { "Doorbelast_deel_materials" }),
            new Veld("Doorbelasten contract %", VeldType.Getal, new[] { "Doorbelast_deel_contract" }),
            new Veld("Materiaalkosten niet meenemen", VeldType.JaNee, new[] { "MateriaalKostenNietMeenemen" }),
            new Veld("Correctie", VeldType.Getal, new[] { "Correction" }))
            { EenKolom = true },

        new("Kenmerken", null,
            new Veld("Inkoop toegestaan", VeldType.JaNee, new[] { "InkoopToegestaan" }),
            new Veld("Virtueel", VeldType.JaNee, new[] { "VirtualProject" }),
            new Veld("Indirect", VeldType.JaNee, new[] { "Indirect" }),
            new Veld("WBSO", VeldType.JaNee, new[] { "WBSO" }))
            { EenKolom = true },
    };
}
