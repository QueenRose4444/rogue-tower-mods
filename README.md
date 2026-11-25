# Rose's Rogue Tower Mod Suite

Original, clean-room **BepInEx 5** mods for **Rogue Tower** -- unified live **Mod Menu**, customizable tower/building hotbars & 5 loadouts, hold / `+10` snap tower upgrades, `90 deg` camera rotation & X-ray occluded placement, customizable Light & Dark themes (`RogueTowerThemes`), clean-room QoL & Levels-to-RPM (`RogueTowerQoL`), late-game FPS optimizations (`RogueTowerLagFix`), one-click Research Tree automation (`RogueTowerQuickTree`), custom maps (`RogueTowerCustomMaps`), and reproducible Round 40/45 Level-40 FPS benchmark scenes with a per-frame stall & hang profiler.

> **AI Collaboration Notice**
> These mods were created by **Rose** with AI assistance using **Google Gemini** and **Anthropic Claude** models.

---

## Included Clean-Room Mods (Source + Packaged Releases)

| Mod Package | Version | Plugin DLL | Summary |
| :--- | :---: | :--- | :--- |
| **[RogueTowerModMenu](./mods/1-core-mod-menu/RogueTowerModMenu/README.md)** | `2.4.0` | `RogueTowerCustomLevels.dll` | Unified live Mod Menu (` / F6), Quick HUD (F10), tower/building loadouts & hotbar sorting, hold/+10 tower upgrades, 90-deg/top-down/X-ray placement, sandbox cheats, Round 40/45 Level-40 benchmark scenes + per-frame stall profiler, and loopback test harness. Created by Rose with Gemini & Claude. |
| **[RogueTowerLagFix](./mods/2-performance-and-profiling/RogueTowerLagFix/README.md)** | `1.3.0` | `RogueTowerLagFix.dll` | Late-game FPS & GC optimizer: 10x fast-forward physics scaling, pure-C# Tower.DetectEnemies & AoE scanning, Enemy/Pathfinder caching, per-wave Dropper path cache, HealthBar diffing, DamageNumber coalescing, Landmine cap, and live Mod Menu toggles. Created by Rose with Gemini & Claude. |
| **[RogueTowerQuickTree](./mods/3-research-tree-qol/RogueTowerQuickTree/README.md)** | `1.4.0` | `RogueTowerQuickTree.dll` | One-click Research Tree Buy/Refund buttons, Shift/Ctrl deep subtree purchasing, cascade refund guards, and live Mod Menu Auto-Max card controls. Created by Rose with Gemini & Claude. |
| **[RogueTowerThemes](./mods/4-visuals-and-qol/RogueTowerThemes/README.md)** | `1.0.0` | `RogueTowerThemes.dll` | Clean-room customizable Light & Dark mode switcher with non-destructive original scene caching, per-theme sky/sun/ambient/UI brightness presets, F7 quick toggle, and live Mod Menu integration. Created by Rose with Gemini & Claude. |
| **[RogueTowerQoL](./mods/4-visuals-and-qol/RogueTowerQoL/README.md)** | `1.0.0` | `RogueTowerQoL.dll` | Clean-room MIT QoL & Levels-to-RPM suite: build & hover range rings, MaterialPropertyBlock priority tinting, persistent per-tower priority defaults & Ctrl+C/V sync, +7% RPM/level scaling, Spacebar pause/build, and automatic legacy plugin conflict guards. Created by Rose with Gemini & Claude. |
| **[RogueTowerCustomMaps](./mods/5-custom-maps/RogueTowerCustomMaps/README.md)** | `1.2.0` | `RogueTowerCustomMaps.dll` | Clean-room remake of custom map generation (Spiral CCW/CW, Barely Legal, The Clock, Instant Portal, T-Split, Custom CSV) with Mod Menu integration. Inspired by acr-Rogue_Tower_Custom_Maps; written by Rose with Gemini & Claude. |

*(Third-party mods without an explicit open-source license on GitHub/Thunderstore are **not** bundled here -- install any companion mods directly from their upstream Thunderstore/GitHub releases below.)*

---

## Companion Community Mods & Upstream Inspiration

Install these upstream packages from Thunderstore / GitHub if you want companion QoL features (all detected and integrated cleanly by `RogueTowerModMenu`):
- **BepInEx Pack (Required):** `bbepis-BepInEx_Rogue_Tower-5.4.1900` ([Thunderstore](https://thunderstore.io/c/rogue-tower/))
- **Dark Mode UI (`Jdewi-DarkMode`) / Custom Light & Dark Themes:** `RogueTowerThemes` adds customizable non-destructive Light/Dark modes; companion `Jdewi-DarkMode` is also supported.
- **Game Speed Controls & Clean-Room QoL:** [`Jdewi-SpeedControls`], `RogueTowerQoL` (standalone clean-room MIT suite), plus companion [`TheirOwn/QoLChanges`](https://github.com/TheirOwn/QoLChanges), `RustyDios-QoLChangesPastePatch`, `afld-LevelsToRpm`, `RustyDios-WASDRemover`
- **Card Trees & Banishing:** [`agustinbutrico/BanishCards`](https://github.com/agustinbutrico/BanishCards), [`agustinbutrico/UpgradesMenu`](https://github.com/agustinbutrico/UpgradesMenu), [`agustinbutrico/CardDataLib`](https://github.com/agustinbutrico/CardDataLib), `CardRenderer`
- **Refund Upgrades:** [`SCLeoX/RogueTower-RefundUpgrades`](https://github.com/SCLeoX/RogueTower-RefundUpgrades)
- **Disable Triumphs / Slot Keybinds / Inspector:** [`harbingerofme/RogueTowerMods`](https://github.com/harbingerofme/RogueTowerMods), [`daymxn/RogueTower-SlotKeybinds`](https://github.com/daymxn/RogueTower-SlotKeybinds), `sinai-dev-UnityExplorer`
- **Community & Price-Scaling Inspiration:** Steam Discussions (`app/1843760`) and `SashaFloats` (`TowerFlyweight` research on UnknownCheats)

---

## Installing Compiled Mods
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy each mod's `plugins/<Mod>.dll` from `./mods/<group>/<Mod>/plugins/` into `Rogue Tower/BepInEx/plugins/<Mod>/`.
3. Launch **Rogue Tower** and press `` ` `` (or `F6`) to open the **Mod Menu** (`F7` toggles **Dark <-> Light** theme!).

## Building from Source
1. Ensure Rogue Tower + BepInEx 5 are installed at the default Steam path (or set `ROGUE_TOWER_DIR` to your install folder).
2. Run:
   ````powershell
   dotnet build .\src\RogueTowerMods.slnx -c Release
   ````

## License
Released under the [MIT License](./LICENSE). All source in [`src/`](./src/) is original clean-room code authored for this project.