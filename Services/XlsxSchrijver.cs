using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace EverythingApp.Services;

/// <summary>Opmaak van een kolom in <see cref="XlsxSchrijver"/>.</summary>
public enum XlsxSoort { Tekst, Getal, Geld, Datum }

/// <summary>
/// Een eenvoudig Excel-bestand (.xlsx) met één blad: een vetgedrukte kopregel en daaronder de rijen, getallen als echte
/// getallen (zodat Excel kan rekenen), geld als "€ #.##0,00". Met de OpenXml-bibliotheek die de app al gebruikt.
/// </summary>
public static class XlsxSchrijver
{
    // stijlindex in de stylesheet hieronder
    private const uint StijlKop = 1, StijlGetal = 2, StijlGeld = 3, StijlDatum = 4, StijlVet = 5, StijlVetGeld = 6;

    public record Kolom(string Kop, XlsxSoort Soort = XlsxSoort.Tekst, double Breedte = 14);

    /// <param name="vet">Rijen die vet moeten (bv. subtotalen), op index in <paramref name="rijen"/>.</param>
    public static byte[] Maak(string blad, IReadOnlyList<Kolom> kolommen, IReadOnlyList<object?[]> rijen, ISet<int>? vet = null)
    {
        using var geheugen = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(geheugen, SpreadsheetDocumentType.Workbook))
        {
            var boek = doc.AddWorkbookPart();
            boek.Workbook = new Workbook();
            var stijlen = boek.AddNewPart<WorkbookStylesPart>();
            stijlen.Stylesheet = Stylesheet();

            var deel = boek.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            var kolomInfo = new Columns(kolommen.Select((k, i) =>
                new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = k.Breedte, CustomWidth = true }));

            var kop = new Row { RowIndex = 1 };
            foreach (var k in kolommen) kop.Append(TekstCel(k.Kop, StijlKop));
            data.Append(kop);

            for (var r = 0; r < rijen.Count; r++)
            {
                var rij = new Row { RowIndex = (uint)(r + 2) };
                var isVet = vet?.Contains(r) == true;
                for (var c = 0; c < kolommen.Count; c++)
                {
                    var waarde = c < rijen[r].Length ? rijen[r][c] : null;
                    rij.Append(Cel(waarde, kolommen[c].Soort, isVet));
                }
                data.Append(rij);
            }

            deel.Worksheet = new Worksheet(
                new SheetViews(new SheetView { WorkbookViewId = 0, Pane = new Pane
                {
                    VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen,
                } }),
                kolomInfo, data);

            var bladen = boek.Workbook.AppendChild(new Sheets());
            bladen.Append(new Sheet { Id = boek.GetIdOfPart(deel), SheetId = 1, Name = Bladnaam(blad) });
            boek.Workbook.Save();
        }
        return geheugen.ToArray();
    }

    private static string Bladnaam(string naam)
    {
        foreach (var c in new[] { '\\', '/', '?', '*', '[', ']', ':' }) naam = naam.Replace(c, '-');
        return naam.Length > 31 ? naam[..31] : naam.Length == 0 ? "Blad1" : naam;
    }

    private static Cell TekstCel(string tekst, uint stijl = 0)
        => new() { DataType = CellValues.InlineString, StyleIndex = stijl, InlineString = new InlineString(new Text(tekst)) };

    private static Cell Cel(object? waarde, XlsxSoort soort, bool vet)
    {
        if (waarde is null) return new Cell();
        if (waarde is string s) return TekstCel(s, vet ? StijlVet : 0);
        if (soort == XlsxSoort.Datum && waarde is DateTime or DateOnly)
        {
            var d = waarde is DateOnly dOnly ? dOnly.ToDateTime(TimeOnly.MinValue) : (DateTime)waarde;
            return new Cell { StyleIndex = StijlDatum, CellValue = new CellValue(d.ToOADate().ToString(CultureInfo.InvariantCulture)) };
        }
        var getal = Convert.ToDecimal(waarde, CultureInfo.InvariantCulture);
        var stijl = soort == XlsxSoort.Geld ? (vet ? StijlVetGeld : StijlGeld) : soort == XlsxSoort.Getal ? StijlGetal : (vet ? StijlVet : 0u);
        return new Cell { StyleIndex = stijl, CellValue = new CellValue(getal.ToString(CultureInfo.InvariantCulture)) };
    }

    private static Stylesheet Stylesheet() => new(
        new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0.###" },
            new NumberingFormat { NumberFormatId = 165, FormatCode = "\"€\" #,##0.00;\"€\" -#,##0.00" },
            new NumberingFormat { NumberFormatId = 166, FormatCode = "dd-mm-yyyy" }) { Count = 3 },
        new Fonts(new Font(), new Font(new Bold())) { Count = 2 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
                  new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
        new Borders(new Border()) { Count = 1 },
        new CellFormats(
            new CellFormat(),                                                                 // 0 gewoon
            new CellFormat { FontId = 1, ApplyFont = true },                                  // 1 kop
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },                // 2 getal
            new CellFormat { NumberFormatId = 165, ApplyNumberFormat = true },                // 3 geld
            new CellFormat { NumberFormatId = 166, ApplyNumberFormat = true },                // 4 datum
            new CellFormat { FontId = 1, ApplyFont = true },                                  // 5 vet
            new CellFormat { FontId = 1, NumberFormatId = 165, ApplyFont = true, ApplyNumberFormat = true }) // 6 vet geld
        { Count = 7 });
}
