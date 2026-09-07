# Conversation log

Reconstructed narrative of the session that produced this handoff, September 2026.
Not a verbatim transcript — a record of what was asked, what was found, and what
changed as a result.

---

## 1. Opening question

> "can you check how to make SPT tarkov mod?"

Researched the official SPT wiki. Established the two mod types:

- **Server mods** — `[game]\SPT\user\mods`, C# since SPT 4.0, handle profiles,
  traders, quests, items, flea, insurance, skill gain, bot spawning. Show up in
  the server console and launcher.
- **Client mods** — `\BepInEx\plugins`, C#, patch the game directly via BepInEx +
  Harmony. Can change anything given effort. Configured from the in-game F12 menu.
- **Combination mods** — both.

Also pulled the official Client Modding Quick Guide and the server mod examples
repo (`sp-tarkov/server-mod-examples`, .NET 9 SDK, dependency injection based).

---

## 2. Scope emerges

> "I want to build a mod that changes the way how you aim"

Aiming is in-raid mechanics, so this is a **client mod**. Server mods cannot touch it.

Asked what "changes aiming" meant. Owner initially answered "all of them" to a list
covering chest camera, no sight alignment, no ADS zoom, decoupled motion, and the
video look.

Owner also shared a Google search for "bodycam like aiming", which established
Bodycam as the reference.

Answers given at this point:
- SPT version: not installed yet
- C#: comfortable
- Owns Bodycam: yes, mirror its feel
- Wanted: a build plan document

First plan produced. Estimated 40–60 hours.

---

## 3. Scope correction

> "one correction, I don't need an actual body camera, just gun aiming decoupled
> from my eye view"

Dropped the chest camera, lens work, video overlay, and removal of sight alignment.
Kept only the decoupling.

Research found `lualeet/sptarkov-deadzone`, an existing SPT free aim mod, ~150 lines,
MIT, built for SPT 3.5.8/3.6.1 and abandoned. Nothing comparable on The Forge.

Plan rewritten as a port. Estimate dropped to 15–25 hours.

Owner chose, at this point:
- Free aim / deadzone as the mechanic
- Deadzone **reduced but not removed** while aiming down sights

*(That second answer was later contradicted by measurement — see
`05-OPEN-QUESTIONS.md` Q2.)*

---

## 4. Stance gating added

> "we can also change the stance, if gun is down on sling then, I look around
> normally. When I at the stance with ready to shoot, then it is bodycam aiming"

Adopted. Researched Realism's `EStance` enum and `DoPatrolStance` implementation
for reference values. Decided against a runtime dependency on Realism.

---

## 5. The lag challenge

> "why lag, does the body cam have the lag?"

A fair challenge that exposed a real error. Claude had introduced weapon lag as a
feature to copy. Correct answer: Bodycam has no gun lag.

Also surfaced a subtlety worth keeping: in lualeet's reactive design the damping
**is** the mechanism, not decoration. Zero damping means zero deadzone.

Introduced the reactive vs true-deadzone distinction.

---

## 6. Verifying the Bodycam claim

> "Can you check deadzone is it what bodycam does?"

First attempt failed — browser extension disconnected and web search rejected
requests. Claude gave a provisional answer that leaned **against** deadzone, based
on three weak signals.

> "try again"

Second attempt succeeded and **overturned** the provisional answer. Deadzone
aiming is confirmed to be Bodycam's mechanic. Evidence listed in
`04-DECISIONS.md` D3.

---

## 7. The measurement session — the pivotal input

Owner tested Bodycam directly and reported detailed observations covering the
aim/view coupling, the soft cone, the ~90° overshoot, the ~1s convergence,
identical hip and shouldered behaviour, optic behaviour, and recoil in both
stances, plus the three-phase shouldering animation.

This **overturned the architecture**. The causation runs gun-leads-camera-chases,
opposite to lualeet's mod. See `04-DECISIONS.md` D4.

Plan rewritten a fourth time. Estimate rose to 30–50 hours. Recoil decoupling added
as a new step. Shouldering animation moved out of scope.

---

## 8. Setup

> "Okay, sounds good, lets start with setup"

Verified the official SPT installation guide. Key findings: retail game must be
current and already launched once; the installer never updates, only installs fresh;
never install to Documents/Desktop or the live game folder; profile edition cannot
be changed after registering.

Flagged that each SPT install is a full game copy, so retail plus two installs is
~3× the game size on disk.

Produced the phased setup checklist with a verification gate per phase.

---

## 9. Both-eyes-open optics

> "Also can we do aiming like we do in real life when the gun gets on the sholder
> and we aim with two eyes"

Plus a reference photo of a red dot viewed with both eyes open: reticle sharp on
target, world visible *through* the ghosted housing, no black tunnel.

Research confirmed nothing in SPT does this and nothing on The Forge comes close,
despite community requests since at least 2019.

First answer gave two tiers. On re-reading the photo the answer was revised to three
tiers, with the middle tier (ghost the housing via a material/shader swap) assessed
as more accessible than first stated. See `01-SPEC.md` section 6.

---

## 10. Handoff

> "Please extract all the logs of this chat and materials we produced. I will move
> to IDE enviornment and will use the agent there"

This package.

---

## Corrections Claude made during the session

Recorded because they are the parts most likely to be wrong again if revisited
carelessly.

| # | Wrong initial position | What corrected it |
|---|---|---|
| 1 | Treated weapon lag as a Bodycam feature to copy | Owner's direct challenge |
| 2 | Guessed Bodycam does *not* use deadzone aiming | Web research on second attempt |
| 3 | Planned around camera-leads-gun-trails | Owner's hands-on measurement |
| 4 | Assessed housing-ghosting as expensive shader work | Closer reading of the reference photo |

The through-line: **three of four came from the owner testing or challenging
directly, not from research.** Where a claim about Bodycam's behaviour matters,
measure it in the game rather than reasoning about it.
