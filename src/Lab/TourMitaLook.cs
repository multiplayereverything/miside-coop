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
        bool labMitaYou;

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
                if (t < 20) labMitaLook.LookOnObject(other.Head);
                else if (t < 45) labMitaLook.LookOnObject(own);
                return;
            }
            string seen = (labMitaLook.lookObject == null ? "nothing" : labMitaLook.lookObject.name) + " / eyes " + (labMitaLook.lookObjectEyes == null ? "nothing" : labMitaLook.lookObjectEyes.name);
            // The two PCs start this a few seconds apart, so each target is watched for, up to a limit.
            if (t >= 3 && labMitaLook.lookObject == own && labMitaLook.lookObjectEyes == own) { CheckOnce("mitalook.you", true, seen); labMitaYou = true; }
            if (t >= 16 && !labMitaYou) CheckOnce("mitalook.you", false, seen);
            if (labMitaYou && labMitaLook.lookObject == other.Head && labMitaLook.lookObjectEyes == other.Head) CheckOnce("mitalook.host", true, seen);
            if (t >= 40) CheckOnce("mitalook.host", false, seen);
        }
    }
}
