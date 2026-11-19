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
    private readonly IWerkorderDelenRepository _delen;
    public UrenService(IUrenRepository repo, IWerkorderDelenRepository delen) { _repo = repo; _delen = delen; }

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

        // Boeken op delen (10-11-2026): heeft de werkorder delen (uit een calculatie), dan hoort er een deel bij.
        if (await _delen.BeschikbaarAsync())
        {
            var delen = (await _delen.DelenAsync(new[] { wo.WerkbonId })).GetValueOrDefault(wo.WerkbonId) ?? new();
            if (delen.Count > 0 && e.WerkorderDeelId is null)
                return $"Kies het deel van werkorder {wo.Weergave} (onder de werkorder): de uren worden per deel bijgehouden.";
            if (e.WerkorderDeelId is int d && delen.All(x => x.DeelId != d))
                return $"Het gekozen deel hoort niet (meer) bij werkorder {wo.Weergave}; kies het deel opnieuw.";
        }
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

    // === Starttijd voorstellen en standaardpauzes (punt van Arnold, 19-11-2026) ===
    /// <summary>Het begin van de werkdag: het voorstel voor de eerste regel van een dag.</summary>
    public static readonly TimeOnly StartVanDeDag = new(7, 30);

    /// <summary>De standaardpauzes; een nieuwe regel die erover loopt kan in stukken zonder de pauzes worden opgeslagen.</summary>
    public static readonly (TimeOnly Van, TimeOnly Tot)[] StandaardPauzes =
    {
        (new(10, 0), new(10, 15)), (new(12, 30), new(13, 0)), (new(15, 0), new(15, 15)),
    };

    /// <summary>Voorstel voor de starttijd: de laatste eindtijd van de regels die er die dag al staan, anders het begin van de dag.</summary>
    public static TimeOnly VoorstelStart(IEnumerable<UrenEntry> dag)
        => dag.Where(u => u.EindTijd is not null).Select(u => u.EindTijd!.Value).DefaultIfEmpty(StartVanDeDag).Max();

    /// <summary>De pauzes die (deels) tussen start en eind liggen, op volgorde. Leeg als de regel over middernacht loopt.</summary>
    public static List<(TimeOnly Van, TimeOnly Tot)> PauzesBinnen(TimeOnly start, TimeOnly eind, IEnumerable<(TimeOnly Van, TimeOnly Tot)> pauzes)
        => eind <= start ? new() : pauzes.Where(p => p.Van < eind && p.Tot > start).OrderBy(p => p.Van).ToList();

    /// <summary>De stukken werktijd tussen start en eind, met de pauzes ertussenuit.</summary>
    public static List<(TimeOnly Van, TimeOnly Tot)> ZonderPauzes(TimeOnly start, TimeOnly eind, IEnumerable<(TimeOnly Van, TimeOnly Tot)> pauzes)
    {
        var uit = new List<(TimeOnly Van, TimeOnly Tot)>();
        var van = start;
        foreach (var p in PauzesBinnen(start, eind, pauzes))
        {
            if (p.Van > van) uit.Add((van, p.Van));
            if (p.Tot > van) van = p.Tot;
        }
        if (eind > van) uit.Add((van, eind));
        return uit;
    }

    /// <summary>
    /// De regel in stukken zonder de pauzes: elk stuk een eigen regel met dezelfde werkorder, omschrijving en tarief en zijn
    /// eigen tijden en uren. Kilometers en een vast bedrag blijven alleen op de eerste regel staan (anders tellen ze dubbel).
    /// Valt er niets te splitsen, dan komt de regel zelf terug.
    /// </summary>
    public static List<UrenEntry> SplitsOpPauzes(UrenEntry e, IEnumerable<(TimeOnly Van, TimeOnly Tot)> pauzes)
    {
        if (e.StartTijd is not TimeOnly start || e.EindTijd is not TimeOnly eind) return new() { e };
        var stukken = ZonderPauzes(start, eind, pauzes);
        if (stukken.Count == 0 || (stukken.Count == 1 && stukken[0] == (start, eind))) return new() { e };
        var uit = new List<UrenEntry>();
        foreach (var (van, tot) in stukken)
        {
            var k = Kopie(e, e.WerknemerId, e.StartDatum);
            k.StartTijd = van; k.EindTijd = tot;
            BerekenUren(k);
            if (uit.Count > 0) { k.Kilometers = 0; k.FixedAmount = 0; }
            uit.Add(k);
        }
        return uit;
    }

    // === VBA: btnPlak_Click — copy all entries of a day/employee to another day/employee ===
    public async Task<int> PlakDagAsync(int vanWerknemer, DateOnly vanDatum, int naarWerknemer, DateOnly naarDatum)
    {
        var bron = await _repo.GetUrenVanDagAsync(vanWerknemer, vanDatum);
        foreach (var b in bron)
            await _repo.AddAsync(Kopie(b, naarWerknemer, naarDatum));
        return bron.Count;
    }

    /// <summary>
    /// Losse regels naar een andere dag/werknemer (wens collega 4-11-2026: één regel of een selectie kopiëren).
    /// Anders dan Plak dag wordt elke kopie gecontroleerd zoals bij opslaan, want een losse regel komt vaak van
    /// lang geleden (werkorder intussen gesloten); zo'n regel wordt overgeslagen en genoemd.
    /// </summary>
    public async Task<(int Geplakt, List<string> Fouten)> PlakRegelsAsync(IReadOnlyList<UrenEntry> bron, int naarWerknemer, DateOnly naarDatum)
    {
        var fouten = new List<string>();
        var n = 0;
        foreach (var b in bron)
        {
            var kopie = Kopie(b, naarWerknemer, naarDatum);
            kopie.SourceUrenId = null;          // een eigen regel, niet gekoppeld aan de bron
            kopie.GeneratedByRule = false;
            var fout = await ValideerAsync(kopie);
            if (fout is not null)
            {
                fouten.Add($"‘{(string.IsNullOrWhiteSpace(b.Omschrijving) ? $"{b.Uren:0.##} uur" : b.Omschrijving)}’: {fout}");
                continue;
            }
            CheckMidnight(kopie);
            await _repo.AddAsync(kopie);
            n++;
        }
        return (n, fouten);
    }

    private static UrenEntry Kopie(UrenEntry b, int naarWerknemer, DateOnly naarDatum) => new()
    {
        StartDatum = naarDatum,
        EindDatum = naarDatum.AddDays((b.EindDatum ?? b.StartDatum).DayNumber - b.StartDatum.DayNumber),
        StartTijd = b.StartTijd, EindTijd = b.EindTijd,
        WerknemerId = naarWerknemer,
        Uren = b.Uren, Werkorder = b.Werkorder,
        Kilometers = b.Kilometers, PriveKMs = b.PriveKMs,
        Omschrijving = b.Omschrijving, TariefsoortId = b.TariefsoortId,
        FixedAmount = b.FixedAmount, FixedTypeId = b.FixedTypeId,
        UrenActiviteitId = b.UrenActiviteitId, WerkorderDeelId = b.WerkorderDeelId, SourceUrenId = b.SourceUrenId,
        GeneratedByRule = b.GeneratedByRule
    };

    // === VBA: WoonWerk_Click — book commute kilometers twice (there and back), only once per day ===
    /// <summary>Null = geboekt; anders waarom niet (staat er al, of de regel kan niet: zelfde controles als bij opslaan).</summary>
    public async Task<string?> BoekWoonWerkAsync(int werknemerId, DateOnly datum)
    {
        var settings = await _repo.GetGeneralSettingsAsync();
        var werknemer = await _repo.GetWerknemerAsync(werknemerId);
        if (werknemer is null) return "Deze werknemer is niet gevonden.";
        if (settings.ManagementKilometerOrder <= 0)
            return "Er is geen werkorder voor woon-werk kilometers ingesteld (General_settings.ManagementKilometerOrder).";

        var bestaand = await _repo.GetUrenVanDagAsync(werknemerId, datum);
        if (bestaand.Any(u => u.Werkorder == settings.ManagementKilometerOrder))
            return "Woon-werk kilometers staan al geboekt op deze dag.";

        var regels = new[] { werknemer.TextWoonWerk, werknemer.TextWerkWoon }.Select(oms => new UrenEntry
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
        }).ToList();
        // eerst alles controleren, dan pas boeken: anders staat alleen de heenreis er
        foreach (var e in regels)
            if (await ValideerAsync(e) is { } fout) return "Woon-werk kilometers boeken kan niet. " + fout;
        foreach (var e in regels) await _repo.AddAsync(e);
        return null;
    }

    // === VBA: KnopVrijeUrenInboeken_Click — book 8 hours on a special order (verlof/ziek) ===
    /// <summary>
    /// Boekt 8 uur op de werkorder van deze soort vrije uren. Geeft terug of het gelukt is en de tekst voor de melding;
    /// bij gelukt staat de werkorder erin, zodat een verkeerd ingestelde werkorder meteen opvalt. Dezelfde controles als
    /// bij opslaan (werkorder bestaat en is open): zonder die controle weigerde de database de regel en liep de pagina vast.
    /// </summary>
    public async Task<(bool Gelukt, string Tekst)> BoekVrijeUrenAsync(int werknemerId, DateOnly datum, int specialOrderId)
    {
        var special = (await _repo.GetSpecialOrdersAsync()).FirstOrDefault(s => s.Id == specialOrderId);
        if (special is null) return (false, "Deze soort vrije uren bestaat niet (meer).");

        var e = new UrenEntry
        {
            Werkorder = special.Order,
            Uren = 8,
            StartDatum = datum, EindDatum = datum,
            WerknemerId = werknemerId,
            TariefsoortId = 2,                           // Normaal
            FixedTypeId = 0,
            Omschrijving = special.Remark.Length > 0 ? special.Remark : special.Weergave,
            Kilometers = 0,
            FixedAmount = 0
        };
        if (await ValideerAsync(e) is { } fout)
            return (false, $"{special.Weergave} boeken kan niet. {fout} De werkorder voor {special.Weergave} staat in de instellingen (GeneralSettingsSpecialOrders); vraag de beheerder die bij te werken.");
        await _repo.AddAsync(e);
        var wo = await _repo.GetWorkorderAsync(special.Order);
        return (true, $"8 uur {special.Weergave} geboekt op {wo?.VolledigeTekst ?? "werkorder " + special.Order}.");
    }
}
