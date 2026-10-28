using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "scene3" (2 players): Scene 3's opening as a player plays it, up to Mita's walk with the player (Quest 2
    // Walking), where the playtest saw NavMesh "SetDestination" errors and a dialogue-choice exception on the client.
    // From the wiring (-mt-dump): step into Quest 1 Start/TriggerForShowMita (a Trigger_Event; it switches on its child
    // TriggerLook), look at it (Trigger_MouseEvent: Mita appears, TriggerSideMita switches on), look at TriggerSideMita
    // (the dialogue starts); a timer then walks Mita to her seat (MitaAIMovePoint) and another starts the walk.
    //   s3.walk@host      the walk quest started on the host
    //   s3.walk           it started on the client too
    //   s3.navmesh        the client logged no SetDestination error during 60 s of the walk
    //   s3.choice         the client applied the host's answer to Mita's menu with no ClickButton exception
    public sealed partial class TogetherRuntime
    {
        float labS3StepAt, labS3WalkAt = -1, labS3ShotAt;
        int labS3Step, labS3Shots;
        float labS3PickAt = -1;
        public static int choiceErrors;

        void Scene3PickNotice() { labS3PickAt = Time.realtimeSinceStartup; }

        static Transform QuestPart(string quest, string path)
        {
            GameObject quests = SceneObject("Quests");
            if (quests == null) return null;
            for (int i = 0; i < quests.transform.childCount; i++)
            {
                Transform q = quests.transform.GetChild(i);
                if (q.name == quest) return q.Find(path);
            }
            return null;
        }

        void Scene3Tick(float now, string scene)
        {
            GlobalGame.hintScreen = false;
            if (Hosting && !tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; LoadThroughGame("Scene 3 - WeTogether"); return; }
            if (scene != "Scene 3 - WeTogether" || LocalPlayer == null || now - tourSceneAt < 25 || now < labS3StepAt) return;
            labS3StepAt = now + 3;
            Transform walk = null;
            GameObject quests = SceneObject("Quests");
            if (quests != null) for (int i = 0; i < quests.transform.childCount; i++) if (quests.transform.GetChild(i).name == "Quest 2 Walking") walk = quests.transform.GetChild(i);
            if (walk != null && walk.gameObject.activeInHierarchy && labS3WalkAt < 0) { labS3WalkAt = now; CheckOnce("s3.walk", true, "the walk quest is on here"); }
            if (labS3WalkAt > 0 && !Hosting && now - labS3WalkAt > 60) CheckOnce("s3.navmesh", navMeshErrors == 0, "SetDestination errors here during the walk: " + navMeshErrors);
            if (!Hosting && labS3PickAt > 0 && now - labS3PickAt > 10)
                CheckOnce("s3.choice", dialogueChoicesApplied > 0 && choiceErrors == 0, "choices applied here: " + dialogueChoicesApplied + ", ClickButton exceptions: " + choiceErrors);
            if (!Hosting) return;
            if (now >= labS3ShotAt) { labS3ShotAt = now + 20; LabShotAll("s3-" + (labS3Shots++).ToString("00")); }
            if (labS3Step == 0)
            {
                Transform show = QuestPart("Quest 1 Start", "TriggerForShowMita");
                Collider c = show != null ? show.GetComponent<Collider>() : null;
                if (c == null || !show.gameObject.activeInHierarchy) return;
                Vector3 p = c.bounds.center; p.y = LocalPlayer.transform.position.y;
                LocalPlayer.TeleportPlayer(p); labS3Step = 1;
                Log("Lab: scene3 host steps into TriggerForShowMita at " + p);
                return;
            }
            if (labS3Step == 1)
            {
                Transform look = QuestPart("Quest 1 Start", "TriggerForShowMita/TriggerLook");
                Trigger_MouseEvent t = look != null && look.gameObject.activeInHierarchy ? look.GetComponent<Trigger_MouseEvent>() : null;
                if (t == null) { Log("Lab: scene3 waits for TriggerLook (" + (look != null ? "off" : "missing") + ")"); return; }
                t.Enter(); labS3Step = 2;
                Log("Lab: scene3 host looks: Mita appears");
                return;
            }
            if (labS3Step == 2)
            {
                Transform side = QuestPart("Quest 1 Start", "TriggerSideMita");
                Trigger_MouseEvent t = side != null && side.gameObject.activeInHierarchy ? side.GetComponent<Trigger_MouseEvent>() : null;
                if (t == null) return;
                t.Enter(); labS3Step = 3;
                Log("Lab: scene3 host looks at Mita (TriggerSideMita)");
                return;
            }
            // The walk: talk to Mita (InteractiveMita opens her choice menu, DialogueChanger) and pick the first answer,
            // as the playtest did when the client's ClickButton threw.
            if (labS3Step == 3 && labS3WalkAt > 0 && now - labS3WalkAt > 5)
            {
                Transform talk = QuestPart("Quest 2 Walking", "DialogueQuest Mita/InteractiveMita");
                ObjectInteractive o = talk != null && talk.gameObject.activeInHierarchy ? talk.GetComponent<ObjectInteractive>() : null;
                if (o == null || !o.active) return;
                o.Click(); labS3Step = 4;
                Log("Lab: scene3 host talks to Mita");
                return;
            }
            if (labS3Step == 4)
            {
                Transform menuT = QuestPart("Quest 2 Walking", "DialogueQuest Mita/DialogueChanger Mita");
                DialogueChanger menu = menuT != null ? menuT.GetComponent<DialogueChanger>() : null;
                if (menu == null || !menu.play || menu.buttons == null) return;
                menu.ClickButton(1); labS3Step = 5;
                Log("Lab: scene3 host picks answer 1");
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-s3pick")));
                return;
            }
            if (labS3WalkAt < 0 && now - tourSceneAt > 400) CheckOnce("s3.walk", false, "the walk never started (step " + labS3Step + ")");
        }
    }
}
