# Environment setup

Runs on Windows, with a licensed and up-to-date retail Escape from Tarkov install.

> **Disk space warning.** The SPT installer copies your whole retail game and
> patches the copy down, so each SPT install is a full game. Two installs plus
> your retail copy is roughly three times the game size. If space is tight, do
> the dev install only and add the play install later.

---

## Phase A — retail game prerequisites

SPT patches down from your own client files, so the retail copy must be current
and already initialised.

- [ ] Update retail Escape from Tarkov to the latest version via the official launcher or Steam
- [ ] Launch retail Tarkov, reach the main menu or stash, quit
      *(generates files SPT needs; skipping this is a common cause of install failure)*
- [ ] Confirm free disk space for two more full game copies

---

## Phase B — SPT installs

Two installs: one you play, one you break. The installer always installs a fresh
latest version and never updates an existing install, so you run it twice to
different paths.

- [ ] Download the SPT installer from **https://sp-tushonka.com/**
      *(sp-tarkov.com is dead - the project was renamed after a trademark notice in
      August 2026. See `07-FINDINGS.md` F1. SPT 4.1 was the last release from the
      original team; 4.0 and 4.1 are both live branches.)*
- [ ] Run the installer, read the Installer Info page, install the **play** copy to `C:\Games\SPT-Play`
      *(never Documents or Desktop — Windows permission errors. Never into your live game folder.)*
- [ ] Run the installer again, install the **dev** copy to `C:\Games\SPT-Dev`
- [ ] In the dev copy, run `SPT.Server`, wait for green "Server has started, happy playing"
      *(server must stay running the whole time you play)*
- [ ] Run `SPT.Launcher`, register a profile with a username that is **not** your retail account name
      *(pick any game edition; you do not need to own it; you cannot change it afterwards)*
- [ ] **GATE:** Start Game, reach the main menu, quit
      *(this deobfuscates the game assembly; every later phase depends on it)*

---

## Phase C — development tooling

Client mods are C# targeting .NET Framework 4.7.2, loaded by BepInEx.

- [ ] Install Visual Studio 2022 Community with the **"Game development with Unity"** workload
- [ ] Install the .NET runtime, 6.0 or newer
- [ ] Install dnSpy into `C:\dnSpy` — https://github.com/dnSpyEx/dnSpy/releases/latest
      *(take the win64 zip from Assets)*
- [ ] Install UnityExplorer into the dev copy — https://github.com/sinai-dev/UnityExplorer/releases/latest
      *(the **Mono** build for **BepInEx 5**; installs like any other client mod)*
- [ ] In `C:\Games\SPT-Dev\BepInEx\config\BepInEx.cfg`, under `[Logging.Console]`:
      set `LogChannels = all` and `Enabled = true`
- [ ] **GATE:** Launch the dev copy, confirm the BepInEx console window opens and UnityExplorer loads
      *(no console means you will be debugging blind for the whole project)*

---

## Phase D — decompile the game

- [ ] Create a folder named after the version, e.g. `SPT415_assembly`
- [ ] dnSpy → File → Open → `C:\Games\SPT-Dev\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll`
- [ ] File → Export to Project → that folder
- [ ] **GATE:** Open `Assembly-CSharp.sln`, confirm <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd> for `ProceduralWeaponAnimation` returns results
      *(nothing found means the export failed or the assembly was still obfuscated — Phase B gate did not actually complete)*

---

## Phase E — mod project

Putting the project inside the dev copy makes the build drop the DLL straight into
`BepInEx\plugins`, so testing is build-then-launch.

- [ ] Create `C:\Games\SPT-Dev\Development\`
      *(the repo goes in a subfolder of this, e.g. `Development\sptfa\`, not
      directly in `Development\` - the template's DLL reference paths assume that
      depth. The shipped `SPTFreeAim.csproj` detects the SPT root either way, but
      the upstream template does not.)*
- [ ] On https://github.com/Jehree/SPTClientModExamples click **Use this template → Create a new repository**
      *(your own repo, not a clone of the example repo)*
- [ ] Clone your new repo into `C:\Games\SPT-Dev\Development\`
- [ ] Rename the project folder, the `.csproj` and the `.sln` to your mod name
- [ ] Text-editor find-and-replace `SPTClientModExamples` inside the `.sln` and `.csproj`, then open the solution
      and do Ctrl+Shift+F → Replace in Files → scope Entire solution for the rest
- [ ] Build with F6, confirm the DLL appears in `C:\Games\SPT-Dev\BepInEx\plugins`
- [ ] **GATE:** Launch, load an offline Factory raid, confirm your own log line appears in the BepInEx console
      *(this is the full loop working: edit, build, launch, observe)*

---

## Phase F — first reconnaissance

Not setup, but do it while the decompiled solution is fresh. Feeds the feasibility
gate in `02-PLAN.md` step 03.

- [ ] Search the decompiled solution for `MovementContext`, find where `Yaw` and `Pitch` are assigned
      *(this is where mouse look becomes body bearing — the whole design depends on intercepting it)*
- [ ] Search for a lowered-weapon or weapon-ready state in the base game
      *(decides whether the stance toggle hooks something native or you build your own)*
- [ ] Record both findings before writing any code

---

## Known failure modes

| Symptom | Cause |
|---|---|
| "Could not find a downgrade patcher for the version of the game you have installed" | Retail copy not current. Redo Phase A. |
| Server crashes instantly or will not open | Missing runtime, or installed to a permission-protected folder |
| Phase D search finds nothing | Phase B gate did not complete — the assembly is still obfuscated |
| Old mods or profiles fail | They never carry across SPT versions. Do not reuse them. |

Support: SPT Discord `#spt-support` for install problems, `#mods-development` for code.
