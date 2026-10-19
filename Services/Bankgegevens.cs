using System.Numerics;
using System.Text.RegularExpressions;

namespace EverythingApp.Services;

/// <summary>
/// Controles op IBAN en BIC, en de BIC van een Nederlandse IBAN afleiden uit de bankcode. Gebruikt om de bankgegevens
/// van een leverancier aan te vullen vanuit een uitgelezen factuur: een verkeerd gelezen IBAN mag er nooit in komen.
/// </summary>
public static class Bankgegevens
{
    public static string Schoon(string? s) => (s ?? "").Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

    /// <summary>Klopt het controlegetal van deze IBAN (ISO 13616, mod 97)?</summary>
    public static bool IbanGeldig(string? iban)
    {
        var i = Schoon(iban);
        if (i.Length < 15 || i.Length > 34 || !Regex.IsMatch(i, "^[A-Z]{2}[0-9]{2}[A-Z0-9]+$")) return false;
        if (i.StartsWith("NL") && i.Length != 18) return false;
        var omgezet = string.Concat((i[4..] + i[..4]).Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString()));
        return BigInteger.Parse(omgezet) % 97 == 1;
    }

    public static bool BicGeldig(string? bic) => Regex.IsMatch(Schoon(bic), "^[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?$");

    /// <summary>De grote Nederlandse banken: bankcode in de IBAN (positie 5-8) → BIC.</summary>
    private static readonly Dictionary<string, string> NlBanken = new()
    {
        ["ABNA"] = "ABNANL2A", ["INGB"] = "INGBNL2A", ["RABO"] = "RABONL2U", ["SNSB"] = "SNSBNL2A",
        ["ASNB"] = "ASNBNL21", ["RBRB"] = "RBRBNL21", ["TRIO"] = "TRIONL2U", ["KNAB"] = "KNABNL2H",
        ["BUNQ"] = "BUNQNL2A", ["FVLB"] = "FVLBNL22", ["HAND"] = "HANDNL2A", ["DEUT"] = "DEUTNL2A",
        ["NNBA"] = "NNBANL2G", ["BNPA"] = "BNPANL2A", ["COBA"] = "COBANL2X", ["ABNC"] = "ABNCNL2A",
    };

    /// <summary>BIC uit een (geldige) Nederlandse IBAN; null als de bank niet in de lijst staat of het geen NL-IBAN is.</summary>
    public static string? BicUitIban(string? iban)
    {
        var i = Schoon(iban);
        if (!i.StartsWith("NL") || !IbanGeldig(i)) return null;
        return NlBanken.TryGetValue(i[4..8], out var bic) ? bic : null;
    }
}
