# MiSide Together development

Start with `../handoff.md` (current state, next steps), `README.md` (behaviour, limitations) and `docs/SYNC.md` (every synced thing, how, which tour proves it, and the work list). Keep them current, along with `MODLOG.md`. Per-cutscene tuning: `CUTSCENES.md`. Per-chapter coverage: `docs/SYNC-INVENTORY.md` (regenerate with the `inventory` tour plus `python tools/inventory_report.py`).

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
| `ObjectInteractive.Click`, `Trigger_MouseClick.Click` (postfix) | Interactions relayed by scene key; replayed with `Replaying` set (no echo). |
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
| Minigame end methods (prefix + postfix) | While the player's end method runs, story relays are held (`MinigameEnding`). Watchers fire the kind's end UnityEvents (`MinigameKind.Events`). |

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
- **Scene 19 (Glasses):** cannot be started cold, even from the menu. Scene 20 cannot be started cold right after Scene 18.

## Lobby and chapter start
- **No mid-chapter joins:** a Hello while the host is in a chapter is accepted but `Peer.Waiting`. The client gets the `wait-chapter` notice and waits in the menu until the host's next `Op.Scene`.
- **Migration rejoin:** a Hello with `rejoining` set and the same scene goes straight in.
- **Player cap:** `MaxPlayers = 4`.
- **Chapter start barrier:** after a chapter loads, every machine holds `Time.timeScale = 0` once its player exists. The host waits for every player's `SceneReady`, then sends `go:<seq>`; a 25 s timeout releases anyone. This makes the openings start together.

## Rules
- **Build and test:** build with `scripts/build.ps1`. Test with `scripts/run-tour.ps1 -Tour <tour>` or `scripts/lab-suite.ps1`.
  - Lab windows are hidden. The user's save folder is backed up and restored per run, and the lab refuses to start beside the user's own MiSide.
- **Evidence:** `private/lab-*/misidetogether-lab.log` (`MT-CHECK`, `MT-METRIC` with `modmax` worst frame, `MT-STATE` every 2 s, `MT-ERROR`, `MT-MODERROR`). Kept per tour in `artifacts/tests/logs/<tour>/`, with Unity error excerpts.
- **Latency runs:** `run-tour.ps1 -Lag 120 -Jitter 40 -JoinAddress <LAN IP>` simulates internet latency (`-mt-lag`, lab only).
- **Control runs:** `run-tour.ps1 -ExtraHostArgs "-mt-nopatch" -Players 1` loads the mod without any game hook, to tell game-side failures from the mod's.
- **One tour at a time:** `private/tour.lock`. Two runs would kill each other's lab processes through the shared PID file.
- **Process safety:** stop only the lab processes (PIDs in `private/lab-pids.txt`, or exe paths under `private/`). Never kill a user-owned game.
- **Lab copies:** `private/lab-*` hardlink and junction the game. Never edit game bytes through them.
- **Never distribute:** game assemblies, `private/` (labs, venv), `artifacts/generated/`, or decompiled code.
- **Protocol changes:** bump `Wire.Version` when the protocol changes, and `ModVersion` for every build friends must match.
