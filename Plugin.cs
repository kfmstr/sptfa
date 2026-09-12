using System;
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
    /// Continues the work of lualeet/sptarkov-deadzone (Unlicense), whose pivot
    /// rotation maths is reused for the apply step. The drive loop is different
    /// on purpose - see docs/04-DECISIONS.md D4.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "kfmstr.sptfreeaim";
        public const string NAME = "SPT Free Aim";
        public const string VERSION = "0.1.0";

        /// <summary>
        /// Stamped at compile time so the log can answer "is the game even
        /// running the build I just made". F31 cost a round trip to a stale
        /// DLL that no line of output distinguished from a fresh one.
        /// </summary>
        public static readonly string BuildStamp =
            System.IO.File.GetLastWriteTime(
                typeof(Plugin).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss");

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        /// <summary>True when the mod should be doing work this frame.</summary>
        public static bool Active
        {
            get
            {
                return Instance != null
                    && Instance._started
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

        /// <summary>True only once AwakeCore has run to completion. See F32.</summary>
        private bool _started;
        private Harmony _harmony;
        private DebugHud _hud;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // Every line of Awake used to run before the first log call, so a
            // throw in any of it produced exactly one line in the log - BepInEx's
            // own "Loading [SPT Free Aim]" - and then silence. That is what
            // happened, and it cost a round trip to diagnose something the mod
            // could have said itself. F30.
            Log.LogInfo("Awake: starting. " + VERSION + "  build " + BuildStamp);
            try { AwakeCore(); }
            catch (Exception e)
            {
                Log.LogError("SPT Free Aim FAILED TO START. Nothing below this line ran:\n" + e);
            }
        }

        private void AwakeCore()
        {
            Log.LogInfo("Awake: binding config");
            Cfg = new FreeAimConfig();
            Cfg.Bind(Config);
            Log.LogInfo("Awake: config bound, resolving game members");

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

            // Two config files, neither able to see the other, is a collision no
            // amount of care inside either one can catch. So compare them. F47.
            Compat.KeyConflicts.CheckOnce(
                new System.Collections.Generic.KeyValuePair<string, BepInEx.Configuration.KeyboardShortcut>(
                    "Master toggle key", Cfg.ToggleKey.Value),
                new System.Collections.Generic.KeyValuePair<string, BepInEx.Configuration.KeyboardShortcut>(
                    "Stance key", Cfg.StanceKey.Value),
                new System.Collections.Generic.KeyValuePair<string, BepInEx.Configuration.KeyboardShortcut>(
                    "HUD toggle key", Cfg.HudKey.Value),
                new System.Collections.Generic.KeyValuePair<string, BepInEx.Configuration.KeyboardShortcut>(
                    "Yaw write probe key", Cfg.ProbeWriteKey.Value));

            RescueClashingToggle();

            Log.LogInfo(NAME + " " + VERSION + " loaded. Mode: " + Cfg.Mode.Value +
                        ". F12 to configure, F8 master toggle, F9 HUD, F10 write probe.");

            _started = true;
        }

        private void Update()
        {
            // A NULL CHECK ON Cfg IS NOT ENOUGH, and that is the whole of F32.
            // Cfg is assigned before Cfg.Bind(Config) runs, so a throw inside
            // Bind leaves a non-null FreeAimConfig whose every ConfigEntry field
            // is still null - and the next line, Cfg.ToggleKey.Value, throws once
            // per frame forever.
            //
            // The flag is set on the last line of AwakeCore, so it means "all of
            // it ran", which is the only thing worth testing.
            if (!_started) return;

            // A hotkey Tarkov also owns is ignored, not merely warned about.
            // "Master toggle key = Alpha1" is Tarkov's SecondaryWeapon, so drawing
            // the pistol toggled the whole mod off, every time, and the only trace
            // was a pair of ON/OFF lines that read like a deliberate keypress. The
            // check for this existed and was right; nothing acted on it. F57.
            if (Cfg.ToggleKey.Value.IsDown() && !Compat.KeyConflicts.IsBlocked("Master toggle key"))
            {
                Cfg.Enabled.Value = !Cfg.Enabled.Value;

                // Turning it back on clears an emergency disable. Without this the
                // flag is permanent for the session, the toggle looks dead, and
                // the only way back is leaving the raid - which is exactly what
                // the owner hit. Fixed once in F19 and lost again in the rollback.
                if (Cfg.Enabled.Value && _emergencyDisabled)
                {
                    _emergencyDisabled = false;
                    Log.LogInfo("Clearing the emergency disable and retrying.");
                }
                Log.LogInfo("Free aim " + (Cfg.Enabled.Value ? "ON" : "OFF"));
                if (!Cfg.Enabled.Value) ResetState();
            }

            if (Cfg.HudKey.Value.IsDown() && !Compat.KeyConflicts.IsBlocked("HUD toggle key"))
                Cfg.ShowHud.Value = !Cfg.ShowHud.Value;

            if (Cfg.ProbeWriteKey.Value.IsDown() && !Compat.KeyConflicts.IsBlocked("Yaw write probe key"))
                YawWriteProbe.Trigger(Cfg.ProbeWriteDegrees.Value);

            YawWriteProbe.Tick();

            if (Cfg.StanceGateEnabled.Value)
            {
                Stance.ReadInput(
                    Compat.KeyConflicts.IsBlocked("Stance key")
                        ? new BepInEx.Configuration.KeyboardShortcut()
                        : Cfg.StanceKey.Value,
                    Cfg.StanceHoldToReady.Value);
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
            if (!_started) return;

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
        /// <summary>
        /// Move the master toggle off a key Tarkov owns, once, and say so.
        ///
        /// Ignoring a clashing key (see Update) stops the damage but leaves the
        /// owner with no master toggle at all until he edits a config file he has
        /// no reason to suspect. The bad value here is one I shipped as a DEFAULT,
        /// and changing a default does nothing to an install that already saved
        /// it - BepInEx only writes defaults for keys it has never seen. So the
        /// repair has to happen in the installed config, not in the source. F57.
        ///
        /// Only ever moves a key that is provably clashing, only to a key Tarkov
        /// does not use, and never silently.
        /// </summary>
        private void RescueClashingToggle()
        {
            if (!Compat.KeyConflicts.Checked) return;
            if (!Compat.KeyConflicts.IsBlocked("Master toggle key")) return;

            string was = Cfg.ToggleKey.Value.MainKey.ToString();
            string why = Compat.KeyConflicts.Why("Master toggle key");

            UnityEngine.KeyCode free = Compat.KeyConflicts.FirstFreeKey(
                new[]
                {
                    UnityEngine.KeyCode.F8, UnityEngine.KeyCode.F11, UnityEngine.KeyCode.F6,
                    UnityEngine.KeyCode.F5, UnityEngine.KeyCode.Backslash
                },
                Cfg.HudKey.Value.MainKey, Cfg.ProbeWriteKey.Value.MainKey, Cfg.StanceKey.Value.MainKey);

            if (free == UnityEngine.KeyCode.None)
            {
                Log.LogWarning("Master toggle key " + was + " clashes (" + why +
                               ") and every fallback key is taken too, so the toggle is IGNORED. " +
                               "Pick a free key in the F12 menu.");
                return;
            }

            Cfg.ToggleKey.Value = new BepInEx.Configuration.KeyboardShortcut(free);
            Compat.KeyConflicts.Blocked.Remove("Master toggle key");
            Compat.KeyConflicts.Report = "master toggle moved " + was + " -> " + free;

            Log.LogWarning("Master toggle key was " + was + ", and " + why +
                           ". Drawing that weapon was switching the mod off. Moved to " + free +
                           " and saved. Change it in the F12 menu if you want a different key.");
        }

        public void EmergencyDisable()
        {
            _emergencyDisabled = true;
            Log.LogError("SPT Free Aim disabled for this session. Press the master toggle (F8) twice to clear it and retry - no need to restart the raid.");
        }

        private void OnDestroy()
        {
            FreeAimPatches.Remove();
        }
    }
}
