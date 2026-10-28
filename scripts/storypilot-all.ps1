# Runs the "storypilot" tour in every chapter (2 players): the host plays the story as far as simple player actions take
# it, and the client must stay on the same quests. Writes one table: per chapter the verdict, how far the story got and
# whether both copies matched.
# Usage: storypilot-all.ps1 [-Chapters "Scene 2 - InGame","Scene 4 - StartSecret"]
# Output: artifacts/tests/storypilot-all.md (per-chapter results and logs as for run-tour.ps1).
param([string[]]$Chapters = @())
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
if ($Chapters.Count -eq 0) {
    # As interactions-all.ps1 without Scene 1 (its tutorial and phone app have the scene1 tour): Scene 18 (the visual
    # novel) has its own tour; Scene 19 cannot be started cold.
    $Chapters = @('Scene 2 - InGame', 'Scene 3 - WeTogether', 'Scene 4 - StartSecret', 'Scene 5 - StartHorror',
        'Scene 6 - BasementFirst', 'Scene 7 - Backrooms', 'Scene 8 - ReRooms', 'Scene 9 - ChibiMita', 'Scene 10 - ManekenWorld',
        'Scene 11 - Backrooms', 'Scene 12 - Freak', 'Scene 13 - HelloCore', 'Scene 14 - MobilePlayer', 'Scene 15 - BasementAndDeath',
        'Scene 16 - TheEnd', 'Scene 17 - Dreamer', 'Scene 20 - FightMita')
}
$rows = @('# Story autopilot in every chapter', '', "Run $(Get-Date -Format 'yyyy-MM-dd HH:mm'). Tour: ``storypilot`` (host plays, client must stay on the same quests).", '',
    '| Chapter | Verdict | Host: how far | Client: same quests | Other failures |', '| --- | --- | --- | --- | --- |')
foreach ($chapter in $Chapters) {
    $name = 'storypilot-' + ($chapter -replace '[^A-Za-z0-9]+', '_')
    & (Join-Path $PSScriptRoot 'run-tour.ps1') -Tour storypilot -Chapter $chapter *> $null
    $resultFile = Join-Path $modRoot "artifacts/tests/$name-results.json"
    $verdict = 'NO RESULT'; $fails = ''
    if (Test-Path $resultFile) {
        $result = Get-Content $resultFile -Raw | ConvertFrom-Json
        $verdict = if ($result.pass) { 'PASS' } else { 'FAIL' }
        $fails = @($result.failures | Where-Object { $_ -notmatch 'expected passing check' }) -join '; '
    }
    $hostLog = Join-Path $modRoot "artifacts/tests/logs/$name/lab-host.log"
    $clientLog = Join-Path $modRoot "artifacts/tests/logs/$name/lab-client.log"
    $acted = if (Test-Path $hostLog) { Select-String -Path $hostLog -Pattern 'pilot.acted (PASS|FAIL) (.*)' | Select-Object -Last 1 }
    $same = if (Test-Path $clientLog) { Select-String -Path $clientLog -Pattern 'pilot.same (PASS|FAIL) (.*)' | Select-Object -Last 1 }
    $a = if ($acted) { $acted.Matches[0].Groups[2].Value } else { '' }
    $s = if ($same) { $same.Matches[0].Groups[1].Value + ': ' + $same.Matches[0].Groups[2].Value } else { '' }
    $rows += "| $chapter | $verdict | $($a -replace '\|', '/') | $($s -replace '\|', '/') | $($fails -replace '\|', '/') |"
    Write-Output "${chapter}: $verdict"
}
$out = Join-Path $modRoot 'artifacts/tests/storypilot-all.md'
$rows | Set-Content -Encoding utf8 $out
Write-Output "Table: $out"
