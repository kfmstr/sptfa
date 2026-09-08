# Findings

Added after the planning handoff. Research, plus a pressure test of the plan
against its own spec. Numbered so they can be cited from the code.

Each finding says what changes as a result. Where something in `docs/01-SPEC.md`
or `docs/02-PLAN.md` is now wrong, it is called out here rather than silently
edited, so the reasoning stays visible.

---

## F1. SPT shut down and relaunched under a new name — SETUP DOC IS STALE

Battlestate served the SP Tarkov team a trademark notice over the "Tarkov" name
in early August 2026. The team announced shutdown, and the original
infrastructure — `sp-tarkov.com`, the installer host, and the original Forge —
went offline on **12 August 2026**.

The project relaunched within two weeks as **Single Player Tushonka**, keeping
the SPT acronym:

| | Old | New |
|---|---|---|
| Site / installer | sp-tarkov.com *(dead)* | sp-tushonka.com |
| Mod platform | The Forge *(dead)* | sp-mod.com |
| Source | sp-tarkov GitHub | github.com/SP-Tushonka |
| Wiki | — | wiki.sp-tushonka.com |

The Forge database was migrated wholesale — mods, listings and accounts carried
over, recoverable with the original email or Discord login.

**SPT 4.1, released 1 August 2026, was the final release from the original
team.** The source is open and development passed to a new maintainer
(Archangel). 4.0 and 4.1 are both live branches.

**What changes:**

- `docs/03-SETUP.md` Phase B points at a dead domain. Get the installer from
  sp-tushonka.com.
- `docs/02-PLAN.md` release notes already name sp-mod.com, which turns out to be
  correct post-migration. The SPT Discord is still the place to ask.
- New standing risk, replacing the old one: the project you are modding for
  changed hands. Version churn is now less predictable, not more. This cuts both
  ways — see F2.

---

## F2. Q8 answered: Battlestate's aiming rework has NOT shipped

The open risk in `docs/04-DECISIONS.md` was that BSG's own aiming rework might
land and make this project redundant.

Timeline as established:

- **11–14 March 2026** — Nikita Buyanov previews an ergonomics rework with
  "realistic aiming". Reception is poor; the loudest community response is to
  ask for optimisation work instead.
- **30 April 2026** — the rework goes to the ETS test servers, bundled with a
  Unity engine transition. Test servers only.
- **3 August 2026** — live patch 1.1.0.0 ships the Seasons system. No aiming
  rework.

So it is still on test servers after four months, tied to an engine transition,
and it was not well received. Nothing suggests it is imminent.

Compounding that: SPT 4.1 was pinned to a pre-rework client and the original
team has dissolved, so even if BSG ships it, the path into an SPT release is now
slower than it was.

**What changes:** Q8 is closed. This is not a reason to delay. Re-check before
starting a port to a future SPT version, not before starting work.

---

## F3. The spec's pseudocode contradicts the spec's own measurements — REAL BUG

`docs/01-SPEC.md` section 2 states two things that cannot both hold with the
pseudocode as written:

> At the cone edge: gun stops advancing, camera is driven by the mouse at the
> same speed `[measured]`
>
> Fast flick: cone is **soft**, not a hard clamp. A hard swing lets the gun reach
> ~90° off body bearing `[measured]`

The first line implies a push factor of 1.0 — past the cone the body gets the
whole mouse delta. But at push 1.0 the offset **stops growing entirely** above
the cone, so it saturates at the cone size no matter how fast you flick. The
cone becomes a hard clamp, which is exactly what the second line says it is not.

This was caught by running the loop against the spec (`SpecTests`, in the
verification harness):

```
push = 1.0    60 deg/s steady turn -> offset  9.59 deg
              900 deg/s hard flick -> offset  9.14 deg   <-- should reach ~90
```

### The fix

Push factor must be **below 1.0**. Above the cone the offset obeys

```
d(offset)/dt = speed * (1 - push) - k * offset
```

so a sustained flick settles at

```
offset_max  ~=  speed * (1 - push) / k
```

At `push = 0.5, k = 5`, a 900 °/s flick reaches ~90°, which is the measured cap,
while a 60 °/s steady turn still parks at the cone because below the threshold
the offset grows at the full mouse rate. All three measured behaviours then hold
simultaneously:

```
push = 0.5    10 deg/s slow turn   -> offset  1.96 deg   (no visible offset)
              60 deg/s steady turn -> offset  9.36 deg   (parks at the cone, 10)
              900 deg/s hard flick -> offset 62.93 deg   (soft, heads for the cap)
              5000 deg/s absurd    -> offset 90.00 deg   (cap holds)
              then stop            -> offset  0.44 deg after 1.0s, ~0 after 3s
```

**What changes:** default push factor is 0.5, not 1.0, and the F12 description
carries the formula so it can be re-derived when the cone number arrives from
Q1. `docs/01-SPEC.md`'s "at the cone edge" row should be read as approximate
behaviour, not as a push factor of 1.

---

## F4. CORRECTED — Compensate is not equivalent, and is not the target feel

> **This finding was wrong in an important way and is corrected below. The
> correction is F11. Read that first.**

## F4 (as originally written). The step 03 feasibility gate can probably be sidestepped entirely

`docs/02-PLAN.md` step 03 is budgeted at 2–4 blocks and framed as the gate the
whole design hangs on: intercept the mouse delta before it reaches
`MovementContext.Yaw`/`.Pitch`.

There is a second formulation that produces the same coupling without
intercepting anything. It falls out of noticing that the game's own yaw/pitch is
already a perfect 1:1 zero-lag mouse follower — which is exactly what the spec
asks the **gun** bearing to be.

**Compensate mode:**

1. Treat the game's yaw/pitch as the **gun** bearing. Nothing to intercept — it
   already tracks the mouse 1:1 with no smoothing.
2. Maintain the lagging **body** bearing yourself, with the same spring, cone and
   cap as the planned design.
3. The game has pointed the camera at the gun bearing, so rotate the camera back
   by `-offset`. The view now sits at the body bearing.
4. The weapon hangs under the camera, so it came back with it. Rotate the weapon
   root forward by `+offset` — the same pivot maths, the same value, as the
   planned design's apply step. The gun lands back on the mouse bearing.

Net result: gun crisp and 1:1, body heavy and springing to follow, offset
converging to zero. The spec's coupling, with no input interception.

The drive loop is **identical** in both modes — same equations, same tuning
values. Only the apply step differs, by one extra camera rotation. Both are
implemented, selectable in F12, and both pass the spec tests.

### What it costs

Be honest about the tradeoff, because it is not free:

- **Movement direction skews.** The character's bearing tracks the gun, not the
  view, so WASD is rotated relative to what you see by up to the current offset.
  At a 10° cone this is barely perceptible; during a 90° flick it is briefly
  strange. In Intercept mode this problem does not exist.
- **Your body faces the gun, not the view.** Visible in shadows and in
  third-person state. Minor in single player.
- **The camera rotation may be overwritten** later in the frame by the
  camera-recoil step. If the view jitters, move the camera write into a postfix
  on that method — the hook is already resolved in `GameRefs`.

### What changes

Step 03 stops being a gate. Build order becomes:

1. Hello world (step 02, unchanged).
2. Compensate mode. Tune the feel. This is the whole core mechanic and it is
   reachable without answering any dnSpy question.
3. Run the write probe (F5) to find out whether Intercept is available.
4. If it is and the movement skew bothers you, switch modes. One config value.

Intercept is still the better mode if it works. The point is that it is no
longer on the critical path, so a failure there costs a config setting instead of
the project.

---

## F5. The write probe answers Q4 in about a minute

Rather than tracing the input path in dnSpy to find out whether
`MovementContext.Yaw` can be driven from a patch, just write to it in a raid and
see what happens. `Debug/YawWriteProbe.cs`, bound to **F10**, does exactly that:
writes `Yaw += 20°`, re-reads over the next three frames, and reports

- **STUCK AND HELD** — Intercept is viable; step 03 collapses to about a block of
  wiring.
- **STUCK THEN LOST** — something recomputes yaw after our hook. This is the one
  case where the dnSpy trace is still worth its budget: you need to find that
  writer.
- **REJECTED** — no usable setter. Use Compensate; do not spend the blocks.

Also note whether the **view visibly jumped**. A write that lands but does not
move the camera means yaw is not what the camera reads from, and the whole
`MovementContext` approach is the wrong tree — worth knowing on day one rather
than after twenty hours.

**What changes:** Q4 moves from "needs dnSpy" to "needs one raid".

---

## F6. Step 07 (recoil decoupling) is cheaper than budgeted

`docs/02-PLAN.md` gives step 07 2–3 blocks and describes it as scaling and
redirecting Tarkov's recoil.

`reference/realism-ProceduralAnimPatches.cs` contains `CamRecoilPatch`, which
already **replaces the camera-follows-weapon step wholesale** — a prefix on
`ProceduralWeaponAnimation.method_19` that returns `false` and writes
`HandsContainer.CameraTransform.localRotation` itself, with separate branches for
aiming and hip fire. Its own comment: *"stop player camera following weapon
muzzle."*

That is structurally the same job as the spec's "hip fire camera follow factor 0,
shouldered 0.5–0.7". The method to patch, the fields to read, and the branch
structure are all in the reference file.

Two caveats:

- `method_19` is an obfuscated name and will move on a client update. It is
  resolved in `GameRefs` and reported at startup, so a rename is one edit.
- Realism's version hard-codes its own behaviour. Copy the structure, not the
  values.

**What changes:** step 07 is closer to 1.5–2 blocks. The config entries exist
(section 5 in F12) and are wired to nothing yet — deliberately, since the core
coupling should be tuned first.

---

## F7. Effort estimate, revised

| Step | Was | Now | Why |
|---|---:|---:|---|
| 01 Setup | 1 | 1 | |
| 02 Hello world | 0.5 | ~0 | Scaffold builds and logs already |
| 03 Intercept look input | 2–4 | 0–1 | F4 removes the gate; F5 answers the question in a raid |
| 04 Gun bearing and spring | 2–3 | 1–2 | Loop written and spec-tested; this is tuning, not building |
| 05 Verify shot direction | 1 (+2–3) | 1 (+2–3) | **Unchanged. Still the top risk** |
| 06 Stance gating | 1–2 | 1 | Toggle, gate and pose written; the auto-transitions are stubbed |
| 07 Recoil decoupling | 2–3 | 1.5–2 | F6 |
| 08 Edge cases | 1–2 | 1–2 | Unchanged |
| **Total** | **10.5–16.5** | **6.5–10** | ~20–30 hours, or 26–39 if the shot ray needs patching |

The reduction is mostly front-loading, not magic: the parts that were cheap to
do without the game are done, so what remains is disproportionately the parts
that need the game in front of you.

**Q5 is untouched and is now clearly the dominant risk.** Test it early — the
plan already says so, and nothing here changes that. If shots do not follow the
barrel, that single item is worth more than everything saved above.

---

## F8. Smaller things the plan does not cover

**Pitch is clamped by the game.** Yaw wraps; pitch does not — Tarkov limits how
far up and down you can look. A pitch offset near the limit will behave
asymmetrically, and in Intercept mode writing a body pitch outside the limit may
be silently clamped, which desynchronises the gun and body bearings. Worth a
targeted test: put the gun at the cone edge while looking near straight up.

**Aiming down sights in Compensate mode.** Tarkov positions the weapon so the
sight lines up with the camera. With the camera lagging, the sight will not be
centred — which is the desired behaviour, and also a direct fight with the game's
own ADS positioning. This is the most likely place for the two systems to
disagree visibly. Test ADS early, not in polish.

**State across raids.** Bearings must reset when the local player changes, or
the first frame of a new raid applies an offset computed from the last one. The
scaffold resets on player change and on master-toggle off.

**Frame-rate independence.** The spring uses `1 - exp(-k*dt)`, which is
frame-rate independent, and that is verified in the harness (30 fps and 240 fps
leave the same residual after one second). A plain `Lerp(a, b, k*dt)` would not
be, and is the usual way this gets written by accident.

**`.vs/` is committed to the repo.** Visual Studio's cache folder, including
`.suo` and Copilot index databases, is in git. Add `.vs/` to `.gitignore` and
`git rm -r --cached .vs`.

---

## F9. What the plan got right

Worth stating, since the above is all corrections.

- **D4, the causation direction, is the pivotal call and it holds.** Everything
  downstream depends on it and it was established by measurement rather than
  inference. Both the drive loop and the Compensate reformulation rest on it.
- **Refusing a runtime dependency on Realism** (D6) is correct and is what makes
  the reference files usable as reference rather than as a dependency.
- **Keeping reflection in one file** is the right response to per-update
  obfuscation, and it survived contact: `Compat/GameRefs.cs` is the only file
  that names an obfuscated member.
- **Flagging Q5 as high risk without a documented fallback cost** was right. It
  is still the thing most likely to add weeks.

---

## F10. Verified against the real assembly — four names were wrong

The scaffold was written blind, from the reference mods' source. It has since
been checked against the actual `Assembly-CSharp.dll` from the SPT 4.1.5 dev
install by reading its metadata directly, and it compiles against the real game
DLLs rather than against stand-ins.

Four things the reference mods' source implies are **not true on this build**.
All four would have failed silently or confusingly.

### F10.1 — `MovementContext.Yaw` and `.Pitch` are READ-ONLY

This is the big one. `docs/02-PLAN.md` step 03 is built on writing them:

> Confirm those are writable from a patch

They are not. Both are computed properties with a getter and no setter:

```
get_Yaw:    ldarg.0 ; call get_Rotation ; ldfld x ; ret
get_Pitch:  ldarg.0 ; call get_Rotation ; ldfld y ; ret
```

**The plan's feasibility gate, taken literally, fails.**

But the design survives, because `Rotation` itself is public and settable, and
`Rotation.x` *is* yaw and `Rotation.y` *is* pitch. Better still, going through
the `Rotation` setter is the *right* way to do it — it runs the game's own
pipeline:

```
set_Rotation:
    SetPreviousRotation(_rotation)
    ClampRotation(value)        <- pitch limits respected, answers half of F8
    SetRotation(clamped)
    _averageRotationX.AddValue(Yaw - PreviousRotation.x)
    ProceduralWeaponAnimation.Pitch = Pitch
    ... hands-to-body angle correction against HANDS_TO_BODY_MAX_ANGLE
```

So Intercept mode is viable after all, and cleaner than planned: write
`Rotation` and the game keeps itself consistent. `SetRotation(Vector2)` also
exists as the raw store, and is kept as a fallback.

**Note that last line.** The game already has a `HANDS_TO_BODY_MAX_ANGLE` and
already rotates the body when the hands exceed it. That is a native version of
the thing this mod is building. Worth reading before tuning the cone — it may
help, and it may fight.

### F10.2 — `HandsContainer` and the transforms are FIELDS, not properties

`pwa.HandsContainer.WeaponRootAnim` reads like a property chain, and both
reference mods write it that way. On 4.1.5 every one of them is a public
**field**:

| Member | Actual |
|---|---|
| `ProceduralWeaponAnimation.HandsContainer` | field, type `PlayerSpring` |
| `PlayerSpring.WeaponRootAnim` | field, `Transform` |
| `PlayerSpring.WeaponRoot` | field, `Transform` |
| `PlayerSpring.CameraTransform` | field, `Transform` |

A `GetProperty` lookup returns null for all four. The mod would have reported
"resolved" and then done nothing, with no error and no clue why.

Fixed by `Compat/Member.cs`, which resolves property-or-field and says which one
it found.

### F10.3 — the camera-recoil method is no longer `method_19`

Realism patches `ProceduralWeaponAnimation.method_19` for its camera recoil
work. On this build that obfuscated name has resolved to a real one:

```
AddHandRecoilRotateToCamera(Single)   public
```

Found by scanning for methods whose body reads `CameraToWeaponAngleStep` —
which is a better way to find it again after the next update than any name.
Both names are tried, real one first.

### F10.4 — `BaseMovementState.Name` is a field

`MovementContext.CurrentState` is a `BaseMovementState`, and its `Name`
(`EPlayerState`) is a field, not a property. Same silent-null failure as F10.2.

### What was confirmed correct

- `EFT.Player`, `EFT.MovementContext`, `EFT.Animations.ProceduralWeaponAnimation`
- `Player.IsYourPlayer`, `.MovementContext`, `.ProceduralWeaponAnimation`, `.HandsController`
- `Player.VisualPass` and `ProceduralWeaponAnimation.AvoidObstacles` — both patch targets exist
- `ProceduralWeaponAnimation.IsAiming`
- `LocalRotateAround` — `TransformTools.LocalRotateAround(Transform, Vector3, Vector3)`, so the pivot maths uses the game's own implementation and lualeet's tuning values carry over
- All four stance probes resolve: `Player.IsSprintEnabled`, `MovementContext.IsSprintEnabled`, `Player.IsInventoryOpened`, `FirearmController.IsInReloadOperation`

### Build

Compiles clean against the real `Assembly-CSharp.dll`, `Comfort`, `BepInEx`,
`0Harmony` and the Unity modules from the dev install.

One reference the upstream template omits is required: **`DissonanceVoip.dll`**.
`EFT.Player` implements `Dissonance.IDissonancePlayer`, so any method taking a
`Player` parameter fails to compile without it. Added to the csproj.

### What this does not tell you

Metadata says a member exists. It does not say the write is honoured at runtime,
that our hook runs at the right point in the frame, or that shots follow the
barrel. `docs/08-RECON.md` session 1 still has to be run in a raid — it is just
much shorter now.


---

## F11. Compensate moves the body, not the view — F4 was wrong

Corrects F4. Found by playing it, which is the only way it could have been
found.

### What F4 claimed

That Compensate mode "produces the same coupling without intercepting
anything", differing only in that "the character's movement bearing tracks the
gun rather than the view, so strafing while offset is skewed".

### What is actually true

In Compensate mode the game's `MovementContext.Rotation` is left alone, tracking
the mouse 1:1. **That value is the body bearing.** So the body turns instantly
with the mouse. The mod then rotates the rendered camera back by the offset.

The gun-to-view angle on screen comes out right. The causation does not. The
body leads and the view is pointed off it — which is the exact inverse of D4,
the measured finding the entire project rests on:

> Bodycam: mouse points the gun 1:1, a spring pulls the camera around to meet
> the gun.

Playing it, the mod reads as "I turn, and the gun follows me". That is not a
tuning problem or a sign error. It is what Compensate does.

### Why the mistake happened

F4 reasoned about what appears on screen — the angle between gun and view —
and treated matching that as matching the mechanic. It is not the same thing.
The spec's claim is about **which bearing is authoritative and which one
chases**, and Compensate inverts that while leaving the on-screen angle intact.

Describing the cost as "movement direction skews" compounded it: that framing
makes it sound like a minor artefact of an otherwise faithful reproduction,
when it is the mechanic being backwards.

### What changes

- **Intercept is the default.** It is the real mechanic: the mouse drives a gun
  bearing nothing else can see, and the lagging body bearing is written into the
  game, so the body genuinely trails.
- **Intercept is confirmed available**, which the original plan doubted. See
  F10.1: `MovementContext.Rotation` is public and settable, and `SetRotation`
  exists. The startup log reports `body bearing drivable (Intercept possible)`.
- **Compensate stays as a fallback**, honestly labelled, for the case where
  Intercept writes do not hold. It is not the target feel.
- The step 03 effort saving in F7 no longer applies the way F4 claimed. Step 03
  is cheap because the write target turned out to be `Rotation` and is
  confirmed writable — not because it could be skipped.

### The lesson worth keeping

Matching what a mechanic looks like is not matching what it does. When the spec
is a statement about causation, only a test that can distinguish causation
counts, and no amount of reasoning about the rendered image is that test.

---

## F12. Four corrections from playing it side by side with Bodycam

Owner observations, September 2026, comparing the first working build against
Bodycam directly. `[measured]` in the sense used by `docs/01-SPEC.md`: observed
in Bodycam, not inferred.

### F12.1 — The pivot is the FIRING HAND, not the shoulder

`docs/02-PLAN.md` says to rotate the weapon

> around a pivot set back from the muzzle, so the gun swings about roughly the
> shoulder rather than spinning about its middle.

Wrong. Owner's words:

> *"the center of rotation is not the shoulder, it is the closest hand on the
> trigger and hand grip. So the buttstock is misplaced from the shoulder, so it
> takes some time for the body to catch up and place the gun on the shoulder."*

The gun hinges about the grip and trigger hand. The **buttstock swings away from
the body**, and the body catching up is what brings the stock back to the
shoulder. That is a different motion from a shoulder-pivot: with a shoulder
pivot the stock stays put and the muzzle sweeps; with a grip pivot the whole
weapon rotates about a point near your hands and the stock travels.

This also explains the *purpose* of the body catching up, which the spec never
articulated: it is not just visual convergence, it is the body reseating the
weapon into the shoulder.

**Consequence:** the pivot cannot be a distance along one axis. `PivotDistance`
(float, along local up) is replaced by `PivotOffset` (Vector3), so the pivot can
be placed at the grip. Default unchanged for now — the real value has to be
found by eye.

### F12.2 — The ready stance is not shouldered

> *"when the player holds the gun, buttstock is not on the shoulder, the hand is
> down and holding the gun with the right hand lowered, and buttstock is going
> behind it between the arm and the hip."*

Bodycam's default weapon-up is a **low ready**: firing hand lowered, buttstock
tucked behind the arm near the hip. Not Tarkov's shouldered idle.

This matters more than it looks. `docs/01-SPEC.md` section 4 defines two states,
"gun down" and "gun up", and treats gun-up as normal Tarkov. It is not — and
much of the felt difference between the two games may come from this rather than
from the aiming model.

Added as an optional ready pose, off by default and blended out by aiming.
Values are zero until measured; guessing them would be noise.

Note this sits next to section 5, "shouldering animation — OUT OF SCOPE for v1".
A static ready pose is cheap; the *transition* into the shoulder is the
expensive animation work that stays out of scope. Worth keeping the line
between them clear.

### F12.3 — Recoil returns the gun to its own origin

> *"when shooting from that position gun is kinda leaving its own life going up,
> and player is trying to control it while body stays on the same stance and
> then after spray gun gets back to its original position."*

Consistent with `01-SPEC.md` section 3 (hip fire: the weapon takes the pattern,
the body does not follow), and adds one thing the spec did not state: **after
the burst the gun returns to where it started**, independent of the body, which
has not moved. Step 07 needs a return-to-origin, not just a reduced follow
factor.

### F12.4 — The gun runs too fast; the body is right

> *"gun runs too fast, the body movements are okay"*

The body spring (k = 5, ~1s convergence) is right — that value was measured, and
it holds up. The gun opening the offset too readily points at the cone or the
push factor, both of which are guesses:

- cone 10° is the `[unknown]` from Q1, never measured
- push 0.5 was derived to satisfy the flick behaviour (F3), not measured

Tune the cone first, since it is the one the spec singles out as deciding
between subtle and disorienting.

### Status

F12.1 is implemented (pivot is now a Vector3). F12.2 is implemented but inert
until values are set. F12.3 is not implemented and belongs to step 07. F12.4 is
tuning.
