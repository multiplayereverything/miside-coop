using System;

namespace MiSideTogether
{
    // Lab tour "steam" (2 players): the Steam transport end to end over the lab Steam mock (SteamMock.cs), with one
    // Steam account. The client joins the way Steam's Join Game does (+mt_join <host's SteamID>), so the host must find
    // it through rich presence and accept its session. Every 4th send fails (-mt-steam-fail 4) as a full Steam send
    // buffer does; the host then sends 60 numbered notices, and the client must get all of them, in order.
    //   steam.joined   the client is connected over Steam (not TCP) and the host accepted it
    //   steam.order    60 of 60 numbered notices arrived in order, with sends retried after failures
    public sealed partial class TogetherRuntime
    {
        const int SteamSeqCount = 60;
        int steamSeqSent, steamSeqNext;
        bool steamSeqBroken;
        float steamSeqAt = -1;

        void SteamTour(float now, string scene)
        {
            MenuChecks(now, scene);
            if (scene != "SceneMenu" || !Connected || now - tourSceneAt < 12) return;
            if (!Hosting)
            {
                CheckOnce("steam.joined", Network is SteamTransport, "client transport " + (Network != null ? Network.GetType().Name : "none") + ", Steam user " + Steam.Self);
                return;
            }
            if (steamSeqAt < 0) steamSeqAt = now;
            // 6 per second, so the failed sends are spread over the whole run.
            while (steamSeqSent < SteamSeqCount && now - steamSeqAt > steamSeqSent / 6f)
            {
                string text = "lab-seq:" + steamSeqSent++;
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, text)));
            }
        }

        void SteamSeqNotice(string notice)
        {
            if (!int.TryParse(notice.Substring("lab-seq:".Length), out int n)) return;
            if (n != steamSeqNext) steamSeqBroken = true;
            steamSeqNext = n + 1;
            if (n == SteamSeqCount - 1)
                CheckOnce("steam.order", !steamSeqBroken, (steamSeqBroken ? "notices lost or out of order" : SteamSeqCount + " of " + SteamSeqCount + " in order")
                    + "; failed sends here " + (Steam.Mock != null ? Steam.Mock.Failed : 0) + ", retried " + SteamTransport.SendRetries);
        }
    }
}
