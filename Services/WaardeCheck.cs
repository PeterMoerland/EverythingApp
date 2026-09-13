using System.Text.RegularExpressions;

namespace EverythingApp.Services;

/// <summary>
/// Vergelijkt de Value uit een Fusion-BOM ("4k7 1/4w", "100nF 100V X7R") met
/// de omschrijving van het onderdeel bij Mouser ("RES SMD 4.7K OHM 1% 0402").
/// Alleen weerstanden, condensatoren en spoelen hebben zo'n waarde; bij al
/// het andere (ICs, connectors) is het antwoord eerlijk 'niet te vergelijken'.
/// </summary>
public static class WaardeCheck
{
    /// <summary>Eenheid: 'R' = ohm, 'F' = farad, 'H' = henry.</summary>
    private sealed record Grootheid(double Waarde, char Eenheid);

    /// <summary>De genormaliseerde waarde uit een BOM-Value ("4k7 1/4w" →
    /// 4700, 'R'), of null. Eenheid: 'R' = ohm, 'F' = farad, 'H' = henry.
    /// Gebruikt door de Eurocircuits-standaardcheck in de kijker.</summary>
    public static (double Waarde, char Eenheid)? Waarde(string bomValue)
        => UitBomValue(bomValue) is { } g ? (g.Waarde, g.Eenheid) : null;

    /// <summary>
    /// De spanning uit een tekst als "100nF 100V X7R" of "50V", in volt;
    /// null als er geen spanning in staat. Bij meerdere telt de hoogste.
    /// </summary>
    public static double? SpanningUit(string tekst)
    {
        double? beste = null;
        foreach (Match m in Regex.Matches(tekst, @"(\d+(?:[.,]\d+)?)\s*(k|m)?V(?![A-Za-z])",
                                          RegexOptions.IgnoreCase))
        {
            var getal = double.Parse(m.Groups[1].Value.Replace(',', '.'),
                                     System.Globalization.CultureInfo.InvariantCulture);
            getal *= m.Groups[2].Value.ToLowerInvariant() switch
            {
                "k" => 1e3,
                "m" => 1e-3,
                _ => 1.0,
            };
            beste = beste is null ? getal : Math.Max(beste.Value, getal);
        }
        return beste;
    }

    /// <summary>
    /// Het vermogen uit een tekst als "4k7 1/4w", "10k 1/16W" of "62.5mW",
    /// in watt; null als er geen vermogen in staat.
    /// </summary>
    public static double? VermogenUit(string tekst)
    {
        // breukvorm: 1/4w, 1/16W
        var breuk = Regex.Match(tekst, @"(\d+)\s*/\s*(\d+)\s*W(?![A-Za-z])", RegexOptions.IgnoreCase);
        if (breuk.Success)
        {
            var noemer = double.Parse(breuk.Groups[2].Value,
                                      System.Globalization.CultureInfo.InvariantCulture);
            if (noemer > 0)
                return double.Parse(breuk.Groups[1].Value,
                                    System.Globalization.CultureInfo.InvariantCulture) / noemer;
        }

        // decimaal: 62.5mW, 100 mW, 0.125W — bij meerdere telt de hoogste
        double? beste = null;
        foreach (Match m in Regex.Matches(tekst, @"(\d+(?:[.,]\d+)?)\s*(m|k)?W(?![A-Za-z])",
                                          RegexOptions.IgnoreCase))
        {
            var getal = double.Parse(m.Groups[1].Value.Replace(',', '.'),
                                     System.Globalization.CultureInfo.InvariantCulture);
            getal *= m.Groups[2].Value.ToLowerInvariant() switch
            {
                "m" => 1e-3,
                "k" => 1e3,
                _ => 1.0,
            };
            beste = beste is null ? getal : Math.Max(beste.Value, getal);
        }
        return beste;
    }

    /// <summary>
    /// Stand: "ok" (waarde bevestigd), "anders" (wijkt af) of "onbekend"
    /// (niet te vergelijken). Tekst is wat de gebruiker in de kolom ziet.
    /// </summary>
    public static (string Stand, string Tekst) Vergelijk(string bomValue, string omschrijving)
    {
        var bom = UitBomValue(bomValue);
        if (bom is null) return ("onbekend", "geen waarde om te vergelijken");

        var gevonden = UitOmschrijving(omschrijving, bom);
        if (gevonden is null)
            return ("onbekend", $"geen {EenheidNaam(bom.Eenheid)} in de Mouser-omschrijving");

        var afwijking = Math.Abs(bom.Waarde - gevonden.Waarde)
                        / Math.Max(bom.Waarde, gevonden.Waarde);
        return afwijking <= 0.01
            ? ("ok", "✓ " + Netjes(gevonden))
            : ("anders", $"⚠ BOM zegt {Netjes(bom)}, Mouser zegt {Netjes(gevonden)}");
    }

    // ================================================================
    //  De BOM-Value ontleden
    // ================================================================

    private static Grootheid? UitBomValue(string value)
    {
        var token = value.Trim().Split(' ', '\t').FirstOrDefault() ?? "";
        if (token.Length == 0) return null;

        // Letter als decimaalteken: 4k7, 2R2, 1M2, 4n7
        var m = Regex.Match(token, @"^(\d+)([pnuµmkMR])(\d+)$");
        if (m.Success)
        {
            var getal = double.Parse(m.Groups[1].Value + "." + m.Groups[3].Value,
                                     System.Globalization.CultureInfo.InvariantCulture);
            return VanLetters(getal, m.Groups[2].Value, "");
        }

        // Getal + voorvoegsel + eenheid: 100nF, 2.2uF, 67uH, 10k, 1M, 220, 220R
        m = Regex.Match(token, @"^(\d+(?:[.,]\d+)?)(p|n|u|µ|m|k|M)?(F|H|R|Ω|ohm)?$",
                        RegexOptions.None);
        if (!m.Success)
            m = Regex.Match(token, @"^(\d+(?:[.,]\d+)?)(p|n|u|µ|m|k|M)?(f|h|r)?$");
        if (!m.Success) return null;

        var waarde = double.Parse(m.Groups[1].Value.Replace(',', '.'),
                                  System.Globalization.CultureInfo.InvariantCulture);
        return VanLetters(waarde, m.Groups[2].Value, m.Groups[3].Value);
    }

    /// <summary>Voorvoegsel + eenheidsletter naar een genormaliseerde grootheid.
    /// Zonder eenheidsletter: k/M/R wijst op een weerstand, p/n/u op een
    /// condensator (het gangbare BOM-gebruik); een kale 'm' is te dubbelzinnig.</summary>
    private static Grootheid? VanLetters(double getal, string voorvoegsel, string eenheid)
    {
        var e = eenheid.ToUpperInvariant() switch
        {
            "F" => 'F',
            "H" => 'H',
            "R" or "Ω" or "OHM" => 'R',
            _ => voorvoegsel switch
            {
                "k" or "M" or "R" or "" => 'R',
                "p" or "n" or "u" or "µ" => 'F',
                _ => '?',
            },
        };
        if (e == '?') return null;

        var factor = voorvoegsel switch
        {
            "p" => 1e-12,
            "n" => 1e-9,
            "u" or "µ" => 1e-6,
            "m" => e == 'R' ? 1e6 : 1e-3,   // 1m-weerstand bestaat niet; 10mH wel
            "k" => 1e3,
            "M" => e == 'R' ? 1e6 : 1e-3,
            "R" => 1.0,
            _ => 1.0,
        };
        return new Grootheid(getal * factor, e);
    }

    // ================================================================
    //  De Mouser-omschrijving afzoeken
    // ================================================================

    /// <summary>
    /// Zoekt in de omschrijving alle waardes met de eenheid van de BOM en
    /// geeft de best passende terug — zo wint "4.7K OHM" het van een
    /// toevallig ander getal in dezelfde tekst.
    /// </summary>
    private static Grootheid? UitOmschrijving(string omschrijving, Grootheid bom)
    {
        Grootheid? beste = null;
        var besteAfwijking = double.MaxValue;

        // De kandidaat die het dichtst bij de BOM-waarde ligt wint — zo wint
        // "4.7K OHM" het van een toevallig ander getal in dezelfde tekst.
        void Probeer(double waarde)
        {
            var kandidaat = new Grootheid(waarde, bom.Eenheid);
            var afwijking = Math.Abs(bom.Waarde - kandidaat.Waarde)
                            / Math.Max(bom.Waarde, kandidaat.Waarde);
            if (afwijking < besteAfwijking)
            {
                beste = kandidaat;
                besteAfwijking = afwijking;
            }
        }

        static double Getal(string tekst)
            => double.Parse(tekst.Replace(',', '.'),
                            System.Globalization.CultureInfo.InvariantCulture);

        // 1) Met eenheid erbij: "4.7K OHM", "0.1UF", "68UH", "100 nF"
        foreach (Match m in Regex.Matches(omschrijving,
                     @"(\d+(?:[.,]\d+)?)\s*(p|n|u|µ|m|k)?\s*(F|H|OHMS?|Ω)(?![A-Za-z])",
                     RegexOptions.IgnoreCase))
        {
            var eenheidLetter = char.ToUpperInvariant(m.Groups[3].Value[0]);
            var e = eenheidLetter is 'O' or 'Ω' ? 'R' : eenheidLetter;
            if (e != bom.Eenheid) continue;

            var factor = m.Groups[2].Value.ToLowerInvariant() switch
            {
                "p" => 1e-12,
                "n" => 1e-9,
                "u" or "µ" => 1e-6,
                "m" => e == 'R' ? 1e6 : 1e-3,   // in omschrijvingen (vaak hoofdletters)
                "k" => 1e3,
                _ => 1.0,
            };
            Probeer(Getal(m.Groups[1].Value) * factor);
        }

        // 2) Weerstanden zonder het woord OHM erbij — Mouser-omschrijvingen in
        //    het Nederlands zeggen bijvoorbeeld "… SMD 0402 232K 1% Lead Free".
        //    Alleen met een duidelijke k/M/R/Ω-letter, zodat 0402, X7R en 1%
        //    niet vals meedoen.
        if (beste is null && bom.Eenheid == 'R')
        {
            foreach (Match m in Regex.Matches(omschrijving,
                         @"(?<![\w.,])(\d+(?:[.,]\d+)?)\s?([kKMRΩ])(?!\w)"))
            {
                var factor = m.Groups[2].Value is "k" or "K" ? 1e3
                           : m.Groups[2].Value is "M" ? 1e6
                           : 1.0;
                Probeer(Getal(m.Groups[1].Value) * factor);
            }

            // en de schrijfwijze met de letter als decimaalteken: 4K7, 2R2, 1M2
            foreach (Match m in Regex.Matches(omschrijving,
                         @"(?<![\w.,])(\d+)([kKMR])(\d+)(?!\w)"))
            {
                var factor = m.Groups[2].Value is "k" or "K" ? 1e3
                           : m.Groups[2].Value is "M" ? 1e6
                           : 1.0;
                Probeer(Getal(m.Groups[1].Value + "." + m.Groups[3].Value) * factor);
            }
        }

        return beste;
    }

    // ================================================================
    //  Weergave
    // ================================================================

    private static string EenheidNaam(char e) => e switch
    {
        'R' => "weerstandswaarde",
        'F' => "capaciteit",
        'H' => "inductie",
        _ => "waarde",
    };

    /// <summary>4700/'R' → "4,7 kΩ"; 1e-7/'F' → "100 nF".</summary>
    private static string Netjes(Grootheid g)
    {
        var eenheid = g.Eenheid switch { 'R' => "Ω", 'F' => "F", 'H' => "H", _ => "?" };
        var (deler, voorvoegsel) = g.Waarde switch
        {
            >= 1e6 => (1e6, "M"),
            >= 1e3 => (1e3, "k"),
            >= 1 => (1.0, ""),
            >= 1e-3 => (1e-3, "m"),
            >= 1e-6 => (1e-6, "µ"),
            >= 1e-9 => (1e-9, "n"),
            _ => (1e-12, "p"),
        };
        var getal = g.Waarde / deler;
        return $"{getal:0.###} {voorvoegsel}{eenheid}";
    }
}
