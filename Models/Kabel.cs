using System.Text.Json;
using System.Text.Json.Serialization;

namespace EverythingApp.Models;

// =====================================================================================================================
//  Kabelconfigurator (Tools ▸ Kabelconfigurator, wens gebruiker 13-11-2026): een kabel op maat samenstellen voor
//  de kabelleverancier LDZY (DongGuan LDZY Electronics). De spec wordt als JSON bewaard (tabel Kabelconfiguraties) en
//  getekend door Services/KabelTekening.cs in de opmaak van de tekeningen van LDZY; W2464-08-G020007 (een kabel die ze al
//  leveren) is het voorbeeld waarmee een nieuwe kabel begint.
// =====================================================================================================================

public enum KabelEindSoort { Connector, OpenEind }

public enum KabelAfscherming { Geen, Folie, Vlecht, FolieEnVlecht }

/// <summary>Wat er tussen de uiteinden zit: een kabel (alle aders in één mantel) of losse aders zonder mantel.</summary>
public enum KabelUitvoering { Kabel, LosseAders }

/// <summary>Een kant van de kabel (P1 of P2): een connector of een open eind.</summary>
public class KabelEind
{
    public KabelEindSoort Soort { get; set; } = KabelEindSoort.Connector;
    /// <summary>Connectorfamilie, bv. "Molex Pico-Lock"; alleen tekst op de tekening.</summary>
    public string Familie { get; set; } = "";
    /// <summary>Steek in mm; bepaalt de letter in het LDZY-nummer (<see cref="KabelNummer.Steken"/>).</summary>
    public double Steek { get; set; } = 1.5;
    public int Pinnen { get; set; } = 8;
    /// <summary>Fabrikantnummer van de housing (het "originele" huis, zoals LDZY het noemt).</summary>
    public string Housing { get; set; } = "";
    public string Terminal { get; set; } = "";
    public bool Krimpkous { get; set; } = true;
    public string KrimpkousKleur { get; set; } = "rood";
    public int KrimpkousLengte { get; set; } = 25;
    /// <summary>Afstand van de connector tot het begin van de krimpkous, mm.</summary>
    public int KrimpkousAfstand { get; set; } = 25;
    /// <summary>Open eind: hoeveel mm mantel eraf.</summary>
    public int StripLengte { get; set; } = 15;
    /// <summary>Open eind: vertind, adereindhuls of blank.</summary>
    public string Afwerking { get; set; } = "vertind";

    [JsonIgnore] public bool IsConnector => Soort == KabelEindSoort.Connector;

    public KabelEind Kopie() => (KabelEind)MemberwiseClone();
}

/// <summary>
/// Een connectortype uit de eigen lijst (tabel KabelConnectoren, KabelConnectoren_MariaDB.sql): een regel per housing.
/// Kiezen in de configurator vult familie, steek, pinnen, housing en terminal van een kabeleind in.
/// </summary>
public record KabelConnector(int Id, string Familie, int Pinnen, double Steek, string Housing, string Terminal, string Opmerking)
{
    /// <summary>Tekst in de keuzelijst: familie, pinnen, steek en het housingnummer.</summary>
    public string Weergave
        => string.Join(" · ", new[]
        {
            Familie.Length > 0 ? Familie : "(geen familie)",
            Pinnen > 0 ? Pinnen + "P" : "",
            Steek > 0 ? Steek.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("nl-NL")) + " mm" : "",
            Housing,
        }.Where(t => t.Length > 0));

    /// <summary>De steek staat als tekst in de tabel; komma en punt mogen allebei ("1,5" of "1.5").</summary>
    public static double LeesSteek(string? tekst)
        => double.TryParse((tekst ?? "").Trim().Replace(',', '.').Replace("mm", "", StringComparison.OrdinalIgnoreCase).Trim(),
                           System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0 ? s : 0;

    /// <summary>Is dit het type van dit kabeleind? Zelfde housingnummer, of (zonder nummer) zelfde familie en pinnen.</summary>
    public bool PastOp(KabelEind e)
        => Housing.Length > 0
            ? string.Equals(Housing, e.Housing.Trim(), StringComparison.OrdinalIgnoreCase)
            : string.Equals(Familie, e.Familie.Trim(), StringComparison.OrdinalIgnoreCase) && Pinnen == e.Pinnen;

    public void ZetOp(KabelEind e)
    {
        e.Soort = KabelEindSoort.Connector;
        e.Familie = Familie;
        if (Steek > 0) e.Steek = Steek;
        if (Pinnen > 0) e.Pinnen = Pinnen;
        e.Housing = Housing;
        e.Terminal = Terminal;
    }
}

/// <summary>Een ader: kleur, op welke pin hij aan elke kant zit en met welke ader hij getwist is.</summary>
public class KabelAder
{
    public int Nummer { get; set; }
    public string Kleur { get; set; } = "";
    public int? PinP1 { get; set; }
    public int? PinP2 { get; set; }
    /// <summary>Paarnummer: aders met hetzelfde paarnummer zijn getwist; null = los.</summary>
    public int? Paar { get; set; }
}

public class KabelSpec
{
    public int Id { get; set; }
    public string Naam { get; set; } = "";
    public string Leverancier { get; set; } = "LDZY";
    /// <summary>De laatste twee cijfers van het LDZY-nummer; geeft LDZY zelf uit. 0 = nog niet bekend (xx).</summary>
    public int Volgnummer { get; set; }
    /// <summary>Ons eigen artikelnummer, als dat er al is.</summary>
    public string EigenNummer { get; set; } = "";
    public int Lengte { get; set; } = 200;
    public int LengteTolerantie { get; set; } = 10;
    public int Aantal { get; set; } = 10;

    /// <summary>Kabel (aders in één mantel) of losse aders. Losse aders hebben geen mantel, afscherming of krimpkous;
    /// DraadType/AWG/opbouw gelden dan per ader (bv. UL1007 24AWG). Ontbreekt in oudere opgeslagen kabels: dan Kabel.</summary>
    public KabelUitvoering Uitvoering { get; set; } = KabelUitvoering.Kabel;

    /// <summary>De aders vastlijmen aan de connector (trekontlasting), aan elke kant waar een connector zit. Eén instelling
    /// voor de hele kabel (wens gebruiker 13-11-2026); komt als eis en als gele markering op de tekening.</summary>
    public bool AdersLijmen { get; set; }

    public string DraadType { get; set; } = "UL2464";
    public int Awg { get; set; } = 28;
    /// <summary>Opbouw van een ader, bv. 7/0.12 (7 draadjes van 0,12 mm).</summary>
    public string Draadopbouw { get; set; } = "7/0.12";
    public KabelAfscherming Afscherming { get; set; } = KabelAfscherming.FolieEnVlecht;
    public double Buitendiameter { get; set; } = 4.2;
    public string Mantelkleur { get; set; } = "zwart";

    public KabelEind P1 { get; set; } = new();
    public KabelEind P2 { get; set; } = new();
    public List<KabelAder> Aders { get; set; } = new();
    /// <summary>Eisen voor de leverancier, in het Engels (komen zo op de tekening).</summary>
    public List<string> Eisen { get; set; } = new();
    public string Opmerkingen { get; set; } = "";

    public DateTime? GemaaktOp { get; set; }
    public string GemaaktDoor { get; set; } = "";
    public DateTime? GewijzigdOp { get; set; }
    public string GewijzigdDoor { get; set; } = "";

    [JsonIgnore] public string LeveranciersNummer => KabelNummer.Maak(this);

    [JsonIgnore] public bool IsLosseAders => Uitvoering == KabelUitvoering.LosseAders;

    /// <summary>Komt er aan deze kant een krimpkous? Alleen om een kabel; om losse aders niet.</summary>
    public bool HeeftKous(KabelEind e) => e.Krimpkous && !IsLosseAders;

    /// <summary>Aantal getwiste paren (paarnummers met precies twee aders).</summary>
    [JsonIgnore] public int Paren => Aders.Where(a => a.Paar is not null).GroupBy(a => a.Paar).Count(g => g.Count() == 2);

    public static readonly string[] StandaardEisen =
    {
        "Terminals must not come loose from the wire (pull test).",
        "100% electrical test: no short circuit, open circuit or miswiring.",
        "Dimensions according to this drawing.",
        "All materials RoHS compliant.",
    };

    /// <summary>Aders bijmaken of weghalen; nieuwe aders krijgen pin = adernummer aan beide kanten.</summary>
    public void ZetAantalAders(int n)
    {
        n = Math.Clamp(n, 1, 60);
        while (Aders.Count > n) Aders.RemoveAt(Aders.Count - 1);
        while (Aders.Count < n)
        {
            var nr = Aders.Count + 1;
            Aders.Add(new KabelAder { Nummer = nr, Kleur = "", PinP1 = nr, PinP2 = nr });
        }
        Hernummer();
    }

    public void VerwijderAder(KabelAder a) { Aders.Remove(a); Hernummer(); }

    private void Hernummer() { for (var i = 0; i < Aders.Count; i++) Aders[i].Nummer = i + 1; }

    /// <summary>P1 pin n naar P2 pin n.</summary>
    public void PinnenEenOpEen()
    {
        foreach (var a in Aders) { a.PinP1 = PinOf(P1, a.Nummer); a.PinP2 = PinOf(P2, a.Nummer); }
    }

    /// <summary>P1 pin 1 naar de laatste pin van P2 enzovoort (gespiegeld).</summary>
    public void PinnenOmgekeerd()
    {
        foreach (var a in Aders)
        {
            a.PinP1 = PinOf(P1, a.Nummer);
            a.PinP2 = P2.IsConnector && a.Nummer <= P2.Pinnen ? P2.Pinnen + 1 - a.Nummer : null;
        }
    }

    private static int? PinOf(KabelEind e, int nummer) => e.IsConnector && nummer <= e.Pinnen ? nummer : null;

    /// <summary>Paren 1-2, 3-4, … ; een oneven laatste ader blijft los.</summary>
    public void ParenAutomatisch()
    {
        for (var i = 0; i < Aders.Count; i++) Aders[i].Paar = i + 1 < Aders.Count || i % 2 == 1 ? i / 2 + 1 : null;
    }

    public void ParenWissen() { foreach (var a in Aders) a.Paar = null; }

    private static readonly JsonSerializerOptions JsonOpties = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string NaarJson() => JsonSerializer.Serialize(this, JsonOpties);

    public static KabelSpec? UitJson(string json)
    {
        try { return JsonSerializer.Deserialize<KabelSpec>(json, JsonOpties); }
        catch (JsonException) { return null; }
    }

    /// <summary>Een losse kopie (nieuw, zonder Id en zonder volgnummer) via JSON.</summary>
    public KabelSpec Kopie()
    {
        var k = UitJson(NaarJson()) ?? new KabelSpec();
        k.Id = 0; k.Volgnummer = 0; k.GemaaktOp = null; k.GemaaktDoor = ""; k.GewijzigdOp = null; k.GewijzigdDoor = "";
        k.Naam = Naam.Length > 0 ? Naam + " (kopie)" : "";
        return k;
    }

    /// <summary>De kabel die LDZY al levert: tekening W2464-08-G020007 (UL2464 28AWG 4 paar afgeschermd, 2x Molex Pico-Lock 8P, 200 mm).
    /// Housing 5040510801 is een Pico-Lock (1,5 mm); de eerste proefopzet noemde hem per abuis Micro-Lock Plus.</summary>
    public static KabelSpec Voorbeeld()
    {
        var k = new KabelSpec
        {
            Naam = "UL2464 4 paar afgeschermd, 2x Pico-Lock 8P, 200 mm",
            Volgnummer = 7,
            Lengte = 200, LengteTolerantie = 10, Aantal = 10,
            DraadType = "UL2464", Awg = 28, Draadopbouw = "7/0.12",
            Afscherming = KabelAfscherming.FolieEnVlecht, Buitendiameter = 4.2, Mantelkleur = "zwart",
            P1 = new KabelEind { Familie = "Molex Pico-Lock", Steek = 1.5, Pinnen = 8, Housing = "5040510801", Terminal = "5040520098" },
            Eisen = StandaardEisen.ToList(),
        };
        k.P2 = k.P1.Kopie();
        var kleuren = new[] { "wit", "zwart", "groen", "zwart", "rood", "zwart", "geel", "zwart" };
        for (var i = 0; i < kleuren.Length; i++)
            k.Aders.Add(new KabelAder { Nummer = i + 1, Kleur = kleuren[i], PinP1 = i + 1, PinP2 = i + 1, Paar = i / 2 + 1 });
        return k;
    }

    /// <summary>Een lege kabel om zelf op te bouwen: 2 aders, open eind aan P2.</summary>
    public static KabelSpec Leeg()
    {
        var k = new KabelSpec { Naam = "", Volgnummer = 0, Aantal = 10, Eisen = StandaardEisen.ToList() };
        k.P2.Soort = KabelEindSoort.OpenEind;
        k.ZetAantalAders(2);
        return k;
    }
}

/// <summary>
/// Het nummerschema van LDZY, uit hun tekening: W (elektronische kabel) 2464 (draadtype UL2464) -08 (pinnen) -G (steek)
/// 0200 (lengte in mm) 07 (volgnummer, door LDZY uitgegeven) = W2464-08-G020007.
/// </summary>
public static class KabelNummer
{
    /// <summary>Steekcodes van LDZY: letter en steek in mm.</summary>
    public static readonly (char Letter, double Steek)[] Steken =
    {
        ('A', 0.5), ('B', 0.8), ('C', 1.0), ('D', 1.2), ('E', 1.25), ('F', 1.27), ('G', 1.5),
        ('H', 2.0), ('I', 2.5), ('J', 2.54), ('K', 3.0), ('L', 3.96), ('M', 4.2), ('P', 5.08),
    };

    public static char? Steekletter(double steek)
    {
        foreach (var (letter, s) in Steken) if (Math.Abs(s - steek) < 0.005) return letter;
        return null;
    }

    /// <summary>Het eind dat het nummer bepaalt: P1 als dat een connector is, anders P2.</summary>
    public static KabelEind Bepalend(KabelSpec k) => k.P1.IsConnector || !k.P2.IsConnector ? k.P1 : k.P2;

    public static string Draadcode(KabelSpec k)
    {
        var cijfers = new string(k.DraadType.Where(char.IsDigit).ToArray());
        return cijfers.Length > 0 ? cijfers : "XXXX";
    }

    public static int Pinnen(KabelSpec k)
    {
        var n = Math.Max(k.P1.IsConnector ? k.P1.Pinnen : 0, k.P2.IsConnector ? k.P2.Pinnen : 0);
        return n > 0 ? n : k.Aders.Count;
    }

    public static string Steekcode(KabelSpec k)
    {
        var e = Bepalend(k);
        return e.IsConnector ? (Steekletter(e.Steek)?.ToString() ?? "?") : "X";
    }

    public static string Volgcode(KabelSpec k) => k.Volgnummer > 0 ? Math.Clamp(k.Volgnummer, 1, 99).ToString("00") : "xx";

    public static string Maak(KabelSpec k)
        => $"W{Draadcode(k)}-{Pinnen(k):00}-{Steekcode(k)}{Math.Clamp(k.Lengte, 0, 9999):0000}{Volgcode(k)}";

    /// <summary>De zes delen van het nummer met hun uitleg (voor de tekening).</summary>
    public static (string Deel, string Uitleg)[] Delen(KabelSpec k)
    {
        var e = Bepalend(k);
        var steek = e.IsConnector ? e.Steek.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " mm" : "no connector";
        return new[]
        {
            ("W", "cable assembly (LDZY category)"),
            (Draadcode(k), "wire type " + k.DraadType),
            ("-" + Pinnen(k).ToString("00"), "number of pins"),
            ("-" + Steekcode(k), "connector pitch " + steek + " (A 0.5, B 0.8, C 1.0, D 1.2, E 1.25, F 1.27, G 1.5, H 2.0, I 2.5, J 2.54, K 3.0, L 3.96, M 4.2, P 5.08)"),
            (Math.Clamp(k.Lengte, 0, 9999).ToString("0000"), $"length {k.Lengte} mm"),
            (Volgcode(k), k.Volgnummer > 0 ? "serial number, assigned by LDZY" : "serial number, to be assigned by LDZY"),
        };
    }
}

/// <summary>Aderkleuren: Nederlandse naam in het scherm, kleur en Engelse naam op de tekening.</summary>
public static class KabelKleuren
{
    private static readonly Dictionary<string, (string Hex, string Engels)> Bekend = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wit"] = ("#f4f4f4", "white"), ["zwart"] = ("#1a1a1a", "black"), ["rood"] = ("#d9262c", "red"),
        ["groen"] = ("#1d9a3a", "green"), ["geel"] = ("#f2d21b", "yellow"), ["blauw"] = ("#1f5fd0", "blue"),
        ["bruin"] = ("#7b4a1e", "brown"), ["oranje"] = ("#f08a1d", "orange"), ["grijs"] = ("#8a8a8a", "grey"),
        ["paars"] = ("#7b3fa0", "purple"), ["roze"] = ("#e87ab0", "pink"), ["transparant"] = ("#cfd8e3", "clear"),
        ["white"] = ("#f4f4f4", "white"), ["black"] = ("#1a1a1a", "black"), ["red"] = ("#d9262c", "red"),
        ["green"] = ("#1d9a3a", "green"), ["yellow"] = ("#f2d21b", "yellow"), ["blue"] = ("#1f5fd0", "blue"),
        ["brown"] = ("#7b4a1e", "brown"), ["orange"] = ("#f08a1d", "orange"), ["grey"] = ("#8a8a8a", "grey"),
        ["purple"] = ("#7b3fa0", "purple"), ["pink"] = ("#e87ab0", "pink"),
    };

    public static string Hex(string? naam) => naam is not null && Bekend.TryGetValue(naam.Trim(), out var k) ? k.Hex : "#9aa0a6";

    public static string Engels(string? naam)
    {
        var n = (naam ?? "").Trim();
        return Bekend.TryGetValue(n, out var k) ? k.Engels : (n.Length > 0 ? n : "-");
    }

    /// <summary>De Nederlandse namen, voor de keuzelijst in het scherm.</summary>
    public static readonly string[] Namen = { "wit", "zwart", "rood", "groen", "geel", "blauw", "bruin", "oranje", "grijs", "paars", "roze", "transparant" };
}
