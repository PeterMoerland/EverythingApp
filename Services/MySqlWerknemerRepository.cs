using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Tijdlijn, relaties en documenten uit de nieuwe tabellen
/// (Werknemers_Schema_MariaDB.sql). Kolomnamen staan hier vast, omdat wij dat
/// schema zelf hebben aangemaakt.
/// </summary>
public class MySqlWerknemerRepository : IWerknemerRepository
{
    private readonly string _cs;
    public MySqlWerknemerRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    private static DateOnly? DO(object v) => v is DBNull or null ? null : DateOnly.FromDateTime(Convert.ToDateTime(v));
    private static decimal? DEC(object v) => v is DBNull or null ? null : Convert.ToDecimal(v);
    private static int? INT(object v) => v is DBNull or null ? null : Convert.ToInt32(v);
    private static string STR(object v) => v is DBNull or null ? "" : v.ToString() ?? "";
    private static bool BOOL(object v)
    {
        if (v is DBNull or null) return false;
        try { return Convert.ToInt64(v) != 0; } catch { return false; }
    }

    // ============================================================
    //  Soorten gebeurtenissen
    // ============================================================
    public async Task<List<GebeurtenisSoort>> GetSoortenAsync()
    {
        var list = new List<GebeurtenisSoort>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `GebeurtenisSoortId`,`Naam`,`Groep`,`WijzigtDienst`,`VraagtDocument`," +
            "`StandaardPrive`,`Jubileum` FROM `Gebeurtenis_Soort` " +
            "WHERE `Actief` <> 0 ORDER BY `Groep`, `Naam`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new GebeurtenisSoort
            {
                Id = r.GetInt32(0),
                Naam = STR(r.GetValue(1)),
                Groep = STR(r.GetValue(2)),
                WijzigtDienst = Convert.ToInt32(r.GetValue(3)),
                VraagtDocument = BOOL(r.GetValue(4)),
                StandaardPrive = BOOL(r.GetValue(5)),
                Jubileum = BOOL(r.GetValue(6)),
            });
        return list;
    }

    // ============================================================
    //  Tijdlijn
    // ============================================================
    public async Task<List<Gebeurtenis>> GetTijdlijnAsync(int werknemerId, bool inclusiefPrive)
    {
        var list = new List<Gebeurtenis>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT g.`GebeurtenisId`, g.`WerknemerId`, g.`GebeurtenisSoortId`, s.`Naam`, s.`Groep`, " +
            "       g.`Moment`, g.`Toelichting`, g.`FunctieId`, g.`Contracturen`, g.`WerkzaamBij`, " +
            "       g.`Bedrag`, g.`TotDatum`, g.`Prive`, " +
            "       g.`BrutoSalaris`, g.`DertiendeMaand`, g.`VrijeDagen`, g.`AtvDagen`, " +
            "       (SELECT COUNT(*) FROM `FileStore` f WHERE f.`StoreType` = 301 AND f.`MasterID` = g.`GebeurtenisId`) " +
            "  FROM `Werknemer_Gebeurtenis` g " +
            "  JOIN `Gebeurtenis_Soort` s ON s.`GebeurtenisSoortId` = g.`GebeurtenisSoortId` " +
            " WHERE g.`WerknemerId` = @wn " + (inclusiefPrive ? "" : "AND g.`Prive` = 0 ") +
            " ORDER BY g.`Moment` DESC, g.`GebeurtenisId` DESC", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new Gebeurtenis
            {
                Id = r.GetInt32(0),
                WerknemerId = r.GetInt32(1),
                SoortId = r.GetInt32(2),
                Soort = STR(r.GetValue(3)),
                Groep = STR(r.GetValue(4)),
                Moment = Convert.ToDateTime(r.GetValue(5)),
                Toelichting = STR(r.GetValue(6)),
                FunctieId = INT(r.GetValue(7)),
                Contracturen = DEC(r.GetValue(8)),
                WerkzaamBij = INT(r.GetValue(9)),
                Bedrag = DEC(r.GetValue(10)),
                TotDatum = DO(r.GetValue(11)),
                Prive = BOOL(r.GetValue(12)),
                BrutoSalaris = DEC(r.GetValue(13)),
                DertiendeMaand = r.IsDBNull(14) ? null : BOOL(r.GetValue(14)),
                VrijeDagen = DEC(r.GetValue(15)),
                AtvDagen = DEC(r.GetValue(16)),
                AantalDocumenten = Convert.ToInt32(r.GetValue(17)),
            });
        return list;
    }

    public async Task<int> BewaarGebeurtenisAsync(Gebeurtenis g)
    {
        await using var cn = await OpenAsync();

        var velden = "`WerknemerId`=@wn, `GebeurtenisSoortId`=@soort, `Moment`=@moment, " +
                     "`Toelichting`=@toel, `FunctieId`=@functie, `Contracturen`=@uren, " +
                     "`WerkzaamBij`=@bv, `Bedrag`=@bedrag, `TotDatum`=@tot, `Prive`=@prive, " +
                     "`BrutoSalaris`=@bruto, `DertiendeMaand`=@dertiende, " +
                     "`VrijeDagen`=@vrij, `AtvDagen`=@atv";

        var sql = g.Id == 0
            ? $"INSERT INTO `Werknemer_Gebeurtenis` SET {velden}"
            : $"UPDATE `Werknemer_Gebeurtenis` SET {velden} WHERE `GebeurtenisId`=@id";

        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@wn", g.WerknemerId);
        cmd.Parameters.AddWithValue("@soort", g.SoortId);
        cmd.Parameters.AddWithValue("@moment", g.Moment);
        cmd.Parameters.AddWithValue("@toel", g.Toelichting ?? "");
        cmd.Parameters.AddWithValue("@functie", (object?)g.FunctieId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@uren", (object?)g.Contracturen ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bv", (object?)g.WerkzaamBij ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bedrag", (object?)g.Bedrag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tot",
            g.TotDatum is null ? DBNull.Value : g.TotDatum.Value.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@prive", g.Prive ? 1 : 0);
        cmd.Parameters.AddWithValue("@bruto", (object?)g.BrutoSalaris ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dertiende",
            g.DertiendeMaand is null ? DBNull.Value : (g.DertiendeMaand.Value ? 1 : 0));
        cmd.Parameters.AddWithValue("@vrij", (object?)g.VrijeDagen ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@atv", (object?)g.AtvDagen ?? DBNull.Value);
        if (g.Id != 0) cmd.Parameters.AddWithValue("@id", g.Id);

        await cmd.ExecuteNonQueryAsync();
        return g.Id == 0 ? (int)cmd.LastInsertedId : g.Id;
    }

    public async Task VerwijderGebeurtenisAsync(int gebeurtenisId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `Werknemer_Gebeurtenis` WHERE `GebeurtenisId` = @id", cn);
        cmd.Parameters.AddWithValue("@id", gebeurtenisId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Relaties
    // ============================================================
    public async Task<List<RelatieSoort>> GetRelatieSoortenAsync()
    {
        var list = new List<RelatieSoort>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `RelatieSoortId`,`Naam` FROM `Relatie_Soort` ORDER BY `RelatieSoortId`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new RelatieSoort { Id = r.GetInt32(0), Naam = STR(r.GetValue(1)) });
        return list;
    }

    public async Task<List<Relatie>> GetRelatiesAsync(int werknemerId)
    {
        var list = new List<Relatie>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT r.`RelatieId`, r.`WerknemerId`, r.`RelatieSoortId`, s.`Naam`, r.`Naam`, " +
            "       r.`Geboortedatum`, r.`Telefoon`, r.`Opmerking`, r.`Noodcontact` " +
            "  FROM `Werknemer_Relatie` r " +
            "  JOIN `Relatie_Soort` s ON s.`RelatieSoortId` = r.`RelatieSoortId` " +
            " WHERE r.`WerknemerId` = @wn AND r.`Actief` <> 0 " +
            " ORDER BY r.`RelatieSoortId`, r.`Geboortedatum`", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        await using var r2 = await cmd.ExecuteReaderAsync();
        while (await r2.ReadAsync())
            list.Add(new Relatie
            {
                Id = r2.GetInt32(0),
                WerknemerId = r2.GetInt32(1),
                SoortId = r2.GetInt32(2),
                Soort = STR(r2.GetValue(3)),
                Naam = STR(r2.GetValue(4)),
                Geboortedatum = DO(r2.GetValue(5)),
                Telefoon = STR(r2.GetValue(6)),
                Opmerking = STR(r2.GetValue(7)),
                Noodcontact = BOOL(r2.GetValue(8)),
            });
        return list;
    }

    public async Task<int> BewaarRelatieAsync(Relatie r)
    {
        await using var cn = await OpenAsync();
        var velden = "`WerknemerId`=@wn, `RelatieSoortId`=@soort, `Naam`=@naam, " +
                     "`Geboortedatum`=@geb, `Telefoon`=@tel, `Opmerking`=@opm, `Noodcontact`=@nood";
        var sql = r.Id == 0
            ? $"INSERT INTO `Werknemer_Relatie` SET {velden}"
            : $"UPDATE `Werknemer_Relatie` SET {velden} WHERE `RelatieId`=@id";

        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@wn", r.WerknemerId);
        cmd.Parameters.AddWithValue("@soort", r.SoortId);
        cmd.Parameters.AddWithValue("@naam", r.Naam ?? "");
        cmd.Parameters.AddWithValue("@geb",
            r.Geboortedatum is null ? DBNull.Value : r.Geboortedatum.Value.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@tel", r.Telefoon ?? "");
        cmd.Parameters.AddWithValue("@opm", r.Opmerking ?? "");
        cmd.Parameters.AddWithValue("@nood", r.Noodcontact ? 1 : 0);
        if (r.Id != 0) cmd.Parameters.AddWithValue("@id", r.Id);

        await cmd.ExecuteNonQueryAsync();
        return r.Id == 0 ? (int)cmd.LastInsertedId : r.Id;
    }

    public async Task VerwijderRelatieAsync(int relatieId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Werknemer_Relatie` SET `Actief` = 0 WHERE `RelatieId` = @id", cn);
        cmd.Parameters.AddWithValue("@id", relatieId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Verjaardagen en jubilea
    //  Datum in dienst: eerst de vroegste 'In dienst'-gebeurtenis uit de tijdlijn,
    //  anders de kolom in Werknemers (zolang de tijdlijn nog niet gevuld is).
    // ============================================================
    public async Task<List<Mijlpalen>> GetMijlpalenAsync()
    {
        var lijst = new List<Mijlpalen>();

        await using var cn = await OpenAsync();

        // 'SELECT *' en kolommen op naam lezen: zo geeft een afwijkende kolomnaam
        // geen fout, maar valt alleen dat ene gegeven weg.
        await using (var cmd = new MySqlCommand("SELECT * FROM `Werknemers`", cn))
        await using (var r = (MySqlDataReader)await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var id = VeldInt(r, "Werknemer-id", "werknemer_id", "werknemerid", "id");
                if (id is null) continue;

                // filters: in dienst, echt een persoon, niet ingeleend
                if (VeldVlag(r, true, "Persoon", "persoon") == false) continue;
                if (VeldVlag(r, false, "Uit dienst", "UitDienst", "uitdienst") == true) continue;
                if (VeldVlag(r, false, "Inleen", "inleen", "Ingeleend") == true) continue;

                var naam = Veld(r, "NaamVoluit", "Naamvoluit", "naamvoluit");
                if (string.IsNullOrWhiteSpace(naam))
                    naam = string.Join(" ", new[]
                    {
                        Veld(r, "Voornaam", "voornaam"),
                        Veld(r, "Tussenvoegsel", "tussenvoegsel"),
                        Veld(r, "Achternaam", "achternaam")
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));

                lijst.Add(new Mijlpalen
                {
                    WerknemerId = id.Value,
                    Naam = string.IsNullOrWhiteSpace(naam) ? $"Werknemer {id}" : naam!,
                    Geboortedatum = VeldDatum(r, "Geboortedatum", "geboortedatum", "Geboorte_datum"),
                    InDienst = VeldDatum(r, "in dienst", "In dienst", "Datum in-dienst",
                                            "Datum in dienst", "DatumInDienst", "datum_in_dienst",
                                            "Indienst", "Startdatum"),
                });
            }
        }

        // Vroegste 'In dienst'-gebeurtenis heeft voorrang
        try
        {
            await using var cmd2 = new MySqlCommand(
                "SELECT g.`WerknemerId`, MIN(g.`Moment`) " +
                "  FROM `Werknemer_Gebeurtenis` g " +
                "  JOIN `Gebeurtenis_Soort` s ON s.`GebeurtenisSoortId` = g.`GebeurtenisSoortId` " +
                " WHERE s.`WijzigtDienst` = 1 GROUP BY g.`WerknemerId`", cn);
            await using var r2 = await cmd2.ExecuteReaderAsync();
            while (await r2.ReadAsync())
            {
                var id = r2.GetInt32(0);
                var datum = DO(r2.GetValue(1));
                var w = lijst.FirstOrDefault(x => x.WerknemerId == id);
                if (w is not null && datum is not null) w.InDienst = datum;
            }
        }
        catch { /* tijdlijn nog niet in gebruik */ }

        return lijst.OrderBy(w => w.Naam).ToList();
    }

    // ============================================================
    //  Beheer: generieke tabellen (definities in BeheerTabellen)
    //  Tabel- en kolomnamen komen uit onze eigen definities, niet van gebruikers.
    // ============================================================
    public async Task<List<Dictionary<string, object?>>> LeesBeheerTabelAsync(BeheerTabel def)
    {
        var rijen = new List<Dictionary<string, object?>>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand($"SELECT * FROM `{def.Tabel}`", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var rij = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < r.FieldCount; i++)
                rij[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            rijen.Add(rij);
        }
        return rijen;
    }

    public async Task BewaarBeheerRijAsync(BeheerTabel def, int id, Dictionary<string, object?> waarden)
    {
        // alleen kolommen uit de definitie toestaan
        var toegestaan = def.Velden.Select(v => v.Kolom).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var paren = waarden.Where(w => toegestaan.Contains(w.Key)).ToList();
        if (paren.Count == 0) return;

        var velden = string.Join(", ", paren.Select((p, i) => $"`{p.Key}` = @p{i}"));
        var sql = id == 0
            ? $"INSERT INTO `{def.Tabel}` SET {velden}"
            : $"UPDATE `{def.Tabel}` SET {velden} WHERE `{def.IdKolom}` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        for (var i = 0; i < paren.Count; i++)
            cmd.Parameters.AddWithValue($"@p{i}", paren[i].Value ?? DBNull.Value);
        if (id != 0) cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> BeheerRijInGebruikAsync(BeheerTabel def, int id)
    {
        await using var cn = await OpenAsync();
        foreach (var verw in def.Verwijzingen)
        {
            try
            {
                await using var cmd = new MySqlCommand(
                    $"SELECT COUNT(*) FROM `{verw.Tabel}` WHERE `{verw.Kolom}` = @id", cn);
                cmd.Parameters.AddWithValue("@id", id);
                var res = await cmd.ExecuteScalarAsync();
                var aantal = res is null or DBNull ? 0 : Convert.ToInt64(res);
                if (aantal > 0) return $"In gebruik bij {aantal} {verw.Omschrijving}.";
            }
            catch { /* tabel of kolom bestaat niet: overslaan */ }
        }
        return null;
    }

    public async Task VerwijderBeheerRijAsync(BeheerTabel def, int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"DELETE FROM `{def.Tabel}` WHERE `{def.IdKolom}` = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Beheer: functies (Werknemers_Functie)
    //  Tariefkolommen bestaan misschien niet; die worden overgeslagen.
    // ============================================================
    private HashSet<string>? _functieKolommen;

    private async Task<HashSet<string>> FunctieKolommenAsync()
    {
        if (_functieKolommen is not null) return _functieKolommen;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT column_name FROM information_schema.columns " +
                "WHERE table_schema = DATABASE() AND table_name = 'Werknemers_Functie'", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) set.Add(r.GetString(0));
        }
        catch { }
        _functieKolommen = set;
        return set;
    }

    public async Task<List<Functie>> GetFunctieBeheerAsync()
    {
        var list = new List<Functie>();
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT f.*, (SELECT COUNT(*) FROM `Werknemers` w " +
                "              WHERE w.`id_functie` = f.`id_functie`) AS `aantal` " +
                "  FROM `Werknemers_Functie` f", cn);
            await using var r = (MySqlDataReader)await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var id = VeldInt(r, "id_functie", "FunctieId", "id");
                if (id is null) continue;
                list.Add(new Functie
                {
                    Id = id.Value,
                    Naam = Veld(r, "functie", "Functie", "Naam", "Omschrijving") ?? "",
                    InternTarief = Bedrag(Veld(r, "Internal_rate", "internal_rate")),
                    ExternTarief = Bedrag(Veld(r, "External_rate", "external_rate")),
                    KostTarief = Bedrag(Veld(r, "Cost_rate", "cost_rate")),
                    AantalWerknemers = VeldInt(r, "aantal") ?? 0,
                });
            }
        }
        catch { }
        return list.OrderBy(f => f.Naam).ToList();
    }

    private static decimal? Bedrag(string? s) =>
        decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                         System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    public async Task<int> BewaarFunctieAsync(Functie f)
    {
        var kolommen = await FunctieKolommenAsync();
        var naamKolom = new[] { "functie", "Functie", "Naam", "Omschrijving" }
            .FirstOrDefault(kolommen.Contains) ?? "functie";

        var stukken = new List<string> { $"`{naamKolom}` = @naam" };
        var pars = new List<(string, object?)> { ("@naam", f.Naam ?? "") };

        void Voeg(string[] namen, string par, decimal? waarde)
        {
            var kolom = namen.FirstOrDefault(kolommen.Contains);
            if (kolom is null) return;
            stukken.Add($"`{kolom}` = {par}");
            pars.Add((par, (object?)waarde ?? DBNull.Value));
        }
        Voeg(new[] { "Internal_rate", "internal_rate" }, "@intern", f.InternTarief);
        Voeg(new[] { "External_rate", "external_rate" }, "@extern", f.ExternTarief);
        Voeg(new[] { "Cost_rate", "cost_rate" }, "@kost", f.KostTarief);

        var velden = string.Join(", ", stukken);
        var sql = f.Id == 0
            ? $"INSERT INTO `Werknemers_Functie` SET {velden}"
            : $"UPDATE `Werknemers_Functie` SET {velden} WHERE `id_functie` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        foreach (var (naam, waarde) in pars) cmd.Parameters.AddWithValue(naam, waarde);
        if (f.Id != 0) cmd.Parameters.AddWithValue("@id", f.Id);

        await cmd.ExecuteNonQueryAsync();
        return f.Id == 0 ? (int)cmd.LastInsertedId : f.Id;
    }

    public async Task<string?> WissenGeblokkeerdAsync(int functieId)
    {
        await using var cn = await OpenAsync();

        async Task<long> Tel(string sql)
        {
            try
            {
                await using var cmd = new MySqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@id", functieId);
                var res = await cmd.ExecuteScalarAsync();
                return res is null or DBNull ? 0 : Convert.ToInt64(res);
            }
            catch { return 0; }   // tabel/kolom bestaat niet
        }

        var werknemers = await Tel("SELECT COUNT(*) FROM `Werknemers` WHERE `id_functie` = @id");
        if (werknemers > 0)
            return $"In gebruik bij {werknemers} werknemer(s).";

        var gebeurtenissen = await Tel("SELECT COUNT(*) FROM `Werknemer_Gebeurtenis` WHERE `FunctieId` = @id");
        if (gebeurtenissen > 0)
            return $"Komt voor in {gebeurtenissen} loopbaangebeurtenis(sen).";

        return null;
    }

    public async Task VerwijderFunctieAsync(int functieId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `Werknemers_Functie` WHERE `id_functie` = @id", cn);
        cmd.Parameters.AddWithValue("@id", functieId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> WerknemersMetFunctieAsync(int functieId)
    {
        var namen = new List<string>();
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COALESCE(`NaamVoluit`, CONCAT_WS(' ', `Voornaam`, `Achternaam`)) AS naam, " +
                "       (`Uit dienst` <> 0) AS uit " +
                "  FROM `Werknemers` WHERE `id_functie` = @id ORDER BY naam", cn);
            cmd.Parameters.AddWithValue("@id", functieId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var naam = STR(r.GetValue(0));
                if (BOOL(r.GetValue(1))) naam += " (uit dienst)";
                namen.Add(naam);
            }
        }
        catch { }
        return namen;
    }

    // ============================================================
    //  Hoofdfoto: welke foto uit de filestore is 'de' foto
    // ============================================================
    public async Task<int?> GetHoofdfotoAsync(int werknemerId)
    {
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT `FileStoreID` FROM `Werknemer_Hoofdfoto` WHERE `WerknemerId` = @wn", cn);
            cmd.Parameters.AddWithValue("@wn", werknemerId);
            var res = await cmd.ExecuteScalarAsync();
            return res is null or DBNull ? null : Convert.ToInt32(res);
        }
        catch { return null; }   // tabel bestaat nog niet: nieuwste foto wint
    }

    public async Task ZetHoofdfotoAsync(int werknemerId, int fileStoreId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO `Werknemer_Hoofdfoto` (`WerknemerId`, `FileStoreID`) VALUES (@wn, @f) " +
            "ON DUPLICATE KEY UPDATE `FileStoreID` = VALUES(`FileStoreID`)", cn);
        cmd.Parameters.AddWithValue("@wn", werknemerId);
        cmd.Parameters.AddWithValue("@f", fileStoreId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Passen (Financieel-tabblad)
    //  Zelfde koppeling als de Access-query:
    //    Passen -> PasType, Passen -> Bankrekeningen -> Relations
    // ============================================================
    public async Task<List<Pas>> GetPassenAsync(int werknemerId, bool inclusiefOpgeheven)
    {
        var list = new List<Pas>();
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT p.`PasID`, t.`Omschrijving`, p.`Pasnummer`, r.`name`, p.`Opgeheven`, " +
                "       p.`PasTypeID`, p.`BankrekeningID`, p.`WerknemerID` " +
                "  FROM `Passen` p " +
                "  JOIN `PasType` t ON t.`PasTypeID` = p.`PasTypeID` " +
                "  LEFT JOIN `Bankrekeningen` b ON b.`Bankrekeningen_id` = p.`BankrekeningID` " +
                "  LEFT JOIN `Relations` r ON r.`id_supplier` = b.`BVid` " +
                " WHERE p.`WerknemerID` = @wn " +
                (inclusiefOpgeheven ? "" : "AND (p.`Opgeheven` = 0 OR p.`Opgeheven` IS NULL) ") +
                " ORDER BY t.`Omschrijving`", cn);
            cmd.Parameters.AddWithValue("@wn", werknemerId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new Pas
                {
                    Id = r.GetInt32(0),
                    Soort = STR(r.GetValue(1)),
                    Pasnummer = STR(r.GetValue(2)),
                    BV = STR(r.GetValue(3)),
                    Opgeheven = BOOL(r.GetValue(4)),
                    PasTypeId = INT(r.GetValue(5)) ?? 0,
                    BankrekeningId = INT(r.GetValue(6)),
                    WerknemerId = INT(r.GetValue(7)) ?? werknemerId,
                });
        }
        catch { /* tabellen of kolommen wijken af: lijst blijft leeg */ }
        return list;
    }

    public async Task<int> BewaarPasAsync(Pas p)
    {
        await using var cn = await OpenAsync();
        var velden = "`WerknemerID`=@wn, `PasTypeID`=@type, `Pasnummer`=@nr, " +
                     "`BankrekeningID`=@rek, `Opgeheven`=@op";
        var sql = p.Id == 0
            ? $"INSERT INTO `Passen` SET {velden}"
            : $"UPDATE `Passen` SET {velden} WHERE `PasID`=@id";

        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@wn", p.WerknemerId);
        cmd.Parameters.AddWithValue("@type", p.PasTypeId);
        cmd.Parameters.AddWithValue("@nr", p.Pasnummer ?? "");
        cmd.Parameters.AddWithValue("@rek", (object?)p.BankrekeningId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@op", p.Opgeheven ? 1 : 0);
        if (p.Id != 0) cmd.Parameters.AddWithValue("@id", p.Id);

        await cmd.ExecuteNonQueryAsync();
        return p.Id == 0 ? (int)cmd.LastInsertedId : p.Id;
    }

    public Task<List<Keuze>> GetPasTypesAsync() => LijstAsync(
        "SELECT * FROM `PasType`",
        new[] { "PasTypeID", "PasTypeId", "id" },
        new[] { "Omschrijving", "Naam", "PasType" });

    /// <summary>Bankrekeningen met de werkmaatschappij erbij, voor de keuzelijst.</summary>
    public async Task<List<Keuze>> GetBankrekeningenAsync()
    {
        var list = new List<Keuze>();
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT b.*, r.`name` AS `bv_naam` FROM `Bankrekeningen` b " +
                "LEFT JOIN `Relations` r ON r.`id_supplier` = b.`BVid`", cn);
            await using var r = (MySqlDataReader)await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var id = VeldInt(r, "Bankrekeningen_id", "BankrekeningID", "id");
                if (id is null) continue;
                var bv = Veld(r, "bv_naam") ?? "";
                var iban = Veld(r, "IBAN", "Iban", "Rekeningnummer", "Rekening", "Banknummer") ?? "";

                // Weergave: werkmaatschappij (rekeningnummer)
                var naam = bv;
                if (!string.IsNullOrWhiteSpace(iban))
                    naam = string.IsNullOrWhiteSpace(bv) ? iban : $"{bv} ({iban})";

                list.Add(new Keuze(id.Value, string.IsNullOrWhiteSpace(naam) ? $"#{id}" : naam));
            }
        }
        catch { /* afwijkende kolommen: lijst blijft leeg */ }
        return OrdenOpNaam(list);
    }

    // ============================================================
    //  Keuzelijsten voor het Status-tabblad
    //  'SELECT *' + kolommen op naam lezen, zodat een afwijkende kolomnaam
    //  geen fout geeft maar een lege lijst.
    // ============================================================
    private static string? Veld(MySqlDataReader r, params string[] namen)
    {
        for (var i = 0; i < r.FieldCount; i++)
            foreach (var n in namen)
                if (string.Equals(r.GetName(i), n, StringComparison.OrdinalIgnoreCase))
                    return r.IsDBNull(i) ? null : r.GetValue(i)?.ToString();
        return null;
    }

    private static int? VeldInt(MySqlDataReader r, params string[] namen)
    {
        var s = Veld(r, namen);
        return int.TryParse(s, out var v) ? v : null;
    }

    /// <summary>Datum uit een kolom; null als de kolom of de waarde ontbreekt.</summary>
    private static DateOnly? VeldDatum(MySqlDataReader r, params string[] namen)
    {
        for (var i = 0; i < r.FieldCount; i++)
            foreach (var n in namen)
                if (string.Equals(r.GetName(i), n, StringComparison.OrdinalIgnoreCase))
                {
                    if (r.IsDBNull(i)) return null;
                    try { return DateOnly.FromDateTime(Convert.ToDateTime(r.GetValue(i))); }
                    catch { return null; }
                }
        return null;
    }

    /// <summary>Ja/nee-kolom; geeft 'standaard' terug als de kolom niet bestaat.</summary>
    private static bool? VeldVlag(MySqlDataReader r, bool standaard, params string[] namen)
    {
        for (var i = 0; i < r.FieldCount; i++)
            foreach (var n in namen)
                if (string.Equals(r.GetName(i), n, StringComparison.OrdinalIgnoreCase))
                {
                    if (r.IsDBNull(i)) return false;
                    try { return Convert.ToInt64(r.GetValue(i)) != 0; } catch { return false; }
                }
        return standaard;   // kolom bestaat niet: filter niet toepassen
    }

    private async Task<List<Keuze>> LijstAsync(string sql, string[] idNamen, string[] naamNamen,
                                               string? terugvalSql = null)
    {
        var list = new List<Keuze>();
        foreach (var poging in terugvalSql is null ? new[] { sql } : new[] { sql, terugvalSql })
        {
            try
            {
                await using var cn = await OpenAsync();
                await using var cmd = new MySqlCommand(poging, cn);
                await using var r = (MySqlDataReader)await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var id = VeldInt(r, idNamen);
                    if (id is null) continue;
                    var naam = Veld(r, naamNamen);
                    list.Add(new Keuze(id.Value, string.IsNullOrWhiteSpace(naam) ? $"#{id}" : naam!));
                }
                if (list.Count > 0) return OrdenOpNaam(list);
            }
            catch { /* volgende poging */ }
        }
        return OrdenOpNaam(list);
    }

    private static List<Keuze> OrdenOpNaam(List<Keuze> l) => l.OrderBy(k => k.Naam).ToList();

    public Task<List<Keuze>> GetBVsAsync() => LijstAsync(
        "SELECT * FROM `Relations` WHERE `werkmaatschappij` <> 0",
        new[] { "id_supplier", "RelationId", "id" },
        new[] { "Afkorting", "name", "Naam", "Bedrijfsnaam" },
        terugvalSql: "SELECT * FROM `Relations` WHERE `Werkmaatschappij` <> 0");

    public Task<List<Keuze>> GetFunctiesAsync() => LijstAsync(
        "SELECT * FROM `Werknemers_Functie`",
        new[] { "id_functie", "FunctieId", "id" },
        new[] { "functie", "Functie", "Naam", "Omschrijving" });

    public Task<List<Keuze>> GetActiviteitenLijstAsync() => LijstAsync(
        "SELECT * FROM `UrenActiviteit`",
        new[] { "UrenActiviteitID", "UrenActiviteitId", "id" },
        new[] { "UrenActiviteit", "Naam", "Omschrijving", "Activiteit" });

    // ============================================================
    //  Documenten uit de bestaande FileStore
    // ============================================================
    public async Task<List<Bestand>> GetBestandenAsync(int storeType, int masterId)
    {
        var list = new List<Bestand>();
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT f.`FileStoreID`, COALESCE(f.`FileOnly`, f.`Filenaam`), f.`DatumToegevoegd`, " +
                "       COALESCE(w.`NaamVoluit`, '') " +
                "  FROM `FileStore` f " +
                "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = f.`ToegevoegdDoor` " +
                " WHERE f.`StoreType` = @st AND f.`MasterID` = @mid " +
                " ORDER BY f.`DatumToegevoegd` DESC", cn);
            cmd.Parameters.AddWithValue("@st", storeType);
            cmd.Parameters.AddWithValue("@mid", masterId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new Bestand
                {
                    FileStoreId = r.GetInt32(0),
                    Bestandsnaam = STR(r.GetValue(1)),
                    Toegevoegd = r.IsDBNull(2) ? null : Convert.ToDateTime(r.GetValue(2)),
                    ToegevoegdDoor = STR(r.GetValue(3)),
                });
        }
        catch { /* FileStore-kolommen wijken af: lijst blijft leeg */ }
        return list;
    }
}
