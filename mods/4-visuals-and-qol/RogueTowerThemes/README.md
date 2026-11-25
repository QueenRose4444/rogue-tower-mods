# RogueTowerThemes (v1.0.0)

Clean-room customizable Light & Dark mode switcher with non-destructive original scene caching, per-theme sky/sun/ambient/UI brightness presets, F7 quick toggle, and live Mod Menu integration. Created by Rose with Gemini & Claude.

> **AI Collaboration Notice**
> Created by **Rose** with AI assistance using **Google Gemini** and **Anthropic Claude** models.

## Features
- Instant 1-key **Dark <-> Light** toggle (`F7`, configurable) or header `Theme` button in `RogueTowerModMenu` (`Vanilla / Dark / Light`).
- Separate customizable **Dark Preset** and **Light Preset** configs (`SkyHex`, `AmbientHex`, `SunMultiplier`, `SunHex`, `FillHex`, `UiBrightness`, `HighContrastText`).
- Non-destructive scene snapshots guarantee switching modes repeatedly never compounds brightness or corrupts UI colors.

## Source Code
- Visual C# project: [`src/Themes/`](../../../src/Themes/)
- Compiled plugin: `plugins/RogueTowerThemes.dll`

## Thunderstore Dependencies
- `bbepis-BepInEx_Rogue_Tower-5.4.1900`

## Installation (BepInEx 5)
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy `plugins/RogueTowerThemes.dll` into `Rogue Tower/BepInEx/plugins/RogueTowerThemes/`.

## Credits & Inspiration
- Original clean-room theme engine by **Rose** (inspired by `Jdewi-DarkMode`, expanded with non-destructive state caching, configurable Light & Dark presets, and 1-key `F7` + Mod Menu live switching).