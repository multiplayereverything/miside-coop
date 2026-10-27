# Finds the MiSide install the scripts build against and test with, in this order:
#   1. $env:MISIDE_GAME
#   2. the folder above this repo (the repo cloned inside the game, as on the first PC)
#   3. steamapps\common\MiSide in every Steam library (libraryfolders.vdf)
# Dot-source it: . (Join-Path $PSScriptRoot 'game-dir.ps1'); $game = Get-MiSideDir
function Get-MiSideDir {
    $exe = 'MiSideFull.exe'
    if ($env:MISIDE_GAME) {
        if (Test-Path (Join-Path $env:MISIDE_GAME $exe)) { return (Resolve-Path $env:MISIDE_GAME).Path }
        throw "MISIDE_GAME is $env:MISIDE_GAME, but there's no $exe there."
    }
    $parent = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    if (Test-Path (Join-Path $parent $exe)) { return $parent }
    $steam = $null
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam') {
        $p = Get-ItemProperty $key -ErrorAction SilentlyContinue
        if ($p -and $p.SteamPath) { $steam = $p.SteamPath; break }
        if ($p -and $p.InstallPath) { $steam = $p.InstallPath; break }
    }
    $libraries = @()
    if ($steam) {
        $libraries += $steam
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { $libraries += $m.Groups[1].Value -replace '\\\\', '\' }
        }
    }
    $libraries += "${env:ProgramFiles(x86)}\Steam"
    foreach ($lib in $libraries) {
        $dir = Join-Path $lib 'steamapps\common\MiSide'
        if (Test-Path (Join-Path $dir $exe)) { return (Resolve-Path $dir).Path }
    }
    throw "MiSide not found. Set MISIDE_GAME to the folder that holds $exe."
}
