# Comfort Shift HUD 0.1.0 (experimenteel)

Nieuwe, zelfstandig ontwikkelde BepInEx 6 IL2CPP-plugin voor Playrooms Unity 6000.6.3f1, naast de oorspronkelijke Playrooms Co-op 1.1.7 / New Game+ 1.2.4.

**Nog niet getest in de echte Playrooms-gameclient.** De code is voorbereid voor bouwen op de pc met reeds geïnstalleerde BepInEx en interop. De mod is geen binaire release.

## Functies

- **Huidige kamer:** RoomService.PlayerRoom is leidend, **geen** nabijheidsschatting. Geen kamerherkenning geeft eerlijk "Nog geen kamer herkend".
- **Ballen in huidige kamer:** het aantal nog niet verzamelde ballen en het totaal, berekend als RoomObject.TotalBallCount minus RoomObject.CollectedBallCount. De originele co-opmod synchroniseert deze speltoestand; onze plugin verzendt geen netwerkdata.
- **Shiftklok:** DayTimeService.RemainingTime, alleen wanneer de dag daadwerkelijk gestart is. Tijdens de nacht staat 00:00.
- **Voltooide kamers:** Rooms met minstens één bal en nul resterende ballen; toon het aantal en een lijst van herkende RoomId's. De lijst betreft **momenteel geladen/herkende** kamers, niet gegarandeerd alle kamers in een nog niet gestreamde wereld.
- **Shift voorbij:** grote, altijd zichtbare **END DAY**-banner tijdens de nachtfasen 1 en 2.
- **Houd het licht aan:** optionele Harmony-prefix die uitsluitend de *Night*-overgang in TwoPixels.LightSystem.LightSystemService.OnDayTimeChanged blokkeert. De plugin verandert de shifttimer, dagtelling, enemy state, savegame en co-opnetwerkdata niet. Bij onbekende methode/signatuur, ontbrekende Harmony of exceptions werkt het standaard lichtgedrag door. **Het visuele resultaat van de patch is nog niet in de game bevestigd**, omdat Playrooms bovendien baked lightmaps en een scenariowisselaar gebruikt.

De input van PlayroomsCoopMod (F8) en de oudere Comfort Ball Tracker (F9/F10) wordt niet onderschept. Deze HUD is standaard altijd zichtbaar en is instelbaar via BepInEx-configuratie, zonder toetsenconflict. De oudere Comfort Ball Tracker blijft als aparte mod bestaan; als je beide installeert, krijg je waarschijnlijk twee HUD's.

## Gecontroleerde interopbron

Op basis van de game-interop uit de in deze conversatie aangeleverde Playrooms-mods-ZIP van 13 november 2026 zijn de volgende namen werkelijk aanwezig:
- TwoPixels.Room.RoomService.PlayerRoom
- TwoPixels.Room.RoomObject.RoomId / TotalBallCount / CollectedBallCount
- TwoPixels.DayTime.DayTimeService.HasStarted / RemainingTime / _phase / CurrentDay
- TwoPixels.DayTime.DayTimeType (Day, Night)
- TwoPixels.LightSystem.LightSystemService.OnDayTimeChanged(DayTimeType)

De originele co-opbron in dezelfde repository gebruikt eveneens RoomObject/DayTimeService en interpreteert fase 0 als dag, fase 1/2 als licht-uit/nacht en fase 3 als nieuwe dag. Deze interopnamen zijn **geen bewijs** dat Harmony de native method daadwerkelijk veilig kan patchen. De patchinstelling is apart uit te schakelen.

## Bouwen en installeren op Windows

Sluit Playrooms en download de actuele repository-ZIP. Gebruik de bestaande modbundle (PlayroomsPatcher, BepInEx en 161 al gegenereerde interop-DLL's), niet opnieuw een BepInEx- of Cpp2IL-installer.

**Eerst alleen bouwen (dry run):**

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-comfort-shift-hud.ps1 -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Playrooms"

Dit bouwt tegen je lokaal bestaande game-assemblies maar **wijzigt de Playrooms-map niet**.

**Daarna installeren:**

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-comfort-shift-hud.ps1 -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Playrooms" -Apply

Of dubbelklik op INSTALLEREN_SHIFT_HUD.bat in de repository-root voor dezelfde installatie. Er wordt slechts één eigen DLL gekopieerd naar:

    Playrooms\BepInEx\plugins\ComfortShiftHUD\ComfortShiftHUD.dll

Een vorige HUD-DLL wordt eerst bewaard in BepInEx/comfort-backups/shift-hud-*. Alleen de eigen HUD-DLL kan bij dit proces worden vervangen.

**Teststatus na starten:** Zoek in BepInEx/LogOutput.log naar:
- Comfort Shift HUD 0.1.0 loaded
- [ShiftHUD] Unity Update callback active
- [ShiftHUD] Unity OnGUI callback active
- [ShiftHUD] Lighting interception installed (als KeepLightsAfterShift ingeschakeld is)
- [ShiftHUD] Night lighting transition suppressed (als een einde-shift-nachttransitie werkelijk plaatsvond)

Als alleen de eerste regel verschijnt, is de HUD nog niet bewezen actief. Het scherm moet balentelling, tijd, kamerstatus en END DAY bij shifteinde tonen. Test solo, daarna co-op als host en als gast. De telling kan bij een co-op gast vertraagd zijn tot de host-snapshot is ontvangen.

## Configuratie

Na de eerste start:

    BepInEx\config\nl.comfortcode.playrooms.shifthud.cfg

| Optie | Standaard | Betekenis |
|---|---|---|
| Display.ShowHud | true | HUD altijd zichtbaar |
| Display.ShowCompletedRooms | true | Kamernamen/ID's tonen |
| Display.MaxRoomNames | 10 | Maximaal 1-25 namen |
| Display.RefreshInterval | 0.75 | Hertelling elke 0.25-5 seconden |
| Gameplay.KeepLightsAfterShift | true | Nachtlichttransitie onderdrukken |

Als een lichtscenario toch donker wordt, zet Gameplay.KeepLightsAfterShift tijdelijk op false en lever de log aan. Herstart de game om een nieuwe dag/nachttransitie te testen. Deze optie kan gameplay veranderen doordat licht ook de zichtbaarheid van spelobjecten beïnvloedt; daarom alleen gebruiken met eigen instemming.

## Terugdraaien

Sluit Playrooms en voer uit:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rollback-comfort-shift-hud.ps1 -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Playrooms"

Het script herstelt de vorige eigen Shift HUD DLL of verwijdert alleen deze nieuwe HUD indien er nog geen eerdere versie was. De oorspronkelijke co-op- en New Game+-bestanden, interop, patcher, oude Comfort Ball Tracker en saves blijven ongemoeid.

## Auteursrecht en herkomst

Alle bestanden onder ComfortShiftHUD/src/ zijn nieuw geschreven voor ComfortCode en staan onder ComfortShiftHUD/LICENSE (MIT). De originele PlayroomsCoop- en NewGamePlus-bronnen behoren aan hun oorspronkelijke auteur(s); deze toevoeging kopieert of herlicentieert die bestaande broncode niet. Deze mod is niet officieel verbonden aan de makers van Playrooms.
