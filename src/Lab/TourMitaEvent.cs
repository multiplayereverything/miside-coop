using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // User report (2026-11-09): "object syncing with her". What Mita handles is moved by her animation's events (Scene 3: spatula, juice glass, TV remote).
    // A full Scene 3 run counted them per PC: on a client four fired twice and the one that puts the spatula back never fired.
    // Lab tour "mitaevent" (Scene 3): the events are fired through the entry point the animation calls.
    //   mitaevent.ignored: (client) an event from this PC's own copy of her animation does nothing
    //   mitaevent.relayed: (client) the event from the PC moving her does happen here
    public sealed partial class TogetherRuntime
    {
        Animator_FunctionsOverride labEventMita;
        Transform_Magnet labEventSpatula;
        float labEventAt = -1;
        bool labEventFired;

        void MitaEventTour(float now, string scene)
        {
            if (scene != "Scene 3 - WeTogether" || LocalPlayer == null || now - tourSceneAt < 20) return;
            if (labEventAt < 0)
            {
                foreach (Animator_FunctionsOverride a in Object.FindObjectsOfType<Animator_FunctionsOverride>(true))
                    if (a != null && a.name == "MitaPerson Mita" && a.GetComponentInParent<MitaPerson>(true) != null) labEventMita = a;
                foreach (Transform_Magnet m in Object.FindObjectsOfType<Transform_Magnet>(true))
                    if (m != null && m.name == "SpatulaInRightHand") labEventSpatula = m;
                if (labEventMita == null || labEventSpatula == null) { CheckOnce("mitaevent.ignored", false, "Mita " + (labEventMita != null) + ", spatula " + (labEventSpatula != null)); return; }
                for (Transform t = labEventSpatula.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                labEventSpatula.active = false;
                labEventAt = now;
                Log("Lab: Mita event test starts");
                return;
            }
            float at = now - labEventAt;
            if (IsClient)
            {
                if (at >= 2 && !labEventFired) { labEventFired = true; labEventMita.NewEvent(0); }
                if (at >= 5 && at < 12) CheckOnce("mitaevent.ignored", !labEventSpatula.active, "this PC's own event " + (labEventSpatula.active ? "ran" : "did nothing"));
                if (at >= 22) CheckOnce("mitaevent.relayed", labEventSpatula.active, "the host's event " + (labEventSpatula.active ? "ran here" : "did not run here"));
            }
            else if (at >= 12 && !labEventFired) { labEventFired = true; labEventMita.NewEvent(0); }
        }
    }
}
