# Creates lab copies of the game for local multi-instance testing: private\lab-host, private\lab-client.
# Exe and DLLs are hardlinked, MiSideFull_Data and the BepInEx runtime are junctioned, so never edit game bytes
# through a lab copy. Each copy has its own BepInEx config, plugins, cache and log, plus steam_appid.txt so a
# direct launch keeps its command line instead of being relaunched by Steam.
param([string[]]$Names = @('host', 'client'))
$ErrorActionPreference = 'Stop'
$mod = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'game-dir.ps1'); $game = Get-MiSideDir
foreach ($name in $Names) {
    $lab = Join-Path $mod "private\lab-$name"
    New-Item -ItemType Directory -Force $lab | Out-Null
    foreach ($file in 'MiSideFull.exe', 'GameAssembly.dll', 'UnityPlayer.dll', 'baselib.dll', 'UnityCrashHandler64.exe', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') {
        $dst = Join-Path $lab $file
        if (-not (Test-Path $dst)) { New-Item -ItemType HardLink -Path $dst -Target (Join-Path $game $file) | Out-Null }
    }
    foreach ($dir in 'MiSideFull_Data', 'Data', 'dotnet') {
        $dst = Join-Path $lab $dir
        if (-not (Test-Path $dst)) { New-Item -ItemType Junction -Path $dst -Target (Join-Path $game $dir) | Out-Null }
    }
    $bie = Join-Path $lab 'BepInEx'
    New-Item -ItemType Directory -Force $bie, "$bie\config", "$bie\plugins", "$bie\cache" | Out-Null
    foreach ($dir in 'core', 'interop', 'unity-libs', 'patchers') {
        $dst = Join-Path $bie $dir
        if (-not (Test-Path $dst)) { New-Item -ItemType Junction -Path $dst -Target (Join-Path $game "BepInEx\$dir") | Out-Null }
    }
    # Reuse the generated interop instead of regenerating it per copy.
    Copy-Item (Join-Path $game 'BepInEx\cache\*') "$bie\cache" -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $game 'BepInEx\config\BepInEx.cfg') "$bie\config" -Force -ErrorAction SilentlyContinue
    Set-Content -Path (Join-Path $lab 'steam_appid.txt') -Value '2527500' -NoNewline
    Write-Host "Lab copy ready: $lab"
}
