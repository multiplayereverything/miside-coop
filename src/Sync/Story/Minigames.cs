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
        sealed class MinigameKind { public Type Type; public string[] Start, End, Events = new string[0]; }

        static readonly MinigameKind[] MinigameKinds =
        {
            new MinigameKind { Type = typeof(Location4TableCardGame), Start = new[] { "StartGame" }, End = new[] { "StopGame" }, Events = new[] { "eventStopGame" } },
            new MinigameKind { Type = typeof(Location7_GameDance), Start = new[] { "PlayGame" }, End = new[] { "EndGame" }, Events = new[] { "eventFinishPlayer" } },
            new MinigameKind { Type = typeof(Location18_TicTacToe), Start = new[] { "StartGame" }, End = new[] { "Finish" }, Events = new[] { "eventFinish" } },
            new MinigameKind { Type = typeof(Location14_PCSnaker), Start = new[] { "StartComponent" }, End = new[] { "CloseGame" } },
            new MinigameKind { Type = typeof(Location14_Saper), Start = new[] { "StartComponent" }, End = new[] { "Win", "Lose" } },
            new MinigameKind { Type = typeof(MinigamesTelevisionGame), Start = new[] { "PlayGame" }, End = new[] { "ExitGame" } },
            new MinigameKind { Type = typeof(CarSpace_Main), Start = new[] { "PlayRacing" }, End = new[] { "FinishRacing", "BossFinish" } },
            new MinigameKind { Type = typeof(Shooter_Main), Start = new[] { "WaveStart" }, End = new[] { "Win" } },
            new MinigameKind { Type = typeof(MakeManeken_Main), Start = new[] { "Start" }, End = new[] { "FinishGame" } },
            new MinigameKind { Type = typeof(Tamagotchi_Main), Start = new[] { "GameStart" }, End = new[] { "GameStop" } },
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

        static int KindOf(Type type) { for (int i = 0; i < MinigameKinds.Length; i++) if (MinigameKinds[i].Type == type) return i; return -1; }

        // --- Ownership -------------------------------------------------------------------------------------------
        // Only the player who starts a minigame plays it (CutsceneRules, minigame kind): a start that comes from
        // replaying someone else's click is skipped here, so nobody else is seated, moved or given the controls. The
        // others watch through the player's eyes (Spectate.cs). What the minigame changes afterwards reaches everyone
        // through the room-state sync and the story events it fires.
        sealed class MinigameRun { public string Key; public int Kind, Owner; }
        readonly Dictionary<string, MinigameRun> minigames = new Dictionary<string, MinigameRun>();
        public int minigameStartsBlocked;
        public static float lastMinigameStartAt = -100;

        static bool MinigameStartPrefix(Component __instance, MethodBase __originalMethod)
        {
            try
            {
                if (!ReplayingInteraction || __instance == null) return true;
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
                if (rt == null || !rt.Connected || Replaying || ReplayingInteraction || __instance == null || __originalMethod == null) return;
                int kind = KindOf(__originalMethod.DeclaringType);
                if (kind < 0) return;
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
                int kind = KindOf(__originalMethod.DeclaringType);
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
                if (rt == null || !rt.Connected || Replaying || __instance == null || __originalMethod == null) return;
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
            if (owner != SelfId) FireMinigameEnd(key, kind);
        }

        // A watcher: the player finished; fire the minigame's end events here so the story carries on the same way.
        void FireMinigameEnd(string key, int kind)
        {
            MinigameKind k = MinigameKinds[kind];
            if (k.Events.Length == 0) return;
            Component game = null;
            Transform t = SceneKeys.Find<Transform>(key);
            if (t != null) game = t.GetComponent(Il2CppType.From(k.Type));
            if (game == null) { Log("Minigame end: " + key + " not found here"); return; }
            object typed = Activator.CreateInstance(k.Type, game.Pointer);
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

        bool OwningMinigame { get { foreach (MinigameRun r in minigames.Values) if (r.Owner == SelfId) return true; return false; } }

        void ClearMinigames() { minigames.Clear(); }
    }
}