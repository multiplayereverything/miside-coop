using System;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // Lab tour "debug" (2 players, Scene 2): the F5 debug page (UI/DebugPage.cs).
    //   debug.rows:    (host) the page lists the client with its chapter
    //   debug.waiting: (host) a client held on a story step shows as waiting on it
    //   debug.skip:    (client) the host's "Skip stuck step" moves it on in under 3 s (the log's own limit is 12)
    //   debug.refused: (client) a press the host can't run is answered with a notice, not silence
    //   debug.report:  (client) the host's "Save bug report" writes a report on this PC too
    public sealed partial class TogetherRuntime
    {
        int labDebugStep;
        float labDebugAt, labDebugBlockedAt = -1;
        const string LabDebugMissing = "Scene 2 - InGame/World#0/MiSideTogether lab missing#0";

        void DebugTour(float now, string scene)
        {
            // The client watches from early on: the host's step can come before the client's own 25 s.
            if (scene != "Scene 2 - InGame" || LocalPlayer == null || now - tourSceneAt < (Hosting ? 25 : 10) || now < labDebugAt) return;
            labDebugAt = now + 0.5f;
            if (Hosting)
            {
                string table = DebugTable();
                switch (labDebugStep)
                {
                    case 0:
                        if (AcceptedCount == 0) return;
                        if (table.Contains("C1") && table.Contains("Scene 2")) { CheckOnce("debug.rows", true, table.Replace("\n", " / ")); labDebugStep = 1; }
                        else if (now - tourSceneAt > 60) { CheckOnce("debug.rows", false, "page: " + table.Replace("\n", " / ")); labDebugStep = 9; }
                        return;
                    case 1:
                        // A story step the client can't run: it holds there, as the doorway trigger did.
                        Log("Lab: debug logs a step the client can't run");
                        HostLog(Op.Interact, LabDebugMissing, 0);
                        labDebugStep = 2; labDebugBlockedAt = now;
                        return;
                    case 2:
                        if (table.Contains("waiting")) { CheckOnce("debug.waiting", true, table.Replace("\n", " / ")); DebugSkip(); labDebugStep = 3; labDebugBlockedAt = now; }
                        else if (now - labDebugBlockedAt > 8) { CheckOnce("debug.waiting", false, "page: " + table.Replace("\n", " / ")); labDebugStep = 9; }
                        return;
                    case 3:
                        if (now - labDebugBlockedAt < 8) return;
                        DebugReport();
                        SetPanel(true); debugOpen = true;
                        labDebugStep = 4;
                        return;
                    case 4:
                        LabShot("debug-page");
                        labDebugStep = 5;
                        return;
                }
                return;
            }
            switch (labDebugStep)
            {
                case 0:
                    if (logBlockedSince >= 0) { labDebugBlockedAt = now; labDebugStep = 1; }
                    return;
                case 1:
                    if (logBlockedSince < 0) { float held = now - labDebugBlockedAt; CheckOnce("debug.skip", held < 3, "moved on after " + held.ToString("0.0") + " s"); labDebugStep = 2; }
                    else if (now - labDebugBlockedAt > 10) { CheckOnce("debug.skip", false, "still held after 10 s"); labDebugStep = 2; }
                    return;
                case 2:
                    // A stale press: the host has no such object, so it can't run it.
                    Log("Lab: debug client asks for a press the host can't run");
                    SendToHost(Wire.Packet(Op.Interact, w => { Wire.Text(w, LabDebugMissing); w.Write(0); }));
                    labDebugStep = 3; labDebugBlockedAt = now;
                    return;
                case 3:
                    if (messages.Exists(m => m.Contains("couldn't"))) { CheckOnce("debug.refused", true, string.Join(" / ", messages)); labDebugStep = 4; }
                    else if (now - labDebugBlockedAt > 10) { CheckOnce("debug.refused", false, "messages: " + string.Join(" / ", messages)); labDebugStep = 4; }
                    return;
                case 4:
                    bool saved = Directory.Exists(SessionLogDir) && Directory.GetFiles(SessionLogDir, "report-*.txt").Length > 0;
                    if (saved || now - labDebugBlockedAt > 25) { CheckOnce("debug.report", saved, saved ? "report written in " + SessionLogDir : "no report-*.txt in " + SessionLogDir); labDebugStep = 5; }
                    return;
            }
        }
    }
}
