# Comfort Shift HUD (Playrooms)

**Nieuwe, zelfstandige BepInEx 6 Unity IL2CPP-mod in deze fork.** Compatibel *ontworpen* met de bestaande PlayroomsCoop 1.1.7, New Game+ 1.2.4 en de originele Unity 6000.6-patcher. Deze mod is afzonderlijk van het project Playrooms-Comfort-Ball-Plus en bevat geen upstream co-opcode.

**Status: broncode gereed voor de eerste Windows-build en praktijktest.** Dit is nog geen bewezen werkende release; de originele Playrooms-mods hoeven niet te worden aangepast.

## Functies

1. **Ballen in de huidige kamer:** leest per kamer RoomObject.RoomId, _ballSpots en BallSpot.IsCollected. Toont resterend / totaal, en toont eerlijk "Telling onbekend" als een balstatus niet uitgelezen kan worden. Er worden maximaal 750 balspots per update gecontroleerd om het spel niet te blokkeren.
2. **Resterende shifttijd:** leest de echte DayTimeService.RemainingTime en _phase, elke halve seconde. Niet een aparte stopwatch of gok. Dit is dezelfde dagservice waarmee de originele co-opmod de klok synchroniseert. De mod zet de klok **nooit** stil of terug.
3. **Opgeruimde kamers plus namen:** een kamer is hier 'opgeruimd' wanneer alle daadwerkelijk geregistreerde balspots IsCollected=true zijn. Kamers met nul balspots worden **niet** automatisch als opgeruimd geteld. De lijst toont RoomId, omdat geen betrouwbaar lokaal veld voor een mooiere naam is bevestigd.
4. **END DAY en licht:** zodra de dagservice naar een fase na de dagshift gaat, verschijnt bovenaan een duidelijke **END DAY**-banner. De mod maakt standaard een **tijdelijke, lokale puntlichtbron** bij de speler aan. Het verandert geen dag-/nachtklok, fase, globale gebouwverlichting, save of co-opdata. De lokale lamp wordt verwijderd wanneer de volgende dag begint, de speler naar het menu gaat of de mod wordt beëindigd.

**Belangrijke nuance:** het extra licht is voorlopig **lokale werkverlichting**, geen bewezen manier om alle originele lampen in het hele gebouw ingeschakeld te houden. Unity HDRP kan de helderheid anders behandelen. Hiervoor is een echte Playrooms-test nodig. De END DAY-tekst is onafhankelijk van het licht en blijft ook bij een lichtfout zichtbaar.

## HUD-voorbeeld (voorbeeldgetallen, geen echte spelmeting)

    COMFORT SHIFT HUD
    Kamer: Office
    Ballen: 124 / 800 over
    Shift: 04:21
    Opgeruimd: 3 / 12 kamers met ballen
    ✓ Kitchen
    ✓ Hallway
    ✓ Storage

Wanneer de shift is verlopen, staat bovenaan in het midden:

    END DAY

Het scherm is standaard linksboven zichtbaar tijdens een gamewereld, **zonder F8/F9/F10-toetsen**. Dat voorkomt overlap met de vorige Comfort Ball Tracker die normaal rechtsboven staat. Daardoor is er geen conflict met de co-opmod en onze eerdere tracker die bij sommige gebruikers geen zichtbare reactie op F9/F10 gaf. Instellingen staan in BepInEx/config/nl.comfortcode.playrooms.shifthud.cfg.

## Installeren via PowerShell (Windows)

Vereisten: originele Playrooms-installatie, werkende TheWildLeon Unity 6000.6-preloader, BepInEx 6 en lokaal gegenereerde IL2CPP-interop-DLL's (in de eerdere test 161 stuks). Microsoft .NET SDK 8 of nieuwer, niet alleen de runtime. Geen download van onbekende executables. Gebruik de nieuwste repository-ZIP, niet een oude gedownloade map.

1. **Sluit Playrooms.**
2. Pak deze hele repository uit en open PowerShell in de repositorymap.
3. Proefbuild, nog geen wijzigingen in Playrooms:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-shift-hud.ps1 -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Playrooms"

4. Na geslaagde proefbuild **alleen** de nieuwe mod installeren:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-shift-hud.ps1 -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Playrooms" -Apply

Doelpad: Playrooms/BepInEx/plugins/PlayroomsShiftHUD/PlayroomsShiftHUD.dll. Een oudere eigen Shift HUD wordt eerst geback-upt onder BepInEx/comfort-backups/shift-hud-*. PlayroomsCoop, New Game+, patcher, 161 interop-DLL's, ComfortBallTracker en saves worden niet gewijzigd.

## Testen

Start Playrooms via Steam en open een **echte kamer**, niet alleen het hoofdmenu. Controleer daarna:

- Het BepInEx-log (Playrooms/BepInEx/LogOutput.log) bevat "Comfort Shift HUD 0.1.0 loaded" plus "Unity Update callback active" en "Unity OnGUI callback active".
- De HUD verschijnt, ook als er nog geen kamers zijn gevonden, en toont tijd of "Niet beschikbaar" zolang de dagservice nog niet beschikbaar is.
- Ballen over daalt na het verzamelen. Verifieer dat de aantallen niet onjuist "0" tonen bij een mislukte scan.
- Opgeruimde kamers worden pas vermeld na een volledige, foutloze telling.
- Bij shifteinde verschijnt END DAY. Controleer of het lokale werklicht er zichtbaar uitziet op jouw HDRP-configuratie. De spelklok en voortgang mogen niet veranderen.
- In co-op kunnen host- en gastwaarden tijdelijk uiteenlopen totdat de originele mod de hostsnapshot heeft gesynchroniseerd. Controleer de gelijkheid na een volledige sync.

Bij fouten: neem de relevante BepInEx-logregels op. Er wordt geen code naar andere spelers gestuurd.

## Terugdraaien

Sluit Playrooms en voer uit:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rollback-shift-hud.ps1 -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Playrooms"

Dat herstelt uitsluitend de vorige Shift HUD-DLL of verwijdert hem als hij voorheen niet geïnstalleerd was. De vorige DLL wordt bewaard om opnieuw te kunnen installeren.

## Configuratie

| Instelling | Default | Gedrag |
| --- | --- | --- |
| HUD.Visible | true | HUD aan/uit zonder toetsen |
| HUD.TopRight | false | Standaard linksboven; rechtsboven instelbaar |
| HUD.MaximumCleanRoomNames | 8 | 1 tot 16 namen |
| Tracking.RefreshSeconds | 3 | Volledige scan elke 1-15 sec; gebeurt in chunks |
| AfterShift.EnableLocalWorkLight | true | Lokale lichtbron na shifteinde |
| AfterShift.WorkLightRange | 45 | Radius in Unity-eenheden, 5-75 |
| AfterShift.WorkLightIntensity | 3 | Lichtintensiteit, 0.5-12 |

## Herkomst en grenzen

De bron van PlayroomsCoopMod maakt duidelijk gebruik van RoomObject/_ballSpots/IsCollected en DayTimeService._phase/RemainingTime/CurrentDay. De nieuwe Shift HUD bevat **eigen** broncode en een optionele BepInEx-softdependency op com.playroomscoop.mod, zonder assemblyreferentie naar of aanpassing van de originele mod.

Tellingen, kamerdetectie (bij ontbreken van een expliciete RoomService-kamer een nabijheidsschatting), shiftweergave en lokale verlichting vereisen een echte clienttest. Niet presenteren als volledig getest vóór zulke resultaten.
