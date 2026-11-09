using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // User report (2026-11-09, 0.3.18): stuck on Scene 4's "LOADING" and couldn't pass the level. It is the story: Quest 1/Loading comes on,
    // a player walks into its trigger, Mita's and the player's "Dialogue Loading" lines start, and only then does the room come back.
    // Lab tour "loading" (Scene 4): only the client walks in, through the physics call.
    //   loading.dialogue: (host) the host's Dialogue Loading has started
    //   loading.back: (both) the house is back and the loading floor is gone
    public sealed partial class TogetherRuntime
    {
        Transform labLoadRoot, labLoadMain;
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
                labLoadMain = LabLoadingMain();
                if (labLoadMain != null) labLoadMain.gameObject.SetActive(false); // the state the story leaves while the "loading" is up
                labLoadAt = now;
                Log("Lab: the Loading section is on");
                return;
            }
            float t2 = now - labLoadAt;
            if (IsClient && !labLoadWalked && t2 >= 5 && labLoadTrigger != null)
            {
                labLoadWalked = true;
                Log("Lab: client walks into the Loading trigger");
                labLoadTrigger.OnTriggerEnter(LocalPlayer.GetComponent<Collider>());
            }
            if (!IsClient && t2 >= 16)
            {
                Transform d = LabDialogueLoading();
                CheckOnce("loading.dialogue", d != null && d.gameObject.activeSelf, "the host's Dialogue Loading is " + (d == null ? "missing" : d.gameObject.activeSelf ? "on" : "off"));
            }
            Transform floor = labLoadRoot == null ? null : labLoadRoot.Find("Box Floor");
            bool back = labLoadMain != null && labLoadMain.gameObject.activeSelf && (floor == null || !floor.gameObject.activeSelf);
            if (back || t2 >= 90) CheckOnce("loading.back", back, "after " + t2.ToString("0") + " s the house is " + (labLoadMain != null && labLoadMain.gameObject.activeSelf ? "on" : "off") + ", the loading floor is " + (floor != null && floor.gameObject.activeSelf ? "on" : "off"));
        }

        static Transform LabLoadingMain()
        {
            foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
                if (t.name == "Main" && t.parent != null && t.parent.name == "House" && t.parent.parent != null && t.parent.parent.name == "HouseGame Tamagotchi") return t;
            return null;
        }
    }
}
