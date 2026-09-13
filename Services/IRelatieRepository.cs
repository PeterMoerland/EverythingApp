using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>Eén document uit de filestore dat bij een relatie hoort.</summary>
public record RelatieDocument(int FileStoreId, int StoreType, string Waar, string Bestandsnaam,
                              DateTime? Toegevoegd, string ToegevoegdDoor);

public interface IRelatieRepository
{
    bool Beschikbaar { get; }

    /// <summary>
    /// Alle documenten in de filestore die aan deze relatie hangen: rechtstreeks
    /// bij de relatie opgeslagen bestanden, NDA's, orders, facturen.
    /// </summary>
    Task<List<RelatieDocument>> GetDocumentenAsync(int relatieId);

    /// <summary>Aantal relaties dat aan de criteria voldoet (voor doorladen tijdens scrollen).</summary>
    Task<int> TelAsync(string? zoek, bool alleenKlanten, bool alleenLeveranciers);

    /// <summary>Een blok relaties, vanaf 'begin'.</summary>
    Task<List<Keuze>> ZoekAsync(string? zoek, bool alleenKlanten, bool alleenLeveranciers,
                                int begin, int aantal);
    Task<Dictionary<string, object?>> GetVeldenAsync(int relatieId);
    Task UpdateVeldAsync(int relatieId, string kolom, object? waarde);
    Task<HashSet<string>> KolommenAsync();
    Task<int> NieuweRelatieAsync(string naam);

    /// <summary>Landen voor de keuzelijsten (country-id).</summary>
    Task<List<Keuze>> GetLandenAsync();

    /// <summary>Reden waarom de relatie niet gewist kan worden, of null als het mag.</summary>
    Task<string?> RelatieInGebruikAsync(int relatieId);

    /// <summary>Verwijdert de relatie inclusief haar contactpersonen.</summary>
    Task VerwijderRelatieAsync(int relatieId);

    // --- tariefafspraken ---
    Task<List<Tariefafspraak>> GetTariefafsprakenAsync(int relatieId);
    Task<int> BewaarTariefafspraakAsync(Tariefafspraak t, int? doorWerknemer);
    Task VerwijderTariefafspraakAsync(int tariefafspraakId);

    // --- contactpersonen ---
    Task<List<Contactpersoon>> GetContactpersonenAsync(int relatieId, bool inclusiefUitDienst);
    Task<int> BewaarContactpersoonAsync(Contactpersoon c);
    Task VerwijderContactpersoonAsync(int contactId);
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenRelatieRepository : IRelatieRepository
{
    public bool Beschikbaar => false;
    public Task<List<RelatieDocument>> GetDocumentenAsync(int relatieId)
        => Task.FromResult(new List<RelatieDocument>());
    public Task<int> TelAsync(string? zoek, bool alleenKlanten, bool alleenLeveranciers) => Task.FromResult(0);
    public Task<List<Keuze>> ZoekAsync(string? zoek, bool alleenKlanten, bool alleenLeveranciers,
                                       int begin, int aantal)
        => Task.FromResult(new List<Keuze>());
    public Task<Dictionary<string, object?>> GetVeldenAsync(int relatieId)
        => Task.FromResult(new Dictionary<string, object?>());
    public Task UpdateVeldAsync(int relatieId, string kolom, object? waarde) => Task.CompletedTask;
    public Task<HashSet<string>> KolommenAsync() => Task.FromResult(new HashSet<string>());
    public Task<int> NieuweRelatieAsync(string naam) => Task.FromResult(0);
    public Task<List<Keuze>> GetLandenAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Tariefafspraak>> GetTariefafsprakenAsync(int relatieId) => Task.FromResult(new List<Tariefafspraak>());
    public Task<int> BewaarTariefafspraakAsync(Tariefafspraak t, int? doorWerknemer) => Task.FromResult(0);
    public Task VerwijderTariefafspraakAsync(int tariefafspraakId) => Task.CompletedTask;
    public Task<string?> RelatieInGebruikAsync(int relatieId) => Task.FromResult<string?>("Geen live database.");
    public Task VerwijderRelatieAsync(int relatieId) => Task.CompletedTask;
    public Task<List<Contactpersoon>> GetContactpersonenAsync(int relatieId, bool inclusiefUitDienst)
        => Task.FromResult(new List<Contactpersoon>());
    public Task<int> BewaarContactpersoonAsync(Contactpersoon c) => Task.FromResult(0);
    public Task VerwijderContactpersoonAsync(int contactId) => Task.CompletedTask;
}

/// <summary>Relaties (klanten en leveranciers) uit de tabel Relations.</summary>
public class MySqlRelatieRepository : IRelatieRepository
{
    private readonly string _cs;
    private HashSet<string>? _kolommen;

    public MySqlRelatieRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<HashSet<string>> KolommenAsync()
    {
        if (_kolommen is not null) return _kolommen;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = 'Relations' ORDER BY ordinal_position", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) set.Add(r.GetString(0));
        _kolommen = set;
        return set;
    }

    private async Task<string> Kolom(params string[] kandidaten)
    {
        var kolommen = await KolommenAsync();
        return kandidaten.FirstOrDefault(kolommen.Contains) ?? kandidaten[0];
    }

    /// <summary>Bouwt het WHERE-deel voor zoeken en tellen (één plek, twee gebruikers).</summary>
    private async Task<(string Where, string IdKolom, string NaamKolom)> ZoekVoorwaarden(
        string? zoek, bool alleenKlanten, bool alleenLeveranciers)
    {
        var kolommen = await KolommenAsync();
        var id = await Kolom("id_supplier", "RelationId", "id");
        var naam = await Kolom("name", "Naam", "Bedrijfsnaam");
        var alias = kolommen.Contains("Alias") ? "Alias" : null;

        var voorwaarden = new List<string>();
        if (!string.IsNullOrWhiteSpace(zoek))
            voorwaarden.Add(alias is null
                ? $"`{naam}` LIKE @z"
                : $"(`{naam}` LIKE @z OR `{alias}` LIKE @z)");

        var klant = new[] { "IsKlant", "Isklant", "Klant", "Customer" }.FirstOrDefault(kolommen.Contains);
        if (alleenKlanten && klant is not null) voorwaarden.Add($"`{klant}` <> 0");

        var lev = new[] { "IsLeverancier", "Isleverancier", "Leverancier", "Supplier" }.FirstOrDefault(kolommen.Contains);
        if (alleenLeveranciers && lev is not null) voorwaarden.Add($"`{lev}` <> 0");

        if (kolommen.Contains("Verberg")) voorwaarden.Add("(`Verberg` = 0 OR `Verberg` IS NULL)");
        if (kolommen.Contains("Deleted")) voorwaarden.Add("(`Deleted` = 0 OR `Deleted` IS NULL)");

        var where = voorwaarden.Count > 0 ? "WHERE " + string.Join(" AND ", voorwaarden) : "";
        return (where, id, naam);
    }

    public async Task<int> TelAsync(string? zoek, bool alleenKlanten, bool alleenLeveranciers)
    {
        var (where, _, _) = await ZoekVoorwaarden(zoek, alleenKlanten, alleenLeveranciers);

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand($"SELECT COUNT(*) FROM `Relations` {where}", cn);
        if (!string.IsNullOrWhiteSpace(zoek)) cmd.Parameters.AddWithValue("@z", "%" + zoek.Trim() + "%");

        var res = await cmd.ExecuteScalarAsync();
        return res is null or DBNull ? 0 : Convert.ToInt32(res);
    }

    public async Task<List<Keuze>> ZoekAsync(string? zoek, bool alleenKlanten, bool alleenLeveranciers,
                                             int begin, int aantal)
    {
        var (where, id, naam) = await ZoekVoorwaarden(zoek, alleenKlanten, alleenLeveranciers);

        var lijst = new List<Keuze>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT `{id}`, `{naam}` FROM `Relations` {where} " +
            $"ORDER BY `{naam}` LIMIT {Math.Clamp(aantal, 1, 200)} OFFSET {Math.Max(begin, 0)}", cn);
        if (!string.IsNullOrWhiteSpace(zoek)) cmd.Parameters.AddWithValue("@z", "%" + zoek.Trim() + "%");

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Keuze(r.GetInt32(0), r.IsDBNull(1) ? "(geen naam)" : r.GetString(1)));
        return lijst;
    }

    public async Task<Dictionary<string, object?>> GetVeldenAsync(int relatieId)
    {
        var velden = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var id = await Kolom("id_supplier", "RelationId", "id");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand($"SELECT * FROM `Relations` WHERE `{id}` = @id", cn);
        cmd.Parameters.AddWithValue("@id", relatieId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
            for (var i = 0; i < r.FieldCount; i++)
                velden[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return velden;
    }

    public async Task UpdateVeldAsync(int relatieId, string kolom, object? waarde)
    {
        var kolommen = await KolommenAsync();
        var echt = kolommen.FirstOrDefault(k => string.Equals(k, kolom, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Kolom '{kolom}' bestaat niet in Relations.");
        var id = await Kolom("id_supplier", "RelationId", "id");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"UPDATE `Relations` SET `{echt}` = @w WHERE `{id}` = @id", cn);
        cmd.Parameters.AddWithValue("@w", waarde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", relatieId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Landen: de tabel is niet met zekerheid bekend, dus we proberen een paar
    /// gangbare namen. Lukt het niet, dan blijft de keuzelijst leeg en toont het
    /// scherm het nummer.
    /// </summary>
    public async Task<List<Keuze>> GetLandenAsync()
    {
        var lijst = new List<Keuze>();
        var tabellen = new[] { "Countries", "countries", "Country", "country", "Landen", "landen" };
        var idNamen = new[] { "country-id", "country_id", "CountryId", "id_country", "id" };
        var naamNamen = new[] { "country", "Country", "name", "Naam", "Land", "land", "Omschrijving" };

        foreach (var tabel in tabellen)
        {
            try
            {
                await using var cn = await OpenAsync();
                await using var cmd = new MySqlCommand($"SELECT * FROM `{tabel}`", cn);
                await using var r = (MySqlDataReader)await cmd.ExecuteReaderAsync();

                while (await r.ReadAsync())
                {
                    int? id = null;
                    string? naam = null;
                    for (var i = 0; i < r.FieldCount; i++)
                    {
                        var kolom = r.GetName(i);
                        if (id is null && idNamen.Any(n => string.Equals(n, kolom, StringComparison.OrdinalIgnoreCase))
                            && !r.IsDBNull(i) && int.TryParse(r.GetValue(i).ToString(), out var v)) id = v;
                        if (naam is null && naamNamen.Any(n => string.Equals(n, kolom, StringComparison.OrdinalIgnoreCase))
                            && !r.IsDBNull(i)) naam = r.GetValue(i).ToString();
                    }
                    if (id is not null) lijst.Add(new Keuze(id.Value, naam ?? $"#{id}"));
                }
                if (lijst.Count > 0) return lijst.OrderBy(k => k.Naam).ToList();
            }
            catch { /* volgende tabelnaam proberen */ }
        }
        return lijst;
    }

    // ============================================================
    //  Documenten uit de filestore die bij een relatie horen
    //
    //  De filestore koppelt met MasterID + StoreType. Voor een relatie zijn dat
    //  deels rechtstreekse bestanden (311) en deels bestanden die aan een order,
    //  factuur of NDA van die relatie hangen. Elke bron wordt apart bevraagd, in
    //  een eigen try/catch: bestaat de tabel of kolom niet, dan wordt die bron
    //  overgeslagen in plaats van dat het hele lijstje faalt.
    // ============================================================
    private static readonly (int StoreType, string Waar, string? Subquery)[] Documentbronnen =
    {
        (311, "Bij de relatie",   null),   // null = MasterID is de relatie zelf
        (310, "NDA",              "SELECT `NDA_id` FROM `NDA` WHERE `Relation_id` = @rel"),
        (4,   "Inkooporder",      "SELECT `Order-id` FROM `Orders` WHERE `id_supplier` = @rel"),
        (3,   "Orderbijlage",     "SELECT `orderitem-id` FROM `Orderitems` " +
                                  "WHERE `Order-id` IN (SELECT `Order-id` FROM `Orders` WHERE `id_supplier` = @rel)"),
        (1,   "Inkomende factuur","SELECT `FactuurInkomend-id` FROM `FacturenInkomend` WHERE `id_supplier` = @rel"),
        (20,  "Inkomende PO",     "SELECT `Order-id` FROM `Orders` WHERE `id_supplier` = @rel"),
        (5,   "Uitgaande factuur","SELECT `FactuurUitgaand-id` FROM `FacturenUitgaand` WHERE `id_supplier` = @rel"),
    };

    public async Task<List<RelatieDocument>> GetDocumentenAsync(int relatieId)
    {
        var lijst = new List<RelatieDocument>();
        await using var cn = await OpenAsync();

        foreach (var (storeType, waar, subquery) in Documentbronnen)
        {
            try
            {
                var voorwaarde = subquery is null
                    ? "f.`MasterID` = @rel"
                    : $"f.`MasterID` IN ({subquery})";

                await using var cmd = new MySqlCommand(
                    "SELECT f.`FileStoreID`, COALESCE(f.`FileOnly`, f.`FileNaam`), f.`DatumToegevoegd`, " +
                    "       COALESCE(w.`NaamVoluit`, '') " +
                    "  FROM `FileStore` f " +
                    "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = f.`ToegevoegdDoor` " +
                    $" WHERE f.`StoreType` = @st AND {voorwaarde} " +
                    "  ORDER BY f.`DatumToegevoegd` DESC", cn);
                cmd.Parameters.AddWithValue("@st", storeType);
                cmd.Parameters.AddWithValue("@rel", relatieId);

                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    lijst.Add(new RelatieDocument(
                        r.GetInt32(0),
                        storeType,
                        waar,
                        r.IsDBNull(1) ? "" : r.GetValue(1).ToString() ?? "",
                        r.IsDBNull(2) ? null : Convert.ToDateTime(r.GetValue(2)),
                        r.IsDBNull(3) ? "" : r.GetString(3)));
            }
            catch { /* deze bron bestaat hier niet */ }
        }

        return lijst
            .OrderByDescending(d => d.Toegevoegd ?? DateTime.MinValue)
            .ToList();
    }

    // ============================================================
    //  Relatie verwijderen
    //  Eerst kijken of er nog iets naar verwijst. Tabellen of kolommen die niet
    //  bestaan worden overgeslagen, zodat dit niet stukloopt op een afwijkend schema.
    // ============================================================
    private static readonly (string Tabel, string Kolom, string Wat)[] Verwijzingen =
    {
        ("Projects",          "Customer-id",   "project(en) als klant"),
        ("Projects",          "Supplier-id",   "project(en) als leverancier"),
        ("Orders",            "id_supplier",   "inkooporder(s)"),
        ("FacturenInkomend",  "id_supplier",   "inkomende factuur/facturen"),
        ("FacturenUitgaand",  "id_supplier",   "uitgaande factuur/facturen"),
        ("Artikelen",         "id_supplier",   "artikel(en)"),
        ("Werknemers",        "WerkzaamBij",   "werknemer(s)"),
        ("Bankrekeningen",    "BVid",          "bankrekening(en)"),
        ("Relations",         "DefaultVerzendAdres", "andere relatie(s) als afleveradres"),
    };

    public async Task<string?> RelatieInGebruikAsync(int relatieId)
    {
        await using var cn = await OpenAsync();
        foreach (var (tabel, kolom, wat) in Verwijzingen)
        {
            try
            {
                await using var cmd = new MySqlCommand(
                    $"SELECT COUNT(*) FROM `{tabel}` WHERE `{kolom}` = @id", cn);
                cmd.Parameters.AddWithValue("@id", relatieId);
                var res = await cmd.ExecuteScalarAsync();
                var aantal = res is null or DBNull ? 0 : Convert.ToInt64(res);
                if (aantal > 0) return $"In gebruik bij {aantal} {wat}.";
            }
            catch { /* tabel of kolom bestaat niet */ }
        }
        return null;
    }

    public async Task VerwijderRelatieAsync(int relatieId)
    {
        var contacten = await ContactTabel();
        var id = await Kolom("id_supplier", "RelationId", "id");

        await using var cn = await OpenAsync();

        // contactpersonen horen bij de relatie en gaan mee
        try
        {
            await using var del = new MySqlCommand($"DELETE FROM `{contacten}` WHERE `id_supplier` = @id", cn);
            del.Parameters.AddWithValue("@id", relatieId);
            await del.ExecuteNonQueryAsync();
        }
        catch { /* geen contactentabel */ }

        await using var cmd = new MySqlCommand($"DELETE FROM `Relations` WHERE `{id}` = @id", cn);
        cmd.Parameters.AddWithValue("@id", relatieId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Tariefafspraken
    // ============================================================
    private string? _tariefTabel;

    private async Task<string> TariefTabel()
    {
        if (_tariefTabel is not null) return _tariefTabel;
        foreach (var naam in new[] { "Tariefafspraken", "Tariefafspraak", "tariefafspraken", "Tariefafspraken_klant" })
        {
            try
            {
                await using var cn = await OpenAsync();
                await using var cmd = new MySqlCommand($"SELECT 1 FROM `{naam}` LIMIT 1", cn);
                await cmd.ExecuteScalarAsync();
                return _tariefTabel = naam;
            }
            catch { }
        }
        return _tariefTabel = "Tariefafspraken";
    }

    public async Task<List<Tariefafspraak>> GetTariefafsprakenAsync(int relatieId)
    {
        var lijst = new List<Tariefafspraak>();
        var tabel = await TariefTabel();

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Tariefafspraak-id`, `Relation-id`, `Contactperson`, `FunctieID`, " +
            "       `Geldigheid start`, `Geldigheid stop`, " +
            "       `BasisTarief`, `LaagTarief`, `MinimumTarief`, `Travel`, `Kilometer`, " +
            "       `Procent Overwerk Basis`, `Procent Overwerk Zon`, `Procent Buitenland`, " +
            "       `Betalingstermijn`, `DefaultToeslagHours`, `DefaultToeslagExpenses`, " +
            "       `DefaultToeslagMaterials`, `Opmerking` " +
            $"  FROM `{tabel}` WHERE `Relation-id` = @id " +
            " ORDER BY `Geldigheid start` DESC", cn);
        cmd.Parameters.AddWithValue("@id", relatieId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            int? I(int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
            decimal? D(int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
            DateOnly? Dat(int i) => r.IsDBNull(i) ? null : DateOnly.FromDateTime(Convert.ToDateTime(r.GetValue(i)));

            lijst.Add(new Tariefafspraak
            {
                Id = r.GetInt32(0),
                RelatieId = I(1) ?? relatieId,
                ContactpersoonId = I(2),
                FunctieId = I(3),
                Van = Dat(4),
                Tot = Dat(5),
                Basistarief = D(6),
                LaagTarief = D(7),
                MinimumTarief = D(8),
                Reistarief = D(9),
                Kilometer = D(10),
                OverwerkBasis = D(11),
                OverwerkZon = D(12),
                Buitenland = D(13),
                Betalingstermijn = I(14),
                ToeslagUren = D(15),
                ToeslagExpenses = D(16),
                ToeslagMateriaal = D(17),
                Opmerking = r.IsDBNull(18) ? "" : r.GetValue(18).ToString() ?? "",
            });
        }
        return lijst;
    }

    public async Task<int> BewaarTariefafspraakAsync(Tariefafspraak t, int? doorWerknemer)
    {
        var tabel = await TariefTabel();

        var velden =
            "`Relation-id` = @rel, `Contactperson` = @contact, `FunctieID` = @functie, " +
            "`Geldigheid start` = @van, `Geldigheid stop` = @tot, " +
            "`BasisTarief` = @basis, `LaagTarief` = @laag, `MinimumTarief` = @min, " +
            "`Travel` = @travel, `Kilometer` = @km, " +
            "`Procent Overwerk Basis` = @ow1, `Procent Overwerk Zon` = @ow2, `Procent Buitenland` = @buit, " +
            "`Betalingstermijn` = @termijn, `DefaultToeslagHours` = @turen, " +
            "`DefaultToeslagExpenses` = @texp, `DefaultToeslagMaterials` = @tmat, " +
            "`Opmerking` = @opm";

        if (t.Id == 0) velden += ", `Created at` = @nu, `Created by` = @door";

        var sql = t.Id == 0
            ? $"INSERT INTO `{tabel}` SET {velden}"
            : $"UPDATE `{tabel}` SET {velden} WHERE `Tariefafspraak-id` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        object? Of(object? v) => v ?? DBNull.Value;
        object? Dat(DateOnly? d) => d is null ? DBNull.Value : d.Value.ToDateTime(TimeOnly.MinValue);

        cmd.Parameters.AddWithValue("@rel", t.RelatieId);
        cmd.Parameters.AddWithValue("@contact", Of(t.ContactpersoonId));
        cmd.Parameters.AddWithValue("@functie", Of(t.FunctieId));
        cmd.Parameters.AddWithValue("@van", Dat(t.Van));
        cmd.Parameters.AddWithValue("@tot", Dat(t.Tot));
        cmd.Parameters.AddWithValue("@basis", Of(t.Basistarief));
        cmd.Parameters.AddWithValue("@laag", Of(t.LaagTarief));
        cmd.Parameters.AddWithValue("@min", Of(t.MinimumTarief));
        cmd.Parameters.AddWithValue("@travel", Of(t.Reistarief));
        cmd.Parameters.AddWithValue("@km", Of(t.Kilometer));
        cmd.Parameters.AddWithValue("@ow1", Of(t.OverwerkBasis));
        cmd.Parameters.AddWithValue("@ow2", Of(t.OverwerkZon));
        cmd.Parameters.AddWithValue("@buit", Of(t.Buitenland));
        cmd.Parameters.AddWithValue("@termijn", Of(t.Betalingstermijn));
        cmd.Parameters.AddWithValue("@turen", Of(t.ToeslagUren));
        cmd.Parameters.AddWithValue("@texp", Of(t.ToeslagExpenses));
        cmd.Parameters.AddWithValue("@tmat", Of(t.ToeslagMateriaal));
        cmd.Parameters.AddWithValue("@opm", t.Opmerking ?? "");
        if (t.Id == 0)
        {
            cmd.Parameters.AddWithValue("@nu", DateTime.Now);
            cmd.Parameters.AddWithValue("@door", Of(doorWerknemer));
        }
        else cmd.Parameters.AddWithValue("@id", t.Id);

        await cmd.ExecuteNonQueryAsync();
        return t.Id == 0 ? (int)cmd.LastInsertedId : t.Id;
    }

    public async Task VerwijderTariefafspraakAsync(int tariefafspraakId)
    {
        var tabel = await TariefTabel();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"DELETE FROM `{tabel}` WHERE `Tariefafspraak-id` = @id", cn);
        cmd.Parameters.AddWithValue("@id", tariefafspraakId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Contactpersonen (tabel Contacts)
    // ============================================================
    private async Task<string> ContactTabel()
    {
        foreach (var naam in new[] { "Contacts", "contacts", "Contactpersonen", "contactpersonen", "Contact" })
        {
            try
            {
                await using var cn = await OpenAsync();
                await using var cmd = new MySqlCommand($"SELECT 1 FROM `{naam}` LIMIT 1", cn);
                await cmd.ExecuteScalarAsync();
                return naam;
            }
            catch { }
        }
        return "Contacts";
    }

    public async Task<List<Contactpersoon>> GetContactpersonenAsync(int relatieId, bool inclusiefUitDienst)
    {
        var lijst = new List<Contactpersoon>();
        var tabel = await ContactTabel();

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ID_contact`, `id_supplier`, `Naam`, `Title`, `Afdeling`, `Funktie`, " +
            "       `Telefoon`, `Mobiel`, `Fax`, `mail`, `Aanhef`, `Opmerking`, " +
            "       `NotAPerson`, `OutOfCompany` " +
            $"  FROM `{tabel}` WHERE `id_supplier` = @id " +
            (inclusiefUitDienst ? "" : "AND (`OutOfCompany` = 0 OR `OutOfCompany` IS NULL) ") +
            " ORDER BY `Naam`", cn);
        cmd.Parameters.AddWithValue("@id", relatieId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string S(int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
            bool B(int i)
            {
                if (r.IsDBNull(i)) return false;
                try { return Convert.ToInt64(r.GetValue(i)) != 0; } catch { return false; }
            }

            lijst.Add(new Contactpersoon
            {
                Id = r.GetInt32(0),
                RelatieId = r.IsDBNull(1) ? relatieId : r.GetInt32(1),
                Naam = S(2), Titel = S(3), Afdeling = S(4), Functie = S(5),
                Telefoon = S(6), Mobiel = S(7), Fax = S(8), Mail = S(9),
                Aanhef = S(10), Opmerking = S(11),
                GeenPersoon = B(12), UitDienst = B(13),
            });
        }
        return lijst;
    }

    public async Task<int> BewaarContactpersoonAsync(Contactpersoon c)
    {
        var tabel = await ContactTabel();
        const string velden =
            "`id_supplier` = @rel, `Naam` = @naam, `Title` = @titel, `Afdeling` = @afd, " +
            "`Funktie` = @functie, `Telefoon` = @tel, `Mobiel` = @mob, `Fax` = @fax, " +
            "`mail` = @mail, `Aanhef` = @aanhef, `Opmerking` = @opm, " +
            "`NotAPerson` = @geenpersoon, `OutOfCompany` = @uit";

        var sql = c.Id == 0
            ? $"INSERT INTO `{tabel}` SET {velden}"
            : $"UPDATE `{tabel}` SET {velden} WHERE `ID_contact` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@rel", c.RelatieId);
        cmd.Parameters.AddWithValue("@naam", c.Naam ?? "");
        cmd.Parameters.AddWithValue("@titel", c.Titel ?? "");
        cmd.Parameters.AddWithValue("@afd", c.Afdeling ?? "");
        cmd.Parameters.AddWithValue("@functie", c.Functie ?? "");
        cmd.Parameters.AddWithValue("@tel", c.Telefoon ?? "");
        cmd.Parameters.AddWithValue("@mob", c.Mobiel ?? "");
        cmd.Parameters.AddWithValue("@fax", c.Fax ?? "");
        cmd.Parameters.AddWithValue("@mail", c.Mail ?? "");
        cmd.Parameters.AddWithValue("@aanhef", c.Aanhef ?? "");
        cmd.Parameters.AddWithValue("@opm", c.Opmerking ?? "");
        cmd.Parameters.AddWithValue("@geenpersoon", c.GeenPersoon ? 1 : 0);
        cmd.Parameters.AddWithValue("@uit", c.UitDienst ? 1 : 0);
        if (c.Id != 0) cmd.Parameters.AddWithValue("@id", c.Id);

        await cmd.ExecuteNonQueryAsync();
        return c.Id == 0 ? (int)cmd.LastInsertedId : c.Id;
    }

    public async Task VerwijderContactpersoonAsync(int contactId)
    {
        var tabel = await ContactTabel();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand($"DELETE FROM `{tabel}` WHERE `ID_contact` = @id", cn);
        cmd.Parameters.AddWithValue("@id", contactId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> NieuweRelatieAsync(string naam)
    {
        var naamKolom = await Kolom("name", "Naam", "Bedrijfsnaam");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"INSERT INTO `Relations` SET `{naamKolom}` = @naam", cn);
        cmd.Parameters.AddWithValue("@naam", naam);
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }
}
