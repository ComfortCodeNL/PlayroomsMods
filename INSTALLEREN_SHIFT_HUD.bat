@echo off
setlocal
cd /d "%~dp0"
echo Comfort Shift HUD - alleen de eigen DLL wordt gebouwd/geinstalleerd.
echo Sluit Playrooms voordat je doorgaat.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install-comfort-shift-hud.ps1" -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Playrooms" -Apply
set "ret=%errorlevel%"
echo.
if not "%ret%"=="0" echo Installatie mislukt. Controleer de foutmelding hierboven.
pause
exit /b %ret%
