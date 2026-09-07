# Project: SPT Free Aim (Bodycam-style aiming)

Read this first. Full detail is in `docs/`.

## What we are building

A **client mod** for Single Player Tarkov (SPT) 4.1.5 that decouples where the
gun points from where the camera looks, reproducing the aiming model used by the
game *Bodycam*.

The mechanic in one sentence: **the mouse points the gun instantly, and the body
turns to follow the gun a moment later.**

Active only when the weapon is raised and ready. Gun down or slung means normal
Tarkov camera behaviour.

## The single most important fact

Bodycam runs the causation **opposite** to the existing SPT deadzone mod.

- Existing SPT mod (`lualeet/sptarkov-deadzone`): mouse turns the camera, the gun
  is dragged behind it, the offset never returns to zero.
- Bodycam (what we want): mouse points the gun 1:1 with zero lag, a spring pulls
  the camera around to meet the gun, the offset always converges back to zero in
  about one second.

This was established by hands-on measurement in Bodycam, not inference. It means
the existing mod is **not** a usable base for the drive loop. Only its
pivot-rotation maths is reused, for the final apply step.

## Tech stack

- C#, .NET Framework 4.7.2 (`net472`)
- BepInEx 5 plugin
- HarmonyX patching, via SPT's `ModulePatch` base class
- Visual Studio 2022 Community, "Game development with Unity" workload
- dnSpy for decompiling `Assembly-CSharp.dll`
- UnityExplorer for live in-game object inspection

## Current state

Scaffold written, never run in the game. The drive loop is implemented and
tested against the spec outside the game (`tests/`). Nothing has been verified
in a raid.

The immediate next job is `docs/08-RECON.md` session 1 — four checks, all
keypresses in an offline Factory raid. The one that matters is **R4: do shots
follow the barrel** (Q5). It is the only open item that can add weeks.

The old feasibility gate (intercepting the look input) is no longer on the
critical path: Compensate mode reaches the same coupling without interception,
and the F10 probe answers whether Intercept is available in about a minute. See
`docs/07-FINDINGS.md` F4 and F5.

## Hard rules for this project

- **Do not take a runtime dependency on the Realism mod.** It also rewrites
  ballistics, medical and recoil. Read its source for technique, copy the
  approach, credit it. Never `require` it.
- **Keep every reflection lookup in one file.** Every Tarkov client update
  re-obfuscates class names, so an update must mean fixing one place.
- **Credit `lualeet/sptarkov-deadzone` (MIT)** for the pivot maths, and say in
  the mod description that this continues that work.
- **Verify, do not assume, that shots follow the barrel.** Rotating the weapon
  model does not automatically move the bullet. This is an explicit test step.
- **Everything user-facing goes in the BepInEx F12 config menu.**
- **Push factor stays below 1.0.** At 1.0 the soft cone becomes a hard clamp.
  See `docs/07-FINDINGS.md` F3.
- **Drive the body bearing through `MovementContext.Rotation`, never `Yaw`/`Pitch`.**
  Those two are read-only computed properties over it. F10.1.
- **`HandsContainer` and its transforms are FIELDS, not properties.** A
  property-only lookup returns null and the mod silently does nothing. F10.2.

## Document map

| File | Contents |
|---|---|
| `docs/00-CONVERSATION-LOG.md` | How we got here, chronologically |
| `docs/01-SPEC.md` | The measured Bodycam specification |
| `docs/02-PLAN.md` | Build steps, effort, test checklist |
| `docs/03-SETUP.md` | Environment setup, with gates |
| `docs/04-DECISIONS.md` | Decisions made, and things tried and rejected |
| `docs/05-OPEN-QUESTIONS.md` | Unresolved, needs the user |
| `docs/06-REFERENCES.md` | Links and reference code |
| `docs/07-FINDINGS.md` | Research, and a pressure test of the plan against its own spec |
| `docs/08-RECON.md` | What to check, in what order, and what each answer changes |
| `reference/*.cs` | Actual source of the reference implementations |
