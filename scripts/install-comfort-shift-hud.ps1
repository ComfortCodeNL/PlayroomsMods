# Build and optionally install ONLY ComfortShiftHUD.dll.
# Original PlayroomsCoop, NewGamePlus, runtime patcher, interop and saves are untouched.
param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Playrooms',
    [switch]$Apply
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$game = [IO.Path]::GetFullPath($GameDirectory)
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repo 'ComfortShiftHUD\ComfortShiftHUD.csproj'
$core = Join-Path $game 'BepInEx\core'
$interop = Join-Path $game 'BepInEx\interop'
$patcher = Join-Path $game 'BepInEx\patchers\PlayroomsPatcher\PlayroomsPatcher.dll'
$destination = Join-Path $game 'BepInEx\plugins\ComfortShiftHUD\ComfortShiftHUD.dll'
$compiled = Join-Path $repo 'ComfortShiftHUD\bin\Release\net6.0\ComfortShiftHUD.dll'
$backupRoot = Join-Path $game 'BepInEx\comfort-backups'
$backup = Join-Path $backupRoot ('shift-hud-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))

function Need([bool]$ok, [string]$message) { if (-not $ok) { throw $message } }
function Info([string]$message) { Write-Host "[ComfortShiftHUD] $message" -ForegroundColor Cyan }

Need (Test-Path -LiteralPath (Join-Path $game 'Playrooms.exe')) 'Playrooms.exe ontbreekt. Geef de juiste -GameDirectory op.'
Need (@(Get-Process -Name Playrooms -ErrorAction SilentlyContinue).Count -eq 0) 'Sluit Playrooms voordat je een mod installeert.'
Need (Test-Path -LiteralPath $project) 'Het complete repositoryproject is niet uitgepakt.'
Need (Test-Path -LiteralPath $patcher) 'Unity 6000.6-preloaderpatcher ontbreekt. Installeer eerst het originele modpakket.'
foreach ($required in @('BepInEx.Core.dll','BepInEx.Unity.IL2CPP.dll','Il2CppInterop.Runtime.dll','0Harmony.dll')) {
    Need (Test-Path -LiteralPath (Join-Path $core $required)) "BepInEx kernbestand ontbreekt: $required"
}
foreach ($required in @('Il2Cppmscorlib.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.TextRenderingModule.dll')) {
    Need (Test-Path -LiteralPath (Join-Path $interop $required)) "Unity-interop ontbreekt: $required"
}
$interopCount = @(Get-ChildItem -LiteralPath $interop -File -Filter '*.dll').Count
Need ($interopCount -ge 100) "Slechts $interopCount interop-DLL's gevonden. Dit is geen bekende compatibele installatie."

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
Need ($null -ne $dotnet) 'Microsoft .NET SDK ontbreekt. Installeer de officiele .NET SDK 8 of nieuwer.'
$sdks = @(& $dotnet.Source --list-sdks)
Need ($LASTEXITCODE -eq 0 -and @($sdks | Where-Object { $_ -match '^(8|9|1[0-9])\.' }).Count -gt 0) 'Een .NET SDK 8 of nieuwer is vereist.'

Info 'Bouwt alleen onze losse HUD-plugin tegen bestaande IL2CPP-assemblies...'
& $dotnet.Source restore $project "-p:GameDir=$game"
Need ($LASTEXITCODE -eq 0) 'dotnet restore mislukt. Geen spelbestanden gewijzigd.'
& $dotnet.Source build $project -c Release --no-restore "-p:GameDir=$game"
Need ($LASTEXITCODE -eq 0) 'dotnet build mislukt. Geen spelbestanden gewijzigd.'
Need (Test-Path -LiteralPath $compiled) 'Build meldde succes maar ComfortShiftHUD.dll is niet gevonden.'
Need ((Get-Item -LiteralPath $compiled).Length -gt 4096) 'ComfortShiftHUD.dll is onverwacht klein.'
$hash = (Get-FileHash -LiteralPath $compiled -Algorithm SHA256).Hash
Info "Build SHA256: $hash"
Info 'Originele co-op, NG+, patcher, interop en bestaande tracker worden niet gewijzigd.'

if (-not $Apply) {
    Write-Host 'DRY RUN succesvol: er is niets in de Playrooms-map veranderd.' -ForegroundColor Green
    Write-Host 'Voer dezelfde opdracht met -Apply uit om uitsluitend de HUD-DLL te installeren.'
    return
}

New-Item -ItemType Directory -Path $backup -Force | Out-Null
$hadPrevious = Test-Path -LiteralPath $destination
if ($hadPrevious) {
    Copy-Item -LiteralPath $destination -Destination (Join-Path $backup 'ComfortShiftHUD.original.dll') -Force
}
$done = $false
try {
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $compiled -Destination $destination -Force
    Need ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $hash) 'SHA256-mismatch bij installatie.'
    $manifest = @{
        Format = 1
        Type = 'ComfortShiftHUD'
        GameDirectory = $game
        PreviousDllExisted = $hadPrevious
        InstalledSha256 = $hash
        InstalledUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 3
    [IO.File]::WriteAllText((Join-Path $backup 'backup.json'), $manifest, [Text.UTF8Encoding]::new($false))
    $done = $true
}
finally {
    if (-not $done) {
        Write-Warning 'Installatie mislukt. Vorige HUD wordt hersteld.'
        if ($hadPrevious) {
            Copy-Item -LiteralPath (Join-Path $backup 'ComfortShiftHUD.original.dll') -Destination $destination -Force
        } elseif (Test-Path -LiteralPath $destination) {
            Remove-Item -LiteralPath $destination -Force
        }
    }
}
Write-Host "Alleen ComfortShiftHUD.dll geinstalleerd: $destination" -ForegroundColor Green
Write-Host "Backup: $backup"
Write-Host 'Start Playrooms; zoek in BepInEx/LogOutput.log naar Comfort Shift HUD 0.1.0 loaded.' -ForegroundColor Yellow
