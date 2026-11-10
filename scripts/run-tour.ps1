# Runs one scripted lab tour with hidden windows and judges it. Results go to artifacts/tests/<tour>-results.json.
# Usage: run-tour.ps1 -Tour avatars [-Players 2] [-Seconds 180] [-Chapter "Scene 3 - WeTogether"]
param([Parameter(Mandatory)][string]$Tour, [int]$Players = 2, [int]$Seconds = 0, [string]$Chapter = '', [switch]$Visible, [string]$ExtraHostArgs = '', [string]$ExtraClientArgs = '', [int]$Lag = 0, [int]$Jitter = 0, [string]$JoinAddress = '127.0.0.1')
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
$tourStarted = Get-Date
$tours = @{
    # sessionlog: the tour itself logs one game error, to prove game errors reach the host.
    'sessionlog' = @{ Seconds = 120; AllowGameErrors = $true; Expect = @('sessionlog.own@host', 'sessionlog.received@host', 'client.sessionlog.file') }
    'samestart' = @{ Seconds = 120; Expect = @('samestart.sent@host', 'client.samestart.flags', 'client.samestart.objects', 'client.samestart.saves', 'client.samestart.photo') }
    'storylog' = @{ Seconds = 200; Expect = @('storylog.sent@host', 'client.storylog.order', 'client.storylog.gated', 'client.storylog.same', 'client.storylog.request') }
    'inputhook' = @{ Seconds = 150; Players = 1; Expect = @('inputhook.control@host', 'inputhook.forced@host') }
    'desync'   = @{ Seconds = 186; Expect = @('chapter.loaded@host', 'client.desync.detected', 'client.desync.repaired') }
    'connect'  = @{ Seconds = 60;  Expect = @('ui.panel', 'connect.handshake', 'connect.roster', 'steam.ready', 'ui.menu', 'ui.menu.open') }
    'core'     = @{ Seconds = 174;  Expect = @('chapter.loaded@host', 'core.driven@host', 'client.core.followed', 'core.answered@host', 'client.core.same') }
    'quest'    = @{ Seconds = 216;  Expect = @('chapter.loaded@host', 'quest.clicked@host', 'client.quest.moved') }
    # scene1: the app's Mita follows the host's steps (name, tidy-up), about 1 s late at a step; normal play is held to 1 s by 'mita'.
    'scene1'   = @{ Seconds = 880; NpcLagLimit = 1.5; Expect = @('end.arrived@host', 'client.end.arrived', 'client.log.clean', 'client.props.inhand', 'client.phone.notmine', 'client.phone.day2', 'client.app.cursor', 'app.cursor@host', 'cook.client@host', 'cook.same@host', 'app.lines@host') }
    'choice'   = @{ Seconds = 154;  AllowGameErrors = $true; Expect = @('choice.picked@host', 'client.choice.applied', 'client.choice.navmesh') }
    'scene1end' = @{ Seconds = 306; NpcLagLimit = 1.5; Expect = @('end.arrived@host', 'client.end.arrived', 'client.log.clean') }
    'scene3'   = @{ Seconds = 326; AllowGameErrors = $true; Expect = @('s3.walk@host', 'client.s3.walk', 'client.s3.navmesh', 'client.s3.choice') }
    'storypilot' = @{ Seconds = 420; Expect = @('pilot.acted@host', 'client.pilot.same') }
    # scene2: the story autopilot through the whole chapter, the client pressing every second object.
    # scene3full: as scene2, all of Scene 3. The other whole-chapter tours (scene1, scene1end) allow 1.5 s too.
    'scene3full' = @{ Seconds = 800; NpcLagLimit = 1.5; LabTour = 'storypilot'; Chapter = 'Scene 3 - WeTogether'; PlayerArgs = '-mt-pilotboth'; Expect = @('pilot.acted@host', 'client.pilot.same', 'client.log.clean') }
    # mitasame: Mita's real body in the same place on both copies, measured while she walks in Scene 3 (screenshots in the lab shots folders).
    'passlevel' = @{ Seconds = 240; Chapter = 'Scene 2 - InGame'; Expect = @('passlevel.arrived@host', 'client.passlevel.arrived', 'passlevel.twice@host', 'client.passlevel.twice') }
    'afk'      = @{ Seconds = 90; Chapter = 'Scene 2 - InGame'; Expect = @('afk.console@host', 'client.afk.console') }
    'door'     = @{ Seconds = 100; Chapter = 'Scene 3 - WeTogether'; Expect = @('door.pushed@host', 'client.door.pushed') }
    'allhere'  = @{ Seconds = 120; Chapter = 'Scene 1 - RealRoom'; Expect = @('allhere.played@host', 'client.allhere.notice', 'client.allhere.brought') }
    'debug'    = @{ Seconds = 150; Chapter = 'Scene 2 - InGame'; Expect = @('debug.rows@host', 'debug.waiting@host', 'client.debug.skip', 'client.debug.refused', 'client.debug.report') }
    'savesguard' = @{ Seconds = 60; Expect = @('savesguard.sent@host', 'client.savesguard.kept') }
    'blip' = @{ Seconds = 130; Chapter = 'Scene 2 - InGame'; Expect = @('blip.kept@host', 'client.blip.back', 'client.blip.stayed', 'client.blip.story') }
    'ownerleaves' = @{ Seconds = 100; Chapter = 'Scene 4 - StartSecret'; ClientLeaves = $true; Expect = @('ownerleaves.watched@host', 'ownerleaves.ended@host') }
    'deathclient' = @{ Seconds = 110; Chapter = 'Scene 11 - Backrooms'; Expect = @('deathclient.restarted@host', 'client.deathclient.died') }
    'labgate' = @{ Seconds = 110; Chapter = 'Scene 1 - RealRoom'; PlayerArgs = '-mt-labnotices-off'; Expect = @('labgate.ignored@host') }
    'novellarace' = @{ Seconds = 110; Chapter = 'Scene 18 - 2D'; PlayerArgs = '-mt-lag 300 0'; Expect = @('novellarace.moved@host', 'novellarace.once@host') }
    'pressrace' = @{ Seconds = 110; Chapter = 'Scene 2 - InGame'; PlayerArgs = '-mt-lag 300 0'; Expect = @('pressrace.hostplays@host', 'client.pressrace.onemain') }
    'scanner'  = @{ Seconds = 150; Chapter = 'Scene 2 - InGame'; Expect = @('scanner.waits@host', 'client.scanner.waits', 'scanner.placed@host', 'client.scanner.placed', 'client.scanner.same') }
    'mitasame' = @{ Seconds = 240; LabTour = 'storypilot'; Chapter = 'Scene 3 - WeTogether'; PlayerArgs = '-mt-pilotboth -mt-mitacheck'; Expect = @('mita.moved@host') }
    # scene4: the game's own Audio_BlendShapeVoice errors after Scene 4 hands over to Scene 5 (jump45 with -mt-nopatch shows them without the mod).
    'scene4'   = @{ Seconds = 1500; AllowGameErrors = $true; LabTour = 'storypilot'; Chapter = 'Scene 4 - StartSecret'; PlayerArgs = '-mt-pilotboth'; Expect = @('pilot.acted@host', 'client.pilot.same', 'client.pilot.keys', 'client.log.clean', 'tv.oneplays@host', 'client.tv.oneplays', 'tv.story@host', 'tv.mirror@host', 'cards.oneplays@host', 'client.cards.oneplays', 'cards.done@host', 'client.hold.same') }
    'scene6'   = @{ Seconds = 420; LabTour = 'storypilot'; Chapter = 'Scene 6 - BasementFirst'; PlayerArgs = '-mt-pilotfull'; Expect = @('pilot.acted@host', 'client.pilot.same', 'client.pilot.keys', 'client.prompt.answered') }
    'scene5'   = @{ Seconds = 1500; LabTour = 'storypilot'; Chapter = 'Scene 5 - StartHorror'; PlayerArgs = '-mt-pilotboth'; Expect = @('pilot.acted@host', 'client.pilot.same', 'client.pilot.keys', 'client.log.clean', 'pilot.pressable@host') }
    'lamp'     = @{ Seconds = 130; Chapter = 'Scene 6 - BasementFirst'; Expect = @('lamp.shared@host', 'lamp.edge@host') }
    'jump45'   = @{ Seconds = 150; Chapter = 'Scene 4 - StartSecret'; Expect = @('jump.clean@host') }
    'distance' = @{ Seconds = 130; Chapter = 'Scene 11 - Backrooms'; PlayerArgs = ''; Expect = @('distance.quiet@host', 'distance.left@host') }
    'scene7' = @{ Seconds = 300; LabTour = 'storypilot'; Chapter = 'Scene 7 - Backrooms'; PlayerArgs = '-mt-pilotboth'; Expect = @('client.pilot.same') }
    'scene8' = @{ Seconds = 300; LabTour = 'storypilot'; Chapter = 'Scene 8 - ReRooms'; PlayerArgs = '-mt-pilotboth'; Expect = @('client.pilot.same') }
    'scene9' = @{ Seconds = 300; LabTour = 'storypilot'; Chapter = 'Scene 9 - ChibiMita'; PlayerArgs = '-mt-pilotboth'; Expect = @('client.pilot.same') }
    'scene10' = @{ Seconds = 300; LabTour = 'storypilot'; Chapter = 'Scene 10 - ManekenWorld'; PlayerArgs = '-mt-pilotboth'; Expect = @('client.pilot.same') }
    'scene12' = @{ Seconds = 300; LabTour = 'storypilot'; Chapter = 'Scene 12 - Freak'; PlayerArgs = '-mt-pilotboth'; Expect = @('client.pilot.same') }
    'scene13' = @{ Seconds = 300; LabTour = 'storypilot'; Chapter = 'Scene 13 - HelloCore'; PlayerArgs = '-mt-pilotboth'; Expect = @('client.pilot.same') }
    'scene16' = @{ Seconds = 300; LabTour = 'storypilot'; Chapter = 'Scene 16 - TheEnd'; PlayerArgs = '-mt-pilotboth'; Expect = @('client.pilot.same') }
    'scene11'  = @{ Seconds = 300; LabTour = 'storypilot'; Chapter = 'Scene 11 - Backrooms'; PlayerArgs = '-mt-pilotboth'; Expect = @('client.pilot.same', 'client.pilot.keys', 'client.log.clean') }
    'scene17'  = @{ Seconds = 600; LabTour = 'storypilot'; Chapter = 'Scene 17 - Dreamer'; PlayerArgs = '-mt-pilotboth'; Expect = @('client.pilot.same', 'client.pilot.keys', 'client.log.clean') }
    'scene14'  = @{ Seconds = 300; LabTour = 'storypilot'; Chapter = 'Scene 14 - MobilePlayer'; PlayerArgs = '-mt-pilotboth'; Expect = @('client.pilot.same') }
    'scene20'  = @{ Seconds = 300; LabTour = 'storypilot'; Chapter = 'Scene 20 - FightMita'; PlayerArgs = '-mt-pilotboth'; Expect = @('client.pilot.same', 'client.death.regroup', 'death.regroup@host', 'client.corridor.same') }
    'doorroom' = @{ Seconds = 90; Chapter = 'Scene 2 - InGame'; Expect = @('doorroom.loaded@host', 'client.doorroom.loaded') }
    'corridor' = @{ Seconds = 90; Chapter = 'Scene 2 - InGame'; Expect = @('corridor.gathered@host', 'client.corridor.gathered') }
    'corridorc' = @{ Seconds = 90; LabTour = 'corridor'; Chapter = 'Scene 2 - InGame'; PlayerArgs = '-mt-corridorclient'; Expect = @('corridor.gathered@host', 'client.corridor.gathered') }
    'trigwalk' = @{ Seconds = 90; Chapter = 'Scene 2 - InGame'; Expect = @('trigwalk.relayed@host') }
    'carrot'   = @{ Seconds = 100; Chapter = 'Scene 3 - WeTogether'; Expect = @('client.carrot.follows') }
    'lockgather' = @{ Seconds = 100; Chapter = 'Scene 4 - StartSecret'; Expect = @('lockgather.together@host', 'client.lockgather.together') }
    'lockgatherc' = @{ Seconds = 100; LabTour = 'lockgather'; Chapter = 'Scene 4 - StartSecret'; PlayerArgs = '-mt-lockclient'; Expect = @('lockgather.together@host', 'client.lockgather.together') }
    'blackscreen' = @{ Seconds = 100; Chapter = 'Scene 3 - WeTogether'; Expect = @('client.blackscreen.moved') }
    'handhold' = @{ Seconds = 110; Chapter = 'Scene 3 - WeTogether'; Expect = @('handhold.led@c1', 'handhold.hand') }
    'mitaevent' = @{ Seconds = 100; Chapter = 'Scene 3 - WeTogether'; Expect = @('mitaevent.ignored@c1', 'mitaevent.relayed@c1') }
    'automate' = @{ Seconds = 120; LightDiffers = $true; Chapter = 'Scene 7 - Backrooms'; Expect = @('automate.watcher@c1', 'automate.free@c1', 'automate.acts') }
    'handhold7' = @{ Seconds = 90; Chapter = 'Scene 7 - Backrooms'; Expect = @('handhold7.client@c1', 'handhold7.host@host') }
    'mitalook' = @{ Seconds = 130; Chapter = 'Scene 4 - StartSecret'; Expect = @('mitalook.you@c1', 'mitalook.host@c1') }
    'loading'  = @{ Seconds = 150; Chapter = 'Scene 4 - StartSecret'; Expect = @('loading.dialogue@host', 'loading.back') }
    'legs'     = @{ Seconds = 100; Chapter = 'Scene 5 - StartHorror'; Expect = @('legs.shared@host') }
    'pickup'   = @{ Seconds = 110; Chapter = 'Scene 6 - BasementFirst'; PlayerArgs = '-mt-nolog'; Expect = @('pickup.gone@host', 'client.pickup.gone') }
    'pickupc'  = @{ Seconds = 110; LabTour = 'pickup'; Chapter = 'Scene 6 - BasementFirst'; PlayerArgs = '-mt-nolog -mt-pickupclient'; Expect = @('pickup.gone@host', 'client.pickup.gone') }
    'scene15'  = @{ Seconds = 300; LabTour = 'storypilot'; Chapter = 'Scene 15 - BasementAndDeath'; PlayerArgs = '-mt-pilotboth'; Expect = @('client.pilot.same', 'client.pilot.keys', 'client.log.clean') }
    # The same chapter with internet-like delay, and with three players.
    'scene2lag' = @{ Seconds = 600; LabTour = 'storypilot'; Chapter = 'Scene 2 - InGame'; PlayerArgs = '-mt-pilotboth -mt-lag 120 40'; Expect = @('pilot.acted@host', 'client.pilot.same', 'client.pilot.keys', 'client.log.clean', 'batteries.room@host', 'client.batteries.room') }
    'scene2x3' = @{ Seconds = 500; Players = 3; LabTour = 'storypilot'; Chapter = 'Scene 2 - InGame'; PlayerArgs = '-mt-pilotboth'; Expect = @('pilot.acted@host', 'client.pilot.same', 'pilot.same@c2', 'client.pilot.keys', 'pilot.keys@c2', 'client.log.clean', 'log.clean@c2', 'batteries.room@host', 'client.batteries.room', 'batteries.room@c2') }
    'scene2'   = @{ Seconds = 500; LabTour = 'storypilot'; Chapter = 'Scene 2 - InGame'; PlayerArgs = '-mt-pilotboth'; Expect = @('pilot.acted@host', 'client.pilot.same', 'client.pilot.keys', 'client.log.clean', 'batteries.room@host', 'client.batteries.room') }
    # roomhold: the client keeps a room the host's story switched off, so its lights differ on purpose.
    'roomhold' = @{ Seconds = 162; LightDiffers = $true; Expect = @('room.off@host', 'client.room.kept') }
    'menuleave' = @{ Seconds = 166; ClientLeaves = $true; Expect = @('leave.seen@host', 'client.leave.left') }
    'menureturn' = @{ Seconds = 166; Expect = @('menu.back@host', 'client.menu.clean') }
    'safe'     = @{ Seconds = 134;  Expect = @('safe.typed@host', 'client.safe.followed') }
    # mobile: Scene 14's "Point Light" flickers by its own random noise on each PC (brightness step 0-2), nothing else differs.
    'mobile'   = @{ Seconds = 182;  LightDiffers = $true; Expect = @('mobile.walked@host', 'client.mobile.seen', 'client.mobile.done') }
    'steam'    = @{ Seconds = 80;   SteamMock = $true; Expect = @('connect.handshake', 'connect.roster', 'steam.ready', 'client.steam.joined', 'client.steam.order') }
    'avatars'  = @{ Seconds = 134;  Expect = @('ui.panel', 'chapter.loaded@host', 'client.follow.scene', 'client.follow.player', 'avatar.model', 'avatar.props', 'avatar.bones', 'avatar.near', 'avatar.anim', 'avatar.params', 'client.avatar.follows') }
    'story'    = @{ Seconds = 120;  Expect = @('chapter.loaded@host', 'story.sent@host', 'client.story.applied', 'story.received@host') }
    'mita'     = @{ Seconds = 142;  Expect = @('chapter.loaded@host', 'mita.sent@host', 'client.mita.synced', 'client.mita.face', 'mita.gaze@host', 'client.teleport.safe') }
    'cutscene' = @{ Seconds = 134;  Expect = @('chapter.loaded@host', 'cutscene.sent@host', 'client.cutscene.started', 'client.cutscene.synced', 'cutscene.visibility') }
    'world'    = @{ Seconds = 122;  PlayerArgs = '-mt-nolog'; Expect = @('chapter.loaded@host', 'items.hand.sent@host', 'items.key.sent@host', 'client.items.key', 'client.seed.match') }
    'props'    = @{ Seconds = 200;  Expect = @('chapter.loaded@host', 'props.sent@host', 'client.props.synced') }
    'minigame' = @{ Seconds = 166;  Expect = @('chapter.loaded@host', 'minigame.started@host', 'minigame.owned@host', 'client.minigame.watch', 'client.minigame.end') }
    'carry'    = @{ Seconds = 240;  Expect = @('chapter.loaded@host', 'carry.acted@host', 'client.carry.still', 'client.carry.finished', 'carry.sent@host', 'client.carry.mirrored', 'client.carry.released') }
    'dialogue' = @{ Seconds = 122;  PlayerArgs = '-mt-nolog'; Expect = @('chapter.loaded@host', 'dialogue.sent@host', 'client.dialogue.started', 'client.dialogue.lag') }
    'gather'   = @{ Seconds = 154;  CutsceneDiffers = $true; Expect = @('chapter.loaded@host', 'gather.cutscene@host', 'client.gather.done', 'client.teleport.safe') }
    'room'     = @{ Seconds = 134;  Expect = @('chapter.loaded@host', 'room.changed@host', 'client.room.active', 'client.room.place', 'client.room.light') }
    'mirror'   = @{ Seconds = 122;  PlayerArgs = '-mt-nolog'; Expect = @('chapter.loaded@host', 'mirror.acted@host', 'client.mirror.played', 'client.mirror.placed', 'client.mirror.end') }
    'interactions' = @{ Seconds = 164; Expect = @('interactions.pressed@host', 'client.interactions.mirrored', 'client.interactions.placed', 'client.interactions.free') }
    'triggers' = @{ Seconds = 186;  Expect = @('triggers.distance@host', 'triggers.hold@host', 'client.triggers.distance', 'client.triggers.hold') }
    'scene19'  = @{ Seconds = 206;  AllowGameErrors = $true; Expect = @('scene19.followed@host', 'client.scene19.followed') }
    'hostcut'  = @{ Seconds = 146;  Expect = @('client.hostcut.requested', 'hostcut.ran@host', 'client.hostcut.played') }
    'held'     = @{ Seconds = 166;  Expect = @('held.plane@host', 'held.door@host') }
    'phone'    = @{ Seconds = 178;  Expect = @('phone.taken@host', 'phone.held@host', 'client.phone.still', 'client.phone.seen', 'client.phone.gone') }
    'novella'  = @{ Seconds = 162;  Expect = @('novella.read@host', 'client.novella.synced', 'novella.clientclick@host') }
    'lobby'    = @{ Seconds = 238;  Players = 4; Expect = @('lobby.held@host', 'lobby.waited@c3', 'lobby.together@host', 'lobby.arrived@c1', 'lobby.arrived@c2', 'lobby.arrived@c3') }
    'migrate'  = @{ Seconds = 142;  Players = 3; Expect = @('migrate.hostleft@host', 'migrate.host@c1', 'migrate.joined@c2', 'migrate.scene@c1', 'migrate.scene@c2') }
    'death'    = @{ Seconds = 144;  Expect = @('chapter.loaded@host', 'death.sent@host', 'client.death.restart') }
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
$labChapter = if ($Chapter) { $Chapter } else { $spec.Chapter }
if ($labChapter) { $hostArgs += ' -mt-chapter ' + ($labChapter -replace ' ', '_') }
if ($spec.PlayerArgs) { $hostArgs += ' ' + $spec.PlayerArgs; $ExtraClientArgs = "$($spec.PlayerArgs) $ExtraClientArgs" }
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
& (Join-Path $PSScriptRoot 'launch-lab.ps1') -Players $Players -HostArgs "$hostArgs $lagArgs" -ClientArgs "$lagArgs $ExtraClientArgs" -JoinAddress $JoinAddress -Tour $(if ($spec.LabTour) { $spec.LabTour } else { $Tour }) -Visible:$Visible -SteamMock:([bool]$spec.SteamMock)

# Every tour stops as soon as all its expected checks have passed (or one failed), or at its time limit.
$deadline = (Get-Date).AddSeconds($Seconds)
$probe = Join-Path $labRoot 'tour-probe.json'
Remove-Item $probe -ErrorAction SilentlyContinue
$labPids = Get-Content (Join-Path $labRoot 'lab-pids.txt') | ForEach-Object { [int]$_ }
$probeArgs = @((Join-Path $modRoot 'tests/verify-lab.py'), $modRoot, '--out', $probe)
if ($spec.AllowGameErrors) { $probeArgs += '--allow-game-errors' }
if ($spec.NpcLagLimit) { $probeArgs += @('--npc-lag-limit', [string]$spec.NpcLagLimit) }
if ($spec.ClientLeaves) { $probeArgs += '--client-leaves' }
foreach ($e in $spec.Expect) { $probeArgs += @('--expect', $e) }
do {
    Start-Sleep 1
    python -I @probeArgs | Out-Null
    $probeResult = Get-Content $probe -Raw | ConvertFrom-Json
    $waiting = @($probeResult.failures | Where-Object { $_ -match 'not seen|no client passed|no lab logs' }).Count
    $failedCheck = @($probeResult.failures | Where-Object { $_ -match 'check .* failed' }).Count
    # A copy that has gone can't finish the tour: don't wait out the time limit. (ClientLeaves tours and migrate can end a copy on purpose.)
    $gone = @($labPids | Where-Object { -not (Get-Process -Id $_ -ErrorAction SilentlyContinue) })
    # The user started their own game: the lab shares its save folder, so stop at once and put the saves back.
    if (Get-Process MiSideFull -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -notlike "$labRoot*" }) { Write-Output "the user's own game started: stopping the lab"; $userGame = $true; break }
    if ($gone.Count -gt 0 -and -not $spec.ClientLeaves -and $Tour -ne 'migrate') { Write-Output "lab copy gone early (PID $($gone -join ', '))"; break }
    # A copy can hang on the logo screen (seen twice, cause unknown): that run has nothing to tell, and its time limit is minutes away.
    $copies = @($probeResult.instances.PSObject.Properties | ForEach-Object { $_.Value })
    if (((Get-Date) - $tourStarted).TotalSeconds -gt 60 -and ($copies.Count -lt $Players -or @($copies | Where-Object { $_.scene_end -eq 'SceneAihasto' }).Count -gt 0)) { Write-Output "lab copy stuck at the start: stopping"; break }
} while (($waiting -gt 0) -and ($failedCheck -eq 0) -and ((Get-Date) -lt $deadline))
# A moment more so error samples cover the end of the tour, and longer only while a client is still following the host.
if (-not $userGame) { Start-Sleep 3 }
for ($try = 0; $try -lt 10 -and -not $userGame; $try++) {
    python -I @probeArgs | Out-Null
    if (-not ((Get-Content $probe -Raw | ConvertFrom-Json).failures -match 'ended in')) { break }
    Start-Sleep 1
}
$pids = Get-Content (Join-Path $labRoot 'lab-pids.txt') | ForEach-Object { [int]$_ }
foreach ($id in $pids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
foreach ($id in $pids) { Wait-Process -Id $id -Timeout 15 -ErrorAction SilentlyContinue }
Write-Output 'stopped lab processes'
New-Item -ItemType Directory -Force $saveDir | Out-Null
# A file a closing game still holds must not stop the restore (2026-11-09: it aborted here and left the saves half gone).
Get-ChildItem $saveDir -File | Remove-Item -Force -ErrorAction SilentlyContinue
$restored = $false
for ($try = 0; $try -lt 15 -and -not $restored; $try++) {
    if ($try -gt 0) { Start-Sleep 1 }
    try { Copy-Item (Join-Path $saveBackup '*') $saveDir -Force -ErrorAction SilentlyContinue } catch { }
    $restored = -not (Get-ChildItem $saveBackup -File | Where-Object { $copy = Get-Item -LiteralPath (Join-Path $saveDir $_.Name) -ErrorAction SilentlyContinue; -not $copy -or $copy.Length -ne $_.Length })
}
if (-not $restored) { throw "SAVES NOT RESTORED: copy them back from $saveBackup" }
Write-Output 'restored the user''s saves'

$name = if ($Chapter) { "$Tour-" + ($Chapter -replace '[^A-Za-z0-9]+', '_') } else { $Tour }
$out = Join-Path $modRoot "artifacts/tests/$name-results.json"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
# Old results must not stand in for this run if the verifier fails.
Remove-Item $out -ErrorAction SilentlyContinue
$verify = @((Join-Path $modRoot 'tests/verify-lab.py'), $modRoot, '--out', $out)
if ($spec.AllowGameErrors) { $verify += '--allow-game-errors' }
if ($spec.NpcLagLimit) { $verify += @('--npc-lag-limit', [string]$spec.NpcLagLimit) }
if ($spec.ClientLeaves) { $verify += '--client-leaves' }
if ($spec.CutsceneDiffers) { $verify += '--cutscene-differs' }
if ($spec.LightDiffers) { $verify += '--light-differs' }
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
Write-Output ("{0}: {1} in {2:0} s" -f $name, $(if ($result.pass) { 'PASS' } else { 'FAIL' }), ((Get-Date) - $tourStarted).TotalSeconds)
foreach ($f in $result.failures) { Write-Output "  - $f" }
foreach ($p in $result.instances.PSObject.Properties) {
    $i = $p.Value
    $passed = @($i.checks | Where-Object pass).Count; $total = @($i.checks).Count
    Write-Output ("  {0}: checks {1}/{2}, scene {3}, errors {4} (slope {5}/10s), heap slope {6} MB/10s, mod ms median {7} max {8} (worst frame {11}), mod objects {9}, mita desync max {10}" -f $p.Name, $passed, $total, $i.scene_end, $i.errors_end, $i.error_slope_per_10s, $i.heap_slope_mb_per_10s, $i.modms_median, $i.modms_max, $i.modobjects_end, $i.mitadesync_max, $i.modmax_frame_ms)
    if ($i.sync) { $s = $i.sync; Write-Output ("     sync vs host: {0} samples, npc p90 {1} m, npc anim mismatch {2}, dialogue mismatch {3}, dialogue lag max/mean {4}, cutscene mismatch {5}, timeline p90 {6} s, npc anim start p90 {7} s over {8} changes (missed {9}, early {10})" -f $s.samples, $s.npc_m_p90, $s.npc_anim_mismatch, $s.dialogue_mismatch, $s.dialogue_lag_max_mean, $s.cutscene_mismatch, $s.timeline_s_p90, $s.npc_anim_start_s_p90, $s.npc_anim_changes, $s.npc_anim_missed, $s.npc_anim_early) }
}
Remove-Item $lock -Force -ErrorAction SilentlyContinue
