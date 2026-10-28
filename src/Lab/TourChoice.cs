using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "choice" (2 players, Scene 3): a dialogue choice (DialogueChanger, relayed as Op.DialogueChoice). The host
    // opens Mita's choice menu while walking with her (Quest 2 Walking) and picks the first answer; the client must apply
    // it without an error, from its own menu (playtest: ClickButton threw a NullReferenceException on the client). Both
    // copies also count Unity's NavMesh "SetDestination" errors (the other playtest error in the same scene).
    //   choice.picked@host   the host's menu opened and the first answer was picked
    //   choice.applied       the client applied the host's answer through its own menu
    //   choice.navmesh       no "SetDestination" errors on the client
    public sealed partial class TogetherRuntime
    {
        float labChoiceOpenAt = -1;
        bool labChoicePicked;
        public int dialogueChoicesApplied;
        public static int navMeshErrors;

        void ChoiceHost(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 3 - WeTogether"); }
            if (scene != "Scene 3 - WeTogether" || LocalPlayer == null || now - tourSceneAt < 30) return;
            DialogueChanger menu = null;
            foreach (DialogueChanger d in Object.FindObjectsOfType<DialogueChanger>(true))
                if (d != null && PathOf(d.transform).Contains("Quest 2 Walking")) menu = d;
            if (menu == null) { if (now - tourSceneAt > 120) CheckOnce("choice.picked", false, "no choice menu in Quest 2 Walking"); return; }
            if (labChoiceOpenAt < 0)
            {
                for (Transform t = menu.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                menu.Play();
                labChoiceOpenAt = now;
                Log("Lab: host opens the choice menu " + SceneKeys.Key(menu));
                return;
            }
            if (labChoicePicked || now - labChoiceOpenAt < 3) return;
            labChoicePicked = true;
            bool open = menu.play && menu.buttons != null;
            if (open) menu.ClickButton(0);
            CheckOnce("choice.picked", open, open ? "picked the first answer" : "the menu did not open (play=" + menu.play + ")");
            Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-choice")));
        }

        float labChoiceNoticeAt = -1;

        void ChoiceClient(float now, string scene)
        {
            if (scene != "Scene 3 - WeTogether" || labChoiceNoticeAt < 0 || now - labChoiceNoticeAt < 8) return;
            CheckOnce("choice.applied", dialogueChoicesApplied > 0, "dialogue choices applied here: " + dialogueChoicesApplied);
            CheckOnce("choice.navmesh", navMeshErrors == 0, "NavMesh SetDestination errors here: " + navMeshErrors);
        }

        void ChoiceNotice() { labChoiceNoticeAt = Time.realtimeSinceStartup; }
    }
}
