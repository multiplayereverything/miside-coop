using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "core" (2 players, Scene 13): the core computer (CoreComputer.cs). The host presses the PC (presser-only),
    // then drives the program through the same methods its input calls: the folders stage, opening the first folder, the
    // version stage (which builds the text field), two keys and a backspace. The client's copy must follow from relays.
    //   core.driven@host   the host's program went through every step
    //   core.followed      the client's text field holds the same typed characters, from relayed input
    public sealed partial class TogetherRuntime
    {
        float labCorePressAt = -1, labCoreStepAt;
        int labCoreStep;

        void CoreHost(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 13 - HelloCore"); }
            if (scene != "Scene 13 - HelloCore" || LocalPlayer == null || now - tourSceneAt < 25) return;
            CheckOnce("chapter.loaded", true, scene);
            if (labCorePressAt < 0)
            {
                ObjectInteractive pc = null;
                foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>()) if (o != null && o.name == "InteractivePC" && o.active) pc = o;
                if (pc == null) { if (now - tourSceneAt > 200) CheckOnce("core.driven", false, "the PC was never armed"); return; }
                labCorePressAt = now; labCoreStepAt = now + 6;
                Log("Lab: host sits at the core computer");
                pc.Click();
                return;
            }
            if (now < labCoreStepAt || labCoreStep > 6) return;
            labCoreStepAt = now + 2;
            GamesCore_Main main = Object.FindObjectOfType<GamesCore_Main>();
            GamesCore_InputField field = Object.FindObjectOfType<GamesCore_InputField>();
            if (main == null) { CheckOnce("core.driven", false, "no running core program after sitting down"); labCoreStep = 99; return; }
            switch (labCoreStep++)
            {
                case 0: main.StartFolders(); Log("Lab: core step folders"); break;
                case 1: if (main.folders != null && main.folders.Length > 0) main.OpenFolder(main.folders[0]); Log("Lab: core step open folder 0"); break;
                case 2: main.StartVersionMita(); Log("Lab: core step version field"); break;
                case 3: case 4:
                    if (field == null) break;
                    field.canInput = true; field.indexButtonNow = labCoreStep - 4 + 1; field.ButtonClick();
                    Log("Lab: core step key " + field.indexButtonNow); break;
                case 5: if (field != null) field.ButtonBackspace(); Log("Lab: core step backspace"); break;
                case 6:
                {
                    string typed = Typed(field);
                    CheckOnce("core.driven", field != null && typed.Length > 0, "host's text field: " + typed);
                    Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-core:" + typed)));
                    break;
                }
            }
        }

        // The cursor (indexCharNow) and every slot's character: "2:AB___".
        static string Typed(GamesCore_InputField field)
        {
            if (field == null || field.fieldCharsNow == null) return "";
            string chars = "";
            for (int i = 0; i < field.fieldCharsNow.Count; i++) { GamesCore_InputField_Char c = field.fieldCharsNow[i]; chars += c != null && !string.IsNullOrEmpty(c.charInput) ? c.charInput : "_"; }
            return field.indexCharNow + ":" + chars;
        }

        void CoreNotice(string notice)
        {
            string hostTyped = notice.Substring("lab-core:".Length);
            string typed = Typed(Object.FindObjectOfType<GamesCore_InputField>());
            CheckOnce("core.followed", typed.Length > 0 && typed == hostTyped && coreInputsApplied >= 5,
                "client's text field " + typed + ", host's " + hostTyped + "; relayed computer inputs applied " + coreInputsApplied);
        }
    }
}
