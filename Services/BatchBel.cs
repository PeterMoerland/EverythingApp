namespace EverythingApp.Services;

/// <summary>
/// Het belletje van de orderbatch. Wie iets aan de batch wijzigt (scanner,
/// batchscherm) luidt de bel; elk open Orderbatch-scherm hoort hem en
/// herlaadt zichzelf. Er is geen polling nodig: de hele app draait in één
/// proces en Blazor heeft al een vaste verbinding met elke openstaande
/// pagina, dus de wijziging is binnen een tel overal zichtbaar.
/// </summary>
public sealed class BatchBel
{
    public event Action? Gewijzigd;

    public void Meld() => Gewijzigd?.Invoke();
}
