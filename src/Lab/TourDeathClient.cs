using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "deathclient" (2 players, Scene 11): a mannequin catches the client, not the host. Both players first walk
    // away from where the chapter put them; the game's death then takes each of them back to the checkpoint.
    //   client.deathclient.died:     the client's own death took it back to the checkpoint
    //   deathclient.restarted@host:  the host, who was not caught, went back too
    public sealed partial class TogetherRuntime
    {
        float labDeathClientAt = -1;
        Vector3 labDeathAway;
        bool labDeathWalked;

        void DeathClientTour(float now, string scene)
        {
            if (Hosting && !tourStarted && AcceptedCount > 0 && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame(TourChapter); return; }
            PlayerMove player = LocalPlayer;
            if (!scene.StartsWith("Scene ", StringComparison.Ordinal) || player == null) return;
            Location10_ManekenChekpoint checkpoint = Object.FindObjectOfType<Location10_ManekenChekpoint>(true);
            if (checkpoint == null || now - tourSceneAt < 24) return;
            if (!labDeathWalked)
            {
                labDeathWalked = true;
                UnityEngine.AI.NavMeshHit hit;
                Vector3 from = player.transform.position;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 to = from + Quaternion.Euler(0, i * 45, 0) * Vector3.forward * 3f;
                    if (UnityEngine.AI.NavMesh.SamplePosition(to, out hit, 1f, UnityEngine.AI.NavMesh.AllAreas) && (hit.position - from).magnitude > 2f) { player.TeleportPlayer(hit.position); break; }
                }
                return;
            }
            if (now - tourSceneAt < 28) return;
            if (labDeathClientAt < 0)
            {
                labDeathClientAt = now; labDeathAway = player.transform.position;
                if (Hosting) return;
                Log("Lab: client is caught by a mannequin");
                try { checkpoint.PlayerDeath(); } catch (Exception ex) { Log("Lab: PlayerDeath() threw: " + ex.Message); }
                return;
            }
            if (now - labDeathClientAt < (Hosting ? 16 : 8)) return;
            float moved = (player.transform.position - labDeathAway).magnitude;
            string detail = "the player is " + moved.ToString("0.0") + " m from where it stood before the death";
            if (Hosting) CheckOnce("deathclient.restarted", moved > 1f, detail);
            else CheckOnce("deathclient.died", moved > 1f, detail);
        }
    }
}
