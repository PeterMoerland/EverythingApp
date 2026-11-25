using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZXing;
using ZXing.Common;

namespace EverythingApp.Services;

/// <summary>
/// Een QR-code (of een streepjescode) uit een foto lezen, bijvoorbeeld het serienummer op het label van een robot dat
/// met de telefoon gefotografeerd is (Tools ▸ Licenties, 25-11-2026). Met ZXing; de foto wordt met System.Drawing
/// geopend, dus dit werkt alleen op Windows (waar de app draait).
/// </summary>
public static class QrLezer
{
    /// <summary>Grootste zijde waarop we zoeken: een telefoonfoto is veel groter dan nodig en dan duurt het lang.</summary>
    private const int MaxZijde = 1800;

    /// <summary>De tekst van de eerste code in de foto; null als er geen te lezen is (of de foto niet te openen is).</summary>
    public static string? Lees(byte[] foto)
    {
        if (!OperatingSystem.IsWindows() || foto.Length == 0) return null;
        try { return LeesWindows(foto); }
        catch { return null; }
    }

    [SupportedOSPlatform("windows")]
    private static string? LeesWindows(byte[] foto)
    {
        using var stroom = new MemoryStream(foto);
        using var bron = new Bitmap(stroom);
        var schaal = Math.Min(1.0, (double)MaxZijde / Math.Max(bron.Width, bron.Height));
        int b = Math.Max(1, (int)(bron.Width * schaal)), h = Math.Max(1, (int)(bron.Height * schaal));
        using var beeld = new Bitmap(b, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(beeld)) g.DrawImage(bron, 0, 0, b, h);

        var data = beeld.LockBits(new Rectangle(0, 0, b, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels;
        try
        {
            pixels = new byte[b * h * 4];
            for (var y = 0; y < h; y++)       // per rij: de rij in het geheugen kan langer zijn dan de breedte
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * b * 4, b * 4);
        }
        finally { beeld.UnlockBits(data); }

        var lezer = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true, TryInverted = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.CODE_128 },
            },
        };
        var uitkomst = lezer.Decode(new RGBLuminanceSource(pixels, b, h, RGBLuminanceSource.BitmapFormat.BGRA32));
        var tekst = uitkomst?.Text?.Trim();
        return string.IsNullOrEmpty(tekst) ? null : tekst;
    }
}
