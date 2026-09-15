using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SPTFreeAim.Compat
{
    /// <summary>
    /// Keep the scope's picture alive when you are not aiming.
    ///
    /// Tarkov blacks out a scope at the hip with TWO switches, and both have to
    /// be held or the feature does nothing visible (F65, corrected by F70):
    ///
    ///     sight.enabled = false          -> OnDisable -> the camera stops
    ///     sight.LensFade(true)           -> the glass is painted out, 0.97
    ///
    /// The first version held only the component, on the reasoning that
    /// OnEnable re-runs LensFade(false) as a side effect. It does. But the pair
    /// is written together by ProceduralWeaponAnimation.method_1, which fires on
    /// aim, pose, FOV and point-of-view changes, so the glass can be repainted
    /// after the component has been re-enabled - a live camera rendering behind
    /// an opaque lens, which looks exactly like the feature doing nothing.
    ///
    /// So both are held. The component here, per frame; the fade at the game's
    /// own seam, by a prefix on OpticSight.LensFade - see Apply(). We still never
    /// call the internals: OnEnable does retrice, updater and camera activation
    /// in the game's own order.
    ///
    /// **A name describes intent; only the mechanism describes behaviour** has
    /// cost this project five findings now. The fifth was this one, and the
    /// specific shape of it is worth naming: I verified what the switch I held
    /// DOES, and never asked who else writes it.
    /// </summary>
    public static class OpticPiP
    {
        public static string Status = "off";
        public static int LiveResolution;
        public static int Reasserts;

        /// <summary>The lens material's fade value: 0 is clear, 0.97 is black.</summary>
        public static float Fade = -1f;

        /// <summary>The sight this is holding open, for the LensFade prefix.</summary>
        public static Behaviour DrivenSight;

        public static bool PatchApplied;
        public static string PatchWhyNot = "not applied yet";

        private static object _ocm;
        private static MethodInfo _setResolution;
        private static int _gameResolution = -1;
        private static int _appliedResolution = -1;
        private static Behaviour _sight;
        private static Transform _sightBone;
        private static bool _driving;

        private static FieldInfo _fLensRenderer;
        private static FieldInfo _fSwitchToSightId;

        /// <summary>
        /// Hold the glass clear at the game's own seam.
        ///
        /// F70. There are TWO switches, not one, and the second is the one you
        /// can see. The black disc is not an absence of picture - it is paint:
        ///
        ///     OpticSight.LensFade(bool fade):
        ///         LensRenderer.sharedMaterial.SetFloat(_switchToSightId,
        ///                                              fade ? 0.97f : 0f)
        ///
        /// and the not-aiming branch of ProceduralWeaponAnimation.method_1 does
        /// BOTH of them together, for every optic on the weapon:
        ///
        ///     sight.enabled = wanted;        // the camera stops rendering
        ///     sight.LensFade(!wanted);       // the glass is painted out
        ///
        /// where `wanted` begins `IsAiming && ...`, so lowering the weapon always
        /// gives false. The first version of this class held only the component,
        /// reasoning that OnEnable would re-run LensFade(false) as a side effect.
        /// It does - but method_1 is called from four places on aim, pose, FOV and
        /// point-of-view changes, and any one of them can repaint the glass after
        /// we have re-enabled the component, leaving a live camera rendering
        /// behind an opaque lens. Exactly the picture the owner sent.
        ///
        /// So the fade is held rather than corrected: a prefix on LensFade forces
        /// the argument to false for the sight we are driving. Every caller,
        /// present and future, is covered by one patch on a named method of a
        /// named type - no dependence on `method_1` keeping its obfuscated name.
        ///
        /// Only the driven sight is affected, and only while driving. Every other
        /// optic on the gun, and every other player's, fades exactly as it did.
        /// </summary>
        public static void Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type t = HarmonyLib.AccessTools.TypeByName("EFT.CameraControl.OpticSight");
                if (t == null) { PatchWhyNot = "EFT.CameraControl.OpticSight not found"; }
                else
                {
                    _fLensRenderer = t.GetField("LensRenderer",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _fSwitchToSightId = t.GetField("_switchToSightId",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                    MethodInfo m = HarmonyLib.AccessTools.Method(t, "LensFade", new[] { typeof(bool) });
                    if (m == null) PatchWhyNot = "OpticSight.LensFade(bool) not found";
                    else
                    {
                        harmony.Patch(m, prefix: new HarmonyLib.HarmonyMethod(
                            typeof(OpticPiP).GetMethod(nameof(BeforeLensFade),
                                BindingFlags.Static | BindingFlags.NonPublic)));
                        PatchApplied = true;
                        PatchWhyNot = null;
                        Plugin.Log.LogInfo("Scope picture: holding OpticSight.LensFade "
                                           + "(inactive until 'scope picture when not aiming' is above 0).");
                    }
                }
            }
            catch (Exception e) { PatchWhyNot = GameRefs.Explain(e); }

            if (!PatchApplied)
                Plugin.Log.LogWarning("Scope picture unavailable: " + PatchWhyNot
                    + ". The scope will still black out at the hip.");
        }

        /// <summary>
        /// Never throws, and never refuses a fade it is not sure about: anything
        /// unexpected leaves the game's own argument alone.
        /// </summary>
        private static void BeforeLensFade(object __instance, ref bool __0)
        {
            try
            {
                if (!_driving || !__0) return;
                if (!ReferenceEquals(__instance, DrivenSight)) return;
                __0 = false;
            }
            catch { }
        }

        /// <summary>What the lens material actually says, so the HUD is a reading.</summary>
        private static float ReadFade(Behaviour sight)
        {
            try
            {
                if (_fLensRenderer == null || _fSwitchToSightId == null) return -1f;
                var rend = _fLensRenderer.GetValue(sight) as Renderer;
                if (rend == null) return -1f;
                Material mat = rend.sharedMaterial;
                if (mat == null) return -1f;
                return mat.GetFloat((int)_fSwitchToSightId.GetValue(null));
            }
            catch { return -1f; }
        }

        private static bool Resolve()
        {
            if (_ocm != null) return true;
            try
            {
                Assembly asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                Type camMgr = asm == null ? null : asm.GetType("EFT.CameraControl.CameraManager", false);
                PropertyInfo pInst = camMgr == null ? null : camMgr.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object inst = pInst == null ? null : pInst.GetValue(null, null);
                if (inst == null) { Status = "not in a raid yet"; return false; }

                PropertyInfo pOcm = camMgr.GetProperty("OpticCameraManager",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object ocm = pOcm == null ? null : pOcm.GetValue(inst, null);
                if (ocm == null) { Status = "OpticCameraManager is null"; return false; }

                _setResolution = ocm.GetType().GetMethod("SetResolution",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _ocm = ocm;
                return true;
            }
            catch (Exception e) { Status = GameRefs.Explain(e); return false; }
        }

        private static object Member(object target, string name)
        {
            if (target == null) return null;
            Type t = target.GetType();
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) { try { return p.GetValue(target, null); } catch { } }
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) { try { return f.GetValue(target); } catch { } }
            return null;
        }

        /// <summary>
        /// The OpticSight component under the fitted sight, cached per bone.
        ///
        /// Found through the bone the mod already resolves for the housing work,
        /// so there is no scene scan. F61 paid for one of those and it is not
        /// happening again.
        /// </summary>
        private static Behaviour FindSight(object pwa)
        {
            Transform bone = GameRefs.GetCurrentSightBone(pwa);
            if (bone == null) return null;

            // Keyed on the bone, not just on "is the cached component still
            // alive". Swapping between two scoped weapons does not necessarily
            // destroy the first one's OpticSight - Tarkov keeps weapon objects
            // around - so a liveness check alone can leave this driving the scope
            // you are no longer holding. The bone IS the identity of the fitted
            // sight, and it is what the housing work already resolves.
            var live = _sight as UnityEngine.Object;
            if (live != null && ReferenceEquals(bone, _sightBone)) return _sight;
            _sightBone = bone;

            Assembly asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            Type tSight = asm == null ? null : asm.GetType("EFT.CameraControl.OpticSight", false);
            if (tSight == null) return null;

            // Up as well as down: the component sits on the scope root, and the
            // bone the animation exposes can be either side of it.
            _sight = bone.GetComponentInChildren(tSight, true) as Behaviour;
            if (_sight == null) _sight = bone.GetComponentInParent(tSight) as Behaviour;
            return _sight;
        }

        /// <summary>
        /// Called when the local player changes - which is the raid boundary.
        ///
        /// Everything cached here belongs to a raid, not to the session: the
        /// sight to a weapon, the OpticCameraManager and the resolution the
        /// player's graphics settings chose to the run. Caching a per-raid object
        /// for the session is the defect in F60, where the isolation fix applied
        /// once and then quietly did nothing on every raid after it. The same
        /// mistake would show up here as "it worked the first time".
        /// </summary>
        public static void Forget()
        {
            _sight = null;
            _sightBone = null;
            DrivenSight = null;
            Fade = -1f;
            _ocm = null;
            _setResolution = null;
            _gameResolution = -1;
            _appliedResolution = -1;
            _driving = false;
            Reasserts = 0;
            Status = "waiting for a raid";
        }

        /// <summary>
        /// <paramref name="idleResolution"/> of 0 is off, the way every dial in
        /// this mod treats zero. Above that it is the scope's pixel size while
        /// you are not aiming; shouldered always gets the game's own value back.
        /// </summary>
        public static void Drive(object pwa, bool aiming, int idleResolution)
        {
            if (idleResolution <= 0)
            {
                if (_driving) Release();
                return;
            }
            if (!Resolve()) return;

            try
            {
                // Learn the game's own resolution once, so "full" means whatever
                // the player's graphics settings chose rather than a number I
                // invented. F55.
                if (_gameResolution < 0)
                {
                    object r = Member(_ocm, "OpticFinalResolution");
                    if (r is int && (int)r > 0) _gameResolution = (int)r;
                    else return;
                    _appliedResolution = _gameResolution;
                }

                Behaviour sight = FindSight(pwa);
                if (sight == null) { Status = "no optic fitted"; return; }

                _driving = true;
                DrivenSight = sight;

                // SWITCH ONE: the component. OpticSight.OnEnable does retrice,
                // updater and camera activation in the game's own order, and the
                // camera is what fills the render texture.
                if (!sight.enabled)
                {
                    sight.enabled = true;
                    Reasserts++;
                }

                // SWITCH TWO: the glass. Held by the LensFade prefix rather than
                // written here - see Apply() for why there are two of these and
                // why holding the second one from a patch is not optional.
                Fade = ReadFade(sight);

                int want = aiming ? _gameResolution : Mathf.Min(idleResolution, _gameResolution);
                if (want != _appliedResolution && _setResolution != null)
                {
                    // SetResolution destroys and rebuilds the render texture, so
                    // it runs ONLY on a genuine transition. Called every frame it
                    // would rebuild the scope's target sixty times a second, which
                    // is the shape of the regression in F61.
                    _setResolution.Invoke(_ocm, new object[] { want });
                    _appliedResolution = want;
                }

                LiveResolution = _appliedResolution;
                Status = string.Format("{0} px{1}   glass {2}{3}{4}",
                    _appliedResolution,
                    aiming ? " aimed" : " idle",
                    Fade < 0f ? "?" : Fade.ToString("F2"),
                    Fade > 0.01f ? " PAINTED OUT" : " clear",
                    Reasserts > 0 ? "   re-enabled x" + Reasserts : "");
            }
            catch (Exception e)
            {
                Status = GameRefs.Explain(e) + " - stopping";
                Plugin.Log.LogWarning("Scope picture: " + Status);
                Release();
            }
        }

        /// <summary>Hand the scope back to the game. Never throws.</summary>
        public static void Release()
        {
            if (!_driving) return;
            _driving = false;
            try
            {
                // Resolution first, so the texture the game inherits is the one it
                // expects. Leaving a 256 px scope behind would look like a bug in
                // Tarkov rather than a leftover from here.
                if (_setResolution != null && _gameResolution > 0 && _appliedResolution != _gameResolution)
                {
                    _setResolution.Invoke(_ocm, new object[] { _gameResolution });
                    _appliedResolution = _gameResolution;
                }
            }
            catch { }

            // Repaint the glass on the way out, but only if the game had
            // already decided the scope should be dark - a disabled component is
            // that decision. Handing back a clear lens over a dead camera would
            // look like a bug in Tarkov rather than a leftover from here, and the
            // repaint cannot wait for method_1, which only fires on aim, pose,
            // FOV and point-of-view changes and may not fire at all.
            try
            {
                var b = DrivenSight;
                if (b != null && !b.enabled && _fSwitchToSightId != null && _fLensRenderer != null)
                {
                    var rend = _fLensRenderer.GetValue(b) as Renderer;
                    Material mat = rend == null ? null : rend.sharedMaterial;
                    if (mat != null) mat.SetFloat((int)_fSwitchToSightId.GetValue(null), 0.97f);
                }
            }
            catch { }

            // The sight component is NOT forced back off. The game disables it
            // itself the moment you lower the weapon, and forcing it would fight
            // the same state machine this feature exists to cooperate with.
            DrivenSight = null;
            _sight = null;
            _sightBone = null;
            Reasserts = 0;
            Fade = -1f;
            Status = "released";
        }
    }
}
