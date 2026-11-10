using System.Collections.Generic;
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

        // User report (2026-11-08): "mita didnt appear on the other player screen" in the phone app.
        //   app.lines: (host) every conversation in the app ends on the same line on both PCs, with Mita's camera showing her
        //              (its far clip opens to 5) on both. Conversations are matched by name: a PC can miss one's end (no player object for a moment).
        const int AppLinesNeeded = 5;
        bool labAppOpen;
        int labAppLine, labAppEnded;
        float labAppFar;
        string labAppWho;
        readonly Dictionary<string, string> labAppMine = new Dictionary<string, string>(), labAppTheirs = new Dictionary<string, string>();

        void LabAppEnded(string result)
        {
            labAppMine[labAppWho ?? "?"] = result;
            Log("MT-APPEND " + labAppMine.Count + " " + result);
            if (Hosting) LabAppCompare();
            else SendToHost(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-appline:" + (labAppWho ?? "?") + "|" + result)));
        }

        public void LabAppLineNotice(string result) { int bar = result.IndexOf('|'); labAppTheirs[result.Substring(0, bar)] = result.Substring(bar + 1); LabAppCompare(); }

        void LabAppCompare()
        {
            int n = 0;
            foreach (KeyValuePair<string, string> c in labAppMine)
            {
                string theirs;
                if (!labAppTheirs.TryGetValue(c.Key, out theirs)) continue;
                n++;
                if (c.Value != theirs) { CheckOnce("app.lines", false, c.Key + ": host " + c.Value + " / client " + theirs); return; }
            }
            if (n >= AppLinesNeeded) CheckOnce("app.lines", true, n + " conversations ended alike");
        }

        void LabAppMitaTrace()
        {
            if (appWindows != null) foreach (Tamagotchi_Dialogue w0 in appWindows) if (w0 != null)
            {
                if (w0.enableDialogue) { labAppOpen = true; labAppLine = w0.dialogueIndex; if (Running(w0)) labAppWho = RunKey(w0.dialogueRun); if (w0.mitaCamera != null && w0.mitaCamera.activeInHierarchy && w0.mitaCameraCam != null) labAppFar = Mathf.Max(labAppFar, w0.mitaCameraCam.farClipPlane); }
                else if (labAppOpen) { labAppOpen = false; LabAppEnded("line " + labAppLine + (labAppFar >= 4 ? " mita shown" : " no mita")); labAppFar = 0; }
                break;
            }
        }

        void Scene1Host(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (scene == "SceneMenu" && !labS1MenuShot && now - tourSceneAt > 4) { labS1MenuShot = true; LabShotAll("menu"); }
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 1 - RealRoom"); }
            // In the phone app the player object is switched off (the camera is the app's), so only the steps before need it.
            Tamagotchi_Main app = Object.FindObjectOfType<Tamagotchi_Main>();
            bool inApp = app != null && app.play;
            if (scene == "Scene 1 - RealRoom") LabAppMitaTrace();
            if (scene != "Scene 1 - RealRoom" || (LocalPlayer == null && !inApp)) return;
            LabScene1Tutorial(now);   // also types the name in the app, where there is no player object
            Scene1State(now);
            LabPropsInHandTick(now);
            LabCursorTrace(); LabPhoneTrace(now);
            // Every 5 s while the phone app is up (a missing Mita there was seen by the user, between 15 s shots), else 15 s.
            if (now - tourSceneAt > 20 && now >= labS1ShotAt) { labS1ShotAt = now + (inApp ? 5 : 2); LabShotAll("s1-" + (labS1Shots++).ToString("00")); }
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
            if (labTour == "scene1end" && inApp && !labS1Jumped)
            {
                // To the last day (37): switch its quest on, as day 33's last dialogue does (eventStop -> Quest 10 SetActive).
                GameObject quests = SceneObject("Quests");
                Transform last = null;
                if (quests != null) for (int i = 0; i < quests.transform.childCount; i++) if (quests.transform.GetChild(i).name.StartsWith("Quest 10")) last = quests.transform.GetChild(i);
                // Day 5 also starts loading Scene 2 in the background (Quest 6: Scene_Load.StartLoad); day 37's GoScene
                // switches to it, so the jump does both.
                Scene_Load loader = Object.FindObjectOfType<Scene_Load>();
                if (last != null && loader != null) { labS1Jumped = true; loader.StartLoad(); last.gameObject.SetActive(true); Log("Lab: scene1end host preloads " + loader.nameSceneLoad + " and jumps to " + last.name); return; }
            }
            if (Scene1Day2(now)) return;
            if (Scene1Later(now)) return;
            Tamagotchi_Dialogue d = AppDialogue();
            if (d != null && !d.play) { Log("Lab: scene1 host reads on (Space): '" + d.stringDialogueNow + "'"); d.KeyNextDialogue(); labS1StepAt = now + 3; }
        }

        bool labS1CutDown, labS1MenuShot, labS1Jumped;
        float labS1EndAt = -1;

        // The phone app's cooking board is one shared board: any player takes a vegetable and holds the knife (the user's
        // request, 2026-11-08). The client plays it first, as a player would, for CookClientSeconds.
        //   cook.client: (host) the host's board moved from the client's presses alone
        //   cook.same:   (host) both boards then show the same cut and food
        const float CookClientSeconds = 22;
        float labS1CookAt = -1, labS1CookStepAt, labS1CookHeldAt = -1;
        string labS1CookClient;
        bool labS1CookSent;
        public void LabCookNotice(string state) { labS1CookClient = state; }

        // A held knife is dragged with the mouse (TamagotchiGame_Cooking.LateUpdate, disassembly: a downward Input.GetAxis
        // grows the slider; past its mark the game calls Cut). The lab's mouse moves down while it holds the knife.
        static bool labInCook, labCookDrag;
        bool labCookHooked;
        static void LabCookLatePrefix() { labInCook = true; }
        static void LabCookLatePostfix() { labInCook = false; }
        static void LabCookAxisPostfix(ref float __result) { if (labInCook && labCookDrag) __result = -3f; }

        void Scene1CookClient(float now, string scene)
        {
            if (!labCookHooked)
            {
                labCookHooked = true;
                HarmonyLib.Harmony lab = new HarmonyLib.Harmony("local.misidetogether.lab.cook");
                lab.Patch(HarmonyLib.AccessTools.Method(typeof(TamagotchiGame_Cooking), "LateUpdate"), new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabCookLatePrefix)), new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabCookLatePostfix)));
                lab.Patch(HarmonyLib.AccessTools.Method(typeof(Input), "GetAxis", new[] { typeof(string) }), null, new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabCookAxisPostfix)) { priority = HarmonyLib.Priority.First });
            }
            labCookDrag = labS1CookHeldAt >= 0;
            if (!IsClient || scene != "Scene 1 - RealRoom" || labS1CookSent || now < labS1CookStepAt) return;
            TamagotchiGame_Cooking cooking = Object.FindObjectOfType<TamagotchiGame_Cooking>();
            if (cooking == null || !cooking.isActiveAndEnabled || cooking.ready) return;
            if (labS1CookAt < 0) labS1CookAt = now;
            labS1CookStepAt = now + 0.3f;
            ButtonMouseClick knife = AppButton("InterfaceCut/Pointer");
            if (now - labS1CookAt >= CookClientSeconds)
            {
                labS1CookSent = true;
                if (labS1CookHeldAt >= 0 && knife != null) knife.PointerUp();
                string s = "lab-cook:" + cooking.indexCut + "," + cooking.indexCutFood;
                Log("Lab: scene1 client's turn at the board ends: " + s);
                SendToHost(Wire.Packet(Op.Notice, w => Wire.Text(w, s)));
                return;
            }
            if (cooking.foodTakeNow == null)
            {
                foreach (Trigger_MouseClick c in Object.FindObjectsOfType<Trigger_MouseClick>())
                    if (c != null && c.isActiveAndEnabled && (c.name == "Carrot" || c.name == "Potato")) { Log("Lab: scene1 client takes the " + c.name); c.Click(); labS1CookStepAt = now + 1.5f; return; }
                return;
            }
            if (knife == null) return;
            if (labS1CookHeldAt >= 0) { if (now - labS1CookHeldAt > 1.2f) { Log("Lab: scene1 client lets go of the knife (cut " + cooking.indexCut + ")"); knife.PointerUp(); labS1CookHeldAt = -1; labS1CookStepAt = now + 0.8f; } return; }
            if (cooking.canCut) { Log("Lab: scene1 client holds the knife"); knife.PointerDown(); labS1CookHeldAt = now; }
        }

        // scene1end: after the last day the game loads Scene 2 (Scene_Load.GoScene); everyone must arrive with a player.
        void Scene1EndTick(float now, string scene)
        {
            if (scene != "Scene 2 - InGame") return;
            if (labS1EndAt < 0) labS1EndAt = now;
            if (LocalPlayer != null && now - labS1EndAt > 15 && now - labS1EndAt < 16) LabShot("end-" + Name);
            if (LocalPlayer != null && now - labS1EndAt > 20) { LabPropsInHandCheck(); LabCursorCheck(); }
            if (LocalPlayer != null && now - labS1EndAt > 20) CheckOnce("end.arrived", true, "in Scene 2 with a player, camera " + (Camera.main != null ? Camera.main.name : "none"));
            else if (now - labS1EndAt > 90) CheckOnce("end.arrived", false, "in Scene 2 for 90 s without a player");
        }

        // The day: the last switched-on quest under World/Quests ("Quest 6 - 5 день" is day 5).
        static string Scene1Day()
        {
            GameObject quests = SceneObject("Quests");
            string day = "none";
            if (quests != null) for (int i = 0; i < quests.transform.childCount; i++) { Transform q = quests.transform.GetChild(i); if (q.gameObject.activeSelf && q.name.StartsWith("Quest ")) day = q.name; }
            return day;
        }


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
            if (AppDialogue() != null || !Scene1Day().StartsWith("Quest 5")) return false;   // day 2 only (its rules match later screens too)
            TamagotchiGame_Cooking cooking = Object.FindObjectOfType<TamagotchiGame_Cooking>();
            if (cooking != null && cooking.isActiveAndEnabled && !cooking.ready)
            {
                // The client cooks first, with the host's hands off (Scene1CookClient).
                if (labS1CookAt < 0) labS1CookAt = now;
                if (now - labS1CookAt < CookClientSeconds + 3) return true;
                if (AcceptedCount > 0)
                {
                    CheckOnce("cook.client", cooking.indexCut > 0 || cooking.indexCutFood > 0, "the host's board after the client's turn: cut " + cooking.indexCut + ", food " + cooking.indexCutFood + ", holding " + (cooking.foodTakeNow != null ? cooking.foodTakeNow.name : "nothing"));
                    if (labS1CookClient != null) CheckOnce("cook.same", labS1CookClient == cooking.indexCut + "," + cooking.indexCutFood, "client " + labS1CookClient + " / host " + cooking.indexCut + "," + cooking.indexCutFood);
                }
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

        string labS1MiniGame;
        int labS1Wins;
        bool labS1TvBought, labS1TvChecked;
        readonly System.Collections.Generic.HashSet<System.IntPtr> labS1Clicked = new System.Collections.Generic.HashSet<System.IntPtr>();

        // Days 5 and later, as a player does them. The lab cannot play a mini-game, so a win's money comes from the app's
        // own MoneyAdd (the money is the host's for everyone, AppSync 22); the rest is the app's own buttons and clicks.
        bool Scene1Later(float now)
        {
            if (AppDialogue() != null) return false;
            Tamagotchi_Main app = Object.FindObjectOfType<Tamagotchi_Main>();
            if (app == null || !app.play) return false;
            ButtonMouseClick gift = AppButton("Gift/ButtonGame");   // day 33: give Mita the present
            if (gift != null) { Press(gift, "give Mita the present"); return true; }
            ButtonMouseClick collect = AppButton("AddMoney/Button");   // "you earned ..." after a mini-game
            if (collect != null) { Press(collect, "collect the money (" + app.money + ")"); return true; }
            if (app.playMinigame && app.minigameNow != null)
            {
                string game = app.minigameNow.name;
                if (labS1MiniGame != game) { labS1MiniGame = game; app.MoneyAdd(1000); Log("Lab: scene1 host wins the mini-game " + game + " (MoneyAdd 1000, money " + app.money + ")"); }
                ButtonMouseClick close = AppButton("Life/ButtonCloseMiniGame");
                if (close != null) { Press(close, "close the mini-game"); return true; }
                return false;
            }
            labS1MiniGame = null;
            // Earn money: the games list (Life/ButtonGame), then a game (Work/FrameKategory .../Game ..). Two wins.
            if (labS1Wins < 2)
            {
                // A game the app has the energy for (Tamagotchi_MiniGameCase.energy); with too little, Click refuses.
                foreach (Tamagotchi_MiniGameCase g in Object.FindObjectsOfType<Tamagotchi_MiniGameCase>())
                {
                    if (g == null || !g.isActiveAndEnabled || g.energy > app.energy) continue;
                    ButtonMouseClick pick = g.GetComponent<ButtonMouseClick>();
                    if (pick == null) continue;
                    labS1Wins++;
                    Press(pick, "the mini-game " + PathOf(g.transform).Split('/')[^2] + " (energy " + g.energy + " of " + app.energy + ")");
                    return true;
                }
                if (AppButton("Work/FrameKategory") != null) { Log("Lab: scene1 no mini-game affordable with energy " + app.energy); labS1Wins = 2; return false; }
                ButtonMouseClick games = AppButton("Life/ButtonGame");
                if (games != null) { Press(games, "the mini-games list"); return true; }
            }
            // Day 10: search the furniture for Mita's comb (TamagotchiGame_FindFurniture, each piece a Trigger_MouseClick).
            // Day 20: put the wardrobe in its place (PlaceForScarf/Squa). Each object is clicked once.
            TamagotchiGame_FindFurniture search = Object.FindObjectOfType<TamagotchiGame_FindFurniture>();
            foreach (Trigger_MouseClick c in Object.FindObjectsOfType<Trigger_MouseClick>())
            {
                if (c == null || !c.isActiveAndEnabled || labS1Clicked.Contains(c.Pointer)) continue;
                string path = PathOf(c.transform);
                bool piece = search != null && search.isActiveAndEnabled && path.Contains("/Furnitures/");
                if (!piece && c.name != "Squa") continue;
                labS1Clicked.Add(c.Pointer);
                Log("Lab: scene1 host clicks " + c.name);
                c.Click();
                return true;
            }
            ButtonMouseClick tv = AppButton("Shop Furniture/FrameKategory 1/Game 3");
            if (tv != null) { labS1TvBought = true; Press(tv, "buy the TV"); return true; }
            ButtonMouseClick furniture = AppButton("Shop Change/FrameKategory/Button 2");
            if (furniture != null) { Press(furniture, "the furniture category"); return true; }
            // Day 5, after the wins: a real win's "you earned" button checks the money for the TV
            // (Location1Main.CheckMoneyForTelevision); the lab's stand-in win has no such screen, so the host calls it.
            // Then the shop, furniture, the TV.
            if (labS1Wins >= 2 && !labS1TvBought && Scene1Day().StartsWith("Quest 6"))
            {
                Location1Main room = Object.FindObjectOfType<Location1Main>();
                if (!labS1TvChecked && room != null) { labS1TvChecked = true; room.CheckMoneyForTelevision(); Log("Lab: scene1 host checks the money for the TV (" + app.money + ")"); return true; }
                if (app.buttonShop != null && app.buttonShop.GetComponent<ButtonMouseClick>() is ButtonMouseClick shop && shop != null && shop.isActiveAndEnabled) { Press(shop, "the shop (for the TV)"); return true; }
            }
            return false;
        }

        void Scene1Client(float now, string scene)
        {
            PhoneNameCheck(now);
            GlobalGame.hintScreen = false;
            if (scene != "Scene 1 - RealRoom") return;
            LabAppMitaTrace();
            LabScene1Tutorial(now);   // also types the name in the app, where there is no player object
            Scene1State(now);
            LabPropsInHandTick(now);
            LabCursorTrace(); LabPhoneTrace(now);
        }
    }
}
