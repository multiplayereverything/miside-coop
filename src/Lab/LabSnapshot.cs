using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // -mt-snap: at every pilot comparison each PC writes snaps/<n>.txt beside the game: every object of the chapter recorded at
    // load, switched on or off, and the state of every light, animator, collider and renderer. tools/compare_state.py lists
    // what stays different between PCs, so a desync nobody wrote a check for still shows.
    // -mt-snapbreak: the client switches one object off, to prove the comparison can fail.
    public sealed partial class TogetherRuntime
    {
        public static bool LabSnapOn, labSnapBreak;
        int labSnapNext;
        string labSnapBroken = "";

        void LabSnapAll()
        {
            int n = labSnapNext++;
            LabSnap(n);
            Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-snap:" + n)));
        }

        static bool LabSnapObject(Component c, Transform me)
        {
            return c != null && ChapterScene(c.gameObject) && (me == null || !c.transform.IsChildOf(me)) && SceneKeys.WasRecorded(SceneKeys.Key(c));
        }

        public void LabSnap(int n)
        {
            string dir = Path.Combine(Root, "snaps");
            if (n == 0 && Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            Transform me = LocalPlayer != null ? LocalPlayer.transform : null;
            List<string> lines = new List<string>();
            foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
                if (LabSnapObject(t, me))
                {
                    if (labSnapBreak && IsClient && n == 2 && labSnapBroken.Length == 0 && t.gameObject.activeInHierarchy && t.childCount == 0 && t.GetComponents<Component>().Length == 1)
                    {
                        labSnapBroken = SceneKeys.Key(t);
                        t.gameObject.SetActive(false);
                        Log("MT-SNAP broke " + labSnapBroken);
                    }
                    lines.Add("O\t" + SceneKeys.Key(t) + "\t" + (t.gameObject.activeSelf ? 1 : 0));
                }
            foreach (Light l in Object.FindObjectsOfType<Light>(true))
                if (LabSnapObject(l, me) && l.GetComponentInParent<FlashLight>() == null)
                    lines.Add("L\t" + SceneKeys.Key(l) + "\t" + (l.enabled ? 1 : 0) + "\t" + (l.GetComponent<Light_Intensity>() != null ? (l.intensity >= 0.25f ? "lit" : "dark") : Mathf.Round(l.intensity * 2).ToString()) + "\t" + Mathf.Round(l.color.r * 4) + Mathf.Round(l.color.g * 4) + Mathf.Round(l.color.b * 4));
            foreach (Animator a in Object.FindObjectsOfType<Animator>(true))
                if (LabSnapObject(a, me))
                    lines.Add("A\t" + SceneKeys.Key(a) + "\t" + (a.enabled ? 1 : 0) + "\t" + (a.isActiveAndEnabled && a.runtimeAnimatorController != null ? a.GetCurrentAnimatorStateInfo(0).shortNameHash : 0));
            foreach (Collider c in Object.FindObjectsOfType<Collider>(true))
                if (LabSnapObject(c, me)) lines.Add("C\t" + SceneKeys.Key(c) + "\t" + (c.enabled ? 1 : 0));
            foreach (Renderer r in Object.FindObjectsOfType<Renderer>(true))
                if (LabSnapObject(r, me)) lines.Add("R\t" + SceneKeys.Key(r) + "\t" + (r.enabled ? 1 : 0));
            lines.Sort(System.StringComparer.Ordinal);
            File.WriteAllLines(Path.Combine(dir, n.ToString("000") + ".txt"), lines);
            Log("MT-SNAP " + n + " " + lines.Count + " lines in " + ActiveSceneName);
        }
    }
}
