using System.Text.Json;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Demo repository: keeps everything in memory, persists the Uren entries and
/// planning to demo-data.json next to the app so your input survives a restart.
/// </summary>
public class JsonDemoRepository : IUrenRepository
{
    private readonly object _lock = new();
    private readonly string _file;
    private List<UrenEntry> _uren = new();
    private List<PlanningItem> _planning = new();
    private int _nextId = 1;
    private int _nextPlanId = 1;

    private class Opslag
    {
        public List<UrenEntry> Uren { get; set; } = new();
        public List<PlanningItem> Planning { get; set; } = new();
    }

    private readonly List<Werknemer> _werknemers = new()
    {
        new() { WerknemerId = 1, Naam = "Peter Moerland",  Directie = true,  Hoofdactiviteit = 1, WoonWerkKilometers = 24 },
        new() { WerknemerId = 2, Naam = "Jan de Vries",    Directie = false, Hoofdactiviteit = 2 },
        new() { WerknemerId = 3, Naam = "Kees Bakker",     Directie = false, Hoofdactiviteit = 2 },
        new() { WerknemerId = 4, Naam = "Sanne Visser",    Directie = false, Hoofdactiviteit = 3 },
    };

    private readonly List<Workorder> _workorders = new()
    {
        new() { WerkbonId = 24001, Omschrijving = "Besturing paneelbouw lijn 3", Klant = "Van Dijk Machines", WorkorderStatus = 1 },
        new() { WerkbonId = 24002, Omschrijving = "Service PLC storing",         Klant = "Bakkerij Jansen",   WorkorderStatus = 1 },
        new() { WerkbonId = 24003, Omschrijving = "Engineering schema's",        Klant = "De Groot Installatie", WorkorderStatus = 1 },
        new() { WerkbonId = 24004, Omschrijving = "Inbedrijfstelling transportband", Klant = "LogiFood",      WorkorderStatus = 1 },
        new() { WerkbonId = 23099, Omschrijving = "Onderhoud (GESLOTEN)",        Klant = "Oude klant",        WorkorderStatus = 0 },
        new() { WerkbonId = 90001, Omschrijving = "Intern - algemeen",           Klant = "MDE",               WorkorderStatus = 1 },
        new() { WerkbonId = 90002, Omschrijving = "Intern - kilometers directie", Klant = "MDE",              WorkorderStatus = 1 },
        new() { WerkbonId = 90003, Omschrijving = "Verlof / vakantie",           Klant = "MDE",               WorkorderStatus = 1 },
        new() { WerkbonId = 90004, Omschrijving = "Ziekte",                      Klant = "MDE",               WorkorderStatus = 1 },
    };

    private readonly List<Tariefsoort> _tariefsoorten = new()
    {
        new() { TariefsoortId = 1, Naam = "Travel" },
        new() { TariefsoortId = 2, Naam = "Normaal" },
        new() { TariefsoortId = 3, Naam = "Overuren 125%" },
        new() { TariefsoortId = 4, Naam = "Overuren 150%" },
        new() { TariefsoortId = 5, Naam = "Fixed" },
    };

    private readonly List<UrenActiviteit> _activiteiten = new()
    {
        new() { UrenActiviteitId = 1,  Naam = "Management" },
        new() { UrenActiviteitId = 2,  Naam = "Paneelbouw" },
        new() { UrenActiviteitId = 3,  Naam = "Engineering" },
        new() { UrenActiviteitId = 4,  Naam = "Service" },
        new() { UrenActiviteitId = 13, Naam = "Woon-werk verkeer" },
    };

    private readonly List<SpecialOrder> _specialOrders = new()
    {
        new() { Id = 1, Order = 90003, Remark = "Verlof" },
        new() { Id = 2, Order = 90004, Remark = "Ziek" },
    };

    private readonly GeneralSettings _settings = new() { ManagementKilometerOrder = 90002 };

    public JsonDemoRepository(IWebHostEnvironment env)
    {
        _file = Path.Combine(env.ContentRootPath, "demo-data.json");
        Load();
        if (_uren.Count == 0) Seed();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var json = File.ReadAllText(_file);
                if (json.TrimStart().StartsWith("["))
                {
                    // oud formaat: alleen een lijst met uren
                    _uren = JsonSerializer.Deserialize<List<UrenEntry>>(json) ?? new();
                }
                else
                {
                    var opslag = JsonSerializer.Deserialize<Opslag>(json) ?? new();
                    _uren = opslag.Uren;
                    _planning = opslag.Planning;
                }
                _nextId = _uren.Count == 0 ? 1 : _uren.Max(u => u.UrenId) + 1;
                _nextPlanId = _planning.Count == 0 ? 1 : _planning.Max(p => p.Id) + 1;
            }
        }
        catch { _uren = new(); _planning = new(); }
    }

    private void Save()
    {
        lock (_lock)
            File.WriteAllText(_file, JsonSerializer.Serialize(
                new Opslag { Uren = _uren, Planning = _planning },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Seed()
    {
        // A few entries earlier this week for employee 1 and 2.
        var maandag = DateOnly.FromDateTime(DateTime.Today);
        while (maandag.DayOfWeek != DayOfWeek.Monday) maandag = maandag.AddDays(-1);

        void Add(int wn, DateOnly d, string st, string et, decimal uren, int wo, int tarief, string oms, int act) =>
            _uren.Add(new UrenEntry
            {
                UrenId = _nextId++, WerknemerId = wn, StartDatum = d, EindDatum = d,
                StartTijd = TimeOnly.Parse(st), EindTijd = TimeOnly.Parse(et),
                Uren = uren, Werkorder = wo, TariefsoortId = tarief, Omschrijving = oms, UrenActiviteitId = act
            });

        Add(1, maandag, "08:00", "12:00", 4, 24001, 2, "Schema's doorgenomen", 3);
        Add(1, maandag, "12:30", "17:00", 4.5m, 24003, 2, "Engineering hoofdschema", 3);
        Add(2, maandag, "07:30", "16:00", 8, 24001, 2, "Paneel opbouw", 2);
        Add(2, maandag.AddDays(1), "07:30", "16:00", 8, 24001, 2, "Paneel bedraden", 2);

        // Planning (Gantt) demo
        void Plan(int wn, int wo, int startDag, int duurDagen, decimal uren, string notitie = "") =>
            _planning.Add(new PlanningItem
            {
                Id = _nextPlanId++, WerknemerId = wn, Werkorder = wo,
                Start = maandag.AddDays(startDag), Eind = maandag.AddDays(startDag + duurDagen - 1),
                GeplandeUren = uren, Notitie = notitie
            });

        Plan(1, 24003, 0, 5, 32, "Hoofdschema afronden");
        Plan(1, 24004, 7, 4, 24, "Voorbereiding IBS");
        Plan(2, 24001, 0, 9, 60, "Paneel bouwen + bedraden");
        Plan(2, 24004, 10, 4, 30, "IBS transportband");
        Plan(3, 24001, 3, 6, 40, "Montage ondersteuning");
        Plan(3, 24002, 0, 2, 12, "Storing oplossen");
        Plan(4, 24003, 2, 8, 45, "Detail-engineering");
        Save();
    }

    // ---- lookups ----
    public Task<List<Werknemer>> GetWerknemersAsync() => Task.FromResult(_werknemers.Where(w => w.Actief).ToList());
    public Task<Werknemer?> GetWerknemerAsync(int id) => Task.FromResult(_werknemers.FirstOrDefault(w => w.WerknemerId == id));

    // Demo: geen e-mailadressen bekend, dus geen koppeling met een Google-account.
    public Task<int?> VindWerknemerOpEmailAsync(string email) => Task.FromResult<int?>(null);

    public Task<List<Werknemer>> ZoekWerknemersAsync(bool inclusiefIngeleend, bool inclusiefUitDienst,
                                                     bool alleenPersonen, string? zoek)
    {
        var lijst = _werknemers.AsEnumerable();
        if (!inclusiefUitDienst) lijst = lijst.Where(w => w.Actief);
        if (!string.IsNullOrWhiteSpace(zoek))
            lijst = lijst.Where(w => w.Naam.Contains(zoek.Trim(), StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(lijst.OrderBy(w => w.Naam).ToList());
    }

    public Task UpdateWerknemerVeldAsync(int werknemerId, string kolom, object? waarde)
    {
        // Demo-data: alleen de naam is zinvol bij te werken.
        var w = _werknemers.FirstOrDefault(x => x.WerknemerId == werknemerId);
        if (w is not null && kolom.Equals("NaamVoluit", StringComparison.OrdinalIgnoreCase))
            w.Naam = waarde?.ToString() ?? w.Naam;
        return Task.CompletedTask;
    }

    public Task<HashSet<string>> WerknemerKolommenAsync() =>
        Task.FromResult(new HashSet<string>(
            new[] { "Werknemer-id", "NaamVoluit", "Directie", "Hoofdactiviteit", "woonwerkkilometers" },
            StringComparer.OrdinalIgnoreCase));

    public Task<Dictionary<string, object?>> GetWerknemerVeldenAsync(int werknemerId)
    {
        var w = _werknemers.FirstOrDefault(x => x.WerknemerId == werknemerId);
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (w != null)
        {
            d["Werknemer-id"] = w.WerknemerId;
            d["NaamVoluit"] = w.Naam;
            d["Directie"] = w.Directie;
        }
        return Task.FromResult(d);
    }
    public Task<List<Workorder>> GetOpenWorkordersAsync() => Task.FromResult(_workorders.Where(w => w.WorkorderStatus != 0).ToList());
    public Task<Workorder?> GetWorkorderAsync(int id) => Task.FromResult(_workorders.FirstOrDefault(w => w.WerkbonId == id));

    // --- werkorder zoeken (demo) ---
    private readonly List<(int Werknemer, int Werkorder)> _persoonlijk = new();

    public Task<List<Workorder>> WerkordersVanKlantAsync(int klantId, bool alleenOpen, int max) =>
        Task.FromResult(new List<Workorder>());

    public Task<List<Workorder>> ZoekWerkordersAsync(string zoek, bool alleenOpen, int max) =>
        Task.FromResult(_workorders
            .Where(w => !alleenOpen || w.WorkorderStatus != 0)
            .Where(w => string.IsNullOrWhiteSpace(zoek)
                     || w.VolledigeTekst.Contains(zoek, StringComparison.OrdinalIgnoreCase)
                     || w.Klant.Contains(zoek, StringComparison.OrdinalIgnoreCase))
            .Take(max).ToList());

    public Task<List<Workorder>> RecentGebruikteWerkordersAsync(int werknemerId, int dagen, int max)
    {
        var vanaf = DateOnly.FromDateTime(DateTime.Today.AddDays(-Math.Abs(dagen)));
        var ids = _uren
            .Where(u => u.WerknemerId == werknemerId && u.Werkorder != null && u.StartDatum >= vanaf)
            .GroupBy(u => u.Werkorder!.Value)
            .OrderByDescending(g => g.Max(u => u.StartDatum))
            .Select(g => g.Key).Take(max).ToList();
        return Task.FromResult(ids
            .Select(id => _workorders.FirstOrDefault(w => w.WerkbonId == id))
            .Where(w => w != null).Select(w => w!).ToList());
    }

    public Task<List<Workorder>> PersoonlijkeWerkordersAsync(int werknemerId) =>
        Task.FromResult(_persoonlijk.Where(p => p.Werknemer == werknemerId)
            .Select(p => _workorders.FirstOrDefault(w => w.WerkbonId == p.Werkorder))
            .Where(w => w != null).Select(w => w!).ToList());

    public Task ZetPersoonlijkeWerkorderAsync(int werknemerId, int werkbonId, bool toevoegen)
    {
        lock (_lock)
        {
            if (toevoegen)
            {
                if (!_persoonlijk.Any(p => p.Werknemer == werknemerId && p.Werkorder == werkbonId))
                    _persoonlijk.Add((werknemerId, werkbonId));
            }
            else _persoonlijk.RemoveAll(p => p.Werknemer == werknemerId && p.Werkorder == werkbonId);
        }
        return Task.CompletedTask;
    }
    public Task<List<Tariefsoort>> GetTariefsoortenAsync() => Task.FromResult(_tariefsoorten);
    public Task<List<UrenActiviteit>> GetActiviteitenAsync() => Task.FromResult(_activiteiten);
    public Task<List<SpecialOrder>> GetSpecialOrdersAsync() => Task.FromResult(_specialOrders);
    public Task<GeneralSettings> GetGeneralSettingsAsync() => Task.FromResult(_settings);

    // ---- planning (Gantt) ----
    public Task<List<PlanningItem>> GetPlanningAsync() =>
        Task.FromResult(_planning.OrderBy(p => p.WerknemerId).ThenBy(p => p.Start).ToList());

    public Task<PlanningItem> AddPlanningAsync(PlanningItem item)
    {
        lock (_lock) { item.Id = _nextPlanId++; _planning.Add(item); }
        Save();
        return Task.FromResult(item);
    }

    public Task UpdatePlanningAsync(PlanningItem item)
    {
        lock (_lock)
        {
            var i = _planning.FindIndex(p => p.Id == item.Id);
            if (i >= 0) _planning[i] = item;
        }
        Save();
        return Task.CompletedTask;
    }

    public Task DeletePlanningAsync(int id)
    {
        lock (_lock) _planning.RemoveAll(p => p.Id == id);
        Save();
        return Task.CompletedTask;
    }

    public Task<List<UrenEntry>> GetAlleUrenAsync(DateOnly van, DateOnly tot) =>
        Task.FromResult(_uren.Where(u => u.StartDatum >= van && u.StartDatum <= tot).ToList());

    // ---- uren ----
    public Task<List<UrenEntry>> GetUrenAsync(int werknemerId, DateOnly van, DateOnly tot) =>
        Task.FromResult(_uren
            .Where(u => u.WerknemerId == werknemerId && u.StartDatum >= van && u.StartDatum <= tot)
            .OrderBy(u => u.StartDatum).ThenBy(u => u.StartTijd)
            .ToList());

    public Task<List<UrenEntry>> GetUrenVanDagAsync(int werknemerId, DateOnly dag) =>
        GetUrenAsync(werknemerId, dag, dag);

    public Task<UrenEntry> AddAsync(UrenEntry entry)
    {
        lock (_lock) { entry.UrenId = _nextId++; _uren.Add(entry); }
        Save();
        return Task.FromResult(entry);
    }

    public Task UpdateAsync(UrenEntry entry)
    {
        lock (_lock)
        {
            var i = _uren.FindIndex(u => u.UrenId == entry.UrenId);
            if (i >= 0) _uren[i] = entry;
        }
        Save();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int urenId)
    {
        lock (_lock) _uren.RemoveAll(u => u.UrenId == urenId);
        Save();
        return Task.CompletedTask;
    }
}
