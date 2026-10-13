using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

// Fase 2 van Offertes: de regels (categorieën, tarieven, uren, materiaal, overigen) en kopiëren.
// Elke schrijfactie op een regel controleert in dezelfde query dat de offerte nog op 'Aanmaken' staat:
// een aangeboden offerte gaat op slot, ook als twee schermen tegelijk open staan.

public partial interface IOfferteRepository
{
    Task<OfferteRegels> RegelsAsync(int offerteId);

    Task<int> BewaarCategorieAsync(int offerteId, OfferteCategorie c);
    /// <summary>Wist de categorie; regels die hem gebruiken komen zonder categorie te staan.</summary>
    Task WisCategorieAsync(int offerteId, int categorieId);

    Task<int> BewaarTariefAsync(int offerteId, OfferteTarief t);
    /// <summary>Weigert als er urenregels met dit tarief zijn.</summary>
    Task WisTariefAsync(int offerteId, int tariefId);
    /// <summary>Standaardtarieven van de BV die in de offerte een ander bedrag hebben (na het toevoegen van de ontbrekende).</summary>
    Task<List<TariefVerschil>> TariefVerschillenAsync(int offerteId);

    Task<int> BewaarUurAsync(int offerteId, OfferteUur u);
    Task WisUurAsync(int offerteId, int id);
    Task<int> BewaarMateriaalAsync(int offerteId, OfferteMateriaalRegel m);
    Task WisMateriaalAsync(int offerteId, int id);
    Task<int> BewaarOverigAsync(int offerteId, OfferteOverig o);
    Task WisOverigAsync(int offerteId, int id);

    /// <summary>Kopie van een offerte in een (ander) open project; geeft het nieuwe id.</summary>
    Task<int> KopieerAsync(int bronId, int doelProjectId, OfferteKopieOpties opties, int? door);
}

public partial class GeenOfferteRepository
{
    public Task<OfferteRegels> RegelsAsync(int offerteId) => Task.FromResult(new OfferteRegels());
    public Task<int> BewaarCategorieAsync(int offerteId, OfferteCategorie c) => Task.FromResult(0);
    public Task WisCategorieAsync(int offerteId, int categorieId) => Task.CompletedTask;
    public Task<int> BewaarTariefAsync(int offerteId, OfferteTarief t) => Task.FromResult(0);
    public Task WisTariefAsync(int offerteId, int tariefId) => Task.CompletedTask;
    public Task<List<TariefVerschil>> TariefVerschillenAsync(int offerteId) => Task.FromResult(new List<TariefVerschil>());
    public Task<int> BewaarUurAsync(int offerteId, OfferteUur u) => Task.FromResult(0);
    public Task WisUurAsync(int offerteId, int id) => Task.CompletedTask;
    public Task<int> BewaarMateriaalAsync(int offerteId, OfferteMateriaalRegel m) => Task.FromResult(0);
    public Task WisMateriaalAsync(int offerteId, int id) => Task.CompletedTask;
    public Task<int> BewaarOverigAsync(int offerteId, OfferteOverig o) => Task.FromResult(0);
    public Task WisOverigAsync(int offerteId, int id) => Task.CompletedTask;
    public Task<int> KopieerAsync(int bronId, int doelProjectId, OfferteKopieOpties opties, int? door) => Task.FromResult(0);
}

public partial class MySqlOfferteRepository
{
    /// <summary>Voorwaarde 'offerte staat nog op Aanmaken' (parameter @o = OfferteID).</summary>
    private const string OpenOfferte =
        "EXISTS (SELECT 1 FROM `Offertes` x WHERE x.`OfferteID` = @o AND COALESCE(x.`Status`, 1) <= 1 AND COALESCE(x.`Deleted`, 0) = 0)";

    private static decimal? Mn(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));

    private static object Db(object? w) => w ?? DBNull.Value;

    /// <summary>Voert een schrijfactie uit en meldt het als de offerte intussen op slot staat.</summary>
    private static async Task<int> SchrijfAsync(MySqlCommand cmd)
    {
        var res = await cmd.ExecuteScalarAsync();
        if (res is null or DBNull)
            throw new InvalidOperationException("Deze offerte staat niet meer op 'Aanmaken'; aanpassen kan niet meer.");
        return Convert.ToInt32(res);
    }

    public async Task<OfferteRegels> RegelsAsync(int offerteId)
    {
        var uit = new OfferteRegels();
        await using var cn = await OpenAsync();

        async Task Lees(string sql, Action<MySqlDataReader> regel)
        {
            await using var cmd = new MySqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@o", offerteId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) regel(r);
        }

        await Lees("SELECT `OfferteCatagorieID`, COALESCE(`Catagorie`, ''), COALESCE(`Omschrijving`, '') FROM `OfferteCatagorie` " +
                   " WHERE `OfferteID` = @o ORDER BY `Catagorie`, `OfferteCatagorieID`",
            r => uit.Categorieen.Add(new OfferteCategorie { Id = r.GetInt32(0), Naam = S(r, 1), Omschrijving = S(r, 2) }));
        await Lees("SELECT `OfferteTarievenID`, COALESCE(`Omschrijving`, ''), COALESCE(`Afkorting`, ''), COALESCE(`Tarief`, 0) " +
                   "  FROM `OfferteTarieven` WHERE `OfferteID` = @o ORDER BY `OfferteTarievenID`",
            r => uit.Tarieven.Add(new OfferteTarief { Id = r.GetInt32(0), Omschrijving = S(r, 1), Afkorting = S(r, 2), Tarief = M(r, 3) }));
        await Lees("SELECT `OfferteUrenID`, `OfferteCatagorieID`, COALESCE(`Uren`, 0), COALESCE(`Omschrijving`, ''), `OfferteTarievenID` " +
                   "  FROM `OfferteUren` WHERE `OfferteID` = @o ORDER BY `OfferteUrenID`",
            r => uit.Uren.Add(new OfferteUur { Id = r.GetInt32(0), CategorieId = I(r, 1), Uren = M(r, 2), Omschrijving = S(r, 3), TariefId = I(r, 4) }));
        await Lees("SELECT m.`OfferteMateriaalID`, m.`OfferteCatagorieID`, COALESCE(m.`Aantal`, 0), m.`MDEID`, COALESCE(m.`ArtikelNr`, ''), " +
                   "       COALESCE(m.`Omschrijving`, ''), COALESCE(m.`Prijs`, 0), COALESCE(m.`Korting`, 0), COALESCE(m.`Methode`, 0), " +
                   "       m.`InkoopprijsNonMDE`, m.`ToeslagNonMDE`, " +
                   "       COALESCE(NULLIF(a.`VastgesteldeKostprijs`, 0), m.`InkoopprijsNonMDE`) " +
                   "  FROM `OfferteMateriaal` m LEFT JOIN `Artikelen` a ON a.`ArtikelID` = m.`MDEID` " +
                   " WHERE m.`OfferteID` = @o ORDER BY m.`OfferteMateriaalID`",
            r => uit.Materiaal.Add(new OfferteMateriaalRegel
            {
                Id = r.GetInt32(0), CategorieId = I(r, 1), Aantal = M(r, 2), ArtikelId = I(r, 3), ArtikelNr = S(r, 4),
                Omschrijving = S(r, 5), Prijs = M(r, 6), Korting = M(r, 7), Methode = Convert.ToInt32(r.GetValue(8)),
                InkoopNonMde = Mn(r, 9), ToeslagNonMde = Mn(r, 10), Kostprijs = Mn(r, 11),
            }));
        await Lees("SELECT `OfferteOverigenID`, `OfferteCatagorieID`, COALESCE(`Aantal`, 0), COALESCE(`Omschrijving`, ''), COALESCE(`Prijs`, 0) " +
                   "  FROM `OfferteOverigen` WHERE `OfferteID` = @o ORDER BY `OfferteOverigenID`",
            r => uit.Overigen.Add(new OfferteOverig { Id = r.GetInt32(0), CategorieId = I(r, 1), Aantal = M(r, 2), Omschrijving = S(r, 3), Prijs = M(r, 4) }));
        return uit;
    }

    // ---------------- categorieën ----------------
    public async Task<int> BewaarCategorieAsync(int offerteId, OfferteCategorie c)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(c.Id == 0
            ? $"INSERT INTO `OfferteCatagorie` (`OfferteID`, `Catagorie`, `Omschrijving`) SELECT @o, @naam, @oms FROM DUAL WHERE {OpenOfferte}; " +
              "SELECT IF(ROW_COUNT() > 0, LAST_INSERT_ID(), NULL);"
            : $"UPDATE `OfferteCatagorie` SET `Catagorie` = @naam, `Omschrijving` = @oms WHERE `OfferteCatagorieID` = @id AND `OfferteID` = @o AND {OpenOfferte}; " +
              "SELECT IF(" + OpenOfferte + ", @id, NULL);", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        cmd.Parameters.AddWithValue("@id", c.Id);
        cmd.Parameters.AddWithValue("@naam", Kort(c.Naam.Trim(), 45));
        cmd.Parameters.AddWithValue("@oms", Kort(c.Omschrijving.Trim(), 250));
        return c.Id = await SchrijfAsync(cmd);
    }

    public async Task WisCategorieAsync(int offerteId, int categorieId)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        foreach (var sql in new[]
                 {
                     $"UPDATE `OfferteUren` SET `OfferteCatagorieID` = NULL WHERE `OfferteCatagorieID` = @id AND `OfferteID` = @o AND {OpenOfferte}",
                     $"UPDATE `OfferteMateriaal` SET `OfferteCatagorieID` = NULL WHERE `OfferteCatagorieID` = @id AND `OfferteID` = @o AND {OpenOfferte}",
                     $"UPDATE `OfferteOverigen` SET `OfferteCatagorieID` = NULL WHERE `OfferteCatagorieID` = @id AND `OfferteID` = @o AND {OpenOfferte}",
                     $"DELETE FROM `OfferteCatagorie` WHERE `OfferteCatagorieID` = @id AND `OfferteID` = @o AND {OpenOfferte}",
                 })
        {
            await using var cmd = new MySqlCommand(sql, cn, tx);
            cmd.Parameters.AddWithValue("@o", offerteId);
            cmd.Parameters.AddWithValue("@id", categorieId);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    // ---------------- tarieven ----------------
    public async Task<int> BewaarTariefAsync(int offerteId, OfferteTarief t)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(t.Id == 0
            ? $"INSERT INTO `OfferteTarieven` (`OfferteID`, `Omschrijving`, `Afkorting`, `Tarief`) SELECT @o, @oms, @afk, @tarief FROM DUAL WHERE {OpenOfferte}; " +
              "SELECT IF(ROW_COUNT() > 0, LAST_INSERT_ID(), NULL);"
            : $"UPDATE `OfferteTarieven` SET `Omschrijving` = @oms, `Afkorting` = @afk, `Tarief` = @tarief WHERE `OfferteTarievenID` = @id AND `OfferteID` = @o AND {OpenOfferte}; " +
              "SELECT IF(" + OpenOfferte + ", @id, NULL);", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        cmd.Parameters.AddWithValue("@id", t.Id);
        cmd.Parameters.AddWithValue("@oms", Kort(t.Omschrijving.Trim(), 45));
        cmd.Parameters.AddWithValue("@afk", Kort(t.Afkorting.Trim(), 15));
        cmd.Parameters.AddWithValue("@tarief", (double)t.Tarief);
        return t.Id = await SchrijfAsync(cmd);
    }

    public async Task WisTariefAsync(int offerteId, int tariefId)
    {
        await using var cn = await OpenAsync();
        await using (var kijk = new MySqlCommand("SELECT COUNT(*) FROM `OfferteUren` WHERE `OfferteTarievenID` = @id", cn))
        {
            kijk.Parameters.AddWithValue("@id", tariefId);
            var n = Convert.ToInt32(await kijk.ExecuteScalarAsync());
            if (n > 0) throw new InvalidOperationException($"Dit tarief wordt gebruikt door {n} urenregel(s); geef die eerst een ander tarief.");
        }
        await using var cmd = new MySqlCommand(
            $"DELETE FROM `OfferteTarieven` WHERE `OfferteTarievenID` = @id AND `OfferteID` = @o AND {OpenOfferte}", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        cmd.Parameters.AddWithValue("@id", tariefId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<TariefVerschil>> TariefVerschillenAsync(int offerteId)
    {
        var lijst = new List<TariefVerschil>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT t.`OfferteTarievenID`, t.`Afkorting`, COALESCE(rt.`Omschrijving`, ''), COALESCE(t.`Tarief`, 0), COALESCE(rt.`Tarief`, 0) " +
            "  FROM `OfferteTarieven` t JOIN `Offertes` o ON o.`OfferteID` = t.`OfferteID` " +
            "  JOIN `Projects` p ON p.`Project-Id` = o.`ProjectID` " +
            "  JOIN `RelationTarieven` rt ON rt.`RelationID` = p.`Werkmaatschappij` AND rt.`Afkorting` = t.`Afkorting` " +
            " WHERE t.`OfferteID` = @o AND ABS(COALESCE(t.`Tarief`, 0) - COALESCE(rt.`Tarief`, 0)) > 0.0001 ORDER BY t.`Afkorting`", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add(new TariefVerschil(r.GetInt32(0), S(r, 1), S(r, 2), M(r, 3), M(r, 4)));
        return lijst;
    }

    // ---------------- uren, materiaal, overigen ----------------
    public async Task<int> BewaarUurAsync(int offerteId, OfferteUur u)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(u.Id == 0
            ? "INSERT INTO `OfferteUren` (`OfferteID`, `OfferteCatagorieID`, `Uren`, `Omschrijving`, `OfferteTarievenID`) " +
              $"SELECT @o, @cat, @uren, @oms, @tarief FROM DUAL WHERE {OpenOfferte}; SELECT IF(ROW_COUNT() > 0, LAST_INSERT_ID(), NULL);"
            : "UPDATE `OfferteUren` SET `OfferteCatagorieID` = @cat, `Uren` = @uren, `Omschrijving` = @oms, `OfferteTarievenID` = @tarief " +
              $"WHERE `OfferteUrenID` = @id AND `OfferteID` = @o AND {OpenOfferte}; SELECT IF({OpenOfferte}, @id, NULL);", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        cmd.Parameters.AddWithValue("@id", u.Id);
        cmd.Parameters.AddWithValue("@cat", Db(u.CategorieId));
        cmd.Parameters.AddWithValue("@uren", (double)u.Uren);
        cmd.Parameters.AddWithValue("@oms", Kort(u.Omschrijving.Trim(), 200));
        cmd.Parameters.AddWithValue("@tarief", Db(u.TariefId));
        return u.Id = await SchrijfAsync(cmd);
    }

    public async Task<int> BewaarMateriaalAsync(int offerteId, OfferteMateriaalRegel m)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(m.Id == 0
            ? "INSERT INTO `OfferteMateriaal` (`OfferteID`, `OfferteCatagorieID`, `Aantal`, `MDEID`, `ArtikelNr`, `Omschrijving`, `Prijs`, " +
              "  `Korting`, `Methode`, `InkoopprijsNonMDE`, `ToeslagNonMDE`) " +
              $"SELECT @o, @cat, @aantal, @art, @nr, @oms, @prijs, @korting, @methode, @inkoop, @toeslag FROM DUAL WHERE {OpenOfferte}; " +
              "SELECT IF(ROW_COUNT() > 0, LAST_INSERT_ID(), NULL);"
            : "UPDATE `OfferteMateriaal` SET `OfferteCatagorieID` = @cat, `Aantal` = @aantal, `MDEID` = @art, `ArtikelNr` = @nr, " +
              "  `Omschrijving` = @oms, `Prijs` = @prijs, `Korting` = @korting, `Methode` = @methode, `InkoopprijsNonMDE` = @inkoop, " +
              $"  `ToeslagNonMDE` = @toeslag WHERE `OfferteMateriaalID` = @id AND `OfferteID` = @o AND {OpenOfferte}; SELECT IF({OpenOfferte}, @id, NULL);", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        cmd.Parameters.AddWithValue("@id", m.Id);
        cmd.Parameters.AddWithValue("@cat", Db(m.CategorieId));
        cmd.Parameters.AddWithValue("@aantal", (double)m.Aantal);
        cmd.Parameters.AddWithValue("@art", Db(m.ArtikelId));
        cmd.Parameters.AddWithValue("@nr", Kort(m.ArtikelNr.Trim(), 45));
        cmd.Parameters.AddWithValue("@oms", Kort(m.Omschrijving.Trim(), 260));
        cmd.Parameters.AddWithValue("@prijs", (double)m.Prijs);
        cmd.Parameters.AddWithValue("@korting", (double)m.Korting);
        cmd.Parameters.AddWithValue("@methode", m.Methode);
        cmd.Parameters.AddWithValue("@inkoop", m.InkoopNonMde is decimal i ? (double)i : DBNull.Value);
        cmd.Parameters.AddWithValue("@toeslag", m.ToeslagNonMde is decimal t ? (double)t : DBNull.Value);
        return m.Id = await SchrijfAsync(cmd);
    }

    public async Task<int> BewaarOverigAsync(int offerteId, OfferteOverig v)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(v.Id == 0
            ? "INSERT INTO `OfferteOverigen` (`OfferteID`, `OfferteCatagorieID`, `Aantal`, `Omschrijving`, `Prijs`) " +
              $"SELECT @o, @cat, @aantal, @oms, @prijs FROM DUAL WHERE {OpenOfferte}; SELECT IF(ROW_COUNT() > 0, LAST_INSERT_ID(), NULL);"
            : "UPDATE `OfferteOverigen` SET `OfferteCatagorieID` = @cat, `Aantal` = @aantal, `Omschrijving` = @oms, `Prijs` = @prijs " +
              $"WHERE `OfferteOverigenID` = @id AND `OfferteID` = @o AND {OpenOfferte}; SELECT IF({OpenOfferte}, @id, NULL);", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        cmd.Parameters.AddWithValue("@id", v.Id);
        cmd.Parameters.AddWithValue("@cat", Db(v.CategorieId));
        cmd.Parameters.AddWithValue("@aantal", (double)v.Aantal);
        cmd.Parameters.AddWithValue("@oms", Kort(v.Omschrijving.Trim(), 200));
        cmd.Parameters.AddWithValue("@prijs", (double)v.Prijs);
        return v.Id = await SchrijfAsync(cmd);
    }

    private async Task WisRegelAsync(string tabel, string sleutel, int offerteId, int id)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            $"DELETE FROM `{tabel}` WHERE `{sleutel}` = @id AND `OfferteID` = @o AND {OpenOfferte}", cn);
        cmd.Parameters.AddWithValue("@o", offerteId);
        cmd.Parameters.AddWithValue("@id", id);
        if (await cmd.ExecuteNonQueryAsync() == 0)
            throw new InvalidOperationException("Wissen lukte niet: de regel bestaat niet meer, of de offerte staat niet meer op 'Aanmaken'.");
    }

    public Task WisUurAsync(int offerteId, int id) => WisRegelAsync("OfferteUren", "OfferteUrenID", offerteId, id);
    public Task WisMateriaalAsync(int offerteId, int id) => WisRegelAsync("OfferteMateriaal", "OfferteMateriaalID", offerteId, id);
    public Task WisOverigAsync(int offerteId, int id) => WisRegelAsync("OfferteOverigen", "OfferteOverigenID", offerteId, id);

    // ---------------- kopiëren ----------------
    /// <summary>
    /// Access: OfferteKopieeren. Verschillen: alles in één transactie; regels worden via id-koppeling aan de nieuwe
    /// categorie/het nieuwe tarief gehangen (Access zocht op naam/afkorting en raakte daarbij de hele tabel); de taal
    /// gaat altijd mee (Access liet hem leeg als 'Algemeen' uit stond).
    /// </summary>
    public async Task<int> KopieerAsync(int bronId, int doelProjectId, OfferteKopieOpties op, int? door)
    {
        await using var cn = await OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        // bron lezen
        Offerte? bron;
        string bronNr;
        await using (var cmd = new MySqlCommand(
            "SELECT o.`ProjectID`, COALESCE(o.`Omschrijving`, ''), o.`Taal`, o.`Contact`, o.`OfferteTekst`, o.`Voettekst`, " +
            "       o.`Geldigheid`, o.`Levertijd`, o.`Betalingstermijn`, o.`Betaling`, o.`AlgemenVoorwaarden`, o.`Disclaimer`, " +
            "       o.`Ondertekening`, o.`Aantekeningen`, " + NrSql +
            "  FROM `Offertes` o JOIN `Projects` p ON p.`Project-Id` = o.`ProjectID` WHERE o.`OfferteID` = @b", cn, tx))
        {
            cmd.Parameters.AddWithValue("@b", bronId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) throw new InvalidOperationException("De bronofferte bestaat niet.");
            bron = new Offerte
            {
                ProjectId = r.GetInt32(0), Omschrijving = S(r, 1), Taal = I(r, 2), ContactId = I(r, 3), OfferteTekst = S(r, 4),
                Voettekst = S(r, 5), Geldigheid = S(r, 6), Levertijd = S(r, 7), Betalingstermijn = S(r, 8), Betaling = S(r, 9),
                AlgemeneVoorwaarden = B(r, 10), Disclaimer = S(r, 11), Ondertekening = B(r, 12), Aantekeningen = S(r, 13),
            };
            bronNr = S(r, 14);
        }

        int nummer;
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(MAX(`OfferteNummer`), 0) + 1 FROM `Offertes` WHERE `ProjectID` = @p FOR UPDATE", cn, tx))
        {
            cmd.Parameters.AddWithValue("@p", doelProjectId);
            nummer = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        int nieuwId;
        await using (var cmd = new MySqlCommand(
            "INSERT INTO `Offertes` (`OfferteNummer`, `ProjectID`, `Omschrijving`, `Status`, `AanmaakDatum`, `AangemaaktDoor`, `Contact`, " +
            "  `Taal`, `OfferteTekst`, `Voettekst`, `Geldigheid`, `Levertijd`, `Betalingstermijn`, `Betaling`, `AlgemenVoorwaarden`, " +
            "  `Disclaimer`, `Ondertekening`, `Aantekeningen`, `Bronofferte`, `Deleted`) " +
            "VALUES (@nr, @p, @oms, 1, NOW(), @door, @contact, @taal, @kop, @voet, @geld, @lever, @termijn, @betaling, @alg, @disc, " +
            "  @onder, @aant, @bron, 0); SELECT LAST_INSERT_ID();", cn, tx))
        {
            cmd.Parameters.AddWithValue("@nr", nummer);
            cmd.Parameters.AddWithValue("@p", doelProjectId);
            cmd.Parameters.AddWithValue("@oms", Kort(op.Algemeen ? "Kopie van: " + bron.Omschrijving : bron.Omschrijving, 250));
            cmd.Parameters.AddWithValue("@door", Db(door));
            cmd.Parameters.AddWithValue("@contact", op.Algemeen && bron.ProjectId == doelProjectId ? Db(bron.ContactId) : DBNull.Value);
            cmd.Parameters.AddWithValue("@taal", Db(bron.Taal ?? OfferteTaal.Nederlands));
            cmd.Parameters.AddWithValue("@kop", op.Algemeen ? bron.OfferteTekst : DBNull.Value);
            cmd.Parameters.AddWithValue("@voet", op.Algemeen ? bron.Voettekst : DBNull.Value);
            cmd.Parameters.AddWithValue("@geld", op.Voorwaarden ? bron.Geldigheid : "30 dagen");
            cmd.Parameters.AddWithValue("@lever", op.Voorwaarden ? bron.Levertijd : "");
            cmd.Parameters.AddWithValue("@termijn", op.Voorwaarden ? bron.Betalingstermijn : "");
            cmd.Parameters.AddWithValue("@betaling", op.Voorwaarden ? bron.Betaling : "");
            cmd.Parameters.AddWithValue("@alg", !op.Voorwaarden || bron.AlgemeneVoorwaarden ? -1 : 0);
            cmd.Parameters.AddWithValue("@disc", op.Voorwaarden ? bron.Disclaimer : "");
            cmd.Parameters.AddWithValue("@onder", op.Voorwaarden && bron.Ondertekening ? -1 : 0);
            cmd.Parameters.AddWithValue("@aant", bron.Aantekeningen);
            cmd.Parameters.AddWithValue("@bron", Kort($"{bronNr} ({bron.Omschrijving})", 250));
            nieuwId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // categorieën en tarieven altijd, met een koppeltabel oud -> nieuw
        var cats = new Dictionary<int, int>();
        var tarieven = new Dictionary<int, int>();
        async Task KopieerTabel(string selectSql, string insertSql, Dictionary<int, int> koppel)
        {
            var rijen = new List<object[]>();
            await using (var lees = new MySqlCommand(selectSql, cn, tx))
            {
                lees.Parameters.AddWithValue("@b", bronId);
                await using var r = await lees.ExecuteReaderAsync();
                while (await r.ReadAsync()) { var v = new object[r.FieldCount]; r.GetValues(v); rijen.Add(v); }
            }
            foreach (var rij in rijen)
            {
                await using var ins = new MySqlCommand(insertSql + "; SELECT LAST_INSERT_ID();", cn, tx);
                ins.Parameters.AddWithValue("@n", nieuwId);
                for (var i = 1; i < rij.Length; i++) ins.Parameters.AddWithValue("@k" + i, rij[i]);
                var id = Convert.ToInt32(await ins.ExecuteScalarAsync());
                koppel.Add(Convert.ToInt32(rij[0]), id);
            }
        }

        object Map(Dictionary<int, int> d, object waarde)
            => waarde is DBNull || waarde is null || !d.TryGetValue(Convert.ToInt32(waarde), out var n) ? DBNull.Value : n;

        await KopieerTabel("SELECT `OfferteCatagorieID`, `Catagorie`, `Omschrijving` FROM `OfferteCatagorie` WHERE `OfferteID` = @b",
            "INSERT INTO `OfferteCatagorie` (`OfferteID`, `Catagorie`, `Omschrijving`) VALUES (@n, @k1, @k2)", cats);
        await KopieerTabel("SELECT `OfferteTarievenID`, `Omschrijving`, `Afkorting`, `Tarief` FROM `OfferteTarieven` WHERE `OfferteID` = @b",
            "INSERT INTO `OfferteTarieven` (`OfferteID`, `Omschrijving`, `Afkorting`, `Tarief`) VALUES (@n, @k1, @k2, @k3)", tarieven);

        // regels: categorie en tarief vertalen naar de nieuwe ids
        async Task KopieerRegels(string selectSql, string insertSql, Func<object[], object[]> pas)
        {
            var rijen = new List<object[]>();
            await using (var lees = new MySqlCommand(selectSql, cn, tx))
            {
                lees.Parameters.AddWithValue("@b", bronId);
                await using var r = await lees.ExecuteReaderAsync();
                while (await r.ReadAsync()) { var v = new object[r.FieldCount]; r.GetValues(v); rijen.Add(pas(v)); }
            }
            foreach (var rij in rijen)
            {
                await using var ins = new MySqlCommand(insertSql, cn, tx);
                ins.Parameters.AddWithValue("@n", nieuwId);
                for (var i = 0; i < rij.Length; i++) ins.Parameters.AddWithValue("@k" + i, rij[i]);
                await ins.ExecuteNonQueryAsync();
            }
        }

        if (op.Uren)
            await KopieerRegels("SELECT `OfferteCatagorieID`, `Uren`, `Omschrijving`, `OfferteTarievenID` FROM `OfferteUren` WHERE `OfferteID` = @b ORDER BY `OfferteUrenID`",
                "INSERT INTO `OfferteUren` (`OfferteID`, `OfferteCatagorieID`, `Uren`, `Omschrijving`, `OfferteTarievenID`) VALUES (@n, @k0, @k1, @k2, @k3)",
                v => { v[0] = Map(cats, v[0]); v[3] = Map(tarieven, v[3]); return v; });
        if (op.Materiaal)
            await KopieerRegels("SELECT `OfferteCatagorieID`, `Aantal`, `MDEID`, `ArtikelNr`, `Omschrijving`, `Prijs`, `Korting`, `Methode`, `Bruto`, " +
                                "`InkoopprijsNonMDE`, `ToeslagNonMDE` FROM `OfferteMateriaal` WHERE `OfferteID` = @b ORDER BY `OfferteMateriaalID`",
                "INSERT INTO `OfferteMateriaal` (`OfferteID`, `OfferteCatagorieID`, `Aantal`, `MDEID`, `ArtikelNr`, `Omschrijving`, `Prijs`, `Korting`, " +
                "`Methode`, `Bruto`, `InkoopprijsNonMDE`, `ToeslagNonMDE`) VALUES (@n, @k0, @k1, @k2, @k3, @k4, @k5, @k6, @k7, @k8, @k9, @k10)",
                v => { v[0] = Map(cats, v[0]); return v; });
        if (op.Overigen)
            await KopieerRegels("SELECT `OfferteCatagorieID`, `Aantal`, `Omschrijving`, `Prijs` FROM `OfferteOverigen` WHERE `OfferteID` = @b ORDER BY `OfferteOverigenID`",
                "INSERT INTO `OfferteOverigen` (`OfferteID`, `OfferteCatagorieID`, `Aantal`, `Omschrijving`, `Prijs`) VALUES (@n, @k0, @k1, @k2, @k3)",
                v => { v[0] = Map(cats, v[0]); return v; });

        await tx.CommitAsync();
        return nieuwId;
    }
}
