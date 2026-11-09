using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // User report (2026-11-09): "sync mita, at which player she looking". Each PC's Character_Look writes its own camera into lookObject
    // and lookObjectEyes, and picks glances by its own random numbers, so she looked at a different player on each PC.
    // Lab tour "mitalook" (Scene 4): the host's Mita looks at the client, then at the host, and the client's Mita must look at the same player.
    //   mitalook.you: (client) she looks at this PC's camera while the host's looks at this player
    //   mitalook.host: (client) she looks at the host's body while the host's looks at the host
    public sealed partial class TogetherRuntime
    {
        Character_Look labMitaLook;
        float labMitaLookAt = -1;

        void MitaLookTour(float now, string scene)
        {
            if (scene != "Scene 4 - StartSecret" || LocalPlayer == null || now - tourSceneAt < 20 || GlobalTag.cameraPlayer == null) return;
            if (labMitaLook == null)
                foreach (Character_Look l in Object.FindObjectsOfType<Character_Look>(true))
                    if (l != null && l.name == "IKLifeCharacter" && l.gameObject.activeInHierarchy) labMitaLook = l;
            RemoteAvatar other = null;
            foreach (Peer p in peers.Values) if (p.Avatar != null && p.Avatar.Head != null) other = p.Avatar;
            if (labMitaLook == null || other == null) return;
            if (labMitaLookAt < 0) { labMitaLookAt = now; Log("Lab: Mita look test starts"); }
            float t = now - labMitaLookAt;
            Transform own = GlobalTag.cameraPlayer.transform;
            if (!IsClient)
            {
                if (t < 15) labMitaLook.LookOnObject(other.Head);
                else if (t < 40) labMitaLook.LookOnObject(own);
                return;
            }
            string seen = (labMitaLook.lookObject == null ? "nothing" : labMitaLook.lookObject.name) + " / eyes " + (labMitaLook.lookObjectEyes == null ? "nothing" : labMitaLook.lookObjectEyes.name);
            if (t >= 11) CheckOnce("mitalook.you", labMitaLook.lookObject == own && labMitaLook.lookObjectEyes == own, seen);
            if (t >= 30) CheckOnce("mitalook.host", labMitaLook.lookObject == other.Head && labMitaLook.lookObjectEyes == other.Head, seen);
        }
    }
}
