using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // User reports (2026-11-09, 0.3.17), Scene 2's batteries:
    // Lab tour "doorroom": rooms the story switched off come back when the door to them opens, on every PC (the game leaves
    // this to ObjectDoor.DoorOpened).
    //   doorroom.loaded: (each PC) after one player opens the door, its rooms are on
    // Lab tour "corridor": taking the first battery switches off the rest of the house (Quest 4 Corridor).
    //   corridor.gathered: the player away from the corridor ends up next to the one at it. -mt-corridorclient: the client is at it.
    public sealed partial class TogetherRuntime
    {
        public static bool LabCorridorClient;
        ObjectDoor labRoomDoor;
        float labRoomAt = -1;
        int labRoomStep;
        float labRoomLogAt;
        bool labRoomShot;

        // Scene 2 end to end (the "scene2" tour): the story has reached the fridge battery (Quest 5), a player pushes the real door from
        // the kitchen, and the room behind it must load on every PC. batteries.room; pictures batteries-before/after.
        ObjectDoor labBatteryDoor;
        float labBatteryAt = -1;
        bool labBatteryShot;
        void LabBatteryDoorTick(float now, string scene)
        {
            if (scene != "Scene 2 - InGame" || LocalPlayer == null) return;
            GameObject quests = SceneObject("Quests");
            Transform five = quests != null ? quests.transform.Find("Quest 5") : null;
            if (five == null || !five.gameObject.activeSelf) return;
            if (labBatteryDoor == null) foreach (ObjectDoor d in Object.FindObjectsOfType<ObjectDoor>()) if (d != null && d.name == "DoorPhysic Kitchen-Main") labBatteryDoor = d;
            if (labBatteryDoor == null) return;
            if (labBatteryAt < 0)
            {
                labBatteryAt = now;
                Vector3 c = labBatteryDoor.transform.position;
                UnityEngine.AI.NavMeshHit hit;
                if (UnityEngine.AI.NavMesh.SamplePosition(c, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) LocalPlayer.TeleportPlayer(hit.position);
                Log("Lab: Quest 5 is on; at the Kitchen-Main door; Main on=" + LabRoomOn(labBatteryDoor));
                return;
            }
            float t = now - labBatteryAt;
            if (t >= 2 && !labBatteryShot) { labBatteryShot = true; LabShot("batteries-before-" + Name); }
            if (Hosting && t >= 3 && t < 6 && labBatteryDoor.rb != null) { labBatteryDoor.enabled = true; labBatteryDoor.lockDoor = false; labBatteryDoor.rb.isKinematic = false; labBatteryDoor.rb.WakeUp(); labBatteryDoor.rb.AddTorque(labBatteryDoor.transform.TransformDirection(labBatteryDoor.hj.axis) * 0.4f, ForceMode.VelocityChange); }
            if (t < 12) return;
            LabShot("batteries-after-" + Name);
            CheckOnce("batteries.room", LabRoomOn(labBatteryDoor), "rooms behind the door on=" + LabRoomOn(labBatteryDoor) + ", door open=" + labBatteryDoor.open);
        }

        static bool LabRoomOn(ObjectDoor d)
        {
            foreach (GameObject o in d.objectsRight) if (o != null && !o.activeSelf) return false;
            foreach (GameObject o in d.objectsLeft) if (o != null && !o.activeSelf) return false;
            return true;
        }

        void DoorRoomTour(float now, string scene)
        {
            if (scene != "Scene 2 - InGame" || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (labRoomDoor == null)
            {
                foreach (ObjectDoor d in Object.FindObjectsOfType<ObjectDoor>(true)) if (d != null && d.name == "DoorPhysic Kitchen-Main") labRoomDoor = d;
                if (labRoomDoor == null) { CheckOnce("doorroom.loaded", false, "no door in " + scene); return; }
                labRoomAt = now;
                return;
            }
            GameObject room = labRoomDoor.objectsRight != null && labRoomDoor.objectsRight.Length > 0 ? labRoomDoor.objectsRight[0] : labRoomDoor.objectsLeft != null && labRoomDoor.objectsLeft.Length > 0 ? labRoomDoor.objectsLeft[0] : null;
            if (room == null) { CheckOnce("doorroom.loaded", false, "the door has no rooms"); return; }
            float t = now - labRoomAt;
            if (labRoomStep == 0 && t >= 2)
            {
                labRoomStep = 1;
                foreach (GameObject o in labRoomDoor.objectsRight) if (o != null && !LocalPlayer.transform.IsChildOf(o.transform)) o.SetActive(false);
                foreach (GameObject o in labRoomDoor.objectsLeft) if (o != null && !LocalPlayer.transform.IsChildOf(o.transform)) o.SetActive(false);
                Log("Lab: the door's rooms are off here; " + room.name + " on=" + room.activeSelf);
            }
            if (labRoomStep == 1 && t >= 6)
            {
                labRoomStep = 2;
                if (Hosting) Log("Lab: host pushes the door");
            }
            // A walking player leans on a door: the same shove as the "door" tour. The game notices it in its own FixedUpdate.
            if (Hosting && labRoomStep == 2 && t < 9 && labRoomDoor.rb != null) { labRoomDoor.enabled = true; labRoomDoor.lockDoor = false; labRoomDoor.rb.isKinematic = false; labRoomDoor.rb.WakeUp(); labRoomDoor.rb.AddTorque(labRoomDoor.transform.TransformDirection(labRoomDoor.hj.axis) * 0.4f, ForceMode.VelocityChange); }
            if (labRoomStep == 2 && now >= labRoomLogAt) { labRoomLogAt = now + 0.5f; Log("MT-DOOR " + labRoomDoor.name + " angle " + (labRoomDoor.hj != null ? labRoomDoor.hj.angle : -999) + " open=" + labRoomDoor.open + " enabled=" + labRoomDoor.enabled + " kinematic=" + (labRoomDoor.rb != null && labRoomDoor.rb.isKinematic)); }
            if (labRoomStep == 1 && t >= 5 && !labRoomShot) { labRoomShot = true; LabShot("doorroom-before-" + Name); }
            if (t < 16) return;
            LabShot("doorroom-after-" + Name);
            CheckOnce("doorroom.loaded", room.activeSelf, room.name + " on=" + room.activeSelf + ", door open=" + labRoomDoor.open);
        }

        Transform labCorridor;
        float labCorridorAt = -1;
        int labCorridorStep;

        void CorridorTour(float now, string scene)
        {
            if (scene != "Scene 2 - InGame" || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (labCorridor == null)
            {
                GameObject quests = SceneObject("Quests");
                Transform q = quests != null ? quests.transform.Find("Quest 4 Corridor") : null;
                labCorridor = q != null ? q.Find("Corridor") : null;
                if (labCorridor == null) { CheckOnce("corridor.gathered", false, "no corridor in " + scene); return; }
                labCorridorAt = now;
                return;
            }
            float t = now - labCorridorAt;
            bool near = Hosting != LabCorridorClient;
            if (labCorridorStep == 0 && t >= 2)
            {
                labCorridorStep = 1;
                UnityEngine.AI.NavMeshHit hit;
                // The battery is taken at the toilet's scarf; the other player is across the house, at the Kitchen-Main door.
                Transform scarf = null, door = null;
                foreach (Transform o in Object.FindObjectsOfType<Transform>(true)) { if (o.name == "Scarf" && scarf == null) scarf = o; if (o.name == "DoorPhysic Kitchen-Main") door = o; }
                Vector3 c = near ? scarf.position : door.position;
                Vector3 to = UnityEngine.AI.NavMesh.SamplePosition(c, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas) ? hit.position : c;
                LocalPlayer.TeleportPlayer(to);
                Log("Lab: this player stands " + (near ? "at" : "away from") + " the corridor, at " + to);
            }
            if (labCorridorStep == 1 && t >= 8)
            {
                labCorridorStep = 2;
                labCorridor.parent.gameObject.SetActive(true);
                Log("Lab: the corridor quest starts here");
            }
            if (t < 18) return;
            foreach (Peer p in peers.Values)
            {
                if (!p.HasPose) continue;
                float d = (LocalPlayer.transform.position - p.Feet).magnitude;
                CheckOnce("corridor.gathered", d < 5, d.ToString("0.0") + " m from " + p.Name + "; this player was " + (near ? "at" : "away from") + " the corridor");
            }
        }
    }
}
