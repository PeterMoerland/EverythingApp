using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Tijdlijn, relaties en documenten van werknemers (de nieuwe tabellen uit
/// Werknemers_Schema_MariaDB.sql). Apart van IUrenRepository gehouden, zodat de
/// demo-modus niet alles hoeft na te bouwen.
/// </summary>
public interface IWerknemerRepository
{
    bool Beschikbaar { get; }

    // --- tijdlijn ---
    Task<List<GebeurtenisSoort>> GetSoortenAsync();
    Task<List<Gebeurtenis>> GetTijdlijnAsync(int werknemerId, bool inclusiefPrive);
    Task<int> BewaarGebeurtenisAsync(Gebeurtenis g);   // id = 0 -> nieuw
    Task VerwijderGebeurtenisAsync(int gebeurtenisId);

    // --- relaties (partner, kinderen) ---
    Task<List<RelatieSoort>> GetRelatieSoortenAsync();
    Task<List<Relatie>> GetRelatiesAsync(int werknemerId);
    Task<int> BewaarRelatieAsync(Relatie r);
    Task VerwijderRelatieAsync(int relatieId);

    // --- documenten uit de bestaande FileStore ---
    Task<List<Bestand>> GetBestandenAsync(int storeType, int masterId);

    // --- welke foto is de hoofdfoto (tabel Werknemer_Hoofdfoto) ---
    Task<int?> GetHoofdfotoAsync(int werknemerId);
    Task ZetHoofdfotoAsync(int werknemerId, int fileStoreId);

    // --- passen (Financieel-tabblad): laadpas, betaalpas, creditcard ---
    Task<List<Pas>> GetPassenAsync(int werknemerId, bool inclusiefOpgeheven);
    Task<int> BewaarPasAsync(Pas p);
    Task<List<Keuze>> GetPasTypesAsync();
    Task<List<Keuze>> GetBankrekeningenAsync();

    // --- beheer: functies (Werknemers_Functie) ---
    Task<List<Functie>> GetFunctieBeheerAsync();
    Task<int> BewaarFunctieAsync(Functie f);
    /// <summary>Reden waarom wissen niet kan, of null als het mag.</summary>
    Task<string?> WissenGeblokkeerdAsync(int functieId);
    Task VerwijderFunctieAsync(int functieId);
    Task<List<string>> WerknemersMetFunctieAsync(int functieId);

    /// <summary>Geboortedatums en indienstdatums van werknemers in dienst.</summary>
    Task<List<Mijlpalen>> GetMijlpalenAsync();

    // --- beheer: generieke tabellen (BeheerTabellen) ---
    Task<List<Dictionary<string, object?>>> LeesBeheerTabelAsync(BeheerTabel def);
    Task BewaarBeheerRijAsync(BeheerTabel def, int id, Dictionary<string, object?> waarden);
    Task<string?> BeheerRijInGebruikAsync(BeheerTabel def, int id);
    Task VerwijderBeheerRijAsync(BeheerTabel def, int id);

    // --- keuzelijsten voor het Status-tabblad ---
    /// <summary>Eigen BV's: Relations waarbij MDEbv aan staat.</summary>
    Task<List<Keuze>> GetBVsAsync();
    /// <summary>Functies uit Werknemers_Functie.</summary>
    Task<List<Keuze>> GetFunctiesAsync();
    /// <summary>Activiteiten uit UrenActiviteit.</summary>
    Task<List<Keuze>> GetActiviteitenLijstAsync();
}

/// <summary>Doet niets — voor de demo-modus zonder live database.</summary>
public class GeenWerknemerRepository : IWerknemerRepository
{
    public bool Beschikbaar => false;
    public Task<List<GebeurtenisSoort>> GetSoortenAsync() => Task.FromResult(new List<GebeurtenisSoort>());
    public Task<List<Gebeurtenis>> GetTijdlijnAsync(int werknemerId, bool inclusiefPrive) => Task.FromResult(new List<Gebeurtenis>());
    public Task<int> BewaarGebeurtenisAsync(Gebeurtenis g) => Task.FromResult(0);
    public Task VerwijderGebeurtenisAsync(int gebeurtenisId) => Task.CompletedTask;
    public Task<List<RelatieSoort>> GetRelatieSoortenAsync() => Task.FromResult(new List<RelatieSoort>());
    public Task<List<Relatie>> GetRelatiesAsync(int werknemerId) => Task.FromResult(new List<Relatie>());
    public Task<int> BewaarRelatieAsync(Relatie r) => Task.FromResult(0);
    public Task VerwijderRelatieAsync(int relatieId) => Task.CompletedTask;
    public Task<List<Bestand>> GetBestandenAsync(int storeType, int masterId) => Task.FromResult(new List<Bestand>());
    public Task<int?> GetHoofdfotoAsync(int werknemerId) => Task.FromResult<int?>(null);
    public Task ZetHoofdfotoAsync(int werknemerId, int fileStoreId) => Task.CompletedTask;
    public Task<List<Pas>> GetPassenAsync(int werknemerId, bool inclusiefOpgeheven) => Task.FromResult(new List<Pas>());
    public Task<int> BewaarPasAsync(Pas p) => Task.FromResult(0);
    public Task<List<Keuze>> GetPasTypesAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetBankrekeningenAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Mijlpalen>> GetMijlpalenAsync() => Task.FromResult(new List<Mijlpalen>());
    public Task<List<Dictionary<string, object?>>> LeesBeheerTabelAsync(BeheerTabel def) =>
        Task.FromResult(new List<Dictionary<string, object?>>());
    public Task BewaarBeheerRijAsync(BeheerTabel def, int id, Dictionary<string, object?> waarden) => Task.CompletedTask;
    public Task<string?> BeheerRijInGebruikAsync(BeheerTabel def, int id) => Task.FromResult<string?>("Geen live database.");
    public Task VerwijderBeheerRijAsync(BeheerTabel def, int id) => Task.CompletedTask;
    public Task<List<Functie>> GetFunctieBeheerAsync() => Task.FromResult(new List<Functie>());
    public Task<int> BewaarFunctieAsync(Functie f) => Task.FromResult(0);
    public Task<string?> WissenGeblokkeerdAsync(int functieId) => Task.FromResult<string?>("Geen live database.");
    public Task VerwijderFunctieAsync(int functieId) => Task.CompletedTask;
    public Task<List<string>> WerknemersMetFunctieAsync(int functieId) => Task.FromResult(new List<string>());
    public Task<List<Keuze>> GetBVsAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetFunctiesAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetActiviteitenLijstAsync() => Task.FromResult(new List<Keuze>());
}
