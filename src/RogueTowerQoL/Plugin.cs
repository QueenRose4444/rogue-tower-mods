using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[assembly: AssemblyCompany("RogueTowerQoL")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyDescription("Clean-room MIT QoL suite for Rogue Tower: range preview rings, MaterialPropertyBlock priority tinting & persistent defaults, Ctrl+C/V priority sync, Levels-to-RPM scaling, Spacebar pause/build, and keybind banner hide.")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
[assembly: AssemblyProduct("RogueTowerQoL")]
[assembly: AssemblyTitle("RogueTowerQoL")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace RogueTowerQoL;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("QoLChanges", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("5fcf1cfc-383c-4ee8-961a-868f560452d9", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Jdewi.SpeedControls", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("RustyDios.WASDRemover", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
	public const string PluginGuid = "Rosie.RogueTowerQoL";
	public const string PluginName = "RogueTowerQoL";
	public const string PluginVersion = "1.0.0";

	internal static ManualLogSource Log;

	public static ConfigEntry<bool> EnableGhostRange;
	public static ConfigEntry<bool> EnableHoverRange;
	public static ConfigEntry<bool> EnablePriorityColors;
	public static ConfigEntry<bool> PersistPriorities;
	public static ConfigEntry<bool> EnableLevelsToRpm;
	public static ConfigEntry<float> RpmBonusPerLevel;
	public static ConfigEntry<bool> BonusStartsAtLevel2;
	public static ConfigEntry<bool> EnableSpacePauseResume;
	public static ConfigEntry<bool> AllowBuildWhilePaused;
	public static ConfigEntry<bool> HideKeybindsOverlay;
	public static ConfigEntry<bool> ForceOverrideLegacy;

	internal static bool LegacyQoLLoaded;
	internal static bool LegacyLevelsRpmLoaded;
	internal static bool LegacySpeedLoaded;
	internal static bool LegacyWasdLoaded;

	internal static readonly Dictionary<TowerType, Tower.Priority[]> DefaultPriorities = new Dictionary<TowerType, Tower.Priority[]>();
	internal static readonly Tower.Priority[] ClipboardPriorities = new Tower.Priority[3];
	internal static readonly Dictionary<TowerType, float> BaseRpmByType = new Dictionary<TowerType, float>();

	private Harmony _harmony;

	private void Awake()
	{
		Log = Logger;

		EnableGhostRange = Config.Bind("1. Range Circles", "EnableGhostRange", true, "Draws build-mode tower placement range circle/square.");
		EnableHoverRange = Config.Bind("1. Range Circles", "EnableHoverRange", true, "Draws range ring when hovering an existing placed tower.");
		EnablePriorityColors = Config.Bind("2. Targeting Priorities", "EnablePriorityColors", true, "Tints tower tops via MaterialPropertyBlock to reflect target priorities.");
		PersistPriorities = Config.Bind("2. Targeting Priorities", "PersistPriorities", true, "Saves default priority triples per tower type across runs.");
		EnableLevelsToRpm = Config.Bind("3. Levels to RPM", "EnableLevelsToRpm", true, "Scales tower fire rate (RPM) by tower level.");
		RpmBonusPerLevel = Config.Bind("3. Levels to RPM", "RpmBonusPerLevel", 0.07f, "Fractional RPM bonus per level (0.07 = +7% RPM/level).");
		BonusStartsAtLevel2 = Config.Bind("3. Levels to RPM", "BonusStartsAtLevel2", true, "If true, Level 1 is base RPM and gains +7%/level starting at Level 2.");
		EnableSpacePauseResume = Config.Bind("4. Speed & HUD", "EnableSpacePauseResume", true, "Spacebar toggles pause/resume while keeping your game speed (when standalone).");
		AllowBuildWhilePaused = Config.Bind("4. Speed & HUD", "AllowBuildWhilePaused", true, "Updates placement ghost while game is paused (when standalone).");
		HideKeybindsOverlay = Config.Bind("4. Speed & HUD", "HideKeybindsOverlay", true, "Hides the redundant KeybindsUI banner if standalone.");
		ForceOverrideLegacy = Config.Bind("5. Compatibility", "ForceOverrideLegacy", false, "Keep false so companion legacy plugins handle their own hooks when installed alongside RogueTowerQoL.");

		DetectLegacyPlugins();
		LoadSavedPriorities();

		_harmony = new Harmony(PluginGuid);
		_harmony.PatchAll(typeof(QoLPatches));
		Log.LogInfo($"{PluginName} v{PluginVersion} loaded (Standalone: QoL={!LegacyQoLLoaded}, Rpm={!LegacyLevelsRpmLoaded}, Speed={!LegacySpeedLoaded}).");
	}

	private void Start()
	{
		DetectLegacyPlugins();
	}

	private void OnDestroy()
	{
		_harmony?.UnpatchSelf();
	}

	internal static bool ShouldRunQoL() => ForceOverrideLegacy.Value || !LegacyQoLLoaded;
	internal static bool ShouldRunRpm() => ForceOverrideLegacy.Value || !LegacyLevelsRpmLoaded;
	internal static bool ShouldRunSpeed() => ForceOverrideLegacy.Value || !LegacySpeedLoaded;
	internal static bool ShouldRunHideKeybinds() => ForceOverrideLegacy.Value || !LegacyWasdLoaded;

	private static void DetectLegacyPlugins()
	{
		var infos = Chainloader.PluginInfos;
		if (infos == null) return;
		LegacyQoLLoaded = infos.ContainsKey("QoLChanges");
		LegacyLevelsRpmLoaded = infos.ContainsKey("5fcf1cfc-383c-4ee8-961a-868f560452d9");
		LegacySpeedLoaded = infos.ContainsKey("Jdewi.SpeedControls");
		LegacyWasdLoaded = infos.ContainsKey("RustyDios.WASDRemover");
	}

	private static void LoadSavedPriorities()
	{
		foreach (TowerType t in Enum.GetValues(typeof(TowerType)))
		{
			Tower.Priority[] p = new Tower.Priority[3];
			for (int i = 0; i < 3; i++)
			{
				p[i] = (Tower.Priority)Mathf.Clamp(PlayerPrefs.GetInt($"RTQoL_Prio_{t}_{i}", (int)Tower.Priority.Progress), 0, 10);
			}
			DefaultPriorities[t] = p;
		}
		for (int i = 0; i < 3; i++)
		{
			ClipboardPriorities[i] = (Tower.Priority)Mathf.Clamp(PlayerPrefs.GetInt($"RTQoL_Clip_{i}", (int)Tower.Priority.Progress), 0, 10);
		}
	}

	internal static void SaveDefaultPriority(TowerType t)
	{
		if (!PersistPriorities.Value || !DefaultPriorities.TryGetValue(t, out var p)) return;
		for (int i = 0; i < 3; i++)
		{
			PlayerPrefs.SetInt($"RTQoL_Prio_{t}_{i}", (int)p[i]);
		}
	}

	internal static void SaveClipboard()
	{
		for (int i = 0; i < 3; i++)
		{
			PlayerPrefs.SetInt($"RTQoL_Clip_{i}", (int)ClipboardPriorities[i]);
		}
	}

	public static void RefreshAllTowers()
	{
		if (TowerManager.instance != null)
		{
			TowerManager.instance.UpdateAllTowers();
		}
	}

	private float _savedSpeedBeforePause = 1f;
	private static readonly FieldInfo BuildSpotAvailField = AccessTools.Field(typeof(BuildingManager), "buildSpotAvailable");
	private static readonly MethodInfo SamplePointMethod = AccessTools.Method(typeof(BuildingManager), "SamplePoint");

	private void Update()
	{
		if (ShouldRunQoL())
		{
			QoLPatches.TickHoverCircle();
		}

		if (!ShouldRunSpeed()) return;

		if (AllowBuildWhilePaused.Value && Time.timeScale < 0.05f && BuildingManager.instance != null && BuildingManager.instance.buildMode)
		{
			if (SamplePointMethod != null && BuildSpotAvailField != null)
			{
				BuildSpotAvailField.SetValue(BuildingManager.instance, SamplePointMethod.Invoke(BuildingManager.instance, null));
			}
		}

		if (EnableSpacePauseResume.Value && GameManager.instance != null && Input.GetKeyDown(KeyCode.Space) && PauseMenu.instance != null && !PauseMenu.instance.paused)
		{
			if (EventSystem.current != null &&
				EventSystem.current.currentSelectedGameObject != null &&
				EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null)
			{
				return;
			}
			if (Time.timeScale > 0.05f)
			{
				_savedSpeedBeforePause = Time.timeScale;
				Time.timeScale = 0f;
			}
			else
			{
				Time.timeScale = _savedSpeedBeforePause > 0.05f ? _savedSpeedBeforePause : 1f;
			}
		}
	}
}

[HarmonyPatch]
internal static class QoLPatches
{
	private static readonly FieldInfo BaseRangeField = AccessTools.Field(typeof(Tower), "baseRange");
	private static readonly FieldInfo PriorityTextsField = AccessTools.Field(typeof(TowerUI), "priorityTexts");
	private static readonly FieldInfo ClickableMaskField = AccessTools.Field(typeof(UIManager), "clickableMask");

	private static LineRenderer _ghostRing;
	private static LineRenderer _hoverRing;
	private static Material _ringMaterial;
	private static Tower _lastHoveredTower;
	private static readonly MaterialPropertyBlock TintBlock = new MaterialPropertyBlock();

	private static readonly Color BaseTintNeutral = new Color(0.596f, 0.588f, 0.537f);

	private static readonly Dictionary<Tower.Priority, Color> PriorityPalette = new Dictionary<Tower.Priority, Color>
	{
		{ Tower.Priority.Progress, BaseTintNeutral },
		{ Tower.Priority.NearDeath, new Color(0.8314f, 0.4157f, 0.4157f) },
		{ Tower.Priority.MostHealth, new Color(0.533f, 0.8f, 0.533f) },
		{ Tower.Priority.MostArmor, new Color(1f, 1f, 0.667f) },
		{ Tower.Priority.MostShield, new Color(0.4706f, 0.5294f, 0.6706f) },
		{ Tower.Priority.LeastHealth, Color.Lerp(new Color(0.533f, 0.8f, 0.533f), Color.black, 0.5f) },
		{ Tower.Priority.LeastArmor, Color.Lerp(new Color(1f, 1f, 0.667f), Color.black, 0.5f) },
		{ Tower.Priority.LeastShield, Color.Lerp(new Color(0.4706f, 0.5294f, 0.6706f), Color.black, 0.5f) },
		{ Tower.Priority.Fastest, new Color(0.8314f, 0.6941f, 0.4157f) },
		{ Tower.Priority.Slowest, Color.Lerp(new Color(0.8314f, 0.6941f, 0.4157f), Color.black, 0.5f) },
		{ Tower.Priority.Marked, Color.Lerp(Color.red, Color.black, 0.25f) }
	};

	private static LineRenderer EnsureRing(ref LineRenderer ring, string name, Color color)
	{
		if (ring != null) return ring;
		GameObject go = new GameObject(name);
		ring = go.AddComponent<LineRenderer>();
		if (_ringMaterial == null)
		{
			Shader sh = Shader.Find("Sprites/Default");
			_ringMaterial = sh != null ? new Material(sh) : null;
		}
		if (_ringMaterial != null) ring.material = _ringMaterial;
		ring.startWidth = 0.08f;
		ring.endWidth = 0.08f;
		ring.startColor = color;
		ring.endColor = color;
		ring.useWorldSpace = true;
		ring.loop = false;
		go.SetActive(false);
		return ring;
	}

	private static void DrawRangeShape(LineRenderer lr, Vector3 pos, float range, bool square)
	{
		float y = Mathf.Max(0.4f, pos.y + 0.35f);
		if (square)
		{
			float r = range + 0.5f;
			lr.positionCount = 5;
			lr.SetPosition(0, new Vector3(pos.x + r, y, pos.z + r));
			lr.SetPosition(1, new Vector3(pos.x + r, y, pos.z - r));
			lr.SetPosition(2, new Vector3(pos.x - r, y, pos.z - r));
			lr.SetPosition(3, new Vector3(pos.x - r, y, pos.z + r));
			lr.SetPosition(4, new Vector3(pos.x + r, y, pos.z + r));
			return;
		}
		const int segs = 64;
		lr.positionCount = segs + 1;
		for (int i = 0; i <= segs; i++)
		{
			float a = (float)i / segs * Mathf.PI * 2f;
			lr.SetPosition(i, new Vector3(pos.x + Mathf.Cos(a) * range, y, pos.z + Mathf.Sin(a) * range));
		}
	}

	private static bool IsSquareTower(Tower t)
	{
		return t.squareUI || t.towerType == TowerType.Frost || t.towerType == TowerType.Encampment;
	}

	[HarmonyPatch(typeof(BuildingManager), "DisplayGhost")]
	[HarmonyPostfix]
	private static void BuildingManager_DisplayGhost_Postfix(Vector3 pos, GameObject ___currentGhost, GameObject ___thingToBuild)
	{
		if (!Plugin.ShouldRunQoL() || !Plugin.EnableGhostRange.Value)
		{
			if (_ghostRing != null) _ghostRing.gameObject.SetActive(false);
			return;
		}
		if (___currentGhost != null && ___currentGhost.activeInHierarchy && ___thingToBuild != null)
		{
			Tower prefabTower = ___thingToBuild.GetComponent<Tower>();
			if (prefabTower != null)
			{
				float baseRange = BaseRangeField != null ? (float)BaseRangeField.GetValue(prefabTower) : 4f;
				int hBonus = (int)Mathf.Round(pos.y * 3f - 1f);
				float hMult = TowerManager.instance != null ? (1f + TowerManager.instance.GetHeightRangeBonusMultiplier(prefabTower.towerType)) : 1f;
				float bRange = TowerManager.instance != null ? TowerManager.instance.GetBonusRange(prefabTower.towerType) : 0f;
				float finalRange = baseRange + hBonus / 2f * hMult + bRange;

				LineRenderer lr = EnsureRing(ref _ghostRing, "RTQoL_GhostRing", new Color(0.45f, 0.85f, 1f, 0.85f));
				lr.gameObject.SetActive(true);
				DrawRangeShape(lr, pos, finalRange, IsSquareTower(prefabTower));
				return;
			}
		}
		if (_ghostRing != null) _ghostRing.gameObject.SetActive(false);
	}

	[HarmonyPatch(typeof(BuildingManager), "HideGhost")]
	[HarmonyPostfix]
	private static void BuildingManager_HideGhost_Postfix()
	{
		if (_ghostRing != null) _ghostRing.gameObject.SetActive(false);
	}

	internal static void TickHoverCircle()
	{
		if (!Plugin.EnableHoverRange.Value || UIManager.instance == null || Camera.main == null || (BuildingManager.instance != null && BuildingManager.instance.buildMode))
		{
			if (_hoverRing != null) _hoverRing.gameObject.SetActive(false);
			_lastHoveredTower = null;
			return;
		}
		if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
		{
			if (_hoverRing != null) _hoverRing.gameObject.SetActive(false);
			_lastHoveredTower = null;
			return;
		}
		if (ClickableMaskField == null) return;
		LayerMask mask = (LayerMask)ClickableMaskField.GetValue(UIManager.instance);
		if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 2000f, mask, QueryTriggerInteraction.Collide))
		{
			Tower t = hit.collider.GetComponent<Tower>();
			if (t != null)
			{
				LineRenderer lr = EnsureRing(ref _hoverRing, "RTQoL_HoverRing", new Color(1f, 0.9f, 0.45f, 0.8f));
				lr.gameObject.SetActive(true);
				if (_lastHoveredTower != t)
				{
					_lastHoveredTower = t;
					DrawRangeShape(lr, t.transform.position, t.range, IsSquareTower(t));
				}
				return;
			}
		}
		if (_hoverRing != null) _hoverRing.gameObject.SetActive(false);
		_lastHoveredTower = null;
	}

	private static void ApplyPriorityTint(Tower tower)
	{
		if (tower == null || !Plugin.EnablePriorityColors.Value) return;
		MeshRenderer mr = tower.GetComponentInChildren<MeshRenderer>();
		if (mr == null) return;

		Color c = PriorityPalette.TryGetValue(tower.priorities[0], out Color p0) ? p0 : BaseTintNeutral;
		if (tower.priorities[1] != tower.priorities[0] && PriorityPalette.TryGetValue(tower.priorities[1], out Color p1))
		{
			c = Color.Lerp(c, p1, 0.25f);
		}
		if (tower.priorities[2] != tower.priorities[0] && tower.priorities[2] != tower.priorities[1] && PriorityPalette.TryGetValue(tower.priorities[2], out Color p2))
		{
			c = Color.Lerp(c, p2, 0.10f);
		}
		Color scaled = new Color(c.r / BaseTintNeutral.r, c.g / BaseTintNeutral.g, c.b / BaseTintNeutral.b, 1f);
		mr.GetPropertyBlock(TintBlock);
		TintBlock.SetColor("_Color", scaled);
		mr.SetPropertyBlock(TintBlock);
	}

	[HarmonyPatch(typeof(Tower), "SetStats")]
	[HarmonyPrefix]
	private static void Tower_SetStats_Prefix(Tower __instance)
	{
		if (!Plugin.ShouldRunRpm() || !Plugin.EnableLevelsToRpm.Value) return;
		if (!Plugin.BaseRpmByType.TryGetValue(__instance.towerType, out float baseRpm))
		{
			baseRpm = __instance.rpm;
			Plugin.BaseRpmByType[__instance.towerType] = baseRpm;
		}
		int eff = Plugin.BonusStartsAtLevel2.Value ? Mathf.Max(0, __instance.level - 1) : __instance.level;
		__instance.rpm = baseRpm * (1f + Plugin.RpmBonusPerLevel.Value * eff);
	}

	[HarmonyPatch(typeof(Tower), "Start")]
	[HarmonyPostfix]
	private static void Tower_Start_Postfix(Tower __instance)
	{
		if (!Plugin.ShouldRunQoL()) return;
		if (Plugin.PersistPriorities.Value && Plugin.DefaultPriorities.TryGetValue(__instance.towerType, out var saved))
		{
			for (int i = 0; i < 3 && i < __instance.priorities.Length; i++)
			{
				__instance.priorities[i] = saved[i];
			}
		}
		ApplyPriorityTint(__instance);
	}

	[HarmonyPatch(typeof(Tower), "TogglePriority")]
	[HarmonyPostfix]
	private static void Tower_TogglePriority_Postfix(Tower __instance)
	{
		if (!Plugin.ShouldRunQoL()) return;
		if (Plugin.PersistPriorities.Value && Plugin.DefaultPriorities.TryGetValue(__instance.towerType, out var saved))
		{
			for (int i = 0; i < 3 && i < __instance.priorities.Length; i++) saved[i] = __instance.priorities[i];
			Plugin.SaveDefaultPriority(__instance.towerType);
		}
		ApplyPriorityTint(__instance);
	}

	[HarmonyPatch(typeof(Tower), "CopyPriorities")]
	[HarmonyPostfix]
	private static void Tower_CopyPriorities_Postfix(Tower __instance)
	{
		if (!Plugin.ShouldRunQoL()) return;
		for (int i = 0; i < 3 && i < __instance.priorities.Length; i++)
		{
			Plugin.ClipboardPriorities[i] = __instance.priorities[i];
		}
		Plugin.SaveClipboard();
	}

	[HarmonyPatch(typeof(Tower), "PastePriorities")]
	[HarmonyPostfix]
	private static void Tower_PastePriorities_Postfix(Tower __instance)
	{
		if (!Plugin.ShouldRunQoL()) return;
		if (Plugin.PersistPriorities.Value && Plugin.DefaultPriorities.TryGetValue(__instance.towerType, out var saved))
		{
			for (int i = 0; i < 3 && i < __instance.priorities.Length; i++) saved[i] = __instance.priorities[i];
			Plugin.SaveDefaultPriority(__instance.towerType);
		}
		ApplyPriorityTint(__instance);
	}

	[HarmonyPatch(typeof(TowerUI), "Update")]
	[HarmonyPostfix]
	private static void TowerUI_Update_Postfix(TowerUI __instance, Tower ___myTower)
	{
		if (!Plugin.ShouldRunQoL() || ___myTower == null) return;
		if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;

		if (Input.GetKeyDown(KeyCode.C))
		{
			___myTower.CopyPriorities();
			Tower_CopyPriorities_Postfix(___myTower);
		}
		else if (Input.GetKeyDown(KeyCode.V))
		{
			for (int i = 0; i < 3 && i < ___myTower.priorities.Length; i++)
			{
				___myTower.priorities[i] = Plugin.ClipboardPriorities[i];
			}
			if (GameManager.instance != null)
			{
				GameManager.instance.prioritiesClipboard = new Vector3Int(
					(int)Plugin.ClipboardPriorities[0],
					(int)Plugin.ClipboardPriorities[1],
					(int)Plugin.ClipboardPriorities[2]);
			}
			Tower_PastePriorities_Postfix(___myTower);
			if (PriorityTextsField?.GetValue(__instance) is Text[] texts)
			{
				for (int i = 0; i < texts.Length && i < ___myTower.priorities.Length; i++)
				{
					if (texts[i] != null) texts[i].text = ___myTower.priorities[i].ToString();
				}
			}
		}
	}

	[HarmonyPatch(typeof(CameraController), "UpdateMovement")]
	[HarmonyPrefix]
	private static bool CameraController_UpdateMovement_Prefix()
	{
		if (!Plugin.ShouldRunQoL()) return true;
		return !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl);
	}

	[HarmonyPatch(typeof(GameManager), "Start")]
	[HarmonyPostfix]
	private static void GameManager_Start_Postfix()
	{
		if (!Plugin.ShouldRunHideKeybinds() || !Plugin.HideKeybindsOverlay.Value) return;
		GameObject keybinds = GameObject.Find("/GameUI/TopSide/KeybindsUI");
		if (keybinds != null) keybinds.SetActive(false);
	}
}
