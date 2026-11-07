using System;
using System.IO;

namespace MiSideTogether
{
    // Lab tour "sessionlog" (2 players): the normal session log works for real players (Core/SessionLog.cs).
    // The client logs a marker holding its profile folder plus a game error; the host must end up with all of it, its
    // own log, and no profile path. Checked while the client still runs, so a later crash could not have lost it.
    public sealed partial class TogetherRuntime
    {
        const string SessionMark = "MT-SESSION-MARK", SessionError = "MT-SESSION-UNITYERR";
        float sessionTourAt = -1, sessionTourNext;
        bool sessionMarked;

        static string ReadShared(string path)
        {
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(file)) return reader.ReadToEnd();
        }

        void SessionLogTour(float now)
        {
            if (!Connected || (IsClient && self <= 0) || (Hosting && AcceptedCount == 0)) return;
            if (sessionTourAt < 0) sessionTourAt = now;
            if (now < sessionTourNext) return;
            sessionTourNext = now + 2;
            bool late = now - sessionTourAt > 150;
            if (IsClient) SessionTourClient(now, late); else SessionTourHost(late);
        }

        void SessionTourClient(float now, bool late)
        {
            if (!sessionMarked && now - sessionTourAt > 5)
            {
                sessionMarked = true;
                Log(SessionMark + " " + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                UnityEngine.Debug.LogError(SessionError);
            }
            string own = "";
            foreach (string path in Directory.GetFiles(SessionLogDir, "session-" + sessionStamp + "-*.log")) if (!path.Contains("-from-")) own += ReadShared(path);
            if (own.Contains(SessionMark) && own.Contains("MT-SESSION ")) CheckOnce("sessionlog.file", true, "the client's own file has the session header and the marker");
            else if (late) CheckOnce("sessionlog.file", false, "no marker in the client's own file");
        }

        void SessionTourHost(bool late)
        {
            string own = "", received = "";
            // This run's files only: an earlier run's log must not pass the check.
            foreach (string path in Directory.GetFiles(SessionLogDir, "session-" + sessionStamp + "-*.log"))
            {
                if (path.Contains("-from-")) received += ReadShared(path); else own += ReadShared(path);
            }
            if (own.Contains("MT-SESSION ") && own.Contains("MT-STATE")) CheckOnce("sessionlog.own", true, "the host's own file has the session header and state lines");
            else if (late) CheckOnce("sessionlog.own", false, "host file incomplete (" + own.Length + " chars)");
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            bool complete = received.Contains("MT-SESSION ") && received.Contains(SessionMark) && received.Contains("MT-ERROR " + SessionError) && received.Contains("MT-STATE");
            if (complete) CheckOnce("sessionlog.received", received.IndexOf(profile, StringComparison.OrdinalIgnoreCase) < 0, "the client's header, marker, game error and state reached the host; profile path scrubbed: " + (received.IndexOf(profile, StringComparison.OrdinalIgnoreCase) < 0));
            else if (late) CheckOnce("sessionlog.received", false, "host received " + received.Length + " chars from the client");
        }
    }
}
