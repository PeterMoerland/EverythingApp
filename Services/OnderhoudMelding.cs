namespace EverythingApp.Services;

/// <summary>
/// Aankondiging van een nieuwe versie (wens gebruiker 27-10-2026): bij iedereen die de app open heeft verschijnt bovenin
/// een balk met aftelklok, zodat werk afgemaakt en opgeslagen kan worden voordat de service stopt. Gezet door
/// service-herstarten.bat (POST /onderhoud/aankondigen, alleen vanaf de server zelf) of met de hand in Beheer ▸ Nieuwe
/// versie aankondigen (Directie). Alleen in het geheugen: na de herstart is hij vanzelf weg; gebeurt er niets, dan
/// verdwijnt hij 10 minuten na het aangekondigde tijdstip.
/// </summary>
public sealed class OnderhoudMelding
{
    public sealed record Aankondiging(DateTime Om, string Tekst, string Door);

    private Aankondiging? _huidig;
    private int _schermen;

    /// <summary>De lopende aankondiging, of null.</summary>
    public Aankondiging? Huidig => _huidig is { } a && DateTime.Now < a.Om.AddMinutes(10) ? a : null;

    /// <summary>Hoeveel schermen nu open staan (elke balk telt zich aan en af).</summary>
    public int OpenSchermen => Volatile.Read(ref _schermen);

    public event Action? Gewijzigd;

    public Aankondiging Kondig(double minuten, string? tekst, string door)
    {
        var a = new Aankondiging(DateTime.Now.AddMinutes(Math.Clamp(minuten, 0, 120)), (tekst ?? "").Trim(), door);
        _huidig = a;
        Gewijzigd?.Invoke();
        return a;
    }

    public void TrekIn()
    {
        _huidig = null;
        Gewijzigd?.Invoke();
    }

    internal void SchermOpen() => Interlocked.Increment(ref _schermen);
    internal void SchermDicht() => Interlocked.Decrement(ref _schermen);
}
