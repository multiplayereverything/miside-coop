using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MiSideTogether
{
    public sealed class NetworkEvent
    {
        public int Peer;
        public string Kind;
        public byte[] Data;
        public string Message;
    }

    // Ported from Spooky Together. No Unity calls are permitted on the socket threads.
    public sealed class Transport : ITransport
    {
        public const int MaxFrame = 65536;
        // Lab only (-mt-lag <ms> <jitter>): every outgoing packet waits this long, to test under internet-like latency.
        public static int LabDelayMs, LabJitterMs;
        public const int MaxPeers = 3;
        readonly ConcurrentQueue<NetworkEvent> events = new ConcurrentQueue<NetworkEvent>();
        readonly Dictionary<int, Link> links = new Dictionary<int, Link>();
        readonly object gate = new object();
        TcpListener listener;
        volatile bool disposed;
        public bool Hosting { get; private set; }
        public int Port { get; private set; }

        void Emit(int peer, string kind, byte[] data, string message)
        {
            if (disposed) return;
            // Only data packets count against the cap; dropping a "disconnected" event would leave a ghost peer.
            if (kind == "packet" && events.Count >= 1024) { Drop(peer); return; }
            events.Enqueue(new NetworkEvent { Peer = peer, Kind = kind, Data = data, Message = message });
        }

        public bool Poll(out NetworkEvent e) { return events.TryDequeue(out e); }

        public void Host(int port, bool loopback)
        {
            if (listener != null || disposed) throw new InvalidOperationException("Transport already used.");
            Hosting = true;
            listener = new TcpListener(loopback ? IPAddress.Loopback : IPAddress.Any, port);
            listener.Start(4);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            StartThread(delegate
            {
                while (!disposed)
                {
                    try
                    {
                        TcpClient client = listener.AcceptTcpClient();
                        lock (gate)
                        {
                            int id = 1;
                            while (links.ContainsKey(id)) id++;
                            if (disposed || id > MaxPeers) { client.Close(); continue; }
                            Attach(id, client);
                        }
                    }
                    catch (Exception ex) { if (!disposed) Emit(-1, "error", null, ex.Message); break; }
                }
            });
        }

        public void Join(string address, int port)
        {
            if (disposed) throw new ObjectDisposedException("Transport");
            StartThread(delegate
            {
                TcpClient client = new TcpClient();
                try
                {
                    IAsyncResult attempt = client.BeginConnect(address, port, null, null);
                    using (attempt.AsyncWaitHandle)
                    {
                        if (!attempt.AsyncWaitHandle.WaitOne(5000)) throw new IOException("Connection timed out.");
                        client.EndConnect(attempt);
                    }
                    lock (gate)
                    {
                        if (disposed) { client.Close(); return; }
                        Attach(0, client);
                    }
                }
                catch (Exception ex) { client.Close(); Emit(0, "error", null, ex.Message); }
            });
        }

        void Attach(int id, TcpClient client)
        {
            client.NoDelay = true;
            // Scene loads can stall the main thread for a while; pings keep the link alive otherwise.
            client.ReceiveTimeout = 30000;
            client.SendTimeout = 10000;
            Link link = new Link(this, id, client);
            links.Add(id, link);
            Emit(id, "connected", null, null);
            link.Start();
        }

        public void Send(int id, byte[] packet)
        {
            if (packet == null || packet.Length == 0 || packet.Length > MaxFrame) throw new ArgumentException("Invalid packet size.");
            lock (gate) { Link link; if (links.TryGetValue(id, out link)) link.Send(packet); }
        }

        public void Drop(int id) { Drop(id, null); }

        // The peer's IP as this host sees it (for host migration: where the others can reach that player).
        public string Address(int id)
        {
            lock (gate) { Link link; return links.TryGetValue(id, out link) ? link.Remote : ""; }
        }

        public void SendAndClose(int id, byte[] packet)
        {
            if (packet == null || packet.Length < 1 || packet.Length > MaxFrame) throw new ArgumentException("Invalid final packet size.");
            lock (gate) { Link link; if (links.TryGetValue(id, out link)) link.SendAndClose(packet); }
        }

        void Drop(int id, Link expected)
        {
            lock (gate)
            {
                Link link;
                if (!links.TryGetValue(id, out link)) return;
                if (expected != null && link != expected) return;
                links.Remove(id);
                link.Close();
                Emit(id, "disconnected", null, null);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                if (listener != null) listener.Stop();
                foreach (Link link in links.Values) link.Close();
                links.Clear();
                NetworkEvent unused; while (events.TryDequeue(out unused)) { }
            }
        }

        static void StartThread(ThreadStart action) { new Thread(action) { IsBackground = true, Name = "MiSideTogether network" }.Start(); }

        sealed class Link
        {
            readonly Transport owner;
            readonly int id;
            readonly TcpClient client;
            readonly NetworkStream stream;
            // Each packet with the time it may leave (lab latency simulation; immediately otherwise).
            readonly ConcurrentQueue<KeyValuePair<long, byte[]>> outgoing = new ConcurrentQueue<KeyValuePair<long, byte[]>>();
            long lastDue;
            readonly System.Random jitter = new System.Random();
            readonly AutoResetEvent wake = new AutoResetEvent(false);
            volatile bool closed;
            volatile bool closeAfterSend;
            public readonly string Remote;
            public Link(Transport owner, int id, TcpClient client)
            {
                this.owner = owner; this.id = id; this.client = client; stream = client.GetStream();
                try { Remote = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString(); } catch { Remote = ""; }
            }
            public void Start() { StartThread(Read); StartThread(Write); }
            public void Close() { closed = true; client.Close(); wake.Set(); }
            public void Send(byte[] packet)
            {
                if (closed || closeAfterSend) return;
                // A link that cannot keep up for several seconds is dropped rather than buffering without limit.
                if (outgoing.Count >= 1024) { owner.Emit(id, "note", null, "link " + id + " dropped: 1024 packets waiting"); owner.Drop(id); return; }
                long due = 0;
                if (LabDelayMs > 0)
                {
                    // Order is kept (TCP): a packet never leaves before the one queued ahead of it.
                    due = Math.Max(lastDue, System.Diagnostics.Stopwatch.GetTimestamp() + (LabDelayMs + jitter.Next(LabJitterMs + 1)) * System.Diagnostics.Stopwatch.Frequency / 1000);
                    lastDue = due;
                }
                outgoing.Enqueue(new KeyValuePair<long, byte[]>(due, packet));
                wake.Set();
            }
            public void SendAndClose(byte[] packet) { Send(packet); closeAfterSend = true; wake.Set(); }
            void Read()
            {
                try
                {
                    while (!closed)
                    {
                        byte[] header = ReadExactly(stream, 4);
                        int size = header[0] | header[1] << 8 | header[2] << 16 | header[3] << 24;
                        if (size < 1 || size > MaxFrame) throw new IOException("Invalid frame length.");
                        owner.Emit(id, "packet", ReadExactly(stream, size), null);
                    }
                }
                catch (Exception ex) { if (!closed) owner.Emit(id, "note", null, "link " + id + " closed while reading: " + ex.GetType().Name + ": " + ex.Message); }
                finally { owner.Drop(id, this); }
            }
            void Write()
            {
                try
                {
                    while (!closed)
                    {
                        byte[] packet;
                        KeyValuePair<long, byte[]> next;
                        if (!outgoing.TryPeek(out next)) { if (closeAfterSend) break; wake.WaitOne(1000); continue; }
                        long wait = next.Key - System.Diagnostics.Stopwatch.GetTimestamp();
                        if (wait > 0) { Thread.Sleep((int)Math.Max(1, wait * 1000 / System.Diagnostics.Stopwatch.Frequency)); continue; }
                        if (!outgoing.TryDequeue(out next)) continue;
                        packet = next.Value;
                        int size = packet.Length;
                        byte[] header = { (byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24) };
                        stream.Write(header, 0, 4); stream.Write(packet, 0, size);
                    }
                }
                catch (Exception ex) { if (!closed) owner.Emit(id, "note", null, "link " + id + " closed while writing: " + ex.GetType().Name + ": " + ex.Message); }
                finally { owner.Drop(id, this); wake.Dispose(); }
            }
        }

        public static byte[] ReadExactly(Stream stream, int size)
        {
            byte[] data = new byte[size];
            int offset = 0;
            while (offset < size)
            {
                int read = stream.Read(data, offset, size - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return data;
        }
    }

    public enum Op : byte
    {
        Hello = 1, Welcome, Roster, Reject, Leave, Ping, Pong, Notice,
        Pose, Scene, SceneReady, History,
        Interact, MouseClick, TriggerEnter, TriggerExit, DialogueChoice,
        Mita, Door, SceneLoad, Visible, SaveFile, Face, Cutscene, CutscenePlay, KeyAdd, KeyRemove, HandItem, Death, SkipDialogue, Prop, Minigame, Carry, InteractionDone, Dialogue, WorldState, AnimEvent, Novella, LookTrigger, DistanceTrigger, Call
    }

    public static class Wire
    {
        public const int Version = 5;
        public static byte[] Packet(Op op, Action<BinaryWriter> write)
        {
            using (MemoryStream memory = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(memory))
            {
                writer.Write((byte)op); if (write != null) write(writer);
                if (memory.Length > Transport.MaxFrame) throw new IOException("Packet too large.");
                return memory.ToArray();
            }
        }
        public static string Text(BinaryReader reader, int max)
        {
            // Explicit byte lengths avoid BinaryReader.ReadString allocating from a peer supplied prefix.
            int count = reader.ReadUInt16();
            if (count > max * 4 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new IOException("Invalid text length.");
            string value = System.Text.Encoding.UTF8.GetString(Transport.ReadExactly(reader.BaseStream, count));
            if (value.Length > max) throw new IOException("Text too long.");
            return value;
        }
        public static void Text(BinaryWriter writer, string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value ?? "");
            if (bytes.Length > 4096) throw new IOException("Text too long.");
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }
        public static float Number(BinaryReader reader)
        {
            float value = reader.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 100000) throw new BadValueException();
            return value;
        }

        // A NaN or absurd value (e.g. a camera pose computed mid-teleport): that one packet is skipped, the link stays.
        public sealed class BadValueException : IOException { public BadValueException() : base("Invalid number.") { } }
        // FNV-1a: stable across processes, unlike string.GetHashCode on .NET 6.
        public static int Hash(string value)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in value ?? "") { h ^= c; h *= 16777619; }
                return (int)h;
            }
        }
    }
}
