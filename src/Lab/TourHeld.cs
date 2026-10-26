using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "held" (host only checks): the held-item detection reads the game's wiring right. Scene 1's toy plane
    // puts the plane in the hand (actor-only by default); Scene 2's exit door does not (mirrored). Every interaction
    // of both chapters is logged with its verdict.
    public sealed partial class TogetherRuntime
    {
        bool labHeldScene1Done, labHeldScene2Done;

        void HeldHost(float now, string scene)
        {
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 1 - RealRoom"); }
            if (LocalPlayer == null || now - tourSceneAt < 15) return;
            if (scene == "Scene 1 - RealRoom" && !labHeldScene1Done)
            {
                labHeldScene1Done = true;
                LogHeld(scene);
                ObjectInteractive plane = null;
                foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>(true)) if (o != null && o.name == "Interactive Air") plane = o;
                CheckOnce("held.plane", plane != null && HeldItems.Holds(plane), plane == null ? "no toy plane" : "toy plane holds an item: " + HeldItems.Holds(plane) + " -> rule " + CutsceneRules.ForInteraction(SceneKeys.Key(plane), null).Note);
                LoadThroughGame("Scene 2 - InGame");
                return;
            }
            if (scene == "Scene 2 - InGame" && !labHeldScene2Done)
            {
                labHeldScene2Done = true;
                LogHeld(scene);
                ObjectInteractive door = null;
                foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>(true)) if (o != null && o.name == "Door Exit") door = o;
                CheckOnce("held.door", door != null && !HeldItems.Holds(door), door == null ? "no exit door" : "exit door holds an item: " + HeldItems.Holds(door) + " -> rule " + CutsceneRules.ForInteraction(SceneKeys.Key(door), null).Note);
            }
        }

        void LogHeld(string scene)
        {
            int holds = 0, all = 0;
            foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>(true))
            {
                if (o == null) continue;
                all++;
                bool h = HeldItems.Holds(o);
                if (h) holds++;
                Log("Lab: held " + (h ? "YES " : "no  ") + SceneKeys.Key(o));
            }
            Log("Lab: " + scene + ": " + holds + " of " + all + " interactions put an item in the hand");
        }
    }
}
