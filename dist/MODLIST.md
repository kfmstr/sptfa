# The rest of the setup

This package contains **only SPT Free Aim**. Everything below is someone else's
work and is not redistributed here: with one exception none of these carry a
licence that permits it, and SPT mods are expected to come from the author's own
page so that people get updates, support and the correct version.

This is the inventory of the install SPT Free Aim was built and tested against,
so it can be reproduced from the original sources. Read it as "what I am
running", not as a dependency list. **SPT Free Aim needs none of these.**

Base: **SPT 4.1.5**, BepInEx 5.4.23.5, Unity 2022.3.43.

Get everything from the SPT hub (`hub.sp-tarkov.com`) unless the author says
otherwise. Match the SPT version, not the version numbers here.

---

## Client plugins (`BepInEx/plugins`)

| Mod | Version |
|---|---|
| Amands's Graphics | 1.8.0 |
| Borkel's Realistic NVGs | 3.0.2 |
| BlackDiv | 1.3.1 |
| BotCallsigns | see server mods |
| DrakiaXYZ BigBrain | 1.5.0 |
| DrakiaXYZ SearchOpenContainers | 1.5.0 |
| DrakiaXYZ Waypoints | 1.9.0 |
| DrakiaXYZ Sense (AmandsSense) | 3.1.0 |
| HandsAreNotBusy | 1.7.1 |
| IncreaseLookDirection | 1.3.0 |
| MoreBotsAPI | 2.1.1 |
| NVG Sight Dimmer | 2.1.0 |
| RUAFComeHome | 1.2.1 |
| SAIN | 4.5.1 |
| SevenBoldPencil BrighterInteriors | 1.1.1 |
| SmajlecLights | 1.3.0 |
| TacticalToaster UNTARGH | 3.2.1 |
| Tarkin Ladders | 1.0.4 |
| UnityExplorer | 4.9.0 |
| WTT Armory | 2.0.5 |
| WTT ClientCommonLib | 3.0.6 |
| WTT ContentBackportClient | 2.0.1 |
| WTT HeadVoiceSelector Core | 1.0.8 |
| acidphantasm BrightLasers | 3.0.0 |
| BepInEx Configuration Manager | 18.4 |
| **SPT Free Aim** | **0.1.0** (this package) |

Plus a local build of **Tarkov Interior Lights 0.4.0**, which is not a public
release.

SPT's own plugins (`SPT.Core`, `SPT.Custom`, `SPT.Debugging`,
`SPT.Singleplayer`, all 4.1.5) ship with SPT itself. Do not copy those.

## Preloader patchers (`BepInEx/patchers`)

FixPluginTypesSerialization, BlackDiv, MoreBotsPrepatch, RUAFComeHome,
UNTARGHPrepatch, WTT-ContentBackportPatcher. These arrive with their parent
mods; there is nothing to install separately.

## Server mods (`user/mods`)

acidphantasm-brightlasers, acidphantasm-progressivebotsystem, BlackDivServer,
BotCallsigns, bushtail-CantedAiming, MoreBotsServer, RUAFComeHomeServer,
Solarint-SAIN-ServerMod, TwitchPlayers, untargh-server, WTT-Armory,
WTT-HeadVoiceSelector, WTT-ServerCommonLib.

Most of these are the server half of a client plugin above and are installed
together with it. Install the pair, never one half.

---

## Install order that works

1. Clean SPT 4.1.5.
2. Server mods into `user/mods`, client plugins into `BepInEx/plugins`. Where a
   mod ships both halves, keep them together.
3. Common libraries before the mods that need them: WTT-ServerCommonLib and
   WTT-ClientCommonLib before any other WTT mod; MoreBotsAPI before MoreBots
   content; BigBrain and Waypoints before SAIN.
4. Start the server once and let it finish before starting the client.
5. SPT Free Aim last. It depends on nothing and can go in or out at any time.

## A note on the graphics mods

Amands Graphics also drives post-processing, and it is the reason one early
version of SPT Free Aim appeared to do nothing: Amands sets Tarkov's own bloom
to zero and renders its own. SPT Free Aim now drives the scope camera's separate
stack instead, so the two no longer argue. If scope effects look wrong, check
Amands's settings before this mod's.
