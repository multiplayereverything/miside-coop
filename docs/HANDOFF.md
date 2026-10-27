# MiSide Together - handoff

Read this first when picking the work up on another PC. Then `AGENTS.md` (architecture, hooks, rules), `README.md` (behaviour, limits), `docs/SYNC.md` (what syncs, how it's proven, the work list) and `MODLOG.md` (history).

## Goal
Co-op for MiSide (2 to 4 players). Everyone sees the others as the main character with real animations, and the game stays in sync: chapters, story, dialogue, cutscenes, Mita and the other NPCs, room state, minigames and endings. BepInEx, to match the user's Baldi Together and Spooky Together mods.

## State (2026-10-27)
- **Version:** 0.3.2, a full GitHub release (`v0.3.2`, commit `78939ad`) on https://github.com/multiplayereverything/miside-coop, with `MiSideTogether-0.3.2.zip` (layout `BepInEx/plugins/MiSideTogether/MiSideTogether.dll`, what the launcher installs) and the DLL. Protocol `Wire.Version` 4. Every player needs the same build.
- **Online play:** Steam P2P (friends join from MULTIPLAYER in the game's menu), LAN/VPN, or a direct address (TCP 27030). It doesn't use the Multiplayer Everything relay.
- **Website:** `games.json` in the website repo has the `miside` entry. The Website session updates it; don't edit it from here.
- **Tests (hidden lab copies on one PC):** suite 21/21 tours pass; the 0.3.2 release run passed 22/23 (only `phone` fails, see below). Pressing every interaction in every chapter (`scripts/interactions-all.ps1`): 17 of 18 chapters pass.
- **Not tested:** a real playtest with friends; two real Steam accounts on separate PCs; four real players (4-player runs are lab-only); installing through the launcher.

## Done in 0.3.2
- **Scene 12 exit door:** cutscene starts are retried for 5 s and start when only the timeline is playing (`NeedsStart`). Player animations started by the host's story are caught up on clients (`Op.Call` 7, `Interactions.cs`), checked by the client's own key.
- **Avatar shading:** the copy of the player's body receives shadows like Mita (user report: flat and over-bright).
- **Held items and the Scene 1 phone:** done by the presser only (`HeldItems.cs`). Cutscenes in a running chapter are run by the host.
- **Lab:** `MT-LOOK` material dump (`src/Lab/LookDump.cs`); `-mt-trace` (`src/Lab/Trace.cs`, don't trace `ObjectDoor`, it freezes the game).

## Open, in order
1. **Ask the user** to check the other player's shading in the game. The lab windows are hidden, so it hasn't been seen.
2. **Co-op Anything write-up** (PR cranebeaker/coop-anything#220) still says 16 of 18 chapters and lists the Scene 12 door as broken. Refresh it.
3. **Scene 13 seat:** after the host takes the cartridge (presser-only), the other player stays seated at the PC until they stand up. Expected; document it in README/CUTSCENES.md, or make the tour accept it.
4. **Scene 1 phone:** never armed in hidden lab windows within 4 minutes, so its pickup is unproven (`phone` tour).
5. **Scene 14 quest game** and the **Scene 13/15 core computer:** not shared. Design notes are in `docs/SYNC.md` item 4 (decompiled entry points).
6. Trigger and puzzle relay tours, echo windows, a real two-account Steam session, performance.

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
