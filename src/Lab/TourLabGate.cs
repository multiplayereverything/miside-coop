using System;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // Lab tour "labgate" (2 players, Scene 1): the host runs with -mt-labnotices-off, which makes it treat lab notices
    // the way a released build must. The client sends the notice that switches a cutscene's object on at the host.
    // Lab copies share a PC, so the cutscene's name travels in a file.
    //   labgate.ignored@host: the object the notice named is still switched off on the host
    public sealed partial class TogetherRuntime
    {
        public static bool LabNoticesOff;
        float labGateAt = -1;

        static string LabGateFile { get { return Path.Combine(Application.persistentDataPath, "MiSideTogether", "labgate.txt"); } }

        void LabGateTour(float now, string scene)
        {
            if (Hosting)
            {
                if (!tourStarted && AcceptedCount > 0 && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; if (File.Exists(LabGateFile)) File.Delete(LabGateFile); LoadThroughGame(TourChapter); return; }
                if (scene != TourChapter || !File.Exists(LabGateFile)) return;
                if (labGateAt < 0) labGateAt = now;
                if (now - labGateAt < 5) return;
                string key = ReadShared(LabGateFile);
                Playable_Animation a = SceneKeys.Find<Playable_Animation>(key);
                bool off = a != null && !a.gameObject.activeInHierarchy;
                CheckOnce("labgate.ignored", off, a == null ? "not found here: " + key : (off ? "still off: " : "a player's notice switched it on: ") + key.Split('/')[^1]);
                return;
            }
            if (!scene.StartsWith("Scene ", StringComparison.Ordinal) || LocalPlayer == null || labGateAt > 0 || now - tourSceneAt < 25) return;
            labGateAt = now;
            foreach (Playable_Animation a in Cutscenes())
            {
                if (a == null || a.gameObject.activeInHierarchy) continue;
                string key = SceneKeys.Key(a);
                Directory.CreateDirectory(Path.GetDirectoryName(LabGateFile));
                File.WriteAllText(LabGateFile, key);
                SendToHost(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-hostcut:" + key)));
                Log("Lab: client sent the host a lab notice for " + key);
                return;
            }
            Log("Lab: no switched-off cutscene in " + scene);
        }
    }
}
