using UnityEngine;

namespace MiSideTogether
{
    // Lab tour "passlevel" (2 players, from Scene 2): the debug page's pass-level press takes everyone to the next chapter.
    //   passlevel.arrived: this PC is in Scene 3 with a player after one press
    //   passlevel.twice: this PC is in Scene 4 with a player after a second press
    public sealed partial class TogetherRuntime
    {
        int labPassPresses;

        void PassLevelTour(float now, string scene)
        {
            if (LocalPlayer == null) return;
            if (scene == "Scene 3 - WeTogether") CheckOnce("passlevel.arrived", true, "in " + scene + " with a player");
            if (scene == "Scene 4 - StartSecret") CheckOnce("passlevel.twice", true, "in " + scene + " with a player");
            if (!Hosting || now - tourSceneAt < 25) return;
            if ((scene == "Scene 2 - InGame" && labPassPresses == 0) || (scene == "Scene 3 - WeTogether" && labPassPresses == 1))
            {
                labPassPresses++;
                Log("Lab: pass level pressed in " + scene);
                DebugPassLevel();
            }
        }
    }
}
