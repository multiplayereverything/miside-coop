using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Minigames: one player plays, everyone else watches them play (see the ownership notes below).
    public sealed partial class TogetherRuntime
    {
        // Events: the UnityEvent fields the end method fires for the story (cards: Mita reacts, the table closes...).
        // When the player finishes, every watcher fires these same events on their copy, and the story events they
        // trigger are not relayed from the player (no double effects).
        // WatchEnd: the method a watcher runs on its own copy when the player finishes, so its game state moves on too.
        // Live: the game reads the keyboard itself as it runs (docs/INPUT-AUDIT.md), so on the story log it runs on the
        // Main Character's PC alone; Mita follows that PC meanwhile (WorldSync.NpcAuthority) and the others' copies show
        // it (ConsoleScreen.cs).
        // Over: how the player's PC sees the game end when the game doesn't call an End method (the card table sets
        // play off and fires eventStopGame itself).
        // Quiet: on a watcher, the game's own scripts under its object are switched off while the mirror shows it (the
        // console's fight would redraw its hearts from its own unplayed state); only for a game object holding nothing else.
        sealed class MinigameKind { public Type Type; public string[] Start, End, Events = new string[0]; public string WatchEnd; public bool Live, Quiet; public Func<Component, bool> Over; public Func<Component, Transform> Extra; }

        static readonly MinigameKind[] MinigameKinds =
        {
            new MinigameKind { Type = typeof(Location4TableCardGame), Start = new[] { "StartGame" }, End = new[] { "StopGame" }, Events = new[] { "eventStopGame" }, Live = true, Over = c => !c.Cast<Location4TableCardGame>().play },
            new MinigameKind { Type = typeof(Location7_GameDance), Start = new[] { "PlayGame" }, End = new[] { "EndGame" }, Events = new[] { "eventFinishPlayer" } },
            new MinigameKind { Type = typeof(Location18_TicTacToe), Start = new[] { "StartGame" }, End = new[] { "Finish" }, Events = new[] { "eventFinish" } },
            new MinigameKind { Type = typeof(Location14_PCSnaker), Start = new[] { "StartComponent" }, End = new[] { "CloseGame" } },
            new MinigameKind { Type = typeof(Location14_Saper), Start = new[] { "StartComponent" }, End = new[] { "Win", "Lose" } },
            new MinigameKind { Type = typeof(MinigamesTelevisionGame), Start = new[] { "PlayGame" }, End = new[] { "ExitGame" }, WatchEnd = "ExitGame", Live = true, Quiet = true,
                Extra = c => { GameObject ui = c.Cast<MinigamesTelevisionGame>().objectInterface; return ui != null ? ui.transform : null; } },
            new MinigameKind { Type = typeof(CarSpace_Main), Start = new[] { "PlayRacing" }, End = new[] { "FinishRacing", "BossFinish" } },
            new MinigameKind { Type = typeof(Shooter_Main), Start = new[] { "WaveStart" }, End = new[] { "Win" } },
            new MinigameKind { Type = typeof(MakeManeken_Main), Start = new[] { "Start" }, End = new[] { "FinishGame" } },
            new MinigameKind { Type = typeof(Location19_Game1), Start = new[] { "OnEnable" }, End = new[] { "Finish" } },
            new MinigameKind { Type = typeof(Location19_Game3), Start = new[] { "Start" }, End = new[] { "Finish" } },
            new MinigameKind { Type = typeof(Location19_Game4), Start = new[] { "Start" }, End = new[] { "Finish" } },
            new MinigameKind { Type = typeof(MT_GameFly), Start = new[] { "Play" }, End = new[] { "Win", "Lose" } },
            new MinigameKind { Type = typeof(MT_GameCnowballs), Start = new[] { "Play" }, End = new[] { "Finish" } },
            new MinigameKind { Type = typeof(QuadLinerMain), Start = new[] { "StartGame" }, End = new[] { "Win", "Lose", "ExitGame" }, Events = new[] { "eventExitGame" } },
        };

        public static int InstallMinigamePatches(HarmonyLib.Harmony harmony)
        {
            int n = 0;
            for (int k = 0; k < MinigameKinds.Length; k++)
            {
                MinigameKind kind = MinigameKinds[k];
                foreach (string name in kind.Start)
                    n += PatchOne(harmony, kind.Type, name, nameof(MinigameStartPostfix), nameof(MinigameStartPrefix));
                foreach (string name in kind.End)
                    n += PatchOne(harmony, kind.Type, name, nameof(MinigameEndPostfix), nameof(MinigameEndPrefix));
            }
            return n;
        }

        static int PatchOne(HarmonyLib.Harmony harmony, Type type, string name, string postfix, string prefix = null)
        {
            try
            {
                MethodInfo target = AccessTools.Method(type, name, Type.EmptyTypes);
                if (target == null) { Log("Minigame hook missing: " + type.Name + "." + name); return 0; }
                harmony.Patch(target, prefix: prefix == null ? null : new HarmonyMethod(typeof(TogetherRuntime), prefix), postfix: new HarmonyMethod(typeof(TogetherRuntime), postfix));
                return 1;
            }
            catch (Exception ex) { Log("Minigame hook failed " + type.Name + "." + name + ": " + ex.Message); return 0; }
        }

        public static bool LiveOff;   // -mt-nolive: every copy plays live minigames, the lab's proof that the tv and cards checks can fail
        static bool IsLive(int kind) { return MinigameKinds[kind].Live && !LiveOff; }

        static int KindOf(Type type) { for (int i = 0; i < MinigameKinds.Length; i++) if (MinigameKinds[i].Type == type) return i; return -1; }

        // The compiled game shares one body between methods with identical code (a minigame's StartGame and a hint's
        // SmoothDestroy, say), so a hook on one also runs for the others. Seen in Scene 1: the tutorial hint's fade made
        // a player the "owner" of Location18_TicTacToe, and the other player's phone pickup was then blocked as watching.
        // Only calls on the minigame's own type count.
        static int KindOf(Component instance, MethodBase method)
        {
            if (instance == null || method == null) return -1;
            int kind = KindOf(method.DeclaringType);
            return kind >= 0 && instance.GetIl2CppType().Name == MinigameKinds[kind].Type.Name ? kind : -1;
        }

        // --- Ownership -------------------------------------------------------------------------------------------
        // Only the player who starts a minigame plays it (CutsceneRules, minigame kind): a start that comes from
        // replaying someone else's click is skipped here, so nobody else is seated, moved or given the controls. The
        // others watch through the player's eyes (Spectate.cs). What the minigame changes afterwards reaches everyone
        // through the room-state sync and the story events it fires.
        sealed class MinigameRun { public string Key; public int Kind, Owner; public bool Running; }
        readonly Dictionary<string, MinigameRun> minigames = new Dictionary<string, MinigameRun>();
        public int minigameStartsBlocked;
        public static float lastMinigameStartAt = -100;

        static bool MinigameStartPrefix(Component __instance, MethodBase __originalMethod)
        {
            try
            {
                int kind = KindOf(__instance, __originalMethod);
                if (kind < 0) return true;
                // On the story log a live minigame (Scene 4's console, started by Mita's line) is the Main Character's:
                // it runs on their PC only, and the story's win and lose lines follow their results.
                if (IsLive(kind) && NotMyMoment)
                {
                    if (Instance != null) Instance.minigameStartsBlocked++;
                    Log("Minigame " + __originalMethod.DeclaringType.Name + ": the Main Character plays it; watching instead");
                    return false;
                }
                if (!ReplayingInteraction) return true;
                CutsceneRules.Rule rule = CutsceneRules.For(CutsceneRules.Kind.Minigame, SceneKeys.Key(__instance));
                if (rule.Who != CutsceneRules.Who.Actor) return true;
                if (Instance != null) Instance.minigameStartsBlocked++;
                Log("Minigame " + (__originalMethod != null ? __originalMethod.DeclaringType.Name : "?") + ": another player plays it; watching instead");
                return false;
            }
            catch (Exception ex) { Log("MT-MODERROR minigame start prefix: " + ex.Message); return true; }
        }

        static void MinigameStartPostfix(Component __instance, MethodBase __originalMethod)
        {
            try
            {
                TogetherRuntime rt = Instance;
                if (rt == null || !rt.Connected || __instance == null || __originalMethod == null) return;
                int kind = KindOf(__instance, __originalMethod);
                if (kind < 0) return;
                // A live one is the Main Character's even when the log started it; a blocked start still gets here.
                if (IsLive(kind) ? NotMyMoment : Replaying || ReplayingInteraction) return;
                string key = SceneKeys.Key(__instance);
                Log("Minigame " + __originalMethod.DeclaringType.Name + " started here: " + key);
                lastMinigameStartAt = Time.realtimeSinceStartup;
                rt.SendMinigame(1, key, kind, rt.SelfId);
            }
            catch (Exception ex) { Log("MT-MODERROR minigame start: " + ex.Message); }
        }

        public static bool MinigameEnding;
        public int minigameEndsApplied;

        // The player's end method: hold back the story events its end events fire (watchers fire them themselves).
        static void MinigameEndPrefix(Component __instance, MethodBase __originalMethod)
        {
            try
            {
                TogetherRuntime rt = Instance;
                if (rt == null || !rt.Connected || __instance == null || __originalMethod == null) return;
                int kind = KindOf(__instance, __originalMethod);
                MinigameRun run;
                MinigameEnding = kind >= 0 && MinigameKinds[kind].Events.Length > 0 && rt.minigames.TryGetValue(SceneKeys.Key(__instance), out run) && run.Owner == rt.SelfId;
            }
            catch (Exception ex) { Log("MT-MODERROR minigame end prefix: " + ex.Message); }
        }

        static void MinigameEndPostfix(Component __instance, MethodBase __originalMethod)
        {
            MinigameEnding = false;
            try
            {
                TogetherRuntime rt = Instance;
                if (rt == null || !rt.Connected || Replaying || KindOf(__instance, __originalMethod) < 0) return;
                string key = SceneKeys.Key(__instance);
                MinigameRun run;
                if (!rt.minigames.TryGetValue(key, out run) || run.Owner != rt.SelfId) return;
                Log("Minigame finished here (" + __originalMethod.Name + ")");
                rt.SendMinigame(2, key, run.Kind, rt.SelfId);
            }
            catch (Exception ex) { Log("MT-MODERROR minigame end: " + ex.Message); }
        }

        void SendMinigame(byte phase, string key, int kind, int owner)
        {
            byte[] packet = Wire.Packet(Op.Minigame, w => { w.Write(phase); Wire.Text(w, key); w.Write(kind); w.Write(owner); });
            if (Hosting) { ApplyMinigame(phase, key, kind, owner); Broadcast(packet); }
            else { ApplyMinigame(phase, key, kind, owner); SendToHost(packet); }
        }

        void ReadMinigame(BinaryReader reader, int from)
        {
            byte phase = reader.ReadByte();
            string key = Wire.Text(reader, 1024);
            int kind = reader.ReadInt32(), owner = reader.ReadInt32();
            if (kind < 0 || kind >= MinigameKinds.Length) throw new IOException("Unknown minigame.");
            if (Hosting)
            {
                owner = from;
                ApplyMinigame(phase, key, kind, owner);
                Broadcast(Wire.Packet(Op.Minigame, w => { w.Write(phase); Wire.Text(w, key); w.Write(kind); w.Write(owner); }), from);
                return;
            }
            ApplyMinigame(phase, key, kind, owner);
        }

        void ApplyMinigame(byte phase, string key, int kind, int owner)
        {
            if (phase == 1) { minigames[key] = new MinigameRun { Key = key, Kind = kind, Owner = owner }; if (owner == SelfId) Tell("You are playing; the others watch."); return; }
            minigames.Remove(key);
            if (key == screenKey) { screenKey = null; screenTarget.Clear(); screenAnimTargets.Clear(); }   // its last picture mustn't outlive it
            if (owner != SelfId) FireMinigameEnd(key, kind);
        }

        // A watcher: the player finished; fire the minigame's end events here so the story carries on the same way.
        void FireMinigameEnd(string key, int kind)
        {
            MinigameKind k = MinigameKinds[kind];
            if (k.Events.Length == 0 && k.WatchEnd == null) return;
            Component game = null;
            Transform t = SceneKeys.Find<Transform>(key);
            if (t != null) game = t.GetComponent(Il2CppType.From(k.Type));
            if (game == null) { Log("Minigame end: " + key + " not found here"); return; }
            object typed = Activator.CreateInstance(k.Type, game.Pointer);
            if (k.WatchEnd != null)
            {
                Replaying = true;
                try { k.Type.GetMethod(k.WatchEnd, Type.EmptyTypes).Invoke(typed, null); minigameEndsApplied++; Log("Minigame end: " + k.Type.Name + "." + k.WatchEnd + " here"); }
                catch (Exception ex) { Log("Minigame end " + k.WatchEnd + " threw here: " + (ex.InnerException ?? ex).Message); }
                finally { Replaying = false; }
            }
            foreach (string field in k.Events)
            {
                PropertyInfo prop = k.Type.GetProperty(field);
                UnityEngine.Events.UnityEvent ev = prop != null ? prop.GetValue(typed) as UnityEngine.Events.UnityEvent : null;
                if (ev == null) continue;
                Replaying = true;
                try { ev.Invoke(); minigameEndsApplied++; Log("Minigame end: fired " + k.Type.Name + "." + field + " here"); }
                catch (Exception ex) { Log("Minigame end event " + field + " threw here: " + ex.Message); }
                finally { Replaying = false; }
            }
        }

        // Scene 4's console and card table: the player's result switches on one of the game's dialogues (Mita's reply).
        // Only their PC plays (Live), so the dialogue it switched on is relayed and switched on everywhere (Op.Call 24).
        readonly List<GameObject> consoleReplies = new List<GameObject>();
        readonly HashSet<IntPtr> consoleRepliesOn = new HashSet<IntPtr>();
        string consoleRepliesScene;

        void ConsoleReplyTick()
        {
            if (consoleRepliesScene != ActiveSceneName)
            {
                consoleRepliesScene = ActiveSceneName; consoleReplies.Clear(); consoleRepliesOn.Clear();
                foreach (MinigamesTelevisionController c in Object.FindObjectsOfType<MinigamesTelevisionController>(true))
                    if (c != null && c.games != null)
                        foreach (MinigamesTelevisionController_Game g in c.games)
                            if (g != null)
                                foreach (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> set in new[] { g.dialogueWin, g.dialogueLose, g.dialogueDraw })
                                    if (set != null) foreach (GameObject d in set) if (d != null) consoleReplies.Add(d);
                foreach (Location4TableCardGame c in Object.FindObjectsOfType<Location4TableCardGame>(true))
                    if (c != null && c.dialogues != null) foreach (GameObject d in c.dialogues) if (d != null) consoleReplies.Add(d);
            }
            bool playing = OwningMinigame;
            foreach (GameObject d in consoleReplies)
            {
                if (d == null) continue;
                bool on = d.activeSelf;
                if (on == consoleRepliesOn.Contains(d.Pointer)) continue;
                if (!on) { consoleRepliesOn.Remove(d.Pointer); continue; }
                consoleRepliesOn.Add(d.Pointer);
                if (playing) { Log("Console reply here: " + d.name); Call(24, d.transform, 0); }
            }
        }

        void ApplyConsoleReply(string key)
        {
            Transform t = SceneKeys.Find<Transform>(key);
            if (Missing(t, key)) return;
            consoleRepliesOn.Add(t.gameObject.Pointer);
            t.gameObject.SetActive(true);
        }

        // The player's PC: a live game that ended by itself (MinigameKind.Over) is finished for everyone.
        void MinigameOverTick()
        {
            foreach (MinigameRun r in new List<MinigameRun>(minigames.Values))
            {
                MinigameKind k = MinigameKinds[r.Kind];
                if (r.Owner != SelfId || k.Over == null) continue;
                Transform t = SceneKeys.Find<Transform>(r.Key);
                Component game = t != null ? t.GetComponent(Il2CppType.From(k.Type)) : null;
                if (game == null) continue;
                if (!k.Over(game)) { r.Running = true; continue; }
                if (!r.Running) continue;   // not under way yet
                Log("Minigame finished here (" + k.Type.Name + " ended by itself)");
                SendMinigame(2, r.Key, r.Kind, SelfId);
            }
        }

        bool OwningMinigame { get { foreach (MinigameRun r in minigames.Values) if (r.Owner == SelfId) return true; return false; } }

        void ClearMinigames() { minigames.Clear(); }

        // Host: a player who left can't finish their minigame. It ends for everyone, as if they had finished it.
        void MinigameOwnerLeft(int id)
        {
            foreach (MinigameRun r in new List<MinigameRun>(minigames.Values))
            {
                if (r.Owner != id) continue;
                Log("Minigame " + r.Key + " ends: its player left");
                SendMinigame(2, r.Key, r.Kind, id);
            }
        }
    }
}