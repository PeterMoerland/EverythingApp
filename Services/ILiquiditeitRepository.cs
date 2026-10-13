using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>De gegevens voor het liquiditeitsoverzicht. Rekenen gebeurt in Liquiditeitsrekenaar.</summary>
public interface ILiquiditeitRepository
{
    bool Beschikbaar { get; }

    Task<List<LiquiditeitBv>> BvsAsync();

    /// <summary>Per bankrekening van deze BV's het saldo na de laatste transactie.</summary>
    Task<List<Banksaldo>> BanksaldiAsync(IReadOnlyCollection<int> bvIds);

    /// <summary>Onbetaalde verkoopfacturen (niet oninbaar) en inkoopfacturen (niet via SEPA) van deze BV's.</summary>
    Task<List<OpenstaandeFactuur>> OpenstaandAsync(IReadOnlyCollection<int> bvIds);

    /// <summary>Te betalen btw (verkoop min inkoop) per jaar en kwartaal, vanaf dit jaar, voor deze BV's.</summary>
    Task<Dictionary<(int Jaar, int Kwartaal), decimal>> BtwPerKwartaalAsync(IReadOnlyCollection<int> bvIds, int vanafJaar);
}

public class GeenLiquiditeitRepository : ILiquiditeitRepository
{
    public bool Beschikbaar => false;
    public Task<List<LiquiditeitBv>> BvsAsync() => Task.FromResult(new List<LiquiditeitBv>());
    public Task<List<Banksaldo>> BanksaldiAsync(IReadOnlyCollection<int> bvIds) => Task.FromResult(new List<Banksaldo>());
    public Task<List<OpenstaandeFactuur>> OpenstaandAsync(IReadOnlyCollection<int> bvIds)
        => Task.FromResult(new List<OpenstaandeFactuur>());
    public Task<Dictionary<(int Jaar, int Kwartaal), decimal>> BtwPerKwartaalAsync(IReadOnlyCollection<int> bvIds, int vanafJaar)
        => Task.FromResult(new Dictionary<(int, int), decimal>());
}

public class MySqlLiquiditeitRepository : ILiquiditeitRepository
{
    private readonly string _cs;

    public MySqlLiquiditeitRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    /// <summary>De id's als lijst voor IN (...); ints, dus veilig in de tekst.</summary>
    private static string Lijst(IReadOnlyCollection<int> ids) => ids.Count == 0 ? "0" : string.Join(",", ids);

    public async Task<List<LiquiditeitBv>> BvsAsync()
    {
        var lijst = new List<LiquiditeitBv>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `id_supplier`, COALESCE(`Afkorting`, ''), COALESCE(`name`, ''), COALESCE(`MDEBV`, 0) <> 0 " +
            "  FROM `Relations` WHERE COALESCE(`Werkmaatschappij`, 0) <> 0 OR COALESCE(`MDEBV`, 0) <> 0 " +
            " ORDER BY `Afkorting`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new LiquiditeitBv(r.GetInt32(0), r.GetString(1), r.GetString(2), Convert.ToInt64(r.GetValue(3)) != 0));
        return lijst;
    }

    public async Task<List<Banksaldo>> BanksaldiAsync(IReadOnlyCollection<int> bvIds)
    {
        var lijst = new List<Banksaldo>();
        await using var cn = await OpenAsync();

        // Access nam DLast zonder volgorde ('een' laatste regel); hier echt de nieuwste per rekening.
        await using var cmd = new MySqlCommand(
            "SELECT r.`Rekeningnummer`, COALESCE(r.`OmschrijvingRekening`, ''), r.`BVid`, " +
            "       (SELECT b.`Saldo na trn` FROM `banktransacties` b " +
            "         WHERE REPLACE(b.`Rekening`, ' ', '') = REPLACE(r.`Rekeningnummer`, ' ', '') " +
            "         ORDER BY b.`Datum` DESC, b.`VolgNr` DESC, b.`BankTransacties_ID` DESC LIMIT 1), " +
            "       (SELECT MAX(b.`Datum`) FROM `banktransacties` b " +
            "         WHERE REPLACE(b.`Rekening`, ' ', '') = REPLACE(r.`Rekeningnummer`, ' ', '')) " +
            "  FROM `Bankrekeningen` r " +
            $" WHERE r.`BVid` IN ({Lijst(bvIds)}) ORDER BY r.`BVid`, r.`Bankrekeningen_id`", cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lijst.Add(new Banksaldo(rd.GetString(0), rd.GetString(1), rd.GetInt32(2),
                rd.IsDBNull(3) ? 0 : Convert.ToDecimal(rd.GetValue(3)),
                rd.IsDBNull(4) ? null : DateOnly.FromDateTime(rd.GetDateTime(4))));
        return lijst;
    }

    public async Task<List<OpenstaandeFactuur>> OpenstaandAsync(IReadOnlyCollection<int> bvIds)
    {
        var lijst = new List<OpenstaandeFactuur>();
        await using var cn = await OpenAsync();

        // Verkoop: onbetaald en niet oninbaar. Oude rommel van voor 2018 telt niet (zoals Access bij te laat).
        await using (var cmd = new MySqlCommand(
            "SELECT f.`Invoice-id`, COALESCE(f.`Invoicenumber`, ''), COALESCE(k.`name`, ''), f.`Werkmaatschappij`, " +
            "       COALESCE(bv.`Afkorting`, ''), DATE_ADD(f.`Date invoice`, INTERVAL COALESCE(f.`Betalingstermijn`, 0) DAY), " +
            "       COALESCE(f.`Total incl`, 0), COALESCE(f.`SentToClient`, 0) <> 0 " +
            "  FROM `Invoices` f " +
            "  LEFT JOIN `Relations` k ON k.`id_supplier` = f.`Customer-id` " +
            "  LEFT JOIN `Relations` bv ON bv.`id_supplier` = f.`Werkmaatschappij` " +
            " WHERE COALESCE(f.`Betaald`, 0) = 0 AND COALESCE(f.`On-Inbaar`, 0) = 0 AND f.`Date invoice` IS NOT NULL " +
            "   AND f.`Date invoice` >= '2018-01-01' " +
            $"  AND f.`Werkmaatschappij` IN ({Lijst(bvIds)})", cn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                lijst.Add(new OpenstaandeFactuur(r.GetInt32(0), true, r.GetString(1), r.GetString(2), r.GetInt32(3),
                    r.GetString(4), DateOnly.FromDateTime(r.GetDateTime(5)), Convert.ToDecimal(r.GetValue(6)),
                    Convert.ToInt64(r.GetValue(7)) != 0));
        }

        // Inkoop: onbetaald en nog niet in een SEPA-betaalopdracht (zoals Access).
        await using (var cmd = new MySqlCommand(
            "SELECT f.`FacturenInkomend-ID`, COALESCE(f.`FactuurNummer`, ''), COALESCE(l.`name`, ''), f.`Admin`, " +
            "       COALESCE(bv.`Afkorting`, ''), DATE_ADD(f.`DatumFactuur`, INTERVAL COALESCE(f.`Betalingstermijn_dagen`, 0) DAY), " +
            "       COALESCE(f.`BedragExBTW`, 0) + COALESCE(f.`BTW`, 0) " +
            "  FROM `FacturenInkomend` f " +
            "  LEFT JOIN `Relations` l ON l.`id_supplier` = f.`Bedrijf` " +
            "  LEFT JOIN `Relations` bv ON bv.`id_supplier` = f.`Admin` " +
            " WHERE COALESCE(f.`Betaald`, 0) = 0 AND COALESCE(f.`SEPAverzonden`, 0) = 0 AND f.`DatumFactuur` IS NOT NULL " +
            $"  AND f.`Admin` IN ({Lijst(bvIds)})", cn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                lijst.Add(new OpenstaandeFactuur(r.GetInt32(0), false, r.GetString(1), r.GetString(2), r.GetInt32(3),
                    r.GetString(4), DateOnly.FromDateTime(r.GetDateTime(5)), Convert.ToDecimal(r.GetValue(6)), true));
        }

        return lijst;
    }

    public async Task<Dictionary<(int Jaar, int Kwartaal), decimal>> BtwPerKwartaalAsync(IReadOnlyCollection<int> bvIds, int vanafJaar)
    {
        var btw = new Dictionary<(int, int), decimal>();
        await using var cn = await OpenAsync();

        // Zoals Access' BTWunion: btw op verkoop min btw op inkoop, per kwartaal van de factuurdatum.
        await using var cmd = new MySqlCommand((
            "SELECT jaar, kwartaal, SUM(btw) FROM ( " +
            "  SELECT YEAR(`Date invoice`) jaar, QUARTER(`Date invoice`) kwartaal, COALESCE(`Total btw`, 0) btw " +
            "    FROM `Invoices` WHERE `Werkmaatschappij` IN ({0}) AND YEAR(`Date invoice`) >= @vanaf " +
            "  UNION ALL " +
            "  SELECT YEAR(`DatumFactuur`), QUARTER(`DatumFactuur`), -ROUND(COALESCE(`BTW`, 0), 2) " +
            "    FROM `FacturenInkomend` WHERE `Admin` IN ({0}) AND YEAR(`DatumFactuur`) >= @vanaf " +
            ") x GROUP BY jaar, kwartaal").Replace("{0}", Lijst(bvIds)), cn);
        cmd.Parameters.AddWithValue("@vanaf", vanafJaar);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            btw[(Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)))] = Convert.ToDecimal(r.GetValue(2));
        return btw;
    }
}
