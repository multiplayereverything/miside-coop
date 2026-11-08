using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // Live minigames (Minigames.cs: Scene 4's console and card table) run on the player's PC alone; a watcher's copy is
    // never started, so it would stand at its first frame. The player's PC streams what can be seen of it (Op.Screen,
    // 10 a second): the parts of the game's object that changed (position, rotation, scale, on or off, a UI part's
    // size; all of them once a second) and its animators. The watchers put their copy's parts there after the game's
    // own update. A part is known by its path inside the game, so something spawned on the player's PC (a thrown card)
    // doesn't move the others; a screen overlay (the card game's hints and score) stays the player's own.
    public sealed partial class TogetherRuntime
    {
        const int ScreenMaxParts = 600, ScreenMaxAnimators = 32;
        sealed class ScreenPart { public int Id; public bool On; public Vector3 Pos, Scale; public Quaternion Rot; public Vector2 Size; }

        // Sender
        float screenSendAt, screenFullAt;
        string screenSentKey;
        readonly List<Transform> screenOut = new List<Transform>();
        readonly List<int> screenOutIds = new List<int>();
        readonly List<Animator> screenOutAnims = new List<Animator>();
        readonly List<int> screenOutAnimIds = new List<int>();
        readonly Dictionary<int, ScreenPart> screenSent = new Dictionary<int, ScreenPart>();
        readonly AnimFrame screenFrame = new AnimFrame();

        // Watcher
        string screenKey;
        float screenAt;
        IntPtr screenRoot;
        int screenRootChildren = -1;
        readonly Dictionary<int, ScreenPart> screenTarget = new Dictionary<int, ScreenPart>();
        readonly Dictionary<int, AnimFrame> screenAnims = new Dictionary<int, AnimFrame>();
        readonly List<Transform> screenIn = new List<Transform>();
        readonly List<int> screenInIds = new List<int>();
        readonly Dictionary<int, Transform> screenById = new Dictionary<int, Transform>();
        readonly List<Animator> screenInAnims = new List<Animator>();
        readonly List<int> screenInAnimIds = new List<int>();
        readonly Dictionary<int, AnimTarget> screenAnimTargets = new Dictionary<int, AnimTarget>();

        int screenKind;

        // Drawn on the player's own screen (the card game's prompts and score), not in the world: the console's picture
        // is a canvas on the TV's own camera, and that one is shown.
        static bool PlayerOverlay(Canvas c)
        {
            if (c == null || c.renderMode == RenderMode.WorldSpace) return false;
            if (c.renderMode == RenderMode.ScreenSpaceOverlay) return true;
            Camera cam = c.worldCamera;
            return cam == null || cam == Camera.main;
        }

        // A spawned game (the console's Fight(Clone)) may sit at another place among the scene's roots on each PC, so
        // its key can miss: then it is the one object of the game's type with that name.
        static Transform LiveGameObject(string key, int kind)
        {
            Transform t = SceneKeys.Find<Transform>(key);
            Il2CppSystem.Type type = Il2CppInterop.Runtime.Il2CppType.From(MinigameKinds[kind].Type);
            if (t != null && t.GetComponent(type) != null) return t;
            string name = key.Substring(key.LastIndexOf('/') + 1);
            int hash = name.LastIndexOf('#');
            if (hash > 0) name = name.Substring(0, hash);
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(type, true))
            {
                Component c = o.TryCast<Component>();
                if (c != null && c.name == name) return c.transform;
            }
            return null;
        }

        // The card table's object also holds story pieces (Mita's lines, timers, the player's animations, Mita herself):
        // those follow the story log on every PC, so the mirror leaves them and everything under them alone.
        static bool StoryPart(Transform t)
        {
            return t.GetComponent<Dialogue_3DText>() != null || t.GetComponent<Time_Events>() != null || t.GetComponent<ObjectAnimationPlayer>() != null
                || t.GetComponent<MitaPerson>() != null || t.GetComponent<ObjectInteractive>() != null || t.GetComponent<Trigger_Event>() != null || t.GetComponent<Events_Data>() != null;
        }

        static int NameHash(string s) { unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return h; } }

        // The same walk on every PC (the copies come from the same prefab); a part's id is its path of names and places.
        // The console game moves its interface (hearts, KO) out of its own object onto the TV as it starts
        // (MinigamesTelevisionGame.objectInterface), so that is walked too, as a second root.
        static void ScreenWalk(Transform root, int kind, List<Transform> parts, List<int> ids, List<Animator> animators, List<int> animatorIds)
        {
            parts.Clear(); ids.Clear(); animators.Clear(); animatorIds.Clear();
            ScreenWalkFrom(root, 17, parts, ids, animators, animatorIds);
            Func<Component, Transform> extra = MinigameKinds[kind].Extra;
            Component game = extra != null ? root.GetComponent(Il2CppInterop.Runtime.Il2CppType.From(MinigameKinds[kind].Type)) : null;
            Transform second = game != null ? extra(game) : null;
            if (second != null && !second.IsChildOf(root)) ScreenWalkFrom(second, 1000003, parts, ids, animators, animatorIds);
        }

        static void ScreenWalkFrom(Transform root, int seed, List<Transform> parts, List<int> ids, List<Animator> animators, List<int> animatorIds)
        {
            Stack<KeyValuePair<Transform, int>> todo = new Stack<KeyValuePair<Transform, int>>();
            todo.Push(new KeyValuePair<Transform, int>(root, seed));
            while (todo.Count > 0 && parts.Count < ScreenMaxParts)
            {
                KeyValuePair<Transform, int> next = todo.Pop();
                Transform t = next.Key;
                Canvas canvas = t.GetComponent<Canvas>();
                if (t != root && (PlayerOverlay(canvas) || StoryPart(t))) continue;
                parts.Add(t); ids.Add(next.Value);
                Animator a = t.GetComponent<Animator>();
                if (a != null && animators.Count < ScreenMaxAnimators) { animators.Add(a); animatorIds.Add(next.Value); }
                for (int i = t.childCount - 1; i >= 0; i--)
                {
                    Transform c = t.GetChild(i);
                    unchecked { todo.Push(new KeyValuePair<Transform, int>(c, (next.Value * 31 + NameHash(c.name)) * 31 + i)); }
                }
            }
        }

        static ScreenPart ReadPart(Transform t, int id)
        {
            RectTransform rect = t.TryCast<RectTransform>();
            return new ScreenPart { Id = id, On = t.gameObject.activeSelf, Pos = t.localPosition, Rot = t.localRotation, Scale = t.localScale, Size = rect != null ? rect.sizeDelta : Vector2.zero };
        }

        static bool SamePart(ScreenPart a, ScreenPart b)
        {
            return a.On == b.On && (a.Pos - b.Pos).sqrMagnitude < 1e-8f && (a.Scale - b.Scale).sqrMagnitude < 1e-8f
                && Quaternion.Angle(a.Rot, b.Rot) < 0.01f && (a.Size - b.Size).sqrMagnitude < 1e-6f;
        }

        void ScreenSendTick(float now)
        {
            if (now < screenSendAt) return;
            screenSendAt = now + 0.1f;
            string key = null; Transform root = null; int kind = -1;
            foreach (MinigameRun r in minigames.Values)
                if (r.Owner == SelfId && IsLive(r.Kind)) { key = r.Key; kind = r.Kind; root = LiveGameObject(key, kind); if (root != null) break; }
            if (key == null) { screenSentKey = null; return; }
            if (root == null) { if (screenSentKey != "") Log("Screen: the live game's object isn't found here: " + key); screenSentKey = ""; return; }
            bool full = key != screenSentKey || now >= screenFullAt;
            // The walk (hundreds of parts at the card table) is redone with each full picture, once a second.
            if (full) ScreenWalk(root, kind, screenOut, screenOutIds, screenOutAnims, screenOutAnimIds);
            if (key != screenSentKey) Log("Screen: showing " + key + " to the others (" + screenOut.Count + " parts, " + screenOutAnims.Count + " animators)");
            if (full) { screenFullAt = now + 1; screenSentKey = key; screenSent.Clear(); }
            List<ScreenPart> changed = new List<ScreenPart>();
            for (int i = 0; i < screenOut.Count; i++)
            {
                if (screenOut[i] == null) continue;   // destroyed since the last walk
                ScreenPart p = ReadPart(screenOut[i], screenOutIds[i]), was;
                if (!full && screenSent.TryGetValue(p.Id, out was) && SamePart(p, was)) continue;
                screenSent[p.Id] = p; changed.Add(p);
            }
            byte[] packet = Wire.Packet(Op.Screen, w =>
            {
                Wire.Text(w, key);
                w.Write((byte)kind);
                w.Write((ushort)changed.Count);
                foreach (ScreenPart p in changed)
                {
                    w.Write(p.Id); w.Write(p.On);
                    w.Write(p.Pos.x); w.Write(p.Pos.y); w.Write(p.Pos.z);
                    w.Write(p.Rot.x); w.Write(p.Rot.y); w.Write(p.Rot.z); w.Write(p.Rot.w);
                    w.Write(p.Scale.x); w.Write(p.Scale.y); w.Write(p.Scale.z);
                    w.Write(p.Size.x); w.Write(p.Size.y);
                }
                w.Write((byte)screenOutAnims.Count);
                for (int i = 0; i < screenOutAnims.Count; i++) { w.Write(screenOutAnimIds[i]); screenFrame.Capture(screenOutAnims[i]); screenFrame.Write(w); }
            });
            if (Hosting) Broadcast(packet); else SendToHost(packet);
        }

        void ReadScreen(BinaryReader reader, int from)
        {
            if (Hosting)
            {
                long start = reader.BaseStream.Position;
                byte[] rest = new byte[reader.BaseStream.Length - start];
                reader.BaseStream.Read(rest, 0, rest.Length);
                reader.BaseStream.Position = start;
                Broadcast(Wire.Packet(Op.Screen, w => w.Write(rest)), from);
            }
            string key = Wire.Text(reader, 1024);
            int kind = reader.ReadByte();
            if (kind >= MinigameKinds.Length) throw new IOException("Unknown minigame.");
            screenKind = kind;
            if (key != screenKey) { Log("Screen: watching " + key); screenKey = key; screenTarget.Clear(); screenAnims.Clear(); screenRoot = IntPtr.Zero; screenAnimTargets.Clear(); }
            screenAt = Time.realtimeSinceStartup;
            int count = reader.ReadUInt16();
            if (count > ScreenMaxParts) throw new IOException("Too many screen parts.");
            for (int i = 0; i < count; i++)
            {
                ScreenPart p = new ScreenPart { Id = reader.ReadInt32(), On = reader.ReadBoolean() };
                p.Pos = new Vector3(Wire.Number(reader), Wire.Number(reader), Wire.Number(reader));
                p.Rot = new Quaternion(Wire.Number(reader), Wire.Number(reader), Wire.Number(reader), Wire.Number(reader));
                p.Scale = new Vector3(Wire.Number(reader), Wire.Number(reader), Wire.Number(reader));
                p.Size = new Vector2(Wire.Number(reader), Wire.Number(reader));
                if (screenTarget.Count < ScreenMaxParts || screenTarget.ContainsKey(p.Id)) screenTarget[p.Id] = p;
            }
            int anims = reader.ReadByte();
            if (anims > ScreenMaxAnimators) throw new IOException("Too many screen animators.");
            for (int i = 0; i < anims; i++)
            {
                int id = reader.ReadInt32();
                AnimFrame f;
                if (!screenAnims.TryGetValue(id, out f)) { if (screenAnims.Count >= ScreenMaxAnimators) throw new IOException("Too many screen animators."); screenAnims[id] = f = new AnimFrame(); }
                f.Read(reader);
            }
        }

        // Watcher, after the game's own update. A part the player's PC has and this one doesn't is left out.
        void ScreenApplyLate()
        {
            if (screenKey == null) return;
            if (Time.realtimeSinceStartup - screenAt > 1f || OwningMinigame) { screenKey = null; screenTarget.Clear(); screenAnimTargets.Clear(); return; }
            Transform root = LiveGameObject(screenKey, screenKind);
            if (root == null)
            {
                if (screenRoot != (IntPtr)1)
                {
                    List<string> here = new List<string>();
                    foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.From(MinigameKinds[screenKind].Type), true)) { Component c = o.TryCast<Component>(); if (c != null) here.Add(PathOf(c.transform) + (c.gameObject.activeInHierarchy ? "" : " (off)")); }
                    Log("Screen: the player's game isn't found here: " + screenKey + "; " + MinigameKinds[screenKind].Type.Name + " here: " + (here.Count == 0 ? "none" : string.Join(", ", here)));
                }
                screenRoot = (IntPtr)1; return;
            }
            if (screenRoot == (IntPtr)1) Log("Screen: found the player's game here: " + PathOf(root));
            if (root.Pointer != screenRoot || root.childCount != screenRootChildren)
            {
                screenRoot = root.Pointer; screenRootChildren = root.childCount;
                ScreenWalk(root, screenKind, screenIn, screenInIds, screenInAnims, screenInAnimIds);
                // The player's prompts and score are on their screen only; here the copy's overlays stay off.
                foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true)) if (PlayerOverlay(c)) c.enabled = false;
                if (MinigameKinds[screenKind].Quiet)
                    foreach (MonoBehaviour b in root.GetComponentsInChildren<MonoBehaviour>(true))
                        if (b != null && b.enabled && b.GetIl2CppType().Assembly.GetName().Name == "Assembly-CSharp" && b.GetIl2CppType().Name != MinigameKinds[screenKind].Type.Name) b.enabled = false;
                screenById.Clear();
                for (int i = 0; i < screenIn.Count; i++) screenById[screenInIds[i]] = screenIn[i];
                int match = 0; foreach (int id in screenTarget.Keys) if (screenById.ContainsKey(id)) match++;
                Log("Screen: showing the player's game on " + PathOf(root) + " here (" + screenIn.Count + " parts; " + match + " of their " + screenTarget.Count + " found)");
                screenAnimTargets.Clear();
            }
            foreach (KeyValuePair<int, ScreenPart> pair in screenTarget)
            {
                Transform t;
                if (!screenById.TryGetValue(pair.Key, out t) || t == null) continue;
                ScreenPart p = pair.Value;
                if (t.gameObject.activeSelf != p.On) t.gameObject.SetActive(p.On);
                t.localPosition = p.Pos; t.localRotation = p.Rot; t.localScale = p.Scale;
                RectTransform rect = t.TryCast<RectTransform>();
                if (rect != null) rect.sizeDelta = p.Size;
            }
            for (int i = 0; i < screenInAnims.Count; i++)
            {
                Animator a = screenInAnims[i]; int id = screenInAnimIds[i];
                AnimFrame f;
                if (a == null || !a.isActiveAndEnabled || !screenAnims.TryGetValue(id, out f)) continue;
                AnimTarget target;
                if (!screenAnimTargets.TryGetValue(id, out target) || target.Animator != a) screenAnimTargets[id] = target = new AnimTarget { Animator = a };
                AnimSync.Apply(target, f, false);
            }
        }
    }
}
