using SPTFreeAim.Compat;
using SPTFreeAim.Patches;
using UnityEngine;

namespace SPTFreeAim.Debugging
{
    /// <summary>
    /// The feasibility probe. Answers docs/05-OPEN-QUESTIONS.md Q4 in a raid,
    /// instead of by tracing input handling in dnSpy.
    ///
    /// It writes MovementContext.Rotation with N degrees of extra yaw, re-reads
    /// it over the next few frames, and reports one of:
    ///
    ///   STUCK AND HELD   - the write landed and the game kept it.
    ///                      Intercept mode is viable.
    ///
    ///   STUCK THEN LOST  - the write landed but the game overwrote it within a
    ///                      frame. Something recomputes the bearing after our
    ///                      hook. This is the one case where the dnSpy trace is
    ///                      still worth the budget - you need to find that
    ///                      writer and patch after it.
    ///
    ///   REJECTED         - no writable path at all. Use Compensate mode.
    ///
    /// In all three cases, also note whether the VIEW visibly jumped. A write
    /// that lands but does not move the camera means the bearing is not what the
    /// camera reads from, and the whole MovementContext approach is the wrong
    /// tree - worth knowing on day one rather than after twenty hours.
    ///
    /// WHAT GETS WRITTEN. Yaw and Pitch are read-only computed properties over
    /// Rotation (Rotation.x is Yaw, Rotation.y is Pitch). Writing Rotation runs
    /// the game's own pipeline - clamping, previous-rotation bookkeeping,
    /// pushing pitch into the weapon animation, the hands-to-body angle
    /// correction - so a passing probe means the supported path works, not that
    /// we got away with poking a private field.
    /// </summary>
    public static class YawWriteProbe
    {
        private enum Phase { Idle, Watching }

        private static Phase _phase = Phase.Idle;
        private static Vector2 _expected;
        private static Vector2 _before;
        private static int _framesWaited;

        public static string LastResult = "not run";

        public static void Trigger(float degrees)
        {
            object mc = GameRefs.GetMovementContext(FreeAimPatches.LocalPlayer);
            if (mc == null)
            {
                LastResult = "no local player - run this in a raid";
                Plugin.Log.LogWarning("[probe] " + LastResult);
                return;
            }

            if (!GameRefs.RotationWritable)
            {
                LastResult = "REJECTED - MovementContext.Rotation has no writable path. Use Compensate mode.";
                Plugin.Log.LogWarning("[probe] " + LastResult);
                return;
            }

            _before = GameRefs.GetRotation(mc);
            _expected = new Vector2(_before.x + degrees, _before.y);

            if (!GameRefs.SetRotation(mc, _expected))
            {
                LastResult = "REJECTED - the write threw or was refused. Use Compensate mode.";
                Plugin.Log.LogWarning("[probe] " + LastResult);
                return;
            }

            Vector2 immediate = GameRefs.GetRotation(mc);
            if (Mathf.Abs(Mathf.DeltaAngle(immediate.x, _expected.x)) > 0.5f)
            {
                LastResult = string.Format(
                    "REJECTED - the write ran but the value did not change (wrote yaw {0:F1}, read {1:F1}). " +
                    "Use Compensate mode.", _expected.x, immediate.x);
                Plugin.Log.LogWarning("[probe] " + LastResult);
                return;
            }

            _phase = Phase.Watching;
            _framesWaited = 0;
            Plugin.Log.LogInfo(string.Format(
                "[probe] wrote yaw {0:F1} -> {1:F1}. Watching 3 frames. DID THE VIEW JUMP?",
                _before.x, _expected.x));
        }

        public static void Tick()
        {
            if (_phase == Phase.Idle) return;

            object mc = GameRefs.GetMovementContext(FreeAimPatches.LocalPlayer);
            if (mc == null) { _phase = Phase.Idle; return; }

            if (++_framesWaited < 3) return;

            float now = GameRefs.GetRotation(mc).x;
            float fromExpected = Mathf.Abs(Mathf.DeltaAngle(now, _expected.x));
            float fromBefore = Mathf.Abs(Mathf.DeltaAngle(now, _before.x));

            if (fromExpected < 2f)
                LastResult = string.Format(
                    "STUCK AND HELD (yaw {0:F1} after 3 frames). Intercept mode is viable.", now);
            else if (fromBefore < 2f)
                LastResult = string.Format(
                    "STUCK THEN LOST (snapped back to {0:F1}). Something rewrites the bearing after " +
                    "our hook - find that writer, or use Compensate mode.", now);
            else
                LastResult = string.Format(
                    "AMBIGUOUS (yaw {0:F1}, wrote {1:F1}, was {2:F1}). Hold the mouse still and retry.",
                    now, _expected.x, _before.x);

            Plugin.Log.LogWarning("[probe] " + LastResult);
            _phase = Phase.Idle;
        }
    }
}
