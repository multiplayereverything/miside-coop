using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // The story log (docs/REMAKE-PLAN.md, Step 3). In a chapter moved to it, the host numbers every story event and every
    // client runs them in that order. An event whose object isn't ready here waits, and everything after it waits too:
    // quests destroy themselves, so a skipped step can't be redone (docs/GAME-STUDY.md).
    //
    // Timers, line ends and animation ends fire from each PC's own clock. Each such UnityEvent gets a stand-in, on every
    // machine, made once as the chapter loads:
    //   host:   the stand-in logs the event, then runs the real one
    //   client: the stand-in does nothing, and the real one runs when the host's log reaches it
    // UnityEvents can be invoked from many places, some inlined, so swapping the event covers them all without a hook on each.
    public sealed partial class TogetherRuntime
    {
        public static bool LogOff;   // -mt-nolog: the old relay, the lab's proof that the storylog tour can fail
        public static bool GateRequestsOff;   // -mt-nogaterequest: the lab's proof that a client Main Character's run can fail
        public static bool TakeShortcutOff;   // -mt-notake: another player's take sends this PC's hand out, the proof that pilot.pressable can fail
        public static bool HoldGatesOff;   // -mt-nohold: holds' events stay local, the lab's proof that hold.same can fail
        public static string LogAlso = "";   // -mt-logalso <chapter>: tries a chapter on the log before it joins the list
        const float LogStuckSeconds = 12;

        readonly SortedDictionary<int, byte[]> logQueue = new SortedDictionary<int, byte[]>();
        readonly Dictionary<string, UnityEvent> gatedEvents = new Dictionary<string, UnityEvent>();
        // Held so the stand-ins' delegates aren't collected while the game can still call them.
        readonly List<UnityAction> gateActions = new List<UnityAction>();
        int logSeq, logNext, logApplied, logWaited, logLost, gateSwallowed, gateFired;
        string logWaitingOn = "";
        float logBlockedSince = -1, logRetryAt;
        bool gatesBuilt, ownRequest;
        string gatesScene = "";
        readonly HashSet<string> ownRequests = new HashSet<string>();
        // Whose moment it is: the last press in the log, so every PC agrees. A press stays theirs until the next one,
        // because its animation can start much later (Scene 3's selfie, on a line's end). The host until anyone presses.
        bool momentMine;
        // Different events logged: the lab's stall check, since a looping timer (Scene 3's doorbell) logs forever.
        readonly HashSet<string> logKeysSeen = new HashSet<string>();

        public static bool LogChapter(string scene) { return !LogOff && (scene == "Scene 1 - RealRoom" || scene == "Scene 2 - InGame" || scene == "Scene 3 - WeTogether" || scene == "Scene 4 - StartSecret" || scene == "Scene 5 - StartHorror" || scene == "Scene 15 - BasementAndDeath" || scene == LogAlso); }

        bool InLogChapter { get { string s = ActiveSceneName; return Connected && (LogChapter(s) || s == logContinuation); } }
        public static bool InStoryLog { get { TogetherRuntime rt = Instance; return rt != null && rt.InLogChapter; } }

        void ResetStoryLog()
        {
            // The stand-ins stay until the chapter changes: the game keeps calling them after a session ends here.
            logQueue.Clear(); ownRequests.Clear(); hostRequests.Clear();
            logSeq = 0; logNext = 0; logBlockedSince = -1;
        }

        // --- Host: numbering ----------------------------------------------------------------------
        // origin: the player who already did this locally (a walked-into trigger, say), who skips it. -1: everyone runs it.
        void HostLog(Op op, string key, int arg, int origin = -1)
        {
            if (holdingLogs != null) { holdingLogs.Add(new HostRequestEntry { Op = op, Key = key, Arg = arg, Origin = origin }); return; }
            if (origin >= 0) lastStoryPlayer = origin;
            int seq = logSeq++;
            logKeysSeen.Add(key);
            byte[] packet = Wire.Packet(Op.Ordered, w => { w.Write(seq); w.Write((byte)op); w.Write(origin); Wire.Text(w, key); w.Write(arg); });
            RecordHistory(packet);
            Broadcast(packet);
            Log("MT-LOG " + seq + " " + op + " " + key + (arg != 0 ? " " + arg : ""));
        }

        // A client asked for something: the host runs it first, so a press that can't work here never enters the log. What
        // the press sets off is logged after the press itself.
        sealed class HostRequestEntry { public Op Op; public string Key; public int Arg, Origin, From; public float Until; }
        readonly List<HostRequestEntry> hostRequests = new List<HostRequestEntry>();
        List<HostRequestEntry> holdingLogs;

        void HostRequest(Op op, BinaryReader reader, int from)
        {
            string key = Wire.Text(reader, 1024);
            int arg = reader.ReadInt32();
            if (op == Op.Gate && !GateRequestAllowed(key, from)) return;
            bool request = op == Op.Interact || op == Op.MouseClick || op == Op.Gate;
            HostRequestEntry entry = new HostRequestEntry { Op = op, Key = key, Arg = arg, Origin = request ? -1 : from, From = from, Until = Time.realtimeSinceStartup + 5 };
            if (!RunRequest(entry)) hostRequests.Add(entry);
        }

        bool RunRequest(HostRequestEntry entry)
        {
            List<HostRequestEntry> held = holdingLogs = new List<HostRequestEntry>();
            retryingStory = true;
            try { ApplyStory(entry.Op, entry.Key, entry.Arg); }
            finally { retryingStory = false; holdingLogs = null; }
            if (storyMissing) return false;
            HostLog(entry.Op, entry.Key, entry.Arg, entry.Origin);
            foreach (HostRequestEntry h in held) HostLog(h.Op, h.Key, h.Arg, h.Origin);
            return true;
        }

        void HostRequestTick(float now)
        {
            for (int i = hostRequests.Count - 1; i >= 0; i--)
            {
                if (RunRequest(hostRequests[i])) hostRequests.RemoveAt(i);
                else if (now >= hostRequests[i].Until)
                {
                    Log("MT-LOG refused " + hostRequests[i].Op + " " + hostRequests[i].Key + ": not possible on the host");
                    // Said to the player, so a press that did nothing isn't pressed thirty more times (2026-11-08, a battery).
                    if (hostRequests[i].Op == Op.Interact || hostRequests[i].Op == Op.MouseClick)
                        Network.Send(hostRequests[i].From, Wire.Packet(Op.Notice, w => Wire.Text(w, "The host couldn't do that: another player already did it, or the host isn't there yet.")));
                    hostRequests.RemoveAt(i);
                }
            }
        }

        void ChapterLogDone()
        {
            if (logApplied > 0 || gateFired > 0) Log("MT-LOG chapter done: applied " + logApplied + ", waited " + logWaited + ", lost " + logLost + ", timers swallowed " + gateSwallowed + ", host fired " + gateFired);
            if (LabMode && logApplied > 0) CheckOnce("log.clean", logLost == 0, "applied " + logApplied + ", waited " + logWaited + ", lost " + logLost + ", swallowed " + gateSwallowed);
            gatesBuilt = false; gatesScene = ""; gatedScenes.Clear(); gatedEvents.Clear(); gateActions.Clear(); hostRequests.Clear(); gateKinds.Clear(); gateSources.Clear(); realOfStandIn.Clear(); gateOwners.Clear(); gatePressedAt.Clear(); ownBody.Clear();
            logApplied = 0; logWaited = 0; logLost = 0; gateSwallowed = 0; gateFired = 0;
        }

        // --- Client: running them in order --------------------------------------------------------
        void ClientOrdered(BinaryReader reader)
        {
            int seq = reader.ReadInt32();
            if (seq < logNext || logQueue.ContainsKey(seq)) return;
            long start = reader.BaseStream.Position;
            logQueue[seq] = Transport.ReadExactly(reader.BaseStream, (int)(reader.BaseStream.Length - start));
        }

        void StoryLogTick(float now)
        {
            if (!IsClient || logQueue.Count == 0 || !ClientStoryLive || replayQueue.Count > 0 || now < logRetryAt) return;
            // History can start later than 0 if the host's list overflowed: start from the first the host still has.
            int first = -1;
            foreach (int seq in logQueue.Keys) { first = seq; break; }
            if (first > logNext)
            {
                if (historySeq != wantedSeq) return;
                Log("MT-LOG lost " + (first - logNext) + " event(s) before " + first);
                logLost += first - logNext;
                logNext = first;
            }
            while (logQueue.ContainsKey(logNext))
            {
                using (BinaryReader reader = new BinaryReader(new MemoryStream(logQueue[logNext])))
                {
                    Op op = (Op)reader.ReadByte();
                    int origin = reader.ReadInt32();
                    string key = Wire.Text(reader, 1024);
                    int arg = reader.ReadInt32();
                    lastStoryPlayer = origin >= 0 ? origin : 0;   // no origin: the host's own event
                    if (origin == self) { logQueue.Remove(logNext); logNext++; logApplied++; continue; }
                    retryingStory = true;
                    bool own = (op == Op.Interact || op == Op.Gate) && ownRequests.Remove(key);
                    ownRequest = own;
                    try { ApplyStory(op, key, arg); }
                    finally { retryingStory = false; ownRequest = false; }
                    if (storyMissing)
                    {
                        if (own) ownRequests.Add(key);
                        if (logBlockedSince < 0) { logBlockedSince = now; logWaited++; logWaitingOn = op + " " + key.Substring(key.LastIndexOf('/') + 1); Log("MT-LOG waiting at " + logNext + " for " + op + " " + key); }
                        if (now - logBlockedSince < LogStuckSeconds) { logRetryAt = now + 0.25f; return; }
                        // Long past anything the story could still do here. Repair (StoryCheck.cs) tries it again.
                        Log("MT-LOG stuck at " + logNext + " for " + LogStuckSeconds + " s, moving on: " + op + " " + key);
                        logLost++;
                        logSkipped.Add(logQueue[logNext]);
                        LogSameNamed(key);
                    }
                    else if (logBlockedSince >= 0) Log("MT-LOG applied " + logNext + " after waiting " + (now - logBlockedSince).ToString("0.0") + " s");
                }
                logBlockedSince = -1;
                logQueue.Remove(logNext);
                logNext++;
                logApplied++;
            }
        }

        // For the report: what this copy has under the stuck key's last name, since the key itself found nothing.
        static void LogSameNamed(string key)
        {
            string leaf = key.Substring(key.LastIndexOf('/') + 1);
            int hash = leaf.LastIndexOf('#');
            if (hash > 0) leaf = leaf.Substring(0, hash);
            foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
                if (t != null && t.name == leaf) Log("MT-LOG same name here: " + PathOf(t) + (t.gameObject.activeInHierarchy ? "" : " (off)"));
        }

        // --- Stand-ins ----------------------------------------------------------------------------
        // Chapter scenes whose events have stand-ins: the active one, and any the story loaded beside it (Scene 5 loads
        // Scene 6 additively and the players go on into its basement while Scene 5 stays the active scene).
        readonly HashSet<string> gatedScenes = new HashSet<string>();
        string gatingScene;

        void BuildGates()
        {
            string scene = ActiveSceneName;
            if (!gatesBuilt || gatesScene != scene)
            {
                if (!InLogChapter || (IsClient && wantedScene != scene)) return;
                gatesBuilt = true; gatesScene = scene;
                momentMine = Hosting;
                gatedEvents.Clear(); gateActions.Clear(); gatedScenes.Clear();
                BuildGatesIn(scene);
                return;
            }
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name.StartsWith("Scene ", StringComparison.Ordinal) && !gatedScenes.Contains(s.name)) BuildGatesIn(s.name);
            }
        }

        void BuildGatesIn(string scene)
        {
            gatingScene = scene; gatedScenes.Add(scene);
            int before = gatedEvents.Count;
            foreach (Time_Events t in Object.FindObjectsOfType<Time_Events>(true))
            {
                if (t == null || t.EventsOnTime == null || SceneKeys.Key(t).Contains("/Mimic Back")) continue;   // the mirror scare is each player's own
                for (int i = 0; i < t.EventsOnTime.Length; i++) { TimePoint p = t.EventsOnTime[i]; if (p != null) p._event = Gate(t, "t" + i, p._event); }
            }
            foreach (Mob_ChibiMita_Animation a in Object.FindObjectsOfType<Mob_ChibiMita_Animation>(true))
            {
                if (a == null) continue;
                a.eventStartLoop = Gate(a, "loop", a.eventStartLoop);
                a.eventFinish = Gate(a, "end", a.eventFinish);
            }
            foreach (Dialogue_3DText d in Object.FindObjectsOfType<Dialogue_3DText>(true)) if (d != null) d.eventFinish = Gate(d, "line", d.eventFinish);
            foreach (Tamagotchi_Dialogue_Mob d in Object.FindObjectsOfType<Tamagotchi_Dialogue_Mob>(true)) if (d != null) d.eventStop = Gate(d, "stop", d.eventStop);
            // Eyes covered, then the story goes on (Scene 3: teleport to the black room). It plays on the acting player's PC only.
            foreach (BlackScreenAnimation b in Object.FindObjectsOfType<BlackScreenAnimation>(true)) if (b != null) b.eventStop1 = Gate(b, "stop1", b.eventStop1, GateKind.First);
            foreach (Playable_Animation p in Object.FindObjectsOfType<Playable_Animation>(true)) if (p != null) p.eventStop = Gate(p, "cut", p.eventStop);
            foreach (ObjectAnimationPlayer a in Object.FindObjectsOfType<ObjectAnimationPlayer>(true))
            {
                if (a == null) continue;
                a.eventStartAnimaiton = Gate(a, "bodystart", a.eventStartAnimaiton, GateKind.Body);
                a.eventStartLoop = Gate(a, "bodyloop", a.eventStartLoop, GateKind.Body);
                a.eventFinish = Gate(a, "bodyend", a.eventFinish, GateKind.Body);
                if (a.eventsPlayer != null) for (int i = 0; i < a.eventsPlayer.Count; i++) a.eventsPlayer[i] = Gate(a, "body" + i, a.eventsPlayer[i], GateKind.Body);
            }
            // A hold (Scene 4's scarf) reads the holder's key in its own Update: its end (eventReady) is the holder's, like
            // the animation it belongs to (the source is that animation, so a client holding asks the host). Its other
            // events fire every frame while held and nothing in the story listens to them, so they stay local.
            if (!HoldGatesOff)
            foreach (ObjectAnimationPlayerHold h in Object.FindObjectsOfType<ObjectAnimationPlayerHold>(true))
            {
                ObjectAnimationPlayer body = h == null ? null : h.scroap != null ? h.scroap : h.GetComponent<ObjectAnimationPlayer>();
                if (body != null) h.eventReady = Gate(body, "holdready", h.eventReady, GateKind.Body);
            }
            foreach (Player_Look l in Object.FindObjectsOfType<Player_Look>(true)) if (l != null) l.eventEnd = Gate(l, "look", l.eventEnd);
            foreach (Animator_Functions f in Object.FindObjectsOfType<Animator_Functions>(true))
            {
                if (f == null || f._events == null) continue;
                AlwaysAnimate(f);
                for (int i = 0; i < f._events.Length; i++) f._events[i] = Gate(f, "anim" + i, f._events[i], GateKind.First);
            }
            foreach (Animator_OneTimeDestroy d in Object.FindObjectsOfType<Animator_OneTimeDestroy>(true)) if (d != null) { AlwaysAnimate(d); d.eventFinish = Gate(d, "once", d.eventFinish, GateKind.First); }
            foreach (Interface_KeyHint_Key k in Object.FindObjectsOfType<Interface_KeyHint_Key>(true)) if (k != null) k.eventKeyDown = Gate(k, "key", k.eventKeyDown, GateKind.Press);
            foreach (Location2LongCorridor c in Object.FindObjectsOfType<Location2LongCorridor>(true)) if (c != null) c.eventNear = Gate(c, "near", c.eventNear, GateKind.First);
            foreach (Location3CutCarrot c in Object.FindObjectsOfType<Location3CutCarrot>(true)) if (c != null) c.eventFinish = Gate(c, "cut", c.eventFinish, GateKind.Moment);
            foreach (Location3WalkToToilet w in Object.FindObjectsOfType<Location3WalkToToilet>(true)) if (w != null) w.eventFinish = Gate(w, "walk", w.eventFinish, GateKind.Moment);
            // Only the story's own: other visibility events (a monitor's screen) belong to each player's camera.
            foreach (Mesh_VisibleEvents v in Object.FindObjectsOfType<Mesh_VisibleEvents>(true))
                if (v != null && StoryPath(v.transform)) { v.eventVisibleEnter = Gate(v, "seen", v.eventVisibleEnter, GateKind.First); v.eventVisibleExit = Gate(v, "unseen", v.eventVisibleExit, GateKind.First); }
            foreach (Tamagotchi_Main m in Object.FindObjectsOfType<Tamagotchi_Main>(true))
            {
                if (m == null) continue;
                m.eventAnimationZoomBack = Gate(m, "zoomback", m.eventAnimationZoomBack);
                m.eventAnimationZoomStop = Gate(m, "zoomstop", m.eventAnimationZoomStop);
            }
            KeepRealEventsFound();
            gatingScene = null;
            Log("MT-LOG " + (gatedEvents.Count - before) + " story events gated in " + scene + (scene != gatesScene ? " (loaded beside " + gatesScene + ")" : ""));
        }

        // Who may set off a stand-in's event:
        //   Host:  the host's own clock. A client's copy never does.
        //   Body:  the end or a mid-point of a player animation, so the player performing it. A client asks the host.
        //   Press: a key prompt. Only a player inside an animation has one to answer (the others see the same prompt on
        //          screen, and pressing it must not pull them into the Main Character's moment).
        //   First: whichever player's copy gets there first, and from then on only that player's (a corridor's end, an
        //          object leaving view, an animator in a room that is only switched on near a player).
        // Moment: fired by a script playing out on the Main Character's own PC (Scene 3's carrot, driven by their mouse).
        enum GateKind { Host, Body, Press, First, Moment }
        const float PressRepeat = 2;
        readonly Dictionary<string, GateKind> gateKinds = new Dictionary<string, GateKind>();
        readonly Dictionary<string, int> gateOwners = new Dictionary<string, int>();
        readonly Dictionary<string, float> gatePressedAt = new Dictionary<string, float>();
        // Animations this client is performing as the Main Character, by scene key.
        readonly HashSet<IntPtr> ownBody = new HashSet<IntPtr>();
        readonly Dictionary<string, IntPtr> gateSources = new Dictionary<string, IntPtr>();

        UnityEvent Gate(Component source, string which, UnityEvent real, GateKind kind = GateKind.Host, bool lateBuilt = false)
        {
            if (real == null) return null;
            // The player's own things (the idle Tetris, say) outlive the chapter and aren't the story.
            // While a scene is being built only its own components; later (GateReadyEvent) any gated scene's.
            string sceneName = source.gameObject.scene.name;
            if (gatingScene != null ? sceneName != gatingScene : !gatedScenes.Contains(sceneName)) return real;
            // An object can carry two of the same script, so the component's place is part of the key.
            // Built late, the sibling numbers in a key may already differ between the copies, so that key goes by name alone.
            string path = SceneKeys.Key(source);
            string key = which + "@" + ComponentIndex(source) + ":" + (lateBuilt ? System.Text.RegularExpressions.Regex.Replace(path, "#[0-9]+", "") : path);
            gatedEvents[key] = real;
            gateKinds[key] = kind;
            IntPtr pointer = source.Pointer;
            gateSources[key] = pointer;
            Action fired = () => GateFired(key, kind, pointer);
            UnityAction action = DelegateSupport.ConvertDelegate<UnityAction>(fired);
            gateActions.Add(action);
            UnityEvent standIn = new UnityEvent();
            standIn.AddListener(action);
            realOfStandIn[standIn.Pointer] = real;
            return standIn;
        }

        // The game's asset sweep (a chapter preload runs one) only follows what scene objects hold. An animation clip named
        // only by a real event, now held by this mod alone, got unloaded (Scene 2, Quest 4 threw on both PCs and the rest of
        // that step never ran). So a switched-off game component holds them too. Switched off, its own code never runs.
        Animator_Functions realEventsHolder;
        void KeepRealEventsFound()
        {
            if (realEventsHolder == null)
            {
                GameObject holder = new GameObject("MiSideTogether story events");
                holder.SetActive(false);
                realEventsHolder = holder.AddComponent<Animator_Functions>();
            }
            Il2CppReferenceArray<UnityEvent> events = new Il2CppReferenceArray<UnityEvent>(gatedEvents.Count);
            int i = 0;
            foreach (UnityEvent real in gatedEvents.Values) events[i++] = real;
            realEventsHolder._events = events;
        }

        // "When Mita is ready" fires a stored event from each PC's own Mita arriving (Scene 3's television started 10 s late
        // on a client). The event is only known when the story asks for it, so its stand-in goes in then, on every PC.
        public static void GateReadyEvent(Events_Data e)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.InLogChapter || e == null || e._event == null || e._event.Length == 0) return;
            UnityEvent first = e._event[0];
            if (first == null || rt.realOfStandIn.ContainsKey(first.Pointer)) return;
            e._event[0] = rt.Gate(e, "ready", first, GateKind.Host, true);
            rt.KeepRealEventsFound();
        }

        // An animator out of view can stop running, and the story would then wait on where the players are looking.
        static void AlwaysAnimate(Component source)
        {
            Animator animator = source.GetComponent<Animator>();
            if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        // Code that reads the game's wiring (HeldItems.cs) needs the real event, not the stand-in in its place.
        readonly Dictionary<IntPtr, UnityEvent> realOfStandIn = new Dictionary<IntPtr, UnityEvent>();
        public static UnityEventBase RealEvent(UnityEventBase e)
        {
            UnityEvent real;
            return e != null && Instance != null && Instance.realOfStandIn.TryGetValue(e.Pointer, out real) ? real : e;
        }

        static int ComponentIndex(Component source)
        {
            Component[] all = source.GetComponents<Component>();
            for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].Pointer == source.Pointer) return i;
            return -1;
        }

        // Before the click's code runs: it can start the moment's animation at once. A client's press waits for the log instead.
        public static bool NotMyMoment { get { TogetherRuntime rt = Instance; return rt != null && rt.Connected && rt.InLogChapter && !rt.momentMine; } }
        public static void NoteHostPress() { TogetherRuntime rt = Instance; if (rt != null && rt.Hosting && rt.InLogChapter) rt.momentMine = true; }

        // A prompt belongs to whoever the moment has hold of: in an animation, or locked in place by Player_Position (Scene 3's glasses).
        bool MayAnswerPrompt { get { PlayerMove p = LocalPlayer; return p != null && (p.animationRun || p.dontMove); } }

        // Some game events fire every frame (Scene 5's BloodFloor ToiletDoor: an Animator_OneTimeDestroy kept alive by
        // dontDeleteThis calls Finish each frame). A repeat within RepeatSeconds runs here only: it isn't logged again
        // or asked for, so it can't send 30 entries a second to every client. Prompts keep their own 2 s rule.
        const float RepeatSeconds = 0.5f;
        readonly Dictionary<string, float> gateLastFired = new Dictionary<string, float>();
        bool GateRepeat(string key)
        {
            float now = Time.realtimeSinceStartup, at;
            bool repeat = gateLastFired.TryGetValue(key, out at) && now - at < RepeatSeconds;
            gateLastFired[key] = now;
            return repeat;
        }

        void GateFired(string key, GateKind kind, IntPtr source)
        {
            try
            {
                if (IsClient && Connected) { ClientGateFired(key, kind, source); return; }
                UnityEvent real;
                if (!gatedEvents.TryGetValue(key, out real)) return;
                if (Connected && (kind == GateKind.Host || kind == GateKind.First) && GateRepeat(key)) { try { real.Invoke(); } catch (Exception ex) { Log("MT-LOG the game's own event threw: " + key + ": " + ex.Message); } return; }
                if (Connected)
                {
                    if (kind == GateKind.Press && !MayAnswerPrompt) return;
                    if (kind == GateKind.Moment && !momentMine) return;
                    if (!GateRequestAllowed(key, 0)) return;
                    if (kind == GateKind.Press) { NoteLocalClick(key); momentMine = true; }
                    HostLog(Op.Gate, key, 0);
                }
                gateFired++;
                try { real.Invoke(); }
                catch (Exception ex) { Log("MT-LOG the game's own event threw: " + key + ": " + ex.Message); }
            }
            catch (Exception ex) { Log("MT-MODERROR gate " + key + ": " + ex.Message); }
        }

        void ClientGateFired(string key, GateKind kind, IntPtr source)
        {
            bool ask = kind == GateKind.Body ? ownBody.Contains(source)
                : kind == GateKind.Press ? MayAnswerPrompt
                : kind == GateKind.Moment ? momentMine
                : kind == GateKind.First;
            if (!ask || GateRequestsOff || GateRepeat(key)) { gateSwallowed++; return; }
            if (kind == GateKind.Press) ownRequests.Add(key);
            Log("MT-LOG request Gate " + key);
            SendToHost(Wire.Packet(Op.Gate, w => { Wire.Text(w, key); w.Write(0); }));
        }

        // The host's own firing and a client's request both pass here, so two players can't both win a First or a Press.
        bool GateRequestAllowed(string key, int from)
        {
            GateKind kind;
            if (!gateKinds.TryGetValue(key, out kind)) return true;   // not built yet: ApplyGate retries
            if (kind == GateKind.First)
            {
                int owner;
                if (gateOwners.TryGetValue(key, out owner) && owner != from) return false;
                gateOwners[key] = from;
                return true;
            }
            if (kind != GateKind.Press) return true;
            float now = Time.realtimeSinceStartup, at;
            if (gatePressedAt.TryGetValue(key, out at) && now - at < PressRepeat) return false;
            gatePressedAt[key] = now;
            return true;
        }

        void ApplyGate(string key)
        {
            UnityEvent real;
            if (!gatedEvents.TryGetValue(key, out real)) { storyMissing = true; if (missingLogged.Add(key)) Log("Gate not built here yet: " + key); return; }
            GateKind kind;
            bool press = gateKinds.TryGetValue(key, out kind) && kind == GateKind.Press;
            // A prompt answered by another player: what it starts is that player's animation (Interactions.cs).
            ReplayingInteraction = press && !ownRequest; ReplayingInteractionKey = ReplayingInteraction ? key : null;
            if (press) { if (ownRequest) NoteLocalClick(key); else NoteReplayedClick(key); momentMine = ownRequest; }
            bool body = gateKinds.TryGetValue(key, out kind) && kind == GateKind.Body;
            IntPtr source;
            bool mine = body && gateSources.TryGetValue(key, out source) && ownBody.Contains(source);
            List<MagnetBefore> magnets = body && !mine ? MagnetsBefore() : null;
            // A throwing listener of the game's mustn't stop the log, or the next tick would run this event again.
            try
            {
                real.Invoke();
                if (body && key.StartsWith("bodyend@", StringComparison.Ordinal)) interactionsFinished++;
            }
            catch (Exception ex) { Log("MT-LOG the game's own event threw: " + key + ": " + ex.Message); }
            finally { ReplayingInteraction = false; ReplayingInteractionKey = null; }
            if (press && !ownRequest) HidePromptAnswered(key);
            if (magnets != null) KeepMagnetsOffMe(magnets);
            if (mine && key.StartsWith("bodyend@", StringComparison.Ordinal)) ownBody.Remove(gateSources[key]);
        }

        // The game takes a prompt down inside KeyDown, on the PC that pressed it. Left up elsewhere, it could be answered again.
        static void HidePromptAnswered(string key)
        {
            Interface_KeyHint_Key prompt = SceneKeys.Find<Interface_KeyHint_Key>(key.Substring(key.IndexOf(':') + 1));
            if (prompt == null) return;
            if (prompt.destroyAfter) prompt.SmoothDestroy();
            if (prompt.downHide) prompt.hide = true;
            if (prompt.downBlind) prompt.Blind();
        }

        // "Go to the player's hand" (Transform_Magnet) means the player who performs the animation, but the game's magnet
        // aims at whoever runs it. Left alone, a watcher pulled the item onto their own body, believed they held it and sent
        // Carry too, against the real holder's (the phone: 0.87 m from the host's hand). Undone afterwards rather than hooked:
        // Activation shares its compiled body with an unrelated setter, and the item's pose comes from the holder (Op.Carry).
        sealed class MagnetBefore { public Transform_Magnet Magnet; public Transform Item, Slot, Parent; public bool Active, TimeParent; public int Sharply; public Vector3 Position; public Quaternion Rotation; }

        List<MagnetBefore> MagnetsBefore()
        {
            List<MagnetBefore> all = new List<MagnetBefore>();
            foreach (Transform_Magnet m in Magnets())
            {
                if (m == null) continue;
                Transform item = Moved(m);
                all.Add(new MagnetBefore { Magnet = m, Item = item, Slot = m.transformMagnet, Parent = item.parent, Active = m.active, TimeParent = m.timeParent, Sharply = m.sharply, Position = item.position, Rotation = item.rotation });
            }
            return all;
        }

        void KeepMagnetsOffMe(List<MagnetBefore> before)
        {
            PlayerMove player = LocalPlayer;
            if (player == null) return;
            Transform me = player.transform;
            foreach (MagnetBefore b in before)
            {
                Transform_Magnet m = b.Magnet;
                if (m == null || b.Item == null || m.transformMagnet == null || !m.transformMagnet.IsChildOf(me)) continue;
                if (m.active == b.Active && b.Item.parent == b.Parent) continue;
                m.active = b.Active; m.timeParent = b.TimeParent; m.sharply = b.Sharply;
                if (b.Slot != null) m.transformMagnet = b.Slot;
                b.Item.SetParent(b.Parent, true);
                b.Item.SetPositionAndRotation(b.Position, b.Rotation);
                if (LabMode) Log("MT-HOLD magnet " + SceneKeys.Key(m) + " stays off this player: the Main Character's");
            }
        }

        // --- Client: a press becomes a request ----------------------------------------------------
        // Nothing happens here until the host has run it and it comes back through the log.
        public static bool RequestInstead(Op op, Component source)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.IsClient || !rt.InLogChapter || Replaying || source == null) return false;
            string key = SceneKeys.Key(source);
            if (op == Op.Interact) rt.ownRequests.Add(key);
            Log("MT-LOG request " + op + " " + key);
            rt.SendToHost(Wire.Packet(op, w => { Wire.Text(w, key); w.Write(0); }));
            return true;
        }
    }
}
