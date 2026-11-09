using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "storypilot" (2 players, any chapter: run-tour.ps1 -Chapter "Scene 2 - InGame"): plays a chapter's story
    // as far as a player's simple actions take it. Every 3 s the host does the next one the game offers, each once:
    // answer an open choice menu (first answer), press any armed interactive, look at a look trigger under World/Quests
    // or World/Acts, or step into a trigger volume there. Every 30 s the host sends which quests are on; the
    // client must have the same ones (the story stays together).
    //   pilot.acted@host   at 360 s: the host did at least 3 story actions (and how far it got)
    //   pilot.same         at the end: the client's quests matched the host's at every comparison (at least 3)
    // -mt-pilotfull: runs until the chapter ends (15 min at most) and answers key prompts. -mt-pilotboth: also has the
    // client press every second interactive, so a client gets to be the Main Character. -mt-pilotclient: the client does
    // all of it (presses, prompts, choices, looks, trigger volumes) and the host only watches and asks.
    public sealed partial class TogetherRuntime
    {
        float labPilotAt, labPilotSendAt, labPilotStart;
        int labPilotActions, labPilotCompared, labPilotDiffer, labPilotShots;
        readonly HashSet<System.IntPtr> labPilotDone = new HashSet<System.IntPtr>();
        // Objects left to the client, and when: if it can't press one (it doesn't walk there), the story mustn't end there.
        readonly Dictionary<string, float> labPilotHanded = new Dictionary<string, float>();
        const float PilotClientSeconds = 10;
        string labPilotLastDiff = "", labPilotClientPress = "";
        bool labPilotFull, labPilotBoth, labPilotTurn, labPilotClient;
        string labPilotClientStep = "", labPilotClientLook = "";
        int labPilotFallbacks;
        float labPilotClientUntil, labPilotClientAt, labPilotProgressAt;
        int labPilotProgressSeq = -1;
        const float PilotStallSeconds = 50;   // the longest quiet stretch measured in Scenes 1-3 is 34 s

        // A player in an animation's loop answers its key prompt, as a player would.
        // Where an NPC's body really is. Mita's MitaPerson is a parent; the body that walks and plays her animations is the
        // child holding the Animator, so measuring the parent said "0.00 m apart" while the players saw her in different places.
        static Vector3 NpcBody(NpcRef n)
        {
            if (n.Anim == null) return n.Root.position;
            Transform hips = n.Anim.isHuman ? n.Anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            return hips != null ? hips.position : n.Anim.transform.position;
        }

        // -mt-mitacheck: the verifier compares her body on both copies, which only means something once she has walked.
        public static bool LabMitaCheck;
        Vector3 labMitaStart; bool labMitaSeen; float labMitaMovedAt = -1, labMitaLogAt;
        void MitaCheckTick(float now)
        {
            if (!LabMitaCheck || Mitas().Count == 0) return;
            NpcRef n = null; foreach (NpcRef x in Npcs()) if (x.Kind == KindMita && x.Root != null && x.Root.gameObject.activeInHierarchy) { n = x; break; }
            if (n == null) return;
            Vector3 body = NpcBody(n);
            // Five times a second: MT-STATE's two-second samples can't judge someone walking (tests/verify-lab.py).
            if (now >= labMitaLogAt) { labMitaLogAt = now + 0.2f; Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "MT-BODY {0:0.00} {1:0.00} {2:0.00}", body.x, body.y, body.z)); }
            if (!Hosting) return;
            if (!labMitaSeen) { labMitaSeen = true; labMitaStart = body; return; }
            if (labMitaMovedAt < 0 && (body - labMitaStart).magnitude > 1.5f) { labMitaMovedAt = now; LabShotAll("mita-walking"); }
            if (labMitaMovedAt > 0 && now - labMitaMovedAt > 45) { LabShotAll("mita-after"); CheckOnce("mita.moved", true, "Mita's body walked from " + labMitaStart + " to " + body); }
        }

        // The carrot is cut with the mouse. The game's own switch does it without one, but the game clears it after each cut.
        void PilotKeepsCutting()
        {
            foreach (Location3CutCarrot c in Object.FindObjectsOfType<Location3CutCarrot>())
                if (c != null && c.play) { c.autoCut = true; if (labPilotDone.Add(c.Pointer)) { Log("Lab: pilot cuts the carrot"); labPilotActions++; } }
            // The walk to the toilet is held with the Up key, which keeps this at 0.5.
            if (!NotMyMoment)
                foreach (Location3WalkToToilet w in Object.FindObjectsOfType<Location3WalkToToilet>())
                    if (w != null) { w.timeWalk = 0.5f; if (labPilotDone.Add(w.Pointer)) { Log("Lab: pilot walks to the toilet"); labPilotActions++; } }
        }

        // Of the keys on offer, the one pressed least: a menu (Scene 4's console: Change, Left, Right, Exit) gets every key in turn.
        readonly Dictionary<System.IntPtr, int> labPromptPresses = new Dictionary<System.IntPtr, int>();
        bool PilotAnswersPrompt()
        {
            if (!labPilotFull || LocalPlayer == null || (LocalPlayer.animationRun ? !LocalPlayer.animationLoopRun : !LocalPlayer.dontMove)) return false;
            Interface_KeyHint_Key pick = null; int least = int.MaxValue, n;
            foreach (Interface_KeyHint_Key k in Object.FindObjectsOfType<Interface_KeyHint_Key>())
                if (k != null && k.isActiveAndEnabled && !k.hide && ChapterScene(k.gameObject) && (labPromptPresses.TryGetValue(k.Pointer, out n) ? n : 0) < least)
                { pick = k; least = labPromptPresses.TryGetValue(k.Pointer, out n) ? n : 0; }
            if (pick == null) return false;
            labPromptPresses[pick.Pointer] = least + 1;
            Log("Lab: pilot answers the prompt " + PathOf(pick.transform)); pick.KeyDown(); if (labPilotDone.Add(pick.Pointer)) labPilotActions++;
            return true;
        }

        // Rooms are only on near a player (Scene 2's toilet, with its floor and its animator), and the pilot doesn't walk.
        // With nothing left to do, it goes where a story animator is waiting.
        bool PilotOpensRoom()
        {
            foreach (Animator_Functions f in Object.FindObjectsOfType<Animator_Functions>(true))
            {
                if (f == null || f.gameObject.activeInHierarchy || labPilotDone.Contains(f.Pointer)) continue;
                Animator a = f.GetComponent<Animator>();
                if (a == null || !a.enabled || f.gameObject.scene.name != ActiveSceneName || !PathOf(f.transform).Contains("/House/")) continue;
                labPilotDone.Add(f.Pointer);
                Log("Lab: pilot opens the room of " + PathOf(f.transform));
                for (Transform t = f.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                return true;
            }
            return false;
        }

        // The press can get here before the log has armed the object: keep trying for a while.
        void PilotClientTick(float now, string scene)
        {
            // Only the host is told the chapter, so a client can't compare against it.
            if (scene == "SceneMenu" || LocalPlayer == null || now < labPilotClientAt) return;
            labPilotClientAt = now + 1;
            if (PilotAnswersPrompt()) { labPilotClientAt = now + 2; return; }
            if (labPilotClient)
            {
                foreach (DialogueChanger d in Object.FindObjectsOfType<DialogueChanger>())
                    if (d != null && d.play && d.buttons != null && d.indexButtons != null && d.indexButtons.Count > 0)
                    {
                        int pick = d.indexButtons[0];
                        Log("Lab: pilot (client) answers " + d.name + " with " + pick); d.ClickButton(pick); return;
                    }
                if (labPilotClientLook.Length > 0)
                {
                    Trigger_MouseEvent look = SceneKeys.Find<Trigger_MouseEvent>(labPilotClientLook);
                    Log("Lab: pilot (client) looks at " + labPilotClientLook);
                    labPilotClientLook = "";
                    if (look != null) labPilotClientAt = now + LabGaze(look);
                    return;
                }
                if (labPilotClientStep.Length > 0)
                {
                    Trigger_Event step = SceneKeys.Find<Trigger_Event>(labPilotClientStep);
                    Collider box = step != null ? step.GetComponent<Collider>() : null;
                    Log("Lab: pilot (client) steps into " + labPilotClientStep);
                    labPilotClientStep = "";
                    if (box != null) { Vector3 p = box.bounds.center; p.y = Mathf.Max(box.bounds.min.y, LocalPlayer.transform.position.y - 5); LocalPlayer.TeleportPlayer(p); step.TargetEnter(); }
                    return;
                }
            }
            if (labPilotClientPress.Length == 0) return;
            ObjectInteractive o = SceneKeys.Find<ObjectInteractive>(labPilotClientPress);
            PilotStepTo(o);
            if (LabPress(o))
            {
                Log("Lab: pilot (client) presses " + labPilotClientPress);
                labPilotClientPress = "";
            }
            else if (now > labPilotClientUntil) { Log("Lab: pilot (client) gave up on " + labPilotClientPress); labPilotClientPress = ""; }
        }

        // A chapter's scene: the one active, or one the story loaded beside it (Scene 5 loads Scene 6 additively).
        static bool ChapterScene(GameObject g) { return g.scene.name != null && g.scene.name.StartsWith("Scene ", System.StringComparison.Ordinal); }

        // The story's own objects: quests and acts (not the house's ambient triggers).
        static bool StoryPath(Transform t) { string p = PathOf(t); return p.Contains("/Quests/") || p.Contains("/Acts/") || p.Contains("/Act/"); }

        // What the pilot compares between copies: the quests on, and the shared key inventory (Scene 2's batteries).
        string PilotState()
        {
            LabRooms();   // logged (MT-ROOMS), not compared: camera-driven objects legitimately differ between PCs
            if (cachedController == null) cachedController = Object.FindObjectOfType<GameController>();
            List<string> keys = new List<string>();
            if (cachedController != null && cachedController.inventoryKeysCase != null)
                foreach (GameObject g in cachedController.inventoryKeysCase) if (g != null) keys.Add(g.name);
            keys.Sort(System.StringComparer.Ordinal);
            return ActiveQuests() + " keys:" + string.Join(",", keys);
        }

        // User report (2026-11-08): "after the hallway ... the room doesn't sync". What is switched on in the house: the
        // rooms by name (three levels down), and a count of everything under them, so a difference deeper down shows too.
        static string LabRooms()
        {
            GameObject world = GameObject.Find("World");
            Transform house = world != null ? world.transform : null;
            if (house == null) return "";
            List<string> on = new List<string>();
            int all = 0;
            foreach (Transform t in house.GetComponentsInChildren<Transform>(false))
            {
                all++;
                int depth = 0;
                for (Transform p = t; p != null && p != house; p = p.parent) depth++;
                if (depth > 0 && depth <= 3) on.Add(t.name);
            }
            on.Sort(System.StringComparer.Ordinal);
            string names = string.Join(",", on);
            if (names != labRoomNames) { labRoomNames = names; Log("MT-ROOMS " + all + " " + names); }
            return all + ":" + Wire.Hash(names).ToString("x8");
        }
        static string labRoomNames = "";

        static string ActiveQuests()
        {
            GameObject quests = SceneObject("Quests");
            if (quests == null) return "";
            List<string> on = new List<string>();
            for (int i = 0; i < quests.transform.childCount; i++) { Transform q = quests.transform.GetChild(i); if (q.gameObject.activeSelf) on.Add(q.name); }
            return string.Join("|", on);
        }

        void StoryPilotTick(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            string chapter = TourChapter;
            if (labPilotFull) { PilotKeepsCutting(); PilotFights(now); PilotPlaysCards(now); PilotHolds(now); }
            LabPropsInHandTick(now);
            MitaCheckTick(now);
            if (IsClient && labPilotFull) PilotClientTick(now, scene);
            if (Hosting && !tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame(chapter); return; }
            // The verdict at 360 s, wherever the story has taken the host by then (it may be the next chapter).
            // A full run that has stopped moving has nothing more to tell: end it rather than wait out the time limit.
            int progress = logKeysSeen.Count + labPilotActions;   // a solo game has no log, only the pilot's own actions
            // A minigame being played is progress too: its rounds log nothing until they end.
            if (progress != labPilotProgressSeq || minigames.Count > 0) { labPilotProgressSeq = progress; labPilotProgressAt = now; }
            bool stalled = labPilotFull && now - labPilotProgressAt > PilotStallSeconds;
            if (stalled && labPilotStart > 0 && !labChecked.Contains("pilot.acted")) { Log("Lab: pilot stalled, no story event for " + PilotStallSeconds + " s"); LogPressable(); Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-pressable"))); }
            bool pilotOver = labPilotStart > 0 && (labPilotFull ? stalled || (scene != chapter && scene != "SceneMenu" && now - tourSceneAt > 15) || now - labPilotStart > 1400 : now - labPilotStart > 360);
            if (Hosting && tourStarted && pilotOver && !labChecked.Contains("pilot.acted"))
            {
                LabTvChecks(); LabPropsInHandCheck();
                CheckOnce("pilot.acted",labPilotActions >= 3 && (!labPilotFull || scene != chapter),labPilotActions + " story actions; quests now " + ActiveQuests() + "; scene " + scene);
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-pilot-end")));
                return;
            }
            if (scene != chapter || LocalPlayer == null || now - tourSceneAt < 25 || !Hosting || now < labPilotAt) return;
            if (labPilotStart <= 0) labPilotStart = now;
            labPilotAt = now + 1.5f;
            if (now >= labPilotSendAt)
            {
                labPilotSendAt = now + 10;
                string q = PilotState();
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-pilot:" + q)));
                // What each player sees at each comparison, to set side by side (tools/shots_to_png.py).
                LabShotAll("pilot-" + Slug(scene) + "-" + (labPilotShots++).ToString("00"));
                if (LabSnapOn) LabSnapAll();
            }
            if (!labPilotClient && PilotAnswersPrompt()) { labPilotAt = now + 2; return; }
            // A choice can come up while sitting (Scene 3's chair), and a player answers it then.
            foreach (DialogueChanger d in Object.FindObjectsOfType<DialogueChanger>())
                if (!labPilotClient && d != null && d.play && d.buttons != null && d.indexButtons != null && d.indexButtons.Count > 0)
                {
                    int pick = d.indexButtons[0];
                    Log("Lab: pilot answers " + d.name + " with " + pick); d.ClickButton(pick); labPilotActions++; return;
                }
            if (LocalPlayer.animationRun) return;   // let an interaction's animation finish first
            foreach (KeyValuePair<string, float> handed in new Dictionary<string, float>(labPilotHanded))
            {
                ObjectInteractive left = SceneKeys.Find<ObjectInteractive>(handed.Key);
                if (left == null || logKeysSeen.Contains(handed.Key)) { labPilotHanded.Remove(handed.Key); continue; }
                if (now - handed.Value < PilotClientSeconds || !LabPress(left)) continue;
                labPilotHanded.Remove(handed.Key);
                labPilotFallbacks++;
                Log("Lab: the client didn't press it, pilot presses " + PathOf(left.transform) + " (" + labPilotFallbacks + " so far)"); return;
            }
            foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                if (o != null && o.active && o.isActiveAndEnabled && !labPilotDone.Contains(o.Pointer) && ChapterScene(o.gameObject))
                {
                    bool toClient = (labPilotClient || (labPilotBoth && !labPilotTurn)) && AcceptedCount > 0;
                    if (!toClient) PilotStepTo(o);
                    if (!toClient && !LabPress(o)) return;   // the press happens in that object's next Update
                    labPilotDone.Add(o.Pointer); labPilotActions++;
                    labPilotTurn = !labPilotTurn;
                    if (toClient)
                    {
                        string key = SceneKeys.Key(o);
                        labPilotHanded[key] = now;
                        Log("Lab: pilot leaves to the client " + PathOf(o.transform));
                        Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-pilotpress:" + key)));
                        labPilotAt = now + 3;
                        return;
                    }
                    Log("Lab: pilot presses " + PathOf(o.transform)); return;
                }
            foreach (Trigger_MouseEvent t in Object.FindObjectsOfType<Trigger_MouseEvent>())
                if (t != null && t.isActiveAndEnabled && !labPilotDone.Contains(t.Pointer) && StoryPath(t.transform) && ChapterScene(t.gameObject))
                {
                    labPilotDone.Add(t.Pointer); labPilotActions++;
                    if (labPilotClient && AcceptedCount > 0) { string key = SceneKeys.Key(t); Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-pilotlook:" + key))); labPilotAt = now + 3; return; }
                    Log("Lab: pilot looks at " + PathOf(t.transform)); labPilotAt = now + LabGaze(t); return;
                }
            foreach (Trigger_Event t in Object.FindObjectsOfType<Trigger_Event>())
            {
                if (t == null || !t.isActiveAndEnabled || labPilotDone.Contains(t.Pointer) || !StoryPath(t.transform) || !ChapterScene(t.gameObject)) continue;
                Collider c = t.GetComponent<Collider>();
                if (c == null) continue;
                labPilotDone.Add(t.Pointer); labPilotActions++;
                if (labPilotClient && AcceptedCount > 0) { string key = SceneKeys.Key(t); Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-pilotstep:" + key))); labPilotAt = now + 3; return; }
                Vector3 p = c.bounds.center; p.y = Mathf.Max(c.bounds.min.y, LocalPlayer.transform.position.y - 5);
                Log("Lab: pilot steps into " + PathOf(t.transform) + " at " + p);
                LocalPlayer.TeleportPlayer(p);
                // The teleport stayed put in Scene 2, so full runs set the trigger off directly.
                if (labPilotFull) t.TargetEnter();
                return;
            }
            if (!labPilotFull) return;
            // The end of the long corridor, which a player reaches by walking.
            foreach (Location2LongCorridor c in Object.FindObjectsOfType<Location2LongCorridor>())
                if (c != null && c.isActiveAndEnabled && c.eventNear != null && !labPilotDone.Contains(c.Pointer))
                {
                    labPilotDone.Add(c.Pointer); labPilotActions++;
                    Log("Lab: pilot reaches the end of " + PathOf(c.transform));
                    c.eventNear.Invoke();
                    return;
                }
            PilotOpensRoom();
        }

        // A player presses what they stand at. Pressed from across the house, a take sends the hand out for an item it
        // never reaches (Scene 6's key in the basement, the client upstairs): the pilot first goes there, onto the
        // walkable floor nearest the object.
        void PilotStepTo(Component o)
        {
            PlayerMove p = LocalPlayer;
            if (p == null || o == null || (p.transform.position - o.transform.position).magnitude < 2f) return;
            UnityEngine.AI.NavMeshHit hit;
            if (!UnityEngine.AI.NavMesh.SamplePosition(o.transform.position, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) return;
            Log("Lab: pilot goes to " + PathOf(o.transform) + " (" + (p.transform.position - o.transform.position).magnitude.ToString("0.0") + " m away)");
            p.TeleportPlayer(hit.position);
        }

        // Where a run stalls: what each PC could press there (story objects in chapter scenes), to compare the two logs.
        public void LogPressable() { Log("Lab: pressable here: " + Pressable()); }

        static string Pressable()
        {
            List<string> on = new List<string>();
            foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                // Switched on by the story; not ObjectInteractive.active, which the game also clears while the player is busy.
                if (o != null && o.isActiveAndEnabled && ChapterScene(o.gameObject) && StoryPath(o.transform)) on.Add(PathOf(o.transform));
            on.Sort(System.StringComparer.Ordinal);
            return string.Join(" | ", on);
        }

        // pilot.pressable (host): at the end, the client can press the same story objects as the host (what one PC's story
        // switched on or used up, the other's did too; Scene 6's key stayed on the host's table after the client took it).
        public void LabPressableNotice(string theirs)
        {
            string here = Pressable();
            if (here.Length > 1000 - "lab-pressablelist:".Length) here = here.Substring(0, 1000 - "lab-pressablelist:".Length);   // as the client's was cut
            CheckOnce("pilot.pressable", theirs == here, theirs == here ? "same " + (here.Length == 0 ? 0 : here.Split('|').Length) + " objects" : "client " + theirs + " / host " + here);
        }

        void StoryPilotNotice(string notice)
        {
            string host = notice.Substring("lab-pilot:".Length), here = PilotState();
            labPilotCompared++;
            // A quest the host just switched can take a moment here: a difference counts if it is still there next time.
            if (host != here) { if (labPilotLastDiff == host + "#" + here) labPilotDiffer++; labPilotLastDiff = host + "#" + here; Log("Lab: pilot quests differ: host " + host + " / here " + here); }
            else labPilotLastDiff = "";
        }

        void StoryPilotPress(string notice)
        {
            labPilotClientPress = notice.Substring("lab-pilotpress:".Length);
            labPilotClientUntil = Time.realtimeSinceStartup + 8;
        }

        // pilot.keys: every story key and shared key item the host sent was found here as it is, without guessing by name.
        void StoryPilotEnd()
        {
            LabTvChecks(); LabPropsInHandCheck();
            // Also when the chapter doesn't end within the tour (Scene 5 goes on into Scene 6 beside it).
            string pressable = "lab-pressablelist:" + Pressable();
            SendToHost(Wire.Packet(Op.Notice, w => Wire.Text(w, pressable.Length > 1000 ? pressable.Substring(0, 1000) : pressable)));
            if (logApplied > 0) CheckOnce("log.clean", logLost == 0, "applied " + logApplied + ", waited " + logWaited + ", lost " + logLost + ", swallowed " + gateSwallowed);
            CheckOnce("pilot.keys",SceneKeys.NameMatched == 0 && keyItemsMissed == 0, SceneKeys.NameMatched + " keys found only by name, " + keyItemsMissed + " key items not found");
            StoryPilotSame();
        }
        void StoryPilotSame() { CheckOnce("pilot.same", labPilotCompared >= 3 && labPilotDiffer == 0, labPilotCompared + " comparisons, " + labPilotDiffer + " lasting differences; quests here " + ActiveQuests() + " in " + ActiveSceneName); }
    }
}
