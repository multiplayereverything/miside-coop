using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Room-state sync: the host's rooms are the truth. The host sweeps the chapter's objects (a few hundred per frame)
    // and sends only what changed since the chapter started: objects switched on/off, objects moved (story placement),
    // and lights (on/off, intensity, colour, range). Clients apply it; a client arriving later gets every change so far.
    // Scene animators (drawers, the TV, lamps, story props) are tracked by state: when the host's layer-0 state changes,
    // clients jump to it, unless their own story got there by itself within a second.
    // Left to their own sync: player bodies, NPCs, doors, carried objects, dialogue lines and UI.
    public sealed partial class TogetherRuntime
    {
        sealed class Tracked { public Transform T; public GameObject G; public string Key; public bool Active; public Vector3 Pos; public Quaternion Rot; public bool Poses; }
        sealed class TrackedLight { public Light L; public string Key; public bool On; public float Intensity, Range; public Color Color; }
        sealed class TrackedAnimator { public Animator A; public string Key; public int State; }
        sealed class PendingAnimator { public Animator A; public int State; public float Time, At; }
        readonly List<TrackedAnimator> trackedAnimators = new List<TrackedAnimator>();
        readonly Dictionary<string, PendingAnimator> pendingAnimators = new Dictionary<string, PendingAnimator>();
        int animatorSweep;
        public int animatorStatesApplied;
        readonly List<Tracked> tracked = new List<Tracked>();
        readonly List<TrackedLight> trackedLights = new List<TrackedLight>();
        readonly Dictionary<string, byte[]> stateChanges = new Dictionary<string, byte[]>();   // latest change per key (for late arrivals)
        readonly List<byte[]> pendingState = new List<byte[]>();
        int sweep, lightSweep;
        bool stateReady;
        float stateReadyAt = -1, nextStateSend;
        public int stateApplied, stateSent;
        const int MaxTracked = 8000;

        void ResetWorldState() { buildList = null; buildSkip = null; buildAnimated = null; trackedAnimators.Clear(); pendingAnimators.Clear(); animatorSweep = 0; tracked.Clear(); trackedLights.Clear(); stateChanges.Clear(); pendingState.Clear(); sweep = 0; lightSweep = 0; stateReady = false; stateReadyAt = -1; deferredOff.Clear(); }

        // Built once per chapter, a moment after the player exists, from every loaded scene object we do not sync otherwise.
        void BuildTracked()
        {
            PlayerMove player = LocalPlayer;
            HashSet<IntPtr> skip = new HashSet<IntPtr>();
            void SkipTree(Transform t) { if (t != null) foreach (Transform c in t.GetComponentsInChildren<Transform>(true)) skip.Add(c.Pointer); }
            if (player != null) SkipTree(player.transform);
            foreach (NpcRef n in Npcs()) SkipTree(n.Root);
            foreach (ObjectDoor d in Object.FindObjectsOfType<ObjectDoor>(true)) if (d != null) SkipTree(d.transform);
            foreach (Dialogue_3DText d in Object.FindObjectsOfType<Dialogue_3DText>(true)) if (d != null) SkipTree(d.transform);
            HashSet<IntPtr> animated = new HashSet<IntPtr>();
            foreach (Animator a in Object.FindObjectsOfType<Animator>(true))
            {
                if (a == null) continue;
                foreach (Transform c in a.GetComponentsInChildren<Transform>(true)) animated.Add(c.Pointer);
                if (!skip.Contains(a.transform.Pointer) && a.runtimeAnimatorController != null && a.gameObject.scene.isLoaded && a.gameObject.scene.name != "DontDestroyOnLoad" && a.transform.TryCast<RectTransform>() == null)
                    trackedAnimators.Add(new TrackedAnimator { A = a, Key = SceneKeys.Key(a), State = AnimatorState(a) });
            }
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>(true)) if (rb != null) foreach (Transform c in rb.GetComponentsInChildren<Transform>(true)) animated.Add(c.Pointer);
            // Carried objects travel with whoever holds them (Interactions.cs), not with the host's room.
            foreach (Transform_Magnet m in Object.FindObjectsOfType<Transform_Magnet>(true)) if (m != null) foreach (Transform c in m.GetComponentsInChildren<Transform>(true)) animated.Add(c.Pointer);
            buildSkip = skip; buildAnimated = animated;
            buildList = Object.FindObjectsOfType<Transform>(true);
            buildIndex = 0;
            foreach (Light l in Object.FindObjectsOfType<Light>(true))
                if (l != null && !skip.Contains(l.transform.Pointer) && l.gameObject.scene.isLoaded)
                    trackedLights.Add(new TrackedLight { L = l, Key = SceneKeys.Key(l), On = l.enabled, Intensity = l.intensity, Range = l.range, Color = l.color });
        }

        // Keys and first states for every object, 1000 a frame (one big chapter took ~80 ms in a single frame).
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Transform> buildList;
        HashSet<IntPtr> buildSkip, buildAnimated;
        int buildIndex;

        bool BuildTrackedStep()
        {
            if (buildList == null) return true;
            int end = Math.Min(buildList.Length, buildIndex + 1000);
            for (; buildIndex < end && tracked.Count < MaxTracked; buildIndex++)
            {
                Transform t = buildList[buildIndex];
                if (t == null || buildSkip.Contains(t.Pointer) || !t.gameObject.scene.isLoaded || t.gameObject.scene.name == "DontDestroyOnLoad" || t.name.StartsWith("MiSideTogether", StringComparison.Ordinal)) continue;
                bool ui = t.TryCast<RectTransform>() != null;
                tracked.Add(new Tracked { T = t, G = t.gameObject, Key = SceneKeys.Key(t), Active = t.gameObject.activeSelf, Pos = t.localPosition, Rot = t.localRotation, Poses = !ui && !buildAnimated.Contains(t.Pointer) });
            }
            if (buildIndex < buildList.Length && tracked.Count < MaxTracked) return false;
            buildList = null; buildSkip = null; buildAnimated = null;
            stateReady = true;
            Log("Room state: tracking " + tracked.Count + " objects, " + trackedLights.Count + " lights and " + trackedAnimators.Count + " animators");
            return true;
        }

        void WorldStateTick(float now)
        {
            if (!Hosting) { DeferredOffTick(now); return; }
            if (!stateReady)
            {
                if (LocalPlayer == null) { stateReadyAt = -1; return; }
                if (stateReadyAt < 0) stateReadyAt = now + 2;
                if (buildList == null && now >= stateReadyAt && trackedLights.Count == 0 && trackedAnimators.Count == 0) BuildTracked();
                if (buildList != null) BuildTrackedStep();
                return;
            }
            // About 120 objects a frame: a full sweep of a big chapter (1500 objects) takes about 0.2 s at 60 fps, 0.4 s at
            // 30 (at 60 a frame, story switches in Scene 1's app reached clients up to 2 s late).
            for (int n = 0; n < 120 && tracked.Count > 0; n++)
            {
                Tracked e = tracked[sweep++ % tracked.Count];
                if (e.G == null) continue;
                bool active = e.G.activeSelf;
                if (active != e.Active) { e.Active = active; Queue(ActivePacket(e.Key, active)); }
                if (!e.Poses) continue;
                Vector3 p = e.T.localPosition; Quaternion r = e.T.localRotation;
                if ((p - e.Pos).sqrMagnitude > 0.0004f || Quaternion.Angle(r, e.Rot) > 2f) { e.Pos = p; e.Rot = r; Queue(PlacePacket(e.Key, p, r)); }
            }
            for (int n = 0; n < 8 && trackedLights.Count > 0; n++)
            {
                TrackedLight l = trackedLights[lightSweep++ % trackedLights.Count];
                if (l.L == null) continue;
                bool on = l.L.enabled; float i = l.L.intensity, range = l.L.range; Color c = l.L.color;
                if (on == l.On && Mathf.Abs(i - l.Intensity) < 0.02f && Mathf.Abs(range - l.Range) < 0.05f && Mathf.Abs(c.r - l.Color.r) + Mathf.Abs(c.g - l.Color.g) + Mathf.Abs(c.b - l.Color.b) < 0.02f) continue;
                l.On = on; l.Intensity = i; l.Range = range; l.Color = c;
                Queue(LightPacket(l.Key, on, i, range, c));
            }
            for (int n = 0; n < 8 && trackedAnimators.Count > 0; n++)
            {
                TrackedAnimator a = trackedAnimators[animatorSweep++ % trackedAnimators.Count];
                if (a.A == null || !a.A.isActiveAndEnabled) continue;
                int state = AnimatorState(a.A);
                if (state == a.State || state == 0) continue;
                a.State = state;
                float t = a.A.GetCurrentAnimatorStateInfo(0).normalizedTime;
                Queue(AnimatorPacket(a.Key, state, t));
            }
            if (now >= nextStateSend && pendingState.Count > 0) { nextStateSend = now + 0.2f; FlushState(-1); }
        }

        // Entry: kind byte (1 active, 2 place, 3 light), key, payload. Grouped into Op.WorldState packets.
        static byte[] ActivePacket(string key, bool active) { return Entry(1, key, w => w.Write(active)); }
        static byte[] PlacePacket(string key, Vector3 p, Quaternion r) { return Entry(2, key, w => WritePose(w, p, r)); }
        static byte[] LightPacket(string key, bool on, float i, float range, Color c) { return Entry(3, key, w => { w.Write(on); w.Write(i); w.Write(range); w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }); }

        static byte[] AnimatorPacket(string key, int state, float time) { return Entry(4, key, w => { w.Write(state); w.Write(time); }); }

        static int AnimatorState(Animator a)
        {
            if (a == null || !a.isActiveAndEnabled || a.layerCount == 0) return 0;
            return a.GetCurrentAnimatorStateInfo(0).fullPathHash;
        }

        // Clients: a host animator state this machine has not reached by itself a second later is played here.
        void AnimatorCatchUpTick(float now)
        {
            if (pendingAnimators.Count == 0) return;
            List<string> done = null;
            foreach (KeyValuePair<string, PendingAnimator> pair in pendingAnimators)
            {
                PendingAnimator p = pair.Value;
                if (p.A == null || AnimatorState(p.A) == p.State) { (done ?? (done = new List<string>())).Add(pair.Key); continue; }
                if (now - p.At < 1f || !p.A.isActiveAndEnabled) continue;
                // Loops keep their phase; one-shots (a drawer opening) land where the host's is now.
                float t = p.Time + (now - p.At) * 0.5f;
                try { p.A.Play(p.State, 0, Mathf.Min(t, 1f)); animatorStatesApplied++; } catch (Exception ex) { Log("Animator state failed: " + ex.Message); }
                (done ?? (done = new List<string>())).Add(pair.Key);
            }
            if (done != null) foreach (string k in done) pendingAnimators.Remove(k);
        }

        static byte[] Entry(byte kind, string key, Action<BinaryWriter> body)
        {
            using (MemoryStream m = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(m)) { w.Write(kind); Wire.Text(w, key); body(w); return m.ToArray(); }
        }

        void Queue(byte[] entry)
        {
            pendingState.Add(entry);
            // Latest state per key and kind, for players who arrive later.
            using (BinaryReader r = new BinaryReader(new MemoryStream(entry))) { byte kind = r.ReadByte(); stateChanges[kind + ":" + Wire.Text(r, 1024)] = entry; }
        }

        void FlushState(int to)
        {
            List<byte[]> batch = to < 0 ? pendingState : new List<byte[]>(stateChanges.Values);
            int i = 0;
            while (i < batch.Count)
            {
                int start = i, size = 16;
                while (i < batch.Count && size + batch[i].Length + 2 < Transport.MaxFrame - 64) { size += batch[i].Length + 2; i++; }
                int end = i, scene = LocalSceneHash;
                byte[] packet = Wire.Packet(Op.WorldState, w =>
                {
                    w.Write(scene); w.Write((ushort)(end - start));
                    for (int k = start; k < end; k++) { w.Write((ushort)batch[k].Length); w.Write(batch[k]); }
                });
                if (to < 0) Broadcast(packet); else Network.Send(to, packet);
                stateSent += end - start;
                if (end == start) break;
            }
            if (to < 0) pendingState.Clear();
        }

        void SendWorldStateTo(int peer) { if (Hosting && stateChanges.Count > 0) FlushState(peer); }

        void ReadWorldState(BinaryReader reader)
        {
            int scene = reader.ReadInt32();
            int count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                int size = reader.ReadUInt16();
                byte[] entry = Transport.ReadExactly(reader.BaseStream, size);
                if (scene != LocalSceneHash || !ClientStoryLive) continue;
                try { ApplyStateEntry(entry); } catch (Exception ex) { Log("Room state entry failed: " + ex.Message); }
            }
        }

        // A room the host's story switches off (the host moved on, into the phone app, through a door that unloads the
        // area behind) stays on here while this player still stands in it; it goes off once they leave. Playtest: a
        // player left behind saw a black void. "In it": inside the bounds of its solid colliders, if those are room-sized.
        readonly Dictionary<string, Transform> deferredOff = new Dictionary<string, Transform>();
        float deferredCheckAt;

        static bool Around(Transform t, PlayerMove player)
        {
            if (player == null || t == null) return false;
            Bounds b = new Bounds(); bool any = false;
            foreach (Collider c in t.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger || !c.enabled) continue;
                if (any) b.Encapsulate(c.bounds); else { b = c.bounds; any = true; }
            }
            if (!any || b.size.x < 2.5f || b.size.z < 2.5f) return false;   // a prop, not a room
            b.Expand(1f);
            return b.Contains(player.transform.position + Vector3.up);
        }

        void DeferredOffTick(float now)
        {
            if (deferredOff.Count == 0 || now < deferredCheckAt) return;
            deferredCheckAt = now + 1;
            PlayerMove player = LocalPlayer;
            List<string> done = new List<string>();
            foreach (KeyValuePair<string, Transform> d in deferredOff)
            {
                if (d.Value == null) { done.Add(d.Key); continue; }
                if (Around(d.Value, player)) continue;
                done.Add(d.Key);
                if (!d.Value.gameObject.activeSelf) continue;
                Replaying = true;
                try { d.Value.gameObject.SetActive(false); stateApplied++; Log("Room state: " + d.Key + " off now that this player left it"); }
                finally { Replaying = false; }
            }
            foreach (string k in done) deferredOff.Remove(k);
        }

        void ApplyStateEntry(byte[] entry)
        {
            using (BinaryReader r = new BinaryReader(new MemoryStream(entry)))
            {
                byte kind = r.ReadByte();
                string key = Wire.Text(r, 1024);
                if (kind == 3)
                {
                    bool on = r.ReadBoolean(); float i = Wire.Number(r), range = Wire.Number(r);
                    Color c = new Color(Wire.Number(r), Wire.Number(r), Wire.Number(r), Wire.Number(r));
                    Light l = SceneKeys.Find<Light>(key);
                    if (l == null) return;
                    l.enabled = on; l.intensity = i; l.range = range; l.color = c;
                    stateApplied++;
                    return;
                }
                if (kind == 4)
                {
                    int state = r.ReadInt32(); float time = Wire.Number(r);
                    Animator a = SceneKeys.Find<Animator>(key);
                    if (a != null) pendingAnimators[key] = new PendingAnimator { A = a, State = state, Time = time, At = Time.realtimeSinceStartup };
                    return;
                }
                Transform t = SceneKeys.Find<Transform>(key);
                if (t == null) return;
                PlayerMove player = LocalPlayer;
                if (player != null && t.IsChildOf(player.transform)) return;
                if (kind == 1)
                {
                    bool on = r.ReadBoolean();
                    deferredOff.Remove(key);
                    if (!on && t.gameObject.activeSelf && Around(t, player)) { deferredOff[key] = t; Log("Room state: keeping " + key + " on while this player is in it"); return; }
                    if (t.gameObject.activeSelf == on) return;
                    Replaying = true;
                    try { t.gameObject.SetActive(on); stateApplied++; }
                    finally { Replaying = false; }
                    return;
                }
                Replaying = true;
                try
                {
                    if (kind == 2) { Vector3 p; Quaternion q; ReadTransform(r, out p, out q); t.localPosition = p; t.localRotation = q; }
                    stateApplied++;
                }
                finally { Replaying = false; }
            }
        }
    }
}
