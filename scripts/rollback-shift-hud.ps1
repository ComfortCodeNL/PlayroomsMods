# Restore ONLY the optional Comfort Shift HUD. No upstream co-op or BepInEx files touched.
param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Playrooms',
    [string]$BackupDirectory = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$game = [IO.Path]::GetFullPath($GameDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $game 'Playrooms.exe'))) { throw 'Playrooms.exe niet gevonden.' }
if (@(Get-Process -Name Playrooms -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Sluit Playrooms eerst.' }
$folder = Join-Path $game 'BepInEx\comfort-backups'
if (-not $BackupDirectory) {
    if (-not (Test-Path -LiteralPath $folder)) { throw 'Geen Shift HUD backup gevonden.' }
    $candidates = @(Get-ChildItem -LiteralPath $folder -Directory |
        Where-Object {
            $_.Name -like 'shift-hud-*' -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'backup.json'))
        } | Sort-Object Name -Descending)
    if ($candidates.Count -eq 0) { throw 'Geen voltooide Shift HUD-installatiebackup gevonden.' }
    $BackupDirectory = $candidates[0].FullName
}
$backup = [IO.Path]::GetFullPath($BackupDirectory)
$manifest = Join-Path $backup 'backup.json'
if (-not (Test-Path -LiteralPath $manifest)) { throw 'Ongeldige backup: manifest ontbreekt.' }
$m = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if ($m.Format -ne 1 -or $m.Type -ne 'ComfortShiftHUD' -or
    [IO.Path]::GetFullPath([string]$m.GameDirectory) -ne $game) {
    throw 'De backup hoort niet bij deze mod of spelmap. Niets gewijzigd.'
}
$old = Join-Path $backup 'PlayroomsShiftHUD.dll.original'
if ($m.OriginalExisted -and -not (Test-Path -LiteralPath $old)) {
    throw 'Originele Shift HUD DLL ontbreekt in de backup; niets gewijzigd.'
}
$target = Join-Path $game 'BepInEx\plugins\PlayroomsShiftHUD\PlayroomsShiftHUD.dll'
$candidate = Join-Path $backup ('rollback-candidate-' +
    (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.dll')
if (Test-Path -LiteralPath $target) {
    Copy-Item -LiteralPath $target -Destination $candidate -Force
}
if ($m.OriginalExisted) {
    Copy-Item -LiteralPath $old -Destination $target -Force
}
elseif (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Force
}
Write-Host 'Comfort Shift HUD hersteld. PlayroomsCoop, New Game+, patcher, interop en saves ongewijzigd.' -ForegroundColor Green
