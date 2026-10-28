using System.Collections.Generic;
using UnityEngine;

namespace MiSideTogether
{
    // Story triggers besides Trigger_Event (found by the inventory tour):
    //  * Trigger_MouseEvent: looking at something (Enter), when it is a one-shot story look. Whoever looks fires it for
    //    everyone; plain hover effects stay local.
    //  * Trigger_DistanceCircle: something comes within a radius (often the player). Its Update flips targetInside and
    //    fires eventEnter/eventExit; the flip is relayed, and an applied flip sets targetInside here too, so this
    //    machine's own Update does not fire it a second time.
    //  * Trigger_MouseEvent's hold timer: looking at something long enough fires eventTimeEnd inside its Update; the
    //    moment timeHoldNow reaches timeHold is relayed (arg 3) and the others fire eventTimeEnd once.
    public sealed partial class TogetherRuntime
    {
        readonly HashSet<string> firedLook = new HashSet<string>();
        public int lookHoldsApplied, distanceApplied;
        static readonly Dictionary<System.IntPtr, bool> distanceInside = new Dictionary<System.IntPtr, bool>();

        static readonly Dictionary<System.IntPtr, float> lookHold = new Dictionary<System.IntPtr, float>();

        public static void LookBefore(Trigger_MouseEvent t) { if (t != null && t.timeHold > 0) lookHold[t.Pointer] = t.timeHoldNow; }

        public static void LookAfter(Trigger_MouseEvent t)
        {
            float was;
            if (t == null || t.timeHold <= 0 || !lookHold.TryGetValue(t.Pointer, out was)) return;
            if (was < t.timeHold && t.timeHoldNow >= t.timeHold) Story(Op.LookTrigger, t, 3);
        }

        public static void DistanceBefore(Trigger_DistanceCircle t) { if (t != null) distanceInside[t.Pointer] = t.targetInside; }

        public static void DistanceAfter(Trigger_DistanceCircle t)
        {
            bool was;
            if (t == null || !distanceInside.TryGetValue(t.Pointer, out was) || was == t.targetInside) return;
            Story(Op.DistanceTrigger, t, t.targetInside ? 1 : 2);
        }

        void ApplyLookTrigger(string key, int arg)
        {
            Trigger_MouseEvent t = SceneKeys.Find<Trigger_MouseEvent>(key);
            if (Missing(t, key) || !t.gameObject.activeInHierarchy) return;
            if (arg == 1) { if (t.oneTime && !firedLook.Add(key)) return; t.Enter(); }
            else if (arg == 3) { if (!firedLook.Add(key + "#hold") && !t.timeContinueHold) return; t.timeHoldNow = t.timeHold; if (t.eventTimeEnd != null) t.eventTimeEnd.Invoke(); lookHoldsApplied++; }
            else t.Exit();
        }

        void ApplyDistanceTrigger(string key, int arg)
        {
            Trigger_DistanceCircle t = SceneKeys.Find<Trigger_DistanceCircle>(key);
            if (Missing(t, key) || !t.gameObject.activeInHierarchy) return;
            bool inside = arg == 1;
            if (t.targetInside == inside) return;   // this machine got there by itself
            t.targetInside = inside;
            distanceInside[t.Pointer] = inside;
            if (inside) { if (t.eventEnter != null) t.eventEnter.Invoke(); }
            else if (t.eventExit != null) t.eventExit.Invoke();
            distanceApplied++;
        }

        void ClearTriggers() { firedLook.Clear(); distanceInside.Clear(); lookHold.Clear(); }

        // --- Method relays: puzzle and input calls replayed on every machine (Op.Call, key "<entry>:<scene key>") ---
        //  0 Basement_Safe.ClickButton(int)       safe keypad presses (the right code opens it everywhere)
        //  1 InputKeyboard_Main.AddKey(char)      on-screen keyboard typing
        //  2 InputKeyboard_Main.RemoveKey()
        //  3 InputKeyboard_Main.ButtonEnter()
        //  4 InputKeyboard_Main.ButtonUpper()
        //  5 Trigger_DistanceCamera.Enter(bool)    camera-distance triggers (whoever gets close fires it)
        //  6 Trigger_DistanceCamera.Exit(bool)
        //  7 ObjectAnimationPlayer started on the host's player (0 AnimationPlay, 1 Fast, 2 OnPlayer): catch-up, Interactions.cs
        //  8 Location14_PlayerQuest.Move           Scene 14 quest game click: "8:<quest>|x,y,z|<QuestInteractive or empty>"
        //  9 Location14_GameQuestPlayer.ChangeDialogue(bool)  Scene 14 quest game reply (QuestGame.cs)
        // 10-14 Scenes 13/15 core computer: text field keys, Enter, folders, program stages (CoreComputer.cs)
        // 15 Location14_PlayerMobileMove.Play     Scene 14 phone character: shown on the others, run by the holder (PhoneCharacter.cs)
        // 16-18 Scene 1 phone app: start/stop, the dialogue line, the name; host only (AppSync.cs)
        public static void Call(int entry, Component source, int arg)
        {
            if (source == null) return;
            Story(Op.Call, entry + ":" + SceneKeys.Key(source), arg);
        }

        void ApplyCall(string key, int arg)
        {
            int colon = key.IndexOf(':');
            int entry;
            if (colon < 1 || !int.TryParse(key.Substring(0, colon), out entry)) return;
            string target = key.Substring(colon + 1);
            switch (entry)
            {
                case 0: { Basement_Safe safe = SceneKeys.Find<Basement_Safe>(target); if (!Missing(safe, target)) safe.ClickButton(arg); break; }
                case 1: case 2: case 3: case 4:
                {
                    InputKeyboard_Main keys = SceneKeys.Find<InputKeyboard_Main>(target);
                    if (Missing(keys, target)) break;
                    if (entry == 1) keys.AddKey(((char)arg).ToString()); else if (entry == 2) keys.RemoveKey(); else if (entry == 3) keys.ButtonEnter(); else keys.ButtonUpper();
                    break;
                }
                case 7: QueueAnimationCatchUp(target, arg); break;
                case 8: ApplyQuestMove(target); break;
                case 9: ApplyQuestReply(target, arg); break;
                case 10: case 11: case 12: case 13: case 14: ApplyCoreCall(entry, target, arg); break;
                case 15: ApplyMobileShow(target); break;
                case 16: case 17: case 18: ApplyAppCall(entry, target, arg); break;
                case 5: case 6:
                {
                    Trigger_DistanceCamera t = SceneKeys.Find<Trigger_DistanceCamera>(target);
                    if (Missing(t, target) || t.enter == (entry == 5)) break;   // already there by itself
                    if (entry == 5) t.Enter(arg != 0); else t.Exit(arg != 0);
                    break;
                }
            }
        }
    }
}
