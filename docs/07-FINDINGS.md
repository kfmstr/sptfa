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

All four addressed. F12.1 implemented and CONFIRMED in game - the pivot was the
fix. F12.2 implemented with starting values, opt-in. F12.3 implemented, see F14.
F12.4 is tuning, in progress.


---

## F13. The stance gate only scaled the offset; the loop kept running

Owner, after the first successful pass:

> *"when you run (gun is down) you should control where you look and not the
> gun. So when I press sprint, or toggle sprint, it should be old way, same with
> the nade and knife."*

Correct, and it was a real gap rather than tuning.

`Gate` was applied in `AppliedOffset` only - it scaled the rotation handed to the
weapon. The drive loop underneath kept running regardless: the mouse still drove
`Gun`, `Body` still sprang along behind it, and in Intercept mode `Body` was
still what got written into the game.

So with the weapon down you got a view that lagged your mouse with no visible
gun offset to explain why - the worst of both. `docs/01-SPEC.md` section 4 says
gun-down means "camera behaves exactly like normal Tarkov, mouse drives the body
directly", and scaling the applied offset does not deliver that.

**Fix:** when the gate reaches zero the loop collapses - `Gun = Body = raw`,
offset zero, and in Intercept mode nothing is written back so the game's own
bearing is authoritative. The transition is already smooth because the gate
fades over ~0.25s and `DisengageBoost` accelerates the spring, so the offset is
near zero before the pass-through takes over.

Covered by four checks in `tests/`: nothing written while disengaged, no offset
builds, the view tracks 1:1, and re-engaging does not jump.

**Worth noting the shape of this bug.** The gate was implemented, the
suspensions all resolved, and the HUD read `gate 0.00 DOWN (sprint)` - every
visible indicator said it was working. What was missing was that the gate had
been wired to the output stage and not to the mechanism. A test that only
checked "does the applied offset go to zero" passed the whole time, which is
exactly what `tests/` did before this. The added checks assert on what is
written to the game, not just on what is applied to the weapon.


---

## F14. Recoil already returns to origin - it only needed routing

F12.3 asked for the gun to climb on its own while the body holds its stance, and
to return to where it started afterwards. Half of that turned out to be free.

`EFT.Animations.RecoilProcessBase` exposes, as public fields:

```
Vector3 Current       the recoil rotation right now
Vector3 Velocity
float   ReturnSpeed
float   Damping
bool    StableOn
```

reachable at
`PWA.Shootingg.CurrentRecoilEffect.HandRotationRecoilEffect.Current`.

Tarkov already models recoil as a value that rises on a shot and **decays back
to zero by itself**. The return-to-origin exists and is BSG-tuned per weapon.
There is nothing to reimplement.

What was missing is where it goes. Stock Tarkov expresses much of that rise by
dragging the camera up. F12.3 wants it expressed on the **gun**:

- recoil is added to the gun bearing, so the muzzle climbs
- the body springs toward the **player-driven** bearing only, so it holds its
  stance and is never dragged by recoil
- as the game's own recoil decays, the gun returns on its own

So the implementation is a read and an addition, not a physics model. No second
spring to fight BSG's, and it stays correct when weapon recoil stats change.

### Deliberately outside the coupling maths

`RecoilOffset` is added at the apply step, not inside the drive loop. If it fed
the cone and push logic, firing would push the body around - the exact thing
F12.3 says does not happen. The total is clamped to the hard cap so recoil
stacked on a wide offset cannot exceed the limit the offset alone respects.

The aim-coupling multiplier scales the player-driven offset only. Recoil is not
a coupling preference - the gun really does climb when shouldered - so it is
added unscaled.

### Off by default, and why

The value is a `Vector3` hand rotation and **which component is pitch was never
verified**. Guessing would give recoil that climbs sideways. The HUD now shows
the raw vector beside the derived gun offset, so one burst reads the mapping off
directly: turn it on in F12, fire, watch which component moves.

### Relationship to the camera-follow patch

Two independent halves of step 07, and they compose:

| | What it does |
|---|---|
| `RecoilPatch` (camera follow) | stops the camera being dragged up by the weapon |
| Recoil-to-gun (this) | makes the gun climb, and return, on its own |

The first alone gives a still camera and a gun that barely moves. The second
alone gives a climbing gun with the camera still chasing it. The spec wants
both.

---

## F15. Realism is CC BY-NC-ND — we cannot copy it, and we already published it

Checked while looking at Realism's stance and aiming code with a view to
adopting it.

### The licence

`space-commits/SPT-Realism-Mod-Client` ships `License.txt`: **Creative Commons
Attribution-NonCommercial-NoDerivatives 4.0 International**. No carve-out for
code anywhere in the repo or README.

**NoDerivatives** is the operative term. It permits sharing the work, and
forbids distributing adapted versions of it. Copying its stance or aiming code
into this mod and releasing that mod would distribute an adaptation.

It also forecloses the release plan. `docs/02-PLAN.md` says "MIT is the
community licence norm" — MIT cannot be applied to a work containing ND
material. Taking the code would mean giving up the licence we intended to ship
under, on top of not being permitted.

This sharpens `docs/04-DECISIONS.md` D6, which already said not to depend on
Realism at runtime because it drags in ballistics, medical and recoil. That was
a design argument. The licence is a harder constraint on top of it, and it
extends to source copying, not just runtime dependency.

### What is still fine

Reading it. Copyright restricts copying and adaptation, not comprehension.
Architecture — "stance wants a state machine rather than a boolean", "a pose is
a target reached over time" — is an idea, and an independent implementation of
an idea is not a derivative work.

Facts about *Battlestate's* assembly discovered while reading are not Realism's
to license either, and everything of that kind in this project was re-verified
directly against `Assembly-CSharp.dll` (F10) rather than taken on trust.

What we specifically do **not** take is their tuning constants. Beyond the
licence question, their pose values are tuned against their own animation,
ergonomics and recoil changes; lifted out of that context they are noise
wearing the costume of data. F12.2 needs a pose measured against Bodycam, here.

Notes written from the reading are in `reference/REALISM-NOTES.md`.

### The part that was already wrong

3,343 lines of verbatim Realism source were committed to `reference/` in the
handoff package and **pushed to a public repository** before anyone checked the
licence. No attribution, no licence text.

Removed from the working tree. Copies kept outside the repo, at
`Development/_reference-local/`, so the reference value survives.

**This does not fix it.** Git history still contains the files, and the repo is
public, so they are still being distributed. Actually removing them needs a
history rewrite:

```
git filter-repo --path-glob 'reference/realism-*' --invert-paths
git push --force
```

That rewrites every commit hash. Worth doing before the repo gets attention,
and much cheaper now — this project has one contributor and nine commits.

### The lesson

The handoff bundled reference material without recording where it came from or
under what terms, and it was published without anyone asking. `reference/` now
carries a note per source. A file dropped into a repo "just for reference" is
published the moment the repo is, and licences are cheaper to read before a
push than after.

---

## F16. Realism's tuning constants were in our config defaults

Caught by the owner asking whether anything of Realism's had been committed —
a good question that a check answered better than an assurance would have.

No Realism *code* was ever in the mod. But its numbers were:

| Our setting | Value | Origin |
|---|---|---|
| `LoweredPos` | `(0.2, 0.025, 0.1)` | Realism's rifle patrol-stance position |
| `LoweredRot` | `(0.05, -0.05, -0.5)` | Realism's rifle patrol-stance rotation |
| `LoweredLerpSpeed` | `5.5` | Realism's lerp rate |
| `ReadyPos` / `ReadyRot` | half the above | derived from them |

They arrived in the handoff as `reference/realism-stance-key-values.cs` and were
carried into `FreeAimConfig` defaults. F15 then removed the source files and
`reference/REALISM-NOTES.md` said in as many words not to take their tuning
constants — while the constants sat in the config the whole time. The note and
the code disagreed, and the code was what shipped.

### Two reasons to remove them, and the second is the real one

**Licence.** Weak on its own. Short numeric values are closer to measurements
than to creative expression, and a handful of floats is not the same order of
problem as 3,343 lines of source. Not nothing, but not the argument.

**They are the wrong numbers.** This is the one that matters. Realism's pose
values are tuned against Realism's animation changes, ergonomics rewrite and
recoil model. Ours has none of those. Carried across, they are not a head start
— they are a number that looks like evidence and is not, sitting in the exact
slot F12.2 says must be measured against Bodycam.

That is worse than an empty field, because an empty field is honest about what
nobody has measured yet, and a plausible-looking wrong number is not.

### What changed

All four are now zero, labelled UNMEASURED, and point at F12.2 for what to aim
for. Zero means the pose does nothing until it is tuned, which is a truthful
statement of what we know.

The blend speed is now 6/s, chosen on its own reasoning — about a sixth of a
second to settle, fast enough not to feel sluggish and slow enough to read as a
movement rather than a snap — rather than inherited.

**Existing installs are unaffected.** BepInEx writes config on first run and does
not overwrite it when defaults change, so anything already tuned in
`kfmstr.sptfreeaim.cfg` stays exactly as it is. This only changes what a fresh
install starts from.

### The pattern worth remembering

F15 removed the obvious thing — the files — and declared the problem handled.
The constants had already been copied *out* of those files into code, where
deleting the source did not touch them. Removing a source does not remove what
was taken from it, and the copy that matters is usually the one that has already
been absorbed somewhere else.

---

## F17. lualeet's mod is the Unlicense, not MIT

Every document in this project has said MIT since the handoff — `CLAUDE.md`,
the plan's release notes, the README, `CREDITS.md`, and three source-file
headers. Checked against the repository while adding the licence notice MIT
requires.

`lualeet/sptarkov-deadzone` ships **the Unlicense**: a public-domain dedication.
No attribution requirement, no notice to carry, no conditions of any kind.

Unusually, checking a licence here removed an obligation rather than adding one.
The `reference/` excerpts carry no burden, the planned MIT release is unaffected,
and there is no notice missing.

**The credit stays.** Nothing requires it and it is still correct: this project
exists because that mod worked out the pivot maths first, and F12.1 only found
the pivot was in the wrong *place* because the mechanism was already there to
move. Attribution as courtesy rather than compliance.

### Both licences were assumed, and both assumptions were wrong

Worth putting next to F15. Realism was assumed permissive and is CC BY-NC-ND —
the assumption was too loose, and 3,343 lines went into a public repo. lualeet
was assumed MIT and is public domain — the assumption was too strict, and work
was queued to satisfy a requirement that does not exist.

Neither licence had been read. The handoff asserted both, and the assertions
propagated into eight files across code and docs before anyone opened either
`LICENSE`. Reading two files would have cost a minute at the start.

---

## F18. Stances: a state machine, and what each one costs

Implements F12.2 properly, and picks up the parts of Realism's approach that a
licence cannot restrict (F15) — the shape of the solution, not its code or its
numbers.

### Three positions, not two

`StanceState` was one bool. F12.2 established that the Bodycam ready is a third
position: weapon **up but not shouldered**, with shouldering being something
aiming does. A bool cannot say that, and a second bool would only hide the state
machine rather than remove it.

| Stance | Weapon | Coupling | When |
|---|---|---|---|
| `Down` | lowered | **0** — mouse drives the view (F13) | suspension, or stance key |
| `LowReady` | up, not shouldered | full | **default**, the Bodycam ready |
| `HighReady` | compressed hold | full | optional, off by default |
| `Shouldered` | Tarkov's own pose | full | aiming |

`Shouldered` deliberately carries a **zero** pose offset. The sights are where
Battlestate put them; the mod decides the coupling, not the sight picture.
Anything else would fight the game's ADS alignment for no gain.

Priority is suspension > stance key > aiming > high ready > low ready. You
cannot shoulder a weapon you have lowered, and you cannot shoulder anything
while sprinting.

### The pose lerp moved into the state machine

There were two pose methods, each lerping its own copy toward its own target.
Now there is one target chosen by the stance and one lerp. "Where should the
weapon be" is a single decision in a single place, which is what made
`HighReady` cost almost nothing to add.

### Arm stamina — the owner's idea, and the game already had it

The suggestion was that holding the weapon up should cost stamina, so that low
ready is worth using. Correct, and Tarkov already models it: `PhysicalBase`
carries a **`HandsStamina`** pool separate from the main one, with its own
`HandsCapacity` and `HandsRestoreRate`.

So the implementation is not a new drain — that would double-count with the
game's own. Each stance scales how fast the existing pool **recovers**:
shouldered 0.5x, low ready 1.5x, down 2.5x, from a captured stock value so
nothing compounds frame to frame.

This is what stops low ready being decoration. Without a cost to holding the
weapon shouldered, nobody lowers it and the state never gets used.

Off by default: it shifts stamina balance, which is a Tarkov-realism idea rather
than a Bodycam one — the same class of deliberate divergence as Q2.

Note `Player.Physical` is a **field** of `PhysicalBase`, not a property. Third
time that has bitten (F10.2, F10.4); `Compat/Member.cs` handles it, which is why
it cost nothing this time.

### ADS speed from weapon weight

`PWA.AimingSpeed` is writable and `Item.TotalWeight` is readable, so this is a
read and a scale against a reference weight, softened by a strength factor and
clamped to 0.35x–2x so nothing becomes unusable. Also off by default — it
changes handling across every weapon in the game.

### Tests

`tests/StanceTests.cs`, 17 checks, separate from the spec harness because it
asserts against the machine's own rules rather than against a Bodycam
measurement.

They check what the machine **decides** — which stance, what coupling, what
recovery — rather than what is visible downstream. That is the direct lesson of
F13, where the gate was wired to the output stage, every indicator agreed it
worked, and the test passed because it asserted the wrong layer.

### Still unmeasured

Every pose value is zero. `LowReady` and `HighReady` do nothing visible until
someone tunes them by eye against Bodycam — see F16 for why a plausible-looking
inherited number would be worse than an empty field.

---

## F19. One reflection call took the whole mod down mid-raid

Reported from a live raid: *"for some reason it breaks at some point and then to
fix you need open knife, after that nothing works. Also I couldn't get down
stance, it was always low ready."*

Three separate symptoms, one cause and two design errors behind it.

```
[Error :SPT Free Aim] Free aim frame failed, disabling to avoid log spam:
System.Reflection.AmbiguousMatchException: Ambiguous match found.
  at SPTFreeAim.Compat.Member.Bind (System.Type owner)
  at SPTFreeAim.Compat.GameRefs.GetHeldWeaponWeight (System.Object player)
  at SPTFreeAim.Patches.FreeAimPatches.ApplyStanceConsequences (...)
  at SPTFreeAim.Patches.FreeAimPatches.Frame (...)
[Error :SPT Free Aim] SPT Free Aim disabled for this session.
```

### The trap

`AbstractHandsController` declares `Item Item { get; }`. `FirearmController`
redeclares the same name with a **narrower type**, `Weapon Item { get; }`. Two
declarations, two vtable slots, one name. A whole-hierarchy `GetProperty("Item")`
has no rule for choosing and throws.

Confirmed against the shipped assembly rather than guessed:

```
EFT.Player/FirearmController . Item
  FirearmController          : EFT.InventoryLogic.Weapon   virtual=True newslot=True
  AbstractHandsController    : EFT.InventoryLogic.Item     virtual=True newslot=True
```

The fix is to walk the hierarchy a level at a time with `DeclaredOnly`,
most-derived first, which resolves it the way the C# compiler itself would: the
closest declaration wins. Same change in `BoolProbe`, whose blanket
`catch { continue; }` had been turning the same exception into a silent "member
not found" — worse than a crash, because nothing in the log said why.

**The type matters, not the shadowing.** My first regression test used plain
`new` shadowing with the same type, and it did not throw — Mono resolves that
without complaint. The test passed while guarding nothing. `tests/MemberTests.cs`
now reproduces the narrowed redeclaration and asserts *first* that the lookup
really is ambiguous, so the day a runtime stops throwing, the file says so
instead of quietly going hollow.

### Why the knife "fixed" it, and why nothing worked afterwards

`KnifeController` does not redeclare `Item`, so binding against it succeeded.
The binding was cached behind a one-shot `_weightChainBound` flag, so switching
back to a rifle never rebound and never threw again. The crash stopped — which
is what "open knife to fix it" was.

By then it was too late. `EmergencyDisable` set a flag that nothing cleared for
the rest of the session, so the master toggle looked dead and the only recovery
was leaving the raid. Hence "after that nothing works".

And with the frame path dead, `StanceState.Update` stopped running, so the HUD
kept displaying the last stance it had computed. The stance key was working
fine; nothing was reading it. That is the "always low ready" report — a frozen
readout, not a broken state machine.

### Three fixes, because there were three failures

1. **`Member.Bind` and `BoolProbe.TryResolve` walk the hierarchy themselves.**
   The actual bug.
2. **`ApplyStanceConsequences` is wrapped on its own.** Arm stamina and
   weight-scaled ADS are optional extras. An exception in either now switches
   those two off and lets the coupling — the entire point of the mod — carry on.
   Optional features must not be able to kill required ones.
3. **`EmergencyDisable` is recoverable.** Toggling F8 clears it and retries. A
   permanent-until-restart failure mode is only acceptable if the failure is
   permanent, and this one was not.

Also: the weight chain now caches against the types it was bound to rather than
a boolean. A rifle resolves `ContainerCollection.TotalWeight` and a grenade
resolves `Item.TotalWeight`; reusing the first binding on the second item type
would invoke a property the target does not have. The old flag would have done
exactly that.

### The lesson worth keeping

An unhandled exception in a per-frame patch does not report itself as one
failure. It reports as every downstream symptom at once — a frozen HUD, a dead
key, a toggle that does nothing — and each of those invites a different wrong
diagnosis. When several unrelated things stop at the same moment, read the log
before believing any of them.

Second: I had the evidence and misread it. An earlier Cecil probe printed
`TotalWeight` twice, I took that for the ambiguous member, and wrote a test
around it. `TotalWeight` is `Single` on both declarations and resolves fine.
Printing a probe's output is not the same as reading it.

---

## F20. The pivot is not a point, it is whatever is bracing the weapon

The owner's correction, and it retires two earlier half-answers at once:

> we have 5 dimensions of freedom, one left hand grip, second right hand grip,
> 3rd butstock back (in case it is fixed on the shoulder), then one rotation
> dimension around right hand grip (going through pistolet grip) in low ready,
> and shoulder in aiming, and another rotation dimensions going through the
> barrel of the gun. Mouse moves left hand, stance defines where butstock and
> right hand almost not moving (we can have a bit of the leeway when turning),
> if aiming butstock is always on the shoulder

`docs/02-PLAN.md` said pivot about the shoulder. F12 corrected it to the firing
hand. Both were right, for different stances, and neither was general: a rifle
is not a free body spinning about one fixed point. Three contacts constrain it,
and they do different jobs.

| Contact | Role |
|---|---|
| Left hand, handguard | The driving end. This is what the mouse moves. |
| Right hand, pistol grip | Braced, nearly still, ready to fight recoil at any moment. The pivot while the weapon is up but not shouldered. |
| Buttstock | Against the hip at low ready; pinned to the shoulder when aiming. The pivot once it is in the pocket. |

So the pivot **slides from the grip to the buttpad as the weapon comes up**, and
that is why a shouldered rifle swings differently from one held at the ready. It
is not a preference, so it is not a setting: the blend is the aim blend, because
"if aiming, the buttstock is always on the shoulder" is a rule.

### Two of the three anchors are measurements, not tuning

Only the grip is a free point to find by eye. The other two follow from it along
the bore, and their distances are facts about a rifle rather than numbers to
hunt: about 0.3 m from the pistol grip back to the buttpad, about 0.3 m forward
to the support hand. Shorter on a folded stock or a stubby handguard.

What genuinely cannot be derived is **which local axis runs down the bore**.
lualeet's mapping puts pitch on X and yaw on Z, which leaves Y - but that is an
inference from someone else's axis convention, not a measurement, so it is a
switch in F12 and not a constant in the source. Same treatment as the invert
toggles, for the same reason, and F16 is why guessing would be worse than asking.

### Leeway: braced is not rigid

"almost not moving (we can have a bit of the leeway when turning)". A perfectly
rigid brace means the grip does not move at all and the muzzle swings the whole
arc, which reads as mechanical. Real bracing gives a little.

Implemented as a small slide of the pivot toward the driving hand - a change of
**pivot**, not an added translation. That distinction matters: an invented
translation along axes nobody has verified is exactly how the weapon walked off
screen in F5. Moving the pivot cannot do that, because the rotation is already
cancelled by the paired `LocalRotateAround` call.

### The hip is why the cone is not centred on you

At low ready the stock rests against the strong-side hip. Swinging the muzzle
that way drives the stock inward until the arm runs out of room; swinging the
other way is unobstructed.

So the cone is asymmetric - and it is implemented by narrowing the CONE rather
than clamping the offset. A clamp stops the muzzle dead mid-swing, which is not
what running out of shoulder room feels like. A smaller cone makes the push
start earlier, so the body begins turning sooner and the gun eases to a stop.
The constraint fades out with the aim blend: once the stock is in the pocket
there is no hip to hit.

Yaw only. The hip constrains the horizontal swing, not elevation, and the tests
assert that pure pitch is untouched.

### Roll: the freedom nothing was using

The third rotational axis runs down the bore. It earns its place twice over -
canting to a 45 degree offset optic, and holding the weapon tipped in a doorway
so it stays controlled against the shoulder without the receiver filling the
view. The owner wanted it on the **change-sight key**, and to work whether or
not a canted sight is fitted.

That is a Harmony postfix on `FirearmController.ChangeAimingMode()`, not a
keybind of our own: the player already has that key bound and already reaches
for it. There are two overloads, so the lookup names the signature -
`GetMethod(name, flags, null, Type.EmptyTypes, null)` - which makes ambiguity
impossible rather than merely survivable. F19 is the reason that is worth
spelling out.

Rolled about the **support hand**, because that is where the bore line is held.
Rolling about the grip would swing the muzzle sideways as well, which is not
what tipping a rifle over does.

### The lesson worth keeping

I offered three options for the pivot and the owner's answer began "I don't
understand what you wrote in either one of these options". He was right to say
so. I had asked which interpolation scheme to use; he was describing the
mechanism, in terms of hands and contact points. The options were phrased in the
vocabulary of the implementation rather than the thing being modelled, so they
were unanswerable even though the underlying question was reasonable.

When someone describes a physical system, the question worth asking is about the
system. `AnchorGrip`, `StockBehindGrip` and `LeftHandAheadOfGrip` are named after
what they are for the same reason.

---

## F21. I broke a working mechanic and then "fixed" the wrong thing

The owner, after F20 shipped:

> right now gun is not rotating around my right arm when I am on low ready, the
> line that goes throigh the barrel towards the end of the tip of the gun is
> always perpendicular to axis of my body. the left hand should lead and give the
> gun the angle left right and up and down, like the right hand is the fixed point

I read that as a bug in the rotation axes. The apply step had used lualeet's
mapping since the beginning:

```csharp
GameRefs.LocalRotateAround(root, pivot, new Vector3(pitch, 0f, yaw));
```

Pitch on the weapon root's local X, yaw on its local Z. I reasoned that nothing
guarantees those axes lie across the bore, that one of them probably ran along
the barrel, and that a share of every mouse movement was therefore rolling the
weapon rather than pointing it. I rewrote it to build the turn axes from a
measured bore, wrote the finding up as fact, and shipped it.

Then he said: *"You had it in the early versions and it was moving exactly as at
body cam, then two three commits back you changed it."*

He was right. The apply step is byte-identical from `514b815` - the commit whose
verdict was "yes, that is right, it was the pivot. super." - through `42b74a5`:

```
514b815  8fd4a4162ee4e5b0cebd5e7033cec770
1e5f52c  8fd4a4162ee4e5b0cebd5e7033cec770
58c19a1  8fd4a4162ee4e5b0cebd5e7033cec770
c66672b  8fd4a4162ee4e5b0cebd5e7033cec770
9700140  8fd4a4162ee4e5b0cebd5e7033cec770
04c8000  8fd4a4162ee4e5b0cebd5e7033cec770
42b74a5  8fd4a4162ee4e5b0cebd5e7033cec770
```

Seven commits of Bodycam-correct motion out of the mapping I declared broken.
Whatever the weapon root's local X and Z are, they were working.

### What actually broke it

`df988c5`, the F20 anchors commit, and not through the axes at all. It changed
one pivot into three derived anchors:

```
LeftHand = Grip + Bore * 0.30
Stock    = Grip - Bore * 0.30
```

with `Bore` defaulting to a **guessed** local Y, because I had made the bore axis
a dropdown for the user to find by experiment. Guess the axis wrong and the
buttpad lands 0.3 m away from the weapon in an arbitrary direction. Rotating
about a point that far off is barely a rotation at all: over the small angles
involved it is almost pure translation. The gun slides, the barrel holds its
angle to the body, and nothing appears to hinge.

The symptom he described was precise and it was the pivot, exactly as in F12. I
went looking in the axes because I had just finished convincing myself the axes
were suspect.

### The fix, and what it is not

1. **The original mapping is restored as the default.** The bore-frame version
   is still there, behind `Turn about the measured bore`, off. It may yet be
   better on a weapon whose root is oriented oddly. It does not get to be the
   default again without someone watching the gun move.
2. **Derived anchors require a measured bore.** `WeaponAnchors.BoreKnown` gates
   them: without a measurement the support hand and buttpad collapse onto the
   grip, so the pivot is exactly where it was before F20 existed. A guessed axis
   can no longer move the pivot at all.
3. **Everything F20 added is now off by default** - leeway 0, inward cone scale
   1.0 - so the shipped feel is the approved one and each new mechanic is opt-in.

`WeaponGeometry` survives and is worth keeping. `mod_align_rear` to
`mod_align_front` really is the bore, measured off the weapon in hand, and it
removes the guess that caused this. It just is not licence to rewrite a working
rotation.

### The lessons, and there are three

**Check whether it ever worked before deciding it is broken.** One `git log -p`
on the apply step would have shown seven commits of stability and sent me
straight to `df988c5`. The whole session has run on "verify against the
assembly"; the same discipline applies to the repository, and I did not apply it.

**A plausible mechanism is not a diagnosis.** The axis argument was sound in the
abstract and I wrote it into the findings as established fact, complete with a
confident lesson about inherited axis conventions. It was a hypothesis I had not
tested, presented as a conclusion. That is worse than being wrong quietly,
because the docs are what the next person trusts.

**A new default is a change to something that already works.** F20 shipped four
new behaviours all switched on. When the feel changed there were four candidates
and no way to bisect. The mod's own convention already had the answer - stamina
and weight-scaled ADS are off by default, and the reason given was that they
change balance. The same reasoning applies to anything that alters the motion:
new mechanics go in off, and get turned on one at a time by someone watching.


---

## F22. The rotation was never a rotation about a point

After the rollback, the owner again:

> The version you got me is already moving gun so that barrel of the gun in the
> plane that is perpendicular to my body access, it never rotates, there is not
> axis of rotation. what I needed is that front hand moves (left), and right hand
> holds (with small leeway), so that the gun actually turn around the hand grip
> following the left hand, and left hand is always bounded to the link between
> its grip and the grip of the right hand and gun is solid object in between.

So the rollback did not restore it either. Every version back to the first
commit has the same apply step, and none of them ever did what he is describing.
This is not a regression at all. It never worked.

### What the call actually does

Every build has ended in the same line, inherited from lualeet:

```csharp
GameRefs.LocalRotateAround(root, pivot, new Vector3(pitch, 0f, yaw));
```

I had assumed `TransformTools.LocalRotateAround(Transform, Vector3 center,
Vector3 eulerRotation)` rotates by those euler angles about that centre, in the
transform's local space. The IL says otherwise:

```
v = t.parent.TransformDirection(eulerRotation)     // the euler VECTOR, as a direction
v = t.InverseTransformDirection(v)                 // re-expressed in t's local axes
q = Quaternion.Euler(v)                            // only now are they angles
localPosition += localRotation*center + q*(-center)
localRotation *= q
```

The argument is carried through two frames as a **direction** before it is
treated as **angles**. Which way the weapon turns therefore depends on how it
happens to sit relative to its parent, and a component of every mouse movement
can land along the barrel, where it rolls the gun instead of aiming it. When
the pitch component maps onto the bore, pitch disappears entirely and the barrel
stays in one plane - exactly the report.

Nothing in the parameter names says this. Reading the IL is what says it, and I
should have read it the first time I called into the game's code rather than the
fourth.

### What replaces it

`HingeAboutGrip` does the plain thing, in world space:

```csharp
Vector3 pivot = cam.TransformPoint(cfg.GripFromEye.Value);
Quaternion q = Quaternion.AngleAxis(yaw, Vector3.up)
             * Quaternion.AngleAxis(-pitch, cam.right);
root.position = pivot + q * (root.position - pivot);
root.rotation = q * root.rotation;
```

A rigid body turned about a point. Yaw about the world vertical, pitch about the
camera's right - the axes those two words mean. No frame juggling, so pitch
cannot vanish, no component can land along the barrel, and there is no per-weapon
axis to discover.

There is also no second cancelling call. The old code needed one because
`LocalRotateAround` displaced the transform as a side effect; this does not
displace anything, so there is nothing to undo.

### The pivot is expressed where a person can reason about it

`Grip from eye` is right / up / forward from the camera, in metres. A hand's
width to the right, most of a forearm below the eye, a little in front. Those are
numbers you can check against your own body while sitting at the desk.

The old `Pivot offset` was a point in the weapon root's local space, whose axes
and scale nobody had established - which is why tuning it by eye never converged
across three attempts, and why "find the bore axis by experiment" was a bad ask.

### The lesson worth keeping

The one call at the centre of the whole mechanic was the one piece of game code I
never verified. I checked member names with Cecil from day one, wrote a finding
about how much that discipline pays, and then took a three-argument method's
behaviour on faith for the entire project because its parameters were named
plainly and someone else's mod used it the same way.

Inherited code is not verified code. `center` and `eulerRotation` are honest
names for arguments that are not used the way those names imply, and no amount
of staring at call sites would have shown it. The IL was forty lines.

---

## F23. It did work, and the stance poses are what broke it

The owner, after I told him the mechanic had never worked:

> Are you telling me my eyes were wrong?! it was working from your first version,
> it was perfect. But then you start adding shit and it broke

No. His eyes were right, and F22's conclusion was half wrong. The call is
strange, and everything F22 says about what `LocalRotateAround` does is accurate.
But "it never worked" does not follow from that, and I should not have said it.

### The coupling

Two methods, two different transforms:

```
ApplyWeaponOffset  ->  LocalRotateAround(WeaponRootAnim, pivot, euler)
ApplyLoweredPose   ->  WeaponRoot.localRotation *= add
ApplyReadyPose     ->  WeaponRoot.localRotation *= add
```

`WeaponRootAnim` sits under `WeaponRoot`. And `LocalRotateAround` begins:

```csharp
v = t.parent.TransformDirection(eulerRotation);
```

So the pose rotations write the very frame the offset is interpreted in. Turning
`WeaponRoot` by any amount silently re-aims every offset the mouse produces, in
proportion to the pose rotation, every frame.

With the pose rotations at zero - which is where they were in the early builds -
the parent frame is untouched and the call behaves exactly as lualeet intended.
That is the version he approved, and it was genuinely correct. The moment stance
poses arrived carrying a rotation, the mapping started drifting. His config has
`Lowered rotation offset = (0.2, 0, 0)`, so it was drifting for him.

Position offsets on the parent are harmless: `TransformDirection` reads rotation
and scale, not translation. Only the rotation is poison.

### The fix

The legacy hinge is the default again, and pose ROTATIONS are suppressed while it
is selected, with a one-time warning saying why. Position poses still apply, so
the weapon still visibly lowers. `Around Grip` does not read the parent frame at
all, so it can have pose rotations back.

`ReportParentageOnce` logs whether `WeaponRootAnim` really is a descendant of
`WeaponRoot`, and the HUD shows it. F23 turns on that being true, and checking is
one loop rather than an assumption - which is the habit I failed to apply to
`LocalRotateAround` in the first place.

### The lesson worth keeping, and it is not a technical one

I had the right facts in F22 and drew a conclusion from them that contradicted
what the user had watched with his own eyes, in his own game, across several
builds. The correct response to "your explanation does not match what I saw" is
to find the thing that reconciles both, because the observation is data and my
model is a guess. Instead I told him the observation was wrong.

He was right four times in this project - F11, F12, F20 and now this - each time
from watching the gun move. My model of the code has been wrong more often than
his eyes have. That should have been priced in long before he had to raise his
voice about it.

A second, smaller one: a change is not additive just because it only touches new
code paths. `ApplyLoweredPose` never went near `ApplyWeaponOffset`. It shared a
transform with it, three nodes up, and that was enough.

---

## F24. One axis right, the other backwards - the toggles were in the wrong place

> horizontal and vertical mouse movement works differently, if you select
> intercept or reactive, it is one on the horizontal and oposite on the vertical

`InvertYaw`, `InvertPitch` and `SwapAxes` were read in exactly one place:
`ApplyWeaponOffset`. They rotate the WEAPON and nothing else.

Everything upstream of that - the drive loop, the body bearing written back in
Intercept, the camera counter-rotation in Compensate - kept the game's own sign
convention. Tarkov counts pitch downward. So the two axes were never consistent
with each other, and the only control offered to fix it made things worse: flip
`Invert pitch` and the gun's picture comes right while the body bearing and the
camera stay on the old sign, so the gun now points somewhere the body does not
agree with.

That is a control that appears to work and quietly breaks the coupling, which is
the same shape of error as F13.

### The fix

Normalise the sign where the bearing is READ, and undo it on write-back:

```csharp
float pitchSign = cfg.InvertGamePitch.Value ? -1f : 1f;
Vector2 raw = new Vector2(GameRefs.GetYaw(mc), GameRefs.GetPitch(mc) * pitchSign);
...
Vector2 back = new Vector2(writeBack.Value.x, writeBack.Value.y * pitchSign);
GameRefs.SetRotation(mc, back);
```

One sign, applied once, before anything consumes the bearing. The loop, the
weapon, the camera and the body all see the same convention, so the two axes
behave the same way in every drive mode.

`Invert pitch` and `Invert yaw` stay, relabelled as cosmetic, because rotating
only the weapon is a legitimate thing to want and an illegitimate way to correct
a sign error.

### And the pivot dial came back

> I had a chance to set where is the rotation axis on the gun, moving by the gun
> length or something, I remember -0.15 gave me exact position of the gun grip

Correct, and it was in the first four commits: `PivotDistance`, a single float,
used as `Vector3.up * pivot`. `514b815` replaced it with a Vector3 on the
reasoning that one axis could not reach the grip. The owner had already found
that it could - **-0.15** put the hinge on the pistol grip - and the replacement
threw away a working, one-dimensional, tunable dial in favour of three numbers
whose axes nobody had established. Every pivot problem since has been a search
through that three-dimensional space for a point he had already found on a line.

`Pivot distance (m)` is back, default -0.15. `Pivot fine offset` is a Vector3 on
top, zero by default, for the case where the grip genuinely is off that line.

Both are NEW config keys on purpose. The stale `Pivot offset = (0.2, 0.1, 0)` and
`Pivot distance = 0.1` in his config are from the old semantics and would have
silently overridden the defaults with values tuned against different maths.

### The lesson worth keeping

Twice now the answer was "he already had it and I replaced it": the pivot dial
here, and the rotation mapping in F23. Both replacements were argued from what
the code could not do in principle. Neither checked whether it was already doing
it in practice.

When a user says a specific number worked, that number is a measurement of the
system. It is worth more than an argument about what the system ought to need.

---

## F25. Arms that tire, and both eyes open

Two requests, both landing on things the game already models, which is the
cheapest kind of feature to add and the most likely to behave.

### Arms tire while the weapon is up

> please add the slow hand drain when you move your gun from lowering position,
> as it physically impossible to hold your gun all the time like that

`PhysicalBase` carries **two** stamina pools:

```
PhysicalBase.Stamina       : Stamina    // the main bar
PhysicalBase.HandsStamina  : Stamina    // arms, separate, drives sway
```

So this is not a new meter. Draining the pool the game already has means its own
consequences - sway, the exhausted state, the recovery curve - follow for free,
and nothing has to be kept in step with anything.

One trap, found by reading the IL rather than the signature:

```csharp
Stamina.UpdateStamina(float stamina)
{
    if (Math.Abs(Current - stamina) < 1f) return;   // <- deadband
    Current = stamina;
    ...
}
```

A slow drain is by definition smaller than 1.0 per frame, so every write through
that method would have been discarded and the feature would have done nothing at
all while appearing wired up correctly. `Stamina.Current` is a public field, so
the drain is accumulated locally and written straight to it once it reaches a
whole unit. `Exhausted` is computed from `Current`, so the game still reacts.

Scaled by the gate rather than the raw stance flag - a weapon on its way down is
already costing less - and multiplied while actually shouldered, which is what
gives the ready position a reason to exist.

### Both eyes open

The link was a Reddit thread I could not read; the domain is blocked for me. So
this implements the common reading of it, and if a different one was meant, it is
one field either way.

Tarkov narrows the field of view when the weapon comes into the shoulder. That
reads as closing one eye and tunnelling onto the sight. A shooter with both eyes
open keeps the room around the sight.

```
EFT.CameraControl.CameraManager.AimDeltaFov : Single   // public STATIC field
```

The whole feature is scaling that by the aim blend. Nothing else about the sight
picture is touched - the optic, the reticle and the alignment stay exactly where
the game puts them. The honest cost is that on magnified optics the narrowing IS
the zoom, so removing it removes the magnification; `Both eyes strength` at 0.5
keeps half of each.

`AimDeltaFov` being **static** is the part worth remembering. A leftover value
does not end with the raid - it follows the player into the next one, and into
the main menu. The stock value is captured once and handed back whenever the
feature is switched off or the mod releases, and `Remove()` releases it too.

### Both are isolated

Each runs in its own try/catch and disables only itself on failure, per F19.
Neither can take the coupling down, which is the only part of this mod that has
to work.

---

## F26. Both eyes open is about the HOUSING, not the field of view

The thread the owner linked, once he sent it as a PDF because Reddit is blocked
for every tool I have:

> Shooting with both eyes open should make the housing on your optic
> transparent. Some of the optics (i.e. Eotechs, Aimpoint Micros) are hard to use
> in Tarkov because your vision is so obstructed.

I had guessed it was about field of view and shipped `AimDeltaFov` scaling in
F25. That is a reasonable feature and it is not this one. An Eotech is a thick
body around a small window; in Tarkov that body is solid, so aiming through one
blanks out most of what is around it. With both eyes open the off eye fills in
what the housing blocks. It is a **rendering** problem.

The F25 feature stays, renamed to `Keep peripheral vision when aiming` and
switched OFF by default. It was never asked for.

### What the game gives you

```
ProceduralWeaponAnimation.CurrentScope : SightNBone
SightNBone.Bone                        : Transform     // root of the housing mesh
EFT.CameraControl.OpticSight.LensRenderer : Renderer   // the window
EFT.CameraControl.OpticRetrice / ScopeReticle          // the aiming dot
```

So the job is: take the renderers under `Bone`, minus the lens, minus anything
carrying a reticle component, and get them out of the way. Hiding the lens or the
reticle would turn "see through the housing" into "the sight no longer works",
which is why both are excluded by identity rather than by guessing at names.

### Two modes, because transparency is not reliably available

**Hide** switches the housing renderers to `ShadowsOnly`. The mesh stops drawing,
the shadow stays, no material is touched. It works on every shader, so it is the
default.

**Fade** writes an alpha instead, which is what the thread's gif shows. EFT's
weapon shaders are custom and some have no `_Color` to write; those fall back to
Hide with one line in the log. The thread's own comments went round this exact
problem - transparency being expensive, scopes being rendered as physical
objects - so a mode that degrades cleanly is worth more than one that is correct
on paper.

### The part that would have bitten

Every touched renderer's original shadow mode and shared materials are recorded,
and restored on weapon change, on switching the feature off, and on unload. A
renderer left in `ShadowsOnly` does not end with the raid: the object persists,
so the player would find an invisible optic in the next one and have no idea why.
Same class of hazard as the static `AimDeltaFov` in F25, and worth stating twice.

### The lesson worth keeping

I implemented a feature from a link I could not open, said so, and got it wrong -
which was the predictable outcome and the reason I flagged it. But flagging a
guess is not the same as not making one. The right move was to build nothing
until he could paste the content, which cost him one message and me a wasted
feature.

Reddit is blocked to WebFetch, to WebSearch, and to the browser pane. Worth
remembering rather than rediscovering: for that domain, ask for a paste.

---

## F27. Two switches, because there are two phenomena

> what I want is options to choose between or two things, make gun blur with eyes
> focusing on the target ahead, and/or make it a bit transparent for the effect
> of two eyes

Right to separate them. They are not two implementations of one effect, they are
two different things your eyes do at once, and either is worth having alone.

**Transparent.** The off eye has line of sight past the housing, so the housing
stops being a wall. Alpha on the housing materials.

**Doubled.** The two eyes see something this close from noticeably different
angles, and the brain does not fuse it, because it is focused past it on the
target. So the housing appears twice, offset by the eye separation, each copy
faint.

That second one is the important realisation. The brief said "blur", and the
obvious reading is a depth-of-field effect - but near-object blur with both eyes
open is not a soft focus, it is a **double image**. Implementing it as doubling
is not an approximation of the phenomenon, it IS the phenomenon, and it happens
to be far cheaper than any real blur: two extra draw calls sharing the original's
mesh and materials.

Default separation is 0.064 m, the average human interpupillary distance, which
is the physically correct number. It is exposed anyway, because the housing sits
much closer to the eye in game than a real optic does and the right *looking*
value may not be the right *measured* one.

### Why not a real depth of field

EFT uses the Prism post-processing stack. Its effect components are not in
`Assembly-CSharp` - only enums and the preset ScriptableObject are - so driving a
real DOF means finding and hooking another assembly. And a camera DOF blurs the
whole near field, not the optic, which is a bigger visual change than was asked
for. Worth revisiting only if the doubling turns out not to read.

### Alpha, and what happens without it

Both effects need a `_Color` on the material. Some of EFT's custom weapon shaders
have none, and those cannot be faded or ghosted at all. Rather than silently
doing nothing, that falls back to hiding the housing, logs once, and the HUD says
`(no alpha here)` so it is visible rather than mysterious.

`Hide` also stays as a switch of its own. It is blunt, it works everywhere, and
after this session's history a reliable fallback that the user can reach
deliberately is worth a line in the config.

### The bookkeeping that would have bitten

Two ghost `GameObject`s per housing renderer, parented into the weapon. Those,
the shadow modes, and the material arrays are all recorded and undone on weapon
change, on switching off, and on unload. Objects parented into a weapon do not
end with the raid - the same hazard as the static `AimDeltaFov` in F25, and the
third time in three features that the restore path was the part most likely to
cause a bug the user could not diagnose.

---

## F28. Prism was reachable. I looked in one assembly and called it impossible.

> so EFT uses the Prism post stack is not reachable at all?

Fair question, and no. In F27 I wrote that a real depth of field was off the
table because "EFT's Prism post stack is not reachable from Assembly-CSharp".
That sentence is true and the conclusion drawn from it is not: I searched one
assembly out of the hundred and sixty in `EscapeFromTarkov_Data\Managed`, found
only Prism's enums, and stopped.

One directory listing later:

```
Unity.Postprocessing.Runtime.dll     216 KB
```

Unity's Post Processing Stack v2, shipped with the game, containing:

```
UnityEngine.Rendering.PostProcessing.DepthOfField
    focusDistance : FloatParameter
    aperture      : FloatParameter
    focalLength   : FloatParameter
    kernelSize    : KernelSizeParameter
```

Three numbers, and they are the three a real lens has. Nothing exotic was needed
at all - the effect was sitting in a public, documented, well-known package the
whole time.

### What it takes

A `PostProcessVolume` with a runtime-built `PostProcessProfile` holding a
`DepthOfField`, at high priority, weighted in by the aim blend. The one thing
that is not obvious: the volume must sit on a layer the game's own
`PostProcessLayer.volumeLayer` mask includes, or it is silently ignored. That
mask is read off the layer rather than guessed - the lowest set bit is used.

The focus distance is a raycast down the centre of the view. Not a setting, and
deliberately the VIEW's direction rather than the gun's, because free aim means
the gun is frequently pointing somewhere the eye is not, and it is the eye that
focuses.

Off by default. It is the only thing added in this session that costs real
frames, and the linked thread spent half its comments on exactly that cost.

### On the scope glare

> I remember in the old tarkov they also had a glare of the optic for all the
> optics, can we turn it on?

Nothing in `Assembly-CSharp` exposes a per-optic glare. The only flare-shaped
things are `UltimateBloom.m_UseLensFlare` (the global bloom's flare, not the
optic's) and a pile of bot-AI "flare" fields that are about grenades.

So whether the old effect can be switched back on is a question about what the
lens SHADER still carries, and that is runtime data no amount of decompiling will
answer. `Log the optic lens material` prints the shader name and every property
on the current optic's lens, once per weapon. Two minutes in a raid answers it
properly, which beats another guess.

### The lesson worth keeping

"Not reachable" was a claim about the world made from a search of one file. The
honest version was "I did not find it in Assembly-CSharp", and the difference
between those two sentences is the difference between a fact and an assumption
wearing a fact's clothes.

This is the same failure as F21 and F23 in a different costume: reasoning
confidently from a partial search, when widening the search was a single cheap
command. The owner has now caught it three times by simply asking whether I was
sure.

---

## F29. Three bugs, one log, and an error handler that repeated the error

> **Corrected by F38.** The const diagnosis is right but stops one question
> short: `AimDeltaFov` is not read by anything at all, so a writable field would
> have changed nothing either. The FOV seam is `CameraManager.SetFov`.

> when I click on these buttons that add perepherial vision nothing happens ...
> at some point the free aim completely stopped working regardless of what I was
> trying to turn on/off

The log answered all of it in six lines.

### 1. The optic housing did nothing, silently

```csharp
public static Transform GetCurrentSightBone(object pwa)
{
    object scope = M_CurrentScope.Get(pwa);   // never bound
    if (scope == null) return null;
```

`M_CurrentScope` was declared and never `Bind`ed. An unresolved `Member` returns
null from `Get`, so this answered "no optic fitted" on every weapon, forever. The
housing effects collected nothing and did nothing.

Nothing threw, nothing logged, and the HUD dutifully reported the truth as it
understood it - which is why it read "always off even with optics". A silent
correct-looking negative is the worst failure mode there is, and this file
already had a comment about that exact hazard in `Member` itself.

### 2. AimDeltaFov is a const

```
AimDeltaFov   Single   static=True   literal(const)=True   constant=15
```

Writing a const throws `FieldAccessException`. Worse, it could never have worked
even if it were writable: the game has `15` compiled into every use site, so
nothing reads the field at runtime at all.

Cecil reports a const as a static field. `IsLiteral` is the only thing that
separates them and I did not check it. That is now checked at resolve time, and
the reason is carried to the HUD instead of discovered by throwing mid-frame.

### 3. The error handler performed the failing operation

This is the one that took the mod down:

```
[Error] Both eyes open failed, switching it off: FieldAccessException
  at GameRefs.SetAimFovNarrowing
  at FreeAimPatches.ApplyBothEyes
[Error] Free aim frame failed, disabling to avoid log spam: FieldAccessException
  at GameRefs.ReleaseAimFov            <-- inside the catch block
  at FreeAimPatches.Frame
[Error] SPT Free Aim disabled for this session.
```

The write threw. The catch handler switched the feature off and called
`ReleaseAimFov()` to hand the stock value back - which performed the same write,
threw again, and that second exception escaped the handler entirely. The outer
frame guard caught it and emergency-disabled everything.

So the isolation added in F19 worked exactly as designed and was defeated by the
cleanup inside it. **An error handler must not repeat the operation that failed.**
`ReleaseAimFov` now cannot throw, and the handler no longer calls it.

### 4. And it stayed dead, because the rollback undid F19

`EmergencyDisable` was made clearable by the master toggle in `42b74a5`. The
rollback to `58c19a1` restored `Plugin.cs` from before that fix, so the flag was
permanent for the session again - "regardless of what I was trying to turn
on/off". Restored.

Worth noting as its own hazard: a rollback undoes the fixes as well as the
mistakes, and a pure safety net is exactly the sort of thing nobody thinks to
check afterwards. I did audit that rollback for the licence work and for the
crash fix in `Member`; I did not think about `Plugin.cs`.

### The lesson worth keeping

Every one of these was visible in the log within seconds of asking for it. The
mod's own diagnostics did their job - the stack traces name the exact methods -
and the fault was that nobody read them for two rounds of changes.

Ask for the log first. It is cheaper than any amount of reasoning about what
might be wrong.

---

## F30. One line in the log, and none of it was mine

> please check the log, nothing worked

```
[Info :BepInEx] Loading [SPT Free Aim 0.1.0]
[Info :BepInEx] Loading [DrakiaXYZ-SearchOpenContainers 1.5.0]
```

That is the entire contribution of the mod to a full session. `Awake` threw
before reaching its first `Log` call, and BepInEx does not surface an exception
thrown inside a plugin's `Awake` - Unity swallows it into the player log.

So the mod ran, failed, and said nothing. Twice in two rounds I have gone hunting
for a cause that the code could have named itself in one line.

### The immediate cause

The prime suspect is the compile-time reference to
`Unity.Postprocessing.Runtime` that F28 introduced. If that assembly is not
resolvable in the plugin's load context, the type load fails, and a failure of
that shape lands exactly here: after BepInEx logs "Loading", before any of the
plugin's own code runs.

I have not proved it, and saying so matters after F22. What is certain is that
the reference was the only new thing between a build that logged normally and a
build that logged nothing, and that it is the only change of a kind that can fail
at *load* rather than at *call*.

So the depth of field is removed for now, along with the csproj reference.
It was off by default, nobody had seen it work, and it was the newest and least
load-bearing thing in the build. Getting the optic housing working - which is
what was actually asked for - matters more than keeping an unproven feature that
might be preventing the whole mod from starting.

It can come back through reflection, with no assembly reference and therefore no
way to fail at load. That is how it should have been written in the first place,
for a type that lives outside the game's main assembly.

### The fix that should have existed from day one

```csharp
private void Awake()
{
    Instance = this;
    Log = Logger;
    Log.LogInfo("Awake: starting. " + VERSION);
    try { AwakeCore(); }
    catch (Exception e)
    {
        Log.LogError("SPT Free Aim FAILED TO START. Nothing below this line ran:\n" + e);
    }
}
```

Plus breadcrumbs between the stages, so the next silent failure names the stage
it died in rather than leaving a gap to be reasoned about.

`GameRefs.Resolve()` was already fully wrapped and reports its own failures
precisely - that was designed carefully, in the first week, because it was the
part expected to break on a game update. `Awake` itself, the thing that calls it,
had no guard at all. The careful part was surrounded by an unguarded one.

### The lesson worth keeping

Diagnostics are not a debugging aid to add when something goes wrong. They are
the difference between a bug report that costs one message and one that costs
four. This mod has an on-screen HUD, a probe key, a findings document and 60
tests, and it could not tell its owner why it failed to start.

Every entry point that can throw gets a handler that logs. Not the frame path
only - the frame path was already handled - but every one.

---

## F31. A build that never ran, because I can write files but not delete them

> nope still doesn't work

The log was identical. So was the DLL:

```
SPTFreeAim.dll   102,400 bytes   mtime 1788995601319   (unchanged)
```

My build of the fix was 93,696 bytes. The deployed plugin had not been rebuilt
at all, so the run that produced that log was the OLD binary. Nothing I changed
had ever been compiled.

### Why the build failed

```
Patches/FocusDepth.cs   6,845 bytes   still on disk
```

F30 removed the depth of field: I deleted `FocusDepth.cs` in the working copy and
removed the `Unity.Postprocessing.Runtime` reference from the csproj, and synced
the changed files.

**`device_commit_files` writes files. It cannot delete them.** So the csproj lost
the reference while the source file that needs it stayed exactly where it was.
Every build since has failed on that file, and Visual Studio does what it always
does with a failed build: leaves the previous DLL in place. The game kept loading
a binary from two fixes ago.

Emptied to a comment block rather than deleted, since the sync can overwrite but
not remove. A comment-only `.cs` compiles to nothing.

### What made it invisible

Three separate things each hid it:

- A failed build leaves a working DLL behind, so the game still runs and still
  logs. Nothing announces staleness.
- The mod's version string never changes, so `SPT Free Aim 0.1.0` in the log says
  nothing about which build it is.
- I verified my own build compiled, in my own tree, against my own file list -
  which was not his file list, because his still had the file I had "deleted".

I had the evidence to catch it immediately and did not look: the DLL's size and
timestamp were in a directory listing I had already run, one round earlier, and I
read the mtime as "recent, so he rebuilt" without comparing it to the byte count
of what I had actually built.

### What changes

**Check the artefact, not the source.** Before diagnosing behaviour, confirm the
binary under test is the binary in question - size and mtime against the build
just made. Two numbers, already on screen.

**A sync that cannot delete is a sync that can break a build.** Any change that
removes a file has to be sent as an emptied file, not as an absence, and said out
loud so the user can delete it properly.

**Version stamps earn their keep.** `0.1.0` has been the version for the entire
project. A build identifier in the startup line would have answered "is this even
the new code" in one glance, every time, for free.

---

## F32. The label that killed the plugin

The `Awake` guard from F30 earned itself back in one run:

```
[Info ] Awake: starting. 0.1.0
[Info ] Awake: binding config
[Error] SPT Free Aim FAILED TO START. Nothing below this line ran:
System.ArgumentException: Cannot use any of the following characters in
section and key names: = \n \t \ " ' [ ]
Parameter name: key
  at BepInEx.Configuration.ConfigDefinition..ctor
  at BepInEx.Configuration.ConfigFile.Bind[T]
  at SPTFreeAim.FreeAimConfig.Bind
```

In F29 I renamed a config key to `"Keep peripheral vision when aiming [BROKEN]"`,
to make it obvious in the F12 menu that the feature could not work. Square
brackets are illegal in a BepInEx key - they are the ini section syntax - so the
bind threw, `Awake` died on its second statement, and the entire mod failed to
start.

A warning label about one broken feature broke everything. The irony is not the
point; the point is that a *cosmetic* edit to a string took the whole plugin
down, and nothing between writing it and shipping it could have noticed.

### And the NullReferenceException on top

```
NullReferenceException at SPTFreeAim.Plugin.Update ()
```

`Update` already opened with `if (Instance == null || Cfg == null) return;`, and
that guard is worthless here:

```csharp
Cfg = new FreeAimConfig();   // Cfg is now non-null
Cfg.Bind(Config);            // throws - every ConfigEntry inside stays null
```

`Cfg` is assigned before `Bind` runs, so after the throw it is a perfectly valid
object full of null entries, and the next line - `Cfg.ToggleKey.Value` - threw
once per frame forever.

Replaced with a `_started` flag set on the final line of `AwakeCore`. It means
"all of it ran", which is the only question worth asking, and `Plugin.Active`
now checks it too. A failed start is silent from then on rather than a stack
trace at 120 Hz.

### The test

`tests/ConfigKeyTests.cs` reads `FreeAimConfig.cs` as text and checks every
`cfg.Bind` key against BepInEx's own illegal set, plus duplicate section+key
pairs, which throw just as fatally. Verified against the real bug: putting the
bracket back fails the check.

Reading source as text rather than running it is the right call here - running it
needs BepInEx and the game, and the rule being enforced is about naming, which is
plainly visible in the source.

### The lesson worth keeping

Config keys are an API with a validator, not labels. Four of the last five
failures in this project have been in the plumbing around the mechanic rather
than the mechanic itself: a const field, an unbound Member, a file that could not
be deleted, and now a string with a bracket in it. The aiming maths has been
right for days.

The guard added in F30 turned this from a silent death into a four-line diagnosis.
That is what it was for, and it paid for itself on its first run.

---

## F33. Zero renderers, because the "sight bone" is a camera

With the plugin finally starting, the housing collector ran for the first time
and reported precisely what was wrong:

```
Optic: SightNBone.Bone -> SightNBone.Bone (field, writable)
Optic housing: 0 housing renderers on mod_aim_camera, 0 kept (lens/reticle)
```

`SightNBone.Bone` is **`mod_aim_camera`**. That name is in the assembly, as a
constant on the very class the field belongs to:

```
ProceduralWeaponAnimation.MOD_CAMERA_BONE = "mod_aim_camera"
```

It is the transform the game aligns the eye to when aiming - an empty node. There
are no meshes under it, on any weapon, ever. So `GetComponentsInChildren<Renderer>`
returned an empty array and the feature did nothing, correctly, every frame.

The optic's body lives further up the hierarchy. `GetOpticHousingRoot` walks up
from the aim bone, preferring the ancestor that carries the optic's own visual
controller, then one carrying `OpticSight`, then simply the nearest ancestor that
has meshes - stopping before the weapon root, since grabbing that would hide the
whole gun.

### The heuristic is a guess, so it prints its evidence

I cannot see this hierarchy. The walk above is a reasonable guess about a scene
graph I am inferring from type names, which is exactly the shape of reasoning
that produced F21 and F28.

So `LogAncestorChainOnce` prints the chain from the aim bone to the weapon root
with a renderer count at each level, and flags which nodes carry `OpticSight` or
`SightModVisualControllers`:

```
Optic hierarchy from the aim bone up:
    * mod_aim_camera        renderers=0
      mod_scope             renderers=4  [OpticSight]
      mod_mount             renderers=2
      Weapon_root           renderers=37  <- weapon root, stopping
```

One raid makes the real shape visible. If the heuristic picks the wrong node, the
log says which one it should have picked, and the fix is a line.

### Also worth knowing

The log shows AmandsGraphics patching `OpticSight.OnEnable` and
`OpticComponentUpdater.Awake`. Another mod is already doing work on these exact
objects, so if the housing effects look wrong rather than absent, that is the
first thing to test against by disabling it for a raid.

### The lesson worth keeping

The name was in the assembly the whole time. `MOD_CAMERA_BONE = "mod_aim_camera"`
sits four lines from `LINE_OF_SIGHT_P0`, in a probe output I read and quoted in
F21. I took `SightNBone.Bone` to mean "the sight's transform" because the type is
called `SightNBone` and the field is called `Bone`, and never checked what it
actually pointed at.

Second time in this project that a plainly-named member did something other than
what its name implied - `LocalRotateAround(center, eulerRotation)` was the first.
Names are a hypothesis. The log is the evidence.

---

## F34. A material with a _Color that ignores it, and a DOF that fails safe

Doubling and hiding work. Transparency does not, and the log says why by saying
nothing at all: there is no "no colour property" warning, so `SetAlpha` found
`_Color`, wrote to it, and reported success.

The material has a colour. Its **shader** renders opaque and ignores the alpha
channel entirely.

Worse, the code around the write was theatre:

```csharp
m.SetInt("_SrcBlend", ...);
m.SetInt("_DstBlend", ...);
m.SetInt("_ZWrite", 0);
m.EnableKeyword("_ALPHABLEND_ON");
```

Those are **Unity Standard shader** property names. EFT's weapon shaders have
none of them, and `SetInt` on a property a shader does not declare is a silent
no-op. Four lines that looked like they configured blending and did nothing.

A material cannot be made transparent by asking politely. The shader has to be
one that blends, so the housing materials are now replaced with copies on a
shader that does - carrying the original's `_MainTex` and tint, with the alpha
applied. `Shader.Find` only returns shaders included in the build, so a list is
tried in order of likelihood and the result is logged:

```
Optic housing: looking for a shader that blends -
    absent  Unlit/Transparent
    FOUND   Sprites/Default
    ...
  using: Sprites/Default
```

And the housing's own materials are printed once, with their shader names and
which properties they actually declare, because assuming a shader's capabilities
from the presence of `_Color` is exactly what went wrong.

If nothing blends, it says so and falls back to hiding. The **doubling is
unaffected either way**, and remains the more faithful effect.

### The depth of field, resolved by name

Back, and rewritten so that every post-processing type is looked up by name at
runtime. The first version referenced `Unity.Postprocessing.Runtime` at compile
time, and a reference that fails to resolve kills the plugin at LOAD - after
BepInEx logs "Loading", before any of the mod's own code runs, with no
diagnostics attached (F30, F31).

That risk is not worth taking for an optional visual effect. Nothing in
`FocusDepth` can now stop the mod starting; the worst case is one warning and a
feature that stays off. Verified by checking the built assembly's reference list:

```
confirmed: no Unity.Postprocessing reference in the output assembly
```

One detail that would have made it silently do nothing: a PPv2
`ParameterOverride` only participates when its `overrideState` is set. Writing
`.value` alone leaves the effect reading the profile default, so `Override()` is
called for each on setup.

### The lesson worth keeping

Both halves of this are the same mistake in different clothes: **an API call that
succeeds is not an API call that did something.** `SetInt` on an absent property
returns quietly. `SetValue` on a const throws, which is better. Writing `.value`
without `overrideState` succeeds and is ignored.

Where a write can be silently discarded, the only honest test is to look at the
result - which is why the shader search, the material list and the housing
renderer count are all printed rather than assumed.

---

## F35. Tarkov already had the depth of field. I was building a second one.

> **Superseded in part by F36.** The conclusion - that the game already owns a
> depth of field and it should be driven rather than duplicated - holds. The
> effect named here is the wrong one: `CameraManager._depthOfField` is PPv2 and
> only `SetZBlur` reads it. The live one is `EffectsController._dof`.

The owner asked for the gun to go soft while the eye is focused downrange, and
added: *"I think Tarkov has something like that already."*

He was right, and it took one Cecil probe to prove it:

```
F  CameraManager._postProcessVolume : PostProcessVolume
F  CameraManager._depthOfField      : DepthOfField
F  CameraManager._postProcessLayer  : PostProcessLayer
F  EffectsController._dof           : DepthOfField
REF Unity.Postprocessing.Runtime, Version=0.0.0.0
```

A live PostProcessing v2 `DepthOfField`, already in the game's own volume, on the
game's own layer, in the game's own render order. Everything F28 through F34 was
spent trying to build beside it — a volume to create, a layer mask to guess, a
blend distance to get right, and a real chance of two DOF passes fighting each
other for the same frame.

So this drives **that** instead:

```csharp
CameraManager.Instance -> _depthOfField -> focusDistance / aperture / focalLength
```

all resolved by name at runtime through `Compat/GameRefs.cs`, with no compile-time
reference to `Unity.Postprocessing.Runtime` (F30's rule: a reference that fails to
resolve kills the plugin at LOAD, before any of its own code runs, with no
diagnostics attached).

### Focus follows the eye, not the gun

`FocusDepth.FocusDistance` raycasts down the middle of the **view**, not down the
bore, and focuses on whatever it hits — capped at `DOF max focus distance`, which
is also the fallback when the ray hits nothing, i.e. focus at infinity.

That choice is the whole point of the feature in a free-aim mod. Free aim means
the gun is usually pointing somewhere the eye is not, and the eye is the thing
that focuses. Focusing down the bore would sharpen whatever the barrel happened
to be crossing, which is exactly backwards.

### Aperture is the strength dial

```csharp
float aperture = Mathf.Lerp(22f, o.Aperture, w);
```

Rather than toggling the effect on and off as the weapon comes up, the aperture
blends from a nearly-closed f/22 (deep focus, nothing visibly blurred) toward the
configured f-stop. Shouldering the weapon *eases* into focus instead of snapping,
and the game's own effect stays enabled the whole time rather than flickering.

### Every stock value is captured, and handed back

The effect belongs to the game, not to this mod. `CaptureStock()` runs once at
resolve time and `ReleaseDepthOfField()` restores focus distance, aperture, focal
length and the `active` flag — and never throws, so a failure to release cannot
cascade the way F29's did. Without this, a modified DOF would follow the player
out of the raid and into the hideout.

One PPv2 detail that would have made all of it silently do nothing, and is worth
repeating from F34: a `ParameterOverride<T>` only participates when its
`overrideState` is true. `Write()` sets both `value` and `overrideState` on every
parameter, every time.

### What was removed, and why

Transparent / doubled / hide are gone from `Patches/OpticHousing.cs` (emptied to a
stub — the sync tool writes but cannot delete, F31).

Doubling and hiding worked. **Transparency never did, and the reason is worth
recording:** the implementation swapped the housing's materials onto a shader that
blends, copying `_MainTex` across. EFT's weapon materials do not carry their
albedo in `_MainTex`. So the replacement shader received no texture and drew flat
**black** — which is why the owner saw the sight turn black rather than
see-through. It also failed to restore on un-aim, so the black stayed.

That is a third instance of F34's lesson wearing different clothes: **an API call
that succeeds is not an API call that did something.** `SetInt` on an absent
shader property returns quietly. `SetValue` on a const throws — better.
`material.mainTexture` on a material with no albedo there returns null and the
shader draws black — no error at all.

### The lesson worth keeping

Before building a system, check whether the host already owns one. Seven findings
of effort went into approximating an effect that was sitting in a field on a
singleton the mod already had a handle on.

The owner found it by remembering the game. I found it by probing the assembly —
which I could have done at F26 for the cost of one command.

---

## F36. Two DepthOfFields, and I wired up the dead one

The owner asked for the gun to blur while aiming and added: *"I think Tarkov has
something like that already."* He was right twice over, and my first attempt
still got it wrong, because the game has **two** depth-of-field effects and only
one of them is alive.

```
CameraManager._depthOfField : UnityEngine.Rendering.PostProcessing.DepthOfField
    store  CameraManager.method_2      <- assigned once at init
    read   CameraManager.SetZBlur      <- exactly one consumer

EffectsController._dof      : UnityStandardAssets.ImageEffects.DepthOfField
    read   EffectsController.SetDoFFocalDistance
    <- called by CameraManager.ApplyFoV, on every FOV change
```

I found the PPv2 one first, saw a real `DepthOfField` on a real
`PostProcessVolume`, and stopped looking. It is only used by `SetZBlur`. The one
you actually see in a raid is the **legacy** image effect, and the game drives it
continuously:

```
focalLength = 5.2 - InverseLerp(minFov, maxFov, fov) * (minFov / (maxFov - minFov))
```

So Tarkov already focuses about five metres out and pulls that in as you zoom.
That is the softness the owner remembered.

### The screenshot settled the technique

He sent a frame of the effect he wanted: the receiver nearest the eye heavily
soft, the front sight half a metre further out nearly sharp, the world beyond in
focus. **The blur is a gradient down the weapon.**

That one observation rules out every approach in F26 through F34. Swapping a
material, blurring "the weapon", fading a renderer - all of them apply one flat
amount to a whole object. Only a depth-based effect blurs each pixel by how far
it sits from the plane of focus, and the gradient falls out of the geometry for
free.

It also explains why the transparency work was a dead end even when it worked:
transparency is not softness, and the reference was never asking for it.

### nearBlur is the whole feature

The legacy effect has a field the PPv2 one does not:

```
nearBlur : bool     - blur things NEARER than the focus plane
```

The gun is nearer than anything you are ever looking at. With `nearBlur` off,
focus can sit at infinity and the weapon stays perfectly sharp. It is the
difference between the feature working and the feature doing nothing, and it is
one boolean.

### focalTransform silently outranks focalLength

From the Standard Assets source:

```csharp
float focalDistance01 = focalTransform
    ? WorldToViewportPoint(focalTransform.position).z / farClipPlane
    : FocalDistance01(focalLength);
```

If `focalTransform` is set, `focalLength` is **ignored entirely**, and
`EffectsController.Init` copies one across from the prefab. Writing a focus
distance without clearing it would have been a perfect silent no-op - the write
succeeds, the value lands in the field, and the effect never reads it.

That is F34's lesson for the third time: **an API call that succeeds is not an API
call that did something.** `DriveDepthOfField` nulls `focalTransform` first and
restores it on release.

### maxBlurSize as the strength dial

The legacy effect also has `aperture`, but its direction is not obvious from the
outside and I could not verify it without the shader. `maxBlurSize` is a blur
**radius**: 0 is unambiguously nothing, larger is unambiguously more. So that is
the dial the config exposes and the one the aim blend ramps, and `aperture` is
left at stock rather than asserting a direction I cannot check.

### Transparency, rewritten rather than repaired

The owner also asked why the transparent option turned the sight black. Because
the swap copied `_MainTex`, and EFT's weapon shaders do not keep albedo there -
so the replacement shader got no texture and drew flat. Nothing threw: asking a
material for a texture it does not have is a legal question with a null answer.

The rewrite does not guess. `Shader.GetPropertyCount` / `GetPropertyType` /
`GetPropertyName` are available in this Unity version, so it reads the source
shader's own property table, keeps the texture properties, scores them by name
(albedo and base-colour high, normal and roughness negative), and takes the best.
**If it finds nothing it leaves the material alone** rather than blacking it out,
and the diagnostic prints the whole table so the next round is informed.

Restore is by the whole `sharedMaterials` array per renderer, which is what the
first version got wrong when it failed to come back on un-aim.

### The lesson worth keeping

Finding *a* thing that matches the description is not finding *the* thing that
runs. Both DepthOfFields were real, both were reachable, both were on the camera.
The probe that mattered was not "does a DOF exist" but "who calls it" - and that
is four lines of Cecil I could have written at F26.

---

## F37. The blur was never about aiming, and the cone is the wrist's slack

Two changes from three frames of reference footage, and both of them are cases
where the right implementation was smaller than the one I would have written.

### The blur should not be gated on aiming

I shipped `Gun blur only while aiming` defaulted ON, reasoning that the effect
should fade in as the weapon comes up. The reference frames say otherwise: the
gun is heavily soft at low ready, hanging at the bottom of the screen, while the
room beyond is sharp.

Of course it is. **The effect is about where the EYES are focused, not where the
gun is.** Your eyes are downrange whether the weapon is shouldered or hanging.
Gating it on the aim blend encoded a confusion between the two.

The pleasing part is what did NOT need building. At low ready the weapon sits
nearer the eye, so it is further from the plane of focus, so a depth-based effect
blurs it harder with no extra term. The stronger low-ready blur the owner asked
for is the physics of the effect chosen in F36, not a setting. The whole change
is a default flipped from `true` to `false`.

That is twice in two findings that the right move was to remove something rather
than add one.

### Roll: the deadband IS the cone

The owner's description of the cant, exactly as given:

> when we turn to one of the direction too far, then the gun is rotated a bit in
> the opposite direction where we turn (left-counterclockwise, right-clockwise),
> when aiming it is more prominent, when going low ready, it is only when we push
> far from the cone

The first two clauses are an ordinary proportional term. The third is the
interesting one, and it has a physical reading that makes it fall out for free:

- **Aimed**, the weapon is braced against the shoulder. There is no slack, so
  every bit of swing twists it, and the roll is proportional from zero.
- **Low ready**, the weapon hangs off the hands with slack in the wrists. Nothing
  twists until the swing takes up that slack.

And the machine already has a number for "how far the gun swings before anything
starts happening": the cone. So the deadband is not a new tuning constant to
guess - it is the cone, fading out as the weapon comes up:

```
dead = cone * (1 - aimBlend)
t    = clamp01((|yaw| - dead) / (cap - dead)) * sign(yaw)
roll = t * lerp(degreesReady, degreesAimed, aimBlend)
```

Two magnitudes to tune and no third constant. F16's rule - an empty field beats a
plausible-looking inherited number - applies just as much to a number I would
have invented myself.

### The axis, and why this is not F20 again

Rolling needs an axis, and picking one from the weapon's local frame is what blew
up in F20. It is a much smaller bet this time, and the difference is worth being
explicit about:

- F20 used a guessed axis to place a **pivot** 0.3 m away. Wrong axis, and the
  hinge lands somewhere off the weapon entirely, so rotation reads as translation
  and the whole mechanic breaks.
- This uses an axis for a **roll of a few degrees**. Wrong axis, and the weapon
  tilts on a slightly different line. It degrades to "not quite right" rather
  than to "broken".

Default is the weapon's own forward, with the camera's forward behind a switch as
the cannot-be-wrong fallback - though at low ready, with the muzzle at the floor,
rolling about the view axis reads as a twist rather than a cant.

It rotates orientation only and never position, so it cannot displace the weapon
or disturb the offset applied immediately before it, and it ships **off by
default** like every mechanic added since F20.

### The lesson worth keeping

When a described behaviour needs a threshold, check whether the machine already
has that number under another name before inventing one. Here the wrist's slack
and the aim cone are the same quantity seen from two directions, and noticing
that removed a tuning constant instead of adding one.

---

## F38. The weapon knows where it turns, and the FOV const was a decoy

Two complaints, one shape: both had an answer sitting in the game, and in both
cases my earlier diagnosis had stopped one question short.

### "The buttstock is glued to my shoulder"

The owner has said four times now that the gun should turn about the RIGHT HAND.
I kept treating this as a tuning problem - a pivot dial to find by eye, a bore
axis to guess at (F20), a grip point to place relative to the camera. All of it
was unnecessary. `PlayerSpring`, which is `ProceduralWeaponAnimation.HandsContainer`
and which this mod has held a reference to since the first build, carries:

```
Vector3 RotationCenter             - centre with the buttstock BRACED
Vector3 RotationCenterWoStock      - centre with it NOT braced
Vector3 RecoilPivot
Vector3 MountingRotationCenter
Vector3 MountingRotationCenterBipods
Transform Fireport
```

Both centres are Vector3s in `WeaponRootAnim`'s **local space** - the exact space
the legacy pivot already uses - and BSG authors them per weapon.
`ApplyComplexRotation` picks between them:

```csharp
center = _shouldMoveWeaponCloser ? HandsContainer.RotationCenterWoStock
                                 : HandsContainer.RotationCenter;
world  = HandsContainer.WeaponRootAnim.TransformPoint(center);
```

So "about the shoulder" and "about the hands" were never two behaviours to model.
They are two numbers the game ships, and the braced one is the one that was in
use. The owner was describing `RotationCenter` and asking for
`RotationCenterWoStock`, in those words, without knowing the field names.

A measured number from the weapon beats a dial tuned by eye on one gun, and it is
right on every other gun for free. `-0.15` was a real measurement of a real grip
on one rifle; it was never going to generalise.

### "I don't want the FOV to come closer"

F29 concluded that `CameraManager.AimDeltaFov` could not be written because it is
a const. True, and beside the point. The question I did not ask was **who reads
it**, and the answer is:

```
=== who READS CameraManager.AimDeltaFov ===
(nothing)
```

Nobody. Not one instruction in `Assembly-CSharp` loads that field. The 15 is
inlined straight into `ProceduralWeaponAnimation.OnAimOrPoseChanged`:

```csharp
float fov = !IsAiming              ? HeadBobbing
          : CurrentScope.IsOptic   ? 35f
                                   : HeadBobbing - 15f;
CameraManager.Instance.SetFov(fov, 1f, !_isAiming);
```

So the peripheral-vision feature could never have worked, and "it is a const"
was the wrong reason for the right conclusion - a writable field would have done
nothing either. That distinction matters, because "const" invites you to look for
a way to write it, and there was none worth finding.

`SetFov` is the seam. A prefix on it puts `x` back to `HeadBobbing` - the game's
own un-aimed value, not fifteen subtracted back - so it stays correct if BSG ever
changes the number.

### Hold your breath and lean in

The owner then asked for the zoom back while holding breath: *"like I am really
focusing on the target and moving my head close."* `PhysicalBase.HoldingBreath`
is a plain bool property, and `Player.Physical` was already reached for the arm
drain.

One wrinkle worth recording: the game only calls `SetFov` when the aim or pose
**changes**, so holding breath mid-aim would never trigger it. The implementation
watches the breath state and makes the call itself, through the game's own
coroutine, rather than writing `Camera.fieldOfView` per frame and fighting it.
The zoom target is not recomputed either - it is whatever the game last asked for
while aiming, recorded in the prefix, so an optic still goes to its own
magnification instead of a flat fifteen degrees.

### The lesson worth keeping

"Can I write this?" is the second question. **"Who reads this?"** is the first,
and F29 shipped a finding without asking it. Same for the pivot: four rounds of
tuning a number that the weapon was carrying the whole time.

Both are the same failure as F36 - finding *a* mechanism that matches the
description and stopping, instead of finding the one the game actually uses. That
is three findings in a row. The probe that keeps paying is the cross-reference,
not the lookup.
