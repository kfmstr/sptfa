using UnityEngine;

namespace SPTFreeAim.Compat
{
    /// <summary>
    /// Makes a per-frame transform modification idempotent.
    ///
    /// THE PROBLEM. Both reference mods modify `WeaponRootAnim` relative to its
    /// current value every frame - `LocalRotateAround` adds to localPosition and
    /// multiplies localRotation. That is only safe if the game re-establishes the
    /// transform from the animation rig each frame, overwriting what we left. If
    /// it does not, every frame stacks on the last and the weapon walks off
    /// screen within a second or two. The residual is not even zero at zero
    /// offset for the general case: LocalRotateAround's two-call cancellation
    /// only holds exactly when the rotation is identity.
    ///
    /// THE FIX. Remember both the value the game gave us (the base) and the value
    /// we left behind. At the start of the next frame:
    ///
    ///   - transform still equals what we left  -> nothing overwrote it, so put
    ///     the base back before applying again
    ///   - transform differs                    -> the game re-established it,
    ///     and this new value is the base
    ///
    /// Correct either way, and it does not need to know which is true. It also
    /// reports which it observed, so the answer gets logged once rather than
    /// guessed at.
    /// </summary>
    public class TransformGuard
    {
        private const float PosEpsilon = 1e-6f;
        private const float RotEpsilon = 1e-6f;

        private readonly string _label;

        private bool _hasLeftover;
        private Vector3 _basePos, _leftPos;
        private Quaternion _baseRot, _leftRot;

        private bool _observed;
        private bool _gameResets;

        public TransformGuard(string label) { _label = label; }

        public string Label { get { return _label; } }
        public bool Observed { get { return _observed; } }

        /// <summary>True when the game re-establishes this transform every frame.</summary>
        public bool GameResets { get { return _gameResets; } }

        /// <summary>
        /// Call before applying anything this frame. Leaves the transform holding
        /// the game's own value, whichever way the game behaves.
        /// </summary>
        public void BeginFrame(Transform t)
        {
            if (t == null) return;

            if (_hasLeftover && Same(t.localPosition, _leftPos) && Same(t.localRotation, _leftRot))
            {
                // Untouched since we wrote it: our own work is still in there.
                t.localPosition = _basePos;
                t.localRotation = _baseRot;
                Observe(false);
            }
            else
            {
                Observe(true);
            }

            _basePos = t.localPosition;
            _baseRot = t.localRotation;
        }

        /// <summary>Call after applying, to record what we left behind.</summary>
        public void EndFrame(Transform t)
        {
            if (t == null) return;
            _leftPos = t.localPosition;
            _leftRot = t.localRotation;
            _hasLeftover = true;
        }

        /// <summary>
        /// Hand the transform back to the game and forget it. Used when the mod is
        /// switched off mid-raid, so the weapon does not stay where we left it.
        /// </summary>
        public void Release(Transform t)
        {
            if (t != null && _hasLeftover
                && Same(t.localPosition, _leftPos) && Same(t.localRotation, _leftRot))
            {
                t.localPosition = _basePos;
                t.localRotation = _baseRot;
            }
            _hasLeftover = false;
        }

        public void Forget() { _hasLeftover = false; }

        private void Observe(bool resets)
        {
            if (!_observed) { _observed = true; _gameResets = resets; }
            else if (resets) { _gameResets = true; }  // one reset is proof; absence is not
        }

        public string Describe()
        {
            if (!_observed) return _label + ": not yet observed";
            return _label + ": game " + (_gameResets ? "re-establishes it each frame"
                                                     : "does NOT reset it (guard is load-bearing)");
        }

        private static bool Same(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < PosEpsilon
                && Mathf.Abs(a.y - b.y) < PosEpsilon
                && Mathf.Abs(a.z - b.z) < PosEpsilon;
        }

        private static bool Same(Quaternion a, Quaternion b)
        {
            return Mathf.Abs(a.x - b.x) < RotEpsilon
                && Mathf.Abs(a.y - b.y) < RotEpsilon
                && Mathf.Abs(a.z - b.z) < RotEpsilon
                && Mathf.Abs(a.w - b.w) < RotEpsilon;
        }
    }
}
