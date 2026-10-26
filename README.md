# MiSide Together

Co-op multiplayer for MiSide (Steam, Unity 2021.3 IL2CPP) as a **BepInEx 6** plugin. Play the story with up to 3 friends
(4 players total). Everyone sees the other players as the main character's own body, with their exact pose and animations.

## How to play
1. Every player installs BepInEx 6 and the plugin (see Install). Everyone needs the **same game version and the same
   MiSide Together build**; the host refuses mismatches and says why.
2. In the main menu, open **MULTIPLAYER** (a new entry above Exit, in the game's own style).
   - The host picks **HOST GAME**. Steam friends and players on your network can then join.
   - Friends see **JOIN <name> (STEAM)** for every Steam friend hosting in MiSide. It needs no VPN, ports or addresses, because Steam relays the connection.
   - **JOIN <name> (LAN)** lists games on your network or VPN. **JOIN BY ADDRESS (F5)** opens the panel to type an address
     (TCP port 27030).
3. The host plays the story as usual: New game, Continue or Chapters. Everyone else follows automatically.
   - **Join in the menu or between chapters.** A friend who connects while the host is in the middle of a chapter waits in the menu
     ("Waiting for the host to start the next chapter") and comes along when the next chapter starts. The host's header shows how many are waiting.
   - Up to 4 players. A fifth is told the game is full.
   - If the host leaves, the next player becomes the host where they stand and the others reconnect to them.

## Playing online
MiSide Together connects players directly. It does **not** use the Multiplayer Everything global relay server; there is no room code and no server of ours in between.
- **Steam (recommended):** the host's game is announced to their Steam friends through the Steam API (`steam_api64.dll`, which ships with MiSide).
  Friends pick **JOIN <name> (STEAM)**. Steam's own P2P networking carries the traffic and relays it when a direct path is blocked,
  so nobody needs to open ports, run a VPN or share an IP address. Everyone just needs to be running MiSide through Steam.
- **LAN or VPN:** games on the same network (or a VPN such as ZeroTier or Radmin) show as **JOIN <name> (LAN)**.
- **Direct address:** **JOIN BY ADDRESS (F5)** connects to an IP address on TCP port 27030 (the host must forward that port).

## Two copies on one PC
`scripts\second-instance.ps1` opens a second MiSide with the mod next to your own, to try co-op alone. It runs from its own folder
(`private\lab-second`, links to your install), uses the installed mod build and never touches your saves.
`-Join` joins the game hosted on this PC automatically, and `-Name "Friend"` sets the name shown above that player.

The **F5** panel also works in game:
- **Player list.**
- **Go to the host:** teleports you onto safe floor next to the host. It refuses while either of you is in a cutscene.
- **Bring everyone here:** the host's button that brings everyone stuck or lost to them.

## What is shared
The full list (how each thing syncs, who is in charge and which test proves it) is in [docs/SYNC.md](docs/SYNC.md). Every game script in every chapter is listed with its sync status in [docs/SYNC-INVENTORY.md](docs/SYNC-INVENTORY.md).

| Thing | How |
| --- | --- |
| Chapters and saves | The host leads. Chapter transitions run the game's own `Scene_Load` on every machine.<br>Clients continue from the host's save files. Their own saves are backed up while connected and restored afterwards (or at the next start after a crash). |
| Players | A copy of the main character's body with the sender's **exact skeleton pose** (all 68 bones).<br>Props and outfit parts show exactly when they show on the sender. Name tags. |
| Cutscenes | **Run by the host:** a client never starts a cutscene on its own. The host starts it and everyone plays it from the host's command, kept on the host's time.<br>Other players are hidden during a real cutscene and gathered next to the host (on safe floor) after it. |
| Interactions | **Held items:** anything that puts an item in the player's hand (like the toy plane) is done by the presser, and everyone sees the item in their hand. The mod reads this from the game itself, with no list per cutscene.<br>**Mirror** (everything else): pressing the phone, the toy plane and similar plays the same animation on every player's own character, at the same spot, so everyone sees it first-hand. Two players on one spot don't see each other's body.<br>Cases can be tuned one by one ([CUTSCENES.md](CUTSCENES.md)), e.g. "only the presser does it". |
| Story | Pressing things, trigger volumes, one-shot "look at" triggers, distance triggers, dialogue choices and skips, the basement safe keypad and the on-screen keyboard happen for everyone. Anyone can drive the story. |
| Dialogue | The host's lines are everyone's: a line the host shows starts for everyone, a line it finished is skipped for those behind. The start lag is measured in the tests. |
| Visual novel (Scene 18) | Reads on together: anyone's click turns the page for everyone, and anyone behind catches up line by line. |
| Room | Objects switched on or off, moved objects, lights, and scene animators (drawers, TV, lamps) follow the host's room. |
| Mita and NPCs | Mita, Chibi Mita, the follow-Mitas and the boss are host-driven: position, animation and face. She looks at the **nearest** player. |
| Chasers and deaths | Chasers hunt each player on their own machine. When anyone dies, **everyone restarts at the checkpoint together**. |
| Keys and items | One shared key inventory; items held in a hand show in that player's hand. |
| Doors and props | Doors follow whoever moved them last; loose physics props stream from whoever knocks them. |
| Randomness | Random placements, materials, idle animations and random lines roll the same on every machine. |
| Minigames | **One plays, the others watch them play.** When the player finishes, the game's own end events run for everyone (card game, dance, tic-tac-toe, QuadLiner), so the story carries on the same way. |
| Pause | The pause menu does not freeze time while connected. |

## Known limitations
- **Scene 19 (Glasses):** the game cannot start it cold (not even single-player from the chapter menu). Reached through the story from Scene 18, everyone moves there together.
- **Handheld Tetris:** it is each player's own and is not mirrored.
- **Steam testing:** Steam joining was verified on one PC and one account only. A real two-account session is the next thing to verify.
- **Scene 13, 14 and 15 computers:** the core computer's keyboard and folders (Scenes 13 and 15) and the Scene 14 phone chat and PC are not shared yet. The story events around them are, but a reply or folder one player picks does not reach the others.

## Install
1. Install **BepInEx 6 (Unity.IL2CPP, win-x64)**, build be.790, from https://builds.bepinex.dev/projects/bepinex_be, into the MiSide folder.
   - Start the game once and wait for the main menu. The first start takes about a minute.
2. Copy `MiSideTogether.dll` to `MiSide\BepInEx\plugins\MiSideTogether\`.
3. Settings are saved in `BepInEx\config\MiSideTogether.ini`.

Uninstall: delete `BepInEx\plugins\MiSideTogether`. To remove BepInEx itself, delete `BepInEx\`, `dotnet\`, `winhttp.dll`,
`doorstop_config.ini`, `.doorstop_version` and `changelog.txt`.

## Building and testing
- **Build:** `scripts\build.ps1` builds and installs the DLL. Use `-NoInstall` to build only.
  - It needs the .NET 8 SDK and `BepInEx\interop`.
  - It reads the animator parameter table from your own game data with UnityPy (`tools\extract_animator_params.py`).
- **Lab:** `scripts\run-tour.ps1 -Tour <name>` runs one scripted tour with 2 to 4 hidden lab copies over loopback; `scripts\lab-suite.ps1` runs them all, one at a time.
  - Your save folder is backed up and restored around each run, and the lab refuses to start while your own MiSide is running.
  - **Results:** `artifacts\tests\<tour>-results.json`. **Evidence:** `artifacts\tests\logs\<tour>\`.
  - **Log lines:**
    - `MT-CHECK`: the tour's assertions.
    - `MT-METRIC`: health and frame cost, including the worst frame.
    - `MT-STATE`: every 2 s per machine, the position, rotation and state of players, NPCs, dialogue lines and timelines. The verifier lines these up across machines and reports the desync.
    - `MT-ANIM`: every NPC animation change as it happens; the verifier reports how late each one started on the clients.
    - `MT-PRESS` (interactions tour): each interaction pressed, its rule, whether the client played it and how far apart the two spots were.
  - **Every chapter:** `scripts\interactions-all.ps1` runs the interactions tour in all 19 chapters and writes `artifacts\tests\interactions-all.md`.
  - **Research:** `-ExtraHostArgs "-mt-trace Type1,Type2"` logs the native address of every method of those game types (`MT-RVA`, for decompiling) and every call to them while playing (`MT-TRACE`).

| Tour | Players | What it proves |
| --- | --- | --- |
| `connect` | 2 | Handshake, roster, F5 panel, native MULTIPLAYER menu, Steam ready. |
| `avatars` | 2 | Body model, props, all bones within a few degrees, animation, a step followed. |
| `story` | 2 | Interactions replayed both ways (Scene 2, armed objects only). |
| `mita` | 2 | Mita position, animation, face and gaze at the nearer player; safe teleport. |
| `cutscene` | 2 | A host timeline starts on the client within 0.3 s; players hidden during it. |
| `world` | 2 | Shared key inventory, shared randomness. |
| `carry` | 2 | Actor-only rule (Scene 2 door): only the host plays it, the client stays put, the finish runs on the client. Then a carried object (the spoon) rides on the host's body and lands where the host drops it. |
| `mirror` | 2 | Scene 2's door: the client plays the same animation at the host's spot, the bodies don't overlap, and controls return. |
| `dialogue` | 2 | Host lines start on the client; finished ones are skipped; lag measured. |
| `triggers` | 2 | A distance trigger (Scene 11) and a look-and-hold timer (Scene 8) fire on the client too. |
| `scene19` | 2 | The game's own loaders take everyone from Scene 17 to 18 to 19 (Glasses) together. |
| `held` | 2 | The held-item detection: Scene 1's plane holds an item and Scene 2's door does not, with every interaction listed. |
| `hostcut` | 2 | A client's own cutscene start becomes a request; the host runs it, and the client plays it from the host's command. |
| `novella` | 2 | The visual novel reads on together: the client keeps level, and the client's click moves the host on. |
| `room` | 2 | Objects off, moved, relit on the host show the same on the client. |
| `gather` | 2 | Everyone is brought to the host after a cutscene. |
| `death` | 2 | A death on the host restarts the client too. |
| `minigame` | 2 | The host plays the card game, the client watches, and the end event runs on the client. |
| `lobby` | 4 | A player connecting mid-chapter waits, then all four arrive together in the next chapter. |
| `migrate` | 3 | The host leaves; the next player hosts in place, and the other rejoins without reloading. |
| `interactions` | 2 | Presses a chapter's interactions one by one; every one that animates the host also plays on the client at the same spot (or not at all, when it hands the presser an item). `-Chapter` picks the chapter. |
| `chapters` | 2 | Every chapter (except 19) loads for both, and teleports never misplace anyone. |
| `inventory` | 1 | Lists every chapter's game scripts, dialogue lines, choices and timelines (feeds `docs/SYNC-INVENTORY.md`). |

## Credits
- Built with Claude Code (AI-assisted), on the architecture of the author's Spooky Together and Baldi Together mods.
- Ideas from QuestionBlock64's **MiSide-Multiplayer** (https://github.com/QuestionBlock64/MiSide-Multiplayer): Mita face relay, nearest-player gaze, its death-hook list, and native menu cloning tips.
  - That project has no license, so no code was copied; everything here is an independent implementation.
- BepInEx, HarmonyX, Il2CppInterop, UnityPy (build-time data extraction), ILSpy (reading interop signatures).
- No game files, decompiled code or extracted assets are distributed.
