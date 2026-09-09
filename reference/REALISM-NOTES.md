# Notes on SPT-Realism-Mod-Client's stance and aiming architecture

**No Realism source is included in this repository.** These are notes written
from reading it, describing the shape of the solution, not its code. See
`../CREDITS.md` for why.

Source read: `space-commits/SPT-Realism-Mod-Client`, `Player/StanceController.cs`
(~2,200 lines), `Player/StancePatches.cs` (~1,450), `Weapons/AimingPatches.cs`
(~220), September 2026.

---

## What is worth learning from it

### 1. Stance is a state machine, not a boolean

Realism models stance as a named enum — high ready, low ready, short stock,
active aiming, patrol, melee, a compressed pistol hold — with one per-frame
update that reads input and dispatches to a handler per stance. Rifles and
pistols take separate paths.

Ours is a single `bool UserWantsReady`. That was enough while "gun up" meant
Tarkov's shouldered idle, and it stopped being enough at `docs/07-FINDINGS.md`
F12.2, where the Bodycam ready turned out to be a *third* state: weapon up but
**not** shouldered.

The lesson is not the specific set of stances — most of Realism's are for
mechanics this mod does not have. It is that "up / down" cannot express
"up, but not in the shoulder", and bolting another bool on will not fix that.

### 2. A stance is a pose target, reached over time

Each handler drives the weapon root toward a position offset and a rotation
offset, lerped per frame with a speed multiplier, rather than snapping. Blend
factors carry the weapon between stances instead of switching instantly.

This is the same mechanism `ApplyLoweredPose` and `ApplyReadyPose` already use
here, which is reassuring — the approach was arrived at independently and it
matches. What we lack is the state machine choosing *which* target.

### 3. Gun-to-camera alignment is a PID, not a lerp

Realism runs a PID controller to bring the weapon back into line with the
camera. We use an exponential spring, `1 - exp(-k·dt)`.

Worth noting, not worth copying. Our spring is frame-rate independent, has one
tuning value, and `k = 5` is the one number in this project measured directly
from Bodycam — it survived first contact (F12.4). A PID would add two more
knobs to buy behaviour we have no measurement asking for. If convergence ever
needs to overshoot or settle differently, revisit.

### 4. Stance has consequences beyond the pose

Their stances feed stamina drain, gate or modify aiming, interact with sprint,
shoulder swap and melee, and are guarded by predicates deciding whether a
stance is allowed at all.

We have the guard idea already, as the suspensions in `StanceState`. The rest is
Realism being an overhaul mod. Free aim needs one consequence: which stance is
active decides whether the coupling runs and how strongly.

---

## What NOT to take

**The tuning constants.** Their pose offsets are tuned against their own
animation changes, their ergonomics rewrite and their recoil model. Numbers
lifted out of that context are not "a head start", they are noise that looks
like data — and the values are theirs. F12.2 asks for a pose measured against
Bodycam. That measurement has to be made here, by eye, in this mod.

**The breadth.** Active aiming, short stock, shoulder swap, tac sprint and
melee are an overhaul mod's feature set. `docs/04-DECISIONS.md` D2 narrowed this
project to the decoupling mechanic on purpose, and D8 put shouldering animation
out of scope for v1. A stance system that serves F12.2 needs three states, not
seven.

**The aiming patches.** Realism's aiming work is mostly orthogonal to free aim:
ADS speed from ergonomics, gear blocking ADS, night-vision and face-shield
interactions. Useful for an overhaul, not for this. The one part that overlaps —
stopping the camera being dragged by the weapon — we already do, more narrowly
and with fewer private fields, in `Patches/RecoilPatch.cs` (see F14).

---

## The design this points at for us

Replace the boolean with three states:

| State | Weapon | Free aim | When |
|---|---|---|---|
| `Shouldered` | in the shoulder pocket, as stock Tarkov | full coupling | aiming down sights |
| `LowReady` | up, hand lowered, buttstock behind the arm | full coupling | **default when the weapon is up** — the Bodycam ready, F12.2 |
| `Down` | lowered / slung | off, mouse drives the view | sprint, grenade, knife, reload, stance key |

`LowReady` being the default is the substantive change. Today "gun up" is
Tarkov's shouldered idle plus an optional offset; F12.2 says the resting
position is not shouldered at all, and shouldering is something aiming does.

Everything needed to apply it already exists — the pose lerp, the guards, the
gate. What has to be built is the state machine that picks the target, and the
`Shouldered` blend riding on the existing `AimBlend`.
