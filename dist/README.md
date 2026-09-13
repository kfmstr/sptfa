# SPT Free Aim 0.2.0

The mouse points the gun. The body catches up a moment later.

Tarkov welds the weapon to the camera: the gun is wherever you look, instantly,
and your character's stance is decorative. This decouples the two. A mouse
movement swings the **gun** immediately; your torso and camera follow over about
a second. Turn slowly and nothing changes. Flick hard and the muzzle leads the
turn, then settles. It is the aiming model from *Bodycam*, on SPT.

Built and tested against **SPT 4.1.5**, BepInEx 5.4.23.

---

## Install

1. Close the game.
2. Copy `BepInEx/plugins/SPTFreeAim.dll` into your `<SPT folder>\BepInEx\plugins\`.
3. Start the game. **F9** in a raid for the readout, **F12** for settings.

That is the whole install. No server mod, no bundles, nothing to register.

### Optional: start from a tuned preset

`preset/kfmstr.sptfreeaim.cfg` is a working configuration rather than the
defaults. Copy it into `<SPT folder>\BepInEx\config\` **before the first launch**
and you get something already playable instead of a wall of zeroes.

BepInEx only writes a setting the first time it sees it, so dropping this in
after the mod has run once will not overwrite what you have already changed.
Delete your existing `kfmstr.sptfreeaim.cfg` first if you want the preset to win.

---

## Controls

| Input | What it does |
|---|---|
| **Tap right mouse** | Weapon drops to low ready, or comes back up |
| **Hold right mouse** | Aims, exactly as it always did |
| **M** | The same low ready toggle, on a key |
| **F8** | Master toggle, on and off mid-raid |
| **F9** | The readout |

The right-mouse tap is the one worth trying. A release inside 0.2 s counts as a
tap; anything longer is an aim and is ignored. Nothing is intercepted, so the
game still sees every press and a tap flicks the sights up for an instant. Tune
the window under `Ready on a right-mouse tap (s)`, or set it to 0 to turn the
gesture off. The readout's `aim tap` row shows your last press duration, so you
can tune against the real number.

### Hotkeys look after themselves now

Tarkov has 46 keys bound and leaves almost nothing free: **J** and **M** of the
letters, the F-keys, and most punctuation. If one of this mod's hotkeys lands on
a key Tarkov already uses, the mod **moves it** to a free key at startup, saves
it, and says so in the log and on the readout's `keys moved` row.

This is not hypothetical. `Z` is `DropBackpack` and `Alpha1` is your sidearm, and
both have cost a raid.

---

## The settings that matter

Everything is off at zero. There are no master switches to forget: a dial set to
nothing **is** the off position.

**1. Main, 2. Coupling.** `Cone` is how far the gun may drift before the body
starts to follow. `Cap` is the hard limit. `Spring k` is how fast the body
catches up; 5 gives roughly one second, which is the number the whole design is
built around. `Aim coupling` shrinks the effect while shouldered.

**3. Pivot.** `Hinge mode = Around Grip` is the one that matters: the weapon
rotates about your right hand rather than its own origin. `Take the pivot from
the weapon itself` reads Tarkov's own rotation centre per weapon.

**4. Stance.** Low ready, and the right-mouse tap above.

**5. Recoil.** `Decouple recoil` sends recoil into the gun rather than the
camera, so the sight picture climbs without your head being pushed.

**6. Body.** `Body lean` tips the horizon against the swing. Start at 4 degrees
and go **down** if anything. `Gun blur` focuses past the weapon.

**7. Optic glass.** Scope effects inside the tube, not on the screen. These need
a **magnified optic**: a holo or red dot has no scope image to affect, and the
readout's `sight` row tells you which you have fitted. A starting point:

```
Optic: allow bright light in the scope  true
Optic: bloom                            10
Optic: bloom threshold                  1.5
Optic: bloom spread                     10
Optic: lens dirt                        1.0
Optic: colour fringing                  0.3
Optic: rim darkening                    0.4
Optic: edge distortion                  -0.1
```

Threshold decides *what* glows, bloom decides *how hard*. If turning bloom up
hazes the whole tube, raise the threshold instead. Lens dirt rides on bloom and
does nothing while bloom is zero.

---

## The readout

F9. The rows worth knowing:

* `offset` is how far the gun leads the body, with a bar against the cap.
* `convergence` reports what your `k` actually gives, in seconds.
* `sight` says whether a magnified optic is fitted, and therefore whether the
  glass settings can do anything at all.
* `scope buffer` says whether the scope renders in HDR. Bloom needs values
  brighter than white; in an LDR scope there are none.
* `keys moved` and `stance key` say whether any hotkey collided with Tarkov's.
* `aim tap` shows your last right-mouse press duration.

Every one of those exists because something silently did nothing once and cost a
raid to find. If a setting looks dead, read the row before changing it.

---

## What changed since 0.1.0

* **Low ready on a right-mouse tap.** Tap for ready, hold to aim.
* **Hotkeys move themselves** off keys Tarkov already uses, instead of being
  silently ignored. `Z` was `DropBackpack` and killed the low ready button.
* **Scope glass actually works.** The scope's post stack is isolated from the
  main camera, so the effects stay inside the tube. The scope render target is
  switched to HDR on demand, because bloom needs values brighter than white and
  Tarkov renders the scope in LDR by default.
* **Colour fringing fixed.** The engine scales that parameter by 0.05 internally,
  so the dial was doing a twentieth of what it said.
* **A frame-rate regression removed.** A diagnostic was scanning the whole scene
  every frame behind a config toggle. Deleted.
* **Dead code swept**, measured against the compiled assembly rather than by
  grepping: the DLL is smaller than 0.1.0 despite doing more.

## Known limits

* Scope glass needs a magnified optic. Red dots and holos cannot take it, by
  construction: a collimator is a mesh with a material, not a second camera.
* `Keep your field of view with optics too` strips scope magnification. Not what
  you want while tuning a scope.
* Tested on one machine, one SPT version. Expect to tune, not to install.

## Compatibility

Runs alongside Amands Graphics, SAIN, Realism and the usual set. It drives the
scope camera's own post-processing rather than the main camera's, and puts the
main camera's volume mask back exactly as it found it.

If something looks wrong, F8 twice: it releases every transform and effect it
touches and hands them back to the game.

## Credits

See `CREDITS.md`. The pivot maths derives from lualeet's SPT deadzone mod (public
domain, the Unlicense); the drive loop does not.
