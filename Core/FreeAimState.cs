using UnityEngine;

namespace SPTFreeAim.Core
{
    public enum DriveMode
    {
        /// <summary>
        /// Off. The mod resolves refs, logs, and draws the HUD but touches nothing.
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// COMPENSATE - no input interception. The game's own yaw/pitch is treated
        /// as the GUN bearing (mouse 1:1, zero lag - exactly what the spec asks for).
        /// We maintain a lagging body bearing ourselves and rotate the camera back
        /// by -offset, then rotate the weapon forward by +offset. Net result matches
        /// the spec's coupling without needing to intercept anything.
        ///
        /// Try this FIRST. It is the cheapest path to knowing whether the feel is
        /// right, and it does not depend on docs/02-PLAN.md step 03 succeeding.
        /// Known cost: the character's movement bearing tracks the gun rather than
        /// the view, so strafing while offset is skewed by up to the offset angle.
        /// </summary>
        Compensate = 1,

        /// <summary>
        /// INTERCEPT - the design in docs/02-PLAN.md. Reads the yaw/pitch the game
        /// just wrote, derives the mouse delta from it, runs the drive loop, and
        /// writes the body bearing back. Requires MovementContext.Yaw/.Pitch to be
        /// writable AND for the write to land before the camera reads it.
        /// Verify with the F10 write probe before selecting this.
        /// </summary>
        Intercept = 2,

        /// <summary>
        /// REACTIVE - the documented fallback (docs/04-DECISIONS.md D10). The camera
        /// leads and the gun trails. Feels gun-heavy rather than body-heavy. Unlike
        /// lualeet's original this version converges back to zero, because a
        /// non-converging offset was measured as wrong (D4).
        /// </summary>
        Reactive = 3
    }

    /// <summary>
    /// The drive loop. Pure maths - no game types, no Unity components beyond
    /// Vector2. Everything here is unit-testable and everything the game touches
    /// happens in the patches.
    /// </summary>
    public class FreeAimState
    {
        /// <summary>Where the gun points. (x = yaw, y = pitch), degrees.</summary>
        public Vector2 Gun;

        /// <summary>Where the camera/body points.</summary>
        public Vector2 Body;

        /// <summary>Gun minus body, wrapped. This is what gets applied to the weapon.</summary>
        public Vector2 Offset;

        /// <summary>Mouse delta for the frame, derived from the bearing the game reported.</summary>
        public Vector2 MouseDelta;

        /// <summary>0 = free aim fully disengaged (gun down), 1 = fully engaged.</summary>
        public float Gate = 1f;

        /// <summary>Smoothed 0..1 aiming-down-sights blend, for the aim coupling multiplier.</summary>
        public float AimBlend;

        private Vector2 _lastRaw;
        private bool _seeded;

        public void Reset(Vector2 bearing)
        {
            Gun = Body = _lastRaw = bearing;
            Offset = Vector2.zero;
            MouseDelta = Vector2.zero;
            _seeded = true;
        }

        public bool Seeded { get { return _seeded; } }

        /// <summary>
        /// Advance one frame.
        /// </summary>
        /// <param name="raw">Yaw/pitch as currently held by the game this frame.</param>
        /// <param name="dt">Delta time, seconds.</param>
        /// <param name="p">Tuning.</param>
        /// <returns>
        /// The bearing that should be written back to the game, or null when the
        /// mode does not write. Compensate and Reactive never write.
        /// </returns>
        public Vector2? Step(Vector2 raw, float dt, Tuning p)
        {
            if (!_seeded) { Reset(raw); return null; }
            if (dt <= 0f) dt = 1f / 120f;

            MouseDelta = AngleMath.Delta(_lastRaw, raw);
            _lastRaw = raw;

            // Free aim fully disengaged - gun down, sprinting, grenade, knife,
            // reload. The mouse must drive the VIEW directly, exactly like
            // unmodded Tarkov.
            //
            // Scaling the applied offset by the gate is not enough on its own:
            // that only stops the weapon being rotated. The loop underneath keeps
            // running, so the mouse still drives a gun bearing and the body still
            // springs along behind it - a laggy view with no visible gun offset,
            // which is the worst of both. Collapse the loop instead, and in
            // Intercept mode write nothing so the game's own bearing stands.
            //
            // The transition into this state is already smoothed: the gate fades
            // over ~0.25s and DisengageBoost accelerates the spring, so the offset
            // is near zero by the time this branch takes over.
            if (Gate <= 0.001f)
            {
                Gun = Body = raw;
                Offset = Vector2.zero;
                return null;
            }

            // Gate 0 collapses the offset quickly rather than snapping, so lowering
            // the weapon does not teleport the view.
            float k = p.SpringK * (1f + (1f - Gate) * p.DisengageBoost);

            switch (p.Mode)
            {
                case DriveMode.Intercept:
                {
                    // raw == what the game derived from Body(last frame) + mouse.
                    Gun = AngleMath.Wrap180(Gun + MouseDelta);

                    Vector2 off = AngleMath.Delta(Body, Gun);
                    if (off.magnitude > p.ConeDegrees)
                        Body = AngleMath.Wrap180(Body + MouseDelta * p.PushFactor);

                    Body = AngleMath.SpringToward(Body, Gun, k, dt);

                    off = AngleMath.ClampMagnitude(AngleMath.Delta(Body, Gun), p.CapDegrees);
                    Gun = AngleMath.Wrap180(Body + off);   // re-derive so the cap holds
                    Offset = off;

                    _lastRaw = Body; // the game will add next frame's mouse to what we write
                    return Body;
                }

                case DriveMode.Compensate:
                {
                    Gun = raw;

                    Vector2 off = AngleMath.Delta(Body, Gun);
                    if (off.magnitude > p.ConeDegrees)
                        Body = AngleMath.Wrap180(Body + MouseDelta * p.PushFactor);

                    Body = AngleMath.SpringToward(Body, Gun, k, dt);

                    off = AngleMath.Delta(Body, Gun);
                    if (off.magnitude > p.CapDegrees)
                    {
                        off = AngleMath.ClampMagnitude(off, p.CapDegrees);
                        Body = AngleMath.Wrap180(Gun - off);   // cap by dragging the body up
                    }
                    Offset = off;
                    return null;
                }

                case DriveMode.Reactive:
                {
                    Body = raw;                                        // camera leads
                    Gun = AngleMath.SpringToward(Gun, raw, k, dt);     // gun trails, converges
                    Offset = AngleMath.ClampMagnitude(AngleMath.Delta(Body, Gun), p.CapDegrees);
                    return null;
                }

                default:
                    Gun = Body = raw;
                    Offset = Vector2.zero;
                    return null;
            }
        }

        /// <summary>
        /// The offset actually applied to the weapon this frame, after the stance
        /// gate and the aim-down-sights multiplier.
        /// </summary>
        public Vector2 AppliedOffset(Tuning p)
        {
            float aimMul = 1f - ((1f - p.AimCoupling) * AimBlend);
            return Offset * Gate * aimMul;
        }

        public void UpdateAimBlend(bool isAiming, float dt)
        {
            AimBlend = Mathf.Lerp(AimBlend, isAiming ? 1f : 0f, Mathf.Clamp01(dt * 6f));
        }

        public void UpdateGate(bool weaponReady, float dt, float gateSpeed)
        {
            Gate = Mathf.MoveTowards(Gate, weaponReady ? 1f : 0f, gateSpeed * dt);
        }

        /// <summary>Plain snapshot of the tuning values, so the loop never reads config directly.</summary>
        public struct Tuning
        {
            public DriveMode Mode;
            public float ConeDegrees;
            public float CapDegrees;
            public float SpringK;
            public float PushFactor;
            public float AimCoupling;
            public float DisengageBoost;
        }
    }
}
