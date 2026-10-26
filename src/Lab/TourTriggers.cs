using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "triggers" (2 players, Scene 11): the trigger relays end to end. The host drives a distance trigger
    // across its edge and a look-and-hold timer to its end through the same hooks the game's Update runs (the
    // prefix/postfix pairs), and the client must fire both on its own copy.
    public sealed partial class TogetherRuntime
    {
        float labTriggersAt = -1;

        void TriggersHost(float now, string scene)
        {
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; Log("Lab: host loading " + TourChapter); LoadThroughGame(TourChapter); }
            // The look-and-hold step runs in Scene 8, whose look triggers are active from the start.
            if (scene == "Scene 8 - ReRooms" && LocalPlayer != null && now - tourSceneAt > 25 && !labHoldDone) { labHoldDone = true; HoldStep(scene); return; }
            if (scene != TourChapter || LocalPlayer == null || now - tourSceneAt < 25 || labTriggersAt > 0) return;
            labTriggersAt = now;
            Trigger_DistanceCircle circle = null;
            foreach (Trigger_DistanceCircle t in Object.FindObjectsOfType<Trigger_DistanceCircle>()) if (t != null && !t.targetInside && circle == null) circle = t;
            if (circle == null) CheckOnce("triggers.distance", false, "no distance trigger in " + scene);
            else
            {
                DistanceBefore(circle);
                circle.targetInside = true;   // as its Update does when the target comes within the radius
                DistanceAfter(circle);
                CheckOnce("triggers.distance", true, "crossed " + SceneKeys.Key(circle).Split('/')[^1]);
            }
            Log("Lab: host loads Scene 8 for the look-and-hold step");
            LoadThroughGame("Scene 8 - ReRooms");
        }

        bool labHoldDone;

        void HoldStep(string scene)
        {
            Trigger_MouseEvent look = null;
            foreach (Trigger_MouseEvent t in Object.FindObjectsOfType<Trigger_MouseEvent>()) if (t != null && look == null) look = t;
            if (look == null) CheckOnce("triggers.hold", false, "no look trigger in " + scene);
            else
            {
                if (look.timeHold <= 0) look.timeHold = 1;
                look.timeHoldNow = 0;
                LookBefore(look);
                look.timeHoldNow = look.timeHold;   // looked at long enough
                LookAfter(look);
                CheckOnce("triggers.hold", true, "held the look on " + SceneKeys.Key(look).Split('/')[^1]);
            }
        }

        void TriggersClient()
        {
            if (distanceApplied > 0) CheckOnce("triggers.distance", true, "the host's distance trigger fired here");
            if (lookHoldsApplied > 0) CheckOnce("triggers.hold", true, "the host's look-and-hold fired here");
        }
    }
}
