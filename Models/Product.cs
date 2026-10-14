namespace EverythingApp.Models;

/// <summary>Een producttype uit ProductNames — 'MPR-1 · M-Pede robot type 1'.</summary>
public record ProductNaam(int Id, string Naam, string Omschrijving, string TypeCode, int? TypeId)
{
    public string Volledig => string.IsNullOrWhiteSpace(Omschrijving) ? Naam : $"{Naam} · {Omschrijving}";

    // Uit de zijtabel ProductNameExtra (ProductGroepen_MariaDB.sql); leeg als die er niet is.
    public int? GroepId { get; init; }
    public string Groep { get; init; } = "";
    public int? ArtikelId { get; init; }
    public string ArtikelCode { get; init; } = "";
    public string ArtikelOmschrijving { get; init; } = "";

    /// <summary>Stappen (ProductStap-codes) die Half-fabricaten voor dit halffabricaat níet toont (ProductNameExtra.Toon…).</summary>
    public HashSet<string> VerborgenStappen { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ToonStap(string stap) => !VerborgenStappen.Contains(stap);
}

/// <summary>Eén exemplaar uit Producten, zoals het in de serienummerlijst staat.</summary>
public record ProductRegel(int Id, string Serienummer, string Klant, string StatusKort,
                           string Opmerking, bool Gewist, int? StatusId = null, string Project = "");

/// <summary>Een regel uit ProductHistorie.</summary>
/// <param name="Uitkomst">true = geslaagd, false = mislukt, null = neutraal (kolom Uitkomst, ProductTechniek_MariaDB.sql).</param>
public record ProductGebeurtenis(int Id, string Event, string Soort, DateTime? Wanneer, string Door, bool? Uitkomst = null)
{
    /// <summary>CSS-klasse voor de kleur van de regel/het chipje.</summary>
    public string Kleur => Uitkomst switch { true => "s-groen", false => "s-rood", _ => "s-grijs" };
}

/// <summary>Wat er op een halffabricaat zit; bepaalt welke knoppen Test PCB toont.</summary>
public static class ProductTechniek
{
    public const string Drives = "drives";
    public const string Esp32 = "esp32";
    public const string Stm32 = "stm32";
    public const string Feetech = "feetech";
    public const string Can = "can";
    /// <summary>
    /// Sub-vinkje onder EtherCAT: dit product krijgt geen parametersets (alleen firmware). Omgekeerd opgeslagen
    /// (een regel = géén parameters), zodat bestaande producten zonder deze regel parametersets houden.
    /// </summary>
    public const string GeenParameters = "geenparams";
    /// <summary>Sub-vinkje onder EtherCAT: het serienummer van het exemplaar in de EEPROM van de eigen MDE-slave(s) schrijven.</summary>
    public const string SerienummerSchrijven = "ecatsn";

    public static readonly (string Code, string Naam, string Uitleg)[] Alle =
    {
        (Drives,  "EtherCAT-slaves", "Alles aan de EtherCAT-bus: Ingenia/Novanta Everest of Capitan, ZeroErr eRob en eigen MDE-slaves (M-Pede-WJ1 …): uitlezen, firmware, parameters, testen; per slave één positie hieronder"),
        (Can,     "eRob via CAN",    "Losse ZeroErr eRob aan de USBCAN: serienummer, model, hardware en partnummer uitlezen over CAN (ZeroErr-protocol)"),
        (Esp32,   "ESP32",           "programmeren via USB-UART (esptool); alleen firmware van de typen op de posities van dit halffabricaat — bij een MDE-slave de eerste keer, daarna via EtherCAT met hetzelfde bestand"),
        (Stm32,   "STM32",           "programmeren via ST-LINK; alleen firmware van de typen op de posities van dit halffabricaat"),
        (Feetech, "Feetech-servo",   "STS/SMS-servomotor: uitlezen en programmeren met een JSON-parameterset via de USB-TTL-adapter"),
    };

}

/// <summary>
/// Welke stappen het scherm Half-fabricaten toont voor een halffabricaat. Een kabel
/// hoeft niet geprogrammeerd of automatisch getest te worden; dan kunnen die stappen weg.
/// Per halffabricaat vastgelegd in ProductNameExtra (kolommen Toon&lt;Code&gt;, 1 = tonen).
/// </summary>
public static class ProductStap
{
    public const string Uitlezen = "Uitlezen";
    public const string Programmeren = "Programmeren";
    public const string Testen = "Testen";
    public const string Opbouw = "Opbouw";
    public const string Duurtest = "Duurtest";
    public const string NodeAdres = "NodeAdres";

    public static readonly (string Code, string Naam, string Uitleg)[] Alle =
    {
        (Uitlezen,     "Automatisch uitlezen",        "Half-fabricaten: knop bij het exemplaar — drives of servo uitlezen en in de attributen zetten"),
        (Programmeren, "Stap Programmeren",           "Half-fabricaten: firmware en parameters in drives, chip flashen, servo programmeren"),
        (Testen,       "Stap Testen",                 "Half-fabricaten: automatische testen via de werkplek-agent en de handmatige uitgangen"),
        (Opbouw,       "Stap Opbouw en eindcontrole", "Half-fabricaten en Producten: de afvinklijst met opbouwstappen, eindchecks en 'Product gereed'"),
        (Duurtest,     "Stap Duurtest",               "Producten: het product in de testrig, daarna de log van de PLC ophalen als document"),
        (NodeAdres,    "Stap EtherCAT node-adressen", "Half-fabricaten en Producten: de node-nummers (station alias) van de slaves op de bus lezen en instellen — 'Write to slaves'"),
    };

    /// <summary>Standaard zichtbaar (1) of niet (0) zolang de kolom in ProductNameExtra ontbreekt of leeg is.</summary>
    public static int Standaard(string code) => code == NodeAdres ? 0 : 1;

    /// <summary>Kolomnaam in ProductNameExtra.</summary>
    public static string Kolom(string code) => "Toon" + code;
}

/// <summary>
/// Een regel uit de stuklijst van een producttype (ProductSamenstelling_MariaDB.sql):
/// welk halffabricaat er in het product gaat en hoeveel.
/// </summary>
public class StuklijstRegel
{
    public int Id { get; set; }
    public int ProductNameId { get; set; }
    public int OnderdeelNameId { get; set; }
    public string OnderdeelNaam { get; set; } = "";
    public string OnderdeelOmschrijving { get; set; } = "";
    public string OnderdeelGroep { get; set; } = "";
    public int Aantal { get; set; } = 1;
    public int Volgorde { get; set; }
    public string Opmerking { get; set; } = "";
}

/// <summary>
/// Een regel in de samenstelling: een product dat in een ander product zit.
/// Zowel gebruikt voor 'Gebruikt' (de onderdelen) als voor 'Wordt gebruikt in'.
/// </summary>
public record SamenstellingRegel(int ProductId, int ProductNameId, string Code, string Serienummer,
                                 string Omschrijving, string TypeCode);

/// <summary>
/// Een attribuut. Dezelfde vorm voor beide tabellen: bij een producttype komt het
/// uit ProductNameAttributes (dan is Waarde leeg en telt VraagBijAanmaak), bij een
/// exemplaar uit ProductAttributes (dan staat er een waarde in).
/// </summary>
public class ProductAttribuut
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Attribuut { get; set; } = "";
    public string Waarde { get; set; } = "";
    public string Eenheid { get; set; } = "";
    public bool Verplicht { get; set; }
    public bool VraagBijAanmaak { get; set; }
    public string Opmerking { get; set; } = "";

    /// <summary>
    /// Waar de waarde automatisch vandaan komt (Test PCB): 'drive:1:serienummer',
    /// 'esp32:mac', ... Leeg = handmatig. Staat op het standaardattribuut van het
    /// producttype; bij een exemplaar-attribuut is het via het sjabloon meegelezen.
    /// </summary>
    public string Bron { get; set; } = "";

    /// <summary>Bij een exemplaar-attribuut: het standaardattribuut (ProductNameAttributeID) waar het van afstamt.</summary>
    public int? SjabloonId { get; set; }
}

/// <summary>De bronnen die Test PCB kan vullen — de keuzelijst in het attributenbeheer.</summary>
public static class AttribuutBronnen
{
    public static readonly (string Code, string Omschrijving)[] Alle =
    {
        ("", "— handmatig —"),
        ("drive:1:serienummer", "Drive 1 · serienummer (EtherCAT)"),
        ("drive:2:serienummer", "Drive 2 · serienummer (EtherCAT)"),
        ("drive:3:serienummer", "Drive 3 · serienummer (EtherCAT)"),
        ("drive:4:serienummer", "Drive 4 · serienummer (EtherCAT)"),
        ("drive:1:type", "Drive 1 · type (Capitan/Everest)"),
        ("drive:2:type", "Drive 2 · type (Capitan/Everest)"),
        ("drive:3:type", "Drive 3 · type (Capitan/Everest)"),
        ("drive:4:type", "Drive 4 · type (Capitan/Everest)"),
        ("drive:1:firmware", "Drive 1 · firmwareversie"),
        ("drive:2:firmware", "Drive 2 · firmwareversie"),
        ("drive:3:firmware", "Drive 3 · firmwareversie"),
        ("drive:4:firmware", "Drive 4 · firmwareversie"),
        ("drive:1:hardware", "Drive 1 · hardwareversie"),
        ("drive:2:hardware", "Drive 2 · hardwareversie"),
        ("drive:1:parameters", "Drive 1 · geladen parameterset"),
        ("drive:2:parameters", "Drive 2 · geladen parameterset"),
        ("drive:3:parameters", "Drive 3 · geladen parameterset"),
        ("drive:4:parameters", "Drive 4 · geladen parameterset"),
        ("can:1:serienummer", "eRob via CAN · serienummer"),
        ("can:1:model", "eRob via CAN · modelcode (bv. eRob110H100I-BS-18EN)"),
        ("can:1:partnummer", "eRob via CAN · partnummer (bv. MC25.ED.CE)"),
        ("can:1:hardware", "eRob via CAN · hardware-info (bv. 1005_5_0)"),
        ("can:1:firmware", "eRob via CAN · firmwareversie"),
        ("motor:id", "Servo · bus-ID (Feetech)"),
        ("motor:model", "Servo · model"),
        ("motor:firmware", "Servo · firmwareversie"),
        ("motor:parameters", "Servo · geladen parameterset"),
        ("esp32:mac", "ESP32 · MAC-adres (na flashen)"),
        ("stm32:uid", "STM32 · UID (na flashen)"),
        ("firmware:versie", "Firmware · geladen bestand"),
    };

    public static string Omschrijving(string? code)
        => Alle.FirstOrDefault(b => b.Code == (code ?? "")).Omschrijving ?? code ?? "";

    /// <summary>'drive:2:serienummer' → (2, "serienummer"); null als het geen drive-bron is.</summary>
    public static (int Nummer, string Veld)? Drive(string? code) => Bus(code, "drive");

    /// <summary>'can:1:serienummer' → (1, "serienummer"); null als het geen CAN-bron is (eRob via CAN).</summary>
    public static (int Nummer, string Veld)? Can(string? code) => Bus(code, "can");

    private static (int Nummer, string Veld)? Bus(string? code, string bus)
    {
        var d = (code ?? "").Split(':');
        return d.Length == 3 && d[0] == bus && int.TryParse(d[1], out var n) && n > 0 ? (n, d[2]) : null;
    }
}
