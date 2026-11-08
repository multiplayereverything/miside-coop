using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Scene 4's card table (Location4TableCardGame) reads the player's buttons itself in Update (Input.GetButtonDown;
    // docs/INPUT-AUDIT.md). The pilot plays it as a player does: while the game's Update runs, the button it picked reads
    // as pressed once. Every button the game asks about is logged, and the pilot presses the one it pressed least.
    public sealed partial class TogetherRuntime
    {
        static bool labInCards;
        static string labCardButton;
        static readonly Dictionary<string, int> labCardButtons = new Dictionary<string, int>();
        static readonly Dictionary<string, float> labCardAsked = new Dictionary<string, float>();
        bool labCardsHooked;
        float labCardAt;

        static void LabCardsUpdatePrefix() { labInCards = true; }
        static void LabCardsUpdatePostfix() { labInCards = false; }
        static void LabCardsButtonPostfix(string __0, ref bool __result)
        {
            if (!labInCards || __0 == null) return;
            if (!labCardButtons.ContainsKey(__0)) { labCardButtons[__0] = 0; Log("Lab: the card game reads button " + __0); }
            labCardAsked[__0] = Time.realtimeSinceStartup;
            if (labCardButton == __0) { __result = true; labCardButton = null; labCardButtons[__0]++; Log("Lab: pilot presses " + __0 + " at the card table"); }
        }

        // Checks (scene4 tour):
        //   cards.oneplays: (every PC) the card game ran only on the Main Character's PC
        //   cards.done:     (host) the game ended and the story went on past it (it stalls when Mita doesn't follow the
        //                   player's PC: her animations drive the game there)
        int labCardsMine, labCardsOthers;
        bool labCardsRan;

        void LabCardsWatch(float now)
        {
            Location4TableCardGame cards = null;
            foreach (Location4TableCardGame c in Object.FindObjectsOfType<Location4TableCardGame>()) if (c != null && c.play) { cards = c; break; }
            if (cards == null) return;
            labCardsRan = true;
            if (NotMyMoment) labCardsOthers++; else labCardsMine++;
        }

        void LabCardsChecks()
        {
            // Only where the card table was played (Scene 4); elsewhere there is nothing to say.
            if (!labCardsHooked || !labCardsRan && !labCardsWasRunning) return;
            CheckOnce("cards.oneplays", labCardsOthers == 0 && (labCardsMine > 0 || labCardsRan == false), labCardsMine + " frames on the Main Character's PC, " + labCardsOthers + " on a PC that wasn't");
            if (Hosting) CheckOnce("cards.done", labCardsEnded && logKeysSeen.Count > labCardsEndKeys, labCardsEnded ? "ended; story events after it: " + (logKeysSeen.Count - labCardsEndKeys) : "the card game never ended");
        }

        bool labCardsEnded; int labCardsEndKeys;
        void LabCardsEndTick()
        {
            if (labCardsEnded || !Hosting) return;
            bool running = false;
            foreach (MinigameRun r in minigames.Values) if (MinigameKinds[r.Kind].Type == typeof(Location4TableCardGame)) running = true;
            if (running) labCardsWasRunning = true;
            else if (labCardsWasRunning) { labCardsEnded = true; labCardsEndKeys = logKeysSeen.Count; Log("Lab: the card game ended"); }
        }
        bool labCardsWasRunning;

        void PilotPlaysCards(float now)
        {
            LabCardsWatch(now);
            LabCardsEndTick();
            if (!labCardsHooked)
            {
                labCardsHooked = true;
                HarmonyLib.Harmony lab = new HarmonyLib.Harmony("local.misidetogether.lab.cards");
                lab.Patch(HarmonyLib.AccessTools.Method(typeof(Location4TableCardGame), "Update"), new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabCardsUpdatePrefix)), new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabCardsUpdatePostfix)));
                lab.Patch(HarmonyLib.AccessTools.Method(typeof(Input), "GetButtonDown", new[] { typeof(string) }), null, new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabCardsButtonPostfix)) { priority = HarmonyLib.Priority.First });
            }
            if (NotMyMoment || now < labCardAt || labCardButtons.Count == 0) return;
            Location4TableCardGame cards = null;
            foreach (Location4TableCardGame c in Object.FindObjectsOfType<Location4TableCardGame>()) if (c != null && c.play) { cards = c; break; }
            if (cards == null) return;
            labCardAt = now + 1.5f;
            Log("Lab: cards state play=" + cards.play + " show=" + cards.showCards + " canShow=" + cards.canShowCards + " playerHold=" + cards.playerHoldCards
                + " mitaHold=" + cards.mitaHoldCards + " card=" + cards.indexCard + " throw=" + cards.indexCardThrow + " mitaThrow=" + cards.indexCardMitaThrow
                + " step=" + cards.stepGame.ToString("0.0") + " steps=" + cards.countSteps + " stepTime=" + cards.timeStepGame.ToString("0.0") + " stop=" + cards.gameStop
                + " score=" + cards.scorePlayer + ":" + cards.scoreMita + " mitaShow=" + cards.cardMitaShow + " player.anim=" + (LocalPlayer != null && LocalPlayer.animationRun));
            // Only a button the game reads right now: in each step it asks about some of them only.
            string pick = null; int least = int.MaxValue; float asked;
            foreach (KeyValuePair<string, int> b in labCardButtons)
                if (b.Value < least && labCardAsked.TryGetValue(b.Key, out asked) && now - asked < 0.5f) { least = b.Value; pick = b.Key; }
            labCardButton = pick;
            if (labPilotDone.Add(cards.Pointer)) { Log("Lab: pilot plays cards"); labPilotActions++; }
        }
    }
}
