using System.Globalization;
using System.Text;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// Brengt de NDA-onderdelen samen: gegevens uit de database, het document uit het
/// sjabloon en de opslag in de filestore. Het scherm hoeft hier alleen nog
/// 'MaakVersieAsync' aan te roepen.
/// </summary>
public class NdaService
{
    /// <summary>StoreType in de filestore voor NDA-documenten (zie NDA_Schema_MariaDB.sql).</summary>
    public const int StoreType = 310;

    private readonly INdaRepository _repo;
    private readonly NdaDocumentService _docs;
    private readonly FilestoreService _store;
    private readonly ILogger<NdaService> _log;

    public NdaService(INdaRepository repo, NdaDocumentService docs,
                      FilestoreService store, ILogger<NdaService> log)
    {
        _repo = repo;
        _docs = docs;
        _store = store;
        _log = log;
    }

    public bool PdfMogelijk => _docs.PdfMogelijk;

    private static readonly CultureInfo Nl = new("nl-NL");

    /// <summary>
    /// Naam die veilig in een bestandsnaam past. Exact dezelfde regel als de oude
    /// Python-app gebruikte, zodat de mapnamen bij het importeren kloppen:
    /// alles wat geen letter, cijfer, liggend streepje of spatie is valt weg, en
    /// spaties worden onderstrepingstekens. 'Tembo / EME Engel' → 'Tembo_EME_Engel'.
    /// </summary>
    public static string VeiligeNaam(string naam)
    {
        var behouden = new StringBuilder();
        foreach (var teken in naam ?? "")
            if (char.IsLetterOrDigit(teken) || teken is '_' or '-' || char.IsWhiteSpace(teken))
                behouden.Append(teken);

        var tekst = System.Text.RegularExpressions.Regex
            .Replace(behouden.ToString().Trim(), @"\s+", "_");
        return tekst.Length == 0 ? "NDA" : tekst;
    }

    /// <summary>De plaatshouders uit het sjabloon, gevuld met de gegevens van deze NDA.</summary>
    private async Task<Dictionary<string, string>> VervangingenAsync(Nda nda)
    {
        var inst = await _repo.GetInstellingenAsync();
        string Inst(string sleutel, string standaard)
            => inst.TryGetValue(sleutel, out var v) && !string.IsNullOrWhiteSpace(v) ? v : standaard;

        var datum = (nda.DatumDocument ?? DateOnly.FromDateTime(DateTime.Today))
                    .ToDateTime(TimeOnly.MinValue)
                    .ToString("d MMMM yyyy", Nl);

        return new Dictionary<string, string>
        {
            ["[DATUM]"] = datum,
            ["[NAAM WEDERPARTIJ]"] = nda.RelatieNaam ?? "",
            ["[RECHTSVORM]"] = nda.Rechtsvorm ?? "",
            ["[TOEPASSELIJK RECHT, bijv. Nederlands]"] = string.IsNullOrWhiteSpace(nda.ToepasselijkRecht)
                                                          ? "Nederlands" : nda.ToepasselijkRecht,
            ["[ADRES WEDERPARTIJ]"] = nda.Adres ?? "",
            ["[KVK-NUMMER WEDERPARTIJ]"] = nda.KvK ?? "",
            ["[PROJECTOMSCHRIJVING]"] = nda.Projectomschrijving ?? "",
            ["[PROJECTNUMMER]"] = nda.Projectnummer ?? "",
            ["[10 (tien)]"] = Inst("retourverplichting", "10 (tien)"),
            ["[LOOPTIJD, bijv. vijf (5) jaar]"] = Inst("looptijd", "vijf (5)") + " jaar",
            ["[NAWERKINGSTERMIJN, bijv. vijf (5) jaar]"] = Inst("nawerkingstermijn", "vijf (5)") + " jaar",
        };
    }

    /// <summary>
    /// Maakt een nieuwe versie: Word-document uit het sjabloon, in de filestore, en
    /// als LibreOffice beschikbaar is ook een PDF. Levert het versienummer terug.
    /// </summary>
    public async Task<string> MaakVersieAsync(Nda nda, int? werknemerId, string notitie)
    {
        if (nda.Id == 0) throw new InvalidOperationException("Bewaar de NDA eerst.");
        if (!_store.Beschikbaar) throw new InvalidOperationException("De filestore is niet ingesteld.");

        var versie = "v" + (nda.HoogsteVersie + 1);
        var basis = $"NDA_{VeiligeNaam(nda.RelatieNaam)}_{versie}";

        var vervangingen = await VervangingenAsync(nda);
        var word = _docs.MaakWord(vervangingen);

        var wordId = await BewaarBestandAsync(word, basis + ".docx", nda.Id, werknemerId);
        await _repo.VoegBestandToeAsync(new NdaBestand
        {
            NdaId = nda.Id, Versie = versie, Soort = "word",
            FileStoreId = wordId, Datum = DateTime.Now,
            ToegevoegdDoor = werknemerId, Notitie = notitie,
        });

        // PDF is een gemak, geen noodzaak: mislukt hij, dan blijft het Word-bestand staan.
        try
        {
            var pdf = await _docs.NaarPdfAsync(word, basis + ".docx");
            if (pdf is not null)
            {
                var pdfId = await BewaarBestandAsync(pdf, basis + ".pdf", nda.Id, werknemerId);
                await _repo.VoegBestandToeAsync(new NdaBestand
                {
                    NdaId = nda.Id, Versie = versie, Soort = "pdf",
                    FileStoreId = pdfId, Datum = DateTime.Now,
                    ToegevoegdDoor = werknemerId, Notitie = notitie,
                });
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "PDF bij NDA {Id} maken mislukt", nda.Id); }

        return versie;
    }

    /// <summary>Zet bytes in de filestore onder StoreType 310 met de NDA als eigenaar.</summary>
    public async Task<int> BewaarBestandAsync(byte[] inhoud, string bestandsnaam, int ndaId, int? werknemerId)
    {
        await using var stroom = new MemoryStream(inhoud);
        return await _store.ToevoegenAsync(stroom, bestandsnaam, StoreType, ndaId, werknemerId ?? 0);
    }

    public async Task<int> BewaarBestandAsync(Stream inhoud, string bestandsnaam, int ndaId, int? werknemerId)
        => await _store.ToevoegenAsync(inhoud, bestandsnaam, StoreType, ndaId, werknemerId ?? 0);

    /// <summary>
    /// Neemt een door de wederpartij ondertekend exemplaar op. De NDA gaat daarmee
    /// naar de status Ondertekend, met de datum van vandaag als die nog leeg was.
    /// </summary>
    public async Task VoegGetekendToeAsync(Nda nda, Stream inhoud, string bestandsnaam,
                                           int? werknemerId)
    {
        var id = await BewaarBestandAsync(inhoud, bestandsnaam, nda.Id, werknemerId);
        await _repo.VoegBestandToeAsync(new NdaBestand
        {
            NdaId = nda.Id, Versie = "getekend", Soort = "getekend",
            FileStoreId = id, Datum = DateTime.Now,
            ToegevoegdDoor = werknemerId, Notitie = "Ondertekend exemplaar",
        });

        nda.Status = NdaStatus.Ondertekend;
        nda.DatumOndertekend ??= DateTime.Now;
        await _repo.BewaarAsync(nda, werknemerId);
    }

    /// <summary>Onderwerp en tekst voor de begeleidende e-mail.</summary>
    public async Task<(string Onderwerp, string Tekst)> ConceptmailAsync(Nda nda)
    {
        var inst = await _repo.GetInstellingenAsync();
        string Inst(string sleutel, string standaard)
            => inst.TryGetValue(sleutel, out var v) && !string.IsNullOrWhiteSpace(v) ? v : standaard;

        var onderwerp = $"Geheimhoudingsovereenkomst – {nda.Projectomschrijving}";
        var tekst =
            $"Bijgaand onze geheimhoudingsovereenkomst voor {nda.Projectomschrijving}.\n" +
            "Graag ontvangen wij een door jullie ondertekend exemplaar retour.\n" +
            "Bij vragen of opmerkingen hoor ik het graag.\n\n" +
            "Met vriendelijke groet,\n" +
            Inst("email_naam", "") + "\n" +
            "MDE Automation B.V.\n" +
            Inst("email_telefoon", "") + "\n" +
            Inst("email_adres", "");

        return (onderwerp, tekst);
    }

    /// <summary>Vult een nieuwe NDA voor met de gegevens van de relatie.</summary>
    public async Task<Nda> NieuwVoorRelatieAsync(int relatieId)
    {
        var nda = new Nda
        {
            RelatieId = relatieId,
            DatumDocument = DateOnly.FromDateTime(DateTime.Today),
            Status = NdaStatus.Concept,
            Soort = "eigen",
            Onbepaald = true,
        };

        var gegevens = await _repo.GetRelatieGegevensAsync(relatieId);
        if (gegevens is not null)
        {
            nda.RelatieNaam = gegevens.Value.Naam;
            nda.Adres = gegevens.Value.Adres;
            nda.KvK = gegevens.Value.KvK;

            // Rechtsvorm uit de naam halen als die er in staat
            foreach (var vorm in new[] { "B.V.", "BV", "N.V.", "NV", "V.O.F.", "GmbH", "Ltd", "Inc" })
                if (nda.RelatieNaam.EndsWith(" " + vorm, StringComparison.OrdinalIgnoreCase))
                {
                    nda.Rechtsvorm = vorm;
                    break;
                }
        }
        return nda;
    }
}
