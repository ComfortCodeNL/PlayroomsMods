# Restore/remove ONLY ComfortShiftHUD.dll; all other Playrooms mods remain untouched.
param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Playrooms',
    [string]$BackupDirectory = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$game = [IO.Path]::GetFullPath($GameDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $game 'Playrooms.exe'))) { throw 'Playrooms.exe ontbreekt.' }
if (@(Get-Process -Name Playrooms -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Sluit Playrooms eerst.' }
$root = Join-Path $game 'BepInEx\comfort-backups'
if (-not $BackupDirectory) {
    if (-not (Test-Path -LiteralPath $root)) { throw 'Geen Comfort Shift HUD backup gevonden.' }
    $candidates = @(Get-ChildItem -LiteralPath $root -Directory |
        Where-Object { $_.Name -like 'shift-hud-*' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'backup.json')) } |
        Sort-Object Name -Descending)
    if ($candidates.Count -eq 0) { throw 'Geen Comfort Shift HUD backup gevonden.' }
    $BackupDirectory = $candidates[0].FullName
}
$backup = [IO.Path]::GetFullPath($BackupDirectory)
$manifest = Join-Path $backup 'backup.json'
if (-not (Test-Path -LiteralPath $manifest)) { throw 'Backupmanifest ontbreekt. Niets gewijzigd.' }
$m = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if ($m.Format -ne 1 -or $m.Type -ne 'ComfortShiftHUD' -or
    [IO.Path]::GetFullPath([string]$m.GameDirectory) -ne $game) {
    throw 'Backup hoort niet bij deze mod of dit spel. Niets gewijzigd.'
}
$old = Join-Path $backup 'ComfortShiftHUD.original.dll'
if ($m.PreviousDllExisted -and -not (Test-Path -LiteralPath $old)) {
    throw 'Vorige DLL ontbreekt in de backup. Niets gewijzigd.'
}
$target = Join-Path $game 'BepInEx\plugins\ComfortShiftHUD\ComfortShiftHUD.dll'
if (Test-Path -LiteralPath $target) {
    Copy-Item -LiteralPath $target -Destination (Join-Path $backup ('rolled-back-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.dll')) -Force
}
if ($m.PreviousDllExisted) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $old -Destination $target -Force
} elseif (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Force
}
Write-Host 'Comfort Shift HUD teruggedraaid. Alle andere mods en savebestanden zijn ongemoeid.' -ForegroundColor Green
