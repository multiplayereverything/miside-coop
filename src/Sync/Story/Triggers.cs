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

        // Told by another player that someone is inside: this PC holds that until they say they left. Its own Update would see its own player outside and send the opposite back, every frame.
        static readonly HashSet<System.IntPtr> distanceHeld = new HashSet<System.IntPtr>();
        public static bool DistanceHeld(Trigger_DistanceCircle t) { return t != null && distanceHeld.Contains(t.Pointer); }

        public static void CarrotCutHere(Location3CutCarrot c) { TogetherRuntime rt = Instance; if (rt != null && rt.Connected && !Replaying) Call(29, c, 0); }

        public static void DistanceBefore(Trigger_DistanceCircle t) { if (t != null) distanceInside[t.Pointer] = t.targetInside; }

        public static void DistanceAfter(Trigger_DistanceCircle t)
        {
            bool was;
            if (t == null || !distanceInside.TryGetValue(t.Pointer, out was) || was == t.targetInside) return;
            Story(Op.DistanceTrigger, t, t.targetInside ? 1 : 2);
        }

        // A look on a chapter's story objects can start the story (Scene 3: Mita Stand's look starts her free roam), and left
        // to one PC it split them. Hover effects elsewhere stay local.
        public static bool StoryLook(Trigger_MouseEvent t)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.InLogChapter) return false;
            string path = SceneKeys.Key(t);
            // Scene 6 names its story root "Act": looking down there ends Scene 5 (Scene_Load.UnloadScene).
            return path.Contains("/Quests#") || path.Contains("/Acts#") || path.Contains("/Act#");
        }

        // Standing still opens the game's Tetris console. In a session players often wait for each other, so that timer
        // doesn't run. A chapter that asks for the console itself (needTetris) still gets it.
        public static void HoldIdleConsole(GameController c)
        {
            TogetherRuntime rt = Instance;
            if (rt != null && rt.Connected && c != null && !c.needTetris && c.timeAFK > 0) c.timeAFK = 0;
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
            if (inside) distanceHeld.Add(t.Pointer); else distanceHeld.Remove(t.Pointer);
            if (t.targetInside == inside) return;   // this machine got there by itself
            t.targetInside = inside;
            distanceInside[t.Pointer] = inside;
            if (inside) { if (t.eventEnter != null) t.eventEnter.Invoke(); }
            else if (t.eventExit != null) t.eventExit.Invoke();
            distanceApplied++;
        }

        void ClearTriggers() { firedLook.Clear(); distanceInside.Clear(); distanceHeld.Clear(); lookHold.Clear(); doorGameOff.Clear(); }

        // --- Method relays: puzzle and input calls replayed on every machine (Op.Call, key "<entry>:<scene key>") ---
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
        // 99 lab only: the storylog tour's test event (TourStoryLog.cs)
        // 16-23 Scene 1 phone app: start/stop, the dialogue line, the name, its buttons, the cooking result; host only (AppSync.cs)
        // 24 Scene 4 console: the win, lose or draw reply the player's game switched on (Minigames.cs)
        // 25 ObjectInteractiveItemTake.TakeDestroy: the taker's hand reached the item; the take completes everywhere
        // 26 Scene_Load.UnloadScene(name): a chapter scene unloaded mid-play (Scene 5 under Scene 6's basement)
        // 28 ObjectDoor.DoorOpened: the rooms behind the door come on for everyone (WorldSync.DoorOpenedHere)
        // 29 Location3CutCarrot.Cut: every slice shows on every PC
        public static bool ApplyingUnload;

        // Another player's take the host waits on (Op.Call 25). If their hand never gets there (a death or cutscene in
        // between, or a lab pilot standing wrong), the host completes it for everyone after TakeWaitSeconds, so the story
        // can't hang on it; the taker's late report then finds the item gone.
        const float TakeWaitSeconds = 8;
        readonly Dictionary<string, float> watchedTakes = new Dictionary<string, float>();
        public static void NoteWatchedTake(ObjectInteractiveItemTake take)
        {
            TogetherRuntime rt = Instance;
            if (rt != null && rt.Hosting && take != null) { string key = SceneKeys.Key(take); if (!rt.watchedTakes.ContainsKey(key)) rt.watchedTakes[key] = Time.realtimeSinceStartup; }
        }

        void WatchedTakeTick(float now)
        {
            foreach (KeyValuePair<string, float> t in new List<KeyValuePair<string, float>>(watchedTakes))
            {
                if (now - t.Value < TakeWaitSeconds) continue;
                watchedTakes.Remove(t.Key);
                if (SceneKeys.Find<ObjectInteractiveItemTake>(t.Key) == null) continue;   // completed meanwhile
                Log("MT-STORY the taker's hand didn't arrive in " + TakeWaitSeconds + " s; the take completes for everyone: " + t.Key);
                Story(Op.Call, "25:" + t.Key, 0);
                ApplyCall("25:" + t.Key, 0);
            }
        }
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
                case 1: case 2: case 3: case 4:
                {
                    InputKeyboard_Main keys = SceneKeys.Find<InputKeyboard_Main>(target);
                    if (Missing(keys, target)) break;
                    if (entry == 1) keys.AddKey(((char)arg).ToString()); else if (entry == 2) keys.RemoveKey(); else if (entry == 3) keys.ButtonEnter(); else keys.ButtonUpper();
                    break;
                }
                case 99: if (LabMode) ApplyLabOrdered(target); break;
                case 7: QueueAnimationCatchUp(target, arg); break;
                case 8: ApplyQuestMove(target); break;
                case 9: ApplyQuestReply(target, arg); break;
                case 10: case 11: case 12: case 13: case 14: ApplyCoreCall(entry, target, arg); break;
                case 15: ApplyMobileShow(target); break;
                case 24: ApplyConsoleReply(target); break;
                case 30: handLeader = arg; break;
                case 29:
                {
                    Location3CutCarrot c = SceneKeys.Find<Location3CutCarrot>(target);
                    if (Missing(c, target)) break;
                    // CutStart runs for the slicer only; Cut needs what it sets. The end of the slicing is the slicer's (its gate).
                    if (c.animPlayer == null) { PlayerMove me = LocalPlayer; c.animPlayer = me != null ? me.GetComponentInChildren<Animator>() : null; c.keyDown = true; }
                    if (c.animPlayer == null) break;
                    UnityEngine.Events.UnityEvent end = c.eventFinish;
                    c.eventFinish = new UnityEngine.Events.UnityEvent();
                    bool was = Replaying;
                    Replaying = true;
                    try { c.Cut(); } finally { Replaying = was; c.eventFinish = end; }
                    break;
                }
                case 28: { ObjectDoor door = SceneKeys.Find<ObjectDoor>(target); if (!Missing(door, target)) door.OptimizationObjectsActivation(); break; }
                case 26:
                {
                    UnityEngine.SceneManagement.Scene s = UnityEngine.SceneManagement.SceneManager.GetSceneByName(target);
                    Scene_Load load = Object.FindObjectOfType<Scene_Load>();
                    if (!s.isLoaded || load == null) break;   // already gone here
                    Log("MT-STORY unloading " + target + " here, as another player did");
                    ApplyingUnload = true;
                    try { load.UnloadScene(target); } finally { ApplyingUnload = false; }
                    break;
                }
                case 25:
                {
                    ObjectInteractiveItemTake take = SceneKeys.Find<ObjectInteractiveItemTake>(target);
                    watchedTakes.Remove(target);
                    if (take == null && SceneKeys.WasRecorded(target)) break;   // already taken here
                    if (!Missing(take, target)) { Log("MT-STORY the taker's take completes here: " + target); take.TakeDestroy(); }
                    break;
                }
                case 16: case 17: case 18: case 19: case 20: case 21: case 22: case 23: case 27: ApplyAppCall(entry, target, arg); break;
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
