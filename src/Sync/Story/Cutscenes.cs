using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Cutscenes: MiSide plays them as Timeline assets through Playable_Animation (a PlayableDirector). The start is a
    // story event (Op.SceneLoad-style relay of Playable_Animation.Play), and while one runs the host streams its time so
    // every machine shows the same moment. Each player experiences it as the protagonist, so other players are hidden
    // while anyone is in a cutscene or a scripted player animation.
    public sealed partial class TogetherRuntime
    {
        List<Playable_Animation> cutsceneList;
        List<PlayableDirector> directorList;
        float cutsceneListAt, nextCutscene;
        public int cutsceneCorrections;
        public string lastCutsceneSync = "";

        List<Playable_Animation> Cutscenes()
        {
            if (cutsceneList != null && Time.realtimeSinceStartup - cutsceneListAt < 2f) return cutsceneList;
            cutsceneListAt = Time.realtimeSinceStartup;
            cutsceneList = new List<Playable_Animation>();
            foreach (Playable_Animation p in Object.FindObjectsOfType<Playable_Animation>(true)) if (p != null) cutsceneList.Add(p);
            // Every timeline, not only Playable_Animation ones: chapters also drive PlayableDirectors from their own scripts.
            directorList = new List<PlayableDirector>();
            foreach (PlayableDirector d in Object.FindObjectsOfType<PlayableDirector>(true)) if (d != null) directorList.Add(d);
            return cutsceneList;
        }

        List<PlayableDirector> Directors() { Cutscenes(); return directorList; }

        static PlayableDirector Director(Playable_Animation p)
        {
            if (p == null) return null;
            return p.scrpd != null ? p.scrpd : p.GetComponent<PlayableDirector>();
        }

        static bool Playing(PlayableDirector d) { return d != null && d.isActiveAndEnabled && d.state == PlayState.Playing; }

        // Cutscenes whose Playable_Animation.Play really ran here (it puts the player into the cutscene). A timeline can be
        // playing without it: the game switches the cutscene object on and the director plays on awake, while this
        // client's own Play became a request to the host. The host's command must still start it then.
        public static readonly HashSet<string> cutscenesStartedHere = new HashSet<string>();
        public static void CutsceneStartedHere(Playable_Animation p) { if (p != null) cutscenesStartedHere.Add(SceneKeys.Key(p)); }
        static bool NeedsStart(Playable_Animation p) { return p != null && (!Playing(Director(p)) || !cutscenesStartedHere.Contains(SceneKeys.Key(p))); }

        // True only during a real cutscene: the game's cutscene mode or a timeline that drives the camera. Ordinary
        // interaction animations (picking up the toy plane, sitting, opening things) do not count: players stay visible
        // there, and their arms and hands follow the bone sync.
        public bool LocalInCutscene()
        {
            if (cachedController == null) cachedController = Object.FindObjectOfType<GameController>();
            if (cachedController != null && cachedController.cutsceneRun) return true;
            foreach (Playable_Animation p in Cutscenes())
                if (p != null && p.useCamera && Playing(Director(p))) return true;
            return false;
        }

        // Host: the time of every running cutscene, five times a second.
        void HostCutsceneTick(float now)
        {
            if (now < nextCutscene) return;
            nextCutscene = now + 0.2f;
            List<KeyValuePair<string, double>> running = new List<KeyValuePair<string, double>>();
            foreach (PlayableDirector d in Directors())
                if (Playing(d) && running.Count < 16) running.Add(new KeyValuePair<string, double>(SceneKeys.Key(d), d.time));
            if (running.Count == 0) return;
            Broadcast(Wire.Packet(Op.Cutscene, w =>
            {
                w.Write(LocalSceneHash); w.Write((byte)running.Count);
                foreach (KeyValuePair<string, double> r in running) { Wire.Text(w, r.Key); w.Write(r.Value); }
            }));
        }

        void ClientCutscene(BinaryReader reader)
        {
            int scene = reader.ReadInt32();
            int count = reader.ReadByte();
            if (count > 16) throw new IOException("Too many cutscenes.");
            for (int i = 0; i < count; i++)
            {
                string key = Wire.Text(reader, 1024);
                double time = reader.ReadDouble();
                if (double.IsNaN(time) || time < 0 || time > 36000) throw new IOException("Invalid cutscene time.");
                if (scene != LocalSceneHash || !ClientStoryLive || replayQueue.Count > 0) continue;
                PlayableDirector d = SceneKeys.Find<PlayableDirector>(key);
                if (d == null || !d.gameObject.activeInHierarchy) continue;
                // A timeline of a presser-only interaction animation (Scene 1's phone) is the presser's: started here, it
                // ran the phone's story on this machine alone (the story autopilot found the client a quest ahead).
                if (d.GetComponent<ObjectAnimationPlayer>() != null && CutsceneRules.ForInteraction(null, key).Who == CutsceneRules.Who.Actor) continue;
                Playable_Animation p = d.GetComponent<Playable_Animation>();
                if (p != null && p.scrpd != d) p = null;
                if (!Playing(d) || (p != null && NeedsStart(p)))
                {
                    // The host is in a cutscene this machine did not start (a trigger only the host hit, or a timeline
                    // that plays on awake while this machine's start went to the host): start it here, through
                    // Playable_Animation when there is one so its camera, player and events run as in the game.
                    Replaying = true;
                    try { if (p != null) p.Play(); else d.Play(); } finally { Replaying = false; }
                    Log("Cutscene started from the host's: " + key);
                }
                // Half the round trip is unknown here; 0.25 s of drift is invisible, larger drift is corrected.
                if (Math.Abs(d.time - time) > 0.25)
                {
                    d.time = time;
                    d.Evaluate();
                    cutsceneCorrections++;
                }
                lastCutsceneSync = key + " @" + time.ToString("0.00");
            }
        }
    }
}
