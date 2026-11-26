# RogueTowerCheats (v1.0.0)

Optional Cheats & Sandbox mod for Rogue Tower (F6 or plugged directly into RogueTowerModMenu): Infinite Gold/Mana/Range/Banishes, God Mode, No Tower Price Scaling, Tower Damage/RPM multipliers, Tower & Loadout spawners, Card/Card-Tree grants, and Level-40 Endgame Stress Scenes. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose.

> **100% AI-Generated Code & Docs Notice**
> Every line of code and documentation in this mod was **100% written by AI** (**Google Gemini** and **Anthropic Claude** models), directed and designed by **Rose**.

## Features
- **Cheats & Multipliers (`F6` or `Cheats` tab inside `RogueTowerModMenu`):** Infinite Gold, Infinite Mana, Infinite Tower Range (`250` global coverage + `Dropper` waypoint optimization), God Mode (0 damage taken), **No Tower Price Scaling** (flat base gold price), Infinite Card Banishes, Include Unresearched Cards toggle, Tower Damage Multiplier (`1x`..`100x`), Tower Fire Rate Multiplier (`1x`..`5x`), plus instant resource grants, `Draw 3 Cards`, `Kill All Enemies`, and `Upgrade All Placed Towers (+10)`.
- **Towers & Saved Sandbox Loadouts:** Unlock or lock all **15 Row 1 Towers** (including Ballista) & **5 Row 2 Buildings** individually on the hotbar, save & apply **5 Custom Loadout Slots**, optionally **Auto-Apply a Loadout at Run Start**, and **Lock Card Pool to Hotbar**.
- **Cards & Card Trees Tabs:** Searchable catalog of every card in the game (including `CardDataLib` / unresearched cards), one-click `Give` single card, `Give Branch` sub-branch, or `Give Tree` / `Give ALL Tower/DOT/Non-Monster Trees` (synced with `UpgradesMenu`).
- **Endgame Sandbox Stress Scenes (Round 40 / 45):** Launch 5 reproducible Round 40/45 Level-40 stress scenes (up to **300 Level-40 Towers -- 20 of each tower + Max Card Trees + InfRange @ 8x Speed**) paired with `RogueTowerModMenu`'s `DeepLagProfiler`. Works both plugged into `RogueTowerModMenu` and standalone (`F6`) with zero hard dependency.

## Keybinds & Controls
- `F6`: Jump directly to the `Cheats` tab in `RogueTowerModMenu` (or toggle the Standalone Cheat Window if `RogueTowerModMenu` is not installed).

## What We Changed / Improved Over Upstream Inspiration
- Decoupled cleanly from `RogueTowerModMenu` with **zero hard DLL dependency**: install both together for a unified tabbed UI, install only `RogueTowerModMenu` for a pure cheat-free Mod Manager, or install `RogueTowerCheats` standalone.

## Source Code
- Visual C# project: [`src/Cheats/`](../../../src/Cheats/)
- Compiled plugin: `plugins/RogueTowerCheats.dll`

## Thunderstore Dependencies
- `bbepis-BepInEx_Rogue_Tower-5.4.1900`

## Installation (BepInEx 5)
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy `plugins/RogueTowerCheats.dll` into `Rogue Tower/BepInEx/plugins/RogueTowerCheats/`.

## Credits & Inspiration
- Clean-room sandbox & cheat suite designed by **Rose**, inspired by the Rogue Tower community (`app/1843760`) and `SashaFloats` (`TowerFlyweight` research on UnknownCheats that inspired No Tower Price Scaling).