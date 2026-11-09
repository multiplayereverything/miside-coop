using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Scene 2: taking the first battery (at the toilet's scarf) starts Quest 4 Corridor, which switches the rest of the house off.
    // Alone that is fine, the player is at the toilet. In a session the others can be anywhere, and are left with no room around
    // them. So when the quest starts, whoever isn't the player nearest the scarf goes to that player. Each PC works it out from the
    // same positions.
    public sealed partial class TogetherRuntime
    {
        bool corridorStarted;
        float corridorGatherUntil = -1;

        void CorridorGatherTick(float now)
        {
            if (!Connected || ActiveSceneName != "Scene 2 - InGame") { corridorStarted = false; corridorGatherUntil = -1; return; }
            GameObject quests = SceneObject("Quests");
            Transform quest = quests != null ? quests.transform.Find("Quest 4 Corridor") : null;
            if (quest != null && quest.gameObject.activeSelf && !corridorStarted) { corridorStarted = true; corridorGatherUntil = now + 8; }
            PlayerMove player = LocalPlayer;
            if (corridorGatherUntil < 0 || player == null) return;
            if (now > corridorGatherUntil) { corridorGatherUntil = -1; return; }
            if (!LocalFree(player)) return;
            Transform scarf = null;
            foreach (Transform t in Object.FindObjectsOfType<Transform>(true)) if (t.name == "Scarf") { scarf = t; break; }
            if (scarf == null) { corridorGatherUntil = -1; return; }
            Peer nearest = null;
            float best = (player.transform.position - scarf.position).sqrMagnitude;
            foreach (Peer p in peers.Values)
            {
                if (!p.HasPose || p.SceneHash != LocalSceneHash) continue;
                float d = (p.Feet - scarf.position).sqrMagnitude;
                if (d < best) { best = d; nearest = p; }
            }
            corridorGatherUntil = -1;
            if (nearest == null || (player.transform.position - nearest.Feet).sqrMagnitude < 9f) return;
            player.TeleportPlayer(nearest.Feet);
            Log("Brought to " + nearest.Name + " as the corridor starts");
            Tell("Brought you to " + nearest.Name + ": the house is changing.");
        }
    }
}
