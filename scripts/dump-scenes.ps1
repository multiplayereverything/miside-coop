# Loads each chapter in a lab copy and saves its hierarchy and UnityEvent wiring to private/game-study/scenes/ (game study, never distribute).
# Usage: dump-scenes.ps1 [-Chapters "Scene 2 - InGame",...]
param([string[]]$Chapters = @('SceneMenu', 'Scene 1 - RealRoom', 'Scene 2 - InGame', 'Scene 3 - WeTogether', 'Scene 4 - StartSecret', 'Scene 5 - StartHorror',
    'Scene 6 - BasementFirst', 'Scene 7 - Backrooms', 'Scene 8 - ReRooms', 'Scene 9 - ChibiMita', 'Scene 10 - ManekenWorld', 'Scene 11 - Backrooms', 'Scene 12 - Freak',
    'Scene 13 - HelloCore', 'Scene 14 - MobilePlayer', 'Scene 15 - BasementAndDeath', 'Scene 16 - TheEnd', 'Scene 17 - Dreamer', 'Scene 18 - 2D', 'Scene 20 - FightMita'))
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
$out = Join-Path $modRoot 'private/game-study/scenes'
New-Item -ItemType Directory -Force $out | Out-Null
foreach ($chapter in $Chapters) {
    $key = $chapter -replace ' ', '_'
    $dump = Join-Path $modRoot "private/lab-host/private-dump-$key.txt"
    $wires = Join-Path $modRoot "private/lab-host/private-wires-$key.txt"
    Remove-Item $dump, $wires -ErrorAction SilentlyContinue
    # 'interactions' honours -Chapter ('held' always plays Scenes 1 and 2).
    $tourArgs = @{ Tour = 'interactions'; Players = 1; Seconds = 90; ExtraHostArgs = "-mt-dump $key" }
    if ($chapter -ne 'SceneMenu') { $tourArgs.Chapter = $chapter }
    # The tour's verdict doesn't matter here: it only has to load the scene.
    try { & (Join-Path $PSScriptRoot 'run-tour.ps1') @tourArgs *> $null } catch { Write-Output "$chapter tour error: $_" }
    if ((Test-Path $dump) -and (Test-Path $wires)) {
        Copy-Item $dump, $wires $out -Force
        Write-Output "${chapter}: dumped ($((Get-Item $dump).Length) bytes hierarchy, $((Get-Item $wires).Length) bytes wiring)"
    } else { Write-Output "${chapter}: NOT DUMPED" }
}
