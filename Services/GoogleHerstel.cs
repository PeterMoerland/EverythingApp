namespace EverythingApp.Services;

/// <summary>De eigen Google-postbus is niet (meer) te openen: opnieuw aanmelden lost het op.</summary>
public class GoogleToegangException : InvalidOperationException
{
    public GoogleToegangException(string bericht) : base(bericht) { }
}

/// <summary>
/// Opnieuw aanmelden bij Google zonder je werk kwijt te raken (melding gebruiker 11-11-2026: na een update mislukte het
/// mailen, en na opnieuw inloggen was de mail weg). Per verbinding (scoped). Een scherm doet
/// <c>await Herstel.VoerUitAsync(() => Post.VerstuurAsync(...))</c>; mist de Google-toegang, dan toont
/// <c>GoogleHerstelVenster</c> (in de layout) "Opnieuw aanmelden". Dat aanmelden gebeurt in een pop-up, zodat het scherm
/// met de ingevulde mail blijft staan; daarna wordt de actie nog één keer uitgevoerd.
/// </summary>
public class GoogleHerstel
{
    private TaskCompletionSource<bool>? _wacht;

    /// <summary>Staat de vraag open?</summary>
    public bool Open => _wacht is { Task.IsCompleted: false };

    /// <summary>De reden die het venster toont.</summary>
    public string Reden { get; private set; } = "";

    /// <summary>Voor het venster: opnieuw tekenen.</summary>
    public event Action? Gewijzigd;

    /// <summary>Voert de actie uit; mist de Google-toegang, dan eerst opnieuw aanmelden (vraag) en dan nog één keer.</summary>
    public async Task VoerUitAsync(Func<Task> actie)
    {
        try { await actie(); }
        catch (GoogleToegangException ex)
        {
            if (!await VraagAanmeldenAsync(ex.Message)) throw;
            await actie();
        }
    }

    /// <summary>Toont het venster en wacht tot de gebruiker aangemeld is (true) of annuleert (false).</summary>
    public Task<bool> VraagAanmeldenAsync(string reden)
    {
        if (Open) return _wacht!.Task;
        Reden = reden;
        _wacht = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Gewijzigd?.Invoke();
        return _wacht.Task;
    }

    /// <summary>Door het venster: aangemeld (true) of geannuleerd (false).</summary>
    public void Klaar(bool aangemeld)
    {
        _wacht?.TrySetResult(aangemeld);
        Gewijzigd?.Invoke();
    }
}
