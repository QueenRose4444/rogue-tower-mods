# RogueTowerModMenu (v3.0.0)

Clean-room, cheat-free BepInEx Mod Manager (`), live Settings dashboard for any installed plugin, non-cheat DeepLagProfiler live FPS/hitch analyzer, and loopback test harness. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose.

> **100% AI-Generated Code & Docs Notice**
> Every line of code and documentation in this mod was **100% written by AI** (**Google Gemini** and **Anthropic Claude** models), directed and designed by **Rose**.

## Features
- **Mods Tab (Installed Plugin Manager):** Lists every loaded BepInEx plugin (`Name vVersion`, `GUID`, `Suite` vs `Community` origin badge, config key count) with one-click **`Configure ->`** buttons to jump straight to that mod's settings.
- **Settings Tab (Live Config Hub + Auto-Detected Configs):** Dedicated live controls for `RogueTowerQoL`, `RogueTowerThemes`, `RogueTowerLagFix`, `RogueTowerQuickTree`, `RogueTowerCustomMaps`, `SpeedControls`, `BanishCards`, and `DisableProgression`, plus automatic live toggles for any other loaded BepInEx plugin with a config file.
- **Profiler Tab (Non-Cheat Live Diagnostics):** Sample live FPS, 1% Low, frame times, active entities/particles, 13 Harmony `Stopwatch` subsystem buckets, and Worst-5 frame hitches in any normal un-cheated run.
- **Header Quick Controls & Plugin Extensibility API:** Instant `<90` / `90>` / `Top` camera controls, `Theme` Light/Dark/Vanilla switcher, `Pause/Resume`, and reflection extension hooks used seamlessly by optional companion mods like **`RogueTowerCheats`**.

## Keybinds & Controls
- `` ` `` (BackQuote): Toggle Mod Menu open/closed (`Esc` closes when open; top-right `Mods (`)` pill also toggles).
- Header buttons: `<90` / `90>` / `Top` (via `RogueTowerQoL`), `Theme` (`Vanilla / Dark / Light` via `RogueTowerThemes`), `Pause / Resume` (`SpeedControls`).

## What We Changed / Improved Over Upstream Inspiration
- Separated into a **100% cheat-free Mod Manager & Live Settings Hub** (`RogueTowerModMenu v3.0.0`), moving all cheats, card/tower spawners, loadouts, and sandbox stress scenes into the dedicated optional **`RogueTowerCheats`** mod.
- Built with crisp `pixelPerfect` `1280x720` OS TrueType UI rendering (`Segoe UI` + `Segoe UI Semibold`) and non-blocking text focus handling.

## Source Code
- Visual C# project: [`src/ModMenu/`](../../../src/ModMenu/)
- Compiled plugin: `plugins/RogueTowerModMenu.dll`

## Thunderstore Dependencies
- `bbepis-BepInEx_Rogue_Tower-5.4.1900`

## Installation (BepInEx 5)
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy `plugins/RogueTowerModMenu.dll` into `Rogue Tower/BepInEx/plugins/RogueTowerModMenu/`.

## Credits & Inspiration
- Clean-room mod manager designed by **Rose**, inspired by the Rogue Tower Steam Discussions & Thunderstore communities (`app/1843760`).