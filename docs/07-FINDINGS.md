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

> **Completed by F40.** This finding read LocalRotateAround's ROTATION and
> stopped one line early. Its POSITION line is broken too: it displaces by
> (I - q)*c where a rotation about c needs R*(I - q)*c. There is no hinge at
> all, which is why no pivot value ever fixed it.

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

> **Repeated as F45.** The unbound-Member half of this happened again to
> `M_Physical`, killing arm drain and the hold-breath zoom. `tests/BindTests.cs`
> now scans for Members that are read but never bound.

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

---

## F39. Allowed and typeable are not the same thing

The owner: *"Are you sure you've implemented these, still only positive values
for the x,y,z across all parameters."*

I was sure, and I was wrong in the most annoying way available: I had checked the
right thing and drawn the wrong conclusion from it. Every Vector3 config entry
was bound with **no** `AcceptableValueRange` at all, and I reported that as "any
component takes any sign". The values were never clamped. They could not be
**typed**.

The config UI draws a Vector3 as three text boxes and re-parses each one on every
keystroke. To enter `-0.15` you must first enter `-`, and `-` on its own is not a
number, so the parse fails and the character is discarded. The field can *display*
a negative that came from a default - his HUD was showing `grip (0.14, -0.24,
0.16)`, which is exactly why I believed the mechanism worked - but you can never
get one in by hand.

So the evidence I cited as proof was the same evidence that should have made me
suspicious: a negative that was already there, which he had not typed.

Every vector entry is now three floats with signed `AcceptableValueRange`s. A
ranged float gets a slider, and a slider has no keystrokes to lose. Twenty float
keys replace seven vector keys, which is more config than before and worth it.

`tests/ConfigKeyTests.cs` gained two checks: no config entry may be a vector type,
and every dial labelled `(m)` or `(deg)` must have a range reaching below zero.
The second one immediately found `Yaw write probe amount (deg)` clamped to 1..90,
which is a direction test that could only be run in one direction. Three keys are
exempt and named in the test rather than silently skipped - cone size, hard cap
and max focus distance are radii and distances, where a negative is not a mirrored
value but nothing at all.

### The sniper scope, and one switch doing two jobs

Same message: *"when looking into a rifle with a sniper scope, it jumps into
zooming my fov right away."*

Back to `OnAimOrPoseChanged`:

```csharp
float fov = !IsAiming              ? HeadBobbing
          : CurrentScope.IsOptic   ? 35f
                                   : HeadBobbing - 15f;
```

Two branches, two completely different physical things, and from inside `SetFov`
they are indistinguishable - both arrive as one float smaller than the base FOV.

- `HeadBobbing - 15` is the **shouldering lean**. Cancelling it is the entire
  point of "keep your field of view when aiming".
- `35` is the **magnification**. Cancelling it takes the zoom off a sniper scope.

I cancelled both, so a 10x scope stopped being a 10x scope. The prefix now asks
`SightNBone.IsOptic` and leaves optics alone unless a second switch says
otherwise, which defaults off.

### The lesson worth keeping

**"Is it permitted?" and "can the user actually do it?" are different questions,
and only the second one matters.** Reading the binding and reporting the range was
answering a question nobody asked. The check that would have caught it is trying
to type the value.

That is the same shape as F34 and F36 one level up the stack: an API call that
succeeds is not an API call that did something, and a config range that permits a
value is not a config field that accepts one.

---

## F40. The legacy hinge cannot hinge, and that is arithmetic

*"Not sure how to explain you even more but my gun is not rotating on horizontal
plate enough around the handgrip."*

Fifth time. He has been describing the same thing since the 5-DOF message and
every previous round I answered it as a tuning problem - find the right pivot,
guess the bore axis (F20), read the weapon's own centre (F38). All of those set
**where** the hinge is. None of them touched **whether there is a hinge at all**,
and there was not.

Here is `TransformTools.LocalRotateAround`, decompiled from the IL rather than
inferred:

```csharp
static void LocalRotateAround(Transform t, Vector3 c, Vector3 euler)
{
    Quaternion q = Quaternion.Euler(
        t.InverseTransformDirection(t.parent.TransformDirection(euler)));

    t.localPosition = t.localPosition + (R * c) - (q * c);   // R = t.localRotation
    t.localRotation = R * q;
}
```

A rotation of a rigid body about the local point `c` moves the origin by

```
    R*c - (R*q)*c  =  R * (I - q) * c
```

The game writes `R*c - q*c`. The mod calls it twice - once with `c`, once with
`-c` and no rotation - which cancels the constant part and leaves

```
    net displacement = (I - q) * c
```

So there IS a lever arm and it does scale with the rotation. It is simply **in
the wrong frame**: `(I - q)*c` instead of `R*(I - q)*c`, off by exactly `R`, the
weapon's own local rotation. Right length, wrong direction.

That is the whole complaint, precisely. A yaw that should have moved the weapon
horizontally moves it along some rotated axis instead, so most of the intended
horizontal swing leaks into pitch and into the barrel direction. The gun turns,
but it does not turn *about the hand*, and it never will - because the pivot dial
sets `c`, and `c` is not the part that is wrong.

F22 had already read this method and concluded the AXES were reinterpreted. True,
and it stopped one line early: the position line is broken too, and that is the
line that decides whether a hinge exists.

### The fix was written months ago

`HingeMode.AroundGrip` has always done the honest thing:

```csharp
root.position = pivot + q * (root.position - pivot);
root.rotation = q * root.rotation;
```

That is an exact rigid rotation about a world point. It was built in F22, tested
against the wrong problem, blamed during the F20-F23 mess, and left as a non-
default while I went back to tuning `c` on a call that cannot use it.

It now takes its pivot from the weapon's own rotation centre when
`PivotFromWeapon` is on - `root.TransformPoint(centre)`, exactly the way
`ApplyComplexRotation` places it, since `root` here IS `WeaponRootAnim`.

### Make the invisible thing visible

The HUD prints `lever <n> m` in AROUND GRIP mode: the distance from the weapon
root to the hinge. That single number is the difference between hinging and
spinning about your own origin, and there was no way to see it before. The legacy
row is now marked in red as unable to hinge, rather than merely "reinterprets the
axes".

### The lesson worth keeping

**When someone repeats the same complaint after each fix, the fix is addressing
the wrong variable.** Four rounds of moving the pivot, when the question that
mattered was whether moving the pivot could do anything at all. One decompile of
the method being called - which F22 had already opened - would have answered it
at any point.

The tell was in his words the whole time: not "the pivot is in the wrong place"
but "it is not rotating around the handgrip". He was describing an absent hinge,
and I kept hearing a misplaced one.

---

## F41. The counterbalance, and one swing number for two reactions

*"when you turn left or right while aiming, the body is kind leaning to the
opposite direction and the gun is leaning as well"*

Read as a bug report this says nothing is wrong. It is a description of what
should be there: the counterbalance. Throw a rifle to the right and the mass goes
right while the spine goes left, because the weight has to stay over the feet. On
a bodycam it shows up as the horizon tipping through every turn, and it is most
of what separates that footage from a camera on a tripod.

### Two reactions, one movement

The wrist cant (F37) and this lean are not independent effects. They are two
things a body does in response to one swing, and if each computed its own idea of
"how hard am I swinging" they would eventually disagree about when the swing
started - one leaning while the other had not begun.

So `SwingFraction` came out of `ApplyGunRoll` and is now shared:

```csharp
dead = cone * (1 - aimBlend)
t    = clamp01((|yaw| - dead) / (cap - dead)) * sign(yaw)
```

Both effects read it, both inherit the cone deadband and its fade with aim, and
both settle on the same spring the offset already uses. No new timing anywhere.

### Applied in two places, computed in one

The camera and the weapon sit under different `TransformGuard`s, and a guard only
covers what is written between its own BeginFrame and EndFrame. So the lean is
computed once before either guard opens and written twice inside them -
`ApplyLeanToCamera` in the camera block, `ApplyLeanToWeapon` in the weapon block.

Splitting it that way also fixed a bug that had not happened yet: `doCamera` was
previously `Compensate mode && ApplyCameraOffset`, so in Intercept mode - the
default - the camera guard is never opened. A camera roll written there would
have compounded every frame and put the horizon on its head in about a second.
`doCamera` is now `doCompensate || doLean`.

### The weapon might already be leaning

`ApplyLeanToWeapon` checks `anim.IsChildOf(cam)` and skips itself if true: when
the weapon hangs off the camera in the hierarchy, Unity has already carried it and
applying the lean again doubles the angle.

Checked rather than assumed. The parentage in this rig has been wrong twice
already - F23 (WeaponRootAnim IS under WeaponRoot, so pose rotations re-aim every
offset) and F33 (the "sight bone" is an empty camera node). It costs one call to
ask.

The weapon leans about the CAMERA's position rather than its own origin, because
a leaning body swings everything it carries about the spine, and the eye is the
closest thing to that axis this rig exposes.

### Numbers

Aimed 4 degrees at the hard cap, low ready 1.5. Both deliberately small: it is
the horizon, and the eye notices a tilted horizon far more readily than it
notices a tilted gun. Ten degrees is not a shooter bracing, it is a shooter
falling over.

Off by default with an invert switch, like every mechanic since F20. Which screen
direction opposes a right-hand swing depends on the camera's handedness, and
reasoning about that from first principles has a poor record here (F24, F37) -
the instruction is to look at it.

### The lesson worth keeping

A request phrased as an observation is still a request. "The body is kind leaning"
described something absent, not something broken, and the useful move was to ask
which - rather than to go hunting for the bug that was producing it.

---

## F42. One bad frame should not end the session

*"Why when I switch to pistol the free aim stops working and I need to enable it
again"*

His words match one line of the mod exactly:

```
SPT Free Aim disabled for this session. Press the master toggle (F8) twice to
clear it and retry - no need to restart the raid.
```

That is `EmergencyDisable`, and the path to it was:

```csharp
try { Frame(__instance); }
catch (Exception e)
{
    Plugin.Log.LogError("Free aim frame failed, disabling to avoid log spam: " + e);
    Plugin.Instance.EmergencyDisable();     // <- on the FIRST exception, ever
}
```

**One** exception, on **one** frame, killed free aim for the rest of the raid.

### Why a weapon swap is the frame that fails

Swapping weapons tears the old weapon's rig down and builds a new one. For a
frame or two in the middle, the transforms this mod reads are Unity objects that
have been Destroyed - and a destroyed Unity object does not read as null when you
touch a member of it, it **throws**. Add the pwa instance changing under
`LocalPwa` in the same window and a transient throw is close to expected.

Which is fine. What was not fine was the response.

### The remedy was aimed at the wrong problem

The old comment says "disabling to avoid log spam", and it is right that an
exception once a frame floods the log. It picked the wrong lever: it stopped the
MOD rather than stopping the LOGGING.

Now the first three failures are logged in full, further ones are silent, and the
mod only gives up after **30 consecutive** failures - about half a second. The
counter resets on the first frame that succeeds, so a swap every few minutes
never accumulates toward the limit. A genuine fault still trips it almost
immediately; a hiccup during a swap costs two frames of offset that nobody can
see.

There is now a recovery line in the log too, which is the thing that was missing
most: previously a transient fault and a permanent one looked identical.

### A second way the same frame could ruin things, silently

Found while reading, not reported:

```csharp
Transform cam = GameRefs.GetCameraTransform(pwa);
if (cam == null) { ...warn once...; cfg.Hinge.Value = HingeMode.LegacyEuler; return; }
```

A single frame where the camera transform is unavailable - again, exactly what a
weapon swap produces - **permanently rewrote the owner's hinge setting**, quietly
dropping him from AROUND GRIP back onto the hinge that F40 proved cannot hinge.
He would then be told the gun does not rotate about the grip, and the config would
show LEGACY EULER as though he had chosen it.

The warning only fires once, so the second occurrence is completely silent.

Now it skips the frame and leaves the config alone.

### The lesson worth keeping

**Error handling has a blast radius, and it should be proportional to the
evidence.** One exception is evidence of one bad frame. It is not evidence that
the feature is broken, and it is certainly not grounds to disable a whole mod or
to overwrite a setting the user chose.

Third time an error handler has done more damage than the error it caught: F29
repeated the failing write and escalated it into an emergency disable, F31's
recovery hid a stale build, and now this. The pattern to watch for is a handler
that changes STATE - disabling, rewriting config, unsubscribing - on evidence
that is only a single sample.

---

## F43. Prism was there all along, and the reference is glass not glow

*"can we get similar lense glare effect? Is there something like that on unity
that we can leverage?"*

Yes, and the thing to leverage is not a Unity built-in - it is `PrismEffects`,
declared in `Assembly-CSharp` and already held by the camera:

```
CameraManager._prismEffects : PrismEffects
    stfld  CameraManager.method_2            <- assigned at init
    ldfld  CameraManager.SetNoise
    ldfld  CameraManager.EnableAutoExposure
    ldfld  FlyingBulletSoundPlayer.StartVignetteEffect
```

The game writes to it itself, for screen noise, auto exposure, and the vignette
on a near miss. So it is live, it is in the render order, and it takes the same
approach as F36's depth of field: turn up what is already running rather than
stacking a pass beside it.

What it carries, all public instance fields:

```
useBloom, bloomType (Simple|HDR), bloomIntensity, bloomThreshold, bloomBlurPasses
useLensDirt, lensDirtTexture, dirtIntensity
useRays, rayTransform, rayWeight, rayColor, rayThreshold
useChromaticAberration, chromaticIntensity, aberrationType (Vignette|Vertical)
useExposure, exposureMiddleGrey, exposureSpeed, exposureLowerLimit/UpperLimit
useVignette, vignetteStart/End/Strength/Color
```

That is a full lens kit, including auto exposure - which is a large part of why
bodycam footage reads as a camera, and worth revisiting separately.

### Lens dirt is the setting that matters

Bloom alone reads as a **glow**. Bloom modulated through a dirt texture reads as
light scattering off a piece of **glass with something on it**, which is what a
lens does and what the reference shows. `dirtIntensity` is the one dial that
changes the character rather than the amount.

It also has the failure mode this project keeps meeting: with no
`lensDirtTexture` assigned, the effect runs, multiplies by nothing, and draws no
difference - a silent no-op, exactly F34's shape. So `ResolvePrism` checks for the
texture at resolve time and both the log and the HUD say `(no texture)` when it is
absent, rather than leaving a dial that cannot move.

### Bloom is a multiplier, not an absolute

`bloomIntensity` is scaled against the captured stock value rather than replaced.
1.0 is stock Tarkov, 1.6 is the default here. An absolute number would mean
nothing to anyone and would silently stop matching if BSG retuned theirs - the
same reasoning that made F38 restore the FOV to `HeadBobbing` rather than adding
fifteen back.

### What the reference actually shows, and what this does not cover

Watched the frame rather than reasoning from the description. The scope fills the
view with a green-yellow coating tint across the glass, a bright rim on the lens
edge, and the image inside noticeably brighter than the world around it.

Prism gets the **full-screen** half of that - the bleed, the fringing, the
scatter. It cannot do the **per-optic** half: the coating tint and the rim
highlight live on the lens material, reached through
`OpticCameraManager.CurrentOpticSight.LensRenderer`, and driving those needs to
know the lens shader's property names. That is what the `Log the optic lens
material` diagnostic was added for, and it has never been run.

Two separate mechanisms behind one word, and only one of them is done. Recorded
here so the other half is not mistaken for a tuning problem later - which is
precisely the mistake F40 cost five rounds.

### Three things, one word

The owner sent two more frames and named the cues himself: *"there are nice
circle around the ages, showing that the glass is curved"* and *"there is round
distortion on the edges of the lense"*.

Looking at those frames rather than reasoning from "glare", the reference is
**three separate mechanisms** wearing one name:

1. **A bright hotspot on the glass** - a blown highlight in the upper-left of the
   lens. Full-screen bloom reaches this. Prism does it.
2. **A ring at the rim, darkening toward the edge** - a vignette INSIDE the scope
   image, not on the screen.
3. **Round distortion at the lens edge** - straight lines bowing near the rim. A
   lens distortion INSIDE the scope image.

Two and three are not screen effects at all. They belong to the optic's own
camera, which has its own stack:

```
OpticCameraManager._postProcessVolume : PostProcessVolume
OpticCameraManager._postProcessLayer  : PostProcessLayer
```

That is a second, independent PPv2 stack rendering only what you see through the
tube. If its profile already carries a `LensDistortion` and a `Vignette` - shipped
and switched off - then both cues are two `enabled = true` writes away. If it does
not, they need creating, which is a different and much larger job.

**That is asset data.** It cannot be read from the assembly, only from a running
raid, so `DumpOpticSetupOnce` prints the profile's effect list and every shader
property on the lens material.

Which is also how this finding avoided repeating F40: three cues, three
mechanisms, and only one of them was ever going to yield to the thing I had
already built. Saying so before building the other two is the entire lesson of
the last five findings.

### Shoulder give

Same message: *"it seems that gun has a bit of the leeway outside of the shoulder
point"* - which is the owner repeating, from the reference, what he first said in
the original five-degrees-of-freedom message: *"we can have a bit of the leeway
when turning"*.

A translation, not a rotation. The hinge stays where the weapon says it is (F38);
this is give on top. It runs opposite the swing, because the weapon's own mass is
what loads the pocket, and it scales with the aim blend since a weapon at low
ready has no pocket to give.

It reuses `SwingFraction` too, so all four swing reactions - cant, lean, give, and
the offset itself - are driven by one number and cannot drift apart.

### A test gap worth recording

While editing the config, a python edit dropped a `);` and left the file
syntactically broken. `ConfigKeyTests` passed - before AND after - because it
reads the file as text and checks naming rules, not compilability.

That is not a flaw in the test, it is the boundary of what a text-scanning test
can know. It does mean the build is the only thing standing between a bad edit and
a broken DLL, which is exactly the F31 situation: build every time, and check the
artefact's size and timestamp rather than trusting that a command ran.

---

## F44. A master switch above a row of dials is a trap

*"none of the things that you listed here worked, no effect."*

He was right that nothing happened, and it was not a bug. His config file:

```
Lens glare                = false     <- master OFF
  Glare: bloom            = 5         <- cranked to maximum
  Glare: lens dirt        = 3         <- cranked to maximum
  Glare: colour fringing  = 2         <- cranked to maximum

Body leans as you swing   = false     <- master OFF
  Body lean aimed (deg)   = 4
  Body lean at low ready  = 1.5

Shoulder give (m)         = 0         <- never set
Log the optic setup       = false     <- never run
```

He found every dial, set every one of them, and never flipped either switch. And
the mod was doing exactly what it was told.

### Why he was going to do that

**The dials look like the feature; the switch looks like a preference.** In a
config list of eighty-odd entries, `Glare: bloom` reads as the thing that makes
glare happen and `Lens glare` reads as an option about it. Setting bloom to 5 and
seeing nothing then looks like a broken mod, not an unset boolean - and there is
nothing on screen to say otherwise unless the HUD happens to be up.

I did this three times: lens glare, body lean, gun roll. And I had already built
the correct shape once without noticing - **`Shoulder give (m)` has no master
switch, because 0 metres already means off.**

### The fix is to delete the switches

`Lens glare` and `Body leans as you swing` are gone. Both are now derived from
their own numbers:

```csharp
GlareActive     => bloom != 1.0 || dirt > 0 || fringing > 0
BodyLeanActive  => leanAimed != 0 || leanReady != 0
```

Bloom defaults to **1.0**, which is stock Tarkov and therefore the off position.
The lean degrees default to **0**. There is nothing to remember, nothing to pair,
and no way to set a value that provably cannot act.

That also removes a class of state the error handlers had to manage: the glare
handler used to write `enabled = false`, and now neutralises the dials instead,
which is the same thing said once rather than twice.

### The diagnosis came from his own screen

The HUD rows read `body lean off`, `shoulder give off`, `lens glare off` in the
video he sent, and the config file agreed. Two independent sources, no guessing,
and the answer was available before a single line of code was read.

That is the payoff for the rule this project keeps relearning: **make the machine
say what it is doing.** Every one of F29, F31, F33 and F42 was a case of a feature
silently doing nothing; the difference this time is it took one frame of video to
know it.

### The lesson worth keeping

**If a setting can be configured into a state where it provably does nothing, the
design is wrong, not the user.** A value that means "nothing" is a better off
switch than a boolean, because it cannot disagree with the value next to it.

---

## F45. Nobody ever bound Player.Physical

The HUD read `hands pool not reached yet` for the whole raid. That wording is part
of why it went unexamined for so long: it sounds like the pool arriving late, not
like the pool being unreachable forever.

`Compat/Member.Get` does **not** bind on demand. An unresolved Member returns null
from every read, does not throw, and is indistinguishable from a member that is
legitimately null. And:

```
grep -c "M_Physical.Bind" Compat/GameRefs.cs
0
```

Never bound. Not once, anywhere. So `Player.Physical` read as null on every frame
since the rollback, and `GetHandsPool` returned at its first line.

The member names were all correct - `Player.Physical : PhysicalBase` is a field,
`PhysicalBase.HandsStamina : Stamina` is a field, both confirmed against the
assembly. Nothing was misnamed. The lookup simply never started.

### It was two features, not one

`IsHoldingBreath` reads `M_Physical` too, so the hold-breath zoom added in F38 has
never fired either. The owner has `Zoom in when holding breath = true` in his
config and has presumably been holding his breath and seeing nothing.

Two features, one missing line, no error in the log from either.

Both now go through `GetPhysical(player)`, which binds against the player's type
on first use and logs once - success or failure - so a third feature reaching for
Physical cannot inherit the same silence.

### This is F29 exactly

F29 was `M_CurrentScope` never bound, which made every optic effect inert while
the log stayed clean. Same failure, same cause, same silence, sixteen findings
apart.

Twice is a pattern, and this one is visible in the source: if a file contains
`M_Foo.Get(` but never `M_Foo.Bind(`, that member can never resolve. So
`tests/BindTests.cs` scans for exactly that. It reports 29 Members declared, 29
read, 29 bound.

### The regression test that passed on the bug

Worth recording, because it nearly shipped hollow.

The first attempt at proving `BindTests` catches this reintroduced the bug by
**commenting out** the `M_Physical.Bind` call. The test passed - the regex matched
the commented text and counted a dead call as a live one.

A scanner that reads dead code as live code is worse than no scanner: it reports
confidence it has not earned, which is precisely how a test goes hollow while
still showing green (F13's lesson, in a new place). `BindTests` now strips
comments before scanning, and is verified against both a commented-out bind and a
deleted one.

### The lesson worth keeping

**"Not yet" and "never" look identical from the outside, and the wording of a
diagnostic decides which one you go looking for.** `hands pool not reached yet`
described a race. The truth was a missing line. A message saying "Player.Physical
not found" would have been read on the first raid.

Diagnostics are not decoration - they are the hypothesis the next person starts
from, and a diagnostic that suggests the wrong hypothesis costs more than none.

---

## F46. My own error message hid the error, and three mods own Prism

*"none of the lense effect worked"*

This time I read his log before writing a line, and it says the code ran:

```
[SPT Free Aim] Lens glare: driving PrismEffects. 7 of 7 fields found, lens dirt texture PRESENT.
```

Seven of seven fields resolved, dirt texture present, no exception. The feature is
running. Something else is eating it.

### Three other mods patch PrismEffects on this install

```
Harmony id=AmandsGraphicsPrismEffectsPatch   -> PrismEffects::OnEnable  postfix
Harmony id=PrismEffectsPatch (SmajlecLights) -> PrismEffects::Awake     postfix
Harmony id=AmandsSensePrismEffectsPatch      -> PrismEffects::OnEnable  postfix
```

And Amands Graphics carries its own settings for the exact fields this drives:

```
com.Amanda.Graphics.cfg:
    Bloom Intensity      = 0.5
    ChromaticAberration  = 0.5
```

Which is a strong hypothesis and **not** a finding. There are two different worlds
consistent with what the owner sees, and guessing between them is how the last six
findings went wrong:

- our write lands and is then overwritten, or
- our write survives and Amands renders bloom through its own chain, so
  PrismEffects' own bloom is simply not what is on screen

So `VerifyPrismStuck` now writes, reads the value back, and logs which world we are
in. That is F34's rule one level further out: **an API call that succeeds is not an
API call that did something, and a field that accepts a value is not a field that
keeps it.**

### The depth of field was failing, silently, for a whole raid

Also in the same log, twice:

```
[Warning:SPT Free Aim] Depth of field: TargetInvocationException while writing - giving up
[Info   :SPT Free Aim] Depth of field: driving ... enabled=False, focalTransform=none
```

Fail, give up, re-resolve, fail again. So the gun blur has not been working either,
and the owner never mentioned it because the message did not look like a failure of
anything he could see.

**And my message is useless.** `TargetInvocationException` is what reflection throws
when the *target* throws - it names the messenger, never the message. I wrote that
handler myself and it discarded the only part worth having.

Fixed two ways:

- `Explain()` unwraps `TargetInvocationException` down to the real inner exception,
  and every reflection handler in `GameRefs` now uses it.
- `DriveDepthOfField` records which field it was writing, so the log names the
  member that threw rather than the method.

The prime suspect is already visible in the stock capture: `enabled=False`, and
`enabled` is the one **property** in that method, the only call that can produce a
TargetInvocationException at all. A destroyed component throws from
`Behaviour.enabled`. Next log will say so outright instead of implying it.

### The lesson worth keeping

**A diagnostic that names the mechanism instead of the cause is worse than none.**
"TargetInvocationException while writing" reads like a real report, passes review,
and tells you nothing - the same failure as F45's "hands pool not reached yet",
which described a race that was actually a missing line.

Two findings running where the bug was findable in seconds and the message sent me
somewhere else. The rule for this codebase now: **if a catch block logs an
exception type, it must unwrap it, and it must say what was being attempted.**

---

## F47. Two config files that cannot see each other

*"Why when I turn on pistol the free aim mod turns off?"*

Asked twice. The first time (F42) the answer was `EmergencyDisable` firing on a
single exception, and that fix was right but it was not this. This time the log has
no exceptions at all - and it has this:

```
[SPT Free Aim] Free aim ON
[SPT Free Aim] Free aim OFF
```

Adjacent lines. That is the MASTER TOGGLE, logging itself, exactly as designed.
The mod was not failing. It was being switched off.

His config:

```
kfmstr.sptfreeaim.cfg:   Master toggle key = Alpha1
```

Tarkov's:

```
Control.ini:   SecondaryWeapon      = Alpha1
               QuickSecondaryWeapon = Alpha1
```

`SecondaryWeapon` is the holster. **Drawing his pistol pressed the mod's master
toggle**, every single time, and the only trace was a log line that looked like a
deliberate keypress.

Nothing was broken. Two files each held a defensible value and neither could see
the other.

### The check pays for itself immediately

`Compat/KeyConflicts.cs` reads Tarkov's control file at startup and compares every
hotkey the mod owns. Run against his actual bindings it found **two** clashes, and
the second one was mine:

```
Master toggle key = Alpha1  ->  SecondaryWeapon, QuickSecondaryWeapon
Stance key        = Z       ->  DropBackpack
Stance key DEFAULT = X      ->  Prone            <- shipped that way
```

The default stance key was `X`, which is Prone in stock Tarkov. Every user who
took the default has been going prone whenever they changed stance, and nobody
reported it because it reads as fumbling the key.

Hunting for a replacement found the real shape of the problem: **almost nothing
near WASD is free in stock Tarkov.** X prone, Z drop backpack, C crouch, V weapon
mounting, B fire mode, T tactical, R reload, F interact, G grenade, Q/E lean.
Default is now `M`, verified free, and the description says so rather than
pretending a good answer exists.

### Parsing without a parser

The file is JSON despite the `.ini` extension, and this deliberately does not parse
it as JSON. A regex over the text cannot throw on a schema change, and a startup
diagnostic is not worth a hard dependency or any chance of taking the plugin down
(F30). The regex was verified against his real file before shipping - it correctly
resolves `Alpha1 -> SecondaryWeapon, QuickSecondaryWeapon` and `X -> Prone`.

### The lesson worth keeping

**When a system has two sources of truth that cannot see each other, something has
to read both.** No amount of care inside either config prevents this class; the
collision only exists in the space between them.

And note what made it findable: the toggle already logged itself. Without those two
lines this would have looked exactly like the F42 crash and I would have gone
hunting for an exception that was not there. That is now three findings running
(F44, F45, F46, this) where the answer was already in the log or on the HUD, and
the work was reading it rather than reasoning about it.

---

## F48. A multiplier cannot lift zero, and a diagnostic that fires too early never fires

Three separate answers, all of them read off his screen and his log rather than
reasoned about. That is the part worth keeping.

### The glare dial was arithmetically incapable

The HUD read:

```
lens glare   bloom x5.00  dirt 3.00  fringe 2.00   value stuck (0.00)
```

The read-back added in F46 did its job perfectly and I nearly misread it. "Value
stuck" means the write survived - and the value that survived was **zero**.

```csharp
expectedBloom = stockBloom * bloomMul;     // 0 * 5 = 0
```

Amands Graphics sets Prism's own `bloomIntensity` to 0 and renders bloom through
its own chain. So the stock value was zero, and a multiplier on zero is zero at
every setting from 0 to 5. He pushed every dial to maximum and the design
guaranteed nothing would happen.

I chose the multiplier deliberately, and the reasoning in the comment was that
"1.0 means stock Tarkov" is easier to picture than an absolute. That is true and
it is beside the point: **a scale factor is only meaningful while the thing it
scales is non-zero.** Absolute now, 0 is off.

Note this is the second time the same instinct has cost a round. F38 restored the
FOV to `HeadBobbing` rather than adding 15 back, and that WAS right - because
HeadBobbing is never zero. The rule is not "prefer relative"; it is "prefer
relative only where the base cannot vanish".

### The optic post stack does not contain what the curved glass needs

This is what the diagnostic was built for, and the answer is a flat no:

```
optic post volume: PostProcessVolume
  profile: PostProcessProfile
    EFFECT ScreenSpaceReflections   enabled=True
current optic sight: NONE FITTED
```

One effect in the entire profile. **No LensDistortion, no Vignette, no Bloom, no
ChromaticAberration.** The rim ring and the edge distortion cannot be switched on
because they are not there; they would have to be constructed and injected into a
PPv2 profile at runtime.

F43 hedged between "two writes away" and "a much larger job". It is the larger
job, and now that is a measurement rather than a guess.

### The diagnostic fired before the thing it diagnoses existed

`current optic sight: NONE FITTED`, and it never ran again, because
`_opticDumped` latched on the first call - which happens as soon as the option is
on, long before a scope is aimed through.

So the half that mattered, the lens material and its shader properties, has never
printed once. **A diagnostic that latches before its subject exists is a
diagnostic that never fires**, and it looks identical to one that ran and found
nothing.

It now only latches once a sight is actually fitted, and says so meanwhile.

### Transparency: the shader swap is the damage, not the alpha

His report: at one setting nothing, one notch up the glass goes opaque - "the
glass is turning untransparent instead". The HUD said `2 parts at alpha 1.00`.

Alpha 1.00 is fully opaque, so the alpha was not the problem. **Swapping the
lens onto a generic blend shader ruins it before alpha is considered**, because
an EOTech window is a specialised reflective-glass material and
`Legacy Shaders/Transparent/Diffuse` cannot be it.

The lens should never have been in the set. It was excluded by NAME matching,
which missed. The game names it exactly - `OpticSight.LensRenderer` - so it is now
excluded **by reference**.

That is F38's lesson again, one object over: four rounds of guessing the pivot
ended when I read the number the weapon carries, and a round of guessing which
renderer is glass ends the same way.

### The lesson worth keeping

**Read the instrument before arguing with it.** "Value stuck (0.00)" contained the
whole first answer, printed in the HUD, and the only work left was noticing that
the number in the parentheses was zero.

---

## F49. The scope is a view inside a view, and it has its own stack

The lens glare finally worked, and the owner's first reaction was the correct
criticism: *"wow, but you are applying it to the entire view, not on the
transparent lense material, perhaps would be most appropriate on the sniper scope
view (since it is view in the view)."*

Right, and the game agrees. `OpticCameraManager.Init`:

```
GetComponent<PostProcessVolume>()  ->  _postProcessVolume
GetComponent<PostProcessLayer>()   ->  _postProcessLayer
```

and `SetSSR` shows exactly how BSG drives it:

```csharp
_postProcessVolume.profile.TryGetSettings<ScreenSpaceReflections>(out s);
s.enabled.value = on;
```

So the scope image has its own PostProcessing v2 stack, entirely separate from the
main camera's Prism. Effects put there touch nothing outside the tube.

### One mechanism, all three cues

F43 listed three things the reference footage shows and split them across two
mechanisms, one of which was "a much larger job". They are all PPv2 effects, so
they are all the same job:

```
Bloom               - the bright hotspot on the glass
ChromaticAberration - colour fringing at the edge
Vignette            - the ring, darkening toward the rim
LensDistortion      - the round bow that says the glass is curved
```

The last two are exactly what he pointed at twice - *"nice circle around the ages,
showing that the glass is curved"* and *"round distortion on the edges of the
lense"* - and they were the pair I could not deliver through Prism at all.

### Adding to a profile that ships nearly empty

F48's dump showed the optic profile holds `ScreenSpaceReflections` and nothing
else, so these have to be created, not enabled. The saving grace is that
`PostProcessProfile` carries **non-generic** overloads:

```
AddSettings(Type) / RemoveSettings(Type) / HasSettings(Type)
```

Generic-method reflection would have made this several times the size. Everything
added is recorded and removed on release; anything already present is left in
place and only disabled. The profile belongs to the game.

And the PPv2 rule that would have wasted the round silently, for the third time
(F34, F36): a `ParameterOverride` only participates when `overrideState` is true.
`SetParam` writes both, always.

### On the full-screen version

It stays, and it is not redundant - a bodycam lens does scatter across the whole
frame. But its defaults are zero, it is the wrong tool for a scope, and the owner
found that out the moment it worked. The two now sit in separate config sections
so nobody reaches for the wrong one.

### A near miss worth recording

The edit that wired this in was applied by a script whose third anchor did not
match. The first two edits had already been made in memory, the assert fired
before the write, and **the file was never saved** - so `OpticStack` compiled
cleanly as completely dead code, called from nowhere.

The build succeeded and grew by 9 KB, which is exactly what a working change looks
like from the outside. It was caught by grepping for the call site rather than
trusting the build. Same shape as F31's stale DLL: **a green build is evidence the
code compiles, never evidence it runs.**

---

## F50. I made the owner the build server for the whole project

*"I have another agent that solves me the interior light problem and he compiles
everything itself, why do I need to build myself for you everytime?"*

No good reason. That is the entire finding.

I have compiled this mod with `mcs` on **every single turn** of this project, as
the verification step - checking it builds, checking the reference list, checking
the artefact size after F31. Then I handed him a git commit message and asked him
to open Visual Studio and build it again.

The other agent, working on `InteriorLights` in the same folder tree, compiles
with the same `mcs` against the same staged assemblies and drops the DLL straight
into `BepInEx\plugins`. Same technique, one fewer human step.

### Why it went unnoticed

Because it never failed. Every turn ended with a plausible instruction, he
followed it, and the loop closed. Nothing in the logs or the HUD could have
flagged it, because it is not a defect in the software - it is a defect in the
process, and the software has no view of that.

That is worth naming: **this project's whole discipline is "make the machine say
what it is doing", and none of that machinery can see a wasted human step.** It
took him comparing two agents to spot it.

### It also explains an earlier bug

F47's stale `GameRefs.cs` - my write reported success and the file on disk was the
old version - is most likely this same arrangement biting. With Visual Studio
holding the solution open, an editor buffer loaded before my sync gets written
back over it on the next save. Two writers, one file.

Shipping the DLL from here removes one of the two writers.

### The rule

`CLAUDE.md` now says it plainly: compile here, install the DLL with
`device_commit_files`, verify it landed, and check the output really is a plugin
(`BepInPlugin` attribute, `BaseUnityPlugin` base, new types present) rather than
trusting a green compile - which F31 and F49 both proved means nothing on its own.

### The lesson worth keeping

**Ask what you are asking the person to do that you could do yourself.** I had the
compiler, the assemblies, and the file-write tool in hand for the entire project,
used all three every turn, and still outsourced the last step by habit.

---

## F51. The lens shader, at last - and a volume is not owned by a camera

Two results in one log, one of them ten rounds overdue.

### CW FX/OpticSight

The optic dump finally ran with a sight fitted, and printed the thing that has
been guessed at since F26:

```
lens renderer: linza_mode_001
MATERIAL scope_dovetail_belomo_pso_1_4x24_LOD0_linza_1 (Instance)
shader  CW FX/OpticSight
    Texture  _MarkTex          the reticle mark
    Texture  _MaskTex
    Texture  _MaskTex2
    Range    _MarkLightness    reticle brightness
    Vector   _ShiftDirection
    Vector   _Shifts
    Vector   _Scales
    Range    _NormalHideness
```

**No `_Color`. No `_MainTex`. No albedo of any kind.**

So sight transparency by shader swap was never possible - there is nothing to
carry across. F34 diagnosed "EFT weapon materials do not keep albedo in
`_MainTex`" and treated it as a thing to work around by searching harder for the
albedo. The truth is simpler and worse: on the lens there is no albedo to find,
because the shader composites a reticle over a render texture rather than
shading a surface.

Three separate attempts (F26, F34, F48) died on a fact that one line of output
would have settled. The diagnostic existed for most of that time and never ran,
for the reasons in F45 and F48 - never called, then latching before a sight was
fitted.

What IS there is interesting on its own: `_MarkLightness` is reticle brightness
and `_NormalHideness` is something worth probing. Those are real dials on real
glass, and they are the honest replacement for the transparency idea.

### A PPv2 volume belongs to a layer, not to a camera

The other half. The effects went into the optic profile and came out on the main
screen:

```
EFFECT ScreenSpaceReflections  enabled=True
EFFECT Bloom                   enabled=True    <- ours
EFFECT ChromaticAberration     enabled=True    <- ours
```

F49 was right that the optic has its own stack and wrong about what that buys.
In PostProcessing v2 a `PostProcessVolume` is not owned by the camera it sits on.
Every `PostProcessLayer` applies every volume whose GameObject **layer** is inside
that layer's `volumeLayer` mask. Two cameras with overlapping masks both apply the
same volume.

So "the optic's own volume" is a name, not an isolation guarantee, and I read it
as one.

That is the same class of error as F40 - `LocalRotateAround` was named as though it
rotated about a point, and did not. **A name describes intent; only the mechanism
describes behaviour.**

### Measuring rather than guessing which way it leaks

There are two consistent explanations - the main layer's mask includes the optic
volume's layer, or the optic layer's mask excludes it so only the main camera ever
applies it - and they need opposite fixes. Guessing between them is how the middle
of this project went.

The dump now prints, for BOTH stacks: the volume's GameObject name, layer index
and name, `isGlobal`, `priority`, `weight`, `enabled`; and the layer's
`volumeLayer` mask decoded into layer names. Four numbers settle it.

It also re-dumps whenever the fitted sight changes, keyed on the sight instance
rather than a bool, so a holo and a scope both report instead of only whichever
was equipped first.

---

## F52. `volumeLayer = -1` is EVERYTHING, and my own sentinel hid it

F51 built the dump that would settle which way the leak ran. The dump ran, the
owner sent it back, and it said this:

```
OPTIC volume  obj=BaseOpticCamera(Clone)  layer=8 (Player)  isGlobal=True  priority=0  weight=1  enabled=True
OPTIC layer   obj=BaseOpticCamera(Clone)  enabled=True  volumeLayer mask=-1
MAIN  volume  obj=FPS Camera              layer=0 (Default) isGlobal=True  priority=0  weight=1  enabled=True
MAIN  layer   obj=FPS Camera              enabled=True  volumeLayer mask=-1
```

`-1` is every bit set. As a `LayerMask` it is **Everything**. The FPS camera's
`PostProcessLayer` looks for volumes on all 32 layers; the optic volume is
`isGlobal` and lives on layer 8; therefore the FPS camera applies the scope's
volume, and every effect "added to the scope" arrives on the whole screen. The
owner, twice: *"It again applies it to the entire screen again, but it should
apply this only to the image behind the scope or glass of a sight."*

### The part that is mine

`MaskNames()` used `-1` as its **failure sentinel** and printed
`"unreadable"` for it. The caller used `-1` the same way, to decide whether the
read had worked. So the one diagnostic written specifically to answer this
question printed *"could not read the mask"* for the value that **was** the
answer, and did it in the same paragraph as the correct numbers.

`-1` is not a spare value here. It is the most common real value a `LayerMask`
takes, and it is what Unity writes when a designer ticks Everything. Reserving it
cost a full round-trip through a raid.

**A sentinel has to be a value the domain cannot produce.** For a `LayerMask`
there is no such `int`; the "did it work" answer needed its own `bool`, which is
what it has now.

This is F46 again in a different costume - there the error message named the
messenger instead of the error, here the diagnostic named its own sentinel
instead of the finding. Both times the instrument was built correctly and then
told to lie about one specific case.

### The fix is one bit

Clear the optic volume's layer from the **main** layer's mask:

```csharp
int before = mask.value;              // -1, Everything
int after  = before & ~(1 << 8);      // everything except Player
```

The main camera keeps its own volume on layer 0 and every other layer it had; it
simply stops reading the one that belongs to the scope. The optic camera's own
mask stays Everything, which is harmless - it is the only camera that *should*
apply the scope's volume. Restored exactly on release, because `volumeLayer` is a
`LayerMask` **struct** and has to be written back into the field after `.value`
is set, not mutated in place through a boxed copy.

Narrowing the optic layer's mask instead would have done nothing. The leak was
never on the scope's side.

### Layer 8 has tenants, so count them first

Layer 8 is Player. Dropping it from the main camera's mask also drops any *other*
global volume parked there, and a mod that silently deletes an unrelated
post-processing effect from the main view is worse than the bug it fixed.

So `Census()` walks every `PostProcessVolume` in the scene before touching
anything:

* the scope is alone on its layer -> drop that bit, done;
* it has company **and** an unused layer exists -> move the scope's own
  GameObject to the unused layer and drop *that* bit, which cannot cost anything
  because nothing is on it;
* it has company and no layer is free -> drop the shared bit anyway, and name in
  the log exactly what went with it.

A layer counts as taken if it carries any volume **or** if Unity has a name for
it, so the mod never squats on something Tarkov means to use.

### The destroyed profile that still read as non-null

Found while writing the above, not reported by anyone. `OpticStack` held the
profile as `object`:

```csharp
private static object _profile;
if (_profile != null) return true;      // wrong
```

`UnityEngine.Object` overloads `==` so that a **destroyed** object compares equal
to null. That overload is chosen by the *static* type. Held as `object`, the
comparison falls back to reference equality, and a destroyed profile reads as
alive forever.

The optic camera is rebuilt when the weapon changes. So after one weapon swap the
mod was writing effects into a `PostProcessProfile` that Unity had already
destroyed, reporting `Ready`, and showing nothing - which is a very good match for
*"none of the lens effect worked"* and for *"I tried both optic, and holo sight"*.

`ProfileAlive()` casts back to `UnityEngine.Object` before comparing, so the
overload applies and a dead profile now forces a re-resolve on the next frame.

**A Unity null check only works at a static type Unity can overload.** Storing a
Unity object in `object`, `var` from a reflection call, or a dictionary of
`object` quietly disarms it - and reflection returns `object` every time, so this
is a trap that gets set every time reflection touches a component.

---

## F53. The install said "written", and wrote the previous build

First ship under F50's rule. Compiled clean, verified with Cecil, copied to the
staging path, committed:

```
device_commit_files -> {"written":["C:\\SPT - Dev\\BepInEx\\plugins\\SPTFreeAim.dll"],"rejected":[]}
```

Then the directory listing:

```
SPTFreeAim.dll   size 151552   mtime 21:12:46      <- 12 seconds ago
```

The build was **155648** bytes. The mtime was current, the result said `written`,
`rejected` was empty, and the file on disk was the *previous* build.

Staging it back and hashing settled it:

```
device      744041d2ff2a6b156f2694d8e381916d   151552
build       1bb1f7fafa0e9504e36939bbb529bae9   155648
```

The staging path had been used for an earlier ship, and the transfer served that
earlier snapshot. Writing the same build to a **new, unique filename** and
committing that put the right bytes on disk, confirmed by matching md5.

Two things worth keeping.

**A success result describes the call, not the outcome.** This is F34 and F48
again - the API said yes, the state did not change. The rule that survives is the
same one: a write is not done until something that reads the destination says so.
Here the size in a directory listing was enough to catch it, and only a hash was
enough to *prove* it.

**Almost-right is the dangerous failure.** A refusal would have been obvious. A
plausible file - right name, right place, fresh timestamp, `written` in the log,
4 KB short - would have shipped silently and produced a raid in which none of the
new code existed, and the next hour would have been spent debugging the source of
a DLL that was never installed. F49 shipped dead code that compiled; this ships
live code that never arrived. Both look green.

The install step now: unique staging filename per build, commit, list to check
the size, stage back and compare md5 against the build. Four calls, and the
alternative is debugging the wrong binary.

---

## F54. Two features, one name, and the wrong one was easy to find

F52 isolated the scope's post stack from the main camera and I said the leak was
fixed. The owner came back with: *"glare is still applied to the entire player
view, not on the lense or glass of the lense."*

He was right, and F52 was irrelevant to what he was looking at. His HUD:

```
lens glare    bloom x0.89  dirt 3.0 ...
optic glass   off
```

There were **two** glass features in the mod:

| dial | section | drives | reaches |
|---|---|---|---|
| `Glare: bloom / lens dirt / colour fringing` | 6. Body | `CameraManager._prismEffects` | the whole screen |
| `Optic: bloom / colour fringing / rim darkening` | 7. Optic glass | the scope's own PPv2 stack | inside the tube |

He had the first set turned up and the second at zero, which is exactly what the
HUD says. So he was tuning the whole-screen feature, four rounds running, while
the scope-only feature sat off.

### Why that was mine, not his

BepInEx sorts the config panel by section and then alphabetically. The `Glare:`
dials sat in **6. Body**, in the middle of the block he was already scrolling
through for arm drain, body lean and gun blur. The `Optic:` dials sat in a
separate section further down. The dials that could not do what he asked for were
directly in his path; the ones that could were somewhere else, and the two sets
described themselves in nearly the same words.

`PrismEffects` is a component on the **main camera**. It has no notion of a
scope. Every value written to it lands on the whole player view, and there is no
tuning that moves it onto the glass - the request and the mechanism were never
compatible. I built it anyway in F43, left it in place after F49 found the right
mechanism, and let both ship.

**Two features whose names a person cannot tell apart are one feature and one
trap.** F44 said a setting that can be configured into doing nothing is a broken
design; this is the sharper version - a setting that works perfectly, does the
thing nobody asked for, and is easier to find than the one that does.

The Prism drive path is deleted. Not deprecated, not renamed - deleted, so it
cannot be found again. One thing survived it: Tarkov's lens-dirt **texture**,
which is what makes bloom read as light scattering off glass rather than as a
glow. PPv2's `Bloom` takes a `dirtTexture` too, so the texture moved onto the
scope's stack as `Optic: lens dirt` and the driving stays inside the tube.

### The holo sight cannot have this at all

The other half of the answer, and it is a hard no. From the assembly:

```
EFT.CameraControl.OpticCameraManager
    Camera            _camera
    RenderTexture     _renderTexture
    PostProcessVolume _postProcessVolume
    PostProcessLayer  _postProcessLayer
    OpticSight        _currentOpticSight        <- only an OpticSight
    OnOpticSightEnabled / OnOpticSightDisabled

CollimatorSight : MonoBehaviour
    MeshRenderer  CollimatorMeshRenderer
    Material      CollimatorMaterial
```

A magnified optic is a **second camera rendering into a texture**, which is why
it can have its own post stack. A holo or red dot is a **mesh with a material on
it** - no camera, no render texture, no volume, nothing. What you see "through"
the glass of an EOTech is the ordinary main view with a reticle drawn on top.

So there is no image-behind-the-glass to post-process on a red dot, and no amount
of work produces one. The only surface that exists there is
`CollimatorSight.CollimatorMaterial`, which is a shader problem, not a
post-processing one. The screenshot that came with the report was an EOTech 553 -
so even the correct feature, correctly isolated, would have shown him nothing.

That fact now leads the HUD:

```
sight         magnified optic, tube rendering
sight         NO scope image - a holo/red dot has no optic camera
```

Always drawn, on or off, above the dials it governs. **A dial that cannot apply
has to say so where the dial is**, not in a log, and not only after someone
spends a raid finding out.

---

## F55. A ceiling I invented, and two dials I had kept for myself

The owner, on the first build where the glass effects landed inside the tube:
*"the lense works great, distortion, darkening, and bloom works perfect! I would
increase the amount of amplification of the light when we looking at the bright
lamp should be much brighter, but the dial is limiting to increase it any
further."*

Two separate mistakes behind one symptom.

### The cap was mine, not the engine's

`Optic: bloom` was `AcceptableValueRange<float>(0f, 5f)`. PPv2's own declaration:

```
FloatParameter intensity   [Min(0)]
    "Strength of the bloom filter. Values higher than 1 will make bloom
     contribute more energy to the final render."
```

`Min(0)` and nothing else. There is no maximum. The 5 was a number I picked while
writing the dial, on no evidence, and it then read to the owner as a limit of the
effect rather than a limit of my imagination. Raised to 30.

**A range on a config dial is a claim about the mechanism.** If the mechanism has
no upper bound, inventing one is asserting something untrue, and the person on the
other end has no way to tell the difference between "the engine stops here" and
"the author stopped here".

### Turning intensity up was the wrong answer anyway

The request was for the LAMP to be brighter. Raising `intensity` alone does not
do that; past a point it hazes the entire tube, because intensity scales
everything already above the bloom threshold. Two other parameters decide which
pixels those are, and I had hardcoded both:

```csharp
SetParam(s, "threshold", 0.9f);    // what blooms
SetParam(s, "diffusion", 7f);      // how far the glow reaches
```

Threshold is the dial he actually needed. Raise it and only a genuinely bright
source qualifies, at which point intensity can go as high as he likes and the
rest of the image stays where it is. Both are now `Optic: bloom threshold` and
`Optic: bloom spread`, with `softKnee` pinned at 0.2 so the threshold has teeth,
and `clamp` written explicitly so a low profile value cannot quietly cap the
bright-light case this is for.

**A hardcoded constant is a decision taken away from the user without telling
them.** Neither of these was tuned; they were placeholders that shipped, and they
happened to sit on exactly the axis he wanted to move.

### Seven floats in a row

`Drive` was about to take `(bloom, threshold, spread, dirt, fringe, vignette,
distortion)`. Seven positional floats is a transposition that compiles, builds
green, and shows up only as two dials doing each other's job. It takes a `Glass`
struct with named fields instead, and the build check now asserts that every
field of that struct is READ inside `Drive` - the F45 shape, where a member that
is never read is a dial that silently does nothing.

---

## F56. The scope has no colours brighter than white, and I told him to raise the threshold

The owner: *"so glare didn't work, and options below glare that create optical
distortion, and darkening the edges worked well."*

Alphabetically in section 7 that is bloom failing while **edge distortion** and
**rim darkening** succeed. All four go through the same profile, the same
`AddSettings`, the same volume, the same isolated stack. Three work. One does not.
So the failure is not in the plumbing, and the dump that would have told me which
one to blame is the difference between the effects themselves.

Vignette and LensDistortion are screen-space operations: they move and darken
pixels and do not care how bright anything is. Bloom is the only one of the four
that asks a question about brightness.

### What the scope renders into

`OpticCameraManager.SetResolution`, decompiled from IL:

```csharp
RenderTextureFormat fmt = Camera.allowHDR ? ARGBHalf : ARGB32;
_renderTexture = new RenderTexture(res, res, 24, fmt, Default);
Camera.targetTexture = _renderTexture;
Shader.SetGlobalTexture(_camTexId, _renderTexture);
```

**ARGB32 clamps at 1.0.** In that buffer a lamp and a white wall are the same
number. Bloom's whole job is to find the pixels brighter than a threshold, and in
an LDR scope image there is nothing above white to find. Threshold 0.9 leaves a
razor-thin band between 0.9 and 1.0 to work with, which is why it looked weak but
present; the owner had already said as much - *"the amount of amplification of the
light when we looking at the bright lamp should be much brighter"* was him
describing a clamped buffer without knowing it.

### And then I made it worse

F55's advice was to raise `Optic: bloom threshold` to about 1.5 so only bright
things would bloom. In an LDR buffer, **nothing is above 1.0**, so a threshold of
1.5 selects the empty set. The dial I added to fix his problem is what turned a
weak effect into a dead one, and the next report was "glare didn't work".

The advice was not wrong in general. It was wrong here, and I gave it without
checking the one thing that decides whether it applies. **A tuning recommendation
is a claim about the data the effect will see.** I had never looked at the data.

### The fix

Set `allowHDR` on the optic camera and let the game rebuild its own texture:

```csharp
cam.allowHDR = true;
setRes.Invoke(_ocm, new object[] { OpticFinalResolution });   // the GAME's method
```

Calling the game's `SetResolution` rather than building a `RenderTexture` here is
not politeness, it is required: that method also does
`Shader.SetGlobalTexture(_camTexId, _renderTexture)`, and the scope lens shader
samples that global. A hand-rolled swap would leave the lens reading a texture
nobody renders into, which is a black scope and an afternoon of confusion. The
build check now asserts this file never constructs a `RenderTexture` at all.

Capped at three attempts: `SetResolution` destroys and recreates the texture, so
if anything in the game sets `allowHDR` back each frame, an uncapped retry would
rebuild the scope's render target sixty times a second.

### The HUD says the format now

```
optic glass    bloom 15.0 over 1.5 spread 10 dirt 1.0 fringe 0.30 rim 0.40 bow -0.10
  scope buffer HDR forced on (ARGB32 -> ARGBHalf)   bloom live 15.0   dirt on
  isolation    main mask -1 -> -257 (dropped layer 8)
```

and, in the case that produced this finding:

```
  scope buffer LDR (ARGB32)   bloom DEAD: threshold 1.5 > 1.0 in an LDR scope
```

That second line is the one worth keeping. **An effect that is running correctly
on data that cannot contain what it looks for is indistinguishable, from the
outside, from an effect that is broken** - and the person tuning it has no way to
tell those apart. The verdict string is cheap; the raid it saves is not.

---

## F57. The check was right, printed in red, and nothing acted on it

Third report of the same symptom: *"when choose pistol/sidearm the free aim
disables itself."* Twice before I looked for a crash. This time I read the log
first, which is what I should have done in round one.

```
[Info :SPT Free Aim] Free aim OFF
[Info :SPT Free Aim] Free aim ON
[Info :SPT Free Aim] Free aim OFF
```

No exception. No emergency disable. Those three lines come from the **toggle key
handler**. Nothing was failing; the key was being pressed.

His config:

```
Master toggle key = Alpha1
```

Tarkov's `Control.ini`:

```
SecondaryWeapon      = Alpha1
QuickSecondaryWeapon = Alpha1
```

Drawing the pistol toggled the mod off, every single time.

### The part that makes this F57 and not F47 again

F47 diagnosed exactly this, wrote `Compat/KeyConflicts.cs` to detect it, and
shipped a red warning on the HUD and in the log. That checker **ran, was
correct, and reported the clash on his screen for weeks** while the code went on
acting on the key. I built the instrument and then left the wire unattached.

**A diagnostic that requires the user to notice it and act on it, for a condition
the code can see perfectly well, is not a fix.** It is a fix shaped like a
warning. The checker now returns a decision, not a sentence:

```csharp
if (Cfg.ToggleKey.Value.IsDown() && !KeyConflicts.IsBlocked("Master toggle key"))
```

Every hotkey read in `Update` is guarded the same way, and the build check
asserts there are at least as many `IsBlocked` calls as `IsDown` calls in that
method, so a hotkey added later cannot quietly skip the guard.

### And the reason the F47 fix reached nobody

F47 also changed the *default* to a safe key. That did nothing for him, and I did
not notice for three rounds: **BepInEx writes a default only for a key it has
never seen before.** His config had already saved `Alpha1`, so the new default
was never consulted. The fix shipped, the source looked correct, and the
installed behaviour did not move.

That is a general trap for any mod with persisted settings. Changing a default
fixes new installs and nobody else. A bad value that has already been written can
only be repaired by *writing over it*, which is what `RescueClashingToggle` now
does: if the master toggle clashes, move it to the first key Tarkov does not use,
save, and say so loudly in the log and on the HUD. It only ever moves a key that
is provably clashing, and only onto a key that provably is not.

**Where a setting lives decides who a fix reaches.** A default is source; a saved
value is state; editing the first has no effect on the second.

---

## F58. Three effects were fine and the fourth was twenty times too small

*"that color distortion on the edge of the lense is gone"* - while edge
distortion and rim darkening were fine. The startup dump said the effect existed
and was on:

```
EFFECT ChromaticAberration   enabled=True
```

So it was added, enabled, in the right profile, on the right camera, and
rendering nothing anybody could see. From `ChromaticAberrationRenderer.Render`:

```
ldfld  ChromaticAberration::intensity
ldc.r4 0.05
mul
callvirt MaterialPropertyBlock::SetFloat(_ChromaticAberration_Amount)
```

**The engine multiplies the dial by 0.05 before the shader sees it.** A dial of
1.0 reaches the shader as 0.05, and his 0.30 as 0.015 - a fraction of a pixel at
scope resolution.

I had already handled exactly this for its neighbour:

```csharp
SetParam(s, "intensity", distortion * 100f);   // LensDistortion is -100..100
```

So the habit existed. I applied it to one effect, checked the declared range of
two more, and never looked at the fourth. Vignette's 0..1 happens to be its true
range and Bloom's had no bound at all, so the two I skipped were harmless and
the one I skipped was not. Three out of four is the worst possible result here,
because it makes the fourth look broken rather than mis-scaled.

**A parameter's declared range is not its effective range.** `RangeAttribute(0,1)`
on `ChromaticAberration.intensity` is an editor hint; the shader's usable range
is 0..1 of `_Amount`, which is 0..20 of the dial. The dial is now scaled by 20
in code so 1.0 means what a person would expect it to mean, `fastMode` is written
explicitly so a profile default cannot silently pick the weaker shader path, and
every one of the four effects gets a read-back line on the HUD:

```
  live   bl:10.00  ch:6.00  vi:0.40  le:-10.00
```

Four numbers, every frame, showing what the engine will actually use. The whole
class of "it is enabled but nothing happens" is visible in that row now, which is
where F48, F56 and this one all would have been caught in one raid instead of
three.

---

## F59. The red dot's glass, measured instead of guessed

F54 closed the door on post-processing for a collimator and left the material
open. The owner asked the right follow-up: *"can we do anything with the surface
of that glass? Any flare? color of that glass or anything like that?"*

The shader lives in an asset bundle, so nothing readable from `Assembly-CSharp`
answers it. Rather than build a tint dial and hope a tint property exists, the
dump was extended to print every collimator's material, its shader, and every
property **with its current value**. One raid:

```
MATERIAL scope_base_aimpoint_micro_t1_LOD0_linza
shader   CW FX/Collimator     renderQueue 4011
    Color    _Color      = RGBA(1.000, 0.071, 0.071, 1.000)
    Texture  _NoiseTex   = none
    Texture  _MarkTex    = scope_base_aimpoint_micro_t1_mark
    Texture  _FadeTex    = mask2
    Vector   _MarkShift  = (0.00, -150.00, 0.00, 0.00)
    Float    _MarkScale  = 1
    Float    _HDR        = 3
    KEYWORDS
```

Seven properties, and the **values** name them better than the names do:

* `_Color` is red, on a sight whose dot is red. It is the reticle colour.
* `_HDR` is 3, a multiplier above white. That is the dot's headroom, and the main
  camera's bloom is what turns headroom into glare. **The flare was already wired
  and simply turned down.**
* `_MarkTex` / `_MarkShift` / `_MarkScale` are the reticle's texture, parallax
  offset and size. "Mark" is BSG's word for reticle here, not for a smudge -
  which the value would not have told me if I had only printed names.
* `_FadeTex` is the rim falloff.
* `_NoiseTex` is **empty**, the one free slot on the shader, named exactly like a
  glass-imperfection input.

**Printing the value, not just the name, is what made this readable.** A property
list alone would have left `_Color` and `_MarkTex` ambiguous; `RGBA(1, 0.071,
0.071, 1)` on a red dot is not ambiguous at all.

### What is therefore impossible

There is no albedo, no `_TintColor`, no base map. This shader draws a reticle and
fades at the edges. It does not colour what is behind it, so a green-tinted
window is not available at any level of effort, and the honest answer to "colour
of that glass" is no. What IS available is the colour of the thing drawn ON the
glass.

### sharedMaterial, and why we write to it anyway

```csharp
CollimatorSight.Awake:
    CollimatorMaterial = CollimatorMeshRenderer.sharedMaterial
```

The instinct is to swap in a per-renderer instance so the asset is never touched.
That would be wrong here: the game caches and writes to the shared material
itself, so displaying our own copy would leave the game writing to an asset
nobody renders, and the in-game reticle brightness control would silently stop
working. Compatibility means writing where the game writes.

So this captures every value it touches first and restores all four on release.
These materials are loaded from bundles into memory and nothing here can reach
disk, so the worst case is a restart rather than a permanently green sight. The
capture is keyed on the material's instance id, not the renderer's, because two
sights of one model share a material and capturing it twice would record our own
written value as the original.

The build check now asserts that every shader property the driver writes is also
captured and restored, by matching the literal in all three method bodies.

### A test that could only fail

That check was written first as "does `Drive` reference the field `P_Color`", and
it failed on all four properties. `const string` is **inlined by the compiler**:
there is no field reference left in the IL, only `ldstr "_Color"`. The test was
looking for something the compiler had already removed.

F45's rule again from the other side. There the regression test passed on a
bugged file because the regex matched commented-out code; here it failed on a
correct file because it matched a construct that does not survive compilation.
**A check that reads compiled output has to know what the compiler does to the
source.**

---

## F60. The reticle work is reverted, and it is not what broke the scopes

The owner: *"the changes of the retical didn't work, but it killed the fix we did
for the scopes, so neither one of these works now."*

Rolled back first, diagnosed second. The reticle driver, its seven config dials,
its HUD row and the F59 latch change are all removed; the installed binary is the
one he last ran clean.

Then the log, from the session that went wrong:

```
line 3783   Optic glass: main mask -1 -> -257 (dropped layer 8).      <- once
...
line 6430   MAIN layer  FPS Camera  volumeLayer mask=-1  -> EVERYTHING
line 6480   MAIN layer  FPS Camera  volumeLayer mask=-1  -> EVERYTHING
line 6688   MAIN layer  FPS Camera  volumeLayer mask=-1  -> EVERYTHING
```

The isolation applied **once, in the first raid of the session**, and every later
raid shows the main camera back at EVERYTHING. F52's fix stops working from the
second raid onward, and the scope's volume leaks to the whole screen again.

`IsolateFromMainCamera` is called only from `Resolve()`, and `Resolve()` opens
with:

```csharp
if (ProfileAlive()) return true;
```

So once the profile is alive the isolation is never revisited, while the FPS
Camera it narrowed is a per-raid object that comes back with a fresh mask of -1.
The isolation is applied to one camera and remembered as if it were a property of
the session.

**This is not caused by the reticle change.** It is a defect that has been in
every build since F52 and only shows from the second raid of a session; the
reticle build is simply when enough back-to-back raids were run to hit it. Saying
otherwise would be convenient and wrong.

The mechanism above is the leading explanation and not yet proven: whether the
profile survives the raid transition or the optic camera is rebuilt, what the log
establishes is narrower and sufficient - **the isolation did not re-apply, and
nothing checked.** The fix belongs where the check belongs: verify the mask every
frame in `Drive`, where the effects are already being written, rather than once at
resolve time.

### The other half of the same mistake

F59 keyed the dump on the collimator count so a red dot would re-dump. That count
is `FindObjectsOfType<CollimatorSight>` over the whole scene, which counts **every
bot's red dot**, so it churned 1, 1, 2, 1, 0, 7 and the dump fired **69 times in
one session**. Reverted with the rest.

Both halves are the same error in different clothes. **State that belongs to an
object was tracked as if it belonged to the session** - the mask remembered
against no camera, the dump keyed against a population rather than a fitting. A
latch is a claim about what can change; get that wrong and it either never fires
or never stops.
