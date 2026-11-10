using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiSideTogether
{
    // Host migration: when the host goes away (quits, crashes, stops hosting), the remaining player with the lowest
    // player number becomes the host where they stand, and everyone else reconnects to them (Steam when both have it,
    // otherwise their LAN address from the roster). Nobody reloads: the new host announces the chapter everyone is in.
    public sealed partial class TogetherRuntime
    {
        sealed class Successor { public int Id; public string Name; public ulong SteamId; public string Address; public int Port; }
        Successor migrateTo;
        float migrateAt = -1;
        bool migrating, leftOnPurpose;
        public int migrations;

        // A dropped link is not a host that left: the client first tries its host again, once, and keeps its place
        // in the story. Only when that fails (or the host said it was leaving) does someone take over.
        List<Successor> reconnectFallback;
        float reconnectUntil = -1;
        int reconnectSelf, resumeLogNext, resumeSceneSeq = -1;
        bool hostLeft;

        bool TryReconnect()
        {
            if (leftOnPurpose || hostLeft || self <= 0) return false;
            Peer host;
            ulong steam = Network is SteamTransport && peers.TryGetValue(0, out host) ? host.SteamId : 0;
            string hostName = peers.TryGetValue(0, out host) ? host.Name : "the host";
            reconnectFallback = Successors(); reconnectSelf = self;
            int next = logNext, sceneSeq = wantedSeq;
            migrating = true;
            Shutdown();
            migrating = false;
            Tell("Lost the connection to the host. Trying again...");
            if (steam != 0) StartJoinSteam(steam, hostName); else StartJoin();
            rejoining = true; resumeLogNext = next; resumeSceneSeq = sceneSeq;
            reconnectUntil = Time.realtimeSinceStartup + 6;
            return true;
        }

        void ReconnectFailed()
        {
            reconnectUntil = -1; resumeSceneSeq = -1;
            migrating = true;
            Shutdown();
            migrating = false;
            if (reconnectFallback != null) { Migrate(reconnectFallback, reconnectSelf); return; }
            Shutdown();
            Tell("Lost connection to the host. You keep playing alone.");
        }

        // Everyone left without the host, lowest player number first; null when this player is the only one.
        List<Successor> Successors()
        {
            List<Successor> remaining = new List<Successor>();
            foreach (Peer p in peers.Values)
                if (p.Accepted && p.Id != 0) remaining.Add(new Successor { Id = p.Id, Name = p.Name, SteamId = p.SteamId, Address = p.Address, Port = p.ListenPort });
            if (remaining.Count == 0) return null;
            remaining.Add(new Successor { Id = self, Name = Name, SteamId = Steam.Self, Address = "", Port = Port });
            remaining.Sort((a, b) => a.Id.CompareTo(b.Id));
            return remaining;
        }

        // Called when a client loses its host. Returns true when a migration was started.
        bool TryMigrate()
        {
            if (leftOnPurpose) return false;
            List<Successor> remaining = Successors();
            if (remaining == null) return false;
            Migrate(remaining, self);
            return true;
        }

        void Migrate(List<Successor> remaining, int me)
        {
            Successor next = remaining[0];
            migrating = true;
            Shutdown();
            migrating = false;
            migrations++;
            if (next.Id == me || next.Id < 0)
            {
                StartHost();
                Tell("The host left: you are the new host.");
                Log("Migration: became the host");
            }
            else
            {
                // Give the successor a moment to start listening.
                migrateTo = next;
                migrateAt = Time.realtimeSinceStartup + 4;
                Tell("The host left: " + next.Name + " takes over...");
                Log("Migration: joining " + next.Name + " (steam " + next.SteamId + ", address " + next.Address + ":" + next.Port + ")");
            }
        }

        void MigrationTick(float now)
        {
            if (reconnectUntil > 0 && now > reconnectUntil) ReconnectFailed();
            if (migrateTo == null || now < migrateAt || Active) return;
            Successor next = migrateTo;
            migrateTo = null;
            rejoining = true;   // already in the chapter: the new host lets us straight back in
            if (next.SteamId != 0 && Steam.Ready && Steam.Self != 0 && next.SteamId != Steam.Self) { StartJoinSteam(next.SteamId, next.Name); return; }
            if (!string.IsNullOrEmpty(next.Address))
            {
                Address = next.Address; portText = (next.Port > 0 ? next.Port : 27030).ToString();
                StartJoin();
                return;
            }
            Tell("Could not reach " + next.Name + "; you keep playing alone.");
        }
    }
}
