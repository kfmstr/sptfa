using SPTFreeAim.Compat;
using SPTFreeAim.Core;
using SPTFreeAim.Patches;
using UnityEngine;

namespace SPTFreeAim.Debugging
{
    /// <summary>
    /// On-screen readout. You will live in this for the whole tuning phase - the
    /// test checklist in docs/02-PLAN.md is mostly "does this number do the right
    /// thing", and reading it off the screen beats reading it off a log.
    ///
    /// What to watch, against docs/02-PLAN.md's per-build checklist:
    ///   slow steady turn   -> offset stays near zero
    ///   moderate turn      -> offset parks at roughly the cone size
    ///   hard flick         -> offset spikes toward the cap, then decays
    ///   stop moving        -> offset returns to 0.0 in about a second
    /// </summary>
    public class DebugHud
    {
        private GUIStyle _style;
        private GUIStyle _boxStyle;
        private readonly float[] _decay = new float[64];
        private int _decayIndex;
        private float _peak;
        private float _peakAge;

        public void Draw(Plugin p)
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    richText = true,
                    alignment = TextAnchor.UpperLeft
                };
                _boxStyle = new GUIStyle(GUI.skin.box);
            }

            FreeAimState st = p.State;
            FreeAimConfig cfg = p.Cfg;
            float mag = st.Offset.magnitude;

            TrackDecay(mag);

            string body =
                Row("mode", cfg.Mode.Value.ToString() + (cfg.Enabled.Value ? "" : "  <color=#ff6666>(OFF)</color>")) +
                Row("refs", GameRefs.Ready
                        ? "ok" + (GameRefs.UsingLocalRotateAroundFallback ? "  <color=#ffcc55>pivot fallback</color>" : "")
                        : "<color=#ff6666>" + GameRefs.LastError + "</color>") +
                Row("player", FreeAimPatches.LocalPlayer == null ? "<color=#ffcc55>none</color>" : "ok") +
                "\n" +
                Row("gun   yaw/pitch", string.Format("{0,7:F1} {1,7:F1}", st.Gun.x, st.Gun.y)) +
                Row("body  yaw/pitch", string.Format("{0,7:F1} {1,7:F1}", st.Body.x, st.Body.y)) +
                Row("offset", string.Format("{0,7:F1} {1,7:F1}   |{2:F1}|  {3}",
                        st.Offset.x, st.Offset.y, mag, Bar(mag, cfg.CapDegrees.Value))) +
                Row("mouse delta", string.Format("{0,7:F2} {1,7:F2}", st.MouseDelta.x, st.MouseDelta.y)) +
                Row("recoil raw xyz", string.Format("{0,6:F2} {1,6:F2} {2,6:F2}   -> gun {3,5:F1} {4,5:F1}",
                        st.RecoilRaw.x, st.RecoilRaw.y, st.RecoilRaw.z, st.RecoilOffset.x, st.RecoilOffset.y)) +
                "\n" +
                Row("hinge", cfg.Hinge.Value == HingeMode.AroundGrip
                        ? string.Format("<color=#7fd1b9>AROUND GRIP</color>  lever <color=#7fd1b9>{0:F3} m</color>   {1}",
                              FreeAimPatches.LastLever,
                              cfg.PivotFromWeapon.Value
                                  ? (GameRefs.RotationCentreAvailable
                                        ? "<color=#7fd1b9>centre from weapon</color>"
                                        : "<color=#ffcc55>" + GameRefs.RotationCentreWhyNot + "</color>")
                                  : "grip " + cfg.GripFromEye)
                        : string.Format("<color=#ff6666>LEGACY EULER</color> cannot hinge (F40)  pivot {0,5:F2} {1,5:F2} {2,5:F2} {3}",
                              FreeAimPatches.LastPivotLocal.x, FreeAimPatches.LastPivotLocal.y,
                              FreeAimPatches.LastPivotLocal.z,
                              cfg.PivotFromWeapon.Value
                                  ? (GameRefs.RotationCentreAvailable
                                        ? "<color=#7fd1b9>from weapon</color>"
                                        : "<color=#ffcc55>" + GameRefs.RotationCentreWhyNot + "</color>")
                                  : "manual dial")) +
                Row("arms", GameRefs.HandsStaminaAvailable
                        ? string.Format("{0,6:F0} hands stamina   drain {1}",
                              GameRefs.GetHandsStamina(FreeAimPatches.LocalPlayer),
                              cfg.ArmDrainEnabled.Value
                                  ? string.Format("{0:F1}/s x{1:F1} aimed", cfg.ArmDrainRate.Value,
                                        cfg.ArmDrainAimedMultiplier.Value)
                                  : "<color=#ffcc55>off</color>")
                        : "<color=#ffcc55>hands pool not reached yet</color>") +
                Row("gun blur", cfg.DofEnabled.Value
                        ? string.Format("focus <color=#7fd1b9>{0:F1} m</color>  blur {1:F2}  band {2:F2}   {3}",
                              FocusDepth.LastFocus, FocusDepth.LastBlur,
                              cfg.DofBand.Value, FocusDepth.Status)
                        : "off") +
                Row("gun roll", cfg.GunRollEnabled.Value
                        ? string.Format("<color=#7fd1b9>{0,6:F1} deg</color>   aimed {1:F0} / ready {2:F0}   {3}",
                              FreeAimPatches.LastRoll, cfg.GunRollAimed.Value, cfg.GunRollReady.Value,
                              cfg.GunRollAboutView.Value ? "about view" : "about barrel")
                        : "off") +
                Row("sight alpha", cfg.SightAlpha.Value > 0.001f
                        ? OpticHousing.Status
                        : "off") +
                Row("aim fov", cfg.KeepFovWhenAiming.Value
                        ? (GameRefs.AimFovAvailable
                              ? "<color=#7fd1b9>" + FreeAimPatches.AimFovState + "</color>   breath "
                                    + (GameRefs.IsHoldingBreath(FreeAimPatches.LocalPlayer) ? "HELD" : "-")
                              : "<color=#ffcc55>" + GameRefs.AimFovWhyNot + "</color>")
                        : "stock Tarkov") +
                Row("parentage", FreeAimPatches.ParentageReport) +
                Row("cone / cap", string.Format("{0:F1} / {1:F1} deg", cfg.ConeDegrees.Value, cfg.CapDegrees.Value)) +
                Row("k / push", string.Format("{0:F1} / {1:F2}", cfg.SpringK.Value, cfg.PushFactor.Value)) +
                Row("gate", string.Format("{0:F2}   {1}", st.Gate, p.Stance)) +
                Row("aim blend", string.Format("{0:F2}   coupling x{1:F2}", st.AimBlend, cfg.AimCoupling.Value)) +
                "\n" +
                Row("peak offset", string.Format("{0:F1} deg  ({1:F1}s ago)", _peak, _peakAge)) +
                Row("convergence", ConvergenceEstimate()) +
                "\n" +
                Row("probe (F10)", YawWriteProbe.LastResult);

            // Grows with the rows. A clipped HUD is a HUD you stop trusting, and
            // the hinge row is wider now that it prints the live pivot.
            GUI.Box(new Rect(10, 10, 560, 360), GUIContent.none, _boxStyle);
            GUI.Label(new Rect(20, 18, 540, 344), "<b>SPT Free Aim</b>\n\n" + body, _style);
        }


        private static string Row(string label, string value)
        {
            return string.Format("<color=#9fb3c8>{0,-16}</color> {1}\n", label, value);
        }

        private static string Bar(float value, float max)
        {
            if (max <= 0f) return "";
            int filled = Mathf.Clamp(Mathf.RoundToInt(value / max * 20f), 0, 20);
            return "<color=#7fd1b9>" + new string('|', filled) + "</color>" + new string('.', 20 - filled);
        }

        private void TrackDecay(float mag)
        {
            _decay[_decayIndex] = mag;
            _decayIndex = (_decayIndex + 1) % _decay.Length;

            if (mag > _peak) { _peak = mag; _peakAge = 0f; }
            else
            {
                _peakAge += Time.deltaTime;
                if (_peakAge > 3f) { _peak = mag; _peakAge = 0f; }
            }
        }

        /// <summary>
        /// The spec's headline number is "about 1 second to exact match". This
        /// reports what the current k actually gives, so you are tuning against
        /// the measurement rather than against a feeling.
        /// </summary>
        private string ConvergenceEstimate()
        {
            float k = Plugin.Instance.Cfg.SpringK.Value;
            if (k <= 0f) return "never (k = 0)";
            float t99 = 4.6f / k;   // ln(100)/k
            string verdict = Mathf.Abs(t99 - 1f) < 0.25f
                ? "<color=#7fd1b9>matches spec</color>"
                : (t99 > 1f ? "<color=#ffcc55>slower than spec</color>" : "<color=#ffcc55>faster than spec</color>");
            return string.Format("{0:F2}s to 99%   {1}", t99, verdict);
        }
    }
}
