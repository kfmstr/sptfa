using System;
using System.Reflection;
using EFT.Animations;
using HarmonyLib;
using SPTFreeAim.Compat;
using UnityEngine;

namespace SPTFreeAim.Patches
{
    /// <summary>
    /// docs/02-PLAN.md step 07 - recoil decoupling.
    ///
    /// The spec (section 3): from the hip the gun jumps on its own recoil pattern
    /// while the camera is NOT pushed up to match; shouldered, the camera follows
    /// the pattern at 0.5-0.7 amplitude and the spring returns it.
    ///
    /// Tarkov implements "camera follows the weapon" as one step per frame that
    /// writes HandsContainer.CameraTransform.localRotation. Realism replaces that
    /// method wholesale (reference/realism-ProceduralAnimPatches.cs,
    /// CamRecoilPatch, "stop player camera following weapon muzzle").
    ///
    /// This does something smaller and, deliberately, much less brittle.
    ///
    /// Realism's approach means reimplementing the method's branch logic, which
    /// binds you to six private field names and to whatever BSG does next. Here
    /// the original still runs; we record where the camera was before it, and
    /// after it we slerp back toward that. Follow 1.0 leaves the game untouched,
    /// 0.0 removes the camera's recoil follow completely, anything between scales
    /// it. Same outcome, no private fields, and it survives a rewrite of the
    /// method's internals.
    ///
    /// It stays off by default. Tune the coupling first (step 04) - two systems
    /// moving the camera at once is not something you want to debug at the same
    /// time as the thing you are actually building.
    /// </summary>
    public static class RecoilPatch
    {
        private static bool _applied;
        private static Quaternion _before;
        private static bool _captured;
        private static bool _warnedNoMethod;

        public static bool Applied { get { return _applied; } }

        public static void Apply(Harmony harmony)
        {
            if (GameRefs.M_Pwa_CameraRecoil == null)
            {
                Plugin.Log.LogWarning(
                    "Recoil decoupling unavailable: none of " +
                    string.Join(", ", GameRefs.CameraRecoilMethodNames) + " found on " +
                    "ProceduralWeaponAnimation. Find the method that rotates the camera toward the " +
                    "weapon after a shot (it reads CameraToWeaponAngleStep) and add its name to " +
                    "GameRefs.CameraRecoilMethodNames. Everything else still works.");
                return;
            }

            const BindingFlags PRIV = BindingFlags.Static | BindingFlags.NonPublic;
            harmony.Patch(
                GameRefs.M_Pwa_CameraRecoil,
                prefix: new HarmonyMethod(typeof(RecoilPatch).GetMethod(nameof(Before), PRIV)),
                postfix: new HarmonyMethod(typeof(RecoilPatch).GetMethod(nameof(After), PRIV)));

            _applied = true;
            Plugin.Log.LogInfo("Recoil decoupling patch applied to " + GameRefs.ResolvedCameraRecoilName +
                               " (inactive until enabled in F12).");
        }

        private static bool Active
        {
            get
            {
                return Plugin.Instance != null
                    && Plugin.Instance.Cfg.Enabled.Value
                    && Plugin.Instance.Cfg.DecoupleRecoil.Value
                    && GameRefs.Ready;
            }
        }

        /// <summary>Follow factor for this frame: 0 = camera ignores the weapon's recoil.</summary>
        private static float FollowFactor(ProceduralWeaponAnimation pwa)
        {
            FreeAimConfig cfg = Plugin.Instance.Cfg;
            float hip = cfg.HipCameraFollow.Value;
            float ads = cfg.AimCameraFollow.Value;

            // Blend on the same smoothed aim value the coupling uses, so the
            // transition into and out of sights is not a step change.
            return Mathf.Lerp(hip, ads, Plugin.Instance.State.AimBlend);
        }

        private static void Before(ProceduralWeaponAnimation __instance)
        {
            _captured = false;
            if (!Active) return;
            if (!ReferenceEquals(__instance, FreeAimPatches.LocalPwa)) return;

            Transform cam = GameRefs.GetCameraTransform(__instance);
            if (cam == null)
            {
                if (!_warnedNoMethod)
                {
                    _warnedNoMethod = true;
                    Plugin.Log.LogWarning(
                        "Recoil decoupling has no camera transform to work with; doing nothing.");
                }
                return;
            }

            _before = cam.localRotation;
            _captured = true;
        }

        private static void After(ProceduralWeaponAnimation __instance)
        {
            if (!_captured) return;
            _captured = false;
            if (!Active) return;

            try
            {
                Transform cam = GameRefs.GetCameraTransform(__instance);
                if (cam == null) return;

                float follow = Mathf.Clamp01(FollowFactor(__instance));
                if (follow >= 0.999f) return;   // let the game have it

                // follow 0   -> camera stays exactly where it was, the weapon
                //               still takes its full recoil pattern
                // follow 0.6 -> camera takes 60% of the movement the game wanted
                cam.localRotation = Quaternion.Slerp(_before, cam.localRotation, follow);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Recoil decoupling failed, switching it off: " + e);
                Plugin.Instance.Cfg.DecoupleRecoil.Value = false;
            }
        }
    }
}
