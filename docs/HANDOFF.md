# MiSide Together - handoff

Read this first when picking the work up on another PC. Then `AGENTS.md` (architecture, hooks, rules), `README.md` (behaviour, limits), `docs/SYNC.md` (what syncs, how it's proven, the work list) and `MODLOG.md` (history).

## Goal
Co-op for MiSide (2 to 4 players). Everyone sees the others as the main character with real animations, and the game stays in sync: chapters, story, dialogue, cutscenes, Mita and the other NPCs, room state, minigames and endings. BepInEx, to match the user's Baldi Together and Spooky Together mods.

## State (2026-10-28)
- **Version:** 0.3.5 in progress (local commits, protocol `Wire.Version` 5: the carry message carries the hand bone). The last published pre-release is v0.3.4 (`de23d30`).
- **Online play:** Steam P2P (friends join from MULTIPLAYER in the game's menu, or Steam's Join Game / invite, `+mt_join`), LAN/VPN, or a direct address (TCP 27030). It doesn't use the Multiplayer Everything relay.
- **This PC (2026-10-27):** the repo lives at `Desktop\AIStuff\ai-work-pc-games\Miside Multiplayer Mod`, outside the game; `scripts\game-dir.ps1` finds the game. BepInEx be.790 and the mod are installed in the game. The user's save folder is empty, as they want it.
- **Website:** `games.json` in the website repo has the `miside` entry. The Website session updates it; don't edit it from here.
- **Tests (lab copies on one PC, minimized):** suite 30 tours, all pass on 0.3.5 (2026-10-28; new: `safe`, `choice`, `menureturn`, `scene1`, and `core` now types the right version and compares both screens). Pressing every interaction in every chapter: 18 of 18 chapters pass.
- **Not tested:** a real playtest with friends; two real Steam accounts on separate PCs; four real players (4-player runs are lab-only); installing through the launcher; the other player's shading seen by a person.

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
- **Rooms stay on around a player:** the room sync no longer switches off a room (solid colliders at least 2.5 m wide) while this player stands in it; it goes off when they leave.
- **Back to the menu:** a client that goes to the main menu by itself leaves the game; following the host to the menu uses `GameController.ExitGame` (a plain load left the player's hands in the menu: `menureturn` tour, failed before the fix).
- **Menu layout:** MULTIPLAYER sits in the play group under LOAD GAME; the column is tightened so EXIT keeps its place and gap (screenshot checked).
- **Dialogue choices** wait for the client's menu and player before `ClickButton` (decompiled 0x3dfd10); **NavMesh agents** of puppeted NPCs are put back on the mesh. Neither error reproduced in the lab (`choice` tour), so both are guards for the playtest errors.
- **Lab screenshots:** `LabShot`/`LabShotAll` (`LabShots.cs`) and `tools/shots_to_png.py`.

## Open, in order
1. **Ask the user** to check the other player's shading in the game (0.3.2 change, not seen by a person yet).
2. **Scene 1 phone: proven** (`phone` tour passes, in the suite since 2026-10-27). The gate was the tutorial's "interact" step: with quest hints on, the player clicks "Interactive TShip Tutorial", and the end of the T-shirt dialogue arms the room (`ObjectInteractiveGroup.ActivationAndDestroy`). Found with the wiring dump (`-mt-dump <scene>` also writes `private-wires-<scene>.txt`: every persistent UnityEvent call).
3. **Scene 14 phone character: done (0.3.4, `mobile` tour).** Next: the **core computer's entry pattern** (sphere moves from raw input in `GamesCore_Main.Update`; its stage change is relayed).
4. **Two Steam accounts:** the real proof. Checklist for the user: both on Steam and friends, same mod build; host picks HOST GAME, the friend picks JOIN <name> (STEAM) in MULTIPLAYER (or Join Game in Steam's friends list); then play to Scene 2 and back to the menu. Logs: `BepInEx\LogOutput.log` on both.
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
