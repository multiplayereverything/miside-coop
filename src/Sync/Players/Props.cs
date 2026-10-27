using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Loose physics props (cups, boxes, things players knock over): whoever moves one streams it while it moves, and
    // everyone else follows (last mover wins, like doors). Character ragdolls, doors and anything kinematic are excluded.
    public sealed partial class TogetherRuntime
    {
        List<Rigidbody> propList;
        float propListAt, nextProp;
        readonly Dictionary<string, float> propAppliedAt = new Dictionary<string, float>();
        public int propsSent, propsApplied;

        List<Rigidbody> Props()
        {
            float now = Time.realtimeSinceStartup;
            if (propList != null && now - propListAt < 3f) return propList;
            propListAt = now;
            propList = new List<Rigidbody>();
            PlayerMove player = LocalPlayer;
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
            {
                if (rb == null || !rb.gameObject.scene.isLoaded) continue;
                if (player != null && rb.transform.IsChildOf(player.transform)) continue;
                if (rb.GetComponentInParent<ObjectDoor>() != null || rb.GetComponentInParent<Rigidbody_Ragdoll>() != null
                    || rb.GetComponentInParent<MitaPerson>() != null || rb.GetComponentInParent<PlayerMove>() != null) continue;
                // Scene 14's phone character is the holder's: a watcher's copy only follows it (PhoneCharacter.cs).
                if (MobileWatcher && rb.GetComponent<Location14_PlayerMobileMove>() != null) continue;
                propList.Add(rb);
                if (propList.Count >= 256) break;
            }
            return propList;
        }

        void PropTick(float now)
        {
            if (now < nextProp || (IsClient && !ClientStoryLive)) return;
            nextProp = now + 0.1f;
            int sent = 0;
            foreach (Rigidbody rb in Props())
            {
                if (rb == null || sent >= 20) continue;
                if (rb.velocity.sqrMagnitude < 0.0025f && rb.angularVelocity.sqrMagnitude < 0.01f) continue;
                string key = SceneKeys.Key(rb);
                float applied;
                // A prop the network is moving is not echoed back while it settles.
                if (propAppliedAt.TryGetValue(key, out applied) && now - applied < 0.5f) continue;
                sent++; propsSent++;
                Vector3 p = rb.position, v = rb.velocity, av = rb.angularVelocity; Quaternion r = rb.rotation;
                Broadcast(Wire.Packet(Op.Prop, w => { Wire.Text(w, key); WritePose(w, p, r); w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(av.x); w.Write(av.y); w.Write(av.z); }));
            }
        }

        void ApplyProp(BinaryReader reader, int from)
        {
            long start = reader.BaseStream.Position;
            string key = Wire.Text(reader, 1024);
            Vector3 p; Quaternion r; ReadTransform(reader, out p, out r);
            Vector3 v = new Vector3(Wire.Number(reader), Wire.Number(reader), Wire.Number(reader));
            Vector3 av = new Vector3(Wire.Number(reader), Wire.Number(reader), Wire.Number(reader));
            if (Hosting)
            {
                reader.BaseStream.Position = start;
                byte[] body = new byte[reader.BaseStream.Length - start + 1];
                body[0] = (byte)Op.Prop;
                reader.BaseStream.Read(body, 1, body.Length - 1);
                Broadcast(body, from);
            }
            if (IsClient && !ClientStoryLive) return;
            Rigidbody rb = SceneKeys.Find<Rigidbody>(key);
            if (rb == null) return;
            // Scene 14's phone character stays kinematic on a watcher (its Play, which frees it, runs only for the
            // holder): it is placed, not simulated (PhoneCharacter.cs).
            if (rb.isKinematic && MobileWatcher && rb.GetComponent<Location14_PlayerMobileMove>() != null) rb.MovePosition(p);
            else if (rb.isKinematic) return;
            else { rb.position = p; rb.velocity = v; rb.angularVelocity = av; }
            rb.rotation = r;
            propAppliedAt[key] = Time.realtimeSinceStartup;
            propsApplied++;
        }
    }
}
