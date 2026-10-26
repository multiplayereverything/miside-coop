using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Interactions (taking the toy plane, the phone...) follow CutsceneRules. By default ("mirror") the replayed click
    // plays the same interaction on every player's own character, so nothing below is needed. With who=actor only the
    // player who pressed it performs it, and everyone else:
    //  * does not replay the animation on their own character (PlayerMove.AnimationPlay is skipped for replayed clicks);
    //  * sees every object the game magnets onto the actor (Transform_Magnet parented under the player) ride in the
    //    actor's hand on their body, and land wherever the actor puts it;
    //  * runs the interaction's own finish event when the actor's animation ends, so its story effect happens here too.
    public sealed partial class TogetherRuntime
    {
        public static bool ReplayingInteraction;
        public int carryFramesApplied, interactionsFinished;

        // --- Watchers: no animation on the local character for someone else's interaction ---------------------
        public static string ReplayingInteractionKey;
        // The last replayed click: interactions often start their animation a moment later (timed events), and that
        // animation must follow the click's rule, not the default.
        static string lastReplayedClick; static float lastReplayedClickAt = -100;
        public static void NoteReplayedClick(string key) { lastReplayedClick = key; lastReplayedClickAt = Time.realtimeSinceStartup; }

        public static bool BeforePlayerAnimation(ObjectAnimationPlayer scr)
        {
            TogetherRuntime rt = Instance;
            // Interactions often start their animation a moment after the click (through the game's timed events), so
            // besides the replayed click itself: an interaction animation this player did not trigger, while another
            // player is acting, belongs to that player.
            bool othersActing = false;
            if (rt != null && rt.Connected && Time.realtimeSinceStartup - rt.lastClickAt > 3)
                foreach (Peer p in rt.peers.Values) if (p.Acting && Time.realtimeSinceStartup - p.PoseAt < 1f) othersActing = true;
            bool recentReplay = Time.realtimeSinceStartup - lastReplayedClickAt < 3 && (rt == null || Time.realtimeSinceStartup - rt.lastClickAt > 3);
            if (!ReplayingInteraction && !othersActing && !recentReplay) return true;
            // CutsceneRules decide per case: "everyone" (mirror) lets this player perform it too.
            string key = scr != null ? SceneKeys.Key(scr) : null;
            string click = ReplayingInteractionKey ?? (Time.realtimeSinceStartup - lastReplayedClickAt < 3 ? lastReplayedClick : null);
            CutsceneRules.Rule rule = CutsceneRules.ForInteraction(click, key);
            if (rule.Who == CutsceneRules.Who.Everyone) return true;
            if (Instance != null && key != null) Instance.pendingWatch[key] = scr;
            lastHeldBackAt = Time.realtimeSinceStartup;
            if (LabMode) Log("MT-HOLD " + key + " (click " + click + "): another player's, rule " + rule.Note);
            return false;
        }

        // When this machine last held back an animation as another player's (the lab tour tells presser-only cases apart).
        public static float lastHeldBackAt = -100;

        readonly Dictionary<string, ObjectAnimationPlayer> pendingWatch = new Dictionary<string, ObjectAnimationPlayer>();

        // --- Story-started player animations ------------------------------------------------------------------
        // The story starts some player animations by itself (a door's animation event puts the player into a cutscene
        // pose). Each machine usually starts them from the same relayed events, but not always (Scene 12's exit door: the
        // event that starts it on the host never fires on the client). The host reports every animation that starts on
        // its own player (Op.Call 7, not in the history); a client that has not started the same one itself within a
        // second plays it, unless its press is presser-only. Mirrored presses start it here anyway, so nothing doubles.
        sealed class AnimationCatchUp { public string Key; public int Variant; public float At; }
        readonly Dictionary<string, float> animationStartedAt = new Dictionary<string, float>();
        readonly List<AnimationCatchUp> animationCatchUps = new List<AnimationCatchUp>();
        public int playerAnimationsCaughtUp;

        // Every call counts as started here (the player can still be walking into place when it returns); only the host's
        // own, with its player already animating, are reported.
        void NoteAnimationStarted(ObjectAnimationPlayer scr, int variant, bool playerAnimating)
        {
            string key = SceneKeys.Key(scr);
            animationStartedAt[key] = Time.realtimeSinceStartup;
            if (!playerAnimating || !Hosting || Replaying || ReplayingInteraction) return;
            Broadcast(Wire.Packet(Op.Call, w => { Wire.Text(w, "7:" + key); w.Write(variant); }));
        }

        void QueueAnimationCatchUp(string key, int variant)
        {
            if (!IsClient) return;
            animationCatchUps.Add(new AnimationCatchUp { Key = key, Variant = variant, At = Time.realtimeSinceStartup });
        }

        void AnimationCatchUpTick(float now)
        {
            for (int i = animationCatchUps.Count - 1; i >= 0; i--)
            {
                AnimationCatchUp c = animationCatchUps[i];
                if (now - c.At < 1f) continue;
                animationCatchUps.RemoveAt(i);
                ObjectAnimationPlayer scr = SceneKeys.Find<ObjectAnimationPlayer>(c.Key);
                if (scr == null || !scr.gameObject.activeInHierarchy) { Log("Player animation not caught up (missing or switched off here): " + c.Key); continue; }
                // By this machine's own key: the host's can differ where siblings are ordered differently (Scene 17).
                if (animationStartedAt.TryGetValue(SceneKeys.Key(scr), out float at) && at > c.At - 2f) continue;   // started here by itself
                string click = Time.realtimeSinceStartup - lastReplayedClickAt < 5 ? lastReplayedClick : null;
                if (CutsceneRules.ForInteraction(click, c.Key).Who == CutsceneRules.Who.Actor) continue;   // the presser's own
                Replaying = true;
                try
                {
                    if (c.Variant == 1) scr.AnimationPlayFast(); else if (c.Variant == 2) scr.AnimationPlayOnPlayer(); else scr.AnimationPlay();
                    playerAnimationsCaughtUp++;
                    Log("MT-STORY caught up player animation " + c.Key + " (the host's story started it)");
                }
                catch (Exception ex) { Log("Player animation catch-up threw: " + ex.Message); }
                finally { Replaying = false; }
            }
        }

        // --- Actor: track the interaction animation and what it carries -----------------------------------------
        string actorAnimation;
        float actorAnimationAt, nextCarry;
        readonly HashSet<string> carrying = new HashSet<string>();
        List<Transform_Magnet> magnetList;
        float magnetListAt;

        public static void AfterObjectAnimation(ObjectAnimationPlayer scr, int variant)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || scr == null) return;
            rt.NoteAnimationStarted(scr, variant, rt.LocalPlayer != null && rt.LocalPlayer.animationRun);
            if (Replaying || ReplayingInteraction) return;
            // Skipped as someone else's (BeforePlayerAnimation): this player is not the actor.
            if (rt.LocalPlayer != null && !rt.LocalPlayer.animationRun) return;
            rt.actorAnimation = SceneKeys.Key(scr);
            rt.actorAnimationAt = Time.realtimeSinceStartup;
        }

        // How MiSide wires a held item (seen on Scene 1's phone): the Transform_Magnet sits on the interaction's object;
        // transformTarget is the item (the phone in the room), transformMagnet the slot it goes to (the player's
        // "RightItem FixPosition"), and the magnet switches on (active) at the animation event that takes it. A magnet
        // without a target moves its own object. The item is held when it is parented under the player, or when the
        // magnet is on and the item has reached a slot on the player (within 0.5 m; the slot within 2 m of the feet).
        static Transform Moved(Transform_Magnet m) { return m.transformTarget != null ? m.transformTarget : m.transform; }
        static bool HeldBy(Transform_Magnet m, Transform root)
        {
            Transform item = Moved(m);
            if (item.IsChildOf(root)) return true;
            Transform slot = m.transformMagnet;
            return m.active && m.enabled && slot != null && slot.IsChildOf(root)
                && (item.position - slot.position).sqrMagnitude < 0.25f && (slot.position - root.position).sqrMagnitude < 4f;
        }

        List<Transform_Magnet> Magnets()
        {
            if (magnetList != null && Time.realtimeSinceStartup - magnetListAt < 3f) return magnetList;
            magnetListAt = Time.realtimeSinceStartup;
            magnetList = new List<Transform_Magnet>();
            foreach (Transform_Magnet m in Object.FindObjectsOfType<Transform_Magnet>(true)) if (m != null) magnetList.Add(m);
            return magnetList;
        }

        // The rule of what this player is performing now (null when nothing).
        CutsceneRules.Rule ActorRule()
        {
            if (actorAnimation == null) return null;
            string click = Time.realtimeSinceStartup - lastClickAt < 3 || actorAnimationAt - lastClickAt < 3 ? lastClickKey : null;
            return CutsceneRules.ForInteraction(click, actorAnimation);
        }

        bool ActorOnly { get { CutsceneRules.Rule r = ActorRule(); return r != null && r.Who == CutsceneRules.Who.Actor; } }

        void InteractionTick(float now)
        {
            PlayerMove player = LocalPlayer;
            if (player == null) return;
            // Finish: the actor's animation is over; with who=actor tell everyone to run its end (mirrored ones end by themselves).
            if (actorAnimation != null && now - actorAnimationAt > 0.5f && !player.animationRun)
            {
                string key = actorAnimation;
                bool relay = ActorOnly;
                actorAnimation = null;
                if (relay) Broadcast(Wire.Packet(Op.InteractionDone, w => Wire.Text(w, key)));
            }
            if (actorAnimation != null && !ActorOnly && carrying.Count == 0) return;
            if (now < nextCarry) return;
            nextCarry = now + 1f / 15f;
            BodyLayout layout = SelfLayout(player);
            if (layout == null) return;
            Transform body = layout.Root, root = player.transform;
            List<KeyValuePair<string, Transform>> held = new List<KeyValuePair<string, Transform>>();
            HashSet<string> now_ = new HashSet<string>();
            foreach (Transform_Magnet m in Magnets())
            {
                if (m == null || held.Count >= 8 || !HeldBy(m, root)) continue;
                string key = SceneKeys.Key(m);
                held.Add(new KeyValuePair<string, Transform>(key, Moved(m)));
                now_.Add(key);
            }
            List<string> released = new List<string>();
            foreach (string key in carrying) if (!now_.Contains(key)) released.Add(key);
            if (held.Count == 0 && released.Count == 0) return;
            int id = SelfId;
            Broadcast(Wire.Packet(Op.Carry, w =>
            {
                w.Write(id); w.Write((byte)held.Count);
                foreach (KeyValuePair<string, Transform> h in held)
                {
                    Wire.Text(w, h.Key);
                    WritePose(w, body.InverseTransformPoint(h.Value.position), Quaternion.Inverse(body.rotation) * h.Value.rotation);
                }
                w.Write((byte)released.Count);
                foreach (string key in released)
                {
                    Transform_Magnet m = SceneKeys.Find<Transform_Magnet>(key);
                    Wire.Text(w, key);
                    WritePose(w, m != null ? Moved(m).position : Vector3.zero, m != null ? Moved(m).rotation : Quaternion.identity);
                }
            }));
            carrying.Clear();
            foreach (string key in now_) carrying.Add(key);
        }

        // --- Watchers: carried objects follow the actor's body ---------------------------------------------------
        sealed class Carried { public int Owner; public Vector3 LocalPos; public Quaternion LocalRot; public Transform_Magnet Magnet; public float Since = Time.realtimeSinceStartup; }
        readonly Dictionary<string, Carried> carried = new Dictionary<string, Carried>();

        void ReadCarry(BinaryReader reader, int from)
        {
            long start = reader.BaseStream.Position;
            int id = reader.ReadInt32();
            if (Hosting)
            {
                id = from;
                long body = reader.BaseStream.Position;
                byte[] rest = new byte[reader.BaseStream.Length - body];
                reader.BaseStream.Read(rest, 0, rest.Length);
                reader.BaseStream.Position = body;
                int sender = id;
                Broadcast(Wire.Packet(Op.Carry, w => { w.Write(sender); w.Write(rest); }), from);
            }
            int count = reader.ReadByte();
            if (count > 8) throw new IOException("Too many carried objects.");
            for (int i = 0; i < count; i++)
            {
                string key = Wire.Text(reader, 1024);
                Vector3 lp; Quaternion lr; ReadTransform(reader, out lp, out lr);
                Carried c;
                if (!carried.TryGetValue(key, out c))
                {
                    Transform_Magnet m = SceneKeys.Find<Transform_Magnet>(key);
                    if (m == null) continue;
                    // The local magnet would pull the object back to this machine's own targets; pause it while carried.
                    m.enabled = false;
                    c = new Carried { Magnet = m };
                    carried[key] = c;
                }
                c.Owner = id; c.LocalPos = lp; c.LocalRot = lr;
                carryFramesApplied++;
            }
            int released = reader.ReadByte();
            if (released > 8) throw new IOException("Too many released objects.");
            for (int i = 0; i < released; i++)
            {
                string key = Wire.Text(reader, 1024);
                Vector3 p; Quaternion r; ReadTransform(reader, out p, out r);
                Carried c;
                if (!carried.TryGetValue(key, out c)) continue;
                carried.Remove(key);
                if (c.Magnet != null) { Moved(c.Magnet).SetPositionAndRotation(p, r); c.Magnet.enabled = true; }
            }
        }

        void LateUpdateCarried()
        {
            foreach (KeyValuePair<string, Carried> pair in carried)
            {
                Carried c = pair.Value;
                Peer owner;
                if (c.Magnet == null || !peers.TryGetValue(c.Owner, out owner) || owner.Avatar == null || owner.Avatar.Root == null) continue;
                Transform body = owner.Avatar.Root.transform;
                Moved(c.Magnet).SetPositionAndRotation(body.TransformPoint(c.LocalPos), body.rotation * c.LocalRot);
            }
        }

        void ReadInteractionDone(BinaryReader reader, int from)
        {
            string key = Wire.Text(reader, 1024);
            if (Hosting) Broadcast(Wire.Packet(Op.InteractionDone, w => Wire.Text(w, key)), from);
            ObjectAnimationPlayer scr;
            if (!pendingWatch.TryGetValue(key, out scr)) scr = SceneKeys.Find<ObjectAnimationPlayer>(key);
            pendingWatch.Remove(key);
            if (scr == null || scr.eventFinish == null) return;
            Replaying = true;
            try { scr.eventFinish.Invoke(); interactionsFinished++; }
            catch (Exception ex) { Log("Interaction finish threw here: " + ex.Message); }
            finally { Replaying = false; }
        }

        // Animation events of the actor's interaction (the game fires PlayerPerson.EventKey(i) from the clip): the others
        // run the same event of the same interaction, so what happens mid-animation happens for everyone.
        public static void AfterAnimEvent(int index)
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.Connected || Replaying || rt.actorAnimation == null) return;
            // Only for actor-only interactions; mirrored ones fire their own events on every machine.
            if (!rt.ActorOnly) return;
            string key = rt.actorAnimation;
            rt.Broadcast(Wire.Packet(Op.AnimEvent, w => { Wire.Text(w, key); w.Write(index); }));
        }

        void ReadAnimEvent(BinaryReader reader, int from)
        {
            string key = Wire.Text(reader, 1024);
            int index = reader.ReadInt32();
            if (Hosting) Broadcast(Wire.Packet(Op.AnimEvent, w => { Wire.Text(w, key); w.Write(index); }), from);
            ObjectAnimationPlayer scr;
            if (!pendingWatch.TryGetValue(key, out scr)) scr = SceneKeys.Find<ObjectAnimationPlayer>(key);
            if (scr == null || scr.eventsPlayer == null || index < 0 || index >= scr.eventsPlayer.Count) return;
            Replaying = true;
            try { scr.eventsPlayer[index].Invoke(); animEventsApplied++; }
            catch (Exception ex) { Log("Interaction event " + index + " threw here: " + ex.Message); }
            finally { Replaying = false; }
        }

        public int animEventsApplied;

        void ClearInteractions()
        {
            foreach (Carried c in carried.Values) if (c.Magnet != null) c.Magnet.enabled = true;
            carried.Clear(); carrying.Clear(); pendingWatch.Clear(); actorAnimation = null; magnetList = null;
        }
    }
}
