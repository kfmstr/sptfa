# SPT Free Aim

A client mod for SPT that decouples where the gun points from where the camera
looks, reproducing the aiming model of *Bodycam*.

**The mechanic:** the mouse points the gun instantly, and the body turns to
follow the gun a moment later. Active only when the weapon is raised.

Continues the work of [lualeet/sptarkov-deadzone](https://github.com/lualeet/sptarkov-deadzone)
(Unlicense), whose pivot-rotation maths is reused for the apply step. The drive loop
runs the causation the other way round, on purpose — see `docs/04-DECISIONS.md`
D4.

---

## Status

Scaffold. The drive loop is written and tested against the spec; nothing has run
in the game yet.

| | |
|---|---|
| Core coupling loop | written, spec-tested |
| Weapon apply (pivot) | written, untested in game |
| Camera apply (Compensate mode) | written, untested in game |
| Stance gate + lowered pose | written; automatic transitions stubbed |
| Recoil decoupling | config only, not implemented |
| Shot direction (Q5) | **untested — the top remaining risk** |

---

## First session

1. Open `SPTFreeAim.sln`, build with F6. The DLL copies itself into
   `BepInEx\plugins`.
2. Launch. Read the BepInEx console for the `GameRefs OK` line.
3. Load an offline Factory raid.
4. Work through `docs/08-RECON.md` session 1. It is four checks, all keypresses.

The one that matters most is **R4 — do shots follow the barrel**. Everything else
is recoverable; that one can add weeks. Do it in the first session.

---

## Keys

| Key | Does |
|---|---|
| F8 | Master on/off, for instant A/B comparison |
| F9 | Debug HUD |
| F10 | Yaw write probe — answers the feasibility gate (`docs/07-FINDINGS.md` F5) |
| F12 | BepInEx config menu. Everything user-facing lives here |
| X | Raise / lower the weapon |

---

## Drive modes

Set in F12. The drive loop is identical in all of them; only how the offset
reaches the screen differs.

**Intercept** *(default)* — the real mechanic. The mouse drives a gun bearing
nothing else can see; the lagging body bearing is what gets written into the
game. The body genuinely trails the gun, which is what `docs/04-DECISIONS.md` D4
describes. Writes `MovementContext.Rotation` (not `Yaw`/`.Pitch` — those are
read-only computed properties over it). Press F10 to confirm the write holds.

**Compensate** — no interception. The game's bearing stays 1:1 with the mouse
and only the rendered camera is rotated back. The gun-to-view angle on screen
comes out right, but the **body still leads**, which is backwards from D4. It
reads as "I turn and the gun follows me". A fallback for when Intercept writes
do not hold, not the target feel. See `docs/07-FINDINGS.md` F11.

**Reactive** — the documented fallback (`docs/04-DECISIONS.md` D10). Camera
leads, gun trails. Feels gun-heavy rather than body-heavy. Unlike lualeet's
original, this one converges back to zero, because a non-converging offset was
measured as wrong.

**Disabled** — resolve and log only.

---

## Tuning

Four numbers do almost everything. `docs/01-SPEC.md` section 2 has the
measurements they are trying to match.

| Setting | Default | Notes |
|---|---:|---|
| Cone size | 10° | **A guess.** `docs/05-OPEN-QUESTIONS.md` Q1 — measure it in Bodycam. This one decides whether the mod feels subtle or disorienting |
| Hard cap | 90° | Measured |
| Spring k | 5 /s | ~99% convergence in 1s, which is the measured figure. The HUD shows what your current k actually gives |
| Push factor | 0.5 | **Not 1.0.** At 1.0 the soft cone becomes a hard clamp and flicks saturate at the cone. See `docs/07-FINDINGS.md` F3 |

Rough relationship, useful when the real cone number arrives:

```
offset a flick reaches  ~=  mouse speed * (1 - push) / k
```

### Measuring the cone with the HUD

Q1 asks for a number from Bodycam, but the HUD makes the reverse check easy: turn
at a steady moderate pace and read the `offset` row. It parks at roughly the cone
size. Set the cone to what looks right in Tarkov, then check it against Bodycam
rather than the other way round.

---

## Layout

```
Plugin.cs                 entry point, keys, HUD hookup
FreeAimConfig.cs          every F12 setting, with the reasoning in the descriptions
Core/
  AngleMath.cs            wrap, clamp, frame-rate-independent spring
  FreeAimState.cs         the drive loop. Pure maths, no game types
  StanceState.cs          gun up / gun down, and the automatic suspensions
Patches/
  FreeAimPatches.cs       the two Harmony patches and the apply step
Compat/
  GameRefs.cs             EVERY reflective lookup into the game. See below
Debug/
  DebugHud.cs             on-screen readout
  YawWriteProbe.cs        the F10 feasibility probe
docs/                     spec, plan, decisions, findings, recon
reference/                lualeet and Realism source, for reference only
```

### When a Tarkov update breaks this

It will. Every client update re-obfuscates class names.

**The break is in `Compat/GameRefs.cs` and nowhere else.** That is the whole
point of the file. It resolves every lookup once at startup, logs which one
failed by name, and degrades to inert rather than throwing every frame. Fix the
name there; nothing else needs touching.

Obfuscated names currently relied on: `method_19` (camera recoil, optional).
Everything else is a stable member name, but verify rather than assume.

---

## Credits and licence

- `lualeet/sptarkov-deadzone` (Unlicense) — the pivot rotation maths in the apply step,
  and the wrap/clamp helpers.
- `space-commits/SPT-Realism-Mod-Client` — read for technique on stance poses and
  camera recoil replacement. **No runtime dependency**, deliberately: Realism also
  rewrites ballistics, medical and recoil, and depending on it would force all of
  that on users.

MIT, matching the community norm.
