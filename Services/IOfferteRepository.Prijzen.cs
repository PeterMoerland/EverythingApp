using EverythingApp.Models;
using MySqlConnector;

namespace EverythingApp.Services;

// Fase 3 van Offertes: artikelen kiezen en de prijsberekening (Access: ZoekArtikel met ParentForm "Offerte",
// OfferteMateriaalSpecialeprijs en -NoMDE, Global.bas GetVerkoopPrijs, query Artikel_MaxInkoopprijs).

public partial interface IOfferteRepository
{
    /// <summary>Inkoop (VastgesteldeKostprijs) en verkoopprijs project van een reeks artikelen, voor de zoeker.</summary>
    Task<Dictionary<int, (decimal Inkoop, decimal Verkoop)>> ArtikelPrijzenAsync(IEnumerable<int> artikelIds);

    /// <summary>Wat de prijsdialoog nodig heeft voor een artikel: duurste leverancier, bruto, korting, inkoop, vaste prijs.</summary>
    Task<OffertePrijsInfo?> PrijsInfoAsync(int artikelId);

    /// <summary>De kortingen die met deze klant zijn afgesproken (RelationKorting).</summary>
    Task<List<(decimal Korting, string Omschrijving)>> KlantkortingenAsync(int klantId);

    /// <summary>General_settings.ToeslagInkoopVerkoop als fractie (43 → 0,43).</summary>
    Task<decimal> StandaardOpslagAsync();
}

public partial class GeenOfferteRepository
{
    public Task<Dictionary<int, (decimal Inkoop, decimal Verkoop)>> ArtikelPrijzenAsync(IEnumerable<int> artikelIds)
        => Task.FromResult(new Dictionary<int, (decimal, decimal)>());
    public Task<OffertePrijsInfo?> PrijsInfoAsync(int artikelId) => Task.FromResult<OffertePrijsInfo?>(null);
    public Task<List<(decimal Korting, string Omschrijving)>> KlantkortingenAsync(int klantId) => Task.FromResult(new List<(decimal, string)>());
    public Task<decimal> StandaardOpslagAsync() => Task.FromResult(0.43m);
}

public partial class MySqlOfferteRepository
{
    public async Task<Dictionary<int, (decimal Inkoop, decimal Verkoop)>> ArtikelPrijzenAsync(IEnumerable<int> artikelIds)
    {
        var ids = artikelIds.Distinct().ToList();
        var uit = new Dictionary<int, (decimal, decimal)>();
        if (ids.Count == 0) return uit;
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `ArtikelID`, COALESCE(`VastgesteldeKostprijs`, 0), COALESCE(`Verkoopprijs project`, 0) FROM `Artikelen` " +
            $" WHERE `ArtikelID` IN ({string.Join(",", ids)})", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) uit[r.GetInt32(0)] = (M(r, 1), M(r, 2));
        return uit;
    }

    public async Task<OffertePrijsInfo?> PrijsInfoAsync(int artikelId)
    {
        await using var cn = await OpenAsync();
        OffertePrijsInfo? info;
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(`ArtikelCodeMDE`, ''), COALESCE(`OmschrijvingNL`, ''), COALESCE(`VastgesteldeKostprijs`, 0), " +
            "       COALESCE(`Verkoopprijs project`, 0) FROM `Artikelen` WHERE `ArtikelID` = @a", cn))
        {
            cmd.Parameters.AddWithValue("@a", artikelId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            info = new OffertePrijsInfo { ArtikelId = artikelId, Code = S(r, 0), Omschrijving = S(r, 1), Kostprijs = M(r, 2), VastePrijs = M(r, 3) };
        }

        // Access-query Artikel_MaxInkoopprijs: inkoop = PrijsPerVE × (1 − korting) / StuksPerVE, afgekapt op 3 decimalen,
        // leverancier 0 niet; de duurste leverancier telt.
        await using (var cmd = new MySqlCommand(
            "SELECT COALESCE(r.`name`, CONCAT('#', s.`SupplierID`)), COALESCE(s.`PrijsPerVE`, 0), " +
            "       IF(COALESCE(s.`StuksPerVE`, 0) = 0, 1, s.`StuksPerVE`), COALESCE(s.`Korting`, 0), " +
            "       TRUNCATE(COALESCE(s.`PrijsPerVE`, 0) * (1 - COALESCE(s.`Korting`, 0)) / IF(COALESCE(s.`StuksPerVE`, 0) = 0, 1, s.`StuksPerVE`), 3) AS inkoop " +
            "  FROM `ArtikelSupplier` s LEFT JOIN `Relations` r ON r.`id_supplier` = s.`SupplierID` " +
            " WHERE s.`ArtikelID` = @a AND COALESCE(s.`SupplierID`, 0) <> 0 ORDER BY inkoop DESC LIMIT 1", cn))
        {
            cmd.Parameters.AddWithValue("@a", artikelId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                info.Leverancier = S(r, 0);
                info.Bruto = M(r, 1) / M(r, 2);
                info.KortingLeverancier = M(r, 3);
                info.Inkoop = info.Bruto * (1 - info.KortingLeverancier);
            }
        }
        return info;
    }

    public async Task<List<(decimal Korting, string Omschrijving)>> KlantkortingenAsync(int klantId)
    {
        var lijst = new List<(decimal, string)>();
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(`Korting`, 0), COALESCE(`Omschrijving`, '') FROM `RelationKorting` WHERE `RelationID` = @k ORDER BY `Korting`", cn);
        cmd.Parameters.AddWithValue("@k", klantId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) lijst.Add((M(r, 0), S(r, 1)));
        return lijst;
    }

    public async Task<decimal> StandaardOpslagAsync()
    {
        await using var cn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT `ToeslagInkoopVerkoop` FROM `General_settings` LIMIT 1", cn);
        var res = await cmd.ExecuteScalarAsync();
        return res is null or DBNull ? 0.43m : Convert.ToDecimal(res) / 100;
    }
}
