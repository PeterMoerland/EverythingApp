# Activity board API

Met deze API kan een programma buiten de browser het Activity board lezen en bijwerken. Ze is gemaakt voor Claude Code
op een pc binnen het MDE-netwerk (punt van Gerard, 19-11-2026): "zet deze punten uit het overleg op board X", "wat
staat er voor mij open", "zet kaart 412 op testen".

Geef dit bestand aan Claude Code, samen met het adres van de app en je sleutel.

## Toegang

- **Adres:** het adres van de MDE-app, met `/api/activityboard` erachter. Alleen bereikbaar binnen het MDE-netwerk.
- **Sleutel:** elke aanroep stuurt je eigen sleutel mee in de kop `X-Sleutel`. De sleutel hoort bij één werknemer:
  alles wat ermee gebeurt staat op jouw naam (initiator van een nieuwe kaart, de melding die een collega krijgt).
  Je vindt hem in de app bij **Werknemers** ▸ je eigen naam ▸ tabblad **API-sleutel** (Tonen of Kopiëren). Alleen
  jij ziet dat tabblad.
  Behandel de sleutel als een wachtwoord: niet in een gedeeld bestand, niet in Git, niet in een chat.
- Zonder geldige sleutel: `401`. Staat de API uit: `404`.

Zet adres en sleutel in omgevingsvariabelen, dan staan ze niet in je opdrachten:

```powershell
$env:MDE_APP = "http://<adres van de app>"
$env:MDE_SLEUTEL = "<jouw sleutel>"
```

Proberen of het werkt (laat zien wie je volgens de sleutel bent, en alle statussen en velden):

```powershell
Invoke-RestMethod "$env:MDE_APP/api/activityboard" -Headers @{ "X-Sleutel" = $env:MDE_SLEUTEL }
```

## Adressen

| Wat | Aanroep |
| --- | --- |
| Uitleg, wie ben ik | `GET /api/activityboard` |
| Boards zoeken | `GET /api/activityboard/boards?zoek=paprika` |
| Board maken | `POST /api/activityboard/boards` |
| Board hernoemen | `PATCH /api/activityboard/boards/{boardId}` |
| Eén board lezen | `GET /api/activityboard/boards/{boardId}` |
| Wat staat er voor mij open | `GET /api/activityboard/mijn` |
| Activiteiten zoeken op titel | `GET /api/activityboard/zoek?q=kabelgoot` |
| Personen (om toe te wijzen) | `GET /api/activityboard/personen` |
| Eén activiteit lezen | `GET /api/activityboard/activiteiten/{id}` |
| Activiteit(en) aanmaken | `POST /api/activityboard/boards/{boardId}/activiteiten` |
| Activiteit wijzigen | `PATCH /api/activityboard/activiteiten/{id}` |
| Onderwerp toevoegen | `POST /api/activityboard/boards/{boardId}/onderwerpen` |
| Type toevoegen | `POST /api/activityboard/boards/{boardId}/types` |
| Punten op de afvinklijst erbij | `POST /api/activityboard/activiteiten/{id}/checklist` |
| Punt afvinken, uitvinken of de tekst aanpassen | `PATCH /api/activityboard/checklist/{puntId}` |
| Punt of kopje van de afvinklijst weghalen | `DELETE /api/activityboard/checklist/{puntId}` |

Bij het lezen van een board kun je de lijst inperken: `?open=true` (alleen wat nog niet klaar is), `?mijn=true` (alleen
waar ik aan toegewezen ben), `?status=bezig`. De lange tekstvelden komen alleen mee met `?tekst=true`; bij het lezen
van één activiteit altijd. `/mijn?persoon=Gerard` geeft de open activiteiten van een collega.

Zoeken gaat over alle boards en kijkt in de titel; elk woord moet voorkomen. Erbij kan `&open=true`, `&board=12` (alleen
dat board) en `&overal=true` (ook in de tekstvelden). Hoogstens 100 treffers.

Een veld dat niet is ingevuld (geen prioriteit, geen vervaldatum) staat niet in het antwoord.

**Een board maken** kan op vier manieren; geef er precies één:

| Soort | Inhoud |
| --- | --- |
| Vrij board | `{ "naam": "Robot software" }` |
| Bij een project | `{ "project": 226102 }` (het projectnummer) |
| Bij een werkorder | `{ "werkorder": "…" }` (het werkordernummer, precies zoals in de app) |
| Bij een klant | `{ "klant": "…" }` (de naam bij Relaties; moet één klant opleveren) |

Bij een project, werkorder of klant mag je `naam` meegeven als eigen naam van het board. Er is één board per project,
werkorder en klant, en een vrij board met dezelfde naam wordt niet nog eens gemaakt: bestaat het al, dan krijg je dat
board terug met `bestond: true` en verandert er niets.

**Hernoemen:** `{ "naam": "…" }`. Met `null` krijgt een board bij een project, werkorder of klant weer de standaardnaam;
een vrij board houdt altijd een naam.

**Niet via de API:** verwijderen (activiteit, onderwerp, type, board), een bestaand board aan iets anders koppelen, de
volgorde binnen een vak en de volgorde van een afvinklijst. Dat doe je in de app. Een activiteit die niet meer hoeft, zet je op `geannuleerd`.

## Velden van een activiteit

| Veld | Waarde |
| --- | --- |
| `titel` | tekst, verplicht bij aanmaken, hoogstens 300 tekens |
| `status` | `backlog` (Voorstel), `next` (Geaccepteerd), `bezig` (In uitvoering), `testen`, `beoordeling` (Ter beoordeling), `opgelost`, `nietopgelost` (Wordt niet opgelost), `geannuleerd`. De naam mag ook. Zonder status: Voorstel |
| `prioriteit` | `hoog`, `gemiddeld`, `laag` (of 1, 2, 3); `null` haalt hem weg |
| `vervaldatum` | `"2026-12-31"`; `null` haalt hem weg |
| `benodigdeTijd` | uren als getal (`1.5`) of tekst: `"45m"`, `"1:30"`, `"2u"`, `"2d"` (een dag is 8 uur); `null` haalt hem weg |
| `onderwerpen` | lijst van namen of ids van onderwerpen **van dit board** |
| `types` | lijst van namen of ids van types **van dit board** |
| `personen` | lijst van namen of ids; `"ik"` ben jezelf. Een deel van de naam mag als het er maar één oplevert (`"Gerard"`) |
| `samenvatting`, `details`, `impact`, `genomenStappen`, `volgendeStappen`, `opmerkingen` | de zes tekstvelden; een lege tekst maakt het veld leeg |
| `checklist` | alleen bij aanmaken: de afvinklijst, zie [Afvinklijst](#afvinklijst) |
| `opmaak` | `true` of `false`: tonen de tekstvelden Markdown? Zie [Opmaak](#opmaak-in-de-tekstvelden) |

Regels:

- Bij **wijzigen** verandert alleen wat je meegeeft. Een lijst (`onderwerpen`, `types`, `personen`) **vervangt** de
  bestaande lijst: wil je iemand toevoegen, lees dan eerst de kaart en stuur de hele lijst.
- Een onderwerp of type dat nog niet op het board staat geeft een fout met de namen die er wel zijn. Voeg het eerst
  toe; bestaat de naam al, dan krijg je het bestaande terug (`bestond: true`).
- Een onbekend veld geeft een fout in plaats van stil te worden overgeslagen.
- Wie je toewijst krijgt een melding in de bel van de app, net als bij toewijzen in het scherm.
- Staat het board bij een collega open, dan ziet die de wijziging meteen.
- Fouten komen terug als `{ "fout": "…" }` met code 400 (invoer), 401 (sleutel), 404 (bestaat niet) of 500.
- **Tijdens het aftellen naar een nieuwe versie is de API dicht:** zolang de balk "nieuwe versie" in de app loopt, geeft
  elke aanroep `503` met `"onderhoud": true`, het tijdstip in de tekst en `opnieuwNaSeconden` (ook als kop
  `Retry-After`). Er is dan niets gelezen of gewijzigd. Wacht die tijd en probeer het opnieuw.

## Opmaak in de tekstvelden

De zes tekstvelden blijven gewone tekst; er mag **Markdown** in staan. De app toont die als opmaak als `opmaak` van het
kaartje `true` is:

- kopjes (`# Kop`, `## Kopje`), **vet** (`**vet**`) en *cursief* (`*cursief*`)
- opsommingen (`- punt`) en genummerde lijsten (`1. punt`)
- code in een regel (`` `Status.Joint[n]` ``) en codeblokken (drie backticks ervoor en erna)
- links (`[tekst](https://…)`, of een los adres)
- een enkele regelovergang blijft een regelovergang

Regels:

- Een kaartje dat je via de API **aanmaakt** krijgt `opmaak: true`, tenzij je `"opmaak": false` meegeeft.
- Kaartjes van vóór deze uitbreiding staan op `false` en blijven platte tekst, precies zoals ze waren. Aanzetten kan
  met `PATCH /activiteiten/{id}` en `{ "opmaak": true }`, of in de pop-up met het vinkje Opmaak.
- `GET /activiteiten/{id}` geeft `opmaak` terug.
- HTML in de tekst wordt niet uitgevoerd maar als tekst getoond. Een link mag alleen naar `http`, `https` of `mailto`;
  een andere link komt als gewone tekst in beeld. Een plaatje (`![…](…)`) wordt een gewone link.

## Afvinklijst

Een activiteit kan een afvinklijst hebben: punten om af te vinken, zo nodig onder kopjes. Hoogstens 300 regels (kopjes
en punten samen) per activiteit, een punt hoogstens 500 tekens.

**Lezen.** `GET /activiteiten/{id}` geeft `checklist` en `voortgang`. In lijsten (een board, `/mijn`, `/zoek`) staat
alleen `voortgang`, bijvoorbeeld `"12/35"`.

```json
"voortgang": "1/3",
"checklist": [
  { "punten": [ { "id": 102, "tekst": "Los punt zonder kopje", "klaar": false } ] },
  { "kop": "Loggen", "kopId": 103, "punten": [
      { "id": 104, "tekst": "Sampletijd 2 ms houden", "klaar": false },
      { "id": 106, "tekst": "Logbestand bewaren", "klaar": true, "klaarOp": "2026-11-20 15:00", "klaarDoor": "Gerard Quaak" } ] }
]
```

**Bij aanmaken** mag `checklist` mee, in dezelfde vorm (zonder ids). Een punt mag ook een losse tekst zijn. Punten
zonder kopje staan vooraan in de lijst.

```json
{ "titel": "Astest: eenmalige volledige robottest",
  "checklist": [
    { "kop": "Loggen", "punten": [ { "tekst": "Sampletijd 2 ms houden", "klaar": false }, "Logbestand bewaren" ] },
    { "kop": "Testbewegingen", "punten": [ "Joint 1", "Joint 2" ] } ] }
```

**Daarna** gaat het per punt (`checklist` in een `PATCH` van de activiteit geeft een fout):

| Doel | Aanroep |
| --- | --- |
| Punten erbij | `POST /api/activityboard/activiteiten/{id}/checklist` met `{ "tekst": "…", "kop": "Loggen" }`, een groep `{ "kop": "…", "punten": [ … ] }`, of een lijst daarvan |
| Afvinken of uitvinken | `PATCH /api/activityboard/checklist/{puntId}` met `{ "klaar": true }` of `{ "klaar": false }` |
| Tekst aanpassen | `PATCH /api/activityboard/checklist/{puntId}` met `{ "tekst": "…" }` (ook voor een kopje, met zijn `kopId`) |
| Punt of kopje weghalen | `DELETE /api/activityboard/checklist/{puntId}` |

- Een nieuw punt komt onderaan het kopje met die naam (hoofdletters tellen niet). Bestaat het kopje niet, dan komt het
  onderaan de lijst erbij. Een punt zonder `kop` komt bij de punten zonder kopje, vooraan.
- Bij afvinken worden jouw naam en het tijdstip bewaard (`klaarDoor`, `klaarOp`); uitvinken haalt ze weg.
- Een kopje verwijderen laat de punten staan: ze horen daarna bij het kopje erboven.
- De volgorde veranderen kan alleen in de app (slepen, of Alt+pijl omhoog/omlaag).
- Elk antwoord bevat de activiteit met de lijst zoals hij nu is.

## Voorbeelden

```powershell
$kop = @{ "X-Sleutel" = $env:MDE_SLEUTEL }
$api = "$env:MDE_APP/api/activityboard"

# Welk board?
(Invoke-RestMethod "$api/boards?zoek=paprika" -Headers $kop).boards

# Wat staat er op, zonder wat al klaar is
Invoke-RestMethod "$api/boards/12?open=true" -Headers $kop

# Eén activiteit aanmaken
$nieuw = @{ titel = "Kabelgoot hertekenen"; prioriteit = "hoog"; personen = @("ik"); onderwerpen = @("Mechanisch")
            samenvatting = "De goot botst met de afzuiging." } | ConvertTo-Json
Invoke-RestMethod "$api/boards/12/activiteiten" -Method Post -Headers $kop -ContentType "application/json; charset=utf-8" `
    -Body ([Text.Encoding]::UTF8.GetBytes($nieuw))

# Meerdere tegelijk: een lijst (hoogstens 50). Klopt er één niet, dan wordt er geen enkele aangemaakt.
$lijst = ConvertTo-Json @(
    @{ titel = "Offerte kabels opvragen"; personen = @("Gerard") },
    @{ titel = "Testplan schrijven"; vervaldatum = "2026-12-05"; benodigdeTijd = "4u" })
Invoke-RestMethod "$api/boards/12/activiteiten" -Method Post -Headers $kop -ContentType "application/json; charset=utf-8" `
    -Body ([Text.Encoding]::UTF8.GetBytes($lijst))

# Status wijzigen en een notitie erbij
$wijzig = @{ status = "testen"; volgendeStappen = "Meten op de machine van dinsdag." } | ConvertTo-Json
Invoke-RestMethod "$api/activiteiten/412" -Method Patch -Headers $kop -ContentType "application/json; charset=utf-8" `
    -Body ([Text.Encoding]::UTF8.GetBytes($wijzig))
```

Stuur de inhoud als UTF-8 (zoals hierboven), anders komen tekens als é verkeerd aan.

## Voor de beheerder

De sleutels staan in `appsettings.json` van de app (niet in Git), onder `ActivityBoardApi`:

```json
"ActivityBoardApi": {
  "Sleutels": [
    { "Sleutel": "<minstens 32 willekeurige tekens>", "WerknemerId": 0 }
  ]
}
```

- Eén regel per persoon; `WerknemerId` is het `Werknemer-id` uit Werknemers. De werknemer moet in dienst zijn.
- Een sleutel korter dan 32 tekens telt niet mee. Geen geldige regels = de API staat uit (alles geeft 404).
- Een sleutel maken:

  ```powershell
  -join ((1..48) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) })
  ```

- Intrekken = de regel weghalen. Meestal leest de app het bestand meteen opnieuw in; controleer het met een aanroep
  (de oude sleutel moet 401 geven) en herstart anders de app.
- Elke wijziging via de API komt in het logboek van de app, met de naam van de werknemer (nooit de sleutel).
