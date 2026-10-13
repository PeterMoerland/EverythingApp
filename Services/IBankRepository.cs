using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>Waar filteren we op: bij, af of allebei.</summary>
public enum BijAf { Beide, Bij, Af }

/// <summary>De uitkomst van een import: wat is er met de regels gebeurd.</summary>
public record BankImportResultaat(int Verwerkt, int Toegevoegd, int AlAanwezig, List<string> Fouten)
{
    public bool Gelukt => Fouten.Count == 0;
}

public interface IBankRepository
{
    bool Beschikbaar { get; }

    /// <summary>
    /// De rekeningen waar deze werknemer bij mag, volgens BankRekeningRechten.
    /// Zonder recht krijg je niets te zien — bankgegevens zijn niet voor iedereen.
    /// </summary>
    Task<List<Bankrekening>> GetRekeningenAsync(int? werknemerId);

    Task<List<Banktransactie>> ZoekAsync(IEnumerable<string> rekeningen, DateOnly van, DateOnly tot,
                                         string? zoek, BijAf bijAf, int? pasnummer, int max);

    Task<BankTotalen> GetTotalenAsync(IEnumerable<string> alleRekeningen, IEnumerable<string> gekozen,
                                      DateOnly van, DateOnly tot, string? zoek, BijAf bijAf, int? pasnummer);

    Task<BanktransactieDetail?> GetDetailAsync(int id);
    Task ZetOpmerkingAsync(int id, string? opmerking);

    /// <summary>Paskaartnummers die in de transacties voorkomen, voor het filter.</summary>
    Task<List<int>> GetPasnummersAsync(IEnumerable<string> rekeningen);

    /// <summary>
    /// Leest een CSV-export van de Rabobank in. Regels die er al staan worden
    /// overgeslagen (op volgnummer; de spaarrekening heeft er geen en gaat op
    /// saldo, bedrag en datum) — dubbel importeren kan dus geen kwaad.
    /// </summary>
    Task<BankImportResultaat> ImporteerAsync(string csv, int? doorWerknemerId);

    /// <summary>
    /// Zet regels uit de Rabobank-API in banktransacties. Een regel bestaat al als het
    /// volgnummer op die rekening er staat, óf als rekening, datum, bedrag en saldo
    /// gelijk zijn — dat vangt ook de regels die eerder uit een CSV kwamen, voor het geval
    /// de API anders nummert. Met <paramref name="alleenKijken"/> wordt er niets
    /// weggeschreven en telt 'Toegevoegd' wat er toegevoegd zou worden.
    /// </summary>
    Task<BankImportResultaat> VoegApiRegelsToeAsync(IReadOnlyList<BankRegel> regels, bool alleenKijken);
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenBankRepository : IBankRepository
{
    public bool Beschikbaar => false;
    public Task<List<Bankrekening>> GetRekeningenAsync(int? werknemerId)
        => Task.FromResult(new List<Bankrekening>());
    public Task<List<Banktransactie>> ZoekAsync(IEnumerable<string> rekeningen, DateOnly van, DateOnly tot,
                                                string? zoek, BijAf bijAf, int? pasnummer, int max)
        => Task.FromResult(new List<Banktransactie>());
    public Task<BankTotalen> GetTotalenAsync(IEnumerable<string> alleRekeningen, IEnumerable<string> gekozen,
                                             DateOnly van, DateOnly tot, string? zoek, BijAf bijAf, int? pasnummer)
        => Task.FromResult(new BankTotalen(0, 0, null, null, null));
    public Task<BanktransactieDetail?> GetDetailAsync(int id) => Task.FromResult<BanktransactieDetail?>(null);
    public Task ZetOpmerkingAsync(int id, string? opmerking) => Task.CompletedTask;
    public Task<List<int>> GetPasnummersAsync(IEnumerable<string> rekeningen) => Task.FromResult(new List<int>());
    public Task<BankImportResultaat> ImporteerAsync(string csv, int? doorWerknemerId)
        => Task.FromResult(new BankImportResultaat(0, 0, 0, new List<string> { "Geen live database." }));
    public Task<BankImportResultaat> VoegApiRegelsToeAsync(IReadOnlyList<BankRegel> regels, bool alleenKijken)
        => Task.FromResult(new BankImportResultaat(regels.Count, 0, 0, new List<string> { "Geen live database." }));
}

/// <summary>Banktransacties uit banktransacties, Bankrekeningen en BankRekeningRechten.</summary>
public class MySqlBankRepository : IBankRepository
{
    private readonly string _cs;

    public MySqlBankRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    // ============================================================
    //  Rekeningen en rechten
    // ============================================================
    public async Task<List<Bankrekening>> GetRekeningenAsync(int? werknemerId)
    {
        var lijst = new List<Bankrekening>();
        if (werknemerId is null) return lijst;      // niet gekoppeld = geen bankgegevens

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT b.`Bankrekeningen_id`, b.`Rekeningnummer`, " +
            "       COALESCE(b.`OmschrijvingRekening`, ''), b.`BVid` " +
            "  FROM `Bankrekeningen` b " +
            "  JOIN `BankRekeningRechten` r ON r.`BankrekeningID` = b.`Bankrekeningen_id` " +
            " WHERE r.`WerknemerID` = @wn " +
            " ORDER BY b.`OmschrijvingRekening`, b.`Rekeningnummer`", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId.Value);

        await using var r2 = await cmd.ExecuteReaderAsync();
        while (await r2.ReadAsync())
            lijst.Add(new Bankrekening(
                r2.GetInt32(0),
                r2.IsDBNull(1) ? "" : r2.GetString(1),
                r2.IsDBNull(2) ? "" : r2.GetString(2),
                r2.IsDBNull(3) ? null : r2.GetInt32(3)));

        return lijst;
    }

    // ============================================================
    //  Zoeken
    // ============================================================

    /// <summary>
    /// Eén plek voor de voorwaarden, gebruikt door de lijst én de totalen. De
    /// rekeningen komen altijd uit de rechtencontrole, nooit uit het scherm.
    /// </summary>
    private static string Voorwaarden(List<string> rekeningen, string? zoek, BijAf bijAf, int? pasnummer)
    {
        var delen = new List<string> { "t.`Datum` >= @van", "t.`Datum` < @tot" };

        if (rekeningen.Count == 0) delen.Add("1 = 0");      // geen recht, dus geen regels
        else delen.Add("t.`Rekening` IN (" +
                       string.Join(",", rekeningen.Select((_, i) => "@r" + i)) + ")");

        if (!string.IsNullOrWhiteSpace(zoek))
            delen.Add("(t.`Omschrijving` LIKE @z OR t.`Tenaamstelling` LIKE @z " +
                      " OR t.`NaamTegenpartij` LIKE @z OR t.`Tegenrekening` LIKE @z " +
                      " OR t.`Opmerking` LIKE @z)");

        if (bijAf == BijAf.Bij) delen.Add("t.`Bedrag` > 0");
        if (bijAf == BijAf.Af) delen.Add("t.`Bedrag` < 0");
        if (pasnummer is not null) delen.Add("t.`Pasnummer` = @pas");

        return "WHERE " + string.Join(" AND ", delen);
    }

    private static void Vul(MySqlCommand cmd, List<string> rekeningen, DateOnly van, DateOnly tot,
                            string? zoek, int? pasnummer)
    {
        cmd.Parameters.AddWithValue("@van", van.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tot", tot.AddDays(1).ToDateTime(TimeOnly.MinValue));

        for (var i = 0; i < rekeningen.Count; i++)
            cmd.Parameters.AddWithValue("@r" + i, rekeningen[i]);

        if (!string.IsNullOrWhiteSpace(zoek)) cmd.Parameters.AddWithValue("@z", "%" + zoek.Trim() + "%");
        if (pasnummer is not null) cmd.Parameters.AddWithValue("@pas", pasnummer.Value);
    }

    public async Task<List<Banktransactie>> ZoekAsync(IEnumerable<string> rekeningen, DateOnly van, DateOnly tot,
                                                      string? zoek, BijAf bijAf, int? pasnummer, int max)
    {
        var lijst = new List<Banktransactie>();
        var toegestaan = rekeningen.ToList();

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT t.`BankTransacties_ID`, t.`Rekening`, t.`Datum`, t.`Bedrag`, " +
            "       COALESCE(t.`Tegenrekening`, ''), COALESCE(t.`Tenaamstelling`, ''), " +
            "       COALESCE(t.`Omschrijving`, ''), COALESCE(t.`Opmerking`, ''), t.`Pasnummer` " +
            "  FROM `banktransacties` t " +
            $" {Voorwaarden(toegestaan, zoek, bijAf, pasnummer)} " +
            $" ORDER BY t.`Datum` DESC, t.`BankTransacties_ID` DESC LIMIT {Math.Clamp(max, 1, 2000)}", cn);
        Vul(cmd, toegestaan, van, tot, zoek, pasnummer);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string S(int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
            lijst.Add(new Banktransactie
            {
                Id = r.GetInt32(0),
                Rekening = S(1),
                Datum = r.IsDBNull(2) ? null : Convert.ToDateTime(r.GetValue(2)),
                Bedrag = r.IsDBNull(3) ? 0 : Convert.ToDecimal(r.GetValue(3)),
                Tegenrekening = S(4),
                Tenaamstelling = S(5),
                Omschrijving = S(6),
                Opmerking = S(7),
                Pasnummer = r.IsDBNull(8) ? null : Convert.ToInt32(r.GetValue(8)),
            });
        }
        return lijst;
    }

    /// <summary>
    /// Totaal en aantal van de selectie, plus de actuele saldo's. Het saldo komt uit
    /// 'Saldo na trn' van de nieuwste transactie per rekening — dat is wat de bank
    /// als laatste doorgaf.
    /// </summary>
    public async Task<BankTotalen> GetTotalenAsync(IEnumerable<string> alleRekeningen, IEnumerable<string> gekozen,
                                                   DateOnly van, DateOnly tot, string? zoek, BijAf bijAf,
                                                   int? pasnummer)
    {
        var toegestaan = gekozen.ToList();
        var alles = alleRekeningen.ToList();

        decimal totaal = 0;
        var aantal = 0;
        DateTime? laatste = null;

        await using var cn = await OpenAsync();

        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(SUM(t.`Bedrag`), 0), COUNT(*), MAX(t.`Datum`) " +
            "  FROM `banktransacties` t " +
            $" {Voorwaarden(toegestaan, zoek, bijAf, pasnummer)}", cn))
        {
            Vul(cmd, toegestaan, van, tot, zoek, pasnummer);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                totaal = r.IsDBNull(0) ? 0 : Convert.ToDecimal(r.GetValue(0));
                aantal = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1));
                laatste = r.IsDBNull(2) ? null : Convert.ToDateTime(r.GetValue(2));
            }
        }

        var saldoGekozen = await SaldoAsync(cn, toegestaan);
        var saldoAlles = await SaldoAsync(cn, alles);

        return new BankTotalen(totaal, aantal, saldoGekozen, saldoAlles, laatste);
    }

    /// <summary>Som van het laatst bekende saldo per rekening.</summary>
    private static async Task<decimal?> SaldoAsync(MySqlConnection cn, List<string> rekeningen)
    {
        if (rekeningen.Count == 0) return null;

        var namen = string.Join(",", rekeningen.Select((_, i) => "@s" + i));
        await using var cmd = new MySqlCommand(
            "SELECT SUM(laatste.`Saldo na trn`) FROM (" +
            "   SELECT t.`Rekening`, t.`Saldo na trn` " +
            "     FROM `banktransacties` t " +
            "     JOIN (SELECT `Rekening`, MAX(`BankTransacties_ID`) AS nieuwste " +
            $"            FROM `banktransacties` WHERE `Rekening` IN ({namen}) " +
            "            GROUP BY `Rekening`) n " +
            "       ON n.`Rekening` = t.`Rekening` AND n.nieuwste = t.`BankTransacties_ID`" +
            ") laatste", cn);

        for (var i = 0; i < rekeningen.Count; i++)
            cmd.Parameters.AddWithValue("@s" + i, rekeningen[i]);

        try
        {
            var res = await cmd.ExecuteScalarAsync();
            return res is null or DBNull ? null : Convert.ToDecimal(res);
        }
        catch { return null; }
    }

    public async Task<List<int>> GetPasnummersAsync(IEnumerable<string> rekeningen)
    {
        var lijst = new List<int>();
        var toegestaan = rekeningen.ToList();
        if (toegestaan.Count == 0) return lijst;

        var namen = string.Join(",", toegestaan.Select((_, i) => "@r" + i));
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT DISTINCT `Pasnummer` FROM `banktransacties` " +
            $" WHERE `Pasnummer` IS NOT NULL AND `Rekening` IN ({namen}) ORDER BY `Pasnummer`", cn);

        for (var i = 0; i < toegestaan.Count; i++)
            cmd.Parameters.AddWithValue("@r" + i, toegestaan[i]);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            if (!r.IsDBNull(0)) lijst.Add(Convert.ToInt32(r.GetValue(0)));

        return lijst;
    }

    // ============================================================
    //  Detail
    // ============================================================
    public async Task<BanktransactieDetail?> GetDetailAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT t.*, COALESCE(w.`NaamVoluit`, '') AS ImportDoor " +
            "  FROM `banktransacties` t " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = t.`GeimporteerdDoor` " +
            " WHERE t.`BankTransacties_ID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        // Op naam lezen: zo maakt de volgorde van de kolommen niet uit.
        string S(string kolom)
        {
            try { var v = r[kolom]; return v is DBNull ? "" : v.ToString() ?? ""; }
            catch { return ""; }
        }
        decimal? M(string kolom)
        {
            try { var v = r[kolom]; return v is DBNull ? null : Convert.ToDecimal(v); }
            catch { return null; }
        }
        DateTime? D(string kolom)
        {
            try { var v = r[kolom]; return v is DBNull ? null : Convert.ToDateTime(v); }
            catch { return null; }
        }
        int? I(string kolom)
        {
            try { var v = r[kolom]; return v is DBNull ? null : Convert.ToInt32(v); }
            catch { return null; }
        }

        return new BanktransactieDetail
        {
            Id = id,
            Rekening = S("Rekening"),
            Bic = S("BIC"),
            Munt = S("Munt"),
            Volgnummer = I("VolgNr"),

            Datum = D("Datum"),
            Rentedatum = D("RenteDatum"),
            Verwerkingsdatum = D("Verwerkingsdatum"),
            Importdatum = D("Importdatum"),
            GeimporteerdDoor = S("ImportDoor"),

            Bedrag = M("Bedrag") ?? 0,
            SaldoNa = M("Saldo na trn"),
            Sombedrag = M("SomBedrag"),
            OorspronkelijkBedrag = M("OorspronkelijkeBedrag"),
            OorspronkelijkeMunt = S("OorspronkelijkeMunt"),

            Omschrijving = S("Omschrijving"),
            Tegenrekening = S("Tegenrekening"),
            NaamTegenpartij = S("NaamTegenpartij"),
            NaamUiteindelijkePartij = S("NaamUiteindelijkePartij"),
            NaamInitierendePartij = S("NaamInitierendePartij"),
            BicTegenpartij = S("BICtegenpartij"),

            Code = S("Code"),
            BatchId = S("BatchID"),
            Transactiereferentie = S("Transactiereferentie"),
            MachtigingsKenmerk = S("MachtigingsKenmerk"),
            IncassantId = S("IncassantID"),
            Betalingskenmerk = S("Betalingskenmerk"),
            RedenRetour = S("RedenRetour"),

            Opmerking = S("Opmerking"),
            Pasnummer = I("Pasnummer"),
        };
    }

    public async Task ZetOpmerkingAsync(int id, string? opmerking)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `banktransacties` SET `Opmerking` = @o WHERE `BankTransacties_ID` = @id", cn);
        cmd.Parameters.AddWithValue("@o", (opmerking ?? "").Length > 50
            ? opmerking![..50] : (object?)opmerking ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Importeren van een Rabobank-CSV
    //
    //  Eén op één overgenomen uit het Access-formulier @ImportBank:
    //  dezelfde kolomvolgorde, dezelfde ontdubbeling (volgnummer per rekening;
    //  de spaarrekening heeft geen volgnummer en gaat op saldo + bedrag +
    //  datum), dezelfde inkortingen per veld en hetzelfde uitlezen van het
    //  pasnummer uit de omschrijving.
    // ============================================================

    // Wat er in de CSV van de bank staat, op volgorde (kolom 0 t/m 24).
    private const int KolIban = 0, KolMunt = 1, KolBic = 2, KolVolgnr = 3, KolDatum = 4,
                      KolRentedatum = 5, KolBedrag = 6, KolSaldoNa = 7, KolTegenrekening = 8,
                      KolNaamTegenpartij = 9, KolNaamUiteindelijk = 10, KolNaamInitierend = 11,
                      KolBicTegenpartij = 12, KolCode = 13, KolBatchId = 14, KolReferentie = 15,
                      KolMachtiging = 16, KolIncassant = 17, KolBetalingskenmerk = 18,
                      KolOmschrijving1 = 19, KolOmschrijving2 = 20, KolOmschrijving3 = 21,
                      KolRedenRetour = 22, KolOorspronkelijkBedrag = 23, KolOorspronkelijkeMunt = 24;

    public async Task<BankImportResultaat> ImporteerAsync(string csv, int? doorWerknemerId)
    {
        var fouten = new List<string>();
        var rijen = LeesCsv(csv);

        if (rijen.Count == 0)
            return new(0, 0, 0, new List<string> { "Het bestand is leeg." });

        // Een snelle zekerheid dat dit echt een bankbestand is: de kopregel van de
        // Rabobank begint met IBAN/BBAN. Zo voorkomen we dat een willekeurige CSV
        // als transacties in de boeken belandt.
        if (!string.Join(",", rijen[0]).Contains("IBAN", StringComparison.OrdinalIgnoreCase))
            return new(0, 0, 0, new List<string>
            {
                "De kopregel bevat geen 'IBAN' — dit lijkt geen transactiebestand van de bank. " +
                "Download de transacties op rabobank.nl als CSV en probeer het opnieuw."
            });

        int verwerkt = 0, toegevoegd = 0, alAanwezig = 0;

        await using var cn = await OpenAsync();

        foreach (var rij in rijen.Skip(1))
        {
            verwerkt++;
            var regelnummer = verwerkt + 1;   // +1 voor de kopregel, zoals je in Excel telt

            // Access verving de backslash door een liggend streepje; dat houden we
            // aan zodat nieuwe regels niet afwijken van tien jaar bestaande data.
            string V(int i) => i < rij.Length ? rij[i].Replace('\\', '_').Trim() : "";

            // Voor de omschrijving juist NIET per stuk trimmen: de bank knipt de
            // tekst in stukken van 35 tekens, en valt die knip op een spatie, dan
            // zouden twee woorden aan elkaar plakken.
            string Ruw(int i) => i < rij.Length ? rij[i].Replace('\\', '_') : "";

            // Genormaliseerd opslaan (hoofdletters, zonder spaties): zo matcht de
            // rekening altijd met het overzichtsfilter.
            var iban = V(KolIban).Replace(" ", "").ToUpperInvariant();
            if (!IbanKlopt(iban))
            {
                // Net als in Access stopt de import hier helemaal: een fout IBAN
                // betekent vrijwel zeker een verkeerd of beschadigd bestand.
                fouten.Add($"Regel {regelnummer}: '{iban}' is geen geldig IBAN. De import is gestopt; " +
                           "er is verder niets meer ingelezen.");
                break;
            }

            var datum = DatumUit(V(KolDatum));
            if (datum is null)
            {
                fouten.Add($"Regel {regelnummer}: '{V(KolDatum)}' is geen datum (verwacht: jjjj-mm-dd). " +
                           "Regel overgeslagen.");
                continue;
            }

            var volgnr = long.TryParse(V(KolVolgnr), out var vn) ? vn : 0;

            // Bedragen streng lezen. Zou een onleesbaar bedrag stil nul worden, dan
            // zou de ontdubbeling van de spaarrekening (saldo + bedrag + datum)
            // regels als 'al aanwezig' aanzien en stilletjes laten vallen.
            if (!ProbeerGeld(V(KolBedrag), out var bedrag))
            {
                fouten.Add($"Regel {regelnummer}: '{V(KolBedrag)}' is geen bedrag. Regel overgeslagen.");
                continue;
            }
            if (!ProbeerGeld(V(KolSaldoNa), out var saldoNa))
            {
                fouten.Add($"Regel {regelnummer}: '{V(KolSaldoNa)}' is geen saldo. Regel overgeslagen.");
                continue;
            }

            // ---- staat hij er al? ----
            bool bestaat;
            if (volgnr != 0)
            {
                await using var kijk = new MySqlCommand(
                    "SELECT COUNT(*) FROM `banktransacties` " +
                    " WHERE `VolgNr` = @v AND `Rekening` = @r", cn);
                kijk.Parameters.AddWithValue("@v", volgnr);
                kijk.Parameters.AddWithValue("@r", iban);
                bestaat = Convert.ToInt64(await kijk.ExecuteScalarAsync()) > 0;
            }
            else
            {
                // Spaarrekening: geen volgnummer, dus herkennen op de combinatie.
                // De CAST is er voor het geval de kolommen als DOUBLE zijn omgezet
                // uit Access: dan zou een rechtstreekse vergelijking op een enkel
                // bitje kunnen missen.
                await using var kijk = new MySqlCommand(
                    "SELECT COUNT(*) FROM `banktransacties` " +
                    " WHERE CAST(`Saldo na trn` AS DECIMAL(15,2)) = @s " +
                    "   AND CAST(`Bedrag` AS DECIMAL(15,2)) = @b " +
                    "   AND DATE(`Datum`) = @d", cn);
                kijk.Parameters.AddWithValue("@s", saldoNa);
                kijk.Parameters.AddWithValue("@b", bedrag);
                kijk.Parameters.AddWithValue("@d", datum.Value);
                bestaat = Convert.ToInt64(await kijk.ExecuteScalarAsync()) > 0;
            }

            if (bestaat) { alAanwezig++; continue; }

            // ---- toevoegen ----
            var omschrijving = ZonderDubbeleSpaties(
                (Ruw(KolOmschrijving1) + Ruw(KolOmschrijving2) + Ruw(KolOmschrijving3))
                .Replace('\'', '_')).Trim();

            await using var voeg = new MySqlCommand(
                "INSERT INTO `banktransacties` " +
                "(`Rekening`, `Munt`, `BIC`, `VolgNr`, `Datum`, `RenteDatum`, " +
                " `Bedrag`, `SomBedrag`, `Saldo na trn`, " +
                " `Tegenrekening`, `NaamTegenpartij`, `NaamUiteindelijkePartij`, `NaamInitierendePartij`, " +
                " `BICtegenpartij`, `Code`, `BatchID`, `Transactiereferentie`, `MachtigingsKenmerk`, " +
                " `IncassantID`, `Betalingskenmerk`, `Omschrijving`, `Pasnummer`, `RedenRetour`, " +
                " `OorspronkelijkeBedrag`, `OorspronkelijkeMunt`, " +
                " `Importdatum`, `GeimporteerdDoor`, `Opmerking`) VALUES " +
                "(@rekening, @munt, @bic, @volgnr, @datum, @rente, " +
                " @bedrag, @bedrag, @saldo, " +
                " @tegen, @naamTegen, @naamUit, @naamInit, " +
                " @bicTegen, @code, @batch, @referentie, @machtiging, " +
                " @incassant, @kenmerk, @omschrijving, @pas, @retour, " +
                " @oorBedrag, @oorMunt, " +
                " @nu, @door, '')", cn);

            voeg.Parameters.AddWithValue("@rekening", iban);
            voeg.Parameters.AddWithValue("@munt", V(KolMunt));
            voeg.Parameters.AddWithValue("@bic", V(KolBic));
            voeg.Parameters.AddWithValue("@volgnr", volgnr);
            voeg.Parameters.AddWithValue("@datum", datum.Value);
            voeg.Parameters.AddWithValue("@rente", (object?)DatumUit(V(KolRentedatum)) ?? DBNull.Value);
            voeg.Parameters.AddWithValue("@bedrag", bedrag);
            voeg.Parameters.AddWithValue("@saldo", saldoNa);

            voeg.Parameters.AddWithValue("@tegen", Kort(V(KolTegenrekening), 40));
            voeg.Parameters.AddWithValue("@naamTegen", Kort(V(KolNaamTegenpartij), 50));
            voeg.Parameters.AddWithValue("@naamUit", Kort(V(KolNaamUiteindelijk), 50));
            voeg.Parameters.AddWithValue("@naamInit", Kort(V(KolNaamInitierend), 50));
            voeg.Parameters.AddWithValue("@bicTegen", Kort(V(KolBicTegenpartij), 20));
            voeg.Parameters.AddWithValue("@code", Kort(V(KolCode), 5));
            voeg.Parameters.AddWithValue("@batch", Kort(V(KolBatchId), 30));
            voeg.Parameters.AddWithValue("@referentie", Kort(V(KolReferentie), 50));
            voeg.Parameters.AddWithValue("@machtiging", Kort(V(KolMachtiging), 50));
            voeg.Parameters.AddWithValue("@incassant", Kort(V(KolIncassant), 50));
            voeg.Parameters.AddWithValue("@kenmerk", Kort(V(KolBetalingskenmerk), 50));

            voeg.Parameters.AddWithValue("@omschrijving", omschrijving);
            voeg.Parameters.AddWithValue("@pas", (object?)PasnummerUit(omschrijving) ?? DBNull.Value);
            voeg.Parameters.AddWithValue("@retour", V(KolRedenRetour));
            voeg.Parameters.AddWithValue("@oorBedrag", Geld(V(KolOorspronkelijkBedrag)));
            voeg.Parameters.AddWithValue("@oorMunt", V(KolOorspronkelijkeMunt));

            voeg.Parameters.AddWithValue("@nu", DateTime.Now);
            voeg.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);

            try
            {
                await voeg.ExecuteNonQueryAsync();
                toegevoegd++;
            }
            catch (Exception ex)
            {
                fouten.Add($"Regel {regelnummer}: opslaan mislukt — {ex.Message}. De import is gestopt.");
                break;
            }
        }

        // Bij een bestand in het verkeerde formaat zou elke regel een fout geven;
        // twintig is genoeg om te zien wat er aan de hand is.
        if (fouten.Count > 20)
        {
            var rest = fouten.Count - 20;
            fouten = fouten.Take(20).ToList();
            fouten.Add($"… en nog {rest} vergelijkbare regels.");
        }

        return new(verwerkt, toegevoegd, alAanwezig, fouten);
    }

    // ============================================================
    //  Regels uit de Rabobank-API (RabobankKoppeling)
    // ============================================================
    public async Task<BankImportResultaat> VoegApiRegelsToeAsync(IReadOnlyList<BankRegel> regels, bool alleenKijken)
    {
        var fouten = new List<string>();
        int toegevoegd = 0, alAanwezig = 0;

        await using var cn = await OpenAsync();

        foreach (var r in regels)
        {
            var iban = r.Rekening.Replace(" ", "").ToUpperInvariant();

            await using (var kijk = new MySqlCommand(
                "SELECT COUNT(*) FROM `banktransacties` " +
                " WHERE `Rekening` = @r AND ((@v <> 0 AND `VolgNr` = @v) " +
                "    OR (DATE(`Datum`) = @d AND CAST(`Bedrag` AS DECIMAL(15,2)) = @b " +
                "        AND CAST(`Saldo na trn` AS DECIMAL(15,2)) = @s))", cn))
            {
                kijk.Parameters.AddWithValue("@r", iban);
                kijk.Parameters.AddWithValue("@v", r.Volgnummer);
                kijk.Parameters.AddWithValue("@d", r.Datum.Date);
                kijk.Parameters.AddWithValue("@b", r.Bedrag);
                kijk.Parameters.AddWithValue("@s", r.SaldoNa);
                if (Convert.ToInt64(await kijk.ExecuteScalarAsync()) > 0) { alAanwezig++; continue; }
            }

            if (alleenKijken) { toegevoegd++; continue; }

            // Zelfde opschoning als de CSV-import, zodat de regels niet afwijken.
            string V(string t) => (t ?? "").Replace('\\', '_').Trim();
            var omschrijving = ZonderDubbeleSpaties(V(r.Omschrijving).Replace('\'', '_')).Trim();

            await using var voeg = new MySqlCommand(
                "INSERT INTO `banktransacties` " +
                "(`Rekening`, `Munt`, `BIC`, `VolgNr`, `Datum`, `RenteDatum`, " +
                " `Bedrag`, `SomBedrag`, `Saldo na trn`, " +
                " `Tegenrekening`, `NaamTegenpartij`, `NaamUiteindelijkePartij`, `NaamInitierendePartij`, " +
                " `BICtegenpartij`, `Code`, `BatchID`, `Transactiereferentie`, `MachtigingsKenmerk`, " +
                " `IncassantID`, `Betalingskenmerk`, `Omschrijving`, `Pasnummer`, `RedenRetour`, " +
                " `OorspronkelijkeBedrag`, `OorspronkelijkeMunt`, " +
                " `Importdatum`, `GeimporteerdDoor`, `Opmerking`) VALUES " +
                "(@rekening, @munt, @bic, @volgnr, @datum, @rente, " +
                " @bedrag, @bedrag, @saldo, " +
                " @tegen, @naamTegen, @naamUit, @naamInit, " +
                " @bicTegen, @code, @batch, @referentie, @machtiging, " +
                " @incassant, @kenmerk, @omschrijving, @pas, @retour, " +
                " @oorBedrag, @oorMunt, " +
                " @nu, NULL, @opmerking)", cn);

            voeg.Parameters.AddWithValue("@rekening", iban);
            voeg.Parameters.AddWithValue("@munt", V(r.Munt));
            voeg.Parameters.AddWithValue("@bic", V(r.Bic));
            voeg.Parameters.AddWithValue("@volgnr", r.Volgnummer);
            voeg.Parameters.AddWithValue("@datum", r.Datum.Date);
            voeg.Parameters.AddWithValue("@rente", (object?)r.Rentedatum?.Date ?? DBNull.Value);
            voeg.Parameters.AddWithValue("@bedrag", r.Bedrag);
            voeg.Parameters.AddWithValue("@saldo", r.SaldoNa);

            voeg.Parameters.AddWithValue("@tegen", Kort(V(r.Tegenrekening), 40));
            voeg.Parameters.AddWithValue("@naamTegen", Kort(V(r.NaamTegenpartij), 50));
            voeg.Parameters.AddWithValue("@naamUit", Kort(V(r.NaamUiteindelijkePartij), 50));
            voeg.Parameters.AddWithValue("@naamInit", Kort(V(r.NaamInitierendePartij), 50));
            voeg.Parameters.AddWithValue("@bicTegen", Kort(V(r.BicTegenpartij), 20));
            voeg.Parameters.AddWithValue("@code", Kort(V(r.Code), 5));
            voeg.Parameters.AddWithValue("@batch", Kort(V(r.BatchId), 30));
            voeg.Parameters.AddWithValue("@referentie", Kort(V(r.Referentie), 50));
            voeg.Parameters.AddWithValue("@machtiging", Kort(V(r.Machtiging), 50));
            voeg.Parameters.AddWithValue("@incassant", Kort(V(r.Incassant), 50));
            voeg.Parameters.AddWithValue("@kenmerk", Kort(V(r.Betalingskenmerk), 50));

            voeg.Parameters.AddWithValue("@omschrijving", omschrijving);
            voeg.Parameters.AddWithValue("@pas", (object?)PasnummerUit(omschrijving) ?? DBNull.Value);
            voeg.Parameters.AddWithValue("@retour", V(r.RedenRetour));
            voeg.Parameters.AddWithValue("@oorBedrag", r.OorspronkelijkBedrag);
            voeg.Parameters.AddWithValue("@oorMunt", V(r.OorspronkelijkeMunt));

            voeg.Parameters.AddWithValue("@nu", DateTime.Now);
            voeg.Parameters.AddWithValue("@opmerking", Kort(r.Opmerking ?? "", 50));

            try
            {
                await voeg.ExecuteNonQueryAsync();
                toegevoegd++;
            }
            catch (Exception ex)
            {
                fouten.Add($"{iban} {r.Datum:dd-MM-yyyy} {r.Bedrag}: opslaan mislukt — {ex.Message}. Gestopt.");
                break;
            }
        }

        return new(regels.Count, toegevoegd, alAanwezig, fouten);
    }

    // ------------------------------------------------------------
    //  hulpjes voor de import
    // ------------------------------------------------------------

    /// <summary>
    /// Leest CSV met aanhalingstekens netjes uit: een komma bínnen aanhalingstekens
    /// splitst niet, en "" binnen een veld is één aanhalingsteken. Access loste dit
    /// op met een truc (","  vervangen door "#"); dit is de nette versie.
    /// </summary>
    private static List<string[]> LeesCsv(string tekst)
    {
        var rijen = new List<string[]>();
        var velden = new List<string>();
        var veld = new System.Text.StringBuilder();
        var binnen = false;

        void EindeVeld() { velden.Add(veld.ToString()); veld.Clear(); }
        void EindeRegel()
        {
            EindeVeld();
            if (velden.Count > 1 || velden[0].Length > 0) rijen.Add(velden.ToArray());
            velden.Clear();
        }

        for (var i = 0; i < tekst.Length; i++)
        {
            var c = tekst[i];
            if (binnen)
            {
                if (c == '"')
                {
                    if (i + 1 < tekst.Length && tekst[i + 1] == '"') { veld.Append('"'); i++; }
                    else binnen = false;
                }
                else veld.Append(c);
            }
            else if (c == '"') binnen = true;
            else if (c == ',') EindeVeld();
            else if (c == '\n') EindeRegel();
            else if (c != '\r') veld.Append(c);
        }
        if (veld.Length > 0 || velden.Count > 0) EindeRegel();

        // Eindigt het bestand midden in een aanhalingsteken, dan is de rest van de
        // regels in dat ene veld opgeslokt. Dat mag nooit stil voorbijgaan.
        if (binnen)
            throw new FormatException(
                "Het bestand eindigt in een niet-gesloten aanhalingsteken; het is " +
                "waarschijnlijk afgebroken bij het downloaden.");

        return rijen;
    }

    /// <summary>De officiële IBAN-controle (deelbaarheid door 97).</summary>
    private static bool IbanKlopt(string iban)
    {
        iban = iban.Replace(" ", "").ToUpperInvariant();
        if (iban.Length is < 15 or > 34) return false;

        var omgezet = iban[4..] + iban[..4];
        var rest = 0;
        foreach (var c in omgezet)
        {
            int w;
            if (char.IsAsciiDigit(c)) w = c - '0';
            else if (c is >= 'A' and <= 'Z') w = c - 'A' + 10;
            else return false;
            rest = w < 10 ? (rest * 10 + w) % 97 : (rest * 100 + w) % 97;
        }
        return rest == 1;
    }

    /// <summary>De bank schrijft datums als 2026-08-17.</summary>
    private static DateTime? DatumUit(string tekst)
        => DateTime.TryParseExact(tekst, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                                  System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    /// <summary>
    /// Bedragen komen als "+12,50", maar oudere of afwijkende exports schrijven
    /// "1.234,56" of "1,234.56". Het láátste scheidingsteken is het decimaalteken;
    /// de rest is duizendtal-opsmuk. Onleesbaar = false, nooit stilletjes nul.
    /// </summary>
    private static bool ProbeerGeld(string tekst, out decimal waarde)
    {
        waarde = 0m;
        var s = tekst.Trim().Replace(" ", "").Replace("\u00a0", "");   // ook de harde spatie
        if (s.Length == 0) return true;    // een leeg veld is gewoon nul, net als in Access

        int komma = s.LastIndexOf(','), punt = s.LastIndexOf('.');
        if (komma >= 0 && punt >= 0)
            s = komma > punt ? s.Replace(".", "").Replace(',', '.')   // 1.234,56
                             : s.Replace(",", "");                     // 1,234.56
        else if (komma >= 0) s = s.Replace(',', '.');

        return decimal.TryParse(s,
            System.Globalization.NumberStyles.AllowLeadingSign |
            System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out waarde);
    }

    /// <summary>Als ProbeerGeld, maar mild: onleesbaar wordt nul (voor bijzaakvelden).</summary>
    private static decimal Geld(string tekst) => ProbeerGeld(tekst, out var v) ? v : 0m;

    private static string Kort(string tekst, int lengte)
        => tekst.Length <= lengte ? tekst : tekst[..lengte].Trim();

    private static string ZonderDubbeleSpaties(string tekst)
    {
        while (tekst.Contains("  ")) tekst = tekst.Replace("  ", " ");
        return tekst;
    }

    /// <summary>
    /// Haalt het pasnummer uit de omschrijving: de cijfers direct na 'PASNR.'.
    /// Zo weet het pasfilter in het overzicht wie er gepind heeft.
    /// </summary>
    private static int? PasnummerUit(string omschrijving)
    {
        var p = omschrijving.IndexOf("PASNR.", StringComparison.OrdinalIgnoreCase);
        if (p < 0) return null;

        var i = p + 6;
        while (i < omschrijving.Length && omschrijving[i] == ' ') i++;

        var cijfers = new System.Text.StringBuilder();
        while (i < omschrijving.Length && char.IsAsciiDigit(omschrijving[i]) && cijfers.Length < 3)
            cijfers.Append(omschrijving[i++]);

        return cijfers.Length > 0 ? int.Parse(cijfers.ToString()) : null;
    }
}
