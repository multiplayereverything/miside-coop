using UnityEngine;

namespace MiSideTogether
{
    // Lab tour "desync" (2 players, Scene 1): once both copies are on the same quest, the client's copy of it is switched off
    // behind the mod's back. The check must notice, and the repair must bring it back. With -mt-norepair desync.repaired fails.
    public sealed partial class TogetherRuntime
    {
        GameObject labBrokenQuest;
        float labBrokeAt = -1;

        void DesyncTour(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (!Connected) return;
            if (Hosting)
            {
                if (AcceptedCount == 0) return;
                if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 1 - RealRoom"); }
                if (scene == "Scene 1 - RealRoom") CheckOnce("chapter.loaded", true, scene);
                return;
            }
            if (scene != "Scene 1 - RealRoom") return;
            if (labBrokeAt < 0)
            {
                if (now - tourSceneAt < 40 || mismatchCount > 0) return;
                GameObject quests = SceneObject("Quests");
                if (quests == null) return;
                for (int i = 0; i < quests.transform.childCount; i++)
                {
                    Transform q = quests.transform.GetChild(i);
                    if (!q.gameObject.activeSelf) continue;
                    labBrokenQuest = q.gameObject;
                    labBrokeAt = now;
                    q.gameObject.SetActive(false);
                    Log("Lab: client broke its copy of " + q.name);
                    return;
                }
                return;
            }
            if (desyncsSeen > 0) CheckOnce("desync.detected", true, "noticed " + (now - labBrokeAt).ToString("0.0") + " s after the break: " + lastMismatch);
            else if (now - labBrokeAt > 40) CheckOnce("desync.detected", false, "not noticed in 40 s");
            if (labBrokenQuest != null && labBrokenQuest.activeSelf && repairsDone > 0) CheckOnce("desync.repaired", true, "the quest is back on and the check is clean, " + (now - labBrokeAt).ToString("0.0") + " s after the break");
            else if (now - labBrokeAt > 60) CheckOnce("desync.repaired", false, "still broken 60 s later; repairs asked " + repairsAsked + ", quests here '" + ActiveQuests() + "'");
        }
    }
}
