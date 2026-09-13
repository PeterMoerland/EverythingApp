# Ureningave — Proof of Concept (Blazor Server)

Web-vervanging van het Access-formulier **Ureningave** (+ subformulier "Ureningave sub"),
uitgebreid met een **projectplanning-module (Gantt)** op `/planning`.
Draait nu op demo-data; voorbereid op de live SQL Server-database.

## Indeling van detailschermen (afspraak voor alle pagina's)

Elk scherm met een lijst links en details rechts gebruikt dezelfde opbouw. Nieuwe
pagina's volgen dit patroon, zodat de app overal hetzelfde aanvoelt:

```razor
<DetailMetZij>
    <Hoofd>
        @* het formulier: velden onder elkaar, leesbare breedte *@
    </Hoofd>
    <Zij>
        <ZijBlok Titel="Contactpersonen" Aantal="_contacten.Count" Meer="KiesContactpersonen"
                 Leeg="_contacten.Count == 0" LeegTekst="Nog geen contactpersonen.">
            <div class="zij-regel">
                <span class="zij-naam">hoofdregel</span>
                <span class="zij-extra">bijzaak, datum, aantal</span>
            </div>
        </ZijBlok>
    </Zij>
</DetailMetZij>
```

Regels van de afspraak:

- De velden blijven **onder elkaar** in één kolom; de vrije ruimte gaat naar het
  zijpaneel, niet naar een tweede kolom velden.
- In het zijpaneel staat wat je bij dit record het vaakst nodig hebt, in korte
  regels — geen invoervelden.
- Het aantal achter de kop is een knop naar het volledige tabblad (`Meer`).
- Een regel die doorklikt is een `<button class="zij-naam zij-knop">`, geen link,
  zodat de selectie in beeld blijft.
- Wordt het venster smal, dan schuift het zijpaneel automatisch onder het formulier.
- Laat je `<Zij>` weg, dan gebruikt het formulier de volle breedte.

Wat er nu in staat: bij **Relaties** de contactpersonen en de laatste documenten,
bij **Werknemers** de pasfoto, de huidige functie met dienstjaren en de laatste
loopbaangebeurtenissen, bij **Projecten** de werkorders met hun deadline en de
klant — en bij een werkorder het project plus de andere werkorders.

## Navigatie-shell

De app heeft nu de navigatiestructuur voor de volledige migratie:

- **Sidebar** met de MDE-modules (Uren, Artikelen, Orders & Offertes, Facturen,
  Projecten & Werkorders, Financieel, Beheer). Gemigreerde schermen werken;
  de rest toont een nette placeholder ("nog niet gemigreerd") — precies hoe de
  gefaseerde migratie eruit gaat zien
- **Ctrl+K zoekpalet**: typ een paginanaam, werkordernummer of werknemersnaam en
  spring er direct heen — het palet zoekt live in de database, dit vervangt het
  Access-lint. Enter opent het eerste resultaat, Escape sluit
- **Recent**-sectie in de sidebar: de laatst bezochte pagina's
- Rolgebaseerd tonen/verbergen van modules kan later per gebruiker
  (zelfde principe als de Directie-vlag in Ureningave)

## 3D-viewer (STEP/STL)

Onder "Extra" in de sidebar, of via Ctrl+K. Toont bij openen een procedureel demo-onderdeel;
via "Open bestand" zijn eigen .stl- en .step/.stp-bestanden te bekijken (draaien/zoomen).
STEP wordt in de browser geparsed met OpenCascade via WebAssembly (occt-import-js);
three.js en de CAD-kernel worden bij eerste gebruik van CDN geladen (internet nodig).
In productie: koppelen aan de filestore (3D-model bij artikel/werkorder) en grote
assemblies server-side voorconverteren naar glTF.

## Dymo-labels bij artikelen

Op de Info-tab van een artikel staat een balk om een label te printen. Het printen zelf
doet **DYMO Connect** op de pc van de gebruiker; die moet daar dus geïnstalleerd zijn.
De browser praat rechtstreeks met de webservice van DYMO op `https://127.0.0.1:41951`
(Chrome of Edge — Firefox vertrouwt het certificaat van DYMO niet). Werkt het niet,
gebruik dan de knop **Diagnose**: die laat per adres zien wat DYMO antwoordt.

De opmaak van de labels komt uit `Templates\DymoKlein.dymo` (99017, 12 × 50 mm) en
`Templates\DymoGroot.dymo` (11356, 41 × 89 mm). Dat zijn gewone labelbestanden, gemaakt
in DYMO Connect. **Wil je de indeling wijzigen, doe dat dan in DYMO Connect en bewaar
het bestand terug** — er hoeft niets aan de app te veranderen. De app vult in, op
volgorde van voorkomen in het bestand:

| in het sjabloon | wordt gevuld met |
| --- | --- |
| de barcode | `MDE` + het artikelnummer |
| het eerste tekstvak | de artikelcode |
| het tweede tekstvak | de omschrijving, over meerdere regels verdeeld |
| de afbeelding | de foto van het artikel; is die er niet, dan valt het vak weg |

Een ander pad kan via `Labels:KleinSjabloon` / `Labels:GrootSjabloon` in appsettings.
Ontbreekt het sjabloon, dan tekent de app het label zelf — dat is een terugval, niet
de bedoelde weg. `Labels:Formaat` staat op `connect`; op een pc met het oudere
DYMO Label v8 moet dat `dls8` zijn (en werkt het sjabloon niet, want dat is een
DYMO Connect-bestand).

## Orderbatch en scannen met de telefoon

De orderbatch (`/orderbatch`) is het wachtlijstje van alles wat besteld moet worden.
Vullen kan met de telefoon via `/scannen`: camera aan, de barcode op het Dymo-label
scannen (`MDE12345`), aantal kiezen — afgerond op hele verpakkingen van de
voorkeursleverancier — en toevoegen. Iedereen bestelt op eigen naam; een regel via
de scanner krijgt een 📷 in het overzicht. Een samenstelling wordt, net als in
Access, uiteengelegd in zijn onderdelen. Het maken van inkooporders uit de batch
volgt later; tot die tijd gebeurt dat in Access (dezelfde tabel).

**De camera werkt alleen via https.** Zolang de app op http draait kan er op de
scanpagina wel getypt worden, en werkt een bluetooth-scanner (die gedraagt zich als
toetsenbord), maar blijft de camera uit. De app draait intern (buiten de zaak via
VPN), dus het eenvoudigst is Caddy zijn eigen certificaat te laten maken:

```
everything.mde-automation.nl {
    tls internal
    handle_path /mde/* {
        reverse_proxy 172.16.1.13:5010
    }
}
```

Let op drie dingen. Eén: telefoons en pc's vertrouwen dat certificaat pas na het
eenmalig installeren van Caddy's basiscertificaat (`caddy trust`, of het bestand
`root.crt` uit Caddy's datamap op het toestel zetten) — zonder dat krijg je een
waarschuwing die je kunt wegklikken, waarna de camera het gewoon doet. Twee: in de
Google Cloud Console moet de redirect-URL van de inlog op `https://` worden gezet.
Drie: de app leest `X-Forwarded-Proto` al (ForwardedHeaders in Program.cs), dus aan
de app zelf hoeft niets te veranderen. Later omschakelen naar een echt
Let's Encrypt-certificaat is één regel in de Caddyfile.

## Facturen automatisch laten uitlezen

Bij het toevoegen van een inkomende factuur kies je de PDF en druk je op
**✨ Laat uitlezen**. De velden worden ingevuld en **geel gemarkeerd**: door de
computer ingevuld, controleer ze. Zodra je een veld zelf aanpast verdwijnt de
markering. Er wordt nooit iets opgeslagen zonder dat je op Opslaan drukt.

Twee extra controles die de app zelf doet, los van het model:

- **De IBAN op de factuur wordt vergeleken** met die bij de leverancier in Relaties.
  Wijkt hij af, dan volgt een expliciete waarschuwing — een gewijzigd rekeningnummer
  is de bekendste vorm van factuurfraude.
- **Staat er alleen een totaalbedrag** (typisch bij een kassabon), dan wordt met 21%
  teruggerekend, met de melding dat je het tarief moet nakijken.

### Waar het model draait

`Ai:Bron` in appsettings bepaalt dat: `ollama` (eigen server) of `claude` (cloud).

De app kiest zelf de goedkoopste weg. Een digitale PDF — alles wat per mail
binnenkomt — heeft al een tekstlaag; die halen we er zelf uit met PdfPig en dan hoeft
het model alleen nog te bepalen welk getal waar hoort. Dat kan een klein model. Pas
bij een scan of een foto van een bon moet er echt gekeken worden, en daarvoor is een
model nodig dat beelden begrijpt.

**Ollama op de server instellen:**

```
ollama pull qwen2.5:14b     (leest de tekst van digitale PDF's)
ollama pull qwen2.5vl:7b    (alleen nodig voor scans en foto's)
```

Zonder videokaart werkt het ook, maar reken op een halve minuut per factuur. Met
16 GB videogeheugen passen beide modellen; met 24 GB blijven ze samen ingeladen, wat
uitmaakt zodra er op de achtergrond een mailbox wordt meegelezen.

**Bij de clouddienst:** zet de sleutel als omgevingsvariabele `Ai__Sleutel` op het
serviceaccount, niet in appsettings.json — dat bestand is voor iedereen op de share
leesbaar en gaat mee bij elke uitrol. Bedenk ook dat inkoopfacturen persoonsgegevens
bevatten en naar een verwerker buiten de EU gaan; leg daar een verwerkersovereenkomst
voor vast voordat je het in productie gebruikt.

## Camera's (Gebouwbeheer)

`/gebouw/cameras` toont de camera's naast elkaar; klikken maakt er één groot. Het
verversingstempo is per pc in te stellen (1 tot 15 seconden) en wordt onthouden.

De camera's staan in `appsettings.json` onder `Cameras`: naam, adres (`host:poort`,
Foscam gebruikt meestal 88), gebruiker en wachtwoord. `Snapshot` leeg laten voor
Foscam; een ander merk vul je in met `{gebruiker}` en `{wachtwoord}` als
plaatshouders in het pad.

**De app haalt de beelden op, niet de browser.** Dat is bewust: zo blijft het
camerawachtwoord op de server in plaats van in het adres van elk beeld, hoeven de
camera's niet bereikbaar te zijn vanaf de werkplekken, en blijven de beelden werken
zodra de app op https draait terwijl de camera's zelf op http zitten (een browser
weigert die combinatie).

## Volledig scherm

Knop "⛶ Volledig scherm" in de topbalk verbergt sidebar en topbalk en zet de browser
in fullscreen — bijv. voor een planbord in de werkplaats. Verlaten met Esc of ✕.

## Planning-module (Gantt)

Nieuw, zonder Access-tegenhanger — laat zien wat er mogelijk wordt na de migratie:

- Balkenplanning per werknemer per werkorder, 2–8 weken zichtbaar, weeknummers en vandaag-markering
- **Gepland vs. gerealiseerd**: de donkere vulling in elke balk is de voortgang op basis van
  werkelijk geboekte uren uit Ureningave — plan en realisatie komen uit dezelfde database
- Klik op een balk om te bewerken; "+ Nieuwe planning" om toe te voegen
- **Drag & drop**: sleep een balk naar een andere dag (startdag verschuift, duur blijft gelijk)
  of naar de rij van een andere werknemer (planning wordt overgezet). Doelcel licht op
  tijdens het slepen; loslaten slaat direct op
- Zelfgebouwd met CSS-grid en de standaard browser drag & drop-events, geen externe
  componenten. Voor duur-oprekken aan de balkrand of afhankelijkheidspijlen is een
  commercieel component (bijv. Syncfusion Gantt) de snellere route; het datamodel
  (`PlanningItem`) blijft daarbij gelijk
- Bij livegang: nieuwe SQL Server-tabel nodig — CREATE TABLE-voorstel staat in
  `SqlServer/SqlServerUrenRepository.cs`

## Starten

1. Installeer de [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (eenmalig).
2. Open een terminal in deze map en:

```
dotnet run
```

3. Open de getoonde URL in de browser (bijv. `http://localhost:5000`).

Demo-invoer wordt bewaard in `demo-data.json` en overleeft een herstart.

## Als Windows-service (de server)

Voor het dagelijks uitrollen is er één script: **`service-herstarten.bat`**. Dat stopt de
service, wacht tot hij echt stil ligt, publiceert, en start hem weer. Bestaat de service
nog niet, dan maakt hij die aan. Mislukt het publiceren, dan zet hij de oude versie terug
in de lucht. `service-installeren.bat` en `service-verwijderen.bat` zijn er nog voor het
echt weghalen van de service, maar zijn verder niet nodig.

### Toegang tot netwerkmappen

Een service draait standaard onder LocalSystem en heet naar buiten toe `MACHINE$`. Dat
account bestaat alleen op die machine zelf, dus **een netwerkmap als `\\172.16.1.4\data`
weigert de toegang** — foto's bij artikelen, de filestore en de laser-PDF blijven dan leeg,
zonder duidelijke foutmelding.

Draai daarom eenmalig **`service-account.bat`**. Dat script:

1. kent het opgegeven account het recht *Aanmelden als service* toe (zonder dat recht
   weigert de service te starten met foutmelding 1069 — `sc config` alleen is niet genoeg);
2. zet de service op dat account;
3. start hem opnieuw en controleert of hij draait.

Deze server zit in een werkgroep, niet in een domein. Het account moet daarom **op beide
machines bestaan, met exact hetzelfde wachtwoord** — op SQL-SERVER en op 172.16.1.4.
Windows logt dan met naam en wachtwoord in op de share.

Werkt een pad niet, kijk dan in Artikelen bij een artikel op de knop **"Waar zoekt hij?"**:
die laat zien onder welk account de app draait, waar hij kijkt en wat daar staat.

## Wat is overgenomen uit Access (1-op-1)

| Access (VBA) | PoC |
|---|---|
| Weekoverzicht ma–zo met uren per dag, geselecteerde dag gemarkeerd, rode markering bij werkdag in verleden zonder uren (`UpdateWeekoverzicht`, `HaalUrenOp`) | Weekbalk met 7 dagknoppen, zelfde kleurregels |
| Dag/week navigatie, Vandaag-knop, datumkiezer (`dat_up/dat_down/weekplus/weekmin/BtnVandaag/Datepicker`) | Knoppenbalk + native date picker |
| ISO-weekberekening (`DatumVanJaarWeekDag`) | `System.Globalization.ISOWeek` (zelfde ISO-regel, minder code) |
| Urenregels invoeren/wijzigen/verwijderen (subformulier) | Inline bewerkbare tabel |
| Validaties (`Form_BeforeUpdate`): tariefsoort verplicht; km alleen bij Travel; omschrijving verplicht; fixed ⇒ geen uren; werkorder verplicht, geldig en niet gesloten | Identieke controles en meldingen in `UrenService.ValideerAsync` |
| Uren berekenen uit start/eindtijd incl. middernacht-overgang (`berekenuren`, `checkmidnight`); eindtijd uit uren (`uren_AfterUpdate`) | `UrenService.BerekenUren` / `BerekenEindtijd` / `CheckMidnight` |
| Dag kopiëren/plakken (`btnKopieer`/`btnPlak`) | Kopieer dag / Plak dag |
| Woon-werk km directie, 2 boekingen heen/terug, éénmaal per dag (`WoonWerk_Click`) | Woon-werk-knop (alleen zichtbaar voor directie) |
| Vrije uren (verlof/ziek) 8 uur boeken (`KnopVrijeUrenInboeken`) | Vrije-uren keuzelijst + knop |
| Standaardactiviteit werknemer (`Hoofdactiviteit`) | Vooringevuld bij nieuwe regel |

Bewust weggelaten in de PoC: datumprikker-popup (native date input), werkorder-zoekformulieren (nu een keuzelijst), toetsenbord-shortcuts, personal settings (invoermasker).

## Omschakelen naar live SQL Server

1. `dotnet add package Microsoft.Data.SqlClient` (en verwijder `NuGet.config`).
2. Haal de regels `<Compile Remove="SqlServer\**" />` uit `EverythingApp.csproj`.
3. Maak de resterende lookup-methoden af in `SqlServer/SqlServerUrenRepository.cs`
   (patroon staat er al in; kolomnamen verifiëren tegen de echte database).
4. In `Program.cs` de `JsonDemoRepository`-registratie vervangen door
   `SqlServerUrenRepository` met de connection string van de `MDE-Automation`-database.

## Structuur

```
Models/            entiteiten (kolomnamen volgen de Access-tabellen)
Services/          IUrenRepository (data-abstractie), JsonDemoRepository (demo),
                   UrenService (alle VBA-businesslogica, per methode geannoteerd)
Components/Pages/  Ureningave.razor — het formulier zelf
SqlServer/         live-database repository (nog niet gecompileerd)
```
