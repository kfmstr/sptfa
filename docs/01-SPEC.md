# Specification: Bodycam aiming model

Source: hands-on testing in Bodycam by the project owner, September 2026.

Tags used below:

- `[measured]` — directly observed in Bodycam
- `[inferred]` — derived starting value, to be tuned
- `[unknown]` — still needs measuring

---

## 1. The core model

**Gun leads, camera chases.**

1. Mouse delta drives the **gun bearing** directly, 1:1, with no smoothing.
2. A spring continuously rotates the **body/camera bearing** toward the gun bearing.
3. Past a threshold (the cone), mouse movement also pushes the camera directly.
4. The offset always converges back to zero.

This is the opposite of lualeet's SPT mod, which lets the camera move first and
drags the gun behind it.

---

## 2. Aim and view coupling

| Behaviour | Detail | Source |
|---|---|---|
| Gun response | Follows the mouse immediately, no smoothing, 1:1 | `[measured]` |
| Camera response | Rotates toward the gun bearing continuously, fast at first then slowing | `[measured]` |
| Convergence time | About 1 second to exact match | `[measured]` |
| Spring shape | Exponential decay. `lerp(cam, gun, 1 - exp(-k*dt))`, k ≈ 5/s gives ~99% in 1s | `[inferred]` |
| Slow mouse movement | No visible offset builds — camera catches up faster than the mouse moves | `[measured]` |
| At the cone edge | Gun stops advancing, camera is driven by the mouse at the same speed | `[measured]` |
| Fast flick | Cone is **soft**, not a hard clamp. A hard swing lets the gun reach ~90° off body bearing | `[measured]` |
| Hard cap | About 90° | `[measured]` |
| Nominal cone size | **Still to measure.** This is the key tuning number | `[unknown]` |
| Hip vs shouldered | Identical coupling in both. The cone is **not** reduced when aiming | `[measured]` |
| Optics | Red dot / holo always show the gun's true current aim, whenever the glass is visible | `[measured]` |
| Pivot | The gun hinges about the FIRING HAND (grip and trigger), not the shoulder. The buttstock swings away from the body and the body catching up is what reseats it. `07-FINDINGS.md` F12.1 | `[measured]` |

### Pseudocode

```
// every frame
gunYaw   += mouseDeltaX        // 1:1, no damping
gunPitch += mouseDeltaY

offset = gunBearing - bodyBearing

// soft cone: past the threshold the mouse pushes the body too
// pushFactor MUST be < 1. At 1.0 the offset stops growing above the cone and
// the "soft" cone behaves as a hard clamp, contradicting the fast-flick row
// in the table above. See 07-FINDINGS.md F3. Default 0.5.
if (magnitude(offset) > coneSize)
    bodyBearing += mouseDelta * pushFactor

// spring: body always converges on the gun
bodyBearing = lerp(bodyBearing, gunBearing, 1 - exp(-k * dt))

// hard cap
offset = clampMagnitude(offset, 90 degrees)

// apply: rotate the weapon root by `offset` around a shoulder-ish pivot
```

The soft cone is what allows a hard flick to overshoot to 90° while a steady turn
holds at the threshold. Both behaviours fall out of the same two rules — **but
only if `pushFactor < 1`**. The offset a sustained flick reaches is roughly
`mouseSpeed * (1 - pushFactor) / k`. See `07-FINDINGS.md` F3.

---

## 3. Recoil

Recoil is decoupled: the weapon and the camera respond differently.

| Behaviour | Detail | Source |
|---|---|---|
| Hip fire, gun | Jumps on the weapon's own recoil pattern, up plus left/right | `[measured]` |
| Hip fire, camera | Shakes in all directions but is **not** pushed up to match the gun | `[measured]` |
| Hip fire, camera follow factor | 0 on the weapon pattern. Independent multi-axis shake instead | `[inferred]` |
| Shouldered, gun | Same pattern, narrower deviation | `[measured]` |
| Shouldered, camera | Follows the weapon's recoil pattern at reduced amplitude, then the spring returns it | `[measured]` |
| Shouldered, camera follow factor | 0.5 to 0.7 of the weapon pattern | `[inferred]` |

Owner's words on hip fire: *"the gun jumps up and down without me jerking my head
up and down, my overall body is shaking due to the recoil in all different
directions, while the gun is jumping up and left right based on the recoil
pattern the gun has."*

Owner's words on shouldered: *"gun jumps up and down or left and right less
(narrowing the pattern of the recoil) and my body follows the same recoil pattern
but a little bit less, so I see the red dot jumping on my screen up and down, and
then it always gets back where I look."*

---

## 4. Stance gating

> **Correction, `07-FINDINGS.md` F12.2:** "gun up" below is NOT Tarkov's
> shouldered idle. In Bodycam the ready position is a low ready - firing hand
> lowered, buttstock behind the arm near the hip, not in the shoulder pocket.
> The weapon is shouldered only when aiming.

Two states.

**Gun down / slung**
- Free aim off
- Camera behaves exactly like normal Tarkov
- Mouse drives the body directly
- Implemented as a full pass-through, not a scaled offset: the drive loop
  collapses and Intercept writes nothing. See `07-FINDINGS.md` F13.

**Gun up / ready**
- Free aim on, full cone
- Mouse drives the gun, body springs to follow
- Same behaviour whether hip or shouldered

Automatic transitions to handle:
- Sprinting forces the gun down; free aim off, back on when you stop
- Aiming from the down position should raise the gun first
- Firing from the down position should either be blocked or auto-raise (pick one, be consistent)
- Reload, heal, swap and melee should suspend the offset rather than fight the animation

Toggle style: **press to toggle** is the recommended default (this is what Realism
does, and it is easier to live with over long looting stretches). Hold-to-ready is
the alternative. `[unconfirmed with owner]`

---

## 5. Shouldering animation — OUT OF SCOPE for v1

Observed in Bodycam, recorded for completeness:

1. Buttstock connects to the shoulder
2. Gun rotates anticlockwise about its barrel axis until vertical (cant correction)
3. Small left/right stabilisation settle
4. Occasionally, once shouldered, the hands and buttstock reposition — the gun
   shifts then recenters

**Why out of scope:** this is bespoke animation authoring, not a Harmony patch.
Tarkov has its own shouldering animation and replacing it means creating new
animation assets. Different discipline, much longer project. Ship the aiming model
first.

---

## 6. Both-eyes-open optics — CANDIDATE FEATURE, not v1

Aiming a red dot the way it works in life: the housing does not black out the
world, and the reticle floats on the target. Nothing in SPT does this. Nothing on
The Forge comes close. The Tarkov community has requested it since at least 2019
without anyone building it.

Reference photo shows: reticle sharp and on target; the world visible *through*
the housing region, ghosted and blurred; no black tunnel; no vignette ring. The
housing is not transparent glass — it is a ghost, because the other eye sees past
it and the brain fills in.

### Tier 1 — nearly free once free aim exists

- When shouldered with a non-magnified optic, do not move the camera into the sight at all
- Draw the reticle as a screen-space sprite where the gun's aim vector projects onto the screen
- That point is already computed for free aim
- Delivers the real benefit: full peripheral view while aiming, reticle floating on target
- No shader work, no asset bundles
- ~1 to 2 blocks

### Tier 2 — ghost the housing

- Find the optic's renderer at aim time, swap its material to a transparent-capable shader, alpha ~0.4 to 0.5
- Probably no asset bundle needed if a suitable transparent shader is already loaded in the game — check in UnityExplorer first
- Alpha alone, no blur, gets most of the look
- ~2 to 3 blocks

Three risks to spike before committing:
- The housing may share a material with other weapon parts, so this could ghost half the gun
- Transparent rendering brings depth sorting problems
- Tarkov draws the reticle on the lens plane, so it may still be clipped even with a see-through housing. Tier 1's screen-space reticle sidesteps this, which is a good reason to build Tier 1 first regardless

### Tier 3 — near-field blur

Depth of field on the housing. Makes screenshots look photographic, contributes
almost nothing to how the game plays. **Recommend skipping.**

### Balance note

All tiers are a significant combat advantage, since you keep full peripheral
vision while aiming. Fine in single player, but make it a toggle.
