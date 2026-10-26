using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // The skeleton of the main character's body, in a stable order every machine computes the same way: the bones of
    // its skinned meshes, sorted by path under the animator object. Bone poses travel in this order, so remote bodies
    // show exactly what the player's body does, including hand IK on door handles and items, sitting and cutscene poses.
    public sealed class BodyLayout
    {
        public const int MaxBones = 160;
        public Transform Root;
        public Transform[] Bones;
        public int Hash;
        public readonly Dictionary<string, Renderer> Renderers = new Dictionary<string, Renderer>();
        public readonly HashSet<string> BodyPaths = new HashSet<string>();

        public static BodyLayout Of(Transform root, HashSet<string> bodyPaths)
        {
            BodyLayout layout = new BodyLayout { Root = root };
            SortedDictionary<string, Transform> bones = new SortedDictionary<string, Transform>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer mesh in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string meshPath = Path(root, mesh.transform);
                // Only the character's own meshes define the skeleton; a held prop's bones would differ between players.
                if (meshPath == null || (bodyPaths.Count > 0 && !bodyPaths.Contains(meshPath))) continue;
                var meshBones = mesh.bones;
                if (meshBones == null) continue;
                for (int i = 0; i < meshBones.Length; i++)
                {
                    Transform bone = meshBones[i];
                    if (bone == null) continue;
                    string path = Path(root, bone);
                    if (path != null && !bones.ContainsKey(path)) bones[path] = bone;
                }
            }
            List<Transform> list = new List<Transform>();
            unchecked
            {
                int hash = 17;
                foreach (KeyValuePair<string, Transform> pair in bones)
                {
                    if (list.Count >= MaxBones) break;
                    list.Add(pair.Value);
                    hash = hash * 31 + Wire.Hash(pair.Key);
                }
                layout.Hash = hash;
            }
            layout.Bones = list.ToArray();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                string path = Path(root, r.transform);
                if (path != null && !layout.Renderers.ContainsKey(path)) layout.Renderers[path] = r;
            }
            foreach (string p in bodyPaths) layout.BodyPaths.Add(p);
            return layout;
        }

        public static string Path(Transform root, Transform t)
        {
            string path = "";
            for (; t != null && t != root; t = t.parent) path = path.Length == 0 ? t.name : t.name + "/" + path;
            return t == root ? path : null;
        }

        // Renderers the player can see on their own body right now, by path. First-person views hide the body with
        // shadow-only rendering, which still counts as shown for everyone else.
        public List<string> VisiblePaths()
        {
            List<string> visible = new List<string>();
            foreach (KeyValuePair<string, Renderer> pair in Renderers)
            {
                Renderer r = pair.Value;
                if (r != null && r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff) visible.Add(pair.Key);
            }
            visible.Sort(StringComparer.Ordinal);
            return visible;
        }
    }

    // One pose of every bone: local rotation (4 x int16) and local position (3 x half).
    public sealed class BoneFrame
    {
        public int Hash, Count;
        public readonly Quaternion[] Rotations = new Quaternion[BodyLayout.MaxBones];
        public readonly Vector3[] Positions = new Vector3[BodyLayout.MaxBones];

        public void Capture(BodyLayout layout)
        {
            Hash = layout.Hash; Count = 0;
            foreach (Transform bone in layout.Bones)
            {
                if (bone == null) { Rotations[Count] = Quaternion.identity; Positions[Count] = Vector3.zero; }
                else { Rotations[Count] = bone.localRotation; Positions[Count] = bone.localPosition; }
                Count++;
            }
        }

        public void Write(BinaryWriter w)
        {
            w.Write(Hash); w.Write((byte)Count);
            for (int i = 0; i < Count; i++)
            {
                Quaternion q = Rotations[i];
                w.Write(Pack(q.x)); w.Write(Pack(q.y)); w.Write(Pack(q.z)); w.Write(Pack(q.w));
                Vector3 p = Positions[i];
                w.Write(BitConverter.HalfToUInt16Bits((Half)Mathf.Clamp(p.x, -60000, 60000)));
                w.Write(BitConverter.HalfToUInt16Bits((Half)Mathf.Clamp(p.y, -60000, 60000)));
                w.Write(BitConverter.HalfToUInt16Bits((Half)Mathf.Clamp(p.z, -60000, 60000)));
            }
        }

        public void Read(BinaryReader r)
        {
            Hash = r.ReadInt32(); Count = r.ReadByte();
            if (Count > BodyLayout.MaxBones) throw new IOException("Too many bones.");
            for (int i = 0; i < Count; i++)
            {
                Quaternion q = new Quaternion(Unpack(r.ReadInt16()), Unpack(r.ReadInt16()), Unpack(r.ReadInt16()), Unpack(r.ReadInt16()));
                float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                Rotations[i] = n < 0.01f ? Quaternion.identity : new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n);
                Vector3 p = new Vector3(Half(r.ReadUInt16()), Half(r.ReadUInt16()), Half(r.ReadUInt16()));
                Positions[i] = IsBad(p.x) || IsBad(p.y) || IsBad(p.z) ? Vector3.zero : p;
            }
        }

        static short Pack(float v) { return (short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32767, 32767); }
        static float Unpack(short v) { return v / 32767f; }
        static float Half(ushort bits) { return (float)BitConverter.UInt16BitsToHalf(bits); }
        static bool IsBad(float v) { return float.IsNaN(v) || float.IsInfinity(v); }
    }

    public sealed partial class TogetherRuntime
    {
        // --- Sender: this player's body ---------------------------------------------------------------
        BodyLayout selfLayout;
        IntPtr selfLayoutRoot;
        string sentVisible = "";
        float nextVisible;

        BodyLayout SelfLayout(PlayerMove player)
        {
            if (player == null || player.animPerson == null) return null;
            Transform root = player.animPerson.transform;
            if (selfLayout == null || selfLayoutRoot != root.Pointer || selfLayout.Root == null)
            {
                selfLayout = BodyLayout.Of(root, RemoteAvatar.BodyMeshPaths(player));
                selfLayoutRoot = root.Pointer;
                Log("Body layout: " + selfLayout.Bones.Length + " bones, " + selfLayout.Renderers.Count + " renderers, hash " + selfLayout.Hash);
            }
            return selfLayout;
        }

        // Which props/outfit parts are showing on this body: sent when it changes (and every 3 s for late joiners).
        void SendVisibility(PlayerMove player)
        {
            BodyLayout layout = SelfLayout(player);
            if (layout == null) return;
            float now = Time.realtimeSinceStartup;
            List<string> visible = layout.VisiblePaths();
            string joined = string.Join("|", visible);
            if (joined == sentVisible && now < nextVisible) return;
            sentVisible = joined; nextVisible = now + 3;
            int id = SelfId;
            Broadcast(Wire.Packet(Op.Visible, w =>
            {
                w.Write(id); w.Write((ushort)Math.Min(visible.Count, 512));
                for (int i = 0; i < visible.Count && i < 512; i++) Wire.Text(w, visible[i]);
            }));
        }

        void ReadVisibility(BinaryReader reader, int from)
        {
            int id = reader.ReadInt32();
            if (Hosting) id = from;
            int count = reader.ReadUInt16();
            if (count > 512) throw new IOException("Too many renderers.");
            HashSet<string> visible = new HashSet<string>();
            for (int i = 0; i < count; i++) visible.Add(Wire.Text(reader, 256));
            if (Hosting)
            {
                List<string> list = new List<string>(visible);
                Broadcast(Wire.Packet(Op.Visible, w => { w.Write(id); w.Write((ushort)list.Count); foreach (string s in list) Wire.Text(w, s); }), from);
            }
            Peer peer;
            if (peers.TryGetValue(id, out peer)) { peer.Visible = visible; if (peer.Avatar != null) peer.Avatar.ApplyVisibility(visible); }
        }
    }
}
