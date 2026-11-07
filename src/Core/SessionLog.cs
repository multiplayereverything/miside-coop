using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace MiSideTogether
{
    // Every real session writes BepInEx/MiSideTogether-logs/session-<time>-<name>.log, and clients stream theirs to the
    // host as it happens (session-<time>-from-<name>.log there), so a crash or the host quitting first loses little.
    // Lines: everything Log() says, game errors (MT-ERROR) and MT-STATE every 2 s. BepInEx's own log is overwritten on
    // each launch and holds no sync state, which left real playtests with nothing to read.
    public sealed partial class TogetherRuntime
    {
        const int SessionLogsKept = 60, MaxUnsentLines = 20000, MaxErrorLines = 500, RepeatsLogged = 5;
        const long MaxPeerLogBytes = 64 * 1024 * 1024;
        const int LinePart = 1100, ChunkBytes = 3500, ChunksPerSend = 8;

        static readonly object sessionGate = new object();
        static StreamWriter sessionLog;
        static string sessionStamp;
        static readonly Queue<string> sessionUnsent = new Queue<string>();
        static readonly Dictionary<string, int> sessionErrorCounts = new Dictionary<string, int>();
        static int sessionErrorLines;
        static string profilePath;
        static bool sessionNoSend;
        float nextSessionSend;

        sealed class PeerLog { public StreamWriter Writer; public long Bytes; }
        readonly Dictionary<int, PeerLog> peerLogs = new Dictionary<int, PeerLog>();

        static string SessionLogDir { get { return Path.Combine(Root, "BepInEx", "MiSideTogether-logs"); } }
        static string FileSafe(string name) { StringBuilder s = new StringBuilder(); foreach (char c in name ?? "") s.Append(char.IsLetterOrDigit(c) ? c : '_'); return s.ToString(); }

        void InstallSessionLog()
        {
            profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Action<string> previous = Log;
            Log = message => { previous(message); SessionLine(message); };
            try { Application.add_logMessageReceivedThreaded(DelegateSupport.ConvertDelegate<Application.LogCallback>(new Action<string, string, LogType>(SessionUnityLog))); }
            catch (Exception ex) { Log("Session log: Unity log hook unavailable: " + ex.Message); }
        }

        // Your name and folders stay on this PC: logs go to the host.
        static string Scrub(string line)
        {
            return string.IsNullOrEmpty(profilePath) ? line : line.Replace(profilePath, "~", StringComparison.OrdinalIgnoreCase);
        }

        static void SessionLine(string line)
        {
            lock (sessionGate)
            {
                if (sessionLog == null) return;
                try
                {
                    string text = DateTime.Now.ToString("HH:mm:ss.fff") + " " + Scrub(line ?? "");
                    sessionLog.WriteLine(text);
                    for (int i = 0; i < text.Length; i += LinePart)
                    {
                        sessionUnsent.Enqueue(text.Substring(i, Math.Min(LinePart, text.Length - i)));
                        if (sessionUnsent.Count > MaxUnsentLines) sessionUnsent.Dequeue();
                    }
                }
                catch { CloseSessionWriter(); }
            }
        }

        static void SessionUnityLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            string at = "";
            if (stackTrace != null) foreach (string line in stackTrace.Split('\n')) if (line.Trim().Length > 0) { at = line.Trim(); break; }
            string key = (condition ?? "").Replace('\n', ' ') + " | " + at;
            // Some game errors repeat every frame: the first few of each, then a count at the end of the session.
            lock (sessionGate)
            {
                int seen;
                sessionErrorCounts.TryGetValue(key, out seen);
                sessionErrorCounts[key] = seen + 1;
                if (seen >= RepeatsLogged || sessionErrorLines >= MaxErrorLines) return;
                sessionErrorLines++;
            }
            SessionLine("MT-ERROR " + key);
        }

        void OpenSessionLog()
        {
            lock (sessionGate)
            {
                // A host migration carries on in the same file.
                if (sessionLog != null) return;
                try
                {
                    Directory.CreateDirectory(SessionLogDir);
                    PruneSessionLogs();
                    sessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    FileStream file = new FileStream(Path.Combine(SessionLogDir, "session-" + sessionStamp + "-" + FileSafe(Name) + ".log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    sessionLog = new StreamWriter(file) { AutoFlush = true };
                    sessionUnsent.Clear(); sessionErrorCounts.Clear(); sessionErrorLines = 0;
                }
                catch (Exception ex) { sessionLog = null; Log("Session log unavailable: " + ex.Message); return; }
            }
            Log("MT-SESSION " + DescribeThisPc());
        }

        // What can make two copies differ (docs/GAME-STUDY.md), so two logs can be compared from their first line.
        string DescribeThisPc()
        {
            StringBuilder s = new StringBuilder("mod=" + ModVersion + " protocol=" + Wire.Version + " role=" + (Hosting ? "host" : "client") + " name=" + Name + " game=" + gameHash);
            Describe(s, "language", () => GlobalGame.Language);
            Describe(s, "voice", () => GlobalGame.LanguageVoice);
            Describe(s, "quality", () => GlobalGame.qualityWorld.ToString());
            Describe(s, "shadow", () => GlobalGame.shadow.ToString());
            Describe(s, "vramMB", () => SystemInfo.graphicsMemorySize.ToString());
            Describe(s, "fps", () => Application.targetFrameRate.ToString());
            Describe(s, "fixedDt", () => Time.fixedDeltaTime.ToString("0.#####"));
            Describe(s, "halloween", () => GlobalGame.halloween.ToString());
            Describe(s, "christmas", () => GlobalGame.christmas.ToString());
            Describe(s, "screen", () => Screen.width + "x" + Screen.height);
            return s.ToString();
        }

        static void Describe(StringBuilder s, string name, Func<string> read)
        {
            string value;
            try { value = read(); } catch (Exception ex) { value = "?" + ex.GetType().Name; }
            s.Append(' ').Append(name).Append('=').Append(value);
        }

        static void PruneSessionLogs()
        {
            FileInfo[] files = new DirectoryInfo(SessionLogDir).GetFiles("session-*.log");
            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (int i = SessionLogsKept; i < files.Length; i++) { try { files[i].Delete(); } catch { } }
        }

        static void CloseSessionWriter()
        {
            try { if (sessionLog != null) sessionLog.Dispose(); } catch { }
            sessionLog = null;
        }

        void CloseSessionLog()
        {
            foreach (int id in new List<int>(peerLogs.Keys)) ClosePeerLog(id);
            lock (sessionGate)
            {
                if (sessionLog == null) return;
                foreach (KeyValuePair<string, int> pair in sessionErrorCounts)
                    if (pair.Value > RepeatsLogged) { try { sessionLog.WriteLine("MT-ERROR-COUNT " + pair.Value + " " + Scrub(pair.Key)); } catch { } }
                CloseSessionWriter();
                sessionUnsent.Clear();
            }
        }

        void SessionLogTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextSessionSend) return;
            nextSessionSend = now + 2;
            SendSessionLines(ChunksPerSend);
        }

        void SendSessionLines(int chunks)
        {
            if (sessionNoSend || Network == null) return;
            if (Hosting) { lock (sessionGate) sessionUnsent.Clear(); return; }
            if (!IsClient || self <= 0) return;
            for (int i = 0; i < chunks; i++)
            {
                StringBuilder text = new StringBuilder();
                int bytes = 0;
                lock (sessionGate)
                {
                    while (sessionUnsent.Count > 0)
                    {
                        int size = Encoding.UTF8.GetByteCount(sessionUnsent.Peek()) + 1;
                        if (bytes + size > ChunkBytes) break;
                        text.Append(sessionUnsent.Dequeue()).Append('\n');
                        bytes += size;
                    }
                }
                if (bytes == 0) return;
                string chunk = text.ToString();
                SendToHost(Wire.Packet(Op.SessionLog, w => Wire.Text(w, chunk)));
            }
        }

        void HostSessionLog(Peer peer, BinaryReader reader)
        {
            string text = Wire.Text(reader, 4096);
            PeerLog log;
            if (!peerLogs.TryGetValue(peer.Id, out log))
            {
                log = new PeerLog();
                peerLogs[peer.Id] = log;
                try
                {
                    Directory.CreateDirectory(SessionLogDir);
                    FileStream file = new FileStream(Path.Combine(SessionLogDir, "session-" + (sessionStamp ?? DateTime.Now.ToString("yyyyMMdd-HHmmss")) + "-from-" + FileSafe(peer.Name) + "-" + peer.Id + ".log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    log.Writer = new StreamWriter(file) { AutoFlush = true };
                }
                catch (Exception ex) { Log("Could not save " + peer.Name + "'s session log: " + ex.Message); }
            }
            if (log.Writer == null || log.Bytes > MaxPeerLogBytes) return;
            log.Bytes += text.Length;
            try { log.Writer.Write(text); } catch { ClosePeerLog(peer.Id); }
        }

        void ClosePeerLog(int id)
        {
            PeerLog log;
            if (!peerLogs.TryGetValue(id, out log)) return;
            peerLogs.Remove(id);
            try { if (log.Writer != null) log.Writer.Dispose(); } catch { }
        }
    }
}
