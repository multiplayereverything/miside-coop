using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // The visual novel (Scene 18, Location18_Novella) has its own dialogue box, outside Dialogue_3DText. The host
    // streams where it is (current dialogue, page, line); anyone's click advances their own copy at once and is sent to
    // the host, so anybody can read on. A client that is behind steps forward along the dialogue chain until it matches;
    // one on another branch for 3 s jumps to the host's dialogue.
    public sealed partial class TogetherRuntime
    {
        Location18_Novella novella;
        float novellaFindAt, nextNovella, novellaBehindSince = -1, novellaStepAt;
        string hostNovellaDialogue = ""; int hostNovellaPage, hostNovellaLine; float hostNovellaAt = -1;
        public int novellaSteps, novellaJumps, novellaClicks;

        Location18_Novella Novella()
        {
            if (novella != null) return novella;
            float now = Time.realtimeSinceStartup;
            if (now < novellaFindAt) return null;
            novellaFindAt = now + 2;
            novella = Object.FindObjectOfType<Location18_Novella>();
            return novella;
        }

        // Postfix of Location18_Novella.NextDialogue: a player click (not a replay).
        public static void NovellaClicked()
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || Replaying || rt.Hosting) return;
            rt.novellaClicks++;
            rt.SendToHost(Wire.Packet(Op.Novella, w => w.Write((byte)2)));
        }

        void NovellaTick(float now)
        {
            if (now < nextNovella) return;
            nextNovella = now + 0.25f;
            Location18_Novella n = Novella();
            if (n == null || n.dialoguePlay == null) return;
            if (Hosting)
            {
                string key = SceneKeys.Key(n.dialoguePlay); int page = n.indexDialogue, line = n.indexStringDialogue;
                int scene = LocalSceneHash;
                Broadcast(Wire.Packet(Op.Novella, w => { w.Write((byte)1); w.Write(scene); Wire.Text(w, key); w.Write(page); w.Write(line); }));
                return;
            }
            if (!ClientStoryLive || hostNovellaAt < 0 || now - hostNovellaAt > 2) return;
            string mine = SceneKeys.Key(n.dialoguePlay);
            int order = NovellaOrder(n, mine);
            if (order >= 0) { novellaBehindSince = -1; return; }   // level with the host, or ahead (the host gets our click)
            if (novellaBehindSince < 0) novellaBehindSince = now;
            if (order == -1 && now >= novellaStepAt)
            {
                // Behind on the same chain: one step at a time, so each line's events (sprites, sounds) still run.
                novellaStepAt = now + 0.15f;
                Replaying = true;
                try { n.NextDialogue(); novellaSteps++; } catch (Exception ex) { Log("Novella step threw: " + ex.Message); } finally { Replaying = false; }
            }
            else if (order == -2 && now - novellaBehindSince > 3)
            {
                Location18_Dialogue target = SceneKeys.Find<Location18_Dialogue>(hostNovellaDialogue);
                if (target == null) return;
                novellaBehindSince = -1;
                Replaying = true;
                try { n.PlayDialogue(target); novellaJumps++; Log("Novella: jumped to the host's dialogue " + hostNovellaDialogue); }
                catch (Exception ex) { Log("Novella jump threw: " + ex.Message); }
                finally { Replaying = false; }
            }
        }

        // 0 level, 1 ahead, -1 behind on the same chain, -2 on another branch.
        int NovellaOrder(Location18_Novella n, string mine)
        {
            if (mine == hostNovellaDialogue)
            {
                int me = n.indexDialogue * 1000 + n.indexStringDialogue, host = hostNovellaPage * 1000 + hostNovellaLine;
                return me == host ? 0 : me > host ? 1 : -1;
            }
            for (Location18_Dialogue d = n.dialoguePlay.nextDialogue; d != null; d = d.nextDialogue)
                if (SceneKeys.Key(d) == hostNovellaDialogue) return -1;
            Location18_Dialogue hostD = SceneKeys.Find<Location18_Dialogue>(hostNovellaDialogue);
            for (Location18_Dialogue d = hostD != null ? hostD.nextDialogue : null; d != null; d = d.nextDialogue)
                if (SceneKeys.Key(d) == mine) return 1;
            return -2;
        }

        void ReadNovella(BinaryReader reader, int from)
        {
            byte kind = reader.ReadByte();
            if (kind == 2)
            {
                // A client's click: the host reads on too, and its next state carries everyone along.
                if (!Hosting) return;
                Location18_Novella n = Novella();
                if (n == null) return;
                Replaying = true;
                try { n.NextDialogue(); novellaClicks++; } catch (Exception ex) { Log("Novella click threw: " + ex.Message); } finally { Replaying = false; }
                return;
            }
            int scene = reader.ReadInt32();
            hostNovellaDialogue = Wire.Text(reader, 1024); hostNovellaPage = reader.ReadInt32(); hostNovellaLine = reader.ReadInt32();
            hostNovellaAt = scene == LocalSceneHash ? Time.realtimeSinceStartup : -1;
        }

        void ClearNovella() { novella = null; novellaFindAt = 0; hostNovellaAt = -1; hostNovellaDialogue = ""; novellaBehindSince = -1; }
    }
}
