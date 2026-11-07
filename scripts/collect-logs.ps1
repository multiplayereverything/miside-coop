# Copies this PC's game and session logs to private/playtests/<date>/ before the next launch overwrites them. Skips files already saved.
# Usage: collect-logs.ps1 [-Date 2026-11-06]
param([string]$Date = (Get-Date -Format 'yyyy-MM-dd'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'game-dir.ps1')
$game = Get-MiSideDir
$out = Join-Path (Split-Path $PSScriptRoot -Parent) "private/playtests/$Date"
New-Item -ItemType Directory -Force $out | Out-Null
$unity = Join-Path $env:USERPROFILE 'AppData\LocalLow\AIHASTO\MiSideFull'
$sources = @((Join-Path $game 'BepInEx\LogOutput.log'), (Join-Path $unity 'Player.log'), (Join-Path $unity 'Player-prev.log'))
$sessions = Join-Path $game 'BepInEx\MiSideTogether-logs'
if (Test-Path $sessions) { $sources += (Get-ChildItem $sessions -Filter 'session-*.log').FullName }
$known = @{}
Get-ChildItem $out -File | ForEach-Object { $known[(Get-FileHash $_.FullName).Hash] = $_.Name }
foreach ($source in $sources) {
    if (-not (Test-Path $source)) { continue }
    $hash = (Get-FileHash $source).Hash
    if ($known.ContainsKey($hash)) { continue }
    $item = Get-Item $source
    $name = "$($item.LastWriteTime.ToString('HHmmss'))-$($item.Name)"
    Copy-Item $source (Join-Path $out $name)
    $known[$hash] = $name
    Write-Output "saved $name ($($item.Length) bytes)"
}
Write-Output "Logs are in $out"
