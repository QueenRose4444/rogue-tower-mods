using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: AssemblyCompany("RogueTowerModMenu")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyDescription("Clean-room BepInEx Mod Manager, live module settings dashboard, non-cheat FPS/lag profiler, and loopback test harness for Rogue Tower.")]
[assembly: AssemblyFileVersion("3.0.0.0")]
[assembly: AssemblyInformationalVersion("3.0.0")]
[assembly: AssemblyProduct("RogueTowerModMenu")]
[assembly: AssemblyTitle("RogueTowerModMenu")]
[assembly: AssemblyVersion("3.0.0.0")]

namespace RogueTowerModMenu;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("Rosie.RogueTowerQoL", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Rosie.RogueTowerThemes", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Rosie.RogueTowerLagFix", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Rosie.RogueTowerQuickTree", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("acr.rougetower.custommaps", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Jdewi.SpeedControls", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("AgusBut.BanishCards", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("AgusBut.UpgradesMenu", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Harb.DisableProgression", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Jdewi.DarkMode", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
	public const string PluginGuid = "Rosie.RogueTowerModMenu";
	public const string PluginName = "RogueTowerModMenu";
	public const string PluginVersion = "3.0.0";

	internal static Plugin Instance;
	internal static ManualLogSource Log;

	// Mod Menu settings
	internal static ConfigEntry<KeyCode> MenuHotkey;
	internal static ConfigEntry<bool> ShowCornerButton;
	internal static ConfigEntry<bool> PauseWhileMenuOpen;
	internal static ConfigEntry<float> WindowPosX;
	internal static ConfigEntry<float> WindowPosY;

	// Focusless Test Harness (127.0.0.1)
	internal static ConfigEntry<bool> EnableTestServer;
	internal static ConfigEntry<int> TestServerPort;
	internal static ConfigEntry<bool> RunInBackgroundForTests;
	internal static ConfigEntry<bool> MuteAudioForTests;
	internal static ConfigEntry<string> TestOutputDir;

	private Harmony _harmony;

	internal static bool IsInGameRun => GameManager.instance != null && CardManager.instance != null;

	// ---- Public Extensibility API (used by RogueTowerCheats and companion mods via reflection) ----
	public static void RegisterExternalTab(string id, string title, int order, Action<RectTransform> buildCallback)
		=> ModMenuUI.RegisterExternalTab(id, title, order, buildCallback);

	public static void UnregisterExternalTab(string id)
		=> ModMenuUI.UnregisterExternalTab(id);

	public static void RegisterHeaderBadgeProvider(Func<string> provider)
		=> ModMenuUI.RegisterHeaderBadgeProvider(provider);

	public static void RegisterHarnessCommand(string op, Func<Dictionary<string, string>, string> handler)
		=> TestHarnessServer.RegisterExternalCommand(op, handler);

	public static void RegisterBenchmarkStatusProvider(Func<string> statusJsonProvider)
		=> TestHarnessServer.RegisterBenchmarkStatusProvider(statusJsonProvider);

	public static void OpenMenuToTab(string id)
		=> ModMenuUI.OpenToTabId(id);

	public static void RefreshActiveTab()
		=> ModMenuUI.RefreshOpenTabPublic();

	public static void ShowMenuToast(string message)
		=> ModMenuUI.ShowToastPublic(message);

	public static void RegisterLiveSyncCallback(Action callback)
		=> ModMenuUI.RegisterExternalLiveSync(callback);

	public static void SetMenuOpen(bool open)
		=> ModMenuUI.SetOpen(open);

	public static bool IsMenuOpen()
		=> ModMenuUI.IsMenuOpen;

	public static void BeginProfilerSession()
		=> DeepLagProfiler.BeginSession();

	public static void RecordProfilerFrame(float unscaledDt)
		=> DeepLagProfiler.RecordRenderedFrame(unscaledDt);

	public static string EndProfilerSessionJson()
		=> DeepLagProfiler.EndSessionAndBuildJson();

	public static string GetTestOutputDirectory(string overrideDir = null)
		=> TestHarnessServer.GetOutputDir(overrideDir);

	private void Awake()
	{
		Instance = this;
		Log = Logger;

		MenuHotkey = Config.Bind("Mod Menu", "MenuHotkey", KeyCode.BackQuote, "Hotkey to open/close the Mod Menu (default: BackQuote / ` key).");
		ShowCornerButton = Config.Bind("Mod Menu", "ShowCornerButton", true, "Shows a small 'Mods' pill in the top-right corner below the Upgrades book icon.");
		PauseWhileMenuOpen = Config.Bind("Mod Menu", "PauseWhileMenuOpen", false, "Automatically pauses the game while the Mod Menu is open.");
		WindowPosX = Config.Bind("Mod Menu", "WindowPosX", 0f, "Saved Mod Menu window X offset.");
		WindowPosY = Config.Bind("Mod Menu", "WindowPosY", 0f, "Saved Mod Menu window Y offset.");

		EnableTestServer = Config.Bind("Test Harness", "EnableTestServer", true, "Enables the focusless loopback-only (127.0.0.1) HTTP test & diagnostics harness.");
		TestServerPort = Config.Bind("Test Harness", "TestServerPort", 18437, "Loopback port for the test harness (never 8080).");
		RunInBackgroundForTests = Config.Bind("Test Harness", "RunInBackgroundForTests", true, "Sets Application.runInBackground = true while test server is enabled so headless tests run without focus.");
		MuteAudioForTests = Config.Bind("Test Harness", "MuteAudioForTests", false, "Mutes AudioListener.volume while enabled for silent automated tests.");
		TestOutputDir = Config.Bind("Test Harness", "TestOutputDir", "", "Optional directory for test screenshots & reports (blank = BepInEx/TestOutput).");

		SceneManager.activeSceneChanged += OnActiveSceneChanged;

		_harmony = new Harmony(PluginGuid);
		_harmony.PatchAll(typeof(InputBlockerPatches));
		DeepLagProfiler.Initialize(_harmony);

		if (MuteAudioForTests.Value || (Environment.CommandLine != null && Environment.CommandLine.IndexOf("-rt-test-mute", StringComparison.OrdinalIgnoreCase) >= 0))
		{
			AudioListener.volume = 0f;
		}

		TestHarnessServer.Initialize(this);

		Log.LogInfo($"{PluginName} v{PluginVersion} loaded (CleanQoL={(ModuleBridge.HasCleanQoL ? "active" : "absent")}, Cheats={(ModuleBridge.HasCheatsMod ? "plugged-in" : "not-installed")}).");
	}

	private void OnDestroy()
	{
		TestHarnessServer.Shutdown();
		ModMenuUI.ClearExternalRegistrations();
		SceneManager.activeSceneChanged -= OnActiveSceneChanged;
		_harmony?.UnpatchSelf();
	}

	private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
	{
		ModMenuUI.OnSceneChanged(newScene.name);
	}

	private void Update()
	{
		ModMenuUI.EnsureCreated();

		bool typingInInput = ModMenuUI.IsTextInputFocused;
		if (Input.GetKeyDown(MenuHotkey.Value) && (!typingInInput || MenuHotkey.Value == KeyCode.BackQuote))
		{
			ModMenuUI.Toggle();
		}

		if (MuteAudioForTests.Value && AudioListener.volume > 0f)
		{
			AudioListener.volume = 0f;
		}

		ModMenuUI.Tick();
		DeepLagProfiler.TickInteractiveSample();
		TestHarnessServer.Tick();
	}
}
