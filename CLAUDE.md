# CLAUDE.md

Notities voor Claude Code-sessies op deze repo. `README.md` heeft de brede
context van de add-in; hier staan alleen niet-vanzelfsprekende dingen:
bouw-gotcha's, recente wijzigingen die de code nog niet zelf vertelt, en
architectuurkeuzes die nog open liggen.

## Bouwen

**Vault op .NET 10 (sinds 6-10-2026).** Inventor 2026 en de Vault Client 2026 draaien
na een update op .NET 10 (zie `Inventor 2026\Bin\*.runtimeconfig.json`); de
Vault-assemblies verwijzen naar `System.Runtime` 10. Daarom target
`InventorTools.Vault` `net10.0-windows` en de rest blijft `net8.0-windows` (draait
gewoon in het .NET 10-proces van Inventor, en ook nog op een oudere Inventor).
- De AddIn heeft **geen projectverwijzing** naar Vault meer: `InventorTools.Vault.dll`
  wordt alleen meegekopieerd (`VaultUitvoer` in `InventorTools.AddIn.vbproj`) en pas bij
  gebruik geladen door `Commands\VaultLader`, via de interfaces in `Core\Vault`
  (`IVaultToegang`, `IVaultSessie`). Lukt laden niet, dan meldt `VaultLader.Reden`
  waarom en werkt de rest door.
- Bouwen van het Vault-project vraagt de **.NET 10 SDK** (op deze pc via winget,
  `Microsoft.DotNet.SDK.10`). Met alleen SDK 9 faalt het op BC32207.
- Bouw nog steeds per project met `--no-dependencies` en roep `deploy.ps1` en
  `publish.ps1` aan met `-SkipBuild`; **Vault vóór AddIn**, anders kopieert de AddIn een
  oude Vault-dll mee.

Werkende volgorde voor een lokale uitrol (Inventor afsluiten eerst):

```powershell
dotnet build src\InventorTools.Core\InventorTools.Core.vbproj   --no-dependencies -c Release --nologo
dotnet build src\InventorTools.Cad\InventorTools.Cad.vbproj     --no-dependencies -c Release --nologo
dotnet build src\InventorTools.Vault\InventorTools.Vault.vbproj --no-dependencies -c Release --nologo
dotnet build src\InventorTools.AddIn\InventorTools.AddIn.vbproj --no-dependencies -c Release --nologo
.\deploy.ps1 -SkipBuild
```

Voor een uitgave naar de share: laatste regel wordt
`.\publish.ps1 -SkipBuild -Doel "Z:\INVENTOR\Apps\InventorTools"` (of het
volledige UNC-pad, want een gekoppelde letter is onzichtbaar in een
beheerder-shell).

## Recente wijzigingen — 4-11-2026 (knop Idee, foutmeldingen met aanwijzing, materiaalmelding)

Voorstel en doorlichting staan in `D:\InventorApp\idee-knop-voorstel.md` (buiten de repo). Alles
gebouwd en in UiPreview bekeken; niet in Inventor getest. Op 6-11-2026 lokaal gedeployed en
gepubliceerd als `2026-11-06-1605` (terugval `2026-11-04-0726`), op verzoek van de gebruiker vóór
de test in Inventor. Eerste uitgave met `uitgave.txt`.

- **Knop Idee** (paneel Instellingen op het MDE-tabblad; op de kleine panelen een eigen groepje
  achteraan; icoon IDEE, geel): `Commands\IdeeKnopService.vb`, `ViewModels\IdeeViewModel.vb`
  (met `Schermafdruk` en `IdeeVerstuurd`), `Views\IdeeWindow.xaml{,.vb}`, `Commands\VensterVanger.vb`
  (BitBlt van het Inventor-venster vanaf het scherm), `Core\Data\VerbeterverzoekRepository.vb`.
  Schrijft in **dezelfde tabel `Verbeterverzoeken` als het lampje van de MDE-webapp**
  (`MDE_APP\Migration_Analysis\UreningavePoC\Verbeterverzoeken_MariaDB.sql`; ontbreekt de tabel,
  dan zegt het venster dat). IngediendDoor = werknemer-id (0 = NULL), Pagina = "Inventor part
  55964.ipt", Tekst = tekst van de gebruiker + regel "— Inventor 2026.x · Inventor part ... ·
  uitgave ... · pc ..." + optioneel de laatste FOUT-regel uit het logboek (vinkje, standaard aan).
  Schermafdrukken (max 3): Ctrl+V in het venster (eerst klembordformaat "PNG", anders
  `Clipboard.GetImage`), "kies een bestand", of knop Inventor-venster (dit venster even
  `Visibility.Hidden`, dan BitBlt; niet Hide/Show, dat verstoort ShowDialog). Ze gaan de filestore
  in via `FileStoreSchrijver.VoegToeUitGeheugen` met StoreType 340, MasterID = VerzoekID
  (`VoegToe` heeft nu een optionele `storeType`). Lijst bekijken opent `verbeterverzoeken.lijstUrl`
  uit appsettings (standaard de productie-webapp; na versturen met `?verzoek=<id>`). Mislukt de
  database: rode melding en knop Tekst kopiëren. Ctrl+Enter = versturen. **Mail naar Peter/Arie is
  bewust nog niet gebouwd**; de mailroute (Google SMTP-relay, app-wachtwoord, of de webapp laat
  mailen) is nog een keuze van de gebruiker. `UiPreview idee [--leeg|--verstuurd|--waarschuw|--fout]`.
  Klein puntje: de webapp toont Pagina als link; "Inventor part ..." leidt daar nergens heen.
- **Uitgave-naam**: `publish.ps1` schrijft `uitgave.txt` in de payload; `AddIn\Uitgave.vb` leest hem
  ("lokale build" als hij ontbreekt).
- **Materiaalbibliotheek** (wens gebruiker): `MateriaalService.Probleem` (reden) en
  `Commands\MateriaalMelding.ToonEenmalig` (een keer per Inventor-sessie, in MDE Parts en Collect
  Parts: niet geladen, reden, "open een part en probeer opnieuw; anders Inventor opnieuw starten;
  anders het project controleren"). `ZoekBibliotheek` zoekt een keer per instantie en vangt ook
  `ArgumentException`.
- **Reservering**: in `PartNumberService.UitvoerenIntern` draait een fout tussen `Reserveer` en het
  venster de reservering terug (`CancelReservering`), zoals Annuleren al deed.
- **Foutmeldingen**: E_INVALIDARG (0x80070057) komt als `ArgumentException` binnen, niet als
  COMException; een `Catch COMException` laat hem door. Nieuw: `Cad\InventorFout.vb` met
  `IsVanInventor(ex)` (voor `Catch ex As Exception When InventorFout.IsVanInventor(ex)`) en
  `Vertaal(ex)` (Nederlands met aanwijzing: E_INVALIDARG, E_FAIL, RPC bezig, toegang geweigerd,
  UnauthorizedAccess, DirectoryNotFound, FileNotFound, IO, Win32, fouten in de add-in zelf), en
  `Commands\Foutmelding.Algemeen(wat, ex)` voor de dialoog van alle zestien knopservices
  ("X is niet gelukt. <reden> Details staan in <log>"). `When IsVanInventor` staat nu op:
  MateriaalService, TekeningMaker (AddFitted met stijlnaam; `Sort("Part Number")`; sjabloon),
  AssyService (Documents.Open x3, met `File.Exists` vooraf en de bestandsnaam in de melding),
  ExportService.Openen, HoleNoteService, RibbonBuilder (4x), FormaatExporteur (`ItemById`, en een
  uitgezette vertaler wordt geactiveerd), VaultAddinKoppeling (`ItemById`, `Activate` proberen),
  TekeningKnopService. `TekeningMaker.Sjabloon` valt terug op `FileManager.GetTemplateFile` als
  Standard.dwg niet in de projectmap staat.
- **Bekende situaties als waarschuwing**: `DocumentTargetException` noemt het deel ("Part1 is nog
  niet opgeslagen") en wordt gevangen in Formaten, Kopieer, Quick Drawing, Maak/Open DWG en Stempel.
  `DocumentTarget.Resolve` neemt `ActiveEditDocument` bij in-place bewerken in een samenstelling;
  het model van een tekening wordt eerst op `File.Exists` gecontroleerd. Stempel: `ActiveEditDocument`.
- **Bereikbaarheidscontroles**: `Commands\FilestoreControle.Bereikbaar` vóór Genereer formaten
  (Collect Parts en de knop Formaten); Quick Drawing zet het in de regelstatus; `KopieerService`
  controleert de filestore en vangt UnauthorizedAccess; `ExportService` zet de reden achter het
  formaat in de status ("PDF (Map niet gevonden: ...)"); `Deelverrijking.Vul` geeft terug waarom
  de tekeningenmap niet doorzocht is en Open DWG toont dat in plaats van "geen tekening gevonden";
  `PrintKnopService.MaakPrintmap` stopt met een melding bij onbereikbaar of geen rechten;
  `SlicerOpener` geeft een duidelijke tekst zonder Bambu en zonder .stl-koppeling;
  `InstellingenService` opent zonder database met een lege gebruikerslijst; `VaultPlakKnopService`
  vangt fouten van `Verbind` en `HaalLaatsteOp`; `ExportViewModel.GenereerAsync` heeft een catch
  (Async Sub).
- Bewust niet gedaan: niet-opgeslagen delen overslaan in Quick Drawing en Collect Parts, en de
  border uit het sjabloon kopiëren bij Sheetsize.
- **3D printen: Esc stopt de export** (wens gebruiker, later op 4-11-2026: een part met veel bodies
  duurt lang). De export blokkeert de UI-draad, dus `Commands\EscToets.vb` leest de toets met
  `GetAsyncKeyState` (ook het "ingedrukt sinds de vorige keer"-bit, zodat een Esc tijdens het
  rekenen aan een body meetelt); `Reset()` vooraf en na de multi-body-vraag, en na afloop
  `VeegToetsenWeg()` (PeekMessage) zodat diezelfde Esc niet via de Sluiten-knop (IsCancel) het
  venster dichtdoet. `PrintKnopService.Exporteer` houdt in `Exportrun` bij wat geschreven is,
  kijkt tussen elk part en elke body naar Esc, en verwijdert bij stoppen alles van deze ronde
  (`RuimOp`; let op: met "overschrijven" aan is het overschreven bestand dan ook weg). Het
  resultaat is nu `ExportUitkomst` (paden + melding); met een melding opent het printscherm niets
  en telt het niet af, het zet alleen "Gestopt met Esc; n bestanden weer verwijderd." in de
  statusregel. Voortgang per bestand in de statusregel via `PrintViewModel.ToonVoortgang`
  (`Dispatcher.Invoke` op Render-prioriteit: tekent wel, verwerkt geen invoer). Niet in Inventor
  getest: of de voortgangstekst echt verschijnt tijdens het exporteren en of de Esc goed
  doorkomt.

## Recente wijzigingen — 3-11-2026 (materiaalbibliotheek, 3D printen van niet opgeslagen delen)

- **MDE Parts gaf E_INVALIDARG** op `AssetLibraries.Item("Inventor Material Library")` (sessie
  begonnen met alleen een samenstelling open). In Inventor stonden toen alleen de Autodesk Material
  en Appearance Library in `AssetLibraries`, terwijl het project (`Vault_2026.ipj`, Design Data in
  `D:\Working Folder\MDE Standards\Design Data`) de Inventor-bibliotheek wel noemt: Inventor laadt
  hem kennelijk pas als hij nodig is. Nadat de gebruiker een part had geopend, werkte het weer.
  `MateriaalService.ZoekBibliotheek` zoekt nu op naam in de geladen bibliotheken, opent anders zelf
  het `InventorMaterialLibrary.adsklib` uit `DesignProject.MaterialLibraries` met
  `AssetLibraries.Open`, en geeft anders een lege lijst (venster blijft bruikbaar). Logregels via
  de nieuwe optionele `meld`. Niet in Inventor getest: `AssetLibraries.Open` zelf.
- **3D printen en een deel dat nog niet op schijf staat** (wens gebruiker): eerst kwam de
  "onverwachte fout" uit `BuildInventorReference` ("Bestand is nog niet opgeslagen"), en een
  nieuw deel in een samenstelling (pad bekend, bestand nog niet) gaf E_INVALIDARG in
  `Documents.Open`. Nu vraagt `PrintKnopService` vóór het printscherm (`NietOpgeslagenWindow`):
  **Opslaan** (standaard) = `AppFileSaveCmd` met `Execute2(True)`, dus Inventors eigen
  opslaanvenster (Opslaan als, namen voor nieuwe delen, Vault), daarna gewoon verder; nog steeds
  niet opgeslagen = stoppen met een statusregel. **Toch exporteren** = uit het geheugen:
  `Opslagstatus.ZoekNietOpgeslagen` (actief document + `AllReferencedDocuments`, geen pad of geen
  bestand), `DocumentTarget.BuildVoorlopigeReferentie` ("{guid} Part1", nooit voor de database),
  `DeelLezer.Lees(..., ookNietOpgeslagen)`, `SelectieVerzamelaar.Verzamel(..., ookNietOpgeslagen)`
  en `VerzamelOpties.OokNietOpgeslagen`; `OpenVoorExport` pakt dan het geladen document. De STL
  heet naar de naam in Inventor (Part1) als er geen partnummer/omschrijving is.
  **Annuleren** = niets. Alleen gewijzigd maar wel opgeslagen = geen vraag (de export neemt al het
  model zoals het open staat). `UiPreview nietopgeslagen [--meer]` toont het venster (licht en
  donker bekeken). **Niet in Inventor getest**: of `Execute2(True)` vanuit onze knop echt wacht op
  het opslaanvenster (logregel "na AppFileSaveCmd nog niet opgeslagen" vóór het opslaanvenster
  betekent: wacht niet; dan wachten op `OnSaveDocument`), en de export uit het geheugen.
- Beide wijzigingen lokaal gedeployed (3-11-2026) en gepubliceerd als `2026-11-04-0726`
  (terugval `2026-10-27-1334`), op verzoek van de gebruiker vóór de test in Inventor.

## Recente wijzigingen — 21-10-2026 (Stempel zoekt een plek, 3D printen sluit vanzelf)

Wensen van de gebruiker na het testen (21-10-2026).

- **Stempel zoekt zelf een plek** in plaats van alleen het midden van het gekozen vlak:
  `GravurePlaatser.PlaatsOpVlak` gebruikt dezelfde zoektocht als de automatische gravure (vanuit
  het midden naar buiten, beide leesrichtingen), eerst met 3 mm marge, dan 1 mm, dan precies
  binnen het vlak. Pas als het op het hele vlak nergens past, komt de tekst in het midden en de
  vraag "Toch laten staan?". Melding in de statusbalk waar hij staat. `PartnummerStempel` houdt
  alleen nog de opmaak, de uitsnede en het raakvlak aan een cilinder; `Zet`, `PlaatsTekst` en
  `TekstPast` zijn weg.
- **Geen werkvlak meer op een plat vlak**: de schets staat direct op het vlak (gekozen of
  automatisch). Alleen een cilinder krijgt nog een (verborgen) raakvlak, want daar kan geen
  schets op; dat heet "StempelPartnumber: Work Plane..".
- **De uitsnede heet StempelPartnumber** (StempelPartnumber2 enzovoort als de naam al bestaat).
  `PartnummerStempel.Graveer` probeert eerst de negatieve richting en anders de positieve: een
  uitsnede die ziek is of geen vlakken heeft (niets weghaalt) gaat weer weg. Nodig omdat bij een
  schets op het vlak zelf niet vaststaat welke kant de schetsnormaal op wijst.
- **3D printen**: na een export met minstens een bestand telt de statusregel 5 s af ("2 geexporteerd.
  Sluit over 5 s."; langer past niet naast de knoppen, zie het plaatje van `print --export`) en sluit het scherm; een klik of toets in het scherm stopt het aftellen
  (`PrintViewModel.StopAftellen`). Een bestand openen met dubbelklik of Enter sluit het scherm
  meteen (niet als het bestand er niet meer is). `UiPreview --printtest` toetst het aftellen;
  `UiPreview print --export` toont het scherm net na een export.
- **3D printen, openen in Bambu Studio** (`Core\Slicer`, `Commands\SlicerOpener`): staat Bambu
  Studio open, dan stuurt de add-in het bestand zelf met **WM_COPYDATA** (dwData 1, de
  opdrachtregel als `"programma";"bestand"` met backslashes verdubbeld, zoals
  escape_strings_cstyle van PrusaSlicer) naar het zichtbare hoofdvenster (herkend aan het
  vensterkenmerk `Instance_Hash_Minor`); Bambu voegt het model toe aan de plaat. Nadat het
  printscherm dicht is, haalt `NaarVoren` Bambu naar voren (eerder kan niet: bij het sluiten zet
  Windows Inventor weer vooraan). Geen zichtbaar Bambu-venster: Bambu via de shell starten met
  alleen `"<bestand>"` (programma uit het draaiende proces, de koppeling
  `HKCRambustudio\shell\open\command` of Program Files); zonder Bambu de standaard-app.
  **Geen `--no-single-instance`**: daarmee stopte Bambu 2.8 meteen (exitcode -2 in de test met een
  andere Bambu open; bij de gebruiker ook zonder zichtbaar Bambu-venster); zonder optie start hij ook naast een andere Bambu (getest 21-10-2026).
  **Waarom niet `--single-instance`** (eerste versie): in de test van 21-10-2026 stopte het nieuwe
  proces (exitcode -2) zonder het open venster te vinden, dus zonder iets door te geven of het
  venster naar voren te halen; en een Bambu die op de achtergrond nog afsluit, slokte het bestand
  op. Het zelf gestuurde bericht is met de hand getest: het model kwam op de plaat.

- **3D printen, meerdere bestanden naar Bambu** (besluiten gebruiker 21-10-2026): de export
  (`PrintKnopService.Exporteer`, `ExporteerPerBody`) geeft nu de geschreven paden terug. Vinkje
  "Na export openen in Bambu Studio" (`gebruiker.json` > `printScherm.naExportOpenen`, standaard
  aan): na Exporteer STL gaan alle bestanden naar Bambu en sluit het scherm meteen.
  **Rechtermuisknop op Exporteer STL** = alleen exporteren (`ExporteerZonderOpenenCommand`, via
  `PreviewMouseRightButtonUp`), met aftellen; het wolkje (`ExportTip`) komt na 1 s. De lijst kan
  meerdere kiezen (Extended; `GeselecteerdeBestanden` vanuit het scherm, want SelectedItems is
  niet te binden); dubbelklik/Enter/rechtermuismenu "Openen in Bambu Studio" opent ze in de
  volgorde van de lijst, Wis wist alle gekozen. `PrintViewModel.Opener` is vervangbaar voor tests.
- **Altijd een bestand per bericht** (`SlicerOpener.Open(lijst)`): getest in een eigen Bambu-
  venster (21-10-2026): meerdere bestanden in een bericht geeft de vraag "Object met meerdere
  delen is gedetecteerd"; een 3MF met een object en componenten geeft bij een niet-lege plaat
  "Projectbestand neerzetten" met **Open als project** voorgekozen (vervangt het project!); een
  per bericht geeft geen vraag, maar Bambu zet ze over elkaar in het midden (Rangschikken, A).
  Een geposte A naar de `wxGLCanvas` gaf "Rangschikken voltooid" maar geen duidelijk resultaat:
  niet gebouwd. "Eén object" kan dus alleen als een samengevoegde STL (bestaande keuze Eén STL).
  Bambu dicht: starten met het eerste bestand, de rest met `StuurLater` (DispatcherTimer) zodra
  het venster van dat proces een titel met "*" heeft (of 15 s na het venster, max. 90 s).
- **Niet in Inventor getest**: rechtermuisknop op de knop, nasturen na het starten van Bambu,
  het rechtermuismenu in donker (scherm zelf wel licht en donker bekeken).
- **Stempel op de aangeklikte plek** (besluiten gebruiker 21-10-2026, na de werkende buistest):
  geen richtingsvlak meer. `Commands\VlakKiezer` laat op een vlak klikken met InteractionEvents/
  SelectEvents (OnSelect geeft `ModelPosition`, CommandManager.Pick niet) en wacht met een
  DispatcherFrame tot klik of Esc; staat er een modaal venster van ons open (hoofdvenster van
  Inventor uitgeschakeld), dan meteen een nette melding in plaats van eeuwig wachten.
  `GravurePlaatser.PlaatsOpVlak(..., klikpunt, ...)` zoekt vanaf het klikpunt (dichtstbijzijnde
  plek die past; nergens = op het klikpunt met de vraag). Buis: `PartnummerStempel.RaakvlakOpPunt`
  maakt een **vast** raakvlak op het klikpunt (x = as, normaal uit het materiaal, ook in een gat);
  beweegt niet mee als de buis verandert (geaccepteerd). Niet in Inventor getest: of ModelPosition
  goed op het vlak ligt en het wachten op de klik.
- **Stempel op een cilinder gaf E_INVALIDARG** (test 21-10-2026): niet de echte fout, maar
  `Opruimen` (ExitEdit op een schets die al dicht was, als ArgumentException) die de oorzaak
  verborg. `Opruimen` vangt nu alles op; de oorzaak gaat via `meldFout` (AddInLog) het logboek in,
  en `Graveer` zegt per richting wat er gebeurde (niet te maken / status en aantal vlakken). Het
  raakvlak wordt pas na `schets.Edit()` verborgen. **Echte oorzaak** (logboek tweede test):
  `CreateExtrudeDefinition` gaf E_FAIL op de schets op het raakvlak, met de schets al uit
  bewerking. Nu `PartnummerStempel.GraveerOpCilinder`: eerst `EmbossFeatures.AddEngraveFromFace`
  met de cilinder als WrapFace (tekst om de cilinder, overal even diep), anders de rechte
  uitsnede zoals de oude VBA, **met de schets nog in bewerking** (`Snijd` sluit pas daarna af).
  Een plat vlak blijft zoals het was (afsluiten, dan uitsnijden). Test 21-10-2026: wikkelen
  werkt (feature "StempelPartnumber (Engrave from Face)"), maar (1) bij een raakvlak aan het
  YZ-vlak stond de tekst in **spiegelbeeld** (normaal van het raakvlak naar de as) en (2) elke
  plek werd afgekeurd, dus kwam de vraag "Toch laten staan?". Nu: `RaakvlakNaarBuiten` raakt aan
  de kant van de normaal van het richtings-/oorsprongvlak en controleert achteraf of de normaal
  van de as af wijst; anders een vast vlak (`AddFixed`, x = as, y = buiten x as). De automatische
  gravure probeert per oorsprongvlak nog maar die ene kant. `Past` brengt bij een cilinder elk
  punt loodrecht op de as naar het oppervlak (`OpCilinder`, hooguit 40 graden van de raaklijn)
  en controleert daar zonder speling. Niet in Inventor getest.
- **Niet in Inventor getest**: de zoektocht op het gekozen vlak (vooral de duur bij een groot vlak
  met veel gaten), de schets direct op het vlak en de richting van de uitsnede, de naam van de
  feature, en Stempel op een cilinder.

## Recente wijzigingen — 15-10-2026 (Quick Drawing: Uitvoeren, fase 2)

Vraag van de gebruiker: een "agent" die na het 3D-werk de rest afhandelt. Besluit: Uitvoeren
in Quick Drawing wordt die afronder (besluiten in `D:\InventorApp\quick-drawing-spec.md`,
"Uitvoeren als afronder"). Gebouwd is fase 2, nog zonder inchecken.

- **Verloop** (`QuickDrawingKnopService.UitvoerenAsync`): voorcontrole (stopt als het
  hoofddocument of een model niet opgeslagen is), een bevestiging met wat er gaat gebeuren,
  dan **vragen vooraf**: het partnummervenster per onderdeel zonder nummer (met de gravurevraag
  die daar al in zit; Annuleren = overslaan of stoppen), en daarna het model opslaan
  (`Save2(False)`, alleen dat document). Dan **zonder vragen** per onderdeel, samenstellingen
  als laatste: database bijwerken, nieuwe tekening maken, formaten. Aan het eind een rapport
  (ook in het logboek) en mislukte regels in rood.
- **Regels zonder COM**: `QuickDrawingPlan.Uitvoer` geeft `UitvoerStappen` (overslaan bij
  rood of doet niet mee; eerst partnummer; database; tekening maken; nakijken; formaten; wat
  later nog zelf moet). Geen PDF/DXF uit een tekening die verouderd is, open staat of
  nagekeken moet worden. `UiPreview --quicktest` toetst ze (13 nieuwe controles).
- **Nieuwe tekening** (`MaakEnBewaarTekening`): Maak DWG, `TekeningControle` (Cad: aanzichten
  bijgewerkt, binnen het blad, niet over elkaar, geen losse maten), dan opslaan als
  "partnummer omschrijving.dwg" in de tekeningenmap (bestaat die al: niet overschrijven) en
  sluiten. **Maak DWG kan het model wijzigen** (uitslag maken bij plaatwerk zonder uitslag,
  `SaveItemOverridesToBOM` bij samenstellingen): dan eerst het model bewerkbaar maken en
  opslaan, anders verwijst de tekening naar iets wat niet op schijf staat; lukt dat niet, dan
  wordt de tekening niet opgeslagen en staat de regel bij Mislukt.
- **Nakijken**: `QuickDrawingFeiten.NakijkReden` (soort zonder automatisch, of wat de controle
  vond). Status "Tekening nakijken", knop Open; na Open en Terug is hij leeg en mogen PDF/DXF.
  Niet bewaard: een volgende Quick Drawing-sessie ziet de tekening als nagekeken.
- **Nog niet**: inchecken (fase 3; besluit: plaatwerk en printdelen zonder venster via VDF,
  de rest via het Vault-venster aan het eind), bestaande tekeningen bijwerken en revisies
  (fase 4). Verouderde tekeningen krijgen nu alleen hun modelformaten.
- **Niet in Inventor getest.** Let op: of `SaveAs` van de onzichtbaar gemaakte tekening goed
  gaat (en geen vraagvenster geeft), of de controle op echte tekeningen geen valse "overlappen"
  geeft (iso in de hoek, vakken van aanzichten), opslaan van het model na partnummer en na
  Maak DWG (ook bij de hoofdsamenstelling), en hoe lang een run duurt.

## Recente wijzigingen — 27-10-2026 (artikelplaatje in de filestore)

Melding collega (Jan, na uitgave 2026-10-21-1625): "de preview wordt niet goed gemaakt danwel
opgeslagen; alle onderdelen tonen een wit vlak; je moet zoom all doen". Onderzocht:
- In `Filestore\Artikelen` staat sinds de uitgave geen enkel nieuw plaatje (laatste 56261, 21-10
  15:47); de 281 plaatjes sinds eind augustus zijn allemaal goed (3-22% onderdeel). Tot 21-10
  schreven vijf verschillende accounts plaatjes, vrijwel zeker met de oude VBA-knop. De add-in
  schrijft alleen als `filestore.writeThumbnails` aan staat; het sjabloon zette die op **false**
  (bedoeld voor testen), dus sinds iedereen de add-in gebruikt krijgen nieuwe artikelen geen
  plaatje meer ("wit vlak").
- `ThumbnailExporter` had bovendien de zwakte van de VBA zonder diens `View.Update`: Zoom All via
  een commando dat pas na `SaveAs` liep, en alles op het actieve venster (fout bij nummeren vanuit
  een samenstelling of Quick Drawing).

Gewijzigd:
- `ThumbnailExporter`: eigen camera (`TransientObjects.CreateCamera`, `SceneObject` =
  ComponentDefinition, iso rechtsboven, `Fit`, `SaveAsBitmap` 2240x1200 wit). Terugval: actief
  venster, `GoHome`, `Camera.Fit` + `View.Update`, `SaveAs`. Schema tijdelijk Presentation,
  werkgeometrie verborgen zoals voorheen. Logregel zegt welke route. (Apprentice kan de camera
  niet zetten -> E_FAIL bij ViewOrientationType; dus niet buiten Inventor te toetsen.)
- Sjabloon: `writeThumbnails: true` en `instellingenVersie: 2`. `install.ps1` zet bij een
  bestaande installatie zonder `instellingenVersie` (of < 2) `writeThumbnails` eenmalig op true;
  daarna komt `instellingenVersie` uit het sjabloon mee en blijft een eigen keuze staan
  (getoetst op voorbeeldinstellingen). Geldt ook voor `deploy.ps1` (gebruikt install.ps1).
- Test gebruiker 27-10-2026: de eigen camera werkt (56303, logregel "eigen camera"), plaatje goed.
  Wens: geen assenkruis -> `DisplayOptions.Show3DIndicator` tijdens het plaatje uit, daarna terug.
  Getest door de gebruiker en gepubliceerd als `2026-10-27-1334` (terugval `2026-10-21-1625`).

## Recente wijzigingen — 21-10-2026 (Quick Drawing: model van een nieuw, niet opgeslagen deel openen)

- Test gebruiker: dubbelklik op een regel gaf "Dat lukte niet voor Fixture 27 prt01.ipt: The
  parameter is incorrect (E_INVALIDARG)". Het deel was net in de samenstelling gemaakt en stond
  nog niet op schijf (opgeslagen een minuut later); daarom ook een leeg Partnummer (Inventor vult
  dat pas bij opslaan). `Documents.Open` op een pad dat niet bestaat geeft E_INVALIDARG.
- Nieuw: `DocumentOpener.Toon(application, pad)`: geladen document met venster -> dat venster
  actief; geladen zonder venster -> `Documents.Open` als het bestand bestaat, anders
  `Views.Add().Activate()`; niet geladen en niet op schijf -> FileNotFoundException met een
  leesbare melding. Gebruikt in Quick Drawing bij dubbelklik (Model) en vóór de Vault-commando's
  (Inchecken, Laatste versie). Gebouwd, niet gedeployed, niet in Inventor getest.
- **Eerste echte Uitvoeren** (Fixture 27 base assy, na opslaan): 8 partnummers goed, daarna
  (1) elke nieuwe tekening: `DrawingDocument.SaveAs(pad, False)` gaf E_INVALIDARG. Maak DWG zet
  `FullFileName` al op precies dat pad (`ZetVoorgesteldPad`). Nu `BewaarNieuweTekening`: naam
  zetten en `Save2(False)`; lukt dat niet, `Views.Add()` en dan `SaveAs`; het logboek zegt welke
  route werkte. (2) Magnet2: formaten gepland maar export weigerde "geen MDE-artikelnummer".
  `IsMdeDeel` = de database kent het bestand (referentie), maar het partnummer in het model was
  nog "Magnet2" (56234 van 8-10 nooit in het model opgeslagen). `QuickDrawingPlan`: `nummerGoed`
  eist nu ook `ArtikelId` (numeriek partnummer); reden "in de database bekend, maar in het model
  staat 'X' als partnummer" -> knop Partnummer (reserveren op referentie vindt het nummer terug).
  `UiPreview --quicktest`: alles ok. Gebouwd, niet gedeployed, niet in Inventor getest.
- **Tweede test** (Uitvoeren werkt bijna): na rechtermuisknop > Inchecken bleef de tekening open
  en bleef de regel staan. Nu: `VaultVenster(..., vanzelfTerug:=True)` -> `WachtOpInchecken`
  kijkt elke seconde of het bestand alleen-lezen is geworden (Vault zet ingecheckte bestanden
  alleen-lezen; bewust geen Vault-aanroepen, die zouden midden in het Vault-venster komen); twee
  keer achter elkaar = klaar: venster dicht als Quick Drawing het opende (`SluitVenster`: niet
  geladen -> `Close(True)`, geladen onder de samenstelling -> alleen de views), balkje dicht (het
  scherm komt terug, `VerversRegel`), dan verder. Terug in het balkje stopt het wachten.
  `QuickDrawingWindow.ToonBalkje` geeft het balkje terug.
- **Alles inchecken** (knop naast Uitvoeren, `AllesInchekkenCommand`): alle regels met
  `Uitkomst.Inchecken`, onderdelen eerst, samenstellingen laatst; per regel eerst `VerversRegel`
  (een eerdere tekening kan het model al meegenomen hebben), dan het Vault-venster; balkje
  "3 van 9 - ..."; na het inchecken vanzelf de volgende; Terug stopt de ronde.
- **Uitchecken buiten Quick Drawing** (vraag gebruiker): de losse knoppen Partnummer
  (`PartNumberService.MaakBewerkbaar`, na Opslaan in MDE, voor het wegschrijven) en Stempel
  (voor het kiezen van het vlak) checken een ingecheckt part nu ook uit via
  `VaultAddinKoppeling.MaakBewerkbaar`; Quick Drawing deed dat al (partnummer, Maak DWG die het
  model wijzigt). Database bijwerken raakt het model niet en hoeft dus niet uit te checken.
- Gebouwd, `--quicktest` ok, knop licht/donker bekeken; niet gedeployed, niet in Inventor getest.

## Recente wijzigingen — 21-10-2026 (Maak DWG: iso los in de hoek; meting buis/koker -> Buislaser)

- **Maak DWG, schaal** (test gebruiker: koker 500x40x40 op A3 kwam op 1:3, 1:2 paste ruim):
  in het raster stond het iso-aanzicht in dezelfde kolom als het profiel, dus bij een lange buis
  werd die kolom zo breed als het iso-aanzicht; 1:2 paste op papier met 2% marge en viel op de
  echte maten af. `TekeningIndeling` kent nu ook "iso los in de linkerbovenhoek, de rest ernaast of
  eronder": `GrootsteFactor` = max(raster, iso apart), `Past` = raster of iso apart, `Plaatsing`
  zet de rest gecentreerd, of anders gecentreerd rechts naast of onder het iso-aanzicht. Geldt voor
  alle Maak DWG-tekeningen met iso. Doorgerekend met de maten uit de tekening: 1:2 met ca. 35%
  ruimte. Niet in Inventor getest.
- **Buis/koker-herkenning als bewerkingsvoorstel** (Probeer koker: beste gok Buislaser, 5/7 eens,
  dus leeg): gemeten met `ProfielHerkenning` op alle 9.404 archiefparts (Apprentice, scratchpad):
  1.748 herkend, daarvan maar 57% precies Buislaser (ook gezette dozen, draaibussen). Met de buren
  erbij is het segment ca. 96-97%, maar voor de parts die nu nog leeg blijven maar ca. 92%. Gladde
  buizen zijn vaak Zagen (koker <= 18 vlakken: 1 op 4; rond <= 14 vlakken: bijna de helft); bij
  bewerkte buizen zijn de missers vooral "Buislaser + Graveren".
  **Besluiten gebruiker (dezelfde dag):** Graveren is geen aparte bewerking (bij `Weggelaten`,
  net als Zetten/Tappen); zagen doen we eigenlijk nooit, bij buizen telt Zagen als Buislaser.
  Regel in `Voorspel` (na SLS, voor de vaste regels): `ModelKenmerken.Buisprofiel` (gevuld in
  `PartNumberService.LeesKenmerken` met `ProfielHerkenning`) en minstens `MinBurenBuis` (5) van de
  7 buren alleen Buislaser of Zagen, dichtste <= `ZekerAfstandRegel` (3,0) -> Buislaser, zeker.
  Gemeten: 99,1% goed op 1.704 herkende parts, 98,6% op de 361 die anders leeg bleven. `--meet`
  (zonder buisregel, want het archief kent `Buisprofiel` niet): zeker 56,6%, 97,2% goed.
  Probeer koker via Apprentice: Buislaser, zeker.

## Recente wijzigingen — 12-10-2026 (avond: zeker bewerkingsvoorstel, compacter partnummervenster)

Aanleiding: Testje4 (massieve RVS-strip 20x40x150) kreeg Buislaser omdat holle kokers van
dezelfde maat het meest leken. Besluiten gebruiker: vulgraad meewegen; **alleen een voorstel
als het minstens 97% zeker is**; het vergelijkbare product moet aan te wijzen (plaatje) of aan
te klikken zijn; het venster kleiner voor laptops.

- `BewerkingVoorspeller.Vector`: kolom 20 = vulgraad (volume / omhullende doos, max 1,5;
  na schalen x2), Profiel2D telt 2 in plaats van 1. Archief hoeft niet opnieuw.
- `BewerkingVoorstel.Zeker`: alle 7 buren hebben precies dezelfde bewerkingen als het voorstel
  (ook bij een vaste regel) en de dichtstbijzijnde ligt op afstand <= `ZekerAfstand` (2,0).
  Meting `--meet` (per project weggelaten): 86% helemaal goed als beste gok, zeker bij 37,3%
  van de parts en daarvan **97,4% goed**. Vaste regels alleen haalden geen 97% (plaatwerk met
  uitslag 90%, profielmaat 82%, SLS 96%), daarom geldt de buren-check ook voor hen.
  **13-10-2026 bijgesteld** na test (Print3.1, SLS PA-GF: 7/7 eens maar afstand 2,48, dus leeg):
  bij een vaste regel geldt `ZekerAfstandRegel` = 3,0. Gemeten per groep blijft elk boven 97%
  (SLS 97,4%, plaatwerk 97,1%, profiel 97,3%, "lijkt op" 97,4% met 2,0); totaal zeker bij 48,2%
  van de parts, 97,2% goed. SLS alleen op materiaal zou 96,4% zijn (de missers zijn parts met
  materiaal SLS PA12 die toch gefreesd/FDM/gewatersneden zijn).
  **14-10-2026, besluit gebruiker: SLS-materiaal is altijd 3D print SLS.** `VasteRegel` kijkt
  eerst naar SLS-materiaal (wint van plaatwerk/Frame Generator, zoals de oude add-in) en geeft
  `Zeker = True` zonder buren; `Detecteer(document, materiaal, ...)` doet dat ook zonder archief
  of kenmerken. "Lijkt op" toont alleen buren met dezelfde bewerkingen. Meting: zeker bij 56,0%
  van de parts, 97,0% goed.
  **14-10-2026, besluit gebruiker: pas bij 99% of meer overeenkomst "lijkt op" of dubbel noemen.**
  "Lijkt op" komt niet meer van de 7 buren van de voorspeller (die leken soms nauwelijks) maar
  van `Core\Bewerkingen\Gelijkenis`: overeenkomst = 1 - grootste relatieve verschil over
  D1-D3, volume, oppervlak, randen, hoekpunten, bodies en vlakken per soort; minimaal 0,99, los
  van het bewerkingsvoorstel en van het materiaal; wat al bij "Mogelijk al bestaand" staat komt er
  niet nog eens. Percentage achter de naam en in het wolkje (naar beneden afgerond). Op het
  archief heeft 11,5% van de parts zo'n tegenhanger (bv. Cable support 495/500, 99,02%). De
  dubbelcheck was al strenger (vrijwel identiek) en is ongewijzigd. `BewerkingSuggestie.Gelijkend`
  is vervallen.
- `BewerkingDetector.Detecteer(document, kenmerken, voorspeller, overslaan)`: part vult alleen
  bij `Zeker`; zonder archief of kenmerken geen voorstel meer (de oude regels
  Frame Generator/plaatwerk/SLS en `DetecteerPart` zijn weg). Samenstelling blijft
  Lassen/Assemblage. Het logboek noemt de beste gok als die niet zeker genoeg was.
- Partnummervenster: onder Bewerking 3 "Lijkt op:" met de 2 meest gelijkende parts als link
  (alleen bij een zeker voorstel). Aanwijzen = wolkje met het artikelplaatje uit de filestore
  (`ArtikelAfbeelding.Zoek`, pas geladen bij openen), nummer, plek, materiaal, maten,
  bewerkingen. Klikken = `Documents.Open` met `SilentOperation`, daarna de vorige `ActiveView`
  weer actief (gravure en thumbnail werken met het actieve document); melding onderin. De
  namen in "Mogelijk al bestaand" werken hetzelfde. Gemeenschappelijke rij: `ArchiefPartRij`
  (DubbelRij erft ervan). `BewerkingReden` is vervallen.
- Venster compacter: 1000 breed, hoogte volgt de inhoud (`SizeToContent`, ca. 680 incl.
  titelbalk; was 1140x880), begrensd op `SystemParameters.WorkArea` (dan scrolt links en komt de
  description in beeld). Rechterkolom 250, voorbeeld max 220 hoog, kleinere marges.
- Niet in Inventor getest: openen vanuit het (modale) venster, wolkje in Inventor, hoogte op
  een laptop. Wel: build, `--meet`, Testje4 via diag (Plaatlaser, niet zeker -> leeg),
  buiten-beeld-render licht/donker.

## Recente wijzigingen — 12-10-2026 (later: Maak DWG voor buis/koker, partnummer-gravure)

Besluiten van de gebruiker staan in `D:\InventorApp\buis-en-gravure-spec.md`.

- **Herkenning buis/koker** (`Cad\ProfielHerkenning`): koker = twee loodrechte
  normaalrichtingen met elk buitenvlakken op de uiterste afstand en een groot binnenvlak op
  gelijke wanddikte; ronde buis = precies twee stralen om één as. Alles moet binnen de
  buitenmaat liggen en de holte moet over minstens 80% van de lengte doorlopen. Kiest buiten-
  en binnenvlakken op **plek en oppervlak**, niet op de richting van `Plane.Normal` (die wijst
  niet altijd naar buiten). **Plaatwerk telt mee**: veel buizen/kokers zijn bij MDE als
  plaatwerk getekend. Getoetst met `tools\BewerkingenArchief --profielen <archief.csv>` (Apprentice):
  150 van 150 buislaserdelen herkend (115 koker, 35 rond).
- **Maak DWG, bladsoort `Profiel`** ("Buis / koker" in Quick Drawing): alleen als Bewerking 1 in
  `TekeningInstellingen.BewerkingenProfiel` staat (standaard `BUISLASER; ZAGEN`) of leeg is; anders
  kregen ook gezette dozen, bussen en deksels deze opzet. Blad `BladProfiel` (standaard A3) en
  **nooit een groter blad** (`DeelIn(..., groterBlad:=False)`). Vooraanzicht = zijkant met de as
  **horizontaal** (`AsIsVerticaal` via de camera van het aanzicht, niet op breed/hoog), kijkrichting
  loodrecht op de as (`KiesProfielRichting`); links het profiel op dezelfde schaal, plus boven en
  iso. Maten: lengte op het vooraanzicht; op het profiel breedte/hoogte (`PlaatsBuitenmaten`) en
  de wanddikte als echte maat tussen de eindranden van buiten- en binnenwand (koker; bij voorkeur
  de staande wand, zodat de maat horizontaal onder het profiel staat — wens na de eerste test) of de
  rechterkant van buiten- en binnencirkel (rond, plus Ø). Ligt de as niet langs een modelas,
  dan de gewone opzet (melding via `meldFout`).
- **Partnummer-gravure**: `Cad\PartnummerGravure` herkent "#nummer" (of alleen het nummer) in een
  schets die door een niet-onderdrukte feature gebruikt wordt (ook zelf gemaakte).
  `Commands\GravureControle` na Opslaan in het partnummervenster bij Plaatlaser/Buislaser: goed =
  niets, ander nummer = aanbieden bij te werken (alleen de tekst), ontbreekt =
  `Views\GravureVraagWindow` (Automatisch / Zelf plaatsen / Niet nu). Bij Genereer formaten en
  FORMAT in Quick Drawing alleen een opmerking in de status ("geen gravure").
- **Automatisch plaatsen** (`Cad\GravurePlaatser`, in een Transaction): plaatwerk eerst de
  binnenkant (normaal naar het zwaartepunt van de hoekpunten; buiten bepaald met
  `SurfaceBody.IsPointInside`), koker grootste buitenvlakken, ronde buis de buitencilinder (raakvlak
  evenwijdig aan een oorsprongvlak langs de as), tekst langs de as; vanaf het midden naar buiten
  (`Core\Gravure\Gravurezoeker`). **Past-controle**: controlepunten van het tekstkader + 3 mm marge
  van schets naar model, `Face.GetClosestPointTo`; afstand > tolerantie (bij een cilinder de
  hoogte boven het raakvlak erbij) = buiten het vlak of boven een gat. Daarna de echte RangeBox
  nog eens, en na het uitsnijden `HealthStatus`. De knop **Stempel** doet nu dezelfde controle en
  vraagt bij "past niet" of het mag blijven (anders `Abort`).
- **Niet in Inventor getest** (alles hierboven): kijkrichting en as-horizontaal via `Camera`,
  `DrawingCurves(edge)` voor de wandlijnen, de maten zelf, `GetClosestPointTo` op een raakvlak aan
  een cilinder, de uitsnederichting op dat raakvlak, en of `PlanarSketch.Dependents` de feature geeft.

## Recente wijzigingen — 12-10-2026 (bewerkingsvoorstellen en dubbelcheck)

Spec met de besluiten van de gebruiker en de meting: `D:\InventorApp\bewerkingen-spec.md`
(hoofdstuk 0). Alles blijft binnen MDE: geen externe AI-dienst.

- **Bewerkingen voorstellen** (partnummervenster, dus ook Quick Drawing): eerst vaste regels
  (Frame Generator -> Buislaser; plaatwerk **met uitslag** -> Plaatlaser; printmateriaal ->
  printsoort), anders "lijkt op eerdere parts": de 7 meest gelijkende parts uit het
  bewerkingen-archief stemmen (`Core\Bewerkingen\BewerkingVoorspeller`). Altijd een voorstel
  (besluit gebruiker); **Zetten en Tappen worden niet meer voorgesteld** (overtollig). Onder
  Bewerking 3 staat een grijs regeltje "Voorstel: ..." met de reden. Namen worden opgezocht in
  `InventorBewerkingen`; een onbekende naam valt weg (logboek). Zonder archief of bij een
  leesfout gelden de oude vaste regels (`BewerkingDetector.DetecteerPart`, nu zonder Zetten/Tappen).
  **Vervangen dezelfde avond**: alleen nog een voorstel als het zeker is, zie de sectie hierboven.
- **Dubbelcheck**: rechts boven het voorbeeld een oranje kaart "Mogelijk al bestaand" met parts
  die precies dezelfde vorm en hetzelfde materiaal hebben en een eigen MDE-nummer
  (`Core\Bewerkingen\DubbelZoeker`). Vingerafdruk: aantallen vlakken per soort, randen,
  hoekpunten, draadvlakken, volume en oppervlak (rel. 2e-6), spreiding van de hoekpunten
  (eigenwaarden) en "hand" tegen spiegelbeelden (`Vormmaat`). Getoetst op 2.288 parts met
  een grove tweeling (11-10-2026): zelfde oordelen als een volledige vergelijking. Een
  base/bronpart zonder MDE-nummer telt niet mee.
- **Kenmerken** uit het model: `Cad\ModelKenmerkenLezer` (alle solid bodies; doosmaten,
  draaibaar en 2D-profiel van het grootste body). Uitslagmaten uit de iProperties "Sheet Metal
  Width/Length" (ook in Apprentice; `FlatPattern.Length` geeft daar E_NOTIMPL). Inventor-fouten
  komen ook als `NotImplementedException` binnen, niet alleen als `COMException`.
- **Archief**: CSV op `\\172.16.1.4\Data\INVENTOR\Apps\InventorTools\data\bewerkingen-archief.csv`
  (`appsettings.json` > `bewerkingen.archiefPad`, die waarde is ook de standaard in code).
  `Commands\BewerkingArchiefLader` leest hem bij het starten van de add-in op de achtergrond;
  het venster wacht hoogstens 3 s. Mislukt (share weg), dan na 10 min opnieuw; na 8 uur ververst.
- **Opbouwen**: `tools\BewerkingenArchief` (**net48**, x64): leest alle .ipt onder
  `D:\Working Folder\Designs` met Inventor **Apprentice**, zonder dat Inventor open hoeft.
  Apprentice werkt alleen vanuit .NET Framework (in .NET 8/10 gaf `ComponentDefinition` E_FAIL);
  daarom zijn de gedeelde bestanden gekoppeld (`Core\Bewerkingen\*.vb`,
  `Cad\ModelKenmerkenLezer.vb`): **houd die net48-proof** (geen `String.Join(Char, ...)`,
  `Contains(String, StringComparison)` enz.). De COM-registratie van Apprentice wees op deze pc
  naar een verwijderde Inventor 2024; het programma laadt `RxApprenticeServer.dll` van 2026 direct
  (`Apprentice.Start`, met `Components\2026\...` op PATH). **Na bepaalde bestanden geeft
  Apprentice in dat proces op elk volgend bestand E_FAIL op `ComponentDefinition`** (los geopend
  werkt hetzelfde bestand gewoon; een verse Apprentice-sessie in hetzelfde proces helpt niet).
  Daarom verdeelt `Opbouwen` het werk over deelprocessen (`--deel <lijst>`, 400 bestanden); een
  deelproces stopt bij de eerste COM-fout en een vers proces gaat verder vanaf dat bestand. Faalt
  een bestand als eerste in een vers proces, dan telt het als fout. Ook `SurfaceEvaluator.Area`
  kan op een enkel vlak E_FAIL geven (dan `Onvolledig`). Ca. 0,3-0,7 s per part.
  Af en toe opnieuw draaien vanaf de pc van de gebruiker:

  ```powershell
  dotnet build tools\BewerkingenArchief\BewerkingenArchief.vbproj -c Release --nologo
  tools\BewerkingenArchief\bin\Release\net48\BewerkingenArchief.exe
  tools\BewerkingenArchief\bin\Release\net48\BewerkingenArchief.exe --meet <archief.csv>
  ```

  `--meet` voorspelt elk part met zijn eigen project weggelaten en telt de dubbelingen in het
  archief. Logboek: `%LOCALAPPDATA%\MDE\InventorTools\bewerkingen-archief.log`.
- **Eerste archief** (12-10-2026 13:26, 175 min): 9.823 parts (9.404 met bewerking, 99 projecten),
  1 fout. `--meet`: 86% alles goed, 88% hoofdbewerking (oude regels 57/59%); 96 dubbelparen, vaak
  `_MIR`-platen (een vlakke plaat omgedraaid is hetzelfde onderdeel, dus "of het spiegelbeeld").
  `IsMdeNummer` = 4-6 cijfers en onder 150.000: daarboven is het een leveranciersnummer.
- `tools\UiPreview` toont in het partnummervenster nu een reden en twee nepdubbelingen.
- **Niet in Inventor getest**: de kenmerken uit een open part (zelfde code als de scan, maar
  in-process), de tijd die dat kost bij grote parts (staat in het logboek), en de 3 s wachttijd
  op het archief bij een trage share.

## Recente wijzigingen — 8-10-2026 (Quick Drawing fase 1, Kopieer uit filestore)

De spec met de besluiten van de gebruiker staat in `D:\InventorApp\quick-drawing-spec.md`
(hoofdstuk 0 gaat voor op de rest). Fasen: 1 overzicht + knoppen per regel (dit), 2
Uitvoeren zonder Vault (nummers een voor een, database, tekeningen opslaan, formaten),
3 uit- en inchecken, 4 bestaande tekeningen bijwerken met controles.

- **Knop Quick Drawing** (`MDE:Tools:QuickDrawing`, pictogram "QUICK"/"QD" rood). In
  assembly en part (in een tekening: melding). Op het MDE-tabblad in paneel DWG, en in de
  gastpanelen van part en assembly naast Maak/Open DWG.
- **Opbouw**: `Commands\QuickDrawingKnopService` verzamelt (assy: `AssemblyCollector` met
  samenstellingen en onzichtbare delen), vult aan (`Deelverrijking.Vul`, `QuickDrawingLezer`,
  database, filestore, bestellingen, Vault) en opent `Views\QuickDrawingWindow` (opzet van
  Genereer formaten, per regel knoppen DWG en FORMAT, Uitvoeren nog uitgeschakeld). De regels
  zelf staan zonder COM in `Core\QuickDrawing\QuickDrawingPlan.vb` (`QuickDrawingFeiten` in,
  `QuickDrawingStatus` uit); `UiPreview --quicktest` loopt ze na, `UiPreview quickdrawing`
  toont het scherm met nepdata.
- **Doet niet mee** (`QuickDrawingLezer`): Content Center buiten de werkmap of in de
  Content Center-map; een Content Center-deel in de werkmap (Frame Generator) doet wel mee.
  Bibliotheekmappen uit `gebruiker.json` > `quickDrawing.uitgeslotenMappen` (standaard
  `MDE Library`, ten opzichte van de werkmap van het project; nog geen instellingen-tab).
  Buiten de werkmap. iPart/iAssembly = nummer handmatig. Gekocht (BOM Purchased) of
  inkoopdeel: geen tekening of formaten zonder bewerking.
- **Soort**: `TekeningMaker.BepaalSoort` (nieuw, Shared) met dezelfde regels als Maak DWG;
  automatisch = Plaatwerk, Plaatwerk met zetting, Printdeel.
- **Verouderd** = lokale opslagtijd model later dan tekening (of dan het filestore-bestand)
  plus 1 minuut. PDF/DXF uit de tekening, STP/STL/DWF uit het model (zoals ExportService).
- **Kopie**: numeriek partnummer dat in `Artikelen` bij een andere `InventorReference` hoort.
  `ArtikelRecord` heeft daarvoor `InventorReference`; `ArtikelRepository.LoadMeerdere` en
  `Verschillen` (de oude `IsGewijzigd`, nu met veldnamen) zijn nieuw.
- **Al besteld**: nieuwste orderregel over alle projecten
  (`BestellingRepository.GetBestellingenVoorNummers`). Model opgeslagen na de besteldatum =
  rood "gewijzigd na bestelling"; anders "al besteld" als tekening of formaat verouderd is.
- **Vault**: alleen met een lopende sessie (geen aanmeldscherm), model en tekening via
  `ZoekBestanden` in groepen van 50, en `ZoekTekeningen` voor MDE-delen zonder lokale tekening.
- **Knoppen per regel**: DWG = Maak (TekeningMaker, tekening blijft open), Open, Ophalen
  (VaultService); bij Maak en Open verbergt het scherm zich en blijft het balkje staan
  (`QuickDrawingWindow.ToonBalkje`, zelfde opzet als Bekijk part). FORMAT =
  `ExportService.ExporteerRijAsync` (nieuw) met de ontbrekende of verouderde formaten van de
  gekozen vinkjes, zonder bevestigingsvraag; daarna de bijlages van die regel opnieuw.
  `ExportRij` is daarvoor niet meer NotInheritable (`QuickDrawingRij` erft ervan).
- **Strikte koppeling tekening - partnummer** (`Core\Tekeningnaam.HoortBij`): de naam begint
  met het nummer en het teken erna is geen cijfer. Geldt nu in `TekeningScanner.Zoek` (dus
  ook Open DWG, Maak DWG en Collect Parts) en in `VaultDienst.ZoekTekeningen` (daar telt de
  paginering nu de opgehaalde treffers, niet de gefilterde). Voorheen vond 1032 ook 10320.
- **Knop Kopieer uit filestore** (`MDE:Tools:KopieerFilestore`, "KOPIE"/"KP" roze) in het
  lint: zelfde scherm als in Collect Parts, voor het part, het model van de tekening, of de
  selectie in een samenstelling (`Commands\KopieerKnopService`). Op het MDE-tabblad in Artikel,
  in de gastpanelen van part en drawing. `KopieerService.Toon` hangt het scherm aan Inventor
  als er geen eigenaar is; `Deelverrijking` heeft `VulMdeStatus` en `VulBijlages` erbij.
- **Niet in Inventor getest.** Let vooral op: de paden van werkmap en Content Center uit het
  project (staan in het logboek), hoe lang het bekijken duurt bij een grote samenstelling
  (tijden per stap in het logboek; het gewicht van samenstellingen wordt voor de
  databasevergelijking gerekend), het verbergen en terugkomen van het scherm bij Maak/Open,
  en de knop FORMAT op een onderdeel dat in de samenstelling open staat.

**Tweede ronde (8-10-2026, na de eerste test door de gebruiker):**

- **Tekening alleen in Vault is in orde**: geen knop Ophalen meer (`DwgActie.Ophalen` is weg).
  Verouderd gaat dan op de opslagtijd in Vault (`VaultTekening.Gewijzigd`, nu in lokale tijd).
  Open en PDF/DXF halen hem eerst stil op (`QuickDrawingKnopService.HaalTekeningOp`).
- **Vinkjes**: "Content Center en bibliotheek verbergen" (standaard aan), "Verberg wat klopt"
  (uit), "Plaatwerk en printdelen zonder nakijken" (aan); onthouden in
  `gebruiker.json` > `quickDrawing`. Filteren via `QuickDrawingViewModel.Weergave`
  (ICollectionView). Wordt een regel door een knop groen terwijl "Verberg wat klopt" aan
  staat, dan staat hij 3 s grijs (`Verdwijnt`, Opacity 0.4) en gaat dan uit de lijst
  (op verzoek van de gebruiker; `UiPreview --quicktest` controleert het).
- **Geen tweede Maak**: de InternalName van een met Maak gemaakte tekening wordt onthouden;
  staat hij nog open en bestaat het bestand nog niet, dan `TekeningOpenNietOpgeslagen` en zet
  de knop Open die tekening weer voor.
- **Knop DWG = Partnummer** zolang er geen geldig nummer is (`DwgActie.Partnummer`):
  `PartNumberService.Uitvoeren(document, eigenaar)` (nieuw, geeft True bij opslaan). Daarvoor
  `VaultAddinKoppeling.MaakBewerkbaar`: via de automation van de Vault-add-in van Inventor
  (`interop.vaultaddinlib.dll` uit Inventor\Bin, ingebed met EmbedInteropTypes) eerst
  `CheckFileStatus`, zo nodig `CheckOutFile`. Zonder add-in en een schrijfbaar bestand gaat het
  gewoon door. Inchecken komt met de tekening, via het standaard Vault-venster (volgende fase);
  `VaultAddinKoppeling.LogCommandos` zet een keer per sessie de Vault-commando's van Inventor in
  het logboek, om het juiste Check In-commando te vinden.
- **Fout gevonden in de test: Genereer formaten sloot het document na de export altijd**
  (`Close(True)`, zonder opslaan), ook als het al open stond. In Quick Drawing ging zo de
  hoofdsamenstelling dicht, met E_FAIL op de volgende knop; een tekening met niet-opgeslagen
  werk zou zijn wijzigingen kwijtraken. Nu `Cad\DocumentOpener`: `ExportService` sluit alleen
  wat hij zelf laadde, en opent een document dat al geladen is (onder de samenstelling) zonder
  extra venster. Ook Bekijk part/tekening in Collect Parts sluit bij Terug geen document dat de
  gebruiker al open had. Quick Drawing zoekt een niet meer bruikbaar model opnieuw op pad
  (`ModelVan`) en meldt het anders netjes. Nog te testen: export van een deel dat alleen
  onzichtbaar onder de samenstelling geladen is (geen eigen venster meer).
- **Uitchecken via de Vault-add-in werkt** (test 8-10-2026: `CheckOutFile` gaf
  `kCheckedOutSuccessfully`, zonder venster). De Vault-commando's van Inventor staan in het
  logboek; Check In op het actieve document is `VaultCheckinTop`.
- **Vierde ronde: openen en inchecken per regel.** Dubbelklik op een regel = 3D model openen
  (niet op een knop in de regel); rechtermuismenu (`Regelmenu`, x:Shared=False, via de
  `BindingProxy` "Context") met Open 3D model, Open 2D tekening en Inchecken... Inchecken opent
  de tekening (als die zelf nieuw of door jou uitgecheckt is; dat Vault-venster neemt het model
  mee) of anders het model, maakt het actief en voert `VaultCheckinTop` asynchroon uit
  (`Execute2(False)`) nadat het scherm verborgen is. Na Terug ververst `VerversRegel` alles van
  die regel, ook Vault. In de regels: `QuickDrawingStatus.ModelInchecken`/`TekeningInchecken`
  en het punt "inchecken" in de statustekst (eerder stond er oranje "Klopt"). Donkere opmaak
  voor ContextMenu/MenuItem/Separator in `TemplatesDonker.xaml`; `UiPreview quickdrawing --menu
  --png` legt het menu vast. `TekeningMaker.ZetVoorgesteldPad` vangt nu ook ArgumentException
  (E_INVALIDARG als er al een tekening met die naam open staat).
- **Laatste versie uit Vault** (rechtermuismenu, op verzoek): via Vault's eigen Refresh
  (`VaultGetLatest`, vroeger Get Latest) op het model, of op de tekening als alleen die ouder
  is; zelfde route als Inchecken (`VaultVenster`). Die laadt het nieuwe bestand ook in Inventor
  opnieuw in, wat zelf bestanden vervangen niet doet. Op de hoofdsamenstelling toont Vault alles
  eronder; daarom ververst Terug dan de hele lijst (`VerversAlles`). Regels: `ModelOuder`,
  `TekeningOuder`, `ModelInVault` (het menu mag bij elk model in Vault).
- **Niet getest in Inventor**: of `ApplicationAddIn.Automation` van de Vault-add-in
  (`{48B682BC-42E6-4953-84C5-3D253B52E77B}`) echt een `IVaultApplication` is (anders staat het
  type in het logboek), of `CheckOutFile` zonder venster werkt, en het partnummervenster boven
  Quick Drawing.

## Recente wijzigingen — 7-10-2026 (Plak uit Vault)

- **Nieuwe knop Plak uit Vault** (`MDE:Tools:VaultPlakken`, pictogram "PVault"/"PV" magenta,
  sneltoets Ctrl+Shift+V). Op het MDE-tabblad van Assembly (paneel Model) en in het
  gastpaneel op Assemble. Werkt alleen in een samenstelling.
- **Werking**: eerst de selectie in de Vault-client lezen (`Vault\VaultClientLezer.vb`,
  zie hieronder), en is daar geen part/assy geselecteerd het klembord
  (`Commands\Klembord.vb`, Win32, voor bestanden gekopieerd in Windows Verkenner) ->
  `Core\Vault\VaultPlakken.vb` (`KlembordLezer`: bestandslijst, `$/`-paden, lokale paden,
  namen op .ipt/.iam; mappen en andere bestanden naar Overgeslagen) ->
  `IVaultSessie.HaalLaatsteOp` (`Vault\VaultPlakker.vb`) -> `PlaatsWachtrij` hangt de
  bestanden een voor een aan de cursor via `Cad\ComponentPlaatser`
  (`PostPrivateEvent(kFileNameEvent)` + `StartCommand(kPlaceComponentCommand)`). Het
  volgende bestand komt na `OnTerminateCommand` van het Place-commando (alleen als dat
  eerst door deze rij actief werd), via de dispatcher. Esc = volgende; de rij stopt bij
  een ander actief document. Elke plaatsing is een eigen Ctrl+Z, zoals bij Place
  (afgesproken met de gebruiker).
- **Ophalen alleen als lokaal ouder of ontbrekend** (keuze gebruiker). `AcquireFiles`
  met kinderen recursief, laatste versie, bibliotheek (Content Center) mee, overschrijven
  op `NoOverwrite`; per bestand beslist `PreFileAcquire` op `FileAcquisitionInfo`:
  gelijk -> overslaan; uitgecheckt door jou / lokaal nieuwer / lokaal gewijzigd ->
  laten staan; `FileStateUknown` met lokaal bestand -> laten staan (voorzichtig); open in
  Inventor -> laten staan; anders ophalen. Per bestand komt een regel in het logboek
  (wat Vault zei, beslissing, resultaat).
- **Opzoeken**: Vault-pad of lokaal pad in de werkmap via `FindLatestFilesByPaths`; een
  lokaal pad buiten de werkmap of een kale naam op `ClientFileName` (SrchOper 3).
  Meerdere treffers = niet plaatsen en de mappen noemen. Niet in Vault maar wel lokaal =
  zo plaatsen (ook zonder Vault-verbinding, behalve bestanden uit de tijdelijke map).
- **Vault Basic geeft zijn selectie niet door** (uitgezocht 7-10-2026): Ctrl+C in de
  Vault-client zet niets op het Windows-klembord; slepen geeft alleen het formaat
  `VaultDataFormat` met live objecten, buiten Vault niet te lezen (DV_E_TYMED); en eigen
  menukeuzes kunnen niet, want `Autodesk.Connectivity.Explorer.Extensibility.dll` ontbreekt
  in Vault Basic (2024 en 2026). Er is ook gekeken naar "Create Shortcut": die schrijft
  `%APPDATA%\Autodesk\VaultCommon\Servers\Services_Security_*\<server>\Vaults\<vault>\Objects\Shortcuts.xml`
  met `vaultfilepath:$/...`; niet gebruikt omdat de gebruiker die lijst als favorieten
  bijhoudt.
- **Selectie lezen via MSAA** (op verzoek van de gebruiker: selecteren in Vault, knop in
  Inventor). `VaultClientLezer` zoekt het hoofdvenster van `Connectivity.Vault*`, loopt de
  zichtbare kindvensters langs met `AccessibleObjectFromWindow`, en neemt de bovenste
  DevExpress-lijst met MSAA-naam `MainView` die een kolom Name heeft (onderin kan een
  tweede lijst staan, bijvoorbeeld Uses). Kinderen: `Header Panel` (kolomkoppen) en
  `Data Panel` (alle regels, ook buiten beeld). Een regel: rol 34, staat 0x2 =
  geselecteerd; cellen in kolomvolgorde, de naam staat in de waarde van de cel. Alle regels
  worden nagelopen (ongeveer 1 s per 1000): `accSelection` op de lijst geeft de regel met
  focus, ook als die niet geselecteerd is. De map: de selectie in de grootste
  SysTreeView32 (alleen de naam; het volledige pad is via MSAA niet betrouwbaar te krijgen,
  het terugrekenen via niveaus gaf een verkeerd pad). `VaultPlakker` zoekt op bestandsnaam
  en kiest bij meerdere treffers het bestand in een map met die naam. Getest buiten
  Inventor tegen de draaiende Vault-client (selectie gelezen, 52 regels in 0,2 s); niet
  met meerdere geselecteerde bestanden en niet in Inventor.
- **Sneltoets**: `RibbonKnop` heeft een optionele `sneltoets`; `RibbonBuilder.ZetSneltoets`
  zet hem alleen als geen ander commando (default of override) hem heeft en de gebruiker
  er zelf geen koos. `DefaultShortcut` zetten gaf in Inventor 2026 E_FAIL; daarna wordt
  `OverrideShortcut` geprobeerd. Een paar bestaande sneltoetsen komen in het logboek om de
  schrijfwijze te vergelijken.
- **Getest door de gebruiker (7-10-2026)** en gepubliceerd als `2026-10-07-0803`: meerdere
  geselecteerde bestanden gelezen (999 regels in 2,8 s), ontbrekend bestand opgehaald
  (`FileAcquisitionInfo` `None`), gelijke bestanden blijven staan (`FileOnDiskIsTheSame`),
  plaatsen werkt. Nog te controleren: zoekresultaten in Vault, of het Place-commando de geposte bestandsnaam pakt en zo heet
  (`AssemblyPlaceComponentCmd`, staat in het logboek), het doorschuiven naar het volgende
  bestand, welke `FileAcquisitionInfo` Vault geeft voor een oudere lokale versie (verwacht
  `None`) en hoe vaak `FileStateUknown` voorkomt, en de sneltoets.

## Recente wijzigingen — 7-10-2026 (lint opnieuw ingedeeld, Sheetsize)

- **Indeling in een tabel**: `Ribbon/LintIndeling.vb`. Na de eerste test koos de
  gebruiker: het **MDE-tabblad toont in part, assembly en drawing altijd alle knoppen**
  (panelen Artikel / DWG / Model / Tekening / Instellingen, overal gelijk; een knop die
  in die omgeving niet werkt geeft zijn melding). Het **kleine MDE-paneel aan het eind
  van Inventors eigen tabbladen** (3D Model, Sheet Metal, Flat Pattern, Assemble, Place
  Views) toont alleen wat in die omgeving werkt, in groepjes met een scheidingslijn
  (`CommandControls.AddSeparator`): part = Get Partnumber, Genereer formaten, 3D Print |
  Maak DWG, Open DWG | Stempel, Derive, Schermafdruk; assembly = Get Partnumber, Collect
  Parts, Genereer formaten, 3D Print | Maak DWG, Open DWG | Plak uit Vault, Schermafdruk;
  drawing = Get Partnumber, Genereer formaten | Sheetsize, Shaded, Hole notes,
  AutoDimension. AutoDimension staat daarnaast nog bij Annotate > Dimension.
  `RibbonKnop.InNatiefTabblad` is vervallen.
- **Opruimen bij elke start**: `RibbonBuilder` verwijdert op het MDE-tabblad alle
  panelen waarvan de interne naam met `MDE:Tools:` begint (ook het oude
  `MDE:Tools:Artikelen`) en op alle tabbladen het `MDE:Tools:Gastpaneel`, en bouwt dan
  opnieuw. Zo verdwijnt een oude indeling ook bij collega's. Knopnamen
  (`MDE:Tools:...`) zijn ongewijzigd, dus Quick Access Toolbar-knoppen blijven werken.
- **Sheetsize, eigen maat**: Inventor negeert een nieuwe `Sheet.Width/Height` stil zolang
  het blad op een standaardformaat staat. Nu eerst `Size = kCustomDrawingSheetSize`, dan
  `Orientation`, dan hoogte en breedte (lange kant volgt de oriëntatie), nameten en zo
  nodig andersom; klopt het dan niet, `SheetSizeException`. Het geheel zit in een
  `Transaction` (een Ctrl+Z; bij een fout `Abort`), en de uitkomst komt in het logboek.
- **Donker thema: keuzelijst** (`ListBox`/`ListBoxItem` in `TemplatesDonker.xaml`); de
  standaardlijst had een vaste witte achtergrond met lichte tekst. `tools/UiPreview`
  kent nu `sheetsize`.
- **Niet in Inventor getest**: het opruimen en opbouwen van de panelen bij het starten,
  de scheidingslijnen, en de eigen sheetmaat (welke volgorde Inventor echt wil).

## Recente wijzigingen — 6-10-2026 (later: Collect Parts, kolom Vault en verouderde formaten)

- **Kolom Vault** (vinkje Vault onder KOLOMMEN, `AssySchermInstellingen.ToonVault`).
  Per onderdeel het modelbestand (`VolledigeBestandsnaam`) en, als die lokaal gevonden
  is, de tekening (`TekeningPad`). Regels in `Core\Vaultstatus.vb`: groen `v12` (gelijk),
  oranje uitgecheckt (door jou / door naam), lokaal gewijzigd, lokaal ouder (lokale
  opslagtijd tegenover `File.ModDate`, 2 s speling), rood niet in Vault, grijs buiten de
  werkmap en Content Center (wordt niet opgezocht).
- **Opzoeken**: lokaal pad wordt `$/...` via `WorkingFoldersManager.GetWorkingFolder("$")`,
  dan een `DocumentService.FindLatestFilesByPaths` per groep van 50 onderdelen (niet
  gevonden = `Id` -1). Naam van wie uitcheckte via de eigenschap `CheckoutUserName`
  (de gebruikerslijst mag alleen een beheerder lezen). Tijden van Vault worden als UTC
  gelezen en naar lokale tijd gezet; het eerste gevonden bestand komt met Vault- en
  lokale tijd in het logboek, om een tijdzonefout snel te zien.
- **Bewust op de hoofdthread** (`DispatcherPriority.Background`, groep voor groep):
  Vault gaat via de sessie van Inventor zelf. Geen aanmeldscherm vanuit Collect Parts;
  zonder sessie staat er een melding onderin.
- **Verouderd**: een PDF/DXF/STP/STL/DWF in de filestore heet verouderd als de bron in
  Vault later is opgeslagen (ModDate, niet CkInDate: exporteren voor inchecken mag) dan
  het bestand is toegevoegd, met 1 minuut klokmarge (filestore-tijd = `NOW()` van de
  databaseserver). STP/STL: model; PDF/DXF/DWF: nieuwste van model en tekening. Cel
  wordt oranje "Verouderd" (zonder dagen, die passen niet), het wolkje noemt de bron.
  Alleen als de kolom Vault aanstaat.
- `Bestelkleur` heet nu `Statuskleur` (`Core\Statuskleur.vb`), gedeeld door Besteld en
  Vault, met de sorteervolgorde in `Statuskleuren.Rang`.
- `tools\UiPreview`: `assy` toont ook nep-Vault; `--vaulttest` loopt de Vault-regels na.
- **Niet getest tegen echte Vault**: de padvertaling via de werkmap, de tijdzone van
  `ModDate`/`CkInDate`, `CheckoutUserName` als systeemnaam, en of "Tekening uit Vault"
  na de ombouw (id in plaats van het File-object) nog werkt.

## Recente wijzigingen — 6-10-2026 (Collect Parts: bestelstatus, project per dag)

- **Kolom Besteld** (vinkje Bestellingen onder KOLOMMEN, onthouden in
  `AssySchermInstellingen.ToonBestellingen`). Blokje + tekst per regel, binnen het
  gekozen project. Regels in `Core\Bestelstatus.vb` (geen COM, geen WPF):
  rood = besteld < QNT, oranje = geleverd < QNT, groen = geleverd >= QNT (reserve die
  nog openstaat telt niet), grijs = geen MDE-nummer (oud nummer telt wel, zoals het
  filter Part nr.), hoofdsamenstelling geen kleur.
- **Geleverd** = `SUM(OrderItemsDelivered.Quantity delivered)` per orderregel via
  `OrderItem-id` (zelfde opbouw als de view `@OrderedDeliveredItems`). `Orderitems`
  zelf heeft geen geleverd-kolom; `Orders.AllesIsGeleverd` wordt niet gebruikt.
  Statussen 0-6 tellen mee (7 = geannuleerd), zoals het Bestellingen-scherm.
- **Twee sporen**: inkoopdeel + bestelnummer + minstens een bewerking = Basis
  (bestelnummer) en Bewerking (MDE-nummer); de cel krijgt de slechtste kleur, bij gelijk
  gaat Basis voor. Inkoopdeel zonder bewerking: beide nummers tellen samen. Afspraak met
  de gebruiker: een bewerking op een inkoopdeel wordt altijd extern besteld.
- **Ophalen op de achtergrond**: `AssyViewModel.VerversBestellingen` zet eerst 'laden',
  daarna `Task.Run` met groepen van 100 nummers
  (`BestellingRepository.GetProjectBestellingen`, een query per groep) en per groep
  terug via de dispatcher. Afgebroken bij nieuw project, re-scan en sluiten.
- **Sorteren** op Besteld via `DataGrid.Sorting` + `ListCollectionView.CustomSort`
  (`BestelVolgorde`): eerste klik rood-oranje-groen, tweede groen-oranje-rood; grijs en
  'laden' altijd onderaan, hoofdsamenstelling bovenaan. Na het laden een keer opnieuw
  sorteren, niet per groep. Dubbelklik op Besteld opent direct het Bestellingen-scherm.
- **Project per dag**: `GebruikersInstellingen.ProjectKeuze` (`projectKeuze` in
  gebruiker.json, los van `assyScherm` omdat dat bij sluiten vervangen wordt). Kiezen
  schrijft meteen weg; Wissen vergeet het project van vandaag. Volgende dag leeg.
- **Zoekscherm project**: opent met de laatste 10 gekozen projecten (uit gebruiker.json,
  geen query), het huidige geselecteerd, Enter in een leeg vak kiest. Typen + Enter zoekt
  zoals voorheen; vak leeg = recent terug. De oude "laatste zoekterm" is vervallen.
- `tools\UiPreview`: `assy` toont nu nepbestellingen (`--oplopend` / `--aflopend`),
  `projectzoek` het zoekscherm, `--besteltest` loopt de kleurregels en het
  projectgeheugen na. PNG's wachten 1,5 s zodat de achtergrond klaar is.
- **Nog niet in Inventor of tegen echte bestellingen getest.** Let op: orders die
  "Gereed" zijn zonder geregistreerde levering blijven oranje; eenheden (meter vs. stuks)
  worden niet omgerekend; een deel dat ook elders in het project zit deelt de bestelling.

## Recente wijzigingen — 5-10-2026 (Maak DWG: opzet, schaal, instellingen)

- **Geen iLogic meer in Maak DWG.** `TekeningMaker` zet de buitenmaten zelf met
  `AutoDimensionService.PlaatsBuitenmaten(view, alleenHorizontaal)` op een expliciet
  aanzicht, in plaats van `AutoOverallDimension_1/_2` op "het laatste aanzicht".
  `ILogicRunner.vb` is daardoor ongebruikt; verwijderen mocht in de sessie niet, dus
  dat staat nog open.
- **Schaal en plaats** in `TekeningMaker.DeelIn`, met het rekenwerk los in
  `TekeningIndeling` (geen COM): vrije vlakken naast titelblok en stuklijst, een raster
  van 2 kolommen x 3 rijen (`AanzichtRol`), schaal uit de reeks, controle met de echte
  maten, en een groter blad uit de Sheetsize-presets als de schaal onder het minimum
  komt. Aanzichten worden met `DrawingView.Center` geplaatst (er bestaat ook
  `Position`, maar `Center` is expliciet het middelpunt).
- **Instellingen > Tekeningen** heeft er drie kaarten bij (`TekeningInstellingen` in
  `gebruiker.json` onder `tekening`). Het blad per soort is een bordernaam van een
  Sheetsize-preset. `InstellingenViewModel` heeft een optionele `kiesBestand` voor de
  sjabloonkiezer.
- **Hole notes: gelijke gaten.** `HoleNoteService.Sleutel` gebruikte
  `FormattedHoleThreadNote`; dat is de opmaakregel met tags en is voor M8 en M10 gelijk,
  waardoor maar een tapgat een note kreeg. Nu straal plus `Text.Text` (de zichtbare
  tekst). Geldt ook voor de knop Hole notes.
- **Indeling, tweede ronde.** Uitslag mag apart (eigen vrij vlak, eigen schaal, max
  4x kleiner dan het hoofdaanzicht; `Indeling.Apart`). Aanzichten schuiven in hun cel
  naar het hoofdaanzicht toe. Een losse part telt stuklijsten niet als obstakel.
  Vulgraad is nu een oppervlaktefractie (wortel per richting).
- **Indeling, derde ronde.** Iso in de linkerbovenhoek (`TekeningIndeling.Plaatsing`),
  zijaanzicht alleen horizontale maat, en `HoleNoteService.Plaats(..., rechts:=True)`
  zet notes rechts van de view op gathoogte, onder elkaar als ze botsen (alleen Maak
  DWG; de knop Hole notes doet het nog zoals voorheen).
- **Tapgaten op plaatwerk.** Vinkje `TapgatenNoteren` (standaard aan): hole notes
  via `HoleNoteService` met `AlleenTapgaten` op het hoofdaanzicht van plat plaatwerk
  (plaatwerk zonder zettingen, of bewerking plaatwerk) en op de uitslag. Of
  `Face.ThreadInfos` op een uitslag gevuld is, is niet bekend; komen er geen notes,
  dan staat er een regel in het logboek.
- `tools\UiPreview` kent `--tab N` om een venster op een bepaalde tab te openen
  (`instellingen --tab 1 --png uit.png` = tab Tekeningen).
- **Nog niet in Inventor getest.** Let vooral op: of `Center` op uitgelijnde
  projecties goed uitvalt, of de stuklijst boven `TitleBlock.RangeBox` goed staat
  (voorheen een vast getal van 5,2 cm), en of de draaiing van het hoofdaanzicht nog
  dezelfde projecties geeft.

## Recente wijzigingen — 5-10-2026 (later: thema, hole-instellingen, Derive-iconen)

- **Donkere modus** (Instellingen > Algemeen > Thema: Zoals Inventor / Licht /
  Donker, standaard **Zoals Inventor**; een kapotte of onleesbare instelling valt
  terug op licht).
  Opzet: `Stijlen.xaml` merget `ThemaPalet` en `ThemaTemplates` (klassen in
  `Views\Thema.vb`) die bij het laden van een venster `PaletLicht/PaletDonker.xaml`
  en `TemplatesLicht/TemplatesDonker.xaml` kiezen. Alle kleuren staan in het palet
  (StaticResource-sleutels); `Thema.Pas(Me)` staat na `InitializeComponent()` in elk
  venster en zet de donkere titelbalk (DwmSetWindowAttribute). Een venster dat al
  open staat houdt zijn thema; het volgende volgt de nieuwe keuze.
  - Licht = ongewijzigd: `TemplatesLicht.xaml` bevat lege basisstijlen
    (`ThemaButton`, `ThemaTextBox`, ...) waar `Stijlen.xaml` op voortbouwt, zodat de
    benoemde stijlen (Hoofdknop, Commandoknop, ...) in donker de templates erven.
  - Donker: eigen templates voor Button, TextBox, ComboBox(+Item), CheckBox,
    RadioButton, TabItem, ScrollBar, DataGrid-onderdelen, ToolTip, ProgressBar, en de
    systeemkleuren (SystemColors) zijn overschreven. Kleuren volgen het blauwgrijs
    van Inventors eigen donkere thema.
  - Nieuwe vensters: gebruik `{DynamicResource Achtergrond}` / `Tekst` op de
    window-root (StaticResource kan daar niet: Window.Resources komt later),
    `{StaticResource ...}` voor de rest, en geen vaste hex-kleuren meer.
  - "Zoals Inventor" leest `Application.ThemeManager.ActiveTheme.Name` en neemt
    donker aan als de naam "dark" bevat. **Niet geverifieerd in Inventor zelf.**
  - Getest met een testappje dat de vensters met nepdata opent en screenshots maakt
    (niet in de repo); lichte weergave vergeleken, donkere bekeken.
- **Hole notes-instellingen** (tab Hole notes): Alle gaten / Alleen tapgaten, en
  centermarks aan/uit (`HoleNoteInstellingen`). Een tapgat = een aangrenzend vlak met
  `ThreadInfos`. Bij "alleen tapgaten" blijft een gat zonder draad helemaal ongemoeid.
- **Derive**: de drie styles zijn nu icoonknoppen (`StijlKnop` in `Stijlen.xaml`);
  de beschrijvingen onder Koppeling zijn weg.

Drie iLogic-regels uit `MDE Standards\iLogic Rules` zijn native knoppen geworden
(Shaded, ThreadedHole, Derive), en de iconen van AutoDimension, Schermafdruk en
Sheetsize zijn omgezet naar de tekst-tegelstijl van de rest (`tools\IconGen`; de
losse tekenfuncties voor die drie zijn vervallen).

- **Shaded** (`Commands\ShadedKnopService.vb`): zet de aangeklikte view op Shaded;
  Shift = alle isometrische views op het blad. Eén `Transaction`, dus één Ctrl+Z.
  Geen instellingen. Iso-detectie staat nu in `Cad\DrawingViewHulp.vb`, en
  AutoDimension gebruikt dezelfde helper voor de 2D-views.
- **Hole notes** (`Cad\HoleNoteService.vb`, `Commands\HoleNoteKnopService.vb`):
  vervangt de testregel ThreadedHole. `Sheet.HoleNotes` bestaat niet in de
  Automation-API; de echte aanroep is `Sheet.DrawingNotes.HoleThreadNotes.Add`, die
  zelf faalt op een curve die geen hole/draadrand is (dat is de hole-test). Per view:
  een centermark op elke hole en een note per groep gelijke holes (gelijk = zelfde
  `FormattedHoleThreadNote`); de dubbele note wordt direct verwijderd en op de
  overblijvende staat `QuantityDefinition = kNumberOfLikeHolesNormalToView`, zodat
  Inventor "4X" zelf toont. Concentrische cirkels (draad, verzinking) tellen als een
  hole. Alleen volledige, zichtbare cirkels. Tijdslimiet 5 s tussen twee curves.
  **Niet getest in een draaiende Inventor** — eerst op een echte tekening proberen
  (patroon van threaded holes, verzinking, assembly-tekening). Een tweede keer
  draaien op dezelfde view geeft dubbele notes/centermarks (Ctrl+Z).
- **Derive** (`Cad\DeriveService.vb`, `DeriveBronnenLezer.vb`, `DeriveModels.vb`,
  `Views\DeriveWindow.xaml`, `ViewModels\DeriveViewModel.vb`,
  `Commands\DeriveKnopService.vb`): WPF-venster met tabs Bron (boom van open
  parts/assemblies, zoals de iLogic-regel) en Opties (derive style, model state /
  design view / position view uit het bronbestand, Break link, Link face color).
  Part en body via `DerivedPartUniformScaleDef`, assy via ShrinkWrap. Bij een part
  verbreekt `BreakLinkToFile` de link na het toevoegen, bij ShrinkWrap is het de
  eigenschap `BreakLink`. Link face color wordt genegeerd (en is grijs) als Break
  link aan staat. Position View bestaat alleen voor een assy als bron. Style, Break
  link en Link face color worden onthouden in `gebruiker.json`
  (`DeriveInstellingen`); de representation-keuzes niet, die hangen van de bron af.
- **Gedeeld**: `Commands\KnopHulp.vb` (Shift-detectie, statusbalkmelding).
- Nog niet uitgerold.

## Recente wijzigingen — 23-9-2026

**Sheetsize** — nieuwe ribbon-knop die een aangeleverde iLogic-regel vervangt (size,
oriëntatie en border van de actieve tekening wijzigen op basis van een gekozen
formaat). De iLogic-regel gebruikte `ActiveSheet.ChangeSize(...)` en
`ActiveSheet.Border = "A4"`; dat zijn iLogic-only wrappers die niet in de gewone
Automation-API bestaan (`Inventor.Sheet` heeft geen `ChangeSize` en `Border` is
alleen leesbaar). Native vertaling: `Sheet.Size`/`Width`/`Height`/`Orientation`
zetten, en de border toepassen via `Sheet.AddBorder(BorderDefinition)` na het
opzoeken van de definitie in `DrawingDocument.BorderDefinitions` op naam.

- **Formaten zijn instelbaar**, niet hardcoded. Nieuwe tab **Sheetsize** in het
  instellingenvenster: een editable lijst van presets (Formaat A4/A3/A2/A1/A0/
  Aangepast, Breedte/Hoogte in mm bij Aangepast, Oriëntatie, Border). De naam die in
  de keuzelijst van de knop verschijnt is de ingevulde Border zelf — bewust geen
  apart naamveld, want de gebruiker vult toch al de bordernaam in die op de
  tekening moet komen. Vijf standaardpresets bij de eerste keer, gelijk aan de
  oorspronkelijke iLogic-regel (A4 staand, A3/A2/A1 liggend, A1×A0 liggend
  594×1189mm).
- **`SheetSizeInstellingen`** (in `GebruikersInstellingen.vb`) hangt als
  `SheetSize` aan `GebruikersInstellingen`, zelfde opslagbestand als de andere
  schermen (`%APPDATA%\MDE\InventorTools\gebruiker.json`).
- **`SheetSizeService.Pas`** (in `InventorTools.Cad`) doet de COM-aanroepen. De
  border-check gaat vóór de size-wijziging: bestaat de ingevulde bordernaam niet in
  de actieve tekening, dan verandert er niets en komt er een melding — dat was een
  bewuste keuze bij het maken van deze knop, i.p.v. size/oriëntatie wel toepassen
  en alleen de border overslaan.
- **`Sheet.Width`/`Height`** staan, zoals de rest van de Inventor-API, in
  centimeters; de mm-invoer in de instellingen wordt bij het toepassen gedeeld
  door 10.
- Werkt alleen op een Drawing-document; anders dezelfde soort melding als bij
  Schermafdruk. Lege presetlijst (bijv. leeggehaald door de gebruiker) geeft een
  vraag om Instellingen te openen, in plaats van alleen een melding.
- Staat zowel op het MDE-tabblad als in het MDE-gastpaneel op elk gekoppeld
  Inventor-tabblad, inclusief Drawing/Place Views (bestaande Gastplekken-lijst in
  `RibbonBuilder.vb`) — geen aparte `NatiefPaneelPlek` nodig, dat gebeurt al
  automatisch voor elke knop met `inNatiefTabblad:=True`.
- **Nog niet uitgerold**: dit is alleen gebouwd (`dotnet build` per project) en
  niet via `deploy.ps1`/`publish.ps1` weggezet.

## Recente wijzigingen — 21-9-2026

**AutoDimension** — nieuwe ribbon-knop bij de standaard Dimension-knoppen op de
Annotate-tab in het Drawing-lint. Zet horizontale en verticale buitenmaten op
de aangeklikte view.

- Overgezet uit een iLogic-regel van Jelte de Jong; met tie-break-fix zodat
  extension lines niet meer schuin door productlijnen heen kunnen lopen. Zie
  `AutoDimensionService.PlaatsBuitenmaten` — primary sort op de as die telt,
  secondary sort in de richting waar de dimlijn komt te staan.
- **Shift ingedrukt bij het klikken** = alle 2D-views op de actieve sheet in
  één keer. Iso-views worden overgeslagen (detectie via camera-richting: als
  Eye−Target op meer dan één as verschilt, is het geen 2D-view).
- **Tijdlimiet 2 s.** Bij shift/multi-view is dat hard: na 2 s stopt de loop en
  komt in de statusbalk hoeveel views wel gedaan zijn. Bij single-view is het
  een achteraf-melding — Inventor-COM-aanroepen kunnen niet halverwege veilig
  worden onderbroken.
- **Undo**: hele actie zit in één `Transaction`, dus één Ctrl+Z ruimt alles op.
- **Icoon** in Inventor-huisstijl (zwart-op-wit) met rode "A"-badge rechtsboven,
  zodat de knop niet uit de toon valt tussen de standaard Dimension-knoppen.
  Zie `TekenAutoDimension` in `tools\IconGen\Program.vb`.
- **Ribbon-integratie**: `RibbonKnop` heeft nu een optionele `NatiefPaneelPlek`
  die aangeeft in welk paneel de knop hoort (lint + tab-InternalName + paneel-
  displayname). Zo'n knop komt niet ook op het MDE-tabblad of MDE-gastpaneel.
  Het paneel wordt op DisplayName herkend want de InternalName wisselt tussen
  Inventor-versies.

## Recente wijzigingen — 14 en 15-9-2026

Scherm **3D printen** uitgebreid volgens een besproken spec, en de
instellingen daarna verhuisd naar het algemene instellingenvenster.

- **Instellingenvenster (`InstellingenWindow`) heeft drie tabs**: Algemeen
  (werknemer), Tekeningen (tekeningenmap), 3D printen (printmap plus de
  export-keuzes). Eén opslag-actie schrijft alle tabs weg. Het losse
  print-instellingenvenster is verwijderd; er is geen tandwielknop meer op
  het 3D-printscherm.
- Op de 3D-printen-tab staan drie kaarten voor de export-keuzes: Multi body
  / part, Multi part assy, Bestanden. Standaardwaarden: multi-body apart
  **aan**, elke keer vragen **aan**, overschrijven **uit**, alle assy-parts
  los **uit**, assy elke keer vragen **aan**.
- Nieuwe klasse `PrintSchermInstellingen` (in
  `Configuration\GebruikersInstellingen.vb`) hangt als `PrintScherm` aan
  `GebruikersInstellingen`. Persistentie via
  `%APPDATA%\MDE\InventorTools\gebruiker.json`. `PrintKnopService.Exporteer`
  leest bij elke exportactie opnieuw, dus wijzigingen slaan meteen aan.
- Het 3D-printscherm zelf toont bovenin alleen nog wat er geëxporteerd gaat
  worden. De printmap staat niet meer als koptekst; "Map openen" onderin is
  een klik weg.
- **Multi-body per part:** `FormaatExporteur.ExporteerBody(part, body, doel)`
  schakelt tijdelijk de zichtbaarheid van andere bodies, roept
  `ActiveView.Update()` aan, exporteert de STL, en herstelt de zichtbaarheden
  in een `Finally`-blok. Alleen bodies met `IsSolid AndAlso Visible` tellen
  mee — surfaces en al verborgen bodies blijven buiten schot.
- **Vraagdialoog multi-body** (`MultiBodyKeuzeWindow`) verschijnt bij het
  eerste multi-body deel van een exportactie; de keuze (apart / één STL /
  annuleren) geldt voor de rest van die run. Bewust: bij een assembly met
  tien multi-body parts hoeft de gebruiker niet tien keer te klikken.
- **Multi part assy** (`AssyKeuzeWindow`) heeft twee scenario's: zonder
  selectie (Eén STL van assy vs. Alle parts los) en met selectie (Alleen
  selectie vs. Alle parts). Het tweede scenario is er om te voorkomen dat een
  per ongeluk aangeklikt part stilzwijgend als hele set wordt uitgeleverd.
- **Naamgeving:** `partnaam[_bodynaam][_NN].stl`. Bodynaam alleen bij
  multi-body-apart; verboden Windows-tekens in de bodynaam worden vervangen
  door `-`, een lege bodynaam valt terug op `Solid`+index. `_01`, `_02`, …
  worden alleen toegevoegd wanneer overschrijven uit staat; de eerste krijgt
  de kale naam.
- **Alle parts van assy** komt uit `AssemblyCollector` met
  `InclusiefSamenstellingen=False, InclusiefOnzichtbaar=True`. Sub-assemblies
  worden platgeslagen (volgens de regels die Collect Parts ook al hanteert:
  phantoms uitklappen, gekochte assemblies als geheel, weldments via hun
  parts). De hoofd-assy zelf wordt uit de lijst gefilterd. Inkoopdelen en
  content-center-parts gaan mee — bewuste keuze.
- **Dubbelklik of Enter** op een regel in de bestandenlijst opent de STL via
  `Process.Start` met `UseShellExecute = True`. Een ontbrekend bestand
  verdwijnt uit de lijst met een melding in de statusregel; klikken op de
  header of lege ruimte doet niets (visual-tree check op `DataGridRow`).

## Openstaande architectuurkeuzes

- **Vault-buildbreuk.** Root-cause fix ontbreekt. Twee kandidaten: expliciete
  `<PackageReference Include="System.Runtime" Version="10.0.0" />` in
  `InventorTools.Vault.vbproj`, of alle targets naar `net10.0-windows`
  tillen. Nu leven we met `--no-dependencies` en `-SkipBuild`.
- **Assembly-naam in `AssyKeuzeWindow` bij het selectie-scenario** valt terug
  op de tekst "deze assembly", omdat de `delen`-lijst dan geen assy-deel
  bevat. Nette oplossing: `_application.ActiveDocument` doorgeven en de
  partnaam via `DeelLezer` ophalen. Kosmetisch; werkt zoals het is.
- **Multi-body-vraag: één keer per exportactie.** Alternatief zou zijn per
  part vragen. Huidige keuze past bij assembly-workflows waar je niet elke
  keer wilt beslissen; heroverwegen als iemand per deel een andere strategie
  wil.
- **Assy-vraag komt vóór de multi-body-vraag.** Kiest de gebruiker "alle
  parts los" en zit er een multi-body deel tussen, dan volgt daarna alsnog
  de multi-body-vraag. Twee dialogen op rij — bewust, het zijn twee losse
  keuzes. Samenvoegen tot één scherm als het als storend voelt.
- **`ActiveView.Update()` na visibility-toggle** in
  `FormaatExporteur.ExporteerBody` staat in een `Try`/`Catch` die exceptions
  stil inslikt (niet elk scenario heeft een actief view). Als STL-exports
  ooit onverwacht álle bodies bevatten in plaats van de gekozen: begin hier.
- **Sub-assemblies platslaan** volgt de regels van `AssemblyCollector`. Als
  3D printen daar ooit van moet afwijken (bijvoorbeeld: gekochte
  sub-assemblies tóch openbreken), splits dan de collector-configuratie in
  plaats van speciale gevallen in `PrintKnopService`.
- **De spec bij deze uitbreiding** is een losstaand markdown-document dat
  niet in de repo staat. Als het herbruikbaar wordt, verhuist het naar
  `docs\` — nu leunt CLAUDE.md op deze samenvatting.

## Waar dingen staan

| Onderwerp | Bestand |
| --- | --- |
| Hoofdscherm 3D printen | `src\InventorTools.AddIn\Views\PrintWindow.xaml{,.vb}` |
| Algemeen instellingenvenster (met tabs) | `Views\InstellingenWindow.xaml{,.vb}`, `ViewModels\InstellingenViewModel.vb` |
| Multi-body-dialoog | `Views\MultiBodyKeuzeWindow.xaml{,.vb}` |
| Assy-dialoog | `Views\AssyKeuzeWindow.xaml{,.vb}` |
| Viewmodels | `ViewModels\PrintViewModel.vb`, `MultiBodyKeuzeViewModel.vb`, `AssyKeuzeViewModel.vb` |
| Orkestratie (vragen, naamgeving, ontdubbelen) | `Commands\PrintKnopService.vb` |
| Body-export | `src\InventorTools.Cad\FormaatExporteur.vb` (`ExporteerBody`) |
| Persistente instellingen | `src\InventorTools.Core\Configuration\GebruikersInstellingen.vb` (`PrintSchermInstellingen`) |
| AutoDimension-service | `src\InventorTools.Cad\AutoDimensionService.vb` |
| Maak DWG (tekening, schaal, plaats) | `src\InventorTools.Cad\TekeningMaker.vb`, rekenwerk in `TekeningIndeling.vb` |
| Maak DWG-instellingen | `GebruikersInstellingen.vb` (`TekeningInstellingen`), tab Tekeningen in `InstellingenWindow.xaml` |
| Schaalnotatie (1:5, 2:1) | `src\InventorTools.Core\Tekeningschaal.vb` |
| AutoDimension-knop (shift, timer, statusbalk) | `Commands\AutoDimensionKnopService.vb` |
| Ribbon-integratie (`NatiefPaneelPlek`) | `Ribbon\RibbonKnop.vb`, `Ribbon\RibbonBuilder.vb` |
| Welke knop waar in het lint staat | `Ribbon\LintIndeling.vb` |
| Sheetsize-knop (orkestratie) | `Commands\SheetSizeKnopService.vb` |
| Sheetsize-service (Size/Orientation/Border via COM) | `src\InventorTools.Cad\SheetSizeService.vb` |
| Sheetsize-keuzedialoog | `Views\SheetSizeWindow.xaml{,.vb}`, `ViewModels\SheetSizeKeuzeViewModel.vb` |
| Sheetsize-presets in Instellingen | `Views\InstellingenWindow.xaml` (tab Sheetsize), `ViewModels\SheetSizePresetRij.vb` |
| Sheetsize-instellingen (persistent) | `GebruikersInstellingen.vb` (`SheetSizeInstellingen`, `SheetSizePreset`) |
| AutoDimension-icoon | `tools\IconGen\Program.vb` (`TekenAutoDimension`) |
| Collect Parts: kleurregels Besteld | `src\InventorTools.Core\Bestelstatus.vb` |
| Collect Parts: bestel-query (met geleverd) | `src\InventorTools.Core\Data\BestellingRepository.vb` (`GetProjectBestellingen`) |
| Collect Parts: ophalen, sorteren, vinkje | `ViewModels\AssyViewModel.vb`, `Views\AssyWindow.xaml{,.vb}` (`OpSorteren`) |
| Project per dag en recente projecten | `GebruikersInstellingen.vb` (`ProjectKeuzeInstellingen`), `Commands\AssyService.vb` (`OnthoudProject`), `Views\ZoekProjectWindow.xaml{,.vb}` |
| Plak uit Vault: knop, melding, zonder Vault | `Commands\VaultPlakKnopService.vb` |
| Plak uit Vault: klembord (Win32) en ontleden | `Commands\Klembord.vb`, `src\InventorTools.Core\Vault\VaultPlakken.vb` (`KlembordLezer`) |
| Plak uit Vault: opzoeken en ophalen | `src\InventorTools.Vault\VaultPlakker.vb` |
| Plak uit Vault: selectie in de Vault-client lezen (MSAA) | `src\InventorTools.Vault\VaultClientLezer.vb` |
| Plak uit Vault: plaatsen aan de cursor, een voor een | `Commands\PlaatsWachtrij.vb`, `src\InventorTools.Cad\ComponentPlaatser.vb` |
| Sneltoets van een knop | `Ribbon\RibbonKnop.vb` (`Sneltoets`), `Ribbon\RibbonBuilder.vb` (`ZetSneltoets`) |
| Quick Drawing: regels (zonder COM) | `src\InventorTools.Core\QuickDrawing\QuickDrawingPlan.vb` |
| Quick Drawing: Uitvoeren (vragen vooraf, onbeheerd, rapport) | `Commands\QuickDrawingKnopService.vb` (`UitvoerenAsync`), regels in `QuickDrawingPlan.Uitvoer` |
| Controle van een tekening (bijgewerkt, binnen blad, overlap, losse maten) | `src\InventorTools.Cad\TekeningControle.vb` |
| Quick Drawing: bekijken en knoppen per regel | `Commands\QuickDrawingKnopService.vb` |
| Quick Drawing: doet mee, soort, iPart, gekocht | `src\InventorTools.Cad\QuickDrawingLezer.vb` |
| Quick Drawing: scherm | `Views\QuickDrawingWindow.xaml{,.vb}`, `ViewModels\QuickDrawingViewModel.vb`, `QuickDrawingRij.vb` |
| Tekening hoort bij partnummer (strikt) | `src\InventorTools.Core\Tekeningnaam.vb` |
| Kopieer uit filestore vanuit het lint | `Commands\KopieerKnopService.vb` |
| Bewerkingen voorstellen (regels + lijkt op) | `src\InventorTools.Core\Bewerkingen\BewerkingVoorspeller.vb`, gebruikt in `Cad\BewerkingDetector.vb` |
| "Lijkt op" en dubbelingen: plaatje (wolkje) en openen | `src\InventorTools.AddIn\ViewModels\ArchiefPartRij.vb`, `Commands\PartNumberService.vb` (`Vul`, `OpenArchiefPart`) |
| Dubbelcheck (vingerafdruk, spiegelbeeld) | `src\InventorTools.Core\Bewerkingen\DubbelZoeker.vb`, `Vormmaat.vb` |
| Kenmerken uit een part (add-in en scan) | `src\InventorTools.Cad\ModelKenmerkenLezer.vb` |
| Bewerkingen-archief (CSV) en laden | `Core\Bewerkingen\BewerkingArchief.vb`, `Commands\BewerkingArchiefLader.vb` |
| Archief opbouwen en meten (Apprentice, net48) | `tools\BewerkingenArchief\Program.vb`, `Apprentice.vb` |
| Reden en dubbelingen in het partnummervenster | `Commands\PartNumberService.vb` (`VulVoorstellen`), `Views\PartNumberWindow.xaml`, `ViewModels\DubbelRij.vb` |
| Buis/koker herkennen | `src\InventorTools.Cad\ProfielHerkenning.vb` (toetsen: `BewerkingenArchief --profielen`) |
| Maak DWG voor buis/koker (opzet en maten) | `Cad\TekeningMaker.vb` (`VulProfiel`, `KiesProfielRichting`, `BemaatProfiel`) |
| Gravure: herkennen en bijwerken | `src\InventorTools.Cad\PartnummerGravure.vb` |
| Gravure: plek zoeken (automatisch en Stempel) en past-controle | `src\InventorTools.Cad\GravurePlaatser.vb`, `Core\Gravure\Gravurezoeker.vb` |
| Gravure: opmaak, uitsnede StempelPartnumber, raakvlak cilinder | `src\InventorTools.Cad\PartnummerStempel.vb` |
| 3D printen: aftellen en sluiten | `ViewModels\PrintViewModel.vb` (`StartAftellen`, `StopAftellen`), `Views\PrintWindow.xaml.vb` |
| 3D printen: openen in Bambu Studio (bericht, starten / venster, WM_COPYDATA) | `src\InventorTools.Core\Slicer.vb`, `Commands\SlicerOpener.vb` |
| 3D printen: vinkje na export, rechtermuisknop, meerdere kiezen | `ViewModels\PrintViewModel.vb`, `Views\PrintWindow.xaml{,.vb}`, `Commands\PrintKnopService.vb` |
| Gravure: vraag na partnummer, opmerking bij export | `Commands\GravureControle.vb`, `Views\GravureVraagWindow.xaml`, `Commands\ExportService.vb` (`GravureNotitie`) |
| Idee of verbeterverzoek (knop, venster, tabel van de webapp) | `Commands\IdeeKnopService.vb`, `ViewModels\IdeeViewModel.vb`, `Views\IdeeWindow.xaml{,.vb}`, `Core\Data\VerbeterverzoekRepository.vb` |
| Schermafdruk van het Inventor-venster (BitBlt) | `Commands\VensterVanger.vb` |
| Uitgave-naam op de werkplek | `AddIn\Uitgave.vb`, `publish.ps1` (uitgave.txt) |
| Foutvertaling en de algemene knopmelding | `src\InventorTools.Cad\InventorFout.vb`, `Commands\Foutmelding.vb` |
| Melding materiaalbibliotheek (een keer per sessie) | `Commands\MateriaalMelding.vb`, `Cad\MateriaalService.vb` (`Probleem`) |
| Filestore bereikbaar vóór een export | `Commands\FilestoreControle.vb` |
| 3D printen: Esc tijdens de export, opruimen | `Commands\EscToets.vb`, `Commands\PrintKnopService.vb` (`Exportrun`), `ViewModels\PrintViewModel.vb` (`ExportUitkomst`, `ToonVoortgang`) |
