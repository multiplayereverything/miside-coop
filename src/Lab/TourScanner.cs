using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "scanner" (2 players, Scene 2): "Bring these to the scanner" (2026-11-08 report: items stuck in players' hands).
    // The host starts the scanner; the client takes the Spoon and the Bowknot, the host the Pencil. Each player walks to the
    // scanner with their item and puts it there with their own press, as in the game. Presses are a player's (TourScanner's
    // LabScannerClick), not Click() from code.
    //   scanner.waits:  (each) 5 s after the first take the item is still with the player, not on the scanner by itself
    //   scanner.placed: here, all three lie on the scanner and nothing is left in any hand
    //   scanner.same:   (client) each item is where the host has it, within 0.15 m
    public sealed partial class TogetherRuntime
    {
        // The scanner's own copies (the Bowknot's is "Scrunchy"), and what each player takes (ItemTake <name>).
        static readonly string[] LabScannerItems = { "Spoon", "Pencil", "Scrunchy" };
        int labScannerStep, labScannerTaken;
        float labScannerAt, labScannerLogAt;

        static T LabFind<T>(string pathEnd) where T : Component
        {
            foreach (T c in Object.FindObjectsOfType<T>(true)) if (c != null && PathOf(c.transform).EndsWith(pathEnd, StringComparison.Ordinal)) return c;
            return null;
        }

        // Found once at the start: placing an item moves it under the scanner, so its path changes.
        readonly Dictionary<string, Transform> labScannerFound = new Dictionary<string, Transform>();
        Transform LabScannerItem(string name)
        {
            Transform t;
            if (labScannerFound.TryGetValue(name, out t) && t != null) return t;
            Transform_Magnet m = LabFind<Transform_Magnet>("Act General/" + name + "/");
            t = m == null ? null : (m.transformTarget != null ? m.transformTarget : m.transform);
            if (t != null) labScannerFound[name] = t;
            return t;
        }

        // Presses when the interactive is armed here, the way a player does: the game's own ObjectInteractive.Update reads
        // the mouse and runs the click itself, never calling Click() (2026-11-08: no player press reached the mod's Click
        // hook in a real session). The lab only aims at the object and holds the mouse button down for its Update.
        static ObjectInteractive labPressTarget;
        static bool labPressInTarget;
        static int labPressFrames;
        bool labPressHooked;

        static void LabPressUpdatePrefix(ObjectInteractive __instance)
        {
            labPressInTarget = labPressFrames > 0 && labPressTarget != null && __instance.Pointer == labPressTarget.Pointer;
            // What the player's look ray hits, as the game compares it: objectCastInteractive with the object's
            // objectInteractive, or (hand-reach objects, interactiveIK) objectCastHandInteractive with its GameObject.
            if (labPressInTarget && __instance.scrpm != null)
            {
                __instance.scrpm.objectCastInteractive = __instance.objectInteractive;
                __instance.scrpm.objectCastHandInteractive = __instance.gameObject;
            }
        }
        static void LabPressUpdatePostfix() { if (labPressInTarget) { labPressInTarget = false; labPressFrames--; } }
        static void LabPressMousePostfix(int __0, ref bool __result) { if (labPressInTarget && __0 == 0) __result = true; }

        bool LabScannerClick(string pathEnd)
        {
            if (!labPressHooked)
            {
                labPressHooked = true;
                HarmonyLib.Harmony lab = new HarmonyLib.Harmony("local.misidetogether.lab.press");
                // The held button comes first, so the mod's own hooks see it as a player's press.
                lab.Patch(HarmonyLib.AccessTools.Method(typeof(ObjectInteractive), "Update"), new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabPressUpdatePrefix)), new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabPressUpdatePostfix)));
                lab.Patch(HarmonyLib.AccessTools.Method(typeof(Input), "GetMouseButtonDown", new[] { typeof(int) }), null, new HarmonyLib.HarmonyMethod(typeof(TogetherRuntime), nameof(LabPressMousePostfix)) { priority = HarmonyLib.Priority.First });
            }
            ObjectInteractive o = LabFind<ObjectInteractive>(pathEnd);
            // A player in an animation can't click (the game checks animationRun), so the lab waits like a player would.
            if (o == null || !o.active || !o.isActiveAndEnabled || labPressFrames > 0 || LocalPlayer == null || LocalPlayer.animationRun) return false;
            Log("Lab: scanner tour presses " + pathEnd + " with the mouse");
            labPressTarget = o; labPressFrames = 1;
            return true;
        }

        // Where an item is held, if anyone holds it: this player, or another player's body copy.
        string LabHolder(Transform item)
        {
            if (LocalPlayer != null && item.IsChildOf(LocalPlayer.transform)) return "this player";
            foreach (Peer p in peers.Values) if (p.Avatar != null && p.Avatar.Root != null && item.IsChildOf(p.Avatar.Root.transform)) return p.Name;
            Transform scanner = LabFind<Transform>("Table MehMita/Scanner/");
            if (LocalPlayer != null && scanner != null && (item.position - scanner.position).sqrMagnitude > 1f
                && (item.position - LocalPlayer.transform.position).sqrMagnitude < 4f) return "near this player";
            return null;
        }

        // Anything shown in a hand here: this player's own hand item, and every active object on this player or on another
        // player's body copy named after a scanner item (the taken one, or the mod's copy shown in a remote hand).
        string LabHands()
        {
            List<string> found = new List<string>();
            PlayerMove me = LocalPlayer;
            if (me != null && me.item != null) found.Add("this player's hand item " + me.item.name + (me.item.activeInHierarchy ? "" : " (hidden)"));
            List<Transform> roots = new List<Transform>();
            if (me != null) roots.Add(me.transform);
            foreach (Peer p in peers.Values) if (p.Avatar != null && p.Avatar.Root != null) roots.Add(p.Avatar.Root.transform);
            foreach (Transform root in roots)
                foreach (Transform t in root.GetComponentsInChildren<Transform>())
                    if (t != null && t.gameObject.activeInHierarchy && (t.name.Contains("Spoon") || t.name.Contains("Pencil") || t.name.Contains("Bow") || t.name.Contains("Scrunchy"))) found.Add(t.name + " on " + root.name);
            return found.Count == 0 ? null : string.Join(", ", found);
        }

        string LabScannerPlaces()
        {
            List<string> all = new List<string>();
            foreach (string n in LabScannerItems)
            {
                Transform t = LabScannerItem(n);
                Vector3 p = t != null ? t.position : Vector3.zero;
                all.Add(n + "=" + p.x.ToString("0.00", CultureInfo.InvariantCulture) + "," + p.y.ToString("0.00", CultureInfo.InvariantCulture) + "," + p.z.ToString("0.00", CultureInfo.InvariantCulture));
            }
            return string.Join(";", all);
        }

        void ScannerTour(float now, string scene)
        {
            if (scene != "Scene 2 - InGame" || LocalPlayer == null || now - tourSceneAt < 25) return;
            if (now >= labScannerLogAt)
            {
                labScannerLogAt = now + 1;
                foreach (string n in LabScannerItems) { Transform t = LabScannerItem(n); if (t != null) Log("Lab: scanner " + n + " at " + PathOf(t.parent) + " " + t.position + " held by " + (LabHolder(t) ?? "nobody")); }
                Log("Lab: scanner hands: " + (LabHands() ?? "empty"));
            }
            if (labScannerStep == 0)
            {
                // The quest is switched on here the way its own start would.
                ObjectInteractive start = LabFind<ObjectInteractive>("Quest 1 Start/Interactive ButtonMeh/");
                if (start == null) { if (now - tourSceneAt > 60) CheckOnce("scanner.placed", false, "no scanner button in " + scene); return; }
                for (Transform t = start.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                foreach (string n in LabScannerItems) Log("Lab: scanner " + n + " starts at " + PathOf(LabScannerItem(n)));
                labScannerStep = 1; labScannerAt = now;
                return;
            }
            float since = now - labScannerAt;
            // What each player takes, and the scanner slot it goes to.
            string[] mine = Hosting ? new[] { "Pencil" } : new[] { "Spoon", "Bow" };
            string[] slots = Hosting ? new[] { "Pencil" } : new[] { "Spoon", "Scrunchy" };
            switch (labScannerStep)
            {
                case 1:
                    if (Hosting && since > 3) { if (LabScannerClick("Quest 1 Start/Interactive ButtonMeh/")) { labScannerStep = 2; labScannerAt = now; } }
                    else if (!Hosting) { labScannerStep = 2; labScannerAt = now; }
                    return;
                case 2:
                    // Each takes their items once armed here (the client after the host's button comes through the log), in turn.
                    float wait = Hosting ? 18 : labScannerTaken == 0 ? 4 : 10;
                    if (since > wait && LabScannerClick("ItemTake " + mine[labScannerTaken] + "/")) { labScannerStep = 3; labScannerAt = now; }
                    return;
                case 3:
                    // The item stays with the player until they put it on the scanner themselves.
                    if (since < 5) return;
                    Transform item = LabScannerItem(slots[labScannerTaken]), table = LabFind<Transform>("Table MehMita/Scanner/");
                    if (item != null && table != null && labScannerTaken == 0)
                        CheckOnce("scanner.waits", (item.position - table.position).sqrMagnitude > 1f, slots[labScannerTaken] + " is " + (item.position - table.position).magnitude.ToString("0.00") + " m from the scanner before this player puts it there");
                    labScannerStep = 5; labScannerAt = now;
                    return;
                case 5:
                    // Walk to the scanner (about 3 m a second), then put the item down.
                    Transform scan = LabFind<Transform>("Table MehMita/Scanner/");
                    if (scan == null) return;
                    Vector3 to = scan.position + (LocalPlayer.transform.position - scan.position).normalized * 1.2f; to.y = LocalPlayer.transform.position.y;
                    if ((LocalPlayer.transform.position - to).sqrMagnitude > 0.04f) { LocalPlayer.transform.position = Vector3.MoveTowards(LocalPlayer.transform.position, to, 3f * Time.deltaTime); return; }
                    if (!LabScannerClick("GiveItems/" + slots[labScannerTaken] + "/")) return;
                    labScannerAt = now;
                    labScannerStep = ++labScannerTaken == mine.Length ? 4 : 2;
                    return;
                case 4:
                    if (since < 25) return;
                    labScannerStep = 6;
                    List<string> wrong = new List<string>();
                    Transform scanner = LabFind<Transform>("Table MehMita/Scanner/");
                    foreach (string n in LabScannerItems)
                    {
                        Transform t = LabScannerItem(n);
                        if (t == null) { wrong.Add(n + " missing"); continue; }
                        string holder = LabHolder(t);
                        float d = scanner != null ? (t.position - scanner.position).magnitude : -1;
                        if (holder != null) wrong.Add(n + " held by " + holder);
                        else if (d < 0 || d > 1f) wrong.Add(n + " " + d.ToString("0.00") + " m from the scanner");
                    }
                    string hands = LabHands();
                    if (hands != null) wrong.Add("still in hand: " + hands);
                    CheckOnce("scanner.placed", wrong.Count == 0, wrong.Count == 0 ? "all on the scanner: " + LabScannerPlaces() : string.Join(", ", wrong));
                    if (Hosting) Broadcast(Wire.Packet(Op.Notice, w => Wire.Text(w, "lab-scanner:" + LabScannerPlaces())));
                    return;
            }
        }

        void ScannerSameNotice(string notice)
        {
            string host = notice.Substring("lab-scanner:".Length), here = LabScannerPlaces();
            List<string> far = new List<string>();
            string[] h = host.Split(';'), m = here.Split(';');
            for (int i = 0; i < h.Length && i < m.Length; i++)
            {
                Vector3 a = LabParse(h[i]), b = LabParse(m[i]);
                if ((a - b).magnitude > 0.15f) far.Add(h[i].Split('=')[0] + " " + (a - b).magnitude.ToString("0.00") + " m apart");
            }
            CheckOnce("scanner.same", far.Count == 0, "host: " + host + " | here: " + here);
        }

        static Vector3 LabParse(string item)
        {
            string[] v = item.Substring(item.IndexOf('=') + 1).Split(',');
            return new Vector3(float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture), float.Parse(v[2], CultureInfo.InvariantCulture));
        }
    }
}
