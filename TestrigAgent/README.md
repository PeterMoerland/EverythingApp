# Testrig-agent — drives uitlezen vanuit Test PCB

> **Vervangen door `MdeAgent/`** (installeerbare Windows-service met ook ESP32/STM32
> flashen). Dit losse scriptje blijft staan als eenvoudige testversie; nieuwe werkplekken
> krijgen de installer uit `MdeAgent/`.

De EverythingApp draait op de server; de EtherCAT-master moet op de testrig-pc
draaien (Npcap + admin). Dit agentje overbrugt dat: het luistert alleen op
`127.0.0.1:5055` op de testrig-pc. De knop **⌁ Serienummer uitlezen** in
*Productie ▸ Test PCB* laat de browser op die pc de agent aanroepen en stuurt het
antwoord naar de app, die de serienummers bij het exemplaar opslaat.

## Installatie op de testrig-pc

1. Zet in één map (bijv. `C:\\tools\\drive-serials\\`):
`drive\_agent.py`, `agent.json`, `start\_agent.bat` en het bestaande
`read\_drive\_serials.py` (met de functie `scan\_identity(ifname)`).
2. Python 3.10+ met `pysoem` (en wat `read\_drive\_serials.py` verder nodig heeft).
3. Npcap geïnstalleerd in WinPcap-compatibele modus.
4. Vul in `agent.json` bij `interface` de pysoem-adapternaam van de EtherCAT-poort
(`\\Device\\NPF\_{...}`). Leeg = eerste gevonden adapter. De namen zie je via
[http://127.0.0.1:5055/interfaces](http://127.0.0.1:5055/interfaces) als de agent draait.
5. Start `start\_agent.bat` (vraagt zelf om administratorrechten). Laat het venster open.
Wil je hem automatisch bij aanmelden starten: Taakplanner, "met hoogste rechten".

## Gebruik

* Open de app in de browser **op de testrig-pc**, ga naar Test PCB, scan het
serienummer en klik **⌁ Serienummer uitlezen**.
* MotionLab3 moet dicht zijn (die claimt de adapter).
* De app verwacht precies zoveel drives als er assen zijn ingesteld
(`Productie:Assen` in appsettings, standaard R-as en Z-as, in volgorde van
slave-positie). Anders: no-go, niets wordt opgeslagen.
* Bij go worden per as `Serienummer <as>` en `Drive <as>` als attributen van het
exemplaar bewaard en komt er een historieregel bij.

## Test zonder app

```
http://127.0.0.1:5055/health
http://127.0.0.1:5055/interfaces
http://127.0.0.1:5055/drives
```

Antwoord van `/drives`:

```json
{"drives":\[{"slave\_position":0,"name":"...","vendor\_id":"0x0000029C",
  "product\_code":"0x...","revision\_number":"0x...","serial\_number":"12345","errors":\[]}],
 "errors":\[],"interface":"\\\\Device\\\\NPF\_{...}"}
```

## Beveiliging

De agent bindt alleen op localhost en beantwoordt CORS alleen voor de adressen in
`toegestaan`. Hij leest alleen; hij schrijft niets naar de drives.

