using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Scene 3's fall: Mita walks the Main Character to the toilet (Location3WalkToToilet), a script that runs on that PC alone, and everyone else was
    // left behind. They are led along it in a line, each PC moving its own player, and every PC shows the bodies it draws hand in hand.
    public sealed partial class TogetherRuntime
    {
        const float HandGap = 0.9f;
        readonly List<Vector3> handTrail = new List<Vector3>();
        Location3WalkToToilet handWalk;
        float handWalkFindAt;
        int handLeader = -1;
        bool handTold;

        Vector3 handWalkerAt;
        bool handWalkerKnown;
        public static void NoteHandWalk() { TogetherRuntime rt = Instance; if (rt != null && rt.Connected && !NotMyMoment && rt.LocalPlayer != null) { rt.handWalkerAt = rt.LocalPlayer.transform.position; rt.handWalkerKnown = true; } }

        void HandHoldTick()
        {
            float now = Time.realtimeSinceStartup;
            if (handWalk == null && now >= handWalkFindAt && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Scene 3 - WeTogether")
            {
                handWalkFindAt = now + 1f;
                foreach (Location3WalkToToilet w in Object.FindObjectsOfType<Location3WalkToToilet>(true)) if (w != null) handWalk = w;
            }
            PlayerMove player = LocalPlayer;
            if (handWalk == null || player == null || !handWalk.gameObject.activeInHierarchy) { handTold = false; handWalkerKnown = false; handTrail.Clear(); return; }
            if (!NotMyMoment && !handTold) { handTold = true; handLeader = SelfId; Call(30, handWalk, SelfId); }
            if (handLeader < 0) return;

            List<int> chain = HandChain();
            if (handLeader != SelfId) FollowLeader(player, chain.IndexOf(SelfId));
        }

        List<int> HandChain()
        {
            List<int> chain = new List<int> { handLeader };
            List<int> rest = new List<int>(peers.Keys) { SelfId };
            rest.Sort();
            foreach (int id in rest) if (id != handLeader) chain.Add(id);
            return chain;
        }

        // After the streamed bones are on the copies. The player's own arms are set by the game's IK and posers, and the copies have neither.
        void HandHoldPose()
        {
            if (handWalk == null || handLeader < 0 || !handWalk.gameObject.activeInHierarchy) return;
            List<int> chain = HandChain();
            for (int i = 0; i < chain.Count; i++)
            {
                if (i > 0) Reach(chain[i], "Left", chain[i - 1], "Right");
                if (i < chain.Count - 1) Reach(chain[i], "Right", chain[i + 1], "Left");   // the left hand may hold the pills
            }
        }

        Transform HandBody(int id)
        {
            Peer peer;
            if (id == SelfId) return LocalPlayer != null && LocalPlayer.animPerson != null ? LocalPlayer.animPerson.transform : null;
            return peers.TryGetValue(id, out peer) && peer.Avatar != null && peer.Avatar.Root != null ? peer.Avatar.Root.transform : null;
        }

        void FollowLeader(PlayerMove player, int rank)
        {
            Peer leader;
            if (!peers.TryGetValue(handLeader, out leader)) return;
            if (handTrail.Count == 0 || (leader.Feet - handTrail[handTrail.Count - 1]).sqrMagnitude > 0.01f) handTrail.Add(leader.Feet);
            float left = HandGap * rank;
            Vector3 spot = handTrail[0];
            for (int i = handTrail.Count - 1; i > 0; i--)
            {
                float step = Vector3.Distance(handTrail[i], handTrail[i - 1]);
                if (step >= left) { spot = Vector3.Lerp(handTrail[i], handTrail[i - 1], left / step); break; }
                left -= step;
            }
            if (LocalFree(player)) player.transform.position = spot;
        }

        void Reach(int id, string side, int partnerId, string partnerSide)
        {
            if (id == SelfId) return;
            Transform body = HandBody(id), partnerBody = HandBody(partnerId);
            if (body == null || partnerBody == null) return;
            Transform arm = HandBone(body, side + " arm"), elbow = HandBone(body, side + " elbow"), wrist = HandBone(body, side + " wrist"), partner = HandBone(partnerBody, partnerSide + " arm");
            if (arm == null || elbow == null || wrist == null || partner == null) return;
            // On the Main Character's PC the walk writes the player's place in its own LateUpdate; until then the transform is a frame's physics behind, metres off.
            Vector3 partnerAt = partnerId == SelfId ? (handWalkerKnown ? handWalkerAt : LocalPlayer.transform.position) + Vector3.up * 1.25f : partner.position;
            Vector3 hold = (arm.position + partnerAt) * 0.5f;
            arm.rotation = Quaternion.FromToRotation(elbow.position - arm.position, hold - arm.position) * arm.rotation;
            elbow.rotation = Quaternion.FromToRotation(wrist.position - elbow.position, hold - elbow.position) * elbow.rotation;
        }

        public static Transform HandBone(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
    }
}
