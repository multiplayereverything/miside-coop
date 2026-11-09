using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Scene 2: taking the first battery (at the toilet's scarf) starts Quest 4 Corridor, which switches the rest of the house off.
    // Alone that is fine, the player is at the toilet. In a session the others can be anywhere, and are left with no room around
    // them. So when the quest starts, whoever isn't the player nearest the scarf goes to that player. Each PC works it out from the
    // same positions.
    public sealed partial class TogetherRuntime
    {
        bool corridorStarted;
        float corridorGatherUntil = -1;

        // The story locks a door behind the player (Scene 4's kitchen: walking in shuts it). Alone the player is always on the right
        // side; in a session anyone else on the far side is locked out for the rest of the scene. They go to the player who set it off.
        public int lastStoryPlayer = -1;
        ObjectDoor lockGatherDoor;
        int lockGatherTo;
        float lockGatherUntil = -1;

        // Before the trigger's own events run: the door locks inside them.
        public static void NoteStoryActor() { TogetherRuntime rt = Instance; if (rt != null && rt.Connected && !Replaying) { rt.lastStoryPlayer = rt.SelfId; if (rt.InLogChapter) rt.momentMine = true; } }

        public static void DoorLockedHere(ObjectDoor door, bool locked)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !locked || !rt.Connected || !rt.InLogChapter || door == null) return;
            rt.lockGatherDoor = door;
            rt.lockGatherTo = rt.lastStoryPlayer >= 0 ? rt.lastStoryPlayer : 0;
            rt.lockGatherUntil = Time.realtimeSinceStartup + 6;
        }

        static bool OtherSides(ObjectDoor door, Vector3 a, Vector3 b)
        {
            // The door's thin direction is the wall's normal.
            BoxCollider box = door.GetComponent<BoxCollider>();
            Vector3 normal = door.transform.forward;
            if (box != null)
            {
                Vector3 s = box.size;
                normal = s.x <= s.y && s.x <= s.z ? door.transform.right : s.y <= s.z ? door.transform.up : door.transform.forward;
            }
            return Vector3.Dot(a - door.transform.position, normal) * Vector3.Dot(b - door.transform.position, normal) < 0;
        }

        void LockGatherTick(float now)
        {
            if (lockGatherUntil < 0) return;
            if (now > lockGatherUntil || lockGatherDoor == null) { lockGatherUntil = -1; return; }
            PlayerMove player = LocalPlayer;
            Peer to;
            if (lockGatherTo == SelfId || !peers.TryGetValue(lockGatherTo, out to) || !to.HasPose) { lockGatherUntil = -1; return; }
            if (player == null || !LocalFree(player)) return;
            lockGatherUntil = -1;
            if (!OtherSides(lockGatherDoor, player.transform.position, to.Feet)) return;
            player.TeleportPlayer(to.Feet);
            Log("Brought to " + to.Name + ": " + lockGatherDoor.name + " locked between us");
            Tell("Brought you to " + to.Name + ": the door was locked.");
        }

        void CorridorGatherTick(float now)
        {
            LockGatherTick(now);
            if (!Connected || ActiveSceneName != "Scene 2 - InGame") { corridorStarted = false; corridorGatherUntil = -1; return; }
            GameObject quests = SceneObject("Quests");
            Transform quest = quests != null ? quests.transform.Find("Quest 4 Corridor") : null;
            if (quest != null && quest.gameObject.activeSelf && !corridorStarted) { corridorStarted = true; corridorGatherUntil = now + 8; }
            PlayerMove player = LocalPlayer;
            if (corridorGatherUntil < 0 || player == null) return;
            if (now > corridorGatherUntil) { corridorGatherUntil = -1; return; }
            if (!LocalFree(player)) return;
            Transform scarf = null;
            foreach (Transform t in Object.FindObjectsOfType<Transform>(true)) if (t.name == "Scarf") { scarf = t; break; }
            if (scarf == null) { corridorGatherUntil = -1; return; }
            Peer nearest = null;
            float best = (player.transform.position - scarf.position).sqrMagnitude;
            foreach (Peer p in peers.Values)
            {
                if (!p.HasPose || p.SceneHash != LocalSceneHash) continue;
                float d = (p.Feet - scarf.position).sqrMagnitude;
                if (d < best) { best = d; nearest = p; }
            }
            corridorGatherUntil = -1;
            if (nearest == null || (player.transform.position - nearest.Feet).sqrMagnitude < 9f) return;
            player.TeleportPlayer(nearest.Feet);
            Log("Brought to " + nearest.Name + " as the corridor starts");
            Tell("Brought you to " + nearest.Name + ": the house is changing.");
        }
    }
}
