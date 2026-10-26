using System;
using UnityEngine;

namespace MiSideTogether
{
    // Minimal IMGUI toolkit for MiSide's IL2CPP build, which strips IMGUI the game never uses (GUILayout.BeginArea
    // throws "Method unstripping failed"). No GUILayout at all: widgets are placed with a cursor, and each native call
    // (GUI.Box, GUI.Button, GUI.TextField) is tried once; if it is stripped, the widget falls back to GUI.Label plus
    // its own mouse and keyboard handling through the legacy Input API the game itself uses.
    public sealed class Ui
    {
        static bool boxBroken, buttonBroken, fieldBroken;
        public static string Fallbacks { get { return (boxBroken ? "box " : "") + (buttonBroken ? "button " : "") + (fieldBroken ? "field " : ""); } }

        public float X, Y, W;
        float scale = 1;
        public GUIStyle Label, Title;
        string focused;
        int clickFrame = -1, typeFrame = -1;

        public void Begin(Rect area, float uiScale, string title)
        {
            scale = uiScale;
            Box(area, "");
            X = area.x + 14; Y = area.y + 10; W = area.width - 28;
            GUI.Label(new Rect(X, Y, W, 24), title, Title);
            Y += 32;
        }

        public void Box(Rect r, string text)
        {
            if (!boxBroken)
            {
                try { GUI.Box(r, text); return; }
                catch (Exception ex) { boxBroken = true; TogetherRuntime.Log("UI: GUI.Box unavailable (" + ex.Message + "), drawing a shaded label"); }
            }
            Color previous = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.75f);
            // A run of block characters behind the panel gives it a dark backdrop without any textures.
            for (float y = r.y; y < r.yMax; y += 16) GUI.Label(new Rect(r.x, y, r.width, 18), new string('█', Mathf.CeilToInt(r.width / 9f)), Label);
            GUI.color = previous;
        }

        public void Text(string text) { Text(text, Label); }

        public void Text(string text, GUIStyle style)
        {
            int lines = Mathf.Max(1, Mathf.CeilToInt((text ?? "").Length * 8f / Mathf.Max(40, W)));
            float h = 20 * lines + 2;
            GUI.Label(new Rect(X, Y, W, h), text, style);
            Y += h;
        }

        public void Space(float h) { Y += h; }

        public bool Button(string text, float h) { bool hit = Button(new Rect(X, Y, W, h), text); Y += h + 4; return hit; }

        public bool Button(Rect r, string text)
        {
            if (!GUI.enabled) { Shade(r, text, 0.35f); return false; }
            if (!buttonBroken)
            {
                try { return GUI.Button(r, text); }
                catch (Exception ex) { buttonBroken = true; TogetherRuntime.Log("UI: GUI.Button unavailable (" + ex.Message + "), using own buttons"); }
            }
            bool hover = r.Contains(Mouse);
            Shade(r, text, hover ? 1f : 0.75f);
            if (hover && Input.GetMouseButtonDown(0) && clickFrame != Time.frameCount) { clickFrame = Time.frameCount; focused = null; return true; }
            return false;
        }

        void Shade(Rect r, string text, float alpha)
        {
            Color previous = GUI.color;
            GUI.color = new Color(previous.r, previous.g, previous.b, previous.a * alpha);
            GUI.Label(r, "[ " + text + " ]", Label);
            GUI.color = previous;
        }

        public string Field(string id, string value, int max)
        {
            string result = Field(new Rect(X, Y, W, 24), id, value, max);
            Y += 28;
            return result;
        }

        public string Field(Rect r, string id, string value, int max)
        {
            value = value ?? "";
            if (!fieldBroken)
            {
                try { return GUI.TextField(r, value, max); }
                catch (Exception ex) { fieldBroken = true; TogetherRuntime.Log("UI: GUI.TextField unavailable (" + ex.Message + "), using own text fields"); }
            }
            bool focus = focused == id;
            if (GUI.enabled && Input.GetMouseButtonDown(0) && r.Contains(Mouse)) { focused = id; focus = true; }
            if (focus && GUI.enabled && typeFrame != Time.frameCount)
            {
                typeFrame = Time.frameCount;
                foreach (char c in Input.inputString)
                {
                    if (c == '\b') { if (value.Length > 0) value = value.Substring(0, value.Length - 1); }
                    else if (c == '\n' || c == '\r') focused = null;
                    else if (!char.IsControl(c) && value.Length < max) value += c;
                }
            }
            Color previous = GUI.color;
            if (!GUI.enabled) GUI.color = new Color(previous.r, previous.g, previous.b, previous.a * 0.5f);
            GUI.Label(r, "> " + value + (focus && (int)(Time.realtimeSinceStartup * 2) % 2 == 0 ? "|" : ""), Label);
            GUI.color = previous;
            return value;
        }

        public bool Typing { get { return focused != null; } }

        // Mouse in the panel's (scaled) GUI coordinates.
        Vector2 Mouse { get { Vector3 m = Input.mousePosition; return new Vector2(m.x / scale, (Screen.height - m.y) / scale); } }
    }
}
