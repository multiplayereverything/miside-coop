using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MiSideTogether
{
    // Lab tour "samestart" (2 players, Step 2b): the client starts with deliberately different settings, story flags and an
    // extra save. Once the chapter is up, both copies must match the host: flags, the set of objects in the scene, and the
    // save folder. Photo mode must refuse to open. Lab copies share a PC, so the host's state travels in a file, not on the
    // wire: with -mt-norules the comparison still works and must fail.
    public sealed partial class TogetherRuntime
    {
        const string ClientOnlySave = "zz-mt-client-only";
        const float SameStartSampleAt = 3, SameStartCompareAt = 10;
        bool sameStartDiffDone, sameStartWritten, sameStartCompared, sameStartPhotoTried;
        float sameStartPhotoAt;

        static string SameStartFile { get { return Path.Combine(Application.persistentDataPath, "MiSideTogether", "samestart-host.txt"); } }

        // Saves the host mirrors to clients. The lab's shared folder holds a "client's own" save the host must not claim.
        static bool SaveMirrored(string name) { return !(LabMode && name == ClientOnlySave); }

        // True once the join may go ahead: the differences are in place first, on top of what the options loaded.
        bool SameStartReadyToJoin()
        {
            if (labTour != "samestart") return true;
            if (sameStartDiffDone) return true;
            if (ActiveSceneName != "SceneMenu" || Time.realtimeSinceStartup < 8) return false;
            sameStartDiffDone = true;
            GlobalGame.LanguageVoice = GlobalGame.LanguageVoice == "Russian" ? "English" : "Russian";
            GlobalGame.halloween = !GlobalGame.halloween; GlobalGame.christmas = !GlobalGame.christmas;
            Location34_Glasses.glassesMita = true; Location4ChangeSide.trysous = true; Location4ChangeSide.playconsole = true;
            Time.fixedDeltaTime = 0.005f;
            Directory.CreateDirectory(SaveDir);
            File.WriteAllText(Path.Combine(SaveDir, ClientOnlySave), "client");
            Log("Lab: client differs before joining: voice=" + GlobalGame.LanguageVoice + " fixedDt=" + Time.fixedDeltaTime);
            return true;
        }

        void SameStartTick(float now, string scene)
        {
            if (!Connected) return;
            if (Hosting)
            {
                if (AcceptedCount == 0) return;
                if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6)
                {
                    tourStarted = true;
                    Log("Lab: host loading " + TourChapter);
                    if (File.Exists(SameStartFile)) File.Delete(SameStartFile);
                    LoadThroughGame(TourChapter);
                }
            }
            if (scene != TourChapter || now - tourSceneAt < SameStartSampleAt) return;
            if (Hosting)
            {
                if (sameStartWritten) return;
                sameStartWritten = true;
                List<string> objects = SceneObjectList(scene);
                string flags = SameStartFlags();
                Directory.CreateDirectory(Path.GetDirectoryName(SameStartFile));
                File.WriteAllText(SameStartFile, flags + "\n" + string.Join("\n", objects));
                CheckOnce("samestart.sent", true, objects.Count + " objects, " + flags);
                return;
            }
            SameStartClient(now, scene);
        }

        void SameStartClient(float now, string scene)
        {
            if (!sameStartPhotoTried)
            {
                sameStartPhotoTried = true;
                sameStartPhotoAt = now;
                sameStartSnapshot = SceneObjectList(scene);
                sameStartFlagsAt = SameStartFlags();
                GameController controller = UnityEngine.Object.FindObjectOfType<GameController>();
                if (controller != null) controller.PhotomodeActive(true); else Log("Lab: no GameController to open photo mode on");
            }
            if (now - sameStartPhotoAt > 2)
            {
                bool opened = UnityEngine.Object.FindObjectOfType<PhotoModeMain>() != null;
                CheckOnce("samestart.photo", !opened, opened ? "photo mode opened in a session" : "photo mode stayed closed");
                bool extra = File.Exists(Path.Combine(SaveDir, ClientOnlySave));
                CheckOnce("samestart.saves", !extra, extra ? "the client's own save is still in the save folder" : "the client's own save was set aside");
            }
            if (sameStartCompared || now - tourSceneAt < SameStartCompareAt || !File.Exists(SameStartFile)) return;
            sameStartCompared = true;
            string[] lines = ReadShared(SameStartFile).Split('\n');
            CheckOnce("samestart.flags", lines[0] == sameStartFlagsAt, "host: " + lines[0] + " | client: " + sameStartFlagsAt);
            CompareObjects(lines);
            File.Delete(SameStartFile);
        }

        List<string> sameStartSnapshot;
        string sameStartFlagsAt = "";

        void CompareObjects(string[] hostLines)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            for (int i = 1; i < hostLines.Length; i++) { int n; counts.TryGetValue(hostLines[i], out n); counts[hostLines[i]] = n + 1; }
            List<string> onlyClient = new List<string>();
            foreach (string path in sameStartSnapshot)
            {
                int n;
                if (counts.TryGetValue(path, out n) && n > 0) counts[path] = n - 1; else onlyClient.Add(path);
            }
            List<string> onlyHost = new List<string>();
            foreach (KeyValuePair<string, int> pair in counts) for (int i = 0; i < pair.Value; i++) onlyHost.Add(pair.Key);
            string detail = (hostLines.Length - 1) + " host objects, " + sameStartSnapshot.Count + " client; only host " + onlyHost.Count + ", only client " + onlyClient.Count;
            for (int i = 0; i < onlyHost.Count && i < 8; i++) Log("MT-SAMESTART only host: " + onlyHost[i]);
            for (int i = 0; i < onlyClient.Count && i < 8; i++) Log("MT-SAMESTART only client: " + onlyClient[i]);
            CheckOnce("samestart.objects", onlyHost.Count == 0 && onlyClient.Count == 0, detail);
        }

        static string SameStartFlags()
        {
            return "voice=" + GlobalGame.LanguageVoice + " fixedDt=" + Time.fixedDeltaTime.ToString("0.0000") + " halloween=" + GlobalGame.halloween + " christmas=" + GlobalGame.christmas
                + " glasses=" + Location34_Glasses.glassesMita + " sous=" + Location4ChangeSide.trysous + " console=" + Location4ChangeSide.playconsole;
        }

        // Every object in the scene, on or off, by name path. The mod's own objects and clones differ per player on purpose.
        static List<string> SceneObjectList(string scene)
        {
            List<string> list = new List<string>();
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (t == null || t.gameObject.scene.name != scene) continue;
                StringBuilder path = new StringBuilder();
                bool ours = false;
                for (Transform p = t; p != null; p = p.parent)
                {
                    if (p.name.StartsWith("MiSideTogether", StringComparison.Ordinal)) ours = true;
                    path.Insert(0, "/" + p.name);
                }
                if (!ours) list.Add(path.ToString());
            }
            return list;
        }
    }
}
