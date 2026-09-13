using System.Globalization;

namespace EverythingApp.Services;

/// <summary>
/// Datums in Nederlandse notatie (dd-mm-jjjj), onafhankelijk van de browsertaal.
/// Ruim in wat het accepteert, zodat snel typen werkt.
/// </summary>
public static class DatumHulp
{
    /// <summary>
    /// Leest o.a.:  1-2-2026 · 01-02-2026 · 1/2/2026 · 1.2.2026 · 01022026 ·
    ///              1-2 (huidig jaar) · 1-2-26
    /// </summary>
    public static DateOnly? Parse(string? invoer)
    {
        if (string.IsNullOrWhiteSpace(invoer)) return null;
        var s = invoer.Trim().Replace('/', '-').Replace('.', '-').Replace(' ', '-');

        // 8 cijfers achter elkaar: ddmmjjjj
        if (s.Length == 8 && s.All(char.IsDigit))
            s = $"{s[..2]}-{s.Substring(2, 2)}-{s[4..]}";

        var delen = s.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (delen.Length is < 2 or > 3) return null;
        if (!int.TryParse(delen[0], out var dag) || !int.TryParse(delen[1], out var maand)) return null;

        var jaar = DateTime.Today.Year;
        if (delen.Length == 3)
        {
            if (!int.TryParse(delen[2], out jaar)) return null;
            if (jaar < 100) jaar += jaar < 70 ? 2000 : 1900;   // '26 -> 2026, '85 -> 1985
        }

        if (maand is < 1 or > 12 || jaar is < 1 or > 9999) return null;
        if (dag < 1 || dag > DateTime.DaysInMonth(jaar, maand)) return null;
        return new DateOnly(jaar, maand, dag);
    }

    /// <summary>dd-mm-jjjj, of leeg.</summary>
    public static string Tekst(DateOnly? d) =>
        d?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "";

    public static string Tekst(DateTime? d) =>
        d?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "";
}
