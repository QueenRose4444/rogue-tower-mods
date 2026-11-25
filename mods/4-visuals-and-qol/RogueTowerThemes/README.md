# RogueTowerThemes (v1.0.0)

Clean-room customizable Light & Dark mode switcher with non-destructive original scene caching, separate Sky/Ambient/Sun/Fill/UI Brightness/High-Contrast presets, F7 quick toggle, and live Mod Menu integration. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose.

> **100% AI-Generated Code & Docs Notice**
> Every line of code and documentation in this mod was **100% written by AI** (**Google Gemini** and **Anthropic Claude** models), directed and designed by **Rose**.

## Features
- **Instant 1-Key Dark <-> Light Switching (`F7`, configurable):** Flip effortlessly between **Dark Mode**, **Light Mode**, and **Vanilla** anywhere (menus or mid-wave), or click the `Theme` button in the `RogueTowerModMenu` header.
- **Customizable Dark & Light Presets:** Independently tune `SkyHex`, `AmbientHex`, `SunMultiplier` (scales all directional lights proportionally), `SunHex`, `FillHex`, `UiBrightness`, and `HighContrastText` in `BepInEx/config/Rosie.RogueTowerThemes.cfg` or live in the `Modules` tab.
- **Non-Destructive Scene Snapshot Cache:** Captures every `Camera.backgroundColor`, `RenderSettings`, `Light`, HUD `Image`, and `Text` baseline before applying any theme -- switching Dark <-> Light <-> Vanilla repeatedly never compounds colors or corrupts UI alpha.

## Keybinds & Controls
- `F7` (configurable): Toggle `Dark` <-> `Light` mode live (`F7` + config cycling supports `Vanilla / Dark / Light`).

## What We Changed / Improved Over Upstream Inspiration
- Unlike `Jdewi-DarkMode` (which destructively multiplies sprite colors in-place and only supports dimming), `RogueTowerThemes` snapshots original Unity colors cleanly so **both** crisp Dark Mode and crisp bright Light Mode can be toggled endlessly with zero drift, and verified proportionally against real `GameScene` directional light intensities.

## Source Code
- Visual C# project: [`src/Themes/`](../../../src/Themes/)
- Compiled plugin: `plugins/RogueTowerThemes.dll`

## Thunderstore Dependencies
- `bbepis-BepInEx_Rogue_Tower-5.4.1900`

## Installation (BepInEx 5)
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy `plugins/RogueTowerThemes.dll` into `Rogue Tower/BepInEx/plugins/RogueTowerThemes/`.

## Credits & Inspiration
- Clean-room theme engine designed by **Rose**, inspired by `Jdewi-DarkMode` and expanded with non-destructive state caching, configurable Light & Dark presets, and 1-key `F7` + Mod Menu live switching.