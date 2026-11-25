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
		BaseUnityPlugin p = GetPlugin(GuidSpeedControls);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("GetEffectiveSpeed", BindingFlags.Public | BindingFlags.Static);
			if (m != null && m.Invoke(null, null) is float f)
			{
				return f;
			}
		}
		return Time.timeScale;
	}

	internal static void SetSpeed(float multiplier)
	{
		BaseUnityPlugin p = GetPlugin(GuidSpeedControls);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("SetCustomSpeed", BindingFlags.Public | BindingFlags.Static);
			if (m != null)
			{
				m.Invoke(null, new object[] { multiplier });
				return;
			}
		}
		Time.timeScale = Mathf.Max(0f, multiplier);
	}

	// ---- QuickTree ----
	internal static bool HasQuickTree => IsLoaded(GuidQuickTree);

	internal static int GetAutoMaxMode()
	{
		BaseUnityPlugin p = GetPlugin(GuidQuickTree);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("GetAutoMaxMode", BindingFlags.Public | BindingFlags.Static);
			if (m != null && m.Invoke(null, null) is int val)
			{
				return val;
			}
		}
		return 0;
	}

	internal static void SetAutoMaxMode(int mode)
	{
		BaseUnityPlugin p = GetPlugin(GuidQuickTree);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("SetAutoMaxMode", BindingFlags.Public | BindingFlags.Static);
			m?.Invoke(null, new object[] { mode });
		}
	}

	// ---- CustomMaps ----
	internal static bool HasCustomMaps => IsLoaded(GuidCustomMaps);

	internal static string[] GetMapPresetNames()
	{
		BaseUnityPlugin p = GetPlugin(GuidCustomMaps);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("GetPresetNames", BindingFlags.Public | BindingFlags.Static);
			if (m != null && m.Invoke(null, null) is string[] arr)
			{
				return arr;
			}
		}
		return new[] { "Vanilla (Random)" };
	}

	internal static int GetSelectedMapIndex()
	{
		BaseUnityPlugin p = GetPlugin(GuidCustomMaps);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("GetSelectedMapIndex", BindingFlags.Public | BindingFlags.Static);
			if (m != null && m.Invoke(null, null) is int idx)
			{
				return idx;
			}
		}
		return 0;
	}

	internal static void SetSelectedMapIndex(int idx)
	{
		BaseUnityPlugin p = GetPlugin(GuidCustomMaps);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("SetSelectedMapIndex", BindingFlags.Public | BindingFlags.Static);
			m?.Invoke(null, new object[] { idx });
		}
	}

	internal static string GetActiveRunMapName()
	{
		BaseUnityPlugin p = GetPlugin(GuidCustomMaps);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("GetActiveRunMapName", BindingFlags.Public | BindingFlags.Static);
			if (m != null && m.Invoke(null, null) is string name)
			{
				return name;
			}
		}
		return "Vanilla (Random)";
	}

	// ---- LevelsToRpm ----
	internal static bool HasLevelsToRpm => IsLoaded(GuidLevelsToRpm);

	internal static void RefreshLevelsToRpmTowers()
	{
		BaseUnityPlugin p = GetPlugin(GuidLevelsToRpm);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("RefreshAllTowers", BindingFlags.Public | BindingFlags.Static);
			m?.Invoke(null, null);
		}
	}

	// ---- DarkMode / RogueTowerThemes ----
	internal static bool HasDarkMode => IsLoaded(GuidDarkMode);
	internal static bool HasThemes => IsLoaded(GuidThemes);
	internal static bool HasCleanQoL => IsLoaded(GuidCleanQoL);

	internal static void ApplyDarkMode()
	{
		BaseUnityPlugin p = GetPlugin(GuidDarkMode);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("ApplyCurrentState", BindingFlags.Public | BindingFlags.Static);
			m?.Invoke(null, null);
		}
	}

	internal static int GetThemeMode()
	{
		BaseUnityPlugin p = GetPlugin(GuidThemes);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("GetModeInt", BindingFlags.Public | BindingFlags.Static);
			if (m != null && m.Invoke(null, null) is int val) return val;
		}
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
		BaseUnityPlugin p = GetPlugin(GuidThemes);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("SetModeInt", BindingFlags.Public | BindingFlags.Static);
			if (m != null)
			{
				m.Invoke(null, new object[] { mode });
				return;
			}
		}
		if (HasDarkMode)
		{
			SetConfig(GuidDarkMode, "General", "Enabled", mode == 1);
			ApplyDarkMode();
		}
	}

	internal static string ToggleLightDark()
	{
		BaseUnityPlugin p = GetPlugin(GuidThemes);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("ToggleLightDark", BindingFlags.Public | BindingFlags.Static);
			m?.Invoke(null, null);
			return GetThemeModeName();
		}
		if (HasDarkMode && TryGetConfig(GuidDarkMode, "General", "Enabled", out bool dmOn))
		{
			SetConfig(GuidDarkMode, "General", "Enabled", !dmOn);
			ApplyDarkMode();
			return !dmOn ? "Dark" : "Vanilla";
		}
		return "Vanilla";
	}

	internal static void ApplyTheme()
	{
		BaseUnityPlugin p = GetPlugin(GuidThemes);
		if (p != null)
		{
			MethodInfo m = p.GetType().GetMethod("ApplyTheme", BindingFlags.Public | BindingFlags.Static);
			m?.Invoke(null, null);
		}
	}

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
