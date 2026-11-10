using System;
using UnityEngine;

namespace MiSideTogether
{
    // Lab tour "novellarace" (2 players, Scene 18, 300 ms of lag each way): the host and the client click the same line
    // of the visual novel before either hears of the other's click. The story must read on by one click, not two.
    //   novellarace.moved@host: the host's own click read on
    //   novellarace.once@host:  6 s later the host is where its own click left it
    public sealed partial class TogetherRuntime
    {
        float labNovellaRaceDue = -1, labNovellaRaceAt = -1;
        string labNovellaRace0, labNovellaRace1;

        void NovellaRaceTour(float now, string scene)
        {
            if (!Connected || !Hosting) return;
            if (!tourStarted && AcceptedCount > 0 && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame(TourChapter); return; }
            Location18_Novella n = Novella();
            if (scene != TourChapter || n == null || n.dialoguePlay == null || now - tourSceneAt < 35) return;
            if (labNovellaRaceDue < 0)
            {
                // The client clicks when this arrives (300 ms); the host 150 ms later, before the client's click reaches it.
                labNovellaRaceDue = now + 0.45f;
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-nrace")));
                return;
            }
            if (labNovellaRaceAt < 0)
            {
                if (now < labNovellaRaceDue) return;
                labNovellaRaceAt = now;
                labNovellaRace0 = NovellaPos(n);
                try { n.NextDialogue(); } catch (Exception ex) { Log("Lab: NextDialogue threw: " + ex.Message); }
                labNovellaRace1 = NovellaPos(n);
                Log("Lab: host clicks at " + labNovellaRace0 + ", now " + labNovellaRace1);
                return;
            }
            if (now - labNovellaRaceAt < 6) return;
            CheckOnce("novellarace.moved", labNovellaRace1 != labNovellaRace0, "from " + labNovellaRace0 + " to " + labNovellaRace1);
            string end = NovellaPos(n);
            CheckOnce("novellarace.once", end == labNovellaRace1, end == labNovellaRace1 ? "still at " + end : "the client's click on the same line read on again: " + labNovellaRace1 + " -> " + end);
        }

        void NovellaRaceNotice()
        {
            Location18_Novella n = Novella();
            if (n == null || n.dialoguePlay == null) { Log("Lab: no novella here to click"); return; }
            string before = NovellaPos(n);
            try { n.NextDialogue(); } catch (Exception ex) { Log("Lab: NextDialogue threw: " + ex.Message); }
            Log("Lab: client clicks at " + before + ", now " + NovellaPos(n));
        }
    }
}
