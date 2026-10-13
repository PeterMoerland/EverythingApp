namespace EverythingApp.Services;

/// <summary>Bedragen en getallen uit een invoerveld lezen.</summary>
public static class Bedragen
{
    /// <summary>
    /// Een bedrag uit een invoerveld. Nederlandse notatie is de norm ("1.250,00"),
    /// maar geplakte tekst uit een PDF staat er soms Engels in ("1,250.00").
    /// Het láátste scheidingsteken is het decimaalteken; de rest is duizendtal.
    ///
    /// Blind de punt door een komma vervangen zou fataal zijn: van "1.250" wordt
    /// dan € 1,25 in plaats van € 1250.
    /// </summary>
    public static decimal? Lees(string tekst)
    {
        var s = tekst.Replace("€", "").Replace("%", "").Replace(" ", "").Replace(" ", "").Trim();
        if (s.Length == 0) return null;

        int komma = s.LastIndexOf(','), punt = s.LastIndexOf('.');

        if (komma >= 0 && punt >= 0)
            s = komma > punt ? s.Replace(".", "").Replace(',', '.')    // 1.250,00
                             : s.Replace(",", "");                      // 1,250.00
        else if (komma >= 0) s = s.Replace(',', '.');
        else if (punt >= 0)
        {
            // Alleen een punt: decimaalteken als er twee cijfers achter staan
            // ("12.50"), anders een duizendtalscheiding ("1.250").
            var achter = s.Length - punt - 1;
            if (achter == 3 && punt > 0) s = s.Replace(".", "");
        }

        return decimal.TryParse(s,
            System.Globalization.NumberStyles.AllowLeadingSign |
            System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
