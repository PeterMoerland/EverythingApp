namespace EverythingApp.Services;

/// <summary>
/// Een postbus die de app kan laten zien.
///
/// Eigen = de postbus van de ingelogde gebruiker; die werkt met het token dat hij
/// bij het inloggen zelf heeft gegeven. Anders gaat het om een gedeelde postbus
/// (inkoop@, verkoop@, info@) die de app namens het bedrijf leest met een
/// servicedescriptor en domeinbrede delegatie.
/// </summary>
public record MailPostbus(string Naam, string Adres, string? Recht, bool Eigen)
{
    /// <summary>
    /// Verzenden als een alias van <see cref="Adres"/> (Gmail "verzenden als"): het serviceaccount werkt namens Adres,
    /// de mail gaat uit van Afzender. Gebruikt door werkplekken (Beheer ▸ Werkplekken).
    /// </summary>
    public string? Afzender { get; init; }

    /// <summary>Sleutel voor keuzelijsten en onthouden welke postbus open stond.</summary>
    public string Sleutel => Eigen ? "eigen" : Adres.ToLowerInvariant();
}

/// <summary>
/// Welke postbussen er zijn en wie erbij mag.
///
/// In appsettings.json:
///   "Mail": {
///     "Postbussen": [
///       { "Naam": "Inkoop", "Adres": "inkoop@mde-automation.nl", "Recht": "Inkoop" },
///       { "Naam": "Info",   "Adres": "info@mde-automation.nl" }
///     ]
///   }
///
/// Recht is de naam van een kolom in Werknemers, net als bij de andere rechten.
/// Laat je Recht weg, dan mag iedereen die postbus zien.
/// </summary>
public class MailPostbussen
{
    private readonly List<MailPostbus> _gedeeld = new();
    private readonly GoogleServiceAccount _serviceaccount;

    public MailPostbussen(IConfiguration config, GoogleServiceAccount serviceaccount)
    {
        _serviceaccount = serviceaccount;

        var deel = config.GetSection("Mail:Postbussen").GetChildren();
        foreach (var regel in deel)
        {
            var adres = regel["Adres"];
            if (string.IsNullOrWhiteSpace(adres)) continue;

            _gedeeld.Add(new MailPostbus(
                Naam: string.IsNullOrWhiteSpace(regel["Naam"]) ? adres! : regel["Naam"]!,
                Adres: adres!,
                Recht: string.IsNullOrWhiteSpace(regel["Recht"]) ? null : regel["Recht"],
                Eigen: false));
        }
    }

    /// <summary>Zijn er gedeelde postbussen ingesteld én bruikbaar?</summary>
    public bool HeeftGedeeld => _gedeeld.Count > 0 && _serviceaccount.Beschikbaar;

    /// <summary>Uitleg als er wel postbussen zijn ingesteld maar ze niet werken.</summary>
    public string? Waarschuwing
        => _gedeeld.Count > 0 && !_serviceaccount.Beschikbaar
            ? "Gedeelde postbussen zijn ingesteld maar nog niet bruikbaar: " + _serviceaccount.Beschrijf()
            : null;

    /// <summary>
    /// De postbussen die deze gebruiker mag zien: zijn eigen postbus vooraan, daarna
    /// de gedeelde waar hij recht op heeft.
    /// </summary>
    public List<MailPostbus> Voor(HuidigeGebruiker ik)
    {
        // Een werkplek (gedeelde pc) leest geen mail; versturen gaat via Werkplek(ik).
        if (ik.IsWerkplek) return new();

        var uit = new List<MailPostbus>
        {
            new("Mijn mail", ik.Email, null, Eigen: true),
        };

        // Zonder werkend serviceaccount kunnen gedeelde postbussen niet open;
        // die laten we dan ook niet in de keuzelijst staan.
        if (!_serviceaccount.Beschikbaar) return uit;

        foreach (var postbus in _gedeeld)
        {
            var mag = postbus.Recht is null
                      || !ik.Gekoppeld
                      || ik.Mag(postbus.Recht)
                      || ik.Mag("Directie");
            if (mag) uit.Add(postbus);
        }
        return uit;
    }

    /// <summary>
    /// De afzender van een werkplek: zijn alias, verstuurd namens het account waar die alias op staat. Null als de
    /// werkplek geen afzender heeft of het serviceaccount niet werkt.
    /// </summary>
    public MailPostbus? Werkplek(HuidigeGebruiker ik)
    {
        var w = ik.Werkplek;
        if (w is null || w.MailAfzender.Trim().Length == 0 || w.MailAccount.Trim().Length == 0 || !_serviceaccount.Beschikbaar) return null;
        return new MailPostbus(w.Naam, w.MailAccount.Trim(), null, Eigen: false) { Afzender = w.MailAfzender.Trim() };
    }

    /// <summary>Waarom een werkplek (nog) niet kan mailen; null als het wel kan.</summary>
    public string? WerkplekReden(HuidigeGebruiker ik)
        => ik.Werkplek is not { } w ? null
         : w.MailAfzender.Trim().Length == 0 || w.MailAccount.Trim().Length == 0
             ? "Voor deze werkplek is nog geen afzender ingesteld (Beheer ▸ Werkplekken)."
         : !_serviceaccount.Beschikbaar ? "Mail versturen werkt niet: " + _serviceaccount.Beschrijf()
         : null;

    public MailPostbus? Zoek(HuidigeGebruiker ik, string? sleutel)
        => Voor(ik).FirstOrDefault(p => p.Sleutel == (sleutel ?? "eigen"));
}
