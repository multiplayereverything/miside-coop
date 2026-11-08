# Where the game reads a player's input (2026-11-08)

A player's press reaches the mod only when the game's own input code calls a method the mod hooks. `ObjectInteractive` didn't: its `Update` read the mouse and ran its own copy of `Click`, so no real press of an interactive object was synced until 0.3.13 (lab tours call `Click()` and never noticed).

How this list was made: every call or jump in GameAssembly's code sections to `Input.GetMouseButtonDown/Up`, `GetButtonDown/Up`, `GetKeyDown/Up` and `anyKeyDown`, mapped to the method it sits in (`methods.tsv`), then the game methods each of those calls (capstone). Scripts: `tools/study/native_callers.py <GameAssembly.dll> <methods.tsv> <address>...` and `tools/study/native_callees.py <GameAssembly.dll> <methods.tsv> <Class.Method>...`; rerun them after a game update.

## Story classes

| Class (method reading input) | What a press calls | Reaches the mod? |
| --- | --- | --- |
| `ObjectInteractive.Update` | nothing: inline copy of `Click` | Yes since 0.3.13: the press read inside `Update` becomes `Click()` (`Patches.cs`) |
| `Trigger_MouseClick.Update` | `Trigger_MouseClick.Click` | Yes (hooked) |
| `DialogueChanger.Update` | `DialogueChanger.ClickButton` | Yes (hooked) |
| `Interface_KeyHint_Key.Update` | `Interface_KeyHint_Key.KeyDown` | Yes: its `eventKeyDown` is gated by the story log |
| `Dialogue_3DText.Update` | no `SkipDialogue` call: inline skip | No. A player's own skip ends the line on their PC only; the line's end is gated, so the story still waits for the host |
| `ObjectAnimationPlayerHold.Update` | `MoveMouse`, `AnimationDown` | No. Hold interactions: Scenes 4, 6, 9, 17 |
| `GameController.Update` | fast menu, `PlayerMove.AnimationPlayStop`, `ObjectAnimationPlayer.AnimationPlay` | Partly; check per chapter |
| `Input_Event.Update`, `ItemInteractive.Update`, `Location18_Blink.Update` | inline | No, but not wired in any story chapter's persistent events |

## Chapter mechanics that read input themselves
Each is checked in the chapter sweep: `Location4TableCardGame`, `Location4Fight` (Scene 4); `Location11_Lift`, `Location11_PaperPart`, `Location11_PrayerRoom`, `Location11_GameLinesMain` (Scene 11); `Location14_GameQuestPlayer`, `Location14_PC*`, `Location14_Saper` (Scene 14); `Location15_ScreenID` (Scene 15); `Location17_PumpkinClicker` (Scene 17); `Location18_Novella`, `Location18_TicTacToe` (Scene 18); `Location19_*` (Scene 19); `Location21_Cooking` (Scene 21); the minigames (`MinigamesController`, `Shooter_*`, `CarSpace_*`, `QuadLinerMain`, `TetrisGame`, `Metroidvania_*`, `FightBoss_Player`, `MakeManeken_*`, `GamesCore_*`) and the Scene 1 phone app (`Tamagotchi_*`, `ButtonMouseClick`).

Not story: `ConsoleCall`, `ConsoleCameraFly`, `ConsoleInterface`, `Input_Cheat`, `PhotoModeMain`, `Menu`, `MenuCaseOption`, `MenuMitaDance`, `ButtonMouseMenu`, `MinigamesFastMenu`, `MagicaCloth.SimpleInputManager`, `BaseInput`.
