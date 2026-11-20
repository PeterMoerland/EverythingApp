using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// De rekenregels van Artikelen ▸ Solar-prijzen, op één plek zodat het scherm en de wekelijkse taak hetzelfde doen
/// (besluit gebruiker 20-11-2026: kleine verschillen automatisch, grote eerst nakijken, grens 15%).
/// </summary>
public static class SolarVergelijk
{
    /// <summary>Binnen 1% noemen we de prijzen gelijk (afronding).</summary>
    public const decimal Gelijk = 0.01m;
    /// <summary>Tot en met dit verschil mag een prijs zonder nakijken worden overgenomen.</summary>
    public const decimal StandaardGrens = 0.15m;

    /// <param name="PerVe">De Solar-prijs is vergeleken met onze prijs per VE (anders per stuk).</param>
    /// <param name="Verschil">Netto Solar ten opzichte van netto bij ons, als fractie; null = niet te vergelijken.</param>
    /// <param name="NieuwPrijsPerVe">Wat PrijsPerVE wordt als je de Solar-prijs overneemt; null = Solar gaf geen prijs.</param>
    public sealed record Uitkomst(bool PerVe, decimal OnsBruto, decimal OnsNetto, decimal? Verschil,
                                  decimal? NieuwPrijsPerVe, decimal? NieuweKorting)
    {
        public bool IsGelijk => Verschil is decimal v && Math.Abs(v) <= Gelijk;
        /// <summary>Een echt verschil dat klein genoeg is om zonder nakijken over te nemen.</summary>
        public bool Klein(decimal grens) => Verschil is decimal v && Math.Abs(v) > Gelijk && Math.Abs(v) <= grens;
        public bool Groot(decimal grens) => Verschil is decimal v && Math.Abs(v) > grens;
    }

    /// <summary>
    /// Vergelijkt onze prijs met die van Solar. Solar rekent per eigen eenheid: per stuk of meter, of per verpakking
    /// (klinknagels per blister van 50), en de API zegt niet betrouwbaar welke. Is het voor deze regel vastgelegd
    /// (<paramref name="vasteBasis"/>), dan geldt dat; anders de basis die het dichtst bij onze prijs uitkomt.
    /// Overnemen: bruto = de brutoprijs van Solar (per VE omgerekend), korting zo dat ons netto het netto van Solar is.
    /// </summary>
    public static Uitkomst Reken(LeverancierArtikel a, SolarPrijs? solar, bool? vasteBasis)
    {
        var netto = solar?.NettoPerStuk is decimal n && n > 0 ? n : (decimal?)null;
        var perVe = vasteBasis ?? DichterBijVe(a, netto);
        var onsBruto = perVe ? a.PrijsPerVe : a.BrutoPerStuk;
        var onsNetto = onsBruto * (1 - a.Korting);
        decimal? verschil = netto is decimal s && onsNetto > 0 ? (s - onsNetto) / onsNetto : null;

        decimal? nieuwPrijs = null, nieuweKorting = null;
        if (netto is decimal nt)
        {
            // geen (geldige) brutoprijs: netto als bruto, zonder korting
            var bruto = solar!.BrutoPerStuk is decimal b && b >= nt ? b : nt;
            nieuwPrijs = Math.Round(perVe ? bruto : bruto * a.StuksPerVe, 4, MidpointRounding.AwayFromZero);
            nieuweKorting = Math.Round(1 - nt / bruto, 6, MidpointRounding.AwayFromZero);
        }
        return new Uitkomst(perVe, onsBruto, onsNetto, verschil, nieuwPrijs, nieuweKorting);
    }

    private static bool DichterBijVe(LeverancierArtikel a, decimal? netto)
    {
        if (netto is not decimal s || a.StuksPerVe <= 1 || a.NettoPerStuk <= 0) return false;
        var perStuk = Math.Abs(Math.Log((double)(s / a.NettoPerStuk)));
        var perVe = Math.Abs(Math.Log((double)(s / (a.NettoPerStuk * a.StuksPerVe))));
        return perVe < perStuk;
    }
}

/// <summary>Een overgenomen prijs uit het logboek.</summary>
public record SolarLogRegel(DateTime Op, string Door, string Bron, int ArtikelId, string Artikel, decimal? OudPrijsPerVe, decimal? OudKorting,
                            decimal NieuwPrijsPerVe, decimal NieuweKorting);

/// <summary>Een keer dat de wekelijkse taak draaide.</summary>
public record SolarRun(DateTime Op, int Bijgewerkt, int NaTeKijken, int ZonderPrijs, string Fout);

/// <summary>
/// Opslag bij het overnemen van Solar-prijzen (SolarPrijzen_MariaDB.sql): de vastgelegde basis per regel, het logboek en
/// de runs van de wekelijkse taak. Het bijwerken van ArtikelSupplier en de logregel gaan in één transactie.
/// </summary>
public interface ISolarPrijsRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabellenAanwezigAsync();
    /// <summary>ArtikelSupplierID → per VE (true) of per stuk (false), voor de regels waar dat is vastgelegd.</summary>
    Task<Dictionary<int, bool>> BasisAsync();
    Task ZetBasisAsync(int artikelSupplierId, bool perVe, int? door);
    /// <summary>De prijs overnemen: PrijsPerVE en Korting van de regel, met een logregel. <paramref name="door"/> null = de taak.</summary>
    Task NeemOverAsync(LeverancierArtikel artikel, decimal nieuwPrijsPerVe, decimal nieuweKorting, SolarPrijs solar, bool perVe, int? door);
    Task<List<SolarLogRegel>> LogAsync(int max = 100);
    Task<SolarRun?> LaatsteRunAsync();
    Task BewaarRunAsync(SolarRun run);
}

public class GeenSolarPrijsRepository : ISolarPrijsRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabellenAanwezigAsync() => Task.FromResult(false);
    public Task<Dictionary<int, bool>> BasisAsync() => Task.FromResult(new Dictionary<int, bool>());
    public Task ZetBasisAsync(int artikelSupplierId, bool perVe, int? door) => Task.CompletedTask;
    public Task NeemOverAsync(LeverancierArtikel artikel, decimal nieuwPrijsPerVe, decimal nieuweKorting, SolarPrijs solar, bool perVe, int? door) => Task.CompletedTask;
    public Task<List<SolarLogRegel>> LogAsync(int max = 100) => Task.FromResult(new List<SolarLogRegel>());
    public Task<SolarRun?> LaatsteRunAsync() => Task.FromResult<SolarRun?>(null);
    public Task BewaarRunAsync(SolarRun run) => Task.CompletedTask;
}

public class MySqlSolarPrijsRepository : ISolarPrijsRepository
{
    private readonly string _cs;
    private bool _tabellen;
    public MySqlSolarPrijsRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<bool> TabellenAanwezigAsync()
    {
        if (_tabellen) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() " +
                "   AND TABLE_NAME IN ('SolarArtikelBasis', 'SolarPrijsLog', 'SolarPrijsRuns')", cn);
            _tabellen = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 3;
        }
        catch { }
        return _tabellen;
    }

    public async Task<Dictionary<int, bool>> BasisAsync()
    {
        var uit = new Dictionary<int, bool>();
        if (!await TabellenAanwezigAsync()) return uit;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT `ArtikelSupplierID`, `PerVE` FROM `SolarArtikelBasis`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit[r.GetInt32(0)] = Convert.ToInt32(r.GetValue(1)) != 0;
        return uit;
    }

    public async Task ZetBasisAsync(int artikelSupplierId, bool perVe, int? door)
    {
        if (!await TabellenAanwezigAsync()) throw new InvalidOperationException("De tabellen voor Solar-prijzen ontbreken (SolarPrijzen_MariaDB.sql).");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `SolarArtikelBasis` (`ArtikelSupplierID`, `PerVE`, `GezetDoor`) VALUES (@id, @p, @door) " +
            "ON DUPLICATE KEY UPDATE `PerVE` = @p, `GezetDoor` = @door", cn);
        cmd.Parameters.AddWithValue("@id", artikelSupplierId);
        cmd.Parameters.AddWithValue("@p", perVe ? 1 : 0);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task NeemOverAsync(LeverancierArtikel artikel, decimal nieuwPrijsPerVe, decimal nieuweKorting, SolarPrijs solar, bool perVe, int? door)
    {
        if (artikel.Id <= 0) throw new ArgumentException("Deze regel heeft geen id.");
        if (nieuwPrijsPerVe <= 0 || nieuweKorting is < 0 or >= 1) throw new ArgumentException("De nieuwe prijs of korting is niet geldig.");
        if (!await TabellenAanwezigAsync()) throw new InvalidOperationException("De tabellen voor Solar-prijzen ontbreken (SolarPrijzen_MariaDB.sql).");
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await using (var upd = new MySqlCommand(
            "UPDATE `ArtikelSupplier` SET `PrijsPerVE` = @p, `Korting` = @k WHERE `ArtikelSupplierID` = @id", cn, tx))
        {
            upd.Parameters.AddWithValue("@p", nieuwPrijsPerVe);
            upd.Parameters.AddWithValue("@k", nieuweKorting);
            upd.Parameters.AddWithValue("@id", artikel.Id);
            if (await upd.ExecuteNonQueryAsync() == 0)
            {
                // niets geraakt: de regel bestaat niet meer, of de waarden waren al gelijk (dan is er ook niets te loggen)
                await tx.RollbackAsync();
                return;
            }
        }
        await using (var log = new MySqlCommand(
            "INSERT INTO `SolarPrijsLog` (`ArtikelSupplierID`, `ArtikelID`, `Op`, `Door`, `Bron`, `OudPrijsPerVE`, `OudKorting`, " +
            "   `NieuwPrijsPerVE`, `NieuwKorting`, `SolarBruto`, `SolarNetto`, `PerVE`) " +
            "VALUES (@id, @a, NOW(), @door, @bron, @op, @ok, @np, @nk, @sb, @sn, @pv)", cn, tx))
        {
            log.Parameters.AddWithValue("@id", artikel.Id);
            log.Parameters.AddWithValue("@a", artikel.ArtikelId);
            log.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            log.Parameters.AddWithValue("@bron", door is null ? "auto" : "hand");
            log.Parameters.AddWithValue("@op", artikel.PrijsPerVe);
            log.Parameters.AddWithValue("@ok", artikel.Korting);
            log.Parameters.AddWithValue("@np", nieuwPrijsPerVe);
            log.Parameters.AddWithValue("@nk", nieuweKorting);
            log.Parameters.AddWithValue("@sb", (object?)solar.BrutoPerStuk ?? DBNull.Value);
            log.Parameters.AddWithValue("@sn", (object?)solar.NettoPerStuk ?? DBNull.Value);
            log.Parameters.AddWithValue("@pv", perVe ? 1 : 0);
            await log.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task<List<SolarLogRegel>> LogAsync(int max = 100)
    {
        var lijst = new List<SolarLogRegel>();
        if (!await TabellenAanwezigAsync()) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT l.`Op`, COALESCE(w.`NaamVoluit`, ''), l.`Bron`, l.`ArtikelID`, COALESCE(a.`ArtikelCodeMDE`, ''), " +
            "       l.`OudPrijsPerVE`, l.`OudKorting`, l.`NieuwPrijsPerVE`, l.`NieuwKorting` " +
            "  FROM `SolarPrijsLog` l LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = l.`Door` " +
            "  LEFT JOIN `Artikelen` a ON a.`ArtikelID` = l.`ArtikelID` ORDER BY l.`LogID` DESC LIMIT @max", cn);
        cmd.Parameters.AddWithValue("@max", Math.Clamp(max, 1, 1000));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new SolarLogRegel(r.GetDateTime(0), Convert.ToString(r.GetValue(1)) ?? "", r.GetString(2), r.GetInt32(3), Convert.ToString(r.GetValue(4)) ?? "",
                                        r.IsDBNull(5) ? null : Convert.ToDecimal(r.GetValue(5)), r.IsDBNull(6) ? null : Convert.ToDecimal(r.GetValue(6)),
                                        Convert.ToDecimal(r.GetValue(7)), Convert.ToDecimal(r.GetValue(8))));
        return lijst;
    }

    public async Task<SolarRun?> LaatsteRunAsync()
    {
        if (!await TabellenAanwezigAsync()) return null;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Op`, `Bijgewerkt`, `NaTeKijken`, `ZonderPrijs`, COALESCE(`Fout`, '') FROM `SolarPrijsRuns` ORDER BY `RunID` DESC LIMIT 1", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new SolarRun(r.GetDateTime(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetString(4)) : null;
    }

    public async Task BewaarRunAsync(SolarRun run)
    {
        if (!await TabellenAanwezigAsync()) return;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `SolarPrijsRuns` (`Op`, `Bijgewerkt`, `NaTeKijken`, `ZonderPrijs`, `Fout`) VALUES (@op, @b, @n, @z, @f)", cn);
        cmd.Parameters.AddWithValue("@op", run.Op);
        cmd.Parameters.AddWithValue("@b", run.Bijgewerkt);
        cmd.Parameters.AddWithValue("@n", run.NaTeKijken);
        cmd.Parameters.AddWithValue("@z", run.ZonderPrijs);
        cmd.Parameters.AddWithValue("@f", run.Fout.Length > 0 ? (run.Fout.Length > 500 ? run.Fout[..500] : run.Fout) : DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// De wekelijkse taak (besluit gebruiker 20-11-2026): één keer per week, in het weekend, de Solar-prijzen ophalen en de
/// kleine verschillen (tot en met Solar:AutoGrens, standaard 15%) overnemen. Grote verschillen blijven staan voor het
/// nakijken in Artikelen ▸ Solar-prijzen. Staat uit tot Solar:AutoBijwerken = true (eerst de achterstand met de hand).
/// Wie de melding "Solar-prijzen bijgewerkt" heeft aangezet (Werknemers ▸ Meldingen) hoort wat er gebeurd is.
/// </summary>
public sealed class SolarPrijsWaker : BackgroundService
{
    public const string MeldingSoort = "solarprijzen";
    public const string Pagina = "artikelen/solar-prijzen";

    private readonly SolarKoppeling _solar;
    private readonly IArtikelRepository _artikelen;
    private readonly ISolarPrijsRepository _repo;
    private readonly IMeldingRepository _meldingen;
    private readonly IMeldingVoorkeurRepository _voorkeuren;
    private readonly MeldingSignaal _signaal;
    private readonly IConfiguration _config;
    private readonly ILogger<SolarPrijsWaker> _log;

    public SolarPrijsWaker(SolarKoppeling solar, IArtikelRepository artikelen, ISolarPrijsRepository repo, IMeldingRepository meldingen,
                           IMeldingVoorkeurRepository voorkeuren, MeldingSignaal signaal, IConfiguration config, ILogger<SolarPrijsWaker> log)
    {
        _solar = solar; _artikelen = artikelen; _repo = repo; _meldingen = meldingen; _voorkeuren = voorkeuren; _signaal = signaal;
        _config = config; _log = log;
    }

    public static decimal Grens(IConfiguration config)
        => decimal.TryParse(config["Solar:AutoGrens"], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var g)
           && g is > 0 and <= 0.5m ? g : SolarVergelijk.StandaardGrens;

    private bool Aan => _config.GetValue<bool>("Solar:AutoBijwerken");
    private DayOfWeek Dag => Enum.TryParse<DayOfWeek>(_config["Solar:AutoDag"], true, out var d) ? d : DayOfWeek.Saturday;
    private int Uur => int.TryParse(_config["Solar:AutoUur"], out var u) && u is >= 0 and <= 23 ? u : 2;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(4), stop); } catch { return; }
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (Aan && _solar.Ingesteld && _repo.Beschikbaar && await IsHetTijd()) await VoerUitAsync(stop);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _log.LogWarning(ex, "Solar-prijzen: de wekelijkse taak mislukte."); }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stop); } catch { return; }
        }
    }

    /// <summary>Het is de ingestelde dag, na het ingestelde uur, en er is vandaag nog niet gedraaid.</summary>
    private async Task<bool> IsHetTijd()
    {
        var nu = DateTime.Now;
        if (nu.DayOfWeek != Dag || nu.Hour < Uur) return false;
        if (!await _repo.TabellenAanwezigAsync()) return false;
        return (await _repo.LaatsteRunAsync())?.Op.Date != nu.Date;
    }

    private async Task VoerUitAsync(CancellationToken stop)
    {
        var grens = Grens(_config);
        int bijgewerkt = 0, nakijken = 0, zonderPrijs = 0;
        var fout = "";
        try
        {
            var artikelen = await _artikelen.ArtikelenVanLeverancierAsync(_solar.LeverancierId);
            var prijzen = await _solar.PrijzenAsync(artikelen.Select(a => a.Artikelnummer).ToList(), null, stop);
            var basis = await _repo.BasisAsync();
            foreach (var a in artikelen)
            {
                stop.ThrowIfCancellationRequested();
                var solar = prijzen.GetValueOrDefault(a.Artikelnummer.Trim());
                var u = SolarVergelijk.Reken(a, solar, basis.TryGetValue(a.Id, out var vast) ? vast : null);
                if (solar is null || u.Verschil is null) { zonderPrijs++; continue; }
                if (u.Groot(grens)) { nakijken++; continue; }
                if (!u.Klein(grens) || u.NieuwPrijsPerVe is not decimal prijs || u.NieuweKorting is not decimal korting) continue;
                try
                {
                    await _repo.NeemOverAsync(a, prijs, korting, solar, u.PerVe, door: null);
                    bijgewerkt++;
                }
                catch (Exception ex) { _log.LogWarning(ex, "Solar-prijzen: artikel {Artikel} bijwerken mislukte", a.ArtikelId); }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            fout = ex.Message;
            _log.LogWarning(ex, "Solar-prijzen: ophalen of bijwerken mislukte.");
        }
        // ook een mislukte poging vastleggen: anders probeert de taak het elk kwartier opnieuw, de hele dag
        await _repo.BewaarRunAsync(new SolarRun(DateTime.Now, bijgewerkt, nakijken, zonderPrijs, fout));
        _log.LogInformation("Solar-prijzen, wekelijkse taak: {Bij} bijgewerkt, {Na} na te kijken, {Zonder} zonder prijs{Fout}",
                            bijgewerkt, nakijken, zonderPrijs, fout.Length > 0 ? ", fout: " + fout : "");

        try
        {
            var wie = await _voorkeuren.WieWilAsync(MeldingSoort);
            if (wie.Count == 0 || !_meldingen.Beschikbaar) return;
            var tekst = fout.Length > 0
                ? $"Solar-prijzen: de wekelijkse controle is mislukt ({(fout.Length > 120 ? fout[..120] + "…" : fout)})."
                : $"Solar-prijzen: {bijgewerkt} prijs{(bijgewerkt == 1 ? "" : "en")} automatisch bijgewerkt, {nakijken} met een groot verschil om na te kijken.";
            foreach (var w in wie) await _meldingen.MaakAsync(w, MeldingSoort, null, tekst);
            _signaal.Geef();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Solar-prijzen: de melding versturen mislukte."); }
    }
}
