using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "inputhook" (1 player, Scene 1): can a mod answer the game's own key checks? This is GMTogether's core idea,
    // and the plan's open question. The host picks up the phone; the first key prompt then waits for its key inside the
    // game's own Interface_KeyHint_Key.Update. It must stay waiting for 6 s while nothing is forced (the control), then
    // press itself once Input.GetKeyDown(string) is made to answer true. No mod code calls KeyDown here.
    public sealed partial class TogetherRuntime
    {
        static int hookCalls, hookForced, hookKeyDowns;
        static bool hookForce;
        static string hookFirstKey;
        bool hookInstalled, hookTook;
        float hookPromptAt = -1;

        static bool HookGetKeyDown(string __0, ref bool __result)
        {
            hookCalls++;
            if (hookFirstKey == null) hookFirstKey = __0;
            if (!hookForce) return true;
            hookForced++;
            __result = true;
            return false;
        }

        static void HookKeyDownSeen() { hookKeyDowns++; }

        void InputHookTour(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (!hookInstalled)
            {
                hookInstalled = true;
                HarmonyLib.Harmony lab = new HarmonyLib.Harmony("local.misidetogether.lab.inputhook");
                lab.Patch(AccessTools.Method(typeof(Input), "GetKeyDown", new[] { typeof(string) }), new HarmonyMethod(typeof(TogetherRuntime), nameof(HookGetKeyDown)));
                lab.Patch(AccessTools.Method(typeof(Interface_KeyHint_Key), "KeyDown"), null, new HarmonyMethod(typeof(TogetherRuntime), nameof(HookKeyDownSeen)));
            }
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 1 - RealRoom"); return; }
            if (scene != "Scene 1 - RealRoom" || LocalPlayer == null) return;
            LabScene1Tutorial(now);
            if (now - tourSceneAt < 25) return;
            if (!hookTook)
            {
                foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                    if (o != null && o.name == "Interactive Mobile" && o.active) { hookTook = true; o.Click(); Log("Lab: inputhook picks up the phone"); return; }
                return;
            }
            if (LocalPlayer.animationRun && !LocalPlayer.animationLoopRun) return;
            if (hookPromptAt < 0)
            {
                foreach (Interface_KeyHint_Key k in Object.FindObjectsOfType<Interface_KeyHint_Key>())
                    if (k != null && k.isActiveAndEnabled && PathOf(k.transform).Contains("Smartphone")) { hookPromptAt = now; Log("Lab: inputhook prompt up: " + k.name); return; }
                return;
            }
            if (now - hookPromptAt < 6 || labChecked.Contains("inputhook.forced")) return;
            if (!hookForce)
            {
                CheckOnce("inputhook.control", hookKeyDowns == 0, "with nothing forced the prompt pressed itself " + hookKeyDowns + " time(s); game called GetKeyDown(string) " + hookCalls + " times, first key '" + hookFirstKey + "'");
                hookForce = true;
                Log("Lab: inputhook forcing the game's key checks");
                return;
            }
            if (now - hookPromptAt < 7) return;
            hookForce = false;
            CheckOnce("inputhook.forced", hookKeyDowns > 0, "forced answers " + hookForced + ", prompt pressed " + hookKeyDowns + " time(s)");
        }
    }
}
