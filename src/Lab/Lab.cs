using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab-only instrumentation, enabled by -mt-lab (ported from Spooky Together). It writes a per-instance
    // misidetogether-lab.log with MT-CHECK and MT-METRIC lines, which tests/verify-lab.py reads to judge pass/fail,
    // error growth, memory growth and desync. Tours (-mt-tour <name>) script what the players do.
    public sealed partial class TogetherRuntime
    {
        public static bool LabMode;
        static StreamWriter labLog;
        static readonly object labLogGate = new object();
        static int errorCount, modErrorCount;
        string labTour = "";
        bool labRuleSet;
        int labPlayers = 2;   // lab instances in this run; the host tour waits for all clients (but the lobby's late one)
        float nextMetric, labJoinRetryAt;
        readonly System.Diagnostics.Stopwatch labWatch = new System.Diagnostics.Stopwatch();
        long labModTicks; int labFrames; float labWindowStart;
        readonly HashSet<string> labChecked = new HashSet<string>();

        void InitLab(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-mt-lab") LabMode = true;
                if (args[i] == "-mt-tour" && i + 1 < args.Length) labTour = args[++i];
                if (args[i] == "-mt-players" && i + 1 < args.Length) int.TryParse(args[++i], out labPlayers);
                if (args[i] == "-mt-lag" && i + 2 < args.Length) { int.TryParse(args[++i], out Transport.LabDelayMs); int.TryParse(args[++i], out Transport.LabJitterMs); }
                if (labTour == "carry" && !labRuleSet) { labRuleSet = true; CutsceneRules.LabOverride("Door Exit", CutsceneRules.Who.Actor); }
                if (args[i] == "-mt-chapter" && i + 1 < args.Length) tourChapter = args[++i].Replace('_', ' ');
                if (args[i] == "-mt-dump" && i + 1 < args.Length) dumpScene = args[++i].Replace('_', ' ');
                if (args[i] == "-mt-trace" && i + 1 < args.Length) traceTypes = args[++i];
            }
            if (!LabMode) return;
            try
            {
                labLog = new StreamWriter(Path.Combine(Root, "misidetogether-lab.log"), false) { AutoFlush = true };
                Action<string> previous = Log;
                Log = message => { previous(message); WriteLab(message); };
            }
            catch (Exception ex) { Log("Lab log unavailable: " + ex.Message); }
            try { Application.add_logMessageReceivedThreaded(DelegateSupport.ConvertDelegate<Application.LogCallback>(new Action<string, string, LogType>(CountErrors))); }
            catch (Exception ex) { Log("Lab: Unity log hook unavailable: " + ex.Message); }
            Log("Lab mode, tour '" + labTour + "'");
            if (traceTypes != null) Trace.Install(traceTypes);
        }

        string traceTypes;

        static void WriteLab(string line)
        {
            lock (labLogGate) { if (labLog != null) labLog.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + line); }
        }

        static void CountErrors(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            int count = Interlocked.Increment(ref errorCount);
            // The first 200 game errors are kept with their source line, so a failed run says what went wrong.
            if (count <= 200)
            {
                string at = "";
                if (stackTrace != null) foreach (string line in stackTrace.Split('\n')) if (line.Trim().Length > 0) { at = line.Trim(); break; }
                WriteLab("MT-ERROR " + (condition ?? "").Replace('\n', ' ') + " | " + at);
            }
            if ((stackTrace != null && stackTrace.Contains("MiSideTogether")) || (condition != null && condition.Contains("MiSideTogether")))
            {
                Interlocked.Increment(ref modErrorCount);
                WriteLab("MT-MODERROR " + condition + " | " + (stackTrace ?? "").Replace('\n', ' '));
            }
        }

        public static void Check(string name, bool pass, string detail)
        {
            Log("MT-CHECK " + name + " " + (pass ? "PASS" : "FAIL") + " " + detail);
        }

        // A check reported once per run (the first pass, or a failure at its deadline).
        bool CheckOnce(string name, bool pass, string detail)
        {
            if (labChecked.Contains(name)) return false;
            labChecked.Add(name);
            Check(name, pass, detail);
            return true;
        }

        void LabFrameStart() { if (LabMode) { labWatch.Reset(); labWatch.Start(); } }
        float nextObjectCount; int lastModObjects; long labAllocLast;
        long labFrameTicks, labMaxFrameTicks;   // one frame's mod time (LateUpdate + Update), and the worst frame this window
        void LabFrameEnd(bool countFrame)
        {
            if (!LabMode) return;
            labWatch.Stop(); labModTicks += labWatch.ElapsedTicks; labFrameTicks += labWatch.ElapsedTicks;
            if (!countFrame) return;
            labFrames++;
            if (labFrameTicks > labMaxFrameTicks) labMaxFrameTicks = labFrameTicks;
            labFrameTicks = 0;
        }

        // The lobby tour's fourth player joins late, while the others are already in a chapter.
        bool LateLabJoiner { get { return LabMode && labTour == "lobby" && Name == "C3"; } }

        void LabTick()
        {
            if (!LabMode) return;
            float now = Time.realtimeSinceStartup;
            // Lab clients may start before the host listens; keep retrying instead of giving up.
            if (autoRole == "join" && !Active && now >= labJoinRetryAt && (!LateLabJoiner || now > 90)) { labJoinRetryAt = now + 3; StartJoin(); }
            if (now >= nextMetric)
            {
                nextMetric = now + 10;
                int avatars = 0, models = 0, modObjects = 0;
                foreach (Peer peer in peers.Values)
                    if (peer.Avatar != null && peer.Avatar.Root != null) { avatars++; if (peer.Avatar.IsModel) models++; }
                // Every loaded object is scanned: heavy in big chapters, so only once a minute (it is a leak check).
                if (now >= nextObjectCount) { nextObjectCount = now + 60; lastModObjects = 0; foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>()) if (go != null && go.name.StartsWith("MiSideTogether", StringComparison.Ordinal)) lastModObjects++; }
                modObjects = lastModObjects;
                float window = Mathf.Max(0.001f, now - labWindowStart);
                double modMs = labFrames > 0 ? labModTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / labFrames : 0;
                float fps = labFrames / window;
                double modMax = labMaxFrameTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                // Live heap = the heap as the last garbage collection left it (garbage awaiting collection is not a leak);
                // alloc = how fast managed memory is allocated (churn, which costs GC time).
                long allocated = GC.GetTotalAllocatedBytes(false);
                double allocRate = labAllocLast > 0 ? (allocated - labAllocLast) / 1048576.0 / Mathf.Max(0.001f, window) : 0;
                labAllocLast = allocated;
                double liveMB = GC.GetGCMemoryInfo().HeapSizeBytes / 1048576.0;
                labModTicks = 0; labFrames = 0; labWindowStart = now; labMaxFrameTicks = 0;
                Log(string.Format("MT-METRIC t={0:0} role={1} scene={2} peers={3} avatars={4} models={5} heapMB={6:0.0} errors={7} moderrors={8} modms={9:0.000} fps={10:0.0} modobjects={11} replay={12} mitadesync={13:0.00} player={14} phase={15}",
                    now, Hosting ? "host" : "client", ActiveSceneName.Replace(' ', '_'), AcceptedCount, avatars, models, GC.GetTotalMemory(false) / 1048576.0,
                    errorCount, modErrorCount, modMs, fps, modObjects, replayQueue.Count, MitaDesync(), LocalPlayer != null, FollowableScene(ActiveSceneName) && ActiveSceneName != "SceneMenu" ? "chapter" : "menu") + " live=" + liveMB.ToString("0.0") + " alloc=" + allocRate.ToString("0.00") + " modmax=" + modMax.ToString("0.00") + " spikes=" + SpikeReport() + " steps=" + StepReport());
            }
            TourUpdate(now);
            DumpTick(now);
        }

        // Largest distance between a client's Mita and the host's latest Mita pose (-1 when nothing to compare).
        float MitaDesync()
        {
            if (!IsClient || mitaTargets.Count == 0) return -1;
            float worst = -1;
            foreach (KeyValuePair<string, NpcState> pair in mitaTargets)
            {
                if (Time.realtimeSinceStartup - pair.Value.At > 1f) continue;
                NpcRef npc;
                if (!npcResolved.TryGetValue(pair.Key, out npc) || npc == null || npc.Root == null || !npc.Root.gameObject.activeInHierarchy) continue;
                worst = Mathf.Max(worst, Vector3.Distance(npc.Root.position, pair.Value.Position));
            }
            return worst;
        }
    }
}
