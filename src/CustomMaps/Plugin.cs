using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[assembly: AssemblyCompany("RogueTowerCustomMaps")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyDescription("Clean-room custom map generator for Rogue Tower with Play Menu preset cycling, non-destructive tile pool restoration, and a built-in In-Game Custom Map Sequence Builder UI. Inspired by acr-Rogue_Tower_Custom_Maps.")]
[assembly: AssemblyFileVersion("1.3.1.0")]
[assembly: AssemblyInformationalVersion("1.3.1")]
[assembly: AssemblyProduct("RogueTowerCustomMaps")]
[assembly: AssemblyTitle("RogueTowerCustomMaps")]
[assembly: AssemblyVersion("1.3.1.0")]

namespace RogueTowerCustomMaps;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
	public const string PluginGuid = "acr.rougetower.custommaps";
	public const string PluginName = "RogueTowerCustomMaps";
	public const string PluginVersion = "1.3.1";

	internal static Plugin Instance;
	internal static ManualLogSource Log;
	internal static ConfigEntry<bool> RememberMapAcrossRuns;
	internal static ConfigEntry<string> CustomMapCSV;

	private Harmony _harmony;

	public static string[] GetPresetNames() => CustomMapManager.GetPresetNames();
	public static int GetSelectedMapIndex() => CustomMapManager.SelectedMapIndex;
	public static void SetSelectedMapIndex(int index) => CustomMapManager.SetSelectedMapIndex(index);
	public static string GetCustomCsv() => CustomMapCSV != null ? CustomMapCSV.Value : "S, S, P";
	public static void SetCustomCsv(string csv)
	{
		if (CustomMapCSV != null)
		{
			CustomMapCSV.Value = CustomMapManager.NormalizeCsv(csv);
			Instance?.Config?.Save();
		}
	}
	public static void OpenCustomMapEditor() => CustomMapEditorUI.Open();

	public static string GetActiveRunMapName()
	{
		int idx = Mathf.Clamp(CustomMapManager.ActiveRunMapIndex, 0, CustomMapManager.Presets.Length - 1);
		return CustomMapManager.Presets[idx].Name;
	}

	private void Awake()
	{
		Instance = this;
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
		SceneManager.activeSceneChanged += OnActiveSceneChanged;

		Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
	}

	private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
	{
		CustomMapEditorUI.Close();
	}

	private void Update()
	{
		CustomMapEditorUI.Tick();
	}

	private void OnDestroy()
	{
		SceneManager.activeSceneChanged -= OnActiveSceneChanged;
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

	internal static int CustomCsvPresetIndex => Presets.Length - 1;
	internal static int SelectedMapIndex;
	internal static int ActiveRunMapIndex;

	private static readonly Queue<string> CurrentQueue = new Queue<string>();
	private static Text _menuButtonText;
	private static Image _menuButtonImage;
	private static Color _defaultButtonColor = Color.white;
	private static readonly Color ActiveHighlightColor = new Color(1.0f, 0.88f, 0.55f, 1.0f);

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

		Transform backBtnTr = scaler.Find("BackButton");
		if (backBtnTr == null)
		{
			Plugin.Log.LogWarning("Could not find BackButton under PlayMenu/Scaler to clone CustomMapSelectorButton.");
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
		rect.anchoredPosition = new Vector2(-179f, 180f);
		rect.sizeDelta = new Vector2(222f, 34f);
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
			_menuButtonText.fontSize = 112;
			_menuButtonText.alignment = TextAnchor.MiddleCenter;
			_menuButtonText.horizontalOverflow = HorizontalWrapMode.Overflow;
			_menuButtonText.verticalOverflow = VerticalWrapMode.Overflow;
		}

		// Companion "Edit" button for the In-Game Custom Map Sequence Builder
		Transform editExisting = scaler.Find("EditCustomMapButton");
		GameObject editObj;
		if (editExisting != null)
		{
			editObj = editExisting.gameObject;
		}
		else
		{
			editObj = UnityEngine.Object.Instantiate(backBtnTr.gameObject, scaler);
			editObj.name = "EditCustomMapButton";

			Button editBtn = editObj.GetComponent<Button>();
			editBtn.onClick = new Button.ButtonClickedEvent();
			editBtn.onClick.AddListener(() =>
			{
				SFXManager.instance?.ButtonClick();
				CustomMapEditorUI.Open();
			});
		}

		RectTransform editRt = editObj.GetComponent<RectTransform>();
		editRt.anchorMin = new Vector2(0.5f, 0.5f);
		editRt.anchorMax = new Vector2(0.5f, 0.5f);
		editRt.pivot = new Vector2(0.5f, 0.5f);
		editRt.anchoredPosition = new Vector2(-35f, 180f);
		editRt.sizeDelta = new Vector2(56f, 34f);
		editRt.localScale = Vector3.one;

		Text editText = editObj.GetComponentInChildren<Text>(includeInactive: true);
		if (editText != null)
		{
			editText.text = "Edit";
			editText.resizeTextForBestFit = false;
			editText.fontSize = 112;
			editText.alignment = TextAnchor.MiddleCenter;
			editText.horizontalOverflow = HorizontalWrapMode.Overflow;
			editText.verticalOverflow = VerticalWrapMode.Overflow;
		}

		UpdatePlayMenuButtonVisual();
	}

	internal static string NormalizeCsv(string csv)
	{
		if (string.IsNullOrWhiteSpace(csv))
		{
			return "S, S, P";
		}
		string[] tokens = csv.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length == 0)
		{
			return "P";
		}
		List<string> normalized = new List<string>(tokens.Length);
		for (int i = 0; i < tokens.Length; i++)
		{
			normalized.Add(tokens[i].Trim().ToUpperInvariant());
		}
		return string.Join(", ", normalized);
	}

	internal static List<string> ParseTokens(string csv)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrWhiteSpace(csv)) return list;
		string[] tokens = csv.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < tokens.Length; i++)
		{
			string t = tokens[i].Trim().ToUpperInvariant();
			if (!string.IsNullOrEmpty(t)) list.Add(t);
		}
		return list;
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
		SFXManager.instance?.ButtonClick();
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
		if (tm == null) return;

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
			List<string> tokens = ParseTokens(Plugin.CustomMapCSV.Value ?? "S, S, P");
			if (tokens.Count == 0)
			{
				CurrentQueue.Enqueue("P");
			}
			else
			{
				for (int i = 0; i < tokens.Count; i++)
				{
					CurrentQueue.Enqueue(tokens[i]);
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
		if (tm == null || !_hasSavedTiles) return;

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
		if (tm == null || !_hasSavedTiles) return;

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
			case 0: left = westFree; top = northFree; right = eastFree; break;
			case 90: left = northFree; top = eastFree; right = southFree; break;
			case 180: left = eastFree; top = southFree; right = westFree; break;
			case 270: left = southFree; top = westFree; right = northFree; break;
			default: return _origDeadEnd;
		}

		if (clockwise)
		{
			if (right && _origR.Length > 0) return _origR;
			if (top && _origT.Length > 0) return _origT;
			if (left && _origL.Length > 0) return _origL;
			return _origDeadEnd;
		}

		if (left && _origL.Length > 0) return _origL;
		if (top && _origT.Length > 0) return _origT;
		if (right && _origR.Length > 0) return _origR;
		return _origDeadEnd;
	}

	internal static bool IsKnownToken(string token)
	{
		switch ((token ?? string.Empty).ToUpperInvariant())
		{
			case "S":
			case "L":
			case "R":
			case "SL":
			case "LS":
			case "SR":
			case "RS":
			case "LR":
			case "RL":
			case "SLR":
			case "SRL":
			case "P":
				return true;
			default:
				return false;
		}
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

internal static class CustomMapEditorUI
{
	private static readonly Color BgWindow = new Color(0.086f, 0.098f, 0.133f, 0.97f);
	private static readonly Color BgHeader = new Color(0.118f, 0.133f, 0.180f, 1.0f);
	private static readonly Color BgCard = new Color(0.135f, 0.155f, 0.210f, 0.96f);
	private static readonly Color BgButton = new Color(0.180f, 0.205f, 0.275f, 1.0f);
	private static readonly Color BgInput = new Color(0.065f, 0.075f, 0.105f, 1.0f);
	private static readonly Color GoldAccent = new Color(0.851f, 0.702f, 0.424f, 1.0f);
	private static readonly Color GoldSoft = new Color(0.320f, 0.265f, 0.165f, 1.0f);
	private static readonly Color GreenOn = new Color(0.180f, 0.620f, 0.388f, 1.0f);
	private static readonly Color RedDanger = new Color(0.680f, 0.240f, 0.240f, 1.0f);
	private static readonly Color TextLight = new Color(0.945f, 0.945f, 0.960f, 1.0f);
	private static readonly Color TextMuted = new Color(0.675f, 0.705f, 0.775f, 1.0f);
	private static readonly Color BorderSubtle = new Color(0.28f, 0.31f, 0.40f, 0.85f);

	private static Font _fontRegular;
	private static Font _fontBold;
	private static GameObject _canvasObj;
	private static GameObject _windowObj;
	private static InputField _csvInput;
	private static Text _previewText;
	private static bool _isOpen;

	internal static void Open()
	{
		EnsureCreated();
		_isOpen = true;
		_windowObj.SetActive(true);
		if (_csvInput != null)
		{
			_csvInput.text = Plugin.CustomMapCSV != null ? Plugin.CustomMapCSV.Value : "S, S, P";
		}
		UpdatePreview();
	}

	internal static void Close()
	{
		_isOpen = false;
		if (_windowObj != null)
		{
			_windowObj.SetActive(false);
		}
	}

	internal static void Tick()
	{
		if (_isOpen && Input.GetKeyDown(KeyCode.Escape))
		{
			Close();
		}
	}

	private static void AppendToken(string token)
	{
		List<string> tokens = CustomMapManager.ParseTokens(_csvInput != null ? _csvInput.text : Plugin.CustomMapCSV.Value);
		tokens.Add(token);
		SetTokens(tokens);
	}

	private static void UndoToken()
	{
		List<string> tokens = CustomMapManager.ParseTokens(_csvInput != null ? _csvInput.text : string.Empty);
		if (tokens.Count > 0)
		{
			tokens.RemoveAt(tokens.Count - 1);
		}
		SetTokens(tokens);
	}

	private static void SetTokens(List<string> tokens)
	{
		string next = tokens != null && tokens.Count > 0 ? string.Join(", ", tokens) : string.Empty;
		if (_csvInput != null)
		{
			_csvInput.text = next;
		}
		UpdatePreview();
	}

	private static void CopySelectedPreset()
	{
		int idx = Mathf.Clamp(CustomMapManager.SelectedMapIndex, 0, CustomMapManager.Presets.Length - 1);
		MapPreset preset = CustomMapManager.Presets[idx];
		if (preset.Sequence != null && preset.Sequence.Length > 0)
		{
			SetTokens(new List<string>(preset.Sequence));
			return;
		}
		SetTokens(new List<string> { "S", "L", "SLR", "P", "P", "P" });
	}

	private static void UpdatePreview()
	{
		if (_previewText == null) return;
		string raw = _csvInput != null ? _csvInput.text : string.Empty;
		List<string> tokens = CustomMapManager.ParseTokens(raw);
		if (tokens.Count == 0)
		{
			_previewText.text = "<color=#9FA8BC>Empty sequence (falls back to P = Portal dead-end when spawning tiles).</color>";
			return;
		}

		int portals = 0;
		int unknown = 0;
		StringBuilder path = new StringBuilder();
		for (int i = 0; i < tokens.Count; i++)
		{
			string t = tokens[i];
			if (t == "P") portals++;
			if (!CustomMapManager.IsKnownToken(t)) unknown++;
			if (i > 0) path.Append(" -> ");
			if (i < 16)
			{
				path.Append(CustomMapManager.IsKnownToken(t) ? t : $"<color=#E66B6B>{t}?</color>");
			}
		}
		if (tokens.Count > 16) path.Append($" -> ... (+{tokens.Count - 16} more)");

		string warn = unknown > 0 ? $"  •  <color=#E66B6B>{unknown} unknown token(s)</color>" : "";
		_previewText.text =
			$"<b>Planned Tiles:</b> {tokens.Count}   •   <b>Explicit Portals (P):</b> {portals}{warn}\n" +
			$"<color=#D9B36C>{path}</color>\n" +
			$"<color=#9FA8BC>After the sequence finishes, remaining open ends spawn Portals (P). Click tiles below to append.</color>";
	}

	private static void SaveAndSelectCustomMap()
	{
		string normalized = CustomMapManager.NormalizeCsv(_csvInput != null ? _csvInput.text : "S, S, P");
		Plugin.SetCustomCsv(normalized);
		CustomMapManager.SetSelectedMapIndex(CustomMapManager.CustomCsvPresetIndex);
		SFXManager.instance?.ButtonClick();
		Close();
	}

	private static void EnsureCreated()
	{
		if (_canvasObj != null) return;
		ResolveFont();

		_canvasObj = new GameObject("CustomMapsEditorCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
		UnityEngine.Object.DontDestroyOnLoad(_canvasObj);

		Canvas canvas = _canvasObj.GetComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 510;
		canvas.pixelPerfect = true;

		CanvasScaler scaler = _canvasObj.GetComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1280f, 720f);
		scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		scaler.matchWidthOrHeight = 0.5f;

		_windowObj = CreatePanel("CustomMapEditorWindow", _canvasObj.transform, BgWindow);
		RectTransform winRt = _windowObj.GetComponent<RectTransform>();
		winRt.anchorMin = new Vector2(0.5f, 0.5f);
		winRt.anchorMax = new Vector2(0.5f, 0.5f);
		winRt.pivot = new Vector2(0.5f, 0.5f);
		winRt.anchoredPosition = Vector2.zero;
		winRt.sizeDelta = new Vector2(640f, 440f);

		Outline ol = _windowObj.AddComponent<Outline>();
		ol.effectColor = GoldAccent;
		ol.effectDistance = new Vector2(2f, -2f);

		// Header
		GameObject header = CreatePanel("Header", winRt, BgHeader);
		RectTransform hdrRt = header.GetComponent<RectTransform>();
		hdrRt.anchorMin = new Vector2(0f, 1f);
		hdrRt.anchorMax = new Vector2(1f, 1f);
		hdrRt.pivot = new Vector2(0.5f, 1f);
		hdrRt.anchoredPosition = Vector2.zero;
		hdrRt.sizeDelta = new Vector2(0f, 42f);

		Text hdrTxt = CreateText(hdrRt, "In-Game Custom Map Sequence Builder", 16, FontStyle.Bold, GoldAccent, TextAnchor.MiddleLeft);
		hdrTxt.rectTransform.anchorMin = Vector2.zero;
		hdrTxt.rectTransform.anchorMax = Vector2.one;
		hdrTxt.rectTransform.offsetMin = new Vector2(14f, 0f);
		hdrTxt.rectTransform.offsetMax = new Vector2(-46f, 0f);

		Button closeBtn = CreateSimpleButton(hdrRt, "X", RedDanger, TextLight, 14, Close);
		RectTransform cRt = closeBtn.GetComponent<RectTransform>();
		cRt.anchorMin = new Vector2(1f, 0.5f);
		cRt.anchorMax = new Vector2(1f, 0.5f);
		cRt.pivot = new Vector2(1f, 0.5f);
		cRt.anchoredPosition = new Vector2(-10f, 0f);
		cRt.sizeDelta = new Vector2(28f, 26f);

		// Body container
		GameObject body = CreatePanel("Body", winRt, Color.clear);
		RectTransform bodyRt = body.GetComponent<RectTransform>();
		bodyRt.anchorMin = Vector2.zero;
		bodyRt.anchorMax = Vector2.one;
		bodyRt.offsetMin = new Vector2(14f, 14f);
		bodyRt.offsetMax = new Vector2(-14f, -50f);

		VerticalLayoutGroup vlg = body.AddComponent<VerticalLayoutGroup>();
		vlg.spacing = 8f;
		vlg.childControlWidth = true;
		vlg.childControlHeight = true;
		vlg.childForceExpandWidth = true;
		vlg.childForceExpandHeight = false;

		// Tile palette
		AddSectionLabel(body.transform, "CLICK TILES TO APPEND TO QUEUE");
		Transform grid = AddGrid(body.transform, 4, 148f, 34f, 6f);
		CreateSimpleButton(grid, "+ Straight (S)", BgButton, TextLight, 13, () => AppendToken("S"));
		CreateSimpleButton(grid, "+ Left (L)", BgButton, TextLight, 13, () => AppendToken("L"));
		CreateSimpleButton(grid, "+ Right (R)", BgButton, TextLight, 13, () => AppendToken("R"));
		CreateSimpleButton(grid, "+ Portal (P)", GoldSoft, GoldAccent, 13, () => AppendToken("P"));
		CreateSimpleButton(grid, "+ Split L+R (LR)", BgButton, TextLight, 13, () => AppendToken("LR"));
		CreateSimpleButton(grid, "+ Split S+L (SL)", BgButton, TextLight, 13, () => AppendToken("SL"));
		CreateSimpleButton(grid, "+ Split S+R (SR)", BgButton, TextLight, 13, () => AppendToken("SR"));
		CreateSimpleButton(grid, "+ 3-Way (SLR)", BgButton, GoldAccent, 13, () => AppendToken("SLR"));

		// Edit tools row
		GameObject editRow = CreatePanel("EditRow", body.transform, Color.clear);
		editRow.AddComponent<LayoutElement>().preferredHeight = 32f;
		HorizontalLayoutGroup ehlg = editRow.AddComponent<HorizontalLayoutGroup>();
		ehlg.spacing = 6f;
		ehlg.childControlWidth = true;
		ehlg.childControlHeight = true;
		ehlg.childForceExpandWidth = true;

		CreateSimpleButton(editRow.transform, "Undo Last", BgButton, TextLight, 13, UndoToken);
		CreateSimpleButton(editRow.transform, "Clear All", BgButton, TextLight, 13, () => SetTokens(new List<string>()));
		CreateSimpleButton(editRow.transform, "Copy Selected Preset", BgButton, TextLight, 13, CopySelectedPreset);
		CreateSimpleButton(editRow.transform, "Default (S, S, P)", BgButton, TextLight, 13, () => SetTokens(new List<string> { "S", "S", "P" }));

		// CSV Input
		AddSectionLabel(body.transform, "CUSTOM TILE SEQUENCE CSV (EDITABLE)");
		GameObject inputBg = CreatePanel("CsvInputBox", body.transform, BgInput);
		inputBg.AddComponent<LayoutElement>().preferredHeight = 36f;
		Outline iOl = inputBg.AddComponent<Outline>();
		iOl.effectColor = BorderSubtle;
		iOl.effectDistance = new Vector2(1f, -1f);

		_csvInput = inputBg.AddComponent<InputField>();
		Text textComp = CreateText(inputBg.transform, "", 14, FontStyle.Bold, TextLight, TextAnchor.MiddleLeft);
		StretchFill(textComp.rectTransform, 10f, 4f, 10f, 4f);
		Text phComp = CreateText(inputBg.transform, "e.g. S, L, SLR, P, P, P", 13, FontStyle.Italic, TextMuted, TextAnchor.MiddleLeft);
		StretchFill(phComp.rectTransform, 10f, 4f, 10f, 4f);
		_csvInput.textComponent = textComp;
		_csvInput.placeholder = phComp;
		_csvInput.onValueChanged.AddListener(_ => UpdatePreview());

		// Preview box
		GameObject prevBox = CreatePanel("PreviewBox", body.transform, BgCard);
		prevBox.AddComponent<LayoutElement>().preferredHeight = 96f;
		Outline pOl = prevBox.AddComponent<Outline>();
		pOl.effectColor = BorderSubtle;
		pOl.effectDistance = new Vector2(1f, -1f);

		_previewText = CreateText(prevBox.transform, "", 13, FontStyle.Normal, TextLight, TextAnchor.UpperLeft);
		_previewText.supportRichText = true;
		StretchFill(_previewText.rectTransform, 10f, 8f, 10f, 8f);

		// Footer buttons
		GameObject footerRow = CreatePanel("FooterRow", body.transform, Color.clear);
		footerRow.AddComponent<LayoutElement>().preferredHeight = 36f;
		HorizontalLayoutGroup fhlg = footerRow.AddComponent<HorizontalLayoutGroup>();
		fhlg.spacing = 8f;
		fhlg.childControlWidth = true;
		fhlg.childControlHeight = true;
		fhlg.childForceExpandWidth = false;

		Button saveBtn = CreateSimpleButton(footerRow.transform, "Save & Select Custom (CSV)", GreenOn, TextLight, 14, SaveAndSelectCustomMap);
		saveBtn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

		Button cancelBtn = CreateSimpleButton(footerRow.transform, "Close", BgButton, TextLight, 13, Close);
		cancelBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;

		_windowObj.SetActive(false);
	}

	private static void AddSectionLabel(Transform parent, string text)
	{
		GameObject obj = CreatePanel("Label", parent, Color.clear);
		obj.AddComponent<LayoutElement>().preferredHeight = 22f;
		Text txt = CreateText(obj.transform, text, 13, FontStyle.Bold, GoldAccent, TextAnchor.LowerLeft);
		StretchFill(txt.rectTransform);
	}

	private static Transform AddGrid(Transform parent, int cols, float w, float h, float spacing)
	{
		GameObject gridObj = CreatePanel("Grid", parent, Color.clear);
		GridLayoutGroup glg = gridObj.AddComponent<GridLayoutGroup>();
		glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		glg.constraintCount = cols;
		glg.cellSize = new Vector2(w, h);
		glg.spacing = new Vector2(spacing, spacing);
		glg.childAlignment = TextAnchor.UpperLeft;
		return gridObj.transform;
	}

	private static void ResolveFont()
	{
		if (_fontRegular != null) return;
		try
		{
			_fontRegular = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Calibri", "Verdana", "Arial" }, 16);
			_fontBold = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Semibold", "Segoe UI Bold", "Calibri Bold", "Arial Bold" }, 16);
		}
		catch
		{
		}
		if (_fontRegular == null) _fontRegular = Resources.GetBuiltinResource<Font>("Arial.ttf");
		if (_fontBold == null) _fontBold = _fontRegular;
	}

	private static GameObject CreatePanel(string name, Transform parent, Color color)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		obj.transform.SetParent(parent, worldPositionStays: false);
		Image img = obj.GetComponent<Image>();
		img.color = color;
		img.raycastTarget = color.a > 0.005f;
		return obj;
	}

	private static Button CreateSimpleButton(Transform parent, string text, Color bgColor, Color textColor, int fontSize, Action onClick)
	{
		GameObject obj = CreatePanel("Btn_" + text, parent, bgColor);
		Button btn = obj.AddComponent<Button>();
		ColorBlock cb = btn.colors;
		cb.normalColor = Color.white;
		cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
		cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
		btn.colors = cb;
		btn.navigation = new Navigation { mode = Navigation.Mode.None };
		if (onClick != null) btn.onClick.AddListener(() => onClick());
		Text txt = CreateText(obj.transform, text, fontSize, FontStyle.Bold, textColor, TextAnchor.MiddleCenter);
		StretchFill(txt.rectTransform, 4f, 2f, 4f, 2f);
		return btn;
	}

	private static Text CreateText(Transform parent, string text, int fontSize, FontStyle style, Color color, TextAnchor align)
	{
		GameObject obj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
		obj.transform.SetParent(parent, worldPositionStays: false);
		Text t = obj.GetComponent<Text>();
		bool useDedicatedBold = style == FontStyle.Bold && _fontBold != null && _fontBold != _fontRegular;
		t.font = useDedicatedBold ? _fontBold : _fontRegular;
		t.text = text;
		t.fontSize = fontSize;
		t.fontStyle = useDedicatedBold ? FontStyle.Normal : style;
		t.color = color;
		t.alignment = align;
		t.horizontalOverflow = HorizontalWrapMode.Wrap;
		t.verticalOverflow = VerticalWrapMode.Truncate;
		t.raycastTarget = false;
		return t;
	}

	private static void StretchFill(RectTransform rt, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
	{
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.offsetMin = new Vector2(left, bottom);
		rt.offsetMax = new Vector2(-right, -top);
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
