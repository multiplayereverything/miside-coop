using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // User report (2026-11-09, 0.3.18): stuck on Scene 4's "LOADING" and couldn't pass the level. It is the story: Quest 1/Loading comes on,
    // a player walks into its trigger, Mita's and the player's "Dialogue Loading" lines start, and only then does the room come back.
    // Lab tour "loading" (Scene 4): only the client walks in, through the physics call.
    //   loading.dialogue: (host) the host's Dialogue Loading has started
    public sealed partial class TogetherRuntime
    {
        Transform labLoadRoot;
        Trigger_Event labLoadTrigger;
        float labLoadAt = -1;
        bool labLoadWalked;

        static Transform LabDialogueLoading()
        {
            foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
                if (t.name == "Dialogue Loading" && t.parent != null && t.parent.name == "Dialogue Mita") return t;
            return null;
        }

        void LoadingTour(float now, string scene)
        {
            if (scene != "Scene 4 - StartSecret" || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (labLoadAt < 0)
            {
                foreach (Trigger_Event t in Object.FindObjectsOfType<Trigger_Event>(true))
                    if (t != null && t.name == "Trigger" && t.transform.parent != null && t.transform.parent.name == "Loading") labLoadTrigger = t;
                if (labLoadTrigger == null) { CheckOnce("loading.dialogue", false, "no Loading trigger in " + scene); return; }
                labLoadRoot = labLoadTrigger.transform.parent;
                for (Transform t = labLoadRoot; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                labLoadAt = now;
                Log("Lab: the Loading section is on");
                return;
            }
            float t2 = now - labLoadAt;
            if (IsClient)
            {
                if (labLoadWalked || t2 < 5 || labLoadTrigger == null) return;
                labLoadWalked = true;
                Log("Lab: client walks into the Loading trigger");
                labLoadTrigger.OnTriggerEnter(LocalPlayer.GetComponent<Collider>());
                return;
            }
            if (t2 < 16) return;
            Transform d = LabDialogueLoading();
            CheckOnce("loading.dialogue", d != null && d.gameObject.activeSelf, "the host's Dialogue Loading is " + (d == null ? "missing" : d.gameObject.activeSelf ? "on" : "off"));
        }
    }
}
