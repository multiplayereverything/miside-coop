using System;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "hostcut" (2 players): the host runs the cutscenes. The client starts one itself; that start must become
    // a request, the host must run it, and the client must then play it from the host's command, on the host's time.
    public sealed partial class TogetherRuntime
    {
        string labHostCutKey; float labHostCutAt = -1;
        Playable_Animation labHostCutPick; int labRequestsBefore;

        // Host: the client's tour named the cutscene it will start; switch it on here too.
        void HostCutAsked(string key)
        {
            labAskedCut = key;
            Playable_Animation a = SceneKeys.Find<Playable_Animation>(key);
            if (a != null) for (Transform t = a.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        }

        // A cutscene starter (Playable_Animation) that is idle, with a short timeline.
        Playable_Animation PickCutscene()
        {
            Playable_Animation pick = null; double best = 0;
            foreach (Playable_Animation a in Cutscenes())
            {
                PlayableDirector d = a != null ? Director(a) : null;
                if (d == null || d.playableAsset == null || Playing(d)) continue;
                if (d.duration > 2 && d.duration < 120 && (pick == null || d.duration > best)) { pick = a; best = d.duration; }
            }
            return pick;
        }

        void HostCutHost(float now, string scene)
        {
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; Log("Lab: host loading " + TourChapter); LoadThroughGame(TourChapter); }
            if (scene != TourChapter) return;
            // The cutscene the client's tour asked for (its notice), running here because of the client's request.
            if (labAskedCut == null || lastClientCut != labAskedCut) return;
            Playable_Animation asked = SceneKeys.Find<Playable_Animation>(labAskedCut);
            if (asked != null && Playing(Director(asked))) CheckOnce("hostcut.ran", true, "the host runs the cutscene the client asked for: " + labAskedCut.Split('/')[^1]);
        }

        string lastClientCut, labAskedCut;   // host: the cutscene a client's request named (from the story event)

        void HostCutClient(float now, string scene)
        {
            if (scene != TourChapter || LocalPlayer == null || !ClientStoryLive) return;
            if (labHostCutAt < 0 && now - tourSceneAt > 25)
            {
                labHostCutAt = now;
                labHostCutPick = PickCutscene();
                if (labHostCutPick == null) { CheckOnce("hostcut.requested", false, "no cutscene to start in " + scene + " (" + Cutscenes().Count + " starters)"); return; }
                labRequestsBefore = cutsceneRequests;
                // Scene 1's "Day N" cutscenes are switched off until their day: switch this one on, here and on the host
                // (switching it on starts it: that start, too, must become a request).
                for (Transform t = labHostCutPick.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                string asked = SceneKeys.Key(labHostCutPick);
                SendToHost(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-hostcut:" + asked)));
            }
            if (labHostCutPick != null && labHostCutKey == null && now - labHostCutAt > 2)
            {
                PlayableDirector dir = Director(labHostCutPick);
                labHostCutKey = SceneKeys.Key(dir);
                if (!Playing(dir)) { Log("Lab: client starts " + labHostCutPick.name + " itself"); labHostCutPick.Play(); }
                CheckOnce("hostcut.requested", cutsceneRequests > labRequestsBefore, (cutsceneRequests - labRequestsBefore) + " local start(s) of " + labHostCutPick.name + " became requests to the host");
            }
            if (labHostCutKey == null) return;
            PlayableDirector mine = SceneKeys.Find<PlayableDirector>(labHostCutKey);
            if (mine != null && Playing(mine) && lastCutsceneSync.StartsWith(labHostCutKey + " @", StringComparison.Ordinal))
            {
                double hostTime = double.Parse(lastCutsceneSync.Substring(labHostCutKey.Length + 2));
                CheckOnce("hostcut.played", true, "playing from the host's command, " + Math.Abs(mine.time - hostTime).ToString("0.000") + " s from the host's time");
            }
        }
    }
}
