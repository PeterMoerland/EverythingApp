namespace EverythingApp.Services;

/// <summary>Wat een knop met de PLC doet.</summary>
public enum KnopSoort
{
    /// <summary>Aan of uit, met de stand terug te lezen (verlichting).</summary>
    Schakelaar,
    /// <summary>Kort aantikken: even aan, dan weer uit (zonwering, alles uit).</summary>
    Puls,
}

/// <summary>
/// Eén bedienknop in het gebouwbeheer.
///
/// 'Tag' is de variabele waar de knop naartoe schrijft. 'StatusTag' is de aparte
/// terugmelding uit de PLC — bij de verlichting is dat een tweede variabele die
/// vertelt of de lamp werkelijk brandt. Is die leeg, dan wordt Tag zelf gelezen.
/// </summary>
public record Knop(string Naam, string Tag, KnopSoort Soort = KnopSoort.Schakelaar)
{
    public string StatusTag { get; init; } = "";

    /// <summary>Welke variabele de stand van de knop bepaalt.</summary>
    public string Terugmelding => string.IsNullOrWhiteSpace(StatusTag) ? Tag : StatusTag;

    /// <summary>Heeft deze knop een lampje? Alleen als er iets terug te lezen is.</summary>
    public bool HeeftLampje => Soort == KnopSoort.Schakelaar || !string.IsNullOrWhiteSpace(StatusTag);

    /// <summary>rood = uit/stop, groen = besparing; leeg is de gewone blauwe knop.</summary>
    public string Kleur { get; init; } = "";

    /// <summary>Extra uitleg onder de naam, bijvoorbeeld 'delayed off'.</summary>
    public string Toelichting { get; init; } = "";
}

/// <summary>Een kolom knoppen; komt overeen met een kolom op het HMI-scherm.</summary>
public record KnopKolom(string Naam, params Knop[] Knoppen);

/// <summary>
/// De indeling van de gebouwschermen, nagebouwd uit het cMT-paneel.
///
/// De tags hieronder zijn nog namen die ik verzonnen heb: zodra de echte
/// tagnamen of adressen uit de PLC bekend zijn, is dit het enige bestand dat
/// aangepast hoeft te worden. De schermen zelf veranderen niet mee.
/// </summary>
public static class Gebouw
{
    // ============================================================
    //  Verlichting
    // ============================================================
    /// <summary>
    /// Elke lamp heeft twee variabelen: het commando waar de knop naartoe schrijft
    /// en een _Status die vertelt of de lamp brandt. Overgenomen uit het cMT-project.
    ///
    /// De commando's zijn als puls uitgevoerd: de app tikt de bit even aan en de
    /// PLC doet de rest. Blijkt dat de PLC een vaste stand verwacht in plaats van
    /// een tikje, dan is dat hier KnopSoort.Schakelaar in plaats van Puls.
    /// </summary>
    public static readonly KnopKolom[] Verlichting =
    {
        new("",
            new Knop("Kantoor 1", "HMI_Light_Office1", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Office1_Status" },
            new Knop("Kantoor 2", "HMI_Light_Office2", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Office2_Status" },
            new Knop("Edwin en Peter", "HMI_Light_HQ", KnopSoort.Puls)
                { StatusTag = "HMI_Light_HQ_Status" },
            new Knop("Buiten", "HMI_Light_Outside", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Outside_Status", Toelichting = "delayed off" },
            new Knop("Vergaderruimte", "HMI_Light_Meetingroom", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Meetingroom_Status" },
            new Knop("Alles uit", "HMI_Light_All_Off", KnopSoort.Puls) { Kleur = "rood" }),

        new("",
            new Knop("Kantine", "HMI_Light_Canteen", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Canteen_Status" },
            new Knop("Werkplaats links", "HMI_Light_Workshop", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Workshop_Status" },
            new Knop("Werkplaats rechts", "HMI_Light_Workshop_Right", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Workshop_Right_Status" },
            new Knop("Slijphok", "HMI_Light_Grind", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Grind_Status" },
            new Knop("Elektrowerkplaats", "HMI_Light_Eworkshop", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Eworkshop_Status" },
            new Knop("Besparing werkplaats", "HMI_Light_Workshop_LowConsumption", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Workshop_LowConsumption_Status", Kleur = "groen" }),

        new("",
            new Knop("Hoogbouw", "HMI_Light_Hoogbouw", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Hoogbouw_Status" },
            new Knop("Lab", "HMI_Light_Lab", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Lab_Status" },
            new Knop("Balkon", "HMI_Light_Balcony", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Balcony_Status" },
            new Knop("Traphuis", "HMI_Light_Staircase", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Staircase_Status" },
            new Knop("Magazijn", "HMI_Light_Storage", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Storage_Status" },
            new Knop("Buiten voorzijde", "HMI_Light_Front_Outside", KnopSoort.Puls)
                { StatusTag = "HMI_Light_Front_Outside_Status" }),
    };

    /// <summary>De terugmeldingen van de verlichting, om in één keer uit te lezen.</summary>
    public static IEnumerable<string> VerlichtingTags =>
        Verlichting.SelectMany(k => k.Knoppen)
                   .Where(k => k.HeeftLampje)
                   .Select(k => k.Terugmelding)
                   .Distinct(StringComparer.OrdinalIgnoreCase);

    // ============================================================
    //  Zonwering
    //  In de PLC heten de variabelen Up1..Up6, Stop1..Stop6, Down1..Down6,
    //  Open1..Open6 en Close1..Close6.
    //
    //  Het zijn allemaal pulsen: de PLC krijgt een tikje en regelt zelf het
    //  op- en neerlopen.
    // ============================================================
    public record Zonwering(string Ruimte, int Nummer);

    /// <summary>
    /// De nummering loopt tegengesteld aan de volgorde op het scherm: links op het
    /// paneel staat Edwin, en dat is Up6/Stop6/Down6. Overgenomen uit het cMT-project.
    /// </summary>
    public static readonly Zonwering[] Zonweringen =
    {
        new("Edwin", 6),
        new("Peter", 5),
        new("Stilteruimte", 4),
        new("Leon", 3),
        new("Arie", 2),
        new("Tim", 1),
    };

    public record ZonweringKnop(string Naam, string TagDeel, string Kleur = "");

    public static readonly ZonweringKnop[] ZonweringCommandos =
    {
        new("Omhoog", "Up"),
        new("Stop", "Stop", "rood"),
        new("Omlaag", "Down"),
        new("Kantel open", "Open"),
        new("Kantel dicht", "Close"),
    };

    /// <summary>Tagnaam zoals hij in de PLC staat: Up1, Stop3, Close6.</summary>
    public static string ZonweringTag(Zonwering zonwering, ZonweringKnop commando)
        => commando.TagDeel + zonwering.Nummer;

    // ============================================================
    //  Compressor
    //  Een lampje dat toont of hij draait, en twee knoppen om buiten werktijd
    //  toch perslucht te hebben.
    // ============================================================
    public const string CompressorDraaitTag = "HMI_CompressorStatus";

    public static readonly Knop[] Compressor =
    {
        new("Start overwerk", "HMI_CompressorStartOvertime", KnopSoort.Puls),
        new("Stop overwerk", "HMI_CompressorStopOvertime", KnopSoort.Puls) { Kleur = "rood" },
    };
}
