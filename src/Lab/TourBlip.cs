using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "blip" (2 players, Scene 2): the client's link to the host drops once, with both games still running.
    // The client must come back to the same host, in the same chapter, and carry on with the story.
    //   blip.kept@host: the host has its player again
    //   blip.back:      the client is connected again and sees the host's body
    //   blip.stayed:    the client never reloaded the chapter
    //   blip.story:     a press the host makes afterwards happens on the client
    public sealed partial class TogetherRuntime
    {
        float labBlipAt = -1;
        bool labBlipPressed;

        void BlipTour(float now, string scene)
        {
            if (Hosting)
            {
                if (!tourStarted && AcceptedCount > 0 && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame(TourChapter); return; }
                if (scene != TourChapter || LocalPlayer == null) return;
                if (!labBlipPressed && now - tourSceneAt > 55 && LocalFree(LocalPlayer))
                {
                    foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                        if (o != null && o.name == "Door Exit" && LabPress(o)) { labBlipPressed = true; Log("Lab: host presses the exit door after the blip"); break; }
                }
                if (now - tourSceneAt > 70) CheckOnce("blip.kept", AcceptedCount == 1, AcceptedCount + " player(s) with the host");
                return;
            }
            if (scene == "SceneMenu" || !scene.StartsWith("Scene ", System.StringComparison.Ordinal)) return;
            if (labBlipAt < 0)
            {
                if (!Connected || LocalPlayer == null || now - tourSceneAt < 25) return;
                labBlipAt = now;
                Log("Lab: the client's link drops");
                Network.Drop(0);
                return;
            }
            if (now - labBlipAt < 15) return;
            Peer host;
            bool back = Connected && self > 0 && peers.TryGetValue(0, out host) && host.Avatar != null;
            CheckOnce("blip.back", back, back ? "connected as player " + self + ", the host's body is here" : Network == null ? "not connected: playing alone" : "connected: " + Connected + ", player " + self);
            CheckOnce("blip.stayed", Time.timeSinceLevelLoad > now - labBlipAt + 5, "in this chapter for " + Time.timeSinceLevelLoad.ToString("0") + " s");
            bool door = LastAppliedStory != null && LastAppliedStory.Contains("Door Exit");
            if (door) CheckOnce("blip.story", true, LastAppliedStory);
            else if (now - labBlipAt > 55) CheckOnce("blip.story", false, "the host's press never happened here; last: " + LastAppliedStory);
        }
    }
}
