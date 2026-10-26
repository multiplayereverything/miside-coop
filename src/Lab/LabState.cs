using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Playables;

namespace MiSideTogether
{
    // Lab-only: every 2 s each machine logs what it shows, so tests/verify-lab.py can line machines up by wall clock and
    // measure the real desync (players, Mitas, dialogue lines, timelines, cutscene mode):
    //   MT-STATE role=host scene=.. me=x,y,z,yaw,anim free=1 cut=0 peers=id:x,y,z,shown;.. npc=key#:x,y,z,state;..
    //            lines=hash;.. tl=hash:time;.. lag=max/mean
    // And every animation state change as it happens (scripted cutscene start times, compared across machines):
    //   MT-ANIM scene=.. who=me|key# state=hash
    // Keys are written as Wire.Hash (stable across processes) to keep lines short.
    public sealed partial class TogetherRuntime
    {
        float nextState;
        readonly Dictionary<string, int> lastAnimState = new Dictionary<string, int>();
        string lastAnimScene;

        void AnimEventTick()
        {
            if (!LabMode) return;
            string scene = ActiveSceneName;
            if (scene != lastAnimScene) { lastAnimScene = scene; lastAnimState.Clear(); }
            if (scene == "SceneMenu") return;
            PlayerMove player = LocalPlayer;
            if (player != null && player.animPerson != null) NoteAnim(scene, "me", player.animPerson);
            foreach (NpcRef n in Npcs())
                if (n != null && n.Root != null && n.Root.gameObject.activeInHierarchy && n.Anim != null && n.Anim.isActiveAndEnabled)
                    NoteAnim(scene, Wire.Hash(n.Key).ToString("x8"), n.Anim);
        }

        void NoteAnim(string scene, string who, Animator anim)
        {
            int state = anim.GetCurrentAnimatorStateInfo(0).shortNameHash;
            if (lastAnimState.TryGetValue(who, out int last) && last == state) return;
            bool first = !lastAnimState.ContainsKey(who);
            lastAnimState[who] = state;
            // The first sight of an animator is not a change (a copy may load a moment later).
            if (!first) Log("MT-ANIM scene=" + scene.Replace(' ', '_') + " who=" + who + " state=" + state);
        }

        static string P(Vector3 v) { return v.x.ToString("0.00") + "," + v.y.ToString("0.00") + "," + v.z.ToString("0.00"); }

        void StateTick(float now)
        {
            if (!LabMode || now < nextState || ActiveSceneName == "SceneMenu") return;
            nextState = now + 2;
            StringBuilder s = new StringBuilder("MT-STATE role=").Append(Hosting ? "host" : "client").Append(" scene=").Append(ActiveSceneName.Replace(' ', '_'));
            PlayerMove player = LocalPlayer;
            if (player != null)
            {
                Transform body = player.animPerson != null ? player.animPerson.transform : player.transform;
                int anim = player.animPerson != null ? player.animPerson.GetCurrentAnimatorStateInfo(0).shortNameHash : 0;
                s.Append(" me=").Append(P(player.transform.position)).Append(',').Append(body.eulerAngles.y.ToString("0")).Append(',').Append(anim)
                 .Append(" free=").Append(LocalFree(player) ? 1 : 0).Append(" acting=").Append(player.animationRun ? 1 : 0);
            }
            s.Append(" cut=").Append(LocalInCutscene() ? 1 : 0).Append(" ts=").Append(Time.timeScale.ToString("0.##")).Append(" peers=");
            foreach (Peer p in peers.Values) if (p.HasPose) s.Append(p.Id).Append(':').Append(P(p.Feet)).Append(',').Append(p.Avatar != null && p.Avatar.Root != null && p.Avatar.Root.activeSelf ? 1 : 0).Append(';');
            s.Append(" npc=");
            foreach (NpcRef n in Npcs())
                if (n != null && n.Root != null && n.Root.gameObject.activeInHierarchy)
                    s.Append(Wire.Hash(n.Key).ToString("x8")).Append(':').Append(P(n.Root.position)).Append(',').Append(n.Anim != null && n.Anim.isActiveAndEnabled ? n.Anim.GetCurrentAnimatorStateInfo(0).shortNameHash : 0).Append(';');
            s.Append(" lines=");
            foreach (Dialogue_3DText d in ActiveLines(now)) s.Append(Wire.Hash(SceneKeys.Key(d)).ToString("x8")).Append(';');
            s.Append(" tl=");
            foreach (PlayableDirector d in Directors())
                if (d != null && d.state == PlayState.Playing) s.Append(Wire.Hash(SceneKeys.Key(d)).ToString("x8")).Append(':').Append(d.time.ToString("0.00")).Append(';');
            s.Append(" anims=").Append(animatorStatesApplied).Append(" room=").Append(stateApplied);
            if (dialogueLagCount > 0) s.Append(" lag=").Append(dialogueLagMax.ToString("0.00")).Append('/').Append((dialogueLagSum / dialogueLagCount).ToString("0.00"));
            Log(s.ToString());
        }
    }
}
