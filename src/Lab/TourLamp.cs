using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "lamp" (2 players, Scene 6): a motion-sensor lamp (LampBox: Scene 6's basement camera room) lights for any
    // player standing in it, not only for this PC's own.
    //   lamp.shared: (host) with only the client in the lamp's box, the host's copy of the lamp lights
    //   lamp.edge: (host) with the client just outside the box the game uses (CheckBox: half extents are size / 2), the host's copy goes dark
    public sealed partial class TogetherRuntime
    {
        LampBox labLamp;
        float labLampAt = -1;
        bool labLampEdge;

        // Where the game's CheckBox puts its box (GlobalAM.TransformPivot: position plus the axes, no scale).
        static Vector3 LampCenter(LampBox l) { Transform t = l.transform; return t.position + t.right * l.center.x + t.up * l.center.y + t.forward * l.center.z; }

        // A floor spot outside the game's box and inside one twice its size.
        void LampStandOutside(LampBox l)
        {
            Vector3 c = LampCenter(l);
            Vector3 half = new Vector3(Mathf.Abs(l.size.x), Mathf.Abs(l.size.y), Mathf.Abs(l.size.z)) * 0.5f;
            Quaternion toLocal = Quaternion.Inverse(l.transform.rotation);
            Transform t = l.transform;
            foreach (Vector3 side in new[] { t.right * half.x, -t.right * half.x, t.forward * half.z, -t.forward * half.z })
            {
                UnityEngine.AI.NavMeshHit hit;
                if (!UnityEngine.AI.NavMesh.SamplePosition(c + side * 1.5f, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                Vector3 feet = toLocal * (hit.position - c), chest = toLocal * (hit.position + Vector3.up - c);
                if (InBox(feet, half) || InBox(chest, half) || !(InBox(feet, half * 2) || InBox(chest, half * 2))) continue;
                LocalPlayer.TeleportPlayer(hit.position);
                Log("Lab: client stands just outside the lamp's box at " + hit.position);
                return;
            }
            Log("Lab: no floor just outside the lamp's box; size " + l.size);
        }

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
                if (labLampAt > 0)
                {
                    if (!labLampEdge && now - labLampAt >= 20) { labLampEdge = true; LampStandOutside(labLamp); }
                    return;
                }
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
            if (now - labLampAt < 30 + labLamp.timeOff) return;
            CheckOnce("lamp.edge", labLamp.timeOffNow <= 0, "the host's lamp " + (labLamp.timeOffNow > 0 ? "lit" : "dark") + " with the client just outside its box");
        }
    }
}
