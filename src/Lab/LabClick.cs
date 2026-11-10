using UnityEngine;

namespace MiSideTogether
{
    // A mouse click on the F5 panel, as the game's own input reports it (position and button), for one frame. Tours can't
    // click the desktop, and calling the button's action directly would skip the part a player's click goes through.
    public sealed partial class TogetherRuntime
    {
        static int labClickFrame = -1;
        static Vector3 labClickAt;
        bool labClickHooked;

        static void LabClickPositionPostfix(ref Vector3 __result) { if (Time.frameCount == labClickFrame) __result = labClickAt; }
        static void LabClickDownPostfix(int __0, ref bool __result) { if (Time.frameCount == labClickFrame && __0 == 0) __result = true; }

        // False when the panel has no button with that label right now.
        bool LabClickButton(string label)
        {
            if (!labClickHooked)
            {
                labClickHooked = true;
                HarmonyLib.Harmony lab = new HarmonyLib.Harmony("local.misidetogether.lab.click");
                lab.Patch(HarmonyLib.AccessTools.PropertyGetter(typeof(Input), "mousePosition"), null, new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabClickPositionPostfix)));
                lab.Patch(HarmonyLib.AccessTools.Method(typeof(Input), "GetMouseButtonDown", new[] { typeof(int) }), null, new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabClickDownPostfix)));
            }
            Rect r;
            if (!ui.Buttons.TryGetValue(label, out r)) return false;
            float scale = Mathf.Max(1f, Screen.height / 900f);
            labClickAt = new Vector3(r.center.x * scale, Screen.height - r.center.y * scale, 0);
            labClickFrame = Time.frameCount + 1;
            return true;
        }
    }
}
