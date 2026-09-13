The mouse points the gun; the body catches up about a second later. Tested on SPT 4.1.5.

**Install:** drop `BepInEx/plugins/SPTFreeAim.dll` into your `BepInEx\plugins\`. That is all of it. Optionally copy `preset/kfmstr.sptfreeaim.cfg` into `BepInEx\config\` before the first launch for a tuned starting point instead of a wall of zeroes.

**F9** for the readout, **F12** for settings, **F8** to toggle the mod.

### New in 0.2.0

- **Low ready on a right-mouse tap.** Tap for ready, hold to aim. Release inside 0.2 s counts as a tap. Nothing is intercepted, so the game still aims normally. Set the window to 0 to turn it off.
- **Hotkeys move themselves** off keys Tarkov already uses, rather than being silently ignored. `Z` is `DropBackpack` and `Alpha1` is your sidearm; both had quietly broken things.
- **Scope glass works.** The scope's post-processing stack is isolated from the main camera so the effects stay inside the tube, and the scope render target switches to HDR on demand, because bloom needs values brighter than white and Tarkov renders the scope in LDR by default.
- **Colour fringing fixed.** The engine scales that parameter by 0.05 internally, so the dial was doing a twentieth of what it claimed.
- **A frame-rate regression removed.** A diagnostic was scanning the whole scene every frame behind a config toggle.
- **Dead code swept**, measured against the compiled assembly rather than by grepping names. The DLL is smaller than 0.1.0 despite doing more.

### Known limits

Scope glass needs a magnified optic. A red dot or holo is a mesh with a material rather than a second camera, so there is no scope image to affect, and the readout's `sight` row says which you have fitted. `Keep your field of view with optics too` strips scope magnification.

Tested on one machine and one SPT version. Expect to tune, not to install.
