using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // User report (2026-11-09): "when its getting glitchy, make other players hold the main player hold the hands". Scene 3's Quest 6: the camera
    // glitches, the player falls, Mita walks them to the toilet (Location3WalkToToilet), on the Main Character's PC alone.
    // Lab tour "handhold" (Scene 3): the game's own walk, held the way the Up key holds it, with the client standing somewhere else.
    //   handhold.led: (client) this player is led along behind the Main Character, who has walked at least 1.5 m
    //   handhold.hand: (both) the body this PC draws has its hand at this player's shoulder, the side it takes
    public sealed partial class TogetherRuntime
    {
        Location3WalkToToilet labHandWalk;
        Vector3 labHandStart;
        float labHandAt = -1, labHandShotAt = -1;

        void HandHoldTour(float now, string scene)
        {
            if (scene != "Scene 3 - WeTogether" || LocalPlayer == null || now - tourSceneAt < 20) return;
            RemoteAvatar other = null; Peer peer = null;
            foreach (Peer p in peers.Values) if (p.Avatar != null && p.Avatar.Root != null) { other = p.Avatar; peer = p; }
            if (other == null) return;
            if (labHandAt < 0)
            {
                foreach (Location3WalkToToilet w in Object.FindObjectsOfType<Location3WalkToToilet>(true)) if (w != null) labHandWalk = w;
                if (labHandWalk == null) { CheckOnce("handhold.led", false, "no walk in " + scene); return; }
                labHandStart = IsClient ? peer.Feet : LocalPlayer.transform.position;
                if (IsClient) LocalPlayer.TeleportPlayer(labHandStart + new Vector3(0, 0, 3));
                for (Transform t = labHandWalk.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                labHandAt = now;
                Log("Lab: the walk to the toilet is on");
                return;
            }
            float at = now - labHandAt;
            if (!IsClient && labHandWalk != null) labHandWalk.timeWalk = 0.5f;
            Vector3 leader = IsClient ? peer.Feet : LocalPlayer.transform.position;
            float walked = (leader - labHandStart).magnitude;
            if (at < 3) return;
            float gap = (LocalPlayer.transform.position - peer.Feet).magnitude;
            Transform me = HandBone(LocalPlayer.animPerson.transform, IsClient ? "Left arm" : "Right arm");
            Transform them = HandBone(other.Root.transform, IsClient ? "Right wrist" : "Left wrist");
            float hands = me != null && them != null ? (me.position - them.position).magnitude : 99;
            if (IsClient && gap <= 1.6f && walked >= 1.5f) CheckOnce("handhold.led", true, gap.ToString("0.00") + " m behind, the host walked " + walked.ToString("0.0") + " m");
            if (hands <= 0.7f && labHandWalk != null && labHandWalk.gameObject.activeInHierarchy) { if (labHandShotAt < 0) labHandShotAt = now + 1f; CheckOnce("handhold.hand", true, hands.ToString("0.00") + " m from their hand to this shoulder"); }
            if (IsClient && labHandShotAt > 0 && Camera.main != null) Camera.main.transform.LookAt(other.Root.transform.position + Vector3.up * 1.1f);
            if (labHandShotAt > 0 && now >= labHandShotAt) { labHandShotAt = 0; LabShot("handhold-" + (IsClient ? "client" : "host")); }
            if (at > 40) { if (IsClient) CheckOnce("handhold.led", false, gap.ToString("0.00") + " m away"); CheckOnce("handhold.hand", false, hands.ToString("0.00") + " m from their hand to this shoulder"); }
        }
    }
}
