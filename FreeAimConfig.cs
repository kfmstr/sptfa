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
        public ConfigEntry<float> PivotDistance;
        public ConfigEntry<Vector3> PivotFineOffset;
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
        public ConfigEntry<float> SightAlpha;
        public ConfigEntry<bool> LogSightMaterials;
        public ConfigEntry<bool> DumpLensMaterial;
        public ConfigEntry<bool> KeepPeripheralVision;
        public ConfigEntry<float> PeripheralStrength;

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
            Hinge = cfg.Bind(S_PIVOT, "Hinge mode", HingeMode.LegacyEuler, new ConfigDescription(
                "LEGACY EULER is the original call and the DEFAULT - it is what was working in the\n" +
                "early builds, and the reason it stopped is documented in F23: the stance poses\n" +
                "rotate WeaponRoot, which is the parent frame this call reads. Those pose rotations\n" +
                "are now suppressed while this mode is selected, so it behaves as it did then.\n" +
                "\n" +
                "AROUND GRIP is the physical one. The right hand holds the pistol " +
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

            PivotDistance = cfg.Bind(S_PIVOT, "Pivot distance (m)", -0.15f, new ConfigDescription(
                "LEGACY EULER mode. Where the weapon hinges, as a distance along its local up axis. " +
                "This is the single dial the earliest builds had, and -0.15 is the value that put " +
                "the hinge on the pistol grip - the firing hand.\n" +
                "\n" +
                "Negative moves the pivot down the weapon toward the grip; positive moves it up and " +
                "out. Larger magnitudes give the muzzle a longer arc for the same angle.",
                new AcceptableValueRange<float>(-1f, 1f)));

            PivotFineOffset = cfg.Bind(S_PIVOT, "Pivot fine offset", Vector3.zero,
                "LEGACY EULER mode. Added on top of the distance above, for nudging the hinge off " +
                "the up axis. Zero by default: get the single dial right first, and only reach for " +
                "this if the grip is genuinely off that line.");

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

            DofOnlyAiming = cfg.Bind(S_BODY, "Gun blur only while aiming", true,
                "On, it fades in as you shoulder the weapon. Off, it is always there, which is " +
                "more truthful about eyes and much more noticeable while moving.");

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

            DumpLensMaterial = cfg.Bind(S_BODY, "Log the optic lens material", false,
                "DIAGNOSTIC, for the scope glare question. Writes the shader name and every " +
                "property on the current optic's lens material to the log, once per weapon.\n" +
                "\n" +
                "Nothing in the client assembly exposes a per-optic glare, so whether the old " +
                "lens-glare effect can be switched back on depends on what that shader still " +
                "carries. Turn this on, aim down a scope, and send me the log lines.");

            KeepPeripheralVision = cfg.Bind(S_BODY, "Keep peripheral vision when aiming - does not work", false,
                "DOES NOT WORK on this build and the switch is left only so the reason is " +
                "visible rather than mysterious. CameraManager.AimDeltaFov turned out to be a " +
                "CONST float of 15 - writing it throws, and the game has 15 compiled into every " +
                "place it uses it, so even a successful write would do nothing. The HUD says so " +
                "too. Doing this properly means going through CameraManager.Fov instead; ask and " +
                "I will. See docs/07-FINDINGS.md F29.\n" +
                "\n" +
                "ORIGINAL DESCRIPTION FOLLOWS.\n" +
                "Keep your peripheral vision when the weapon comes into the shoulder.\n" +
                "\n" +
                "Tarkov narrows the field of view as you aim, which reads as closing one eye and " +
                "tunnelling on the sight. A shooter with both eyes open does not lose the room " +
                "around the sight. This scales CameraManager.AimDeltaFov, the game's own " +
                "aim-narrowing amount, so nothing else about the sight picture changes - the optic " +
                "and the reticle are exactly where the game puts them.\n" +
                "\n" +
                "It does mean no free zoom on magnified optics, because the narrowing is what that " +
                "zoom IS. Back the strength off below if you want some of it back.\n" +
                "\n" +
                "SEPARATE from the optic housing setting above, and off by default: this one was " +
                "my guess at what the thread asked for before I could read it, and the thread was " +
                "about the housing. It is a reasonable thing to want on its own, so it stays.");

            PeripheralStrength = cfg.Bind(S_BODY, "Peripheral vision strength", 1f, new ConfigDescription(
                "1.0 removes the aim narrowing entirely - full peripheral vision, no tunnel. " +
                "0.0 is stock Tarkov. 0.5 keeps half of it, which is a reasonable middle if " +
                "losing the magnification on scopes bothers you.",
                new AcceptableValueRange<float>(0f, 1f)));

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
