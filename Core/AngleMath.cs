using UnityEngine;

namespace SPTFreeAim.Core
{
    /// <summary>
    /// Angle bookkeeping in degrees. Yaw/pitch pairs are treated as a 2D vector
    /// so "the offset" has a single magnitude, which is what the cone and the
    /// hard cap are defined against (docs/01-SPEC.md section 2).
    ///
    /// Wrap/clamp helpers derive from lualeet/sptarkov-deadzone (MIT) via
    /// SPT-Realism-Mod-Client. Credited in the mod description; see CLAUDE.md.
    /// </summary>
    public static class AngleMath
    {
        /// <summary>Wrap a single angle into (-180, 180].</summary>
        public static float Wrap180(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            if (a <= -180f) a += 360f;
            return a;
        }

        public static Vector2 Wrap180(Vector2 v)
            => new Vector2(Wrap180(v.x), Wrap180(v.y));

        /// <summary>Shortest signed delta from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static Vector2 Delta(Vector2 from, Vector2 to)
            => Wrap180(to - from);

        public static Vector2 ClampMagnitude(Vector2 v, float maxDegrees)
            => Vector2.ClampMagnitude(v, maxDegrees);

        /// <summary>
        /// Frame-rate independent exponential approach.
        /// Returns the blend factor for lerp(current, target, f) such that the
        /// gap decays as exp(-k*dt). k = 5/s gives ~99% convergence in 1s, which
        /// is the measured Bodycam figure.
        /// </summary>
        public static float SpringFactor(float k, float dt)
        {
            if (k <= 0f || dt <= 0f) return 0f;
            return 1f - Mathf.Exp(-k * dt);
        }

        /// <summary>
        /// Move <paramref name="current"/> toward <paramref name="target"/> along the
        /// shortest angular path. Wrapping matters: without it a bearing crossing
        /// +-180 makes the camera spin the long way round.
        /// </summary>
        public static Vector2 SpringToward(Vector2 current, Vector2 target, float k, float dt)
            => Wrap180(current + Delta(current, target) * SpringFactor(k, dt));
    }
}
