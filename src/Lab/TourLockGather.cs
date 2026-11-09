using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // User reports (2026-11-09, 0.3.18), both on the story log.
    // Lab tour "lockgather" (Scene 4): walking into the kitchen locks its door behind the player (Trigger Entry Kitchen). A player on the
    // other side of the door must not be left there. -mt-lockclient: the client walks in.
    //   lockgather.together: (each PC) both players end up on the same side of the door
    // Lab tour "blackscreen" (Scene 3): Mita covers the player's eyes (BlackScreenAnimation); when it ends the game teleports that
    // player to the black room and switches the house off. Everyone has to go.
    //   blackscreen.moved: (client) the client was moved by the host's black screen
    public sealed partial class TogetherRuntime
    {
        public static bool LabLockClient;
        Trigger_Event labLockTrigger;
        ObjectDoor labLockDoor;
        float labLockAt = -1;
        int labLockStep;
        Vector3 labLockTo, labLockOutside;

        bool LabLockOtherAway() { foreach (Peer p in peers.Values) if (p.HasPose && labLockTrigger != null && (p.Feet - labLockOutside).sqrMagnitude < 4f) return true; return false; }

        void LockGatherTour(float now, string scene)
        {
            if (scene != "Scene 4 - StartSecret" || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (labLockAt < 0)
            {
                foreach (Trigger_Event t in Object.FindObjectsOfType<Trigger_Event>(true)) if (t != null && t.name == "Trigger Entry Kitchen") labLockTrigger = t;
                foreach (ObjectDoor d in Object.FindObjectsOfType<ObjectDoor>(true)) if (d != null && d.name == "DoorPhysic Kitchen-Main") labLockDoor = d;
                if (labLockTrigger == null || labLockDoor == null) { CheckOnce("lockgather.together", false, "no kitchen trigger or door in " + scene); return; }
                for (Transform t = labLockTrigger.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                Vector3 inp = labLockTrigger.transform.position, drp = labLockDoor.transform.position; labLockOutside = drp + (drp - inp).normalized * 4; UnityEngine.AI.NavMeshHit nearOut; if (UnityEngine.AI.NavMesh.SamplePosition(labLockOutside, out nearOut, 3f, UnityEngine.AI.NavMesh.AllAreas)) labLockOutside = nearOut.position;
                labLockAt = now;
                return;
            }
            float t2 = now - labLockAt;
            bool walker = Hosting != LabLockClient;
            if (labLockStep == 0 && t2 >= 2 && (!walker || LabLockOtherAway()))
            {
                labLockStep = 1;
                // The walker goes to the kitchen trigger, the other player stays well outside, on the far side of the door.
                Vector3 inside = labLockTrigger.transform.position, door = labLockDoor.transform.position;
                labLockOutside = door + (door - inside).normalized * 4; UnityEngine.AI.NavMeshHit near; if (UnityEngine.AI.NavMesh.SamplePosition(labLockOutside, out near, 3f, UnityEngine.AI.NavMesh.AllAreas)) labLockOutside = near.position;
                Vector3 to = walker ? inside : labLockOutside;
                UnityEngine.AI.NavMeshHit hit;
                if (UnityEngine.AI.NavMesh.SamplePosition(to, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) to = hit.position;
                labLockTo = to;
                Log("Lab: this player " + (walker ? "walks in" : "stays outside") + " at " + to);
            }
            // Held there: a teleport during the chapter's opening animation is undone.
            if (walker && labLockStep == 1 && t2 < 7f && (LocalPlayer.transform.position - labLockTo).sqrMagnitude > 1f) LocalPlayer.transform.position = labLockTo;
            if (labLockStep == 1 && t2 >= 7 && (!walker || LabLockOtherAway()))
            {
                labLockStep = 2;
                if (walker && labLockTrigger != null) { Log("Lab: into the kitchen"); labLockTrigger.OnTriggerEnter(LocalPlayer.GetComponent<Collider>()); }
            }
            if (t2 < 26) return;
            foreach (Peer p in peers.Values)
            {
                if (!p.HasPose) continue;
                float d = (LocalPlayer.transform.position - p.Feet).magnitude;
                CheckOnce("lockgather.together", d < 3.5f, d.ToString("0.0") + " m from " + p.Name + "; door locked here " + labLockDoor.lockDoor);
            }
        }

        BlackScreenAnimation labBlack;
        float labBlackAt = -1;
        Vector3 labBlackHome;
        bool labBlackPlayed;

        void BlackScreenTour(float now, string scene)
        {
            if (scene != "Scene 3 - WeTogether" || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (labBlackAt < 0)
            {
                foreach (BlackScreenAnimation b in Object.FindObjectsOfType<BlackScreenAnimation>(true)) if (b != null && SceneKeys.Key(b).Contains("Quest 6 PlayerFall")) labBlack = b;
                if (labBlack == null) { CheckOnce("blackscreen.moved", false, "no black screen in " + scene); return; }
                for (Transform t = labBlack.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                labBlackAt = now;
                labBlackHome = LocalPlayer.transform.position;
                return;
            }
            float t2 = now - labBlackAt;
            if (Hosting && !labBlackPlayed && t2 >= 3 && labBlack != null) { labBlackPlayed = true; labBlack.Play(); Log("Lab: the host's black screen plays"); }
            if (IsClient && t2 >= 20) { float d = (LocalPlayer.transform.position - labBlackHome).magnitude; CheckOnce("blackscreen.moved", d > 2, "client moved " + d.ToString("0.0") + " m"); }
        }
    }
}
