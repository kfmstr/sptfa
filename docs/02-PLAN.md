# Build plan

Effort is expressed in **blocks** of 2 to 3 hours of focused work.

---

## Architecture

Four pieces, run every frame in this order.

### 1. Intercept the look input

Tarkov currently feeds mouse delta straight into the body bearing. Take that delta
before it lands.

**This is the main unknown and the feasibility gate.** Find it in the dnSpy export:
search around `MovementContext` yaw/pitch assignment and the input handling that
drives it.

### 2. Drive the gun

Maintain authoritative `gunYaw` / `gunPitch`. Add the full mouse delta to them,
unmodified. This is what makes the gun feel crisp.

### 3. Drive the camera

Soft cone push + exponential spring toward the gun bearing + hard cap. See
`01-SPEC.md` section 2 for the pseudocode.

### 4. Apply the offset to the weapon

Reuse the existing pivot maths: rotate `HandsContainer.WeaponRootAnim` around a
pivot point.

> **Not the shoulder.** This step originally said "so the gun swings about
> roughly the shoulder". Measured in Bodycam, the pivot is the FIRING HAND -
> grip and trigger - and the buttstock swings away from the body. See
> `07-FINDINGS.md` F12.1. The pivot is a Vector3, not a distance along one axis.

Source: `reference/lualeet-DeadzonePatch.cs` (`ApplyDeadzone`), and the same maths
already working on current SPT in `reference/realism-StanceController-excerpt.cs`
(`ApplyPivotPoint`). Feed it your offset instead of its own accumulated value.

---

## Steps

### 01. Environment setup — 1 block
See `03-SETUP.md`. Ends with a dnSpy export of `Assembly-CSharp.dll`.

### 02. Hello world plugin — 0.5 block
- Start from Jehree's `SPTClientModExamples` template (Use this template on GitHub)
- Rename solution, project, namespace
- Build with F6, confirm a log line reaches the BepInEx console
- Bind one `ConfigEntry<float>`, confirm it appears in the F12 menu in a raid

### 03. Find and intercept the look input — 0 to 1 block — **NO LONGER A GATE**

> **Superseded by `07-FINDINGS.md` F4 and F5.** Compensate mode reaches the same
> coupling without interception, and the F10 probe answers whether Intercept is
> available in about a minute in a raid. Do the probe, then decide. The steps
> below still apply if the probe says "stuck then lost".

- Trace how mouse delta reaches `MovementContext` yaw and pitch
- Confirm those are writable from a patch, or find the upstream source to intercept
- Prove it crudely: patch the input so the camera turns at half rate, and log values
- **Done when:** you can read the mouse delta, suppress its normal effect, and write your own body bearing
- **Risk:** this decides whether the whole design is feasible as a client mod. Do it before anything else that matters.

### 04. Gun bearing and camera spring — 2 to 3 blocks
- Implement the four-piece architecture
- Expose in F12: cone size, spring constant `k`, push factor, hard cap
- Tune against the spec: ~1s convergence, ~90° cap, no offset at slow mouse speeds
- **Done when:** side by side with Bodycam the coupling feels the same

### 05. Verify shots follow the barrel — 1 block — **HIGH RISK**

> Rotating `WeaponRootAnim` moves the *model*. Whether the bullet follows depends
> on whether Tarkov builds the shot direction from the muzzle transform under that
> root, or separately from the camera. Neither reference mod documents this.
> **Test it, do not assume.**

- Raise the cone to something exaggerated (e.g. 20°) so the difference is unmistakable
- Stand still, push the gun to the cone edge, fire at a wall at 10 m
- Is the impact on the muzzle line, or at screen centre?
- If it follows the barrel: done. The spec says the red dot always shows true aim, so this is the behaviour to match.
- If it goes to screen centre: find where the shot ray is built in the firearm controller and apply the offset to it. **Add 2 to 3 blocks.**

### 06. Stance gating — 1 to 2 blocks
- Keybind + bool for gun up vs gun down
- On gun down, apply a lowered pose. Realism's `DoPatrolStance` uses position offset
  `(0.2, 0.025, 0.1)` and rotation `(0.05, -0.05, -0.5)` for rifles, lerped at 5.5/s.
  Good starting values.
- Gate free aim on the bool: full cone when up, zero when down
- Wire the automatic transitions from `01-SPEC.md` section 4
- Still to confirm in dnSpy: whether base 4.1.5 has a native lowered-weapon state, or you define your own

### 07. Recoil decoupling — 1.5 to 2 blocks

> Realism already replaces the camera-follows-weapon step wholesale; the method,
> fields and branch structure are in `reference/realism-ProceduralAnimPatches.cs`
> (`CamRecoilPatch`). See `07-FINDINGS.md` F6.

- Hip fire: weapon takes its full recoil pattern; camera gets independent multi-axis shake, follow factor 0
- Shouldered: weapon pattern narrows; camera follows at 0.5 to 0.7 amplitude, then the spring returns it
- Tarkov already has a recoil system, so this is scaling and redirecting rather than writing one
- The gun returns to its own origin after a burst, independently of the body (`07-FINDINGS.md` F12.3)
- **Done when:** hip fire does not yank the view up, and shouldered fire makes the dot jump and settle

### 08. Edge cases and polish — 1 to 2 blocks
- Weapon clipping into walls (your rotation runs after the obstacle check). Clamp the cone near surfaces, or re-run the check.
- Magnified optics: a 90° cap with a scope is a very large movement at the target. Consider a lower cap for optics even if the cone stays full.
- Prone and leaning: check the pivot with the body rotated
- Per-weapon cone and spring values, keyed on weapon class or weight
- Master on/off key so you can compare instantly

---

## Effort estimate

| Step | Blocks | Note |
|---|---:|---|
| 01 Setup | 1 | |
| 02 Hello world | 0.5 | |
| 03 Intercept look input | 2–4 | Biggest unknown, feasibility gate |
| 04 Gun bearing and spring | 2–3 | The core mechanic |
| 05 Verify shot direction | 1 | +2–3 if the shot ray needs patching |
| 06 Stance gating | 1–2 | |
| 07 Recoil decoupling | 2–3 | |
| 08 Edge cases | 1–2 | |
| **Total** | **10.5–16.5** | ~30–50 hours, or 36–60 if the shot ray needs patching |

**Revised estimate after the scaffold: 6.5–10 blocks, ~20–30 hours.** Breakdown
in `07-FINDINGS.md` F7. The reduction is front-loading, not magic — what remains
is disproportionately the parts that need the game in front of you.

Both-eyes-open optics (see `01-SPEC.md` section 6) adds 1 to 2 blocks for Tier 1,
plus 2 to 3 for Tier 2 if the spike succeeds.

---

## Test checklist per build

- Slow steady mouse turn: no visible offset builds
- Moderate steady turn: gun leads by the cone size, body follows at the same rate
- Hard flick: gun reaches ~90°, body catches up in ~1s, fast then slowing
- Stop moving: body always converges exactly onto the gun bearing
- Hip and shouldered: identical coupling
- Wall test at 10 m with the gun at the cone edge: impact follows the muzzle
- Hip fire burst: view is not yanked upward
- Shouldered burst: dot jumps on the weapon pattern and settles back
- Stance toggle: gun lowers and raises, free aim only when raised
- Offline Woods with bots: hit rate at 15, 30, 50 m against the mod disabled
- Reload, swap, vault, prone, lean, sprint: no animation fighting
- Remove the DLL, start a raid, confirm nothing lingers

---

## Release notes

- Every Tarkov client update re-obfuscates class names. Keep reflection lookups in one file.
- Pin the SPT version range in the plugin metadata.
- Publish on The Forge (sp-mod.com). Ask for testers in the SPT Discord `#mods-development` before public release.
- MIT is the community licence norm for our own release. Credit lualeet's deadzone
  mod for the pivot maths - it is the Unlicense (public domain), so the credit is
  courtesy rather than obligation. See `07-FINDINGS.md` F17.
- Nothing on The Forge currently offers standalone free aim. The closest listed are
  Canted Aiming and Quick Aim, which do different things.
