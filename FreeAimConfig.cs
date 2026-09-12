using BepInEx.Configuration;
using SPTFreeAim.Core;
using SPTFreeAim.Patches;
using UnityEngine;

namespace SPTFreeAim
{
    /// <summary>
    /// Everything user-facing lives in the F12 menu. Hard rule, see CLAUDE.md.
    /// Values marked [spec] come from docs/01-SPEC.md; values marked [guess] are
    /// placeholders waiting on measurement (docs/05-OPEN-QUESTIONS.md Q1).
    /// </summary>
    public class FreeAimConfig
    {
        private const string S_MAIN = "1. Main";
        private const string S_COUPLING = "2. Coupling";
        private const string S_PIVOT = "3. Pivot";
        private const string S_STANCE = "4. Stance";
        private const string S_RECOIL = "5. Recoil";
        private const string S_BODY = "6. Body";
        private const string S_OPTIC = "7. Optic glass";
        private const string S_DEBUG = "9. Debug";

        // ---- Main --------------------------------------------------------
        public ConfigEntry<bool> Enabled;
        public ConfigEntry<DriveMode> Mode;
        public ConfigEntry<KeyboardShortcut> ToggleKey;

        // ---- Coupling ----------------------------------------------------
        public ConfigEntry<float> ConeDegrees;
        public ConfigEntry<float> CapDegrees;
        public ConfigEntry<float> SpringK;
        public ConfigEntry<float> PushFactor;
        public ConfigEntry<float> AimCoupling;
        public ConfigEntry<float> DisengageBoost;

        // ---- Pivot -------------------------------------------------------
        public ConfigEntry<HingeMode> Hinge;
        public ConfigEntry<float> GripRight, GripUp, GripForward;
        public Vector3 GripFromEye
        { get { return new Vector3(GripRight.Value, GripUp.Value, GripForward.Value); } }
        public ConfigEntry<float> PivotDistance;
        public ConfigEntry<float> PivotOffsetX, PivotOffsetY, PivotOffsetZ;
        public Vector3 PivotFineOffset
        { get { return new Vector3(PivotOffsetX.Value, PivotOffsetY.Value, PivotOffsetZ.Value); } }
        public ConfigEntry<bool> InvertGamePitch;
        public ConfigEntry<bool> ApplyWeaponOffset;
        public ConfigEntry<bool> ApplyCameraOffset;
        public ConfigEntry<bool> InvertYaw;
        public ConfigEntry<bool> InvertPitch;
        public ConfigEntry<bool> SwapAxes;

        // ---- Stance ------------------------------------------------------
        public ConfigEntry<bool> StanceGateEnabled;
        public ConfigEntry<KeyboardShortcut> StanceKey;
        public ConfigEntry<bool> StanceHoldToReady;
        public ConfigEntry<float> GateSpeed;
        public ConfigEntry<bool> LoweredPoseEnabled;
        public ConfigEntry<float> LoweredPosX, LoweredPosY, LoweredPosZ;
        public Vector3 LoweredPos
        { get { return new Vector3(LoweredPosX.Value, LoweredPosY.Value, LoweredPosZ.Value); } }
        public ConfigEntry<float> LoweredRotX, LoweredRotY, LoweredRotZ;
        public Vector3 LoweredRot
        { get { return new Vector3(LoweredRotX.Value, LoweredRotY.Value, LoweredRotZ.Value); } }
        public ConfigEntry<float> LoweredLerpSpeed;
        public ConfigEntry<bool> SuspendOnSprint;
        public ConfigEntry<bool> SuspendOnAnimation;
        public ConfigEntry<bool> SuspendOnStationary;
        public ConfigEntry<bool> ReadyPoseEnabled;
        public ConfigEntry<float> ReadyPosX, ReadyPosY, ReadyPosZ;
        public Vector3 ReadyPos
        { get { return new Vector3(ReadyPosX.Value, ReadyPosY.Value, ReadyPosZ.Value); } }
        public ConfigEntry<float> ReadyRotX, ReadyRotY, ReadyRotZ;
        public Vector3 ReadyRot
        { get { return new Vector3(ReadyRotX.Value, ReadyRotY.Value, ReadyRotZ.Value); } }

        // ---- Recoil ------------------------------------------------------
        public ConfigEntry<bool> DecoupleRecoil;
        public ConfigEntry<float> HipCameraFollow;
        public ConfigEntry<float> AimCameraFollow;
        public ConfigEntry<bool> RecoilMovesGun;
        public ConfigEntry<float> RecoilScaleYaw, RecoilScalePitch;
        public Vector2 RecoilGunScale
        { get { return new Vector2(RecoilScaleYaw.Value, RecoilScalePitch.Value); } }
        public ConfigEntry<bool> RecoilSwapAxes;

        // ---- Body --------------------------------------------------------
        public ConfigEntry<bool> ArmDrainEnabled;
        public ConfigEntry<float> ArmDrainRate;
        public ConfigEntry<float> ArmDrainAimedMultiplier;
        public ConfigEntry<bool> DofEnabled;
        public ConfigEntry<float> DofBlurSize;
        public ConfigEntry<float> DofBand;
        public ConfigEntry<float> DofMaxDistance;
        public ConfigEntry<bool> DofOnlyAiming;
        public ConfigEntry<float> DofStrength;
        public ConfigEntry<bool> GunRollEnabled;
        public ConfigEntry<float> GunRollAimed;
        public ConfigEntry<float> GunRollReady;
        public ConfigEntry<bool> InvertGunRoll;
        public ConfigEntry<bool> GunRollAboutView;
        public ConfigEntry<float> ShoulderGive;
        public ConfigEntry<float> OpticBloom;
        public ConfigEntry<float> OpticBloomThreshold;
        public ConfigEntry<float> OpticBloomSpread;
        public ConfigEntry<bool>  OpticForceHdr;
        public ConfigEntry<float> OpticDirt;
        public ConfigEntry<float> OpticFringe;
        public ConfigEntry<float> OpticVignette;
        public ConfigEntry<float> OpticDistortion;

        /// <summary>
        /// Any glass effect asked for at all. Same shape as the others: a value
        /// that means nothing IS the off switch, no separate bool. F44.
        /// </summary>
        public bool OpticGlassActive
        {
            get
            {
                return OpticBloom.Value > 0.001f
                    || OpticDirt.Value > 0.001f
                    || OpticFringe.Value > 0.001f
                    || OpticVignette.Value > 0.001f
                    || Mathf.Abs(OpticDistortion.Value) > 0.001f;
            }
        }

        // No master switches for these two. A master bool sitting above a row of
        // dials looks optional, and the dials look like the feature - so the dials
        // get turned up and the switch never gets flipped, and the owner is left
        // with a set of settings that provably do nothing (docs/07-FINDINGS.md
        // F44). Every one of these is derived from its own numbers instead, the
        // way "Shoulder give" always was: a value that means "nothing" IS the off
        // switch, and there is only one thing to set.
        public bool BodyLeanActive
        {
            get
            {
                return Mathf.Abs(BodyLeanAimed.Value) > 0.01f
                    || Mathf.Abs(BodyLeanReady.Value) > 0.01f;
            }
        }
        public ConfigEntry<float> BodyLeanAimed;
        public ConfigEntry<float> BodyLeanReady;
        public ConfigEntry<bool> InvertBodyLean;
        public ConfigEntry<bool> GunLeansWithBody;
        public ConfigEntry<float> SightAlpha;
        public ConfigEntry<bool> LogSightMaterials;
        public ConfigEntry<bool> KeepFovWhenAiming;
        public ConfigEntry<bool> KeepFovWithOptics;
        public ConfigEntry<bool> ZoomOnHoldBreath;
        public ConfigEntry<float> ZoomTime;
        public ConfigEntry<bool> PivotFromWeapon;
        public ConfigEntry<float> PivotStockWeight;

        // ---- Debug -------------------------------------------------------
        public ConfigEntry<bool> ShowHud;
        public ConfigEntry<KeyboardShortcut> HudKey;
        public ConfigEntry<KeyboardShortcut> ProbeWriteKey;
        public ConfigEntry<float> ProbeWriteDegrees;

        public void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind(S_MAIN, "Enabled", true,
                "Master switch. Bind a key below so you can A/B the feel instantly.");

            Mode = cfg.Bind(S_MAIN, "Drive mode", DriveMode.Intercept, new ConfigDescription(
                "Intercept: THE REAL MECHANIC, and the default. The mouse drives a gun bearing " +
                "nothing else can see, and the lagging body bearing is what gets written into the " +
                "game. The body genuinely trails the gun. Needs MovementContext.Rotation writes to " +
                "hold - the startup log says whether they can.\n" +
                "\n" +
                "Compensate: no interception. The game's bearing stays 1:1 with the mouse and only " +
                "the rendered camera is rotated back. Produces the same gun-to-view angle on screen, " +
                "but the BODY still leads - which is backwards from docs/04-DECISIONS.md D4. Useful " +
                "as a fallback if Intercept writes do not hold, not as the target feel.\n" +
                "\n" +
                "Reactive: camera leads, gun trails. The documented fallback, D10.\n" +
                "Disabled: resolve and log only."));

            ToggleKey = cfg.Bind(S_MAIN, "Master toggle key",
                new KeyboardShortcut(KeyCode.F8),
                "Turns the whole effect on and off in raid, for side-by-side comparison.\n" +
                "Not F7: that is UnityExplorer's default UI toggle, and F12 is the BepInEx " +
                "config menu. F8, F9 and F10 are free.");

            // -- coupling --
            ConeDegrees = cfg.Bind(S_COUPLING, "Cone size (deg)", 10f, new ConfigDescription(
                "[GUESS - docs/05-OPEN-QUESTIONS.md Q1] How far the gun leads the body on a " +
                "steady turn before the mouse starts pushing the body too. This is the number " +
                "that decides whether the mod feels subtle or disorienting. Measure it in Bodycam.",
                new AcceptableValueRange<float>(0f, 45f)));

            CapDegrees = cfg.Bind(S_COUPLING, "Hard cap (deg)", 90f, new ConfigDescription(
                "[spec] Maximum gun-to-body angle a hard flick can reach.",
                new AcceptableValueRange<float>(5f, 179f)));

            SpringK = cfg.Bind(S_COUPLING, "Spring constant k (1/s)", 5f, new ConfigDescription(
                "[spec-derived] Body converges on the gun as exp(-k*t). k=5 gives ~99% in 1s, " +
                "which is the measured Bodycam convergence.",
                new AcceptableValueRange<float>(0.5f, 30f)));

            PushFactor = cfg.Bind(S_COUPLING, "Push factor", 0.5f, new ConfigDescription(
                "Past the cone, this fraction of the mouse delta also drives the body.\n" +
                "\n" +
                "DO NOT SET THIS TO 1.0. The spec's pseudocode implies 1.0, but at 1.0 the cone " +
                "stops being soft: above the threshold the body receives the whole mouse delta, " +
                "the offset stops growing, and a hard flick saturates at the cone size instead " +
                "of reaching the measured ~90 degrees. See docs/07-FINDINGS.md F3.\n" +
                "\n" +
                "The offset a flick reaches is roughly  speed * (1 - push) / k.\n" +
                "At push 0.5 and k 5, a 900 deg/s flick reaches ~90 deg (the measured cap) while " +
                "a 60 deg/s steady turn still parks at the cone. Raise it for a tighter, more " +
                "clamped feel; lower it for a looser one.",
                new AcceptableValueRange<float>(0f, 1f)));

            AimCoupling = cfg.Bind(S_COUPLING, "Coupling while aiming", 1f, new ConfigDescription(
                "[docs/05-OPEN-QUESTIONS.md Q2 - PICK ONE DELIBERATELY] 1.0 matches the Bodycam " +
                "measurement (coupling identical hip and shouldered). Lower values reduce free " +
                "aim down sights, which is a deliberate divergence, defensible for Tarkov's " +
                "engagement ranges. Do NOT leave this at lualeet's 0.0 default by accident - " +
                "that means no free aim at all while aiming.",
                new AcceptableValueRange<float>(0f, 1f)));

            DisengageBoost = cfg.Bind(S_COUPLING, "Disengage spring boost", 4f, new ConfigDescription(
                "How much faster the body catches up while free aim is switching off (gun going " +
                "down, reload, sprint). Stops the offset lingering through an animation.",
                new AcceptableValueRange<float>(0f, 20f)));

            // -- pivot --
            Hinge = cfg.Bind(S_PIVOT, "Hinge mode", HingeMode.LegacyEuler, new ConfigDescription(
                "AROUND GRIP is the one that actually hinges. Pick it if the gun does not swing " +
                "about your hand.\n" +
                "\n" +
                "LEGACY EULER is the original call and still the default, because it is what the " +
                "early builds felt right with and changing a default underneath you has gone badly " +
                "before. But it CANNOT rotate about a point, and that is arithmetic rather than " +
                "opinion. TransformTools.LocalRotateAround displaces the weapon by\n" +
                "\n" +
                "    (I - q) * c\n" +
                "\n" +
                "where a real rotation about the point c needs\n" +
                "\n" +
                "    R * (I - q) * c        (R = the weapon's own local rotation)\n" +
                "\n" +
                "Right length, wrong direction. The lever arm comes out in the PARENT's frame " +
                "instead of the weapon's, so the horizontal part of the swing leaks away into " +
                "pitch and into the barrel axis. No pivot dial can correct that - the dial sets " +
                "c, and c is not the part that is wrong.\n" +
                "\n" +
                "AROUND GRIP does the honest thing instead: rotate the position about the pivot " +
                "and rotate the orientation by the same amount. The muzzle swings one way, the " +
                "buttstock swings the other, and the HUD prints the lever arm in metres so you " +
                "can see it is non-zero. Turn on \"Take the pivot from the weapon itself\" with " +
                "it and the hinge lands on the weapon's own hands centre. See F40.",
                null));

            GripRight = cfg.Bind(S_PIVOT, "Grip: right of the eye (m)", 0.14f, new ConfigDescription(
                "AROUND GRIP mode. How far your firing hand sits to the RIGHT of the camera, in " +
                "metres. Negative for a left-handed hold.",
                new AcceptableValueRange<float>(-1f, 1f)));

            GripUp = cfg.Bind(S_PIVOT, "Grip: above the eye (m)", -0.24f, new ConfigDescription(
                "AROUND GRIP mode. How far ABOVE the camera the hand sits. Negative is below, which " +
                "is where a hand actually is - about most of a forearm down.",
                new AcceptableValueRange<float>(-1f, 1f)));

            GripForward = cfg.Bind(S_PIVOT, "Grip: forward of the eye (m)", 0.16f, new ConfigDescription(
                "AROUND GRIP mode. How far FORWARD of the camera the hand sits. Further out means " +
                "the muzzle sweeps a longer arc for the same angle.",
                new AcceptableValueRange<float>(-1f, 1f)));

            PivotDistance = cfg.Bind(S_PIVOT, "Pivot distance (m)", -0.15f, new ConfigDescription(
                "LEGACY EULER mode. Where the weapon hinges, as a distance along its local up axis. " +
                "This is the single dial the earliest builds had, and -0.15 is the value that put " +
                "the hinge on the pistol grip - the firing hand.\n" +
                "\n" +
                "Negative moves the pivot down the weapon toward the grip; positive moves it up and " +
                "out. Larger magnitudes give the muzzle a longer arc for the same angle.",
                new AcceptableValueRange<float>(-1f, 1f)));

            PivotOffsetX = cfg.Bind(S_PIVOT, "Pivot offset X (m)", 0f, new ConfigDescription(
                "Nudges the hinge off the up axis, in the weapon root's local space. Added on top " +
                "of the distance above, and on top of the weapon's own centre when that is in use.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

            PivotOffsetY = cfg.Bind(S_PIVOT, "Pivot offset Y (m)", 0f, new ConfigDescription(
                "The same, on the second axis.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

            PivotOffsetZ = cfg.Bind(S_PIVOT, "Pivot offset Z (m)", 0f, new ConfigDescription(
                "The same, on the third axis. On most weapons this is the one that walks the hinge " +
                "along the barrel, which is the direction that decides how much the buttstock " +
                "swings when you turn.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

            InvertGamePitch = cfg.Bind(S_PIVOT, "Invert game pitch", true,
                "Normalises which way pitch counts, at the point the bearing is READ from the game " +
                "rather than at the point the weapon is drawn.\n" +
                "\n" +
                "This is the fix for horizontal and vertical behaving in opposite senses. The " +
                "Invert pitch toggle below only ever rotated the weapon, so flipping it fixed the " +
                "gun's picture while the body bearing and the camera kept the old sign - one axis " +
                "right, the other backwards. This one flips the sign for the whole loop at once, " +
                "and undoes it again on write-back, so every part agrees.\n" +
                "\n" +
                "If vertical is now inverted the OTHER way, flip this. See docs/07-FINDINGS.md F24.");

            ApplyWeaponOffset = cfg.Bind(S_PIVOT, "Apply weapon offset", true,
                "Rotate the weapon by the offset. Turn OFF to isolate a problem: with this off " +
                "and the camera apply on, only the view moves, so you can tell which of the two " +
                "is misbehaving.");

            ApplyCameraOffset = cfg.Bind(S_PIVOT, "Apply camera offset", true,
                "Compensate mode only: rotate the camera back by the offset. Turn OFF and the " +
                "mod behaves like Reactive - the gun trails the view instead of leading it - " +
                "which is a useful A/B when something looks wrong.");

            InvertYaw = cfg.Bind(S_PIVOT, "Invert yaw", false,
                "Flip if the weapon swings the wrong way horizontally. Expect to need one of these.");
            InvertPitch = cfg.Bind(S_PIVOT, "Invert pitch", false,
                "Cosmetic only - it rotates the WEAPON and nothing else, so using it to correct a " +
                "sign leaves the body bearing disagreeing with the gun. For a genuine vertical " +
                "inversion use \"Invert game pitch\" above instead.");
            SwapAxes = cfg.Bind(S_PIVOT, "Swap axes", false,
                "Flip if horizontal mouse movement tilts the weapon and vertical movement pans it.");

            // -- stance --
            StanceGateEnabled = cfg.Bind(S_STANCE, "Gate on weapon ready", true,
                "[docs/04-DECISIONS.md D6] Free aim only when the weapon is up. Off means always on.");
            StanceKey = cfg.Bind(S_STANCE, "Stance key", new KeyboardShortcut(KeyCode.M),
                "Raises and lowers the weapon.\n\n" +
                "Defaulted to M because almost nothing near WASD is free in stock Tarkov: X is Prone, Z drops your backpack, C crouches, V is weapon mounting, B is fire mode, T and R and F are all taken. If you rebind this, watch the startup log - the mod compares its own hotkeys against Tarkov's control file and warns on a clash (F47).");
            StanceHoldToReady = cfg.Bind(S_STANCE, "Hold to ready", false,
                "[Q3] Off = press to toggle (recommended). On = weapon drops on key release.");
            GateSpeed = cfg.Bind(S_STANCE, "Gate transition speed (1/s)", 4f, new ConfigDescription(
                "How fast free aim fades in and out across a stance change.",
                new AcceptableValueRange<float>(0.5f, 20f)));

            LoweredPoseEnabled = cfg.Bind(S_STANCE, "Apply lowered pose", true,
                "Visually lower the weapon in the down stance. Off leaves the pose alone and only " +
                "gates free aim.");
            LoweredPosX = cfg.Bind(S_STANCE, "Lowered position X (m)", 0f, new ConfigDescription(
                "Weapon-down pose, sideways. UNMEASURED - zero means no visual lowering until you " +
                "set it. Tune by eye against Bodycam (docs/07-FINDINGS.md F16).",
                new AcceptableValueRange<float>(-1f, 1f)));
            LoweredPosY = cfg.Bind(S_STANCE, "Lowered position Y (m)", 0f, new ConfigDescription(
                "Weapon-down pose, vertical. Negative drops the weapon.",
                new AcceptableValueRange<float>(-1f, 1f)));
            LoweredPosZ = cfg.Bind(S_STANCE, "Lowered position Z (m)", 0f, new ConfigDescription(
                "Weapon-down pose, along the third axis.",
                new AcceptableValueRange<float>(-1f, 1f)));

            LoweredRotX = cfg.Bind(S_STANCE, "Lowered rotation X (deg)", 0f, new ConfigDescription(
                "Weapon-down pose, pitch. UNMEASURED - see above.",
                new AcceptableValueRange<float>(-180f, 180f)));
            LoweredRotY = cfg.Bind(S_STANCE, "Lowered rotation Y (deg)", 0f, new ConfigDescription(
                "Weapon-down pose, yaw.",
                new AcceptableValueRange<float>(-180f, 180f)));
            LoweredRotZ = cfg.Bind(S_STANCE, "Lowered rotation Z (deg)", 0f, new ConfigDescription(
                "Weapon-down pose, roll.",
                new AcceptableValueRange<float>(-180f, 180f)));

            LoweredLerpSpeed = cfg.Bind(S_STANCE, "Lowered pose lerp speed", 6f, new ConfigDescription(
                "How fast the weapon moves into the lowered pose, per second. 6/s is roughly a " +
                "sixth of a second to settle - fast enough not to feel sluggish, slow enough to " +
                "read as a movement rather than a snap.",
                new AcceptableValueRange<float>(0.5f, 20f)));

            SuspendOnSprint = cfg.Bind(S_STANCE, "Suspend while sprinting", true,
                "[spec section 4] Tarkov lowers the weapon natively when sprinting, so leaving " +
                "free aim on here means the offset fights the sprint animation. Turn off if the " +
                "sprint state cannot be found on your build - the startup log says whether it was.");

            SuspendOnAnimation = cfg.Bind(S_STANCE, "Suspend during reload and item use", true,
                "[spec section 4] Reload, heal, swap and melee suspend the offset rather than " +
                "fight the animation. Item use, throwables and melee are detected by the hands " +
                "controller's type, which is reliable. A magazine reload needs a flag whose name " +
                "is not certain - check the startup log for whether it resolved.");

            SuspendOnStationary = cfg.Bind(S_STANCE, "Suspend on mounted weapons", true,
                "Mounted weapons and ladders, where the camera is not yours to move.");

            ReadyPoseEnabled = cfg.Bind(S_STANCE, "Apply ready pose", false,
                "[docs/07-FINDINGS.md F12] In Bodycam the weapon is NOT shouldered at rest: the " +
                "firing hand is lowered, the gun is held low, and the buttstock sits behind the " +
                "arm rather than in the shoulder pocket. Tarkov's weapon-up is already shouldered, " +
                "so this offsets away from it. Off by default - the values below are zero, because " +
                "they have to be found by eye and a guess would just be noise. Fades out as you aim.");

            ReadyPosX = cfg.Bind(S_STANCE, "Ready position X (m)", 0f, new ConfigDescription(
                "Un-shouldered ready stance, sideways (docs/07-FINDINGS.md F12.2). UNMEASURED. The " +
                "target is buttstock behind the arm near the hip, firing hand lowered - not the " +
                "shoulder pocket. Set cone to 25 while you tune, then put it back.",
                new AcceptableValueRange<float>(-1f, 1f)));
            ReadyPosY = cfg.Bind(S_STANCE, "Ready position Y (m)", 0f, new ConfigDescription(
                "Ready stance, vertical. Negative drops the weapon.",
                new AcceptableValueRange<float>(-1f, 1f)));
            ReadyPosZ = cfg.Bind(S_STANCE, "Ready position Z (m)", 0f, new ConfigDescription(
                "Ready stance, along the third axis.",
                new AcceptableValueRange<float>(-1f, 1f)));

            ReadyRotX = cfg.Bind(S_STANCE, "Ready rotation X (deg)", 0f, new ConfigDescription(
                "Ready stance, pitch. UNMEASURED - see above.",
                new AcceptableValueRange<float>(-180f, 180f)));
            ReadyRotY = cfg.Bind(S_STANCE, "Ready rotation Y (deg)", 0f, new ConfigDescription(
                "Ready stance, yaw.",
                new AcceptableValueRange<float>(-180f, 180f)));
            ReadyRotZ = cfg.Bind(S_STANCE, "Ready rotation Z (deg)", 0f, new ConfigDescription(
                "Ready stance, roll.",
                new AcceptableValueRange<float>(-180f, 180f)));

            // -- recoil --
            DecoupleRecoil = cfg.Bind(S_RECOIL, "Decouple recoil", false,
                "[docs/02-PLAN.md step 07 - NOT IMPLEMENTED YET] Stops the camera being dragged " +
                "up by the weapon's recoil. Leave off until the core coupling is tuned.");
            HipCameraFollow = cfg.Bind(S_RECOIL, "Hip camera follow", 0f, new ConfigDescription(
                "[spec] Fraction of the weapon's recoil pattern the camera follows from the hip. " +
                "Spec says 0 - the camera shakes independently instead.",
                new AcceptableValueRange<float>(0f, 1f)));
            AimCameraFollow = cfg.Bind(S_RECOIL, "Shouldered camera follow", 0.6f, new ConfigDescription(
                "[spec] 0.5-0.7 measured.", new AcceptableValueRange<float>(0f, 1f)));

            RecoilMovesGun = cfg.Bind(S_RECOIL, "Recoil moves the gun", false,
                "[docs/07-FINDINGS.md F12.3] Adds the weapon's own recoil to the GUN bearing, so " +
                "the gun climbs while the body holds its stance, then returns to where it " +
                "started. The rise and the return are Tarkov's own - this only routes them to the " +
                "gun instead of the camera - so there is no second spring to tune.\n" +
                "\n" +
                "Off by default until the axis mapping is confirmed: watch the HUD's recoil row " +
                "while firing to see which component moves, then set the scale below.");

            RecoilScaleYaw = cfg.Bind(S_RECOIL, "Recoil to gun scale - yaw", 1f, new ConfigDescription(
                "How much of the weapon's recoil reaches the gun bearing sideways. Negative flips " +
                "the direction, zero disables it. Start at 1 and watch the HUD.",
                new AcceptableValueRange<float>(-5f, 5f)));

            RecoilScalePitch = cfg.Bind(S_RECOIL, "Recoil to gun scale - pitch", 1f, new ConfigDescription(
                "The same, vertically. Negative flips the direction, zero disables it.",
                new AcceptableValueRange<float>(-5f, 5f)));

            RecoilSwapAxes = cfg.Bind(S_RECOIL, "Recoil swap axes", false,
                "Flip if the recoil climbs sideways instead of up. The Vector3 the game exposes " +
                "is a hand rotation, and which component is pitch was never verified.");

            // -- body --
            ArmDrainEnabled = cfg.Bind(S_BODY, "Arms tire while the weapon is up", true,
                "Holding a rifle up is work, and Tarkov already models it: PhysicalBase carries a " +
                "HandsStamina pool separate from the main one, which drives sway and the exhausted " +
                "state. This drains THAT pool while the weapon is raised, so the game's own " +
                "consequences follow rather than a second meter being invented alongside it.\n" +
                "\n" +
                "Lower the weapon and it stops draining and the game restores it at its own rate. " +
                "Which is the point: low ready becomes worth using.");

            ArmDrainRate = cfg.Bind(S_BODY, "Arm drain per second", 1.2f, new ConfigDescription(
                "Hands-stamina units taken per second while the weapon is up. The pool is on the " +
                "same scale as the main stamina bar, so 1.2/s is a slow burn you notice over a " +
                "long hold rather than a timer. Raise it until lowering the weapon feels like a " +
                "decision rather than a courtesy.",
                new AcceptableValueRange<float>(0f, 20f)));

            ArmDrainAimedMultiplier = cfg.Bind(S_BODY, "Extra drain while shouldered", 1.6f, new ConfigDescription(
                "Multiplier on the rate above while the weapon is actually in the shoulder. " +
                "Holding the sights up costs more than holding the weapon ready, which is what " +
                "makes the ready position worth returning to. 1.0 removes the distinction.",
                new AcceptableValueRange<float>(0.25f, 4f)));

            PivotFromWeapon = cfg.Bind(S_PIVOT, "Take the pivot from the weapon itself", false,
                "Ignores the pivot dials above and uses the rotation centre the WEAPON carries.\n" +
                "\n" +
                "Every weapon in Tarkov ships two of these - a centre for when the buttstock is " +
                "braced against your shoulder, and one for when it is not - in exactly the space " +
                "the pivot dial uses. The game picks between them itself. The braced one is what " +
                "has been in use, which is why swinging has felt like the stock was glued to your " +
                "shoulder.\n" +
                "\n" +
                "On, the gun turns about the hands instead, on every weapon, with no tuning. This " +
                "is the fix for the buttstock feel. Turn it on first and swing before touching " +
                "anything else in this section.");

            PivotStockWeight = cfg.Bind(S_PIVOT, "Blend toward the shouldered centre", 0f, new ConfigDescription(
                "0 turns about the hands always - the right hand is the axis, which is what was " +
                "asked for. 1 uses the braced centre, which is the stock Tarkov feel. In between " +
                "blends the two.\n" +
                "\n" +
                "It does not stop at either end. NEGATIVE walks the pivot on past the hands centre, " +
                "along the same line, which points forward down the weapon - so if the authored " +
                "hands centre still sits too far back for you, -0.5 pushes the hinge toward the " +
                "muzzle. Above 1 goes the other way, behind the shoulder.\n" +
                "\n" +
                "Only does anything when the switch above is on.",
                new AcceptableValueRange<float>(-2f, 2f)));

            ShoulderGive = cfg.Bind(S_PIVOT, "Shoulder give (m)", 0f, new ConfigDescription(
                "The shoulder pocket is flesh, not a bolt. Under a hard swing the weapon slides a " +
                "centimetre or two in the pocket and comes back, instead of being welded in place.\n" +
                "\n" +
                "This is a TRANSLATION on top of the hinge, not another rotation - the gun still " +
                "turns about the point the weapon itself specifies. It runs opposite the swing, " +
                "because the weapon's own mass is what loads the pocket: swing right and the stock " +
                "is left behind for a moment.\n" +
                "\n" +
                "Scaled by how shouldered you are, since a weapon at low ready has no pocket to " +
                "give. Try 0.02 - two centimetres. Negative slides it the other way.",
                new AcceptableValueRange<float>(-0.1f, 0.1f)));

            GunRollEnabled = cfg.Bind(S_PIVOT, "Gun rolls as it swings", false,
                "Cants the weapon while it swings, the way your wrist rolls when the support hand " +
                "leads the gun around. Turn right and it rolls clockwise; turn left and it rolls " +
                "counter-clockwise.\n" +
                "\n" +
                "It is proportional from zero while you are aiming, because the weapon is braced " +
                "against the shoulder and every bit of swing twists it. At low ready it only shows " +
                "up once the gun is pushed past the cone - the weapon is hanging off your hands " +
                "with slack in the wrists, and nothing twists until the swing takes up that slack.\n" +
                "\n" +
                "Off by default. Turn it on and watch the gun before deciding the numbers.");

            GunRollAimed = cfg.Bind(S_PIVOT, "Gun roll aimed (deg)", 8f, new ConfigDescription(
                "How far the weapon cants at the hard cap while shouldered. Negative flips the " +
                "direction, same as the invert switch.",
                new AcceptableValueRange<float>(-45f, 45f)));

            GunRollReady = cfg.Bind(S_PIVOT, "Gun roll at low ready (deg)", 4f, new ConfigDescription(
                "The same, at low ready, where it only begins past the cone. Smaller than the aimed " +
                "figure is the point - a braced weapon twists more than a hanging one.",
                new AcceptableValueRange<float>(-45f, 45f)));

            InvertGunRoll = cfg.Bind(S_PIVOT, "Invert gun roll", false,
                "If it cants the wrong way. Which sign means clockwise depends on the weapon's own " +
                "axes, and that has needed an experiment on every version so far.");

            GunRollAboutView = cfg.Bind(S_PIVOT, "Roll about the view instead of the barrel", false,
                "Off, the weapon rolls about its own forward axis - a true cant about the barrel.\n" +
                "\n" +
                "On, it rolls about the axis you are looking down. That one cannot be wrong, but at " +
                "low ready, with the muzzle at the floor, it reads as a twist rather than a cant. " +
                "Use it only if the barrel axis turns out to be wrong on some weapon.");

            BodyLeanAimed = cfg.Bind(S_BODY, "Body lean aimed (deg)", 0f, new ConfigDescription(
                "The counterbalance. Swing the weapon right and your torso leans LEFT - the mass " +
                "goes one way and the spine goes the other to keep the weight over your feet. On a " +
                "bodycam it reads as the horizon tipping through every turn.\n" +
                "\n" +
                "This is how far the view tips at the hard cap while shouldered. ZERO IS OFF - " +
                "there is no separate switch. Start at 4 and go DOWN if anything: it is the " +
                "horizon, and the eye catches a tilted horizon far faster than a tilted gun. Ten " +
                "degrees is not a shooter bracing, it is a shooter falling over.",
                new AcceptableValueRange<float>(-30f, 30f)));

            BodyLeanReady = cfg.Bind(S_BODY, "Body lean at low ready (deg)", 0f, new ConfigDescription(
                "The same, at low ready, where it only starts once the swing has taken up the " +
                "slack in your arms. Smaller again: a weapon held loose does not load the spine " +
                "the way a shouldered one does.",
                new AcceptableValueRange<float>(-30f, 30f)));

            InvertBodyLean = cfg.Bind(S_BODY, "Invert body lean", false,
                "If the horizon tips the wrong way. Which screen direction opposes a right-hand " +
                "swing depends on the camera's handedness, and guessing that from first principles " +
                "has a poor record here - look at it and flip this if it is backwards.");

            GunLeansWithBody = cfg.Bind(S_BODY, "Gun leans with the body", true,
                "Carries the weapon along with the lean, about the eye, so the gun stays welded to " +
                "you instead of hanging level while the horizon tips around it.\n" +
                "\n" +
                "Off, only the view leans and the weapon keeps its own attitude - which looks wrong " +
                "for a rifle in your hands, but is worth a look to see what the lean is doing on " +
                "its own.\n" +
                "\n" +
                "Skipped automatically when the weapon already hangs off the camera in the " +
                "hierarchy, since Unity would then apply the lean twice.");

            DofEnabled = cfg.Bind(S_BODY, "Gun blur: focus past the gun", true,
                "The gun goes soft while your eyes are downrange - the focused-at-infinity look.\n" +
                "\n" +
                "This drives the game's OWN depth of field, the legacy image effect that Tarkov " +
                "already runs and already re-focuses every time your field of view changes. It is " +
                "depth-based, which is what makes it look right: the blur is a gradient down the " +
                "weapon, heaviest at the receiver nearest your eye and easing off toward the front " +
                "sight half a metre further out. No per-object trick can do that - it would blur " +
                "the whole gun by one flat amount.\n" +
                "\n" +
                "The focus distance is not a setting: it is a raycast down the middle of the " +
                "screen, so look at a wall two metres off and the gun sharpens up, look down a " +
                "street and it melts. Deliberately the VIEW's direction, not the gun's - free aim " +
                "means the gun is often pointing somewhere your eye is not, and the eye focuses.\n" +
                "\n" +
                "Every value it touches is captured first and handed back when it stops, so it " +
                "cannot follow you out of the raid.");

            DofBlurSize = cfg.Bind(S_BODY, "Gun blur amount", 2.5f, new ConfigDescription(
                "How soft the gun gets, as a blur radius. This is the strength dial and it is the " +
                "unambiguous one: 0 is no blur at all, larger is blurrier. Around 2 to 3 matches " +
                "the reference footage; past 5 it turns to soup.",
                new AcceptableValueRange<float>(0f, 8f)));

            DofBand = cfg.Bind(S_BODY, "Gun blur: sharp zone", 0.05f, new ConfigDescription(
                "How deep the in-focus zone is around whatever you are looking at. Smaller is a " +
                "tighter plane of focus, so the gun falls out of it sooner. Raise it if the world " +
                "itself starts looking soft.",
                new AcceptableValueRange<float>(0f, 1f)));

            DofMaxDistance = cfg.Bind(S_BODY, "Gun blur: max focus distance (m)", 120f, new ConfigDescription(
                "Where to focus when the ray hits nothing - open sky. This is your infinity, and " +
                "where the gun is at its softest.",
                new AcceptableValueRange<float>(10f, 500f)));

            DofOnlyAiming = cfg.Bind(S_BODY, "Gun blur only while aiming", false,
                "OFF by default, and that is the honest setting: this is about where your EYES are " +
                "focused, not where the gun is. Your eyes are downrange whether the weapon is " +
                "shouldered or hanging at low ready, so the gun should be soft either way.\n" +
                "\n" +
                "At low ready it gets softer on its own, with nothing extra to configure - the " +
                "weapon is nearer the eye down there, so it sits further from the plane of focus " +
                "and the depth effect blurs it harder. That falloff is the effect doing physics, " +
                "not a setting.\n" +
                "\n" +
                "Turn this on if you want the blur to fade in only as you shoulder the weapon.");

            DofStrength = cfg.Bind(S_BODY, "Gun blur strength", 1f, new ConfigDescription(
                "Scales the blur amount above. Back this off for something present but quieter, " +
                "without losing the shape of the falloff.",
                new AcceptableValueRange<float>(0f, 1f)));

            SightAlpha = cfg.Bind(S_BODY, "Sight transparency", 0f, new ConfigDescription(
                "Fades the optic's BODY out while you aim, so the sight stops being a brick in " +
                "front of the eye that is not looking through it. The lens and reticle are left " +
                "alone - they are the part you look through.\n" +
                "\n" +
                "0 is off. Try 0.5 first. This is a rewrite of the version that turned the sight " +
                "black: that one assumed the albedo lived in _MainTex, EFT's shaders do not keep " +
                "it there, and an untextured blend shader draws flat black. This one asks the " +
                "shader where its textures actually are, and if it cannot find one it leaves the " +
                "material alone rather than blacking it out.\n" +
                "\n" +
                "Whether a build has any shader that blends is not guaranteed. If it does not, you " +
                "get one line in the log and nothing changes. Turn on the log below to see what " +
                "was found.",
                new AcceptableValueRange<float>(0f, 0.95f)));

            LogSightMaterials = cfg.Bind(S_BODY, "Log the sight materials", false,
                "DIAGNOSTIC. Writes every material on the current sight's body, its shader name, " +
                "and every texture property that shader declares, once per raid.\n" +
                "\n" +
                "This is what makes transparency fixable rather than guessable - it says out loud " +
                "where EFT keeps its albedo instead of assuming.");

            OpticBloom = cfg.Bind(S_OPTIC, "Optic: bloom", 0f, new ConfigDescription(
                "How hard light blooms INSIDE the scope image. Nothing outside the tube.\n" +
                "\n" +
                "0 is off. 1 to 3 is a lens catching the light. The ceiling is 30 because PPv2 " +
                "puts no upper bound on this at all, and the old cap of 5 was mine, not the " +
                "engine's - a bright lamp should be able to blow out.\n" +
                "\n" +
                "IF TURNING THIS UP JUST HAZES THE WHOLE TUBE, the dial you actually want is " +
                "'Optic: bloom threshold'. Intensity decides how hard things bloom; threshold " +
                "decides WHAT blooms. Raise the threshold so only the lamp qualifies, then this " +
                "can go as high as you like and the rest of the image stays put.",
                new AcceptableValueRange<float>(0f, 30f)));

            OpticBloomThreshold = cfg.Bind(S_OPTIC, "Optic: bloom threshold", 0.9f, new ConfigDescription(
                "How bright a pixel has to be before it blooms at all. This is the dial that " +
                "makes a LAMP glow instead of the whole picture glowing.\n" +
                "\n" +
                "LOWER and more of the scene blooms, which reads as a dirty or cheap lens. " +
                "HIGHER and only genuinely bright things do, which is what lets you push " +
                "'Optic: bloom' up hard without washing the tube out. Around 1.2 to 2 keeps it " +
                "to lamps, muzzle flash and sky.\n" +
                "\n" +
                "Gamma-space, so it lines up with how bright things look rather than with the " +
                "raw HDR numbers.",
                new AcceptableValueRange<float>(0f, 5f)));

            OpticBloomSpread = cfg.Bind(S_OPTIC, "Optic: bloom spread", 7f, new ConfigDescription(
                "How far the glow reaches out from the bright thing. Low is a tight halo right " +
                "at the source; high is a wide bloom across the tube. 10 is the engine's maximum " +
                "and is what a big light seen through glass looks like.\n" +
                "\n" +
                "This changes an internal iteration count, so move it in whole steps and do not " +
                "sweep it while looking at something bright.",
                new AcceptableValueRange<float>(1f, 10f)));

            OpticForceHdr = cfg.Bind(S_OPTIC, "Optic: allow bright light in the scope", true,
                "Leave this on unless the scope image starts looking wrong.\n" +
                "\n" +
                "Tarkov renders the scope into an ARGB32 texture unless the optic camera allows " +
                "HDR, and ARGB32 stops at white. In that buffer a bright lamp and a white wall " +
                "are the SAME NUMBER, so bloom has nothing to pick out and a bloom threshold " +
                "above 1.0 selects nothing at all.\n" +
                "\n" +
                "On, and the scope camera is switched to HDR and its texture rebuilt through the " +
                "game's own SetResolution, so light can be brighter than white and the lamp can " +
                "actually blow out. Costs a little memory. Put back when you turn the glass " +
                "effects off. The HUD says which format the scope is in either way.");

            OpticDirt = cfg.Bind(S_OPTIC, "Optic: lens dirt", 0f, new ConfigDescription(
                "The one that most makes it read as GLASS rather than as a glow.\n" +
                "\n" +
                "It modulates the bloom through Tarkov's own lens-dirt texture, so light does not " +
                "just brighten, it scatters off whatever is on the lens. Inside the tube only.\n" +
                "\n" +
                "IT RIDES ON BLOOM. Dirt with 'Optic: bloom' at 0 has nothing to modulate and can " +
                "show nothing at all, so turn the bloom up first. The HUD says 'no dirt texture' " +
                "if this build ships none, rather than leaving you turning a dial that cannot move.",
                new AcceptableValueRange<float>(0f, 3f)));

            OpticFringe = cfg.Bind(S_OPTIC, "Optic: colour fringing", 0f, new ConfigDescription(
                "Colour separating toward the edge of the scope image, the way real glass does. " +
                "0 is off; 0.2 to 0.5 is plenty. This one reads as a headache long before it " +
                "reads as realism.",
                new AcceptableValueRange<float>(0f, 1f)));

            OpticVignette = cfg.Bind(S_OPTIC, "Optic: rim darkening", 0f, new ConfigDescription(
                "Darkens the image toward the edge of the tube - the ring that says you are " +
                "looking through a cylinder of glass rather than at a floating picture. This is " +
                "the cue you pointed at in the reference footage.\n" +
                "\n" +
                "0 is off. 0.3 to 0.6 is about right.",
                new AcceptableValueRange<float>(0f, 1f)));

            OpticDistortion = cfg.Bind(S_OPTIC, "Optic: edge distortion", 0f, new ConfigDescription(
                "Bows the scope image near the rim, which is the other half of reading as curved " +
                "glass. POSITIVE barrels it outward, NEGATIVE pinches it inward - real optics " +
                "usually pincushion slightly, so try small negatives first.\n" +
                "\n" +
                "0 is off. Stay under about 0.3 either way; past that it looks like a fisheye.",
                new AcceptableValueRange<float>(-1f, 1f)));

            KeepFovWhenAiming = cfg.Bind(S_BODY, "Keep your field of view when aiming", false,
                "Tarkov narrows the view by fifteen degrees the moment the weapon comes up. It is " +
                "meant to read as leaning into the sight, and under free aim it is wrong twice " +
                "over: your head has not moved, and the gun is no longer nailed to the middle of " +
                "the screen for you to lean toward.\n" +
                "\n" +
                "On, the view stays where it was. The zoom is not subtracted - the view is put back " +
                "to the game's own un-aimed value, so it stays correct if BSG ever changes that " +
                "number.\n" +
                "\n" +
                "This replaces the old peripheral-vision switch, which never worked and could not " +
                "have: it wrote CameraManager.AimDeltaFov, a const that nothing in the game reads. " +
                "See docs/07-FINDINGS.md F38.");

            KeepFovWithOptics = cfg.Bind(S_BODY, "Keep your field of view with optics too", false,
                "OFF by default, and it should usually stay off.\n" +
                "\n" +
                "An optic is a different animal from iron sights. Tarkov hands irons and red dots " +
                "your own field of view minus fifteen degrees - that is the shouldering lean, and " +
                "cancelling it is the whole point of the switch above. But it hands a magnifying " +
                "optic a flat thirty-five, and THAT is the magnification. From inside the game's " +
                "own SetFov the two look identical, so one switch cancelling both takes the zoom " +
                "straight off a sniper scope.\n" +
                "\n" +
                "Off, scopes magnify exactly as they always did and only irons and red dots stop " +
                "leaning in. Turn this on only if you want a scope to stop magnifying, which is " +
                "almost certainly not what you want. See docs/07-FINDINGS.md F39.");

            ZoomOnHoldBreath = cfg.Bind(S_BODY, "Zoom in when holding breath", true,
                "Give the zoom back while you hold your breath - now you really are putting your " +
                "eye to the sight and focusing on the target, and it costs stamina to do it.\n" +
                "\n" +
                "It moves to whatever the game itself wanted while aiming, so an optic still goes " +
                "to its own magnification rather than a flat fifteen degrees.\n" +
                "\n" +
                "Only does anything when the switch above is on.");

            ZoomTime = cfg.Bind(S_BODY, "Zoom transition (s)", 0.25f, new ConfigDescription(
                "How long the lean-in takes. The game uses a full second for shouldering; this is " +
                "quicker because holding your breath is a deliberate act, not a posture change.",
                new AcceptableValueRange<float>(0.01f, 2f)));

            // -- debug --
            ShowHud = cfg.Bind(S_DEBUG, "Show HUD", true,
                "On-screen readout of gun bearing, body bearing, offset and mode. " +
                "You will want this for the whole tuning phase.");
            HudKey = cfg.Bind(S_DEBUG, "HUD toggle key", new KeyboardShortcut(KeyCode.F9), "");

            ProbeWriteKey = cfg.Bind(S_DEBUG, "Yaw write probe key", new KeyboardShortcut(KeyCode.F10),
                "ANSWERS THE FEASIBILITY GATE. In raid, press this: it writes " +
                "MovementContext.Yaw += the value below, then re-reads it next frame and logs " +
                "whether the write stuck and whether the view actually moved. If it stuck, " +
                "Intercept mode is viable and docs/02-PLAN.md step 03 is far cheaper than budgeted.");
            ProbeWriteDegrees = cfg.Bind(S_DEBUG, "Yaw write probe amount (deg)", 20f, new ConfigDescription(
                "Large enough to be unmistakable on screen.",
                new AcceptableValueRange<float>(-90f, 90f)));

        }

        public FreeAimState.Tuning Snapshot()
        {
            return new FreeAimState.Tuning
            {
                Mode = Mode.Value,
                ConeDegrees = ConeDegrees.Value,
                CapDegrees = CapDegrees.Value,
                SpringK = SpringK.Value,
                PushFactor = PushFactor.Value,
                AimCoupling = AimCoupling.Value,
                DisengageBoost = DisengageBoost.Value
            };
        }
    }
}
