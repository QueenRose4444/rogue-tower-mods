# RogueTowerQoL (v1.0.0)

Clean-room MIT QoL & Levels-to-RPM suite: build & hover range rings, MaterialPropertyBlock priority tinting, persistent per-tower priority defaults & Ctrl+C/V sync, +7% RPM/level scaling, Spacebar pause/build, and automatic legacy plugin conflict guards. Created by Rose with Gemini & Claude.

> **AI Collaboration Notice**
> Created by **Rose** with AI assistance using **Google Gemini** and **Anthropic Claude** models.

## Features
- Procedural LineRenderer range preview circle/square in build mode and while hovering placed towers (skips UI hover cleanly).
- Zero-allocation MaterialPropertyBlock tower priority tinting, persistent per-TowerType default priorities, and synchronized Q/E + Ctrl+C/Ctrl+V priority clipboards.
- Configurable **Levels-to-RPM** (+7% RPM per level starting at Level 2 by default) + standalone Spacebar pause/resume + build-while-paused.
- Automatically defers to legacy Thunderstore plugins if both are installed side-by-side so hooks never duplicate.

## Source Code
- Visual C# project: [`src/RogueTowerQoL/`](../../../src/RogueTowerQoL/)
- Compiled plugin: `plugins/RogueTowerQoL.dll`

## Thunderstore Dependencies
- `bbepis-BepInEx_Rogue_Tower-5.4.1900`

## Installation (BepInEx 5)
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy `plugins/RogueTowerQoL.dll` into `Rogue Tower/BepInEx/plugins/RogueTowerQoL/`.

## Credits & Inspiration
- Original clean-room implementation by **Rose**, inspired by community QoL concepts (Theirown/QoLChanges, dmchurch priority colors, fld-LevelsToRpm, Jdewi-SpeedControls, RustyDios-WASDRemover).