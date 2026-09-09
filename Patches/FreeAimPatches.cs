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

            st.UpdateAimBlend(GameRefs.GetIsAiming(pwa), dt);
            st.UpdateGate(p.Stance.WeaponReady || !cfg.StanceGateEnabled.Value, dt, cfg.GateSpeed.Value);

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
            if (doWeapon) { ApplyWeaponOffset(pwa, applied, cfg); GuardWeapon.EndFrame(weaponRootAnim); }
            if (doPose) { ApplyLoweredPose(pwa, p, dt); ApplyReadyPose(pwa, p, dt); GuardPose.EndFrame(weaponRoot); }

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
        private static void ApplyWeaponOffset(ProceduralWeaponAnimation pwa, Vector2 offset, FreeAimConfig cfg)
        {
            Transform root = GameRefs.GetWeaponRootAnim(pwa);
            if (root == null) return;

            float yaw = cfg.InvertYaw.Value ? -offset.x : offset.x;
            float pitch = cfg.InvertPitch.Value ? -offset.y : offset.y;
            if (cfg.SwapAxes.Value) { float t = yaw; yaw = pitch; pitch = t; }

            // The pivot is a full 3D point in the weapon root's local space, not a
            // distance along one axis.
            //
            // docs/02-PLAN.md said to rotate "about roughly the shoulder". That is
            // wrong: measured in Bodycam, the gun hinges about the FIRING HAND -
            // the grip and trigger - and the buttstock swings away from the body.
            // See docs/07-FINDINGS.md F12. A single up-axis distance cannot place
            // a pivot at the grip, which is why this takes a Vector3.
            Vector3 pivot = cfg.PivotOffset.Value;

            GameRefs.LocalRotateAround(root, pivot, new Vector3(pitch, 0f, yaw));

            // Without this second call the pivot is left displaced and every
            // offset applied after ours is wrong. lualeet's comment, and it is
            // correct - do not remove it as dead code.
            GameRefs.LocalRotateAround(root, -pivot, Vector3.zero);
        }

        private static Vector3 _loweredPos;
        private static Vector3 _loweredRot;
        private static Vector3 _readyPos;
        private static Vector3 _readyRot;

        /// <summary>
        /// Lowered-weapon pose. A position and rotation offset from the stock
        /// weapon-up pose, lerped rather than snapped. The values are ours and
        /// start at zero - see docs/07-FINDINGS.md F16.
        /// </summary>
        private static void ApplyLoweredPose(ProceduralWeaponAnimation pwa, Plugin p, float dt)
        {
            Transform root = GameRefs.GetWeaponRoot(pwa);
            if (root == null) return;

            bool down = !p.Stance.WeaponReady;
            float speed = p.Cfg.LoweredLerpSpeed.Value * dt;

            _loweredPos = Vector3.Lerp(_loweredPos, down ? p.Cfg.LoweredPos.Value : Vector3.zero, speed);
            _loweredRot = Vector3.Lerp(_loweredRot, down ? p.Cfg.LoweredRot.Value : Vector3.zero, speed);

            root.localPosition += _loweredPos;

            Quaternion add = Quaternion.identity;
            add.x = _loweredRot.x;
            add.y = _loweredRot.y;
            add.z = _loweredRot.z;
            root.localRotation *= add;
        }

        /// <summary>
        /// The ready stance, as measured in Bodycam: the weapon is NOT shouldered
        /// at rest. The firing hand is lowered, the gun is held low, and the
        /// buttstock sits behind the arm rather than in the shoulder pocket.
        /// Shouldering happens when you aim, and takes a moment.
        ///
        /// Tarkov's default "weapon up" is already shouldered, so reproducing the
        /// Bodycam ready position means offsetting away from it. Off by default
        /// (zero offsets) because the right values have to be found by eye and a
        /// guess here would just be noise - see docs/07-FINDINGS.md F12.
        ///
        /// Fades out as you aim, so aiming down sights is unaffected.
        /// </summary>
        private static void ApplyReadyPose(ProceduralWeaponAnimation pwa, Plugin p, float dt)
        {
            if (!p.Cfg.ReadyPoseEnabled.Value) return;

            Transform root = GameRefs.GetWeaponRoot(pwa);
            if (root == null) return;

            // Only while the weapon is up, and blended out by aiming.
            float weight = p.State.Gate * (1f - p.State.AimBlend);
            float speed = p.Cfg.LoweredLerpSpeed.Value * dt;

            _readyPos = Vector3.Lerp(_readyPos, p.Cfg.ReadyPos.Value * weight, speed);
            _readyRot = Vector3.Lerp(_readyRot, p.Cfg.ReadyRot.Value * weight, speed);

            root.localPosition += _readyPos;

            Quaternion add = Quaternion.identity;
            add.x = _readyRot.x;
            add.y = _readyRot.y;
            add.z = _readyRot.z;
            root.localRotation *= add;
        }
    }
}
