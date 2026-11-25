# RogueTowerQuickTree (v1.4.0)

All-in-one Research Tree Buy + Bundled Native Upgrade Refunding mod: single-click, Right-Click, branch, full-tree, & Refund All XP refunds (no separate Refund mod needed!), one-click Buy Tree, Shift/Ctrl chain buying, vanilla armor unlockString bug fixes, and in-run Auto-Max Branch. 100% AI-generated (Google Gemini & Anthropic Claude), designed by Rose.

> **100% AI-Generated Code & Docs Notice**
> Every line of code and documentation in this mod was **100% written by AI** (**Google Gemini** and **Anthropic Claude** models), directed and designed by **Rose**.

## Features
- **Bundled Upgrade Buying & Refunding in ONE Mod (No Separate Refund Mod Required):**
- **Per-Tree `Buy Tree` & `Refund Tree` Buttons + `Research All` / `Refund All`:** Added cleanly across all 15 tower trees, 6 passive/economy groups, and Burn/Poison/Bleed DOT branches, showing live purchasable counts, exact XP costs, or refundable `+XP`.
- **Smart Single-Click & Modifier Buying:** Left-Click any locked card deep in a tree to automatically buy its prerequisite chain (`ancestors -> card`); `Shift+Click` buys the prerequisite chain + whole branch; `Ctrl+Shift+Click` buys the entire tower/group tree (`Alt` temporarily flips the `SkipPermanentStatUpgradesByDefault` filter).
- **Native Single-Node, Branch & Full-Tree Refunding:** Left-Click or Right-Click any unlocked card to refund that node + its unlocked descendants for 100% XP; `Shift+Click` / `Shift+Right-Click` (or clicking a default root like Ballista/Mortar/Tesla) refunds the whole tree, automatically cascading `cardsWhichRequireCardCount` requirements and restoring cached locked sprites.
- **Vanilla Bug Fixes Built In:** Automatically repairs vanilla `VampireLair`, `Monument`, and `ParticleCannon` `Armor2`/`Armor3` `unlockString` collisions before `UpgradeButton.Start` so buying or refunding them never corrupts XP/unlocks.
- **In-Run `Auto-Max Branch` Progression:** `PlayMenu` & pre-round HUD toggle (`Off`, `Branch Line`, `Full Subtree`, `Whole Tower`, `All Researched!`) that automatically grabs researched follow-up cards during a run (synced cleanly with `RogueTowerModMenu` & `UpgradesMenu`).

## Keybinds & Controls
- `Left-Click` locked card: Auto-buy prerequisite chain up to clicked card.
- `Shift+Left-Click` locked card: Buy prerequisites + full sub-branch (`Ctrl+Shift+Click`: whole tree).
- `Left-Click` or `Right-Click` unlocked card: Refund card + unlocked descendants (`Shift`: whole tree).
- Hold `LeftAlt` on Research Tree: Temporarily invert `SkipPermanentStatUpgradesByDefault` filter.

## What We Changed / Improved Over Upstream Inspiration
- Combined **buying**, **single/branch/tree/all refunding**, and **in-run Auto-Max Branch** into one self-contained mod (`SCLeoX-RefundUpgrades` is completely optional and automatically guarded if also present).
- Fixed vanilla duplicate `Armor2`/`Armor3` `PlayerPrefs` unlock key bugs and added cascading `unlockedCardCount` refund verification so you can never remain illegally locked above your card-count gate.

## Source Code
- Visual C# project: [`src/QuickTree/`](../../../src/QuickTree/)
- Compiled plugin: `plugins/RogueTowerQuickTree.dll`

## Thunderstore Dependencies
- `bbepis-BepInEx_Rogue_Tower-5.4.1900`

## Installation (BepInEx 5)
1. Install **BepInEx 5** for Rogue Tower (`bbepis-BepInEx_Rogue_Tower-5.4.1900`).
2. Copy `plugins/RogueTowerQuickTree.dll` into `Rogue Tower/BepInEx/plugins/RogueTowerQuickTree/`.

## Credits & Inspiration
- Clean-room mod designed by **Rose**, inspired by research-tree QoL requests and `SCLeoX/RogueTower-RefundUpgrades` concepts (with standalone native refunding + vanilla `VampireLair`, `Monument`, and `ParticleCannon` armor `unlockString` fixes built in).