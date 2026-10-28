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
            if (scene == "SceneMenu" && !labS1MenuShot && now - tourSceneAt > 4) { labS1MenuShot = true; LabShotAll("menu"); }
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 1 - RealRoom"); }
            // In the phone app the player object is switched off (the camera is the app's), so only the steps before need it.
            Tamagotchi_Main app = Object.FindObjectOfType<Tamagotchi_Main>();
            bool inApp = app != null && app.play;
            if (scene != "Scene 1 - RealRoom" || (LocalPlayer == null && !inApp)) return;
            LabScene1Tutorial(now);   // also types the name in the app, where there is no player object
            Scene1State(now);
            if (now - tourSceneAt > 500) CheckOnce("scene1.done", true, "played Scene 1 for 500 s (" + labS1Shots + " screenshots)");
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
            if (Scene1Day2(now)) return;
            Tamagotchi_Dialogue d = AppDialogue();
            if (d != null && !d.play) { Log("Lab: scene1 host reads on (Space): '" + d.stringDialogueNow + "'"); d.KeyNextDialogue(); labS1StepAt = now + 3; }
        }

        bool labS1CutDown, labS1MenuShot;

        static ButtonMouseClick AppButton(string pathPart)
        {
            foreach (ButtonMouseClick b in Object.FindObjectsOfType<ButtonMouseClick>())
                if (b != null && b.isActiveAndEnabled && PathOf(b.transform).Contains(pathPart)) return b;
            return null;
        }

        void Press(ButtonMouseClick b, string what) { Log("Lab: scene1 host presses " + what); b.PointerDown(); b.PointerUp(); }

        // Day 2: Mita is hungry. Open the shop, buy food, give it to her, and cook it on the board (take a vegetable,
        // hold the knife and let go, until the board is done: TamagotchiGame_Cooking.ready).
        bool Scene1Day2(float now)
        {
            if (AppDialogue() != null) return false;
            TamagotchiGame_Cooking cooking = Object.FindObjectOfType<TamagotchiGame_Cooking>();
            if (cooking != null && cooking.isActiveAndEnabled && !cooking.ready)
            {
                ButtonMouseClick knife = AppButton("InterfaceCut/Pointer");
                // A well-timed release cuts (the game's own Cut, which the release calls when the slider is in place).
                if (labS1CutDown && knife != null) { cooking.Cut(); knife.PointerUp(); labS1CutDown = false; Log("Lab: scene1 host cuts (cut " + cooking.indexCut + ", food " + cooking.indexCutFood + ")"); return true; }
                if (cooking.foodTakeNow == null)
                {
                    foreach (Trigger_MouseClick c in Object.FindObjectsOfType<Trigger_MouseClick>())
                        if (c != null && c.isActiveAndEnabled && (c.name == "Carrot" || c.name == "Potato")) { Log("Lab: scene1 host takes the " + c.name); c.Click(); return true; }
                    return false;
                }
                if (knife != null && cooking.canCut) { knife.PointerDown(); labS1CutDown = true; labS1StepAt = now + 0.6f; Log("Lab: scene1 host holds the knife"); return true; }
                return false;
            }
            foreach (Trigger_MouseClick c in Object.FindObjectsOfType<Trigger_MouseClick>())
                if (c != null && c.isActiveAndEnabled && c.enabled && c.name == "Food" && PathOf(c.transform).Contains("Quest 5")) { Log("Lab: scene1 host gives Mita the food"); c.Click(); return true; }
            GameObject bought = SceneObject("BuySoup");   // "you bought ..." popup: a standard UI button closes it
            UnityEngine.UI.Button ok = bought != null && bought.activeInHierarchy ? bought.GetComponent<UnityEngine.UI.Button>() : null;
            if (ok != null) { Log("Lab: scene1 host closes the purchase popup"); ok.Press(); return true; }
            ButtonMouseClick buy = AppButton("Shop Food/FrameKategory 1/Game 1");
            if (buy != null) { Press(buy, "buy food"); return true; }
            ButtonMouseClick food = AppButton("Shop Change/FrameKategory/Button 1");   // the shop's first category: food
            if (food != null) { Press(food, "the food category"); return true; }
            GameObject pointer = SceneObject("PointerButton");
            Tamagotchi_Main app = Object.FindObjectOfType<Tamagotchi_Main>();
            if (pointer != null && pointer.activeInHierarchy && app != null && app.buttonShop != null)
            {
                ButtonMouseClick shop = app.buttonShop.GetComponent<ButtonMouseClick>();
                if (shop != null) { Press(shop, "the shop"); return true; }
            }
            return false;
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
