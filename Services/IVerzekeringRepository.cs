using System.Collections.Concurrent;
using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

// ============================================================================================================
//  Verzekeringen (wens gebruiker 9-11-2026): alle verzekeringen met de polissen (Financiën > Organisatie >
//  Verzekeringen), alleen Financial en Directie. Tabel Verzekeringen (Verzekeringen_MariaDB.sql); documenten in de
//  filestore StoreType 361. Herinnering in de bel vóór de opzegdatum: VerzekeringWaker.
// ============================================================================================================

public static class VerzekeringSoort
{
    public static readonly (string Code, string Naam)[] Alle =
    {
        ("aansprakelijkheid", "Aansprakelijkheid"), ("brand", "Brand / opstal / inventaris"), ("materieel", "Werkmaterieel"),
        ("wagenpark", "Wagenpark"), ("transport", "Transport"), ("verzuim", "Verzuim"), ("wia", "WIA / arbeidsongeschiktheid"),
        ("rechtsbijstand", "Rechtsbijstand"), ("reis", "Reis"), ("cyber", "Cyber"), ("bestuurders", "Bestuurders (D&O)"),
        ("overig", "Overig"),
    };

    public static string Naam(string code) => Alle.FirstOrDefault(s => s.Code == code).Naam ?? code;

    /// <summary>Een eerste gok op de soort uit de omschrijving (bij overnemen uit de vaste lasten).</summary>
    public static string Raad(string omschrijving)
    {
        var o = omschrijving.ToLowerInvariant();
        bool Bevat(params string[] w) => w.Any(o.Contains);
        if (Bevat("auto", "renault", "tesla", "wagen", "bus ")) return "wagenpark";
        if (Bevat("verzuim")) return "verzuim";
        if (Bevat("wia", "arbeidsongeschikt")) return "wia";
        if (Bevat("rechtsbijstand", "arag")) return "rechtsbijstand";
        if (Bevat("reis")) return "reis";
        if (Bevat("brand", "opstal", "inventaris")) return "brand";
        if (Bevat("aansprakelijk", "avb")) return "aansprakelijkheid";
        if (Bevat("transport")) return "transport";
        if (Bevat("materieel", "machine")) return "materieel";
        if (Bevat("cyber")) return "cyber";
        return "overig";
    }
}

public static class VerzekeringStatus
{
    public const string Actief = "actief", Opgezegd = "opgezegd", Beeindigd = "beeindigd";
    public static readonly string[] Alle = { Actief, Opgezegd, Beeindigd };
    public static string Naam(string s) => s == Beeindigd ? "beëindigd" : s;
}

public class Verzekering
{
    public int Id { get; set; }
    public int? BvId { get; set; }
    public string Bv { get; set; } = "";
    public string Soort { get; set; } = "overig";
    public string Omschrijving { get; set; } = "";
    public int? VerzekeraarId { get; set; }
    public string Verzekeraar { get; set; } = "";
    public int? TussenpersoonId { get; set; }
    public string Tussenpersoon { get; set; } = "";
    public string Polisnummer { get; set; } = "";
    public decimal? VerzekerdBedrag { get; set; }
    public decimal? EigenRisico { get; set; }
    public decimal? Premie { get; set; }
    public VasteLastInterval? PremieInterval { get; set; }
    public int? VasteLastId { get; set; }
    /// <summary>De gekoppelde vaste last (alleen lezen): omschrijving, bedrag per termijn, interval.</summary>
    public string VasteLast { get; set; } = "";
    public decimal? VasteLastBedrag { get; set; }
    public VasteLastInterval? VasteLastTermijn { get; set; }
    public DateOnly? Ingangsdatum { get; set; }
    public DateOnly? Contractvervaldatum { get; set; }
    public bool StilzwijgendVerlengd { get; set; } = true;
    public int VerlengingMaanden { get; set; } = 12;
    public int? OpzegtermijnMaanden { get; set; }
    public string Status { get; set; } = VerzekeringStatus.Actief;
    public int? BeheerderId { get; set; }
    public string Beheerder { get; set; } = "";
    public string Dekking { get; set; } = "";
    public string Opmerking { get; set; } = "";
    public DateOnly? GemeldVoor { get; set; }
    public int Documenten { get; set; }

    /// <summary>Hoeveel dagen vóór de uiterste opzegdatum de herinnering komt.</summary>
    public const int HerinneringDagen = 30;

    /// <summary>De eerstvolgende vervaldatum vanaf vandaag (bij stilzwijgend verlengen telkens een termijn verder).</summary>
    public DateOnly? VolgendeVervaldatum(DateOnly vandaag)
    {
        if (Contractvervaldatum is not DateOnly d) return null;
        if (!StilzwijgendVerlengd) return d;
        var stap = VerlengingMaanden > 0 ? VerlengingMaanden : 12;
        for (var i = 0; d < vandaag && i < 600; i++) d = d.AddMonths(stap);
        return d;
    }

    /// <summary>Uiterlijk opzeggen: de volgende vervaldatum min de opzegtermijn.</summary>
    public DateOnly? UitersteOpzegdatum(DateOnly vandaag)
        => VolgendeVervaldatum(vandaag) is DateOnly v ? v.AddMonths(-(OpzegtermijnMaanden ?? 0)) : null;

    /// <summary>Vanaf wanneer de herinnering komt.</summary>
    public DateOnly? HerinneringVanaf(DateOnly vandaag)
        => UitersteOpzegdatum(vandaag) is DateOnly o ? o.AddDays(-HerinneringDagen) : null;

    /// <summary>"rood" = de opzegdatum voor deze termijn is voorbij, "oranje" = binnen de herinnering, anders "".</summary>
    public string Stand(DateOnly vandaag)
    {
        if (Status != VerzekeringStatus.Actief || UitersteOpzegdatum(vandaag) is not DateOnly o) return "";
        if (vandaag > o) return "rood";
        return vandaag >= o.AddDays(-HerinneringDagen) ? "oranje" : "";
    }

    /// <summary>Premie per jaar: uit de vaste last als die er is, anders premie × termijnen.</summary>
    public decimal? PremiePerJaar
    {
        get
        {
            var (bedrag, interval) = VasteLastId is not null ? (VasteLastBedrag, VasteLastTermijn) : (Premie, PremieInterval);
            if (bedrag is not decimal b || interval is not VasteLastInterval i) return null;
            return b * i switch { VasteLastInterval.Wekelijks => 52, VasteLastInterval.Maandelijks => 12, VasteLastInterval.Kwartaal => 4, _ => 1 };
        }
    }
}

/// <summary>Een vaste last in de categorie Verzekeringen die nog niet aan een verzekering hangt.</summary>
public record VerzekeringUitVasteLast(int VasteLastId, string Omschrijving, int? BvId);

public interface IVerzekeringRepository
{
    bool Beschikbaar { get; }
    Task<bool> TabelAanwezigAsync();
    Task<List<Verzekering>> LijstAsync();
    Task<int> BewaarAsync(Verzekering v, int? door);
    /// <summary>Wissen kan alleen zonder documenten; geeft anders een reden.</summary>
    Task<string?> WisAsync(int id);
    /// <summary>Vaste lasten in een categorie met "verzeker" in de naam, nog niet gekoppeld.</summary>
    Task<List<VerzekeringUitVasteLast>> NietGekoppeldAsync();
    /// <summary>Werknemers in dienst, voor de beheerder.</summary>
    Task<List<Keuze>> WerknemersAsync();
    /// <summary>Directieleden in dienst (krijgen de herinnering).</summary>
    Task<List<int>> DirectieAsync();
    Task ZetGemeldAsync(int id, DateOnly vervaldatum);
}

public class GeenVerzekeringRepository : IVerzekeringRepository
{
    public bool Beschikbaar => false;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(false);
    public Task<List<Verzekering>> LijstAsync() => Task.FromResult(new List<Verzekering>());
    public Task<int> BewaarAsync(Verzekering v, int? door) => throw new InvalidOperationException("Er is geen database.");
    public Task<string?> WisAsync(int id) => Task.FromResult<string?>("Er is geen database.");
    public Task<List<VerzekeringUitVasteLast>> NietGekoppeldAsync() => Task.FromResult(new List<VerzekeringUitVasteLast>());
    public Task<List<Keuze>> WerknemersAsync() => Task.FromResult(new List<Keuze>());
    public Task<List<int>> DirectieAsync() => Task.FromResult(new List<int>());
    public Task ZetGemeldAsync(int id, DateOnly vervaldatum) => Task.CompletedTask;
}

/// <summary>Lokaal testen zonder de tabel: Verzekeringen__Geheugen=true. Leest vaste lasten en werknemers echt; schrijft in het geheugen.</summary>
public class GeheugenVerzekeringRepository : IVerzekeringRepository
{
    private readonly IVerzekeringRepository? _lezen;
    private readonly ConcurrentDictionary<int, Verzekering> _v = new();
    private int _volgende;

    public GeheugenVerzekeringRepository(IVerzekeringRepository? lezen = null) => _lezen = lezen;

    public bool Beschikbaar => true;
    public Task<bool> TabelAanwezigAsync() => Task.FromResult(true);
    public Task<List<Verzekering>> LijstAsync() => Task.FromResult(_v.Values.OrderBy(v => v.Omschrijving).Select(Kopie).ToList());

    private static Verzekering Kopie(Verzekering v) => (Verzekering)typeof(Verzekering).GetMethod("MemberwiseClone",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(v, null)!;

    public Task<int> BewaarAsync(Verzekering v, int? door)
    {
        if (v.Id <= 0) v.Id = Interlocked.Increment(ref _volgende);
        _v[v.Id] = Kopie(v);
        return Task.FromResult(v.Id);
    }

    public Task<string?> WisAsync(int id) { _v.TryRemove(id, out _); return Task.FromResult<string?>(null); }

    public async Task<List<VerzekeringUitVasteLast>> NietGekoppeldAsync()
    {
        var alle = _lezen is null ? new List<VerzekeringUitVasteLast>() : await _lezen.NietGekoppeldAsync();
        return alle.Where(a => _v.Values.All(v => v.VasteLastId != a.VasteLastId)).ToList();
    }

    public Task<List<Keuze>> WerknemersAsync() => _lezen?.WerknemersAsync() ?? Task.FromResult(new List<Keuze>());
    public Task<List<int>> DirectieAsync() => Task.FromResult(new List<int>());
    public Task ZetGemeldAsync(int id, DateOnly vervaldatum) { if (_v.TryGetValue(id, out var v)) v.GemeldVoor = vervaldatum; return Task.CompletedTask; }
}

public class MySqlVerzekeringRepository : IVerzekeringRepository
{
    private readonly string _cs;
    private bool _tabel;

    public MySqlVerzekeringRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : (r.GetValue(i).ToString() ?? "").Trim();
    private static int? IN(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static decimal? DN(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
    private static DateOnly? D(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : DateOnly.FromDateTime(Convert.ToDateTime(r.GetValue(i)));
    private static VasteLastInterval? Iv(MySqlDataReader r, int i)
        => r.IsDBNull(i) ? null : Enum.IsDefined(typeof(VasteLastInterval), Convert.ToInt32(r.GetValue(i))) ? (VasteLastInterval)Convert.ToInt32(r.GetValue(i)) : null;
    private static object Of(object? w) => w ?? DBNull.Value;
    private static object Datum(DateOnly? d) => d is DateOnly x ? x.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
    private static string Kort(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();

    public async Task<bool> TabelAanwezigAsync()
    {
        if (_tabel) return true;
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Verzekeringen'", cn);
            _tabel = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }
        catch { }
        return _tabel;
    }

    public async Task<List<Verzekering>> LijstAsync()
    {
        var lijst = new List<Verzekering>();
        if (!await TabelAanwezigAsync()) return lijst;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT v.`VerzekeringID`, v.`BvID`, COALESCE(NULLIF(TRIM(b.`Afkorting`), ''), b.`name`, ''), v.`Soort`, v.`Omschrijving`, " +
            "       v.`VerzekeraarID`, COALESCE(vz.`name`, ''), v.`TussenpersoonID`, COALESCE(tp.`name`, ''), COALESCE(v.`Polisnummer`, ''), " +
            "       v.`VerzekerdBedrag`, v.`EigenRisico`, v.`Premie`, v.`PremieInterval`, v.`VasteLastID`, COALESCE(vl.`Omschrijving`, ''), " +
            "       vl.`Bedrag`, vl.`interval`, v.`Ingangsdatum`, v.`Contractvervaldatum`, v.`StilzwijgendVerlengd`, v.`VerlengingMaanden`, " +
            "       v.`OpzegtermijnMaanden`, v.`Status`, v.`BeheerderID`, COALESCE(w.`NaamVoluit`, ''), COALESCE(v.`Dekking`, ''), " +
            "       COALESCE(v.`Opmerking`, ''), v.`GemeldVoor`, " +
            "       (SELECT COUNT(*) FROM `FileStore` f WHERE f.`StoreType` = @st AND f.`MasterID` = v.`VerzekeringID`) " +
            "  FROM `Verzekeringen` v " +
            "  LEFT JOIN `Relations` b ON b.`id_supplier` = v.`BvID` " +
            "  LEFT JOIN `Relations` vz ON vz.`id_supplier` = v.`VerzekeraarID` " +
            "  LEFT JOIN `Relations` tp ON tp.`id_supplier` = v.`TussenpersoonID` " +
            "  LEFT JOIN `VasteLasten` vl ON vl.`ID` = v.`VasteLastID` " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = v.`BeheerderID` " +
            " ORDER BY v.`Omschrijving`", cn);
        cmd.Parameters.AddWithValue("@st", FilestoreService.StoreTypeVerzekering);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Verzekering
            {
                Id = r.GetInt32(0), BvId = IN(r, 1), Bv = S(r, 2), Soort = S(r, 3), Omschrijving = S(r, 4),
                VerzekeraarId = IN(r, 5), Verzekeraar = S(r, 6), TussenpersoonId = IN(r, 7), Tussenpersoon = S(r, 8), Polisnummer = S(r, 9),
                VerzekerdBedrag = DN(r, 10), EigenRisico = DN(r, 11), Premie = DN(r, 12), PremieInterval = Iv(r, 13),
                VasteLastId = IN(r, 14), VasteLast = S(r, 15), VasteLastBedrag = DN(r, 16), VasteLastTermijn = Iv(r, 17),
                Ingangsdatum = D(r, 18), Contractvervaldatum = D(r, 19), StilzwijgendVerlengd = !r.IsDBNull(20) && Convert.ToInt64(r.GetValue(20)) != 0,
                VerlengingMaanden = IN(r, 21) ?? 12, OpzegtermijnMaanden = IN(r, 22), Status = S(r, 23), BeheerderId = IN(r, 24), Beheerder = S(r, 25),
                Dekking = S(r, 26), Opmerking = S(r, 27), GemeldVoor = D(r, 28), Documenten = Convert.ToInt32(r.GetValue(29)),
            });
        return lijst;
    }

    public async Task<int> BewaarAsync(Verzekering v, int? door)
    {
        const string velden =
            "`BvID` = @bv, `Soort` = @soort, `Omschrijving` = @oms, `VerzekeraarID` = @vz, `TussenpersoonID` = @tp, `Polisnummer` = @polis, " +
            "`VerzekerdBedrag` = @vb, `EigenRisico` = @er, `Premie` = @premie, `PremieInterval` = @pi, `VasteLastID` = @vl, " +
            "`Ingangsdatum` = @in, `Contractvervaldatum` = @cv, `StilzwijgendVerlengd` = @stil, `VerlengingMaanden` = @verl, " +
            "`OpzegtermijnMaanden` = @opz, `Status` = @status, `BeheerderID` = @beh, `Dekking` = @dekking, `Opmerking` = @opm";
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(v.Id <= 0
            ? $"INSERT INTO `Verzekeringen` SET {velden}, `AangemaaktOp` = NOW(), `AangemaaktDoor` = @door; SELECT LAST_INSERT_ID();"
            : $"UPDATE `Verzekeringen` SET {velden}, `GewijzigdOp` = NOW(), `GewijzigdDoor` = @door WHERE `VerzekeringID` = @id; SELECT @id;", cn);
        cmd.Parameters.AddWithValue("@bv", Of(v.BvId));
        cmd.Parameters.AddWithValue("@soort", VerzekeringSoort.Alle.Any(s => s.Code == v.Soort) ? v.Soort : "overig");
        cmd.Parameters.AddWithValue("@oms", Kort(v.Omschrijving.Length > 0 ? v.Omschrijving : "Verzekering", 200));
        cmd.Parameters.AddWithValue("@vz", Of(v.VerzekeraarId));
        cmd.Parameters.AddWithValue("@tp", Of(v.TussenpersoonId));
        cmd.Parameters.AddWithValue("@polis", Kort(v.Polisnummer, 60));
        cmd.Parameters.AddWithValue("@vb", Of(v.VerzekerdBedrag));
        cmd.Parameters.AddWithValue("@er", Of(v.EigenRisico));
        cmd.Parameters.AddWithValue("@premie", Of(v.Premie));
        cmd.Parameters.AddWithValue("@pi", v.PremieInterval is VasteLastInterval i ? (int)i : DBNull.Value);
        cmd.Parameters.AddWithValue("@vl", Of(v.VasteLastId));
        cmd.Parameters.AddWithValue("@in", Datum(v.Ingangsdatum));
        cmd.Parameters.AddWithValue("@cv", Datum(v.Contractvervaldatum));
        cmd.Parameters.AddWithValue("@stil", v.StilzwijgendVerlengd ? 1 : 0);
        cmd.Parameters.AddWithValue("@verl", v.VerlengingMaanden > 0 ? v.VerlengingMaanden : 12);
        cmd.Parameters.AddWithValue("@opz", Of(v.OpzegtermijnMaanden));
        cmd.Parameters.AddWithValue("@status", VerzekeringStatus.Alle.Contains(v.Status) ? v.Status : VerzekeringStatus.Actief);
        cmd.Parameters.AddWithValue("@beh", Of(v.BeheerderId));
        cmd.Parameters.AddWithValue("@dekking", Kort(v.Dekking, 2000));
        cmd.Parameters.AddWithValue("@opm", Kort(v.Opmerking, 2000));
        cmd.Parameters.AddWithValue("@door", Of(door));
        cmd.Parameters.AddWithValue("@id", v.Id);
        v.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return v.Id;
    }

    public async Task<string?> WisAsync(int id)
    {
        await using var cn = await OpenAsync();
        await using (var tel = new MySqlCommand("SELECT COUNT(*) FROM `FileStore` WHERE `StoreType` = @st AND `MasterID` = @id", cn))
        {
            tel.Parameters.AddWithValue("@st", FilestoreService.StoreTypeVerzekering);
            tel.Parameters.AddWithValue("@id", id);
            if (Convert.ToInt32(await tel.ExecuteScalarAsync()) > 0)
                return "Er hangen documenten aan deze verzekering; verwijder die eerst, of zet de verzekering op beëindigd.";
        }
        await using var cmd = new MySqlCommand("DELETE FROM `Verzekeringen` WHERE `VerzekeringID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        return null;
    }

    public async Task<List<VerzekeringUitVasteLast>> NietGekoppeldAsync()
    {
        var lijst = new List<VerzekeringUitVasteLast>();
        await using var cn = await OpenAsync();
        bool bvKolom;
        await using (var k = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'VasteLasten' AND COLUMN_NAME = 'BvID'", cn))
            bvKolom = Convert.ToInt32(await k.ExecuteScalarAsync()) > 0;
        var tabel = await TabelAanwezigAsync();
        await using var cmd = new MySqlCommand(
            $"SELECT v.`ID`, COALESCE(v.`Omschrijving`, ''), {(bvKolom ? "v.`BvID`" : "NULL")} " +
            "  FROM `VasteLasten` v JOIN `VasteLastenCategorie` c ON c.`CategorieID` = v.`CategorieID` " +
            " WHERE c.`Omschrijving` LIKE '%verzeker%' " +
            (tabel ? " AND v.`ID` NOT IN (SELECT `VasteLastID` FROM `Verzekeringen` WHERE `VasteLastID` IS NOT NULL) " : "") +
            " ORDER BY v.`Omschrijving`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new VerzekeringUitVasteLast(r.GetInt32(0), S(r, 1), IN(r, 2)));
        return lijst;
    }

    public async Task<List<Keuze>> WerknemersAsync()
    {
        var lijst = new List<Keuze>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Werknemer-id`, COALESCE(`NaamVoluit`, '') FROM `Werknemers` WHERE COALESCE(`Uit dienst`, 0) = 0 " +
            "   AND COALESCE(`NaamVoluit`, '') <> '' ORDER BY `NaamVoluit`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new Keuze(r.GetInt32(0), S(r, 1)));
        return lijst;
    }

    public async Task<List<int>> DirectieAsync()
    {
        var lijst = new List<int>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `Werknemer-id` FROM `Werknemers` WHERE COALESCE(`Directie`, 0) <> 0 AND COALESCE(`Uit dienst`, 0) = 0", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(r.GetInt32(0));
        return lijst;
    }

    public async Task ZetGemeldAsync(int id, DateOnly vervaldatum)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE `Verzekeringen` SET `GemeldVoor` = @d WHERE `VerzekeringID` = @id", cn);
        cmd.Parameters.AddWithValue("@d", vervaldatum.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
