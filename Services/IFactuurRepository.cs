using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

public interface IFactuurRepository
{
    bool Beschikbaar { get; }

    Task<List<FactuurRegel>> ZoekAsync(FactuurFilter filter, int max);
    Task<FactuurTotalen> GetTotalenAsync(FactuurFilter filter);

    /// <summary>De eigen werkmaatschappijen: waarop kan een factuur staan.</summary>
    Task<List<Administratie>> GetAdministratiesAsync();

    Task<Factuur?> GetAsync(int id);

    /// <summary>De gekoppelde inkooporders van een factuur, met het toegewezen bedrag.</summary>
    Task<List<FactuurPo>> GetPosAsync(int id);

    /// <summary>Nieuw of bestaand; geeft het id terug.</summary>
    Task<int> BewaarAsync(Factuur factuur, int? doorWerknemerId);

    Task VerwijderAsync(int id);

    /// <summary>
    /// Zet een van de vlaggen en schrijft het bijbehorende wie-en-wanneer mee.
    /// Uitvinken wist die velden weer.
    /// </summary>
    Task ZetVlagAsync(int id, FactuurVlag vlag, bool aan, int? doorWerknemerId);

    /// <summary>
    /// Staat er al een factuur met dezelfde datum, leverancier en bedrag? Dan is het
    /// waarschijnlijk een dubbele invoer.
    /// </summary>
    Task<bool> BestaatAlAsync(DateOnly? factuurdatum, int? bedrijfId, decimal bedragExBtw, int negeerId);

    /// <summary>IBAN en BIC van de leverancier; nodig voordat een factuur betaald kan worden.</summary>
    Task<(string Iban, string Bic)> GetBankgegevensAsync(int bedrijfId);

    /// <summary>
    /// De leverancier die bij deze naam van een factuur hoort, uit de aliastabel.
    /// Null als de naam onbekend is.
    /// </summary>
    Task<(int Id, string Naam)?> ZoekOpAliasAsync(string naamOpFactuur);

    /// <summary>
    /// De leverancier met dit IBAN. Betrouwbaarder dan een naam: een rekeningnummer
    /// verandert niet als een bedrijf zich anders gaat noemen.
    /// </summary>
    Task<(int Id, string Naam)?> ZoekOpIbanAsync(string iban);

    /// <summary>Legt vast dat deze naam op facturen bij deze leverancier hoort.</summary>
    Task BewaarAliasAsync(string naamOpFactuur, int relatieId, int? doorWerknemerId);

    /// <summary>De namen die aan deze leverancier zijn gekoppeld.</summary>
    Task<List<string>> GetAliassenAsync(int relatieId);

    // ---- PO-koppelingen ----

    /// <summary>De hele inkooporder, alleen-lezen: kop, regels en gekoppelde facturen.</summary>
    Task<PoInfo?> GetPoAsync(int orderId);

    /// <summary>
    /// Inkooporders van deze leverancier om aan te koppelen. Zonder zoektekst
    /// alleen status 3 t/m 6 (besteld of verder, zoals de keuzelijst in Access);
    /// mét zoektekst alles van de leverancier.
    /// </summary>
    Task<List<PoKeuze>> ZoekPosAsync(int leverancierId, string zoek, int max);

    /// <summary>
    /// Werkorders waar ingekocht op mag worden (InkoopToegestaan), niet verwijderd,
    /// desgewenst alleen open. Zoekt in project- en werkbonnummer en omschrijvingen.
    /// </summary>
    Task<List<WerkorderKeuze>> ZoekInkoopWerkordersAsync(string zoek, bool alleenOpen, int max);

    /// <summary>
    /// Koppelt de factuur aan een bestaande PO. Schrijft ook het doorbelastbedrag
    /// (VerkoopBedrag) volgens het percentage van de werkorder, zoals Access deed.
    /// Geeft terug of de PO 'Block payments' aan heeft staan — dan hoort de
    /// betaling op hold.
    /// </summary>
    Task<bool> KoppelAsync(int factuurId, int orderId, decimal bedrag, int? doorWerknemerId);

    /// <summary>
    /// De werkorder-route: maakt in één moeite een PO aan onder de werkorder
    /// (met één orderregel voor het bedrag, meteen op geleverd) en koppelt de
    /// factuur eraan. Geeft het nieuwe order-id terug.
    /// </summary>
    Task<int> MaakPoEnKoppelAsync(int factuurId, string factuurnummer, int werkbonId,
                                  int leverancierId, decimal bedrag, int? doorWerknemerId);

    /// <summary>
    /// Is deze PO destijds automatisch aangemaakt vanuit déze factuur, en hangt er
    /// verder niets aan? Dan mag hij bij het ontkoppelen mee de prullenbak in.
    /// </summary>
    Task<bool> IsEigenAutoPoAsync(int orderId, int factuurId);

    /// <summary>Haalt een koppeling weg; met <paramref name="ookAutoPo"/> gaat ook de
    /// automatisch aangemaakte PO (regels en al) mee weg.</summary>
    Task OntkoppelAsync(int linkId, bool ookAutoPo);

    /// <summary>Past het factuurdeel van een koppeling aan (en rekent het
    /// doorbelastbedrag opnieuw uit).</summary>
    Task ZetKoppelBedragAsync(int linkId, decimal bedrag, int? doorWerknemerId);
}

/// <summary>De vlaggen die elk hun eigen wie-en-wanneer bijhouden.</summary>
public enum FactuurVlag { Goedgekeurd, Betaald, Sepa, Basecone, Hold }

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenFactuurRepository : IFactuurRepository
{
    public bool Beschikbaar => false;
    public Task<List<FactuurRegel>> ZoekAsync(FactuurFilter filter, int max)
        => Task.FromResult(new List<FactuurRegel>());
    public Task<FactuurTotalen> GetTotalenAsync(FactuurFilter filter)
        => Task.FromResult(new FactuurTotalen(0, 0, 0));
    public Task<List<Administratie>> GetAdministratiesAsync() => Task.FromResult(new List<Administratie>());
    public Task<Factuur?> GetAsync(int id) => Task.FromResult<Factuur?>(null);
    public Task<List<FactuurPo>> GetPosAsync(int id) => Task.FromResult(new List<FactuurPo>());
    public Task<int> BewaarAsync(Factuur factuur, int? doorWerknemerId) => Task.FromResult(0);
    public Task VerwijderAsync(int id) => Task.CompletedTask;
    public Task ZetVlagAsync(int id, FactuurVlag vlag, bool aan, int? doorWerknemerId) => Task.CompletedTask;
    public Task<bool> BestaatAlAsync(DateOnly? d, int? b, decimal bedrag, int negeerId) => Task.FromResult(false);
    public Task<(string, string)> GetBankgegevensAsync(int bedrijfId) => Task.FromResult(("", ""));
    public Task<(int Id, string Naam)?> ZoekOpAliasAsync(string n) => Task.FromResult<(int, string)?>(null);
    public Task<(int Id, string Naam)?> ZoekOpIbanAsync(string iban) => Task.FromResult<(int, string)?>(null);
    public Task BewaarAliasAsync(string n, int relatieId, int? door) => Task.CompletedTask;
    public Task<List<string>> GetAliassenAsync(int relatieId) => Task.FromResult(new List<string>());
    public Task<PoInfo?> GetPoAsync(int orderId) => Task.FromResult<PoInfo?>(null);
    public Task<List<PoKeuze>> ZoekPosAsync(int l, string z, int m) => Task.FromResult(new List<PoKeuze>());
    public Task<List<WerkorderKeuze>> ZoekInkoopWerkordersAsync(string z, bool o, int m)
        => Task.FromResult(new List<WerkorderKeuze>());
    public Task<bool> KoppelAsync(int f, int o, decimal b, int? d) => Task.FromResult(false);
    public Task<int> MaakPoEnKoppelAsync(int f, string nr, int w, int l, decimal b, int? d) => Task.FromResult(0);
    public Task<bool> IsEigenAutoPoAsync(int o, int f) => Task.FromResult(false);
    public Task OntkoppelAsync(int linkId, bool ookAutoPo) => Task.CompletedTask;
    public Task ZetKoppelBedragAsync(int linkId, decimal b, int? d) => Task.CompletedTask;
}

/// <summary>
/// Inkomende facturen uit de tabel FacturenInkomend.
///
/// In Access kwam dit scherm uit een SQL Server-view die 'Overdue' en de bedragen
/// voorrekende. Die view bestaat hier niet, dus rekenen we het zelf uit — met
/// dezelfde formule: dagen sinds de factuurdatum min de betalingstermijn, en 0
/// zodra de factuur betaald is.
/// </summary>
public class MySqlFactuurRepository : IFactuurRepository
{
    private readonly string _cs;

    /// <summary>
    /// De kolomnamen van FacturenInkomend zoals ze er werkelijk zijn. Een paar
    /// namen verschillen per database (Basecone met of zonder liggend streepje),
    /// dus die zoeken we één keer op in plaats van te gokken.
    /// </summary>
    private HashSet<string>? _kolommen;

    public MySqlFactuurRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    // ============================================================
    //  Kolomnamen ontdekken
    // ============================================================
    private async Task<HashSet<string>> KolommenAsync(MySqlConnection cn)
    {
        if (_kolommen is not null) return _kolommen;

        var namen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var cmd = new MySqlCommand(
                "SELECT `COLUMN_NAME` FROM `information_schema`.`COLUMNS` " +
                " WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'FacturenInkomend'", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) namen.Add(r.GetString(0));
        }
        catch { }

        // Alleen onthouden als het gelukt is; anders zouden we een lege lijst
        // bewaren en daarna nooit meer een kolomnaam vinden.
        if (namen.Count > 0) _kolommen = namen;
        return namen;
    }

    /// <summary>De eerste naam die echt bestaat, of null.</summary>
    private string? Kol(params string[] kandidaten)
        => _kolommen is null ? kandidaten.FirstOrDefault()
                             : kandidaten.FirstOrDefault(k => _kolommen.Contains(k));

    // Vaste namen; deze staan in elke versie van de tabel gelijk.
    private const string Sleutel = "FacturenInkomend-ID";

    /// <summary>
    /// Dagen te laat. Positief = over de vervaldatum, negatief = nog te gaan,
    /// 0 zodra de factuur betaald is. Zelfde formule als de Access-view.
    /// </summary>
    private const string OverdueUit =
        "CASE WHEN COALESCE(f.`Betaald`, 0) <> 0 THEN 0 " +
        "     ELSE DATEDIFF(CURDATE(), f.`DatumFactuur`) - COALESCE(f.`Betalingstermijn_dagen`, 0) END";

    // ============================================================
    //  Voorwaarden, één plek voor de lijst en de totalen
    // ============================================================
    private static string Voorwaarden(FactuurFilter f)
    {
        var delen = new List<string>();

        var datum = f.Datumsoort == FactuurDatumsoort.Factuur ? "f.`DatumFactuur`" : "f.`DatumInvoer`";
        if (f.Van is not null) delen.Add($"{datum} >= @van");
        if (f.Tot is not null) delen.Add($"{datum} < @tot");

        // In Access heette dit 'Vervallen'. Hier telt de opgegeven dag mee: 0 is
        // 'vervalt vandaag of eerder', -7 is 'binnen een week'.
        if (f.VanafOverdue is not null) delen.Add($"({OverdueUit}) >= @overdue");

        if (f.Bedrijf.Trim().Length > 0) delen.Add("r.`name` LIKE @bedrijf");
        if (f.Workorder.Trim().Length > 0) delen.Add("f.`SummaryWorkorders` LIKE @workorder");
        if (f.Administratie.Trim().Length > 0) delen.Add("a.`Afkorting` LIKE @admin");

        // Zoeken doet Access over één samengeraapte tekst; dat houden we aan, zodat
        // je net als vroeger op een bedrag of een PO-nummer kunt zoeken. Bedragen
        // staan er tweemaal in: met een punt en met een komma, zodat zowel 1234.50
        // als 1234,50 gevonden wordt. Geen FORMAT() — die zet er duizendscheidings-
        // tekens in en dan vind je juist niets.
        if (f.Zoek.Trim().Length > 0)
            delen.Add("CONCAT_WS(' ', COALESCE(f.`Omschrijving`,''), COALESCE(f.`FactuurNummer`,''), " +
                      "COALESCE(r.`name`,''), COALESCE(f.`SummaryWorkorders`,''), " +
                      "CAST(ROUND(COALESCE(f.`BedragExBTW`,0), 2) AS CHAR), " +
                      "REPLACE(CAST(ROUND(COALESCE(f.`BedragExBTW`,0), 2) AS CHAR), '.', ','), " +
                      "CAST(ROUND(COALESCE(f.`BedragExBTW`,0) + COALESCE(f.`BTW`,0), 2) AS CHAR), " +
                      "REPLACE(CAST(ROUND(COALESCE(f.`BedragExBTW`,0) + COALESCE(f.`BTW`,0), 2) AS CHAR), " +
                      "        '.', ',')) LIKE @zoek");

        Voeg(delen, f.Goedgekeurd, "f.`Goedgekeurd`");
        Voeg(delen, f.Sepa, "f.`SEPAverzonden`");
        Voeg(delen, f.Betaald, "f.`Betaald`");

        return delen.Count == 0 ? "" : " WHERE " + string.Join(" AND ", delen);
    }

    private static void Voeg(List<string> delen, Drieluik keuze, string kolom)
    {
        if (keuze == Drieluik.Ja) delen.Add($"COALESCE({kolom}, 0) <> 0");
        if (keuze == Drieluik.Nee) delen.Add($"COALESCE({kolom}, 0) = 0");
    }

    private static void Vul(MySqlCommand cmd, FactuurFilter f)
    {
        if (f.Van is not null)
            cmd.Parameters.AddWithValue("@van", f.Van.Value.ToDateTime(TimeOnly.MinValue));
        if (f.Tot is not null)
            cmd.Parameters.AddWithValue("@tot", f.Tot.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (f.VanafOverdue is not null)
            cmd.Parameters.AddWithValue("@overdue", f.VanafOverdue.Value);

        if (f.Bedrijf.Trim().Length > 0) cmd.Parameters.AddWithValue("@bedrijf", "%" + f.Bedrijf.Trim() + "%");
        if (f.Workorder.Trim().Length > 0) cmd.Parameters.AddWithValue("@workorder", "%" + f.Workorder.Trim() + "%");
        if (f.Administratie.Trim().Length > 0) cmd.Parameters.AddWithValue("@admin", f.Administratie.Trim() + "%");
        if (f.Zoek.Trim().Length > 0) cmd.Parameters.AddWithValue("@zoek", "%" + f.Zoek.Trim() + "%");
    }

    /// <summary>De twee koppelingen naar Relations: de leverancier en de eigen BV.</summary>
    private const string Van =
        "  FROM `FacturenInkomend` f " +
        "  LEFT JOIN `Relations` r ON r.`id_supplier` = f.`Bedrijf` " +
        "  LEFT JOIN `Relations` a ON a.`id_supplier` = f.`Admin` ";

    // ============================================================
    //  De lijst
    // ============================================================
    public async Task<List<FactuurRegel>> ZoekAsync(FactuurFilter filter, int max)
    {
        var lijst = new List<FactuurRegel>();

        await using var cn = await OpenAsync();
        await KolommenAsync(cn);

        var datum = filter.Datumsoort == FactuurDatumsoort.Factuur ? "f.`DatumFactuur`" : "f.`DatumInvoer`";

        await using var cmd = new MySqlCommand(
            $"SELECT f.`{Sleutel}`, f.`DatumInvoer`, f.`DatumFactuur`, " +
            "       COALESCE(f.`Betalingstermijn_dagen`, 0), " +
            "       f.`Bedrijf`, COALESCE(r.`name`, ''), " +
            "       COALESCE(f.`FactuurNummer`, ''), COALESCE(f.`Omschrijving`, ''), " +
            "       COALESCE(f.`BedragExBTW`, 0), COALESCE(f.`BTW`, 0), " +
            "       f.`Admin`, COALESCE(a.`Afkorting`, ''), " +
            "       COALESCE(f.`SummaryWorkorders`, ''), " +
            "       COALESCE(f.`Goedgekeurd`, 0), COALESCE(f.`Betaald`, 0), " +
            "       COALESCE(f.`SEPAverzonden`, 0), COALESCE(f.`VerzondenBasecone`, 0), " +
            "       COALESCE(f.`BetalingOpHold`, 0), " +
            $"       (SELECT COUNT(*) FROM `Facturen_Orders_Link` l WHERE l.`FactuurID` = f.`{Sleutel}`), " +
            $"       ({OverdueUit}) AS Overdue " +
            Van +
            Voorwaarden(filter) +
            $" ORDER BY {datum} DESC, f.`{Sleutel}` DESC " +
            $" LIMIT {Math.Clamp(max, 1, 5000)}", cn);
        Vul(cmd, filter);

        await using var r2 = await cmd.ExecuteReaderAsync();
        while (await r2.ReadAsync())
        {
            string S(int i) => r2.IsDBNull(i) ? "" : r2.GetValue(i).ToString() ?? "";
            int? I(int i) => r2.IsDBNull(i) ? null : Convert.ToInt32(r2.GetValue(i));
            decimal M(int i) => r2.IsDBNull(i) ? 0 : Convert.ToDecimal(r2.GetValue(i));
            bool B(int i) => !r2.IsDBNull(i) && Convert.ToInt64(r2.GetValue(i)) != 0;
            DateTime? D(int i) => r2.IsDBNull(i) ? null : Convert.ToDateTime(r2.GetValue(i));

            lijst.Add(new FactuurRegel
            {
                Id = r2.GetInt32(0),
                Invoerdatum = D(1),
                Factuurdatum = D(2),
                Betalingstermijn = I(3) ?? 0,
                BedrijfId = I(4),
                Bedrijf = S(5),
                Factuurnummer = S(6),
                Omschrijving = S(7),
                BedragExBtw = M(8),
                Btw = M(9),
                AdministratieId = I(10),
                Administratie = S(11),
                PoSamenvatting = S(12),
                Goedgekeurd = B(13),
                Betaald = B(14),
                SepaVerzonden = B(15),
                NaarBasecone = B(16),
                OpHold = B(17),
                PoKoppelingen = I(18) ?? 0,
                Overdue = I(19) ?? 0,
            });
        }
        return lijst;
    }

    public async Task<FactuurTotalen> GetTotalenAsync(FactuurFilter filter)
    {
        await using var cn = await OpenAsync();
        await KolommenAsync(cn);

        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(SUM(COALESCE(f.`BedragExBTW`, 0)), 0), " +
            "       COALESCE(SUM(COALESCE(f.`BTW`, 0)), 0), COUNT(*) " +
            Van + Voorwaarden(filter), cn);
        Vul(cmd, filter);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return new FactuurTotalen(0, 0, 0);

        return new FactuurTotalen(
            r.IsDBNull(0) ? 0 : Convert.ToDecimal(r.GetValue(0)),
            r.IsDBNull(1) ? 0 : Convert.ToDecimal(r.GetValue(1)),
            r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2)));
    }

    // ============================================================
    //  Administraties (de eigen BV's)
    // ============================================================
    public async Task<List<Administratie>> GetAdministratiesAsync()
    {
        var lijst = new List<Administratie>();
        await using var cn = await OpenAsync();

        // De eigen werkmaatschappijen staan in Relations, met een vlag erbij.
        foreach (var vlag in new[] { "Werkmaatschappij", "Werkmaatschappy", "OwnCompany" })
        {
            try
            {
                await using var cmd = new MySqlCommand(
                    $"SELECT `id_supplier`, COALESCE(`Afkorting`, ''), COALESCE(`name`, '') " +
                    $"  FROM `Relations` WHERE COALESCE(`{vlag}`, 0) <> 0 " +
                    "  ORDER BY `Afkorting`", cn);

                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    lijst.Add(new Administratie(r.GetInt32(0), r.GetString(1), r.GetString(2)));

                if (lijst.Count > 0) return lijst;
            }
            catch { /* kolom bestaat niet; volgende naam proberen */ }
        }
        return lijst;
    }

    // ============================================================
    //  Eén factuur
    // ============================================================
    public async Task<Factuur?> GetAsync(int id)
    {
        await using var cn = await OpenAsync();
        await KolommenAsync(cn);

        await using var cmd = new MySqlCommand(
            "SELECT f.*, COALESCE(r.`name`, '') AS Leverancier, " +
            "       COALESCE(inv.`NaamVoluit`, '') AS InvoerNaam, " +
            "       COALESCE(bet.`NaamVoluit`, '') AS BetaaldNaam, " +
            "       COALESCE(goe.`NaamVoluit`, '') AS GoedNaam, " +
            "       COALESCE(sep.`NaamVoluit`, '') AS SepaNaam " +
            "  FROM `FacturenInkomend` f " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = f.`Bedrijf` " +
            "  LEFT JOIN `Werknemers` inv ON inv.`Werknemer-id` = f.`IngevoerdDoor` " +
            "  LEFT JOIN `Werknemers` bet ON bet.`Werknemer-id` = f.`Betaald_door` " +
            "  LEFT JOIN `Werknemers` goe ON goe.`Werknemer-id` = f.`GoedgekeurdDoor` " +
            "  LEFT JOIN `Werknemers` sep ON sep.`Werknemer-id` = f.`SEPADoor` " +
            $" WHERE f.`{Sleutel}` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        // Op naam lezen; de volgorde van de kolommen doet dan niet mee, en een
        // kolom die in deze database anders heet levert gewoon niets op.
        string S(string k) { try { var v = r[k]; return v is DBNull ? "" : v.ToString() ?? ""; } catch { return ""; } }
        int? I(string k) { try { var v = r[k]; return v is DBNull ? null : Convert.ToInt32(v); } catch { return null; } }
        decimal M(string k) { try { var v = r[k]; return v is DBNull ? 0 : Convert.ToDecimal(v); } catch { return 0; } }
        double Db(string k) { try { var v = r[k]; return v is DBNull ? 0 : Convert.ToDouble(v); } catch { return 0; } }
        bool B(string k) { try { var v = r[k]; return v is not DBNull && Convert.ToInt64(v) != 0; } catch { return false; } }
        DateTime? D(string k) { try { var v = r[k]; return v is DBNull ? null : Convert.ToDateTime(v); } catch { return null; } }

        var factuurdatum = D("DatumFactuur");
        var betaaldOp = D("DatumBetaald");

        return new Factuur
        {
            Id = id,
            AdministratieId = I("Admin"),
            BedrijfId = I("Bedrijf"),
            Bedrijf = S("Leverancier"),

            Factuurdatum = factuurdatum is null ? null : DateOnly.FromDateTime(factuurdatum.Value),
            Invoerdatum = D("DatumInvoer"),
            IngevoerdDoor = S("InvoerNaam"),

            Factuurnummer = S("FactuurNummer"),
            Omschrijving = S("Omschrijving"),
            ReferentieMde = S("Referentie_MDE"),
            Betalingstermijn = I("Betalingstermijn_dagen") ?? 30,

            BedragExBtw = M("BedragExBTW"),
            Btw = M("BTW"),
            Doorbelasten = Db("PercentageDoorbelasten"),

            OpHold = B("BetalingOpHold"),

            Betaald = B("Betaald"),
            BetaaldOp = betaaldOp is null ? null : DateOnly.FromDateTime(betaaldOp.Value),
            BetaaldDoor = S("BetaaldNaam"),

            Goedgekeurd = B("Goedgekeurd"),
            GoedgekeurdOp = D("GoedgekeurdOp"),
            GoedgekeurdDoor = S("GoedNaam"),

            SepaVerzonden = B("SEPAverzonden"),
            SepaOp = D("SEPAop"),
            SepaDoor = S("SepaNaam"),

            NaarBasecone = B("VerzondenBasecone"),
            BaseconeOp = D(Kol("VerzondenBaseconeOp", "VerzondenBasecone_Op") ?? "VerzondenBaseconeOp"),

            Opmerking = S("Opmerking"),
            PoSamenvatting = S("SummaryWorkorders"),
        };
    }

    // ============================================================
    //  Gekoppelde inkooporders
    // ============================================================
    public async Task<List<FactuurPo>> GetPosAsync(int id)
    {
        var lijst = new List<FactuurPo>();
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT l.`LinkID`, l.`OrderID`, COALESCE(l.`Bedrag`, 0), " +
                "       COALESCE(p.`Projectdescription`, ''), COALESCE(w.`Werkbonomschrijving`, ''), " +
                "       COALESCE(m.`NaamVoluit`, ''), " +
                // Het PO-nummer zoals Access hem overal opbouwt:
                // projectnummer-werkbon(2 cijfers)-extensie(3 cijfers).
                "       CASE WHEN o.`Order-id` IS NULL THEN '' ELSE CONCAT( " +
                "            COALESCE(p.`Projectnumber`, '?'), '-', " +
                "            RIGHT(CONCAT('00', COALESCE(w.`Werkbonnummer`, 0)), 2), '-', " +
                "            RIGHT(CONCAT('000', COALESCE(o.`Extension`, 0)), 3)) END, " +
                "       COALESCE((SELECT SUM(oi.`Totalprice`) FROM `Orderitems` oi " +
                "                  WHERE oi.`Order-id` = l.`OrderID`), 0) " +
                "  FROM `Facturen_Orders_Link` l " +
                "  LEFT JOIN `Orders` o ON o.`Order-id` = l.`OrderID` " +
                "  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = o.`Workorder-id` " +
                "  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
                "  LEFT JOIN `Werknemers` m ON m.`Werknemer-id` = o.`Created by` " +
                " WHERE l.`FactuurID` = @id " +
                " ORDER BY l.`LinkID`", cn);
            cmd.Parameters.AddWithValue("@id", id);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                lijst.Add(new FactuurPo
                {
                    LinkId = r.GetInt32(0),
                    OrderId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    Bedrag = r.IsDBNull(2) ? 0 : Convert.ToDecimal(r.GetValue(2)),
                    Project = r.IsDBNull(3) ? "" : r.GetString(3),
                    Werkorder = r.IsDBNull(4) ? "" : r.GetString(4),
                    Door = r.IsDBNull(5) ? "" : r.GetString(5),
                    NummerTekst = r.IsDBNull(6) ? "" : r.GetString(6),
                    PoBedrag = r.IsDBNull(7) ? 0 : Convert.ToDecimal(r.GetValue(7)),
                });
        }
        catch
        {
            // De namen van de project- en werkbontabellen verschillen per database;
            // lukt de nette versie niet, dan tonen we in elk geval de orders zelf.
            try
            {
                await using var cn = await OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT `OrderID`, COALESCE(`Bedrag`, 0), `LinkID` FROM `Facturen_Orders_Link` " +
                    " WHERE `FactuurID` = @id ORDER BY `LinkID`", cn);
                cmd.Parameters.AddWithValue("@id", id);

                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    lijst.Add(new FactuurPo
                    {
                        OrderId = r.GetInt32(0),
                        Bedrag = r.IsDBNull(1) ? 0 : Convert.ToDecimal(r.GetValue(1)),
                        // Ook hier het LinkID: zonder dat zouden ontkoppelen en
                        // bedrag wijzigen stilletjes niets doen.
                        LinkId = r.GetInt32(2),
                    });
            }
            catch { }
        }
        return lijst;
    }

    // ============================================================
    //  Opslaan
    // ============================================================
    public async Task<int> BewaarAsync(Factuur factuur, int? doorWerknemerId)
    {
        await using var cn = await OpenAsync();
        await KolommenAsync(cn);

        var velden =
            "`Admin` = @admin, `Bedrijf` = @bedrijf, `DatumFactuur` = @datum, " +
            "`FactuurNummer` = @nummer, `Omschrijving` = @omschrijving, " +
            "`Referentie_MDE` = @referentie, `Betalingstermijn_dagen` = @termijn, " +
            "`BedragExBTW` = @ex, `BTW` = @btw, `PercentageDoorbelasten` = @doorbelast, " +
            "`BetalingOpHold` = @hold";

        var nieuw = factuur.Id == 0;

        // Access zette bij elke nieuwe factuur een GUID; sommige rapporten zoeken
        // daarop. Bestaat de kolom niet, dan slaan we hem over.
        var guid = nieuw && Kol("GUID") is not null ? ", `GUID` = @guid" : "";

        var sql = nieuw
            ? $"INSERT INTO `FacturenInkomend` SET {velden}, " +
              $"`DatumInvoer` = @nu, `IngevoerdDoor` = @door, `Opmerking` = ''{guid}"
            : $"UPDATE `FacturenInkomend` SET {velden} WHERE `{Sleutel}` = @id";

        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@admin", (object?)factuur.AdministratieId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bedrijf", (object?)factuur.BedrijfId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@datum", factuur.Factuurdatum is null
            ? DBNull.Value : factuur.Factuurdatum.Value.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@nummer", Kort(factuur.Factuurnummer, 45));
        cmd.Parameters.AddWithValue("@omschrijving", Kort(factuur.Omschrijving, 100));
        cmd.Parameters.AddWithValue("@referentie", Kort(factuur.ReferentieMde, 45));
        cmd.Parameters.AddWithValue("@termijn", factuur.Betalingstermijn);
        cmd.Parameters.AddWithValue("@ex", factuur.BedragExBtw);
        cmd.Parameters.AddWithValue("@btw", factuur.Btw);
        cmd.Parameters.AddWithValue("@doorbelast", factuur.Doorbelasten);
        cmd.Parameters.AddWithValue("@hold", factuur.OpHold ? 1 : 0);

        if (nieuw)
        {
            cmd.Parameters.AddWithValue("@nu", DateTime.Now);
            cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
            if (guid.Length > 0)
                cmd.Parameters.AddWithValue("@guid", Guid.NewGuid().ToString("N").ToUpperInvariant());
        }
        else cmd.Parameters.AddWithValue("@id", factuur.Id);

        await cmd.ExecuteNonQueryAsync();
        return nieuw ? (int)cmd.LastInsertedId : factuur.Id;
    }

    private static string Kort(string tekst, int lengte)
        => tekst.Length <= lengte ? tekst : tekst[..lengte];

    public async Task VerwijderAsync(int id)
    {
        await using var cn = await OpenAsync();

        // Eerst de koppelingen los, dan de factuur; anders blijft er rommel staan.
        try
        {
            await using var los = new MySqlCommand(
                "DELETE FROM `Facturen_Orders_Link` WHERE `FactuurID` = @id", cn);
            los.Parameters.AddWithValue("@id", id);
            await los.ExecuteNonQueryAsync();
        }
        catch { }

        await using var cmd = new MySqlCommand(
            $"DELETE FROM `FacturenInkomend` WHERE `{Sleutel}` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Vlaggen met wie-en-wanneer
    // ============================================================
    public async Task ZetVlagAsync(int id, FactuurVlag vlag, bool aan, int? doorWerknemerId)
    {
        await using var cn = await OpenAsync();
        await KolommenAsync(cn);

        // Per vlag: de vlag zelf, wie het deed en wanneer. Uitvinken wist die twee
        // weer — anders blijft er een naam staan bij iets wat niet meer waar is.
        var (kolom, wie, wanneer, wanneerIsDatum) = vlag switch
        {
            FactuurVlag.Goedgekeurd => ("Goedgekeurd", "GoedgekeurdDoor", "GoedgekeurdOp", false),
            FactuurVlag.Betaald => ("Betaald", "Betaald_door", "DatumBetaald", true),
            FactuurVlag.Sepa => ("SEPAverzonden", "SEPADoor", "SEPAop", false),
            FactuurVlag.Basecone => ("VerzondenBasecone",
                                      Kol("VerzondenBaseconeDoor", "VerzondenBasecone_Door") ?? "",
                                      Kol("VerzondenBaseconeOp", "VerzondenBasecone_Op") ?? "", false),
            _ => ("BetalingOpHold", "", "", false),      // hold houdt geen wie/wanneer bij
        };

        var zet = new List<string> { $"`{kolom}` = @aan" };
        if (wie.Length > 0) zet.Add($"`{wie}` = @wie");
        if (wanneer.Length > 0) zet.Add($"`{wanneer}` = @wanneer");

        await using var cmd = new MySqlCommand(
            $"UPDATE `FacturenInkomend` SET {string.Join(", ", zet)} WHERE `{Sleutel}` = @id", cn);

        cmd.Parameters.AddWithValue("@aan", aan ? 1 : 0);
        if (wie.Length > 0)
            cmd.Parameters.AddWithValue("@wie", aan ? (object?)doorWerknemerId ?? DBNull.Value : DBNull.Value);
        if (wanneer.Length > 0)
            cmd.Parameters.AddWithValue("@wanneer", aan
                ? (wanneerIsDatum ? DateTime.Today : DateTime.Now)
                : DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Controles bij het invoeren
    // ============================================================
    public async Task<bool> BestaatAlAsync(DateOnly? factuurdatum, int? bedrijfId, decimal bedragExBtw,
                                           int negeerId)
    {
        if (factuurdatum is null || bedrijfId is null) return false;

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM `FacturenInkomend` " +
            " WHERE `DatumFactuur` = @datum AND `Bedrijf` = @bedrijf " +
            "   AND ABS(COALESCE(`BedragExBTW`, 0) - @ex) < 0.01 " +
            $"  AND `{Sleutel}` <> @negeer", cn);
        cmd.Parameters.AddWithValue("@datum", factuurdatum.Value.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@bedrijf", bedrijfId.Value);
        cmd.Parameters.AddWithValue("@ex", bedragExBtw);
        cmd.Parameters.AddWithValue("@negeer", negeerId);

        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    // ============================================================
    //  Leveranciers herkennen
    //
    //  Op een factuur staat niet altijd de naam waaronder wij een leverancier
    //  kennen. Twee manieren om er toch bij te komen: een naam die eerder is
    //  gekoppeld (de aliastabel), of het rekeningnummer — dat verandert niet als
    //  een bedrijf zich anders gaat noemen.
    // ============================================================

    /// <summary>
    /// Een naam opschonen zodat kleine verschillen niet meer meedoen: kleine
    /// letters, geen punten of spaties, en de rechtsvorm eraf. Zo is
    /// "GeT Components Group B.V." hetzelfde als "get components group bv".
    /// </summary>
    public static string AliasSleutel(string naam)
    {
        var schoon = new string((naam ?? "").ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());

        foreach (var vorm in new[] { " bv", " b v", " nv", " n v", " gmbh", " ltd", " limited",
                                     " sarl", " sa", " ag", " inc", " llc", " vof", " cv" })
            if (schoon.EndsWith(vorm)) { schoon = schoon[..^vorm.Length]; break; }

        return schoon.Replace(" ", "").Trim();
    }

    public async Task<(int Id, string Naam)?> ZoekOpAliasAsync(string naamOpFactuur)
    {
        var sleutel = AliasSleutel(naamOpFactuur);
        if (sleutel.Length < 3) return null;

        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT a.`RelatieID`, COALESCE(r.`name`, '') " +
                "  FROM `LeverancierAlias` a " +
                "  LEFT JOIN `Relations` r ON r.`id_supplier` = a.`RelatieID` " +
                " WHERE a.`Sleutel` = @s LIMIT 1", cn);
            cmd.Parameters.AddWithValue("@s", sleutel);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            var id = r.GetInt32(0);
            var naam = r.IsDBNull(1) ? "" : r.GetString(1);
            await r.CloseAsync();

            // Bijhouden hoe vaak een alias van pas komt; dan zie je later welke
            // er echt gebruikt worden.
            try
            {
                await using var tel = new MySqlCommand(
                    "UPDATE `LeverancierAlias` SET `Gebruikt` = `Gebruikt` + 1 " +
                    " WHERE `Sleutel` = @s", cn);
                tel.Parameters.AddWithValue("@s", sleutel);
                await tel.ExecuteNonQueryAsync();
            }
            catch { }

            return (id, naam);
        }
        catch
        {
            // Tabel bestaat nog niet: dan werkt de app gewoon zonder aliassen.
            return null;
        }
    }

    public async Task<(int Id, string Naam)?> ZoekOpIbanAsync(string iban)
    {
        var schoon = (iban ?? "").Replace(" ", "").ToUpperInvariant();
        if (schoon.Length < 15) return null;

        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT `id_supplier`, COALESCE(`name`, '') FROM `Relations` " +
                " WHERE REPLACE(UPPER(COALESCE(`IBAN`, '')), ' ', '') = @iban LIMIT 2", cn);
            cmd.Parameters.AddWithValue("@iban", schoon);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            var uit = (r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1));

            // Staat hetzelfde rekeningnummer bij twee relaties, dan is het geen
            // bewijs meer en laten we de keuze aan de gebruiker.
            if (await r.ReadAsync()) return null;

            return uit;
        }
        catch { return null; }
    }

    public async Task BewaarAliasAsync(string naamOpFactuur, int relatieId, int? doorWerknemerId)
    {
        var sleutel = AliasSleutel(naamOpFactuur);
        if (sleutel.Length < 3 || relatieId <= 0) return;

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `LeverancierAlias` " +
            " (`Naam`, `Sleutel`, `RelatieID`, `ToegevoegdOp`, `ToegevoegdDoor`) " +
            "VALUES (@naam, @sleutel, @relatie, @nu, @door) " +
            // Bestond de naam al maar wees hij naar iemand anders, dan geldt de
            // nieuwe keuze; de gebruiker weet het beter dan de oude regel.
            "ON DUPLICATE KEY UPDATE `RelatieID` = @relatie, `Naam` = @naam, " +
            "                        `ToegevoegdOp` = @nu, `ToegevoegdDoor` = @door", cn);

        cmd.Parameters.AddWithValue("@naam", naamOpFactuur.Length > 200
            ? naamOpFactuur[..200] : naamOpFactuur);
        cmd.Parameters.AddWithValue("@sleutel", sleutel.Length > 200 ? sleutel[..200] : sleutel);
        cmd.Parameters.AddWithValue("@relatie", relatieId);
        cmd.Parameters.AddWithValue("@nu", DateTime.Now);
        cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetAliassenAsync(int relatieId)
    {
        var lijst = new List<string>();
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT `Naam` FROM `LeverancierAlias` WHERE `RelatieID` = @id ORDER BY `Naam`", cn);
            cmd.Parameters.AddWithValue("@id", relatieId);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) lijst.Add(r.GetString(0));
        }
        catch { }
        return lijst;
    }

    public async Task<(string Iban, string Bic)> GetBankgegevensAsync(int bedrijfId)
    {
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COALESCE(`IBAN`, ''), COALESCE(`BIC`, '') " +
                "  FROM `Relations` WHERE `id_supplier` = @id", cn);
            cmd.Parameters.AddWithValue("@id", bedrijfId);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) return (r.GetString(0).Trim(), r.GetString(1).Trim());
        }
        catch { }
        return ("", "");
    }

    // ============================================================
    //  PO-koppelingen
    //
    //  Nagebouwd uit de Access-formulieren FacturenOrdersLink_add/_edit en het
    //  cmdOK van 'FacturenInkomend toevoegen'. Twee bewuste afwijkingen van
    //  Access: de orderregel krijgt het gekoppelde bedrag in plaats van altijd
    //  het hele factuurbedrag (dat was daar een fout), en de omschrijving van
    //  de regel noemt het factuurnummer, zodat iedereen ziet waar hij vandaan
    //  komt.
    // ============================================================

    /// <summary>Het PO-nummer zoals Access hem overal opbouwt; verwacht de
    /// aliassen o (Orders), w (Workorders) en p (Projects).</summary>
    private const string PoNummerSql =
        "CASE WHEN o.`Order-id` IS NULL THEN '' ELSE CONCAT(" +
        "COALESCE(p.`Projectnumber`, '?'), '-', " +
        "RIGHT(CONCAT('00', COALESCE(w.`Werkbonnummer`, 0)), 2), '-', " +
        "RIGHT(CONCAT('000', COALESCE(o.`Extension`, 0)), 3)) END";

    /// <summary>De statusteksten van orders, één keer opgehaald. De tabelnaam en
    /// kolommen verschillen nogal eens; lukt het niet, dan tonen we het nummer.</summary>
    private Dictionary<int, string>? _orderStatussen;

    private async Task<Dictionary<int, string>> StatusTekstenAsync(MySqlConnection cn)
    {
        if (_orderStatussen is not null) return _orderStatussen;
        var map = new Dictionary<int, string>();
        foreach (var tabel in new[] { "OrderStatus", "Orderstatussen", "Order_Status" })
        {
            try
            {
                await using var cmd = new MySqlCommand($"SELECT * FROM `{tabel}` LIMIT 50", cn);
                await using var r = await cmd.ExecuteReaderAsync();

                // De eerste getalkolom is het nummer, de eerste tekstkolom de naam.
                int kolNr = -1, kolTekst = -1;
                for (var i = 0; i < r.FieldCount; i++)
                {
                    var t = r.GetFieldType(i);
                    if (kolNr < 0 && (t == typeof(int) || t == typeof(short) || t == typeof(long))) kolNr = i;
                    if (kolTekst < 0 && t == typeof(string)) kolTekst = i;
                }
                if (kolNr < 0 || kolTekst < 0) continue;

                while (await r.ReadAsync())
                    if (!r.IsDBNull(kolNr) && !r.IsDBNull(kolTekst))
                        map[Convert.ToInt32(r.GetValue(kolNr))] = r.GetValue(kolTekst)?.ToString() ?? "";
                if (map.Count > 0) break;
            }
            catch { /* volgende naam proberen */ }
        }
        return _orderStatussen = map;
    }

    /// <summary>
    /// Houdt FacturenInkomend.SummaryWorkorders gelijk aan de koppelingen, zodat
    /// de Access-schermen (die deze tekst tonen) blijven kloppen zolang beide
    /// programma's naast elkaar draaien.
    /// </summary>
    private async Task WerkSummaryBijAsync(MySqlConnection cn, int factuurId)
    {
        try
        {
            var kolommen = await KolommenAsync(cn);
            if (!kolommen.Contains("SummaryWorkorders")) return;

            var nummers = new List<string>();
            await using (var cmd = new MySqlCommand(
                $"SELECT {PoNummerSql} " +
                "  FROM `Facturen_Orders_Link` l " +
                "  LEFT JOIN `Orders` o ON o.`Order-id` = l.`OrderID` " +
                "  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = o.`Workorder-id` " +
                "  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
                " WHERE l.`FactuurID` = @id ORDER BY l.`LinkID`", cn))
            {
                cmd.Parameters.AddWithValue("@id", factuurId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    if (!r.IsDBNull(0) && r.GetString(0).Length > 0) nummers.Add(r.GetString(0));
            }

            // Zelfde teksten als Access' UpdateSummary, inclusief de lege variant.
            var samenvatting = nummers.Count > 0 ? string.Join(" + ", nummers) : "#Geen koppeling#";

            await using var upd = new MySqlCommand(
                $"UPDATE `FacturenInkomend` SET `SummaryWorkorders` = @s WHERE `{Sleutel}` = @id", cn);
            upd.Parameters.AddWithValue("@s", samenvatting);
            upd.Parameters.AddWithValue("@id", factuurId);
            await upd.ExecuteNonQueryAsync();
        }
        catch { /* de samenvatting is hulptekst; een koppeling mag hier niet op stranden */ }
    }

    /// <summary>Doorbelastpercentage van de werkorder achter een order, plus of
    /// betalingen op die order geblokkeerd zijn.</summary>
    private static async Task<(decimal DoorbelastDeel, bool Block)> OrderGegevensAsync(
        MySqlConnection cn, int orderId)
    {
        await using var cmd = new MySqlCommand(
            // Geen percentage op de werkorder? Dan 100 (één-op-één doorbelasten).
            // Access schreef in dat geval stilletjes niets weg; dit is eerlijker.
            "SELECT COALESCE(w.`Doorbelast_deel_materials`, 100), COALESCE(o.`Block payments`, 0) " +
            "  FROM `Orders` o " +
            "  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = o.`Workorder-id` " +
            " WHERE o.`Order-id` = @id", cn);
        cmd.Parameters.AddWithValue("@id", orderId);

        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
            return (Convert.ToDecimal(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)) != 0);
        return (100m, false);
    }

    public async Task<PoInfo?> GetPoAsync(int orderId)
    {
        await using var cn = await OpenAsync();

        PoInfo? po = null;
        await using (var cmd = new MySqlCommand(
            $"SELECT o.`Order-id`, {PoNummerSql}, COALESCE(o.`Description`, ''), " +
            "        COALESCE(r.`name`, ''), COALESCE(p.`Projectdescription`, ''), " +
            "        COALESCE(w.`Werkbonomschrijving`, ''), COALESCE(o.`Status`, 0), " +
            "        o.`Creation date`, COALESCE(m.`NaamVoluit`, ''), " +
            "        COALESCE(o.`Block payments`, 0) " +
            "   FROM `Orders` o " +
            "   LEFT JOIN `Relations` r ON r.`id_supplier` = o.`Supplier` " +
            "   LEFT JOIN `Workorders` w ON w.`Werkbon-id` = o.`Workorder-id` " +
            "   LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            "   LEFT JOIN `Werknemers` m ON m.`Werknemer-id` = o.`Created by` " +
            "  WHERE o.`Order-id` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", orderId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            po = new PoInfo
            {
                OrderId = r.GetInt32(0),
                Nummer = r.IsDBNull(1) ? "" : r.GetString(1),
                Omschrijving = r.GetString(2),
                Leverancier = r.GetString(3),
                Project = r.GetString(4),
                Werkorder = r.GetString(5),
                Status = Convert.ToInt32(r.GetValue(6)),
                Aangemaakt = r.IsDBNull(7) ? null : r.GetDateTime(7),
                AangemaaktDoor = r.GetString(8),
                BetalingenGeblokkeerd = Convert.ToInt32(r.GetValue(9)) != 0,
            };
        }

        var statussen = await StatusTekstenAsync(cn);
        po.StatusTekst = statussen.TryGetValue(po.Status, out var st) ? st : $"status {po.Status}";

        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(oi.`Quantity ordered`, 0), COALESCE(v.`Units`, ''), " +
            "       COALESCE(oi.`Description`, ''), COALESCE(oi.`Partnumber`, ''), " +
            "       COALESCE(oi.`Grossprice per VE`, 0), COALESCE(oi.`Discount`, 0), " +
            "       COALESCE(oi.`Totalprice`, 0), COALESCE(oi.`Needed-by`, '') " +
            "  FROM `Orderitems` oi " +
            "  LEFT JOIN `VE` v ON v.`VE-ID` = oi.`VE-id` " +
            " WHERE oi.`Order-id` = @id ORDER BY oi.`Orderitem-id`", cn))
        {
            cmd.Parameters.AddWithValue("@id", orderId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                po.Regels.Add(new PoRegel
                {
                    Aantal = Convert.ToDecimal(r.GetValue(0)),
                    Eenheid = r.GetString(1),
                    Omschrijving = r.GetString(2),
                    Artikelcode = r.GetString(3),
                    PrijsPerEenheid = Convert.ToDecimal(r.GetValue(4)),
                    Korting = Convert.ToDecimal(r.GetValue(5)),
                    Totaal = Convert.ToDecimal(r.GetValue(6)),
                    Voor = r.GetString(7),
                });
        }

        await using (var cmd = new MySqlCommand(
            $"SELECT f.`{Sleutel}`, COALESCE(f.`FactuurNummer`, ''), " +
            "        COALESCE(l.`Bedrag`, 0), COALESCE(f.`Betaald`, 0) " +
            "   FROM `Facturen_Orders_Link` l " +
            "   JOIN `FacturenInkomend` f ON f.`" + Sleutel + "` = l.`FactuurID` " +
            "  WHERE l.`OrderID` = @id ORDER BY l.`LinkID`", cn))
        {
            cmd.Parameters.AddWithValue("@id", orderId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                po.Facturen.Add(new PoFactuur(
                    r.GetInt32(0), r.GetString(1),
                    Convert.ToDecimal(r.GetValue(2)),
                    Convert.ToInt32(r.GetValue(3)) != 0));
        }

        return po;
    }

    /// <summary>'PO12345', 'PO-12345', 'po 12345' en '#12345' betekenen allemaal
    /// hetzelfde als '12345' — mensen schrijven het nu eenmaal verschillend.</summary>
    public static string KaalPoNummer(string zoek)
    {
        var s = zoek.Trim().TrimStart('#').Trim();
        if (s.StartsWith("PO", StringComparison.OrdinalIgnoreCase))
        {
            var rest = s[2..].TrimStart('-', ' ');
            // Alleen strippen als er iets bruikbaars overblijft; wie zoekt op
            // "Poedercoating" bedoelt geen PO-nummer.
            if (rest.Length > 0 && char.IsDigit(rest[0])) s = rest;
        }
        return s;
    }

    public async Task<List<PoKeuze>> ZoekPosAsync(int leverancierId, string zoek, int max)
    {
        var lijst = new List<PoKeuze>();
        zoek = zoek.Trim();

        // Voor het nummer-deel zoeken we met de kale variant, zodat PO12345,
        // PO-12345 en 12345 allemaal dezelfde order vinden.
        var kaal = KaalPoNummer(zoek);
        if (kaal.Length == 0) kaal = zoek;

        await using var cn = await OpenAsync();

        // Zonder zoektekst dezelfde selectie als de Access-keuzelijst (status 3
        // t/m 6: besteld of verder). Wie zoekt, zoekt door alles van de leverancier.
        var filter = zoek.Length == 0
            ? " AND o.`Status` BETWEEN 3 AND 6 "
            : " AND (CAST(o.`Order-id` AS CHAR) LIKE @kaal " +
              $"      OR {PoNummerSql} LIKE @kaal " +
              "      OR o.`Description` LIKE @zoek " +
              "      OR w.`Werkbonomschrijving` LIKE @zoek " +
              "      OR p.`Projectdescription` LIKE @zoek) ";

        await using var cmd = new MySqlCommand(
            $"SELECT o.`Order-id`, {PoNummerSql}, COALESCE(o.`Description`, ''), " +
            "        COALESCE(w.`Werkbonomschrijving`, ''), " +
            "        COALESCE((SELECT SUM(oi.`Totalprice`) FROM `Orderitems` oi " +
            "                   WHERE oi.`Order-id` = o.`Order-id`), 0), " +
            "        COALESCE(o.`Status`, 0), o.`Creation date` " +
            "   FROM `Orders` o " +
            "   LEFT JOIN `Workorders` w ON w.`Werkbon-id` = o.`Workorder-id` " +
            "   LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            $"  WHERE o.`Supplier` = @lev {filter} " +
            "  ORDER BY o.`Order-id` DESC LIMIT " + Math.Clamp(max, 1, 200), cn);
        cmd.Parameters.AddWithValue("@lev", leverancierId);
        if (zoek.Length > 0)
        {
            cmd.Parameters.AddWithValue("@zoek", "%" + zoek + "%");
            cmd.Parameters.AddWithValue("@kaal", "%" + kaal + "%");
        }

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new PoKeuze(
                r.GetInt32(0),
                r.IsDBNull(1) ? "" : r.GetString(1),
                r.GetString(2), r.GetString(3),
                Convert.ToDecimal(r.GetValue(4)),
                Convert.ToInt32(r.GetValue(5)),
                r.IsDBNull(6) ? null : r.GetDateTime(6)));
        return lijst;
    }

    public async Task<List<WerkorderKeuze>> ZoekInkoopWerkordersAsync(string zoek, bool alleenOpen, int max)
    {
        var lijst = new List<WerkorderKeuze>();
        zoek = zoek.Trim();

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT w.`Werkbon-id`, " +
            "       CONCAT(COALESCE(p.`Projectnumber`, '?'), '-', " +
            "              RIGHT(CONCAT('00', w.`Werkbonnummer`), 2)), " +
            "       COALESCE(w.`Werkbonomschrijving`, ''), COALESCE(p.`Projectdescription`, ''), " +
            "       COALESCE(w.`WorkOrderStatus`, 0) " +
            "  FROM `Workorders` w " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            // Dezelfde regels als de Access-zoekpopup: alleen werkorders waar
            // ingekocht op mag worden, en geen verwijderde.
            " WHERE COALESCE(w.`InkoopToegestaan`, 0) <> 0 " +
            "   AND COALESCE(w.`Deleted`, 0) = 0 " +
            (alleenOpen ? "   AND COALESCE(w.`WorkOrderStatus`, 0) = 1 " : "") +
            (zoek.Length == 0 ? "" :
             "   AND (CAST(p.`Projectnumber` AS CHAR) LIKE @zoek " +
             "        OR CAST(w.`Werkbonnummer` AS CHAR) LIKE @zoek " +
             "        OR CONCAT(COALESCE(p.`Projectnumber`, '?'), '-', " +
             "                  RIGHT(CONCAT('00', COALESCE(w.`Werkbonnummer`, 0)), 2)) LIKE @zoek " +
             "        OR w.`Werkbonomschrijving` LIKE @zoek " +
             "        OR p.`Projectdescription` LIKE @zoek) ") +
            " ORDER BY p.`Projectnumber` DESC, w.`Werkbonnummer` " +
            " LIMIT " + Math.Clamp(max, 1, 200), cn);
        if (zoek.Length > 0) cmd.Parameters.AddWithValue("@zoek", "%" + zoek + "%");

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new WerkorderKeuze(
                r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3),
                Convert.ToInt32(r.GetValue(4)) == 1));
        return lijst;
    }

    public async Task<bool> KoppelAsync(int factuurId, int orderId, decimal bedrag, int? doorWerknemerId)
    {
        await using var cn = await OpenAsync();

        // Twee keer dezelfde PO aan dezelfde factuur zegt vrijwel altijd: vergissing.
        await using (var check = new MySqlCommand(
            "SELECT COUNT(*) FROM `Facturen_Orders_Link` " +
            " WHERE `FactuurID` = @f AND `OrderID` = @o", cn))
        {
            check.Parameters.AddWithValue("@f", factuurId);
            check.Parameters.AddWithValue("@o", orderId);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0)
                throw new InvalidOperationException("Deze PO is al aan de factuur gekoppeld.");
        }

        // De PO moet van dezelfde leverancier zijn als de factuur. Dit vangt
        // onder meer een leverancierwissel af nadat de PO al gekozen was.
        await using (var check = new MySqlCommand(
            "SELECT COALESCE(o.`Supplier`, 0), COALESCE(f.`Bedrijf`, 0) " +
            "  FROM `Orders` o, `FacturenInkomend` f " +
            $" WHERE o.`Order-id` = @o AND f.`{Sleutel}` = @f", cn))
        {
            check.Parameters.AddWithValue("@o", orderId);
            check.Parameters.AddWithValue("@f", factuurId);
            await using var r = await check.ExecuteReaderAsync();
            if (await r.ReadAsync() && r.GetInt32(0) != r.GetInt32(1))
                throw new InvalidOperationException(
                    "Deze PO hoort bij een andere leverancier dan de factuur.");
        }

        var (doorbelast, block) = await OrderGegevensAsync(cn, orderId);

        await using (var cmd = new MySqlCommand(
            "INSERT INTO `Facturen_Orders_Link` " +
            " (`FactuurID`, `OrderID`, `Bedrag`, `VerkoopBedrag`, `DatumAanmaak`, `AanmaakDoor`) " +
            "VALUES (@f, @o, @b, @v, @nu, @door)", cn))
        {
            cmd.Parameters.AddWithValue("@f", factuurId);
            cmd.Parameters.AddWithValue("@o", orderId);
            cmd.Parameters.AddWithValue("@b", bedrag);
            cmd.Parameters.AddWithValue("@v", Math.Round(doorbelast / 100m * bedrag, 2));
            cmd.Parameters.AddWithValue("@nu", DateTime.Now);
            cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        await WerkSummaryBijAsync(cn, factuurId);
        return block;
    }

    public async Task<int> MaakPoEnKoppelAsync(int factuurId, string factuurnummer, int werkbonId,
                                               int leverancierId, decimal bedrag, int? doorWerknemerId)
    {
        var nu = DateTime.Now;
        var omschrijving = "Automatisch gegenereerd op basis van factuur "
                         + (factuurnummer.Length > 0 ? factuurnummer : "#" + factuurId);

        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        try
        {
            // Volgnummer binnen de werkorder (Access: DMax + 1). FOR UPDATE zodat
            // twee gelijktijdige koppelingen niet hetzelfde PO-nummer krijgen.
            int extensie;
            await using (var cmd = new MySqlCommand(
                "SELECT COALESCE(MAX(`Extension`), 0) + 1 FROM `Orders` " +
                " WHERE `Workorder-id` = @w FOR UPDATE", cn, tx))
            {
                cmd.Parameters.AddWithValue("@w", werkbonId);
                extensie = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            // Vaste contactpersoon en taal van de leverancier, zoals Access ze meenam.
            object contact = DBNull.Value, taal = DBNull.Value;
            await using (var cmd = new MySqlCommand(
                "SELECT `OrderContact-id`, `OrderLanguage` FROM `Relations` " +
                " WHERE `id_supplier` = @lev", cn, tx))
            {
                cmd.Parameters.AddWithValue("@lev", leverancierId);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    if (!r.IsDBNull(0)) contact = r.GetValue(0);
                    if (!r.IsDBNull(1)) taal = r.GetValue(1);
                }
            }

            // De initialen voor 'Needed-by' (nvarchar(5)).
            var initialen = "";
            if (doorWerknemerId is int wn)
            {
                await using var cmd = new MySqlCommand(
                    "SELECT COALESCE(`Afkorting`, '') FROM `Werknemers` " +
                    " WHERE `Werknemer-id` = @id", cn, tx);
                cmd.Parameters.AddWithValue("@id", wn);
                initialen = (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
                if (initialen.Length > 5) initialen = initialen[..5];
            }

            // 1. De order zelf. Status 6 (afgerond) en alle datums op nu — er is
            //    immers al geleverd én gefactureerd; dit legt het alleen nog vast.
            int orderId;
            await using (var cmd = new MySqlCommand(
                "INSERT INTO `Orders` SET " +
                " `Workorder-id` = @w, `Extension` = @ext, `Supplier` = @lev, " +
                " `Description` = 'Created from invoice link', `Status` = 6, " +
                " `Creation date` = @nu, `Created by` = @door, " +
                " `Request date` = @nu, `Requested by` = @door, " +
                " `Ordered date` = @nu, `Ordered by` = @door, " +
                " `Delivered date` = @nu, `Delivered by` = @door, " +
                " `Invoiced date` = @nu, `Invoiced by` = @door, " +
                " `Contact` = @contact, `Language` = @taal, `GUID` = @guid", cn, tx))
            {
                cmd.Parameters.AddWithValue("@w", werkbonId);
                cmd.Parameters.AddWithValue("@ext", extensie);
                cmd.Parameters.AddWithValue("@lev", leverancierId);
                cmd.Parameters.AddWithValue("@nu", nu);
                cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@contact", contact);
                cmd.Parameters.AddWithValue("@taal", taal);
                cmd.Parameters.AddWithValue("@guid", Guid.NewGuid().ToString("N").ToUpperInvariant());
                await cmd.ExecuteNonQueryAsync();
                orderId = (int)cmd.LastInsertedId;
            }

            // 2. Eén orderregel voor het gekoppelde bedrag.
            int itemId;
            await using (var cmd = new MySqlCommand(
                "INSERT INTO `Orderitems` SET " +
                " `Order-id` = @o, `Quantity ordered` = 1, `VE-id` = 2, " +
                " `Description` = @oms, `Agreed date of delivery` = @vandaag, " +
                " `Needed-by` = @init, `Grossprice per VE` = @bedrag, `Discount` = 0, " +
                " `Totalprice` = @bedrag, `FactuurLink` = @f, `GUID` = @guid", cn, tx))
            {
                cmd.Parameters.AddWithValue("@o", orderId);
                cmd.Parameters.AddWithValue("@oms", omschrijving);
                cmd.Parameters.AddWithValue("@vandaag", DateTime.Today);
                cmd.Parameters.AddWithValue("@init", initialen);
                cmd.Parameters.AddWithValue("@bedrag", bedrag);
                cmd.Parameters.AddWithValue("@f", factuurId);
                cmd.Parameters.AddWithValue("@guid", Guid.NewGuid().ToString("N").ToUpperInvariant());
                await cmd.ExecuteNonQueryAsync();
                itemId = (int)cmd.LastInsertedId;
            }

            // 3. Meteen op geleverd, zoals Access (pakbon 'NVT').
            await using (var cmd = new MySqlCommand(
                "INSERT INTO `OrderItemsDelivered` SET " +
                " `OrderItem-id` = @i, `Order-ID` = @o, `Quantity delivered` = 1, " +
                " `Pakbonnummer` = 'NVT', `Pakbon signed at` = @nu, `Pakbon signed by` = @door", cn, tx))
            {
                cmd.Parameters.AddWithValue("@i", itemId);
                cmd.Parameters.AddWithValue("@o", orderId);
                cmd.Parameters.AddWithValue("@nu", nu);
                cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            // 4. De koppeling zelf, met het doorbelastbedrag van de werkorder.
            decimal doorbelast = 100m;
            await using (var cmd = new MySqlCommand(
                "SELECT COALESCE(`Doorbelast_deel_materials`, 100) FROM `Workorders` " +
                " WHERE `Werkbon-id` = @w", cn, tx))
            {
                cmd.Parameters.AddWithValue("@w", werkbonId);
                var uit = await cmd.ExecuteScalarAsync();
                if (uit is not null && uit is not DBNull) doorbelast = Convert.ToDecimal(uit);
            }

            await using (var cmd = new MySqlCommand(
                "INSERT INTO `Facturen_Orders_Link` " +
                " (`FactuurID`, `OrderID`, `Bedrag`, `VerkoopBedrag`, `DatumAanmaak`, `AanmaakDoor`) " +
                "VALUES (@f, @o, @b, @v, @nu, @door)", cn, tx))
            {
                cmd.Parameters.AddWithValue("@f", factuurId);
                cmd.Parameters.AddWithValue("@o", orderId);
                cmd.Parameters.AddWithValue("@b", bedrag);
                cmd.Parameters.AddWithValue("@v", Math.Round(doorbelast / 100m * bedrag, 2));
                cmd.Parameters.AddWithValue("@nu", nu);
                cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            await WerkSummaryBijAsync(cn, factuurId);
            return orderId;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> IsEigenAutoPoAsync(int orderId, int factuurId)
    {
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT (SELECT COUNT(*) FROM `Orderitems` WHERE `Order-id` = @o), " +
                "       (SELECT COUNT(*) FROM `Orderitems` WHERE `Order-id` = @o " +
                "         AND `FactuurLink` = @f), " +
                "       (SELECT COUNT(*) FROM `Facturen_Orders_Link` WHERE `OrderID` = @o " +
                "         AND `FactuurID` <> @f)", cn);
            cmd.Parameters.AddWithValue("@o", orderId);
            cmd.Parameters.AddWithValue("@f", factuurId);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return false;

            var totaal = r.GetInt32(0);
            var eigen = r.GetInt32(1);
            var anderen = r.GetInt32(2);

            // Alle regels komen uit deze factuur en niemand anders hangt eraan.
            return totaal > 0 && totaal == eigen && anderen == 0;
        }
        catch { return false; }
    }

    public async Task OntkoppelAsync(int linkId, bool ookAutoPo)
    {
        await using var cn = await OpenAsync();

        int factuurId = 0, orderId = 0;
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(`FactuurID`, 0), COALESCE(`OrderID`, 0) " +
            "  FROM `Facturen_Orders_Link` WHERE `LinkID` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", linkId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) { factuurId = r.GetInt32(0); orderId = r.GetInt32(1); }
        }

        // De automatisch aangemaakte PO mag mee weg — maar alleen na een eigen
        // hercontrole, zodat een verkeerd aangeklikte knop nooit een echte
        // inkooporder kan wissen.
        var poMeeWeg = ookAutoPo && orderId > 0 && await IsEigenAutoPoAsync(orderId, factuurId);

        // Link en (eventueel) PO in één transactie: anders kan er bij een fout
        // een losgezongen PO zonder factuurkoppeling achterblijven.
        await using var tx = await cn.BeginTransactionAsync();
        try
        {
            await using (var del = new MySqlCommand(
                "DELETE FROM `Facturen_Orders_Link` WHERE `LinkID` = @id", cn, tx))
            {
                del.Parameters.AddWithValue("@id", linkId);
                await del.ExecuteNonQueryAsync();
            }

            if (poMeeWeg)
            {
                foreach (var sql in new[]
                {
                    "DELETE FROM `OrderItemsDelivered` WHERE `Order-ID` = @o",
                    "DELETE FROM `Orderitems` WHERE `Order-id` = @o",
                    "DELETE FROM `Orders` WHERE `Order-id` = @o",
                })
                {
                    await using var cmd = new MySqlCommand(sql, cn, tx);
                    cmd.Parameters.AddWithValue("@o", orderId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        if (factuurId > 0) await WerkSummaryBijAsync(cn, factuurId);
    }

    public async Task ZetKoppelBedragAsync(int linkId, decimal bedrag, int? doorWerknemerId)
    {
        await using var cn = await OpenAsync();

        // Het doorbelastpercentage eerst ophalen en in C# rekenen, met dezelfde
        // afronding als bij het aanmaken — anders kan hetzelfde bedrag via
        // wijzigen een cent verschillen van via koppelen.
        decimal doorbelast = 100m;
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(w.`Doorbelast_deel_materials`, 100) " +
            "  FROM `Facturen_Orders_Link` l " +
            "  LEFT JOIN `Orders` o ON o.`Order-id` = l.`OrderID` " +
            "  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = o.`Workorder-id` " +
            " WHERE l.`LinkID` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", linkId);
            var uit = await cmd.ExecuteScalarAsync();
            if (uit is not null && uit is not DBNull) doorbelast = Convert.ToDecimal(uit);
        }

        await using var upd = new MySqlCommand(
            "UPDATE `Facturen_Orders_Link` " +
            "   SET `Bedrag` = @b, `VerkoopBedrag` = @v, " +
            "       `DatumWijziging` = @nu, `WijzigingDoor` = @door " +
            " WHERE `LinkID` = @id", cn);
        upd.Parameters.AddWithValue("@b", bedrag);
        upd.Parameters.AddWithValue("@v", Math.Round(doorbelast / 100m * bedrag, 2));
        upd.Parameters.AddWithValue("@nu", DateTime.Now);
        upd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
        upd.Parameters.AddWithValue("@id", linkId);
        await upd.ExecuteNonQueryAsync();
    }
}
