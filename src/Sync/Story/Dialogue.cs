using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Dialogue follows the host. MiSide shows each line as an active Dialogue_3DText (lines chain through nextText);
    // every machine runs its own copy of the story, so lines can drift apart. The host streams which lines are showing
    // and how long each has been up. A client starts a line the host shows and it does not, skips past lines the host
    // has finished, and measures how far behind it started each line (the dialogue lag metric).
    // Skips and choices are relayed separately (Op.SkipDialogue, Op.DialogueChoice).
    public sealed partial class TogetherRuntime
    {
        const float SkipGrace = 0.8f;   // a line the host finished is skipped here after this long
        float nextDialogue, hostLinesAt = -1, nextLineScan;
        string sentLines = "";
        readonly Dictionary<string, float> hostLines = new Dictionary<string, float>();   // key -> host's start time on our clock
        readonly Dictionary<string, float> staleSince = new Dictionary<string, float>();
        // Lines this machine shows: registered by the Dialogue_3DText.Start postfix, re-checked every 2 s.
        readonly Dictionary<IntPtr, KeyValuePair<Dialogue_3DText, float>> liveLines = new Dictionary<IntPtr, KeyValuePair<Dialogue_3DText, float>>();
        readonly Dictionary<string, float> lineStartedAt = new Dictionary<string, float>();
        readonly HashSet<string> lagMeasured = new HashSet<string>();
        public int dialogueStarted, dialogueSkipped;
        public float dialogueLagMax, dialogueLagSum; public int dialogueLagCount;

        public static void LineStarted(Dialogue_3DText line)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || line == null) return;
            float now = Time.realtimeSinceStartup;
            rt.liveLines[line.Pointer] = new KeyValuePair<Dialogue_3DText, float>(line, now);
            rt.lineStartedAt[SceneKeys.Key(line)] = now;
        }

        List<Dialogue_3DText> ActiveLines(float now)
        {
            // A line set active again does not run Start a second time; a slow rescan catches those.
            if (now >= nextLineScan)
            {
                nextLineScan = now + 2;
                foreach (Dialogue_3DText d in Object.FindObjectsOfType<Dialogue_3DText>())
                    if (d != null && d.isActiveAndEnabled && !liveLines.ContainsKey(d.Pointer)) LineStarted(d);
            }
            List<Dialogue_3DText> list = new List<Dialogue_3DText>();
            List<IntPtr> gone = null;
            foreach (KeyValuePair<IntPtr, KeyValuePair<Dialogue_3DText, float>> pair in liveLines)
            {
                Dialogue_3DText d = pair.Value.Key;
                if (d != null && d.isActiveAndEnabled) list.Add(d); else (gone ?? (gone = new List<IntPtr>())).Add(pair.Key);
            }
            if (gone != null) foreach (IntPtr p in gone) liveLines.Remove(p);
            return list;
        }

        float LineAge(Dialogue_3DText d, float now) { float at; return lineStartedAt.TryGetValue(SceneKeys.Key(d), out at) ? now - at : 0; }

        void DialogueTick(float now)
        {
            if (now < nextDialogue) return;
            nextDialogue = now + 0.2f;
            if (Hosting) { HostDialogue(now); return; }
            ClientDialogue(now);
        }

        void HostDialogue(float now)
        {
            List<Dialogue_3DText> active = ActiveLines(now);
            List<string> keys = new List<string>();
            foreach (Dialogue_3DText d in active) if (keys.Count < 16) keys.Add(SceneKeys.Key(d));
            keys.Sort(StringComparer.Ordinal);
            string joined = string.Join("|", keys);
            // On change, and every 2 s so a late client lines up too.
            if (joined == sentLines && now - hostLinesAt < 2) return;
            sentLines = joined; hostLinesAt = now;
            int scene = LocalSceneHash;
            List<float> ages = new List<float>();
            foreach (string k in keys) { float at; ages.Add(lineStartedAt.TryGetValue(k, out at) ? now - at : 0); }
            Broadcast(Wire.Packet(Op.Dialogue, w =>
            {
                w.Write(scene); w.Write((byte)keys.Count);
                for (int i = 0; i < keys.Count; i++) { Wire.Text(w, keys[i]); w.Write(ages[i]); }
            }));
        }

        void ReadDialogue(BinaryReader reader)
        {
            int scene = reader.ReadInt32();
            int count = reader.ReadByte();
            if (count > 16) throw new IOException("Too many dialogue lines.");
            float now = Time.realtimeSinceStartup;
            hostLines.Clear();
            for (int i = 0; i < count; i++) { string key = Wire.Text(reader, 1024); float age = Wire.Number(reader); hostLines[key] = now - Mathf.Clamp(age, 0, 600); }
            hostLinesAt = scene == LocalSceneHash ? now : -1;
        }

        void ClientDialogue(float now)
        {
            List<Dialogue_3DText> active = ActiveLines(now);
            if (!ClientStoryLive || replayQueue.Count > 0 || hostLinesAt < 0 || now - hostLinesAt > 3) return;
            HashSet<string> mine = new HashSet<string>();
            foreach (Dialogue_3DText d in active) mine.Add(SceneKeys.Key(d));
            foreach (KeyValuePair<string, float> host in hostLines)
            {
                string key = host.Key;
                if (mine.Contains(key))
                {
                    // How far behind the host this line started here (measured once per line).
                    float at;
                    if (!lagMeasured.Contains(key) && lineStartedAt.TryGetValue(key, out at))
                    {
                        lagMeasured.Add(key);
                        float lag = Mathf.Max(0, at - host.Value);
                        dialogueLagMax = Mathf.Max(dialogueLagMax, lag); dialogueLagSum += lag; dialogueLagCount++;
                    }
                    continue;
                }
                // A line the host shows and this machine does not: start it here.
                Dialogue_3DText line = SceneKeys.Find<Dialogue_3DText>(key);
                if (line == null || line.gameObject.activeSelf) continue;
                Replaying = true;
                try { line.gameObject.SetActive(true); dialogueStarted++; Log("Dialogue: started the host's line " + key); }
                finally { Replaying = false; }
            }
            // A line still showing here that the host has finished: skip it.
            foreach (Dialogue_3DText d in active)
            {
                string key = SceneKeys.Key(d);
                if (hostLines.ContainsKey(key)) { staleSince.Remove(key); continue; }
                float since;
                if (!staleSince.TryGetValue(key, out since)) { staleSince[key] = now; continue; }
                if (now - since < SkipGrace) continue;
                staleSince[key] = now + 3;   // one skip, then give it time to close
                Replaying = true;
                try { d.SkipDialogue(); dialogueSkipped++; Log("Dialogue: skipped a line the host finished " + key); }
                catch (Exception ex) { Log("Dialogue skip threw: " + ex.Message); }
                finally { Replaying = false; }
            }
        }

        void ClearDialogue() { hostLines.Clear(); staleSince.Clear(); liveLines.Clear(); lineStartedAt.Clear(); lagMeasured.Clear(); sentLines = ""; hostLinesAt = -1; }
    }
}
