using System;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // F5 debug page: what every player's game is doing right now, so a desync in real play is seen as it happens.
    // Each PC sends a status line every second (Op.Status); the host adds ping and shares the table with everyone.
    // Buttons: skip a story step a player is stuck on, bring everyone here, pass the level, save a bug report on every PC.
    public sealed partial class TogetherRuntime
    {
        // Every chapter a load can start cold, in the order to pass them. Scene 19 (Glasses) is left out: the game itself cannot
        // start it cold, even from the menu (World.Update NRE every frame on the host too, run of 2026-10-22); players only
        // reach it through the story, where Op.SceneLoad replays the game's own transition.
        public static readonly string[] Chapters =
        {
            "Scene 1 - RealRoom", "Scene 2 - InGame", "Scene 3 - WeTogether", "Scene 4 - StartSecret", "Scene 5 - StartHorror",
            "Scene 6 - BasementFirst", "Scene 7 - Backrooms", "Scene 8 - ReRooms", "Scene 9 - ChibiMita", "Scene 10 - ManekenWorld",
            "Scene 11 - Backrooms", "Scene 12 - Freak", "Scene 13 - HelloCore", "Scene 14 - MobilePlayer", "Scene 15 - BasementAndDeath",
            "Scene 16 - TheEnd", "Scene 17 - Dreamer", "Scene 20 - FightMita", "Scene 18 - 2D"
        };

        string statusTable = "";
        float nextStatus;
        bool debugOpen;

        string StatusLine()
        {
            float now = Time.realtimeSinceStartup;
            if (cachedController == null) cachedController = UnityEngine.Object.FindObjectOfType<GameController>();
            int keys = cachedController != null && cachedController.inventoryKeysCase != null ? cachedController.inventoryKeysCase.Count : 0;
            string line = ActiveSceneName + " | " + (ActiveQuests().Length > 0 ? ActiveQuests() : "-") + " | story " + (Hosting ? logSeq : logNext) + " | key items " + keys;
            if (logBlockedSince >= 0) line += " | waiting " + (now - logBlockedSince).ToString("0") + " s on " + logWaitingOn;
            return line;
        }

        void DebugTick(float now)
        {
            if (now < nextStatus) return;
            nextStatus = now + 1;
            if (IsClient) { string line = StatusLine(); SendToHost(Wire.Packet(Op.Status, w => Wire.Text(w, line))); return; }
            System.Text.StringBuilder table = new System.Text.StringBuilder(Name + " (host): " + StatusLine());
            foreach (Peer p in peers.Values)
                if (p.Accepted) table.Append("\n" + p.Name + " (" + p.PingMs.ToString("0") + " ms): " + (p.Status.Length > 0 ? p.Status : "no status yet"));
            statusTable = table.ToString();
            string all = statusTable;
            Broadcast(Wire.Packet(Op.Status, w => Wire.Text(w, all)));
        }

        void HostStatus(Peer peer, BinaryReader reader) { peer.Status = Wire.Text(reader, 600); }
        void ClientStatus(BinaryReader reader) { statusTable = Wire.Text(reader, 3000); }

        string DebugTable() { return statusTable; }

        // A step this PC is held on is let go now instead of after the log's own limit. The host lets go of everyone's.
        void DebugSkip()
        {
            if (Hosting) Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "dbg-skip")));
            SkipStuckStep();
        }

        void SkipStuckStep()
        {
            if (logBlockedSince < 0) return;
            Log("MT-LOG skip pressed at " + logNext + " (" + logWaitingOn + ")");
            logBlockedSince = Time.realtimeSinceStartup - LogStuckSeconds;
            logRetryAt = 0;
        }

        // Every PC writes a report beside its session log; clients' logs already stream to the host.
        void DebugReport()
        {
            if (Hosting) Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "dbg-report")));
            SaveReport("report", "bug report\n" + statusTable);
            Tell("Bug report saved in " + SessionLogDir + ". Send the host's MiSideTogether-logs folder.");
        }

        // Host: everyone goes on to the next chapter, by the same load a chapter picked from the menu uses; clients follow Op.Scene.
        static string NextLevel(string scene)
        {
            int i = Array.IndexOf(Chapters, scene);
            return i >= 0 && i + 1 < Chapters.Length ? Chapters[i + 1] : null;
        }

        void DebugPassLevel()
        {
            string next = NextLevel(ActiveSceneName);
            if (!Hosting || next == null) return;
            Log("Debug: passing the level, next " + next);
            LoadThroughGame(next);
        }

        void DrawDebugPage(float scale)
        {
            ui.Begin(new Rect(470, 30, 560, 420), scale, "Debug");
            foreach (string row in statusTable.Split('\n')) ui.Text(row);
            ui.Space(6);
            if (ui.Button("Skip stuck story step" + (Hosting ? " (everyone)" : ""), 26)) DebugSkip();
            if (Hosting && ui.Button("Bring everyone here", 26)) { Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "gather"))); Tell("Bringing everyone to you."); }
            string next = Hosting ? NextLevel(ActiveSceneName) : null;
            if (next != null && ui.Button("Pass this level (everyone goes to " + next + ")", 26)) DebugPassLevel();
            if (ui.Button("Save bug report" + (Hosting ? " (every PC)" : ""), 26)) DebugReport();
        }
    }
}
