using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Uren op een uitgaande factuur zetten: het Access-formulier InvoiceImportHours.
/// Kiezen (klant/project/werkorders + periode), tarieven rekenen en de bedragen in Uren
/// bijwerken. Van die uren maakt het factuurvenster daarna de regels.
/// </summary>
public interface IUrenImportRepository
{
    bool Beschikbaar { get; }

    Task<List<ProjectKeuze>> GetProjectenAsync(int klantId);
    Task<List<WerkorderKeuzeUren>> GetWerkordersAsync(int projectId);

    /// <summary>Eerste en laatste dag met uren in deze selectie (de periode zelf telt niet mee).</summary>
    Task<(DateOnly Van, DateOnly Tot)?> PeriodeVanUrenAsync(UrenSelectie selectie);

    Task<List<UurRegel>> GetUrenAsync(UrenSelectie selectie);

    /// <summary>Rekent het tarief van elke regel (NieuwBedrag/Melding); schrijft nog niets.</summary>
    Task RekenAsync(int klantId, IReadOnlyList<UurRegel> uren);

    /// <summary>Zet de gerekende bedragen in Uren, alleen waar ze veranderd zijn. Geeft het aantal terug.</summary>
    Task<int> SchrijfTarievenAsync(IReadOnlyList<UurRegel> uren);
}

public class GeenUrenImportRepository : IUrenImportRepository
{
    public bool Beschikbaar => false;
    public Task<List<ProjectKeuze>> GetProjectenAsync(int klantId) => Task.FromResult(new List<ProjectKeuze>());
    public Task<List<WerkorderKeuzeUren>> GetWerkordersAsync(int projectId)
        => Task.FromResult(new List<WerkorderKeuzeUren>());
    public Task<(DateOnly Van, DateOnly Tot)?> PeriodeVanUrenAsync(UrenSelectie selectie)
        => Task.FromResult<(DateOnly, DateOnly)?>(null);
    public Task<List<UurRegel>> GetUrenAsync(UrenSelectie selectie) => Task.FromResult(new List<UurRegel>());
    public Task RekenAsync(int klantId, IReadOnlyList<UurRegel> uren) => Task.CompletedTask;
    public Task<int> SchrijfTarievenAsync(IReadOnlyList<UurRegel> uren) => Task.FromResult(0);
}

public class MySqlUrenImportRepository : IUrenImportRepository
{
    private readonly string _cs;

    /// <summary>Access viel terug op de afspraken van deze relatie als de klant er geen had.</summary>
    private const string StandaardRelatie = "MDE Automation B.V.";

    public MySqlUrenImportRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    // ============================================================
    //  Keuzelijsten
    // ============================================================
    public async Task<List<ProjectKeuze>> GetProjectenAsync(int klantId)
    {
        var lijst = new List<ProjectKeuze>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Project-Id`, COALESCE(CAST(`Projectnumber` AS CHAR), ''), COALESCE(`Projectdescription`, '') " +
            "  FROM `Projects` WHERE `Customer-id` = @k ORDER BY `Project-Id` DESC", cn);
        cmd.Parameters.AddWithValue("@k", klantId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new ProjectKeuze(r.GetInt32(0), r.GetString(1), r.GetString(2)));
        return lijst;
    }

    public async Task<List<WerkorderKeuzeUren>> GetWerkordersAsync(int projectId)
    {
        var lijst = new List<WerkorderKeuzeUren>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT w.`Werkbon-id`, LPAD(COALESCE(w.`Werkbonnummer`, 0), 2, '0'), " +
            "       COALESCE(w.`Werkbonomschrijving`, ''), LEFT(COALESCE(d.`Description`, ''), 3) " +
            "  FROM `Workorders` w LEFT JOIN `Doorbelasten` d ON d.`Doorbelasten` = w.`Doorbelasten` " +
            " WHERE w.`Project-id` = @p ORDER BY w.`Werkbonnummer`", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new WerkorderKeuzeUren(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return lijst;
    }

    // ============================================================
    //  De uren in de selectie
    // ============================================================

    /// <summary>
    /// Zoals Access: gekozen werkorders gaan voor, dan het project, dan alle projecten van
    /// de klant. Het project van de klant telt (Projects.Customer-id), niet wie de uren schreef.
    /// </summary>
    private static string Waar(UrenSelectie s, MySqlCommand cmd)
    {
        if (s.WerkbonIds.Count > 0)
            return $"u.`werkorder` IN ({string.Join(",", s.WerkbonIds.Select(id => id.ToString()))})";

        if (s.ProjectId is int p)
        {
            cmd.Parameters.AddWithValue("@project", p);
            return "p.`Project-Id` = @project";
        }

        cmd.Parameters.AddWithValue("@klant", s.KlantId);
        return "p.`Customer-id` = @klant";
    }

    private const string Van =
        "  FROM `Uren` u " +
        "  JOIN `Workorders` w ON w.`Werkbon-id` = u.`werkorder` " +
        "  JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` ";

    public async Task<(DateOnly Van, DateOnly Tot)?> PeriodeVanUrenAsync(UrenSelectie selectie)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand { Connection = cn };
        cmd.CommandText = "SELECT MIN(u.`StartDatum`), MAX(u.`StartDatum`) " + Van + " WHERE " + Waar(selectie, cmd);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync() || r.IsDBNull(0)) return null;
        return (DateOnly.FromDateTime(r.GetDateTime(0)), DateOnly.FromDateTime(r.GetDateTime(1)));
    }

    public async Task<List<UurRegel>> GetUrenAsync(UrenSelectie selectie)
    {
        var lijst = new List<UurRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand { Connection = cn };
        cmd.CommandText =
            "SELECT u.`Uren-id`, u.`StartDatum`, u.`Werknemer-id`, COALESCE(n.`NaamVoluit`, ''), " +
            "       u.`werkorder`, CONCAT(COALESCE(p.`Projectnumber`, ''), '-', LPAD(COALESCE(w.`Werkbonnummer`, 0), 2, '0')), " +
            "       COALESCE(w.`Werkbonomschrijving`, ''), p.`Project-Id`, COALESCE(p.`Projectdescription`, ''), " +
            "       u.`Tariefsoort-id`, COALESCE(t.`Tariefsoort`, ''), " +
            "       COALESCE(u.`Uren`, 0), COALESCE(u.`Kilometers`, 0), COALESCE(u.`Fixed_Amount`, 0), u.`FixedType-ID`, " +
            "       COALESCE(u.`Total_Amount`, 0), COALESCE(u.`Total_Cost`, 0), " +
            "       CASE WHEN COALESCE(u.`Invoiced`, 0) <> 0 THEN u.`InvoicedOnInvoice` END, " +
            "       CASE WHEN COALESCE(u.`ReservedInvoice`, 0) <> 0 THEN u.`ReservedOnInvoice` END, " +
            "       COALESCE(gi.`Invoicenumber`, ''), COALESCE(ri.`Invoicenumber`, '') " +
            Van +
            "  JOIN `Werknemers` n ON n.`Werknemer-id` = u.`Werknemer-id` " +
            "  JOIN `Tariefsoort` t ON t.`Tariefsoort-id` = u.`Tariefsoort-id` " +
            "  LEFT JOIN `Invoices` gi ON gi.`Invoice-id` = u.`InvoicedOnInvoice` " +
            "  LEFT JOIN `Invoices` ri ON ri.`Invoice-id` = u.`ReservedOnInvoice` " +
            " WHERE " + Waar(selectie, cmd) +
            "   AND u.`StartDatum` >= @van AND u.`StartDatum` <= @tot " +
            " ORDER BY u.`StartDatum`, n.`NaamVoluit`, u.`Uren-id`";
        cmd.Parameters.AddWithValue("@van", selectie.Van.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tot", selectie.Tot.ToDateTime(TimeOnly.MinValue));

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            int? I(int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
            decimal M(int i) => r.IsDBNull(i) ? 0 : Convert.ToDecimal(r.GetValue(i));
            lijst.Add(new UurRegel
            {
                UrenId = r.GetInt32(0),
                Datum = DateOnly.FromDateTime(r.GetDateTime(1)),
                WerknemerId = I(2) ?? 0,
                Werknemer = r.GetString(3),
                WerkbonId = I(4) ?? 0,
                Wo = r.GetString(5),
                WerkorderOmschrijving = r.GetString(6),
                ProjectId = I(7) ?? 0,
                ProjectOmschrijving = r.GetString(8),
                TariefsoortId = I(9) ?? 0,
                Tariefsoort = r.GetString(10),
                Uren = M(11),
                Kilometers = M(12),
                VastBedrag = M(13),
                VastType = I(14),
                Bedrag = M(15),
                Kostprijs = M(16),
                GefactureerdOp = I(17),
                GereserveerdOp = I(18),
                GefactureerdNr = r.GetString(19),
                GereserveerdNr = r.GetString(20),
            });
        }
        return lijst;
    }

    // ============================================================
    //  Tarieven rekenen: alles in een paar query's ophalen, dan per regel rekenen
    // ============================================================
    public async Task RekenAsync(int klantId, IReadOnlyList<UurRegel> uren)
    {
        if (uren.Count == 0) return;
        await using var cn = await OpenAsync();

        var werkbonnen = uren.Select(u => u.WerkbonId).Distinct().ToList();
        var werknemers = uren.Select(u => u.WerknemerId).Distinct().ToList();

        // Verkoopprijzen per werkorder en werknemer. WorkorderTeam koppelt via de tekstkolom
        // Workorder-Id (Werkbon-id is daar leeg), net als Access.
        var team = new Dictionary<(int, int), TeamTarief>();
        await using (var cmd = new MySqlCommand(
            "SELECT CAST(`Workorder-Id` AS SIGNED), `Werknemer-id`, COALESCE(`VKwerkuur`, 0), " +
            "       COALESCE(`VKreisuur`, 0), COALESCE(`VKkilometer`, 0) FROM `WorkorderTeam` " +
            $" WHERE `Workorder-Id` IN ({string.Join(",", werkbonnen.Select(w => $"'{w}'"))})", cn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                team.TryAdd((Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1))),
                    new TeamTarief(Convert.ToDecimal(r.GetValue(2)), Convert.ToDecimal(r.GetValue(3)),
                                   Convert.ToDecimal(r.GetValue(4))));
        }

        var klantAfspraken = await AfsprakenAsync(cn, "t.`Relation-id` = @k", klantId);
        var standaard = await AfsprakenAsync(cn, "r.`name` = @k", StandaardRelatie);

        var kostprijzen = new Dictionary<int, List<Kostprijstarief>>();
        await using (var cmd = new MySqlCommand(
            "SELECT `WerknemerID`, `Begindatum`, `Einddatum`, `KostprijsWerkuur`, `KostprijsReisuur`, `KostprijsKM` " +
            $"  FROM `WerknemersTarieven` WHERE `WerknemerID` IN ({string.Join(",", werknemers)})", cn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                if (r.IsDBNull(1) || r.IsDBNull(2)) continue;
                decimal? M(int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
                var id = Convert.ToInt32(r.GetValue(0));
                if (!kostprijzen.TryGetValue(id, out var lijst)) kostprijzen[id] = lijst = new();
                lijst.Add(new Kostprijstarief(DateOnly.FromDateTime(r.GetDateTime(1)),
                    DateOnly.FromDateTime(r.GetDateTime(2)), M(3), M(4), M(5)));
            }
        }

        var doorbelasting = new Dictionary<int, Doorbelasting>();
        await using (var cmd = new MySqlCommand(
            "SELECT `Werkbon-id`, `Doorbelast deel`, `Doorbelast_deel_expense_claims` FROM `Workorders` " +
            $" WHERE `Werkbon-id` IN ({string.Join(",", werkbonnen)})", cn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                doorbelasting[r.GetInt32(0)] = new Doorbelasting(
                    r.IsDBNull(1) ? 0 : Convert.ToDecimal(r.GetValue(1)),
                    r.IsDBNull(2) ? 0 : Convert.ToDecimal(r.GetValue(2)));
        }

        foreach (var u in uren)
        {
            Tariefregels.Reken(u,
                team.GetValueOrDefault((u.WerkbonId, u.WerknemerId)),
                klantAfspraken, standaard,
                kostprijzen.GetValueOrDefault(u.WerknemerId) ?? new List<Kostprijstarief>(),
                doorbelasting.GetValueOrDefault(u.WerkbonId));
        }
    }

    private static async Task<List<TariefafspraakPeriode>> AfsprakenAsync(MySqlConnection cn, string waar, object waarde)
    {
        var lijst = new List<TariefafspraakPeriode>();
        await using var cmd = new MySqlCommand(
            "SELECT t.`Geldigheid start`, t.`Geldigheid stop`, COALESCE(t.`BasisTarief`, 0), COALESCE(t.`Travel`, 0), " +
            "       COALESCE(t.`Kilometer`, 0), t.`Procent Overwerk Basis`, t.`Procent Overwerk Zon` " +
            "  FROM `Tariefafspraken` t JOIN `Relations` r ON r.`id_supplier` = t.`Relation-id` " +
            " WHERE " + waar, cn);
        cmd.Parameters.AddWithValue("@k", waarde);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            if (r.IsDBNull(0) || r.IsDBNull(1)) continue;
            decimal? M(int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
            lijst.Add(new TariefafspraakPeriode(DateOnly.FromDateTime(r.GetDateTime(0)), DateOnly.FromDateTime(r.GetDateTime(1)),
                Convert.ToDecimal(r.GetValue(2)), Convert.ToDecimal(r.GetValue(3)), Convert.ToDecimal(r.GetValue(4)),
                M(5), M(6)));
        }
        return lijst;
    }

    public async Task<int> SchrijfTarievenAsync(IReadOnlyList<UurRegel> uren)
    {
        var teSchrijven = uren.Where(u => u.NieuwBedrag is not null
            && (u.NieuwBedrag != u.Bedrag || u.NieuweKostprijs != u.Kostprijs)).ToList();
        if (teSchrijven.Count == 0) return 0;

        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        foreach (var u in teSchrijven)
        {
            await using var cmd = new MySqlCommand(
                "UPDATE `Uren` SET `Total_Amount` = @b, `Total_Cost` = @k WHERE `Uren-id` = @id", cn, tx);
            cmd.Parameters.AddWithValue("@b", u.NieuwBedrag);
            cmd.Parameters.AddWithValue("@k", (object?)u.NieuweKostprijs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", u.UrenId);
            await cmd.ExecuteNonQueryAsync();

            u.Bedrag = u.NieuwBedrag!.Value;
            u.Kostprijs = u.NieuweKostprijs ?? u.Kostprijs;
        }
        await tx.CommitAsync();
        return teSchrijven.Count;
    }
}
