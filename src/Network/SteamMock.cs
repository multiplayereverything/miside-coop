using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MiSideTogether
{
    // Lab-only stand-in for Steam (-mt-steam-mock <slot>), so the Steam transport is tested with one Steam account
    // (pattern from Co-op Anything, networking-steam-spacewar: "mock Steamworks.NET, not the DLL"). Every lab copy is a
    // fake account 900000000 + slot, and all of them are friends playing MiSide. What it copies from real Steam:
    //   rich presence     each copy's keys in private\steam-mock\<id>.txt, read by the others
    //   P2P packets       localhost TCP, port 27200 + slot, reliable and in order like k_EP2PSendReliable
    //   session accept    the first packets from a stranger are held until AcceptP2PSessionWithUser
    //   send failures     -mt-steam-fail N makes every Nth send fail, as a full send buffer does
    public sealed class SteamMock
    {
        public const ulong Base = 900000000;
        const int BasePort = 27200;
        public readonly ulong Self;
        readonly string folder;
        readonly int failEvery;
        int sends;
        readonly Dictionary<string, string> presence = new Dictionary<string, string>();
        readonly ConcurrentQueue<KeyValuePair<ulong, byte[]>> incoming = new ConcurrentQueue<KeyValuePair<ulong, byte[]>>();
        readonly Dictionary<ulong, Queue<byte[]>> held = new Dictionary<ulong, Queue<byte[]>>();
        readonly Queue<KeyValuePair<ulong, byte[]>> ready = new Queue<KeyValuePair<ulong, byte[]>>();
        readonly HashSet<ulong> accepted = new HashSet<ulong>();
        readonly Dictionary<ulong, NetworkStream> outgoing = new Dictionary<ulong, NetworkStream>();
        readonly TcpListener listener;
        public int Failed;

        public SteamMock(int slot, string folder, int failEvery)
        {
            Self = Base + (ulong)slot;
            this.folder = folder;
            this.failEvery = failEvery;
            Directory.CreateDirectory(folder);
            WritePresence();
            listener = new TcpListener(IPAddress.Loopback, BasePort + slot);
            listener.Start();
            new Thread(AcceptLoop) { IsBackground = true, Name = "MiSideTogether steam mock" }.Start();
        }

        void AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); } catch { return; }
                new Thread(() => ReadLoop(client)) { IsBackground = true }.Start();
            }
        }

        // Each frame: sender id (8 bytes), length (4 bytes), data.
        void ReadLoop(TcpClient client)
        {
            try
            {
                BinaryReader reader = new BinaryReader(client.GetStream());
                while (true)
                {
                    ulong from = reader.ReadUInt64();
                    int size = reader.ReadInt32();
                    incoming.Enqueue(new KeyValuePair<ulong, byte[]>(from, reader.ReadBytes(size)));
                }
            }
            catch { client.Close(); }
        }

        // Main thread: move arrivals to ready (accepted senders) or held (strangers).
        void Sort()
        {
            while (incoming.TryDequeue(out KeyValuePair<ulong, byte[]> p))
            {
                if (accepted.Contains(p.Key)) { ready.Enqueue(p); continue; }
                if (!held.TryGetValue(p.Key, out Queue<byte[]> q)) held[p.Key] = q = new Queue<byte[]>();
                q.Enqueue(p.Value);
            }
        }

        public bool Available(out uint size)
        {
            Sort();
            size = ready.Count > 0 ? (uint)ready.Peek().Value.Length : 0;
            return ready.Count > 0;
        }

        public bool Read(byte[] buffer, out uint size, out ulong from)
        {
            Sort();
            size = 0; from = 0;
            if (ready.Count == 0) return false;
            KeyValuePair<ulong, byte[]> p = ready.Dequeue();
            from = p.Key; size = (uint)p.Value.Length;
            Buffer.BlockCopy(p.Value, 0, buffer, 0, Math.Min(p.Value.Length, buffer.Length));
            return true;
        }

        public void Accept(ulong id)
        {
            Sort();
            if (!accepted.Add(id)) return;
            if (held.TryGetValue(id, out Queue<byte[]> q)) { while (q.Count > 0) ready.Enqueue(new KeyValuePair<ulong, byte[]>(id, q.Dequeue())); held.Remove(id); }
        }

        public void Close(ulong id)
        {
            accepted.Remove(id); held.Remove(id);
            if (outgoing.TryGetValue(id, out NetworkStream s)) { try { s.Close(); } catch { } outgoing.Remove(id); }
        }

        public bool Send(ulong to, byte[] data)
        {
            if (failEvery > 0 && ++sends % failEvery == 0) { Failed++; return false; }
            try
            {
                if (!outgoing.TryGetValue(to, out NetworkStream s))
                {
                    TcpClient c = new TcpClient(); c.NoDelay = true;
                    c.Connect(IPAddress.Loopback, BasePort + (int)(to - Base));
                    outgoing[to] = s = c.GetStream();
                }
                byte[] frame = new byte[12 + data.Length];
                BitConverter.GetBytes(Self).CopyTo(frame, 0);
                BitConverter.GetBytes(data.Length).CopyTo(frame, 8);
                data.CopyTo(frame, 12);
                s.Write(frame, 0, frame.Length);
                return true;
            }
            catch { Close(to); return false; }
        }

        public void SetPresence(string key, string value) { presence[key] = value ?? ""; WritePresence(); }

        void WritePresence()
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> pair in presence) lines.Add(pair.Key + "=" + pair.Value);
            try { File.WriteAllLines(Path.Combine(folder, Self + ".txt"), lines); } catch (IOException) { }
        }

        public string FriendPresence(ulong id, string key)
        {
            try
            {
                foreach (string line in File.ReadAllLines(Path.Combine(folder, id + ".txt")))
                    if (line.StartsWith(key + "=", StringComparison.Ordinal)) return line.Substring(key.Length + 1);
            }
            catch (IOException) { }
            return "";
        }

        // Every other lab copy of this run is a friend in MiSide.
        public List<ulong> Friends()
        {
            List<ulong> list = new List<ulong>();
            foreach (string file in Directory.GetFiles(folder, "*.txt"))
                if (ulong.TryParse(Path.GetFileNameWithoutExtension(file), out ulong id) && id != Self) list.Add(id);
            return list;
        }
    }
}
