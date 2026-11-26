using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[assembly: AssemblyCompany("RogueTowerThemes")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyDescription("Clean-room customizable Light & Dark themes with non-destructive original state caching and instant hotkey / Mod Menu switching for Rogue Tower.")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]
[assembly: AssemblyProduct("RogueTowerThemes")]
[assembly: AssemblyTitle("RogueTowerThemes")]
[assembly: AssemblyVersion("1.1.0.0")]

namespace RogueTowerThemes;

public enum ThemeMode
{
	Vanilla = 0,
	Dark = 1,
	Light = 2
}

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("Jdewi.DarkMode", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
	public const string PluginGuid = "Rosie.RogueTowerThemes";
	public const string PluginName = "RogueTowerThemes";
	public const string PluginVersion = "1.1.0";

	internal static ManualLogSource Log;

	public static ConfigEntry<int> ConfigSchemaVersion;
	public static ConfigEntry<ThemeMode> ActiveMode;
	public static ConfigEntry<KeyCode> QuickToggleKey;
	public static ConfigEntry<bool> CycleIncludesVanilla;
	public static ConfigEntry<bool> OverrideAmbient;

	// Dark preset
	public static ConfigEntry<string> DarkSkyHex;
	public static ConfigEntry<float> DarkSunMultiplier;
	public static ConfigEntry<string> DarkAmbientHex;
	public static ConfigEntry<string> DarkSunHex;
	public static ConfigEntry<string> DarkFillHex;
	public static ConfigEntry<float> DarkUiBrightness;
	public static ConfigEntry<bool> DarkHighContrastText;

	// Light preset
	public static ConfigEntry<string> LightSkyHex;
	public static ConfigEntry<float> LightSunMultiplier;
	public static ConfigEntry<string> LightAmbientHex;
	public static ConfigEntry<string> LightSunHex;
	public static ConfigEntry<string> LightFillHex;
	public static ConfigEntry<float> LightUiBrightness;
	public static ConfigEntry<bool> LightHighContrastText;

	private static readonly Color DefaultCameraSky = new Color(0.192f, 0.302f, 0.475f, 1f);

	private struct LightSnapshot
	{
		public Light Ref;
		public float Intensity;
		public Color Color;
	}

	private struct ImageSnapshot
	{
		public Image Ref;
		public Color Color;
	}

	private struct TextSnapshot
	{
		public Text Ref;
		public Color Color;
	}

	private static Color _origCamBg = DefaultCameraSky;
	private static Color _origAmbient = new Color(0.212f, 0.227f, 0.259f, 1f);
	private static float _origAmbientIntensity = 1f;
	private static bool _hasCamBg;
	private static readonly Dictionary<int, LightSnapshot> OrigLights = new Dictionary<int, LightSnapshot>(8);
	private static readonly Dictionary<int, ImageSnapshot> OrigImages = new Dictionary<int, ImageSnapshot>(256);
	private static readonly Dictionary<int, TextSnapshot> OrigTexts = new Dictionary<int, TextSnapshot>(64);
	private static bool _hasHudTexts;

	private void Awake()
	{
		Log = Logger;

		ConfigSchemaVersion = Config.Bind("1. General", "ConfigSchemaVersion", 1, "Internal preset schema version used to migrate stale lighting defaults cleanly.");
		ActiveMode = Config.Bind("1. General", "ActiveMode", ThemeMode.Dark, "Current visual theme: Vanilla, Dark, or Light.");
		QuickToggleKey = Config.Bind("1. General", "QuickToggleKey", KeyCode.F7, "Hotkey to quickly toggle Light <-> Dark (or cycle modes).");
		CycleIncludesVanilla = Config.Bind("1. General", "CycleIncludesVanilla", false, "If false, hotkey switches directly Dark <-> Light. If true, cycles Vanilla -> Dark -> Light.");
		OverrideAmbient = Config.Bind("1. General", "OverrideAmbient", false, "If false (default), preserves Rogue Tower's vanilla ambient lighting so toon-shaded stone/roof shadows stay warm and readable without black crush or white road clipping.");

		DarkSkyHex = Config.Bind("2. Dark Preset", "DarkSkyHex", "#0B1020", "Sky/camera clear color in Dark mode (#RRGGBB).");
		DarkSunMultiplier = Config.Bind("2. Dark Preset", "DarkSunMultiplier", 0.84f, "Primary directional sun intensity multiplier in Dark mode (1.0 = vanilla).");
		DarkAmbientHex = Config.Bind("2. Dark Preset", "DarkAmbientHex", "#1A2238", "Scene ambient tint in Dark mode (#RRGGBB) when OverrideAmbient = true.");
		DarkSunHex = Config.Bind("2. Dark Preset", "DarkSunHex", "#C8D4FF", "Main directional light tint in Dark mode.");
		DarkFillHex = Config.Bind("2. Dark Preset", "DarkFillHex", "#DFC8EC", "Secondary fill light tint in Dark mode.");
		DarkUiBrightness = Config.Bind("2. Dark Preset", "DarkUiBrightness", 0.90f, "UI panel brightness multiplier (0.5 - 1.2) applied non-destructively in Dark mode.");
		DarkHighContrastText = Config.Bind("2. Dark Preset", "DarkHighContrastText", true, "Use crisp white modifier/HUD text in Dark mode.");

		LightSkyHex = Config.Bind("3. Light Preset", "LightSkyHex", "#DCEBFF", "Sky/camera clear color in Light mode (#RRGGBB).");
		LightSunMultiplier = Config.Bind("3. Light Preset", "LightSunMultiplier", 0.98f, "Primary directional sun intensity multiplier in Light mode (capped <= 1.0 by default so toon steps stay clean).");
		LightAmbientHex = Config.Bind("3. Light Preset", "LightAmbientHex", "#6D7A8E", "Scene ambient tint in Light mode (#RRGGBB) when OverrideAmbient = true.");
		LightSunHex = Config.Bind("3. Light Preset", "LightSunHex", "#FFF9EC", "Main directional light tint in Light mode.");
		LightFillHex = Config.Bind("3. Light Preset", "LightFillHex", "#D8ECFF", "Secondary fill light tint in Light mode.");
		LightUiBrightness = Config.Bind("3. Light Preset", "LightUiBrightness", 1.00f, "UI panel brightness multiplier (0.5 - 1.2) applied non-destructively in Light mode.");
		LightHighContrastText = Config.Bind("3. Light Preset", "LightHighContrastText", false, "Keep dark readable HUD text in Light mode (true forces white text).");

		if (ConfigSchemaVersion.Value < 2)
		{
			DarkSunMultiplier.Value = 0.84f;
			DarkSunHex.Value = "#C8D4FF";
			DarkFillHex.Value = "#DFC8EC";
			DarkUiBrightness.Value = 0.90f;
			LightSunMultiplier.Value = 0.98f;
			LightUiBrightness.Value = 1.00f;
			ConfigSchemaVersion.Value = 2;
			Config.Save();
		}

		DisableLegacyDarkModeIfPresent();
		SceneManager.sceneLoaded += OnSceneLoaded;
		Log.LogInfo($"{PluginName} v{PluginVersion} loaded (ActiveMode={ActiveMode.Value}).");
	}

	private void OnDestroy()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	private void Update()
	{
		if (QuickToggleKey == null || QuickToggleKey.Value == KeyCode.None)
		{
			return;
		}
		if (EventSystem.current != null &&
			EventSystem.current.currentSelectedGameObject != null &&
			EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null)
		{
			return;
		}
		if (Input.GetKeyDown(QuickToggleKey.Value))
		{
			ToggleLightDark();
		}
	}

	private static void DisableLegacyDarkModeIfPresent()
	{
		try
		{
			if (Chainloader.PluginInfos != null && Chainloader.PluginInfos.TryGetValue("Jdewi.DarkMode", out var info) && info.Instance != null && info.Instance.Config != null)
			{
				if (info.Instance.Config.TryGetEntry<bool>(new ConfigDefinition("General", "Enabled"), out var entry) && entry.Value)
				{
					entry.Value = false;
					info.Instance.Config.Save();
					Log?.LogInfo("Delegated Dark/Light styling from legacy DarkMode to RogueTowerThemes.");
				}
			}
		}
		catch
		{
		}
	}

	// ---- Public Static Integration API (ModMenuUI / TestHarness) ----
	public static int GetModeInt() => ActiveMode != null ? (int)ActiveMode.Value : 0;

	public static void SetModeInt(int mode)
	{
		if (ActiveMode == null) return;
		ThemeMode m = (ThemeMode)Mathf.Clamp(mode, 0, 2);
		ActiveMode.Value = m;
		ApplyTheme();
	}

	public static ThemeMode ToggleLightDark()
	{
		if (ActiveMode == null) return ThemeMode.Dark;
		ThemeMode cur = ActiveMode.Value;
		ThemeMode next = (CycleIncludesVanilla != null && CycleIncludesVanilla.Value)
			? (ThemeMode)(((int)cur + 1) % 3)
			: (cur == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);
		ActiveMode.Value = next;
		ApplyTheme();
		return next;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		DisableLegacyDarkModeIfPresent();
		ClearSceneSnapshots();
		CaptureCurrentSceneSnapshots(scene);
		ApplyTheme();
	}

	private static void ClearSceneSnapshots()
	{
		_hasCamBg = false;
		_origCamBg = DefaultCameraSky;
		OrigLights.Clear();
		OrigImages.Clear();
		OrigTexts.Clear();
		_hasHudTexts = false;
	}

	private static void CaptureCurrentSceneSnapshots(Scene scene)
	{
		if (Camera.main != null && !_hasCamBg)
		{
			_origCamBg = Camera.main.backgroundColor;
			_origAmbient = RenderSettings.ambientLight;
			_origAmbientIntensity = RenderSettings.ambientIntensity;
			_hasCamBg = true;
		}

		Light[] lights = FindObjectsOfType<Light>(includeInactive: true);
		for (int i = 0; i < lights.Length; i++)
		{
			Light l = lights[i];
			if (l == null) continue;
			int id = l.GetInstanceID();
			if (!OrigLights.ContainsKey(id))
			{
				OrigLights[id] = new LightSnapshot { Ref = l, Intensity = l.intensity, Color = l.color };
			}
		}

		Image[] images = FindObjectsOfType<Image>(includeInactive: true);
		for (int i = 0; i < images.Length; i++)
		{
			Image img = images[i];
			if (img == null || ShouldSkipUiRoot(img.transform)) continue;
			int id = img.GetInstanceID();
			if (!OrigImages.ContainsKey(id))
			{
				OrigImages[id] = new ImageSnapshot { Ref = img, Color = img.color };
			}
		}

		CaptureHudTextRoots(scene);
	}

	private static void CaptureHudTextRoots(Scene scene)
	{
		if (_hasHudTexts) return;
		if (scene.name == "MainMenu")
		{
			SnapshotTextObj(GameObject.Find("/Canvas/SocialMedia/Text"), includeChildren: false);
		}
		SnapshotTextObj(GameObject.Find("/GameUI/BottomSide/Build Menu"), includeChildren: true, onlyNameContains: "ModifiersText");
		SnapshotTextObj(GameObject.Find("/GameUI/TopSide/DOTnumbers"), includeChildren: true);
		SnapshotTextObj(GameObject.Find("/GameUI/TopSide/OtherCardUIs"), includeChildren: true);
		SnapshotTextObj(GameObject.Find("/GameUI/LeftSide"), includeChildren: true);
		_hasHudTexts = OrigTexts.Count > 0;
	}

	private static void SnapshotTextObj(GameObject root, bool includeChildren, string onlyNameContains = null)
	{
		if (root == null) return;
		if (!includeChildren)
		{
			Text t = root.GetComponent<Text>();
			if (t != null)
			{
				int id = t.GetInstanceID();
				if (!OrigTexts.ContainsKey(id)) OrigTexts[id] = new TextSnapshot { Ref = t, Color = t.color };
			}
			return;
		}
		Text[] list = root.GetComponentsInChildren<Text>(includeInactive: true);
		for (int i = 0; i < list.Length; i++)
		{
			Text t = list[i];
			if (t == null) continue;
			if (!string.IsNullOrEmpty(onlyNameContains) && !t.name.Contains(onlyNameContains)) continue;
			int id = t.GetInstanceID();
			if (!OrigTexts.ContainsKey(id)) OrigTexts[id] = new TextSnapshot { Ref = t, Color = t.color };
		}
	}

	private static bool ShouldSkipUiRoot(Transform t)
	{
		if (t == null) return true;
		string rootName = t.root.name;
		return rootName == "CardRenderer"
			|| rootName.StartsWith("RogueTower", StringComparison.Ordinal)
			|| rootName == "CustomMapsEditorCanvas";
	}

	public static void ApplyTheme()
	{
		if (ActiveMode == null) return;
		ThemeMode mode = ActiveMode.Value;
		Scene activeScene = SceneManager.GetActiveScene();
		if (OrigLights.Count == 0 || OrigImages.Count == 0)
		{
			CaptureCurrentSceneSnapshots(activeScene);
		}
		else if (!_hasHudTexts)
		{
			CaptureHudTextRoots(activeScene);
		}

		if (mode == ThemeMode.Vanilla)
		{
			if (Camera.main != null)
			{
				Camera.main.backgroundColor = _hasCamBg ? _origCamBg : DefaultCameraSky;
			}
			RenderSettings.ambientLight = _origAmbient;
			RenderSettings.ambientIntensity = _origAmbientIntensity;
			foreach (var kv in OrigLights)
			{
				if (kv.Value.Ref != null)
				{
					kv.Value.Ref.intensity = kv.Value.Intensity;
					kv.Value.Ref.color = kv.Value.Color;
				}
			}
			foreach (var kv in OrigImages)
			{
				if (kv.Value.Ref != null)
				{
					kv.Value.Ref.color = kv.Value.Color;
				}
			}
			foreach (var kv in OrigTexts)
			{
				if (kv.Value.Ref != null)
				{
					kv.Value.Ref.color = kv.Value.Color;
				}
			}
			return;
		}

		bool dark = mode == ThemeMode.Dark;
		Color skyColor = ParseHexOrDefault(dark ? DarkSkyHex.Value : LightSkyHex.Value, dark ? new Color(0.043f, 0.063f, 0.125f, 1f) : new Color(0.863f, 0.922f, 1f, 1f));
		Color ambientColor = ParseHexOrDefault(dark ? DarkAmbientHex.Value : LightAmbientHex.Value, dark ? new Color(0.10f, 0.13f, 0.22f, 1f) : new Color(0.43f, 0.48f, 0.56f, 1f));
		Color sunColor = ParseHexOrDefault(dark ? DarkSunHex.Value : LightSunHex.Value, dark ? new Color(0.62f, 0.69f, 1f, 1f) : new Color(1f, 0.976f, 0.925f, 1f));
		Color fillColor = ParseHexOrDefault(dark ? DarkFillHex.Value : LightFillHex.Value, dark ? new Color(0.78f, 0.48f, 0.88f, 1f) : new Color(0.847f, 0.925f, 1f, 1f));
		float sunMult = Mathf.Clamp(dark ? DarkSunMultiplier.Value : LightSunMultiplier.Value, 0.2f, 2.5f);
		float uiBrightness = Mathf.Clamp(dark ? DarkUiBrightness.Value : LightUiBrightness.Value, 0.4f, 1.35f);
		bool highContrast = dark ? DarkHighContrastText.Value : LightHighContrastText.Value;

		if (Camera.main != null)
		{
			Camera.main.backgroundColor = skyColor;
		}
		if (OverrideAmbient != null && OverrideAmbient.Value)
		{
			RenderSettings.ambientLight = ambientColor;
			RenderSettings.ambientIntensity = _origAmbientIntensity * (dark ? 0.75f : 1.15f);
		}
		else
		{
			RenderSettings.ambientLight = _origAmbient;
			RenderSettings.ambientIntensity = _origAmbientIntensity;
		}

		bool isMainMenu = activeScene.name == "MainMenu";
		foreach (var kv in OrigLights)
		{
			Light l = kv.Value.Ref;
			if (l == null) continue;
			if (l.type == LightType.Directional)
			{
				if (isMainMenu || l.name == "Directional Light")
				{
					l.intensity = kv.Value.Intensity * Mathf.Clamp(sunMult, 0.5f, 1.0f);
					l.color = Color.Lerp(kv.Value.Color, sunColor, dark ? 0.45f : 0.20f);
				}
				else
				{
					// Keep 100% of vanilla fill directional light intensity so Tower & shaded roofs do not crush to black
					l.intensity = kv.Value.Intensity;
					l.color = Color.Lerp(kv.Value.Color, fillColor, dark ? 0.28f : 0.15f);
				}
			}
		}

		foreach (var kv in OrigImages)
		{
			Image img = kv.Value.Ref;
			if (img == null) continue;
			Color orig = kv.Value.Color;
			if (Mathf.Abs(uiBrightness - 1f) < 0.005f)
			{
				img.color = orig;
			}
			else
			{
				img.color = new Color(
					Mathf.Clamp01(orig.r * uiBrightness),
					Mathf.Clamp01(orig.g * uiBrightness),
					Mathf.Clamp01(orig.b * uiBrightness),
					orig.a);
			}
		}

		foreach (var kv in OrigTexts)
		{
			Text t = kv.Value.Ref;
			if (t == null) continue;
			t.color = highContrast ? Color.white : kv.Value.Color;
		}
	}

	private static Color ParseHexOrDefault(string hex, Color fallback)
	{
		if (string.IsNullOrWhiteSpace(hex)) return fallback;
		string s = hex.Trim().TrimStart('#');
		if (s.Length != 6 && s.Length != 8) return fallback;
		try
		{
			byte r = Convert.ToByte(s.Substring(0, 2), 16);
			byte g = Convert.ToByte(s.Substring(2, 2), 16);
			byte b = Convert.ToByte(s.Substring(4, 2), 16);
			byte a = s.Length == 8 ? Convert.ToByte(s.Substring(6, 2), 16) : (byte)255;
			return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
		}
		catch
		{
			return fallback;
		}
	}
}
