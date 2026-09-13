# MDE-app: componenten-API voor de Fusion-add-in

Instructie voor het bouwen van de Fusion 360-add-in die standaardcomponenten
van Eurocircuits kiest. De MDE-app (Blazor, op de server) stelt de tabel met
standaardcomponenten beschikbaar via één HTTP-endpoint. De add-in is de
vragende kant en heeft **geen** databasetoegang nodig.

## Endpoint

```
GET https://everything.mde-automation.nl/mde/api/componenten
```

Rechtstreeks (binnen het netwerk, zonder Caddy/certificaat) kan ook:
`http://172.16.1.13:5010/api/componenten` — dan vervalt het hele
ssl-gedeelte uit het voorbeeld verderop.

## Autorisatie

Elke aanvraag stuurt de gedeelde sleutel mee in een header:

```
X-Sleutel: <de afgesproken sleutel>
```

De sleutel staat op de server in `appsettings.json` onder `Componenten:Sleutel`
(of als omgevingsvariabele `Componenten__Sleutel`). Zet exact dezelfde tekst in
de add-in-configuratie. Bewaar hem in de add-in niet hard in de code maar in
een klein config-bestand naast de add-in.

## Parameters (allemaal optioneel, te combineren)

| Parameter | Betekenis                | Voorbeeld        |
|-----------|--------------------------|------------------|
| `soort`   | `R` (weerstand) of `C` (condensator) | `soort=C` |
| `grootte` | behuizingsmaat, vier cijfers | `grootte=0402` |
| `zoek`    | vrije tekst; zoekt in GPN, waarde en omschrijving; spaties in waardes worden genegeerd (`100nF` vindt ook `100 nF`) | `zoek=100nF` |

Voorbeeld: alle 0402-condensatoren van 100 nF:

```
GET .../api/componenten?soort=C&grootte=0402&zoek=100nF
```

## Antwoord

`200 OK` met een JSON-array. Velden per component:

```json
[
  {
    "gpn": "GPC0402104",
    "soort": "C",
    "grootte": "0402",
    "waardeBasis": 1e-07,
    "waardeTekst": "100 nF",
    "tolerantie": "10",
    "vermogen": null,
    "spanning": "16V",
    "dielectricum": "X7R",
    "omschrijving": "100nF ±10% 16V Ceramic Capacitor X7R 0402 (1005 Metric)"
  }
]
```

- `gpn` — het Eurocircuits General Part Number; **dit is wat de add-in in het
  Fusion-ontwerp moet zetten** (of tonen) zodat de BOM-export later als
  standaardcomponent herkend wordt.
- `waardeBasis` — de waarde genormaliseerd in de grondeenheid (ohm voor R,
  farad voor C), als getal. Handig om zelf op te sorteren of te matchen.
- `waardeTekst` — de leesbare schrijfwijze voor in de UI.
- `vermogen` is alleen bij weerstanden gevuld, `dielectricum` alleen bij
  condensatoren; `null` betekent: niet van toepassing of onbekend.

## Statuscodes

| Code | Betekenis en wat de add-in moet doen |
|------|--------------------------------------|
| 200  | Gelukt; body is de JSON-array (kan leeg zijn: `[]`). |
| 401  | Sleutel ontbreekt of klopt niet → melding "componentensleutel onjuist". |
| 404  | Endpoint uit (geen sleutel op de server ingesteld) of database onbereikbaar → melding "componentendatabase niet beschikbaar". |

## Voorbeeld in Python (Fusion-add-ins draaien op ingebouwd Python 3)

Gebruik de standaardbibliotheek (`urllib`); `requests` is in Fusion niet
gegarandeerd aanwezig.

```python
import json
import ssl
import urllib.parse
import urllib.request

BASIS = "https://everything.mde-automation.nl/mde/api/componenten"
SLEUTEL = "..."  # uit het config-bestand van de add-in

def componenten(soort=None, grootte=None, zoek=None):
    velden = {k: v for k, v in {"soort": soort, "grootte": grootte, "zoek": zoek}.items() if v}
    url = BASIS + ("?" + urllib.parse.urlencode(velden) if velden else "")
    aanvraag = urllib.request.Request(url, headers={"X-Sleutel": SLEUTEL})

    # Het https-certificaat komt van de interne CA van MDE. Python gebruikt op
    # Windows zijn eigen certificaatwinkel, dus laad het bedrijfscertificaat mee
    # (mde-root.crt staat op elke pc, ook te downloaden van https://everything.mde-automation.nl/mde-root.crt):
    ctx = ssl.create_default_context()
    ctx.load_verify_locations("mde-root.crt")

    with urllib.request.urlopen(aanvraag, context=ctx, timeout=10) as antwoord:
        return json.loads(antwoord.read().decode("utf-8"))

# alle 100nF-condensatoren in 0402:
for c in componenten(soort="C", grootte="0402", zoek="100nF"):
    print(c["gpn"], c["waardeTekst"], c["spanning"], c["dielectricum"])
```

Gebruik je de interne http-variant (ip + poort), dan vervalt het hele
ssl-gedeelte en volstaat `urllib.request.urlopen(aanvraag, timeout=10)`.

## Praktische adviezen voor de add-in

- **Cache** het antwoord per (soort, grootte) een paar minuten in het geheugen;
  de tabel verandert zelden en Fusion voelt dan direct snel.
- De volledige tabel is klein (hooguit enkele duizenden regels); alles in één
  keer ophalen zonder filters en lokaal filteren is ook prima.
- Toon bij een 401/404 een rustige melding en laat de gebruiker verder werken;
  het kiezen van standaardcomponenten is een hulpmiddel, geen blokkade.
- De bron van deze tabel is de CSV-import in de MDE-app
  (Tools ▸ Fusion ▸ Standaardcomponenten). Nieuwe maten of reeksen importeren
  = daar slepen; de add-in ziet ze meteen.
