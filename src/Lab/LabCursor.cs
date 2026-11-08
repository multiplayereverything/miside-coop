using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // User report (2026-11-08): the other players "dont see the cursor" in the phone's minigames, and not after them either.
    // MT-CURSOR: every change of this PC's cursor, with what the app is doing then.
    // app.cursor: 3 s after each minigame closes, with the app still up, this PC shows a cursor. Holding the cooking knife hides
    // it (TamagotchiGame_Cooking.CutHold); letting go only shows it again while canCut is set, and the last Cut clears that.
    public sealed partial class TogetherRuntime
    {
        string labCursor = "";
        bool labCursorInGame;
        float labCursorEndedAt = -1;
        int labCursorSamples, labCursorHidden;

        void LabCursorTrace()
        {
            Tamagotchi_Main app = Object.FindObjectOfType<Tamagotchi_Main>();
            bool game = app != null && app.playMinigame;
            if (labCursorInGame && !game && app != null && app.play) labCursorEndedAt = Time.realtimeSinceStartup;
            labCursorInGame = game;
            if (labCursorEndedAt >= 0 && Time.realtimeSinceStartup - labCursorEndedAt > 3)
            {
                labCursorEndedAt = -1;
                if (app != null && app.play && !game) { labCursorSamples++; if (!Cursor.visible) labCursorHidden++; }
            }
            string now = "visible=" + Cursor.visible + " lock=" + Cursor.lockState + " app=" + (app != null && app.play) + " minigame=" + (game ? (app.minigameNow != null ? app.minigameNow.name : "?") : "-");
            if (now == labCursor) return;
            labCursor = now;
            Log("MT-CURSOR " + now);
        }

        void LabCursorCheck() { if (labCursorSamples > 0) CheckOnce("app.cursor", labCursorHidden == 0, labCursorHidden + " of " + labCursorSamples + " minigame ends left this PC's cursor hidden"); }
    }
}
