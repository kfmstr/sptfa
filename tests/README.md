# Spec harness

Runs `Core/FreeAimState.cs` and `Core/AngleMath.cs` against the measured
behaviours in `docs/01-SPEC.md`, outside the game. No Unity, no Tarkov, no raid.

This is what caught the push-factor problem in `docs/07-FINDINGS.md` F3.

`realmath.cs` supplies real implementations of the handful of `Vector2` and
`Mathf` members the drive loop uses, so the loop under test is the shipping one,
unmodified.

## Run it

These files are excluded from the mod build (`<Compile Remove="tests\**" />` in
the csproj), so compile them separately:

```
csc /out:spec.exe tests\realmath.cs Core\AngleMath.cs Core\FreeAimState.cs tests\SpecTests.cs
spec.exe

csc /out:stance.exe tests\realmath.cs tests\stance-stubs.cs Core\Stance.cs Core\StanceState.cs tests\StanceTests.cs
stance.exe
```

Two harnesses. `SpecTests` asserts against the measurements in
`docs/01-SPEC.md`; `StanceTests` asserts against the stance machine's own rules,
which are a design decision rather than a measurement.

`stance-stubs.cs` exists because StanceState has two halves - one that reads the
game and one that decides. Only the second is worth testing, so the reading half
is stubbed inert and the tests set the suspension flags directly.

`csc.exe` ships with Visual Studio - the Developer Command Prompt has it on the
path. Anything that compiles C# will do; the harness has no dependencies.

## What it checks

| Check | Spec line |
|---|---|
| Slow turn builds no visible offset | section 2, "Slow mouse movement" |
| Moderate steady turn parks at the cone | section 2, "At the cone edge" |
| Hard flick heads for the cap, softly | section 2, "Fast flick" |
| Converges to zero in ~1s after stopping | section 2, "Convergence time" |
| Offset does not linger | D4, the difference from lualeet |
| Hard cap holds under absurd input | section 2, "Hard cap" |
| Wrapping across the 180 seam | not in the spec - a correctness check |
| Frame-rate independence | not in the spec - a correctness check |
| Stance gate suppresses the offset | section 4 |
| Aim coupling scales the offset | Q2 |

## Re-run it when

You change the drive loop, or you change a default in `FreeAimConfig`. The
defaults are the numbers under test - `Tune()` in `SpecTests.cs` mirrors them, so
keep the two in step or the harness stops meaning anything.
