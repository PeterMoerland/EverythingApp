using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Data access abstraction. Demo mode uses JsonDemoRepository;
/// production will use SqlServerUrenRepository (see SqlServer folder).
/// </summary>
public interface IUrenRepository
{
    Task<List<Werknemer>> GetWerknemersAsync();
    Task<Werknemer?> GetWerknemerAsync(int id);

    /// <summary>Zoekt de werknemer die bij een ingelogd e-mailadres hoort (Google-login).</summary>
    Task<int?> VindWerknemerOpEmailAsync(string email);

    /// <summary>
    /// Alle kolommen van één werknemer als sleutel/waarde-paren. Wordt gebruikt voor
    /// rechten: elke (nieuwe) ja/nee-kolom in Werknemers is direct beschikbaar via
    /// HuidigeGebruiker.Mag("KolomNaam") — zonder de app aan te passen.
    /// </summary>
    Task<Dictionary<string, object?>> GetWerknemerVeldenAsync(int werknemerId);

    /// <summary>Werknemerslijst met de filters van het Access-formulier.</summary>
    Task<List<Werknemer>> ZoekWerknemersAsync(bool inclusiefIngeleend, bool inclusiefUitDienst,
                                              bool alleenPersonen, string? zoek);

    /// <summary>Eén veld van een werknemer opslaan. Alleen bestaande kolommen worden geaccepteerd.</summary>
    Task UpdateWerknemerVeldAsync(int werknemerId, string kolom, object? waarde);

    /// <summary>Welke kolommen bestaan er in Werknemers (voor het generieke scherm).</summary>
    Task<HashSet<string>> WerknemerKolommenAsync();
    Task<List<Workorder>> GetOpenWorkordersAsync();
    Task<Workorder?> GetWorkorderAsync(int id);
    /// <summary>Werkorders bij een lijst id's (open en gesloten), in de volgorde van de lijst; onbekende vallen weg.</summary>
    Task<List<Workorder>> GetWorkordersAsync(IReadOnlyCollection<int> ids);

    // --- werkorder zoeken (vervangt de Access-popups
    //     'Projectreg_zoek_naar_workorder' en 'SelectRecentWorkorder') ---

    /// <summary>Zoekt op nummer, project, klant of omschrijving.</summary>
    Task<List<Workorder>> ZoekWerkordersAsync(string zoek, bool alleenOpen, int max);
    /// <summary>Werkorders van de projecten van deze klant (Projects.Customer-id), nieuwste eerst.</summary>
    Task<List<Workorder>> WerkordersVanKlantAsync(int klantId, bool alleenOpen, int max);

    /// <summary>Werkorders waarop deze werknemer de laatste X dagen uren boekte (recentste eerst).</summary>
    Task<List<Workorder>> RecentGebruikteWerkordersAsync(int werknemerId, int dagen, int max);

    /// <summary>Persoonlijke lijst van deze werknemer (tabel Personal_Orders).</summary>
    Task<List<Workorder>> PersoonlijkeWerkordersAsync(int werknemerId);

    /// <summary>Werkorder toevoegen aan of verwijderen uit de persoonlijke lijst.</summary>
    Task ZetPersoonlijkeWerkorderAsync(int werknemerId, int werkbonId, bool toevoegen);
    Task<List<Tariefsoort>> GetTariefsoortenAsync();
    Task<List<UrenActiviteit>> GetActiviteitenAsync();
    Task<List<SpecialOrder>> GetSpecialOrdersAsync();
    Task<GeneralSettings> GetGeneralSettingsAsync();

    // Planning (Gantt module)
    Task<List<PlanningItem>> GetPlanningAsync();
    Task<PlanningItem> AddPlanningAsync(PlanningItem item);
    Task UpdatePlanningAsync(PlanningItem item);
    Task DeletePlanningAsync(int id);

    // Uren van ALLE werknemers in een periode (voor gerealiseerde uren in de Gantt)
    Task<List<UrenEntry>> GetAlleUrenAsync(DateOnly van, DateOnly tot);

    Task<List<UrenEntry>> GetUrenAsync(int werknemerId, DateOnly van, DateOnly tot);
    Task<List<UrenEntry>> GetUrenVanDagAsync(int werknemerId, DateOnly dag);
    Task<UrenEntry> AddAsync(UrenEntry entry);
    Task UpdateAsync(UrenEntry entry);
    Task DeleteAsync(int urenId);
}
