using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "legs" (2 players, Scene 5): Location5_MitaLegs measures the distance to this PC's own player and, once, fires
    // eventStartPlayer, which puts the basement key out. One player walks up to it, the other stays away.
    //   legs.shared: (host) with only the client near Mita's legs, the key is out on the host too
    public sealed partial class TogetherRuntime
    {
        Location5_MitaLegs labLegs;
        float labLegsAt = -1;
        bool labLegsMoved;

        void LegsTour(float now, string scene)
        {
            if (scene != "Scene 5 - StartHorror" || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (labLegs == null)
            {
                labLegs = Object.FindObjectOfType<Location5_MitaLegs>(true);
                if (labLegs == null) { CheckOnce("legs.shared", false, "no Location5_MitaLegs in " + scene); return; }
                for (Transform t = labLegs.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                labLegsAt = now;
                Log("Lab: Mita's legs at " + labLegs.transform.position + ", this player " + (LocalPlayer.transform.position - labLegs.transform.position).magnitude.ToString("0.0") + " m away");
                return;
            }
            if (IsClient)
            {
                if (labLegsMoved || now - labLegsAt < 3) return;
                labLegsMoved = true;
                UnityEngine.AI.NavMeshHit hit;
                Vector3 to = labLegs.transform.position;
                if (UnityEngine.AI.NavMesh.SamplePosition(to, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) to = hit.position;
                LocalPlayer.TeleportPlayer(to);
                Log("Lab: client walks up to Mita's legs at " + to);
                return;
            }
            if (now - labLegsAt < 14) return;
            Transform key = labLegs.transform.parent != null ? labLegs.transform.parent.Find("ItemTake Key") : null;
            CheckOnce("legs.shared", key != null && key.gameObject.activeSelf, "key " + (key == null ? "not found" : key.gameObject.activeSelf ? "out" : "not out") + " on the host, this player " + (LocalPlayer.transform.position - labLegs.transform.position).magnitude.ToString("0.0") + " m away");
        }
    }
}
