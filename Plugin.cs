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

            RescueClashingHotkeys();

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

                // The aim button carries a second gesture: tap for low ready,
                // hold to aim. Both routes set the same flag, so the key and the
                // tap are alternatives rather than rivals. F64.
                Stance.ReadAimTap(Cfg.AimTapWindow.Value,
                    FreeAimPatches.LocalPlayer != null
                    && !Compat.GameRefs.Probe_InventoryOpen.Read(FreeAimPatches.LocalPlayer));
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
        /// Move ANY clashing hotkey off a key Tarkov owns, once, and say so.
        ///
        /// F57 made a clashing hotkey inert, which was right for the master
        /// toggle - a key that silently switches the whole mod off is a trap - and
        /// wrong for everything else. The owner had "Stance key = Z", Tarkov has
        /// Z = DropBackpack, and the result was a low ready button that did
        /// nothing at all, with one warning line in a log nobody reads mid-raid.
        /// A dead key is worse than a shared one. F63.
        ///
        /// Letting it through would have been worse still: every trip to low
        /// ready would drop his backpack. So the answer is neither ignore nor
        /// allow, it is MOVE - onto a key Tarkov does not use - and say loudly
        /// which key it now is.
        ///
        /// Only ever moves a key that is provably clashing, only onto a key
        /// Tarkov provably does not use and this mod is not already using, and
        /// never silently.
        /// </summary>
        private void RescueClashingHotkeys()
        {
            if (!Compat.KeyConflicts.Checked) return;

            // Letters first where a key gets pressed in combat, function keys
            // where it does not. Tarkov leaves very few letters free, so the list
            // is short by necessity rather than by choice.
            Rescue("Master toggle key", Cfg.ToggleKey, new[]
            {
                UnityEngine.KeyCode.F8, UnityEngine.KeyCode.F11, UnityEngine.KeyCode.F6,
                UnityEngine.KeyCode.F5, UnityEngine.KeyCode.Backslash
            });

            Rescue("Stance key", Cfg.StanceKey, new[]
            {
                UnityEngine.KeyCode.M, UnityEngine.KeyCode.J, UnityEngine.KeyCode.Semicolon,
                UnityEngine.KeyCode.Quote, UnityEngine.KeyCode.Backslash
            });

            Rescue("HUD toggle key", Cfg.HudKey, new[]
            {
                UnityEngine.KeyCode.F9, UnityEngine.KeyCode.F7, UnityEngine.KeyCode.F6
            });

            Rescue("Yaw write probe key", Cfg.ProbeWriteKey, new[]
            {
                UnityEngine.KeyCode.F10, UnityEngine.KeyCode.F11, UnityEngine.KeyCode.F5
            });
        }

        private void Rescue(string name, BepInEx.Configuration.ConfigEntry<BepInEx.Configuration.KeyboardShortcut> entry,
                            UnityEngine.KeyCode[] preferred)
        {
            if (!Compat.KeyConflicts.IsBlocked(name)) return;

            string was = entry.Value.MainKey.ToString();
            string why = Compat.KeyConflicts.Why(name);

            UnityEngine.KeyCode free = Compat.KeyConflicts.FirstFreeKey(preferred,
                Cfg.ToggleKey.Value.MainKey, Cfg.StanceKey.Value.MainKey,
                Cfg.HudKey.Value.MainKey,
                Cfg.ProbeWriteKey.Value.MainKey);

            if (free == UnityEngine.KeyCode.None)
            {
                Log.LogWarning(name + " is on " + was + ", which " + why +
                               ", and every fallback is taken too. That key is IGNORED so the " +
                               "game's action is the only one. Pick a free key in the F12 menu.");
                return;
            }

            entry.Value = new BepInEx.Configuration.KeyboardShortcut(free);
            Compat.KeyConflicts.Blocked.Remove(name);
            Compat.KeyConflicts.Moved.Add(name + " " + was + " -> " + free);

            Log.LogWarning(name + " was on " + was + ", which " + why +
                           ". Pressing it would have done both. Moved to " + free +
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
