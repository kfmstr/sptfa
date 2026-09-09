using BepInEx.Configuration;
using SPTFreeAim.Compat;
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

        // ---- Anchors (replaces the single pivot) --------------------------
        public ConfigEntry<bool> UseMeasuredGeometry;
        public ConfigEntry<bool> TurnAboutMeasuredBore;
        public ConfigEntry<Vector3> AnchorGrip;
        public ConfigEntry<BoreAxis> BoreAxisChoice;
        public ConfigEntry<bool> BoreAxisInvert;
        public ConfigEntry<float> StockBehindGrip;
        public ConfigEntry<float> LeftHandAheadOfGrip;
        public ConfigEntry<float> AnchorLeeway;
        public ConfigEntry<float> InwardConeScale;
        public ConfigEntry<bool> StrongSideRight;

        // ---- Cant --------------------------------------------------------
        public ConfigEntry<bool> CantEnabled;
        public ConfigEntry<float> CantAngle;
        public ConfigEntry<float> CantSpeed;

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
        public ConfigEntry<KeyboardShortcut> HighReadyKey;
        public ConfigEntry<bool> HighReadyEnabled;
        public ConfigEntry<Vector3> HighReadyPos;
        public ConfigEntry<Vector3> HighReadyRot;
        public ConfigEntry<float> CouplingLowReady;
        public ConfigEntry<float> CouplingHighReady;
        public ConfigEntry<Vector3> ReadyPos;
        public ConfigEntry<Vector3> ReadyRot;

        // ---- Recoil ------------------------------------------------------
        public ConfigEntry<bool> DecoupleRecoil;
        public ConfigEntry<float> HipCameraFollow;
        public ConfigEntry<float> AimCameraFollow;
        public ConfigEntry<bool> RecoilMovesGun;
        public ConfigEntry<Vector2> RecoilGunScale;
        public ConfigEntry<bool> RecoilSwapAxes;

        // ---- Stance consequences -----------------------------------------
        public ConfigEntry<bool> StanceStaminaEnabled;
        public ConfigEntry<float> HandsRecoveryShouldered;
        public ConfigEntry<float> HandsRecoveryLowReady;
        public ConfigEntry<float> HandsRecoveryDown;
        public ConfigEntry<bool> AdsSpeedFromWeight;
        public ConfigEntry<float> AdsWeightReference;
        public ConfigEntry<float> AdsWeightStrength;

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

            // -- anchors --
            UseMeasuredGeometry = cfg.Bind(S_PIVOT, "Measure the weapon", true,
                "Read the bore, grip and buttpad off the weapon in your hands instead of using the " +
                "numbers below.\n" +
                "\n" +
                "Every EFT weapon carries two transforms that define its sight line, mod_align_rear " +
                "and mod_align_front. Rear to front IS the bore, on this weapon, with these " +
                "attachments. There is nothing to guess and nothing to re-tune per gun.\n" +
                "\n" +
                "Falls back per value, not all at once: a pistol has a real grip and sight line but " +
                "no buttstock, so the stock distance below stands in for that one alone. The startup " +
                "log and the HUD both say what was found. Turn this off only to override it by hand.");

            TurnAboutMeasuredBore = cfg.Bind(S_PIVOT, "Turn about the measured bore", false,
                "OFF is the mapping that matches Bodycam. Leave it off unless you are testing.\n" +
                "\n" +
                "Off applies yaw and pitch to the weapon root's local Z and X - lualeet's mapping, " +
                "unchanged from 514b815 to 42b74a5, and the motion you approved.\n" +
                "\n" +
                "On builds the turn axes from the measured bore instead, so they are perpendicular " +
                "to the barrel by construction. That sounds strictly better and the git history says " +
                "otherwise: the raw axes had been producing the right motion for seven commits before " +
                "I replaced them on a theory. Kept because it may be the better answer on a weapon " +
                "whose root is oriented oddly, but it does not get to be the default again without " +
                "someone watching the gun move. See docs/07-FINDINGS.md F21.");

            AnchorGrip = cfg.Bind(S_PIVOT, "Anchor: pistol grip", new Vector3(0.2f, 0.1f, 0f),
                "The right hand on the pistol grip, as a point in the weapon root's local space. " +
                "The braced contact: nearly still, ready to fight recoil at any moment. This is " +
                "the pivot while the weapon is up but NOT shouldered.\n" +
                "\n" +
                "The default is the value you found by eye in the first tuning pass, not a number " +
                "copied from anywhere (docs/07-FINDINGS.md F16). The other two anchors are derived " +
                "from this one, so this is the point to get right first: set cone to 25 so the " +
                "swing is obvious, change one component at a time, and watch which part of the " +
                "weapon stays still.");

            BoreAxisChoice = cfg.Bind(S_PIVOT, "Bore axis", BoreAxis.Y,
                "Which local axis runs down the barrel. The other two anchors sit along it and the " +
                "cant rolls about it, so this has to be right before either means anything.\n" +
                "\n" +
                "It cannot be derived: lualeet's mapping puts pitch on X and yaw on Z, which leaves " +
                "Y as the bore, but that is an inference and not a measurement. Find it the same " +
                "way as the invert toggles - set the cant angle to 45, press your change-sight key, " +
                "and try each axis until the gun rolls rather than pitching or yawing.");

            BoreAxisInvert = cfg.Bind(S_PIVOT, "Bore axis points backward", false,
                "Flip if the stock and support hand come out swapped - the gun will pivot about a " +
                "point in front of the muzzle when aiming, which is unmistakable.");

            StockBehindGrip = cfg.Bind(S_PIVOT, "Buttpad behind grip (m)", 0.30f, new ConfigDescription(
                "Distance from the pistol grip back to the buttpad. This is a real measurement of " +
                "a rifle rather than something to find by eye: about 0.30 m on a mid-size rifle, " +
                "shorter on a folded or bullpup layout.\n" +
                "\n" +
                "It matters because it IS the pivot once the weapon is shouldered. The stock is " +
                "pinned to your shoulder, so that is what the gun turns about.",
                new AcceptableValueRange<float>(0f, 1f)));

            LeftHandAheadOfGrip = cfg.Bind(S_PIVOT, "Support hand ahead of grip (m)", 0.30f, new ConfigDescription(
                "Distance from the pistol grip forward to the support hand on the handguard. " +
                "About 0.30 m with a normal C-clamp hold, less on a short handguard.\n" +
                "\n" +
                "The support hand is the driving end - it is what the mouse moves - and it sits on " +
                "the bore line, which is why the cant rolls about it.",
                new AcceptableValueRange<float>(0f, 1f)));

            AnchorLeeway = cfg.Bind(S_PIVOT, "Anchor leeway", 0f, new ConfigDescription(
                "How much the braced contact gives, 0..1.\n" +
                "\n" +
                "Zero is a perfectly rigid brace: the grip does not move at all. Real bracing is " +
                "not rigid - there is a little travel when you turn - so the centre of rotation " +
                "slides slightly toward the driving hand. Small values only; at 1 the gun swings " +
                "about the wrong end entirely.",
                new AcceptableValueRange<float>(0f, 0.5f)));

            InwardConeScale = cfg.Bind(S_PIVOT, "Inward cone scale", 1f, new ConfigDescription(
                "How much of the cone survives on the side the buttstock cannot swing to.\n" +
                "\n" +
                "At low ready the stock rests against your strong-side hip. Swinging the muzzle " +
                "that way drives the stock into your body and it runs out of room; swinging the " +
                "other way is free. So the cone is not centred on you.\n" +
                "\n" +
                "1.0 restores the old symmetric behaviour. 0.55 gives noticeably less room inward. " +
                "This narrows the cone rather than clamping the offset, so the gun eases to a stop " +
                "instead of hitting a wall - and it fades out as you shoulder the weapon, because " +
                "once the stock is in the pocket there is no hip to hit.",
                new AcceptableValueRange<float>(0.1f, 1f)));

            StrongSideRight = cfg.Bind(S_PIVOT, "Strong side is right", true,
                "Which side the stock is braced on. If the resistance turns up on the wrong side, " +
                "flip this - the yaw sign convention decides it and it is quicker to switch than " +
                "to reason about.");

            // -- cant --
            CantEnabled = cfg.Bind(S_PIVOT, "Cant on sight switch", true,
                "Roll the weapon about the bore when you press your CHANGE SIGHT key - the game's " +
                "own action, so it works on whatever you have it bound to.\n" +
                "\n" +
                "Press once to cant, press again to come back upright. It rolls whether or not a " +
                "canted sight is actually fitted, because the hold is useful on its own: it keeps " +
                "the gun controlled against the shoulder in a doorway without the receiver filling " +
                "your view.");

            CantAngle = cfg.Bind(S_PIVOT, "Cant angle (deg)", 45f, new ConfigDescription(
                "How far the weapon rolls. 45 is the usual offset-sight mount angle. Negative " +
                "rolls the other way.",
                new AcceptableValueRange<float>(-90f, 90f)));

            CantSpeed = cfg.Bind(S_PIVOT, "Cant speed (1/s)", 12f, new ConfigDescription(
                "How fast the roll settles, same exponential form as the body spring. 12/s is " +
                "about a quarter second - fast enough to be a deliberate action, slow enough to " +
                "read as a movement.",
                new AcceptableValueRange<float>(1f, 40f)));

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

            LoweredPoseEnabled = cfg.Bind(S_STANCE, "Apply stance poses", true,
                "Visually lower the weapon in the down stance. Off leaves the pose alone and only " +
                "gates free aim.");
            LoweredPos = cfg.Bind(S_STANCE, "Lowered position offset", Vector3.zero,
                "Position offset for the weapon-down pose. UNMEASURED - zero means no visual " +
                "lowering until you set it. Tune by eye against Bodycam; there is no source to " +
                "copy a number from (docs/07-FINDINGS.md F16).");
            LoweredRot = cfg.Bind(S_STANCE, "Lowered rotation offset", Vector3.zero,
                "Rotation offset for the weapon-down pose. UNMEASURED - see above.");
            LoweredLerpSpeed = cfg.Bind(S_STANCE, "Pose lerp speed", 6f, new ConfigDescription(
                "How fast the weapon moves between stance poses, per second. 6/s is roughly a " +
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
                "Position offset for the un-shouldered low-ready stance (docs/07-FINDINGS.md " +
                "F12.2). UNMEASURED. The target is buttstock behind the arm near the hip, firing " +
                "hand lowered - not the shoulder pocket. Set cone to 25 to make the pose obvious " +
                "while you tune, then put it back.");

            ReadyRot = cfg.Bind(S_STANCE, "Ready rotation offset", Vector3.zero,
                "Rotation offset for the low-ready stance. UNMEASURED - see above.");

            HighReadyEnabled = cfg.Bind(S_STANCE, "Enable high ready", false,
                "A compressed intermediate hold between low ready and shouldered - weapon closer " +
                "to the body, quicker to bring up. NOT in the Bodycam spec: a deliberate " +
                "divergence, off by default.");

            HighReadyKey = cfg.Bind(S_STANCE, "High ready key", new KeyboardShortcut(KeyCode.C),
                "Toggles the compressed hold. Only does anything when high ready is enabled.");

            HighReadyPos = cfg.Bind(S_STANCE, "High ready position offset", Vector3.zero,
                "UNMEASURED. Compressed: weapon pulled in toward the chest, muzzle up rather than " +
                "down - the opposite direction from low ready.");

            HighReadyRot = cfg.Bind(S_STANCE, "High ready rotation offset", Vector3.zero,
                "UNMEASURED - see above.");

            CouplingLowReady = cfg.Bind(S_STANCE, "Coupling in low ready", 1f, new ConfigDescription(
                "Free-aim strength while the weapon is up but not shouldered. 1.0 is full - the " +
                "spec measures the coupling as identical hip and shouldered.",
                new AcceptableValueRange<float>(0f, 1f)));

            CouplingHighReady = cfg.Bind(S_STANCE, "Coupling in high ready", 1f, new ConfigDescription(
                "Free-aim strength in the compressed hold.",
                new AcceptableValueRange<float>(0f, 1f)));

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

            StanceStaminaEnabled = cfg.Bind(S_STANCE, "Stance affects arm stamina", false,
                "Tarkov already has a separate HandsStamina pool that drains while the weapon is " +
                "up. Rather than adding a second drain that double-counts with it, this scales how " +
                "fast that pool RECOVERS depending on stance: shouldered recovers slowly, low ready " +
                "faster, weapon down fastest.\n" +
                "\n" +
                "This is what gives low ready a reason to exist. Without a cost to holding the " +
                "weapon shouldered, nobody lowers it and the stance is decoration.\n" +
                "\n" +
                "Off by default: it changes stamina balance, which is a Tarkov-realism idea rather " +
                "than a Bodycam one. See docs/07-FINDINGS.md F18.");

            HandsRecoveryShouldered = cfg.Bind(S_STANCE, "Arm recovery: shouldered", 0.5f, new ConfigDescription(
                "Multiplier on the stock hands-stamina restore rate while shouldered. Below 1 " +
                "recovers slower than stock.", new AcceptableValueRange<float>(0f, 3f)));

            HandsRecoveryLowReady = cfg.Bind(S_STANCE, "Arm recovery: low ready", 1.5f, new ConfigDescription(
                "Multiplier while the weapon is up but not shouldered.",
                new AcceptableValueRange<float>(0f, 3f)));

            HandsRecoveryDown = cfg.Bind(S_STANCE, "Arm recovery: weapon down", 2.5f, new ConfigDescription(
                "Multiplier with the weapon lowered.", new AcceptableValueRange<float>(0f, 3f)));

            AdsSpeedFromWeight = cfg.Bind(S_STANCE, "ADS speed from weapon weight", false,
                "Heavier weapons take longer to come into the shoulder. Scales the game's own " +
                "AimingSpeed by weight relative to the reference below. Off by default: it changes " +
                "handling balance across every weapon.");

            AdsWeightReference = cfg.Bind(S_STANCE, "ADS reference weight (kg)", 3.5f, new ConfigDescription(
                "A weapon at this weight aims at stock speed. Heavier is slower, lighter faster. " +
                "3.5 kg is roughly a loaded mid-size rifle.",
                new AcceptableValueRange<float>(0.5f, 12f)));

            AdsWeightStrength = cfg.Bind(S_STANCE, "ADS weight effect strength", 0.5f, new ConfigDescription(
                "0 = weight does nothing. 1 = speed scales inversely with weight in full. 0.5 " +
                "halves the effect, which keeps heavy guns usable.",
                new AcceptableValueRange<float>(0f, 1f)));

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

        /// <summary>
        /// Stance profiles, rebuilt each frame so F12 edits take effect live.
        /// Down has zero coupling: that is what makes the mouse drive the view
        /// with the weapon lowered (F13).
        /// </summary>
        public StanceProfiles StanceSnapshot()
        {
            float blend = LoweredLerpSpeed.Value;
            return new StanceProfiles
            {
                Down = new StanceProfile {
                    Pos = LoweredPos.Value, Rot = LoweredRot.Value,
                    Coupling = 0f, HandsRecovery = HandsRecoveryDown.Value, BlendSpeed = blend },

                LowReady = new StanceProfile {
                    Pos = ReadyPoseEnabled.Value ? ReadyPos.Value : Vector3.zero,
                    Rot = ReadyPoseEnabled.Value ? ReadyRot.Value : Vector3.zero,
                    Coupling = CouplingLowReady.Value, HandsRecovery = HandsRecoveryLowReady.Value, BlendSpeed = blend },

                HighReady = new StanceProfile {
                    Pos = HighReadyPos.Value, Rot = HighReadyRot.Value,
                    Coupling = CouplingHighReady.Value, HandsRecovery = HandsRecoveryLowReady.Value, BlendSpeed = blend },

                // Shouldered is Tarkov's own pose - zero offset. The sights are
                // where the game puts them; we only decide the coupling.
                Shouldered = new StanceProfile {
                    Pos = Vector3.zero, Rot = Vector3.zero,
                    Coupling = 1f, HandsRecovery = HandsRecoveryShouldered.Value, BlendSpeed = blend },
            };
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
                DisengageBoost = DisengageBoost.Value,
                InwardConeScale = InwardConeScale.Value,
                StrongSideSign = StrongSideRight.Value ? 1f : -1f
            };
        }

        /// <summary>
        /// The three contact points, rebuilt each frame so F12 edits apply live.
        /// Only the grip is a free point; the others follow from it along the bore.
        /// </summary>
        public WeaponAnchors AnchorSnapshot()
        {
            bool measured = UseMeasuredGeometry.Value;

            // Measured beats configured, per member, rather than all or nothing.
            // A pistol has a real grip and a real sight line but no buttstock, and
            // the right answer there is to use the two that exist and fall back
            // only on the third.
            bool useBore = measured && WeaponGeometry.HaveBore;
            bool useGrip = measured && WeaponGeometry.HaveGrip;
            bool useStock = measured && WeaponGeometry.HaveStock;

            return new WeaponAnchors
            {
                Grip = useGrip ? WeaponGeometry.Grip : AnchorGrip.Value,
                Bore = useBore ? WeaponGeometry.Bore
                               : WeaponAnchors.AxisVector(BoreAxisChoice.Value, BoreAxisInvert.Value),
                StockBehind = StockBehindGrip.Value,
                LeftHandAhead = LeftHandAheadOfGrip.Value,
                Leeway = AnchorLeeway.Value,
                BoreKnown = useBore,
                StockMeasured = useStock,
                MeasuredStock = WeaponGeometry.Stock
            };
        }
    }
}
