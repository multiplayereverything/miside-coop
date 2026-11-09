using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "trigwalk" (2 players, Scene 2). A player walking into a story volume reaches the game through OnTriggerEnter, the call
    // the physics engine makes; it has its own copy of TargetEnter's logic and never calls it. Tours that call TargetEnter miss this.
    //   trigwalk.relayed: (host) after only the client walked in, the volume has gone off here too
    public sealed partial class TogetherRuntime
    {
        Trigger_Event labWalkTrigger;
        float labWalkAt = -1;
        bool labWalked;

        void TrigWalkTour(float now, string scene)
        {
            if (scene != "Scene 2 - InGame" || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (labWalkTrigger == null)
            {
                string best = null;
                foreach (Trigger_Event t in Object.FindObjectsOfType<Trigger_Event>())
                {
                    if (t == null || t.triggerActive || t.enterTime || t.loop || t.objectTarget != LocalPlayer.gameObject || !StoryPath(t.transform)) continue;
                    string key = SceneKeys.Key(t);
                    if (best == null || string.CompareOrdinal(key, best) < 0) { best = key; labWalkTrigger = t; }
                }
                if (labWalkTrigger == null) { if (now - tourSceneAt > 40) CheckOnce("trigwalk.relayed", false, "no story volume to walk into"); return; }
                labWalkAt = now;
                Log("Lab: volume for the walk " + best);
                return;
            }
            if (IsClient)
            {
                if (labWalked || now - labWalkAt < 3) return;
                labWalked = true;
                Log("Lab: client walks into the volume");
                labWalkTrigger.OnTriggerEnter(LocalPlayer.GetComponent<Collider>());
                return;
            }
            if (now - labWalkAt < 12) return;
            CheckOnce("trigwalk.relayed", labWalkTrigger.triggerActive || labWalkTrigger.enterTime, "the host's copy of the volume went off: " + (labWalkTrigger.triggerActive || labWalkTrigger.enterTime));
        }
    }
}
