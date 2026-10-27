using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab-only: -mt-dump "<scene>" writes that scene's full hierarchy (active and inactive objects, component types,
    // UI texts, button wiring) to private-dump-<scene>.txt next to the game, for designing hooks and the native menu.
    public sealed partial class TogetherRuntime
    {
        string dumpScene;
        bool dumped;

        void DumpTick(float now)
        {
            if (dumpScene == null || dumped || ActiveSceneName != dumpScene || now - tourSceneAt < 6) return;
            dumped = true;
            string path = Path.Combine(Root, "private-dump-" + dumpScene.Replace(' ', '_') + ".txt");
            StringBuilder text = new StringBuilder();
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects()) DumpObject(root.transform, 0, text);
            // Menu layout: every page and button with its position, size and the page it opens.
            foreach (MenuLocation location in Object.FindObjectsOfType<MenuLocation>(true))
            {
                text.Append("\nPAGE ").Append(location.name).Append(" countCases=").Append(location.countCases).Append(" objects=").Append(location.objects != null ? location.objects.Count : -1)
                    .Append(" back=").Append(location.buttonBack != null ? location.buttonBack.name : "-").Append('\n');
                if (location.objects != null) foreach (RectTransform r in location.objects) if (r != null)
                    {
                        MenuNextLocation next = r.GetComponent<MenuNextLocation>();
                        ButtonMouseClick button = r.GetComponent<ButtonMouseClick>();
                        text.Append("  ").Append(r.name).Append(" pos=").Append(r.anchoredPosition).Append(" size=").Append(r.sizeDelta)
                            .Append(" next=").Append(next != null && next.nextLocation != null ? next.nextLocation.name : "-")
                            .Append(" persistent=").Append(button != null && button.eventClick != null ? button.eventClick.GetPersistentEventCount() : -1).Append('\n');
                    }
            }
            File.WriteAllText(path, text.ToString());
            Log("Lab: dumped " + dumpScene + " to " + path);
            StringBuilder wires = new StringBuilder();
            DumpWires(wires);
            string wiresPath = Path.Combine(Root, "private-wires-" + dumpScene.Replace(' ', '_') + ".txt");
            File.WriteAllText(wiresPath, wires.ToString());
            Log("Lab: wiring of " + dumpScene + " to " + wiresPath);
        }

        static void DumpObject(Transform t, int depth, StringBuilder text)
        {
            if (depth > 14) return;
            text.Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)").Append(" [");
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string type = c.GetIl2CppType().Name;
                if (type == "Transform" || type == "RectTransform") continue;
                text.Append(type);
                UnityEngine.UI.Text label = c.TryCast<UnityEngine.UI.Text>();
                if (label != null) text.Append("=\"").Append((label.text ?? "").Replace('\n', ' ')).Append('"');
                text.Append(' ');
            }
            text.Append("]\n");
            for (int i = 0; i < t.childCount; i++) DumpObject(t.GetChild(i), depth + 1, text);
        }
    }
}
