using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "ownerleaves" (2 players, Scene 4): the client is the Main Character and plays the card game, the host
    // watches. The client then leaves the game. The card game must end for the host, as if the player had finished.
    //   ownerleaves.watched@host: the host watched the client's card game
    //   ownerleaves.ended@host:   after the client left, the game's end ran on the host and nobody plays it
    public sealed partial class TogetherRuntime
    {
        float labOwnerPressAt = -1, labOwnerStartAt = -1, labOwnerGoneAt = -1;
        bool labOwnerWatched;

        void OwnerLeavesTour(float now, string scene)
        {
            if (Hosting)
            {
                if (!tourStarted && AcceptedCount > 0 && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame(TourChapter); return; }
                if (scene != TourChapter || LocalPlayer == null) return;
                if (AcceptedCount > 0) { foreach (MinigameRun r in minigames.Values) if (r.Owner != 0) labOwnerWatched = true; return; }
                if (!labOwnerWatched && now - tourSceneAt < 90) return;
                if (labOwnerGoneAt < 0) labOwnerGoneAt = now;
                if (now - labOwnerGoneAt < 3) return;
                CheckOnce("ownerleaves.watched", labOwnerWatched, labOwnerWatched ? "the client played, the host watched" : "the client never started the card game");
                bool ended = minigames.Count == 0 && minigameEndsApplied > 0;
                CheckOnce("ownerleaves.ended", ended, ended ? "the card game's end ran here (" + minigameEndsApplied + ")" : "the card game still belongs to a player who left (" + minigames.Count + " running, " + minigameEndsApplied + " ended)");
                return;
            }
            PlayerMove player = LocalPlayer;
            if (scene == "SceneMenu" || !scene.StartsWith("Scene ", StringComparison.Ordinal) || player == null || labOwnerGoneAt > 0) return;
            // A live minigame is the Main Character's. Nothing in Scene 4's opening can be pressed, so the tour makes the
            // moment the client's by hand; what is checked is the host's game after the client has gone.
            if (labOwnerPressAt < 0)
            {
                if (now - tourSceneAt < 25 || !LocalFree(player)) return;
                labOwnerPressAt = now; momentMine = true;
                return;
            }
            if (labOwnerStartAt < 0)
            {
                if (now - labOwnerPressAt < 1) return;
                labOwnerStartAt = now;
                Location4TableCardGame cards = Object.FindObjectOfType<Location4TableCardGame>(true);
                Log("Lab: client starts the card game, its moment: " + momentMine);
                try { if (cards != null) cards.StartGame(); } catch (Exception ex) { Log("Lab: StartGame threw: " + ex.Message); }
                return;
            }
            if (now - labOwnerStartAt < 8) return;
            labOwnerGoneAt = now;
            GameController gc = Object.FindObjectOfType<GameController>();
            Log("Lab: client leaves while playing, owning: " + OwningMinigame);
            if (gc != null) gc.ExitGame();
        }
    }
}
