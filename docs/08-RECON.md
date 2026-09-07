# Reconnaissance checklist

What to look up, in what order, and what each answer changes. Replaces the
open-ended "search the decompiled solution" step in `03-SETUP.md` Phase F.

The ordering principle: **anything answerable without the game comes first,
then anything answerable in a raid, then dnSpy.**

Most of the original session-1 list has already been answered by reading the
assembly's metadata directly - see `docs/07-FINDINGS.md` F10. What is left below
is what genuinely needs the game running.

---

## Session 1 — in the game, no dnSpy needed

Build the scaffold, launch, load an offline Factory raid. Every check here is a
keypress and a log line.

### R1. Do the reflection lookups resolve? — MOSTLY PRE-ANSWERED

Every name was verified against your actual `Assembly-CSharp.dll` (F10), so this
should just confirm. Read the BepInEx console at startup; it prints a block:

```
GameRefs OK
  MovementContext.Rotation -> MovementContext.Rotation (property, writable)
  body bearing drivable (Intercept possible): True  [SetRotation(Vector2) available]
  PWA.HandsContainer -> ProceduralWeaponAnimation.HandsContainer (field, writable)
  HandsContainer.WeaponRootAnim -> PlayerSpring.WeaponRootAnim (field, writable)
  ...
  LocalRotateAround: game implementation
  camera recoil hook: AddHandRecoilRotateToCamera
```

| What you see | What it means |
|---|---|
| all lines resolved | As expected. Move on |
| any `MISSING` | That name moved since the scan. Fix `Compat/GameRefs.cs`, nothing else |
| `LocalRotateAround: FALLBACK` | The game's extension was not found; the pivot uses our reimplementation and `PivotDistance` needs re-tuning |
| `camera recoil hook: NOT FOUND` | Recoil decoupling only; everything else still works |

The stance probes report separately, on the first frame a local player exists -
they cannot resolve before that.

### R2. Does the offset apply to the weapon at all? (F9 HUD)

Turn on the HUD, raise the weapon, move the mouse. The `offset` row should move.

Then look at the gun. If the weapon does not visibly swing, or swings the wrong
way, use **Invert yaw / Invert pitch / Swap axes** in the F12 menu. The axis
mapping lualeet uses (`pitch -> X, yaw -> Z` in weapon local space) is not
obvious and is not documented; expect to need at least one of these toggles.

**Done when:** a steady mouse turn visibly leads the gun ahead of the view, and
stopping brings it back to centre in about a second.

### R3. Is the body bearing actually drivable? (F10 probe)

Answers `05-OPEN-QUESTIONS.md` Q4. Metadata says yes - `Rotation` is public and
settable - but metadata cannot tell you whether the game honours the write or
stamps over it later in the frame. That is what this measures.

Stand still, look at a landmark, press F10.

| Log says | Do this |
|---|---|
| `STUCK AND HELD` | Intercept is viable. Switch the mode and compare feel |
| `STUCK THEN LOST` | Go to session 2, question R6 — you need to find what rewrites yaw |
| `REJECTED` | Stay on Compensate. Do not spend the dnSpy budget |

Also record: **did the view visibly jump 20°?** If it did not, yaw is not what
the camera reads from and the whole `MovementContext` approach is wrong. That is
a bigger finding than any of the above.

### R4. Do shots follow the barrel? (Q5 — THE ONE THAT MATTERS)

Do not defer this. It is the only remaining item that can add weeks.

1. Set cone to 25° and cap to 40° so the offset is unmistakable.
2. Stand still, 10 m from a flat wall, weapon raised.
3. Push the gun to the cone edge and hold it there. Fire one round.
4. Is the impact on the **muzzle line**, or at **screen centre**?

| Result | Consequence |
|---|---|
| Muzzle line | Done. Matches the spec — the sight always shows true aim |
| Screen centre | The shot ray is built from the camera. Find where the firearm controller builds it and apply the offset there. **+2 to 3 blocks** |

Then put the cone back where it was.

### R5. Does the camera offset survive the frame? (Compensate mode only)

Watch for jitter, or for the view snapping back toward the gun each frame. That
means something later in the frame is overwriting
`HandsContainer.CameraTransform.localRotation` — almost certainly the camera
recoil step (`method_19`, already resolved in `GameRefs`). Move the camera write
into a postfix on that method.

---

## Session 2 — dnSpy, only if session 1 says so

Export `Assembly-CSharp.dll` per `03-SETUP.md` Phase D. Each question below is
conditional; skip any whose trigger did not occur.

### R6. What rewrites the bearing after our hook? — only if R3 said STUCK THEN LOST

Search for callers of `MovementContext.set_Rotation` and `SetRotation`, and for
whatever reads mouse input. You are looking for the last writer in the frame;
patch after it, or prefix it.

While you are there, read `set_Rotation` itself. It ends with a hands-to-body
angle correction against `EFTHardSettings.HANDS_TO_BODY_MAX_ANGLE` - the game
has its own version of the thing this mod builds, and it may be fighting you.

This is the only question that still justifies the original 2–4 block budget,
and only in this one case.

### R7. Where is the shot direction built? — only if R4 hit screen centre

Search the firearm controller for the shot origin and direction. Note whether it
derives from a muzzle transform under `WeaponRootAnim` or from the camera. Then
decide: apply the offset to the ray, or re-parent.

### R8. Is there a native lowered-weapon state? — Q6

Search for a weapon-ready or lowered state in the base game. Sprinting lowers the
weapon natively, so something exists; the question is whether a deliberate
gun-down toggle is exposed or whether it is Realism's own addition.

The scaffold defines its own toggle and does not need this. It matters only if
hooking a native state would make the animation transitions cleaner.

### R9. Stance suspensions — IMPLEMENTED, verify in a raid

No longer stubs. All four resolve on this build (F10) and are wired:

| Suspension | Source |
|---|---|
| sprinting | `MovementContext.IsSprintEnabled`, falling back to `Player.IsSprintEnabled` |
| inventory open | `Player.IsInventoryOpened` |
| mounted / ladder | `MovementContext.CurrentState.Name == "Stationary"` |
| reload | `FirearmController.IsInReloadOperation` |
| heal, throwable, melee | hands controller is not a firearm controller — by type, no member name |

What to check in a raid: sprint, reload, throw a grenade, use a medkit, mount a
stationary weapon. The HUD's `gate` row should drop to 0 and name the reason
each time. Each has its own F12 toggle if one misbehaves.

### R10. Is a transparent shader already loaded? — Q7, both-eyes-open Tier 2

UnityExplorer, not dnSpy. Only relevant once the aiming model ships.

---

## Recording the answers

Put the results in `04-DECISIONS.md` as they come in. R1, R3 and R4 in
particular are worth writing down with the build version next to them — they are
the three that will need re-checking after every Tarkov client update, and they
are cheap to re-run once you know what you are looking at.
