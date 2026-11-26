using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace RogueTowerCheats;

internal static class CheatBridge
{
	internal static class Keys
	{
		public const string ModMenu = "Rosie.RogueTowerModMenu";
		public const string SpeedControls = "Jdewi.SpeedControls";
		public const string CleanQoL = "Rosie.RogueTowerQoL";
		public const string LagFix = "Rosie.RogueTowerLagFix";
		public const string Themes = "Rosie.RogueTowerThemes";
		public const string Banish = "AgusBut.BanishCards";
		public const string UpgradesMenu = "AgusBut.UpgradesMenu";
	}

	internal static bool IsLoaded(string guid)
		=> Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(guid);

	internal static bool HasModMenu => IsLoaded(Keys.ModMenu);
	internal static bool HasCleanQoL => IsLoaded(Keys.CleanQoL);
	internal static bool HasSpeedControls => IsLoaded(Keys.SpeedControls);
	internal static bool HasThemes => IsLoaded(Keys.Themes);
	internal static bool HasBanishCards => IsLoaded(Keys.Banish);
	internal static bool HasUpgradesMenu => IsLoaded(Keys.UpgradesMenu);

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

	// ---- ModMenu Extensibility ----
	internal static bool RegisterTab(string id, string title, int order, Action<RectTransform> buildCallback)
		=> InvokeStaticVoid(Keys.ModMenu, "RegisterExternalTab", id, title, order, buildCallback);

	internal static bool UnregisterTab(string id)
		=> InvokeStaticVoid(Keys.ModMenu, "UnregisterExternalTab", id);

	internal static bool RegisterHeaderBadge(Func<string> provider)
		=> InvokeStaticVoid(Keys.ModMenu, "RegisterHeaderBadgeProvider", provider);

	internal static bool RegisterHarnessCommand(string op, Func<Dictionary<string, string>, string> handler)
		=> InvokeStaticVoid(Keys.ModMenu, "RegisterHarnessCommand", op, handler);

	internal static bool RegisterBenchmarkStatusProvider(Func<string> provider)
		=> InvokeStaticVoid(Keys.ModMenu, "RegisterBenchmarkStatusProvider", provider);

	internal static bool OpenMenuToTab(string id)
		=> InvokeStaticVoid(Keys.ModMenu, "OpenMenuToTab", id);

	internal static bool IsModMenuOpen()
		=> InvokeStatic(Keys.ModMenu, "IsMenuOpen", false);

	internal static void SetModMenuOpen(bool open)
		=> InvokeStaticVoid(Keys.ModMenu, "SetMenuOpen", open);

	internal static void RefreshModMenuTab()
		=> InvokeStaticVoid(Keys.ModMenu, "RefreshActiveTab");

	internal static void ShowModMenuToast(string msg)
		=> InvokeStaticVoid(Keys.ModMenu, "ShowMenuToast", msg);

	internal static void RegisterModMenuLiveSync(Action cb)
		=> InvokeStaticVoid(Keys.ModMenu, "RegisterLiveSyncCallback", cb);

	internal static void BeginProfilerSession()
		=> InvokeStaticVoid(Keys.ModMenu, "BeginProfilerSession");

	internal static void RecordProfilerFrame(float dt)
		=> InvokeStaticVoid(Keys.ModMenu, "RecordProfilerFrame", dt);

	internal static string EndProfilerSessionJson()
		=> InvokeStatic<string>(Keys.ModMenu, "EndProfilerSessionJson", "null");

	internal static string GetTestOutputDir(string overrideDir = null)
		=> InvokeStatic(Keys.ModMenu, "GetTestOutputDirectory", System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "TestOutput"), overrideDir);

	// ---- Speed / Themes / QoL ----
	internal static float GetSpeed() => InvokeStatic(Keys.SpeedControls, "GetEffectiveSpeed", Time.timeScale);

	internal static void SetSpeed(float multiplier)
	{
		if (!InvokeStaticVoid(Keys.SpeedControls, "SetCustomSpeed", multiplier))
		{
			Time.timeScale = Mathf.Max(0f, multiplier);
		}
	}

	internal static string GetThemeModeName()
	{
		if (!HasThemes) return "Vanilla";
		int m = InvokeStatic(Keys.Themes, "GetModeInt", 0);
		return m switch { 1 => "Dark", 2 => "Light", _ => "Vanilla" };
	}

	internal static bool GetQoLAutoSortHotbar() => InvokeStatic(Keys.CleanQoL, "GetAutoSortHotbar", Plugin.AutoSortHotbar.Value);
	internal static void SetQoLAutoSortHotbar(bool value)
	{
		if (!InvokeStaticVoid(Keys.CleanQoL, "SetAutoSortHotbar", value))
		{
			Plugin.AutoSortHotbar.Value = value;
		}
	}
	internal static string GetQoLTowerHotbarOrderCsv(string fallback) => InvokeStatic(Keys.CleanQoL, "GetTowerHotbarOrderCsv", fallback);
	internal static string GetQoLBuildingHotbarOrderCsv(string fallback) => InvokeStatic(Keys.CleanQoL, "GetBuildingHotbarOrderCsv", fallback);
	internal static bool MoveQoLHotbarItem(string id, int delta) => InvokeStaticVoid(Keys.CleanQoL, "MoveHotbarItem", id, delta);
	internal static bool ResetQoLHotbarOrder() => InvokeStaticVoid(Keys.CleanQoL, "ResetHotbarOrder");
	internal static bool ForceRefreshQoLHotbar() => InvokeStaticVoid(Keys.CleanQoL, "ForceRefreshHotbar");
	internal static int OpenAllChests() => InvokeStatic(Keys.CleanQoL, "OpenAllChests", -1);
	internal static int ChopAllTrees() => InvokeStatic(Keys.CleanQoL, "ChopAllTrees", -1);

	// ---- BanishCards & UpgradesMenu ----
	internal static void SyncBanishOverride(bool infinite)
	{
		BaseUnityPlugin p = GetPlugin(Keys.Banish);
		if (p == null) return;
		FieldInfo f = p.GetType().GetField("InfiniteBanishesOverride", BindingFlags.Public | BindingFlags.Static);
		if (f != null) f.SetValue(null, infinite);
		MethodInfo m = p.GetType().GetMethod("RefreshActiveButtons", BindingFlags.Public | BindingFlags.Static);
		m?.Invoke(null, null);
	}

	internal static void NotifyUpgradesMenuCardUnlocked(string unlockName, string title, bool isMonster)
	{
		BaseUnityPlugin p = GetPlugin(Keys.UpgradesMenu);
		if (p == null) return;
		Type t = p.GetType().Assembly.GetType("UpgradesMenu.StateManagement.Runtime.ExternalCardSync");
		if (t == null) return;
		if (!string.IsNullOrEmpty(unlockName))
		{
			t.GetMethod("MarkCardUnlocked", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { unlockName, isMonster });
		}
		else if (!string.IsNullOrEmpty(title))
		{
			t.GetMethod("MarkCardUnlockedByTitle", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { title, isMonster });
		}
	}
}
