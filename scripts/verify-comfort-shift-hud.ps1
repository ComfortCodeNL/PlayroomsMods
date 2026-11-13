# Read-only check after installing ComfortShiftHUD.dll.
param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Playrooms'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$game = [IO.Path]::GetFullPath($GameDirectory)
$root = Join-Path $game 'BepInEx'
$plugin = Join-Path $root 'plugins\ComfortShiftHUD\ComfortShiftHUD.dll'
$other = Join-Path $root 'plugins\PlayroomsCoop\PlayroomsCoop.dll'
$patcher = Join-Path $root 'patchers\PlayroomsPatcher\PlayroomsPatcher.dll'
if (-not (Test-Path -LiteralPath (Join-Path $game 'Playrooms.exe'))) { throw 'Geen Playrooms.exe gevonden.' }
if (-not (Test-Path -LiteralPath $plugin)) {
    Write-Warning 'ComfortShiftHUD.dll ontbreekt. Voer eerst scripts\install-comfort-shift-hud.ps1 -Apply uit.'
    exit 2
}
Write-Host 'ComfortShiftHUD.dll: aanwezig'
Write-Host ('Originele co-opmod: ' + $(if (Test-Path -LiteralPath $other) { 'aanwezig' } else { 'niet aangetroffen' }))
Write-Host ('Unity 6000.6-patcher: ' + $(if (Test-Path -LiteralPath $patcher) { 'aanwezig' } else { 'niet aangetroffen' }))
$logs = @((Join-Path $root 'LogOutput.log'), (Join-Path $root 'LogOutput.txt')) | Where-Object { Test-Path -LiteralPath $_ }
if ($logs.Count -eq 0) { Write-Warning 'Geen BepInEx-log. Start de game en sluit deze daarna.'; exit 3 }
$log = @($logs | Sort-Object { (Get-Item -LiteralPath $_).LastWriteTimeUtc } -Descending)[0]
$logTime = (Get-Item -LiteralPath $log).LastWriteTimeUtc
if ($logTime -lt (Get-Item -LiteralPath $plugin).LastWriteTimeUtc) {
    Write-Warning 'De BepInEx-log is ouder dan de HUD-DLL. Start de game opnieuw.'
    exit 3
}
$lines = @(Get-Content -LiteralPath $log -Tail 3000)
$loaded = @($lines | Where-Object { $_ -match 'Comfort Shift HUD\s+0\.1\.0 loaded' }).Count -gt 0
$update = @($lines | Where-Object { $_ -match '\[ShiftHUD\] Unity Update callback active' }).Count -gt 0
$gui = @($lines | Where-Object { $_ -match '\[ShiftHUD\] Unity OnGUI callback active' }).Count -gt 0
$rendered = @($lines | Where-Object { $_ -match '\[ShiftHUD\] HUD frame rendered without exception' }).Count -gt 0
$drawError = @($lines | Where-Object { $_ -match '\[ShiftHUD\].*HUD drawing failed at stage' -or $_ -match 'Method unstripping failed' } | Select-Object -Last 5)
$hook = @($lines | Where-Object { $_ -match '\[ShiftHUD\] Lighting interception installed' }).Count -gt 0
$night = @($lines | Where-Object { $_ -match '\[ShiftHUD\] Night lighting transition suppressed' }).Count -gt 0
$errors = @($lines | Where-Object { $_ -match '(?i)\[ShiftHUD\].*(Warning|error|failed|unavailable|Unexpected|missing)' } | Select-Object -Last 12)
Write-Host ('BepInEx-plugin-load: ' + $loaded)
Write-Host ('Unity Update callback: ' + $update)
Write-Host ('Unity OnGUI callback: ' + $gui)
Write-Host ('HUD frame zonder uitzondering getekend: ' + $rendered)
if ($drawError.Count -gt 0) {
    Write-Warning 'Tekenfouten aangetroffen in de huidige game-log:'
    foreach ($line in $drawError) { Write-Host $line }
}
Write-Host ('Nachtlicht-hook geinstalleerd: ' + $hook)
Write-Host ('Nachtlichttransitie werkelijk onderschept: ' + $night)
if ($errors.Count -gt 0) {
    Write-Warning 'Mogelijke HUD/lichteffectproblemen in BepInEx-log:'
    foreach ($line in $errors) { Write-Host $line }
}
if (-not $loaded -or -not $update -or -not $gui -or -not $rendered) {
    Write-Warning 'Plugin, Unity callbacks of een werkelijk getekend HUD-frame zijn niet allemaal bevestigd.'
    exit 2
}
Write-Host 'HUD-plugin, Unity callbacks en een daadwerkelijk getekend HUD-frame zijn bevestigd.' -ForegroundColor Green
Write-Warning 'Ballen, timer, kamers, END DAY en daadwerkelijk brandend licht zijn nog NIET in-game gevalideerd.'
