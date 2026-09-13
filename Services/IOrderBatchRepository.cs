using MySqlConnector;
using EverythingApp.Models;

namespace EverythingApp.Services;

public interface IOrderBatchRepository
{
    bool Beschikbaar { get; }

    /// <summary>De batch, eventueel gefilterd op leverancier en/of werkorder.</summary>
    Task<List<OrderBatchRegel>> GetRegelsAsync(int? leverancierId, int? orderId);

    /// <summary>Het artikel zoals de scanner het nodig heeft, of null als het niet bestaat.</summary>
    Task<ScanArtikel?> GetScanArtikelAsync(int artikelId);

    /// <summary>
    /// Zet een artikel in de batch. Een samenstelling wordt — net als in Access —
    /// uiteengelegd in zijn onderdelen, elk met zijn eigen voorkeursleverancier.
    /// Geeft terug hoeveel regels er zijn toegevoegd.
    /// </summary>
    Task<int> VoegToeAsync(int artikelId, int aantal, int orderId, int? doorWerknemerId,
                           bool viaScanner, string opmerking);

    Task ZetAantalAsync(int orderBatchId, int aantal);
    Task ZetOpmerkingAsync(int orderBatchId, string opmerking);
    Task VerwijderAsync(int orderBatchId);
    Task VerwijderAsync(IEnumerable<int> orderBatchIds);
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenOrderBatchRepository : IOrderBatchRepository
{
    public bool Beschikbaar => false;
    public Task<List<OrderBatchRegel>> GetRegelsAsync(int? leverancierId, int? orderId)
        => Task.FromResult(new List<OrderBatchRegel>());
    public Task<ScanArtikel?> GetScanArtikelAsync(int artikelId) => Task.FromResult<ScanArtikel?>(null);
    public Task<int> VoegToeAsync(int artikelId, int aantal, int orderId, int? doorWerknemerId,
                                  bool viaScanner, string opmerking) => Task.FromResult(0);
    public Task ZetAantalAsync(int orderBatchId, int aantal) => Task.CompletedTask;
    public Task ZetOpmerkingAsync(int orderBatchId, string opmerking) => Task.CompletedTask;
    public Task VerwijderAsync(int orderBatchId) => Task.CompletedTask;
    public Task VerwijderAsync(IEnumerable<int> orderBatchIds) => Task.CompletedTask;
}

/// <summary>
/// De orderbatch uit de tabel OrderBatch: het wachtlijstje van alles wat besteld
/// moet worden, tot er inkooporders van worden gemaakt.
/// </summary>
public class MySqlOrderBatchRepository : IOrderBatchRepository
{
    private readonly string _cs;

    public MySqlOrderBatchRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    // ============================================================
    //  Lezen
    // ============================================================
    public async Task<List<OrderBatchRegel>> GetRegelsAsync(int? leverancierId, int? orderId)
    {
        var lijst = new List<OrderBatchRegel>();

        var waar = new List<string>();
        if (leverancierId is not null) waar.Add("b.`Supplier_ID` = @lev");
        if (orderId is not null) waar.Add("b.`Order_Id` = @order");

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT b.`OrderBatch_ID`, b.`Aantal`, b.`Artikel_ID`, b.`Supplier_ID`, b.`Order_Id`, " +
            "       COALESCE(b.`Opmerking`, ''), b.`ToegevoegdOp`, COALESCE(b.`ViaScanner`, 0), " +
            "       COALESCE(a.`ArtikelCodeMDE`, ''), COALESCE(a.`OmschrijvingNL`, ''), " +
            "       a.`VastgesteldeKostprijs`, " +
            "       COALESCE(s.`name`, ''), COALESCE(w.`NaamVoluit`, '') " +
            "  FROM `OrderBatch` b " +
            "  LEFT JOIN `Artikelen` a ON a.`ArtikelID` = b.`Artikel_ID` " +
            "  LEFT JOIN `Relations` s ON s.`id_supplier` = b.`Supplier_ID` " +
            "  LEFT JOIN `Werknemers` w ON w.`Werknemer-id` = b.`ToegevoegdDoor` " +
            (waar.Count > 0 ? " WHERE " + string.Join(" AND ", waar) : "") +
            " ORDER BY b.`OrderBatch_ID` DESC", cn);

        if (leverancierId is not null) cmd.Parameters.AddWithValue("@lev", leverancierId.Value);
        if (orderId is not null) cmd.Parameters.AddWithValue("@order", orderId.Value);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string S(int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
            int? I(int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));

            var regel = new OrderBatchRegel
            {
                Id = r.GetInt32(0),
                Aantal = I(1) ?? 0,
                ArtikelId = I(2) ?? 0,
                LeverancierId = I(3),
                OrderId = I(4),
                Opmerking = S(5),
                ToegevoegdOp = r.IsDBNull(6) ? null : Convert.ToDateTime(r.GetValue(6)),
                ViaScanner = !r.IsDBNull(7) && Convert.ToInt64(r.GetValue(7)) != 0,
                Code = S(8),
                Omschrijving = S(9),
                StuksPrijs = r.IsDBNull(10) ? null : Convert.ToDecimal(r.GetValue(10)),
                Leverancier = S(11),
                ToegevoegdDoor = S(12),
            };

            // Het werkordernummer wordt door het scherm ingevuld via de bestaande
            // werkorderquery; de kolomnamen daarvan verschillen per database en
            // die kennis hoort op één plek te blijven.
            lijst.Add(regel);
        }
        return lijst;
    }

    // ============================================================
    //  Het artikel voor de scanner
    // ============================================================
    public async Task<ScanArtikel?> GetScanArtikelAsync(int artikelId)
    {
        await using var cn = await OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(a.`ArtikelCodeMDE`, ''), COALESCE(a.`OmschrijvingNL`, ''), " +
            "       COALESCE(a.`Samenstelling`, 0), a.`AantalInVoorraad`, a.`MinimumVoorraad` " +
            "  FROM `Artikelen` a WHERE a.`ArtikelID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", artikelId);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        var artikel = new ScanArtikel
        {
            Id = artikelId,
            Code = r.IsDBNull(0) ? "" : r.GetString(0),
            Omschrijving = r.IsDBNull(1) ? "" : r.GetString(1),
            Samenstelling = !r.IsDBNull(2) && Convert.ToInt64(r.GetValue(2)) != 0,
            Voorraad = r.IsDBNull(3) ? null : Convert.ToDouble(r.GetValue(3)),
            MinimumVoorraad = r.IsDBNull(4) ? null : Convert.ToDouble(r.GetValue(4)),
        };
        await r.CloseAsync();

        var (leverancierId, perVerpakking) = await VoorkeursleverancierAsync(cn, artikelId);
        artikel.LeverancierId = leverancierId;
        artikel.PerVerpakking = perVerpakking;

        if (leverancierId is not null)
        {
            await using var naam = new MySqlCommand(
                "SELECT COALESCE(`name`, '') FROM `Relations` WHERE `id_supplier` = @id", cn);
            naam.Parameters.AddWithValue("@id", leverancierId.Value);
            artikel.Leverancier = (await naam.ExecuteScalarAsync())?.ToString() ?? "";
        }

        return artikel;
    }

    /// <summary>
    /// De leverancier waar dit artikel hoort te worden besteld, met de
    /// verpakkingsgrootte. Zelfde spelregels als GetPreferedSuppier in Access:
    /// de voorkeursleverancier wint; is die er niet, dan de leverancier met de
    /// kleinste verpakking; is er helemaal niets, dan per stuk.
    /// </summary>
    private static async Task<(int? LeverancierId, int PerVerpakking)> VoorkeursleverancierAsync(
        MySqlConnection cn, int artikelId)
    {
        await using var cmd = new MySqlCommand(
            "SELECT `SupplierID`, COALESCE(`StuksPerVE`, 1), COALESCE(`PreferedSupplier`, 0) " +
            "  FROM `ArtikelSupplier` WHERE `ArtikelID` = @id " +
            " ORDER BY COALESCE(`PreferedSupplier`, 0) DESC, COALESCE(`StuksPerVE`, 1) ASC " +
            " LIMIT 1", cn);
        cmd.Parameters.AddWithValue("@id", artikelId);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return (null, 1);

        var leverancier = r.IsDBNull(0) ? (int?)null : Convert.ToInt32(r.GetValue(0));
        var per = r.IsDBNull(1) ? 1 : Convert.ToInt32(r.GetValue(1));
        return (leverancier, Math.Max(1, per));
    }

    // ============================================================
    //  Toevoegen
    // ============================================================
    public async Task<int> VoegToeAsync(int artikelId, int aantal, int orderId, int? doorWerknemerId,
                                        bool viaScanner, string opmerking)
    {
        if (aantal <= 0) return 0;

        await using var cn = await OpenAsync();

        // Een samenstelling ga je niet bestellen; zijn onderdelen wel. Net als in
        // Access komt elk onderdeel als eigen regel in de batch, met het aantal
        // vermenigvuldigd en zijn eigen leverancier.
        var isSamenstelling = false;
        await using (var kijk = new MySqlCommand(
            "SELECT COALESCE(`Samenstelling`, 0) FROM `Artikelen` WHERE `ArtikelID` = @id", cn))
        {
            kijk.Parameters.AddWithValue("@id", artikelId);
            var v = await kijk.ExecuteScalarAsync();
            isSamenstelling = v is not null && v is not DBNull && Convert.ToInt64(v) != 0;
        }

        if (!isSamenstelling)
        {
            // Afronden op hele verpakkingen doet Access alleen bij het hoofdartikel;
            // onderdelen van een samenstelling gaan onafgerond de batch in.
            var (_, per) = await VoorkeursleverancierAsync(cn, artikelId);
            if (per > 1) aantal = (aantal + per - 1) / per * per;

            await VoegRegelToeAsync(cn, artikelId, aantal, orderId, doorWerknemerId, viaScanner, opmerking);
            return 1;
        }

        var onderdelen = new List<(int ArtikelId, double Aantal)>();
        await using (var lees = new MySqlCommand(
            "SELECT `ArtikelID`, COALESCE(`Aantal`, 1) FROM `ArtikelSamenstelling` " +
            " WHERE `ArtikelParentID` = @id AND `ArtikelID` IS NOT NULL AND `ArtikelID` <> 0", cn))
        {
            lees.Parameters.AddWithValue("@id", artikelId);
            await using var r = await lees.ExecuteReaderAsync();
            while (await r.ReadAsync())
                onderdelen.Add((r.GetInt32(0), r.IsDBNull(1) ? 1 : Convert.ToDouble(r.GetValue(1))));
        }

        var toegevoegd = 0;
        foreach (var (onderdeelId, perStuk) in onderdelen)
        {
            // Naar boven op hele stuks: de kolom Aantal is een geheel getal, en
            // 2,5 meter kabel bestellen als 2 zou te weinig zijn.
            var nodig = (int)Math.Ceiling(perStuk * aantal);
            if (nodig <= 0) continue;

            await VoegRegelToeAsync(cn, onderdeelId, nodig, orderId, doorWerknemerId, viaScanner, opmerking);
            toegevoegd++;
        }
        return toegevoegd;
    }

    /// <summary>
    /// Zet een regel in de batch. Staat hetzelfde artikel er voor dezelfde
    /// leverancier en werkorder al in, dan wordt — net als in Access — het
    /// aantal opgehoogd in plaats van dat er een tweede regel bijkomt. Twee keer
    /// scannen is dus gewoon twee keer bestellen.
    /// </summary>
    private static async Task VoegRegelToeAsync(MySqlConnection cn, int artikelId, int aantal,
                                                int orderId, int? doorWerknemerId, bool viaScanner,
                                                string opmerking)
    {
        var (leverancierId, _) = await VoorkeursleverancierAsync(cn, artikelId);

        await using (var ophogen = new MySqlCommand(
            "UPDATE `OrderBatch` SET `Aantal` = `Aantal` + @aantal " +
            " WHERE `Artikel_ID` = @artikel AND `Order_Id` = @order " +
            "   AND (`Supplier_ID` = @leverancier OR (@leverancier IS NULL AND `Supplier_ID` IS NULL)) " +
            " LIMIT 1", cn))
        {
            ophogen.Parameters.AddWithValue("@aantal", aantal);
            ophogen.Parameters.AddWithValue("@artikel", artikelId);
            ophogen.Parameters.AddWithValue("@order", orderId);
            ophogen.Parameters.AddWithValue("@leverancier", (object?)leverancierId ?? DBNull.Value);

            if (await ophogen.ExecuteNonQueryAsync() > 0) return;
        }

        await using var cmd = new MySqlCommand(
            "INSERT INTO `OrderBatch` " +
            " (`Aantal`, `Artikel_ID`, `Supplier_ID`, `Order_Id`, `Opmerking`, " +
            "  `ToegevoegdDoor`, `ToegevoegdOp`, `ViaScanner`) " +
            "VALUES (@aantal, @artikel, @leverancier, @order, @opmerking, @door, @nu, @scanner)", cn);

        cmd.Parameters.AddWithValue("@aantal", aantal);
        cmd.Parameters.AddWithValue("@artikel", artikelId);
        cmd.Parameters.AddWithValue("@leverancier", (object?)leverancierId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@order", orderId);
        cmd.Parameters.AddWithValue("@opmerking", opmerking.Length > 200 ? opmerking[..200] : opmerking);
        cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nu", DateTime.Now);
        cmd.Parameters.AddWithValue("@scanner", viaScanner ? 1 : 0);

        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    //  Wijzigen en verwijderen
    // ============================================================
    public async Task ZetAantalAsync(int orderBatchId, int aantal)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `OrderBatch` SET `Aantal` = @a WHERE `OrderBatch_ID` = @id", cn);
        cmd.Parameters.AddWithValue("@a", Math.Max(1, aantal));
        cmd.Parameters.AddWithValue("@id", orderBatchId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ZetOpmerkingAsync(int orderBatchId, string opmerking)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE `OrderBatch` SET `Opmerking` = @o WHERE `OrderBatch_ID` = @id", cn);
        cmd.Parameters.AddWithValue("@o", opmerking.Length > 200 ? opmerking[..200] : opmerking);
        cmd.Parameters.AddWithValue("@id", orderBatchId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task VerwijderAsync(int orderBatchId)
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `OrderBatch` WHERE `OrderBatch_ID` = @id", cn);
        cmd.Parameters.AddWithValue("@id", orderBatchId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task VerwijderAsync(IEnumerable<int> orderBatchIds)
    {
        var ids = orderBatchIds.ToList();
        if (ids.Count == 0) return;

        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM `OrderBatch` WHERE `OrderBatch_ID` IN (" +
            string.Join(",", ids.Select((_, i) => "@i" + i)) + ")", cn);
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue("@i" + i, ids[i]);
        await cmd.ExecuteNonQueryAsync();
    }
}
