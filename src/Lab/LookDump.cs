using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab-only research (-mt-look): how the game lights its own characters next to the remote player's copy. Once per chapter,
    // 10 s after a copy exists, MT-LOOK lines list every renderer of the local body, Mita and the copy (layer, shader,
    // probes, a few toon material values) and the game scripts on the local body that the copy leaves out.
    public sealed partial class TogetherRuntime
    {
        static readonly string[] LookProps = { "_MainColor", "_HighlightColor", "_OverallShadowColor", "_SelfLitIntensity", "_EnvironmentalLightingIntensity",
            "_GIFlatShade", "_GIShadeThreshold", "_LightAffectShadow", "_DirectionalLightIntensity", "_PointSpotlightIntensity", "_LightIntensity", "_Color" };
        string lookScene;
        float lookAvatarAt = -1;
        bool lookDump;   // -mt-look

        void LookTick()
        {
            if (!lookDump) return;
            string scene = ActiveSceneName;
            if (scene == lookScene || scene == "SceneMenu") return;
            GameObject copy = null;
            foreach (Peer peer in peers.Values) if (peer.Avatar != null && peer.Avatar.IsModel && peer.Avatar.Root != null) { copy = peer.Avatar.Root; break; }
            if (copy == null) { lookAvatarAt = -1; return; }
            float now = Time.realtimeSinceStartup;
            if (lookAvatarAt < 0) lookAvatarAt = now;
            if (now - lookAvatarAt < 10) return;
            lookScene = scene;
            PlayerMove player = LocalPlayer;
            if (player != null && player.animPerson != null)
            {
                LookRenderers("me", player.animPerson.transform);
                HashSet<string> types = new HashSet<string>();
                foreach (Component c in player.animPerson.GetComponentsInChildren<Component>(true))
                    if (c != null) types.Add(c.GetIl2CppType().Name);
                foreach (Component c in player.animPerson.GetComponentsInParent<Component>(true))
                    if (c != null) types.Add("parent:" + c.GetIl2CppType().Name);
                Log("MT-LOOK me scripts " + string.Join(",", types));
            }
            // Every other character in the chapter (Mita and the rest, active or not), one line per skinned mesh.
            int others = 0;
            foreach (SkinnedMeshRenderer s in Object.FindObjectsOfType<SkinnedMeshRenderer>(true))
            {
                if (s == null || others >= 30 || s.transform.IsChildOf(copy.transform) || (player != null && s.transform.IsChildOf(player.transform))) continue;
                others++;
                LookRenderer("other:" + s.transform.root.name, s);
            }
            LookRenderers("copy", copy.transform);
            Log("MT-LOOK ambient mode=" + RenderSettings.ambientMode + " sky=" + RenderSettings.ambientSkyColor + " light=" + RenderSettings.ambientLight
                + " intensity=" + RenderSettings.ambientIntensity + " fog=" + RenderSettings.fog + " fogColor=" + RenderSettings.fogColor);
            foreach (Light l in Object.FindObjectsOfType<Light>())
                if (l != null && l.isActiveAndEnabled)
                    Log("MT-LOOK light " + l.name + " type=" + l.type + " mask=" + l.cullingMask + " color=" + l.color + " intensity=" + l.intensity + " range=" + l.range + " pos=" + l.transform.position);
        }

        void LookRenderers(string who, Transform root)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) if (r != null) LookRenderer(who, r);
        }

        void LookRenderer(string who, Renderer r)
        {
            {
                StringBuilder line = new StringBuilder("MT-LOOK ").Append(who).Append(' ').Append(r.name)
                    .Append(" on=").Append(r.enabled && r.gameObject.activeInHierarchy ? 1 : 0).Append(" layer=").Append(r.gameObject.layer)
                    .Append(" probes=").Append(r.lightProbeUsage).Append('/').Append(r.reflectionProbeUsage)
                    .Append(" anchor=").Append(r.probeAnchor != null ? r.probeAnchor.name : "-").Append(" rlm=").Append(r.renderingLayerMask)
                    .Append(" shadows=").Append(r.shadowCastingMode).Append('/').Append(r.receiveShadows ? 1 : 0).Append(" block=").Append(r.HasPropertyBlock() ? 1 : 0);
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    line.Append(" | ").Append(m.name).Append('#').Append(m.GetInstanceID()).Append(' ').Append(m.shader != null ? m.shader.name : "-");
                    foreach (string p in LookProps)
                    {
                        if (!m.HasProperty(p)) continue;
                        if (p.EndsWith("Color")) line.Append(' ').Append(p).Append('=').Append(m.GetColor(p));
                        else line.Append(' ').Append(p).Append('=').Append(m.GetFloat(p).ToString("0.##"));
                    }
                    line.Append(" kw=").Append(string.Join(" ", m.shaderKeywords));
                }
                Log(line.ToString());
            }
        }
    }
}
