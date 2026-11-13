using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace EverythingApp.Services;

// ============================================================================================================
//  Omroep op de Sonos-speakers (wens gebruiker 13-11-2026): een gesproken tekst laten klinken, nu (Beheer ▸ Omroep,
//  als PA-systeem) of op vaste tijden (tabel OmroepMeldingen, bv. vrijdag 9:00 "Hebben jullie allemaal een broodje
//  besteld?"). De tekst wordt uitgesproken door de Windows-stem op de server (WindowsStem, niets verlaat het
//  netwerk), als WAV klaargezet op /omroep/geluid/<code>.wav en met de meldingsfunctie van Sonos (audioClip,
//  SonosService.MeldingAsync) op elke speaker afgespeeld: de muziek gaat even zachter en daarna vanzelf verder.
// ============================================================================================================

/// <summary>WAV-bestanden maken van ruwe PCM (22,05 kHz, 16 bit, mono), met stilte en een toon vooraf.</summary>
public static class Wav
{
    /// <summary>Zet er een WAV-kop voor.</summary>
    public static byte[] Maak(byte[] pcm, int hertz = WindowsStem.Hertz)
    {
        using var m = new MemoryStream(44 + pcm.Length);
        using var w = new BinaryWriter(m);
        w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);     // PCM, mono
        w.Write(hertz); w.Write(hertz * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm);
        w.Flush();
        return m.ToArray();
    }

    public static byte[] Stilte(double seconden, int hertz = WindowsStem.Hertz) => new byte[(int)(seconden * hertz) * 2];

    /// <summary>Een rustige ding-dong (twee tonen die uitsterven), als aankondiging vóór de stem.</summary>
    public static byte[] Toon(int hertz = WindowsStem.Hertz)
    {
        var uit = new List<byte>();
        foreach (var (frequentie, duur) in new[] { (659.25, 0.55), (523.25, 0.85) })      // E5, C5
        {
            var n = (int)(duur * hertz);
            for (var i = 0; i < n; i++)
            {
                var t = i / (double)hertz;
                var inzet = Math.Min(1, t / 0.01);                                   // geen klik aan het begin
                var monster = Math.Sin(2 * Math.PI * frequentie * t) * Math.Exp(-3.2 * t) * inzet * 0.45;
                var waarde = (short)(monster * short.MaxValue);
                uit.Add((byte)(waarde & 0xFF)); uit.Add((byte)((waarde >> 8) & 0xFF));
            }
        }
        return uit.ToArray();
    }

    public static byte[] Achterelkaar(params byte[][] delen) => delen.SelectMany(d => d).ToArray();
}

/// <summary>De klaargezette geluidsbestanden: een kwartier te halen op /omroep/geluid/&lt;code&gt;.wav (zonder inloggen;
/// de code is willekeurig en de speakers kunnen niet aanmelden).</summary>
public class OmroepGeluid
{
    private readonly ConcurrentDictionary<string, (byte[] Wav, DateTime Tot)> _bestanden = new();

    public string Bewaar(byte[] wav)
    {
        foreach (var oud in _bestanden.Where(b => b.Value.Tot < DateTime.UtcNow).Select(b => b.Key).ToList()) _bestanden.TryRemove(oud, out _);
        var code = Guid.NewGuid().ToString("N");
        _bestanden[code] = (wav, DateTime.UtcNow.AddMinutes(15));
        return code;
    }

    public byte[]? Haal(string code) => _bestanden.TryGetValue(code, out var b) && b.Tot >= DateTime.UtcNow ? b.Wav : null;
}

public record OmroepSpeakerUitkomst(string Speaker, bool Gelukt, string Fout);

/// <summary>Wat er van een omroep terechtkwam: per speaker, of een fout vooraf (geen stem, geen speakers).</summary>
public record OmroepUitkomst(List<OmroepSpeakerUitkomst> Speakers, string Fout)
{
    public bool Gelukt => Fout.Length == 0 && Speakers.Any(s => s.Gelukt);

    public string Tekst
    {
        get
        {
            if (Fout.Length > 0) return Fout;
            var goed = Speakers.Where(s => s.Gelukt).Select(s => s.Speaker).ToList();
            var mis = Speakers.Where(s => !s.Gelukt).ToList();
            var tekst = goed.Count > 0 ? "Omgeroepen op " + string.Join(", ", goed) + "." : "Op geen enkele speaker gelukt.";
            if (mis.Count > 0) tekst += " Niet gelukt: " + string.Join("; ", mis.Select(s => s.Speaker + " (" + s.Fout + ")")) + ".";
            return tekst;
        }
    }
}

public class OmroepService
{
    private readonly SonosService _sonos;
    private readonly WindowsStem _stem;
    private readonly OmroepGeluid _geluid;
    private readonly IConfiguration _config;
    private readonly IServer _server;
    private readonly ILogger<OmroepService> _log;

    public OmroepService(SonosService sonos, WindowsStem stem, OmroepGeluid geluid, IConfiguration config, IServer server,
                         ILogger<OmroepService> log)
    {
        _sonos = sonos; _stem = stem; _geluid = geluid; _config = config; _server = server; _log = log;
    }

    public bool Ingesteld => _sonos.Ingesteld;

    /// <summary>Volume van een melding als er niets is opgegeven (0-100).</summary>
    public int StandaardVolume => Math.Clamp(_config.GetValue("Omroep:Volume", 25), 1, 100);

    /// <summary>
    /// Het adres waarop een speaker deze app bereikt, voor het ophalen van het geluid. Omroep:Adres in appsettings
    /// (bv. "http://172.16.1.20:5010"), anders zelf bepaald: het eigen IP-adres op de route naar de speaker en de
    /// poort waarop de app luistert. De speakers moeten die poort wel kunnen bereiken (firewall van de server).
    /// </summary>
    public string Adres(string speakerAdres)
    {
        if ((_config["Omroep:Adres"] ?? "").Trim().TrimEnd('/') is { Length: > 0 } vast) return vast;
        var poort = 5010;
        var luistert = _server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        if (luistert is not null && Uri.TryCreate(luistert.Replace("*", "localhost").Replace("+", "localhost").Replace("[::]", "localhost"), UriKind.Absolute, out var u))
            poort = u.Port;
        var eigen = "127.0.0.1";
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect(speakerAdres, 1400);                       // UDP: er gaat niets over de lijn, het kiest alleen de route
            if (s.LocalEndPoint is IPEndPoint ep) eigen = ep.Address.ToString();
        }
        catch { }
        return $"http://{eigen}:{poort}";
    }

    /// <summary>
    /// Spreekt de tekst uit op de gekozen speakers (uuid's; null of leeg = alle bereikbare). Volume null = het
    /// standaardvolume voor meldingen. Met toon klinkt er eerst een ding-dong.
    /// </summary>
    public async Task<OmroepUitkomst> OmroepAsync(string tekst, IReadOnlyCollection<string>? speakers, int? volume, bool toon, string door)
    {
        tekst = (tekst ?? "").Trim();
        if (tekst.Length == 0) return new(new(), "Er is geen tekst om om te roepen.");
        if (!_sonos.Ingesteld) return new(new(), "Sonos is niet ingesteld (appsettings, Sonos:Adressen).");

        List<SonosSpeaker> doel;
        try
        {
            var alle = await _sonos.SpeakersAsync();
            doel = alle.Where(s => s.Bereikbaar && (speakers is null || speakers.Count == 0 || speakers.Contains(s.Uuid))).ToList();
            if (doel.Count == 0) return new(new(), "Geen van de gekozen speakers is bereikbaar.");
        }
        catch (Exception ex) { return new(new(), "De speakers zijn niet te bereiken: " + ex.Message); }

        byte[] wav;
        try
        {
            var spraak = await _stem.SpreekAsync(tekst);
            wav = Wav.Maak(toon
                ? Wav.Achterelkaar(Wav.Stilte(0.25), Wav.Toon(), Wav.Stilte(0.25), spraak, Wav.Stilte(0.4))
                : Wav.Achterelkaar(Wav.Stilte(0.4), spraak, Wav.Stilte(0.4)));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Omroep: de tekst kon niet uitgesproken worden.");
            return new(new(), "De tekst kon niet uitgesproken worden: " + ex.Message);
        }

        var code = _geluid.Bewaar(wav);
        var sterkte = Math.Clamp(volume ?? StandaardVolume, 1, 100);
        var taken = doel.Select(async s =>
        {
            try
            {
                await _sonos.MeldingAsync(s.Adres, s.Uuid, $"{Adres(s.Adres)}/omroep/geluid/{code}.wav", sterkte);
                return new OmroepSpeakerUitkomst(s.Naam, true, "");
            }
            catch (Exception ex) { return new OmroepSpeakerUitkomst(s.Naam, false, ex.Message); }
        });
        var uit = new OmroepUitkomst((await Task.WhenAll(taken)).ToList(), "");
        _log.LogInformation("Omroep door {Door}: \"{Tekst}\" (volume {Volume}). {Uitkomst}", door, tekst, sterkte, uit.Tekst);
        return uit;
    }
}

/// <summary>
/// De wekker voor de meldingen op vaste tijden (tabel OmroepMeldingen): kijkt elke 15 seconden wat er aan de beurt
/// is (OmroepRegels.MoetNu). Vóór het omroepen wordt LaatstGedaan gezet, zodat een herstart of een trage speaker
/// geen tweede keer geeft. In Development staat hij standaard uit (Omroep:Aan), anders roept een lokale testversie
/// om in het hele pand.
/// </summary>
public class OmroepWaker : BackgroundService
{
    private readonly IOmroepRepository _repo;
    private readonly OmroepService _omroep;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<OmroepWaker> _log;

    public OmroepWaker(IOmroepRepository repo, OmroepService omroep, IConfiguration config, IHostEnvironment env, ILogger<OmroepWaker> log)
    {
        _repo = repo; _omroep = omroep; _config = config; _env = env; _log = log;
    }

    public bool Aan => _config.GetValue("Omroep:Aan", !_env.IsDevelopment());

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stop); } catch (OperationCanceledException) { return; }
        var tabelGezien = DateTime.MinValue;
        var tabel = false;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (Aan && _repo.Beschikbaar && _omroep.Ingesteld)
                {
                    // de tabel komt er pas na het script: af en toe opnieuw kijken
                    if (!tabel && DateTime.UtcNow - tabelGezien > TimeSpan.FromMinutes(5))
                    {
                        tabel = await _repo.TabelAanwezigAsync();
                        tabelGezien = DateTime.UtcNow;
                    }
                    if (tabel)
                    {
                        var nu = DateTime.Now;
                        foreach (var m in (await _repo.LijstAsync()).Where(m => OmroepRegels.MoetNu(m, nu)))
                        {
                            await _repo.ZetGedaanAsync(m.Id, nu, "bezig…");
                            var uit = await _omroep.OmroepAsync(m.Tekst, m.SpeakerLijst, m.Volume, m.Toon, "de tijdklok");
                            await _repo.ZetGedaanAsync(m.Id, nu, uit.Tekst);
                        }
                    }
                }
            }
            catch (Exception ex) { _log.LogWarning(ex, "Omroep: de tijdklok liep vast."); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stop); } catch (OperationCanceledException) { return; }
        }
    }
}
