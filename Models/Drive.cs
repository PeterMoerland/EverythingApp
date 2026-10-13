namespace EverythingApp.Models;

/// <summary>Een drivetype (Everest, Capitan …), herkend aan de EtherCAT-productcode.</summary>
public class DriveSoort
{
    public int Id { get; set; }
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

    public static readonly (string Code, string Naam, string CodeLabel, string ParamBestand)[] Alle =
    {
        (EtherCat, "EtherCAT (Ingenia/Novanta)", "Productcode (hex)", ".xcf"),
        (Coe,      "EtherCAT CoE / eRob (CiA402)", "Productcode (hex, bv. 0x00029252)", ".json"),
        (Feetech,  "Feetech serieel (STS/SMS)",  "Model (bv. STS3215)", ".json"),
        (Esp32,    "ESP32 (USB-UART, esptool)",  "Chip voor esptool (auto, esp32s3, esp32c3 …)", ""),
        (Stm32,    "STM32 (ST-LINK)",            "pyocd-target (bv. stm32g474retx; leeg = herkennen)", ""),
        (CanOpen,  "CANopen (later)",             "Productcode",        ".json"),
    };

    public static string Naam(string code) => Alle.FirstOrDefault(p => p.Code == code).Naam ?? code;

    /// <summary>ESP32/STM32: een chip die met een .hex/.bin geprogrammeerd wordt (geen parametersets).</summary>
    public static bool IsChip(string code) => code is Esp32 or Stm32;

    /// <summary>Zit op de EtherCAT-bus (uitlezen via de agent, drive-posities).</summary>
    public static bool OpBus(string code) => code is EtherCat or Coe;

    /// <summary>Welke firmwarebestanden bij dit protocol horen (voor het uploadvak).</summary>
    public static string FirmwareExtensies(string code) => code switch
    {
        Esp32 => ".hex/.bin",
        Stm32 => ".hex/.bin/.elf",
        Feetech => ".bin",
        Coe => "n.v.t. — alleen de versie, ter controle",
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
