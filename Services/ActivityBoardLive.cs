namespace EverythingApp.Services;

/// <summary>
/// Samenwerken op een Activity board (spec §7): na elke wijziging meldt een scherm hier welk board veranderde,
/// met zijn eigen sleutel. De andere schermen op dat board laden het opnieuw, zonder verversen. Eén proces
/// (Blazor Server), dus een gewoon event volstaat.
/// </summary>
public class ActivityBoardLive
{
    /// <summary>(boardId, bron, naam van wie het deed). Bron = de sleutel van het scherm dat het veranderde.</summary>
    public event Action<int, Guid, string>? Gewijzigd;

    public void Meld(int boardId, Guid bron, string door)
    {
        try { Gewijzigd?.Invoke(boardId, bron, door); }
        catch { /* een scherm dat net sluit mag de rest niet tegenhouden */ }
    }
}
