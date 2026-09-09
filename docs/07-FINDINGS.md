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
