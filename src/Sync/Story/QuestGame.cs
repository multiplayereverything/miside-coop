using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Scene 14's point-and-click quest game on the PC (Location14_GameQuestPlayer). Every machine runs its own copy,
    // started by the relayed story; what a player does in it is relayed so every copy walks and answers the same:
    //   8 Location14_PlayerQuest.Move(pos, event)   a click in the game: the quest character walks to the point, or to a
    //                                               Location14_QuestInteractive whose eventFinishMove fires on arrival
    //   9 Location14_GameQuestPlayer.ChangeDialogue  a reply picked (Location14_ButtonChangeDialogue.sideChange)
    // Only player input is relayed: Move inside ClickMouse (moves the story starts itself happen everywhere already),
    // and the reply button's OnPointerDown, which inlines ChangeDialogue (a hook on ChangeDialogue never sees a click).
    public sealed partial class TogetherRuntime
    {
        static bool inQuestClick;
        public int questMovesApplied, questRepliesApplied;

        public static void QuestClickPrefix() { inQuestClick = true; }
        public static void QuestClickPostfix() { inQuestClick = false; }

        public static void QuestMoved(Location14_PlayerQuest quest, Vector3 pos, UnityEvent ev)
        {
            if (!inQuestClick || quest == null) return;
            string target = "";
            if (ev != null)
                foreach (Location14_QuestInteractive qi in Object.FindObjectsOfType<Location14_QuestInteractive>())
                    if (qi != null && qi.eventFinishMove != null && qi.eventFinishMove.Pointer == ev.Pointer) { target = SceneKeys.Key(qi); break; }
            string spot = string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", pos.x, pos.y, pos.z);
            Story(Op.Call, "8:" + SceneKeys.Key(quest) + "|" + spot + "|" + target, 0);
        }

        // Prefix: whether the button was live (an inactive one does nothing on click).
        public static void QuestReplyPicked(Location14_ButtonChangeDialogue button, bool wasActive)
        {
            if (!wasActive || button == null || button.main == null) return;
            Story(Op.Call, "9:" + SceneKeys.Key(button.main), button.sideChange ? 1 : 0);
        }

        void ApplyQuestMove(string target)
        {
            string[] p = target.Split('|');
            if (p.Length < 3) return;
            Location14_PlayerQuest quest = SceneKeys.Find<Location14_PlayerQuest>(p[0]);
            if (Missing(quest, p[0]) || Inactive(quest, p[0])) return;
            string[] c = p[1].Split(',');
            NumberStyles f = NumberStyles.Float; CultureInfo inv = CultureInfo.InvariantCulture;
            if (c.Length != 3 || !float.TryParse(c[0], f, inv, out float x) || !float.TryParse(c[1], f, inv, out float y) || !float.TryParse(c[2], f, inv, out float z)) return;
            UnityEvent ev = null;
            if (p[2].Length > 0)
            {
                Location14_QuestInteractive qi = SceneKeys.Find<Location14_QuestInteractive>(p[2]);
                if (Missing(qi, p[2])) return;
                ev = qi.eventFinishMove;
            }
            quest.Move(new Vector3(x, y, z), ev);
            questMovesApplied++;
        }

        void ApplyQuestReply(string target, int side)
        {
            Location14_GameQuestPlayer game = SceneKeys.Find<Location14_GameQuestPlayer>(target);
            if (Missing(game, target) || Inactive(game, target)) return;
            game.ChangeDialogue(side != 0);
            questRepliesApplied++;
        }
    }
}
