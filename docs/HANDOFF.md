# MiSide Together - handoff

Read this first when picking the work up on another PC. Then `AGENTS.md` (architecture, hooks, rules), `README.md` (behaviour, limits), `docs/SYNC.md` (what syncs, how it's proven, the work list) and `MODLOG.md` (history).

## Goal
Co-op for MiSide (2 to 4 players). Everyone sees the others as the main character with real animations, and the game stays in sync: chapters, story, dialogue, cutscenes, Mita and the other NPCs, room state, minigames and endings. BepInEx, to match the user's Baldi Together and Spooky Together mods.

## State (2026-11-07)
- **Version:** 0.3.17 (protocol 13), published as a pre-release (2026-11-09): basement safe, taken items on every PC, Scenes 5 and 15 on the story log, distance triggers, Scene 17 trigger, Scene 14 light (MODLOG). Friends need 0.3.17. Open: Scene 5 key, Scenes 7, 10, 13 (MODLOG). Before it, 0.3.16: F5 debug page can pass a level (host), lamp box, cursor and phone-in-hand fixes. Before it, 0.3.15 (2026-11-08): Scene 4 on the story log, shared cooking board, Mita and phone fixes (MODLOG). Before it, 0.3.14: stable chapter keys, Scene 2 batteries, walk-and-place scanner, F5 debug page. 0.3.12's scanner fix didn't work in real play.
- **Chapter sweep (2026-11-08, 0.3.15, protocol 12):** Scene 4 is on the story log and passes whole (`scene4`, 25 min budget, about 9 min): the console fight and the card table run on the Main Character's PC (live minigames), their result lines and end reach everyone, watchers see the TV and the table (`Op.Screen`), Mita follows the card player, the scarf hold is shared. Every check has a red run (`-mt-nolive`, `-mt-nohold`). The pilot now takes a picture on every PC at each comparison. Found in the game itself: Scene 4 to 5 floods `Audio_BlendShapeVoice` errors (`jump45` with `-mt-nopatch`). Next: Scene 5 (its lighting differed on the client in a cold run), then 6 onward.
- **Scene 1 phone app (2026-11-08, 0.3.15):** cooking board shared by all players (`cook.client`, `cook.same`, mutation red), Mita no longer goes missing on the other player's screen (`app.lines`, red then green). The phone that flew away (both PCs placing a shared object on each other's body) is fixed (`props.inhand`, `scene2`). Regression 2026-11-08 with the phone fix: scene1, scene2, scene4, triggers, world, carry, phone pass. lamp, storylog, story, minigame, mitasame passed just before the phone fix and weren't rerun; scene3full's reruns were cut off by the user's stops, so it has no result on this code (rerun all six first). scene5 (trial, `-mt-logalso`) still fails only on lighting (13 to 16%), so Scene 5 is not on the log list yet. Released on the user's word before these finished.
- **After 0.3.15 (2026-11-08, local, not released):** a three-player session reported three bugs. Cursor lost after the phone minigames: fixed (`app.cursor`). Phone left hanging in front of the body from day 2: fixed (`phone.day2`), not yet seen by a person. Room not syncing after Scene 2's corridor: **not reproduced**, `MT-STATE` now records `rooms=` for the next session. Open: Scene 3 `CapsulePlace Mita` stays on on clients only (MODLOG).
- **After 0.3.16 (2026-11-09, local, not released):** Scenes 5 and 15 are on the story log. Scenes 6 to 20 had a pilot sweep, off and on the log (MODLOG): shallow, because the pilot stops at minigames. **Next:** Scene 11's `DistanceTrigger` flood, Scene 17's lost entry, Scene 20's lighting, Scene 14's thrown event; then teach the pilot those minigames so it gets deeper.
- **Study without the game (2026-11-09, local, nothing run):** Scene 5's lighting gap is mostly each player's own flashlight, which the lab's `LightPrint` now ignores. The rest is a real bug: the lamp box in `Lamps.cs` is twice the game's. Done in 0.3.16: `lamp.edge` red then green, `scene5` lighting passes. **Next:** try Scene 5 on the log list, then a check that other players see the flashlight in the holder's hand (`/ItemFlashLight` is not found on clients). Details in MODLOG.
- **Plan the user approved (2026-11-08):** 1 stable keys (done), 2 input audit (done, `docs/INPUT-AUDIT.md`), 3 pilot presses like a player (done), 4 Scene 2 batteries (done; watch the double battery add), 5 debug page, auto-repair visibility, bug report, refusal notice (done), 6 chapter-by-chapter objective sweep with the pilot (next; Scenes 1-3 first, then add 4+ to the story log one at a time), 7 instant click feedback, walking tests under lag, teleport before important cutscenes. 0.3.13: a player's real press of an `ObjectInteractive` now reaches the mod at all (it never did: `Update` runs its own copy of `Click`), plus 0.3.12's scanner auto-place. Real-play effect beyond Scene 2 not tried by people yet. Friends need the same version. The last full release is v0.3.2. The full suite was not run on 0.3.11: the user stopped it to test the build themselves.
- **Online play:** Steam P2P (friends join from MULTIPLAYER in the game's menu, or Steam's Join Game / invite, `+mt_join`), LAN/VPN, or a direct address (TCP 27030). It doesn't use the Multiplayer Everything relay.
- **This PC (2026-10-27):** the repo lives at `Desktop\AIStuff\ai-work-pc-games\Miside Multiplayer Mod`, outside the game; `scripts\game-dir.ps1` finds the game. BepInEx be.790 and the mod are installed in the game. The user's save folder is empty, as they want it.
- **Website:** `games.json` in the website repo has the `miside` entry. The Website session updates it; don't edit it from here.
- **Tests (lab copies on one PC, minimized):** suite 34 tours on 0.3.5 (2026-10-28; new: `safe`, `choice`, `menureturn`, `menuleave`, `roomhold`, `scene1` (all of Scene 1), `scene1end`, `scene3` (Scene 3 to Mita's walk), and `core` now types the right version and compares both screens): last full run 33 of 34, `scene19` failed when the client stuck on the first splash screen and passed on its own rerun. Pressing every interaction in every chapter: 18 of 18 chapters pass.
- **Played by people:** the 2026-10-28 playtest had three players (the user and two friends) on separate PCs and Steam accounts, joined over Steam; their 19 reports became 0.3.5.
- **Full suite on 0.3.6:** not run; its one code change passed its tours on 2026-10-28, and the lab could not start beside the user's own MiSide on release day. Run `scripts\lab-suite.ps1` next.
- **Played on 0.3.6 (2026-11-06, about 06:36-06:39 local):** the user hosted, a friend ("lidar") joined, both reached Scene 1. The user reports "a lot of bugs" but has not listed them yet; one screenshot so far. Host logs and the screenshot are in `private\playtests\2026-11-06\`. The friend's log was not collected.
- **Not tested:** four real players (4-player runs are lab-only); the other player's shading confirmed by a person.
- **Co-op Anything:** the hub's MiSide entry and write-up describe v0.3.5 (cranebeaker/coop-anything#264, merged by the maintainer by hand on 2026-10-31, so GitHub shows it closed). No hub update for 0.3.6.

## Pick up here (2026-11-07)
0.3.11 (protocol 10) is committed, pushed and published as a pre-release (2026-11-07). The user is testing it themselves; real players haven't tried it yet.

The user played the opening with two copies on this PC (logs: `private\playtests\2026-11-07\*session-20261107-093638*`) and gave five priority fixes, for the beginning of the game. They asked for GMTogether principles, proper tests, screenshots where useful, and tests that aren't long. Rules: test first (`AGENTS.md`), run only the tours a change can affect.

| # | Report | Cause | State |
| --- | --- | --- | --- |
| 1 | Mita in different places | The sync moved her empty parent (`MitaPerson`), not the child that walks. Her key changed when the story moved her, so clients froze. A story look (`Mita Stand`) ran on the host only | Fixed in the lab: `mitasame`, 0.09-0.14 m while she moves (was 2.75 m) |
| 2 | Doors glitch, player walks through | Physics doors with no owner: an angle written onto a live hinge was thrown away, and a shut door's component is switched off, so its last angle wasn't sent | Fixed in the lab: `door`, 2.7-2.9 degrees apart. The test pushes with force; a real player's walk hasn't been tried |
| 3 | Lighting changes | Doors hide rooms, and their lights, on the far side of each PC's own player (`ObjectDoor.DoorClosed`). With players on both sides each PC hid the other's room | Fixed in the lab: in a session doors set `offOptimization` and keep both sides on. `door` with players across it: 72% to 0%. Whole of Scene 3: 20% to 0% |
| 4 | Game console opens by itself | The game's AFK easter egg (`GameController.timeAFK`) | Fixed in the lab: `afk`. Timer held in a session unless the chapter asks for the console (`needTetris`) |
| 5 | "All players must be here" notice, for important cutscenes | New | Done for the notice: every story timeline cutscene (`Playable_Animation`, `Op.CutscenePlay`) tells every PC. `allhere` passes. Not built: teleporting people first. The cutscenes found so far put every player in place themselves, so there's no failing case to test; the gather after a cutscene still brings stragglers to the host |

Also found and fixed on the way: a story look on `/Quests` or `/Acts` in a log chapter now goes through the log; a dialogue choice could stall the pilot while seated; old results could pass for a crashed verifier.

**Open:**
- `scene1` lost one phone-app event (`ButtonCloseMiniGame`) in one run of three, with the button already switched off on the client. The same family: rapid repeat presses in the app made a client wait 12 s per press. A real host pressing app buttons quickly could leave a client behind.
- `BakesEnable` (the panel's lightmaps) isn't measured by the lighting check: `LightmapSettings.lightmaps` throws under IL2CPP.
- Whole house always on in a session: costs some frame time, not measured on a slower PC.
- Not tried by real players. Next: a two-copy session with the user, then the remake plan (Scene 4).

Tours for these: `mitasame`, `door`, `afk`, `allhere` (each 1-2 minutes). The verifier also checks lighting (`light=` in MT-STATE, `MT-LIGHTS` on change), NPC bodies at 5 samples a second (`MT-BODY`), door angles (`MT-DOOR`) and line starts (`MT-LINE`).

Don't launch anything while the user plays on another PC: every copy shows on Steam as MiSide on their account, and starts Steam if it's closed (`MiSideFull.exe` has Steam's copy protection).

## Next: the remake (approved 2026-11-06)
The plan, its reasons and the status of each step are in [REMAKE-PLAN.md](REMAKE-PLAN.md). Start there.

## Done in 0.3.3
- **Lab on a fresh install:** the game's first-run LANGUAGE/VOICE picker is answered (English); lab copies start minimized as normal small windows ("MiSide lab - host"), lowest quality, 30 fps, BelowNormal priority, never focused.
- **Steam:** failed reliable sends wait and go out in order (were dropped). A lab Steam mock (`SteamMock.cs`) and the `steam` tour test the Steam transport with one account (join by `+mt_join`, rich presence, held sessions, failed sends); mutation-checked.
- **Scene 13 seat:** the core computer (`InteractivePC`) is one operator, the others watch through their eyes; nobody is left seated. Scene 13 passes the every-chapter run.
- **Core computer (Scenes 13, 15) shared:** the operator's keys, Enter, folders and program stages are replayed on every copy (`CoreComputer.cs`, `Op.Call` 10-14); `core` tour, mutation-checked.
- **Scene 14 quest game shared:** clicks and replies (`QuestGame.cs`, `Op.Call` 8-9); `quest` tour, mutation-checked.
- **Scene 1 tutorial in the lab:** answered (hint question, mouse, walking) as decompiled from `Location1Tutorial.Update`.
- **Lab metrics:** NPC animation lag counts lateness only (early chapter-start changes reported as `early`); the interactions tour waits for the client's player before its final checks.

## Done in 0.3.5 (from the 2026-10-28 playtest with two friends)
- **Minigame hooks check the real type:** the game shares one compiled body between methods with identical code, so a hook on `Location18_TicTacToe.StartGame` also ran for Scene 1's tutorial hint; a client became "minigame owner" and the host's phone pickup was blocked. Every minigame and phone-character hook now checks `GetIl2CppType()`.
- **Scene 1 phone and app host-driven** (`AppSync.cs`, see SYNC.md 4b): one phone holder, the app's dialogue, name, days, shop, cooking board and money follow the host; `scene1` tour with screenshots, client ends on the host's day.
- **Phone in the holder's hand for the others:** carried items ride on the holder's nearest wrist/hand bone (the game's hold slot sits near the camera); `phone` tour checks 0.12 m from the hand.
- **Rooms stay on around a player:** the room sync no longer switches off a room (solid colliders at least 2.5 m wide) while this player stands in it; it goes off when they leave (`roomhold` tour, mutation-checked).
- **All of Scene 1 in the lab:** `scene1` plays every day (1, 2, 5, 10, 20, 33, 37) with two players and both arrive in Scene 2 together (2026-10-28).
- **End of Scene 1:** `scene1end` jumps to day 37 (with day 5's background load of Scene 2) and plays it to "Put the phone down."; both copies arrive in Scene 2 with a player (screenshots: no black screen).
- **Gathering (playtest item 16):** already covered: each chapter starts with everyone held until all have loaded (start barrier), and everyone is gathered next to the host after a cutscene (`gather` tour); "Bring everyone here" in the F5 panel does it by hand.
- **Back to the menu:** a client whose player exits to the main menu (`GameController.ExitGame`, hooked) leaves the game instead of being pulled back (`menuleave` tour, failed before the fix); following the host to the menu uses the same `ExitGame`, flagged as the mod's own (a plain load left the player's hands in the menu: `menureturn` tour, failed before the fix).
- **Menu layout:** MULTIPLAYER sits in the play group under LOAD GAME; the column is tightened so EXIT keeps its place and gap (screenshot checked).
- **Dialogue choices** wait for the client's menu and player before `ClickButton` (decompiled 0x3dfd10); **NavMesh agents** of puppeted NPCs are put back on the mesh. Neither error reproduced in the lab (`choice` tour), so both are guards for the playtest errors.
- **Lab screenshots:** `LabShot`/`LabShotAll` (`LabShots.cs`) and `tools/shots_to_png.py`.

## Open, in order
1. **Ask the user** to check the other player's shading in the game (0.3.2 change, not seen by a person yet).
2. **Scene 1 phone: proven** (`phone` tour passes, in the suite since 2026-10-27). The gate was the tutorial's "interact" step: with quest hints on, the player clicks "Interactive TShip Tutorial", and the end of the T-shirt dialogue arms the room (`ObjectInteractiveGroup.ActivationAndDestroy`). Found with the wiring dump (`-mt-dump <scene>` also writes `private-wires-<scene>.txt`: every persistent UnityEvent call).
3. **Scene 14 phone character: done (0.3.4, `mobile` tour).** Next: the **core computer's entry pattern** (sphere moves from raw input in `GamesCore_Main.Update`; its stage change is relayed).
4. **Steam with real players: done** (2026-10-28 playtest, three players over Steam). Next: play 0.3.6 with friends to confirm the playtest fixes and the dialogue-choice and NavMesh guards. Logs: `BepInEx\LogOutput.log` on every PC.
5. Trigger and puzzle relay tours, echo windows, performance.

## Release rules
- Every version is a real GitHub release in this repo, tagged `vX.Y.Z`. Test builds are pre-releases. A tag or a draft alone doesn't count.
- Asset name `MiSideTogether-*.zip`. Build the zip with Python `zipfile` (PowerShell 5.1 `Compress-Archive` writes backslash paths).
- Bump `ModVersion` (`src/Core/Runtime.cs`) for every build friends must match, `Wire.Version` when the protocol changes.
- Never put a bot token or `discord.json` in any repo or message.

## Setting up a new PC
1. **Game and repo:** MiSide from Steam (app 2527500). Clone the repo anywhere (the user keeps it outside the game folder, e.g. `Desktop\AIStuff\ai-work-pc-games\Miside Multiplayer Mod`). `scripts\game-dir.ps1` finds the game: `MISIDE_GAME` if set, then the folder above the repo (cloned inside the game, as on the first PC), then `steamapps\common\MiSide` in every Steam library. Run the scripts from the PowerShell that allows them (`-ExecutionPolicy Bypass` per process if scripts are disabled).
2. **Loader:** BepInEx 6.0.0-**be.790**, Unity.IL2CPP win-x64, from https://builds.bepinex.dev/projects/bepinex_be, unzipped into the game folder. Start the game once and wait for the menu (about a minute) so `BepInEx\interop\` gets generated. The build references it.
3. **SDK:** .NET 8 SDK. `scripts/build.ps1` uses `%USERPROFILE%\.dotnet` if it's there, else the dotnet on `PATH`.
4. **Python 3** on `PATH`. The first build makes `private\venv` and installs UnityPy, then extracts the animator parameter table from the game data into `artifacts\generated\` (never distribute it).
5. **Build and install:** `scripts\build.ps1` (installs into `BepInEx\plugins\MiSideTogether`, keeps the last 10 DLLs in `private\plugin-backup`); `-NoInstall` builds only.
6. **Lab copies:** `scripts\make-lab.ps1 -Names host,client,c2,c3` creates `private\lab-*` (hardlinks and junctions to the game, never edit game bytes through them). `scripts\second-instance.ps1` makes `private\lab-second` for playing two copies by hand.
7. **Tests:** `scripts\run-tour.ps1 -Tour <name> [-Chapter "Scene 3 - WeTogether"]`, `scripts\lab-suite.ps1` (all tours), `scripts\interactions-all.ps1 [-Chapters ...]` (every chapter, table in `artifacts\tests\interactions-all.md`). Logs go to `artifacts\tests\logs\<tour>\`. `tests\verify-lab.py` checks them.
8. **Optional:** decompiled interop signatures for reading game APIs: `ilspycmd` 8.2 over `BepInEx\interop\Assembly-CSharp.dll` into `%USERPROFILE%\MiSide-decomp` (never commit). Native bodies: REA/Ghidra on `GameAssembly.dll`, addresses from `-mt-trace`.

## Safety
- The lab backs up and restores the user's save folder (`%USERPROFILE%\AppData\LocalLow\AIHASTO\MiSideFull\Save`) every run, keeps the last 30 in `private\save-history`, and refuses to start while the user's own MiSide runs. The user wants that save left empty (they start fresh).
- Stop only PIDs from `private\lab-pids.txt` or exes under `private\`. One tour at a time (`private\tour.lock`).
- Never distribute game files, `private\`, `artifacts\generated\` or decompiled code.
- Uninstall: delete `BepInEx\plugins\MiSideTogether`.
