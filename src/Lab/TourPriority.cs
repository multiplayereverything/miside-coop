using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Short tours for what the 2026-11-07 session reported.
    public sealed partial class TogetherRuntime
    {
        void PriorityTours(float now, string scene)
        {
            if (labTour == "afk") AfkTour(now, scene);
            if (labTour == "passlevel") PassLevelTour(now, scene);
            if (labTour == "distance") DistanceTour(now, scene);
            if (labTour == "pickup") PickupTour(now, scene);
            if (labTour == "legs") LegsTour(now, scene);
            if (labTour == "doorroom") DoorRoomTour(now, scene);
            if (labTour == "trigwalk") TrigWalkTour(now, scene);
            if (labTour == "carrot") CarrotTour(now, scene);
            if (labTour == "lockgather") LockGatherTour(now, scene);
            if (labTour == "loading") LoadingTour(now, scene);
            if (labTour == "blackscreen") BlackScreenTour(now, scene);
            if (labTour == "corridor") CorridorTour(now, scene);
            if (labTour == "door") DoorTour(now, scene);
            if (labTour == "allhere") AllHereTour(now, scene);
            if (labTour == "scanner") ScannerTour(now, scene);
            if (labTour == "debug") DebugTour(now, scene);
            if (labTour == "jump45") JumpTour(now, scene);
            if (labTour == "lamp") LampTour(now, scene);
            if (labTour == "scene1") Scene1CookClient(now, scene);
        }

        // A cutscene that takes over the camera needs everyone: a player elsewhere is told and brought along.
        Playable_Animation AllHereCutscene()
        {
            foreach (Playable_Animation a in Cutscenes()) if (a != null && a.name.StartsWith("CutScene 2", StringComparison.Ordinal)) return a;
            return null;
        }

        bool labAllHereMoved; float labAllHereAt = -1;
        void AllHereTour(float now, string scene)
        {
            PlayerMove player = LocalPlayer;
            if (!scene.StartsWith("Scene ", StringComparison.Ordinal) || player == null || now - tourSceneAt < 25) return;
            if (Hosting)
            {
                if (labAllHereAt > 0 || now - tourSceneAt < 40) return;
                foreach (Playable_Animation a in Cutscenes()) if (a != null && Playing(Director(a))) return;   // the chapter's opening
                Playable_Animation pick = AllHereCutscene();
                if (pick == null) { if (now - tourSceneAt > 70) CheckOnce("allhere.played", false, "no such cutscene in " + scene); return; }
                for (Transform t = pick.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                labAllHereAt = now;
                Log("Lab: host starts the cutscene " + pick.name + ", useCamera=" + pick.useCamera);
                pick.Play();
                CheckOnce("allhere.played", true, pick.name);
                return;
            }
            Peer host;
            if (!peers.TryGetValue(0, out host) || !host.HasPose) return;
            if (!labAllHereMoved)
            {
                labAllHereMoved = true;
                Playable_Animation cut = AllHereCutscene();
                if (cut != null) for (Transform t = cut.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                player.transform.position += player.transform.right * 4;
                Log("Lab: client is now " + (player.transform.position - host.Feet).magnitude.ToString("0.0") + " m from the host");
                return;
            }
            Playable_Animation mine = AllHereCutscene();
            if (labAllHereAt < 0) { if (mine != null && Playing(Director(mine))) labAllHereAt = now; return; }
            if (now - labAllHereAt < 3) return;
            float d = (player.transform.position - host.Feet).magnitude;
            CheckOnce("allhere.notice", messages.Exists(m => m.Contains("All players")), "notice shown: " + string.Join(" / ", messages));
            CheckOnce("allhere.brought", d < 2.5f, "this player is " + d.ToString("0.0") + " m from the host");
        }

        // What the lighting is here, as one number: every light, the ambient colour and the baked lightmaps. Rounded, so a
        // flicker within a step doesn't count; names, so sibling numbers don't. Compared between copies by tests/verify-lab.py.
        static string labLightNames = "";
        static string LightPrint()
        {
            System.Collections.Generic.List<string> all = new System.Collections.Generic.List<string>();
            // A light the game animates (Light_Intensity: a noise curve and lerps on each PC's own clock; Scene 5's house)
            // counts as lit or dark, not by its level in this frame. Its colour and the story's switching it off still count.
            // A flashlight is this player's own: its lights go off while this player's hands are busy (FlashLight, on the hand
            // item; PlayerMove moves its sphere light onto the player as "LightSphere"), so they aren't part of the room.
            foreach (Light l in Object.FindObjectsOfType<Light>())
                if (l != null && l.enabled && l.name != "LightSphere" && l.GetComponentInParent<FlashLight>() == null) all.Add(l.name + (l.GetComponent<Light_Intensity>() != null ? (l.intensity >= 0.25f ? "lit" : "dark") : Mathf.Round(l.intensity * 2).ToString()) + Mathf.Round(l.color.r * 4) + Mathf.Round(l.color.g * 4) + Mathf.Round(l.color.b * 4));
            all.Sort(StringComparer.Ordinal);
            string names = string.Join(",", all);
            if (names != labLightNames) { labLightNames = names; Log("MT-LIGHTS " + names); }
            Color a = RenderSettings.ambientLight, f = RenderSettings.fogColor;
            return Wire.Hash(string.Join("|", all) + "#" + Mathf.Round(a.r * 8) + Mathf.Round(a.g * 8) + Mathf.Round(a.b * 8) + "#" + RenderSettings.fog + Mathf.Round(f.r * 8) + Mathf.Round(f.g * 8) + Mathf.Round(f.b * 8)).ToString("x8") + "/" + all.Count;
        }

        // Standing still opens the game's Tetris console (GameController.timeAFK), and in a session a player is often waiting.
        float labAfkAt = -1;
        void AfkTour(float now, string scene)
        {
            if (!scene.StartsWith("Scene ", StringComparison.Ordinal) || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (cachedController == null) cachedController = Object.FindObjectOfType<GameController>();
            if (cachedController == null) return;
            if (labAfkAt < 0) { labAfkAt = now; cachedController.timeAFK = 100000; Log("Lab: this player has stood still for a long time"); return; }
            if (now - labAfkAt < 4) return;
            bool open = cachedController.tetrisPlay || (cachedController.tetrisObject != null && cachedController.tetrisObject.activeSelf);
            CheckOnce("afk.console", !open, open ? "the console opened by itself" : "the console stayed shut");
        }

        // One physics door, pushed by each player in turn. Both copies log its angle five times a second (tests/verify-lab.py).
        ObjectDoor labDoor; float labDoorLogAt, labDoorPushedAt = -1; Quaternion labDoorStart; bool labDoorAcross;
        void DoorTour(float now, string scene)
        {
            if (!scene.StartsWith("Scene ", StringComparison.Ordinal) || now - tourSceneAt < 20) return;
            if (labDoor == null)
            {
                foreach (ObjectDoor d in Object.FindObjectsOfType<ObjectDoor>())
                    if (d != null && d.rb != null && !d.lockDoor && (labDoor == null || string.CompareOrdinal(d.name, labDoor.name) < 0)) labDoor = d;
                if (labDoor == null) { if (now - tourSceneAt > 60) CheckOnce("door.pushed", false, "no unlocked door here"); return; }
                labDoorStart = labDoor.transform.localRotation;
                Log("Lab: door tour uses " + labDoor.name);
            }
            if (!Hosting && !labDoorAcross && LocalPlayer != null)
            {
                Peer host;
                if (peers.TryGetValue(0, out host) && host.HasPose)
                {
                    labDoorAcross = true;
                    Vector3 away = labDoor.transform.position - host.Feet; away.y = 0;
                    Vector3 to = labDoor.transform.position + away.normalized * 2f; to.y = host.Feet.y;
                    LocalPlayer.transform.position = to;
                    Log("Lab: client stands across the door from the host, at " + to);
                }
            }
            if (now >= labDoorLogAt) { labDoorLogAt = now + 0.2f; Log("MT-DOOR " + labDoor.name + " " + Quaternion.Angle(labDoorStart, labDoor.transform.localRotation).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)); }
            float pushAt = Hosting ? 22 : 34;
            if (labDoorPushedAt < 0 && now - tourSceneAt > pushAt)
            {
                labDoorPushedAt = now;
                Log("Lab: this player pushes the door");
            }
            // A walking player leans on a door for a while; one shove and it swings straight back.
            if (labDoorPushedAt > 0 && now - labDoorPushedAt < 2)
            {
                labDoor.rb.isKinematic = false;
                labDoor.rb.WakeUp();
                labDoor.rb.AddTorque(labDoor.transform.TransformDirection(labDoor.hj.axis) * (Hosting ? 0.4f : -0.4f), ForceMode.VelocityChange);
            }
            if (labDoorPushedAt > 0 && now - labDoorPushedAt > 14) CheckOnce("door.pushed", true, "pushed at " + labDoorPushedAt.ToString("0.0"));
        }
    }
}
