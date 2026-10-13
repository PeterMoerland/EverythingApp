using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Uitgaande (verkoop)facturen: tabel Invoices met de regels in InvoiceHours.</summary>
public interface IVerkoopFactuurRepository
{
    bool Beschikbaar { get; }

    Task<List<VerkoopFactuurRegel>> ZoekAsync(VerkoopFactuurFilter filter, int max);
    Task<FactuurTotalen> GetTotalenAsync(VerkoopFactuurFilter filter);

    /// <summary>De eigen BV's die het afgelopen jaar gefactureerd hebben: de snelknoppen.</summary>
    Task<List<Administratie>> GetFacturerendeBvsAsync();

    /// <summary>Alle eigen BV's, voor de keuzelijst.</summary>
    Task<List<Administratie>> GetAdministratiesAsync();

    Task<List<VerkoopFactuurItem>> GetItemsAsync(int factuurId);
    Task<List<VerkoopFactuurBetaling>> GetBetalingenAsync(int factuurId);

    /// <summary>Zet een vinkje, met wie en wanneer waar die kolommen er zijn.</summary>
    Task ZetVlagAsync(int id, VerkoopFactuurVlag vlag, bool aan, int? doorWerknemerId);

    /// <summary>
    /// Wist een factuur die nog niet verstuurd is, en geeft de daarop gereserveerde uren
    /// weer vrij. Geeft een reden terug als het niet mag, anders null.
    /// </summary>
    Task<string?> WisAsync(int id);

    // ---- het venster Nieuw/Aanpassen ----

    Task<VerkoopFactuur?> GetAsync(int id);

    /// <summary>Het volgende nummer voor deze BV: jaar + volgnummer van drie cijfers.</summary>
    Task<string> VolgendNummerAsync(int administratieId);

    /// <summary>Betalingstermijn (laatste tariefafspraak), btw verlegd en Engels van een klant.</summary>
    Task<KlantFactuurStandaard> KlantStandaardAsync(int klantId);

    Task<FactuurArtikel?> ArtikelAsync(int artikelId);

    /// <summary>
    /// Slaat kop en regels op en zet de urenreserveringen opnieuw, in één transactie.
    /// Een nieuwe factuur krijgt hier pas haar echte nummer. Geeft het id terug.
    /// </summary>
    Task<int> BewaarAsync(VerkoopFactuur factuur, int? doorWerknemerId);

    // ---- PDF en versturen ----

    /// <summary>Factuur, klantadres, afzender en de uren voor de bijlage. Logo en extra regels vult de PDF-dienst aan.</summary>
    Task<FactuurPdfGegevens?> GetPdfGegevensAsync(int id);

    /// <summary>De mailtekst uit General_settings (eInvoiceMailTekst_NL of _EN).</summary>
    Task<string> MailTekstAsync(bool engels);

    /// <summary>
    /// Zoals Access na versturen: SentToClient (-1) en DateSent, en de uren van deze factuur
    /// op gefactureerd. Bij een herinnering blijft de oorspronkelijke verzenddatum staan.
    /// </summary>
    Task MarkeerVerstuurdAsync(int id);

    // ---- betalingscheck ----

    /// <summary>Onbetaalde, niet oninbare facturen met een positief bedrag, van BV's met een IBAN.</summary>
    Task<List<OpenFactuur>> GetOpenFacturenAsync();

    /// <summary>Ontvangsten op de rekeningen van de eigen BV's vanaf deze datum, die nog nergens aan hangen.</summary>
    Task<List<Ontvangst>> GetOntvangstenAsync(DateOnly vanaf);

    /// <summary>Zet de facturen op betaald (datum van de laatste ontvangst) en legt de koppelingen vast. Geeft het aantal facturen terug.</summary>
    Task<int> VerwerkBetalingenAsync(IEnumerable<BetalingVoorstel> voorstellen, int? doorWerknemerId);
}

public class GeenVerkoopFactuurRepository : IVerkoopFactuurRepository
{
    public bool Beschikbaar => false;
    public Task<List<VerkoopFactuurRegel>> ZoekAsync(VerkoopFactuurFilter filter, int max)
        => Task.FromResult(new List<VerkoopFactuurRegel>());
    public Task<FactuurTotalen> GetTotalenAsync(VerkoopFactuurFilter filter)
        => Task.FromResult(new FactuurTotalen(0, 0, 0));
    public Task<List<Administratie>> GetFacturerendeBvsAsync() => Task.FromResult(new List<Administratie>());
    public Task<List<Administratie>> GetAdministratiesAsync() => Task.FromResult(new List<Administratie>());
    public Task<List<VerkoopFactuurItem>> GetItemsAsync(int factuurId) => Task.FromResult(new List<VerkoopFactuurItem>());
    public Task<List<VerkoopFactuurBetaling>> GetBetalingenAsync(int factuurId)
        => Task.FromResult(new List<VerkoopFactuurBetaling>());
    public Task ZetVlagAsync(int id, VerkoopFactuurVlag vlag, bool aan, int? doorWerknemerId) => Task.CompletedTask;
    public Task<string?> WisAsync(int id) => Task.FromResult<string?>("Geen database.");
    public Task<VerkoopFactuur?> GetAsync(int id) => Task.FromResult<VerkoopFactuur?>(null);
    public Task<string> VolgendNummerAsync(int administratieId) => Task.FromResult("");
    public Task<KlantFactuurStandaard> KlantStandaardAsync(int klantId)
        => Task.FromResult(new KlantFactuurStandaard(30, false, false));
    public Task<FactuurArtikel?> ArtikelAsync(int artikelId) => Task.FromResult<FactuurArtikel?>(null);
    public Task<int> BewaarAsync(VerkoopFactuur factuur, int? doorWerknemerId) => Task.FromResult(0);
    public Task<FactuurPdfGegevens?> GetPdfGegevensAsync(int id) => Task.FromResult<FactuurPdfGegevens?>(null);
    public Task<string> MailTekstAsync(bool engels) => Task.FromResult("");
    public Task MarkeerVerstuurdAsync(int id) => Task.CompletedTask;
    public Task<List<OpenFactuur>> GetOpenFacturenAsync() => Task.FromResult(new List<OpenFactuur>());
    public Task<List<Ontvangst>> GetOntvangstenAsync(DateOnly vanaf) => Task.FromResult(new List<Ontvangst>());
    public Task<int> VerwerkBetalingenAsync(IEnumerable<BetalingVoorstel> voorstellen, int? doorWerknemerId)
        => Task.FromResult(0);
}

/// <summary>
/// Uitgaande facturen uit Invoices.
///
/// Access las de lijst uit de view @UitgaandeFacturen. Die rekent 'overdue' met
/// Betaald &lt;&gt; 1, maar Access schreef 'waar' als -1; ruim 700 betaalde facturen
/// tellen daar dus als onbetaald. Hier rekenen we het zelf uit, met &lt;&gt; 0 voor elk
/// ja/nee-veld. Zelf schrijven we 1, zoals de rest van de app.
/// </summary>
public class MySqlVerkoopFactuurRepository : IVerkoopFactuurRepository
{
    private readonly string _cs;

    public MySqlVerkoopFactuurRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private const string Sleutel = "Invoice-id";

    /// <summary>
    /// Dagen te laat. Positief = over de vervaldatum, negatief = nog te gaan,
    /// 0 zodra de factuur betaald is. Zelfde formule als de Access-view.
    /// </summary>
    private const string OverdueUit =
        "CASE WHEN COALESCE(f.`Betaald`, 0) <> 0 THEN 0 " +
        "     ELSE DATEDIFF(CURDATE(), f.`Date invoice`) - COALESCE(f.`Betalingstermijn`, 0) END";

    private const string Van =
        "  FROM `Invoices` f " +
        "  LEFT JOIN `Relations` r ON r.`id_supplier` = f.`Customer-id` " +
        "  LEFT JOIN `Relations` a ON a.`id_supplier` = f.`Werkmaatschappij` ";

    // ============================================================
    //  Voorwaarden, één plek voor de lijst en de totalen
    // ============================================================
    private static string Voorwaarden(VerkoopFactuurFilter f)
    {
        var delen = new List<string>();

        if (f.Van is not null) delen.Add("f.`Date invoice` >= @van");
        if (f.Tot is not null) delen.Add("f.`Date invoice` < @tot");

        // Access: 'overdue > FilterOverdue'; 0 = te laat, -7 = vervalt deze week.
        if (f.MeerDanOverdue is not null) delen.Add($"({OverdueUit}) > @overdue");

        if (f.Klant.Trim().Length > 0) delen.Add("r.`name` LIKE @klant");
        if (f.AdministratieId is not null) delen.Add("f.`Werkmaatschappij` = @admin");

        // Zoeken over één samengeraapte tekst, zoals Access: omschrijving, PO klant,
        // bedragen en factuurnummer. Bedragen met punt én komma, zodat 1234.50 en
        // 1234,50 allebei gevonden worden.
        if (f.Zoek.Trim().Length > 0)
            delen.Add("CONCAT_WS(' ', COALESCE(f.`Description`,''), COALESCE(f.`PO_Klant`,''), " +
                      "COALESCE(f.`Invoicenumber`,''), COALESCE(f.`Referentie`,''), " +
                      "COALESCE(f.`Opmerking`,''), " +
                      "CAST(ROUND(COALESCE(f.`Total ex`,0), 2) AS CHAR), " +
                      "REPLACE(CAST(ROUND(COALESCE(f.`Total ex`,0), 2) AS CHAR), '.', ','), " +
                      "CAST(ROUND(COALESCE(f.`Total incl`,0), 2) AS CHAR), " +
                      "REPLACE(CAST(ROUND(COALESCE(f.`Total incl`,0), 2) AS CHAR), '.', ',')) LIKE @zoek");

        Voeg(delen, f.Verstuurd, "f.`SentToClient`");
        Voeg(delen, f.Betaald, "f.`Betaald`");

        return delen.Count == 0 ? "" : " WHERE " + string.Join(" AND ", delen);
    }

    private static void Voeg(List<string> delen, Drieluik keuze, string kolom)
    {
        if (keuze == Drieluik.Ja) delen.Add($"COALESCE({kolom}, 0) <> 0");
        if (keuze == Drieluik.Nee) delen.Add($"COALESCE({kolom}, 0) = 0");
    }

    private static void Vul(MySqlCommand cmd, VerkoopFactuurFilter f)
    {
        if (f.Van is not null)
            cmd.Parameters.AddWithValue("@van", f.Van.Value.ToDateTime(TimeOnly.MinValue));
        if (f.Tot is not null)
            cmd.Parameters.AddWithValue("@tot", f.Tot.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (f.MeerDanOverdue is not null) cmd.Parameters.AddWithValue("@overdue", f.MeerDanOverdue.Value);
        if (f.Klant.Trim().Length > 0) cmd.Parameters.AddWithValue("@klant", "%" + f.Klant.Trim() + "%");
        if (f.AdministratieId is not null) cmd.Parameters.AddWithValue("@admin", f.AdministratieId.Value);
        if (f.Zoek.Trim().Length > 0) cmd.Parameters.AddWithValue("@zoek", "%" + f.Zoek.Trim() + "%");
    }

    // ============================================================
    //  De lijst
    // ============================================================
    public async Task<List<VerkoopFactuurRegel>> ZoekAsync(VerkoopFactuurFilter filter, int max)
    {
        var lijst = new List<VerkoopFactuurRegel>();

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT f.`{Sleutel}`, COALESCE(f.`Invoicenumber`, ''), f.`Date invoice`, " +
            "       COALESCE(f.`Betalingstermijn`, 0), f.`Customer-id`, COALESCE(r.`name`, ''), " +
            "       COALESCE(f.`Description`, ''), COALESCE(f.`PO_Klant`, ''), COALESCE(f.`Opmerking`, ''), " +
            "       COALESCE(f.`Total ex`, 0), COALESCE(f.`Total btw`, 0), COALESCE(f.`Total incl`, 0), " +
            "       f.`Werkmaatschappij`, COALESCE(a.`Afkorting`, ''), " +
            "       COALESCE(f.`SentToClient`, 0), f.`DateSent`, " +
            "       COALESCE(f.`Betaald`, 0), f.`DatumBetaald`, " +
            "       COALESCE(f.`VerzondenBasecone`, 0), COALESCE(f.`On-Inbaar`, 0), " +
            "       COALESCE(f.`Betalingsherinnering`, 0), " +
            $"       ({OverdueUit}) AS Overdue " +
            Van +
            Voorwaarden(filter) +
            // Zoals Access: hoogste nummer bovenaan. Het nummer begint met het jaar.
            $" ORDER BY f.`Invoicenumber` DESC, f.`Date invoice` DESC, f.`{Sleutel}` DESC " +
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

            lijst.Add(new VerkoopFactuurRegel
            {
                Id = r2.GetInt32(0),
                Factuurnummer = S(1),
                Factuurdatum = D(2),
                Betalingstermijn = I(3) ?? 0,
                KlantId = I(4),
                Klant = S(5),
                Omschrijving = S(6),
                PoKlant = S(7),
                Opmerking = S(8),
                BedragExBtw = M(9),
                Btw = M(10),
                BedragInclBtw = M(11),
                AdministratieId = I(12),
                Administratie = S(13),
                Verstuurd = B(14),
                DatumVerstuurd = D(15),
                Betaald = B(16),
                DatumBetaald = D(17),
                NaarBasecone = B(18),
                Oninbaar = B(19),
                Herinnering = I(20) ?? 0,
                Overdue = I(21) ?? 0,
            });
        }
        return lijst;
    }

    public async Task<FactuurTotalen> GetTotalenAsync(VerkoopFactuurFilter filter)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(SUM(COALESCE(f.`Total ex`, 0)), 0), " +
            "       COALESCE(SUM(COALESCE(f.`Total btw`, 0)), 0), COUNT(*) " +
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
    //  De eigen BV's
    // ============================================================
    public async Task<List<Administratie>> GetAdministratiesAsync()
    {
        var lijst = new List<Administratie>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `id_supplier`, COALESCE(`Afkorting`, ''), COALESCE(`name`, '') " +
            "  FROM `Relations` WHERE COALESCE(`Werkmaatschappij`, 0) <> 0 ORDER BY `Afkorting`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Administratie(r.GetInt32(0), r.GetString(1), r.GetString(2)));
        return lijst;
    }

    public async Task<List<Administratie>> GetFacturerendeBvsAsync()
    {
        var lijst = new List<Administratie>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT a.`id_supplier`, COALESCE(a.`Afkorting`, ''), COALESCE(a.`name`, '') " +
            "  FROM `Relations` a " +
            " WHERE COALESCE(a.`Werkmaatschappij`, 0) <> 0 " +
            "   AND EXISTS (SELECT 1 FROM `Invoices` f WHERE f.`Werkmaatschappij` = a.`id_supplier` " +
            "                  AND f.`Date invoice` >= CURDATE() - INTERVAL 1 YEAR) " +
            " ORDER BY a.`Afkorting`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Administratie(r.GetInt32(0), r.GetString(1), r.GetString(2)));
        return lijst;
    }

    // ============================================================
    //  Wat bij één factuur hoort
    // ============================================================
    public async Task<List<VerkoopFactuurItem>> GetItemsAsync(int factuurId)
    {
        var lijst = new List<VerkoopFactuurItem>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `InvoiceHoursID`, COALESCE(`Order`, 0), COALESCE(`WO`, ''), " +
            "       COALESCE(`ItemDescription`, ''), COALESCE(`WorkorderDescription`, ''), " +
            "       COALESCE(`Naam`, ''), COALESCE(`Uurtype`, ''), " +
            "       `Uren`, `Kilometers`, `Quantity`, `StuksPrijs`, `Korting`, COALESCE(`Totaal`, 0), " +
            "       COALESCE(`ProjectDescription`, '') " +
            "  FROM `InvoiceHours` WHERE `InvoiceID` = @id ORDER BY `Order`, `InvoiceHoursID`", cn);
        cmd.Parameters.AddWithValue("@id", factuurId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            decimal? M(int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
            lijst.Add(new VerkoopFactuurItem
            {
                Id = r.GetInt32(0),
                Volgorde = Convert.ToInt32(r.GetValue(1)),
                // Access zette een lege werkorder als "0" weg (Null_Zero).
                Werkorder = r.GetString(2) == "0" ? "" : r.GetString(2),
                Omschrijving = r.GetString(3),
                WerkorderOmschrijving = r.GetString(4),
                Naam = r.GetString(5),
                Uurtype = r.GetString(6),
                Uren = M(7),
                Kilometers = M(8),
                Aantal = M(9),
                Stuksprijs = M(10),
                Korting = M(11),
                Totaal = M(12) ?? 0,
                ProjectOmschrijving = r.GetString(13),
            });
        }
        return lijst;
    }

    // ============================================================
    //  Eén factuur, voor het venster
    // ============================================================
    public async Task<VerkoopFactuur?> GetAsync(int id)
    {
        VerkoopFactuur f;
        await using (var cn = await OpenAsync())
        {
            await using var cmd = new MySqlCommand(
                "SELECT f.`Werkmaatschappij`, COALESCE(f.`Invoicenumber`, ''), f.`Customer-id`, " +
                "       COALESCE(r.`name`, ''), f.`Date invoice`, f.`Date creation`, " +
                "       COALESCE(w.`NaamVoluit`, ''), COALESCE(f.`Betalingstermijn`, 0), " +
                "       COALESCE(f.`Referentie`, ''), COALESCE(f.`PO_Klant`, ''), " +
                "       COALESCE(f.`Description`, ''), COALESCE(f.`Opmerking`, ''), " +
                "       COALESCE(f.`BTWverlegd`, 0), COALESCE(f.`FactuurEngels`, 0), " +
                "       COALESCE(f.`Betalingsherinnering`, 0), COALESCE(f.`Betaald`, 0), f.`DatumBetaald`, " +
                "       COALESCE(f.`On-Inbaar`, 0), COALESCE(f.`SentToClient`, 0), f.`DateSent`, " +
                "       COALESCE(f.`VerzondenBasecone`, 0), f.`VerzondenBaseconeOp` " +
                "  FROM `Invoices` f " +
                "  LEFT JOIN `Relations` r ON r.`id_supplier` = f.`Customer-id` " +
                "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = f.`CreatedBy` " +
                $" WHERE f.`{Sleutel}` = @id", cn);
            cmd.Parameters.AddWithValue("@id", id);

            await using var r2 = await cmd.ExecuteReaderAsync();
            if (!await r2.ReadAsync()) return null;

            string S(int i) => r2.IsDBNull(i) ? "" : r2.GetValue(i).ToString() ?? "";
            int? I(int i) => r2.IsDBNull(i) ? null : Convert.ToInt32(r2.GetValue(i));
            bool B(int i) => !r2.IsDBNull(i) && Convert.ToInt64(r2.GetValue(i)) != 0;
            DateTime? D(int i) => r2.IsDBNull(i) ? null : Convert.ToDateTime(r2.GetValue(i));
            DateOnly? Dag(int i) => D(i) is DateTime d ? DateOnly.FromDateTime(d) : null;

            f = new VerkoopFactuur
            {
                Id = id,
                AdministratieId = I(0),
                Factuurnummer = S(1),
                KlantId = I(2),
                Klant = S(3),
                Factuurdatum = Dag(4),
                Aangemaakt = D(5),
                AangemaaktDoor = S(6),
                Betalingstermijn = I(7) ?? 0,
                Referentie = S(8),
                PoKlant = S(9),
                Omschrijving = S(10),
                Opmerking = S(11),
                BtwVerlegd = B(12),
                Engels = B(13),
                Herinnering = I(14) ?? 0,
                Betaald = B(15),
                DatumBetaald = Dag(16),
                Oninbaar = B(17),
                Verstuurd = B(18),
                DatumVerstuurd = D(19),
                NaarBasecone = B(20),
                BaseconeOp = D(21),
            };
        }

        f.Regels = await GetItemsAsync(id);
        return f;
    }

    public async Task<string> VolgendNummerAsync(int administratieId)
    {
        await using var cn = await OpenAsync();
        return await VolgendNummerAsync(cn, null, administratieId);
    }

    /// <summary>
    /// Zoals Access' GetNewNumber: het hoogste nummer van deze BV; is dat van een
    /// vorig jaar, dan begint het jaar bij 001, anders één hoger.
    /// </summary>
    private static async Task<string> VolgendNummerAsync(MySqlConnection cn, MySqlTransaction? tx, int bv)
    {
        await using var cmd = new MySqlCommand(
            "SELECT MAX(`Invoicenumber`) FROM `Invoices` WHERE `Werkmaatschappij` = @bv" +
            (tx is null ? "" : " FOR UPDATE"), cn, tx);
        cmd.Parameters.AddWithValue("@bv", bv);
        var hoogste = (await cmd.ExecuteScalarAsync()) as string ?? "";

        var jaar = DateTime.Today.Year;
        if (hoogste.Length < 5 || !int.TryParse(hoogste[..4], out var nrJaar) || nrJaar < jaar
            || !int.TryParse(hoogste[4..], out var volg))
            return $"{jaar}001";

        return $"{jaar}{volg + 1:000}";
    }

    public async Task<KlantFactuurStandaard> KlantStandaardAsync(int klantId)
    {
        await using var cn = await OpenAsync();

        // Access nam de termijn uit de query 'laatste tariefafspraken': de nieuwste afspraak.
        int? termijn = null;
        await using (var cmd = new MySqlCommand(
            "SELECT `Betalingstermijn` FROM `Tariefafspraken` WHERE `Relation-id` = @k " +
            " ORDER BY `Geldigheid start` DESC LIMIT 1", cn))
        {
            cmd.Parameters.AddWithValue("@k", klantId);
            var w = await cmd.ExecuteScalarAsync();
            if (w is not null and not DBNull) termijn = Convert.ToInt32(w);
        }

        bool verlegd = false, engels = false;
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(`BTWverlegd`, 0), COALESCE(`FactuurEngels`, 0) FROM `Relations` " +
            " WHERE `id_supplier` = @k", cn))
        {
            cmd.Parameters.AddWithValue("@k", klantId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                verlegd = Convert.ToInt64(r.GetValue(0)) != 0;
                engels = Convert.ToInt64(r.GetValue(1)) != 0;
            }
        }

        return new KlantFactuurStandaard(termijn is > 0 ? termijn.Value : 30, verlegd, engels);
    }

    public async Task<FactuurArtikel?> ArtikelAsync(int artikelId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(`ArtikelCodeMDE`, ''), COALESCE(`OmschrijvingNL`, ''), " +
            "       COALESCE(`Verkoopprijs project`, 0) FROM `Artikelen` WHERE `ArtikelID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", artikelId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new FactuurArtikel(artikelId, r.GetString(0), r.GetString(1), Convert.ToDecimal(r.GetValue(2)));
    }

    // ============================================================
    //  Opslaan
    // ============================================================
    public async Task<int> BewaarAsync(VerkoopFactuur f, int? doorWerknemerId)
    {
        if (f.AdministratieId is not int bv) throw new InvalidOperationException("Kies een BV.");
        if (f.KlantId is not int klant) throw new InvalidOperationException("Klant niet ingevuld.");
        if (f.Omschrijving.Trim().Length == 0) throw new InvalidOperationException("Geen omschrijving ingevuld.");

        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        var nieuw = f.Id == 0;
        var regelsVast = false;

        if (nieuw)
        {
            // Pas hier het nummer: de rij-vergrendeling voorkomt dat twee mensen
            // tegelijk hetzelfde nummer krijgen.
            f.Factuurnummer = await VolgendNummerAsync(cn, tx, bv);
        }
        else
        {
            // Een verstuurde factuur: regels en de kopvelden die op de factuur staan
            // liggen vast (Access zette ze op slot). Kijk in de database, niet op het scherm.
            await using var cmd = new MySqlCommand(
                $"SELECT COALESCE(`SentToClient`, 0) FROM `Invoices` WHERE `{Sleutel}` = @id FOR UPDATE", cn, tx);
            cmd.Parameters.AddWithValue("@id", f.Id);
            var w = await cmd.ExecuteScalarAsync();
            if (w is null) throw new InvalidOperationException("Deze factuur bestaat niet meer.");

            // Wie het vinkje Verstuurd weghaalt (zoals in Access kon), maakt de factuur
            // in dezelfde keer weer bewerkbaar.
            regelsVast = Convert.ToInt64(w) != 0 && f.Verstuurd;
        }

        // ---- de kop ----
        var velden = new List<(string Kolom, object? Waarde)>
        {
            ("Referentie", Kort(f.Referentie, 100)),
            ("PO_Klant", Kort(f.PoKlant, 100)),
            ("Opmerking", Kort(f.Opmerking, 400)),
            ("BTWverlegd", f.BtwVerlegd ? 1 : 0),
            ("FactuurEngels", f.Engels ? 1 : 0),
            ("Betalingsherinnering", f.Herinnering),
            ("Betaald", f.Betaald ? 1 : 0),
            ("DatumBetaald", f.Betaald ? f.DatumBetaald?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today : null),
            ("On-Inbaar", f.Oninbaar ? 1 : 0),
        };

        if (!regelsVast)
        {
            velden.Add(("Customer-id", klant));
            velden.Add(("Description", Kort(f.Omschrijving, 100)));
            velden.Add(("Date invoice", (f.Factuurdatum ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue)));
            velden.Add(("Betalingstermijn", f.Betalingstermijn));
            velden.Add(("Total ex", f.BedragExBtw));
            velden.Add(("Total btw", f.Btw));
            velden.Add(("Total incl", f.BedragInclBtw));
        }

        if (nieuw)
        {
            velden.Add(("Invoicenumber", f.Factuurnummer));
            velden.Add(("Werkmaatschappij", bv));
            velden.Add(("Date creation", DateTime.Now));
            velden.Add(("CreatedBy", doorWerknemerId));
            velden.Add(("SentToClient", 0));
        }
        else
        {
            // -1, niet 1: het filter 'Verstuurd' in Access zoekt op "-1*".
            velden.Add(("SentToClient", f.Verstuurd ? -1 : 0));
            velden.Add(("DateSent", f.Verstuurd ? f.DatumVerstuurd ?? DateTime.Now : null));
        }

        if (nieuw)
        {
            await using var cmd = new MySqlCommand(
                $"INSERT INTO `Invoices` ({string.Join(", ", velden.Select(v => $"`{v.Kolom}`"))}) " +
                $"VALUES ({string.Join(", ", velden.Select((_, i) => "@p" + i))}); SELECT LAST_INSERT_ID();",
                cn, tx);
            for (var i = 0; i < velden.Count; i++)
                cmd.Parameters.AddWithValue("@p" + i, velden[i].Waarde ?? DBNull.Value);
            f.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        else
        {
            await using var cmd = new MySqlCommand(
                $"UPDATE `Invoices` SET {string.Join(", ", velden.Select((v, i) => $"`{v.Kolom}` = @p{i}"))} " +
                $" WHERE `{Sleutel}` = @id", cn, tx);
            for (var i = 0; i < velden.Count; i++)
                cmd.Parameters.AddWithValue("@p" + i, velden[i].Waarde ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", f.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        if (!regelsVast)
        {
            await BewaarRegelsAsync(cn, tx, f);
            await ReserveerUrenAsync(cn, tx, f.Id);
        }

        await tx.CommitAsync();
        return f.Id;
    }

    // ============================================================
    //  PDF en versturen
    // ============================================================
    public async Task<FactuurPdfGegevens?> GetPdfGegevensAsync(int id)
    {
        var factuur = await GetAsync(id);
        if (factuur is null) return null;
        var g = new FactuurPdfGegevens { Factuur = factuur };

        await using var cn = await OpenAsync();

        static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : (r.GetValue(i).ToString() ?? "").Trim();

        // Het factuuradres; is er geen apart factuuradres, dan het gewone adres.
        await using (var cmd = new MySqlCommand(
            "SELECT r.`InvoiceName`, r.`name`, r.`InvoiceContactperson1`, r.`InvoiceAddress`, r.`address`, " +
            "       r.`InvoicePO_box`, r.`PO_box`, r.`InvoiceTown`, r.`town`, " +
            "       COALESCE(ci.`Country`, c.`Country`), COALESCE(ci.`Landnummer`, c.`Landnummer`, 31), " +
            "       r.`VAT number`, r.`InvoiceMailAddress` " +
            "  FROM `Relations` r " +
            "  LEFT JOIN `Country` c ON c.`Country-id` = r.`country-id` " +
            "  LEFT JOIN `Country` ci ON ci.`Country-id` = r.`InvoiceCountry-id` " +
            " WHERE r.`id_supplier` = @k", cn))
        {
            cmd.Parameters.AddWithValue("@k", factuur.KlantId ?? 0);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var eigenAdres = S(r, 3).Length > 0;
                g.Klant = new FactuurAdres
                {
                    Naam = S(r, 0).Length > 0 ? S(r, 0) : S(r, 1),
                    Tav = S(r, 2),
                    Adres = eigenAdres ? S(r, 3) : S(r, 4),
                    Postcode = eigenAdres ? S(r, 5) : S(r, 6),
                    Plaats = eigenAdres ? S(r, 7) : S(r, 8),
                    Land = S(r, 9),
                    Landnummer = r.IsDBNull(10) ? 31 : Convert.ToInt32(r.GetValue(10)),
                    BtwNummer = S(r, 11),
                };
                g.MailAdres = S(r, 12);
            }
        }

        await using (var cmd = new MySqlCommand(
            "SELECT `id_supplier`, `Afkorting`, `name`, `address`, `PO_box`, `town`, `email`, " +
            "       `VAT number`, `KvK`, `IBAN`, `BIC` FROM `Relations` WHERE `id_supplier` = @bv", cn))
        {
            cmd.Parameters.AddWithValue("@bv", factuur.AdministratieId ?? 0);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                g.Afzender = new FactuurAfzender
                {
                    Id = r.GetInt32(0), Afkorting = S(r, 1), Naam = S(r, 2), Adres = S(r, 3),
                    Postcode = S(r, 4), Plaats = S(r, 5), Email = S(r, 6), Btw = S(r, 7),
                    Kvk = S(r, 8), Iban = S(r, 9), Bic = S(r, 10),
                };
        }

        // De uren achter elke urenregel, met dezelfde selectie als de reservering
        // (plus de medewerker, zoals de Access-bijlage).
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(p.`Projectdescription`, ''), " +
            "       CONCAT(COALESCE(p.`Projectnumber`, ''), '-', LPAD(COALESCE(w.`Werkbonnummer`, 0), 2, '0')), " +
            "       COALESCE(w.`Werkbonomschrijving`, ''), h.`SelectieVan`, h.`SelectieTot`, " +
            "       COALESCE(w.`Klant referentienummer`, ''), COALESCE(w.`Klant represenative`, ''), " +
            "       u.`StartDatum`, u.`StartTijd`, u.`Eindtijd`, COALESCE(n.`Voornaam`, ''), COALESCE(t.`Tariefsoort`, ''), " +
            "       COALESCE(u.`Uren`, 0), COALESCE(u.`Kilometers`, 0), COALESCE(u.`Omschrijving`, ''), " +
            "       COALESCE(u.`Total_Amount`, 0) " +
            "  FROM `InvoiceHours` h " +
            "  JOIN `Uren` u ON u.`werkorder` = h.`SelectieOrderId` AND u.`Tariefsoort-id` = h.`SelectieUurtypeID` " +
            "               AND u.`Werknemer-id` = h.`WerknemerID` " +
            "               AND u.`StartDatum` >= h.`SelectieVan` AND u.`StartDatum` <= h.`SelectieTot` " +
            "  JOIN `Workorders` w ON w.`Werkbon-id` = u.`werkorder` " +
            "  JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            "  LEFT JOIN `Werknemers` n ON n.`Werknemer-id` = u.`Werknemer-id` " +
            "  LEFT JOIN `Tariefsoort` t ON t.`Tariefsoort-id` = u.`Tariefsoort-id` " +
            " WHERE h.`InvoiceID` = @f AND h.`SelectieVan` IS NOT NULL AND h.`SelectieTot` IS NOT NULL " +
            " ORDER BY p.`Projectnumber`, w.`Werkbonnummer`, u.`StartDatum`, n.`Voornaam`, u.`Uren-id`", cn))
        {
            cmd.Parameters.AddWithValue("@f", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                TimeOnly? Tijd(int i) => r.IsDBNull(i) ? null : TimeOnly.FromDateTime(r.GetDateTime(i));
                g.Uren.Add(new BijlageUur
                {
                    Project = S(r, 0), Werkorder = S(r, 1), WerkorderOmschrijving = S(r, 2),
                    SelectieVan = DateOnly.FromDateTime(r.GetDateTime(3)),
                    SelectieTot = DateOnly.FromDateTime(r.GetDateTime(4)),
                    KlantReferentie = S(r, 5), KlantContact = S(r, 6),
                    Datum = DateOnly.FromDateTime(r.GetDateTime(7)),
                    Start = Tijd(8), Eind = Tijd(9),
                    Medewerker = S(r, 10), Uurtype = S(r, 11),
                    Uren = Convert.ToDecimal(r.GetValue(12)), Kilometers = Convert.ToDecimal(r.GetValue(13)),
                    Omschrijving = S(r, 14), Bedrag = Convert.ToDecimal(r.GetValue(15)),
                });
            }
        }

        return g;
    }

    public async Task<string> MailTekstAsync(bool engels)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT `{(engels ? "eInvoiceMailTekst_EN" : "eInvoiceMailTekst_NL")}` FROM `General_settings` LIMIT 1", cn);
        return (await cmd.ExecuteScalarAsync()) as string ?? "";
    }

    public async Task MarkeerVerstuurdAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        // -1 zoals Access; een herinnering of opnieuw versturen houdt de eerste verzenddatum.
        await using (var cmd = new MySqlCommand(
            $"UPDATE `Invoices` SET `SentToClient` = -1, `DateSent` = COALESCE(`DateSent`, NOW()) WHERE `{Sleutel}` = @f",
            cn, tx))
        {
            cmd.Parameters.AddWithValue("@f", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // Zoals Access' InvoicePrint: de uren in de selectie van de urenregels op gefactureerd.
        await using (var cmd = new MySqlCommand(
            "UPDATE `Uren` u JOIN `InvoiceHours` h " +
            "    ON h.`SelectieUurtypeID` = u.`Tariefsoort-id` AND h.`SelectieOrderId` = u.`werkorder` " +
            "   SET u.`Invoiced` = -1, u.`InvoiceDate` = NOW(), u.`InvoicedOnInvoice` = @f " +
            " WHERE h.`InvoiceID` = @f AND u.`StartDatum` >= h.`SelectieVan` AND u.`StartDatum` <= h.`SelectieTot`",
            cn, tx))
        {
            cmd.Parameters.AddWithValue("@f", id);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    // ============================================================
    //  Betalingscheck
    // ============================================================

    /// <summary>De rekeningnummers van de eigen BV's, zonder spaties: zo matchen ze met banktransacties.</summary>
    private const string BvRekening =
        "JOIN `Relations` bv ON COALESCE(bv.`Werkmaatschappij`, 0) <> 0 AND COALESCE(bv.`IBAN`, '') <> '' ";

    public async Task<List<OpenFactuur>> GetOpenFacturenAsync()
    {
        var lijst = new List<OpenFactuur>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT f.`Invoice-id`, COALESCE(f.`Invoicenumber`, ''), f.`Werkmaatschappij`, COALESCE(bv.`Afkorting`, ''), " +
            "       COALESCE(r.`name`, ''), f.`Date invoice`, COALESCE(f.`Total incl`, 0) " +
            "  FROM `Invoices` f " + BvRekening + " AND bv.`id_supplier` = f.`Werkmaatschappij` " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = f.`Customer-id` " +
            " WHERE COALESCE(f.`Betaald`, 0) = 0 AND COALESCE(f.`On-Inbaar`, 0) = 0 " +
            "   AND COALESCE(f.`Total incl`, 0) > 0 AND f.`Date invoice` IS NOT NULL", cn);
        await using var r2 = await cmd.ExecuteReaderAsync();
        while (await r2.ReadAsync())
            lijst.Add(new OpenFactuur(r2.GetInt32(0), r2.GetString(1), r2.GetInt32(2), r2.GetString(3), r2.GetString(4),
                DateOnly.FromDateTime(r2.GetDateTime(5)), Convert.ToDecimal(r2.GetValue(6))));
        return lijst;
    }

    public async Task<List<Ontvangst>> GetOntvangstenAsync(DateOnly vanaf)
    {
        var lijst = new List<Ontvangst>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT b.`BankTransacties_ID`, bv.`id_supplier`, COALESCE(b.`Verwerkingsdatum`, b.`Datum`), b.`Bedrag`, " +
            "       COALESCE(NULLIF(b.`NaamTegenpartij`, ''), b.`Tenaamstelling`, ''), COALESCE(b.`Omschrijving`, '') " +
            "  FROM `banktransacties` b " + BvRekening +
            "   AND REPLACE(bv.`IBAN`, ' ', '') = REPLACE(b.`Rekening`, ' ', '') " +
            " WHERE b.`Bedrag` > 0 AND b.`Datum` >= @vanaf " +
            "   AND NOT EXISTS (SELECT 1 FROM `banktransacties_facturen_crossref` x " +
            "                    WHERE x.`Banktransacties-id` = b.`BankTransacties_ID`)", cn);
        cmd.Parameters.AddWithValue("@vanaf", vanaf.ToDateTime(TimeOnly.MinValue));
        await using var r2 = await cmd.ExecuteReaderAsync();
        while (await r2.ReadAsync())
            lijst.Add(new Ontvangst(r2.GetInt32(0), r2.GetInt32(1), DateOnly.FromDateTime(r2.GetDateTime(2)),
                Convert.ToDecimal(r2.GetValue(3)), r2.GetString(4), r2.GetString(5)));
        return lijst;
    }

    public async Task<int> VerwerkBetalingenAsync(IEnumerable<BetalingVoorstel> voorstellen, int? doorWerknemerId)
    {
        var aantal = 0;
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        foreach (var v in voorstellen.Where(v => v.KanVerwerken))
        {
            var betaaldOp = v.Ontvangsten.Max(o => o.Datum).ToDateTime(TimeOnly.MinValue);
            foreach (var f in v.Facturen)
            {
                // Alleen als hij nog open staat: iemand kan hem intussen met de hand betaald hebben gezet.
                await using (var cmd = new MySqlCommand(
                    $"UPDATE `Invoices` SET `Betaald` = 1, `DatumBetaald` = @op, `BetaaldDoor` = @door " +
                    $" WHERE `{Sleutel}` = @f AND COALESCE(`Betaald`, 0) = 0", cn, tx))
                {
                    cmd.Parameters.AddWithValue("@op", betaaldOp);
                    cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@f", f.Id);
                    aantal += await cmd.ExecuteNonQueryAsync();
                }

                foreach (var o in v.Ontvangsten)
                {
                    await using var cmd = new MySqlCommand(
                        "INSERT INTO `banktransacties_facturen_crossref` (`Banktransacties-id`, `Facturenuitgaand-id`) " +
                        "VALUES (@b, @f)", cn, tx);
                    cmd.Parameters.AddWithValue("@b", o.Id);
                    cmd.Parameters.AddWithValue("@f", f.Id);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        await tx.CommitAsync();
        return aantal;
    }

    private static string Kort(string tekst, int max)
    {
        var t = tekst.Trim();
        return t.Length <= max ? t : t[..max];
    }

    /// <summary>
    /// Regels bijwerken in plaats van wissen en opnieuw schrijven (wat Access deed):
    /// zo blijven de kolommen die dit venster niet kent, zoals de urenselectie, staan.
    /// </summary>
    private static async Task BewaarRegelsAsync(MySqlConnection cn, MySqlTransaction tx, VerkoopFactuur f)
    {
        var houden = f.Regels.Where(r => r.Id > 0).Select(r => r.Id).ToList();
        await using (var cmd = new MySqlCommand(
            "DELETE FROM `InvoiceHours` WHERE `InvoiceID` = @f" +
            (houden.Count > 0 ? $" AND `InvoiceHoursID` NOT IN ({string.Join(",", houden)})" : ""), cn, tx))
        {
            cmd.Parameters.AddWithValue("@f", f.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        var volgorde = 1;
        foreach (var r in f.Regels)
        {
            r.Volgorde = volgorde++;
            r.Totaal = Math.Round(r.Bereken(), 2);

            var h = r.Herkomst;
            var sql = r.Id > 0
                ? "UPDATE `InvoiceHours` SET `Order` = @order, `ItemDescription` = @oms, `Quantity` = @aantal, " +
                  "       `StuksPrijs` = @prijs, `Korting` = @korting, `Totaal` = @totaal " +
                  " WHERE `InvoiceHoursID` = @id AND `InvoiceID` = @f"
                : h is null
                // Zoals de artikelimport in Access: geen werkorder, project 0.
                ? "INSERT INTO `InvoiceHours` (`InvoiceID`, `Order`, `ItemDescription`, `Quantity`, `StuksPrijs`, " +
                  "       `Korting`, `Totaal`, `Kilometers`, `Project-ID`, `ProjectDescription`, `Workorder-ID`, " +
                  "       `KlantReferentie`, `Naam`) " +
                  "VALUES (@f, @order, @oms, @aantal, @prijs, @korting, @totaal, 0, 0, '', 0, '', '')"
                // Zoals de urenimport in Access, inclusief de selectie waarmee de uren gereserveerd worden.
                : "INSERT INTO `InvoiceHours` (`InvoiceID`, `Order`, `ItemDescription`, `Quantity`, `StuksPrijs`, " +
                  "       `Korting`, `Totaal`, `WO`, `Workorder-ID`, `WorkorderDescription`, `Project-ID`, " +
                  "       `ProjectDescription`, `Uren`, `Kilometers`, `Uurtype`, `KlantReferentie`, `Naam`, " +
                  "       `WerknemerID`, `SelectieVan`, `SelectieTot`, `SelectieOrderId`, `SelectieProjectID`, " +
                  "       `SelectieUurtypeID`) " +
                  "VALUES (@f, @order, @oms, @aantal, @prijs, @korting, @totaal, @wo, @wb, @wbOms, @proj, " +
                  "       @projOms, @uren, @km, @uurtype, '', @naam, @wn, @van, @tot, @wb, @proj, @soort)";

            await using var cmd = new MySqlCommand(sql, cn, tx);
            if (r.Id == 0 && h is not null)
            {
                cmd.Parameters.AddWithValue("@wo", Kort(r.Werkorder, 200));
                cmd.Parameters.AddWithValue("@wb", h.WerkbonId);
                cmd.Parameters.AddWithValue("@wbOms", Kort(r.WerkorderOmschrijving, 200));
                cmd.Parameters.AddWithValue("@proj", h.ProjectId);
                cmd.Parameters.AddWithValue("@projOms", Kort(h.ProjectOmschrijving, 200));
                cmd.Parameters.AddWithValue("@uren", (object?)r.Uren ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@km", (object?)r.Kilometers ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@uurtype", Kort(r.Uurtype, 20));
                cmd.Parameters.AddWithValue("@naam", Kort(r.Naam, 45));
                cmd.Parameters.AddWithValue("@wn", h.WerknemerId);
                cmd.Parameters.AddWithValue("@van", h.Van.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@tot", h.Tot.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@soort", h.TariefsoortId);
            }
            cmd.Parameters.AddWithValue("@f", f.Id);
            cmd.Parameters.AddWithValue("@id", r.Id);
            cmd.Parameters.AddWithValue("@order", r.Volgorde);
            cmd.Parameters.AddWithValue("@oms", Kort(r.Omschrijving, 200));
            cmd.Parameters.AddWithValue("@aantal", (object?)r.Aantal ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@prijs", (object?)r.Stuksprijs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@korting", r.Korting ?? 0);
            cmd.Parameters.AddWithValue("@totaal", r.Totaal);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Zoals Access na het opslaan: eerst alle uren van deze factuur vrijgeven, dan de
    /// uren reserveren die binnen de selectie van een urenregel vallen. Uren-vlaggen als
    /// -1, zoals Access ze altijd schreef.
    /// </summary>
    private static async Task ReserveerUrenAsync(MySqlConnection cn, MySqlTransaction tx, int factuurId)
    {
        await using (var cmd = new MySqlCommand(
            "UPDATE `Uren` SET `ReservedInvoice` = 0, `ReservedInvoiceDate` = NULL, `ReservedOnInvoice` = NULL " +
            " WHERE `ReservedOnInvoice` = @f", cn, tx))
        {
            cmd.Parameters.AddWithValue("@f", factuurId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new MySqlCommand(
            "UPDATE `Uren` u JOIN `InvoiceHours` h " +
            "    ON h.`SelectieUurtypeID` = u.`Tariefsoort-id` AND h.`SelectieOrderId` = u.`werkorder` " +
            "   SET u.`ReservedInvoice` = -1, u.`ReservedInvoiceDate` = NOW(), u.`ReservedOnInvoice` = @f " +
            " WHERE h.`InvoiceID` = @f AND u.`StartDatum` >= h.`SelectieVan` AND u.`StartDatum` <= h.`SelectieTot`",
            cn, tx))
        {
            cmd.Parameters.AddWithValue("@f", factuurId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<List<VerkoopFactuurBetaling>> GetBetalingenAsync(int factuurId)
    {
        var lijst = new List<VerkoopFactuurBetaling>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT b.`BankTransacties_ID`, COALESCE(b.`Verwerkingsdatum`, b.`Datum`), " +
            "       COALESCE(b.`Bedrag`, 0), COALESCE(NULLIF(b.`NaamTegenpartij`, ''), b.`Tenaamstelling`, ''), " +
            "       COALESCE(b.`Omschrijving`, '') " +
            "  FROM `banktransacties_facturen_crossref` x " +
            "  JOIN `banktransacties` b ON b.`BankTransacties_ID` = x.`Banktransacties-id` " +
            " WHERE x.`Facturenuitgaand-id` = @id ORDER BY 2", cn);
        cmd.Parameters.AddWithValue("@id", factuurId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new VerkoopFactuurBetaling(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : r.GetDateTime(1),
                Convert.ToDecimal(r.GetValue(2)),
                r.GetString(3),
                r.GetString(4)));
        return lijst;
    }

    // ============================================================
    //  Vinkjes
    // ============================================================
    public async Task ZetVlagAsync(int id, VerkoopFactuurVlag vlag, bool aan, int? doorWerknemerId)
    {
        // Per vlag: de vlag zelf, wie het deed en wanneer. Uitvinken wist die twee
        // weer, anders blijft er een naam staan bij iets wat niet meer waar is.
        var (kolom, wie, wanneer, wanneerIsDatum) = vlag switch
        {
            VerkoopFactuurVlag.Betaald => ("Betaald", "BetaaldDoor", "DatumBetaald", true),
            VerkoopFactuurVlag.Basecone => ("VerzondenBasecone", "VerzondenBaseconeDoor", "VerzondenBaseconeOp", false),
            _ => ("On-Inbaar", "", "", false),
        };

        var zet = new List<string> { $"`{kolom}` = @aan" };
        if (wie.Length > 0) zet.Add($"`{wie}` = @wie");
        if (wanneer.Length > 0) zet.Add($"`{wanneer}` = @wanneer");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"UPDATE `Invoices` SET {string.Join(", ", zet)} WHERE `{Sleutel}` = @id", cn);

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
    //  Wissen
    // ============================================================
    public async Task<string?> WisAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        // Opnieuw kijken in de database, niet op wat het scherm dacht.
        await using (var cmd = new MySqlCommand(
            $"SELECT COALESCE(`SentToClient`, 0), " +
            $"       (SELECT COUNT(*) FROM `banktransacties_facturen_crossref` x WHERE x.`Facturenuitgaand-id` = @id) " +
            $"  FROM `Invoices` WHERE `{Sleutel}` = @id FOR UPDATE", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return "Deze factuur bestaat niet meer.";
            if (Convert.ToInt64(r.GetValue(0)) != 0) return "Een verstuurde factuur is niet te wissen.";
            if (Convert.ToInt64(r.GetValue(1)) != 0) return "Aan deze factuur hangt een banktransactie.";
        }

        // De uren die voor deze factuur gereserveerd waren komen weer vrij (zoals Access).
        await using (var cmd = new MySqlCommand(
            "UPDATE `Uren` SET `ReservedInvoice` = 0, `ReservedInvoiceDate` = NULL, `ReservedOnInvoice` = NULL " +
            " WHERE `ReservedOnInvoice` = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // De regels in InvoiceHours gaan mee via de foreign key (ON DELETE CASCADE).
        await using (var cmd = new MySqlCommand($"DELETE FROM `Invoices` WHERE `{Sleutel}` = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return null;
    }
}
