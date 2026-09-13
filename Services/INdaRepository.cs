using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

public interface INdaRepository
{
    bool Beschikbaar { get; }

    /// <summary>Alle NDA's, eventueel beperkt tot één relatie of een zoekterm.</summary>
    Task<List<Nda>> ZoekAsync(string? zoek, int? relatieId, string? status);

    Task<Nda?> GetAsync(int ndaId);

    /// <summary>Nieuw of bijwerken; levert het id.</summary>
    Task<int> BewaarAsync(Nda nda, int? doorWerknemer);

    Task VerwijderAsync(int ndaId);

    /// <summary>Voegt een bestandsregel toe (het bestand zelf zit al in de filestore).</summary>
    Task<int> VoegBestandToeAsync(NdaBestand bestand);

    Task VerwijderBestandAsync(int ndaBestandId);

    /// <summary>Instellingen uit NDA_Instelling (sleutel → waarde).</summary>
    Task<Dictionary<string, string>> GetInstellingenAsync();
    Task ZetInstellingAsync(string sleutel, string? waarde);

    /// <summary>Gegevens van een relatie voor het invullen van een nieuwe NDA.</summary>
    Task<(string Naam, string Adres, string KvK)?> GetRelatieGegevensAsync(int relatieId);

    /// <summary>Relatie zoeken op naam — gebruikt bij het importeren van de oude map.</summary>
    Task<int?> ZoekRelatieOpNaamAsync(string naam);
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenNdaRepository : INdaRepository
{
    public bool Beschikbaar => false;
    public Task<List<Nda>> ZoekAsync(string? zoek, int? relatieId, string? status) => Task.FromResult(new List<Nda>());
    public Task<Nda?> GetAsync(int ndaId) => Task.FromResult<Nda?>(null);
    public Task<int> BewaarAsync(Nda nda, int? doorWerknemer) => Task.FromResult(0);
    public Task VerwijderAsync(int ndaId) => Task.CompletedTask;
    public Task<int> VoegBestandToeAsync(NdaBestand bestand) => Task.FromResult(0);
    public Task VerwijderBestandAsync(int ndaBestandId) => Task.CompletedTask;
    public Task<Dictionary<string, string>> GetInstellingenAsync() => Task.FromResult(new Dictionary<string, string>());
    public Task ZetInstellingAsync(string sleutel, string? waarde) => Task.CompletedTask;
    public Task<(string Naam, string Adres, string KvK)?> GetRelatieGegevensAsync(int relatieId)
        => Task.FromResult<(string, string, string)?>(null);
    public Task<int?> ZoekRelatieOpNaamAsync(string naam) => Task.FromResult<int?>(null);
}

/// <summary>NDA's uit de tabellen NDA en NDA_Bestand.</summary>
public class MySqlNdaRepository : INdaRepository
{
    private readonly string _cs;

    public MySqlNdaRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private const string Kolommen =
        "n.`NDA_id`, n.`Relation_id`, n.`Contactperson_id`, n.`Project_id`, " +
        "n.`RelatieNaam`, n.`Rechtsvorm`, n.`Adres`, n.`KvK`, n.`ToepasselijkRecht`, " +
        "n.`Projectomschrijving`, n.`Projectnummer`, n.`DatumDocument`, n.`DatumAangemaakt`, " +
        "n.`AangemaaktDoor`, n.`DatumVerzonden`, n.`DatumOndertekend`, n.`Einddatum`, " +
        "n.`Onbepaald`, n.`Status`, n.`Soort`, n.`Notities`, COALESCE(r.`name`, '')";

    private static Nda Lees(MySqlDataReader r)
    {
        string S(int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
        int? I(int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
        DateOnly? D(int i) => r.IsDBNull(i) ? null : DateOnly.FromDateTime(Convert.ToDateTime(r.GetValue(i)));
        DateTime? T(int i) => r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));
        bool B(int i)
        {
            if (r.IsDBNull(i)) return false;
            try { return Convert.ToInt64(r.GetValue(i)) != 0; } catch { return false; }
        }

        return new Nda
        {
            Id = r.GetInt32(0),
            RelatieId = I(1),
            ContactpersoonId = I(2),
            ProjectId = I(3),
            RelatieNaam = S(4),
            Rechtsvorm = S(5),
            Adres = S(6),
            KvK = S(7),
            ToepasselijkRecht = S(8),
            Projectomschrijving = S(9),
            Projectnummer = S(10),
            DatumDocument = D(11),
            DatumAangemaakt = T(12),
            AangemaaktDoor = I(13),
            DatumVerzonden = T(14),
            DatumOndertekend = T(15),
            Einddatum = D(16),
            Onbepaald = B(17),
            Status = S(18),
            Soort = S(19),
            Notities = S(20),
            RelatieNu = S(21),
        };
    }

    public async Task<List<Nda>> ZoekAsync(string? zoek, int? relatieId, string? status)
    {
        var delen = new List<string>();
        if (relatieId is not null) delen.Add("n.`Relation_id` = @rel");
        if (!string.IsNullOrWhiteSpace(status)) delen.Add("n.`Status` = @status");
        if (!string.IsNullOrWhiteSpace(zoek))
            delen.Add("(n.`RelatieNaam` LIKE @z OR r.`name` LIKE @z OR n.`Projectomschrijving` LIKE @z " +
                      " OR n.`Projectnummer` LIKE @z OR n.`Zoektekst` LIKE @z)");
        var where = delen.Count > 0 ? "WHERE " + string.Join(" AND ", delen) : "";

        var lijst = new List<Nda>();
        await using var cn = await OpenAsync();
        await using (var cmd = new MySqlCommand(
            $"SELECT {Kolommen} FROM `NDA` n " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = n.`Relation_id` " +
            $" {where} ORDER BY COALESCE(n.`DatumDocument`, n.`DatumAangemaakt`) DESC", cn))
        {
            if (relatieId is not null) cmd.Parameters.AddWithValue("@rel", relatieId.Value);
            if (!string.IsNullOrWhiteSpace(status)) cmd.Parameters.AddWithValue("@status", status);
            if (!string.IsNullOrWhiteSpace(zoek)) cmd.Parameters.AddWithValue("@z", "%" + zoek.Trim() + "%");

            await using var r = (MySqlDataReader)await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) lijst.Add(Lees(r));
        }

        // bestanden er in één keer bij halen
        if (lijst.Count > 0)
        {
            var ids = string.Join(",", lijst.Select(n => n.Id));
            var perNda = lijst.ToDictionary(n => n.Id);
            await using var cmd = new MySqlCommand(
                "SELECT b.`NDA_Bestand_id`, b.`NDA_id`, b.`Versie`, b.`Soort`, b.`FileStoreID`, " +
                "       b.`Datum`, b.`ToegevoegdDoor`, b.`Notitie`, COALESCE(f.`FileOnly`, '') " +
                "  FROM `NDA_Bestand` b " +
                "  LEFT JOIN `FileStore` f ON f.`FileStoreID` = b.`FileStoreID` " +
                $" WHERE b.`NDA_id` IN ({ids}) " +
                "  ORDER BY b.`Datum` DESC, b.`NDA_Bestand_id` DESC", cn);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var ndaId = r.GetInt32(1);
                if (!perNda.TryGetValue(ndaId, out var nda)) continue;
                nda.Bestanden.Add(new NdaBestand
                {
                    Id = r.GetInt32(0),
                    NdaId = ndaId,
                    Versie = r.IsDBNull(2) ? "" : r.GetString(2),
                    Soort = r.IsDBNull(3) ? "word" : r.GetString(3),
                    FileStoreId = r.IsDBNull(4) ? null : r.GetInt32(4),
                    Datum = r.IsDBNull(5) ? null : r.GetDateTime(5),
                    ToegevoegdDoor = r.IsDBNull(6) ? null : r.GetInt32(6),
                    Notitie = r.IsDBNull(7) ? "" : r.GetString(7),
                    Bestandsnaam = r.IsDBNull(8) ? "" : r.GetString(8),
                });
            }
        }

        return lijst;
    }

    public async Task<Nda?> GetAsync(int ndaId)
    {
        var lijst = await ZoekAsync(null, null, null);
        return lijst.FirstOrDefault(n => n.Id == ndaId);
    }

    public async Task<int> BewaarAsync(Nda nda, int? doorWerknemer)
    {
        const string velden =
            "`Relation_id` = @rel, `Contactperson_id` = @contact, `Project_id` = @proj, " +
            "`RelatieNaam` = @naam, `Rechtsvorm` = @vorm, `Adres` = @adres, `KvK` = @kvk, " +
            "`ToepasselijkRecht` = @recht, `Projectomschrijving` = @poms, `Projectnummer` = @pnr, " +
            "`DatumDocument` = @datum, `DatumVerzonden` = @verzonden, `DatumOndertekend` = @getekend, " +
            "`Einddatum` = @eind, `Onbepaald` = @onbepaald, `Status` = @status, `Soort` = @soort, " +
            "`Notities` = @notities";

        var sql = nda.Id == 0
            ? $"INSERT INTO `NDA` SET {velden}, `DatumAangemaakt` = @nu, `AangemaaktDoor` = @door"
            : $"UPDATE `NDA` SET {velden} WHERE `NDA_id` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);

        object Of(object? v) => v ?? DBNull.Value;
        object Dat(DateOnly? d) => d is null ? DBNull.Value : d.Value.ToDateTime(TimeOnly.MinValue);

        cmd.Parameters.AddWithValue("@rel", Of(nda.RelatieId));
        cmd.Parameters.AddWithValue("@contact", Of(nda.ContactpersoonId));
        cmd.Parameters.AddWithValue("@proj", Of(nda.ProjectId));
        cmd.Parameters.AddWithValue("@naam", nda.RelatieNaam ?? "");
        cmd.Parameters.AddWithValue("@vorm", nda.Rechtsvorm ?? "");
        cmd.Parameters.AddWithValue("@adres", nda.Adres ?? "");
        cmd.Parameters.AddWithValue("@kvk", nda.KvK ?? "");
        cmd.Parameters.AddWithValue("@recht", nda.ToepasselijkRecht ?? "Nederlands");
        cmd.Parameters.AddWithValue("@poms", nda.Projectomschrijving ?? "");
        cmd.Parameters.AddWithValue("@pnr", nda.Projectnummer ?? "");
        cmd.Parameters.AddWithValue("@datum", Dat(nda.DatumDocument));
        cmd.Parameters.AddWithValue("@verzonden", Of(nda.DatumVerzonden));
        cmd.Parameters.AddWithValue("@getekend", Of(nda.DatumOndertekend));
        cmd.Parameters.AddWithValue("@eind", Dat(nda.Einddatum));
        cmd.Parameters.AddWithValue("@onbepaald", nda.Onbepaald ? 1 : 0);
        cmd.Parameters.AddWithValue("@status", nda.Status ?? NdaStatus.Concept);
        cmd.Parameters.AddWithValue("@soort", nda.Soort ?? "eigen");
        cmd.Parameters.AddWithValue("@notities", nda.Notities ?? "");

        if (nda.Id == 0)
        {
            cmd.Parameters.AddWithValue("@nu", DateTime.Now);
            cmd.Parameters.AddWithValue("@door", Of(doorWerknemer));
        }
        else cmd.Parameters.AddWithValue("@id", nda.Id);

        await cmd.ExecuteNonQueryAsync();
        return nda.Id == 0 ? (int)cmd.LastInsertedId : nda.Id;
    }

    public async Task VerwijderAsync(int ndaId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `NDA` WHERE `NDA_id` = @id", cn);
        cmd.Parameters.AddWithValue("@id", ndaId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> VoegBestandToeAsync(NdaBestand b)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `NDA_Bestand` SET `NDA_id` = @nda, `Versie` = @versie, `Soort` = @soort, " +
            "`FileStoreID` = @file, `Datum` = @datum, `ToegevoegdDoor` = @door, `Notitie` = @notitie", cn);
        cmd.Parameters.AddWithValue("@nda", b.NdaId);
        cmd.Parameters.AddWithValue("@versie", b.Versie ?? "");
        cmd.Parameters.AddWithValue("@soort", b.Soort ?? "word");
        cmd.Parameters.AddWithValue("@file", (object?)b.FileStoreId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@datum", b.Datum ?? DateTime.Now);
        cmd.Parameters.AddWithValue("@door", (object?)b.ToegevoegdDoor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notitie", b.Notitie ?? "");
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    public async Task VerwijderBestandAsync(int ndaBestandId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `NDA_Bestand` WHERE `NDA_Bestand_id` = @id", cn);
        cmd.Parameters.AddWithValue("@id", ndaBestandId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Dictionary<string, string>> GetInstellingenAsync()
    {
        var uit = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand("SELECT `Sleutel`, `Waarde` FROM `NDA_Instelling`", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                uit[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1);
        }
        catch { /* tabel bestaat nog niet */ }
        return uit;
    }

    public async Task ZetInstellingAsync(string sleutel, string? waarde)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `NDA_Instelling` (`Sleutel`, `Waarde`) VALUES (@s, @w) " +
            "ON DUPLICATE KEY UPDATE `Waarde` = VALUES(`Waarde`)", cn);
        cmd.Parameters.AddWithValue("@s", sleutel);
        cmd.Parameters.AddWithValue("@w", (object?)waarde ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Naam en adres van de relatie, in de vorm die het document verwacht:
    /// "Effenseweg 1, 4838 BA  BREDA". Postcode zit bij jullie in PO_box.
    /// </summary>
    public async Task<(string Naam, string Adres, string KvK)?> GetRelatieGegevensAsync(int relatieId)
    {
        // Plaats heet in deze database 'town', postcode zit in 'PO_box'.
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `name`, `address`, `PO_box`, `town`, `KvK` " +
            "  FROM `Relations` WHERE `id_supplier` = @id", cn);
        cmd.Parameters.AddWithValue("@id", relatieId);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        string S(int i) => r.IsDBNull(i) ? "" : (r.GetValue(i).ToString() ?? "").Trim();

        var straat = S(1);
        var achter = string.Join("  ", new[] { S(2), S(3).ToUpperInvariant() }.Where(s => s.Length > 0));
        var adres = achter.Length == 0 ? straat
                  : straat.Length == 0 ? achter
                  : $"{straat}, {achter}";

        return (S(0), adres, S(4));
    }

    /// <summary>
    /// Zoekt de relatie die bij een naam uit de oude NDA-map hoort. Eerst exact,
    /// daarna 'begint met', daarna 'bevat' — maar alleen als er precies één
    /// treffer is, zodat er nooit een verkeerde relatie aan hangt.
    /// </summary>
    public async Task<int?> ZoekRelatieOpNaamAsync(string naam)
    {
        if (string.IsNullOrWhiteSpace(naam)) return null;
        var schoon = naam.Replace('_', ' ').Trim();

        await using var cn = await OpenAsync();

        foreach (var (patroon, waarde) in new[]
        {
            ("`name` = @n", schoon),
            ("`name` LIKE @n", schoon + "%"),
            ("`name` LIKE @n", "%" + schoon + "%"),
        })
        {
            await using var cmd = new MySqlCommand(
                $"SELECT `id_supplier` FROM `Relations` WHERE {patroon} LIMIT 2", cn);
            cmd.Parameters.AddWithValue("@n", waarde);

            var treffers = new List<int>();
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync()) treffers.Add(r.GetInt32(0));

            if (treffers.Count == 1) return treffers[0];
        }
        return null;
    }
}
