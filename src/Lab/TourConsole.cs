using System.Text.RegularExpressions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Scene 4's console games (MinigamesTelevisionGame: the fight, the snowballs), checked inside the scene4 tour.
    // The player sitting with the gamepad (the Main Character) plays; the story's Win/Lose/Draw lines must be theirs.
    //   tv.oneplays: (every PC) no console game result came from a PC that wasn't the Main Character's
    //   tv.story:    (host) the story's Win/Lose/Draw lines match the results on the Main Character's PC
    public sealed partial class TogetherRuntime
    {
        bool labTvHooked;
        static int labTvMine, labTvOthers;
        static string labTvLocal = "";
        string labTvPlayer = "";   // host: results the Main Character's PC reported, in order

        void LabTvHook()
        {
            if (labTvHooked) return;
            labTvHooked = true;
            HarmonyLib.Harmony lab = new HarmonyLib.Harmony("local.misidetogether.lab.tv");
            foreach (string m in new[] { "GameWin", "GameLose", "GameDraw" })
                lab.Patch(HarmonyLib.AccessTools.Method(typeof(MinigamesTelevisionGame), m), new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabTvResultPrefix)));
        }

        static void LabTvResultPrefix(System.Reflection.MethodBase __originalMethod)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected) return;
            char r = __originalMethod.Name[4];   // W, L or D
            if (NotMyMoment) { labTvOthers++; Log("Lab: console result " + r + " on a PC that isn't the Main Character's"); return; }
            labTvMine++; labTvLocal += r;
            Log("Lab: console result " + r + " (Main Character)");
            if (rt.Hosting) rt.labTvPlayer += r;
            else rt.SendToHost(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-tvresult:" + r)));
        }

        public void LabTvResultNotice(string r) { labTvPlayer += r; }

        // The Main Character plays to win: a hit on Mita's fighter every half second, through its own Damage.
        float labPilotHitAt;
        void PilotFights(float now)
        {
            LabTvHook();
            LabTvSampleTick(now);
            if (NotMyMoment || now < labPilotHitAt) return;
            labPilotHitAt = now + 0.5f;
            foreach (Location4Fight f in Object.FindObjectsOfType<Location4Fight>())
                if (f != null && f.play && f.playerEnemy != null && !f.playerEnemy.death)
                {
                    if (labPilotDone.Add(f.Pointer)) { Log("Lab: pilot fights"); labPilotActions++; }
                    f.playerEnemy.Damage(5);
                }
        }

        // tv.mirror: (the watcher) its TV shows the player's fight: the hearts left on both sides and the KO sign match the
        // player's own at least 80% of the times compared (a sample can cross a change in flight), and some had changed.
        float labTvSampleAt;
        int labTvSamples, labTvSame, labTvChanged;
        static string FightHearts(Location4Fight f)
        {
            int p = 0, e = 0;
            if (f.healthPointPlayer != null) foreach (GameObject g in f.healthPointPlayer) if (g != null && g.activeSelf) p++;
            if (f.healthPointEnemy != null) foreach (GameObject g in f.healthPointEnemy) if (g != null && g.activeSelf) e++;
            return p + "," + e + "," + (f.KO != null && f.KO.activeSelf ? 1 : 0);
        }
        static Location4Fight LabFight() { foreach (Location4Fight f in Object.FindObjectsOfType<Location4Fight>()) if (f != null) return f; return null; }

        void LabTvSampleTick(float now)
        {
            if (!OwningMinigame || now < labTvSampleAt) return;
            Location4Fight f = LabFight();
            if (f == null || !f.play) return;
            labTvSampleAt = now + 2;
            string s = "lab-tvhearts:" + FightHearts(f) + "," + (f.healthPointPlayer != null ? f.healthPointPlayer.Length : 0) + "," + (f.healthPointEnemy != null ? f.healthPointEnemy.Length : 0);
            if (Hosting) Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, s))); else SendToHost(Wire.Packet(Op.Notice, w => Wire.Text(w, s)));
        }

        public void LabTvHeartsNotice(string n)
        {
            Location4Fight f = LabFight();
            if (f == null || OwningMinigame) return;
            string[] p = n.Split(',');
            string theirs = p[0] + "," + p[1] + "," + p[2], here = FightHearts(f);
            labTvSamples++;
            // Only samples after a hit count: before one, an unmirrored screen shows full hearts too.
            if (p[0] == p[3] && p[1] == p[4]) return;
            labTvChanged++;
            if (theirs == here) labTvSame++;
            if (theirs != here) Log("Lab: console screen differs: player " + theirs + " / here " + here);
        }

        void LabTvChecks()
        {
            LabCardsChecks();
            if (labTvSamples > 0)
                CheckOnce("tv.mirror", labTvChanged >= 2 && labTvSame >= labTvChanged * 0.8f, labTvSame + " of " + labTvChanged + " samples after a hit showed the player's hearts and KO (" + labTvSamples + " in all)");
            // Only where a console game was played (Scene 4); elsewhere there is nothing to say.
            if (!labTvHooked || labTvMine + labTvOthers == 0 && labTvPlayer.Length == 0) return;
            CheckOnce("tv.oneplays", labTvOthers == 0 && (labTvMine > 0 || IsClient || labTvPlayer.Length > 0), labTvMine + " results as the Main Character (" + labTvLocal + "), " + labTvOthers + " on a copy that wasn't");
            if (!Hosting) return;
            string story = "";
            foreach (string k in logKeysSeen)
            {
                Match m = Regex.Match(k, "/Dialogues#[0-9]+/[^/]+/Dialogue (Win|Lose|Draw) [0-9]+#[0-9]+$");
                if (m.Success && k.StartsWith("t0@", System.StringComparison.Ordinal)) story += m.Groups[1].Value[0];
            }
            CheckOnce("tv.story", labTvPlayer.Length > 0 && Tally(story) == Tally(labTvPlayer), "story lines " + Tally(story) + ", the player's results " + Tally(labTvPlayer) + " (" + labTvPlayer + ")");
        }

        static string Tally(string s)
        {
            int w = 0, l = 0, d = 0;
            foreach (char c in s) { if (c == 'W') w++; else if (c == 'L') l++; else if (c == 'D') d++; }
            return "W" + w + " L" + l + " D" + d;
        }
    }
}
