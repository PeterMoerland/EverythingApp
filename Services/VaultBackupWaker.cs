namespace EverythingApp.Services;

/// <summary>De actuele toestand van de Vault-backups, voor de balk in de app.</summary>
public sealed record VaultBackupStand(
    int Aantal,
    DateTime? Nieuwste,
    double NieuwsteGB,
    string Probleem,          // leeg = alles in orde
    IReadOnlyList<string> LaatstOpgeruimd,
    DateTime Gekeken);

/// <summary>
/// Houdt de dagelijkse Autodesk Vault-backup in de gaten, zoals het oude
/// Access dat bij het opstarten deed — maar dan doorlopend, en met de
/// schoonmaak erbij die tot nu toe handwerk was.
///
/// Elk half uur kijkt hij in de backupmap. Hij ruimt oude backups op tot het
/// ingestelde aantal (standaard 2), maar alléén als de nieuwste vers en
/// compleet oogt — er wordt nooit geveegd terwijl de laatste redding twijfel-
/// achtig is. Is er ná de deadline nog geen backup van vandaag, of oogt de
/// nieuwste onaf, dan zet hij een probleem klaar dat de directie als balk in
/// de app ziet.
///
/// Instellingen (appsettings, VaultBackup):
///   Pad      — de map waar Vault zijn backups neerzet (submap per backup).
///              Leeg = deze waker staat uit.
///   Bewaren  — hoeveel backups er blijven staan (standaard 2).
///   KlaarOm  — hoe laat de backup van vandaag er uiterlijk moet zijn
///              (standaard 07:00); vóór die tijd wordt er niet gemopperd.
///   Dagen    — op welke avonden de backup start (de taakplanner op de server),
///              standaard "ma,di,wo,do,vr": geen backup in de nacht van zaterdag en
///              zondag, dus op maandag geen melding als die van vrijdagnacht er is
///              (wens gebruiker 9-11-2026). Ook Engels kan (mon,tue,...).
/// </summary>
public sealed class VaultBackupWaker : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<VaultBackupWaker> _log;

    /// <summary>Laatst bekende stand; null zolang er nog niet gekeken is.</summary>
    public VaultBackupStand? Stand { get; private set; }

    /// <summary>Gaat af na elke kijkronde; de balk in de app ververst zich dan.</summary>
    public event Action? Gewijzigd;

    public VaultBackupWaker(IConfiguration config, ILogger<VaultBackupWaker> log)
    {
        _config = config;
        _log = log;
    }

    private string Pad => _config["VaultBackup:Pad"] ?? "";
    private int Bewaren => int.TryParse(_config["VaultBackup:Bewaren"], out var n) && n >= 1 ? n : 2;

    private TimeSpan KlaarOm => TimeSpan.TryParse(_config["VaultBackup:KlaarOm"], out var t)
        ? t : new TimeSpan(7, 0, 0);

    public bool Beschikbaar => Pad.Length > 0;

    /// <summary>De avonden waarop de backup start (VaultBackup:Dagen); leeg of onleesbaar = maandag t/m vrijdag.</summary>
    private HashSet<DayOfWeek> Dagen
    {
        get
        {
            var dagen = new HashSet<DayOfWeek>();
            foreach (var deel in (_config["VaultBackup:Dagen"] ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var d = deel.Trim().ToLowerInvariant();
                DayOfWeek? dag = d.Length < 2 ? null : d[..2] switch
                {
                    "ma" or "mo" => DayOfWeek.Monday,
                    "di" or "tu" => DayOfWeek.Tuesday,
                    "wo" or "we" => DayOfWeek.Wednesday,
                    "do" or "th" => DayOfWeek.Thursday,
                    "vr" or "fr" => DayOfWeek.Friday,
                    "za" or "sa" => DayOfWeek.Saturday,
                    "zo" or "su" => DayOfWeek.Sunday,
                    _ => null,
                };
                if (dag is DayOfWeek x) dagen.Add(x);
            }
            return dagen.Count > 0 ? dagen
                : new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        }
    }

    /// <summary>
    /// De avond van de laatste backup die er nu al moet zijn: de meest recente backupavond waarvan de volgende ochtend
    /// KlaarOm al voorbij is. Op maandagochtend is dat vrijdag (de nacht van vrijdag op zaterdag).
    /// </summary>
    public static DateTime VerwachteAvond(DateTime nu, IReadOnlySet<DayOfWeek> dagen, TimeSpan klaarOm)
    {
        for (var i = 0; i < 8; i++)
        {
            var avond = nu.Date.AddDays(-i);
            if (dagen.Contains(avond.DayOfWeek) && avond.AddDays(1).Add(klaarOm) <= nu) return avond;
        }
        return nu.Date.AddDays(-7);
    }

    private static readonly System.Globalization.CultureInfo Nl = new("nl-NL");

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!Beschikbaar)
        {
            _log.LogInformation("VaultBackup:Pad is leeg; de backup-waker staat uit.");
            return;
        }

        // Even wachten tot de app op gang is; daarna elk half uur kijken.
        // Vaker heeft geen zin: de backup komt één keer per nacht.
        await Task.Delay(TimeSpan.FromSeconds(30), stop);

        while (!stop.IsCancellationRequested)
        {
            try { Kijk(); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Kijken naar de Vault-backups mislukte.");
                Stand = new VaultBackupStand(0, null, 0,
                    "De backupmap is niet te lezen: " + ex.Message,
                    Array.Empty<string>(), DateTime.Now);
            }
            Gewijzigd?.Invoke();

            try { await Task.Delay(TimeSpan.FromMinutes(30), stop); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Kijk()
    {
        var wortel = new DirectoryInfo(Pad);
        if (!wortel.Exists)
        {
            Stand = new VaultBackupStand(0, null, 0,
                $"De backupmap {Pad} bestaat niet (of de service mag er niet bij).",
                Array.Empty<string>(), DateTime.Now);
            return;
        }

        // Elke submap "VaultBackup_..." is één backup; Vault zet er per nacht
        // een neer (VaultBackup_2026_08_21_00_00_28). Bewust alléén mappen met
        // die naam: wat er verder ooit in deze map belandt (Backup.Txt, een
        // losse testmap) wordt nooit aangeraakt, laat staan opgeruimd.
        var backups = wortel.GetDirectories("VaultBackup_*")
            .Select(map => (Map: map, Moment: NieuwsteBestandIn(map), GrootteB: GrootteVan(map)))
            .OrderByDescending(b => b.Moment)
            .ToList();

        var opgeruimd = new List<string>();
        var probleem = "";

        if (backups.Count == 0)
        {
            probleem = "Er staat geen enkele Vault-backup in de map.";
        }
        else
        {
            var nieuwste = backups[0];

            // Compleet? Twee nuchtere controles: er is al een half uur niets
            // meer aan geschreven (de kopieerslag is dus klaar), en hij is niet
            // verdacht veel kleiner dan zijn voorganger.
            var inRust = DateTime.Now - nieuwste.Moment > TimeSpan.FromMinutes(30);
            var grootGenoeg = backups.Count < 2
                || nieuwste.GrootteB >= backups[1].GrootteB * 0.5;

            // Ontbreekt de backup van de laatste backupavond? De backup start rond middernacht; alles van na het middaguur
            // van die avond telt (een backup die iets eerder of later start, valt er ook onder).
            var avond = VerwachteAvond(DateTime.Now, Dagen, KlaarOm);
            if (nieuwste.Moment < avond.AddHours(12))
                probleem = $"De nieuwste Vault-backup is van {nieuwste.Moment:dd-MM HH:mm} — "
                         + $"die van {avond.ToString("dddd d-M", Nl)} (de nacht naar {avond.AddDays(1).ToString("dddd", Nl)}) is er dus niet.";
            else if (inRust && !grootGenoeg)
                probleem = $"De nieuwste Vault-backup ({nieuwste.Moment:dd-MM HH:mm}) is "
                         + $"opvallend klein ({Gb(nieuwste.GrootteB):0.0} GB tegen "
                         + $"{Gb(backups[1].GrootteB):0.0} GB de vorige keer) — mogelijk afgebroken.";

            // Opruimen: alleen als de nieuwste vers, in rust en op formaat is.
            // Anders blijft alles staan — liever te veel backups dan één te weinig.
            if (probleem.Length == 0 && inRust && grootGenoeg && backups.Count > Bewaren)
            {
                foreach (var (map, moment, _) in backups.Skip(Bewaren))
                {
                    try
                    {
                        map.Delete(recursive: true);
                        opgeruimd.Add($"{map.Name} ({moment:dd-MM})");
                        _log.LogInformation("Vault-backup {Map} opgeruimd (ouder dan de nieuwste {N}).",
                            map.Name, Bewaren);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Vault-backup {Map} kon niet worden opgeruimd.", map.Name);
                        probleem = $"Opruimen van {map.Name} mislukte: {ex.Message}";
                    }
                }
            }
        }

        // De stand ná eventueel opruimen opnieuw tellen, zodat de balk klopt.
        var na = wortel.Exists ? wortel.GetDirectories("VaultBackup_*").Length : 0;
        var top = backups.FirstOrDefault();
        Stand = new VaultBackupStand(
            na,
            backups.Count > 0 ? top.Moment : null,
            backups.Count > 0 ? Gb(top.GrootteB) : 0,
            probleem,
            opgeruimd.Count > 0 ? opgeruimd : Stand?.LaatstOpgeruimd ?? Array.Empty<string>(),
            DateTime.Now);
    }

    /// <summary>Wanneer er voor het laatst iets in deze map (of dieper) is geschreven.</summary>
    private static DateTime NieuwsteBestandIn(DirectoryInfo map)
    {
        var nieuwste = map.LastWriteTime;
        try
        {
            foreach (var bestand in map.EnumerateFiles("*", SearchOption.AllDirectories))
                if (bestand.LastWriteTime > nieuwste) nieuwste = bestand.LastWriteTime;
        }
        catch { /* half leesbare map: dan telt de mapdatum */ }
        return nieuwste;
    }

    private static long GrootteVan(DirectoryInfo map)
    {
        long totaal = 0;
        try
        {
            foreach (var bestand in map.EnumerateFiles("*", SearchOption.AllDirectories))
                totaal += bestand.Length;
        }
        catch { }
        return totaal;
    }

    private static double Gb(long bytes) => bytes / 1_073_741_824.0;
    private static double Gb(double bytes) => bytes / 1_073_741_824.0;
}
