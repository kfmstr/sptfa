using SPTFreeAim.Compat;
using UnityEngine;

namespace SPTFreeAim.Core
{
    /// <summary>
    /// Which stance the weapon is in, what pose that implies, and what it costs.
    ///
    /// This was a single bool - weapon up or down - until docs/07-FINDINGS.md
    /// F12.2 established that the Bodycam ready position is neither: the weapon
    /// is up but NOT shouldered, and shouldering is what aiming does. A bool
    /// cannot express three positions, and a second bool would only hide the
    /// state machine rather than remove it.
    ///
    /// Deliberately not hooked into any other mod's stance system
    /// (docs/04-DECISIONS.md D6, and the licence in F15). The states here are
    /// the ones this mod's spec needs, which is fewer than an overhaul mod wants.
    /// </summary>
    public class StanceState
    {
        // ---- what the player asked for -----------------------------------
        public bool UserWantsDown;      // stance key
        public bool UserWantsHighReady; // optional compressed hold

        // ---- automatic suspensions (spec section 4) -----------------------
        public bool SuspendSprinting;
        public bool SuspendAnimation;   // reload, heal, swap, melee
        public bool SuspendStationary;  // mounted weapons, ladders
        public bool SuspendInventory;

        /// <summary>The stance in force this frame.</summary>
        public Stance Current { get; private set; }

        /// <summary>Smoothed 0..1 aim blend, driven from the game's IsAiming.</summary>
        public float AimBlend { get; private set; }

        /// <summary>Pose actually applied this frame, lerped toward the current stance's.</summary>
        public Vector3 PosePos;
        public Vector3 PoseRot;

        /// <summary>Coupling strength this frame: 0 = free aim off, 1 = full.</summary>
        public float Coupling { get; private set; }

        /// <summary>Hands-stamina restore multiplier for the current stance.</summary>
        public float HandsRecovery { get; private set; } = 1f;

        private bool _keyHeldLast;

        /// <summary>True when anything is forcing the weapon down regardless of the key.</summary>
        public bool Suspended
        {
            get { return SuspendSprinting || SuspendAnimation || SuspendStationary || SuspendInventory; }
        }

        public void ReadInput(BepInEx.Configuration.KeyboardShortcut stanceKey, bool holdToReady,
                              BepInEx.Configuration.KeyboardShortcut highReadyKey, bool highReadyEnabled)
        {
            bool down = stanceKey.IsDown();
            bool held = stanceKey.IsPressed();

            if (holdToReady) UserWantsDown = !held;
            else if (down) UserWantsDown = !UserWantsDown;

            _keyHeldLast = held;

            if (highReadyEnabled && highReadyKey.IsDown())
                UserWantsHighReady = !UserWantsHighReady;
        }

        public bool KeyHeld { get { return _keyHeldLast; } }

        /// <summary>
        /// Read the automatic suspensions from the game. Safe with a null player.
        /// </summary>
        public void ReadAutoState(object player, bool respectSprint, bool respectAnimation, bool respectStationary)
        {
            if (player == null)
            {
                SuspendSprinting = SuspendAnimation = SuspendStationary = SuspendInventory = false;
                return;
            }

            object mc = GameRefs.GetMovementContext(player);

            SuspendSprinting = respectSprint &&
                (GameRefs.Probe_SprintOnContext.Read(mc) || GameRefs.Probe_SprintOnPlayer.Read(player));

            SuspendStationary = respectStationary &&
                GameRefs.GetMovementStateName(mc) == "Stationary";

            SuspendInventory = GameRefs.Probe_InventoryOpen.Read(player);

            SuspendAnimation = respectAnimation &&
                (GameRefs.Probe_Reloading.Read(GameRefs.GetHandsController(player))
                 || GameRefs.HoldingNonFirearm(player));
        }

        /// <summary>
        /// Decide the stance, then move the pose and coupling toward it.
        ///
        /// Order matters: a suspension beats the key, and aiming beats everything
        /// short of a suspension. You cannot be shouldered while sprinting, and
        /// pressing the stance key while aiming should not fight the sights.
        /// </summary>
        public void Update(bool isAiming, float dt, StanceProfiles profiles, bool gatingEnabled)
        {
            AimBlend = Mathf.Lerp(AimBlend, isAiming ? 1f : 0f, Mathf.Clamp01(dt * 6f));

            if (!gatingEnabled) Current = Stance.LowReady;
            else if (Suspended || UserWantsDown) Current = Stance.Down;
            else if (isAiming) Current = Stance.Shouldered;
            else if (UserWantsHighReady) Current = Stance.HighReady;
            else Current = Stance.LowReady;

            StanceProfile p = profiles.For(Current);
            float step = Mathf.Clamp01(p.BlendSpeed * dt);

            PosePos = Vector3.Lerp(PosePos, p.Pos, step);
            PoseRot = Vector3.Lerp(PoseRot, p.Rot, step);
            Coupling = Mathf.Lerp(Coupling, p.Coupling, step);
            HandsRecovery = Mathf.Lerp(HandsRecovery, p.HandsRecovery, step);
        }

        public override string ToString()
        {
            if (SuspendSprinting) return "DOWN (sprint)";
            if (SuspendInventory) return "DOWN (inventory)";
            if (SuspendAnimation) return "DOWN (animation)";
            if (SuspendStationary) return "DOWN (stationary)";
            return Current.ToString().ToUpperInvariant();
        }
    }

    /// <summary>The four profiles, rebuilt from config each frame so F12 edits apply live.</summary>
    public struct StanceProfiles
    {
        public StanceProfile Down, LowReady, HighReady, Shouldered;

        public StanceProfile For(Stance s)
        {
            switch (s)
            {
                case Stance.Down: return Down;
                case Stance.HighReady: return HighReady;
                case Stance.Shouldered: return Shouldered;
                default: return LowReady;
            }
        }
    }
}
