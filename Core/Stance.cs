using UnityEngine;

namespace SPTFreeAim.Core
{
    /// <summary>
    /// How the weapon is being held.
    ///
    /// This replaces a single "weapon up" bool. docs/07-FINDINGS.md F12.2 is why:
    /// measured in Bodycam, the resting position with the weapon up is NOT
    /// shouldered - the firing hand is lowered and the buttstock sits behind the
    /// arm near the hip. Shouldering is something AIMING does, not the idle.
    ///
    /// A bool cannot express "up, but not in the shoulder", and adding a second
    /// bool just hides the state machine rather than removing it.
    /// </summary>
    public enum Stance
    {
        /// <summary>
        /// Lowered or slung. Free aim off entirely - the mouse drives the view,
        /// exactly like unmodded Tarkov (spec section 4, and F13).
        /// </summary>
        Down = 0,

        /// <summary>
        /// Weapon up, not shouldered. THE DEFAULT, and the Bodycam ready (F12.2).
        /// Full free-aim coupling.
        /// </summary>
        LowReady = 1,

        /// <summary>
        /// Compressed intermediate hold - weapon closer to the body, quicker to
        /// bring up than from low ready. NOT in the Bodycam spec: a deliberate
        /// divergence, off unless bound to a key.
        /// </summary>
        HighReady = 2,

        /// <summary>
        /// In the shoulder pocket. What aiming produces. Full coupling by default
        /// (spec: the cone is not reduced when aiming), adjustable via Q2.
        /// </summary>
        Shouldered = 3
    }

    /// <summary>
    /// What a stance does: where the weapon sits, whether free aim runs, and what
    /// it costs to hold.
    ///
    /// Pose values are offsets from Tarkov's own weapon-up position, applied to
    /// the weapon root and reached over time rather than snapped. They are all
    /// tunable in F12 and all have to be found by eye - see the note on Realism's
    /// constants in reference/REALISM-NOTES.md for why they are not copied from
    /// anywhere.
    /// </summary>
    public struct StanceProfile
    {
        /// <summary>Position offset from the stock weapon-up pose.</summary>
        public Vector3 Pos;

        /// <summary>Rotation offset from the stock weapon-up pose.</summary>
        public Vector3 Rot;

        /// <summary>0 = free aim off (mouse drives the view), 1 = full coupling.</summary>
        public float Coupling;

        /// <summary>
        /// Multiplier on the game's hands-stamina restore rate.
        ///
        /// Tarkov already models arm fatigue as a separate HandsStamina pool that
        /// drains while the weapon is up. Rather than adding a second drain that
        /// double-counts with it, each stance scales how fast that pool comes
        /// back: below 1 recovers slower than stock, above 1 faster.
        ///
        /// This is what gives low ready a reason to exist. Without a cost to
        /// holding the weapon shouldered, nobody would ever lower it and the
        /// stance would be decoration.
        /// </summary>
        public float HandsRecovery;

        /// <summary>How fast the weapon moves into this pose, per second.</summary>
        public float BlendSpeed;
    }
}
