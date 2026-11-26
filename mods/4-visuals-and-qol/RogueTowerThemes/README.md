# RogueTowerThemes (v1.1.0)

Clean-room customizable Light & Dark mode switcher with toon-safe ambient & shadow fill preservation, non-destructive scene caching, separate Dark/Light presets, F7 quick toggle, and Mod Menu live switching. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose.

> **100% AI-Generated Code & Docs Notice**
> Every line of code and documentation in this mod was **100% written by AI** (**Google Gemini** and **Anthropic Claude** models), directed and designed by **Rose**.

## Features
- **Instant 1-Key Dark <-> Light Switching (`F7`, configurable):** Flip effortlessly between **Dark Mode**, **Light Mode**, and **Vanilla** anywhere (menus or mid-wave), or click the `Theme` button in the `RogueTowerModMenu` header.
- **Toon-Safe Lighting (`OverrideAmbient = false` default & 100% Fill Light Intensity):** Preserves Rogue Tower's warm vanilla shadow fill and step-toon lighting thresholds so central Towers stay detailed in Dark Mode and roads never bleach white in Light Mode (optional `OverrideAmbient` toggle available).
- **Customizable Dark & Light Presets + Canvas Exclusions:** Independently tune `SkyHex`, `SunMultiplier`, `SunHex`, `FillHex`, `UiBrightness`, and `HighContrastText` in `BepInEx/config/Rosie.RogueTowerThemes.cfg` or `RogueTowerModMenu` -> `Settings`, while automatically skipping all custom mod canvases.

## Keybinds & Controls
- `F7` (configurable): Toggle `Dark` <-> `Light` mode live (`F7` + config cycling supports `Vanilla / Dark / Light`).

## What We Changed / Improved Over Upstream Inspiration
- Fixed shadow black-crush on the central Tower/houses in Dark Mode and road white-clipping in Light Mode by preserving vanilla ambient and secondary fill-light intensity (`v1.1.0` auto-migrates stale `v1.0.0` configs cleanly).
- Excludes all `RogueTower*` / `CustomMapsEditorCanvas` / `CardRenderer` UI canvases so custom menus keep their exact high-contrast palette across theme switches.

## Source Code
- Visual C# project: [`src/Themes/`](../../../src/Themes/)
- Compiled plugin: `plugins/RogueTowerThemes.dll`

## Thunderstore Dependencies
- `bbepis-BepInEx_Rogue_Tower-5.4.1900`

## Installation (BepInEx 5)
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy `plugins/RogueTowerThemes.dll` into `Rogue Tower/BepInEx/plugins/RogueTowerThemes/`.

## Credits & Inspiration
- Clean-room theme engine designed by **Rose**, inspired by `Jdewi-DarkMode` and expanded with non-destructive state caching, toon-shader lighting preservation, configurable Light & Dark presets, and 1-key `F7` + Mod Menu switching.