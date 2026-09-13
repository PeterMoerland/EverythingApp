using System.Globalization;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Business logic ported 1-on-1 from the VBA of Form_Ureningave / Form_Ureningave sub.
/// Each method notes the original VBA procedure it replaces.
/// </summary>
public class UrenService
{
    private readonly IUrenRepository _repo;
    public UrenService(IUrenRepository repo) => _repo = repo;

    // === VBA: DatumVanJaarWeekDag (ISO week rule) — .NET has this built in ===
    public static DateOnly DatumVanJaarWeekDag(int jaar, int week, int weekdag /*1=ma..7=zo*/)
    {
        // Vangnet: tijdens de eerste weergave (voordat de data uit de database terug is)
        // kunnen jaar/week nog 0 zijn. Dan de huidige week gebruiken i.p.v. crashen.
        if (jaar < 1 || jaar > 9999 || week < 1 || week > 53)
        {
            var nu = DateOnly.FromDateTime(DateTime.Today);
            var (j, w, _) = WeekInfo(nu);
            jaar = j; week = w;
        }
        if (weekdag < 1) weekdag = 1;
        if (weekdag > 7) weekdag = 7;

        var dow = weekdag == 7 ? DayOfWeek.Sunday : (DayOfWeek)weekdag;
        return DateOnly.FromDateTime(ISOWeek.ToDateTime(jaar, week, dow));
    }

    /// <summary>
    /// Leest een tijd in 24-uursnotatie. Ruim in wat het accepteert, zodat snel typen werkt:
    ///   "8" -> 08:00,  "830" of "8:30" of "8.30" of "8,30" -> 08:30,  "0830" -> 08:30,
    ///   "16" -> 16:00,  "1615" -> 16:15
    /// Geeft null bij lege of onzinnige invoer. (Vergelijkbaar met het invoermasker
    /// "00:00" uit Personal_Setting in de Access-versie.)
    /// </summary>
    public static TimeOnly? ParseTijd(string? invoer)
    {
        if (string.IsNullOrWhiteSpace(invoer)) return null;

        var s = invoer.Trim().Replace('.', ':').Replace(',', ':').Replace('-', ':');

        int uur, minuut;
        var dp = s.IndexOf(':');
        if (dp >= 0)
        {
            var u = s.Substring(0, dp);
            var m = s.Substring(dp + 1);
            if (!int.TryParse(u.Length == 0 ? "0" : u, out uur)) return null;
            if (m.Length == 0) minuut = 0;
            else if (!int.TryParse(m, out minuut)) return null;
        }
        else
        {
            if (!s.All(char.IsDigit)) return null;
            switch (s.Length)
            {
                case 1:
                case 2: uur = int.Parse(s); minuut = 0; break;
                case 3: uur = int.Parse(s.Substring(0, 1)); minuut = int.Parse(s.Substring(1)); break;
                case 4: uur = int.Parse(s.Substring(0, 2)); minuut = int.Parse(s.Substring(2)); break;
                default: return null;
            }
        }

        if (uur == 24 && minuut == 0) { uur = 0; }          // 24:00 = middernacht
        if (uur < 0 || uur > 23 || minuut < 0 || minuut > 59) return null;
        return new TimeOnly(uur, minuut);
    }

    /// <summary>Rondt een tijd af op het naaste kwartier.</summary>
    public static TimeOnly OpKwartier(TimeOnly t)
    {
        var minuten = (int)Math.Round(t.ToTimeSpan().TotalMinutes / 15.0) * 15;
        minuten = ((minuten % 1440) + 1440) % 1440;
        return new TimeOnly(minuten / 60, minuten % 60);
    }

    public static (int jaar, int week, int dagnummer) WeekInfo(DateOnly datum)
    {
        var dt = datum.ToDateTime(TimeOnly.MinValue);
        int dag = datum.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)datum.DayOfWeek;
        return (ISOWeek.GetYear(dt), ISOWeek.GetWeekOfYear(dt), dag);
    }

    // === VBA: HaalUrenOp — hours per weekday for the week overview ===
    public async Task<decimal?[]> UrenPerDagVanWeek(int werknemerId, int jaar, int week)
    {
        var maandag = DatumVanJaarWeekDag(jaar, week, 1);
        var entries = await _repo.GetUrenAsync(werknemerId, maandag, maandag.AddDays(6));
        var result = new decimal?[7];
        foreach (var g in entries.Where(e => !e.GeneratedByRule && e.Werkorder != null)
                                 .GroupBy(e => e.StartDatum))
        {
            var (_, _, dagnr) = WeekInfo(g.Key);
            result[dagnr - 1] = g.Sum(e => e.Uren);
        }
        return result;
    }

    // === VBA: Form_BeforeUpdate — all validations, same messages ===
    public async Task<string?> ValideerAsync(UrenEntry e)
    {
        if (e.TariefsoortId == 0)
            return "Geen tariefsoort ingevuld.";
        if (e.Kilometers > 0 && e.TariefsoortId != 1)
            return "Verkeerd tarief gekozen. Kilometers zijn ingevuld maar geen Travel gekozen.";
        if (string.IsNullOrWhiteSpace(e.Omschrijving))
            return "Geen omschrijving ingevoerd.";
        if (e.TariefsoortId == 5 && e.Uren != 0)
            return "Er zijn uren ingevuld met een fixed bedrag.";
        if (e.Werkorder is null)
            return "Geen geldige werkorder gekozen.";

        var wo = await _repo.GetWorkorderAsync(e.Werkorder.Value);
        if (wo is null)
            return "Deze werkorder bestaat niet.";
        if (wo.WorkorderStatus == 0)
            return "Deze werkbon is al gesloten. Hier zijn geen uren meer op te boeken " +
                   "omdat de werkorder waarschijnlijk al gefactureerd is.";
        return null; // OK
    }

    // === VBA: berekenuren + checkmidnight ===
    public static void BerekenUren(UrenEntry e)
    {
        if (e.StartTijd is null || e.EindTijd is null) return;
        var minuten = (e.EindTijd.Value - e.StartTijd.Value).TotalMinutes;
        CheckMidnight(e);
        if (minuten < 0) minuten += 24 * 60;
        e.Uren = Math.Round((decimal)(minuten / 60.0), 2);
    }

    // === VBA: uren_AfterUpdate — end time from start + hours ===
    public static void BerekenEindtijd(UrenEntry e)
    {
        if (e.StartTijd is null) return;
        e.EindTijd = e.StartTijd.Value.AddMinutes((double)(e.Uren * 60));
        CheckMidnight(e);
    }

    public static void CheckMidnight(UrenEntry e)
    {
        if (e.StartTijd is null || e.EindTijd is null) { e.EindDatum = e.StartDatum; return; }
        e.EindDatum = e.EindTijd < e.StartTijd ? e.StartDatum.AddDays(1) : e.StartDatum;
    }

    // === VBA: btnPlak_Click — copy all entries of a day/employee to another day/employee ===
    public async Task<int> PlakDagAsync(int vanWerknemer, DateOnly vanDatum, int naarWerknemer, DateOnly naarDatum)
    {
        var bron = await _repo.GetUrenVanDagAsync(vanWerknemer, vanDatum);
        foreach (var b in bron)
        {
            var kopie = new UrenEntry
            {
                StartDatum = naarDatum,
                EindDatum = naarDatum.AddDays((b.EindDatum ?? b.StartDatum).DayNumber - b.StartDatum.DayNumber),
                StartTijd = b.StartTijd, EindTijd = b.EindTijd,
                WerknemerId = naarWerknemer,
                Uren = b.Uren, Werkorder = b.Werkorder,
                Kilometers = b.Kilometers, PriveKMs = b.PriveKMs,
                Omschrijving = b.Omschrijving, TariefsoortId = b.TariefsoortId,
                FixedAmount = b.FixedAmount, FixedTypeId = b.FixedTypeId,
                UrenActiviteitId = b.UrenActiviteitId, SourceUrenId = b.SourceUrenId,
                GeneratedByRule = b.GeneratedByRule
            };
            await _repo.AddAsync(kopie);
        }
        return bron.Count;
    }

    // === VBA: WoonWerk_Click — book commute kilometers twice (there and back), only once per day ===
    public async Task<bool> BoekWoonWerkAsync(int werknemerId, DateOnly datum)
    {
        var settings = await _repo.GetGeneralSettingsAsync();
        var werknemer = await _repo.GetWerknemerAsync(werknemerId);
        if (werknemer is null) return false;

        var bestaand = await _repo.GetUrenVanDagAsync(werknemerId, datum);
        if (bestaand.Any(u => u.Werkorder == settings.ManagementKilometerOrder))
            return false; // al geboekt vandaag

        foreach (var oms in new[] { werknemer.TextWoonWerk, werknemer.TextWerkWoon })
        {
            await _repo.AddAsync(new UrenEntry
            {
                Werkorder = settings.ManagementKilometerOrder,
                StartDatum = datum, EindDatum = datum,
                WerknemerId = werknemerId,
                TariefsoortId = 1,                       // Travel
                Omschrijving = oms,
                Kilometers = werknemer.WoonWerkKilometers,
                UrenActiviteitId = 13,                   // Woon-werk verkeer
                PriveKMs = true,
                FixedTypeId = 0
            });
        }
        return true;
    }

    // === VBA: KnopVrijeUrenInboeken_Click — book 8 hours on a special order (verlof/ziek) ===
    public async Task<bool> BoekVrijeUrenAsync(int werknemerId, DateOnly datum, int specialOrderId)
    {
        var special = (await _repo.GetSpecialOrdersAsync()).FirstOrDefault(s => s.Id == specialOrderId);
        if (special is null) return false;

        await _repo.AddAsync(new UrenEntry
        {
            Werkorder = special.Order,
            Uren = 8,
            StartDatum = datum, EindDatum = datum,
            WerknemerId = werknemerId,
            TariefsoortId = 2,                           // Normaal
            FixedTypeId = 0,
            Omschrijving = special.Remark,
            Kilometers = 0,
            FixedAmount = 0
        });
        return true;
    }
}
