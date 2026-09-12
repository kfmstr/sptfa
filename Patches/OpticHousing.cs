using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPTFreeAim.Patches
{
    /// <summary>
    /// Makes the optic's BODY see-through while you are aiming, so the sight
    /// stops being a brick in front of the eye that is not looking through it.
    ///
    /// Hiding and doubling are gone at the owner's request. This is transparency
    /// only, and it is a rewrite rather than a repair, because the first version
    /// rendered the sight BLACK.
    ///
    /// Why it was black (docs/07-FINDINGS.md F36): it swapped each material onto
    /// a blending shader and copied `_MainTex` across. EFT's weapon shaders do
    /// not keep their albedo in `_MainTex`, so the replacement shader received no
    /// texture at all and drew flat black - and nothing threw, because asking a
    /// material for a texture it does not have is a legal question with a null
    /// answer.
    ///
    /// So this version does not assume where the albedo lives. It reads the
    /// source shader's own property table, finds the texture properties, and
    /// picks the one that actually looks like albedo. If it cannot find one it
    /// LEAVES THE MATERIAL ALONE and says so in the log, because an untextured
    /// swap is exactly the black brick this is meant to fix.
    /// </summary>
    public static class OpticHousing
    {
        public struct Options
        {
            public bool Enabled;
            public float Alpha;      // 0 = solid, 0.8 = mostly gone
            public bool LogMaterials;
        }

        public static string Status = "not started";

        // Renderer -> the exact array it had before we touched it. Restoring the
        // ARRAY and not just element 0 is why this one comes back cleanly.
        private static readonly Dictionary<Renderer, Material[]> _original =
            new Dictionary<Renderer, Material[]>();

        // Source material -> our transparent clone, so a weapon with six parts
        // sharing one material makes one clone, not six.
        private static readonly Dictionary<Material, Material> _clones =
            new Dictionary<Material, Material>();

        private static Shader _blend;
        private static bool _searched;
        private static bool _logged;

        public static void Frame(Transform housingRoot, float aimBlend, Options o)
        {
            if (!o.Enabled || o.Alpha <= 0.001f || housingRoot == null || aimBlend <= 0.01f)
            {
                RestoreAll();
                return;
            }

            if (!_searched) FindBlendShader();
            if (_blend == null) { RestoreAll(); return; }

            // Ramp with the aim blend so it fades in with the weapon coming up
            // rather than popping at a threshold.
            float alpha = Mathf.Lerp(1f, 1f - Mathf.Clamp01(o.Alpha), Mathf.Clamp01(aimBlend));

            Renderer lens = Compat.GameRefs.GetCurrentLensRenderer();
            Renderer[] rs = housingRoot.GetComponentsInChildren<Renderer>(false);
            int touched = 0;
            StringBuilder report = o.LogMaterials && !_logged ? new StringBuilder() : null;

            foreach (Renderer r in rs)
            {
                if (r == null) continue;

                // The lens and the reticle are the parts you look THROUGH. They
                // are the point of the sight; leave them exactly as they are.
                //
                // Matched by REFERENCE against OpticSight.LensRenderer, not by
                // name. The name match missed on the EOTech - two parts were
                // swapped, one of them the glass, and the window went opaque
                // whatever the alpha was, because replacing a specialised glass
                // shader with a generic one ruins it before alpha is even
                // considered. F48.
                if (ReferenceEquals(r, lens)) continue;
                if (Compat.GameRefs.IsReticleObject(r.gameObject)) continue;

                Material[] src;
                if (!_original.TryGetValue(r, out src))
                {
                    src = r.sharedMaterials;
                    _original[r] = src;
                }

                Material[] swapped = new Material[src.Length];
                bool any = false;
                for (int i = 0; i < src.Length; i++)
                {
                    Material clone = CloneFor(src[i], report);
                    if (clone == null) { swapped[i] = src[i]; continue; }
                    clone.SetColor("_Color", WithAlpha(clone.GetColor("_Color"), alpha));
                    swapped[i] = clone;
                    any = true;
                }

                if (any) { r.sharedMaterials = swapped; touched++; }
            }

            Status = touched > 0
                ? string.Format("{0} parts at alpha {1:F2} via {2}", touched, alpha, _blend.name)
                : "no part of the housing had a texture we could carry over";

            if (report != null)
            {
                _logged = true;
                Plugin.Log.LogInfo("Sight materials under " + housingRoot.name + ":\n" + report);
            }
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        /// <summary>
        /// Build (once) a transparent stand-in for a material, carrying over
        /// whatever texture the source actually uses as albedo. Returns null when
        /// there is nothing to carry - the caller then leaves the original alone.
        /// </summary>
        private static Material CloneFor(Material src, StringBuilder report)
        {
            if (src == null) return null;

            Material clone;
            if (_clones.TryGetValue(src, out clone)) return clone;

            string where;
            Texture albedo = FindAlbedo(src, out where, report);

            if (albedo == null)
            {
                // THE fix. No texture means the replacement shader would draw
                // flat black, which is what the owner saw. Refuse instead.
                _clones[src] = null;
                return null;
            }

            clone = new Material(_blend);
            clone.name = src.name + " (see-through)";
            clone.mainTexture = albedo;

            if (src.HasProperty("_Color") && clone.HasProperty("_Color"))
                clone.SetColor("_Color", src.GetColor("_Color"));

            // Transparent geometry has to sort after everything opaque or it
            // punches a hole in whatever is behind it.
            // Assembly-CSharp declares its own RenderQueue, so this one is spelled
            // out in full rather than resolved through the using directive.
            clone.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            _clones[src] = clone;
            return clone;
        }

        /// <summary>
        /// Where does this material keep its albedo? Ask the shader instead of
        /// guessing: walk its property table, keep the texture properties, and
        /// prefer the one whose name reads like a base colour map.
        /// </summary>
        private static Texture FindAlbedo(Material m, out string where, StringBuilder report)
        {
            where = "none";
            Shader sh = m.shader;
            if (sh == null) return null;

            if (report != null)
                report.AppendLine("  " + m.name + "   shader: " + sh.name);

            Texture best = null;
            int bestScore = -1;

            int n = sh.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                if (sh.GetPropertyType(i) != ShaderPropertyType.Texture) continue;

                string name = sh.GetPropertyName(i);
                Texture tex = m.GetTexture(name);

                if (report != null)
                    report.AppendLine("      " + name + " = " +
                        (tex == null ? "(null)" : tex.name));

                if (tex == null) continue;

                int score = Score(name);
                if (score > bestScore) { bestScore = score; best = tex; where = name; }
            }

            return best;
        }

        private static int Score(string prop)
        {
            string p = prop.ToLowerInvariant();
            if (p.Contains("albedo") || p.Contains("basecolor") || p.Contains("basemap")) return 5;
            if (p.Contains("maintex") || p == "_main") return 4;
            if (p.Contains("diffuse") || p.Contains("_diff")) return 3;
            if (p.Contains("color") || p.Contains("_base")) return 2;
            // Anything obviously NOT albedo is worse than nothing else on offer.
            if (p.Contains("normal") || p.Contains("bump") || p.Contains("mask") ||
                p.Contains("metal") || p.Contains("rough") || p.Contains("gloss") ||
                p.Contains("ao") || p.Contains("occl") || p.Contains("emis") ||
                p.Contains("detail") || p.Contains("spec")) return -1;
            return 1;
        }

        /// <summary>
        /// Find a shader that actually blends. Shader.Find only sees shaders the
        /// build kept, so a miss is normal and not an error - fall back to
        /// whatever transparent shader is already loaded in the game, and log the
        /// candidates either way so the next round is informed.
        /// </summary>
        private static void FindBlendShader()
        {
            _searched = true;

            string[] names =
            {
                "Legacy Shaders/Transparent/Diffuse",
                "Transparent/Diffuse",
                "Unlit/Transparent",
                "Sprites/Default",
                "Mobile/Particles/Alpha Blended"
            };

            foreach (string n in names)
            {
                Shader s = Shader.Find(n);
                if (s != null) { _blend = s; break; }
            }

            if (_blend == null)
            {
                var found = new List<string>();
                Shader[] all = Resources.FindObjectsOfTypeAll<Shader>();
                foreach (Shader s in all)
                {
                    if (s == null || s.name == null) continue;
                    string ln = s.name.ToLowerInvariant();
                    if (ln.Contains("transparent") || ln.Contains("alpha") || ln.Contains("blend"))
                    {
                        if (found.Count < 20) found.Add(s.name);
                        if (_blend == null && (ln.Contains("diffuse") || ln.Contains("unlit")))
                            _blend = s;
                    }
                }
                if (_blend == null && found.Count > 0) _blend = Shader.Find(found[0]);

                Plugin.Log.LogInfo("Sight transparency: no built-in blend shader; " +
                    (found.Count == 0
                        ? "and nothing loaded looks transparent either."
                        : "loaded candidates: " + string.Join(", ", found.ToArray())));
            }

            Status = _blend == null
                ? "no shader that blends - transparency cannot work on this build"
                : "using " + _blend.name;

            if (_blend == null) Plugin.Log.LogWarning("Sight transparency: " + Status);
            else Plugin.Log.LogInfo("Sight transparency: " + Status);
        }

        /// <summary>Put every renderer back the way it was. Never throws.</summary>
        public static void RestoreAll()
        {
            if (_original.Count == 0) return;
            try
            {
                foreach (var kv in _original)
                    if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
            }
            catch { }
            _original.Clear();
        }

        public static void Release()
        {
            RestoreAll();
            Status = "released";
        }
    }
}
