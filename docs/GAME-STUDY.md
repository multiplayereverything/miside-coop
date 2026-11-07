# What the game does, and what it means for sync

Results of the whole-game study (2026-11-06), Step 0 of [REMAKE-PLAN.md](REMAKE-PLAN.md). The detail is local only, because it describes decompiled code: `private/game-study/notes/` has one entry per script and `private/game-study/scene-notes/` has one file per scene.

## Coverage
| What | Count | How it was checked |
| --- | --- | --- |
| Game scripts | 707, plus 8 library groups | `tools/study/make_batches.py check`: 715 of 715 have an entry |
| Methods decompiled | 9,163, 0 failures | Ghidra export log |
| Scenes | 24 of 24 in the build | Hierarchy and wiring dumped from the running game |

**Limits:**
- The notes are model readings of Ghidra pseudocode, not original source, and they still contain about 300 "probably" marks.
- A few large methods were read for their logic, not branch by branch. Very repetitive code was read once and the copies compared.
- Five findings were checked by hand against the code; the rest were not individually re-checked.
- Nothing here was tested with two running copies. Treat each row below as a lead until a tour proves it.

## Script verdicts
| Verdict | Scripts |
| --- | --- |
| Presentation | 238 |
| Data only | 177 |
| Minigame | 98 |
| Story-driving | 82 |
| Player-local | 58 |
| UI and menu | 44 |
| Dev tool | 36 |

## How the story actually runs
Nearly every chapter is the same machine: a chain of quest objects. A quest root switches on, its `Events_Data` and `Time_Events` fire, the player does something, and the next quest destroys the last one. Events chain through scene-wired UnityEvents. `Events_Data` fires event 0 by itself when `onStartZeroIndex` is set, and dialogue lines chain through `Dialogue_3DText.nextText`, a field that isn't in the wiring.

Three facts follow:
1. **A missed step can't be redone.** Quests, triggers and one-shot objects destroy themselves, so a copy that misses a step is stuck, and replaying a later event finds its targets gone. Repair has to replay in order from the point of divergence. A later state can't be set directly.
2. **The story advances from a small set of sources:**
   - `Trigger_Event` (zones)
   - `ObjectInteractive`, `Trigger_MouseClick`, `ItemInteractive` (clicks)
   - `Interface_KeyHint_Key`, `Input_Event` (raw keys)
   - `DialogueChanger` (choices)
   - `Dialogue_3DText` (line ends)
   - `Time_*` (timers)
   - `MitaAIMovePoint`, `MitaPerson`, `Mob_ChibiMita_Animation` (NPC arrival)
   - `ObjectAnimationPlayer`, `MitaEvent`, `Playable_Animation`, `Player_Look` (animation ends)
   - `Trigger_MouseEvent`, `Mesh_VisibleEvents`, `Trigger_Distance*` (look and distance)
3. **There's no single central hook.** `UnityEvent.Invoke` is called by name in 324 places but also inlined elsewhere (`Events_Data.Start`), so one hook on it would silently miss events. Gates go on the sources above.

## Why today's mod drifts
All of these fire from each PC's own clock, position or camera. 56 of the 80 story-driving scripts checked are never named in the mod's sync code. That doesn't prove each one is unsynced, because many run downstream of a relayed event, but nothing keeps their timing or their outcome the same.

| Cause | Examples | Status |
| --- | --- | --- |
| Local timers drive story steps | Every `Time_Events` chain starts at that PC's `OnEnable` | Read in code and wiring |
| Line ends differ per PC | `Dialogue_3DText`: voice length by language, typing speed, Space skip, local camera check | Read in code |
| NPC arrival differs per PC | `MitaAIMovePoint`, `MitaPerson` walk by NavMesh, and arrival fires the next beat | Read in code and wiring |
| Local player and camera gates | `Trigger_Event` tests only the local player, `Mesh_VisibleEvents` only the local camera | Read in code |
| Local counters and gates | `Location8_InfinityRoom.indexRoom`, `Location12.questCount`, `Events_IntMemory`, money gate of 450 in Scene 1 | Read in code |

**Seen in the lab (2026-11-06):** a story event for an object that's still off on the client is retried for 5 s and then thrown away (`MT-STORY dropped ... missing or switched off here`). The test forced this case, so how often it happens in real play is unknown, but with self-destroying quests one dropped event can leave a copy stuck. An ordered log has to wait for the object instead of giving up.

## The copies don't start the same
GMTogether requires both copies to begin identical. These break that here, and most are cheap to fix at session start.

| Difference | Source | Effect | Status |
| --- | --- | --- | --- |
| VRAM | `Options_GameObjectMemoryVideo` destroys objects by `graphicsMemorySize` | None found: the script is on no object in the 24 scene dumps, so no rule was added | Corrected 2026-11-06. A prefab spawned later isn't ruled out |
| Quality setting | `Options_QualityWorld` destroys objects by `GlobalGame.qualityWorld` | None found: same as VRAM, no rule was added | Corrected 2026-11-06. A prefab spawned later isn't ruled out |
| Physics rate | `OptionsGame` sets `fixedDeltaTime` from the frame rate, which comes from the monitor | Physics and FixedUpdate counters differ | Checked in code |
| Language and voice | `GlobalGame.Language`, `LanguageVoice` | Line lengths, so every dialogue-driven step | Read in code |
| Date | `OptionsGame.Holidays` (Oct 27 to Nov 1, Dec 29 to Jan 2) | Halloween and Christmas objects and lighting | Seen in the `samestart` tour: with client flags on, 258 extra objects and 8 missing in Scene 3 (the tour also sets a story flag) |
| Cartridges | `FlashTaker` destroys itself if this PC's `Save/Flashes` has it | Pickups exist on only some PCs. The mod copied the host's saves but left a client's extra files in place, so this stayed possible (fixed in 0.3.8) | Read in code; extra-file removal tested in `samestart` |
| Story statics | `Location4ChangeSide` flags, `Location34_Glasses.glassesMita`, `PeaceMode` | "Yes" can be locked on one PC | Read in code |
| Random at start | `Location2Main`, `RealRoom_Time.ThingsRandom`, `Tamagotchi_Main` stats | Layouts and money differ | Read in code |
| Random that decides play | `Location15_ScreenID` door code, `Location7_HammerButton`, `Location20_RunCorridor` | Different correct answers per PC | Door code checked in code |
| Time scale | `World.LoadVoiceFileNow` pauses while loading a voice file; photo mode and the console set `timeScale` | One PC stalls alone. The mod's `Pause` hook may hide part of this; not tested | Pause call checked in code |

## What this means for the remake
**The approved direction holds:** one host log, and one Main Character per story moment. The study changes four things:

1. **New work: same starting state.** Make the copies start alike when the session starts: the host's quality and language rule, fixed physics rate, host's date flags, statics and cartridge state, and console and photo mode off. This is cheap, and it should remove whole classes of drift even before the rebuild.
2. **Client timers must stop driving the story.** The earlier plan treated timers as something only repair could catch. Instead, gate the sources listed above on clients, so a client's `Time_Events`, line ends and NPC arrivals wait for the host's log. This is the riskiest part and needs a test in Scene 1 first, including whether a blocked timer can be resumed by the host.
3. **Repair must replay in order.** Direct state correction can't rebuild destroyed objects. A reload is impossible for Scene 19 from a cold start. Reload is only for chapters that allow it.
4. **"Watch through their eyes" may be too much.** Agents counted about 17 moments in Scene 3 and 28 in Scene 19. Watching all of them would feel like a movie. Default proposal: short interactions stay on the Main Character's body while the others keep walking, and only moments that take over the camera use `eyes`.

**Input hook:** 86 files make about 410 direct calls to named `UnityEngine.Input` functions, so hooking looks workable. Not yet tested at runtime.

## Unknowns to settle in the Scene 1 rebuild
- Whether a client's `Time_Events` coroutine can be gated and released cleanly.
- Whether `Dialogue_3DText`'s end can wait for the host without visible glitches.
- How much lag gating adds to a client's click, over real Steam.
- Exact chapter order: `Scene_Load.nameSceneLoad` is a field, not in the wiring dumps.
