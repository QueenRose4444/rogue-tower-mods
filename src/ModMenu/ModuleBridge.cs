using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace RogueTowerModMenu;

internal static class ModuleBridge
{
	internal static class Keys
	{
		public const string ModMenu = "Rosie.RogueTowerModMenu";
		public const string Cheats = "Rosie.RogueTowerCheats";
		public const string SpeedControls = "Jdewi.SpeedControls";
		public const string QuickTree = "Rosie.RogueTowerQuickTree";
		public const string CustomMaps = "acr.rougetower.custommaps";
		public const string LevelsToRpm = "5fcf1cfc-383c-4ee8-961a-868f560452d9";
		public const string LagFix = "Rosie.RogueTowerLagFix";
		public const string QoL = "QoLChanges";
		public const string Banish = "AgusBut.BanishCards";
		public const string DarkMode = "Jdewi.DarkMode";
		public const string Themes = "Rosie.RogueTowerThemes";
		public const string CleanQoL = "Rosie.RogueTowerQoL";
		public const string DisableProg = "Harb.DisableProgression";
		public const string UpgradesMenu = "AgusBut.UpgradesMenu";
	}

	private static readonly HashSet<string> SuiteGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		Keys.ModMenu,
		Keys.Cheats,
		Keys.LagFix,
		Keys.QuickTree,
		Keys.CleanQoL,
		Keys.Themes,
		Keys.CustomMaps
	};

	internal static readonly HashSet<string> DedicatedSettingsGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		Keys.ModMenu,
		Keys.SpeedControls,
		Keys.QuickTree,
		Keys.CustomMaps,
		Keys.LevelsToRpm,
		Keys.LagFix,
		Keys.QoL,
		Keys.Banish,
		Keys.DarkMode,
		Keys.Themes,
		Keys.CleanQoL,
		Keys.DisableProg
	};

	internal static bool IsSuitePlugin(string guid) => !string.IsNullOrEmpty(guid) && SuiteGuids.Contains(guid);

	internal static bool IsLoaded(string guid)
		=> Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(guid);

	internal static List<PluginInfo> GetLoadedPluginsSorted()
	{
		List<PluginInfo> list = new List<PluginInfo>();
		if (Chainloader.PluginInfos == null) return list;
		foreach (PluginInfo pi in Chainloader.PluginInfos.Values)
		{
			if (pi != null) list.Add(pi);
		}
		list.Sort((a, b) =>
		{
			bool aSuite = IsSuitePlugin(a.Metadata.GUID);
			bool bSuite = IsSuitePlugin(b.Metadata.GUID);
			if (aSuite != bSuite) return aSuite ? -1 : 1;
			return string.Compare(a.Metadata.Name, b.Metadata.Name, StringComparison.OrdinalIgnoreCase);
		});
		return list;
	}

	private static BaseUnityPlugin GetPlugin(string guid)
	{
		if (Chainloader.PluginInfos != null && Chainloader.PluginInfos.TryGetValue(guid, out var info))
		{
			return info.Instance;
		}
		return null;
	}

	private static T InvokeStatic<T>(string guid, string methodName, T fallback, params object[] args)
	{
		BaseUnityPlugin p = GetPlugin(guid);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
			if (m != null && m.Invoke(null, args) is T val)
			{
				return val;
			}
		}
		return fallback;
	}

	private static bool InvokeStaticVoid(string guid, string methodName, params object[] args)
	{
		BaseUnityPlugin p = GetPlugin(guid);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
			if (m != null)
			{
				m.Invoke(null, args);
				return true;
			}
		}
		return false;
	}

	internal static bool TryGetConfig<T>(string guid, string section, string key, out T value)
	{
		value = default;
		BaseUnityPlugin p = GetPlugin(guid);
		if (p == null || p.Config == null)
		{
			return false;
		}
		var def = new ConfigDefinition(section, key);
		if (p.Config.TryGetEntry<T>(def, out var entry))
		{
			value = entry.Value;
			return true;
		}
		return false;
	}

	internal static bool SetConfig<T>(string guid, string section, string key, T value)
	{
		BaseUnityPlugin p = GetPlugin(guid);
		if (p == null || p.Config == null)
		{
			return false;
		}
		var def = new ConfigDefinition(section, key);
		if (p.Config.TryGetEntry<T>(def, out var entry))
		{
			entry.Value = value;
			p.Config.Save();
			return true;
		}
		return false;
	}

	// ---- SpeedControls ----
	internal static bool HasSpeedControls => IsLoaded(Keys.SpeedControls);
	internal static bool HasCheatsMod => IsLoaded(Keys.Cheats);

	internal static float GetSpeed() => InvokeStatic(Keys.SpeedControls, "GetEffectiveSpeed", Time.timeScale);

	internal static void SetSpeed(float multiplier)
	{
		if (!InvokeStaticVoid(Keys.SpeedControls, "SetCustomSpeed", multiplier))
		{
			Time.timeScale = Mathf.Max(0f, multiplier);
		}
	}

	// ---- QuickTree ----
	internal static bool HasQuickTree => IsLoaded(Keys.QuickTree);
	internal static int GetAutoMaxMode() => InvokeStatic(Keys.QuickTree, "GetAutoMaxMode", 0);
	internal static void SetAutoMaxMode(int mode) => InvokeStaticVoid(Keys.QuickTree, "SetAutoMaxMode", mode);

	// ---- CustomMaps ----
	internal static bool HasCustomMaps => IsLoaded(Keys.CustomMaps);
	internal static string[] GetMapPresetNames() => InvokeStatic(Keys.CustomMaps, "GetPresetNames", new[] { "Vanilla (Random)" });
	internal static int GetSelectedMapIndex() => InvokeStatic(Keys.CustomMaps, "GetSelectedMapIndex", 0);
	internal static void SetSelectedMapIndex(int idx) => InvokeStaticVoid(Keys.CustomMaps, "SetSelectedMapIndex", idx);
	internal static string GetActiveRunMapName() => InvokeStatic(Keys.CustomMaps, "GetActiveRunMapName", "Vanilla (Random)");
	internal static bool OpenCustomMapEditor() => InvokeStaticVoid(Keys.CustomMaps, "OpenCustomMapEditor");

	// ---- LevelsToRpm ----
	internal static bool HasLevelsToRpm => IsLoaded(Keys.LevelsToRpm);
	internal static void RefreshLevelsToRpmTowers() => InvokeStaticVoid(Keys.LevelsToRpm, "RefreshAllTowers");

	// ---- DarkMode / RogueTowerThemes ----
	internal static bool HasDarkMode => IsLoaded(Keys.DarkMode);
	internal static bool HasThemes => IsLoaded(Keys.Themes);
	internal static void ApplyDarkMode() => InvokeStaticVoid(Keys.DarkMode, "ApplyCurrentState");
	internal static void ApplyTheme() => InvokeStaticVoid(Keys.Themes, "ApplyTheme");

	internal static int GetThemeMode()
	{
		if (HasThemes) return InvokeStatic(Keys.Themes, "GetModeInt", 0);
		return HasDarkMode && TryGetConfig(Keys.DarkMode, "General", "Enabled", out bool dmOn) && dmOn ? 1 : 0;
	}

	internal static string GetThemeModeName()
	{
		return GetThemeMode() switch
		{
			1 => "Dark",
			2 => "Light",
			_ => "Vanilla"
		};
	}

	internal static void SetThemeMode(int mode)
	{
		if (InvokeStaticVoid(Keys.Themes, "SetModeInt", mode)) return;
		if (HasDarkMode)
		{
			SetConfig(Keys.DarkMode, "General", "Enabled", mode == 1);
			ApplyDarkMode();
		}
	}

	internal static string ToggleLightDark()
	{
		if (InvokeStaticVoid(Keys.Themes, "ToggleLightDark")) return GetThemeModeName();
		if (HasDarkMode && TryGetConfig(Keys.DarkMode, "General", "Enabled", out bool dmOn))
		{
			SetConfig(Keys.DarkMode, "General", "Enabled", !dmOn);
			ApplyDarkMode();
			return !dmOn ? "Dark" : "Vanilla";
		}
		return "Vanilla";
	}

	// ---- Clean-Room QoL (RogueTowerQoL) ----
	internal static bool HasCleanQoL => IsLoaded(Keys.CleanQoL);
	internal static bool MoveQoLHotbarItemToIndex(string id, int targetIndex, bool amongAllSlots)
		=> InvokeStatic(Keys.CleanQoL, "MoveHotbarItemToIndex", false, id, targetIndex, amongAllSlots);
	internal static bool SaveQoLHotbarPreset(int slot) => InvokeStaticVoid(Keys.CleanQoL, "SaveHotbarPreset", slot);
	internal static bool LoadQoLHotbarPreset(int slot) => InvokeStatic(Keys.CleanQoL, "LoadHotbarPreset", false, slot);
	internal static bool ForceRefreshQoLHotbar() => InvokeStaticVoid(Keys.CleanQoL, "ForceRefreshHotbar");
	internal static bool SetQoLHotbarSortOpen(bool open) => InvokeStaticVoid(Keys.CleanQoL, "SetHotbarSortOpen", open);
	internal static bool RotateCameraBy(float deg) => InvokeStaticVoid(Keys.CleanQoL, "RotateCameraBy", deg);
	internal static bool ToggleTopDown() => InvokeStaticVoid(Keys.CleanQoL, "ToggleTopDown");
	internal static bool ResetCameraView() => InvokeStaticVoid(Keys.CleanQoL, "ResetCameraView");
	internal static float GetCameraYaw() => InvokeStatic(Keys.CleanQoL, "GetCameraYaw", 0f);
	internal static bool IsTopDownActive() => InvokeStatic(Keys.CleanQoL, "IsTopDownActive", false);
	internal static int GetQoLChestModeInt() => InvokeStatic(Keys.CleanQoL, "GetChestModeInt", 1);
	internal static void SetQoLChestModeInt(int mode) => InvokeStaticVoid(Keys.CleanQoL, "SetChestModeInt", mode);
	internal static void RefreshQoLTowers() => InvokeStaticVoid(Keys.CleanQoL, "RefreshAllTowers");
	internal static List<string> GetQoLActiveHotbarIds(bool isTower)
		=> InvokeStatic<List<string>>(Keys.CleanQoL, "GetActiveHotbarIds", new List<string>(), isTower);

	// ---- BanishCards ----
	internal static bool HasBanishCards => IsLoaded(Keys.Banish);
}
