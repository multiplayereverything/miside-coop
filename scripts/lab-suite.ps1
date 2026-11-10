# Runs every feature tour in a row and prints one line per tour. Results: artifacts/tests/<tour>-results.json.
# Usage: lab-suite.ps1 [-Tours connect,avatars,...] [-WithChapters] [-Quick]
# -Quick: one tour per area the story log touches. Use it while working; the full list before a release.
param([string[]]$Tours = @('connect', 'sessionlog', 'samestart', 'savesguard', 'storylog', 'pressrace', 'blip', 'desync', 'inputhook', 'avatars', 'story', 'mita', 'cutscene', 'world', 'carry', 'dialogue', 'gather', 'room', 'mirror', 'interactions', 'triggers', 'held', 'hostcut', 'novella', 'scene19', 'lobby', 'migrate', 'death', 'minigame', 'steam', 'quest', 'core', 'phone', 'mobile', 'safe', 'choice', 'menureturn', 'scene1', 'scene2', 'menuleave', 'roomhold', 'scene1end', 'scene3', 'scene3full'), [switch]$WithChapters, [switch]$Quick)
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
if ($Quick) { $Tours = @('connect', 'storylog', 'desync', 'phone', 'carry', 'held', 'menureturn', 'scene2') }
if ($WithChapters) { $Tours += 'chapters' }
$suiteStarted = Get-Date
$summary = @()
foreach ($tour in $Tours) {
    $lines = & (Join-Path $PSScriptRoot 'run-tour.ps1') -Tour $tour 2>&1
    $verdict = ($lines | Where-Object { $_ -match "^${tour}: (PASS|FAIL)" } | Select-Object -Last 1)
    $summary += $(if ($verdict) { $verdict } else { "${tour}: NO RESULT" })
    $lines | Where-Object { $_ -match '^\s+- ' } | ForEach-Object { $summary += "    $_" }
}
Write-Output ("=== lab suite: {0} tours in {1:0.0} min ===" -f $Tours.Count, ((Get-Date) - $suiteStarted).TotalMinutes)
$summary | ForEach-Object { Write-Output $_ }
