# EverythingApp (MDE Automation) — projectgeheugen voor Claude Code

Blazor Server (.NET 8) bedrijfsapp van MDE Automation, migratie van een Access/VBA-app. Nederlandstalig:
code-commentaar, UI-teksten, commits en antwoorden in het Nederlands. Engelse termen alleen waar ze
naar externe tools verwijzen (MotionLab-registernamen, EtherCAT-begrippen).

## Omgeving
- Project: deze map (`EverythingApp.csproj`). Zelfde map via `C:\ClaudeCoworkShare\…` op de server 172.16.1.13.
- Draait als Windows-service **MDEApp** op http://172.16.1.13:5010, via Caddy op https://everything.mde-automation.nl/mde/.
  Publiceren gebeurt met het bestaande publiceerscript op de server (stopt de service, `dotnet publish`, start weer;
  bij een buildfout blijft de oude versie staan).
- Database: MariaDB op NAS 172.16.1.4, **tabelnamen zijn hoofdlettergevoelig**. Schema-uitbreidingen staan als
  `*_MariaDB.sql`-scripts in de projectmap; de app controleert kolommen tolerant via `information_schema`
  (zie bv. `HeeftStappenAsync` in `Services/IProductRepository.cs`) zodat een script later gedraaid mag worden.
- Filestore: `\\172.16.1.4\data\Filestore` (artikelfoto's in `…\Artikelen`).
- `appsettings.json` bevat geheimen (DB-wachtwoord, API-sleutels, agenttoken, FTP) en staat buiten Git.
  **Nooit** geheimen in chat, commits of logs herhalen; nieuwe instellingen ook in `appsettings.voorbeeld.json`.

## Structuur
- `Components/Pages/*.razor` — schermen (Producten.razor is het grootste: producttypen, exemplaren, tabs).
- `Components/ExemplaarWerk.razor` — de werkplekcomponent (uitlezen, programmeren, testen, encoder, OD-venster);
  gebruikt in Producten (tab "Programmeren en testen") en TestPcb.
- `Services/I*Repository.cs` — interface + `Geen…` (null-implementatie) + `MySql…` in één bestand.
- `Models/` — records/klassen (bv. `Product.cs`: ProductStap, ProductTechniek, AttribuutBronnen).
- `wwwroot/app.css` — één stylesheet; na elke CSS-wijziging het versienummer in `Components/App.razor`
  (`app.css?v=NNN`) ophogen, anders ziet de browser het niet.
- `MdeAgent/` — de **MDE Werkplek-agent** (Python, apart gebouwd; zie onder). Uitgesloten van de .NET-build via
  `<Compile/Content Remove="MdeAgent\**" />` in het csproj.

## Razor-valkuilen (kosten al meerdere builds)
- Geen dubbele aanhalingstekens binnen een attribuutexpressie: `@onclick="() => Doe("x")"` breekt.
  Gebruik een helpermethode (`Doe1()`), of een veld.
- `<text …>` met attributen mag niet in een RenderFragment in `@code`, en de letterlijke tekenreeks van die
  SVG-tag mag zelfs niet in een C#-string in `@code` staan. SVG-tekst via de helper `SvgTekst(...)`
  (bouwt de tag uit stukjes).
- Patroonvariabelen uit `@if (x is { } y)` lekken naar de omliggende scope: unieke namen gebruiken.
- Een `string.Replace`/anchor-gebaseerde bulkwijziging kan stil mislukken als de omgeving verschoven is —
  na scripted edits altijd `grep` dat het resultaat er staat (zie de CSS-episode in de historie).
- Bestanden zijn UTF-8 zonder BOM. **Niet** `Get-Content`/`Set-Content` van PowerShell 5.1 gebruiken
  voor scripted edits: die leest zonder BOM als ANSI en verminkt dan alle tekens als ⚠ en é. Gebruik
  `[IO.File]::ReadAllText/WriteAllText` met `UTF8Encoding($false)`, of de Edit-tool.
- JS-interop en SignalR: lange taken niet synchroon; `MaximumReceiveMessageSize` is 8 MB; interop met
  CancellationToken.

## Werkplek-agent (MdeAgent/)
- Python 3.12, PyInstaller + Inno Setup. Bouwen: `MdeAgent\bouw.bat` (kopieert bron naar `D:\MdeAgent`,
  venv, controle, `build.ps1`, installer in `uitvoer\`). Installeren op een werkplek-pc: `installeer.bat`
  (token = `Productie:AgentToken` uit appsettings; per pc gelijk). Versie in `mde_agent/__init__.py` ophogen bij
  elke wijziging; de app controleert `/health`.
- OpenBLAS op 1 thread (`OPENBLAS_NUM_THREADS=1` in bouw.bat/build.ps1/start.py): anders sterven PyInstallers
  kindprocessen op pc's met veel kernen of een krap wisselbestand. `numpy.f2py` NIET uitsluiten.
- Taken staan in `mde_agent/taken/` en worden geregistreerd in `taken/__init__.py` (`ALLE`). Patroon:
  `NAAM`, `OMSCHRIJVING`, `beschikbaar(inst)`, `voer_uit(job, inst, parameters)`; `job.log/voortgang/klaar/mislukt`;
  `job.resultaat` mag tussentijds gezet worden met `"live": True` voor live-weergave in de app
  (`VoerTaakUit(..., live: handler)` in ExemplaarWerk). `job.stop_gevraagd` = Stop-knop.
- **Slave-identiteit = fabrikantcode + productcode** (okt 2026, `DriveTypen_Vendor_MariaDB.sql`: kolom `VendorID`,
  uniek op het paar). `ZoekTypeOpCodeAsync(code, vendor)`: eerst het paar, dan een type zonder vastgelegde
  fabrikantcode (alleen code); een ander vendor telt nooit. Test PCB meldt "fabrikantcode 0x… nog niet
  vastgelegd" zolang het veld bij Controllertypen leeg is (Ingenia bewust leeg gelaten: invullen na de eerste
  scan). Bekend: MDE 0x000010E3 (EtherCAT-partner), ZeroErr 0x5A65726F. De agent gaf `vendor_id` al door.
- **MDE-profiel** (`taken/mde.py`, protocol `mde` in `DriveProtocol`, agent 1.12.0): eigen slaves (vendor
  0x10E3) alleen via standaardobjecten (0x1008/0x1009/0x100A/0x1018), control/status 0x7000/0x6000, SDO's en
  FoE (`mde.firmware`: INIT→BOOT, `foe_write`, terug, 0x100A teruglezen; bestandsnaam = naam in de filestore).
  `drives.uitlezen` slaat bij vendor 0x10E3 de Ingenia-registers over. `mde.test` voert een recept uit: lijst
  stappen met bouwstenen identiteit / lees / schrijf / wacht / beweging / doorgeefluik (zie moduledocstring).
  **Aanname** 0x8000: sub 1 MotorId en 2 RegisterAddress schrijven, dan 3 Value en 4 Status lezen (0 = goed);
  bits van 0x7000/0x6000 komen per recept (`controlword: {index, aan, uit}`). Beweging gaat via SDO op de
  PDO-objecten (0x7010 → 0x6010); werkt dat niet in PREOP, dan moet er een cyclische PDO-uitwisseling bij.
  `DriveProtocol.ViaSdo` = coe + mde (JSON-parametersets, Objecten…); `IsErob` alleen coe (hoek/positie).
  In Test PCB: knop 🪪 Identiteit bij een MDE-positie. **Niet getest tegen een echte slave.**
- **Testrecepten per type** (`DriveRecepten_MariaDB.sql`, model `DriveRecept`, `IDriveRepository.*Recept*`):
  Controllertypen toont bij een type met protocol coe/mde de recepten (naam, volgorde, verplicht, stappen als
  JSON in een tekstvak; `DriveRecept.Controleer` weigert ongeldige JSON of onbekende bouwstenen; "+ Voorbeeld-
  recept" zet `DriveRecept.Voorbeeld` neer). Test PCB voegt per slave × recept een test toe met code
  `recept:<id>:<positie>` (`_recepten`, `ReceptVan`, `StartRecept` → `mde.test`), uitslag in de historie met per
  stap ✓/✗ in het logboek. Zonder de tabel werkt Test PCB gewoon zonder recepten (`GetReceptenVoorTypenAsync`
  vangt "doesn't exist"). Het script zet de recepten "Identiteit" en "Vacuüm en blaas" op de Swivel.
- **Live meekijken** (agent 1.12.0): bouwsteen `monitor` (objecten) in een recept → de werker leest die elke
  0,2 s (tijdens wacht/beweging en na elke stap) en print `{"live": {"t", "waarden"}}`; `coe._draai` zet dat in
  `job.resultaat` met `live: True`, en `VoerTaakUit(..., live: MdeLive)` tekent in het testvenster een
  waardentabel plus `SporenGrafiek` (max. 4 objecten). Taak `mde.monitor` doet hetzelfde los (knop 📈 Meekijken
  bij een MDE-positie, objectenlijst als JSON, Stop = klaar). Eén taak tegelijk bij de agent: schakelen tijdens
  het meekijken gaat dus via het recept, niet los. Rooktest zonder hardware: scratchpad `mdetoets.py` (nepslave).
  Bouwstenen erbij op verzoek: `pauze` (= wacht), `wacht_tot` (index/sub/type + gelijk/min/max, timeout: wachten
  tot een waarde een grens haalt) en `herhaal` (keer, stappen [ … ]; genest, stapnummers "3.1"); `_voer_stappen`
  is de recursieve uitvoerder, `DriveRecept.Controleer` valideert genest. `"stoppen": false` op een stap of blok
  laat het recept doorlopen; de einduitslag is dan toch MISLUKT als er iets fout ging.
- **Chip programmeren beperkt tot het halffabricaat** (14-10-2026, op verzoek): `ChipFirmwares` toont alleen
  firmware van de typen op de posities van dit product met het gekozen chip-protocol **of protocol mde** (een
  M-Pede-print krijgt de eerste firmware via USB en daarna via FoE: hetzelfde bestand, hetzelfde type; geen apart
  ESP32-type of extra positie nodig). Terugval op alle typen alleen als geen enkele positie een type heeft.
  `ChipCodeVanType` geeft bij een MDE-type "" (agent herkent de chip zelf). Na een USB-flash van MDE-firmware
  wordt ook `drive:<positie>:firmware` gezet, zoals bij FoE.
- **Seriële monitor** (agent 1.13.0, `taken/serieel.py`, taak `serieel.monitor`): uitvoer van de ESP32 via de
  USB-UART als logregels van de job (poort zoals esp32.flashen, baud, seconden, reset via DTR/RTS zoals esptool
  default_reset; zonder reset DTR/RTS laag laten, anders blijft de ESP32 in reset). Tekst sturen: nieuw
  `POST /jobs/<id>/invoer` {"tekst"} → `Job.stuur`/`lees_invoer`. **Debug-gereedschap, niet in het joblog**
  (wens gebruiker, agent 1.13.1): de regels gaan in `Job.uitvoer_toevoegen` en de app haalt ze met
  `GET /jobs/<id>/uitvoer?vanaf=n`; eigen terminalvenster in Test PCB (`_serOpen`, `StartSerieel` pollt zelf,
  zoals het encodervenster; `mdeConsole.onder` scrolt mee). Eén taak tegelijk: tijdens de monitor kan esptool de
  poort niet gebruiken. **Fout gevonden**: `StartTest` controleerde eerst of de code in `_testen` stond, waardoor
  Meekijken (code `monitor:n`) en de eerste seriële monitor niets deden; de monitor-afhandeling staat nu ervoor.
  Eerste echte test (14-10-2026, Swivel): werkt; de opstartregels van de ESP32-S3 lieten een kapotte firmware
  zien (SHA-256-fout + watchdog-lus) die daarna via VS Code opnieuw is geflasht. Regels in het venster zijn
  blok-elementen (`.prod-console .regel`), anders plakt gekopieerde tekst aan elkaar; de agent (1.13.2) neemt
  \r\n, \n én een losse \r als regeleinde, ook als \r\n over twee leesbeurten valt (`serieeltoets.py`).
- **Eerste echte FoE-test op de Swivel (14-10-2026)**: de slave gaat niet naar BOOT (blijft INIT, AL-status
  0x0000 = geen fout, dus de firmware doet de overgang gewoon niet) en 0x1008/0x100A gaven niets terug. Agent
  1.12.1: lukt BOOT niet, dan FoE via de gewone mailbox in PRE-OP proberen; foutmeldingen noemen de toestand en
  de AL-statuscode in woorden (`_toestand`, `_al_status`). Blijft het mislukken, dan moet de slave-firmware
  bootstrap + FoE aanzetten (SSC: BOOTSTRAPMODE_SUPPORTED, FOE_SUPPORTED) of FoE in PRE-OP toestaan.
  **Vervolg (14-10-2026, na nieuwe firmware)**: TwinCAT krijgt de Swivel wél in BOOT, onze INIT→BOOT niet
  (seriële monitor: PRE-OP→INIT, daarna niets). Verschil met TwinCAT/SOEM-`firm_update`: die zetten in INIT
  eerst de bootstrap-mailbox uit de SII (woord 0x14/0x16) in SM0/SM1 en vragen dan BOOT. Agent 1.13.3 doet dat
  ook (`_bootstrap_mailbox`: `eeprom_read(SiiOffset.BOOT_RX_MBX/BOOT_TX_MBX)` + `amend_mbx('out'/'in')`),
  wacht 10 s op BOOT en logt bij mislukken de ESC-registers 0x0120/0x0130/0x0134 (`_esc_registers`).
  Test met 1.13.3: AL control stond op 0x0012 (PRE-OP + error-ack) terwijl wij BOOT vroegen → een tweede master
  (TwinCAT nog aan de bus) schrijft het register. Agent 1.13.4 weigert dan ("andere master") in plaats van FoE
  in PRE-OP te proberen. Ook gerepareerd: "slave terug" eiste een gevulde 0x100A (de Swivel-firmware vult die
  niet); nu telt dezelfde vendor/productcode op dezelfde positie, versie alleen gecontroleerd als hij gelezen is.
  De gebruiker is zeker dat TwinCAT er niet aan hing; de firmware-log van de Swivel toont PRE-OP → INIT (onze
  vraag kwam dus aan) → binnen een seconde weer PRE-OP. 0x0012 is wat SOEM's `config_init` schrijft, maar ook wat
  elke andere master/scanner schrijft (MotionLab3 op de rig-pc?). pysoem's `amend_mbx` wist en herschrijft alleen
  SM0/SM1 (bron op GitHub nagekeken), geen toestandsvraag. Agent 1.13.5: `_vraag_toestand` logt per toestandsvraag
  de wkc, AL control meteen teruggelezen (en schrijft zo nodig nog eens met `_fpwr`, dat een WkcError geeft) en
  elke verandering van AL control/status/code tijdens het wachten (`boottoets.py` in de scratchpad toetst de drie
  gevallen). Daarmee is te zien óf de vraag aankomt en wie er daarna PRE-OP vraagt.
  **Opgelost (14-10-2026, 1.13.5):** de "tweede master" was SOEM zelf: `write_state` gaf wkc 0 (frame niet
  aangekomen), dus AL control hield de 0x0012 van `config_init`. De directe FPWR erachter landde; daarna INIT →
  bootstrap-mailbox → BOOT in één keer, FoE 358 kB in 37,6 s, slave na 2 s terug. Elke toestandsvraag blijft dus
  gecontroleerd en zo nodig herhaald.
- **Serienummer schrijven in eigen MDE-prints** (agent 1.14.0, `mde.serienummer`): sub-vinkje "Serienummer
  schrijven" onder EtherCAT (techniek `ecatsn`), knop 🔢 in de positierij van een MDE-slave. Het nummer = de cijfers
  uit het serienummer van het exemplaar; de agent zet het in de EEPROM van de ESC op SII-woord 0x0E/0x0F (vaste
  plek uit de standaard, buiten de CRC, blijft bij firmware-updates), in INIT, en leest terug. Staat er al een ander
  nummer, dan vraagt de app eerst of het overschreven mag. Uitlezen (`drives.py`) valt bij een MDE-slave met
  0x1018:4 = 0 terug op SII 0x0E. **De firmware moet SII 0x0E zelf in 0x1018:4 zetten**, anders meldt de CoE-kant
  0. Getoetst met een nep-EEPROM (`sntoets.py`), niet op de print.
- **Recepten op de echte Swivel (14-10-2026)**: stap identiteit slaagt, `lees` 0x6020 (druk, een PDO-object)
  mislukte met een lege fout. Agent 1.14.1: `fouttekst(e)` toont soort, SDO-abortcode in woorden en `desc` (pysoem
  geeft bij een mailbox-time-out een exceptie zonder tekst). Uitkomst: `WkcError, wkc 0` — ook 0x1008/0x100A gaven
  niets: de werker vroeg SDO's binnen 0,4 s na `config_init`, vóór de Swivel in PRE-OP zijn mailbox had geopend.
  Agent 1.14.2: identiteit/test/monitor vragen eerst PRE-OP via `_vraag_toestand`, en `drives.py` wacht bij een
  MDE-slave op PRE-OP voor het uitlezen (daarom stond firmware op "niet gelezen"). Blijkt daarna dat PDO-objecten
  niet via SDO gaan, dan moeten lees/beweging/monitor via procesdata (config_map, SAFE-OP) — nog niet gebouwd.
- **EEPROM vullen uit het ESI** (agent 1.15.0, `taken/sii.py` + `mde.eeprom`; sub-vinkje "EEPROM vullen uit
  ESI", techniek `ecateeprom`; knoppen 🧩 EEPROM vergelijken en 💾 EEPROM vullen… in de positierij). `sii.maak`
  bouwt de SII volgens ETG.1000.6/ETG.2010: ConfigData (PDI-control: 0x80 = SPI op de LAN9252-familie, 0x05 = SPI
  op ET1100), CRC-8, vendor/product/revisie/serienummer, bootstrap- en standaardmailbox, protocollen, grootte,
  categorieën Strings/General/FMMU/SyncM/TxPDO/RxPDO/End. Het node-adres (alias, woord 4) van een gevulde EEPROM
  blijft staan. De werker leest eerst, logt per gebied wat verschilt, schrijft alleen afwijkende woorden in INIT en
  leest terug; een andere identiteit of een ander serienummer alleen na bevestiging. Daarna spanning uit/aan.
  Getoetst met het ESI van de M-Pede-WJ1 (444 bytes, `siitoets.py`) en een nep-EEPROM in zes gevallen
  (`eepromtoets.py`); **niet op de print**. Eerst "vergelijken" op een door TwinCAT geschreven EEPROM draaien om de
  omzetting te controleren. Productcode 0x00055955 moet 0x0000DA93 worden (fout van de firmwarebouwer): nieuw ESI,
  firmware 0x1018:2 en Controllertypen aanpassen, dan EEPROM vullen.
- **Meekijk-objecten uit het ESI** (14-10-2026): bij het openen van Meekijken leest `LaadMonitorUitEsi` het ESI
  van het type (actuele firmware eerst) en zet alle PDO-entries als vinkjes neer: TxPdo ("wat de print meldt")
  standaard aan, RxPdo ("wat hij krijgt") uit; opvulbits en onbekende types vallen weg, dubbele namen krijgen
  "(stand)"/"(doel)". De vinkjes schrijven het JSON-vak (`ZetMonitorJson`), dat inklapbaar blijft voor eigen
  objecten. Swivel: Status Word, Joint 1/2, Pressure / Control Word, Joint 1/2, Vacuum, Blowoff.
- **Parametersets optioneel** (14-10-2026, op verzoek): sub-vinkje "Parametersets laden" onder EtherCAT bij
  Producten ▸ Techniek. Omgekeerd opgeslagen als techniek `geenparams` (`ProductTechniek.GeenParameters`), zodat
  bestaande producten parametersets houden. Uit = in Test PCB geen keuze/knop/Vergelijken voor parameters en geen
  sectie Parametersets bij Techniek (behalve met Feetech). De aparte MDE-rij in Programmeren is weg: Objecten…
  en Meekijken… staan in de positierij; Identiteit (dubbel met Automatisch uitlezen en het recept) is vervallen.
  Geen SQL nodig: het vinkje is een regel in de bestaande tabel `ProductTechnieken`. Uit = ook de kolom
  Parameterset in de uitleescontrole weg. Basistest en IO-test (Ingenia: serienummers, 0x2602/0x2601) staan
  alleen nog bij een product met een Ingenia-positie (of zonder vastgelegde posities).
- **EtherCAT IO** (agent 1.16.0, 15-10-2026): taak `ecat.io` (`taken/ecat_io.py`, werker `--werker ecatio`). De agent
  is master met een procesdata-cyclus (2 ms, `timeBeginPeriod(1)`): alle slaves naar OP; digitale kanalen per slave
  uit de PDO-toewijzing via SDO (0x3020/0x6000/0x6100 in, 0x3220/0x6200/0x6300 uit; Omron GX: Sysmac-foutbyte
  0x2002:01). Opdrachten als JSON-regels via `POST /jobs/<id>/invoer` → stdin van de werker (uit per bit of masker,
  filter = Omron 0x3000, stop); live in job.resultaat (bij verandering, anders 2x/s). Stop = uitgangen 0, 20 cycli,
  INIT. Scherm Productie ▸ EtherCAT IO (`Components/Pages/EthercatIo.razor`): LED's voor ingangen, knoppen voor
  uitgangen, ingangsfilter bij Omron; GX-MD16xx toont 8+8 (PDO's hebben 16). Getest met een nagebootste pysoem
  (scratchpad `nep_gx.py`, cyclus ~2,0 ms, max ~2,6 ms) en een nep-agent voor het scherm; **werkt op een echte GX-MD1622** (gebruiker, 15-10-2026).
  Bedoeld als eerste stap naar de testrig (robot + GX in één keten, optie 1: agent als enige master).
- **Indeling Programmeren** (15-10-2026, op verzoek): eerst per positie de firmwarekeuze (en parameterset), dan
  het kader **USB** (chip programmeren, seriële monitor; ESP32 programmeren kiest de bovenaan gekozen firmware voor
  als die bij de chip hoort), dan het kader **EtherCAT** met per node een eigen kader (`prod-node`): Firmware laden,
  Parameters laden, Vergelijken/registers/encoder, EEPROM vergelijken, EEPROM vullen, Serienummer schrijven,
  Objecten, Monitoring achter elkaar; de eRob-hoekregel in hetzelfde node-kader. Alle knoppen van Test PCB (ook
  uitlezen, node-adressen en testen) staan achter elkaar, links, 9rem breed (eerst 12rem, op verzoek 25% smaller) en vast 2 regels hoog; icoon in `<span class="ico">` in een vaste kolom links, tekst ernaast;
  onder elkaar gaf bij meerdere nodes een te lange pagina. Meekijken heet nu **Monitoring**. Objecten en Monitoring zijn
  sub-vinkjes onder EtherCAT (`ecatobj`, `ecatmon`, opt-in zoals `ecatsn`; Objecten geldt ook voor de eRob-regel).
  De controletabel noemt de kolom Node.
- **ESP32: app-image nooit op 0x0** (agent 1.13.6, `esp32.soort_image`/`_juiste_offset`): een los ESP-IDF
  app-image (0xE9 + `esp_app_desc_t`-magic 0xABCD5432 op 0x20) op 0x0 overschrijft de bootloader; de ROM laadt de
  app dan als bootloader → "SHA-256 comparison failed" + TG0WDT-lus (twee keer gezien op de Swivel; het venster
  stond standaard op 0x0). De agent zet een app-image op 0x10000 en een samengevoegd image (0xAA50 op 0x8000) op
  0x0, en weigert "eerst flash wissen" met alleen een app-image. Beste route: `idf.py merge-bin` in de filestore.
  Eigen prints: M-Pede-WJ1 (Swivel, 0x00055955: 2 joints ±512, kleppen,
  druk), 6-voudige stappenmotorsturing, Display v2 (ESP32 flashen via USB + EtherCAT voor test/firmware),
  Gripper. Nog nodig van de gebruiker: bits van 0x7000/0x6000 en bevestiging van het 0x8000-protocol.
- Hardware: EtherCAT via pysoem (eigen werker-processen voor scans: `--werker drives|coe|alias`),
  Ingenia Summit (Everest/Capitan) via ingeniamotion (`_ingenia.py`: verbinding, STO-check `sto_vrij`,
  `motor_aan`, SDO-herhaling `sdo()` tegen EMI-haperingen, `uid_voor(idx)`), eRob (ZeroErr) via CoE-SDO's en
  via het eigen CAN-protocol (0x640+ID / 0x5C0+ID) met een GCAN USBCAN (`can_bus.py`, 32-bit DLL via
  `CanLuisteraar.exe`), ESP32 (esptool), STM32 (pyocd), Feetech-servo's (serieel).
- Testen voor drives: stroomlus (disturbance+monitoring), encodertest (interne generator, 1 cyclus = 1 omw),
  phasing, motiontest, dual motion (twee aliassen op één MotionController), IO-test (bruggen uitgang→ingang),
  EtherCAT node-adressen (EEPROM woord 4 + CRC-8 in woord 7), encoder-live (wijzerplaat, BiSS full frame).
- Bekende hardwarefeiten: Mosrac 17BM BiSS-C = 15 MT + 17 ST + E + W + CRC6 (40 bits), Summit-register
  0x204B is U32 → bovenste MT-bits vallen af; positie bij omgekeerde polariteit = 2³¹ − telling (Ingenia);
  31-bit positiebereik zonder teken; positie-offset = 0x2438; alle testen rekenen overloop-veilig (`_verschil`).

## Werkafspraken
- Geen bestanden verwijderen via tooling (markeer als "NIET IN GEBRUIK"); geen `C:\Windows\System32` als
  safe.directory; web alleen via de normale fetch/search-tools.
- Eerst uitleggen wat je gaat bouwen bij grotere of gevaarlijke dingen (bewegende assen, schrijven in drives);
  bij hardwarestappen altijd: motoren uit in `finally`, registers terugzetten, STO-check vooraf.
- Na het bouwen: zeg welke stappen bij de gebruiker liggen (publiceren, agent bouwen/installeren, SQL draaien).
- Historie-regels in de app noemen concreet wát faalde (drive/uitgang/register), niet alleen een telling.

## Bestellingen: popup Nieuwe bestelling (16-10-2026)

Op verzoek: "+ Nieuwe bestelling" opent `Components/NieuweBestellingVenster.razor` (was een blok met twee lange
keuzelijsten). Links leverancier, rechts werkorder; leeg zoekveld = wat deze werknemer de laatste tijd gebruikte
(`IBestelRepository.RecentGebruiktAsync`: Supplier en Workorder-id uit Orders op Created by, nieuwste eerst), typen zoekt
op alle woorden (leveranciers; open werkorders op nummer, omschrijving, project, klant). Pijltjes + Enter kiezen en gaan
door naar het volgende veld, daarna staat de focus op Aanmaken; Ctrl+Enter = aanmaken, Esc = sluiten. Werkorder mag
leeg ("Zonder werkorder"); "Uitgebreid zoeken" opent de bestaande `WerkorderKiezer` (ook gesloten, Recent, Persoonlijk).
Aanmaken zelf is ongewijzigd (`NieuweBestellingAsync`). Lokaal getest zonder op Aanmaken te drukken (zou in de echte
database schrijven).
**Alleen werkorders met `Workorders.InkoopToegestaan <> 0`** (besluit gebruiker; waarden -1/1 = ja, 0/NULL = nee):
`Workorder.InkoopToegestaan` (uit `WorkorderQuery`, ontbreekt de kolom dan ja). Geldt in de popup (lijst en
`WerkorderKiezer AlleenInkoop`), in de keuzelijst Werkorder van het orderformulier (`_werkorders`; de huidige werkorder van
een bestelling komt er per bestelling bij met "(geen inkoop toegestaan)" of "(gesloten)", `WerkorderLijst`) en als controle
bij opslaan van `Workorder-id` en bij aanmaken (`WerkorderNiet`).

## Nacalculatie (19-10-2026)

`/financien/nacalculatie` (Financiën ▸ Facturen ▸ Nacalculatie én Projecten ▸ Rapporten ▸ Nacalculatie, recht Financial),
`Components/Pages/Nacalculatie.razor` + `Services/INacalculatieRepository.cs`. Overgezet van Access' formulier
`_Nacalc_Analyse` met de rapporten Kostenoverzicht totaal, Directe PO's, Materials, Hours (per jaar) en Hours details
(Combinatierapport = tab **Alles**, afdrukken met `@media print`). Links projecten zoeken (klant / projectnummer of
omschrijving, onthouden per pc via `mdeOpslag`) en aanvinken, eventueel werkorders (geen = alle); `?project=` en
`?werkorder=` zoals Access' OpenArgs.
- **Rekenregels = de MariaDB-views `_Nacalc_*`** (dezelfde als Access). `_Nacalc_Totaal` rekent altijd alles uit (± 3,5 s);
  daarom worden de bladeren gefilterd op werkorder opgevraagd (< 0,1 s): `_Nacalc_Uurkosten_Overview`,
  `_Nacalc_ProjectMaterials_TotalPerSupplier_Ordered`, `_Nacalc_Orders_Overview`. Besteld = orderregels bij status 4-6,
  gefactureerd = gekoppelde inkoopfacturen (ook bij geannuleerde orders; die staan doorgestreept in de lijst als ze een
  factuurbedrag hebben). Materiaal = projectmaterialen "vrijgegeven voor bestellen" tegen vastgestelde of geschatte
  kostprijs (0 bij `MateriaalKostenNietMeenemen`). Uren: tariefsoort 1 = reisuur + km, 2-5 = werkuur, overige 0.
- **Nieuw t.o.v. Access**: kolom **Inkoop** = per bestelling het gefactureerde bedrag, zonder gekoppelde factuur het
  bestelde; **Totaal** = inkoop + materiaal + uren (de opbouw van het Access-rapport zelf was niet te zien, alleen de
  query). En **uren zonder kostprijstarief** worden getoond (de view laat ze stil weg): sinds 2024 1.945 regels / 6.113 uur,
  waarvan 1.519 buiten alle tariefperiodes, 165 precies op een begindatum (de view eist `StartDatum > Begindatum`) en 261
  van 5 werknemers zonder tarief.
- Getest met project 223159 (getallen gelijk aan `_Nacalc_Totaal`), rechten tijdelijk lokaal opengezet en teruggezet.

## Bestellingen: regels uit een screenshot of PDF (19-10-2026)

Bestellingen ▸ regels ▸ Importeren ▾ ▸ **📷 Uit screenshot of PDF** (`Components/ScreenshotVenster.razor`,
`Services/BestelregelLezer.cs`, via `BedrockClient`). Plakken (Ctrl+V na Win+Shift+S), kiezen of slepen; een PDF gaat als
document met citations. Een geplakte afbeelding gaat **als stroom** naar .NET (`wwwroot/klembord.js`: paste-event bewaart de
bytes, seintje `AfbeeldingGeplakt`, daarna `mdeKlembord.afbeelding()` → `IJSStreamReference`; de bytes zelf teruggeven,
níet `DotNet.createJSStreamReference`, dat wikkelt Blazor zelf al in). Grote berichten in één keer gaan niet over SignalR.
Voorbeeld met nagerekende regels (aantal × prijs vs. regeltotaal, ⚠), MDE-nummer via `ZoekPlakArtikelAsync`, verzendkosten
als optionele regel, omrekenen incl→ex btw **standaard uit** (de AI gokte "incl. btw" bij Hoffmann zonder dat het er
stond; nu alleen een waarschuwing). Opdracht: artikelnummer letterlijk ("478450 0,5"), merk tussen haakjes achter de
omschrijving. Getest met een Hoffmann-winkelwagen (6 regels, alles goed, 4,6 s); Toevoegen niet aangeklikt (zelfde
`BewaarRegelAsync` als Plakken uit klembord).

## Artikelen ▸ Leveranciers (19-10-2026)

Tabblad Leveranciers in `Components/Pages/Artikelen.razor` (Access: subformulier ArtikelSupplier en
ArtikelenLaatstBesteld). Tabel `ArtikelSupplier`: leverancier, voorkeur (`PreferedSupplier`, Access schrijft -1),
bestelnummer, VE (`VerpakkingsEenheid` = VE-ID als tekst, standaard 2 = st), stuks per VE, prijs per VE, korting als
fractie (0,6 = 60 %; invoer "60" of "60%" wordt 0,6). Prijs per stuk = prijs/VE ÷ stuks × (1 − korting). Opslaan per
veld; toevoegen via zoeken in leveranciers (`IRelatieRepository.ZoekAsync` met alleen leveranciers); de eerste wordt
voorkeur. In plaats van Access' vraag "algemene prijzen updaten?" een knop zodra de kostprijs afwijkt van de hoogste
leveranciersprijs (zoals Artikel_MaxInkoopprijs, afgekapt op 3 decimalen): `ZetKostprijsAsync` (verkoop = kostprijs +
toeslag; toeslag 0 = niets doen) plus twee regels in `ArtikelPrijsHistory` zoals Access' LogKostPrijs. Laatst besteld
uit de view `@LaatstBesteldeArtikelen`, PO opent in Bestellingen. Rechten = `MagBewerken` van het artikelscherm.
Getest lokaal alleen lezend (artikel 830 gelijk aan Access); toevoegen, wijzigen, weghalen en kostprijs bijwerken zijn
niet tegen de database getest.

## Welkomstscherm hal (20-10-2026)

Relaties ▸ Onderhoud ▸ **Welkomstscherm hal** (`/relaties/welkomstscherm`, `Components/Pages/Welkomstscherm.razor`) en
de knop "📺 Welkom heten" bij een relatie (opent het scherm met die relatie). Instellen: **Directie of
Budgetverantwoordelijke** (besluit gebruiker; er is geen recht projectmanagement). Tabel `Welkomstbord`
(`Welkomstbord_MariaDB.sql`, door de gebruiker te draaien), `Services/IWelkomRepository.cs` (MySql/Geen; lokaal
`Welkom__Geheugen=true`). Per welkom: datum van/tot, tijd van/tot (leeg = hele dag), relatie, gastheer (ZoekKeuze over
werknemers in dienst), tekst.
- **Tv**: `/hal/<sleutel>` zonder inlog (minimal API in Program.cs, `Services/WelkomSchermService.cs`); sleutel in
  `Welkomstscherm:Sleutel` (minstens 12 tekens, staat in appsettings.json; verkeerde sleutel = 404). Losse HTML, geen
  Blazor: klok, ons logo (`/hal/<sleutel>/eigen-logo`: Welkomstscherm:Logo of het factuurlogo 5254), "Welkom", relatie,
  tekst, gastheer met pasfoto (`/werknemerfoto/{id}`, was al openbaar) of initialen. Haalt elke minuut
  `/hal/<sleutel>/data` op, wisselt bij meerdere tegelijk elke 12 s, laadt elke 6 uur opnieuw. Niemand gepland =
  `Welkomstscherm:StandaardTekst`. Voorbeeld van één welkom: `?voorbeeld=<id>` (knop in het beheerscherm).
- **Logo van de relatie**: het nieuwste plaatje met "logo" in de naam bij de documenten van die relatie in de
  filestore (StoreType 311, wens gebruiker); het beheerscherm kan er een toevoegen (bestandsnaam "logo.<ext>").
- Getest lokaal met de geheugenvariant: aanmaken (Mosa, Peter Moerland, tekst), "nu op tv", tv-pagina op 1920×1080,
  data-JSON leeg en gevuld, verkeerde sleutel 404. Logo uploaden niet getest (schrijft in de echte filestore).

## Bestellingen: werkorder zoeken in het orderformulier (19-10-2026)

Het veld Werkorder op tab Algemeen is een zoekvak (`Components/ZoekKeuze.razor`) in plaats van een lange keuzelijst:
`Veld.Zoekbaar = true` in `BestelVelden`, `VeldRaster` toont dan ZoekKeuze (andere schermen ongewijzigd). Typen filtert op
alle woorden in nummer · project · omschrijving en ook op de klant: de lijst bestaat uit `ZoekbareKeuze` (Extra = klant),
de klant staat klein in de uitklaplijst maar niet in het veld (eerdere wens). Pijltjes/Enter kiezen, Esc of wegklikken =
niets wijzigen, "—" = leegmaken. De lijst blijft: open werkorders met inkoop toegestaan plus de huidige. Getest lokaal met
gesimuleerde invoer ("mosa" → 17 werkorders, "224121 rapid"); niet gekozen (zou opslaan).

## Artikelen ▸ Voorraad/Bestellen en weggehaalde velden (19-10-2026)

- Tabblad **Voorraad/Bestellen** (Access: zelfde tabblad in ArtikelenSubForm), voorlopig alleen (wens gebruiker):
  actuele voorraad (view `@VoorraadActueel`) met Overzicht = alle voorraadmutaties (`IVoorraadRepository.HistorieAsync`,
  plaatsnamen via `ILokatieRepository.BoomAsync`; Access-rapport ArtikelVoorraadMutatiesHistory) en besteld tot nu toe
  (view `@OrderItems_BesteldTotaal`, orderstatus 4-6) met Historie = bestellingen met besteldatum
  (`IArtikelRepository.BestelHistorieAsync`; Access-rapport ArtikelBestelHistorie, hier LEFT JOIN). Let op: in het
  Overzicht tellen ook leveringen op een werkorder mee (projectmateriaal, geen plek); die zitten niet in de actuele
  voorraad, net als in Access. Minimum voorraad, in aanvraag, nalevering, besteladvies en binnengeboekt nog niet.
- Uit het artikelscherm weg (staan bij Maakdelen): materiaal, gewicht, plaatdikte; de waarden blijven bij opslaan
  ongewijzigd. Getest lokaal alleen lezend op artikel 830 (1 op voorraad, 5 besteld, gelijk aan Access).

## Voorraadstand per datum (19-10-2026)

Financiën ▸ Rapporten ▸ **Voorraadstand** (`/financien/voorraadstand`, `Components/Pages/Voorraadstand.razor`), van het
Access-formulier VoorraadStandPerDatum (query en rapport VoorraadStand). `IVoorraadRepository.StandOpDatumAsync`:
som van `VoorraadMutaties.Mutatie` per artikel en status, zonder projectmateriaal (`WerkorderID` leeg), met
`@Artikelen.VastgesteldeKostprijs`. **Verschil met Access**: Access vergeleek `MutatieOp <= datum` (= middernacht,
dus zonder de mutaties van die dag); hier tot en met de gekozen dag. Kaartjes per status, groepen met subtotaal,
zoeken in de lijst, MDE-nummer opent `ArtikelVenster`, artikelen zonder kostprijs grijs. Excel: `Services/XlsxSchrijver.cs`
(OpenXml, getallen als getal, geld als "€ #,##0.00", kopregel vast), herbruikbaar voor andere exports. Getest: totalen
gelijk aan een eigen controlequery (19-10-2026: € 481.778,10), xlsx geopend met Excel. De grijze tegel Voorraadstand
onder Voorraad ▸ Rapporten is blijven staan.

## Werkplek-inlog: vaste gebruikers bovenaan (19-10-2026)

In "Wie ben jij?" (`Components/Layout/WerkplekMedewerker.razor`, ook de vergrendeling) staan bovenaan onder "Vaak op
deze werkplek" de zes mensen die hier het vaakst inlogden, daaronder "Iedereen" (zonder de bovenste). Bij zoeken één
lijst, vaste gebruikers eerst. Geteld na elke geslaagde pincode in de browser van die pc (`localStorage`
"werkplekAanmeldingen", via `mdeOpslag`): dus per werkstation, geen tabel; browsergegevens wissen = telling opnieuw.
Getest lokaal met `Werkplekken__Geheugen` (testwerkplek gekoppeld, Kees Blok pincode 2580).

## Pakbonnen (19-10-2026)

Bestellingen ▸ Verzenden ▸ **Pakbonnen** (`/bestellingen/pakbonnen`, `Components/Pages/Pakbonnen.razor`), overgezet van
het Access-formulier Pakbonnen (PakbonSub, PakbonSub2, PakbonNieuw, rapporten Pakbon/PakbonPlaatjes, Verzendlabel).
Bestaande tabellen `Pakbonnen`, `PakbonItem`, `PakbonStatus`; geen SQL-script nodig. `Services/IPakbonRepository.cs`
(MySql/Geen; lokaal `Pakbon__Geheugen=true`: leest echt, schrijft in het geheugen; let op: een nieuwe contactpersoon
gaat via `IRelatieRepository` wél de database in).
- Links de pakbonnen (niet gewist, nieuwste eerst, zoeken op klant of PS-nummer); Wissen = `Deleted = 1` zoals Access.
- Nieuwe pakbon: klant zoeken, t.a.v. kiezen of nieuwe naam (Access eiste een contactpersoon; nieuwe naam = nieuwe
  rij in Contacts), status 1 (Aanmaken), `sGUID` en `AangemaaktDoorID` zoals Access.
- Regels: + Regel, Uit PO (alle regels aan behalve kostenregels zonder artikelnummer; gewicht uit Artikelen, Remark
  "Uit PO-…"), Uit artikelen (`OfferteArtikelZoeker` met `MetPrijzen=false`, dekt ook maakdelen; aantal 1).
  Getallen: "1,5" en "1.5" allebei anderhalf. Opslaan per veld, met een slot tegen wisselen tijdens opslaan.
- Afdruk (`Services/PakbonPdfService.cs`): zelfde opmaak en Edge-route als de factuur (`FactuurPdfService.Begin/Meta/
  Plaats` zijn daarvoor internal), afzender `Pakbonnen:Afzender` (standaard 5254), regels samengevoegd zoals het
  Access-rapport, referenties = de verschillende InkooporderKlant, kolom kg alleen als er gewicht is, "Melding op bon"
  onderaan. Met afbeeldingen: artikelfoto via `LabelService.ArtikelFotoAsync`. Verzendlabel: A5 liggend.
- **Nog niet**: het Dymo-verzendlabel. Access printte op "DYMO LabelWriter DUO Label", lade 1, labeltype 1933088
  (lokale Access-tabel Settings), met LargeShipping.label; dat is een andere printer dan de labelprinter van de app.
- Getest lokaal: lijst, openen, drie PDF's (PS-1705, met foto's), nieuw, regels, uit PO, uit artikelen, status, wissen
  (in het geheugen).

## Financiën afgeschermd (19-10-2026)

Besluit gebruiker: Financiën (menu en schermen) is **volledig** dicht voor wie geen Financial of Directie heeft, ook de AI
(`/ai`, de ✨-knop) en Banktransacties (de rechten per rekening gelden daarbinnen nog). Eén plek:
`HuidigeGebruiker.MagFinancien` en `IsFinancienPad` (`/financien/...`, `/module/financien`, `/ai`). `MainLayout` toont
dan "Geen toegang" in plaats van de pagina (zolang de gebruiker laadt "Laden…", zodat de pagina niet al data ophaalt),
de module staat niet in het menu (ook niet door tegels zonder recht) en niet in het zoekvenster. Directie ziet in de
module ook de tegels met recht Financial (`Module.razor`). Een nieuw financieel scherm: adres onder `/financien/`, dan
valt het vanzelf onder het slot. Getest lokaal: zonder recht dicht (liquiditeit, ai, module), met tijdelijk recht open.

## Factuurinbox: invoice@ en bonnetjes van de telefoon (19-10-2026)

Besluiten gebruiker: bonnetjes zijn betaald met de **zakelijke pas** (geen declaratie), werkorder **niet verplicht**
(de beoordelaar mag hem toevoegen), inbox zien en verwerken = **Financial of Directie** (zelfde als facturen invoeren),
een mail zonder factuur komt ook in de inbox ("Geen factuur gevonden").
- Tabellen `FactuurInbox` + `FactuurInboxBestand` (`FactuurInbox_MariaDB.sql`, door de gebruiker te draaien),
  `Services/IFactuurInboxRepository.cs` (MySql/Geen/Geheugen; lokaal `FactuurInbox__Geheugen=true`). Wat de AI las
  staat als JSON in `Gelezen` (`InboxGelezen`, want `FactuurUitPdf.Gevonden` heeft geen setter).
- **Bonnetje**: MDE mobiel ▸ tegel Bonnetje (`Components/Pages/MobielBon.razor`, `/mobiel/bon`). Foto via
  `InputFile capture="environment"`, in de browser verkleind met `RequestImageFileAsync` (2000 px), lezen met
  `FactuurLezer` op de achtergrond terwijl de collega soort/werkorder kiest; Versturen wacht op het lezen.
  Werkorders: recent door jou (uren, alleen `InkoopToegestaan`) of zoeken (`ZoekInkoopWerkordersAsync`).
- **Beoordelen**: Inkomende facturen ▸ knop "📥 Te beoordelen (n)" (blok boven de filters, opent vanzelf als er iets
  ligt). Beoordelen = `FactuurVenster` met `UitInbox`: bestanden, gelezen velden, kopje met herkomst; bij een bon
  Betaald aan en termijn 0 (niet nog eens via SEPA) en de werkorder als wachtende koppeling (nieuwe PO, bedrag volgt
  ex btw via `WachtKoppeling.Volgt`). Opslaan = gewone factuur, inbox-regel Status 1 (`FactuurToegevoegd`); Negeren = 2.
- **invoice@**: `Services/FactuurInboxOphaler.cs` (BackgroundService, sectie `FactuurInbox`: Aan, Postbus, Minuten,
  Vanaf, Label). Gmail-zoekopdracht `in:inbox -label:"EverythingApp/Verwerkt"`, per PDF/foto een regel (logo's onder
  20 kB overgeslagen, overige bijlagen gaan mee met de eerste), daarna het label (`MailService.VoegLabelToeAsync`, maakt
  het label zo nodig aan). Dubbel tegengehouden door label én `MailId` in de tabel; een mail die faalt wordt tot de
  volgende herstart overgeslagen. **MailService is scoped**: de ophaler maakt per ronde een scope (anders start de app
  niet). Niets wordt uit de postbus verwijderd.
- **Nog niet getest tegen de echte postbus**: lokaal is `Google:ServiceAccountJson` leeg (melding getest). Te
  controleren na inschakelen: of invoice@ een echte gebruiker is (geen groep), het label, en de eerste ronde vanaf
  `Vanaf`. Bonnetje en beoordelen getest met een verzonnen tankbon in het geheugen; Opslaan niet aangeklikt.

## Relaties: leverancier uit een screenshot of PDF (19-10-2026)

Relaties ▸ **✨ Uit screenshot** (`Components/RelatieUitScreenshot.razor`, `Services/RelatieLezer.cs`, via
`BedrockClient.LeesBeeldAsync`, dezelfde plak-/kiesroute als het bestelscherm). De AI leest naam, adres, land (Engels,
gekoppeld aan tabel `Country`), telefoon, e-mail, website, KvK, btw, IBAN en BIC; alles is daarna nog te wijzigen.
Aanmaken = `NieuweRelatieAsync` + `UpdateVeldAsync` per veld, Leverancier standaard aan. Waarborgen:
- `IRelatieRepository.MogelijkDezelfdeAsync`: zelfde KvK/btw/IBAN of onderscheidende woorden uit de naam (stoplijst:
  netherlands, express, bv, gmbh, ...). FedEx werd eerst niet gevonden omdat alleen op "Netherlands" werd gezocht.
- Een treffer met `Werkmaatschappij`/`MDEBV` ("EIGEN BV") = gegevens van MDE zelf: veld leeggemaakt met ⚠. De AI nam
  bij FedEx "Uw BTW nummer" over; de opdracht verbiedt dat nu ook.
- IBAN alleen opgeslagen als mod 97 klopt. `Bankgegevens.Ingekort`: te lang voor het land (lengtetabel) en het begin
  klopt wél, dan dat begin met ⚠ (Materialise: KBO-nummer achter de IBAN geplakt).
Getest met de FedEx-factuur (PNG) en Materialise (PDF); Aanmaken niet aangeklikt.

## Facturen uitlezen via Bedrock (19-10-2026)

`Services/FactuurLezer.cs` gebruikt `BedrockClient` zodra Bedrock is ingesteld (anders Ollama als terugval; `Ai:Bron`,
`Ai:Sleutel/Model/Adres` en de directe Anthropic-route zijn vervallen). Drie wegen: digitale PDF = tekst eruit (PdfPig) en
alleen tekst naar het model; **gescande PDF** = de PDF zelf als Converse-`document` met `citations: {enabled: true}`
(zonder citations leest Bedrock alleen de tekstlaag; met citations bekijkt Claude elke pagina als beeld; die werden
voorheen geweigerd); foto = `image`-blok. Grenzen: beeld 3,75 MB, document 4,5 MB. Tekst komt uit `text`-blokken en, met
citations, uit `citationsContent.content[].text`. Getest (Haiku 4.5) op 3 echte inkoopfacturen + een foto en een
beeld-PDF gemaakt van één ervan: 3-4,5 s per factuur (Ollama: minuten). Materialise gelijk aan de administratie; DHL
(scan) gelijk aan pagina 1 (de boekhouding splitste 7,58 als invoer-btw uit pagina 2 af); FedEx las invoerrechten als btw →
huisregel in `Templates/FactuurUitlezen.txt` (invoerrechten/voorschotprovisie zijn geen btw), daarna gelijk aan de
administratie, ook als scan. Testtip: een PDF-pagina naar PNG met `Windows.Data.Pdf` (TFM net8.0-windows10.0.19041.0).
- **Meteen uitlezen** (wens gebruiker): na kiezen/slepen start `Lees` vanzelf, als de factuur nog geen factuurnummer heeft
  (niet bij een bijlage achteraf); bij meerdere bestanden het bestand met factuur/invoice/rekening/bon in de naam.
- **IBAN/BIC aanvullen**: de AI leest ook `bic`. Ontbreekt IBAN of BIC bij de leverancier, dan stelt het venster voor die
  aan te vullen (knop Toevoegen; `IFactuurRepository.VulBankgegevensAanAsync` vult alléén lege velden). IBAN alleen met
  kloppend controlegetal (`Services/Bankgegevens.cs`, mod 97); BIC van de factuur alleen bij dezelfde rekening, anders bij
  een NL-IBAN afgeleid uit de bankcode (tabel van grote NL-banken). Een IBAN met fout controlegetal geeft geen fraudealarm
  meer maar "niet betrouwbaar gelezen" (Materialise: twee rekeningen op de factuur, model las een onjuiste). Getest in de
  browser (rechten tijdelijk lokaal open, testfactuur via een lokale testserver in de file-input): uitlezen start vanzelf,
  voorstel verschijnt bij een leverancier zonder IBAN; op Toevoegen niet geklikt (schrijft in Relations).

## Vragen aan AI, Financiën (19-10-2026)

Besluit gebruiker: AI **alleen via Amazon Bedrock** (EU). Claude mag, maar alleen via Bedrock (gekozen model:
`eu.anthropic.claude-sonnet-5-5`, regio eu-north-1); géén directe Anthropic-API en géén Ollama. De bestaande routes 'ollama'
en 'claude' (api.anthropic.com) in `FactuurLezer`: **stap 1 gedaan (19-10-2026)**, zie hieronder. Stand
19-10-2026: **werkt** met `eu.anthropic.claude-haiku-4-5-20251001-v1:0` (regio eu-north-1, IAM-sleutels). Nodig waren:
IAM-policy (EU-profiel vanuit eu-north-1 + `foundation-model/*` met conditie `bedrock:InferenceProfileArn`; een conditie
`aws:RequestedRegion = eu-north-1` blokkeert, want het EU-profiel voert uit in o.a. eu-west-3), het Anthropic
use-case-formulier, en één keer gebruik in de playground door een beheerder (Marketplace-abonnement). Haiku 4.5 kent **geen
Flex** (400 "service tier is not supported") en geen aanroep zonder inference profile; de terugval naar `default` werkt.
**Eerste echte test**: het model nam de maandbedragen goed over maar rekende het vergelijkingstotaal fout (heel 2025 als
"jan t/m okt"). Daarom: omzet/kosten per maand zonder uitsplitsing geven één regel per maand met kolom **Cumulatief**, een
melding als de laatste maand nog loopt, en de opdracht om bij vergelijkingen beide perioden met even lange datums op te
vragen. Daarna klopte het antwoord (nagerekend). Gebouwd zijn stap 2 en 3:
- **Invoer** (`Components/AiVraagInvoer.razor`) op de pagina Financiën (`Module.razor`, alleen met Financial/Directie) en
  boven aan Financiën ▸ Analyse ▸ **Vragen aan AI** (`/financien/analyse`, `Pages/AiAnalyse.razor`). Keuze **Direct**
  (laag `Bedrock:LaagDirect`, standaard `default`) of **Mag wachten** (laag `Bedrock:LaagWachten`, standaard `flex`;
  alleen uitgevoerd in de daluren `Bedrock:DalurenVan/-Tot` 18:00-07:00 en het weekend). Klaar = melding bij de bel
  (Meldingen, soort `analyse`; `OrderID` is dan het nummer van de vraag, de bel linkt naar de analyse).
- **Tabel `AiVragen`** (`AiVragen_MariaDB.sql`, **door de gebruiker uit te voeren**) = wachtrij én logboek: vraag, status
  (wacht/bezig/klaar/fout/geannuleerd), antwoord (markdown, getoond met Markdig, HTML uit), gebruikte gegevens (JSON),
  model, laag, tokens, kostenschatting (prijzen per miljoen tokens uit appsettings). `IAiVraagRepository` + Geen/Geheugen.
- **`Services/AiAnalyseService.cs`** (BackgroundService): elke minuut of op een seintje; direct eerst, max 3 tegelijk,
  `ClaimAsync` maakt het ophalen veilig; hangt iets > 30 min op 'bezig' dan bij het starten terug naar 'wacht'. Tool-use
  lus (max 8 rondes). Kent het model de laag niet (400 met "tier"), dan opnieuw met `default`. `AiSignaal` ververst open
  schermen.
- **`Services/BedrockClient.cs`**: Converse-API over HTTP, zonder AWS-SDK. Aanmelden met `Bedrock:Sleutel` (Bedrock API-key,
  Bearer; AWS raadt een lange-termijnsleutel af voor productie) of `AccessKey`/`SecretKey` (+`SessionToken`) met eigen
  **SigV4** (`SigV4`, getoetst met de AWS-voorbeelden "signing key" en "get-vanilla"; canoniek pad dubbel gecodeerd, de ':'
  in de model-id wordt %253A). `serviceTier` = `{"type": "flex"}` enz.; zonder geen veld (default).
- **`Services/FinancieleAiTools.cs`**: de opvragingen (liquiditeit_prognose, verkoopfacturen, inkoopfacturen,
  omzet_per_maand, kosten_per_maand, banktransacties, vaste_lasten). Alleen lezen, max 200 regels plus totalen in de
  opmerking, getallen met een punt. Hergebruikt de repositories (geen proforma's; banktransacties alleen van rekeningen
  waar de vraagsteller bij mag). Omzet/kosten per maand zijn eigen queries en tellen **facturen tussen eigen BV's
  standaard niet mee** (`met_intern`): dit jaar 58 van 252 facturen, ± € 638k.
- **Grafieken** (`Components/AiGrafiek.razor`): het model zet een blok ```` ```grafiek {json} ```` in zijn antwoord (type
  staaf/lijn, titel, eenheid, labels, max 4 reeksen; formaat staat in de systeemopdracht). `AiAnalyse.Delen` knipt het
  antwoord in markdown en grafieken; de grafiek is server-side SVG met de eerste vier categoriekleuren uit het dataviz-palet
  (licht/donker), `<title>` per staaf/punt als tooltip en een uitklapbare tabel. Elke waarde wordt vergeleken met de
  opgevraagde gegevens (cellen en bedragen in de opmerking, op een cent): wat er niet letterlijk in staat geeft een
  waarschuwing. SVG-`<text>` staat in een `<g>`: direct in een Razor-codeblok leest Razor het als zijn eigen text-tag. De
  systeemopdracht is een `$$"""`-string (de accolades in het JSON-voorbeeld). Getest met Haiku: 20 staven, alle waarden
  gevonden; donker thema niet bekeken.
- **Niet alleen financieel (19-10-2026, wens gebruiker)**: `Services/AlgemeneAiTools.cs` met projecten_zoeken,
  werkorders_van_project (uren = ALLE jaren, zo genoemd), uren_per_project (standaard per werkorder opgeteld;
  `per_medewerker` alleen als daarnaar gevraagd), uren_per_medewerker (zonder Financial/Directie alleen eigen uren),
  bestellingen, voorraad (`IVoorraadRepository`), producten_overzicht, product_todos en (alleen Financial/Directie)
  nacalculatie_project met budget (`Workorders.Budget`). De pagina staat op `/ai` (✨ in de bovenbalk,
  naast de bel) en `/financien/analyse`; **sinds 19-10-2026 alleen voor Financial of Directie** (zie "Financiën
  afgeschermd"); later komen er andere vraagmogelijkheden voor iedereen. Of de financiële opvragingen meedoen staat per vraag
  in `AiVragen.Financieel` (bepaald bij het stellen: `Ik.Mag("Financial") || Ik.Mag("Directie")`, dus nooit op een werkplek;
  kolom via `ALTER ... ADD COLUMN IF NOT EXISTS` in het script; zonder kolom afgeleid uit de rechten van de werknemer).
  **Lessen uit de tests met Haiku**: het model telt slecht zelf op (2 van 18 werkordertotalen fout) en pakt soms een
  verkeerde kolom (uren alle jaren als "dit jaar"). Daarom: opvragingen geven de totalen op het gevraagde niveau zelf, en
  kolomnamen en beschrijvingen zijn ondubbelzinnig. Daarna klopten uren per werkorder, nacalculatie en producten (nagerekend).
- **Gesprekken / doorvragen (19-10-2026, wens gebruiker)**: onder het gesprek een vak "Doorvragen" (alleen in je eigen
  gesprek, als er niets meer loopt). `AiVragen.GesprekID` = de eerste vraag van het gesprek; `AiVragen.Berichten` = het hele
  gesprek zoals het naar Bedrock ging (inclusief toolUse/toolResult), bewaard na elke beantwoorde beurt. Een vervolgvraag
  begint met de `Berichten` van de laatste beantwoorde beurt (`AiAnalyseService.Voorgeschiedenis`; zonder: de vragen en
  antwoorden als tekst) en voegt de nieuwe vraag toe. Boven 120.000 tekens worden de toolResults van oudere beurten (behalve
  de laatste 6 berichten) vervangen door een notitie (`Inkorten`). De lijst links toont alleen het begin van elk gesprek
  met "+n vervolgvragen"; rechts staat het hele gesprek. Kolommen via `ALTER ... IF NOT EXISTS` in het script; zonder de
  kolommen geeft doorvragen een duidelijke melding. Getest met Haiku: "welke medewerkers ... op de grootste twee
  werkorders?" na de urenvraag koos zelf 223159-11/-10 en klopte (nagerekend), 10k tokens in.
- Lokaal testen: `Bedrock__Nep=true` (geen AWS; eerste ronde vraagt echt de omzet op, dan een vast testantwoord) en
  `AiVragen__Geheugen=true`. Getest met die twee (rechten tijdelijk lokaal opengezet en teruggezet): direct-vraag loopt van
  wachtrij tot antwoord met gegevens, 'Mag wachten' blijft overdag in de wachtrij. **Niet getest tegen echte Bedrock**
  (nog geen sleutel): de vorm van tool use bij het gekozen model, flex-ondersteuning, de SigV4 tegen AWS zelf.

## Todo bij producten (19-10-2026)

Wens: punten die bij een exemplaar nog moeten gebeuren (bv. de print in een robot moet nog worden doorgesoldeerd), en
een overzicht "welke robots moeten nog worden doorgesoldeerd". Tabel `ProductTodo` (`ProductTodo_MariaDB.sql`, **door de
gebruiker uit te voeren**): per punt het exemplaar, `Wat` (kort en herbruikbaar, het overzicht groepeert erop; bij het
invoeren een keuzelijst met eerdere waarden en dezelfde schrijfwijze als een bestaande), `Toelichting`, aangemaakt/gereed
(datum + werknemer). Los van de oude Access-tabel `Todo` (34 todo's per werkorder/activiteit), die blijft ongemoeid.
- `Services/IProductTodoRepository.cs`: `VanExemplaarAsync` (dit exemplaar + alles eronder, recursieve CTE over
  `ParentProductenID`), `OpenPerExemplaarAsync` (open punten per exemplaar van een type, inclusief die van onderdelen),
  `OverzichtAsync` (met het bovenste product, "wortel", plus klant/project/status daarvan), `WatLijstAsync`.
- Producten ▸ exemplaar ▸ tab **Todo** (`Components/ProductTodoTab.razor`): toevoegen (Enter), afvinken, weer open, ✕ met
  "Zeker?" voor iets dat per ongeluk is ingevoerd. Punten van onderdelen staan erbij met een link naar dat onderdeel.
  Toevoegen/gereed/weer open komen in de historie van het exemplaar. Tabtitel "Todo (n)" en een oranje stipje met het
  aantal in de exemplarenlijst.
- Producten ▸ Overzichten ▸ **Todo-overzicht** (`/producten/todo`, `Pages/ProductTodos.razor`): knoppen per Wat met
  aantallen, zoeken, "ook gereed", afvinken, afdrukken (`@media print` verbergt menu en balk; eerste printstijl in de app).
- Queries getest tegen de echte data met nep-punten in plaats van de tabel (kabel in MPR-1 16 → wortel MPR-1 16, klant
  Viscon). Tabel aangemaakt door de gebruiker en op de server getest (19-10-2026): werkt.
- Het oranje bolletje heeft een eigen kolom in de exemplarenlijst (`.todo-kol`, raster 1,4rem) en een klik erop opent dat
  exemplaar op de tab Todo (`TodoStipKlik`, met `@onclick:stopPropagation`).
- Los hiervan (melding collega): een wijziging van het veld Project ververst nu ook de exemplarenlijst (`Bewaar`).
- **Kopiëren** (tab Todo, "Kopiëren naar andere exemplaren…"): open punten kiezen, doel = links aangevinkte exemplaren of
  serienummer van t/m (zelfde type, `_alleProducten`, zonder gewiste). `KopieerAsync` voegt per doel alleen toe wat daar
  nog niet open staat (zelfde Wat + Toelichting), in één transactie; per doel één historieregel "Todo gekopieerd van ...".
  Een punt van een onderdeel komt bij het doel zelf met "bij <type>" in de toelichting (geen koppeling naar het onderdeel
  van het doel).

## Zoekvak + lijst als combobox (19-10-2026)

`wwwroot/zoeklijst.js` (geladen in App.razor), één globale keydown-listener. **Opt-in**: het zoekvak krijgt
`data-zoeklijst`; zijn lijst is de eerstvolgende `.wn-namen` (of `[data-zoeklijst-lijst]`) tot 6 niveaus omhoog.
Automatisch herkennen is geprobeerd maar ving ook scanvelden (Producten "Serienummer of scan", SN-veld in
SamenstellingKiezer) en het formulier Nieuw project; daarom expliciet. **Enter** in het zoekvak: na 500 ms rust in de
lijst (een zoekvak met onchange zoekt pas na de Enter) en precies één `button.wn-naam` → die wordt aangeklikt; niet bij
`data-zoeklijst="pijltjes"` (zoekvakken die ook scannen: Artikelvoorraad, Dragers). **Pijltje omlaag**: naar de eerste
regel; in de lijst pijltjes/Home/End, Enter = de knop zelf, Esc of omhoog op de eerste = terug naar het zoekvak, een
letter of spatie = verder typen. Een lijst met `tabindex` (Bestellingen) houdt zijn eigen pijltjes: alleen de focus
gaat naar de lijst. Focusstijl `.wn-naam:focus-visible`. Nieuw zoekvak met lijst? Zet er `data-zoeklijst` op.
WerkorderVeld en NieuweBestellingVenster hebben hun eigen toetsenbordafhandeling en doen niet mee. Getest op Relaties
(mosa + Enter opent de enige treffer; omron + pijltjes + Enter opent de gekozen).
Producten: bij het openen staat de cursor in "Serienummer of scan" (`_scanVeld.FocusAsync()` bij de eerste render),
zodat je meteen kunt scannen; "Zoek type" heeft `data-zoeklijst` (mpr-1 + Enter opent het type).

## Veld opslaan vs. ander record kiezen (19-10-2026)

Gemeld bij Producten: opmerking typen, zonder het veld te verlaten een ander exemplaar aanklikken, en de opmerking
leek niet opgeslagen. Oorzaak: het `onchange` vuurt bij het verlaten van het veld, vlak voor de klik; Blazor Server
laat een tweede event doorlopen zodra de eerste op een `await` staat. Het nieuwe exemplaar werd dus al geladen
terwijl de UPDATE nog liep, en daarna schreef `Bewaar` de oude waarde in `_velden` van het nieuwe exemplaar.
Fix in Producten, Bestellingen, Projecten, Relaties en Werknemers: `Bewaar` is een wrapper die de taak in
`_opslaanBezig` zet (`BewaarKern` doet het werk met `vanId`, het id van het moment van de wijziging, en raakt
`_velden` alleen aan als dat record nog open staat); `Kies…` doet `await WachtOpOpslaan()` voordat het de velden
van het nieuwe record laadt. Tweede helft (na de test van de gebruiker): heeft het nieuwe record dezelfde waarde
als het oude vóór het typen (bv. allebei leeg), dan ziet Blazor geen verschil en blijft de getypte tekst in het
DOM-veld staan. Daarom `@key` op het veldenblok met het record-id (VeldRaster in Producten/Bestellingen/Projecten,
de `wn-velden`-div in Relaties/Werknemers): bij een ander record worden de velden opnieuw opgebouwd.
Niet in de browser getest (schrijft naar de database); bouwt wel.

## Tarieven berekenen (19-10-2026)

`/financien/tarieven` (Financiën ▸ Tarieven berekenen, recht Financial), `Components/Pages/Tarieven.razor`: het losse
gebruik van Access' InvoiceImportHours. Klant zoeken → project/werkorders/periode (zelfde keuze als het importvenster)
→ Uren tonen (gerekend met `IUrenImportRepository.RekenAsync`, regels in `Services/Tariefregels.cs`) → samenvatting per
werkorder/medewerker/uurtype + de losse urenregels. **Tarieven opslaan** = Access' "Bereken tarieven"
(`SchrijfTarievenAsync`, los van een factuur). **Factuur maken met deze uren** opent `VerkoopFactuurVenster` als nieuwe
factuur via `FactuurVoorstel.Regels` (nieuw: meerdere regels ineens; uren die al op een factuur staan doen niet mee).
Het importeren vanuit een bestaande factuur blijft `UrenImportVenster` (markup grotendeels gelijk; bewust niet samengevoegd).
In beide: na het aan-/uitvinken van een werkorder springt de periode naar de eerste t/m laatste dag met uren in die
keuze (`PeriodeVanUrenAsync`; wens gebruiker, zoals Access' 'zoek data' bij een klik op de werkorder).
Niet overgenomen: de Access-rapporten "Overzicht project" en "Weekoverzicht per klant". Niet getest in de browser
(lokaal geen Financial-recht); bouwt wel.

## Financiën: blok Analyse (19-10-2026)

In `Services/Menu.cs` bij Financiën een groep **Analyse** onder Rapporten, met Liquiditeitsoverzicht (verhuisd uit
Rapporten). Hier zet een collega van de gebruiker zijn tools bij: een `MenuItem` met recht `"Financial"` toevoegen.
De lege knop "Bankgegevens exporteren" (Bank) is weg.

## Uitgaande facturen: werkorder per factuur (19-10-2026)

Nieuwe kolom `Invoices.Workorder-id` (`FactuurWerkorder_MariaDB.sql`, gebruiker voert uit; zonder kolom blijft het veld
leeg en wordt het niet opgeslagen). In het factuurvenster onder Referentie het veld **Werkorder**
(`Components/WerkorderVeld.razor`, herbruikbaar): leeg zoekveld = werkorders van de klant van de factuur
(`IUrenRepository.WerkordersVanKlantAsync`, Projects.Customer-id), typen = alle werkorders; vinkje "ook gesloten";
pijltjes/Enter/Esc. Intern veld: ook na versturen aan te passen (buiten `regelsVast`), alleen met recht Financial/Directie.
`ZoekWerkordersAsync` zoekt nu op **alle woorden** (bv. "tembo bsl"), ook in de WerkorderKiezer.
Lokaal getest t/m kiezen (Amcor-werkorders, "tembo bsl"); opslaan niet getest.
In de lijst een kolom **Werkorder** (nummer; wolkje = klant, project, werkorderomschrijving), via één subquery in
`ZoekAsync` met CHAR(31) als scheiding. Ruimte (wens gebruiker): blok rechts 21 → 17rem, tabel min-width 88 → 92rem
(omschrijving houdt ~11rem).

## Product uitlezen: drives via de halffabricaten (16-10-2026)

"Automatisch uitlezen" (ExemplaarWerk) eiste attributen met een bron `drive:n:serienummer`; een product (bv. MPR-1) heeft
die niet, zijn drives horen bij de halffabricaten in de samenstelling. Nu **productmodus** (`ProductModus`: geen
drive-attributen, wel driveposities bij het producttype): de verwachte assen zijn de driveposities (`VerwachteAssen`),
type/firmware worden per positie gecontroleerd zoals altijd, en per gelezen serienummer zoekt
`IProductRepository.ZoekOpDriveSerienummerAsync` het halffabricaat (attribuut met bron drive:/can:…:serienummer) en
`KoppelHalffabricaten` vergelijkt met `GetOnderdelenAsync` (Producten.ParentProductenID): ✓ / "niet in de samenstelling"
(NO-GO) / onbekend (melding). Er wordt **niets opgeslagen** in productmodus. eRob via SDO heeft geen elektronisch
serienummer en is niet te koppelen. Query getest: SN 2148397740 → MPR-PCBA-RZ (576) in MPR-1 (330). Niet getest met
de agent/hardware.

## Materiaal inboeken (16-10-2026)

`/bestellingen/ontvangst` (Bestellingen ▸ Ontvangst ▸ Materiaal inboeken), `Components/Pages/Ontvangst.razor` +
`Services/IOntvangstRepository.cs`. Vervangt Orders_incoming_goods + _signoff + MateriaalGeleverd (Access).
- Lijst: `Orders.Status` 4 of 5 (zoals Access), zoeken op PO/leverancier/werkorder/project/klant/besteller. Er staan er
  ~480, waarvan veel oude die nooit helemaal zijn afgetekend.
- **Hele pakbon in één keer** (besluit gebruiker): pakbon (max 30, of "Onbekend"), "Ontvangen door" (standaard de
  aangemelde medewerker), per regel "Nu geleverd" vooraf ingevuld met wat open staat, opmerking. Meer dan open = vraag.
- Aftekenen (één transactie): per regel `OrderItemsDelivered` (pakbon, NOW, door, Remark) en bij een artikel
  `VoorraadMutaties` +aantal, event 4, status 1, WerkorderID van de bestelling (zoals Access MateriaalGeleverd).
  Alles binnen → zoals `OrderComplete`: Status 6, AllesIsGeleverd, Delivered date/by, Log_Orderchanges.
- **Toeslag/correctie-regels hoeven niet geleverd** (Access telde ze wel mee voor "compleet").
- Compleet → altijd een melding (📦) in de bel van NotifyTo of de besteller; mail "PO-n van LEV is compleet geleverd"
  alleen als `Notify_when_delivered` (zoals Access; nieuwe opmaak, `BestelMail.GeleverdHtml`), via eigen Gmail/alias.
- Weggelaten (besluit gebruiker): projectkrat scannen (nooit gebruikt), voorraadorders verdelen (4 van 1.362, in Access
  waarschijnlijk stuk door `MagazijnID`), mail per regel (nooit gebruikt).
- Lokaal getest t/m het scherm (PO-18351); **Aftekenen niet getest** (schrijft in de echte database).
- **Oude bestellingen opruimen** (alleen Directie, knop 🧹 bovenaan): bestellingen in status 4/5 besteld vóór een datum
  (standaard 1 januari van dit jaar; besteldatum leeg telt mee), vinkje per bestelling, dan `OpruimenAsync`: Status 6,
  Delivered date/by, logregel "opgeruimd, niet afgetekend". **Geen** leveringen of voorraadmutaties, AllesIsGeleverd
  ongewijzigd, geen mails/meldingen. Niet in de browser getest (lokaal geen Directie).

## Bestellingen: mail aan de leverancier (16-10-2026)

Vervangt `Mail_Click` + `Create_order_request_Mail` / `Create_order_Mail` (Access stelde een Outlook-mail samen).
Knoppen in de statusbalk: **✉ Offerte aanvragen…** bij Aanmaken (PI-), **✉ Bestelling versturen…** bij In bestelling (PO-).
`Components/BestelMailVenster.razor`: links Van/Aan/CC/onderwerp/aanhef en de bijlagen, rechts het voorbeeld (iframe
srcdoc); Versturen via de eigen Gmail (`MailService.Eigen`), op een werkplek via de alias. Opbouw in `Services/BestelMail.cs`.
- Inhoud zoals Access: taal 0 = NL, anders EN (geen Duits); onderwerp "AANVRAAG prijs levertijd PI-n" / "REQUEST price
  and delivery time PI-n" / "Order: PO-n"; aan = `Contacts.mail` van `Orders.Contact`; cc admin@mde-automation.nl, bij de
  bestelling ook `CCmailadressBijBestelling` (vinkje, als `CCbijBestelling`); OC-kolom als `Relations.GebruikOCcodes`
  (`Artikelen.OCcodeOmron`); "LET OP: DIT IS GEEN OPDRACHT" bij de aanvraag; prijzen + totaal alleen bij de bestelling;
  Purchasenote en afleveradres als blokken; ondertekening van wie verstuurt (niet-persoon → besteller).
- **Nieuwe opmaak** (wens gebruiker): inline-stijlen, blauwe tabelkop, zebra, logo als ingebed plaatje (`cid:logo-mde`,
  `wwwroot/logo-mde.png`; `MailService.VerstuurAsync(html: true, ingebed: …)` maakt multipart/related), adresregel van MDE
  uit Relations (`Bestellingen:EigenRelatieId`, standaard 5254). Weggelaten: lege prijskolommen in de aanvraag, de
  voettekst-.htm's uit Google Drive, het "officiële order"-PDF (`GenereerOfficieleOrder`, maar 1 leverancier).
- Bijlagen = StoreType 3 van de regels (vinkje per bestand), grootte van schijf; ZIP (`PI-n.zip`/`PO-n.zip`) voorgesteld
  boven 20 MB, maximaal 24 MB (Gmail). "Verouderd" = bij hetzelfde artikel een nieuwere StoreType 2 met dezelfde naam
  (Access keek alleen naar de naam).
- Controles (`BestelMail.Controles`): contact, regels, compleet afleveradres = blokkerend; afwijkende standaardcontact,
  leverdatum in het verleden, oude bijlagen, regels zonder prijs = waarschuwing. Aanhef leeg → invullen en (vinkje) opslaan
  in `Contacts.Aanhef`, zoals Access vroeg.
- Vrije tekst = de **Melding op orderformulier** (`Orders.Purchasenote`, het gele blok): in het venster aan te passen
  (voorbeeld volgt direct) en bij versturen opgeslagen bij de bestelling (besluit gebruiker; geen apart tekstvak).
- Na versturen: `Orders.MailAangemaakt` = nu, regel in `Log_Orderchanges` (groep Mail); bij de bestelling meteen de vraag
  "Nu op Besteld zetten?" (Access: "Vergeet de status niet vooruit te zetten").
- Lokaal getest t/m het voorbeeld (PO-18241, PI-18330); **echt versturen niet getest**.

## Bestellingen: Volgende, goedkeuren en de bel (16-10-2026)

In Access was dit nooit goed ingericht (de laatste ~1.400 orders sloegen status 1 en 2 over). Besluiten gebruiker:
- Knop **Volgende ▸** (bij In bestelling: **Besteld ▸**); regels in `Services/BestelFlow.cs` (zonder database).
  Aanmaken → werkorder verplicht (+ InkoopToegestaan), minstens één regel, toeslagen zoals Access (minimum orderbedrag
  → regel "Ordercosts", next-day → "Next-day delivery fee", `Toeslag` = 1). Totaal ≤ `Werknemers.GoedkeurTarief` van
  wie drukt, of Directie → In bestelling; anders Goedkeuring PM. **PM = `Projects.ProjectResponsible`** van het project
  van de werkorder; is de besteller zelf de PM, dan meteen Goedkeuring Directie.
- Goedkeuring PM: alleen de PM of Directie; ≤ GoedkeurTarief van de PM (of PM is Directie) → In bestelling, anders
  Goedkeuring Directie (**grens = die van de PM**, niet de vaste € 2.500 uit Access). Goedkeuring Directie: alleen Directie.
- In bestelling → Besteld: elke regel prijs of schatting (Access), vraag e-mail bij gereed (`Notify_when_delivered`),
  `DirectGeleverdBijBestellen` → meteen Gereed. Wacht op levering → nog in Access.
- Datum/wie per stap via de bestaande `ZetStatusAsync` (`BestelVelden.StatusVelden`).
- Statusknoppen (vrij springen) alleen voor Directie; anders alleen weergave.
- **Afkeuren…** (wie in die stap mag goedkeuren, reden verplicht): terug naar Aanmaken, regel "[datum] Afgekeurd door X:
  reden" onder `Orders.Remark` (interne opmerking), melding soort "afgekeurd" (❌ in de bel) aan de besteller.
- **Bel** (`Components/Layout/MeldingBel.razor`, bovenbalk, alleen zichtbaar als er iets is: rood ! + aantal): goed te
  keuren (`TeKeurenAsync`: status 1 met jou als PM, status 2 als je Directie bent; volgt uit de status) en meldingen
  (`IMeldingRepository`, tabel `Meldingen` uit `Meldingen_MariaDB.sql`). Klik = `bestellingen?order=ID` in een nieuw
  tabblad. Na goedkeuring tot In bestelling krijgt de besteller een melding "goedgekeurd, je kunt bestellen".
  Ververst elke minuut en meteen via `MeldingSignaal` (singleton) als iemand op de server iets doorzet.
- Lokaal alleen getest zonder gebruiker (openen via ?order=, knop uit met reden, statusknoppen uit); de stappen zelf
  schrijven in de echte database en zijn **niet getest**.

## Bestellingen: standaardwaarden van de leverancier en afleveradres (16-10-2026)

Zoals `FillInDefaults_Order` in Access (na aanmaken en na wisselen van leverancier, `OrderSupplier_AfterUpdate`):
`IBestelRepository.LeverancierStandaardAsync` + `VulStandaardAsync` zetten contactpersoon (`Relations.OrderContact-id`),
taal (`OrderLanguage`), standaard leverdatum (`OrderTime`, alleen als `Orders.DefaultLeverDatum` leeg is; volgende werkdag
ma-vr + levertijd - 1, Access telde vanaf het weekend een dag verder) en het afleveradres. Heeft de leverancier een
`DefaultVerzendAdres`, dan de vraag "Afleveren bij … ?": in de popup Nieuwe bestelling als Ja/Nee (Aanmaken wacht erop),
bij wisselen van leverancier via `Bevestig`. Ja = `AfleverKlant-id`/`AfleverTAV-id` van de leverancier, Nee = leeg.
"Ter attentie van" toont nu de contactpersonen van het afleveradres (lijst `AfleverContact`; was die van de leverancier).
**CC bij bestelling** (`CCbijBestelling`, `CCmailadressBijBestelling`) vroeg Access pas bij het versturen van de mail; die
staat nog niet in de app, dus nu alleen zichtbaar in het blok Leverancier. De vraag hoort bij het toekomstige versturen.
Lokaal getest t/m de vraag (Schur B.V. -> Smits Service); het wegschrijven niet (echte database).

## Bestellingen: werkbalk boven de regels, Plakken uit klembord (16-10-2026)

**Plek van knoppen die regels toevoegen** (besluit gebruiker): de werkbalk boven de regels: "+ Regel" en het menu
"Importeren ▾". In dat menu staan "📋 Plakken uit klembord" (sinds 19-10-2026, was een eigen knop), "📷 Uit screenshot of
PDF" en de andere imports uit het Access-formulier "Order sub form" (SMC- en Solar-website, Materialise-prijzen; nu grijs
met "volgt").

**Plakken uit klembord** (`Components/PlakVenster.razor`) vervangt `PasteFromClipboard_Click` (Access, "Order sub form"):
- Klembord via `wwwroot/klembord.js` (`navigator.clipboard.readText`); mag dat niet, dan Ctrl+V in het vak.
- Lezen in `Services/KlembordBestelling.cs`: tabs, kopregel; kopwoorden uit Access' lokale tabel `General_Settings`
  (Keyword…) staan nu als constanten daar. Inventor-add-in: `BomExport` (Aantal, Artikelnummer, Omschrijving). Regels
  zonder aantal doen niet mee.
- Artikel zoeken `ZoekPlakArtikelAsync` in de volgorde van Access (ArtikelSupplier bij deze leverancier, ArtikelCodeMDE,
  ArtikelID, `@ArtikelSearchlist.Filtered`, ArtikelCodeFactory); gevonden = fabriekscode en OmschrijvingNL.
- Geen prijs geplakt: schatting zoals `GetSchatting` (`KlembordBestelling.Schatting`). **Anders dan Access**: de schatting
  wordt niet teruggeschreven in `Artikelen.SchattingsKostprijsStuk`, en `needed-by` wordt niet gezet (net als "+ Regel").
- Bijlages zoals `VoegBijlagesToe`: per soort die de leverancier wil (`Relations.ExportPDF/DXF/STP/STL`), alleen voor
  artikelen in `@ArtikelListInventor`, nieuwste versie per bestandsnaam (zoals `sp_FileStore_Order_Bijlages`); precies
  één = gekopieerd als StoreType 3, geen of meer = opmerking (zelf toevoegen via 📎).
- **Nieuw t.o.v. Access**: eerst een voorbeeld (gevonden artikel, prijs/schatting, bijlages ✓/ontbreekt/meerdere, vinkje
  per regel), dan Toevoegen; daarna een verslag.
- Lokaal getest t/m het voorbeeld (PO-18352, Jansen Tholen: PDF/DXF/STP); **Toevoegen niet getest** (schrijft in de
  echte database), en het automatisch lezen van het klembord op de server (https, browser vraagt toestemming) ook niet.

## Migratie Uitgaande facturen (okt 2026)
- Analyse van het Access-formulier: `..\FacturenUitgaand_analyse.md`. In fasen (besluit gebruiker):
  1 overzicht (klaar: `Pages/UitgaandeFacturen.razor`, `IVerkoopFactuurRepository`), 2 nieuw/aanpassen met
  regels en artikelen (klaar: `Components/VerkoopFactuurVenster.razor`; regels worden bijgewerkt, niet
  gewist en opnieuw geschreven, zodat de urenselectie blijft staan), 3 uren importeren + tarieven (klaar:
  `Components/UrenImportVenster.razor`, `IUrenImportRepository`; de rekenregels van Access' `calctarief`
  staan zonder database in `Services/Tariefregels.cs`), 4 PDF (klaar: `Services/FactuurPdfService.cs` maakt
  HTML en laat Edge headless er een PDF van maken; nieuwe opmaak op verzoek van de gebruiker; logo's en extra
  regels per BV in `Templates/Factuur/`; `Components/FactuurVersturenVenster.razor` mailt via de eigen Gmail,
  zet de PDF's in de filestore (StoreType 5) en eventueel `Facturen:Map`), 5 betalingscheck (klaar:
  `Components/BetalingscheckVenster.razor`; regels zonder database in `Services/Betalingsmatcher.cs`: per
  ontvangst op de BV-rekening alle los genoemde factuurnummers optellen, voorstellen, gebruiker vinkt aan;
  terugtoets 2025-2026: 414 van 510 betaalde facturen teruggevonden). Alle fasen klaar.
- **Ja/nee-velden in Invoices staan als -1 én 1** (Access schreef -1). Altijd `<> 0` testen; de view
  `@UitgaandeFacturen` rekent overdue met `<> 1` en is daardoor fout voor ~700 betaalde facturen.
  Schrijven: `SentToClient` en de `Uren`-vlaggen als **-1** (Access filtert op "-1*"), `Betaald` als 1
  (Access' filter Betaald zoekt "1*").
- Btw zoals Access: `Floor(ex * 21) / 100` (afkappen, niet afronden). Access rekende in double en kwam
  bij 3 van 556 facturen een cent lager uit; de app rekent in decimal.
- `InvoiceHours` hangt met ON DELETE CASCADE aan `Invoices`; de crossref met banktransacties niet.

## Migratie Vaste lasten (okt 2026)
- Analyse: `..\VasteLasten_analyse.md`. Klaar: `Pages/VasteLasten.razor` (lijst + jaaroverzicht + komende 60 dagen),
  `Components/VasteLastVenster.razor`, `IVasteLastenRepository`, rekenregels in `Models/VasteLast.cs`.
- **`Incassodatum` blijft zoals Access** (besluit gebruiker): wekelijks = weekdag 1-7, maandelijks = dag van de
  maand, kwartaal en jaarlijks = dag van het jaar in het lopende jaar (kwartaal in jan-mrt). `VasteLast.Dag/Maand`
  is de leesbare vorm; `NaarIncassodatum`/`VanIncassodatum` vertalen. Rondreis getoetst: 31 van 33 gelijk, de 2
  andere zijn kwartaallasten die Access in december opsloeg (zelfde betekenis).
- Access' Liquiditeitsoverzicht leest kwartaal/jaarlijks als dag van de maand en mist zo die lasten (nog niet gemigreerd).

## Migratie Liquiditeitsoverzicht (okt 2026)
- Klaar: `Pages/Liquiditeit.razor` (alleen lezen), `ILiquiditeitRepository`, rekenregels zonder database in
  `Services/Liquiditeitsrekenaar.cs`. Vervangt `Report_LiquiditeitsOverzicht` en de twee formulieren 'Te laat'.
- BV's te kiezen (standaard `Relations.MDEBV`), periode 30/60/90 dagen (besluiten gebruiker). Saldo, facturen en
  btw tellen consequent voor dezelfde BV's; vaste lasten altijd.
- Start = laatste dag met banktransacties; al vervallen op die dag = 'te laat' (verkoop alleen als verstuurd),
  anders op de vervaldatum in de prognose. Twee lijnen zoals Access: saldo − te laat aan leveranciers en
  saldo + te laat van klanten.
- Access-fouten die hier niet meer zitten: saldo via DLast zonder volgorde; facturen van alle BV's tegen het saldo
  van alleen MDE-BV's; vaste lasten kwartaal/jaar op de verkeerde dag; dubbeltelling tussen bankdatum en vandaag.

## Migratie Betalingen klaarzetten (okt 2026)
- `Pages/BetalingenKlaarzetten.razor`, `IBetalingRepository`, `Models/Betaling.cs`, SEPA in `Services/Sepa.cs`
  (pain.001.001.03, per BV een bestand, uitvoerdatum vandaag, zoals `ExportBetalingen_Click`). De hulptabel
  BankExport uit Access is niet nodig: de regels staan in het geheugen van het scherm.
- Bron = Access-query BetalingenKlaarzetten: goedgekeurd, niet betaald, niet SEPAverzonden, BV met MDEBV,
  leverancier zonder AutomatischIncasso. Filter "vervalt na" (NogDagen > waarde, standaard -3) en BV.
- Er wordt betaald wat geselecteerd **en zichtbaar** is (Access nam ook selecties buiten het BV-filter mee).
- IBAN/BIC wijzigen gaat ook naar `Relations` (met bevestiging); tenaamstelling en omschrijving alleen voor het
  bestand. Markeren: `SEPAverzonden = -1`, `SEPADoor`, `SEPAop`, regel in Opmerking (alleen als die past, varchar 200).
- Bestand in `Betalingen:SepaMap` (standaard `\\172.16.1.4\data\Administration\Finance\SEPA`) en als download.
- Later via de betaal-API van de Rabobank i.p.v. het bestand (wens gebruiker): alleen de stap in `MaakSepa` vervangen.
- Niet overgenomen: de verborgen knop "meerdere betalingsdata" (vaste MDE-rekening in de code).

## Migratie Bankrekeningen (okt 2026)
- `Pages/Bankrekeningen.razor` (Financiën ▸ Bank), beheermethodes in `IBankRepository` (`BeheerLijstAsync` enz.).
  Access had alleen nummer, omschrijving en BV (BV = `Relations.Werkmaatschappij`); nieuw is wie de rekening
  mag zien (`BankRekeningRechten`, bepaalt Banktransacties), alleen te wijzigen met het recht Directie.
- Rekeningnummer = IBAN zonder spaties (zo koppelen de transacties, op tekst). Niet te wijzigen zolang er
  transacties aan hangen; wissen alleen zonder transacties, `Passen` en `BankReferentieNegeren` (FK's).
- Wie een rekening toevoegt krijgt er meteen zelf recht op.

## Migratie PO's van klanten (okt 2026)
- Access-formulier POInkomend (+ toevoegen/aanpassen, subformulier PO_Payments, rapport Payments_due) →
  `Pages/PoKlanten.razor` (/financien/po-klant, tabbladen PO's en Facturatie-agenda), `POKlantVenster.razor`,
  `IPOKlantRepository`, `Models/POKlant.cs`. Tabellen `PO_Inkomend` en `Payments`; documenten in FileStore
  met StoreType 20. De pagina heet niet `POKlant.razor`: dat botst met het model.
- Termijnen worden bijgewerkt in plaats van (zoals Access) gewist en opnieuw neergezet, zodat ids en
  gekoppelde facturen blijven; een termijn met factuur kan niet weg, een PO met zo'n termijn ook niet.
- "＋ Factuur" bij een termijn opent `VerkoopFactuurVenster` met een `FactuurVoorstel` (BV, klant, PO, regel);
  na opslaan (`Bewaard`) worden InvoiceID, Invoiced = -1, datum en Checked_invoiced gezet. Access maakte de
  factuur direct en koppelde hem nooit. "Koppel…" kiest een bestaande factuur van de klant.
- Niet overgenomen: PO_Offertes (tabel leeg, Access sloeg ze nooit op), scannen (WIA), plakken via de
  Verkenner, de knop "Vul summaryWorkorders". "Niet volledig gefactureerd" had in Access geen code; nu een vinkje.

## Bezoekverslagen (nieuw, okt 2026)
- Geen Access-voorganger. Schema in `Bezoekverslagen_MariaDB.sql` (tabellen Bezoekverslagen, BezoekDeelnemers,
  BezoekActies; FileStoreType 330). Zonder die tabellen meldt het scherm dat het script nog moet draaien.
- `Pages/Bezoekverslagen.razor` (/relaties/bezoekverslagen, /{id} opent een verslag, ?relatie=123 een nieuw
  verslag), `BezoekVenster.razor`, `IBezoekRepository`, `Models/Bezoek.cs`. Tabblad "Bezoeken" in Relaties
  (datum + titel, linkt naar het verslag).
- Deelnemer = collega (WerknemerID), contactpersoon van de relatie (ContactID) of een losse naam.
- Vertrouwelijk: alleen maker, collega's die erbij waren en Directie — gefilterd in elke query (`Zichtbaar`).
  Bewerken: maker, deelnemende collega's, Directie; wissen: maker en Directie.
- Foto's: "Foto maken" (`capture="environment"`, opent op een telefoon de camera) en bestanden; bij een nieuw
  verslag pas geüpload na de eerste keer opslaan.

## Migratie Offertes (okt 2026, in fasen)
- Analyse: `..\Offertes_analyse.md` (datamodel, werkstromen, berekeningen, PDF-opbouw, 17 vragen).
  Besluiten gebruiker (13-10-2026): 4 fasen; mailen als Gmail-concept (zoals facturen); de dubbele klantkorting
  bij methode Standaard/Vast herstellen (korting één keer); iedereen met offerterecht (= werknemerskoppeling)
  mag Afgewezen/Opdracht zetten, ook na aanbieden; terug naar Aanmaken alleen Directie.
- **Fase 1 klaar**: `Pages/OffertesPagina.razor` (/projecten/offertes, /{id}), `IOfferteRepository`,
  `Models/Offerte.cs`. Per project of zoeken; tabs Algemeen, Voorwaarden (keuzelijsten per taal met
  "toevoegen aan lijst", disclaimer kiezen/bewaren), Aantekeningen; nieuw (nummer = max+1 in het project, ook
  gewiste, met `FOR UPDATE`; tarieven van de BV overnemen), zacht wissen, status. Laatst gekozen offerte in
  localStorage (`mde-laatste-offerte`). Totalen in `IOfferteRepository` (consts `Totaal*`) = zelfde als
  `@OfferteTotaal` (0 verschillen getoetst); de kortingsfix hoort daar in fase 2.
- **Fase 2 klaar**: `Components/OfferteRegelsTab.razor` (tabs Uren, Materiaal, Overigen, Categorieën, Tarieven;
  elke wijziging meteen opgeslagen zoals Access, daarna `TotalenVerversen` in de pagina) en
  `Services/IOfferteRepository.Regels.cs` (partial). Elke schrijfactie checkt in dezelfde query `OpenOfferte`
  (status ≤ 1). Categorie wissen = regels zonder categorie; tarief wissen geweigerd als het gebruikt wordt.
  "Standaardtarieven van de BV ophalen": ontbrekende toevoegen + per afwijkend tarief vragen (Access-gedrag).
  Kopiëren in één transactie met id-koppeling (Access koppelde op naam/afkorting en raakte de hele tabel);
  taal gaat altijd mee. Materiaal: prijs en korting % direct te bewerken, regeltotaal = aantal × prijs × (1 − korting),
  dus de korting telt één keer. De ~20 oude Access-regels (methode Standaard/Vast mét korting) waar de korting al
  in de prijs zit, blijven zoals ze zijn (besluit: bestaande offertes ongemoeid). Fase 3 moet bij Standaard/Vast
  de prijs **vóór** klantkorting opslaan, zodat de korting ook daar één keer telt.
- **Fase 3 klaar**: `OfferteArtikelZoeker.razor` (via `IArtikelRepository.ZoekAsync` + `ArtikelPrijzenAsync`;
  kiezen = Access-gedrag: code, omschrijving, verkoopprijs project, methode Standaard, korting 0),
  `OffertePrijsVenster.razor` + `IOfferteRepository.Prijzen.cs` (duurste leverancier zoals `Artikel_MaxInkoopprijs`,
  inkoop = bruto × (1 − lev.korting); opslag uit `General_settings.ToeslagInkoopVerkoop`, niet bewaard bij een
  artikel; zonder artikel kostprijs/opslag in `InkoopprijsNonMDE`/`ToeslagNonMDE`). **Prijs = vóór klantkorting**
  (`OffertePrijs.Prijs`); de korting staat alleen in het regeltotaal. Winst tonen (intern, Access-rapport
  OfferteMaterialenWinst): kostprijs = VastgesteldeKostprijs of anders InkoopprijsNonMDE.
  Getoetst: artikel 42524 → inkoop 255,15, standaard 364,86 = verkoopprijs project.
- **Fase 4 klaar**: `OffertePdfService` (HTML zoals de factuur-PDF, Edge via `FactuurPdfService.NaarPdfAsync`,
  logo/kleur/regels per BV via `FactuurPdfService.VulAfzender`, nu public), `IOfferteRepository.Pdf.cs`,
  `OfferteVersturenVenster.razor` (knop "📄 PDF en versturen…"). Klaarzetten = PDF naar
  `Offertes:Map\<jaar>\<BV>\<OfferteNr>.PDF` + FileStore (StoreType 8, MasterID = ProjectID) + **Gmail-concept**
  (`MailService.MaakConceptAsync`, POST /drafts; valt onder gmail.modify); daarna knop "Verstuurd" =
  GemaildOp/Door + status Aangeboden (Afgewezen/Opdracht blijven staan). Paden uit Access met `Z:` worden
  `Offertes:Netwerkschijf` (`\\172.16.1.4\data`); handtekening = `Offertes:Handtekeningen\<werknemer-id>.PNG`.
  Ondertekenaar = wie verstuurt (zoals Access), BV-naam nu die van het project (Access: vast "MDE Automation B.V.").
  Vertaald (Access niet): t.a.v./Attn., Totaal materiaal, akkoordtekst. Datum = vandaag (zoals Access).
  Getoetst met een testprogramma (scratchpad `offertetoets`): PDF's van 984 en 991 bekeken. Niet getest: het
  Gmail-concept, de filestore en "Verstuurd" (vragen het token en schrijfrechten van de gebruiker). Fase 4: PDF (HTML zoals `FactuurPdfService`), FileStore StoreType 8
  (MasterID = ProjectID), Gmail-concept, status Aangeboden.

## Migratie Artikel-samenstellingen (okt 2026)
- Access ArtikelSamenstellingen (+ _Open, _Nieuw, _Details) → `Pages/ArtikelSamenstellingen.razor`
  (/artikelen/samenstellingen, /{id}), `ISamenstellingRepository` (lijst, hele boom in één keer uit
  ArtikelSamenstelling, ~1.250 regels; kringlopen en >12 niveaus afgebroken; kostprijs samenstelling = som onderdelen).
  Wijzigen via `IArtikelRepository` (zelfde als Artikelen ▸ Samenstelling); rechten Inkoop/Beheer/Directie.
- Boomsleutel = pad van regel-ids ("w/12/34"): een onderdeel kan meerdere keren in dezelfde boom staan.
- Wijzigen/toevoegen/wissen in een onderliggende samenstelling die elders ook gebruikt wordt: eerst waarschuwen (Access).
- Niet overgenomen: Inventor-import (InventorImportAssy); geïmporteerde regels staan wel vet.
- Opgelost in `SamenstellingTak`: inspringen gebruikte de Nederlandse komma in CSS ("1,3rem") en werkte dus niet;
  nu `FormattableString.Invariant`. Let op dit patroon bij andere `style=` met getallen.

## Voorraad: lokaties en dragers (okt 2026)

Nieuwe module **Voorraad** in het menu (groepen Lokaties, Voorraad, Rapporten; de oude groep Voorraad onder
Artikelen is daarheen verhuisd). Recht om te wijzigen: `Voorraadbeheer` of `Directie`; bekijken mag iedereen.

- **Lokatiestructuur** (`/voorraad/lokatiestructuur`, Access `LocationChilds` "Lokatiestruktuur"): lokatietypes
  (`LocationTypes`) met hun vinkjes, en welke types eronder mogen (`LocationChilds`). Rechts de hele structuur als
  boom. Regels in `LocationChilds` die naar een niet-bestaand type wijzen (in okt 2026: 7 stuks, types 6/10/11/14)
  staan apart om op te ruimen. Wissen van een type kan alleen zonder lokaties en zonder regels (Access keek alleen
  naar de regels). Systeemtypes (Wees) zijn niet te wijzigen.
- **Dragers** (`/voorraad/dragers`, `/{id}`, Access `LocationDragers` + subformulieren, `locationdragerAanmaken`,
  `LocationDragersPrintRange`, `LocationDragerVerplaatsen`): lijst met filters type/inhoud/met voorraad, details
  (omschrijving, opmerking, soort inhoud, werkorder via `WerkorderKiezer`), globale inhoud (`LocationContents`) en
  de artikelen die er volgens `VoorraadMutaties` op liggen. Aanmaken = N stuks met doorlopend `Index` per type, in
  Wees (`LocationID` 10), zoals Access. Labels: Dymo klein label met barcode `@L` + 8 cijfers en de naam
  (`LabelService.LokatieLabelXml`), zet `PrintedAt`. Scannen: de handscanner typt de code in het zoekvak + Enter.
- **Verplaatsen** (`Components/LokatieKiezer.razor`): boom met alleen toegestane voorraadplekken (volgens
  `LocationChilds`, plus Wees); vinkje "ook niet-toegestane" voor als de structuur onvolledig is. Kan niet onder
  zichzelf.
- **Fouten gevonden in Access**: `LocationDragerVerplaatsenBevestiging` sloot alleen het venster en wijzigde
  `ParentLocationID` niet, daarom stonden alle 261 dragers in Wees. `AantalItems` telde ook artikelen die op 0
  staan; hier alleen voorraad <> 0.
- Namen en paden: `LokatieBoom` (Models/Lokatie.cs) leest alle lokaties in het geheugen (ca. 400) en maakt
  `Naam` (= view `LocationNames`) en `Pad` (= VBA `LocationPath`). Access' `MoveWezen` (wezen naar Wees) draait
  hier niet; in okt 2026 waren er geen wezen.
- **Artikelvoorraad** (`/voorraad/artikelen`, `/{id}`, `?lokatie=`; Access `ArtikelInVoorraad` met
  `ArtikelInboeken`, `ArtikelUitboeken`, `ArtikelVerplaatsen`): `Services/IVoorraadRepository.cs`. Stand = som van
  `VoorraadMutaties` per artikel/plek/status, **alle** mutaties met een `LocationID`. Boeken als event 2 (HAND), in
  een transactie met `FOR UPDATE` en controle dat er genoeg ligt. Uitboeken met werkorder schrijft ook
  `ProjectMaterials` (import 1, vrijgegeven) en `ProjectMaterialsDelivered`, zoals Access. Verplaatsen = twee
  mutaties; "bij een persoon" maakt zo nodig de persoonlijke lokatie onder Werknemer (1065, type 20; Access
  `AddLocationPerson`). Corrigeren (telling) alleen met `Voorraadbeheer`/`Directie`; boeken mag iedereen (Access had
  geen rechten). Scannen in het zoekvak: `MDE123`/artikelnr/EAN opent het artikel, `@L…` filtert op die plek.
  `LokatieKiezer` met `VoorArtikel=true`: elke voorraadlokatie behalve Wees en systeemlokaties.
- **Fouten in Access (artikelvoorraad)**: de stand sloot mutaties met een werkorder uit, dus uitgeboekt naar een
  project bleef op voorraad staan (okt 2026: 232 posities, 1001 stuks tegen 466 echt; met alle mutaties is geen
  positie negatief). "Artikel aanpassen" opende het niet-bestaande formulier `VoorraadAanpassen`. Uitboeken zonder
  werkorder maakte projectmateriaal zonder werkorder (362 regels in `ProjectMaterials` met `WerkorderID` NULL).
  Plekken werden op naam (`LocationNames.Lokatie`) teruggezocht.
- Nog niet overgezet: `LocationTreeview` (lokaties zelf beheren, `LocationToevoegen`), `Lokaties`,
  `VoorraadPerMagazijn` (deels: plekfilter en Dragers), `VoorraadOrders`, `MateriaalInboeken`, `Report_VoorraadStand`.
- Niet getest: alle schrijfacties (aanmaken, wijzigen, verplaatsen, inhoud, types, in-/uitboeken, corrigeren) en het printen.

## Sonos: volume (okt 2026)

Gebouwbeheer ▸ Sonos (`/gebouw/sonos`, pagina `SonosVolume.razor`: de naam `Sonos` botst met de geïnjecteerde
service). Alleen volume en dempen, op verzoek van de gebruiker. `Services/SonosService.cs` praat lokaal UPnP/SOAP
met poort 1400 (geen Sonos-account of cloud): `ZoneGroupTopology#GetZoneGroupState` voor de speakers (alleen
`ZoneGroupMember` zonder `Invisible="1"`; surround/sub zijn `Satellite`), `RenderingControl` voor Get/SetVolume en
Get/SetMute. Config `Sonos:Adressen` (een adres is genoeg; leeg = scherm toont uitleg). De pagina ververst elke
10 s, niet tijdens het schuiven. Zoeken via SSDP vanaf de ontwikkel-pc gaf geen antwoord; daarom vaste adressen.
Speakers (13-10-2026, 172.16.10.x): Kantoor Elektro .5, Edwin&Peter .7 (+ .1 onzichtbaar, stereopaar), WP
Mechanisch .6, Mancave .43 (+ .44), Fablab .215. Volume zetten getest door de gebruiker.

- **Bediening in `Components/SonosPaneel.razor`** (de pagina gebruikt het ook). Volume gaat al tijdens het schuiven
  naar de speaker (`@oninput`): per speaker loopt hoogstens één opdracht, daarna alleen de nieuwste stand.
- **Wat speelt er** (alleen lezen): per groep bij de hoofdspeaker (`Coordinator` uit de topologie) via AVTransport
  `GetTransportInfo`/`GetPositionInfo` (DIDL: title, creator, album, albumArtURI, streamContent); radio: zender uit
  `GetMediaInfo`, nummer uit `streamContent`; TV (`x-sonos-htastream`) en Line-in (`x-rincon-stream`) apart.
  Hoes via de server: `/sonos/hoes/{uuid}` (alleen bekende speakers, alleen image/*). Ververst elke 5 s.

## Gebouwbeheer als zwevend venster (okt 2026)

Op verzoek: Gebouwbeheer is iets voor even tussendoor, dus het menu opent het als zwevend venster rechts boven
het huidige scherm (`MenuModule.Venster`, `MainLayout`: `_venster`, Esc of ✕ sluit, ↗ opent de losse pagina).
Geldt voor Verlichting, Zonwering, Compressor, Sonos, Alarm en Camera's (die laatste is de pagina zelf, breder
venster). De pagina's onder `/gebouw/...` blijven bestaan (links, zoekpalet). Energie heeft nog geen scherm.

## Activity board (15-10-2026, fase 1)

Naar "Design specificatie MDE Activity Board" (Claude Doc van Freek Moerland, gedeeld artifact) en prototype
`MDE_Activity_Board.html` v0.5 (in de code v0.4; lag in Downloads van de gebruiker). Besluiten gebruiker: in de app
(de spec liet het nog los), database in plaats van browseropslag, stijl van de app (MDE-rood als accent, chip- en
statuskleuren uit het prototype), eigen menu-item (`/activityboard`, project kiezen op het startscherm), in fasen.
- **SQL:** `ActivityBoard_MariaDB.sql` (gebruiker voert uit): `ActivityBoards` (Project -> Board een-op-veel,
  release 1 één per project via `ZorgVoorBoardAsync`), `ActivityBoardProblemen` (Positie = prioriteit),
  `ActivityBoardTypes`, `ActivityBoardActiviteiten` (status als code: backlog/next/bezig/testen/beoordeling/
  opgelost/nietopgelost; prioriteit 1-3; zes tekstkolommen) en koppeltabellen voor problemen, types en personen
  (Werknemers). Namen uniek per board via unieke index (general_ci: hoofdletters tellen niet). Verwijzingen op id.
- **Code:** `Models/ActivityBoard.cs` (statussen, prioriteit, palet, initialen/avatarkleur zoals het prototype),
  `Services/IActivityBoardRepository.cs` (elke wijziging direct, per veld of koppeling; slepen in één transactie),
  `Services/ActivityBoardLive.cs` (singleton-event: andere schermen op hetzelfde board laden opnieuw; niet tijdens
  slepen), `Components/Pages/ActivityBoard.razor`, `wwwroot/activityboard.js` (slepen van kaarten en problemen met
  muis en touch, 300 ms vasthouden; pijltjes op de greep), CSS met prefix `ab-` in app.css.
- **Kernregel:** een kaart staat in de baan van het gekoppelde probleem met de laagste Positie (`Lane`). Slepen
  volgt spec §4 (andere baan vervangt het probleem van de bronbaan; "Geen probleem" wist alle koppelingen;
  ingeklapte baankop = alleen het probleem), daarna wordt het doelvak 0..n genummerd.
- **Nieuw via "+ Activiteit"** komt pas in de database als er een titel is (`_open.Id == 0` tot dan). Verwijderen
  van een activiteit gaat zonder vraag, met "Ongedaan maken" (zet een kopie terug, nieuw id).
- **Lokaal testen zonder database:** `ActivityBoard__Geheugen=true` (GeheugenActivityBoardRepository). Getest
  15-10-2026 in de browser: problemen/types toevoegen, hernoemen, dubbele naam, verwijderen met bevestiging,
  prioriteit met pijltjes, snel aanmaken, detailpaneel, slepen (kolom, baan, volgorde, ingeklapte kop),
  tabellen, filters, zoeken, verwijderen + ongedaan, live in een tweede tabblad.
- **Soorten board** (15-10-2026, op verzoek): een board hoort bij een project, een klant (relatie, `RelatieID`;
  één per klant) of niets (vrij board met alleen een naam). Script `ActivityBoard_Soorten_MariaDB.sql` (ProjectID
  NULL, kolom RelatieID); zonder dat script werkt alles als voorheen en verbergt het scherm klant/vrij/Koppelen
  (`SoortenAanwezigAsync`). Startscherm: filter op soort, "Board openen of maken" met Project / Klant (zoekt via
  `IRelatieRepository.ZoekAsync`, alleen klanten) / Vrij board. Kop: naam als invoerveld (leeg = naam van project
  of klant; een vrij board moet een naam hebben) en **Koppelen…** (zelfde zoekvak): `KoppelBoardAsync` weigert een
  project of klant die al een board heeft; Losmaken bewaart eerst de huidige titel als naam. De gegevens hangen aan
  het board, dus koppelen verplaatst alles mee. `In dienst` in Werknemers is een datum; in dienst = `Uit dienst` = 0.
- **Fase 2 (nog niet):** snel-editor met Wie/Type/Probleem/Prio en toetsenbord (Tab/komma/Backspace), kaartmenu
  (drie bolletjes), ongedaan maken van alle eigen wijzigingen (40 stappen), Tab binnen het detailpaneel houden,
  FLIP-animatie. Open vragen spec §6: wie toewijsbaar is (nu iedereen in dienst; uit dienst blijft zichtbaar).

## Snelbalk bovenin (15-10-2026)

Op verzoek: per gebruiker maximaal 10 tegels (icoon of eigen plaatje + naam) in de bovenbalk van elke pagina
(`Components/Layout/Snelbalk.razor`, in MainLayout links van de gebruikersnaam). ☆ zet het huidige scherm erbij (naam
= paginatitel zonder " — MDE", anders de naam uit het menu via `SnelIconen.MenuNaam`) en opent het bewerkvenster; ★ haalt
het eraf. Rechts klikken of ✎ (bewerkmodus) = naam, adres (binnen de app of http(s)), icoon of eigen plaatje, ◀ ▶,
verwijderen; slepen = volgorde; Alt+1 … Alt+0 (`wwwroot/snelbalk.js`, op `e.code`).
- **Opslag:** `Snelkoppelingen_MariaDB.sql` (gebruiker voert uit), `ISnelkoppelingRepository` (MySql / Geen / Geheugen via
  `Snelbalk__Geheugen=true`). Gebruiker = `w:<Werknemer-id>`, zonder koppeling `e:<e-mail>`. Zonder tabel geen balk.
- **Iconen:** Lucide v0.460.0 (ISC) in `wwwroot/iconen` (sprite + tags + licentie), gecontroleerd: alleen vormen.
  `SnelIconen` leest de sprite één keer; zoeken op naam, Engelse trefwoorden en een lijst Nederlandse woorden.
  Geen icoon gekozen = automatisch het icoon van de module (Menu.cs, `AutoIcoon`).
- **Eigen plaatje:** de browser verkleint naar 64x64 PNG (canvas, vierkant uit het midden); de server accepteert alleen
  een PNG-data-URL tot 100 kB en bewaart de bytes in `Plaatje`.
- **Zwevende vensters** (Gebouwbeheer, MenuModule.Venster): een tegel met zo'n adres opent het venster (EventCallback
  OpenVenster naar MainLayout) in plaats van de pagina; de ster staat in de kop van het venster (MainLayout roept
  Snelbalk.WisselAsync aan; Gewijzigd laat MainLayout opnieuw tekenen). .snel-modal z-index 1000 (boven het venster).
- **Zijbalk:** de blokjes "Snel" (Ureningave, Mail, Agenda) en "Recent" zijn weg (verzoek gebruiker); Ureningave, Mail en
  Agenda staan in het zoekvenster (Ctrl+K). RecentPagesService houdt nog bij, maar wordt niet meer getoond.
- **Nog niet:** standaardset van Beheer, tellingen op tegels.

## Werkplekken: gedeelde pc's zonder Google-account (15-10-2026)

Pc's waar meerdere mensen aan werken (robots, magazijn/inboeken). Besluit gebruiker: **geen Google-account per werkplek**
(licentiekosten) en de keuzes **in de app**, niet in de agent (de agent-instelling `gedeelde_werkplek` uit 1.17 is in
1.18.0 weer weg; de agent doet alleen hardware). Mailen vanaf een werkplek = **een alias per werkplek**.
- **Tabellen** `Werkplekken_MariaDB.sql` (gebruiker voert uit): `Werkplekken` (naam, WerknemerID = een niet-persoon in
  Werknemers voor rechten en historie, Productie, Startpagina, MailAfzender = alias, MailAccount = het account waar de
  alias als "verzenden als" op staat, Actief, KoppelCode + KoppelCodeTot) en `WerkplekApparaten` (gekoppelde pc's).
- **Koppelen**: Beheer ▸ Werkplekken (recht Beheer of Directie) maakt een code van 6 tekens (30 min, eenmalig); op de pc
  `/werkplek/koppelen` (minimal API + `Pagina()`, geen Google nodig, `DisableAntiforgery`) → cookie met claims
  `mde:werkplek`/`mde:apparaat`, 365 dagen glijdend (`WerkplekLogin`). `OnValidatePrincipal` = `WerkplekLogin.ControleerAsync`:
  per minuut per apparaat naar de database; ontkoppeld of werkplek uit = uitgelogd bij het volgende laden (een lopend
  Blazor-circuit merkt het pas bij herladen). `Vergeet` na koppelen/ontkoppelen.
- **In de app** (`HuidigeGebruiker.Werkplek`): label "👥 <werkplek>" in plaats van naam + Uitloggen; Agenda uit (pagina
  en zoekvenster); Mail = alleen een verstuurformulier vanaf de alias (`MailPostbussen.Werkplek`, `MailPostbus.Afzender`:
  serviceaccount werkt namens MailAccount, From = alias; `Voor(ik)` geeft op een werkplek niets, dus ook geen mail lezen bij
  Relaties); Productie alleen met agent én Productie aan (menu + tab Programmeren); startpagina bij openen op "/";
  snelbalk per werkplek (`p:<id>`). **Financiën en Werknemers nooit op een werkplek** (besluit gebruiker, ook niet met
  rechten): `Mag()` geeft daar false voor Financial, MagFacturenGoedkeuren, HRM en **Directie** (Directie opent overal ook
  financiën); modules verborgen (`HuidigeGebruiker.ModulesNietOpWerkplek`), zoekvenster zonder die schermen en zonder
  werknemersnamen, en `MainLayout` toont op `<module>/*` en `module/<module>` een melding in plaats van de pagina
  (`HuidigeGebruiker.DichtOpWerkplek`). Rabobank-koppeling gaat op e-mail, dus al dicht. Andere verstuurplekken (factuur, NDA) gebruiken nog `MailService.Eigen(Ik.Email)`.
- **Wie werkt er** (`Components/Layout/WerkplekMedewerker.razor`, besluit gebruiker: naam + pincode, 30 min): "🙋 Wie ben
  jij?" → naam (personen in dienst) → pincode van 4 cijfers via cijferblok of toetsenbord. Aangemeld = op dat apparaat
  `WerkplekApparaten.MedewerkerID`; `HuidigeGebruiker.WerknemerId` wordt dan die persoon (historie, "Mijn activiteiten"),
  `Mag()` blijft de rechten van de werkplek. `wwwroot/werkplek.js` meldt elke minuut of er geklikt/getypt is
  (`TikAsync`: LaatsteActiviteit bijwerken en ander tabblad volgen); na `Pincode.Duur` = 30 min zonder activiteit is er
  weer niemand. Pincode: tabel `WerknemerPincodes`, PBKDF2-SHA256 met zout (`Pincode`), 5 fout = 5 min wachten (in het
  geheugen van de server). Instellen: Werknemers ▸ Mijn pincode (`/mijn-pincode`, niet op een werkplek; 0000/1234 e.d.
  geweigerd); wissen: Beheer ▸ Werkplekken. Geheugenversie: Kees Blok (5) heeft testpincode 2580.
- **Werkplek gebruiker** (vinkje bij Werknemers ▸ Status, kolom `Werknemers.Werkplekgebruiker` uit het script): alleen
  die personen staan bij "Wie ben jij?" en alleen zij kunnen zich aanmelden (`MeldAanAsync` controleert het ook). Zonder
  de kolom: alle personen in dienst (`PersonenAsync` kijkt een keer of de kolom er is).
- **Op slot** (besluit gebruiker): zolang niemand aangemeld is (ook na Ik stop en na 30 min) ligt er een dicht scherm
  over de app met dezelfde naam/pincodekeuze, zonder Annuleren. Tweede `<WerkplekMedewerker Slot="true" />` los van de
  bovenbalk, omdat die in volledig-schermmodus verborgen is; de app eronder blijft staan, dus na aanmelden ga je verder
  waar je was. Verlopen van de 30 min geeft geen eigen gebeurtenis: `TikAsync` meldt `MedewerkerGewijzigd` als het
  resultaat anders is dan de vorige keer. Geen slot zolang geen enkele werkplekgebruiker een pincode heeft.
  **Valkuil (gevonden bij de eerste test op de server):** `HuidigeGebruiker.ZorgGeladenAsync` zette eerst `_geladen` en
  laadde daarna; een tweede onderdeel dat tegelijk startte kreeg dan een lege gebruiker (geen werkplek → geen slot).
  Nu wacht iedereen op dezelfde `Task`. Lokaal met de geheugenversie zie je zoiets niet (die antwoordt meteen).
- Lokaal testen: `Werkplekken__Geheugen=true`. Getest: aanmaken, code, koppelen (ook met kleine letters/streepje),
  foute code, startpagina, label, geen uitlog/Beheer/Productie, Agenda dicht, mailformulier (met een nep-serviceaccount,
  **niets verstuurd**), uitgelogd na verdwijnen van het apparaat; wie-werkt-er: foute/goede pincode, geen pincode, 5x fout = geblokkeerd, Ik stop,
  blijft na herladen. Niet getest: echt versturen via een alias, en het verlopen na 30 minuten (niet afgewacht).

## Historie van een exemplaar opschonen (15-10-2026)

Op verzoek (testen van de software gaf veel niet-relevante rode regels): met het recht **Directie** staat op
Producten ▸ exemplaar ▸ Historie een vinkjeskolom; het vinkje in de kop kiest alle getoonde regels (dus samen met
het filter "alleen mislukt"). Knop "🗑 n regel(s) wissen…" vraagt bevestiging (geen terugval op 'ja' als de vraag
niet getoond kan worden) en wist **definitief** (`IProductRepository.VerwijderHistorieAsync`, DELETE op
`ProductHistorie`; er is geen isDeleted-kolom). Alleen aangevinkte regels die nu zichtbaar zijn tellen mee.
Met "incl. onderdelen" kunnen ook regels van een onderdeel gewist worden; de vraag noemt dat. Er komt geen
regel "gewist" in de historie zelf (dat zou het opschonen tenietdoen).

## Open punten (okt 2026)
- Ingenia: HW-overstroom na firmware 2.11 (geïsoleerde test op de rig), 33→32-bit BiSS-frame.
- Mosrac USB-kastje (VID 6B6B): eigen protocol onbekend; `taken/encoder_usb.py` is niet in gebruik.
- Idee: snelheidslus-staprespons en J-schatting als tuninghulp (nog niet bouwen zonder akkoord).

## Rabobank-koppeling (bankafschriften elk uur)

- Business Account Insight (Direct Connector), geen PSD2. `Services/RabobankKoppeling.cs`: OAuth2
  (scope `bai.accountinformation.read`), mTLS-clientcertificaat en ondertekende verzoeken (Date,
  Digest sha-512, X-Request-ID, Signature rsa-sha512 met keyId = serienummer decimaal,
  Signature-Certificate). Tokens versleuteld in `C:\ProgramData\MDE\rabobank-<omgeving>.dat`;
  refresh-token is eenmalig, dus elk nieuw token wordt meteen bewaard.
- `Services/RabobankOphaler.cs` (BackgroundService, singleton): elk `IntervalMinuten` de laatste
  `DagenTerug` dagen. **Sandbox schrijft nooit** en raakt de echte tabel niet; Productie pas met
  `Wegschrijven: true` (daarvoor "alleen kijken": telt wat nieuw zou zijn).
- Ontdubbelen (`IBankRepository.VoegApiRegelsToeAsync`): volgnummer op rekening, óf rekening + datum +
  bedrag + saldo — vangt CSV-regels als `entryReference` anders nummert dan Volgnr.
- Koppelen: `/rabobank/koppel` en redirect-URI `/rabobank/terug` (Financial/Directie). Status en
  "Nu ophalen" bovenaan Banktransacties. In de sandbox staat de eerste pagina ruwe JSON in
  `C:\ProgramData\MDE\rabobank-sandbox-voorbeeld.json` om de veldnamen na te lopen.
- **Sandbox werkt (13-10-2026)**: koppelen, rekeningen, saldi en transacties. `dateFrom`/`dateTo` moeten
  een UTC-tijdstip zijn (`2026-02-22T06:00:00.000001Z`), een kale datum geeft 400. Na het terugkomen van
  de Rabobank toonde Banktransacties eenmalig "geen rechten" (F5 hielp).
- **Nog niet nagelopen**: veldnamen van transacties (o.a. `balanceAfterBooking`,
  `raboTransactionTypeName`, of bedragen met teken komen), paginering (`_links.next`), en het
  token-endpoint met mTLS.
