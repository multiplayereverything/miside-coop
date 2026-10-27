using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Scripted lab tours (-mt-tour). Each one drives the host and/or clients like players would and reports MT-CHECK
    // lines; scripts/run-tour.ps1 lists which checks each tour expects.
    //   connect   handshake and roster on both sides
    //   avatars   host starts a chapter, client follows, walks up; both see the other's body model and animation
    //   story     a host interaction is replayed on the client; a client interaction reaches the host
    //   mita      host-driven Mita matches on the client
    //   chapters  host walks through every chapter scene; clients must follow each and see the host's model
    public sealed partial class TogetherRuntime
    {
        // Every chapter, entered from the main menu like a player picking it from Chapters. Scene 19 (Glasses) is left out:
        // the game itself cannot start it cold, even from the menu (World.Update NRE every frame on the host too, run of
        // 2026-10-22); players only reach it through the story, where Op.SceneLoad replays the game's own transition.
        public static readonly string[] Chapters =
        {
            "Scene 1 - RealRoom", "Scene 2 - InGame", "Scene 3 - WeTogether", "Scene 4 - StartSecret", "Scene 5 - StartHorror",
            "Scene 6 - BasementFirst", "Scene 7 - Backrooms", "Scene 8 - ReRooms", "Scene 9 - ChibiMita", "Scene 10 - ManekenWorld",
            "Scene 11 - Backrooms", "Scene 12 - Freak", "Scene 13 - HelloCore", "Scene 14 - MobilePlayer", "Scene 15 - BasementAndDeath",
            "Scene 16 - TheEnd", "Scene 17 - Dreamer", "Scene 20 - FightMita", "Scene 18 - 2D"
        };
        const float ChapterSeconds = 45;

        string tourChapter;
        bool tourStarted, tourMoved, tourStepped;
        float tourTeleportAt;
        bool menuDumped;
        float tourSceneAt = -1, tourChapterAt = -1, labStepAt = -1;
        string tourScene = "";
        int chapterIndex = -1;
        Vector3 tourStepTarget;
        public static string LastAppliedStory;

        string TourChapter
        {
            get
            {
                if (!string.IsNullOrEmpty(tourChapter)) return tourChapter;
                return labTour == "mita" ? "Scene 4 - StartSecret" : labTour == "death" ? "Scene 11 - Backrooms" : labTour == "props" ? "Scene 5 - StartHorror" : labTour == "world" ? "Scene 2 - InGame" : labTour == "carry" ? "Scene 2 - InGame" : labTour == "dialogue" ? "Scene 4 - StartSecret" : labTour == "gather" ? "Scene 2 - InGame" : labTour == "room" ? "Scene 1 - RealRoom" : labTour == "mirror" ? "Scene 2 - InGame" : labTour == "minigame" ? "Scene 4 - StartSecret" : labTour == "novella" ? "Scene 18 - 2D" : labTour == "interactions" ? "Scene 2 - InGame" : labTour == "triggers" ? "Scene 11 - Backrooms" : labTour == "hostcut" ? "Scene 1 - RealRoom" : labTour == "story" ? "Scene 2 - InGame" : "Scene 1 - RealRoom";
            }
        }

        void TourUpdate(float now)
        {
            string scene = ActiveSceneName;
            if (scene != tourScene) { tourScene = scene; tourSceneAt = now; tourMoved = false; }
            if (labTour.Length == 0) return;
            PanelCheck(now);
            if (scene == "SceneMenu" && !menuDumped && now - tourSceneAt > 3)
            {
                // How the game's own chapter menu loads each chapter (scene + save file), for scene-follow research.
                menuDumped = true;
                foreach (MenuCaseLoad c in Object.FindObjectsOfType<MenuCaseLoad>(true))
                    if (c != null) Log("Lab: menu chapter '" + c.nameSceneLoad + "' file '" + c.fileNameLoad + "' at " + SceneKeys.Key(c));
            }
            if (Connected && AcceptedCount > 0)
            {
                CheckOnce("connect.handshake", true, (Hosting ? "host sees " : "client sees ") + AcceptedCount + " peer(s)");
                bool named = true; foreach (Peer p in peers.Values) if (p.Accepted && string.IsNullOrEmpty(p.Name)) named = false;
                CheckOnce("connect.roster", named, string.Join(",", Names()));
            }
            if (labTour == "survey") { SurveyTour(now, scene); return; }
            if (labTour == "inventory") { InventoryTour(now, scene); return; }
            TeleportSafeCheck(now);
            if (labTour == "connect") { MenuChecks(now, scene); return; }
            if (labTour == "steam") { SteamTour(now, scene); return; }
            if (labTour == "quest") { if (Hosting) QuestHost(now, scene); return; }
            if (Hosting) HostTour(now, scene); else if (Connected) ClientTour(now, scene);
        }

        // Every tour opens the F5 panel for a few seconds: it must draw (every widget) without an exception.
        float panelOpenedAt = -1; int panelFramesAtOpen;

        void PanelCheck(float now)
        {
            if (labChecked.Contains("ui.panel") || now < 8) return;
            if (panelOpenedAt < 0) { panelOpenedAt = now; panelFramesAtOpen = panelFrames; SetPanel(true); return; }
            if (now - panelOpenedAt < 3) return;
            SetPanel(false);
            int drawn = panelFrames - panelFramesAtOpen;
            CheckOnce("ui.panel", drawn > 0 && TogetherBehaviour.GuiErrors == 0, drawn + " panel frames drawn, " + TogetherBehaviour.GuiErrors + " OnGUI errors, fallbacks: " + (Ui.Fallbacks.Length > 0 ? Ui.Fallbacks : "none"));
        }

        IEnumerable<string> Names() { foreach (Peer p in peers.Values) yield return p.Name; }

        void HostTour(float now, string scene)
        {
            if (labTour == "migrate" && migrations > 0) { MigrateChecks(now, scene); return; }
            if ((AcceptedCount == 0 && labPlayers > 1) || (!tourStarted && AcceptedCount < labPlayers - 1 - (labTour == "lobby" ? 1 : 0))) return;
            if (labTour == "chapters") { HostChapters(now, scene); return; }
            if (labTour == "lobby" && LobbyHost(now, scene)) return;
            if (labTour == "novella") { NovellaHost(now, scene); return; }
            if (labTour == "interactions") { InteractionsHost(now, scene); return; }
            if (labTour == "triggers") { TriggersHost(now, scene); return; }
            if (labTour == "hostcut") { HostCutHost(now, scene); return; }
            if (labTour == "held") { HeldHost(now, scene); return; }
            if (labTour == "phone") { PhoneHost(now, scene); return; }
            if (labTour == "scene19") { Scene19Host(now, scene); return; }
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6)
            {
                tourStarted = true;
                Log("Lab: host loading " + TourChapter);
                LoadThroughGame(TourChapter);
            }
            if (scene != TourChapter) return;
            PlayerMove player = LocalPlayer;
            if (player != null) CheckOnce("chapter.loaded", true, scene);
            Peer client = FirstClient();
            if (labTour == "avatars" && client != null) AvatarChecks(now, client, 60);
            if (labTour == "avatars" && player != null && !tourStepped && labChecked.Contains("avatar.model") && now - tourSceneAt > 25)
            {
                // A sideways step the client must see the host's body follow.
                tourStepped = true;
                tourStepTarget = player.transform.position + player.transform.right * 1.5f;
                player.TeleportPlayer(tourStepTarget);
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-step")));
                Log("Lab: host stepped to " + tourStepTarget);
            }
            if (labTour == "story" && player != null && !tourStepped && now - tourSceneAt > 20 && ClientInScene(client))
            {
                tourStepped = true;
                ObjectInteractive pick = PickInteractive();
                if (pick == null) CheckOnce("story.sent", false, "no active ObjectInteractive in " + scene);
                else { Log("Lab: host clicks " + SceneKeys.Key(pick)); pick.Click(); CheckOnce("story.sent", true, SceneKeys.Key(pick)); }
            }
            if (labTour == "story" && LastAppliedStory != null) CheckOnce("story.received", true, LastAppliedStory);
            if (labTour == "mita")
            {
                if (Mitas().Count > 0 && now - tourSceneAt > 10) CheckOnce("mita.sent", true, Mitas().Count + " Mita(s) streamed");
                else if (now - tourSceneAt > 60) CheckOnce("mita.sent", false, "no MitaPerson in " + scene);
                MitaPerson mita = Mitas().Count > 0 ? Mitas()[0] : null;
                // Face: a face layer change on the host must replay on the client.
                if (mita != null && now - tourSceneAt > 20 && !tourStepped) { tourStepped = true; mita.FaceLayer(1); Log("Lab: host set Mita face layer 1"); }
                // Gaze: once the client stands at Mita (see ClientTour), her "look at the player" picks the nearer client.
                if (mita != null && mita.lookLife != null && labGazeAt > 0 && now - labGazeAt > 3 && now - labGazeAt < 40 && Time.frameCount % 30 == 0)
                {
                    int before = gazeRedirects;
                    mita.lookLife.LookOnPlayer();
                    if (gazeRedirects > before) CheckOnce("mita.gaze", true, "Mita looked at the nearer remote player");
                }
                if (labGazeAt > 0 && now - labGazeAt > 40) CheckOnce("mita.gaze", false, "Mita never looked at the nearer remote player");
            }
            if (labTour == "cutscene") { HostCutsceneTour(now, scene); CutsceneHiddenCheck(now, client); }
            if (labTour == "world") HostWorldTour(now, scene, player);
            if (labTour == "migrate" && player != null && scene == TourChapter && labWorldAt < 0 && AcceptedCount >= 2 && now - tourSceneAt > 25)
            {
                labWorldAt = now;
                Log("Lab: host drops out");
                CheckOnce("migrate.hostleft", true, AcceptedCount + " clients connected when the host left");
                Shutdown();
                return;
            }
            if (labTour == "room" && player != null && scene == TourChapter && labWorldAt < 0 && stateReady && now - tourSceneAt > 25)
            {
                // Change the host's room directly (not through any story event): one object switched, one light, one move.
                labWorldAt = now;
                foreach (Tracked e in tracked) if (e.T != null && e.Poses && e.T.childCount == 0 && e.T.GetComponent<Renderer>() != null && e.T.gameObject.activeInHierarchy) { labRoomMove = e.Key; e.T.localPosition += Vector3.up * 0.5f; break; }
                foreach (Tracked e in tracked) if (e.T != null && e.Key != labRoomMove && e.T.GetComponent<Renderer>() != null && e.T.gameObject.activeSelf && e.T.parent != null && e.T.parent.gameObject.activeInHierarchy) { labRoomOff = e.Key; e.T.gameObject.SetActive(false); break; }
                foreach (TrackedLight l in trackedLights) if (l.L != null) { labRoomLight = l.Key; l.L.color = new Color(1, 0.2f, 0.2f); l.L.intensity = l.L.intensity * 0.5f + 0.1f; break; }
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-room:" + labRoomMove + "|" + labRoomOff + "|" + labRoomLight)));
                CheckOnce("room.changed", labRoomMove != null && labRoomOff != null, "moved " + labRoomMove + ", switched off " + labRoomOff + ", relit " + labRoomLight);
            }
            if (labTour == "mirror" && player != null && labWorldAt < 0 && now - labDiagAt > 10) { labDiagAt = now; Log("Lab: mirror waits: free=" + LocalFree(player) + " sit=" + player.sit + " anim=" + player.animationRun + " dontMove=" + player.dontMove + " cutscene=" + LocalInCutscene()); }
            if (labTour == "mirror" && player != null && scene == TourChapter && labWorldAt < 0 && now - tourSceneAt > 25 && !player.animationRun && !LocalInCutscene())
            {
                labWorldAt = now;
                ObjectInteractive phone = null;
                // Scene 2's exit door plays a full player animation right away (Scene 1's opening can hold the player
                // for minutes, and its phone opens a view that waits for input). Fallbacks: an item pick-up, the plane.
                foreach (string want in new[] { "Door Exit", "ItemTake Spoon", "Interactive Air" })
                    if (phone == null) foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>()) if (o != null && o.name == want && o.gameObject.activeInHierarchy) phone = o;
                if (phone == null) CheckOnce("mirror.acted", false, "nothing to press in " + scene);
                else { Log("Lab: host presses " + phone.name); phone.Click(); }
            }
            if (labTour == "mirror" && labWorldAt > 0 && actorAnimation != null) CheckOnce("mirror.acted", true, "host performs " + actorAnimation.Split('/')[^1] + " (" + (ActorOnly ? "actor only" : "mirrored") + ")");
            if (labTour == "gather" && scene == TourChapter && (player != null || labWorldAt > 0))
            {
                // The game's cutscene mode on the host for 8 s; the client walks off meanwhile and must be gathered after.
                if (cachedController == null) cachedController = Object.FindObjectOfType<GameController>();
                if (labWorldAt < 0 && player != null && now - tourSceneAt > 25 && cachedController != null) { labWorldAt = now; cachedController.CutscenePlay(player.transform); Log("Lab: host cutscene starts (cutsceneRun=" + cachedController.cutsceneRun + ")"); }
                if (labWorldAt > 0 && !tourStepped && now - labWorldAt > 8) { tourStepped = true; bool was = LocalInCutscene(); cachedController.CutsceneStop(); Log("Lab: host cutscene ends"); CheckOnce("gather.cutscene", was, was ? "host was in cutscene mode for 8 s" : "cutscene mode did not register"); }
            }
            if (labTour == "dialogue" && player != null && scene == TourChapter)
            {
                if (labWorldAt < 0 && now - tourSceneAt > 25)
                {
                    labWorldAt = now;
                    Dialogue_3DText pick = null;
                    List<Dialogue_3DText> all = new List<Dialogue_3DText>();
                    foreach (Dialogue_3DText d in Object.FindObjectsOfType<Dialogue_3DText>(true)) if (d != null && !d.gameObject.activeSelf && d.transform.parent != null && d.transform.parent.gameObject.activeInHierarchy) all.Add(d);
                    all.Sort((a, b) => string.CompareOrdinal(SceneKeys.Key(a), SceneKeys.Key(b)));
                    if (all.Count > 0) pick = all[0];
                    if (pick == null) CheckOnce("dialogue.sent", false, "no inactive dialogue line in " + scene);
                    else { labDialogueKey = SceneKeys.Key(pick); pick.gameObject.SetActive(true); CheckOnce("dialogue.sent", true, labDialogueKey); }
                }
                if (labWorldAt > 0 && now - labWorldAt > 8 && !tourStepped && labDialogueKey != null)
                {
                    tourStepped = true;
                    Dialogue_3DText line = SceneKeys.Find<Dialogue_3DText>(labDialogueKey);
                    if (line != null) { line.gameObject.SetActive(false); Log("Lab: host closed the line"); }
                }
            }
            if (labTour == "carry" && player != null && scene == TourChapter)
            {
                if (labWorldAt < 0 && now - tourSceneAt > 25)
                {
                    labWorldAt = now;
                    // Actor-only rule (forced for Scene 2's exit door by Lab.cs): only the host plays it, the client stays
                    // put, and the host's finish event runs on the client. (Magnet carrying needs Scene 1's plane, which
                    // waits behind that chapter's phone quest.)
                    ObjectInteractive plane = null;
                    foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>()) if (o != null && o.name == "Door Exit" && o.gameObject.activeInHierarchy) plane = o;
                    if (plane == null) CheckOnce("carry.acted", false, "no exit door in " + scene);
                    else { Log("Lab: host opens the exit door (actor only)"); plane.Click(); }
                }
                // Only the spoon counts: the chapter's own opening (everyone plays it) attaches a smartphone first.
                if (actorAnimation != null && labWorldAt > 0) CheckOnce("carry.acted", ActorOnly, "host performs " + actorAnimation.Split('/')[^1] + (ActorOnly ? " (actor only)" : " (rule not actor-only)"));
                if (labWorldAt > 0 && now - labWorldAt > 60) CheckOnce("carry.acted", false, "the host did not perform the door");
                // Then a carried object: the game magnets picked-up objects under the player; here the spoon's magnet is
                // put in the host's hand for 8 s and dropped beside them. The client must show it on the host's body,
                // then where the host dropped it.
                if (labWorldAt > 0 && now - labWorldAt > 20 && labMagnet == null)
                {
                    // An object nobody holds and whose magnet pulls it nowhere (the chapter's opening already hands over
                    // the spoon, pencil and scrunchy, which stream as held items by themselves).
                    foreach (Transform_Magnet m in Object.FindObjectsOfType<Transform_Magnet>(true))
                        if (labMagnet == null && m != null && !HeldBy(m, player.transform) && !Moved(m).IsChildOf(player.transform)) labMagnet = m;
                    if (labMagnet == null) CheckOnce("carry.sent", false, "no free magnet object in " + scene);
                    else
                    {
                        labMagnetParent = Moved(labMagnet).parent;
                        labMagnetWasOn = labMagnet.enabled; labMagnet.enabled = false;   // its own pull rests while held
                        labMagnet.gameObject.SetActive(true);
                        BodyLayout layout = SelfLayout(player);
                        Transform hand = layout != null ? layout.Root : player.transform;
                        Moved(labMagnet).SetParent(hand, false);
                        Moved(labMagnet).localPosition = new Vector3(0.3f, 1.0f, 0.3f);
                        labMagnetAt = now;
                        Log("Lab: host holds the spoon");
                    }
                }
                if (labMagnet != null) { string mk = SceneKeys.Key(labMagnet); foreach (string c in carrying) if (c == mk) CheckOnce("carry.sent", true, "carrying " + c.Split('/')[^1]); }
                if (labMagnet != null && labMagnetAt > 0 && now - labMagnetAt > 8)
                {
                    labMagnetAt = -1;
                    Vector3 drop = player.transform.position + player.transform.forward * 0.6f + Vector3.up * 0.8f;
                    Moved(labMagnet).SetParent(labMagnetParent, true);
                    labMagnet.active = false;   // put down: its pull to a slot on the player is off, as when the game drops it
                    labMagnet.enabled = labMagnetWasOn;
                    Moved(labMagnet).position = drop;
                    labDropAt = now;
                    Log("Lab: host dropped " + labMagnet.name + " at " + drop);
                }
                // Where the spoon really is 2 s after the drop (its own magnet may move it on every machine).
                if (labMagnet != null && labDropAt > 0 && now - labDropAt > 2)
                {
                    labDropAt = -1;
                    Vector3 p = Moved(labMagnet).position;
                    System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
                    string mk = SceneKeys.Key(labMagnet);
                    Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-drop:" + p.x.ToString("0.00", inv) + "," + p.y.ToString("0.00", inv) + "," + p.z.ToString("0.00", inv) + "," + mk)));
                }
            }
            if (labTour == "minigame" && player != null && scene == TourChapter)
            {
                if (labCards == null) labCards = Object.FindObjectOfType<Location4TableCardGame>(true);
                Location4TableCardGame cards = labCards;
                if (now - tourSceneAt > 25 && labWorldAt < 0)
                {
                    labWorldAt = now;
                    if (cards == null) CheckOnce("minigame.started", false, "no card game in " + scene);
                    else { try { cards.StartGame(); } catch (Exception ex) { Log("Lab: StartGame threw: " + ex.Message); } CheckOnce("minigame.started", OwningMinigame, OwningMinigame ? "host owns the card game" : "start was not claimed"); }
                }
                if (labWorldAt > 0 && now - labWorldAt > 20 && !tourStepped && cards != null)
                {
                    tourStepped = true;
                    CheckOnce("minigame.owned", OwningMinigame, "host owns the card game while playing");
                    try { cards.StopGame(); } catch (Exception ex) { Log("Lab: StopGame threw: " + ex.Message); }
                    Log("Lab: host finished the card game");
                }
            }
            if (labTour == "props" && player != null && scene == TourChapter && now - tourSceneAt > 25 && labWorldAt < 0)
            {
                labWorldAt = now;
                Rigidbody pick = null;
                foreach (Rigidbody rb in Props()) if (rb != null && !rb.isKinematic) { pick = rb; break; }
                if (pick == null) CheckOnce("props.sent", false, Props().Count + " props, none dynamic, in " + scene);
                else { pick.velocity = Vector3.up * 3f + player.transform.forward * 2f; labPropKey = SceneKeys.Key(pick); CheckOnce("props.sent", true, labPropKey); }
            }
            if (labTour == "death" && player != null && now - tourSceneAt > 25 && labDeathAt < 0)
            {
                labDeathAt = now;
            }
            if (labTour == "death" && labDeathAt > 0 && now - labDeathAt > 4 && !tourStepped)
            {
                tourStepped = true;
                // The mannequin chapter's checkpoint death: what happens when a mannequin catches the player.
                Location10_ManekenChekpoint checkpoint = Object.FindObjectOfType<Location10_ManekenChekpoint>(true);
                if (checkpoint == null) CheckOnce("death.sent", false, "no mannequin checkpoint in " + scene);
                else
                {
                    Log("Lab: host is caught by a mannequin");
                    try { checkpoint.PlayerDeath(); } catch (Exception ex) { Log("Lab: PlayerDeath() threw: " + ex.Message); }
                    CheckOnce("death.sent", deathsSent > 0, "PlayerDeath() relayed as " + deathsSent + " death event(s)");
                }
            }
        }

        // --- cutscene -----------------------------------------------------------------------------------
        float labCutsceneAt = -1; string labCutsceneKey;

        void HostCutsceneTour(float now, string scene)
        {
            if (scene != TourChapter || labCutsceneAt >= 0 || now - tourSceneAt < 25 || LocalPlayer == null) return;
            labCutsceneAt = now;
            PlayableDirector pick = null; double best = 0;
            foreach (PlayableDirector d in Directors())
            {
                if (d == null || d.playableAsset == null || !d.gameObject.activeInHierarchy || Playing(d)) continue;
                double length = d.duration;
                if (length > 4 && length < 120 && (pick == null || length > best)) { pick = d; best = length; }
            }
            Log("Lab: " + Directors().Count + " timelines (" + Cutscenes().Count + " Playable_Animation) in " + scene);
            if (pick == null) { CheckOnce("cutscene.sent", false, "no playable timeline in " + scene); return; }
            labCutsceneKey = SceneKeys.Key(pick);
            Playable_Animation anim = pick.GetComponent<Playable_Animation>();
            if (anim != null) anim.Play(); else pick.Play();
            CheckOnce("cutscene.sent", true, labCutsceneKey + " (" + best.ToString("0.0") + " s)");
        }

        void CutsceneHiddenCheck(float now, Peer other)
        {
            if (other == null) return;
            // Real cutscenes (cutscene mode or a camera timeline) hide the other player; any other timeline keeps them visible.
            bool someoneInCutscene = localCutscene || other.InCutscene;
            bool shown = other.Avatar != null && other.Avatar.Root != null && other.Avatar.Root.activeSelf;
            if (!someoneInCutscene)
            {
                bool timeline = false;
                foreach (PlayableDirector d in Directors()) if (Playing(d)) timeline = true;
                if (timeline && shown && now - tourSceneAt > 30) CheckOnce("cutscene.visibility", true, "other player visible during a non-camera timeline");
                return;
            }
            if (!shown) CheckOnce("cutscene.visibility", true, "other player hidden while " + (localCutscene ? "this player" : "the other player") + " is in a camera cutscene");
            else if (labCutsceneHiddenSince < 0) labCutsceneHiddenSince = now;
            else if (now - labCutsceneHiddenSince > 1.5f) CheckOnce("cutscene.visibility", false, "other player still visible during a camera cutscene");
        }

        float labCutsceneHiddenSince = -1, labGazeAt = -1, labDeathAt = -1, labWorldAt = -1;
        string labPropKey; Vector3 labPropTarget;

        // --- world: keys, hand items, props, shared randomness --------------------------------------------------
        static int RandomFingerprint()
        {
            unchecked
            {
                int h = 17, n = 0;
                foreach (Random_Position r in Object.FindObjectsOfType<Random_Position>(true))
                    if (r != null) { Vector3 p = r.transform.position; h = h * 31 + Mathf.RoundToInt(p.x * 100) * 7 + Mathf.RoundToInt(p.y * 100) * 13 + Mathf.RoundToInt(p.z * 100); n++; }
                return h * 31 + n;
            }
        }

        void HostWorldTour(float now, string scene, PlayerMove player)
        {
            if (player == null || scene != TourChapter || now - tourSceneAt < 25) return;
            if (labWorldAt < 0)
            {
                labWorldAt = now;
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-seed:" + RandomFingerprint())));
                List<ObjectInteractiveItemTake> takes = new List<ObjectInteractiveItemTake>();
                foreach (ObjectInteractiveItemTake o in Object.FindObjectsOfType<ObjectInteractiveItemTake>()) if (o != null && o.gameObject.activeInHierarchy) takes.Add(o);
                takes.Sort((a, b) => string.CompareOrdinal(SceneKeys.Key(a), SceneKeys.Key(b)));
                ObjectInteractiveItemTake item = takes.Count > 0 ? takes[0] : null;
                if (item == null) CheckOnce("items.hand.sent", false, "no take-able item in " + scene);
                else { Log("Lab: host takes " + SceneKeys.Key(item)); item.Take(); CheckOnce("items.hand.sent", true, SceneKeys.Key(item)); }
                GameController gc = Object.FindObjectOfType<GameController>();
                ObjectInteractiveItemTake key = takes.Count > 1 ? takes[1] : null;
                // Key items enter the inventory the way the game's events do it: Player_AddItem.AddItemKey.
                Player_AddItem adder = Object.FindObjectOfType<Player_AddItem>(true);
                if (key != null)
                {
                    try { if (adder != null) adder.AddItemKey(key.gameObject); else if (gc != null) gc.AddKeyItem(key.gameObject); } catch (Exception ex) { Log("Lab: key add threw: " + ex.Message); }
                    CheckOnce("items.key.sent", true, SceneKeys.Key(key) + (adder != null ? " via Player_AddItem" : " via GameController"));
                }
                else CheckOnce("items.key.sent", false, "no second take-able item to use as a key");
            }
            else if (now - labWorldAt > 10 && !tourStepped) { tourStepped = true; player.RemoveItem(); Log("Lab: host put the item down"); }
        }

        void ClientWorldTour(float now, Peer host)
        {
            if (labSeed != 0) CheckOnce("seed.match", labSeed == RandomFingerprint(), "host " + labSeed + " here " + RandomFingerprint() + " (" + Object.FindObjectsOfType<Random_Position>(true).Length + " random placements, " + seededRolls + " seeded rolls)");
            if (handItemsShown > 0)
            {
                HeldVisual h; bool inHand = held.TryGetValue(0, out h) && h.Clone != null && host.Avatar != null && h.Clone.transform.IsChildOf(host.Avatar.Root.transform);
                CheckOnce("items.hand", inHand, inHand ? "the host's item rides in the host's hand" : "item copy not under the host's body");
                if (inHand) labHeldSeen = true;
            }
            if (labHeldSeen && !held.ContainsKey(0)) CheckOnce("items.drop", true, "the item left the host's hand when the host put it down");
            if (keyItemsApplied > 0) CheckOnce("items.key", true, keyItemsApplied + " key item(s) added to the shared inventory");
            if (propsApplied > 0) CheckOnce("props.synced", true, propsApplied + " prop update(s) applied");
        }

        // --- survey (one instance): what each chapter contains, for choosing test chapters ---------------------
        int surveyIndex = -1; float surveyAt;
        void SurveyTour(float now, string scene)
        {
            if (surveyIndex >= 0 && surveyIndex < Chapters.Length && scene == Chapters[surveyIndex] && now - tourSceneAt > 12 && now - surveyAt > 12)
            {
                surveyAt = now;
                int items = Object.FindObjectsOfType<ObjectItem>(true).Length, takes = Object.FindObjectsOfType<ObjectInteractiveItemTake>(true).Length;
                string games = "";
                void Count<T>(string n) where T : Component { int k = Object.FindObjectsOfType<T>(true).Length; if (k > 0) games += n + "=" + k + " "; }
                Count<TetrisGame>("Tetris"); Count<Location4TableCardGame>("Cards"); Count<Location7_GameDance>("Dance"); Count<Location18_TicTacToe>("TicTacToe");
                Count<Location14_PCSnaker>("Snake"); Count<Location14_Saper>("Mines"); Count<MinigamesTelevisionGame>("TV"); Count<CarSpace_Main>("CarSpace");
                Count<Shooter_Main>("Shooter"); Count<MakeManeken_Main>("Maneken"); Count<Tamagotchi_Main>("Tamagotchi"); Count<Location19_Game1>("G19a");
                Count<Location19_Game4>("G19d"); Count<MT_GameFly>("Fly"); Count<MT_GameCnowballs>("Snowballs"); Count<QuadLinerMain>("QuadLiner");
                Count<Location6_MitaKiller>("MitaKiller"); Count<Mob_Maneken>("Mannequin"); Count<Location10_ManekenChekpoint>("ManekenCheckpoint");
                Log("Lab: survey " + scene + " items=" + items + " takes=" + takes + " props=" + Props().Count + " timelines=" + Directors().Count + " npcs=" + Npcs().Count + " | " + games);
                surveyIndex++;
                if (surveyIndex < Chapters.Length) LoadThroughGame(Chapters[surveyIndex]); else CheckOnce("survey.done", true, "");
            }
            if (surveyIndex < 0 && scene == "SceneMenu" && now - tourSceneAt > 5) { surveyIndex = 0; LoadThroughGame(Chapters[0]); }
        }

        // --- native menu and Steam ------------------------------------------------------------------------------
        float labMenuOpenAt = -1;
        void MenuChecks(float now, string scene)
        {
            if (scene != "SceneMenu" || now - tourSceneAt < 6) return;
            if (!labChecked.Contains("steam.ready") && (Steam.Ready || now - tourSceneAt > 20)) CheckOnce("steam.ready", Steam.Ready && Steam.Self != 0, Steam.Ready ? "Steam user " + Steam.Self + " (" + Steam.SelfName + "), " + Steam.FriendsInGame().Count + " friend(s) in MiSide" : Steam.Problem);
            if (!MenuBuilt) { if (now - tourSceneAt > 30) CheckOnce("ui.menu", false, "MULTIPLAYER page was not built"); return; }
            MenuNextLocation next = menuButton.GetComponent<MenuNextLocation>();
            CheckOnce("ui.menu", next != null && next.nextLocation == menuPage, "MULTIPLAYER button opens the page; page rows " + MenuRowCount);
            if (labMenuOpenAt < 0 && next != null && Connected) { labMenuOpenAt = now; next.Click(); }
            if (labMenuOpenAt > 0 && now - labMenuOpenAt > 2)
            {
                string texts = "";
                foreach (GameObject row in rows) if (row != null) foreach (UnityEngine.UI.Text t in row.GetComponentsInChildren<UnityEngine.UI.Text>(true)) texts += t.text + "; ";
                bool ok = menuPage.activeInHierarchy && rows.Count > 0 && texts.Contains(Hosting ? "STOP HOSTING" : "LEAVE");
                CheckOnce("ui.menu.open", ok, (menuPage.activeInHierarchy ? "page open: " : "page did not open: ") + texts);
            }
        }

        // Every teleport must end on the floor at the target's height with head-room: never in furniture or above a ceiling.
        void TeleportSafeCheck(float now)
        {
            PlayerMove player = LocalPlayer;
            if (player == null || lastTeleportAt < 0 || now - lastTeleportAt < 1.5f || now - lastTeleportAt > 3f) return;
            Vector3 feet = player.transform.position;
            bool level = Mathf.Abs(feet.y - lastTeleportHostY) < 0.3f, headroom = true, floor = true;
            try
            {
                headroom = !Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.up, 1.4f, ~0, QueryTriggerInteraction.Ignore);
                floor = Physics.Raycast(feet + Vector3.up * 0.4f, Vector3.down, 0.8f, ~0, QueryTriggerInteraction.Ignore);
            }
            catch { }
            lastTeleportAt = -1;
            // Height and head-room catch the real failures (ceiling, furniture). Some chapters have no collider under the
            // host's own feet, so floor is reported but not required.
            bool ok = level && headroom;
            if (!ok || !labChecked.Contains("teleport.safe"))
            {
                labChecked.Remove("teleport.safe");
                CheckOnce("teleport.safe", ok, "after teleport: height diff " + (feet.y - lastTeleportHostY).ToString("0.00") + " m, head-room " + headroom + ", floor " + floor);
            }
        }

        bool labWalkedOff, labWasWatching;
        string labMigrateScene;

        // --- novella: the visual novel reads on together --------------------------------------------------------
        int labNovellaClicks; float labNovellaAt = -1; string labNovellaStart;

        string NovellaPos(Location18_Novella n) { return n == null || n.dialoguePlay == null ? "" : SceneKeys.Key(n.dialoguePlay).Split('/')[^1] + "#" + n.indexDialogue + "." + n.indexStringDialogue; }

        void NovellaHost(float now, string scene)
        {
            if (!tourStarted && scene == "SceneMenu" && now - tourSceneAt > 6) { tourStarted = true; Log("Lab: host loading " + TourChapter); LoadThroughGame(TourChapter); }
            if (scene != TourChapter) return;
            Location18_Novella n = Novella();
            if (n == null || n.dialoguePlay == null || now - tourSceneAt < 25) return;
            if (labNovellaStart == null) { labNovellaStart = NovellaPos(n); labNovellaAt = now; }
            if (labNovellaClicks < 8 && now - labNovellaAt > 2.5f)
            {
                labNovellaAt = now; labNovellaClicks++;
                try { n.NextDialogue(); } catch (Exception ex) { Log("Lab: NextDialogue threw: " + ex.Message); }
                Log("Lab: host reads on (" + labNovellaClicks + ") at " + NovellaPos(n));
                if (labNovellaClicks == 8) CheckOnce("novella.read", NovellaPos(n) != labNovellaStart, "from " + labNovellaStart + " to " + NovellaPos(n));
            }
            if (novellaClicks > 0) CheckOnce("novella.clientclick", true, "a client's click moved the host on (" + novellaClicks + ")");
        }

        void NovellaClient(float now, string scene)
        {
            Location18_Novella n = Novella();
            if (scene != TourChapter || n == null || n.dialoguePlay == null || hostNovellaAt < 0) return;
            string mine = SceneKeys.Key(n.dialoguePlay);
            if (labNovellaStart == null) labNovellaStart = hostNovellaDialogue + hostNovellaPage + "." + hostNovellaLine;
            bool hostMoved = hostNovellaDialogue + hostNovellaPage + "." + hostNovellaLine != labNovellaStart;
            if (hostMoved && NovellaOrder(n, mine) == 0) CheckOnce("novella.synced", true, "level with the host at " + NovellaPos(n) + " (" + novellaSteps + " catch-up steps, " + novellaJumps + " jumps)");
            // Then this player reads on once: the host must follow.
            if (labChecked.Contains("novella.synced") && labNovellaClicks == 0 && now - tourSceneAt > 60)
            {
                labNovellaClicks++;
                try { n.NextDialogue(); } catch (Exception ex) { Log("Lab: NextDialogue threw: " + ex.Message); }
                Log("Lab: client reads on at " + NovellaPos(n));
            }
        }

        // --- lobby (4 players): no joining mid-chapter -----------------------------------------------------------
        // C3 joins while the others are in Scene 1: it must wait in the menu, then come along to Scene 2 with everyone.
        const string LobbyNext = "Scene 2 - InGame";
        bool labLobbyMoved;
        float labLobbyHeldAt = -1;

        bool LobbyHost(float now, string scene)
        {
            bool held = false; foreach (Peer p in peers.Values) if (p.Waiting) held = true;
            if (held && CheckOnce("lobby.held", scene == TourChapter, "a player joining in " + scene + " waits for the next chapter")) labLobbyHeldAt = now;
            // The waiting player gets 8 s in the menu before the next chapter, so its own check sees the wait.
            if (!labLobbyMoved && held && scene == TourChapter && now - tourSceneAt > 15 && labLobbyHeldAt > 0 && now - labLobbyHeldAt > 8)
            {
                labLobbyMoved = true;
                Log("Lab: host starts the next chapter");
                LoadThroughGame(LobbyNext);
            }
            if (scene == LobbyNext && LocalPlayer != null)
            {
                int there = 0; foreach (Peer p in peers.Values) if (p.Accepted && p.HasPose && p.SceneHash == LocalSceneHash) there++;
                if (there >= MaxPlayers - 1) CheckOnce("lobby.together", true, "all " + MaxPlayers + " players in " + scene);
            }
            return scene == LobbyNext || labLobbyMoved;
        }

        void LobbyClient(string scene)
        {
            if (waitingForChapter) CheckOnce("lobby.waited", scene == "SceneMenu", "held in " + scene + " while the host is mid-chapter");
            if (scene == LobbyNext && LocalPlayer != null) CheckOnce("lobby.arrived", true, "in " + scene + " with the host");
            if (Name == "C3" && scene == TourChapter) CheckOnce("lobby.nomid", false, "joined mid-chapter into " + scene);
        }
        float labMirrorAt = -1, labDiagAt;
        Location4TableCardGame labCards;
        Transform_Magnet labMagnet; Transform labMagnetParent; float labMagnetAt = -1; bool labMagnetWasOn;
        Vector3 labDrop; bool labDropKnown, labSawCarried; string labDropKey;
        readonly HashSet<string> labCarriedSeen = new HashSet<string>(); float labDropAt = -1, labCarriedSince = -1;

        // After the host drops: the lowest-numbered client hosts in place, the other joins it, nobody reloads.
        void MigrateChecks(float now, string scene)
        {
            if (labMigrateScene == null && migrations == 0 && scene != "SceneMenu" && FollowableScene(scene)) labMigrateScene = scene;
            if (migrations == 0) return;
            if (Hosting && AcceptedCount >= 1) CheckOnce("migrate.host", true, "became the host; " + AcceptedCount + " player(s) reconnected");
            if (IsClient && Connected && peers.ContainsKey(0)) CheckOnce("migrate.joined", true, "joined the new host " + peers[0].Name);
            if ((Hosting && AcceptedCount >= 1) || (IsClient && Connected)) CheckOnce("migrate.scene", scene == labMigrateScene, "still in " + scene + " (was " + labMigrateScene + ")");
        }
        string labRoomMove, labRoomOff, labRoomLight, labRoomNotice;
        float labRoomNoticeAt;
        string labDialogueKey;
        bool labHeldSeen;
        public int labSeed;

        void ClientTour(float now, string scene)
        {
            if (labTour == "migrate") { MigrateChecks(now, scene); return; }
            if (labTour == "chapters") { ClientChapters(now, scene); return; }
            if (labTour == "lobby") { LobbyClient(scene); return; }
            if (labTour == "novella") { NovellaClient(now, scene); return; }
            if (labTour == "interactions") { InteractionsClient(now); return; }
            if (labTour == "triggers") { TriggersClient(); return; }
            if (labTour == "hostcut") { HostCutClient(now, scene); return; }
            if (labTour == "phone") { PhoneClient(now, scene); return; }
            if (labTour == "scene19") { Scene19Checks(scene); return; }
            if (wantedScene.Length == 0 || scene != wantedScene || wantedScene == "SceneMenu") return;
            PlayerMove player = LocalPlayer;
            if (ClientStoryLive && player != null) { CheckOnce("follow.scene", true, scene); CheckOnce("follow.player", true, scene); }
            else if (now - tourSceneAt > 60) { CheckOnce("follow.player", false, "no PlayerMove 60 s after " + scene + " loaded"); }
            Peer host;
            if (!peers.TryGetValue(0, out host)) return;
            if ((labTour == "avatars" || labTour == "mita") && player != null && !tourMoved && now - tourSceneAt > 8 && now >= tourTeleportAt && replayQueue.Count == 0 && host.HasPose && host.SceneHash == LocalSceneHash)
            { tourMoved = TryTeleportToHost(false); tourTeleportAt = now + 2; }
            if (labTour == "avatars") AvatarChecks(now, host, 60);
            if (labTour == "cutscene") CutsceneHiddenCheck(now, host);
            if (labTour == "avatars" && labStepAt > 0)
            {
                float tourStepAt = labStepAt;
                float gap = Vector3.Distance(host.Avatar != null && host.Avatar.Root != null ? host.Avatar.Root.transform.position : Vector3.zero, host.Position);
                if (now - tourStepAt > 2 && gap < 0.3f) CheckOnce("avatar.follows", true, "avatar within " + gap.ToString("0.00") + " m of the host's new position");
                else if (now - tourStepAt > 10) CheckOnce("avatar.follows", false, "avatar still " + gap.ToString("0.00") + " m from the host's new position");
            }
            if (labTour == "story" && LastAppliedStory != null && CheckOnce("story.applied", true, LastAppliedStory))
            {
                // Reverse direction: the client interacts and the host must receive it.
                ObjectInteractive pick = PickInteractive();
                if (pick != null) { Log("Lab: client clicks " + SceneKeys.Key(pick)); pick.Click(); CheckOnce("story.clientsent", true, SceneKeys.Key(pick)); }
                else CheckOnce("story.clientsent", false, "no active ObjectInteractive on the client");
            }
            if (labTour == "world" || labTour == "props") ClientWorldTour(now, host);
            if (labTour == "room" && labRoomNotice != null && now - labRoomNoticeAt > 3)
            {
                string[] k = labRoomNotice.Split('|');
                Transform moved = SceneKeys.Find<Transform>(k[0]), off = SceneKeys.Find<Transform>(k[1]);
                Light lit = k.Length > 2 && k[2].Length > 0 ? SceneKeys.Find<Light>(k[2]) : null;
                CheckOnce("room.active", off != null && !off.gameObject.activeSelf, off == null ? "object not found" : "switched-off object is off here too");
                CheckOnce("room.place", moved != null && stateApplied > 0, moved == null ? "object not found" : "moved object at " + moved.localPosition + " (" + stateApplied + " room changes applied)");
                if (k.Length > 2 && k[2].Length > 0) CheckOnce("room.light", lit != null && lit.color.g < 0.3f, lit == null ? "light not found" : "light colour here " + lit.color);
            }
            if (labTour == "mirror" && player != null && peers.ContainsKey(0))
            {
                Peer actor = peers[0];
                if (player.animationRun && actor.Acting)
                {
                    labWasWatching = true;
                    Vector3 d = actor.Feet - player.transform.position; d.y = 0;
                    CheckOnce("mirror.played", !Spectating, "this player plays the same interaction on their own character");
                    if (player.animationRun && Time.realtimeSinceStartup - labMirrorAt > 1.5f && labMirrorAt > 0) CheckOnce("mirror.placed", d.magnitude < 1.2f, "at the host's spot (" + d.magnitude.ToString("0.00") + " m apart)");
                    if (labMirrorAt < 0) labMirrorAt = Time.realtimeSinceStartup;
                    if (d.magnitude < 0.5f) CheckOnce("mirror.overlap", !PeerVisible(actor), "the host's body is not drawn over this player's");
                }
                if (labWasWatching && !player.animationRun && !actor.Acting) CheckOnce("mirror.end", LocalFree(player) || !player.dontMove, "own controls back after the interaction");
            }
            if (labTour == "gather" && player != null)
            {
                if (host.InCutscene && !labWalkedOff)
                {
                    labWalkedOff = true;
                    bool moved = false;
                    for (int a = 0; a < 16 && !moved; a++)
                        foreach (float dist in new[] { 4.5f, 3.6f })
                        {
                            Vector3 dir = Quaternion.Euler(0, a * 22.5f, 0) * Vector3.forward, spot;
                            if (!ClearBeside(player.transform.position, dir, dist, out spot)) continue;
                            player.TeleportPlayer(spot); moved = true;
                            Log("Lab: client walked " + dist + " m away during the host's cutscene");
                            break;
                        }
                    if (!moved) Log("Lab: no clear 3.6 m path from here; cannot walk off");
                }
                if (gathers > 0) CheckOnce("gather.done", (player.transform.position - host.Feet).magnitude < 2f, "brought back to the host after the cutscene (" + (player.transform.position - host.Feet).magnitude.ToString("0.0") + " m)");
            }
            if (labTour == "dialogue")
            {
                if (dialogueStarted > 0) CheckOnce("dialogue.started", true, dialogueStarted + " host line(s) started here");
                if (dialogueSkipped > 0) CheckOnce("dialogue.skipped", true, "a line the host finished was skipped here");
                if (dialogueLagCount >= 1) CheckOnce("dialogue.lag", dialogueLagMax < 1.0f, dialogueLagCount + " lines started here at most " + dialogueLagMax.ToString("0.00") + " s after the host (mean " + (dialogueLagSum / dialogueLagCount).ToString("0.00") + " s)");
            }
            if (labTour == "carry" && player != null && host.Acting && Time.realtimeSinceStartup - host.PoseAt < 1f && labChecked.Contains("follow.player"))
            {
                labWasWatching = true;
                if (Time.realtimeSinceStartup - tourSceneAt > 40) CheckOnce("carry.still", !player.animationRun, player.animationRun ? "this player's character also played it" : "this player's character stayed put while the host acted");
            }
            if (labTour == "carry")
            {
                foreach (string k in carried.Keys) labCarriedSeen.Add(k);
                labSawCarried = labDropKey != null && labCarriedSeen.Contains(labDropKey);
                if (labSawCarried && labDropKnown)
                {
                    Transform_Magnet spoon = labDropKey != null ? SceneKeys.Find<Transform_Magnet>(labDropKey) : null;
                    bool still = false; foreach (string k in carried.Keys) if (k == labDropKey) still = true;
                    if (spoon != null && !still) CheckOnce("carry.released", Vector3.Distance(Moved(spoon).position, labDrop) < 0.5f, "dropped at " + Moved(spoon).position + " (host dropped it at " + labDrop + ")");
                }
                foreach (KeyValuePair<string, Carried> pair in carried)
                {
                    if (pair.Key.Contains("Smartphone") || Time.realtimeSinceStartup - pair.Value.Since < 1f || pair.Value.Magnet == null || host.Avatar == null || host.Avatar.Root == null) continue;
                    float d = Vector3.Distance(Moved(pair.Value.Magnet).position, host.Avatar.Root.transform.position);
                    CheckOnce("carry.mirrored", d < 2.5f, pair.Key + " is " + d.ToString("0.00") + " m from the host's body");
                    if (player != null) CheckOnce("carry.still", !player.animationRun, player.animationRun ? "this player's character also played the interaction" : "this player's character stayed put");
                }
                if (interactionsFinished > 0) CheckOnce("carry.finished", true, "the interaction's finish event ran here");
            }
            if (labTour == "minigame")
            {
                // The host plays, this player watches; when the host finishes, the game's end event runs here too.
                bool hostPlays = false; foreach (MinigameRun r in minigames.Values) if (r.Owner == 0) hostPlays = true;
                if (hostPlays) { labWasWatching = true; CheckOnce("minigame.watch", !OwningMinigame && !Spectating, "the host plays; this player watches in their own view"); }
                if (labWasWatching && !hostPlays && minigameEndsApplied > 0) CheckOnce("minigame.end", player != null && !player.dontMove, "the card game's end event ran here (" + minigameEndsApplied + "); controls free");
            }
            if (labTour == "death" && deathsApplied > 0) CheckOnce("death.restart", true, "the host's death replayed here (" + deathsApplied + ")");
            if (labTour == "mita" && faceApplied > 0) CheckOnce("mita.face", true, faceApplied + " face change(s) replayed from the host");
            if (labTour == "mita" && labGazeAt < 0 && now - tourSceneAt > 30 && player != null && Mitas().Count > 0)
            {
                // Stand right beside Mita (same safe rule as Go to the host), so this client is nearer to her than the host.
                Transform m = Mitas()[0].transform;
                Vector3 spot = m.position;
                foreach (Vector3 dir in new[] { m.forward, m.right, -m.right, -m.forward }) if (ClearBeside(m.position, dir, out spot)) break;
                player.TeleportPlayer(spot);
                lastTeleportAt = now; lastTeleportHostY = m.position.y;
                labGazeAt = now;
                Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-gaze")));
            }
            if (labTour == "cutscene")
            {
                PlayableDirector seen = null; string seenKey = null;
                foreach (PlayableDirector d in Directors()) if (Playing(d) && lastCutsceneSync.StartsWith(SceneKeys.Key(d) + " @", StringComparison.Ordinal)) { seen = d; seenKey = SceneKeys.Key(d); break; }
                if (seen != null && lastCutsceneSync.Length > 0) CheckOnce("cutscene.started", true, seenKey);
                if (seen != null && lastCutsceneSync.StartsWith(seenKey + " @", StringComparison.Ordinal))
                {
                    double hostTime = double.Parse(lastCutsceneSync.Substring(seenKey.Length + 2));
                    double drift = Math.Abs(seen.time - hostTime);
                    if (drift < 0.3 && now - tourSceneAt > 30) CheckOnce("cutscene.synced", true, "timeline within " + drift.ToString("0.000") + " s of the host (" + cutsceneCorrections + " correction(s))");
                }
            }
            if (labTour == "mita" && now - tourSceneAt > 15)
            {
                float desync = MitaDesync();
                if (desync >= 0 && desync < 0.5f) CheckOnce("mita.synced", true, "Mita within " + desync.ToString("0.00") + " m of the host's");
                else if (now - tourSceneAt > 70) CheckOnce("mita.synced", false, desync < 0 ? "no Mita pose received" : "Mita " + desync.ToString("0.00") + " m away from the host's");
            }
        }

        // The other player's body: a real character model, close by, whose animator plays the sender's state.
        void AvatarChecks(float now, Peer peer, float deadline)
        {
            PlayerMove player = LocalPlayer;
            if (player == null || now - tourSceneAt < 12) return;
            RemoteAvatar avatar = peer.Avatar;
            bool shown = avatar != null && avatar.Root != null && avatar.Root.activeSelf;
            bool late = now - tourSceneAt > deadline;
            if (shown && avatar.IsModel) CheckOnce("avatar.model", true, "copy of the player body with " + avatar.Root.GetComponentsInChildren<SkinnedMeshRenderer>().Length + " skinned meshes");
            // Props and outfit parts (the Game Boy...) show on the copy exactly when they show on the sender's own body.
            if (shown && avatar.IsModel && avatar.Layout != null && peer.Visible != null)
            {
                int wrong = 0, props = 0; string first = "";
                foreach (KeyValuePair<string, Renderer> pair in avatar.Layout.Renderers)
                {
                    if (pair.Value == null || avatar.Layout.BodyPaths.Contains(pair.Key)) continue;
                    props++;
                    if (pair.Value.enabled != peer.Visible.Contains(pair.Key)) { wrong++; if (first.Length == 0) first = pair.Key; }
                }
                CheckOnce("avatar.props", wrong == 0, wrong == 0 ? props + " prop/outfit renderers mirror the sender" : wrong + " of " + props + " props differ, first '" + first + "'");
            }
            // The copy's skeleton holds the sender's bone pose (IK arms included): mean angle error across all bones.
            if (shown && avatar.IsModel && avatar.Layout != null && peer.Bones.Count > 0 && Time.realtimeSinceStartup - peer.BonesAt < 0.3f)
            {
                if (peer.Bones.Hash != avatar.Layout.Hash) { if (late) CheckOnce("avatar.bones", false, "skeleton layout differs: sender " + peer.Bones.Hash + " copy " + avatar.Layout.Hash); }
                else
                {
                    float sum = 0; int n = 0;
                    for (int i = 0; i < peer.Bones.Count; i++)
                        if (avatar.Layout.Bones[i] != null) { sum += Quaternion.Angle(avatar.Layout.Bones[i].localRotation, peer.Bones.Rotations[i]); n++; }
                    float mean = n > 0 ? sum / n : 999;
                    if (mean < 3f) CheckOnce("avatar.bones", true, n + " bones, mean error " + mean.ToString("0.00") + " deg");
                    else if (late) CheckOnce("avatar.bones", false, "mean bone error " + mean.ToString("0.00") + " deg over " + n + " bones");
                }
            }
            else if (late) CheckOnce("avatar.bones", false, "no bone frames received");
            else if (late) CheckOnce("avatar.model", false, shown ? "capsule fallback only" : "no avatar shown");
            // Body to body: poses carry the animated body's position, which intros move away from the PlayerMove root.
            Vector3 mine = player.animPerson != null ? player.animPerson.transform.position : player.transform.position;
            float dist = peer.HasPose ? Vector3.Distance(mine, peer.Position) : -1;
            if (shown && dist >= 0 && dist < 4) CheckOnce("avatar.near", true, dist.ToString("0.0") + " m");
            else if (late) CheckOnce("avatar.near", false, "distance " + dist.ToString("0.0"));
            if (shown && avatar.IsModel && avatar.Anim.Animator != null && peer.Anim.StateHash != 0)
            {
                int playing = avatar.Anim.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
                if (playing == peer.Anim.StateHash) CheckOnce("avatar.anim", true, "state " + playing + " clip '" + peer.Anim.Clip + "'");
            }
            if (late) CheckOnce("avatar.anim", false, "sender state " + peer.Anim.StateHash + " not playing on the copy");
            if (peer.Anim.ParamHashes.Count > 0) CheckOnce("avatar.params", true, peer.Anim.ParamHashes.Count + " animator parameters synced");
            else if (late) CheckOnce("avatar.params", false, "no animator parameters received; unavailable APIs: " + AnimSync.Status);
        }

        // Check names carry no spaces (verify-lab.py splits on whitespace).
        static string Slug(string scene) { return scene.Replace(' ', '_'); }

        Peer FirstClient() { foreach (Peer p in peers.Values) if (p.Accepted) return p; return null; }

        bool ClientInScene(Peer p) { return p != null && p.HasPose && p.SceneHash == LocalSceneHash; }

        ObjectInteractive PickInteractive()
        {
            List<ObjectInteractive> list = new List<ObjectInteractive>();
            foreach (ObjectInteractive o in Object.FindObjectsOfType<ObjectInteractive>())
                if (o != null && o.isActiveAndEnabled && o.active && (LastAppliedStory == null || !LastAppliedStory.EndsWith(SceneKeys.Key(o), StringComparison.Ordinal))) list.Add(o);
            list.Sort((a, b) => string.CompareOrdinal(SceneKeys.Key(a), SceneKeys.Key(b)));
            Log("Lab: " + list.Count + " candidate interactives");
            // Host and client pick different objects so both directions are tested.
            int index = Hosting ? 0 : list.Count - 1;
            return list.Count > 0 ? list[index] : null;
        }

        // --- chapters ---------------------------------------------------------------------------------
        bool chaptersDone;

        void HostChapters(float now, string scene)
        {
            if (chaptersDone) return;
            if (chapterIndex < 0)
            {
                if (scene == "SceneMenu" && now - tourSceneAt > 6) NextChapter(now);
                return;
            }
            string target = Chapters[chapterIndex];
            // Loaded = the chapter is up without game errors piling up (Scene 18 is 2D and has no PlayerMove).
            if (scene == target && now - tourSceneAt > 10)
                CheckOnce("chapter.loaded:" + Slug(target), errorCount - chapterErrorsAtLoad < 20, "player=" + (LocalPlayer != null) + " game errors since load " + (errorCount - chapterErrorsAtLoad));
            if (returningToMenu)
            {
                // Back in the menu between chapters, like a player picking the next one from Chapters.
                if (scene == "SceneMenu" && now - tourSceneAt > 4) { returningToMenu = false; NextChapter(now); }
                return;
            }
            // Stay ChapterSeconds in each chapter once it is up (so the client can follow and look), at most 150 s in all.
            bool stayed = scene == target && now - tourSceneAt > ChapterSeconds;
            if (!stayed && now - tourChapterAt < 150) return;
            if (!labChecked.Contains("chapter.loaded:" + Slug(target)))
                CheckOnce("chapter.loaded:" + Slug(target), false, "scene=" + scene + " after 150 s");
            if (chapterIndex + 1 >= Chapters.Length) { NextChapter(now); return; }
            returningToMenu = true;
            Log("Lab: host back to the menu");
            LoadThroughGame("SceneMenu");
        }

        bool returningToMenu;
        int chapterErrorsAtLoad;

        void NextChapter(float now)
        {
            chapterIndex++;
            if (chapterIndex >= Chapters.Length) { chaptersDone = true; CheckOnce("chapters.done", true, Chapters.Length + " chapters"); return; }
            tourChapterAt = now;
            chapterErrorsAtLoad = errorCount;
            Log("Lab: host loading chapter " + Chapters[chapterIndex]);
            LoadThroughGame(Chapters[chapterIndex]);
        }

        void ClientChapters(float now, string scene)
        {
            if (wantedScene.Length == 0 || wantedScene == "SceneMenu" || scene != wantedScene) return;
            PlayerMove player = LocalPlayer;
            Peer host;
            if (!ClientStoryLive) return;
            CheckOnce("follow:" + Slug(scene), true, player != null ? "" : "no 3D player in this chapter");
            if (player == null || !peers.TryGetValue(0, out host)) return;
            if (!tourMoved && now - tourSceneAt > 6 && now >= tourTeleportAt && host.HasPose && host.SceneHash == LocalSceneHash) { tourMoved = TryTeleportToHost(false); tourTeleportAt = now + 2; }
            if (host.Avatar != null && host.Avatar.Root != null && host.Avatar.Root.activeSelf && now - tourSceneAt > 10)
                CheckOnce("avatar:" + Slug(scene), host.Avatar.IsModel, host.Avatar.IsModel ? "model" : "capsule fallback");
        }
    }
}
