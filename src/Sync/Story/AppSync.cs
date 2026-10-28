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

        // Host: the conversation and line, sent when they change. Client: step like Space until on the host's line.
        void AppDialogueTick()
        {
            if (!Hosting) { AppFollow(); return; }
            Tamagotchi_Dialogue d = AppDialogue();
            string run = d != null && d.dialogueRun != null ? SceneKeys.Key(d.dialogueRun) : null;
            int index = run != null ? d.dialogueIndex : -1;
            if (run == null && appDialogueSent == null) return;
            if (run == appDialogueSent && index == appIndexSent) return;
            if (run == null) Story(Op.Call, "17:" + appDialogueSent, -1);
            else Story(Op.Call, "17:" + run, index);
            appDialogueSent = run; appIndexSent = index;
        }

        static Tamagotchi_Dialogue AppDialogue()
        {
            foreach (Tamagotchi_Dialogue d in Object.FindObjectsOfType<Tamagotchi_Dialogue>()) if (d != null && d.enableDialogue) return d;
            return null;
        }

        void AppFollow()
        {
            if (appTarget == null || Time.realtimeSinceStartup < appStepAt) return;
            Tamagotchi_Dialogue d = AppDialogue();
            if (d == null || d.dialogueRun == null || SceneKeys.Key(d.dialogueRun) != appTarget) return;   // not open here (yet), or another one
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
