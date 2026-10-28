using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "safe" (2 players, Scene 6): the basement safe's keypad (Basement_Safe.ClickButton, relayed as Op.Call 0).
    // The host presses the safe's interactive (which opens the keypad), then three digits through ClickButton, as the
    // keypad buttons do. The client presses nothing: its keypad must show the same digits from the relay.
    //   safe.typed@host   the host's keypad holds the three digits
    //   safe.followed     the client's keypad holds the same digits
    public sealed partial class TogetherRuntime
    {
        float labSafeOpenAt = -1, labSafeStepAt, labSafeLogAt;
        int labSafeStep;

        static Basement_Safe LabSafe()
        {
            foreach (Basement_Safe s in Object.FindObjectsOfType<Basement_Safe>(true)) if (s != null) return s;
            return null;
        }

        static string SafeTyped(Basement_Safe s) => s == null ? "" : (s.passwordNow ?? "") + "|" + (s.textNumbers != null ? s.textNumbers.text : "");

        void SafeHost(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 6 - BasementFirst"); }
            if (scene != "Scene 6 - BasementFirst" || LocalPlayer == null || now - tourSceneAt < 25) return;
            Basement_Safe safe = LabSafe();
            if (now >= labSafeLogAt)
            {
                labSafeLogAt = now + 10;
                ObjectInteractive place = safe != null && safe.interactivePlace != null ? safe.interactivePlace.GetComponent<ObjectInteractive>() : null;
                Log("Lab: safe " + (safe == null ? "none" : "active=" + safe.active + " shown=" + safe.gameObject.activeInHierarchy + " typed '" + SafeTyped(safe) + "' place="
                    + (place == null ? "-" : place.name + " armed=" + place.active + " on=" + place.isActiveAndEnabled)));
            }
            if (safe == null) { if (now - tourSceneAt > 200) CheckOnce("safe.typed", false, "no safe in the chapter"); return; }
            if (labSafeOpenAt < 0)
            {
                // In Scene 6 the keypad is armed from the start (active, no interactive of its own): press straight away.
                ObjectInteractive place = safe.interactivePlace != null ? safe.interactivePlace.GetComponent<ObjectInteractive>() : null;
                if (!safe.active && (place == null || !place.active || !place.isActiveAndEnabled))
                {
                    if (now - tourSceneAt > 200) CheckOnce("safe.typed", false, "the safe was never armed");
                    return;
                }
                labSafeOpenAt = now; labSafeStepAt = now + (safe.active ? 0 : 6);
                if (!safe.active) { Log("Lab: host opens the safe's keypad"); place.Click(); }
                return;
            }
            if (now < labSafeStepAt || labSafeStep > 3) return;
            labSafeStepAt = now + 1.5f;
            if (labSafeStep < 3) { safe.ClickButton(labSafeStep + 1); Log("Lab: host presses " + (labSafeStep + 1) + " -> '" + SafeTyped(safe) + "'"); labSafeStep++; return; }
            labSafeStep++;
            string typed = SafeTyped(safe);
            CheckOnce("safe.typed", (safe.passwordNow ?? "").Length == 3, "host's keypad: " + typed + " (active=" + safe.active + ")");
            Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-safe:" + typed)));
        }

        void SafeNotice(string notice)
        {
            string hostTyped = notice.Substring("lab-safe:".Length);
            string typed = SafeTyped(LabSafe());
            CheckOnce("safe.followed", typed.Length > 1 && typed == hostTyped, "client's keypad '" + typed + "', host's '" + hostTyped + "'");
        }
    }
}
