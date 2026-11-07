# MiSide Together remake plan

Approved by the user on 2026-11-06. Work through it one step at a time, following the Karpathy guidelines in `AGENTS.md`. Update the status table when a step changes, and log results in `MODLOG.md`.

## Why
Every PC runs its own story and any player can trigger events, which are relayed to the others. Events can arrive late or in a different order, so the copies drift apart. Fixes have been added one feature at a time (120 game hooks). Lab tours have passed while real players still found bugs.

The user asked for sync inspired by [GMTogether](https://yal.cc/docs/gm/gmtogether/), with one main character in cutscenes.

## What GMTogether is, and what we can use
GMTogether is P2P lockstep: both PCs run the same deterministic game and exchange only inputs, with hooks on the game's input calls. It detects desyncs by comparing metrics such as object count, RNG state and level, and notifies both players. Its docs describe no repair. Its sessions are limited to 2 computers, and its docs don't cover cutscenes.

True lockstep won't work here. MiSide is a Unity IL2CPP game, and its physics, animators and NPC pathfinding are not expected to match exactly between PCs. We use the ideas instead:

| GMTogether idea | In the remake |
| --- | --- |
| One shared game | One story log: the host numbers every story event, and every PC runs them in the same order. A client's click or trigger becomes a request to the host. |
| Input delay | Each event starts a moment later on every PC, including the host, so they start together. |
| Pause when a player is behind | Cutscenes and chapter starts wait for the slowest PC. |
| Hooked input | Tested only for the Scene 1 phone app and the core computer, where each PC must draw the screen itself. If it fails, they keep their current relays. |
| Desync check | PCs regularly compare chapter, story position, dialogue, cutscene time and Mita's position, and save a report and screenshots on a mismatch. |
| Not in GMTogether | Repair: replay missed events, then correct room state. A chapter reload is rare and a last resort. |

## Design decisions (Option A)
- **Free roam:** everyone walks their own body between story moments.
- **One Main Character per story moment:** the first player to press something is the Main Character. The host is it for cutscenes that start on their own. The others have their controls paused and are placed next to the Main Character afterwards.
- **Timeline cutscenes** (Scenes 1, 3, 5, 6, 8, 12, 13 only): play on each PC at the host's time.
- **Scripted story moments:** moments that take over the camera are watched through the Main Character's eyes (`Spectate.cs`), with faster and smoothed camera updates. Short interactions stay on the Main Character's body while the others keep walking (proposal from the study: some chapters have 17 to 28 moments).
- **Minigames:** run only on the Main Character's PC while the others watch.
- **Repair limit:** Scene 19 can't be started cold, and Scene 20 can't be started cold right after Scene 18. Where a reload isn't possible, the desync warning tells the players instead.
- **Moving over:** chapters move one at a time, and each chapter uses only old sync or only new sync, never both.

- **Same starting state (decided 2026-11-06):** a client's text language stays its own so everyone can read. The voice follows the host, because voice length drives line ends. Physics runs at a fixed 60 Hz on every PC. A client's save folder matches the host's exactly (its own files are backed up and restored when it leaves). Photo mode and the console are off in a session.
- **Stand-ins for story events (Step 3):** timers, line ends and animation ends are `UnityEvent`s fired from each PC's own clock, some inlined. Each is swapped for a stand-in when the chapter loads. On the host the stand-in logs the event and runs the real one. On a client it does nothing, and the real one runs when the log reaches it. This needs no hook per source.

## Kept and rebuilt
- **Kept:** Steam and LAN transports, native menu, avatars and bones, lab and tours, hook knowledge in `AGENTS.md`.
- **Rebuilt:** story and world sync, about 3,000 of the 6,600 lines outside `src/Lab`.

## Known risks
- Game scripts run on their own timers. The study shows these drive most story steps, so the plan is to gate them on clients. If that fails in Scene 1, repair has to carry far more of the load. Repair must replay in order, because quests destroy themselves.
- The input hook works in IL2CPP (`inputhook` tour, one PC, 2026-11-06). The same input can still give different results on each PC.
- A client's clicks respond later because they reach the host first: 66-69 ms in the lab on one PC. Not yet measured over Steam.
- Old and new sync exist side by side during the move.
- Logs must stay out of the repo and releases. Friends are told their log is sent to the host when they connect, and README says so.

## Steps
| # | Step | Done when | Status |
| --- | --- | --- | --- |
| 0 | Study the whole game, every script, before any mod code changes (user's request, 2026-11-06) | Every script type in the game has an entry saying what it does and how it matters for sync, with the coverage counted by a tool, not by hand | Done 2026-11-06: [GAME-STUDY.md](GAME-STUDY.md), 715 of 715 script entries, 24 of 24 scenes |
| 1 | Session logs on every PC in normal play, with friends' logs streamed to the host as they happen (changed from "when the session ends", so a crash or the host quitting first loses little) | The client's session log arrives on the host while the client still runs. The check fails when sending is disabled | Done in the lab 2026-11-06 (0.3.7, protocol 6): `sessionlog` tour passes, fails with `-mt-nosessionsend`. Not yet run over real Steam or by real players |
| 2 | Baseline: run `scripts/lab-suite.ps1` on 0.3.6 with the user's game closed, then one real session with a friend, and read the logs | Suite results and real-session logs saved and read | Lab part done 2026-11-06 on 0.3.7: 33 of 35 on the full run. `cutscene` failed from a stale test (fixed, then passed 3 of 3); `menuleave` failed once, on dialogue mismatch (45%), then passed 3 of 3 alone, cause not found. Real session still to do |
| 2b | Same starting state (from the study, see GAME-STUDY.md): session-start rules for quality, language, physics rate, date flags, statics and cartridges; console and photo mode off | A tour with deliberately different settings on two copies ends with matching object sets and flags. The check fails with the rules off | Done in the lab 2026-11-06 (0.3.8, `samestart` tour): with the rules on, 2,968 objects match with none different. With `-mt-norules`, the client has 258 extra and 8 missing, and flags, saves and photo mode all fail. The console hook is installed but not exercised, because the lab can't hold its key. Not yet run by real players |
| 3 | Rebuild Scene 1 with the story log and Main Character, gate the story sources on clients (including timers), and test the input hook on its phone app | A Scene 1 tour passes with both copies on the same story position, then a real session with a friend | Lab part done 2026-11-06 (0.3.8, `src/Sync/Story/StoryLog.cs`): `scene1` runs 267 events in order with 0 lost and 0 game errors, and both copies reach Scene 2. `storylog` fails with `-mt-nolog`. In Scene 1 the Main Character is always the host. Input hook works in the `inputhook` tour, but Scene 1 doesn't need it. Real session still to do |
| 4 | Desync checks and repair | A forced desync is detected and repaired in a tour, and the check fails with repair disabled | Done in the lab 2026-11-06 (`src/Sync/Story/StoryCheck.cs`, `desync` tour): noticed in about 7 s, fixed about 5 s later, fails with `-mt-norepair`. It checks chapter, quests and conversation, and saves a report and screenshot. Not compared: cutscene time and Mita's position, which have their own live correction. No chapter reload: if repair doesn't settle it, the players are told. Quest repair is the only kind tested |
| 5 | Move the other chapters one at a time, removing each old relay | Each chapter passes its tour, then a real session | In progress. Scene 2 lab part done 2026-11-07 (0.3.9, protocol 8): `scene2` plays the whole chapter with the client pressing every second object, 195 events applied, 0 lost, 0 waits, both copies reach Scene 3. Fails at the scarf with `-mt-nogaterequest`. Full suite 38 of 40 with 0 `MT-MODERROR` in 84 logs; the two failures were fixed and `phone`, `carry`, `held`, `scene1`, `scene2` pass again, but the full suite was not rerun after that fix. Real session still to do. Scene 3 lab part done 2026-11-07 (0.3.10, protocol 9): `scene3full` plays all seven quests with the client pressing every second object, 717 events applied, 0 lost, 0 waits, 215 of 215 lines within 0.5 s (worst 0.09 s), both copies reach Scene 4. After listing it, 12 affected tours pass; the full suite has not been rerun on 0.3.10. Real session still to do. Next: Scene 4 |

## Current priority (2026-11-07)
Before Scene 4: the five fixes the user asked for after playing the opening. Their state and next steps are in `docs/HANDOFF.md`, "Pick up here".

## Step 5 notes
Gate kinds in `StoryLog.cs`, chosen by who is able to fire the event:
- **Host:** only the host's clock counts (timers, line ends).
- **Body:** the player's animation events. The performer asks the host; on a watcher they run when the log arrives, but a magnet they aim at the watcher's own body is undone.
- **Press:** a key prompt. Only a player with `animationRun` may answer, with a 2 s repeat guard.
- **First:** the first player's copy owns that key and the others are refused. Used for animators in rooms that only switch on near a player, the corridor end and story visibility events.
- **Moment:** a script playing out on the Main Character's own PC from their input fires it (Scene 3's carrot and walk to the toilet). The script itself doesn't run on watchers.
- **Built late:** Scene 3's "when Mita is ready" events (`TakeEventWhenReadyWalk`) are only known when the story asks for them, so their stand-ins go in then, with a key by name because sibling numbers have drifted by that point.

For each new chapter, look for scripts driven by raw input or a PC's own NPC: they don't show up as events until the pilot stalls on them.

One Main Character, as built (`momentMine` in `StoryLog.cs`): the last press in the log owns the moment on every PC until the next press, with no time window, because a press's animation can start much later (Scene 3's selfie, on a line's end). The host owns it until anyone presses. Only the owner runs player animations and `Player_Position` moves. A press from the log runs even if the object isn't armed on that PC. A player locked by `dontMove` may answer a prompt, and an answered prompt is taken down on the other PCs too.

Not built or not shown yet:
- With a client as Main Character on the walk to the toilet, the script moves Mita on that client's PC, but her position streams from the host. Not checked what the host sees.
- In `scene3full`, 53% of Mita's animation changes on the host have no match on the client within 3 s (Scene 1: 23%), while her position matches to 0.00 m. Could be real or short states falling between samples; needs someone looking at the game. The tour's start-lag limit is 1.5 s, as in the other whole-chapter tours (measured 1.08 s).
- Walking into a trigger doesn't take the moment: an animation it starts plays on the last presser. Room triggers would flip it constantly otherwise. Not tried with real players.
- Scene 3's hall objects (`Electro`, `Books`, `Ring` and others) stayed off on a client that doesn't walk there, so the pilot's client never pressed them. Why they stay off wasn't found, so whether a real client can press them is unknown.
- The rule "others have their controls paused and are placed next to the Main Character afterwards" isn't built. Everyone keeps walking during short moments.
- The lab pilot doesn't really walk: it fires triggers directly and opens rooms itself, so real walking can still hit what it skips.
- A First gate drops repeats from players who aren't its owner. Some audio could be lost; untested.
- `phone.seen` checks a held item only when the host holds it. When a client holds one, the host logs the magnet staying off (two batteries in `scene2`), but where the item appears on the host isn't measured.
- A client once stuck at event 136 on `TriggerCorridorNear` (key not found, Scene 2), which did not recur. `LogSameNamed` logs same-named objects if it does.

## Standing rules
- Save and read logs every time: run `scripts/collect-logs.ps1` (copies them to `private/playtests/<date>/`) before the next launch overwrites them, then report what they show.
- Upload nothing to GitHub until the user says so. Commit locally only when asked.
- Report only what was observed. A lab pass is not proof that real play works.

## Still needed from the user
- The bug list from the 2026-11-06 session.
- lidar's `BepInEx\LogOutput.log`, if it still exists.
