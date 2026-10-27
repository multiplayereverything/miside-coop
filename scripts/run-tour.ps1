# Runs one scripted lab tour with hidden windows and judges it. Results go to artifacts/tests/<tour>-results.json.
# Usage: run-tour.ps1 -Tour avatars [-Players 2] [-Seconds 180] [-Chapter "Scene 3 - WeTogether"]
param([Parameter(Mandatory)][string]$Tour, [int]$Players = 2, [int]$Seconds = 0, [string]$Chapter = '', [switch]$Visible, [string]$ExtraHostArgs = '', [string]$ExtraClientArgs = '', [int]$Lag = 0, [int]$Jitter = 0, [string]$JoinAddress = '127.0.0.1')
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
$tours = @{
    'connect'  = @{ Seconds = 60;   Expect = @('ui.panel', 'connect.handshake', 'connect.roster', 'steam.ready', 'ui.menu', 'ui.menu.open') }
    'core'     = @{ Seconds = 300;  Expect = @('chapter.loaded@host', 'core.driven@host', 'client.core.followed') }
    'quest'    = @{ Seconds = 360;  Expect = @('chapter.loaded@host', 'quest.clicked@host', 'client.quest.moved') }
    'mobile'   = @{ Seconds = 300;  Expect = @('mobile.walked@host', 'client.mobile.seen', 'client.mobile.done') }
    'steam'    = @{ Seconds = 80;   SteamMock = $true; Expect = @('connect.handshake', 'connect.roster', 'steam.ready', 'client.steam.joined', 'client.steam.order') }
    'avatars'  = @{ Seconds = 200;  Expect = @('ui.panel', 'chapter.loaded@host', 'client.follow.scene', 'client.follow.player', 'avatar.model', 'avatar.props', 'avatar.bones', 'avatar.near', 'avatar.anim', 'avatar.params', 'client.avatar.follows') }
    'story'    = @{ Seconds = 200;  Expect = @('chapter.loaded@host', 'story.sent@host', 'client.story.applied', 'story.received@host') }
    'mita'     = @{ Seconds = 240;  Expect = @('chapter.loaded@host', 'mita.sent@host', 'client.mita.synced', 'client.mita.face', 'mita.gaze@host', 'client.teleport.safe') }
    'cutscene' = @{ Seconds = 240;  Expect = @('chapter.loaded@host', 'cutscene.sent@host', 'client.cutscene.started', 'client.cutscene.synced', 'cutscene.visibility') }
    'world'    = @{ Seconds = 220;  Expect = @('chapter.loaded@host', 'items.hand.sent@host', 'items.key.sent@host', 'client.items.key', 'client.seed.match') }
    'props'    = @{ Seconds = 200;  Expect = @('chapter.loaded@host', 'props.sent@host', 'client.props.synced') }
    'minigame' = @{ Seconds = 220;  Expect = @('chapter.loaded@host', 'minigame.started@host', 'minigame.owned@host', 'client.minigame.watch', 'client.minigame.end') }
    'carry'    = @{ Seconds = 240;  Expect = @('chapter.loaded@host', 'carry.acted@host', 'client.carry.still', 'client.carry.finished', 'carry.sent@host', 'client.carry.mirrored', 'client.carry.released') }
    'dialogue' = @{ Seconds = 200;  Expect = @('chapter.loaded@host', 'dialogue.sent@host', 'client.dialogue.started', 'client.dialogue.lag') }
    'gather'   = @{ Seconds = 200;  Expect = @('chapter.loaded@host', 'gather.cutscene@host', 'client.gather.done', 'client.teleport.safe') }
    'room'     = @{ Seconds = 200;  Expect = @('chapter.loaded@host', 'room.changed@host', 'client.room.active', 'client.room.place', 'client.room.light') }
    'mirror'   = @{ Seconds = 420;  Expect = @('chapter.loaded@host', 'mirror.acted@host', 'client.mirror.played', 'client.mirror.placed', 'client.mirror.end') }
    'interactions' = @{ Seconds = 480; Expect = @('interactions.pressed@host', 'client.interactions.mirrored', 'client.interactions.placed', 'client.interactions.free') }
    'triggers' = @{ Seconds = 300;  Expect = @('triggers.distance@host', 'triggers.hold@host', 'client.triggers.distance', 'client.triggers.hold') }
    'scene19'  = @{ Seconds = 420;  AllowGameErrors = $true; Expect = @('scene19.followed@host', 'client.scene19.followed') }
    'hostcut'  = @{ Seconds = 200;  Expect = @('client.hostcut.requested', 'hostcut.ran@host', 'client.hostcut.played') }
    'held'     = @{ Seconds = 200;  Expect = @('held.plane@host', 'held.door@host') }
    'phone'    = @{ Seconds = 480;  Expect = @('phone.taken@host', 'phone.held@host', 'client.phone.still', 'client.phone.seen', 'client.phone.gone') }
    'novella'  = @{ Seconds = 240;  Expect = @('novella.read@host', 'client.novella.synced', 'novella.clientclick@host') }
    'lobby'    = @{ Seconds = 300;  Players = 4; Expect = @('lobby.held@host', 'lobby.waited@c3', 'lobby.together@host', 'lobby.arrived@c1', 'lobby.arrived@c2', 'lobby.arrived@c3') }
    'migrate'  = @{ Seconds = 240;  Players = 3; Expect = @('migrate.hostleft@host', 'migrate.host@c1', 'migrate.joined@c2', 'migrate.scene@c1', 'migrate.scene@c2') }
    'death'    = @{ Seconds = 220;  Expect = @('chapter.loaded@host', 'death.sent@host', 'client.death.restart') }
    'inventory' = @{ Seconds = 1200; Players = 1; Expect = @('inventory.done@host') }
    'survey'   = @{ Seconds = 900;  Expect = @('survey.done@host') }
    'chapters' = @{ Seconds = 2400; Expect = @('chapters.done@host', 'client.teleport.safe') }
}
if (!$tours.ContainsKey($Tour)) { throw "Unknown tour $Tour (known: $($tours.Keys -join ', '))" }
$spec = $tours[$Tour]
if ($Seconds -le 0) { $Seconds = $spec.Seconds }
$labRoot = Join-Path $modRoot 'private'
# One tour at a time: two runs would stop each other's lab processes through the shared PID file.
$lock = Join-Path $labRoot 'tour.lock'
if (Test-Path $lock) { $other = [int](Get-Content $lock -Raw); if (Get-Process -Id $other -ErrorAction SilentlyContinue) { throw "Another lab tour is running (PID $other)." } }
Set-Content $lock $PID -NoNewline
# The lab backs up and restores the shared save folder, so it must never run beside the user's own game.
# The second copy (scripts\second-instance.ps1, private\lab-second) is the user's own game too.
$userGame = Get-CimInstance Win32_Process -Filter "Name='MiSideFull.exe'" | Where-Object { $_.ExecutablePath -and (-not $_.ExecutablePath.StartsWith($labRoot, [StringComparison]::OrdinalIgnoreCase) -or $_.ExecutablePath -like '*\lab-second\*') }
if ($userGame) { throw "MiSide is running (PID $($userGame.ProcessId)); close it before running lab tours." }
# Only copies under private\ (lab-owned, never the real game) are touched.
$busy = Get-CimInstance Win32_Process -Filter "Name='MiSideFull.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($labRoot, [StringComparison]::OrdinalIgnoreCase) }
$busy | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue; Wait-Process -Id $_.ProcessId -Timeout 15 -ErrorAction SilentlyContinue }
# A copy that was just stopped can hold its log for a few seconds more: retry before giving up.
foreach ($log in Get-ChildItem $labRoot -Filter 'misidetogether-lab.log' -Recurse -Depth 1 -ErrorAction SilentlyContinue) {
    for ($try = 0; ; $try++) {
        try { Clear-Content $log.FullName -ErrorAction Stop; break }
        catch { if ($try -ge 15) { throw "Lab log still in use after 15 s: $($log.FullName)" }; Start-Sleep 1 }
    }
}
$hostArgs = ''
if ($Chapter) { $hostArgs += ' -mt-chapter ' + ($Chapter -replace ' ', '_') }
if ($ExtraHostArgs) { $hostArgs += ' ' + $ExtraHostArgs }
# Lab copies share the user's MiSide save folder: back it up for the run and put it back afterwards.
$saveDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\AIHASTO\MiSideFull\Save'
$saveBackup = Join-Path $labRoot 'save-backup'
if (Test-Path $saveBackup) { Remove-Item $saveBackup -Recurse -Force }
New-Item -ItemType Directory -Force $saveBackup | Out-Null
if (Test-Path $saveDir) { Copy-Item (Join-Path $saveDir '*') $saveBackup -Force -ErrorAction SilentlyContinue }
# A timestamped copy too, never overwritten by later runs (the last 30 kept), so a lost save can always be found.
if ((Test-Path $saveDir) -and (Get-ChildItem $saveDir -File -ErrorAction SilentlyContinue)) {
    $history = Join-Path $labRoot 'save-history'
    $stamp = Join-Path $history (Get-Date -Format 'yyyyMMdd-HHmmss')
    New-Item -ItemType Directory -Force $stamp | Out-Null
    Copy-Item (Join-Path $saveDir '*') $stamp -Force -ErrorAction SilentlyContinue
    Get-ChildItem $history -Directory | Sort-Object Name -Descending | Select-Object -Skip 30 | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
if ($spec.Players) { $Players = $spec.Players }
$lagArgs = if ($Lag -gt 0) { "-mt-lag $Lag $Jitter" } else { '' }
& (Join-Path $PSScriptRoot 'launch-lab.ps1') -Players $Players -HostArgs "$hostArgs $lagArgs" -ClientArgs "$lagArgs $ExtraClientArgs" -JoinAddress $JoinAddress -Tour $Tour -Visible:$Visible -SteamMock:([bool]$spec.SteamMock)

# Every tour stops as soon as all its expected checks have passed (or one failed), or at its time limit.
$deadline = (Get-Date).AddSeconds($Seconds)
$probe = Join-Path $labRoot 'tour-probe.json'
$probeArgs = @((Join-Path $modRoot 'tests/verify-lab.py'), $modRoot, '--out', $probe)
if ($spec.AllowGameErrors) { $probeArgs += '--allow-game-errors' }
foreach ($e in $spec.Expect) { $probeArgs += @('--expect', $e) }
do {
    Start-Sleep 5
    python -I @probeArgs | Out-Null
    $probeResult = Get-Content $probe -Raw | ConvertFrom-Json
    $waiting = @($probeResult.failures | Where-Object { $_ -match 'not seen|no client passed|no lab logs' }).Count
    $failedCheck = @($probeResult.failures | Where-Object { $_ -match 'check .* failed' }).Count
} while (($waiting -gt 0) -and ($failedCheck -eq 0) -and ((Get-Date) -lt $deadline))
# A few more seconds so error and memory samples cover the end of the tour.
Start-Sleep 10
$pids = Get-Content (Join-Path $labRoot 'lab-pids.txt') | ForEach-Object { [int]$_ }
foreach ($id in $pids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
foreach ($id in $pids) { Wait-Process -Id $id -Timeout 15 -ErrorAction SilentlyContinue }
Write-Output 'stopped lab processes'
New-Item -ItemType Directory -Force $saveDir | Out-Null
Get-ChildItem $saveDir -File | Remove-Item -Force
Copy-Item (Join-Path $saveBackup '*') $saveDir -Force -ErrorAction SilentlyContinue
Write-Output 'restored the user''s saves'

$name = if ($Chapter) { "$Tour-" + ($Chapter -replace '[^A-Za-z0-9]+', '_') } else { $Tour }
$out = Join-Path $modRoot "artifacts/tests/$name-results.json"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$verify = @((Join-Path $modRoot 'tests/verify-lab.py'), $modRoot, '--out', $out)
if ($spec.AllowGameErrors) { $verify += '--allow-game-errors' }
# Other games running beside the lab (another mod's lab copies) slow every copy down: frame cost is then not the mod's.
$busyGames = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.WorkingSet64 -gt 300MB -and $_.Path -and $_.Path -notlike "$labRoot*" -and ($_.Path -like '*steamapps*' -or $_.ProcessName -match 'Unity|BALDI') })
if ($busyGames.Count -gt 0) { $verify += @('--busy', (($busyGames | ForEach-Object ProcessName | Sort-Object -Unique) -join ',')) }
foreach ($e in $spec.Expect) { $verify += @('--expect', $e) }
python -I @verify | Out-Null
# Keep each tour's per-instance evidence; the next tour reuses the lab folders.
$keep = Join-Path $modRoot "artifacts/tests/logs/$name"
New-Item -ItemType Directory -Force $keep | Out-Null
foreach ($lab in 'lab-host', 'lab-client', 'lab-c2', 'lab-c3') {
    $src = Join-Path $labRoot "$lab/misidetogether-lab.log"
    if (Test-Path $src) { Copy-Item -Force $src (Join-Path $keep "$lab.log") }
}
# The Unity player logs too (game exceptions), only the error lines, so they survive the next tour.
foreach ($u in Get-ChildItem (Join-Path $labRoot 'lab-logs') -Filter '*.log' -ErrorAction SilentlyContinue) {
    $errs = Select-String -Path $u.FullName -Pattern 'Exception|Error' -Context 0,3 -ErrorAction SilentlyContinue | Select-Object -First 200
    if ($errs) { $errs | ForEach-Object { $_.Line; $_.Context.PostContext } | Set-Content (Join-Path $keep ("unity-" + $u.BaseName + "-errors.txt")) }
}
$result = Get-Content $out -Raw | ConvertFrom-Json
Write-Output ("{0}: {1}" -f $name, $(if ($result.pass) { 'PASS' } else { 'FAIL' }))
foreach ($f in $result.failures) { Write-Output "  - $f" }
foreach ($p in $result.instances.PSObject.Properties) {
    $i = $p.Value
    $passed = @($i.checks | Where-Object pass).Count; $total = @($i.checks).Count
    Write-Output ("  {0}: checks {1}/{2}, scene {3}, errors {4} (slope {5}/10s), heap slope {6} MB/10s, mod ms median {7} max {8} (worst frame {11}), mod objects {9}, mita desync max {10}" -f $p.Name, $passed, $total, $i.scene_end, $i.errors_end, $i.error_slope_per_10s, $i.heap_slope_mb_per_10s, $i.modms_median, $i.modms_max, $i.modobjects_end, $i.mitadesync_max, $i.modmax_frame_ms)
    if ($i.sync) { $s = $i.sync; Write-Output ("     sync vs host: {0} samples, npc p90 {1} m, npc anim mismatch {2}, dialogue mismatch {3}, dialogue lag max/mean {4}, cutscene mismatch {5}, timeline p90 {6} s, npc anim start p90 {7} s over {8} changes (missed {9}, early {10})" -f $s.samples, $s.npc_m_p90, $s.npc_anim_mismatch, $s.dialogue_mismatch, $s.dialogue_lag_max_mean, $s.cutscene_mismatch, $s.timeline_s_p90, $s.npc_anim_start_s_p90, $s.npc_anim_changes, $s.npc_anim_missed, $s.npc_anim_early) }
}
Remove-Item $lock -Force -ErrorAction SilentlyContinue
