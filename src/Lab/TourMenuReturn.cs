using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "menureturn" (2 players, Scene 2): the host goes back to the main menu the game's way (pause menu, main
    // menu: GameController.ExitGame); the client follows. Both take a screenshot there (playtest: "I see my hands in the
    // main menu"). Both also log whether a player object is still active in the menu.
    //   menu.back@host    the host reached the main menu
    //   menu.clean        the client reached the main menu with no player object active (no hands)
    // Lab tour "menuleave" (2 players, Scene 2): the client goes back to the main menu by itself (GameController.ExitGame)
    // while the host stays in the chapter; it must leave the game, not be pulled back in.
    //   leave.left        the client is in the menu and no longer connected
    //   leave.seen@host   the host no longer has the client
    public sealed partial class TogetherRuntime
    {
        float labLeaveAt = -1;

        void MenuLeaveTick(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (Hosting)
            {
                if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 2 - InGame"); return; }
                if (labLeaveAt < 0 && AcceptedCount > 0 && scene == "Scene 2 - InGame") labLeaveAt = now;
                if (labLeaveAt > 0 && AcceptedCount == 0) CheckOnce("leave.seen", true, "the client left; host still in " + scene);
                return;
            }
            if (scene == "Scene 2 - InGame" && LocalPlayer != null && now - tourSceneAt > 25 && labLeaveAt < 0)
            {
                labLeaveAt = now;
                GameController gc = Object.FindObjectOfType<GameController>();
                Log("Lab: client goes back to the main menu by itself");
                if (gc != null) gc.ExitGame();
                return;
            }
            if (labLeaveAt > 0 && scene == "SceneMenu" && now - labLeaveAt > 15)
                CheckOnce("leave.left", Network == null, Network == null ? "in the main menu, not connected" : "still connected (pulled back?)");
        }

        float labMenuBackAt = -1;
        bool labMenuShotTaken, labMenuSawChapter;

        static PlayerMove ActivePlayerObject()
        {
            foreach (PlayerMove p in Object.FindObjectsOfType<PlayerMove>()) if (p != null && p.isActiveAndEnabled) return p;
            return null;
        }

        void MenuReturnTick(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (Hosting && !tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 2 - InGame"); return; }
            if (Hosting && scene == "Scene 2 - InGame" && LocalPlayer != null && now - tourSceneAt > 30 && labMenuBackAt < 0)
            {
                GameController gc = Object.FindObjectOfType<GameController>();
                labMenuBackAt = now;
                Log("Lab: host goes back to the main menu (" + (gc != null ? "GameController.ExitGame" : "no GameController") + ")");
                if (gc != null) gc.ExitGame(); else LoadThroughGame("SceneMenu");
                return;
            }
            if (scene == "Scene 2 - InGame") labMenuSawChapter = true;
            if (scene != "SceneMenu" || !labMenuSawChapter || now - tourSceneAt < 8 || labMenuShotTaken) return;
            labMenuShotTaken = true;
            PlayerMove p = ActivePlayerObject();
            LabShot("menu-back-" + Name);
            Log("Lab: back in the menu; active player object: " + (p != null ? PathOf(p.transform) : "none"));
            if (Hosting) CheckOnce("menu.back", true, "host in the main menu");
            else CheckOnce("menu.clean", p == null, p != null ? "a player object is still active: " + PathOf(p.transform) : "no player object active");
        }
    }
}
