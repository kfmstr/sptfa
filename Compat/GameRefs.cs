using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SPTFreeAim.Compat
{
    /// <summary>
    /// EVERY reflective lookup into the game lives here. Hard rule, see CLAUDE.md.
    ///
    /// Tarkov re-obfuscates class and member names on most client updates. When an
    /// update breaks this mod, the break is in this file and nowhere else. Every
    /// lookup is resolved once at startup, reports success or failure by name, and
    /// degrades to a no-op rather than throwing every frame.
    ///
    /// Names here were verified against a real SPT 4.1.5 Assembly-CSharp.dll by
    /// reading its metadata, not inferred from the reference mods. Four of them
    /// differ from what the reference mods' source suggests - see
    /// docs/07-FINDINGS.md F10. Re-verify after any client update.
    /// </summary>
    public static class GameRefs
    {
        public static bool Ready { get; private set; }
        public static string LastError { get; private set; } = "";

        // ---- Types -------------------------------------------------------
        public static Type T_Player;
        public static Type T_ProceduralWeaponAnimation;
        public static Type T_MovementContext;
        public static Type T_HandsContainer;      // PlayerSpring on 4.1.5

        // ---- Player ------------------------------------------------------
        private static readonly Member M_IsYourPlayer = new Member("Player.IsYourPlayer", "IsYourPlayer");
        private static readonly Member M_MovementContext = new Member("Player.MovementContext", "MovementContext");
        private static readonly Member M_Pwa = new Member("Player.ProceduralWeaponAnimation", "ProceduralWeaponAnimation");
        private static readonly Member M_HandsController = new Member("Player.HandsController", "HandsController");

        // ---- MovementContext ---------------------------------------------
        // Yaw and Pitch are READ-ONLY on 4.1.5 - they are computed properties over
        // Rotation (Rotation.x is Yaw, Rotation.y is Pitch, confirmed from the IL).
        // The writable member is Rotation, whose setter runs the game's own
        // pipeline: ClampRotation, previous-rotation bookkeeping, pushing Pitch
        // into the weapon animation, and the hands-to-body angle correction.
        // That is exactly what we want Intercept mode to go through.
        private static readonly Member M_Yaw = new Member("MovementContext.Yaw", "Yaw");
        private static readonly Member M_Pitch = new Member("MovementContext.Pitch", "Pitch");
        private static readonly Member M_Rotation = new Member("MovementContext.Rotation", "Rotation");
        private static MethodInfo _m_SetRotation;

        /// <summary>True when the body bearing can be driven, i.e. Intercept mode is possible.</summary>
        public static bool RotationWritable { get; private set; }

        // ---- ProceduralWeaponAnimation ------------------------------------
        private static readonly Member M_HandsContainer = new Member("PWA.HandsContainer", "HandsContainer");
        private static readonly Member M_IsAiming = new Member("PWA.IsAiming", "IsAiming");

        // ---- The weapon's own recoil ---------------------------------------
        // Tarkov already models recoil as a value that rises on a shot and decays
        // back to zero (RecoilProcessBase has Current, Velocity, ReturnSpeed and
        // Damping). So the "gun returns to its original position" half of
        // docs/07-FINDINGS.md F12.3 is already built and already tuned by BSG -
        // there is nothing to reimplement, only somewhere else to route it.
        //
        //   PWA.Shootingg (field, ShotEffector)
        //     .CurrentRecoilEffect (property, IRecoilShotEffect)
        //       .HandRotationRecoilEffect (property, RotationRecoilProcessBase)
        //         .Current (field on RecoilProcessBase, Vector3)
        private static readonly Member M_Shootingg = new Member("PWA.Shootingg", "Shootingg");
        private static readonly Member M_CurrentRecoilEffect = new Member("ShotEffector.CurrentRecoilEffect", "CurrentRecoilEffect");
        private static readonly Member M_HandRotationRecoil = new Member("IRecoilShotEffect.HandRotationRecoilEffect", "HandRotationRecoilEffect");
        private static readonly Member M_RecoilCurrent = new Member("RecoilProcessBase.Current", "Current");
        private static bool _recoilChainBound;
        public static bool RecoilAvailable { get; private set; }

        // ---- Arm fatigue ---------------------------------------------------
        // Tarkov already models arm fatigue: PhysicalBase carries a HandsStamina
        // pool separate from the main one. Draining THAT rather than inventing a
        // second meter means the game's own consequences - sway, the exhausted
        // state - come along for free.
        private static readonly Member M_Physical = new Member("Player.Physical", "Physical");
        private static readonly Member M_HandsStamina = new Member("PhysicalBase.HandsStamina", "HandsStamina");
        private static readonly Member M_StaminaCurrent = new Member("Stamina.Current", "Current");
        private static Type _boundPhysicalType, _boundStaminaType;

        /// <summary>Set once the hands pool has been reached at least once.</summary>
        public static bool HandsStaminaAvailable { get; private set; }

        // ---- The optic in front of your eye ---------------------------------
        // PWA.CurrentScope is a SightNBone: the scene-side handle on whatever
        // sight is currently being aimed through. Bone is its transform, which is
        // the root of the housing mesh. That is what F26 needs.
        private static readonly Member M_CurrentScope = new Member("PWA.CurrentScope", "CurrentScope");
        private static readonly Member M_SightBone = new Member("SightNBone.Bone", "Bone");
        private static readonly Member M_SightIsOptic = new Member("SightNBone.IsOptic", "IsOptic");
        private static readonly Member M_LensRenderer = new Member("OpticSight.LensRenderer", "LensRenderer");
        private static Type _boundScopeType;
        private static bool _warnedNoScope;

        /// <summary>Component types that draw the aiming dot. Never hidden.</summary>
        private static Type[] _reticleTypes;
        private static Type _t_OpticSight;
        private static Type _t_SightVisual;
        private static bool _loggedChain;

        // ---- Aiming field of view ------------------------------------------
        // Everything here now lives further down, next to the SetFov seam that
        // actually works. AimDeltaFov turned out to be a const that nothing reads
        // at all - see F38.

        // ---- HandsContainer (PlayerSpring) transforms -----------------------
        // All three are public FIELDS, not properties. See Compat/Member.cs.
        private static readonly Member M_WeaponRootAnim = new Member("HandsContainer.WeaponRootAnim", "WeaponRootAnim");
        private static readonly Member M_WeaponRoot = new Member("HandsContainer.WeaponRoot", "WeaponRoot");
        private static readonly Member M_CameraTransform = new Member("HandsContainer.CameraTransform", "CameraTransform");

        // ---- Movement state ------------------------------------------------
        private static readonly Member M_CurrentState = new Member("MovementContext.CurrentState", "CurrentState");
        private static readonly Member M_StateName = new Member("BaseMovementState.Name", "Name");

        // ---- Transform.LocalRotateAround ------------------------------------
        // Extension method. On 4.1.5 it is TransformTools.LocalRotateAround.
        private static MethodInfo _m_LocalRotateAround;
        public static bool UsingLocalRotateAroundFallback { get; private set; }

        // ---- Harmony patch targets -----------------------------------------
        public static MethodInfo M_Player_VisualPass;
        public static MethodInfo M_Pwa_AvoidObstacles;

        /// <summary>
        /// The step that rotates the camera toward the weapon after recoil.
        /// Named AddHandRecoilRotateToCamera(float) on 4.1.5. Realism's reference
        /// source calls it method_19, which is what it was called on the build
        /// Realism targeted - the obfuscated name has since resolved to a real
        /// one. Both are tried.
        /// </summary>
        public static MethodInfo M_Pwa_CameraRecoil;
        public static readonly string[] CameraRecoilMethodNames =
            { "AddHandRecoilRotateToCamera", "method_19" };
        public static string ResolvedCameraRecoilName = "";

        public static void Resolve()
        {
            Ready = false;
            try
            {
                Assembly asmCSharp = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asmCSharp == null) { LastError = "Assembly-CSharp not loaded"; return; }

                T_Player = asmCSharp.GetType("EFT.Player", false);
                T_ProceduralWeaponAnimation = asmCSharp.GetType("EFT.Animations.ProceduralWeaponAnimation", false);
                if (T_Player == null) { LastError = "EFT.Player not found"; return; }
                if (T_ProceduralWeaponAnimation == null) { LastError = "EFT.Animations.ProceduralWeaponAnimation not found"; return; }

                const BindingFlags ANY = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                M_IsYourPlayer.Bind(T_Player);
                M_Pwa.Bind(T_Player);
                M_HandsController.Bind(T_Player);

                if (!M_MovementContext.Bind(T_Player)) { LastError = "Player.MovementContext not found"; return; }
                T_MovementContext = M_MovementContext.MemberType;

                M_Yaw.Bind(T_MovementContext);
                M_Pitch.Bind(T_MovementContext);
                M_Rotation.Bind(T_MovementContext);
                M_CurrentState.Bind(T_MovementContext);
                if (M_CurrentState.Resolved) M_StateName.Bind(M_CurrentState.MemberType);

                if (!M_Yaw.Resolved || !M_Pitch.Resolved)
                {
                    LastError = "MovementContext.Yaw / .Pitch not found - obfuscation changed. " +
                                "Search the dnSpy export for the float pair mouse look drives.";
                    return;
                }

                _m_SetRotation = T_MovementContext.GetMethod("SetRotation", ANY, null,
                    new[] { typeof(Vector2) }, null);
                RotationWritable = (M_Rotation.Resolved && M_Rotation.Writable) || _m_SetRotation != null;

                if (!M_HandsContainer.Bind(T_ProceduralWeaponAnimation))
                {
                    LastError = "ProceduralWeaponAnimation.HandsContainer not found";
                    return;
                }
                M_IsAiming.Bind(T_ProceduralWeaponAnimation);

                T_HandsContainer = M_HandsContainer.MemberType;
                M_WeaponRootAnim.Bind(T_HandsContainer);
                M_WeaponRoot.Bind(T_HandsContainer);
                M_CameraTransform.Bind(T_HandsContainer);
                if (!M_WeaponRootAnim.Resolved)
                {
                    LastError = "HandsContainer.WeaponRootAnim not found - nothing to rotate";
                    return;
                }

                // The recoil chain is bound lazily: CurrentRecoilEffect is an
                // interface, so the concrete type is only known once a weapon
                // exists. Binding it here against the interface would miss.
                M_Shootingg.Bind(T_ProceduralWeaponAnimation);

                ResolveLocalRotateAround(asmCSharp);

                M_Player_VisualPass = T_Player.GetMethod("VisualPass", ANY);
                M_Pwa_AvoidObstacles = T_ProceduralWeaponAnimation.GetMethod("AvoidObstacles", ANY);

                // Optional: absence costs only both-eyes-open, so it must not
                // fail the whole resolve.
                // Optional, for F26. Absence costs only the optic housing.
                _t_OpticSight = asmCSharp.GetType("EFT.CameraControl.OpticSight", false);
                _t_SightVisual = asmCSharp.GetType("SightModVisualControllers", false);
                _reticleTypes = new Type[]
                {
                    asmCSharp.GetType("EFT.CameraControl.OpticRetrice", false),
                    asmCSharp.GetType("EFT.CameraControl.ScopeReticle", false),
                    asmCSharp.GetType("CollimatorSight", false),
                    asmCSharp.GetType("SightingCartridge", false)
                };

                // AimDeltaFov LOOKS like a public static float and is a CONST
                // float of 15. Writing one throws FieldAccessException, and even
                // if it did not, every use site in the game has 15 baked in at
                // compile time, so the write would change nothing.
                //
                // Rejected here, at resolve, rather than discovered by throwing
                // in the middle of a frame. IsLiteral is the only thing that
                // separates a const from a static field, and Cecil reports both
                // the same way. F29.
                // The FOV seam is CameraManager.SetFov, resolved lazily further
                // down - CameraManager does not exist until a raid is running.
                foreach (string n in CameraRecoilMethodNames)
                {
                    M_Pwa_CameraRecoil = T_ProceduralWeaponAnimation.GetMethod(n, ANY);
                    if (M_Pwa_CameraRecoil != null) { ResolvedCameraRecoilName = n; break; }
                }

                if (M_Player_VisualPass == null) { LastError = "Player.VisualPass not found"; return; }
                if (M_Pwa_AvoidObstacles == null)
                {
                    LastError = "ProceduralWeaponAnimation.AvoidObstacles not found - the per-frame " +
                                "hook is gone. Find another late-in-frame method on the same class.";
                    return;
                }

                Ready = true;
                LastError = "";
            }
            catch (Exception e)
            {
                LastError = e.ToString();
                Ready = false;
            }
        }

        private static void ResolveLocalRotateAround(Assembly asmCSharp)
        {
            if (TryFindLocalRotateAround(asmCSharp)) return;

            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = a.GetName().Name;
                if (n == "Assembly-CSharp") continue;
                if (!n.StartsWith("Comfort") && !n.StartsWith("EFT")) continue;
                if (TryFindLocalRotateAround(a)) return;
            }

            _m_LocalRotateAround = null;
            UsingLocalRotateAroundFallback = true;
        }

        private static bool TryFindLocalRotateAround(Assembly asm)
        {
            foreach (Type t in SafeGetTypes(asm))
            {
                MethodInfo m;
                try
                {
                    m = t.GetMethod("LocalRotateAround",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(Transform), typeof(Vector3), typeof(Vector3) },
                        null);
                }
                catch { continue; }

                if (m != null)
                {
                    _m_LocalRotateAround = m;
                    UsingLocalRotateAroundFallback = false;
                    return true;
                }
            }
            return false;
        }

        private static Type[] SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
            catch { return new Type[0]; }
        }

        // ================= Stance state probes ===========================
        // Verified present on 4.1.5: Player.IsSprintEnabled,
        // MovementContext.IsSprintEnabled, Player.IsInventoryOpened, and
        // FirearmController.IsInReloadOperation. Kept as probes anyway, because
        // the point of a probe is to survive the version where one moves.

        public static readonly BoolProbe Probe_SprintOnContext =
            new BoolProbe("sprint (context)", "IsSprintEnabled", "IsSprinting", "Sprinting");

        public static readonly BoolProbe Probe_SprintOnPlayer =
            new BoolProbe("sprint (player)", "IsSprintEnabled", "IsSprinting", "Sprinting");

        public static readonly BoolProbe Probe_InventoryOpen =
            new BoolProbe("inventory open", "IsInventoryOpened", "IsInventoryOpen");

        public static readonly BoolProbe Probe_Reloading =
            new BoolProbe("reloading",
                "IsInReloadOperation", "IsReloading", "IsInReload", "Reloading", "IsChangingWeapon");

        /// <summary>
        /// True when the player is holding something that is not a firearm - a
        /// medkit, a grenade, a melee weapon. Checked by the hands controller's
        /// TYPE rather than a member name: HandsController is typed
        /// AbstractHandsController, and type names survive obfuscation better
        /// than members do.
        /// </summary>
        public static bool HoldingNonFirearm(object player)
        {
            object hc = GetHandsController(player);
            if (hc == null) return false;

            for (Type t = hc.GetType(); t != null; t = t.BaseType)
                if (t.Name.IndexOf("Firearm", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            return true;
        }

        public static string DescribeProbes()
        {
            return "Stance probes: "
                 + Probe_SprintOnContext.Describe() + " | "
                 + Probe_SprintOnPlayer.Describe() + " | "
                 + Probe_InventoryOpen.Describe() + " | "
                 + Probe_Reloading.Describe();
        }

        // ================= Accessors =====================================
        // Nothing outside this file touches reflection.

        public static bool IsYourPlayer(object player) => M_IsYourPlayer.Get(player, false);

        public static object GetMovementContext(object player) => M_MovementContext.Get(player);

        public static object GetPwa(object player) => M_Pwa.Get(player);

        public static object GetHandsController(object player) => M_HandsController.Get(player);

        public static float GetYaw(object mc) => M_Yaw.Get(mc, 0f);

        public static float GetPitch(object mc) => M_Pitch.Get(mc, 0f);

        public static Vector2 GetRotation(object mc) => new Vector2(GetYaw(mc), GetPitch(mc));

        /// <summary>
        /// Drive the body bearing. x is yaw, y is pitch, matching MovementContext's
        /// own Rotation vector.
        ///
        /// Prefers the Rotation property setter over the SetRotation method: the
        /// setter runs the game's whole pipeline (clamping, previous-rotation
        /// bookkeeping, pushing pitch into the weapon animation, the hands-to-body
        /// angle correction), whereas SetRotation is the raw store. Going through
        /// the setter means the game stays internally consistent.
        /// </summary>
        public static bool SetRotation(object mc, Vector2 yawPitch)
        {
            if (mc == null) return false;
            if (M_Rotation.Resolved && M_Rotation.Writable && M_Rotation.Set(mc, yawPitch)) return true;

            if (_m_SetRotation != null)
            {
                try { _m_SetRotation.Invoke(mc, new object[] { yawPitch }); return true; }
                catch { return false; }
            }
            return false;
        }

        public static bool GetIsAiming(object pwa) => M_IsAiming.Get(pwa, false);

        /// <summary>
        /// The weapon's current recoil rotation, in the game's own units. Rises
        /// on a shot and decays back to zero by itself.
        /// Returns zero when unavailable, so a missing member means "no recoil
        /// influence" rather than a crash.
        /// </summary>
        public static Vector3 GetHandRecoil(object pwa)
        {
            object shoot = M_Shootingg.Get(pwa);
            if (shoot == null) return Vector3.zero;

            if (!_recoilChainBound)
            {
                M_CurrentRecoilEffect.Bind(shoot.GetType());
                object eff0 = M_CurrentRecoilEffect.Get(shoot);
                if (eff0 == null) return Vector3.zero;      // no weapon yet; try again next frame

                M_HandRotationRecoil.Bind(eff0.GetType());
                object hand0 = M_HandRotationRecoil.Get(eff0);
                if (hand0 == null) return Vector3.zero;

                M_RecoilCurrent.Bind(hand0.GetType());
                _recoilChainBound = true;
                RecoilAvailable = M_RecoilCurrent.Resolved;
                Plugin.Log.LogInfo("Recoil chain: " + M_CurrentRecoilEffect.Describe()
                    + " | " + M_HandRotationRecoil.Describe() + " | " + M_RecoilCurrent.Describe());
            }

            object eff = M_CurrentRecoilEffect.Get(shoot);
            if (eff == null) return Vector3.zero;
            object hand = M_HandRotationRecoil.Get(eff);
            if (hand == null) return Vector3.zero;
            return M_RecoilCurrent.Get(hand, Vector3.zero);
        }

        public static object GetHandsContainer(object pwa) => M_HandsContainer.Get(pwa);

        public static Transform GetWeaponRootAnim(object pwa)
            => M_WeaponRootAnim.Get(GetHandsContainer(pwa)) as Transform;

        public static Transform GetWeaponRoot(object pwa)
            => M_WeaponRoot.Get(GetHandsContainer(pwa)) as Transform;

        public static Transform GetCameraTransform(object pwa)
            => M_CameraTransform.Get(GetHandsContainer(pwa)) as Transform;

        /// <summary>
        /// The movement state's name ("Stationary", "Sprint", ...). Read as text
        /// so we never need the EPlayerState enum type, which is exactly the sort
        /// of thing that gets renamed. Note Name is a FIELD on BaseMovementState.
        /// </summary>
        public static string GetMovementStateName(object mc)
        {
            object state = M_CurrentState.Get(mc);
            if (state == null) return "";
            object name = M_StateName.Get(state);
            return name == null ? "" : name.ToString();
        }

        /// <summary>
        /// Rotate <paramref name="t"/> by <paramref name="eulerAngles"/> about a
        /// point in the transform's own local space.
        /// </summary>
        public static void LocalRotateAround(Transform t, Vector3 point, Vector3 eulerAngles)
        {
            if (t == null) return;

            if (_m_LocalRotateAround != null)
            {
                _m_LocalRotateAround.Invoke(null, new object[] { t, point, eulerAngles });
                return;
            }

            Quaternion q = Quaternion.Euler(eulerAngles);
            Vector3 worldPivot = t.TransformPoint(point);
            Quaternion worldRot = t.rotation * q * Quaternion.Inverse(t.rotation);
            t.position = worldPivot + worldRot * (t.position - worldPivot);
            t.rotation = worldRot * t.rotation;
        }

        // ================= Arm fatigue ===================================

        private static Type _boundPlayerType;
        private static bool _physicalReported;

        /// <summary>
        /// Player.Physical, bound on first use.
        ///
        /// This was the whole of the arm-drain bug and half of the hold-breath
        /// one: Member.Get does NOT bind on demand, and nothing anywhere called
        /// M_Physical.Bind. So every read returned null, both features quietly did
        /// nothing, and the only symptom was the HUD saying "hands pool not
        /// reached yet" - which reads like the pool being late, not absent
        /// (docs/07-FINDINGS.md F45).
        ///
        /// Exactly F29's shape: an unresolved Member is indistinguishable from a
        /// member that is legitimately null, and neither one throws. So this logs
        /// once, either way, rather than failing in silence a third time.
        /// </summary>
        private static object GetPhysical(object player)
        {
            if (player == null) return null;

            Type plt = player.GetType();
            if (plt != _boundPlayerType)
            {
                _boundPlayerType = plt;
                _physicalReported = false;
                M_Physical.Bind(plt);
            }

            if (!M_Physical.Resolved)
            {
                if (!_physicalReported)
                {
                    _physicalReported = true;
                    Plugin.Log.LogWarning("Player.Physical not found on " + plt.Name +
                        " - arm drain and hold-breath zoom are both unavailable on this build.");
                }
                return null;
            }

            object phys = M_Physical.Get(player);
            if (phys != null && !_physicalReported)
            {
                _physicalReported = true;
                Plugin.Log.LogInfo("Physical: " + M_Physical.Describe() + " on " + plt.Name);
            }
            return phys;
        }

        private static object GetHandsPool(object player)
        {
            if (player == null) return null;

            object phys = GetPhysical(player);
            if (phys == null) return null;

            Type pt = phys.GetType();
            if (pt != _boundPhysicalType)
            {
                if (!M_HandsStamina.Bind(pt)) { _boundPhysicalType = pt; return null; }
                _boundPhysicalType = pt;
                _boundStaminaType = null;
                Plugin.Log.LogInfo("Arm fatigue: " + M_HandsStamina.Describe() + " on " + pt.Name);
            }

            object pool = M_HandsStamina.Get(phys);
            if (pool == null) return null;

            Type st = pool.GetType();
            if (st != _boundStaminaType)
            {
                if (!M_StaminaCurrent.Bind(st)) { _boundStaminaType = st; return null; }
                _boundStaminaType = st;
                HandsStaminaAvailable = true;
                Plugin.Log.LogInfo("Arm fatigue: " + M_StaminaCurrent.Describe() + " on " + st.Name);
            }
            return pool;
        }

        /// <summary>Current hands-stamina value, or -1 when unavailable.</summary>
        public static float GetHandsStamina(object player)
        {
            object pool = GetHandsPool(player);
            return pool == null ? -1f : M_StaminaCurrent.Get(pool, -1f);
        }

        /// <summary>
        /// Take <paramref name="amount"/> off the hands pool.
        ///
        /// Written straight to the field rather than through UpdateStamina(float),
        /// which ignores any change smaller than 1.0 - a deadband that would
        /// swallow a slow per-frame drain entirely. Exhausted is computed from
        /// Current, so the game's own consequences still follow.
        /// </summary>
        public static bool DrainHands(object player, float amount)
        {
            object pool = GetHandsPool(player);
            if (pool == null) return false;

            float cur = M_StaminaCurrent.Get(pool, -1f);
            if (cur < 0f) return false;

            float next = cur - amount;
            if (next < 0f) next = 0f;
            return M_StaminaCurrent.Set(pool, next);
        }

        // ================= The game's own depth of field =================
        //
        // There are TWO DepthOfFields in this game and only one of them is alive.
        //
        //   CameraManager._depthOfField : UnityEngine.Rendering.PostProcessing
        //       .DepthOfField - PPv2. Assigned once in method_2 and read by
        //       exactly one caller, SetZBlur. Not the one you see in a raid.
        //
        //   EffectsController._dof : UnityStandardAssets.ImageEffects.DepthOfField
        //       - the legacy image effect, sitting on the camera GameObject, and
        //       the one the game actually drives: CameraManager.ApplyFoV calls
        //       SetDoFFocalDistance on every FOV change, which writes
        //           focalLength = 5.2 - InverseLerp(minFov, maxFov, fov)
        //                               * (minFov / (maxFov - minFov))
        //       So the game already focuses a few metres out and pulls that in as
        //       you zoom. That is the softness the owner remembered.  F36.
        //
        // The legacy effect is the one worth driving, and it is also the only one
        // with the field this feature needs:
        //
        //   nearBlur - blur things NEARER than the focus plane. The gun is nearer
        //              than anything you are looking at, so without this the gun
        //              never goes soft no matter where focus sits.
        //
        // Because it is depth-based, the blur is a gradient down the weapon on its
        // own: the receiver at ~0.3 m is furthest from focus and blurs hardest,
        // the front sight at ~0.8 m is closer to focus and stays crisper. That
        // gradient is the effect, and no per-renderer trick can produce it.

        private static object _dof;              // the legacy DepthOfField component
        private static FieldInfo _fFocalLength, _fFocalSize, _fMaxBlur,
                                 _fNearBlur, _fFocalTransform;
        private static PropertyInfo _pBehaviourEnabled;

        private static float _stockFocalLength, _stockFocalSize, _stockMaxBlur;
        private static bool _stockNearBlur, _stockEnabled;
        private static object _stockFocalTransform;
        private static bool _haveStock;

        public static bool HaveGameDof { get { return _dof != null; } }
        public static string DofWhyNot = "not resolved yet";

        /// <summary>
        /// Find EffectsController._dof and the five fields worth writing.
        /// Lazy, because none of this exists until a raid is running.
        /// </summary>
        public static bool ResolveGameDof()
        {
            try
            {
                Assembly asmCSharp = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asmCSharp == null) { DofWhyNot = "Assembly-CSharp not loaded"; return false; }

                Type camMgr = asmCSharp.GetType("EFT.CameraControl.CameraManager", false);
                if (camMgr == null) { DofWhyNot = "CameraManager not found"; return false; }

                PropertyInfo instProp = camMgr.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object inst = instProp == null ? null : instProp.GetValue(null, null);
                if (inst == null) { DofWhyNot = "CameraManager.Instance is null (not in a raid yet)"; return false; }

                // ApplyFoV reaches the effect through get_EffectsController(), so
                // the property is the documented route. The backing field is the
                // fallback in case a future build renames the property.
                object effects = null;
                PropertyInfo pEff = camMgr.GetProperty("EffectsController",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pEff != null) effects = pEff.GetValue(inst, null);
                if (effects == null)
                {
                    FieldInfo fEff = camMgr.GetField("_effectsController",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (fEff != null) effects = fEff.GetValue(inst);
                }
                if (effects == null) { DofWhyNot = "EffectsController is null (camera not built yet)"; return false; }

                FieldInfo fDof = effects.GetType().GetField("_dof",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fDof == null) { DofWhyNot = "EffectsController._dof not found"; return false; }

                object dof = fDof.GetValue(effects);
                if (dof == null) { DofWhyNot = "EffectsController._dof is null - Init has not run"; return false; }

                Type t = dof.GetType();
                _fFocalLength    = t.GetField("focalLength",    BindingFlags.Instance | BindingFlags.Public);
                _fFocalSize      = t.GetField("focalSize",      BindingFlags.Instance | BindingFlags.Public);
                _fMaxBlur        = t.GetField("maxBlurSize",    BindingFlags.Instance | BindingFlags.Public);
                _fNearBlur       = t.GetField("nearBlur",       BindingFlags.Instance | BindingFlags.Public);
                _fFocalTransform = t.GetField("focalTransform", BindingFlags.Instance | BindingFlags.Public);

                if (_fFocalLength == null || _fNearBlur == null)
                {
                    DofWhyNot = "DepthOfField is missing focalLength/nearBlur - shape changed, on " + t.FullName;
                    return false;
                }

                // Behaviour.enabled, so the effect can be forced on if the prefab
                // or a graphics preset left it off.
                _pBehaviourEnabled = t.GetProperty("enabled",
                    BindingFlags.Instance | BindingFlags.Public);

                _dof = dof;
                CaptureStock();

                Plugin.Log.LogInfo(string.Format(
                    "Depth of field: driving {0}. Stock focus {1:F2} m, band {2:F2}, blur {3:F2}, nearBlur={4}, enabled={5}, focalTransform={6}",
                    t.FullName, _stockFocalLength, _stockFocalSize, _stockMaxBlur,
                    _stockNearBlur, _stockEnabled,
                    _stockFocalTransform == null ? "none" : "SET - would have overridden focalLength"));
                return true;
            }
            catch (Exception e)
            {
                DofWhyNot = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        private static void CaptureStock()
        {
            _stockFocalLength = ReadF(_fFocalLength, 5.2f);
            _stockFocalSize   = ReadF(_fFocalSize, 0.05f);
            _stockMaxBlur     = ReadF(_fMaxBlur, 2f);
            _stockNearBlur    = ReadB(_fNearBlur);
            _stockFocalTransform = _fFocalTransform == null ? null : _fFocalTransform.GetValue(_dof);
            _stockEnabled = _pBehaviourEnabled == null
                || (_pBehaviourEnabled.GetValue(_dof, null) as bool?).GetValueOrDefault();
            _haveStock = true;
        }

        private static float ReadF(FieldInfo f, float fallback)
        {
            if (f == null) return fallback;
            object v = f.GetValue(_dof);
            return v is float ? (float)v : fallback;
        }

        private static bool ReadB(FieldInfo f)
        {
            if (f == null) return false;
            object v = f.GetValue(_dof);
            return v is bool && (bool)v;
        }

        /// <summary>
        /// Point the game's own depth of field at a distance and let the near
        /// side of it blur.
        ///
        /// focusMetres - where the plane of focus sits, in world units.
        /// band        - focalSize, the depth of the sharp zone. Small = tight.
        /// blur        - maxBlurSize, the blur radius. This is the strength dial,
        ///               and at 0 nothing is visibly blurred.
        /// </summary>
        public static void DriveDepthOfField(float focusMetres, float band, float blur)
        {
            if (_dof == null) return;
            try
            {
                // focalTransform WINS over focalLength when it is set - the effect
                // reads the transform's depth and ignores the number entirely. The
                // game's Init copies one across from the prefab, so writing
                // focalLength without clearing this can be a silent no-op. Exactly
                // the failure mode of F34.
                if (_fFocalTransform != null) _fFocalTransform.SetValue(_dof, null);

                if (_fNearBlur != null) _fNearBlur.SetValue(_dof, true);
                if (_fFocalLength != null) _fFocalLength.SetValue(_dof, focusMetres);
                if (_fFocalSize != null) _fFocalSize.SetValue(_dof, band);
                if (_fMaxBlur != null) _fMaxBlur.SetValue(_dof, blur);
                if (_pBehaviourEnabled != null) _pBehaviourEnabled.SetValue(_dof, true, null);
            }
            catch (Exception e)
            {
                DofWhyNot = e.GetType().Name + " while writing - giving up";
                Plugin.Log.LogWarning("Depth of field: " + DofWhyNot);
                _dof = null;
            }
        }

        /// <summary>
        /// Hand the game its own numbers back, focalTransform included. Never
        /// throws: F29 was an error handler that repeated the failing write.
        /// </summary>
        public static void ReleaseDepthOfField()
        {
            if (_dof == null || !_haveStock) return;
            try
            {
                if (_fFocalTransform != null) _fFocalTransform.SetValue(_dof, _stockFocalTransform);
                if (_fNearBlur != null) _fNearBlur.SetValue(_dof, _stockNearBlur);
                if (_fFocalLength != null) _fFocalLength.SetValue(_dof, _stockFocalLength);
                if (_fFocalSize != null) _fFocalSize.SetValue(_dof, _stockFocalSize);
                if (_fMaxBlur != null) _fMaxBlur.SetValue(_dof, _stockMaxBlur);
                if (_pBehaviourEnabled != null) _pBehaviourEnabled.SetValue(_dof, _stockEnabled, null);
            }
            catch { }
        }

        // ================= The optic in front of your eye ================

        /// <summary>
        /// The transform of the sight currently being aimed through, or null when
        /// there is none - iron sights, or nothing fitted.
        /// </summary>
        public static Transform GetCurrentSightBone(object pwa)
        {
            if (pwa == null) return null;

            // Bind before reading. This was missing, so Get() returned null from
            // an unresolved Member every frame, GetCurrentSightBone always
            // answered "no optic fitted", and the housing effects silently did
            // nothing on every weapon. Nothing in the log, because nothing threw.
            // F29.
            if (!M_CurrentScope.Resolved && !M_CurrentScope.Bind(pwa.GetType()))
            {
                if (!_warnedNoScope)
                {
                    _warnedNoScope = true;
                    Plugin.Log.LogWarning("ProceduralWeaponAnimation.CurrentScope not found, so the " +
                                          "optic housing effects are unavailable on this build.");
                }
                return null;
            }

            object scope = M_CurrentScope.Get(pwa);
            if (scope == null) return null;

            Type st = scope.GetType();
            if (st != _boundScopeType)
            {
                M_SightBone.Bind(st);
                M_SightIsOptic.Bind(st);
                _boundScopeType = st;
                Plugin.Log.LogInfo("Optic: " + M_SightBone.Describe() + " | " + M_SightIsOptic.Describe());
            }

            return M_SightBone.Get(scope) as Transform;
        }

        /// <summary>
        /// True when the sight being aimed through is a magnifying optic.
        ///
        /// This is the branch OnAimOrPoseChanged takes: an optic gets a flat 35
        /// degrees - its magnification - while everything else gets
        /// HeadBobbing minus fifteen, which is the shouldering lean. They look the
        /// same from inside SetFov and they are completely different things, so
        /// cancelling both with one switch takes the magnification off a sniper
        /// scope. F39.
        /// </summary>
        public static bool IsCurrentScopeOptic(object pwa)
        {
            if (pwa == null) return false;
            if (!M_CurrentScope.Resolved && !M_CurrentScope.Bind(pwa.GetType())) return false;

            object scope = M_CurrentScope.Get(pwa);
            if (scope == null) return false;

            Type st = scope.GetType();
            if (st != _boundScopeType)
            {
                M_SightBone.Bind(st);
                M_SightIsOptic.Bind(st);
                _boundScopeType = st;
            }
            return M_SightIsOptic.Get<bool>(scope, false);
        }

        /// <summary>
        /// The root of the optic's MESH, walked up from the aim bone.
        ///
        /// SightNBone.Bone is <c>mod_aim_camera</c> - the transform the game
        /// aligns the eye to. It is an empty node with nothing under it, which is
        /// why the housing collector found zero renderers on every weapon (F33).
        /// The sight body lives further up the hierarchy.
        ///
        /// Preference order: the ancestor carrying the optic's own visual
        /// controller, then the ancestor carrying OpticSight, then simply the
        /// nearest ancestor that actually has meshes. Stops before the weapon
        /// root, because grabbing that would hide the entire gun.
        /// </summary>
        public static Transform GetOpticHousingRoot(Transform aimBone)
        {
            if (aimBone == null) return null;

            LogAncestorChainOnce(aimBone);

            for (Transform t = aimBone; t != null; t = t.parent)
            {
                if (IsWeaponRootName(t.name)) break;
                if (_t_SightVisual != null && t.GetComponent(_t_SightVisual) != null) return t;
                if (_t_OpticSight != null && t.GetComponent(_t_OpticSight) != null) return t;
            }

            int depth = 0;
            for (Transform t = aimBone.parent; t != null && depth < 6; t = t.parent, depth++)
            {
                if (IsWeaponRootName(t.name)) break;
                if (t.GetComponentInChildren<Renderer>(true) != null) return t;
            }
            return null;
        }

        private static bool IsWeaponRootName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            return n.IndexOf("weapon_root", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Weapon_root", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Print the hierarchy from the aim bone up to the weapon root, with the
        /// renderer count at each level.
        ///
        /// The heuristic above is a guess about a hierarchy I cannot see. This
        /// makes the real shape visible in one raid, so if it picks the wrong
        /// node the log says what it should have picked instead.
        /// </summary>
        private static void LogAncestorChainOnce(Transform aimBone)
        {
            if (_loggedChain) return;
            _loggedChain = true;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("Optic hierarchy from the aim bone up:");

            int depth = 0;
            for (Transform t = aimBone; t != null && depth < 10; t = t.parent, depth++)
            {
                Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
                sb.Append("\n    ").Append(depth == 0 ? "* " : "  ")
                  .Append(t.name)
                  .Append("   renderers=").Append(rs.Length);
                if (_t_OpticSight != null && t.GetComponent(_t_OpticSight) != null) sb.Append("  [OpticSight]");
                if (_t_SightVisual != null && t.GetComponent(_t_SightVisual) != null) sb.Append("  [SightModVisualControllers]");
                if (IsWeaponRootName(t.name)) { sb.Append("  <- weapon root, stopping"); break; }
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        /// <summary>
        /// The lens renderer of the optic under <paramref name="bone"/>, when it
        /// has one. This is the window - it must keep drawing.
        /// </summary>
        public static Renderer GetLensRenderer(Transform bone)
        {
            if (bone == null || _t_OpticSight == null) return null;

            Component c = bone.GetComponentInChildren(_t_OpticSight, true);
            if (c == null) return null;

            if (!M_LensRenderer.Resolved) M_LensRenderer.Bind(c.GetType());
            return M_LensRenderer.Get(c) as Renderer;
        }

        /// <summary>True when this object carries a component that draws the aiming dot.</summary>
        public static bool IsReticleObject(GameObject go)
        {
            if (go == null || _reticleTypes == null) return false;
            for (int i = 0; i < _reticleTypes.Length; i++)
            {
                if (_reticleTypes[i] == null) continue;
                if (go.GetComponent(_reticleTypes[i]) != null) return true;
                if (go.GetComponentInParent(_reticleTypes[i]) != null) return true;
            }
            return false;
        }

        private static Material _dumpedFor;

        /// <summary>
        /// Write the optic lens's shader and every property it exposes to the
        /// log, once per material.
        ///
        /// Nothing in Assembly-CSharp exposes a per-optic glare, so whether the
        /// old lens-glare effect can be turned back on is a question about what
        /// the SHADER still carries - and that is runtime data. Rather than guess
        /// at property names, print them.
        /// </summary>
        public static void DumpLensMaterialOnce(Transform bone)
        {
            Renderer lens = GetLensRenderer(bone);
            if (lens == null) return;

            Material m = lens.sharedMaterial;
            if (m == null || ReferenceEquals(m, _dumpedFor)) return;
            _dumpedFor = m;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("Optic lens material: ").Append(m.name)
              .Append("   shader: ").Append(m.shader == null ? "none" : m.shader.name);

            try
            {
                int n = m.shader.GetPropertyCount();
                for (int i = 0; i < n; i++)
                    sb.Append("\n    ").Append(m.shader.GetPropertyType(i))
                      .Append("  ").Append(m.shader.GetPropertyName(i));
            }
            catch (Exception e)
            {
                // Older Unity has no shader reflection at runtime. Fall back to
                // asking about the names a glare would plausibly use.
                sb.Append("\n    (no shader reflection: ").Append(e.GetType().Name).Append(")");
                string[] guesses =
                {
                    "_Glare", "_GlareIntensity", "_LensFlare", "_Flare", "_Reflection",
                    "_ReflectionIntensity", "_ReflectionColor", "_SpecColor", "_Glossiness",
                    "_EmissionColor", "_Fresnel", "_FresnelPower", "_RimColor", "_RimPower"
                };
                for (int i = 0; i < guesses.Length; i++)
                    if (m.HasProperty(guesses[i]))
                        sb.Append("\n    HAS ").Append(guesses[i]);
            }

            Plugin.Log.LogInfo(sb.ToString());
        }

        // ============= The weapon's own rotation centre ==================
        //
        // The owner has said three times that the gun should turn about the RIGHT
        // HAND, and that it still feels like the buttstock is glued to his
        // shoulder. He is right, and the number that fixes it was in the game the
        // whole time (docs/07-FINDINGS.md F38):
        //
        //   PlayerSpring.RotationCenter        - centre WITH the stock braced
        //   PlayerSpring.RotationCenterWoStock - centre with the stock NOT braced
        //
        // Both are Vector3s in WeaponRootAnim's LOCAL space - the same space the
        // legacy pivot already uses - and BSG authors them per weapon.
        // ProceduralWeaponAnimation.ApplyComplexRotation picks between them:
        //
        //   center = _shouldMoveWeaponCloser ? RotationCenterWoStock : RotationCenter;
        //   world  = HandsContainer.WeaponRootAnim.TransformPoint(center);
        //
        // So "about the shoulder" and "about the hands" are not two behaviours to
        // model. They are two numbers the game ships, and the shoulder one is the
        // one that was in use.

        // M_HandsContainer is already declared up with the other PWA members.
        private static readonly Member M_RotationCenter =
            new Member("PlayerSpring.RotationCenter", "RotationCenter");
        private static readonly Member M_RotationCenterWoStock =
            new Member("PlayerSpring.RotationCenterWoStock", "RotationCenterWoStock");

        private static Type _boundPwaType, _boundSpringType;
        public static bool RotationCentreAvailable { get; private set; }
        public static string RotationCentreWhyNot = "not resolved yet";
        public static Vector3 LastRotationCentre;

        /// <summary>
        /// The weapon's own rotation centre, in WeaponRootAnim local space.
        /// <paramref name="stockWeight"/> 0 takes the hands centre, 1 the
        /// shouldered one, and anything between blends.
        /// Returns false when the game does not offer them.
        /// </summary>
        public static bool GetRotationCentre(object pwa, float stockWeight, out Vector3 centre)
        {
            centre = Vector3.zero;
            if (pwa == null) return false;

            Type pt = pwa.GetType();
            if (pt != _boundPwaType)
            {
                _boundPwaType = pt;
                _boundSpringType = null;
                // Resolve() normally binds this already; re-bind only if it did not,
                // so this never clobbers a working binding.
                if (!M_HandsContainer.Resolved && !M_HandsContainer.Bind(pt))
                {
                    RotationCentreWhyNot = "ProceduralWeaponAnimation.HandsContainer not found";
                    return false;
                }
            }

            object spring = M_HandsContainer.Get(pwa);
            if (spring == null) { RotationCentreWhyNot = "HandsContainer is null"; return false; }

            Type st = spring.GetType();
            if (st != _boundSpringType)
            {
                _boundSpringType = st;
                bool a = M_RotationCenter.Bind(st);
                bool b = M_RotationCenterWoStock.Bind(st);
                if (!a && !b)
                {
                    RotationCentreWhyNot = "PlayerSpring has neither RotationCenter nor RotationCenterWoStock";
                    return false;
                }
                RotationCentreAvailable = true;
                Plugin.Log.LogInfo("Rotation centre: " + M_RotationCenter.Describe()
                    + " / " + M_RotationCenterWoStock.Describe() + " on " + st.Name);
            }

            Vector3 stock = M_RotationCenter.Get<Vector3>(spring, Vector3.zero);
            Vector3 hands = M_RotationCenterWoStock.Get<Vector3>(spring, stock);

            // Unclamped on purpose. 0 is the hands, 1 is the shoulder, and the
            // useful range does not stop there: a NEGATIVE weight walks the pivot
            // on past the hands centre - along the shoulder-to-hands line, which
            // points forward down the weapon - so the hinge can be pushed toward
            // the muzzle if the authored hands centre still sits too far back.
            centre = Vector3.LerpUnclamped(hands, stock, stockWeight);
            LastRotationCentre = centre;
            return true;
        }

        // ============ Diagnostic: what the optic actually has ============
        //
        // The curved-glass look in the reference - the rim ring, the darkening
        // toward the edge, the bright hotspot on the glass - is NOT a full-screen
        // effect. It lives inside the scope image, and the scope image is rendered
        // by its own camera with its own post stack:
        //
        //     OpticCameraManager._postProcessVolume : PostProcessVolume
        //     OpticCameraManager._postProcessLayer  : PostProcessLayer
        //     OpticCameraManager.CurrentOpticSight.LensRenderer : Renderer
        //
        // Whether that volume's profile already carries a LensDistortion, a
        // Bloom, a Vignette or a ChromaticAberration - shipped but switched off -
        // is asset data. It cannot be read from the assembly, only from a running
        // raid. So this prints it instead of guessing, which is the whole lesson
        // of F34 and F43.
        //
        // Note the old DumpLensMaterialOnce was never called from anywhere. It sat
        // in the codebase looking like a working diagnostic for several rounds.

        private static bool _opticDumped;

        public static void DumpOpticSetupOnce()
        {
            if (_opticDumped) return;

            try
            {
                Assembly asmCSharp = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asmCSharp == null) return;

                Type camMgr = asmCSharp.GetType("EFT.CameraControl.CameraManager", false);
                if (camMgr == null) return;

                PropertyInfo instProp = camMgr.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object inst = instProp == null ? null : instProp.GetValue(null, null);
                if (inst == null) return;

                PropertyInfo pOcm = camMgr.GetProperty("OpticCameraManager",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object ocm = pOcm == null ? null : pOcm.GetValue(inst, null);
                if (ocm == null) return;

                Type tOcm = ocm.GetType();
                var sb = new StringBuilder();
                sb.AppendLine("=== OPTIC SETUP DUMP (docs/07-FINDINGS.md F43) ===");

                // ---- the optic's own post-process volume ----
                FieldInfo fVol = tOcm.GetField("_postProcessVolume",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object vol = fVol == null ? null : fVol.GetValue(ocm);
                sb.AppendLine("optic post volume: " + (vol == null ? "NULL" : vol.GetType().FullName));

                if (vol != null)
                {
                    object profile = GetMemberValue(vol, "profile") ?? GetMemberValue(vol, "sharedProfile");
                    sb.AppendLine("  profile: " + (profile == null ? "NULL" : profile.ToString()));

                    object settings = profile == null ? null : GetMemberValue(profile, "settings");
                    var list = settings as System.Collections.IEnumerable;
                    if (list == null) sb.AppendLine("  settings: none readable");
                    else
                        foreach (object eff in list)
                        {
                            if (eff == null) continue;
                            object en = GetMemberValue(eff, "enabled");
                            object enVal = en == null ? null : GetMemberValue(en, "value");
                            sb.AppendLine("    EFFECT " + eff.GetType().Name + "   enabled=" +
                                          (enVal == null ? "?" : enVal.ToString()));
                        }
                }

                // ---- the lens itself ----
                PropertyInfo pSight = tOcm.GetProperty("CurrentOpticSight",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object sight = pSight == null ? null : pSight.GetValue(ocm, null);
                sb.AppendLine("current optic sight: " + (sight == null ? "NONE FITTED" : sight.ToString()));

                if (sight != null)
                {
                    FieldInfo fLens = sight.GetType().GetField("LensRenderer",
                        BindingFlags.Instance | BindingFlags.Public);
                    Renderer lens = fLens == null ? null : fLens.GetValue(sight) as Renderer;
                    sb.AppendLine("  lens renderer: " + (lens == null ? "NULL" : lens.name));

                    if (lens != null)
                        foreach (Material mat in lens.sharedMaterials)
                        {
                            if (mat == null) continue;
                            Shader sh = mat.shader;
                            sb.AppendLine("    MATERIAL " + mat.name + "   shader " +
                                          (sh == null ? "NULL" : sh.name));
                            if (sh == null) continue;

                            int n = sh.GetPropertyCount();
                            for (int i = 0; i < n; i++)
                                sb.AppendLine("        " + sh.GetPropertyType(i) + "  " + sh.GetPropertyName(i));
                        }
                }

                _opticDumped = true;
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                _opticDumped = true;
                Plugin.Log.LogWarning("Optic setup dump failed: " + e.Message);
            }
        }

        private static object GetMemberValue(object target, string name)
        {
            if (target == null) return null;
            Type t = target.GetType();
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) { try { return p.GetValue(target, null); } catch { } }
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) { try { return f.GetValue(target); } catch { } }
            return null;
        }

        // ================= Lens glare (Prism) ============================
        //
        // Tarkov ships Prism, and CameraManager already holds one:
        //
        //     CameraManager._prismEffects : PrismEffects
        //
        // assigned in method_2 and already written to by the game itself
        // (SetNoise, EnableAutoExposure, FlyingBulletSoundPlayer's vignette). So
        // this is the same shape as the depth of field in F36 - drive what is
        // already in the render order rather than adding a pass beside it. F43.
        //
        // What it carries, all public instance fields:
        //
        //     useBloom, bloomType, bloomIntensity, bloomThreshold, bloomBlurPasses
        //     useLensDirt, lensDirtTexture, dirtIntensity
        //     useRays, rayTransform, rayWeight, rayColor, rayThreshold
        //     useChromaticAberration, chromaticIntensity, aberrationType
        //
        // Lens dirt is the one that matters most. Bloom on its own reads as a
        // glow; bloom modulated by a dirt texture reads as light scattering off
        // GLASS, which is what a lens does and what the reference footage shows.

        private static object _prism;
        private static readonly string[] PrismDriven =
        {
            "useBloom", "bloomIntensity", "bloomThreshold",
            "useLensDirt", "dirtIntensity",
            "useChromaticAberration", "chromaticIntensity"
        };
        private static readonly Dictionary<string, FieldInfo> _prismFields =
            new Dictionary<string, FieldInfo>();
        private static readonly Dictionary<string, object> _prismStock =
            new Dictionary<string, object>();

        public static bool HavePrism { get { return _prism != null; } }
        public static string PrismWhyNot = "not resolved yet";
        public static bool PrismHasDirtTexture;

        public static bool ResolvePrism()
        {
            try
            {
                Assembly asmCSharp = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asmCSharp == null) { PrismWhyNot = "Assembly-CSharp not loaded"; return false; }

                Type camMgr = asmCSharp.GetType("EFT.CameraControl.CameraManager", false);
                if (camMgr == null) { PrismWhyNot = "CameraManager not found"; return false; }

                PropertyInfo instProp = camMgr.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object inst = instProp == null ? null : instProp.GetValue(null, null);
                if (inst == null) { PrismWhyNot = "CameraManager.Instance is null (not in a raid yet)"; return false; }

                FieldInfo fPrism = camMgr.GetField("_prismEffects",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fPrism == null) { PrismWhyNot = "CameraManager._prismEffects not found"; return false; }

                object prism = fPrism.GetValue(inst);
                if (prism == null) { PrismWhyNot = "_prismEffects is null on this scene"; return false; }

                Type t = prism.GetType();
                _prismFields.Clear();
                _prismStock.Clear();

                foreach (string n in PrismDriven)
                {
                    FieldInfo f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public);
                    if (f == null) continue;
                    _prismFields[n] = f;
                    _prismStock[n] = f.GetValue(prism);
                }

                if (!_prismFields.ContainsKey("bloomIntensity"))
                {
                    PrismWhyNot = "PrismEffects has no bloomIntensity - shape changed on " + t.FullName;
                    return false;
                }

                // Lens dirt without a texture is a silent no-op: the effect runs,
                // multiplies by nothing, and draws no difference. Say so rather
                // than letting the owner turn a dial that cannot move. F34.
                FieldInfo fTex = t.GetField("lensDirtTexture", BindingFlags.Instance | BindingFlags.Public);
                PrismHasDirtTexture = fTex != null && fTex.GetValue(prism) != null;

                _prism = prism;

                Plugin.Log.LogInfo(string.Format(
                    "Lens glare: driving {0}. {1} of {2} fields found, lens dirt texture {3}.",
                    t.FullName, _prismFields.Count, PrismDriven.Length,
                    PrismHasDirtTexture ? "PRESENT" : "MISSING - dirt will do nothing"));
                return true;
            }
            catch (Exception e)
            {
                PrismWhyNot = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        private static void PrismSet(string name, object value)
        {
            FieldInfo f;
            if (_prismFields.TryGetValue(name, out f)) f.SetValue(_prism, value);
        }

        /// <summary>
        /// Push the glare settings. Bloom is scaled rather than replaced, so a
        /// strength of 1 means "the game's own amount" and the dial reads as a
        /// multiplier on whatever BSG tuned rather than an absolute nobody can
        /// picture.
        /// </summary>
        public static void DrivePrism(float bloomMul, float threshold, float dirt, float chromatic)
        {
            if (_prism == null) return;
            try
            {
                object stockBloom;
                float baseBloom = 1f;
                if (_prismStock.TryGetValue("bloomIntensity", out stockBloom) && stockBloom is float)
                    baseBloom = (float)stockBloom;

                PrismSet("useBloom", true);
                PrismSet("bloomIntensity", baseBloom * bloomMul);
                if (threshold > 0f) PrismSet("bloomThreshold", threshold);

                if (dirt > 0.001f && PrismHasDirtTexture)
                {
                    PrismSet("useLensDirt", true);
                    PrismSet("dirtIntensity", dirt);
                }

                if (chromatic > 0.001f)
                {
                    PrismSet("useChromaticAberration", true);
                    PrismSet("chromaticIntensity", chromatic);
                }
            }
            catch (Exception e)
            {
                PrismWhyNot = e.GetType().Name + " while writing - giving up";
                Plugin.Log.LogWarning("Lens glare: " + PrismWhyNot);
                _prism = null;
            }
        }

        /// <summary>Hand every stock value back. Never throws.</summary>
        public static void ReleasePrism()
        {
            if (_prism == null || _prismStock.Count == 0) return;
            try
            {
                foreach (var kv in _prismStock) PrismSet(kv.Key, kv.Value);
            }
            catch { }
        }

        // ================= Aiming field of view ==========================
        //
        // F29 said AimDeltaFov could not be written because it is a const. True,
        // and beside the point: NOTHING READS IT. The 15 is inlined straight into
        // ProceduralWeaponAnimation.OnAimOrPoseChanged, so a writable field would
        // have changed nothing either. F38.
        //
        // What the game actually does, on every aim or pose change:
        //
        //   float fov = !IsAiming        ? HeadBobbing
        //             : CurrentScope.IsOptic ? 35f
        //                                    : HeadBobbing - 15f;
        //   CameraManager.Instance.SetFov(fov, 1f, !_isAiming);
        //
        // So the seam is SetFov, and the honest target to restore is HeadBobbing -
        // the game's own un-aimed value, not a magic 15 subtracted back.

        private static readonly Member M_HeadBobbing =
            new Member("ProceduralWeaponAnimation.HeadBobbing", "HeadBobbing");
        private static readonly Member M_HoldingBreath =
            new Member("PhysicalBase.HoldingBreath", "HoldingBreath");
        private static Type _boundBreathType;

        public static bool AimFovAvailable { get { return _mSetFov != null; } }
        public static string AimFovWhyNot = "not resolved yet";

        private static MethodInfo _mSetFov;
        private static PropertyInfo _pCamMgrInstance;
        private static Type _camMgrType;

        /// <summary>True while the player is holding their breath.</summary>
        public static bool IsHoldingBreath(object player)
        {
            if (player == null) return false;
            object phys = GetPhysical(player);
            if (phys == null) return false;

            Type pt = phys.GetType();
            if (pt != _boundBreathType)
            {
                _boundBreathType = pt;
                if (!M_HoldingBreath.Bind(pt)) return false;
                Plugin.Log.LogInfo("Hold breath: " + M_HoldingBreath.Describe() + " on " + pt.Name);
            }
            return M_HoldingBreath.Get<bool>(phys, false);
        }

        /// <summary>The field of view the game uses when NOT aiming.</summary>
        public static float GetBaseFov(object pwa, float fallback)
        {
            if (pwa == null) return fallback;
            if (!M_HeadBobbing.Resolved && !M_HeadBobbing.Bind(pwa.GetType())) return fallback;
            return M_HeadBobbing.Get<float>(pwa, fallback);
        }

        public static bool ResolveSetFov()
        {
            if (_mSetFov != null) return true;
            try
            {
                Assembly asmCSharp = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asmCSharp == null) { AimFovWhyNot = "Assembly-CSharp not loaded"; return false; }

                _camMgrType = asmCSharp.GetType("EFT.CameraControl.CameraManager", false);
                if (_camMgrType == null) { AimFovWhyNot = "CameraManager not found"; return false; }

                _pCamMgrInstance = _camMgrType.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                _mSetFov = _camMgrType.GetMethod("SetFov",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(float), typeof(float), typeof(bool) }, null);

                if (_mSetFov == null) { AimFovWhyNot = "CameraManager.SetFov(float,float,bool) not found"; return false; }
                return true;
            }
            catch (Exception e)
            {
                AimFovWhyNot = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>The MethodInfo to hand Harmony for the SetFov patch.</summary>
        public static MethodInfo SetFovMethod { get { return _mSetFov; } }

        /// <summary>
        /// Ask the game to move to a field of view over <paramref name="time"/>
        /// seconds, using its own coroutine so nothing fights it frame by frame.
        /// </summary>
        public static bool CallSetFov(float fov, float time)
        {
            if (!ResolveSetFov()) return false;
            try
            {
                object inst = _pCamMgrInstance == null ? null : _pCamMgrInstance.GetValue(null, null);
                if (inst == null) return false;
                _mSetFov.Invoke(inst, new object[] { fov, time, false });
                return true;
            }
            catch (Exception e)
            {
                AimFovWhyNot = e.GetType().Name + " calling SetFov";
                return false;
            }
        }

        public static string Describe()
        {
            if (!Ready) return "GameRefs: NOT READY - " + LastError;

            return "GameRefs OK"
                 + "\n  " + M_Rotation.Describe()
                 + "\n  body bearing drivable (Intercept possible): " + RotationWritable
                     + (_m_SetRotation != null ? "  [SetRotation(Vector2) available]" : "")
                 + "\n  " + M_HandsContainer.Describe()
                 + "\n  " + M_WeaponRootAnim.Describe()
                 + "\n  " + M_WeaponRoot.Describe()
                 + "\n  " + M_CameraTransform.Describe()
                 + "\n  " + M_StateName.Describe()
                 + "\n  LocalRotateAround: " + (UsingLocalRotateAroundFallback
                        ? "FALLBACK (re-tune PivotDistance)" : "game implementation")
                 + "\n  camera recoil hook: " + (M_Pwa_CameraRecoil == null
                        ? "NOT FOUND (recoil decoupling unavailable)" : ResolvedCameraRecoilName);
        }
    }
}
