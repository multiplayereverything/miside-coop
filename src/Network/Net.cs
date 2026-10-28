using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // Star topology: clients talk only to the host (peer id 0), which relays to everyone else.
    public sealed partial class TogetherRuntime
    {
        void Broadcast(byte[] packet) { Broadcast(packet, -1); }

        void Broadcast(byte[] packet, int except)
        {
            if (Network == null) return;
            if (!Hosting) { if (self > 0) Network.Send(0, packet); return; }
            foreach (Peer peer in peers.Values) if (peer.Accepted && peer.Id != except) Network.Send(peer.Id, packet);
        }

        void SendToHost(byte[] packet) { if (IsClient && self > 0) Network.Send(0, packet); }

        void PumpNetwork()
        {
            NetworkEvent e;
            int budget = 256;
            while (Network != null && budget-- > 0 && Network.Poll(out e))
            {
                try { HandleEvent(e); }
                catch (Wire.BadValueException) { badValues++; if (badValues < 20) Log("Skipped a packet with an invalid number from " + e.Peer + " (op " + (e.Data != null && e.Data.Length > 0 ? ((Op)e.Data[0]).ToString() : "?") + ")"); }
                catch (IOException ex) { Log("Malformed packet from " + e.Peer + ": " + ex.Message); if (Hosting && e.Peer > 0) Network.Drop(e.Peer); }
                // A game-side failure while applying one update must not cost anyone their connection.
                catch (Exception ex) { Log("Could not apply a packet from " + e.Peer + ": " + ex.GetType().Name + ": " + ex.Message); }
            }
        }

        static bool Finite(Vector3 v) { return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z)) && v.sqrMagnitude < 1e10f; }
        int badValues;

        void HandleEvent(NetworkEvent e)
        {
            if (e.Kind == "note") { Log("Network: " + e.Message); return; }
            if (e.Kind == "error")
            {
                Tell("Network error: " + e.Message);
                if (IsClient && self <= 0) Shutdown();
                return;
            }
            if (e.Kind == "connected")
            {
                if (Hosting) peers[e.Peer] = new Peer { Id = e.Peer, LastHeard = Time.realtimeSinceStartup };
                else SendHello();
                return;
            }
            if (e.Kind == "disconnected")
            {
                if (!Hosting) { if (TryMigrate()) return; Shutdown(); Tell("Lost connection to the host. You keep playing alone."); return; }
                Peer gone;
                if (peers.TryGetValue(e.Peer, out gone))
                {
                    DestroyAvatar(gone); peers.Remove(e.Peer);
                    if (gone.Accepted) { Tell(gone.Name + " left."); SendRoster(); }
                }
                return;
            }
            using (BinaryReader reader = new BinaryReader(new MemoryStream(e.Data)))
            {
                Op op = (Op)reader.ReadByte();
                if (Hosting) HostPacket(e.Peer, op, reader); else ClientPacket(op, reader);
            }
        }

        public void SendHello()
        {
            if (!IsClient) return;
            int listen = Port;
            Network.Send(0, Wire.Packet(Op.Hello, w => { w.Write(Wire.Version); Wire.Text(w, ModVersion); Wire.Text(w, gameHash); Wire.Text(w, Name); w.Write(Colour); w.Write(Steam.Self); w.Write(listen); Wire.Text(w, ActiveSceneName); w.Write(rejoining); }));
        }

        void HostPacket(int from, Op op, BinaryReader reader)
        {
            Peer peer;
            if (!peers.TryGetValue(from, out peer)) return;
            peer.LastHeard = Time.realtimeSinceStartup;
            if (op == Op.Hello)
            {
                int version = reader.ReadInt32();
                string mod = Wire.Text(reader, 32), hash = Wire.Text(reader, 80), name = CleanName(Wire.Text(reader, 32));
                byte colour = reader.ReadByte();
                ulong steamId = reader.ReadUInt64();
                int listenPort = reader.ReadInt32();
                string theirScene = Wire.Text(reader, 128);
                bool rejoin = reader.ReadBoolean();
                if (version != Wire.Version || mod != ModVersion) { Reject(from, "Version mismatch: host runs MiSide Together " + ModVersion + "."); return; }
                if (hash != gameHash) { Reject(from, "Your game version differs from the host's. Update both copies of MiSide."); return; }
                bool first = !peer.Accepted;
                if (first && AcceptedCount >= MaxPlayers - 1) { Reject(from, "The game is full (" + MaxPlayers + " players)."); return; }
                peer.Name = name; peer.Colour = (byte)Mathf.Clamp(colour, 0, Colours.Length - 1); peer.Accepted = true;
                // Who takes over if the host goes away, and how to reach them (Migration.cs).
                peer.SteamId = steamId; peer.ListenPort = listenPort > 0 && listenPort < 65536 ? listenPort : 27030; peer.Address = Network.Address(from) ?? "";
                if (first)
                {
                    Network.Send(from, Wire.Packet(Op.Welcome, w => w.Write(from)));
                    Tell(name + " joined.");
                    SendSavesTo(from);
                    // No joining in the middle of a chapter: the player waits in the menu and comes along when the host
                    // starts the next chapter (or goes back to the menu). Someone rejoining after a host migration, who is
                    // already in this chapter, carries on.
                    // Someone already in this chapter (after a host migration, or reconnecting after a dropped link)
                    // carries on; anyone else waits for the next chapter.
                    if (HostMidChapter && theirScene != hostScene)
                    {
                        peer.Waiting = true;
                        Network.Send(from, Wire.Packet(Op.Notice, w => Wire.Text(w, "wait-chapter")));
                        Tell(name + " will join at the start of the next chapter.");
                    }
                    else SendSceneTo(from);
                }
                SendRoster();
                return;
            }
            if (!peer.Accepted) return;
            switch (op)
            {
                case Op.Pose:
                    ReadPose(peer, reader);
                    Broadcast(PosePacket(peer.Id, peer), from);
                    break;
                case Op.SceneReady: HostSceneReady(peer, reader); break;
                case Op.Interact:
                case Op.MouseClick:
                case Op.TriggerEnter:
                case Op.TriggerExit:
                case Op.DialogueChoice:
                case Op.SceneLoad:
                case Op.CutscenePlay:
                case Op.KeyAdd:
                case Op.KeyRemove:
                case Op.Death:
                case Op.SkipDialogue:
                case Op.LookTrigger:
                case Op.DistanceTrigger:
                case Op.Call:
                    ReadStoryEvent(op, reader, from);
                    break;
                case Op.Door: ApplyDoor(reader, from); break;
                case Op.Visible: ReadVisibility(reader, from); break;
                case Op.HandItem: ReadHandItem(reader, from); break;
                case Op.Prop: ApplyProp(reader, from); break;
                case Op.Minigame: ReadMinigame(reader, from); break;
                case Op.Carry: ReadCarry(reader, from); break;
                case Op.InteractionDone: ReadInteractionDone(reader, from); break;
                case Op.AnimEvent: ReadAnimEvent(reader, from); break;
                case Op.Novella: ReadNovella(reader, from); break;
                case Op.Notice: { string n = Wire.Text(reader, 1100); if (n == "lab-gaze") labGazeAt = Time.realtimeSinceStartup; else if (n.StartsWith("lab-hostcut:", StringComparison.Ordinal)) HostCutAsked(n.Substring(12)); break; }
                case Op.Ping: Network.Send(from, Wire.Packet(Op.Pong, null)); break;
                case Op.Pong: break;
                case Op.Leave: Network.Drop(from); break;
            }
        }

        void Reject(int id, string reason)
        {
            Network.SendAndClose(id, Wire.Packet(Op.Reject, w => Wire.Text(w, reason)));
            Log("Rejected peer " + id + ": " + reason);
        }

        void SendRoster()
        {
            if (!Hosting) return;
            List<Peer> list = new List<Peer>();
            foreach (Peer p in peers.Values) if (p.Accepted) list.Add(p);
            Broadcast(Wire.Packet(Op.Roster, w =>
            {
                w.Write((byte)(list.Count + 1));
                w.Write(0); Wire.Text(w, Name); w.Write(Colour); w.Write(Steam.Self); Wire.Text(w, ""); w.Write(Port);
                foreach (Peer p in list) { w.Write(p.Id); Wire.Text(w, p.Name); w.Write(p.Colour); w.Write(p.SteamId); Wire.Text(w, p.Address); w.Write(p.ListenPort); }
            }));
        }

        void ClientPacket(Op op, BinaryReader reader)
        {
            switch (op)
            {
                case Op.Welcome:
                    self = reader.ReadInt32();
                    rejoining = false;
                    Tell("Connected as player " + self + ". You follow the host's story.");
                    break;
                case Op.Reject:
                    string reason = Wire.Text(reader, 400);
                    Shutdown(); Tell("Host refused: " + reason);
                    break;
                case Op.Roster:
                    int count = reader.ReadByte();
                    HashSet<int> seen = new HashSet<int>();
                    for (int i = 0; i < count; i++)
                    {
                        int id = reader.ReadInt32(); string name = Wire.Text(reader, 32); byte colour = reader.ReadByte();
                        ulong steamId = reader.ReadUInt64(); string address = Wire.Text(reader, 64); int listenPort = reader.ReadInt32();
                        if (id == self) continue;
                        seen.Add(id);
                        Peer p; if (!peers.TryGetValue(id, out p)) { p = new Peer { Id = id }; peers[id] = p; Tell(name + " is here."); }
                        p.Name = name; p.Colour = (byte)Mathf.Clamp(colour, 0, Colours.Length - 1); p.Accepted = true;
                        p.SteamId = steamId; p.Address = address; p.ListenPort = listenPort;
                    }
                    List<int> stale = new List<int>();
                    foreach (int id in peers.Keys) if (!seen.Contains(id)) stale.Add(id);
                    foreach (int id in stale) { Tell(peers[id].Name + " left."); DestroyAvatar(peers[id]); peers.Remove(id); }
                    break;
                case Op.Pose:
                    int who = reader.ReadInt32();
                    Peer target;
                    if (who != self && peers.TryGetValue(who, out target)) ReadPose(target, reader);
                    break;
                case Op.Scene: ClientScene(reader); break;
                case Op.History: ClientHistory(reader); break;
                case Op.Interact:
                case Op.MouseClick:
                case Op.TriggerEnter:
                case Op.TriggerExit:
                case Op.DialogueChoice:
                case Op.SceneLoad:
                case Op.CutscenePlay:
                case Op.KeyAdd:
                case Op.KeyRemove:
                case Op.Death:
                case Op.SkipDialogue:
                case Op.LookTrigger:
                case Op.DistanceTrigger:
                case Op.Call:
                    ReadStoryEvent(op, reader, 0);
                    break;
                case Op.Mita: ClientMita(reader); break;
                case Op.Door: ApplyDoor(reader, 0); break;
                case Op.Visible: ReadVisibility(reader, 0); break;
                case Op.HandItem: ReadHandItem(reader, 0); break;
                case Op.Prop: ApplyProp(reader, 0); break;
                case Op.Minigame: ReadMinigame(reader, 0); break;
                case Op.Carry: ReadCarry(reader, 0); break;
                case Op.InteractionDone: ReadInteractionDone(reader, 0); break;
                case Op.Dialogue: ReadDialogue(reader); break;
                case Op.WorldState: ReadWorldState(reader); break;
                case Op.AnimEvent: ReadAnimEvent(reader, 0); break;
                case Op.Novella: ReadNovella(reader, 0); break;
                case Op.SaveFile: ClientSaveFile(reader); break;
                case Op.Face: ClientFace(reader); break;
                case Op.Cutscene: ClientCutscene(reader); break;
                case Op.Notice:
                    string notice = Wire.Text(reader, 400);
                    if (notice.StartsWith("go:", StringComparison.Ordinal)) { GoNotice(notice.Substring(3)); break; }
                    if (notice == "gather") { if (!TryTeleportToHost(false)) Tell("The host called everyone, but there is no safe spot beside them right now."); else Tell("The host brought you to them."); break; }
                    if (notice == "wait-chapter") { waitingForChapter = true; Tell("The host is in the middle of a chapter. You join them when the next chapter starts."); break; }
                    if (notice == "lab-step") { labStepAt = Time.realtimeSinceStartup; break; }
                    if (notice == "lab-gaze") { labGazeAt = Time.realtimeSinceStartup; break; }
                    if (notice.StartsWith("lab-press", StringComparison.Ordinal)) { InteractionsNotice(notice); break; }
                    if (notice.StartsWith("lab-seq:", StringComparison.Ordinal)) { SteamSeqNotice(notice); break; }
                    if (notice.StartsWith("lab-quest:", StringComparison.Ordinal)) { QuestNotice(notice); break; }
                    if (notice == "lab-quest-open") { LabOpenQuest(); break; }
                    if (notice.StartsWith("lab-mobile:", StringComparison.Ordinal)) { MobileNotice(notice); break; }
                    if (notice.StartsWith("lab-core:", StringComparison.Ordinal)) { CoreNotice(notice); break; }
                    if (notice.StartsWith("lab-safe:", StringComparison.Ordinal)) { SafeNotice(notice); break; }
                    if (notice == "lab-choice") { ChoiceNotice(); break; }
                    if (notice.StartsWith("lab-shot:", StringComparison.Ordinal)) { LabShot(notice.Substring(9) + "-" + Name); break; }
                    if (notice.StartsWith("lab-drop:", StringComparison.Ordinal)) { string[] v = notice.Substring(9).Split(','); labDrop = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture)); if (v.Length > 3) labDropKey = string.Join(",", v, 3, v.Length - 3); labDropKnown = true; break; }
                    if (notice.StartsWith("lab-room:", StringComparison.Ordinal)) { labRoomNotice = notice.Substring(9); labRoomNoticeAt = Time.realtimeSinceStartup; break; }
                    if (notice.StartsWith("lab-seed:", StringComparison.Ordinal)) { int.TryParse(notice.Substring(9), out labSeed); break; }
                    Tell(notice);
                    break;
                case Op.Ping: SendToHost(Wire.Packet(Op.Pong, null)); break;
            }
        }

        // --- Poses ------------------------------------------------------------------------------
        // Pose = body transform + animator frame, so remote players show the real character model and its animations.
        static byte[] PosePacket(int id, Peer source)
        {
            return Wire.Packet(Op.Pose, w =>
            {
                w.Write(id); w.Write(source.SceneHash);
                WritePose(w, source.Position, source.Rotation);
                w.Write(source.Feet.x); w.Write(source.Feet.y); w.Write(source.Feet.z);
                w.Write((byte)((source.Free ? 1 : 0) | (source.InCutscene ? 2 : 0) | (source.WatchMe ? 4 : 0) | (source.Acting ? 8 : 0)));
                WritePose(w, source.CamPos, source.CamRot); w.Write(source.Fov);
                source.Anim.Write(w);
                w.Write(source.Bones.Count > 0);
                if (source.Bones.Count > 0) source.Bones.Write(w);
            });
        }

        static void WritePose(BinaryWriter w, Vector3 pos, Quaternion rot)
        {
            w.Write(pos.x); w.Write(pos.y); w.Write(pos.z);
            w.Write(rot.x); w.Write(rot.y); w.Write(rot.z); w.Write(rot.w);
        }

        static void ReadTransform(BinaryReader reader, out Vector3 pos, out Quaternion rot)
        {
            pos = new Vector3(Wire.Number(reader), Wire.Number(reader), Wire.Number(reader));
            rot = new Quaternion(Wire.Number(reader), Wire.Number(reader), Wire.Number(reader), Wire.Number(reader));
            if (rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w < 0.01f) rot = Quaternion.identity;
        }

        void ReadPose(Peer peer, BinaryReader reader)
        {
            if (Hosting) reader.ReadInt32();
            peer.SceneHash = reader.ReadInt32();
            Vector3 pos; Quaternion rot;
            ReadTransform(reader, out pos, out rot);
            Vector3 feet = new Vector3(Wire.Number(reader), Wire.Number(reader), Wire.Number(reader));
            // When the feet last changed height (falling, a lift, a descent animation): teleports wait for steady feet.
            if (Mathf.Abs(feet.y - peer.Feet.y) > 0.03f) peer.FeetMovedAt = Time.realtimeSinceStartup;
            peer.Feet = feet;
            byte flags = reader.ReadByte();
            peer.Free = (flags & 1) != 0; peer.InCutscene = (flags & 2) != 0; peer.WatchMe = (flags & 4) != 0; peer.Acting = (flags & 8) != 0;
            Vector3 camPos; Quaternion camRot; ReadTransform(reader, out camPos, out camRot);
            peer.CamPos = camPos; peer.CamRot = camRot; peer.Fov = Wire.Number(reader);
            peer.Anim.Read(reader);
            if (reader.ReadBoolean()) { peer.Bones.Read(reader); peer.BonesAt = Time.realtimeSinceStartup; }
            else peer.Bones.Count = 0;
            if (!peer.HasPose || (pos - peer.Position).sqrMagnitude > 100f) { peer.ShownPosition = pos; peer.ShownRotation = rot; }
            peer.Position = pos; peer.Rotation = rot;
            peer.HasPose = true; peer.PoseAt = Time.realtimeSinceStartup;
        }

        readonly Peer selfPose = new Peer();

        void SendPose()
        {
            PlayerMove player = LocalPlayer;
            selfPose.SceneHash = player != null ? LocalSceneHash : 0;
            Transform body = player != null && player.animPerson != null ? player.animPerson.transform : player != null ? player.transform : null;
            selfPose.Position = body != null ? body.position : Vector3.zero;
            selfPose.Rotation = body != null ? body.rotation : Quaternion.identity;
            selfPose.Feet = player != null ? player.transform.position : Vector3.zero;
            selfPose.Free = LocalFree(player);
            // Cutscene mode can switch the player object off, so this does not depend on it.
            selfPose.InCutscene = LocalInCutscene();
            selfPose.WatchMe = WatchMe();
            selfPose.Acting = actorAnimation != null || OwningMinigame;
            CaptureCamera(selfPose, body);
            if (player != null && player.animPerson != null) selfPose.Anim.Capture(player.animPerson); else selfPose.Anim.Clear();
            // Update runs before this frame's animation, so the bones still hold the pose the player last saw rendered,
            // after the game's IK and LateUpdate adjustments.
            BodyLayout layout = SelfLayout(player);
            if (layout != null) selfPose.Bones.Capture(layout); else selfPose.Bones.Count = 0;
            if (player != null) SendVisibility(player);
            // A pose computed mid-teleport or on a broken frame can hold NaN; the next frame's is sent instead.
            if (!Finite(selfPose.Position) || !Finite(selfPose.Feet) || !Finite(selfPose.CamPos) || float.IsNaN(selfPose.Fov) || float.IsNaN(selfPose.CamRot.w) || float.IsNaN(selfPose.Rotation.w)) return;
            Broadcast(PosePacket(SelfId, selfPose));
        }

        public void Notice(string text)
        {
            Tell(text);
            if (Hosting) Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, text)));
        }

        void TeleportToHost()
        {
            Peer host;
            PlayerMove player = LocalPlayer;
            TryTeleportToHost(true);
        }

        // Returns true once moved. Refuses (and says why) while either player is in a cutscene or scripted animation.
        bool TryTeleportToHost(bool tell)
        {
            Peer host;
            PlayerMove player = LocalPlayer;
            string why = null;
            if (!peers.TryGetValue(0, out host) || !host.HasPose || player == null) why = "The host's position is not known yet.";
            else if (host.SceneHash != LocalSceneHash) why = "The host is in another scene; you will follow when it finishes loading.";
            else if (!host.Free) why = "The host is in a cutscene; try again in a moment.";
            else if (Time.realtimeSinceStartup - host.FeetMovedAt < 1f) why = "The host is moving up or down; try again in a moment.";
            else if (!LocalFree(player)) why = "You are in a cutscene; try again in a moment.";
            if (why != null) { if (tell) Tell(why); return false; }
            // Minimal and safe: at the host's own height, at most 0.9 m beside them, and only where a ray from the host's
            // chest reaches the spot without hitting anything and there is floor right under it. Otherwise exactly on the
            // host's spot (bodies do not collide). Rays never start above the host's head, so a ceiling or the top of a
            // cabinet can never be taken for floor.
            Vector3 forward = host.Rotation * Vector3.forward; forward.y = 0;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            // The host's feet (PlayerMove root), not the body: the body can sit well above the floor.
            Vector3 feet = host.Feet;
            Vector3 target = feet;
            foreach (Vector3 dir in new[] { right, -right, -forward, forward })
            {
                Vector3 spot;
                if (ClearBeside(feet, dir, out spot)) { target = spot; break; }
            }
            Vector3 face = feet - target; face.y = 0;
            float yaw = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face).eulerAngles.y : host.Rotation.eulerAngles.y;
            player.TeleportPlayer(target, yaw);
            lastTeleportAt = Time.realtimeSinceStartup; lastTeleportHostY = feet.y;
            if (tell) Tell("Moved next to the host.");
            Log("Teleported next to the host at " + target + " (host feet at " + feet + ")");
            return true;
        }

        public float lastTeleportAt = -1, lastTeleportHostY;
        static bool raysBroken;

        static bool ClearBeside(Vector3 feet, Vector3 dir, out Vector3 spot) { return ClearBeside(feet, dir, 0.9f, out spot); }

        static bool ClearBeside(Vector3 feet, Vector3 dir, float distance, out Vector3 spot)
        {
            spot = feet;
            if (raysBroken) return false;
            try
            {
                if (Physics.Raycast(feet + Vector3.up * 1.0f, dir, distance + 0.4f, ~0, QueryTriggerInteraction.Ignore)) return false;
                if (Physics.Raycast(feet + Vector3.up * 0.4f, dir, distance + 0.4f, ~0, QueryTriggerInteraction.Ignore)) return false;
                Vector3 p = feet + dir * distance;
                RaycastHit hit;
                if (!Physics.Raycast(p + Vector3.up * 0.4f, Vector3.down, out hit, 0.8f, ~0, QueryTriggerInteraction.Ignore)) return false;
                if (Mathf.Abs(hit.point.y - feet.y) > 0.25f) return false;
                spot = hit.point;
                return true;
            }
            catch (Exception ex) { raysBroken = true; Log("Physics rays unavailable: " + ex.Message); return false; }
        }

        // Floor under a point, searched from just above it (never from above a ceiling).
        static bool GroundAt(Vector3 near, out Vector3 spot)
        {
            spot = near;
            if (raysBroken) return true;
            try
            {
                RaycastHit hit;
                if (Physics.Raycast(near + Vector3.up * 0.3f, Vector3.down, out hit, 2f, ~0, QueryTriggerInteraction.Ignore)) { spot = hit.point; return true; }
                return false;
            }
            catch (Exception ex) { raysBroken = true; Log("Physics rays unavailable: " + ex.Message); return true; }
        }
        GameController cachedController;

        // Free = the local player walks around on their own (no cutscene, no scripted animation, not frozen by the story).
        bool LocalFree(PlayerMove player)
        {
            if (player == null || player.dontMove || player.animationRun || player.typeAnimation || player.sit != 0) return false;
            if (cachedController == null) cachedController = UnityEngine.Object.FindObjectOfType<GameController>();
            return cachedController == null || !cachedController.cutsceneRun;
        }
    }
}
