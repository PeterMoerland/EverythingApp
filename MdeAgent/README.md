# MDE Werkplek-agent

Lokale hulpdienst op productie-pc's (testrig, programmeerplek) voor de
EverythingApp. De app draait op de server en kan niet bij de hardware van een
werkplek; de agent draait *op* die pc als Windows-service en luistert alleen op
`http://127.0.0.1:5055`. De browser op die pc roept hem aan vanuit
*Productie ▸ Test PCB*.

Taken (uitbreidbaar, zie `mde_agent/taken/`):

| Taak              | Doet                                              | Vereist op de pc                          |
|-------------------|---------------------------------------------------|-------------------------------------------|
| `drives.uitlezen` | EtherCAT-drives (Everest/Capitan): serienummers   | Npcap (WinPcap-modus), MotionLab3 dicht   |
| `esp32.flashen`   | ESP32 programmeren via USB-UART (esptool)         | USB-UART-driver (CP210x/CH340) — meestal al aanwezig |
| `stm32.flashen`   | STM32 programmeren via ST-LINK                    | ST-LINK-driver; STM32CubeProgrammer is optioneel (anders pyocd, ingebakken) |
| `drives.parameters` | Parameterset (.xcf) in een drive laden, opslaan en terugkijken (ingeniamotion) | Npcap; dictionary (.xdf) van de firmware, via de app |
| `drives.firmware` | Drive-firmware (.lfu) via EtherCAT/FoE laden (ingeniamotion) | Npcap; duurt minuten; `drive_boot_in_app` in agent.json |
| `test.uitvoeren`  | Een test op de print (basistest, IO-test; meer in `taken/test.py`) | — |
| `io.zet`          | Digitale uitgangen van een drive zetten/teruglezen (0x2602/0x2601) | Npcap |
| `feetech.uitlezen` | Feetech-servo (STS3215) uitlezen: ID's op de bus, versie, alle registers | USB-TTL-adapter (FE-URT-1), `feetech_poort` optioneel |
| `feetech.parameters` | Feetech-servo programmeren met een JSON-parameterset (zie `voorbeelden/feetech-sts3215.json`), terugleescontrole, ID/baud als laatste | idem |

## Snelste weg

- **Laptop:** dubbelklik `bouw.bat` (kopieert naar `D:\@@@@\MdeAgent`, bouwt, zet de installer in `uitvoer\` op de share).
- **Werkplek-pc:** dubbelklik `installeer.bat` op de share (vraagt admin, installeert stil, toont versie/token/health).
  Eerste keer met token: `installeer.bat MijnToken`. Bijwerken: gewoon nog eens dubbelklikken.

## Installeren op een werkplek (met de hand)

1. Draai `MdeAgent-setup-<versie>.exe` als administrator.
2. Vul op de tokenpagina het token in dat in de app staat (`Productie:AgentToken`
   in appsettings). Alle werkplekken mogen hetzelfde token gebruiken.
3. De installer zet Npcap erbij als hij ontbreekt (mits `npcap-*.exe` was meegeleverd),
   registreert de service **MDE Werkplek-agent** (automatisch starten, LocalSystem) en start hem.
4. Controle: open op die pc <http://127.0.0.1:5055/health>. Je ziet versie, pc-naam en
   per taak of hij beschikbaar is (en anders waarom niet).

Instellingen: `C:\ProgramData\MDE\Agent\agent.json`; log: `...\Agent\log\agent.log`.
Meestal hoef je niets in te stellen — de EtherCAT-adapter wordt gezocht en onthouden,
de COM-poort van de ESP32 wordt herkend, STM32CubeProgrammer wordt gevonden als hij er is.

## Bouwen (ontwikkel-pc)

```powershell
cd MdeAgent
py -3.12 -m venv .venv
.venv\Scripts\pip install -r requirements.txt
.venv\Scripts\pip install pillow          # alleen voor het icoon
.\build.ps1                               # → dist\MdeAgent\ en uitvoer\MdeAgent-setup-x.y.z.exe
```

Inno Setup 6 (gratis, jrsoftware.org) is nodig voor de installer; zonder krijg je alleen
de map `dist\MdeAgent\` die je ook met de hand kunt kopiëren en registreren:

```
MdeAgent.exe --service install
MdeAgent.exe --service start
```

Tijdens ontwikkelen zonder te bouwen: `.venv\Scripts\python start.py` (console, Ctrl+C stopt).

Nieuwe versie: `VERSIE` in `mde_agent/__init__.py` ophogen, `build.ps1`, installer
over de oude heen draaien (stopt en vervangt de service).

## API

```
GET  /health                     {"ok":true,"versie":"1.0.0","pc":"TESTRIG","taken":{...},"bezig":null}
GET  /adapters                   netwerkadapters
GET  /poorten                    COM-poorten
GET  /drives[?ifname=]           {"drives":[...],"errors":[...],"adapter":"..."}
POST /taken/esp32.flashen        {"firmware":"http://…/api/firmware/<id>","offset":"0x0","wissen":false}
POST /taken/stm32.flashen        {"firmware":"http://…/api/firmware/<id>","adres":"0x08000000"}
POST /taken/drives.uitlezen      {}
GET  /jobs/<id>?vanaf=<n>        {"stand":"bezig|klaar|mislukt","procent":42,"regels":[...],"resultaat":{...}}
```

Alles behalve `/health` vraagt het token: kop `X-Agent-Token` (of `?token=`).
CORS wordt alleen beantwoord voor de origins in `toegestaan`.

## Firmware-bestanden

De app zet de firmware kort klaar op `/api/firmware/<guid>` (een uur geldig) en geeft
die URL aan de agent, die hem via het LAN ophaalt. ESP32: bij voorkeur één samengevoegd
`.bin` op offset `0x0` (in ESP-IDF: `idf.py merge-bin`, in PlatformIO/Arduino: esptool
`merge_bin`); losse delen kunnen ook via `bestanden: [{firmware, offset}, …]`.
STM32: `.hex` of `.elf` (adres zit erin) of `.bin` met `adres`.

## Resultaten in de app

- Drives: per as `Serienummer <as>` en `Drive <as>` als attribuut + historieregel.
- ESP32: `MAC` (uniek per chip) + historieregel "Firmware geladen".
- STM32: `UID` (96-bits uniek id) + historieregel.
