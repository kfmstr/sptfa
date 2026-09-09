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
            Vector3 pivot = cfg.AnchorSnapshot().Pivot(st.AimBlend);

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
                Row("cone / cap", string.Format("{0:F1} / {1:F1} deg", cfg.ConeDegrees.Value, cfg.CapDegrees.Value)) +
                Row("k / push", string.Format("{0:F1} / {1:F2}", cfg.SpringK.Value, cfg.PushFactor.Value)) +
                Row("stance", string.Format("{0,-11} gate {1:F2}   arms x{2:F2}",
                        p.Stance.Current, st.Gate, p.Stance.HandsRecovery)) +
                Row("stance pose", string.Format("pos {0,5:F2} {1,5:F2} {2,5:F2}   {3}",
                        p.Stance.PosePos.x, p.Stance.PosePos.y, p.Stance.PosePos.z, p.Stance)) +
                Row("aim blend", string.Format("{0:F2}   coupling x{1:F2}", st.AimBlend, cfg.AimCoupling.Value)) +
                "\n" +
                // The pivot is the thing being tuned now, so show where it
                // actually is this frame rather than only what was configured.
                Row("pivot", string.Format("{0,5:F2} {1,5:F2} {2,5:F2}   {3}",
                        pivot.x, pivot.y, pivot.z,
                        st.AimBlend > 0.5f ? "toward BUTTPAD" : "toward GRIP")) +
                Row("cone now", string.Format("{0:F1} deg  (set {1:F1}, inward x{2:F2})",
                        st.EffectiveCone(st.Offset, cfg.Snapshot()), cfg.ConeDegrees.Value,
                        cfg.InwardConeScale.Value)) +
                Row("cant", string.Format("{0,5:F1} -> {1,5:F1} deg   axis {2}{3}   {4}",
                        st.Roll, st.RollTarget, cfg.BoreAxisChoice.Value,
                        cfg.BoreAxisInvert.Value ? "-" : "+",
                        SightSwitchPatch.Hooked ? "on change-sight key"
                                                : "<color=#ffcc55>not hooked</color>")) +
                "\n" +
                Row("peak offset", string.Format("{0:F1} deg  ({1:F1}s ago)", _peak, _peakAge)) +
                Row("convergence", ConvergenceEstimate()) +
                "\n" +
                Row("probe (F10)", YawWriteProbe.LastResult);

            GUI.Box(new Rect(10, 10, 560, 392), GUIContent.none, _boxStyle);
            GUI.Label(new Rect(20, 18, 540, 376), "<b>SPT Free Aim</b>\n\n" + body, _style);
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
