using System;
using UnityEngine;

namespace MiSideTogether
{
    // Watching through the actor's eyes. Every pose carries the sender's camera pose relative to their body and a
    // "watch me" flag, set while they perform an interaction or minigame whose rule says pov (CutsceneRules). The other
    // players' cameras then show exactly what the actor sees until it ends; their own view comes back unchanged.
    public sealed partial class TogetherRuntime
    {
        string lastClickKey; float lastClickAt = -100;
        int spectating = -1;
        Transform spectateCamera;
        Vector3 savedCameraPos; Quaternion savedCameraRot; float savedFov;
        public int spectateFrames;

        public static void NoteLocalClick(string key) { if (Instance != null) { Instance.lastClickKey = key; Instance.lastClickAt = Time.realtimeSinceStartup; NoteCoreOperator(key, true); } }

        // Sender: should the others watch through this player's eyes right now?
        bool WatchMe()
        {
            if (actorAnimation != null)
            {
                string key = Time.realtimeSinceStartup - lastClickAt < 3 || actorAnimationAt - lastClickAt < 3 ? lastClickKey : actorAnimation;
                return CutsceneRules.For(CutsceneRules.Kind.Interaction, key ?? actorAnimation).Pov;
            }
            foreach (MinigameRun r in minigames.Values)
                if (r.Owner == SelfId && CutsceneRules.For(CutsceneRules.Kind.Minigame, r.Key).Pov) return true;
            return false;
        }

        void CaptureCamera(Peer pose, Transform body)
        {
            Camera cam = Camera.main;
            if (cam == null || body == null) { pose.CamPos = Vector3.up * 1.6f; pose.CamRot = Quaternion.identity; pose.Fov = 60; return; }
            pose.CamPos = body.InverseTransformPoint(cam.transform.position);
            pose.CamRot = Quaternion.Inverse(body.rotation) * cam.transform.rotation;
            pose.Fov = cam.fieldOfView;
        }

        // Watcher: pick the player to watch, and put this camera where their eyes are (after the game moved it).
        void SpectateTick()
        {
            int want = -1;
            if (Connected && LocalPlayer != null)
                foreach (Peer p in peers.Values)
                    if (p.WatchMe && p.HasPose && p.SceneHash == LocalSceneHash && Time.realtimeSinceStartup - p.PoseAt < 1f && p.Avatar != null && p.Avatar.Root != null) { want = p.Id; break; }
            if (want != spectating) { if (spectating >= 0) EndSpectate(); if (want >= 0) BeginSpectate(want); }
            if (spectating < 0) return;
            Peer actor;
            if (!peers.TryGetValue(spectating, out actor) || actor.Avatar == null || actor.Avatar.Root == null || spectateCamera == null) { EndSpectate(); return; }
            Transform body = actor.Avatar.Root.transform;
            spectateCamera.SetPositionAndRotation(body.TransformPoint(actor.CamPos), body.rotation * actor.CamRot);
            Camera cam = spectateCamera.GetComponent<Camera>();
            if (cam != null && actor.Fov > 10 && actor.Fov < 170) cam.fieldOfView = actor.Fov;
            // Inside the actor's head: their body would block the view.
            if (actor.Avatar.Root.activeSelf) actor.Avatar.Root.SetActive(false);
            spectateFrames++;
        }

        void BeginSpectate(int id)
        {
            Camera cam = Camera.main;
            PlayerMove player = LocalPlayer;
            if (cam == null || player == null) return;
            spectating = id;
            spectateCamera = cam.transform;
            savedCameraPos = spectateCamera.localPosition; savedCameraRot = spectateCamera.localRotation; savedFov = cam.fieldOfView;
            // While watching, this player's own controls rest.
            spectateHeld = !player.dontMove && !player.stopMouseMove;
            if (spectateHeld) { player.dontMove = true; player.stopMouseMove = true; }
            Peer p; Tell("Watching " + (peers.TryGetValue(id, out p) ? p.Name : "another player") + ".");
        }

        void EndSpectate()
        {
            if (spectateCamera != null)
            {
                spectateCamera.localPosition = savedCameraPos; spectateCamera.localRotation = savedCameraRot;
                Camera cam = spectateCamera.GetComponent<Camera>();
                if (cam != null) cam.fieldOfView = savedFov;
            }
            PlayerMove player = LocalPlayer;
            if (spectateHeld && player != null) { player.dontMove = false; player.stopMouseMove = false; }
            spectateHeld = false;
            spectating = -1; spectateCamera = null;
        }

        bool spectateHeld;

        public bool Spectating { get { return spectating >= 0; } }
    }
}
