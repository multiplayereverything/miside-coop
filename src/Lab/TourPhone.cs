using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "phone" (2 players, Scene 1): the very start. Whoever picks up the phone holds and uses it; the others
    // see them pick it up with the phone in their hand, do not pick one up themselves, and cannot take another.
    public sealed partial class TogetherRuntime
    {
        float labPhoneAt = -1;
        bool labMagnetsDumped;

        static string PathOf(Transform t) { if (t == null) return "null"; string p = ""; for (Transform x = t; x != null && p.Length < 160; x = x.parent) p = x.name + "/" + p; return p; }

        void DumpMagnets(string when)
        {
            Transform root = LocalPlayer != null ? LocalPlayer.transform : null;
            foreach (Transform_Magnet m in Object.FindObjectsOfType<Transform_Magnet>(true))
            {
                if (m == null || !(m.name.Contains("Smartphone"))) continue;
                Log("Lab: magnet " + when + " " + PathOf(m.transform) + " active=" + m.active + " enabled=" + m.enabled + " playerRoot=" + m.playerRoot + " magnetPlayer='" + m.magnetPlayer + "' local=" + m.local
                    + " | transformMagnet=" + PathOf(m.transformMagnet) + " | transformTarget=" + PathOf(m.transformTarget)
                    + " | onPlayer: self=" + (root != null && m.transform.IsChildOf(root)) + " magnet=" + (root != null && m.transformMagnet != null && m.transformMagnet.IsChildOf(root)) + " target=" + (root != null && m.transformTarget != null && m.transformTarget.IsChildOf(root)));
            }
        }

        bool labPhoneHostActed, labPhoneMoved; float labPhoneSeenAt = -1;

        // Scene 1's tutorial waits for the player to move the mouse and walk (Location1Tutorial: eventReadyMouse and
        // eventReadyMove fire when their timers fill), asks whether to show quest hints, and then asks for a name
        // (Location1_InputNamePlayer.CheckName). A lab copy gets no input, so it answers each step as a player would.
        // Each player does this on their own machine, so both copies run it. Before this, the phone was never armed.
        float labTutorialAt;
        bool labTutorialAnswered, labTutorialShirt;
        float labTutorialLogAt;

        void LabScene1Tutorial(float now)
        {
            if (now < labTutorialAt) return;
            labTutorialAt = now + 2;
            foreach (Location1Tutorial t in Object.FindObjectsOfType<Location1Tutorial>())
            {
                if (t == null || !t.isActiveAndEnabled) continue;
                if (now >= labTutorialLogAt)
                {
                    labTutorialLogAt = now + 10;
                    Log("Lab: tutorial state hint " + t.indexHint + " play=" + t.play + " request=" + t.requestPlayer + " canMove " + t.timeCanMove.ToString("0.0")
                        + " mouse=" + (t.objectMouseMove != null && t.objectMouseMove.activeInHierarchy) + " move=" + (t.objectMove != null && t.objectMove.activeInHierarchy)
                        + " frame=" + (t.frameTutorial != null && t.frameTutorial.activeInHierarchy) + " style=" + (t.frameStyle != null && t.frameStyle.activeInHierarchy)
                        + " fill " + (t.imageTimeMouse != null ? t.imageTimeMouse.fillAmount.ToString("0.00") : "-") + "/" + (t.imageTimeMove != null ? t.imageTimeMove.fillAmount.ToString("0.00") : "-"));
                }
                // Location1Tutorial.Update (GameAssembly 0x3209b0): hint 1 fills imageTimeMouse from the mouse axes, then
                // invokes eventReadyMouse, hides the prompt and goes to hint 2 (0.5 s pause); hint 2 does the same for
                // walking with imageTimeMove and eventReadyMove, then hint 3. The same transitions, without the input:
                if (t.play && t.indexHint == 1)
                {
                    if (t.imageTimeMouse != null) t.imageTimeMouse.fillAmount = 1;
                    t.eventReadyMouse?.Invoke();
                    if (t.objectMouseMove != null) t.objectMouseMove.SetActive(false);
                    t.indexHint = 2; t.timeCanMove = 0.5f;
                    Log("Lab: tutorial - moved the mouse");
                    return;
                }
                if (t.play && t.indexHint == 2)
                {
                    if (t.imageTimeMove != null) t.imageTimeMove.fillAmount = 1;
                    t.eventReadyMove?.Invoke();
                    if (t.objectMove != null) t.objectMove.SetActive(false);
                    t.indexHint = 3;
                    Log("Lab: tutorial - walked");
                    return;
                }
                // Hint 3 ("interact"): with quest hints on, eventYesQuest switched on "Interactive TShip Tutorial"; clicking it
                // starts the T-shirt dialogue, whose end arms the room's interactives (the phone among them). Found with the
                // wiring dump (-mt-dump, private-wires-*.txt). With hints off, eventNoQuest arms them at once.
                if (t.play && t.indexHint >= 3 && !labTutorialShirt)
                {
                    foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                        if (o != null && o.name == "Interactive TShip Tutorial" && o.active)
                        {
                            labTutorialShirt = true;
                            o.Click();
                            Log("Lab: tutorial - looked at the T-shirt");
                            return;
                        }
                }
                if (t.frameTutorial != null && t.frameTutorial.activeInHierarchy && !labTutorialAnswered)
                {
                    labTutorialAnswered = true;
                    t.ChangePlayerQuest(true);
                    t.CloseQuestHint();
                    Log("Lab: tutorial - quest hints on (requestPlayer=" + t.requestPlayer + ", play=" + t.play + ", hint " + t.indexHint + ")");
                    return;
                }
            }
            foreach (Location1_InputNamePlayer n in Object.FindObjectsOfType<Location1_InputNamePlayer>())
            {
                if (n == null || !n.isActiveAndEnabled || n.inputName == null || !n.inputName.gameObject.activeInHierarchy || n.inputName.text == Name) continue;
                n.inputName.text = Name;
                n.CheckName();
                Log("Lab: tutorial - name " + Name);
                return;
            }
        }

        void PhoneHost(float now, string scene)
        {
            // The lab cannot press a key, so the game's location hint screens (which wait for one) are off for this tour.
            GlobalGame.hintScreen = false;
            if (scene == "Scene 1 - RealRoom") LabScene1Tutorial(now);
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 1 - RealRoom"); }
            if (scene != "Scene 1 - RealRoom" || LocalPlayer == null || now - tourSceneAt < 25) return;
            if (labPhoneAt < 0)
            {
                ObjectInteractive phone = null;
                foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>()) if (o != null && o.name == "Interactive Mobile") phone = o;
                if (phone == null) { if (now - tourSceneAt > 240) CheckOnce("phone.taken", false, "no phone to pick up"); return; }
                // The game arms the phone (ObjectInteractive.active) when the opening ends; a click before that does nothing.
                if (!phone.active) { if (now - tourSceneAt > 240) CheckOnce("phone.taken", false, "the phone was never armed"); return; }
                labPhoneAt = now;
                Log("Lab: the phone is armed " + (now - tourSceneAt).ToString("0") + " s into the chapter");
                DumpEvents("click", phone.eventClick, 0);
                DumpMagnets("before");
                Log("Lab: host picks up the phone");
                CutsceneRules.Rule rule = CutsceneRules.ForInteraction(SceneKeys.Key(phone), null);
                CheckOnce("phone.taken", rule.Who == CutsceneRules.Who.Actor, "the phone's rule: " + rule.Note);
                phone.Click();
            }
            if (labPhoneAt > 0 && now - labPhoneAt > 3 && !labMagnetsDumped) { labMagnetsDumped = true; DumpMagnets("after"); }
            foreach (string c in carrying) if (c.Contains("Smartphone")) CheckOnce("phone.held", true, "the phone rides in the host's hand: " + c.Split('/')[^1]);
        }

        // What an event calls, a few levels down (to see how the game wires an interaction).
        void DumpEvents(string label, UnityEngine.Events.UnityEventBase e, int depth)
        {
            if (e == null || depth > 3) return;
            for (int i = 0; i < e.GetPersistentEventCount(); i++)
            {
                Object t = e.GetPersistentTarget(i);
                string target = t == null ? "null" : t.name + " [" + t.GetIl2CppType().Name + "]";
                Log("Lab: " + new string(' ', depth * 2) + label + " -> " + target + "." + e.GetPersistentMethodName(i));
                ObjectAnimationPlayer a = t != null ? t.TryCast<ObjectAnimationPlayer>() : null;
                if (a != null) { DumpEvents("anim start", a.eventStartAnimaiton, depth + 1); if (a.eventsPlayer != null) for (int k = 0; k < a.eventsPlayer.Count; k++) DumpEvents("anim event " + k, a.eventsPlayer[k], depth + 1); DumpEvents("anim finish", a.eventFinish, depth + 1); }
                ObjectInteractive o = t != null ? t.TryCast<ObjectInteractive>() : null;
                if (o != null) DumpEvents("click", o.eventClick, depth + 1);
            }
        }

        void PhoneClient(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (scene == "Scene 1 - RealRoom") LabScene1Tutorial(now);
            PlayerMove player = LocalPlayer;
            if (scene != "Scene 1 - RealRoom" || player == null || !peers.ContainsKey(0)) return;
            Peer host = peers[0];
            bool hostHasPhone = false;
            foreach (string k in carried.Keys) if (k.Contains("Smartphone")) hostHasPhone = true;
            if (hostHasPhone && labPhoneSeenAt < 0) labPhoneSeenAt = now;
            // While the host holds the phone (and for 10 s after it showed up): this player does not pick one up.
            if (labPhoneSeenAt > 0 && now - labPhoneSeenAt < 10 && player.animationRun) labPhoneMoved = true;
            if (labPhoneSeenAt > 0 && now - labPhoneSeenAt > 10)
            {
                labPhoneHostActed = true;
                CheckOnce("phone.still", !labPhoneMoved, labPhoneMoved ? "this player picked up a phone too" : "this player's character did not pick up a phone while the host held it");
            }
            foreach (KeyValuePair<string, Carried> pair in carried)
            {
                if (!pair.Key.Contains("Smartphone") || Time.realtimeSinceStartup - pair.Value.Since < 1f || pair.Value.Magnet == null || host.Avatar == null || host.Avatar.Root == null) continue;
                // In the hand: within 0.3 m of a wrist or hand bone of the host's body here (the item rides on that bone).
                Transform moved = Moved(pair.Value.Magnet);
                float d = 99;
                foreach (Transform b in host.Avatar.Root.GetComponentsInChildren<Transform>())
                {
                    string n = b.name.ToLowerInvariant();
                    if (n.Contains("wrist") || n.Contains("hand")) d = Mathf.Min(d, Vector3.Distance(b.position, moved.position));
                }
                if (d < 0.3f) CheckOnce("phone.seen", true, "the phone is in the host's hand here (" + d.ToString("0.00") + " m from their hand)");
                else if (Time.realtimeSinceStartup - pair.Value.Since > 8) CheckOnce("phone.seen", false, "the phone is " + d.ToString("0.00") + " m from the host's hand here");
            }
            if (labPhoneHostActed)
            {
                ObjectInteractive mine = null;
                foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>(true)) if (o != null && o.name == "Interactive Mobile") mine = o;
                bool available = mine != null && mine.gameObject.activeInHierarchy && mine.enabled && mine.active;
                CheckOnce("phone.gone", !available, available ? "this player could still pick up a phone" : "no phone left to pick up here");
            }
        }
    }
}
