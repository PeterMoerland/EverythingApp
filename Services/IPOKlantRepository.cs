using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Opdrachten (PO's) van klanten met hun betaaltermijnen: tabellen PO_Inkomend en Payments, het
/// Access-formulier POInkomend (met POInkomend toevoegen/aanpassen en PO_Payments).
/// </summary>
public interface IPOKlantRepository
{
    bool Beschikbaar { get; }

    Task<List<POKlant>> ZoekAsync(POFilter filter);

    /// <summary>Eén PO met termijnen (en het factuurnummer van gekoppelde facturen).</summary>
    Task<POKlant?> GetAsync(int id);

    /// <summary>Nieuw (Id 0) of bijwerken, met de termijnen; geeft het id terug.</summary>
    Task<int> BewaarAsync(POKlant po, int? doorWerknemerId);

    /// <summary>Wist de PO met zijn termijnen. Weigert als er een factuur aan een termijn hangt.</summary>
    Task WisAsync(int id);

    /// <summary>Bestaat dit PO-nummer al bij deze klant (Access: POBestaatAl)?</summary>
    Task<bool> PoNummerBestaatAsync(int klantId, string poNummer, int negeerId);

    /// <summary>Termijnen met een verwachte factuurdatum die nog niet gefactureerd zijn (Payments_due).</summary>
    Task<List<POAgendaRegel>> AgendaAsync();

    /// <summary>Uitgaande facturen van deze klant om aan een termijn te koppelen; die rond het bedrag eerst.</summary>
    Task<List<POFactuurKeuze>> FacturenVanKlantAsync(int klantId, decimal bedrag);

    /// <summary>Koppelt een factuur aan een termijn en zet hem op gefactureerd (of ontkoppelt bij null).</summary>
    Task KoppelFactuurAsync(int termijnId, int? factuurId, int? doorWerknemerId);

    Task<List<Administratie>> BvsAsync();

    /// <summary>Heeft PO_Inkomend de kolom Project-id al (PoProject_MariaDB.sql)?</summary>
    Task<bool> HeeftProjectKolomAsync();
}

public class GeenPOKlantRepository : IPOKlantRepository
{
    public bool Beschikbaar => false;
    public Task<List<POKlant>> ZoekAsync(POFilter filter) => Task.FromResult(new List<POKlant>());
    public Task<POKlant?> GetAsync(int id) => Task.FromResult<POKlant?>(null);
    public Task<int> BewaarAsync(POKlant po, int? doorWerknemerId) => Task.FromResult(0);
    public Task WisAsync(int id) => Task.CompletedTask;
    public Task<bool> PoNummerBestaatAsync(int klantId, string poNummer, int negeerId) => Task.FromResult(false);
    public Task<List<POAgendaRegel>> AgendaAsync() => Task.FromResult(new List<POAgendaRegel>());
    public Task<List<POFactuurKeuze>> FacturenVanKlantAsync(int klantId, decimal bedrag) => Task.FromResult(new List<POFactuurKeuze>());
    public Task KoppelFactuurAsync(int termijnId, int? factuurId, int? doorWerknemerId) => Task.CompletedTask;
    public Task<List<Administratie>> BvsAsync() => Task.FromResult(new List<Administratie>());
    public Task<bool> HeeftProjectKolomAsync() => Task.FromResult(false);
}

public class MySqlPOKlantRepository : IPOKlantRepository
{
    private readonly string _cs;

    public MySqlPOKlantRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static int? I(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static decimal M(MySqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToDecimal(r.GetValue(i));
    private static DateOnly? D(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : DateOnly.FromDateTime(Convert.ToDateTime(r.GetValue(i)));
    private static bool B(MySqlDataReader r, int i) => !r.IsDBNull(i) && Convert.ToInt64(r.GetValue(i)) != 0;
    private static string Kort(string s, int max) => s.Length <= max ? s : s[..max];

    private const string BasisMetProject =
        ", p.`Project-id`, (SELECT CONCAT_WS(' · ', CAST(pr_.`Projectnumber` AS CHAR), NULLIF(pr_.`Projectdescription`, '')) " +
        "                   FROM `Projects` pr_ WHERE pr_.`Project-Id` = p.`Project-id`) ";

    private async Task<string> BasisAsync()
        => await HeeftProjectKolomAsync() ? Basis.Replace(" FROM `PO_Inkomend` p ", BasisMetProject + " FROM `PO_Inkomend` p ") : Basis;

    private bool? _projectKolom;

    public async Task<bool> HeeftProjectKolomAsync()
    {
        if (_projectKolom == true) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "   AND TABLE_NAME = 'PO_Inkomend' AND COLUMN_NAME = 'Project-id'", cn);
            _projectKolom = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { _projectKolom = false; }
        return _projectKolom.Value;
    }

    private const string Basis =
        "SELECT p.`PO_inkomendID`, p.`PO_Date`, p.`Werkmaatschappij`, COALESCE(bv.`Afkorting`, ''), " +
        "       p.`Customer-id`, COALESCE(k.`name`, ''), p.`Contactpersoon`, COALESCE(c.`Naam`, ''), " +
        "       COALESCE(p.`PO_Klant`, ''), COALESCE(p.`Bedrag`, 0), COALESCE(p.`Description`, ''), " +
        "       COALESCE(p.`Opmerking`, ''), p.`DatumInvoer`, COALESCE(w.`NaamVoluit`, ''), " +
        "       (SELECT COUNT(*) FROM `Payments` t WHERE t.`POinkomendID` = p.`PO_inkomendID`), " +
        "       (SELECT COALESCE(SUM(t.`Amount`), 0) FROM `Payments` t " +
        "         WHERE t.`POinkomendID` = p.`PO_inkomendID` AND COALESCE(t.`Invoiced`, 0) <> 0)" +
        " FROM `PO_Inkomend` p " +
        "  LEFT JOIN `Relations` k ON k.`id_supplier` = p.`Customer-id` " +
        "  LEFT JOIN `Relations` bv ON bv.`id_supplier` = p.`Werkmaatschappij` " +
        "  LEFT JOIN `Contacts` c ON c.`ID_contact` = p.`Contactpersoon` " +
        "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = p.`CreatedBy` ";

    private static POKlant LeesPo(MySqlDataReader r)
    {
        var po = LeesPoBasis(r);
        if (r.FieldCount > 16)
        {
            po.ProjectId = I(r, 16);
            po.Project = S(r, 17);
        }
        return po;
    }

    private static POKlant LeesPoBasis(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Datum = D(r, 1),
        BvId = I(r, 2),
        Bv = S(r, 3),
        KlantId = I(r, 4),
        Klant = S(r, 5),
        ContactId = I(r, 6),
        Contact = S(r, 7),
        PoNummer = S(r, 8),
        Bedrag = M(r, 9),
        Omschrijving = S(r, 10),
        Opmerking = S(r, 11),
        Ingevoerd = r.IsDBNull(12) ? null : Convert.ToDateTime(r.GetValue(12)),
        IngevoerdDoor = S(r, 13),
        AantalTermijnen = Convert.ToInt32(r.GetValue(14)),
        Gefactureerd = M(r, 15),
    };

    public async Task<List<POKlant>> ZoekAsync(POFilter f)
    {
        var lijst = new List<POKlant>();
        var waar = new List<string>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand { Connection = cn };

        if (f.BvId is int bv) { waar.Add("p.`Werkmaatschappij` = @bv"); cmd.Parameters.AddWithValue("@bv", bv); }
        if (f.Van is DateOnly van) { waar.Add("p.`PO_Date` >= @van"); cmd.Parameters.AddWithValue("@van", van.ToDateTime(TimeOnly.MinValue)); }
        if (f.Tot is DateOnly tot) { waar.Add("p.`PO_Date` < @tot"); cmd.Parameters.AddWithValue("@tot", tot.AddDays(1).ToDateTime(TimeOnly.MinValue)); }
        if (f.Klant.Trim().Length > 0) { waar.Add("k.`name` LIKE @klant"); cmd.Parameters.AddWithValue("@klant", "%" + f.Klant.Trim() + "%"); }
        if (f.ProjectId is int proj)
        {
            if (!await HeeftProjectKolomAsync()) return new();   // zonder kolom hangt er geen PO aan een project
            waar.Add("p.`Project-id` = @proj"); cmd.Parameters.AddWithValue("@proj", proj);
        }
        if (f.Zoek.Trim().Length > 0)
        {
            // Access: PO-nummer, klant, omschrijving en contactpersoon in één zoekveld
            waar.Add("CONCAT_WS(' ', p.`PO_Klant`, k.`name`, p.`Description`, c.`Naam`, p.`Opmerking`) LIKE @zoek");
            cmd.Parameters.AddWithValue("@zoek", "%" + f.Zoek.Trim() + "%");
        }

        cmd.CommandText = await BasisAsync() + (waar.Count > 0 ? " WHERE " + string.Join(" AND ", waar) : "") +
                          " ORDER BY p.`PO_Date` DESC, p.`PO_inkomendID` DESC";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(LeesPo(r));

        return f.AlleenOpen ? lijst.Where(p => p.Gefactureerd < p.Bedrag - 0.005m).ToList() : lijst;
    }

    public async Task<POKlant?> GetAsync(int id)
    {
        await using var cn = await OpenAsync();
        POKlant? po;
        await using (var cmd = new MySqlCommand(await BasisAsync() + " WHERE p.`PO_inkomendID` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            po = await r.ReadAsync() ? LeesPo(r) : null;
        }
        if (po is null) return null;

        await using (var cmd = new MySqlCommand(
            "SELECT t.`Payment-id`, COALESCE(t.`Term`, ''), COALESCE(t.`Amount`, 0), t.`EstimateInvoiceDate`, " +
            "       t.`Invoiced`, t.`Invoiced at`, t.`Checked_invoiced`, t.`To be invoiced`, COALESCE(t.`Remark`, ''), " +
            "       t.`InvoiceID`, COALESCE(i.`InvoiceNumber`, '') " +
            "  FROM `Payments` t LEFT JOIN `Invoices` i ON i.`Invoice-id` = t.`InvoiceID` " +
            " WHERE t.`POinkomendID` = @id ORDER BY t.`Payment-id`", cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                po.Termijnen.Add(new POTermijn
                {
                    Id = r.GetInt32(0),
                    Omschrijving = S(r, 1),
                    Bedrag = Math.Round(M(r, 2), 2),
                    VerwachtOp = D(r, 3),
                    Gefactureerd = B(r, 4),
                    GefactureerdOp = D(r, 5),
                    GecontroleerdDoor = I(r, 6),
                    TeFactureren = B(r, 7),
                    Opmerking = S(r, 8),
                    FactuurId = I(r, 9),
                    Factuurnummer = S(r, 10),
                });
        }
        return po;
    }

    public async Task<int> BewaarAsync(POKlant po, int? door)
    {
        var metProject = await HeeftProjectKolomAsync();
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        var nieuw = po.Id == 0;
        // het bedrag van voor deze wijziging: de budgetregel volgt de PO alleen als hij nog gelijk was aan de PO
        decimal? oudBedrag = null;
        int? oudProject = null;
        if (!nieuw)
        {
            await using var oud = new MySqlCommand(
                "SELECT `Bedrag`, " + (metProject ? "`Project-id`" : "NULL") + " FROM `PO_Inkomend` WHERE `PO_inkomendID` = @id", cn, tx);
            oud.Parameters.AddWithValue("@id", po.Id);
            await using var r = await oud.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                oudBedrag = r.IsDBNull(0) ? null : Math.Round(Convert.ToDecimal(r.GetValue(0)), 2);
                oudProject = r.IsDBNull(1) ? null : Convert.ToInt32(r.GetValue(1));
            }
        }
        await using (var cmd = new MySqlCommand(nieuw
            ? "INSERT INTO `PO_Inkomend` (`PO_Date`, `DatumInvoer`, `CreatedBy`, `Customer-id`, `Bedrag`, `Contactpersoon`, " +
              "  `Opmerking`, `Werkmaatschappij`, `PO_Klant`, `Description`) " +
              "VALUES (@datum, @nu, @door, @klant, @bedrag, @contact, @opm, @bv, @po, @oms); SELECT LAST_INSERT_ID();"
            : "UPDATE `PO_Inkomend` SET `PO_Date` = @datum, `Customer-id` = @klant, `Bedrag` = @bedrag, " +
              "  `Contactpersoon` = @contact, `Opmerking` = @opm, `Werkmaatschappij` = @bv, `PO_Klant` = @po, " +
              "  `Description` = @oms WHERE `PO_inkomendID` = @id; SELECT @id;", cn, tx))
        {
            cmd.Parameters.AddWithValue("@datum", (object?)po.Datum?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@nu", DateTime.Now);
            cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@klant", (object?)po.KlantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bedrag", Math.Round(po.Bedrag, 2));
            cmd.Parameters.AddWithValue("@contact", (object?)po.ContactId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@opm", Kort(po.Opmerking.Trim(), 400));
            cmd.Parameters.AddWithValue("@bv", (object?)po.BvId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@po", Kort(po.PoNummer.Trim(), 100));
            cmd.Parameters.AddWithValue("@oms", Kort(po.Omschrijving.Trim(), 100));
            cmd.Parameters.AddWithValue("@id", po.Id);
            po.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        if (metProject)
        {
            await using (var cmd = new MySqlCommand("UPDATE `PO_Inkomend` SET `Project-id` = @pr WHERE `PO_inkomendID` = @id", cn, tx))
            {
                cmd.Parameters.AddWithValue("@pr", (object?)po.ProjectId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", po.Id);
                await cmd.ExecuteNonQueryAsync();
            }
            if (po.ProjectId is int projectId)
                await BudgetVolgtPoAsync(cn, tx, po, projectId, oudBedrag, nieuweKoppeling: nieuw || oudProject != projectId);
        }

        // Termijnen: Access gooide ze weg en zette ze opnieuw neer; hier bijwerken, toevoegen en
        // alleen wissen wat er niet meer staat, zodat de ids (en gekoppelde facturen) blijven.
        var blijven = po.Termijnen.Where(t => t.Id > 0).Select(t => t.Id).ToList();
        await using (var cmd = new MySqlCommand(
            "DELETE FROM `Payments` WHERE `POinkomendID` = @po AND `InvoiceID` IS NULL" +
            (blijven.Count > 0 ? $" AND `Payment-id` NOT IN ({string.Join(",", blijven)})" : ""), cn, tx))
        {
            cmd.Parameters.AddWithValue("@po", po.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var t in po.Termijnen)
        {
            await using var cmd = new MySqlCommand(t.Id == 0
                ? "INSERT INTO `Payments` (`POinkomendID`, `Term`, `Amount`, `To be invoiced`, `Invoiced`, `Invoiced at`, " +
                  "  `Remark`, `Checked_invoiced`, `EstimateInvoiceDate`, `InvoiceID`) " +
                  "VALUES (@po, @term, @bedrag, @tef, @gef, @gefop, @opm, @contr, @verwacht, @fact); SELECT LAST_INSERT_ID();"
                : "UPDATE `Payments` SET `Term` = @term, `Amount` = @bedrag, `To be invoiced` = @tef, `Invoiced` = @gef, " +
                  "  `Invoiced at` = @gefop, `Remark` = @opm, `Checked_invoiced` = @contr, `EstimateInvoiceDate` = @verwacht, " +
                  "  `InvoiceID` = @fact WHERE `Payment-id` = @id AND `POinkomendID` = @po; SELECT @id;", cn, tx);
            cmd.Parameters.AddWithValue("@po", po.Id);
            cmd.Parameters.AddWithValue("@term", Kort(t.Omschrijving.Trim(), 50));
            cmd.Parameters.AddWithValue("@bedrag", (double)Math.Round(t.Bedrag, 2));
            cmd.Parameters.AddWithValue("@tef", t.TeFactureren ? -1 : 0);
            cmd.Parameters.AddWithValue("@gef", t.Gefactureerd ? -1 : 0);
            cmd.Parameters.AddWithValue("@gefop", (object?)t.GefactureerdOp?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@opm", t.Opmerking.Trim());
            cmd.Parameters.AddWithValue("@contr", (object?)t.GecontroleerdDoor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@verwacht", (object?)t.VerwachtOp?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fact", (object?)t.FactuurId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", t.Id);
            t.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        await tx.CommitAsync();
        return po.Id;
    }

    public async Task WisAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using (var kijk = new MySqlCommand(
            "SELECT COUNT(*) FROM `Payments` WHERE `POinkomendID` = @id AND `InvoiceID` IS NOT NULL", cn))
        {
            kijk.Parameters.AddWithValue("@id", id);
            if (Convert.ToInt64(await kijk.ExecuteScalarAsync()) > 0)
                throw new InvalidOperationException("Er hangt een factuur aan een termijn van deze PO; ontkoppel die eerst.");
        }

        await using var tx = await cn.BeginTransactionAsync();
        foreach (var sql in new[]
                 {
                     "DELETE FROM `Payments` WHERE `POinkomendID` = @id",
                     "DELETE FROM `PO_Offertes` WHERE `POinkomendID` = @id",
                     // het budget dat bij deze PO hoort gaat mee (Projecten ▸ Budget); andere budgetregels blijven
                     "DELETE FROM `BudgetProject` WHERE `POklant` = @id",
                     "DELETE FROM `PO_Inkomend` WHERE `PO_inkomendID` = @id",
                 })
        {
            await using var cmd = new MySqlCommand(sql, cn, tx);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    /// <summary>
    /// De budgetregel van deze PO (BudgetProject.POklant): bestaat hij niet, dan komt hij erbij met het PO-bedrag (het
    /// projectbudget gaat omhoog). Bestaat hij al, dan verhuist hij mee naar het project van de PO, en volgt het bedrag
    /// de PO zolang het nog gelijk was aan het oude PO-bedrag (een met de hand aangepast budget blijft staan). Een
    /// gewiste budgetregel komt alleen terug bij een nieuwe PO of als het project van de PO verandert.
    /// </summary>
    private static async Task BudgetVolgtPoAsync(MySqlConnection cn, MySqlTransaction tx, POKlant po, int projectId, decimal? oudBedrag,
                                                 bool nieuweKoppeling)
    {
        var regels = new List<(int Id, decimal Bedrag)>();
        await using (var cmd = new MySqlCommand(
            "SELECT `BudgetProjectID`, COALESCE(`Bedrag`, 0) FROM `BudgetProject` WHERE `POklant` = @po", cn, tx))
        {
            cmd.Parameters.AddWithValue("@po", po.Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) regels.Add((r.GetInt32(0), Math.Round(Convert.ToDecimal(r.GetValue(1)), 2)));
        }
        var bedrag = Math.Round(po.Bedrag, 2);
        if (regels.Count == 0)
        {
            // alleen bij een nieuwe PO of een ander project: is de regel op de tab Budget gewist, dan komt hij niet terug
            if (!nieuweKoppeling) return;
            var oms = "PO " + po.PoNummer.Trim() + (po.Omschrijving.Trim().Length > 0 ? " · " + po.Omschrijving.Trim() : "");
            await using var cmd = new MySqlCommand(
                "INSERT INTO `BudgetProject` (`ProjectID`, `Omschrijving`, `Bedrag`, `POklant`, `Opmerking`) " +
                "VALUES (@pr, @oms, @bedrag, @po, @opm)", cn, tx);
            cmd.Parameters.AddWithValue("@pr", projectId);
            cmd.Parameters.AddWithValue("@oms", Kort(oms, 100));
            cmd.Parameters.AddWithValue("@bedrag", (double)bedrag);
            cmd.Parameters.AddWithValue("@po", po.Id);
            cmd.Parameters.AddWithValue("@opm", "Automatisch bij het invoeren van de PO");
            await cmd.ExecuteNonQueryAsync();
            return;
        }
        foreach (var (id, huidig) in regels)
        {
            var volgBedrag = regels.Count == 1 && (oudBedrag is null || huidig == oudBedrag || huidig == 0);
            await using var cmd = new MySqlCommand(
                "UPDATE `BudgetProject` SET `ProjectID` = @pr" + (volgBedrag ? ", `Bedrag` = @bedrag" : "") +
                " WHERE `BudgetProjectID` = @id", cn, tx);
            cmd.Parameters.AddWithValue("@pr", projectId);
            cmd.Parameters.AddWithValue("@bedrag", (double)bedrag);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<bool> PoNummerBestaatAsync(int klantId, string poNummer, int negeerId)
    {
        if (poNummer.Trim().Length == 0) return false;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM `PO_Inkomend` WHERE `Customer-id` = @k AND TRIM(`PO_Klant`) = @po AND `PO_inkomendID` <> @id", cn);
        cmd.Parameters.AddWithValue("@k", klantId);
        cmd.Parameters.AddWithValue("@po", poNummer.Trim());
        cmd.Parameters.AddWithValue("@id", negeerId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    public async Task<List<POAgendaRegel>> AgendaAsync()
    {
        var lijst = new List<POAgendaRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT p.`PO_inkomendID`, t.`Payment-id`, COALESCE(k.`name`, ''), COALESCE(bv.`Afkorting`, ''), " +
            "       COALESCE(p.`PO_Klant`, ''), COALESCE(p.`Description`, ''), COALESCE(t.`Term`, ''), " +
            "       COALESCE(t.`Amount`, 0), t.`EstimateInvoiceDate`, COALESCE(t.`Remark`, '') " +
            "  FROM `Payments` t JOIN `PO_Inkomend` p ON p.`PO_inkomendID` = t.`POinkomendID` " +
            "  LEFT JOIN `Relations` k ON k.`id_supplier` = p.`Customer-id` " +
            "  LEFT JOIN `Relations` bv ON bv.`id_supplier` = p.`Werkmaatschappij` " +
            " WHERE COALESCE(t.`Invoiced`, 0) = 0 AND t.`EstimateInvoiceDate` IS NOT NULL " +
            " ORDER BY t.`EstimateInvoiceDate`, k.`name`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new POAgendaRegel(r.GetInt32(0), r.GetInt32(1), S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6),
                                        Math.Round(M(r, 7), 2), D(r, 8), S(r, 9)));
        return lijst;
    }

    public async Task<List<POFactuurKeuze>> FacturenVanKlantAsync(int klantId, decimal bedrag)
    {
        var lijst = new List<POFactuurKeuze>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT i.`Invoice-id`, COALESCE(i.`InvoiceNumber`, ''), i.`Date invoice`, COALESCE(i.`Total ex`, 0), " +
            "       COALESCE(i.`Description`, ''), COALESCE(i.`PO_Klant`, '') " +
            "  FROM `Invoices` i WHERE i.`Customer-id` = @k " +
            " ORDER BY ABS(COALESCE(i.`Total ex`, 0) - @b) < 1 DESC, i.`Date invoice` DESC LIMIT 40", cn);
        cmd.Parameters.AddWithValue("@k", klantId);
        cmd.Parameters.AddWithValue("@b", bedrag);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new POFactuurKeuze(r.GetInt32(0), S(r, 1), D(r, 2), Math.Round(M(r, 3), 2), S(r, 4), S(r, 5)));
        return lijst;
    }

    public async Task KoppelFactuurAsync(int termijnId, int? factuurId, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(factuurId is null
            ? "UPDATE `Payments` SET `InvoiceID` = NULL WHERE `Payment-id` = @t"
            : "UPDATE `Payments` SET `InvoiceID` = @f, `Invoiced` = -1, " +
              "       `Invoiced at` = COALESCE((SELECT `Date invoice` FROM `Invoices` WHERE `Invoice-id` = @f), CURDATE()), " +
              "       `Checked_invoiced` = @door WHERE `Payment-id` = @t", cn);
        cmd.Parameters.AddWithValue("@t", termijnId);
        cmd.Parameters.AddWithValue("@f", (object?)factuurId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Administratie>> BvsAsync()
    {
        var lijst = new List<Administratie>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `id_supplier`, COALESCE(`Afkorting`, ''), COALESCE(`name`, '') FROM `Relations` " +
            " WHERE COALESCE(`Werkmaatschappij`, 0) <> 0 ORDER BY `Afkorting`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new Administratie(r.GetInt32(0), S(r, 1), S(r, 2)));
        return lijst;
    }
}
