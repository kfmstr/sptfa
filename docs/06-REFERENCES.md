# References

---

## Reference implementations (source included in `reference/`)

### lualeet/sptarkov-deadzone
https://github.com/lualeet/sptarkov-deadzone

The original SPT free aim mod. Insurgency-style deadzone. MIT licensed. Built for
SPT 3.5.8 / 3.6.1 and not updated since.

**Use it for:** the pivot-rotation maths in the apply step
(`ApplyDeadzone` / `LocalRotateAround` around a pivot set back from the muzzle).

**Do not use it for:** the drive loop. Its causation is backwards from what we
want (see `04-DECISIONS.md` D4).

Files included: `reference/lualeet-DeadzonePatch.cs`, `reference/lualeet-PluginSettings.cs`

Key hook points it uses:
- Prefix on `Player.VisualPass` — to grab your own player
- Postfix on `ProceduralWeaponAnimation.AvoidObstacles` — to run after Tarkov has finished positioning the weapon

Its config surface: `Enabled`, `Position` (pivot distance, default 0.1), `Sensitivity`
(default 0.25), `MaxAngle` (default 5.0), `AimMultiplier` (default 0.0 — **change this**).
It has scaffolding for per-weapon settings (`WeaponSettingsGroup` keyed by string)
but always falls back to one shared config.

### space-commits/SPT-Realism-Mod-Client
https://github.com/space-commits/SPT-Realism-Mod-Client

**Read for technique. Never take a runtime dependency.**

- `Player/StanceController.cs` — the stance system (`EStance` enum), and, from the
  comment crediting lualeet onward, the same deadzone maths already ported to
  current SPT for Realism's weapon mounting. This is the modern reference for
  which member names survived.
- `Weapons/AimingPatches.cs` — patches on `Player.FirearmController.ToggleAim` and
  `MovementContext.SetAimingSlowdown`. Shows how to intercept the aim key and change
  the movement speed penalty.
- `Weapons/ProceduralAnimPatches.cs` — patches on `ProceduralWeaponAnimation`. Map of
  which methods control weapon position, sway and the aim transition.

Files included: `reference/realism-StanceController-excerpt.cs`, `reference/realism-AimingPatches.cs`

### space-commits/SPT-FOV-Fix
https://github.com/space-commits/SPT-FOV-Fix

Shows how the weapon/arms camera relates to the world camera, and how the ADS
transition is smoothed. Relevant if you touch FOV or camera FOV.

---

## Tooling

| Tool | URL |
|---|---|
| SPT | https://sp-tarkov.com/ |
| SPT mod hub (The Forge) | https://sp-mod.com/ |
| Jehree's client mod template | https://github.com/Jehree/SPTClientModExamples |
| dnSpy | https://github.com/dnSpyEx/dnSpy/releases/latest |
| UnityExplorer | https://github.com/sinai-dev/UnityExplorer/releases/latest |
| BepInEx docs | https://docs.bepinex.dev/ |
| Harmony 2 docs | https://harmony.pardeike.net/articles/intro.html |
| Visual Studio | https://visualstudio.microsoft.com/ |
| .NET downloads | https://dotnet.microsoft.com/en-us/download/dotnet |

---

## SPT documentation

| Page | URL |
|---|---|
| Client Modding Quick Guide | https://github.com/sp-tarkov/wiki/blob/main/modding/tutorials/Client_Modding_Quick_Guide.md |
| Mod Types | https://github.com/sp-tarkov/wiki/blob/main/Mod_Types.md |
| Modding Resources | https://github.com/sp-tarkov/wiki/blob/main/modding/Modding_Resources.md |
| Installation Guide | https://github.com/sp-tarkov/wiki/blob/main/Installation_Guide.md |
| Server mod examples (C#) | https://github.com/sp-tarkov/server-mod-examples |
| SPT technical docs | https://deepwiki.com/sp-tarkov/server-csharp/1-overview |

> Note: the `sp-tarkov/wiki` GitHub repo was archived on 2026-08-11 and is read-only.
> Content still readable, but check sp-tarkov.com for anything newer.

---

## Background on the mechanic

| Source | Relevance |
|---|---|
| https://forums.unrealengine.com/t/how-to-make-a-deadzone-aim-free-aim/1428285 | Unreal tutorial for deadzone aim, referencing Unrecord |
| r/UnrealEngine5 thread "what is that movement in bodycam where the gun moves around the screen" | Community identification of the mechanic as deadzone aiming |
| Steam Workshop "Free Aim & Bodycam System" | Describes decoupled aiming within a deadzone, Insurgency comparison |

---

## Existing SPT mods in adjacent space (as of Sept 2026)

- **Canted Aiming** 2.0.0 by bushtail, SPT 4.1.5 — point shoot without a backup sight mount
- **Quick Aim** 1.0.0 by BLACK HAWK, SPT 4.1.5 — hold aim longer when crouched/prone/mounted
- **ScopeRangefinder** 3.3.0 by maschine, SPT 4.1.5 — in-scope rangefinder and trajectory preview

Nothing offers standalone free aim. Nothing offers both-eyes-open optics.

---

## Environment facts recorded at handoff

- Current SPT version: **4.1.5**
- Client mods target **.NET Framework 4.7.2** (`net472`)
- BepInEx **5** (Mono builds of tools)
- Server mods are C# as of SPT 4.0 (were TypeScript before)
