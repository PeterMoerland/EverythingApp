using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Lokaties en dragers (Access: LocationChilds = "Lokatiestruktuur", LocationDragers, LocationTreeview).
/// Een lokatie hangt via ParentLocationID onder een andere; een drager (krat, pallet) is een lokatie van een
/// verplaatsbaar type. Welk type onder welk type mag staat in LocationChilds.
/// </summary>
public interface ILokatieRepository
{
    bool Beschikbaar { get; }

    // ---- lokatiestructuur (types) ----
    Task<List<LokatieType>> TypesAsync();
    Task<List<LokatieTypeRegel>> TypeRegelsAsync();
    Task BewaarTypeAsync(LokatieType type);
    /// <summary>Nieuw type; null als de naam al bestaat.</summary>
    Task<int?> NieuwTypeAsync(string omschrijving);
    /// <summary>Wist een type; geeft de reden terug als het niet mag (in gebruik).</summary>
    Task<string?> WisTypeAsync(int typeId);
    Task VoegSubtypeToeAsync(int ouderTypeId, int kindTypeId);
    Task WisSubtypeAsync(int regelId);

    // ---- lokaties en dragers ----
    Task<LokatieBoom> BoomAsync();
    Task<List<InhoudSoort>> InhoudSoortenAsync();
    /// <summary>Per lokatie: aantal artikelen met voorraad en het totaal aantal stuks.</summary>
    Task<Dictionary<int, (int Items, decimal Stuks)>> TellingenAsync();
    Task<List<LokatieArtikel>> ArtikelenOpAsync(int lokatieId);
    Task BewaarDragerAsync(Lokatie drager);
    /// <summary>Maakt <paramref name="aantal"/> nieuwe dragers van een type in de Wees-lokatie; geeft de nieuwe ids.</summary>
    Task<List<int>> MaakDragersAsync(int typeId, int aantal, int? werknemerId);
    Task VerplaatsAsync(int lokatieId, int nieuweOuderId);
    Task ZetGeprintAsync(IEnumerable<int> lokatieIds);

    // ---- vaste lokaties beheren (Access LocationTreeview / LocationToevoegen; Voorraad > Lokaties) ----
    /// <summary>Nieuwe vaste lokatie (gebouw, ruimte, stelling, vak, ...) onder een ouder; geeft het id.</summary>
    Task<int> NieuweLokatieAsync(int ouderId, int typeId, string omschrijving, string afkorting, int? index, int? door);
    /// <summary>Omschrijving, afkorting, opmerking, type en "voorraad mag hier" van een vaste lokatie.</summary>
    Task BewaarLokatieAsync(Lokatie lokatie);
    /// <summary>Wist een lokatie; geeft de reden als het niet mag (lokaties eronder, voorraadmutaties, inhoud, systeem).</summary>
    Task<string?> WisLokatieAsync(int lokatieId);
    /// <summary>De BV van een bovenste lokatie (type Bedrijf); null = geen. Vraagt VoorraadPerBv_MariaDB.sql.</summary>
    Task ZetBvAsync(int lokatieId, int? bvId);

    // ---- globale inhoud van een drager (LocationContents) ----
    Task<List<LokatieInhoud>> InhoudAsync(int lokatieId);
    Task VoegInhoudToeAsync(int lokatieId, string omschrijving, int? werknemerId);
    Task BewaarInhoudAsync(int inhoudId, string omschrijving);
    Task WisInhoudAsync(int inhoudId);
}

public class GeenLokatieRepository : ILokatieRepository
{
    public Task<int> NieuweLokatieAsync(int ouderId, int typeId, string omschrijving, string afkorting, int? index, int? door)
        => throw new InvalidOperationException("Er is geen database.");
    public Task BewaarLokatieAsync(Lokatie lokatie) => Task.CompletedTask;
    public Task<string?> WisLokatieAsync(int lokatieId) => Task.FromResult<string?>("Er is geen database.");
    public Task ZetBvAsync(int lokatieId, int? bvId) => Task.CompletedTask;
    public bool Beschikbaar => false;
    public Task<List<LokatieType>> TypesAsync() => Task.FromResult(new List<LokatieType>());
    public Task<List<LokatieTypeRegel>> TypeRegelsAsync() => Task.FromResult(new List<LokatieTypeRegel>());
    public Task BewaarTypeAsync(LokatieType type) => Task.CompletedTask;
    public Task<int?> NieuwTypeAsync(string omschrijving) => Task.FromResult<int?>(null);
    public Task<string?> WisTypeAsync(int typeId) => Task.FromResult<string?>("Geen database.");
    public Task VoegSubtypeToeAsync(int ouderTypeId, int kindTypeId) => Task.CompletedTask;
    public Task WisSubtypeAsync(int regelId) => Task.CompletedTask;
    public Task<LokatieBoom> BoomAsync() => Task.FromResult(new LokatieBoom(Array.Empty<Lokatie>()));
    public Task<List<InhoudSoort>> InhoudSoortenAsync() => Task.FromResult(new List<InhoudSoort>());
    public Task<Dictionary<int, (int Items, decimal Stuks)>> TellingenAsync() => Task.FromResult(new Dictionary<int, (int, decimal)>());
    public Task<List<LokatieArtikel>> ArtikelenOpAsync(int lokatieId) => Task.FromResult(new List<LokatieArtikel>());
    public Task BewaarDragerAsync(Lokatie drager) => Task.CompletedTask;
    public Task<List<int>> MaakDragersAsync(int typeId, int aantal, int? werknemerId) => Task.FromResult(new List<int>());
    public Task VerplaatsAsync(int lokatieId, int nieuweOuderId) => Task.CompletedTask;
    public Task ZetGeprintAsync(IEnumerable<int> lokatieIds) => Task.CompletedTask;
    public Task<List<LokatieInhoud>> InhoudAsync(int lokatieId) => Task.FromResult(new List<LokatieInhoud>());
    public Task VoegInhoudToeAsync(int lokatieId, string omschrijving, int? werknemerId) => Task.CompletedTask;
    public Task BewaarInhoudAsync(int inhoudId, string omschrijving) => Task.CompletedTask;
    public Task WisInhoudAsync(int inhoudId) => Task.CompletedTask;
}

public class MySqlLokatieRepository : ILokatieRepository
{
    /// <summary>De lokatie "Wees": nieuwe en losgeraakte dragers komen hier (Access: MoveWezen, locationdragerAanmaken).</summary>
    public const int Wees = 10;

    private readonly string _cs;
    public MySqlLokatieRepository(string connectionString) => _cs = connectionString;
    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static string S(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
    private static bool B(MySqlDataReader r, int i) => !r.IsDBNull(i) && Convert.ToInt64(r.GetValue(i)) != 0;
    private static int? I(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static DateTime? D(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));

    private static async Task<int> UitvoerAsync(MySqlConnection cn, string sql, params (string, object?)[] p)
    {
        await using var cmd = new MySqlCommand(sql, cn);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> WaardeAsync(MySqlConnection cn, string sql, params (string, object?)[] p)
    {
        await using var cmd = new MySqlCommand(sql, cn);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var w = await cmd.ExecuteScalarAsync();
        return w is DBNull ? null : w;
    }

    // ================= types =================

    public async Task<List<LokatieType>> TypesAsync()
    {
        var lijst = new List<LokatieType>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT t.`LocationTypeID`, COALESCE(t.`Omschrijving`, ''), t.`StartWithIndex1`, t.`UniqueNumbering`, t.`UseNumbering`, " +
            "       t.`Moveable`, t.`System`, (SELECT COUNT(*) FROM `Locations` l WHERE l.`LocationTypeID` = t.`LocationTypeID`) " +
            "  FROM `LocationTypes` t ORDER BY t.`Omschrijving`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new LokatieType
            {
                Id = r.GetInt32(0), Omschrijving = S(r, 1), StartMetIndex1 = B(r, 2), UniekeNummering = B(r, 3),
                GebruikNummering = B(r, 4), Verplaatsbaar = B(r, 5), Systeem = B(r, 6), AantalLokaties = Convert.ToInt32(r.GetValue(7)),
            });
        return lijst;
    }

    public async Task<List<LokatieTypeRegel>> TypeRegelsAsync()
    {
        var lijst = new List<LokatieTypeRegel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `LocationChildID`, `LocationTypeID`, `ChildLocationTypeID` FROM `LocationChilds` " +
            " WHERE `LocationTypeID` IS NOT NULL AND `ChildLocationTypeID` IS NOT NULL", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new LokatieTypeRegel(r.GetInt32(0), Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2))));
        return lijst;
    }

    public async Task BewaarTypeAsync(LokatieType t)
    {
        await using var cn = await OpenAsync();
        if (t.Systeem) return;   // systeemtypes (Wees) niet aanpassen
        await UitvoerAsync(cn,
            "UPDATE `LocationTypes` SET `Omschrijving` = @o, `StartWithIndex1` = @s, `UniqueNumbering` = @u, " +
            "       `UseNumbering` = @g, `Moveable` = @m WHERE `LocationTypeID` = @id AND COALESCE(`System`, 0) = 0",
            ("@o", t.Omschrijving.Trim()), ("@s", t.StartMetIndex1), ("@u", t.UniekeNummering),
            ("@g", t.GebruikNummering), ("@m", t.Verplaatsbaar), ("@id", t.Id));
    }

    public async Task<int?> NieuwTypeAsync(string omschrijving)
    {
        var naam = omschrijving.Trim();
        await using var cn = await OpenAsync();
        var bestaat = Convert.ToInt32(await WaardeAsync(cn, "SELECT COUNT(*) FROM `LocationTypes` WHERE `Omschrijving` = @o", ("@o", naam)));
        if (bestaat > 0) return null;
        await UitvoerAsync(cn, "INSERT INTO `LocationTypes` (`Omschrijving`) VALUES (@o)", ("@o", naam));
        return Convert.ToInt32(await WaardeAsync(cn, "SELECT LAST_INSERT_ID()"));
    }

    public async Task<string?> WisTypeAsync(int typeId)
    {
        await using var cn = await OpenAsync();
        if (Convert.ToInt32(await WaardeAsync(cn, "SELECT COUNT(*) FROM `LocationTypes` WHERE `LocationTypeID` = @t AND COALESCE(`System`, 0) <> 0", ("@t", typeId))) > 0)
            return "Dit is een systeemtype.";
        // Access keek alleen naar LocationChilds; een type met lokaties wissen zou die lokaties zonder type achterlaten.
        var lokaties = Convert.ToInt32(await WaardeAsync(cn, "SELECT COUNT(*) FROM `Locations` WHERE `LocationTypeID` = @t", ("@t", typeId)));
        if (lokaties > 0) return $"Er zijn nog {lokaties} lokaties van dit type.";
        var regels = Convert.ToInt32(await WaardeAsync(cn,
            "SELECT COUNT(*) FROM `LocationChilds` WHERE `LocationTypeID` = @t OR `ChildLocationTypeID` = @t", ("@t", typeId)));
        if (regels > 0) return "Dit type komt nog voor in de structuur (als ouder of als sub-type). Haal die regels eerst weg.";
        await UitvoerAsync(cn, "DELETE FROM `LocationTypes` WHERE `LocationTypeID` = @t", ("@t", typeId));
        return null;
    }

    public async Task VoegSubtypeToeAsync(int ouderTypeId, int kindTypeId)
    {
        await using var cn = await OpenAsync();
        var bestaat = Convert.ToInt32(await WaardeAsync(cn,
            "SELECT COUNT(*) FROM `LocationChilds` WHERE `LocationTypeID` = @o AND `ChildLocationTypeID` = @k",
            ("@o", ouderTypeId), ("@k", kindTypeId)));
        if (bestaat > 0) return;
        await UitvoerAsync(cn, "INSERT INTO `LocationChilds` (`LocationTypeID`, `ChildLocationTypeID`) VALUES (@o, @k)",
            ("@o", ouderTypeId), ("@k", kindTypeId));
    }

    public async Task WisSubtypeAsync(int regelId)
    {
        await using var cn = await OpenAsync();
        await UitvoerAsync(cn, "DELETE FROM `LocationChilds` WHERE `LocationChildID` = @id", ("@id", regelId));
    }

    // ================= lokaties =================

    private static bool _bvKolom;

    public async Task<LokatieBoom> BoomAsync()
    {
        var lijst = new List<Lokatie>();
        await using var cn = await OpenAsync();
        if (!_bvKolom)
            try
            {
                await using var k = new MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Locations' AND COLUMN_NAME = 'BvID'", cn);
                _bvKolom = Convert.ToInt32(await k.ExecuteScalarAsync()) > 0;
            }
            catch { }
        await using var cmd = new MySqlCommand(
            "SELECT l.`LocationID`, l.`ParentLocationID`, l.`LocationTypeID`, COALESCE(l.`Omschrijving`, ''), COALESCE(l.`Afkorting`, ''), " +
            "       COALESCE(l.`Opmerking`, ''), l.`Index`, l.`Voorraad`, l.`Systeemlocatie`, l.`LocationContentsTypeID`, l.`WerkorderID`, " +
            "       CASE WHEN w.`Werkbon-id` IS NULL THEN '' ELSE CONCAT(COALESCE(p.`Projectnumber`, '?'), '-', " +
            "            RIGHT(CONCAT('00', COALESCE(w.`Werkbonnummer`, 0)), 2), ' ', COALESCE(w.`Werkbonomschrijving`, '')) END, " +
            "       COALESCE(m.`NaamVoluit`, ''), l.`AangemaaktOp`, l.`PrintedAt`, COALESCE(t.`Moveable`, 0), " +
            (_bvKolom ? "l.`BvID`, COALESCE(NULLIF(TRIM(bv.`Afkorting`), ''), bv.`name`, '') " : "NULL, '' ") +
            "  FROM `Locations` l " +
            (_bvKolom ? "  LEFT JOIN `Relations` bv ON bv.`id_supplier` = l.`BvID` " : "") +
            "  LEFT JOIN `LocationTypes` t ON t.`LocationTypeID` = l.`LocationTypeID` " +
            "  LEFT JOIN `Workorders` w ON w.`Werkbon-id` = l.`WerkorderID` " +
            "  LEFT JOIN `Projects` p ON p.`Project-Id` = w.`Project-id` " +
            "  LEFT JOIN `Werknemers` m ON m.`Werknemer-id` = l.`AangemaaktDoor`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new Lokatie
            {
                Id = r.GetInt32(0), OuderId = I(r, 1), TypeId = I(r, 2), Omschrijving = S(r, 3).Trim(), Afkorting = S(r, 4),
                Opmerking = S(r, 5), Index = I(r, 6), Voorraad = B(r, 7), Systeemlokatie = B(r, 8), InhoudSoortId = I(r, 9),
                WerkorderId = I(r, 10), Werkorder = S(r, 11).Trim(), AangemaaktDoor = S(r, 12), AangemaaktOp = D(r, 13),
                GeprintOp = D(r, 14), Verplaatsbaar = B(r, 15), BvId = I(r, 16), BvNaam = S(r, 17),
            });
        return new LokatieBoom(lijst);
    }

    public async Task<List<InhoudSoort>> InhoudSoortenAsync()
    {
        var lijst = new List<InhoudSoort>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `LocationContentsTypeID`, COALESCE(`Omschrijving`, '') FROM `LocationContentsTypes` ORDER BY `Omschrijving`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new InhoudSoort(r.GetInt32(0), S(r, 1)));
        return lijst;
    }

    public async Task<Dictionary<int, (int Items, decimal Stuks)>> TellingenAsync()
    {
        // Access telde in VoorraadPerArtikelPerMagazijn ook artikelen die op 0 staan; hier alleen wat er echt ligt.
        var uit = new Dictionary<int, (int, decimal)>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `LocationID`, COUNT(*), SUM(`stand`) FROM ( " +
            "   SELECT `LocationID`, `ArtikelID`, SUM(`Mutatie`) AS `stand` FROM `VoorraadMutaties` " +
            "    WHERE `LocationID` IS NOT NULL GROUP BY `LocationID`, `ArtikelID` HAVING ABS(SUM(`Mutatie`)) > 0.0005) x " +
            " GROUP BY `LocationID`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            uit[Convert.ToInt32(r.GetValue(0))] = (Convert.ToInt32(r.GetValue(1)), Math.Round(Convert.ToDecimal(r.GetValue(2)), 3));
        return uit;
    }

    public async Task<List<LokatieArtikel>> ArtikelenOpAsync(int lokatieId)
    {
        var lijst = new List<LokatieArtikel>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT v.`ArtikelID`, COALESCE(a.`ArtikelCodeMDE`, ''), COALESCE(a.`OmschrijvingNL`, ''), COALESCE(s.`Status`, ''), v.`stand` " +
            "  FROM (SELECT `ArtikelID`, `VoorraadStatusID`, SUM(`Mutatie`) AS `stand` FROM `VoorraadMutaties` " +
            "         WHERE `LocationID` = @l GROUP BY `ArtikelID`, `VoorraadStatusID` HAVING ABS(SUM(`Mutatie`)) > 0.0005) v " +
            "  LEFT JOIN `Artikelen` a ON a.`ArtikelID` = v.`ArtikelID` " +
            "  LEFT JOIN `VoorraadStatus` s ON s.`VoorraadStatusID` = v.`VoorraadStatusID` " +
            " ORDER BY a.`ArtikelCodeMDE`", cn);
        cmd.Parameters.AddWithValue("@l", lokatieId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new LokatieArtikel(I(r, 0) ?? 0, S(r, 1), S(r, 2), S(r, 3), Math.Round(Convert.ToDecimal(r.GetValue(4)), 3)));
        return lijst;
    }

    public async Task BewaarDragerAsync(Lokatie d)
    {
        await using var cn = await OpenAsync();
        await UitvoerAsync(cn,
            "UPDATE `Locations` SET `Omschrijving` = @o, `Opmerking` = @op, `LocationContentsTypeID` = @i, `WerkorderID` = @w " +
            " WHERE `LocationID` = @id",
            ("@o", Kort(d.Omschrijving, 50)), ("@op", Kort(d.Opmerking, 100)), ("@i", d.InhoudSoortId), ("@w", d.WerkorderId), ("@id", d.Id));
    }

    public async Task<List<int>> MaakDragersAsync(int typeId, int aantal, int? werknemerId)
    {
        aantal = Math.Clamp(aantal, 1, 100);
        var ids = new List<int>();
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        string naam;
        await using (var cmd = new MySqlCommand("SELECT COALESCE(`Omschrijving`, '') FROM `LocationTypes` WHERE `LocationTypeID` = @t", cn, tx))
        {
            cmd.Parameters.AddWithValue("@t", typeId);
            naam = (await cmd.ExecuteScalarAsync())?.ToString()?.Trim() ?? throw new InvalidOperationException("Type bestaat niet.");
        }

        // Volgnummer per type over het hele bedrijf (Access: DMax("Index") + 1). Rijen vergrendelen tegen dubbele nummers.
        int hoogste;
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(MAX(`Index`), 0) FROM `Locations` WHERE `LocationTypeID` = @t FOR UPDATE", cn, tx))
        {
            cmd.Parameters.AddWithValue("@t", typeId);
            hoogste = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        for (var i = 1; i <= aantal; i++)
        {
            var nr = hoogste + i;
            await using var cmd = new MySqlCommand(
                "INSERT INTO `Locations` (`Index`, `AangemaaktDoor`, `AangemaaktOp`, `LocationTypeID`, `Omschrijving`, `ParentLocationID`) " +
                "VALUES (@n, @door, NOW(), @t, @o, @ouder); SELECT LAST_INSERT_ID();", cn, tx);
            cmd.Parameters.AddWithValue("@n", nr);
            cmd.Parameters.AddWithValue("@door", (object?)werknemerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@t", typeId);
            cmd.Parameters.AddWithValue("@o", Kort($"{naam} {nr}", 50));
            cmd.Parameters.AddWithValue("@ouder", Wees);
            ids.Add(Convert.ToInt32(await cmd.ExecuteScalarAsync()));
        }

        await tx.CommitAsync();
        return ids;
    }

    public async Task VerplaatsAsync(int lokatieId, int nieuweOuderId)
    {
        if (lokatieId == nieuweOuderId) throw new InvalidOperationException("Een lokatie kan niet in zichzelf.");
        var boom = await BoomAsync();
        if (!boom.PerId.ContainsKey(nieuweOuderId)) throw new InvalidOperationException("De nieuwe plek bestaat niet meer.");
        if (boom.IsOnder(nieuweOuderId, lokatieId)) throw new InvalidOperationException("Een lokatie kan niet onder zichzelf geplaatst worden.");
        await using var cn = await OpenAsync();
        await UitvoerAsync(cn, "UPDATE `Locations` SET `ParentLocationID` = @o WHERE `LocationID` = @id", ("@o", nieuweOuderId), ("@id", lokatieId));
    }

    public async Task<int> NieuweLokatieAsync(int ouderId, int typeId, string omschrijving, string afkorting, int? index, int? door)
    {
        if (omschrijving.Trim().Length == 0) throw new InvalidOperationException("Geef de lokatie een omschrijving.");
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `Locations` (`ParentLocationID`, `LocationTypeID`, `Omschrijving`, `Afkorting`, `Index`, `Voorraad`, `Systeemlocatie`, " +
            "  `AangemaaktDoor`, `AangemaaktOp`) VALUES (@ouder, @t, @o, @a, @i, 1, 0, @door, NOW()); SELECT LAST_INSERT_ID();", cn);
        cmd.Parameters.AddWithValue("@ouder", ouderId);
        cmd.Parameters.AddWithValue("@t", typeId);
        cmd.Parameters.AddWithValue("@o", Kort(omschrijving, 50));
        cmd.Parameters.AddWithValue("@a", afkorting.Trim().Length == 0 ? DBNull.Value : Kort(afkorting, 10));
        cmd.Parameters.AddWithValue("@i", (object?)index ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@door", (object?)door ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task BewaarLokatieAsync(Lokatie l)
    {
        await using var cn = await OpenAsync();
        await UitvoerAsync(cn,
            "UPDATE `Locations` SET `Omschrijving` = @o, `Afkorting` = @a, `Opmerking` = @op, `LocationTypeID` = @t, `Voorraad` = @v " +
            " WHERE `LocationID` = @id AND COALESCE(`Systeemlocatie`, 0) = 0",
            ("@o", Kort(l.Omschrijving, 50)), ("@a", l.Afkorting.Trim().Length == 0 ? null : Kort(l.Afkorting, 10)),
            ("@op", Kort(l.Opmerking, 100)), ("@t", l.TypeId), ("@v", l.Voorraad ? 1 : 0), ("@id", l.Id));
    }

    public async Task<string?> WisLokatieAsync(int lokatieId)
    {
        await using var cn = await OpenAsync();
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(l.`Systeemlocatie`, 0) + 0, " +
            "       (SELECT COUNT(*) FROM `Locations` k WHERE k.`ParentLocationID` = l.`LocationID`), " +
            "       (SELECT COUNT(*) FROM `VoorraadMutaties` m WHERE m.`LocationID` = l.`LocationID`), " +
            "       (SELECT COUNT(*) FROM `LocationContents` c WHERE c.`LocationID` = l.`LocationID`) " +
            "  FROM `Locations` l WHERE l.`LocationID` = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", lokatieId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            if (Convert.ToInt64(r.GetValue(0)) != 0) return "Een systeemlokatie kan niet gewist worden.";
            if (Convert.ToInt32(r.GetValue(1)) > 0) return "Er staan nog lokaties of dragers onder; verplaats of wis die eerst.";
            if (Convert.ToInt32(r.GetValue(2)) > 0)
                return "Er zijn voorraadmutaties op deze lokatie geweest (de historie wijst ernaar); zet liever \"voorraad mag hier\" uit.";
            if (Convert.ToInt32(r.GetValue(3)) > 0) return "Er staat nog inhoud op deze lokatie.";
        }
        await UitvoerAsync(cn, "DELETE FROM `Locations` WHERE `LocationID` = @id", ("@id", lokatieId));
        return null;
    }

    public async Task ZetBvAsync(int lokatieId, int? bvId)
    {
        if (!_bvKolom) throw new InvalidOperationException("Voer eerst VoorraadPerBv_MariaDB.sql uit (kolom Locations.BvID).");
        await using var cn = await OpenAsync();
        await UitvoerAsync(cn, "UPDATE `Locations` SET `BvID` = @b WHERE `LocationID` = @id", ("@b", bvId), ("@id", lokatieId));
    }

    public async Task ZetGeprintAsync(IEnumerable<int> lokatieIds)
    {
        var ids = lokatieIds.Distinct().ToList();
        if (ids.Count == 0) return;
        await using var cn = await OpenAsync();
        await UitvoerAsync(cn, $"UPDATE `Locations` SET `PrintedAt` = NOW() WHERE `LocationID` IN ({string.Join(",", ids)})");
    }

    // ================= globale inhoud =================

    public async Task<List<LokatieInhoud>> InhoudAsync(int lokatieId)
    {
        var lijst = new List<LokatieInhoud>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT c.`LocationContentsID`, COALESCE(c.`Omschrijving`, ''), COALESCE(m.`NaamVoluit`, ''), c.`AddedAt` " +
            "  FROM `LocationContents` c LEFT JOIN `Werknemers` m ON m.`Werknemer-id` = c.`AddedBy` " +
            " WHERE c.`LocationID` = @l ORDER BY c.`LocationContentsID`", cn);
        cmd.Parameters.AddWithValue("@l", lokatieId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lijst.Add(new LokatieInhoud { Id = r.GetInt32(0), Omschrijving = S(r, 1), Door = S(r, 2), Op = D(r, 3) });
        return lijst;
    }

    public async Task VoegInhoudToeAsync(int lokatieId, string omschrijving, int? werknemerId)
    {
        await using var cn = await OpenAsync();
        await UitvoerAsync(cn,
            "INSERT INTO `LocationContents` (`LocationID`, `Omschrijving`, `AddedBy`, `AddedAt`) VALUES (@l, @o, @d, NOW())",
            ("@l", lokatieId), ("@o", Kort(omschrijving, 100)), ("@d", werknemerId));
    }

    public async Task BewaarInhoudAsync(int inhoudId, string omschrijving)
    {
        await using var cn = await OpenAsync();
        await UitvoerAsync(cn, "UPDATE `LocationContents` SET `Omschrijving` = @o WHERE `LocationContentsID` = @id",
            ("@o", Kort(omschrijving, 100)), ("@id", inhoudId));
    }

    public async Task WisInhoudAsync(int inhoudId)
    {
        await using var cn = await OpenAsync();
        await UitvoerAsync(cn, "DELETE FROM `LocationContents` WHERE `LocationContentsID` = @id", ("@id", inhoudId));
    }

    private static string Kort(string? s, int max)
    {
        var t = (s ?? "").Trim();
        return t.Length <= max ? t : t[..max];
    }
}

/// <summary>
/// Alleen om lokaal te testen (Lokaties__Geheugen=true): leest de echte lokaties één keer; daarna gebeurt aanmaken,
/// wijzigen, verplaatsen, wissen en de BV in het geheugen. Types, regels, tellingen en inhoud worden echt gelezen.
/// </summary>
public class GeheugenLokatieRepository : ILokatieRepository
{
    private readonly ILokatieRepository _echt;
    private Dictionary<int, Lokatie>? _lok;
    private int _volgende = 900000;

    public GeheugenLokatieRepository(ILokatieRepository echt) => _echt = echt;
    public bool Beschikbaar => true;

    private static Lokatie Kopie(Lokatie l) => (Lokatie)typeof(Lokatie).GetMethod("MemberwiseClone",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(l, null)!;

    private async Task<Dictionary<int, Lokatie>> Lok()
        => _lok ??= (await _echt.BoomAsync()).PerId.Values.Select(Kopie).ToDictionary(l => l.Id);

    public async Task<LokatieBoom> BoomAsync() => new((await Lok()).Values.Select(Kopie));

    public Task<List<LokatieType>> TypesAsync() => _echt.TypesAsync();
    public Task<List<LokatieTypeRegel>> TypeRegelsAsync() => _echt.TypeRegelsAsync();
    public Task BewaarTypeAsync(LokatieType type) => Task.CompletedTask;
    public Task<int?> NieuwTypeAsync(string omschrijving) => Task.FromResult<int?>(null);
    public Task<string?> WisTypeAsync(int typeId) => Task.FromResult<string?>("Niet in de testversie.");
    public Task VoegSubtypeToeAsync(int ouderTypeId, int kindTypeId) => Task.CompletedTask;
    public Task WisSubtypeAsync(int regelId) => Task.CompletedTask;
    public Task<List<InhoudSoort>> InhoudSoortenAsync() => _echt.InhoudSoortenAsync();
    public Task<Dictionary<int, (int Items, decimal Stuks)>> TellingenAsync() => _echt.TellingenAsync();
    public Task<List<LokatieArtikel>> ArtikelenOpAsync(int lokatieId) => _echt.ArtikelenOpAsync(lokatieId);

    public async Task BewaarDragerAsync(Lokatie drager)
    {
        if ((await Lok()).TryGetValue(drager.Id, out var l)) { l.Omschrijving = drager.Omschrijving; l.Opmerking = drager.Opmerking; l.InhoudSoortId = drager.InhoudSoortId; l.WerkorderId = drager.WerkorderId; }
    }

    public async Task<List<int>> MaakDragersAsync(int typeId, int aantal, int? werknemerId)
    {
        var lok = await Lok();
        var ids = new List<int>();
        for (var i = 0; i < Math.Clamp(aantal, 1, 100); i++)
        {
            var id = Interlocked.Increment(ref _volgende);
            lok[id] = new Lokatie { Id = id, OuderId = 10, TypeId = typeId, Omschrijving = $"Drager {id}", Verplaatsbaar = true, Voorraad = true };
            ids.Add(id);
        }
        return ids;
    }

    public async Task VerplaatsAsync(int lokatieId, int nieuweOuderId)
    {
        var boom = await BoomAsync();
        if (boom.IsOnder(nieuweOuderId, lokatieId)) throw new InvalidOperationException("Een lokatie kan niet onder zichzelf geplaatst worden.");
        if ((await Lok()).TryGetValue(lokatieId, out var l)) l.OuderId = nieuweOuderId;
    }

    public Task ZetGeprintAsync(IEnumerable<int> lokatieIds) => Task.CompletedTask;

    public async Task<int> NieuweLokatieAsync(int ouderId, int typeId, string omschrijving, string afkorting, int? index, int? door)
    {
        var id = Interlocked.Increment(ref _volgende);
        (await Lok())[id] = new Lokatie { Id = id, OuderId = ouderId, TypeId = typeId, Omschrijving = omschrijving.Trim(), Afkorting = afkorting.Trim(), Index = index, Voorraad = true, AangemaaktOp = DateTime.Now };
        return id;
    }

    public async Task BewaarLokatieAsync(Lokatie n)
    {
        if ((await Lok()).TryGetValue(n.Id, out var l) && !l.Systeemlokatie)
        { l.Omschrijving = n.Omschrijving; l.Afkorting = n.Afkorting; l.Opmerking = n.Opmerking; l.TypeId = n.TypeId; l.Voorraad = n.Voorraad; }
    }

    public async Task<string?> WisLokatieAsync(int lokatieId)
    {
        var lok = await Lok();
        if (lok.Values.Any(l => l.OuderId == lokatieId)) return "Er staan nog lokaties of dragers onder; verplaats of wis die eerst.";
        if (lokatieId < 900000) return "In de testversie kunnen alleen hier aangemaakte lokaties gewist worden.";
        lok.Remove(lokatieId);
        return null;
    }

    public async Task ZetBvAsync(int lokatieId, int? bvId)
    {
        if ((await Lok()).TryGetValue(lokatieId, out var l)) { l.BvId = bvId; l.BvNaam = bvId is null ? "" : $"BV {bvId}"; }
    }

    public Task<List<LokatieInhoud>> InhoudAsync(int lokatieId) => _echt.InhoudAsync(lokatieId);
    public Task VoegInhoudToeAsync(int lokatieId, string omschrijving, int? werknemerId) => Task.CompletedTask;
    public Task BewaarInhoudAsync(int inhoudId, string omschrijving) => Task.CompletedTask;
    public Task WisInhoudAsync(int inhoudId) => Task.CompletedTask;
}
