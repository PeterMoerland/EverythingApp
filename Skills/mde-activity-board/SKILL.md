---
name: mde-activity-board
description: Het MDE Activity board lezen en bijwerken via de API van de MDE-app. Gebruik deze skill als de gebruiker vraagt naar kaartjes, activiteiten of boards van MDE - bijvoorbeeld "wat staat er voor mij open", "zet deze punten op het board van Paprika", "zet kaart 412 op testen", "zoek het kaartje over de kabelgoot" of "wijs dit toe aan Gerard".
---

# MDE Activity board

Je werkt met het Activity board van de MDE-app via een kleine REST-API. De API is alleen bereikbaar binnen het
MDE-netwerk. Alles wat je doet staat op naam van de gebruiker van wie de sleutel is.

## Verbinding

- Adres van de app: omgevingsvariabele `MDE_APP` (bv. `http://server:5000`). De API staat op `$env:MDE_APP/api/activityboard`.
- Sleutel: omgevingsvariabele `MDE_SLEUTEL`, mee te sturen in de kop `X-Sleutel`.
- Ontbreekt een van de twee, vraag de gebruiker dan om hem te zetten. De sleutel staat in de MDE-app bij Werknemers ▸ je
  eigen naam ▸ tabblad "API-sleutel"; het adres is het adres waarop de gebruiker de app opent. **Vraag nooit om de sleutel in de chat te typen,
  toon hem nooit en schrijf hem nooit in een bestand.**

Gebruik PowerShell. Begin een sessie zo:

```powershell
$kop = @{ "X-Sleutel" = $env:MDE_SLEUTEL }; $api = "$env:MDE_APP/api/activityboard"
Invoke-RestMethod "$api" -Headers $kop | ConvertTo-Json -Depth 6
```

Dat eerste antwoord zegt wie je bent (`ik`) en geeft de geldige statussen, prioriteiten, tekstvelden en adressen. Lees
het één keer per sessie; raad geen waarden.

Stuur inhoud altijd als UTF-8-bytes, anders weigert de API tekens als é:

```powershell
$body = @{ titel = "…" } | ConvertTo-Json -Depth 6
Invoke-RestMethod "$api/boards/12/activiteiten" -Method Post -Headers $kop `
    -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($body))
```

Een lijst van één element maak je met `ConvertTo-Json @( @{…} )`, niet via de pijp (die pakt de lijst uit).

## Adressen

| Doel | Aanroep |
| --- | --- |
| Boards zoeken | `GET /boards?zoek=paprika` |
| Board maken | `POST /boards` met `{ "naam": "…" }` (vrij board), of `{ "project": 226102 }`, `{ "werkorder": "nummer" }`, `{ "klant": "naam" }`; `naam` erbij = eigen naam |
| Board hernoemen | `PATCH /boards/{boardId}` met `{ "naam": "…" }` |
| Board lezen (onderwerpen, types, activiteiten) | `GET /boards/{boardId}?open=true` (ook `&mijn=true`, `&status=bezig`, `&tekst=true`) |
| Open activiteiten van mij of een collega | `GET /mijn` of `GET /mijn?persoon=Gerard` |
| Activiteit zoeken op titel | `GET /zoek?q=kabelgoot` (ook `&open=true`, `&board=12`, `&overal=true`) |
| Eén activiteit met alle tekst | `GET /activiteiten/{id}` |
| Personen | `GET /personen` |
| Aanmaken (één object of een lijst, hoogstens 50) | `POST /boards/{boardId}/activiteiten` |
| Wijzigen (alleen wat je meegeeft) | `PATCH /activiteiten/{id}` |
| Onderwerp of type toevoegen | `POST /boards/{boardId}/onderwerpen` of `/types` met `{ "naam": "…" }` |
| Punten op de afvinklijst erbij | `POST /activiteiten/{id}/checklist` met `{ "tekst": "…", "kop": "Loggen" }`, `{ "kop": "…", "punten": [ … ] }` of een lijst daarvan |
| Punt afvinken, uitvinken, tekst aanpassen | `PATCH /checklist/{puntId}` met `{ "klaar": true }`, `{ "klaar": false }` of `{ "tekst": "…" }` |
| Punt of kopje weghalen | `DELETE /checklist/{puntId}` |

## Velden van een activiteit

- `titel` (verplicht bij aanmaken, hoogstens 300 tekens)
- `status`: `backlog` (Voorstel), `next` (Geaccepteerd), `bezig`, `testen`, `beoordeling`, `opgelost`, `nietopgelost`,
  `geannuleerd`. Zonder status wordt het Voorstel.
- `prioriteit`: `hoog`, `gemiddeld`, `laag`, of `null` om hem weg te halen
- `vervaldatum`: `"2026-12-31"` of `null`
- `benodigdeTijd`: uren als getal, of `"45m"`, `"1:30"`, `"2u"`, `"2d"` (een dag is 8 uur)
- `onderwerpen`, `types`: namen of ids **van dat board**
- `personen`: namen of ids; `"ik"` is de gebruiker zelf; een voornaam mag als die maar één persoon oplevert
- tekstvelden: `samenvatting`, `details`, `impact`, `genomenStappen`, `volgendeStappen`, `opmerkingen`
- `checklist` (alleen bij aanmaken): de afvinklijst, bv.
  `[ { "kop": "Loggen", "punten": [ { "tekst": "Sampletijd 2 ms houden", "klaar": false }, "Logbestand bewaren" ] } ]`.
  Een punt mag een losse tekst zijn; punten zonder kopje staan vooraan. Hoogstens 300 regels per kaartje.

## Afvinklijst

- `GET /activiteiten/{id}` geeft `checklist` (groepen met `kop`, `kopId` en `punten`; elk punt heeft `id`, `tekst`,
  `klaar` en na afvinken `klaarOp` en `klaarDoor`) en `voortgang` (bv. `"12/35"`). In lijsten staat alleen `voortgang`.
- **Een punt afvinken = eerst de kaart lezen** om het `id` van het punt te vinden, daarna `PATCH /checklist/{puntId}`.
  Vink alleen af wat de gebruiker noemt; bij twijfel welk punt bedoeld is, laat de punten zien en vraag het.
- Een nieuw punt komt onderaan het kopje met die naam; bestaat het kopje niet, dan komt het er onderaan de lijst bij.
- `checklist` in een `PATCH` van de activiteit geeft een fout: na het aanmaken gaat het per punt.
- Een kopje verwijderen laat de punten staan (ze horen dan bij het kopje erboven). Verwijder alleen op verzoek.
- De volgorde veranderen kan alleen in de app.

## Werkwijze

1. **Zoek eerst het board.** Noemt de gebruiker een naam, gebruik dan `/boards?zoek=`. Meer dan één treffer: vraag welke.
   Bestaat het board niet, **maak er dan alleen een als de gebruiker dat vraagt of bevestigt**, en zeg welk soort je
   maakt (vrij, of bij welk project, welke werkorder of klant). Het antwoord zegt met `bestond` of het er al was.
2. **Lees het board voordat je schrijft**, zodat je de onderwerpen en types kent. Staat een onderwerp of type er niet
   op, vraag dan of je het mag toevoegen; maak er niet stilzwijgend een bij.
3. **Meer dan één kaartje aanmaken: laat eerst de lijst zien** (titel, personen, onderwerp, prioriteit) en wacht op
   akkoord. Stuur ze daarna in één aanroep als lijst: klopt er één niet, dan wordt er geen enkele aangemaakt en zegt de
   fout welke.
4. **Lijsten vervangen.** `personen`, `onderwerpen` en `types` in een PATCH vervangen de bestaande lijst. Iemand
   toevoegen = eerst de kaart lezen, dan de hele lijst terugsturen met de nieuwe naam erbij.
5. **Toewijzen geeft een melding** aan die collega in de app. Wijs dus niet "voor de zekerheid" mensen toe.
6. **Fouten** komen terug als `{ "fout": "…" }` en zeggen wat er mis is en wat wel kan. Lees de tekst en herstel de
   aanroep; probeer niet blind opnieuw. `401` = de sleutel klopt niet: meld dat aan de gebruiker.
7. **`503` met `"onderhoud": true`** = de app wordt bijgewerkt en de API is even dicht; er is niets gewijzigd. Zeg dat
   tegen de gebruiker met het tijdstip uit de melding. Wacht `opnieuwNaSeconden` en probeer het dan één keer opnieuw;
   blijf niet in een lus proberen. Stuur daarna de hele aanroep opnieuw (een lijst is alles of niets).
8. **Antwoord kort**: noem wat je deed en geef de `link` van het kaartje of board, zodat de gebruiker het kan openen.

## Wat niet kan

Een kaartje of board verwijderen, een bestaand board aan iets anders koppelen, de volgorde binnen een vak en de volgorde
van een afvinklijst. (Een los punt of kopje van de afvinklijst weghalen kan wel.) Zeg dat de
gebruiker dat in de app doet. Een kaartje dat niet meer hoeft zet je op `geannuleerd`, na overleg.
