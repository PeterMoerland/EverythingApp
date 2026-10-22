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

    /// <summary>Is de kolom Bron aanwezig (ProductAttribuutBron_MariaDB.sql)? Anders kan 'Automatisch gevuld uit' niet bewaard worden.</summary>
    Task<bool> HeeftAttribuutBronAsync();
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
    /// <param name="uitkomst">true = geslaagd, false = mislukt, null = neutraal (kleur in de historie).</param>
    Task VoegHistorieToeAsync(int productId, string gebeurtenis, int? soortId, int? door, bool? uitkomst = null);
    /// <summary>Historieregels definitief wissen (opschonen, alleen Directie; de rechtencontrole zit in het scherm).
    /// Geeft het aantal gewiste regels.</summary>
    Task<int> VerwijderHistorieAsync(IReadOnlyCollection<int> historieIds);

    /// <summary>Indeling van halffabricaten (Motor, PCBA, Kabel …) en de koppeling naar een artikel.</summary>
    Task<List<Keuze>> GetProductgroepenAsync();
    Task ZetProductnaamExtraAsync(int productNameId, int? groepId, int? artikelId);
    /// <summary>Stap (ProductStap-code) in Half-fabricaten tonen of weglaten voor dit halffabricaat.</summary>
    Task ZetStapZichtbaarAsync(int productNameId, string stap, bool toon);

    /// <summary>Welke techniek er op een halffabricaat zit (ProductTechniek.Drives/Esp32/Stm32).</summary>
    Task<HashSet<string>> GetTechniekenAsync(int productNameId);
    Task ZetTechniekAsync(int productNameId, string techniek, bool aan);

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
    /// <summary>
    /// Het halffabricaat dat dit drive-serienummer bij zijn eigen test kreeg: een exemplaar met een attribuut waarvan de
    /// bron 'drive:n:serienummer' of 'can:n:serienummer' is en de waarde dit nummer. Null = onbekend.
    /// </summary>
    Task<SamenstellingRegel?> ZoekOpDriveSerienummerAsync(string serienummer);
    Task<SamenstellingRegel?> GetGebruiktInAsync(int productId);

    /// <summary>Hangt een exemplaar onder een ander exemplaar.</summary>
    Task KoppelAsync(int onderdeelId, int? ouderId);

    /// <summary>Exemplaren die nog vrij zijn (geen ouder), om als onderdeel te kiezen;
    /// met <paramref name="productNameId"/> alleen van dat halffabricaat.</summary>
    /// <summary>
    /// Exemplaren zonder ouder, om in te bouwen. Afgekeurde (ProductStatus FAILED) niet, tenzij
    /// <paramref name="ookAfgekeurd"/>: dan alleen om te kunnen zeggen waarom een serienummer niet kan.
    /// </summary>
    Task<List<SamenstellingRegel>> ZoekVrijeExemplarenAsync(string zoek, int max, int? productNameId = null, bool ookAfgekeurd = false);

    // --- stuklijst per producttype (ProductSamenstelling_MariaDB.sql) ---
    /// <summary>Welke halffabricaten er in dit producttype gaan, en hoeveel. Lege lijst als de tabel ontbreekt.</summary>
    Task<List<StuklijstRegel>> GetStuklijstAsync(int productNameId);
    Task<int> BewaarStuklijstRegelAsync(StuklijstRegel regel, int? door);
    Task VerwijderStuklijstRegelAsync(int samenstellingId);
    Task<bool> HeeftStuklijstAsync();

    // --- keuzelijsten ---
    Task<List<Keuze>> GetStatussenAsync();
    /// <summary>ProductStatusID's waarvan Status óf StatusShort gelijk is aan <paramref name="naam"/> (bijv. 'OK').</summary>
    Task<HashSet<int>> GetStatusIdsMetNaamAsync(string naam);
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
    public Task<bool> HeeftAttribuutBronAsync() => Task.FromResult(false);
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
    public Task<List<Keuze>> GetProductgroepenAsync() => Task.FromResult(new List<Keuze>());
    public Task ZetProductnaamExtraAsync(int productNameId, int? groepId, int? artikelId) => Task.CompletedTask;
    public Task ZetStapZichtbaarAsync(int productNameId, string stap, bool toon) => Task.CompletedTask;
    public Task<HashSet<string>> GetTechniekenAsync(int productNameId) => Task.FromResult(new HashSet<string>());
    public Task ZetTechniekAsync(int productNameId, string techniek, bool aan) => Task.CompletedTask;
    public Task VoegHistorieToeAsync(int productId, string gebeurtenis, int? soortId, int? door, bool? uitkomst = null)
        => Task.CompletedTask;
    public Task<int> VerwijderHistorieAsync(IReadOnlyCollection<int> historieIds) => Task.FromResult(0);
    public Task<List<ProductAttribuut>> GetAttributenAsync(int productId)
        => Task.FromResult(new List<ProductAttribuut>());
    public Task<int> BewaarAttribuutAsync(int productId, ProductAttribuut attribuut, int? door)
        => Task.FromResult(0);
    public Task VerwijderAttribuutAsync(int productAttributeId) => Task.CompletedTask;
    public Task<int> SyncAttributenAsync(int productId, int productNameId, int? door) => Task.FromResult(0);
    public Task<SamenstellingRegel?> ZoekOpDriveSerienummerAsync(string serienummer) => Task.FromResult<SamenstellingRegel?>(null);
    public Task<List<SamenstellingRegel>> GetOnderdelenAsync(int productId)
        => Task.FromResult(new List<SamenstellingRegel>());
    public Task<SamenstellingRegel?> GetGebruiktInAsync(int productId)
        => Task.FromResult<SamenstellingRegel?>(null);
    public Task KoppelAsync(int onderdeelId, int? ouderId) => Task.CompletedTask;
    public Task<List<SamenstellingRegel>> ZoekVrijeExemplarenAsync(string zoek, int max, int? productNameId = null, bool ookAfgekeurd = false)
        => Task.FromResult(new List<SamenstellingRegel>());
    public Task<List<StuklijstRegel>> GetStuklijstAsync(int productNameId) => Task.FromResult(new List<StuklijstRegel>());
    public Task<int> BewaarStuklijstRegelAsync(StuklijstRegel regel, int? door) => Task.FromResult(0);
    public Task VerwijderStuklijstRegelAsync(int samenstellingId) => Task.CompletedTask;
    public Task<bool> HeeftStuklijstAsync() => Task.FromResult(false);
    public Task<List<Keuze>> GetStatussenAsync() => Task.FromResult(new List<Keuze>());
    public Task<HashSet<int>> GetStatusIdsMetNaamAsync(string naam) => Task.FromResult(new HashSet<int>());
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

    /// <summary>KlantID voor een nieuw exemplaar als er geen klant is meegegeven (Productie:StandaardKlantId, MDE zelf).</summary>
    private readonly int? _standaardKlantId;

    public MySqlProductRepository(string connectionString, int? standaardKlantId = null)
    {
        _cs = connectionString;
        _standaardKlantId = standaardKlantId;
    }

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

    // Is de kolom 'Bron' al toegevoegd (ProductAttribuutBron_MariaDB.sql)? Zo niet,
    // dan werkt alles als vanouds, alleen zonder automatische bronnen.
    private bool? _heeftBron;

    public async Task<bool> HeeftAttribuutBronAsync()
    {
        await using var cn = await OpenAsync();
        return await HeeftBronAsync(cn);
    }

    private async Task<bool> HeeftBronAsync(MySqlConnection cn)
    {
        // Alleen 'ja' onthouden: wordt het script uitgevoerd terwijl de app draait,
        // dan pikt de volgende aanroep dat meteen op zonder herstart.
        if (_heeftBron == true) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND LOWER(table_name) = 'productnameattributes' AND LOWER(column_name) = 'bron'", cn);
        _heeftBron = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        return _heeftBron.Value;
    }

    // ============================================================
    //  Producttypen (ProductNames)
    // ============================================================
    // Zijtabel ProductNameExtra (groep + artikel) aanwezig? Alleen 'ja' onthouden.
    private bool? _heeftExtra;
    private async Task<bool> HeeftExtraAsync(MySqlConnection cn)
    {
        if (_heeftExtra == true) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables " +
            "WHERE table_schema = DATABASE() AND table_name IN ('ProductNameExtra', 'ProductGroepen')", cn);
        _heeftExtra = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 2;
        return _heeftExtra.Value;
    }

    // Kolommen Toon… (stappen in Half-fabricaten) in ProductNameExtra? Alleen 'ja' onthouden.
    private bool? _heeftStappen;
    private async Task<bool> HeeftStappenAsync(MySqlConnection cn)
    {
        if (_heeftStappen == true) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = 'ProductNameExtra' AND column_name IN (" +
            string.Join(",", ProductStap.Alle.Select(s => $"'{ProductStap.Kolom(s.Code)}'")) + ")", cn);
        _heeftStappen = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == ProductStap.Alle.Length;
        return _heeftStappen.Value;
    }

    public async Task<List<ProductNaam>> ZoekProductnamenAsync(string? zoek, int? productTypeId)
    {
        var lijst = new List<ProductNaam>();
        await using var cn = await OpenAsync();
        var extra = await HeeftExtraAsync(cn);
        var stappen = extra && await HeeftStappenAsync(cn);

        var delen = new List<string>();
        if (!string.IsNullOrWhiteSpace(zoek))
            delen.Add(extra
                ? "(n.`ProductName` LIKE @z OR n.`Omschrijving` LIKE @z OR a.`ArtikelCodeMDE` LIKE @z OR CAST(a.`ArtikelID` AS CHAR) = @zk OR g.`Naam` LIKE @z)"
                : "(n.`ProductName` LIKE @z OR n.`Omschrijving` LIKE @z)");
        if (productTypeId is not null) delen.Add("n.`ProductTypeID` = @t");
        var where = delen.Count > 0 ? "WHERE " + string.Join(" AND ", delen) : "";

        // Groep en artikel komen uit de zijtabel; zonder die tabellen blijven de kolommen leeg.
        var select = extra
            ? ", x.`GroepID`, COALESCE(g.`Naam`, ''), x.`ArtikelID`, COALESCE(a.`ArtikelCodeMDE`, ''), COALESCE(a.`OmschrijvingNL`, '')"
            : ", NULL, '', NULL, '', ''";
        // Stappen: per ProductStap-code één kolom Toon…; ontbreekt de kolom, dan alles tonen.
        select += stappen
            ? string.Concat(ProductStap.Alle.Select(s => $", COALESCE(x.`{ProductStap.Kolom(s.Code)}`, {ProductStap.Standaard(s.Code)})"))
            : string.Concat(ProductStap.Alle.Select(s => $", {ProductStap.Standaard(s.Code)}"));
        var joins = extra
            ? "  LEFT JOIN `ProductNameExtra` x ON x.`ProductNameID` = n.`ProductNameID` " +
              "  LEFT JOIN `ProductGroepen` g ON g.`GroepID` = x.`GroepID` " +
              "  LEFT JOIN `Artikelen` a ON a.`ArtikelID` = x.`ArtikelID` "
            : "";

        await using var cmd = new MySqlCommand(
            $"SELECT n.`ProductNameID`, n.`ProductName`, n.`Omschrijving`, n.`ProductTypeID`{select} " +
            $"  FROM `ProductNames` n {joins}{where} " +
            "  ORDER BY n.`ProductName`", cn);
        if (!string.IsNullOrWhiteSpace(zoek))
        {
            cmd.Parameters.AddWithValue("@z", "%" + zoek.Trim() + "%");
            cmd.Parameters.AddWithValue("@zk", zoek.Trim().TrimStart('#'));
        }
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
                typeId)
            {
                GroepId = r.IsDBNull(4) ? null : Convert.ToInt32(r.GetValue(4)),
                Groep = r.IsDBNull(5) ? "" : r.GetString(5),
                ArtikelId = r.IsDBNull(6) ? null : Convert.ToInt32(r.GetValue(6)),
                ArtikelCode = r.IsDBNull(7) ? "" : r.GetString(7),
                ArtikelOmschrijving = r.IsDBNull(8) ? "" : r.GetString(8),
                VerborgenStappen = ProductStap.Alle
                    .Select((s, i) => (s.Code, Toon: r.IsDBNull(9 + i) || Convert.ToInt32(r.GetValue(9 + i)) != 0))
                    .Where(x => !x.Toon).Select(x => x.Code)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
            });
        }
        return lijst;
    }

    public async Task<List<Keuze>> GetProductgroepenAsync()
    {
        var lijst = new List<Keuze>();
        await using var cn = await OpenAsync();
        if (!await HeeftExtraAsync(cn)) return lijst;
        await using var cmd = new MySqlCommand("SELECT `GroepID`, `Naam` FROM `ProductGroepen` ORDER BY `Volgorde`, `Naam`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new Keuze(r.GetInt32(0), r.GetString(1)));
        return lijst;
    }

    public async Task ZetProductnaamExtraAsync(int productNameId, int? groepId, int? artikelId)
    {
        await using var cn = await OpenAsync();
        if (!await HeeftExtraAsync(cn))
            throw new InvalidOperationException("De tabellen ProductGroepen/ProductNameExtra ontbreken nog: voer ProductGroepen_MariaDB.sql uit.");
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProductNameExtra` (`ProductNameID`, `GroepID`, `ArtikelID`) VALUES (@n, @g, @a) " +
            "ON DUPLICATE KEY UPDATE `GroepID` = VALUES(`GroepID`), `ArtikelID` = VALUES(`ArtikelID`)", cn);
        cmd.Parameters.AddWithValue("@n", productNameId);
        cmd.Parameters.AddWithValue("@g", (object?)groepId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@a", (object?)artikelId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetStapZichtbaarAsync(int productNameId, string stap, bool toon)
    {
        if (!ProductStap.Alle.Any(s => s.Code == stap)) throw new ArgumentException($"Onbekende stap '{stap}'.");
        await using var cn = await OpenAsync();
        if (!await HeeftExtraAsync(cn) || !await HeeftStappenAsync(cn))
            throw new InvalidOperationException("De kolommen Toon… ontbreken nog in ProductNameExtra: voer ProductGroepen_MariaDB.sql (opnieuw) uit.");
        var kolom = ProductStap.Kolom(stap);
        // Rij aanmaken als die er nog niet is (groep/artikel blijven dan leeg), anders alleen de vlag zetten.
        await using var cmd = new MySqlCommand(
            $"INSERT INTO `ProductNameExtra` (`ProductNameID`, `{kolom}`) VALUES (@n, @t) " +
            $"ON DUPLICATE KEY UPDATE `{kolom}` = VALUES(`{kolom}`)", cn);
        cmd.Parameters.AddWithValue("@n", productNameId);
        cmd.Parameters.AddWithValue("@t", toon ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
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
        var bron = await HeeftBronAsync(cn) ? "`Bron`" : "NULL";
        await using var cmd = new MySqlCommand(
            "SELECT `ProductNameAttributeID`, `Attribute`, `AttributeEenheid`, `Opmerking`, " +
            $"       `VerplichteInvoer`, `VraagBijAanmaak`, {bron} " +
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
                Bron = S(6),
            });
        }
        return lijst;
    }

    public async Task<int> BewaarTypeAttribuutAsync(int productNameId, ProductAttribuut a, int? door)
    {
        await using var cn = await OpenAsync();
        var metBron = await HeeftBronAsync(cn);

        var velden =
            "`ProductNameID` = @naam, `Attribute` = @attr, `AttributeEenheid` = @eenheid, " +
            "`Opmerking` = @opm, `VerplichteInvoer` = @verplicht, `VraagBijAanmaak` = @vraag" +
            (metBron ? ", `Bron` = @bron" : "");

        var sql = a.Id == 0
            ? $"INSERT INTO `ProductNameAttributes` SET {velden}, `AddedBy` = @door, `AddedAt` = NOW()"
            : $"UPDATE `ProductNameAttributes` SET {velden} WHERE `ProductNameAttributeID` = @id";

        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@naam", productNameId);
        cmd.Parameters.AddWithValue("@attr", a.Attribuut ?? "");
        cmd.Parameters.AddWithValue("@eenheid", a.Eenheid ?? "");
        cmd.Parameters.AddWithValue("@opm", a.Opmerking ?? "");
        cmd.Parameters.AddWithValue("@verplicht", a.Verplicht ? 1 : 0);
        cmd.Parameters.AddWithValue("@vraag", a.VraagBijAanmaak ? 1 : 0);
        if (metBron) cmd.Parameters.AddWithValue("@bron", string.IsNullOrWhiteSpace(a.Bron) ? DBNull.Value : a.Bron.Trim());
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
        // Project bestaat pas na Producten_Project_MariaDB.sql.
        var projectKolom = kolommen.Contains("Project") ? "COALESCE(p.`Project`, '')" : "''";
        var filter = inclusiefGewist || !kolommen.Contains("isDeleted")
            ? ""
            : "AND (p.`isDeleted` = 0 OR p.`isDeleted` IS NULL) ";

        var lijst = new List<ProductRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT p.`ProductenID`, p.`{serie}`, COALESCE(r.`name`, ''), " +
            "        COALESCE(s.`StatusShort`, s.`Status`, ''), COALESCE(p.`Opmerking`, ''), " +
            $"       {gewistKolom}, p.`ProductStatusID`, {projectKolom} " +
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

            lijst.Add(new ProductRegel(r2.GetInt32(0), S(1), S(2), S(3), S(4), B(5),
                                       r2.IsDBNull(6) ? null : Convert.ToInt32(r2.GetValue(6)), S(7)));
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

        // Geen klant opgegeven: het exemplaar is (nog) van MDE zelf.
        klantId ??= _standaardKlantId;

        var sets = new List<string> { "`ProductNameID` = @naam", $"`{serie}` = @sn" };
        if (klantId is not null) sets.Add("`KlantID` = @klant");
        if (werkorderId is not null) sets.Add("`WorkorderID` = @wo");
        if (kolommen.Contains("AanmaakDoor")) sets.Add("`AanmaakDoor` = @door");
        if (kolommen.Contains("Bouwjaar")) sets.Add("`Bouwjaar` = @jaar");
        if (kolommen.Contains("ProductStatusID")) sets.Add("`ProductStatusID` = 1");

        await using var cn = await OpenAsync();

        // Serienummers lopen per halffabricaat/producttype: '65' bestaat bij de RZ-print
        // én bij de display-print. Geen nummer meegegeven? Dan het volgende vrije
        // nummer binnen dít producttype (gewiste exemplaren tellen mee, anders krijg
        // je een nummer dat al eens is uitgegeven).
        var nummer = serienummer;
        if (string.IsNullOrWhiteSpace(nummer))
        {
            await using var max = new MySqlCommand(
                $"SELECT COALESCE(MAX(CAST(`{serie}` AS UNSIGNED)), 0) + 1 FROM `Producten` WHERE `ProductNameID` = @naam", cn);
            max.Parameters.AddWithValue("@naam", productNameId);
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
        var uitkomst = await HeeftUitkomstAsync(cn) ? "h.`Uitkomst`" : "NULL";
        await using var cmd = new MySqlCommand(
            "SELECT h.`ProductHistorieID`, h.`Event`, h.`HistorieTypeID`, h.`ToegevoegdOp`, " +
            $"       COALESCE(w.`NaamVoluit`, ''), {uitkomst} " +
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
                r.IsDBNull(4) ? "" : r.GetString(4),
                r.IsDBNull(5) ? null : Convert.ToInt32(r.GetValue(5)) != 0));
        }
        return lijst;
    }

    public async Task VoegHistorieToeAsync(int productId, string gebeurtenis, int? soortId, int? door, bool? uitkomst = null)
    {
        await using var cn = await OpenAsync();
        var metUitkomst = uitkomst is not null && await HeeftUitkomstAsync(cn);
        await using var cmd = new MySqlCommand(
            "INSERT INTO `ProductHistorie` SET `ProductID` = @id, `Event` = @event, " +
            "`HistorieTypeID` = @soort, `ToegevoegdDoor` = @door, `ToegevoegdOp` = NOW()" +
            (metUitkomst ? ", `Uitkomst` = @uitkomst" : ""), cn);
        cmd.Parameters.AddWithValue("@id", productId);
        cmd.Parameters.AddWithValue("@event", (gebeurtenis ?? "").Length > 250
            ? gebeurtenis![..250] : gebeurtenis ?? "");
        cmd.Parameters.AddWithValue("@soort", (object?)soortId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        if (metUitkomst) cmd.Parameters.AddWithValue("@uitkomst", uitkomst!.Value ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> VerwijderHistorieAsync(IReadOnlyCollection<int> historieIds)
    {
        var ids = historieIds.Distinct().ToList();
        if (ids.Count == 0) return 0;
        var namen = ids.Select((_, i) => "@h" + i).ToList();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"DELETE FROM `ProductHistorie` WHERE `ProductHistorieID` IN ({string.Join(", ", namen)})", cn);
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue(namen[i], ids[i]);
        return await cmd.ExecuteNonQueryAsync();
    }

    // Kolom Uitkomst aanwezig? (ProductTechniek_MariaDB.sql) Alleen 'ja' wordt onthouden.
    private bool? _heeftUitkomst;
    private async Task<bool> HeeftUitkomstAsync(MySqlConnection cn)
    {
        if (_heeftUitkomst == true) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = 'ProductHistorie' AND LOWER(column_name) = 'uitkomst'", cn);
        _heeftUitkomst = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        return _heeftUitkomst.Value;
    }

    // ============================================================
    //  Technieken op een halffabricaat (ProductTechnieken)
    // ============================================================
    public async Task<HashSet<string>> GetTechniekenAsync(int productNameId)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT `Techniek` FROM `ProductTechnieken` WHERE `ProductNameID` = @n", cn);
            cmd.Parameters.AddWithValue("@n", productNameId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) set.Add(r.GetString(0));
        }
        catch (MySqlException ex) when (ex.Message.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            // Tabel nog niet aangemaakt: dan gedragen we ons als 'onbekend' (lege set).
        }
        return set;
    }

    public async Task ZetTechniekAsync(int productNameId, string techniek, bool aan)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(aan
            ? "INSERT IGNORE INTO `ProductTechnieken` (`ProductNameID`, `Techniek`) VALUES (@n, @t)"
            : "DELETE FROM `ProductTechnieken` WHERE `ProductNameID` = @n AND `Techniek` = @t", cn);
        cmd.Parameters.AddWithValue("@n", productNameId);
        cmd.Parameters.AddWithValue("@t", techniek);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Attributen
    // ============================================================
    public async Task<List<ProductAttribuut>> GetAttributenAsync(int productId)
    {
        var lijst = new List<ProductAttribuut>();
        await using var cn = await OpenAsync();
        // De Bron komt van het standaardattribuut waar dit exemplaar-attribuut van afstamt.
        var bron = await HeeftBronAsync(cn) ? "s.`Bron`" : "NULL";
        await using var cmd = new MySqlCommand(
            "SELECT a.`ProductAttributeID`, a.`Attribute`, a.`AttributeValue`, a.`AttributeEenheid`, " +
            $"       a.`VerplichteInvoer`, a.`Opmerking`, a.`ProductNameAttributeID`, {bron} " +
            "  FROM `ProductAttributes` a " +
            "  LEFT JOIN `ProductNameAttributes` s ON s.`ProductNameAttributeID` = a.`ProductNameAttributeID` " +
            " WHERE a.`ProductID` = @id ORDER BY a.`Attribute`", cn);
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
                SjabloonId = r.IsDBNull(6) ? null : Convert.ToInt32(r.GetValue(6)),
                Bron = S(7),
            });
        }
        return lijst;
    }

    public async Task<int> BewaarAttribuutAsync(int productId, ProductAttribuut a, int? door)
    {
        const string velden =
            "`ProductID` = @prod, `Attribute` = @attr, `AttributeValue` = @waarde, " +
            "`AttributeEenheid` = @eenheid, `Opmerking` = @opm, `VerplichteInvoer` = @verplicht";

        // Nieuw attribuut dat van een standaardattribuut afstamt: de koppeling meenemen,
        // dan weet de app later nog waar het vandaan komt (Bron, sync).
        var sql = a.Id == 0
            ? $"INSERT INTO `ProductAttributes` SET {velden}, `ProductNameAttributeID` = @sjabloon, `AddedBy` = @door, `AddedAt` = NOW()"
            : $"UPDATE `ProductAttributes` SET {velden} WHERE `ProductAttributeID` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        if (a.Id == 0) cmd.Parameters.AddWithValue("@sjabloon", (object?)a.SjabloonId ?? DBNull.Value);
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
    /// <summary>Is exemplaar p afgekeurd (ProductStatus 'Product afgekeurd', StatusShort FAILED)? Mag niet ingebouwd worden.</summary>
    private const string AfgekeurdTest =
        "EXISTS (SELECT 1 FROM `ProductStatus` ps WHERE ps.`ProductStatusID` = p.`ProductStatusID` AND ps.`StatusShort` = 'FAILED')";

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
                S(3),                       // afkorting van het producttype
                r.FieldCount > 6 && !r.IsDBNull(6) && Convert.ToInt64(r.GetValue(6)) != 0));   // afgekeurd
        }
        return lijst;
    }

    public async Task<List<SamenstellingRegel>> GetOnderdelenAsync(int productId)
    {
        var serie = await Kolom("Serienummer", "SerieNummer");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            SamenstellingSelect + $", p.`{serie}`, {AfgekeurdTest} " +
            "  FROM `Producten` p " +
            "  LEFT JOIN `ProductNames` n ON n.`ProductNameID` = p.`ProductNameID` " +
            "  LEFT JOIN `ProductTypes` t ON t.`ProductTypeID` = n.`ProductTypeID` " +
            " WHERE p.`ParentProductenID` = @id " +
            $" ORDER BY n.`ProductName`, p.`{serie}`", cn);
        cmd.Parameters.AddWithValue("@id", productId);
        return await LeesSamenstelling(cmd);
    }

    public async Task<SamenstellingRegel?> ZoekOpDriveSerienummerAsync(string serienummer)
    {
        if (string.IsNullOrWhiteSpace(serienummer)) return null;
        var serie = await Kolom("Serienummer", "SerieNummer");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            SamenstellingSelect + $", p.`{serie}`, {AfgekeurdTest} " +
            "  FROM `Producten` p " +
            "  LEFT JOIN `ProductNames` n ON n.`ProductNameID` = p.`ProductNameID` " +
            "  LEFT JOIN `ProductTypes` t ON t.`ProductTypeID` = n.`ProductTypeID` " +
            " WHERE p.`ProductenID` = (SELECT a.`ProductID` FROM `ProductAttributes` a " +
            "                            JOIN `ProductNameAttributes` na ON na.`ProductNameAttributeID` = a.`ProductNameAttributeID` " +
            "                           WHERE (na.`Bron` LIKE 'drive:%:serienummer' OR na.`Bron` LIKE 'can:%:serienummer') " +
            // Staat hetzelfde serienummer bij meer printen (drive omgezet naar een andere print), dan telt de laatst
            // vastgelegde: daar zit de drive nu. Voorheen won het hoogste ProductID, ook als dat een oude koppeling was.
            "                             AND TRIM(a.`AttributeValue`) = @sn ORDER BY a.`last_updated` DESC, a.`ProductID` DESC LIMIT 1)", cn);
        cmd.Parameters.AddWithValue("@sn", serienummer.Trim());
        return (await LeesSamenstelling(cmd)).FirstOrDefault();
    }

    public async Task<SamenstellingRegel?> GetGebruiktInAsync(int productId)
    {
        var serie = await Kolom("Serienummer", "SerieNummer");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            SamenstellingSelect + $", p.`{serie}`, {AfgekeurdTest} " +
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
        if (ouderId is not null)
        {
            // Een afgekeurd exemplaar hoort niet in een product (melding gebruiker 22-10-2026); ook hier, niet alleen in de kiezer.
            await using var test = new MySqlCommand($"SELECT {AfgekeurdTest} FROM `Producten` p WHERE p.`ProductenID` = @id", cn);
            test.Parameters.AddWithValue("@id", onderdeelId);
            if (await test.ExecuteScalarAsync() is { } a && a is not DBNull && Convert.ToInt64(a) != 0)
                throw new InvalidOperationException("Dit exemplaar is afgekeurd en kan niet in een product worden ingebouwd.");
        }
        await using var cmd = new MySqlCommand(
            "UPDATE `Producten` SET `ParentProductenID` = @ouder WHERE `ProductenID` = @id", cn);
        cmd.Parameters.AddWithValue("@ouder", (object?)ouderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", onderdeelId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Exemplaren zonder ouder, om als onderdeel toe te voegen.</summary>
    public async Task<List<SamenstellingRegel>> ZoekVrijeExemplarenAsync(string zoek, int max, int? productNameId = null, bool ookAfgekeurd = false)
    {
        var serie = await Kolom("Serienummer", "SerieNummer");
        var kolommen = await KolommenAsync();
        var gewist = kolommen.Contains("isDeleted") ? "AND (p.`isDeleted` = 0 OR p.`isDeleted` IS NULL) " : "";
        var type = productNameId is null ? "" : "AND p.`ProductNameID` = @n ";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            SamenstellingSelect + $", p.`{serie}`, {AfgekeurdTest} " +
            "  FROM `Producten` p " +
            "  LEFT JOIN `ProductNames` n ON n.`ProductNameID` = p.`ProductNameID` " +
            "  LEFT JOIN `ProductTypes` t ON t.`ProductTypeID` = n.`ProductTypeID` " +
            " WHERE p.`ParentProductenID` IS NULL " + gewist + type + (ookAfgekeurd ? "" : $"AND NOT {AfgekeurdTest} ") +
            $"   AND (p.`{serie}` LIKE @z OR n.`ProductName` LIKE @z OR n.`Omschrijving` LIKE @z) " +
            $" ORDER BY n.`ProductName`, CAST(p.`{serie}` AS UNSIGNED) DESC, p.`{serie}` DESC LIMIT {Math.Clamp(max, 1, 200)}", cn);
        cmd.Parameters.AddWithValue("@z", "%" + (zoek ?? "").Trim() + "%");
        if (productNameId is not null) cmd.Parameters.AddWithValue("@n", productNameId.Value);
        return await LeesSamenstelling(cmd);
    }

    // ============================================================
    //  Stuklijst per producttype (ProductSamenstelling)
    // ============================================================
    private bool? _heeftStuklijst;
    public async Task<bool> HeeftStuklijstAsync()
    {
        if (_heeftStuklijst == true) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'ProductSamenstelling'", cn);
            _heeftStuklijst = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { return false; }
        return _heeftStuklijst.Value;
    }

    public async Task<List<StuklijstRegel>> GetStuklijstAsync(int productNameId)
    {
        var lijst = new List<StuklijstRegel>();
        if (!await HeeftStuklijstAsync()) return lijst;
        await using var cn = await OpenAsync();
        var extra = await HeeftExtraAsync(cn);
        await using var cmd = new MySqlCommand(
            "SELECT s.`SamenstellingID`, s.`ProductNameID`, s.`OnderdeelNameID`, COALESCE(n.`ProductName`, ''), " +
            "       COALESCE(n.`Omschrijving`, ''), s.`Aantal`, s.`Volgorde`, COALESCE(s.`Opmerking`, '')" +
            (extra ? ", COALESCE(g.`Naam`, '') " : ", '' ") +
            "  FROM `ProductSamenstelling` s " +
            "  LEFT JOIN `ProductNames` n ON n.`ProductNameID` = s.`OnderdeelNameID` " +
            (extra ? "  LEFT JOIN `ProductNameExtra` x ON x.`ProductNameID` = n.`ProductNameID` " +
                     "  LEFT JOIN `ProductGroepen` g ON g.`GroepID` = x.`GroepID` " : "") +
            " WHERE s.`ProductNameID` = @n ORDER BY s.`Volgorde`, n.`ProductName`", cn);
        cmd.Parameters.AddWithValue("@n", productNameId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new StuklijstRegel
            {
                Id = r.GetInt32(0), ProductNameId = r.GetInt32(1), OnderdeelNameId = r.GetInt32(2),
                OnderdeelNaam = r.GetString(3), OnderdeelOmschrijving = r.GetString(4),
                Aantal = r.GetInt32(5), Volgorde = r.GetInt32(6), Opmerking = r.GetString(7), OnderdeelGroep = r.GetString(8),
            });
        return lijst;
    }

    public async Task<int> BewaarStuklijstRegelAsync(StuklijstRegel s, int? door)
    {
        if (!await HeeftStuklijstAsync())
            throw new InvalidOperationException("De tabel ProductSamenstelling ontbreekt nog: voer ProductSamenstelling_MariaDB.sql uit.");
        if (s.OnderdeelNameId == s.ProductNameId) throw new InvalidOperationException("Een product kan niet zichzelf bevatten.");
        await using var cn = await OpenAsync();
        if (s.Id == 0 && s.Volgorde == 0)
        {
            await using var max = new MySqlCommand("SELECT COALESCE(MAX(`Volgorde`), 0) + 1 FROM `ProductSamenstelling` WHERE `ProductNameID` = @n", cn);
            max.Parameters.AddWithValue("@n", s.ProductNameId);
            s.Volgorde = Convert.ToInt32(await max.ExecuteScalarAsync());
        }
        var velden = "`ProductNameID` = @n, `OnderdeelNameID` = @o, `Aantal` = @a, `Volgorde` = @v, `Opmerking` = @opm";
        var sql = s.Id == 0
            ? $"INSERT INTO `ProductSamenstelling` SET {velden}, `AddedBy` = @door, `AddedAt` = NOW()"
            : $"UPDATE `ProductSamenstelling` SET {velden} WHERE `SamenstellingID` = @id";
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@n", s.ProductNameId);
        cmd.Parameters.AddWithValue("@o", s.OnderdeelNameId);
        cmd.Parameters.AddWithValue("@a", Math.Max(1, s.Aantal));
        cmd.Parameters.AddWithValue("@v", s.Volgorde);
        cmd.Parameters.AddWithValue("@opm", string.IsNullOrWhiteSpace(s.Opmerking) ? DBNull.Value : s.Opmerking.Trim());
        if (s.Id == 0) cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        else cmd.Parameters.AddWithValue("@id", s.Id);
        try { await cmd.ExecuteNonQueryAsync(); }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new InvalidOperationException("Dit halffabricaat staat al in de stuklijst; pas daar het aantal aan.");
        }
        return s.Id == 0 ? (int)cmd.LastInsertedId : s.Id;
    }

    public async Task VerwijderStuklijstRegelAsync(int samenstellingId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `ProductSamenstelling` WHERE `SamenstellingID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", samenstellingId);
        await cmd.ExecuteNonQueryAsync();
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

    public async Task<HashSet<int>> GetStatusIdsMetNaamAsync(string naam)
    {
        var ids = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(naam)) return ids;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ProductStatusID` FROM `ProductStatus` " +
            " WHERE UPPER(TRIM(COALESCE(`Status`, ''))) = @naam OR UPPER(TRIM(COALESCE(`StatusShort`, ''))) = @naam", cn);
        cmd.Parameters.AddWithValue("@naam", naam.Trim().ToUpperInvariant());
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
        return ids;
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
