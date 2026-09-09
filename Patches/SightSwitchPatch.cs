using System;
using System.Reflection;
using HarmonyLib;
using SPTFreeAim.Compat;
using UnityEngine;

namespace SPTFreeAim.Patches
{
    /// <summary>
    /// Cant the weapon on the game's own change-sight action.
    ///
    /// Hooking the ACTION rather than a key of our own is the point. The player
    /// already has a change-sight key bound and already reaches for it when they
    /// want the offset optic; a second binding would be a second thing to
    /// remember, and it would drift out of step with the game's own sight state.
    ///
    /// This fires whether or not a canted sight is fitted, which is deliberate.
    /// The roll is useful without one: it keeps the gun controlled close to the
    /// shoulder in a doorway without the receiver filling the view. See
    /// docs/07-FINDINGS.md F20.
    ///
    /// Entirely optional. If the method cannot be resolved on a future client the
    /// cant is unavailable and nothing else is affected.
    /// </summary>
    public static class SightSwitchPatch
    {
        public static bool Hooked { get; private set; }

        public static void Apply(Harmony harmony)
        {
            if (GameRefs.M_ChangeAimingMode == null)
            {
                Plugin.Log.LogWarning(
                    "FirearmController.ChangeAimingMode() not found, so the weapon cant is " +
                    "unavailable on this build. Everything else is unaffected. The name is in the " +
                    "dnSpy export - see docs/08-RECON.md.");
                return;
            }

            harmony.Patch(
                GameRefs.M_ChangeAimingMode,
                postfix: new HarmonyMethod(typeof(SightSwitchPatch).GetMethod(
                    nameof(AfterChangeAimingMode), BindingFlags.Static | BindingFlags.NonPublic)));

            Hooked = true;
            Plugin.Log.LogInfo("Cant hooked to FirearmController.ChangeAimingMode().");
        }

        /// <summary>
        /// Postfix, not prefix: let the game do its own sight switch first, so a
        /// throw of ours can never stop the player changing sights.
        /// </summary>
        private static void AfterChangeAimingMode(object __instance)
        {
            try
            {
                Plugin p = Plugin.Instance;
                if (p == null || p.State == null) return;
                if (!p.Cfg.CantEnabled.Value) return;

                // Only the local player's weapon. Bots change sights too.
                if (FreeAimPatches.LocalPlayer == null) return;
                object mine = GameRefs.GetHandsController(FreeAimPatches.LocalPlayer);
                if (!ReferenceEquals(mine, __instance)) return;

                float angle = p.Cfg.CantAngle.Value;
                bool canted = Mathf.Abs(p.State.RollTarget) > 0.01f;
                p.State.RollTarget = canted ? 0f : angle;

                if (p.Cfg.VerboseLogging.Value)
                    Plugin.Log.LogInfo("Cant -> " + p.State.RollTarget.ToString("F0") + " deg");
            }
            catch (Exception e)
            {
                // A failure here must not break sight switching, which is a
                // normal game action the player needs whether or not this mod
                // works. Log once-ish and carry on.
                Plugin.Log.LogError("Cant toggle failed, disabling the cant: " + e);
                if (Plugin.Instance != null) Plugin.Instance.Cfg.CantEnabled.Value = false;
            }
        }
    }
}
