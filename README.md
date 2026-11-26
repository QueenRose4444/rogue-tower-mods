# Rose's Rogue Tower Mod Suite

Original, clean-room **BepInEx 5** mods for **Rogue Tower** -- cleanly split between **Mod Manager & Settings Hub** (`RogueTowerModMenu`), **Non-Cheat Quality of Life & Hotbar Sorting** (`RogueTowerQoL`), **Late-Game FPS Optimizations** (`RogueTowerLagFix`), **All-in-One Research Tree Buy + Refunds + Auto-Max** (`RogueTowerQuickTree`), **Customizable Light & Dark Themes** (`RogueTowerThemes`), **In-Game Custom Map Building** (`RogueTowerCustomMaps`), and **Optional Sandbox Cheats & Stress Tests** (`RogueTowerCheats`).

> **100% AI-Generated Code & Docs Notice**
> Every line of code and documentation in this repository was **100% written by AI** (**Google Gemini** and **Anthropic Claude** models), directed and designed by **Rose**.

---

## Included Clean-Room Mods (Source + Packaged Releases)

*(Click any mod name below to browse its mod folder & full documentation.)*

| Mod Package | Version | Plugin DLL | Summary |
| :--- | :---: | :--- | :--- |
| **[RogueTowerModMenu](./mods/1-core-mod-menu/RogueTowerModMenu)** | `3.0.0` | `RogueTowerModMenu.dll` | Clean-room, cheat-free BepInEx Mod Manager (`), live Settings dashboard for any installed plugin, non-cheat DeepLagProfiler live FPS/hitch analyzer, and loopback test harness. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose. |
| **[RogueTowerCheats](./mods/6-cheats-and-sandbox/RogueTowerCheats)** | `1.0.0` | `RogueTowerCheats.dll` | Optional Cheats & Sandbox mod for Rogue Tower (F6 or plugged directly into RogueTowerModMenu): Infinite Gold/Mana/Range/Banishes, God Mode, No Tower Price Scaling, Tower Damage/RPM multipliers, Tower & Loadout spawners, Card/Card-Tree grants, and Level-40 Endgame Stress Scenes. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose. |
| **[RogueTowerQoL](./mods/4-visuals-and-qol/RogueTowerQoL)** | `2.1.0` | `RogueTowerQoL.dll` | Complete non-cheat MIT QoL suite for Rogue Tower: direct hotbar drag-and-drop sorting + saveable layout presets, tower hold & +10 snap upgrades, 90-deg camera rotation/top-down/X-ray occluded tile placement, Open Chests & Chop Trees HUD, early wave expand, range preview rings, MaterialPropertyBlock priority tinting/sync, +7%/lvl RPM scaling, and Spacebar pause/build. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose. |
| **[RogueTowerLagFix](./mods/2-performance-and-profiling/RogueTowerLagFix)** | `1.3.0` | `RogueTowerLagFix.dll` | Late-game FPS & GC optimizer (verified 4.60 -> 43.54 FPS in 300-tower Level-40 8x stress tests): fast-forward physics scaling, pure-C# Tower.DetectEnemies & AoE scans, Enemy/Pathfinder caching, per-wave Dropper path cell cache, dead-enemy TakeDamage short-circuit, Landmine cap, HealthBar diffing, DamageNumber coalescing, FX caps, and live Mod Menu toggles. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose. |
| **[RogueTowerQuickTree](./mods/3-research-tree-qol/RogueTowerQuickTree)** | `1.4.0` | `RogueTowerQuickTree.dll` | All-in-one Research Tree Buy + Bundled Native Upgrade Refunding mod: single-click, Right-Click, branch, full-tree, & Refund All XP refunds (no separate Refund mod needed!), one-click Buy Tree, Shift/Ctrl chain buying, vanilla armor unlockString bug fixes, and in-run Auto-Max Branch. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose. |
| **[RogueTowerThemes](./mods/4-visuals-and-qol/RogueTowerThemes)** | `1.1.0` | `RogueTowerThemes.dll` | Clean-room customizable Light & Dark mode switcher with toon-safe ambient & shadow fill preservation, non-destructive scene caching, separate Dark/Light presets, F7 quick toggle, and Mod Menu live switching. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose. |
| **[RogueTowerCustomMaps](./mods/5-custom-maps/RogueTowerCustomMaps)** | `1.3.1` | `RogueTowerCustomMaps.dll` | Clean-room custom map generator featuring Play Menu preset cycling, a built-in In-Game Custom Map Sequence Builder UI (click +S/+L/+R/+SLR/+Portal chips without editing configs!), non-destructive TileManager pool restoration, and Mod Menu sync. Inspired by acr-Rogue_Tower_Custom_Maps; 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose. |

*(Third-party mods without an explicit open-source license on GitHub/Thunderstore are **not** bundled here -- install any optional companion mods directly from their upstream Thunderstore/GitHub releases below.)*

---

## Companion Community Mods & Upstream Inspiration

Optional upstream packages on Thunderstore / GitHub (supported cleanly by `RogueTowerModMenu`'s module bridge -- note that `RogueTowerQoL` already bundles non-cheat QoL, Levels-to-RPM, Pause/WASD fixes, and `RogueTowerQuickTree` already bundles Upgrade Refunds natively):
- **BepInEx Pack (Required):** `bbepis-BepInEx_Rogue_Tower-5.4.1900` ([Thunderstore](https://thunderstore.io/c/rogue-tower/))
- **Light & Dark Themes:** `RogueTowerThemes` (customizable Light/Dark presets); optional legacy `Jdewi-DarkMode`
- **QoL & Game Speed:** `RogueTowerQoL` (hotbar sorter, `+10` hold upgrades, `90 deg`/Top-Down/X-Ray, range rings, priority tinting/sync, `+7%/lvl` RPM, pause/build), `Jdewi-SpeedControls`, plus optional legacy [`TheirOwn/QoLChanges`](https://github.com/TheirOwn/QoLChanges), `RustyDios-QoLChangesPastePatch`, `afld-LevelsToRpm`, `RustyDios-WASDRemover`
- **Card Trees & Refunds:** `RogueTowerQuickTree` (includes single/branch/tree/all refunds natively), [`agustinbutrico/BanishCards`](https://github.com/agustinbutrico/BanishCards), [`agustinbutrico/UpgradesMenu`](https://github.com/agustinbutrico/UpgradesMenu), [`agustinbutrico/CardDataLib`](https://github.com/agustinbutrico/CardDataLib), `CardRenderer`, optional [`SCLeoX/RogueTower-RefundUpgrades`](https://github.com/SCLeoX/RogueTower-RefundUpgrades)
- **Disable Triumphs / Slot Keybinds / Inspector:** [`harbingerofme/RogueTowerMods`](https://github.com/harbingerofme/RogueTowerMods), [`daymxn/RogueTower-SlotKeybinds`](https://github.com/daymxn/RogueTower-SlotKeybinds), `sinai-dev-UnityExplorer`
- **Community & Price-Scaling Inspiration:** Steam Discussions (`app/1843760`) and `SashaFloats` (`TowerFlyweight` research on UnknownCheats)

---

## Installing Compiled Mods
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy each mod's `plugins/<Mod>.dll` from `./mods/<group>/<Mod>/plugins/` into `Rogue Tower/BepInEx/plugins/<Mod>/`.
3. Launch **Rogue Tower**:
   - Play **vanilla/legitimate with great QoL & Mod Manager**: install `RogueTowerModMenu`, `RogueTowerQoL`, `RogueTowerLagFix`, `RogueTowerQuickTree`, `RogueTowerThemes`, and `RogueTowerCustomMaps`, then press `` ` `` (`BackQuote`) for mods/settings/profiling.
   - Play **Sandbox / Cheats / Stress Benchmarks**: also include `RogueTowerCheats` (`F6`, or tabs inside `RogueTowerModMenu`).

## Building from Source
1. Ensure Rogue Tower + BepInEx 5 are installed at the default Steam path (or set `ROGUE_TOWER_DIR` to your install folder).
2. Run:
   ````powershell
   dotnet build .\src\RogueTowerMods.slnx -c Release
   ````

## License
Released under the [MIT License](./LICENSE). All source in [`src/`](./src/) is original clean-room code authored for this project.