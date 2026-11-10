using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab-only screenshots, to compare what each player sees. LabShot("label") reads the finished frame on the next
    // OnGUI repaint (cameras and UI already drawn) and writes shots/<label>.rgb next to the game: a 2 x int32 header
    // (width, height) and RGB rows bottom-up. Texture2D.EncodeToPNG is stripped in this build; tools/shots_to_png.py
    // turns the files into PNGs. "lab-shot:<label>" notices make every machine take one at the same moment.
    public sealed partial class TogetherRuntime
    {
        string labShotPending;

        public static bool noShots;   // -mt-noshots: tell the screenshots' own memory use apart from the mod's
        public void LabShot(string label) { if (LabMode && !noShots) labShotPending = label; }

        // Every machine at once: <label>-Host, <label>-C1, ...
        void LabShotAll(string label)
        {
            LabShot(label + "-" + Name);
            LabWhere(label);
            Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-shot:" + label)));
        }

        // Beside each picture: the loaded scenes, and where the player and camera really are (a black picture is often a player left in the void).
        void LabWhere(string label)
        {
            System.Text.StringBuilder s = new System.Text.StringBuilder("MT-WHERE " + label + " scenes=");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) s.Append(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name).Append('|');
            foreach (PlayerMove p in UnityEngine.Object.FindObjectsOfType<PlayerMove>(true))
                s.Append(" player[").Append(p.gameObject.scene.name).Append(p.gameObject.activeInHierarchy ? ",on" : ",off").Append(p == LocalPlayer ? ",local]=" : "]=").Append(p.transform.position.ToString("0.0"));
            Camera cam = Camera.main;
            s.Append(" cam=").Append(cam != null ? cam.transform.position.ToString("0.0") + "@" + cam.gameObject.scene.name : "none");
            // Screens in the scene (a phone, a console) are cameras drawing into a texture.
            foreach (Camera c in UnityEngine.Object.FindObjectsOfType<Camera>(true))
                if (c.targetTexture != null) s.Append(" screen[").Append(PathOf(c.transform)).Append(c.gameObject.activeInHierarchy ? ",on" : ",off").Append(c.enabled ? "" : ",disabled").Append(",mask ").Append(c.cullingMask.ToString("x")).Append("]=").Append(c.transform.position.ToString("0.0"));
            Log(s.ToString());
        }

        public int labPanelDraws, labPanelLocked;
        void LabPanelDrawn() { labPanelDraws++; if (Cursor.lockState != CursorLockMode.None || !Cursor.visible) labPanelLocked++; }

        void LabShotOnGui()
        {
            if (labShotPending == null || Event.current == null || Event.current.type != EventType.Repaint) return;
            string label = labShotPending;
            labShotPending = null;
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) { Log("Lab: shot " + label + " skipped (no screen)"); return; }
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                Color32[] px = tex.GetPixels32();
                byte[] data = new byte[8 + px.Length * 3];
                System.BitConverter.GetBytes(w).CopyTo(data, 0);
                System.BitConverter.GetBytes(h).CopyTo(data, 4);
                for (int i = 0; i < px.Length; i++) { data[8 + i * 3] = px[i].r; data[9 + i * 3] = px[i].g; data[10 + i * 3] = px[i].b; }
                string dir = Path.Combine(Root, "shots");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, label + ".rgb");
                File.WriteAllBytes(path, data);
                Log("Lab: shot " + label + " (" + w + "x" + h + ")");
            }
            finally { Object.Destroy(tex); }
        }
    }
}
