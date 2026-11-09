using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // User report (2026-11-09, 0.3.18, with pictures): one player slices the carrot, the other's stays whole. Lab tour "carrot" (Scene 3):
    // the host slices (Location3CutCarrot, the game's own Cut from its Update).
    //   carrot.follows: (client) the client's carrot has been cut too
    public sealed partial class TogetherRuntime
    {
        Location3CutCarrot labCarrot;
        float labCarrotAt = -1;
        bool labCarrotStarted;

        void CarrotTour(float now, string scene)
        {
            if (scene != "Scene 3 - WeTogether" || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (labCarrot == null)
            {
                labCarrot = Object.FindObjectOfType<Location3CutCarrot>(true);
                if (labCarrot == null) { CheckOnce("carrot.follows", false, "no carrot in " + scene); return; }
                for (Transform t = labCarrot.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                labCarrotAt = now;
                return;
            }
            float t2 = now - labCarrotAt;
            if (Hosting)
            {
                if (!labCarrotStarted && t2 >= 3) { labCarrotStarted = true; labCarrot.CutStart(); Log("Lab: host starts slicing"); }
                if (labCarrotStarted) labCarrot.autoCut = true;
                if (t2 >= 18) Log("Lab: host cut " + labCarrot.indexTimeCut);
                return;
            }
            if (t2 < 18) return;
            CheckOnce("carrot.follows", labCarrot.indexTimeCut >= 3, "client's carrot cut " + labCarrot.indexTimeCut + " times");
        }
    }
}
