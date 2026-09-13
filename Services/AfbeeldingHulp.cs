namespace EverythingApp.Services;

/// <summary>
/// Maakt van een databaseveld een afbeelding die de browser kan tonen.
///
/// Velden die uit Access komen (OLE Object) bevatten een omhulsel vóór de echte
/// afbeelding. Soms zit daar een PNG of JPEG in, maar vaak een ruwe bitmap
/// (DIB) zónder bestandskop — die moet eerst aangevuld worden, anders toont een
/// browser niets. Beide gevallen worden hier afgehandeld.
/// </summary>
public static class AfbeeldingHulp
{
    public static string? DataAdres(object? waarde)
    {
        if (waarde is string tekst)
            return tekst.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? tekst : null;

        var ontleed = Ontleed(waarde);
        return ontleed is null
            ? null
            : $"data:{ontleed.Value.Mime};base64,{Convert.ToBase64String(ontleed.Value.Bytes)}";
    }

    /// <summary>Haalt een toonbare afbeelding uit een databaseveld.</summary>
    public static (byte[] Bytes, string Mime)? Ontleed(object? waarde)
    {
        if (waarde is not byte[] b || b.Length < 16) return null;

        // 1. Bestandsformaten met een betrouwbare kop
        var png = Zoek(b, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        if (png >= 0) return (b[png..], "image/png");

        var jpg = ZoekJpeg(b);
        if (jpg >= 0) return (b[jpg..], "image/jpeg");

        var gif = Zoek(b, new byte[] { 0x47, 0x49, 0x46, 0x38 });
        if (gif >= 0) return (b[gif..], "image/gif");

        // 2. Complete BMP (met 'BM'-kop) — alleen als de kop klopt
        var bmp = ZoekBmp(b);
        if (bmp >= 0) return (b[bmp..], "image/bmp");

        // 3. Ruwe bitmap zonder bestandskop (typisch voor Access-OLE):
        //    we zetten er zelf een BMP-kop voor.
        var dib = ZoekDib(b);
        if (dib >= 0)
        {
            var gemaakt = BouwBmp(b, dib);
            if (gemaakt is not null) return (gemaakt, "image/bmp");
        }

        return null;
    }

    /// <summary>Korte beschrijving van wat er in het veld zit — voor diagnose op het scherm.</summary>
    public static string Beschrijf(object? waarde)
    {
        if (waarde is null) return "leeg";
        if (waarde is string s) return $"tekst ({s.Length} tekens)";
        if (waarde is not byte[] b) return waarde.GetType().Name;

        var kop = string.Join(" ", b.Take(16).Select(x => x.ToString("X2")));
        var soort = Ontleed(waarde) is null ? "geen bekende afbeelding" : "afbeelding gevonden";

        // WMF/EMF komen uit oudere Access-versies en kan een browser niet tonen
        if (Zoek(b, new byte[] { 0x01, 0x00, 0x00, 0x00 }) is >= 0 and < 200 &&
            Zoek(b, new byte[] { 0x20, 0x45, 0x4D, 0x46 }) >= 0)
            soort = "EMF-tekening (browser kan dit niet tonen)";
        if (Zoek(b, new byte[] { 0xD7, 0xCD, 0xC6, 0x9A }) >= 0)
            soort = "WMF-tekening (browser kan dit niet tonen)";

        return $"{b.Length:N0} bytes, {soort} — begint met: {kop}";
    }

    // ---------- zoekhulpjes ----------

    private static int Zoek(byte[] bron, byte[] patroon, int maxStart = 8192)
    {
        var grens = Math.Min(bron.Length - patroon.Length, maxStart);
        for (var i = 0; i <= grens; i++)
        {
            var gelijk = true;
            for (var j = 0; j < patroon.Length; j++)
                if (bron[i + j] != patroon[j]) { gelijk = false; break; }
            if (gelijk) return i;
        }
        return -1;
    }

    /// <summary>JPEG: FF D8 FF gevolgd door een geldige markering.</summary>
    private static int ZoekJpeg(byte[] b)
    {
        var grens = Math.Min(b.Length - 4, 8192);
        for (var i = 0; i <= grens; i++)
            if (b[i] == 0xFF && b[i + 1] == 0xD8 && b[i + 2] == 0xFF && b[i + 3] >= 0xC0)
                return i;
        return -1;
    }

    /// <summary>
    /// 'BM' alléén is te kort om op te vertrouwen; daarom controleren we of de
    /// bestandsgrootte in de kop ongeveer overeenkomt met wat er nog volgt.
    /// </summary>
    private static int ZoekBmp(byte[] b)
    {
        var grens = Math.Min(b.Length - 30, 8192);
        for (var i = 0; i <= grens; i++)
        {
            if (b[i] != 0x42 || b[i + 1] != 0x4D) continue;
            var grootte = Lees32(b, i + 2);
            var offset = Lees32(b, i + 10);
            var rest = b.Length - i;
            if (offset is >= 26 and <= 4096 && grootte > 60 && Math.Abs(grootte - rest) <= 1024)
                return i;
        }
        return -1;
    }

    /// <summary>Ruwe DIB: begint met een BITMAPINFOHEADER van 40 bytes.</summary>
    private static int ZoekDib(byte[] b)
    {
        var grens = Math.Min(b.Length - 40, 4096);
        for (var i = 0; i <= grens; i++)
        {
            if (Lees32(b, i) != 40) continue;
            var breedte = (int)Lees32(b, i + 4);
            var hoogte = (int)Lees32(b, i + 8);
            var bits = b[i + 14] | (b[i + 15] << 8);
            if (breedte is > 0 and <= 20000 && Math.Abs(hoogte) is > 0 and <= 20000
                && bits is 1 or 4 or 8 or 16 or 24 or 32)
                return i;
        }
        return -1;
    }

    /// <summary>Zet een BMP-bestandskop van 14 bytes voor een ruwe DIB.</summary>
    private static byte[]? BouwBmp(byte[] bron, int dibStart)
    {
        try
        {
            var dib = bron[dibStart..];
            var bits = dib[14] | (dib[15] << 8);
            var kleuren = (int)Lees32(dib, 32);
            if (kleuren == 0 && bits <= 8) kleuren = 1 << bits;

            var pixelOffset = 14 + 40 + kleuren * 4;
            var totaal = 14 + dib.Length;

            var uit = new byte[totaal];
            uit[0] = 0x42; uit[1] = 0x4D;                 // 'BM'
            Schrijf32(uit, 2, (uint)totaal);              // bestandsgrootte
            Schrijf32(uit, 10, (uint)pixelOffset);        // begin van de pixels
            Buffer.BlockCopy(dib, 0, uit, 14, dib.Length);
            return uit;
        }
        catch { return null; }
    }

    private static uint Lees32(byte[] b, int i) =>
        (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24));

    private static void Schrijf32(byte[] b, int i, uint v)
    {
        b[i] = (byte)v; b[i + 1] = (byte)(v >> 8); b[i + 2] = (byte)(v >> 16); b[i + 3] = (byte)(v >> 24);
    }
}
