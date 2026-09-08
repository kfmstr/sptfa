using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SPTFreeAim.Compat;
using SPTFreeAim.Core;
using SPTFreeAim.Debugging;
using SPTFreeAim.Patches;
using UnityEngine;

namespace SPTFreeAim
{
    /// <summary>
    /// SPT Free Aim - Bodycam-style decoupled aiming.
    ///
    /// The gun points where the mouse points, instantly. The body turns to
    /// follow the gun a moment later. Active only when the weapon is raised.
    ///
    /// Continues the work of lualeet/sptarkov-deadzone (MIT), whose pivot
    /// rotation maths is reused for the apply step. The drive loop is different
    /// on purpose - see docs/04-DECISIONS.md D4.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "kfmstr.sptfreeaim";
        public const string NAME = "SPT Free Aim";
        public const string VERSION = "0.1.0";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        /// <summary>True when the mod should be doing work this frame.</summary>
        public static bool Active
        {
            get
            {
                return Instance != null
                    && !Instance._emergencyDisabled
                    && Instance.Cfg.Enabled.Value
                    && Instance.Cfg.Mode.Value != DriveMode.Disabled
                    && GameRefs.Ready;
            }
        }

        public FreeAimConfig Cfg { get; private set; }
        public FreeAimState State { get; private set; }
        public StanceState Stance { get; private set; }

        private bool _emergencyDisabled;
        private Harmony _harmony;
        private DebugHud _hud;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Cfg = new FreeAimConfig();
            Cfg.Bind(Config);

            State = new FreeAimState();
            Stance = new StanceState();
            _hud = new DebugHud();

            GameRefs.Resolve();
            Log.LogInfo(GameRefs.Describe());

            if (!GameRefs.Ready)
            {
                Log.LogError(
                    "SPT Free Aim could not resolve the game members it needs, so it is inert. " +
                    "This is what a Tarkov client update looks like: fix Compat/GameRefs.cs and " +
                    "nothing else. Reason: " + GameRefs.LastError);
                return;
            }

            if (!GameRefs.RotationWritable)
            {
                Log.LogWarning(
                    "MovementContext.Rotation is not writable on this build, so Intercept mode is " +
                    "unavailable. Compensate mode does not need it. See docs/05-OPEN-QUESTIONS.md Q4.");
            }

            FreeAimPatches.Apply(_harmony = new Harmony(GUID));
            RecoilPatch.Apply(_harmony);

            Log.LogInfo(NAME + " " + VERSION + " loaded. Mode: " + Cfg.Mode.Value +
                        ". F12 to configure, F8 master toggle, F9 HUD, F10 write probe.");
        }

        private void Update()
        {
            if (Instance == null || Cfg == null) return;

            if (Cfg.ToggleKey.Value.IsDown())
            {
                Cfg.Enabled.Value = !Cfg.Enabled.Value;
                Log.LogInfo("Free aim " + (Cfg.Enabled.Value ? "ON" : "OFF"));
                if (!Cfg.Enabled.Value) ResetState();
            }

            if (Cfg.HudKey.Value.IsDown())
                Cfg.ShowHud.Value = !Cfg.ShowHud.Value;

            if (Cfg.ProbeWriteKey.Value.IsDown())
                YawWriteProbe.Trigger(Cfg.ProbeWriteDegrees.Value);

            YawWriteProbe.Tick();

            if (Cfg.StanceGateEnabled.Value)
            {
                Stance.ReadInput(Cfg.StanceKey.Value, Cfg.StanceHoldToReady.Value);
                Stance.ReadAutoState(
                    FreeAimPatches.LocalPlayer,
                    Cfg.SuspendOnSprint.Value,
                    Cfg.SuspendOnAnimation.Value,
                    Cfg.SuspendOnStationary.Value);

                ReportProbesOnce();
            }
        }

        private bool _probesReported;

        /// <summary>
        /// The stance probes can only resolve once a local player exists, so the
        /// result is not known at Awake. Report it once, the first frame it is.
        /// </summary>
        private void ReportProbesOnce()
        {
            if (_probesReported || FreeAimPatches.LocalPlayer == null) return;
            _probesReported = true;

            Log.LogInfo(GameRefs.DescribeProbes());

            if (!GameRefs.Probe_SprintOnContext.Resolved && !GameRefs.Probe_SprintOnPlayer.Resolved)
                Log.LogWarning(
                    "Sprint state not found, so free aim will stay on while sprinting and fight the " +
                    "sprint animation. Add the real member name to the probe's candidate list in " +
                    "Compat/GameRefs.cs. See docs/08-RECON.md R9.");

            if (!GameRefs.Probe_Reloading.Resolved)
                Log.LogInfo(
                    "No explicit reload flag resolved. Item use, melee and throwables are still " +
                    "covered by the hands-controller type check; a magazine reload is not. " +
                    "docs/08-RECON.md R9 says where to look.");
        }

        private void OnGUI()
        {
            if (Cfg != null && Cfg.ShowHud.Value) _hud.Draw(this);
        }

        public void OnLocalPlayerChanged()
        {
            FreeAimPatches.ForgetGuards();
            ResetState();
            Log.LogInfo("Local player acquired. Free aim state reset.");
        }

        private void ResetState()
        {
            State = new FreeAimState();
        }

        /// <summary>
        /// Called when the per-frame path throws. Better to stop cleanly than to
        /// fill the log at 120 Hz and leave the weapon in a broken pose.
        /// </summary>
        public void EmergencyDisable()
        {
            _emergencyDisabled = true;
            Log.LogError("SPT Free Aim disabled for this session. Re-enable by restarting the raid.");
        }

        private void OnDestroy()
        {
            FreeAimPatches.Remove();
        }
    }
}
