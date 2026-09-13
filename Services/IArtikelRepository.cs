using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

public interface IArtikelRepository
{
    bool Beschikbaar { get; }

    /// <summary>
    /// Zoekt op losse steekwoorden: 'PLC NX 1100' levert alleen artikelen waarin
    /// alle drie voorkomen — in de code, de omschrijving, het merk of het groepspad.
    /// </summary>
    /// <param name="groepIds">
    /// Gekozen uit de boom: de groep zelf plus alles eronder. Het pad is als tekst
    /// niet altijd in de tabel terug te vinden, met id's klopt de selectie altijd.
    /// </param>
    Task<List<ArtikelRegel>> ZoekAsync(string? zoek, string? groepspad, bool maakdelen, int max,
                                       IReadOnlyCollection<int>? groepIds = null);

    Task<Artikel?> GetAsync(int artikelId);
    Task<List<ArtikelGroep>> GetGroepenAsync(int artikelId);

    /// <summary>Alle artikelgroepen, met het aantal artikelen erin. Voor de keuzeboom.</summary>
    Task<List<Groepknoop>> GetGroepenBoomAsync();

    /// <summary>
    /// Waar de boomstructuur vandaan kwam. Alleen om te kunnen zien of de juiste
    /// kolom is gevonden; pas gevuld nadat de boom een keer is opgehaald.
    /// </summary>
    string GroepenHerkomst { get; }

    /// <summary>Bewaart de bewerkbare velden van een artikel.</summary>
    Task BewaarArtikelAsync(Artikel artikel, int? doorWerknemerId);

    /// <summary>Koppelt een artikel aan een groep, of haalt de koppeling weg.</summary>
    Task ZetGroepAsync(int artikelId, int groepId, bool aan);

    /// <summary>De merken voor het keuzelijstje, op naam gesorteerd.</summary>
    Task<List<(int Id, string Naam)>> GetMerkenAsync();

    /// <summary>Aliassen uit SEE Electrical en Inventor.</summary>
    Task<List<(string Alias, string Bron)>> GetAliassenAsync(int artikelId);

    /// <summary>
    /// De onderdelen waaruit een artikel is samengesteld. Een onderdeel dat zelf
    /// een samenstelling is, kan opnieuw met deze methode worden uitgeklapt.
    /// </summary>
    Task<List<Samenstellingregel>> GetSamenstellingAsync(int artikelId);

    /// <summary>Aantal of opmerking van een regel bijwerken, of een nieuwe toevoegen.</summary>
    Task<int> BewaarSamenstellingregelAsync(int parentId, Samenstellingregel regel);

    Task VerwijderSamenstellingregelAsync(int regelId);

    /// <summary>Zet de vlag 'samenstelling' van een artikel aan of uit.</summary>
    Task ZetSamenstellingAsync(int artikelId, bool aan);

    /// <summary>
    /// Legt de inkoopprijs van een artikel vast en berekent daaruit de
    /// verkoopprijs met de toeslag uit de algemene instellingen.
    /// </summary>
    Task ZetKostprijsAsync(int artikelId, decimal kostprijs, decimal toeslagProcent);

    /// <summary>
    /// De kostprijs zoals die uit de stuklijst volgt: van een samenstelling de som
    /// van de onderdelen, doorgerekend tot onderin. Voor een gewoon artikel is het
    /// gewoon de vastgestelde kostprijs.
    /// </summary>
    Task<decimal> BerekendeKostprijsAsync(int artikelId);
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenArtikelRepository : IArtikelRepository
{
    public bool Beschikbaar => false;
    public Task<List<ArtikelRegel>> ZoekAsync(string? zoek, string? groepspad, bool maakdelen, int max,
                                              IReadOnlyCollection<int>? groepIds = null)
        => Task.FromResult(new List<ArtikelRegel>());
    public Task<Artikel?> GetAsync(int artikelId) => Task.FromResult<Artikel?>(null);
    public Task<List<ArtikelGroep>> GetGroepenAsync(int artikelId) => Task.FromResult(new List<ArtikelGroep>());
    public Task<List<Groepknoop>> GetGroepenBoomAsync() => Task.FromResult(new List<Groepknoop>());
    public string GroepenHerkomst => "geen database";
    public Task BewaarArtikelAsync(Artikel artikel, int? doorWerknemerId) => Task.CompletedTask;
    public Task ZetGroepAsync(int artikelId, int groepId, bool aan) => Task.CompletedTask;
    public Task<List<(int Id, string Naam)>> GetMerkenAsync() => Task.FromResult(new List<(int, string)>());
    public Task<List<(string Alias, string Bron)>> GetAliassenAsync(int artikelId)
        => Task.FromResult(new List<(string, string)>());
    public Task<List<Samenstellingregel>> GetSamenstellingAsync(int artikelId)
        => Task.FromResult(new List<Samenstellingregel>());
    public Task<int> BewaarSamenstellingregelAsync(int parentId, Samenstellingregel regel) => Task.FromResult(0);
    public Task VerwijderSamenstellingregelAsync(int regelId) => Task.CompletedTask;
    public Task ZetSamenstellingAsync(int artikelId, bool aan) => Task.CompletedTask;
    public Task ZetKostprijsAsync(int artikelId, decimal kostprijs, decimal toeslagProcent)
        => Task.CompletedTask;
    public Task<decimal> BerekendeKostprijsAsync(int artikelId) => Task.FromResult(0m);
}

/// <summary>Artikelen en maakdelen uit de tabel Artikelen.</summary>
public class MySqlArtikelRepository : IArtikelRepository
{
    private readonly string _cs;
    private (string Tabel, string Id, string Naam)? _merken;
    private bool _merkenGezocht;
    private Dictionary<int, string>? _merkNamen;
    private string? _eenheidTabel;

    public MySqlArtikelRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    // ============================================================
    //  Merken — tabelnaam is niet zeker, dus die zoeken we één keer op
    // ============================================================
    private async Task<(string Tabel, string Id, string Naam)?> MerkTabelAsync()
    {
        if (_merkenGezocht) return _merken;
        _merkenGezocht = true;

        foreach (var (tabel, id, naam) in new[]
        {
            ("Brands", "BrandID", "Brand"),
            ("Brands", "BrandID", "Name"),
            ("Merken", "MerkID", "Merk"),
            ("Merk", "MerkID", "Omschrijving"),
        })
        {
            try
            {
                await using var cn = await OpenAsync();
                await using var cmd = new MySqlCommand($"SELECT `{id}`, `{naam}` FROM `{tabel}` LIMIT 1", cn);
                await cmd.ExecuteScalarAsync();
                return _merken = (tabel, id, naam);
            }
            catch { /* volgende combinatie proberen */ }
        }
        return _merken = null;
    }

    private async Task<Dictionary<int, string>> MerkNamenAsync()
    {
        if (_merkNamen is not null) return _merkNamen;

        var uit = new Dictionary<int, string>();
        var tabel = await MerkTabelAsync();
        if (tabel is null) return _merkNamen = uit;

        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                $"SELECT `{tabel.Value.Id}`, `{tabel.Value.Naam}` FROM `{tabel.Value.Tabel}`", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                if (!r.IsDBNull(0)) uit[r.GetInt32(0)] = r.IsDBNull(1) ? "" : r.GetString(1);
        }
        catch { }

        return _merkNamen = uit;
    }

    // ============================================================
    //  Zoeken
    // ============================================================
    public async Task<List<ArtikelRegel>> ZoekAsync(string? zoek, string? groepspad, bool maakdelen, int max,
                                                    IReadOnlyCollection<int>? groepIds = null)
    {
        // '#4528' betekent: dít artikelnummer, verder niet zoeken.
        var kort = (zoek ?? "").Trim();
        if (kort.StartsWith("#") && int.TryParse(kort[1..].Trim(), out var direct))
            return await OpNummerAsync(direct);

        var woorden = kort
            .Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => w.Length > 0)
            .Take(6)
            .ToList();

        var merk = await MerkTabelAsync();
        var delen = new List<string>
        {
            maakdelen ? "(a.`Maakdeel` <> 0)" : "(a.`Maakdeel` = 0 OR a.`Maakdeel` IS NULL)",
        };

        // Elk woord moet érgens voorkomen; samen dus een AND van OR's.
        for (var i = 0; i < woorden.Count; i++)
        {
            var stukken = new List<string>
            {
                $"a.`ArtikelCodeMDE` LIKE @w{i}",
                $"a.`ArtikelCodeFactory` LIKE @w{i}",
                $"a.`OmschrijvingNL` LIKE @w{i}",
                $"CAST(a.`ArtikelID` AS CHAR) = @g{i}",
            };

            if (merk is not null)
                stukken.Add($"a.`BrandID` IN (SELECT `{merk.Value.Id}` FROM `{merk.Value.Tabel}` " +
                            $"                WHERE `{merk.Value.Naam}` LIKE @w{i})");

            stukken.Add($"EXISTS (SELECT 1 FROM `ArtikelGroepenLink` l " +
                        $"          JOIN `ArtikelGroep` g ON g.`ArtikelGroepID` = l.`GroupID` " +
                        $"         WHERE l.`ArtikelID` = a.`ArtikelID` " +
                        $"           AND (g.`Omschrijving` LIKE @w{i} OR g.`ParentTree` LIKE @w{i}))");

            delen.Add("(" + string.Join(" OR ", stukken) + ")");
        }

        // Filteren op groep: op de gekozen groep en alles eronder (de id's komen uit
        // de boom), of op tekst als iemand het groepsveld zelf invult. LOCATE in
        // plaats van LIKE, want in een LIKE is de backslash uit de paden een
        // stuurteken en valt hij weg.
        var ids = (groepIds ?? Array.Empty<int>()).Where(i => i > 0).Distinct().Take(2000).ToList();
        if (!string.IsNullOrWhiteSpace(groepspad) || ids.Count > 0)
        {
            var voorwaarden = new List<string>();

            // getallen uit onze eigen boom, dus veilig om zo in de query te zetten
            if (ids.Count > 0) voorwaarden.Add("l2.`GroupID` IN (" + string.Join(",", ids) + ")");

            // Spaties rond de scheiders verschillen per rij ('A\B' tegenover 'A \ B'),
            // dus die laten we bij het vergelijken aan beide kanten weg.
            if (!string.IsNullOrWhiteSpace(groepspad))
                voorwaarden.Add("LOCATE(@groep, REPLACE(g2.`ParentTree`, ' ', '')) > 0");

            delen.Add("EXISTS (SELECT 1 FROM `ArtikelGroepenLink` l2 " +
                      "          JOIN `ArtikelGroep` g2 ON g2.`ArtikelGroepID` = l2.`GroupID` " +
                      "         WHERE l2.`ArtikelID` = a.`ArtikelID` " +
                      "           AND (" + string.Join(" OR ", voorwaarden) + "))");
        }

        var lijst = new List<ArtikelRegel>();
        var namen = await MerkNamenAsync();

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT a.`ArtikelID`, COALESCE(a.`ArtikelCodeMDE`, ''), " +
            "       COALESCE(a.`OmschrijvingNL`, ''), a.`BrandID` " +
            "  FROM `Artikelen` a " +
            " WHERE " + string.Join(" AND ", delen) +
            " ORDER BY a.`ArtikelCodeMDE` " +
            $" LIMIT {Math.Clamp(max, 1, 500)}", cn);

        for (var i = 0; i < woorden.Count; i++)
        {
            cmd.Parameters.AddWithValue("@w" + i, "%" + woorden[i] + "%");
            cmd.Parameters.AddWithValue("@g" + i, woorden[i]);
        }
        if (!string.IsNullOrWhiteSpace(groepspad))
            cmd.Parameters.AddWithValue("@groep", groepspad.Replace(" ", ""));

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var merkId = r.IsDBNull(3) ? (int?)null : Convert.ToInt32(r.GetValue(3));
            lijst.Add(new ArtikelRegel(
                r.GetInt32(0),
                r.IsDBNull(1) ? "" : r.GetString(1),
                r.IsDBNull(2) ? "" : r.GetString(2),
                merkId is not null && namen.TryGetValue(merkId.Value, out var m) ? m : ""));
        }
        return lijst;
    }

    /// <summary>Precies één artikel op zijn MDE-nummer, ongeacht maakdeel of niet.</summary>
    private async Task<List<ArtikelRegel>> OpNummerAsync(int artikelId)
    {
        var lijst = new List<ArtikelRegel>();
        var namen = await MerkNamenAsync();

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ArtikelID`, COALESCE(`ArtikelCodeMDE`, ''), " +
            "       COALESCE(`OmschrijvingNL`, ''), `BrandID` " +
            "  FROM `Artikelen` WHERE `ArtikelID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", artikelId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var merkId = r.IsDBNull(3) ? (int?)null : Convert.ToInt32(r.GetValue(3));
            lijst.Add(new ArtikelRegel(
                r.GetInt32(0),
                r.IsDBNull(1) ? "" : r.GetString(1),
                r.IsDBNull(2) ? "" : r.GetString(2),
                merkId is not null && namen.TryGetValue(merkId.Value, out var m) ? m : ""));
        }
        return lijst;
    }

    // ============================================================
    //  Eén artikel
    // ============================================================
    public async Task<Artikel?> GetAsync(int artikelId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT a.*, COALESCE(t.`NaamVoluit`, '') AS ToegDoor, COALESCE(g.`NaamVoluit`, '') AS GewDoor " +
            "  FROM `Artikelen` a " +
            "  LEFT JOIN `Werknemers` t ON t.`Werknemer-id` = a.`ToegevoegdDoor` " +
            "  LEFT JOIN `Werknemers` g ON g.`Werknemer-id` = a.`AangepastDoor` " +
            " WHERE a.`ArtikelID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", artikelId);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        // op naam lezen: de vololgorde van de kolommen doet er dan niet toe
        string S(string k) { try { var v = r[k]; return v is DBNull ? "" : v.ToString() ?? ""; } catch { return ""; } }
        int? I(string k) { try { var v = r[k]; return v is DBNull ? null : Convert.ToInt32(v); } catch { return null; } }
        double? D(string k) { try { var v = r[k]; return v is DBNull ? null : Convert.ToDouble(v); } catch { return null; } }
        decimal? M(string k) { try { var v = r[k]; return v is DBNull ? null : Convert.ToDecimal(v); } catch { return null; } }
        DateTime? T(string k) { try { var v = r[k]; return v is DBNull ? null : Convert.ToDateTime(v); } catch { return null; } }
        bool B(string k) { try { var v = r[k]; return v is not DBNull && Convert.ToInt64(v) != 0; } catch { return false; } }

        var merkId = I("BrandID");
        var namen = await MerkNamenAsync();

        var artikel = new Artikel
        {
            Id = artikelId,
            Code = S("ArtikelCodeMDE"),
            CodeFabrikant = S("ArtikelCodeFactory"),
            MerkId = merkId,
            Merk = merkId is not null && namen.TryGetValue(merkId.Value, out var m) ? m : "",

            OmschrijvingNl = S("OmschrijvingNL"),
            OmschrijvingEn = S("OmschrijvingEN"),
            OmschrijvingFr = S("OmschrijvingFR"),
            Opmerking = S("Opmerking"),

            Ean = S("EANcode"),
            OcCodeOmron = S("OCcodeOmron"),

            Samenstelling = B("Samenstelling"),
            Voorraadartikel = B("Stockpart"),
            Standaard = B("Standaard"),
            InventorPart = B("InventorInkoopDeel"),
            IsProduct = B("isProduct"),
            Webshop = B("WebshopArtikelYN"),
            Uitleenbaar = B("Uitleenbaar"),
            CheckNodig = B("CheckNodig"),

            Kostprijs = M("VastgesteldeKostprijs"),
            VerkoopprijsProject = M("Verkoopprijs project"),
            VerkoopprijsWebshop = M("Verkoopprijs webshop"),
            Schattingsprijs = M("SchattingsKostprijsStuk"),

            MinimumVoorraad = D("MinimumVoorraad"),
            Bestelhoeveelheid = D("BestelHoeveelheid"),
            AantalInVoorraad = D("AantalInVoorraad"),
            Gewicht = D("Gewicht"),
            AantalUitgeleend = I("AantalObjectenUitgeleend"),

            Maakdeel = B("Maakdeel"),
            Bewerking1 = I("Bewerking1"),
            Bewerking2 = I("Bewerking2"),
            Bewerking3 = I("Bewerking3"),
            PlaatdikteMm = D("PlaatdikteInMm"),
            Materiaal = S("Material"),
            InventorReferentie = S("InventorReference"),
            InventorBestand = S("InventorFullFilename"),
            InventorInkoopdeel = B("InventorInkoopDeel"),

            Toegevoegd = T("DatumToegevoegd"),
            ToegevoegdDoor = S("ToegDoor"),
            Gewijzigd = T("DatumLaatsteAanpassing"),
            GewijzigdDoor = S("GewDoor"),
        };

        artikel.Eenheid = await EenheidAsync(I("ArtikelEenheidID"));
        return artikel;
    }

    /// <summary>De eenheid ('st', 'm') bij een ArtikelEenheidID.</summary>
    private async Task<string> EenheidAsync(int? id)
    {
        if (id is null) return "";

        foreach (var tabel in new[] { _eenheidTabel, "VE", "ArtikelEenheid", "Eenheden" })
        {
            if (string.IsNullOrWhiteSpace(tabel)) continue;
            try
            {
                await using var cn = await OpenAsync();
                await using var cmd = new MySqlCommand($"SELECT * FROM `{tabel}` LIMIT 500", cn);
                await using var r = await cmd.ExecuteReaderAsync();

                while (await r.ReadAsync())
                {
                    int? rijId = null;
                    string? naam = null;
                    for (var i = 0; i < r.FieldCount; i++)
                    {
                        var kolom = r.GetName(i);
                        if (rijId is null && kolom.Contains("id", StringComparison.OrdinalIgnoreCase)
                            && !r.IsDBNull(i) && int.TryParse(r.GetValue(i).ToString(), out var v)) rijId = v;
                        if (naam is null && (kolom.Equals("Units", StringComparison.OrdinalIgnoreCase)
                                          || kolom.Equals("Eenheid", StringComparison.OrdinalIgnoreCase)
                                          || kolom.Equals("Omschrijving", StringComparison.OrdinalIgnoreCase))
                            && !r.IsDBNull(i)) naam = r.GetValue(i).ToString();
                    }
                    if (rijId == id && naam is not null)
                    {
                        _eenheidTabel = tabel;
                        return naam;
                    }
                }
            }
            catch { }
        }
        return "";
    }

    public async Task<List<ArtikelGroep>> GetGroepenAsync(int artikelId)
    {
        var lijst = new List<ArtikelGroep>();
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT g.`ArtikelGroepID`, COALESCE(g.`Omschrijving`, ''), COALESCE(g.`ParentTree`, '') " +
                "  FROM `ArtikelGroepenLink` l " +
                "  JOIN `ArtikelGroep` g ON g.`ArtikelGroepID` = l.`GroupID` " +
                " WHERE l.`ArtikelID` = @id " +
                " ORDER BY g.`ParentTree`", cn);
            cmd.Parameters.AddWithValue("@id", artikelId);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                lijst.Add(new ArtikelGroep(r.GetInt32(0), r.GetString(1), r.GetString(2)));
        }
        catch { }
        return lijst;
    }

    // ============================================================
    //  De boom met artikelgroepen
    //
    //  Access bouwt deze boom op de kolom die naar de bovenliggende groep wijst.
    //  Die kolom heet niet overal hetzelfde, dus we zoeken hem één keer op. Lukt
    //  dat niet, dan vallen we terug op het pad in 'ParentTree' — minder
    //  betrouwbaar, want dat pad is niet bij elke rij ingevuld.
    // ============================================================
    private string? _ouderKolom;
    private bool _ouderKolomGezocht;

    public string GroepenHerkomst { get; private set; } = "nog niet opgehaald";

    public async Task<List<Groepknoop>> GetGroepenBoomAsync()
    {
        var rijen = new List<(int Id, string Naam, string Pad, int? Ouder, int Aantal)>();
        try
        {
            await using var cn = await OpenAsync();
            var ouderKolom = await OuderKolomAsync(cn);

            await using var cmd = new MySqlCommand(
                "SELECT g.`ArtikelGroepID`, COALESCE(g.`Omschrijving`, ''), COALESCE(g.`ParentTree`, ''), " +
                (ouderKolom is null ? "NULL" : $"g.`{ouderKolom}`") + ", " +
                "       (SELECT COUNT(*) FROM `ArtikelGroepenLink` l WHERE l.`GroupID` = g.`ArtikelGroepID`) " +
                "  FROM `ArtikelGroep` g " +
                " ORDER BY g.`Omschrijving`", cn);

            // De ouderkolom is geraden; blijkt hij geen getal te bevatten, dan telt
            // die rij gewoon als hoofdgroep in plaats van dat de hele boom sneuvelt.
            int? Ouder(MySqlDataReader lezer)
            {
                if (lezer.IsDBNull(3)) return null;
                try { return Convert.ToInt32(lezer.GetValue(3)); } catch { return null; }
            }

            await using var r = (MySqlDataReader)await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                rijen.Add((r.GetInt32(0), r.GetString(1), r.GetString(2),
                           Ouder(r), Convert.ToInt32(r.GetValue(4))));
        }
        catch (Exception ex)
        {
            GroepenHerkomst = "mislukt: " + ex.Message;
            return new List<Groepknoop>();
        }

        // Wijst minstens één rij naar een bestaande andere groep, dan is die kolom
        // de echte boomstructuur en gebruiken we die.
        var bestaat = rijen.Select(x => x.Id).ToHashSet();
        if (rijen.Any(x => x.Ouder is int o && o != x.Id && bestaat.Contains(o)))
        {
            GroepenHerkomst = $"kolom {_ouderKolom}";
            return BoomOpOuder(rijen);
        }

        GroepenHerkomst = _ouderKolom is null
            ? "ParentTree (geen ouderkolom gevonden)"
            : $"ParentTree (kolom {_ouderKolom} verwijst nergens naar)";
        return BoomOpPad(rijen.Select(x => (x.Id, x.Naam, x.Pad, x.Aantal)).ToList());
    }

    /// <summary>
    /// De boom zoals Access hem toont: elke groep hangt onder de groep waar zijn
    /// ouderkolom naar wijst. Wijst hij nergens heen, dan is het een hoofdgroep.
    /// </summary>
    private static List<Groepknoop> BoomOpOuder(List<(int Id, string Naam, string Pad, int? Ouder, int Aantal)> rijen)
    {
        var opId = rijen.ToDictionary(
            x => x.Id,
            x => new Groepknoop { Id = x.Id, Naam = x.Naam.Trim(), Aantal = x.Aantal, OuderId = x.Ouder });

        var wortels = new List<Groepknoop>();
        foreach (var rij in rijen)
        {
            var knoop = opId[rij.Id];
            if (rij.Ouder is int o && o != rij.Id && opId.TryGetValue(o, out var ouder))
                ouder.Kinderen.Add(knoop);
            else
            {
                knoop.OuderId = null;
                wortels.Add(knoop);
            }
        }

        // Een kringetje (A onder B, B onder A) zou nooit bij een wortel uitkomen en
        // dus onzichtbaar blijven. Wat niet bereikbaar is, hangen we bovenin.
        var bereikt = new HashSet<int>();
        void Pad(Groepknoop k, string boven, int diepte)
        {
            if (!bereikt.Add(k.Id)) return;

            k.Pad = boven.Length == 0 ? k.Naam : boven + "\\" + k.Naam;

            // Terugverwijzingen er hier uit halen, niet verderop: knippen in de lijst
            // waar de aanroeper overheen loopt geeft een fout.
            k.Kinderen.RemoveAll(kind => diepte >= 20 || bereikt.Contains(kind.Id));
            k.Kinderen.Sort((a, b) => string.Compare(a.Naam, b.Naam, StringComparison.OrdinalIgnoreCase));

            foreach (var kind in k.Kinderen) Pad(kind, k.Pad, diepte + 1);
        }

        foreach (var w in wortels) Pad(w, "", 0);

        foreach (var rij in rijen)
        {
            if (bereikt.Contains(rij.Id)) continue;

            var los = opId[rij.Id];
            // eerst losmaken van zijn ouder, anders staat hij straks twee keer in de boom
            if (rij.Ouder is int o && opId.TryGetValue(o, out var ouder)) ouder.Kinderen.Remove(los);

            los.OuderId = null;
            wortels.Add(los);
            Pad(los, "", 0);
        }

        wortels.Sort((a, b) => string.Compare(a.Naam, b.Naam, StringComparison.OrdinalIgnoreCase));
        return wortels;
    }

    /// <summary>Terugval: de boom uit de padtekst in 'ParentTree'.</summary>
    private static List<Groepknoop> BoomOpPad(List<(int Id, string Naam, string Pad, int Aantal)> rijen)
    {
        var scheider = Scheider(rijen.Select(x => x.Pad));

        var opPad = new Dictionary<string, Groepknoop>(StringComparer.OrdinalIgnoreCase);
        var wortels = new List<Groepknoop>();

        // Zorgt dat een pad (en alles erboven) in de boom bestaat en geeft die knoop terug.
        Groepknoop Zorg(string pad)
        {
            if (opPad.TryGetValue(pad, out var bestaand)) return bestaand;

            var knip = pad.LastIndexOf(scheider);
            var knoop = new Groepknoop
            {
                Naam = knip < 0 ? pad : pad[(knip + 1)..],
                Pad = pad,
            };
            opPad[pad] = knoop;

            if (knip <= 0) wortels.Add(knoop);   // <= 0: een pad kan niet met de scheider beginnen
            else
            {
                var ouder = Zorg(pad[..knip]);
                ouder.Kinderen.Add(knoop);
            }
            return knoop;
        }

        foreach (var rij in rijen)
        {
            var pad = VolledigPad(rij.Naam, rij.Pad, scheider);
            if (pad.Length == 0) continue;

            var knoop = Zorg(pad);
            knoop.Id = rij.Id;               // deze groep bestaat echt, dus kiesbaar
            knoop.Aantal = rij.Aantal;
        }

        // ouder-id's invullen en alles alfabetisch zetten
        void Netjes(Groepknoop k, int? ouderId)
        {
            k.OuderId = ouderId;
            k.Kinderen.Sort((a, b) => string.Compare(a.Naam, b.Naam, StringComparison.OrdinalIgnoreCase));
            foreach (var kind in k.Kinderen) Netjes(kind, k.Id == 0 ? ouderId : k.Id);
        }

        wortels.Sort((a, b) => string.Compare(a.Naam, b.Naam, StringComparison.OrdinalIgnoreCase));
        foreach (var w in wortels) Netjes(w, null);
        return wortels;
    }

    /// <summary>
    /// De kolom in ArtikelGroep die naar de bovenliggende groep wijst. Welke naam
    /// dat is weten we niet zeker, dus we kijken welke van de gangbare namen echt
    /// bestaat. Vinden we niets, dan blijft het null en valt de boom terug op het pad.
    /// </summary>
    private async Task<string?> OuderKolomAsync(MySqlConnection cn)
    {
        if (_ouderKolomGezocht) return _ouderKolom;
        _ouderKolomGezocht = true;

        var kandidaten = new[]
        {
            "MemberOfGroup",        // zo heet hij in deze database
            "ParentID", "ParentGroupID", "ParentGroepID", "ParentArtikelGroepID",
            "ArtikelGroepParentID", "OuderID", "HoofdGroepID", "HoofdGroep",
            "BovenliggendeGroepID", "Parent", "ParentGroep", "MemberOf",
        };

        try
        {
            // Alleen kolommen met een getal erin: een naam van de bovenliggende
            // groep in plaats van een verwijzing levert geen boom op.
            await using var cmd = new MySqlCommand(
                "SELECT `COLUMN_NAME` FROM `information_schema`.`COLUMNS` " +
                " WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'ArtikelGroep' " +
                "   AND `DATA_TYPE` IN ('int','bigint','smallint','mediumint','tinyint','decimal')", cn);

            var aanwezig = new List<string>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) aanwezig.Add(r.GetString(0));

            _ouderKolom = kandidaten.FirstOrDefault(
                k => aanwezig.Any(a => a.Equals(k, StringComparison.OrdinalIgnoreCase)));

            // niets uit de lijst: dan nog één poging op de vorm van de naam
            _ouderKolom ??= aanwezig.FirstOrDefault(
                a => (a.Contains("parent", StringComparison.OrdinalIgnoreCase)
                   || a.Contains("memberof", StringComparison.OrdinalIgnoreCase))
                  && !a.Contains("tree", StringComparison.OrdinalIgnoreCase));
        }
        catch { _ouderKolom = null; }

        return _ouderKolom;
    }

    /// <summary>Welk teken de niveaus in het pad scheidt; dat verschilt per database.</summary>
    private static char Scheider(IEnumerable<string> paden)
    {
        var kandidaten = new[] { '\\', '/', '>', '|' };
        var tellingen = kandidaten.ToDictionary(c => c, _ => 0);
        foreach (var pad in paden)
            foreach (var c in pad)
                if (tellingen.ContainsKey(c)) tellingen[c]++;

        var beste = tellingen.OrderByDescending(p => p.Value)
                             .ThenBy(p => Array.IndexOf(kandidaten, p.Key))
                             .First();
        return beste.Value > 0 ? beste.Key : '\\';
    }

    /// <summary>
    /// Haalt lege niveaus uit een pad ('a\\\\b' of '\\a') weg. Zonder dit komen er
    /// naamloze takken in de boom te staan.
    /// </summary>
    private static string Normaliseer(string pad, char scheider)
        => string.Join(scheider, pad.Split(scheider)
                                    .Select(d => d.Trim())
                                    .Where(d => d.Length > 0));

    /// <summary>
    /// Het volledige pad van een groep. Sommige rijen hebben hun eigen naam al
    /// achteraan het pad staan, andere niet — beide gevallen komen hier goed uit.
    /// </summary>
    private static string VolledigPad(string naam, string pad, char scheider)
    {
        // ook de naam zelf normaliseren: die kan de scheider bevatten
        naam = Normaliseer(naam, scheider);
        pad = Normaliseer(pad, scheider);

        if (pad.Length == 0) return naam;
        if (naam.Length == 0) return pad;

        var laatste = pad.Split(scheider).Last();
        return laatste.Equals(naam, StringComparison.OrdinalIgnoreCase)
            ? pad
            : pad + scheider + naam;
    }

    /// <summary>
    /// De onderdelen van een samenstelling. De omschrijving komt bij voorkeur uit
    /// het artikel zelf; staat die er niet, dan die van de samenstellingsregel.
    /// 'IsSamenstelling' vertelt of dit onderdeel verder uit te klappen is.
    /// </summary>
    public async Task<List<Samenstellingregel>> GetSamenstellingAsync(int artikelId)
    {
        var regels = await LeesSamenstellingAsync(artikelId);

        // Een onderdeel dat zelf een samenstelling is, krijgt de prijs die uit zijn
        // eigen stuklijst volgt in plaats van zijn vastgestelde kostprijs.
        var gezien = new HashSet<int>();
        foreach (var regel in regels.Where(r => r.IsSamenstelling))
            regel.Kostprijs = await RekenAsync(regel.ArtikelId, gezien, 0);

        return regels;
    }

    private async Task<List<Samenstellingregel>> LeesSamenstellingAsync(int artikelId)
    {
        var lijst = new List<Samenstellingregel>();

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT s.`ArtikelSamenstellingID`, s.`ArtikelID`, COALESCE(s.`Aantal`, 0), " +
            "       COALESCE(a.`ArtikelCodeMDE`, ''), " +
            "       COALESCE(NULLIF(a.`OmschrijvingNL`, ''), s.`Omschrijving`, ''), " +
            "       COALESCE(s.`Opmerking`, ''), a.`VastgesteldeKostprijs`, " +
            "       COALESCE(a.`Samenstelling`, 0) " +
            "  FROM `ArtikelSamenstelling` s " +
            "  LEFT JOIN `Artikelen` a ON a.`ArtikelID` = s.`ArtikelID` " +
            " WHERE s.`ArtikelParentID` = @id " +
            " ORDER BY a.`ArtikelCodeMDE`", cn);
        cmd.Parameters.AddWithValue("@id", artikelId);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            bool B(int i)
            {
                if (r.IsDBNull(i)) return false;
                try { return Convert.ToInt64(r.GetValue(i)) != 0; } catch { return false; }
            }

            lijst.Add(new Samenstellingregel
            {
                Id = r.GetInt32(0),
                ArtikelId = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1)),
                Aantal = r.IsDBNull(2) ? 0 : Convert.ToDouble(r.GetValue(2)),
                Code = r.IsDBNull(3) ? "" : r.GetString(3),
                Omschrijving = r.IsDBNull(4) ? "" : r.GetString(4),
                Opmerking = r.IsDBNull(5) ? "" : r.GetString(5),
                Kostprijs = r.IsDBNull(6) ? null : Convert.ToDecimal(r.GetValue(6)),
                IsSamenstelling = B(7),
            });
        }
        return lijst;
    }

    // ============================================================
    //  Stuklijst wijzigen
    // ============================================================
    public async Task<int> BewaarSamenstellingregelAsync(int parentId, Samenstellingregel regel)
    {
        const string velden = "`ArtikelParentID` = @parent, `ArtikelID` = @artikel, " +
                              "`Aantal` = @aantal, `Opmerking` = @opm";

        var sql = regel.Id == 0
            ? $"INSERT INTO `ArtikelSamenstelling` SET {velden}"
            : $"UPDATE `ArtikelSamenstelling` SET {velden} WHERE `ArtikelSamenstellingID` = @id";

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@parent", parentId);
        cmd.Parameters.AddWithValue("@artikel", regel.ArtikelId);
        cmd.Parameters.AddWithValue("@aantal", regel.Aantal);
        cmd.Parameters.AddWithValue("@opm", (regel.Opmerking ?? "").Length > 250
            ? regel.Opmerking![..250] : regel.Opmerking ?? "");
        if (regel.Id != 0) cmd.Parameters.AddWithValue("@id", regel.Id);

        await cmd.ExecuteNonQueryAsync();
        return regel.Id == 0 ? (int)cmd.LastInsertedId : regel.Id;
    }

    public async Task VerwijderSamenstellingregelAsync(int regelId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `ArtikelSamenstelling` WHERE `ArtikelSamenstellingID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", regelId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetSamenstellingAsync(int artikelId, bool aan)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Artikelen` SET `Samenstelling` = @aan WHERE `ArtikelID` = @id", cn);
        cmd.Parameters.AddWithValue("@aan", aan ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", artikelId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inkoopprijs vastleggen en de verkoopprijs meteen meeschrijven:
    /// verkoop = inkoop × (1 + toeslag/100). Met een toeslag van 43 wordt
    /// € 1.413,20 dus € 2.020,88 — zoals het nu in Access ook uitkomt.
    /// </summary>
    public async Task ZetKostprijsAsync(int artikelId, decimal kostprijs, decimal toeslagProcent)
    {
        var verkoop = Math.Round(kostprijs * (1 + toeslagProcent / 100m), 2);

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Artikelen` SET `VastgesteldeKostprijs` = @kost, " +
            "                       `Verkoopprijs project` = @verkoop " +
            " WHERE `ArtikelID` = @id", cn);
        cmd.Parameters.AddWithValue("@kost", kostprijs);
        cmd.Parameters.AddWithValue("@verkoop", verkoop);
        cmd.Parameters.AddWithValue("@id", artikelId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Artikel bewaren
    //
    //  Alleen de velden die het scherm laat bewerken. De kolommen die de
    //  database zelf bijhoudt (aantal in voorraad, uitgeleend) blijven met rust.
    // ============================================================
    public async Task BewaarArtikelAsync(Artikel artikel, int? doorWerknemerId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `Artikelen` SET " +
            "  `ArtikelCodeMDE` = @code, `ArtikelCodeFactory` = @fab, `BrandID` = @merk, " +
            "  `OmschrijvingNL` = @nl, `OmschrijvingEN` = @en, `OmschrijvingFR` = @fr, " +
            "  `Opmerking` = @opm, `EANcode` = @ean, `OCcodeOmron` = @oc, " +
            "  `Samenstelling` = @samen, `Stockpart` = @stock, `Standaard` = @std, " +
            "  `isProduct` = @prod, `WebshopArtikelYN` = @web, `Uitleenbaar` = @leen, " +
            "  `CheckNodig` = @check, " +
            "  `SchattingsKostprijsStuk` = @schat, `Verkoopprijs webshop` = @webprijs, " +
            "  `MinimumVoorraad` = @min, `BestelHoeveelheid` = @best, `Gewicht` = @gew, " +
            "  `Material` = @mat, `PlaatdikteInMm` = @dikte, `InventorInkoopDeel` = @inv, " +
            "  `DatumLaatsteAanpassing` = @nu, `AangepastDoor` = @door " +
            " WHERE `ArtikelID` = @id", cn);

        // Een leeg veld hoort NULL te blijven. Zou het als lege tekst worden
        // weggeschreven, dan verandert elke opslag stilletjes alle lege kolommen
        // en kloppen zoekopdrachten op 'is leeg' in Access niet meer.
        static object Los(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? DBNull.Value : tekst.Trim();

        cmd.Parameters.AddWithValue("@code", Los(artikel.Code));
        cmd.Parameters.AddWithValue("@fab", Los(artikel.CodeFabrikant));
        cmd.Parameters.AddWithValue("@merk", (object?)artikel.MerkId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nl", Los(artikel.OmschrijvingNl));
        cmd.Parameters.AddWithValue("@en", Los(artikel.OmschrijvingEn));
        cmd.Parameters.AddWithValue("@fr", Los(artikel.OmschrijvingFr));
        cmd.Parameters.AddWithValue("@opm", Los(artikel.Opmerking));
        cmd.Parameters.AddWithValue("@ean", Los(artikel.Ean));
        cmd.Parameters.AddWithValue("@oc", Los(artikel.OcCodeOmron));
        cmd.Parameters.AddWithValue("@samen", artikel.Samenstelling ? 1 : 0);
        cmd.Parameters.AddWithValue("@stock", artikel.Voorraadartikel ? 1 : 0);
        cmd.Parameters.AddWithValue("@std", artikel.Standaard ? 1 : 0);
        cmd.Parameters.AddWithValue("@prod", artikel.IsProduct ? 1 : 0);
        cmd.Parameters.AddWithValue("@web", artikel.Webshop ? 1 : 0);
        cmd.Parameters.AddWithValue("@leen", artikel.Uitleenbaar ? 1 : 0);
        cmd.Parameters.AddWithValue("@check", artikel.CheckNodig ? 1 : 0);
        cmd.Parameters.AddWithValue("@schat", (object?)artikel.Schattingsprijs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@webprijs", (object?)artikel.VerkoopprijsWebshop ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@min", (object?)artikel.MinimumVoorraad ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@best", (object?)artikel.Bestelhoeveelheid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gew", (object?)artikel.Gewicht ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mat", Los(artikel.Materiaal));
        cmd.Parameters.AddWithValue("@dikte", (object?)artikel.PlaatdikteMm ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@inv", artikel.InventorInkoopdeel ? 1 : 0);
        cmd.Parameters.AddWithValue("@nu", DateTime.Now);
        cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", artikel.Id);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetGroepAsync(int artikelId, int groepId, bool aan)
    {
        if (groepId <= 0) return;

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(aan
            // dubbel koppelen heeft geen zin, dus eerst kijken of het er al staat
            ? "INSERT INTO `ArtikelGroepenLink` (`ArtikelID`, `GroupID`) " +
              "SELECT @a, @g FROM DUAL WHERE NOT EXISTS " +
              "  (SELECT 1 FROM `ArtikelGroepenLink` WHERE `ArtikelID` = @a AND `GroupID` = @g)"
            : "DELETE FROM `ArtikelGroepenLink` WHERE `ArtikelID` = @a AND `GroupID` = @g", cn);
        cmd.Parameters.AddWithValue("@a", artikelId);
        cmd.Parameters.AddWithValue("@g", groepId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<(int Id, string Naam)>> GetMerkenAsync()
    {
        var lijst = new List<(int, string)>();
        var merk = await MerkTabelAsync();
        if (merk is null) return lijst;

        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                $"SELECT `{merk.Value.Id}`, COALESCE(`{merk.Value.Naam}`, '') " +
                $"  FROM `{merk.Value.Tabel}` ORDER BY `{merk.Value.Naam}`", cn);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                lijst.Add((Convert.ToInt32(r.GetValue(0)), r.IsDBNull(1) ? "" : r.GetString(1)));
        }
        catch { }
        return lijst;
    }

    // ============================================================
    //  Kostprijs doorrekenen
    // ============================================================
    public Task<decimal> BerekendeKostprijsAsync(int artikelId)
        => RekenAsync(artikelId, new HashSet<int>(), 0);

    /// <summary>
    /// Som van de onderdelen, en van hún onderdelen. Heeft een artikel geen
    /// stuklijst, dan telt zijn eigen vastgestelde kostprijs.
    ///
    /// 'gezien' voorkomt dat een stuklijst die zichzelf bevat eindeloos doorloopt.
    /// </summary>
    private async Task<decimal> RekenAsync(int artikelId, HashSet<int> gezien, int diepte)
    {
        if (diepte > 12 || !gezien.Add(artikelId)) return 0;

        try
        {
            var onderdelen = await LeesSamenstellingAsync(artikelId);
            if (onderdelen.Count == 0) return await EigenKostprijsAsync(artikelId);

            decimal som = 0;
            foreach (var onderdeel in onderdelen)
            {
                var stukprijs = onderdeel.IsSamenstelling
                    ? await RekenAsync(onderdeel.ArtikelId, gezien, diepte + 1)
                    : onderdeel.Kostprijs ?? 0;

                som += (decimal)onderdeel.Aantal * stukprijs;
            }
            return som;
        }
        finally { gezien.Remove(artikelId); }
    }

    private async Task<decimal> EigenKostprijsAsync(int artikelId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `VastgesteldeKostprijs` FROM `Artikelen` WHERE `ArtikelID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", artikelId);

        var res = await cmd.ExecuteScalarAsync();
        return res is null or DBNull ? 0 : Convert.ToDecimal(res);
    }

    /// <summary>Aliassen; de tabelnaam is niet zeker, dus een paar varianten proberen.</summary>
    public async Task<List<(string Alias, string Bron)>> GetAliassenAsync(int artikelId)
    {
        foreach (var tabel in new[] { "ArtikelAlias", "ArtikelAliassen", "Artikel_Alias", "Aliassen" })
        {
            var lijst = new List<(string, string)>();
            try
            {
                await using var cn = await OpenAsync();
                await using var cmd = new MySqlCommand(
                    $"SELECT * FROM `{tabel}` WHERE `ArtikelID` = @id", cn);
                cmd.Parameters.AddWithValue("@id", artikelId);

                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    string? alias = null, bron = null;
                    for (var i = 0; i < r.FieldCount; i++)
                    {
                        var kolom = r.GetName(i);
                        if (alias is null && kolom.Contains("alias", StringComparison.OrdinalIgnoreCase)
                            && !kolom.Contains("id", StringComparison.OrdinalIgnoreCase) && !r.IsDBNull(i))
                            alias = r.GetValue(i).ToString();
                        if (bron is null && (kolom.Contains("bron", StringComparison.OrdinalIgnoreCase)
                                          || kolom.Contains("source", StringComparison.OrdinalIgnoreCase))
                            && !r.IsDBNull(i)) bron = r.GetValue(i).ToString();
                    }
                    if (alias is not null) lijst.Add((alias, bron ?? ""));
                }
                return lijst;
            }
            catch { /* volgende tabelnaam */ }
        }
        return new List<(string, string)>();
    }
}
