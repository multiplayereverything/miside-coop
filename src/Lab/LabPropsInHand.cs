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
        int labPropSamples, labPropFar;
        string labPropWorst = "";
        float labPropWorstD;

        void LabPropsInHandTick(float now)
        {
            if (now < labPropAt) return;
            labPropAt = now + 2;
            // Objects the game magnets to a player (Scene 1's phone), placed here on that player's body by the carry stream.
            foreach (KeyValuePair<string, Carried> pair in carried)
            {
                Peer owner;
                if (pair.Value.Magnet == null || !peers.TryGetValue(pair.Value.Owner, out owner) || owner.Avatar == null || owner.Avatar.Layout == null) continue;
                Transform item = Moved(pair.Value.Magnet);
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

        void LabPropsInHandCheck()
        {
            if (labPropSamples > 0) CheckOnce("props.inhand", labPropFar == 0, labPropSamples + " samples of items on other players' bodies, " + labPropFar + " more than 0.3 m from a hand; worst " + labPropWorst + " at " + labPropWorstD.ToString("0.00") + " m");
        }
    }
}
