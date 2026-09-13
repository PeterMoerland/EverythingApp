using UglyToad.PdfPig;

namespace EverythingApp.Services;

/// <summary>
/// Haalt de tekst uit een PDF.
///
/// Dit is de sleutel tot het uitlezen van facturen: bijna elke factuur die per
/// mail binnenkomt is een digitale PDF waarin de tekst letterlijk al staat. Dan
/// hoeft er niets herkend te worden en is de vraag alleen nog welk getal waar
/// hoort. Een ingescande factuur of een foto heeft die tekstlaag niet; die moet
/// door een model dat kan kijken.
/// </summary>
public static class PdfTekst
{
    /// <summary>
    /// De tekst van de eerste bladzijden. Geeft een lege tekst terug als er geen
    /// tekstlaag is — dan weet de beller dat het om een scan gaat.
    /// </summary>
    public static string Lees(byte[] pdf, int maxBladzijden = 4)
    {
        try
        {
            using var document = PdfDocument.Open(pdf);
            var samen = new System.Text.StringBuilder();

            var nummer = 0;
            foreach (var blad in document.GetPages())
            {
                if (++nummer > maxBladzijden) break;

                // Woorden in leesvolgorde: zo blijven bedragen bij hun label staan,
                // wat een model veel houvast geeft.
                foreach (var woord in blad.GetWords())
                {
                    samen.Append(woord.Text);
                    samen.Append(' ');
                }
                samen.AppendLine();
                samen.AppendLine();
            }

            return samen.ToString().Trim();
        }
        catch
        {
            // Beschadigd of met een wachtwoord beveiligd: dan behandelen we het als
            // een scan en laten we een kijkend model het proberen.
            return "";
        }
    }

    /// <summary>
    /// Is er genoeg tekst om zinnig uit te lezen? Een enkel woord of alleen
    /// paginanummers wijst op een scan.
    /// </summary>
    public static bool HeeftTekstlaag(string tekst)
        => tekst.Count(char.IsLetterOrDigit) >= 80;
}
