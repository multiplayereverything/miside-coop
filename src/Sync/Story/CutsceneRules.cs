using System;
using System.Collections.Generic;
using System.IO;

namespace MiSideTogether
{
    // How each cutscene, interaction animation and minigame is shared. ONE place to tune them, case by case.
    // The defaults below cover everything; a specific case is overridden by a rule whose Match is part of its scene key
    // (e.g. "Interactive Mobile" for the phone at the start). Rules can also be added without rebuilding, in
    // BepInEx/config/MiSideTogether.cutscenes.ini, one per line:
    //   Interactive Mobile | who=actor | pov=on | hide=off | gather=off   # comment
    // See CUTSCENES.md for the list of known cases and what each rule does.
    public static class CutsceneRules
    {
        public enum Kind { Interaction, Cutscene, Minigame }
        public enum Who { Actor, Everyone }

        public sealed class Rule
        {
            public string Match = "";
            public Who Who = Who.Actor;
            public bool Pov, Hide, Gather;
            public string Note = "";
            public Rule Copy() { return (Rule)MemberwiseClone(); }
        }

        // Defaults per kind.
        //  Interaction (pressing something that plays a player animation):
        //    puts an item in the player's hand (detected, HeldItems.cs; e.g. the toy plane): who=actor - only the presser
        //    plays it, the others see the item ride in their hand, and its events and finish run for everyone.
        //    otherwise "mirror": every player plays the same animation on their own character, at the same spot (two
        //    players on one spot do not see each other's body). pov=on adds watching through the actor's eyes.
        //  Cutscene (the game's cutscene mode or a camera timeline): everyone is in it; other players are hidden;
        //    everyone is gathered at the host afterwards.
        //  Minigame: only the player who started it plays; the others watch them play.
        static readonly Rule DefaultInteraction = new Rule { Who = Who.Everyone, Note = "default interaction (mirror)" };
        static readonly Rule DefaultCutscene = new Rule { Who = Who.Everyone, Hide = true, Gather = true, Note = "default cutscene" };
        static readonly Rule DefaultMinigame = new Rule { Who = Who.Actor, Note = "default minigame" };
        // An interaction that puts an item in the player's hand (found from the game's own wiring, HeldItems.cs): only
        // the presser performs it, and everyone sees the item in their hand.
        static readonly Rule DefaultHoldItem = new Rule { Who = Who.Actor, Note = "default interaction (holds an item)" };

        // Per-case overrides, tuned from play-testing. Add new cases here (or in the ini file).
        static readonly List<Rule> BuiltIn = new List<Rule>
        {
            // The core computer (Scenes 13 and 15) is one seat: whoever presses it sits and operates it, the others
            // watch through their eyes. Mirrored, every player sat down and only the operator's program could end the
            // seat, so the others stayed seated (every-chapter run, Scene 13). Playbook "how-coop-mods-work" 1.5:
            // the initiator picks, the others follow.
            new Rule { Match = "InteractivePC", Who = Who.Actor, Pov = true, Note = "core computer: one operator" },
        };

        static List<Rule> loaded;

        // Lab tours can force a rule for one case (e.g. the carry tour keeps the toy plane actor-only).
        public static void LabOverride(string match, Who who) { List<Rule> all = All; all.Insert(0, new Rule { Match = match, Who = who, Note = "lab override" }); }

        static List<Rule> All
        {
            get
            {
                if (loaded != null) return loaded;
                loaded = new List<Rule>();
                string path = Path.Combine(TogetherRuntime.Root, "BepInEx", "config", "MiSideTogether.cutscenes.ini");
                try
                {
                    if (File.Exists(path))
                        foreach (string raw in File.ReadAllLines(path))
                        {
                            string line = raw; int hash = line.IndexOf('#'); if (hash >= 0) line = line.Substring(0, hash);
                            string[] parts = line.Split('|');
                            if (parts.Length < 2 || parts[0].Trim().Length == 0) continue;
                            Rule r = new Rule { Match = parts[0].Trim(), Note = "ini" };
                            for (int i = 1; i < parts.Length; i++)
                            {
                                string[] kv = parts[i].Split('=');
                                if (kv.Length != 2) continue;
                                string k = kv[0].Trim().ToLowerInvariant(), v = kv[1].Trim().ToLowerInvariant();
                                bool on = v == "on" || v == "true" || v == "yes";
                                if (k == "who") r.Who = v == "everyone" ? Who.Everyone : Who.Actor;
                                if (k == "pov") r.Pov = on;
                                if (k == "hide") r.Hide = on;
                                if (k == "gather") r.Gather = on;
                            }
                            loaded.Add(r);
                        }
                }
                catch (Exception ex) { TogetherRuntime.Log("Cutscene rules file unreadable: " + ex.Message); }
                loaded.AddRange(BuiltIn);
                TogetherRuntime.Log("Cutscene rules: " + loaded.Count + " overrides");
                return loaded;
            }
        }

        // The first key with its own rule wins (an interaction has a click key and an animation key); else the default.
        public static Rule ForAny(Kind kind, string a, string b)
        {
            Rule r = For(kind, a);
            return r.Note.StartsWith("default", StringComparison.Ordinal) && !string.IsNullOrEmpty(b) ? For(kind, b) : r;
        }

        // An interaction's rule: an explicit rule for its click or animation wins; otherwise the default, which depends
        // on whether it puts an item in the player's hand.
        public static Rule ForInteraction(string clickKey, string animationKey)
        {
            Rule r = ForAny(Kind.Interaction, clickKey, animationKey);
            if (!r.Note.StartsWith("default", StringComparison.Ordinal)) return r;
            return HeldItems.Holds(clickKey, animationKey) ? DefaultHoldItem : r;
        }

        public static Rule For(Kind kind, string key)
        {
            if (!string.IsNullOrEmpty(key))
                foreach (Rule r in All) if (key.IndexOf(r.Match, StringComparison.OrdinalIgnoreCase) >= 0) return r;
            return kind == Kind.Interaction ? DefaultInteraction : kind == Kind.Minigame ? DefaultMinigame : DefaultCutscene;
        }
    }
}
