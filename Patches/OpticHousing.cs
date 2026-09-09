using System;
using System.Collections.Generic;
using SPTFreeAim.Compat;
using UnityEngine;

namespace SPTFreeAim.Patches
{
    /// <summary>
    /// What your off eye does to the body of the optic, while aiming.
    ///
    /// The request (docs/07-FINDINGS.md F26, F27): an Eotech or an Aimpoint Micro
    /// is a thick body around a small window, and in Tarkov that body is solid,
    /// so aiming through one blanks out everything around it. With both eyes open
    /// it does not, and there are two separable reasons why - so they are two
    /// separable switches:
    ///
    ///   TRANSPARENT  the off eye sees past the housing, so the housing reads as
    ///                a ghost rather than a wall. Alpha on the housing materials.
    ///
    ///   DOUBLED      the two eyes see the near object from slightly different
    ///                angles and the brain does not fuse it, because it is focused
    ///                past it on the target. The housing appears twice, offset by
    ///                the eye separation, each copy faint. That doubling IS what
    ///                near-object blur looks like to a shooter - it is not a
    ///                depth-of-field approximation, it is the actual phenomenon.
    ///
    ///   HIDE         neither: just take the housing out. Blunt, works on every
    ///                shader, and the reliable answer when the other two cannot
    ///                touch a particular weapon's materials.
    ///
    /// Use either, both, or none. The LENS and the RETICLE are never touched by
    /// any of them - hiding those would turn "see past the housing" into "the
    /// sight stopped working".
    ///
    /// A real camera depth-of-field would be the other way to do the blur, and
    /// EFT's Prism post stack is not reachable from Assembly-CSharp, so it is not
    /// on the table without a great deal more digging. It would also blur the
    /// whole near field rather than the optic, which may or may not be wanted.
    /// </summary>
    public static class OpticHousing
    {
        public struct Options
        {
            public bool Transparent;
            public float Alpha;        // 0 = invisible, 1 = solid
            public bool Doubled;
            public float Separation;   // metres between the two ghost copies
            public bool Hide;
        }

        private sealed class Touched
        {
            public Renderer R;
            public UnityEngine.Rendering.ShadowCastingMode Shadows;
            public Material[] Shared;
            public bool MaterialsChanged;
            public GameObject GhostA, GhostB;
        }

        private static readonly List<Touched> _touched = new List<Touched>();
        private static Transform _collectedFor;
        private static bool _applied;
        private static bool _warnedNoAlpha;

        public static string LastReport = "not collected yet";
        public static bool AlphaWorks { get; private set; }

        /// <summary>
        /// Put everything back and forget it.
        ///
        /// This is not optional tidying. A renderer left in shadows-only, or a
        /// ghost copy left parented to a weapon, does not end with the raid - the
        /// objects persist, and the player finds a broken optic next time with no
        /// idea why. Same hazard as the static AimDeltaFov in F25.
        /// </summary>
        public static void Restore()
        {
            for (int i = 0; i < _touched.Count; i++)
            {
                Touched t = _touched[i];
                try
                {
                    if (t.R != null)
                    {
                        t.R.shadowCastingMode = t.Shadows;
                        if (t.MaterialsChanged && t.Shared != null) t.R.sharedMaterials = t.Shared;
                        t.R.enabled = true;
                    }
                    if (t.GhostA != null) UnityEngine.Object.Destroy(t.GhostA);
                    if (t.GhostB != null) UnityEngine.Object.Destroy(t.GhostB);
                }
                catch { }
            }
            _touched.Clear();
            _collectedFor = null;
            _applied = false;
            LastReport = "restored";
        }

        public static void Frame(Transform bone, Renderer lens, float aimBlend, Options o)
        {
            bool anything = o.Transparent || o.Doubled || o.Hide;

            if (!anything || bone == null)
            {
                if (_touched.Count > 0) Restore();
                if (bone == null && anything) LastReport = "no optic fitted (irons, or none)";
                return;
            }

            if (!ReferenceEquals(bone, _collectedFor))
            {
                Restore();
                Collect(bone, lens, o);
                _collectedFor = bone;
            }

            bool want = aimBlend > 0.5f;
            if (want == _applied) return;
            _applied = want;

            for (int i = 0; i < _touched.Count; i++) Apply(_touched[i], want, o);
        }

        private static void Apply(Touched t, bool want, Options o)
        {
            if (t.R == null) return;

            if (!want)
            {
                t.R.shadowCastingMode = t.Shadows;
                if (t.MaterialsChanged && t.Shared != null)
                {
                    t.R.sharedMaterials = t.Shared;
                    t.MaterialsChanged = false;
                }
                if (t.GhostA != null) t.GhostA.SetActive(false);
                if (t.GhostB != null) t.GhostB.SetActive(false);
                return;
            }

            // Hide wins outright: there is nothing to fade or double if the mesh
            // is not drawing at all.
            if (o.Hide)
            {
                t.R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                return;
            }

            // Doubling divides the light between two copies, so each is fainter
            // than the requested alpha would be on its own. Without that the
            // doubled housing reads as heavier than the solid one, which is
            // backwards.
            float alpha = o.Transparent ? o.Alpha : 1f;
            if (o.Doubled) alpha *= 0.5f;

            bool faded = SetAlpha(t, alpha);

            if (o.Doubled && t.GhostA != null)
            {
                t.GhostA.SetActive(true);
                if (t.GhostB != null) t.GhostB.SetActive(true);

                // The original stops drawing; the two ghosts are the image now.
                t.R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                return;
            }

            if (!faded && o.Transparent)
            {
                // Asked for transparency, could not have it. Hiding is closer to
                // the intent than leaving it solid.
                t.R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }

        private static void Collect(Transform bone, Renderer lens, Options o)
        {
            Renderer[] all = bone.GetComponentsInChildren<Renderer>(true);
            int kept = 0;
            AlphaWorks = false;

            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;

                // The window, and whatever draws the aiming dot.
                if (ReferenceEquals(r, lens)) { kept++; continue; }
                if (GameRefs.IsReticleObject(r.gameObject)) { kept++; continue; }

                Touched t = new Touched
                {
                    R = r,
                    Shadows = r.shadowCastingMode,
                    Shared = r.sharedMaterials
                };

                if (o.Doubled) MakeGhosts(t, o.Separation);
                _touched.Add(t);
            }

            LastReport = string.Format("{0} housing renderers on {1}, {2} kept (lens/reticle){3}",
                _touched.Count, bone.name, kept,
                o.Doubled ? ", ghosts built" : "");
            Plugin.Log.LogInfo("Optic housing: " + LastReport);
        }

        /// <summary>
        /// Two copies of the mesh, offset either side of where it really is, by
        /// half the eye separation each. Mesh and materials are shared with the
        /// original - only the transform differs - so this costs two draw calls
        /// and no memory to speak of.
        ///
        /// Skinned meshes are skipped. Cloning one means cloning its bone
        /// hierarchy to animate correctly, and an optic housing is not skinned.
        /// </summary>
        private static void MakeGhosts(Touched t, float separation)
        {
            MeshFilter mf = t.R.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            if (t.R is SkinnedMeshRenderer) return;

            t.GhostA = MakeGhost(t, mf, separation * 0.5f);
            t.GhostB = MakeGhost(t, mf, -separation * 0.5f);
        }

        private static GameObject MakeGhost(Touched t, MeshFilter mf, float shift)
        {
            try
            {
                GameObject go = new GameObject("sptfa_ghost");
                go.transform.SetParent(t.R.transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                // Sideways in the VIEWER's frame, not the mesh's: the eyes are
                // side by side relative to the camera, whatever angle the weapon
                // happens to be held at.
                go.transform.position += Camera.main != null
                    ? Camera.main.transform.right * shift
                    : new Vector3(shift, 0f, 0f);

                MeshFilter gf = go.AddComponent<MeshFilter>();
                gf.sharedMesh = mf.sharedMesh;

                MeshRenderer gr = go.AddComponent<MeshRenderer>();
                gr.sharedMaterials = t.R.sharedMaterials;
                gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                gr.receiveShadows = false;

                go.SetActive(false);
                return go;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Optic housing: could not build a ghost copy: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Give the housing materials an alpha. Returns false when this weapon's
        /// shader has nothing to write, which is not a failure - EFT's weapon
        /// shaders are custom and some are simply opaque.
        /// </summary>
        private static bool SetAlpha(Touched t, float alpha)
        {
            try
            {
                Material[] mats = t.R.materials;   // instances, not the shared assets
                bool any = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null || !m.HasProperty("_Color")) continue;

                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0);
                    m.EnableKeyword("_ALPHABLEND_ON");
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

                    Color c = m.GetColor("_Color");
                    c.a = Mathf.Clamp01(alpha);
                    m.SetColor("_Color", c);
                    any = true;
                }

                if (any)
                {
                    t.MaterialsChanged = true;
                    AlphaWorks = true;

                    // The ghosts share the original's materials, so they pick the
                    // alpha up for free - but they are holding the SHARED array,
                    // and the instances live on the renderer. Point them at the
                    // instances so they fade with it.
                    if (t.GhostA != null) t.GhostA.GetComponent<MeshRenderer>().sharedMaterials = mats;
                    if (t.GhostB != null) t.GhostB.GetComponent<MeshRenderer>().sharedMaterials = mats;
                }
                else WarnNoAlpha();

                return any;
            }
            catch (Exception e)
            {
                WarnNoAlpha(e);
                return false;
            }
        }

        private static void WarnNoAlpha(Exception e = null)
        {
            if (_warnedNoAlpha) return;
            _warnedNoAlpha = true;
            Plugin.Log.LogWarning(
                "Optic housing: this sight's materials have no colour property to write, so " +
                "transparency and the ghost double cannot work on it. Falling back to hiding the " +
                "housing, which is blunter but reads similarly. EFT's weapon shaders are custom " +
                "and this is expected on some optics."
                + (e == null ? "" : "  (" + e.Message + ")"));
        }
    }
}
