using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Activity board in het geheugen, alleen om het scherm lokaal te testen zonder de database aan te raken
/// (instelling <c>ActivityBoard:Geheugen = true</c>). Alles is weg na een herstart.
/// </summary>
public class GeheugenActivityBoardRepository : IActivityBoardRepository
{
    private readonly object _slot = new();
    private int _volgend = 100;
    private sealed class Bord { public int Id; public int? ProjectId; public int? RelatieId; public int? WerkorderId; public string Naam = ""; public int? Door; }
    private readonly List<Bord> _boards = new();
    private readonly List<(int BoardId, ActProbleem P)> _problemen = new();
    private readonly List<(int BoardId, ActType T)> _types = new();
    private readonly List<(int BoardId, Activiteit A)> _acts = new();

    private static readonly List<ActPersoon> Personen = new()
    {
        new(1, "Arian Duine", true), new(2, "Niek Verschuure", true), new(3, "Freek Moerland", true),
        new(4, "Edwin Denissen", true), new(5, "Kees Blok", true), new(6, "Peter Moerland", true), new(7, "Tim Franke", false),
    };

    public bool Beschikbaar => true;
    public Task<bool> TabellenAanwezigAsync() => Task.FromResult(true);
    private int Nieuw() => Interlocked.Increment(ref _volgend);

    public Task<List<ActBoardRegel>> GetBoardsAsync()
    {
        lock (_slot)
            return Task.FromResult(_boards.Select(b => new ActBoardRegel(b.Id, b.ProjectId, b.ProjectId,
                b.ProjectId is null ? "" : $"Testproject {b.ProjectId}", b.Naam,
                _acts.Count(a => a.BoardId == b.Id && ActStatus.Groep(a.A.Status) != ActStatus.GroepKlaar),
                _acts.Count(a => a.BoardId == b.Id && ActStatus.Groep(a.A.Status) == ActStatus.GroepKlaar), DateTime.Now,
                b.RelatieId, b.RelatieId is null ? "" : $"Testklant {b.RelatieId}",
                b.WerkorderId, b.WerkorderId is null ? "" : $"WO-{b.WerkorderId}", b.WerkorderId is null ? "" : $"Testwerkorder {b.WerkorderId}")
            {
                // nep-hiërarchie om de boom te kunnen bekijken: alles onder Testklant 1, werkorders onder Testproject 500
                KlantId = b.RelatieId ?? (b.ProjectId is not null || b.WerkorderId is not null ? 1 : null),
                KlantNaam = b.RelatieId is not null ? $"Testklant {b.RelatieId}" : b.ProjectId is not null || b.WerkorderId is not null ? "Testklant 1" : "",
                HoofdProjectId = b.WerkorderId is null ? null : 500, HoofdProjectNummer = b.WerkorderId is null ? null : 500,
                HoofdProjectNaam = b.WerkorderId is null ? "" : "Testproject 500",
            }).ToList());
    }

    public Task<bool> SoortenAanwezigAsync() => Task.FromResult(true);

    public Task<int> ZorgVoorBoardAsync(int projectId, int? door)
    {
        lock (_slot)
        {
            if (_boards.FirstOrDefault(x => x.ProjectId == projectId) is { } b) return Task.FromResult(b.Id);
            var n = new Bord { Id = Nieuw(), ProjectId = projectId, Door = door };
            _boards.Add(n);
            return Task.FromResult(n.Id);
        }
    }

    public Task<int> ZorgVoorKlantBoardAsync(int relatieId, int? door)
    {
        lock (_slot)
        {
            if (_boards.FirstOrDefault(x => x.RelatieId == relatieId) is { } b) return Task.FromResult(b.Id);
            var n = new Bord { Id = Nieuw(), RelatieId = relatieId, Door = door };
            _boards.Add(n);
            return Task.FromResult(n.Id);
        }
    }

    public Task<bool> WerkorderAanwezigAsync() => Task.FromResult(true);

    public Task<int> ZorgVoorWerkorderBoardAsync(int werkbonId, int? door)
    {
        lock (_slot)
        {
            if (_boards.FirstOrDefault(x => x.WerkorderId == werkbonId) is { } b) return Task.FromResult(b.Id);
            var n = new Bord { Id = Nieuw(), WerkorderId = werkbonId, Door = door };
            _boards.Add(n);
            return Task.FromResult(n.Id);
        }
    }

    public Task<int> NieuwVrijBoardAsync(string naam, int? door)
    {
        lock (_slot) { var n = new Bord { Id = Nieuw(), Naam = naam.Trim(), Door = door }; _boards.Add(n); return Task.FromResult(n.Id); }
    }

    public Task HernoemBoardAsync(int boardId, string naam)
    { lock (_slot) _boards.First(x => x.Id == boardId).Naam = naam.Trim(); return Task.CompletedTask; }

    public Task VerwijderBoardAsync(int boardId)
    {
        lock (_slot)
        {
            _boards.RemoveAll(b => b.Id == boardId);
            _problemen.RemoveAll(x => x.BoardId == boardId);
            _types.RemoveAll(x => x.BoardId == boardId);
            _acts.RemoveAll(x => x.BoardId == boardId);
        }
        return Task.CompletedTask;
    }

    public Task<string?> KoppelBoardAsync(int boardId, int? projectId, int? relatieId, int? werkorderId = null)
    {
        lock (_slot)
        {
            if (werkorderId is not null)
            {
                if (_boards.Any(x => x.Id != boardId && x.WerkorderId == werkorderId))
                    return Task.FromResult<string?>("Deze werkorder heeft al een board.");
                var w = _boards.First(x => x.Id == boardId);
                w.WerkorderId = werkorderId; w.ProjectId = null; w.RelatieId = null;
                return Task.FromResult<string?>(null);
            }
            if (projectId is not null && _boards.Any(x => x.Id != boardId && x.ProjectId == projectId))
                return Task.FromResult<string?>("Dit project heeft al een board.");
            if (projectId is null && relatieId is not null && _boards.Any(x => x.Id != boardId && x.RelatieId == relatieId))
                return Task.FromResult<string?>("Deze klant heeft al een board.");
            var b = _boards.First(x => x.Id == boardId);
            b.ProjectId = projectId; b.RelatieId = projectId is null ? relatieId : null; b.WerkorderId = null;
            return Task.FromResult<string?>(null);
        }
    }

    public Task<ActBoardData?> LaadAsync(int boardId)
    {
        lock (_slot)
        {
            var b = _boards.FirstOrDefault(x => x.Id == boardId);
            if (b is null) return Task.FromResult<ActBoardData?>(null);
            return Task.FromResult<ActBoardData?>(new ActBoardData
            {
                Board = new ActBoard(b.Id, b.ProjectId, b.ProjectId, b.ProjectId is null ? "" : $"Testproject {b.ProjectId}", b.Naam,
                                     b.RelatieId, b.RelatieId is null ? "" : $"Testklant {b.RelatieId}",
                                     b.WerkorderId, b.WerkorderId is null ? "" : $"WO-{b.WerkorderId}", b.WerkorderId is null ? "" : $"Testwerkorder {b.WerkorderId}")
                        { AangemaaktDoor = b.Door },
                Problemen = _problemen.Where(x => x.BoardId == boardId).Select(x => x.P).OrderBy(p => p.Positie)
                    .Select(p => new ActProbleem { Id = p.Id, Naam = p.Naam, Kleur = p.Kleur, Positie = p.Positie }).ToList(),
                Types = _types.Where(x => x.BoardId == boardId).Select(x => new ActType { Id = x.T.Id, Naam = x.T.Naam, Kleur = x.T.Kleur }).ToList(),
                Activiteiten = _acts.Where(x => x.BoardId == boardId).Select(x => { var k = x.A.Kopie(); k.Koppelingen = KoppelingenVan(k.Id); return k; }).ToList(),
            });
        }
    }

    public Task<List<ActPersoon>> GetPersonenAsync() => Task.FromResult(Personen.ToList());

    public Task<int?> BoardVanActiviteitAsync(int activiteitId)
    {
        lock (_slot)
            return Task.FromResult(_acts.Where(x => x.A.Id == activiteitId).Select(x => (int?)x.BoardId).FirstOrDefault());
    }
    public Task<bool> TijdAanwezigAsync() => Task.FromResult(true);

    public Task<int> VoegProbleemToeAsync(int boardId, string naam, string kleur)
    {
        lock (_slot)
        {
            if (_problemen.Any(x => x.BoardId == boardId && x.P.Naam.Equals(naam.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Duplicate entry");
            var p = new ActProbleem { Id = Nieuw(), Naam = naam.Trim(), Kleur = kleur,
                                      Positie = _problemen.Where(x => x.BoardId == boardId).Select(x => x.P.Positie).DefaultIfEmpty(0).Max() + 1 };
            _problemen.Add((boardId, p));
            return Task.FromResult(p.Id);
        }
    }

    public Task HernoemProbleemAsync(int probleemId, string naam)
    { lock (_slot) _problemen.First(x => x.P.Id == probleemId).P.Naam = naam.Trim(); return Task.CompletedTask; }

    public Task ZetProbleemVolgordeAsync(int boardId, IReadOnlyList<int> probleemIds)
    {
        lock (_slot)
            for (var i = 0; i < probleemIds.Count; i++)
                if (_problemen.FirstOrDefault(x => x.P.Id == probleemIds[i]).P is { } p) p.Positie = i + 1;
        return Task.CompletedTask;
    }

    public Task VerwijderProbleemAsync(int probleemId)
    {
        lock (_slot)
        {
            _problemen.RemoveAll(x => x.P.Id == probleemId);
            foreach (var a in _acts) a.A.Problemen.Remove(probleemId);
        }
        return Task.CompletedTask;
    }

    public Task<int> VoegTypeToeAsync(int boardId, string naam, string kleur)
    {
        lock (_slot)
        {
            var t = new ActType { Id = Nieuw(), Naam = naam.Trim(), Kleur = kleur };
            _types.Add((boardId, t));
            return Task.FromResult(t.Id);
        }
    }

    public Task HernoemTypeAsync(int typeId, string naam)
    { lock (_slot) _types.First(x => x.T.Id == typeId).T.Naam = naam.Trim(); return Task.CompletedTask; }

    public Task VerwijderTypeAsync(int typeId)
    {
        lock (_slot)
        {
            _types.RemoveAll(x => x.T.Id == typeId);
            foreach (var a in _acts) a.A.Types.Remove(typeId);
        }
        return Task.CompletedTask;
    }

    public Task<int> NieuweActiviteitAsync(int boardId, Activiteit a, int? door)
    {
        lock (_slot)
        {
            foreach (var p in a.Checklist) { p.Id = Nieuw(); if (p.Klaar) { p.KlaarOp ??= DateTime.Now; p.KlaarDoor ??= door; } }
            var k = a.Kopie();
            k.Id = Nieuw();
            k.GemaaktDoor ??= door;
            k.GewijzigdOp = DateTime.Now; k.GewijzigdDoor = door;
            _acts.Add((boardId, k));
            return Task.FromResult(k.Id);
        }
    }

    private Activiteit Act(int id) => _acts.First(x => x.A.Id == id).A;
    /// <summary>De activiteit, en hij telt als nu aangepast door deze werknemer (zoals last_updated en GewijzigdDoor).</summary>
    private Activiteit Act(int id, int? door) { var a = Act(id); a.GewijzigdOp = DateTime.Now; a.GewijzigdDoor = door; return a; }

    public Task ZetVeldAsync(int activiteitId, string kolom, object? waarde, int? door)
    {
        lock (_slot)
        {
            var a = Act(activiteitId, door);
            switch (kolom)
            {
                case "Titel": a.Titel = (string)waarde!; break;
                case "Prioriteit": a.Prioriteit = (int?)waarde; break;
                case "Vervaldatum": a.Vervaldatum = (DateTime?)waarde; break;
                case "BenodigdeUren": a.BenodigdeUren = (decimal?)waarde; break;
                default:
                    if (waarde is string s && s.Length > 0) a.Tekst[kolom] = s; else a.Tekst.Remove(kolom);
                    break;
            }
        }
        return Task.CompletedTask;
    }

    public Task ZetStatusAsync(int activiteitId, string status, DateTime? geslotenOp, int? door)
    {
        lock (_slot) { var a = Act(activiteitId, door); a.Status = status; a.GeslotenOp = geslotenOp; }
        return Task.CompletedTask;
    }

    public Task ZetKoppelingenAsync(int activiteitId, string soort, IReadOnlyCollection<int> ids, int? door)
    {
        lock (_slot)
        {
            var a = Act(activiteitId, door);
            var lijst = ids.Distinct().ToList();
            if (soort == "problemen") a.Problemen = lijst; else if (soort == "types") a.Types = lijst; else a.Personen = lijst;
        }
        return Task.CompletedTask;
    }

    public Task VerplaatsAsync(int activiteitId, string status, DateTime? geslotenOp, IReadOnlyCollection<int> problemen,
                               IReadOnlyList<int> vakVolgorde, int? door)
    {
        lock (_slot)
        {
            var a = Act(activiteitId);
            a.Status = status; a.GeslotenOp = geslotenOp; a.Problemen = problemen.Distinct().ToList();
            for (var i = 0; i < vakVolgorde.Count; i++)
                if (_acts.FirstOrDefault(x => x.A.Id == vakVolgorde[i]).A is { } v) v.Volgorde = i;
        }
        return Task.CompletedTask;
    }

    public Task VerwijderActiviteitAsync(int activiteitId)
    { lock (_slot) { _acts.RemoveAll(x => x.A.Id == activiteitId); _koppelingen.RemoveAll(k => k.Van == activiteitId || k.Naar == activiteitId); } return Task.CompletedTask; }

    public Task<bool> OpmaakAanwezigAsync() => Task.FromResult(true);
    public Task ZetOpmaakAsync(int activiteitId, bool opmaak, int? door)
    { lock (_slot) Act(activiteitId).Opmaak = opmaak; return Task.CompletedTask; }

    // ---- koppelingen ----
    private readonly List<(int Id, int Van, int Naar, string Soort)> _koppelingen = new();
    public Task<bool> KoppelingenAanwezigAsync() => Task.FromResult(true);

    private string BoardNaam(int boardId) => _boards.FirstOrDefault(b => b.Id == boardId) is { } b && b.Naam.Length > 0 ? b.Naam : $"Board {boardId}";

    private List<ActKoppeling> KoppelingenVan(int id)
    {
        var uit = new List<ActKoppeling>();
        foreach (var k in _koppelingen.Where(k => k.Van == id || k.Naar == id))
        {
            var heen = k.Van == id;
            var ander = _acts.FirstOrDefault(x => x.A.Id == (heen ? k.Naar : k.Van));
            if (ander.A is null) continue;
            uit.Add(new ActKoppeling(k.Id, k.Soort, heen, ander.A.Id, ander.A.Titel, ander.A.Status, ander.BoardId, BoardNaam(ander.BoardId)));
        }
        return uit;
    }

    public Task<int> NieuweKoppelingAsync(int vanId, int naarId, string soort, int? door)
    {
        lock (_slot)
        {
            if (vanId == naarId) throw new InvalidOperationException("Een activiteit kan niet aan zichzelf gekoppeld worden.");
            var bestaand = _koppelingen.FirstOrDefault(k => k.Soort == soort && ((k.Van == vanId && k.Naar == naarId) || (k.Van == naarId && k.Naar == vanId)));
            if (bestaand.Id != 0)
            {
                if (bestaand.Van == vanId || soort == ActKoppelSoort.ZieOok) return Task.FromResult(bestaand.Id);
                throw new InvalidOperationException("Deze twee activiteiten zijn al andersom gekoppeld.");
            }
            var id = Nieuw();
            _koppelingen.Add((id, vanId, naarId, soort));
            return Task.FromResult(id);
        }
    }

    public Task VerwijderKoppelingAsync(int koppelingId, int? door)
    { lock (_slot) _koppelingen.RemoveAll(k => k.Id == koppelingId); return Task.CompletedTask; }

    public Task<(int VanId, int NaarId)?> KoppelingAsync(int koppelingId)
    {
        lock (_slot)
            return Task.FromResult(_koppelingen.FirstOrDefault(k => k.Id == koppelingId) is { Id: not 0 } k ? ((int, int)?)(k.Van, k.Naar) : null);
    }

    public Task<List<ActKoppelDoel>> ZoekActiviteitenAsync(string zoek, int max)
    {
        lock (_slot)
        {
            var z = zoek.Trim();
            var nummer = int.TryParse(z.TrimStart('#'), out var n) ? n : (int?)null;
            var woorden = z.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return Task.FromResult(_acts.Where(x => x.A.Id == nummer || woorden.All(w => x.A.Titel.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.A.Id == nummer).Take(max)
                .Select(x => new ActKoppelDoel(x.A.Id, x.A.Titel, x.A.Status, x.BoardId, BoardNaam(x.BoardId))).ToList());
        }
    }

    // ---- afvinklijst ----
    public Task<bool> ChecklistAanwezigAsync() => Task.FromResult(true);

    private (Activiteit A, ActPunt P)? Punt(int puntId)
    {
        foreach (var x in _acts)
            if (x.A.Checklist.FirstOrDefault(p => p.Id == puntId) is { } p) return (x.A, p);
        return null;
    }

    public Task<int> NieuwPuntAsync(int activiteitId, ActPunt punt, int? naPuntId, int? door)
    {
        lock (_slot)
        {
            var lijst = Act(activiteitId).Checklist;
            if (lijst.Count >= ActPunt.MaxPerActiviteit) throw new InvalidOperationException($"Een afvinklijst heeft hooguit {ActPunt.MaxPerActiviteit} regels.");
            punt.Id = Nieuw();
            if (punt.Klaar) { punt.KlaarOp ??= DateTime.Now; punt.KlaarDoor ??= door; }
            var plek = naPuntId is null ? lijst.Count : naPuntId == 0 ? 0 : lijst.FindIndex(p => p.Id == naPuntId) is >= 0 and var i ? i + 1 : lijst.Count;
            lijst.Insert(plek, punt.Kopie());
            return Task.FromResult(punt.Id);
        }
    }

    public Task ZetPuntTekstAsync(int puntId, string tekst, int? door)
    { lock (_slot) { if (Punt(puntId) is { } x) x.P.Tekst = tekst.Trim(); } return Task.CompletedTask; }

    public Task ZetPuntKlaarAsync(int puntId, bool klaar, int? door)
    {
        lock (_slot)
            if (Punt(puntId) is { } x && !x.P.Kop) { x.P.Klaar = klaar; x.P.KlaarOp = klaar ? DateTime.Now : null; x.P.KlaarDoor = klaar ? door : null; }
        return Task.CompletedTask;
    }

    public Task ZetPuntVolgordeAsync(int activiteitId, IReadOnlyList<int> puntIds, int? door)
    {
        lock (_slot)
        {
            var a = Act(activiteitId);
            a.Checklist = a.Checklist.OrderBy(p => puntIds.ToList().IndexOf(p.Id) is >= 0 and var i ? i : int.MaxValue).ToList();
        }
        return Task.CompletedTask;
    }

    public Task VerwijderPuntAsync(int puntId, int? door)
    { lock (_slot) { if (Punt(puntId) is { } x) x.A.Checklist.Remove(x.P); } return Task.CompletedTask; }

    public Task<int?> ActiviteitVanPuntAsync(int puntId)
    { lock (_slot) return Task.FromResult(Punt(puntId)?.A.Id); }
}
