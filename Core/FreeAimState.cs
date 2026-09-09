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

        /// <summary>
        /// The weapon's own recoil, in degrees, riding on top of the player-driven
        /// offset. docs/07-FINDINGS.md F12.3: the gun climbs on its own while the
        /// body holds its stance, then returns to where it started.
        ///
        /// Deliberately kept OUT of the coupling maths. The body springs toward
        /// the player-driven Gun bearing only, so recoil never drags the body
        /// around - which is the whole point of the observation. It is added at
        /// the apply step instead.
        ///
        /// The return-to-origin is Tarkov's own: this mirrors a value that already
        /// decays to zero, so there is no second spring to tune or get wrong.
        /// </summary>
        public Vector2 RecoilOffset;

        /// <summary>The raw Vector3 the game exposes, shown on the HUD so the axis mapping can be read off.</summary>
        public Vector3 RecoilRaw;

        /// <summary>0 = free aim fully disengaged (gun down), 1 = fully engaged.</summary>
        public float Gate = 1f;

        /// <summary>Smoothed 0..1 aiming-down-sights blend, for the aim coupling multiplier.</summary>
        public float AimBlend;

        /// <summary>
        /// Roll about the bore, in degrees. The weapon's third rotational freedom,
        /// and the one nothing in the mod touched until now: canting the gun to
        /// use an offset sight, or to keep it controlled close to the shoulder in
        /// a doorway without the receiver filling the view (F20).
        ///
        /// Kept apart from Offset because it is COMMANDED, not driven. The mouse
        /// never produces roll; a deliberate action does.
        /// </summary>
        public float Roll;

        /// <summary>Where the roll is heading. Set by the sight-switch action.</summary>
        public float RollTarget;

        private Vector2 _lastRaw;
        private bool _seeded;

        public void Reset(Vector2 bearing)
        {
            Gun = Body = _lastRaw = bearing;
            Offset = Vector2.zero;
            RecoilOffset = Vector2.zero;
            MouseDelta = Vector2.zero;
            Roll = RollTarget = 0f;
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
                RecoilOffset = Vector2.zero;
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
                    if (off.magnitude > EffectiveCone(off, p))
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
                    if (off.magnitude > EffectiveCone(off, p))
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

            // The aim-coupling multiplier scales the player-driven offset only.
            // Recoil is not a coupling choice - the gun really does climb when
            // shouldered - so it is added afterwards, unscaled.
            Vector2 total = Offset * aimMul + RecoilOffset;

            // Cap the total, so recoil on top of a wide offset cannot exceed the
            // limit the player-driven offset alone respects.
            return AngleMath.ClampMagnitude(total, p.CapDegrees) * Gate;
        }

        /// <summary>
        /// The cone, narrowed on the side where the buttstock would have to swing
        /// through the body.
        ///
        /// At low ready the stock rests against the strong-side hip. Swinging the
        /// muzzle toward that side drives the stock inward, and it runs out of
        /// room - the arm simply cannot take it further. Swinging the other way is
        /// unobstructed, so the cone is not centred on the body.
        ///
        /// This narrows the CONE rather than clamping the offset, which is what
        /// makes it a resistance instead of a wall: a smaller cone means the push
        /// starts earlier, so the body begins turning sooner and the gun eases to
        /// a stop. A clamp would stop the muzzle dead mid-swing, which is not what
        /// running out of shoulder room feels like.
        ///
        /// Fades out as the weapon comes up: once the stock is in the shoulder
        /// pocket there is no hip to hit, and the cone is symmetric again.
        /// </summary>
        public float EffectiveCone(Vector2 off, Tuning p)
        {
            // 1.0 means symmetric. So does 0, which cannot come from the config
            // (its range starts at 0.1) and therefore means the Tuning struct was
            // built without this field - an older call site, or a harness. A
            // default-constructed zero must not silently mean "fully constrained";
            // that is the kind of trap that reads as a bug in the feel and gets
            // hunted in the wrong file.
            if (p.InwardConeScale <= 0f || p.InwardConeScale >= 0.999f) return p.ConeDegrees;

            float mag = off.magnitude;
            if (mag < 0.0001f) return p.ConeDegrees;

            float inward = Mathf.Clamp01(p.StrongSideSign * off.x / mag);
            float shape = inward * (1f - AimBlend);
            return p.ConeDegrees * Mathf.Lerp(1f, p.InwardConeScale, shape);
        }

        /// <summary>
        /// Ease the roll toward its commanded angle. Same exponential form as the
        /// body spring, for the same reason: frame-rate independence.
        /// </summary>
        public void UpdateRoll(float dt, float speed)
        {
            if (dt <= 0f) return;
            Roll += (RollTarget - Roll) * (1f - Mathf.Exp(-speed * dt));
        }

        /// <summary>
        /// Aim blend is owned by StanceState now (it needs it to pick the stance),
        /// and mirrored here because the coupling maths and the recoil patch both
        /// read it. One updater, one source of truth.
        /// </summary>
        public void SetAimBlend(float blend) { AimBlend = Mathf.Clamp01(blend); }

        /// <summary>Kept for the spec harness, which has no StanceState.</summary>
        public void UpdateAimBlend(bool isAiming, float dt)
        {
            AimBlend = Mathf.Lerp(AimBlend, isAiming ? 1f : 0f, Mathf.Clamp01(dt * 6f));
        }

        /// <summary>
        /// Drive the gate toward a continuous coupling target. The stance decides
        /// the target - Down is 0, the weapon-up stances are 1 by default - so
        /// this takes a float rather than a bool.
        /// </summary>
        public void UpdateGate(float couplingTarget, float dt, float gateSpeed)
        {
            Gate = Mathf.MoveTowards(Gate, Mathf.Clamp01(couplingTarget), gateSpeed * dt);
        }

        /// <summary>Kept so the spec harness and any older call sites still read clearly.</summary>
        public void UpdateGate(bool weaponReady, float dt, float gateSpeed)
        {
            UpdateGate(weaponReady ? 1f : 0f, dt, gateSpeed);
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

            /// <summary>
            /// How much of the cone survives on the side the buttstock cannot
            /// swing to. 1 = symmetric, the old behaviour. 0.5 = half the room
            /// swinging inward.
            /// </summary>
            public float InwardConeScale;

            /// <summary>
            /// +1 when swinging the muzzle toward positive yaw drives the stock
            /// into the body, -1 when it is the other way. A right-handed shooter
            /// braces the stock on the right hip, so swinging right is the
            /// constrained direction - but which sign that is on screen depends
            /// on the yaw convention, so it is a switch rather than a constant.
            /// </summary>
            public float StrongSideSign;
        }
    }
}
