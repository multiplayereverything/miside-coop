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
    public sealed partial class TogetherRuntime
    {
        float labPilotAt, labPilotSendAt, labPilotStart;
        int labPilotActions, labPilotCompared, labPilotDiffer;
        readonly HashSet<System.IntPtr> labPilotDone = new HashSet<System.IntPtr>();
        string labPilotLastDiff = "";

        // The story's own objects: quests and acts (not the house's ambient triggers).
        static bool StoryPath(Transform t) { string p = PathOf(t); return p.Contains("/Quests/") || p.Contains("/Acts/"); }

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
            if (Hosting && !tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame(chapter); return; }
            // The verdict at 360 s, wherever the story has taken the host by then (it may be the next chapter).
            if (Hosting && tourStarted && now - labPilotStart > 360 && labPilotStart > 0 && !labChecked.Contains("pilot.acted"))
            {
                CheckOnce("pilot.acted", labPilotActions >= 3, labPilotActions + " story actions; quests now " + ActiveQuests() + "; scene " + scene);
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-pilot-end")));
                return;
            }
            if (scene != chapter || LocalPlayer == null || now - tourSceneAt < 25 || !Hosting || now < labPilotAt) return;
            if (labPilotStart <= 0) labPilotStart = now;
            labPilotAt = now + 3;
            if (now >= labPilotSendAt)
            {
                labPilotSendAt = now + 30;
                string q = ActiveQuests();
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-pilot:" + q)));
            }
            if (LocalPlayer.animationRun) return;   // let an interaction's animation finish first
            foreach (DialogueChanger d in Object.FindObjectsOfType<DialogueChanger>())
                if (d != null && d.play && d.buttons != null && d.indexButtons != null && d.indexButtons.Count > 0)
                {
                    int pick = d.indexButtons[0];
                    Log("Lab: pilot answers " + d.name + " with " + pick); d.ClickButton(pick); labPilotActions++; return;
                }
            foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                if (o != null && o.active && o.isActiveAndEnabled && !labPilotDone.Contains(o.Pointer))
                {
                    labPilotDone.Add(o.Pointer); labPilotActions++;
                    Log("Lab: pilot presses " + PathOf(o.transform)); o.Click(); return;
                }
            foreach (Trigger_MouseEvent t in Object.FindObjectsOfType<Trigger_MouseEvent>())
                if (t != null && t.isActiveAndEnabled && !labPilotDone.Contains(t.Pointer) && StoryPath(t.transform))
                {
                    labPilotDone.Add(t.Pointer); labPilotActions++;
                    Log("Lab: pilot looks at " + PathOf(t.transform)); t.Enter(); return;
                }
            foreach (Trigger_Event t in Object.FindObjectsOfType<Trigger_Event>())
            {
                if (t == null || !t.isActiveAndEnabled || labPilotDone.Contains(t.Pointer) || !StoryPath(t.transform)) continue;
                Collider c = t.GetComponent<Collider>();
                if (c == null) continue;
                labPilotDone.Add(t.Pointer); labPilotActions++;
                Vector3 p = c.bounds.center; p.y = Mathf.Max(c.bounds.min.y, LocalPlayer.transform.position.y - 5);
                Log("Lab: pilot steps into " + PathOf(t.transform) + " at " + p);
                LocalPlayer.TeleportPlayer(p);
                return;
            }
        }

        void StoryPilotNotice(string notice)
        {
            string host = notice.Substring("lab-pilot:".Length), here = ActiveQuests();
            labPilotCompared++;
            // A quest the host just switched can take a moment here: a difference counts if it is still there next time.
            if (host != here) { if (labPilotLastDiff == host + "#" + here) labPilotDiffer++; labPilotLastDiff = host + "#" + here; Log("Lab: pilot quests differ: host " + host + " / here " + here); }
            else labPilotLastDiff = "";
        }

        void StoryPilotEnd() { CheckOnce("pilot.same", labPilotCompared >= 3 && labPilotDiffer == 0, labPilotCompared + " comparisons, " + labPilotDiffer + " lasting differences; quests here " + ActiveQuests() + " in " + ActiveSceneName); }
    }
}
