using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "pressrace" (2 players, Scene 2, 300 ms of lag each way): both players press the same object before either
    // hears of the other's press. The host's press is first on the log, so the host is the Main Character.
    //   pressrace.hostplays: the host's player performed the press
    //   pressrace.onemain:   the client's player did not perform it too
    public sealed partial class TogetherRuntime
    {
        string labRaceKey;
        float labRaceDue = -1, labRacePressedAt = -1;
        bool labRaceActed;

        void PressRaceTour(float now, string scene)
        {
            if (!Connected || (Hosting && AcceptedCount == 0)) return;
            if (Hosting && !tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame(TourChapter); return; }
            PlayerMove player = LocalPlayer;
            if (scene != TourChapter || player == null) return;
            if (Hosting && labRaceKey == null && now - tourSceneAt > 30 && LocalFree(player))
            {
                string first = null;
                foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                {
                    if (o == null || !o.active || !o.isActiveAndEnabled) continue;
                    string key = SceneKeys.Key(o);
                    if (first == null || string.CompareOrdinal(key, first) < 0) first = key;
                }
                if (first == null) return;
                // The client presses when this arrives (300 ms); the host presses 150 ms after that, before the
                // client's request can reach it (600 ms).
                labRaceKey = first; labRaceDue = now + 0.45f;
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-race:" + first)));
            }
            if (labRaceKey == null) return;
            if (labRacePressedAt < 0)
            {
                if (now < labRaceDue || !LabPress(SceneKeys.Find<ObjectInteractive>(labRaceKey))) return;
                labRacePressedAt = now;
                Log("Lab: race press on " + labRaceKey);
            }
            if (player.animationRun) labRaceActed = true;
            if (now - labRacePressedAt < 8) return;
            if (Hosting) CheckOnce("pressrace.hostplays", labRaceActed, labRaceActed ? "the host's player performed the press" : "the host's player never moved: this object can't show who plays");
            else CheckOnce("pressrace.onemain", !labRaceActed, labRaceActed ? "this player performed the host's press too" : "this player watched");
        }

        void PressRaceNotice(string notice) { labRaceKey = notice.Substring(9); labRaceDue = 0; }
    }
}
