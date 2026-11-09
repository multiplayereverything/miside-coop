using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Scene 7's Quest 3: Mita holds out her hand, and walking into "Position HandHold/Trigger HandHold" starts it for that player.
    // Lab tour "handhold7" (Scene 7): only the client walks in, through the physics call.
    //   handhold7.client: (client) this player plays the hand-hold
    //   handhold7.host: (host) the host's story saw it and the host's player is free
    public sealed partial class TogetherRuntime
    {
        Trigger_Event labHold7;
        float labHold7At = -1;
        bool labHold7Walked, labHold7HostPlayed;

        void HandHold7Tour(float now, string scene)
        {
            if (scene != "Scene 7 - Backrooms" || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (labHold7At < 0)
            {
                foreach (Trigger_Event t in Object.FindObjectsOfType<Trigger_Event>(true))
                    if (t != null && t.name == "Trigger HandHold" && t.transform.parent != null && t.transform.parent.name == "Position HandHold") labHold7 = t;
                if (labHold7 == null) { CheckOnce("handhold7.client", false, "no trigger in " + scene); return; }
                for (Transform t = labHold7.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                labHold7At = now;
                return;
            }
            float at = now - labHold7At;
            if (IsClient)
            {
                if (at >= 5 && !labHold7Walked && labHold7 != null) { labHold7Walked = true; Log("Lab: client walks into Trigger HandHold"); labHold7.OnTriggerEnter(LocalPlayer.GetComponent<Collider>()); }
                if (at >= 6 && LocalPlayer.animationRun) CheckOnce("handhold7.client", true, "this player holds her hand");
                if (at >= 16) CheckOnce("handhold7.client", false, "this player never played it");
                return;
            }
            if (at >= 6 && LocalPlayer.animationRun) labHold7HostPlayed = true;
            if (at >= 14) CheckOnce("handhold7.host", LogSaw("Trigger HandHold") && !labHold7HostPlayed, "story saw it: " + LogSaw("Trigger HandHold") + ", the host played it: " + labHold7HostPlayed);
        }

        bool LogSaw(string part) { foreach (string k in logKeysSeen) if (k.Contains(part)) return true; return false; }
    }
}
