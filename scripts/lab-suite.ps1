# Runs every feature tour in a row and prints one line per tour. Results: artifacts/tests/<tour>-results.json.
# Usage: lab-suite.ps1 [-Tours connect,avatars,...] [-WithChapters]
param([string[]]$Tours = @('connect', 'avatars', 'story', 'mita', 'cutscene', 'world', 'carry', 'dialogue', 'gather', 'room', 'mirror', 'interactions', 'triggers', 'held', 'hostcut', 'novella', 'scene19', 'lobby', 'migrate', 'death', 'minigame'), [switch]$WithChapters)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
if ($WithChapters) { $Tours += 'chapters' }
$summary = @()
foreach ($tour in $Tours) {
    $lines = & (Join-Path $PSScriptRoot 'run-tour.ps1') -Tour $tour 2>&1
    $verdict = ($lines | Where-Object { $_ -match "^${tour}: (PASS|FAIL)" } | Select-Object -Last 1)
    $summary += $(if ($verdict) { $verdict } else { "${tour}: NO RESULT" })
    $lines | Where-Object { $_ -match '^\s+- ' } | ForEach-Object { $summary += "    $_" }
}
Write-Output '=== lab suite ==='
$summary | ForEach-Object { Write-Output $_ }
