using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // "Everyone restarts": MiSide restarts chases inside the chapter (no scene reload). When any player dies, the same
    // death call runs on every machine, so the whole group goes back to the checkpoint together. A short window
    // swallows the echoes (the replayed death often calls a second death method on the way).
    // Death list cross-checked with QuestionBlock64's MiSide-Multiplayer death link (ideas only; no code reused).
    public sealed partial class TogetherRuntime
    {
        public const int DeathMitaKiller = 1, DeathManekenCheckpoint = 2, DeathLift = 3, DeathLampMove = 4, DeathArena = 5,
            DeathRunCorridor = 6, DeathManeken = 7, DeathShooter = 8, DeathGame4 = 9;
        static float deathQuietUntil = -1;
        public static int deathsApplied, deathsSent;

        public static void AfterDeath(Component source, int kind)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || Replaying || source == null) return;
            if (Time.realtimeSinceStartup < deathQuietUntil) return;
            deathQuietUntil = Time.realtimeSinceStartup + 6;
            deathsSent++;
            Log("Death (" + kind + ") at " + SceneKeys.Key(source) + ": everyone restarts");
            Story(Op.Death, source, kind);
            rt.Tell("A player died: everyone restarts at the checkpoint.");
        }

        static void ApplyDeath(string key, int kind)
        {
            if (Time.realtimeSinceStartup < deathQuietUntil) return;
            deathQuietUntil = Time.realtimeSinceStartup + 6;
            deathsApplied++;
            try
            {
            switch (kind)
            {
                case DeathMitaKiller: { var c = SceneKeys.Find<Location6_MitaKiller>(key); if (c != null) c.Kill(); break; }
                case DeathManekenCheckpoint: { var c = SceneKeys.Find<Location10_ManekenChekpoint>(key); if (c != null) c.PlayerDeath(); break; }
                case DeathLift: { var c = SceneKeys.Find<Location11_Lift>(key); if (c != null) c.DeathPlayer(); break; }
                case DeathLampMove: { var c = SceneKeys.Find<Location11_LampMove>(key); if (c != null) c.RestartPlayer(); break; }
                case DeathArena: { var c = SceneKeys.Find<Location20_Arena>(key); if (c != null) c.PlayerDeath(); break; }
                case DeathRunCorridor: { var c = SceneKeys.Find<Location20_RunCorridor>(key); if (c != null) c.KillPlayerStart(); break; }
                case DeathManeken: { var c = SceneKeys.Find<Mob_Maneken>(key); if (c != null) c.StartKillPlayer(); break; }
                case DeathShooter: { var c = SceneKeys.Find<Shooter_Main>(key); if (c != null) c.PlayerDeath(); break; }
                case DeathGame4: { var c = SceneKeys.Find<Location19_Game4>(key); if (c != null) c.Death(); break; }
            }
            }
            // The game's death code can throw part-way outside its usual moment; the restart it began still counts.
            catch (Exception ex) { Log("Replayed death (" + kind + ") threw inside the game: " + ex.Message); }
            if (Instance != null) Instance.Tell("Another player died: everyone restarts at the checkpoint.");
        }
    }
}
