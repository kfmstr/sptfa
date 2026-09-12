# SPT Free Aim 0.1.0

The mouse points the gun. The body catches up a moment later.

Tarkov normally welds the weapon to the camera: the gun is wherever you look,
instantly, and your character's stance is decorative. This decouples the two. A
mouse movement swings the **gun** immediately; your torso and camera follow it
over about a second. Turn slowly and nothing changes. Flick hard and the muzzle
leads the turn, then settles. It is the aiming model from *Bodycam*, on SPT.

Built and tested against **SPT 4.1.5**, BepInEx 5.4.23.

---

## Install

1. Close the game.
2. Copy `BepInEx/plugins/SPTFreeAim.dll` into your own
   `<SPT folder>\BepInEx\plugins\`.
3. Start the game. Press **F9** in a raid for the readout, **F12** for settings.

That is the whole install. No server mod, no bundles, nothing to register.

### Optional: start from a tuned preset

`preset/kfmstr.sptfreeaim.cfg` is a working configuration rather than the
defaults. Copy it into `<SPT folder>\BepInEx\config\` **before the first
launch** and you get something already playable instead of a wall of zeroes.

BepInEx only writes a setting the first time it sees it, so dropping this in
after the mod has run once will not overwrite anything you have already changed.
Delete your existing `kfmstr.sptfreeaim.cfg` first if you want the preset to win.

---

## Hotkeys

| Key | What it does |
|---|---|
| F8 | Master toggle, on and off mid-raid |
| F9 | The readout |
| F10 | Yaw write probe, a diagnostic |
| Z | Low ready, if the stance gate is on |

**Do not put a hotkey on a number key.** Tarkov uses `Alpha1` and friends for
weapon slots, so a mod hotkey there fires every time you draw a weapon. The mod
reads Tarkov's own `Control.ini` at startup, ignores any of its hotkeys that
clash, and says so on the readout and in the log.

---

## The settings that matter

Everything is off at zero. There are no master switches to forget: a dial set to
nothing **is** the off position.

### 1. Main, 2. Coupling

`Cone` is how far the gun may drift from centre before the body starts to
follow. `Cap` is the hard limit it can never exceed. `Spring k` is how fast the
body catches up; 5 gives roughly one second to converge, which is the number the
whole design is built around.

`Aim coupling` shrinks the whole effect while shouldered, because a shouldered
weapon has much less free play than one held at the hip.

### 3. Pivot

`Hinge mode = Around Grip` is the one that matters. The weapon rotates about
your right hand, the way a real one does, rather than spinning about its own
origin. `Take the pivot from the weapon itself` reads Tarkov's own rotation
centre per weapon, so a pistol and a rifle both hinge in the right place.

### 4. Stance

Low ready: the weapon drops when you are not aiming, and the cone opens up. Off
by default.

### 5. Recoil

`Decouple recoil` sends recoil into the gun rather than the camera, so the sight
picture climbs without your head being pushed. `Hip camera follow` decides how
much of it still reaches the view.

### 6. Body

`Body lean` tips the horizon against the swing, the counterbalance a shooter
makes to keep the weight over their feet. Start at 4 degrees and go **down** if
anything. `Gun blur` focuses past the weapon so the gun softens while the world
stays sharp.

### 7. Optic glass

Scope effects only, inside the tube, not on the screen. These need a **magnified
optic**. A holo or red dot has no scope image to affect (it is a mesh with a
material, not a second camera), and the readout's `sight` row says which you
have fitted.

A workable starting point:

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

Bloom threshold decides *what* glows and bloom decides *how hard*. If turning
bloom up just hazes the whole tube, raise the threshold instead. Lens dirt rides
on bloom and does nothing while bloom is zero.

---

## The readout

F9. The rows worth knowing:

* `offset` is how far the gun currently leads the body, with a bar against the
  cap. Slow turn, it stays near zero. Flick, it spikes and decays.
* `convergence` reports what your `k` actually gives, in seconds, rather than
  leaving you to guess.
* `sight` says whether a magnified optic is fitted, and therefore whether the
  glass settings can do anything at all.
* `scope buffer` says whether the scope renders in HDR. Bloom needs values
  brighter than white; in an LDR scope there are none.
* `hotkeys` and `master toggle` say whether any of your keys collide with
  Tarkov's own bindings.

Every one of those rows exists because something silently did nothing once and
cost a raid to find. If a setting appears dead, read the row before changing it.

---

## Known limits

* Scope glass effects need a magnified optic. Red dots and holos cannot take
  them, by construction.
* `Keep your field of view with optics too` strips scope magnification. It is a
  real option, but it is not what you want while tuning a scope.
* Tested on one machine, one SPT version. Expect to tune, not to install.

## Compatibility

Runs alongside Amands Graphics, SAIN, Realism and the usual set. The mod drives
the scope's own post-processing stack rather than the main camera's, and puts
the main camera's volume mask back exactly as it found it when you switch the
glass effects off.

If something looks wrong, F8 twice: it releases every transform and effect it
touches and hands them back to the game.

## Credits

See `CREDITS.md`. The pivot maths derives from lualeet's SPT deadzone mod
(public domain, the Unlicense); the drive loop does not.
