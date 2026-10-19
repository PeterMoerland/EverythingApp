using System.Text.Json;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een vraag aan het taalmodel (tabel AiVragen, AiVragen_MariaDB.sql).</summary>
public class AiVraag
{
    public int Id { get; init; }
    public int WerknemerId { get; init; }
    public string Wie { get; init; } = "";
    public string Vraag { get; init; } = "";
    public string Prioriteit { get; init; } = AiVraag.Direct;
    public string Status { get; init; } = AiVraag.Wacht;
    public DateTime Aangemaakt { get; init; }
    public DateTime? Gestart { get; init; }
    public DateTime? Klaar { get; init; }
    public string Antwoord { get; init; } = "";
    public string GegevensJson { get; init; } = "";
    public string Model { get; init; } = "";
    public string Laag { get; init; } = "";
    public int? TokensIn { get; init; }
    public int? TokensUit { get; init; }
    public decimal? Kosten { get; init; }
    public string Fout { get; init; } = "";

    public const string Direct = "direct", Wachten = "wachten";
    public const string Wacht = "wacht", Bezig = "bezig", KlaarStatus = "klaar", FoutStatus = "fout", Geannuleerd = "geannuleerd";

    public bool Loopt => Status is Wacht or Bezig;

    public List<AiGegevens> Gegevens
    {
        get
        {
            if (GegevensJson.Length == 0) return new();
            try { return JsonSerializer.Deserialize<List<AiGegevens>>(GegevensJson) ?? new(); }
            catch { return new(); }
        }
    }
}

/// <summary>Wat het model opvroeg en wat het kreeg: één tabel per opvraging. Zo is elk antwoord na te rekenen.</summary>
public class AiGegevens
{
    public string Naam { get; set; } = "";             // de tool, bv. omzet_per_maand
    public string Invoer { get; set; } = "";           // de parameters, als JSON
    public string Titel { get; set; } = "";
    public List<string> Kolommen { get; set; } = new();
    public List<List<string>> Rijen { get; set; } = new();
    public string Opmerking { get; set; } = "";         // bv. "afgekapt op 200 regels" of een foutmelding
}

public interface IAiVraagRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();
    Task<int> MaakAsync(int werknemerId, string vraag, string prioriteit);
    Task<AiVraag?> GetAsync(int id);
    /// <summary>De laatste vragen; werknemerId null = van iedereen.</summary>
    Task<List<AiVraag>> LijstAsync(int? werknemerId, int max);
    /// <summary>Vragen met status 'wacht' (oudste eerst), om uit te voeren.</summary>
    Task<List<AiVraag>> WachtendAsync();
    /// <summary>Zet 'wacht' op 'bezig'; false als een ander hem al heeft (of hij is geannuleerd).</summary>
    Task<bool> ClaimAsync(int id);
    Task KlaarAsync(int id, string antwoord, string gegevensJson, string model, string laag, int tokensIn, int tokensUit, decimal? kosten);
    Task FoutAsync(int id, string fout, string gegevensJson, string model, string laag, int tokensIn, int tokensUit, decimal? kosten);
    Task<bool> AnnuleerAsync(int id, int werknemerId);
    /// <summary>Vragen die al lang 'bezig' zijn (app herstart tijdens het werk) weer op 'wacht'.</summary>
    Task<int> HerstelHangendAsync(TimeSpan ouderDan);
}

public class GeenAiVraagRepository : IAiVraagRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<int> MaakAsync(int werknemerId, string vraag, string prioriteit) => Task.FromResult(0);
    public Task<AiVraag?> GetAsync(int id) => Task.FromResult<AiVraag?>(null);
    public Task<List<AiVraag>> LijstAsync(int? werknemerId, int max) => Task.FromResult(new List<AiVraag>());
    public Task<List<AiVraag>> WachtendAsync() => Task.FromResult(new List<AiVraag>());
    public Task<bool> ClaimAsync(int id) => Task.FromResult(false);
    public Task KlaarAsync(int id, string antwoord, string gegevensJson, string model, string laag, int tokensIn, int tokensUit, decimal? kosten) => Task.CompletedTask;
    public Task FoutAsync(int id, string fout, string gegevensJson, string model, string laag, int tokensIn, int tokensUit, decimal? kosten) => Task.CompletedTask;
    public Task<bool> AnnuleerAsync(int id, int werknemerId) => Task.FromResult(false);
    public Task<int> HerstelHangendAsync(TimeSpan ouderDan) => Task.FromResult(0);
}

/// <summary>Alleen om lokaal te testen zonder de tabel: AiVragen__Geheugen=true.</summary>
public class GeheugenAiVraagRepository : IAiVraagRepository
{
    private readonly List<AiVraag> _lijst = new();
    private readonly object _slot = new();
    private int _volgende = 1;

    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);

    public Task<int> MaakAsync(int werknemerId, string vraag, string prioriteit)
    {
        lock (_slot)
        {
            var id = _volgende++;
            _lijst.Add(new AiVraag { Id = id, WerknemerId = werknemerId, Wie = "ik", Vraag = vraag, Prioriteit = prioriteit,
                                     Status = AiVraag.Wacht, Aangemaakt = DateTime.Now });
            return Task.FromResult(id);
        }
    }

    public Task<AiVraag?> GetAsync(int id) { lock (_slot) return Task.FromResult(_lijst.FirstOrDefault(v => v.Id == id)); }
    public Task<List<AiVraag>> LijstAsync(int? werknemerId, int max)
    { lock (_slot) return Task.FromResult(_lijst.Where(v => werknemerId is null || v.WerknemerId == werknemerId).OrderByDescending(v => v.Id).Take(max).ToList()); }
    public Task<List<AiVraag>> WachtendAsync()
    { lock (_slot) return Task.FromResult(_lijst.Where(v => v.Status == AiVraag.Wacht).OrderBy(v => v.Id).ToList()); }

    private void Vervang(int id, Func<AiVraag, AiVraag> maak)
    {
        var i = _lijst.FindIndex(v => v.Id == id);
        if (i >= 0) _lijst[i] = maak(_lijst[i]);
    }

    private static AiVraag Kopie(AiVraag v, string status, DateTime? gestart = null, DateTime? klaar = null, string antwoord = "",
                                 string gegevens = "", string model = "", string laag = "", int? tin = null, int? tuit = null,
                                 decimal? kosten = null, string fout = "")
        => new()
        {
            Id = v.Id, WerknemerId = v.WerknemerId, Wie = v.Wie, Vraag = v.Vraag, Prioriteit = v.Prioriteit, Status = status,
            Aangemaakt = v.Aangemaakt, Gestart = gestart ?? v.Gestart, Klaar = klaar, Antwoord = antwoord, GegevensJson = gegevens,
            Model = model, Laag = laag, TokensIn = tin, TokensUit = tuit, Kosten = kosten, Fout = fout,
        };

    public Task<bool> ClaimAsync(int id)
    {
        lock (_slot)
        {
            var v = _lijst.FirstOrDefault(x => x.Id == id);
            if (v is null || v.Status != AiVraag.Wacht) return Task.FromResult(false);
            Vervang(id, x => Kopie(x, AiVraag.Bezig, gestart: DateTime.Now));
            return Task.FromResult(true);
        }
    }

    public Task KlaarAsync(int id, string antwoord, string gegevensJson, string model, string laag, int tokensIn, int tokensUit, decimal? kosten)
    { lock (_slot) Vervang(id, x => Kopie(x, AiVraag.KlaarStatus, klaar: DateTime.Now, antwoord: antwoord, gegevens: gegevensJson, model: model, laag: laag, tin: tokensIn, tuit: tokensUit, kosten: kosten)); return Task.CompletedTask; }

    public Task FoutAsync(int id, string fout, string gegevensJson, string model, string laag, int tokensIn, int tokensUit, decimal? kosten)
    { lock (_slot) Vervang(id, x => Kopie(x, AiVraag.FoutStatus, klaar: DateTime.Now, gegevens: gegevensJson, model: model, laag: laag, tin: tokensIn, tuit: tokensUit, kosten: kosten, fout: fout)); return Task.CompletedTask; }

    public Task<bool> AnnuleerAsync(int id, int werknemerId)
    {
        lock (_slot)
        {
            var v = _lijst.FirstOrDefault(x => x.Id == id && x.WerknemerId == werknemerId && x.Status == AiVraag.Wacht);
            if (v is null) return Task.FromResult(false);
            Vervang(id, x => Kopie(x, AiVraag.Geannuleerd, klaar: DateTime.Now));
            return Task.FromResult(true);
        }
    }

    public Task<int> HerstelHangendAsync(TimeSpan ouderDan) => Task.FromResult(0);
}

public class MySqlAiVraagRepository : IAiVraagRepository
{
    private readonly string _cs;
    private bool _tabel;
    public MySqlAiVraagRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'AiVragen'", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        return _tabel;
    }

    private const string Select =
        "SELECT v.`AiVraagID`, v.`WerknemerID`, COALESCE(NULLIF(w.`Afkorting`, ''), w.`Voornaam`, ''), v.`Vraag`, v.`Prioriteit`, " +
        "       v.`Status`, v.`Aangemaakt`, v.`Gestart`, v.`Klaar`, {0}, {1}, v.`Model`, v.`Laag`, v.`TokensIn`, v.`TokensUit`, " +
        "       v.`Kosten`, v.`Fout` " +
        "  FROM `AiVragen` v LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = v.`WerknemerID` ";

    private static async Task<List<AiVraag>> Lees(MySqlCommand cmd)
    {
        var lijst = new List<AiVraag>();
        await using var r = await cmd.ExecuteReaderAsync();
        string S(int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
        int? I(int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
        DateTime? D(int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
        while (await r.ReadAsync())
            lijst.Add(new AiVraag
            {
                Id = r.GetInt32(0), WerknemerId = r.GetInt32(1), Wie = S(2), Vraag = S(3), Prioriteit = S(4), Status = S(5),
                Aangemaakt = r.GetDateTime(6), Gestart = D(7), Klaar = D(8), Antwoord = S(9), GegevensJson = S(10),
                Model = S(11), Laag = S(12), TokensIn = I(13), TokensUit = I(14),
                Kosten = r.IsDBNull(15) ? null : r.GetDecimal(15), Fout = S(16),
            });
        return lijst;
    }

    public async Task<int> MaakAsync(int werknemerId, string vraag, string prioriteit)
    {
        if (!await TabelAanwezigAsync()) throw new InvalidOperationException("De tabel AiVragen bestaat nog niet (AiVragen_MariaDB.sql).");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `AiVragen` (`WerknemerID`, `Vraag`, `Prioriteit`, `Status`, `Aangemaakt`) " +
            "VALUES (@wn, @vraag, @prio, 'wacht', NOW()); SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        cmd.Parameters.AddWithValue("@vraag", vraag);
        cmd.Parameters.AddWithValue("@prio", prioriteit);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<AiVraag?> GetAsync(int id)
    {
        if (!await TabelAanwezigAsync()) return null;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(string.Format(Select, "v.`Antwoord`", "v.`Gegevens`") + " WHERE v.`AiVraagID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        return (await Lees(cmd)).FirstOrDefault();
    }

    public async Task<List<AiVraag>> LijstAsync(int? werknemerId, int max)
    {
        if (!await TabelAanwezigAsync()) return new();
        await using var cn = await OpenAsync();
        // in de lijst geen antwoord en gegevens (kan groot zijn)
        await using var cmd = new MySqlCommand(string.Format(Select, "NULL", "NULL") +
            (werknemerId is null ? "" : " WHERE v.`WerknemerID` = @wn ") +
            " ORDER BY v.`AiVraagID` DESC LIMIT @max", cn);
        if (werknemerId is not null) cmd.Parameters.AddWithValue("@wn", werknemerId);
        cmd.Parameters.AddWithValue("@max", max);
        return await Lees(cmd);
    }

    public async Task<List<AiVraag>> WachtendAsync()
    {
        if (!await TabelAanwezigAsync()) return new();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(string.Format(Select, "NULL", "NULL") +
            " WHERE v.`Status` = 'wacht' ORDER BY v.`AiVraagID` LIMIT 50", cn);
        return await Lees(cmd);
    }

    public async Task<bool> ClaimAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `AiVragen` SET `Status` = 'bezig', `Gestart` = NOW() WHERE `AiVraagID` = @id AND `Status` = 'wacht'", cn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    private async Task Afronden(int id, string status, string? antwoord, string? fout, string gegevensJson, string model,
                                string laag, int tokensIn, int tokensUit, decimal? kosten)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `AiVragen` SET `Status` = @status, `Klaar` = NOW(), `Antwoord` = @antwoord, `Gegevens` = @gegevens, " +
            "       `Model` = @model, `Laag` = @laag, `TokensIn` = @tin, `TokensUit` = @tuit, `Kosten` = @kosten, `Fout` = @fout " +
            " WHERE `AiVraagID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@antwoord", (object?)antwoord ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gegevens", gegevensJson.Length == 0 ? DBNull.Value : gegevensJson);
        cmd.Parameters.AddWithValue("@model", model);
        cmd.Parameters.AddWithValue("@laag", laag);
        cmd.Parameters.AddWithValue("@tin", tokensIn);
        cmd.Parameters.AddWithValue("@tuit", tokensUit);
        cmd.Parameters.AddWithValue("@kosten", (object?)kosten ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fout", fout is null ? DBNull.Value : (fout.Length > 1000 ? fout[..1000] : fout));
        await cmd.ExecuteNonQueryAsync();
    }

    public Task KlaarAsync(int id, string antwoord, string gegevensJson, string model, string laag, int tokensIn, int tokensUit, decimal? kosten)
        => Afronden(id, AiVraag.KlaarStatus, antwoord, null, gegevensJson, model, laag, tokensIn, tokensUit, kosten);

    public Task FoutAsync(int id, string fout, string gegevensJson, string model, string laag, int tokensIn, int tokensUit, decimal? kosten)
        => Afronden(id, AiVraag.FoutStatus, null, fout, gegevensJson, model, laag, tokensIn, tokensUit, kosten);

    public async Task<bool> AnnuleerAsync(int id, int werknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `AiVragen` SET `Status` = 'geannuleerd', `Klaar` = NOW() " +
            " WHERE `AiVraagID` = @id AND `WerknemerID` = @wn AND `Status` = 'wacht'", cn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    public async Task<int> HerstelHangendAsync(TimeSpan ouderDan)
    {
        if (!await TabelAanwezigAsync()) return 0;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `AiVragen` SET `Status` = 'wacht', `Gestart` = NULL " +
            " WHERE `Status` = 'bezig' AND `Gestart` < NOW() - INTERVAL @min MINUTE", cn);
        cmd.Parameters.AddWithValue("@min", (int)ouderDan.TotalMinutes);
        return await cmd.ExecuteNonQueryAsync();
    }
}
