using UnityEngine;

namespace MiSideTogether
{
    // Motion-sensor lamps (LampBox: Scene 6's basement camera room). The game's CheckBox (disassembly) lights the lamp when
    // an OverlapBox finds this PC's own player (or one of its otherTargets): eventOn if it was off, then timeOffNow =
    // timeOff, counted down in Update to eventOff. The other players' bodies here have no colliders, so a lamp stayed
    // dark on every PC but the one whose player stood in it. Before its Update, any other player standing in the box
    // counts the same way. Each PC decides from the same player positions, so the lamp's events stay local.
    public sealed partial class TogetherRuntime
    {
        public static void LampSeesPlayers(LampBox lamp)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || lamp == null) return;
            Vector3 center = lamp.transform.TransformPoint(lamp.center);
            Quaternion toLocal = Quaternion.Inverse(lamp.transform.rotation);
            Vector3 half = new Vector3(Mathf.Abs(lamp.size.x), Mathf.Abs(lamp.size.y), Mathf.Abs(lamp.size.z));
            foreach (Peer p in rt.peers.Values)
            {
                if (!p.HasPose || p.SceneHash != LocalSceneHash) continue;
                // Feet and chest, so a low or high box still sees the body.
                if (!InBox(toLocal * (p.Feet - center), half) && !InBox(toLocal * (p.Feet + Vector3.up - center), half)) continue;
                if (lamp.timeOffNow <= 0 && lamp.eventOn != null) lamp.eventOn.Invoke();
                lamp.timeOffNow = lamp.timeOff;
                return;
            }
        }

        static bool InBox(Vector3 local, Vector3 half) { return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z; }
    }
}
