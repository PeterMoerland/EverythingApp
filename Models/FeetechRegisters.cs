using System.Text.Json;
using System.Text.Json.Serialization;

namespace EverythingApp.Models;

/// <summary>
/// De registertabel van de Feetech STS3215 (zelfde als in de agent: taken/_feetech.py).
/// Gebruikt door de parameter-editor: welke registers bestaan, hoe groot, EEPROM of
/// SRAM, en wat de waarden betekenen.
/// </summary>
public record FeetechRegister(int Adres, string Naam, int Bytes, string Geheugen, string Rw, bool Signed,
                              int Min, int Max, string Uitleg = "");

public static class FeetechRegisters
{
    public static readonly FeetechRegister[] Sts3215 =
    {
        new(0,  "Firmware main version", 1, "eeprom", "r", false, 0, 255),
        new(1,  "Firmware sub version",  1, "eeprom", "r", false, 0, 255),
        new(3,  "Servo main version",    1, "eeprom", "r", false, 0, 255),
        new(4,  "Servo sub version",     1, "eeprom", "r", false, 0, 255),
        new(5,  "ID",                    1, "eeprom", "rw", false, 0, 253, "Bus-ID, uniek per servo"),
        new(6,  "Baud rate",             1, "eeprom", "rw", false, 0, 7, "0=1M, 1=500k, 2=250k, 3=128k, 4=115200, 5=76800, 6=57600, 7=38400"),
        new(7,  "Return delay",          1, "eeprom", "rw", false, 0, 254, "×2 µs"),
        new(8,  "Ack level",             1, "eeprom", "rw", false, 0, 1, "0 = alleen antwoord op READ, 1 = op alles"),
        new(9,  "Minimum angle",         2, "eeprom", "rw", true, -32766, 32766, "0..4095 = 0..360°"),
        new(11, "Maximum angle",         2, "eeprom", "rw", true, -32766, 32766, "0..4095 = 0..360°"),
        new(13, "Maximum temperature",   1, "eeprom", "rw", false, 0, 100, "°C"),
        new(14, "Maximum voltage",       1, "eeprom", "rw", false, 0, 254, "×0,1 V"),
        new(15, "Minimum voltage",       1, "eeprom", "rw", false, 0, 254, "×0,1 V"),
        new(16, "Maximum torque",        2, "eeprom", "rw", false, 0, 1000, "‰"),
        new(19, "Unloading condition",   1, "eeprom", "rw", false, 0, 255, "bitmasker foutvlaggen"),
        new(20, "LED alarm condition",   1, "eeprom", "rw", false, 0, 255, "bitmasker foutvlaggen"),
        new(21, "P",                     1, "eeprom", "rw", false, 0, 254),
        new(22, "D",                     1, "eeprom", "rw", false, 0, 254),
        new(23, "I",                     1, "eeprom", "rw", false, 0, 254),
        new(24, "Punch",                 2, "eeprom", "rw", false, 0, 1000),
        new(26, "CW dead band",          1, "eeprom", "rw", false, 0, 32),
        new(27, "CCW dead band",         1, "eeprom", "rw", false, 0, 32),
        new(28, "Maximum current",       2, "eeprom", "rw", false, 0, 511, "×6,5 mA"),
        new(30, "Resolution",            1, "eeprom", "rw", false, 1, 100),
        new(31, "Position offset",       2, "eeprom", "rw", true, -2047, 2047),
        new(33, "Run mode",              1, "eeprom", "rw", false, 0, 3, "0=positie, 1=snelheid (wiel), 2=PWM, 3=stap"),
        new(34, "Protective torque",     1, "eeprom", "rw", false, 0, 100, "%"),
        new(35, "Protection time",       1, "eeprom", "rw", false, 0, 254, "×10 ms"),
        new(36, "Overload torque",       1, "eeprom", "rw", false, 0, 100, "%"),
        new(38, "Current protection time", 1, "eeprom", "rw", false, 0, 254, "×10 ms"),
        new(40, "Torque enable",         1, "sram", "rw", false, 0, 1),
        new(41, "Acceleration",          1, "sram", "rw", false, 0, 254, "×100 stappen/s²"),
        new(42, "Target position",       2, "sram", "rw", true, -32766, 32766),
        new(46, "Running speed",         2, "sram", "rw", false, 0, 3400, "stappen/s"),
        new(48, "Torque limit",          2, "sram", "rw", false, 0, 1000, "‰"),
        new(55, "Lock",                  1, "sram", "rw", false, 0, 1, "1 = EEPROM vergrendeld (de agent regelt dit zelf)"),
        new(56, "Current position",      2, "sram", "r", true, -32766, 32766),
        new(58, "Current speed",         2, "sram", "r", true, -32766, 32766),
        new(60, "Current load",          2, "sram", "r", true, -1000, 1000),
        new(62, "Current voltage",       1, "sram", "r", false, 0, 254, "×0,1 V"),
        new(63, "Current temperature",   1, "sram", "r", false, 0, 100, "°C"),
        new(66, "Moving",                1, "sram", "r", false, 0, 1),
        new(69, "Current current",       2, "sram", "r", false, 0, 65535, "×6,5 mA"),
    };

    public static FeetechRegister? Zoek(int adres) => Sts3215.FirstOrDefault(r => r.Adres == adres);

    /// <summary>Alleen wat geprogrammeerd wordt: rw-registers in EEPROM (Lock uitgezonderd).</summary>
    public static IEnumerable<FeetechRegister> Programmeerbaar =>
        Sts3215.Where(r => r.Rw == "rw" && r.Geheugen == "eeprom");

    /// <summary>Fabrieksinstellingen van de STS3215 als startpunt (baud 4 = 115200).</summary>
    public static readonly Dictionary<int, int> Fabriek = new()
    {
        [5] = 1, [6] = 4, [7] = 250, [8] = 1, [9] = 0, [11] = 4095, [13] = 80, [14] = 254, [15] = 90, [16] = 1000,
        [19] = 44, [20] = 44, [21] = 32, [22] = 32, [23] = 0, [24] = 0, [26] = 0, [27] = 0, [28] = 500, [30] = 1,
        [31] = 0, [33] = 0, [34] = 20, [35] = 200, [36] = 80, [38] = 250,
    };
}

/// <summary>Het JSON-bestand zoals de agent het leest (voorbeelden/feetech-sts3215.json).</summary>
public class FeetechParameterset
{
    [JsonPropertyName("protocol")] public string Protocol { get; set; } = "feetech";
    [JsonPropertyName("model")] public string Model { get; set; } = "STS3215";
    [JsonPropertyName("endian")] public string Endian { get; set; } = "little";
    [JsonPropertyName("omschrijving")] public string Omschrijving { get; set; } = "";
    [JsonPropertyName("registers")] public List<FeetechParameterRegel> Registers { get; set; } = new();

    public static readonly JsonSerializerOptions Opties = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static FeetechParameterset? Lees(byte[] inhoud)
        => JsonSerializer.Deserialize<FeetechParameterset>(inhoud, Opties);

    public byte[] Schrijf()
    {
        Registers = Registers.OrderBy(r => r.Adres).ToList();
        return JsonSerializer.SerializeToUtf8Bytes(this, Opties);
    }

    /// <summary>Nieuwe set met de fabriekswaarden van alle programmeerbare registers.</summary>
    public static FeetechParameterset Sjabloon(string omschrijving) => new()
    {
        Omschrijving = omschrijving,
        Registers = FeetechRegisters.Programmeerbaar
            .Select(r => new FeetechParameterRegel { Adres = r.Adres, Naam = r.Naam, Waarde = FeetechRegisters.Fabriek.GetValueOrDefault(r.Adres) })
            .ToList(),
    };
}

public class FeetechParameterRegel
{
    [JsonPropertyName("adres")] public int Adres { get; set; }
    [JsonPropertyName("naam")] public string Naam { get; set; } = "";
    [JsonPropertyName("waarde")] public int Waarde { get; set; }
}
