# Build/install ONLY Comfort's new Shift HUD. Leave the working upstream co-op,
# New Game+, shared preloader patcher, BepInEx and IL2CPP interop untouched.
param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Playrooms',
    [switch]$Apply
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$game = [IO.Path]::GetFullPath($GameDirectory)
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'PlayroomsShiftHUD\PlayroomsShiftHUD.csproj'
$buildFile = Join-Path $root 'PlayroomsShiftHUD\bin\Release\netstandard2.1\PlayroomsShiftHUD.dll'
$interop = Join-Path $game 'BepInEx\interop'
$loader = Join-Path $game 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'
$patcher = Join-Path $game 'BepInEx\patchers\PlayroomsPatcher\PlayroomsPatcher.dll'
$targetFolder = Join-Path $game 'BepInEx\plugins\PlayroomsShiftHUD'
$targetFile = Join-Path $targetFolder 'PlayroomsShiftHUD.dll'
$backupRoot = Join-Path $game 'BepInEx\comfort-backups'

function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Note([string]$message) {
    Write-Host "[ShiftHUD] $message" -ForegroundColor Cyan
}

Check (Test-Path -LiteralPath (Join-Path $game 'Playrooms.exe')) 'Playrooms.exe niet gevonden. Controleer -GameDirectory.'
Check (@(Get-Process -Name Playrooms -ErrorAction SilentlyContinue).Count -eq 0) 'Sluit Playrooms voordat je de mod bouwt of installeert.'
Check (Test-Path -LiteralPath $project) 'Projectbestand ontbreekt. Pak de volledige nieuwste repository-ZIP uit.'
Check (Test-Path -LiteralPath $loader) 'BepInEx Unity IL2CPP ontbreekt.'
Check (Test-Path -LiteralPath $patcher) 'Unity 6000.6 preloader-patcher ontbreekt. Laat de bestaande TheWildLeon-installatie staan.'
foreach ($name in @('Il2Cppmscorlib.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.IMGUIModule.dll')) {
    Check (Test-Path -LiteralPath (Join-Path $interop $name)) "Vereiste interop-DLL ontbreekt: $name"
}
$count = @(Get-ChildItem -LiteralPath $interop -Filter '*.dll' -File).Count
Check ($count -ge 20) "Onverwacht weinig interop-DLL's: $count. Er worden geen bestanden aangepast."
$sdk = Get-Command dotnet -ErrorAction SilentlyContinue
Check ($null -ne $sdk) 'Microsoft .NET SDK 8 of nieuwer ontbreekt (dotnet niet gevonden).'
$sdkList = @(& $sdk.Source --list-sdks)
Check ($LASTEXITCODE -eq 0 -and @($sdkList | Where-Object {
    $_ -match '^(8|9|1[0-9])\.'
}).Count -gt 0) 'Vereist: .NET SDK 8 of nieuwer, niet alleen de runtime.'

Note "Bouwen tegen $count bestaande game-interop-DLL's. Originele mods blijven ongemoeid."
& $sdk.Source build $project -c Release "-p:GameDir=$game"
Check ($LASTEXITCODE -eq 0) 'Build mislukt; er is niets in de spelmap veranderd.'
Check (Test-Path -LiteralPath $buildFile) 'Build gereed, maar PlayroomsShiftHUD.dll niet gevonden.'
Check ((Get-Item -LiteralPath $buildFile).Length -gt 4096) 'De gebouwde DLL is onverwacht klein.'
$hash = (Get-FileHash -LiteralPath $buildFile -Algorithm SHA256).Hash
Note "DLL SHA256: $hash"

if (-not $Apply) {
    Write-Host 'DRY RUN: DLL gebouwd; geen gamebestanden veranderd.' -ForegroundColor Green
    Write-Host 'Gebruik -Apply om uitsluitend PlayroomsShiftHUD.dll te installeren.'
    return
}

$backup = Join-Path $backupRoot ('shift-hud-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$hadPrevious = Test-Path -LiteralPath $targetFile
if ($hadPrevious) {
    Copy-Item -LiteralPath $targetFile -Destination (Join-Path $backup 'PlayroomsShiftHUD.dll.original') -Force
}
$success = $false
try {
    New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
    Copy-Item -LiteralPath $buildFile -Destination $targetFile -Force
    Check ((Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash -eq $hash) 'DLL verificatie na kopieren mislukt.'
    $manifest = @{
        Format = 1
        Type = 'ComfortShiftHUD'
        GameDirectory = $game
        OriginalExisted = $hadPrevious
        InstalledHash = $hash
        InstalledUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText((Join-Path $backup 'backup.json'), $manifest,
        [Text.UTF8Encoding]::new($false))
    $success = $true
}
finally {
    if (-not $success) {
        Write-Warning 'Installatie mislukt, vorige Shift HUD wordt hersteld.'
        if ($hadPrevious) {
            Copy-Item -LiteralPath (Join-Path $backup 'PlayroomsShiftHUD.dll.original') -Destination $targetFile -Force
        }
        elseif (Test-Path -LiteralPath $targetFile) {
            Remove-Item -LiteralPath $targetFile -Force
        }
    }
}
Write-Host "Shift HUD geinstalleerd: $targetFile" -ForegroundColor Green
Write-Host "Backup: $backup"
Write-Host 'Start Playrooms en controleer BepInEx\LogOutput.log op "Comfort Shift HUD 0.1.0 loaded".'
Write-Host 'Herstel: scripts\rollback-shift-hud.ps1 -GameDirectory <Playrooms map>.'
