# Reference source

Third-party source, included so the IDE agent can read it without network access.
**Not our code. Do not compile it into the project.**

| File | Origin | Licence | Use it for |
|---|---|---|---|
| `lualeet-DeadzonePatch.cs` | lualeet/sptarkov-deadzone | MIT | The pivot-rotation apply step (`ApplyDeadzone`). **Not** the drive loop. |
| `lualeet-PluginSettings.cs` | lualeet/sptarkov-deadzone | MIT | Config surface shape, per-weapon settings scaffolding |
| `lualeet-Plugin.cs` | lualeet/sptarkov-deadzone | MIT | BepInEx plugin entry point shape |

## Critical reading note

`lualeet-DeadzonePatch.cs` implements the **reactive** design:

```
camera moves (game does this) -> gun is rotated backwards to trail it -> offset persists
```

We want the **opposite**:

```
mouse moves the gun 1:1 -> spring pulls the camera to meet the gun -> offset converges to zero
```

So read `ApplyDeadzone` and `SetRotationClamped` closely — those are reusable. Read
`UpdateDeadzoneRotation` as an example of what **not** to do for the drive loop.

See `../docs/04-DECISIONS.md` D4 for why.

## Attribution requirement

lualeet's mod is MIT. Credit it in the source and in the published mod description,
and state that this continues that work. Realism credits it in a code comment, which
is the community norm.

---

## Note on the .txt extensions

These files were renamed from `.cs` to `.cs.txt` when they were brought into the
repo. The project uses SDK-style globbing, which compiles every `.cs` file under
the project folder, and these reference sources will not compile - they name
Realism types and namespaces that do not exist here.

They are for reading. Strip the `.txt` if you want syntax highlighting in an
editor outside the solution.

---

## What is here, and what is not

**lualeet's deadzone mod (MIT)** — kept. MIT allows redistribution with the
licence notice; see the TODO in `../CREDITS.md` about adding that notice.

**SPT-Realism-Mod-Client — REMOVED.** It is licensed CC BY-NC-ND 4.0, and
NoDerivatives means excerpts of it cannot be redistributed here. The source was
removed from this repo; `REALISM-NOTES.md` records what was learned from reading
it, in our own words and with no code. Full context in `../CREDITS.md`.
