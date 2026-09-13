using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

public interface IProductRepository
{
    bool Beschikbaar { get; }

    Task<HashSet<string>> KolommenAsync();

    /// <summary>Producttypen, eventueel gefilterd op zoektekst en producttype.</summary>
    Task<List<ProductNaam>> ZoekProductnamenAsync(string? zoek, int? productTypeId);

    // --- het producttype zelf (ProductNames) ---
    Task<int> NieuwProductnaamAsync(string naam, string omschrijving, int? productTypeId);
    Task BewaarProductnaamAsync(int productNameId, string naam, string omschrijving, int? productTypeId);
    Task<string?> ProductnaamInGebruikAsync(int productNameId);
    Task VerwijderProductnaamAsync(int productNameId);

    /// <summary>De standaardattributen van een producttype (ProductNameAttributes).</summary>
    Task<List<ProductAttribuut>> GetTypeAttributenAsync(int productNameId);
    Task<int> BewaarTypeAttribuutAsync(int productNameId, ProductAttribuut attribuut, int? door);
    Task VerwijderTypeAttribuutAsync(int productNameAttributeId);

    /// <summary>De exemplaren van een producttype (serienummers).</summary>
    Task<List<ProductRegel>> GetProductenAsync(int productNameId, bool inclusiefGewist);

    /// <summary>Zoekt een product op serienummer — voor het scanveld.</summary>
    Task<(int ProductId, int ProductNameId)?> ZoekOpSerienummerAsync(string serienummer);

    /// <summary>Wat er op het typeplaatje moet: producttype, serienummer, bouwjaar.</summary>
    Task<(string Type, string Serienummer, string Bouwjaar)?> GetLabelGegevensAsync(int productId);

    Task<Dictionary<string, object?>> GetVeldenAsync(int productId);
    Task UpdateVeldAsync(int productId, string kolom, object? waarde);

    Task<int> NieuwProductAsync(int productNameId, string serienummer, int? klantId,
                                int? werkorderId, int? door);

    /// <summary>Meerdere exemplaren tegelijk, met doorlopende serienummers.</summary>
    Task<List<int>> NieuweProductenAsync(int productNameId, int aantal, int? klantId,
                                         int? werkorderId, int? door);

    /// <summary>Eén veld voor een groep exemplaren tegelijk zetten.</summary>
    Task WijzigVeldVoorAsync(IEnumerable<int> productIds, string kolom, object? waarde);

    /// <summary>Zacht wissen (isDeleted), zodat de historie blijft bestaan.</summary>
    Task ZetGewistAsync(int productId, bool gewist);

    // --- historie ---
    Task<List<ProductGebeurtenis>> GetHistorieAsync(int productId);
    Task VoegHistorieToeAsync(int productId, string gebeurtenis, int? soortId, int? door);

    // --- attributen van een exemplaar ---
    Task<List<ProductAttribuut>> GetAttributenAsync(int productId);
    Task<int> BewaarAttribuutAsync(int productId, ProductAttribuut attribuut, int? door);
    Task VerwijderAttribuutAsync(int productAttributeId);

    /// <summary>
    /// Vult aan met attributen die het producttype wél heeft en dit exemplaar nog
    /// niet (de knop 'Sync' in Access). Levert het aantal toegevoegde regels.
    /// </summary>
    Task<int> SyncAttributenAsync(int productId, int productNameId, int? door);

    // --- samenstelling (ParentProductenID) ---
    Task<List<SamenstellingRegel>> GetOnderdelenAsync(int productId);
    Task<SamenstellingRegel?> GetGebruiktInAsync(int productId);

    /// <summary>Hangt een exemplaar onder een ander exemplaar.</summary>
    Task KoppelAsync(int onderdeelId, int? ouderId);

    /// <summary>Exemplaren die nog vrij zijn (geen ouder), om als onderdeel te kiezen.</summary>
    Task<List<SamenstellingRegel>> ZoekVrijeExemplarenAsync(string zoek, int max);

    // --- keuzelijsten ---
    Task<List<Keuze>> GetStatussenAsync();
    Task<List<Keuze>> GetProducttypesAsync();
    Task<List<Keuze>> GetArtikelenAsync();
    Task<List<Keuze>> GetHistorieSoortenAsync();
    Task<List<Keuze>> GetKlantenAsync();
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenProductRepository : IProductRepository
{
    public bool Beschikbaar => false;
    public Task<HashSet<string>> KolommenAsync() => Task.FromResult(new HashSet<string>());
    public Task<List<ProductNaam>> ZoekProductnamenAsync(string? zoek, int? productTypeId)
        => Task.FromResult(new List<ProductNaam>());
    public Task<int> NieuwProductnaamAsync(string naam, string omschrijving, int? productTypeId)
        => Task.FromResult(0);
    public Task BewaarProductnaamAsync(int productNameId, string naam, string omschrijving, int? productTypeId)
        => Task.CompletedTask;
    public Task<string?> ProductnaamInGebruikAsync(int productNameId)
        => Task.FromResult<string?>("Geen live database.");
    public Task VerwijderProductnaamAsync(int productNameId) => Task.CompletedTask;
    public Task<List<ProductAttribuut>> GetTypeAttributenAsync(int productNameId)
        => Task.FromResult(new List<ProductAttribuut>());
    public Task<int> BewaarTypeAttribuutAsync(int productNameId, ProductAttribuut attribuut, int? door)
        => Task.FromResult(0);
    public Task VerwijderTypeAttribuutAsync(int productNameAttributeId) => Task.CompletedTask;
    public Task<List<ProductRegel>> GetProductenAsync(int productNameId, bool inclusiefGewist)
        => Task.FromResult(new List<ProductRegel>());
    public Task<(int ProductId, int ProductNameId)?> ZoekOpSerienummerAsync(string serienummer)
        => Task.FromResult<(int, int)?>(null);
    public Task<(string Type, string Serienummer, string Bouwjaar)?> GetLabelGegevensAsync(int productId)
        => Task.FromResult<(string, string, string)?>(null);
    public Task<Dictionary<string, object?>> GetVeldenAsync(int productId)
        => Task.FromResult(new Dictionary<string, object?>());
    public Task UpdateVeldAsync(int productId, string kolom, object? waarde) => Task.CompletedTask;
    public Task<int> NieuwProductAsync(int productNameId, string serienummer, int? klantId,
                                       int? werkorderId, int? door) => Task.FromResult(0);
    public Task<List<int>> NieuweProductenAsync(int productNameId, int aantal, int? klantId,
                                                int? werkorderId, int? door)
        => Task.FromResult(new List<int>());
    public Task WijzigVeldVoorAsync(IEnumerable<int> productIds, string kolom, object? waarde)
        => Task.CompletedTask;
    public Task ZetGewistAsync(int productId, bool gewist) => Task.CompletedTask;
    public Task<List<ProductGebeurtenis>> GetHistorieAsync(int productId)
        => Task.FromResult(new List<ProductGebeurtenis>());
    public Task VoegHistorieToeAsync(int productId, string gebeurtenis, int? soortId, int? door)
        => Task.CompletedTask;
    public Task<List<ProductAttribuut>> GetAttributenAsync(int productId)
        => Task.FromResult(new List<ProductAttribuut>());
    public Task<int> BewaarAttribuutAsync(int productId, ProductAttribuut attribuut, int? door)
        => Task.FromResult(0);
    public Task VerwijderAttribuutAsync(int productAttributeId) => Task.CompletedTask;
    public Task<int> SyncAttributenAsync(int productId, int productNameId, int? door) => Task.FromResult(0);
    public Task<List<SamenstellingRegel>> GetOnderdelenAsync(int productId)
        => Task.FromResult(new List<SamenstellingRegel>());
    public Task<SamenstellingRegel?> GetGebruiktInAsync(int productId)
        => Task.FromResult<SamenstellingRegel?>(null);
    public Task KoppelAsync(int onderdeelId, int? ouderId) => Task.CompletedTask;
    public Task<List<SamenstellingRegel>> ZoekVrijeExemplarenAsync(string zoek, int max)
        => Task.FromResult(new List<SamenstellingRegel>());
    public Task<List<Keuze>> GetStatussenAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetProducttypesAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetArtikelenAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetHistorieSoortenAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<Keuze>> GetKlantenAsync() => Task.FromResult(new List<Keuze>());
}

/// <summary>
/// Producten en producttypen uit de tabellen Producten, ProductNames,
/// ProductStatus, ProductHistorie en ProductAttributes.
/// </summary>
public class MySqlProductRepository : IProductRepository
{
    private readonly string _cs;
    private HashSet<string>? _kolommen;

    // Opzoeklijstjes veranderen zelden; één keer ophalen per app-start is genoeg.
    private List<Keuze>? _types;
    private List<Keuze>? _historieSoorten;

    public MySqlProductRepository(string connectionString) => _cs = connectionString;

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
            "WHERE table_schema = DATABASE() AND table_name = 'Producten' ORDER BY ordinal_position", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) set.Add(r.GetString(0));
        return _kolommen = set;
    }

    /// <summary>De eerste kolomnaam die echt bestaat.</summary>
    private async Task<string> Kolom(params string[] kandidaten)
    {
        var kolommen = await KolommenAsync();
        return kandidaten.FirstOrDefault(kolommen.Contains) ?? kandidaten[0];
    }

    // ============================================================
    //  Producttypen (ProductNames)
    // ============================================================
    public async Task<List<ProductNaam>> ZoekProductnamenAsync(string? zoek, int? productTypeId)
    {
        var lijst = new List<ProductNaam>();

        var delen = new List<string>();
        if (!string.IsNullOrWhiteSpace(zoek))
            delen.Add("(n.`ProductName` LIKE @z OR n.`Omschrijving` LIKE @z)");
        if (productTypeId is not null) delen.Add("n.`ProductTypeID` = @t");
        var where = delen.Count > 0 ? "WHERE " + string.Join(" AND ", delen) : "";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT n.`ProductNameID`, n.`ProductName`, n.`Omschrijving`, n.`ProductTypeID` " +
            $"  FROM `ProductNames` n {where} " +
            "  ORDER BY n.`ProductName`", cn);
        if (!string.IsNullOrWhiteSpace(zoek)) cmd.Parameters.AddWithValue("@z", "%" + zoek.Trim() + "%");
        if (productTypeId is not null) cmd.Parameters.AddWithValue("@t", productTypeId.Value);

        var types = await GetProducttypesAsync();

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var typeId = r.IsDBNull(3) ? (int?)null : Convert.ToInt32(r.GetValue(3));
            var code = typeId is null ? "" : types.FirstOrDefault(t => t.Id == typeId)?.Naam ?? "";

            lijst.Add(new ProductNaam(
                r.GetInt32(0),
                r.IsDBNull(1) ? "" : r.GetString(1),
                r.IsDBNull(2) ? "" : r.GetString(2),
                code,
                typeId));
        }
        return lijst;
    }

    public async Task<int> NieuwProductnaamAsync(string naam, string omschrijving, int? productTypeId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProductNames` SET `ProductName` = @naam, `Omschrijving` = @oms, " +
            "`ProductTypeID` = @type", cn);
        cmd.Parameters.AddWithValue("@naam", naam ?? "");
        cmd.Parameters.AddWithValue("@oms", omschrijving ?? "");
        cmd.Parameters.AddWithValue("@type", (object?)productTypeId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    public async Task BewaarProductnaamAsync(int productNameId, string naam, string omschrijving,
                                             int? productTypeId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `ProductNames` SET `ProductName` = @naam, `Omschrijving` = @oms, " +
            "`ProductTypeID` = @type WHERE `ProductNameID` = @id", cn);
        cmd.Parameters.AddWithValue("@naam", naam ?? "");
        cmd.Parameters.AddWithValue("@oms", omschrijving ?? "");
        cmd.Parameters.AddWithValue("@type", (object?)productTypeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", productNameId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> ProductnaamInGebruikAsync(int productNameId)
    {
        await using var cn = await OpenAsync();
        var kolommen = await KolommenAsync();
        var filter = kolommen.Contains("isDeleted") ? "AND (`isDeleted` = 0 OR `isDeleted` IS NULL)" : "";

        await using var cmd = new MySqlCommand(
            $"SELECT COUNT(*) FROM `Producten` WHERE `ProductNameID` = @id {filter}", cn);
        cmd.Parameters.AddWithValue("@id", productNameId);
        var res = await cmd.ExecuteScalarAsync();
        var aantal = res is null or DBNull ? 0 : Convert.ToInt64(res);
        return aantal > 0 ? $"Er hangen nog {aantal} niet-gewiste exemplaren aan dit type." : null;
    }

    public async Task VerwijderProductnaamAsync(int productNameId)
    {
        await using var cn = await OpenAsync();

        try
        {
            await using var attr = new MySqlCommand(
                "DELETE FROM `ProductNameAttributes` WHERE `ProductNameID` = @id", cn);
            attr.Parameters.AddWithValue("@id", productNameId);
            await attr.ExecuteNonQueryAsync();
        }
        catch { /* geen attributen */ }

        await using var cmd = new MySqlCommand(
            "DELETE FROM `ProductNames` WHERE `ProductNameID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", productNameId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Standaardattributen van een producttype
    // ============================================================
    public async Task<List<ProductAttribuut>> GetTypeAttributenAsync(int productNameId)
    {
        var lijst = new List<ProductAttribuut>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ProductNameAttributeID`, `Attribute`, `AttributeEenheid`, `Opmerking`, " +
            "       `VerplichteInvoer`, `VraagBijAanmaak` " +
            "  FROM `ProductNameAttributes` WHERE `ProductNameID` = @id " +
            "  ORDER BY `ProductNameAttributeID`", cn);
        cmd.Parameters.AddWithValue("@id", productNameId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string S(int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
            bool B(int i)
            {
                if (r.IsDBNull(i)) return false;
                try { return Convert.ToInt64(r.GetValue(i)) != 0; } catch { return false; }
            }

            lijst.Add(new ProductAttribuut
            {
                Id = r.GetInt32(0),
                ProductId = productNameId,
                Attribuut = S(1),
                Eenheid = S(2),
                Opmerking = S(3),
                Verplicht = B(4),
                VraagBijAanmaak = B(5),
            });
        }
        return lijst;
    }

    public async Task<int> BewaarTypeAttribuutAsync(int productNameId, ProductAttribuut a, int? door)
    {
        const string velden =
            "`ProductNameID` = @naam, `Attribute` = @attr, `AttributeEenheid` = @eenheid, " +
            "`Opmerking` = @opm, `VerplichteInvoer` = @verplicht, `VraagBijAanmaak` = @vraag";

        var sql = a.Id == 0
            ? $"INSERT INTO `ProductNameAttributes` SET {velden}, `AddedBy` = @door, `AddedAt` = NOW()"
            : $"UPDATE `ProductNameAttributes` SET {velden} WHERE `ProductNameAttributeID` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@naam", productNameId);
        cmd.Parameters.AddWithValue("@attr", a.Attribuut ?? "");
        cmd.Parameters.AddWithValue("@eenheid", a.Eenheid ?? "");
        cmd.Parameters.AddWithValue("@opm", a.Opmerking ?? "");
        cmd.Parameters.AddWithValue("@verplicht", a.Verplicht ? 1 : 0);
        cmd.Parameters.AddWithValue("@vraag", a.VraagBijAanmaak ? 1 : 0);
        if (a.Id == 0) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        else cmd.Parameters.AddWithValue("@id", a.Id);

        await cmd.ExecuteNonQueryAsync();
        return a.Id == 0 ? (int)cmd.LastInsertedId : a.Id;
    }

    public async Task VerwijderTypeAttribuutAsync(int productNameAttributeId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `ProductNameAttributes` WHERE `ProductNameAttributeID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", productNameAttributeId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Exemplaren (Producten)
    // ============================================================
    public async Task<List<ProductRegel>> GetProductenAsync(int productNameId, bool inclusiefGewist)
    {
        var kolommen = await KolommenAsync();
        var serie = await Kolom("Serienummer", "SerieNummer");
        var gewistKolom = kolommen.Contains("isDeleted") ? "p.`isDeleted`" : "0";
        var filter = inclusiefGewist || !kolommen.Contains("isDeleted")
            ? ""
            : "AND (p.`isDeleted` = 0 OR p.`isDeleted` IS NULL) ";

        var lijst = new List<ProductRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT p.`ProductenID`, p.`{serie}`, COALESCE(r.`name`, ''), " +
            "        COALESCE(s.`StatusShort`, s.`Status`, ''), COALESCE(p.`Opmerking`, ''), " +
            $"       {gewistKolom} " +
            "  FROM `Producten` p " +
            "  LEFT JOIN `Relations` r ON r.`id_supplier` = p.`KlantID` " +
            "  LEFT JOIN `ProductStatus` s ON s.`ProductStatusID` = p.`ProductStatusID` " +
            $" WHERE p.`ProductNameID` = @naam {filter}" +
            $" ORDER BY p.`{serie}`", cn);
        cmd.Parameters.AddWithValue("@naam", productNameId);

        await using var r2 = await cmd.ExecuteReaderAsync();
        while (await r2.ReadAsync())
        {
            bool B(int i)
            {
                if (r2.IsDBNull(i)) return false;
                try { return Convert.ToInt64(r2.GetValue(i)) != 0; } catch { return false; }
            }
            string S(int i) => r2.IsDBNull(i) ? "" : r2.GetValue(i).ToString() ?? "";

            lijst.Add(new ProductRegel(r2.GetInt32(0), S(1), S(2), S(3), S(4), B(5)));
        }
        return lijst;
    }

    /// <summary>
    /// Zoekt een exemplaar. De DataMatrix op het typeplaatje bevat 'PN' plus het
    /// ProductenID (zo maakt het rapport Typeplaatje_Mpede de code), dus dat vangen
    /// we eerst af. Typt iemand gewoon een serienummer, dan werkt dat ook — met of
    /// zonder voorloopnullen.
    /// </summary>
    public async Task<(int ProductId, int ProductNameId)?> ZoekOpSerienummerAsync(string serienummer)
    {
        if (string.IsNullOrWhiteSpace(serienummer)) return null;
        var tekst = serienummer.Trim();

        await using var cn = await OpenAsync();

        if (tekst.StartsWith("PN", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(tekst[2..], out var scanId))
        {
            await using var opId = new MySqlCommand(
                "SELECT `ProductenID`, `ProductNameID` FROM `Producten` WHERE `ProductenID` = @id", cn);
            opId.Parameters.AddWithValue("@id", scanId);
            await using var rid = await opId.ExecuteReaderAsync();
            if (await rid.ReadAsync())
                return (rid.GetInt32(0), rid.IsDBNull(1) ? 0 : rid.GetInt32(1));
            return null;
        }

        var serie = await Kolom("Serienummer", "SerieNummer");
        await using var cmd = new MySqlCommand(
            $"SELECT `ProductenID`, `ProductNameID` FROM `Producten` " +
            $" WHERE `{serie}` = @sn OR `{serie}` = LPAD(@sn, 6, '0') " +
            $" ORDER BY `ProductenID` DESC LIMIT 1", cn);
        cmd.Parameters.AddWithValue("@sn", tekst);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.GetInt32(0), r.IsDBNull(1) ? 0 : r.GetInt32(1));
    }

    /// <summary>
    /// De drie regels van het typeplaatje. Het serienummer krijgt voorloopnullen
    /// tot zes cijfers, net als in het Access-rapport.
    /// </summary>
    public async Task<(string Type, string Serienummer, string Bouwjaar)?> GetLabelGegevensAsync(int productId)
    {
        var serie = await Kolom("Serienummer", "SerieNummer");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT COALESCE(n.`ProductName`, ''), p.`{serie}`, p.`Bouwjaar` " +
            "  FROM `Producten` p " +
            "  LEFT JOIN `ProductNames` n ON n.`ProductNameID` = p.`ProductNameID` " +
            " WHERE p.`ProductenID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", productId);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        var type = r.IsDBNull(0) ? "" : r.GetString(0);
        var nummer = r.IsDBNull(1) ? "" : (r.GetValue(1).ToString() ?? "").Trim();
        var jaar = r.IsDBNull(2) ? "" : (r.GetValue(2).ToString() ?? "").Trim();

        if (nummer.Length > 0 && nummer.Length < 6 && long.TryParse(nummer, out var getal))
            nummer = getal.ToString("D6");

        return (type, nummer, jaar);
    }

    public async Task<Dictionary<string, object?>> GetVeldenAsync(int productId)
    {
        var velden = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT * FROM `Producten` WHERE `ProductenID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", productId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
            for (var i = 0; i < r.FieldCount; i++)
                velden[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return velden;
    }

    public async Task UpdateVeldAsync(int productId, string kolom, object? waarde)
    {
        var kolommen = await KolommenAsync();
        var echt = kolommen.FirstOrDefault(k => string.Equals(k, kolom, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Kolom '{kolom}' bestaat niet in Producten.");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"UPDATE `Producten` SET `{echt}` = @w WHERE `ProductenID` = @id", cn);
        cmd.Parameters.AddWithValue("@w", waarde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", productId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Nieuw exemplaar. De attributen worden meteen overgenomen uit
    /// ProductNameAttributes, precies zoals de VBA dat deed, en er komt een
    /// eerste historieregel bij.
    /// </summary>
    public async Task<int> NieuwProductAsync(int productNameId, string serienummer, int? klantId,
                                             int? werkorderId, int? door)
    {
        var kolommen = await KolommenAsync();
        var serie = await Kolom("Serienummer", "SerieNummer");

        var sets = new List<string> { "`ProductNameID` = @naam", $"`{serie}` = @sn" };
        if (klantId is not null) sets.Add("`KlantID` = @klant");
        if (werkorderId is not null) sets.Add("`WorkorderID` = @wo");
        if (kolommen.Contains("AanmaakDoor")) sets.Add("`AanmaakDoor` = @door");
        if (kolommen.Contains("Bouwjaar")) sets.Add("`Bouwjaar` = @jaar");
        if (kolommen.Contains("ProductStatusID")) sets.Add("`ProductStatusID` = 1");

        await using var cn = await OpenAsync();

        // Serienummer is een doorlopende reeks over alle producten (zo deed de VBA
        // het ook met DMax). Geen nummer meegegeven? Dan het volgende vrije nummer.
        var nummer = serienummer;
        if (string.IsNullOrWhiteSpace(nummer))
        {
            await using var max = new MySqlCommand(
                $"SELECT COALESCE(MAX(CAST(`{serie}` AS UNSIGNED)), 0) + 1 FROM `Producten`", cn);
            var res = await max.ExecuteScalarAsync();
            var volgend = res is null or DBNull ? 1 : Convert.ToInt64(res);
            nummer = volgend.ToString("D6");
        }

        int id;
        await using (var cmd = new MySqlCommand(
            $"INSERT INTO `Producten` SET {string.Join(", ", sets)}", cn))
        {
            cmd.Parameters.AddWithValue("@naam", productNameId);
            cmd.Parameters.AddWithValue("@sn", nummer);
            if (klantId is not null) cmd.Parameters.AddWithValue("@klant", klantId.Value);
            if (werkorderId is not null) cmd.Parameters.AddWithValue("@wo", werkorderId.Value);
            if (kolommen.Contains("AanmaakDoor"))
                cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            if (kolommen.Contains("Bouwjaar")) cmd.Parameters.AddWithValue("@jaar", DateTime.Today.Year);

            await cmd.ExecuteNonQueryAsync();
            id = (int)cmd.LastInsertedId;
        }

        // attributen overnemen van het producttype
        {
            await using var attr = new MySqlCommand(
                "INSERT INTO `ProductAttributes` " +
                "  (`ProductID`, `Attribute`, `AttributeEenheid`, `Opmerking`, `VerplichteInvoer`, " +
                "   `ProductNameAttributeID`, `AddedBy`, `AddedAt`) " +
                "SELECT @id, a.`Attribute`, a.`AttributeEenheid`, a.`Opmerking`, a.`VerplichteInvoer`, " +
                "       a.`ProductNameAttributeID`, @door, NOW() " +
                "  FROM `ProductNameAttributes` a WHERE a.`ProductNameID` = @naam", cn);
            attr.Parameters.AddWithValue("@id", id);
            attr.Parameters.AddWithValue("@naam", productNameId);
            attr.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            var aantal = await attr.ExecuteNonQueryAsync();
            if (aantal > 0)
                await VoegHistorieToeAsync(id, "Attributes toegevoegd.",
                                           ProductVelden.HistorieAttributen, door);
        }

        await VoegHistorieToeAsync(id, "Product aangemaakt.", ProductVelden.HistorieLevensloop, door);
        return id;
    }

    /// <summary>
    /// Meerdere exemplaren in één keer. De serienummers lopen door; elk exemplaar
    /// krijgt zijn eigen attributen en historieregels, alsof je ze los aanmaakte.
    /// </summary>
    public async Task<List<int>> NieuweProductenAsync(int productNameId, int aantal, int? klantId,
                                                      int? werkorderId, int? door)
    {
        var ids = new List<int>();
        for (var i = 0; i < Math.Clamp(aantal, 1, 100); i++)
            ids.Add(await NieuwProductAsync(productNameId, "", klantId, werkorderId, door));
        return ids;
    }

    /// <summary>Eén veld voor een groep exemplaren tegelijk zetten.</summary>
    public async Task WijzigVeldVoorAsync(IEnumerable<int> productIds, string kolom, object? waarde)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return;

        var kolommen = await KolommenAsync();
        var echt = kolommen.FirstOrDefault(k => string.Equals(k, kolom, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Kolom '{kolom}' bestaat niet in Producten.");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"UPDATE `Producten` SET `{echt}` = @w " +
            $" WHERE `ProductenID` IN ({string.Join(",", ids)})", cn);
        cmd.Parameters.AddWithValue("@w", waarde ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetGewistAsync(int productId, bool gewist)
    {
        var kolommen = await KolommenAsync();
        if (!kolommen.Contains("isDeleted"))
            throw new InvalidOperationException("De tabel Producten heeft geen kolom isDeleted.");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Producten` SET `isDeleted` = @w WHERE `ProductenID` = @id", cn);
        cmd.Parameters.AddWithValue("@w", gewist ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", productId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Historie
    // ============================================================
    public async Task<List<ProductGebeurtenis>> GetHistorieAsync(int productId)
    {
        var lijst = new List<ProductGebeurtenis>();
        var soorten = await GetHistorieSoortenAsync();

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT h.`ProductHistorieID`, h.`Event`, h.`HistorieTypeID`, h.`ToegevoegdOp`, " +
            "       COALESCE(w.`NaamVoluit`, '') " +
            "  FROM `ProductHistorie` h " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = h.`ToegevoegdDoor` " +
            " WHERE h.`ProductID` = @id " +
            " ORDER BY h.`ToegevoegdOp` DESC, h.`ProductHistorieID` DESC", cn);
        cmd.Parameters.AddWithValue("@id", productId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var soortId = r.IsDBNull(2) ? (int?)null : Convert.ToInt32(r.GetValue(2));
            var soort = soortId is null ? "" : soorten.FirstOrDefault(s => s.Id == soortId)?.Naam ?? $"#{soortId}";

            lijst.Add(new ProductGebeurtenis(
                r.GetInt32(0),
                r.IsDBNull(1) ? "" : r.GetString(1),
                soort,
                r.IsDBNull(3) ? null : Convert.ToDateTime(r.GetValue(3)),
                r.IsDBNull(4) ? "" : r.GetString(4)));
        }
        return lijst;
    }

    public async Task VoegHistorieToeAsync(int productId, string gebeurtenis, int? soortId, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProductHistorie` SET `ProductID` = @id, `Event` = @event, " +
            "`HistorieTypeID` = @soort, `ToegevoegdDoor` = @door, `ToegevoegdOp` = NOW()", cn);
        cmd.Parameters.AddWithValue("@id", productId);
        cmd.Parameters.AddWithValue("@event", (gebeurtenis ?? "").Length > 250
            ? gebeurtenis![..250] : gebeurtenis ?? "");
        cmd.Parameters.AddWithValue("@soort", (object?)soortId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Attributen
    // ============================================================
    public async Task<List<ProductAttribuut>> GetAttributenAsync(int productId)
    {
        var lijst = new List<ProductAttribuut>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ProductAttributeID`, `Attribute`, `AttributeValue`, `AttributeEenheid`, " +
            "       `VerplichteInvoer`, `Opmerking` " +
            "  FROM `ProductAttributes` WHERE `ProductID` = @id ORDER BY `Attribute`", cn);
        cmd.Parameters.AddWithValue("@id", productId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string S(int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
            lijst.Add(new ProductAttribuut
            {
                Id = r.GetInt32(0),
                ProductId = productId,
                Attribuut = S(1),
                Waarde = S(2),
                Eenheid = S(3),
                Verplicht = !r.IsDBNull(4) && Convert.ToInt64(r.GetValue(4)) != 0,
                Opmerking = S(5),
            });
        }
        return lijst;
    }

    public async Task<int> BewaarAttribuutAsync(int productId, ProductAttribuut a, int? door)
    {
        const string velden =
            "`ProductID` = @prod, `Attribute` = @attr, `AttributeValue` = @waarde, " +
            "`AttributeEenheid` = @eenheid, `Opmerking` = @opm, `VerplichteInvoer` = @verplicht";

        var sql = a.Id == 0
            ? $"INSERT INTO `ProductAttributes` SET {velden}, `AddedBy` = @door, `AddedAt` = NOW()"
            : $"UPDATE `ProductAttributes` SET {velden} WHERE `ProductAttributeID` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@prod", productId);
        cmd.Parameters.AddWithValue("@attr", a.Attribuut ?? "");
        cmd.Parameters.AddWithValue("@waarde", a.Waarde ?? "");
        cmd.Parameters.AddWithValue("@eenheid", a.Eenheid ?? "");
        cmd.Parameters.AddWithValue("@opm", a.Opmerking ?? "");
        cmd.Parameters.AddWithValue("@verplicht", a.Verplicht ? 1 : 0);
        if (a.Id == 0) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        else cmd.Parameters.AddWithValue("@id", a.Id);

        await cmd.ExecuteNonQueryAsync();
        return a.Id == 0 ? (int)cmd.LastInsertedId : a.Id;
    }

    public async Task VerwijderAttribuutAsync(int productAttributeId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `ProductAttributes` WHERE `ProductAttributeID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", productAttributeId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Vult dit exemplaar aan met attributen die inmiddels bij het producttype zijn
    /// gekomen. Bestaande waarden blijven staan; alleen wat ontbreekt komt erbij.
    /// </summary>
    public async Task<int> SyncAttributenAsync(int productId, int productNameId, int? door)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProductAttributes` " +
            "  (`ProductID`, `Attribute`, `AttributeEenheid`, `Opmerking`, `VerplichteInvoer`, " +
            "   `ProductNameAttributeID`, `AddedBy`, `AddedAt`) " +
            "SELECT @prod, a.`Attribute`, a.`AttributeEenheid`, a.`Opmerking`, a.`VerplichteInvoer`, " +
            "       a.`ProductNameAttributeID`, @door, NOW() " +
            "  FROM `ProductNameAttributes` a " +
            " WHERE a.`ProductNameID` = @naam " +
            "   AND a.`ProductNameAttributeID` NOT IN " +
            "       (SELECT COALESCE(b.`ProductNameAttributeID`, 0) FROM `ProductAttributes` b " +
            "         WHERE b.`ProductID` = @prod)", cn);
        cmd.Parameters.AddWithValue("@prod", productId);
        cmd.Parameters.AddWithValue("@naam", productNameId);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);

        return await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Samenstelling — onderdelen hangen met ParentProductenID aan een exemplaar
    // ============================================================
    private const string SamenstellingSelect =
        "SELECT p.`ProductenID`, p.`ProductNameID`, COALESCE(n.`ProductName`, ''), " +
        "       COALESCE(t.`Afkorting`, ''), COALESCE(n.`Omschrijving`, '') ";

    private static async Task<List<SamenstellingRegel>> LeesSamenstelling(MySqlCommand cmd)
    {
        var lijst = new List<SamenstellingRegel>();

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string S(int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
            lijst.Add(new SamenstellingRegel(
                r.GetInt32(0),
                r.IsDBNull(1) ? 0 : r.GetInt32(1),
                S(2),                       // code (MPR-PCBA-DISP)
                S(5),                       // serienummer
                S(4),                       // omschrijving van het type
                S(3)));                     // afkorting van het producttype
        }
        return lijst;
    }

    public async Task<List<SamenstellingRegel>> GetOnderdelenAsync(int productId)
    {
        var serie = await Kolom("Serienummer", "SerieNummer");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            SamenstellingSelect + $", p.`{serie}` " +
            "  FROM `Producten` p " +
            "  LEFT JOIN `ProductNames` n ON n.`ProductNameID` = p.`ProductNameID` " +
            "  LEFT JOIN `ProductTypes` t ON t.`ProductTypeID` = n.`ProductTypeID` " +
            " WHERE p.`ParentProductenID` = @id " +
            $" ORDER BY n.`ProductName`, p.`{serie}`", cn);
        cmd.Parameters.AddWithValue("@id", productId);
        return await LeesSamenstelling(cmd);
    }

    public async Task<SamenstellingRegel?> GetGebruiktInAsync(int productId)
    {
        var serie = await Kolom("Serienummer", "SerieNummer");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            SamenstellingSelect + $", p.`{serie}` " +
            "  FROM `Producten` kind " +
            "  JOIN `Producten` p ON p.`ProductenID` = kind.`ParentProductenID` " +
            "  LEFT JOIN `ProductNames` n ON n.`ProductNameID` = p.`ProductNameID` " +
            "  LEFT JOIN `ProductTypes` t ON t.`ProductTypeID` = n.`ProductTypeID` " +
            " WHERE kind.`ProductenID` = @id LIMIT 1", cn);
        cmd.Parameters.AddWithValue("@id", productId);

        var lijst = await LeesSamenstelling(cmd);
        return lijst.FirstOrDefault();
    }

    public async Task KoppelAsync(int onderdeelId, int? ouderId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Producten` SET `ParentProductenID` = @ouder WHERE `ProductenID` = @id", cn);
        cmd.Parameters.AddWithValue("@ouder", (object?)ouderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", onderdeelId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Exemplaren zonder ouder, om als onderdeel toe te voegen.</summary>
    public async Task<List<SamenstellingRegel>> ZoekVrijeExemplarenAsync(string zoek, int max)
    {
        var serie = await Kolom("Serienummer", "SerieNummer");
        var kolommen = await KolommenAsync();
        var gewist = kolommen.Contains("isDeleted") ? "AND (p.`isDeleted` = 0 OR p.`isDeleted` IS NULL) " : "";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            SamenstellingSelect + $", p.`{serie}` " +
            "  FROM `Producten` p " +
            "  LEFT JOIN `ProductNames` n ON n.`ProductNameID` = p.`ProductNameID` " +
            "  LEFT JOIN `ProductTypes` t ON t.`ProductTypeID` = n.`ProductTypeID` " +
            " WHERE p.`ParentProductenID` IS NULL " + gewist +
            $"   AND (p.`{serie}` LIKE @z OR n.`ProductName` LIKE @z OR n.`Omschrijving` LIKE @z) " +
            $" ORDER BY n.`ProductName`, p.`{serie}` LIMIT {Math.Clamp(max, 1, 100)}", cn);
        cmd.Parameters.AddWithValue("@z", "%" + (zoek ?? "").Trim() + "%");
        return await LeesSamenstelling(cmd);
    }

    // ============================================================
    //  Keuzelijsten
    // ============================================================

    /// <summary>Statussen op hun eigen sorteervolgorde, met het nummer ervoor zoals in Access.</summary>
    public async Task<List<Keuze>> GetStatussenAsync()
    {
        var lijst = new List<Keuze>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ProductStatusID`, `Status`, `Order` FROM `ProductStatus` " +
            "ORDER BY COALESCE(`Order`, `ProductStatusID`)", cn);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var nummer = r.IsDBNull(2) ? (int?)null : Convert.ToInt32(r.GetValue(2));
            var naam = r.IsDBNull(1) ? "" : r.GetString(1);
            lijst.Add(new Keuze(r.GetInt32(0), nummer is null ? naam : $"{nummer} - {naam}"));
        }
        return lijst;
    }

    /// <summary>Producttypes met hun afkorting (HALF, PROD) — die staat in de lijst links.</summary>
    public async Task<List<Keuze>> GetProducttypesAsync()
        => _types ??= await LijstAsync(
            new[] { "ProductTypes", "ProductType", "Producttype", "ProductTypen" },
            new[] { "ProductTypeID", "ProductTypeId", "id" },
            new[] { "Afkorting", "TypeShort", "Code", "ProductType", "Omschrijving", "Naam", "name" });

    /// <summary>Producttypes met de volledige omschrijving — voor de keuzelijst.</summary>
    public Task<List<Keuze>> GetProducttypeNamenAsync()
        => LijstAsync(new[] { "ProductTypes", "ProductType", "Producttype", "ProductTypen" },
                      new[] { "ProductTypeID", "ProductTypeId", "id" },
                      new[] { "Omschrijving", "ProductType", "Naam", "name", "Afkorting" });

    public Task<List<Keuze>> GetArtikelenAsync()
        => LijstAsync(new[] { "Artikelen", "Artikel", "Articles" },
                      new[] { "ArtikelID", "Artikel-id", "ArtikelId", "id" },
                      new[] { "Artikelnummer", "Artikelcode", "Omschrijving", "Naam", "Description", "name" });

    /// <summary>
    /// Soorten historieregels uit HistorieTypes. Let op: de kolom heet daar
    /// 'HistoryType' — Engels, terwijl de tabelnaam Nederlands is.
    /// </summary>
    public async Task<List<Keuze>> GetHistorieSoortenAsync()
        => _historieSoorten ??= await LijstAsync(
            new[] { "HistorieTypes", "ProductHistorieType", "HistorieType", "ProductHistorieTypes" },
            new[] { "HistorieTypeID", "ProductHistorieTypeID", "id" },
            new[] { "HistoryType", "HistorieType", "Omschrijving", "Naam", "Type", "name" });

    /// <summary>Klanten uit Relations — de volledige lijst, niet afgekapt op 200.</summary>
    public async Task<List<Keuze>> GetKlantenAsync()
    {
        var lijst = new List<Keuze>();
        await using var cn = await OpenAsync();

        // Access filterde deze lijst op Relations.Customer
        var sql = "SELECT `id_supplier`, `name` FROM `Relations` " +
                  "WHERE (`Customer` <> 0 OR `Customer` IS NULL) ORDER BY `name` LIMIT 5000";
        try
        {
            await using var cmd = new MySqlCommand(sql, cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                lijst.Add(new Keuze(r.GetInt32(0), r.IsDBNull(1) ? "(geen naam)" : r.GetString(1)));
        }
        catch
        {
            // geen kolom Customer: dan zonder filter
            await using var cmd = new MySqlCommand(
                "SELECT `id_supplier`, `name` FROM `Relations` ORDER BY `name` LIMIT 5000", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                lijst.Add(new Keuze(r.GetInt32(0), r.IsDBNull(1) ? "(geen naam)" : r.GetString(1)));
        }
        return lijst;
    }

    /// <summary>
    /// Opzoeklijstje voor een tabel waarvan ik de precieze naam niet ken: probeer
    /// een paar gangbare namen en pak daarin de eerste passende id- en naamkolom.
    /// Lukt het niet, dan blijft de lijst leeg en toont het scherm het nummer.
    /// </summary>
    private async Task<List<Keuze>> LijstAsync(string[] tabellen, string[] idNamen, string[] naamNamen)
    {
        foreach (var tabel in tabellen)
        {
            var lijst = new List<Keuze>();
            try
            {
                await using var cn = await OpenAsync();
                await using var cmd = new MySqlCommand($"SELECT * FROM `{tabel}` LIMIT 2000", cn);
                await using var r = await cmd.ExecuteReaderAsync();

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
        return new List<Keuze>();
    }
}
