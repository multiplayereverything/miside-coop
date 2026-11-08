using System.Collections.Generic;
using UnityEngine;

namespace MiSideTogether
{
    // User report (2026-11-08, watching the lab): "the player holding the phone, the phone was offset by a lot".
    // props.inhand: on every PC, every 2 s, each item shown on another player's body (a renderer that isn't body, switched
    // on by the sender's visibility) is within 0.3 m of that body's left or right hand. Every sample is logged (MT-PROP).
    public sealed partial class TogetherRuntime
    {
        float labPropAt;
        int labPhoneChecks, labPhoneMine;
        int labPropSamples, labPropFar;
        string labPropWorst = "";
        float labPropWorstD;

        void LabPropsInHandTick(float now)
        {
            if (now < labPropAt) return;
            labPropAt = now + 2;
            // User report (2026-11-08, 0.3.15): the phone floats beside the holder's body. Scene 1's phone is the host's alone,
            // so a client's own player must never have it (day 2's story snaps it to whichever player runs the story).
            if (IsClient && ActiveSceneName == "Scene 1 - RealRoom" && LocalPlayer != null)
            {
                labPhoneChecks++;
                foreach (Transform_Magnet m in Magnets())
                {
                    if (m == null || !HeldBy(m, LocalPlayer.transform) || !SceneKeys.Key(m).Contains("Smartphone")) continue;
                    Renderer r = Moved(m).GetComponentInChildren<Renderer>();
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    labPhoneMine++;
                    Log("MT-PROP the phone is on this client's own player: " + SceneKeys.Key(m));
                    break;
                }
            }
            // Objects the game magnets to a player (Scene 1's phone), placed here on that player's body by the carry stream.
            foreach (KeyValuePair<string, Carried> pair in carried)
            {
                Peer owner;
                if (pair.Value.Item == null || !peers.TryGetValue(pair.Value.Owner, out owner) || owner.Avatar == null || owner.Avatar.Layout == null) continue;
                Transform item = pair.Value.Item;
                if (item.name.StartsWith("Dialogue", System.StringComparison.Ordinal)) continue;   // a spoken line kept at the player's face, not a thing in a hand
                Renderer shown = item.GetComponentInChildren<Renderer>();
                Vector3 at = shown != null ? shown.bounds.center : item.position;
                float d = NearestHand(owner.Avatar.Layout, at);
                if (d >= 0) LabPropSample(owner, pair.Key + " (bone " + (pair.Value.Bone.Length > 0 ? pair.Value.Bone.Substring(pair.Value.Bone.LastIndexOf('/') + 1) : "body root") + ")", d);
            }
            foreach (Peer p in peers.Values)
            {
                RemoteAvatar a = p.Avatar;
                if (a == null || !a.IsModel || a.Layout == null || a.Root == null || !a.Root.activeInHierarchy) continue;
                foreach (var pair in a.Layout.Renderers)
                {
                    Renderer r = pair.Value;
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || a.Layout.BodyPaths.Contains(pair.Key)) continue;
                    float d = NearestHand(a.Layout, r.bounds.center);
                    if (d >= 0) LabPropSample(p, pair.Key, d);
                }
            }
        }

        // The body copy's rig isn't humanoid to Unity, so hands are the bones named like them (as the carry code finds them).
        static float NearestHand(BodyLayout layout, Vector3 at)
        {
            float best = -1;
            foreach (Transform b in layout.Bones)
            {
                if (b == null) continue;
                string n = b.name.ToLowerInvariant();
                if (!n.Contains("wrist") && !n.Contains("hand")) continue;
                float d = Vector3.Distance(b.position, at);
                if (best < 0 || d < best) best = d;
            }
            return best;
        }

        void LabPropSample(Peer p, string what, float d)
        {
            labPropSamples++;
            if (d > 0.3f) labPropFar++;
            if (d > labPropWorstD) { labPropWorstD = d; labPropWorst = what; }
            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "MT-PROP {0} {1} {2:0.00} m from the nearest hand", p.Name, what, d));
        }

        // User report (2026-11-08): on the other players' screens the phone hangs in front of the body. phone.day2: during day 2
        // (Quest 4, the real room, the phone in the holder's hands) it is at the host's hand on every client. MT-PHONE per sample.
        float labPhoneTraceAt;
        int labPhoneDay2, labPhoneDay2Far;
        void LabPhoneTrace(float now)
        {
            if (now < labPhoneTraceAt || !IsClient) return;
            labPhoneTraceAt = now + 1;
            Tamagotchi_Main app = Object.FindObjectOfType<Tamagotchi_Main>();
            if ((app != null && app.play) || !ActiveQuests().Contains("Quest 4 -")) return;
            GameObject room = SceneObject("World RealRoom");
            Transform phone = room != null ? room.transform.Find("Smartphone") : null;
            Renderer r = phone != null ? phone.GetComponent<Renderer>() : null;
            Peer host;
            if (r == null || !r.enabled || !phone.gameObject.activeInHierarchy || !peers.TryGetValue(0, out host) || host.Avatar == null || host.Avatar.Layout == null) return;
            float d = NearestHand(host.Avatar.Layout, r.bounds.center);
            labPhoneDay2++;
            if (d > 0.3f) labPhoneDay2Far++;
            Log("MT-PHONE day 2: " + d.ToString("0.00") + " m from the host's hand");
        }

        void LabPropsInHandCheck()
        {
            if (labPhoneDay2 > 0) CheckOnce("phone.day2", labPhoneDay2Far < 2, labPhoneDay2Far + " of " + labPhoneDay2 + " day 2 samples had the phone away from the host's hand");
            if (labPhoneChecks > 0) CheckOnce("phone.notmine", labPhoneMine == 0, labPhoneMine + " of " + labPhoneChecks + " samples had the host's phone on this client's own player");
            if (labPropSamples > 0) CheckOnce("props.inhand", labPropFar == 0, labPropSamples + " samples of items on other players' bodies, " + labPropFar + " more than 0.3 m from a hand; worst " + labPropWorst + " at " + labPropWorstD.ToString("0.00") + " m");
        }
    }
}
