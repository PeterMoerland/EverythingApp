using System.Globalization;
using System.Reflection;

namespace EverythingApp.Services;

/// <summary>Een stem die Windows kan laten spreken.</summary>
public record StemInfo(string Naam, bool Nederlands);

/// <summary>
/// Tekst naar spraak met de stem van Windows zelf, zodat de tekst het netwerk niet verlaat (besluit gebruiker
/// 13-11-2026, voor de omroep op de Sonos-speakers). De Nederlandse stem (Microsoft Frank) is een "OneCore"-stem:
/// System.Speech ziet die niet, maar de oude SAPI-COM-objecten kunnen hem wel gebruiken als je de categorie
/// Speech_OneCore opgeeft. Daarom late binding op SAPI.SpVoice (geen extra pakket). De stem moet op de server
/// geïnstalleerd zijn (Instellingen ▸ Tijd en taal ▸ Spraak ▸ Stemmen toevoegen ▸ Nederlands).
/// Uitkomst: ruwe PCM, 22,05 kHz, 16 bit, mono (zie Wav.Maak in OmroepService.cs).
/// </summary>
public class WindowsStem
{
    public const int Hertz = 22050;

    private const string OneCore = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices";
    private const string Klassiek = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices";
    private const int Formaat22kHz16BitMono = 22;     // SpeechAudioFormatType.SAFT22kHz16BitMono
    private const int GeenXml = 16;                   // SpeechVoiceSpeakFlags.SVSFIsNotXML: de tekst letterlijk voorlezen

    private readonly string _voorkeur;
    private readonly int _tempo;
    private readonly SemaphoreSlim _slot = new(1, 1);

    public WindowsStem(IConfiguration config)
    {
        _voorkeur = (config["Omroep:Stem"] ?? "").Trim();
        _tempo = Math.Clamp(config.GetValue("Omroep:Tempo", 0), -10, 10);
    }

    /// <summary>De stemmen die Windows op deze computer heeft (OneCore en klassiek), Nederlandse eerst.</summary>
    public Task<List<StemInfo>> StemmenAsync() => OpEigenDraad(() =>
        Tokens().Select(t => new StemInfo(t.Naam, t.Nederlands)).DistinctBy(s => s.Naam)
                .OrderByDescending(s => s.Nederlands).ThenBy(s => s.Naam).ToList());

    /// <summary>De stem die gebruikt wordt: Omroep:Stem (stukje van de naam) als die er is, anders de eerste Nederlandse.</summary>
    public async Task<StemInfo?> GekozenAsync()
    {
        var alle = await StemmenAsync();
        return Kies(alle.Select(s => (s.Naam, s.Nederlands)).ToList()) is { } k ? new StemInfo(k.Naam, k.Nederlands) : null;
    }

    /// <summary>Spreekt de tekst uit; geeft ruwe PCM (22,05 kHz, 16 bit, mono).</summary>
    public async Task<byte[]> SpreekAsync(string tekst)
    {
        tekst = (tekst ?? "").Trim();
        if (tekst.Length == 0) throw new InvalidOperationException("Er is geen tekst om uit te spreken.");
        await _slot.WaitAsync();
        try
        {
            return await OpEigenDraad(() =>
            {
                var tokens = Tokens();
                var keuze = Kies(tokens.Select(t => (t.Naam, t.Nederlands)).ToList())
                    ?? throw new InvalidOperationException(
                        "Er is geen Nederlandse Windows-stem op de server. Voeg hem toe bij Instellingen ▸ Tijd en taal ▸ Spraak ▸ " +
                        "Stemmen toevoegen ▸ Nederlands, of zet in appsettings Omroep:Stem op (een stukje van) de naam van een andere stem.");
                var token = tokens.First(t => t.Naam == keuze.Naam).Token;

                var stem = Maak("SAPI.SpVoice");
                var stroom = Maak("SAPI.SpMemoryStream");
                Zet(Lees(stroom, "Format")!, "Type", Formaat22kHz16BitMono);
                ZetVerwijzing(stem, "Voice", token);
                ZetVerwijzing(stem, "AudioOutputStream", stroom);
                Zet(stem, "Rate", _tempo);
                Roep(stem, "Speak", tekst, GeenXml);
                return Roep(stroom, "GetData") as byte[] ?? throw new InvalidOperationException("De stem gaf geen geluid terug.");
            });
        }
        finally { _slot.Release(); }
    }

    // ---- kiezen ----
    private (string Naam, bool Nederlands)? Kies(List<(string Naam, bool Nederlands)> stemmen)
    {
        if (_voorkeur.Length > 0 && stemmen.Where(s => s.Naam.Contains(_voorkeur, StringComparison.OrdinalIgnoreCase))
                                           .Select(s => ((string, bool)?)s).FirstOrDefault() is { } v) return v;
        return stemmen.Where(s => s.Nederlands).Select(s => ((string, bool)?)s).FirstOrDefault();
    }

    // ---- SAPI via COM (late binding) ----
    private static List<(string Naam, bool Nederlands, object Token)> Tokens()
    {
        var lijst = new List<(string, bool, object)>();
        foreach (var categorie in new[] { OneCore, Klassiek })
        {
            try
            {
                var cat = Maak("SAPI.SpObjectTokenCategory");
                Roep(cat, "SetId", categorie, false);
                var tokens = Roep(cat, "EnumerateTokens")!;
                var aantal = Convert.ToInt32(Lees(tokens, "Count"));
                for (var i = 0; i < aantal; i++)
                {
                    var token = Roep(tokens, "Item", i)!;
                    var naam = Roep(token, "GetDescription")?.ToString() ?? "";
                    var taal = "";
                    try { taal = Roep(token, "GetAttribute", "Language")?.ToString() ?? ""; } catch { }
                    if (naam.Length > 0) lijst.Add((naam, IsNederlands(taal), token));
                }
            }
            catch { /* categorie bestaat niet op deze Windows: de andere proberen */ }
        }
        return lijst;
    }

    /// <summary>Het kenmerk Language is een hexadecimale taalcode, soms meerdere met ; ertussen: 413 = nl-NL, 813 = nl-BE.</summary>
    public static bool IsNederlands(string taal)
        => taal.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Any(t => int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) && code is 0x413 or 0x813);

    private static object Maak(string progId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("De Windows-stem werkt alleen op Windows.");
        var soort = Type.GetTypeFromProgID(progId) ?? throw new InvalidOperationException($"{progId} is niet beschikbaar op deze computer.");
        return Activator.CreateInstance(soort) ?? throw new InvalidOperationException($"{progId} kon niet gestart worden.");
    }

    private static object? Roep(object o, string naam, params object[] args)
        => Binnenste(() => o.GetType().InvokeMember(naam, BindingFlags.InvokeMethod, null, o, args));
    private static object? Lees(object o, string naam)
        => Binnenste(() => o.GetType().InvokeMember(naam, BindingFlags.GetProperty, null, o, null));
    private static void Zet(object o, string naam, object waarde)
        => Binnenste(() => o.GetType().InvokeMember(naam, BindingFlags.SetProperty, null, o, new[] { waarde }));
    /// <summary>Een eigenschap die een object vasthoudt (COM: putref), zoals Voice en AudioOutputStream.</summary>
    private static void ZetVerwijzing(object o, string naam, object waarde)
        => Binnenste(() => o.GetType().InvokeMember(naam, BindingFlags.PutRefDispProperty, null, o, new[] { waarde }));

    private static object? Binnenste(Func<object?> actie)
    {
        try { return actie(); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { throw ex.InnerException; }
    }

    /// <summary>SAPI wil een eigen draad met een berichtenloze STA; de aanroep blokkeert tot het geluid klaar is.</summary>
    private static Task<T> OpEigenDraad<T>(Func<T> werk)
    {
        var klaar = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var draad = new Thread(() =>
        {
            try { klaar.SetResult(werk()); }
            catch (Exception ex) { klaar.SetException(ex); }
        }) { IsBackground = true, Name = "WindowsStem" };
        if (OperatingSystem.IsWindows()) draad.SetApartmentState(ApartmentState.STA);
        draad.Start();
        return klaar.Task;
    }
}
