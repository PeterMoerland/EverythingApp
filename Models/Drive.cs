namespace EverythingApp.Models;

/// <summary>Een drivetype (Everest, Capitan, M-Pede-WJ1 …), herkend aan EtherCAT-fabrikantcode + productcode.</summary>
public class DriveSoort
{
    public int Id { get; set; }
    /// <summary>EtherCAT Vendor ID als hex (0x000010E3 = MDE); leeg = nog niet vastgelegd, dan alleen op productcode.</summary>
    public string VendorId { get; set; } = "";
    public string ProductCode { get; set; } = "";
    /// <summary>ethercat | feetech | canopen — bepaalt hoe de agent ermee praat.</summary>
    public string Protocol { get; set; } = DriveProtocol.EtherCat;
    public string Naam { get; set; } = "";
    public string Fabrikant { get; set; } = "";
    public string Opmerking { get; set; } = "";
    public int AantalFirmwares { get; set; }
}

/// <summary>Een firmwareversie van een drivetype, met status en bestanden.</summary>
public class DriveFirmware
{
    public int Id { get; set; }
    public int DriveTypeId { get; set; }
    public string Versie { get; set; } = "";
    public string Status { get; set; } = DriveStatus.Toegestaan;
    public int? FirmwareFileId { get; set; }
    public string FirmwareBestand { get; set; } = "";
    public int? DictionaryFileId { get; set; }
    public string DictionaryBestand { get; set; } = "";
    public int? EsiFileId { get; set; }
    public string EsiBestand { get; set; } = "";
    public DateTime? Vrijgegeven { get; set; }
    public string Opmerking { get; set; } = "";
    public string StatusDoor { get; set; } = "";
    public DateTime? StatusOp { get; set; }
}

/// <summary>
/// Testrecept bij een controllertype (tabel DriveRecepten): een lijst stappen uit vaste bouwstenen die de
/// werkplek-agent uitvoert (mde.test). Stappen is JSON, zoals in MdeAgent/mde_agent/taken/mde.py beschreven.
/// </summary>
public class DriveRecept
{
    public int Id { get; set; }
    public int DriveTypeId { get; set; }
    public int Volgorde { get; set; } = 1;
    public string Naam { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public string Stappen { get; set; } = "[]";
    public bool Verplicht { get; set; } = true;

    /// <summary>Voorbeeldrecept (Swivel) om van te starten; alle bouwstenen komen erin voor.</summary>
    public const string Voorbeeld =
        "[\n" +
        "  {\"stap\": \"monitor\", \"objecten\": [{\"index\": \"0x6020\", \"sub\": 0, \"type\": \"UINT\", \"naam\": \"Druk (bar x100)\", \"min\": 0, \"max\": 4500},\n" +
        "                                 {\"index\": \"0x6010\", \"sub\": 1, \"type\": \"INT\", \"naam\": \"Joint 1\"}]},\n" +
        "  {\"stap\": \"identiteit\", \"vendor\": \"0x000010E3\", \"productcode\": \"0x00055955\", \"revisie\": \"0x00000001\"},\n" +
        "  {\"stap\": \"lees\", \"naam\": \"Druk (bar x100)\", \"index\": \"0x6020\", \"sub\": 0, \"type\": \"UINT\", \"min\": 0, \"max\": 4500},\n" +
        "  {\"stap\": \"doorgeefluik\", \"naam\": \"Motor 1 firmware\", \"motor\": 1, \"register\": 3, \"index\": \"0x8000\"},\n" +
        "  {\"stap\": \"herhaal\", \"naam\": \"Joint 1 heen en terug\", \"keer\": 10, \"stappen\": [\n" +
        "    {\"stap\": \"beweging\", \"naam\": \"naar +300\", \"doel_index\": \"0x7010\", \"doel_sub\": 1, \"stand_index\": \"0x6010\", \"stand_sub\": 1,\n" +
        "     \"type\": \"INT\", \"doel\": 300, \"tolerantie\": 5, \"timeout\": 5, \"controlword\": {\"index\": \"0x7000\", \"sub\": 0, \"aan\": 1, \"uit\": 0}},\n" +
        "    {\"stap\": \"pauze\", \"seconden\": 0.5},\n" +
        "    {\"stap\": \"beweging\", \"naam\": \"terug naar 0\", \"doel_index\": \"0x7010\", \"doel_sub\": 1, \"stand_index\": \"0x6010\", \"stand_sub\": 1,\n" +
        "     \"type\": \"INT\", \"doel\": 0, \"tolerantie\": 5, \"timeout\": 5, \"controlword\": {\"index\": \"0x7000\", \"sub\": 0, \"aan\": 1, \"uit\": 0}}\n" +
        "  ]},\n" +
        "  {\"stap\": \"schrijf\", \"naam\": \"Vacuum aan\", \"index\": \"0x7020\", \"sub\": 1, \"type\": \"BOOL\", \"waarde\": 1},\n" +
        "  {\"stap\": \"wacht_tot\", \"naam\": \"Druk zakt onder 0,50 bar\", \"index\": \"0x6020\", \"sub\": 0, \"type\": \"UINT\", \"max\": 50, \"timeout\": 5},\n" +
        "  {\"stap\": \"schrijf\", \"naam\": \"Vacuum uit\", \"index\": \"0x7020\", \"sub\": 1, \"type\": \"BOOL\", \"waarde\": 0, \"stoppen\": false},\n" +
        "  {\"stap\": \"bediening\", \"naam\": \"Swivel met de hand bedienen\", \"seconden\": 1800,\n" +
        "   \"knoppen\": [{\"naam\": \"Joint 1 naar 0\", \"index\": \"0x7010\", \"sub\": 1, \"type\": \"INT\", \"waarde\": 0}],\n" +
        "   \"schakelaars\": [{\"naam\": \"Vacuum\", \"index\": \"0x7020\", \"sub\": 1}, {\"naam\": \"Blazen\", \"index\": \"0x7020\", \"sub\": 2}],\n" +
        "   \"invoer\": [{\"naam\": \"Doel joint 1\", \"index\": \"0x7010\", \"sub\": 1, \"type\": \"INT\", \"min\": -1000, \"max\": 1000}]}\n" +
        "]";

    /// <summary>Controleert de JSON: een lijst van objecten met een bekende "stap" (ook binnen "herhaal"). Geeft de fout, of null.</summary>
    public static string? Controleer(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return ControleerLijst(doc.RootElement, "");
        }
        catch (System.Text.Json.JsonException ex) { return "Geen geldige JSON: " + ex.Message; }
    }

    private static string? ControleerLijst(System.Text.Json.JsonElement lijst, string prefix)
    {
        if (lijst.ValueKind != System.Text.Json.JsonValueKind.Array) return prefix.Length == 0 ? "Het recept moet een lijst [ … ] zijn." : $"Stap {prefix.TrimEnd('.')}: \"stappen\" moet een lijst zijn.";
        var nr = 0;
        foreach (var s in lijst.EnumerateArray())
        {
            nr++;
            if (s.ValueKind != System.Text.Json.JsonValueKind.Object) return $"Stap {prefix}{nr} is geen object {{ … }}.";
            var stap = s.TryGetProperty("stap", out var st) ? st.GetString() ?? "" : "";
            if (!Bouwstenen.Contains(stap)) return $"Stap {prefix}{nr}: onbekende bouwsteen '{stap}' (bekend: {string.Join(", ", Bouwstenen)}).";
            if (stap == "autostart" && prefix.Length > 0) return $"Stap {prefix}{nr}: autostart hoort in het recept zelf, niet in een herhaal-blok.";
            if (stap == "herhaal")
            {
                if (!s.TryGetProperty("stappen", out var binnen)) return $"Stap {prefix}{nr}: herhaal heeft geen \"stappen\".";
                var fout = ControleerLijst(binnen, $"{prefix}{nr}.");
                if (fout is not null) return fout;
            }
            if (stap == "bediening")
            {
                var aantal = 0;
                foreach (var soort in new[] { "knoppen", "schakelaars", "invoer" })
                {
                    if (!s.TryGetProperty(soort, out var elementen)) continue;
                    if (elementen.ValueKind != System.Text.Json.JsonValueKind.Array) return $"Stap {prefix}{nr}: \"{soort}\" moet een lijst zijn.";
                    foreach (var el in elementen.EnumerateArray())
                    {
                        aantal++;
                        if (el.ValueKind != System.Text.Json.JsonValueKind.Object || !el.TryGetProperty("index", out _))
                            return $"Stap {prefix}{nr}: elk element in \"{soort}\" heeft een \"index\" nodig.";
                        if (soort == "knoppen" && !el.TryGetProperty("waarde", out _))
                            return $"Stap {prefix}{nr}: elke knop heeft een \"waarde\" nodig.";
                    }
                }
                if (aantal == 0) return $"Stap {prefix}{nr}: bediening zonder knoppen, schakelaars of invoer.";
            }
        }
        return nr == 0 ? (prefix.Length == 0 ? "Het recept heeft geen stappen." : $"Stap {prefix.TrimEnd('.')}: het herhaal-blok is leeg.") : null;
    }

    public static readonly string[] Bouwstenen = { "identiteit", "lees", "schrijf", "wacht", "pauze", "wacht_tot", "herhaal", "beweging", "doorgeefluik", "monitor", "bediening", "autostart" };

    /// <summary>Stap "autostart" (hoogste niveau): het recept start vanzelf als het testvenster opent, na zoveel seconden
    /// (standaard 0 = meteen). Null = geen autostart. De stap zelf gaat niet naar de agent (<see cref="StappenVoorAgent"/>).</summary>
    public double? Autostart()
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Stappen);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            foreach (var s in doc.RootElement.EnumerateArray())
                if (s.ValueKind == System.Text.Json.JsonValueKind.Object && s.TryGetProperty("stap", out var st) && st.GetString() == "autostart")
                    return Math.Clamp(BedienStap.Getal(s, "seconden") ?? 0, 0, 60);
        }
        catch { }
        return null;
    }

    /// <summary>De stappen zonder "autostart" (dat is een instelling voor Test PCB, geen stap voor de agent).</summary>
    public string StappenVoorAgent()
    {
        try
        {
            var lijst = System.Text.Json.Nodes.JsonNode.Parse(Stappen) as System.Text.Json.Nodes.JsonArray;
            if (lijst is null) return Stappen;
            foreach (var s in lijst.Where(s => s is System.Text.Json.Nodes.JsonObject o && o["stap"]?.GetValue<string>() == "autostart").ToList())
                lijst.Remove(s);
            return lijst.ToJsonString();
        }
        catch { return Stappen; }
    }

    /// <summary>De bediening-stap op plek <paramref name="nr"/> (1 = eerste stap, alleen het hoogste niveau); null als het geen bediening is.</summary>
    public BedienStap? Bediening(int nr)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(StappenVoorAgent());
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array || nr < 1 || nr > doc.RootElement.GetArrayLength()) return null;
            var s = doc.RootElement[nr - 1];
            if (s.ValueKind != System.Text.Json.JsonValueKind.Object || !s.TryGetProperty("stap", out var st) || st.GetString() != "bediening") return null;
            return new BedienStap
            {
                Naam = s.TryGetProperty("naam", out var n) ? n.GetString() ?? "" : "",
                Knoppen = BedienStap.Lees(s, "knoppen"), Schakelaars = BedienStap.Lees(s, "schakelaars"), Invoer = BedienStap.Lees(s, "invoer"),
            };
        }
        catch { return null; }
    }

    /// <summary>Grenzen (min/max) per objectnaam uit de monitor-stappen, ook binnen herhaal-blokken: grenslijnen in de grafiek.</summary>
    public Dictionary<string, (double? Min, double? Max)> Grenzen()
    {
        var uit = new Dictionary<string, (double? Min, double? Max)>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Stappen);
            void Loop(System.Text.Json.JsonElement lijst)
            {
                if (lijst.ValueKind != System.Text.Json.JsonValueKind.Array) return;
                foreach (var s in lijst.EnumerateArray())
                {
                    if (s.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    if (s.TryGetProperty("objecten", out var o)) foreach (var kv in GrenzenUit(o)) uit[kv.Key] = kv.Value;
                    if (s.TryGetProperty("stappen", out var b)) Loop(b);
                }
            }
            Loop(doc.RootElement);
        }
        catch { }
        return uit;
    }

    /// <summary>Grenzen uit een objectenlijst [{naam, min?, max?}, …] (monitor-stap of Meekijken).</summary>
    public static Dictionary<string, (double? Min, double? Max)> GrenzenUit(System.Text.Json.JsonElement objecten)
    {
        var uit = new Dictionary<string, (double? Min, double? Max)>();
        if (objecten.ValueKind != System.Text.Json.JsonValueKind.Array) return uit;
        foreach (var o in objecten.EnumerateArray())
        {
            if (o.ValueKind != System.Text.Json.JsonValueKind.Object || !o.TryGetProperty("naam", out var n) || n.GetString() is not { Length: > 0 } naam) continue;
            var min = BedienStap.Getal(o, "min");
            var max = BedienStap.Getal(o, "max");
            if (min is not null || max is not null) uit[naam] = (min, max);
        }
        return uit;
    }

    /// <summary>De namen van de stappen, voor de weergave; een herhaal-blok als "naam (herhaal 10×: a → b)".</summary>
    public List<string> StapNamen()
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Stappen);
            return Namen(doc.RootElement);
        }
        catch { return new(); }
    }

    private static List<string> Namen(System.Text.Json.JsonElement lijst)
    {
        var uit = new List<string>();
        if (lijst.ValueKind != System.Text.Json.JsonValueKind.Array) return uit;
        foreach (var s in lijst.EnumerateArray())
        {
            if (s.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
            var stap = s.TryGetProperty("stap", out var st) ? st.GetString() ?? "" : "";
            var naam = s.TryGetProperty("naam", out var n) ? n.GetString() ?? "" : "";
            if (stap == "autostart") continue;      // geen stap voor de agent; telt niet mee in de nummering
            if (stap == "herhaal")
            {
                var keer = s.TryGetProperty("keer", out var k) && k.ValueKind == System.Text.Json.JsonValueKind.Number ? k.GetInt32() : 1;
                var binnen = s.TryGetProperty("stappen", out var b) ? Namen(b) : new();
                stap = $"herhaal {keer}×: {string.Join(" → ", binnen)}";
            }
            uit.Add(naam.Length > 0 ? $"{naam} ({stap})" : stap);
        }
        return uit;
    }
}

/// <summary>Een stap "bediening" uit een testrecept: knoppen, schakelaars en invoervelden (HMI) voor Test PCB.</summary>
public class BedienStap
{
    public string Naam { get; set; } = "";
    public List<BedienElement> Knoppen { get; set; } = new();
    public List<BedienElement> Schakelaars { get; set; } = new();
    public List<BedienElement> Invoer { get; set; } = new();

    internal static List<BedienElement> Lees(System.Text.Json.JsonElement stap, string soort)
    {
        var uit = new List<BedienElement>();
        if (!stap.TryGetProperty(soort, out var lijst) || lijst.ValueKind != System.Text.Json.JsonValueKind.Array) return uit;
        foreach (var el in lijst.EnumerateArray())
        {
            if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
            var index = el.TryGetProperty("index", out var i) ? i.ToString() : "";
            var sub = el.TryGetProperty("sub", out var s) ? s.ToString() : "0";
            var naam = el.TryGetProperty("naam", out var n) ? n.GetString() ?? "" : "";
            // knop: de waarde die hij schrijft; schakelaar: de waarde voor "aan" (standaard 1), om de stand te tonen
            var waarde = soort == "schakelaars" ? (el.TryGetProperty("aan", out var a) ? a.ToString() : "1")
                       : el.TryGetProperty("waarde", out var w) ? w.ToString() : null;
            uit.Add(new BedienElement(naam.Length > 0 ? naam : $"{index}:{sub}", Getal(el, "min"), Getal(el, "max"), waarde));
        }
        return uit;
    }

    internal static double? Getal(System.Text.Json.JsonElement el, string veld)
        => el.TryGetProperty(veld, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDouble()
         : el.TryGetProperty(veld, out v) && v.ValueKind == System.Text.Json.JsonValueKind.String
           && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d
         : null;
}

/// <summary>Eén knop, schakelaar of invoerveld van een bediening-stap (de agent zoekt index/sub/type zelf op in het recept).</summary>
public record BedienElement(string Naam, double? Min, double? Max, string? Waarde);

/// <summary>Wat er op een drive-positie van een halffabricaat verwacht wordt.</summary>
public class DrivePositie
{
    public int Id { get; set; }
    public int ProductNameId { get; set; }
    public int Positie { get; set; }
    public string Naam { get; set; } = "";
    public int? DriveTypeId { get; set; }
    public string DriveTypeNaam { get; set; } = "";
}

/// <summary>Parameterset (.xcf) voor halffabricaat × positie × firmwareversie.</summary>
public class DriveParameterset
{
    public int Id { get; set; }
    public int ProductNameId { get; set; }
    public int Positie { get; set; }
    public int DriveFirmwareId { get; set; }
    public string FirmwareVersie { get; set; } = "";
    public string DriveTypeNaam { get; set; } = "";
    public string Omschrijving { get; set; } = "";
    public int? ParameterFileId { get; set; }
    public string ParameterBestand { get; set; } = "";
    public string Status { get; set; } = DriveStatus.Actueel;
    public string Opmerking { get; set; } = "";
}

public static class DriveProtocol
{
    public const string EtherCat = "ethercat";
    public const string Feetech = "feetech";
    public const string CanOpen = "canopen";
    public const string Esp32 = "esp32";
    public const string Stm32 = "stm32";
    /// <summary>Generieke CiA402-drive over EtherCAT (SDO's), zoals de ZeroErr eRob-actuatoren.</summary>
    public const string Coe = "coe";
    /// <summary>Eigen MDE-slave (vendor 0x000010E3: M-Pede-WJ1, stappenmotorsturing, Display v2, Gripper): standaardobjecten, SDO's, FoE, testrecept.</summary>
    public const string Mde = "mde";

    public static readonly (string Code, string Naam, string CodeLabel, string ParamBestand)[] Alle =
    {
        (EtherCat, "EtherCAT (Ingenia/Novanta)", "Productcode (hex)", ".xcf"),
        (Coe,      "EtherCAT CoE / eRob (CiA402)", "Productcode (hex, bv. 0x00029252)", ".json"),
        (Mde,      "EtherCAT MDE-slave (M-Pede)",  "Productcode (hex, uit het ESI)", ".json"),
        (Feetech,  "Feetech serieel (STS/SMS)",  "Model (bv. STS3215)", ".json"),
        (Esp32,    "ESP32 (USB-UART, esptool)",  "Chip voor esptool (auto, esp32s3, esp32c3 …)", ""),
        (Stm32,    "STM32 (ST-LINK)",            "pyocd-target (bv. stm32g474retx; leeg = herkennen)", ""),
        (CanOpen,  "CANopen (later)",             "Productcode",        ".json"),
    };

    public static string Naam(string code) => Alle.FirstOrDefault(p => p.Code == code).Naam ?? code;

    /// <summary>ESP32/STM32: een chip die met een .hex/.bin geprogrammeerd wordt (geen parametersets).</summary>
    public static bool IsChip(string code) => code is Esp32 or Stm32;

    /// <summary>Zit op de EtherCAT-bus (uitlezen via de agent, drive-posities).</summary>
    public static bool OpBus(string code) => code is EtherCat or Coe or Mde;

    /// <summary>Praat met SDO's (JSON-parametersets, objectdictionary, losse SDO): eRob en MDE-slaves.</summary>
    public static bool ViaSdo(string code) => code is Coe or Mde;

    /// <summary>Welke firmwarebestanden bij dit protocol horen (voor het uploadvak).</summary>
    public static string FirmwareExtensies(string code) => code switch
    {
        Esp32 => ".hex/.bin",
        Stm32 => ".hex/.bin/.elf",
        Feetech => ".bin",
        Coe => "n.v.t. — alleen de versie, ter controle",
        Mde => ".bin (FoE; de eerste keer via USB/ST-LINK met hetzelfde bestand)",
        _ => ".lfu/.sfu",
    };
}

/// <summary>StoreTypes in de FileStore voor drive-bestanden (MasterID = firmware- of parameter-id).</summary>
public static class DriveStore
{
    public const int Firmware = 320;     // .lfu / .sfu, MasterID = DriveFirmwareID
    public const int Dictionary = 321;   // .xdf,        MasterID = DriveFirmwareID
    public const int Parameters = 322;   // .xcf,        MasterID = DriveParameterID
    public const int Esi = 323;          // ESI .xml,    MasterID = DriveFirmwareID
}

public static class DriveStatus
{
    public const string Actueel = "actueel";
    public const string Toegestaan = "toegestaan";
    public const string Geblokkeerd = "geblokkeerd";

    public static readonly (string Code, string Tekst)[] Firmware =
    {
        (Actueel, "actueel — nieuwe prints krijgen deze"),
        (Toegestaan, "toegestaan — mag blijven zitten"),
        (Geblokkeerd, "geblokkeerd — moet vervangen worden"),
    };

    public static readonly (string Code, string Tekst)[] Parameters =
    {
        (Actueel, "actueel"),
        (Geblokkeerd, "geblokkeerd"),
    };

    public static string Uitleg(string status)
        => Firmware.FirstOrDefault(s => s.Code == status).Tekst ?? status;

    /// <summary>CSS-klasse voor het chipje.</summary>
    public static string Kleur(string status) => status switch
    {
        Actueel => "s-groen",
        Toegestaan => "s-geel",
        Geblokkeerd => "s-rood",
        _ => "s-grijs",
    };
}
