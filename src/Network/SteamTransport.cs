using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MiSideTogether
{
    // The interface both transports share: the TCP star (LAN / VPN / port forward) and Steam P2P (friends, no setup).
    public interface ITransport : IDisposable
    {
        bool Hosting { get; }
        int Port { get; }
        bool Poll(out NetworkEvent e);
        void Send(int id, byte[] packet);
        void SendAndClose(int id, byte[] packet);
        void Drop(int id);
        string Address(int id);
    }

    // Steam's own API, called straight from steam_api64.dll (the game already initialised Steam). The game's
    // Steamworks.NET wrapper is not used: MiSide's IL2CPP build strips parts of it.
    public static class Steam
    {
        const string Lib = "steam_api64";
        public const uint AppId = 2527500;
        const int FriendFlagImmediate = 4;

        [DllImport(Lib)] static extern IntPtr SteamAPI_SteamNetworking_v006();
        [DllImport(Lib)] static extern IntPtr SteamAPI_SteamFriends_v017();
        [DllImport(Lib)] static extern IntPtr SteamAPI_SteamUser_v021();
        [DllImport(Lib)] static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);
        [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamNetworking_SendP2PPacket(IntPtr self, ulong remote, byte[] data, uint size, int sendType, int channel);
        [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamNetworking_IsP2PPacketAvailable(IntPtr self, out uint size, int channel);
        [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamNetworking_ReadP2PPacket(IntPtr self, byte[] dest, uint capacity, out uint size, out ulong remote, int channel);
        [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(IntPtr self, ulong remote);
        [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamNetworking_CloseP2PSessionWithUser(IntPtr self, ulong remote);
        [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamNetworking_AllowP2PPacketRelay(IntPtr self, [MarshalAs(UnmanagedType.I1)] bool allow);
        [DllImport(Lib)] static extern int SteamAPI_ISteamFriends_GetFriendCount(IntPtr self, int flags);
        [DllImport(Lib)] static extern ulong SteamAPI_ISteamFriends_GetFriendByIndex(IntPtr self, int index, int flags);
        [DllImport(Lib)] static extern IntPtr SteamAPI_ISteamFriends_GetFriendPersonaName(IntPtr self, ulong friend);
        [DllImport(Lib)] static extern IntPtr SteamAPI_ISteamFriends_GetPersonaName(IntPtr self);
        [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamFriends_GetFriendGamePlayed(IntPtr self, ulong friend, out FriendGameInfo info);
        [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamFriends_SetRichPresence(IntPtr self, byte[] key, byte[] value);
        [DllImport(Lib)] static extern IntPtr SteamAPI_ISteamFriends_GetFriendRichPresence(IntPtr self, ulong friend, byte[] key);
        [DllImport(Lib)] static extern void SteamAPI_ISteamFriends_RequestFriendRichPresence(IntPtr self, ulong friend);
        [DllImport(Lib)] static extern void SteamAPI_ISteamFriends_ActivateGameOverlayInviteDialogConnectString(IntPtr self, byte[] connect);

        // FriendGameInfo_t (Steamworks packs callback structs to 8 bytes on Windows).
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        struct FriendGameInfo { public ulong GameId; public uint GameIp; public ushort GamePort, QueryPort; public ulong Lobby; }

        static IntPtr networking, friends, user;
        // Lab only (-mt-steam-mock): every call below goes to the mock instead of steam_api64.
        public static SteamMock Mock;
        static bool resolverSet;
        static float retryAt;
        public static string Problem = "";

        public static bool Ready
        {
            get
            {
                if (Mock != null || (networking != IntPtr.Zero && friends != IntPtr.Zero && user != IntPtr.Zero)) return true;
                // Steam may come up after the mod starts: try again every few seconds instead of giving up.
                float now = SteamTransport.Clock();
                if (now < retryAt) return false;
                retryAt = now + 5;
                try
                {
                    if (!resolverSet)
                    {
                        resolverSet = true;
                        // steam_api64.dll lives in MiSideFull_Data/Plugins/x86_64, outside the default DLL search path.
                        NativeLibrary.SetDllImportResolver(typeof(Steam).Assembly, (name, assembly, path) =>
                        {
                            if (name != Lib) return IntPtr.Zero;
                            string dll = System.IO.Path.Combine(UnityEngine.Application.dataPath, "Plugins", "x86_64", "steam_api64.dll");
                            return System.IO.File.Exists(dll) ? NativeLibrary.Load(dll) : IntPtr.Zero;
                        });
                    }
                    networking = SteamAPI_SteamNetworking_v006(); friends = SteamAPI_SteamFriends_v017(); user = SteamAPI_SteamUser_v021();
                    if (networking == IntPtr.Zero || friends == IntPtr.Zero || user == IntPtr.Zero) { Problem = "Steam is not running for this game"; return false; }
                    SteamAPI_ISteamNetworking_AllowP2PPacketRelay(networking, true);
                    return true;
                }
                catch (Exception ex) { Problem = "Steam API unavailable: " + ex.Message; networking = friends = user = IntPtr.Zero; return false; }
            }
        }

        public static ulong Self { get { return Mock != null ? Mock.Self : Ready ? SteamAPI_ISteamUser_GetSteamID(user) : 0; } }
        public static string SelfName { get { return Mock != null ? "Mock " + (Mock.Self - SteamMock.Base) : Ready ? Utf8(SteamAPI_ISteamFriends_GetPersonaName(friends)) : ""; } }

        static string Utf8(IntPtr p) { return p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? ""; }
        static byte[] Z(string s) { return Encoding.UTF8.GetBytes((s ?? "") + "\0"); }

        public static void SetPresence(string key, string value) { if (Mock != null) Mock.SetPresence(key, value); else if (Ready) SteamAPI_ISteamFriends_SetRichPresence(friends, Z(key), Z(value)); }

        // Steam's own "invite a friend" overlay; accepting starts MiSide with the connect string (+mt_join <host>).
        public static void InviteOverlay(string connect) { if (Mock == null && Ready) SteamAPI_ISteamFriends_ActivateGameOverlayInviteDialogConnectString(friends, Z(connect)); }

        public sealed class Friend { public ulong Id; public string Name, Hosting; }

        // Friends currently in MiSide, with what they advertise under "mt_host" (empty when not hosting).
        public static List<Friend> FriendsInGame()
        {
            List<Friend> list = new List<Friend>();
            if (Mock != null)
            {
                foreach (ulong id in Mock.Friends()) list.Add(new Friend { Id = id, Name = "Mock " + (id - SteamMock.Base), Hosting = Mock.FriendPresence(id, "mt_host") });
                return list;
            }
            if (!Ready) return list;
            int count = SteamAPI_ISteamFriends_GetFriendCount(friends, FriendFlagImmediate);
            for (int i = 0; i < count && i < 500; i++)
            {
                ulong id = SteamAPI_ISteamFriends_GetFriendByIndex(friends, i, FriendFlagImmediate);
                FriendGameInfo info;
                if (!SteamAPI_ISteamFriends_GetFriendGamePlayed(friends, id, out info) || (info.GameId & 0xFFFFFF) != AppId) continue;
                SteamAPI_ISteamFriends_RequestFriendRichPresence(friends, id);
                list.Add(new Friend { Id = id, Name = Utf8(SteamAPI_ISteamFriends_GetFriendPersonaName(friends, id)), Hosting = Utf8(SteamAPI_ISteamFriends_GetFriendRichPresence(friends, id, Z("mt_host"))) });
            }
            return list;
        }

        public static string FriendPresence(ulong id, string key) { return Mock != null ? Mock.FriendPresence(id, key) : Ready ? Utf8(SteamAPI_ISteamFriends_GetFriendRichPresence(friends, id, Z(key))) : ""; }
        public static bool Send(ulong to, byte[] data, bool reliable) { if (Mock != null) return Mock.Send(to, data); return Ready && SteamAPI_ISteamNetworking_SendP2PPacket(networking, to, data, (uint)data.Length, reliable ? 2 : 1, 0); }
        public static bool Available(out uint size) { size = 0; if (Mock != null) return Mock.Available(out size); return Ready && SteamAPI_ISteamNetworking_IsP2PPacketAvailable(networking, out size, 0); }
        public static bool Read(byte[] buffer, out uint size, out ulong from) { size = 0; from = 0; if (Mock != null) return Mock.Read(buffer, out size, out from); return Ready && SteamAPI_ISteamNetworking_ReadP2PPacket(networking, buffer, (uint)buffer.Length, out size, out from, 0); }
        public static void Accept(ulong id) { if (Mock != null) Mock.Accept(id); else if (Ready) SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(networking, id); }
        public static void Close(ulong id) { if (Mock != null) Mock.Close(id); else if (Ready) SteamAPI_ISteamNetworking_CloseP2PSessionWithUser(networking, id); }
    }

    // Steam P2P with the same events and peer ids as the TCP transport (host = 0, clients 1..3). Polled on the main
    // thread. Steam relays the traffic, so no ports, VPN or IP addresses are involved.
    public sealed class SteamTransport : ITransport
    {
        readonly Queue<NetworkEvent> events = new Queue<NetworkEvent>();
        readonly Dictionary<ulong, int> ids = new Dictionary<ulong, int>();
        readonly Dictionary<int, ulong> steamIds = new Dictionary<int, ulong>();
        readonly Dictionary<int, float> heardAt = new Dictionary<int, float>();
        // A reliable send fails when Steam's send buffer is full: the packet waits here and goes out, in order, on the
        // next poll (never dropped: every packet is a story event or state the other side needs).
        readonly Dictionary<int, Queue<byte[]>> unsent = new Dictionary<int, Queue<byte[]>>();
        public static int SendRetries;
        readonly byte[] buffer = new byte[Transport.MaxFrame];
        ulong hostId;
        float nextAccept;
        bool disposed;
        public bool Hosting { get; private set; }
        public int Port { get { return 0; } }
        public static Func<float> Clock = () => 0;

        public void Host()
        {
            if (!Steam.Ready) throw new InvalidOperationException(Steam.Problem);
            Hosting = true;
        }

        public void Join(ulong host)
        {
            if (!Steam.Ready) throw new InvalidOperationException(Steam.Problem);
            hostId = host;
            ids[host] = 0; steamIds[0] = host; heardAt[0] = Clock();
            Steam.SetPresence("mt_join", host.ToString());
            Steam.Accept(host);
            // Packets wait in Steam until the session is up, so the handshake can start straight away.
            events.Enqueue(new NetworkEvent { Peer = 0, Kind = "connected" });
        }

        public bool Poll(out NetworkEvent e)
        {
            if (!disposed) Pump();
            if (events.Count > 0) { e = events.Dequeue(); return true; }
            e = null; return false;
        }

        void Pump()
        {
            float now = Clock();
            if (unsent.Count > 0) FlushUnsent();
            if (Hosting && now >= nextAccept)
            {
                // No callbacks from here: accept sessions from friends in MiSide who asked to join this host.
                nextAccept = now + 2;
                string me = Steam.Self.ToString();
                foreach (Steam.Friend f in Steam.FriendsInGame())
                    if (Steam.FriendPresence(f.Id, "mt_join") == me) Steam.Accept(f.Id);
            }
            for (int budget = 0; budget < 256; budget++)
            {
                uint size; ulong from;
                if (!Steam.Available(out size) || !Steam.Read(buffer, out size, out from)) break;
                if (size == 0 || size > Transport.MaxFrame) continue;
                int id;
                if (!ids.TryGetValue(from, out id))
                {
                    if (!Hosting) continue;   // a client only talks to its host
                    id = 1;
                    while (steamIds.ContainsKey(id)) id++;
                    if (id > Transport.MaxPeers) { Steam.Close(from); continue; }
                    ids[from] = id; steamIds[id] = from;
                    events.Enqueue(new NetworkEvent { Peer = id, Kind = "connected" });
                }
                heardAt[id] = now;
                byte[] packet = new byte[size];
                Buffer.BlockCopy(buffer, 0, packet, 0, (int)size);
                events.Enqueue(new NetworkEvent { Peer = id, Kind = "packet", Data = packet });
            }
            // Peers send a ping every 2 s; 20 s of silence is a lost connection.
            List<int> silent = null;
            foreach (KeyValuePair<int, float> pair in heardAt) if (now - pair.Value > 20) (silent ?? (silent = new List<int>())).Add(pair.Key);
            if (silent != null) foreach (int id in silent) Drop(id);
        }

        public void Send(int id, byte[] packet)
        {
            ulong to;
            if (packet == null || packet.Length == 0 || packet.Length > Transport.MaxFrame) throw new ArgumentException("Invalid packet size.");
            if (!steamIds.TryGetValue(id, out to)) return;
            if (unsent.TryGetValue(id, out Queue<byte[]> waiting) && waiting.Count > 0) { waiting.Enqueue(packet); return; }
            if (Steam.Send(to, packet, true)) return;
            SendRetries++;
            if (waiting == null) unsent[id] = waiting = new Queue<byte[]>();
            waiting.Enqueue(packet);
        }

        void FlushUnsent()
        {
            foreach (KeyValuePair<int, Queue<byte[]>> pair in unsent)
            {
                if (!steamIds.TryGetValue(pair.Key, out ulong to)) { pair.Value.Clear(); continue; }
                while (pair.Value.Count > 0 && Steam.Send(to, pair.Value.Peek(), true)) pair.Value.Dequeue();
            }
        }

        public void SendAndClose(int id, byte[] packet) { Send(id, packet); Drop(id); }
        public string Address(int id) { return ""; }

        public void Drop(int id)
        {
            ulong to;
            if (!steamIds.TryGetValue(id, out to)) return;
            steamIds.Remove(id); ids.Remove(to); heardAt.Remove(id); unsent.Remove(id);
            Steam.Close(to);
            events.Enqueue(new NetworkEvent { Peer = id, Kind = "disconnected" });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (ulong to in steamIds.Values) Steam.Close(to);
            steamIds.Clear(); ids.Clear(); heardAt.Clear(); events.Clear();
            Steam.SetPresence("mt_join", ""); Steam.SetPresence("mt_host", ""); Steam.SetPresence("connect", ""); Steam.SetPresence("status", "");
        }
    }
}
