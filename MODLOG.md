# MiSide Together — MODLOG

Journal for the MiSide co-op mod. Anything not written here is lost at the next context reset.

## Environment (2026-10-22)
- Game: MiSide (Steam, AIHASTO), `C:\Program Files (x86)\Steam\steamapps\common\MiSide`
- Exe `MiSideFull.exe`, Unity **2021.3.35f1**, **IL2CPP** (`GameAssembly.dll`, `MiSideFull_Data\il2cpp_data`).
- Launching the exe directly makes Steam relaunch it (new PID). Look up the process by name after launch.
- Loader: **BepInEx 6.0.0-be.790 Unity.IL2CPP win-x64** (from builds.bepinex.dev). The user chose it to match
  their Baldi's Basics Plus / Spooky's Jump Scare Mansion mods. MelonLoader 0.7.3 was tried first and then removed.
  - First run generates `BepInEx\interop\` (89 assemblies) in about 1 minute. Log: `BepInEx\LogOutput.log`.
- Build SDK: .NET 8 SDK in `%USERPROFILE%\.dotnet` (set `DOTNET_ROOT` and put it on `PATH`). The machine-wide
  dotnet has runtimes only.
- Decompiled interop signatures: `%USERPROFILE%\MiSide-decomp` (ilspycmd 8.2). These are signatures only, with no
  method bodies. Never commit them.

## Restore path
- Remove the mod: delete `BepInEx\plugins\MiSideTogether\`.
- Remove BepInEx: delete `BepInEx\`, `dotnet\`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`.

## Scenes (build order)
SceneAihasto, SceneLoading, SceneMenu, "Scene 1 - RealRoom" … "Scene 20 - FightMita", MinigameShooter.

## Key game classes (Assembly-CSharp, global namespace)
- `PlayerMove`: local player controller. `rb`, `capsule`, `rotX`/`rotY` (look), `animPerson` (body Animator),
  `animArmsFace`, `hipsPerson`, `head`, `mainCamera`, `dontMove`, `sit`, `speedPlayer`, `velocityLocal`,
  `TeleportPlayer(Vector3[,float[,float]])`, `AnimationPlay(...)`, `TakeItem`/`RemoveItem`.
- `PlayerPerson`: body mesh component. `anim`, `meshes[]` (SkinnedMeshRenderer), `meshHead`.
- `WorldPlayer` / `GameController`: camera FX, pause (`GameController.Pause(bool,bool)`, `isPaused`), dialogue
  printing (`PrintDialogue(Dialogue_3DText,bool)`), cutscenes (`CutscenePlay`).
- `MitaPerson`: Mita NPC. `animMita`, `nma` (NavMeshAgent), `AiWalkToTarget*`, `MitaTeleport`, `FaceEmotion`.
- `ObjectInteractive.Click()`: the generic "press E on thing" entry point that fires `eventClick` (UnityEvent).
- `ObjectDoor`: hinge doors (`open`, `angleNow`, `DoorOpened/DoorClosed`, `Lock`).
- `Scene_Load.StartLoad()` / `GoScene()`: in-game scene transitions. `SceneLoading`: the loading screen
  (`levelLoadName`).
- Steamworks.NET is in the interop (SteamMatchmaking, SteamNetworkingMessages, SteamFriends all present).
- `Assembly-CSharp-firstpass`: `GlobalGame` (static: `LoadingLevel`, `namePlayer`, settings) and `GlobalTag` (static GameObjects:
  `player`, `gameController`, `world`, `playerRightItem`...).

## Route (decided 2026-10-22)
- BepInEx 6 plugin and Harmony, using the architecture of the user's Spooky Together: TCP star, `Op` byte packets, the F5 panel,
  LAN discovery, and an ini config (`BepInEx/config/MiSideTogether.ini`).
- The host leads the story and clients follow. Story *events* are relayed rather than state, because MiSide is a scripted linear
  game driven by UnityEvents. The catch-up history replays a scene's events for late arrivals.

## 0.1.0 log

### Found by running it
1. **Loading a chapter directly is wrong.** `SceneManager.LoadScene("Scene 1 - RealRoom")` causes a NRE in `World.Start`,
   `WorldPlayer.Update` and `ObjectInteractive_CaseInfo.Update` every frame, and no PlayerMove ever exists.
   - The correct way: set `GlobalGame.LoadingLevel = name`, then load `SceneLoading`.
2. **`Animator.parameters` cannot be used.** Il2CppInterop throws a TypeLoadException (`Il2CppReferenceArray<AnimatorControllerParameter>`
   violates the constraint).
   - `GetParameter(int)` gives "Method unstripping failed", and `il2cpp_class_get_method_from_name(Animator, "get_parameters")` returns 0.
   - Fix: at build time, read every AnimatorController from the game data with UnityPy (`m_Controller.m_Values.data.m_ValueArray`
     plus the `m_TOS` hash→name table), giving 282 parameters across 109 controllers. The table is embedded and looked up by base controller name.
   - The player controller "Player" has Run (float), KeyMove and Fly (bool), and Jump/Fall/StartRun/StopRun (triggers).
   - The string `Get/Set*` overloads work.
3. **`GUILayout.BeginArea` is stripped** ("Method unstripping failed"), so the F5 panel threw.
   - The panel was rewritten on `Ui.cs`: plain GUI calls, and each native widget falls back to a label plus Input handling if it is stripped.
   - The lab check `ui.panel` opens the panel in every tour.
4. **The Game Boy showed on remote bodies** (reported by the user). The body copy force-enabled every renderer, including the
   held items parented under the hand bones.
   - Fix: keep only the `PlayerPerson.meshes` and `meshHead` renderers. Lab check `avatar.noprops`.
5. **Launching the lab:**
   - A direct launch is relaunched through Steam, which drops the arguments, so lab copies get `steam_appid.txt` 2527500.
   - `Start-Process` does not quote its arguments, so the `-logFile` path with spaces killed the start.

### Lab evidence (hidden windows, loopback, 2 players)
| Tour | Result | Notes |
| --- | --- | --- |
| avatars | PASS (host 8/8, client 10/10) | 4 skinned meshes, no props, 1.4 m apart, state "Player Idle" matched, parameters synced, a 1.5 m step followed within 0.11 m |
| story | PASS | Host "Interactive Air" was replayed on the client; client "Interactive Trash" was replayed on the host |
| mita | PASS | Scene 3: client Mita within 0.00 m of the host's |
| chapters | see `artifacts/tests/chapters-results.json` | the first run skipped chapters 1–2 (a tour sequencing bug, fixed); chapters 3–9 followed, with the model shown |

All tours had 0 mod errors and 0 game errors, except 2 game errors on the client when entering Scene 6. The mod costs 0.1–0.4 ms per frame.

## 0.1.1 log (protocol 2)

### Reported by the user while watching the lab
1. **"Floating" client.** The client was teleported to the host's body during chapter intros, where the game parks the player in empty space.
   - Fix: poses carry a *free* flag (no cutscene, no `dontMove`/`animationRun`/`typeAnimation`). "Go to the host" needs both players free and a raycast floor at the target (front, then side, then behind).
   - Lab check: `grounded:<chapter>`.
2. **"Other player sees a loading screen."** This was the chapters tour switching chapters every 45 s, which is expected. The host log shows every load finishing in 1–5 s.

### Found by the lab
3. **`GUI.TextField` is stripped as well** (`GUI.Box`/`GUI.Button`/`GUI.Label` work). `Ui.cs` falls back to its own fields. `ui.panel` passes in every tour.
4. **Some chapters cannot be started cold.**
   - Scene 19 (Glasses) gives a `World.Update`/`WorldPlayer.Update` NRE every frame on the host too, whether jumped to directly or from the main menu.
   - Scene 20 breaks only when jumped to straight from Scene 18 (2D); from the menu it is clean.
   - Fix: hook `Scene_Load.StartLoad` and relay it (`Op.SceneLoad`), so clients run the game's own transition. The direct `SceneLoading` load is only the late-join fallback.
   - The chapters tour now returns to the menu between chapters and skips Scene 19.
5. **The tour compared the client's PlayerMove root with the host's body.** In Scene 20's intro the body is 49 m from the root. Checks now compare body to body.

### Lab evidence (0.1.1)
| Tour | Result |
| --- | --- |
| connect | PASS (`ui.panel`: 546 frames, 0 OnGUI errors, field fallback) |
| avatars (Scene 1) | PASS: host 9/9, client 11/11, 0 game errors |
| avatars (Scene 20) | PASS: host 9/9, client 11/11, 0 game errors; teleport correctly refused during the intro |
| story | PASS in both directions |
| mita (Scene 4) | PASS, desync 0.0 m |
| chapters via menu | Scenes 1–18 loaded on the host with 0 game errors (2 loading warnings in Scene 6); client 41/41 (follow, grounded, model); Scene 19 fails cold (game-side) |

### Final chapters run (0.1.1, menu between chapters, Scene 19 skipped)
- **Host:** Scenes 1–18 load cleanly (0 game errors each).
- **Client:** 41/41 checks (follow, grounded and model in every chapter).
- **Scene 20 fails when entered after Scene 18 (2D), even via the main menu,** and on the host too: `World.Update` NRE.
  - A fresh start into Scene 20 is clean (avatars tour: PASS, 0 errors).
  - So the 2D chapter leaves static game state that the menu does not reset. This is game-side and only hit by cold jumps the lab makes.
  - Next tour change: put Scene 18 last in `Chapters`.

### Still not verified
- Real internet/VPN play between two PCs (only loopback so far).
- Long play-throughs where both players actually play (the tours drive scripted actions only).
- Minigames, held items, and UI-only prompts are not synced (see README limitations).

## 0.2.0 log (protocol 3)
Requested: all NPCs, doors plus player arms, cutscenes, a native main-menu server list with Steam friends, everyone restarting on death, minigames where one plays and the others watch, dialogue, and "the other player does it" for interaction animations.
Ideas cross-checked against QuestionBlock64's MiSide-Multiplayer (it has no license, so no code was copied).

### Built and what the lab proved
| Feature | Evidence |
| --- | --- |
| Full bone pose (68 bones) and props mirrored by visibility | avatars: mean bone error 0.18 deg, 3 props mirrored |
| NPC registry: Mita, Chibi, follow-Mitas, boss. Chasers left local | mita: desync 0.00 m |
| Mita face relay, gaze at the nearest player | mita: face replayed, gaze redirected to the nearer client |
| Cutscenes: every PlayableDirector time-synced; players hidden only in camera cutscenes | cutscene: within 0.036-0.099 s; visible during non-camera timelines (user request) |
| Interaction mirroring (toy plane) | carry: AirPlane in the host's hand on the client, client still, eventFinish replayed |
| Dialogue follows the host | dialogue: a host line started on the client; a finished line skipped |
| Shared key inventory | world: the Bow from `ObjectInteractiveItemTake.Take` arrived in the client's inventory (by name: scene-less copy) |
| Seeded randomness | world: fingerprint match. Scene 2 has no random placements, so the check passes trivially |
| Death relay (prefix) | death: Scene 11 mannequin checkpoint death replayed on the client |
| Minigame watch mode | minigame: card game owned by the host, 40 frames sent, client copy paused, StopGame replayed |
| Native MULTIPLAYER menu | connect: page built and opened; the rows show host and players |
| Steam transport | connect: `steam.ready` on both lab copies. A real two-account join is not verified |
| Saves | host mirror plus client backup and restore; run-tour backs up the user's saves |

### User reports fixed
- **Hidden during interactions:** players were hidden during interaction animations. Now only during real cutscenes.
- **Plane interaction:** the toy plane interaction did not show the actor taking the plane. Now it does (interaction mirroring).
- **Teleport onto furniture:** a client was teleported onto a bathroom sink by the spawn spread. Targets now need the host's floor height and head-room.

### Found by the lab
- **Saves:** `GlobalGame.dataFiles` is the Data folder; redirecting it broke voice loading. Saves are `<persistentDataPath>/Save`.
- **Stripped encoders:** `EncodeToJPG`/`EncodeToPNG` are stripped (the watch stream uses RGB565 + Deflate). `GUI.DrawTexture` works.
- **Menu rows:** `MenuLocation.Update` threw ArgumentOutOfRange after rows changed; re-running `MenuLocation.Start()` fixes it.
- **Steam DLL load:** `steam_api64` was not loadable by name early on. Resolve it from `Plugins/x86_64` and add Steam to the host when it becomes ready.
- **Methods that throw out of context:** `Location6_MitaKiller.Kill`/`StartAttack` and `Location10_ManekenChekpoint.PlayerDeath` throw when called cold, so death relays are prefixes.
- **Survey of chapters:** timelines exist only in Scenes 1, 3, 5, 6, 8, 12 and 13; there are no hand-held `ObjectItem`s and essentially no loose props.
  - Minigames: cards (4), dance (7), QuadLiner (13, 15), snake (14), tic-tac-toe (18), Tetris on the handheld everywhere.

### Still not verified
- A real Steam friend join between two accounts.
- Every minigame individually; the tour covers the card game.
- Every interaction animation; the tour covers the toy plane.
- A full real play-through with two people.
## 0.3.0 (2026-10-23): sync everything, documented first

### Documentation first
- **`inventory` tour:** loads all 19 chapters and lists every game script instance, every dialogue line (index, speaker, next line, voice, timing), every choice menu and every timeline.
- **Report:** `tools/inventory_report.py` turns it into `docs/SYNC-INVENTORY.md` (each type: synced, story-driven, local) and `artifacts/generated/DIALOGUES.md` (2311 lines with text, local only).
- **Master list:** `docs/SYNC.md`, with authority, mechanism, proving tour and work list.

### Sync added
| Feature | How |
| --- | --- |
| Interactions: mirror | Default rule `who=everyone`: the replayed click plays the same animation on every player's own character; same-spot bodies are hidden. Actor-only relays (finish, anim events, carry) only run for `who=actor`. |
| No mid-chapter joins | A joiner waits (`Peer.Waiting`, `wait-chapter`) until the host's next chapter; migration rejoins go straight in. 4-player cap. |
| Chapter start barrier | Every machine holds `timeScale = 0` from chapter activation until the host has every SceneReady (`go:<seq>`, 45 s timeout). The host announces the chapter on its loading screen, so loads run in parallel. Intro timeline offsets went from 6 s to under 1 s. |
| Dialogue | Lines registered by a `Dialogue_3DText.Start` postfix (no per-tick scan), streamed with their age; start lag measured (0.15 s mean in Scene 4); skip grace 0.8 s. |
| Visual novel | `Location18_Novella` position streamed; clients step along `nextDialogue`; client clicks forwarded. |
| Triggers and puzzles | One-shot `Trigger_MouseEvent.Enter`, `Trigger_DistanceCircle` flips, `Trigger_DistanceCamera`, `Basement_Safe.ClickButton`, `InputKeyboard_Main` keys (`Op.Call` table). |
| Minigame endings | The player's end method holds story relays; watchers fire the kind's end UnityEvents (cards, dance, tic-tac-toe, QuadLiner). |
| Scene animators | Layer-0 state in room sync; a client catches up if its own story has not reached the state within 1 s. |
| Host migration | Fixed: the successor compared against `self` after `Shutdown()` reset it. Verified with 3 players. |
| Panel | "Bring everyone here" for the host; the waiting count shows in the header. |

### Lab
- **MT-STATE:** every 2 s per machine, players (position, yaw, animation state, free, acting), peers, NPCs, active dialogue lines, playing timelines and counters. verify-lab.py lines machines up by wall clock and reports NPC distance, animation mismatch, dialogue mismatch, cutscene mismatch, timeline difference and dialogue lag.
- **Frame cost:** worst frame per window (`modmax`), and the worst single call per step (`spikes=`). Lab diagnostics no longer count as the mod's cost.
- **New tours:** `mirror` (toy plane), `interactions` (presses a chapter's interactions and checks mirroring), `novella`, `lobby` (4 players, late joiner), `migrate` (3 players), `inventory`.
- **One tour at a time** (`private/tour.lock`): two overlapping runs had killed each other's lab processes through the shared PID file.
- **Unity error excerpts** are kept per tour.

### Found by the lab
- **Scene 1:** its first quest is "take the phone in hand" ("Interactive Mobile" opens a phone view that waits for input). Other interactions wait until then, so mirror and interaction tests run in Scene 2 (door, item pick-ups).
- **Delayed animations:** interactions often start their animation a moment after the click, so the last replayed click's rule now applies to animations that start within 3 s.
- **Bone LOD:** players over 12 m away or behind the camera are posed every 4th frame. The host's cost with 4 players went from 2.1 ms to 1.5 ms per frame.
- **Teleport into mid-air:** a teleport landed 0.85 m off the host while the host was descending (Scene 6). Teleports now wait for the host's feet to hold a steady height for 1 s.
- **Look-and-hold:** the `Trigger_MouseEvent` hold timer is now relayed (the moment `timeHoldNow` reaches `timeHold`).
- **carry tour:** tests the actor-only rule on Scene 2's door. Every magnet object in early chapters is quest-gated, so magnet streaming was last proven with Scene 1's plane in an earlier build.
- **Scene 18 to 19:** the loader preloads with `StartLoad`, then switches with `GoScene`; `GoScene` is now relayed too. The `scene19` tour now starts at Scene 17 and lets the game's loaders carry everyone 17 → 18 → 19; everyone moves together at each step. With the loaders fired directly (not by playing those chapters to their ends), the game's `World` fails to set up Scene 19 on every machine. A single-player control run (`-Players 1`) fails the same way, so it is game-side. Ruled out as causes: `GoSceneAfterPause` instead of `GoScene`, saving first (`Scene_Load.SaveGame`), and the empty `GlobalGame` Mad Mita textures and screen capture. In Scene 19 the player object (`GameController/Player`) stays switched off: `GameController.StartComponent` throws before enabling it, so `World` finds no player (`scrpm` empty). A control run with the mod loaded but **no game hooks at all** (`-mt-nopatch`) fails identically, so the mod is not the cause. It needs state that only playing the earlier chapters through produces.
- **Dropped link (real bug):** a pose with a NaN made the host drop the client as malformed. NaN poses are no longer sent, a packet with an invalid number is skipped instead of dropping the player, and link closes are logged with their reason.
- **Reconnects:** a player reconnecting into the chapter they are already in goes straight back in (no wait for the next chapter, no start barrier).
- **Leak test:** sustained growth of the live-heap floor (first third, then second, then last of a chapter), not a single step when a player joins.
- **Internet-like conditions:** lab latency simulation (`run-tour.ps1 -Lag 120 -Jitter 40 -JoinAddress <LAN IP>`: each outgoing packet waits 120 to 160 ms in order, and clients join over the PC's network address, not loopback).
  - All 11 key tours pass under it: connect, story, mita, cutscene, dialogue, mirror, novella, minigame, death, lobby (4 players), migrate (3 players).
  - Mita 0.0 m apart, dialogue lag 0.16 s max, timelines 0.15 to 0.24 s apart (p90).
- **Held items, from the game's wiring (`HeldItems.cs`):** an interaction whose UnityEvent chain reaches a `Transform_Magnet` attach-to-player call is presser-only, and the item rides in their hand for everyone. No per-cutscene rules are needed; the `held` tour lists the verdicts (the plane and phone yes, the door no).
- **Magnet wiring (seen on the phone):** the magnet sits on the interaction object; `transformTarget` is the item, `transformMagnet` the player's slot (`RightItem FixPosition`), and `active` switches on at the animation event. Carry streaming follows that: an item counts as held when it is parented under the player, or pulled onto a slot on their body.
- **Scene 1 phone:** presser-only (the explicit mirror rule removed). Others see the presser pick it up with the phone in hand and cannot take another. In the lab, the host's pickup animation waits behind Scene 1's opening, so the live part of the `phone` tour depends on that opening.
- **Host-run cutscenes:** in a running chapter a client's own `Playable_Animation.Play` becomes a request; the host starts it and everyone plays it from the host's command. `hostcut` tour: the client's start became a request, the host ran it, and the client played it 0.13 s from the host's time.
- **Final results:**
  - Final installed DLL (SHA256 B8124DA8…): all 20 tours pass (connect, avatars, story, mita, cutscene, world, carry, dialogue, gather, room, mirror, interactions, triggers, novella, scene19, lobby, migrate, death, minigame, chapters).
  - carry also proves magnet streaming (the spoon on the host's body, then dropped in the same place), and triggers proves the distance and look-and-hold relays.
  - Leak test: the live-heap floor within one chapter (the heap after a collection); no tour shows a leak.
  - `chapters`: all chapters except 19; NPCs 0.0 m apart, dialogue lines differ in 4% of samples, timelines 0.11 s apart (p90) over 414 samples.
  - The 0.3.0 DLL is installed in the game.
- **Late chapter joiner:** a player arriving at a chapter from the menu with the host's `playWorld=True` never initialises its player (`PlayerMove` NRE every frame), so `playWorld` is now only carried over from a chapter.
- **Metric cost:** the mod object count (`Resources.FindObjectsOfTypeAll<GameObject>`) caused a heavy frame every metric window; it now runs once a minute.
- **Room-state scan:** it took ~80 ms in one frame in big chapters; it is now spread over frames, 1000 objects at a time.

## 2026-10-26: testing tools, published
- **Published** to https://github.com/multiplayereverything/miside-coop (source, docs, scripts; no game files, `private/` or `artifacts/`).
  Online play stays direct: Steam P2P through the game's own `steam_api64` (Steam relays when needed), LAN/VPN, or an address. It does not use the Multiplayer Everything global relay.
- **Second copy on one PC:** `scripts/second-instance.ps1` (added 2026-10-23, `-mt-second`): its own folder `private/lab-second`, the installed build, never the user's saves; `-Join` joins the local host.
- **Every chapter's interactions:** `scripts/interactions-all.ps1` runs the `interactions` tour per chapter and writes `artifacts/tests/interactions-all.md`.
  The tour now presses up to 25 interactions, logs `MT-PRESS scene|key|rule|played|distance` on the client, and checks `interactions.placed` (mirrored animations within 0.6 m of the host's spot).
- **Scripted cutscene timing:** `MT-ANIM` lines (every NPC/player animation state change as it happens); verify-lab pairs host and client NPC changes and reports `npc_anim_start_s_p90` and `npc_anim_missed` (fails over 1 s p90).
- **Research tracer:** `-mt-trace Type1,Type2` logs `MT-RVA` (native address of each method, for decompiling GameAssembly.dll) and `MT-TRACE` (calls while playing).
- **Lab robustness:** run-tour retries clearing a lab log for 15 s (a just-stopped copy held it and stopped the suite); build.ps1 keeps the last 10 installed DLLs in `private/plugin-backup`.
- **Found (not synced yet):** Scene 14's phone chat and PC (`Location14_GameQuestPlayer.ClickMouse`/`ChangeDialogue(bool)`/`EnterDialogueChange`, `Location14_ButtonChangeDialogue.OnPointerDown`, `Location14_PCFileIcon.Click`) and the Scene 13/15 core computer (`GamesCore_InputField` keys, `GamesCore_Main.OpenFolder`) are player input with no relay. Next: trace and decompile them, then a host-led sync like the Scene 18 novella.
- **Research:** other MiSide co-op mods (QuestionBlock64's on Nexus #778, WiSide MultiSplit #779) show players only; neither syncs the world, Mita or story.

### Every-chapter run (first results) and fixes
- **Suite:** 15 of 20 passed. world, dialogue, gather were over the 2 ms frame budget only because the Spooky and Baldi labs (6 game copies) ran beside it; run-tour now passes `--busy <games>` and the budget is reported, not judged.
  lobby: the host moved on 0.4 s after the late joiner's wait notice (tour race; the host now holds 8 s). phone: the host's click never starts the pickup animation (traced next).
- **interactions-all:** 9 of 18 chapters passed (Scene 18 left out: no 3D player; the novella tour covers it). Real findings:
  - **Story object missing (Scene 17 lever):** sibling order differs between machines under `Quests`. Fixed: `SceneKeys.Find` falls back to the index-free path when exactly one object has it (logged `Scene key matched by name`). Scene 17 now passes.
  - **Lost story events:** an event whose object was missing, or switched off here, was dropped silently. Now retried every 0.5 s for 5 s (`MT-STORY retried` / `MT-STORY dropped`), one retry per op and key.
  - **Hand animations:** `PlayerMove.AnimationHandPlay` was blocked for every replayed click; it now follows the same rule as body animations (mirrored clicks play it).
  - **Cartridge (Scenes 6, 10) and Scene 14 smartphone:** traced on both machines. The client runs the same `AnimationPlay`, and `BeforePlayerAnimation` applies the held-item rule (the item goes into the hand), so presser-only is correct. The tour now labels presses with the mod's own rule and the animation that really started.
  - **Tour fixes:** the spot is where the animation ends (players walk or teleport into place first); `interactions.free` compares with the host (Scenes 15 and 16 keep everyone in a scripted animation); presses that start a minigame are labelled `minigame`.
- **Scene 14 quest game decompiled** with REA (Ghidra) at the `MT-RVA` addresses: see docs/SYNC.md item 4.
- **`ObjectInteractive.Click` only fires when armed** (decompiled at `MT-RVA` 0x3fee40): it checks `active`, invokes `eventClick`, and disarms itself when `timeDeactive` is set. A replayed click on an object this machine's story has not armed yet did nothing, silently. Now it waits (story retry, 5 s) until the object is armed. The phone tour waits for the phone to be armed (the opening arms it); the interactions tour skips unarmed objects instead of counting them.
- **Only clicks that fired are relayed:** `ObjectInteractive.Click` gets a prefix that reads `active` first; an unarmed click did nothing on its machine, so it is not sent (it used to make the others' story retry or, before that, "apply" a click that did nothing). The `story` tour had been passing on such a no-op click in Scene 1; it now runs in Scene 2 and picks armed objects.
- **Tour animation detection:** a press counts as animating a character when an animation starts or the running one changes state (sitting at the Scene 15 safe, every button is the same long animation).
- **Cartridge (Scenes 6, 10, 13):** the client's character never plays it. The tour wrongly counted a client still in an earlier animation (Scene 13's PC seat) as playing; it now needs an animation that starts. The mod logs `MT-HOLD` whenever it holds back an animation as another player's, with the rule, so the next run shows why. With the mod's own rule and the animation that started, the tour now labels it presser-only (the cartridge goes into the hand), and Scenes 6, 10 and 14 pass.

### Regression after the fixes (build E449A0B6, then 1ABCBA68 for the story tour)
- **Suite:** all 20 tours pass (story after moving it to Scene 2). phone: the game never arms the phone in the lab within 4 minutes (Scene 1's opening waits for something hidden windows cannot do), so the pickup is still unproven.
- **interactions-all:** 16 of 18 chapters pass (Scenes 1-11, 14-17, 20).
  - Scene 12 `DoorPhysic Exit`: the players stood on different sides of the door, the game sent them to different spots (4 m apart) and played different animations; the host stayed in a loop (state 1467812248), the client's ended and it returned to where it stood. Open: find out whether the door's side picks the animation and whether the host's loop waits for input.
  - Scene 13: the host's presser-only cartridge takes them out of the PC seat; the client stays seated in its own (mirrored) PC animation until it gets up. Expected with presser-only, but it leaves the two in different places.

## 0.3.1 (2026-10-26): first GitHub release
- `ModVersion` 0.3.1 (protocol still 4). Published as pre-release **v0.3.1** on https://github.com/multiplayereverything/miside-coop/releases with `MiSideTogether.dll` (SHA256 320A5944…) and `MiSideTogether-0.3.1.zip` (unzips into the MiSide folder).
- Smoke test on the release build: connect, story, mirror pass.
- From now on every version is a GitHub release (test builds as pre-release); the Multiplayer Everything Discord bot announces it.

## 0.3.2 (2026-10-26): Scene 12 door, avatar shading
- `ModVersion` 0.3.2 (protocol still 4; `Op.Call` 7 is new and only sent between same-version players).
- **Scene 12 exit door:** the host's cutscene reached the client before the client's story had switched it on, and the client's own timeline played on awake without `Playable_Animation.Play`. Cutscene starts are now retried for 5 s like other story events, and start when only the timeline is playing (`NeedsStart`). The host also reports every `ObjectAnimationPlayer` animation that starts on its own player (`Op.Call` 7); a client that has not started the same one within 1 s plays it (not for presser-only). Its "started here" check uses the client's own key (Scene 17 orders siblings differently) and counts every call, not only those already animating.
- **Avatar shading:** the copy of the first-person body received no shadows, so it stayed fully lit in shade (user report: flat and over-bright). Mita's toon materials are the same; she receives shadows. The copy now does too. Lab `MT-LOOK` dump (`src/Lab/LookDump.cs`) lists the materials, probes and lights of the local body, the copy and every other character once per chapter.
- **Interactions tour:** the host's spot is its last animating frame, like the client's (Scene 8's door ended in the state it had at the press).
- **Tests:** suite 21/21 pass; every-chapter interactions 17 of 18 (Scene 13 seat, expected).
- **Held items and the phone (user request):** interactions that put an item in the hand are detected from the game's own wiring (`HeldItems.cs`) and done by the presser only, with the item in their hand; the Scene 1 phone too (the others see the pickup and can't take another). Cutscenes in a running chapter are run by the host.
- **Release suite (DLL SHA256 D4896EBF…):** 22 of 23 tours pass, including chapters. `phone` fails only because Scene 1's opening never arms the phone in hidden lab windows (known; its rule check passes).
- Published as a full release **v0.3.2** (not pre-release), at the user's request.

## 0.3.3 (2026-10-27): new PC, Steam mock, core computer, Scene 14 quest game
- `ModVersion` 0.3.3 (protocol still 4; `Op.Call` 8-14 are new). Pre-release **v0.3.3**.
- **New PC:** the repo lives outside the game (`scripts/game-dir.ps1`: `MISIDE_GAME`, the folder above the repo, or any Steam library). BepInEx be.790 from the repo's own release. The first build no longer stops on pip's version notice under PowerShell 5.1.
- **First-run picker:** a fresh install stops in `SceneLoading` on `ChangeLanguageStart` (LANGUAGE/VOICE) with no error anywhere; lab copies answer it (`ChangeLanguage`, `ChangeVoice`, `Apply`), found with `-mt-dump SceneLoading`.
- **Lab windows (user rules):** start minimized as normal windowed apps titled "MiSide lab - <name>", restore small at the right edge (set once, `SetWindowPlacement` with `SW_SHOWMINNOACTIVE`), 480x270, `-screen-quality Fastest`, 30 fps (`-mt-fps`), BelowNormal priority. Minimized copies keep running (`runInBackground`); every tour passes this way.
- **Steam (Co-op Anything `networking-steam-spacewar`, mock pattern "mock Steamworks.NET, not the DLL"):** a failed reliable send was silently dropped; it now waits and goes out in order. `SteamMock.cs` stands in for steam_api64 in the lab (fake accounts 900000000+slot, rich presence in `private/steam-mock`, packets over localhost TCP, sessions held until accepted, every 4th send failing). `steam` tour: the client joins by `+mt_join` and 60 of 60 notices arrive in order with 68 failed sends; a mutant that drops failed sends fails it.
- **Core computer (Scenes 13, 15):** `InteractivePC` is presser-only with pov (playbook `how-coop-mods-work` 1.5: the initiator acts, the others follow); fixes the Scene 13 seat. Decompiled with capstone (Ghidra timed out on the 22 MB GameAssembly; no rizin on this PC): the text field is an on-screen keyboard; `ButtonClick` (with `indexButtonNow`), `ButtonBackspace`, `ButtonEnter`, `OpenFolder` (folders are data, sent as an index path) and the stages `StartFolders`/`StartVersionMita`/`StartIndex`/`StopProgramma` (2 s duplicate guard) are relayed, only from the operator. `core` tour: both text fields `1:1_,__`; a mutant without key relays fails it (a first, weaker check counted slots and let the mutant pass).
- **Scene 14 quest game:** `PlayerQuest.Move` inside `ClickMouse` and `ButtonChangeDialogue.OnPointerDown` (`sideChange`) relayed. `quest` tour opens the game on every copy (the phone chat before it needs input) and proves a click (0.00 m, relayed move counted; room-state sync alone also moves the character, so the count is what proves the relay).
- **Scene 1 tutorial (lab):** `Location1Tutorial.Update` (0x3209b0) fills the mouse and walking prompts from real input; the lab makes the same transitions (`indexHint` 1 to 2 to 3). The phone still isn't armed afterwards (open). `-mt-trace` on `Time_Events`/`Events_Data`/`Location1Tutorial` disturbs them.
- **Lab:** `MT-LOOK` is opt-in (`-mt-look`); NPC animation lag counts lateness only (`early` reported); interactions tour waits for the client's player.
- **Tests on 0.3.3:** suite 24/24 (with `steam`, `quest`, `core`); every chapter 18 of 18.
- **Co-op Anything:** PR cranebeaker/coop-anything#220 refreshed to v0.3.2 facts (commit bd91310).

## Unreleased 0.3.4 (2026-10-27): Scene 1 phone proven, Scene 14 phone character shared
- **Wiring dump (lab):** `-mt-dump <scene>` also writes `private-wires-<scene>.txt`, every persistent UnityEvent call in the scene (UnityEvent properties, arrays and lists of them, and game structs holding them such as `Time_Events.TimePoint`). It found the Scene 1 gate in one run, after tracing `Time_Events`/`Events_Data` had disturbed them.
- **Scene 1 gate:** `Location1Tutorial.eventYesQuest` (quest hints on) switches on "Interactive TShip Tutorial"; clicking it starts the T-shirt dialogue, whose `eventFinish` arms the room's interactives (`ObjectInteractiveGroup.ActivationAndDestroy`), the phone among them. `eventNoQuest` (hints off) arms them at once. The lab now clicks the T-shirt after the walking step.
- **`phone` tour passes** (first time): phone armed 37 s into the chapter, the host holds it, the client sees it in the host's hand (1.39 m), does not pick one up and has none left. The host's T-shirt click is replayed on the client, which needs no click of its own. Added to the suite.

- **Scene 14 phone character (`ModVersion` 0.3.4, `Op.Call` 15, protocol still 4):** the phone shows a toy house where the holder walks a little character (`Location14_PlayerMobileMove`, keys into `hMove`/`vMove`, read by `FixedUpdate` at 0x33ed50) to "Trigger PlayerMobile". On the others, the story's catch-up of the "CheckUp" animation called `Play` too, and their copy threw a NullReferenceException in `FixedUpdate` every physics step (734 errors in one run) and would walk from their own keys. Now the holder (whoever clicked the toy Smartphone) runs it and relays `Play`; the others show it without running it (`Play`/`Update`/`FixedUpdate` skipped) and place it from the prop stream (kinematic there, so placed with `MovePosition`); a watcher's copy never streams (it had fought the holder's: the echo guard then stopped the holder's own updates).
- **`mobile` tour:** the lab walks into the save point first (clicked first, the phone pickup is replaced by the save point's ring animation 66 ms later, found with `-mt-trace`, same with `-mt-nopatch`), picks up the phone, and walks the character through both toy doors by setting its keys after the game's Update (`LabLateTick`). The client shows it 0.12 m from the host's, 2.7 m into its walk, and runs the trigger's story step; a mutant without the watcher handling fails.

## 0.3.5 (2026-10-28, local): the friends' playtest
- **Report (user, 19 items):** phone held "on the side" for others, one phone holder, name typed by the host, the app's dialogue/days/board in sync, black screen after putting the phone down, rooms unloading, hands in the main menu, leaving to the menu, the exit button, plus Mita/doors/objectives from the host's world. Screenshots showed a client `DialogueChanger.ClickButton` NullReferenceException and NavMesh "SetDestination" spam (Scene 3), and differing core-computer screens (Scene 13).
- **Found with the new lab screenshots and `scene1` tour:** a client became "owner" of `Location18_TicTacToe` when Scene 1's tutorial hint faded (IL2CPP shares one compiled body between identical methods, so the minigame hook ran for the hint); from then on the host's phone pickup was blocked. All minigame and phone-character hooks check the real type now.
- **Scene 1 app:** made host-driven (AppSync.cs, `Op.Call` 16-23) after reading the wiring (`-mt-dump`) and decompiling the app's dialogue (0x3d81a0, 0x3d83c0). Two wrong first tries are worth remembering: blocking `NextDialogue` on clients also blocked the game's own first line (only Space's `KeyNextDialogue` is blocked now), and forcing lines by setting `stringDialogueNow` skipped the game's text handling (the client showed a later line in Russian); the client now steps like Space once its own line has typed out.
- **Rooms, menu, carry, choices, NavMesh:** see HANDOFF "Done in 0.3.5". `menureturn` (player left in the menu) and `phone` (0.12 m from the hand) prove their fixes; `choice` passes but its mutation passed too, so the choice and NavMesh guards are unproven in the lab.
- **Not done yet:** day 5's mini-games and later days in the `scene1` tour; the core computer's differing screens (Scene 13); Mita/doors/objectives (items 13, 14, 17) need a concrete repro; gathering players before big events (16).
- **Lab note:** other sessions' work (Flash labs, a Rust build) loaded this PC to 100% CPU at times; runs then show 0.1-10 fps and fail frame budgets. Functional checks still hold; rerun budget failures when the PC is quiet.

- **Later the same day:** `menuleave` showed that a client exiting to the menu was still pulled back: the game's exit goes through the loading scene and the follow logic reloaded the chapter before the menu was reached. Now `GameController.ExitGame` is hooked (the mod's own call when following the host is flagged). `roomhold` proves rooms stay on around a client (fails without the fix). `scene1end` plays day 37 to its end: day 37's `Scene_Load.GoScene` only switches to Scene 2 if day 5 started loading it (`StartLoad`), so the lab's jump does both; everyone arrives in Scene 2. `core` now types the right version and compares both computers after Enter (same screen, "Bingo!"); the playtest's "PORTAL ACTIVATED" difference did not reproduce. The scene1 NPC animation lag (a step's chain about 1 s at the name and the tidy-up's end) gets its own limit (1.5 s, `NpcLagLimit`); app catch-up now finishes a typing line the game's way (all but the last character: Update appends before it checks).
- **Leak check fixed (lab):** `live` was `GCMemoryInfo.HeapSizeBytes`, the heap's size at the last collection with garbage included; it grew with the allocation rate, so long 2-player runs (`scene1`, `menuleave`) were flagged as leaking. Controls: with hooks off the number stayed flat; host-only with hooks it stayed flat; with a client it rose 50 -> 76 MB. A full collection once a minute (`GC.GetTotalMemory(true)`) shows 11.1 MB held all chapter long on both machines, so `live` is now that. `-mt-noshots` turns lab screenshots off.
- **Scene 1 played through (lab):** the `scene1` tour now drives every day as a player does (app buttons, the games list, a game the app has the energy for, the furniture search, the wardrobe, the present) and ends with both copies in Scene 2. Day-2 rules had to be limited to day 2 (the shop pointer and food category stay on later and the host kept toggling them).
- **Scene 3 opening (lab, `scene3`):** step into Quest 1 Start/TriggerForShowMita (it switches on its own child TriggerLook; another object of that name sits in Quest 3), look at it, look at TriggerSideMita; Mita walks to her seat and the walk quest starts on both copies; then the host talks to Mita (InteractiveMita opens her DialogueChanger) and picks an answer. The client applies it with no exception and logs no SetDestination error, but both also hold with the choice and NavMesh guards switched off: the playtest's errors came from a state the lab does not reach (likely a client away from Mita), so the guards stay unproven.
