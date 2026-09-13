using MySqlConnector;

namespace EverythingApp.Services;

/// <summary>
/// Eén factuur die klaarstaat om naar Basecone gemaild te worden. Inkomend
/// ([INK], uit FacturenInkomend) of verkoop ([VRK], uit Invoices) — zelfde
/// vorm, want de twee databaseviews leveren dezelfde kolommen.
/// </summary>
public sealed class BaseconeRegel
{
    /// <summary>"INK" of "VRK"; in Access de PrefixSubject.</summary>
    public string Soort { get; set; } = "INK";

    /// <summary>FacturenInkomend-ID of Invoice-id, afhankelijk van de soort.</summary>
    public int BronId { get; set; }

    /// <summary>Leverancier (inkomend) of klant (verkoop).</summary>
    public string Naam { get; set; } = "";

    public string Factuurnummer { get; set; } = "";
    public DateTime? Factuurdatum { get; set; }
    public decimal Bedrag { get; set; }
    public string BvNaam { get; set; } = "";

    /// <summary>De werkmaatschappij (Relations-id); bepaalt het Basecone-adres.</summary>
    public int? AdminId { get; set; }

    /// <summary>Het hoofddocument volgens de view; leeg = geen bestand, dan kan
    /// er niets gemaild worden (Access toonde dan 'Ontbreekt').</summary>
    public int? FileStoreId { get; set; }

    /// <summary>Zoals Access hem opbouwde: "Invoice: nummer".</summary>
    public string Omschrijving => Factuurnummer.Length > 0 ? "Invoice: " + Factuurnummer : "";

    /// <summary>Alleen voor het scherm: aangevinkt om te versturen.</summary>
    public bool Geselecteerd { get; set; }
}

/// <summary>
/// De kandidaten voor Basecone. Welke facturen klaarstaan bepalen twee views
/// in de database — dezelfde die Access gebruikte: @BaseconeKlaarzettenInkomend
/// en @BaseconeKlaarzettenUitgaand. Zo blijven de criteria op één plek staan.
/// </summary>
public interface IBaseconeRepository
{
    bool Beschikbaar { get; }

    /// <summary>De kandidaten uit beide views. Ontbreekt een view in de
    /// database, dan komt daarover een melding terug en levert de andere
    /// gewoon zijn regels.</summary>
    Task<(List<BaseconeRegel> Regels, List<string> Fouten)> GetKandidatenAsync();

    /// <summary>Het Basecone-mailadres van een werkmaatschappij
    /// (Relations.BaseconeEmailAdres). Leeg = niet ingesteld.</summary>
    Task<string> GetBaseconeAdresAsync(int adminId);

    /// <summary>Zet de vlag op een verkóópfactuur (Invoices) na verzending.
    /// Inkomende facturen gaan via IFactuurRepository.ZetVlagAsync, die de
    /// kolomnamen daar al kent.</summary>
    Task ZetVerzondenUitgaandAsync(int invoiceId, int? doorWerknemerId);
}

/// <summary>Doet niets — demo-modus zonder live database.</summary>
public class GeenBaseconeRepository : IBaseconeRepository
{
    public bool Beschikbaar => false;
    public Task<(List<BaseconeRegel>, List<string>)> GetKandidatenAsync()
        => Task.FromResult((new List<BaseconeRegel>(), new List<string>()));
    public Task<string> GetBaseconeAdresAsync(int adminId) => Task.FromResult("");
    public Task ZetVerzondenUitgaandAsync(int invoiceId, int? door) => Task.CompletedTask;
}

public class MySqlBaseconeRepository : IBaseconeRepository
{
    private readonly string _cs;

    public MySqlBaseconeRepository(string connectionString) => _cs = connectionString;

    public bool Beschikbaar => true;

    private async Task<MySqlConnection> OpenAsync()
    {
        var cn = new MySqlConnection(_cs);
        await cn.OpenAsync();
        return cn;
    }

    public async Task<(List<BaseconeRegel> Regels, List<string> Fouten)> GetKandidatenAsync()
    {
        var regels = new List<BaseconeRegel>();
        var fouten = new List<string>();

        await using var cn = await OpenAsync();

        // Beide views leveren dezelfde kolommen; ook de uitgaande noemt zijn
        // sleutel 'FacturenInkomend-ID' (een alias voor Invoice-id) — zo deed
        // Access het ook.
        foreach (var (view, soort) in new[]
        {
            ("@BaseconeKlaarzettenInkomend", "INK"),
            ("@BaseconeKlaarzettenUitgaand", "VRK"),
        })
        {
            try
            {
                await using var cmd = new MySqlCommand(
                    "SELECT `FacturenInkomend-ID`, COALESCE(`name`, ''), " +
                    "       COALESCE(`FactuurNummer`, ''), `DatumFactuur`, " +
                    "       COALESCE(`BedragExBTW`, 0), `Admin`, " +
                    "       COALESCE(`BVnaam`, ''), `FileStoreID` " +
                    $"  FROM `{view}` " +
                    " ORDER BY `name`, `FactuurNummer`", cn);

                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    regels.Add(new BaseconeRegel
                    {
                        Soort = soort,
                        BronId = r.GetInt32(0),
                        Naam = r.GetString(1),
                        Factuurnummer = r.GetString(2),
                        Factuurdatum = r.IsDBNull(3) ? null : r.GetDateTime(3),
                        Bedrag = r.IsDBNull(4) ? 0 : Math.Round(Convert.ToDecimal(r.GetValue(4)), 2),
                        AdminId = r.IsDBNull(5) ? null : Convert.ToInt32(r.GetValue(5)),
                        BvNaam = r.GetString(6),
                        FileStoreId = r.IsDBNull(7) ? null : Convert.ToInt32(r.GetValue(7)),
                    });
            }
            catch (Exception ex)
            {
                // De view bestaat (nog) niet in deze database. De criteria horen
                // in de view te blijven — dat is de ene plek die Access én de
                // app delen — dus dit lossen we niet met een eigen query op.
                fouten.Add($"De view {view} is niet te lezen ({ex.Message}). " +
                           "Zet hem over uit de oorspronkelijke database; daar staan de selectiecriteria in.");
            }
        }

        return (regels, fouten);
    }

    public async Task<string> GetBaseconeAdresAsync(int adminId)
    {
        try
        {
            await using var cn = await OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT COALESCE(`BaseconeEmailAdres`, '') FROM `Relations` " +
                " WHERE `id_supplier` = @id", cn);
            cmd.Parameters.AddWithValue("@id", adminId);
            return (await cmd.ExecuteScalarAsync())?.ToString()?.Trim() ?? "";
        }
        catch { return ""; }
    }

    public async Task ZetVerzondenUitgaandAsync(int invoiceId, int? doorWerknemerId)
    {
        await using var cn = await OpenAsync();
        // Kolomnamen exact zoals de VBA ze schreef (Form_BaseconeVerzenden):
        // VerzondenBasecone, VerzondenBaseconeDoor, VerzondenBaseconeOp.
        await using var cmd = new MySqlCommand(
            "UPDATE `Invoices` SET `VerzondenBasecone` = 1, " +
            "       `VerzondenBaseconeDoor` = @door, `VerzondenBaseconeOp` = @vandaag " +
            " WHERE `Invoice-id` = @id", cn);
        cmd.Parameters.AddWithValue("@door", (object?)doorWerknemerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vandaag", DateTime.Today);
        cmd.Parameters.AddWithValue("@id", invoiceId);
        await cmd.ExecuteNonQueryAsync();
    }
}
