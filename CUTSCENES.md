# Cutscene and interaction rules

Every cutscene, interaction animation and minigame is shared according to one rule. Rules live in `src/CutsceneRules.cs`; a rule changed or added there is the only change needed for a per-cutscene fix. They can also be changed without rebuilding, in `BepInEx/config/MiSideTogether.cutscenes.ini`.

## Kinds and defaults

| Kind | What it is | Default |
| --- | --- | --- |
| Interaction | Pressing something that plays a player animation | **Puts an item in the hand** (detected from the game's own wiring, e.g. the toy plane): `who=actor`. Only the presser plays it, the others see the item in their hand, and its events and finish run for everyone. **Otherwise**: `who=everyone`, called mirror. Every player plays the same animation on their own character, at the same spot, and two players on one spot don't see each other's body. |
| Cutscene | The game's cutscene mode, or a timeline that drives the camera | **Run by the host**: in a running chapter a client never starts one itself. Its start becomes a request; the host starts it and everyone plays it from the host's command, kept on the host's time. Also `hide=on` (other players hidden) and `gather=on` (everyone moved to the host afterwards). |
| Minigame | One of the 16 minigames | `who=actor`: only the player who started it plays; the others see that player's body. |

## Options

| Option | Meaning |
| --- | --- |
| `who=everyone` | Everyone plays it on their own character (mirror). |
| `who=actor` | Only the player who pressed it plays it. The others see it on that player's body. Objects the game attaches to the player ride in the actor's hand, and the end and mid-animation events are relayed. |
| `pov=on` | With `who=actor`: the others watch through the actor's eyes. Off by default. |
| `hide=on` | Other players are hidden while it runs. |
| `gather=on` | Afterwards, players are moved next to the host (on safe floor only). |

## Held items: no per-cutscene rules needed

`src/Sync/Story/HeldItems.cs` walks each interaction's UnityEvents: the click, then the animation's start, loop and mid-animation events, and whatever components and switched-on objects those call, 4 levels deep. If the chain reaches a `Transform_Magnet` attach-to-the-player call (`ReMagnerAndParentPlayer…`, or a magnet aimed at the player), the interaction holds an item. That covers any interaction or cutscene where the player takes something in hand, without a rule for it.

The `held` tour lists every interaction of Scenes 1 and 2 with its verdict. For example, Scene 1's plane and phone hold an item, its door does not; Scene 2 has 14 of 22, including the batteries, spoon, bow and pencil.

## Matching

A rule's match text is compared, ignoring case, against the scene key of what was clicked. The key is the object path, e.g. `.../Interactive Mobile`. If the click key has no rule of its own, the key of the animation it starts is checked next. The first match wins, and rules in the ini file come before the built-in ones.

## Built-in cases

| Match | Rule | Why |
| --- | --- | --- |
| `InteractivePC` | who=actor, pov=on | The core computer (Scenes 13, 15) is one seat: the presser operates it, the others watch through their eyes. Mirrored, everyone sat down and the others could not get up. |

## Ini file

One rule per line; `#` starts a comment:

```
Interactive Mobile | who=everyone                     # everyone picks up their own phone instead
Some Cutscene      | who=everyone | hide=off | gather=off
```

## Fix log

When the user reports a cutscene that needs different handling, add the rule to `BuiltIn` in `CutsceneRules.cs` with a note, add a row to the table above, and note here what was wrong.

- 2026-10: the phone and plane switched from eyes POV and actor-only to mirror, at the user's request ("playing the same animation and putting them there").
- 2026-10: held items are detected from the game's wiring, so the plane and anything else handed to the player is presser-only with the item in their hand. Cutscenes are run by the host.
- 2026-10-26: hand animations (`PlayerMove.AnimationHandPlay`) follow the same rule as body ones; they used to be blocked for every replayed click, mirrored ones included.
- 2026-10-26: the cartridge (Scenes 6, 10, 13) and the Scene 14 smartphone are presser-only by the held-item detection (the item goes into the hand); the every-chapter tour confirms it.
- 2026-10-27: `InteractivePC` presser-only with pov: the every-chapter run left the other player seated at the Scene 13 computer after the host moved on.
- 2026-10: the Scene 1 phone is presser-only too, at the user's request. Whoever picks it up holds and uses it; the others see them pick it up and cannot take another (the replayed click disables theirs).
