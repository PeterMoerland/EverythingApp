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
    private sealed class Bord { public int Id; public int? ProjectId; public int? RelatieId; public string Naam = ""; }
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
                b.RelatieId, b.RelatieId is null ? "" : $"Testklant {b.RelatieId}")).ToList());
    }

    public Task<bool> SoortenAanwezigAsync() => Task.FromResult(true);

    public Task<int> ZorgVoorBoardAsync(int projectId, int? door)
    {
        lock (_slot)
        {
            if (_boards.FirstOrDefault(x => x.ProjectId == projectId) is { } b) return Task.FromResult(b.Id);
            var n = new Bord { Id = Nieuw(), ProjectId = projectId };
            _boards.Add(n);
            return Task.FromResult(n.Id);
        }
    }

    public Task<int> ZorgVoorKlantBoardAsync(int relatieId, int? door)
    {
        lock (_slot)
        {
            if (_boards.FirstOrDefault(x => x.RelatieId == relatieId) is { } b) return Task.FromResult(b.Id);
            var n = new Bord { Id = Nieuw(), RelatieId = relatieId };
            _boards.Add(n);
            return Task.FromResult(n.Id);
        }
    }

    public Task<int> NieuwVrijBoardAsync(string naam, int? door)
    {
        lock (_slot) { var n = new Bord { Id = Nieuw(), Naam = naam.Trim() }; _boards.Add(n); return Task.FromResult(n.Id); }
    }

    public Task HernoemBoardAsync(int boardId, string naam)
    { lock (_slot) _boards.First(x => x.Id == boardId).Naam = naam.Trim(); return Task.CompletedTask; }

    public Task<string?> KoppelBoardAsync(int boardId, int? projectId, int? relatieId)
    {
        lock (_slot)
        {
            if (projectId is not null && _boards.Any(x => x.Id != boardId && x.ProjectId == projectId))
                return Task.FromResult<string?>("Dit project heeft al een board.");
            if (projectId is null && relatieId is not null && _boards.Any(x => x.Id != boardId && x.RelatieId == relatieId))
                return Task.FromResult<string?>("Deze klant heeft al een board.");
            var b = _boards.First(x => x.Id == boardId);
            b.ProjectId = projectId; b.RelatieId = projectId is null ? relatieId : null;
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
                                     b.RelatieId, b.RelatieId is null ? "" : $"Testklant {b.RelatieId}"),
                Problemen = _problemen.Where(x => x.BoardId == boardId).Select(x => x.P).OrderBy(p => p.Positie)
                    .Select(p => new ActProbleem { Id = p.Id, Naam = p.Naam, Kleur = p.Kleur, Positie = p.Positie }).ToList(),
                Types = _types.Where(x => x.BoardId == boardId).Select(x => new ActType { Id = x.T.Id, Naam = x.T.Naam, Kleur = x.T.Kleur }).ToList(),
                Activiteiten = _acts.Where(x => x.BoardId == boardId).Select(x => x.A.Kopie()).ToList(),
            });
        }
    }

    public Task<List<ActPersoon>> GetPersonenAsync() => Task.FromResult(Personen.ToList());

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
            var k = a.Kopie();
            k.Id = Nieuw();
            _acts.Add((boardId, k));
            return Task.FromResult(k.Id);
        }
    }

    private Activiteit Act(int id) => _acts.First(x => x.A.Id == id).A;

    public Task ZetVeldAsync(int activiteitId, string kolom, object? waarde, int? door)
    {
        lock (_slot)
        {
            var a = Act(activiteitId);
            switch (kolom)
            {
                case "Titel": a.Titel = (string)waarde!; break;
                case "Prioriteit": a.Prioriteit = (int?)waarde; break;
                case "Vervaldatum": a.Vervaldatum = (DateTime?)waarde; break;
                default:
                    if (waarde is string s && s.Length > 0) a.Tekst[kolom] = s; else a.Tekst.Remove(kolom);
                    break;
            }
        }
        return Task.CompletedTask;
    }

    public Task ZetStatusAsync(int activiteitId, string status, DateTime? geslotenOp, int? door)
    {
        lock (_slot) { var a = Act(activiteitId); a.Status = status; a.GeslotenOp = geslotenOp; }
        return Task.CompletedTask;
    }

    public Task ZetKoppelingenAsync(int activiteitId, string soort, IReadOnlyCollection<int> ids, int? door)
    {
        lock (_slot)
        {
            var a = Act(activiteitId);
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
    { lock (_slot) _acts.RemoveAll(x => x.A.Id == activiteitId); return Task.CompletedTask; }
}
