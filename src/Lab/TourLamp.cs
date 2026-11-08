using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "lamp" (2 players, Scene 6): a motion-sensor lamp (LampBox: Scene 6's basement camera room) lights for any
    // player standing in it, not only for this PC's own.
    //   lamp.shared: (host) with only the client in the lamp's box, the host's copy of the lamp lights
    public sealed partial class TogetherRuntime
    {
        LampBox labLamp;
        float labLampAt = -1;

        static Vector3 LampCenter(LampBox l) { return l.transform.TransformPoint(l.center); }

        void LampTour(float now, string scene)
        {
            if (scene != "Scene 6 - BasementFirst" || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (labLamp == null)
            {
                foreach (LampBox l in Object.FindObjectsOfType<LampBox>()) if (l != null && l.name == "RoomCamera") labLamp = l;
                if (labLamp == null) { if (Hosting && now - tourSceneAt > 40) CheckOnce("lamp.shared", false, "no RoomCamera lamp in " + scene); return; }
            }
            if (IsClient)
            {
                if (labLampAt > 0) return;
                labLampAt = now;
                UnityEngine.AI.NavMeshHit hit;
                Vector3 to = LampCenter(labLamp);
                if (UnityEngine.AI.NavMesh.SamplePosition(to, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) to = hit.position;
                LocalPlayer.TeleportPlayer(to);
                Log("Lab: client stands in the lamp's box at " + to);
                return;
            }
            // The host keeps away from the lamp (more than its box) and watches its copy.
            if (labLampAt < 0)
            {
                labLampAt = now;
                Vector3 c = LampCenter(labLamp);
                if ((LocalPlayer.transform.position - c).magnitude < 8) LocalPlayer.TeleportPlayer(LocalPlayer.transform.position + (LocalPlayer.transform.position - c).normalized * 8);
                Log("Lab: host is " + (LocalPlayer.transform.position - c).magnitude.ToString("0.0") + " m from the lamp; lit " + (labLamp.timeOffNow > 0));
                return;
            }
            if (now - labLampAt < 12) return;
            CheckOnce("lamp.shared", labLamp.timeOffNow > 0, "the host's lamp " + (labLamp.timeOffNow > 0 ? "lit" : "dark") + " with the client in it, the host " + (LocalPlayer.transform.position - LampCenter(labLamp)).magnitude.ToString("0.0") + " m away");
        }
    }
}
