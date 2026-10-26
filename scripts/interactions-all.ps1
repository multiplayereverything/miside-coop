# Runs the "interactions" tour in every chapter (2 players) and writes one table: per chapter the verdict, and per press
# the rule (mirror, presser, none), whether the client played it and how far apart the two spots were.
# Usage: interactions-all.ps1 [-Chapters "Scene 2 - InGame","Scene 4 - StartSecret"]
# Output: artifacts/tests/interactions-all.md (per-chapter results and logs as for run-tour.ps1).
param([string[]]$Chapters = @())
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
if ($Chapters.Count -eq 0) {
    # Same list as TogetherRuntime.Chapters (Scene 19 cannot be started cold).
    $Chapters = @('Scene 1 - RealRoom', 'Scene 2 - InGame', 'Scene 3 - WeTogether', 'Scene 4 - StartSecret', 'Scene 5 - StartHorror',
        'Scene 6 - BasementFirst', 'Scene 7 - Backrooms', 'Scene 8 - ReRooms', 'Scene 9 - ChibiMita', 'Scene 10 - ManekenWorld',
        'Scene 11 - Backrooms', 'Scene 12 - Freak', 'Scene 13 - HelloCore', 'Scene 14 - MobilePlayer', 'Scene 15 - BasementAndDeath',
        'Scene 16 - TheEnd', 'Scene 17 - Dreamer', 'Scene 20 - FightMita', 'Scene 18 - 2D')
}
$summary = @('# Interactions in every chapter', '', "Run $(Get-Date -Format 'yyyy-MM-dd HH:mm'). Tour: ``interactions`` (host presses, client checks).", '',
    '| Chapter | Verdict | Pressed | Failures |', '| --- | --- | --- | --- |')
$presses = @('', '## Presses', '', '| Chapter | Interaction | Rule | Played on client | Spot distance (m) |', '| --- | --- | --- | --- | --- |')
foreach ($chapter in $Chapters) {
    $lines = & (Join-Path $PSScriptRoot 'run-tour.ps1') -Tour interactions -Chapter $chapter 2>&1
    $name = 'interactions-' + ($chapter -replace '[^A-Za-z0-9]+', '_')
    $verdict = ($lines | Where-Object { $_ -match "^${name}: (PASS|FAIL)" } | Select-Object -Last 1) -replace "^${name}: ", ''
    if (-not $verdict) { $verdict = 'NO RESULT' }
    $fails = @($lines | Where-Object { $_ -match '^\s+- ' } | ForEach-Object { $_.Trim().TrimStart('-').Trim() }) -join '; '
    $hostLog = Join-Path $modRoot "artifacts/tests/logs/$name/lab-host.log"
    $clientLog = Join-Path $modRoot "artifacts/tests/logs/$name/lab-client.log"
    $pressed = if (Test-Path $hostLog) { (Select-String -Path $hostLog -Pattern 'interactions.pressed (PASS|FAIL) (.*)' | Select-Object -Last 1).Matches.Groups[2].Value } else { '' }
    $summary += "| $chapter | $verdict | $pressed | $($fails -replace '\|', '/') |"
    if (Test-Path $clientLog) {
        foreach ($m in Select-String -Path $clientLog -Pattern 'MT-PRESS (.*)$') {
            $f = $m.Matches[0].Groups[1].Value.Split('|')
            if ($f.Count -ge 5) { $presses += "| $($f[0]) | ``$(($f[1] -split '/')[-1])`` | $($f[2]) | $(if ($f[3] -eq '1') { 'yes' } else { 'no' }) | $($f[4]) |" }
        }
    }
    Write-Output "${chapter}: $verdict"
}
$out = Join-Path $modRoot 'artifacts/tests/interactions-all.md'
($summary + $presses) | Set-Content -Encoding utf8 $out
Write-Output "Table: $out"
