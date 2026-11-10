using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // Scene 20's escape corridor is built from random pieces. Every player runs it on their own PC, but it must be the
    // same corridor, or the others' bodies run through walls. Lab copies share a PC, so the host's corridor travels in
    // files, one per attempt: the host is caught once on purpose, and the corridor built after that restart must match too.
    //   corridor.same (client): in every attempt, the pieces built here, in build order, are the host's (kind, place, obstacles)
    public sealed partial class TogetherRuntime
    {
        readonly List<List<string>> labRunParts = new List<List<string>>();
        readonly HashSet<System.IntPtr> labRunSeen = new HashSet<System.IntPtr>();
        float labRunSince = -1;
        bool labRunKilled;
        static string LabCorridorFile(int attempt) { return Path.Combine(Application.persistentDataPath, "MiSideTogether", "corridor-host-" + attempt + ".txt"); }

        void LabCorridorTick(float now, string scene)
        {
            if (scene != "Scene 20 - FightMita" || labChecked.Contains("corridor.same")) return;
            Location20_RunCorridor run = Object.FindObjectOfType<Location20_RunCorridor>();
            bool gone = run == null || run.objectsRun == null;
            if (gone && (labRunSince < 0 || Hosting)) return;
            if (labRunSince < 0)
            {
                labRunSince = now; labRunParts.Add(new List<string>());
                if (Hosting) for (int i = 0; i < 4; i++) if (File.Exists(LabCorridorFile(i))) File.Delete(LabCorridorFile(i));
            }
            // The game's restart empties the corridor and builds a new one.
            bool known = false;
            for (int i = 0; !gone && i < run.objectsRun.Count; i++) if (run.objectsRun[i] != null && run.objectsRun[i].objectPart != null && labRunSeen.Contains(run.objectsRun[i].objectPart.Pointer)) known = true;
            if (!gone && !known && labRunSeen.Count > 0 && run.objectsRun.Count > 0) { labRunParts.Add(new List<string>()); labRunSeen.Clear(); Log("Lab: the corridor was rebuilt (attempt " + labRunParts.Count + ")"); }
            List<string> parts = labRunParts[labRunParts.Count - 1];
            int before = parts.Count;
            for (int i = 0; !gone && i < run.objectsRun.Count; i++)
            {
                Location20_RunCorridor_Part part = run.objectsRun[i];
                if (part == null || part.objectPart == null || !labRunSeen.Add(part.objectPart.Pointer)) continue;
                Transform t = part.objectPart.transform;
                parts.Add(part.typeCorridor + "@" + t.position.ToString("0.0") + "/" + t.GetComponentsInChildren<Transform>(true).Length);
            }
            if (Hosting)
            {
                if (parts.Count != before) { Directory.CreateDirectory(Path.GetDirectoryName(LabCorridorFile(0))); File.WriteAllText(LabCorridorFile(labRunParts.Count - 1), string.Join("\n", parts)); }
                if (!labRunKilled && now - labRunSince > 12) { labRunKilled = true; Log("Lab: Mita catches the host in the corridor"); try { run.KillPlayerStart(); } catch (System.Exception ex) { Log("Lab: KillPlayerStart threw: " + ex.GetType().Name); } }
                return;
            }
            if (!gone && now - labRunSince < 68) return;
            bool ok = labRunParts.Count >= 2;
            string detail = labRunParts.Count + " attempt(s):";
            for (int a = 0; a < labRunParts.Count; a++)
            {
                string[] host = File.Exists(LabCorridorFile(a)) ? ReadShared(LabCorridorFile(a)).Split('\n') : new string[0];
                int n = Mathf.Min(host.Length, labRunParts[a].Count), same = 0;
                while (same < n && host[same] == labRunParts[a][same]) same++;
                Log("MT-CORRIDOR attempt " + a + ": host " + host.Length + " pieces, here " + labRunParts[a].Count + ", first difference at " + same + (same < n ? ": host " + host[same] + " | here " + labRunParts[a][same] : ""));
                if (n < (a == 0 ? 12 : 8) || same < n) ok = false;
                File.WriteAllText(LabCorridorFile(a).Replace("-host-", "-client-"), string.Join("\n", labRunParts[a]));
                detail += " " + same + " of " + n + " the same;";
            }
            CheckOnce("corridor.same", ok, detail);
        }
    }
}
