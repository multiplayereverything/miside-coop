using System;
using System.Collections.Generic;

namespace MiSideTogether
{
    // A host listens on both transports at once (TCP for LAN/VPN friends, Steam P2P for Steam friends) and gives every
    // client one player id (1..3) whichever way it came in.
    public sealed class HostTransport : ITransport
    {
        readonly List<ITransport> parts = new List<ITransport>();
        readonly Dictionary<long, int> toGlobal = new Dictionary<long, int>();
        readonly Dictionary<int, long> toLocal = new Dictionary<int, long>();
        int next;

        public bool Hosting { get { return true; } }
        public int Port { get; private set; }
        public bool Steam { get; private set; }

        public void Add(ITransport part, bool steam)
        {
            parts.Add(part);
            if (!steam && part.Port > 0) Port = part.Port;
            if (steam) Steam = true;
        }

        static long Pack(int part, int id) { return ((long)part << 32) | (uint)id; }

        public bool Poll(out NetworkEvent e)
        {
            for (int n = 0; n < parts.Count; n++)
            {
                int k = (next + n) % parts.Count;
                NetworkEvent inner;
                while (parts[k].Poll(out inner))
                {
                    if (inner.Peer < 0) { e = inner; next = k + 1; return true; }
                    long local = Pack(k, inner.Peer);
                    int global;
                    if (inner.Kind == "connected")
                    {
                        global = 1;
                        while (toLocal.ContainsKey(global)) global++;
                        if (global > Transport.MaxPeers) { parts[k].Drop(inner.Peer); continue; }
                        toGlobal[local] = global; toLocal[global] = local;
                    }
                    else if (!toGlobal.TryGetValue(local, out global)) continue;
                    if (inner.Kind == "disconnected") { toGlobal.Remove(local); toLocal.Remove(global); }
                    e = new NetworkEvent { Peer = global, Kind = inner.Kind, Data = inner.Data, Message = inner.Message };
                    next = k + 1;
                    return true;
                }
            }
            e = null;
            return false;
        }

        bool Route(int id, out ITransport part, out int local)
        {
            long packed;
            part = null; local = 0;
            if (!toLocal.TryGetValue(id, out packed)) return false;
            part = parts[(int)(packed >> 32)]; local = (int)(packed & 0xFFFFFFFF);
            return true;
        }

        public void Send(int id, byte[] packet) { ITransport p; int l; if (Route(id, out p, out l)) p.Send(l, packet); }
        public void SendAndClose(int id, byte[] packet) { ITransport p; int l; if (Route(id, out p, out l)) p.SendAndClose(l, packet); }
        public void Drop(int id) { ITransport p; int l; if (Route(id, out p, out l)) p.Drop(l); }
        public string Address(int id) { ITransport p; int l; return Route(id, out p, out l) ? p.Address(l) : ""; }
        public void Dispose() { foreach (ITransport p in parts) p.Dispose(); parts.Clear(); toGlobal.Clear(); toLocal.Clear(); }
    }
}
