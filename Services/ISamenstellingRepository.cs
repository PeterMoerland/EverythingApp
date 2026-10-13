using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een samenstelling in de lijst links (Access: Artikelsamenstellingen_Open).</summary>
public record SamenstellingKop(int ArtikelId, string Code, string Omschrijving, int AantalOnderdelen, DateTime? Gewijzigd);

/// <summary>
/// Een knoop in de boom (Access: AssemblyTree via de procedure GetArtikelTree). De wortel heeft RegelId 0.
/// Kostprijs: bij een gewoon onderdeel de vastgestelde kostprijs, bij een samenstelling de som van zijn onderdelen.
/// </summary>
public class SamenstellingKnoop
{
    public int RegelId { get; set; }                    // ArtikelSamenstellingID; 0 = de samenstelling zelf
    public int OuderArtikelId { get; set; }             // ArtikelParentID
    public int ArtikelId { get; set; }
    public int Niveau { get; set; }
    public double Aantal { get; set; } = 1;
    public string Code { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string Opmerking { get; set; } = "";
    public bool IsSamenstelling { get; set; }
    public bool InventorImport { get; set; }            // vet in Access
    public decimal? Kostprijs { get; set; }
    public List<SamenstellingKnoop> Kinderen { get; set; } = new();
    public bool Kringloop { get; set; }                 // de samenstelling bevat zichzelf (boom afgebroken)

    public decimal Totaal => (decimal)Aantal * (Kostprijs ?? 0);
}

/// <summary>
/// Access-formulier ArtikelSamenstellingen: de hele stuklijst van een artikel als boom. Wijzigen van regels gaat via
/// IArtikelRepository (BewaarSamenstellingregelAsync enz.), zodat Artikelen ▸ Samenstelling en dit scherm hetzelfde doen.
/// </summary>
public interface ISamenstellingRepository
{
    bool Beschikbaar { get; }

    /// <summary>Artikelen met het vinkje Samenstelling of met onderdelen.</summary>
    Task<List<SamenstellingKop>> LijstAsync(string? zoek, int max);

    /// <summary>De volledige boom onder een artikel (max. 12 niveaus, kringlopen afgebroken).</summary>
    Task<SamenstellingKnoop?> BoomAsync(int artikelId);

    /// <summary>In hoeveel samenstellingen dit artikel als onderdeel voorkomt.</summary>
    Task<int> GebruiktInAsync(int artikelId);
}

public class GeenSamenstellingRepository : ISamenstellingRepository
{
    public bool Beschikbaar => false;
    public Task<List<SamenstellingKop>> LijstAsync(string? zoek, int max) => Task.FromResult(new List<SamenstellingKop>());
    public Task<SamenstellingKnoop?> BoomAsync(int artikelId) => Task.FromResult<SamenstellingKnoop?>(null);
    public Task<int> GebruiktInAsync(int artikelId) => Task.FromResult(0);
}

public class MySqlSamenstellingRepository : ISamenstellingRepository
{
    private readonly string _cs;
    public MySqlSamenstellingRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static bool B(MySqlDataReader r, int i) => !r.IsDBNull(i) && Convert.ToInt64(r.GetValue(i)) != 0;

    public async Task<List<SamenstellingKop>> LijstAsync(string? zoek, int max)
    {
        var lijst = new List<SamenstellingKop>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT a.`ArtikelID`, COALESCE(a.`ArtikelCodeMDE`, ''), COALESCE(a.`OmschrijvingNL`, ''), " +
            "       (SELECT COUNT(*) FROM `ArtikelSamenstelling` s WHERE s.`ArtikelParentID` = a.`ArtikelID`), " +
            "       (SELECT MAX(s.`last_updated`) FROM `ArtikelSamenstelling` s WHERE s.`ArtikelParentID` = a.`ArtikelID`) " +
            "  FROM `Artikelen` a " +
            " WHERE (COALESCE(a.`Samenstelling`, 0) <> 0 OR EXISTS (SELECT 1 FROM `ArtikelSamenstelling` s WHERE s.`ArtikelParentID` = a.`ArtikelID`)) " +
            (string.IsNullOrWhiteSpace(zoek) ? "" :
                "   AND CONCAT_WS(' ', a.`ArtikelID`, a.`ArtikelCodeMDE`, a.`OmschrijvingNL`, a.`ArtikelCodeFactory`) LIKE @z ") +
            $" ORDER BY a.`ArtikelCodeMDE` LIMIT {Math.Clamp(max, 1, 2000)}", cn);
        if (!string.IsNullOrWhiteSpace(zoek)) cmd.Parameters.AddWithValue("@z", "%" + zoek.Trim() + "%");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new SamenstellingKop(r.GetInt32(0), S(r, 1), S(r, 2), Convert.ToInt32(r.GetValue(3)),
                                           r.IsDBNull(4) ? null : Convert.ToDateTime(r.GetValue(4))));
        return lijst;
    }

    public async Task<SamenstellingKnoop?> BoomAsync(int artikelId)
    {
        await using var cn = await OpenAsync();

        // Alle samenstellingregels zijn er maar ~1.250: in één keer lezen en de boom in het geheugen opbouwen.
        var regels = new Dictionary<int, List<SamenstellingKnoop>>();
        await using (var cmd = new MySqlCommand(
            "SELECT s.`ArtikelSamenstellingID`, s.`ArtikelParentID`, s.`ArtikelID`, COALESCE(s.`Aantal`, 0), " +
            "       COALESCE(a.`ArtikelCodeMDE`, ''), COALESCE(NULLIF(a.`OmschrijvingNL`, ''), s.`Omschrijving`, ''), " +
            "       COALESCE(s.`Opmerking`, ''), COALESCE(a.`Samenstelling`, 0), COALESCE(s.`InventorImport`, 0), a.`VastgesteldeKostprijs` " +
            "  FROM `ArtikelSamenstelling` s LEFT JOIN `Artikelen` a ON a.`ArtikelID` = s.`ArtikelID` " +
            " WHERE s.`ArtikelParentID` IS NOT NULL AND s.`ArtikelID` IS NOT NULL " +
            " ORDER BY a.`ArtikelCodeMDE`, s.`ArtikelSamenstellingID`", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var k = new SamenstellingKnoop
                {
                    RegelId = r.GetInt32(0), OuderArtikelId = Convert.ToInt32(r.GetValue(1)), ArtikelId = Convert.ToInt32(r.GetValue(2)),
                    Aantal = Convert.ToDouble(r.GetValue(3)), Code = S(r, 4), Omschrijving = S(r, 5), Opmerking = S(r, 6),
                    IsSamenstelling = B(r, 7), InventorImport = B(r, 8),
                    Kostprijs = r.IsDBNull(9) ? null : Convert.ToDecimal(r.GetValue(9)),
                };
                if (!regels.TryGetValue(k.OuderArtikelId, out var lijst)) regels[k.OuderArtikelId] = lijst = new();
                lijst.Add(k);
            }

        SamenstellingKnoop? wortel = null;
        await using (var cmd = new MySqlCommand(
            "SELECT `ArtikelID`, COALESCE(`ArtikelCodeMDE`, ''), COALESCE(`OmschrijvingNL`, ''), COALESCE(`Samenstelling`, 0), " +
            "       `VastgesteldeKostprijs` FROM `Artikelen` WHERE `ArtikelID` = @a", cn))
        {
            cmd.Parameters.AddWithValue("@a", artikelId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                wortel = new SamenstellingKnoop
                {
                    ArtikelId = r.GetInt32(0), Code = S(r, 1), Omschrijving = S(r, 2), IsSamenstelling = true,
                    Kostprijs = r.IsDBNull(4) ? null : Convert.ToDecimal(r.GetValue(4)),
                };
        }
        if (wortel is null) return null;

        // Opbouwen met een pad tegen kringlopen; kinderen krijgen eigen kopieën (een onderdeel kan op meerdere plekken staan).
        void Bouw(SamenstellingKnoop ouder, HashSet<int> pad)
        {
            if (!regels.TryGetValue(ouder.ArtikelId, out var onder)) return;
            ouder.IsSamenstelling = true;
            foreach (var bron in onder)
            {
                var kind = new SamenstellingKnoop
                {
                    RegelId = bron.RegelId, OuderArtikelId = bron.OuderArtikelId, ArtikelId = bron.ArtikelId, Niveau = ouder.Niveau + 1,
                    Aantal = bron.Aantal, Code = bron.Code, Omschrijving = bron.Omschrijving, Opmerking = bron.Opmerking,
                    IsSamenstelling = bron.IsSamenstelling, InventorImport = bron.InventorImport, Kostprijs = bron.Kostprijs,
                };
                ouder.Kinderen.Add(kind);
                if (pad.Contains(kind.ArtikelId) || kind.Niveau >= 12) { kind.Kringloop = regels.ContainsKey(kind.ArtikelId); continue; }
                pad.Add(kind.ArtikelId);
                Bouw(kind, pad);
                pad.Remove(kind.ArtikelId);
            }
            // prijs van een samenstelling = som van de onderdelen (zoals Artikelen ▸ Samenstelling)
            if (ouder.Kinderen.Count > 0) ouder.Kostprijs = ouder.Kinderen.Sum(k => k.Totaal);
        }

        Bouw(wortel, new HashSet<int> { wortel.ArtikelId });
        return wortel;
    }

    public async Task<int> GebruiktInAsync(int artikelId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(DISTINCT `ArtikelParentID`) FROM `ArtikelSamenstelling` WHERE `ArtikelID` = @a", cn);
        cmd.Parameters.AddWithValue("@a", artikelId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
