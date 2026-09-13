using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace EverythingApp.Services;

/// <summary>Eén sectie (blok) van het alarmpaneel, met de actuele stand.</summary>
public sealed record UNiiSectie(int Nummer, string Naam, bool Ingeschakeld, bool Alarm);

/// <summary>
/// Eén ingang (sensor) van het alarmpaneel. Stand is een leesbare Nederlandse
/// tekst ("gesloten", "open", "sabotage", "afgedekt", …); Aandacht is true bij
/// alles wat niet gewoon gesloten of open is (sabotage, afdekking, storing,
/// overbrugging) — daar hoort iemand naar te kijken.
/// </summary>
public sealed record UNiiIngang(int Nummer, string Naam, string Stand, bool Aandacht);

/// <summary>De volledige uitlezing: secties, ingangen en het tijdstip van lezen.</summary>
public sealed record UNiiStand(
    IReadOnlyList<UNiiSectie> Secties,
    IReadOnlyList<UNiiIngang> Ingangen,
    DateTime Gelezen);

/// <summary>
/// Leest de stand van het Alphatronics UNii-alarmpaneel uit via de "Generieke
/// Interface" (de API-poort van het paneel, standaard 6502).
///
/// Dit is een C#-vertaling van de officiële open-source Python-bibliotheek
/// py-unii (github.com/unii-security/py-unii). Alle magische getallen hieronder
/// komen letterlijk uit die bibliotheek; bij elk getal staat het bronbestand en
/// de functie waar het vandaan komt, zodat het later te controleren blijft.
///
/// De koppeling kan bewust alleen lézen: verbinden, per sectie de stand
/// (ingeschakeld / uitgeschakeld / alarm), per ingang de omschrijving en de
/// stand (gesloten / open / sabotage / afgedekt / overbrugd / storing), en
/// weer netjes afsluiten. Geen inschakelen, geen uitschakelen, geen
/// overbruggen — dat komt pas als het lezen op het echte paneel bewezen werkt.
/// De secties zijn de hoofdvraag ("staat het alarm aan"); de ingangen zijn
/// extra, en als alleen dát deel faalt komen de secties gewoon terug.
///
/// Over de verbindingsvorm: het protocol staat een blijvende verbinding toe
/// (dan moet er elke 30 seconden een "poll alive" heen, zie unii.py,
/// _poll_alive_coroutine), maar dat is een verplichting van lange sessies, geen
/// eis van het paneel. De officiële Home Assistant-integratie doet bij het
/// instellen precies wat wij hier doen: verbinden, iets vragen, en direct weer
/// netjes afsluiten (config_flow.py, test_connection). Een korte
/// verbind-vraag-verbreek-cyclus, bijvoorbeeld elke 10 seconden, is dus
/// toegestaan. Eén aandachtspunt uit diezelfde integratie: na een disconnect
/// heeft het paneel ongeveer een seconde nodig voordat het een nieuwe
/// verbinding aanneemt — sneller dan elke paar seconden lezen heeft dus geen zin.
/// </summary>
public sealed class UNiiKoppeling
{
    // ---- Commandonummers -------------------------------------------------
    // Alle nummers komen uit unii_command.py (enum UNiiCommand). Het zijn
    // 2-byte-waarden die big-endian in het bericht staan.
    private const ushort CmdVerbindingsverzoek = 0x0001;        // CONNECTION_REQUEST
    private const ushort CmdVerbindingAkkoord = 0x0002;         // CONNECTION_REQUEST_RESPONSE
    private const ushort CmdVerbindingGeweigerd = 0x0003;       // CONNECTION_REQUEST_DENIED
    private const ushort CmdNetjesAfsluiten = 0x0014;           // NORMAL_DISCONNECT
    private const ushort CmdGebeurtenis = 0x0102;               // EVENT_OCCURRED (ongevraagd)
    private const ushort CmdGebeurtenisOntvangen = 0x0103;      // RESPONSE_EVENT_OCCURRED
    private const ushort CmdVraagSectieIndeling = 0x0130;       // REQUEST_SECTION_ARRANGEMENT
    private const ushort CmdAntwoordSectieIndeling = 0x0131;    // RESPONSE_REQUEST_SECTION_ARRANGEMENT
    private const ushort CmdVraagSectieStatus = 0x0116;         // REQUEST_SECTION_STATUS
    private const ushort CmdAntwoordSectieStatus = 0x0117;      // RESPONSE_REQUEST_SECTION_STATUS
    private const ushort CmdAlgemeenAntwoord = 0x0101;          // GENERAL_RESPONSE (ACK/NACK op vragen zonder eigen antwoord)
    private const ushort CmdIngangStatusTabel = 0x0105;         // INPUT_STATUS_CHANGED: de hele statustabel — hét antwoord op
                                                                // 0x0106, maar het paneel stuurt hem ook ongevraagd bij wijzigingen
    private const ushort CmdVraagIngangStatus = 0x0106;         // REQUEST_INPUT_STATUS (zonder data)
    private const ushort CmdIngangStatusLos = 0x0125;           // INPUT_STATUS_UPDATE (ongevraagd, één ingang; negeren wij)
    private const ushort CmdVraagIngangIndeling = 0x0140;       // REQUEST_INPUT_ARRANGEMENT (data: bloknummer, 2 bytes)
    private const ushort CmdAntwoordIngangIndeling = 0x0141;    // RESPONSE_REQUEST_INPUT_ARRANGEMENT

    // ---- Sectiestanden ----------------------------------------------------
    // Uit unii_command_data.py, enum UNiiSectionArmedState. Let op de gaten:
    // 3 t/m 6 bestaan niet in de bibliotheek.
    private const byte StandNietGeprogrammeerd = 0; // sectie bestaat niet in de configuratie
    private const byte StandIngeschakeld = 1;
    private const byte StandUitgeschakeld = 2;
    private const byte StandAlarm = 7;
    private const byte StandUitloopvertraging = 8;  // EXIT_TIMER: inschakeling loopt
    private const byte StandInloopvertraging = 9;   // ENTRY_TIMER: iemand kwam binnen, alarm dreigt

    // ---- Ingangstanden ------------------------------------------------------
    // Uit unii_command_data.py, enum UNiiInputState: de láágste 4 bits van de
    // statusbyte van een ingang. De hoogste 4 bits zijn losse vlaggen
    // (UNiiInputStatusRecord): 0x10 overbrugd (bypassed), 0x20 alarm in het
    // geheugen, 0x40 batterij laag, 0x80 supervisie (draadloze melder meldt
    // zich niet meer). Let op het gat: 3 bestaat niet, MASKING is bit-waarde 4.
    private const byte IngangGesloten = 0x0;   // INPUT_OK: lus in rust
    private const byte IngangOpen = 0x1;       // ALARM: lus geactiveerd (deur open, beweging, …)
    private const byte IngangSabotage = 0x2;   // TAMPER: sabotagelus verbroken
    private const byte IngangAfgedekt = 0x4;   // MASKING: melder afgedekt/geblindeerd
    private const byte IngangNietActief = 0xF; // DISABLED: ingang niet in gebruik

    /// <summary>
    /// Noodrem voor de blokkenlus van de ingang-indeling. Normaal stopt de lus
    /// doordat het paneel bloknummer 0xFFFF terugstuurt ("geen blok meer");
    /// mocht dat ooit uitblijven, dan is 32 blokken ruim genoeg voor elke UNii
    /// (de grootste, UNii 512, heeft 963 mogelijke ingangnummers en een blok
    /// bevat er tientallen).
    /// </summary>
    private const int MaxIngangBlokken = 32;

    /// <summary>Standaardpoort van de Generieke Interface: DEFAULT_PORT = 6502 in unii_connection.py.</summary>
    private const int StandaardPoort = 6502;

    /// <summary>Antwoorden zijn nooit groter dan dit: _MAX_LENGTH_OF_ANSWER = 1500 in unii_connection.py.</summary>
    private const int MaxBerichtLengte = 1500;

    // Korte time-outs: het paneel staat op het eigen netwerk, dus als het er
    // is, antwoordt het vlot. py-unii wacht zelf maximaal 5 s op een antwoord
    // (unii.py, _get_received_message); wij houden dezelfde grens aan.
    private static readonly TimeSpan VerbindTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan AntwoordTimeout = TimeSpan.FromSeconds(5);

    private readonly string _adres;
    private readonly int _poort;
    private readonly byte[]? _sleutel; // altijd precies 16 bytes als hij er is
    private readonly ILogger<UNiiKoppeling> _log;

    public UNiiKoppeling(IConfiguration config, ILogger<UNiiKoppeling> log)
    {
        _log = log;
        _adres = config["Alarm:Adres"] ?? "172.16.3.4";
        _poort = int.TryParse(config["Alarm:Poort"], out var p) ? p : StandaardPoort;

        // De sleutel zoals de installateur hem in het paneel heeft gezet, als
        // gewone tekst. De Home Assistant-integratie (config_flow.py,
        // async_step_setup_local) maakt daar zó bytes van:
        //
        //     shared_key = shared_key[:16].ljust(16, " ")   # aanvullen met spaties
        //     shared_key = shared_key.encode()              # utf-8
        //
        // Dus: maximaal 16 tekens, en korter dan 16 wordt rechts aangevuld met
        // spaties (0x20). Een sleutel die letterlijk "1" is, wordt dus
        // "1" plus vijftien spaties — elke tekstlengte van 1 t/m 16 werkt.
        var sleutelTekst = config["Alarm:Sleutel"];
        if (!string.IsNullOrEmpty(sleutelTekst))
        {
            var aangevuld = sleutelTekst.Length > 16
                ? sleutelTekst[..16]
                : sleutelTekst.PadRight(16, ' ');
            var bytes = Encoding.UTF8.GetBytes(aangevuld);

            // AES-128 eist exact 16 bytes. Met gewone (ASCII-)tekens klopt dat
            // altijd; alleen tekens buiten ASCII (é, €, …) worden in UTF-8
            // meer dan één byte en dan klopt de sleutel niet meer. Dat merken
            // we hier, niet pas midden in de handshake.
            if (bytes.Length == 16)
            {
                _sleutel = bytes;
            }
            else
            {
                _log.LogError(
                    "Alarm:Sleutel bevat tekens buiten ASCII en wordt daardoor {Aantal} bytes " +
                    "in plaats van 16; de sleutel wordt genegeerd.", bytes.Length);
            }
        }
    }

    /// <summary>Alleen als adres én sleutel zijn ingevuld heeft verbinden zin.</summary>
    public bool Beschikbaar => !string.IsNullOrEmpty(_adres) && _sleutel is not null;

    /// <summary>
    /// Verbindt met het paneel, leest de sectie-indeling en de actuele stand,
    /// en sluit netjes af. Bij een fout komt er geen halve uitlezing terug maar
    /// null met een Nederlandse reden.
    /// </summary>
    public async Task<(UNiiStand? Stand, string Fout)> LeesAsync(CancellationToken stop = default)
    {
        if (!Beschikbaar)
            return (null, "Alarmkoppeling is niet geconfigureerd: Alarm:Adres en/of Alarm:Sleutel ontbreekt.");

        using var client = new TcpClient();
        try
        {
            // -- Verbinden ------------------------------------------------
            using (var verbindStop = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                verbindStop.CancelAfter(VerbindTimeout);
                try
                {
                    await client.ConnectAsync(_adres, _poort, verbindStop.Token);
                }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                {
                    return (null, $"Geen verbinding met het alarmpaneel op {_adres}:{_poort} (time-out).");
                }
            }

            var stream = client.GetStream();
            var sessie = new Sessie(stream, _sleutel!, _log);

            // -- Handshake ------------------------------------------------
            // Eén bericht heen (CONNECTION_REQUEST, zonder data), één terug.
            // py-unii doet exact dit in unii.py, _connect(). Het paneel geeft
            // in het antwoord de echte sessie-ID; die neemt de Sessie-klasse
            // vanaf dan automatisch over.
            var (antwoord, _) = await sessie.VraagAsync(
                CmdVerbindingsverzoek, null, CmdVerbindingAkkoord, stop);

            if (antwoord == CmdVerbindingGeweigerd)
                return (null, "Het alarmpaneel weigert de verbinding (connection request denied). " +
                              "Controleer of de Generieke Interface in het paneel is vrijgegeven.");
            if (antwoord != CmdVerbindingAkkoord)
            {
                // Bij een verkeerde sleutel klopt de checksum wél (die gaat
                // over de versleutelde bytes) maar ontcijfert het antwoord
                // tot ruis, of er komt helemaal niets terug. py-unii trekt
                // dan dezelfde conclusie (UNiiEncryptionError in unii.py).
                return (null, "Geen geldig antwoord op het verbindingsverzoek. " +
                              "Meestal betekent dit dat Alarm:Sleutel niet overeenkomt met de sleutel in het paneel.");
            }

            // -- Sectie-indeling (namen) ----------------------------------
            // REQUEST_SECTION_ARRANGEMENT heeft geen data nodig; het antwoord
            // bevat per sectie of hij actief is en hoe hij heet.
            var (cmdIndeling, dataIndeling) = await sessie.VraagAsync(
                CmdVraagSectieIndeling, null, CmdAntwoordSectieIndeling, stop);
            if (cmdIndeling != CmdAntwoordSectieIndeling || dataIndeling is null)
                return (null, "Het paneel gaf geen sectie-indeling terug.");

            var indeling = LeesSectieIndeling(dataIndeling);

            // -- Sectie-status (standen) ----------------------------------
            // REQUEST_SECTION_STATUS wil een lijst sectienummers als data.
            // py-unii stuurt altijd de nummers 1 t/m 32 (0x01..0x20), als 32
            // losse bytes — zie unii.py, _connect(), de hexstring
            // "0102030405...1f20". Wij doen hetzelfde: het paneel antwoordt
            // gewoon "niet geprogrammeerd" voor nummers die het niet kent.
            var alleNummers = new byte[32];
            for (var i = 0; i < 32; i++) alleNummers[i] = (byte)(i + 1);

            var (cmdStatus, dataStatus) = await sessie.VraagAsync(
                CmdVraagSectieStatus, alleNummers, CmdAntwoordSectieStatus, stop);
            if (cmdStatus != CmdAntwoordSectieStatus || dataStatus is null)
                return (null, "Het paneel gaf geen sectie-status terug.");

            // -- Ingangen (omschrijvingen + standen) -----------------------
            // Extra informatie bovenop de hoofdvraag. Faalt dit deel (time-out,
            // onverwacht antwoord, oudere firmware), dan gaan de secties gewoon
            // door en blijven de ingangen leeg — precies zoals afgesproken.
            IReadOnlyList<UNiiIngang> ingangen = Array.Empty<UNiiIngang>();
            try
            {
                ingangen = await LeesIngangenAsync(sessie, stop);
            }
            catch (ProtocolFout pf)
            {
                _log.LogWarning("Ingangen niet gelezen: {Reden} De secties gaan gewoon door.", pf.Message);
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested)
            {
                _log.LogWarning("Ingangen niet gelezen: het paneel antwoordde niet op tijd. " +
                                "De secties gaan gewoon door.");
            }
            catch (Exception ex) when (ex is SocketException or IOException or EndOfStreamException)
            {
                // Ook een netwerkfout halverwege de ingangen mag de al gelezen
                // secties niet meer kosten; de hoofdvraag is immers beantwoord.
                _log.LogWarning(ex, "Ingangen niet gelezen door een netwerkfout. De secties gaan gewoon door.");
            }

            // -- Netjes afsluiten ------------------------------------------
            // NORMAL_DISCONNECT heeft geen data en krijgt geen antwoord;
            // py-unii stuurt hem en sluit dan de socket (unii.py, _disconnect).
            // Als de verbinding zojuist al wegviel, is dit gebaar niet meer
            // nodig — dan sluit de using de socket wel.
            try
            {
                await sessie.StuurAsync(CmdNetjesAfsluiten, null, stop);
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                _log.LogDebug(ex, "Nette afsluiting niet verstuurd; de socket gaat toch dicht.");
            }

            // -- Antwoorden combineren -------------------------------------
            var secties = CombineerTotStand(indeling, dataStatus);
            return (new UNiiStand(secties, ingangen, DateTime.Now), "");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return (null, "Uitlezen van het alarmpaneel is afgebroken.");
        }
        catch (OperationCanceledException)
        {
            return (null, "Het alarmpaneel antwoordde niet op tijd. " +
                          "Als dit blijft gebeuren: controleer Alarm:Sleutel; met een verkeerde " +
                          "sleutel negeert het paneel onze berichten.");
        }
        catch (ProtocolFout pf)
        {
            return (null, pf.Message);
        }
        catch (Exception ex) when (ex is SocketException or IOException or EndOfStreamException)
        {
            _log.LogWarning(ex, "Netwerkfout richting alarmpaneel {Adres}:{Poort}", _adres, _poort);
            return (null, $"Netwerkfout richting het alarmpaneel ({ex.Message}).");
        }
        // De TcpClient staat in een using: ook bij elke fout hierboven gaat de
        // socket dicht en blijft er niets hangen.
    }

    // =====================================================================
    // Antwoorden uitpakken
    // =====================================================================

    /// <summary>
    /// Ontleedt het antwoord op REQUEST_SECTION_ARRANGEMENT. De opbouw komt
    /// uit unii_command_data.py, klassen UNiiSectionArrangement en UNiiSection:
    /// records achter elkaar, elk record begint met een versiebyte.
    ///   versie 0: [versie][actief][naam: vaste 17 bytes]           => 19 bytes
    ///   versie 1: [versie][actief][naamlengte][naam: n bytes]      => n + 3 bytes
    /// Het sectienummer staat er niet in; dat is gewoon de volgorde, tellend
    /// vanaf 1 (in de bibliotheek: index = 1; index += 1 per record).
    /// </summary>
    private static Dictionary<int, (bool Actief, string Naam)> LeesSectieIndeling(byte[] data)
    {
        var indeling = new Dictionary<int, (bool, string)>();
        var offset = 0;
        var nummer = 1;
        while (offset < data.Length)
        {
            var versie = data[offset];
            if (versie > 1)
                throw new ProtocolFout($"Onbekende recordversie {versie} in de sectie-indeling.");

            var actief = data[offset + 1] == 1;
            string naam;
            int recordLengte;
            if (versie == 0)
            {
                naam = TekstUit(data.AsSpan(offset + 2, 17));
                recordLengte = 19;
            }
            else
            {
                var naamLengte = data[offset + 2];
                naam = TekstUit(data.AsSpan(offset + 3, naamLengte));
                recordLengte = naamLengte + 3;
            }

            indeling[nummer] = (actief, naam);
            nummer++;
            offset += recordLengte;
        }
        return indeling;
    }

    /// <summary>
    /// Combineert de indeling (namen) met het antwoord op REQUEST_SECTION_STATUS.
    /// Dat antwoord bestaat uit records van precies 2 bytes: [sectienummer][stand]
    /// (unii_command_data.py, UNiiSectionStatusRecord: number = data[0],
    /// armed_state = data[1]).
    /// </summary>
    private List<UNiiSectie> CombineerTotStand(
        Dictionary<int, (bool Actief, string Naam)> indeling, byte[] statusData)
    {
        var secties = new List<UNiiSectie>();
        for (var offset = 0; offset + 1 < statusData.Length; offset += 2)
        {
            int nummer = statusData[offset];
            var stand = statusData[offset + 1];

            // Stand 0 = niet geprogrammeerd: die sectie bestaat niet, overslaan.
            if (stand == StandNietGeprogrammeerd)
                continue;

            // Alleen secties die het paneel zelf als actief aanmerkt; py-unii
            // filtert net zo (unii.py, _handle_section_arrangement).
            indeling.TryGetValue(nummer, out var info);
            if (!info.Actief)
                continue;

            // Vertaling van de zes standen naar twee vlaggen:
            //  - Ingeschakeld: ook bij ALARM (7) en INLOOPVERTRAGING (9) staat de
            //    sectie scherp, en bij UITLOOPVERTRAGING (8) is het inschakelen al
            //    onderweg — voor het dashboard telt dat alles als "aan".
            //  - Alarm: alleen stand 7.
            var ingeschakeld = stand is StandIngeschakeld or StandAlarm
                or StandUitloopvertraging or StandInloopvertraging;
            var alarm = stand == StandAlarm;

            if (stand is not (StandIngeschakeld or StandUitgeschakeld or StandAlarm
                or StandUitloopvertraging or StandInloopvertraging))
            {
                _log.LogWarning("Sectie {Nummer} meldt onbekende stand {Stand}.", nummer, stand);
            }

            var naam = string.IsNullOrWhiteSpace(info.Naam) ? $"Sectie {nummer}" : info.Naam;
            secties.Add(new UNiiSectie(nummer, naam, ingeschakeld, alarm));
        }
        return secties.OrderBy(s => s.Nummer).ToList();
    }

    // =====================================================================
    // Ingangen
    // =====================================================================

    /// <summary>
    /// Leest eerst de ingang-indeling (omschrijvingen) en dan de statustabel,
    /// en combineert die tot de lijst ingangen. Gooit een ProtocolFout als het
    /// paneel niet meewerkt; de aanroeper vangt die op en laat de secties
    /// gewoon doorgaan.
    /// </summary>
    private async Task<IReadOnlyList<UNiiIngang>> LeesIngangenAsync(Sessie sessie, CancellationToken stop)
    {
        // -- Indeling, in blokken --------------------------------------
        // REQUEST_INPUT_ARRANGEMENT wil een 2-byte bloknummer als data,
        // tellend vanaf 1. Alle ingangen passen lang niet altijd in één
        // bericht (maximaal 1500 bytes), dus het paneel deelt de tabel zelf
        // in blokken op. py-unii vraagt blok 1, 2, 3, … tot er geen data
        // meer komt (unii.py, _connect, de while-lus met block.to_bytes(2));
        // het paneel meldt het einde met bloknummer 0xFFFF in het antwoord
        // (unii_command_data.py, UNiiInputArrangement, "Invalid block number").
        var indeling = new Dictionary<int, (byte SensorType, string Naam)>();
        for (var blok = 1; blok <= MaxIngangBlokken; blok++)
        {
            var blokNummer = new byte[] { (byte)(blok >> 8), (byte)blok };
            var (cmd, data) = await sessie.VraagAsync(
                CmdVraagIngangIndeling, blokNummer, CmdAntwoordIngangIndeling, stop);
            if (cmd != CmdAntwoordIngangIndeling)
                throw new ProtocolFout($"Het paneel gaf geen ingang-indeling terug (antwoord 0x{cmd:X4}).");
            if (data is null || data.Length < 4 || !LeesIngangIndelingBlok(data, indeling))
                break; // geen blokken meer
        }

        if (indeling.Count == 0)
            throw new ProtocolFout("Het paneel kent geen enkele geprogrammeerde ingang.");

        // -- Statustabel -----------------------------------------------
        // REQUEST_INPUT_STATUS heeft geen data. Het antwoord heet geen
        // "RESPONSE_…" maar is een gewoon INPUT_STATUS_CHANGED-bericht:
        // dezelfde volledige tabel die het paneel ook ongevraagd stuurt
        // zodra er iets verandert. py-unii wacht op precies dat commando
        // (unii.py, _connect: REQUEST_INPUT_STATUS → INPUT_STATUS_CHANGED)
        // en herkent het als antwoord aan het geëchode volgnummer — dat
        // doet VraagAsync bij ons ook. De tabel past altijd in één bericht:
        // hooguit 963 statusbytes plus 2 kopbytes, ruim binnen de 1500.
        // Losse INPUT_STATUS_UPDATE-berichten (0x0125, één ingang tegelijk)
        // slaat VraagAsync als ongevraagd over.
        var (cmdStatus, dataStatus) = await sessie.VraagAsync(
            CmdVraagIngangStatus, null, CmdIngangStatusTabel, stop);
        if (cmdStatus != CmdIngangStatusTabel || dataStatus is null || dataStatus.Length < 3)
            throw new ProtocolFout($"Het paneel gaf geen ingang-status terug (antwoord 0x{cmdStatus:X4}).");

        return CombineerIngangen(indeling, dataStatus);
    }

    /// <summary>
    /// Ontleedt één blok van het antwoord op REQUEST_INPUT_ARRANGEMENT en
    /// vult daarmee de indeling aan. Geeft false terug bij bloknummer 0xFFFF:
    /// het teken "geen blokken meer" van het paneel.
    ///
    /// De opbouw komt uit unii_command_data.py (UNiiInputArrangement en
    /// UNiiInput):
    ///   byte 0     (onbenut; de bibliotheek slaat hem over)
    ///   byte 1     versie, moet 2 zijn
    ///   byte 2-3   bloknummer (0xFFFF = einde)
    ///   daarna records van 9 + naamlengte bytes:
    ///     [ingangnummer: 2][sensortype: 1][reactie: 1][naamlengte: 1]
    ///     [naam: n bytes][secties-bitmasker: 4 bytes]
    /// Anders dan bij de secties staat het nummer hier dus wél in het record
    /// zelf, en het volgt de reeksen uit bijlage 4 van de UNii-API: 1-512
    /// bedraad, 601-664 draadloos, 701-732 bediendeel, 801-848 KNX, 1001 en
    /// hoger deuren. Sensortype 0 (NOT_ACTIVE) betekent: niet geprogrammeerd.
    /// </summary>
    private static bool LeesIngangIndelingBlok(
        byte[] data, Dictionary<int, (byte SensorType, string Naam)> indeling)
    {
        if (data[1] != 2)
            throw new ProtocolFout($"Onbekende recordversie {data[1]} in de ingang-indeling.");

        var blokNummer = (data[2] << 8) | data[3];
        if (blokNummer == 0xFFFF)
            return false;

        var offset = 4;
        while (offset + 9 <= data.Length)
        {
            int naamLengte = data[offset + 4];
            var recordLengte = 9 + naamLengte;
            if (offset + recordLengte > data.Length)
                throw new ProtocolFout("De ingang-indeling houdt midden in een record op.");

            var nummer = (data[offset] << 8) | data[offset + 1];
            var sensorType = data[offset + 2];
            // data[offset + 3] is de reactiesoort (direct/vertraagd/…) en de
            // laatste 4 bytes zijn het secties-bitmasker: allebei niet nodig.
            var naam = naamLengte > 0 ? TekstUit(data.AsSpan(offset + 5, naamLengte)) : "";

            indeling[nummer] = (sensorType, naam);
            offset += recordLengte;
        }
        return true;
    }

    /// <summary>
    /// Combineert de indeling met de statustabel. Die tabel is één statusbyte
    /// per ingang-índex (unii_command_data.py, UNiiInputStatus): byte 0
    /// onbenut, byte 1 versie (moet 2 zijn), en vanaf byte 2 de indexen
    /// 0, 1, 2, … De index is níét het ingangnummer; die vertaling staat in
    /// IndexNaarNummer. Alleen ingangen die zowel geprogrammeerd zijn
    /// (sensortype ≠ 0) als in gebruik (stand ≠ DISABLED) komen in de lijst.
    /// </summary>
    private List<UNiiIngang> CombineerIngangen(
        Dictionary<int, (byte SensorType, string Naam)> indeling, byte[] statusData)
    {
        if (statusData[1] != 2)
            throw new ProtocolFout($"Onbekende versie {statusData[1]} in de ingang-status.");

        var ingangen = new List<UNiiIngang>();
        for (var index = 0; index + 2 < statusData.Length; index++)
        {
            var statusByte = statusData[index + 2];
            var nummer = IndexNaarNummer(index);
            if (nummer < 0)
                continue; // gereserveerde reeks zonder ingangnummer (bijlage 4)

            var stand = (byte)(statusByte & 0x0F);
            if (stand == IngangNietActief)
                continue; // ingang niet in gebruik

            // Alleen ingangen die het paneel in de indeling als geprogrammeerd
            // aanmerkt; py-unii negeert statussen van onbekende ingangen net zo
            // (unii.py, _handle_input_status_update).
            if (!indeling.TryGetValue(nummer, out var info) || info.SensorType == 0)
                continue;

            var naam = string.IsNullOrWhiteSpace(info.Naam) ? $"Ingang {nummer}" : info.Naam;
            ingangen.Add(MaakIngang(nummer, naam, stand, statusByte));
        }
        return ingangen.OrderBy(i => i.Nummer).ToList();
    }

    /// <summary>
    /// Vertaalt één statusbyte naar leesbaar Nederlands: onderin de byte de
    /// stand zelf, bovenin vier losse vlaggen (zie de constanten bovenaan).
    /// Alles wat niet gewoon "gesloten" of "open" is, krijgt Aandacht = true.
    /// </summary>
    private UNiiIngang MaakIngang(int nummer, string naam, byte stand, byte statusByte)
    {
        var (tekst, aandacht) = stand switch
        {
            IngangGesloten => ("gesloten", false),
            IngangOpen => ("open", false),
            IngangSabotage => ("sabotage", true),
            IngangAfgedekt => ("afgedekt", true),
            _ => ($"onbekend ({stand})", true),
        };
        if (stand is not (IngangGesloten or IngangOpen or IngangSabotage or IngangAfgedekt))
            _log.LogWarning("Ingang {Nummer} meldt onbekende stand {Stand}.", nummer, stand);

        // De vlaggen komen achter de stand; elke vlag is reden voor aandacht.
        var vlaggen = new List<string>();
        if ((statusByte & 0x10) != 0) vlaggen.Add("overbrugd");
        if ((statusByte & 0x40) != 0) vlaggen.Add("batterij laag");
        if ((statusByte & 0x80) != 0) vlaggen.Add("storing (melder meldt zich niet)");
        // Vlag 0x20 (alarm in het geheugen) laten we bewust weg: die gaat over
        // vroeger, niet over nu, en zou de lijst vol uitroeptekens zetten.
        if (vlaggen.Count > 0)
        {
            tekst += " — " + string.Join(", ", vlaggen);
            aandacht = true;
        }

        return new UNiiIngang(nummer, naam, tekst, aandacht);
    }

    /// <summary>
    /// Vertaalt de positie in de statustabel naar het ingangnummer, volgens
    /// bijlage 4 van de UNii-API (unii_command_data.py, input_index_to_number):
    ///   index   0-511  =>  nummer    1-512   (bedraad)
    ///   index 512-543  =>  nummer  701-732   (bediendeel)
    ///   index 544-575  =>  (gereserveerd, geen nummer)
    ///   index 576-639  =>  nummer  601-664   (draadloos)
    ///   index 640-688  =>  nummer  801-849   (KNX)
    ///   index 689-705  =>  (gereserveerd, geen nummer)
    ///   index 706-962  =>  nummer 1001-1257  (deuren)
    /// </summary>
    private static int IndexNaarNummer(int index) => index switch
    {
        <= 511 => index + 1,
        >= 512 and <= 543 => index + 189,
        >= 576 and <= 639 => index + 25,
        >= 640 and <= 688 => index + 161,
        >= 706 and <= 962 => index + 295,
        _ => -1,
    };

    /// <summary>
    /// Bytes naar tekst zoals decode_and_strip in unii_command_data.py:
    /// UTF-8, en witruimte plus \0 aan beide kanten eraf.
    /// </summary>
    private static string TekstUit(ReadOnlySpan<byte> bytes)
        => Encoding.UTF8.GetString(bytes).Trim().Trim('\0').Trim();

    // =====================================================================
    // De sessie: framing, checksum en versleuteling
    // =====================================================================

    /// <summary>Fout in de inhoud van het protocol (niet in het netwerk).</summary>
    private sealed class ProtocolFout(string melding) : Exception(melding) { }

    /// <summary>
    /// Eén open verbinding met het paneel. Houdt de drie tellers bij die in
    /// elke berichtkop meegaan en verzorgt het in- en uitpakken van berichten.
    ///
    /// De berichtopbouw komt uit unii_message.py (UNiiRequestMessage /
    /// UNiiResponseMessage). Alles is big-endian. Kop van 14 bytes:
    ///
    ///   byte  0- 1  sessie-ID       (eerste bericht: 0xFFFF, daarna wat het paneel gaf)
    ///   byte  2- 5  tx-volgnummer   (start willekeurig 0..65535, +1 per bericht)
    ///   byte  6- 9  rx-volgnummer   (laatst ontvangen tx-volgnummer van het paneel)
    ///   byte 10     protocol-ID     (0x04 = onversleuteld, 0x05 = "basic encryption")
    ///   byte 11     pakkettype      (0x01 bij commando &lt; 0x0008, anders 0x02)
    ///   byte 12-13  totale lengte   (inclusief de 2 checksum-bytes aan het eind)
    ///
    /// Daarna de inhoud: [commando: 2][datalengte: 2][data][opvulling met 0x00],
    /// waarbij zó wordt opgevuld dat kop + inhoud + 2 checksum-bytes een
    /// zestienvoud is (er komt dus altijd 1 t/m 16 bytes opvulling bij, ook
    /// als het al paste — precies zoals de lus in UNiiRequestMessage.to_bytes).
    /// Afsluitend 2 bytes CRC.
    /// </summary>
    private sealed class Sessie(NetworkStream stream, byte[] sleutel, ILogger log)
    {
        // Startwaarden uit unii_connection.py, UNiiTCPConnection.connect():
        // "Fixed session ID only one session" => 0xFFFF, en het tx-volgnummer
        // begint op random.randint(0, 65535).
        private ushort _sessieId = 0xFFFF;
        private uint _txVolgnummer = (uint)Random.Shared.Next(0, 65536);
        private uint _rxVolgnummer; // 0 tot het paneel iets heeft gestuurd

        /// <summary>Stuurt een commando zonder op antwoord te wachten (voor NORMAL_DISCONNECT).</summary>
        public async Task StuurAsync(ushort commando, byte[]? data, CancellationToken stop)
        {
            _txVolgnummer++;
            var frame = MaakFrame(commando, data);
            await stream.WriteAsync(frame, stop);
        }

        /// <summary>
        /// Stuurt een commando en wacht op het antwoord daarop. Ongevraagde
        /// berichten (het paneel mag altijd gebeurtenissen of statuswijzigingen
        /// sturen) worden netjes overgeslagen; alleen een gebeurtenis krijgt een
        /// ontvangstbevestiging terug, zoals py-unii dat ook doet
        /// (unii.py, _message_received_callback).
        /// </summary>
        public async Task<(ushort Commando, byte[]? Data)> VraagAsync(
            ushort commando, byte[]? data, ushort verwachtAntwoord, CancellationToken stop)
        {
            await StuurAsync(commando, data, stop);
            var onsVolgnummer = _txVolgnummer;

            using var wacht = CancellationTokenSource.CreateLinkedTokenSource(stop);
            wacht.CancelAfter(AntwoordTimeout);

            while (true)
            {
                var (antwoordCmd, antwoordData, opOnsBericht) = await LeesBerichtAsync(wacht.Token, onsVolgnummer);

                // Het paneel bevestigt met het rx-volgnummer op wélk bericht
                // van ons het reageert. Het verbindingsantwoord op een
                // weigering telt ook als antwoord, net als een GENERAL_RESPONSE
                // (0x0101): dat is de algemene ACK/NACK van het paneel, onder
                // andere op commando's die het niet kent of weigert. Door die
                // terug te geven ziet de aanroeper meteen "niet het verwachte
                // antwoord" in plaats van vijf seconden op de time-out te wachten.
                if (opOnsBericht && (antwoordCmd == verwachtAntwoord
                    || antwoordCmd == CmdVerbindingGeweigerd
                    || antwoordCmd == CmdAlgemeenAntwoord))
                    return (antwoordCmd, antwoordData);

                if (antwoordCmd == CmdGebeurtenis)
                {
                    // Beleefd bevestigen, anders blijft het paneel de
                    // gebeurtenis herhalen. Inhoud negeren we in versie 1.
                    await StuurAsync(CmdGebeurtenisOntvangen, null, wacht.Token);
                    continue;
                }

                log.LogDebug("Bericht 0x{Cmd:X4} overgeslagen tijdens wachten op 0x{Verwacht:X4}.",
                    antwoordCmd, verwachtAntwoord);
            }
        }

        // ---- Bericht opbouwen -------------------------------------------

        private byte[] MaakFrame(ushort commando, byte[]? data)
        {
            var dataLengte = data?.Length ?? 0;

            // Inhoud: commando (2) + datalengte (2) + data + opvulling.
            // De opvulregel uit UNiiRequestMessage.to_bytes: tel kop (14),
            // inhoud en checksum (2) op en vul aan tot een zestienvoud.
            // 16 - (x % 16) is nooit 0: bij een exact zestienvoud komt er een
            // heel blok van 16 nullen bij. Dat lijkt onhandig maar de
            // bibliotheek doet het zo, dus het paneel verwacht het zo.
            var kaal = 14 + 4 + dataLengte + 2;
            var opvulling = 16 - (kaal % 16);
            var inhoud = new byte[4 + dataLengte + opvulling];
            SchrijfU16(inhoud, 0, commando);
            SchrijfU16(inhoud, 2, (ushort)dataLengte);
            data?.CopyTo(inhoud, 4);

            var frame = new byte[14 + inhoud.Length + 2];
            SchrijfU16(frame, 0, _sessieId);
            SchrijfU32(frame, 2, _txVolgnummer);
            SchrijfU32(frame, 6, _rxVolgnummer);
            frame[10] = 0x05; // BASIC_ENCRYPTION (unii_message.py, UNiiProtocolID)
            frame[11] = (byte)(commando < 0x0008 ? 0x01 : 0x02); // SESSION_SETUP / NORMAL_CONECTION
            SchrijfU16(frame, 12, (ushort)frame.Length);

            // "Basic encryption": alléén de inhoud wordt versleuteld, de kop en
            // de checksum niet. AES-128 in CTR-stand; het startblok van de
            // teller is de eerste 12 bytes van de kop (dus zonder de lengte)
            // gevolgd door vier nulbytes — zie UNiiRequestMessage.to_bytes:
            // "initial_value = header[:12]" plus "\00\00\00\00".
            var versleuteld = AesCtr(sleutel, frame.AsSpan(0, 12), inhoud);
            versleuteld.CopyTo(frame, 14);

            // Checksum over álles behalve de laatste 2 bytes (dus inclusief de
            // al versleutelde inhoud), en big-endian achteraan.
            SchrijfU16(frame, frame.Length - 2, Crc16(frame.AsSpan(0, frame.Length - 2)));
            return frame;
        }

        // ---- Bericht ontvangen --------------------------------------------

        private async Task<(ushort Commando, byte[]? Data, bool OpOnsBericht)> LeesBerichtAsync(
            CancellationToken stop, uint onsVolgnummer)
        {
            // Eerst de vaste kop van 14 bytes; daarin staat de totale lengte.
            var kop = new byte[14];
            await stream.ReadExactlyAsync(kop, stop);

            int totaleLengte = LeesU16(kop, 12);
            if (totaleLengte < 16 || totaleLengte > MaxBerichtLengte)
                throw new ProtocolFout($"Onzinnige berichtlengte {totaleLengte} van het paneel; " +
                                       "vermoedelijk praat er iets anders op deze poort.");

            var frame = new byte[totaleLengte];
            kop.CopyTo(frame, 0);
            await stream.ReadExactlyAsync(frame.AsMemory(14), stop);

            // Checksum controleren, net als UNiiResponseMessage.__init__.
            var ontvangenCrc = LeesU16(frame, frame.Length - 2);
            var berekendeCrc = Crc16(frame.AsSpan(0, frame.Length - 2));
            if (ontvangenCrc != berekendeCrc)
                throw new ProtocolFout("Checksum van het antwoord klopt niet; bericht beschadigd.");

            // Inhoud ontcijferen als het paneel versleuteld antwoordt
            // (protocol-ID 0x05). Zelfde teller-opzet als bij verzenden.
            var inhoud = frame[14..^2];
            if (frame[10] == 0x05)
                inhoud = AesCtr(sleutel, frame.AsSpan(0, 12), inhoud);

            if (inhoud.Length < 4)
                throw new ProtocolFout("Antwoord van het paneel is te kort om te ontleden.");

            var commando = LeesU16(inhoud, 0);
            int dataLengte = LeesU16(inhoud, 2);
            if (4 + dataLengte > inhoud.Length)
            {
                // De checksum klopte (die gaat over de versleutelde bytes) maar
                // de ontcijferde inhoud is onzin: vrijwel zeker een verkeerde
                // sleutel.
                throw new ProtocolFout("Antwoord van het paneel is niet te ontcijferen; " +
                                       "controleer of Alarm:Sleutel gelijk is aan de sleutel in het paneel.");
            }
            var data = dataLengte > 0 ? inhoud[4..(4 + dataLengte)] : null;

            // Tellers bijwerken zoals unii_connection.py, _receive_coroutine:
            // de sessie-ID van het paneel overnemen, en diens tx-volgnummer
            // onthouden om in ons volgende bericht als rx terug te geven.
            _sessieId = LeesU16(frame, 0);
            var hunTx = LeesU32(frame, 2);
            var hunRx = LeesU32(frame, 6);
            _rxVolgnummer = hunTx;

            // Is dit het antwoord op óns laatste bericht? Dan echoot het paneel
            // ons tx-volgnummer in zijn rx-veld.
            return (commando, data, hunRx == onsVolgnummer);
        }

        // ---- Checksum ------------------------------------------------------

        /// <summary>
        /// CRC-16 zoals _UNiiMessage._calculate_crc16: polynoom 0x1021,
        /// startwaarde 0x0000, geen spiegeling, geen slot-XOR (de variant die
        /// meestal CRC-16/XMODEM heet). Gecontroleerd tegen de testvector in
        /// tests/test_unii_message.py: het onversleutelde verbindingsverzoek
        /// eindigt op checksum 0x5ED3.
        /// </summary>
        private static ushort Crc16(ReadOnlySpan<byte> bytes)
        {
            var crc = 0;
            foreach (var b in bytes)
            {
                crc ^= b << 8;
                for (var i = 0; i < 8; i++)
                    crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1;
                crc &= 0xFFFF;
            }
            return (ushort)crc;
        }

        // ---- AES-CTR --------------------------------------------------------

        /// <summary>
        /// AES-128 in CTR-stand. .NET heeft geen ingebouwde CTR, maar CTR is
        /// niets anders dan: versleutel een oplopend tellerblok met AES-ECB en
        /// XOR dat blok voor blok met de inhoud. Versleutelen en ontcijferen
        /// zijn daardoor dezelfde bewerking. Het startblok is de meegegeven
        /// 12 kop-bytes plus vier nulbytes ("The block counter is reset to 0
        /// for every new message", unii_message.py), en de teller loopt
        /// big-endian op over het hele blok — zo doet PyCryptodome het met
        /// nonce=b"" en alleen een initial_value.
        /// </summary>
        private static byte[] AesCtr(byte[] sleutel, ReadOnlySpan<byte> kop12, byte[] inhoud)
        {
            using var aes = Aes.Create();
            aes.Key = sleutel;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            using var ecb = aes.CreateEncryptor();

            var teller = new byte[16];
            kop12.CopyTo(teller);
            // teller[12..16] blijft 0: de blokteller begint elke boodschap op 0.

            var sleutelstroom = new byte[16];
            var uitvoer = new byte[inhoud.Length];
            for (var blok = 0; blok < inhoud.Length; blok += 16)
            {
                ecb.TransformBlock(teller, 0, 16, sleutelstroom, 0);
                var n = Math.Min(16, inhoud.Length - blok);
                for (var i = 0; i < n; i++)
                    uitvoer[blok + i] = (byte)(inhoud[blok + i] ^ sleutelstroom[i]);

                // Teller big-endian ophogen: achterste byte eerst, met overloop
                // naar voren. Berichten zijn hooguit 1500 bytes (94 blokken),
                // dus in de praktijk verandert alleen de laatste byte of twee.
                for (var i = 15; i >= 0; i--)
                {
                    if (++teller[i] != 0) break;
                }
            }
            return uitvoer;
        }

        // ---- Big-endian hulpjes ---------------------------------------------
        // "All numbers are exchanged in big endian order" (unii_message.py,
        // UNiiRequestMessage): grootste byte eerst, vandaar deze vier regels.

        private static void SchrijfU16(byte[] doel, int plek, ushort waarde)
        {
            doel[plek] = (byte)(waarde >> 8);
            doel[plek + 1] = (byte)waarde;
        }

        private static void SchrijfU32(byte[] doel, int plek, uint waarde)
        {
            doel[plek] = (byte)(waarde >> 24);
            doel[plek + 1] = (byte)(waarde >> 16);
            doel[plek + 2] = (byte)(waarde >> 8);
            doel[plek + 3] = (byte)waarde;
        }

        private static ushort LeesU16(byte[] bron, int plek)
            => (ushort)((bron[plek] << 8) | bron[plek + 1]);

        private static uint LeesU32(byte[] bron, int plek)
            => ((uint)bron[plek] << 24) | ((uint)bron[plek + 1] << 16)
             | ((uint)bron[plek + 2] << 8) | bron[plek + 3];
    }
}
