# RogueTowerQoL (v2.1.0)

Complete non-cheat MIT QoL suite for Rogue Tower: direct hotbar drag-and-drop sorting + saveable layout presets, tower hold & +10 snap upgrades, 90-deg camera rotation/top-down/X-ray occluded tile placement, Open Chests & Chop Trees HUD, early wave expand, range preview rings, MaterialPropertyBlock priority tinting/sync, +7%/lvl RPM scaling, and Spacebar pause/build. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose.

> **100% AI-Generated Code & Docs Notice**
> Every line of code and documentation in this mod was **100% written by AI** (**Google Gemini** and **Anthropic Claude** models), directed and designed by **Rose**.

## Features
- **Direct Hotbar Drag-and-Drop + `Edit Bar` & 3 Saveable Layouts:** Drag any Row 1 Tower (15 slots) or Row 2 Building (5 slots) directly on the hotbar at any time to rearrange its position (`EnableHotbarDragAndDrop`), with real-time slot sliding preview! Click **`Edit Bar`** beside the hotbar to preview all 20 slots (dimmed for unowned towers so you can organize before unlocking them), fine-nudge `< Left` / `Right >`, and save/load up to **3 persistent Hotbar Layout Presets (`Layout 1`, `Layout 2`, `Layout 3`)**. Zero cheats involved.
- **Tower Fast Upgrades (`+10` Snap & Hold-to-Buy):** Hold Health/Armor/Shield upgrade buttons or `1`/`2`/`3` keys to continuously buy levels (`Normal` / `Fast` / `Turbo` speeds), or click the attached **`+10` Snap Panel** (`Shift+1/2/3` or `Shift+Click`) to jump directly to the next multiple of 10 (`3 -> 10 -> 20`).
- **`90 deg` Camera Rotation, Top-Down & Occluded Tile Placement:** Rotate camera cleanly in `90 deg` steps (`[` / `]`), toggle straight **Top-Down tactical view** (`V` or `AutoTopDownInBuildMode`), automatically billboard world-space expand arrows & placement ghosts, **X-Ray raycast through foreground cliffs/tall towers** to place on hidden grass tiles behind them, and hold **`LeftAlt`** to flatten tall tower meshes & select deeper hidden tiles/towers behind obstacles.
- **Quick Chests, Chop Trees & Early Expand HUD:** Shows non-intrusive **`Open Chests`** and **`Chop Trees`** buttons beside Tower HP (displaying exact total gold & chest drop chance) plus optional **Auto-Open Chests** (`PopupButton` / `AutoOpenImmediately` / `Off`), and **`AllowEarlyExpand`** so you can expand to the next tile mid-wave without losing current-wave XP/score/card draws.
- **Build & Hover Range Preview Rings:** Procedural `LineRenderer` circles/squares showing exact range including height bonuses while placing or hovering placed towers (skips UI hover cleanly).
- **Zero-Alloc `MaterialPropertyBlock` Priority Tinting + Persistent Defaults + Sync:** Color-codes tower tops cleanly without leaking per-tower `Material` instances, remembers your default priority triple per `TowerType` across runs, and keeps `Q`/`E` + `Ctrl+C`/`Ctrl+V` priority clipboards synchronized.
- **Levels-to-RPM + Spacebar Pause & Build:** Configurable `+7%` RPM per tower level (starting at Level 2 by default), `Space` pause/resume preserving speed, build ghost updates while paused, and `HideKeybindsOverlay`.

## Keybinds & Controls
- `[` / `]`: Rotate camera `-90 deg` / `+90 deg`.
- `V`: Toggle straight Top-Down tactical view (safely ignored during `Ctrl+V` paste).
- `LeftAlt` (hold): Flatten tall tower meshes + select/place behind foreground cliffs & towers.
- `Hold 1 / 2 / 3` or hold UI buttons: Rapid continuous Health/Armor/Shield upgrading.
- `Shift + 1 / 2 / 3` or click `+10`: Snap stat multiplier to the next multiple of 10.
- `Ctrl+C` / `Ctrl+V` (or `Q`/`E`) while selecting a tower: Copy & paste targeting priority triple.
- `Space`: Pause/Resume while preserving custom game speed; **Drag any hotbar icon**: reorder live; **`Edit Bar`** pill: preview all 20 slots & save `Layout 1/2/3`.

## What We Changed / Improved Over Upstream Inspiration
- Consolidated 8 standalone non-cheat QoL systems cleanly into one MIT plugin so you do not need separate `QoLChanges`, `QoLChangesPastePatch`, `LevelsToRpm`, or `WASDRemover` installs (while still detecting legacy plugins automatically and deferring via `ForceOverrideLegacy = false` if someone keeps them installed).
- Replaced shared-material color mutation with GPU-friendly `MaterialPropertyBlock` priority tinting (no VRAM material leaks across 100+ towers), synced `Ctrl+C`/`V` with `Q`/`E`, fixed Hover Range Rings triggering through UI panels, and moved all non-cheat UX out of `RogueTowerModMenu`.

## Source Code
- Visual C# project: [`src/RogueTowerQoL/`](../../../src/RogueTowerQoL/)
- Compiled plugin: `plugins/RogueTowerQoL.dll`

## Thunderstore Dependencies
- `bbepis-BepInEx_Rogue_Tower-5.4.1900`

## Installation (BepInEx 5)
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy `plugins/RogueTowerQoL.dll` into `Rogue Tower/BepInEx/plugins/RogueTowerQoL/`.

## Credits & Inspiration
- Clean-room implementation designed by **Rose**, inspired by community QoL concepts (`Theirown/QoLChanges`, `dmchurch` priority colors, `afld-LevelsToRpm`, `Jdewi-SpeedControls`, `RustyDios-WASDRemover`, and Steam Discussions occlusion feedback).