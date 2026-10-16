using EverythingApp.Models;

namespace EverythingApp.Services;

/// <summary>
/// De knop "Volgende ▸" van een bestelling (Bestellingen). Vervangt NextStatus_Click uit het Access-formulier
/// "Order main form"; daar was de goedkeuring nooit echt ingericht (de laatste ~1.400 orders sloegen 1 en 2 over).
/// Besluiten gebruiker (16-10-2026):
///   - Aanmaken → Volgende: werkorder verplicht. Totaal binnen de grens van de besteller (Werknemers.GoedkeurTarief)
///     of besteller is Directie → In bestelling; anders → Goedkeuring PM.
///   - De PM is de projectverantwoordelijke (Projects.ProjectResponsible) van het project van de werkorder. Is de
///     besteller zelf de PM, dan is zijn eigen grens al gecontroleerd en gaat het meteen naar de Directie.
///   - Goedkeuring PM → Volgende (alleen de PM of de Directie): binnen de grens van de PM (of PM is Directie) →
///     In bestelling; anders → Goedkeuring Directie.
///   - Goedkeuring Directie → Volgende (alleen Directie) → In bestelling.
///   - Vrij naar een status springen (de statusknoppen) alleen voor de Directie.
///   - Afkeuren (wie mag goedkeuren, met een reden): terug naar Aanmaken, reden in de interne opmerking, melding aan de
///     besteller.
/// Zonder COM of database, zodat de regels los te lezen en te toetsen zijn.
/// </summary>
public static class BestelFlow
{
    public const int Aanmaken = 0, GoedkeuringPm = 1, GoedkeuringDirectie = 2, InBestelling = 3, WachtOpLevering = 4, Gereed = 6;

    /// <summary>Waar een bestelling heen gaat als de besteller op Volgende drukt.</summary>
    public static int NaAanmaken(decimal totaal, Goedkeurder besteller, int? pmId)
    {
        if (besteller.Directie || totaal <= besteller.Tarief) return InBestelling;
        if (pmId is null || pmId == besteller.Id) return GoedkeuringDirectie;   // geen PM, of de besteller is de PM zelf
        return GoedkeuringPm;
    }

    /// <summary>Waar een bestelling heen gaat als de PM (of de Directie) goedkeurt.</summary>
    public static int NaPm(decimal totaal, Goedkeurder pm)
        => pm.Directie || totaal <= pm.Tarief ? InBestelling : GoedkeuringDirectie;

    /// <summary>Waarom deze persoon nu niet op Volgende mag drukken, of null als het wel mag.</summary>
    public static string? MagNiet(BestelFlowInfo info, Goedkeurder? ik)
    {
        if (ik is null) return "Je account is niet aan een werknemer gekoppeld.";
        return info.Status switch
        {
            GoedkeuringPm when !(ik.Directie || info.PmId == ik.Id)
                => $"Deze bestelling wacht op goedkeuring door {(info.PmNaam.Length > 0 ? info.PmNaam : "de projectmanager")}.",
            GoedkeuringDirectie when !ik.Directie => "Deze bestelling wacht op goedkeuring door de Directie.",
            WachtOpLevering => "Levering registreren staat nog in Access.",
            > WachtOpLevering => "Er is geen volgende stap.",
            _ => null,
        };
    }

    /// <summary>Mag deze persoon de bestelling nu afkeuren? Alleen in een goedkeurstap, en alleen wie daar mag goedkeuren.</summary>
    public static bool MagAfkeuren(BestelFlowInfo info, Goedkeurder? ik)
        => (info.Status is GoedkeuringPm or GoedkeuringDirectie) && MagNiet(info, ik) is null;

    /// <summary>Korte uitleg naast de knop: wat er gebeurt of waarop gewacht wordt.</summary>
    public static string Uitleg(BestelFlowInfo info) => info.Status switch
    {
        Aanmaken => "Lijst compleet? Volgende stuurt hem ter goedkeuring, of meteen naar In bestelling.",
        GoedkeuringPm => $"Wacht op goedkeuring door {(info.PmNaam.Length > 0 ? info.PmNaam : "de projectmanager")}.",
        GoedkeuringDirectie => "Wacht op goedkeuring door de Directie.",
        InBestelling => "Goedgekeurd. Na het bestellen bij de leverancier: Volgende.",
        WachtOpLevering => "Wacht op levering.",
        _ => "",
    };
}
