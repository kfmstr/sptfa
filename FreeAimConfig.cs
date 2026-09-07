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
        public ConfigEntry<float> PivotDistance;
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

        // ---- Recoil ------------------------------------------------------
        public ConfigEntry<bool> DecoupleRecoil;
        public ConfigEntry<float> HipCameraFollow;
        public ConfigEntry<float> AimCameraFollow;

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

            Mode = cfg.Bind(S_MAIN, "Drive mode", DriveMode.Compensate, new ConfigDescription(
                "Compensate: no input interception needed. Start here.\n" +
                "Intercept: the planned design. Needs MovementContext.Yaw/.Pitch writes to land - test with the write probe first.\n" +
                "Reactive: fallback, camera leads and the gun trails.\n" +
                "Disabled: resolve and log only."));

            ToggleKey = cfg.Bind(S_MAIN, "Master toggle key",
                new KeyboardShortcut(KeyCode.F7),
                "Turns the whole effect on and off in raid, for side-by-side comparison.");

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
            PivotDistance = cfg.Bind(S_PIVOT, "Pivot distance", 0.1f, new ConfigDescription(
                "How far back along the weapon the rotation pivot sits, so the gun swings about " +
                "roughly the shoulder rather than spinning about its middle. lualeet's default " +
                "is 0.1; Realism's mounting uses 0.75. Tune by eye.",
                new AcceptableValueRange<float>(-2f, 2f)));

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
            LoweredPos = cfg.Bind(S_STANCE, "Lowered position offset", new Vector3(0.2f, 0.025f, 0.1f),
                "Realism's rifle patrol-stance values, as a starting point.");
            LoweredRot = cfg.Bind(S_STANCE, "Lowered rotation offset", new Vector3(0.05f, -0.05f, -0.5f),
                "Realism's rifle patrol-stance values, as a starting point.");
            LoweredLerpSpeed = cfg.Bind(S_STANCE, "Lowered pose lerp speed", 5.5f, new ConfigDescription(
                "Realism uses 5.5/s.", new AcceptableValueRange<float>(0.5f, 20f)));

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
