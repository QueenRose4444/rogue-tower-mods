using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: AssemblyCompany("RogueTowerCheats")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyDescription("Clean-room Cheats & Sandbox mod for Rogue Tower: Infinite Gold/Mana/Range/Banishes, God Mode, No Tower Price Scaling, Tower & Loadout Spawner, Card/Card-Tree Grants, and Level-40 FPS Benchmark Scenes. Plugs cleanly into RogueTowerModMenu or runs standalone on F6.")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
[assembly: AssemblyProduct("RogueTowerCheats")]
[assembly: AssemblyTitle("RogueTowerCheats")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace RogueTowerCheats;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("Rosie.RogueTowerModMenu", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Rosie.RogueTowerQoL", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("AgusBut.CardDataLib", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("AgusBut.BanishCards", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("AgusBut.UpgradesMenu", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Jdewi.SpeedControls", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Rosie.RogueTowerLagFix", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Rosie.RogueTowerThemes", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
	public const string PluginGuid = "Rosie.RogueTowerCheats";
	public const string PluginName = "RogueTowerCheats";
	public const string PluginVersion = "1.0.0";

	internal static Plugin Instance;
	internal static ManualLogSource Log;

	internal static ConfigEntry<KeyCode> CheatHotkey;
	internal static ConfigEntry<bool> ShowStandaloneButton;

	// Cheats
	internal static ConfigEntry<bool> InfiniteMoney;
	internal static ConfigEntry<bool> InfiniteMana;
	internal static ConfigEntry<bool> InfiniteRange;
	internal static ConfigEntry<bool> GodMode;
	internal static ConfigEntry<bool> InfiniteBanishes;
	internal static ConfigEntry<bool> NoTowerPriceScaling;
	internal static ConfigEntry<float> DamageMultiplier;
	internal static ConfigEntry<float> FireRateMultiplier;
	internal static ConfigEntry<bool> IncludeUnresearchedCards;

	// Loadouts & Standalone Hotbar Order Fallback
	internal static ConfigEntry<bool> AutoSortHotbar;
	internal static ConfigEntry<string> TowerHotbarOrder;
	internal static ConfigEntry<string> BuildingHotbarOrder;
	internal static ConfigEntry<int> AutoApplyLoadoutSlot;
	internal static ConfigEntry<bool> LockCardPoolToHotbar;
	internal static ConfigEntry<string> LoadoutSlot1;
	internal static ConfigEntry<string> LoadoutSlot2;
	internal static ConfigEntry<string> LoadoutSlot3;
	internal static ConfigEntry<string> LoadoutSlot4;
	internal static ConfigEntry<string> LoadoutSlot5;

	private Harmony _harmony;
	private bool _registeredWithModMenu;

	private void Awake()
	{
		Instance = this;
		Log = Logger;

		CheatHotkey = Config.Bind("General", "CheatHotkey", KeyCode.F6, "Hotkey to jump straight to Cheats (inside RogueTowerModMenu, or standalone Cheat Window if ModMenu is not installed).");
		ShowStandaloneButton = Config.Bind("General", "ShowStandaloneButton", false, "Shows a dedicated 'Cheats (F6)' pill even when RogueTowerModMenu is installed (always shown if ModMenu is absent).");

		InfiniteMoney = Config.Bind("Cheats", "InfiniteMoney", false, "Infinite Gold (purchases never fail or spend gold).");
		InfiniteMana = Config.Bind("Cheats", "InfiniteMana", false, "Infinite Mana (mana stays full and casts are free).");
		InfiniteRange = Config.Bind("Cheats", "InfiniteRange", false, "Infinite Range (all towers have 250 range / global coverage).");
		GodMode = Config.Bind("Cheats", "GodMode", false, "God Mode (your main Tower takes zero damage).");
		InfiniteBanishes = Config.Bind("Cheats", "InfiniteBanishes", false, "Infinite card Banishes per run (when BanishCards is installed).");
		NoTowerPriceScaling = Config.Bind("Cheats", "NoTowerPriceScaling", false, "Keeps tower & building gold costs fixed at basePrice instead of increasing with each copy built.");
		DamageMultiplier = Config.Bind("Cheats", "DamageMultiplier", 1f, "Global tower base damage multiplier (1x, 2x, 5x, 10x, 100x).");
		FireRateMultiplier = Config.Bind("Cheats", "FireRateMultiplier", 1f, "Global tower fire rate (RPM) multiplier (1x, 2x, 3x, 5x).");
		IncludeUnresearchedCards = Config.Bind("Cheats", "IncludeUnresearchedCards", true, "When granting Cards or Card Trees, include cards not yet unlocked with meta XP.");

		AutoSortHotbar = Config.Bind("Hotbar & Loadouts", "AutoSortHotbar", true, "Standalone fallback if RogueTowerQoL is not installed.");
		TowerHotbarOrder = Config.Bind("Hotbar & Loadouts", "TowerHotbarOrder", HotbarAndLoadoutManager.DefaultTowerOrderCsv, "Fallback Row 1 Tower order when RogueTowerQoL is not installed.");
		BuildingHotbarOrder = Config.Bind("Hotbar & Loadouts", "BuildingHotbarOrder", HotbarAndLoadoutManager.DefaultBuildingOrderCsv, "Fallback Row 2 Building order when RogueTowerQoL is not installed.");
		AutoApplyLoadoutSlot = Config.Bind("Hotbar & Loadouts", "AutoApplyLoadoutSlot", 0, "0 = Off, 1..5 = automatically grant that saved cheat loadout at the start of every run.");
		LockCardPoolToHotbar = Config.Bind("Hotbar & Loadouts", "LockCardPoolToHotbar", false, "Cheat filter: restricts random card draws so you only draw upgrades for towers/buildings currently unlocked on your hotbar.");
		LoadoutSlot1 = Config.Bind("Hotbar & Loadouts", "LoadoutSlot1", "Ballista,Mortar,FlameThrower,PoisonSprayer,FrostKeep,TeslaCoil,Shredder,Lookout|ManaSiphon,ManaBank,HauntedHouse", "Saved Loadout Slot 1 (Towers|Buildings).");
		LoadoutSlot2 = Config.Bind("Hotbar & Loadouts", "LoadoutSlot2", "Ballista,Mortar,Lookout,Radar,Obelisk,ParticleCannon|ManaSiphon,ManaBank,University", "Saved Loadout Slot 2 (Towers|Buildings).");
		LoadoutSlot3 = Config.Bind("Hotbar & Loadouts", "LoadoutSlot3", "", "Saved Loadout Slot 3 (Towers|Buildings).");
		LoadoutSlot4 = Config.Bind("Hotbar & Loadouts", "LoadoutSlot4", "", "Saved Loadout Slot 4 (Towers|Buildings).");
		LoadoutSlot5 = Config.Bind("Hotbar & Loadouts", "LoadoutSlot5", "", "Saved Loadout Slot 5 (Towers|Buildings).");

		CheatBridge.SyncBanishOverride(InfiniteBanishes.Value);

		SceneManager.activeSceneChanged += OnActiveSceneChanged;

		_harmony = new Harmony(PluginGuid);
		_harmony.PatchAll(typeof(CheatPatches));
		_harmony.PatchAll(typeof(HotbarLoadoutPatches));
		_harmony.PatchAll(typeof(StandaloneCheatInputPatches));

		TryRegisterModMenuIntegration();
		Log.LogInfo($"{PluginName} v{PluginVersion} loaded (ModMenu={(CheatBridge.HasModMenu ? "plugged-in" : "standalone")}).");
	}

	private void Start()
	{
		if (!_registeredWithModMenu)
		{
			TryRegisterModMenuIntegration();
		}
	}

	private void TryRegisterModMenuIntegration()
	{
		if (_registeredWithModMenu || !CheatBridge.HasModMenu) return;
		_registeredWithModMenu = true;

		CheatBridge.RegisterTab("cheats", "Cheats", 40, parent => CheatTabsUI.BuildCheatsTab(parent));
		CheatBridge.RegisterTab("towers", "Towers", 50, parent => CheatTabsUI.BuildTowersTab(parent));
		CheatBridge.RegisterTab("cards", "Cards", 60, parent => CheatTabsUI.BuildCardsTab(parent));
		CheatBridge.RegisterTab("trees", "Card Trees", 70, parent => CheatTabsUI.BuildCardTreesTab(parent));
		CheatBridge.RegisterHeaderBadge(() =>
		{
			int c = CheatActions.ActiveCheatCount();
			return c > 0 ? $"Cheats: {c}" : null;
		});

		CheatBridge.RegisterBenchmarkStatusProvider(() => BenchmarkSceneRunner.BuildStatusJson());
		RegisterHarnessCommands();
	}

	private void RegisterHarnessCommands()
	{
		CheatBridge.RegisterHarnessCommand("set_cheat", p =>
		{
			p.TryGetValue("name", out string cname);
			p.TryGetValue("val", out string cval);
			bool ok = ApplyCheatSetting(cname, cval);
			CheatTabsUI.RefreshOpenUi();
			return $"{{\"ok\":{(ok ? "true" : "false")}}}";
		});

		CheatBridge.RegisterHarnessCommand("set_tower", p =>
		{
			p.TryGetValue("id", out string id);
			bool unlock = !p.TryGetValue("unlock", out string uStr) || bool.Parse(uStr);
			bool ok = HotbarAndLoadoutManager.SetItemUnlocked(id, unlock);
			CheatTabsUI.RefreshOpenUi();
			return $"{{\"ok\":{(ok ? "true" : "false")}}}";
		});

		CheatBridge.RegisterHarnessCommand("apply_loadout", p =>
		{
			int slot = 1;
			if (p.TryGetValue("slot", out string sStr)) int.TryParse(sStr, out slot);
			int cnt = HotbarAndLoadoutManager.ApplyLoadoutSlot(Mathf.Clamp(slot, 1, 5));
			CheatTabsUI.RefreshOpenUi();
			return $"{{\"ok\":true,\"unlocked\":{cnt}}}";
		});

		CheatBridge.RegisterHarnessCommand("move_hotbar", p =>
		{
			p.TryGetValue("id", out string id);
			int delta = -1;
			if (p.TryGetValue("delta", out string dStr)) int.TryParse(dStr, out delta);
			HotbarAndLoadoutManager.MoveItemInOrder(id, delta);
			CheatTabsUI.RefreshOpenUi();
			return "{\"ok\":true}";
		});

		CheatBridge.RegisterHarnessCommand("benchmark_status", _ => BenchmarkSceneRunner.BuildStatusJson());

		CheatBridge.RegisterHarnessCommand("load_benchmark", p =>
		{
			if (BenchmarkSceneRunner.IsBusy)
			{
				return $"{{\"ok\":false,\"error\":{JsonStr("benchmark already running: " + BenchmarkSceneRunner.Phase)}}}";
			}
			p.TryGetValue("preset", out string presetId);
			if (BenchmarkSceneRunner.FindPreset(presetId) == null)
			{
				return $"{{\"ok\":false,\"error\":{JsonStr("unknown preset: " + presetId)}}}";
			}
			float dur = -1f;
			if (p.TryGetValue("duration", out string dStr)) float.TryParse(dStr, NumberStyles.Float, CultureInfo.InvariantCulture, out dur);
			float warm = 2f;
			if (p.TryGetValue("warmup", out string wStr)) float.TryParse(wStr, NumberStyles.Float, CultureInfo.InvariantCulture, out warm);
			bool record = !p.TryGetValue("record", out string recStr) || !string.Equals(recStr, "false", StringComparison.OrdinalIgnoreCase);
			p.TryGetValue("outDir", out string bOutDir);
			BenchmarkSceneRunner.StartBenchmark(this, presetId, dur, warm, record, bOutDir);
			return $"{{\"ok\":true,\"started\":true,\"preset\":{JsonStr(BenchmarkSceneRunner.ActivePresetId)}}}";
		});
	}

	private static bool ApplyCheatSetting(string name, string val)
	{
		if (string.IsNullOrEmpty(name)) return false;
		bool.TryParse(val, out bool b);
		switch (name.ToLowerInvariant())
		{
			case "infinitemoney": InfiniteMoney.Value = b; return true;
			case "infinitemana": InfiniteMana.Value = b; return true;
			case "infiniterange": InfiniteRange.Value = b; CheatActions.RefreshAllPlacedTowers(); return true;
			case "godmode": GodMode.Value = b; return true;
			case "infinitebanishes": InfiniteBanishes.Value = b; CheatBridge.SyncBanishOverride(b); return true;
			case "notowerpricescaling": NoTowerPriceScaling.Value = b; CheatActions.RefreshTowerPrices(); return true;
			case "damagemultiplier":
				if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float dm))
				{
					DamageMultiplier.Value = dm;
					CheatActions.RefreshAllPlacedTowers();
					return true;
				}
				return false;
			case "fireratemultiplier":
				if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float fm))
				{
					FireRateMultiplier.Value = fm;
					CheatActions.RefreshAllPlacedTowers();
					return true;
				}
				return false;
			default:
				return false;
		}
	}

	private static string JsonStr(string s)
	{
		if (s == null) return "null";
		return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
	}

	private void OnDestroy()
	{
		if (_registeredWithModMenu && CheatBridge.HasModMenu)
		{
			CheatBridge.UnregisterTab("cheats");
			CheatBridge.UnregisterTab("towers");
			CheatBridge.UnregisterTab("cards");
			CheatBridge.UnregisterTab("trees");
		}
		SceneManager.activeSceneChanged -= OnActiveSceneChanged;
		_harmony?.UnpatchSelf();
	}

	private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
	{
		CheatActions.OnSceneChanged(newScene.name);
		HotbarAndLoadoutManager.OnSceneChanged();
		CheatTabsUI.OnSceneChanged();
		CheatBridge.SyncBanishOverride(InfiniteBanishes.Value);
	}

	private void Update()
	{
		CheatTabsUI.EnsureStandaloneCreatedIfNeeded();

		if (Input.GetKeyDown(CheatHotkey.Value) && !CheatTabsUI.IsStandaloneSearchFocused)
		{
			if (CheatBridge.HasModMenu)
			{
				if (CheatBridge.IsModMenuOpen()) CheatBridge.SetModMenuOpen(false);
				else CheatBridge.OpenMenuToTab("cheats");
			}
			else
			{
				CheatTabsUI.ToggleStandalone();
			}
		}

		CheatActions.Tick();
		CheatTabsUI.Tick();
		BenchmarkSceneRunner.TickFrameSample();
	}
}
