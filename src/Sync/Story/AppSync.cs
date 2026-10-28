using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Scene 1's phone app (Tamagotchi_Main, "the app"): the host holds the phone and plays it; everyone else's app is a
    // view of the host's (playtest: "only one player can hold the phone", "only host can type their name", the app's
    // dialogue and days must match). Read from the game's wiring (-mt-dump Scene 1):
    //   the phone's animations ("Animation Smartphone ...", CutsceneRules) start the app with Tamagotchi_Main.GameStart;
    //   each day is a quest object whose Mita dialogue (Tamagotchi_Dialogue_Mob, shown by Tamagotchi_Dialogue) ends in
    //   eventStop, which switches on the next day; the name is typed into Location1_InputNamePlayer and checked by
    //   CheckName, whose events start the greeting.
    // Relays (Op.Call, from the host only):
    //   16 Tamagotchi_Main.GameStart (arg 1) / GameStop (arg 0)
    //   17 the host's dialogue position: "17:<Tamagotchi_Dialogue_Mob>" arg line index, or -1 when the host closed it;
    //      a client steps its own dialogue (NextDialogue) until it is on the same line
    //   18 the name: "18:<Location1_InputNamePlayer>|<name>", checked there with the same name
    //   19 a press on the app's own interface (ButtonMouseClick under Tamagotchi/: the shop, buying, the minigames, the
    //      cooking knife): arg 0 PointerDown (its eventClick), 1 PointerUp (eventUp)
    //   20 TamagotchiGame_Cooking.Ready: the cooking board finished (it opens day 5); a client's own cutting may differ
    //   21 a click on one of the app's standard UI buttons (UnityEngine.UI.Button.Press: "you bought ..." popups)
    //   22/23 the app's money / energy (Tamagotchi_Main.money, energy): the host's, so buying works the same everywhere
    //      (mini-games that pay can play out differently on each copy)
    // On clients the player's own reading on (Space) and name check do nothing; the host's do.
    public sealed partial class TogetherRuntime
    {
        public int appCallsApplied;
        string appDialogueSent;
        int appIndexSent = -2;

        public static bool AppViewer => Instance != null && Instance.IsClient && Instance.Connected && !Replaying;

        float hostOnlyToldAt = -100;

        // Scene 1: only the host picks up the phone. A client's own press does nothing (the host's is replayed there).
        public static bool HostOnlyClick(ObjectInteractive o)
        {
            TogetherRuntime rt = Instance;
            if (!AppViewer || ReplayingInteraction || o == null || o.name != "Interactive Mobile" || ActiveSceneName != "Scene 1 - RealRoom") return false;
            if (Time.realtimeSinceStartup - rt.hostOnlyToldAt > 5) { rt.hostOnlyToldAt = Time.realtimeSinceStartup; rt.Tell("The host holds the phone in this chapter; you see their app."); }
            return true;
        }

        public static void AppStarted(Tamagotchi_Main app, bool start)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Hosting || Replaying || app == null) return;
            Call(16, app, start ? 1 : 0);
        }

        // A press on the app's interface (not the game's menus, which use the same button class).
        public static bool IsAppButton(Component b)
        {
            return b != null && ActiveSceneName == "Scene 1 - RealRoom" && SceneKeys.Key(b).Contains("/Tamagotchi#");
        }

        public static void AppClicked(UnityEngine.UI.Button b)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Hosting || Replaying || !IsAppButton(b)) return;
            Call(21, b, 0);
        }

        public static void AppPressed(ButtonMouseClick b, bool up)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Hosting || Replaying || !IsAppButton(b)) return;
            Call(19, b, up ? 1 : 0);
        }

        public static void AppCooked(TamagotchiGame_Cooking cooking)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Hosting || Replaying || cooking == null) return;
            Call(20, cooking, 0);
        }

        public static void AppNamed(Location1_InputNamePlayer input)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Hosting || Replaying || input == null || input.inputName == null) return;
            Story(Op.Call, "18:" + SceneKeys.Key(input) + "|" + input.inputName.text, 0);
        }

        // The window (Tamagotchi_Dialogue, decompiled at 0x3d81a0/0x3d83c0): enableDialogue while a conversation is open,
        // play while a line is still typing out. Space (KeyNextDialogue) works only when open and done typing, and calls
        // NextDialogue: the next line, or after the last one the end (the conversation's eventStop).
        string appTarget;      // client: the host's conversation and line to catch up to (-1: the host closed it)
        int appTargetIndex;
        float appStepAt;

        int appMoneySent = int.MinValue, appEnergySent = int.MinValue;

        // The app's objects, looked up every 2 s (not every frame) and only in Scene 1.
        Tamagotchi_Main appMain;
        Tamagotchi_Dialogue[] appWindows;
        float appFindAt;
        System.IntPtr appRunPointer;
        string appRunKey;

        // Host: the conversation and line, sent when they change. Client: step like Space until on the host's line.
        void AppDialogueTick()
        {
            if (ActiveSceneName != "Scene 1 - RealRoom") { appMain = null; appWindows = null; return; }
            float now = Time.realtimeSinceStartup;
            if (now >= appFindAt) { appFindAt = now + 2; appMain = Object.FindObjectOfType<Tamagotchi_Main>(); appWindows = Object.FindObjectsOfType<Tamagotchi_Dialogue>(); }
            if (!Hosting) { AppFollow(); return; }
            Tamagotchi_Main main = appMain;
            if (main != null && main.money != appMoneySent) { appMoneySent = main.money; Call(22, main, main.money); }
            if (main != null && main.energy != appEnergySent) { appEnergySent = main.energy; Call(23, main, main.energy); }
            Tamagotchi_Dialogue d = AppDialogue();
            string run = d != null && d.dialogueRun != null ? RunKey(d.dialogueRun) : null;
            int index = run != null ? d.dialogueIndex : -1;
            if (run == null && appDialogueSent == null) return;
            if (run == appDialogueSent && index == appIndexSent) return;
            if (run == null) Story(Op.Call, "17:" + appDialogueSent, -1);
            else Story(Op.Call, "17:" + run, index);
            appDialogueSent = run; appIndexSent = index;
        }

        Tamagotchi_Dialogue AppDialogue()
        {
            if (appWindows == null) return null;
            foreach (Tamagotchi_Dialogue d in appWindows) if (d != null && d.enableDialogue) return d;
            return null;
        }

        string RunKey(Tamagotchi_Dialogue_Mob mob)
        {
            if (mob.Pointer != appRunPointer) { appRunPointer = mob.Pointer; appRunKey = SceneKeys.Key(mob); }
            return appRunKey;
        }

        void AppFollow()
        {
            if (appTarget == null || Time.realtimeSinceStartup < appStepAt) return;
            Tamagotchi_Dialogue d = AppDialogue();
            if (d == null || d.dialogueRun == null || RunKey(d.dialogueRun) != appTarget) return;   // not open here (yet), or another one
            if (d.play || (appTargetIndex >= 0 && d.dialogueIndex >= appTargetIndex)) return;              // typing, or already there
            appStepAt = Time.realtimeSinceStartup + 0.2f;
            bool was = Replaying;
            Replaying = true;
            try { d.NextDialogue(); appCallsApplied++; }
            finally { Replaying = was; }
        }

        void ApplyAppCall(int entry, string target, int arg)
        {
            bool was = Replaying;
            Replaying = true;   // the calls below are the host's, not this player's input
            try { ApplyAppCallCore(entry, target, arg); }
            finally { Replaying = was; }
        }

        void ApplyAppCallCore(int entry, string target, int arg)
        {
            if (entry == 16)
            {
                Tamagotchi_Main app = SceneKeys.Find<Tamagotchi_Main>(target);
                if (Missing(app, target)) return;
                if (arg == 1 && !app.play) app.GameStart(); else if (arg == 0 && app.play) app.GameStop();
                appCallsApplied++;
                return;
            }
            if (entry == 19)
            {
                ButtonMouseClick b = SceneKeys.Find<ButtonMouseClick>(target);
                if (Missing(b, target) || Inactive(b, target)) return;
                if (arg == 1) b.PointerUp(); else b.PointerDown();
                appCallsApplied++;
                return;
            }
            if (entry == 22 || entry == 23)
            {
                Tamagotchi_Main main = SceneKeys.Find<Tamagotchi_Main>(target);
                if (Missing(main, target)) return;
                if (entry == 22) { main.money = arg; if (main.textMoney != null) main.textMoney.text = arg.ToString(); }
                else main.energy = arg;
                appCallsApplied++;
                return;
            }
            if (entry == 21)
            {
                UnityEngine.UI.Button b = SceneKeys.Find<UnityEngine.UI.Button>(target);
                if (Missing(b, target) || Inactive(b, target)) return;
                b.Press();
                appCallsApplied++;
                return;
            }
            if (entry == 20)
            {
                TamagotchiGame_Cooking cooking = SceneKeys.Find<TamagotchiGame_Cooking>(target);
                if (Missing(cooking, target)) return;
                if (!cooking.ready) cooking.Ready();
                appCallsApplied++;
                return;
            }
            if (entry == 18)
            {
                int bar = target.LastIndexOf('|');
                if (bar < 0) return;
                string key = target.Substring(0, bar);
                Location1_InputNamePlayer input = SceneKeys.Find<Location1_InputNamePlayer>(key);
                if (Missing(input, key) || Inactive(input, key) || input.inputName == null) return;
                input.inputName.text = target.Substring(bar + 1);
                input.CheckName();
                appCallsApplied++;
                return;
            }
            // 17: the host's conversation and line; AppFollow steps there as this machine's own lines finish typing.
            appTarget = target; appTargetIndex = arg;
        }
    }
}
