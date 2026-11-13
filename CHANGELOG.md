# Wat is er nieuw

Wijzigingen in de MDE-app, per dag, met de nieuwste dag bovenaan. De datum is de dag waarop de wijziging gemaakt is; in de
app staat hij meestal dezelfde of de volgende werkdag. Ideeën of fouten? Gebruik het lampje 💡 bovenin.

Opbouw van een regel: `- **Onderdeel** — Nieuw/Verbeterd/Opgelost: wat er veranderd is (verzoek van …)`.

## 13-11-2026

- **Bestellingen** — Verbeterd (punt van Tim): bij Importeren ▸ Uit andere bestelling ga je met **← Terug naar de zoekresultaten** terug naar je zoekopdracht, zonder opnieuw te zoeken; de bestelling die je net bekeek is geel gemarkeerd. Ook na Annuleren blijft de laatste zoekopdracht staan als je het venster opnieuw opent.
- **Bestellingen** — Opgelost: een prijs met een punt (2.02) werd € 202,00. Een punt telt nu als komma: 2.02 en 2,02 zijn allebei € 2,02 (1.250 blijft twaalfhonderdvijftig). Geldt ook voor aantal en korting, voor de prijs bij Artikelen, de getalvelden in formulieren en de algemene instellingen.
- **Kabelconfigurator** — Verbeterd: de aanvraagtekening download je nu als PDF (knop ⬇ PDF). De knoppen Afdrukken en SVG zijn weg.
- **Kabelconfigurator** — Nieuw: Molex Mini-SPOX (2,5 mm, 2 t/m 8 pinnen) in de lijst van connectortypes. Draai KabelConnectoren_MariaDB.sql nog een keer om ze erbij te krijgen.
- **Kabelconfigurator** — Nieuw: knop **Artikelnummer aanmaken…** bij een kabel. In een tussenvenster vul je artikelcode, omschrijving, merk, fabrikantcode en groep in (al voorgesteld uit de kabel); het nieuwe artikelnummer komt bij de kabel en op de aanvraagtekening.
- **Kabelconfigurator** — Verbeterd: de aanvraagtekening is nu van MDE (logo, onze gegevens, ons artikelnummer en onze naam van de kabel). De naam en het nummer van LDZY staan er niet meer op, zodat je hetzelfde formulier bij elke kabelmaker kunt aanvragen.
- **Kabelconfigurator** — Nieuw: onder het veld AWG staat de doorsnede en een richtwaarde voor de maximale stroom per ader, ook voor het aantal aders dat bij elkaar ligt.
- **PDF maken** — Opgelost: met een nieuwere versie van Edge kon het maken van een PDF (factuur, offerte, orderbevestiging, pakbon) mislukken met "Edge heeft geen PDF gemaakt". De app wacht nu tot het bestand er echt staat.
- **Uitgaande facturen** — Nieuw: twee knopjes onder Documenten, **📄 Factuur** en **📎 Bijlage**, om de factuur en de urenbijlage meteen te bekijken. Bij een verstuurde factuur zie je de PDF die de klant kreeg; bij een nog niet verstuurde een voorbeeld van hoe hij nu wordt.
- **Kabelconfigurator** — Nieuw: vinkje **Aders vastlijmen aan de connector** (kaart Kabel), voor de hele kabel. Op de tekening komt het als eis voor LDZY en als gele markering bij de connectoren.
- **Kabelconfigurator** — Opgelost: opslaan overschreef steeds de geopende kabel, waardoor je geen tweede configuratie kon bewaren. Er zijn nu twee knoppen: **Wijzigingen opslaan** (werkt de geopende kabel bij) en **Opslaan als nieuwe kabel** (bewaart hem ernaast, onder een eigen naam).
- **Kabelconfigurator** — Nieuw: bij Draad kies je of er een kabel (alle aders in één mantel) of losse aders tussen de connectoren zit. Bij losse aders vervallen mantel, afscherming en krimpkous, en de tekening toont de aders los naast elkaar.
- **Bestellingen** — Nieuw: knopje ⧉ naast de titel van een bestelling zet het nummer (PO-12345) op het klembord.
- **Kabelconfigurator** — Nieuw: de connectortypes die we gebruiken staan in een eigen lijst. Kies bij P1 of P2 een type uit de lijst en familie, steek, pinnen, housing en terminal worden ingevuld; zelf invullen kan nog steeds. De lijst onderhoud je via de link Connectortypes beheren. Werkt na het script KabelConnectoren_MariaDB.sql.
- **Tools** — Nieuw: **Kabelconfigurator** (Tools ▸ Kabelconfigurator), eerste proefopzet. Stel een kabel op maat samen voor de kabelleverancier LDZY: connector of open eind aan elke kant, draad, lengte, krimpkous, bedrading met kleuren en getwiste paren. De tekening in de opmaak van LDZY (maten, Wiring Table, stuklijst, eisen, nummer) groeit mee en is af te drukken als PDF of te bewaren als SVG; de spec is als tekst te kopiëren voor de e-mail. Het LDZY-nummer (W2464-08-G020007) rekent zichzelf uit. Opslaan kan na het script Kabelconfiguraties_MariaDB.sql. Nog geen koppeling met Artikelen of een bestelling.
- **Menu** — Nieuw: Tools ▸ Kabelconfigurator. De plek staat er; de configurator zelf moet nog gebouwd worden.
- **Omroep** — Nieuw: gesproken meldingen op de Sonos-speakers (Beheer ▸ Omroep). Typ een tekst en laat hem meteen horen op de speakers die je kiest, of zet meldingen op vaste dagen en tijden in de tabel, zoals vrijdag om 9:00 "Hebben jullie allemaal een broodje besteld?". De muziek gaat even zachter en daarna vanzelf verder. Voor Beheer en Directie.
- **Bestellingen** — Verbeterd: Prijzen bijwerken uit de factuur werkt nu ook als de leverancier onze nummers niet op de factuur zet (bv. alleen "3D print part"). De regels worden dan gekoppeld op volgorde en aantal, met een waarschuwing om het na te kijken. En je kunt bij elke bestelregel zelf de regel van de factuur kiezen of verbeteren.
- **Broodjes** — Verbeterd: de vrijdaglijst van het Kaeswienkeltje sluit nu om 9:30 (was 10:00); daarna telefonisch.
- **Bestellingen** — Nieuw: **Prijzen bijwerken uit de factuur** (tab Facturen, knop ✨). De AI leest de gekoppelde factuur en zoekt bij elke bestelregel de prijs op. Je ziet eerst per regel wat er nu staat, wat er op de factuur staat en het verschil; daarna kies je welke regels worden bijgewerkt. Alleen prijs en korting veranderen, de aantallen niet. Wat de AI niet zeker weet of wat niet rond rekent staat niet vanzelf aangevinkt.
- **Bestellingen** — Nieuw: tab **Facturen** bij een bestelling: de facturen van de leverancier die eraan gekoppeld zijn, als tegels met een miniatuur, het factuurnummer, de datum, het bedrag en of hij betaald is. Klik op een factuur om hem te bekijken (alleen een voorbeeld; aanpassen en koppelen blijft bij Financiën). Iedereen kan dit zien. Directie en Finance hebben in dat voorbeeld een knop om door te klikken naar de factuur bij Inkomende facturen.
- **Bestellingen** — Nieuw: een bestelling die op Wacht op levering staat kun je nu in het bestelscherm **gereed melden** (knop Gereed ▸). Net als in Access komt de vraag of de geleverde aantallen gelijk moeten worden aan wat besteld is: Ja boekt wat nog openstaat als geleverd, Nee zet de bestelling alleen op Gereed, Annuleren doet niets. Je ziet eerst welke regels nog openstaan.
- **Bestellingen** — Verbeterd: de tab Documenten toont de bestanden als tegels met een miniatuur (PDF en afbeelding in het klein), en het vlak is hoger, zodat de bestelregels niet meer omhoog springen.
- **Bestellingen** — Nieuw: in het blokje Leverancier staat het telefoonnummer van de leverancier en van de contactpersoon, om erachteraan te bellen (klik om te bellen).
- **Bestellingen** — Nieuw: tegel **Niet doorgezet**: je eigen bestellingen die langer dan twee weken op Aanmaken of In bestelling staan. Zo blijft een bestelling niet hangen (en kan de factuur er straks aan gekoppeld worden). Onder de tegel staat hoeveel het er van jou zijn; in de lijst zie je of er al een factuur aan hangt, en met een klik op het PO-nummer open je de bestelling in een nieuw venster. Financial en Directie kunnen ook de lijst van iedereen of van één collega bekijken.
- **Menu** — Verbeterd: de tegel Verkooporders staat nu bij Projecten (onder Offertes en calculaties) in plaats van bij Bestellingen.
- **Financiën** — Nieuw: overzicht **Te laat betaald** (Financiën ▸ Te laat betaald): alle facturen aan klanten die verstuurd zijn, nog niet betaald en over de vervaldatum, per klant met wat er nog openstaat. Klik op een factuurnummer om de factuur in een nieuw venster te openen; 📄 opent de PDF. Wie het recht Financial of Directie heeft krijgt op werkdagen één keer per dag (vanaf 7 uur) een melding in de bel met het aantal en het bedrag; klikken opent het overzicht in een nieuw venster.
- **Uitgaande facturen** — Verbeterd: na het versturen van een factuur gaat het venster meteen dicht en komt er een groene melding "Factuur … is verstuurd" met een OK-knop, zodat je zeker weet dat het gelukt is. Ging er bij het bewaren iets mis, dan staat dat in de melding. (Wens van Edwin.)

## 12-11-2026

- **Bestellingen** — Nieuw: orderbevestigingen uit de inkooppostbus komen vanzelf bij de bestelling. De app kijkt op de achtergrond in inkoop@, zoekt in elk binnengekomen bericht een PO-nummer (PO-12345) en zet de PDF op het tabblad Documenten van die bestelling. Onder de knop "📥 Bevestigingen" staat wat er gekoppeld is, en wat niet vanzelf lukte kun je daar met de hand aan een bestelling hangen of negeren. (Moet eerst worden aangezet.)
- **Projecten** — Verbeterd: projectdocumenten krijgen een eigen plek in de opslag, zodat een project en een PO klant met hetzelfde nummer elkaars bestanden niet meer kunnen zien. De bestaande documenten gaan mee (na het script).
- **Artikelen** — Verbeterd: het venster dat je krijgt met het vergrootglas bij een artikel (bijvoorbeeld in Bestellingen) toont de foto nu groot over de hele breedte, en het lege vlak onderaan is weg. Een klik op de foto opent hem op ware grootte.
- **Projecten** — Nieuw: tabblad Documenten bij een project, zoals in Access. De documenten die daar al bij een project stonden, staan er meteen; toevoegen kan door te slepen of te kiezen, en een klik op de naam toont het document.
- **Projecten** — Nieuw: projectfoto's vanaf je telefoon. Op MDE mobiel ▸ Foto ▸ Projectfoto's kies je een werkorder of een project, geef je een onderwerp (dat geldt voor alle volgende foto's) en maak je foto's, met een notitie per foto als je wilt. Ze staan daarna in de grote app bij het project op het nieuwe tabblad Foto's, per onderwerp.
- **MDE mobiel** — Verbeterd: Foto opent nu een eigen menu met Projectfoto's en "Voor de pc" (de foto voor een scherm dat erop wacht).
- **MDE mobiel** — Verbeterd: het startscherm heeft nu een menu: Gebouwbeheer (verlichting, zonwering, compressor, alarm), Bestellen (scannen naar batch), Foto en Bonnetje. Elke pagina heeft bovenaan een link terug naar zijn menu, en linksboven staat het echte MDE-logo.
- **Inkomende facturen** — Nieuw: een papieren factuur fotograferen met je telefoon. Klik in het factuurvenster op "📱 Foto met telefoon", open op je telefoon MDE mobiel ▸ Foto (of scan de QR-code) en maak de foto: hij staat meteen bij de factuur en wordt uitgelezen zoals een PDF.
- **Producten** — Nieuw: programmeren en testen in de fases (voorstel van Sjoerd). Bij een producttype kies je bij Techniek ▸ Stappen per blok (uitlezen, programmeren, testen, node-adressen, duurtest) in welke fase het hoort. Bij een exemplaar staat het blok dan in die fase op het tabblad Opbouw (uitklappen) en is het daarbuiten op slot: zo haal je geen duurtest op bij een robot die net is aangemaakt. Met het recht Productcorrectie kun je een blok toch buiten zijn fase gebruiken, bijvoorbeeld om een product dat gereed is nog eens uit te lezen.
- **Producten** — Nieuw: invullen in de fase (voorstel van Sjoerd). Bij een producttype kies je per attribuut en per regel van de stuklijst in welke fase van de opbouw het wordt ingevuld. Bij een exemplaar staat het dan in die fase op het tabblad Opbouw: daar vul je het attribuut in of koppel je het halffabricaat. Een verplicht attribuut dat leeg is of een halffabricaat dat nog ontbreekt houdt Fase klaar tegen.
- **Producten** — Verbeterd: bij een exemplaar staan Samenstelling, Attributen en Documenten nu samen op het tabblad Info. Wat bij een fase hoort is daar alleen te lezen; met het nieuwe recht Productcorrectie (Werknemers ▸ Rechten) kun je het met "Corrigeren" toch wijzigen, en dat komt in de historie. Wat geen fase heeft blijft invulbaar zoals voorheen.
- **Producten** — Nieuw: bij node-adressen schrijven controleert de app eerst de serienummers op de bus (voorstel van Sjoerd). Horen de prints bij een andere robot dan je gekozen hebt, dan wordt er niets geschreven en zie je bij welke robot ze wel horen. In het venster staat per slave bij welk halffabricaat hij hoort.
- **Producten** — Nieuw: bij het ophalen van de duurtest kijkt de app of het serienummer in de log van de testrig bij het gekozen product hoort; een log van een ander serienummer wordt niet opgeslagen. Dit werkt zodra de PLC het serienummer in de log zet (een regel als "Serienummer=32").
- **Producten** — Opgelost: een document bij een stap van de opbouw kon ook bij het bezoekverslag met hetzelfde nummer terechtkomen (en omgekeerd). Er stonden nog geen documenten, dus er is niets kwijt of verplaatst.
- **Producten** — Nieuw: bij de opbouw van een exemplaar kun je een fout melden (voorstel van Sjoerd). De opbouw gaat dan op slot tot de fout is opgelost; bij het oplossen leg je vast wat er gedaan is, met foto's als je wilt. In de exemplarenlijst staat een ⚠ bij een exemplaar met een open fout.
- **Producten** — Nieuw: terug naar een fase, bijvoorbeeld na het vervangen van een print. De vinkjes van die fase en van alle fases erna vervallen (bij de stap blijft staan wie hem eerder afvinkte) en je doorloopt ze opnieuw of slaat een fase over. Iedereen mag dit, met een reden; die komt in de historie.
- **MDE mobiel** — Nieuw: je telefoon als camera. Klik op de pc bij een fout op "📱 Met telefoon", open op je telefoon MDE mobiel ▸ Foto (of scan de QR-code) en maak de foto: die staat meteen op het scherm van de pc.
- **Producten** — Nieuw: een todo-punt kan bij een fase van de opbouw horen ("＋ Todo voor deze fase", of de keuze Fase op de tab Todo). Het staat dan in die fase en moet gereed zijn voordat je de fase afrondt.
- **Producten** — Verbeterd: de opbouwboom van een producttype (tab Opbouw) bedien je nu zoals de calculatieboom, zonder rij knopjes per regel: slepen met de greep ⠿ (boven of onder een regel = ervoor of erna, op een regel = eronder hangen), sneltoetsen (Ctrl+Enter nieuw item, Ctrl+Shift+Enter nieuw item eronder, Alt+Shift+←/→ uit- en inspringen, Alt+↑/↓ schuiven) en een rechtermuismenu voor de rest (verplicht, op volgorde, documenten, verwijderen). Fases kun je in- en uitklappen.
- **Calculaties** — Nieuw: een calculatie wissen (zolang hij niet bevroren is en er geen werkorders uit gemaakt zijn) en daarna opnieuw maken of importeren; en exporteren als JSON-bestand, om een kopie te bewaren of buiten de app verder te werken.
- **Projecten** — Nieuw: tabblad Offertes bij een project, met de offertes van dat project (status, datum, maker, bedrag) en knoppen om de offerte of de calculatie te openen.
- **Algemeen** — Verbeterd: 🆕 Nieuw verschijnt nu bij elke publicatie met iets nieuws, ook als dat vaker op één dag is; op de pagina Wat is er nieuw staat het label "nieuw" bij precies de regels die je nog niet zag.
- **Calculaties** — Nieuw: een calculatie die buiten de app in de calculatieboom is gemaakt (JSON-bestand) importeren bij een offerte die nog geen calculatie heeft; je ziet eerst een samenvatting met uren, kostprijs en verkoop (verzoek van Edwin).
- **Producten** — Nieuw: opbouw in fases (voorstel van Sjoerd). Bij een MPR-robot werk je acht fases van boven naar beneden af: afgeronde fases zijn ingeklapt, alleen in de actieve fase kun je afvinken en latere fases staan op slot. Een fase rond je af met "Fase klaar" of sla je over. De eindcheck is de laatste fase.
- **Producten** — Nieuw: elke regel in de historie krijgt de fase waarin hij viel; je kunt op fase filteren en de fase achteraf aanpassen.
- **Producten** — Nieuw: fases, subfases en stappen staan in één opbouwboom, die je bij het producttype (tabblad Opbouw) onderhoudt: items toevoegen, verschuiven, in- en uitspringen. Per fase of subfase stel je in of wat eronder hangt op volgorde moet. De losse tabbladen Eindcheck zijn opgegaan in de boom.
- **Menu** — Verbeterd: de grijze tegels "in Access" zijn weg; je ziet alleen nog wat in de app werkt. See Electrical en Energie (nog zonder schermen) staan daardoor ook niet meer in het menu.

## 11-11-2026

- **Banktransacties** — Nieuw: de export van iBanFirst importeren (euro-, dollar- en pondrekening). Bedragen in dollar en pond staan in de lijst met $ of £ in plaats van €.
- **Algemeen** — Opgelost: na een update van de app moest je opnieuw inloggen voordat mailen weer werkte, en een half verstuurde mail was dan weg. De Google-aanmelding blijft nu bewaard. Is hij toch verlopen, dan vraagt de app je opnieuw aan te melden in een klein venster en gaat de mail daarna gewoon weg. De eerste keer vraagt Google nog één keer om toestemming.
- **Bankrekeningen** — Nieuw: creditcards als rekening (soort 💳 Creditcard, met het kaartnummer als nummer) en "Afgerekend via" voor de betaalrekening waarvan de kaart wordt afgeschreven.
- **Banktransacties** — Nieuw: de creditcard-export van de Rabobank importeren; kaarttransacties koppel je net zo aan facturen en grootboek als gewone transacties.
- **Banktransacties** — Opgelost: importeren in de app stopte met "Data too long for column 'Importdatum'".
- **Banktransacties** — Verbeterd: tijdens het importeren een voortgangsbalk met percentage en het aantal regels.
- **Banktransacties** — Nieuw: een afschrijving zonder factuur toch verantwoorden met "Koppeling aanmaken (factuur zonder bon + PO)": kies leverancier en werkorder, eventueel een omschrijving, en de app maakt de factuur (op betaald, zonder btw) en een PO onder de werkorder en koppelt ze.
- **Service** — Nieuw: kopje Service in het menu. Bij een storing een klant en project kiezen en zien wie eraan werkte, met het aandeel in de uren en wie de software deed (verzoek van Michiel).
- **Service** — Nieuw: een storing of verzoek doormailen naar een of meer mensen uit de lijst (vinkjes of ✉ achter de naam), met wie belde, wanneer, het telefoonnummer en de omschrijving.
- **Service** — Verbeterd: een melding afvinken (of weer open zetten) vraagt eerst of je het zeker weet.
- **Service** — Opgelost: de bevestiging na het mailen van een melding vulde de hele hoogte van het scherm; nu een klein blokje rechtsboven dat vanzelf verdwijnt.
- **Service** — Nieuw: meldingen worden opgeslagen (ook zonder mail met "Alleen opslaan"). Bovenaan staan de open meldingen, bij een project alle meldingen van dat project, en met ✓ Afgehandeld vink je ze af.
- **Mobiel** — Opgelost: op een telefoon viel de vraag "mobiel verder?" buiten beeld, en na "Volledige app gebruiken" kon je niet meer terug. Nu staat de vraag linksboven en heeft de volledige app op een telefoon de knop 📱 Mobiele weergave.
- **Banktransacties** — Nieuw: filter Facturen (Alle / Zonder factuur / Met factuur) om bijvoorbeeld alleen de nog niet verantwoorde transacties te zien; aantal en totaal bovenin volgen het filter.
- **Algemeen** — Nieuw: deze pagina Wat is er nieuw; bovenin verschijnt 🆕 Nieuw als er iets veranderd is sinds je vorige bezoek.
- **Artikelen** — Nieuw: knop 🛒 In orderbatch in het artikeldetail, met aantal (per verpakking), werkorder en opmerking; zat ook in Access (verzoek van Stein).
- **Artikelen** — Verbeterd: artikelen en samenstellingen wijzigen kan nu met het nieuwe recht Artikelbeheer (of Directie); wie niet aan een werknemer gekoppeld is, kan alleen nog kijken.
- **Bestellingen** — Nieuw: 📦 Pakbon maken vanuit een bestelling, om mee te geven als iemand de spullen wegbrengt (bijvoorbeeld naar de anodiseerder).
- **Bestellingen** — Nieuw: Importeren ▸ Uit andere bestelling: regels (en desgewenst de tekeningen) overnemen uit een eerdere bestelling, zoeken ook op besteller ("Jansen Leon").
- **Bestellingen** — Verbeterd: de knoppen boven de regels zijn even hoog.
- **Banktransacties** — Nieuw: een factuur zelf zoeken en koppelen in het detail; voorstellen letten op nummer, bedrag, IBAN en datum.
- **Banktransacties** — Nieuw: ongekoppelde transacties in één keer nalopen en koppelen; betalingscheck kijkt ook naar het rekeningnummer.
- **Banktransacties** — Nieuw: grootboekrekening per afschrift kiezen.
- **Banktransacties** — Verbeterd: rustiger detailvenster, regels dichter op elkaar, kolom Tegenpartij gevuld.
- **Banktransacties** — Opgelost: een nieuwe rekening zonder nummer toonde alle transacties.
- **Bankrekeningen** — Nieuw: eerst een BV kiezen, dan zie je alleen de rekeningen van die BV.
- **Relaties** — Nieuw: tab Bankrekeningen met alle IBAN's van een relatie (toevoegen, wijzigen, standaard kiezen); alleen zichtbaar voor Financial en Directie.
- **Relaties** — Verbeterd: verwijzingen (contactpersoon, taal, verantwoordelijke, verzendadres) als keuzelijst; activiteiten als vinkjes.
- **Grootboekrekeningen** — Nieuw: lijst van grootboekrekeningen onderhouden en inlezen uit Excel (.xlsx, .xls, HTML en CSV); compacte lijst.
- **Inkoopfacturen** — Opgelost: Doorbelasten toont 100% of 0% in plaats van 1.
- **Factuur-inbox** — Opgelost: foto's die in de mail zelf staan worden ook gelezen; opnieuw proberen als het uitlezen even niet lukt; mails zonder factuur te verbergen of te negeren.

## 10-11-2026

- **Calculaties** — Nieuw: de calculatieboom per offerte in de app, met overzichtsboom; werkorders aanmaken zodra de klant akkoord geeft.
- **Uren invoer** — Nieuw: uren boeken op de delen van een werkorder.
- **Banktransacties** — Nieuw: gekoppelde facturen zien, dubbelklik voor details; eerst de BV kiezen, dan de rekening.
- **Bestellingen** — Nieuw: BV op inkooporders; blok Besteller in de zijkolom.
- **Projecten** — Nieuw: BV-filter en BV bij een nieuw project (alleen een BV met "Mag bestellen").
- **Voorraad** — Nieuw: voorraad per BV (kaarten, filter, kolom en Excel); lokaties beheren.
- **Factuur-inbox** — Nieuw: al ingevoerde facturen weghalen; vanuit Liquiditeit doorklikken naar de factuur.
- **Producten** — Verbeterd: Todo-overzicht gegroepeerd per werkzaamheid.
- **MDE Agent** — Nieuw: agent installeren vanuit de app (Tools ▸ MDE Agent); de installatie vraagt altijd om het token.
- **Algemeen** — Opgelost: de calculatieboom stond bij sommige adressen op alleen lezen.

## 9-11-2026

- **Verkooporders** — Nieuw: order met regels en orderbevestiging, bestellen bij de leverancier, pakbon en factuur (ook in delen).
- **BV-structuur** — Nieuw: aandeelhouders, opgeheven BV's en documenten, als organigram; documenten afgeschermd.
- **Liquiditeit** — Nieuw: per BV via de boom; vaste lasten per BV.
- **Verzekeringen** — Nieuw: verzekeringen met polissen en een herinnering voor de opzegdatum.
- **Nacalculatie** — Verbeterd: urenregels over de volle breedte.
- **Projecten** — Verbeterd: tab Inventor weggehaald.
- **Producten** — Opgelost: een extra slave (swivel) blokkeert het herkennen niet meer.
- **Algemeen** — Nieuw: melding als de Vault-backup achterloopt (volgens het schema ma t/m vr).

## 6-11-2026

- **Werkplekken** — Nieuw: startcode voor wie geen Google-account heeft; per werkplek wie er mag werken; inlener in Uren invoer.
- **Werknemers** — Nieuw: werknemer toevoegen (HRM en Directie).
- **Tools** — Nieuw: Fusion-add-in en Inventor-add-in installeren vanuit de app; recht Fusion voor de schermen onder Tools ▸ Fusion.
- **Verbeterverzoeken** — Nieuw: gesprek per verzoek, met melding in de bel.
- **Nacalculatie** — Nieuw: tab Per leverancier.
- **Producten** — Verbeterd: samenstelling inklapbaar, historie compacter.
- **Broodjes** — Verbeterd: de vrijdaglijst sluit om 10:00; oude lijsten staan achter Eerdere lijsten.
- **Algemeen** — Opgelost: berichtenvenster weer leesbaar; knoppen op de telefoon in donker zonder grijze rand.

## 5-11-2026

- **Uitgaande facturen** — Nieuw: deelbetalingen vastleggen (bijvoorbeeld 90% betaald, 10% ingehouden).
- **Producten** — Nieuw: samenstelling als boom naast het detail, lijsten schaalbaar met sleepgrepen, historie per exemplaar kiezen en zelf een regel toevoegen.
- **Producten** — Nieuw: programmeren en dual motion via een product; verkeerd exemplaar herkennen met een melding; detecteren ook op de werkplek.
- **Producten** — Verbeterd: de keuze staat in de adresbalk, zodat Terug in de browser werkt.
- **Bestellingen** — Nieuw: artikel zoeken bij de artikelcode (standaard of maakdelen); een lege regel wissen zonder vraag.
- **Algemeen** — Verbeterd: menuschermen met iconen en grotere tegels.

## 4-11-2026

- **Uren invoer** — Nieuw: losse regels kopiëren en plakken, ook meerdere tegelijk.
- **Uitgaande facturen** — Nieuw: regels overnemen uit een pakbon.
- **Producten** — Verbeterd: parametersets op volgorde van positie; EEPROM volgens ETG.2010 (agent 1.25.0).

## 29-10-2026

- **Projecten** — Nieuw: projectteam met rollen.
- **Producten** — Nieuw: dynamic phasing (preset); motiontest-tabellen over de volle breedte.
- **Producten** — Opgelost: Techniek-vinkjes verliezen de verbinding niet meer.

## 27-10-2026

- **Bestellingen** — Nieuw: zoeken in bestelregels.
- **Projecten** — Nieuw: van een project naar de PO van de klant klikken en terug.
- **Producten** — Nieuw: drive-testen tonen motorstroom en encoderstatus (agent 1.22–1.23).
- **Algemeen** — Nieuw: aankondiging "nieuwe versie over … minuten" bovenin, zodat je je werk kunt afmaken.

## 24-10-2026

- **Projecten** — Nieuw: kostenbegroting per project, voortgang tegen de begrote kosten.
- **Activity board** — Nieuw: werkorder, onderwerpen met bijlagen, boom, PO uit een PDF met AI; board wissen (Directie).
- **Broodjes** — Nieuw: broodjes bestellen bij het Kaeswienkeltje.
- **Producten** — Nieuw: werkorderfilter en tab Typeplaatje; de exemplarenlijst toont het type en ververst statussen.
- **Producten** — Nieuw: een todo-punt overal in één keer hernoemen; bevestiging bij gereed melden (verzoek van Tim Franke).
- **Nacalculatie** — Nieuw: subwerkorders in nacalculatie en urenimport; laatst bekend kostprijstarief als er voor die datum geen is.
- **Mail** — Verbeterd: je Gmail-handtekening onder factuur-, bestel- en offertemails.
- **Scannen** — Opgelost: labels met "]C1" ervoor worden herkend.
- **Algemeen** — Verbeterd: tekstvakken groeien mee; rustigere melding bij het uitlezen.

## 22-10-2026

- **Verbeterverzoeken** — Nieuw: lampje 💡 bovenin om een idee in te dienen, met schermafdrukken; lijst voor iedereen en een bericht bij een nieuwe status.
- **Berichten** — Nieuw: berichtenbox in de bel, berichten (met plaatjes) aan collega's, berichtgeluid; videobellen met collega's.
- **Projecten** — Nieuw: tabs Budget en Budget verdeling (uit Access); voortgang per project (OHW).
- **PO klant** — Verbeterd: project verplicht; het PO-bedrag komt als budget bij het project.
- **Uitgaande facturen** — Nieuw: aan een project koppelen.
- **Vragen aan AI** — Nieuw: gesprekken privé, met delen en wissen.
- **Producten** — Nieuw: recht MagFirmware, waarschuwing bij teruggevallen firmware, controle na het laden, afgekeurd niet inbouwen; Techniek alleen door Directie te wijzigen.
- **Uren invoer** — Verbeterd: vraagt bij weggaan als er nog iets niet opgeslagen is.
- **MDE Agent** — Nieuw: tray-icoon; lopende taak zichtbaar en te stoppen; installer downloaden.

## 21-10-2026

- **Producten** — Nieuw: testrecepten met een venster voor de lopende stap, grenslijnen en bediening (HMI); receptenlijst met editor per controllertype.
- **Samenstellingen** — Nieuw: naar-knop achter elk gebruikt onderdeel.
- **Producten** — Opgelost: drive-serienummer bij meer printen; node-adressen veilig schrijven (agent 1.20).

## 20-10-2026

- **Algemeen** — Nieuw: welkomstscherm voor de hal.
- **Bestellingen** — Nieuw: werkorder zoeken in het bestelformulier.
- **MDE Agent** — Nieuw: recepten met procesdata, uitrol vanaf Z: (agent 1.19).

## 19-10-2026

- **Vragen aan AI** — Nieuw: vragen stellen over projecten, uren, bestellingen, voorraad en producten, als gesprek, met grafieken (via Amazon Bedrock); Financiën-vragen alleen voor Financial en Directie.
- **Factuur-inbox** — Nieuw: invoice@ en bonnetjes komen binnen en worden meteen uitgelezen, ook gescande PDF's; IBAN/BIC van de factuur aangevuld.
- **Bestellingen** — Nieuw: regels en leverancier uit een screenshot of PDF halen; stuksprijs in euro; Plakken in het menu Importeren.
- **Pakbonnen** — Nieuw: pakbonnen in de app.
- **Nacalculatie** — Nieuw: overgezet uit Access.
- **Uitgaande facturen** — Nieuw: werkorder per factuur; blok Analyse in Financiën.
- **Tarieven** — Nieuw: tarieven berekenen als eigen scherm.
- **Voorraad** — Nieuw: voorraadstand per datum (met Excel).
- **Artikelen** — Nieuw: tabbladen Leveranciers en Voorraad/Bestellen.
- **Producten** — Nieuw: todo-punten per exemplaar en een Todo-overzicht.
- **Algemeen** — Opgelost: een veld opslaan botste niet meer met het kiezen van een ander record; zoekvakken met een lijst werken als keuzelijst.

## 16-10-2026

- **Bestellingen** — Nieuw: nieuwe bestelling in een venster, plakken uit het klembord, standaardwaarden per leverancier, Volgende en goedkeuren, mail aan de leverancier, materiaal inboeken.
- **Bestellingen** — Nieuw: oude bestellingen opruimen.

## 15-10-2026

- **Activity board** — Nieuw: problemen op prioriteit en activiteiten op een kanbanbord.
- **Snelbalk** — Nieuw: tien eigen snelkoppelingen bovenin, met icoon of plaatje.
- **Werkplekken** — Nieuw: gedeelde pc's zonder Google-account met "wie werkt er"; Mail, Agenda, Financiën en Werknemers dicht op een gedeelde pc.
- **Producten** — Nieuw: digitale IO live bedienen; historie van een exemplaar opschonen (Directie).
- **Gebouwbeheer** — Verbeterd: vensters in het midden, Sonos afspelen en pauzeren.

## 14-10-2026

- **Producten** — Nieuw: testrecepten per controllertype met live meekijken; seriële monitor voor de ESP32; serienummer en EEPROM van eigen MDE-prints schrijven; firmware via FoE.

## 13-10-2026

- **Financiën** — Nieuw: uit Access overgezet: uitgaande facturen, vaste lasten, liquiditeitsoverzicht, betalingen klaarzetten, bankrekeningen; bankafschriften elk uur van de Rabobank.
- **Verkoop** — Nieuw: offertes en PO klant overgezet uit Access.
- **Artikelen** — Nieuw: foto plakken, merkkiezer en nieuwe groepen; samenstellingen overgezet uit Access.
- **Voorraad** — Nieuw: lokatiestructuur, dragers en artikelvoorraad overgezet uit Access.
- **Relaties** — Nieuw: bezoekverslagen (contactmomenten vastleggen).
- **Producten** — Nieuw: half-fabricaten, producten, drivetypen; eigen EtherCAT-slaves uitlezen, testen en firmware laden.
- **Beheer** — Nieuw: Computers (vervangt het laptopregister), Filestore en Labelprinter.
- **Gebouwbeheer** — Nieuw: Sonos-volume; opent als venster boven het huidige scherm.
- **Algemeen** — Opgelost: de app opende niet op een iPhone; eigen bevestigingsvensters.

## 13-9-2026

- **Algemeen** — Nieuw: eerste versie van de app, met onder meer Uren invoer, Werknemers, Relaties, Projecten, Artikelen, Bestellingen, Orderbatch en scannen met de telefoon, Producten en maakdelen, Planning, Facturen, Banktransacties, Agenda, Mail, 3D-viewer, Dymo-labels en Gebouwbeheer (camera's, verlichting, zonwering, alarm, compressor).
