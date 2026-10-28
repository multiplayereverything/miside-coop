using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MiSideTogether
{
    // The host leads: clients load whatever chapter scene the host is in, then replay the story events the host's
    // group already produced in that scene (the catch-up history) so late arrivals reach the same story state.
    public sealed partial class TogetherRuntime
    {
        const int MaxHistory = 4000;
        // Host side
        string hostScene = "";
        int sceneSeq;
        readonly List<byte[]> history = new List<byte[]>();
        // Client side
        string wantedScene = "";
        int wantedSeq = -1, readySentSeq = -1, historySeq = -1;
        float wantedSince, arrivedAt = -1, nextLoadCheck;
        AsyncOperation followLoad;
        readonly Queue<byte[]> replayQueue = new Queue<byte[]>();
        string lastActiveScene = "";

        public static bool FollowableScene(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.StartsWith("Scene ", StringComparison.Ordinal) || name == "SceneMenu" || name == "MinigameShooter";
        }

        void ResetSceneSync()
        {
            hostScene = ""; history.Clear(); waitingForChapter = false;
            if (barrierHolding) { Time.timeScale = 1; AudioListener.pause = false; barrierHolding = false; }
            barrierSeq = -1; barrierSince = -1; barrierReady.Clear();
            wantedScene = ""; wantedSeq = -1; readySentSeq = -1; historySeq = -1; arrivedAt = -1;
            followLoad = null; replayQueue.Clear();
        }

        void UpdateSceneSync()
        {
            string active = ActiveSceneName;
            if (active != lastActiveScene)
            {
                Log("Scene now " + active + " (was " + lastActiveScene + ")");
                lastActiveScene = active;
                CaptureLoadStatics(active);
                OnLocalSceneChanged();
                arrivedAt = Time.realtimeSinceStartup;
            }
            if (!Connected) return;
            StartBarrierTick(Time.realtimeSinceStartup);
            if (Hosting) { HostSceneTick(active); return; }
            ClientSceneTick(active);
        }

        void HostSceneTick(string active)
        {
            // The loading screen already names the chapter: announce it now, so everyone loads at the same time.
            if (active == "SceneLoading" && !string.IsNullOrEmpty(GlobalGame.LoadingLevel) && GlobalGame.LoadingLevel.StartsWith("Scene ", StringComparison.Ordinal)) active = GlobalGame.LoadingLevel;
            if (!FollowableScene(active) || active == hostScene) return;
            hostScene = active;
            sceneSeq++;
            // Everyone starts the chapter together (StartBarrierTick).
            barrierSeq = HostMidChapter ? sceneSeq : -1; barrierReady.Clear(); barrierSince = -1;
            // Players held back from the last chapter come along now.
            foreach (Peer p in peers.Values) if (p.Waiting) { p.Waiting = false; Log(p.Name + " joins at " + active); }
            history.Clear();
            Broadcast(ScenePacket());
            Log("Host scene " + sceneSeq + ": " + hostScene);
        }

        byte[] ScenePacket()
        {
            GameStatics statics = hostLoadStatics ?? GameStatics.Capture();
            return Wire.Packet(Op.Scene, w => { w.Write(sceneSeq); Wire.Text(w, hostScene); statics.Write(w); });
        }

        public const int MaxPlayers = 4;
        bool waitingForChapter, rejoining;

        // A chapter is under way (the menu is not): new players wait for the next one.
        bool HostMidChapter { get { return hostScene.StartsWith("Scene ", StringComparison.Ordinal) || hostScene == "MinigameShooter"; } }

        void SendSceneTo(int peer)
        {
            if (hostScene.Length > 0) Network.Send(peer, ScenePacket());
        }

        // A client has the host's scene loaded; send it everything that already happened there.
        void HostSceneReady(Peer peer, BinaryReader reader)
        {
            int seq = reader.ReadInt32();
            if (seq != sceneSeq) return;
            barrierReady.Add(peer.Id);
            int sent = 0;
            // Split into frames under the transport cap; the client concatenates them in order.
            int index = 0;
            do
            {
                int start = index;
                int size = 16;
                while (index < history.Count && size + history[index].Length + 4 < Transport.MaxFrame - 64) { size += history[index].Length + 4; index++; }
                int end = index;
                bool last = end >= history.Count;
                Network.Send(peer.Id, Wire.Packet(Op.History, w =>
                {
                    w.Write(seq); w.Write(last); w.Write((ushort)(end - start));
                    for (int i = start; i < end; i++) { w.Write(history[i].Length); w.Write(history[i]); }
                }));
                sent += end - start;
                if (end == start && !last) break;
            }
            while (index < history.Count);
            Log("Sent " + sent + " catch-up events to " + peer.Name);
            // Then the room as it is now (everything switched, moved or relit since the chapter started).
            SendWorldStateTo(peer.Id);
        }

        void RecordHistory(byte[] packet)
        {
            if (!Hosting) return;
            if (history.Count >= MaxHistory) history.RemoveAt(0);
            history.Add(packet);
        }

        void ClientScene(BinaryReader reader)
        {
            int seq = reader.ReadInt32();
            string name = Wire.Text(reader, 128);
            clientLoadStatics = GameStatics.Read(reader);
            clientLoadStatics.ApplyIdentity();
            if (LabMode) Log("Host statics: " + clientLoadStatics);
            if (!FollowableScene(name)) return;
            wantedSeq = seq; wantedScene = name; wantedSince = Time.realtimeSinceStartup;
            waitingForChapter = false;
            // Already in this chapter (reconnecting): its start is long past, so no start barrier.
            barrierSeq = name.StartsWith("Scene ", StringComparison.Ordinal) && ActiveSceneName != name ? seq : -1; barrierSince = -1;
            historySeq = -1; replayQueue.Clear();
            Log("Host is in " + name + " (scene " + seq + ")");
        }

        // A client that leaves the chapter for the main menu by its own choice (pause menu, main menu:
        // GameController.ExitGame) leaves the game instead of being pulled back in (playtest). The mod's own call, when it
        // follows the host to the menu, is not the player's choice.
        static bool exitingForHost;
        public static bool leftByChoice;

        public static void BeforeExitGame()
        {
            TogetherRuntime rt = Instance;
            if (rt == null || !rt.IsClient || !rt.Connected || exitingForHost) return;
            rt.Tell("You left the game (back to the main menu).");
            leftByChoice = true;
            rt.Shutdown();
        }

        void ClientSceneTick(string active)
        {
            if (wantedScene.Length == 0) return;
            float now = Time.realtimeSinceStartup;
            if (active != wantedScene)
            {
                if (followLoad != null && !followLoad.isDone) return;
                // Grace period: the same story event usually loads the scene on this machine too.
                if (now - wantedSince < 3f || now < nextLoadCheck) return;
                if (LocalLoadInProgress(active)) { nextLoadCheck = now + 1; return; }
                nextLoadCheck = now + 10;
                Tell("Following the host to " + wantedScene + "...");
                // Same GlobalGame state the host's menu or story set before its load (chapter, save level, flags).
                if (clientLoadStatics != null) clientLoadStatics.ApplyLoad(active.StartsWith("Scene ", StringComparison.Ordinal));
                // From a chapter to the main menu: the game's own way out (pause menu, main menu), which also takes the
                // player away. A plain load of the menu left the player active there (playtest: "I see my hands").
                GameController controller = wantedScene == "SceneMenu" && active.StartsWith("Scene ", StringComparison.Ordinal) ? UnityEngine.Object.FindObjectOfType<GameController>() : null;
                if (controller != null) { exitingForHost = true; try { controller.ExitGame(); } finally { exitingForHost = false; } return; }
                followLoad = LoadThroughGame(wantedScene);
                return;
            }
            followLoad = null;
            GatherAfterCutscene(now);
            // Ready: the scene is up (and the player, unless the start barrier holds the game, which can delay it).
            if (readySentSeq != wantedSeq && arrivedAt >= 0 && now - arrivedAt > 1.5f && (wantedScene == "SceneMenu" || LocalPlayer != null || barrierHolding || now - arrivedAt > 8))
            {
                readySentSeq = wantedSeq;
                SendToHost(Wire.Packet(Op.SceneReady, w => w.Write(wantedSeq)));
            }
        }

        // Chapter scenes must go through the game's loading scene, which reads GlobalGame.LoadingLevel; loading one
        // directly leaves World/WorldPlayer uninitialised (NullReferenceException every frame, no player).
        public static AsyncOperation LoadThroughGame(string scene)
        {
            if (scene == "SceneMenu") return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            GlobalGame.LoadingLevel = scene;
            return SceneManager.LoadSceneAsync("SceneLoading", LoadSceneMode.Single);
        }

        // After a cutscene everyone carries on together: when the host's cutscene ends (or this player's own), a client
        // left more than 3 m away is brought to the host's feet with the same safe teleport as "Go to the host".
        bool hostWasInCutscene, wasInCutscene;
        float gatherAt = -1;
        public int gathers;

        void GatherAfterCutscene(float now)
        {
            Peer host; PlayerMove player = LocalPlayer;
            if (!ClientStoryLive || !peers.TryGetValue(0, out host) || !host.HasPose) return;
            bool mine = localCutscene;
            if ((hostWasInCutscene && !host.InCutscene) || (wasInCutscene && !mine)) gatherAt = now + 1.5f;
            hostWasInCutscene = host.InCutscene; wasInCutscene = mine;
            if (player == null || host.SceneHash != LocalSceneHash) return;
            if (gatherAt < 0 || now < gatherAt || host.InCutscene || mine) return;
            if (now - gatherAt > 20) { gatherAt = -1; return; }
            if ((player.transform.position - host.Feet).sqrMagnitude < 9f) { gatherAt = -1; return; }
            if (Time.frameCount % 30 == 0 && TryTeleportToHost(false)) { gatherAt = -1; gathers++; Tell("Brought you to the host after the cutscene."); }
            else if (LabMode && Time.frameCount % 300 == 0) Log("Gather waiting: host free=" + host.Free + " me free=" + LocalFree(player) + " distance " + (player.transform.position - host.Feet).magnitude.ToString("0.0"));
        }

        bool LocalLoadInProgress(string active)
        {
            if (active == "SceneLoading" || active == "SceneAihasto") return true;
            foreach (Scene_Load load in UnityEngine.Object.FindObjectsOfType<Scene_Load>())
                if (load != null && load.loading) return true;
            return false;
        }

        void ClientHistory(BinaryReader reader)
        {
            int seq = reader.ReadInt32();
            bool last = reader.ReadBoolean();
            int count = reader.ReadUInt16();
            if (seq != wantedSeq) return;
            for (int i = 0; i < count; i++)
            {
                int size = reader.ReadInt32();
                if (size < 1 || size > Transport.MaxFrame) throw new IOException("Invalid history entry.");
                replayQueue.Enqueue(Transport.ReadExactly(reader.BaseStream, size));
            }
            if (last)
            {
                historySeq = seq;
                if (replayQueue.Count > 0) Tell("Catching up with " + replayQueue.Count + " story events...");
            }
        }

        // Live story events are only applied once the catch-up history for this scene has arrived (it contains them).
        bool ClientStoryLive { get { return historySeq == wantedSeq && wantedSeq >= 0; } }

        int replayDelay;

        void ReplayTick()
        {
            if (replayQueue.Count == 0) return;
            // One event every few frames gives cutscene triggers time to start like they would live.
            if (replayDelay-- > 0) return;
            replayDelay = 3;
            byte[] packet = replayQueue.Dequeue();
            using (BinaryReader reader = new BinaryReader(new MemoryStream(packet)))
            {
                Op op = (Op)reader.ReadByte();
                ApplyStoryEvent(op, reader);
            }
            if (replayQueue.Count == 0) Tell("Caught up with the host.");
        }

        // --- Chapter start barrier --------------------------------------------------------------------------------
        // Machines finish loading a chapter seconds apart, and its opening (timelines, Mita, first lines) starts on
        // arrival (Scene 1's "Day 1" plays before the player even exists). So every machine holds the game still from
        // the moment the chapter scene is active, until the host has heard from every player (SceneReady) and says go,
        // or 45 s pass. Then all openings start together.
        int barrierSeq = -1;
        float barrierSince = -1;
        bool barrierHolding;
        readonly HashSet<int> barrierReady = new HashSet<int>();
        public float lastBarrierWait;

        void StartBarrierTick(float now)
        {
            bool hold = false;
            if (Connected && barrierSeq >= 0 && (Hosting ? barrierSeq == sceneSeq && ActiveSceneName == hostScene : barrierSeq == wantedSeq && ActiveSceneName == wantedScene))
            {
                if (barrierSince < 0) barrierSince = now;
                hold = now - barrierSince < 45;
                if (Hosting)
                {
                    bool all = true;
                    foreach (Peer p in peers.Values) if (p.Accepted && !p.Waiting && !barrierReady.Contains(p.Id)) all = false;
                    if (all || !hold)
                    {
                        int seq = barrierSeq;
                        Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "go:" + seq)));
                        ReleaseBarrier(now, all ? "everyone loaded" : "timed out");
                        return;
                    }
                }
                else if (!hold) { ReleaseBarrier(now, "timed out"); return; }
            }
            if (hold)
            {
                // The game may set its own time scale; hold it every frame.
                Time.timeScale = 0; AudioListener.pause = true; barrierHolding = true;
            }
            else if (barrierHolding) ReleaseBarrier(now, "left");
        }

        void ReleaseBarrier(float now, string why)
        {
            lastBarrierWait = barrierSince >= 0 ? now - barrierSince : 0;
            if (barrierHolding || barrierSince >= 0) Log("Chapter start: held " + lastBarrierWait.ToString("0.0") + " s (" + why + ")");
            barrierSeq = -1; barrierSince = -1;
            if (barrierHolding) { Time.timeScale = 1; AudioListener.pause = false; barrierHolding = false; }
        }

        void GoNotice(string text) { int seq; if (int.TryParse(text, out seq) && seq == barrierSeq) ReleaseBarrier(Time.realtimeSinceStartup, "host says go"); }

        string WaitingText()
        {
            if (barrierHolding) return "Waiting for everyone to load...";
            if (!IsClient || !Connected) return null;
            if (waitingForChapter) return "Waiting for the host to start the next chapter...";
            if (wantedScene.Length > 0 && ActiveSceneName != wantedScene) return "Following the host to " + wantedScene + "...";
            if (replayQueue.Count > 0) return "Catching up with the host (" + replayQueue.Count + ")...";
            return null;
        }
    }
}
