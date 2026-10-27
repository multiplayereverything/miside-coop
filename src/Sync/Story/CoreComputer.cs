using System.Collections.Generic;
using UnityEngine;

namespace MiSideTogether
{
    // The core computer in Scenes 13 and 15 (GamesCore_Main with its text field GamesCore_InputField). One player sits and
    // operates it (CutsceneRules: InteractivePC is who=actor, the others watch through their eyes); every machine runs its
    // own copy of the program, started by the relayed press, and the operator's input is replayed on the others so their
    // copies show the same screens and fire the same story events (eventRight / eventUnright of the text field, a folder's
    // eventClick). Read from the game code (GameAssembly.dll, docs/SYNC.md item 4):
    //   10 GamesCore_InputField.ButtonClick       types the character of the selected key (indexButtonNow, the arg)
    //   11 GamesCore_InputField.ButtonBackspace
    //   12 GamesCore_InputField.ButtonEnter        compares what was typed with fieldCharsNeed: eventRight or eventUnright
    //   13 GamesCore_Main.OpenFolder(folder)       "<main>|i/j/..": the folder's index path in the folders tree (folders
    //                                              are plain data inside the program, not scene objects)
    //   14 GamesCore_Main stage: 0 StartFolders, 1 StartVersionMita, 2 StartIndex, 3 StopProgramma
    // Stage methods are also started by the program itself (a right answer's event, the entry pattern finishing), so a
    // stage that already ran here within 2 s is not run again, and one that runs here within 2 s of a relay is not sent.
    public sealed partial class TogetherRuntime
    {
        public static readonly string[] CoreStages = { "StartFolders", "StartVersionMita", "StartIndex", "StopProgramma" };
        readonly Dictionary<string, float> coreStageAt = new Dictionary<string, float>();
        public int coreInputsApplied;
        // Only the operator's input is sent: the other copies run the same program and can call these methods by
        // themselves (a timer opening a folder), which must not come back to the operator.
        static bool coreOperatorHere;

        public static void NoteCoreOperator(string clickKey, bool local)
        {
            if (clickKey != null && clickKey.Contains("InteractivePC")) coreOperatorHere = local;
        }

        public static void CoreKey(GamesCore_InputField field, int entry)
        {
            if (!coreOperatorHere || field == null || !field.canInput) return;
            Call(entry, field, entry == 10 ? field.indexButtonNow : 0);
        }

        public static void CoreFolder(GamesCore_Main main, GamesCore_Main_Folder folder)
        {
            if (!coreOperatorHere || main == null || folder == null || main.folders == null) return;
            string path = FolderPath(main.folders, folder, "", 0);
            if (path == null) { Log("Core computer: opened folder not found in its tree"); return; }
            Story(Op.Call, "13:" + SceneKeys.Key(main) + "|" + path, 0);
        }

        static string FolderPath(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GamesCore_Main_Folder> list, GamesCore_Main_Folder find, string prefix, int depth)
        {
            if (list == null || depth > 8) return null;
            for (int i = 0; i < list.Length; i++)
            {
                GamesCore_Main_Folder f = list[i];
                if (f == null) continue;
                string here = prefix.Length == 0 ? i.ToString() : prefix + "/" + i;
                if (f.Pointer == find.Pointer) return here;
                string inner = FolderPath(f.folders, find, here, depth + 1);
                if (inner != null) return inner;
            }
            return null;
        }

        public static void CoreStage(GamesCore_Main main, int stage)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || main == null) return;
            if (!coreOperatorHere) { rt.coreStageAt[SceneKeys.Key(main) + "|" + stage] = Time.realtimeSinceStartup; return; }
            string key = SceneKeys.Key(main) + "|" + stage;
            float now = Time.realtimeSinceStartup;
            bool recent = rt.coreStageAt.TryGetValue(key, out float at) && now - at < 2f;
            rt.coreStageAt[key] = now;
            if (!recent) Call(14, main, stage);
        }

        void ApplyCoreCall(int entry, string target, int arg)
        {
            if (entry == 13)
            {
                int bar = target.LastIndexOf('|');
                if (bar < 0) return;
                string mainKey = target.Substring(0, bar);
                GamesCore_Main main = SceneKeys.Find<GamesCore_Main>(mainKey);
                if (Missing(main, mainKey) || Inactive(main, mainKey)) return;
                GamesCore_Main_Folder folder = null;
                var list = main.folders;
                foreach (string part in target.Substring(bar + 1).Split('/'))
                {
                    if (list == null || !int.TryParse(part, out int i) || i < 0 || i >= list.Length) { folder = null; break; }
                    folder = list[i];
                    list = folder != null ? folder.folders : null;
                }
                if (folder == null) { Log("Core computer: no folder " + target); return; }
                main.OpenFolder(folder);
                coreInputsApplied++;
                return;
            }
            if (entry == 14)
            {
                GamesCore_Main main = SceneKeys.Find<GamesCore_Main>(target);
                if (Missing(main, target) || arg < 0 || arg >= CoreStages.Length) return;
                string key = SceneKeys.Key(main) + "|" + arg;
                float now = Time.realtimeSinceStartup;
                if (coreStageAt.TryGetValue(key, out float at) && now - at < 2f) return;   // the program got there by itself
                coreStageAt[key] = now;
                if (arg == 0) main.StartFolders(); else if (arg == 1) main.StartVersionMita(); else if (arg == 2) main.StartIndex(); else main.StopProgramma();
                coreInputsApplied++;
                return;
            }
            GamesCore_InputField field = SceneKeys.Find<GamesCore_InputField>(target);
            if (Missing(field, target) || Inactive(field, target)) return;
            if (entry == 10) { field.indexButtonNow = arg; field.ButtonClick(); }
            else if (entry == 11) field.ButtonBackspace();
            else field.ButtonEnter();
            coreInputsApplied++;
        }
    }
}
