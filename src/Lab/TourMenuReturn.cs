using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "menureturn" (2 players, Scene 2): the host goes back to the main menu the game's way (pause menu, main
    // menu: GameController.ExitGame); the client follows. Both take a screenshot there (playtest: "I see my hands in the
    // main menu"). Both also log whether a player object is still active in the menu.
    //   menu.back@host    the host reached the main menu
    //   menu.clean        the client reached the main menu with no player object active (no hands)
    public sealed partial class TogetherRuntime
    {
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
