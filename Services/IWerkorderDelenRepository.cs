using MySqlConnector;

namespace EverythingApp.Services;

// Uren boeken op de delen van een werkorder (wens gebruiker 10-11-2026). De delen maakt een calculatie bij "Werkorders
// aanmaken" (WerkorderDelen, CalculatieWerkorders_MariaDB.sql); Uren.WerkorderDeelID (UrenDelen_MariaDB.sql) zegt op
// welk deel een urenregel staat. Ureningave vraagt het deel als de werkorder delen heeft; de calculatie (tabblad
// Werkorders) en de nacalculatie zetten per deel begroot tegen geboekt.

/// <summary>Een deel van een werkorder waarop uren geboekt kunnen worden.</summary>
public record WerkorderDeel(int DeelId, int WerkbonId, string Naam, decimal BegroteUren, decimal BegroteKosten, int? ActiviteitId);

/// <summary>
/// Begroot tegen geboekt voor een deel. DeelId null = de uren die op de werkorder zelf staan, zonder deel (alleen bij een
/// werkorder met delen; begroot is dan 0).
/// </summary>
public record DeelStand(int WerkbonId, int? DeelId, string Naam, decimal BegroteUren, decimal Geboekt);

public interface IWerkorderDelenRepository
{
    /// <summary>Kan er op delen geboekt worden: tabel WerkorderDelen en kolom Uren.WerkorderDeelID bestaan.</summary>
    Task<bool> BeschikbaarAsync();

    /// <summary>De actieve delen per werkorder (alleen werkorders die delen hebben), in de volgorde van de calculatie.</summary>
    Task<Dictionary<int, List<WerkorderDeel>>> DelenAsync(IReadOnlyCollection<int> werkbonIds);

    /// <summary>Per deel begroot en geboekt (uren, zonder automatisch gemaakte regels), plus "zonder deel" als er zulke uren zijn.</summary>
    Task<List<DeelStand>> StandAsync(IReadOnlyCollection<int> werkbonIds);
}

public class GeenWerkorderDelenRepository : IWerkorderDelenRepository
{
    public Task<bool> BeschikbaarAsync() => Task.FromResult(false);
    public Task<Dictionary<int, List<WerkorderDeel>>> DelenAsync(IReadOnlyCollection<int> werkbonIds) => Task.FromResult(new Dictionary<int, List<WerkorderDeel>>());
    public Task<List<DeelStand>> StandAsync(IReadOnlyCollection<int> werkbonIds) => Task.FromResult(new List<DeelStand>());
}

public class MySqlWerkorderDelenRepository : IWerkorderDelenRepository
{
    private readonly string _cs;
    private readonly IUrenRepository _uren;
    private bool _tabel;   // alleen "ja" onthouden: na het script werkt het zonder herstart van deze kant

    public MySqlWerkorderDelenRepository(string cs, IUrenRepository uren) { _cs = cs; _uren = uren; }

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<bool> BeschikbaarAsync()
    {
        // De kolom komt uit dezelfde schema-lezing als het opslaan van urenregels; zo kan het scherm nooit een deel vragen
        // dat daarna niet bewaard wordt (de urenrepository leest de kolommen één keer, bij het starten).
        if (!_uren.KentWerkorderDeel) return false;
        if (_tabel) return true;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'WerkorderDelen'", cn);
        return _tabel = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static string InLijst(MySqlCommand cmd, IReadOnlyCollection<int> ids)
    {
        var namen = new List<string>();
        foreach (var id in ids.Distinct())
        {
            var p = "@w" + namen.Count;
            cmd.Parameters.AddWithValue(p, id);
            namen.Add(p);
        }
        return string.Join(",", namen);
    }

    private static decimal M(MySqlDataReader r, int i) => r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i));

    public async Task<Dictionary<int, List<WerkorderDeel>>> DelenAsync(IReadOnlyCollection<int> werkbonIds)
    {
        var uit = new Dictionary<int, List<WerkorderDeel>>();
        if (werkbonIds.Count == 0 || !await BeschikbaarAsync()) return uit;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("", cn);
        cmd.CommandText =
            "SELECT `DeelID`, `WerkbonID`, `Naam`, `BegroteUren`, `BegroteKosten`, `UrenActiviteitID` FROM `WerkorderDelen` " +
            $" WHERE `Actief` <> 0 AND `WerkbonID` IN ({InLijst(cmd, werkbonIds)}) ORDER BY `WerkbonID`, `Volgorde`, `DeelID`";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var d = new WerkorderDeel(r.GetInt32(0), r.GetInt32(1), r.GetString(2), M(r, 3), M(r, 4), r.IsDBNull(5) ? null : r.GetInt32(5));
            if (!uit.TryGetValue(d.WerkbonId, out var l)) uit[d.WerkbonId] = l = new();
            l.Add(d);
        }
        return uit;
    }

    public async Task<List<DeelStand>> StandAsync(IReadOnlyCollection<int> werkbonIds)
    {
        var uit = new List<DeelStand>();
        if (werkbonIds.Count == 0 || !await BeschikbaarAsync()) return uit;
        await using var cn = await OpenAsync();
        await using (var cmd = new MySqlCommand("", cn))
        {
            // ook inactieve delen: daar kan al op geboekt zijn
            cmd.CommandText =
                "SELECT d.`WerkbonID`, d.`DeelID`, d.`Naam`, d.`BegroteUren`, " +
                "       (SELECT COALESCE(SUM(u.`Uren`), 0) FROM `Uren` u WHERE u.`WerkorderDeelID` = d.`DeelID` AND COALESCE(u.`GeneratedByRule`, 0) = 0) " +
                "  FROM `WerkorderDelen` d " +
                $" WHERE d.`WerkbonID` IN ({InLijst(cmd, werkbonIds)}) ORDER BY d.`WerkbonID`, d.`Volgorde`, d.`DeelID`";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                uit.Add(new DeelStand(r.GetInt32(0), r.GetInt32(1), r.GetString(2), M(r, 3), M(r, 4)));
        }
        var metDelen = uit.Select(s => s.WerkbonId).Distinct().ToList();
        if (metDelen.Count == 0) return uit;
        await using (var cmd = new MySqlCommand("", cn))
        {
            cmd.CommandText =
                "SELECT u.`werkorder`, SUM(u.`Uren`) FROM `Uren` u " +
                $" WHERE u.`werkorder` IN ({InLijst(cmd, metDelen)}) AND u.`WerkorderDeelID` IS NULL AND COALESCE(u.`GeneratedByRule`, 0) = 0 " +
                " GROUP BY u.`werkorder` HAVING SUM(u.`Uren`) <> 0";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                uit.Add(new DeelStand(r.GetInt32(0), null, "zonder deel", 0m, M(r, 1)));
        }
        return uit;
    }
}

/// <summary>
/// Lokaal testen zonder de kolom en zonder echte delen (UrenDelen__Geheugen=true): elke werkorder krijgt drie nepdelen met
/// een nepstand. Ureningave toont dan de keuze; opslaan van een urenregel bewaart het deel níet (de kolom ontbreekt).
/// </summary>
public class GeheugenWerkorderDelenRepository : IWerkorderDelenRepository
{
    private static readonly (string Naam, decimal Uren)[] Nep = { ("Engineering", 16), ("Assemblage", 24), ("Test en oplevering", 8) };

    public Task<bool> BeschikbaarAsync() => Task.FromResult(true);

    public Task<Dictionary<int, List<WerkorderDeel>>> DelenAsync(IReadOnlyCollection<int> werkbonIds)
        => Task.FromResult(werkbonIds.Distinct().ToDictionary(w => w,
            w => Nep.Select((d, i) => new WerkorderDeel(w * 10 + i, w, d.Naam, d.Uren, d.Uren * 55, null)).ToList()));

    public Task<List<DeelStand>> StandAsync(IReadOnlyCollection<int> werkbonIds)
        => Task.FromResult(werkbonIds.Distinct().SelectMany(w =>
                Nep.Select((d, i) => new DeelStand(w, w * 10 + i, d.Naam, d.Uren, i switch { 0 => 19.5m, 1 => 12m, _ => 0m }))
                   .Append(new DeelStand(w, null, "zonder deel", 0m, 3.25m)))
            .ToList());
}
