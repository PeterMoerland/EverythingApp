using System.Text.Json;
using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>Wat er met één NDA uit de oude map gebeurde.</summary>
public record ImportRegel(string Relatie, bool Gekoppeld, int Bestanden, int Gemist, string Opmerking);

/// <summary>
/// Neemt de losse NDA-app over: leest nda_register.json, koppelt elke NDA aan een
/// relatie en zet de documenten in de filestore. Kan zonder bezwaar twee keer
/// draaien — wat er al staat wordt overgeslagen.
/// </summary>
public class NdaImportService
{
    private readonly INdaRepository _repo;
    private readonly NdaService _nda;
    private readonly IConfiguration _config;
    private readonly ILogger<NdaImportService> _log;

    public NdaImportService(INdaRepository repo, NdaService nda,
                            IConfiguration config, ILogger<NdaImportService> log)
    {
        _repo = repo;
        _nda = nda;
        _config = config;
        _log = log;
    }

    /// <summary>Map van de oude app; instelbaar met Nda:OudePad.</summary>
    public string OudePad => _config["Nda:OudePad"] ?? @"T:\NDA APP";

    public string RegisterPad => Path.Combine(OudePad, "nda_register.json");

    public bool Klaar => File.Exists(RegisterPad);

    public async Task<List<ImportRegel>> ImporteerAsync(int? doorWerknemer)
    {
        var uitkomst = new List<ImportRegel>();

        if (!File.Exists(RegisterPad))
        {
            uitkomst.Add(new ImportRegel("—", false, 0, 0,
                $"nda_register.json niet gevonden in {OudePad}. Zet Nda:OudePad in appsettings.json."));
            return uitkomst;
        }

        var json = await File.ReadAllTextAsync(RegisterPad);
        using var doc = JsonDocument.Parse(json);

        var bestaand = await _repo.ZoekAsync(null, null, null);

        foreach (var regel in doc.RootElement.EnumerateArray())
        {
            var naam = Tekst(regel, "relatie_naam");
            if (string.IsNullOrWhiteSpace(naam)) continue;

            var omschrijving = Tekst(regel, "projectomschrijving");

            // al eerder geïmporteerd?
            if (bestaand.Any(n => string.Equals(n.RelatieNaam, naam, StringComparison.OrdinalIgnoreCase)
                                  && string.Equals(n.Projectomschrijving, omschrijving, StringComparison.OrdinalIgnoreCase)))
            {
                uitkomst.Add(new ImportRegel(naam, true, 0, 0, "Stond er al — overgeslagen."));
                continue;
            }

            int? relatieId = null;
            try { relatieId = await _repo.ZoekRelatieOpNaamAsync(naam); }
            catch { }

            var nda = new Nda
            {
                RelatieId = relatieId,
                RelatieNaam = naam,
                Rechtsvorm = Tekst(regel, "relatie_rechtsvorm"),
                Adres = Tekst(regel, "relatie_adres"),
                KvK = Tekst(regel, "relatie_kvk"),
                ToepasselijkRecht = Leeg(Tekst(regel, "toepasselijk_recht"), "Nederlands"),
                Projectomschrijving = omschrijving,
                Projectnummer = Tekst(regel, "projectnummer"),
                DatumDocument = Datum(regel, "datum_document"),
                DatumAangemaakt = Tijd(regel, "datum_aangemaakt"),
                DatumVerzonden = Tijd(regel, "datum_verzonden"),
                DatumOndertekend = Tijd(regel, "datum_ondertekend"),
                Status = Leeg(Tekst(regel, "status"), NdaStatus.Concept),
                Soort = Leeg(Tekst(regel, "type"), "eigen"),
                Notities = Tekst(regel, "notities"),
            };

            // einddatum is óf een datum, óf de tekst 'onbepaalde_tijd'
            var eind = Tekst(regel, "einddatum");
            if (string.Equals(eind, "onbepaalde_tijd", StringComparison.OrdinalIgnoreCase)) nda.Onbepaald = true;
            else if (DateOnly.TryParse(eind.Length >= 10 ? eind[..10] : eind, out var d)) nda.Einddatum = d;

            int ndaId;
            try { ndaId = await _repo.BewaarAsync(nda, doorWerknemer); }
            catch (Exception ex)
            {
                uitkomst.Add(new ImportRegel(naam, false, 0, 0, "Opslaan mislukt: " + ex.Message));
                continue;
            }
            nda.Id = ndaId;

            // documenten
            var map = Path.Combine(OudePad, NdaService.VeiligeNaam(naam));
            var gedaan = 0;
            var gemist = 0;

            if (regel.TryGetProperty("versies", out var versies) && versies.ValueKind == JsonValueKind.Array)
            {
                foreach (var versie in versies.EnumerateArray())
                {
                    var versienaam = Leeg(Tekst(versie, "versie"), "v1");
                    var isGetekend = versienaam.Contains("getekend", StringComparison.OrdinalIgnoreCase);

                    foreach (var (sleutel, soort) in new[] { ("bestand", isGetekend ? "getekend" : "word"), ("pdf", "pdf") })
                    {
                        var bestandsnaam = Tekst(versie, sleutel);
                        if (string.IsNullOrWhiteSpace(bestandsnaam)) continue;

                        var pad = Path.Combine(map, bestandsnaam);
                        if (!File.Exists(pad)) { gemist++; continue; }

                        try
                        {
                            await using var stroom = File.OpenRead(pad);
                            var fileId = await _nda.BewaarBestandAsync(stroom, bestandsnaam, ndaId, doorWerknemer);
                            await _repo.VoegBestandToeAsync(new NdaBestand
                            {
                                NdaId = ndaId,
                                Versie = versienaam,
                                Soort = soort,
                                FileStoreId = fileId,
                                Datum = Tijd(versie, "datum") ?? File.GetLastWriteTime(pad),
                                ToegevoegdDoor = doorWerknemer,
                                Notitie = Leeg(Tekst(versie, "notitie"), "Overgenomen uit de oude NDA-app"),
                            });
                            gedaan++;
                        }
                        catch (Exception ex)
                        {
                            gemist++;
                            _log.LogWarning(ex, "NDA-bestand {Pad} kon niet worden overgezet", pad);
                        }
                    }
                }
            }

            var opmerking = relatieId is null
                ? "Geen relatie gevonden met deze naam — koppel hem handmatig."
                : "Overgezet.";
            if (gemist > 0) opmerking += $" {gemist} bestand(en) niet gevonden in {map}.";

            uitkomst.Add(new ImportRegel(naam, relatieId is not null, gedaan, gemist, opmerking));
        }

        return uitkomst;
    }

    // ---- kleine hulpjes voor het lezen van de JSON ----
    private static string Tekst(JsonElement of, string naam)
        => of.TryGetProperty(naam, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Leeg(string waarde, string standaard)
        => string.IsNullOrWhiteSpace(waarde) ? standaard : waarde;

    private static DateOnly? Datum(JsonElement of, string naam)
    {
        var t = Tekst(of, naam);
        return t.Length >= 10 && DateOnly.TryParse(t[..10], out var d) ? d : null;
    }

    private static DateTime? Tijd(JsonElement of, string naam)
    {
        var t = Tekst(of, naam);
        return DateTime.TryParse(t, out var d) ? d : null;
    }
}
