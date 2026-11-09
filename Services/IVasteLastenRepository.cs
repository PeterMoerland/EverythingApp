using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Vaste lasten (tabellen VasteLasten en VasteLastenCategorie), het Access-formulier VasteLasten.</summary>
public interface IVasteLastenRepository
{
    bool Beschikbaar { get; }

    /// <summary>Alle vaste lasten; het zijn er een paar tientallen, filteren gebeurt op het scherm.</summary>
    Task<List<VasteLast>> LijstAsync();

    Task<List<VasteLastCategorie>> CategorieenAsync();

    /// <summary>Voegt een categorie toe (of geeft de bestaande met die naam terug).</summary>
    Task<int> CategorieToevoegenAsync(string omschrijving);

    /// <summary>Nieuw (Id 0) of bijwerken. Geeft het id terug.</summary>
    Task<int> BewaarAsync(VasteLast last);

    Task WisAsync(int id);

    /// <summary>De BV's waar een vaste last bij kan horen (MDE-groep, niet opgeheven); leeg = kolom BvID ontbreekt nog.</summary>
    Task<List<Keuze>> BvsAsync();
}

public class GeenVasteLastenRepository : IVasteLastenRepository
{
    public bool Beschikbaar => false;
    public Task<List<VasteLast>> LijstAsync() => Task.FromResult(new List<VasteLast>());
    public Task<List<VasteLastCategorie>> CategorieenAsync() => Task.FromResult(new List<VasteLastCategorie>());
    public Task<int> CategorieToevoegenAsync(string omschrijving) => Task.FromResult(0);
    public Task<int> BewaarAsync(VasteLast last) => Task.FromResult(0);
    public Task WisAsync(int id) => Task.CompletedTask;
    public Task<List<Keuze>> BvsAsync() => Task.FromResult(new List<Keuze>());
}

public class MySqlVasteLastenRepository : IVasteLastenRepository
{
    private readonly string _cs;

    public MySqlVasteLastenRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private bool _bvKolom, _eindKolom, _verzTabel;

    /// <summary>VasteLasten.Einddatum (VerzekeringenBron_MariaDB.sql) en de tabel Verzekeringen: bestaan ze al?</summary>
    private async Task<(bool Eind, bool Verz)> ExtraAsync(MySqlConnection cn)
    {
        if (_eindKolom && _verzTabel) return (true, true);
        try
        {
            await using var cmd = new MySqlCommand(
                "SELECT (SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'VasteLasten' AND COLUMN_NAME = 'Einddatum'), " +
                "       (SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Verzekeringen')", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) { _eindKolom = Convert.ToInt32(r.GetValue(0)) > 0; _verzTabel = Convert.ToInt32(r.GetValue(1)) > 0; }
        }
        catch { }
        return (_eindKolom, _verzTabel);
    }

    /// <summary>Bestaat VasteLasten.BvID al (script VasteLastenBv_MariaDB.sql)? Eenmaal gevonden blijft het zo.</summary>
    private async Task<bool> BvKolomAsync(MySqlConnection cn)
    {
        if (_bvKolom) return true;
        try
        {
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "AND TABLE_NAME = 'VasteLasten' AND COLUMN_NAME = 'BvID'", cn);
            _bvKolom = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _bvKolom;
    }

    public async Task<List<Keuze>> BvsAsync()
    {
        var lijst = new List<Keuze>();
        await using var cn = await OpenAsync();
        if (!await BvKolomAsync(cn)) return lijst;
        var opg = await BvOpgeheven.KolomAsync(cn);
        await using var cmd = new MySqlCommand(
            "SELECT `id_supplier`, COALESCE(NULLIF(TRIM(`Afkorting`), ''), `name`) FROM `Relations` " +
            $" WHERE COALESCE(`MDEBV`, 0) <> 0 AND NOT {BvOpgeheven.Is(opg)} ORDER BY `name`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new Keuze(r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1)));
        return lijst;
    }

    public async Task<List<VasteLast>> LijstAsync()
    {
        var lijst = new List<VasteLast>();
        var jaar = DateTime.Today.Year;      // Access rekende de dag van het jaar in het lopende jaar

        await using var cn = await OpenAsync();
        var bv = await BvKolomAsync(cn);
        var (eind, verz) = await ExtraAsync(cn);
        await using var cmd = new MySqlCommand(
            "SELECT v.`ID`, COALESCE(v.`Omschrijving`, ''), COALESCE(v.`Bedrag`, 0), COALESCE(v.`interval`, 2), " +
            "       v.`Incassodatum`, v.`CategorieID`, COALESCE(c.`Omschrijving`, ''), " +
            "       COALESCE(v.`last_updated`, v.`Tijdstip`), " +
            (bv ? "v.`BvID`, COALESCE(NULLIF(TRIM(b.`Afkorting`), ''), b.`name`, ''), " : "NULL, '', ") +
            (eind ? "v.`Einddatum`, " : "NULL, ") +
            (verz ? "(SELECT z.`VerzekeringID` FROM `Verzekeringen` z WHERE z.`VasteLastID` = v.`ID` LIMIT 1) " : "NULL ") +
            "  FROM `VasteLasten` v LEFT JOIN `VasteLastenCategorie` c ON c.`CategorieID` = v.`CategorieID` " +
            (bv ? "  LEFT JOIN `Relations` b ON b.`id_supplier` = v.`BvID` " : "") +
            " ORDER BY v.`Omschrijving`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var last = new VasteLast
            {
                Id = r.GetInt32(0),
                Omschrijving = r.GetString(1),
                Bedrag = Convert.ToDecimal(r.GetValue(2)),
                Interval = Enum.IsDefined(typeof(VasteLastInterval), Convert.ToInt32(r.GetValue(3)))
                    ? (VasteLastInterval)Convert.ToInt32(r.GetValue(3)) : VasteLastInterval.Maandelijks,
                CategorieId = r.IsDBNull(5) ? null : r.GetInt32(5),
                Categorie = r.GetString(6),
                Gewijzigd = r.IsDBNull(7) ? null : r.GetDateTime(7),
                BvId = r.IsDBNull(8) ? null : Convert.ToInt32(r.GetValue(8)),
                Bv = r.IsDBNull(9) ? "" : r.GetValue(9).ToString() ?? "",
                Einddatum = r.IsDBNull(10) ? null : DateOnly.FromDateTime(Convert.ToDateTime(r.GetValue(10))),
                VerzekeringId = r.IsDBNull(11) ? null : Convert.ToInt32(r.GetValue(11)),
            };
            last.VanIncassodatum(r.IsDBNull(4) ? null : Convert.ToInt32(r.GetValue(4)), jaar);
            lijst.Add(last);
        }
        return lijst;
    }

    public async Task<List<VasteLastCategorie>> CategorieenAsync()
    {
        var lijst = new List<VasteLastCategorie>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `CategorieID`, COALESCE(`Omschrijving`, '') FROM `VasteLastenCategorie` ORDER BY `Omschrijving`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new VasteLastCategorie(r.GetInt32(0), r.GetString(1)));
        return lijst;
    }

    public async Task<int> CategorieToevoegenAsync(string omschrijving)
    {
        var naam = omschrijving.Trim();
        if (naam.Length == 0) throw new InvalidOperationException("Geef de categorie een naam.");
        if (naam.Length > 45) naam = naam[..45];

        await using var cn = await OpenAsync();
        await using (var zoek = new MySqlCommand(
            "SELECT `CategorieID` FROM `VasteLastenCategorie` WHERE `Omschrijving` = @n LIMIT 1", cn))
        {
            zoek.Parameters.AddWithValue("@n", naam);
            if (await zoek.ExecuteScalarAsync() is { } bestaand and not DBNull) return Convert.ToInt32(bestaand);
        }

        await using var cmd = new MySqlCommand(
            "INSERT INTO `VasteLastenCategorie` (`Omschrijving`) VALUES (@n); SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@n", naam);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<int> BewaarAsync(VasteLast last)
    {
        if (last.Omschrijving.Trim().Length == 0) throw new InvalidOperationException("Geen omschrijving ingevuld.");

        var omschrijving = last.Omschrijving.Trim();
        if (omschrijving.Length > 45) omschrijving = omschrijving[..45];

        await using var cn = await OpenAsync();
        var bv = await BvKolomAsync(cn);
        var (eind, _) = await ExtraAsync(cn);
        var extraKol = (bv ? ", `BvID`" : "") + (eind ? ", `Einddatum`" : "");
        var extraWaarde = (bv ? ", @bv" : "") + (eind ? ", @eind" : "");
        var extraSet = (bv ? ", `BvID` = @bv" : "") + (eind ? ", `Einddatum` = @eind" : "");
        await using var cmd = new MySqlCommand(last.Id == 0
            ? "INSERT INTO `VasteLasten` (`Omschrijving`, `Bedrag`, `Incassodatum`, `interval`, `CategorieID`, " +
              $"       `TotaalPerJaar`, `Tijdstip`{extraKol}) VALUES (@oms, @bedrag, @dag, @interval, @cat, @jaar, NOW(){extraWaarde}); " +
              "SELECT LAST_INSERT_ID();"
            : "UPDATE `VasteLasten` SET `Omschrijving` = @oms, `Bedrag` = @bedrag, `Incassodatum` = @dag, " +
              $"       `interval` = @interval, `CategorieID` = @cat, `TotaalPerJaar` = @jaar, `Tijdstip` = NOW(){extraSet} " +
              " WHERE `ID` = @id; SELECT @id;", cn);
        cmd.Parameters.AddWithValue("@bv", (object?)last.BvId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@eind", last.Einddatum is DateOnly e ? e.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@oms", omschrijving);
        cmd.Parameters.AddWithValue("@bedrag", last.Bedrag);
        cmd.Parameters.AddWithValue("@dag", last.NaarIncassodatum(DateTime.Today.Year));
        cmd.Parameters.AddWithValue("@interval", (int)last.Interval);
        cmd.Parameters.AddWithValue("@cat", (object?)last.CategorieId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@jaar", last.TotaalPerJaar);
        cmd.Parameters.AddWithValue("@id", last.Id);
        last.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return last.Id;
    }

    public async Task WisAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `VasteLasten` WHERE `ID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Alleen om lokaal te testen (VasteLasten__Geheugen=true): leest de echte vaste lasten één keer, en daarna gebeurt alles
/// (opslaan, wissen, categorie toevoegen) in het geheugen. Zo kan de koppeling met Verzekeringen getest worden zonder in
/// VasteLasten te schrijven.
/// </summary>
public class GeheugenVasteLastenRepository : IVasteLastenRepository
{
    private readonly IVasteLastenRepository _echt;
    private List<VasteLast>? _lasten;
    private List<VasteLastCategorie>? _categorieen;
    private int _volgende = 900000;

    public GeheugenVasteLastenRepository(IVasteLastenRepository echt) => _echt = echt;

    public bool Beschikbaar => true;

    private async Task ZorgGeladen()
    {
        _lasten ??= await _echt.LijstAsync();
        _categorieen ??= await _echt.CategorieenAsync();
    }

    public async Task<List<VasteLast>> LijstAsync()
    {
        await ZorgGeladen();
        return _lasten!.Select(v => new VasteLast
        {
            Id = v.Id, Omschrijving = v.Omschrijving, Bedrag = v.Bedrag, Interval = v.Interval, CategorieId = v.CategorieId,
            Categorie = _categorieen!.FirstOrDefault(c => c.Id == v.CategorieId)?.Omschrijving ?? v.Categorie, Dag = v.Dag, Maand = v.Maand,
            BvId = v.BvId, Bv = v.Bv, Einddatum = v.Einddatum, VerzekeringId = v.VerzekeringId, Gewijzigd = v.Gewijzigd,
        }).ToList();
    }

    public async Task<List<VasteLastCategorie>> CategorieenAsync() { await ZorgGeladen(); return _categorieen!.ToList(); }

    public async Task<int> CategorieToevoegenAsync(string omschrijving)
    {
        await ZorgGeladen();
        if (_categorieen!.FirstOrDefault(c => c.Omschrijving == omschrijving.Trim()) is { } c) return c.Id;
        var id = Interlocked.Increment(ref _volgende);
        _categorieen!.Add(new VasteLastCategorie(id, omschrijving.Trim()));
        return id;
    }

    public async Task<int> BewaarAsync(VasteLast last)
    {
        await ZorgGeladen();
        if (last.Id == 0) last.Id = Interlocked.Increment(ref _volgende);
        _lasten!.RemoveAll(v => v.Id == last.Id);
        _lasten.Add(last);
        return last.Id;
    }

    public async Task WisAsync(int id) { await ZorgGeladen(); _lasten!.RemoveAll(v => v.Id == id); }

    public Task<List<Keuze>> BvsAsync() => _echt.BvsAsync();
}
