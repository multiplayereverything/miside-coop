using UnityEngine;

namespace MiSideTogether
{
    // Hold interactions (ObjectAnimationPlayerHold: Scene 4's scarf; Scenes 6, 9, 17) read the player's key in their own
    // Update (Input.GetKey/GetKeyDown with the hold's key; docs/INPUT-AUDIT.md). The pilot holds it as a player does:
    // while the hold's Update runs on the PC whose player is in it, its key reads as held.
    public sealed partial class TogetherRuntime
    {
        static bool labInHold, labHoldLogged;
        static string labHoldKey;
        bool labHoldHooked;

        static void LabHoldUpdatePrefix(ObjectAnimationPlayerHold __instance)
        {
            labInHold = false;
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.labPilotFull || __instance == null || !__instance.isActiveAndEnabled || __instance.scrpm == null || rt.LocalPlayer == null) return;
            if (__instance.scrpm.Pointer != rt.LocalPlayer.Pointer || !rt.LocalPlayer.animationRun) return;
            labInHold = true; labHoldKey = __instance.key;
            if (!labHoldLogged) { labHoldLogged = true; Log("Lab: pilot holds " + PathOf(__instance.transform) + " (key " + __instance.key + ", mouse " + __instance.mouseRotate + ", " + __instance.animationTimeHold + " s)"); }
        }
        static void LabHoldUpdatePostfix() { labInHold = false; }
        static void LabHoldKeyPostfix(string __0, ref bool __result) { if (labInHold && __0 == labHoldKey) __result = true; }
        // A hold that wants the mouse turned in circles (mouseRotate) gets it turned.
        static void LabHoldAxisPostfix(string __0, ref float __result)
        {
            if (!labInHold) return;
            float t = Time.realtimeSinceStartup * 6f;
            if (__0 == "Mouse X") __result = Mathf.Cos(t) * 3f; else if (__0 == "Mouse Y") __result = Mathf.Sin(t) * 3f;
        }

        // hold.same: (every other PC) 5 s after a player's hold is done (Ready), the quests and key items here are the
        // holder's: what the hold set off ran everywhere.
        static float labHoldReadyAt = -1;
        static void LabHoldReadyPostfix(ObjectAnimationPlayerHold __instance)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || __instance == null || __instance.scrpm == null || rt.LocalPlayer == null || __instance.scrpm.Pointer != rt.LocalPlayer.Pointer) return;
            Log("Lab: hold done here: " + PathOf(__instance.transform));
            labHoldReadyAt = Time.realtimeSinceStartup;
        }

        // The game can finish a hold without calling Ready() (seen in Scene 4): its ready flag turning on counts too.
        readonly System.Collections.Generic.HashSet<System.IntPtr> labHoldsReady = new System.Collections.Generic.HashSet<System.IntPtr>();
        void LabHoldTick(float now)
        {
            if (LocalPlayer != null)
                foreach (ObjectAnimationPlayerHold h in Object.FindObjectsOfType<ObjectAnimationPlayerHold>())
                    if (h != null && h.ready && h.scrpm != null && h.scrpm.Pointer == LocalPlayer.Pointer && labHoldsReady.Add(h.Pointer) && labHoldReadyAt < 0)
                    { Log("Lab: hold done here: " + PathOf(h.transform)); labHoldReadyAt = now; }
            if (labHoldReadyAt < 0 || now - labHoldReadyAt < 5) return;
            labHoldReadyAt = -1;
            string s = "lab-holdquests:" + PilotState();
            if (Hosting) Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, s))); else SendToHost(Wire.Packet(Op.Notice, w => Wire.Text(w, s)));
        }

        public void LabHoldQuestsNotice(string theirs)
        {
            string here = PilotState();
            CheckOnce("hold.same", theirs == here, "holder " + theirs + " / here " + here);
        }

        void PilotHolds(float now)
        {
            LabHoldTick(now);
            if (labHoldHooked) return;
            labHoldHooked = true;
            HarmonyLib.Harmony lab = new HarmonyLib.Harmony("local.misidetogether.lab.hold");
            lab.Patch(HarmonyLib.AccessTools.Method(typeof(ObjectAnimationPlayerHold), "Ready"), null, new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabHoldReadyPostfix)));
            lab.Patch(HarmonyLib.AccessTools.Method(typeof(ObjectAnimationPlayerHold), "Update"), new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabHoldUpdatePrefix)), new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabHoldUpdatePostfix)));
            lab.Patch(HarmonyLib.AccessTools.Method(typeof(Input), "GetKey", new[] { typeof(string) }), null, new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabHoldKeyPostfix)) { priority = HarmonyLib.Priority.First });
            lab.Patch(HarmonyLib.AccessTools.Method(typeof(Input), "GetKeyDown", new[] { typeof(string) }), null, new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabHoldKeyPostfix)) { priority = HarmonyLib.Priority.First });
            lab.Patch(HarmonyLib.AccessTools.Method(typeof(Input), "GetAxis", new[] { typeof(string) }), null, new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabHoldAxisPostfix)) { priority = HarmonyLib.Priority.First });
        }
    }
}
