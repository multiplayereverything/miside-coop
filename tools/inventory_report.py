"""Turn the lab's chapter inventory (<lab>/mt-inventory/*.tsv, written by the "inventory" tour) into:
  docs/SYNC-INVENTORY.md                  - per chapter: every game script type, how the mod syncs it, and the gaps
  artifacts/generated/DIALOGUES.md        - every dialogue line with its text (game text: local only, never distributed)
Usage: python tools/inventory_report.py [<inventory dir>] [<game Data/Languages/English dir>]"""
import collections, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
INV = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "private" / "lab-host" / "mt-inventory"
LANG = Path(sys.argv[2]) if len(sys.argv) > 2 else ROOT.parent / "Data" / "Languages" / "English"

# How each game script type is shared. Anything not listed is checked by name below.
COVERED = {
    "PlayerMove": "local player; body pose + bones streamed (Bones/Avatars)",
    "PlayerPerson": "animation events relayed for who=actor interactions",
    "MitaPerson": "NPC puppet on clients (host's transform + animator), face relay",
    "Mob_ChibiMita": "NPC puppet", "Location5_MitaFollowPlayer": "NPC puppet", "FightBoss_Person": "NPC puppet",
    "Character_Look": "looks at the nearest player",
    "Dialogue_3DText": "host streams active lines; clients show and finish them",
    "DialogueChanger": "choice relayed (DialogueChoice)",
    "ObjectInteractive": "click relayed (Interact)", "Trigger_MouseClick": "click relayed (MouseClick)",
    "Trigger_Event": "enter/exit relayed once per group",
    "ObjectAnimationPlayer": "interaction animation: mirror (everyone plays) or actor-only per CutsceneRules",
    "ObjectDoor": "door angle, last mover wins",
    "ObjectInteractiveItemTake": "key items shared (KeyAdd/KeyRemove)", "ObjectItem": "hand items shown on the holder",
    "Transform_Magnet": "carried objects follow the actor (who=actor only)",
    "Playable_Animation": "cutscene start relayed", "GameController": "cutscene mode hides other players; pause keeps time running",
    "Scene_Load": "chapter transitions: everyone loads together",
    "Random_Position": "seeded", "Material_Random": "seeded", "Animator_RandomPlay": "seeded", "Transform_StartRandom": "seeded", "Location21_DialogueRandom": "seeded",
    "Location6_MitaKiller": "death: everyone restarts", "Location10_ManekenChekpoint": "death: everyone restarts", "Location11_Lift": "death: everyone restarts",
    "Location11_LampMove": "death: everyone restarts", "Location20_Arena": "death: everyone restarts", "Location20_RunCorridor": "death: everyone restarts",
    "Mob_Maneken": "chaser (local) + death relay", "Shooter_Main": "minigame + death relay", "Location19_Game4": "minigame + death relay",
    "TetrisGame": "minigame (one plays)", "Location4TableCardGame": "minigame (one plays)", "Location7_GameDance": "minigame (one plays)",
    "Location18_TicTacToe": "minigame (one plays)", "Location14_PCSnaker": "minigame (one plays)", "Location14_Saper": "minigame (one plays)",
    "MinigamesTelevisionGame": "minigame (one plays)", "CarSpace_Main": "minigame (one plays)", "MakeManeken_Main": "minigame (one plays)",
    "Tamagotchi_Main": "minigame (one plays)", "Location19_Game1": "minigame (one plays)", "Location19_Game3": "minigame (one plays)",
    "MT_GameFly": "minigame (one plays)", "MT_GameCnowballs": "minigame (one plays)", "QuadLinerMain": "minigame (one plays)",
    "Trigger_MouseEvent": "one-shot looks relayed (LookTrigger)", "Trigger_DistanceCircle": "enter/exit relayed (DistanceTrigger)",
    "Location18_Novella": "visual novel: host position streamed, anyone's click reads on", "Location18_Dialogue": "visual novel dialogue (Novella sync)",
    "ObjectAnimationPlayerHands": "hand interaction: mirrored through the replayed click", "ObjectAnimationPlayerHold": "hold interaction: local to the player holding",
    "Animator": "scene animators: layer-0 state synced (room state)", "Light": "room state (on/off, colour, intensity, range)",
    "Basement_Safe": "keypad presses relayed (Call 0)", "InputKeyboard_Main": "typing relayed (Call 1-4)", "Trigger_DistanceCamera": "enter/exit relayed (Call 5-6)",
    "Basement_SafeConsole": "story-driven (safe console)", "MirrorRef": "local rendering (player bodies are scene objects, so mirrors show them)",
    "World": "chapter bootstrap (each machine)", "WorldPlayer": "chapter bootstrap (each machine)",
}
# Logic that runs on every machine from the synced story (clicks, triggers, dialogue, cutscenes); what it changes in the
# room is reconciled by the room-state sync (objects on/off, placement, lights, animator states).
STORY_LOCAL = re.compile(r"^(Time_|Events_|Event_|Animator_|GameObject_|Transform_|Rigidbody_|Mesh_|Object_|ObjectMaterial|ObjectClock|MitaEvent|Mita(AI|Clothes)|Character_LookSimple|Player_|Trigger_(Zoom|Teleport)|FunctionsGame_|FunctionsEditorUnity_|DataValues_|Location\d+|Loc\d+|Mob_|Tamagotchi|DialogueChange_Button|ObjectInteractive_CaseInfo|ObjectInteractiveReqIK|IK_Hand|MinigamesAutomate|RealRoom_|FlashTaker|FlashLight|Dialogue_Data3DText|MitaLifeEmotion|LampBox|LinePhysic|ChibiRoomLogic|Character_LookEvent|Locatrion\d+|AnitaStatLife|Basement_|Trigger_DistanceCheck|ObjectInteractiveGroup|MinigamesTelevisionController|QuadLiner_|InputKeyboard_Key|GamesCore_|Core_|CoreScreens|MitaCore)")
# Types that need no sync: presentation that each machine runs for itself from synced state.
LOCAL = re.compile(r"^(Localization|Menu|Button|UI|Ui|Camera|Audio|Sound|Post|Light|Shadow|Reflection|LOD|Optimization|Particle|Shader|RealToon|Kino|Colorful|Text|Font|Outlin|Cursor|Settings|Options_|Achievement|Subtitles|Steam|Magica|.*IK$|IKExecutionOrder|LookAtIK|GrounderFBBIK|Material_|SpriteRenderer_|Dialogue_Symbol|Interface_|Unity_Note|Glitch|AnalogTV|PIDI_|BlackScreen|Animations_CameraFov|WorldEditor|ConsoleEditor|Debug|GaussianBlur|Datamosh|Negative|Noise|LoFiPalette|ComicBook|FastVignette|VintageFast|BrightnessContrastGamma|Sharpen|PlayerCameraEffects|PlayerHandIK|Renderer_|MagicCloth|Materials_|WorldPlayer_AnimationFOV|Transforms_|Destroy_|IK_)")


def read(path):
    rows = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("#") or not line.strip():
            continue
        p = line.split("\t")
        while len(p) < 6:
            p.append("")
        rows.append(dict(kind=p[0], type=p[1], key=p[2], active=p[3], pos=p[4], extra=p[5]))
    return rows


def extra(s):
    out = {}
    for part in s.split("|"):
        if "=" in part:
            k, v = part.split("=", 1)
            out[k] = v
    return out


def dialogue_files():
    files = {}
    for f in sorted(LANG.glob("LocationDialogue *.txt")):
        files[f.stem.replace("LocationDialogue ", "")] = f.read_text(encoding="utf-8-sig", errors="replace").splitlines()
    return files


def best_file(lines, files):
    """The chapter's dialogue file: the one whose lines match the loaded texts best."""
    best, score = None, -1
    for name, text in files.items():
        s = 0
        for d in lines:
            i, t = int(d.get("index", -1) or -1), d.get("text", "").strip()
            if t and 0 <= i < len(text) and text[i].strip() == t:
                s += 1
        if s > score:
            best, score = name, s
    return best if score > 0 else None


def main():
    chapters = sorted(INV.glob("*.tsv"), key=lambda p: [int(x) if x.isdigit() else x for x in re.split(r"(\d+)", p.stem)])
    if not chapters:
        sys.exit(f"no inventory in {INV}; run scripts/run-tour.ps1 -Tour inventory first")
    files = dialogue_files()
    doc = ["# Sync inventory", "",
           "Generated by `tools/inventory_report.py` from the lab's `inventory` tour (every chapter loaded, every game script listed).",
           "Status: **synced** (how); **story-driven** (runs on every machine from the synced story events, its changes to the room reconciled by room-state sync); **local** (presentation each machine draws for itself); **REVIEW** (not classified yet: check it).", ""]
    dlg = ["# Dialogue lines (local reference, contains game text: never distribute)", ""]
    totals = collections.Counter()
    review_all = collections.Counter()
    for path in chapters:
        rows = read(path)
        name = path.stem.replace("_", " ")
        scripts = collections.Counter(r["type"] for r in rows if r["kind"] == "script")
        lines = [dict(extra(r["extra"]), key=r["key"], active=r["active"], pos=r["pos"]) for r in rows if r["kind"] == "dialogue"]
        choices = [r for r in rows if r["kind"] == "choice"]
        timelines = [r for r in rows if r["kind"] == "timeline"]
        animators = [r for r in rows if r["kind"] == "animator"]
        counts = {r["type"]: r["extra"] for r in rows if r["kind"] == "count"}
        totals["dialogue"] += len(lines); totals["choice"] += len(choices); totals["timeline"] += len(timelines)
        doc += [f"## {name}", "",
                f"{sum(scripts.values())} script instances of {len(scripts)} types; {len(lines)} dialogue lines; {len(choices)} choice menus; "
                f"{len(timelines)} timelines; {len(animators)} animators; {counts.get('Light', '?')} lights; {counts.get('AudioSource', '?')} audio sources.", "",
                "| Type | Count | Sync |", "| --- | ---: | --- |"]
        for t, n in sorted(scripts.items(), key=lambda kv: (kv[0] not in COVERED, -kv[1])):
            if t in COVERED:
                status = "synced: " + COVERED[t]
            elif LOCAL.match(t):
                status = "local"
            elif STORY_LOCAL.match(t):
                status = "story-driven"
            else:
                status = "**REVIEW**"
                review_all[t] += n
            doc.append(f"| {t} | {n} | {status} |")
        if timelines:
            doc += ["", "Timelines: " + ", ".join(f"`{r['key'].split('/')[-1]}` ({extra(r['extra']).get('duration', '?')} s)" for r in timelines)]
        doc.append("")
        src = best_file(lines, files)
        num = re.match(r"Scene (\d+)", name)
        if src is None and num and f"Location{num.group(1)}" in files:
            src = f"Location{num.group(1)}"   # inactive lines carry no loaded text; the chapter number names the file
        dlg += [f"## {name}" + (f" (LocationDialogue {src})" if src else ""), "", "| Key | Index | Speaker | Text | Voice | Next |", "| --- | ---: | --- | --- | --- | --- |"]
        text = files.get(src, [])
        for d in sorted(lines, key=lambda d: int(d.get("index", 0) or 0)):
            i = int(d.get("index", -1) or -1)
            t = d.get("text", "").strip() or (text[i].strip() if 0 <= i < len(text) else "")
            dlg.append(f"| `{d['key'].split('/')[-1]}` | {i} | {d.get('speaker', '')} | {t.replace('|', '/')} | {d.get('voice', '')} | `{d.get('next', '').split('/')[-1]}` |")
        if choices:
            dlg += ["", "Choices:", ""]
            for c in choices:
                e = extra(c["extra"]); quest = (LANG / (e.get("file", "") + ".txt"))
                qt = quest.read_text(encoding="utf-8-sig", errors="replace").splitlines() if quest.exists() else []
                opts = []
                for b in e.get("buttons", "").split():
                    i = int(re.match(r"\d+", b).group(0))
                    opts.append((qt[i].strip() if i < len(qt) else f"#{i}") + (" (exit)" if "x" in b else ""))
                dlg.append(f"- `{c['key'].split('/')[-1]}` ({e.get('file', '')}): " + " / ".join(opts))
        dlg.append("")
    doc[3:3] = [f"Totals: {totals['dialogue']} dialogue lines, {totals['choice']} choice menus, {totals['timeline']} timelines across {len(chapters)} chapters.", "",
                "## To review (game logic types with no dedicated sync, all chapters)", "",
                ", ".join(f"`{t}` ({n})" for t, n in review_all.most_common()) or "none", ""]
    (ROOT / "docs").mkdir(exist_ok=True)
    (ROOT / "docs" / "SYNC-INVENTORY.md").write_text("\n".join(doc) + "\n", encoding="utf-8")
    gen = ROOT / "artifacts" / "generated"; gen.mkdir(parents=True, exist_ok=True)
    (gen / "DIALOGUES.md").write_text("\n".join(dlg) + "\n", encoding="utf-8")
    print(f"{len(chapters)} chapters, {totals['dialogue']} dialogue lines, {len(review_all)} types to review")


if __name__ == "__main__":
    main()
