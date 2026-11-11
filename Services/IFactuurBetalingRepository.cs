using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Soort van een regel in FactuurBetalingen.</summary>
public static class BetalingSoort
{
    public const string Bank = "bank";
    public const string Handmatig = "handmatig";
    public const string Afboeking = "afboeking";

    public static string Tekst(string soort) => soort switch
    {
        Bank => "bank",
        Afboeking => "afgeboekt",
        _ => "handmatig",
    };
}

/// <summary>Een ontvangst of afboeking op een uitgaande factuur (tabel FactuurBetalingen).</summary>
public record FactuurBetaling(int Id, int FactuurId, DateOnly Datum, decimal Bedrag, string Soort, int? BanktransactieId,
                              string Opmerking, DateTime? AangemaaktOp, string AangemaaktDoor);

/// <summary>
/// Betalingen per uitgaande factuur (wens collega 4-11-2026: een factuur deels betaald melden, bv. 90% ontvangen en
/// 10% inhouding). Tabel uit FactuurBetalingen_MariaDB.sql; zolang die er niet is geeft <see cref="TabelAsync"/> false
/// en werkt alles zoals voorheen (alleen de vlag Betaald). Invoices.Betaald blijft de vlag waar de rest op filtert:
/// toevoegen zet hem zodra de regels samen het factuurbedrag halen (met <see cref="Betalingsmatcher.Marge"/>);
/// weghalen zet hem nooit terug, want een factuur kan ook met de hand op betaald staan.
/// </summary>
public interface IFactuurBetalingRepository
{
    bool Beschikbaar { get; }
    /// <summary>Is FactuurBetalingen_MariaDB.sql uitgevoerd?</summary>
    Task<bool> TabelAsync();
    Task<List<FactuurBetaling>> LijstAsync(int factuurId);
    /// <summary>Som van de regels (ontvangen en afgeboekt) per factuur; facturen zonder regels ontbreken.</summary>
    Task<Dictionary<int, decimal>> OntvangenAsync(IReadOnlyCollection<int> factuurIds);
    /// <summary>Een handmatige ontvangst of afboeking; true = de factuur staat nu op betaald.</summary>
    Task<bool> VoegToeAsync(int factuurId, DateOnly datum, decimal bedrag, string soort, string opmerking, int? door);
    Task VerwijderAsync(int betalingId);
    /// <summary>
    /// De aangevinkte voorstellen van de betalingscheck: per factuur het deel van elke ontvangst als regel, de
    /// koppeling met de banktransactie, en Betaald als het rond is. Facturen.Bedrag is het openstaande restant.
    /// </summary>
    Task<(int Betaald, int Deels)> VerwerkAsync(IReadOnlyList<BetalingVoorstel> voorstellen, int? door);
}

public class GeenFactuurBetalingRepository : IFactuurBetalingRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAsync() => Task.FromResult(false);
    public Task<List<FactuurBetaling>> LijstAsync(int factuurId) => Task.FromResult(new List<FactuurBetaling>());
    public Task<Dictionary<int, decimal>> OntvangenAsync(IReadOnlyCollection<int> factuurIds) => Task.FromResult(new Dictionary<int, decimal>());
    public Task<bool> VoegToeAsync(int factuurId, DateOnly datum, decimal bedrag, string soort, string opmerking, int? door)
        => throw new InvalidOperationException("Er is geen database.");
    public Task VerwijderAsync(int betalingId) => Task.CompletedTask;
    public Task<(int Betaald, int Deels)> VerwerkAsync(IReadOnlyList<BetalingVoorstel> voorstellen, int? door) => Task.FromResult((0, 0));
}

/// <summary>
/// Lokaal testen zonder in de database te schrijven (FactuurBetalingen__Geheugen=true): bestaande regels uit de
/// database als de tabel er is, nieuwe en gewiste alleen in het geheugen. De vlag Betaald in Invoices verandert niet.
/// </summary>
public class GeheugenFactuurBetalingRepository : IFactuurBetalingRepository
{
    private readonly IFactuurBetalingRepository _echt;
    private readonly IVerkoopFactuurRepository _facturen;
    private readonly List<FactuurBetaling> _nieuw = new();
    private readonly HashSet<int> _gewist = new();
    private int _volgend = 900000;

    public GeheugenFactuurBetalingRepository(IFactuurBetalingRepository echt, IVerkoopFactuurRepository facturen)
        => (_echt, _facturen) = (echt, facturen);

    public bool Beschikbaar => true;
    public Task<bool> TabelAsync() => Task.FromResult(true);

    private async Task<List<FactuurBetaling>> EchtAsync(int factuurId)
    {
        try { return await _echt.TabelAsync() ? await _echt.LijstAsync(factuurId) : new(); }
        catch { return new(); }
    }

    public async Task<List<FactuurBetaling>> LijstAsync(int factuurId)
    {
        var lijst = (await EchtAsync(factuurId)).Where(b => !_gewist.Contains(b.Id)).ToList();
        lock (_nieuw) lijst.AddRange(_nieuw.Where(b => b.FactuurId == factuurId && !_gewist.Contains(b.Id)));
        return lijst.OrderBy(b => b.Datum).ThenBy(b => b.Id).ToList();
    }

    public async Task<Dictionary<int, decimal>> OntvangenAsync(IReadOnlyCollection<int> factuurIds)
    {
        var uit = new Dictionary<int, decimal>();
        foreach (var id in factuurIds.Distinct())
        {
            var som = (await LijstAsync(id)).Sum(b => b.Bedrag);
            if (som != 0) uit[id] = som;
        }
        return uit;
    }

    public async Task<bool> VoegToeAsync(int factuurId, DateOnly datum, decimal bedrag, string soort, string opmerking, int? door)
    {
        Voeg(factuurId, datum, bedrag, soort, null, opmerking);
        return await RondAsync(factuurId);
    }

    private void Voeg(int factuurId, DateOnly datum, decimal bedrag, string soort, int? bank, string opmerking)
    {
        lock (_nieuw)
            _nieuw.Add(new FactuurBetaling(_volgend++, factuurId, datum, bedrag, soort, bank, opmerking, DateTime.Now, "(geheugen)"));
    }

    private async Task<bool> RondAsync(int factuurId)
    {
        var f = await _facturen.GetAsync(factuurId);
        if (f is null || f.BedragInclBtw <= 0) return false;
        return (await LijstAsync(factuurId)).Sum(b => b.Bedrag) >= f.BedragInclBtw - Betalingsmatcher.Marge;
    }

    public Task VerwijderAsync(int betalingId)
    {
        lock (_nieuw) { _nieuw.RemoveAll(b => b.Id == betalingId); _gewist.Add(betalingId); }
        return Task.CompletedTask;
    }

    public async Task<(int Betaald, int Deels)> VerwerkAsync(IReadOnlyList<BetalingVoorstel> voorstellen, int? door)
    {
        int betaald = 0, deels = 0;
        foreach (var v in voorstellen.Where(v => v.KanVerwerken))
        {
            foreach (var d in Betalingsmatcher.Verdeel(v))
                Voeg(d.FactuurId, d.Ontvangst.Datum, d.Bedrag, BetalingSoort.Bank, d.Ontvangst.Id, "");
            foreach (var f in v.Facturen)
                if (await RondAsync(f.Id)) betaald++; else deels++;
        }
        return (betaald, deels);
    }
}

public class MySqlFactuurBetalingRepository : IFactuurBetalingRepository
{
    private readonly string _cs;
    private bool _tabel;
    public MySqlFactuurBetalingRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<bool> TabelAsync()
    {
        if (_tabel) return true;
        await using var cn = await OpenAsync();
        return await TabelAsync(cn);
    }

    private async Task<bool> TabelAsync(MySqlConnection cn)
    {
        if (_tabel) return true;
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'FactuurBetalingen'", cn);
        _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        return _tabel;
    }

    public async Task<List<FactuurBetaling>> LijstAsync(int factuurId)
    {
        var lijst = new List<FactuurBetaling>();
        await using var cn = await OpenAsync();
        if (!await TabelAsync(cn)) return lijst;
        await using var cmd = new MySqlCommand(
            "SELECT b.`FactuurBetalingID`, b.`FactuurID`, b.`Datum`, b.`Bedrag`, b.`Soort`, b.`BanktransactieID`, " +
            "       COALESCE(b.`Opmerking`, ''), b.`AangemaaktOp`, COALESCE(w.`NaamVoluit`, '') " +
            "  FROM `FactuurBetalingen` b LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = b.`AangemaaktDoor` " +
            " WHERE b.`FactuurID` = @f ORDER BY b.`Datum`, b.`FactuurBetalingID`", cn);
        cmd.Parameters.AddWithValue("@f", factuurId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new FactuurBetaling(r.GetInt32(0), r.GetInt32(1), DateOnly.FromDateTime(r.GetDateTime(2)), r.GetDecimal(3),
                r.GetString(4), r.IsDBNull(5) ? null : r.GetInt32(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetDateTime(7),
                r.GetString(8)));
        return lijst;
    }

    public async Task<Dictionary<int, decimal>> OntvangenAsync(IReadOnlyCollection<int> factuurIds)
    {
        var uit = new Dictionary<int, decimal>();
        if (factuurIds.Count == 0) return uit;
        await using var cn = await OpenAsync();
        if (!await TabelAsync(cn)) return uit;
        foreach (var groep in factuurIds.Distinct().Chunk(500))
        {
            await using var cmd = new MySqlCommand(
                "SELECT `FactuurID`, SUM(`Bedrag`) FROM `FactuurBetalingen` " +
                $" WHERE `FactuurID` IN ({string.Join(",", groep)}) GROUP BY `FactuurID`", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) uit[r.GetInt32(0)] = r.GetDecimal(1);
        }
        return uit;
    }

    public async Task<bool> VoegToeAsync(int factuurId, DateOnly datum, decimal bedrag, string soort, string opmerking, int? door)
    {
        await using var cn = await OpenAsync();
        if (!await TabelAsync(cn)) throw new InvalidOperationException("Deelbetalingen kunnen pas na FactuurBetalingen_MariaDB.sql.");
        await using var tx = await cn.BeginTransactionAsync();
        await VoegAsync(cn, tx, factuurId, datum, bedrag, soort, null, opmerking, door);
        var rond = await MaakRondAsync(cn, tx, factuurId, door);
        await tx.CommitAsync();
        return rond;
    }

    public async Task VerwijderAsync(int betalingId)
    {
        await using var cn = await OpenAsync();
        if (!await TabelAsync(cn)) return;
        await using var tx = await cn.BeginTransactionAsync();
        int factuur = 0;
        int? bank = null;
        await using (var cmd = new MySqlCommand(
            "SELECT `FactuurID`, `BanktransactieID` FROM `FactuurBetalingen` WHERE `FactuurBetalingID` = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", betalingId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return;
            factuur = r.GetInt32(0);
            bank = r.IsDBNull(1) ? null : r.GetInt32(1);
        }
        await using (var cmd = new MySqlCommand("DELETE FROM `FactuurBetalingen` WHERE `FactuurBetalingID` = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", betalingId);
            await cmd.ExecuteNonQueryAsync();
        }
        // De bankkoppeling ook weg, tenzij dezelfde transactie nog een regel bij deze factuur heeft; zo komt de
        // ontvangst weer in de betalingscheck.
        if (bank is int b)
        {
            await using var cmd = new MySqlCommand(
                "DELETE FROM `banktransacties_facturen_crossref` WHERE `Banktransacties-id` = @b AND `Facturenuitgaand-id` = @f " +
                "   AND NOT EXISTS (SELECT 1 FROM `FactuurBetalingen` WHERE `BanktransactieID` = @b AND `FactuurID` = @f)", cn, tx);
            cmd.Parameters.AddWithValue("@b", b);
            cmd.Parameters.AddWithValue("@f", factuur);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task<(int Betaald, int Deels)> VerwerkAsync(IReadOnlyList<BetalingVoorstel> voorstellen, int? door)
    {
        await using var cn = await OpenAsync();
        if (!await TabelAsync(cn)) throw new InvalidOperationException("Deelbetalingen kunnen pas na FactuurBetalingen_MariaDB.sql.");
        await using var tx = await cn.BeginTransactionAsync();
        int betaald = 0, deels = 0;
        var gekoppeld = new List<(int, int)>();
        foreach (var v in voorstellen.Where(v => v.KanVerwerken))
        {
            foreach (var d in Betalingsmatcher.Verdeel(v))
            {
                gekoppeld.Add((d.Ontvangst.Id, d.FactuurId));
                await VoegAsync(cn, tx, d.FactuurId, d.Ontvangst.Datum, d.Bedrag, BetalingSoort.Bank, d.Ontvangst.Id, "", door);
                await using var cmd = new MySqlCommand(
                    "INSERT INTO `banktransacties_facturen_crossref` (`Banktransacties-id`, `Facturenuitgaand-id`) " +
                    "SELECT @b, @f FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM `banktransacties_facturen_crossref` " +
                    "  WHERE `Banktransacties-id` = @b AND `Facturenuitgaand-id` = @f)", cn, tx);
                cmd.Parameters.AddWithValue("@b", d.Ontvangst.Id);
                cmd.Parameters.AddWithValue("@f", d.FactuurId);
                await cmd.ExecuteNonQueryAsync();
            }
            foreach (var f in v.Facturen)
                if (await MaakRondAsync(cn, tx, f.Id, door)) betaald++; else deels++;
        }
        await tx.CommitAsync();
        // de tegenrekeningen bij de klanten onthouden (RelatieIbans), na het vastleggen
        await RelatieIbans.LeerAllesAsync(cn, gekoppeld, verkoop: true, door, "betalingscheck");
        return (betaald, deels);
    }

    private static async Task VoegAsync(MySqlConnection cn, MySqlTransaction tx, int factuurId, DateOnly datum, decimal bedrag,
                                        string soort, int? bank, string opmerking, int? door)
    {
        await using var cmd = new MySqlCommand(
            "INSERT INTO `FactuurBetalingen` (`FactuurID`, `Datum`, `Bedrag`, `Soort`, `BanktransactieID`, `Opmerking`, " +
            "       `AangemaaktOp`, `AangemaaktDoor`) VALUES (@f, @d, @b, @s, @bank, @o, NOW(), @door)", cn, tx);
        cmd.Parameters.AddWithValue("@f", factuurId);
        cmd.Parameters.AddWithValue("@d", datum.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@b", Math.Round(bedrag, 2));
        cmd.Parameters.AddWithValue("@s", soort);
        cmd.Parameters.AddWithValue("@bank", (object?)bank ?? DBNull.Value);
        var o = opmerking.Trim();
        cmd.Parameters.AddWithValue("@o", o.Length == 0 ? DBNull.Value : o.Length > 255 ? o[..255] : o);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Halen de regels samen het factuurbedrag? Dan Betaald op de datum van de laatste regel. True = betaald.</summary>
    private static async Task<bool> MaakRondAsync(MySqlConnection cn, MySqlTransaction tx, int factuurId, int? door)
    {
        decimal totaal, som;
        bool betaald;
        DateTime? laatste;
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(f.`Total incl`, 0), COALESCE(f.`Betaald`, 0) <> 0, " +
            "       (SELECT COALESCE(SUM(b.`Bedrag`), 0) FROM `FactuurBetalingen` b WHERE b.`FactuurID` = f.`Invoice-id`), " +
            "       (SELECT MAX(b.`Datum`) FROM `FactuurBetalingen` b WHERE b.`FactuurID` = f.`Invoice-id`) " +
            "  FROM `Invoices` f WHERE f.`Invoice-id` = @f", cn, tx))
        {
            cmd.Parameters.AddWithValue("@f", factuurId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return false;
            totaal = Convert.ToDecimal(r.GetValue(0));
            betaald = Convert.ToInt64(r.GetValue(1)) != 0;
            som = Convert.ToDecimal(r.GetValue(2));
            laatste = r.IsDBNull(3) ? null : r.GetDateTime(3);
        }
        if (betaald) return true;
        if (totaal <= 0 || som < totaal - Betalingsmatcher.Marge) return false;
        await using (var cmd = new MySqlCommand(
            "UPDATE `Invoices` SET `Betaald` = 1, `DatumBetaald` = @op, `BetaaldDoor` = @door " +
            " WHERE `Invoice-id` = @f AND COALESCE(`Betaald`, 0) = 0", cn, tx))
        {
            cmd.Parameters.AddWithValue("@op", laatste ?? DateTime.Today);
            cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@f", factuurId);
            await cmd.ExecuteNonQueryAsync();
        }
        return true;
    }
}
