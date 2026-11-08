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

## Unreleased (2026-10-28, after v0.3.5): story autopilot
- **`storypilot` / `storypilot-all.ps1`:** the host plays any chapter as far as simple player actions take it (answer choices, press armed interactives, look triggers and trigger volumes under Quests/Acts); every 30 s the client must have the host's quests. First run over 18 chapters: 17 stayed together; Scene 1 did not.
- **Two fixes from it:** (1) a player's own press is noted in the `ObjectInteractive.Click` prefix, before the game's click code: the host's own phone pickup started its animation inside Click and was held back as "another player's" because the client happened to be animating; (2) clients no longer start a host's timeline that belongs to a presser-only interaction animation (Scene 1's phone timelines ran the phone's story on the client alone, a quest ahead). After: Scene 1 stays together; `phone`, `held`, `mirror`, `carry`, `interactions`, `scene1` pass.

## 0.3.6 (2026-11-06): Scene 1 phone desync
- `ModVersion` 0.3.6 (protocol still 5). Pre-release **v0.3.6**. Code change since v0.3.5: only `d5c30b4`, the two story-autopilot fixes above.
- **Tests:** `phone`, `held`, `mirror`, `carry`, `interactions`, `scene1` and `storypilot` in Scene 1 passed on that code on 2026-10-28. The full suite was not rerun for this release, because the user's own MiSide was running and the lab refuses to start beside it.
- **Launcher install seen working (user, 2026-11-06):** it installed 0.3.5 and the game loaded it. It keeps the old DLL in `BepInEx\plugins\MiSideTogether\.installer\backup\`, which BepInEx also scans: the log shows "2 plugins to load" and "Skipping [MiSide Together 0.3.4] because a newer version exists". Harmless while the backup is older. The installer belongs to the launcher, not this repo.

## 2026-11-06: first session on 0.3.6, remake requested
- **Session:** the user hosted, "lidar" joined, Scene 1 loaded, chapter start "held 9.0 s (everyone loaded)". Host logs and the user's screenshot are in `private/playtests/2026-11-06/` (`Player-prev-host.log` is the session before it). The friend's log was not collected. The host's mod log has no `MT-MODERROR` and no errors after the chapter loaded, so the reported bugs don't show there.
- **Screenshot (`screenshot-1.webp`):** a player's body seen from above beside the bed in Scene 1, holding the phone low in one hand, in a half-sitting pose with the legs against the bed frame. Which player took it and what they expected were not stated.
- **`MenuMita.Update` NullReferenceException:** 13 and 27 lines in two sessions, in `Player.log` only. They come as the game leaves the main menu (right before the menu unloads in the session with lidar), not from hosting (first reading, wrong). The method is the game's main-menu Mita (RVA 0x4ea820); the mod does not reference `MenuMita`. It reads cameraT, headIk and eyesIk, and a null or destroyed one throws. Which one is not known, and no control run without the mod has been done. The lab has never logged it, though it goes through the menu; whether tours leave it the way a player does was not checked. The session with 27 lines ended right after them, with no crash report or Windows crash event, and whether the user closed the game is not known. Treated as harmless, nothing changed.
- **Finding a game method's address without a lab run:** REA `inspect_il2cpp_metadata` on `global-metadata.dat` plus `GameAssembly.dll` gives every method's RVA (`inspect_analysis_view`, collections `types`, `methods`, `fields`, `method_addresses`; `declaring_types` takes type indexes). `capstone` is now in `private/venv` for reading the bytes. `Game SHA256` in the mod's log is `GameAssembly.dll`'s, not the exe's.
- **Launcher path:** the launcher reads `multiplayereverything/mods-info`; its `releases` workflow writes `releases.json` (scheduled every 10 min, in practice every 40-60 min; pre-releases are listed). 0.3.6 was running in the user's install by 06:35, shortly after release; how it got there (launcher or by hand) was not checked.
- **Autopilot (tryaura.dev) can't test MiSide:** it needs a Unity 6, Unreal 5 or Godot 4 project open in the editor, and MiSide is a shipped Unity 2021.3 IL2CPP build. The user decided against uninstalling it, then its MCP server went away by itself.
- **Hub worktree:** the shared checkout `%USERPROFILE%\.claude\coop-anything` is used by other sessions at the same time: work in a `git worktree`, never switch its branch (this session did once and put it back).
- **Remake requested:** see "Next" in `docs/HANDOFF.md`. Nothing started.


## 2026-11-06: remake plan and whole-game study
- The user asked to remake the mod with GMTogether-inspired sync; plan in `docs/REMAKE-PLAN.md` (Option A: free roam, one host story log, one Main Character per story moment).
- Before any code, the user asked to study the whole game ("each detail each script"). Done as Step 0; results in `docs/GAME-STUDY.md`, details in `private/game-study/` (never distribute).
- Method: REA metadata export, then Ghidra headless with `-noanalysis`, naming every method first (`tools/study/`): 9,163 methods, 0 failures, about 7 minutes. Full auto-analysis through REA timed out at 330 s.
- Ghidra's `analyzeHeadless.bat` breaks on a path containing `(x86)`: copy the DLL first.
- `MetadataRegistration`: `typesCount` at the address REA reports as `types_field`, `types` at +8, `fieldOffsets` at +0x28 (6281 entries, matching the type count). Exact field offsets come from there.
- Read tool truncates very long lines; `fields.txt` lists each type's fields one per line.
- `held` tour ignores `-Chapter`: `scripts/dump-scenes.ps1` uses `interactions`. Scene 19 dumps through the `scene19` tour.

- 0.3.7 (protocol 6): session logs for real play (`src/Core/SessionLog.cs`, `Op.SessionLog`). Streamed rather than sent at the end, so crashes and a host quitting first lose little. Each log opens with the settings that can split two copies (language, quality, VRAM, physics step, holiday flags). `sessionlog` tour passes; with `-mt-nosessionsend` it fails ("host received 0 chars"). `connect`, `steam`, `migrate`, `avatars` pass on this build. The profile folder is scrubbed to `~`. Not yet run over real Steam.
- First real data from the header: a lab copy reports `language=Russian voice= quality=0 vramMB=10053 fixedDt=0.02`.

- Baseline suite on 0.3.7 (2026-11-06): 33 of 35, 0 `MT-MODERROR` in 105 logs. `cutscene` failed 3 of 3: since d5c30b4 clients skip presser-only timelines, and the tour picked one (the phone). The test was stale, so it now measures Scene 1's opening cutscene, which the story starts: within 0.03-0.10 s of the host, 3 of 3. No full-suite rerun after that test-only change. `menuleave` failed once (dialogue lines differ in 45% of samples), then passed 3 of 3 alone: intermittent, cause not found.
- `scene19` logs 198 `NullReferenceException`s each on host and client, in the game's `World.Update` and `WorldPlayer.Update`, right after "Switched to the preloaded chapter with the host" (first error: `GameController.StartComponent`). The tour allows game errors. Not investigated; check it when Scene 19 moves to the new sync.
- A story event for an object still off on the client is retried for 5 s, then dropped (`MT-STORY dropped`). Forced by a test here, but with self-destroying quests that can strand a copy. The story log must wait instead.

- 0.3.8 (protocol 7), Step 2b: `src/Sync/World/SessionRules.cs`. The first `samestart` run proved nothing: with `-mt-norules` the flag and save checks still passed, because the client joined from `started` before the tour's differences applied. Always run the mutation. Fixed run: 0 object differences of 2,968 with rules on; 258 extra and 8 missing on the client with rules off (date flags spawn `(Clone)` decorations and the host's lights differ).
- `Options_QualityWorld` and `Options_GameObjectMemoryVideo` are on no object in the 24 scene dumps, so the study's "different objects per PC" rows for quality and VRAM were wrong, and those rules weren't built.
- Real bug found: `SendSavesTo` only overwrote files, so a client kept its own `Flashes`, `PeaceMode` and `FinishGame` when the host had none. The rules packet now lists the host's saves and a client sets the rest aside, after a backup.
- Step 3, `src/Sync/Story/StoryLog.cs`: `UnityEvent`s can be swapped through their interop setters for a stand-in `new UnityEvent()` with a managed listener (`DelegateSupport.ConvertDelegate<UnityAction>`). It works for inlined invokes and coroutines (`Time_Events`), with no hook per source. 110 events gated in Scene 1. A destroyed source's real event can still be invoked.
- The first full `scene1` failed where the old relay passed. A press can hide its own button, and the release (`ButtonMouseClick.PointerUp`) is what opens the next panel. The old relay's 5 s drop hid this because later events still ran; the ordered log waited on it and everything behind it stalled. Releases no longer wait.
- The tutorial's yes/no was each player's own, so the host's T-shirt press stalled the client's log for 30 s. `Frame EnterTutorial` buttons are now host-only like the phone app, and a client still on the Russian-only style frame skips it when the host answers.
- `inputhook` tour: a Harmony prefix on `Input.GetKeyDown(string)` answers the game's own native calls (177 calls in 6 s; `Interface_KeyHint_Key.Update` pressed itself only when forced). True lockstep is still out, but input routing is available.
- Step 4, `src/Sync/Story/StoryCheck.cs`: `SendWorldStateTo` doesn't bring back a quest switched off on a client, so repair matches the host's quest list directly. `ScreenCapture.CaptureScreenshot` works under IL2CPP (a 200 KB png was written).

## 0.3.9 (2026-11-07, local): Scene 2 on the story log
- `ModVersion` 0.3.9, protocol 8 (`Op.Gate` can now be a client's request). New gate kinds: Host, Body, Press, First (see `docs/REMAKE-PLAN.md`). Lab flags: `-mt-logalso <chapter>`, `-mt-pilotfull`, `-mt-pilotboth`, `-mt-nogaterequest`. New tour `scene2`.
- A pass with the pilot stuck in Quest 2 meant nothing, so in full mode `pilot.acted` now requires leaving the chapter.
- Scene 2's rooms (`Toilet (off)` with its floor and animator) switch on only near a player, so their animator events run only on that player's PC. This is also why `TeleportPlayer` does nothing there: its downward ray finds no floor. Hence the First kind.
- The game's asset sweep (a chapter preload runs one) follows only what scene objects hold. A clip named only by a real event held by the mod got unloaded, and `AnimationClipSimple` threw on a null clip in Quest 4 on both PCs. The real events are now also held by a switched-off `Animator_Functions` on a hidden object.
- A game listener that throws no longer stops the log; before, the next tick ran the same event again.
- `HeldItems` read the wiring through stand-ins and found nothing (`held` failed); it now reads the real events.
- Results: `scene2` 195 applied, 0 lost, 0 waits, both copies in Scene 3. With `-mt-nogaterequest` it stalls at the scarf, as it should. Solo reaches Scene 3 with only the game's own `ColorAnimationDestroy` error at the toilet, from the pilot's order.
- Full suite: 38 of 40, 0 `MT-MODERROR` in 84 logs, the only desync line from the `desync` tour. My first count of this covered only 4 logs: the copy step filtered on folder dates, which don't change when a log inside is overwritten. Recounted by file date.
- `phone` failure was a real bug from the Body gates: `ActivationParent` ran on the watcher, parenting the phone under the watcher's own slot. `HeldBy` then said the watcher held it, so both copies sent Carry; the host placed the phone on the client's avatar and sent back a pose relative to its body (0.87 m from the hand). `ApplyGate` now undoes changes to magnets aimed at this player when the moment isn't this player's. It restores afterwards rather than hooking, because `Transform_Magnet.Activation` shares its compiled body with an unrelated setter. Now 0.11-0.12 m.
- `carry` failure was a stale check: `carry.finished` counted only the old `InteractionDone` relay. It now counts `bodyend` gates too.
- After the fix `phone`, `carry`, `held`, `scene1` and `scene2` passed. `phone` and `carry` first failed the frame budget (15.35 and 3.15 ms, 9 and 24 fps, one 15 s stall, in steps the fix doesn't touch), then passed at 30 fps and about 0.7 ms with no code change: the machine was busy. No full-suite rerun after this fix.
- Not tested: real players, real Steam. Logs: `private/playtests/2026-11-07/`.

## 2026-11-07: one Main Character per moment, Scene 3 on trial, faster tours
- Moment owner (`momentMine`): the last press in the log, on every PC. The 3 s window it replaces let `DMita 5`'s line end start the selfie on both PCs. The watching client, now "animating", answered a leftover Glasses prompt, which made the host hold its own selfie and stick. The owner must be set before the click runs: set after it, the host's phone press was treated as someone else's, and `phone`, `carry` and Scene 3 all broke.
- `Interface_KeyHint_Key` takes a prompt down inside `KeyDown`, on the PC that pressed it, so answered prompts stayed up elsewhere. Also new: a press from the log runs on an object not armed on that PC, `Player_Position` skipped on watchers, and a player locked by `dontMove` may answer a prompt.
- `mirror` is run with `-mt-nolog`: everyone playing one animation is what a log chapter now forbids, so its pass in Scene 2 had been the bug. Every-player mirroring remains for chapters not yet moved.
- Tours: full suite 65 -> 46.1 min measured (40 tours, 37 passed, 0 `MT-MODERROR` in 82 logs). `mita`, `gather`, `mirror` and `scene1` were then fixed or sped up and rerun alone (41, 41, 36, 357 s), giving about 42 min by adding up; not rerun as a whole. Target was 35. Where the 65 went: 25 min waiting on chapter timers, 16 startup, 14 story between checks, 10 after the last check.
- What changed: a head start on the shared tour clock (4 s at the menu, 15 s in a chapter) instead of about 50 separate edits, logos and loading at 4x, 1 s polling, 3 s at the end unless a client is still following, limits at twice a passing run, a 50 s stall stop (longest quiet stretch measured in Scenes 1-3: 34 s), `lab-suite -Quick`.
- Waits that turned out to matter: `MenuNextLocation.Click` throws if pressed 2 s after the menu appears; `mita` must wait for the client or it misses the face change; `held` needs a client to finish following the host.
- Chapters at 3x game speed rejected: `scene2` took 100 s against 161, not half, and `TimeInToilet` threw on both PCs, which it doesn't at normal speed. Removed.
- `gather`: the client's cutscene flag stays 0 while the host's is 1. Same on 0.3.8: the tour forces the cutscene on the host alone. The shorter run pushed it over the 30% limit, so that comparison is off for this tour only.
- A lab host copy hung on the logo screen for the second time, cause unknown (507 s lost, its log not kept). The runner now stops within about 60 s when a copy is stuck there. Also seen: a lab client access violation at 03:16 with 632 MB of RAM free, `ruffle_desktop` holding 8 GB; not proven to be the cause.
- Mutation after the speed-up: `scene2` with `-mt-nogaterequest` fails at the stall in 150 s.

## 0.3.10 (2026-11-07, local): Scene 3 on the story log
- `ModVersion` 0.3.10, protocol 9. `scene3full`: all seven quests, 717 events applied, 0 lost, 0 waits, 215 of 215 lines within 0.5 s, both copies reach Scene 4, 651 s. Also passing after listing it: `scene3`, `choice`, `dialogue`, `connect`, `storylog`, `desync`, `phone`, `carry`, `held`, `menureturn`, `scene2` (12 tours, 23.2 min, 0 `MT-MODERROR`). No full suite on this build.
- Real bug, any chapter end: the story moves a client into the next chapter just before the host's word of it arrives, and following the old word took it back (Scene 3 to 4, 24 ms apart; Scene 1 to 2 had escaped by 1 ms). After a scene change here, the host's word now gets the same 3 s grace a new word gets.
- Real bug, old relay: `Dialogue.cs` compares keys with sibling numbers, and lines destroy themselves as they play, so a client's `DMita 9#0` counted as finished when the host showed `DMita 9#4` (153 lines cut short in one run). Off in log chapters. The same comparison remains in chapters not yet moved.
- "When Mita is ready" events fired from each PC's own Mita arriving: lines 10 s late and 12 missing on a client. Now through the log.
- `Location3CutCarrot` and `Location3WalkToToilet` were not synced at all: raw input on each PC, and a finish event that advances the story. Now they run only on the Main Character's PC with a Moment gate. `autoCut` and `timeWalk` let the pilot play them; the game clears `autoCut` after each cut.
- The dialogue check measured the wrong thing: MT-STATE samples are 2 s apart on each copy's own clock, so 46% of samples "differed" while 182 of 190 lines started within 0.5 s. It now compares start times from `MT-LINE`, and is stricter: over 1 s late or missing counts. Mismatch 46% -> 12% -> 0% as the bugs above were fixed.
- Pilot: skips prompts the game has hidden, and takes back an object the client didn't press.
- Open: see "Step 5 notes" in `docs/REMAKE-PLAN.md` (Mita's animation changes unmatched 53%, Mita's position when a client walks to the toilet, hall objects off on a client). Not tested: real players, real Steam.

## 2026-11-07: fixes from playing the opening (in progress, see docs/HANDOFF.md)
- "Mita matches to 0.00 m" was false. The check measured `MitaPerson`, an empty parent; her body is the child with the Animator and NavMeshAgent. Measured on the hip bone: 2.12 m. A first fix then passed at 0.0 m only because she stands still most of the time: judged at five samples a second while she moves, 2.75 m, and frozen for 30 s after her key changed. Now 0.14 m (`mitasame`, under 2 minutes).
- Doors: a rotation written onto a non-kinematic hinge is thrown away by the next physics step. `angleNow` also stays at zero on the copy that only receives, so the test reads the transform. A door moved by another player is now kinematic here and glides, released after 0.5 s.
- The console opening by itself is the game's AFK easter egg, not sync. Two windows make it look random, because one is always idle.
- `Trigger_MouseEvent` looks that weren't `oneTime` stayed local; Mita Stand's look starts her free roam. In a log chapter, looks under `/Quests` or `/Acts` now go through the log. Repeated hover-style looks there haven't been checked for noise.
- `run-tour.ps1` now deletes old results first: a crashed verifier had shown the previous run's verdict as if it were new. `LightmapSettings.lightmaps` throws TypeLoadException under IL2CPP.
- Not tested yet: any of this by real players, and the wider tours against the new 0.75 m NPC limit.

## 0.3.11 (2026-11-07, local): the five fixes from playing the opening
- `ModVersion` 0.3.11, protocol 10: Mita's stream now carries her body, not her parent.
- Lighting: `ObjectDoor.DoorClosed` switches off `objectsRight`/`objectsLeft` (rooms and their lighting groups) on the far side of the local player (`playerT`), unless `offOptimization`. Each PC hid the other player's room, and the room-state stream copied the host's choice. In a session every door sets `offOptimization` and switches both sides on once. Red then green: `door` with the client across the door, 72% then 0%; `scene3full`, 20% then 0%.
- Doors: a shut door's component is switched off by the game, so `DoorTick` skipped it and its closing angle never went out (client stuck at 20.5 degrees). It now checks the GameObject only.
- Notice: "All players must be here for this cutscene." on every PC when a story cutscene starts. `useCamera` was the first idea, but Scene 1's day cutscenes have it false. Teleporting first isn't built: those cutscenes put everyone in place (`allhere.brought` passed with no new code), so there was no failing test for it.
- The Scene 1 pilot's faster pace (from the tour speed-up) pressed app buttons again before the screen changed; each repeat made the client wait 12 s. Restored to its old pace. Real-play note: fast repeat presses in the phone app can do the same.
- Check fixes: lighting compares only while both copies are in one chapter; the coarse NPC limit is 1.5 m (two-second samples can't judge a walk), and MT-BODY at 0.6 m does; `roomhold` keeps a room on for the client on purpose, so it skips the lighting compare.
- Results on 0.3.11: `mitasame`, `door`, `afk`, `allhere`, `room`, `world`, `menureturn`, `gather`, `carry`, `scene2`, `scene3full` (0 lost, lighting 0%) pass; `mita`, `desync` pass on rerun (a face set 0.14 s before the client arrived; a client copy that never started). `scene1` lost one app event in one of three runs. No full suite on 0.3.11.

## 0.3.12 (2026-11-08): Scene 2's scanner
- Report (relayed from the launcher session): taken items stuck in players' hands at "Bring these to the scanner". The user asked that every taken item be put on the scanner, as the game intends, tested only for that, then released.
- In a session, taking an item that arms a scanner slot (`GiveItems/<item>`, read from the take's own click event) presses that slot for the taker once they are free (`Interactions.cs`, `GiveTick`). The game's own drop animation then puts it on the scanner, through the story log.
- Test first: the `scanner` tour (Scene 2; the client takes the Spoon and Bowknot, the host the Pencil, nobody clicks the scanner) failed before the fix (all three items 8.85 m from the scanner) and passes after (all on the scanner on both PCs, same place, no item in any hand). `scene2` passes. A first version of the tour, which also clicked the scanner, passed without any fix: the stuck item didn't reproduce through code clicks.
- `ModVersion` 0.3.12, protocol unchanged (10).

## 0.3.13 (2026-11-08): a player's press reaches the mod
- 0.3.12's scanner fix didn't fix real play. The real session of 2026-11-07 (three players, `private/playtests/2026-11-08`) has no `Interact` entry anywhere: no player press ever reached the mod's `ObjectInteractive.Click` hook. GameAssembly has no caller of `Click` (scan of the code sections for call/jmp to 0x3fee40); `ObjectInteractive.Update` reads `GetMouseButtonDown(0)`/`GetButtonDown` and runs its own copy of the click (`eventClick.Invoke`, then the destroy/deactivate steps). Lab tours call `Click()`, so they never saw it. In real play every client press of an ObjectInteractive ran on that PC alone; only side effects with their own relays (key items) reached the others.
- Fix (`Patches.cs`): in a session, a press read inside `ObjectInteractive.Update` is taken away and `Click()` runs right after that Update, so it goes through the story log (a client's press is a request) and the scanner's auto-place.
- Test first: the `scanner` tour now presses like a player (aims `objectCastInteractive`/`objectCastHandInteractive` and holds the mouse for that object's Update; the game's own `Update` does the rest, after waiting while `animationRun`). On 0.3.12 it failed like the real session (host's button and take ran on the host only, items 8.85 m from the scanner); with the fix it passes (presses logged, client takes requested, all three on the scanner on both PCs). Quick suite: all pass, `scene2` once stalled at Quest 5's battery and passed on rerun.
- `ModVersion` 0.3.13, protocol unchanged (10).

## 0.3.14 (2026-11-08, local): stable keys, batteries, debug page
- Scanner: 0.3.12's auto-place is gone (it snapped players onto the scanner). Players carry the item, walk over and place it; since 0.3.13 that press reaches everyone. `scanner` checks the item waits for the player (fails with the auto-place).
- Real session 01:19 (0.3.13, user and lidar): the client's story stalled 12 s per entry at the battery quest's door (`TriggerStopDoorTakTak`, logged three times) and fell behind: the dark corridor, and 31 refused battery presses. Cause: scene keys carry sibling numbers that drift as the story spawns and destroys objects, and the name fallback was ambiguous (two `Quest 3` objects, each with that trigger). Fix: every object's key is recorded as the chapter loads, the same on every PC (`SceneKeys.RegisterItems`; 1,460 objects in 12 ms). `scene2`'s new `pilot.keys` check: 63 keys found only by name before, 0 after (spawned `(Clone)` objects excepted).
- Batteries: the fridge and server hand over the battery prefab itself (`AddKeyItem(ItemBataryServer)`, key `/ItemBataryServer#0`); clients missed every battery (6 in `scene2`). Now found among loaded assets. Watch: each battery take is logged twice on the host (local and the client's copy); not yet shown to leave an extra battery.
- A `doorrepeat` tour was written and removed: it never reproduced the stall (the lab destroys the trigger on both PCs), so it proved nothing; `pilot.keys` covers the cause.
- Story pilot presses like a player (`LabPress`, shared with `scanner`), and compares key items as well as quests.
- `docs/INPUT-AUDIT.md`: every place the game reads input itself (scan of GameAssembly, `tools/study/native_callers.py`/`native_callees.py`). Inline, not reaching the mod: `Dialogue_3DText` skip, `ObjectAnimationPlayerHold` (Scenes 4, 6, 9, 17), chapter mechanics for the sweep.
- F5 debug page (`UI/DebugPage.cs`, `Op.Status`, protocol 11): every player's chapter, quests, story position, what they wait on and ping; skip a stuck story step; save a bug report on every PC. A press the host refuses is now answered with a notice. `debug` tour: rows, waiting, skip (0.5 s instead of 12), refused, report pass. The page's layout isn't in lab screenshots (they read the camera, not the IMGUI overlay).
- `ModVersion` 0.3.14, `Wire.Version` 11.
