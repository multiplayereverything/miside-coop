using UnityEngine;

namespace MiSideTogether
{
    // Found in the Scene 20 screenshots (2026-11-09): the host died in the corridor run and went back to the checkpoint, the
    // client stayed at the far end in the dark. death.regroup: 12 s after a death on any PC, this player is within 15 m of
    // the other player. Logged as MT-REGROUP.
    public sealed partial class TogetherRuntime
    {
        int labDeathsSeen;
        float labRegroupAt = -1;

        void LabDeathRegroupTick(float now)
        {
            int deaths = deathsSent + deathsApplied;
            if (deaths != labDeathsSeen) { labDeathsSeen = deaths; labRegroupAt = now; }
            if (labRegroupAt < 0 || now - labRegroupAt < 12 || LocalPlayer == null) return;
            labRegroupAt = -1;
            foreach (Peer p in peers.Values)
            {
                if (!p.HasPose) continue;
                float d = (LocalPlayer.transform.position - p.Feet).magnitude;
                Log("MT-REGROUP " + d.ToString("0.0") + " m from " + p.Name + " after the death");
                LabShot("regroup-" + labDeathsSeen + "-" + Name);
                CheckOnce("death.regroup", d < 15, d.ToString("0.0") + " m from " + p.Name + " 12 s after a death");
            }
        }
    }
}
