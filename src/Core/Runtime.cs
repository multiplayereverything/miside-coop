using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    public sealed class Peer
    {
        public int Id;
        public string Name = "Player";
        public bool Accepted, HasPose, Free, InCutscene, WatchMe, Acting, Waiting;
        public Vector3 CamPos; public Quaternion CamRot = Quaternion.identity; public float Fov = 60;
        public ulong SteamId;
        public string Address = "";
        public int ListenPort;
        public byte Colour;
        public float PoseAt, LastHeard;
        public int SceneHash;
        public Vector3 Position, ShownPosition, Feet;
        public float FeetMovedAt = -100;
        public Quaternion Rotation = Quaternion.identity, ShownRotation = Quaternion.identity;
        public AnimFrame Anim = new AnimFrame();
        public BoneFrame Bones = new BoneFrame();
        public float BonesAt = -100, FloorCheckAt, NoFloorSince = -1;
        public HashSet<string> Visible;
        public RemoteAvatar Avatar;
    }

    // Unity drives this through TogetherBehaviour (the only class injected into IL2CPP). Keeping the runtime a plain
    // C# class avoids Il2CppInterop trying to expose every helper method to the native side.
    public sealed partial class TogetherRuntime
    {
        public const string ModVersion = "0.3.0";
        public static TogetherRuntime Instance;
        public static Action<string> Log = delegate { };
        public ITransport Network;
        public bool Hosting { get { return Network != null && Network.Hosting; } }
        public bool Active { get { return Network != null; } }
        public bool IsClient { get { return Network != null && !Network.Hosting; } }
        public bool Connected { get { return Hosting || (IsClient && self > 0); } }
        public string Name = "Player", Address = "127.0.0.1";
        public byte Colour;
        public int Port = 27030;
        public string Status = "F5: host a game or join a friend.";
        readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
        readonly List<string> messages = new List<string>();
        readonly List<float> messageTimes = new List<float>();
        string gameHash = "";
        int self = -1;
        bool show;
        string portText = "27030";
        string autoRole;
        // -mt-second: a second copy of the game on this PC (scripts/second-instance.ps1). It shares the save folder
        // with the main copy, so it never backs up, mirrors or restores saves.
        public static bool SecondCopy;
        float secondJoinAt;
        float nextPose, nextPing;
        GUIStyle boxStyle, labelStyle, tagStyle;
        public static readonly Color[] Colours = { new Color(0.95f, 0.35f, 0.3f), new Color(0.35f, 0.75f, 1f), new Color(0.45f, 0.95f, 0.45f), new Color(1f, 0.85f, 0.3f), new Color(0.85f, 0.5f, 1f), new Color(1f, 0.6f, 0.25f) };
        public static string Root { get { return Directory.GetParent(Application.dataPath).FullName; } }
        public int SelfId { get { return Hosting ? 0 : self; } }
        public IEnumerable<Peer> Peers { get { return peers.Values; } }

        public static void Boot(GameObject host)
        {
            if (Instance != null) return;
            SteamTransport.Clock = () => Time.realtimeSinceStartup;
            Instance = new TogetherRuntime();
            Instance.Awake();
        }

        void Awake()
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream file = File.OpenRead(Path.Combine(Root, "GameAssembly.dll")))
                    gameHash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception ex) { Log("Could not hash the game assembly: " + ex.Message); }
            LoadSettings();
            RestoreSaves();
            string[] args = Environment.GetCommandLineArgs();
            InitLab(args);
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-mt-host") autoRole = "host";
                if (args[i] == "-mt-join" && i + 1 < args.Length) { autoRole = "join"; Address = args[++i]; }
                if (args[i] == "-mt-name" && i + 1 < args.Length) Name = CleanName(args[++i]);
                if (args[i] == "-mt-second") SecondCopy = true;
                if (args[i] == "-mt-port" && i + 1 < args.Length) portText = args[++i];
                // Steam adds this when a friend accepts an invite or clicks Join Game while MiSide is closed.
                if (args[i] == "+mt_join" && i + 1 < args.Length) ulong.TryParse(args[++i], out launchJoin);
            }
            Application.runInBackground = true;
            Log("MiSide Together " + ModVersion + " ready. F5 opens the multiplayer panel. Game SHA256 " + gameHash);
        }

        bool started;

        public void Tell(string message)
        {
            Status = message;
            messages.Add(message); messageTimes.Add(Time.realtimeSinceStartup);
            if (messages.Count > 6) { messages.RemoveAt(0); messageTimes.RemoveAt(0); }
            Log(message);
        }

        static string SettingsPath { get { return Path.Combine(Root, "BepInEx/config/MiSideTogether.ini"); } }

        void LoadSettings()
        {
            Name = CleanName(Environment.UserName);
            if (!File.Exists(SettingsPath)) return;
            foreach (string line in File.ReadAllLines(SettingsPath))
            {
                int split = line.IndexOf('='); if (split < 0) continue;
                string key = line.Substring(0, split), value = line.Substring(split + 1);
                if (key == "Name") Name = CleanName(value);
                if (key == "Address") Address = value.Substring(0, Math.Min(128, value.Length)).Trim();
                if (key == "Port") portText = value.Trim();
                int colour; if (key == "Colour" && int.TryParse(value, out colour)) Colour = (byte)Mathf.Clamp(colour, 0, Colours.Length - 1);
            }
        }

        void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllLines(SettingsPath, new[] { "Name=" + Name, "Address=" + Address, "Port=" + portText, "Colour=" + Colour });
            }
            catch (Exception ex) { Log("Could not save settings: " + ex.Message); }
        }

        public static string CleanName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Player";
            System.Text.StringBuilder clean = new System.Text.StringBuilder();
            foreach (char c in value) if (!char.IsControl(c) && clean.Length < 16) clean.Append(c);
            string result = clean.ToString().Trim();
            return result.Length == 0 ? "Player" : result;
        }

        bool ParsePort()
        {
            int port;
            if (!int.TryParse(portText, out port) || port < 1024 || port > 65535) { Tell("Port must be between 1024 and 65535."); return false; }
            Port = port; return true;
        }

        public void StartHost()
        {
            if (Active || !ParsePort()) return;
            SaveSettings();
            leftOnPurpose = false;
            HostTransport host = new HostTransport();
            string how = "";
            try { Transport tcp = new Transport(); tcp.Host(Port, false); host.Add(tcp, false); how = "port " + Port; }
            catch (Exception ex) { Log("LAN hosting unavailable: " + ex.Message); }
            // Steam friends join through Steam's relay: no ports or addresses needed.
            try { if (Steam.Ready) { SteamTransport steam = new SteamTransport(); steam.Host(); host.Add(steam, true); how += (how.Length > 0 ? " and " : "") + "Steam"; } else Log("Steam hosting unavailable: " + Steam.Problem); }
            catch (Exception ex) { Log("Steam hosting unavailable: " + ex.Message); }
            if (how.Length == 0) { host.Dispose(); Tell("Could not host: no network available."); return; }
            Network = host;
            ResetSession();
            UpdatePresence(true);
            Tell("Hosting on " + how + ". Friends join from the Multiplayer menu. You lead: they follow your chapter.");
        }

        public void StartJoin()
        {
            if (Active || !ParsePort()) return;
            SaveSettings();
            leftOnPurpose = false;
            Transport tcp = new Transport();
            Network = tcp;
            ResetSession();
            tcp.Join(Address, Port);
            Tell("Connecting to " + Address + ":" + Port + "...");
        }

        // Join a Steam friend's game (listed in the Multiplayer menu).
        public void StartJoinSteam(ulong host, string name)
        {
            if (Active) return;
            try
            {
                SteamTransport steam = new SteamTransport();
                steam.Join(host);
                Network = steam;
                ResetSession();
                Tell("Joining " + name + " through Steam...");
            }
            catch (Exception ex) { Network = null; Tell("Could not join through Steam: " + ex.Message); }
        }

        float nextPresence;
        ulong launchJoin;

        void SteamJoinFromLaunch()
        {
            if (launchJoin == 0 || Active || ActiveSceneName != "SceneMenu" || !Steam.Ready) return;
            ulong host = launchJoin;
            launchJoin = 0;
            StartJoinSteam(host, "your friend");
        }

        // Steam rich presence: what friends see in their Multiplayer menu (protocol|version|name|players|scene).
        void UpdatePresence(bool force)
        {
            float now = Time.realtimeSinceStartup;
            if (!force && now < nextPresence) return;
            nextPresence = now + 5;
            if (!Steam.Ready) return;
            if (!Hosting) return;
            Steam.SetPresence("mt_host", Wire.Version + "|" + ModVersion + "|" + CleanName(Name) + "|" + (AcceptedCount + 1) + "|" + hostScene);
            // "Join Game" in the Steam friends list (and invites) start MiSide with this, see SteamJoinFromLaunch.
            Steam.SetPresence("connect", "+mt_join " + Steam.Self);
            Steam.SetPresence("status", "Playing MiSide Together (" + (AcceptedCount + 1) + "/4)");
        }

        public void Shutdown()
        {
            if (Network != null)
            {
                try { if (Connected) Broadcast(Wire.Packet(Op.Leave, null)); } catch { }
                Network.Dispose();
            }
            Network = null;
            // A migration keeps the co-op saves in place; a real leave puts the player's own saves back.
            if (!migrating) RestoreSaves();
            foreach (Peer peer in peers.Values) DestroyAvatar(peer);
            peers.Clear();
            self = -1;
            ResetSession();
        }

        void ResetSession()
        {
            ResetSceneSync();
            ResetWorldSync();
        }

        public void OnApplicationQuit() { Shutdown(); ShutdownLanDiscovery(); }

        public void Update()
        {
            LabFrameStart();
            try { UpdateCore(); }
            finally { LabFrameEnd(true); }
        }

        void UpdateCore()
        {
            if (!started) { started = true; if (autoRole == "host") StartHost(); if (autoRole == "join" && !LateLabJoiner) StartJoin(); }
            // The second copy (-mt-join) keeps trying until the main game hosts, unless the player left on purpose.
            if (SecondCopy && autoRole == "join" && !Active && !leftOnPurpose && Time.realtimeSinceStartup >= secondJoinAt) { secondJoinAt = Time.realtimeSinceStartup + 3; StartJoin(); }
            if (Input.GetKeyDown(KeyCode.F5)) SetPanel(!show);
            if (show) HoldPanelLocks();
            if (Network != null) Step("network", PumpNetwork);
            float now = Time.realtimeSinceStartup;
            if (Connected && now >= nextPose) { nextPose = now + 1f / 15f; Step("pose", SendPose); }
            if (Connected && now >= nextPing)
            {
                nextPing = now + 2f;
                Broadcast(Wire.Packet(Op.Ping, null));
            }
            Step("menu", NativeMenuTick);
            Step("lan", UpdateLanDiscovery);
            Step("migrate", () => MigrationTick(Time.realtimeSinceStartup));
            Step("steam-join", SteamJoinFromLaunch);
            if (Hosting) Step("presence", () => UpdatePresence(false));
            // Steam can be ready only after hosting started (game still booting): add it to the running host then.
            HostTransport ht = Network as HostTransport;
            if (ht != null && !ht.Steam && Time.frameCount % 120 == 0 && Steam.Ready) { SteamTransport st = new SteamTransport(); st.Host(); ht.Add(st, true); UpdatePresence(true); Log("Steam hosting added"); }
            if (Hosting) Step("saves", HostSaveScan);
            Step("scene", UpdateSceneSync);
            if (Connected) Step("world", UpdateWorldSync);
            Step("avatars", UpdateAvatars);
            // Lab diagnostics are not the mod's cost: the frame stopwatch pauses for them (they stay in steps=).
            if (LabMode)
            {
                labWatch.Stop();
                Step("lab", LabTick);
                Step("lab-state", () => StateTick(Time.realtimeSinceStartup));
                Step("lab-anim", AnimEventTick);
                labWatch.Start();
            }
        }

        public void LateUpdate()
        {
            LabFrameStart();
            if (Connected) Step("world-late", LateUpdateWorldSync);
            Step("avatars-late", LateUpdateAvatars);
            Step("spectate", SpectateTick);
            LabFrameEnd(false);
        }

        readonly Dictionary<string, float> stepErrorAt = new Dictionary<string, float>();

        // Each subsystem fails on its own: one broken feature in one scene must not stop the others.
        readonly Dictionary<string, long> stepTicks = new Dictionary<string, long>();
        readonly Dictionary<string, long> stepWorst = new Dictionary<string, long>();   // the slowest single call per step

        // The steps whose single slowest call this window took over 2 ms: where the frame spikes come from.
        public string SpikeReport()
        {
            List<KeyValuePair<string, long>> list = new List<KeyValuePair<string, long>>(stepWorst);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            string s = "";
            foreach (KeyValuePair<string, long> e in list)
            {
                double ms = e.Value * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (ms < 2 || s.Length > 120) break;
                s += e.Key + ":" + ms.ToString("0.0") + ",";
            }
            stepWorst.Clear();
            return s.Length > 0 ? s : "-";
        }
        readonly System.Diagnostics.Stopwatch stepWatch = new System.Diagnostics.Stopwatch();

        public string StepReport()
        {
            List<KeyValuePair<string, long>> list = new List<KeyValuePair<string, long>>(stepTicks);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            string s = "";
            for (int i = 0; i < list.Count && i < 7; i++) s += list[i].Key + ":" + (list[i].Value * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("0") + "ms,";
            stepTicks.Clear();
            return s.TrimEnd(',');
        }

        void Step(string name, Action action)
        {
            long before = LabMode ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            try { action(); }
            catch (Exception ex)
            {
                float last;
                if (stepErrorAt.TryGetValue(name, out last) && Time.realtimeSinceStartup - last < 5) return;
                stepErrorAt[name] = Time.realtimeSinceStartup;
                Log("MT-MODERROR " + name + ": " + ex);
            }
            finally
            {
                if (LabMode)
                {
                    long spent = System.Diagnostics.Stopwatch.GetTimestamp() - before; long total, worst;
                    stepTicks.TryGetValue(name, out total); stepTicks[name] = total + spent;
                    if (!stepWorst.TryGetValue(name, out worst) || spent > worst) stepWorst[name] = spent;
                }
            }
        }

        // --- Panel -----------------------------------------------------------------------------
        bool panelHeldPlayer, panelCursorVisible;
        CursorLockMode panelCursorLock;

        void SetPanel(bool open)
        {
            if (show == open) return;
            show = open;
            PlayerMove player = LocalPlayer;
            if (open)
            {
                panelCursorVisible = Cursor.visible; panelCursorLock = Cursor.lockState;
                panelHeldPlayer = player != null && !player.stopMouseMove && !player.dontMove;
            }
            else
            {
                if (panelHeldPlayer && player != null) { player.stopMouseMove = false; player.dontMove = false; }
                panelHeldPlayer = false;
                Cursor.visible = panelCursorVisible; Cursor.lockState = panelCursorLock;
            }
        }

        // The game re-locks the cursor every frame, so the panel re-asserts its own state while open.
        void HoldPanelLocks()
        {
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
            PlayerMove player = LocalPlayer;
            // Typing a name or address must not also walk the player around.
            if (panelHeldPlayer && player != null) { player.stopMouseMove = true; player.dontMove = true; }
        }

        void Styles()
        {
            if (boxStyle != null) return;
            boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.UpperLeft };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            tagStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }

        public void OnGUI()
        {
            Styles();
            DrawNameTags(tagStyle);
            DrawHud();
            if (!show) return;
            float scale = Mathf.Max(1f, Screen.height / 900f);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            try { DrawPanel(scale); }
            finally { GUI.enabled = true; GUI.matrix = old; }
            panelFrames++;
        }

        readonly Ui ui = new Ui();
        public int panelFrames;

        // Plain GUI calls only: GUILayout is stripped from MiSide's build (see Ui).
        void DrawPanel(float scale)
        {
            ui.Label = labelStyle; ui.Title = tagStyle;
            ui.Begin(new Rect(30, 30, 430, Active ? 560 : 720), scale, "MiSide Together " + ModVersion + "  (F5 closes)");
            ui.Text("Name");
            string name = ui.Field("name", Name, 16);
            if (name != Name) Name = CleanName(name);
            ui.Text("Colour");
            for (int i = 0; i < Colours.Length; i++)
            {
                Color previous = GUI.color; GUI.color = Colours[i];
                if (ui.Button(new Rect(ui.X + i * 44, ui.Y, 40, 26), i == Colour ? "X" : "  ")) { Colour = (byte)i; if (Connected) SendHello(); }
                GUI.color = previous;
            }
            ui.Space(32);
            GUI.enabled = !Active;
            ui.Text("Host address (for joining)");
            Address = ui.Field("address", Address, 128);
            ui.Text("Port");
            portText = ui.Field("port", portText, 5);
            float half = (ui.W - 8) / 2;
            if (ui.Button(new Rect(ui.X, ui.Y, half, 32), "Host")) StartHost();
            if (ui.Button(new Rect(ui.X + half + 8, ui.Y, half, 32), "Join")) StartJoin();
            ui.Space(38);
            GUI.enabled = Active;
            if (ui.Button(Hosting ? "Stop hosting" : "Leave", 28)) { leftOnPurpose = true; Shutdown(); Tell("Disconnected."); }
            GUI.enabled = true;
            ui.Space(4);
            ui.Text(Status);
            if (Connected)
            {
                ui.Text("Players:");
                ui.Text("- " + Name + (Hosting ? " (host, you)" : " (you)"));
                foreach (Peer peer in peers.Values) if (peer.Accepted) ui.Text("- " + peer.Name + (peer.Id == 0 ? " (host)" : "") + (peer.SceneHash != LocalSceneHash ? " - in another scene" : ""));
                if (IsClient && ui.Button("Go to the host", 26)) TeleportToHost();
                // Someone stuck or lost: everyone in the host's chapter comes to the host (same safe placement).
                if (Hosting && AcceptedCount > 0 && ui.Button("Bring everyone here", 26)) { Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "gather"))); Tell("Bringing everyone to you."); }
            }
            if (!Active) DrawServerList();
            ui.Text("The host leads the story: chapters, cutscenes and Mita follow the host. Anything any player presses or triggers happens for everyone.");
        }

        void DrawHud()
        {
            if (!Active) return;
            float now = Time.realtimeSinceStartup;
            float y = 8;
            int waiting = 0; foreach (Peer p in peers.Values) if (p.Waiting) waiting++;
            string header = "MiSide Together | " + (Hosting ? "host" : Connected ? "client" : "connecting") + " | " + (1 + AcceptedCount) + " player" + (AcceptedCount == 0 ? "" : "s")
                + (waiting > 0 ? " (" + waiting + " joining next chapter)" : "");
            GUI.Label(new Rect(Screen.width - 420, y, 410, 24), header, labelStyle);
            y += 22;
            string wait = WaitingText();
            if (wait != null) GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height / 2 + 40, 500, 30), wait, tagStyle);
            for (int i = 0; i < messages.Count; i++)
            {
                if (now - messageTimes[i] > 8) continue;
                GUI.Label(new Rect(Screen.width - 420, y, 410, 40), messages[i], labelStyle); y += 22;
            }
        }

        int AcceptedCount { get { int n = 0; foreach (Peer p in peers.Values) if (p.Accepted) n++; return n; } }

        // --- Game access ---------------------------------------------------------------------------
        PlayerMove cachedPlayer;
        int cachedPlayerFrame = -1;

        public PlayerMove LocalPlayer
        {
            get
            {
                if (cachedPlayer != null && cachedPlayer.gameObject.activeInHierarchy) return cachedPlayer;
                // FindObjectOfType is expensive; re-query at most twice a second while no player exists (menus).
                if (Time.frameCount - cachedPlayerFrame < 30 && cachedPlayerFrame >= 0) return null;
                cachedPlayerFrame = Time.frameCount;
                cachedPlayer = Object.FindObjectOfType<PlayerMove>();
                return cachedPlayer;
            }
        }

        public static string ActiveSceneName { get { return SceneManager.GetActiveScene().name; } }
        public static int LocalSceneHash { get { return Wire.Hash(ActiveSceneName); } }
    }

    public sealed class TogetherBehaviour : MonoBehaviour
    {
        public TogetherBehaviour(IntPtr ptr) : base(ptr) { }

        public void Awake() { TogetherRuntime.Boot(gameObject); }
        public void Update() { Guard("Update", () => TogetherRuntime.Instance.Update()); }
        public void LateUpdate() { Guard("LateUpdate", () => TogetherRuntime.Instance.LateUpdate()); }
        public void OnGUI() { Guard("OnGUI", () => TogetherRuntime.Instance.OnGUI()); }
        public void OnApplicationQuit() { Guard("Quit", () => TogetherRuntime.Instance.OnApplicationQuit()); }

        static readonly Dictionary<string, float> lastError = new Dictionary<string, float>();
        public static int GuiErrors;

        // An exception thrown back into IL2CPP can take the game down; log it (rate limited) and keep going.
        static void Guard(string where, Action action)
        {
            if (TogetherRuntime.Instance == null) return;
            try { action(); }
            catch (Exception ex)
            {
                if (where == "OnGUI") GuiErrors++;
                float last;
                if (lastError.TryGetValue(where, out last) && Time.realtimeSinceStartup - last < 5) return;
                lastError[where] = Time.realtimeSinceStartup;
                TogetherRuntime.Log("MT-MODERROR " + where + ": " + ex);
            }
        }
    }
}
