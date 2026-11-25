# RogueTowerModMenu (v2.5.0)

Cheat & sandbox Mod Menu (`), saved tower loadouts & unlock grants, card/tree spawners, live companion module controls, Round 40/45 Level-40 FPS benchmark scenes, per-frame stall profiler, and loopback test harness. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose.

> **100% AI-Generated Code & Docs Notice**
> Every line of code and documentation in this mod was **100% written by AI** (**Google Gemini** and **Anthropic Claude** models), directed and designed by **Rose**.

## Features
- **Cheats Tab:** Infinite Gold, Infinite Mana, Infinite Tower Range (`250` global coverage + `Dropper` waypoint optimization), God Mode (0 damage taken), **No Tower Price Scaling** (flat base gold price for every copy built), Infinite Card Banishes, Include Unresearched Cards toggle, Tower Damage Multiplier (`1x`..`100x`), Tower Fire Rate Multiplier (`1x`..`5x`), plus instant resource grants (`+1k/+10k/+100k Gold`, `Fill/+100/+10/s Mana`, `Repair/+10/+50 HP`, `+1k/+25k/+100k Meta XP`, `Draw 3 Cards`, `Kill All Enemies`, `Upgrade All Placed Towers +10`).
- **Towers & Loadouts Tab (Cheats/Sandbox):** Unlock or lock all **15 Row 1 Towers** (including Ballista) & **5 Row 2 Buildings** individually on the hotbar, save & apply **5 Custom Loadout Slots**, optionally **Auto-Apply a Loadout at Run Start**, and optionally **Lock Card Pool to Hotbar** (filter random card draws to unlocked hotbar towers only). *(Hotbar slot ordering itself lives in `RogueTowerQoL` as a non-cheat QoL feature, and `ModMenu` syncs cleanly with it when both are installed.)*
- **Cards & Card Trees Tabs:** Searchable catalog of every card in the game (including `CardDataLib` / unresearched cards), one-click `Give` single card, `Give Branch` sub-branch, or `Give Tree` / `Give ALL Tower/DOT/Non-Monster Trees`, synced with `UpgradesMenu` if installed.
- **Modules & Benchmark Dashboard:** Toggle and configure installed mods live (`RogueTowerQoL`, `RogueTowerThemes`, `RogueTowerLagFix`, `RogueTowerQuickTree`, `RogueTowerCustomMaps`, `SpeedControls`, `BanishCards`, `DisableProgression`), cycle Dark/Light themes from the header, and launch **5 Round 40/45 Level-40 FPS Benchmark Scenes** (up to **300 Level-40 Towers -- 20 of each tower + Max Card Trees + Infinite Range @ 8x Speed**) backed by **DeepLagProfiler** (13 Harmony `Stopwatch` buckets, entity/particle census, GC counts, and Worst-5 frame hitch culprit attribution) plus a focusless `127.0.0.1:18437` CLI test harness.

## Keybinds & Controls
- `` ` `` (BackQuote): Toggle Mod Menu open/closed (`Esc` closes when open; top-right `Mods (`)` pill also toggles).
- Header buttons: `<90` / `90>` / `Top` (via `RogueTowerQoL`), `Theme` (`Vanilla / Dark / Light` via `RogueTowerThemes`), `Pause / Resume` (`SpeedControls`).

## What We Changed / Improved Over Upstream Inspiration
- Separated all **non-cheat QoL features** (Hotbar Sorting, Tower Hold / `+10` Fast Upgrades, `90 deg` Camera Rotation / Top-Down / X-Ray Placement, Open Chests / Chop Trees HUD, Early Wave Expand) cleanly into **`RogueTowerQoL` v2.0.0** so players who want pure vanilla balance with great UX do not need cheats installed.
- Added per-frame worst-spike profiler (`DeepLagProfiler`), Level-40 (`+13/+13/+13`) uniform stress presets, and zero-CLI-arg Steam-popup-free headless test tooling (`127.0.0.1:18437`).

## Source Code
- Visual C# project: [`src/CustomLevels/`](../../../src/CustomLevels/)
- Compiled plugin: `plugins/RogueTowerCustomLevels.dll`

## Thunderstore Dependencies
- `bbepis-BepInEx_Rogue_Tower-5.4.1900`

## Installation (BepInEx 5)
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy `plugins/RogueTowerCustomLevels.dll` into `Rogue Tower/BepInEx/plugins/RogueTowerModMenu/`.

## Credits & Inspiration
- Clean-room suite designed by **Rose**, inspired by the Rogue Tower Steam Discussions & Thunderstore communities (`app/1843760`) and `SashaFloats` (`TowerFlyweight` research on UnknownCheats that inspired No Tower Price Scaling).