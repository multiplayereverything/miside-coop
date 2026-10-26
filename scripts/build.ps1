# Builds MiSideTogether.dll and installs it into this game's BepInEx\plugins\MiSideTogether.
#   scripts\build.ps1            build + install
#   scripts\build.ps1 -NoInstall build only (artifacts\build\MiSideTogether.dll)
param([switch]$NoInstall)
$ErrorActionPreference = 'Stop'
$mod = Split-Path -Parent $PSScriptRoot
$game = Split-Path -Parent $mod

# The machine-wide dotnet has runtimes only; the SDK lives in %USERPROFILE%\.dotnet.
$userDotnet = Join-Path $env:USERPROFILE '.dotnet'
if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) { $env:DOTNET_ROOT = $userDotnet; $env:PATH = "$userDotnet;$env:PATH" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if (-not (Test-Path (Join-Path $game 'BepInEx\interop\Assembly-CSharp.dll'))) {
    throw 'BepInEx\interop is missing. Install BepInEx 6 (Unity.IL2CPP) and start the game once first.'
}

# Animator parameter table from the user's own game data (the IL2CPP build strips the runtime API that lists them).
$params = Join-Path $mod 'artifacts\generated\animator-params.tsv'
if (-not (Test-Path $params)) {
    $venv = Join-Path $mod 'private\venv\Scripts\python.exe'
    if (-not (Test-Path $venv)) {
        python -m venv (Join-Path $mod 'private\venv')
        & $venv -m pip install -q UnityPy
    }
    & $venv -I (Join-Path $mod 'tools\extract_animator_params.py') (Join-Path $game 'MiSideFull_Data') $params
    if ($LASTEXITCODE -ne 0) { throw 'Animator parameter extraction failed.' }
}

dotnet build (Join-Path $mod 'MiSideTogether.csproj') -c Release -nologo -v q "-p:GameDir=$game"
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
$dll = Join-Path $mod 'artifacts\build\MiSideTogether.dll'
Write-Host ("Built {0} ({1:N0} bytes)" -f $dll, (Get-Item $dll).Length)

if (-not $NoInstall) {
    $target = Join-Path $game 'BepInEx\plugins\MiSideTogether'
    New-Item -ItemType Directory -Force $target | Out-Null
    # Keep the build being replaced (the last 10) in private\plugin-backup.
    $installed = Join-Path $target 'MiSideTogether.dll'
    if (Test-Path $installed) {
        $backups = Join-Path $PSScriptRoot '..\private\plugin-backup'
        New-Item -ItemType Directory -Force $backups | Out-Null
        Copy-Item $installed (Join-Path $backups ("MiSideTogether-{0}.dll" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))) -Force
        Get-ChildItem $backups -Filter '*.dll' | Sort-Object Name -Descending | Select-Object -Skip 10 | Remove-Item -Force
    }
    Copy-Item $dll $target -Force
    $hash = (Get-FileHash (Join-Path $target 'MiSideTogether.dll') -Algorithm SHA256).Hash
    Write-Host "Installed to $target (SHA256 $hash)"
}
