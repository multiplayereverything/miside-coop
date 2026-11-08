# MiSide Together development

Start with `docs/HANDOFF.md` (current state, next steps, new-PC setup), `README.md` (behaviour, limitations) and `docs/SYNC.md` (every synced thing, how, which tour proves it, and the work list). Keep them current, along with `MODLOG.md`. Per-cutscene tuning: `CUTSCENES.md`. Per-chapter coverage: `docs/SYNC-INVENTORY.md` (regenerate with the `inventory` tour plus `python tools/inventory_report.py`).

## How to work (Karpathy guidelines, `andrej-karpathy-skills:karpathy-guidelines`)
Every agent on this repo follows these. They favour care over speed; use judgement on trivial edits.
1. **Think before coding.** State your assumptions. Before writing a fix, find out what the game actually does: the wiring dump (`-mt-dump`), `-mt-trace`, disassembly, and a control run with `-mt-nopatch`. If there are two readings of a request, name both; when the user has said not to ask, pick one and note the choice in `MODLOG.md`.
2. **Simplicity first.** Write the least code that solves the problem, with nothing speculative: no options, abstractions or error handling for cases that can't happen. Reuse what exists (`Op.Call` entries, the prop stream, `CutsceneRules`) before adding a new op or system.
3. **Surgical changes.** Every changed line traces to the task. Match the surrounding style and line endings (most files are CRLF, some have a BOM). Don't reformat or refactor neighbouring code; mention unrelated problems instead of fixing them. Remove what your own change left unused, including temporary lab logging.
4. **Goal-driven execution.** Turn the task into a check that can fail: a tour with `MT-CHECK`s, then a mutation run proving the check fails without the fix. Loop until it passes. Report only what was observed (`/results-table`: ✅ with evidence, ⚠️ when not verified).

## Test-driven development (`test-driven-development`)
Use this for every feature, bug fix, refactor and behaviour change, before writing the code it covers. The only exceptions are throwaway prototypes, generated code and configuration files; anything else needs the user's permission.

**The rule: no production code without a failing test first.** If you didn't watch the test fail, you don't know it tests the right thing. Code written before its test gets deleted and rewritten from the test: don't keep it as a reference or adapt it. "Just this once", "too simple", "I already tried it by hand", "I'll test after" and "this case is different" all mean stop and start over.

Here a test is an `MT-CHECK` in a tour, listed in that tour's `Expect` in `scripts/run-tour.ps1`, or a check in `tests/verify-lab.py`. It runs in real game copies, so a mock is the last resort (`SteamMock.cs` exists because the lab has one Steam account).

1. **Red: write one failing test.** It covers one behaviour, under a name that says what should happen. Before writing it, name the production change that would make it fail.
2. **Watch it fail.** Never skip this. Run the tour with `scripts/run-tour.ps1 -Tour <tour>` and read the log. It must fail on the expected `MT-CHECK` because the behaviour is missing, not because of a typo, a stuck lab copy or a busy PC. If it passes at once, it's testing what already exists: fix the test. If it errors, fix that and run it again.
3. **Green: write the least code that passes.** Add no options, and don't improve anything else on the way.
4. **Watch it pass.** The `MT-CHECK` passes, the other affected tours still pass, and the logs show no new `MT-MODERROR` or `own event threw`. If it fails, fix the code, not the test.
5. **Refactor, only when green.** Remove duplication and improve names, adding no behaviour, and stay green.
6. **Repeat** with the next failing test.

**Bugs:** every bug starts as a failing test that reproduces it. Never fix one without a test. A bug found in a real session gets a tour or a new `MT-CHECK` too. If it can't be reproduced in the lab, say so instead of reporting it fixed.

**Honest tests:**
- Assert what really happened in the game, never that a mock or the mod's own flag was set.
- Keep test-only code in `src/Lab`, out of the sync code.
- Understand what a game call does (`-mt-dump`, the decompiled code) before faking or stubbing it.
- A test that needs a huge setup usually means the design needs simplifying.

**Running tests:** while working, run the tours a change can affect, or `scripts/lab-suite.ps1 -Quick`. Before a milestone or release, run the full `scripts/lab-suite.ps1`. Report every failure by name, including ones you didn't cause, and say which tours you didn't run.

**Before calling work done:**
- Every new behaviour has a check, and you watched each one fail for the expected reason.
- You wrote only enough code to pass.
- All affected tours pass with clean logs.
- Edge cases and errors are covered, or named as not covered.

## Repository
- **GitHub:** https://github.com/multiplayereverything/miside-coop (public, branch `main`). This folder is the repo (anywhere on disk; `scripts/game-dir.ps1` finds the game); `.gitignore` keeps out `artifacts/` (builds, test logs, the generated dialogue script), `private/` (lab copies, backups), `graphify-out/` and `obj/`.
- **Online play:** Steam P2P (the game's own `steam_api64`), LAN/VPN, or an address. This mod does not use the Multiplayer Everything global relay.

## Code graph
- **Mod code:** the mod's Graphify graph is `graphify-out/` in this folder. Run `graphify update .` after code changes.
- **Game signatures:** decompiled interop (signatures only, no bodies) is in `%USERPROFILE%\MiSide-decomp` (`firstpass\` holds `GlobalGame`/`GlobalTag`).

## Architecture
- **Entry point:** `BepInExEntry` (BepInEx 6 `BasePlugin`) installs the Harmony patches and adds `TogetherBehaviour`. That is the only
  class injected into IL2CPP, and it forwards `Update`/`LateUpdate`/`OnGUI` to `TogetherRuntime`, a plain C# partial class spread over the feature files.
- **Network (`ITransport`):**
  - `Transport` is the TCP star, ported from Spooky Together.
  - `SteamTransport` is Steam P2P through flat `steam_api64` P/Invoke. The game's Steamworks.NET wrapper is partly stripped.
  - `HostTransport` is a host listening on both. The host is peer 0 and relays to everyone. Packets are `Op` byte + `BinaryWriter` body.
- **Failure isolation:** every subsystem runs through `Step(...)`, so one failing feature logs `MT-MODERROR` and the others keep running.
  - In lab mode `Step` also times each subsystem: `steps=` in `MT-METRIC`.
- **Source layout:**
  - `src/Core`: entry point, runtime loop, Harmony patches.
  - `src/Network`: transports, packet handling, migration, LAN.
  - `src/Sync/Players`: bodies, bones, props, items.
  - `src/Sync/Story`: scene follow, story events, triggers, dialogue, novella, cutscenes, interactions, minigames, deaths, seeds, Mita face.
  - `src/Sync/World`: room state, saves.
  - `src/UI`: native menu, F5 panel.
  - `src/Lab`: tours, state logging, inventory (lab only).
- **Feature files:**
  - `Avatars`/`Bones`: body copy, bone pose, prop visibility.
  - `AnimSync`: animator fallback.
  - `SceneSync`: follow, history, lobby wait, chapter start barrier, gather after cutscenes.
  - `WorldSync`: story events, NPC puppets, doors, `SceneKeys`.
  - `MitaFace`, `Cutscenes`, `CutsceneRules` (per-case tuning), `Interactions` (mirror or actor-only), `Dialogue`, `Novella`, `Triggers` (look, distance, method relays), `Items`, `Deaths`, `Seeds`, `Props`, `Minigames`, `WorldState` (room: objects, lights, animators), `Migration`.
  - `Saves`: backup, mirror, `GlobalGame` statics.
  - `NativeMenu`, `Ui` (F5 panel), `LanDiscovery`.
  - `Lab`/`Tours`/`Research`: lab-only.

## How the mod hooks the game (all in `src/Patches.cs`, minigames in `Minigames.InstallMinigamePatches`)
| Hook | Purpose |
| --- | --- |
| `ObjectInteractive.Click`, `Trigger_MouseClick.Click` (prefix + postfix) | Interactions relayed by scene key; replayed with `Replaying` set (no echo). In a story-log chapter (`StoryLog.cs`) a client's press is a request: nothing runs until the host's log brings it back. The chapters on the log are listed in `LogChapter`; try another with `-mt-logalso <chapter>` before listing it. |
| `ObjectInteractive.Update` (prefix + postfix), `Input.GetMouseButtonDown`/`GetButtonDown` (postfix) | A player's press never calls `Click`: `Update` reads the mouse and runs its own copy. In a session the press read inside `Update` is taken away and `Click` runs after it, so it reaches the hooks above. Lab tours that call `Click()` skip this path; `scanner` presses like a player. |
| `GameController.PhotomodeActive(bool)`, `ConsoleCall.Update` (prefix) | Photo mode and the console are off during a session, since both change the local time scale (`SessionRules.cs`). |
| `Trigger_Event.TargetEnter`/`TargetExit` (prefix + postfix), `.Restart` | Story volumes fire once per group; re-armed ones may fire again. |
| `DialogueChanger.ClickButton(int)`, `Dialogue_3DText.SkipDialogue` | Dialogue choices and skips. |
| `Scene_Load.StartLoad` | Chapter transitions: everyone runs the game's own `Scene_Load`. |
| `Playable_Animation.Play` | Cutscene start. Time sync is streamed separately for every running `PlayableDirector`. |
| `MitaPerson.FaceEmotion*`/`FaceLayer*` | Host face changes replayed on clients. |
| `Character_Look.LookOnPlayer`/`LookOnPlayerAndRotate`/`PriorityLookOnPlayer`/`LookRotatePriorityOnPlayer`/`PriorityLookAndOnPlayer` (prefix) | Look at the nearest player's head instead. |
| `GameController.AddKeyItem`/`RemoveKeyItem` | Shared key inventory; the key is taken in the prefix, before the game moves the item. |
| `PlayerMove.TakeItem`/`RemoveItem` | Hand items shown in the holder's hand on their body. |
| Death prefixes: `Location6_MitaKiller.Kill`, `Location10_ManekenChekpoint.PlayerDeath`, `Location11_Lift.DeathPlayer`, `Location11_LampMove.RestartPlayer`, `Location20_Arena.PlayerDeath`, `Location20_RunCorridor.KillPlayerStart`, `Mob_Maneken.StartKillPlayer`, `Shooter_Main.PlayerDeath`, `Location19_Game4.Death` | Everyone restarts together. Prefixes, because the game's death code can throw part-way; there is a 6 s echo window. |
| `Random_Position`, `Material_Random`, `Animator_RandomPlay`, `Transform_StartRandom`, `Location21_DialogueRandom` | Seeded `UnityEngine.Random`: key hash plus call count. |
| Minigame start methods (16 kinds, parameterless) | Owner claim (host settles races). A replayed start is blocked for the others, who watch the player's body. |
| `GameController.Pause(bool,bool)` | While connected, the pause menu keeps `Time.timeScale` at 1. |
| `PlayerMove.AnimationPlay`/`AnimationHandPlay` (prefix) | Skipped for someone else's click only when its rule is `who=actor`. By default (mirror) every player plays it. |
| `ObjectAnimationPlayer.AnimationPlay`/`AnimationPlayFast`/`AnimationPlayOnPlayer` (postfix) | Tracks the actor's interaction. For `who=actor` rules, `Op.InteractionDone` runs `eventFinish` on the others when it ends; mirrored ones end by themselves. |
| `Dialogue_3DText.Start` (postfix) | Registers the line with its start time (dialogue stream and lag metric), with no per-frame scan. |
| `Location18_Novella.NextDialogue` (postfix) | Visual novel: a client's click is sent to the host, which reads on; clients step after the host's streamed position. |
| `Trigger_MouseEvent.Enter` (postfix, one-shot looks only), `Trigger_DistanceCircle.Update` (prefix + postfix: `targetInside` flips) | `Op.LookTrigger` / `Op.DistanceTrigger`. |
| `Basement_Safe.ClickButton`, `InputKeyboard_Main.AddKey`/`RemoveKey`/`ButtonEnter`/`ButtonUpper`, `Trigger_DistanceCamera.Enter`/`Exit` | `Op.Call` method relays (table in `Triggers.cs`). |
| `Location14_PlayerMobileMove.Play` (prefix + postfix), `.Update`/`.FixedUpdate` (prefix) | Scene 14 phone character: the phone's holder runs it and relays `Op.Call` 15; on the others it is shown, not run, and placed from the prop stream (`PhoneCharacter.cs`). |
| `Tamagotchi_Main.GameStart`/`GameStop`, `Tamagotchi_Dialogue.KeyNextDialogue`, `Location1_InputNamePlayer.CheckName`, `ButtonMouseClick.PointerDown`/`PointerUp`, `UnityEngine.UI.Button.Press`, `TamagotchiGame_Cooking.Ready`/`Cut` | Scene 1 phone app: the host's are relayed (`Op.Call` 16-23), the clients' own input in the app does nothing (`AppSync.cs`), except the cooking board: every player drags their own knife and any player's `Cut` runs on every PC (`Op.Call` 27). Buttons count only under `Tamagotchi/` in Scene 1. |
| Minigame end methods (prefix + postfix) | While the player's end method runs, story relays are held (`MinigameEnding`). Watchers fire the kind's end UnityEvents (`MinigameKind.Events`). |
| Live minigames (`MinigameKind.Live`: Scene 4's console `MinigamesTelevisionGame` and card table `Location4TableCardGame`, which read the keyboard themselves) | On the story log the start runs on the Main Character's PC only (a blocked start's postfix doesn't claim it). Mita follows that PC (`NpcAuthority`, `Op.Mita` sent by it). Its result replies are relayed (`Op.Call` 24), an end it makes by itself is seen (`MinigameKind.Over`), and watchers see it through `Op.Screen` (`ConsoleScreen.cs`). |
| `ObjectAnimationPlayerHold.eventReady` (stand-in, `StoryLog.BuildGates`) | Hold interactions read the holder's key; the end of a hold is a Body gate on the hold's animation. Its other events fire every frame and have no story listeners, so they stay local. |
| `ObjectInteractiveItemTake.Take` (prefix), `.TakeDestroy` (postfix) | On the story log a watcher's hand stays put; the taker's arrival goes through the log (`Op.Call` 25). The host completes a take that isn't reported within 8 s. |
| `Scene_Load.UnloadScene(string)` (postfix) | A chapter scene unloaded mid-play is unloaded on every PC (`Op.Call` 26). A scene that was already loaded beside the chapter and becomes active continues the chapter (`SceneSync.ContinueChapter`): no reset, no barrier, no reload. |

## Not hooked, but driven by the mod
- **Scene follow (late join):** set the host's `GlobalGame` statics, then `LoadingLevel`, then load `SceneLoading`.
  - A direct `SceneManager.LoadScene` of a chapter breaks `World.Start`.
- **NPC puppets:** each NPC's transform and animator are set in `LateUpdate`, and the `NavMeshAgent` gets `updatePosition=false` on clients.
  - Chasers are deliberately local.
- **Carried objects:** every `Transform_Magnet` parented under the actor's player is streamed relative to the body (`Op.Carry`).
  - Watchers pause their own magnet and place the object on the actor's body copy. Magnet keys are registered at chapter start.
- **Dialogue:** the host streams active `Dialogue_3DText` keys. Clients activate missing lines and `SkipDialogue` lines the host finished (after 1.2 s).
- **Doors and props:** last mover wins, with a 0.5–0.6 s echo guard.
- **Saves:**
  - `<persistentDataPath>/Save` (the `Continue` file and friends): the host scans it every 2 s and mirrors changes; clients back up their own saves first.
  - `GlobalGame.dataFiles` is the Data folder (languages and voices), not the saves. Redirecting it breaks voice loading.
- **Scene keys:** every object's key is recorded when the chapter loads (`SceneKeys.RegisterItems`), the same on every PC. A key computed later can drift (sibling numbers change as the story spawns and destroys objects).
- **Debug page:** F5, "Debug page" (`src/UI/DebugPage.cs`, `Op.Status`): each player's chapter, quests, story position, what they wait on, ping; skip a stuck step; save a bug report on every PC.
- **Native menu:**
  - MULTIPLAYER is cloned from `Button Settings`; the page is cloned from `Location MainOptions`, with rows cloned from `Button Option Game`.
  - After changing a page's `objects`, call `MenuLocation.Start()` again; otherwise `MenuLocation.Update` throws ArgumentOutOfRange every frame.

## IL2CPP gotchas found here
- **Stripped Animator members:** `Animator.parameters` cannot be loaded (`TypeLoadException`), and `GetParameter`/`get_parameters` are stripped.
  - The parameter table comes from the game data at build time (UnityPy). The string overloads of `Get/Set*` work.
- **Stripped IMGUI:** `GUILayout.BeginArea` and `GUI.TextField` are stripped; `GUI.Label`/`Box`/`Button`/`DrawTexture` work. The F5 panel uses `Ui.cs`.
- **Stripped encoders:** `Texture2D.EncodeToJPG`/`EncodeToPNG` are stripped; `ReadPixels`, `GetPixels32`, `SetPixels32` and `LoadImage` work.
- **Steam DLL path:** `steam_api64.dll` is in `MiSideFull_Data/Plugins/x86_64` and may not be loaded yet when the mod starts.
  - Use `NativeLibrary.SetDllImportResolver` and retry; a host adds Steam once it is ready.
- **Game methods called out of context throw:** `Location6_MitaKiller.Kill`/`StartAttack` and `Location10_ManekenChekpoint.PlayerDeath` throw from a cold scene, and a postfix then never runs.
  - Tours use the game's real paths (e.g. `ObjectInteractiveItemTake.Take`, not `PlayerMove.TakeItem`), and relays that must not miss use prefixes.
- **Scene-less inventory copies:** the inventory may receive a copy of an item ("/ItemBow#0").
  - `SceneKeys.RegisterItems` records the original keys at chapter start, and `FindItemByName` matches copies by name.
- **Few loose props, few timelines:** there are almost no loose physics props, and timelines exist only in Scenes 1, 3, 5, 6, 8, 12 and 13 (`tools/count_timelines.py`). Most "cutscenes" are scripted player and Mita animations started by story events.
- **Steam relaunch:** a direct exe launch relaunches through Steam; lab copies carry `steam_appid.txt` (2527500).
- **Quoting arguments:** `Start-Process -ArgumentList` does not quote.
- **Shared compiled bodies:** IL2CPP folds methods with identical code into one function, so a Harmony hook on one also runs for the others (a minigame's `StartGame` ran for a tutorial hint). Hooks that act on their instance must check `__instance.GetIl2CppType().Name`.
- **Scene 19 (Glasses):** cannot be started cold, even from the menu. Scene 20 cannot be started cold right after Scene 18.

## Lobby and chapter start
- **No mid-chapter joins:** a Hello while the host is in a chapter is accepted but `Peer.Waiting`. The client gets the `wait-chapter` notice and waits in the menu until the host's next `Op.Scene`.
- **Migration rejoin:** a Hello with `rejoining` set and the same scene goes straight in.
- **Player cap:** `MaxPlayers = 4`.
- **Chapter start barrier:** after a chapter loads, every machine holds `Time.timeScale = 0` once its player exists. The host waits for every player's `SceneReady`, then sends `go:<seq>`; a 25 s timeout releases anyone. This makes the openings start together.

## Rules
- **Build and test:** build with `scripts/build.ps1`. Test with `scripts/run-tour.ps1 -Tour <tour>` or `scripts/lab-suite.ps1`.
  - Lab copies start minimized as normal 480x270 windows titled "MiSide lab - <name>" (lowest quality, 30 fps via `-mt-fps`, BelowNormal priority). Restoring one from the taskbar brings it up small at the right edge of the primary monitor; the launcher sets that once and never moves, re-minimizes or focuses a window afterwards (`launch-lab.ps1 -Hidden` hides them instead). The mod keeps `Application.runInBackground` on, so minimized copies keep running. The user's PC is shared with other mods' labs: one tour at a time, and never send keys or clicks to the desktop.
  - Presses in tours go through `LabPress(o)` (`src/Lab/LabPress.cs`), never `o.Click()`: a player's press never calls `Click`, so a tour calling it tests a path real play doesn't take.
  - Screenshots: `LabShot("label")` / `LabShotAll("label")` write `shots/<label>.rgb` next to the lab game (encoders are stripped); `python tools/shots_to_png.py private/lab-host/shots private/lab-client/shots` makes PNGs to compare what each player sees.
  - `-mt-dump <scene>` (scene name with `_` for spaces) writes the hierarchy and, in `private-wires-<scene>.txt`, every persistent UnityEvent call (`source [Type].field -> target [Type].Method`). To find what arms or starts something, search the wiring for its name and walk backwards.
  - A fresh install stops in `SceneLoading` on the game's LANGUAGE/VOICE picker; lab copies answer it with English (`FirstRunLanguageTick`, Lab.cs).
  - The user's save folder is backed up and restored per run, and the lab refuses to start beside the user's own MiSide.
  - Steam with one account: `run-tour.ps1 -Tour steam` uses the lab Steam mock (`src/Network/SteamMock.cs`, `-mt-steam-mock`, `-mt-steam-fail`): fake accounts, rich presence through `private/steam-mock`, sessions held until accepted, failed sends.
- **Real play logs:** every session writes `BepInEx/MiSideTogether-logs/session-*.log` (`src/Core/SessionLog.cs`: `MT-SESSION` header, everything `Log()` says, `MT-ERROR`, `MT-STATE` every 2 s), and clients stream theirs to the host as `*-from-<name>-<id>.log`. Always run `scripts/collect-logs.ps1` after a real session and read the result.
- **Evidence:** `private/lab-*/misidetogether-lab.log` (`MT-CHECK`, `MT-METRIC` with `modmax` worst frame, `MT-STATE` every 2 s, `MT-ERROR`, `MT-MODERROR`). Kept per tour in `artifacts/tests/logs/<tour>/`, with Unity error excerpts.
- **Latency runs:** `run-tour.ps1 -Lag 120 -Jitter 40 -JoinAddress <LAN IP>` simulates internet latency (`-mt-lag`, lab only).
- **Control runs:** `run-tour.ps1 -ExtraHostArgs "-mt-nopatch" -Players 1` loads the mod without any game hook, to tell game-side failures from the mod's.
- **One tour at a time:** `private/tour.lock`. Two runs would kill each other's lab processes through the shared PID file.
- **Process safety:** stop only the lab processes (PIDs in `private/lab-pids.txt`, or exe paths under `private/`). Never kill a user-owned game.
- **Lab copies:** `private/lab-*` hardlink and junction the game. Never edit game bytes through them.
- **Never distribute:** game assemblies, `private/` (labs, venv), `artifacts/generated/`, or decompiled code.
- **Protocol changes:** bump `Wire.Version` when the protocol changes, and `ModVersion` for every build friends must match.
