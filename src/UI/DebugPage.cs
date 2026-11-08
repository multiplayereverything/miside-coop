using System;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // F5 debug page: what every player's game is doing right now, so a desync in real play is seen as it happens.
    // Each PC sends a status line every second (Op.Status); the host adds ping and shares the table with everyone.
    // Buttons: skip a story step a player is stuck on, bring everyone here, save a bug report on every PC.
    public sealed partial class TogetherRuntime
    {
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

        void DrawDebugPage(float scale)
        {
            ui.Begin(new Rect(470, 30, 560, 420), scale, "Debug");
            foreach (string row in statusTable.Split('\n')) ui.Text(row);
            ui.Space(6);
            if (ui.Button("Skip stuck story step" + (Hosting ? " (everyone)" : ""), 26)) DebugSkip();
            if (Hosting && ui.Button("Bring everyone here", 26)) { Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "gather"))); Tell("Bringing everyone to you."); }
            if (ui.Button("Save bug report" + (Hosting ? " (every PC)" : ""), 26)) DebugReport();
        }
    }
}
