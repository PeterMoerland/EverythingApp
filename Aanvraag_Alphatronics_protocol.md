# Aanvraag protocolbeschrijving generieke interface (UNii)

Te versturen aan Alphatronics (of aan de installateur, die het kan doorzetten).
Onderstaande tekst kan zo in een mail.

---

**Betreft: protocolbeschrijving generieke interface / API — UNii**

Geachte heer/mevrouw,

Wij hebben in ons bedrijfspand een UNii-installatie in gebruik. Voor onze eigen
bedrijfsapplicatie willen wij de status van de installatie kunnen **uitlezen** —
in eerste instantie alleen of het pand in- of uitgeschakeld is, later mogelijk ook
de status per zone en het gebeurtenissenlogboek. Het gaat uitsluitend om lezen; wij
willen niet in- of uitschakelen vanuit de applicatie.

Onze applicatie is in .NET geschreven en draait op een eigen server in ons netwerk.
Wij zijn bekend met de Python-bibliotheek achter de Home Assistant-koppeling, maar
implementeren de koppeling liever rechtstreeks in onze eigen applicatie.

Wij zouden graag ontvangen:

1. de **protocolbeschrijving van de generieke interface** (de API over TCP), inclusief
   de opbouw van de berichten en de wijze van versleutelen bij de instelling
   "basisversleuteling";
2. of daarvoor een **NDA** of ander formulier nodig is — dat vullen wij graag in;
3. of er beperkingen zijn per **firmwareversie** die wij moeten weten.

Onze gegevens:

- Bedrijf: MDE Automation B.V.
- Contactpersoon: Peter Moerland, p.moerland@mde-automation.nl
- Type installatie: UNii (model: **… invullen …**)
- Firmwareversie: **… invullen …**
- Installateur: **… invullen …**

Met vriendelijke groet,

Peter Moerland
MDE Automation B.V.

---

## Wat ik nog van jou nodig heb

Naast het document:

| Wat | Waar het straks in komt | Nu bekend? |
| --- | --- | --- |
| IP-adres van het paneel | `Alarm:Adres` | ja — 172.16.3.4 |
| Poortnummer van de API | `Alarm:Poort` | nee |
| Gedeelde sleutel | `Alarm:Sleutel` | ja, bij jou |
| Model en firmwareversie | — | nee |

En de controle in het paneel (door de installateur, of door jou als je
installateurstoegang hebt), onder de API-instellingen:

- **Type:** basisversleuteling
- **Verzending:** TCP
- **API-versie:** UNii
- **Interval invoerstatus:** 0 s — alleen bij firmware 2.17 of nieuwer, geeft de
  snelste terugmelding

Zet de sleutel niet in een mail of in een chat; die vul ik in `appsettings.json`
in zodra ik erbij kan, of je zet hem er zelf in.

## Wat er ondertussen al werkt

Op **Gebouwbeheer → Alarm** staat de pagina klaar. Zolang de koppeling er niet is,
kan die pagina de stand al tonen via de gebouw-PLC: één uitgang van het paneel op
een vrije ingang, en de tagnaam in `Alarm:PlcTag`. Dat kost een draadje en werkt
vandaag; de UNii-koppeling vervangt het later zonder dat er iets aan het scherm
verandert.
