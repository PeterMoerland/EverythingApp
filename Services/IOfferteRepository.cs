using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Offertes aan klanten (Access-formulier Offertes). Fase 1: kop, voorwaarden, aantekeningen, status,
/// nieuw en wissen. Zie ..\Offertes_analyse.md.
/// </summary>
public partial interface IOfferteRepository
{
    bool Beschikbaar { get; }

    /// <summary>Projecten voor de keuzelijst links (Access: @OpenProjectKlant), met het aantal niet-gewiste offertes.</summary>
    Task<List<OfferteProject>> ProjectenAsync(string? klant, string? project, bool alleenMetOffertes, int max);

    Task<List<OfferteRegel>> VanProjectAsync(int projectId);

    /// <summary>Zoeken over alle offertes (Access: Zoek offerte op @ZoekOfferte).</summary>
    Task<List<OfferteRegel>> ZoekAsync(string? tekst, int? makerId, int? status, int max);

    Task<Offerte?> GetAsync(int id);

    /// <summary>Nieuwe offerte (status Aanmaken, Nederlands); nummer = hoogste in het project + 1, ook gewiste.</summary>
    Task<int> NieuwAsync(int projectId, string omschrijving, int? contactId, int? door);

    /// <summary>Kop, voorwaarden en aantekeningen. Bij een offerte op slot alleen de aantekeningen.</summary>
    Task BewaarAsync(Offerte o);

    Task ZetStatusAsync(int id, int status);

    /// <summary>Zacht wissen (Deleted = 1), alleen bij status Aanmaken.</summary>
    Task WisAsync(int id, int? door);

    Task<List<OfferteContact>> ContactenAsync(int klantId);

    Task<List<string>> TekstenAsync(OfferteLijst lijst, int taal);
    Task VoegTekstToeAsync(OfferteLijst lijst, string tekst, int taal);
    Task<List<string>> DisclaimersAsync(int taal);
    Task VoegDisclaimerToeAsync(string tekst, int taal);

    /// <summary>De tarieven van de BV van het project die de offerte nog niet heeft (op afkorting) toevoegen; geeft het aantal.</summary>
    Task<int> ImporteerTarievenAsync(int offerteId);

    /// <summary>Wie er offertes gemaakt heeft (voor het zoekfilter).</summary>
    Task<List<Keuze>> MakersAsync();
}

public partial class GeenOfferteRepository : IOfferteRepository
{
    public bool Beschikbaar => false;
    public Task<List<OfferteProject>> ProjectenAsync(string? klant, string? project, bool alleenMetOffertes, int max) => Task.FromResult(new List<OfferteProject>());
    public Task<List<OfferteRegel>> VanProjectAsync(int projectId) => Task.FromResult(new List<OfferteRegel>());
    public Task<List<OfferteRegel>> ZoekAsync(string? tekst, int? makerId, int? status, int max) => Task.FromResult(new List<OfferteRegel>());
    public Task<Offerte?> GetAsync(int id) => Task.FromResult<Offerte?>(null);
    public Task<int> NieuwAsync(int projectId, string omschrijving, int? contactId, int? door) => Task.FromResult(0);
    public Task BewaarAsync(Offerte o) => Task.CompletedTask;
    public Task ZetStatusAsync(int id, int status) => Task.CompletedTask;
    public Task WisAsync(int id, int? door) => Task.CompletedTask;
    public Task<List<OfferteContact>> ContactenAsync(int klantId) => Task.FromResult(new List<OfferteContact>());
    public Task<List<string>> TekstenAsync(OfferteLijst lijst, int taal) => Task.FromResult(new List<string>());
    public Task VoegTekstToeAsync(OfferteLijst lijst, string tekst, int taal) => Task.CompletedTask;
    public Task<List<string>> DisclaimersAsync(int taal) => Task.FromResult(new List<string>());
    public Task VoegDisclaimerToeAsync(string tekst, int taal) => Task.CompletedTask;
    public Task<int> ImporteerTarievenAsync(int offerteId) => Task.FromResult(0);
    public Task<List<Keuze>> MakersAsync() => Task.FromResult(new List<Keuze>());
}

public partial class MySqlOfferteRepository : IOfferteRepository
{
    private readonly string _cs;

    public MySqlOfferteRepository(string connectionString) => _cs = connectionString;

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
    private static DateTime? D(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));
    private static bool B(MySqlDataReader r, int i) => !r.IsDBNull(i) && Convert.ToInt64(r.GetValue(i)) != 0;
    private static string Kort(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>
    /// Totalen per offerte, zoals de views @OfferteTotaal*: uren × tarief, materiaal aantal × prijs × (1 − korting),
    /// overigen aantal × prijs. (Fase 2 herstelt de dubbele korting bij methode Standaard/Vast; hier op één plek.)
    /// </summary>
    private const string TotaalUren =
        "(SELECT COALESCE(SUM(u.`Uren` * t.`Tarief`), 0) FROM `OfferteUren` u JOIN `OfferteTarieven` t " +
        "  ON t.`OfferteTarievenID` = u.`OfferteTarievenID` WHERE u.`OfferteID` = o.`OfferteID`)";
    private const string AantalUren =
        "(SELECT COALESCE(SUM(u.`Uren`), 0) FROM `OfferteUren` u JOIN `OfferteTarieven` t " +
        "  ON t.`OfferteTarievenID` = u.`OfferteTarievenID` WHERE u.`OfferteID` = o.`OfferteID`)";
    private const string TotaalMateriaal =
        "(SELECT COALESCE(SUM(m.`Aantal` * m.`Prijs` * (1 - COALESCE(m.`Korting`, 0))), 0) FROM `OfferteMateriaal` m " +
        "  WHERE m.`OfferteID` = o.`OfferteID`)";
    private const string TotaalOverigen =
        "(SELECT COALESCE(SUM(v.`Aantal` * v.`Prijs`), 0) FROM `OfferteOverigen` v WHERE v.`OfferteID` = o.`OfferteID`)";
    private const string Totaal = "(" + TotaalUren + " + " + TotaalMateriaal + " + " + TotaalOverigen + ")";

    private const string NrSql =
        "CONCAT('Q', p.`Projectnumber`, '-', IF(o.`OfferteNummer` < 100, LPAD(o.`OfferteNummer`, 2, '0'), o.`OfferteNummer`))";

    private const string RegelSelect =
        "SELECT o.`OfferteID`, " + NrSql + ", COALESCE(o.`Omschrijving`, ''), COALESCE(o.`Status`, 1), o.`AanmaakDatum`, " +
        "       COALESCE(w.`NaamVoluit`, ''), " + Totaal + ", o.`ProjectID`, " +
        "       CONCAT_WS(' · ', p.`Projectnumber`, p.`Projectdescription`), COALESCE(k.`name`, '') " +
        "  FROM `Offertes` o JOIN `Projects` p ON p.`Project-Id` = o.`ProjectID` " +
        "  LEFT JOIN `Relations` k ON k.`id_supplier` = p.`Customer-id` " +
        "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = o.`AangemaaktDoor` " +
        " WHERE COALESCE(o.`Deleted`, 0) = 0 ";

    private static OfferteRegel LeesRegel(MySqlDataReader r) => new(
        r.GetInt32(0), S(r, 1), S(r, 2), Convert.ToInt32(r.GetValue(3)), D(r, 4), S(r, 5),
        Math.Round(M(r, 6), 2), r.GetInt32(7), S(r, 8), S(r, 9));

    public async Task<List<OfferteProject>> ProjectenAsync(string? klant, string? project, bool alleenMetOffertes, int max)
    {
        var lijst = new List<OfferteProject>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand { Connection = cn };
        var waar = new List<string>();
        if (!string.IsNullOrWhiteSpace(klant)) { waar.Add("k.`name` LIKE @klant"); cmd.Parameters.AddWithValue("@klant", "%" + klant.Trim() + "%"); }
        if (!string.IsNullOrWhiteSpace(project))
        {
            waar.Add("CONCAT_WS(' ', p.`Projectnumber`, p.`Projectdescription`) LIKE @proj");
            cmd.Parameters.AddWithValue("@proj", "%" + project.Trim() + "%");
        }
        var aantal = "(SELECT COUNT(*) FROM `Offertes` o WHERE o.`ProjectID` = p.`Project-Id` AND COALESCE(o.`Deleted`, 0) = 0)";
        if (alleenMetOffertes) waar.Add(aantal + " > 0");

        cmd.CommandText =
            "SELECT p.`Project-Id`, p.`Projectnumber`, COALESCE(p.`Projectdescription`, ''), p.`Customer-id`, COALESCE(k.`name`, ''), " +
            $"       {aantal}, COALESCE(p.`ProjectStatus`, 1) <> 0 " +
            "  FROM `Projects` p LEFT JOIN `Relations` k ON k.`id_supplier` = p.`Customer-id` " +
            (waar.Count > 0 ? " WHERE " + string.Join(" AND ", waar) : "") +
            $" ORDER BY p.`Projectnumber` DESC LIMIT {Math.Clamp(max, 1, 1000)}";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new OfferteProject(r.GetInt32(0), I(r, 1), S(r, 2), I(r, 3), S(r, 4), Convert.ToInt32(r.GetValue(5)), B(r, 6)));
        return lijst;
    }

    public async Task<List<OfferteRegel>> VanProjectAsync(int projectId)
    {
        var lijst = new List<OfferteRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(RegelSelect + " AND o.`ProjectID` = @p ORDER BY o.`OfferteNummer` DESC", cn);
        cmd.Parameters.AddWithValue("@p", projectId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(LeesRegel(r));
        return lijst;
    }

    public async Task<List<OfferteRegel>> ZoekAsync(string? tekst, int? makerId, int? status, int max)
    {
        var lijst = new List<OfferteRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand { Connection = cn };
        var sql = RegelSelect;
        if (!string.IsNullOrWhiteSpace(tekst))
        {
            // Access @ZoekOfferte: projectomschrijving, offerte-omschrijving, projectnummer, klantnaam; plus het offertenummer
            sql += " AND CONCAT_WS(' ', p.`Projectdescription`, o.`Omschrijving`, p.`Projectnumber`, k.`name`, " + NrSql + ") LIKE @z";
            cmd.Parameters.AddWithValue("@z", "%" + tekst.Trim() + "%");
        }
        if (makerId is int m) { sql += " AND o.`AangemaaktDoor` = @m"; cmd.Parameters.AddWithValue("@m", m); }
        if (status is int s) { sql += " AND o.`Status` = @s"; cmd.Parameters.AddWithValue("@s", s); }
        cmd.CommandText = sql + $" ORDER BY o.`AanmaakDatum` DESC LIMIT {Math.Clamp(max, 1, 500)}";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(LeesRegel(r));
        return lijst;
    }

    public async Task<Offerte?> GetAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT o.`OfferteID`, o.`OfferteNummer`, o.`ProjectID`, p.`Projectnumber`, COALESCE(p.`Projectdescription`, ''), " +
            "       p.`Customer-id`, COALESCE(k.`name`, ''), p.`Werkmaatschappij`, COALESCE(bv.`Afkorting`, ''), " +
            "       COALESCE(o.`Omschrijving`, ''), COALESCE(o.`Status`, 1), o.`Taal`, o.`Contact`, " +
            "       COALESCE(o.`OfferteTekst`, ''), COALESCE(o.`Voettekst`, ''), COALESCE(o.`Geldigheid`, ''), " +
            "       COALESCE(o.`Levertijd`, ''), COALESCE(o.`Betalingstermijn`, ''), COALESCE(o.`Betaling`, ''), " +
            "       o.`AlgemenVoorwaarden`, o.`Ondertekening`, COALESCE(o.`Disclaimer`, ''), COALESCE(o.`Aantekeningen`, ''), " +
            "       o.`AanmaakDatum`, COALESCE(wa.`NaamVoluit`, ''), o.`GemaildOp`, COALESCE(wg.`NaamVoluit`, ''), " +
            "       COALESCE(o.`Bronofferte`, ''), " +
            $"       {TotaalUren}, {AantalUren}, {TotaalMateriaal}, {TotaalOverigen} " +
            "  FROM `Offertes` o JOIN `Projects` p ON p.`Project-Id` = o.`ProjectID` " +
            "  LEFT JOIN `Relations` k ON k.`id_supplier` = p.`Customer-id` " +
            "  LEFT JOIN `Relations` bv ON bv.`id_supplier` = p.`Werkmaatschappij` " +
            "  LEFT JOIN `Werknemers` wa ON wa.`Werknemer-id` = o.`AangemaaktDoor` " +
            "  LEFT JOIN `Werknemers` wg ON wg.`Werknemer-id` = o.`GemaildDoor` " +
            " WHERE o.`OfferteID` = @id AND COALESCE(o.`Deleted`, 0) = 0", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new Offerte
        {
            Id = r.GetInt32(0),
            Nummer = Convert.ToInt32(r.GetValue(1)),
            ProjectId = r.GetInt32(2),
            Projectnummer = I(r, 3),
            Project = S(r, 4),
            KlantId = I(r, 5),
            Klant = S(r, 6),
            BvId = I(r, 7),
            Bv = S(r, 8),
            Omschrijving = S(r, 9),
            Status = Convert.ToInt32(r.GetValue(10)),
            Taal = I(r, 11),
            ContactId = I(r, 12),
            OfferteTekst = S(r, 13),
            Voettekst = S(r, 14),
            Geldigheid = S(r, 15),
            Levertijd = S(r, 16),
            Betalingstermijn = S(r, 17),
            Betaling = S(r, 18),
            AlgemeneVoorwaarden = B(r, 19),
            Ondertekening = B(r, 20),
            Disclaimer = S(r, 21),
            Aantekeningen = S(r, 22),
            AanmaakDatum = D(r, 23),
            AangemaaktDoor = S(r, 24),
            GemaildOp = D(r, 25),
            GemaildDoor = S(r, 26),
            Bronofferte = S(r, 27),
            Totalen = new OfferteTotalen
            {
                Uren = Math.Round(M(r, 28), 2),
                AantalUren = M(r, 29),
                Materiaal = Math.Round(M(r, 30), 2),
                Overigen = Math.Round(M(r, 31), 2),
            },
        };
    }

    public async Task<int> NieuwAsync(int projectId, string omschrijving, int? contactId, int? door)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        // Nummer = hoogste in het project + 1, ook gewiste (Access: DMax). Rijen van het project vergrendelen,
        // zodat twee mensen tegelijk niet hetzelfde nummer krijgen.
        int nummer;
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(MAX(`OfferteNummer`), 0) + 1 FROM `Offertes` WHERE `ProjectID` = @p FOR UPDATE", cn, tx))
        {
            cmd.Parameters.AddWithValue("@p", projectId);
            nummer = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        int id;
        await using (var cmd = new MySqlCommand(
            "INSERT INTO `Offertes` (`OfferteNummer`, `ProjectID`, `Omschrijving`, `Status`, `AanmaakDatum`, `AangemaaktDoor`, " +
            "  `Contact`, `Taal`, `Deleted`) VALUES (@nr, @p, @oms, 1, NOW(), @door, @contact, 1, 0); SELECT LAST_INSERT_ID();", cn, tx))
        {
            cmd.Parameters.AddWithValue("@nr", nummer);
            cmd.Parameters.AddWithValue("@p", projectId);
            cmd.Parameters.AddWithValue("@oms", Kort(omschrijving.Trim(), 250));
            cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@contact", (object?)contactId ?? DBNull.Value);
            id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        await tx.CommitAsync();
        return id;
    }

    public async Task BewaarAsync(Offerte o)
    {
        await using var cn = await OpenAsync();
        // Op slot (vanaf Aangeboden): alleen de interne aantekeningen, zoals Access.
        var sql = o.OpSlot
            ? "UPDATE `Offertes` SET `Aantekeningen` = @aant WHERE `OfferteID` = @id"
            : "UPDATE `Offertes` SET `Omschrijving` = @oms, `Taal` = @taal, `Contact` = @contact, `OfferteTekst` = @kop, " +
              "  `Voettekst` = @voet, `Geldigheid` = @geld, `Levertijd` = @lever, `Betalingstermijn` = @termijn, " +
              "  `Betaling` = @betaling, `AlgemenVoorwaarden` = @alg, `Ondertekening` = @onder, `Disclaimer` = @disc, " +
              "  `Aantekeningen` = @aant WHERE `OfferteID` = @id AND COALESCE(`Status`, 1) <= 1";
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@id", o.Id);
        cmd.Parameters.AddWithValue("@aant", Kort(o.Aantekeningen, 2000));
        cmd.Parameters.AddWithValue("@oms", Kort(o.Omschrijving.Trim(), 250));
        cmd.Parameters.AddWithValue("@taal", (object?)o.Taal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@contact", (object?)o.ContactId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kop", Kort(o.OfferteTekst, 5000));
        cmd.Parameters.AddWithValue("@voet", Kort(o.Voettekst, 2000));
        cmd.Parameters.AddWithValue("@geld", Kort(o.Geldigheid.Trim(), 100));
        cmd.Parameters.AddWithValue("@lever", Kort(o.Levertijd.Trim(), 100));
        cmd.Parameters.AddWithValue("@termijn", Kort(o.Betalingstermijn.Trim(), 100));
        cmd.Parameters.AddWithValue("@betaling", Kort(o.Betaling.Trim(), 100));
        cmd.Parameters.AddWithValue("@alg", o.AlgemeneVoorwaarden ? -1 : 0);
        cmd.Parameters.AddWithValue("@onder", o.Ondertekening ? -1 : 0);
        cmd.Parameters.AddWithValue("@disc", Kort(o.Disclaimer, 1000));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetStatusAsync(int id, int status)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `Offertes` SET `Status` = @s WHERE `OfferteID` = @id", cn);
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task WisAsync(int id, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Offertes` SET `Deleted` = 1, `DeletedAt` = NOW(), `DeletedBy` = @door " +
            " WHERE `OfferteID` = @id AND COALESCE(`Status`, 1) <= 1", cn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        if (await cmd.ExecuteNonQueryAsync() == 0)
            throw new InvalidOperationException("Alleen een offerte met status Aanmaken kan gewist worden.");
    }

    public async Task<List<OfferteContact>> ContactenAsync(int klantId)
    {
        var lijst = new List<OfferteContact>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ID_contact`, COALESCE(`Naam`, ''), COALESCE(`mail`, ''), COALESCE(`OutOfCompany`, 0) <> 0 " +
            "  FROM `Contacts` WHERE `id_supplier` = @k ORDER BY COALESCE(`OutOfCompany`, 0) <> 0, `Naam`", cn);
        cmd.Parameters.AddWithValue("@k", klantId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new OfferteContact(r.GetInt32(0), S(r, 1), S(r, 2), B(r, 3)));
        return lijst;
    }

    private static (string Tabel, string Kolom) LijstTabel(OfferteLijst l) => l switch
    {
        OfferteLijst.Geldigheid => ("OfferteGeldigheid", "Geldigheid"),
        OfferteLijst.Levertijd => ("OfferteLevertijd", "Levertijd"),
        OfferteLijst.Betalingstermijn => ("OfferteBetalingstermijn", "Betalingstermijn"),
        _ => ("OfferteBetaling", "Betaling"),
    };

    public async Task<List<string>> TekstenAsync(OfferteLijst lijst, int taal)
    {
        var (tabel, kolom) = LijstTabel(lijst);
        var uit = new List<string>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT DISTINCT TRIM(`{kolom}`) FROM `{tabel}` WHERE `Taal` = @t AND TRIM(COALESCE(`{kolom}`, '')) NOT IN ('', '`') ORDER BY 1", cn);
        cmd.Parameters.AddWithValue("@t", taal);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit.Add(S(r, 0));
        return uit;
    }

    public async Task VoegTekstToeAsync(OfferteLijst lijst, string tekst, int taal)
    {
        var (tabel, kolom) = LijstTabel(lijst);
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"INSERT INTO `{tabel}` (`{kolom}`, `Taal`) SELECT @x, @t FROM DUAL " +
            $" WHERE NOT EXISTS (SELECT 1 FROM `{tabel}` WHERE TRIM(`{kolom}`) = @x AND `Taal` = @t)", cn);
        cmd.Parameters.AddWithValue("@x", Kort(tekst.Trim(), 100));
        cmd.Parameters.AddWithValue("@t", taal);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> DisclaimersAsync(int taal)
    {
        var uit = new List<string>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Disclaimer` FROM `OfferteDisclaimer` WHERE `Taal` = @t AND TRIM(COALESCE(`Disclaimer`, '')) <> '' " +
            " ORDER BY `OfferteDisclaimerID`", cn);
        cmd.Parameters.AddWithValue("@t", taal);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit.Add(S(r, 0));
        return uit;
    }

    public async Task VoegDisclaimerToeAsync(string tekst, int taal)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `OfferteDisclaimer` (`Disclaimer`, `Taal`) SELECT @x, @t FROM DUAL " +
            " WHERE NOT EXISTS (SELECT 1 FROM `OfferteDisclaimer` WHERE `Disclaimer` = @x AND `Taal` = @t)", cn);
        cmd.Parameters.AddWithValue("@x", Kort(tekst.Trim(), 1000));
        cmd.Parameters.AddWithValue("@t", taal);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> ImporteerTarievenAsync(int offerteId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `OfferteTarieven` (`OfferteID`, `Omschrijving`, `Afkorting`, `Tarief`) " +
            "SELECT o.`OfferteID`, rt.`Omschrijving`, rt.`Afkorting`, rt.`Tarief` " +
            "  FROM `Offertes` o JOIN `Projects` p ON p.`Project-Id` = o.`ProjectID` " +
            "  JOIN `RelationTarieven` rt ON rt.`RelationID` = p.`Werkmaatschappij` " +
            " WHERE o.`OfferteID` = @id AND NOT EXISTS (SELECT 1 FROM `OfferteTarieven` t " +
            "   WHERE t.`OfferteID` = o.`OfferteID` AND t.`Afkorting` = rt.`Afkorting`)", cn);
        cmd.Parameters.AddWithValue("@id", offerteId);
        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Keuze>> MakersAsync()
    {
        var lijst = new List<Keuze>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT DISTINCT w.`Werknemer-id`, w.`NaamVoluit` FROM `Offertes` o " +
            "  JOIN `Werknemers` w ON w.`Werknemer-id` = o.`AangemaaktDoor` ORDER BY w.`NaamVoluit`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new Keuze(r.GetInt32(0), S(r, 1)));
        return lijst;
    }
}
