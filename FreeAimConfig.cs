using BepInEx.Configuration;
using SPTFreeAim.Core;
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
        public ConfigEntry<Vector3> GripFromEye;
        public ConfigEntry<Vector3> PivotOffset;
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
        public ConfigEntry<Vector3> LoweredPos;
        public ConfigEntry<Vector3> LoweredRot;
        public ConfigEntry<float> LoweredLerpSpeed;
        public ConfigEntry<bool> SuspendOnSprint;
        public ConfigEntry<bool> SuspendOnAnimation;
        public ConfigEntry<bool> SuspendOnStationary;
        public ConfigEntry<bool> ReadyPoseEnabled;
        public ConfigEntry<Vector3> ReadyPos;
        public ConfigEntry<Vector3> ReadyRot;

        // ---- Recoil ------------------------------------------------------
        public ConfigEntry<bool> DecoupleRecoil;
        public ConfigEntry<float> HipCameraFollow;
        public ConfigEntry<float> AimCameraFollow;
        public ConfigEntry<bool> RecoilMovesGun;
        public ConfigEntry<Vector2> RecoilGunScale;
        public ConfigEntry<bool> RecoilSwapAxes;

        // ---- Debug -------------------------------------------------------
        public ConfigEntry<bool> ShowHud;
        public ConfigEntry<KeyboardShortcut> HudKey;
        public ConfigEntry<KeyboardShortcut> ProbeWriteKey;
        public ConfigEntry<float> ProbeWriteDegrees;
        public ConfigEntry<bool> VerboseLogging;

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
            Hinge = cfg.Bind(S_PIVOT, "Hinge mode", HingeMode.AroundGrip, new ConfigDescription(
                "AROUND GRIP is the physical one, and the default. The right hand holds the pistol " +
                "grip, the mouse moves the support hand, the weapon is the rigid link between them, " +
                "so it turns about the grip and the muzzle swings. Yaw about the world vertical, " +
                "pitch about the camera's right - the axes those words actually mean.\n" +
                "\n" +
                "LEGACY EULER is what every build before this one did: hand (pitch, 0, yaw) to " +
                "TransformTools.LocalRotateAround. That method does not take local euler angles - it " +
                "runs the vector through parent.TransformDirection and InverseTransformDirection " +
                "first, so which way the gun turns depends on how it sits relative to its parent, " +
                "and part of the mouse movement can land along the barrel and roll it. Kept only for " +
                "comparison. docs/07-FINDINGS.md F22."));

            GripFromEye = cfg.Bind(S_PIVOT, "Grip from eye (right, up, fwd, m)", new Vector3(0.14f, -0.24f, 0.16f),
                "AROUND GRIP mode. Where your firing hand is, measured from the camera in metres: " +
                "how far to your RIGHT, how far UP (negative = below the eye), and how far FORWARD.\n" +
                "\n" +
                "These are numbers you can picture and check against your own body, which is the " +
                "point - a point in the weapon root's local space is not, and tuning that by eye " +
                "never converged. The defaults are roughly where a right-handed shooter's grip hand " +
                "sits with the weapon up: a hand's width right, most of a forearm below the eye, and " +
                "a little in front.\n" +
                "\n" +
                "Further from the eye means the muzzle sweeps a longer arc for the same angle. All " +
                "three take negatives; flip the first one for a left-handed hold.");

            PivotOffset = cfg.Bind(S_PIVOT, "Pivot offset", new Vector3(0f, 0.1f, 0f),
                "LEGACY EULER mode only, and ignored in Around Grip. " +
                "Where the weapon hinges, as a point in the weapon root's local space.\n" +
                "\n" +
                "NOT the shoulder. docs/02-PLAN.md said to pivot about roughly the shoulder; " +
                "measured in Bodycam the gun hinges about the FIRING HAND - grip and trigger - " +
                "and the buttstock swings away from the body. See docs/07-FINDINGS.md F12.\n" +
                "\n" +
                "The default (0, 0.1, 0) is lualeet's original single-axis value, kept only so " +
                "behaviour does not change until you tune it. To find the grip: set cone to 25 so " +
                "the swing is obvious, then change one component at a time and watch which part of " +
                "the weapon stays still. The component that stops the grip moving is the one you " +
                "want.");

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
                "Flip if the weapon swings the wrong way vertically.");
            SwapAxes = cfg.Bind(S_PIVOT, "Swap axes", false,
                "Flip if horizontal mouse movement tilts the weapon and vertical movement pans it.");

            // -- stance --
            StanceGateEnabled = cfg.Bind(S_STANCE, "Gate on weapon ready", true,
                "[docs/04-DECISIONS.md D6] Free aim only when the weapon is up. Off means always on.");
            StanceKey = cfg.Bind(S_STANCE, "Stance key", new KeyboardShortcut(KeyCode.X),
                "Raises and lowers the weapon.");
            StanceHoldToReady = cfg.Bind(S_STANCE, "Hold to ready", false,
                "[Q3] Off = press to toggle (recommended). On = weapon drops on key release.");
            GateSpeed = cfg.Bind(S_STANCE, "Gate transition speed (1/s)", 4f, new ConfigDescription(
                "How fast free aim fades in and out across a stance change.",
                new AcceptableValueRange<float>(0.5f, 20f)));

            LoweredPoseEnabled = cfg.Bind(S_STANCE, "Apply lowered pose", true,
                "Visually lower the weapon in the down stance. Off leaves the pose alone and only " +
                "gates free aim.");
            LoweredPos = cfg.Bind(S_STANCE, "Lowered position offset", Vector3.zero,
                "Position offset for the weapon-down pose. UNMEASURED - zero means no visual " +
                "lowering until you set it. Tune by eye against Bodycam; there is no source to " +
                "copy a number from (docs/07-FINDINGS.md F16).");
            LoweredRot = cfg.Bind(S_STANCE, "Lowered rotation offset", Vector3.zero,
                "Rotation offset for the weapon-down pose. UNMEASURED - see above.");
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

            ReadyPos = cfg.Bind(S_STANCE, "Ready position offset", Vector3.zero,
                "Position offset for the un-shouldered ready stance (docs/07-FINDINGS.md F12.2). " +
                "UNMEASURED. The target is buttstock behind the arm near the hip, firing hand " +
                "lowered - not the shoulder pocket. Set cone to 25 to make the pose obvious while " +
                "you tune, then put it back.");

            ReadyRot = cfg.Bind(S_STANCE, "Ready rotation offset", Vector3.zero,
                "Rotation offset for the ready stance. UNMEASURED - see above.");

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

            RecoilGunScale = cfg.Bind(S_RECOIL, "Recoil to gun scale (yaw, pitch)", new Vector2(1f, 1f),
                "How much of the weapon's recoil reaches the gun bearing. Negative flips the " +
                "direction. Zero on an axis disables it. Start at (1, 1) and watch the HUD.");

            RecoilSwapAxes = cfg.Bind(S_RECOIL, "Recoil swap axes", false,
                "Flip if the recoil climbs sideways instead of up. The Vector3 the game exposes " +
                "is a hand rotation, and which component is pitch was never verified.");

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
                new AcceptableValueRange<float>(1f, 90f)));

            VerboseLogging = cfg.Bind(S_DEBUG, "Verbose logging", false,
                "Per-frame values to the BepInEx console. Noisy; for short captures only.");
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
