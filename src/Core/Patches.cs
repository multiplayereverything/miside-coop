using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MiSideTogether
{
    // Every game hook in one table (documented in AGENTS.md). Patch methods are thin stubs into TogetherRuntime.
    public static class ModHooks
    {
        static HarmonyLib.Harmony harmony;
        static int applied, failed;

        public static void InstallPatches()
        {
            if (harmony != null) return;
            harmony = new HarmonyLib.Harmony("local.misidetogether.multiplayer");
            Patch(typeof(ObjectInteractive), "Click", "InteractPrefix", "InteractPostfix");
            Patch(typeof(Trigger_MouseClick), "Click", null, "MouseClickPostfix");
            Patch(typeof(Trigger_Event), "TargetEnter", "TriggerEnterPrefix", "TriggerEnterPostfix");
            Patch(typeof(Trigger_Event), "TargetExit", "TriggerExitPrefix", "TriggerExitPostfix");
            Patch(typeof(Trigger_Event), "Restart", null, "TriggerRestartPostfix");
            Patch(typeof(DialogueChanger), "ClickButton", null, "DialogueChoicePostfix", typeof(int));
            Patch(typeof(GameController), "Pause", null, "PausePostfix", typeof(bool), typeof(bool));
            Patch(typeof(Scene_Load), "StartLoad", null, "SceneLoadPostfix");
            Patch(typeof(Scene_Load), "GoScene", null, "SceneGoPostfix");
            Patch(typeof(Playable_Animation), "Play", "CutscenePlayPrefix", "CutscenePlayPostfix");
            Patch(typeof(PlayerMove), "AnimationPlay", "PlayerAnimationPrefix", null);
            Patch(typeof(PlayerPerson), "EventKey", null, "AnimEventPostfix", typeof(int));
            Patch(typeof(PlayerMove), "AnimationHandPlay", "PlayerHandAnimationPrefix", null);
            Patch(typeof(ObjectAnimationPlayer), "AnimationPlay", null, "ObjectAnimationPostfix");
            Patch(typeof(ObjectAnimationPlayer), "AnimationPlayFast", null, "ObjectAnimationFastPostfix");
            Patch(typeof(ObjectAnimationPlayer), "AnimationPlayOnPlayer", null, "ObjectAnimationOnPlayerPostfix");
            Patch(typeof(GameController), "AddKeyItem", "KeyItemPrefix", "KeyAddPostfix", typeof(GameObject));
            Patch(typeof(GameController), "RemoveKeyItem", "KeyItemPrefix", "KeyRemovePostfix", typeof(GameObject));
            Patch(typeof(PlayerMove), "TakeItem", "TakeItemPrefix", "TakeItemPostfix", typeof(GameObject));
            Patch(typeof(PlayerMove), "RemoveItem", "RemoveItemPrefix", "RemoveItemPostfix");
            Patch(typeof(Dialogue_3DText), "SkipDialogue", null, "SkipDialoguePostfix");
            Patch(typeof(Dialogue_3DText), "Start", null, "DialogueStartPostfix");
            Patch(typeof(Location18_Novella), "NextDialogue", null, "NovellaNextPostfix");
            Patch(typeof(Trigger_MouseEvent), "Enter", null, "LookEnterPostfix");
            Patch(typeof(Trigger_MouseEvent), "Update", "LookUpdatePrefix", "LookUpdatePostfix");
            Patch(typeof(Trigger_DistanceCircle), "Update", "DistancePrefix", "DistancePostfix");
            Patch(typeof(Basement_Safe), "ClickButton", null, "SafeButtonPostfix");
            Patch(typeof(InputKeyboard_Main), "AddKey", null, "KeyboardAddPostfix");
            Patch(typeof(InputKeyboard_Main), "RemoveKey", null, "KeyboardRemovePostfix");
            Patch(typeof(InputKeyboard_Main), "ButtonEnter", null, "KeyboardEnterPostfix");
            Patch(typeof(InputKeyboard_Main), "ButtonUpper", null, "KeyboardUpperPostfix");
            Patch(typeof(Trigger_DistanceCamera), "Enter", null, "CameraEnterPostfix");
            Patch(typeof(Trigger_DistanceCamera), "Exit", null, "CameraExitPostfix");
            Patch(typeof(Location14_GameQuestPlayer), "ClickMouse", "QuestClickPrefix", "QuestClickPostfix");
            Patch(typeof(Location14_PlayerQuest), "Move", null, "QuestMovePostfix");
            Patch(typeof(Location14_PlayerMobileMove), "Play", "MobilePlayPrefix", "MobilePlayPostfix");
            Patch(typeof(Tamagotchi_Main), "GameStart", null, "AppStartPostfix");
            Patch(typeof(Tamagotchi_Main), "GameStop", null, "AppStopPostfix");
            Patch(typeof(Tamagotchi_Dialogue), "KeyNextDialogue", "AppReadPrefix", null);
            Patch(typeof(Location1_InputNamePlayer), "CheckName", "AppNamePrefix", "AppNamePostfix");
            Patch(typeof(ButtonMouseClick), "PointerDown", "AppButtonPrefix", "AppButtonDownPostfix");
            Patch(typeof(ButtonMouseClick), "PointerUp", "AppButtonPrefix", "AppButtonUpPostfix");
            Patch(typeof(TamagotchiGame_Cooking), "Ready", null, "AppCookedPostfix");
            Patch(typeof(UnityEngine.UI.Button), "Press", "AppUiButtonPrefix", "AppUiButtonPostfix");
            Patch(typeof(Location14_PlayerMobileMove), "Update", "MobileRunPrefix", null);
            Patch(typeof(Location14_PlayerMobileMove), "FixedUpdate", "MobileRunPrefix", null);
            Patch(typeof(Location14_ButtonChangeDialogue), "OnPointerDown", "QuestReplyPrefix", "QuestReplyPostfix");
            Patch(typeof(GamesCore_InputField), "ButtonClick", "CoreClickPrefix", null);
            Patch(typeof(GamesCore_InputField), "ButtonBackspace", "CoreBackspacePrefix", null);
            Patch(typeof(GamesCore_InputField), "ButtonEnter", "CoreEnterPrefix", null);
            Patch(typeof(GamesCore_Main), "OpenFolder", null, "CoreFolderPostfix");
            Patch(typeof(GamesCore_Main), "StartFolders", null, "CoreStage0Postfix");
            Patch(typeof(GamesCore_Main), "StartVersionMita", null, "CoreStage1Postfix");
            Patch(typeof(GamesCore_Main), "StartIndex", null, "CoreStage2Postfix");
            Patch(typeof(GamesCore_Main), "StopProgramma", null, "CoreStage3Postfix");
            Patch(typeof(Location6_MitaKiller), "Kill", "DeathMitaKillerPostfix", null);
            Patch(typeof(Location10_ManekenChekpoint), "PlayerDeath", "DeathManekenCheckpointPostfix", null);
            Patch(typeof(Location11_Lift), "DeathPlayer", "DeathLiftPostfix", null);
            Patch(typeof(Location11_LampMove), "RestartPlayer", "DeathLampMovePostfix", null);
            Patch(typeof(Location20_Arena), "PlayerDeath", "DeathArenaPostfix", null);
            Patch(typeof(Location20_RunCorridor), "KillPlayerStart", "DeathRunCorridorPostfix", null);
            Patch(typeof(Mob_Maneken), "StartKillPlayer", "DeathManekenPostfix", null);
            Patch(typeof(Shooter_Main), "PlayerDeath", "DeathShooterPostfix", null);
            Patch(typeof(Location19_Game4), "Death", "DeathGame4Postfix", null);
            Patch(typeof(Random_Position), "Start", "SeedPrefix", "SeedPostfix");
            Patch(typeof(Random_Position), "RandomPosition", "SeedPrefix", "SeedPostfix");
            Patch(typeof(Material_Random), "Start", "SeedPrefix", "SeedPostfix");
            Patch(typeof(Material_Random), "RechangeRandom", "SeedPrefix", "SeedPostfix");
            Patch(typeof(Animator_RandomPlay), "OnEnable", "SeedPrefix", "SeedPostfix");
            Patch(typeof(Transform_StartRandom), "Start", "SeedPrefix", "SeedPostfix");
            Patch(typeof(Location21_DialogueRandom), "ChangeRandom", "SeedPrefix", "SeedPostfix");
            Patch(typeof(Location21_DialogueRandom), "ChangeRandomDestroyOther", "SeedPrefix", "SeedPostfix");
            Patch(typeof(MitaPerson), "FaceEmotion", null, "FaceEmotionPostfix", typeof(string));
            Patch(typeof(MitaPerson), "FaceEmotionFast", null, "FaceEmotionFastPostfix", typeof(string));
            Patch(typeof(MitaPerson), "FaceEmotionOff", null, "FaceEmotionOffPostfix");
            Patch(typeof(MitaPerson), "FaceEmotionFastOff", null, "FaceEmotionFastOffPostfix");
            Patch(typeof(MitaPerson), "FaceLayer", null, "FaceLayerPostfix", typeof(int));
            Patch(typeof(MitaPerson), "FaceLayerFast", null, "FaceLayerFastPostfix", typeof(int));
            Patch(typeof(Character_Look), "LookOnPlayer", "LookOnPlayerPrefix", null);
            Patch(typeof(Character_Look), "LookOnPlayerAndRotate", "LookOnPlayerAndRotatePrefix", null);
            Patch(typeof(Character_Look), "PriorityLookOnPlayer", "PriorityLookOnPlayerPrefix", null);
            Patch(typeof(Character_Look), "LookRotatePriorityOnPlayer", "PriorityLookAndOnPlayerPrefix", null);
            Patch(typeof(Character_Look), "PriorityLookAndOnPlayer", "PriorityLookAndOnPlayerPrefix", null);
            applied += TogetherRuntime.InstallMinigamePatches(harmony);
            TogetherRuntime.Log("Harmony patches applied: " + applied + ", failed: " + failed);
        }

        static void Patch(Type type, string name, string prefix, string postfix, params Type[] args)
        {
            try
            {
                MethodInfo target = args.Length > 0 ? AccessTools.Method(type, name, args) : AccessTools.Method(type, name);
                if (target == null) throw new MissingMethodException(type.Name, name);
                harmony.Patch(target,
                    prefix == null ? null : new HarmonyMethod(typeof(ModHooks), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(ModHooks), postfix));
                applied++;
            }
            catch (Exception ex) { failed++; TogetherRuntime.Log("Patch failed " + type.Name + "." + name + ": " + ex.Message); }
        }

        // Exceptions must never escape into the game's native frames.
        static void Safe(string where, Action action)
        {
            try { action(); }
            catch (Exception ex) { TogetherRuntime.Log("MT-MODERROR hook " + where + ": " + ex.Message); }
        }

        // Click only fires its event when the object is armed (ObjectInteractive.active, read before Click disarms it):
        // an unarmed click did nothing here, so there is nothing to relay.
        static bool InteractPrefix(ObjectInteractive __instance, out bool __state)
        {
            __state = __instance != null && __instance.active;
            bool hostOnly = false;
            try { hostOnly = TogetherRuntime.HostOnlyClick(__instance); } catch (Exception ex) { TogetherRuntime.Log("MT-MODERROR hook Interact: " + ex.Message); }
            if (hostOnly) __state = false;
            return !hostOnly;
        }
        static void InteractPostfix(ObjectInteractive __instance, bool __state) { if (__state) Safe("Interact", () => TogetherRuntime.Story(Op.Interact, __instance, 0)); }

        static void MouseClickPostfix(Trigger_MouseClick __instance) { Safe("MouseClick", () => TogetherRuntime.Story(Op.MouseClick, __instance, 0)); }

        static bool TriggerEnterPrefix(Trigger_Event __instance)
        {
            try { return TogetherRuntime.BeforeTriggerEnter(__instance); }
            catch (Exception ex) { TogetherRuntime.Log("MT-MODERROR hook TriggerEnter: " + ex.Message); return true; }
        }

        static void TriggerEnterPostfix(Trigger_Event __instance) { Safe("TriggerEnter", () => TogetherRuntime.AfterTriggerEnter(__instance)); }

        static bool TriggerExitPrefix(Trigger_Event __instance)
        {
            try { return TogetherRuntime.BeforeTriggerExit(__instance); }
            catch (Exception ex) { TogetherRuntime.Log("MT-MODERROR hook TriggerExit: " + ex.Message); return true; }
        }

        static void TriggerExitPostfix(Trigger_Event __instance) { Safe("TriggerExit", () => TogetherRuntime.AfterTriggerExit(__instance)); }

        static void TriggerRestartPostfix(Trigger_Event __instance) { Safe("TriggerRestart", () => TogetherRuntime.AfterTriggerRestart(__instance)); }

        static void DialogueChoicePostfix(DialogueChanger __instance, int __0) { Safe("DialogueChoice", () => TogetherRuntime.Story(Op.DialogueChoice, __instance, __0)); }

        // Scene transitions: everyone runs the same Scene_Load the game used, so they load the next chapter the game's way.
        // GoScene switches to a chapter StartLoad preloaded (Scene 18 to 19 waits for it): relayed as SceneLoad arg 1.
        static void SceneGoPostfix(Scene_Load __instance) { Safe("SceneGo", () => TogetherRuntime.Story(Op.SceneLoad, __instance, 1)); }
        static void SceneLoadPostfix(Scene_Load __instance) { Safe("SceneLoad", () => TogetherRuntime.Story(Op.SceneLoad, __instance, 0)); }

        // Mita's face (host-authoritative) and gaze (nearest player).
        static void FaceEmotionPostfix(MitaPerson __instance, string __0) { Safe("Face", () => TogetherRuntime.AfterFace(__instance, TogetherRuntime.FaceEmotion, __0, 0)); }
        static void FaceEmotionFastPostfix(MitaPerson __instance, string __0) { Safe("Face", () => TogetherRuntime.AfterFace(__instance, TogetherRuntime.FaceEmotionFast, __0, 0)); }
        static void FaceEmotionOffPostfix(MitaPerson __instance) { Safe("Face", () => TogetherRuntime.AfterFace(__instance, TogetherRuntime.FaceEmotionOff, null, 0)); }
        static void FaceEmotionFastOffPostfix(MitaPerson __instance) { Safe("Face", () => TogetherRuntime.AfterFace(__instance, TogetherRuntime.FaceEmotionFastOff, null, 0)); }
        static void FaceLayerPostfix(MitaPerson __instance, int __0) { Safe("Face", () => TogetherRuntime.AfterFace(__instance, TogetherRuntime.FaceLayer, null, __0)); }
        static void FaceLayerFastPostfix(MitaPerson __instance, int __0) { Safe("Face", () => TogetherRuntime.AfterFace(__instance, TogetherRuntime.FaceLayerFast, null, __0)); }

        static bool Look(string where, Func<bool> body)
        {
            try { return body(); }
            catch (Exception ex) { TogetherRuntime.Log("MT-MODERROR hook " + where + ": " + ex.Message); return true; }
        }
        static bool LookOnPlayerPrefix(Character_Look __instance) { return Look("Look", () => TogetherRuntime.LookRedirect(__instance, t => __instance.LookOnObject(t))); }
        static bool LookOnPlayerAndRotatePrefix(Character_Look __instance) { return Look("Look", () => TogetherRuntime.LookRedirect(__instance, t => __instance.LookOnObjectAndRotate(t))); }
        static bool PriorityLookOnPlayerPrefix(Character_Look __instance) { return Look("Look", () => TogetherRuntime.LookRedirect(__instance, t => __instance.PriorityLook(t))); }
        static bool PriorityLookAndOnPlayerPrefix(Character_Look __instance) { return Look("Look", () => TogetherRuntime.LookRedirect(__instance, t => __instance.LookAndPriorityObject(t))); }

        // Cutscenes start for everyone; their time is kept in step by Op.Cutscene.
        // The host runs the cutscenes: a client's own start is turned into a request, and it plays when the host says.
        static IntPtr heldCutscene;
        static bool CutscenePlayPrefix(Playable_Animation __instance)
        {
            try
            {
                if (__instance == null || !TogetherRuntime.HostRunsCutscene()) return true;
                heldCutscene = __instance.Pointer;
                TogetherRuntime.Story(Op.CutscenePlay, __instance, 0);
                TogetherRuntime.Log("Cutscene requested from the host: " + __instance.name);
                TogetherRuntime.cutsceneRequests++;
                return false;
            }
            catch (Exception ex) { TogetherRuntime.Log("MT-MODERROR hook CutscenePrefix: " + ex.Message); return true; }
        }
        static void CutscenePlayPostfix(Playable_Animation __instance)
        {
            if (__instance != null && __instance.Pointer == heldCutscene) { heldCutscene = IntPtr.Zero; return; }
            Safe("CutsceneStarted", () => TogetherRuntime.CutsceneStartedHere(__instance));
            Safe("Cutscene", () => TogetherRuntime.Story(Op.CutscenePlay, __instance, 0));
        }

        // Key items: the scene key is taken before the game moves the item into the inventory.
        static void KeyItemPrefix(GameObject __0, out string __state) { __state = null; try { if (__0 != null) __state = SceneKeys.Key(__0); } catch { } }
        static void KeyAddPostfix(GameObject __0, string __state) { Safe("KeyAdd", () => { if (__state != null && __0 != null) TogetherRuntime.Story(Op.KeyAdd, __state, 0); }); }
        static void KeyRemovePostfix(GameObject __0, string __state) { Safe("KeyRemove", () => { if (__state != null && __0 != null) TogetherRuntime.Story(Op.KeyRemove, __state, 0); }); }

        // Hand items.
        static void TakeItemPrefix(GameObject __0, out string __state) { __state = null; try { if (__0 != null) __state = SceneKeys.Key(__0); } catch { } }
        static void TakeItemPostfix(PlayerMove __instance, GameObject __0, string __state) { Safe("TakeItem", () => TogetherRuntime.AfterTakeItem(__instance, __0, __state)); }
        static GameObject removing; static string removingKey;
        static void RemoveItemPrefix(PlayerMove __instance) { removing = null; removingKey = null; try { removing = __instance.item; if (removing != null) removingKey = SceneKeys.Key(removing); } catch { } }
        static void RemoveItemPostfix(PlayerMove __instance) { Safe("RemoveItem", () => TogetherRuntime.AfterRemoveItem(__instance, removing, removingKey)); }

        // Only story looks (one-shot or self-destroying); plain hover effects stay local.
        static void LookEnterPostfix(Trigger_MouseEvent __instance) { Safe("LookEnter", () => { if (__instance != null && (__instance.oneTime || __instance.destroyEnterMouse)) TogetherRuntime.Story(Op.LookTrigger, __instance, 1); }); }
        static void LookUpdatePrefix(Trigger_MouseEvent __instance) { Safe("LookBefore", () => TogetherRuntime.LookBefore(__instance)); }
        static void LookUpdatePostfix(Trigger_MouseEvent __instance) { Safe("LookAfter", () => TogetherRuntime.LookAfter(__instance)); }
        static void DistancePrefix(Trigger_DistanceCircle __instance) { Safe("DistanceBefore", () => TogetherRuntime.DistanceBefore(__instance)); }
        static void DistancePostfix(Trigger_DistanceCircle __instance) { Safe("DistanceAfter", () => TogetherRuntime.DistanceAfter(__instance)); }
        static void SafeButtonPostfix(Basement_Safe __instance, int x) { Safe("SafeButton", () => TogetherRuntime.Call(0, __instance, x)); }
        static void KeyboardAddPostfix(InputKeyboard_Main __instance, string _key) { Safe("KeyboardAdd", () => { if (!string.IsNullOrEmpty(_key)) TogetherRuntime.Call(1, __instance, _key[0]); }); }
        static void KeyboardRemovePostfix(InputKeyboard_Main __instance) { Safe("KeyboardRemove", () => TogetherRuntime.Call(2, __instance, 0)); }
        static void KeyboardEnterPostfix(InputKeyboard_Main __instance) { Safe("KeyboardEnter", () => TogetherRuntime.Call(3, __instance, 0)); }
        static void KeyboardUpperPostfix(InputKeyboard_Main __instance) { Safe("KeyboardUpper", () => TogetherRuntime.Call(4, __instance, 0)); }
        static void QuestClickPrefix() { TogetherRuntime.QuestClickPrefix(); }
        static void QuestClickPostfix() { TogetherRuntime.QuestClickPostfix(); }
        // Scene 14 phone character: only the phone's holder runs it; the others show it from the prop stream.
        // The game shares one compiled body between methods with identical code, so these hooks can run for other types'
        // Play/Update too (see Minigames.KindOf): only the phone character itself is held back.
        static bool IsMobile(Location14_PlayerMobileMove m) { return m != null && m.GetIl2CppType().Name == nameof(Location14_PlayerMobileMove); }
        static bool MobilePlayPrefix(Location14_PlayerMobileMove __instance)
        {
            if (!TogetherRuntime.MobileWatcher || !IsMobile(__instance)) return true;
            try { if (!__instance.gameObject.activeSelf) __instance.gameObject.SetActive(true); } catch (Exception ex) { TogetherRuntime.Log("MT-MODERROR hook MobilePlay: " + ex.Message); }
            return false;
        }
        static void MobilePlayPostfix(Location14_PlayerMobileMove __instance) { if (IsMobile(__instance)) Safe("MobilePlay", () => TogetherRuntime.MobilePlayed(__instance)); }
        static bool MobileRunPrefix(Location14_PlayerMobileMove __instance) { return !TogetherRuntime.MobileWatcher || !IsMobile(__instance); }

        // Scene 1 phone app (AppSync.cs): the host's app is relayed; on clients the player's own reading on and name
        // check do nothing. Each checks the real type (shared compiled bodies, see Minigames.KindOf).
        static bool Is<T>(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase o) { return o != null && o.TryCast<Component>() is Component c && c != null && c.GetIl2CppType().Name == typeof(T).Name; }
        static void AppStartPostfix(Tamagotchi_Main __instance) { if (Is<Tamagotchi_Main>(__instance)) Safe("AppStart", () => TogetherRuntime.AppStarted(__instance, true)); }
        static void AppStopPostfix(Tamagotchi_Main __instance) { if (Is<Tamagotchi_Main>(__instance)) Safe("AppStop", () => TogetherRuntime.AppStarted(__instance, false)); }
        static bool AppReadPrefix(Tamagotchi_Dialogue __instance) { return !TogetherRuntime.AppViewer || !Is<Tamagotchi_Dialogue>(__instance); }
        static bool AppNamePrefix(Location1_InputNamePlayer __instance) { return !TogetherRuntime.AppViewer || !Is<Location1_InputNamePlayer>(__instance); }
        static bool AppButtonPrefix(ButtonMouseClick __instance)
        {
            try { return !TogetherRuntime.AppViewer || !Is<ButtonMouseClick>(__instance) || !TogetherRuntime.IsAppButton(__instance); }
            catch (Exception ex) { TogetherRuntime.Log("MT-MODERROR hook AppButton: " + ex.Message); return true; }
        }
        static void AppButtonDownPostfix(ButtonMouseClick __instance) { if (Is<ButtonMouseClick>(__instance)) Safe("AppButton", () => TogetherRuntime.AppPressed(__instance, false)); }
        static void AppButtonUpPostfix(ButtonMouseClick __instance) { if (Is<ButtonMouseClick>(__instance)) Safe("AppButton", () => TogetherRuntime.AppPressed(__instance, true)); }
        static bool AppUiButtonPrefix(UnityEngine.UI.Button __instance)
        {
            try { return !TogetherRuntime.AppViewer || __instance == null || !TogetherRuntime.IsAppButton(__instance); }
            catch (Exception ex) { TogetherRuntime.Log("MT-MODERROR hook AppUiButton: " + ex.Message); return true; }
        }
        static void AppUiButtonPostfix(UnityEngine.UI.Button __instance) { Safe("AppUiButton", () => TogetherRuntime.AppClicked(__instance)); }
        static void AppCookedPostfix(TamagotchiGame_Cooking __instance) { if (Is<TamagotchiGame_Cooking>(__instance)) Safe("AppCooked", () => TogetherRuntime.AppCooked(__instance)); }
        static void AppNamePostfix(Location1_InputNamePlayer __instance) { if (Is<Location1_InputNamePlayer>(__instance)) Safe("AppName", () => TogetherRuntime.AppNamed(__instance)); }
        static void QuestMovePostfix(Location14_PlayerQuest __instance, Vector3 _pos, UnityEngine.Events.UnityEvent _event) { Safe("QuestMove", () => TogetherRuntime.QuestMoved(__instance, _pos, _event)); }
        static void QuestReplyPrefix(Location14_ButtonChangeDialogue __instance, out bool __state) { __state = __instance != null && __instance.active; }
        // Core computer input is sent before the method runs: Enter's right/wrong event can change the screen at once.
        static void CoreClickPrefix(GamesCore_InputField __instance) { Safe("CoreClick", () => TogetherRuntime.CoreKey(__instance, 10)); }
        static void CoreBackspacePrefix(GamesCore_InputField __instance) { Safe("CoreBackspace", () => TogetherRuntime.CoreKey(__instance, 11)); }
        static void CoreEnterPrefix(GamesCore_InputField __instance) { Safe("CoreEnter", () => TogetherRuntime.CoreKey(__instance, 12)); }
        static void CoreFolderPostfix(GamesCore_Main __instance, GamesCore_Main_Folder _folder) { Safe("CoreFolder", () => TogetherRuntime.CoreFolder(__instance, _folder)); }
        static void CoreStage0Postfix(GamesCore_Main __instance) { Safe("CoreStage", () => TogetherRuntime.CoreStage(__instance, 0)); }
        static void CoreStage1Postfix(GamesCore_Main __instance) { Safe("CoreStage", () => TogetherRuntime.CoreStage(__instance, 1)); }
        static void CoreStage2Postfix(GamesCore_Main __instance) { Safe("CoreStage", () => TogetherRuntime.CoreStage(__instance, 2)); }
        static void CoreStage3Postfix(GamesCore_Main __instance) { Safe("CoreStage", () => TogetherRuntime.CoreStage(__instance, 3)); }
        static void QuestReplyPostfix(Location14_ButtonChangeDialogue __instance, bool __state) { Safe("QuestReply", () => TogetherRuntime.QuestReplyPicked(__instance, __state)); }
        static void CameraEnterPostfix(Trigger_DistanceCamera __instance, bool _deactiveAfter) { Safe("CameraEnter", () => TogetherRuntime.Call(5, __instance, _deactiveAfter ? 1 : 0)); }
        static void CameraExitPostfix(Trigger_DistanceCamera __instance, bool _deactiveAfter) { Safe("CameraExit", () => TogetherRuntime.Call(6, __instance, _deactiveAfter ? 1 : 0)); }
        static void NovellaNextPostfix() { Safe("NovellaNext", TogetherRuntime.NovellaClicked); }
        static void DialogueStartPostfix(Dialogue_3DText __instance) { Safe("DialogueStart", () => TogetherRuntime.LineStarted(__instance)); }
        static void SkipDialoguePostfix(Dialogue_3DText __instance) { Safe("SkipDialogue", () => TogetherRuntime.Story(Op.SkipDialogue, __instance, 0)); }

        // Deaths: everyone restarts together (Deaths.cs). Prefixes: a death counts from the moment it begins, even if the
        // game's own death code later throws part-way.
        static void DeathMitaKillerPostfix(Location6_MitaKiller __instance) { Safe("Death", () => TogetherRuntime.AfterDeath(__instance, TogetherRuntime.DeathMitaKiller)); }
        static void DeathManekenCheckpointPostfix(Location10_ManekenChekpoint __instance) { Safe("Death", () => TogetherRuntime.AfterDeath(__instance, TogetherRuntime.DeathManekenCheckpoint)); }
        static void DeathLiftPostfix(Location11_Lift __instance) { Safe("Death", () => TogetherRuntime.AfterDeath(__instance, TogetherRuntime.DeathLift)); }
        static void DeathLampMovePostfix(Location11_LampMove __instance) { Safe("Death", () => TogetherRuntime.AfterDeath(__instance, TogetherRuntime.DeathLampMove)); }
        static void DeathArenaPostfix(Location20_Arena __instance) { Safe("Death", () => TogetherRuntime.AfterDeath(__instance, TogetherRuntime.DeathArena)); }
        static void DeathRunCorridorPostfix(Location20_RunCorridor __instance) { Safe("Death", () => TogetherRuntime.AfterDeath(__instance, TogetherRuntime.DeathRunCorridor)); }
        static void DeathManekenPostfix(Mob_Maneken __instance) { Safe("Death", () => TogetherRuntime.AfterDeath(__instance, TogetherRuntime.DeathManeken)); }
        static void DeathShooterPostfix(Shooter_Main __instance) { Safe("Death", () => TogetherRuntime.AfterDeath(__instance, TogetherRuntime.DeathShooter)); }
        static void DeathGame4Postfix(Location19_Game4 __instance) { Safe("Death", () => TogetherRuntime.AfterDeath(__instance, TogetherRuntime.DeathGame4)); }

        // Shared randomness (Seeds.cs).
        static void SeedPrefix(Component __instance) { Safe("Seed", () => TogetherRuntime.BeginSeeded(__instance)); }
        static void SeedPostfix() { Safe("Seed", TogetherRuntime.EndSeeded); }

        // Interaction mirroring (Interactions.cs): replayed clicks do not animate the local player.
        static bool PlayerAnimationPrefix(ObjectAnimationPlayer __7)
        {
            try { return TogetherRuntime.BeforePlayerAnimation(__7); }
            catch (Exception ex) { TogetherRuntime.Log("MT-MODERROR hook PlayerAnimation: " + ex.Message); return true; }
        }
        // Hand animations follow the same rule as body ones: mirrored clicks play them here too, presser-only ones do not.
        static bool PlayerHandAnimationPrefix()
        {
            try
            {
                if (!TogetherRuntime.ReplayingInteraction) return true;
                return CutsceneRules.ForInteraction(TogetherRuntime.ReplayingInteractionKey, null).Who == CutsceneRules.Who.Everyone;
            }
            catch (Exception ex) { TogetherRuntime.Log("MT-MODERROR hook PlayerHandAnimation: " + ex.Message); return false; }
        }
        static void AnimEventPostfix(int __0) { Safe("AnimEvent", () => TogetherRuntime.AfterAnimEvent(__0)); }
        static void ObjectAnimationPostfix(ObjectAnimationPlayer __instance) { Safe("ObjectAnimation", () => TogetherRuntime.AfterObjectAnimation(__instance, 0)); }
        static void ObjectAnimationFastPostfix(ObjectAnimationPlayer __instance) { Safe("ObjectAnimation", () => TogetherRuntime.AfterObjectAnimation(__instance, 1)); }
        static void ObjectAnimationOnPlayerPostfix(ObjectAnimationPlayer __instance) { Safe("ObjectAnimation", () => TogetherRuntime.AfterObjectAnimation(__instance, 2)); }

        // Opening the pause menu must not freeze the world under the other players' feet.
        static void PausePostfix(bool __0, bool __1)
        {
            Safe("Pause", () =>
            {
                TogetherRuntime rt = TogetherRuntime.Instance;
                if (rt != null && rt.Connected && __0 && !__1 && Time.timeScale < 1f) Time.timeScale = 1f;
            });
        }
    }
}
