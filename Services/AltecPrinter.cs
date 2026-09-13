using System.Net.Sockets;
using System.Text;

namespace EverythingApp.Services;

/// <summary>
/// Stuurt serienummerlabels rechtstreeks naar de Altec-labelprinter, zonder
/// driver of hulpsoftware: de printer luistert op het netwerk (poort 9100)
/// en krijgt kant-en-klare printercommando's toegestuurd.
///
/// De Altec ATP-printers zijn omgedoopte TSC-machines en spreken TSPL2;
/// de meeste verstaan daarnaast ZPL (Zebra). Welke taal we sturen staat in
/// appsettings ("Altec:Taal": "tspl" of "zpl") — werkt het ene niet
/// (label blijft leeg of er komt onzin uit), zet dan de andere aan.
///
/// Instellingen (appsettings "Altec"):
///   Adres      IP of hostnaam van de printer; leeg = knop onzichtbaar
///   Poort      9100 (raw printing, standaard op vrijwel elke labelprinter)
///   Taal       "tspl" (standaard) of "zpl"
///   Dpi        203 (ATP-30/300/3000) of 600 (ATP-600)
///   BreedteMm / HoogteMm   labelformaat
///   SpatieMm   ruimte tussen twee labels op de rol (gap), meestal 2
///   MetQr      true = naast de tekst ook een QR-code met het serienummer,
///              scanbaar met de telefoonscanner van de app
/// </summary>
public class AltecPrinter(IConfiguration config)
{
    private string Adres => config["Altec:Adres"] ?? "";
    private int Poort => int.TryParse(config["Altec:Poort"], out var p) ? p : 9100;
    private string Taal => (config["Altec:Taal"] ?? "tspl").Trim().ToLowerInvariant();
    private int Dpi => int.TryParse(config["Altec:Dpi"], out var d) ? d : 203;
    private double BreedteMm => double.TryParse(config["Altec:BreedteMm"], System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : 50;
    private double HoogteMm => double.TryParse(config["Altec:HoogteMm"], System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 25;
    private double SpatieMm => double.TryParse(config["Altec:SpatieMm"], System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 2;
    private bool MetQr => string.Equals(config["Altec:MetQr"], "true", StringComparison.OrdinalIgnoreCase);

    public bool Beschikbaar => Adres.Length > 0;

    /// <summary>Millimeters naar printerdots, afgerond.</summary>
    private int Dots(double mm) => (int)Math.Round(mm * Dpi / 25.4);

    /// <summary>Print het serienummerlabel, <paramref name="aantal"/> keer.</summary>
    public async Task PrintSerienummerAsync(string serienummer, int aantal = 1)
    {
        if (!Beschikbaar) throw new InvalidOperationException("Geen Altec-printer ingesteld (Altec:Adres).");
        serienummer = Schoon(serienummer);
        if (serienummer.Length == 0) throw new InvalidOperationException("Dit exemplaar heeft nog geen serienummer.");
        aantal = Math.Clamp(aantal, 1, 100);

        var commando = Taal == "zpl"
            ? MaakZpl(serienummer, aantal)
            : MaakTspl(serienummer, aantal);

        await VerstuurAsync(Encoding.UTF8.GetBytes(commando));
    }

    /// <summary>
    /// Alleen tekens die veilig in beide printertalen passen: aanhalingstekens,
    /// backslashes en de ZPL-stuurtekens ^ en ~ zouden het commando zelf breken.
    /// </summary>
    private static string Schoon(string tekst)
    {
        var sb = new StringBuilder(tekst.Length);
        foreach (var c in tekst.Trim())
            if (!char.IsControl(c) && c is not ('"' or '\\' or '^' or '~'))
                sb.Append(c);
        return sb.ToString();
    }

    // ================================================================
    //  TSPL2 — de moedertaal van de ATP-serie (TSC)
    // ================================================================

    private string MaakTspl(string sn, int aantal)
    {
        int breed = Dots(BreedteMm), hoog = Dots(HoogteMm);
        int marge = Dots(2);

        // Bitmapfont 3 is 16×24 dots per teken; de vermenigvuldiger kiezen we
        // zo groot mogelijk zonder dat het serienummer buiten het label loopt.
        const int charB = 16, charH = 24;
        var qrZijde = MetQr ? Math.Min(hoog - 2 * marge, Dots(18)) : 0;
        var tekstRuimte = breed - 2 * marge - (MetQr ? qrZijde + marge : 0);
        var mult = Math.Clamp(Math.Min(tekstRuimte / (charB * Math.Max(sn.Length, 1)),
                                       (hoog - 2 * marge) / charH), 1, 8);

        var tekstBreed = sn.Length * charB * mult;
        var tekstX = marge + (MetQr ? qrZijde + marge : 0) + Math.Max(0, (tekstRuimte - tekstBreed) / 2);
        var tekstY = Math.Max(marge, (hoog - charH * mult) / 2);

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("SIZE ").Append(BreedteMm.ToString(inv)).Append(" mm,")
          .Append(HoogteMm.ToString(inv)).AppendLine(" mm");
        sb.Append("GAP ").Append(SpatieMm.ToString(inv)).AppendLine(" mm,0 mm");
        sb.AppendLine("DIRECTION 1");
        sb.AppendLine("CODEPAGE UTF-8");
        sb.AppendLine("CLS");
        if (MetQr)
        {
            // celgrootte zo dat de QR ongeveer qrZijde hoog wordt (~25 cellen)
            var cel = Math.Clamp(qrZijde / 25, 2, 10);
            sb.Append("QRCODE ").Append(marge).Append(',').Append(Math.Max(marge, (hoog - qrZijde) / 2))
              .Append(",M,").Append(cel).Append(",A,0,\"").Append(sn).AppendLine("\"");
        }
        sb.Append("TEXT ").Append(tekstX).Append(',').Append(tekstY)
          .Append(",\"3\",0,").Append(mult).Append(',').Append(mult)
          .Append(",\"").Append(sn).AppendLine("\"");
        sb.Append("PRINT ").Append(aantal).AppendLine(",1");
        return sb.ToString();
    }

    // ================================================================
    //  ZPL — voor als het model toch Zebra-taal wil
    // ================================================================

    private string MaakZpl(string sn, int aantal)
    {
        int breed = Dots(BreedteMm), hoog = Dots(HoogteMm);
        int marge = Dots(2);

        var qrZijde = MetQr ? Math.Min(hoog - 2 * marge, Dots(18)) : 0;
        var tekstRuimte = breed - 2 * marge - (MetQr ? qrZijde + marge : 0);

        // Font 0 is schaalbaar; hoogte zo kiezen dat de tekst past
        // (een teken is grofweg 0,55 × zijn hoogte breed).
        var fontH = Math.Clamp(Math.Min(hoog - 2 * marge,
                                        (int)(tekstRuimte / (0.55 * Math.Max(sn.Length, 1)))), 20, 200);
        var tekstY = Math.Max(marge, (hoog - fontH) / 2);
        var tekstX = marge + (MetQr ? qrZijde + marge : 0);

        var sb = new StringBuilder();
        sb.Append("^XA^CI28");
        sb.Append("^PW").Append(breed);
        sb.Append("^LL").Append(hoog);
        if (MetQr)
        {
            var cel = Math.Clamp(qrZijde / 25, 2, 10);
            sb.Append("^FO").Append(marge).Append(',').Append(Math.Max(marge, (hoog - qrZijde) / 2))
              .Append("^BQN,2,").Append(cel)
              .Append("^FDLA,").Append(sn).Append("^FS");
        }
        sb.Append("^FO").Append(tekstX).Append(',').Append(tekstY)
          .Append("^FB").Append(tekstRuimte).Append(",1,0,C,0")
          .Append("^A0N,").Append(fontH).Append(",0")
          .Append("^FD").Append(sn).Append("^FS");
        sb.Append("^PQ").Append(aantal);
        sb.Append("^XZ");
        return sb.ToString();
    }

    // ================================================================
    //  Versturen: kale TCP naar poort 9100
    // ================================================================

    private async Task VerstuurAsync(byte[] bytes)
    {
        using var klant = new TcpClient();
        using var tijd = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await klant.ConnectAsync(Adres, Poort, tijd.Token);
            await using var stroom = klant.GetStream();
            await stroom.WriteAsync(bytes, tijd.Token);
            await stroom.FlushAsync(tijd.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"De labelprinter op {Adres}:{Poort} reageert niet. Staat hij aan en hangt hij aan het netwerk?");
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"Geen verbinding met de labelprinter op {Adres}:{Poort} ({ex.SocketErrorCode}).");
        }
    }
}
