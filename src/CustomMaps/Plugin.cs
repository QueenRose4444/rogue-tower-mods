using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RogueTowerCustomMaps;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
	public const string PluginGuid = "acr.rougetower.custommaps";
	public const string PluginName = "RogueTowerCustomMaps";
	public const string PluginVersion = "1.2.0";

	internal static ManualLogSource Log;
	internal static ConfigEntry<bool> RememberMapAcrossRuns;
	internal static ConfigEntry<string> CustomMapCSV;

	private Harmony _harmony;

	public static string[] GetPresetNames()
	{
		return CustomMapManager.GetPresetNames();
	}

	public static int GetSelectedMapIndex()
	{
		return CustomMapManager.SelectedMapIndex;
	}

	public static void SetSelectedMapIndex(int index)
	{
		CustomMapManager.SetSelectedMapIndex(index);
	}

	public static string GetActiveRunMapName()
	{
		int idx = Mathf.Clamp(CustomMapManager.ActiveRunMapIndex, 0, CustomMapManager.Presets.Length - 1);
		return CustomMapManager.Presets[idx].Name;
	}

	private void Awake()
	{
		Log = Logger;

		RememberMapAcrossRuns = Config.Bind(
			"General",
			"RememberMapAcrossRuns",
			false,
			"If false (default), the selected custom map resets back to Vanilla (Random) after each run starts so you toggle it per-run on the Play menu. If true, the selected map stays active across runs.");

		CustomMapCSV = Config.Bind(
			"General",
			"CustomMapCSV",
			"S, S, P",
			"Comma-separated tile sequence when 'Custom (CSV)' is selected. Valid codes: S, L, R, SL, SR, LR, SLR, P.");

		_harmony = new Harmony(PluginGuid);
		_harmony.PatchAll(typeof(CustomMapPatches));

		Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
	}

	private void OnDestroy()
	{
		_harmony?.UnpatchSelf();
	}
}

internal enum MapModeKind
{
	Vanilla = 0,
	SpiralCCW = 1,
	SpiralCW = 2,
	PresetQueue = 3,
	CustomCSV = 4
}

internal readonly struct MapPreset
{
	public readonly string Name;
	public readonly MapModeKind Kind;
	public readonly string[] Sequence;

	public MapPreset(string name, MapModeKind kind, string[] sequence = null)
	{
		Name = name;
		Kind = kind;
		Sequence = sequence;
	}
}

internal static class CustomMapManager
{
	internal static readonly MapPreset[] Presets = new MapPreset[]
	{
		new MapPreset("Vanilla (Random)", MapModeKind.Vanilla),
		new MapPreset("Spiral Outwards (CCW)", MapModeKind.SpiralCCW),
		new MapPreset("Spiral Outwards (CW)", MapModeKind.SpiralCW),
		new MapPreset("Barely Legal", MapModeKind.PresetQueue, new[] { "L", "S", "L", "S", "L", "L", "P" }),
		new MapPreset("The Clock", MapModeKind.PresetQueue, new[] { "L", "L", "S", "L", "S", "L", "S", "P" }),
		new MapPreset("5 Straights + Portal", MapModeKind.PresetQueue, new[] { "S", "S", "S", "S", "S", "P" }),
		new MapPreset("Instant Portal", MapModeKind.PresetQueue, new[] { "P", "P", "P", "P" }),
		new MapPreset("3-Way Instant Portal", MapModeKind.PresetQueue, new[] { "SLR", "P", "P", "P" }),
		new MapPreset("1 Turn 1 Portal", MapModeKind.PresetQueue, new[] { "L", "P" }),
		new MapPreset("1 Turn 3 Portals", MapModeKind.PresetQueue, new[] { "L", "SLR", "P", "P", "P" }),
		new MapPreset("T-Split Portals", MapModeKind.PresetQueue, new[] { "S", "S", "LR", "P", "P" }),
		new MapPreset("Custom (CSV)", MapModeKind.CustomCSV)
	};

	// Index selected in PlayMenu for the next run (0 = Vanilla / OFF)
	internal static int SelectedMapIndex;

	// Index latched for the currently active GameScene run
	internal static int ActiveRunMapIndex;

	private static readonly Queue<string> CurrentQueue = new Queue<string>();
	private static Text _menuButtonText;
	private static Image _menuButtonImage;
	private static Color _defaultButtonColor = Color.white;
	private static readonly Color ActiveHighlightColor = new Color(1.0f, 0.88f, 0.55f, 1.0f);

	// Original TileManager prefab arrays captured once per TileManager instance
	private static bool _hasSavedTiles;
	private static GameObject[] _origDeadEnd = Array.Empty<GameObject>();
	private static GameObject[] _origL = Array.Empty<GameObject>();
	private static GameObject[] _origT = Array.Empty<GameObject>();
	private static GameObject[] _origR = Array.Empty<GameObject>();
	private static GameObject[] _origLT = Array.Empty<GameObject>();
	private static GameObject[] _origLR = Array.Empty<GameObject>();
	private static GameObject[] _origTR = Array.Empty<GameObject>();
	private static GameObject[] _origLTR = Array.Empty<GameObject>();
	private static readonly GameObject[] EmptyTiles = Array.Empty<GameObject>();

	private static readonly AccessTools.FieldRef<TileManager, GameObject[]> DeadEndTilesRef =
		AccessTools.FieldRefAccess<TileManager, GameObject[]>("deadEndTiles");
	private static readonly AccessTools.FieldRef<TileManager, GameObject[]> LTilesRef =
		AccessTools.FieldRefAccess<TileManager, GameObject[]>("Ltiles");
	private static readonly AccessTools.FieldRef<TileManager, GameObject[]> TTilesRef =
		AccessTools.FieldRefAccess<TileManager, GameObject[]>("Ttiles");
	private static readonly AccessTools.FieldRef<TileManager, GameObject[]> RTilesRef =
		AccessTools.FieldRefAccess<TileManager, GameObject[]>("Rtiles");
	private static readonly AccessTools.FieldRef<TileManager, GameObject[]> LTTilesRef =
		AccessTools.FieldRefAccess<TileManager, GameObject[]>("LTtiles");
	private static readonly AccessTools.FieldRef<TileManager, GameObject[]> LRTilesRef =
		AccessTools.FieldRefAccess<TileManager, GameObject[]>("LRtiles");
	private static readonly AccessTools.FieldRef<TileManager, GameObject[]> TRTilesRef =
		AccessTools.FieldRefAccess<TileManager, GameObject[]>("TRtiles");
	private static readonly AccessTools.FieldRef<TileManager, GameObject[]> LTRTilesRef =
		AccessTools.FieldRefAccess<TileManager, GameObject[]>("LTRtiles");
	private static readonly AccessTools.FieldRef<TileManager, int[,]> IntArrayRef =
		AccessTools.FieldRefAccess<TileManager, int[,]>("intArray");

	internal static void SetupPlayMenuButton(PlayMenu playMenu)
	{
		if (playMenu == null)
		{
			return;
		}

		if (!Plugin.RememberMapAcrossRuns.Value)
		{
			SelectedMapIndex = 0;
		}
		SelectedMapIndex = Mathf.Clamp(SelectedMapIndex, 0, Presets.Length - 1);

		Transform scaler = playMenu.transform.Find("Scaler");
		if (scaler == null)
		{
			return;
		}

		Transform existing = scaler.Find("CustomMapSelectorButton");
		GameObject btnObj;
		if (existing != null)
		{
			btnObj = existing.gameObject;
		}
		else
		{
			Transform backBtnTr = scaler.Find("BackButton");
			if (backBtnTr == null)
			{
				Plugin.Log.LogWarning("Could not find BackButton under PlayMenu/Scaler to clone CustomMapSelectorButton.");
				return;
			}

			btnObj = UnityEngine.Object.Instantiate(backBtnTr.gameObject, scaler);
			btnObj.name = "CustomMapSelectorButton";

			Button btn = btnObj.GetComponent<Button>();
			btn.onClick = new Button.ButtonClickedEvent();
			btn.onClick.AddListener(CycleSelectedMap);
		}

		RectTransform rect = btnObj.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2(0.5f, 0.5f);
		rect.anchorMax = new Vector2(0.5f, 0.5f);
		rect.pivot = new Vector2(0.5f, 0.5f);
		// Side-by-side with Auto-Max button cleanly above Single/Double/Triple/Quadruple (y=+50..+150)
		rect.anchoredPosition = new Vector2(-145f, 180f);
		rect.sizeDelta = new Vector2(280f, 34f);
		rect.localScale = Vector3.one;

		_menuButtonImage = btnObj.GetComponent<Image>();
		if (_menuButtonImage != null && existing == null)
		{
			_defaultButtonColor = _menuButtonImage.color;
		}

		_menuButtonText = btnObj.GetComponentInChildren<Text>(includeInactive: true);
		if (_menuButtonText != null)
		{
			_menuButtonText.resizeTextForBestFit = false;
			_menuButtonText.fontSize = 120;
			_menuButtonText.alignment = TextAnchor.MiddleCenter;
			_menuButtonText.horizontalOverflow = HorizontalWrapMode.Overflow;
			_menuButtonText.verticalOverflow = VerticalWrapMode.Overflow;
		}

		UpdatePlayMenuButtonVisual();
	}

	internal static string[] GetPresetNames()
	{
		string[] names = new string[Presets.Length];
		for (int i = 0; i < Presets.Length; i++)
		{
			names[i] = Presets[i].Name;
		}
		return names;
	}

	internal static void SetSelectedMapIndex(int index)
	{
		SelectedMapIndex = Mathf.Clamp(index, 0, Presets.Length - 1);
		UpdatePlayMenuButtonVisual();
	}

	private static void CycleSelectedMap()
	{
		SelectedMapIndex = (SelectedMapIndex + 1) % Presets.Length;
		UpdatePlayMenuButtonVisual();
		if (SFXManager.instance != null)
		{
			SFXManager.instance.ButtonClick();
		}
	}

	private static void UpdatePlayMenuButtonVisual()
	{
		MapPreset preset = Presets[Mathf.Clamp(SelectedMapIndex, 0, Presets.Length - 1)];
		if (_menuButtonText != null)
		{
			_menuButtonText.text = $"Map: {preset.Name}";
		}
		if (_menuButtonImage != null)
		{
			_menuButtonImage.color = preset.Kind == MapModeKind.Vanilla
				? _defaultButtonColor
				: ActiveHighlightColor;
		}
	}

	internal static void CaptureOriginalTilesAndLatchRunMode(TileManager tm)
	{
		if (tm == null)
		{
			return;
		}

		_origDeadEnd = DeadEndTilesRef(tm) ?? EmptyTiles;
		_origL = LTilesRef(tm) ?? EmptyTiles;
		_origT = TTilesRef(tm) ?? EmptyTiles;
		_origR = RTilesRef(tm) ?? EmptyTiles;
		_origLT = LTTilesRef(tm) ?? EmptyTiles;
		_origLR = LRTilesRef(tm) ?? EmptyTiles;
		_origTR = TRTilesRef(tm) ?? EmptyTiles;
		_origLTR = LTRTilesRef(tm) ?? EmptyTiles;
		_hasSavedTiles = true;

		ActiveRunMapIndex = Mathf.Clamp(SelectedMapIndex, 0, Presets.Length - 1);
		CurrentQueue.Clear();

		MapPreset preset = Presets[ActiveRunMapIndex];
		if (preset.Kind == MapModeKind.PresetQueue && preset.Sequence != null)
		{
			for (int i = 0; i < preset.Sequence.Length; i++)
			{
				CurrentQueue.Enqueue(preset.Sequence[i]);
			}
		}
		else if (preset.Kind == MapModeKind.CustomCSV)
		{
			string csv = Plugin.CustomMapCSV.Value ?? "S, S, P";
			string[] tokens = csv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (tokens.Length == 0)
			{
				CurrentQueue.Enqueue("P");
			}
			else
			{
				for (int i = 0; i < tokens.Length; i++)
				{
					CurrentQueue.Enqueue(tokens[i].Trim().ToUpperInvariant());
				}
			}
		}

		Plugin.Log.LogInfo($"Run started with map mode: {preset.Name}");
	}

	internal static void ConsumeSelectionIfPerRun()
	{
		if (!Plugin.RememberMapAcrossRuns.Value)
		{
			SelectedMapIndex = 0;
		}
	}

	internal static void PrepareTilePoolForSpawn(TileManager tm, int posX, int posY, int eulerAngle)
	{
		if (tm == null || !_hasSavedTiles)
		{
			return;
		}

		MapPreset preset = Presets[Mathf.Clamp(ActiveRunMapIndex, 0, Presets.Length - 1)];
		if (preset.Kind == MapModeKind.Vanilla)
		{
			RestoreOriginalArrays(tm);
			return;
		}

		GameObject[] chosenPool;
		if (preset.Kind == MapModeKind.SpiralCCW || preset.Kind == MapModeKind.SpiralCW)
		{
			chosenPool = SelectSpiralTilePool(tm, posX, posY, eulerAngle, clockwise: preset.Kind == MapModeKind.SpiralCW);
		}
		else
		{
			string key = CurrentQueue.Count > 0 ? CurrentQueue.Dequeue() : "P";
			chosenPool = ResolveTokenPool(key);
		}

		if (chosenPool == null || chosenPool.Length == 0)
		{
			chosenPool = _origDeadEnd;
		}

		ClearBranchArraysToDeadEndFallback(tm, chosenPool);
	}

	internal static void RestoreOriginalArrays(TileManager tm)
	{
		if (tm == null || !_hasSavedTiles)
		{
			return;
		}

		DeadEndTilesRef(tm) = _origDeadEnd;
		LTilesRef(tm) = _origL;
		TTilesRef(tm) = _origT;
		RTilesRef(tm) = _origR;
		LTTilesRef(tm) = _origLT;
		LRTilesRef(tm) = _origLR;
		TRTilesRef(tm) = _origTR;
		LTRTilesRef(tm) = _origLTR;
	}

	private static void ClearBranchArraysToDeadEndFallback(TileManager tm, GameObject[] forcedPool)
	{
		LTilesRef(tm) = EmptyTiles;
		TTilesRef(tm) = EmptyTiles;
		RTilesRef(tm) = EmptyTiles;
		LTTilesRef(tm) = EmptyTiles;
		LRTilesRef(tm) = EmptyTiles;
		TRTilesRef(tm) = EmptyTiles;
		LTRTilesRef(tm) = EmptyTiles;
		DeadEndTilesRef(tm) = forcedPool;
	}

	private static GameObject[] SelectSpiralTilePool(TileManager tm, int posX, int posY, int eulerAngle, bool clockwise)
	{
		if (SpawnManager.instance != null)
		{
			int num = SpawnManager.instance.lastLevel - SpawnManager.instance.level;
			int count = SpawnManager.instance.tileSpawnUis.Count;
			if (count == num)
			{
				// Final wave reaches lastLevel: force portal dead-ends so the run can end cleanly
				return _origDeadEnd;
			}
		}

		int[,] intArray = IntArrayRef(tm);
		if (intArray == null || posX <= 0 || posX >= 98 || posY <= 0 || posY >= 98)
		{
			return _origDeadEnd;
		}

		bool southFree = intArray[posX, posY - 1] == 0;
		bool westFree = intArray[posX - 1, posY] == 0;
		bool northFree = intArray[posX, posY + 1] == 0;
		bool eastFree = intArray[posX + 1, posY] == 0;

		bool left;
		bool top;
		bool right;
		switch (eulerAngle)
		{
			case 0:
				left = westFree;
				top = northFree;
				right = eastFree;
				break;
			case 90:
				left = northFree;
				top = eastFree;
				right = southFree;
				break;
			case 180:
				left = eastFree;
				top = southFree;
				right = westFree;
				break;
			case 270:
				left = southFree;
				top = westFree;
				right = northFree;
				break;
			default:
				return _origDeadEnd;
		}

		if (clockwise)
		{
			if (right && _origR.Length > 0) return _origR;
			if (top && _origT.Length > 0) return _origT;
			if (left && _origL.Length > 0) return _origL;
			return _origDeadEnd;
		}

		// Counter-clockwise spiral hugs left wall first, then straight, then right
		if (left && _origL.Length > 0) return _origL;
		if (top && _origT.Length > 0) return _origT;
		if (right && _origR.Length > 0) return _origR;
		return _origDeadEnd;
	}

	private static GameObject[] ResolveTokenPool(string token)
	{
		return token switch
		{
			"S" => _origT,
			"L" => _origL,
			"R" => _origR,
			"SL" or "LS" => _origLT,
			"SR" or "RS" => _origTR,
			"LR" or "RL" => _origLR,
			"SLR" or "SRL" => _origLTR,
			"P" => _origDeadEnd,
			_ => _origDeadEnd
		};
	}
}

internal static class CustomMapPatches
{
	[HarmonyPatch(typeof(PlayMenu), "Start")]
	[HarmonyPostfix]
	private static void PlayMenu_Start_Postfix(PlayMenu __instance)
	{
		CustomMapManager.SetupPlayMenuButton(__instance);
	}

	[HarmonyPatch(typeof(TileManager), "Awake")]
	[HarmonyPostfix]
	private static void TileManager_Awake_Postfix(TileManager __instance)
	{
		CustomMapManager.CaptureOriginalTilesAndLatchRunMode(__instance);
	}

	[HarmonyPatch(typeof(TileManager), "Start")]
	[HarmonyPostfix]
	private static void TileManager_Start_Postfix()
	{
		CustomMapManager.ConsumeSelectionIfPerRun();
	}

	[HarmonyPatch(typeof(TileManager), nameof(TileManager.SpawnNewTile))]
	[HarmonyPrefix]
	private static void TileManager_SpawnNewTile_Prefix(TileManager __instance, int posX, int posY, int eulerAngle)
	{
		CustomMapManager.PrepareTilePoolForSpawn(__instance, posX, posY, eulerAngle);
	}

	[HarmonyPatch(typeof(TileManager), nameof(TileManager.SpawnNewTile))]
	[HarmonyPostfix]
	private static void TileManager_SpawnNewTile_Postfix(TileManager __instance)
	{
		CustomMapManager.RestoreOriginalArrays(__instance);
	}
}
