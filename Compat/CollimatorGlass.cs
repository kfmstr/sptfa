using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SPTFreeAim.Compat
{
    /// <summary>
    /// The red dot's glass, which is a material and not a camera.
    ///
    /// F54 established that a collimator has no scope image: no camera, no render
    /// texture, no PostProcessVolume, so nothing in OpticStack can reach it. What
    /// it does have is one mesh with one material, and F59's dump finally said
    /// what that material exposes:
    ///
    ///     MATERIAL scope_base_aimpoint_micro_t1_LOD0_linza
    ///     shader   CW FX/Collimator     renderQueue 4011
    ///         Color    _Color      = RGBA(1.000, 0.071, 0.071, 1.000)
    ///         Texture  _NoiseTex   = none
    ///         Texture  _MarkTex    = scope_base_aimpoint_micro_t1_mark
    ///         Texture  _FadeTex    = mask2
    ///         Vector   _MarkShift  = (0.00, -150.00, 0.00, 0.00)
    ///         Float    _MarkScale  = 1
    ///         Float    _HDR        = 3
    ///
    /// Seven properties, and what they are is readable from their values:
    ///
    ///   _Color      the RETICLE colour. That is not red-ish by accident, it is
    ///               an Aimpoint Micro T-1 and its dot is red.
    ///   _HDR        3, a multiplier above white. This is the dot's headroom, and
    ///               it is what makes a red dot BLOOM on the main camera at night.
    ///               The closest thing to the "flare" the owner asked for, and it
    ///               is already wired, just turned down.
    ///   _MarkTex    the reticle shape. "Mark" is BSG's word for reticle here, not
    ///               for a smudge - _MarkShift is the parallax offset and
    ///               _MarkScale its size.
    ///   _FadeTex    the edge fade mask, how the glass falls off at the rim.
    ///   _NoiseTex   EMPTY. The only free slot on the whole shader, and named
    ///               exactly like a glass-imperfection input.
    ///
    /// What is NOT there, and so is not possible: any tint on the world seen
    /// THROUGH the glass. There is no albedo, no _TintColor, no base map. This
    /// shader draws a reticle and fades at the edges; it does not colour what is
    /// behind it. A green-tinted window is not available at any effort.
    ///
    /// A NOTE ON sharedMaterial. CollimatorSight.Awake caches
    /// CollimatorMeshRenderer.sharedMaterial, so the game writes to the shared
    /// asset itself. Swapping in a per-renderer instance would display our copy
    /// while the game kept writing to the original, and the in-game reticle
    /// brightness control would silently stop working. So this writes to the
    /// shared material too, exactly as the game does, and captures every value it
    /// touches first. These materials live in memory, loaded from bundles, so
    /// nothing here can reach disk - the worst case is a restart.
    /// </summary>
    public static class CollimatorGlass
    {
        public static string Status = "not resolved yet";
        public static int Count;

        private const string P_Color = "_Color";
        private const string P_Hdr = "_HDR";
        private const string P_Scale = "_MarkScale";
        private const string P_Noise = "_NoiseTex";

        private sealed class Stock
        {
            public Material Mat;
            public Color Color;
            public float Hdr;
            public float Scale;
            public Texture Noise;
            public bool HasColor, HasHdr, HasScale, HasNoise;
        }

        private static readonly Dictionary<int, Stock> _stock = new Dictionary<int, Stock>();
        private static Type _tCollimator;
        private static bool _driving;

        public struct Glass
        {
            public float ColourStrength;
            public float R, G, B;
            public float Brightness;
            public float Size;
            public float Grain;
        }

        /// <summary>
        /// Every collimator material currently in the scene, captured once each.
        ///
        /// Keyed on the material's instance id rather than the renderer's: two
        /// sights of the same type share one material, and capturing it twice
        /// would record our own written value as the "original" the second time
        /// and make the restore a no-op. That is the shape of a leak that only
        /// shows up on the second weapon.
        /// </summary>
        private static void Capture()
        {
            if (_tCollimator == null)
            {
                Assembly asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                _tCollimator = asm == null ? null : asm.GetType("CollimatorSight", false);
                if (_tCollimator == null) { Status = "CollimatorSight type not found"; return; }
            }

            UnityEngine.Object[] sights;
            try { sights = UnityEngine.Object.FindObjectsOfType(_tCollimator); }
            catch (Exception e) { Status = e.GetType().Name + " finding collimators"; return; }

            Count = sights.Length;

            foreach (UnityEngine.Object s in sights)
            {
                if (s == null) continue;
                Material mat = GetMember(s, "CollimatorMaterial") as Material;
                if (mat == null) continue;

                int id = mat.GetInstanceID();
                if (_stock.ContainsKey(id)) continue;

                var st = new Stock { Mat = mat };
                st.HasColor = mat.HasProperty(P_Color);
                st.HasHdr = mat.HasProperty(P_Hdr);
                st.HasScale = mat.HasProperty(P_Scale);
                st.HasNoise = mat.HasProperty(P_Noise);

                if (st.HasColor) st.Color = mat.GetColor(P_Color);
                if (st.HasHdr) st.Hdr = mat.GetFloat(P_Hdr);
                if (st.HasScale) st.Scale = mat.GetFloat(P_Scale);
                if (st.HasNoise) st.Noise = mat.GetTexture(P_Noise);

                _stock[id] = st;

                Plugin.Log.LogInfo(string.Format(
                    "Sight glass: captured {0} ({1})  colour {2}  HDR {3:F2}  scale {4:F2}  noise {5}",
                    mat.name, mat.shader == null ? "?" : mat.shader.name,
                    st.HasColor ? st.Color.ToString() : "absent",
                    st.Hdr, st.Scale,
                    st.HasNoise ? (st.Noise == null ? "empty" : st.Noise.name) : "absent"));
            }
        }

        public static void Drive(Glass g)
        {
            try
            {
                Capture();
                if (_stock.Count == 0) { Status = "no collimator fitted"; return; }

                _driving = true;
                int touched = 0;

                foreach (Stock st in _stock.Values)
                {
                    if (st.Mat == null) continue;   // Unity null: material destroyed
                    touched++;

                    if (st.HasColor && g.ColourStrength > 0.001f)
                    {
                        // Lerp from the game's own colour, so the dial is a
                        // strength rather than a switch. Zero IS off, which is the
                        // pattern every other dial in this mod uses, and it avoids
                        // a master bool sitting above three colour sliders doing
                        // nothing until someone finds it. F44.
                        //
                        // Alpha is left alone. On this shader it is 1 and it is not
                        // an opacity the owner wants to discover by accident.
                        Color want = new Color(g.R, g.G, g.B, st.Color.a);
                        st.Mat.SetColor(P_Color,
                            Color.LerpUnclamped(st.Color, want, Mathf.Clamp01(g.ColourStrength)));
                    }
                    else if (st.HasColor)
                    {
                        st.Mat.SetColor(P_Color, st.Color);
                    }

                    // _HDR is the dot's headroom above white, and the main camera's
                    // bloom is what turns that into glare on the glass. This is the
                    // flare, and the game ships it at 3.
                    if (st.HasHdr)
                        st.Mat.SetFloat(P_Hdr, g.Brightness > 0.001f ? g.Brightness : st.Hdr);

                    if (st.HasScale)
                        st.Mat.SetFloat(P_Scale, g.Size > 0.001f ? g.Size : st.Scale);

                    // The experiment. _NoiseTex is the one empty slot on the
                    // shader; whether it renders anything is not knowable from the
                    // property list, so this is off by default and the HUD reports
                    // whether a texture was actually accepted, not whether we tried.
                    if (st.HasNoise)
                    {
                        Texture want = g.Grain > 0.001f ? GameRefs.PrismDirtTexture() : st.Noise;
                        st.Mat.SetTexture(P_Noise, want);
                    }
                }

                Status = touched == 0
                    ? "collimator materials gone - recapturing"
                    : string.Format("driving {0} glass material(s)", touched);
            }
            catch (Exception e)
            {
                Status = GameRefs.Explain(e) + " - stopping";
                Plugin.Log.LogWarning("Sight glass: " + Status);
                Release();
            }
        }

        /// <summary>Every captured value back where it was. Never throws.</summary>
        public static void Release()
        {
            if (!_driving && _stock.Count == 0) return;
            foreach (Stock st in _stock.Values)
            {
                if (st.Mat == null) continue;
                try
                {
                    if (st.HasColor) st.Mat.SetColor(P_Color, st.Color);
                    if (st.HasHdr) st.Mat.SetFloat(P_Hdr, st.Hdr);
                    if (st.HasScale) st.Mat.SetFloat(P_Scale, st.Scale);
                    if (st.HasNoise) st.Mat.SetTexture(P_Noise, st.Noise);
                }
                catch { }
            }
            _stock.Clear();
            _driving = false;
            Status = "released";
        }

        private static object GetMember(object target, string name)
        {
            if (target == null) return null;
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) { try { return f.GetValue(target); } catch { } }
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) { try { return p.GetValue(target, null); } catch { } }
            return null;
        }
    }
}
