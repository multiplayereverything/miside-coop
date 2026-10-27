using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "mobile" (2 players, Scene 14): the phone section. The host picks up the phone; its little character
    // ("Player Mobile", Location14_PlayerMobileMove) walks the toy house until it reaches "Trigger PlayerMobile", whose
    // Trigger_Event moves the story on (wiring from -mt-dump). The lab cannot press keys, so it sets the character's key
    // fields (LabLateTick) along a route through the toy doors. Both copies log their phone character.
    //   mobile.walked@host   the host's phone character reached the trigger (the game stopped it)
    //   mobile.seen          while the host's character walks (over 1.5 m from its start), the client shows it where the
    //                        host has it (within 0.5 m), from the relay (PhoneCharacter.cs) and the prop stream
    //   mobile.done          the trigger's story step ran on the client too ("Trigger NearPlayer" switched on)
    public sealed partial class TogetherRuntime
    {
        bool labMobilePressed, labMobilePlayed, labMobileRingSeen, labMobileStartKnown;
        Vector3 labMobileStart;
        float labMobileSaveAt = -1;
        float labMobileLogAt, labMobileSendAt, labMobileDoneAt = -1, labMobileFindAt;
        Location14_PlayerMobileMove labMobile;
        GameObject labMobileTrigger;
        System.Collections.Generic.List<Vector3> labMobileRoute;

        static Location14_PlayerMobileMove MobileCharacter()
        {
            Location14_PlayerMobileMove any = null;
            foreach (Location14_PlayerMobileMove m in Object.FindObjectsOfType<Location14_PlayerMobileMove>(true))
            {
                if (m == null) continue;
                if (m.gameObject.activeInHierarchy) return m;
                if (any == null) any = m;
            }
            return any;
        }

        static GameObject SceneObject(string name)
        {
            foreach (Transform t in Object.FindObjectsOfType<Transform>(true)) if (t != null && t.name == name) return t.gameObject;
            return null;
        }

        void LogMobile(float now)
        {
            if (now < labMobileLogAt) return;
            labMobileLogAt = now + 5;
            Location14_PlayerMobileMove m = MobileCharacter();
            GameObject trigger = SceneObject("Trigger PlayerMobile"), near = SceneObject("Trigger NearPlayer");
            Log("Lab: mobile " + (m == null ? "none" : "active=" + m.gameObject.activeInHierarchy + " play=" + m.play + " at " + m.transform.position
                + " kinematic=" + (m.rb != null && m.rb.isKinematic) + (trigger != null ? " to trigger " + Vector3.Distance(m.transform.position, trigger.transform.position).ToString("0.00") : ""))
                + " kinematic=" + (m != null && m.GetComponent<Rigidbody>() is Rigidbody mrb && mrb != null && mrb.isKinematic)
                + " streamed=" + (m != null && m.rb != null && Props().Contains(m.rb)) + "/" + Props().Count + "/" + Object.FindObjectsOfType<Rigidbody>().Length
                + " trigger=" + (trigger != null && trigger.activeInHierarchy) + " near=" + (near != null && near.activeInHierarchy) + " applied props " + propsApplied
                + (LocalPlayer != null ? " | player anim=" + LocalPlayer.animationRun + " loop=" + LocalPlayer.animationLoopRun + " now=" + (LocalPlayer.scrAnimationNow != null ? LocalPlayer.scrAnimationNow.name : "-") + " t=" + LocalPlayer.timeAnimationNow.ToString("0.0") : ""));
        }

        void MobileHost(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 14 - MobilePlayer"); }
            if (scene != "Scene 14 - MobilePlayer" || LocalPlayer == null || now - tourSceneAt < 20) return;
            LogMobile(now);
            // On the way to the phone the player walks through "Trigger Save" (an autosave with a ring animation on the
            // player). Clicked first, the phone's pickup animation is replaced by the ring 66 ms later and the phone
            // character never starts (seen with -mt-trace, also with -mt-nopatch). So: the save point, the ring, then the phone.
            if (labMobileSaveAt < 0)
            {
                GameObject save = SceneObject("Trigger Save");
                Collider box = save != null ? save.GetComponent<Collider>() : null;
                if (box == null || !save.activeInHierarchy) { if (now - tourSceneAt > 60) labMobileSaveAt = now; return; }
                Vector3 spot = box.bounds.center; spot.y = LocalPlayer.transform.position.y;
                LocalPlayer.TeleportPlayer(spot);
                labMobileSaveAt = now;
                Log("Lab: host walks into the save point at " + spot);
                return;
            }
            if (LocalPlayer.animationRun) labMobileRingSeen = true;
            if (!labMobilePressed && (LocalPlayer.animationRun || (!labMobileRingSeen && now - labMobileSaveAt < 15) || now - labMobileSaveAt < 3)) return;
            if (!labMobilePressed)
            {
                foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                    if (o != null && o.name == "Smartphone" && o.active && o.isActiveAndEnabled && !LocalPlayer.animationRun)
                    {
                        labMobilePressed = true;
                        Log("Lab: host picks up the Scene 14 phone");
                        o.Click();
                    }
                if (!labMobilePressed && now - tourSceneAt > 200) CheckOnce("mobile.walked", false, "the phone was never armed");
                return;
            }
            if (now >= labMobileFindAt) { labMobileFindAt = now + 2; labMobile = MobileCharacter(); labMobileTrigger = SceneObject("Trigger PlayerMobile"); }
            Location14_PlayerMobileMove m = labMobile;
            GameObject trigger = labMobileTrigger;
            if (m == null || trigger == null) return;
            if (m.play) labMobilePlayed = true;
            if (labMobilePlayed && !m.play) { CheckOnce("mobile.walked", true, "the host's phone character reached the trigger and the game stopped it"); }
            else if (now - tourSceneAt > 240) CheckOnce("mobile.walked", false, labMobilePlayed ? "the phone character never reached the trigger" : "the phone character never started");

            if (now >= labMobileSendAt && m.gameObject.activeInHierarchy)
            {
                labMobileSendAt = now + 1;
                Vector3 p = m.transform.position;
                string text = string.Format(CultureInfo.InvariantCulture, "lab-mobile:{0:R},{1:R},{2:R},{3}", p.x, p.y, p.z, m.play ? 1 : 0);
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, text)));
            }
        }

        void MobileClient(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (scene != "Scene 14 - MobilePlayer" || LocalPlayer == null) return;
            LogMobile(now);
            if (now < labMobileFindAt) return;
            labMobileFindAt = now + 2;
            GameObject near = SceneObject("Trigger NearPlayer");
            if (near != null && near.activeInHierarchy) CheckOnce("mobile.done", true, "the phone trigger's story step ran here (Trigger NearPlayer on)");
            if (labMobileDoneAt > 0 && now - labMobileDoneAt > 20) CheckOnce("mobile.done", false, "the host's phone character reached the trigger 20 s ago, nothing here");
        }

        void MobileNotice(string notice)
        {
            string[] c = notice.Substring("lab-mobile:".Length).Split(',');
            NumberStyles f = NumberStyles.Float; CultureInfo inv = CultureInfo.InvariantCulture;
            if (c.Length != 4 || !float.TryParse(c[0], f, inv, out float x) || !float.TryParse(c[1], f, inv, out float y) || !float.TryParse(c[2], f, inv, out float z)) return;
            float now = Time.realtimeSinceStartup;
            if (c[3] == "0" && labMobileDoneAt < 0 && labChecked.Contains("mobile.seen")) labMobileDoneAt = now;
            if (c[3] != "1") return;
            Vector3 host = new Vector3(x, y, z);
            if (!labMobileStartKnown) { labMobileStartKnown = true; labMobileStart = host; }
            if (Vector3.Distance(host, labMobileStart) < 1.5f) return;   // not walking yet: both would still be at the start
            Location14_PlayerMobileMove m = MobileCharacter();
            bool shown = m != null && m.gameObject.activeInHierarchy;
            float d = shown ? Vector3.Distance(m.transform.position, host) : -1;
            if (shown && d < 0.5f && mobileShown > 0)
                CheckOnce("mobile.seen", true, "the host's phone character is shown here " + d.ToString("F2", inv) + " m from where it is on the host, "
                    + Vector3.Distance(host, labMobileStart).ToString("F1", inv) + " m into its walk");
        }

        // After the game's Update has read the (absent) keys: the phone character's keys, as a player would press them.
        // Location14_PlayerMobileMove.FixedUpdate (0x33ed50) sets the velocity from forward * vMove + right * hMove, so the
        // lab turns the character towards the trigger and holds "forward" (vMove 1).
        void LabLateTick()
        {
            if (labTour != "mobile" || !Hosting || labMobile == null || labMobileTrigger == null || !labMobile.play) return;
            if (labMobileRoute == null)
            {
                // Bedroom (start) -> door Main-Bedroom -> main room -> door Kitchen-Main -> kitchen (the trigger): the toy
                // house layout logged by this tour (doors on one wall, the bedroom and kitchen south of it).
                labMobileRoute = new System.Collections.Generic.List<Vector3>();
                foreach (string name in new[] { "DoorPhysic Main-Bedroom", "DoorPhysic Kitchen-Main" })
                {
                    GameObject door = SceneObject(name);
                    Collider c = door != null ? door.GetComponentInChildren<Collider>() : null;
                    if (c == null) continue;
                    Vector3 d = c.bounds.center;
                    bool inward = labMobileRoute.Count == 0;   // first door: from the south side into the main room
                    labMobileRoute.Add(new Vector3(d.x, 0, d.z + (inward ? -1.3f : 1.6f)));
                    labMobileRoute.Add(new Vector3(d.x, 0, d.z + (inward ? 1.6f : -1.3f)));
                }
                labMobileRoute.Add(labMobileTrigger.transform.position);
                Log("Lab: phone character route " + string.Join(" ", labMobileRoute));
            }
            Vector3 target = labMobileRoute[0];
            Vector3 to = target - labMobile.transform.position; to.y = 0;
            if (to.magnitude < 0.35f && labMobileRoute.Count > 1) { labMobileRoute.RemoveAt(0); return; }
            if (to.sqrMagnitude < 0.0001f) return;
            labMobile.transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
            labMobile.rotBody = labMobile.transform.eulerAngles.y;
            labMobile.vMove = 1; labMobile.hMove = 0;
        }
    }
}