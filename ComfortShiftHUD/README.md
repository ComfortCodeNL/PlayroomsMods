# Comfort Shift HUD voor Playrooms

Zelfstandige BepInEx 6 Unity IL2CPP-plugin naast de originele PlayroomsCoop en New Game+. Installer wijzigt uitsluitend de eigen ComfortShiftHUD.dll en maakt eerst een backup.

## Wat verandert er?

- Toont resterende ballen en totaal in de huidige kamer, de tijd tot shifteinde, en de namen/aantallen opgeruimde kamers.
- **END GAME** verschijnt uitsluitend tijdens de laatste **10 seconden vóór het shifteinde**, wanneer de echte DayTimeService.RemainingTime tussen 0 en 10 seconden staat en de dag nog actief is. Bij 0 seconden verdwijnt de banner.
- Playrooms mag na de shift **weer normaal donker worden**. De NightLightKeeper Harmony-hook is verwijderd en er wordt geen lokale vervangende lamp ingeschakeld.
- Je mag na de shift **gewoon ballen blijven rapen in het donker**. Er is geen geforceerde stop, spelklokwijziging, teleportatie of automatisch einde van de dag.

## Cheats: ALT+4 t/m ALT+7

| Toets | Actie |
| --- | --- |
| **ALT+4** | Ontgrendelt de handstofzuiger, probeert hem uit te rusten en maximaliseert gevonden vacuum- en suctionskills |
| **ALT+5** | Zet alle door het spel bekende skilldefinities op hun maximale niveau |
| **ALT+6** | Schrijft **€10.000,00** bij op je bestaande saldo (1.000.000 game-centen, niet overschrijven) |
| **ALT+7** | Ontgrendelt zes normale kamersleutels: PlayroomLeft1/2/3 en PlayroomRight1/2/3 |

De shortcuts werken uitsluitend met het Playrooms-venster actief, één keer per toetsdruk. Ze zijn standaard ingeschakeld en optioneel uit te schakelen via Cheats.EnableAltHotkeys=false in de BepInEx-config. Ze zijn onafhankelijk van de co-op F8-toets en de oude tracker F9/F10.

**Let op:** ALT+4, ALT+5, ALT+6 en ALT+7 **wijzigen de savegame/progressie**. Maak eerst een back-up en test bij voorkeur solo. Achievements, balans en co-opvoortgang kunnen worden beïnvloed. Een eerdere cheat terugdraaien vereist de oorspronkelijke saveback-up, niet alleen het terugzetten van de mod.

De gebruikte ToolIds, DoorKeyIds, SkillService, StatisticService en gamecurrency komen uit de echte Playrooms-interop. Of de gameclient de nieuwe shortcuts daadwerkelijk volledig uitvoert, moet nog in Playrooms getest worden. Bij ontbrekende services geeft de mod een foutmelding en logt de mislukte actie in BepInEx.

## Installeren (Windows)

Vereisten: bestaande werkende Playrooms-installatie met BepInEx 6, TheWildLeon Unity 6000.6-patcher, de gegenereerde interop-DLL's en .NET SDK 8 of hoger. Niet opnieuw interop genereren.

1. Sluit Playrooms. Pak de nieuwste repository-ZIP uit in een nieuwe map.
2. Open PowerShell in de uitgepakte hoofdmap en doe eerst een dry run:

       powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-comfort-shift-hud.ps1 -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Playrooms"

3. Installeer na een succesvolle build alleen de eigen plugin:

       powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-comfort-shift-hud.ps1 -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Playrooms" -Apply

Doel: Playrooms/BepInEx/plugins/ComfortShiftHUD/ComfortShiftHUD.dll. Automatische backups van de vorige eigen DLL staan in Playrooms/BepInEx/comfort-backups/shift-hud-*.

**Belangrijk:** deze repository bevat ook een oudere, alternatieve pluginmap genaamd PlayroomsShiftHUD met dezelfde plugin-GUID en een experimentele lichtfunctie. Installeer die niet tegelijk! Gebruik uitsluitend de installer met de naam install-comfort-shift-hud.ps1.

## Testen en logs

Start Playrooms, laad je savegame en controleer of de HUD wordt getekend. Test de eindwaarschuwing en toetscombinaties. Sluit de game en voer uit:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-comfort-shift-hud.ps1 -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Playrooms"

De verifier zoekt onder meer de logregels Comfort Shift HUD 0.1.0 loaded, Unity Update callback active, Unity OnGUI callback active, en HUD frame rendered without exception. Bij een renderfout staat de exacte tekenfase in het log. Elke ALT-actie logt een eigen resultaat of fout. De verifier bewijst nog niet dat alle gameplayeffecten correct zijn.

De volgende praktijktests zijn nog nodig: werkelijk aftellen van tien tot nul, natuurlijk donker worden, verder ballen rapen, uitrusten van de handstofzuiger, de skill-effecten, €10.000 bijschrijving en toegang door alle zes deuren.

## Configuratie

In Playrooms/BepInEx/config/nl.comfortcode.playrooms.shifthud.cfg:

| Optie | Default |
| --- | --- |
| Display.ShowHud | true |
| Display.ShowCompletedRooms | true |
| Display.MaxRoomNames | 10 |
| Display.RefreshInterval | 0.75 |
| Cheats.EnableAltHotkeys | true |

De oude waarde Gameplay.KeepLightsAfterShift kan blijven staan, maar wordt niet meer gelezen en heeft geen effect.

## Terugdraaien

Sluit Playrooms en voer uit:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\rollback-comfort-shift-hud.ps1 -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Playrooms"

Dit herstelt/verwijdert alleen de eigen plugin-DLL, niet door cheats gewijzigde savegamegegevens.

## Herkomst

De eigen ComfortShiftHUD-code valt onder ComfortShiftHUD/LICENSE (MIT). De oorspronkelijke co-op- en New Game+-code blijven eigendom van de oorspronkelijke auteurs. Deze mod is niet officieel verbonden aan Two Pixels.
