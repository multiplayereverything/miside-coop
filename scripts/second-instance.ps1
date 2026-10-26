# Opens a second copy of MiSide on this PC, with MiSide Together, next to your normal game.
# It runs from its own folder (private\lab-second: links to your install, its own BepInEx config and log), uses the
# mod build installed in your game, and never touches your saves (-mt-second).
#
# Usage:
#   second-instance.ps1                 open it; join from MULTIPLAYER > JOIN ... (LAN) or F5 > Join 127.0.0.1
#   second-instance.ps1 -Join           open it and join the game hosted on this PC automatically
#   second-instance.ps1 -Name "Friend"   the name shown above that player
param([switch]$Join, [string]$Name = 'Player 2', [int]$Width = 1280, [int]$Height = 720)
$ErrorActionPreference = 'Stop'
$mod = Split-Path -Parent $PSScriptRoot
$game = Split-Path -Parent $mod
$copy = Join-Path $mod 'private\lab-second'

if (-not (Test-Path (Join-Path $copy 'MiSideFull.exe'))) { & (Join-Path $PSScriptRoot 'make-lab.ps1') -Names 'second' }

# Same mod build as your game (a different build is refused by the host).
$installed = Join-Path $game 'BepInEx\plugins\MiSideTogether\MiSideTogether.dll'
if (-not (Test-Path $installed)) { throw "MiSide Together is not installed in $game (run scripts\build.ps1 first)." }
$plugins = Join-Path $copy 'BepInEx\plugins\MiSideTogether'
New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item -Force $installed $plugins

$arguments = "-screen-fullscreen 0 -screen-width $Width -screen-height $Height -mt-second -mt-name `"$Name`""
if ($Join) { $arguments += ' -mt-join 127.0.0.1' }
$p = Start-Process -FilePath (Join-Path $copy 'MiSideFull.exe') -WorkingDirectory $copy -ArgumentList $arguments -PassThru
Write-Output "Second copy started (PID $($p.Id)) as '$Name'. Close its window to stop it."
if (-not $Join) { Write-Output 'To play together: host in your main game (MULTIPLAYER > HOST GAME), then in this copy pick MULTIPLAYER > JOIN ... (LAN), or F5 > Join with address 127.0.0.1.' }
