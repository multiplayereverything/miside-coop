using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // Desync checks and repair (docs/REMAKE-PLAN.md, Step 4). Every few seconds the host says where the story is. A client
    // compares once it has run the log that far, so normal lag never counts. The same mismatch twice in a row is a desync:
    // the client saves a report and a screenshot, and repairs. Repair runs in order, because quests destroy themselves:
    // first the log events this client had to skip, then the host's room state. If that doesn't settle it, the players are
    // told, since some state can't be rebuilt (a destroyed object, say).
    public sealed partial class TogetherRuntime
    {
        public static bool RepairOff;   // -mt-norepair: the lab's proof that the desync tour can fail
        const float CheckEvery = 5, RepairWarnAfter = 30;
        float checkSendAt, repairAt = -1;
        string lastMismatch = "";
        int mismatchCount, desyncsSeen, repairsDone, repairsAsked;
        bool desyncWarned;
        sealed class HeldCheck { public int Seq; public string Scene = "", Quests = "", Dialogue = ""; }
        HeldCheck heldCheck;
        readonly List<byte[]> logSkipped = new List<byte[]>();

        string DialoguePosition()
        {
            Tamagotchi_Dialogue d = AppDialogue();
            return d != null && d.dialogueRun != null ? d.dialogueRun.name + "#" + d.dialogueIndex : "";
        }

        void HostCheckTick(float now)
        {
            if (now < checkSendAt || AcceptedCount == 0) return;
            checkSendAt = now + CheckEvery;
            string scene = ActiveSceneName, quests = ActiveQuests(), dialogue = DialoguePosition();
            int seq = logSeq;
            Broadcast(Wire.Packet(Op.Check, w => { w.Write(seq); Wire.Text(w, scene); Wire.Text(w, quests); Wire.Text(w, dialogue); }));
        }

        void ClientCheck(BinaryReader reader)
        {
            heldCheck = new HeldCheck { Seq = reader.ReadInt32(), Scene = Wire.Text(reader, 128), Quests = Wire.Text(reader, 1024), Dialogue = Wire.Text(reader, 256) };
        }

        void ClientCheckTick(float now)
        {
            HeldCheck check = heldCheck;
            if (check == null || !ClientStoryLive || check.Scene != ActiveSceneName) return;
            if (InLogChapter && logNext < check.Seq) return;   // still running the log up to that point
            heldCheck = null;
            string mismatch = "";
            string quests = ActiveQuests();
            if (quests != check.Quests) mismatch += "quests host '" + check.Quests + "' here '" + quests + "'; ";
            // A line can legitimately be a step behind, so only the conversation counts.
            string dialogue = DialoguePosition();
            if (ConversationOf(dialogue) != ConversationOf(check.Dialogue)) mismatch += "dialogue host '" + check.Dialogue + "' here '" + dialogue + "'; ";
            if (mismatch.Length == 0)
            {
                if (lastMismatch.Length > 0) { Log("MT-DESYNC cleared after " + mismatchCount + " check(s)" + (repairAt >= 0 ? ", " + (now - repairAt).ToString("0.0") + " s after repair" : "")); if (repairAt >= 0 && !RepairOff) repairsDone++; }
                lastMismatch = ""; mismatchCount = 0; repairAt = -1; desyncWarned = false;
                return;
            }
            mismatchCount++;
            bool same = mismatch == lastMismatch;
            lastMismatch = mismatch;
            if (!same) { if (mismatchCount > 1) mismatchCount = 1; return; }
            if (repairAt < 0)
            {
                desyncsSeen++;
                Log("MT-DESYNC " + mismatch);
                SaveDesyncReport(mismatch);
                repairAt = now;
                if (!RepairOff) RepairNow(check.Quests);
                return;
            }
            if (!desyncWarned && now - repairAt > RepairWarnAfter)
            {
                desyncWarned = true;
                Log("MT-DESYNC repair didn't settle it: " + mismatch);
                Tell("Your game is out of step with the host's. If something looks wrong, leave and rejoin at the next chapter.");
            }
        }

        static string ConversationOf(string position) { int hash = position.LastIndexOf('#'); return hash < 0 ? position : position.Substring(0, hash); }

        void SaveDesyncReport(string mismatch)
        {
            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Directory.CreateDirectory(SessionLogDir);
                File.WriteAllText(Path.Combine(SessionLogDir, "desync-" + stamp + ".txt"), ActiveSceneName + "\nlog position " + logNext + "\n" + mismatch + "\n");
                ScreenCapture.CaptureScreenshot(Path.Combine(SessionLogDir, "desync-" + stamp + ".png"));
            }
            catch (Exception ex) { Log("MT-DESYNC report failed: " + ex.Message); }
        }

        // The host's list of live quests. A quest that was destroyed here can't come back; the warning covers that.
        void MatchQuests(string hostQuests)
        {
            GameObject quests = SceneObject("Quests");
            if (quests == null) return;
            HashSet<string> on = new HashSet<string>(hostQuests.Split('|'));
            for (int i = 0; i < quests.transform.childCount; i++)
            {
                Transform q = quests.transform.GetChild(i);
                bool want = on.Contains(q.name);
                if (q.gameObject.activeSelf == want) continue;
                q.gameObject.SetActive(want);
                Log("MT-DESYNC switched " + q.name + (want ? " on" : " off") + " to match the host");
            }
        }

        void RepairNow(string hostQuests)
        {
            repairsAsked++;
            int retried = 0, stillOut = 0;
            foreach (byte[] packet in logSkipped.ToArray())
            {
                using (BinaryReader reader = new BinaryReader(new MemoryStream(packet)))
                {
                    Op op = (Op)reader.ReadByte();
                    reader.ReadInt32();
                    string key = Wire.Text(reader, 1024);
                    int arg = reader.ReadInt32();
                    retryingStory = true;
                    try { ApplyStory(op, key, arg); } finally { retryingStory = false; }
                    if (storyMissing) { stillOut++; continue; }
                    logSkipped.Remove(packet);
                    retried++;
                }
            }
            Log("MT-DESYNC repairing: reran " + retried + " skipped event(s), " + stillOut + " still can't run; asking the host for its rooms");
            MatchQuests(hostQuests);
            SendToHost(Wire.Packet(Op.Repair, null));
        }

        void HostRepair(Peer peer)
        {
            Log("MT-DESYNC " + peer.Name + " asked for repair");
            SendWorldStateTo(peer.Id);
        }

        void ResetStoryCheck()
        {
            heldCheck = null; lastMismatch = ""; mismatchCount = 0; repairAt = -1; desyncWarned = false; checkSendAt = 0;
            logSkipped.Clear();
        }
    }
}
