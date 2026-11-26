using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RogueTowerCheats;

internal static class StandaloneCheatInputPatches
{
	[HarmonyPatch(typeof(UIManager), "Update")]
	[HarmonyPrefix]
	private static bool UIManager_Update_Prefix() => !CheatTabsUI.IsPointerOverStandalone;

	[HarmonyPatch(typeof(BuildingManager), "Update")]
	[HarmonyPrefix]
	private static bool BuildingManager_Update_Prefix() => !(CheatTabsUI.IsPointerOverStandalone || CheatTabsUI.IsStandaloneSearchFocused);

	[HarmonyPatch(typeof(CameraController), "UpdateZoom")]
	[HarmonyPrefix]
	private static bool CameraController_UpdateZoom_Prefix() => !CheatTabsUI.IsPointerOverStandalone;

	[HarmonyPatch(typeof(CameraController), "UpdateMovement")]
	[HarmonyPrefix]
	private static bool CameraController_UpdateMovement_Prefix() => !(CheatTabsUI.IsStandaloneSearchFocused || CheatTabsUI.IsPointerOverStandalone);

	[HarmonyPatch(typeof(PauseMenu), "Update")]
	[HarmonyPrefix]
	private static bool PauseMenu_Update_Prefix()
	{
		if (CheatTabsUI.IsStandaloneSearchFocused)
		{
			if (Input.GetKeyDown(KeyCode.Escape)) EventSystem.current?.SetSelectedGameObject(null);
			return false;
		}
		if (CheatTabsUI.IsStandaloneOpen && Input.GetKeyDown(KeyCode.Escape))
		{
			CheatTabsUI.CloseStandalone();
			return false;
		}
		return true;
	}
}

internal static class CheatTabsUI
{
	private static readonly Color BgWindow = new Color(0.086f, 0.098f, 0.133f, 0.96f);
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

	// State shared across embedded ModMenu tabs & Standalone window
	private static InputField _searchInput;
	private static string _searchQuery = string.Empty;
	private static CardFilterCategory _cardFilter = CardFilterCategory.All;
	private static readonly HashSet<string> ExpandedTrees = new HashSet<string>();
	private static Transform _cardsListContainer;
	private static RectTransform _activeContentRoot;

	// Standalone fallback window state (when RogueTowerModMenu is not installed)
	private static GameObject _standaloneCanvas;
	private static GameObject _standaloneWin;
	private static RectTransform _standaloneWinRt;
	private static GameObject _standaloneBtn;
	private static RectTransform _standaloneBtnRt;
	private static RectTransform _standaloneContentRt;
	private static ScrollRect _standaloneScroll;
	private static Text _standaloneToast;
	private static float _standaloneToastUntil;
	private static int _standaloneTab;
	private static bool _standaloneOpen;
	private static readonly Button[] _standaloneTabBtns = new Button[4];
	private static readonly Image[] _standaloneTabImgs = new Image[4];
	private static readonly Text[] _standaloneTabTxts = new Text[4];
	private static readonly List<Action> _standaloneLiveSync = new List<Action>();

	internal static bool IsStandaloneOpen => _standaloneOpen && _standaloneWin != null && _standaloneWin.activeInHierarchy;

	internal static bool IsStandaloneSearchFocused =>
		IsStandaloneOpen &&
		_searchInput != null &&
		EventSystem.current != null &&
		EventSystem.current.currentSelectedGameObject == _searchInput.gameObject;

	internal static bool IsPointerOverStandalone
	{
		get
		{
			Vector2 mouse = Input.mousePosition;
			if (IsStandaloneOpen && _standaloneWinRt != null && RectTransformUtility.RectangleContainsScreenPoint(_standaloneWinRt, mouse, null)) return true;
			if (_standaloneBtn != null && _standaloneBtn.activeInHierarchy && _standaloneBtnRt != null && RectTransformUtility.RectangleContainsScreenPoint(_standaloneBtnRt, mouse, null)) return true;
			return false;
		}
	}

	internal static void OnSceneChanged()
	{
		if (IsStandaloneOpen) RebuildStandaloneTab();
	}

	internal static void RefreshOpenUi()
	{
		if (CheatBridge.HasModMenu && CheatBridge.IsModMenuOpen()) CheatBridge.RefreshModMenuTab();
		if (IsStandaloneOpen) RebuildStandaloneTab();
	}

	internal static void CloseStandalone()
	{
		_standaloneOpen = false;
		if (_standaloneWin != null) _standaloneWin.SetActive(false);
	}

	internal static void ToggleStandalone()
	{
		EnsureStandaloneCreatedIfNeeded(forceCreateWindow: true);
		_standaloneOpen = !_standaloneOpen;
		if (_standaloneWin != null) _standaloneWin.SetActive(_standaloneOpen);
		if (_standaloneOpen) RebuildStandaloneTab();
	}

	internal static void EnsureStandaloneCreatedIfNeeded(bool forceCreateWindow = false)
	{
		bool wantBtn = !CheatBridge.HasModMenu || Plugin.ShowStandaloneButton.Value;
		if (!wantBtn && !forceCreateWindow) return;
		if (_standaloneCanvas != null) return;

		ResolveFonts();
		_standaloneCanvas = new GameObject("RogueTowerCheatsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
		UnityEngine.Object.DontDestroyOnLoad(_standaloneCanvas);

		Canvas canvas = _standaloneCanvas.GetComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 505;
		canvas.pixelPerfect = true;

		CanvasScaler scaler = _standaloneCanvas.GetComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1280f, 720f);
		scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		scaler.matchWidthOrHeight = 0.5f;

		_standaloneBtn = CreatePanel("CheatsCornerButton", _standaloneCanvas.transform, BgHeader);
		_standaloneBtnRt = _standaloneBtn.GetComponent<RectTransform>();
		_standaloneBtnRt.anchorMin = new Vector2(1f, 1f);
		_standaloneBtnRt.anchorMax = new Vector2(1f, 1f);
		_standaloneBtnRt.pivot = new Vector2(1f, 1f);
		_standaloneBtnRt.anchoredPosition = new Vector2(-18f, CheatBridge.HasModMenu ? -98f : -68f);
		_standaloneBtnRt.sizeDelta = new Vector2(78f, 26f);
		Outline ol = _standaloneBtn.AddComponent<Outline>();
		ol.effectColor = GoldAccent;
		ol.effectDistance = new Vector2(1f, -1f);
		Button cb = _standaloneBtn.AddComponent<Button>();
		cb.onClick.AddListener(() =>
		{
			if (CheatBridge.HasModMenu) CheatBridge.OpenMenuToTab("cheats");
			else ToggleStandalone();
		});
		Text ct = CreateText(_standaloneBtn.transform, "Cheats", 12, FontStyle.Bold, GoldAccent, TextAnchor.MiddleCenter);
		StretchFill(ct.rectTransform);
		_standaloneBtn.SetActive(wantBtn);

		CreateStandaloneWindow();
		_standaloneWin.SetActive(false);
	}

	private static void CreateStandaloneWindow()
	{
		_standaloneWin = CreatePanel("RogueTowerCheatsWindow", _standaloneCanvas.transform, BgWindow);
		_standaloneWinRt = _standaloneWin.GetComponent<RectTransform>();
		_standaloneWinRt.anchorMin = new Vector2(0.5f, 0.5f);
		_standaloneWinRt.anchorMax = new Vector2(0.5f, 0.5f);
		_standaloneWinRt.pivot = new Vector2(0.5f, 0.5f);
		_standaloneWinRt.sizeDelta = new Vector2(780f, 520f);
		_standaloneWinRt.anchoredPosition = Vector2.zero;

		Outline border = _standaloneWin.AddComponent<Outline>();
		border.effectColor = GoldAccent;
		border.effectDistance = new Vector2(2f, -2f);

		GameObject header = CreatePanel("Header", _standaloneWinRt, BgHeader);
		RectTransform hdrRt = header.GetComponent<RectTransform>();
		hdrRt.anchorMin = new Vector2(0f, 1f);
		hdrRt.anchorMax = new Vector2(1f, 1f);
		hdrRt.pivot = new Vector2(0.5f, 1f);
		hdrRt.anchoredPosition = Vector2.zero;
		hdrRt.sizeDelta = new Vector2(0f, 44f);
		Text title = CreateText(hdrRt, "Rogue Tower Cheats & Sandbox (Standalone)", 16, FontStyle.Bold, GoldAccent, TextAnchor.MiddleLeft);
		title.rectTransform.anchorMin = Vector2.zero;
		title.rectTransform.anchorMax = Vector2.one;
		title.rectTransform.offsetMin = new Vector2(14f, 0f);
		title.rectTransform.offsetMax = new Vector2(-50f, 0f);

		Button closeBtn = CreateSimpleButton(hdrRt, "X", RedDanger, TextLight, 14, CloseStandalone);
		RectTransform xRt = closeBtn.GetComponent<RectTransform>();
		xRt.anchorMin = new Vector2(1f, 0.5f);
		xRt.anchorMax = new Vector2(1f, 0.5f);
		xRt.pivot = new Vector2(1f, 0.5f);
		xRt.anchoredPosition = new Vector2(-10f, 0f);
		xRt.sizeDelta = new Vector2(30f, 28f);

		GameObject tabBar = CreatePanel("TabBar", _standaloneWinRt, new Color(0.10f, 0.115f, 0.155f, 1f));
		RectTransform tabRt = tabBar.GetComponent<RectTransform>();
		tabRt.anchorMin = new Vector2(0f, 1f);
		tabRt.anchorMax = new Vector2(1f, 1f);
		tabRt.pivot = new Vector2(0.5f, 1f);
		tabRt.anchoredPosition = new Vector2(0f, -44f);
		tabRt.sizeDelta = new Vector2(0f, 36f);
		HorizontalLayoutGroup hlg = tabBar.AddComponent<HorizontalLayoutGroup>();
		hlg.padding = new RectOffset(10, 10, 4, 4);
		hlg.spacing = 6f;
		hlg.childControlWidth = true;
		hlg.childControlHeight = true;
		hlg.childForceExpandWidth = true;

		string[] names = { "Cheats", "Towers", "Cards", "Card Trees" };
		for (int i = 0; i < 4; i++)
		{
			int idx = i;
			Button tb = CreateSimpleButton(tabRt, names[i], BgButton, TextLight, 13, () => { _standaloneTab = idx; RebuildStandaloneTab(); });
			_standaloneTabBtns[i] = tb;
			_standaloneTabImgs[i] = tb.GetComponent<Image>();
			_standaloneTabTxts[i] = tb.GetComponentInChildren<Text>();
		}

		GameObject scrollObj = new GameObject("ScrollBody", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		scrollObj.transform.SetParent(_standaloneWinRt, worldPositionStays: false);
		RectTransform scrollRt = scrollObj.GetComponent<RectTransform>();
		scrollRt.anchorMin = Vector2.zero;
		scrollRt.anchorMax = Vector2.one;
		scrollRt.offsetMin = new Vector2(10f, 30f);
		scrollRt.offsetMax = new Vector2(-12f, -84f);
		scrollObj.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
		scrollObj.GetComponent<Mask>().showMaskGraphic = false;

		GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
		contentObj.transform.SetParent(scrollRt, worldPositionStays: false);
		_standaloneContentRt = contentObj.GetComponent<RectTransform>();
		_standaloneContentRt.anchorMin = new Vector2(0f, 1f);
		_standaloneContentRt.anchorMax = new Vector2(1f, 1f);
		_standaloneContentRt.pivot = new Vector2(0.5f, 1f);
		_standaloneContentRt.anchoredPosition = Vector2.zero;
		_standaloneContentRt.sizeDelta = Vector2.zero;
		VerticalLayoutGroup vlg = contentObj.GetComponent<VerticalLayoutGroup>();
		vlg.padding = new RectOffset(4, 8, 6, 12);
		vlg.spacing = 8f;
		vlg.childControlWidth = true;
		vlg.childControlHeight = true;
		vlg.childForceExpandWidth = true;
		vlg.childForceExpandHeight = false;
		contentObj.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		_standaloneScroll = scrollObj.GetComponent<ScrollRect>();
		_standaloneScroll.viewport = scrollRt;
		_standaloneScroll.content = _standaloneContentRt;
		_standaloneScroll.horizontal = false;
		_standaloneScroll.vertical = true;
		_standaloneScroll.movementType = ScrollRect.MovementType.Clamped;
		_standaloneScroll.scrollSensitivity = 38f;

		GameObject footer = CreatePanel("Footer", _standaloneWinRt, BgHeader);
		RectTransform fRt = footer.GetComponent<RectTransform>();
		fRt.anchorMin = Vector2.zero;
		fRt.anchorMax = new Vector2(1f, 0f);
		fRt.pivot = new Vector2(0.5f, 0f);
		fRt.anchoredPosition = Vector2.zero;
		fRt.sizeDelta = new Vector2(0f, 28f);
		_standaloneToast = CreateText(fRt, $"Hotkey: [{Plugin.CheatHotkey.Value}]  •  Esc to close", 12, FontStyle.Normal, TextMuted, TextAnchor.MiddleLeft);
		StretchFill(_standaloneToast.rectTransform, 12f, 0f, 12f, 0f);
	}

	private static void RebuildStandaloneTab()
	{
		if (_standaloneContentRt == null) return;
		_standaloneLiveSync.Clear();
		for (int i = 0; i < 4; i++)
		{
			bool act = i == _standaloneTab;
			if (_standaloneTabImgs[i] != null) _standaloneTabImgs[i].color = act ? GoldAccent : BgButton;
			if (_standaloneTabTxts[i] != null)
			{
				_standaloneTabTxts[i].color = act ? new Color(0.08f, 0.09f, 0.12f, 1f) : TextLight;
				_standaloneTabTxts[i].font = act ? _fontBold : _fontRegular;
				_standaloneTabTxts[i].fontStyle = FontStyle.Normal;
			}
		}
		for (int i = _standaloneContentRt.childCount - 1; i >= 0; i--)
		{
			Transform child = _standaloneContentRt.GetChild(i);
			child.gameObject.SetActive(false);
			child.SetParent(null, worldPositionStays: false);
			UnityEngine.Object.Destroy(child.gameObject);
		}
		switch (_standaloneTab)
		{
			case 0: BuildCheatsTab(_standaloneContentRt); break;
			case 1: BuildTowersTab(_standaloneContentRt); break;
			case 2: BuildCardsTab(_standaloneContentRt); break;
			case 3: BuildCardTreesTab(_standaloneContentRt); break;
		}
		Canvas.ForceUpdateCanvases();
		LayoutRebuilder.ForceRebuildLayoutImmediate(_standaloneContentRt);
		if (_standaloneScroll != null) _standaloneScroll.verticalNormalizedPosition = 1f;
	}

	internal static void Tick()
	{
		if (_standaloneBtn != null)
		{
			bool show = !CheatBridge.HasModMenu || Plugin.ShowStandaloneButton.Value;
			if (_standaloneBtn.activeSelf != show) _standaloneBtn.SetActive(show);
		}
		if (IsStandaloneOpen && _standaloneToastUntil > 0f && Time.unscaledTime > _standaloneToastUntil && _standaloneToast != null)
		{
			_standaloneToastUntil = 0f;
			_standaloneToast.color = TextMuted;
			_standaloneToast.text = $"Hotkey: [{Plugin.CheatHotkey.Value}]  •  Esc to close";
		}
	}

	private static void ShowToast(string msg)
	{
		if (CheatBridge.HasModMenu && CheatBridge.IsModMenuOpen()) CheatBridge.ShowModMenuToast(msg);
		if (_standaloneToast != null)
		{
			_standaloneToast.color = GoldAccent;
			_standaloneToast.text = msg;
			_standaloneToastUntil = Time.unscaledTime + 2.6f;
		}
	}

	private static void RequestRebuild()
	{
		if (IsStandaloneOpen) RebuildStandaloneTab();
		else if (CheatBridge.HasModMenu && CheatBridge.IsModMenuOpen()) CheatBridge.RefreshModMenuTab();
	}

	private static void RegisterSync(Action sync)
	{
		if (IsStandaloneOpen) _standaloneLiveSync.Add(sync);
		else if (CheatBridge.HasModMenu) CheatBridge.RegisterModMenuLiveSync(sync);
	}

	private static void RequireRun(Action action)
	{
		if (!CheatActions.IsInGameRun) { ShowToast("Start a run first to use this cheat action."); return; }
		action();
	}

	// ======================== EMBEDDABLE CHEATS TAB ========================
	internal static void BuildCheatsTab(RectTransform parent)
	{
		ResolveFonts();
		_activeContentRoot = parent;
		_searchInput = null;

		AddSectionHeader(parent, "Core Sandbox Cheats (RogueTowerCheats v1.0)");
		Transform grid = AddGrid(parent, 2, 356f, 34f, 8f);

		AddToggleTile(grid, "Infinite Money", () => Plugin.InfiniteMoney.Value, v =>
		{
			Plugin.InfiniteMoney.Value = v;
			if (v && CheatActions.IsInGameRun && CheatActions.GetCurrentGold() < 50000) CheatActions.AddGold(50000);
			ShowToast($"Infinite Money: {(v ? "ON" : "OFF")}");
		});
		AddToggleTile(grid, "Infinite Mana", () => Plugin.InfiniteMana.Value, v =>
		{
			Plugin.InfiniteMana.Value = v;
			if (v) CheatActions.FillMana();
			ShowToast($"Infinite Mana: {(v ? "ON" : "OFF")}");
		});
		AddToggleTile(grid, "Infinite Range", () => Plugin.InfiniteRange.Value, v =>
		{
			Plugin.InfiniteRange.Value = v;
			CheatActions.RefreshAllPlacedTowers();
			ShowToast($"Infinite Range: {(v ? "ON (250)" : "OFF")}");
		});
		AddToggleTile(grid, "God Mode (Invincible Tower)", () => Plugin.GodMode.Value, v =>
		{
			Plugin.GodMode.Value = v;
			if (v) CheatActions.HealTowerFull();
			ShowToast($"God Mode: {(v ? "ON" : "OFF")}");
		});
		AddToggleTile(grid, "Infinite Card Banishes", () => Plugin.InfiniteBanishes.Value, v =>
		{
			Plugin.InfiniteBanishes.Value = v;
			CheatBridge.SyncBanishOverride(v);
			ShowToast($"Infinite Banishes: {(v ? "ON" : "OFF")}");
		});
		AddToggleTile(grid, "Include Unresearched Cards", () => Plugin.IncludeUnresearchedCards.Value, v =>
		{
			Plugin.IncludeUnresearchedCards.Value = v;
			ShowToast($"Include Unresearched: {(v ? "ON" : "OFF")}");
		});
		AddToggleTile(grid, "No Tower Price Scaling", () => Plugin.NoTowerPriceScaling.Value, v =>
		{
			Plugin.NoTowerPriceScaling.Value = v;
			CheatActions.RefreshTowerPrices();
			ShowToast($"No Tower Price Scaling: {(v ? "ON (flat base price)" : "OFF")}");
		});

		float[] dmgOpts = { 1f, 2f, 5f, 10f, 100f };
		AddCycleTile(grid, "Tower Damage Multiplier", () => $"{Plugin.DamageMultiplier.Value:0.#}x", () =>
		{
			Plugin.DamageMultiplier.Value = NextInCycle(dmgOpts, Plugin.DamageMultiplier.Value);
			CheatActions.RefreshAllPlacedTowers();
			ShowToast($"Tower Damage Multiplier: {Plugin.DamageMultiplier.Value:0.#}x");
		});

		float[] rpmOpts = { 1f, 2f, 3f, 5f };
		AddCycleTile(grid, "Tower Fire Rate Multiplier", () => $"{Plugin.FireRateMultiplier.Value:0.#}x", () =>
		{
			Plugin.FireRateMultiplier.Value = NextInCycle(rpmOpts, Plugin.FireRateMultiplier.Value);
			CheatActions.RefreshAllPlacedTowers();
			ShowToast($"Tower Fire Rate Multiplier: {Plugin.FireRateMultiplier.Value:0.#}x");
		});

		AddSectionHeader(parent, "Quick Resources & Grants");
		AddActionRow(parent, "Gold", new (string, Action)[]
		{
			("+1,000g", () => RequireRun(() => { CheatActions.AddGold(1000); ShowToast("+1,000 Gold"); })),
			("+10,000g", () => RequireRun(() => { CheatActions.AddGold(10000); ShowToast("+10,000 Gold"); })),
			("+100,000g", () => RequireRun(() => { CheatActions.AddGold(100000); ShowToast("+100,000 Gold"); }))
		});
		AddActionRow(parent, "Mana", new (string, Action)[]
		{
			("Fill Mana", () => RequireRun(() => { CheatActions.FillMana(); ShowToast("Mana filled"); })),
			("+100 Max Mana", () => RequireRun(() => { CheatActions.AddMaxMana(100); ShowToast("+100 Max Mana"); })),
			("+10 Mana/s", () => RequireRun(() => { CheatActions.AddManaRegen(10); ShowToast("+10 Mana/s"); }))
		});
		AddActionRow(parent, "Tower Health", new (string, Action)[]
		{
			("Full Repair", () => RequireRun(() => { CheatActions.HealTowerFull(); ShowToast("Tower fully repaired"); })),
			("+10 Max HP", () => RequireRun(() => { CheatActions.AddMaxTowerHealth(10); ShowToast("+10 Max Tower HP"); })),
			("+50 Max HP", () => RequireRun(() => { CheatActions.AddMaxTowerHealth(50); ShowToast("+50 Max Tower HP"); }))
		});
		AddActionRow(parent, "Meta Research XP", new (string, Action)[]
		{
			("+1,000 XP", () => { CheatActions.AddMetaXP(1000); ShowToast("+1,000 Meta XP"); }),
			("+25,000 XP", () => { CheatActions.AddMetaXP(25000); ShowToast("+25,000 Meta XP"); }),
			("+100,000 XP", () => { CheatActions.AddMetaXP(100000); ShowToast("+100,000 Meta XP"); })
		});

		AddSectionHeader(parent, "Run & Combat Actions");
		AddActionRow(parent, "Wave & Cards", new (string, Action)[]
		{
			("Draw 3 Cards", () => RequireRun(() => { CheatActions.DrawCardsNow(3); ShowToast("Drawing 3 cards"); })),
			("Open Chests", () => RequireRun(() => { int n = CheatActions.OpenAllChestsCheat(); ShowToast(n > 0 ? $"Opened {n} chest(s)" : "No chests on map"); })),
			("Chop Trees", () => RequireRun(() => { int n = CheatActions.ChopAllTreesCheat(); ShowToast(n > 0 ? $"Chopped {n} tree(s)" : "No trees on map"); })),
			("Kill All Enemies", () => RequireRun(() => { int n = CheatActions.KillAllEnemies(); ShowToast($"Killed {n} enemies"); }))
		});
		AddActionRow(parent, "Placed Towers", new (string, Action)[]
		{
			("Upgrade All Placed Towers (+10 H/A/S)", () => RequireRun(() =>
			{
				int n = CheatActions.UpgradeAllPlacedTowers(10);
				ShowToast(n > 0 ? $"Upgraded {n} placed tower(s) (+10 H/A/S)" : "No towers placed yet (or need gold)");
			}))
		});

		AddSectionHeader(parent, "Endgame Sandbox Stress Scenes (Round 40 / 45)");
		AddActionRow(parent, "Load Scene", new (string, Action)[]
		{
			("R40 Avg (8p)", () => { BenchmarkSceneRunner.StartBenchmark(Plugin.Instance, "r40_vanilla_avg", 12f, 2f, true); ShowToast("Loading R40 Avg (8p) scene..."); }),
			("R40 High (13p)", () => { BenchmarkSceneRunner.StartBenchmark(Plugin.Instance, "r40_high_12p", 12f, 2f, true); ShowToast("Loading R40 High-Roll (13p) scene..."); }),
			("R40 Split (21p)", () => { BenchmarkSceneRunner.StartBenchmark(Plugin.Instance, "r40_extreme_20p", 12f, 2f, true); ShowToast("Loading R40 Split-Mode (21p) scene..."); }),
			("R45 Boss 3x", () => { BenchmarkSceneRunner.StartBenchmark(Plugin.Instance, "r45_boss_chaos", 12f, 2f, true); ShowToast("Loading R45 Boss + 20p @ 3x scene..."); }),
			("Omega 300T 8x", () => { BenchmarkSceneRunner.StartBenchmark(Plugin.Instance, "r45_omega_all20_infrange", 20f, 2f, true); ShowToast("Loading 300-Tower InfRange 8x scene..."); })
		});
	}

	// ======================== EMBEDDABLE TOWERS & LOADOUTS TAB ========================
	internal static void BuildTowersTab(RectTransform parent)
	{
		ResolveFonts();
		_activeContentRoot = parent;
		_searchInput = null;

		if (!CheatActions.IsInGameRun)
		{
			AddNoticeBanner(parent, "Start a run to grant/lock individual towers or apply saved sandbox loadouts.");
			return;
		}

		AddSectionHeader(parent, "Cheat Tower Loadouts & Card Pool Filter");
		Transform optGrid = AddGrid(parent, 2, 356f, 34f, 8f);
		AddToggleTile(optGrid, CheatBridge.HasCleanQoL ? "Auto-Sort Hotbar (via QoL)" : "Auto-Sort Hotbar Order", () => CheatBridge.GetQoLAutoSortHotbar(), v =>
		{
			CheatBridge.SetQoLAutoSortHotbar(v);
			if (v) HotbarAndLoadoutManager.ForceRefreshHotbar();
			ShowToast($"Auto-Sort Hotbar: {(v ? "ON" : "OFF")}");
		});
		AddToggleTile(optGrid, "Lock Card Pool to Hotbar (Cheat)", () => Plugin.LockCardPoolToHotbar.Value, v =>
		{
			Plugin.LockCardPoolToHotbar.Value = v;
			ShowToast($"Lock Card Pool to Hotbar: {(v ? "ON" : "OFF")}");
		});
		AddCycleTile(optGrid, "Auto-Apply at Run Start", () => Plugin.AutoApplyLoadoutSlot.Value == 0 ? "OFF" : $"Slot {Plugin.AutoApplyLoadoutSlot.Value}", () =>
		{
			Plugin.AutoApplyLoadoutSlot.Value = (Plugin.AutoApplyLoadoutSlot.Value + 1) % 6;
			ShowToast(Plugin.AutoApplyLoadoutSlot.Value == 0 ? "Auto-Apply Loadout: OFF" : $"Auto-Apply at Run Start: Slot {Plugin.AutoApplyLoadoutSlot.Value}");
		});

		for (int slot = 1; slot <= 5; slot++)
		{
			int s = slot;
			GameObject row = CreatePanel($"LoadoutSlot_{s}", parent, BgCard);
			row.AddComponent<LayoutElement>().preferredHeight = 36f;
			Outline ol = row.AddComponent<Outline>();
			ol.effectColor = BorderSubtle;
			ol.effectDistance = new Vector2(1f, -1f);

			HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
			hlg.padding = new RectOffset(10, 8, 4, 4);
			hlg.spacing = 6f;
			hlg.childControlWidth = true;
			hlg.childControlHeight = true;
			hlg.childForceExpandWidth = false;

			Text lbl = CreateText(row.transform, $"<b>Slot {s}:</b> <color=#9FA8BC>{HotbarAndLoadoutManager.GetSlotSummary(s)}</color>", 12, FontStyle.Normal, TextLight, TextAnchor.MiddleLeft);
			lbl.supportRichText = true;
			lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

			Button applyBtn = CreateSimpleButton(row.transform, "Apply", GreenOn, TextLight, 12, () =>
			{
				int n = HotbarAndLoadoutManager.ApplyLoadoutSlot(s, replaceExisting: true);
				ShowToast(n > 0 ? $"Applied Loadout Slot {s} ({n} items)" : $"Slot {s} is empty");
				RequestRebuild();
			});
			applyBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 66f;

			Button saveBtn = CreateSimpleButton(row.transform, "Save Bar", GoldSoft, GoldAccent, 12, () =>
			{
				HotbarAndLoadoutManager.SaveCurrentToSlot(s);
				ShowToast($"Saved current hotbar to Slot {s}");
				RequestRebuild();
			});
			saveBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;

			Button clrBtn = CreateSimpleButton(row.transform, "Clear", BgButton, TextMuted, 12, () =>
			{
				HotbarAndLoadoutManager.ClearSlot(s);
				ShowToast($"Cleared Loadout Slot {s}");
				RequestRebuild();
			});
			clrBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 56f;
		}

		AddSectionHeader(parent, "Bulk Hotbar Grants");
		AddActionRow(parent, "Unlock / Order", new (string, Action)[]
		{
			("Unlock ALL Towers", () => { int n = CheatActions.GiveAllUnlocks(towers: true, buildings: false); HotbarAndLoadoutManager.ForceRefreshHotbar(); ShowToast($"Unlocked {n} tower(s)"); RequestRebuild(); }),
			("Unlock ALL Buildings", () => { int n = CheatActions.GiveAllUnlocks(towers: false, buildings: true); HotbarAndLoadoutManager.ForceRefreshHotbar(); ShowToast($"Unlocked {n} building(s)"); RequestRebuild(); }),
			("Unlock Everything", () => { int n = CheatActions.GiveAllUnlocks(towers: true, buildings: true); HotbarAndLoadoutManager.ForceRefreshHotbar(); ShowToast($"Unlocked {n} item(s)"); RequestRebuild(); }),
			("Reset Order", () => { HotbarAndLoadoutManager.ResetDefaultOrder(); ShowToast("Reset hotbar order to preset"); RequestRebuild(); })
		});

		AddSectionHeader(parent, "Row 1 — Towers (Hotbar Order: Left -> Right)");
		List<HotbarItemInfo> towers = HotbarAndLoadoutManager.GetOrderedItems(isTower: true);
		for (int i = 0; i < towers.Count; i++) AddHotbarOrderRow(parent, towers[i], i + 1, towers.Count);

		AddSectionHeader(parent, "Row 2 — Buildings (Hotbar Order: Left -> Right)");
		List<HotbarItemInfo> blds = HotbarAndLoadoutManager.GetOrderedItems(isTower: false);
		for (int i = 0; i < blds.Count; i++) AddHotbarOrderRow(parent, blds[i], i + 1, blds.Count);
	}

	private static void AddHotbarOrderRow(Transform parent, HotbarItemInfo info, int position, int total)
	{
		bool owned = HotbarAndLoadoutManager.IsItemUnlocked(info);
		GameObject row = CreatePanel($"Hotbar_{info.Id}", parent, BgCard);
		row.AddComponent<LayoutElement>().preferredHeight = 34f;
		Outline ol = row.AddComponent<Outline>();
		ol.effectColor = BorderSubtle;
		ol.effectDistance = new Vector2(1f, -1f);

		HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
		hlg.padding = new RectOffset(10, 8, 3, 3);
		hlg.spacing = 6f;
		hlg.childControlWidth = true;
		hlg.childControlHeight = true;
		hlg.childForceExpandWidth = false;

		string nick = string.IsNullOrEmpty(info.Nickname) ? "" : $"  <color=#9FA8BC>({info.Nickname})</color>";
		Text nameTxt = CreateText(row.transform, $"<color=#D9B36C>#{position}</color>  <b>{info.DisplayName}</b>{nick}", 13, FontStyle.Normal, TextLight, TextAnchor.MiddleLeft);
		nameTxt.supportRichText = true;
		nameTxt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

		Button upBtn = CreateSimpleButton(row.transform, "Up", BgButton, TextLight, 11, () =>
		{
			HotbarAndLoadoutManager.MoveItemInOrder(info.Id, -1);
			RequestRebuild();
		});
		upBtn.interactable = position > 1;
		upBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 42f;

		Button downBtn = CreateSimpleButton(row.transform, "Dn", BgButton, TextLight, 11, () =>
		{
			HotbarAndLoadoutManager.MoveItemInOrder(info.Id, +1);
			RequestRebuild();
		});
		downBtn.interactable = position < total;
		downBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 42f;

		Button toggleBtn = CreateSimpleButton(row.transform, owned ? "Active [Lock]" : "Locked [Give]", owned ? GreenOn : BgButton, TextLight, 12, () =>
		{
			bool nextState = !owned;
			HotbarAndLoadoutManager.SetItemUnlocked(info.Id, nextState);
			ShowToast($"{info.DisplayName}: {(nextState ? "Unlocked on hotbar" : "Removed from hotbar")}");
			RequestRebuild();
		});
		toggleBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 112f;
	}

	// ======================== EMBEDDABLE CARDS TAB ========================
	internal static void BuildCardsTab(RectTransform parent)
	{
		ResolveFonts();
		_activeContentRoot = parent;
		if (!CheatActions.IsInGameRun)
		{
			AddNoticeBanner(parent, "Start a run to browse and grant upgrade cards.");
			return;
		}

		GameObject searchRow = CreatePanel("SearchRow", parent, Color.clear);
		searchRow.AddComponent<LayoutElement>().preferredHeight = 34f;
		HorizontalLayoutGroup srHlg = searchRow.AddComponent<HorizontalLayoutGroup>();
		srHlg.spacing = 6f;
		srHlg.childControlWidth = true;
		srHlg.childControlHeight = true;
		srHlg.childForceExpandWidth = false;

		GameObject inputBg = CreatePanel("SearchBox", searchRow.transform, BgInput);
		inputBg.AddComponent<LayoutElement>().flexibleWidth = 1f;
		Outline ibOl = inputBg.AddComponent<Outline>();
		ibOl.effectColor = BorderSubtle;
		ibOl.effectDistance = new Vector2(1f, -1f);

		_searchInput = inputBg.AddComponent<InputField>();
		Text textComp = CreateText(inputBg.transform, "", 14, FontStyle.Normal, TextLight, TextAnchor.MiddleLeft);
		StretchFill(textComp.rectTransform, 10f, 4f, 10f, 4f);
		Text phComp = CreateText(inputBg.transform, "Search cards by name, tower, or description...", 13, FontStyle.Italic, TextMuted, TextAnchor.MiddleLeft);
		StretchFill(phComp.rectTransform, 10f, 4f, 10f, 4f);
		_searchInput.textComponent = textComp;
		_searchInput.placeholder = phComp;
		_searchInput.text = _searchQuery;

		Button clearBtn = CreateSimpleButton(searchRow.transform, "Clear", BgButton, TextLight, 12, () =>
		{
			_searchQuery = string.Empty;
			if (_searchInput != null) _searchInput.text = string.Empty;
			RebuildCardsList();
		});
		clearBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 64f;

		Button unresBtn = CreateSimpleButton(searchRow.transform,
			Plugin.IncludeUnresearchedCards.Value ? "Unresearched: SHOWN" : "Unresearched: HIDDEN",
			Plugin.IncludeUnresearchedCards.Value ? GoldSoft : BgButton, TextLight, 12, () =>
			{
				Plugin.IncludeUnresearchedCards.Value = !Plugin.IncludeUnresearchedCards.Value;
				RequestRebuild();
			});
		unresBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 170f;

		GameObject filterRow = CreatePanel("FilterRow", parent, Color.clear);
		filterRow.AddComponent<LayoutElement>().preferredHeight = 30f;
		HorizontalLayoutGroup frHlg = filterRow.AddComponent<HorizontalLayoutGroup>();
		frHlg.spacing = 4f;
		frHlg.childControlWidth = true;
		frHlg.childControlHeight = true;
		frHlg.childForceExpandWidth = true;

		foreach (CardFilterCategory fc in Enum.GetValues(typeof(CardFilterCategory)))
		{
			bool active = fc == _cardFilter;
			CardFilterCategory captured = fc;
			CreateSimpleButton(filterRow.transform, fc.ToString(), active ? GoldAccent : BgButton, active ? new Color(0.08f, 0.09f, 0.12f, 1f) : TextLight, 12, () =>
			{
				_cardFilter = captured;
				RequestRebuild();
			});
		}

		GameObject listContainer = CreatePanel("CardsListContainer", parent, Color.clear);
		VerticalLayoutGroup lVlg = listContainer.AddComponent<VerticalLayoutGroup>();
		lVlg.spacing = 4f;
		lVlg.childControlWidth = true;
		lVlg.childControlHeight = true;
		lVlg.childForceExpandWidth = true;
		lVlg.childForceExpandHeight = false;

		_cardsListContainer = listContainer.transform;
		_searchInput.onValueChanged.AddListener(val => { _searchQuery = val ?? string.Empty; RebuildCardsList(); });
		RebuildCardsList();
	}

	private static void RebuildCardsList()
	{
		if (_cardsListContainer == null) return;
		for (int i = _cardsListContainer.childCount - 1; i >= 0; i--)
		{
			Transform child = _cardsListContainer.GetChild(i);
			child.gameObject.SetActive(false);
			child.SetParent(null, worldPositionStays: false);
			UnityEngine.Object.Destroy(child.gameObject);
		}

		IReadOnlyList<CardCatalogEntry> all = CheatActions.GetAllEntries();
		string q = (_searchQuery ?? string.Empty).Trim();
		bool hasQ = q.Length > 0;
		bool showUnres = Plugin.IncludeUnresearchedCards.Value;

		List<CardCatalogEntry> matches = new List<CardCatalogEntry>(64);
		foreach (CardCatalogEntry e in all)
		{
			if (!showUnres && !e.Card.unlocked) continue;
			if (_cardFilter == CardFilterCategory.Available) { if (!CheatActions.IsCardCurrentlyAvailable(e)) continue; }
			else if (_cardFilter != CardFilterCategory.All && e.Category != _cardFilter) continue;

			if (hasQ)
			{
				bool hit =
					(e.Title != null && e.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
					(e.TreeLabel != null && e.TreeLabel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
					(e.Description != null && e.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
				if (!hit) continue;
			}
			matches.Add(e);
		}

		if (matches.Count == 0)
		{
			AddNoticeBanner(_cardsListContainer, "No matching cards found.");
			return;
		}

		int cap = Mathf.Min(matches.Count, 60);
		for (int i = 0; i < cap; i++) AddCardRow(_cardsListContainer, matches[i]);

		if (matches.Count > cap)
		{
			AddNoticeBanner(_cardsListContainer, $"Showing first {cap} of {matches.Count} matching cards — type in search or pick a category to narrow.");
		}

		if (_activeContentRoot != null)
		{
			Canvas.ForceUpdateCanvases();
			LayoutRebuilder.ForceRebuildLayoutImmediate(_activeContentRoot);
		}
	}

	private static void AddCardRow(Transform parent, CardCatalogEntry entry)
	{
		bool owned = CheatActions.IsCardOwned(entry);
		GameObject row = CreatePanel("CardRow", parent, BgCard);
		row.AddComponent<LayoutElement>().preferredHeight = 48f;
		Outline ol = row.AddComponent<Outline>();
		ol.effectColor = BorderSubtle;
		ol.effectDistance = new Vector2(1f, -1f);

		HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
		hlg.padding = new RectOffset(10, 8, 5, 5);
		hlg.spacing = 8f;
		hlg.childControlWidth = true;
		hlg.childControlHeight = true;
		hlg.childForceExpandWidth = false;

		GameObject info = CreatePanel("Info", row.transform, Color.clear);
		info.AddComponent<LayoutElement>().flexibleWidth = 1f;

		string researchTag = entry.Card.unlocked ? "" : "  <color=#E6A23C>[Unresearched]</color>";
		Text titleTxt = CreateText(info.transform, $"<b>{entry.Title}</b>  <color=#9FA8BC>({entry.TreeLabel})</color>{researchTag}", 13, FontStyle.Normal, TextLight, TextAnchor.UpperLeft);
		titleTxt.supportRichText = true;
		titleTxt.rectTransform.anchorMin = new Vector2(0f, 0.5f);
		titleTxt.rectTransform.anchorMax = Vector2.one;
		titleTxt.rectTransform.offsetMin = Vector2.zero;
		titleTxt.rectTransform.offsetMax = Vector2.zero;

		string cleanDesc = (entry.Description ?? "").Replace('\n', ' ');
		if (cleanDesc.Length > 92) cleanDesc = cleanDesc.Substring(0, 89) + "...";
		Text descTxt = CreateText(info.transform, cleanDesc, 11, FontStyle.Normal, TextMuted, TextAnchor.LowerLeft);
		descTxt.rectTransform.anchorMin = Vector2.zero;
		descTxt.rectTransform.anchorMax = new Vector2(1f, 0.5f);
		descTxt.rectTransform.offsetMin = Vector2.zero;
		descTxt.rectTransform.offsetMax = Vector2.zero;

		if (entry.Card.unlocks != null && entry.Card.unlocks.Count > 0)
		{
			Button brBtn = CreateSimpleButton(row.transform, "Branch", GoldSoft, GoldAccent, 12, () =>
			{
				int n = CheatActions.GiveSubBranch(entry.Card, Plugin.IncludeUnresearchedCards.Value);
				ShowToast(n > 0 ? $"Granted {n} card(s) from {entry.Title} branch" : $"{entry.Title} branch already owned");
				RebuildCardsList();
			});
			brBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 74f;
		}

		Button giveBtn = CreateSimpleButton(row.transform, owned ? "Owned" : "Give", owned ? new Color(0.14f, 0.28f, 0.20f, 1f) : GreenOn, TextLight, 12, () =>
		{
			if (CheatActions.GiveCard(entry))
			{
				ShowToast($"Granted card: {entry.Title}");
				RebuildCardsList();
			}
		});
		giveBtn.interactable = !owned;
		giveBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 72f;
	}

	// ======================== EMBEDDABLE CARD TREES TAB ========================
	internal static void BuildCardTreesTab(RectTransform parent)
	{
		ResolveFonts();
		_activeContentRoot = parent;
		_searchInput = null;

		if (!CheatActions.IsInGameRun)
		{
			AddNoticeBanner(parent, "Start a run to grant entire Card Trees or sub-branches.");
			return;
		}

		AddSectionHeader(parent, "Bulk Tree Grants");
		GameObject topBar = CreatePanel("TreeTopBar", parent, Color.clear);
		topBar.AddComponent<LayoutElement>().preferredHeight = 34f;
		HorizontalLayoutGroup tbHlg = topBar.AddComponent<HorizontalLayoutGroup>();
		tbHlg.spacing = 6f;
		tbHlg.childControlWidth = true;
		tbHlg.childControlHeight = true;
		tbHlg.childForceExpandWidth = true;

		CreateSimpleButton(topBar.transform, Plugin.IncludeUnresearchedCards.Value ? "Include Unresearched: ON" : "Include Unresearched: OFF",
			Plugin.IncludeUnresearchedCards.Value ? GoldSoft : BgButton, TextLight, 12, () =>
			{
				Plugin.IncludeUnresearchedCards.Value = !Plugin.IncludeUnresearchedCards.Value;
				RequestRebuild();
			});
		CreateSimpleButton(topBar.transform, "Give ALL Tower Trees", GreenOn, TextLight, 12, () =>
		{
			int n = CheatActions.GiveAllTreesByCategory(CardFilterCategory.Towers, Plugin.IncludeUnresearchedCards.Value);
			ShowToast($"Granted {n} tower card(s)");
			RequestRebuild();
		});
		CreateSimpleButton(topBar.transform, "Give ALL DOT Trees", GreenOn, TextLight, 12, () =>
		{
			int n = CheatActions.GiveAllTreesByCategory(CardFilterCategory.DOT, Plugin.IncludeUnresearchedCards.Value);
			ShowToast($"Granted {n} DOT card(s)");
			RequestRebuild();
		});
		CreateSimpleButton(topBar.transform, "Give ALL Non-Monster", GoldAccent, new Color(0.08f, 0.09f, 0.12f, 1f), 12, () =>
		{
			int n = CheatActions.GiveAllNonMonsterTrees(Plugin.IncludeUnresearchedCards.Value);
			ShowToast($"Granted {n} card(s) across all trees");
			RequestRebuild();
		});

		IReadOnlyList<TreeGroupSummary> groups = CheatActions.GetTreeGroups();
		CardFilterCategory? lastCat = null;
		foreach (TreeGroupSummary g in groups)
		{
			if (lastCat != g.Category)
			{
				lastCat = g.Category;
				AddSectionHeader(parent, g.Category.ToString());
			}
			AddTreeGroupRow(parent, g);
		}
	}

	private static void AddTreeGroupRow(Transform parent, TreeGroupSummary group)
	{
		bool includeUnres = Plugin.IncludeUnresearchedCards.Value;
		int ownedCount = 0;
		int eligibleCount = 0;
		foreach (CardCatalogEntry e in group.Cards)
		{
			if (!includeUnres && !e.Card.unlocked) continue;
			eligibleCount++;
			if (CheatActions.IsCardOwned(e)) ownedCount++;
		}

		GameObject row = CreatePanel($"Tree_{group.TreeKey}", parent, BgCard);
		row.AddComponent<LayoutElement>().preferredHeight = 40f;
		Outline ol = row.AddComponent<Outline>();
		ol.effectColor = BorderSubtle;
		ol.effectDistance = new Vector2(1f, -1f);

		HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
		hlg.padding = new RectOffset(12, 8, 5, 5);
		hlg.spacing = 8f;
		hlg.childControlWidth = true;
		hlg.childControlHeight = true;
		hlg.childForceExpandWidth = false;

		Text nameTxt = CreateText(row.transform, $"{group.TreeLabel}   <color=#9FA8BC>({ownedCount}/{eligibleCount})</color>", 13, FontStyle.Bold, TextLight, TextAnchor.MiddleLeft);
		nameTxt.supportRichText = true;
		nameTxt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

		bool expanded = ExpandedTrees.Contains(group.TreeKey);
		if (group.BranchRoots.Count > 0)
		{
			Button expBtn = CreateSimpleButton(row.transform, expanded ? "Branches ▲" : "Branches ▼", BgButton, TextLight, 12, () =>
			{
				if (expanded) ExpandedTrees.Remove(group.TreeKey);
				else ExpandedTrees.Add(group.TreeKey);
				RequestRebuild();
			});
			expBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 100f;
		}

		bool allOwned = eligibleCount > 0 && ownedCount >= eligibleCount;
		Button giveBtn = CreateSimpleButton(row.transform, allOwned ? "Complete" : "Give Tree", allOwned ? new Color(0.14f, 0.28f, 0.20f, 1f) : GreenOn, TextLight, 12, () =>
		{
			int n = CheatActions.GiveCardTree(group.TreeKey, Plugin.IncludeUnresearchedCards.Value);
			ShowToast(n > 0 ? $"Granted {n} card(s) in {group.TreeLabel}" : $"{group.TreeLabel} already complete");
			RequestRebuild();
		});
		giveBtn.interactable = !allOwned && eligibleCount > 0;
		giveBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;

		if (expanded && group.BranchRoots.Count > 0)
		{
			GameObject subBox = CreatePanel("SubBranches", parent, new Color(0.10f, 0.115f, 0.155f, 0.95f));
			VerticalLayoutGroup svlg = subBox.AddComponent<VerticalLayoutGroup>();
			svlg.padding = new RectOffset(18, 10, 6, 6);
			svlg.spacing = 4f;
			svlg.childControlWidth = true;
			svlg.childControlHeight = true;
			svlg.childForceExpandWidth = true;

			Transform subGrid = AddGrid(subBox.transform, 2, 348f, 30f, 6f);
			foreach (CardCatalogEntry root in group.BranchRoots)
			{
				CreateSimpleButton(subGrid, $"Give Branch: {root.Title}", GoldSoft, GoldAccent, 12, () =>
				{
					int n = CheatActions.GiveSubBranch(root.Card, Plugin.IncludeUnresearchedCards.Value);
					ShowToast(n > 0 ? $"Granted {n} card(s) in {root.Title} branch" : $"{root.Title} branch already owned");
					RequestRebuild();
				});
			}
		}
	}

	// ======================== UI PRIMITIVES ========================
	private static T NextInCycle<T>(T[] options, T current)
	{
		for (int i = 0; i < options.Length; i++)
		{
			if (EqualityComparer<T>.Default.Equals(options[i], current)) return options[(i + 1) % options.Length];
		}
		return options[0];
	}

	private static void ResolveFonts()
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

	private static void AddSectionHeader(Transform parent, string title)
	{
		GameObject obj = CreatePanel("SectionHeader", parent, Color.clear);
		obj.AddComponent<LayoutElement>().preferredHeight = 26f;
		Text txt = CreateText(obj.transform, title.ToUpperInvariant(), 12, FontStyle.Bold, GoldAccent, TextAnchor.LowerLeft);
		StretchFill(txt.rectTransform, 4f, 2f, 4f, 2f);
	}

	private static void AddNoticeBanner(Transform parent, string message)
	{
		GameObject obj = CreatePanel("Notice", parent, BgCard);
		obj.AddComponent<LayoutElement>().preferredHeight = 36f;
		Text txt = CreateText(obj.transform, message, 12, FontStyle.Normal, TextMuted, TextAnchor.MiddleCenter);
		StretchFill(txt.rectTransform, 10f, 4f, 10f, 4f);
	}

	private static Transform AddGrid(Transform parent, int columns, float cellWidth, float cellHeight, float spacing)
	{
		GameObject gridObj = CreatePanel("Grid", parent, Color.clear);
		GridLayoutGroup glg = gridObj.AddComponent<GridLayoutGroup>();
		glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		glg.constraintCount = columns;
		glg.cellSize = new Vector2(cellWidth, cellHeight);
		glg.spacing = new Vector2(spacing, spacing);
		glg.childAlignment = TextAnchor.UpperLeft;
		return gridObj.transform;
	}

	private static void AddActionRow(Transform parent, string label, (string text, Action onClick)[] buttons)
	{
		GameObject row = CreatePanel("ActionRow", parent, BgCard);
		row.AddComponent<LayoutElement>().preferredHeight = 38f;
		Outline ol = row.AddComponent<Outline>();
		ol.effectColor = BorderSubtle;
		ol.effectDistance = new Vector2(1f, -1f);

		HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
		hlg.padding = new RectOffset(12, 8, 4, 4);
		hlg.spacing = 6f;
		hlg.childControlWidth = true;
		hlg.childControlHeight = true;
		hlg.childForceExpandWidth = false;

		Text lbl = CreateText(row.transform, label, 13, FontStyle.Bold, TextLight, TextAnchor.MiddleLeft);
		lbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 140f;

		foreach (var (text, onClick) in buttons)
		{
			Button b = CreateSimpleButton(row.transform, text, BgButton, TextLight, 12, onClick);
			b.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
		}
	}

	private static void AddToggleTile(Transform parent, string label, Func<bool> getter, Action<bool> setter)
	{
		GameObject tile = CreatePanel("ToggleTile", parent, BgCard);
		Outline ol = tile.AddComponent<Outline>();
		ol.effectColor = BorderSubtle;
		ol.effectDistance = new Vector2(1f, -1f);

		Button btn = tile.AddComponent<Button>();
		Image bg = tile.GetComponent<Image>();

		HorizontalLayoutGroup hlg = tile.AddComponent<HorizontalLayoutGroup>();
		hlg.padding = new RectOffset(10, 8, 4, 4);
		hlg.spacing = 6f;
		hlg.childControlWidth = true;
		hlg.childControlHeight = true;
		hlg.childForceExpandWidth = false;

		Text lbl = CreateText(tile.transform, label, 13, FontStyle.Normal, TextLight, TextAnchor.MiddleLeft);
		lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

		GameObject badge = CreatePanel("Badge", tile.transform, BgButton);
		badge.AddComponent<LayoutElement>().preferredWidth = 54f;
		Image badgeImg = badge.GetComponent<Image>();
		Text badgeTxt = CreateText(badge.transform, "OFF", 12, FontStyle.Bold, TextLight, TextAnchor.MiddleCenter);
		StretchFill(badgeTxt.rectTransform);

		void Sync()
		{
			bool on = getter();
			badgeImg.color = on ? GreenOn : BgButton;
			badgeTxt.text = on ? "ON" : "OFF";
			bg.color = on ? new Color(0.13f, 0.18f, 0.21f, 0.98f) : BgCard;
		}

		btn.onClick.AddListener(() => { setter(!getter()); Sync(); });
		Sync();
		RegisterSync(Sync);
	}

	private static void AddCycleTile(Transform parent, string label, Func<string> valueGetter, Action onCycle)
	{
		GameObject tile = CreatePanel("CycleTile", parent, BgCard);
		Outline ol = tile.AddComponent<Outline>();
		ol.effectColor = BorderSubtle;
		ol.effectDistance = new Vector2(1f, -1f);

		Button btn = tile.AddComponent<Button>();
		HorizontalLayoutGroup hlg = tile.AddComponent<HorizontalLayoutGroup>();
		hlg.padding = new RectOffset(10, 8, 4, 4);
		hlg.spacing = 6f;
		hlg.childControlWidth = true;
		hlg.childControlHeight = true;
		hlg.childForceExpandWidth = false;

		Text lbl = CreateText(tile.transform, label, 12, FontStyle.Normal, TextLight, TextAnchor.MiddleLeft);
		lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

		GameObject badge = CreatePanel("ValueBadge", tile.transform, GoldSoft);
		badge.AddComponent<LayoutElement>().preferredWidth = 128f;
		Text badgeTxt = CreateText(badge.transform, "", 12, FontStyle.Bold, GoldAccent, TextAnchor.MiddleCenter);
		StretchFill(badgeTxt.rectTransform, 4f, 0f, 4f, 0f);

		void Sync() => badgeTxt.text = valueGetter();
		btn.onClick.AddListener(() => { onCycle(); Sync(); });
		Sync();
		RegisterSync(Sync);
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
		cb.disabledColor = new Color(0.65f, 0.65f, 0.65f, 0.65f);
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
