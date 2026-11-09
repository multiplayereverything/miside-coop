using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Story events (interactions, trigger volumes, dialogue choices) are relayed so that anything one player does
    // happens for everyone, exactly once. Mita is host-authoritative; doors follow whoever last moved them.
    public sealed partial class TogetherRuntime
    {
        // Set while the mod itself calls a game method, so the patched method does not relay it again.
        public static bool Replaying;
        readonly HashSet<string> firedEnter = new HashSet<string>(), firedExit = new HashSet<string>();
        readonly HashSet<string> missingLogged = new HashSet<string>();

        // A story event whose object is missing or switched off here (this machine is a moment behind, or the object
        // is spawned later) is retried for a few seconds instead of being lost.
        sealed class StoryRetry { public Op Op; public string Key; public int Arg; public float Until, Next; }
        readonly List<StoryRetry> storyRetries = new List<StoryRetry>();
        const float StoryRetrySeconds = 5;
        bool storyMissing, retryingStory;

        void ResetWorldSync()
        {
            ClearHeld();
            firedEnter.Clear(); firedExit.Clear(); missingLogged.Clear(); storyRetries.Clear();
            mitaTargets.Clear(); mitaAnim.Clear();
            doorLast.Clear(); doorTargets.Clear();
            coreOperatorHere = false; coreStageAt.Clear(); mobileHolder = 0;
            Replaying = false;
        }

        bool itemsRegistered;

        void OnLocalSceneChanged()
        {
            ResetWorldState();
            ChapterLogDone();
            ResetStoryCheck();
            ClearDialogue();
            ClearNovella();
            ClearTriggers();
            ClearInteractions();
            ClearMinigames();
            itemsRegistered = false;
            ClearHeld();
            seededCalls.Clear();
            SceneKeys.Invalidate();
            HeldItems.Clear();
            firedEnter.Clear(); firedExit.Clear(); missingLogged.Clear(); storyRetries.Clear(); cutscenesStartedHere.Clear(); animationStartedAt.Clear(); animationCatchUps.Clear();
            mitaTargets.Clear(); mitaAnim.Clear(); doorLast.Clear(); doorTargets.Clear(); npcResolved.Clear();
            screenKey = null; screenTarget.Clear(); screenAnimTargets.Clear(); screenSentKey = null;
            cutsceneList = null; npcList = null; propList = null; propAppliedAt.Clear(); mitaList = null; doorList = null;
        }

        int keyedScenes;
        static int LoadedScenes() { int n = 0; for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isLoaded) n++; return n; }

        void UpdateWorldSync()
        {
            float now = Time.realtimeSinceStartup;
            // Once the chapter's objects exist (the player is in), record the original keys of its items.
            if (!itemsRegistered && LocalPlayer != null) { itemsRegistered = true; keyedScenes = LoadedScenes(); SceneKeys.RegisterItems(); }
            // A chapter scene the story loads beside this one (Scene 5 loads Scene 6) gets its keys as it arrives, the same
            // on every PC; objects already recorded keep theirs.
            else if (itemsRegistered && LoadedScenes() != keyedScenes) { keyedScenes = LoadedScenes(); SceneKeys.RegisterItems(); }
            // Each feature is its own step: timed separately in the lab (steps=), and one failing does not stop the rest.
            if (IsClient) Step("w.replay", ReplayTick);
            Step("w.gates", BuildGates);
            if (IsClient) Step("w.log", () => StoryLogTick(now));
            if (Hosting) Step("w.check", () => HostCheckTick(now)); else Step("w.check", () => ClientCheckTick(now));
            Step("w.debug", () => DebugTick(now));
            if (InLogChapter) Step("w.console", ConsoleReplyTick);
            if (minigames.Count > 0) { Step("w.screen", () => ScreenSendTick(now)); Step("w.minigame-over", MinigameOverTick); }
            if (watchedTakes.Count > 0) Step("w.takes", () => WatchedTakeTick(now));
            if (hostRequests.Count > 0) Step("w.requests", () => HostRequestTick(now));
            if (storyRetries.Count > 0) Step("w.retry", () => StoryRetryTick(now));
            if (pendingGo != null) Step("w.go", () => GoTick(now));
            if (SelfId == NpcAuthority && now >= nextMita) { nextMita = now + 1f / 15f; Step("w.mita", SendMitas); }
            if (Hosting) Step("w.cutscene", () => HostCutsceneTick(now));
            DoorTargetsTick(now);
            if (now >= nextDoor) { nextDoor = now + 0.1f; Step("w.door", () => DoorTick(now)); }
            Step("w.prop", () => PropTick(now));
            Step("w.interact", () => InteractionTick(now));            Step("w.dialogue", () => DialogueTick(now));
            Step("w.novella", () => NovellaTick(now));
            Step("w.room", () => WorldStateTick(now));
            if (IsClient) Step("w.anim", () => AnimatorCatchUpTick(now));
            if (animationCatchUps.Count > 0) Step("w.playeranim", () => AnimationCatchUpTick(now));
        }

        // The host switched to the preloaded chapter: do the same here once this machine's preload can switch.
        Scene_Load pendingGo; int pendingGoTries; float pendingGoAt;

        void GoTick(float now)
        {
            if (now < pendingGoAt) return;
            Scene_Load load = pendingGo;
            pendingGo = null;
            if (load == null) return;
            Replaying = true;
            try { load.GoScene(); Log("Switched to the preloaded chapter with the host"); }
            catch (Exception ex)
            {
                if (++pendingGoTries < 10) { pendingGo = load; pendingGoAt = now + 1; }
                else Log("Could not switch to the preloaded chapter: " + ex.Message);
            }
            finally { Replaying = false; }
        }

        void LateUpdateWorldSync()
        {
            if (SelfId != NpcAuthority) Step("w.mita-apply", ApplyMitas);
            Step("w.carried", LateUpdateCarried);
            Step("w.screen-apply", ScreenApplyLate);
        }

        // --- Story events ------------------------------------------------------------------------
        // Cutscenes are the host's: in a running chapter a client does not start one by itself (it asks the host). The
        // chapter's opening is excepted (the client is not live yet; the start barrier lines those up).
        public static int cutsceneRequests;

        public static bool HostRunsCutscene()
        {
            TogetherRuntime rt = Instance;
            return rt != null && rt.IsClient && rt.Connected && rt.ClientStoryLive && !Replaying;
        }

        public static void Story(Op op, Component source, int arg)
        {
            if (source == null) return;
            Story(op, SceneKeys.Key(source), arg);
        }

        public static void Story(Op op, string key, int arg)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || Replaying || key == null) return;
            // The minigame's own end event runs on every machine (Minigames.cs), so what it fires is not relayed.
            if (MinigameEnding) { Log("MT-STORY held (minigame end event) " + op + " " + key); return; }
            Log("MT-STORY local " + op + " " + key + (arg != 0 ? " " + arg : ""));
            rt.lastStoryPlayer = rt.SelfId;
            if (op == Op.Interact || op == Op.MouseClick) NoteLocalClick(key);
            if (op == Op.CutscenePlay && rt.Hosting) rt.AllPlayersHere();
            if (rt.Hosting && rt.InLogChapter) { rt.HostLog(op, key, arg); return; }
            byte[] packet = Wire.Packet(op, w => { Wire.Text(w, key); w.Write(arg); });
            if (rt.Hosting) { rt.RecordHistory(packet); rt.Broadcast(packet); }
            else rt.SendToHost(packet);
        }

        void ReadStoryEvent(Op op, BinaryReader reader, int from)
        {
            long start = reader.BaseStream.Position;
            if (Hosting && InLogChapter) { lastStoryPlayer = from; HostRequest(op, reader, from); return; }
            if (Hosting)
            {
                byte[] packet = new byte[reader.BaseStream.Length - start + 1];
                packet[0] = (byte)op;
                reader.BaseStream.Read(packet, 1, packet.Length - 1);
                reader.BaseStream.Position = start;
                RecordHistory(packet);
                // A cutscene a client asked for starts for everyone, the asker included, from the host's command.
                Broadcast(packet, op == Op.CutscenePlay ? -1 : from);
                ApplyStoryEvent(op, reader);
                return;
            }
            if (!ClientStoryLive) return;
            ApplyStoryEvent(op, reader);
        }

        void ApplyStoryEvent(Op op, BinaryReader reader)
        {
            string key = Wire.Text(reader, 1024);
            int arg = reader.ReadInt32();
            ApplyStory(op, key, arg);
        }

        void StoryRetryTick(float now)
        {
            for (int i = storyRetries.Count - 1; i >= 0; i--)
            {
                StoryRetry r = storyRetries[i];
                if (now < r.Next) continue;
                r.Next = now + 0.5f;
                retryingStory = true;
                try { ApplyStory(r.Op, r.Key, r.Arg); } finally { retryingStory = false; }
                if (!storyMissing) { storyRetries.RemoveAt(i); Log("MT-STORY retried " + r.Op + " " + r.Key + " (applied " + (now - (r.Until - StoryRetrySeconds)).ToString("0.0") + " s late)"); }
                else if (now >= r.Until) { storyRetries.RemoveAt(i); Log("MT-STORY dropped " + r.Op + " " + r.Key + ": missing or switched off here for " + StoryRetrySeconds + " s"); }
            }
        }

        void ApplyStory(Op op, string key, int arg)
        {
            storyMissing = false;
            Replaying = true;
            try
            {
                switch (op)
                {
                    case Op.Interact:
                        ObjectInteractive interactive = SceneKeys.Find<ObjectInteractive>(key);
                        if (Missing(interactive, key)) return;
                        // In a chapter on the story log the press has already counted, whatever this copy thinks of the object:
                        // Scene 3's hall objects stayed off on a client, and the click was lost after waiting for them.
                        if (InLogChapter) { interactive.active = true; momentMine = ownRequest; }
                        // Not armed here yet (ObjectInteractive.active): Click would do nothing, so wait for the story to arm it.
                        if (Inactive(interactive, key, interactive.active)) return;
                        // Someone else's interaction: its animation is theirs (Interactions.cs).
                        ReplayingInteraction = !ownRequest; ReplayingInteractionKey = ownRequest ? null : key;
                        if (ownRequest) NoteLocalClick(key); else NoteReplayedClick(key);
                        NoteCoreOperator(key, false);
                        try { interactive.Click(); } finally { ReplayingInteraction = false; ReplayingInteractionKey = null; }
                        break;
                    case Op.MouseClick:
                        Trigger_MouseClick click = SceneKeys.Find<Trigger_MouseClick>(key);
                        if (Missing(click, key) || Inactive(click, key)) return;
                        ReplayingInteraction = true;
                        try { click.Click(); } finally { ReplayingInteraction = false; }
                        break;
                    case Op.TriggerEnter:
                        Trigger_Event enter = SceneKeys.Find<Trigger_Event>(key);
                        if (Missing(enter, key)) return;
                        if (!enter.loop && !firedEnter.Add(key)) return;
                        enter.TargetEnter();
                        break;
                    case Op.TriggerExit:
                        Trigger_Event exit = SceneKeys.Find<Trigger_Event>(key);
                        // A trigger already destroyed here can't run an exit, and waiting won't bring it back (Scene 6's
                        // hide-and-seek trigger, gone with its enter before the host's player walked out).
                        if (exit == null && SceneKeys.WasRecorded(key)) { if (missingLogged.Add(key)) Log("MT-STORY trigger gone here, its exit skipped: " + key); return; }
                        if (Missing(exit, key)) return;
                        if (!exit.loop && !firedExit.Add(key)) return;
                        exit.TargetExit();
                        // The player who left has destroyed theirs; a copy nobody entered here would otherwise live on and fire alone (Scene 17's Trigger Save).
                        if (exit != null && exit.destroyOnExit) Object.Destroy(exit.gameObject);
                        break;
                    case Op.LookTrigger: ApplyLookTrigger(key, arg); break;
                    case Op.DistanceTrigger: ApplyDistanceTrigger(key, arg); break;
                    case Op.Call: ApplyCall(key, arg); break;
                    case Op.Gate: ApplyGate(key); break;
                    case Op.SceneLoad:
                        // The game's own transition component, with the same settings it has on the sender's machine.
                        Scene_Load load = SceneKeys.Find<Scene_Load>(key);
                        if (Missing(load, key)) return;
                        if (arg == 1) { if (!load.loading) load.StartLoad(); pendingGo = load; pendingGoTries = 0; pendingGoAt = Time.realtimeSinceStartup + 0.5f; return; }
                        if (load.loading) return;
                        load.StartLoad();
                        break;
                    case Op.KeyAdd:
                    case Op.KeyRemove:
                        ApplyKeyItem(op, key);
                        break;
                    case Op.Death:
                        ApplyDeath(key, arg);
                        break;
                    case Op.SkipDialogue:
                        Dialogue_3DText line = SceneKeys.Find<Dialogue_3DText>(key);
                        if (Missing(line, key) || !line.gameObject.activeInHierarchy) return;
                        line.SkipDialogue();
                        break;
                    case Op.CutscenePlay:
                        Playable_Animation cutscene = SceneKeys.Find<Playable_Animation>(key);
                        if (Hosting) lastClientCut = SceneKeys.Key(cutscene);
                        // Switched off here yet (the event that switches it on comes a moment later): retried. Already
                        // playing only counts when its Play ran here (a timeline can play on awake without it).
                        if (Missing(cutscene, key) || Inactive(cutscene, key) || !NeedsStart(cutscene)) return;
                        cutscene.Play();
                        AllPlayersHere();
                        break;
                    case Op.DialogueChoice:
                        DialogueChanger dialogue = SceneKeys.Find<DialogueChanger>(key);
                        if (Missing(dialogue, key)) return;
                        // The choice is only clickable while its menu is open here (play, buttons built); clicked before,
                        // ClickButton threw a NullReferenceException (playtest, Scene 3). Not open yet: open it the
                        // game's way and click on the next retry.
                        // ClickButton (0x3dfd10) first reaches the player through a static, then buttons[arg]: both must
                        // exist here, or it throws.
                        if (!dialogue.play || dialogue.buttons == null || dialogue.indexButtons == null || LocalPlayer == null
                            || arg < 0 || arg >= dialogue.buttons.Length || dialogue.buttons[arg] == null)
                        {
                            if (!dialogue.play && dialogue.gameObject.activeInHierarchy) dialogue.Play();
                            Inactive(dialogue, key, false);
                            return;
                        }
                        dialogue.ClickButton(arg);
                        dialogueChoicesApplied++;
                        break;
                }
                Log("MT-STORY applied " + op + " " + key);
                LastAppliedStory = op + " " + key;
            }
            finally
            {
                Replaying = false;
                if (storyMissing && !retryingStory && !storyRetries.Exists(r => r.Op == op && r.Key == key && r.Arg == arg))
                {
                    float now = Time.realtimeSinceStartup;
                    storyRetries.Add(new StoryRetry { Op = op, Key = key, Arg = arg, Until = now + StoryRetrySeconds, Next = now + 0.5f });
                }
            }
        }

        bool Missing(Object found, string key)
        {
            if (found != null) return false;
            storyMissing = true;
            if (missingLogged.Add(key)) Log("Story object not found here (retrying): " + key);
            return true;
        }

        bool Inactive(Component found, string key, bool armed = true)
        {
            if (found.gameObject.activeInHierarchy && armed) return false;
            storyMissing = true;
            if (missingLogged.Add(key)) Log("Story object switched off here (retrying): " + key);
            return true;
        }

        // Trigger volumes fire once for the whole group: a trigger another player already set off stays quiet here.
        public static bool BeforeTriggerEnter(Trigger_Event trigger)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || Replaying || trigger.loop) return true;
            return !rt.firedEnter.Contains(SceneKeys.Key(trigger));
        }

        public static void AfterTriggerEnter(Trigger_Event trigger)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || Replaying) return;
            if (!trigger.loop) rt.firedEnter.Add(SceneKeys.Key(trigger));
            Story(Op.TriggerEnter, trigger, 0);
        }

        public static bool BeforeTriggerExit(Trigger_Event trigger)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || Replaying || trigger.loop) return true;
            return !rt.firedExit.Contains(SceneKeys.Key(trigger));
        }

        public static void AfterTriggerExit(Trigger_Event trigger)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || Replaying) return;
            if (!trigger.loop) rt.firedExit.Add(SceneKeys.Key(trigger));
            Story(Op.TriggerExit, trigger, 0);
        }

        // The game re-arms a trigger with Restart(); it may then fire again for the group.
        public static void AfterTriggerRestart(Trigger_Event trigger)
        {
            TogetherRuntime rt = Instance;
            if (rt == null) return;
            string key = SceneKeys.Key(trigger);
            rt.firedEnter.Remove(key); rt.firedExit.Remove(key);
        }

        // A story cutscene (a timeline: Scene 1's days) changes the place for everyone, so everyone is told. The ones seen so
        // far put every player in position themselves; the gather afterwards brings anyone left elsewhere.
        float allHereToldAt = -100;
        void AllPlayersHere()
        {
            if (!Connected || Time.realtimeSinceStartup - allHereToldAt < 10) return;
            allHereToldAt = Time.realtimeSinceStartup;
            Tell("All players must be here for this cutscene.");
        }

        // --- NPCs (host-authoritative puppets on clients) --------------------------------------------
        // Story characters move and animate on the host and are mirrored on clients. Chasers (Mita Killer, mannequins,
        // lift and bacteria enemies) are deliberately left to each machine's own AI: they hunt that machine's player,
        // and their kills are relayed so the whole group restarts together (see Deaths.cs).
        public sealed class NpcRef { public Component Owner; public Transform Root; public Animator Anim; public NavMeshAgent Agent; public byte Kind; public string Key; }
        sealed class NpcState { public byte Kind; public Vector3 Position; public Quaternion Rotation; public readonly AnimFrame Anim = new AnimFrame(); public float At; public bool Snap; }
        readonly Dictionary<string, NpcState> mitaTargets = new Dictionary<string, NpcState>();
        readonly Dictionary<string, AnimTarget> mitaAnim = new Dictionary<string, AnimTarget>();
        List<NpcRef> npcList;
        List<MitaPerson> mitaList;
        float npcListAt, nextMita;
        readonly AnimFrame mitaFrame = new AnimFrame();
        public const byte KindMita = 1, KindChibi = 2, KindFollow = 3, KindBoss = 4;

        // MitaPerson sits on an empty parent; what walks and plays her animations is the child with the Animator and the NavMeshAgent.
        static Transform MitaBody(MitaPerson m) { return m.animMita != null ? m.animMita.transform : m.transform; }

        List<MitaPerson> Mitas()
        {
            Npcs();
            return mitaList;
        }

        public List<NpcRef> Npcs()
        {
            if (npcList != null && Time.realtimeSinceStartup - npcListAt < 2f) return npcList;
            npcListAt = Time.realtimeSinceStartup;
            npcList = new List<NpcRef>(); mitaList = new List<MitaPerson>();
            HashSet<IntPtr> chasers = new HashSet<IntPtr>();
            foreach (Location6_MitaKiller k in Object.FindObjectsOfType<Location6_MitaKiller>()) if (k != null && k.mita != null) chasers.Add(k.mita.gameObject.Pointer);
            foreach (MitaPerson m in Object.FindObjectsOfType<MitaPerson>())
            {
                if (m == null || chasers.Contains(m.gameObject.Pointer)) continue;
                mitaList.Add(m);
                npcList.Add(new NpcRef { Owner = m, Root = MitaBody(m), Anim = m.animMita, Agent = m.nma, Kind = KindMita });
            }
            foreach (Mob_ChibiMita c in Object.FindObjectsOfType<Mob_ChibiMita>())
                if (c != null) npcList.Add(new NpcRef { Owner = c, Root = c.transform, Anim = c.anim, Agent = c.nma, Kind = KindChibi });
            foreach (Location5_MitaFollowPlayer f in Object.FindObjectsOfType<Location5_MitaFollowPlayer>())
                if (f != null && f.anim != null && chasers.Add(f.anim.gameObject.Pointer)) npcList.Add(new NpcRef { Owner = f, Root = f.transform, Anim = f.anim, Kind = KindFollow });
            foreach (FightBoss_Person b in Object.FindObjectsOfType<FightBoss_Person>())
                if (b != null) npcList.Add(new NpcRef { Owner = b, Root = b.transform, Anim = b.anim, Kind = KindBoss });
            foreach (NpcRef n in npcList) n.Key = SceneKeys.Key(n.Owner);
            return npcList;
        }

        static NpcRef Resolve(byte kind, string key)
        {
            switch (kind)
            {
                case KindMita: { MitaPerson m = SceneKeys.Find<MitaPerson>(key); return m == null ? null : new NpcRef { Owner = m, Root = MitaBody(m), Anim = m.animMita, Agent = m.nma, Kind = kind }; }
                case KindChibi: { Mob_ChibiMita c = SceneKeys.Find<Mob_ChibiMita>(key); return c == null ? null : new NpcRef { Owner = c, Root = c.transform, Anim = c.anim, Agent = c.nma, Kind = kind }; }
                case KindFollow: { Location5_MitaFollowPlayer f = SceneKeys.Find<Location5_MitaFollowPlayer>(key); return f == null ? null : new NpcRef { Owner = f, Root = f.transform, Anim = f.anim, Kind = kind }; }
                case KindBoss: { FightBoss_Person b = SceneKeys.Find<FightBoss_Person>(key); return b == null ? null : new NpcRef { Owner = b, Root = b.transform, Anim = b.anim, Kind = kind }; }
            }
            return null;
        }

        // Who moves the NPCs: the host, except while a player plays a live minigame (Minigames.cs): the game drives Mita
        // on their PC alone (Scene 4's card table), so the others copy her from there.
        int NpcAuthority
        {
            get
            {
                foreach (MinigameRun r in minigames.Values) if (IsLive(r.Kind) && (r.Owner == SelfId || peers.ContainsKey(r.Owner))) return r.Owner;
                return 0;
            }
        }

        void SendMitas()
        {
            List<NpcRef> live = new List<NpcRef>();
            foreach (NpcRef n in Npcs()) if (n.Owner != null && n.Root != null && n.Root.gameObject.activeInHierarchy && live.Count < 24) live.Add(n);
            if (live.Count == 0) return;
            (Hosting ? (Action<byte[]>)(p => Broadcast(p)) : SendToHost)(Wire.Packet(Op.Mita, w =>
            {
                w.Write(LocalSceneHash);
                w.Write((byte)live.Count);
                foreach (NpcRef n in live)
                {
                    w.Write(n.Kind);
                    Wire.Text(w, n.Key);
                    WritePose(w, n.Root.position, n.Root.rotation);
                    mitaFrame.Capture(n.Anim);
                    mitaFrame.Write(w);
                }
            }));
        }

        // On the host: a player's PC moving the NPCs (NpcAuthority) sends them here, for everyone.
        void HostMita(BinaryReader reader, int from)
        {
            if (from != NpcAuthority) return;
            long start = reader.BaseStream.Position;
            byte[] rest = new byte[reader.BaseStream.Length - start];
            reader.BaseStream.Read(rest, 0, rest.Length);
            reader.BaseStream.Position = start;
            Broadcast(Wire.Packet(Op.Mita, w => w.Write(rest)), from);
            ClientMita(reader);
        }

        void ClientMita(BinaryReader reader)
        {
            int scene = reader.ReadInt32();
            int count = reader.ReadByte();
            if (count > 24) throw new IOException("Too many NPCs.");
            bool sameScene = scene == LocalSceneHash && (Hosting || ClientStoryLive);
            for (int i = 0; i < count; i++)
            {
                byte kind = reader.ReadByte();
                string key = Wire.Text(reader, 1024);
                Vector3 pos; Quaternion rot;
                ReadTransform(reader, out pos, out rot);
                NpcState state;
                if (!mitaTargets.TryGetValue(key, out state)) { state = new NpcState { Snap = true, Kind = kind }; if (sameScene) mitaTargets[key] = state; }
                state.Anim.Read(reader);
                if ((pos - state.Position).sqrMagnitude > 25f) state.Snap = true;
                state.Position = pos; state.Rotation = rot; state.At = Time.realtimeSinceStartup;
            }
        }

        readonly Dictionary<string, NpcRef> npcResolved = new Dictionary<string, NpcRef>();

        void ApplyMitas()
        {
            if (mitaTargets.Count == 0 || replayQueue.Count > 0) return;
            float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            foreach (KeyValuePair<string, NpcState> pair in mitaTargets)
            {
                NpcState state = pair.Value;
                // Stale (the host's NPC went inactive): leave the local one to its own scripts.
                if (Time.realtimeSinceStartup - state.At > 1f) continue;
                NpcRef npc;
                if (!npcResolved.TryGetValue(pair.Key, out npc) || npc == null || npc.Owner == null || npc.Root == null)
                {
                    npc = Resolve(state.Kind, pair.Key);
                    if (npc == null) continue;
                    npcResolved[pair.Key] = npc;
                }
                if (!npc.Root.gameObject.activeInHierarchy) continue;
                Transform t = npc.Root;
                if (state.Snap) { t.SetPositionAndRotation(state.Position, state.Rotation); state.Snap = false; }
                else t.SetPositionAndRotation(Vector3.Lerp(t.position, state.Position, k), Quaternion.Slerp(t.rotation, state.Rotation, k));
                NavMeshAgent agent = npc.Agent;
                // Placed where the host's NPC is, the agent can end up off the walkable mesh (a cutscene spot); the game's
                // own AI here then calls SetDestination on it every frame and Unity logs an error each time (playtest,
                // Scene 3). Put the agent back on the nearest walkable point; the body stays where the host's is.
                if (agent != null && agent.isActiveAndEnabled && !agent.isOnNavMesh && NavMesh.SamplePosition(t.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    Vector3 keep = t.position; Quaternion keepRot = t.rotation;
                    agent.Warp(hit.position);
                    t.SetPositionAndRotation(keep, keepRot);
                }
                if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
                {
                    agent.updatePosition = false; agent.updateRotation = false;
                    agent.nextPosition = t.position;
                }
                AnimTarget target;
                if (!mitaAnim.TryGetValue(pair.Key, out target) || target.Animator != npc.Anim)
                {
                    target = new AnimTarget { Animator = npc.Anim };
                    if (npc.Anim != null && npc.Anim.runtimeAnimatorController != null)
                        target.Override = npc.Anim.runtimeAnimatorController.TryCast<AnimatorOverrideController>();
                    mitaAnim[pair.Key] = target;
                }
                AnimSync.Apply(target, state.Anim, false);
            }
        }
        // --- Doors ---------------------------------------------------------------------------------
        List<ObjectDoor> doorList;
        float doorListAt, nextDoor;
        readonly Dictionary<string, Quaternion> doorLast = new Dictionary<string, Quaternion>();
        // A door another player is moving: its physics is paused here and it glides to their angle. Written onto a live
        // hinge, the angle was thrown away by the next physics step: the door stayed shut, or snapped back and forth.
        sealed class DoorTarget { public ObjectDoor Door; public Quaternion Rotation; public float At; }
        readonly Dictionary<string, DoorTarget> doorTargets = new Dictionary<string, DoorTarget>();
        const float DoorReleaseSeconds = 0.5f;

        void DoorTargetsTick(float now)
        {
            if (doorTargets.Count == 0) return;
            float k = 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime);
            List<string> done = null;
            foreach (KeyValuePair<string, DoorTarget> pair in doorTargets)
            {
                DoorTarget t = pair.Value;
                if (t.Door == null) { (done ?? (done = new List<string>())).Add(pair.Key); continue; }
                if (now - t.At > DoorReleaseSeconds)
                {
                    t.Door.transform.localRotation = t.Rotation;
                    if (t.Door.rb != null) t.Door.rb.isKinematic = t.Door.lockDoor;
                    doorLast[pair.Key] = t.Rotation;
                    (done ?? (done = new List<string>())).Add(pair.Key);
                    continue;
                }
                t.Door.transform.localRotation = Quaternion.Slerp(t.Door.transform.localRotation, t.Rotation, k);
            }
            if (done != null) foreach (string key in done) doorTargets.Remove(key);
        }


        void DoorTick(float now)
        {
            if (doorList == null || now - doorListAt > 3f)
            {
                doorListAt = now;
                doorList = new List<ObjectDoor>();
                foreach (ObjectDoor d in Object.FindObjectsOfType<ObjectDoor>()) if (d != null) doorList.Add(d);
                // A shut door hides the rooms (and their lights) on the far side of this PC's own player
                // (ObjectDoor.DoorClosed). With players on both sides, each PC hid the other player's room. In a
                // session the house stays whole: both sides on, and no hiding.
                foreach (ObjectDoor d in doorList)
                {
                    if (!doorGameOff.ContainsKey(d.Pointer)) doorGameOff[d.Pointer] = d.offOptimization;
                    if (!d.offOptimization) { d.offOptimization = true; d.OptimizationObjectsActivation(); }
                }
            }
            if (IsClient && !ClientStoryLive) return;
            int sent = 0;
            foreach (ObjectDoor door in doorList)
            {
                // Shut, the game switches the door component off (OptimizationObjectsActivation): its last angle still goes out.
                if (door == null || !door.gameObject.activeInHierarchy || sent >= 32) continue;
                string key = SceneKeys.Key(door);
                if (doorTargets.ContainsKey(key)) continue;   // another player has it: not ours to report
                Quaternion rot = door.transform.localRotation;
                Quaternion last;
                if (!doorLast.TryGetValue(key, out last)) { doorLast[key] = rot; continue; }
                if (Quaternion.Angle(rot, last) < 0.75f) continue;
                doorLast[key] = rot;
                sent++;
                Broadcast(Wire.Packet(Op.Door, w => { Wire.Text(w, key); w.Write(rot.x); w.Write(rot.y); w.Write(rot.z); w.Write(rot.w); w.Write(door.open); }));
            }
        }

        // What the game itself set for each door: offOptimization is forced on by DoorTick, so a shut door hides nothing.
        static readonly Dictionary<IntPtr, bool> doorGameOff = new Dictionary<IntPtr, bool>();
        static readonly Dictionary<IntPtr, float> doorToldAt = new Dictionary<IntPtr, float>();
        public static void NoteDoorOptimization(ObjectDoor door) { doorGameOff[door.Pointer] = door.offOptimization; }

        // The game brings back the rooms behind a door when it opens, unless the story turned that off for the door. Scene 2 switches
        // Main and Bedroom off for the corridor and relies on this to bring them back.
        public static void DoorOpenedHere(ObjectDoor door)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || door == null || Replaying) return;
            bool gameOff;
            if (doorGameOff.TryGetValue(door.Pointer, out gameOff) && gameOff) return;   // the story said this door brings nothing (the corridor's)
            door.OptimizationObjectsActivation();
            // A door leaned on flaps open and shut: once a second is plenty.
            float last;
            if (doorToldAt.TryGetValue(door.Pointer, out last) && Time.realtimeSinceStartup - last < 1f) return;
            doorToldAt[door.Pointer] = Time.realtimeSinceStartup;
            Call(28, door, 0);   // the game's own DoorOpened only: its story also swings doors (OpenAngle) with rooms that must stay off
        }

        void ApplyDoor(BinaryReader reader, int from)
        {
            long start = reader.BaseStream.Position;
            string key = Wire.Text(reader, 1024);
            Quaternion rot = new Quaternion(Wire.Number(reader), Wire.Number(reader), Wire.Number(reader), Wire.Number(reader));
            bool open = reader.ReadBoolean();
            if (Hosting)
            {
                reader.BaseStream.Position = start;
                byte[] body = new byte[reader.BaseStream.Length - start + 1];
                body[0] = (byte)Op.Door;
                reader.BaseStream.Read(body, 1, body.Length - 1);
                Broadcast(body, from);
            }
            ObjectDoor door = SceneKeys.Find<ObjectDoor>(key);
            if (door == null) return;
            door.open = open;
            if (door.rb != null) door.rb.isKinematic = true;
            DoorTarget target;
            if (!doorTargets.TryGetValue(key, out target)) doorTargets[key] = target = new DoorTarget { Door = door };
            target.Rotation = rot;
            target.At = Time.realtimeSinceStartup;
        }
    }

    // Stable identity for scene objects on every machine: scene name plus the sibling-indexed hierarchy path.
    public static class SceneKeys
    {
        static readonly Dictionary<Type, Dictionary<string, Object>> byType = new Dictionary<Type, Dictionary<string, Object>>();
        static readonly Dictionary<Type, float> scannedAt = new Dictionary<Type, float>();

        // Original keys of objects the game moves around at runtime (items go into the inventory or a hand): they keep
        // the identity they had when the chapter loaded, on every machine.
        static readonly Dictionary<IntPtr, string> stable = new Dictionary<IntPtr, string>();

        static readonly HashSet<string> recorded = new HashSet<string>();

        public static void Invalidate() { byType.Clear(); scannedAt.Clear(); stable.Clear(); recorded.Clear(); }

        public static void RegisterStable(GameObject go)
        {
            if (go == null || stable.ContainsKey(go.Pointer)) return;
            string key = Compute(go);
            stable[go.Pointer] = key;
            recorded.Add(key);
        }

        // A key recorded as its scene loaded: the object existed then, so if it can't be found now it was destroyed.
        public static bool WasRecorded(string key) { return recorded.Contains(key); }

        public static void RegisterItems()
        {
            // Every object's key as the chapter loaded, the same on every PC. Computed later, sibling numbers drift as the
            // story spawns and destroys objects, and a name alone can be ambiguous (Scene 2 has two "Quest 3", each with a
            // TriggerStopDoorTakTak): a client then never found the host's door trigger and its story stalled (2026-11-08).
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
                if (t != null && t.gameObject.scene.isLoaded) RegisterStable(t.gameObject);
            TogetherRuntime.Log("Chapter keys recorded: " + stable.Count + " objects in " + watch.ElapsedMilliseconds + " ms");
            byType.Clear(); scannedAt.Clear();
            foreach (ObjectInteractiveItemTake take in Object.FindObjectsOfType<ObjectInteractiveItemTake>(true))
            {
                if (take == null) continue;
                if (take.transform.parent != null) RegisterStable(take.transform.parent.gameObject);
                foreach (Transform t in take.GetComponentsInChildren<Transform>(true)) RegisterStable(t.gameObject);
            }
            // Mita gets moved around the hierarchy by the story; a key that changed left clients' Mita standing still.
            foreach (MitaPerson m in Object.FindObjectsOfType<MitaPerson>(true)) if (m != null) RegisterStable(m.gameObject);
            // Objects the game magnets onto the player (the toy plane...) keep their chapter-start identity.
            foreach (Transform_Magnet magnet in Object.FindObjectsOfType<Transform_Magnet>(true)) if (magnet != null) RegisterStable(magnet.gameObject);
            foreach (ObjectItem item in Object.FindObjectsOfType<ObjectItem>(true))
                if (item != null) foreach (Transform t in item.GetComponentsInChildren<Transform>(true)) RegisterStable(t.gameObject);
        }

        // The game sometimes hands the inventory a scene-less copy of an item ("/ItemBow#0"): match it by name among the
        // chapter's recorded items instead.
        public static Transform FindItemByName(string key)
        {
            int slash = key.LastIndexOf('/'), hash = key.LastIndexOf('#');
            if (slash < 0 || hash <= slash) return null;
            string name = key.Substring(slash + 1, hash - slash - 1);
            foreach (KeyValuePair<IntPtr, string> pair in stable)
            {
                if (!pair.Value.Contains("/" + name + "#")) continue;
                Transform t = Find<Transform>(pair.Value);
                if (t != null && t.name == name) return t;
            }
            return null;
        }

        public static string Key(Component c) { return c == null ? "" : Key(c.gameObject); }

        public static string Key(GameObject go)
        {
            string known;
            // The name guards against a destroyed object's address reused by a new one.
            if (go != null && stable.TryGetValue(go.Pointer, out known) && known.EndsWith("/" + go.name + known.Substring(known.LastIndexOf('#')), StringComparison.Ordinal)) return known;
            return Compute(go);
        }

        static string Compute(GameObject go)
        {
            StringBuilder path = new StringBuilder();
            for (Transform t = go.transform; t != null; t = t.parent)
                path.Insert(0, "/" + t.name + "#" + t.GetSiblingIndex());
            return go.scene.name + path;
        }

        public static T Find<T>(string key) where T : Component
        {
            Dictionary<string, Object> map;
            Object found;
            if (byType.TryGetValue(typeof(T), out map) && map.TryGetValue(key, out found) && found != null) return found.Cast<T>();
            float last;
            // Objects spawned later (or re-parented) are picked up by a rescan, at most once a second per type.
            if (map != null && scannedAt.TryGetValue(typeof(T), out last) && Time.realtimeSinceStartup - last < 1f) return ByName<T>(map, key);
            scannedAt[typeof(T)] = Time.realtimeSinceStartup;
            map = new Dictionary<string, Object>();
            foreach (T item in Object.FindObjectsOfType<T>(true))
            {
                if (item == null || !item.gameObject.scene.isLoaded) continue;
                map[Key(item)] = item;
            }
            byType[typeof(T)] = map;
            return map.TryGetValue(key, out found) && found != null ? found.Cast<T>() : ByName<T>(map, key);
        }

        // Sibling order can differ between machines (an object spawned or destroyed under the same parent): the same
        // path without the sibling indices, when exactly one object has it, is the same object.
        static readonly System.Text.RegularExpressions.Regex SiblingIndex = new System.Text.RegularExpressions.Regex(@"#\d+");
        static readonly HashSet<string> namedLogged = new HashSet<string>();
        // Story objects found only by name. Objects spawned while the chapter runs ("(Clone)") have no chapter-start key.
        public static int NameMatched { get { int n = 0; foreach (string k in namedLogged) if (!k.Contains("(Clone)")) n++; return n; } }

        static T ByName<T>(Dictionary<string, Object> map, string key) where T : Component
        {
            string wanted = SiblingIndex.Replace(key, "");
            Object match = null;
            foreach (KeyValuePair<string, Object> pair in map)
            {
                if (pair.Value == null || SiblingIndex.Replace(pair.Key, "") != wanted) continue;
                if (match != null) return null;   // ambiguous
                match = pair.Value;
            }
            if (match != null && namedLogged.Add(key)) TogetherRuntime.Log("Scene key matched by name (sibling order differs here): " + key);
            return match != null ? match.Cast<T>() : null;
        }
    }
}
