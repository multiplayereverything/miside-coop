using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab-only: the "inventory" tour loads every chapter and writes what it contains to <lab copy>/mt-inventory/<scene>.tsv:
    // every game script instance (type, scene key, active, position), every dialogue line (index into the chapter's
    // LocationDialogue file, speaker, next line, voice, timing, the text as loaded), every dialogue choice and every
    // timeline. tools/inventory_report.py turns these into docs/SYNC-INVENTORY.md (what must be synced and how).
    public sealed partial class TogetherRuntime
    {
        int inventoryIndex = -1; float inventoryAt;

        void InventoryTour(float now, string scene)
        {
            if (inventoryIndex < 0 && scene == "SceneMenu" && now - tourSceneAt > 5) { inventoryIndex = 0; LoadThroughGame(Chapters[0]); return; }
            if (inventoryIndex < 0 || inventoryIndex >= Chapters.Length || scene != Chapters[inventoryIndex] || now - tourSceneAt < 10 || now - inventoryAt < 10) return;
            inventoryAt = now;
            try { WriteInventory(scene); } catch (Exception ex) { Log("Lab: inventory of " + scene + " failed: " + ex); }
            inventoryIndex++;
            if (inventoryIndex < Chapters.Length) LoadThroughGame(Chapters[inventoryIndex]); else CheckOnce("inventory.done", true, Chapters.Length + " chapters");
        }

        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '); }
        static string V3(Vector3 v) { return v.x.ToString("0.00") + "," + v.y.ToString("0.00") + "," + v.z.ToString("0.00"); }

        void WriteInventory(string scene)
        {
            string dir = Path.Combine(Root, "mt-inventory");
            Directory.CreateDirectory(dir);
            StringBuilder o = new StringBuilder();
            o.Append("#kind\ttype\tkey\tactive\tposition\textra\n");
            int scripts = 0;
            foreach (MonoBehaviour m in Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (m == null || !m.gameObject.scene.isLoaded || m.gameObject.scene.name == "DontDestroyOnLoad") continue;
                string type = m.GetIl2CppType().Name;
                if (type.StartsWith("MiSideTogether", StringComparison.Ordinal)) continue;
                string ns = m.GetIl2CppType().Namespace ?? "";
                if (ns.StartsWith("UnityEngine", StringComparison.Ordinal) || ns.StartsWith("TMPro", StringComparison.Ordinal)) continue;
                o.Append("script\t").Append(type).Append('\t').Append(SceneKeys.Key(m)).Append('\t').Append(m.gameObject.activeInHierarchy ? 1 : 0).Append('\t').Append(V3(m.transform.position)).Append("\t\n");
                scripts++;
            }
            int lines = 0;
            foreach (Dialogue_3DText d in Object.FindObjectsOfType<Dialogue_3DText>(true))
            {
                if (d == null) continue;
                string speaker = d.speaker != null ? d.speaker.name : "", next = d.nextText != null ? SceneKeys.Key(d.nextText.transform) : "";
                string voice = d.audioVoice != null ? d.audioVoice.name : "";
                o.Append("dialogue\tDialogue_3DText\t").Append(SceneKeys.Key(d)).Append('\t').Append(d.gameObject.activeInHierarchy ? 1 : 0).Append('\t').Append(V3(d.transform.position))
                 .Append("\tindex=").Append(d.indexString).Append("|speaker=").Append(Clean(speaker)).Append("|next=").Append(next).Append("|voice=").Append(Clean(voice))
                 .Append("|show=").Append(d.timeShow.ToString("0.00")).Append("|finish=").Append(d.timeFinish.ToString("0.00")).Append("|text=").Append(Clean(d.textPrint)).Append('\n');
                lines++;
            }
            int choices = 0;
            foreach (DialogueChanger c in Object.FindObjectsOfType<DialogueChanger>(true))
            {
                if (c == null) continue;
                StringBuilder b = new StringBuilder();
                if (c.buttons != null) foreach (DialogueChangerCase k in c.buttons) if (k != null) b.Append(k.stringFile).Append(k.exitButton ? "x" : "").Append(k.oneTime ? "o" : "").Append(' ');
                o.Append("choice\tDialogueChanger\t").Append(SceneKeys.Key(c)).Append('\t').Append(c.gameObject.activeInHierarchy ? 1 : 0).Append('\t').Append(V3(c.transform.position))
                 .Append("\tfile=").Append(Clean(c.fileQuest)).Append("|buttons=").Append(b.ToString().Trim()).Append('\n');
                choices++;
            }
            foreach (UnityEngine.Playables.PlayableDirector p in Object.FindObjectsOfType<UnityEngine.Playables.PlayableDirector>(true))
                if (p != null) o.Append("timeline\tPlayableDirector\t").Append(SceneKeys.Key(p)).Append('\t').Append(p.gameObject.activeInHierarchy ? 1 : 0).Append('\t').Append(V3(p.transform.position))
                 .Append("\tduration=").Append(p.duration.ToString("0.0")).Append("|asset=").Append(p.playableAsset != null ? Clean(p.playableAsset.name) : "").Append('\n');
            int animators = 0;
            foreach (Animator a in Object.FindObjectsOfType<Animator>(true))
                if (a != null && a.gameObject.scene.isLoaded) { animators++; o.Append("animator\tAnimator\t").Append(SceneKeys.Key(a)).Append('\t').Append(a.gameObject.activeInHierarchy ? 1 : 0).Append('\t').Append(V3(a.transform.position)).Append("\tcontroller=").Append(a.runtimeAnimatorController != null ? Clean(a.runtimeAnimatorController.name) : "").Append('\n'); }
            int lights = Object.FindObjectsOfType<Light>(true).Length, audio = Object.FindObjectsOfType<AudioSource>(true).Length;
            o.Append("count\tLight\t\t\t\t").Append(lights).Append('\n').Append("count\tAudioSource\t\t\t\t").Append(audio).Append('\n');
            File.WriteAllText(Path.Combine(dir, scene.Replace(' ', '_') + ".tsv"), o.ToString(), new UTF8Encoding(false));
            Log("Lab: inventory " + scene + " scripts=" + scripts + " dialogues=" + lines + " choices=" + choices + " animators=" + animators + " lights=" + lights);
        }
    }
}
