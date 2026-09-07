# Open questions

Unresolved at handoff. Status updated after `07-FINDINGS.md`.

| | Status |
|---|---|
| Q1 cone size | **open, needs you.** Placeholder 10 deg is live in the config |
| Q2 coupling while aiming | **open, needs you.** Default 1.0 (matches Bodycam) |
| Q3 toggle style | **open, minor.** Default press-to-toggle, switchable in F12 |
| Q4 can look input be intercepted | **yes, via Rotation not Yaw/Pitch** - confirmed in the assembly, F10.1. Press F10 in a raid to confirm the write holds |
| Q5 do shots follow the barrel | **open, and now the top risk.** See `08-RECON.md` R4 |
| Q6 native lowered-weapon state | open, not blocking - the mod defines its own |
| Q7 transparent shader | open, not v1 |
| Q8 did BSG's rework ship | **CLOSED - no.** See F2 |

---

## Q1. Nominal cone size — NEEDS OWNER MEASUREMENT

Every other spec value has a number. This one drives the whole feel and is still
unmeasured.

**Test to run in Bodycam:** face a wall with a clear vertical feature. Move the
mouse at a steady moderate pace. Note roughly how many degrees the gun leads the
body before the body starts turning at the same rate.

Rough is fine. Even "about a hand's width at arm's length" is usable.

**Placeholder if unanswered:** 8 to 12 degrees. But a guess here is the difference
between subtle and disorienting, so get the real number before tuning.

---

## Q2. Full cone while shouldered, or reduced? — CONTRADICTION TO RESOLVE

The owner was asked early whether the deadzone should still apply while aiming down
sights, and chose **"reduced but not removed."**

The later hands-on measurement of Bodycam says the coupling is **identical** hip and
shouldered — the cone is not reduced at all.

These conflict. Both are defensible:

- **Match Bodycam:** drop the aim multiplier entirely, full cone always
- **Diverge deliberately:** keep a reduction, defensible in Tarkov given engagement
  ranges, especially with magnified optics

It should be a choice, not an accident. Note that `lualeet/sptarkov-deadzone`
defaults `AimMultiplier` to `0f`, meaning *no deadzone at all* while aiming — that
default is wrong for either answer and must be changed.

---

## Q3. Stance toggle: press or hold? — MINOR, OWNER PREFERENCE

- **Press to toggle:** gun stays where you put it. Easier over long looting stretches. Realism uses this.
- **Hold to ready:** gun drops the moment you release. Makes the ready position feel deliberate.

Recommended default: press to toggle.

---

## Q4. Can Tarkov's look input be intercepted? — NO LONGER A GATE

The whole gun-leads design depends on taking the mouse delta before it reaches
`MovementContext.Yaw` / `.Pitch`.

**Update.** Two things changed this.

1. `07-FINDINGS.md` F4 describes Compensate mode, which produces the same
   coupling without intercepting anything. The gate is gone; this is now a
   preference between two working modes.
2. `07-FINDINGS.md` F5: the question is answerable by writing in a raid and
   seeing what happens. Press F10. No dnSpy needed unless the answer is
   "stuck then lost".
3. `07-FINDINGS.md` F10.1, which settles the mechanism: `Yaw` and `Pitch` are
   read-only computed properties, so the plan's literal instruction ("confirm
   those are writable") fails. `Rotation` is the writable member, `Rotation.x`
   is yaw and `.y` is pitch, and its setter runs the game's own clamping and
   bookkeeping. Intercept mode is viable, and cleaner than planned.

---

## Q5. Do shots follow the barrel? — HIGH RISK, needs in-game test

Rotating `WeaponRootAnim` moves the weapon model. Whether the bullet direction
follows is undocumented in both reference mods.

If it does not, the shot ray needs its own patch: +2 to 3 blocks.

Test procedure is in `02-PLAN.md` step 05.

---

## Q6. Does base SPT 4.1.5 have a native lowered-weapon state? — needs dnSpy

Sprinting lowers the weapon natively, but a deliberate gun-down toggle appears to
be a Realism addition. Assume you build your own until you have looked.

---

## Q7. Is a transparent-capable shader already loaded in the game? — needs UnityExplorer

Determines whether Tier 2 of the both-eyes-open optic needs a shipped asset bundle
or can swap to something already present. Check before assuming you must ship your
own.

---

## Q8. Did Battlestate's aiming rework ship? — CLOSED, NO

Previewed March 2026, went to the ETS test servers on 30 April 2026 bundled with
a Unity engine transition, and is **still not on live** - the 3 August 2026 patch
(1.1.0.0, Seasons) shipped without it. SPT 4.1 is pinned to a pre-rework client
and the original SPT team has dissolved, so the path into an SPT release is
slower than it was.

Not a reason to delay. Re-check before porting to a future SPT version, not
before starting work. Full timeline in `07-FINDINGS.md` F2.
