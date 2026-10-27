using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "quest" (2 players, Scene 14): the point-and-click quest game on the PC (QuestGame.cs). Once the host's
    // quest game is live, the host clicks in it through the game's own click path (ClickMouse around PlayerQuest.Move,
    // with the point the mouse raycast would give: a live Location14_QuestInteractive), then tells the client where its
    // quest character ended up. Until then it logs what is armed every 10 s, which shows how the chapter starts it.
    //   quest.clicked@host   the host's click walked its quest character
    //   quest.moved          the client's quest character walked to the same spot (within 0.3 m), from the relay
    public sealed partial class TogetherRuntime
    {
        float labQuestClickAt = -1, labQuestLogAt;
        readonly HashSet<string> labQuestPressed = new HashSet<string>();
        bool labQuestOpened;

        // Switches the PC's quest game on (with its parents) and arms its targets, as the story does after the phone.
        void LabOpenQuest()
        {
            foreach (Location14_GameQuestPlayer game in Object.FindObjectsOfType<Location14_GameQuestPlayer>(true))
            {
                if (game == null) continue;
                for (Transform t = game.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                foreach (Location14_PlayerQuest q in Object.FindObjectsOfType<Location14_PlayerQuest>(true))
                    for (Transform t = q.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                try { game.ActiveInteractives(true); } catch (System.Exception ex) { Log("Lab: ActiveInteractives threw: " + ex.Message); }
                Log("Lab: opened the quest game " + SceneKeys.Key(game));
            }
        }

        static Location14_PlayerQuest LiveQuest()
        {
            foreach (Location14_PlayerQuest q in Object.FindObjectsOfType<Location14_PlayerQuest>()) if (q != null && q.isActiveAndEnabled) return q;
            return null;
        }

        void QuestHost(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 14 - MobilePlayer"); }
            if (scene != "Scene 14 - MobilePlayer" || LocalPlayer == null || now - tourSceneAt < 20) return;
            CheckOnce("chapter.loaded", true, scene);
            Location14_PlayerQuest quest = LiveQuest();
            List<Location14_QuestInteractive> targets = new List<Location14_QuestInteractive>();
            foreach (Location14_QuestInteractive qi in Object.FindObjectsOfType<Location14_QuestInteractive>()) if (qi != null && qi.isActiveAndEnabled && qi.active) targets.Add(qi);
            if (labQuestClickAt < 0)
            {
                if (now >= labQuestLogAt)
                {
                    labQuestLogAt = now + 10;
                    List<string> armed = new List<string>();
                    ObjectInteractive next = null;
                    foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                    {
                        if (o == null || !o.active || !o.isActiveAndEnabled) continue;
                        armed.Add(o.name);
                        if (next == null && !labQuestPressed.Contains(SceneKeys.Key(o))) next = o;
                    }
                    Log("Lab: quest game " + (quest != null ? "live" : "not live") + ", " + targets.Count + " quest target(s); armed: " + string.Join(", ", armed));
                    // The story reaches the PC game through what it arms on the way (the phone first): press each once.
                    if (quest == null && next != null && !LocalPlayer.animationRun)
                    {
                        labQuestPressed.Add(SceneKeys.Key(next));
                        Log("Lab: host presses " + SceneKeys.Key(next) + " on the way to the quest game");
                        next.Click();
                    }
                }
                // The phone section before it needs input the lab cannot give: after 60 s every machine opens the quest
                // game itself (test harness, LabOpenQuest), which tests the relay but not the story's way there.
                if ((quest == null || targets.Count == 0) && now - tourSceneAt > 60 && !labQuestOpened)
                {
                    labQuestOpened = true;
                    Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-quest-open")));
                    LabOpenQuest();
                }
                if (quest == null || targets.Count == 0) { if (now - tourSceneAt > 300) CheckOnce("quest.clicked", false, "the quest game never went live"); return; }
                Location14_QuestInteractive target = targets[0];
                Vector3 before = quest.transform.position;
                QuestClickPrefix();
                try { quest.Move(target.positionMove, target.eventFinishMove); }
                finally { QuestClickPostfix(); }
                labQuestClickAt = now;
                Log("Lab: host clicked quest target " + SceneKeys.Key(target) + " from " + before);
                return;
            }
            if (now - labQuestClickAt > 8 && !labChecked.Contains("quest.clicked"))
            {
                Vector3 at = quest != null ? quest.transform.position : Vector3.zero;
                CheckOnce("quest.clicked", quest != null, "host's quest character at " + at);
                string text = string.Format(CultureInfo.InvariantCulture, "lab-quest:{0:R},{1:R},{2:R}", at.x, at.y, at.z);
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, text)));
            }
        }

        void QuestNotice(string notice)
        {
            string[] c = notice.Substring("lab-quest:".Length).Split(',');
            NumberStyles f = NumberStyles.Float; CultureInfo inv = CultureInfo.InvariantCulture;
            if (c.Length != 3 || !float.TryParse(c[0], f, inv, out float x) || !float.TryParse(c[1], f, inv, out float y) || !float.TryParse(c[2], f, inv, out float z)) return;
            Location14_PlayerQuest quest = LiveQuest();
            float d = quest != null ? Vector3.Distance(quest.transform.position, new Vector3(x, y, z)) : -1;
            CheckOnce("quest.moved", quest != null && d >= 0 && d < 0.3f && questMovesApplied > 0,
                (quest != null ? "client's quest character " + d.ToString("F2", inv) + " m from the host's" : "no live quest game here") + ", relayed moves applied " + questMovesApplied);
        }
    }
}
