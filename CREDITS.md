# Credits and third-party material

## lualeet/sptarkov-deadzone — MIT

The pivot-rotation approach in the apply step, and the angle wrap/clamp helpers
in `Core/AngleMath.cs`, derive from lualeet's SPT deadzone mod. This project
continues that work.

MIT permits reuse with attribution and the licence notice. Excerpts of that
source are kept in `reference/` for exactly that reason.

> **TODO:** add lualeet's `LICENSE` text to `reference/` alongside the excerpts.
> MIT requires the notice to travel with the code, and it is currently missing.

The drive loop is deliberately *not* lualeet's — see `docs/04-DECISIONS.md` D4.
Their offset accumulates camera-minus-gun and never returns to zero; ours is
gun-minus-body and converges. Only the pivot maths is shared.

## space-commits/SPT-Realism-Mod-Client — CC BY-NC-ND 4.0, read only

Realism was read to understand technique for stances and camera recoil. **No
Realism code is included in this project, and none will be.**

Its licence is Creative Commons **Attribution-NonCommercial-NoDerivatives 4.0**.
NoDerivatives means adapted versions may not be distributed — so copying its
code into this mod would be a licence violation, and would also make the planned
MIT release impossible, since MIT cannot be applied to ND-licensed material.

What we take is what a licence cannot restrict: knowing that a stance system
wants a state machine rather than a boolean, and that a pose is a target reached
over time. Written up in our own words in `reference/REALISM-NOTES.md`. The
implementation here is independent, and where Realism's approach was the better
one we say so in the notes rather than importing it.

Facts about the *game* found while reading — that `HandsContainer` is a field,
that the camera-recoil step reads `CameraToWeaponAngleStep` — are facts about
Battlestate's assembly, not Realism's expression, and were re-verified directly
against `Assembly-CSharp.dll` (`docs/07-FINDINGS.md` F10).

### History note

Verbatim Realism source was committed to `reference/` early in this project and
pushed publicly before the licence was checked. It has been removed from the
working tree, but **it remains in git history**, which is still a
redistribution. Removing it properly needs a history rewrite (`git filter-repo`)
and a force-push. See `docs/07-FINDINGS.md` F15.

## Battlestate Games

Escape from Tarkov and its assemblies are Battlestate's. This mod patches the
client at runtime and redistributes none of it.
