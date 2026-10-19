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
        if (Lengtes.TryGetValue(i[..2], out var lengte) && i.Length != lengte) return false;
        var omgezet = string.Concat((i[4..] + i[..4]).Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString()));
        return BigInteger.Parse(omgezet) % 97 == 1;
    }

    /// <summary>Lengte van een IBAN per land (de gangbare landen; een onbekend land wordt alleen op mod 97 getoetst).</summary>
    private static readonly Dictionary<string, int> Lengtes = new()
    {
        ["NL"] = 18, ["BE"] = 16, ["DE"] = 22, ["FR"] = 27, ["LU"] = 20, ["GB"] = 22, ["IE"] = 22, ["AT"] = 20,
        ["CH"] = 21, ["DK"] = 18, ["SE"] = 24, ["NO"] = 15, ["FI"] = 18, ["ES"] = 24, ["IT"] = 27, ["PT"] = 25,
        ["PL"] = 28, ["CZ"] = 24, ["SK"] = 24, ["HU"] = 28, ["SI"] = 19, ["EE"] = 20, ["LT"] = 20, ["LV"] = 21,
    };

    /// <summary>
    /// Een gelezen IBAN die te lang is voor zijn land (de AI plakt er soms een nummer achter, bv. het KBO-nummer):
    /// het begin met de juiste lengte, maar alleen als dat het controlegetal haalt. Anders null.
    /// </summary>
    public static string? Ingekort(string? iban)
    {
        var i = Schoon(iban);
        if (i.Length < 2 || IbanGeldig(i) || !Lengtes.TryGetValue(i[..2], out var lengte) || i.Length <= lengte) return null;
        return IbanGeldig(i[..lengte]) ? i[..lengte] : null;
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
