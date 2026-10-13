using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Haalt elk uur (Rabobank:IntervalMinuten) de transacties van de laatste dagen op bij de
/// Rabobank en zet de nieuwe in banktransacties. In de sandbox, en in productie zolang
/// Rabobank:Wegschrijven uit staat, wordt er alleen gekeken: de stand toont wat er zou komen.
/// Als singleton geregistreerd, zodat het scherm dezelfde stand ziet als de achtergrondtaak.
/// </summary>
public class RabobankOphaler : BackgroundService
{
    private readonly RabobankKoppeling _bank;
    private readonly IBankRepository _repo;
    private readonly ILogger<RabobankOphaler> _log;
    private readonly SemaphoreSlim _bezig = new(1, 1);
    private readonly RabobankStand _stand = new();

    public RabobankOphaler(RabobankKoppeling bank, IBankRepository repo, ILogger<RabobankOphaler> log)
    {
        _bank = bank;
        _repo = repo;
        _log = log;
    }

    /// <summary>Een kopie van de huidige stand.</summary>
    public RabobankStand Stand
    {
        get
        {
            var i = _bank.Instellingen;
            var (gekoppeld, op, _) = i.Ingesteld ? _bank.Koppelstand() : (false, null, null);
            lock (_stand)
                return new RabobankStand
                {
                    Ingesteld = i.Ingesteld,
                    Sandbox = i.Sandbox,
                    Gekoppeld = gekoppeld,
                    GekoppeldOp = op,
                    LaatstOpgehaald = _stand.LaatstOpgehaald,
                    VolgendeKeer = i.Aan && gekoppeld ? _stand.VolgendeKeer : null,
                    Bezig = _bezig.CurrentCount == 0,
                    Fout = _stand.Fout,
                    Rekeningen = _stand.Rekeningen.ToList(),
                    Proef = _stand.Proef.ToList(),
                };
        }
    }

    /// <summary>Wegschrijven alleen in productie én met Rabobank:Wegschrijven = true.</summary>
    public bool SchrijftWeg
    {
        get { var i = _bank.Instellingen; return !i.Sandbox && i.Wegschrijven && _repo.Beschikbaar; }
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // Even wachten na het starten van de dienst; de rest van de app gaat voor.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stop); } catch (OperationCanceledException) { return; }

        while (!stop.IsCancellationRequested)
        {
            var i = _bank.Instellingen;
            var wacht = TimeSpan.FromMinutes(Math.Clamp(i.IntervalMinuten, 5, 24 * 60));

            if (i.Aan && i.Ingesteld && _bank.Koppelstand().Gekoppeld)
            {
                try { await HaalOpAsync(stop); }
                catch (Exception ex) { _log.LogWarning(ex, "Rabobank: ophalen mislukt"); }
            }

            lock (_stand) _stand.VolgendeKeer = DateTime.Now + wacht;
            try { await Task.Delay(wacht, stop); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Nu ophalen (knop op het scherm, of de achtergrondtaak). Geeft een foutmelding of null.</summary>
    public async Task<string?> HaalOpAsync(CancellationToken stop = default)
    {
        if (!await _bezig.WaitAsync(0, stop)) return "Er wordt al opgehaald.";
        try
        {
            var i = _bank.Instellingen;
            var tot = DateTime.UtcNow.AddMinutes(-5);
            var van = DateTime.UtcNow.Date.AddDays(-Math.Clamp(i.DagenTerug, 1, 90));
            var wegschrijven = SchrijftWeg;

            var resultaten = new List<RabobankRekeningResultaat>();
            var proef = new List<BankRegel>();
            var fouten = new List<string>();

            foreach (var rek in await _bank.RekeningenAsync())
            {
                stop.ThrowIfCancellationRequested();
                var regels = await _bank.TransactiesAsync(rek, van, tot);
                decimal? saldo = null;
                try { saldo = await _bank.SaldoAsync(rek); }
                catch (Exception ex) { _log.LogInformation(ex, "Rabobank: saldo van {Iban} niet op te halen", rek.Iban); }

                // Oudste eerst, zoals in de CSV: dan klopt 'nieuwste id = laatste saldo'.
                regels = regels.OrderBy(r => r.Datum).ThenBy(r => r.Volgnummer).ToList();

                int toegevoegd = 0, aanwezig = 0;
                if (_repo.Beschikbaar && !i.Sandbox)
                {
                    var res = await _repo.VoegApiRegelsToeAsync(regels, alleenKijken: !wegschrijven);
                    toegevoegd = res.Toegevoegd;
                    aanwezig = res.AlAanwezig;
                    fouten.AddRange(res.Fouten);
                }
                else toegevoegd = regels.Count;   // sandbox: niets tegen de echte tabel houden

                if (!wegschrijven) proef.AddRange(regels);
                resultaten.Add(new RabobankRekeningResultaat(rek.Iban, regels.Count, toegevoegd, aanwezig, saldo));
                _log.LogInformation("Rabobank {Omgeving}: {Iban} {Aantal} regels, {Nieuw} nieuw{Proef}",
                    i.Omgeving, rek.Iban, regels.Count, toegevoegd, wegschrijven ? "" : " (niet weggeschreven)");
            }

            lock (_stand)
            {
                _stand.LaatstOpgehaald = DateTime.Now;
                _stand.Rekeningen = resultaten;
                _stand.Proef = proef.OrderByDescending(r => r.Datum).Take(200).ToList();
                _stand.Fout = string.Join(" ", fouten);
            }
            return fouten.Count > 0 ? _stand.Fout : null;
        }
        catch (Exception ex)
        {
            var tekst = ex is RabobankFout { OpnieuwKoppelen: true }
                ? "Opnieuw koppelen nodig: " + ex.Message
                : ex.Message;
            lock (_stand) _stand.Fout = tekst;
            return tekst;
        }
        finally { _bezig.Release(); }
    }
}
