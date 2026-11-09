using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Scene 7's arcade cabinet (MinigamesAutomate; also Scene 10). Its StartLoading is an animation event of the press, relayed, so every PC loaded the
    // arcade game and hid its own player; its start and stop events ran only where the game ended.
    // Lab tour "automate" (Scene 7): the host presses the cabinet like a player, and leaves through the game's own exit.
    //   automate.watcher: (client) this player is not put in the host's game
    //   automate.free: (client) this player is not held in the cabinet's animation while the host plays
    //   automate.acts: (both) World/Acts is off while the game runs, on after it
    public sealed partial class TogetherRuntime
    {
        MinigamesAutomate labAuto;
        GameObject labAutoActs;
        float labAutoAt = -1;
        bool labAutoLeft, labAutoOffSeen;

        void AutomateTour(float now, string scene)
        {
            if (scene != "Scene 7 - Backrooms" || now - tourSceneAt < 20) return;
            if (labAuto == null)
            {
                foreach (MinigamesAutomate m in Object.FindObjectsOfType<MinigamesAutomate>(true)) if (m != null) labAuto = m;
                GameObject world = SceneObject("World");
                Transform acts = world != null ? world.transform.Find("Acts") : null;
                labAutoActs = acts != null ? acts.gameObject : null;
                if (labAuto == null || labAutoActs == null) { CheckOnce("automate.acts", false, "cabinet " + (labAuto != null) + ", Acts " + (labAutoActs != null)); return; }
                labAutoAt = now;
                Transform press = labAuto.transform.Find("Interactive");
                for (Transform t = press; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                ObjectInteractive o = press.GetComponent<ObjectInteractive>();
                o.active = true;
                if (!IsClient && LocalPlayer != null) { LocalPlayer.TeleportPlayer(press.position); Log("Lab: host presses the cabinet"); LabPress(o); }
                return;
            }
            float at = now - labAutoAt;
            if (at > 8 && !labAutoActs.activeSelf) labAutoOffSeen = true;
            Log0(at);
            if (IsClient && at >= 18) CheckOnce("automate.watcher", GlobalTag.cameraPlayer != null && GlobalTag.cameraPlayer.activeSelf && labAuto.game == null, "camera " + (GlobalTag.cameraPlayer != null && GlobalTag.cameraPlayer.activeSelf) + ", game here " + (labAuto.game != null));
            if (IsClient && at >= 18 && LocalPlayer != null) CheckOnce("automate.free", !LocalPlayer.animationRun, "in an animation: " + LocalPlayer.animationRun);
            if (!IsClient && at >= 22 && !labAutoLeft) { labAutoLeft = true; Log("Lab: host leaves the game"); labAuto.StartStopGame(); }
            if (at >= 50) CheckOnce("automate.acts", labAutoOffSeen && labAutoActs.activeSelf, "off during the game " + labAutoOffSeen + ", on after " + labAutoActs.activeSelf);
        }

        float labAutoLogAt;
        void Log0(float at)
        {
            if (at < labAutoLogAt) return;
            labAutoLogAt = at + 3;
            Log("Lab-AUTO at=" + at.ToString("0") + " acts=" + labAutoActs.activeSelf + " game=" + (labAuto.game != null) + " camera=" + (GlobalTag.cameraPlayer != null && GlobalTag.cameraPlayer.activeSelf) + " loading=" + labAuto.loading + " exit=" + labAuto.exitGame + " ts=" + Time.timeScale);
        }
    }
}
