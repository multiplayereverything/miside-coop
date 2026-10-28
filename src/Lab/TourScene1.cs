using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "scene1" (2 players): Scene 1 played the way a player does, with screenshots from both copies at each step
    // (LabShots.cs), to compare what each player sees. The host answers the tutorial, picks up the phone, presses the
    // phone's key prompts as they appear (Interface_KeyHint_Key.KeyDown: open message, download, play), and in the
    // phone app reads on through Mita's dialogue (Tamagotchi_Dialogue.NextDialogue) and types the name. Both copies log
    // what they show every 10 s: the real room, the toy house, the app, the dialogue line.
    public sealed partial class TogetherRuntime
    {
        float labS1StepAt, labS1ShotAt, labS1StateAt;
        int labS1Shots;
        bool labS1Took;

        void Scene1State(float now)
        {
            if (now < labS1StateAt) return;
            labS1StateAt = now + 10;
            GameObject real = SceneObject("World RealRoom"), house = SceneObject("World TamagotchiHouse");
            Tamagotchi_Main app = Object.FindObjectOfType<Tamagotchi_Main>();
            Tamagotchi_Dialogue d = AppDialogue();
            Camera cam = Camera.main;
            Log("Lab: scene1 real=" + (real != null && real.activeInHierarchy) + " house=" + (house != null && house.activeInHierarchy)
                + " app=" + (app != null && app.play) + " minigame=" + (app != null && app.playMinigame)
                + " dialogue=" + (d != null ? (d.dialogueRun != null ? d.dialogueRun.name : "?") + "#" + d.dialogueIndex + " '" + d.stringDialogueNow + "'" : "-")
                + " camera=" + (cam != null ? cam.name + " at " + cam.transform.position : "none") + " player at " + (LocalPlayer != null ? LocalPlayer.transform.position.ToString() : "-")
                + " watching=" + OwningOrWatching());
        }

        string OwningOrWatching() { foreach (MinigameRun r in minigames.Values) return r.Owner == SelfId ? "playing" : "watching " + r.Owner; return "-"; }

        void Scene1Host(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 1 - RealRoom"); }
            // In the phone app the player object is switched off (the camera is the app's), so only the steps before need it.
            Tamagotchi_Main app = Object.FindObjectOfType<Tamagotchi_Main>();
            bool inApp = app != null && app.play;
            if (scene != "Scene 1 - RealRoom" || (LocalPlayer == null && !inApp)) return;
            LabScene1Tutorial(now);   // also types the name in the app, where there is no player object
            Scene1State(now);
            if (now - tourSceneAt > 220) CheckOnce("scene1.done", true, "played Scene 1 for 220 s (" + labS1Shots + " screenshots)");
            if (now - tourSceneAt > 20 && now >= labS1ShotAt) { labS1ShotAt = now + 15; LabShotAll("s1-" + (labS1Shots++).ToString("00")); }
            if (now < labS1StepAt || now - tourSceneAt < 25) return;
            labS1StepAt = now + 2;
            if (!labS1Took)
            {
                foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                    if (o != null && o.name == "Interactive Mobile" && o.active) { labS1Took = true; Log("Lab: scene1 host picks up the phone"); o.Click(); return; }
                return;
            }
            if (LocalPlayer != null && LocalPlayer.animationRun && !LocalPlayer.animationLoopRun) return;   // wait for the animation's loop (the prompt)
            foreach (Interface_KeyHint_Key k in Object.FindObjectsOfType<Interface_KeyHint_Key>())
                if (k != null && k.isActiveAndEnabled && PathOf(k.transform).Contains("Smartphone"))
                {
                    Log("Lab: scene1 host presses the prompt " + k.name);
                    k.KeyDown();
                    labS1StepAt = now + 4;
                    return;
                }
            // Day 1's task: tidy up. Click a piece ("Trash N", TamagotchiGame_HelpTrash.TakeTrash), then the basket (its
            // Trigger_MouseClick calls ClickBasket, which drops what is held).
            TamagotchiGame_HelpTrash tidy = Object.FindObjectOfType<TamagotchiGame_HelpTrash>();
            if (tidy != null && tidy.trashTake != null)
                foreach (Trigger_MouseClick c in Object.FindObjectsOfType<Trigger_MouseClick>())
                    if (c != null && c.isActiveAndEnabled && c.name == "Basket") { Log("Lab: scene1 host drops " + tidy.trashTake.name + " in the basket"); c.Click(); return; }
            foreach (Trigger_MouseClick c in Object.FindObjectsOfType<Trigger_MouseClick>())
                if (c != null && c.isActiveAndEnabled && c.name.StartsWith("Trash") && PathOf(c.transform).Contains("Quest 3"))
                {
                    Log("Lab: scene1 host tidies up " + c.name);
                    c.Click();
                    return;
                }
            Tamagotchi_Dialogue d = AppDialogue();
            if (d != null && !d.play) { Log("Lab: scene1 host reads on (Space): '" + d.stringDialogueNow + "'"); d.KeyNextDialogue(); labS1StepAt = now + 3; }
        }

        void Scene1Client(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (scene != "Scene 1 - RealRoom") return;
            LabScene1Tutorial(now);   // also types the name in the app, where there is no player object
            Scene1State(now);
        }
    }
}
