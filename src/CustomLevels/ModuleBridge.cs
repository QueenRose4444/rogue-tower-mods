using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace RogueTowerCustomLevels;

internal static class ModuleBridge
{
	private const string GuidSpeedControls = "Jdewi.SpeedControls";
	private const string GuidQuickTree = "Rosie.RogueTowerQuickTree";
	private const string GuidCustomMaps = "acr.rougetower.custommaps";
	private const string GuidLevelsToRpm = "5fcf1cfc-383c-4ee8-961a-868f560452d9";
	private const string GuidLagFix = "Rosie.RogueTowerLagFix";
	private const string GuidQoL = "QoLChanges";
	private const string GuidBanish = "AgusBut.BanishCards";
	private const string GuidDarkMode = "Jdewi.DarkMode";
	private const string GuidThemes = "Rosie.RogueTowerThemes";
	private const string GuidCleanQoL = "Rosie.RogueTowerQoL";
	private const string GuidDisableProg = "Harb.DisableProgression";
	private const string GuidUpgradesMenu = "AgusBut.UpgradesMenu";

	internal static bool IsLoaded(string guid)
	{
		return Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(guid);
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
	internal static bool HasSpeedControls => IsLoaded(GuidSpeedControls);

	internal static float GetSpeed()
	{
		return InvokeStatic(GuidSpeedControls, "GetEffectiveSpeed", Time.timeScale);
	}

	internal static void SetSpeed(float multiplier)
	{
		if (!InvokeStaticVoid(GuidSpeedControls, "SetCustomSpeed", multiplier))
		{
			Time.timeScale = Mathf.Max(0f, multiplier);
		}
	}

	// ---- QuickTree ----
	internal static bool HasQuickTree => IsLoaded(GuidQuickTree);

	internal static int GetAutoMaxMode() => InvokeStatic(GuidQuickTree, "GetAutoMaxMode", 0);
	internal static void SetAutoMaxMode(int mode) => InvokeStaticVoid(GuidQuickTree, "SetAutoMaxMode", mode);

	// ---- CustomMaps ----
	internal static bool HasCustomMaps => IsLoaded(GuidCustomMaps);

	internal static string[] GetMapPresetNames() => InvokeStatic(GuidCustomMaps, "GetPresetNames", new[] { "Vanilla (Random)" });
	internal static int GetSelectedMapIndex() => InvokeStatic(GuidCustomMaps, "GetSelectedMapIndex", 0);
	internal static void SetSelectedMapIndex(int idx) => InvokeStaticVoid(GuidCustomMaps, "SetSelectedMapIndex", idx);
	internal static string GetActiveRunMapName() => InvokeStatic(GuidCustomMaps, "GetActiveRunMapName", "Vanilla (Random)");
	internal static bool OpenCustomMapEditor() => InvokeStaticVoid(GuidCustomMaps, "OpenCustomMapEditor");

	// ---- LevelsToRpm ----
	internal static bool HasLevelsToRpm => IsLoaded(GuidLevelsToRpm);
	internal static void RefreshLevelsToRpmTowers() => InvokeStaticVoid(GuidLevelsToRpm, "RefreshAllTowers");

	// ---- DarkMode / RogueTowerThemes ----
	internal static bool HasDarkMode => IsLoaded(GuidDarkMode);
	internal static bool HasThemes => IsLoaded(GuidThemes);

	internal static void ApplyDarkMode() => InvokeStaticVoid(GuidDarkMode, "ApplyCurrentState");

	internal static int GetThemeMode()
	{
		if (HasThemes) return InvokeStatic(GuidThemes, "GetModeInt", 0);
		return HasDarkMode && TryGetConfig(GuidDarkMode, "General", "Enabled", out bool dmOn) && dmOn ? 1 : 0;
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
		if (InvokeStaticVoid(GuidThemes, "SetModeInt", mode)) return;
		if (HasDarkMode)
		{
			SetConfig(GuidDarkMode, "General", "Enabled", mode == 1);
			ApplyDarkMode();
		}
	}

	internal static string ToggleLightDark()
	{
		if (InvokeStaticVoid(GuidThemes, "ToggleLightDark")) return GetThemeModeName();
		if (HasDarkMode && TryGetConfig(GuidDarkMode, "General", "Enabled", out bool dmOn))
		{
			SetConfig(GuidDarkMode, "General", "Enabled", !dmOn);
			ApplyDarkMode();
			return !dmOn ? "Dark" : "Vanilla";
		}
		return "Vanilla";
	}

	internal static void ApplyTheme() => InvokeStaticVoid(GuidThemes, "ApplyTheme");

	// ---- Clean-Room QoL (RogueTowerQoL v2.0.0+) ----
	internal static bool HasCleanQoL => IsLoaded(GuidCleanQoL);

	internal static bool GetQoLAutoSortHotbar() => InvokeStatic(GuidCleanQoL, "GetAutoSortHotbar", Plugin.AutoSortHotbar.Value);
	internal static void SetQoLAutoSortHotbar(bool value)
	{
		if (!InvokeStaticVoid(GuidCleanQoL, "SetAutoSortHotbar", value))
		{
			Plugin.AutoSortHotbar.Value = value;
		}
	}
	internal static string GetQoLTowerHotbarOrderCsv(string fallback) => InvokeStatic(GuidCleanQoL, "GetTowerHotbarOrderCsv", fallback);
	internal static string GetQoLBuildingHotbarOrderCsv(string fallback) => InvokeStatic(GuidCleanQoL, "GetBuildingHotbarOrderCsv", fallback);
	internal static bool MoveQoLHotbarItem(string id, int delta) => InvokeStaticVoid(GuidCleanQoL, "MoveHotbarItem", id, delta);
	internal static bool ResetQoLHotbarOrder() => InvokeStaticVoid(GuidCleanQoL, "ResetHotbarOrder");
	internal static bool ForceRefreshQoLHotbar() => InvokeStaticVoid(GuidCleanQoL, "ForceRefreshHotbar");
	internal static bool SetQoLHotbarSortOpen(bool open) => InvokeStaticVoid(GuidCleanQoL, "SetHotbarSortOpen", open);

	internal static bool RotateCameraBy(float deg) => InvokeStaticVoid(GuidCleanQoL, "RotateCameraBy", deg);
	internal static bool ToggleTopDown() => InvokeStaticVoid(GuidCleanQoL, "ToggleTopDown");
	internal static bool ResetCameraView() => InvokeStaticVoid(GuidCleanQoL, "ResetCameraView");
	internal static float GetCameraYaw() => InvokeStatic(GuidCleanQoL, "GetCameraYaw", 0f);
	internal static bool IsTopDownActive() => InvokeStatic(GuidCleanQoL, "IsTopDownActive", false);

	internal static int OpenAllChests() => InvokeStatic(GuidCleanQoL, "OpenAllChests", -1);
	internal static int ChopAllTrees() => InvokeStatic(GuidCleanQoL, "ChopAllTrees", -1);
	internal static int GetQoLChestModeInt() => InvokeStatic(GuidCleanQoL, "GetChestModeInt", 1);
	internal static void SetQoLChestModeInt(int mode) => InvokeStaticVoid(GuidCleanQoL, "SetChestModeInt", mode);
	internal static void RefreshQoLTowers() => InvokeStaticVoid(GuidCleanQoL, "RefreshAllTowers");

	// ---- BanishCards ----
	internal static bool HasBanishCards => IsLoaded(GuidBanish);

	internal static void SyncBanishOverride(bool infinite)
	{
		BaseUnityPlugin p = GetPlugin(GuidBanish);
		if (p == null)
		{
			return;
		}
		FieldInfo f = p.GetType().GetField("InfiniteBanishesOverride", BindingFlags.Public | BindingFlags.Static);
		if (f != null)
		{
			f.SetValue(null, infinite);
		}
		MethodInfo m = p.GetType().GetMethod("RefreshActiveButtons", BindingFlags.Public | BindingFlags.Static);
		m?.Invoke(null, null);
	}

	// ---- UpgradesMenu ----
	internal static bool HasUpgradesMenu => IsLoaded(GuidUpgradesMenu);

	internal static void NotifyUpgradesMenuCardUnlocked(string unlockName, string title, bool isMonster)
	{
		BaseUnityPlugin p = GetPlugin(GuidUpgradesMenu);
		if (p == null)
		{
			return;
		}
		Type t = p.GetType().Assembly.GetType("UpgradesMenu.StateManagement.Runtime.ExternalCardSync");
		if (t == null)
		{
			return;
		}
		if (!string.IsNullOrEmpty(unlockName))
		{
			MethodInfo m = t.GetMethod("MarkCardUnlocked", BindingFlags.Public | BindingFlags.Static);
			m?.Invoke(null, new object[] { unlockName, isMonster });
		}
		else if (!string.IsNullOrEmpty(title))
		{
			MethodInfo m = t.GetMethod("MarkCardUnlockedByTitle", BindingFlags.Public | BindingFlags.Static);
			m?.Invoke(null, new object[] { title, isMonster });
		}
	}

	// ---- Config Key Constants for UI ----
	internal static class Keys
	{
		public const string SpeedControls = GuidSpeedControls;
		public const string QuickTree = GuidQuickTree;
		public const string CustomMaps = GuidCustomMaps;
		public const string LevelsToRpm = GuidLevelsToRpm;
		public const string LagFix = GuidLagFix;
		public const string QoL = GuidQoL;
		public const string Banish = GuidBanish;
		public const string DarkMode = GuidDarkMode;
		public const string Themes = GuidThemes;
		public const string CleanQoL = GuidCleanQoL;
		public const string DisableProg = GuidDisableProg;
		public const string UpgradesMenu = GuidUpgradesMenu;
	}
}
