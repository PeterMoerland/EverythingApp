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
}

public class GeenVasteLastenRepository : IVasteLastenRepository
{
    public bool Beschikbaar => false;
    public Task<List<VasteLast>> LijstAsync() => Task.FromResult(new List<VasteLast>());
    public Task<List<VasteLastCategorie>> CategorieenAsync() => Task.FromResult(new List<VasteLastCategorie>());
    public Task<int> CategorieToevoegenAsync(string omschrijving) => Task.FromResult(0);
    public Task<int> BewaarAsync(VasteLast last) => Task.FromResult(0);
    public Task WisAsync(int id) => Task.CompletedTask;
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

    public async Task<List<VasteLast>> LijstAsync()
    {
        var lijst = new List<VasteLast>();
        var jaar = DateTime.Today.Year;      // Access rekende de dag van het jaar in het lopende jaar

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT v.`ID`, COALESCE(v.`Omschrijving`, ''), COALESCE(v.`Bedrag`, 0), COALESCE(v.`interval`, 2), " +
            "       v.`Incassodatum`, v.`CategorieID`, COALESCE(c.`Omschrijving`, ''), " +
            "       COALESCE(v.`last_updated`, v.`Tijdstip`) " +
            "  FROM `VasteLasten` v LEFT JOIN `VasteLastenCategorie` c ON c.`CategorieID` = v.`CategorieID` " +
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
        await using var cmd = new MySqlCommand(last.Id == 0
            ? "INSERT INTO `VasteLasten` (`Omschrijving`, `Bedrag`, `Incassodatum`, `interval`, `CategorieID`, " +
              "       `TotaalPerJaar`, `Tijdstip`) VALUES (@oms, @bedrag, @dag, @interval, @cat, @jaar, NOW()); " +
              "SELECT LAST_INSERT_ID();"
            : "UPDATE `VasteLasten` SET `Omschrijving` = @oms, `Bedrag` = @bedrag, `Incassodatum` = @dag, " +
              "       `interval` = @interval, `CategorieID` = @cat, `TotaalPerJaar` = @jaar, `Tijdstip` = NOW() " +
              " WHERE `ID` = @id; SELECT @id;", cn);
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
