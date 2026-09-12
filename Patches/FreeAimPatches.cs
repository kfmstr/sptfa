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

        /// <summary>
        /// How many frames in a row may fail before the mod gives up on itself.
        ///
        /// It used to be one. Swapping to a pistol tears the old weapon down and
        /// builds a new one, and for a frame or two in the middle the transforms
        /// this mod reads can be destroyed Unity objects - which throw on access
        /// rather than reading as null. One such frame killed free aim for the
        /// whole session and left the owner pressing the master toggle twice to
        /// get it back (docs/07-FINDINGS.md F42).
        ///
        /// A transient failure during a swap is a hiccup. A real fault fails
        /// every frame and still trips this within half a second. The budget
        /// resets on the first frame that succeeds, so a swap every few minutes
        /// never accumulates.
        /// </summary>
        private const int FailureBudget = 30;
        private static int _consecutiveFailures;

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

        private static bool _fovPatched;

        /// <summary>
        /// Patch CameraManager.SetFov the first time we are actually in a raid.
        /// It cannot be done in Apply: CameraManager is resolved by name from
        /// Assembly-CSharp and does not exist before a raid loads, and a patch
        /// that throws during startup takes the whole plugin with it (F30).
        /// </summary>
        private static void PatchSetFovOnce()
        {
            if (_fovPatched || _harmony == null) return;
            if (!GameRefs.ResolveSetFov()) return;

            _fovPatched = true;
            try
            {
                _harmony.Patch(
                    GameRefs.SetFovMethod,
                    prefix: new HarmonyMethod(typeof(FreeAimPatches).GetMethod(
                        nameof(BeforeSetFov), BindingFlags.Static | BindingFlags.NonPublic)));
                Plugin.Log.LogInfo("Aim FOV: patched CameraManager.SetFov.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Aim FOV: could not patch SetFov, leaving it stock: " + e.Message);
                GameRefs.AimFovWhyNot = "patch failed: " + e.GetType().Name;
            }
        }

        // The last field of view the GAME asked for while aiming. That is its
        // zoom target, whatever it happens to be - HeadBobbing minus fifteen on
        // irons, a flat thirty-five through an optic - so recording it beats
        // recomputing it and cannot drift when BSG changes the numbers.
        private static float _gameAimFov = -1f;
        private static bool _selfCall;
        private static bool _lastWantZoom;
        public static string AimFovState = "stock";

        /// <summary>
        /// Cancel the shouldering zoom, because your head did not move.
        ///
        /// Tarkov narrows the view by fifteen degrees the moment the weapon comes
        /// up, which reads as leaning into the sight. Under free aim that is
        /// wrong twice over: the eye has not moved, and the gun is no longer
        /// nailed to the middle of the screen for it to lean toward.
        ///
        /// It is restored to HeadBobbing - the game's own un-aimed value - rather
        /// than by adding fifteen back, so it stays correct if that number ever
        /// changes.
        /// </summary>
        private static bool BeforeSetFov(ref float x, ref float time, ref bool applyFovOnCamera)
        {
            if (_selfCall) return true;

            FreeAimConfig cfg = Plugin.Instance == null ? null : Plugin.Instance.Cfg;
            if (cfg == null || !cfg.KeepFovWhenAiming.Value) return true;

            ProceduralWeaponAnimation pwa = LocalPwa;
            if (pwa == null) return true;

            float baseFov = GameRefs.GetBaseFov(pwa, -1f);
            if (baseFov <= 0f) return true;

            // Not aiming: this IS the base value. Nothing to do.
            if (x >= baseFov - 0.01f) return true;

            _gameAimFov = x;

            // An optic is a different animal. The game hands it a flat 35 degrees
            // - that IS the magnification - where irons and red dots get
            // HeadBobbing minus fifteen, which is only the shouldering lean.
            // Cancelling both with one switch takes the zoom off a sniper scope,
            // which is not what anyone wants. F39.
            if (GameRefs.IsCurrentScopeOptic(LocalPwa) && !cfg.KeepFovWithOptics.Value)
            {
                AimFovState = "optic - magnification left alone";
                return true;
            }

            if (WantZoom(cfg))
            {
                AimFovState = "zoomed (breath)";
                return true;
            }

            AimFovState = "held open";
            x = baseFov;
            return true;
        }

        private static bool WantZoom(FreeAimConfig cfg)
        {
            return cfg.ZoomOnHoldBreath.Value && GameRefs.IsHoldingBreath(LocalPlayer);
        }

        /// <summary>
        /// Hold your breath and the view leans in - now you really are putting
        /// your eye to the sight.
        ///
        /// The game only calls SetFov when the aim or pose CHANGES, so holding
        /// breath mid-aim would otherwise do nothing. This watches the breath
        /// state and makes that call itself, through the game's own coroutine, so
        /// nothing is fighting the camera frame by frame.
        /// </summary>
        private static void ApplyAimFov(Plugin p, FreeAimConfig cfg)
        {
            PatchSetFovOnce();

            if (!cfg.KeepFovWhenAiming.Value) { _lastWantZoom = false; AimFovState = "stock"; return; }

            bool aiming = p.State.AimBlend > 0.5f;
            bool want = aiming && WantZoom(cfg);

            if (want == _lastWantZoom) return;
            _lastWantZoom = want;

            float baseFov = GameRefs.GetBaseFov(LocalPwa, -1f);
            if (baseFov <= 0f) return;

            float target = want && _gameAimFov > 0f ? _gameAimFov : baseFov;

            _selfCall = true;
            try { GameRefs.CallSetFov(target, cfg.ZoomTime.Value); }
            finally { _selfCall = false; }

            AimFovState = want ? "zooming in (breath)" : "easing back out";
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
            FocusDepth.Release();
            OpticHousing.Release();
            Compat.OpticStack.Release();
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

            try
            {
                Frame(__instance);
                if (_consecutiveFailures != 0)
                {
                    Plugin.Log.LogInfo("Free aim recovered after " + _consecutiveFailures +
                                       " failed frame(s) - carrying on.");
                    _consecutiveFailures = 0;
                }
            }
            catch (Exception e)
            {
                _consecutiveFailures++;

                // Log the first few in full, then go quiet. The old handler was
                // right that an exception once a frame floods the log; it was
                // wrong about the remedy.
                if (_consecutiveFailures <= 3)
                    Plugin.Log.LogError("Free aim frame failed (" + _consecutiveFailures + " of " +
                                        FailureBudget + " before giving up):\n" + e);

                if (_consecutiveFailures >= FailureBudget)
                {
                    Plugin.Log.LogError("Free aim has failed " + FailureBudget +
                                        " frames in a row. This is not a hiccup.");
                    Plugin.Instance.EmergencyDisable();
                }
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

            // The diagnostic that was never wired up. One raid with this on tells
            // us what the optic's own post stack already carries, which is the
            // only way to know - it is asset data, not code. F43.
            if (cfg.DumpLensMaterial.Value) GameRefs.DumpOpticSetupOnce();

            try { ApplyOpticGlass(cfg); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Optic glass failed, switching it off: " + e);
                cfg.OpticBloom.Value = 0f;
                cfg.OpticDirt.Value = 0f;
                cfg.OpticFringe.Value = 0f;
                cfg.OpticVignette.Value = 0f;
                cfg.OpticDistortion.Value = 0f;
                Compat.OpticStack.Release();
            }

            try { ApplySightTransparency(p, pwa, cfg); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Sight transparency failed, switching it off: " + e);
                cfg.SightAlpha.Value = 0f;
                OpticHousing.Release();
            }

            try { ApplyAimFov(p, cfg); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Aim FOV failed, switching it off: " + e);
                cfg.KeepFovWhenAiming.Value = false;
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
                Vector2 scale = cfg.RecoilGunScale;
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

            ComputeBodyLean(applied.x, st.AimBlend, cfg);

            bool doCompensate = tuning.Mode == DriveMode.Compensate && cfg.ApplyCameraOffset.Value;
            bool doLean = cfg.BodyLeanActive;
            bool doCamera = doCompensate || doLean;
            bool doWeapon = cfg.ApplyWeaponOffset.Value;
            bool doPose = cfg.LoweredPoseEnabled.Value && cfg.StanceGateEnabled.Value;

            // Undo last frame's work first, so what we apply is never applied twice.
            if (doCamera) GuardCamera.BeginFrame(cameraTransform); else GuardCamera.Release(cameraTransform);
            if (doWeapon) GuardWeapon.BeginFrame(weaponRootAnim); else GuardWeapon.Release(weaponRootAnim);
            if (doPose) GuardPose.BeginFrame(weaponRoot); else GuardPose.Release(weaponRoot);

            if (doCamera)
            {
                if (doCompensate) ApplyCameraOffset(pwa, -applied);
                if (doLean) ApplyLeanToCamera(pwa);
                GuardCamera.EndFrame(cameraTransform);
            }
            if (doWeapon)
            {
                ApplyWeaponOffset(pwa, applied, cfg);
                ApplyGunRoll(pwa, weaponRootAnim, applied.x, st.AimBlend, cfg);
                if (doLean && cfg.GunLeansWithBody.Value) ApplyLeanToWeapon(pwa, weaponRootAnim);
                ApplyShoulderGive(pwa, weaponRootAnim, applied.x, st.AimBlend, cfg);
                GuardWeapon.EndFrame(weaponRootAnim);
            }
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
            Vector3 pivot = Vector3.up * cfg.PivotDistance.Value + cfg.PivotFineOffset;

            // Or let the weapon say where it turns.
            //
            // PlayerSpring carries RotationCenter and RotationCenterWoStock, both
            // Vector3s in exactly this space, both authored by BSG per weapon, and
            // the game itself picks between them in ApplyComplexRotation. The
            // first is the centre with the buttstock braced - which is why
            // swinging has felt like the stock was glued to the shoulder. The
            // second is the centre with it not braced: the hands.
            //
            // A measured number from the weapon beats a dial tuned by eye on one
            // gun, and it is right on every other gun for free. F38.
            Vector3 gameCentre;
            if (cfg.PivotFromWeapon.Value &&
                GameRefs.GetRotationCentre(pwa, cfg.PivotStockWeight.Value, out gameCentre))
                pivot = gameCentre + cfg.PivotFineOffset;

            LastPivotLocal = pivot;

            GameRefs.LocalRotateAround(root, pivot, new Vector3(pitch, 0f, yaw));

            // Without this second call the pivot is left displaced and every
            // offset applied after ours is wrong. lualeet's comment, and it is
            // correct - do not remove it as dead code.
            GameRefs.LocalRotateAround(root, -pivot, Vector3.zero);
        }

        /// <summary>
        /// How hard the weapon is being swung, as a signed 0..1.
        ///
        /// Shared by the wrist cant and the body lean so the two cannot drift out
        /// of step - they are two reactions to one movement, and if they were
        /// computed separately they would eventually disagree about when the
        /// movement started.
        ///
        ///     dead = cone * (1 - aimBlend)
        ///     t    = clamp01((|yaw| - dead) / (cap - dead)) * sign(yaw)
        ///
        /// The deadband is the cone, fading out as the weapon comes up: braced
        /// against the shoulder there is no slack and everything reacts at once;
        /// at low ready the slack has to be taken up first. F37.
        /// </summary>
        private static float SwingFraction(float yawOffset, float blend, FreeAimConfig cfg)
        {
            float cone = Mathf.Abs(cfg.ConeDegrees.Value);
            float cap = Mathf.Abs(cfg.CapDegrees.Value);

            float dead = cone * (1f - Mathf.Clamp01(blend));
            float span = Mathf.Max(cap - dead, 1f);

            float mag = Mathf.Max(0f, Mathf.Abs(yawOffset) - dead);
            return Mathf.Clamp01(mag / span) * Mathf.Sign(yawOffset);
        }

        /// <summary>Last body lean applied, in degrees, for the HUD.</summary>
        public static float LastLean;

        /// <summary>Last shoulder slide, in metres, for the HUD.</summary>
        public static float LastGive;

        /// <summary>
        /// The shoulder pocket is flesh, not a bolt.
        ///
        /// The owner said this in the original five-degrees-of-freedom message -
        /// "the buttstock and right hand almost not moving (we can have a bit of
        /// the leeway when turning)" - and again looking at the reference: the
        /// weapon is not welded in place while shouldered, it slides a centimetre
        /// or two under a hard swing and comes back.
        ///
        /// Translation, not rotation: the hinge stays where the weapon says it is
        /// (F38), and this is the give ON TOP of it. It runs opposite the swing,
        /// because the weapon's own mass is what loads the pocket - swing right,
        /// the stock is left behind for a moment.
        ///
        /// Scaled by the aim blend, since a weapon that is not in the shoulder has
        /// no pocket to give.
        /// </summary>
        private static void ApplyShoulderGive(ProceduralWeaponAnimation pwa, Transform anim,
                                              float yawOffset, float aimBlend, FreeAimConfig cfg)
        {
            LastGive = 0f;
            if (Mathf.Abs(cfg.ShoulderGive.Value) < 0.0001f || anim == null) return;

            float blend = Mathf.Clamp01(aimBlend);
            float t = SwingFraction(yawOffset, blend, cfg);

            float give = -t * cfg.ShoulderGive.Value * blend;
            LastGive = give;
            if (Mathf.Abs(give) < 0.0002f) return;

            Transform cam = GameRefs.GetCameraTransform(pwa);
            Vector3 axis = cam == null ? anim.right : cam.right;
            anim.position += axis * give;
        }

        /// <summary>
        /// The counterbalance: swing the weapon right and the torso leans LEFT.
        ///
        /// This is what a body does when it throws a rifle around - the mass goes
        /// one way and the spine goes the other to keep the weight over the feet.
        /// On a bodycam it reads as the horizon tipping as the shooter turns, and
        /// it is most of what makes that footage feel like a person rather than a
        /// tripod.
        ///
        /// Computed once here and applied in two places, because the camera and
        /// the weapon live under different guards. Nothing is written yet.
        /// </summary>
        private static void ComputeBodyLean(float yawOffset, float aimBlend, FreeAimConfig cfg)
        {
            if (!cfg.BodyLeanActive) { LastLean = 0f; return; }

            float blend = Mathf.Clamp01(aimBlend);
            float t = SwingFraction(yawOffset, blend, cfg);
            float degrees = Mathf.Lerp(cfg.BodyLeanReady.Value, cfg.BodyLeanAimed.Value, blend);

            // Negated: the lean opposes the swing. Which screen direction that is
            // depends on the camera's handedness, so there is an invert switch and
            // the honest instruction is to look rather than to reason about it.
            float lean = -t * degrees;
            if (cfg.InvertBodyLean.Value) lean = -lean;

            LastLean = lean;
        }

        /// <summary>Roll the view. Inside GuardCamera.</summary>
        private static void ApplyLeanToCamera(ProceduralWeaponAnimation pwa)
        {
            if (Mathf.Abs(LastLean) < 0.01f) return;
            Transform cam = GameRefs.GetCameraTransform(pwa);
            if (cam == null) return;

            // Local Z is the camera's forward, so this is a roll and nothing else.
            cam.localRotation = cam.localRotation * Quaternion.Euler(0f, 0f, LastLean);
        }

        /// <summary>
        /// Carry the weapon with the lean, so the gun stays welded to the body
        /// instead of hanging level while the horizon tips.
        ///
        /// Skipped when the weapon already descends from the camera transform -
        /// then Unity has carried it for us and doing it again would double the
        /// angle. Checked rather than assumed, because the parentage in this rig
        /// has already been wrong twice (F23, F33). Inside GuardWeapon.
        /// </summary>
        private static void ApplyLeanToWeapon(ProceduralWeaponAnimation pwa, Transform anim)
        {
            if (Mathf.Abs(LastLean) < 0.01f || anim == null) return;
            Transform cam = GameRefs.GetCameraTransform(pwa);
            if (cam == null || anim.IsChildOf(cam)) return;

            // About the eye, not about the weapon's own origin: a body leaning
            // swings everything it is carrying about the spine, and the eye is the
            // closest thing to that axis we have.
            Quaternion q = Quaternion.AngleAxis(LastLean, cam.forward);
            anim.position = cam.position + q * (anim.position - cam.position);
            anim.rotation = q * anim.rotation;
        }

        /// <summary>
        /// Cant the weapon as it swings - the wrist rolling as the support hand
        /// leads the gun around.
        ///
        /// The owner's description, and the sign convention: turn RIGHT and the
        /// gun rolls CLOCKWISE from behind it, turn left and it rolls
        /// counter-clockwise. It is more pronounced while aiming, and at low ready
        /// it only appears once the gun is pushed well past the cone.
        ///
        /// That last part is the interesting half. Aimed, the weapon is braced
        /// against the shoulder and every bit of swing twists it, so the roll is
        /// proportional from zero. At low ready the weapon hangs off the hands
        /// with slack in the wrists, and nothing twists until the swing takes up
        /// that slack - which is exactly the cone. So the deadband IS the cone,
        /// and it fades out as the weapon comes up.
        ///
        ///     dead = cone * (1 - aimBlend)
        ///     t    = clamp01((|yaw| - dead) / (cap - dead)) * sign(yaw)
        ///     roll = t * lerp(degreesReady, degreesAimed, aimBlend)
        ///
        /// This rotates orientation only and never position, so it cannot displace
        /// the weapon or disturb the offset applied just before it. Off by
        /// default, like every mechanic added since F20.
        /// </summary>
        private static void ApplyGunRoll(ProceduralWeaponAnimation pwa, Transform root,
                                         float yawOffset, float aimBlend, FreeAimConfig cfg)
        {
            if (!cfg.GunRollEnabled.Value || root == null) return;

            float blend = Mathf.Clamp01(aimBlend);
            float t = SwingFraction(yawOffset, blend, cfg);
            float degrees = Mathf.Lerp(cfg.GunRollReady.Value, cfg.GunRollAimed.Value, blend);
            float roll = t * degrees;
            if (cfg.InvertGunRoll.Value) roll = -roll;

            LastRoll = roll;
            if (Mathf.Abs(roll) < 0.01f) return;

            // Which axis to roll about is a real question and the reason it is a
            // setting. The weapon's own forward is the barrel on every EFT weapon
            // seen so far, and rolling about it is a true cant. The camera's
            // forward is the safe fallback: it cannot be wrong, because it is the
            // axis the player is looking down, but at low ready - where the muzzle
            // is pointed at the floor - it reads as a twist rather than a cant.
            //
            // Note this is a much smaller bet than F20's: a roll about a slightly
            // wrong axis looks like a slightly different tilt, where a PIVOT on a
            // wrong axis put the hinge in the next room.
            Vector3 axis = root.forward;
            if (cfg.GunRollAboutView.Value)
            {
                Transform cam = GameRefs.GetCameraTransform(pwa);
                if (cam != null) axis = cam.forward;
            }

            root.rotation = Quaternion.AngleAxis(roll, axis) * root.rotation;
        }

        /// <summary>Last roll applied, in degrees, for the HUD.</summary>
        public static float LastRoll;

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
                    Plugin.Log.LogWarning(
                        "HandsContainer.CameraTransform not available this frame, so the grip pivot " +
                        "cannot be placed. Skipping the offset for this frame only.");
                }

                // Deliberately NOT writing cfg.Hinge here. It used to, and that
                // turned a single missing frame - which is exactly what happens
                // while a weapon swap tears the old rig down - into a permanent,
                // silent change to the owner's setting, dropping him back onto
                // the hinge that cannot hinge (F40, F42). A transient miss earns
                // a skipped frame, not a rewritten config.
                return;
            }

            // The grip, as a point in the world.
            //
            // Two ways to get one. The weapon's own rotation centre is the better
            // one - measured, per weapon, and placed exactly the way the game
            // places it in ApplyComplexRotation:
            //
            //     world = HandsContainer.WeaponRootAnim.TransformPoint(centre)
            //
            // and `root` here IS WeaponRootAnim. The camera-relative grip point is
            // the fallback, for when the weapon does not carry a centre or you
            // want to place the hand yourself.
            Vector3 centre;
            Vector3 pivot;
            if (cfg.PivotFromWeapon.Value &&
                GameRefs.GetRotationCentre(pwa, cfg.PivotStockWeight.Value, out centre))
                pivot = root.TransformPoint(centre + cfg.PivotFineOffset);
            else
                pivot = cam.TransformPoint(cfg.GripFromEye);

            // Yaw about the world vertical, pitch about the camera's right. Using
            // the camera's right rather than the world X keeps pitch square to
            // where you are looking when you are turned away from the axes.
            Quaternion q = Quaternion.AngleAxis(yaw, Vector3.up)
                         * Quaternion.AngleAxis(-pitch, cam.right);

            // A rigid body turned about a point: rotate the position around it,
            // and rotate the orientation by the same amount. Nothing else - no
            // second cancelling call, because nothing was displaced.
            // A rigid body turned about a point: rotate the position around it and
            // rotate the orientation by the same amount. This is the whole reason
            // to be in this mode - it is an exact rotation about `pivot`, so the
            // muzzle swings one way and the buttstock swings the other, by the
            // lever arm each side of the hand.
            //
            // The legacy hinge cannot do this. TransformTools.LocalRotateAround
            // displaces by (I - q)*c where a rotation about c needs R*(I - q)*c,
            // so its lever arm comes out in the parent's frame rather than the
            // weapon's - right length, wrong direction, and the horizontal swing
            // leaks into pitch and into the barrel axis. F40.
            LastLever = Vector3.Distance(root.position, pivot);

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


        private static bool _opticGlassDriving;

        /// <summary>
        /// The glass effects that belong inside the tube rather than across the
        /// whole screen. F49.
        /// </summary>
        private static void ApplyOpticGlass(FreeAimConfig cfg)
        {
            if (!cfg.OpticGlassActive)
            {
                if (_opticGlassDriving) { _opticGlassDriving = false; Compat.OpticStack.Release(); }
                return;
            }

            if (!Compat.OpticStack.Ready && !Compat.OpticStack.Resolve()) return;

            _opticGlassDriving = true;
            Compat.OpticStack.Drive(new Compat.OpticStack.Glass
            {
                Bloom          = cfg.OpticBloom.Value,
                BloomThreshold = cfg.OpticBloomThreshold.Value,
                BloomSpread    = cfg.OpticBloomSpread.Value,
                Dirt           = cfg.OpticDirt.Value,
                Fringe         = cfg.OpticFringe.Value,
                Vignette       = cfg.OpticVignette.Value,
                Distortion     = cfg.OpticDistortion.Value,
                ForceHdr       = cfg.OpticForceHdr.Value
            });
        }

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

        /// <summary>Last world pivot used, for the HUD.</summary>
        public static Vector3 LastPivot;

        /// <summary>Last local pivot used by the legacy hinge, for the HUD.</summary>
        public static Vector3 LastPivotLocal;

        /// <summary>
        /// Distance from the weapon root to the hinge, in metres. This is the
        /// lever arm, and it is the number that decides how far the weapon
        /// actually swings - zero here means it is spinning about its own origin
        /// no matter what the pivot dials say.
        /// </summary>
        public static float LastLever;

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

            _loweredPos = Vector3.Lerp(_loweredPos, down ? p.Cfg.LoweredPos : Vector3.zero, speed);
            _loweredRot = Vector3.Lerp(_loweredRot, down ? p.Cfg.LoweredRot : Vector3.zero, speed);

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

            _readyPos = Vector3.Lerp(_readyPos, p.Cfg.ReadyPos * weight, speed);
            _readyRot = Vector3.Lerp(_readyRot, p.Cfg.ReadyRot * weight, speed);

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
