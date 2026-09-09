using System;
using System.Reflection;
using EFT;
using EFT.Animations;
using HarmonyLib;
using SPTFreeAim.Compat;
using SPTFreeAim.Core;
using UnityEngine;

namespace SPTFreeAim.Patches
{
    /// <summary>
    /// Two patches, both manually applied so the target method names stay in
    /// GameRefs. Declaring TYPES are referenced directly here - EFT.Player and
    /// ProceduralWeaponAnimation are not obfuscated, only their members are.
    ///
    /// Patched with raw HarmonyX rather than SPT's ModulePatch. ModulePatch is a
    /// thin wrapper over exactly this, and going direct means the mod has no
    /// build- or run-time dependency on spt-reflection.dll, whose filename and
    /// folder have moved between SPT versions.
    /// </summary>
    public static class FreeAimPatches
    {
        public static Player LocalPlayer;
        public static ProceduralWeaponAnimation LocalPwa;

        // Every transform we touch is modified relative to its current value, so
        // each needs a guard or the frames stack. See Compat/TransformGuard.cs.
        private static readonly TransformGuard GuardWeapon = new TransformGuard("WeaponRootAnim");
        private static readonly TransformGuard GuardCamera = new TransformGuard("CameraTransform");
        private static readonly TransformGuard GuardPose = new TransformGuard("WeaponRoot");
        private static bool _guardsReported;

        private static Harmony _harmony;
        private static bool _warnedNoCamera;

        public static void Apply(Harmony harmony)
        {
            _harmony = harmony;

            harmony.Patch(
                GameRefs.M_Player_VisualPass,
                prefix: new HarmonyMethod(typeof(FreeAimPatches).GetMethod(
                    nameof(TrackLocalPlayer), BindingFlags.Static | BindingFlags.NonPublic)));

            harmony.Patch(
                GameRefs.M_Pwa_AvoidObstacles,
                postfix: new HarmonyMethod(typeof(FreeAimPatches).GetMethod(
                    nameof(AfterAvoidObstacles), BindingFlags.Static | BindingFlags.NonPublic)));

            Plugin.Log.LogInfo("FreeAimPatches applied.");
        }

        private static void ReleaseAll(ProceduralWeaponAnimation pwa)
        {
            GuardWeapon.Release(GameRefs.GetWeaponRootAnim(pwa));
            GuardCamera.Release(GameRefs.GetCameraTransform(pwa));
            GuardPose.Release(GameRefs.GetWeaponRoot(pwa));
        }

        /// <summary>Called when the local player changes, so no stale base survives a raid.</summary>
        public static void ForgetGuards()
        {
            WeaponGeometry.Forget();
            GuardWeapon.Forget();
            GuardCamera.Forget();
            GuardPose.Forget();
            _guardsReported = false;
        }

        public static void Remove()
        {
            LocalPlayer = null;
            LocalPwa = null;
            if (_harmony != null) _harmony.UnpatchSelf();
        }

        // ------------------------------------------------------------------

        private static void TrackLocalPlayer(Player __instance)
        {
            if (__instance == null || !__instance.IsYourPlayer) return;
            if (!ReferenceEquals(LocalPlayer, __instance))
            {
                LocalPlayer = __instance;
                Plugin.Instance.OnLocalPlayerChanged();
            }
            LocalPwa = __instance.ProceduralWeaponAnimation;
        }

        private static void AfterAvoidObstacles(ProceduralWeaponAnimation __instance)
        {
            if (LocalPlayer == null) return;
            if (!ReferenceEquals(__instance, LocalPwa)) return;

            // Switched off mid-raid: hand the transforms back rather than leaving
            // the weapon wherever our last frame put it.
            if (!Plugin.Active) { ReleaseAll(__instance); return; }

            try { Frame(__instance); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Free aim frame failed, disabling to avoid log spam: " + e);
                Plugin.Instance.EmergencyDisable();
            }
        }

        // ------------------------------------------------------------------

        private static void Frame(ProceduralWeaponAnimation pwa)
        {
            Plugin p = Plugin.Instance;
            FreeAimConfig cfg = p.Cfg;
            FreeAimState st = p.State;

            float dt = Time.deltaTime;

            object mc = GameRefs.GetMovementContext(LocalPlayer);
            if (mc == null) return;

            Vector2 raw = new Vector2(GameRefs.GetYaw(mc), GameRefs.GetPitch(mc));

            // Stance first: it decides the pose, the coupling target and the aim
            // blend, and the drive loop needs all three.
            p.Stance.Update(GameRefs.GetIsAiming(pwa), dt, cfg.StanceSnapshot(), cfg.StanceGateEnabled.Value);
            st.SetAimBlend(p.Stance.AimBlend);
            st.UpdateGate(p.Stance.Coupling, dt, cfg.GateSpeed.Value);

            // Stamina and ADS speed are optional extras. A failure in either must
            // not take the coupling down with it - that is exactly what happened
            // in F19, where one reflection lookup killed the whole mod mid-raid.
            // Each disables only itself.
            try { ApplyStanceConsequences(pwa, p, cfg); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Stance consequences failed; switching those two off " +
                                    "and carrying on with the coupling: " + e);
                cfg.StanceStaminaEnabled.Value = false;
                cfg.AdsSpeedFromWeight.Value = false;
            }

            // The weapon's own recoil, routed to the gun bearing rather than the
            // camera (docs/07-FINDINGS.md F12.3). Read before Step so the HUD and
            // the apply step see the same frame's value.
            st.RecoilRaw = GameRefs.GetHandRecoil(pwa);
            if (cfg.RecoilMovesGun.Value)
            {
                float rYaw = st.RecoilRaw.y;
                float rPitch = st.RecoilRaw.x;
                if (cfg.RecoilSwapAxes.Value) { float t = rYaw; rYaw = rPitch; rPitch = t; }
                Vector2 scale = cfg.RecoilGunScale.Value;
                st.RecoilOffset = new Vector2(rYaw * scale.x, rPitch * scale.y);
            }
            else st.RecoilOffset = Vector2.zero;

            st.UpdateRoll(dt, cfg.CantSpeed.Value);

            FreeAimState.Tuning tuning = cfg.Snapshot();
            Vector2? writeBack = st.Step(raw, dt, tuning);

            if (writeBack.HasValue)
            {
                // Note this writes Rotation, not Yaw/Pitch: those are read-only
                // computed properties over it. See docs/07-FINDINGS.md F10.
                if (!GameRefs.SetRotation(mc, writeBack.Value))
                {
                    Plugin.Log.LogWarning(
                        "Intercept mode cannot drive MovementContext.Rotation. Falling back to " +
                        "Compensate, which does not need it. See docs/05-OPEN-QUESTIONS.md Q4.");
                    cfg.Mode.Value = DriveMode.Compensate;
                    st.Reset(raw);
                    return;
                }
            }

            Vector2 applied = st.AppliedOffset(tuning);

            Transform weaponRootAnim = GameRefs.GetWeaponRootAnim(pwa);
            Transform cameraTransform = GameRefs.GetCameraTransform(pwa);
            Transform weaponRoot = GameRefs.GetWeaponRoot(pwa);

            bool doCamera = tuning.Mode == DriveMode.Compensate && cfg.ApplyCameraOffset.Value;
            bool doWeapon = cfg.ApplyWeaponOffset.Value;
            bool doPose = cfg.LoweredPoseEnabled.Value && cfg.StanceGateEnabled.Value;

            // Undo last frame's work first, so what we apply is never applied twice.
            if (doCamera) GuardCamera.BeginFrame(cameraTransform); else GuardCamera.Release(cameraTransform);
            if (doWeapon) GuardWeapon.BeginFrame(weaponRootAnim); else GuardWeapon.Release(weaponRootAnim);
            if (doPose) GuardPose.BeginFrame(weaponRoot); else GuardPose.Release(weaponRoot);

            if (doCamera) { ApplyCameraOffset(pwa, -applied); GuardCamera.EndFrame(cameraTransform); }
            if (doWeapon) { ApplyWeaponOffset(pwa, applied, cfg, p); GuardWeapon.EndFrame(weaponRootAnim); }
            if (doPose) { ApplyStancePose(pwa, p); GuardPose.EndFrame(weaponRoot); }

            ReportGuardsOnce();
        }

        /// <summary>
        /// Whether the game re-establishes these transforms each frame is the fact
        /// that decides whether the guards are load-bearing or merely harmless.
        /// It is cheap to observe and worth knowing, so log it once.
        /// </summary>
        private static void ReportGuardsOnce()
        {
            if (_guardsReported) return;
            if (!GuardWeapon.Observed && !GuardCamera.Observed && !GuardPose.Observed) return;
            _guardsReported = true;

            Plugin.Log.LogInfo("Transform reset behaviour: "
                + GuardWeapon.Describe() + " | "
                + GuardCamera.Describe() + " | "
                + GuardPose.Describe());
        }

        /// <summary>
        /// Compensate mode only. The game has already pointed the camera at the
        /// gun bearing; rotate it back by the offset so the view lags. The weapon
        /// hangs under the camera, so it comes with it - which is why the weapon
        /// offset below is applied in the opposite sense and lands back on the
        /// mouse bearing.
        ///
        /// IF THE CAMERA JITTERS OR SNAPS BACK: something later in the frame is
        /// overwriting CameraTransform.localRotation - almost certainly the
        /// camera-recoil step (GameRefs.M_Pwa_CameraRecoil, "method_19"). Move
        /// this call into a postfix on that method instead. See
        /// Patches/CameraApplyAlternative.cs.
        /// </summary>
        private static void ApplyCameraOffset(ProceduralWeaponAnimation pwa, Vector2 offset)
        {
            Transform cam = GameRefs.GetCameraTransform(pwa);
            if (cam == null)
            {
                if (!_warnedNoCamera)
                {
                    _warnedNoCamera = true;
                    Plugin.Log.LogError(
                        "HandsContainer.CameraTransform not found - Compensate mode cannot move the " +
                        "camera and will behave like Reactive. Find the camera transform in the " +
                        "dnSpy export and add it to GameRefs.");
                }
                return;
            }

            // Yaw about local up, pitch about local right.
            cam.localRotation = cam.localRotation * Quaternion.Euler(-offset.y, offset.x, 0f);
        }

        /// <summary>
        /// The apply step, derived from lualeet/sptarkov-deadzone (Unlicense) via
        /// lualeet's deadzone mod (Unlicense). Rotate the weapon root about
        /// a pivot set back from the muzzle so the gun swings about roughly the
        /// shoulder rather than spinning about its middle.
        ///
        /// The axis mapping below (pitch to X, yaw to Z) is lualeet's. It is not
        /// obvious and it is not documented anywhere. If the weapon moves the wrong
        /// way, use the Invert/Swap toggles in the F12 menu rather than editing
        /// this - they exist precisely because this mapping has to be found by
        /// experiment on each game version.
        /// </summary>
        private static void ApplyWeaponOffset(ProceduralWeaponAnimation pwa, Vector2 offset,
                                              FreeAimConfig cfg, Plugin p)
        {
            Transform root = GameRefs.GetWeaponRootAnim(pwa);
            if (root == null) return;

            float yaw = cfg.InvertYaw.Value ? -offset.x : offset.x;
            float pitch = cfg.InvertPitch.Value ? -offset.y : offset.y;
            if (cfg.SwapAxes.Value) { float t = yaw; yaw = pitch; pitch = t; }

            // The pivot is a point in the weapon root's local space, and it MOVES.
            //
            // docs/02-PLAN.md said to rotate about roughly the shoulder. F12
            // corrected that to the firing hand. Both were half right, and F20 is
            // why: the pivot is wherever the weapon is braced, and what braces it
            // changes with the stance. Held at the ready that is the right hand on
            // the grip; once the buttstock is in the shoulder pocket it is the
            // buttpad.
            //
            // The blend is the aim blend, not a setting of its own. "If aiming,
            // the buttstock is always on the shoulder" is a rule rather than a
            // preference, so there is nothing here to tune.
            WeaponGeometry.EnsureMeasured(root);

            WeaponAnchors anchors = cfg.AnchorSnapshot();
            Vector3 pivot = anchors.Pivot(p.State.AimBlend);

            // Turn the weapon about axes ACROSS the barrel.
            //
            // This used to be Vector3(pitch, 0, yaw) - the weapon root's raw
            // local X and Z, lualeet's mapping. That only aims the gun if those
            // axes happen to lie across the bore, and on this build they do not:
            // part of every mouse movement was rolling the weapon rather than
            // pointing it, so the barrel kept its angle to the body and the whole
            // gun slid around instead of hinging at the grip. F21.
            //
            // Built from the MEASURED bore, so it is correct on any weapon and
            // there is no per-gun axis to find.
            Vector3 right, up;
            anchors.Frame(out right, out up);

            Quaternion q = Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(-pitch, right);

            // Handed to LocalRotateAround as euler because that is the signature
            // the game's own TransformTools exposes. Quaternion.Euler(q.eulerAngles)
            // reproduces q, so nothing is lost in the round trip.
            GameRefs.LocalRotateAround(root, pivot, q.eulerAngles);

            // Without this second call the pivot is left displaced and every
            // offset applied after ours is wrong. lualeet's comment, and it is
            // correct - do not remove it as dead code.
            GameRefs.LocalRotateAround(root, -pivot, Vector3.zero);

            ApplyCant(root, anchors, p);
        }

        /// <summary>
        /// Roll about the bore: the weapon's third rotational freedom (F20).
        ///
        /// Rolled about the SUPPORT HAND, not the grip, because that is where the
        /// bore line is held. Canting about the grip would swing the muzzle
        /// sideways as well as rolling it, which is not what tipping a rifle over
        /// feels like.
        ///
        /// Scaled by the gate so a lowered weapon is not left canted in the hand.
        /// The commanded angle survives, so raising it again restores the cant.
        /// </summary>
        private static void ApplyCant(Transform root, WeaponAnchors anchors, Plugin p)
        {
            if (!p.Cfg.CantEnabled.Value) return;

            float roll = p.State.Roll * p.State.Gate;
            if (Mathf.Abs(roll) < 0.01f) return;

            // About the measured bore, which is what "roll" means. Passing the
            // bore vector scaled by the angle would only be a rotation if the
            // bore were an axis-aligned unit vector, which it is not once it is
            // measured off a real weapon.
            Vector3 rollPivot = anchors.LeftHand;
            Quaternion q = Quaternion.AngleAxis(roll, anchors.Bore);
            GameRefs.LocalRotateAround(root, rollPivot, q.eulerAngles);
            GameRefs.LocalRotateAround(root, -rollPivot, Vector3.zero);
        }

        /// <summary>
        /// Apply the stance's pose. The lerp lives in StanceState, so this is
        /// just the write - which keeps the "where should the weapon be" decision
        /// in one place instead of spread across two pose methods that each
        /// lerped their own copy.
        /// </summary>
        private static void ApplyStancePose(ProceduralWeaponAnimation pwa, Plugin p)
        {
            Transform root = GameRefs.GetWeaponRoot(pwa);
            if (root == null) return;

            root.localPosition += p.Stance.PosePos;

            Quaternion add = Quaternion.identity;
            add.x = p.Stance.PoseRot.x;
            add.y = p.Stance.PoseRot.y;
            add.z = p.Stance.PoseRot.z;
            root.localRotation *= add;
        }

        /// <summary>
        /// What the stance costs: arm stamina, and how fast the sights come up.
        ///
        /// Both work WITH the game's own systems rather than replacing them -
        /// scaling Tarkov's hands-stamina restore rate and its AimingSpeed, from
        /// captured stock values so nothing compounds frame to frame.
        /// </summary>
        private static void ApplyStanceConsequences(ProceduralWeaponAnimation pwa, Plugin p, FreeAimConfig cfg)
        {
            if (cfg.StanceStaminaEnabled.Value)
                GameRefs.SetHandsRecovery(LocalPlayer, p.Stance.HandsRecovery);
            else
                GameRefs.ReleaseHandsRecovery(LocalPlayer);

            if (cfg.AdsSpeedFromWeight.Value)
            {
                float w = GameRefs.GetHeldWeaponWeight(LocalPlayer);
                float reference = Mathf.Max(0.1f, cfg.AdsWeightReference.Value);
                if (w > 0.01f)
                {
                    // Speed scales inversely with weight, softened by strength:
                    // strength 0 leaves it stock, 1 is the full inverse ratio.
                    float ratio = reference / w;
                    float mul = Mathf.Lerp(1f, ratio, Mathf.Clamp01(cfg.AdsWeightStrength.Value));
                    GameRefs.SetAimingSpeed(pwa, Mathf.Clamp(mul, 0.35f, 2f));
                }
            }
            else GameRefs.ReleaseAimingSpeed(pwa);
        }
    }
}
