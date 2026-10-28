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

        public void LabShot(string label) { if (LabMode) labShotPending = label; }

        // Every machine at once: <label>-Host, <label>-C1, ...
        void LabShotAll(string label)
        {
            LabShot(label + "-" + Name);
            Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-shot:" + label)));
        }

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
