using UnityEngine;

namespace MiSideTogether
{
    // Scene 14's phone: the player who picks it up walks a little character ("Player Mobile", Location14_PlayerMobileMove)
    // through a toy house with the movement keys until it reaches "Trigger PlayerMobile", which moves the story on
    // (wiring from the lab's -mt-dump). The phone is presser-only (HeldItems), so the holder runs the character; on the
    // others the story's catch-up still calls Play (the "CheckUp" animation's loop), and their copy then threw in
    // FixedUpdate every physics step and walked from their own keys. Now:
    //   * the holder's Play is relayed (15) and shows the character on the others, without running it;
    //   * on the others Play, Update and FixedUpdate do nothing, and the holder's character is placed by the prop stream
    //     (Props.cs streams any moving rigidbody: position, rotation, velocity);
    //   * the holder is whoever clicked "Tamagotchi/.../Smartphone" here (unknown until someone does: then nothing changes).
    public sealed partial class TogetherRuntime
    {
        static int mobileHolder;   // 0 unknown, 1 this machine, 2 someone else
        public int mobileShown;

        public static void NoteMobileHolder(string clickKey, bool local)
        {
            if (clickKey != null && clickKey.Contains("Tamagotchi") && clickKey.Contains("Smartphone")) mobileHolder = local ? 1 : 2;
        }

        public static bool MobileWatcher => mobileHolder == 2 && Instance != null && Instance.Connected;

        public static void MobilePlayed(Location14_PlayerMobileMove m)
        {
            if (m == null || mobileHolder != 1) return;
            Call(15, m, 0);
        }

        void ApplyMobileShow(string target)
        {
            Location14_PlayerMobileMove m = SceneKeys.Find<Location14_PlayerMobileMove>(target);
            if (Missing(m, target)) return;
            if (!m.gameObject.activeSelf) m.gameObject.SetActive(true);
            mobileShown++;
            Log("Phone character: showing the holder's character " + target);
        }
    }
}
