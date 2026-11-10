using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // Lab tour "savesguard" (2 players, in the menu): the client's backup of its own saves fails. The host's saves must
    // then not be written over the client's, and the client's own save must stay. The client uses its own folder here,
    // because lab copies share the user's.
    //   savesguard.sent: the host has saves to mirror
    //   savesguard.kept: the client's folder still holds its own save and nothing else
    public sealed partial class TogetherRuntime
    {
        static string LabSaveDir;
        static bool LabBackupFails;
        float savesGuardAt = -1;

        void SavesGuardSetup()
        {
            LabSaveDir = Path.Combine(Application.persistentDataPath, "MiSideTogether", "savesguard");
            LabBackupFails = true;
            if (Directory.Exists(LabSaveDir)) Directory.Delete(LabSaveDir, true);
            Directory.CreateDirectory(LabSaveDir);
            File.WriteAllText(Path.Combine(LabSaveDir, "mine"), "client-own");
        }

        void SavesGuardTour(float now)
        {
            if (!Connected || AcceptedCount == 0) return;
            if (savesGuardAt < 0) savesGuardAt = now;
            if (Hosting)
            {
                int files = 0;
                if (Directory.Exists(SaveDir)) foreach (string file in Directory.GetFiles(SaveDir)) if (SaveMirrored(Path.GetFileName(file))) files++;
                CheckOnce("savesguard.sent", files > 0, files + " save file(s) to mirror");
                return;
            }
            if (now - savesGuardAt < 15) return;
            string[] names = Directory.GetFiles(LabSaveDir);
            string mine = Path.Combine(LabSaveDir, "mine");
            bool kept = names.Length == 1 && File.Exists(mine) && File.ReadAllText(mine) == "client-own";
            CheckOnce("savesguard.kept", kept, names.Length + " file(s) in the client's folder" + (File.Exists(mine) ? "" : ", its own save is gone"));
        }
    }
}
