using UnityEngine;

namespace SPTFreeAim.Core
{
    /// <summary>Which local axis runs down the bore. Found by experiment, like the invert toggles.</summary>
    public enum BoreAxis { X = 0, Y = 1, Z = 2 }

    /// <summary>
    /// The points that hold the weapon in space, and the pivot they imply.
    ///
    /// A rifle is not a free-floating object that spins about a single fixed
    /// point. Three contacts constrain it, and they do different jobs
    /// (docs/07-FINDINGS.md F20):
    ///
    ///   LEFT HAND on the handguard  - the driving end. This is what the mouse
    ///                                 moves. It sits on the bore line, which is
    ///                                 also why the roll axis runs through it.
    ///   RIGHT HAND on the pistol grip - braced, nearly still, ready to fight
    ///                                 recoil at any moment. The pivot while the
    ///                                 weapon is not shouldered.
    ///   BUTTSTOCK                   - free at low ready (it sits against the
    ///                                 hip), pinned once the weapon is in the
    ///                                 shoulder. The pivot while aiming.
    ///
    /// So the pivot is not a constant. It slides from the grip to the stock as
    /// the weapon comes up, which is the whole reason a shouldered rifle swings
    /// differently from one held at the ready.
    ///
    /// Only the grip is a free point to tune. The other two are derived from it
    /// along the bore, because their distances are real measurements of a rifle
    /// rather than numbers to find by eye: on a mid-size rifle the buttpad is
    /// roughly 0.3 m behind the pistol grip and the support hand roughly 0.3 m
    /// ahead of it. What does have to be found by experiment is which local axis
    /// runs down the bore - same situation as the invert toggles, and the same
    /// fix: a switch in F12 rather than a constant in the source.
    /// </summary>
    public struct WeaponAnchors
    {
        /// <summary>Right hand on the pistol grip, in the weapon root's local space.</summary>
        public Vector3 Grip;

        /// <summary>Unit vector down the bore, muzzle-ward.</summary>
        public Vector3 Bore;

        /// <summary>Metres from the grip back to the buttpad.</summary>
        public float StockBehind;

        /// <summary>Metres from the grip forward to the support hand.</summary>
        public float LeftHandAhead;

        /// <summary>
        /// How far the braced contact is allowed to give, 0..1.
        ///
        /// Zero is a perfectly rigid brace: the grip (or the stock) does not move
        /// at all and the muzzle swings the whole arc. Real bracing is not rigid,
        /// so the centre of rotation slides a little toward the driving hand.
        /// That is what this does - it is a change of pivot, not an extra
        /// translation, so it cannot walk the weapon off screen the way an
        /// invented offset can (docs/07-FINDINGS.md F5).
        ///
        /// Small values only. At 1 the pivot lands on the support hand and the
        /// gun swings about the wrong end entirely.
        /// </summary>
        public float Leeway;

        public Vector3 LeftHand { get { return Grip + Bore * LeftHandAhead; } }
        public Vector3 Stock { get { return Grip - Bore * StockBehind; } }

        /// <summary>
        /// The pivot for this frame.
        /// </summary>
        /// <param name="shoulderBlend">
        /// 0 = not shouldered, pivot at the grip. 1 = in the shoulder pocket,
        /// pivot at the buttpad. Driven from the aim blend rather than from a
        /// separate setting: "if aiming, the buttstock is always on the shoulder"
        /// is the rule, so there is nothing extra to tune.
        /// </param>
        public Vector3 Pivot(float shoulderBlend)
        {
            Vector3 braced = Vector3.Lerp(Grip, Stock, Mathf.Clamp01(shoulderBlend));
            return Vector3.Lerp(braced, LeftHand, Mathf.Clamp01(Leeway));
        }

        public static Vector3 AxisVector(BoreAxis axis, bool invert)
        {
            float s = invert ? -1f : 1f;
            switch (axis)
            {
                case BoreAxis.X: return new Vector3(s, 0f, 0f);
                case BoreAxis.Z: return new Vector3(0f, 0f, s);
                default: return new Vector3(0f, s, 0f);
            }
        }
    }
}
