using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "jump45" (any player count, Scene 4): the end of Scene 4 goes straight on to Scene 5 through the game's own
    // Scene_Load on World (StartLoad, then GoScene once loaded), as the story does. In the scene4 tour every frame of
    // Scene 5 then threw in Audio_BlendShapeVoice.LateUpdate on both PCs; a cold start of Scene 5 didn't. Run it with
    // -mt-nopatch to see whether the game alone does it.
    //   jump.clean: (host) after 30 s in Scene 5, fewer than 20 game errors since the jump
    public sealed partial class TogetherRuntime
    {
        Scene_Load labJumpLoad;
        float labJumpAt = -1;
        int labJumpErrors;

        void JumpTour(float now, string scene)
        {
            if (!Hosting) return;
            if (scene == "Scene 4 - StartSecret" && LocalPlayer != null && now - tourSceneAt > 20 && labJumpAt < 0)
            {
                foreach (Scene_Load l in Object.FindObjectsOfType<Scene_Load>(true))
                    if (l != null && l.transform.parent == null && l.name == "World") { labJumpLoad = l; break; }
                if (labJumpLoad == null) { CheckOnce("jump.clean", false, "no Scene_Load on World in " + scene); return; }
                labJumpAt = now; labJumpErrors = errorCount;
                Log("Lab: jump from Scene 4 through its own Scene_Load (errors so far " + errorCount + ")");
                labJumpLoad.StartLoad();
                return;
            }
            if (labJumpLoad != null && scene == "Scene 4 - StartSecret" && now - labJumpAt > 10 && !labJumpWent) { labJumpWent = true; Log("Lab: GoScene"); labJumpLoad.GoScene(); }
            if (scene == "Scene 5 - StartHorror" && labJumpAt > 0 && now - tourSceneAt > 30)
                CheckOnce("jump.clean", errorCount - labJumpErrors < 20, (errorCount - labJumpErrors) + " game errors since the jump");
        }
        bool labJumpWent;
    }
}
