using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "storylog" (2 players, Scene 1): an event that can't run yet on the client must wait, longer than the old
    // relay's 5 s retry, and the event after it must wait behind it. With -mt-nolog the old relay throws the first away and
    // runs the second alone, so storylog.order fails.
    //   storylog.gated: the client's own timers and animation ends ran nothing; the host's reached it through the log
    //   storylog.same:  after that, both copies have the same quests on
    public sealed partial class TogetherRuntime
    {
        const float LabHoldSeconds = 8;
        float labLogFirstSeenAt = -1, labLogSentAt = -1, labLogSameNext;
        string labLogOrder = "";
        bool labLogSent;

        // The test event: "first" isn't ready here until it has waited longer than the old relay would have.
        void ApplyLabOrdered(string which)
        {
            float now = Time.realtimeSinceStartup;
            if (which == "first")
            {
                if (labLogFirstSeenAt < 0) labLogFirstSeenAt = now;
                if (now - labLogFirstSeenAt < LabHoldSeconds) { storyMissing = true; return; }
            }
            labLogOrder += (labLogOrder.Length > 0 ? "," : "") + which;
        }

        void StoryLogTour(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (!Connected) return;
            if (Hosting && AcceptedCount == 0) return;
            if (Hosting && !tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 1 - RealRoom"); return; }
            if (scene != "Scene 1 - RealRoom" || now - tourSceneAt < 20) return;
            if (Hosting)
            {
                if (!labLogSent)
                {
                    labLogSent = true; labLogSentAt = now;
                    Story(Op.Call, "99:first", 0);
                    Story(Op.Call, "99:second", 0);
                    CheckOnce("storylog.sent", true, "two test events sent");
                }
                LabScene1Tutorial(now);
                if (now >= labLogSameNext && now - labLogSentAt > 40) { labLogSameNext = now + 10; string q = ActiveQuests(); Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-logsame:" + q))); }
                return;
            }
            LabRequestStep(now);
            if (labLogOrder.Length > 0 && (labLogOrder.Contains("second") || now - tourSceneAt > 60))
                CheckOnce("storylog.order", labLogOrder == "first,second", "ran here in the order: " + labLogOrder);
            if (now - tourSceneAt > 70 && !labChecked.Contains("storylog.order")) CheckOnce("storylog.order", false, "ran here: '" + labLogOrder + "'");
            if (now - tourSceneAt > 60)
                CheckOnce("storylog.gated", gateSwallowed > 0 && logApplied > 2, gatedEvents.Count + " events gated, " + gateSwallowed + " swallowed here, " + logApplied + " applied from the log, " + logWaited + " waits");
        }

        // A client's press does nothing here until the host's log brings it back.
        string labRequestKey;
        float labRequestAt = -1;

        void LabRequestStep(float now)
        {
            if (labRequestKey == null)
            {
                if (now - tourSceneAt > 150) { CheckOnce("storylog.request", false, "no armed interactive appeared on the client"); labRequestKey = ""; return; }
                foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                {
                    if (o == null || !o.active || !o.isActiveAndEnabled || o.name == "Interactive Mobile") continue;
                    labRequestKey = SceneKeys.Key(o); labRequestAt = now;
                    o.Click();
                    Log("Lab: client presses " + labRequestKey + "; still armed right after: " + o.active);
                    if (!o.active) { CheckOnce("storylog.request", false, "the press ran here at once, without the host"); labRequestKey = ""; }
                    return;
                }
                return;
            }
            if (labRequestKey.Length == 0) return;
            if (LastAppliedStory == "Interact " + labRequestKey) { CheckOnce("storylog.request", true, "ran here only after the host's log, " + ((now - labRequestAt) * 1000).ToString("0") + " ms after the press (at most one frame of it is this check)"); labRequestKey = ""; }
            else if (now - labRequestAt > 10) { CheckOnce("storylog.request", false, "the host's log never brought the press back"); labRequestKey = ""; }
        }

        void StoryLogSameNotice(string notice)
        {
            string host = notice.Substring("lab-logsame:".Length), here = ActiveQuests();
            CheckOnce("storylog.same", host == here, "host: " + host + " | here: " + here);
        }
    }
}
