using SPTFreeAim.Compat;

namespace SPTFreeAim.Core
{
    /// <summary>
    /// Owns the gun-up / gun-down bool and the automatic suspensions.
    ///
    /// Deliberately NOT hooked into Realism's EStance (docs/04-DECISIONS.md D6):
    /// Realism also rewrites ballistics, medical and recoil, so depending on it
    /// would force all of that on the user.
    ///
    /// The automatic transitions are the ones in docs/01-SPEC.md section 4:
    /// sprinting forces the gun down; reload, heal, swap and melee suspend the
    /// offset rather than fight the animation.
    ///
    /// Every state read goes through a probe (Compat/BoolProbe.cs) that reports
    /// at startup whether it found the member. An unresolved probe reads false,
    /// which means that one suspension does not fire - visibly wrong, harmless,
    /// and named in the log rather than silent.
    /// </summary>
    public class StanceState
    {
        /// <summary>User's toggle. True = weapon up and ready.</summary>
        public bool UserWantsReady = true;

        public bool SuspendSprinting;
        public bool SuspendAnimation;   // reload, heal, swap, melee
        public bool SuspendStationary;  // mounted weapons, ladders
        public bool SuspendInventory;

        /// <summary>Final answer used by the drive loop.</summary>
        public bool WeaponReady
        {
            get
            {
                return UserWantsReady
                    && !SuspendSprinting
                    && !SuspendAnimation
                    && !SuspendStationary
                    && !SuspendInventory;
            }
        }

        private bool _keyHeldLast;

        public void ReadInput(BepInEx.Configuration.KeyboardShortcut key, bool holdToReady)
        {
            bool down = key.IsDown();
            bool held = key.IsPressed();

            if (holdToReady)
            {
                UserWantsReady = held;
            }
            else if (down)
            {
                UserWantsReady = !UserWantsReady;
            }

            _keyHeldLast = held;
        }

        private float _rmbDownAt = -1f;
        private bool _clearedByAim;

        /// <summary>
        /// Raising the sights from low ready brings the weapon up first.
        ///
        /// Without this, aiming while the weapon is down fights the stance pose:
        /// the game runs its aim animation while the mod is still holding the
        /// muzzle at the floor. The weapon should come up and keep going into the
        /// shoulder, which is one flag, not an animation.
        ///
        /// It has to cooperate with the right-mouse tap, because both gestures
        /// live on the same button. A tap at low ready would otherwise flip twice
        /// - aim clears ready on the press, the tap sets it again on release - and
        /// land back where it started, looking like a dead button. So a press that
        /// cleared ready suppresses its own tap. F66.
        /// </summary>
        public void ReadAimRaises(bool isAiming, bool enabled)
        {
            if (!enabled || !isAiming || !UserWantsReady) return;
            UserWantsReady = false;
            _clearedByAim = true;
        }

        /// <summary>Last right-mouse press duration, in seconds, for the HUD.</summary>
        public float LastRmbHeld;

        /// <summary>
        /// A TAP of the aim button toggles low ready; a HOLD aims, untouched.
        ///
        /// The two gestures live on one button because that is where the hand
        /// already is, and because Tarkov leaves almost no keys free - the owner's
        /// stance key had to be moved off Z after it turned out to be DropBackpack
        /// (F63). Nothing here intercepts or suppresses the button: the game still
        /// sees every press and still aims. We only measure how long it was held
        /// and act on the short ones.
        ///
        /// A tap therefore also produces a brief flick of the sights, which is
        /// unavoidable without stealing the input, and reads as a quick sight
        /// check rather than a bug.
        ///
        /// <paramref name="window"/> of zero is off, the same way every other dial
        /// in this mod treats zero, so there is no separate switch to forget.
        /// </summary>
        public void ReadAimTap(float window, bool allowed)
        {
            if (window <= 0.0001f) { _rmbDownAt = -1f; return; }

            // Blocked while a menu or the inventory owns the mouse, otherwise
            // closing a container would drop the weapon to low ready.
            if (!allowed) { _rmbDownAt = -1f; return; }

            if (UnityEngine.Input.GetMouseButtonDown(1))
            {
                _rmbDownAt = UnityEngine.Time.unscaledTime;
                _clearedByAim = false;
            }

            if (UnityEngine.Input.GetMouseButtonUp(1))
            {
                if (_rmbDownAt >= 0f)
                {
                    LastRmbHeld = UnityEngine.Time.unscaledTime - _rmbDownAt;
                    if (LastRmbHeld <= window && !_clearedByAim) UserWantsReady = !UserWantsReady;
                }
                _rmbDownAt = -1f;
                _clearedByAim = false;
            }
        }

        public bool KeyHeld { get { return _keyHeldLast; } }

        /// <summary>
        /// Read the automatic suspensions from the game. Call once per frame with
        /// the local player; safe with a null player, which clears everything.
        /// </summary>
        public void ReadAutoState(object player, bool respectSprint, bool respectAnimation, bool respectStationary)
        {
            if (player == null)
            {
                SuspendSprinting = SuspendAnimation = SuspendStationary = SuspendInventory = false;
                return;
            }

            object mc = GameRefs.GetMovementContext(player);

            // Sprint. Tarkov lowers the weapon natively when sprinting, so leaving
            // free aim on here means the offset fights the sprint animation.
            SuspendSprinting = respectSprint &&
                (GameRefs.Probe_SprintOnContext.Read(mc) || GameRefs.Probe_SprintOnPlayer.Read(player));

            // Mounted weapons and ladders. Realism special-cases the same state,
            // and it is the one case where the camera is not the player's to move.
            SuspendStationary = respectStationary &&
                GameRefs.GetMovementStateName(mc) == "Stationary";

            // Inventory or stash open: the mouse is driving a cursor, not a gun.
            SuspendInventory = GameRefs.Probe_InventoryOpen.Read(player);

            // Reload, heal, swap, melee. Two independent signals, because the
            // reload flag is the least certain member in the whole mod:
            //   - an explicit reloading flag, if one resolved
            //   - the hands controller not being a firearm controller, which
            //     covers medkits, throwables and melee without naming a member
            SuspendAnimation = respectAnimation &&
                (GameRefs.Probe_Reloading.Read(GameRefs.GetHandsController(player))
                 || GameRefs.HoldingNonFirearm(player));
        }

        public override string ToString()
        {
            if (WeaponReady) return "READY";
            if (SuspendSprinting) return "DOWN (sprint)";
            if (SuspendInventory) return "DOWN (inventory)";
            if (SuspendAnimation) return "DOWN (animation)";
            if (SuspendStationary) return "DOWN (stationary)";
            return "DOWN";
        }
    }
}
