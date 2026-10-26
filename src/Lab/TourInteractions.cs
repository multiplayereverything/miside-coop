using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "interactions" (2 players): mirroring across a whole chapter (-Chapter picks it; scripts/interactions-all.ps1
    // runs every chapter). The host presses the chapter's interactions one at a time (up to 25). For every press that
    // animated the host's character, the client's own character must have played it too (mirror) at the same spot, or
    // stayed put when the interaction hands over an item (presser only). Both must be free again afterwards.
    // Every press gets an MT-PRESS line on the client: key|rule|played here|distance between the two spots.
    public sealed partial class TogetherRuntime
    {
        const int MaxLabPresses = 25;
        List<ObjectInteractive> labPresses;
        int labPressIndex, labPressed, labSkipped, labHostAnimated, labBothAnimated, labPlacedOk, labPlacedChecked;
        float labPressAt = -1, labLastAnimAt = -100, labPressNoticeAt = -1;
        bool labPressAnimated, labMyAnimating, labAnimatingAtPress;
        int labStateAtPress, labMyState;
        string labPressKey;
        Vector3 labPressSpot, labMyAnimSpot;

        void InteractionsHost(float now, string scene)
        {
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; Log("Lab: host loading " + TourChapter); LoadThroughGame(TourChapter); }
            PlayerMove player = LocalPlayer;
            if (scene != TourChapter || player == null || now - tourSceneAt < 25) return;
            // The spot is where the character is when its animation ends (the game walks or teleports it into place first),
            // its last animating frame as on the client, even when it ends in the state it had at the press.
            int state = AnimState(player);
            if (player.animationRun && (labPressAnimated || !labAnimatingAtPress || state != labStateAtPress)) { labPressAnimated = true; labPressSpot = player.transform.position; }
            if (labPresses == null)
            {
                labPresses = new List<ObjectInteractive>();
                foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                    if (o != null && o.gameObject.activeInHierarchy && o.GetComponent<Scene_Load>() == null) labPresses.Add(o);
                // Doors last: they may lead out of the chapter.
                labPresses.Sort((a, b) => { int da = a.name.Contains("Door") ? 1 : 0, db = b.name.Contains("Door") ? 1 : 0; return da != db ? da - db : string.CompareOrdinal(SceneKeys.Key(a), SceneKeys.Key(b)); });
                Log("Lab: " + labPresses.Count + " interactions to press in " + scene);
            }
            // Report the last press 5 s after it, then press the next one once the host is idle again.
            if (labPressAt > 0 && labPressNoticeAt < 0 && now - labPressAt > 5)
            {
                labPressNoticeAt = now;
                ObjectInteractive pressed = SceneKeys.Find<ObjectInteractive>(labPressKey);
                Vector3 s = labPressSpot;
                bool minigame = Time.realtimeSinceStartup - lastMinigameStartAt < 6;
                // The mod's own rule for this press, with the animation it really started (its events may hand over the item).
                string animation = actorAnimationAt >= labPressAt ? actorAnimation : null;
                bool presser = CutsceneRules.ForInteraction(labPressKey, animation).Who == CutsceneRules.Who.Actor;
                string text = "lab-press:" + labPressKey + "|" + (labPressAnimated ? 1 : 0) + "|" + (minigame ? 2 : presser ? 1 : 0)
                    + "|" + string.Format(CultureInfo.InvariantCulture, "{0:F2},{1:F2},{2:F2}", s.x, s.y, s.z);
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, text)));
                Log("Lab: pressed " + labPressKey + " animated=" + labPressAnimated);
                if (labPressAnimated) labHostAnimated++;
            }
            // Next press once the host is idle again (or 12 s after: a press can open a view that waits for input).
            bool idle = !player.animationRun && !LocalInCutscene();
            if (labPressAt < 0 || (labPressNoticeAt > 0 && now - labPressNoticeAt > 2 && (idle || now - labPressAt > 12)))
            {
                if (labPressIndex >= labPresses.Count || labPressed >= MaxLabPresses)
                {
                    // The host's own state goes along: a chapter that keeps everyone in a scripted animation is not a stuck client.
                    string done = "lab-press-done|" + (idle ? 1 : 0);
                    Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, done)));
                    // A chapter whose open interactions play no animation at its start is still covered (nothing to mirror).
                    CheckOnce("interactions.pressed", labPressed > 0 || labSkipped == labPresses.Count, labPressed + " of " + labPresses.Count + " pressed (" + labSkipped + " not armed yet), " + labHostAnimated + " animated the host");
                    return;
                }
                ObjectInteractive o = labPresses[labPressIndex++];
                if (o == null || !o.gameObject.activeInHierarchy) return;
                // Not armed yet by the story (ObjectInteractive.active): a click would do nothing, so it is not a test.
                if (!o.active) { labSkipped++; Log("Lab: skipped " + SceneKeys.Key(o) + " (not armed by the story yet)"); return; }
                labPressKey = SceneKeys.Key(o); labPressAnimated = false; labPressAt = now; labPressNoticeAt = -1; labPressed++;
                labAnimatingAtPress = player.animationRun; labStateAtPress = AnimState(player);
                try { o.Click(); } catch (Exception ex) { Log("Lab: press threw: " + ex.Message); }
            }
        }

        // Client: did this player's own character animate within the host's press window, and where?
        void InteractionsClient(float now)
        {
            PlayerMove player = LocalPlayer;
            bool animating = player != null && player.animationRun;
            if (animating) labMyAnimSpot = player.transform.position;
            // Only an animation that starts counts (a chapter can keep this player in one for a long time).
            int state = AnimState(player);
            if (animating && (!labMyAnimating || state != labMyState)) labLastAnimAt = now;
            labMyAnimating = animating; labMyState = state;
        }

        void InteractionsNotice(string notice)
        {
            float now = Time.realtimeSinceStartup;
            if (notice.StartsWith("lab-press-done", StringComparison.Ordinal))
            {
                PlayerMove player = LocalPlayer;
                bool hostFree = !notice.EndsWith("|0", StringComparison.Ordinal);
                CheckOnce("interactions.mirrored", labBothAnimated >= labHostAnimated * 0.9f, labBothAnimated + " of " + labHostAnimated + " host interactions behaved right here (mirrored, or presser-only when they hand over an item)");
                CheckOnce("interactions.placed", labPlacedOk >= labPlacedChecked * 0.9f, labPlacedOk + " of " + labPlacedChecked + " mirrored interactions played within 0.6 m of the host's spot");
                CheckOnce("interactions.free", player != null && (!player.animationRun || !hostFree), player != null && player.animationRun ? "in an animation at the end, like the host (the chapter's own)" : "not stuck in an animation at the end");
                return;
            }
            string[] p = notice.Substring("lab-press:".Length).Split('|');
            if (p.Length < 2) return;
            // p[2]: 1 hands the presser an item, 2 started a minigame (both: only the presser plays it).
            bool hostPlayed = p[1] == "1", mine = now - labLastAnimAt < 6.5f;
            // Presser-only by the host's labelling, or because this machine's own rule held the animation back (MT-HOLD).
            bool heldBack = now - lastHeldBackAt < 6.5f;
            bool held = p.Length > 2 && (p[2] == "1" || p[2] == "2") || (heldBack && !mine);
            string distance = "-";
            if (hostPlayed)
            {
                labHostAnimated++;
                // Holding an item: only the presser plays it (this player stays put). Otherwise: mirrored here too.
                if (held ? !mine : mine) labBothAnimated++;
                if (!held && mine && p.Length > 3 && TryParseSpot(p[3], out Vector3 hostSpot))
                {
                    float d = Vector3.Distance(hostSpot, labMyAnimSpot);
                    distance = d.ToString("F2", CultureInfo.InvariantCulture);
                    labPlacedChecked++;
                    if (d < 0.6f) labPlacedOk++;
                }
            }
            string rule = !hostPlayed ? "none" : p[2] == "2" ? "minigame" : held ? "presser" : "mirror";
            Log("MT-PRESS " + ActiveSceneName + "|" + p[0] + "|" + rule + "|" + (mine ? 1 : 0) + "|" + distance);
        }

        static int AnimState(PlayerMove player)
        {
            return player != null && player.animPerson != null ? player.animPerson.GetCurrentAnimatorStateInfo(0).shortNameHash : 0;
        }

        static bool TryParseSpot(string text, out Vector3 spot)
        {
            spot = Vector3.zero;
            string[] c = text.Split(',');
            if (c.Length != 3) return false;
            NumberStyles f = NumberStyles.Float; CultureInfo inv = CultureInfo.InvariantCulture;
            if (!float.TryParse(c[0], f, inv, out float x) || !float.TryParse(c[1], f, inv, out float y) || !float.TryParse(c[2], f, inv, out float z)) return false;
            spot = new Vector3(x, y, z);
            return true;
        }
    }
}
