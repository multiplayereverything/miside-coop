using UnityEngine;

namespace MiSideTogether
{
    // Lab looks the way a player does. Trigger_MouseEvent.Update (disassembly) counts the player as looking while their
    // look ray's object (PlayerMove.objectCast) is the trigger and distanceCast is within its distance; it then calls Enter
    // and counts timeHoldNow down to eventTimeEnd (Scene 6's caged Mita: looking at her starts "pass me the key"). The
    // pilot points that ray at the trigger for as long as the look takes, before the trigger's own Update.
    public sealed partial class TogetherRuntime
    {
        static Trigger_MouseEvent labGazeTarget;
        static float labGazeUntil;
        bool labGazeHooked;

        static void LabGazeUpdatePrefix(Trigger_MouseEvent __instance)
        {
            if (labGazeTarget == null || __instance.Pointer != labGazeTarget.Pointer || Time.realtimeSinceStartup > labGazeUntil || __instance.scrpm == null) return;
            __instance.scrpm.objectCast = __instance.gameObject;
            __instance.scrpm.distanceCast = 0;
        }

        // Returns how long the look takes, so the pilot waits for it.
        float LabGaze(Trigger_MouseEvent t)
        {
            if (!labGazeHooked)
            {
                labGazeHooked = true;
                new HarmonyLib.Harmony("local.misidetogether.lab.gaze").Patch(HarmonyLib.AccessTools.Method(typeof(Trigger_MouseEvent), "Update"), new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabGazeUpdatePrefix)));
            }
            float hold = Mathf.Max(t.timeHold, 0) + 1;
            labGazeTarget = t; labGazeUntil = Time.realtimeSinceStartup + hold;
            return hold;
        }
    }
}
