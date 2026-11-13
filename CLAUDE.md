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
- **Links nooit met `/` laten beginnen**: de app draait in productie onder `/mde/` (base href). `href="/relaties?id=1"`
  of `Nav.NavigateTo("/financien/...")` gaat naar de hoofdmap van de server (niemandsland). Altijd relatief:
  `href="relaties?id=1"`, `Nav.NavigateTo("financien/...")` (melding gebruiker 10-11-2026; 10 plekken hersteld).
- JS-interop en SignalR: lange taken niet synchroon; `MaximumReceiveMessageSize` is 8 MB; interop met
  CancellationToken.

## Werkplek-agent (MdeAgent/)
- Python 3.12, PyInstaller + Inno Setup. Bouwen: `MdeAgent\bouw.bat` (kopieert bron naar `D:\MdeAgent`,
  venv, controle, `build.ps1`, installer in `uitvoer\`). Installeren op een werkplek-pc: `installeer.bat`
  (token = `Productie:AgentToken` uit appsettings; per pc gelijk). Versie in `mde_agent/__init__.py` ophogen bij
  elke wijziging; de app controleert `/health`.
- **Voor iedereen op Z:** (20-10-2026) `MdeAgent\publiceer-z.bat` zet installeer.bat, LEESMIJ.txt en de nieuwste
  installer in `\\172.16.1.4\data\software\MDE Apps\MDE Agent` (oudere installers daar naar `uitvoer\oud`, niet
  gewist); waarschuwt als de installer ouder is dan `VERSIE` in de bron (eerst bouw.bat). Test met `MDE_DOEL=<map>`.
  `installeer.bat` vraagt op een nieuwe pc (nog geen `%ProgramData%\MDE\Agent\agent.json`) om het token (verborgen
  invoer), `installeer.bat /token` vraagt het opnieuw; het token staat bewust niet op de share en wordt aan het eind
  maar half getoond. Let op: zonder token maakt de agent zelf een willekeurig token, dat de app niet kent.
  **Sinds 10-11-2026 vraagt installeer.bat altijd** (melding collega: geen vraag, want agent.json bestond al met zo'n
  zelfverzonnen token): bij bijwerken toont hij de eerste 4 tekens en houdt Enter het huidige token; op een nieuwe pc
  (`NIEUW`, geen agent.json vóór de installatie) mag leeg niet "houden" betekenen. Droog getest met een nepagent
  (scratchpad `battest`). Uitrollen = `publiceer-z.bat` (zet installeer.bat en LEESMIJ op de share; de download in
  Tools ▸ MDE Agent pakt hem dan vanzelf). De losse installer (.exe, knop in de versiemelding) vraagt nog steeds niet
  als er al een token staat.
- **Installeren vanuit de app** (10-11-2026, wens gebruiker): Tools ▸ MDE Agent (`/tools/mde-agent`,
  `Components/Pages/AgentInstalleren.razor`; onder Tools omdat Productie pas verschijnt als er al een agent draait).
  Geen apart recht (net als Test PCB). Bovenaan de stand van deze pc: versie (bijgewerkt/verouderd), token (`/adapters`
  met token: 401 = `installeer.bat /token`; `WerkplekAgent.TokenKloptAsync`), Npcap (reden van `drives.uitlezen` in
  `/health`, `WerkplekAgent.Taken`) en andere taken die niet kunnen. Download `/tools/mde-agent/download`
  (`AgentUitgave.ZipAsync`): `MdeAgent-<versie>\installeer.bat`, LEESMIJ.txt, mde-root.crt en `uitvoer\<installer>`
  (zelfde opbouw als de share, installer ongecomprimeerd), eenmaal ingepakt in `%TEMP%\mde-agent-download` tot er iets
  nieuwers op de share staat. Token zit er bewust niet in. De versiemelding in Test PCB linkt naar de pagina. Lokaal: zip
  gecontroleerd (84 MB, 3 s); de stand met een echte agent en `installeer.bat` vanuit een uitgepakte zip niet getest.
- **Certificaat van de server** (20-10-2026): de app draait met de eigen CA van Caddy ("Caddy Local Authority - 2026
  ECC Root", mde-root.crt). De agent is een service (systeemaccount) en vertrouwt alleen wat voor de COMPUTER
  (LocalMachine\Root) is geinstalleerd; een dubbelklik zet het alleen bij de gebruiker → firmware ophalen faalde met
  CERTIFICATE_VERIFY_FAILED op een werkstation. `publiceer-z.bat` zet mde-root.crt (uit de store van deze pc) op de
  share, `installeer.bat` voegt het toe met `certutil -addstore Root`. Agent 1.18.1 meldt dit direct en duidelijk
  (`_hulp.haal_firmware`) in plaats van vier keer opnieuw te proberen.
- **Npcap** (20-10-2026): de installers bevatten Npcap NIET (er stond nooit een `npcap-*.exe` naast installer.iss,
  ook niet in D:\MdeAgent); op de eigen pc stond het los. Een nieuw werkstation kan dan wel USB (ESP32) maar geen
  EtherCAT (RZ-print, Everest). Agent 1.18.2: `drives.npcap_aanwezig()` (zelfde controle als installer.iss:
  System32\Npcap\wpcap.dll of System32\wpcap.dll); `/health` en Automatisch uitlezen melden dan "Npcap ontbreekt"
  i.p.v. "geen drives", en noemen bij geen antwoord welke adapters zijn geprobeerd. Npcap meeleveren = de installer
  van npcap.com naast installer.iss zetten vóór bouw.bat (let op de Npcap-licentie bij verspreiden).
- OpenBLAS op 1 thread (`OPENBLAS_NUM_THREADS=1` in bouw.bat/build.ps1/start.py): anders sterven PyInstallers
  kindprocessen op pc's met veel kernen of een krap wisselbestand. `numpy.f2py` NIET uitsluiten.
- Taken staan in `mde_agent/taken/` en worden geregistreerd in `taken/__init__.py` (`ALLE`). Patroon:
  `NAAM`, `OMSCHRIJVING`, `beschikbaar(inst)`, `voer_uit(job, inst, parameters)`; `job.log/voortgang/klaar/mislukt`;
  `job.resultaat` mag tussentijds gezet worden met `"live": True` voor live-weergave in de app
  (`VoerTaakUit(..., live: handler)` in ExemplaarWerk). `job.stop_gevraagd` = Stop-knop.
- **Teller communicatiefouten** (agent 1.22.0, 26-10-2026, wens gebruiker): `_ingenia.sdo()` (4 pogingen, alleen bij
  "working counter"/"wkc"/"timeout", pauze 0,08·n s) telt via `jobs.tel_comm` elke herhaling en elke actie die na alle
  pogingen mislukt, bij de lopende job (`Job.comm`: herhaald, mislukt, perDrive op servo-alias, laatste) en sinds de start
  van de agent (`COMM_SINDS_START`, in `/health` als `comm`). Aan het eind van een job een logregel "Communicatie met de
  drives: …" als er iets was. De ~37 rechtstreekse ingeniamotion-aanroepen in de drivetaken (registers, posities,
  move_to_position, motor_disable) lopen nu ook via `sdo()` (dus ook 3 retries; allemaal idempotent). De app
  (`ExemplaarWerk.CommTekst`) zet het achter de uitslag van de taak en schrijft bij klaar een neutrale historieregel, bij
  mislukt in de bestaande regel. Droog getest met een nepfunctie (scratchpad `comm_toets.py`); niet met een echte drive.
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
  blijft staan (zie ook "EEPROM volgens ETG.2010", agent 1.25.0). De werker leest eerst, logt per gebied wat verschilt, schrijft alleen afwijkende woorden in INIT en
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
- **`CHANGELOG.md` bijwerken bij elke commit** (afspraak 11-11-2026), in dezelfde commit: onder `## d-M-yyyy` van de
  commitdatum (kop bovenaan erbij als die er nog niet is) een regel `- **Onderdeel** — Nieuw|Verbeterd|Opgelost: tekst`,
  in gebruikerstaal voor collega's, met "(verzoek van Naam)" als een collega erom vroeg. Technisch onderhoud (build,
  CLAUDE.md, scripts) hoort er niet in. De app toont het bestand op `/nieuw` (Wat is er nieuw).

## Creditcards als rekening (11-11-2026)

Wens gebruiker: creditcards net zo behandelen als rekeningen (koppelen aan facturen, grootboek). Transacties hangen aan een
rekening via `banktransacties.Rekening` = `Bankrekeningen.Rekeningnummer`; dat blijft de sleutel, dus nummers blijven uniek.
- `Bankrekeningen_Creditcard_MariaDB.sql` (**zelf draaien**): `Soort` (0 bank, 1 creditcard) en `AfgerekendVia` (IBAN van
  de maandafrekening, mag bij meerdere kaarten gelijk). `IBankRepository.SoortKolomAsync` (alleen "ja" gecachet);
  zonder kolommen kan alleen een IBAN en zegt Bankrekeningen dat het script nodig is.
- Bankrekeningen: kolommen Soort en Afgerekend via; nummer = IBAN (bank) of 4-19 cijfers (creditcard, `NummerKlopt`).
- Import (`ImporteerAsync` stuurt door naar `ImporteerCreditcardAsync` als de kop "Creditcard Nummer" heeft; **vóór** de
  IBAN-kopcontrole, anders kwamen kaartregels op de betaalrekening uit "Tegenrekening IBAN"). Rabo BusinessCard-export
  (RA_CC_….csv): kaart = rekening met dat nummer (onbekende kaart = melding met aantal, regels overgeslagen); ontdubbelen op
  Rekening + Transactiereferentie (uniek per kaart, niet over kaarten); NaamTegenpartij = eerste 25 tekens van de
  omschrijving (vaste breedte: winkel 25, plaats 13, land 3), NaamInitierendePartij = kaarthouder, Tegenrekening leeg,
  Saldo na trn NULL (geen saldo in de export), oorspronkelijk bedrag/munt mee; AfgerekendVia eenmalig uit de export.
  Getoetst (alleen lezen) op de export van 11-11-2026: 671 regels, kaarten 6481/8960/2723, alles leesbaar. Niet in de
  database geïmporteerd. "Verrekening vorig overzicht" en "Koersopslag" zijn gewone regels.
- **Importdatum** (melding gebruiker bij de eerste creditcard-import): `banktransacties.Importdatum` is VARCHAR(25) met
  Access-tekst "9-11-2026 09:14:05"; een DateTime-parameter werd 26 tekens ("Data too long"). Nu `Importdatum()` als tekst
  in alle drie de inserts (CSV, creditcard, API). Gold dus ook voor de gewone CSV- en API-import; de regels tot 9-11-2026
  kwamen nog uit Access.

## Wat is er nieuw / changelog (11-11-2026)

Wens gebruiker: een changelog per datum, ook voor collega's, met namen. `CHANGELOG.md` (repo-root, met terugwerkende kracht
gevuld uit Git: 13-9-2026 = eerste commit als één blok, daarna 13-10 t/m 11-11-2026 per dag) wordt meegekopieerd naast de
dll (`EverythingApp.csproj`). `Services/Changelog.cs` leest hem (opnieuw als hij gewijzigd is); `Components/Pages/
WatIsErNieuw.razor` (`/nieuw`, Beheer ▸ Verbeteren ▸ Wat is er nieuw) met filter op onderdeel en zoeken; dagen na je vorige
bezoek krijgen "nieuw". `Components/Layout/NieuwKnop.razor` in de bovenbalk: "🆕 Nieuw" alleen als er iets is van na je
vorige bezoek (of je de pagina nog nooit opende). Onthouden per browser in localStorage `mde-nieuw-gezien` (geen database).

## BV op inkooporders (10-11-2026)

Script **`BestellingBv_MariaDB.sql`**: `Orders.BvID` (bestaande: BV van het project van de werkorder, anders 5254).
- **De BV volgt altijd het project** (besluit gebruiker 10-11-2026: een MPR-project = MPR, een MDE-project = AUTO; niet met
  de hand te kiezen). Opgeslagen in Orders.BvID voor de lijst/het filter en de historie. Nieuwe bestelling:
  `ProjectBvAsync(werkorder)` (Projects.Werkmaatschappij), anders MDE Automation.
- **Formulier**: veld "BV (volgt het project)" alleen-lezen (`VeldRaster` toont bij een alleen-lezen keuzeveld nu de naam
  uit de lijst); andere werkorder = BV van dat project. Zonder kolom verborgen.
- **Verkooporders**: het project "Verkoop jjjj" wordt per klant **en per BV** aangemaakt (andere BV: "Verkoop jjjj AFK"),
  met Werkmaatschappij = de BV van de VO; een bestaand project zonder Werkmaatschappij telt als AUTO. Zo volgt de BV van
  de inkoop vanzelf.
- **Lijst**: filter "Alle BV's", chip met de afkorting als het niet AUTO is.
- **Mag bestellen** (script **`BvMagBestellen_MariaDB.sql`**, `Relations.MagBestellen`, Relaties > tab BV): een werkorder
  van een project van een BV zonder dat vinkje wordt geweigerd (`Bestellingen.BvMagNiet`), net als een nieuwe bestelling
  (`NieuweBestellingAsync` gooit) en bestellen vanuit een verkooporder. Bij het aanmaken van de kolom: AUTO en MPR.
  Het BV-filter in de lijst toont alleen BV's met het vinkje (`BestelBvsAsync`, lijst "BestelBV"; alleen als het er meer
  dan één zijn), en de werkorders (formulier en Nieuwe bestelling) laten werkorders van projecten van andere BV's weg
  (`WerkordersZonderBestelrechtAsync`). Lijst "BV" blijft alle eigen BV's bevatten voor de naam van oude bestellingen.
- **Nieuwe bestelling** (`NieuweBestellingVenster`, parameters `Bvs`/`WerkorderBv`): bovenaan een BV-keuze (alleen BV's
  met Mag bestellen; bij één BV geen keuze), standaard de BV van je laatst gebruikte werkorder, anders AUTO. De werkorders
  tonen alleen projecten van die BV (`WerkorderBvsAsync`: open werkorders → BV van het project). Zonder werkorder krijgt
  de bestelling de gekozen BV; met werkorder volgt hij het project. Lokaal bekeken (AUTO: MDE-werkorders, MPR: nog leeg).
  BestellingBv_MariaDB.sql was op 10-11-2026 al gedraaid (AUTO 17.322, HOLD 380, ROB 139 bestellingen).
- **Mail aan de leverancier** (`BestelMailAsync`, `BestelMail.Html`): adresregel en naam onder de ondertekening van de
  BV van de bestelling; is het niet de standaard-BV (`Bestellingen:EigenRelatieId`, 5254) dan een blok "Graag de factuur
  richten aan" met naam, adres, btw-nummer uit Relations. Getoetst met een los programma (MPR wel, AUTO niet).
- **Inkomende factuur**: PO koppelen vult een lege administratie met de BV van de PO, of waarschuwt als ze verschillen
  (`IFactuurRepository.PoBvAsync`, `FactuurVenster.BvVanPo`).
- Ontvangst boekt nog steeds op de werkorder (projectmateriaal). Inkopen voor de eigen voorraad is een volgende stap.
- Niet in de browser getest met de kolom (script nog niet gedraaid); zonder kolom laadt Bestellingen zoals voorheen.
- **Projecten** (wens gebruiker 10-11-2026): filter "Alle BV's" (alle eigen BV's, opgeheven achteraan; leeg
  Werkmaatschappij telt als AUTO) via `TelProjectenAsync`/`ZoekProjectenAsync(..., bv)`; ✕ wist het mee. Nieuw project:
  keuze BV (alleen BV's met "Mag bestellen", `BestelBvsAsync`; zonder die kolom alle niet opgeheven), standaard de BV van het filter, anders AUTO; `NieuwProjectAsync(..., bvId)` zet
  Werkmaatschappij meteen (ook gebruikt door het verkoopproject van een VO; de losse UPDATE is weg). Lokaal bekeken:
  AUTO 1502 van 1506 open projecten, MPR nog 0; niet aangemaakt.

## Calculaties, stap 1 (10-11-2026)

Het voorbeeld "Calculatieboom FIT Clean" (claude.ai-artifact, goed ontvangen door collega's) in de app. Besluiten gebruiker:
**per offerte één calculatie**, **de bestaande boom overnemen** (zelfde bediening), eerste stap = **calculeren en
bevriezen**; werkorders aanmaken (budget, kostenbegroting, delen met begrote uren) en boeken op delen volgen later.
- Script **`Calculaties_MariaDB.sql`** (zelf draaien): `Calculaties` (OfferteID uniek, Status 0 concept / 1 bevroren,
  Versie, Data = de boom als JSON, totalen voor de lijst) en `CalculatieTarieven` (kostprijs per uur per UrenActiviteit,
  beginwaarden blad Uurtarief). Zonder script: uitleg op de pagina.
- **Boom** = `wwwroot/calculatieboom.html`: gemaakt uit het artifact met scratchpad `calcboom.py`; **wijzig voortaan dit
  bestand**. Draait in een iframe (eigen opmaak, botst niet met app.css); thema volgt `html[data-thema]` van de app.
  Laadt/bewaart via **`/calculaties/api/{offerte}`** (Program.cs): GET (info, activiteiten met tarief, data, versie,
  rechten), PUT (debounce 0,7 s, met versie: verouderd = 409 en stoppen; bevroren mag alleen wat
  `CalculatieData.Kern` weglaat veranderen: notities, in-/uitklappen), POST `bevries` / `ontdooi`. `RO()` in de boom =
  bevroren of geen recht. Werkordernummers in de vooruitblik = projectnummer-01…; knop Werkorders aanmaken staat uit.
- **Rechten**: lezen = aangemeld; wijzigen/bevriezen = gekoppelde werknemer (zoals offertes) of Directie; ontdooien =
  Directie; tarieven = Financial/Directie. Bron bij een notitie: alleen de bestandsnaam (filestore later).
- **Pagina** `Components/Pages/Calculaties.razor` (`/projecten/calculaties`, `/{offerteId}`): recente calculaties of
  offertes zoeken (`IOfferteRepository.ZoekAsync` + `VoorOffertesAsync`), maken (leeg of kopie van een andere in het
  project), tarieven. **Overzicht** (wens gebruiker): standaard een **boom Klant ▸ Project ▸ Offerte** (de laatste 500
  offertes of de zoekresultaten, met hun calculatie), op klant- en projectregel chipjes per offertestatus, aantal
  calculaties/bevroren, offertebedrag, uren en verkoop van de calculaties; filterknoppen offertestatus (meer tegelijk) en
  calculatie (nog geen / concept / bevroren) met aantallen; klanten dicht tenzij je zoekt of filtert; weergave Lijst =
  dezelfde regels plat. Menu Projecten: groep **Offertes en calculaties**, tegel Calculaties; knop 🧮 Calculatie bij een
  offerte. Repository `Services/ICalculatieRepository.cs`; lokaal `Calculaties__Geheugen=true` (offertes echt,
  calculaties en tarieven in het geheugen, ook zonder script).
- Getest met Geheugen en rechten tijdelijk open (teruggezet): maken, plakken uit Excel (5 regels, verkoop € 335.664 =
  nagerekend), opslaan met versie, 409 bij een oude versie, 423 bij een getal na bevriezen, notitie na bevriezen wel,
  ontdooien, werkordernummers 224112-01/-02, donker thema. Niet getest: echte database, twee echte gebruikers tegelijk.

## Ingelogde gebruiker in losse adressen (10-11-2026)

Melding gebruiker: de calculatieboom stond in productie op "Concept · alleen lezen". Oorzaak: `HuidigeGebruiker` leest de
gebruiker via Blazor's `AuthenticationStateProvider`, en die is bij een gewoon HTTP-verzoek (minimal API) niet gezet →
iedereen "niet ingelogd" (`GetAuthenticationStateAsync` gooit, `LaadAsync` vangt). Nu zet een middleware in Program.cs
(na `UseAuthorization`) per verzoek `IHostEnvironmentAuthenticationStateProvider.SetAuthenticationState(ctx.User)`.
Gevolg ook elders: **/bestand en /voorbeeld** van vertrouwelijke soorten (personeelsdossier, BV-documenten,
verzekeringen, berichtbijlagen) en de **Fusion-download** controleerden tot nu toe tegen een niet-ingelogde gebruiker
(soorten: altijd geweigerd; Fusion: `!Gekoppeld` liet iedereen door); nu tellen de echte rechten. `MagRabobank` las al
zelf het cookie. Lokaal alleen niet-ingelogd getest (pagina's en API laden, geen fouten in het logboek).

## Banktransacties: rustiger detail, ongekoppeld nalopen, betalingscheck op rekening (11-11-2026)

Wensen gebruiker. **Detailvenster**: velden onder elkaar en links (`.voorbeeld-inhoud.bankdetail-inhoud` = display block; de
voorbeeldvenster-stijl stond later in app.css en centreerde alles, vandaar het lege stuk bovenin). **Factuur koppelen** in hetzelfde
venster (een tweede venster werkte niet handig): open zolang het bedrag niet gedekt is, anders achter een knop (`_koppelOpen`). Kolom Tenaamstelling
heet **Tegenpartij** en toont `NaamTegenpartij` (anders de tenaamstelling; die was altijd leeg).
**🔗 Ongekoppeld…** (Financial/Directie): de transacties in de lijst zonder factuur (max. 300) nalopen met`FactuurKandidatenAsync`;
`Beste`: zeker = één met nummer, bedrag en rekening (of nummer en bedrag als geen voorstel de rekening kent), aangevinkt;
anders één met rekening en bedrag, nog nergens gekoppeld ("zelf bekijken"). Een factuur maar bij één transactie. Per regel
koppelen of de aangevinkte samen (met bevestiging). Lezend gemeten sep-okt 2026: 354 ongekoppeld, 60 zeker, 10 zelf bekijken,
21 s. **Betalingscheck**: `OpenFactuur.KlantId`, `Ontvangst.Tegenrekening`, `KlantRekeningenAsync` (Relations.IBAN +
RelatieIbans); stap 4 van `Betalingsmatcher`: bedrag gelijk en de betaler is een bekende rekening van de klant = aangevinkt,
ook als een andere klant een factuur met hetzelfde bedrag open heeft (gemeten: 4 voorstellen kregen dit). Detailvenster en
scan niet in de browser gezien (lokaal geen bankrekeningen zonder login); de logica lezend getest via de dll.

**Datum in de beoordeling** (11-11-2026): `FactuurKandidaat.Dagen` (transactie min factuur); `DatumPast` = -7..120 dagen,
`NaBetaling` (> 7 dagen na de betaling: nooit zeker), `Oud` (> 120). Volgorde nummer > rekening > bedrag > datum > niet elders;
de scan kiest bij rekening + bedrag de laatste factuur vóór de betaling (maandelijkse facturen). Lezend sep-okt: 291 ongekoppeld
(de gebruiker had er al ruim 60 gekoppeld), 7 zeker, 7 zelf bekijken.

## Artikelen: 🛒 In orderbatch (11-11-2026)

Verzoek collega Stein (zat ook in Access): knop naast ✎ Bewerken in het artikeldetail, voor iedereen (zoals de scanner).
`Components/OrderBatchVenster.razor`: aantal (−/＋ per verpakking van de voorkeursleverancier; afronden doet
`IOrderBatchRepository.VoegToeAsync` al, het venster toont "wordt N"), werkorder (voorraadorders, standaard de
standaard-voorraadorder; 🔍 = `WerkorderKiezer` met AlleenInkoop), opmerking; Enter = toevoegen. Samenstelling = onderdelen
los in de batch. `viaScanner: false`, daarna `BatchBel.Meld()`. Lokaal alleen het venster bekeken (geen geheugenstand
voor de orderbatch, dus niet toegevoegd).

## Rechten: Artikelbeheer, bankrekeningen alleen Financial/Directie (11-11-2026)

- **Artikelen en Artikelsamenstellingen wijzigen**: recht `Artikelbeheer` (nieuwe kolom, `Werknemers_Artikelbeheer_MariaDB.sql`,
  vinkje in Werknemers ▸ Rechten) of Directie. Daarvoor keek de code naar `Inkoop` en `Beheer`: **die kolommen bestaan niet**
  in `Werknemers` (alleen Directie mocht dus), en wie niet aan een werknemer gekoppeld was (`!Ik.Gekoppeld`) mocht alles;
  dat gat is dicht. "+ Artikel" (nieuw artikel aanmaken) blijft voor iedereen. Nog open: `Mag("Beheer")` staat ook in
  MainLayout, Instellingen, Onderhoud en Werkplekken (daar telt dus alleen Directie of niet ingelogd/gekoppeld).
- **Relaties ▸ Bankrekeningen** (tab, zijblok Rekeningen en IBAN/BIC in Opties) alleen met Financial of Directie
  (`MagIbansBeheren`); anders worden de rekeningen ook niet geladen.
- **Financiën ▸ Bankrekeningen**: BV-keuze bovenaan ("Alle BV's", per BV met aantal, "zonder BV" achteraan), zoals bij
  Banktransacties; een nieuwe rekening krijgt de gekozen BV.
- **Banktransacties: rekening kiezen op id** (melding gebruiker): de keuzelijst gebruikte het rekeningnummer als waarde, en
  nieuwe rekeningen zonder nummer (IBAN First, creditcards, import volgt) gaven dus "" = alle rekeningen. Nu `_rekeningId`;
  zonder nummer = geen regels. In de lijst staat zo'n rekening als "Omschrijving · nog geen nummer".

## Bestellingen: pakbon om mee te geven (11-11-2026)

Wens gebruiker: delen die iemand naar de leverancier rijdt (bv. naar Nederlandse Anodising) krijgen een pakbon mee. Knop
**📦 Pakbon** in de regelsbalk van Bestellingen: maakt in `Pakbonnen` een bon voor de leverancier (`Orders.Supplier`, net als
`Pakbonnen.RelationID` een `Relations.id_supplier`), t.a.v. het contact van de bestelling, met de regels van de bestelling
(zonder kostenregels, `PakbonRegels.IsKostenregel`, gedeeld met Pakbonnen ▸ Uit PO), gewicht uit Artikelen, Remark
"Uit PO-…" en referentie "PO-…". Daarna meteen de PDF (`PakbonPdfService`) in een venster: Met afbeeldingen, Downloaden,
✎ In Pakbonnen (`?pakbon=`). Is er al een pakbon met een regel "Uit PO-…" (`IPakbonRepository.PakbonnenVanPoAsync`), dan
eerst de vraag die te openen (Annuleren = nieuwe). Lokaal getest met `Pakbon__Geheugen=true` op PO-18521 (3 regels, 3,471 kg).

## Bestellingen: importeren uit een andere bestelling (11-11-2026)

Wens gebruiker (frezen bij Jansen, daarna anodiseren bij Nederlandse Anodising). Importeren ▸ **📦 Uit andere bestelling**
(`Components/ImportBestellingVenster.razor`): bestelling zoeken (`IBestelRepository.ZoekAsync`, de recentste bij openen), regels
aanvinken met eigen aantal; artikelcode, omschrijving, MDE-nummer, eenheid en werkorder gaan mee, leverdatum = die van de nieuwe
bestelling. Opties: prijzen niet overnemen (standaard), tekst vóór de omschrijving ("Anodiseren:"), "uit PO-…" in de
opmerking, en **bijlagen per soort** (PDF/STEP/DXF met aantallen, wens gebruiker; nieuwste per bestandsnaam, gekopieerd
via `FilestoreService`). Lokaal bekeken met PO-18506 (Jansen, 4 regels, elk DXF/PDF/STEP); niet toegevoegd.
Zoeken (wens gebruiker): `IBestelRepository.ZoekVoorImportAsync` - elk woord past op PO-nummer, leverancier, omschrijving of
besteller (`Orders.Created by`, NaamVoluit/Afkorting): "Jansen Leon" = bij Jansen door Leon Kerkvliet; kolom Besteller in de lijst.
Bouwwaarschuwingen opgeruimd (11-11-2026): 0 warnings.

## Grootboekrekeningen en grootboek per afschrift (11-11-2026)

Wens gebruiker. Script **`Grootboekrekeningen_MariaDB.sql`**: tabel `Grootboekrekeningen` (BvID, Administratie = naam uit de
export, Code als tekst met voorloopnul, Naam, CodeRelatieKostenplaats, Actief; uniek per administratie + code; nooit wissen,
niet-actief) en `banktransacties.GrootboekID`. `Services/IGrootboekRepository.cs`: `GrootboekExcel.Lees` (OpenXml; kopregel op
naam, cellen zonder verwijzing, code als getal -> voorloopnul terug tot de gebruikelijke lengte), `Vergelijk` (nieuw /
gewijzigd / gelijk / ontbrekend), MySql (BvID op naam of afkorting van de eigen BV, zonder punten en spaties), Geheugen
(`Grootboek__Geheugen=true`), `GrootboekKoppeling` (tabel/kolom aanwezig). Financiën ▸ Bank ▸ **Grootboekrekeningen**
(`/financien/grootboekrekeningen`, Financial; beheren Financial/Directie): filter administratie, zoeken, naam en kostenplaats
bewerken, actief, toevoegen, **Excel inlezen** met voorbeeld (ontbrekende eventueel op niet-actief) en **Naar Excel** (zelfde
kolommen). Banktransacties: kolom Grootboek en in het detail een keuzelijst (de actieve rekeningen van de BV van die
bankrekening). Getest: inlezen van proefbestanden (tekst- en getalcodes) en de pagina met Geheugen; de koppeling in
Banktransacties niet in de browser (lokaal geen rekeningen zonder login).
**Ook .xls** (melding gebruiker: de export is een .xls): `GrootboekExcel.Lees` herkent de soort aan de inhoud: ZIP = xlsx
(OpenXml), OLE2 = Excel 97-2003 via het pakket **ExcelDataReader 3.7.0** (CodePages-encoding geregistreerd), HTML-tabel en
Excel 2003 XML (exports die zich .xls noemen), anders CSV/TSV (scheidingsteken uit de eerste regel). Cellen worden `Cel(Tekst,
Getal)`, daarna dezelfde `Verwerk`. Getest met proefbestanden in alle soorten (de .xls gemaakt met Excel).

## Factuur-inbox: foto's in de mail, opnieuw proberen, zonder factuur (11-11-2026)

Melding gebruiker: veel "Geen factuur gevonden" en "Bedrock gaf 503". Oorzaken: `MailService` sloeg elk onderdeel met
Content-Disposition **inline** over (bedoeld voor logo's), maar een op de telefoon geplakte foto en bijlagen uit Apple Mail
komen zo binnen; herkenning ging alleen op extensie; een 503 werd opgeslagen en de mail gelabeld, dus nooit opnieuw.
Nu: `MailBericht.Ingebed` (inline-onderdelen, `MailBijlage.Soort`/`Inline`; het Mail-scherm ziet ze niet), de ophaler
neemt ingebedde PDF's en foto's vanaf 100 kB mee (`MinIngebedFoto`), herkent op MIME-type (`Soort`, naam krijgt de juiste
extensie), meldt HEIC (iPhone) duidelijk, en `LeesMetHerhalingAsync` probeert bij 503/429/time-out na 5, 30 en 120 s opnieuw.
Te beoordelen: per regel **Opnieuw uitlezen** (`OpnieuwUitlezenAsync`, bewaarde bijlage, `BijwerkenGelezenAsync`) en **Mail
opnieuw verwerken** (`OpnieuwVerwerkenAsync`: nieuwe regels, oude genegeerd; anders "nog steeds geen factuur" met de
onderdelen van de mail); vinkje **Zonder factuur verbergen** en **Alle zonder factuur negeren**. Lokaal bekeken: 69 regels,
9 opnieuw uitlezen, 18 opnieuw verwerken. Niet getest: het ophalen zelf (de sleutel van het serviceaccount staat alleen
op de server). Transactielijst: rijen compacter, opmerking zonder kader (alleen `tr.bt-rij`; `.banktabel` zit ook in
het rechtenpaneel).

## Inkoopfactuur: Doorbelasten is 1 = 100% (11-11-2026)

`FacturenInkomend.PercentageDoorbelasten` is een geheel getal (standaard 100) dat Access als percentage toonde: alle 23.824
facturen staan op 1, 225 oude op leeg; views geven het alleen door, nergens wordt ermee gerekend. Het factuurvenster toonde "1 %".
Nu: lezen 1 -> 100% (leeg -> 100%), keuzelijst 100% / 0%, opslaan 1 of 0 (een nieuwe factuur schreef eerder 100 = 10.000% in Access).
Banktransacties-voorstellen zonder zoektekst: alleen bedrag telt alleen als de datum past en de factuur nog nergens hangt
(TME-factuur van 820 dagen, al gekoppeld, werd voorgesteld). Detailvenster: rijen dichter, opmerking zonder kader.

## Relaties: verwijzingen als keuzelijst, activiteiten (11-11-2026)

Wens gebruiker (Inkoop contactpersoon stond als 6299). `RelatieVelden`: `Lijst` = "Contactpersoon" (OfferteContactID,
OrderContact-id, DefaultVerzendContact; de contactpersonen van de relatie), "Taal" (OrderLanguage; `IBestelRepository.GetTalenAsync`,
0 = Nederlands), "Werknemer" (Responsible; in dienst, oud-werknemers als "(niet meer in dienst)"), "Relatie"
(DefaultVerzendAdres; alle relaties, in blokken van 200 want `ZoekAsync` geeft er hoogstens 200). Een waarde buiten de lijst
blijft zichtbaar als "#nummer". De lijsten laden bij het eerste openen van Inkoop of Kenmerken (`KiesVeldTab`).
**Activiteiten**: `Business_id` stond overal op 0; op die plek nu vinkjes uit `RelationActivities` (29 soorten) via
`ActivityPerRelation` (24 relaties hadden er al; `GetActiviteitenAsync`, `ZetRelatieActiviteitAsync`). Bankrekeningen: geen
voorbeeldteksten in de invoervakjes (wens gebruiker). Lokaal bekeken (Moerland Engineering, Kalkhuis), niets gewijzigd.

## Relaties: bankrekeningen als lijst (11-11-2026)

Wens gebruiker: IBAN en BIC als bewerkbare lijst in plaats van twee velden. Script **`RelatieRekeningen_MariaDB.sql`** (na
RelatieIbans_MariaDB.sql): `RelatieIbans` + BIC, Omschrijving, Standaard, GewijzigdOp/Door, en de huidige IBAN/BIC van elke
relatie als standaard erin (11-11: 558 met IBAN, 554 met BIC). **De standaard blijft ook in `Relations.IBAN`/`BIC` staan**
(`ZetRelatieRekeningAsync`, alleen als er een standaard is): SEPA-betalingen (`IBetalingRepository`), factuur inlezen
(`GetBankgegevensAsync`, fraudecontrole in `FactuurVenster.ControleerIban`), Access en de BV-rekeningen lezen die kolommen.
Relaties ▸ tabblad **Bankrekeningen** (pas na het script; dan verdwijnen IBAN/BIC uit Opties en het zijblok): standaard
(rondje, met bevestiging), IBAN/BIC/omschrijving bewerken (IBAN met controlegetal, `Bankgegevens.IbanGeldig`; BIC leeg = uit
een NL-IBAN), ✕ (de standaard alleen als het de enige is), + Toevoegen. Alleen Financial/Directie wijzigt (fraudegevoelig).
Staat bij de relatie een IBAN dat niet de standaard in de lijst is (Access, "Uit screenshot"), dan een melding met
"Overnemen als standaard" (`RelatieIbans.SyncVanRelatieAsync`). Factuur inlezen dat een lege IBAN vult, zet hem ook in de lijst.
**Niet getest**: het tabblad en de schrijfacties (verschijnen pas na het script; lokaal niet tegen productie geschreven).

## Bekende rekeningen van relaties (11-11-2026)

Wens gebruiker: bij een koppeling transactie-factuur de tegenrekening bij de relatie onthouden (meer IBANs per relatie).
Script **`RelatieIbans_MariaDB.sql`**: tabel `RelatieIbans` (RelatieID, IBAN zonder spaties/hoofdletters, Bron, wanneer, door;
uniek per relatie+IBAN) en vullen uit de bestaande koppelingen (11-11: 155 rekeningen bij 135 relaties; 513 combinaties
waren al `Relations.IBAN`). `Services/RelatieIbans.cs`: `LeerAsync` (tegenrekening bij de leverancier/klant van de factuur,
alleen een geldig IBAN, niet als het al Relations.IBAN is), `LeerAllesAsync` (na het vastleggen, fout stopt niets),
`KentRekening` (SQL voor de matching). Geleerd bij: handmatig koppelen (Banktransacties, `KoppelFactuurAsync` met `door`),
de betalingscheck met deelbetalingen (`FactuurBetalingRepository.VerwerkAsync`) en Verkoopfacturen ▸ betalingen verwerken
(`VerwerkBetalingenAsync`). Gebruikt in de voorstellen ("zelfde rekening") en achter de tegenrekening in het detail.
Relaties ▸ zijblok **Rekeningen** (altijd: eerst Relations.IBAN, dan de bekende; ✕ bij een bekende voor Financial/Directie, bijvoorbeeld een betaaldienst:
sommige rekeningen horen bij meerdere relaties, zoals DEUT-rekeningen van webshops). Zonder de tabel doet alles niets.
De automatische betalingscheck (`Betalingsmatcher`) kijkt nog niet naar de rekening, alleen naar de naam.

## Banktransacties: factuur handmatig koppelen (11-11-2026)

Wens gebruiker (Weyers 26-10: "Rechnung 2026185", geen koppeling). Detailvenster ▸ **Factuur koppelen** (Financial/Directie):
soort uitgaand/inkomend (standaard op het teken van het bedrag), voorstellen zonder zoektekst, of zoeken (elk woord in
nummer, relatie, omschrijving of als bedrag, `AlsBedrag`). `IBankRepository.FactuurKandidatenAsync` (`Kant` = kolommen per
kant): **alleen facturen van de BV van de rekening** (`Invoices.Werkmaatschappij` / `FacturenInkomend.Admin`, leeg = 5254;
factuurnummers zijn per BV, 2026011 bestaat bij vijf BV's), niet al aan deze transactie. Volgorde: nummer uit omschrijving of
betalingskenmerk (`NummersIn`) > tegenrekening = IBAN van de relatie (Moerland/Denissen Engineering sturen allebei 2026011 van
€ 15.125) > zelfde bedrag > nog nergens gekoppeld > dichtst bij de datum. `KoppelFactuurAsync` (INSERT met `Datum`, niet dubbel),
`OntkoppelFactuurAsync` (✕ bij een gekoppelde factuur; weigert als de koppeling bij een `FactuurBetalingen`-regel hoort).
Alleen de koppeling: de betaald-status verandert niet. Dekken de gekoppelde facturen het hele bedrag, dan geen voorstellen meer
(link "Toch nog een factuur koppelen"); kloppen nummer, bedrag en rekening (of nummer en bedrag als geen voorstel de rekening
kent), dan alleen die, met een link voor de gedeeltelijke (`Volledig`, `AlleenVolledig`). Detail toont de BV achter de
rekening en de relatie(s) achter de tegenrekening. Lezend getest (console tegen de dll) op Weyers, Record Industry, GP
Stainless en de managementfees: elk de goede factuur bovenaan. Ook de managementfees van eerdere maanden hingen nergens aan.
**Let op (11-11-2026):** commit 3f51963 zette per ongeluk de CLAUDE.md van InventorTools hier neer (script las een relatief
pad uit de verkeerde map); hersteld uit 983a741. Lees en schrijf CLAUDE.md altijd met het volledige pad.

## Todo-overzicht per werkzaamheid (10-11-2026)

Wens gebruiker. Vinkje **Per werkzaamheid** (standaard aan, onthouden via `mdeOpslag` `todoGroeperen`): per Wat een kop
(`tr.todo-groep`) met het aantal open (en gereed bij Ook gereed), klik = in-/uitklappen (`_dicht`), knoppen Alles open /
Alles dicht; de kolom Wat valt dan weg (`Kolommen`, kopklasse `todo-toel-kop` in plaats van `nth-child(5)`). Rij als
RenderFragment `Rij`. Lokaal bekeken met de echte punten (155 open, 13 werkzaamheden), niets afgevinkt.

## Banktransacties: eerst de BV, dan de rekening (10-11-2026)

Wens gebruiker. Keuzelijst **BV** vóór Rekening (alleen als je rekeningen van meer dan één BV mag zien); de rekeninglijst
toont dan alleen die BV, "Alle rekeningen van AUTO" telt alle rekeningen van die BV (`RekeningenVanBv`, `Gekozen()`).
`Bankrekening` heeft `Bv` (afkorting, anders naam, uit Relations via `Bankrekeningen.BVid`); een rekening zonder BV valt
onder "zonder BV" (nu geen). "Saldo alle rekeningen" blijft alle rekeningen waar je bij mag; het andere saldo heet nu
naar de keuze (rekening / BV / gekozen rekeningen). Lokaal niet in beeld te krijgen (zonder ingelogde werknemer geen
rekeningen); de query is lezend gecontroleerd: AUTO, HOLD, ME, PROD, ROB.

## Calculaties, stap 3: uren boeken op de delen van een werkorder (10-11-2026)

Wens gebruiker: per deel zien of het uitloopt. Zoals het ontwerp in de boom al zei: heeft een werkorder delen, dan is
het deel **verplicht**; de activiteit blijft zoals ze was (het deel zet hem niet).
- Script **`UrenDelen_MariaDB.sql`**: `Uren.WerkorderDeelID` (INT NULL, index). Daarna de app **herstarten**:
  `MySqlUrenRepository` leest de kolommen één keer; `KentWerkorderDeel` (nieuw op `IUrenRepository`, standaard false)
  zegt of de kolom er is, en zonder kolom staat alles uit (geen keuze, geen controle), zodat het scherm nooit een deel
  vraagt dat niet bewaard wordt.
- `Services/IWerkorderDelenRepository.cs`: `BeschikbaarAsync` (kolom + tabel WerkorderDelen), `DelenAsync` (actieve delen
  per werkorder), `StandAsync` (begroot tegen geboekt per deel, ook inactieve delen, plus "zonder deel" = uren op een
  werkorder met delen maar zonder deel; zonder `GeneratedByRule`-regels). Geen / MySql / Geheugen
  (`UrenDelen__Geheugen=true`: elke werkorder drie nepdelen met een nepstand).
- **Ureningave**: onder de werkorder een keuzelijst (`DeelKeuze`, oranje zolang leeg) als de werkorder delen heeft;
  één deel = vanzelf gekozen (`PasDeelAan`, ook bij een andere werkorder); bestaande regel slaat op bij het kiezen.
  `UrenService.ValideerAsync` eist het deel en controleert dat het bij de werkorder hoort; Plak kopieert het deel mee.
- **Calculatie, tabblad Werkorders** (na het aanmaken): delen als `geboekt / begroot u` (rood = meer dan begroot, gestippeld
  = zonder deel) en een kolom Geboekt; GET `/calculaties/api/{offerte}` geeft `stand` en `info.boekenOpDelen`. Het
  voorbeeldje "Uren boeken (volgt)" (`vulBoekDelen`) is weg; het tabblad heet geen "vooruitblik" meer.
- **Nacalculatie**, tab Uren (en Alles): per werkorder met delen een tabel Deel / Begroot / Geboekt / Verschil / %, altijd
  over alle jaren (het jaarfilter geldt daar niet).
- Lokaal getest met `UrenDelen__Geheugen` + `Calculaties__Geheugen`: keuze in een nieuwe regel (niet opgeslagen,
  Annuleren), nacalculatie van 224112 (rechten tijdelijk open, teruggezet), het tabblad Werkorders met nepgegevens in de
  browser (fetch vervangen). **Niet getest**: opslaan van een regel met deel en de SQL van `StandAsync` tegen de echte
  database (de kolom bestaat nog niet).

## Calculaties, stap 2: werkorders aanmaken bij klant akkoord (10-11-2026)

Besluiten gebruiker: groepen zonder werkorder (korting, afronding) **naar rato** over de werkorderbudgetten (de
keuze "apart" is uit de boom gehaald); **kostenbegroting** per werkorder (uren en materiaal); **delen nu al vastleggen**;
na het aanmaken **niet meer ontdooien** (ook Directie niet; wijzigen = meerwerk).
- Script **`CalculatieWerkorders_MariaDB.sql`** (na Calculaties_MariaDB.sql): `CalculatieWerkorders` (calculatie, groep-id
  uit de JSON, werkbon, budget, kosten, uren) en `WerkorderDelen` (werkbon, naam, begrote uren/kosten, activiteit;
  uniek per werkbon+naam; nog nergens zichtbaar, voor boeken op delen).
- Boom: tabblad Werkorders toont de volgende vrije nummers (`info.volgendWerkbon`) of na het aanmaken de echte
  (`WERKORDERS`, met link Project openen); knop **Werkorders aanmaken** (`maakWerkorders`: eerst alles opslaan, dan het
  plan uit `werkorderPlan()` naar POST `/calculaties/api/{offerte}/werkorders`).
- App controleert het plan (bevroren, versie, ouders vóór kinderen, namen, delen uniek, som budgetten = Calculaties.Verkoop
  ± € 1) en `MySqlCalculatieRepository.MaakWerkordersAsync` maakt in **één transactie** (calculatie en de werkorders van
  het project `FOR UPDATE`): Workorders (volgend Werkbonnummer, omschrijving = groepnaam, Open, Doorbelast deel 100,
  InkoopToegestaan, Budget, ParentWerkbonID), CalculatieWerkorders, WerkorderDelen, en ProjectKostenbegroting
  ("224112-05 Counter (calculatie Q224112-01)", soort uren/materiaal; alleen als die tabel bestaat). Ontdooien na het
  aanmaken = 423.
- Getest met Geheugen (rechten tijdelijk open, teruggezet): Counter + Buffer (sub) + Korting zonder werkorder →
  224112-07 en ↳ -08, budgetten 35.937 / 13.304 (nagerekend, samen de verkoop 49.241), tweede keer 409, ontdooien 423.
  **De SQL van MaakWerkordersAsync is niet tegen de echte database getest** (zou schrijven): eerste echte keer
  nakijken in Projecten (werkorders, Budget verdeling, Kostenbegroting).

## Banktransacties: gekoppelde facturen (10-11-2026)

Wens gebruiker: geen vergrootglas meer, maar **dubbelklik** op een regel voor de details; in de details de **gekoppelde
facturen** (`banktransacties_facturen_crossref`: `Factureninkomend-id` → FacturenInkomend, `Facturenuitgaand-id` →
Invoices; ~16.000 koppelingen, vooral inkomend) met een klik naar de factuur (`FactuurVenster` / `VerkoopFactuurVenster`,
na het detailvenster in de markup zodat ze erboven liggen). In de lijst een kolom **Facturen** (🔗 n, klik = details);
het aantal komt uit een afgeleide tabel die de koppeltabel één keer telt, niet per regel.
`IBankRepository.GekoppeldeFacturenAsync`, `Banktransactie.Facturen`. Lokaal alleen lezend getest (203 van 388 transacties
gekoppeld; factuur 597324 Drabbe BV geopend). Lokaal zie je geen transacties zonder ingelogde werknemer
(BankRekeningRechten).

## Voorraad per BV, stap 1 (10-11-2026)

MDE Automation en M-Pede Robotics (MPR) krijgen elk een eigen voorraad. Ontwerp (voorstel, stap 1 gebouwd): één
artikelbestand en één lokatieboom; de voorraad hoort bij de BV van de bovenste lokatie (type Bedrijf, 17) waar hij onder
ligt. Script **`VoorraadPerBv_MariaDB.sql`**: `Locations.BvID` (alleen op de bovenste lokatie); MDE (1) en Wees (10)
→ MDE Automation (5254); nieuwe bovenste lokatie "M-Pede Robotics" (MPR) voor de BV met afkorting MPR.
`LokatieBoom.Bv(id)` erft de BV van boven (`HeeftBvs` = script gedraaid). Artikelen > Voorraad/Bestellen: actuele
voorraad = som van de posities (zoals Artikelvoorraad) met per BV een regel (elke BV, ook 0; "niet ingedeeld" als er
voorraad zonder BV is). Voorraad > Artikelvoorraad: per BV in de totaalregel en een BV-label per plek.
**Lokaties beheren** (gebouwd 10-11-2026, Voorraad > Lokaties, `Components/Pages/Lokaties.razor`, vervangt Access
LocationTreeview/LocationToevoegen): boom van de vaste plekken (dragers optioneel, aantal artikelen hier en eronder),
zoeken/scannen `@L…`; details wijzigen (omschrijving, afkorting, type binnen Lokatiestructuur, "voorraad mag hier",
opmerking; systeemlokaties niet); BV kiezen op een bovenste lokatie van type Bedrijf; toevoegen onder de gekozen plek
(types volgens Lokatiestructuur, vinkje voor andere; dragers niet, die blijven bij Dragers), ook als reeks (aantal n:
"Vak 1..n", afkorting doorgenummerd na de hoogste Index van dat type onder de ouder); verplaatsen (`LokatieKiezer`);
wissen alleen zonder lokaties eronder, zonder voorraadmutaties en zonder inhoud; labels (Dymo, één of alles eronder).
Repository: `NieuweLokatieAsync`, `BewaarLokatieAsync`, `WisLokatieAsync`, `ZetBvAsync`. Lokaal: `Lokaties__Geheugen=true`
(leest echt, schrijft in het geheugen). Getest in het geheugen: gebouw/ruimte/3 stellingen onder MPR (label
MPR-S48-MAG-1), wissen, weigeren met plekken eronder. Niet getest: echt opslaan en printen.
**Voorraadstand per BV** (gebouwd 10-11-2026, Financiën > Voorraadstand): `StandOpDatumAsync` geeft nu een regel per
artikel, status **en lokatie** (`VoorraadstandRegel.LokatieId`); het scherm telt op per BV via `LokatieBoom.Bv`
(`BvId`/`Bv`, "niet ingedeeld" = geen BV erboven of geen lokatie). Kaarten per BV (klik = filter), filter BV, kolom BV
(bij Alle), Excel met kolom BV. Op de lokatieboom van nu, ook voor een datum in het verleden. Getoetst: totaal gelijk
aan de oude query (€ 481.778,10, 1075 regels op 10-11-2026); alles is nu AUTO, MPR leeg.
**Volgende stappen** (nog niet gebouwd): bij ontvangst boeken op een plek van de BV van de bestelling, inkopen voor de
eigen voorraad (zonder project), verplaatsen tussen BV's = interne verkoop.

## Factuurinbox: al ingevoerde facturen weghalen (10-11-2026)

Michiel voert inkomende facturen nog in Access in, dus die bleven in Inkomende facturen > Te beoordelen staan. Nu krijgt
elk stuk dat al bij Inkomende facturen staat het label "✓ al ingevoerd" en haalt de knop **Al ingevoerde weghalen (n)**
ze in één keer weg (FactuurInbox status 1 = verwerkt, met de gevonden factuur). Regels in `InboxDubbel`
(`Services/IFactuurRepository.cs`): zelfde factuurnummer (genormaliseerd: zonder spaties, punten, streepjes, schuine
strepen, `_`; hoofdletters gelijk) en hetzelfde totaal (5 cent) of een woord uit de leveranciersnaam (kort nummer < 4
tekens: beide). Mislukte het uitlezen (geen nummer), dan nummers uit onderwerp en bestandsnaam (min. 5 tekens, 4 cijfers),
en dan moet een woord uit afzender of onderwerp in de leveranciersnaam staan. Zoeken: `IFactuurRepository.ZoekOpNummersAsync`.
Lokaal tegen de echte inbox bekeken (155 van 225 herkend), niet weggehaald.

## Verzekeringen (9-11-2026)

Financiën > Organisatie > **Verzekeringen** (`Components/Pages/Verzekeringen.razor`, `Services/IVerzekeringRepository.cs`),
alleen Financial/Directie. Script **`Verzekeringen_MariaDB.sql`** (tabel `Verzekeringen`, FileStoreType 361
`Verzekeringspolis`). Per verzekering: BV (verzekeringnemer), soort, verzekeraar en tussenpersoon (relaties), polisnummer,
verzekerd bedrag, eigen risico, premie (of gekoppeld aan een vaste last: dan alleen lezen en al in Liquiditeit),
ingangs- en contractvervaldatum, stilzwijgend verlengd (termijn in maanden), opzegtermijn, status, beheerder, dekking.
- **Herinnering** (`Services/VerzekeringWaker.cs`, elke 6 uur): vanaf 30 dagen vóór de uiterste opzegdatum (volgende
  vervaldatum min opzegtermijn) een melding (soort `verzekering`, 🛡️, link `financien/verzekeringen?verzekering=`) aan
  de Directie en de beheerder; één keer per vervaldatum (`GemeldVoor`). In de lijst oranje/rood.
- **Overnemen uit Vaste lasten**: elke vaste last in een categorie met "verzeker" die nog niet gekoppeld is wordt een
  verzekering (soort geraden met `VerzekeringSoort.Raad`), gekoppeld aan die vaste last.
- **Documenten**: StoreType 361, afgeschermd zoals de BV-documenten (`FilestoreService.MagSoortZien`).
- **Eén bron** (besluit gebruiker 9-11-2026; script **`VerzekeringenBron_MariaDB.sql`**: `Verzekeringen.PremieDag/
  PremieMaand`, `VasteLasten.Einddatum`): de verzekering bevat premie, termijn en betaaldag; `Services/VerzekeringBron.cs`
  maakt/werkt bij elk opslaan de vaste last bij (categorie Verzekeringen, omschrijving max 45, BV, einddatum = dag vóór de
  vervaldatum bij opgezegd/beëindigd; geen premie = vaste last weg; wissen neemt hem mee). Na het script neemt het scherm
  bij het openen eenmalig de premie over van de al gekoppelde vaste lasten. In Vaste lasten 🛡️, Aanpassen opent de
  verzekering, geen ✕; Liquiditeit opent bij dubbelklik ook de verzekering. `VasteLast.Betaaldagen` stopt na `Einddatum`
  (`Volgende` is nu nullable). Lokaal ook `VasteLasten__Geheugen=true` (leest echt, schrijft in het geheugen).
- Lokaal: `Verzekeringen__Geheugen=true` (de waker draait dan ook; vul in tests geen beheerder in, die krijgt anders een
  echte melding).

## BV-structuur en meerdere BV's (9-11-2026)

Aanleiding: M-Pede Robotics wordt een aparte BV naast MDE Automation; uren en materialen worden over en weer gefactureerd.
Eerst de structuur (script **`BvStructuur_MariaDB.sql`, zelf draaien**): `Relations.Opgeheven` (datum) en tabel
`BvAandeelhouders` (BV, aandeelhouder, percentage, vanaf/tot). Gewenste structuur volgens de gebruiker: Moerland
Engineering en Denissen Engineering elk 50 % in MDE Vastgoed; MDE Vastgoed 90 % en F.M. Moerland Holding 10 % in MDE
Holding; MDE Holding in MDE Patenten; MDE Patenten in MDE Automation en M-Pede Robotics. MDE Robotics (5645) en MDE
Products (5646) vervallen. MDE Vastgoed, MDE Patenten en M-Pede Robotics bestonden op 9-11-2026 nog niet als relatie.
- **Vinkjes** (kolommen ongewijzigd, nu in Relaties > tab BV): `Werkmaatschappij` = eigen BV die factureert (BV-keuzes bij
  facturen, projecten, PO klant, bankrekeningen); `MDEBV` = hoort bij de MDE-groep (Liquiditeit, Betalingen klaarzetten;
  samen met Werkmaatschappij = "intern" bij Vragen aan AI).
- **Opgeheven**: de BV blijft bij oude facturen/projecten staan; in de keuzelijsten "(opgeheven)" achteraan
  (`BvOpgeheven` in `Services/IBvStructuurRepository.cs`, werkt ook zonder de kolom), niet bij "facturerende BV's" voor
  een nieuwe factuur, en telt niet meer standaard mee in Liquiditeit.
- **Financiën > BV-structuur** (`Components/Pages/BvStructuur.razor`, Financial/Directie): organigram in lagen (volgorde
  per laag met barycentra, zodat lijnen niet kruisen; haakse lijnen met percentage via `wwwroot/organigram.js`, SVG achter
  de kaarten, opnieuw getekend bij elke maatverandering),
  per BV KvK, btw, werknemers (WerkzaamBij), rekeningen, facturen; aandeelhouders toevoegen/wijzigen (geen kring:
  niet zichzelf of iets wat hij bezit), opgeheven per, en documenten (filestore StoreType 360 `BV-document`, alleen
  Financial/Directie; niet bij Relaties > Documenten). Lokaal: `BvStructuur__Geheugen=true`.
- **Afscherming bestanden** (`FilestoreService.MagSoortZien`, `MagBestand` in Program.cs): 300/301 HRM/Directie, 360
  Financial/Directie, 341 alleen afzender/ontvanger; geldt voor `/bestand/{id}`, `/bestand/{id}/download` en (nieuw,
  daar zat geen controle op) `/voorbeeld/{id}`. Beheer > Filestore toont die soorten alleen aan wie ze mag zien.
- **Liquiditeit per BV** (besluiten gebruiker 9-11-2026): BV kiezen in een kleine boom van de MDE-groep (alleen BV's
  die in de structuur hangen, niet opgeheven), met "met dochters" (standaard aan); standaard MDE Holding (5660) met
  dochters. Telt mee: bankrekeningen, openstaande facturen en btw van die BV's, en de vaste lasten van die BV's
  (**`VasteLastenBv_MariaDB.sql`**: `VasteLasten.BvID`, bestaande op MDE Automation; BV te kiezen in Vaste lasten).
  Facturen tussen de gekozen BV's tellen niet mee (`OpenstaandeFactuur.RelatieId`). Zonder vastgelegde structuur de
  oude knoppen, nu alleen MDE-groep zonder opgeheven (DE, FM, ME niet meer).
- **Relaties**: veld Naam bovenaan tab Adres (melding gebruiker: bij een nieuwe relatie kon je geen naam ingeven; "name"
  stond bij de verborgen kolommen). Lege naam wordt geweigerd; de lijst links ververst. Datumvelden als datumkiezer.
- **Nog niet** (volgende stappen): welke BV een inkooporder doet (Orders heeft geen BV-kolom), inkomende facturen per
  eigen BV, en het doorfactureren van uren en materialen tussen BV's.

## Uitgaande facturen: deelbetalingen (FactuurBetalingen, 5-11-2026)

Wens collega: een factuur deels betaald melden (Viscon betaalt 90%, houdt 10% in); onbetaald laten staan maakte
Liquiditeit fout. Tabel `FactuurBetalingen` (script `FactuurBetalingen_MariaDB.sql`): per factuur regels met datum,
bedrag (incl. btw, het deel voor die factuur), soort `bank`/`handmatig`/`afboeking`, banktransactie, opmerking.
`Services/IFactuurBetalingRepository.cs` (Geen, MySql, Geheugen via `FactuurBetalingen__Geheugen=true`).
- **Betaald blijft de vlag** waar alles op filtert. Toevoegen (`MaakRondAsync`) zet hem als de regels samen het
  bedrag halen (marge `Betalingsmatcher.Marge` = 1 euro), op de datum van de laatste regel. Weghalen zet hem nooit
  terug (kan met de hand betaald zijn); het venster zegt dan dat je het vinkje zelf weghaalt. Een betaalde factuur
  zonder regels (alles van vóór de tabel) telt als volledig betaald.
- **Betalingscheck**: open facturen tellen met hun restant (totaal min regels), zodat een latere betaling van de rest
  gewoon "Zeker" wordt. "Bedrag wijkt af" met minder ontvangen krijgt een vinkje = deelbetaling (`BetalingVoorstel.
  Deelbetaling`). Verwerken (`VerwerkAsync`) schrijft per ontvangst/factuur een bankregel (`Betalingsmatcher.Verdeel`:
  ontvangsten op datum over de facturen, rest bij de laatste) plus de crossref, en zet Betaald waar het rond is.
  Zonder tabel werkt alles als voorheen (`Repo.VerwerkBetalingenAsync`).
- **Factuurvenster**: blok Betalingen onder Stand van zaken; ontvangen (handmatig) of afboeken (opmerking verplicht)
  schrijft meteen weg en neemt daarna Betaald/DatumBetaald uit de database over (anders zette Opslaan hem terug).
  Bankregels komen alleen via de betalingscheck; ✕ op een bankregel haalt ook de crossref weg.
- **Lijst**: ◐ achter Bet bij deels betaald (wolkje: ontvangen/open), status in de zijkant, en het blok Betalingen
  toont de regels (anders de gekoppelde banktransacties zoals voorheen).
- **Liquiditeit** (`OpenstaandAsync`): restant in plaats van het hele bedrag zodra de tabel bestaat.
- Lokaal getest tegen de live database met de geheugenversie en tijdelijk alle rechten: 90% ontvangen, 10% afboeken
  (zonder opmerking geweigerd), weghalen, lijst/zijkant, en de betalingscheck met de echte Viscon-factuur 2026153
  (€ 113.647,61 van € 126.275,12, open € 12.627,51). **Niet getest**: het echte wegschrijven (MySql-versie,
  `MaakRondAsync`, crossref) en Liquiditeit; eerst het script draaien.

## Uitgaande factuur: ＋ Pakbon (4-11-2026)

Melding collega: "Importeren uit pakbon bestaat niet meer". Nooit overgezet (analyse: in 2024-2026 niet gebruikt).
Nu in `VerkoopFactuurVenster` naast ＋ Artikel/＋ Uren: zoekvak met de pakbonnen (`IPakbonRepository.ZoekAsync`,
begint met de klant van de factuur, anders de nieuwste 30), klik = zoals Access' `PakbonZoeken.Importeer`: kopregel
"Pakbon: PS-…", per pakbonregel code + omschrijving met het aantal (MDE-artikel: verkoopprijs project, anders 0 en
een melding), daarna een lege regel. De PakbonID wordt niet in `InvoiceHours` bewaard (Access deed dat wel; nergens
gebruikt). Lokaal getest tegen de live database op een nieuwe factuur (Annuleren, niets opgeslagen).

## Ureningave: losse regels kopiëren (4-11-2026)

Wens collega: een regel (of een selectie) naar een andere dag kopiëren. Elke regel heeft een vinkje (kop = alles)
en een knopje ⧉. Zijn er regels aangevinkt, dan wordt "Kopieer dag" "Kopieer 2 regels"; Plak wordt dan
"Plak 2 regels". Een dag of regels, niet allebei; het gekopieerde blijft staan (meer dagen plakken), ook naar een
andere werknemer, zoals Plak dag. `UrenService.PlakRegelsAsync` controleert elke kopie met `ValideerAsync` (Plak dag
doet dat niet, zoals de VBA) en slaat een regel met bv. een gesloten werkorder over met de reden in de melding;
`SourceUrenId`/`GeneratedByRule` gaan niet mee. Gekopieerd worden de regels zoals ze op het scherm staan (geen nieuwe
query). Lokaal getest in demo-modus (JsonDemoRepository, eigen contentRoot): 2 regels, ⧉, alles, Plak dag.
Niet getest: de overgeslagen regel (de demo heeft geen gesloten werkorder).

## EEPROM volgens ETG.2010 (agent 1.25.0, 30-10-2026)

Een collega vergeleek met zijn M-Pede Tool Configurator wat `mde.eeprom` op een WJ1 schreef met een ETG.2010-
referentie (document: `MDE-Automation/M-Pede-tool-configurator`, `docs/eeprom-tool-etg2010-changes.md`, privé
repo). Verschil: 3 bytes. Aangepast in `taken/sii.py` en `_werker_eeprom` (`taken/mde.py`):
- **General, Flags (byte 0x0B)**: bit 0 `StartToSafeopNoSync`, bit 1 `Type/@UseLrdLwr`, **bit 2
  `Mailbox/@DataLinkLayer`** (de WJ1 heeft dat: 0x00 -> 0x04), bit 3 `Info/IdentificationReg134`, bit 4
  `Info/IdentificationAdo` (adres op 0x12). Ook EBusCurrent, ImgIdx, EoE, en `Physics` H = Fast Hot Connect (4).
- **PDO-flags**: 0x0002 PdoDefault voor elke PDO met een `@Sm` (0x0011 -> 0x0013), plus Virtual, DependOnSlot(Group),
  OverwrittenByModule. Alleen PDO's met een `@Sm` komen erin, en elke PDO krijgt een eigen categorie.
- **DC-categorie** (60) per `Dc/OpMode`, `ConfigData2` (woord 0x28-0x2F met eigen CRC), `<Eeprom><Category>`,
  SM-enable met Virtual/OpOnly. Categorieën oplopend op type.
- **Bij herschrijven**: de alias (woord 4) uit het ESI telt als 0; die van de slave blijft, behalve bij een lege
  chip (vendor 0 of 0xFFFFFFFF), een kop met foute checksum of 0xFFFF. Geeft de app geen serienummer (0), dan blijft
  dat van de slave (niet 0xFFFFFFFF). Apparaatspecifieke categorieën (1-9, kalibratie) van de slave gaan mee;
  leverancier-/applicatiespecifieke (>= 0x0800 of bit 15) die het ESI niet heeft geven een waarschuwing in het log.
  De slave wordt gelezen tot de End-markering (`_lees_sii`), niet de hele chip. Na End blijft alles staan (punt 7
  van het document, optioneel, niet gedaan: kost veel schrijfacties).
- **Getoetst**: WJ1-ESI uit de filestore -> byte voor byte gelijk aan de referentiegenerator van de collega tot en
  met End; t.o.v. de oude versie precies 0x111, 0x15C, 0x188. Werker met een nep-EEPROM: lege chip, nog eens
  (niets te schrijven, sn blijft), alias 12 + categorie 3 + 0x0801, alias 0xFFFF, foute CRC, vergelijken. **Niet op
  de print**: daar met de Configurator "Compare with slave" na vullen moet een lege lijst geven.

## Phasing: statisch of dynamisch (Preset) (agent 1.24.0, 29-10-2026)

- `drives.phasing` heeft `methode`: **statisch** (zoals voorheen, `tests.commutation`) of **dynamisch**
  (`tests.dynamic_forced_phasing`, MotionLab's dynamic phasing): Id-stroom in open lus, één omwenteling vooruit en
  één terug op `frequentie` (standaard 0,03 Hz = 33 s/omw), de drive meet met monitoring het verschil tussen
  COMMU_ANGLE_VALUE en COMMU_ANGLE_REF_VALUE. Alleen bij SUCCESS zet de agent zelf COMMU_PHASING_MODE = 2 (No phasing,
  in MotionLab "Preset") en COMMU_ANGLE_OFFSET / COMMU_ANGLE_REF_OFFSET op de gevonden hoek, dan store. ingeniamotion
  zou ook bij WARNING (asymmetrie > 10 %, meestal te snel) toepassen; daarom `apply_changes=False`.
- Eis: absolute encoder als commutatie- én referentiesensor (anders TestError, met uitleg in de melding).
- `stroom` (A) optioneel; leeg = 40 % van MOT_RATED_CURRENT (80 % bij een vertraging), zoals ingeniamotion.
- App: keuze Methode bij de test Phasing, met frequentie en stroom bij Dynamisch. De phasing-knop bij Nulpunt
  (`drives.nulpunt`) is nog alleen statisch.
- Alleen met mocks getest (geen drive in handen).

## Projectteam: rollen per project (28-10-2026)

- **Projecten ▸ tab Team** (`Components/ProjectTeam.razor`): per rol een regel met de teamleden als chips (✕ = weg) en
  een keuzelijst "+ persoon…"; een rol mag meerdere personen hebben. Elke wijziging gaat meteen naar de database.
  Bij de rol "Projectleider" staat de projectleider van de projectkaart (`ProjectResponsible`) als voorstel; niet
  automatisch gekoppeld. Voor iedereen die Projecten mag openen.
- **Tabellen** uit `ProjectTeam_MariaDB.sql`: `ProjectRollen` (Naam, Volgorde, Actief; 8 standaardrollen) en
  `ProjectTeam` (ProjectID, RolID, WerknemerID, uniek per drietal). Zonder script: melding in de tab.
- **Rollen beheren**: Beheer ▸ Tabellen ▸ Projectrollen, via de algemene beheertabel (`BeheerTabellen`, sleutel
  `projectrollen`). `BeheerTabel.SorteerOp` (nieuw) sorteert op een getalkolom. Een rol die in een team staat kan niet
  weg (verwijzing); niet actief = niet meer te kiezen, bestaande leden blijven zichtbaar.
- Lokaal testen zonder te schrijven: `ProjectTeam__Geheugen=true` (`GeheugenProjectTeamRepository`).

## Motorstroom en encoderstatus in de drive-testen (agent 1.23.0, 27-10-2026)

- **Motiontest en dual motion** meten per beweging de motorstroom |Iq| (`get_actual_current_quadrature`,
  CL_CUR_Q_VALUE): max, gemiddeld en RMS (`_ingenia.Stroommeter`). Via SDO in de bewaakloop (±20-40 ms), dus een
  heel korte piek kan ertussen vallen. Optionele grens `maxStroom` (A, invoer "Max. stroom" in de app; leeg =
  alleen meten). Resultaat: `imax/igem/irms` per beweging (dual: met de drive-positie erachter) en `stromen`.
- **Encodertest** bewaakt de foutbuffer en het statuswoord (`_ingenia.Foutbewaking`): elke nieuwe melding van de
  feedbackmodule (0x73xx, bv. 0x7381 "BiSS-C warning bit active in absolute encoder 1") keurt af, ook een
  waarschuwing (encoder niet goed uitgelijnd). Andere meldingen staan alleen in `meldingen`, want de test zet
  zelf een paar foutopties tijdelijk op waarschuwing. Resultaat: `encoderstatus` ok/waarschuwing/fout.
- Alleen met mocks getest (geen drive in handen).

## Projecten ↔ PO klant (27-10-2026)

Wens collega: vanuit een project naar de PO van de klant klikken en terug.
- **Deep links**: `projecten?project=<id>` opent dat project (filter op het projectnummer, `OpenUitUrl` in
  `Projecten.razor`); `financien/po-klant?po=<id>` opent die PO (`PoQuery` in `PoKlanten.razor`).
- **Projecten**: boven de tabs chips "PO klant: PO <nummer> · € bedrag" (✓ = volledig gefactureerd), via
  `POFilter.ProjectId` (`IPOKlantRepository.ZoekAsync`; zonder kolom `Project-id` geen PO's). Alleen voor
  `Ik.MagFinancien`, want PO klant staat onder Financiën en de MainLayout blokkeert `/financien/*` anders toch.
- **PO-venster**: onder het projectveld "Project openen ↗", in een nieuw tabblad zodat de PO open blijft.
- Lokaal getest met PO 148 / project 1929 (heen en terug). Op 27-10 hingen pas 2 PO's aan een project.

## Nieuwe versie aankondigen (27-10-2026)

Wens gebruiker: bij publiceren een melding op ieders scherm, zodat werk afgemaakt kan worden. `Services/OnderhoudMelding.cs`
(singleton, alleen in het geheugen: na de herstart vanzelf weg, anders 10 min na het tijdstip) + `Components/Layout/
OnderhoudBalk.razor` in Main-, Mobiel- en ScanLayout: oranje balk bovenin (sticky) met aftelklok, laatste minuut rood, daarna
"wordt nu bijgewerkt" (rood, pulserend); komt meteen binnen op open schermen (event), geluidje eenmaal (`mdeBerichtGeluid(0,0)`),
niet weg te klikken. Zetten:
- **`service-herstarten.bat`** vraagt vóór het stoppen (alleen als de service draait) "Over hoeveel minuten stopt de app?"
  (Enter = 3, 0 = niet), doet `POST http://127.0.0.1:5010/onderhoud/aankondigen?minuten=n` en wacht met `timeout` (toets =
  nu door). Poort 5010 staat vast in het script.
- `/onderhoud/aankondigen` en `/onderhoud/intrekken` (Program.cs) alleen vanaf de server zelf: loopback en geen
  X-Forwarded-For/X-Original-For (via Caddy dus altijd 403). Antwoord noemt hoeveel schermen open zijn (balken tellen zich).
- **Beheer ▸ Systeem ▸ Nieuwe versie aankondigen** (`/beheer/onderhoud`, Beheer of Directie): minuten + extra tekst,
  Aankondigen / Intrekken.
Lokaal getest: POST gaf "1 scherm(en) open gewaarschuwd", balk meteen zichtbaar met klok, via X-Forwarded-For 403, Intrekken.
Mogelijke verbetering: eerst publiceren naar een aparte map terwijl de app draait en dan pas stoppen en wisselen
(nu valt de hele `dotnet publish` in de stilstand).

## Bestellingen: zoeken in bestelregels (27-10-2026)

Wens gebruiker: zoeken door de regels (items) van alle bestellingen in een groot apart venster. Knop **🔍 In regels…**
naast "+ Nieuwe bestelling" opent `Components/BestelZoekVenster.razor` (`.brz-*`, 110rem × 88vh; eerst `.bz-*`, maar dat voorvoegsel is van Bezoekverslagen: `.bz-regel` = flex-wrap, waardoor elke cel onder de vorige kwam). Zoekvak bovenin (na 400 ms
rust of Enter), eronder een platte tabel: **één regel per treffer** (wens gebruiker: alles op 1 regel) met PO · leverancier ·
status · datum · aantal · artikelnummer · omschrijving · MDE · prijs · totaal · opmerking; vaste kolombreedtes
(`table-layout: fixed`, colgroup), afgekapt met "…" en de volledige tekst in het wolkje. Bij meerdere regels van dezelfde
PO staan PO/leverancier/datum op de vervolgregels grijs (`vervolg`) en de eerste regel krijgt een bovenlijn. `IBestelRepository.ZoekRegelsAsync(zoek,
max, metGeannuleerd)`: Orderitems ⋈ Orders ⋈ Relations ⋈ OrderState, alle woorden moeten voorkomen in Partnumber /
Description / MDE_ID / Remark / leveranciersnaam / orderomschrijving / PO-nummer, `ToBeDeleted` weg, geannuleerde alleen
met het vinkje, nieuwste PO eerst, max 500 regels (`BestelregelTreffer` in Models/Bestelling.cs). Dubbelklik op een regel (of
Enter) sluit het venster en roept `OpenOrder(id)` aan; die zoekt nu met
max 500 omdat LIKE op het nummer ook PO-18123 bij "123" vindt. Buiten het venster klikken sluit het niet (wens gebruiker); ✕ of Esc. Lokaal getest ("coax": 15 regels in 14 bestellingen, alleen lezen).

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

## Broodjes bij het Kaeswienkeltje (24-10-2026)

Wens gebruiker: op vrijdag bestellen we broodjes bij het Kaeswienkeltje (Tholen, 0166 603 018, info@kaeswienkeltje.nl).
Besluiten: iedereen mag voor iedereen bestellen (ook inleen, ook een gast) en de lijst mailen ("verantwoording regelen we
onderling"); MDE betaalt; prijzen in beeld (later misschien beperken); vrijdag tot 11:00, daarna telefonisch; ook
bruikbaar voor een klantbezoek (losse lijst).
- Tabellen `BroodjesAssortiment`, `BroodjesLijsten`, `BroodjesBestellingen` (**`Broodjes_MariaDB.sql`, zelf draaien**),
  `Services/IBroodjesRepository.cs` (Geen / Geheugen via `Broodjes__Geheugen=true` / MySql) met `BroodjesRegels`
  (vrijdag van de week; za/zo = volgende week; sluitingstijd 11:00).
- **Assortiment** (`Services/BroodjesAssortimentService.cs`): hun webshop is WooCommerce, de openbare Store API
  (`/wp-json/wc/store/v1/products?per_page=100&page=N`, header X-WP-TotalPages) geeft naam, prijs (in centen,
  `currency_minor_unit`), categorieën (HTML-entiteiten en zachte afbreekstreepjes), korte omschrijving, voorraad. Alleen
  producten in een lunchcategorie (`Broodjes:Categorieen`, stukjes van namen) komen mee: ca. 139 van de 280; de winkel
  (vleeswaren per ons, kaas, noten, wijn, jam) niet. Groep in de keuzelijst via een vaste volgorde (warm gaat voor koud).
  Vanzelf bijgewerkt bij het openen van /broodjes als het ouder is dan 7 dagen (een tweede scherm wacht op het eerste),
  of met ↻. Wat van de site verdwijnt wordt `Leverbaar = 0`, niet gewist; `Verborgen` is een eigen keuze die blijft
  (nog geen knop voor). Een bestelling bewaart naam en prijs zoals ze toen waren.
- **Pagina `/broodjes`** (menu Werknemers ▸ Lunch): tabs met de vrijdaglijst van deze week (bij openen aangemaakt) en
  losse lijsten van de afgelopen week en later (+ Losse lijst: datum en waarvoor). Links bestellen (voor wie: werknemers
  incl. inleen, of een gast met vrije naam; groepknoppen, zoeken; niet gevonden = als vrije tekst zonder prijs), rechts
  de lijst per persoon (eigen naam geel, "jij"), ✕ met bevestiging als het niet van jou is of al gemaild/gesloten, totaal
  per product en in euro. Na vrijdag 11:00: oranje balk met het telefoonnummer en een vinkje "Ik heb gebeld; toch op de
  lijst zetten".
- **Mailen**: tekst gemaakt (totaal per product, per persoon met opmerkingen), aanpasbaar; Aan = `Broodjes:Mail`
  (standaard info@kaeswienkeltje.nl); met de eigen Gmail-handtekening (zoals de factuurmail); `GemaildOp/Door` en een
  waarschuwing bij nog eens mailen. Zonder Gmail-toegang: melding + "Tekst kopiëren".
- **Bovenbalk** (`Components/Layout/BroodjeKnop.razor`, links van het lampje): 🥖 op donderdag en vrijdag. Op vrijdag
  vóór 11:00 bij het openen van de app een wolkje "Het is vrijdag!" (stokbroodje wiebelt) als je nog niets op de
  vrijdaglijst hebt (`HeeftBesteldAsync`); "Niet nodig", "Broodje bestellen" of naar /broodjes gaan onthoudt de
  browser voor die dag (`broodje-herinnering-<wn>-<yyyyMMdd>`).
- Lokaal getest met de geheugen-opslag op een zaterdag: assortiment echt van de site, gast bestellen, vrije tekst, losse
  lijst, mailvenster (niet verstuurd). **Niet getest**: het stokbroodje en de vrijdagherinnering (alleen do/vr en met
  inloggen), de gesloten-stand na 11:00, echt mailen, de MySql-opslag.

## Verbeterverzoeken: lampje in de bovenbalk (22-10-2026)

Wens gebruiker: collega's dienen ideeën in via een knop in de bovenbalk; iedereen ziet de lijst, Directie wijzigt en zet
de status. Tabel `Verbeterverzoeken` (**`Verbeterverzoeken_MariaDB.sql`, zelf draaien**), `Services/IVerbeterRepository`
(Geen / Geheugen via `Verbeter__Geheugen=true` / MySql). `Components/Layout/IdeeLampje` (💡, naast de bel): tekstvak,
de huidige pagina gaat mee, indiener = `Ik.WerknemerId` (op een werkplek de gekozen medewerker, plus de werkplek-
naam). Directie ziet het aantal 'nieuw' op het lampje (hoogstens elke 30 s geteld, bij navigeren). Pagina
`/verbeterverzoeken` (Beheer > Verbeteren): filter open/gedaan/afgewezen/mijn/alles en zoeken; Directie wijzigt tekst,
status (nieuw, gepland, bezig, gedaan, afgewezen) en reactie in de tabel, meteen opgeslagen (ook in `Wijzig` gecontroleerd).
Niet verwijderen: afwijzen. Lokaal getest met de geheugen-opslag (indienen, lijst, status wijzigen).
**Schermafdrukken** (22-10-2026, verzoek collega): in het idee-venster Ctrl+V (na Win+Shift+S) of "kies een bestand",
hoogstens 3, 10 MB, voorbeeld met ✕. `mdeKlembord.koppelPlakken` hangt aan het hele venster (tekst plakken in het vak
blijft gewoon werken: `GeenAfbeelding` doet niets). Pas na indienen naar de filestore (`FilestoreService.ToevoegenAsync`,
**StoreType 340**, MasterID = VerzoekID, naam "schermafdruk <id>-<n>.png"). Lijst: plaatjes via `bestand/<id>`
(`Verbeterverzoek.Schermafdrukken`, GROUP_CONCAT in `LijstAsync`). Plakken lokaal getest; versturen niet (echte filestore).
**Fout na uitrol** (verzoek 20): FileStore.StoreType heeft een FK naar `FileStoreType`; 340 stond er niet in, dus elke
schermafdruk werd geweigerd. `Verbeterverzoeken_Schermafdruk_MariaDB.sql` (INSERT IGNORE 340) moet gedraaid worden; ook
in `Verbeterverzoeken_MariaDB.sql` opgenomen. **Let op bij een nieuwe StoreType: altijd ook in FileStoreType zetten.**
De melding noemt nu de echte reden en logt de fout.
Bericht aan de indiener (zelfde dag, op verzoek): bij een nieuwe status of reactie maakt `BerichtAanIndiener` een
melding (tabel Meldingen, soort `verbeter`, OrderID = verzoek, max. 250 tekens) en geeft `MeldingSignaal`; de bel toont
💡 met een link `verbeterverzoeken?verzoek=<id>` (dat verzoek uitgelicht, filter "alles"). Niet aan jezelf, niet bij een
tekstcorrectie. Niet getest met echte meldingen (zou in de database schrijven).

**Producten > Techniek alleen Directie** (zelfde dag): de drie Techniek-blokken staan in een `fieldset disabled`
(`MagTechniek`), en `ZetTechniek`, `ZetStapZichtbaar`, `ZetMetParameters`, posities en parametersets controleren het
ook zelf. Uitzetten van een stap of techniek vraagt bevestiging (`BevestigUit`; Annuleren bouwt de vinkjes opnieuw op
met `_techniekVersie`).

## Melding: oude MDE Agent op deze pc (21-10-2026)

`Services/AgentUitgave` (singleton) leest de hoogste `MdeAgent-setup-<versie>.exe` uit `Productie:AgentMap` (standaard
`\\172.16.1.4\data\software\MDE Apps\MDE Agent\uitvoer`, waar `publiceer-z.bat` hem neerzet; submap `oud` telt niet),
10 min onthouden. `Components/AgentVersieMelding` (bovenin `ExemplaarWerk`, dus Test PCB en Producten > Programmeren)
toont een oranje melding als de versie uit `/health` lager is. Share onbereikbaar = geen melding. Het serviceaccount van
de app moet die share kunnen lezen (zelfde share als de filestore). Dus: eerst `publiceer-z.bat`, dan ziet iedereen het.

## Taak blijft 'bezig' bij de agent (agent 1.20.2, 21-10-2026)

Melding collega (Swivels testen): de app zegt dat er nog een taak loopt, alleen de service herstarten hielp. Twee oorzaken:
- De app verliest de taak (pagina vernieuwd, venster dicht, recept met `bediening` wacht op Klaar): de agent doet er één
  tegelijk en weigert de volgende met 409 "Er loopt al een taak (naam, id)". Nu vraagt `VoerTaakUit` dan of die taak
  gestopt mag worden (`StopAchtergeblevenTaak`: POST /jobs/<id>/stop, tot 15 s wachten) en start daarna opnieuw.
- Een stil vastgelopen werkproces: `coe._draai` keek alleen naar de time-out als er een regel binnenkwam. Nu bewaakt de
  draad `doorgeven` time-out en Stop los van de uitvoer (scratchpad `draaitoets.py`: time-out 2 s, Stop coe 1 s, Stop mde 9 s).
  Werkprocessen sluiten na het resultaat af met `os._exit` (`__main__`): een draad die op stdin wacht (mde, ecat.io)
  kan het normale afsluiten van Python laten hangen.

## Node-adressen schrijven veilig gemaakt (agent 1.20.1, 21-10-2026)

Incident: twee robots met een Everest (drive B) die daarna onbereikbaar was voor de app, MotionLab en firmware laden
(drive vervangen). Historie product 83, 21-10 11:34: "node-adressen schrijven: MISLUKT — positie 1: EEPROM write error;
positie 2: EEPROM leest 0 terug in plaats van 31; positie 4: EEPROM leest 128 terug in plaats van 33". Oorzaak in
`ecat_alias._schrijf_alias`: alias (woord 4) schrijven, 50 ms, **checksum (woord 7) schrijven**, en pas dan controleren.
Kwam het alias er verkeerd in, dan paste de checksum niet meer bij de kop; na spanning uit/aan negeert de ESC de
EEPROM-kop (PDI niet actief), dus geen mailbox: MotionLab en FoE zien de drive niet. Vermoedelijke aanleiding van het
verkeerd schrijven: een tweede master tegelijk (MotionLab open) of een te korte wachttijd.
Nu: wachten op EEPROM-bit "bezig" (0x0502 bit 15), alias teruglezen; klopt het niet, dan het oude alias terugzetten en de
checksum niet aanraken; pas daarna de checksum, teruglezen, en de hele kop nog eens controleren. Melding "NIET de spanning
eraf halen" als terugzetten of checksum mislukt. De bevestiging in de app vraagt MotionLab/TwinCAT eerst te sluiten.
Getest met een nagebootste EEPROM (scratchpad `aliastoets.py`), niet op een echte drive.
Herstel van een drive met alleen een kapotte checksum: los aansluiten, Node-adressen lezen (checksum ok = false), het
gewenste nummer opnieuw schrijven, spanning uit/aan. Werkt alleen als de ESC de EEPROM zelf beheert (geen emulatie).
**22-10-2026: agent 1.20.2 uitgerold; beide Everests op deze manier hersteld en weer bereikbaar (gebruiker).**

## Controllertypen: tabs, receptenlijst met editor (21-10-2026)

- Rechts twee tabs: **Firmware** en **Testrecepten** (alleen bij SDO-slaves). Uitleg staat achter een **?**-knop
  (popup; bouwstenen als tabel in `DriveTypen.Bouwstenen`, bij een nieuwe bouwsteen daar ook bijwerken).
- Recepten: links een lijst, rechts een editor op een concept (`ReceptConcept`); pas **Opslaan** schrijft naar de database
  (alleen als de JSON klopt, `DriveRecept.Controleer` live onder het vak). Niet-opgeslagen wijzigingen: vraag bij wisselen van
  recept, tab of type. "+ Recept"/"+ Voorbeeld" maken het recept meteen aan en kiezen het.
- **Bouwsteen `autostart`** (`{"stap": "autostart", "seconden": 3}`, alleen op het hoogste niveau): Test PCB start het
  recept zodra het testvenster opent, na een aftelling (max. 60 s) met Annuleren (`ExemplaarWerk.Autostart`). Het is
  een instelling, geen stap: `DriveRecept.StappenVoorAgent` haalt hem eruit voor de agent, `StapNamen` en `Bediening(nr)`
  tellen zonder, dus de nummering klopt met "Stap n/m" van de agent. Geen nieuwe agent nodig.
- Link: `productie/drivetypen?type=<id>&tab=recepten&recept=<id>`. In Producten ▸ Techniek ▸ Posities staat voor
  **Directie** achter het controllertype "✎ bewerken" (`MagControllertypen`, `ControllertypeUrl`).

## Testrecepten: venster, grenzen en bediening/HMI (agent 1.20.0, 21-10-2026)

- **Recept-venster** (Test PCB): de uitvoer staat in het testvenster zelf (`VoerTaakUit` telt `mde.test` nu als test;
  daarvoor ging het log naar het scherm erachter). Groot venster (`prod-test-recept`), bovenin Start/Stop/stand, links de
  stappen met een balk op de lopende stap en ✓/✗ + tekst per stap, rechts een vast log (column-reverse = loopt onderaan
  mee). De stap komt uit de logregels "Stap n/m:" (`ReceptVerloop`; "Stap 2.1/3" binnen herhaal telt niet).
- **Grenzen**: een monitor-object mag `min`/`max` hebben (`DriveRecept.Grenzen`, `GrenzenUit`; ook bij Meekijken). De
  grafiek tekent ze als stippellijn in de kleur van de lijn en neemt ze mee in de schaal; een waarde erbuiten wordt rood.
- **Bouwsteen `bediening`** (HMI): `knoppen` (vaste waarde), `schakelaars` (aan/uit, standaard 1/0, type BOOL) en
  `invoer` (getal met min/max), `seconden` (standaard 3600, max 4 uur). De werker wacht op opdrachten via stdin
  (`{"knop": n}`, `{"schakelaar": n, "aan": b}`, `{"invoer": n, "waarde": w}`, `{"klaar": true}`) en schrijft **alleen
  wat in die stap staat**, met de grenzen (`_stap_bediening`); via de procesdata als het object gemapt is. De live-
  tussenstand krijgt `geschreven` (laatste waarde per element) voor de stand van schakelaars. De app toont het paneel
  zolang de lopende stap een bediening is (`DriveRecept.Bediening(nr)`, alleen het hoogste niveau) en stuurt via
  `POST /jobs/<id>/invoer` (`Bedien`). Een recept met bediening krijgt in de app 4 uur + 15 min volgtijd.
- **Stop netjes**: `coe._draai` geeft invoer en Stop door via stdin (draad `doorgeven`); de mde-werker stopt zelf
  (`_STOP`, `_Gestopt`, uitgangen 0 via `_Procesdata.stop`) en geeft een resultaat met `gestopt`; andere werkers worden
  bij Stop meteen afgebroken, mde na 8 s ook. `mde._duur` rekent de time-out (herhaal × blok, bediening).
- Getest tegen de nagebootste Swivel (scratchpad `nep_bediening.py`: knoppen, schakelaar, invoer binnen/buiten grens,
  onbekende knop, Klaar, Stop); app gebouwd, niet in de browser of op de testrig getest.

## MDE-slaves: versie zonder "v", serienummer in 0x1018:4 (agent 1.19.1, 21-10-2026)

- De Swivel-firmware meldt in 0x100A "v1.0.5"; `coe_versie_gelijk` vond dat niet gelijk aan "1.0.5" ("Laden lijkt niet
  gelukt", product 538). Nu `mde.kale_versie`: identiteit, firmware laden en de vergelijking gebruiken de versie zonder
  "v". Oude attributen met "v1.0.5" (bv. product 596) zijn niet aangepast.
- Serienummer: `mde.eeprom`/`mde.serienummer` zetten het in de SII (EEPROM-woord 0x0E/0x0F). 0x1018:4 is volgens het
  ESI read-only (standaard 0) en wordt door de firmware gevuld; die moet bij het opstarten SII-woord 0x0E/0x0F lezen
  (EEPROM-interface van de ESC, registers 0x0502-0x050F, nadat de master de EEPROM aan de PDI gaf) en in 0x1018:4
  zetten. Dat is werk in de firmware, niet in de agent.
- ESI v1.0.7 heeft productcode 0x0000DA93 (eerder 0x00055955).

## MDE-recepten in OP met procesdata (agent 1.19.0, 20-10-2026)

Aanleiding: Swivel (MPR-PCBA-WJ1), recept "Vacuüm en blaas": schrijven op 0x7020:1/2 lukte, maar de kleppen
schakelden niet (druk bleef 100, max 50). Oorzaak: `mde.test` draaide in PRE-OP met SDO's; 0x7020 (kleppen) en
0x6020 (druk) zijn PDO-objecten en de firmware neemt uitgangen alleen uit de procesdata in OP. Nu
(`mde._Procesdata`, `_PdoDrive`): PDO-toewijzing lezen (0x1C12/0x1C13, `ecat_io._mapping`), `config_map`, SAFE-OP,
een draad met send/receive elke 2 ms (timeBeginPeriod 1), dan OP. Lees/schrijf op een gemapt object gaat via het
in-/uitgangsbeeld (op bitniveau, BOOL = 1 bit), de rest blijft SDO. Na afloop uitgangen 0, 20 cycli, terug naar
PRE-OP. Lukt OP niet (of `"procesdata": false` in de taakparameters), dan zoals vroeger alles via SDO, met een
regel in het log. Alle slaves op de bus gaan mee naar OP (met nullen). Getest met een nagebootste pysoem
(scratchpad nep_swivel.py) en op de testopstelling: kleppen schakelen (20-10-2026). Let op: of pysoem tijdens een trage SDO de GIL loslaat (anders
hapert de cyclus en valt de slave op de watchdog terug naar SAFE-OP; het log noemt de langste cyclus als die
boven 50 ms komt).

## Test PCB: waarschuwing firmware ↔ parameterset (20-10-2026)

Aanleiding: product 246 (MPR-PCBA-RZ), drives met firmware 2.9.0, parametersets v1.0.1 gemaakt voor 2.11.0 →
"276 registers, 17 afwijkingen" en een stroomlus zonder meetdata. `ExemplaarWerk.LaadParameters` vraagt nu eerst
bevestiging als `HuidigeFirmware(p)` (laatste uitlezing, anders attribuut drive:n:firmware, anders de agent) volgens
`VersieGelijk` afwijkt van de firmware van de set ("2.9.0.000" ≠ "2.11.0"; "2.11.0.000" = "2.11.0"). Onbekende
firmware = geen vraag. Geldt voor Ingenia en CoE. Niet in de app getest (zou een echte taak starten).

## Producten: verwijzingen in de historie (20-10-2026)

"Ingebouwd in product #322" / "Uit product #322 gehaald" (geschreven door `SamenstellingKiezer`) worden bij het tonen
"product MPR-1 · 57 (#322)", klikbaar (opent dat exemplaar, `OpenExemplaar` = dezelfde route als `?product=`).
`Producten.razor`: `HistorieTekst` (RenderFragment, regex `product #nr`), namen via `GetLabelGegevensAsync` in
`VulVerwijzingen` (vanuit OnAfterRenderAsync, gecached in `_verwezen`; serienummer zonder de voorloopnullen van het
label). De opgeslagen tekst blijft ongewijzigd, dus ook oude regels worden duidelijker. Historietabel en blok
"Laatste historie". Getest op exemplaar 370 → MPR-1 · 57.

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

## Recht MagFirmware (22-10-2026)

Firmwareversies toevoegen en wijzigen (Controllertypen > tab Firmware: versie, status, bestanden, vrijgegeven, opmerking,
verwijderen) alleen met de kolom **`Werknemers.MagFirmware`** (`Werknemers_MagFirmware_MariaDB.sql`, zelf draaien; per
werknemer in SQL zetten, besluit gebruiker). Zonder recht: tabel in een `fieldset disabled` (downloadlinks blijven werken)
en een uitleg; de handlers controleren `MagFirmware` ook zelf. Directie krijgt het niet vanzelf. Testrecepten vallen er
niet onder. Lokaal bekeken zonder recht (alles uit, 4 downloadlinks werken).
Ook aan te vinken op het werknemersscherm, tab Rechten ("Mag firmware beheren", `WerknemerVelden`; die tab ziet alleen HRM).
**+ Firmwareversie** (DriveTypen `NieuweFirmware`): de lijst sorteert op versie (nieuwste boven), dus de nieuwe regel
"(versie)" sprong naar boven terwijl de knop onder de tabel staat. Nu blijven de bestaande regels staan en komt de nieuwe
onderaan (`_nieuweFirmware`, geel), met een leeg versieveld en de cursor erin; opnieuw openen sorteert weer op versie.

## Uitgaande factuur: Gmail-handtekening onder de mail (24-10-2026)

Wens collega ("vroeger deed Outlook dat"). Versturen via de Gmail-API zet de Gmail-handtekening er niet onder, dus
`MailService.HandtekeningAsync` leest hem (`settings/sendAs/<adres>` → `signature`, HTML; scope gmail.readonly/modify
volstaat) en `MailService.MetHandtekening` maakt van de platte tekst HTML met de handtekening eronder (dan `html: true`).
`FactuurVersturenVenster`: onder Tekst een vinkje "Mijn Gmail-handtekening eronder" (standaard aan) met voorbeeld; zonder
handtekening in Gmail de uitleg waar je hem instelt. Geen veld in Werknemers nodig (besluit: één plek, Gmail). Niet lokaal
te testen (geen Google-login).
**Ook bij bestellingen en offertes** (zelfde dag, op verzoek): `BestelMailVenster` (offerteaanvraag/bestelling aan de
leverancier) heeft een vinkje Ondertekening "Gmail-handtekening (in plaats van naam en logo)", standaard aan als er een
is; `BestelMail.Html(..., handtekening)` zet hem na "Met vriendelijke groet," in plaats van naam/gegevens/logo (de grijze
adresregel blijft) en het logo gaat dan niet als ingebed plaatje mee. Op een werkplek de handtekening van de alias
(`HandtekeningAsync` vraagt `sendAs/<Afzender>`, "Naam <adres>" wordt het adres). `OfferteVersturenVenster`: vinkje +
voorbeeld zoals de factuur, het concept wordt dan HTML (`MaakConceptAsync(..., html:)`); Gmail zet zelf geen handtekening
onder een concept dat via de API klaarstaat. De handtekening wordt op de achtergrond opgehaald (venster opent meteen).
Niet lokaal getest (geen Google-login).
Link naar de plek in Gmail: `https://mail.google.com/mail/?authuser=<adres>#settings/general` (Algemeen ▸ Handtekening;
authuser opent het goede account), plus ↻ om na het opslaan in Gmail opnieuw op te halen.

## Programmeren: firmwareregel bijgewerkt na "Firmware laden" (24-10-2026)

Wens gebruiker (Everest vervangen: 2.09.0, na de update stond er nog steeds 2.09.0 tot opnieuw uitlezen). De
controletabel bovenaan Programmeren (Node / Type / Firmware / Parameterset, `_controles`) werd alleen bij Automatisch
uitlezen gevuld. Nu `ExemplaarWerk.FirmwareBijgewerkt(positie, versie)` na `drives.firmware`/`mde.firmware` (de door de
agent teruggelezen versie) en na `coe.parameters` (teruggelezen firmware): de regel krijgt opnieuw status en parameterset
via `VulFirmware` (uit `ControleerDrives` gehaald; `DriveControle.TypeId` onthoudt het herkende type), en een nog open
uitlezing (`_drives`) krijgt de nieuwe versie, zodat de waarschuwing "andere firmware" bij Parameters laden niet meer op
de oude versie afgaat. Niet getest met een echte drive (schrijft attribuut en historie).

## Kostprijstarieven: melding bovenin en laatst bekend tarief in de nacalculatie (24-10-2026)

Verbeterverzoek. **Melding** `Components/TariefBalk.razor` (in MainLayout onder de Vault-balk, oranje, alleen Financial en
Directie): werknemers die de laatste 30 dagen uren schreven maar voor vandaag geen periode in `WerknemersTarieven` hebben
(`INacalculatieRepository.ZonderTariefVandaagAsync`, met de einddatum van hun laatste tarief); één opvraging na de eerste
weergave, ✕ = voor vandaag gezien (`mdeOpslag` "tarief-balk-yyyyMMdd"). Tarieven worden in Access bijgehouden, niet in
de app. Op 24-10-2026: Marc de Kock (laatste tarief tot 31-12-2025).
**Nacalculatie**: uren zonder tarief voor die datum (de view `_Nacalc_Uurkosten_Overview` laat ze weg) worden in
`LaadAsync` per dag opgehaald en gerekend met het **laatst bekende tarief** van de werknemer (periode met de laatste
begindatum op of vóór die dag; vóór alle periodes: de eerste), zelfde regels als de view; `NacalcUren.LaatstBekend`,
in de tab Uren "≈" voor de kostprijs, voetnoot in Totaal. Daarmee tellen ook de uren precies op een begindatum mee (de
view eist `> Begindatum`). Alleen werknemers zonder enig tarief blijven "niet meegeteld". Werkt door in de voortgang
(Bepaal zelf) en de AI-opvraging. Getest met 225086-02 (Marc 37,5 uur à 65,00; rechten tijdelijk opengezet en
teruggezet).
**Factureren** (zelfde dag, op verzoek): `Tariefregels.Reken` (uren importeren op een factuur en de pagina Tarieven)
gebruikt zonder kostprijstarief voor die datum ook het laatst bekende tarief (afwijking van Access, waar de regel dan
geen bedrag kreeg, ook niet het verkoopbedrag), met de melding "geen kostprijs voor deze datum voor X: laatst bekende
kostprijs gebruikt". Alleen zonder enig tarief blijft het "geen kostprijs bekend". Getoetst met een los programmaatje
(binnen periode, na de laatste, vóór de eerste, geen tarief); niet in het importvenster bekeken.

## Activity board wissen (24-10-2026)

Wens gebruiker: een board kunnen wissen, alleen Directie. Knop **Board wissen…** in de kop van het board (naast Naar
Excel), voor iedereen zichtbaar maar alleen voor Directie te gebruiken (zoals bij todo hernoemen). Bevestiging in de pagina
met het aantal onderwerpen, types en activiteiten ("kan niet ongedaan worden gemaakt"; bij een project-/werkorder-/
klantboard: opnieuw openen begint leeg). `IActivityBoardRepository.VerwijderBoardAsync` = alleen `DELETE FROM
ActivityBoards`; de rest gaat mee via ON DELETE CASCADE (op de echte database gecontroleerd: alle 8 FK's CASCADE).
Bijlagen van onderwerpen (filestore 342) blijven staan, net als bij het wissen van één onderwerp. Open schermen van
collega's krijgen via de live-melding "Dit board is verwijderd." Lokaal getest met `ActivityBoard__Geheugen=true`.

## Subwerkorders in nacalculatie, uren importeren en tarieven (24-10-2026)

Gevolg van de werkorderboom (`Workorders.ParentWerkbonID`, altijd hetzelfde project): wat per project optelt klopte al,
maar per werkorder niet. Gedeelde hulp `Services/WerkorderBoom.cs` (Ordenen diepte-eerst op nummer, Nakomelingen,
MetNakomelingen, Voorouders; bestand tegen kringen). Zonder de kolom blijft alles plat (kolomcheck, alleen 'ja' onthouden).
- **Nacalculatie**: `NacalcWerkorder.OuderId` (LEFT JOIN Workorders op de view). Een gekozen werkorder (ook via
  `?werkorder=`) neemt zijn subwerkorders mee; links ingesprongen, "telt mee via ouder" grijs aangevinkt (`nc-via`). In
  het kostenoverzicht boomvolgorde met ↳ en na de laatste nakomeling een regel **Subtotaal <nummer> incl. n
  subwerkorders** (`SubtotaalNa`, diepste eerst); het eindtotaal blijft de som van de eigen kosten (niets dubbel).
  Lokaal getest met 224101-12 (17 en 18 eronder), rechten tijdelijk opengezet en teruggezet.
- **Uren importeren** (factuur) en **Tarieven**: `WerkorderKeuzeUren.OuderId`, lijst in boomvolgorde en ingesprongen; een
  ouder aan- of uitvinken doet de subwerkorders mee.
- **Teamtarief** (`RekenAsync`): heeft een werknemer op de subwerkorder geen teamtarief (geen regel of alles 0), dan dat
  van de dichtstbijzijnde bovenliggende werkorder, met de melding "subwerkorder zonder eigen teamtarief: tarief van
  <nummer> gebruikt". Doorbelast deel en "materiaalkosten niet meenemen" blijven per werkorder (overal standaard).
  Niet in de browser getest (factuur/rechten); de opvraging van de ouders is los op de database gecontroleerd.

## Producten: filter op werkorder (24-10-2026)

Wens gebruiker. Onder producttype/groep een keuzelijst "Alle werkorders" met alleen werkorders waar exemplaren op staan
(`IProductRepository.ExemplarenPerWerkorderAsync`: per WorkorderID de ProductNameIDs met aantal, niet gewist; labels via
het nieuwe `IUrenRepository.GetWorkordersAsync(ids)`, open en gesloten, nieuwste boven, met het aantal erachter). Gekozen:
de typelijst toont alleen types met exemplaren op die werkorder (`NamenGefilterd`), de exemplarenlijst alleen die
exemplaren (`ProductRegel.WerkorderId`, `OpWerkorder` in `StatusZichtbaar` en de tellingen). Het statusfilter blijft
gelden; vallen alle exemplaren van de werkorder erbuiten (vaak: alles "Product gereed"), dan een melding met de knop
"Alle statussen tonen". Scannen of openen via een link buiten het filter zet het werkorderfilter los. De lijst wordt bij
het openen van de pagina opgehaald en na de groepsbewerking Werkorder opnieuw. Lokaal getest (lezen).
Zelfde dag: het **typeplaatje** heeft een eigen tab "Typeplaatje" (na Samenstelling, alleen bij een exemplaar; stond
onder Algemeen) en **Bouwjaar** staat links uitgelijnd (`ProductVelden`: `Links = true`).

## Producten: exemplarenlijst duidelijk en actueel (24-10-2026)

Verbeterverzoek 36 (collega): "Als er geen product is geselecteerd laat hij wel de lijst van mpedes zien … statussen
niet up to date". Oorzaak: het gekozen type stond weggescrold in de typelijst (leek niets gekozen), en de exemplarenlijst
werd alleen bij het kiezen geladen. Nu: kop "Exemplaren van <type>"; het gekozen type scrolt in beeld (`_naarType`,
`mdeInBeeld` in App.razor); `VerversLijst` (alleen de lijst, het geopende exemplaar krijgt zijn verse regel) via ↻ naast
het statusfilter en elke minuut (`_verversKlok`, Producten is nu IDisposable). Lokaal bekeken met ?product=410.

## Activity board: werkorder, "Onderwerp" in plaats van "Probleem", bijlagen (24-10-2026)

Wensen gebruiker. **Script `ActivityBoard_Werkorder_Bijlagen_MariaDB.sql` zelf draaien** (kolom `ActivityBoards.WerkorderID`
en FileStoreType 342).
- **Board bij een werkorder**: vierde soort naast project, klant en vrij (`ActBoardSoort.Werkorder`, gaat voor project
  in `Van`). Eén board per werkorder; een werkorderboard heeft geen ProjectID. `ZorgVoorWerkorderBoardAsync`,
  `KoppelBoardAsync(…, werkorderId)`, `WerkorderDeelAsync` (nummer = projectnummer-werkbonnummer, omschrijving).
  Zoeken via `IUrenRepository.ZoekWerkordersAsync` (ook gesloten). Knop Werkorder alleen als de kolom er is.
- **"Probleem" heet op het scherm "Onderwerp"** (ook voor verbeterpunten en andere todo's). Alleen zichtbare teksten;
  database, code en de sleutel `"problemen"` (ZetKoppelingenAsync) zijn ongewijzigd.
- **Bijlagen bij een onderwerp**: 📎 in de regel (aantal), uitklapregel `li.ab-prob-bijlagen` (geen `ab-prob`, dus het
  slepen in activityboard.js slaat hem over) met miniaturen/bestandslinks, × (mdeBevestig), bestand kiezen of Ctrl+V
  (mdeKlembord.koppelPlakken; `AfbeeldingGeplakt` in de pagina). Filestore StoreType 342, MasterID = ProbleemID, meteen
  opgeslagen. Raster van `.ab-prob` heeft er een kolom bij.
Lokaal getest met `ActivityBoard__Geheugen=true`: werkorders zoeken (echte database, alleen lezen), werkorderboard
openen, onderwerp toevoegen, bijlagenvak; uploaden niet getest (zou in de echte filestore schrijven).
- **Boom op het startscherm** (wens gebruiker, meer overzicht bij een grote lijst): Boom / Lijst (onthouden per pc,
  `mdeOpslag` "ab-weergave", standaard Boom). Klant ▸ Project ▸ Werkorder ▸ subwerkorder (`Workorders.ParentWerkbonID`,
  één niveau), daarnaast "Zonder klant" en "Vrije boards". `GetBoardsAsync` geeft per board `KlantId/KlantNaam` (klantboard:
  zelf; projectboard: Projects.Customer-id; werkorderboard: klant van zijn project), `HoofdProject*` en `OuderWerkorder*`.
  Tussenniveaus zonder eigen board zijn groeperingen (grijs, met aantal boards en open). Klanten standaard ingeklapt;
  Alles uit-/inklappen; zoekvak filtert en klapt de treffers open; het soortfilter werkt ook in de boom. Valkuilen:
  padding met `InvariantCulture` (anders "1,55rem"), en de klasse `groep` bestaat al globaal (dus `ab-boomgroep`).
  Getest in het geheugen (met nep-hiërarchie in `GeheugenActivityBoardRepository`); de klantjoin tegen de echte boards
  gecontroleerd (225086 → Andela, 225092 → Sojitz).

## PO van klant: PDF erin slepen en met AI uitlezen (24-10-2026)

Wens gebruiker (en verbeterverzoek "uitlezen van de PO voor naam klant en bedragen"). In `POKlantVenster` is het
documentenvak een **insleepvak** (`.po-insleep`: label met een onzichtbare `InputFile` erover, dus slepen of klikken).
De eerste PDF/afbeelding wordt meteen gelezen door `FactuurLezer.LeesPoAsync` (eigen opdracht `PoOpdracht`; zelfde
wegen als facturen via `LeesRuwAsync`: tekstlaag → tekst naar het model, scan → beeld, Bedrock of Ollama). ✨ bij een
document (opgeslagen of nog in de wachtrij) doet het opnieuw. `Vul` zet alleen **lege** velden (klant: precies één
treffer of exact gelijk, anders de zoeklijst met de naam zonder rechtsvorm; contactpersoon op naam; PO-nummer; datum
ook als bij een nieuwe PO nog "vandaag" staat; bedrag ex btw; omschrijving; termijnen als er nog geen zijn) en meldt
verschillen met wat er al staat, plus de opmerkingen van het model. Nooit vanzelf opslaan.
Getest (rechten tijdelijk open, teruggezet, niets opgeslagen) met PO 148 (TDC/Tembo): bestaande PO = verschillen klant en
datum gemeld; nieuwe PO met dezelfde PDF via het bestandsveld = PO-nummer, datum, bedrag en omschrijving ingevuld, klant
"Technical Development Corporation" niet bij de relaties → zoeklijst.

## Projecten: tab Kostenbegroting, voortgang tegen begrote kosten (24-10-2026)

Wens gebruiker: het budget (tab Budget) is het **verkoop**budget; de voortgang moet gemaakte kosten tegen **begrote
kosten** afzetten. Nieuwe tab **Kostenbegroting** (zelfde rechten als de budgettabs), `Components/ProjectKostenbegroting.razor`:
- Regels per kostensoort (Uren / Materiaal / Inkoop / Overig, `KostenSoort`) in tabel `ProjectKostenbegroting`
  (**`ProjectKostenbegroting_MariaDB.sql`, zelf draaien**; zonder: een uitleg). `IProjectBudgetRepository.KostenRegelsAsync`
  (null = geen tabel), `BewaarKostenRegelAsync`, `VerwijderKostenRegelAsync`.
- **Begroot tegenover gemaakt** per soort: gemaakt = `NacalcData` (Uurkosten, Materiaalkosten, Inkoop) van alle
  werkorders; "Overig" kent de nacalculatie niet. Nog over en % verbruikt; uren zonder kostprijstarief genoemd.
- **Marge**: verkoopbudget (som BudgetProject) min begrote kosten.
- **Voortgang** (`Projects.Voortgang*`) verhuisd van de tab Budget naar hier; "Bepaal zelf" = gemaakt / begroot (bron
  "bepaald: gemaakt … van begroot …"). Eerder gezette waarden (tegen het verkoopbudget) blijven staan tot opnieuw bepaald.
Lokaal bekeken met 218183 (nep-begroting in het scherm, rechten tijdelijk open; teruggezet): gemaakt uren 54.930,22,
inkoop 100.845,57, totaal 155.775,79 = zelfde als de nacalculatie.

## Todo-overzicht: een punt overal hernoemen (24-10-2026)

Verzoek collega (Tim Franke). Producten ▸ Todo-overzicht: knop **✎ Punt hernoemen** staat er altijd, klikbaar als je een
punt (Wat) gekozen hebt én projectleider bent van een open project (`IProjectRepository.IsProjectleiderAsync`:
Projects.ProjectResponsible, ProjectStatus 1) of Directie (besluit gebruiker); anders uitgeschakeld met uitleg in de
tooltip. Eerst `WatTellingAsync` (exemplaren, open, gereed) in de bevestiging; bestaat de nieuwe naam al, dan staat erbij
dat ze samengevoegd worden. `HernoemWatAsync` = UPDATE op `Wat`, open én gereed. Getest met `ProductTodo__Geheugen=true`
(knop uit zonder recht; met recht tijdelijk open: samenvoegen naar een bestaande naam); query's gecontroleerd. Op 24-10
waren 18 werknemers projectleider van een open project.
**Gereed melden vraagt bevestiging** (wens gebruiker 24-10-2026), in het todo-overzicht én op de tab Todo van een
exemplaar (`ProductTodoTab`); weer open zetten niet. Bij Annuleren springt het vinkje terug via `_herteken` (@key op het
vinkje: Blazor zet een checkbox anders niet terug, want het model is niet veranderd). Getest in het overzicht (geheugen).

## Scannen: AIM-voorvoegsel van de barcode eraf (24-10-2026)

Melding: bestellen met de telefoon gaf "']C1MDE56262' is geen artikelnummer". `]C1` is de AIM-code van het barcodesoort
(Code 128 als GS1-128, FNC1 voorop) die de lezer meegeeft; geen deel van het nummer. `scan.js` (`meld`, v=10) en
`Scannen.ZoekCode` halen nu `^\][A-Za-z][0-9A-Za-z]` en stuurtekens (FNC1/GS = \x1D) eraf, daarna gaat MDE eraf zoals
voorheen. Getest op de regels (JS en .NET): "]C1MDE56262" -> 56262.

## Videobellen op het intranet (22-10-2026)

Wens gebruiker: camera en microfoon rechtstreeks naar een collega, zonder externe dienst. **WebRTC** tussen de browsers
(`wwwroot/videobellen.js`, `mdeVideo`), zonder STUN/TURN: werkt binnen het eigen netwerk, niet van buiten. Camera en
microfoon alleen op **https** (https://everything.mde-automation.nl), niet via http://172.16.1.13:5010 (de app zegt dat).
- `Services/BelCentrale.cs` (singleton): wie online is (`IBelDeelnemer` per tabblad), overgaan (40 s, daarna
  "Niet opgenomen" + bericht soort `gemist` in de berichtenbox), opnemen (andere tabbladen stoppen met overgaan), ophangen,
  en het doorgeven van offer/answer/ice. In het geheugen: na een herstart van de app is iedereen offline.
- `Components/Layout/Videobellen.razor` in MainLayout (gesprek blijft staan bij doorklikken, niet bij F5): inkomende
  oproep rechtsonder met belgeluid (Opnemen/Weigeren), belvenster met beeld van de ander groot en het eigen beeld klein,
  Dempen, Camera uit, Groter, Ophangen. Geen camera = alleen geluid. Meldt zich pas aan in `OnAfterRenderAsync` (niet het
  voorgerenderde exemplaar); op een werkplek opnieuw bij `MedewerkerGewijzigd`.
- Bellen: berichtenbox ▸ **📹 Bellen** (collega's, online met groen bolletje eerst) en 📹 bij een bericht van een online
  collega; via de scoped `BelVerzoek`.
- **Valkuil (gevonden bij testen)**: bij opnemen `_gesprek` vastleggen vóór `NeemOpAsync`, anders komt het offer van de
  beller binnen terwijl het nog niet als eigen gesprek telt en wordt het weggegooid (verbinding blijft op "verbinden").
- Getest lokaal met twee tabbladen (tijdelijke testingang `?belwn=` en nepbeeld via canvas, teruggezet): verbonden,
  beeld 320x180 over en weer, doorklikken, dempen, ophangen aan beide kanten. Niet getest: twee echte pc's met camera,
  een firewall ertussen, gemiste oproep (zou in de echte database schrijven), Weigeren.

## Berichten: plaatjes meesturen (22-10-2026)

Wens gebruiker. In het schrijfvak van de berichtenbox (`MeldingBel`) plaatjes plakken (Ctrl+V, `mdeKlembord.koppelPlakken`
op het schrijfvak, zoals het lampje) of kiezen; hoogstens 3, 10 MB (`BerichtBijlage`). Na versturen naar de filestore:
**StoreType 341** (`Meldingen_Bijlagen_MariaDB.sql`, zelf draaien: FK naar FileStoreType, anders weigert de database),
MasterID = MeldingID (`StuurBerichtAsync` geeft nu het id terug). `AlleAsync` haalt ze op (GROUP_CONCAT), de lijst toont
ze als miniatuur met link. **Privé**: `BestandUitgeven` (Program.cs) laat soort 341 alleen zien aan de ontvanger of de
afzender (`IMeldingRepository.MagBijlageZienAsync`). Wist de ontvanger het bericht, dan blijft het bestand in de
filestore staan. Gebouwd; query's gecontroleerd tegen de database; niet in de browser (lokaal geen login, dus geen bel).

## Projecten: voortgang per project voor het OHW (22-10-2026)

Wens gebruiker: per project een percentage gereed (aanname, voor het onderhanden werk) met een knop **Bepaal zelf**.
Op de tab **Budget** (dus alleen Directie en Budgetverantwoordelijke), onder het totaal (`ProjectBudget.razor`).
Kolommen `Projects.Voortgang` (0-100, 1 decimaal), `VoortgangOp`, `VoortgangDoor`, `VoortgangBron`
(**`Projects_Voortgang_MariaDB.sql`, zelf draaien**; zonder: een uitleg in plaats van het veld). Alleen de huidige
stand (besluit gebruiker), met wie/wanneer/hoe. **Bepaal zelf** = gemaakte kosten / totaal projectbudget (tab Budget;
besluit gebruiker: het projectbudget, ook al is dat meestal de verkoopwaarde). Kosten = de nacalculatie (inkoop +
materiaal + uren); de rekenregels staan nu in `NacalcData` (`Inkoop`, `Materiaalkosten`, `Uurkosten`, `Totaal`,
`TotaalKosten`), Nacalculatie.razor gebruikt dezelfde. Boven 100% wordt 100% met een melding; uren zonder
kostprijstarief worden genoemd. Lokaal getest met project 218183 (kosten € 155.775,79 van € 248.940 = 62,6%), met
opslaan tijdelijk uitgezet en de rechten tijdelijk open; teruggezet.

## Vragen aan AI: privé, delen met de directie, wissen (22-10-2026)

Besluit gebruiker: gesprekken zijn **privé** (alleen de vraagsteller); met het vinkje **Delen met de directie** (kolom
`AiVragen.Openbaar` op de eerste vraag van het gesprek, `AiVragen_Openbaar_MariaDB.sql`, ook achteraan
AiVragen_MariaDB.sql) zien directieleden het ook, alleen lezen. Het oude "Van iedereen" voor Directie is weg; Directie
heeft "Ook gedeelde gesprekken" (standaard aan). `LijstAsync(werknemerId, ookGedeeld, max)`, toegang in
`AiAnalyse.Kies` (eigen, of gedeeld en Directie; ook via ?vraag= uit de bel). Eigen gesprek wissen:
`WisGesprekAsync` (DELETE eerste vraag + vervolgvragen, alleen eigen, niet als er nog iets wacht/loopt), met
`mdeBevestig`. Zonder de kolom: alleen eigen gesprekken, delen geeft een melding. Kolomcontrole onthoudt alleen "ja".
Gebouwd; DELETE gecontroleerd met EXPLAIN; niet in de browser getest (lokaal geen login, en een vraag stellen kost
Bedrock-tokens).

## Lopende taak op de agent zichtbaar en te stoppen (22-10-2026)

Melding gebruiker: uitlezen gaf "NO-GO 0 drive(s) … uitlezen mislukt / Er loopt al een taak." (`/drives` geeft 409 als er een
job loopt; de `StopAchtergeblevenTaak`-route gold alleen voor taken via `VoerTaakUit`). Nu in ExemplaarWerk een paneel
bovenaan (`_lopendeTaak`, inline: `.melding.lopende-taak` is niet zwevend): taak, stand, duur en de laatste 6 logregels,
knoppen Taak stoppen en Vernieuwen. `ZoekLopendeTaak`: `/health` ▸ `bezig` (job-id), dan `/jobs/<id>`; jobs van dit scherm
(`_taakJobId`, `_encJobId`, `_serJobId`) tellen niet. Gezocht bij het openen (`OnAfterRenderAsync`, één keer), bij 409 op
`/drives` (dan geen NO-GO) en bij 409 van de seriële monitor. Stoppen + wachten: `StopEnWacht` (ook gebruikt door
`StopAchtergeblevenTaak`). Werkt met elke agentversie. Getest met een nep-agent op poort 5066
(`Productie__UitleesAgent`); op deze pc draait een echte agent op 5055.

## Ureningave: vragen bij weggaan met een niet-opgeslagen nieuwe regel (22-10-2026)

Bestaande regels slaan zichzelf op bij het wegklikken uit een veld; alleen de nieuwe regel (+ Nieuwe regel) moet met
Opslaan. Ureningave.razor vraagt nu (`mdeBevestig`) als die regel iets anders bevat dan vooringevuld (JSON-vergelijking met
`_nieuwBegin`): via `<NavigationLock>` bij een andere pagina, de browser-vraag bij sluiten/herladen, en `MagWeg()` bij
andere dag/week/werknemer/datum, Me, Plak dag en de boekknoppen. Na "Annuleren" zet `_herteken` (@key) de werknemer- en
datumkeuze terug. Lokaal getest (dag wisselen, zijbalk, annuleren, doorgaan); niets opgeslagen.

## Afgekeurd exemplaar niet inbouwen (22-10-2026)

Melding gebruiker: een afgekeurd halffabricaat kon nog aan een robot gekoppeld worden. Afgekeurd = `ProductStatus`
met StatusShort **FAILED** (id 8, "Product afgekeurd"). `IProductRepository.AfgekeurdTest` (EXISTS-subquery op
`p.ProductStatusID`) zit in alle samenstellingsquery's en vult `SamenstellingRegel.Afgekeurd`.
- `ZoekVrijeExemplarenAsync` biedt afgekeurde niet meer aan (`ookAfgekeurd: true` alleen om de reden te kunnen noemen:
  SN typen in `SamenstellingKiezer` zegt dan "… is afgekeurd").
- `KoppelAsync` weigert zelf ook (InvalidOperationException) als er een ouder komt; de kiezers in SamenstellingKiezer
  en Producten controleren `v.Afgekeurd` vooraf.
- Al gekoppelde afgekeurde delen: rood omrand met chip "afgekeurd" in de samenstelling (`sam-afgekeurd`).
- Automatisch uitlezen van een product: drive op een afgekeurd halffabricaat = "AFGEKEURD" (rood) en NO-GO.
- Bij het bouwen zat er al één in: product 410 (MPR-PCBA-RZ SN 72) in MPR-1 SN 4 (product 86); niet aangepast.

## Verbeterverzoeken ook in de lijst toevoegen (22-10-2026)

Beheer ▸ Verbeterverzoeken heeft **+ Nieuw verzoek** (wens gebruiker): tekstvak met de cursor erin, Toevoegen =
`IVerbeterRepository.IndienenAsync` zoals het lampje, maar zonder pagina en zonder schermafdruk (daarvoor blijft het
lampje). Mag wie het lampje ook mag (`Ik.Ingelogd || Ik.IsWerkplek`). Staat het filter op Gedaan/Afgewezen, dan springt
het naar Open zodat het nieuwe verzoek zichtbaar is. Lokaal getest met `Verbeter__Geheugen=true`.

## Berichtenbox: geluidje; werkplek: minuten per werknemer (22-10-2026)

- **Geluidje bij een nieuw bericht** (`MeldingBel.SpeelBijNieuw`): na elk laden (elke minuut of na een seintje) een
  ongelezen bericht met een id boven het hoogste van het vorige laden = nieuw (niet bij het eerste laden). Afspelen met
  `mdeBerichtGeluid(wn, id)` in App.razor: twee tonen via Web Audio, geen bestand; het hoogste gemelde id per werknemer
  in localStorage, zodat meerdere tabbladen niet allemaal piepen. Aan/uit met 🔊/🔇 in de kop van het berichtenvenster,
  onthouden in de browser per werknemer (`mde-geluid-<id>`, standaard aan; aanzetten speelt een proefje). Een browser
  speelt pas geluid na een eerste klik op de pagina. Goed te keuren bestellingen geven geen geluid.
- **Werkplek, afmelden na inactiviteit**: was vast `Pincode.Duur` (30). Nu per werknemer `Werknemers.WerkplekMinuten`
  (`Werknemers_WerkplekMinuten_MariaDB.sql`, zelf draaien; leeg = 30, max 720), op het werknemersscherm tab Status
  ("Werkplek: minuten tot afmelden"). `IWerkplekRepository.MinutenAsync`, gelezen bij aanmelden en als een ander
  tabblad iemand aanmeldde (`HuidigeGebruiker.WerkplekDuur`).
- **Activiteit telt nu zekerder** (melding gebruiker: "de tijd loopt gewoon door"; WerkplekApparaten 4 kreeg na het
  aanmelden maar één activiteit): werkplek.js (v=2) telt ook muisbewegen; mislukt `Tik` (verbinding even weg), dan
  blijft de activiteit staan voor de volgende minuut; `koppel` geeft een nummer en `los(nummer)` van een ouder exemplaar
  zet een nieuwere koppeling niet meer stil. **Werken in een ander programma (TwinCAT, Inventor) telt niet**: de browser
  ziet dat niet. Kan alleen via de tray van de agent (GetLastInputInfo in de sessie van de gebruiker), niet gebouwd.

## Melding "nieuwere MDE Agent": direct installeren (22-10-2026)

`AgentVersieMelding` heeft een knop **⬇ Installer x downloaden** (`GET /agent/installer` in Program.cs: de nieuwste
`MdeAgent-setup-*.exe` uit `AgentUitgave.NieuwstePad()`, achter login als die aan staat; een werkplek is ook aangemeld)
en **📋 Map kopiëren** (pad van installeer.bat, `mdeKopieer` in App.razor met terugval op execCommand zonder https).
Een `file://`- of `\server`-link kan niet: de browser blokkeert die vanaf een webpagina. De gedownloade setup vraagt
zelf om beheerderrechten; de tokenpagina leeg laten houdt het bestaande token. Getest: download (200, 88 MB) en kopiëren,
lokaal met een tijdelijk geforceerde melding. Niet getest: installeren via de download (SmartScreen kan waarschuwen,
de setup is niet ondertekend). Getest door de gebruiker: download en installeren werken, maar hij vulde het token
opnieuw in (de tokenpagina stond er altijd). Nu slaat installer.iss die pagina over als agent.json al een token heeft
(`HeeftToken`, `ShouldSkipPage`); wijzigen kan met `installeer.bat /token`. Compileert (ISCC), komt mee met de
volgende agentbuild; het overslaan zelf is nog niet getest.

## Firmware teruggevallen na herstart (agent 1.21.1, 22-10-2026)

Product 738 (Swivel SN 40): FoE-laden meldde "firmware 1.0.9 geladen, drive meldt 1.0.9", na EEPROM vullen en
herstart gaf uitlezen 1.0.8 (andere borden hielden 1.0.9). `drives.py` leest 0x100A live, dus geen cache in de app:
vermoedelijk een OTA-rollback in de ESP32 (de nieuwe image markeert zich niet als goed,
`esp_ota_mark_app_valid_cancel_rollback()`); dat is aan de firmware, niet aan de app.
- **Waarschuwing bij uitlezen** (`ExemplaarWerk.ControleerTerugval`, EtherCAT-route, na `ControleerDrives`): per
  positie het laatste wat de historie over de firmware zegt, "Drive N (...): firmware X geladen, drive meldt Y" of
  "Drive N uitgelezen (...): ..., firmware Z". Is dat het laden en meldt de drive nu iets anders (`VersieGelijk`), dan
  een regel in `_driveFouten` (geen NO-GO). Na opslaan van het uitlezen verdwijnt hij dus. Laden via USB (esptool,
  "Firmware geladen (ESP32): ...") telt niet mee: noemt geen positie en schrijft het hele flash.
- **Agent 1.21.1**: `drives.py` geeft bij eigen MDE-slaves `mde.kale_versie(0x100A)`, dus "1.0.9" in plaats van
  "v1.0.9", net als firmware laden en de EEPROM.
- **Patroon** (historie 22-10-2026): alleen boards die met **1.0.8** binnenkwamen (SN 38-42, via USB geflasht,
  vermoedelijk met rollback in de bootloader) vallen terug; boards van 1.0.3/1.0.7 (SN 15-19) houden 1.0.9. Na een
  tweede keer laden meldde 738/740 wel 1.0.9, maar toen zat er geen herstart tussen laden en uitlezen.
- **Agent 1.21.2, controleherstart** (`mde._controle_herstart`, na de versiecontrole in `_werker_firmware`, uit te
  zetten met `controle: false`): nog eens INIT → BOOT → INIT (de Swivel herstart bij het verlaten van BOOT),
  `_wacht_terug`, 0x100A opnieuw. Anders dan net geladen = taak faalt ("teruggevallen", historie MISLUKT). Of hij echt
  herstartte: PDI-watchdogteller van de ESC (0x0443) voor/na; niet veranderd = `controle: "geen herstart"`, de app
  zet dan "(controleherstart niet gezien: …)" achter de historieregel, "bleef" geeft ", ook na een controleherstart".
  Getest met nagebootste objecten; **niet op een echte Swivel**: of BOOT → INIT zonder bestand herstart, of een
  ESP32 met een onbevestigde update BOOT wel aanneemt, en of 0x0443 op de LAN9252 optelt.

## Tray-icoon van de agent (agent 1.21.0, 22-10-2026)

`MdeAgentTray.exe` (tweede EXE in `MdeAgent.spec`, `console=False`, startpunt `tray_start.py`, code `mde_agent/tray.py`;
tijdens ontwikkelen `python -m mde_agent --tray`). Draait in de sessie van de gebruiker; start voor iedereen bij het
aanmelden (snelkoppeling in `{commonstartup}`) en na installeren meteen (`runasoriginaluser`). Peilt elke 3 s
`/health` (+ `/jobs/<id>` met het token uit agent.json voor de naam van de taak) en de service. Kleur groen klaar /
blauw bezig / grijs service gestopt / rood antwoordt niet. Menu: status, Lopende taak stoppen (`/jobs/<id>/stop`),
Agent herstarten/stoppen/starten, Logboek openen, EverythingApp openen, Tray-icoon sluiten. Eén per gebruiker (mutex
`Local\MdeAgentTray`).
**Servicerechten** (besluit gebruiker: iedereen mag): installer.iss zet met `sc sdset` voor Authenticated Users
start/stop/query op de service MdeAgent (SDDL met `(A;;CCLCSWRPWPDTLOCRRC;;;AU)`); `tray._service` opent de service
met alleen START|STOP|QUERY_STATUS (niet SERVICE_ALL_ACCESS zoals win32serviceutil). Installer en verwijderen doden ook
`MdeAgentTray.exe`. `pystray` en `pillow` in requirements.txt. Getest: plaatjes, peilen tegen de draaiende 1.20.2 op de
ontwikkel-pc, menu; niet: herstarten als gewone gebruiker (na installeren met de nieuwe rechten).

## Programmeren en testen: eerst uitlezen; node-adressen met het echte type (22-10-2026)

- **Eerst uitlezen** (wens gebruiker): in `ExemplaarWerk` staan Programmeren, Node-adressen en Testen in een
  `fieldset disabled="@Vergrendeld"` (`ToonUitlezen && !_uitgelezen && !_tochDoor`), met erboven een melding en de knop
  "toch doorgaan" (print die niet antwoordt). `_uitgelezen` wordt gezet als het uitlezen iets vond: `Beoordeel` met minstens
  één drive, eRob via CAN, of de servo. Per exemplaar opnieuw (component heeft @key op het product).
- **Node-adressen**: de kolom Slave toonde de EEPROM-naam ("Capitan Drv" bij een Everest). Nu `NoemAliasSlaves`: positie-
  naam van het product + type uit het uitlezen (`DriveRegel.TypeUitTabel`) of de drivetypen-tabel op productcode/vendor;
  de EEPROM-naam staat in het wolkje. Gebouwd; niet in de browser gezien (geen agent/login lokaal).

## Bel = berichtenbox: altijd open, historie, berichten aan collega's (22-10-2026)

Wens gebruiker: het "messageboard" altijd toegankelijk; gelezen/ongelezen, historie bewaren tot de eigenaar wist; een
bericht naar een collega. `Components/Layout/MeldingBel.razor`: de bel staat er altijd (grijs zonder iets nieuws; getal =
goed te keuren + ongelezen). Venster "Berichten": Goed te keuren (zoals voorheen), dan alle meldingen en berichten
(`IMeldingRepository.AlleAsync`, nieuwste 200), filter Alles/Ongelezen, per regel ✓/○ (gelezen/ongelezen,
`ZetGelezenAsync`), ↩ antwoorden (alleen bij een bericht van een collega), ✕ wissen (vraag; `WisAsync` alleen op
`WerknemerID` = jij). Klikken op een melding met link zet hem op gelezen (verbeterverzoek: zelfde tabblad, anders nieuw).
**✉ Nieuw bericht**: collega kiezen (`IUrenRepository.GetWerknemersAsync`, actief, zonder jezelf), tekst tot 1000 tekens
-> `StuurBerichtAsync` (Soort `bericht`, `VanWerknemerID`, OrderID = bericht waarop geantwoord) + `MeldingSignaal`.
`Melding` heeft nu `Gelezen`, `VanId`, `Van` (init-eigenschappen, constructor ongewijzigd). **`Meldingen_Berichten_MariaDB.sql`
zelf draaien** (kolom VanWerknemerID, Tekst 1000); zonder kolom werkt de box wel, maar sturen niet (melding in het venster).
Gelezen meldingen werden al nooit gewist, dus de historie is er meteen. Gebouwd; leesquery op de database gecontroleerd;
de bel zelf niet in de browser bekeken (zonder login geen werknemer).

## PO klant: verplicht project, PO-bedrag als projectbudget (22-10-2026)

Wens gebruiker: bij een PO klant een verplicht **Project**; bij toevoegen gaat het projectbudget omhoog, bij wissen
weer omlaag. Kolom `PO_Inkomend.Project-id` (**`PoProject_MariaDB.sql`, zelf draaien**; zonder kolom geen veld en
geen budgetkoppeling). In `POKlantVenster` onder Klant het `ProjectVeld` (projecten van de klant), verplicht bij
opslaan (ook bij een oude PO zonder project). Lijst PO klant: kolom Project.
`IPOKlantRepository.BewaarAsync` (zelfde transactie) -> `BudgetVolgtPoAsync`: geen budgetregel met `POklant` = deze PO
-> nieuwe regel "PO <nummer> · <omschrijving>" met het PO-bedrag (alleen bij een nieuwe PO of een ander project, zodat
een op de tab Budget gewiste regel niet terugkomt); wel een regel -> mee naar het project van de PO, en het bedrag volgt
de PO zolang het gelijk was aan het oude PO-bedrag (met de hand aangepast budget blijft). `WisAsync` wist ook de
budgetregel(s) van de PO. Gebouwd; niet getest tegen de database (zou schrijven).

## Projecten: tabs Budget en Budget verdeling (22-10-2026)

Overgezet uit Access (Project/werkorder registratie, formulieren BudgetProject en ProjectBudgetVerdeling; VBA in
`Migration_Analysis\VBA_Source`). Alleen **Directie en Budgetverantwoordelijke** zien de tabs (`Projecten.MagBudget`,
besluit gebruiker). `Components/ProjectBudget.razor` + `Services/IProjectBudgetRepository.cs`, geen nieuwe tabellen:
- **Budget**: regels in `BudgetProject` (Omschrijving, Bedrag, POklant = `PO_Inkomend.PO_inkomendID`, Opmerking; Factuur*
  alleen tonen). PO-keuze = PO's van de klant van het project; kiezen stelt zoals Access het PO-bedrag voor (leeg bedrag =
  overnemen, anders vraag). % van PO = bedrag / PO-bedrag (NVT zonder PO). Vergrootglas = PDF van de PO (FileStore,
  StoreType 20, MasterID = PO). Opmerking van de gekozen regel onderaan, totaal rechts. Toevoegen en verwijderen (vraag).
- **Budget verdeling**: alle niet-verwijderde werkorders met `Workorders.Budget` (in te vullen), Doorbelasten
  (`ProjectVelden.DoorbelastBasis`), % van het projectbudget; onder: totaal werkorders (en %), totaal project, overschot
  (rood als negatief). De tabel `budget` (per werkorder, leeg) wordt niet gebruikt.
Bedragen Nederlands invoeren ("12.500,50"). Lokaal bekeken met project 218183 (budget 248.940, werkorders 273.200, dus
-24.260); niet geklikt om te wijzigen (zou in de echte database schrijven).

## Uitgaande facturen: project per factuur in plaats van werkorder (22-10-2026)

Wens gebruiker: koppelen aan een **project**, niet aan een werkorder. Nieuwe kolom `Invoices.Project-id`
(**`FactuurProject_MariaDB.sql`, zelf draaien**; zonder kolom blijft het veld leeg en wordt het niet opgeslagen).
`Workorder-id` is NIET IN GEBRUIK (op 22-10-2026 was geen enkele factuur gekoppeld, dus niets over te zetten; het oude
script staat zo gemarkeerd). `Components/ProjectVeld.razor` = zelfde opzet als `WerkorderVeld` (leeg = projecten van de
klant, typen = alle projecten op nummer/omschrijving/klant, alle woorden; "ook gesloten" = ProjectStatus 0), met
`IVerkoopFactuurRepository.ZoekProjectenAsync` / `ProjectenVanKlantAsync` (`FactuurProject`). Lijst: kolom **Project**
(nummer; wolkje omschrijving en klant). `SelectieProject` in Invoices is iets anders (uren ophalen). De zoekquery is
op de echte database gecontroleerd; kiezen en opslaan niet in de browser getest (rechten Financial/Directie nodig).
`WerkorderVeld` blijft bestaan (herbruikbaar), maar wordt hier niet meer gebruikt.

## Uitgaande facturen: werkorder per factuur (19-10-2026) — vervangen, zie hierboven

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

## Opbouwboom per producttype (12-11-2026, voorstel Sjoerd Quist deel 1 en 2, daarna als boom)

Voorstel en keuzes: artifact "MPR-robot opbouwfases" (claude.ai/artifact/QX2tdEnmJcax53mVsGt1Hs, collectie `keuzes`).
Keuzes 11-11-2026: fases instelbaar per producttype; aparte blokkade per fase (fase op "failed", handmatig op te lossen
met vastgelegde actie en foto's); status TEST vervalt; opmerking krijgt automatisch de actieve fase (aan te passen);
iedereen mag overslaan; duurtest: serienummer in de download van de SD-kaart; veld Opmerkingen op Algemeen blijft;
samenstelling/attributen corrigeren met een recht; volgorde van bouwen akkoord.
Wens gebruiker 12-11-2026: fases, subfases en stappen in **één boom**, daar te onderhouden; het slot (op volgorde) **per
item instelbaar**; een hoofdfase afronden met de **knop Fase klaar**.

- **Eén tabel**: `ProductOpbouwStappen` met `ParentStapID` en `OpVolgorde` (`ProductOpbouwBoom_MariaDB.sql`); status per
  exemplaar in `ProductOpbouwStatus` (+ `Overgeslagen`); `ProductHistorie.FaseStapID` = de hoofdfase van een regel.
  `ProductFases_MariaDB.sql` (eerste versie, losse fasetabellen) is in productie gedraaid; het boomscript zet die om
  (fases worden items, stappen eronder, stand en historie over). `ProductFases`, `ProductFaseStatus` en de kolommen
  `FaseID` zijn daarna **NIET MEER IN GEBRUIK**. Zonder boomscript: losse stappen zonder fases (`HeeftBoomAsync`).
- **Regels** in `Models/Opbouw.cs` (`OpbouwBoom`): item met kinderen = (sub)fase, zonder = stap. Hangt er op het bovenste
  niveau iets onder een item, dan is het een type met fases en zijn álle items van dat niveau hoofdfases (op volgorde;
  actief = de eerste die niet `Klaar` is). Hoofdfase af = afgerond met de knop of overgeslagen; subfase af = alles
  eronder af; stap af = afgevinkt of niet verplicht (`Moet`; een oude eindcheck, Soort 'controle', telt als verplicht).
  `Vrij(item)` = in de actieve hoofdfase, en bij een ouder met `OpVolgorde` pas als alles erboven af is.
- `Components/OpbouwLijst.razor`: de boom bij een exemplaar (hoofdfases ingeklapt/actief/op slot, `Tak` recursief,
  `Vink` controleert `Vrij` ook server-side, Fase klaar / Overslaan). Gebruikt in Producten (tab Opbouw; het tabblad
  Eindcheck is weg, ook bij het type), ProductieProduct en ExemplaarWerk.
- Onderhoud bij het type: `Components/OpbouwBoomBewerker.razor` (wens gebruiker later op 12-11-2026: "erg druk
  formulier met veel knopjes", bedienen zoals de calculatieboom). Per rij alleen greep ⠿, in-/uitklappijl, naam en
  omschrijving (velden zonder rand tot je erin staat) en kleine labels voor wat afwijkt ("↓ op volgorde", "optioneel",
  📎 n; klik = uitzetten/openen). **Slepen** met de greep: bovenste kwart van een rij = ervoor, onderste = erna (de
  streep staat onder het hele item met wat eronder hangt, daar komt hij ook), midden = eronder hangen (achteraan);
  niet op zichzelf of iets wat eronder hangt. **Sneltoetsen**: Ctrl+Enter nieuw item hierna, Ctrl+Shift+Enter nieuw
  item eronder, Alt+Shift+←/→ uit-/inspringen, Alt+↑/↓ schuiven, ↑/↓ zelfde veld in de rij erboven/eronder, Esc sluit
  het menu. **Rechtermuismenu** (of ⋯ op de rij): dezelfde handelingen plus Verplicht (stap) / Op volgorde ((sub)fase),
  Documenten en Verwijderen… (met alles eronder; `VerwijderStapAsync` haalt de hele tak weg). Balk: + Nieuw item,
  Uitspringen, Inspringen, status ("Opgeslagen · tijd") en de sneltoetsen.
  Het browserdeel is `window.mdeBoomBewerk` in App.razor (`koppel(el, net)`, `focus(el, id)`; **niet** `mdeBoom`, dat
  is het verhangen van werkorders bij Projecten): tekent de aanwijzers (`sleep-voor/na/in`, `sleept`) en roept
  `Verplaats(bron, doel, zone)`, `OpenMenu(id, x, y)`, `SluitMenu()` en `Toets(id, actie)` aan. Een sneltoets stuurt
  eerst een `change` van het veld waar je in staat, zodat wat je net typte bewaard is. Alle wijzigingen lopen door één
  `SemaphoreSlim` (volgorde van geven). Na een wijziging in de opbouw roept de bewerker `OpGewijzigd` aan; Producten
  laadt dan met `HerlaadOpbouw` (zonder eerst leeg te maken, anders verdwijnen de rijen even en is de cursor weg) en
  de bewerker zet de cursor terug (`_focusNa`). Zonder boomkolommen (`BoomAan` false): platte lijst, alleen ervoor/erna.
  Lokaal getest met een tijdelijke repository in het geheugen (scratchpad `boomb_tijdelijk.py`): alle sneltoetsen,
  menu, slepen (ervoor, erna, eronder, weigeren onder zichzelf), inklappen, verwijderen, licht en donker. **Niet
  getest**: tegen de echte tabel, de platte lijst zonder boomkolommen, documenten uploaden, en slepen met een echte muis
  (de test stuurde de sleepgebeurtenissen vanuit de pagina).
- Historie: `VoegHistorieToeAsync(..., faseId)`; niet opgegeven = subquery op de actieve hoofdfase, dus alle bestaande
  aanroepen krijgen vanzelf een fase. Kolom Fase (klik = aanpassen, `ZetHistorieFaseAsync`) en filter op fase.
- Getest lokaal met tijdelijke nep-gegevens (scratchpad `boom_tijdelijk.py`), niets geschreven. **Niet getest**: het
  boomscript zelf, alles wat schrijft (afvinken, Fase klaar, Overslaan, in-/uitspringen, verwijderen), de fase in
  nieuwe historieregels.
- Het voorstel is hiermee af: deel 3, 4 en 5 en "Programmeren en testen in de fases" staan hieronder.

## Programmeren en testen in de fases (12-11-2026)

Laatste stuk van het voorstel van Sjoerd Quist. Script: `ProductOpbouwStapFases_MariaDB.sql` (tabel `ProductStapFases`:
producttype, blok, hoofdfase). **Zonder gekozen fase verandert er niets**; zonder script ook niet
(`IOpbouwRepository.HeeftStapFasesAsync`).

- **Bij het type**: Producten ▸ type ▸ tab Techniek ▸ Stappen: achter elk blok (`ProductStap.MetFase`: Uitlezen,
  Programmeren, Testen, NodeAdres, Duurtest; niet Opbouw) een keuze uit de hoofdfases (`ZetStapFaseAsync`). Alleen
  Directie (`MagTechniek`), zoals de rest van dat blok.
- **De regel** staat in `StapFaseStand` (Models/Opbouw.cs; `LaadAsync(repo, type, exemplaar)`): een blok met een fase
  is alleen te gebruiken als die fase de **actieve** hoofdfase is (`OpSlot`). Een fase die niet meer bestaat telt niet.
- **`ExemplaarWerk`** past de regel zelf toe (dus ook op de werkplek, Test PCB): `ToonUitlezen/Programmeren/Testen/
  NodeAdres/Duurtest` = `HeeftBlok(stap) && Blok(stap)`. Blokken van een andere fase verdwijnen en bovenaan staat een
  balk "🔒 Niet in deze fase: … (fase n · naam)" met **✎ Buiten de fase om…** (recht Productcorrectie of Directie,
  bevestigen, één historieregel "Programmeren en testen buiten de fase om geopend: …"; geldt tot het scherm sluit).
  Parameter `AlleenFase`: alleen de blokken van die fase (geen balk). `NaOpbouw` herlaadt de stand als de afvinklijst
  in hetzelfde scherm staat (werkplek).
- **In de fase**: `OpbouwLijst` heeft `FaseActies` (`RenderFragment<OpbouwStap>`); Producten vult die met
  `<ExemplaarWerk AlleenFase="fase.Id" …>`. In de actieve fase staat een inklapbaar blok (`details.opb-acties`,
  standaard dicht) "⚙ Programmeren en testen in deze fase: …"; in de andere fases, en waar geen `FaseActies` is
  meegegeven (werkplek), alleen een regel met de namen. De blokken werken **ook bij een open fout** (om de oorzaak te
  vinden): alleen afvinken, invullen en afronden staan dan op slot. Blokken afronden is geen voorwaarde voor Fase klaar
  (daar zijn de afvinkstappen voor).
- **Productie ▸ Producten** (`ProductieProduct`): de eigen stap Duurtest toont 🔒 met de fase als die niet actief is.
- Het tabblad Programmeren en testen blijft bestaan (blokken zonder fase, blokken van de actieve fase, en buiten de
  fase om). "Eerst uitlezen" geldt per scherm: staat Uitlezen in een andere fase dan Node-adressen, dan vraagt het
  scherm van die latere fase er niet om (de serienummercontrole van deel 4 draait daar wel).
- Getest lokaal in het geheugen met de nagebootste agent (scratchpad `d3_tijdelijk.py`, `nepagent.py`): fase kiezen
  bij Techniek, blokken in de actieve fase (inklapbaar, blijft open na een vinkje), namen in de andere fases, slot en
  "buiten de fase om" op het tabblad. **Niet getest**: de echte tabel en het script, de werkplekschermen (Test PCB,
  Productie ▸ Producten), de duurtest in een fase (lokaal geen filestore) en het recht zonder de tijdelijke "alles mag".

## Opbouw deel 5: invullen in de fase, tabblad Info, corrigeren met een recht (12-11-2026)

Voorstel Sjoerd Quist deel 5 ("Documenten, Samenstelling en Attributen samen, alleen lezen; invullen gebeurt in de
fase"). Besluiten gebruiker 12-11-2026: het invullen in de fase **samen** met het tabblad bouwen (anders kan niemand
zonder recht nog iets invullen), en een **nieuw recht Productcorrectie**. Script: `ProductOpbouwDeel5_MariaDB.sql`
(`ProductNameAttributes.FaseStapID`, `ProductSamenstelling.FaseStapID`, `Werknemers.Productcorrectie`). Het tabblad
Info komt er altijd; het slot en de kolom Fase pas na het script (`IProductRepository.HeeftInvulFaseAsync`) en alleen
bij een type met fases. **Zonder gekozen fase verandert er niets**: het slot geldt per attribuut/stuklijstregel.

- **Bij het type** (Producten ▸ type): tab Attributen kolom "Invullen in fase", tab Samenstelling kolom "Inbouwen in
  fase" (keuze uit de hoofdfases; `ProductAttribuut.FaseStapId`, `StuklijstRegel.FaseStapId`). Een exemplaar-attribuut
  leest de fase via zijn standaardattribuut (`ProductNameAttributeID`), dus de keuze geldt meteen voor alle exemplaren.
- **In de fase** (`OpbouwLijst`, blok "Invullen in deze fase", fragment `Invullen`): de attributen van die fase als
  invoerveld en de stuklijstregels via `SamenstellingKiezer` met `AlleenFase`. Alleen in de **actieve** fase en niet
  bij een open fout; in andere fases staat hetzelfde blok als "Ingevuld in deze fase", alleen lezen. `OntbreektIn(f)`
  (lege verplichte attributen + nog niet gekoppelde halffabricaten) houdt **Fase klaar** tegen (ook server-side);
  Overslaan mag. Een attribuut invullen in de fase geeft een historieregel (soort Attributen, met de fase). Achteraf
  iets veranderen: Terug naar de fase (iedereen) of Corrigeren (recht).
- **Tabblad Info** (Producten ▸ exemplaar): Samenstelling, Attributen en Documenten onder elkaar; de tabs Documenten,
  Samenstelling en Attributen bestaan bij een exemplaar niet meer (bij het type wel). `ZorgTab()` en `ZetTabUitUrl`
  zetten een onthouden tab of een oude link (`?tab=Samenstelling`) om naar Info. Balk bovenaan (`info-slot`,
  `InfoMetSlot`): wat bij een fase hoort is alleen te lezen (🔒 met de fase); **✎ Corrigeren…** (`_corrigeren`, per
  geopend exemplaar opnieuw; `MagCorrigeren` = recht Productcorrectie of Directie) maakt alles bewerkbaar en zet
  "Correctie: …" in de historie (attribuutwaarde oud → nieuw, attribuut verwijderd, samenstelling, document
  verwijderd). **Documenten**: toevoegen mag altijd; verwijderen bij een type met fases alleen bij corrigeren
  (`DocumentOpSlot`). Op een werkplek gelden de rechten van de werkplek, niet van de aangemelde medewerker.
- **`SamenstellingKiezer`**: parameters `AlleenFase`, `AlleenLezen`, `Corrigeren`; zoekt de hoofdfases van het type
  zelf op (alleen als een regel een fase heeft), zodat het slot overal geldt, ook in stap 4 van Productie ▸ Producten
  (daar staat 🔒 en koppel je in de fase eronder). `OpSlot(regel)` wordt ook in `Koppel`/`Ontkoppel` getoetst (vrij
  zoeken kan een halffabricaat van een regel op slot vinden). Halffabricaten buiten de stuklijst ("Overig") en regels
  zonder fase blijven vrij.
- Niet op slot: wat Programmeren en testen zelf in attributen zet (uitgelezen serienummers, `BewaarDrives`), en de
  vraag bij het aanmaken van een exemplaar. De oude `Koppel`/`Ontkoppel` in Producten.razor worden niet meer gebruikt.
- Getest lokaal met alles in het geheugen (scratchpad `d5_tijdelijk.py` naast `d3_tijdelijk.py`: de fase per
  attribuut/regel en de waarden in het geheugen, schrijven naar de console): fase kiezen bij het type, Info met slot,
  oude link naar Info, Corrigeren (historie "Correctie:"), invullen in de actieve fase, Fase klaar tegengehouden tot
  alles is ingevuld, alleen lezen in andere fases. **Niet getest**: de echte kolommen en het script, het recht zonder
  de tijdelijke "alles mag" (de uitgeschakelde knop Corrigeren), echt koppelen/ontkoppelen in een fase (de test
  schreef niet), Productie ▸ Producten met een regel op slot, en het donkere thema van de nieuwe blokken.

## Opbouw deel 4: serienummercontrole bij node-adressen en duurtest (12-11-2026)

Voorstel Sjoerd Quist deel 4: niet per ongeluk node-adressen schrijven in, of een duurtest ophalen van, een andere robot
dan die gekozen is. Geen script en **geen nieuwe agentversie** nodig (de controle gebruikt het bestaande `/drives`).

- **Node-adressen** (`ExemplaarWerk`, venster Node-adressen): `ControleerBus()` leest de serienummers op de bus
  (`Agent("/drives")`, posities + 1: het uitlezen telt vanaf 0, de node-adressen vanaf 1) en zoekt elk nummer op met
  `ZoekOpDriveSerienummerAsync`. Goed = het halffabricaat is dit exemplaar zelf of zit erin (`Afstammelingen`); **vreemd**
  = het hoort bij een ander exemplaar (met `WaarZit` erbij in welke robot het nu zit, via `GetGebruiktInAsync`). Los
  exemplaar met een eigen MDE-slave zonder attribuut: het nummer in de EEPROM moet het serienummer van het exemplaar
  zijn. Geen serienummer (eRob) of een onbekend nummer telt niet mee.
  Uitkomst (`BusControle`): **Vreemd** → er wordt niets geschreven (melding, rode balk in het venster, historieregel
  "niet geschreven: op de bus zit … uit …", uitkomst mislukt); **Ok** (minstens één herkend, niets vreemd) → de vraag
  noemt "✓ Serienummers gecontroleerd"; **niet te controleren** (niets herkend, of lezen mislukt) → schrijven mag, met
  "LET OP" in de vraag. De controle draait na Lezen (kolom **Hoort bij** per slave) en **opnieuw vlak voor het schrijven**
  (de kabel kan intussen op een andere robot zitten). De historieregel van het schrijven eindigt op "(serienummers
  gecontroleerd)" of "(serienummers niet te controleren)". Zit een print er echt in maar staat hij niet in de
  samenstelling: eerst koppelen op het tabblad Samenstelling.
  Meteen hersteld: `NoemAliasSlaves` vergeleek de positie van het uitlezen (vanaf 0) met die van de node-adressen
  (vanaf 1) en nam dus het type van de buurman.
- **Duurtest** (`DuurtestService`): keuze 11-11-2026 "de PLC zet het serienummer in de download van de SD-kaart";
  later komt de bediening van de test erbij en kan het nummer vooraf gelezen worden. **Het formaat is nog niet
  afgesproken.** De app zoekt in de tekstbestanden van de log (`ZoekSerienummer`, niet-binaire bestanden tot 4 MB) een
  regel die begint met Serienummer, Serienr of Serial number, dan een scheidingsteken en een waarde met een cijfer
  (`StandaardPatroon`; een kopregel "Serienummer;Datum" en een los "SN" tellen bewust niet, een log kan ook
  drive-serienummers noemen). `ZelfdeSerienummer`: 032 = 32, "MPR1-0032" = 32. Ander serienummer → uitzondering, er wordt
  **niets opgeslagen** (de aanroeper zet "Duurtest: log ophalen MISLUKT — …" in de historie). Geen serienummer in de log →
  doorgaan zoals voorheen met "geen serienummer in de log, niet gecontroleerd" in de historie, tenzij
  `Productie:Duurtest:SerienummerVerplicht` aan staat. `Productie:Duurtest:SerienummerPatroon`: eigen regex (eerste
  groep = het nummer); lege tekst = niet controleren.
- Getest: node-adressen in de browser met een nagebootste agent (scratchpad `nepagent.py`, poort 5066, echte
  serienummers van MPR-1 16 en MPR-1 4): goed, vreemd (niets geschreven), niet te controleren, en schrijven na de
  controle; historie en schrijven naar de console. Duurtest: `ZoekSerienummer`/`ZelfdeSerienummer` met voorbeeldlogs
  (scratchpad `d4toets`). **Niet getest**: echte hardware en de echte agent, een los exemplaar (print direct
  aangesloten, MDE-slave), of `/drives` op een echte robot evenveel slaves geeft als `ecat.alias` (anders staat er een
  waarschuwing in de vraag), en de duurtest tegen de PLC (er staat nog geen serienummer in de log).

## Opbouw deel 3: fout melden, terug naar een fase, todo's per fase, telefoon als camera (12-11-2026)

Voorstel Sjoerd Quist deel 3. Besluit gebruiker 12-11-2026: **iedereen mag terugzetten**; wens tijdens het bouwen: de
foto maken met de telefoon, "interactief als het kan". Scripts (door de gebruiker te draaien):
`ProductOpbouwDeel3_MariaDB.sql` (tabel `ProductOpbouwBlokkades`, `ProductTodo.FaseStapID`, FileStoreType 331 en 332) en los
daarvan `ProductStatusTestVervalt_MariaDB.sql`. Zonder het eerste script werkt alleen Terug naar een fase (dat heeft
geen script nodig); Fout melden en de todo's per fase verschijnen pas daarna (`HeeftBlokkadesAsync`, `HeeftFaseAsync`).

- **Terug naar een fase** (`OpbouwLijst`, knop in de actieve fase, of onder de lijst als alles af is): keuze uit de
  afgeronde hoofdfases + verplichte reden. `IOpbouwRepository.ZetTerugAsync` zet alleen `Klaar = 0` (en
  `Overgeslagen = 0`) voor die fase, alle fases erna en alles eronder; `KlaarOp`/`KlaarDoor` blijven staan, zodat bij
  de stap "eerder: datum · naam" staat. (Een vinkje met de hand weghalen maakt ze wel leeg, zoals voorheen.) Historie:
  "Terug naar fase n · naam (x vinkjes vervallen): reden", fase = de doelfase. Was het exemplaar gereed (`_isGereed`:
  status bevat `Productie:GereedStatus` of is gelijk aan `Productie:OkStatus`; StatusKort is "OK", dus alleen op
  "Gereed" testen klopte nooit), dan gaat de status naar `Productie:BezigStatus` (standaard "In productie").
- **Fout melden** (blokkade; iets anders dan status FAILED = definitief afgekeurd): `OpbouwBlokkade` in
  `Models/Opbouw.cs`, tabel `ProductOpbouwBlokkades` (per exemplaar, met de hoofdfase; null bij een type zonder fases).
  Zolang er een open staat is de opbouw van dat exemplaar **op slot**: afvinken (ook server-side in `Vink`), todo's
  afvinken, Fase klaar, Overslaan en Product gereed. Rode balk boven de lijst met ✔ Oplossen…: verplichte tekst wat
  er gedaan is, foto's, en "Daarna": verder in de actieve fase of meteen terug naar een eerdere fase. Opgeloste fouten
  staan onderin hun fase (melding, oplossing, 📎 foto's). Historie met uitkomst mislukt/geslaagd en de fase. Foto's:
  FileStore **StoreType 332** (`OpbouwStore.Blokkade`, MasterID = BlokkadeID), naam begint met "fout " of
  "oplossing ". Producten: ⚠ in de exemplarenlijst (`GeblokkeerdeExemplarenAsync`, geladen met de todo-telling).
  De blokken Programmeren en testen (ExemplaarWerk) kijken **niet** naar het slot.
- **Telefoon als camera**: `Services/FotoBrug.cs` (singleton, alles in het geheugen: `FotoVraag` met een Guid, `Open`,
  `Sluit`, `Zoek`, `NieuwsteVan(werknemer)`, `LeverAsync`, event `Gewijzigd`; ook `QrSvg.Maak` met ZXing).
  `Components/FotoVak.razor` (pc): miniaturen met ✕, "📎 Kies foto…" en "📱 Met telefoon" = vraag openen + QR-code
  naar `mobiel/foto?v=<id>` (`Nav.ToAbsoluteUri`, dus het adres waarmee de pc de app open heeft). De foto's blijven in
  de lijst `Fotos` van de aanroeper (`FotoStuk`: naam, JPEG-bytes, voorbeeld als data-URL) tot die ze opslaat; sluiten
  van het vak sluit de vraag. `Components/Pages/MobielFoto.razor` (`/mobiel/foto`, tegel 📷 Foto op MDE mobiel): toont
  de vraag van de QR-code, of zonder QR de nieuwste vraag van dezelfde werknemer, en luistert naar `Gewijzigd` (een
  nieuwe vraag op de pc verschijnt vanzelf). Foto in de browser verkleind (2000 px, voorbeeld 360 px) zoals MobielBon.
  De aanroepen van de brug komen binnen op de verbinding van de telefoon: FotoVak gaat eerst naar zijn eigen scherm
  (`InvokeAsync`). Herbruikbaar voor andere schermen die foto's willen.
  **Ook in het factuurvenster** (wens gebruiker 12-11-2026, na de geslaagde test met de fout-foto):
  `<FotoVak AlleenTelefoon="true" OpNieuweFoto="FotoVanTelefoon" />` onder het sleepvak (alleen met `MagInvoeren`).
  `AlleenTelefoon` = alleen de knop "📱 Foto met telefoon" en de QR-code; `OpNieuweFoto` geeft elke foto meteen door
  in plaats van hem in `Fotos` te zetten. `FactuurVenster.FotoVanTelefoon` maakt er een `GekozenBestand` van met de
  naam "factuur foto <datum tijd>.jpg" (telt zo als de factuur zelf) en loopt dezelfde weg als een gekozen bestand
  (`NaToevoegen`, afgesplitst uit `BestandenGekozen`: bewaren bij een bestaande factuur, in beeld, uitlezen als de
  factuur nog leeg is). FotoVak wacht niet op de aanroeper: de telefoon mag door zodra de foto is aangekomen, ook als
  het uitlezen nog loopt. Getest lokaal met een tweede tabblad als telefoon en een getekende proeffactuur: foto in de
  lijst en het voorbeeld, 5 velden uitgelezen via Bedrock, telefoon klaar in ruim 2 s; niets opgeslagen. Niet getest:
  een echte telefoon, een tweede foto (bijlage), en een bestaande factuur (dan wordt de foto direct opgeslagen).
- **Todo's per fase**: `ProductTodo.FaseStapId`/`Fase` ("4 · Kalibratie", zelfde subquery als de historie),
  `MaakAsync(..., faseStapId)`, `ZetFaseAsync`, `KopieerAsync` neemt de fase mee (doelen zijn van hetzelfde type). In
  `OpbouwLijst` staan de punten van een fase onder de stappen (chip "todo"), met "＋ Todo voor deze fase" in elke
  geopende fase (ook een latere). Open punten van de actieve fase houden **Fase klaar** tegen; Overslaan mag, met een
  waarschuwing. Afvinken in de lijst kan in de actieve of een afgeronde fase; op de tab Todo altijd. Tab Todo: keuze
  Fase bij het invoeren en per punt van het exemplaar zelf (`ProductNameId` als parameter); todo-overzicht toont de fase.
- **Status TEST vervalt**: de app zette TEST nergens zelf. Het script zet de exemplaren op TEST (12-11: 14 × MPR-1,
  4 × MPR-CAB-CA2) naar PROD met een historieregel en geeft de status `Vervallen = 1`; `GetStatussenAsync` laat
  vervallen statussen weg uit de keuzelijsten (`HeeftStatusVervallenAsync`).
- Lokaal getest met alles in het geheugen (scratchpad `d3_tijdelijk.py`: opbouw-repository in het geheugen, historie en
  statuswijziging naar de console, `ProductTodo__Geheugen=true`, `Filestore__Pad=REPLACE_x`): fout melden met een foto
  vanaf een tweede tabblad als telefoon (QR-adres), het slot, oplossen met terug naar fase 2, terug-paneel, todo in
  een fase (houdt Fase klaar tegen), tab Todo, licht en donker. **Niet getest**: de MySql-versies van blokkades en
  todo-fase (tabel/kolom bestonden nog niet; de fase-subquery is lezend gecontroleerd), foto's echt opslaan en tonen,
  de route zonder QR (zelfde werknemer op pc en telefoon; lokaal is er geen aanmelding), een echte telefoon, de ⚠ in
  de lijst met echte gegevens, en beide scripts.
- **Documenten bij een opbouwstap: StoreType 331** (`OpbouwStore.Stap`). Dat was 330, maar 330 is van de
  bezoekverslagen (`BezoekVenster.StoreTypeBezoek`, in `FileStoreType` "Bezoekverslag"): een document bij stap 7 zou
  ook bij bezoekverslag 7 staan. Op 12-11-2026 stonden er geen bestanden met 330 of 331 (lezend gecontroleerd), dus er
  is niets verhuisd. 331 komt in `FileStoreType` met `ProductOpbouwDeel3_MariaDB.sql`; **zonder dat script faalt een
  document toevoegen bij een opbouwstap op de foreign key**. Niet getest (zou in de filestore schrijven).

## MDE mobiel: menu in twee lagen en het echte logo (12-11-2026)

Wens gebruiker. `Components/Pages/Mobiel.razor` heeft drie adressen: `/mobiel` (startscherm: Gebouwbeheer, Bestellen,
Foto, Bonnetje), `/mobiel/gebouwbeheer` (Verlichting, Zonwering, Compressor, Alarm) en `/mobiel/bestellen` (Scannen naar
batch, `scannen?van=mobiel`). Welk menu er staat komt uit het adres (`Menu`); het is één pagina zonder parameters, dus
Blazor tekent hem bij wisselen van adres **niet vanzelf opnieuw**: de pagina luistert naar `Nav.LocationChanged` (zonder
dat bleef het startscherm staan onder het nieuwe adres). Een nieuwe functie = één tegel in het juiste menu; een tegel
kan een regel uitleg hebben (`<small>`). De onderliggende pagina's hebben bovenaan `a.mobiel-pad` terug naar hun menu
(licht/zonwering/compressor/alarm → Gebouwbeheer; bon/foto → Menu); Scannen toont met `?van=mobiel` "‹ Bestellen" in
plaats van "‹ Batch". `MobielLayout`: linksboven `wwwroot/logo-mde.png` (zoals de zijbalk van de grote app), in donker
op een wit vlakje omdat de letters MDE zwart zijn. Lokaal bekeken op telefoonformaat, licht en donker.

## Bestellingen: lijst Niet doorgezet, en Verkooporders naar Projecten (13-11-2026)

Verzoek (via de gebruiker): een extra controle zodat bestellingen niet op "bestellen" blijven staan, waardoor er geen
factuur aan gekoppeld kan worden. Gemeten (alleen lezen): 108 bestellingen langer dan 14 dagen op **Aanmaken**
(status 0; staan niet in de keuzelijst bij het koppelen van een factuur, alleen via zoeken) en 70 op **In bestelling**
(status 3; koppelen lukt daar wel, 26 van de 37 uit 2026 hebben al een factuur, maar er is nooit op Volgende gedrukt;
bijna niets is via de app gemaild, dus de vraag "Nu op Besteld zetten?" komt zelden). Besluit gebruiker: een
**persoonlijke lijst** van wat langer dan twee weken op een van beide staat, als **tegel bij Bestellingen**, PO's te
openen in een nieuw venster. (Een herinnering in de bel of iets bij het koppelen zelf is voorgesteld, niet gekozen.)

- `IBestelRepository.NietDoorgezetAsync(dagen, bestellerId?)` en `AantalNietDoorgezetAsync` -> `HangendeBestelling`
  (Models/Bestelling.cs). Status 0 of 3, besteller = `Created by`. "Sinds" = bij Aanmaken de aanmaakdatum, bij In
  bestelling `Approved2 date` ?? `Approved1 date` ?? `Request date` ?? aanmaakdatum (die velden worden gezet bij het
  verlaten van een stap). Erbij: aantal regels, aantal gekoppelde facturen (`Facturen_Orders_Link`), gemaild
  (`MailAangemaakt`). Grens: `BestelFlow.NietDoorgezetDagen` = 14.
- **Pagina `Components/Pages/NietDoorgezet.razor`** (`/bestellingen/niet-doorgezet`): eigen bestellingen per status (In
  bestelling eerst), kolom "Let op" (er is al een factuur / al gemaild / geen regels), dagen rood boven 60.
  `Ik.MagFinancien` krijgt een keuzelijst: Mijn bestellingen, Iedereen (per besteller, meeste bovenaan) of één collega.
  PO-nummer = `bestellingen?order=<id>` in een nieuw venster. Alleen lezen. Opmaak van Te laat betaald (`telaat-tabel`).
- **Tegel**: `Menu.cs` Bestellingen ▸ Inkoop ▸ **Niet doorgezet**. `Module.razor` kan nu een regel onder een tegel
  tonen (`_onder`, adres -> tekst, klasse `tegel-sub`): bij Bestellingen "n van jou" als de ingelogde werknemer er
  heeft (één telling per keer dat de modulepagina opent).
- **Verkooporders** staat nu in het menu bij Projecten ▸ Offertes en calculaties (wens gebruiker); de groep Verkoop
  bij Bestellingen is weg. Het adres blijft `/bestellingen/verkooporders`.
- Getest lokaal op de echte gegevens (alleen lezen, testpatch voor rechten): Iedereen (178 bestellingen, 22
  bestellers), één collega (per status), de link naar de bestelling, de tegels bij Bestellingen en Projecten.
  **Niet getest**: de eigen lijst en "n van jou" onder de tegel (lokaal geen ingelogde werknemer), en de weergave
  zonder het recht Financial/Directie.
- De lijst bevat ook oude rommel (Aanmaken vanaf 2018, In bestelling vanaf 2019): opruimen is annuleren of doorzetten.

## Te laat betaald: dagelijkse melding en overzicht (13-11-2026)

Wens gebruiker: één keer per dag voor Directie en Finance een overzicht van facturen die nog niet betaald zijn (te
laat), en ze in een nieuw venster kunnen openen. Besluiten (gevraagd): **uitgaande** facturen, en als **melding in de
bel** (geen mail). Geen script en geen eigen tabel.

- **`Services/TeLaatFacturen.cs`**: `TeLaatFacturen.LaadAsync` = dezelfde facturen als de knop "Te laat" op Uitgaande
  facturen (verstuurd, niet betaald, `Overdue > 0`) zonder oninbaar (`VerkoopFactuurRegel.TeLaat`), met wat er al binnen
  is (`IFactuurBetalingRepository.OntvangenAsync`) -> `TeLaatFactuur` (Open, Vervaldatum, Dagen). `Meldtekst` en
  `MoetNu` (werkdag, vanaf het uur, nog niet gedaan vandaag) zijn los te toetsen (scratchpad `d15toets`).
- **`TeLaatWaker`** (BackgroundService, kijkt elke 10 minuten): maakt de melding (Soort `telaat`, OrderID = aantal
  facturen) voor iedereen in dienst met Financial of Directie (`IMeldingRepository.WerknemersMetRechtAsync`, nieuw) en
  geeft `MeldingSignaal`. Niets te laat = geen melding. "Vandaag al gedaan" volgt uit de tabel Meldingen
  (`VandaagGemaaktAsync`, nieuw: een melding van die soort sinds middernacht, van wie dan ook), dus een herstart
  stuurt niet opnieuw. Instellingen `TeLaatFacturen:Aan` en `:Uur` (7); de sectie mag ontbreken. **In Development
  staat hij standaard uit**, anders zet een lokale testversie meldingen in de echte database.
- **Bel** (`MeldingBel`): `telaat` -> link `financien/te-laat` (nieuw venster, zoals de andere meldingen), icoon ⏰.
- **Pagina `Components/Pages/TeLaat.razor`** (`/financien/te-laat`, menu Financiën ▸ Facturen ▸ Te laat betaald; alleen
  `Ik.MagFinancien`): per klant (meeste open bovenaan), per factuur datum, vervaldatum, dagen (rood boven 30), BV,
  omschrijving (met herinnering/proforma), incl. btw, ontvangen, open; totaalregel. Factuurnummer =
  `financien/uitgaande-facturen?factuur=<nummer>` in een nieuw venster; 📄 = `bestand/<id>` (filestore soort 5: de
  nieuwste PDF met `_Invoice` in de naam, anders de nieuwste PDF; per factuur opgezocht nadat de lijst er staat).
- **Uitgaande facturen**: `?factuur=<nummer>` (`FactuurUitUrl`) opent zonder datumfilter met het nummer in het zoekvak
  en die factuur gekozen in het zijpaneel.
- Getest: de rekenregels (`d15toets`, alles ok); lokaal de pagina op de echte gegevens (alleen lezen: 26 facturen,
  7 klanten, licht en donker), de link naar een factuur, de PDF-links; de twee nieuwe leesvragen op de database (4
  ontvangers). **Niet getest**: de waker zelf (het maken van de meldingen, het moment), de melding in de bel en het
  openen daarvandaan (lokaal geen ingelogde werknemer), het menu-item met een echt recht.
- Opgevallen in de gegevens: 13 van de 26 zijn facturen aan MDE Holding B.V. uit 2023 (management fee, ruim 1000
  dagen); die staan nooit op betaald. Niet uitgesloten: dat is een keuze van de gebruiker.

## Factuur versturen: venster dicht en een melding met OK (13-11-2026)

Wens Edwin: na het mailen van een factuur een pop-up dat het gelukt is, met OK, en het venster meteen dicht.
`FactuurVersturenVenster.Afronden(kop, gelukt)` sluit nu altijd (`Sluit()`) en roept daarna `mdeGelukt` aan (nieuw in
`App.razor`: dezelfde dialoog als `mdeBevestig`/`mdeMelding` met optie `goed` = groen, klasse `bevestig-box goed`; wacht
niet op OK). Voorheen: een groene regel in het venster, 0,8 s, dan dicht; en bij een fout bij het bewaren bleef het
venster open (met de knop Mail versturen nog actief, dus kans op een tweede mail). Nu ook dan dicht, met de oranje
`mdeMelding` "…, maar let op" en wat er misging (ook in het logboek). Geldt ook voor "Alleen opslaan als verstuurd"
en voor het versturen vanuit Verkooporders (zelfde venster). De orderbevestiging (`OrderbevestigingVenster`) is niet
aangepast. Getest lokaal met een tijdelijke patch (bewaren en op verstuurd zetten overgeslagen) via Alleen opslaan:
venster dicht, groene melding, OK. **Niet getest**: een echte mail, en de melding bij een fout.

## Orderbevestigingen uit de inkooppostbus (12-11-2026)

Wens gebruiker: wat vroeger een VB-programmaatje deed (de inkoopbox scannen op "PO-xxxxx" en de orderbevestiging als
document bij de bestelling zetten). Besluiten: postbus `inkoop@mde-automation.nl`, **alleen koppelen** (niets
uitlezen), de eerste keer drie maanden terug en daarna bijhouden, simpel houden. Script (gebruiker):
`InkoopBevestigingen_MariaDB.sql`. Staat standaard **uit**: aanzetten in appsettings, sectie `InkoopBevestigingen`
(`Aan`, `Postbus`, `Minuten` 15, `Vanaf` leeg = drie maanden terug, `Label` "EverythingApp/Bevestiging").

- **`Services/InkoopBevestigingOphaler.cs`** (BackgroundService, zelfde opzet als `FactuurInboxOphaler`; MailService
  is scoped, dus per ronde een scope). Gmail-zoekopdracht: `-label:"…" -in:sent -in:drafts -in:spam -in:trash
  after:<vanaf>`: alles wat binnenkwam, ook als het al in een map staat, maar niet onze eigen bestelmail (daar staat
  het PO-nummer ook in). Per ronde hoogstens 6 × 50 berichten; de eerste drie maanden komen dus over meerdere rondes.
  Elk bekeken bericht krijgt het label; er wordt niets verwijderd of verplaatst.
- **PO-nummer** (`PoHerkenning`): "PO" + hooguit drie scheidingstekens + 4 tot 6 cijfers (PO-18544, PO 18544,
  PO18544, P.O. 18544, PO#18544, po_18544), niet midden in een woord en niet het begin van een langer getal
  (PO 4500087158 is van de leverancier); **een los getal zonder PO telt niet**. Eerst het onderwerp, dan de namen van
  de bijlagen, dan de tekst: de eerste plek die iets oplevert telt. De bestelling moet bestaan en in de tijd passen
  (aangemaakt hooguit 400 dagen vóór en 2 dagen na de mail).
- **Koppelen**: alle PDF's van het bericht (ook een PDF in de tekst zelf) naar FileStore soort 4
  (`BestelVelden.StoreTypeOrder`, MasterID = Order-id), dus op Bestellingen ▸ Documenten; "Door" is leeg (de service).
  Een bestand dat er met dezelfde naam al staat komt er niet nog eens bij. Meerdere PO-nummers in één bericht: bij
  elke bestelling. De bestelling zelf (status, leverdatum) verandert niet.
- **Tabel `InkoopBevestigingen`** (een regel per bericht dat ertoe doet; ook het vangnet tegen dubbel verwerken):
  uitkomst `gekoppeld`, `geenpo` (wel een PDF, geen PO-nummer), `onbekend` (PO-nummer dat niet bestaat, met PDF),
  `geenbijlage` (bestelling gevonden, geen PDF), `genegeerd`. Een bericht zonder PO-nummer én zonder PDF wordt niet
  vastgelegd.
- **Overzicht**: Bestellingen ▸ knop **📥 Bevestigingen (n)** (`Components/InkoopBevestigingenPaneel.razor`; n = wat je
  zelf moet bekijken: `geenpo` en `onbekend`). Daar: PO-nummer typen + Koppelen (`KoppelMetDeHandAsync` haalt het
  bericht opnieuw op), Negeren, ↻ Nu ophalen, de stand van de laatste ronde, en een link naar de bestelling.
- Getest: `PoHerkenning` met 21 voorbeelden (scratchpad `d13toets`); het overzicht lokaal met
  `InkoopBevestigingen__Geheugen=true` (voorbeeldregels): tellen, filter, Negeren, link naar de bestelling.
  **Niet getest**: alles wat de postbus raakt (lokaal is `Google:ServiceAccountJson` leeg): de zoekopdracht, het
  label, de bijlagen ophalen, opslaan in de filestore, met de hand koppelen; de echte tabel en het script; en of
  inkoop@ een echte gebruiker is (een groep kan het serviceaccount niet openen).

## Artikelvenster: grote foto, geen leeg vlak (12-11-2026)

Melding gebruiker (vergrootglas bij een artikel in Bestellingen): de foto was klein en het venster had een groot leeg
vlak. `Components/ArtikelVenster.razor` + app.css. Twee oorzaken: (1) `.cmdk-overlay` is een flexrij zonder
`align-items`, dus het venster werd uitgerekt tot de volle hoogte: `.artikelvenster` heeft nu `align-self: flex-start`
(zo hoog als de inhoud, max 86vh). (2) Verderop in app.css staat een tweede `.av-foto` (9 x 7 rem, van het
voorraadscherm) die de foto klein hield: de regels van het venster beginnen nu met `.artikelvenster`. De foto staat
over de volle breedte (venster 56rem, foto max 26rem hoog, `object-fit: contain`) met de omschrijving eronder; klik =
de foto in een nieuw tabblad. Lokaal bekeken in Bestellingen (artikel 56466: foto 864 x 416 in plaats van ca. 140 breed).

## Projecten: tabblad Documenten, zoals in Access (12-11-2026)

Wens gebruiker: het tabblad Documenten uit Access terug, met het bestaande type; alleen bij projecten (werkorders
hadden het in Access niet). `Components/ProjectDocumenten.razor` op Projecten ▸ project ▸ tab **Documenten (n)**
(`DocumentTab`, `TelDocumenten`): lijst (klik = `BestandVoorbeeld`, ⭳, ✕ met bevestiging) en een sleepvak. Geen script.

- **Opslag zoals Access** (`Form_Projects Subform Project`): FileStore **StoreType 20**, MasterID = `Project-Id`. Op
  12-11-2026 stonden er 14 documenten bij 9 projecten (project-id 696 t/m 1768); die staan er dus meteen.
- **LET OP, soort 20 is dubbel gebruikt**: in `FileStoreType` heet 20 "inkomende PO" en Access (en de app:
  `IProjectBudgetRepository.StoreTypePoKlant`) bewaart daar ook de PDF van een PO klant, met MasterID =
  `PO_inkomendID` (145 bestanden, nummers 3 t/m 152). Een project met een nummer dat ook een PO-nummer is zou de
  PDF van die PO tonen. Dat zijn nu alleen de 141 projecten van vóór 2011 (allemaal gesloten; laagste open project is
  403). Daarom `IProjectRepository.IsOokPoKlantAsync(projectId)`: bestaat er een PO klant met dat nummer, dan toont
  het tabblad niets van soort 20, telt het niet en kan er niets bij (uitleg op het scherm).
  **Omgekeerd is er nog geen bescherming**: zodra de PO-nummers een project-id met documenten bereiken (696; bij ruim
  20 PO's per jaar duurt dat tientallen jaren) zou de PO-kant een projectdocument als zijn PDF zien.
- **Eigen soort 21** (verzoek gebruiker later op 12-11-2026): `ProjectDocumenten_MariaDB.sql` voegt FileStoreType 21
  "Projectdocument" toe en zet de bestaande projectdocumenten om (soort 20, er is een project met dat nummer en het
  nummer ligt **boven het hoogste PO-nummer**; op 12-11 de 14 bestanden). Niet "er bestaat geen PO met dat nummer":
  PO 10, 22, 58 en 59 zijn verwijderd maar hun PDF staat er nog, en bij 22, 58 en 59 bestaat ook een oud project.
  Om dezelfde reden kijkt `IsOokPoKlantAsync` naar het bereik (`PO_inkomendID >= project-id`). `IProjectRepository.ProjectDocumentSoortAsync` (`ProjectDocumentSoort.Oud` 20 /
  `.Eigen` 21): **bestaat soort 21 in FileStoreType, dan gebruikt het tabblad alleen 21** (lezen, tellen, toevoegen) en
  vervalt de controle op een PO klant; tot dan werkt alles met 20 zoals hierboven. Het script en de code kunnen dus in
  elke volgorde live. Na het script toont Access deze documenten niet meer op zijn projecttabblad. De PO-kant
  (`StoreTypePoKlant` 20) is ongewijzigd en ziet dan alleen nog PO-PDF's. Gebouwd; het script is niet gedraaid en de
  stand ná het script is niet getest.
- Lokaal bekeken, alleen lezend, tegen de echte gegevens: project 223093 (id 1647) toont zijn 3 documenten uit Access;
  een oud project (id 100) toont de uitleg. **Niet getest**: toevoegen, verwijderen en het voorbeeld (zou in de echte
  filestore schrijven of lezen).

## Projectfoto's vanaf de telefoon (12-11-2026)

Wens gebruiker: eerst een project kiezen, een onderwerp ingeven voor de foto's van die sessie, daarna foto's maken die
het project ingaan. Besluiten: kiezen tussen **werkorder of project** (werkorder staat voor), iedereen die is aangemeld
mag maken en zien, weghalen alleen de maker en Directie, 3000 px, een optionele notitie per foto. Script (gebruiker):
`ProjectFotos_MariaDB.sql` (tabel `ProjectFotos`, FileStoreType **370** Projectfoto). Projecten hadden tot nu toe geen
bestanden in de app: dit is de eerste plek.

- **Opslag**: `Services/IProjectFotoRepository.cs` (MySql, Geen, en `GeheugenProjectFotoRepository` met
  `ProjectFotos__Geheugen=true`: zoeken gaat dan echt, de foto's blijven in het geheugen). Een regel per foto: project,
  eventueel werkorder (een foto bij een werkorder hoort ook bij het project), onderwerp, notitie, `FileStoreID` en een
  klein voorbeeld (`Miniatuur`, MEDIUMBLOB, 400 px). De foto zelf: FileStore soort 370, MasterID = `ProjectFotoID`,
  naam "<nummer> <onderwerp> <datum tijd>.jpg". Voorbeeld via `/projectfoto/{id}/klein` (Program.cs, aangemeld, een dag
  in de cache); groot via `/bestand/{FileStoreID}`.
- **Telefoon**: `Components/Pages/MobielProjectFoto.razor` (`/mobiel/projectfoto`, MDE mobiel ▸ Foto ▸ Projectfoto's).
  Stap 1 werkorder of project: "recent door jou gebruikt" (`IUrenRepository.RecentGebruikteWerkordersAsync` →
  `DoelenVanWerkordersAsync`; bij Project de projecten daarvan) of zoeken op alle woorden in nummer, omschrijving en
  klant (alleen open). Stap 2 onderwerp, met de eerdere onderwerpen van dat project als knop (zelfde schrijfwijze).
  Stap 3 foto's: elke foto meteen opgeslagen (`VoegToeAsync` → `BewaarBestand`; mislukt het bestand, dan gaat de regel
  weer weg), met in de lijst een notitieveld en "Weg". Keuze en onderwerp worden voor vandaag op het toestel onthouden
  (`mdeOpslag`, sleutel `mde-projectfoto`).
- **Grote app**: `Components/ProjectFotoLijst.razor` op Projecten ▸ project ▸ tab **Foto's (n)** (`FotoTab`,
  `TelFotos`): per onderwerp (nieuwste eerst), filter op werkorder, klik = groot, ⭳ downloaden; notitie aanpassen en ✕
  weghalen voor de maker en Directie.
- **Menu**: `/mobiel/fotos` is een derde menu in Mobiel.razor (Projectfoto's, Voor de pc); `/mobiel/foto` heet nu
  "Foto voor de pc" en FotoVak verwijst naar "MDE mobiel ▸ Foto ▸ Voor de pc".
- Getest lokaal op telefoonformaat met `ProjectFotos__Geheugen=true` (scratchpad `d9_tijdelijk.py`: zonder aanmelding
  en zonder filestore): menu, zoeken tegen de echte database (226099-01), onderwerp, twee foto's, notitie, onthouden op
  het toestel, en daarna het tabblad Foto's met de voorbeelden. **Niet getest**: de echte tabel en het script, het
  bestand in de filestore (groot bekijken, downloaden, weghalen), "recent door jou gebruikt" (lokaal geen werknemer),
  een echte telefoon en het donkere thema.

## Calculaties: importeren uit de calculatieboom (12-11-2026, verzoek Edwin)

Een offline calculatie (artifact Calculatieboom, export `{"soort":"mde-calculatieboom","versie":1,"calculatie":{id, meta,
inst, groepen, regels, bevroren}}`) heeft dezelfde opbouw als `Calculaties.Data` (`inst`, `groepen`, `regels`; groep met
`aantal`, `notes`). `CalculatieData.UitExport` (Services/ICalculatieRepository.cs) haalt die drie eruit (een bestand met
alleen de boom mag ook), controleert nummers en groepverwijzingen en geeft de naam uit `meta`. `CalculatieData.Totalen`
rekent kost/verkoop/uren zoals `bereken()` in calculatieboom.html (groepsaantal, `keer`, uitsplitsing, eigen opslag),
zodat de lijst meteen klopt (`MaakAsync`, dan `BewaarAsync` met versie 1). Pagina Calculaties, blok "Nog geen
calculatie": bestand kiezen → samenvatting → Importeren. Alleen bij een offerte zonder calculatie.
**Wissen en exporteren** (wens gebruiker, later op 12-11-2026): `ICalculatieRepository.WisAsync` (alleen concept en
zonder regels in `CalculatieWerkorders`; echt DELETE, geen prullenbak) met knop 🗑 Wissen… in de kop van de pagina
(vraag, daarna staat het blok "Nog geen calculatie" er weer); `GET /calculaties/api/{offerte}/export` geeft het
JSON-bestand (`CalculatieData.NaarExport`, zelfde formaat) via de link ⭳ Exporteren. Kringloop lokaal getest met
Geheugen: importeren → exporteren → wissen → de export weer importeren, zelfde totalen. Getest: bestand van Edwin (M-Pede casepacker cel, 9 groepen, 24
regels, kost 645.894,90, verkoop 1.024.389,31, 3.750 uur; met de hand nagerekend), foute bestanden, en de hele import
lokaal met `Calculaties__Geheugen=true`. Niet getest tegen de echte tabel.
