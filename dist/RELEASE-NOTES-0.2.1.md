The mouse points the gun; the body catches up about a second later. Tested on SPT 4.1.5.

**Install:** drop `BepInEx/plugins/SPTFreeAim.dll` into your `BepInEx\plugins\`. That is all of it.

**F9** for the readout, **F12** for settings, **F8** to toggle the mod.

### New in 0.2.1

- **Aiming from low ready raises the weapon first.** Hold aim while the gun is down and it comes up and keeps going into the shoulder, instead of fighting the low-ready pose. A press that raises the weapon does not also count as a tap that drops it again.
- **Scope picture when not aiming.** Optional. Set `Optic: scope picture when not aiming (px)` above 0 to keep the world visible in the tube at the hip instead of a black disc. Holds both of Tarkov's blackout switches (camera off and lens fade), not just one. 256 is cheap; shouldering always restores your graphics setting.
- **HUD** reports live scope resolution and whether the lens glass is clear or painted out.

### Known limits

- **Grip / hinge / gun control** still needs a rework. The Around Grip path and pivot dials are not reliable yet; expect to tune, and expect a follow-up release that replaces that whole path.
- Scope glass needs a magnified optic. A red dot or holo has no scope camera to drive.
- Tested on one machine and one SPT version.

### From 0.2.0

Low ready on a right-mouse tap, hotkeys that move off Tarkov binds, isolated scope glass effects, HDR scope buffer for bloom.
