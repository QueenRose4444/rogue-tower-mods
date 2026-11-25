# RogueTowerLagFix (v1.3.0)

Late-game FPS & GC optimizer (verified 4.60 -> 43.54 FPS in 300-tower Level-40 8x stress tests): fast-forward physics scaling, pure-C# Tower.DetectEnemies & AoE scans, Enemy/Pathfinder caching, per-wave Dropper path cell cache, dead-enemy TakeDamage short-circuit, Landmine cap, HealthBar diffing, DamageNumber coalescing, FX caps, and live Mod Menu toggles. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose.

> **100% AI-Generated Code & Docs Notice**
> Every line of code and documentation in this mod was **100% written by AI** (**Google Gemini** and **Anthropic Claude** models), directed and designed by **Rose**.

## Features
- **Fast-Forward Physics Scaling (`OptimizeFastForwardPhysics`):** Scales `Time.fixedDeltaTime` up to `10x` speed (and restores exact `0.02s` when toggled off) so fast-forward never triggers a Unity `FixedUpdate` spiral of death.
- **Pure-C# Enemy & AoE Scanning (`OptimizeTargetingAndEnemyLookups`):** Replaces `Physics.OverlapSphere` + `Collider[]` allocations in `Tower.DetectEnemies`, `TeslaCoil.Fire`, and `VampireLair.Fire` with direct C# squared-distance scans over `SpawnManager.currentEnemies` + cached `Pathfinder` lookups (`0 B` GC per scan).
- **Per-Wave `Dropper` Path Cache + Active `Landmine` Cap (`180`):** Caches valid road cells once per wave instead of running 251,001 `Physics.Raycast` calls per Dropper when `InfiniteRange` is enabled, and caps simultaneous `Landmine` objects cleanly.
- **Dead-Enemy `TakeDamage` Short-Circuit + `Mathf.Pow` Removal:** Stops already-dead enemies in DOT/AoE loops from hitting `Debug.Log("Dead enemy taking damage")` thousands of times per second and replaces `Mathf.Pow(x, 2)` in `Tower.SelectEnemy` / `Lookout.SelectEnemy` with `x * x`.
- **`HealthBar` DOT String Diffing + `InfiniteRange` Hotspot Fixes:** Skips redundant `DOTUpdate()` `Text.text` rebuilds when DOT stacks have not changed and caps wasteful `FlameThrower` / `Cannonball` / `Obelisk` hotspots under huge ranges.
- **Pooled `DamageNumber` Coalescing + `ProjectileFX` / `Explosion` / Trail / Audio Caps:** Coalesces nearby floating damage numbers without spinning UI `RectTransform`s, caps simultaneous particle explosions, prevents `TeslaCoil`/`VampireLair` `WaitForEndOfFrame` trail coroutine leaks, and deduplicates overlapping combat SFX.

## Keybinds & Controls
- Every optimization category has a live config toggle in `BepInEx/config/Rosie.RogueTowerLagFix.cfg` and live ON/OFF switches in `RogueTowerModMenu` -> `Modules`.

## What We Changed / Improved Over Upstream Inspiration
- Verified via `DeepLagProfiler` on **Round 45 Omega (300 Level-40 Towers -- 20 of each tower + Max Card Trees + InfRange @ 8x Speed)**: raises mean FPS from **4.60 FPS** (`92/92` >100ms hangs, `430.29ms` worst spike) to **43.54 FPS** (`0` hangs over 128ms, **9.5x speedup**).

## Source Code
- Visual C# project: [`src/LagFix/`](../../../src/LagFix/)
- Compiled plugin: `plugins/RogueTowerLagFix.dll`

## Thunderstore Dependencies
- `bbepis-BepInEx_Rogue_Tower-5.4.1900`

## Installation (BepInEx 5)
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy `plugins/RogueTowerLagFix.dll` into `Rogue Tower/BepInEx/plugins/RogueTowerLagFix/`.

## Credits & Inspiration
- Clean-room optimizer designed by **Rose**, built strictly from game decompilation and live `DeepLagProfiler` telemetry.