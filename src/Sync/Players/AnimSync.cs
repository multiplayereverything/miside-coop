using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiSideTogether
{
    // One animator snapshot: layer-0 state, the clip actually playing (MiSide swaps clips at runtime through
    // AnimatorOverrideControllers for cutscenes and interactions), and the controller parameters.
    public sealed class AnimFrame
    {
        public const int MaxParams = 48;
        public int StateHash;
        public float NormTime;
        public string Clip = "";
        public readonly List<int> ParamHashes = new List<int>();
        public readonly List<byte> ParamTypes = new List<byte>();
        public readonly List<float> ParamValues = new List<float>();

        public void Clear() { StateHash = 0; NormTime = 0; Clip = ""; ParamHashes.Clear(); ParamTypes.Clear(); ParamValues.Clear(); }

        public void Capture(Animator anim)
        {
            Clear();
            if (anim == null || !anim.isActiveAndEnabled || anim.runtimeAnimatorController == null) return;
            bool next = anim.IsInTransition(0);
            AnimatorStateInfo state = next ? anim.GetNextAnimatorStateInfo(0) : anim.GetCurrentAnimatorStateInfo(0);
            StateHash = state.fullPathHash;
            NormTime = state.normalizedTime;
            if (float.IsNaN(NormTime) || float.IsInfinity(NormTime)) NormTime = 0;
            NormTime = Mathf.Clamp(NormTime, 0, 10000);
            Clip = AnimSync.PlayingClip(anim, next);
            foreach (AnimSync.Param p in AnimSync.Parameters(anim))
            {
                if (ParamHashes.Count >= MaxParams) break;
                float value;
                if (!AnimSync.TryGet(anim, p, out value)) continue;
                if (float.IsNaN(value) || float.IsInfinity(value)) value = 0;
                ParamHashes.Add(p.Hash); ParamTypes.Add((byte)p.Type); ParamValues.Add(Mathf.Clamp(value, -10000, 10000));
            }
        }

        public void Write(BinaryWriter w)
        {
            w.Write(StateHash); w.Write(NormTime); Wire.Text(w, Clip);
            w.Write((byte)ParamHashes.Count);
            for (int i = 0; i < ParamHashes.Count; i++) { w.Write(ParamHashes[i]); w.Write(ParamTypes[i]); w.Write(ParamValues[i]); }
        }

        public void Read(BinaryReader r)
        {
            Clear();
            StateHash = r.ReadInt32(); NormTime = Wire.Number(r); Clip = Wire.Text(r, 128);
            int count = r.ReadByte();
            if (count > MaxParams) throw new IOException("Too many animator parameters.");
            for (int i = 0; i < count; i++) { ParamHashes.Add(r.ReadInt32()); ParamTypes.Add(r.ReadByte()); ParamValues.Add(Wire.Number(r)); }
        }
    }

    // Per-animator receiving state, so a remote frame only restarts a state when the sender actually changed state.
    public sealed class AnimTarget
    {
        public Animator Animator;
        public AnimatorOverrideController Override;
        public int AppliedState;
        public float AppliedAt;
        public string AppliedClip = "";
        // original clip name -> clip currently forced into its slot
        public readonly Dictionary<string, string> Swapped = new Dictionary<string, string>();
    }

    // MiSide's IL2CPP build strips Animator members the game never calls (GetParameter, the hash-based getters, ...),
    // and Il2CppInterop cannot load Animator.parameters at all. Every call here therefore has a fallback, and an API
    // that fails once is not tried again.
    public static class AnimSync
    {
        public struct Param { public int Hash; public string Name; public AnimatorControllerParameterType Type; }

        static readonly Dictionary<IntPtr, Param[]> parameterCache = new Dictionary<IntPtr, Param[]>();
        static readonly HashSet<string> broken = new HashSet<string>();
        public static string Status = "";

        static bool Usable(string api) { return !broken.Contains(api); }

        static void Broke(string api, Exception ex)
        {
            if (broken.Add(api)) { Status += api + " "; TogetherRuntime.Log("Animator API unavailable in this build: " + api + " (" + ex.GetType().Name + ": " + ex.Message + ")"); }
        }

        public static Param[] Parameters(Animator anim)
        {
            RuntimeAnimatorController controller = anim.runtimeAnimatorController;
            if (controller == null) return new Param[0];
            Param[] list;
            if (parameterCache.TryGetValue(controller.Pointer, out list)) return list;
            AnimatorOverrideController over = controller.TryCast<AnimatorOverrideController>();
            RuntimeAnimatorController baseController = over != null && over.runtimeAnimatorController != null ? over.runtimeAnimatorController : controller;
            string name = baseController.name;
            List<Param> found;
            list = name != null && Table.TryGetValue(name, out found) ? found.ToArray() : new Param[0];
            if (parameterCache.Count > 256) parameterCache.Clear();
            parameterCache[controller.Pointer] = list;
            return list;
        }

        // The build reads every AnimatorController's parameter list from the user's own game data
        // (tools/extract_animator_params.py) and embeds it, because the runtime cannot enumerate parameters.
        static Dictionary<string, List<Param>> table;
        static Dictionary<string, List<Param>> Table
        {
            get
            {
                if (table != null) return table;
                table = new Dictionary<string, List<Param>>();
                try
                {
                    using (Stream s = typeof(AnimSync).Assembly.GetManifestResourceStream("MiSideTogether.animator-params.tsv"))
                    using (StreamReader reader = new StreamReader(s))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string[] f = line.Split('\t');
                            int type;
                            if (f.Length != 3 || !int.TryParse(f[2], out type)) continue;
                            // Triggers are one-shot events the receiver's own state sync already covers.
                            if (type == (int)AnimatorControllerParameterType.Trigger) continue;
                            List<Param> list;
                            if (!table.TryGetValue(f[0], out list)) table[f[0]] = list = new List<Param>();
                            list.Add(new Param { Name = f[1], Hash = StringToHash(f[1]), Type = (AnimatorControllerParameterType)type });
                        }
                    }
                }
                catch (Exception ex) { TogetherRuntime.Log("Animator parameter table unavailable: " + ex.Message); }
                TogetherRuntime.Log("Animator parameter table: " + table.Count + " controllers");
                return table;
            }
        }

        // Animator.StringToHash is CRC-32 (IEEE) of the name; computed here so it cannot be lost to stripping.
        static uint[] crcTable;
        public static int StringToHash(string name)
        {
            if (crcTable == null)
            {
                crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    crcTable[n] = c;
                }
            }
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(name)) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return (int)(crc ^ 0xFFFFFFFFu);
        }

        // The game itself drives its animators by parameter name, so the string overloads survive stripping.
        public static bool TryGet(Animator anim, Param p, out float value)
        {
            value = 0;
            string api = "get-" + p.Type;
            if (!Usable(api)) return false;
            try
            {
                if (p.Type == AnimatorControllerParameterType.Float) value = anim.GetFloat(p.Name);
                else if (p.Type == AnimatorControllerParameterType.Int) value = anim.GetInteger(p.Name);
                else if (p.Type == AnimatorControllerParameterType.Bool) value = anim.GetBool(p.Name) ? 1 : 0;
                else return false;
                return true;
            }
            catch (Exception ex) { Broke(api, ex); return false; }
        }

        static void TrySet(Animator anim, Param p, float value)
        {
            string api = "set-" + p.Type;
            if (!Usable(api)) return;
            try
            {
                if (p.Type == AnimatorControllerParameterType.Float) anim.SetFloat(p.Name, value);
                else if (p.Type == AnimatorControllerParameterType.Int) anim.SetInteger(p.Name, (int)value);
                else if (p.Type == AnimatorControllerParameterType.Bool) anim.SetBool(p.Name, value > 0.5f);
            }
            catch (Exception ex) { Broke(api, ex); }
        }

        public static string PlayingClip(Animator anim, bool next)
        {
            string api = next ? "GetNextAnimatorClipInfo" : "GetCurrentAnimatorClipInfo";
            if (!Usable(api)) return "";
            try
            {
                var infos = next ? anim.GetNextAnimatorClipInfo(0) : anim.GetCurrentAnimatorClipInfo(0);
                string best = ""; float weight = -1;
                for (int i = 0; i < infos.Length; i++)
                {
                    AnimatorClipInfo info = infos[i];
                    if (info.clip != null && info.weight > weight) { weight = info.weight; best = info.clip.name; }
                }
                return best;
            }
            catch (Exception ex) { Broke(api, ex); return ""; }
        }

        public static void Apply(AnimTarget target, AnimFrame frame, bool forceState)
        {
            Animator anim = target.Animator;
            if (anim == null || !anim.isActiveAndEnabled || anim.runtimeAnimatorController == null || frame.StateHash == 0) return;
            if (frame.ParamHashes.Count > 0)
            {
                Param[] local = Parameters(anim);
                for (int i = 0; i < frame.ParamHashes.Count; i++)
                    for (int j = 0; j < local.Length; j++)
                        if (local[j].Hash == frame.ParamHashes[i]) { TrySet(anim, local[j], frame.ParamValues[i]); break; }
            }
            // The sender's clip goes into the receiver's slot for this state before the state starts, so a cutscene
            // clip the sender's override controller swapped in also plays here.
            if (target.Override != null && frame.Clip.Length > 0 && frame.Clip != target.AppliedClip) SwapClip(target, frame.Clip);
            AnimatorStateInfo now = anim.GetCurrentAnimatorStateInfo(0);
            bool changed = frame.StateHash != target.AppliedState;
            // Re-sync a state that drifted (e.g. the local controller transitioned away on its own) once a second.
            bool drifted = !anim.IsInTransition(0) && now.fullPathHash != frame.StateHash && Time.realtimeSinceStartup - target.AppliedAt > 1f;
            if ((changed || drifted || forceState) && HasState(anim, frame.StateHash))
            {
                float t = frame.NormTime;
                if (t > 1) t -= Mathf.Floor(t);
                Play(anim, frame.StateHash, changed ? 0.15f : 0.05f, t);
                target.AppliedState = frame.StateHash;
                target.AppliedAt = Time.realtimeSinceStartup;
            }
        }

        static bool HasState(Animator anim, int hash)
        {
            if (!Usable("HasState")) return true;
            try { return anim.HasState(0, hash); }
            catch (Exception ex) { Broke("HasState", ex); return true; }
        }

        static void Play(Animator anim, int hash, float fade, float time)
        {
            if (Usable("CrossFade"))
            {
                try { anim.CrossFade(hash, fade, 0, time); return; }
                catch (Exception ex) { Broke("CrossFade", ex); }
            }
            if (Usable("Play"))
            {
                try { anim.Play(hash, 0, time); }
                catch (Exception ex) { Broke("Play", ex); }
            }
        }

        static void SwapClip(AnimTarget target, string clipName)
        {
            target.AppliedClip = clipName;
            if (!Usable("SetClipByName")) return;
            Animator anim = target.Animator;
            string playing = PlayingClip(anim, false);
            if (playing.Length == 0 || playing == clipName) return;
            string original = playing;
            foreach (KeyValuePair<string, string> pair in target.Swapped) if (pair.Value == playing) { original = pair.Key; break; }
            AnimationClip clip = FindClip(clipName);
            if (clip == null) return;
            try
            {
                target.Override.Internal_SetClipByName(original, clip);
                target.Swapped[original] = clipName;
            }
            catch (Exception ex) { Broke("SetClipByName", ex); }
        }

        static readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        static float clipsScannedAt = -100;

        public static AnimationClip FindClip(string name)
        {
            AnimationClip clip;
            if (clips.TryGetValue(name, out clip) && clip != null) return clip;
            if (Time.realtimeSinceStartup - clipsScannedAt < 2) return null;
            clipsScannedAt = Time.realtimeSinceStartup;
            clips.Clear();
            foreach (AnimationClip c in Resources.FindObjectsOfTypeAll<AnimationClip>())
                if (c != null && !clips.ContainsKey(c.name)) clips[c.name] = c;
            return clips.TryGetValue(name, out clip) ? clip : null;
        }
    }
}
