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
            GameRefs.ReleaseAimFov();
            FocusDepth.Release();
            OpticHousing.Release();
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

            // Pitch sign is normalised HERE, at the source, not at the apply step.
            //
            // The Invert toggles only ever rotated the weapon. The drive loop, the
            // body bearing and the camera offset all kept the game's own sign, so
            // flipping pitch fixed the gun's picture while leaving the body
            // disagreeing with it - one axis right, the other backwards, exactly
            // as reported. Tarkov counts pitch downward; the loop wants up. One
            // sign, applied on read and undone on write, and every consumer
            // agrees. F24.
            float pitchSign = cfg.InvertGamePitch.Value ? -1f : 1f;
            Vector2 raw = new Vector2(GameRefs.GetYaw(mc), GameRefs.GetPitch(mc) * pitchSign);

            st.UpdateAimBlend(GameRefs.GetIsAiming(pwa), dt);
            st.UpdateGate(p.Stance.WeaponReady || !cfg.StanceGateEnabled.Value, dt, cfg.GateSpeed.Value);

            // Both of these are extras. A failure in either must not take the
            // coupling down with it - that is F19, where one reflection lookup
            // killed the whole mod mid-raid. Each switches off only itself.
            try { ApplyArmDrain(p, cfg, dt); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Arm drain failed, switching it off: " + e);
                cfg.ArmDrainEnabled.Value = false;
            }

            try
            {
                FocusDepth.Frame(Camera.main, p.State.AimBlend, new FocusDepth.Options
                {
                    Enabled = cfg.DofEnabled.Value,
                    BlurSize = cfg.DofBlurSize.Value,
                    Band = cfg.DofBand.Value,
                    MaxDistance = cfg.DofMaxDistance.Value,
                    OnlyWhileAiming = cfg.DofOnlyAiming.Value,
                    Strength = cfg.DofStrength.Value
                });
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Depth of field failed, switching it off: " + e);
                cfg.DofEnabled.Value = false;
                FocusDepth.Release();
            }

            try { ApplySightTransparency(p, pwa, cfg); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Sight transparency failed, switching it off: " + e);
                cfg.SightAlpha.Value = 0f;
                OpticHousing.Release();
            }

            try { ApplyBothEyes(p, cfg); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Both eyes open failed, switching it off: " + e);
                cfg.KeepPeripheralVision.Value = false;
                GameRefs.ReleaseAimFov();
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

            FreeAimState.Tuning tuning = cfg.Snapshot();
            Vector2? writeBack = st.Step(raw, dt, tuning);

            if (writeBack.HasValue)
            {
                // Note this writes Rotation, not Yaw/Pitch: those are read-only
                // computed properties over it. See docs/07-FINDINGS.md F10.
                Vector2 back = new Vector2(writeBack.Value.x, writeBack.Value.y * pitchSign);
                if (!GameRefs.SetRotation(mc, back))
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

            ReportParentageOnce(weaponRootAnim, weaponRoot);
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
        /// The apply step, derived from lualeet/sptarkov-deadzone (Unlicense).
        /// Rotate the weapon root about
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

            if (cfg.Hinge.Value == HingeMode.AroundGrip)
            {
                HingeAboutGrip(pwa, root, yaw, pitch, cfg);
                return;
            }

            // The pivot is a full 3D point in the weapon root's local space, not a
            // distance along one axis.
            //
            // docs/02-PLAN.md said to rotate "about roughly the shoulder". That is
            // wrong: measured in Bodycam, the gun hinges about the FIRING HAND -
            // the grip and trigger - and the buttstock swings away from the body.
            // See docs/07-FINDINGS.md F12. A single up-axis distance cannot place
            // a pivot at the grip, which is why this takes a Vector3.
            // One dial along the weapon's local up, which is what the earliest
            // builds had and what -0.15 was measured against: it put the hinge on
            // the pistol grip. The Vector3 below is a fine offset on top, zero by
            // default, for nudging off that line.
            Vector3 pivot = Vector3.up * cfg.PivotDistance.Value + cfg.PivotFineOffset.Value;

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
        /// Turn the weapon about the firing hand, in world space.
        ///
        /// The physical description, and the one the owner has given three times:
        /// the right hand holds the pistol grip, the mouse moves the support hand,
        /// and the weapon is a rigid link between them - so it HINGES about the
        /// grip and the muzzle swings.
        ///
        /// Two things make this different from what came before.
        ///
        /// First, the pivot is a world point, placed relative to the camera. The
        /// grip is at a knowable place on your body - a little to the strong side,
        /// well below the eye, a little forward of it - and those are numbers
        /// anyone can picture and adjust. A point in the weapon root's local space
        /// is not, which is why tuning it by eye never converged.
        ///
        /// Second, yaw turns about the world vertical and pitch about the camera's
        /// own right vector. Those are the axes the words mean. Nothing is
        /// reinterpreted through the weapon's local frame on the way there, so
        /// pitch cannot go missing and no component of the mouse movement can land
        /// along the barrel and roll the gun. See docs/07-FINDINGS.md F22 for what
        /// the old call actually did.
        /// </summary>
        private static void HingeAboutGrip(ProceduralWeaponAnimation pwa, Transform root,
                                           float yaw, float pitch, FreeAimConfig cfg)
        {
            Transform cam = GameRefs.GetCameraTransform(pwa);
            if (cam == null)
            {
                if (!_warnedNoCamera)
                {
                    _warnedNoCamera = true;
                    Plugin.Log.LogError(
                        "HandsContainer.CameraTransform not found, so the grip pivot cannot be " +
                        "placed. Falling back to the legacy rotation. See docs/08-RECON.md.");
                }
                cfg.Hinge.Value = HingeMode.LegacyEuler;
                return;
            }

            // The grip, as a point in the world: offset from the camera in its own
            // right / up / forward axes, in metres.
            Vector3 pivot = cam.TransformPoint(cfg.GripFromEye.Value);

            // Yaw about the world vertical, pitch about the camera's right. Using
            // the camera's right rather than the world X keeps pitch square to
            // where you are looking when you are turned away from the axes.
            Quaternion q = Quaternion.AngleAxis(yaw, Vector3.up)
                         * Quaternion.AngleAxis(-pitch, cam.right);

            // A rigid body turned about a point: rotate the position around it,
            // and rotate the orientation by the same amount. Nothing else - no
            // second cancelling call, because nothing was displaced.
            root.position = pivot + q * (root.position - pivot);
            root.rotation = q * root.rotation;

            LastPivot = pivot;
        }

        private static float _drainCarry;

        /// <summary>
        /// Take arm stamina while the weapon is up.
        ///
        /// The drain is accumulated and applied in whole units. The pool's own
        /// setter ignores changes below 1.0, and although this writes the field
        /// directly, keeping the writes coarse means the game's threshold and
        /// exhaustion checks see a value that actually moved rather than a
        /// thousand invisible nudges a second.
        ///
        /// Gate, not the raw stance flag: the weapon being on its way down should
        /// already be costing less, and the gate is the thing that knows how far
        /// through that transition we are.
        /// </summary>
        private static void ApplyArmDrain(Plugin p, FreeAimConfig cfg, float dt)
        {
            if (!cfg.ArmDrainEnabled.Value || LocalPlayer == null) { _drainCarry = 0f; return; }

            float raised = p.State.Gate;
            if (raised <= 0.001f) { _drainCarry = 0f; return; }

            float aimed = Mathf.Lerp(1f, cfg.ArmDrainAimedMultiplier.Value, p.State.AimBlend);
            _drainCarry += cfg.ArmDrainRate.Value * raised * aimed * dt;

            if (_drainCarry < 1f) return;

            float whole = Mathf.Floor(_drainCarry);
            _drainCarry -= whole;
            GameRefs.DrainHands(LocalPlayer, whole);
        }

        /// <summary>
        /// Keep the room around the sight when the weapon comes up.
        ///
        /// Scaled by the aim blend rather than switched, so bringing the weapon
        /// into the shoulder does not snap the field of view. Handing the stock
        /// value back when the feature is off matters: it is a STATIC field on
        /// CameraManager, so leaving it modified would follow the player out of
        /// the raid and into the next one.
        /// </summary>


        /// <summary>
        /// Fade the optic's body while aiming. Cheap when off: the sight bone
        /// lookup only happens once the option is actually turned up.
        /// </summary>
        private static void ApplySightTransparency(Plugin p, object pwa, FreeAimConfig cfg)
        {
            if (cfg.SightAlpha.Value <= 0.001f) { OpticHousing.RestoreAll(); return; }

            Transform bone = GameRefs.GetCurrentSightBone(pwa);
            Transform housing = bone == null ? null : GameRefs.GetOpticHousingRoot(bone);

            OpticHousing.Frame(housing, p.State.AimBlend, new OpticHousing.Options
            {
                Enabled = true,
                Alpha = cfg.SightAlpha.Value,
                LogMaterials = cfg.LogSightMaterials.Value
            });
        }

        private static void ApplyBothEyes(Plugin p, FreeAimConfig cfg)
        {
            if (!cfg.KeepPeripheralVision.Value) { GameRefs.ReleaseAimFov(); return; }

            float open = cfg.PeripheralStrength.Value * p.State.AimBlend;
            GameRefs.SetAimFovNarrowing(1f - Mathf.Clamp01(open));
        }

        /// <summary>Last world pivot used, for the HUD.</summary>
        public static Vector3 LastPivot;

        private static bool _warnedPoseRot;
        private static void WarnPoseRotationSuppressed()
        {
            if (_warnedPoseRot) return;
            _warnedPoseRot = true;
            Plugin.Log.LogWarning(
                "Stance pose ROTATION is being ignored, on purpose. It writes WeaponRoot's rotation, " +
                "which is the parent frame the legacy hinge reads, so applying it silently re-aims " +
                "every offset (docs/07-FINDINGS.md F23). Position offsets still apply. Switch Hinge " +
                "mode to Around Grip if you want pose rotations back.");
        }

        /// <summary>
        /// Is WeaponRootAnim actually a descendant of WeaponRoot? F23 turns on
        /// this being true, and it is one line to check rather than assume - the
        /// habit that should have been applied to LocalRotateAround itself.
        /// </summary>
        public static string ParentageReport = "not checked yet";
        private static bool _parentageChecked;
        private static void ReportParentageOnce(Transform anim, Transform root)
        {
            if (_parentageChecked || anim == null || root == null) return;
            _parentageChecked = true;

            int depth = 0;
            bool descends = false;
            for (Transform t = anim.parent; t != null && depth < 12; t = t.parent, depth++)
                if (ReferenceEquals(t, root)) { descends = true; break; }

            ParentageReport = descends
                ? "WeaponRootAnim IS under WeaponRoot (" + depth + " up) - F23 applies"
                : "WeaponRootAnim is NOT under WeaponRoot - F23 does not apply here";
            Plugin.Log.LogInfo("Transform parentage: " + ParentageReport
                + "   anim.parent=" + (anim.parent == null ? "none" : anim.parent.name)
                + "  root=" + root.name);
        }


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

            // Position on the parent is harmless. ROTATION on it is not, and this
            // is the whole of F23: LocalRotateAround reads
            // WeaponRootAnim.parent.TransformDirection, and WeaponRoot IS that
            // parent. Turning it re-aims every offset the mouse produces, which
            // is how a working build quietly stopped working when stance poses
            // arrived. Suppressed while the legacy hinge is in use.
            if (p.Cfg.Hinge.Value != HingeMode.LegacyEuler)
                root.localRotation *= Quaternion.Euler(_loweredRot);
            else
                WarnPoseRotationSuppressed();
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

            // Position on the parent is harmless. ROTATION on it is not, and this
            // is the whole of F23: LocalRotateAround reads
            // WeaponRootAnim.parent.TransformDirection, and WeaponRoot IS that
            // parent. Turning it re-aims every offset the mouse produces, which
            // is how a working build quietly stopped working when stance poses
            // arrived. Suppressed while the legacy hinge is in use.
            if (p.Cfg.Hinge.Value != HingeMode.LegacyEuler)
                root.localRotation *= Quaternion.Euler(_readyRot);
            else
                WarnPoseRotationSuppressed();
        }
    }
}
