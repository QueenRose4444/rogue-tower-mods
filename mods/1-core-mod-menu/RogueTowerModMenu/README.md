# RogueTowerModMenu (v2.4.0)

Unified live Mod Menu (` / F6), Quick HUD (F10), tower/building loadouts & hotbar sorting, hold/+10 tower upgrades, 90-deg/top-down/X-ray placement, sandbox cheats, Round 40/45 Level-40 benchmark scenes + per-frame stall profiler, and loopback test harness. Created by Rose with Gemini & Claude.

> **AI Collaboration Notice**
> Created by **Rose** with AI assistance using **Google Gemini** and **Anthropic Claude** models.

## Features
- Unified tabbed Mod Menu (`` ` `` / `BackQuote` or `F6`) + draggable Quick HUD (`F10`) + header `Theme` switch.
- **Towers & Loadouts Tab:** Lock/unlock all 15 Row 1 Towers (including Ballista) & 5 Row 2 Buildings, reorder hotbar slots (`Up`/`Dn`), save/apply/auto-apply 5 loadouts, and optionally lock card draws to your hotbar.
- **Camera & Occluded Placement:** `90 deg` camera rotation (`[` / `]`), Top-Down tactical view (`V`), X-ray build raycasting behind cliffs/tall towers, and `LeftAlt` tower cutaway + deep selection.
- **Tower Upgrade UX:** Hold Health/Armor/Shield buttons to auto-buy continuously, or click/hold `+10` Snap to jump to the next multiple of 10.
- **Sandbox Cheats:** Infinite Money/Mana/Range, God Mode, **No Tower Price Scaling**, Give Tower / Give Cards / Give Card Trees, Multipliers.
- **Endgame Benchmark Scenes & Deep Lag Profiler:** 5 preset Round 40/45 Level-40 stress scenes (up to **300 Towers -- 20 of every tower + Max Trees + Inf Range @ 8x Speed**), per-frame hang/spike culprit attribution, visuals vs FPS matrix, and `127.0.0.1:18437` focusless CLI harness.

## Source Code
- Visual C# project: [`src/CustomLevels/`](../../../src/CustomLevels/)
- Compiled plugin: `plugins/RogueTowerCustomLevels.dll`

## Thunderstore Dependencies
- `bbepis-BepInEx_Rogue_Tower-5.4.1900`

## Installation (BepInEx 5)
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy `plugins/RogueTowerCustomLevels.dll` into `Rogue Tower/BepInEx/plugins/RogueTowerModMenu/`.

## Credits & Inspiration
- Original suite by **Rose**, inspired by the Rogue Tower Steam Discussions & Thunderstore communities (`app/1843760`) and `SashaFloats` (UnknownCheats `TowerFlyweight` price-scaling research that inspired No Tower Price Scaling).