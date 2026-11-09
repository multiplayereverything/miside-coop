using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "distance" (2 players, Scene 11 on the story log): one player stands in a distance trigger, the other is away.
    //   distance.quiet: (host) the trigger settles on "inside" and stays there (it flipped about 30 times a second)
    //   distance.left: (host) once the client has walked out, the trigger is "outside" here too
    public sealed partial class TogetherRuntime
    {
        Trigger_DistanceCircle labDistance;
        float labDistanceAt = -1;
        int labDistanceFlips, labDistanceStep;
        bool labDistanceWas;
        Vector3 labDistanceHome;

        void DistanceTour(float now, string scene)
        {
            if (scene != "Scene 11 - Backrooms" || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (labDistance == null)
            {
                foreach (Trigger_DistanceCircle t in Object.FindObjectsOfType<Trigger_DistanceCircle>(true)) if (t != null && t.name == "DistanceWork") { labDistance = t; break; }
                if (labDistance == null) { CheckOnce("distance.quiet", false, "no DistanceWork in " + scene); return; }
                for (Transform t = labDistance.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                labDistanceAt = now;
                labDistanceHome = LocalPlayer.transform.position;
                Log("Lab: distance trigger " + SceneKeys.Key(labDistance) + " radius " + labDistance.radius + ", this player " + (labDistanceHome - labDistance.transform.position).magnitude.ToString("0.0") + " m away");
                return;
            }
            float t2 = now - labDistanceAt;
            if (IsClient)
            {
                if (labDistanceStep == 0 && t2 >= 3)
                {
                    labDistanceStep = 1;
                    UnityEngine.AI.NavMeshHit hit;
                    Vector3 to = labDistance.transform.position;
                    if (UnityEngine.AI.NavMesh.SamplePosition(to, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) to = hit.position;
                    LocalPlayer.TeleportPlayer(to);
                    Log("Lab: client stands in the distance trigger at " + to);
                }
                if (labDistanceStep == 1 && t2 >= 22) { labDistanceStep = 2; LocalPlayer.TeleportPlayer(labDistanceHome); Log("Lab: client walked out of the distance trigger"); }
                return;
            }
            if (t2 < 8) { labDistanceWas = labDistance.targetInside; return; }
            if (t2 < 18) { if (labDistance.targetInside != labDistanceWas) { labDistanceWas = labDistance.targetInside; labDistanceFlips++; } return; }
            CheckOnce("distance.quiet", labDistanceFlips <= 1 && labDistance.targetInside, labDistanceFlips + " flips in 10 s; inside here " + labDistance.targetInside);
            if (t2 >= 34) CheckOnce("distance.left", !labDistance.targetInside, "inside here " + labDistance.targetInside + " after the client left");
        }
    }
}
