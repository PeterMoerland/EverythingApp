using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>Een melding op een vaste tijd op de Sonos-speakers (tabel OmroepMeldingen, Beheer ▸ Omroep).</summary>
public class OmroepMelding
{
    public int Id { get; set; }
    public string Tekst { get; set; } = "";
    /// <summary>Op welke dagen: de cijfers 1 (maandag) t/m 7 (zondag) achter elkaar, bv. "5" = vrijdag, "12345" = werkdagen.</summary>
    public string Dagen { get; set; } = "";
    public TimeOnly Tijd { get; set; } = new(9, 0);
    /// <summary>Uuid's van de speakers met een komma ertussen; leeg = alle speakers.</summary>
    public string Speakers { get; set; } = "";
    /// <summary>Volume van de melding (1-100); null = het standaardvolume (Omroep:Volume).</summary>
    public int? Volume { get; set; }
    /// <summary>Eerst een ding-dong.</summary>
    public bool Toon { get; set; } = true;
    public bool Actief { get; set; } = true;
    public DateTime? LaatstGedaan { get; set; }
    public string LaatsteUitkomst { get; set; } = "";

    public List<string> SpeakerLijst => Speakers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    public bool OpDag(int dag) => Dagen.Contains((char)('0' + dag));
}

public static class OmroepRegels
{
    public static readonly string[] Dagnamen = { "ma", "di", "wo", "do", "vr", "za", "zo" };

    /// <summary>1 = maandag … 7 = zondag.</summary>
    public static int Dagnummer(DayOfWeek dag) => dag == DayOfWeek.Sunday ? 7 : (int)dag;

    /// <summary>
    /// Is deze melding nu aan de beurt? Actief, vandaag een van zijn dagen, de tijd is bereikt (en hooguit
    /// <paramref name="vensterMinuten"/> geleden: na een lange storing niet alsnog om elf uur omroepen), en vandaag
    /// nog niet gedaan.
    /// </summary>
    public static bool MoetNu(OmroepMelding m, DateTime nu, int vensterMinuten = 10)
    {
        if (!m.Actief || m.Tekst.Trim().Length == 0 || !m.OpDag(Dagnummer(nu.DayOfWeek))) return false;
        var moment = nu.Date + m.Tijd.ToTimeSpan();
        return nu >= moment && nu < moment.AddMinutes(vensterMinuten) && (m.LaatstGedaan is null || m.LaatstGedaan < moment);
    }
}

public interface IOmroepRepository
{
    bool Beschikbaar { get; }
    /// <summary>Bestaat de tabel OmroepMeldingen al (Omroep_MariaDB.sql)?</summary>
    Task<bool> TabelAanwezigAsync();
    Task<List<OmroepMelding>> LijstAsync();
    /// <summary>Nieuw (Id 0) of wijzigen; geeft het Id.</summary>
    Task<int> BewaarAsync(OmroepMelding m, int? door);
    Task VerwijderAsync(int id);
    Task ZetGedaanAsync(int id, DateTime op, string uitkomst);
}

public class GeenOmroepRepository : IOmroepRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<OmroepMelding>> LijstAsync() => Task.FromResult(new List<OmroepMelding>());
    public Task<int> BewaarAsync(OmroepMelding m, int? door) => Task.FromResult(0);
    public Task VerwijderAsync(int id) => Task.CompletedTask;
    public Task ZetGedaanAsync(int id, DateTime op, string uitkomst) => Task.CompletedTask;
}

public class MySqlOmroepRepository : IOmroepRepository
{
    private readonly string _cs;
    public MySqlOmroepRepository(string cs) => _cs = cs;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<bool> TabelAanwezigAsync()
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'OmroepMeldingen'", cn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    public async Task<List<OmroepMelding>> LijstAsync()
    {
        var lijst = new List<OmroepMelding>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `OmroepID`, COALESCE(`Tekst`, ''), COALESCE(`Dagen`, ''), `Tijd`, COALESCE(`Speakers`, ''), `Volume`, " +
            "       COALESCE(`Toon`, 1), COALESCE(`Actief`, 1), `LaatstGedaan`, COALESCE(`LaatsteUitkomst`, '') " +
            "  FROM `OmroepMeldingen` ORDER BY `Tijd`, `OmroepID`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new OmroepMelding
            {
                Id = r.GetInt32(0), Tekst = r.GetString(1), Dagen = r.GetString(2),
                Tijd = r.IsDBNull(3) ? new TimeOnly(9, 0) : TimeOnly.FromTimeSpan(r.GetTimeSpan(3)),
                Speakers = r.GetString(4),
                Volume = r.IsDBNull(5) ? null : Convert.ToInt32(r.GetValue(5)),
                Toon = Convert.ToInt64(r.GetValue(6)) != 0, Actief = Convert.ToInt64(r.GetValue(7)) != 0,
                LaatstGedaan = r.IsDBNull(8) ? null : r.GetDateTime(8), LaatsteUitkomst = r.GetString(9),
            });
        return lijst;
    }

    public async Task<int> BewaarAsync(OmroepMelding m, int? door)
    {
        const string velden = "`Tekst` = @t, `Dagen` = @d, `Tijd` = @tijd, `Speakers` = @s, `Volume` = @v, `Toon` = @toon, `Actief` = @a, " +
                              "`GewijzigdOp` = NOW(), `GewijzigdDoor` = @door";
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(m.Id == 0
            ? $"INSERT INTO `OmroepMeldingen` SET {velden}"
            : $"UPDATE `OmroepMeldingen` SET {velden} WHERE `OmroepID` = @id", cn);
        var tekst = (m.Tekst ?? "").Trim();
        cmd.Parameters.AddWithValue("@t", tekst.Length > 500 ? tekst[..500] : tekst);
        cmd.Parameters.AddWithValue("@d", new string((m.Dagen ?? "").Where(c => c is >= '1' and <= '7').Distinct().OrderBy(c => c).ToArray()));
        cmd.Parameters.AddWithValue("@tijd", m.Tijd.ToTimeSpan());
        cmd.Parameters.AddWithValue("@s", string.Join(",", m.SpeakerLijst));
        cmd.Parameters.AddWithValue("@v", m.Volume is int v ? Math.Clamp(v, 1, 100) : DBNull.Value);
        cmd.Parameters.AddWithValue("@toon", m.Toon ? 1 : 0);
        cmd.Parameters.AddWithValue("@a", m.Actief ? 1 : 0);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        if (m.Id != 0) cmd.Parameters.AddWithValue("@id", m.Id);
        await cmd.ExecuteNonQueryAsync();
        return m.Id == 0 ? (int)cmd.LastInsertedId : m.Id;
    }

    public async Task VerwijderAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM `OmroepMeldingen` WHERE `OmroepID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetGedaanAsync(int id, DateTime op, string uitkomst)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `OmroepMeldingen` SET `LaatstGedaan` = @op, `LaatsteUitkomst` = @u WHERE `OmroepID` = @id", cn);
        cmd.Parameters.AddWithValue("@op", op);
        cmd.Parameters.AddWithValue("@u", uitkomst.Length > 300 ? uitkomst[..300] : uitkomst);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
