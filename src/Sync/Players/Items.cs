using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Keys and held items.
    //  * Key items (GameController.AddKeyItem/RemoveKeyItem) are one shared inventory: a key anyone picks up opens the
    //    door for everyone, and using it uses it up for everyone. Relayed as story events (history covers late joins).
    //  * Hand items (PlayerMove.TakeItem/RemoveItem) belong to whoever holds them: everyone else sees the item leave
    //    the world and appear in that player's hand on their body, and land wherever the holder put it down.
    public sealed partial class TogetherRuntime
    {
        // --- Key items: replayed through ApplyStoryEvent (Op.KeyAdd / Op.KeyRemove) ----------------------------
        static void ApplyKeyItem(Op op, string key)
        {
            Transform item = SceneKeys.Find<Transform>(key) ?? SceneKeys.FindItemByName(key);
            if (item == null) { Log("Key item not found here: " + key); return; }
            GameController gc = Object.FindObjectOfType<GameController>();
            if (gc == null) return;
            if (op == Op.KeyAdd) gc.AddKeyItem(item.gameObject); else gc.RemoveKeyItem(item.gameObject);
            keyItemsApplied++;
        }

        public static int keyItemsApplied;

        // --- Hand items ----------------------------------------------------------------------------------------
        sealed class HeldVisual { public GameObject Clone; public GameObject Original; }
        readonly Dictionary<int, HeldVisual> held = new Dictionary<int, HeldVisual>();
        public int handItemsShown;

        public static void AfterTakeItem(PlayerMove player, GameObject item, string keyBefore)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || Replaying || item == null || keyBefore == null) return;
            BodyLayout layout = rt.SelfLayout(player);
            Transform parent = item.transform.parent;
            string bone = layout != null && parent != null ? BodyLayout.Path(layout.Root, parent) : null;
            Vector3 lp = item.transform.localPosition; Quaternion lr = item.transform.localRotation; Vector3 ls = item.transform.localScale;
            int id = rt.SelfId;
            Log("Hand item taken: " + keyBefore + " into " + (bone ?? "(no bone)"));
            rt.Broadcast(Wire.Packet(Op.HandItem, w =>
            {
                w.Write(id); w.Write(true); Wire.Text(w, keyBefore); Wire.Text(w, bone ?? "");
                WritePose(w, lp, lr); w.Write(ls.x); w.Write(ls.y); w.Write(ls.z);
            }));
        }

        public static void AfterRemoveItem(PlayerMove player, GameObject item, string keyOfItem)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || Replaying || keyOfItem == null) return;
            bool exists = item != null;
            bool active = exists && item.activeInHierarchy;
            Vector3 p = exists ? item.transform.position : Vector3.zero; Quaternion r = exists ? item.transform.rotation : Quaternion.identity;
            int id = rt.SelfId;
            rt.Broadcast(Wire.Packet(Op.HandItem, w => { w.Write(id); w.Write(false); Wire.Text(w, keyOfItem); w.Write(active); WritePose(w, p, r); }));
        }

        void ReadHandItem(BinaryReader reader, int from)
        {
            int id = reader.ReadInt32();
            if (Hosting)
            {
                // Relay with the sender's real id (a client cannot speak for someone else).
                id = from;
                long body = reader.BaseStream.Position;
                byte[] rest = new byte[reader.BaseStream.Length - body];
                reader.BaseStream.Read(rest, 0, rest.Length);
                reader.BaseStream.Position = body;
                int sender = id;
                Broadcast(Wire.Packet(Op.HandItem, w => { w.Write(sender); w.Write(rest); }), from);
            }
            bool take = reader.ReadBoolean();
            string key = Wire.Text(reader, 1024);
            if (take)
            {
                string bone = Wire.Text(reader, 512);
                Vector3 lp; Quaternion lr; ReadTransform(reader, out lp, out lr);
                Vector3 ls = new Vector3(Wire.Number(reader), Wire.Number(reader), Wire.Number(reader));
                ShowHeld(id, key, bone, lp, lr, ls);
            }
            else
            {
                bool active = reader.ReadBoolean();
                Vector3 p; Quaternion r; ReadTransform(reader, out p, out r);
                DropHeld(id, key, active, p, r);
            }
        }

        void ShowHeld(int id, string key, string bone, Vector3 lp, Quaternion lr, Vector3 ls)
        {
            DropHeld(id, null, false, Vector3.zero, Quaternion.identity);
            Transform original = SceneKeys.Find<Transform>(key);
            Peer peer;
            if (original == null || !peers.TryGetValue(id, out peer) || peer.Avatar == null || peer.Avatar.Root == null)
            {
                Log("Hand item for player " + id + " not shown: " + (original == null ? "item not found " + key : "no body for that player yet"));
                return;
            }
            Transform hand = bone.Length > 0 ? peer.Avatar.Root.transform.Find(bone) : null;
            if (hand == null) hand = peer.Avatar.Head != null ? peer.Avatar.Head : peer.Avatar.Root.transform;
            // A render-only copy rides in the remote hand; the world object is hidden while someone else holds it.
            GameObject holder = new GameObject("MiSideTogether holder");
            holder.SetActive(false);
            GameObject clone = Object.Instantiate(original.gameObject, holder.transform).Cast<GameObject>();
            clone.name = "MiSideTogether held " + original.name;
            foreach (Component c in clone.GetComponentsInChildren<Component>(true))
                if (c != null && c.TryCast<Transform>() == null && c.TryCast<MeshRenderer>() == null && c.TryCast<MeshFilter>() == null && c.TryCast<SkinnedMeshRenderer>() == null)
                    try { Object.DestroyImmediate(c); } catch { }
            clone.transform.SetParent(hand, false);
            clone.transform.localPosition = lp; clone.transform.localRotation = lr; clone.transform.localScale = ls;
            clone.SetActive(true);
            Object.Destroy(holder);
            original.gameObject.SetActive(false);
            held[id] = new HeldVisual { Clone = clone, Original = original.gameObject };
            handItemsShown++;
        }

        void DropHeld(int id, string key, bool active, Vector3 p, Quaternion r)
        {
            HeldVisual h;
            if (held.TryGetValue(id, out h))
            {
                if (h.Clone != null) Object.Destroy(h.Clone);
                held.Remove(id);
                if (key != null && h.Original != null)
                {
                    h.Original.transform.SetPositionAndRotation(p, r);
                    h.Original.SetActive(active);
                }
            }
        }

        void ClearHeld()
        {
            foreach (HeldVisual h in held.Values) if (h.Clone != null) Object.Destroy(h.Clone);
            held.Clear();
        }
    }
}
