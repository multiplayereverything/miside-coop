using UnityEngine;

namespace MiSideTogether
{
    // Lab presses the way a player does: the game's own ObjectInteractive.Update reads the mouse and runs the click itself,
    // never calling Click() (2026-11-08: no player press reached the mod's Click hook in a real session, and tours that
    // called Click() never noticed). The lab only aims at the object and holds the mouse button down for its Update.
    public sealed partial class TogetherRuntime
    {
        static ObjectInteractive labPressTarget;
        static bool labPressInTarget;
        static int labPressFrames;
        static float labPressSince;
        bool labPressHooked;

        static void LabPressUpdatePrefix(ObjectInteractive __instance)
        {
            labPressInTarget = labPressFrames > 0 && labPressTarget != null && __instance.Pointer == labPressTarget.Pointer;
            // What the player's look ray hits, as the game compares it: objectCastInteractive with the object's
            // objectInteractive, or (hand-reach objects, interactiveIK) objectCastHandInteractive with its GameObject.
            if (labPressInTarget && __instance.scrpm != null)
            {
                __instance.scrpm.objectCastInteractive = __instance.objectInteractive;
                __instance.scrpm.objectCastHandInteractive = __instance.gameObject;
            }
        }
        static void LabPressUpdatePostfix() { if (labPressInTarget) { labPressInTarget = false; labPressFrames--; } }
        static void LabPressMousePostfix(int __0, ref bool __result) { if (labPressInTarget && __0 == 0) __result = true; }

        // False while the press can't happen yet: not armed, or the player is in an animation (the game ignores clicks then).
        bool LabPress(ObjectInteractive o)
        {
            if (!labPressHooked)
            {
                labPressHooked = true;
                HarmonyLib.Harmony lab = new HarmonyLib.Harmony("local.misidetogether.lab.press");
                // The held button comes first, so the mod's own hooks see it as a player's press.
                lab.Patch(HarmonyLib.AccessTools.Method(typeof(ObjectInteractive), "Update"), new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabPressUpdatePrefix)), new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabPressUpdatePostfix)));
                lab.Patch(HarmonyLib.AccessTools.Method(typeof(Input), "GetMouseButtonDown", new[] { typeof(int) }), null, new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabPressMousePostfix)) { priority = HarmonyLib.Priority.First });
            }
            // A press whose object never ran its Update (switched off since) is dropped after a second.
            if (labPressFrames > 0 && Time.realtimeSinceStartup - labPressSince > 1) labPressFrames = 0;
            if (o == null || !o.active || !o.isActiveAndEnabled || labPressFrames > 0 || LocalPlayer == null || LocalPlayer.animationRun) return false;
            labPressTarget = o; labPressFrames = 1; labPressSince = Time.realtimeSinceStartup;
            return true;
        }
    }
}
