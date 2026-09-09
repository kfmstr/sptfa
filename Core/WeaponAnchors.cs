using UnityEngine;

namespace SPTFreeAim.Core
{
    /// <summary>
    /// Which description of the weapon's hinge to use.
    ///
    /// Classic is one fixed pivot point, applied the same way in every stance.
    /// It is less true to how a rifle is held and it is the DEFAULT, because it
    /// is the one whose motion has been watched and approved. Anchors is the
    /// three-contact model from F20, which is the better description and has
    /// not yet earned the default. See docs/07-FINDINGS.md F21.
    /// </summary>
    public enum PivotModel { Classic = 0, Anchors = 1 }

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
        /// How far the braced contact is allowed to give, -0.5..0.5.
        ///
        /// Zero is a perfectly rigid brace: the grip (or the stock) does not move
        /// at all and the muzzle swings the whole arc. Real bracing is not rigid,
        /// so the centre of rotation slides a little toward the driving hand.
        /// That is what this does - it is a change of pivot, not an extra
        /// translation, so it cannot walk the weapon off screen the way an
        /// invented offset can (docs/07-FINDINGS.md F5).
        ///
        /// Small values only. At 1 the pivot lands on the support hand and the
        /// gun swings about the wrong end entirely. Negative is legitimate and
        /// slides it the other way, past the grip toward the buttpad.
        /// </summary>
        public float Leeway;

        /// <summary>
        /// Set when the stock was measured off the weapon rather than derived
        /// from the bore distance. A real buttpad beats an assumed one.
        /// </summary>
        public bool StockMeasured;
        public Vector3 MeasuredStock;

        /// <summary>
        /// True when the bore was measured off the weapon rather than guessed
        /// from a configured axis.
        ///
        /// This gates the derived anchors, and it has to. Deriving a buttpad
        /// 0.3 m along an axis nobody has verified puts the pivot a foot away
        /// from the weapon in some arbitrary direction, and rotating about a
        /// point that far off reads as the gun SLIDING rather than hinging -
        /// which is exactly the regression the owner caught. Without a measured
        /// bore the anchors collapse onto the grip, which is the behaviour that
        /// matched Bodycam for seven commits. F21.
        /// </summary>
        public bool BoreKnown;

        public Vector3 LeftHand
        {
            get { return BoreKnown ? Grip + Bore * LeftHandAhead : Grip; }
        }

        public Vector3 Stock
        {
            get
            {
                if (StockMeasured) return MeasuredStock;
                return BoreKnown ? Grip - Bore * StockBehind : Grip;
            }
        }

        /// <summary>
        /// The weapon's own right and up, perpendicular to the bore.
        ///
        /// This is the part that was missing, and it is why the gun changed
        /// attitude without changing where it pointed (F21). Yaw and pitch have
        /// to turn the weapon about axes ACROSS the barrel. Applying them to the
        /// weapon root's raw local X and Z only lands on those axes by luck, and
        /// on this build it did not: one of them ran along the bore, so part of
        /// every mouse movement rolled the gun instead of aiming it.
        ///
        /// Built from the measured bore, so it is right on any weapon.
        /// </summary>
        public void Frame(out Vector3 right, out Vector3 up)
        {
            Vector3 f = Normalize(Bore);

            // Any reference that is not parallel to the bore will do; the
            // re-orthogonalisation below fixes whatever it was.
            Vector3 hint = Mathf.Abs(f.y) > 0.9f ? new Vector3(0f, 0f, 1f) : new Vector3(0f, 1f, 0f);

            right = Normalize(Cross(hint, f));
            up = Normalize(Cross(f, right));
        }

        private static Vector3 Cross(Vector3 a, Vector3 b)
        {
            return new Vector3(a.y * b.z - a.z * b.y,
                               a.z * b.x - a.x * b.z,
                               a.x * b.y - a.y * b.x);
        }

        private static Vector3 Normalize(Vector3 v)
        {
            float m = v.magnitude;
            return m < 0.0001f ? new Vector3(0f, 1f, 0f) : new Vector3(v.x / m, v.y / m, v.z / m);
        }

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

            // Unclamped on purpose. Vector3.Lerp clamps t to 0..1, which would
            // silently swallow a negative leeway - and negative is meaningful
            // here: it slides the centre of rotation the other way, past the grip
            // toward the buttpad, which braces harder than rigid.
            return new Vector3(
                braced.x + (LeftHand.x - braced.x) * Leeway,
                braced.y + (LeftHand.y - braced.y) * Leeway,
                braced.z + (LeftHand.z - braced.z) * Leeway);
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
