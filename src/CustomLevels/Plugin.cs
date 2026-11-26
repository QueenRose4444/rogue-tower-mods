using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: AssemblyCompany("RogueTowerModMenu")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyDescription("Cheat Mod Menu (` / F6) for Rogue Tower: sandbox cheats, Give/Lock Towers & 5 Saved Loadouts, Give Cards & Card Trees, Live Modules Dashboard, Endgame Level-40 Benchmark Scenes & DeepLagProfiler, and loopback test harness. Non-cheat QoL lives in RogueTowerQoL.")]
[assembly: AssemblyFileVersion("2.5.1.0")]
[assembly: AssemblyInformationalVersion("2.5.1")]
[assembly: AssemblyProduct("RogueTowerModMenu")]
[assembly: AssemblyTitle("RogueTowerQuickChests")]
[assembly: AssemblyVersion("2.5.1.0")]

namespace RogueTowerCustomLevels;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("AgusBut.CardDataLib", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("AgusBut.BanishCards", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("AgusBut.UpgradesMenu", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Jdewi.SpeedControls", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Rosie.RogueTowerQuickTree", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("acr.rougetower.custommaps", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Rosie.RogueTowerThemes", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Rosie.RogueTowerQoL", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
	public const string PluginGuid = "Rosie.RogueTowerCustomLevels";
	public const string PluginName = "RogueTowerQuickChests";
	public const string PluginVersion = "2.5.1";

	internal static ManualLogSource Log;

	// Mod Menu settings
	internal static ConfigEntry<KeyCode> MenuHotkey;
	internal static ConfigEntry<bool> ShowCornerButton;
	internal static ConfigEntry<bool> PauseWhileMenuOpen;
	internal static ConfigEntry<float> WindowPosX;
	internal static ConfigEntry<float> WindowPosY;

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
	internal static ConfigEntry<bool> LegacyCheatModeEnabled;

	// Saved Cheat Loadouts & Standalone Hotbar Order Fallback
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

	// Focusless Test Harness (127.0.0.1)
	internal static ConfigEntry<bool> EnableTestServer;
	internal static ConfigEntry<int> TestServerPort;
	internal static ConfigEntry<bool> RunInBackgroundForTests;
	internal static ConfigEntry<bool> MuteAudioForTests;
	internal static ConfigEntry<string> TestOutputDir;

	private Harmony _harmony;

	private void Awake()
	{
		Log = Logger;

		MenuHotkey = Config.Bind("Mod Menu", "MenuHotkey", KeyCode.BackQuote, "Hotkey to open/close the Mod Menu (default: BackQuote / ` key).");
		ShowCornerButton = Config.Bind("Mod Menu", "ShowCornerButton", true, "Shows a small 'Mods' pill in the top-right corner below the Upgrades book icon.");
		PauseWhileMenuOpen = Config.Bind("Mod Menu", "PauseWhileMenuOpen", false, "Automatically pauses the game while the Mod Menu is open.");
		WindowPosX = Config.Bind("Mod Menu", "WindowPosX", 0f, "Saved Mod Menu window X offset.");
		WindowPosY = Config.Bind("Mod Menu", "WindowPosY", 0f, "Saved Mod Menu window Y offset.");

		LegacyCheatModeEnabled = Config.Bind("Cheats", "CheatModeEnabled", false, "Legacy all-in-one cheat flag (migrated to individual toggles).");
		InfiniteMoney = Config.Bind("Cheats", "InfiniteMoney", false, "Infinite Gold (purchases never fail or spend gold).");
		InfiniteMana = Config.Bind("Cheats", "InfiniteMana", false, "Infinite Mana (mana stays full and casts are free).");
		InfiniteRange = Config.Bind("Cheats", "InfiniteRange", false, "Infinite Range (all towers have 250 range / global coverage).");
		GodMode = Config.Bind("Cheats", "GodMode", false, "God Mode (your main Tower takes zero damage).");
		InfiniteBanishes = Config.Bind("Cheats", "InfiniteBanishes", false, "Infinite card Banishes per run (when BanishCards is installed).");
		NoTowerPriceScaling = Config.Bind("Cheats", "NoTowerPriceScaling", false, "Keeps tower & building gold costs fixed at basePrice instead of increasing with each copy built.");
		DamageMultiplier = Config.Bind("Cheats", "DamageMultiplier", 1f, "Global tower base damage multiplier (1x, 2x, 5x, 10x, 100x).");
		FireRateMultiplier = Config.Bind("Cheats", "FireRateMultiplier", 1f, "Global tower fire rate (RPM) multiplier (1x, 2x, 3x, 5x).");
		IncludeUnresearchedCards = Config.Bind("Cheats", "IncludeUnresearchedCards", true, "When granting Cards or Card Trees, include cards not yet unlocked with meta XP.");

		if (LegacyCheatModeEnabled.Value)
		{
			InfiniteMoney.Value = true;
			InfiniteMana.Value = true;
			GodMode.Value = true;
			LegacyCheatModeEnabled.Value = false;
			Config.Save();
		}

		AutoSortHotbar = Config.Bind("Hotbar & Loadouts", "AutoSortHotbar", true, "Standalone fallback if RogueTowerQoL is not installed. When RogueTowerQoL is loaded, hotbar sorting uses RogueTowerQoL's order.");
		TowerHotbarOrder = Config.Bind("Hotbar & Loadouts", "TowerHotbarOrder", HotbarAndLoadoutManager.DefaultTowerOrderCsv, "Fallback Row 1 Tower order when RogueTowerQoL is not installed.");
		BuildingHotbarOrder = Config.Bind("Hotbar & Loadouts", "BuildingHotbarOrder", HotbarAndLoadoutManager.DefaultBuildingOrderCsv, "Fallback Row 2 Building order when RogueTowerQoL is not installed.");
		AutoApplyLoadoutSlot = Config.Bind("Hotbar & Loadouts", "AutoApplyLoadoutSlot", 0, "0 = Off, 1..5 = automatically grant that saved cheat loadout at the start of every run.");
		LockCardPoolToHotbar = Config.Bind("Hotbar & Loadouts", "LockCardPoolToHotbar", false, "Cheat filter: restricts random card draws so you only draw upgrades for towers/buildings currently unlocked on your hotbar.");
		LoadoutSlot1 = Config.Bind("Hotbar & Loadouts", "LoadoutSlot1", "Ballista,Mortar,FlameThrower,PoisonSprayer,FrostKeep,TeslaCoil,Shredder,Lookout|ManaSiphon,ManaBank,HauntedHouse", "Saved Loadout Slot 1 (Towers|Buildings).");
		LoadoutSlot2 = Config.Bind("Hotbar & Loadouts", "LoadoutSlot2", "Ballista,Mortar,Lookout,Radar,Obelisk,ParticleCannon|ManaSiphon,ManaBank,University", "Saved Loadout Slot 2 (Towers|Buildings).");
		LoadoutSlot3 = Config.Bind("Hotbar & Loadouts", "LoadoutSlot3", "", "Saved Loadout Slot 3 (Towers|Buildings).");
		LoadoutSlot4 = Config.Bind("Hotbar & Loadouts", "LoadoutSlot4", "", "Saved Loadout Slot 4 (Towers|Buildings).");
		LoadoutSlot5 = Config.Bind("Hotbar & Loadouts", "LoadoutSlot5", "", "Saved Loadout Slot 5 (Towers|Buildings).");

		EnableTestServer = Config.Bind("Test Harness", "EnableTestServer", true, "Enables the focusless loopback-only (127.0.0.1) HTTP test & FPS harness.");
		TestServerPort = Config.Bind("Test Harness", "TestServerPort", 18437, "Loopback port for the test harness (never 8080).");
		RunInBackgroundForTests = Config.Bind("Test Harness", "RunInBackgroundForTests", true, "Sets Application.runInBackground = true while test server is enabled so headless tests run without focus.");
		MuteAudioForTests = Config.Bind("Test Harness", "MuteAudioForTests", false, "Mutes AudioListener.volume while enabled for silent automated tests.");
		TestOutputDir = Config.Bind("Test Harness", "TestOutputDir", "", "Optional directory for test screenshots & FPS benchmark JSON reports (blank = BepInEx/TestOutput).");

		ModuleBridge.SyncBanishOverride(InfiniteBanishes.Value);

		SceneManager.activeSceneChanged += OnActiveSceneChanged;

		_harmony = new Harmony(PluginGuid);
		_harmony.PatchAll(typeof(CheatPatches));
		_harmony.PatchAll(typeof(HotbarLoadoutPatches));
		_harmony.PatchAll(typeof(InputBlockerPatches));
		DeepLagProfiler.Initialize(_harmony);

		if (MuteAudioForTests.Value || (Environment.CommandLine != null && Environment.CommandLine.IndexOf("-rt-test-mute", StringComparison.OrdinalIgnoreCase) >= 0))
		{
			AudioListener.volume = 0f;
		}

		TestHarnessServer.Initialize(this);

		Log.LogInfo($"{PluginName} / ModMenu v{PluginVersion} loaded (CleanQoL={(ModuleBridge.HasCleanQoL ? "active" : "fallback")}).");
	}

	private void OnDestroy()
	{
		TestHarnessServer.Shutdown();
		SceneManager.activeSceneChanged -= OnActiveSceneChanged;
		_harmony?.UnpatchSelf();
	}

	private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
	{
		CheatActions.OnSceneChanged(newScene.name);
		HotbarAndLoadoutManager.OnSceneChanged();
		ModMenuUI.OnSceneChanged(newScene.name);
		ModuleBridge.SyncBanishOverride(InfiniteBanishes.Value);
	}

	private void Update()
	{
		ModMenuUI.EnsureCreated();

		bool typingInSearch = ModMenuUI.IsSearchFocused;
		if (Input.GetKeyDown(MenuHotkey.Value) && (!typingInSearch || MenuHotkey.Value == KeyCode.BackQuote))
		{
			ModMenuUI.Toggle();
		}

		if (MuteAudioForTests.Value && AudioListener.volume > 0f)
		{
			AudioListener.volume = 0f;
		}

		CheatActions.Tick();
		ModMenuUI.Tick();
		BenchmarkSceneRunner.TickFrameSample();
		TestHarnessServer.Tick();
	}
}
