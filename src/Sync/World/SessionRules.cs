using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // Same starting state (docs/REMAKE-PLAN.md, Step 2b): things that differ per PC and change how the story runs are set
    // from the host for the length of the session, and put back when the player leaves. Nothing here is written to the
    // player's settings. Text language stays each player's own, so everyone can read; the voice follows the host because
    // voice length drives line ends. Photo mode and the console are off (ModHooks), since both change the local time scale.
    public sealed partial class TogetherRuntime
    {
        const float PhysicsStep = 1f / 60f;
        public static bool RulesOff;   // -mt-norules: the lab's proof that the samestart tour can fail
        SessionRules ownRules, hostRules;
        float ownFixedDt;
        bool haveOwnRules;

        sealed class SessionRules
        {
            public string Voice = "";
            public bool Halloween, Christmas, Glasses, NoLookStove, TakeMagnet, NoLookVent, TrySous, PlayConsole;
            public readonly HashSet<string> Saves = new HashSet<string>();

            public static SessionRules Capture()
            {
                return new SessionRules
                {
                    Voice = GlobalGame.LanguageVoice ?? "", Halloween = GlobalGame.halloween, Christmas = GlobalGame.christmas,
                    Glasses = Location34_Glasses.glassesMita, NoLookStove = Location4ChangeSide.nolookstove, TakeMagnet = Location4ChangeSide.takemagnetfridge,
                    NoLookVent = Location4ChangeSide.nolookventil, TrySous = Location4ChangeSide.trysous, PlayConsole = Location4ChangeSide.playconsole
                };
            }

            public void Write(BinaryWriter w)
            {
                Wire.Text(w, Voice);
                w.Write(Halloween); w.Write(Christmas); w.Write(Glasses); w.Write(NoLookStove); w.Write(TakeMagnet); w.Write(NoLookVent); w.Write(TrySous); w.Write(PlayConsole);
                w.Write((ushort)Saves.Count);
                foreach (string save in Saves) Wire.Text(w, save);
            }

            public static SessionRules Read(BinaryReader r)
            {
                SessionRules rules = new SessionRules { Voice = Wire.Text(r, 64) };
                rules.Halloween = r.ReadBoolean(); rules.Christmas = r.ReadBoolean(); rules.Glasses = r.ReadBoolean(); rules.NoLookStove = r.ReadBoolean();
                rules.TakeMagnet = r.ReadBoolean(); rules.NoLookVent = r.ReadBoolean(); rules.TrySous = r.ReadBoolean(); rules.PlayConsole = r.ReadBoolean();
                int saves = r.ReadUInt16();
                for (int i = 0; i < saves; i++) rules.Saves.Add(Wire.Text(r, 128));
                return rules;
            }

            // What the options menu could change mid-session: held by RulesTick.
            public void ApplySettings()
            {
                if (GlobalGame.LanguageVoice != Voice && Voice.Length > 0) GlobalGame.LanguageVoice = Voice;
                GlobalGame.halloween = Halloween; GlobalGame.christmas = Christmas;
            }

            // Changed by play, so only set when a packet arrives (join and each chapter start).
            public void ApplyStory()
            {
                Location34_Glasses.glassesMita = Glasses;
                Location4ChangeSide.nolookstove = NoLookStove; Location4ChangeSide.takemagnetfridge = TakeMagnet; Location4ChangeSide.nolookventil = NoLookVent;
                Location4ChangeSide.trysous = TrySous; Location4ChangeSide.playconsole = PlayConsole;
            }

            public override string ToString()
            {
                return "voice=" + Voice + " halloween=" + Halloween + " christmas=" + Christmas + " glasses=" + Glasses + " stove=" + NoLookStove + " magnet=" + TakeMagnet + " vent=" + NoLookVent + " sous=" + TrySous + " console=" + PlayConsole;
            }
        }

        // Called as a session opens; kept through a host migration, which is the same session.
        void EnterRules()
        {
            if (haveOwnRules) return;
            haveOwnRules = true;
            ownRules = SessionRules.Capture();
            ownFixedDt = Time.fixedDeltaTime;
        }

        void LeaveRules()
        {
            hostRules = null;
            if (!haveOwnRules) return;
            haveOwnRules = false;
            ownRules.ApplySettings();
            ownRules.ApplyStory();
            Time.fixedDeltaTime = ownFixedDt;
        }

        void RulesTick()
        {
            if (RulesOff) return;
            if (!Mathf.Approximately(Time.fixedDeltaTime, PhysicsStep)) Time.fixedDeltaTime = PhysicsStep;
            if (hostRules != null) hostRules.ApplySettings();
        }

        byte[] RulesPacket()
        {
            SessionRules rules = SessionRules.Capture();
            if (Directory.Exists(SaveDir))
                foreach (string file in Directory.GetFiles(SaveDir))
                {
                    string name = Path.GetFileName(file);
                    if (rules.Saves.Count < 256 && SaveMirrored(name)) rules.Saves.Add(name);
                }
            return Wire.Packet(Op.Rules, rules.Write);
        }

        void ClientRules(BinaryReader reader)
        {
            SessionRules rules = SessionRules.Read(reader);
            if (RulesOff) return;
            hostRules = rules;
            rules.ApplySettings();
            rules.ApplyStory();
            Log("MT-RULES " + rules + " saves=" + rules.Saves.Count);
            PruneOwnSaves(rules.Saves);
        }

        // The host's files have already been mirrored over this PC's. Any other file here would still count: a client's own
        // cartridges or PeaceMode unlock would change what exists and which answers are allowed. Only runs on a backed-up
        // folder, and the player's own files come back when they leave (RestoreSaves).
        void PruneOwnSaves(HashSet<string> hostSaves)
        {
            if (SecondCopy || !Directory.Exists(SaveDir)) return;
            if (!SavesBackedUp()) { Log("Saves: no backup, so this player's extra saves stay"); return; }
            foreach (string file in Directory.GetFiles(SaveDir))
            {
                string name = Path.GetFileName(file);
                if (hostSaves.Contains(name)) continue;
                try { File.Delete(file); Log("Saves: set aside " + name + " (the host doesn't have it)"); }
                catch (IOException ex) { Log("Saves: could not set aside " + name + ": " + ex.Message); }
            }
        }

        bool photoNoticeShown;

        public static bool PhotoModeAllowed()
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || RulesOff) return true;
            if (!rt.photoNoticeShown) { rt.photoNoticeShown = true; rt.Tell("Photo mode is off while playing together."); }
            return false;
        }

        public static bool ConsoleAllowed()
        {
            TogetherRuntime rt = Instance;
            return rt == null || !rt.Connected || RulesOff;
        }
    }
}
