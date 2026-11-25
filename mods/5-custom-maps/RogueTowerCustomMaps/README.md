# RogueTowerCustomMaps (v1.3.0)

Clean-room custom map generator featuring Play Menu preset cycling, a built-in In-Game Custom Map Sequence Builder UI (click +S/+L/+R/+SLR/+Portal chips without editing configs!), non-destructive TileManager pool restoration, and Mod Menu sync. Inspired by acr-Rogue_Tower_Custom_Maps; 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose.

> **100% AI-Generated Code & Docs Notice**
> Every line of code and documentation in this mod was **100% written by AI** (**Google Gemini** and **Anthropic Claude** models), directed and designed by **Rose**.

## Features
- **Built-In In-Game Custom Map Sequence Builder (`Edit` Button on `PlayMenu` & Mod Menu):** Click tile chips directly in-game (`+ Straight (S)`, `+ Left (L)`, `+ Right (R)`, `+ Split LR`, `+ Split SL`, `+ Split SR`, `+ 3-Way (SLR)`, `+ Portal (P)`), `Undo Last`, `Clear All`, `Copy Selected Preset`, preview the live path (`S -> L -> SLR -> P -> ...`), and click `Save & Select Custom (CSV)` without ever touching a `.cfg` file.
- **12 Instant Map Presets:** `Vanilla (Random)`, `Spiral Outwards (CCW)`, `Spiral Outwards (CW)`, `Barely Legal`, `The Clock`, `5 Straights + Portal`, `Instant Portal`, `3-Way Instant Portal`, `1 Turn 1 Portal`, `1 Turn 3 Portals`, `T-Split Portals`, and `Custom (CSV)`.
- **Per-Run Safety (`RememberMapAcrossRuns = false` by default):** Automatically resets back to `Vanilla (Random)` after starting a custom run so you never accidentally lock future normal runs into a meme/test layout unless you enable persistence.

## Keybinds & Controls
- Click `Map: ...` on `PlayMenu` (Single/Double/Triple/Quadruple screen) to cycle presets; click **`Edit`** right beside it (or in `RogueTowerModMenu` -> `Modules`) to open the In-Game Custom Map Sequence Builder (`Esc` closes).

## What We Changed / Improved Over Upstream Inspiration
- Replaced file-only CSV editing with a visual **In-Game Custom Map Sequence Builder** UI right on `PlayMenu`.
- Added non-destructive **TileManager prefab array snapshot & Postfix restoration** after every `SpawnNewTile`, ensuring Vanilla fallback or mid-run tile generation never breaks after custom spawns.
- Added boundary-safe **CW & CCW Spiral** free-neighbor detection (reading `TileManager.intArray`) that closes cleanly with Portal dead-ends near `lastLevel`, plus live `RogueTowerModMenu` preset synchronization.

## Source Code
- Visual C# project: [`src/CustomMaps/`](../../../src/CustomMaps/)
- Compiled plugin: `plugins/RogueTowerCustomMaps.dll`

## Thunderstore Dependencies
- `bbepis-BepInEx_Rogue_Tower-5.4.1900`

## Installation (BepInEx 5)
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy `plugins/RogueTowerCustomMaps.dll` into `Rogue Tower/BepInEx/plugins/RogueTowerCustomMaps/`.

## Credits & Inspiration
- Inspired by **acr** (`acr-gaming/rogue-tower-custom-maps` on GitHub / `acr-Rogue_Tower_Custom_Maps` on Thunderstore); written clean-room from scratch.