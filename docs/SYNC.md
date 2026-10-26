# What MiSide Together syncs, and how

This is the master list. The per-chapter detail (every game script type in every chapter, with its sync status) is generated into [SYNC-INVENTORY.md](SYNC-INVENTORY.md) by the `inventory` tour plus `tools/inventory_report.py`. Every dialogue line is in `artifacts/generated/DIALOGUES.md` (local only, since it contains game text). Per-cutscene tuning lives in [../CUTSCENES.md](../CUTSCENES.md).

**Model.** Every machine runs its own copy of the game. The host is the authority for the chapter, the story, NPCs, dialogue and the room. Players are each the authority for their own body. Anyone can drive the story: clicks, choices and triggers from any player are relayed to everyone.

**Lobby rules.**
- At most 4 players.
- Players can't join mid-chapter. A player who connects while the host is in a chapter waits in the menu and comes along when the host starts the next chapter (or returns to the menu).
- After a host migration, the remaining players carry on in place.

## The sync surface

| Area | What | How | Authority | Proved by tour |
| --- | --- | --- | --- | --- |
| Players | Body pose, 68 bones, feet, camera, held props | `Op.Pose` at 15 Hz (int16 quaternions, Half positions); players over 12 m away or behind the camera are posed every 4th frame | each player | `avatars` |
| Players | Visibility | hidden during real cutscenes, and when two players animate on one spot (mirror) | each machine | `cutscene`, `mirror` |
| Players | After cutscenes | gather at the host on safe floor | host | `gather`, `chapters` (teleport.safe) |
| Chapters | Which scene | `Op.Scene`; clients load through the game's own loading scene with the host's `GlobalGame` statics | host | `connect`, `chapters` |
| Chapters | Transitions | `Scene_Load.StartLoad` relayed | anyone | `story` |
| Chapters | Joining | mid-chapter joiners wait for the next chapter; 4-player cap | host | `lobby` |
| Story | Clicks | `ObjectInteractive.Click`, `Trigger_MouseClick.Click` | anyone | `story` |
| Story | Volumes | `Trigger_Event` enter/exit, once per group | anyone | `story`, `world` |
| Story | Looks / distance | one-shot `Trigger_MouseEvent.Enter`, `Trigger_DistanceCircle` and `Trigger_DistanceCamera` enter/exit | anyone | `triggers` |
| Story | Puzzles and typing | `Basement_Safe.ClickButton`, `InputKeyboard_Main` keys (`Op.Call`) | anyone | inventory coverage (no tour yet) |
| Story | Randomness | seeded `Random_*`, `Material_Random`, `Animator_RandomPlay`, `Location21_DialogueRandom` | deterministic | `story` (seed fingerprint) |
| Interactions | Animations (phone, plane, ...) | mirror: the replayed click plays the same animation on every player's own character, at the same spot. `who=actor` cases: carried objects, anim events and finish relayed | anyone | `mirror`, `interactions`, `carry` |
| Dialogue | 3D text lines | the host streams active lines with their age; clients start missing lines, skip finished ones (0.8 s grace), and measure the start lag | host | `dialogue` (lag in MT-STATE) |
| Dialogue | Choices | `DialogueChanger.ClickButton` | anyone | `story` |
| Dialogue | Skips | `Dialogue_3DText.SkipDialogue` | anyone | `dialogue` |
| Dialogue | Visual novel (Scene 18) | the host streams its position (dialogue, page, line); clients step along the chain; anyone's click reads on | host plus anyone | `novella` |
| Cutscenes | Timelines | every `PlayableDirector` time-synced; start relayed | host | `cutscene` |
| Cutscenes | Scripted (player and Mita animations) | follow from the relayed story event; Mita is puppeted by the host | host | `cutscene`, `mita` |
| NPCs | Mita, Chibi Mita, follower, boss | puppets on clients: the host's transform and animator at 15 Hz, face changes, gaze at the nearest player | host | `mita` |
| NPCs | Chasers (mannequins, killers) | local on purpose: each chases its own player; deaths are shared | each machine | `death` |
| Deaths | Restart | death prefixes relayed; everyone restarts together | anyone | `death` |
| Minigames | Ownership | one plays (the one who started); the others' starts are blocked | first starter | `minigame` |
| Minigames | Endings | the player's end events (cards, tic-tac-toe, dance, QuadLiner) fire on every machine; story events they trigger are not relayed from the player | the player | `minigame` |
| Room | Objects on/off, placement | the host sweeps every scene object and sends changes; snapshot for players arriving later | host | `room` |
| Room | Lights | on/off, colour, intensity, range | host | `room` |
| Room | Scene animators (drawers, TV, lamps) | layer-0 state; clients jump to it if their own story has not reached it within 1 s | host | MT-STATE `anims=` counter |
| Room | Doors | angle, last mover wins | anyone | `world` |
| Items | Key items, hand items | `AddKeyItem`/`RemoveKeyItem`; hand items shown on the holder | anyone | `story` |
| Saves | Continue file | the host mirrors its save folder; clients back theirs up and restore it | host | `connect` |
| Session | Host leaves | the lowest-numbered player becomes host in place; the others rejoin | next player | `migrate` |

## Testing and logs

- **Run a tour:** `scripts/run-tour.ps1 -Tour <name>` (2 to 4 players, hidden windows, stops as soon as its checks pass).
- **Run everything:** `scripts/lab-suite.ps1`. Only one tour runs at a time (`private/tour.lock`).
- **Checks:** `MT-CHECK <name> PASS|FAIL <detail>`, the tour's own assertions.
- **Health every 10 s (`MT-METRIC`):** fps, mod ms per frame (with the 4 most expensive subsystems), heap, errors, Mita desync.
- **State every 2 s per machine (`MT-STATE`):**
  - this player's position, yaw and animation state; free, acting, cutscene;
  - every peer's position and whether it is drawn;
  - every NPC's position and animation state;
  - the active dialogue lines;
  - each playing timeline and its time;
  - room and animator change counters, and the dialogue lag.
- **What verify-lab.py reports from `MT-STATE`:** it lines the machines up by wall clock and reports NPC distance (p90), NPC animation mismatch, dialogue mismatch, cutscene-mode mismatch and timeline difference (p90). Gross desync fails the run.

## Work list (priority order)

1. **Interactions on every chapter:** `scripts/interactions-all.ps1` presses every interaction in every chapter with two players and checks that both played it at the same spot and ended free. Cases that misbehave get a line in CUTSCENES.md.
2. **Scripted cutscenes:** compare Mita's animation start times per chapter (`MT-ANIM`, reported as `npc_anim_start_s_p90`).
2b. **Open from the every-chapter run (0.3.2: 17 of 18 pass):** Scene 13: a presser-only action (cartridge) leaves the other player in the mirrored PC seat until they stand up (expected with presser-only; the check flags it); the Scene 1 phone is never armed in the lab, so its pickup is unproven. Fixed in 0.3.2: the Scene 12 `DoorPhysic Exit` (cutscene start retried, and the host's story-started player animations caught up on clients, `Op.Call` 7).
3. **Trigger and puzzle relays** (look, distance, camera-distance, safe keypad, keyboard): tours in the chapters that use them (Scene 6/15 safe, Scene 14 keyboard).
4. **Scene 14 quest game** (the point-and-click game on the PC, `Location14_GameQuestPlayer`) and the **Scene 13/15 core computer** (`GamesCore_InputField` keys, `GamesCore_Main.OpenFolder`): player input with no relay.
   - Decompiled (GameAssembly.dll, addresses from `-mt-trace`): `ClickMouse` raycasts the mouse 100 m from the camera and walks the quest character (`Location14_PlayerQuest.Move(pos, event)`) to the point or to a `Location14_QuestInteractive` (its event fires on arrival).
   - The reply choice is `Location14_ButtonChangeDialogue.OnPointerDown`, which inlines `ChangeDialogue(side)`: buttons off, the dialogue's `eventFinish`, then `eventRight` (side) or `eventLeft`. A hook on `ChangeDialogue` never sees a click; `EnterDialogueChange` is only the hover sound.
   - Plan: the player at the PC drives the quest game (minigame style, others watch); relay `PlayerQuest.Move` targets and the `OnPointerDown` side through the host so every machine's quest game walks and chooses the same. Needs mouse input in the lab (not possible in hidden windows yet).
5. **Look trigger hold timer** (`Trigger_MouseEvent.eventTimeEnd`): relayed and proven by the `triggers` tour.
6. **Echo windows:** deaths (6 s) and doors (0.5 s) should become sender-plus-sequence ids.
7. **Steam transport:** measure loss and latency on a real Steam pair.
8. **Performance:**
   - the chapter-start room scan (up to 8000 objects in one frame);
   - pose bandwidth for far-away players;
   - `SceneKeys.Key` path building on hot paths.
