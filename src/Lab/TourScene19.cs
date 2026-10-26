using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "scene19" (2 players): Scene 19 (Glasses) cannot be started cold, but the story reaches it from
    // Scene 18's own Scene_Load (StartLoad, then GoScene). The host fires that loader; the relays must carry the client
    // the same way, at the same time.
    public sealed partial class TogetherRuntime
    {
        bool labScene19Fired, labScene18Fired, labScene19Noted;
        Scene_Load labGo; float labGoAt;

        void Scene19Host(float now, string scene)
        {
            // Start at Scene 17 and let the game's own loaders carry everyone 17 -> 18 -> 19, as a play-through does.
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; Log("Lab: host loading Scene 17 - Dreamer"); LoadThroughGame("Scene 17 - Dreamer"); }
            if (scene == "Scene 17 - Dreamer" && !labScene18Fired && now - tourSceneAt > 25)
            {
                labScene18Fired = true;
                Scene_Load next = null;
                foreach (Scene_Load l in Object.FindObjectsOfType<Scene_Load>(true)) if (l != null && (l.nameSceneLoad ?? "").StartsWith("Scene 18", StringComparison.Ordinal)) next = l;
                if (next == null) { CheckOnce("scene19.loader", false, "no Scene 18 loader in Scene 17"); return; }
                Log("Lab: host fires Scene 17's loader to " + next.nameSceneLoad);
                next.StartLoad();
                labGo = next; labGoAt = now + 8;
            }
            if (scene == "Scene 18 - 2D" && !labScene19Fired && now - tourSceneAt > 25)
            {
                labScene19Fired = true;
                Scene_Load load = null;
                foreach (Scene_Load l in Object.FindObjectsOfType<Scene_Load>(true)) if (l != null && (l.nameSceneLoad ?? "").StartsWith("Scene 19", StringComparison.Ordinal)) load = l;
                if (load == null) { CheckOnce("scene19.loader", false, "no Scene 19 loader in Scene 18"); return; }
                Log("Lab: host fires Scene 18's loader to " + load.nameSceneLoad + " (save " + load.fileSave + ", levels " + (load.nameLevelSaves != null ? string.Join(",", load.nameLevelSaves) : "-") + ")");
                load.StartLoad();
                labGo = load; labGoAt = now + 8;
            }
            if (labGo != null && now > labGoAt) { Scene_Load go = labGo; labGo = null; Log("Lab: host switches to the loaded chapter"); go.GoScene(); }
            Scene19Checks(scene);
        }

        bool AllPeersReady() { foreach (Peer p in peers.Values) if (p.Accepted && !barrierReady.Contains(p.Id)) return false; return peers.Count > 0; }

        void Scene19Checks(string scene)
        {
            // What multiplayer owns: everyone moves to Scene 19 together through the game's own loader. (Started from a
            // cold Scene 18, the game's World then fails to set up on every machine alike, the host included; in a
            // real play-through Scene 18 is reached from 17 with that state.)
            // Informational: reached by firing the loaders directly (not by playing Scenes 17 and 18 to their ends), the
            // game's World fails to set up Scene 19 even single-player (-Players 1 control run). Ruled out as causes:
            // GoScene vs GoSceneAfterPause, saving first (Scene_Load.SaveGame), the GlobalGame Mad Mita textures and
            // screen capture. So this is a note, not a check.
            if (scene.StartsWith("Scene 19", StringComparison.Ordinal) && Time.realtimeSinceStartup - tourSceneAt > 20 && !labScene19Noted)
            {
                labScene19Noted = true;
                Log("Lab: Scene 19 player " + (LocalPlayer != null ? "up" : "missing") + ", " + errorCount + " game errors (game-side when the loaders are fired directly)");
                // What World.Start works with: which references are empty here.
                foreach (World w in Object.FindObjectsOfType<World>(true))
                {
                    if (w == null) continue;
                    Log("Lab: World " + SceneKeys.Key(w) + " active=" + w.gameObject.activeInHierarchy + " scrpm=" + (w.scrpm != null) + " scrgc=" + (w.scrgc != null)
                        + " levelLoad=" + (w.levelLoad != null ? w.levelLoad.Length : -1) + " (GlobalGame.levelLoad " + GlobalGame.levelLoad + ") cutsceneStart=" + (w.cutsceneStart != null)
                        + " tagObjects=" + (w.tagObjects != null ? w.tagObjects.Length : -1) + " languageTexture=" + (w.languageTexture != null));
                }
                Log("Lab: PlayerMove objects " + Object.FindObjectsOfType<PlayerMove>(true).Length + ", GameController objects " + Object.FindObjectsOfType<GameController>(true).Length
                    + ", WorldPlayer " + Object.FindObjectsOfType<WorldPlayer>(true).Length + ", loaded scenes " + UnityEngine.SceneManagement.SceneManager.sceneCount);
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) Log("Lab: scene " + UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name);
                foreach (PlayerMove pm in Object.FindObjectsOfType<PlayerMove>(true))
                {
                    string chain = "";
                    for (Transform t = pm.transform; t != null; t = t.parent) chain = t.name + (t.gameObject.activeSelf ? "" : "(off)") + "/" + chain;
                    Log("Lab: player " + chain + " tag=" + pm.gameObject.tag + " enabled=" + pm.enabled);
                }
            }
            if (scene.StartsWith("Scene 19", StringComparison.Ordinal))
            {
                // From the scene handshake (poses carry no scene while there is no player object).
                bool together = Hosting ? AllPeersReady() : wantedScene == scene;
                if (together) CheckOnce("scene19.followed", true, "everyone moved to " + scene + " through Scene 18's loader");
            }
        }
    }
}
