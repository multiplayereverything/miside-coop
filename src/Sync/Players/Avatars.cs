using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // A remote player: a copy of the main character's own body (mesh, rig and animator) with every game script removed,
    // driven by the peer's pose and animator frames. Falls back to a capsule where the scene has no player body.
    public sealed class RemoteAvatar
    {
        public GameObject Root;
        public Transform Head;
        public readonly AnimTarget Anim = new AnimTarget();
        public bool IsModel;
        public int SceneHash;
        public BodyLayout Layout;
        HashSet<string> visible;
        Material fallbackMaterial;

        public static RemoteAvatar Create(PlayerMove player, Color colour)
        {
            RemoteAvatar avatar = new RemoteAvatar { SceneHash = TogetherRuntime.LocalSceneHash };
            if (player != null && player.animPerson != null)
            {
                try { avatar.BuildModel(player.animPerson, BodyMeshPaths(player)); }
                catch (Exception ex) { TogetherRuntime.Log("Avatar model copy failed, using a capsule: " + ex.Message); avatar.Destroy(); avatar.Root = null; }
            }
            if (avatar.Root == null) avatar.BuildCapsule(colour);
            return avatar;
        }

        // Paths (relative to the animator object) of the renderers that make up the character itself, as the game lists
        // them on PlayerPerson. Everything else under the rig is a held item or outfit part (the Game Boy, the phone,
        // pyjamas...) that the game shows only while in use; the copy shows those only when the sender shows them.
        public static HashSet<string> BodyMeshPaths(PlayerMove player)
        {
            HashSet<string> paths = new HashSet<string>();
            Transform root = player.animPerson.transform;
            PlayerPerson person = player.scrpp != null ? player.scrpp : player.animPerson.GetComponentInParent<PlayerPerson>();
            if (person == null) return paths;
            if (person.meshes != null)
                foreach (SkinnedMeshRenderer mesh in person.meshes)
                    if (mesh != null) { string path = RelativePath(root, mesh.transform); if (path != null) paths.Add(path); }
            if (person.meshHead != null) { string path = RelativePath(root, person.meshHead.transform); if (path != null) paths.Add(path); }
            return paths;
        }

        static string RelativePath(Transform root, Transform t)
        {
            string path = "";
            for (; t != null && t != root; t = t.parent) path = path.Length == 0 ? t.name : t.name + "/" + path;
            return t == root ? path : null;
        }

        void BuildModel(Animator source, HashSet<string> bodyPaths)
        {
            // Instantiating under an inactive parent means no Awake/OnEnable runs on the copied game scripts.
            GameObject holder = new GameObject("MiSideTogether holder");
            holder.SetActive(false);
            try
            {
                GameObject copy = Object.Instantiate(source.gameObject, holder.transform).Cast<GameObject>();
                copy.name = "MiSideTogether remote player";
                Strip(copy);
                int layer = VisibleLayer();
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = layer;
                    // First-person rigs shrink bones (the head) to zero; a third-person copy needs them back.
                    if (t.localScale.sqrMagnitude < 1e-6f) t.localScale = Vector3.one;
                }
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
                {
                    string path = RelativePath(copy.transform, r.transform);
                    SkinnedMeshRenderer body = r.TryCast<SkinnedMeshRenderer>();
                    // Without the game's list, skinned meshes count as the body. Props start hidden until the sender
                    // says they show (Op.Visible), so the Game Boy no longer appears in a hand that is not playing.
                    bool isBody = bodyPaths.Count > 0 ? path != null && bodyPaths.Contains(path) : body != null;
                    r.enabled = isBody;
                    r.forceRenderingOff = false;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    // The first-person body receives no shadows; Mita, with the same toon materials, does. Without them
                    // the copy stays fully lit in shade and looks flat and over-bright next to her (2026-10-26).
                    r.receiveShadows = true;
                    r.gameObject.SetActive(true);
                    if (body != null) body.updateWhenOffscreen = true;
                }
                if (bodyPaths.Count == 0) TogetherRuntime.Log("Avatar: no PlayerPerson mesh list here; kept the skinned meshes only.");
                Animator anim = copy.GetComponent<Animator>();
                if (anim == null) throw new InvalidOperationException("The copied body has no Animator.");
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                RuntimeAnimatorController controller = source.runtimeAnimatorController;
                if (controller != null)
                {
                    AnimatorOverrideController existing = controller.TryCast<AnimatorOverrideController>();
                    RuntimeAnimatorController baseController = existing != null && existing.runtimeAnimatorController != null ? existing.runtimeAnimatorController : controller;
                    // A private override controller: the local player's own clip swaps must not leak onto the copy.
                    Anim.Override = new AnimatorOverrideController(baseController);
                    anim.runtimeAnimatorController = Anim.Override;
                }
                Anim.Animator = anim;
                Layout = BodyLayout.Of(copy.transform, bodyPaths.Count > 0 ? bodyPaths : SkinnedPaths(copy.transform));
                Head = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
                // Generic rigs: the bone named Head, else an anchor at head height (used for name tags and Mita's gaze).
                if (Head == null) foreach (Transform t in Layout.Bones) if (t != null && t.name.Equals("Head", StringComparison.OrdinalIgnoreCase)) { Head = t; break; }
                if (Head == null)
                {
                    GameObject anchor = new GameObject("MiSideTogether head");
                    anchor.transform.SetParent(copy.transform, false);
                    anchor.transform.localPosition = new Vector3(0, 1.55f, 0);
                    Head = anchor.transform;
                }
                copy.transform.SetParent(null, false);
                Object.DontDestroyOnLoad(copy);
                copy.SetActive(true);
                Root = copy;
                IsModel = true;
            }
            finally { Object.Destroy(holder); }
        }

        static HashSet<string> SkinnedPaths(Transform root)
        {
            HashSet<string> paths = new HashSet<string>();
            foreach (SkinnedMeshRenderer s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { string p = RelativePath(root, s.transform); if (p != null) paths.Add(p); }
            return paths;
        }

        // Body meshes always show; every other renderer (props, outfit parts) mirrors the sender's own body.
        public void ApplyVisibility(HashSet<string> shown)
        {
            visible = shown;
            if (Layout == null || shown == null) return;
            foreach (KeyValuePair<string, Renderer> pair in Layout.Renderers)
                if (pair.Value != null) pair.Value.enabled = Layout.BodyPaths.Contains(pair.Key) || shown.Contains(pair.Key);
        }

        public float BonesAt = -100;

        // The sender's exact skeleton pose, smoothed. While bone frames arrive the copy's own animator is switched off.
        public bool ApplyBones(BoneFrame frame, float k)
        {
            if (Layout == null || frame.Hash != Layout.Hash || frame.Count != Layout.Bones.Length) return false;
            for (int i = 0; i < frame.Count; i++)
            {
                Transform bone = Layout.Bones[i];
                if (bone == null) continue;
                bone.localRotation = Quaternion.Slerp(bone.localRotation, frame.Rotations[i], k);
                bone.localPosition = Vector3.Lerp(bone.localPosition, frame.Positions[i], k);
            }
            return true;
        }

        void BuildCapsule(Color colour)
        {
            Root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Root.name = "MiSideTogether remote player (capsule)";
            Collider collider = Root.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            Renderer renderer = Root.GetComponent<Renderer>();
            if (renderer != null) { fallbackMaterial = renderer.material; fallbackMaterial.color = colour; }
            Root.layer = VisibleLayer();
            Object.DontDestroyOnLoad(Root);
            IsModel = false;
        }

        static void Strip(GameObject copy)
        {
            // Several passes: a component another one requires can only go once its dependant is gone.
            for (int pass = 0; pass < 4; pass++)
            {
                bool left = false;
                foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || Keep(c)) continue;
                    try { Object.DestroyImmediate(c); }
                    catch { left = true; }
                }
                if (!left) break;
            }
        }

        static bool Keep(Component c)
        {
            return c.TryCast<Transform>() != null || c.TryCast<Animator>() != null || c.TryCast<SkinnedMeshRenderer>() != null
                || c.TryCast<MeshRenderer>() != null || c.TryCast<MeshFilter>() != null;
        }

        // A layer the player's camera renders, taken from Mita when possible (she is always visible).
        static int VisibleLayer()
        {
            Camera cam = Camera.main;
            int mask = cam != null ? cam.cullingMask : ~0;
            MitaPerson mita = Object.FindObjectOfType<MitaPerson>();
            if (mita != null)
            {
                SkinnedMeshRenderer r = mita.GetComponentInChildren<SkinnedMeshRenderer>();
                if (r != null && (mask & (1 << r.gameObject.layer)) != 0) return r.gameObject.layer;
            }
            if ((mask & 1) != 0) return 0;
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
            return 0;
        }

        public Vector3 TagPosition
        {
            get
            {
                if (Head != null) return Head.position + Vector3.up * 0.35f;
                return Root.transform.position + Vector3.up * (IsModel ? 1.9f : 1.3f);
            }
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
            if (fallbackMaterial != null) Object.Destroy(fallbackMaterial);
            if (Anim.Override != null) Object.Destroy(Anim.Override);
            Root = null; fallbackMaterial = null; Anim.Override = null; Anim.Animator = null;
        }
    }

    public sealed partial class TogetherRuntime
    {
        int avatarSceneHash;
        bool localCutscene;

        void DestroyAvatar(Peer peer)
        {
            if (peer.Avatar != null) peer.Avatar.Destroy();
            peer.Avatar = null;
        }

        bool PeerVisible(Peer peer)
        {
            // Hidden while either side is in a cutscene: each player sees it as the protagonist, without the others in shot.
            return peer.Accepted && peer.HasPose && peer.SceneHash != 0 && peer.SceneHash == LocalSceneHash && Time.realtimeSinceStartup - peer.PoseAt < 3f
                && !peer.InCutscene && !localCutscene && OverLoadedFloor(peer) && !SameSpotAnimation(peer);
        }

        // A mirrored interaction puts every player on the same spot playing the same animation: their bodies would
        // overlap, so while this player is animating, a player within half a metre is not drawn.
        bool SameSpotAnimation(Peer peer)
        {
            PlayerMove player = LocalPlayer;
            if (player == null || !player.animationRun) return false;
            Vector3 d = peer.Feet - player.transform.position; d.y = 0;
            return d.sqrMagnitude < 0.25f;
        }

        // A player standing where this machine has no floor (a part of the house streamed out here) would float in
        // empty space; hide them until this machine has that area loaded again. Checked twice a second.
        bool OverLoadedFloor(Peer peer)
        {
            float now = Time.realtimeSinceStartup;
            if (now >= peer.FloorCheckAt)
            {
                peer.FloorCheckAt = now + 0.5f;
                Vector3 spot;
                bool floor = !peer.Free || GroundAt(peer.Position + Vector3.up * 0.5f, out spot);
                if (floor) peer.NoFloorSince = -1; else if (peer.NoFloorSince < 0) peer.NoFloorSince = now;
            }
            return peer.NoFloorSince < 0 || now - peer.NoFloorSince < 1f;
        }

        void UpdateAvatars()
        {
            int scene = LocalSceneHash;
            // Each chapter dresses the player differently, so copies are rebuilt from the new scene's own body.
            if (scene != avatarSceneHash) { avatarSceneHash = scene; foreach (Peer p in peers.Values) DestroyAvatar(p); }
            PlayerMove player = LocalPlayer;
            localCutscene = LocalInCutscene();
            float dt = Time.unscaledDeltaTime;
            foreach (Peer peer in peers.Values)
            {
                bool visible = Connected && PeerVisible(peer);
                if (!visible) { if (peer.Avatar != null && peer.Avatar.Root != null) peer.Avatar.Root.SetActive(false); continue; }
                if (peer.Avatar == null || peer.Avatar.Root == null || (!peer.Avatar.IsModel && player != null))
                {
                    DestroyAvatar(peer);
                    peer.Avatar = RemoteAvatar.Create(player, Colours[peer.Colour]);
                    peer.Avatar.ApplyVisibility(peer.Visible);
                    peer.ShownPosition = peer.Position; peer.ShownRotation = peer.Rotation;
                    AnimSync.Apply(peer.Avatar.Anim, peer.Anim, true);
                }
                if (!peer.Avatar.Root.activeSelf) { peer.Avatar.Root.SetActive(true); AnimSync.Apply(peer.Avatar.Anim, peer.Anim, true); }
                float k = 1f - Mathf.Exp(-12f * dt);
                peer.ShownPosition = Vector3.Lerp(peer.ShownPosition, peer.Position, k);
                peer.ShownRotation = Quaternion.Slerp(peer.ShownRotation, peer.Rotation, k);
                if (!peer.Avatar.IsModel) peer.Avatar.Root.transform.SetPositionAndRotation(peer.ShownPosition + Vector3.up, Quaternion.Euler(0, peer.ShownRotation.eulerAngles.y, 0));
                else AnimSync.Apply(peer.Avatar.Anim, peer.Anim, false);
            }
        }

        // The copied rig has no root motion, so its transform is placed after the animator has evaluated. Then the
        // sender's bone pose (arms on door handles, IK, cutscene poses) overrides whatever the animator produced.
        void LateUpdateAvatars()
        {
            float k = 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);
            Camera cam = Camera.main;
            foreach (Peer peer in peers.Values)
            {
                RemoteAvatar avatar = peer.Avatar;
                if (avatar == null || !avatar.IsModel || avatar.Root == null || !avatar.Root.activeSelf) continue;
                avatar.Root.transform.SetPositionAndRotation(peer.ShownPosition, peer.ShownRotation);
                // Level of detail: a player far away (over 12 m) or behind the camera gets its 68 bones every 4th frame,
                // unsmoothed; nearby players in view are posed every frame.
                bool far = false;
                if (cam != null)
                {
                    Vector3 d = peer.ShownPosition - cam.transform.position;
                    far = d.sqrMagnitude > 144f || Vector3.Dot(d, cam.transform.forward) < -0.5f * d.magnitude;
                }
                bool posed = Time.realtimeSinceStartup - peer.BonesAt < 0.5f;
                if (far && posed && avatar.BonesAt >= 0 && (Time.frameCount + peer.Id) % 4 != 0) continue;
                bool fresh = posed && avatar.ApplyBones(peer.Bones, avatar.BonesAt < 0 || far ? 1f : k);
                if (fresh) avatar.BonesAt = peer.BonesAt;
                if (avatar.Anim.Animator != null && avatar.Anim.Animator.enabled == fresh) avatar.Anim.Animator.enabled = !fresh;
            }
        }

        void DrawNameTags(GUIStyle style)
        {
            if (!Connected) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            foreach (Peer peer in peers.Values)
            {
                if (peer.Avatar == null || peer.Avatar.Root == null || !peer.Avatar.Root.activeSelf) continue;
                Vector3 world = peer.Avatar.TagPosition;
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0 || screen.z > 40) continue;
                Rect rect = new Rect(screen.x - 100, Screen.height - screen.y - 12, 200, 24);
                Color previous = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.8f);
                GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), peer.Name, style);
                GUI.color = Colours[peer.Colour];
                GUI.Label(rect, peer.Name, style);
                GUI.color = previous;
            }
        }
    }
}
