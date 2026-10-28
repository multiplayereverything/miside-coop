using UnityEngine;

namespace MiSideTogether
{
    // Lab tour "roomhold" (2 players, Scene 1): the host's story switches off a room while the client stands in it (here
    // "World RealRoom", which the game itself switches off when the phone app opens). The client must keep it on, so it
    // does not stand in a black void (playtest: "if a room gets unloaded because the host is moving rooms ...").
    //   room.off@host    the host switched the room off
    //   room.kept        the client still has the room on 8 s later
    public sealed partial class TogetherRuntime
    {
        float labRoomOffAt = -1;

        void RoomHoldTick(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (Hosting && !tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 1 - RealRoom"); return; }
            if (scene != "Scene 1 - RealRoom" || LocalPlayer == null || now - tourSceneAt < 40) return;
            GameObject room = SceneObject("World RealRoom");
            if (Hosting && labRoomOffAt < 0 && room != null && room.activeSelf)
            {
                labRoomOffAt = now;
                room.SetActive(false);
                CheckOnce("room.off", true, "host switched World RealRoom off");
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-roomoff")));
            }
            if (!Hosting && labRoomOffAt > 0 && now - labRoomOffAt > 8)
                CheckOnce("room.kept", room != null && room.activeSelf, room != null && room.activeSelf ? "World RealRoom still on here (this player stands in it)" : "World RealRoom went off around this player");
        }

        void RoomOffNotice() { labRoomOffAt = Time.realtimeSinceStartup; }
    }
}
