using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RogueTowerCustomLevels;

internal static class InputBlockerPatches
{
	private static readonly AccessTools.FieldRef<PauseMenu, bool> UiHiddenRef =
		AccessTools.FieldRefAccess<PauseMenu, bool>("uiHidden");

	internal static bool IsPauseUiHidden()
	{
		return PauseMenu.instance != null && UiHiddenRef(PauseMenu.instance);
	}

	[HarmonyPatch(typeof(UIManager), "Update")]
	[HarmonyPrefix]
	private static bool UIManager_Update_Prefix()
	{
		if (QuickHudManager.IsPointerOverQuickButton() || ModMenuUI.IsPointerOverMenu)
		{
			return false;
		}
		return true;
	}

	[HarmonyPatch(typeof(BuildingManager), "Update")]
	[HarmonyPrefix]
	private static bool BuildingManager_Update_Prefix()
	{
		if (ModMenuUI.IsPointerOverMenu || ModMenuUI.IsSearchFocused)
		{
			return false;
		}
		return true;
	}

	[HarmonyPatch(typeof(CameraController), "UpdateZoom")]
	[HarmonyPrefix]
	private static bool CameraController_UpdateZoom_Prefix()
	{
		if (ModMenuUI.IsPointerOverMenu)
		{
			return false;
		}
		return true;
	}

	[HarmonyPatch(typeof(CameraController), "UpdateMovement")]
	[HarmonyPrefix]
	private static bool CameraController_UpdateMovement_Prefix()
	{
		if (ModMenuUI.IsSearchFocused || ModMenuUI.IsPointerOverMenu)
		{
			return false;
		}
		return true;
	}

	[HarmonyPatch(typeof(PauseMenu), "Update")]
	[HarmonyPrefix]
	private static bool PauseMenu_Update_Prefix()
	{
		if (ModMenuUI.IsSearchFocused)
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				ModMenuUI.UnfocusSearch();
			}
			return false;
		}
		if (ModMenuUI.IsMenuOpen && Input.GetKeyDown(KeyCode.Escape))
		{
			ModMenuUI.SetOpen(false);
			return false;
		}
		return true;
	}
}

internal enum ModMenuTab
{
	Cheats = 0,
	Towers = 1,
	Cards = 2,
	CardTrees = 3,
	Modules = 4
}

internal static class ModMenuUI
{
	private sealed class WindowDragger : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		public RectTransform Target;
		private Vector2 _offset;

		public void OnBeginDrag(PointerEventData eventData)
		{
			if (Target == null) return;
			RectTransformUtility.ScreenPointToLocalPointInRectangle(
				Target.parent as RectTransform, eventData.position, null, out Vector2 localPt);
			_offset = Target.anchoredPosition - localPt;
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (Target == null) return;
			if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
				Target.parent as RectTransform, eventData.position, null, out Vector2 localPt))
			{
				Vector2 next = localPt + _offset;
				next.x = Mathf.Clamp(next.x, -560f, 560f);
				next.y = Mathf.Clamp(next.y, -260f, 260f);
				Target.anchoredPosition = next;
			}
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			if (Target != null)
			{
				Plugin.WindowPosX.Value = Target.anchoredPosition.x;
				Plugin.WindowPosY.Value = Target.anchoredPosition.y;
			}
		}
	}

	// Palette
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

	private static Font _uiFont;
	private static GameObject _canvasObj;
	private static GameObject _windowObj;
	private static RectTransform _windowRt;
	private static GameObject _cornerBtnObj;
	private static RectTransform _cornerBtnRt;
	private static Text _cornerBtnText;

	private static Text _statusPillText;
	private static Text _pauseBtnText;
	private static Text _toastText;
	private static float _toastUntil;

	private static RectTransform _contentRt;
	private static ScrollRect _scrollRect;

	private static readonly Button[] _tabButtons = new Button[5];
	private static readonly Image[] _tabImages = new Image[5];
	private static readonly Text[] _tabTexts = new Text[5];

	private static ModMenuTab _currentTab = ModMenuTab.Cheats;
	private static bool _isOpen;
	private static bool _pausedByMenu;
	private static float _prePauseSpeed = 1f;

	// Cards Tab state
	private static InputField _searchInput;
	private static string _searchQuery = string.Empty;
	private static CardFilterCategory _cardFilter = CardFilterCategory.All;
	private static readonly HashSet<string> ExpandedTrees = new HashSet<string>();

	private static float _nextLiveRefreshTime;
	private static readonly List<Action> LiveSyncCallbacks = new List<Action>();

	internal static bool IsMenuOpen => _isOpen && _windowObj != null && _windowObj.activeInHierarchy;
	internal static int CurrentTabIndex => (int)_currentTab;

	internal static void SelectTabPublic(int tabIndex)
	{
		SelectTab((ModMenuTab)Mathf.Clamp(tabIndex, 0, 4));
	}

	internal static void RefreshOpenTabPublic()
	{
		if (IsMenuOpen)
		{
			RebuildCurrentTab();
		}
	}

	internal static bool IsSearchFocused =>
		IsMenuOpen &&
		_searchInput != null &&
		EventSystem.current != null &&
		EventSystem.current.currentSelectedGameObject == _searchInput.gameObject;

	internal static bool IsPointerOverMenu
	{
		get
		{
			Vector2 mouse = Input.mousePosition;
			if (IsMenuOpen && _windowRt != null && RectTransformUtility.RectangleContainsScreenPoint(_windowRt, mouse, null))
			{
				return true;
			}
			if (_cornerBtnObj != null && _cornerBtnObj.activeInHierarchy && _cornerBtnRt != null &&
				RectTransformUtility.RectangleContainsScreenPoint(_cornerBtnRt, mouse, null))
			{
				return true;
			}
			return false;
		}
	}

	internal static void UnfocusSearch()
	{
		if (EventSystem.current != null && IsSearchFocused)
		{
			EventSystem.current.SetSelectedGameObject(null);
		}
	}

	internal static void OnSceneChanged(string sceneName)
	{
		if (_isOpen && _pausedByMenu)
		{
			_pausedByMenu = false;
		}
		if (IsMenuOpen)
		{
			RebuildCurrentTab();
		}
	}

	internal static void EnsureCreated()
	{
		if (_canvasObj != null)
		{
			return;
		}
		ResolveFont();

		_canvasObj = new GameObject("RogueTowerModMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
		UnityEngine.Object.DontDestroyOnLoad(_canvasObj);

		Canvas canvas = _canvasObj.GetComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 500;

		CanvasScaler scaler = _canvasObj.GetComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		scaler.matchWidthOrHeight = 0.5f;

		CreateCornerButton();
		CreateMainWindow();
		SetOpen(false);
	}

	private static void ResolveFont()
	{
		if (_uiFont != null)
		{
			return;
		}
		Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
		foreach (Font f in fonts)
		{
			if (f != null && !string.IsNullOrEmpty(f.name) && f.name != "Arial")
			{
				_uiFont = f;
				break;
			}
		}
		if (_uiFont == null && fonts.Length > 0)
		{
			_uiFont = fonts[0];
		}
		if (_uiFont == null)
		{
			_uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
		}
	}

	private static void CreateCornerButton()
	{
		_cornerBtnObj = new GameObject("ModsCornerButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
		_cornerBtnObj.transform.SetParent(_canvasObj.transform, worldPositionStays: false);

		_cornerBtnRt = _cornerBtnObj.GetComponent<RectTransform>();
		_cornerBtnRt.anchorMin = new Vector2(1f, 1f);
		_cornerBtnRt.anchorMax = new Vector2(1f, 1f);
		_cornerBtnRt.pivot = new Vector2(1f, 1f);
		_cornerBtnRt.anchoredPosition = new Vector2(-30f, -96f);
		_cornerBtnRt.sizeDelta = new Vector2(68f, 30f);

		Image img = _cornerBtnObj.GetComponent<Image>();
		img.color = BgHeader;

		Outline ol = _cornerBtnObj.AddComponent<Outline>();
		ol.effectColor = GoldAccent;
		ol.effectDistance = new Vector2(1f, -1f);

		Button btn = _cornerBtnObj.GetComponent<Button>();
		btn.onClick.AddListener(Toggle);

		_cornerBtnText = CreateText(_cornerBtnObj.transform, "Mods (`)", 13, FontStyle.Bold, GoldAccent, TextAnchor.MiddleCenter);
		StretchFill(_cornerBtnText.rectTransform);
	}

	private static void CreateMainWindow()
	{
		_windowObj = new GameObject("ModMenuWindow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		_windowObj.transform.SetParent(_canvasObj.transform, worldPositionStays: false);

		_windowRt = _windowObj.GetComponent<RectTransform>();
		_windowRt.anchorMin = new Vector2(0.5f, 0.5f);
		_windowRt.anchorMax = new Vector2(0.5f, 0.5f);
		_windowRt.pivot = new Vector2(0.5f, 0.5f);
		_windowRt.sizeDelta = new Vector2(820f, 600f);
		_windowRt.anchoredPosition = new Vector2(
			Mathf.Clamp(Plugin.WindowPosX.Value, -560f, 560f),
			Mathf.Clamp(Plugin.WindowPosY.Value, -260f, 260f));

		Image winBg = _windowObj.GetComponent<Image>();
		winBg.color = BgWindow;
		winBg.raycastTarget = true;

		Outline winBorder = _windowObj.AddComponent<Outline>();
		winBorder.effectColor = GoldAccent;
		winBorder.effectDistance = new Vector2(2f, -2f);

		// Header bar (height 46, draggable)
		GameObject header = CreatePanel("Header", _windowRt, BgHeader);
		RectTransform hdrRt = header.GetComponent<RectTransform>();
		hdrRt.anchorMin = new Vector2(0f, 1f);
		hdrRt.anchorMax = new Vector2(1f, 1f);
		hdrRt.pivot = new Vector2(0.5f, 1f);
		hdrRt.anchoredPosition = Vector2.zero;
		hdrRt.sizeDelta = new Vector2(0f, 46f);

		WindowDragger dragger = header.AddComponent<WindowDragger>();
		dragger.Target = _windowRt;

		Text titleTxt = CreateText(hdrRt, "Rogue Tower Mod Menu", 19, FontStyle.Bold, GoldAccent, TextAnchor.MiddleLeft);
		titleTxt.rectTransform.anchorMin = new Vector2(0f, 0f);
		titleTxt.rectTransform.anchorMax = new Vector2(0f, 1f);
		titleTxt.rectTransform.pivot = new Vector2(0f, 0.5f);
		titleTxt.rectTransform.anchoredPosition = new Vector2(16f, 0f);
		titleTxt.rectTransform.sizeDelta = new Vector2(240f, 0f);

		_statusPillText = CreateText(hdrRt, "", 12, FontStyle.Normal, TextMuted, TextAnchor.MiddleRight);
		_statusPillText.rectTransform.anchorMin = new Vector2(0f, 0f);
		_statusPillText.rectTransform.anchorMax = new Vector2(1f, 1f);
		_statusPillText.rectTransform.offsetMin = new Vector2(248f, 0f);
		_statusPillText.rectTransform.offsetMax = new Vector2(-316f, 0f);

		Button rotLeftBtn = CreateSimpleButton(hdrRt, "<90", Vector2.zero, new Vector2(34f, 28f), BgButton, TextLight, 11, () =>
		{
			if (!CheatActions.IsInGameRun) return;
			CameraAndPlacementHelper.RotateBy(-90f);
			ShowToast("Rotated camera -90 deg");
		});
		RectTransform rlRt = rotLeftBtn.GetComponent<RectTransform>();
		rlRt.anchorMin = new Vector2(1f, 0.5f);
		rlRt.anchorMax = new Vector2(1f, 0.5f);
		rlRt.pivot = new Vector2(1f, 0.5f);
		rlRt.anchoredPosition = new Vector2(-274f, 0f);

		Button rotRightBtn = CreateSimpleButton(hdrRt, "90>", Vector2.zero, new Vector2(34f, 28f), BgButton, TextLight, 11, () =>
		{
			if (!CheatActions.IsInGameRun) return;
			CameraAndPlacementHelper.RotateBy(90f);
			ShowToast("Rotated camera +90 deg");
		});
		RectTransform rrRt = rotRightBtn.GetComponent<RectTransform>();
		rrRt.anchorMin = new Vector2(1f, 0.5f);
		rrRt.anchorMax = new Vector2(1f, 0.5f);
		rrRt.pivot = new Vector2(1f, 0.5f);
		rrRt.anchoredPosition = new Vector2(-236f, 0f);

		Button topBtn = CreateSimpleButton(hdrRt, "Top", Vector2.zero, new Vector2(38f, 28f), BgButton, GoldAccent, 11, () =>
		{
			if (!CheatActions.IsInGameRun) return;
			CameraAndPlacementHelper.ToggleTopDown();
			ShowToast($"Top-Down View: {(CameraAndPlacementHelper.IsTopDown ? "ON" : "OFF")}");
		});
		RectTransform tdRt = topBtn.GetComponent<RectTransform>();
		tdRt.anchorMin = new Vector2(1f, 0.5f);
		tdRt.anchorMax = new Vector2(1f, 0.5f);
		tdRt.pivot = new Vector2(1f, 0.5f);
		tdRt.anchoredPosition = new Vector2(-194f, 0f);

		Button themeBtn = CreateSimpleButton(hdrRt, "Theme", Vector2.zero, new Vector2(66f, 28f), BgButton, TextLight, 11, () =>
		{
			string next = ModuleBridge.ToggleLightDark();
			ShowToast($"Visual Theme: {next}");
			RebuildCurrentTab();
		});
		RectTransform thRt = themeBtn.GetComponent<RectTransform>();
		thRt.anchorMin = new Vector2(1f, 0.5f);
		thRt.anchorMax = new Vector2(1f, 0.5f);
		thRt.pivot = new Vector2(1f, 0.5f);
		thRt.anchoredPosition = new Vector2(-122f, 0f);

		Button pauseBtn = CreateSimpleButton(hdrRt, "Pause", new Vector2(-50f, 0f), new Vector2(66f, 28f), BgButton, TextLight, 12, () =>
		{
			if (!CheatActions.IsInGameRun) return;
			float cur = ModuleBridge.GetSpeed();
			ModuleBridge.SetSpeed(cur <= 0.01f ? 1f : 0f);
			UpdateHeaderStatus();
		});
		RectTransform pRt = pauseBtn.GetComponent<RectTransform>();
		pRt.anchorMin = new Vector2(1f, 0.5f);
		pRt.anchorMax = new Vector2(1f, 0.5f);
		pRt.pivot = new Vector2(1f, 0.5f);
		pRt.anchoredPosition = new Vector2(-48f, 0f);
		_pauseBtnText = pauseBtn.GetComponentInChildren<Text>();

		Button closeBtn = CreateSimpleButton(hdrRt, "X", Vector2.zero, new Vector2(30f, 28f), RedDanger, TextLight, 14, () => SetOpen(false));
		RectTransform cRt = closeBtn.GetComponent<RectTransform>();
		cRt.anchorMin = new Vector2(1f, 0.5f);
		cRt.anchorMax = new Vector2(1f, 0.5f);
		cRt.pivot = new Vector2(1f, 0.5f);
		cRt.anchoredPosition = new Vector2(-10f, 0f);

		// Tabs bar (y = -46, height = 38)
		GameObject tabBar = CreatePanel("TabBar", _windowRt, new Color(0.10f, 0.115f, 0.155f, 1f));
		RectTransform tabRt = tabBar.GetComponent<RectTransform>();
		tabRt.anchorMin = new Vector2(0f, 1f);
		tabRt.anchorMax = new Vector2(1f, 1f);
		tabRt.pivot = new Vector2(0.5f, 1f);
		tabRt.anchoredPosition = new Vector2(0f, -46f);
		tabRt.sizeDelta = new Vector2(0f, 38f);

		HorizontalLayoutGroup tabHlg = tabBar.AddComponent<HorizontalLayoutGroup>();
		tabHlg.padding = new RectOffset(10, 10, 4, 4);
		tabHlg.spacing = 6f;
		tabHlg.childControlWidth = true;
		tabHlg.childControlHeight = true;
		tabHlg.childForceExpandWidth = true;
		tabHlg.childForceExpandHeight = true;

		string[] tabNames = { "Cheats", "Towers", "Cards", "Card Trees", "Modules" };
		for (int i = 0; i < tabNames.Length; i++)
		{
			int idx = i;
			Button tb = CreateSimpleButton(tabRt, tabNames[i], Vector2.zero, Vector2.zero, BgButton, TextLight, 14, () => SelectTab((ModMenuTab)idx));
			_tabButtons[i] = tb;
			_tabImages[i] = tb.GetComponent<Image>();
			_tabTexts[i] = tb.GetComponentInChildren<Text>();
		}

		// ScrollRect body (top -86, bottom +30)
		GameObject scrollObj = new GameObject("ScrollBody", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		scrollObj.transform.SetParent(_windowRt, worldPositionStays: false);
		RectTransform scrollRt = scrollObj.GetComponent<RectTransform>();
		scrollRt.anchorMin = Vector2.zero;
		scrollRt.anchorMax = Vector2.one;
		scrollRt.offsetMin = new Vector2(10f, 32f);
		scrollRt.offsetMax = new Vector2(-18f, -88f);

		Image viewportImg = scrollObj.GetComponent<Image>();
		viewportImg.color = new Color(0f, 0f, 0f, 0.01f);
		Mask mask = scrollObj.GetComponent<Mask>();
		mask.showMaskGraphic = false;

		GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
		contentObj.transform.SetParent(scrollRt, worldPositionStays: false);
		_contentRt = contentObj.GetComponent<RectTransform>();
		_contentRt.anchorMin = new Vector2(0f, 1f);
		_contentRt.anchorMax = new Vector2(1f, 1f);
		_contentRt.pivot = new Vector2(0.5f, 1f);
		_contentRt.anchoredPosition = Vector2.zero;
		_contentRt.sizeDelta = Vector2.zero;

		VerticalLayoutGroup vlg = contentObj.GetComponent<VerticalLayoutGroup>();
		vlg.padding = new RectOffset(4, 8, 6, 12);
		vlg.spacing = 8f;
		vlg.childControlWidth = true;
		vlg.childControlHeight = true;
		vlg.childForceExpandWidth = true;
		vlg.childForceExpandHeight = false;

		ContentSizeFitter csf = contentObj.GetComponent<ContentSizeFitter>();
		csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		// Scrollbar
		GameObject sbObj = CreatePanel("Scrollbar", _windowRt, new Color(0.06f, 0.07f, 0.09f, 0.9f));
		RectTransform sbRt = sbObj.GetComponent<RectTransform>();
		sbRt.anchorMin = new Vector2(1f, 0f);
		sbRt.anchorMax = new Vector2(1f, 1f);
		sbRt.pivot = new Vector2(1f, 0.5f);
		sbRt.offsetMin = new Vector2(-14f, 32f);
		sbRt.offsetMax = new Vector2(-5f, -88f);

		GameObject handleObj = CreatePanel("Handle", sbRt, GoldAccent);
		RectTransform handleRt = handleObj.GetComponent<RectTransform>();
		StretchFill(handleRt);

		Scrollbar sb = sbObj.AddComponent<Scrollbar>();
		sb.direction = Scrollbar.Direction.BottomToTop;
		sb.handleRect = handleRt;
		sb.targetGraphic = handleObj.GetComponent<Image>();

		_scrollRect = scrollObj.GetComponent<ScrollRect>();
		_scrollRect.viewport = scrollRt;
		_scrollRect.content = _contentRt;
		_scrollRect.horizontal = false;
		_scrollRect.vertical = true;
		_scrollRect.movementType = ScrollRect.MovementType.Clamped;
		_scrollRect.scrollSensitivity = 38f;
		_scrollRect.verticalScrollbar = sb;
		_scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

		// Footer bar
		GameObject footer = CreatePanel("Footer", _windowRt, BgHeader);
		RectTransform fRt = footer.GetComponent<RectTransform>();
		fRt.anchorMin = new Vector2(0f, 0f);
		fRt.anchorMax = new Vector2(1f, 0f);
		fRt.pivot = new Vector2(0.5f, 0f);
		fRt.anchoredPosition = Vector2.zero;
		fRt.sizeDelta = new Vector2(0f, 28f);

		_toastText = CreateText(fRt, $"Hotkey: [{Plugin.MenuHotkey.Value}]  •  Drag header to move", 12, FontStyle.Normal, TextMuted, TextAnchor.MiddleLeft);
		_toastText.rectTransform.anchorMin = Vector2.zero;
		_toastText.rectTransform.anchorMax = Vector2.one;
		_toastText.rectTransform.offsetMin = new Vector2(12f, 0f);
		_toastText.rectTransform.offsetMax = new Vector2(-12f, 0f);
	}

	internal static void Toggle()
	{
		SetOpen(!_isOpen);
	}

	internal static void SetOpen(bool open)
	{
		EnsureCreated();
		_isOpen = open;
		if (_windowObj != null)
		{
			_windowObj.SetActive(open);
		}

		if (open)
		{
			if (Plugin.PauseWhileMenuOpen.Value && CheatActions.IsInGameRun && (PauseMenu.instance == null || !PauseMenu.instance.paused))
			{
				_prePauseSpeed = ModuleBridge.GetSpeed();
				if (_prePauseSpeed > 0.01f)
				{
					ModuleBridge.SetSpeed(0f);
					_pausedByMenu = true;
				}
			}
			SelectTab(_currentTab);
		}
		else
		{
			UnfocusSearch();
			if (_pausedByMenu)
			{
				_pausedByMenu = false;
				if (CheatActions.IsInGameRun && (PauseMenu.instance == null || !PauseMenu.instance.paused))
				{
					ModuleBridge.SetSpeed(_prePauseSpeed > 0.01f ? _prePauseSpeed : 1f);
				}
			}
		}
	}

	internal static void Tick()
	{
		if (_cornerBtnObj != null)
		{
			bool showCorner = Plugin.ShowCornerButton.Value && !InputBlockerPatches.IsPauseUiHidden();
			if (_cornerBtnObj.activeSelf != showCorner)
			{
				_cornerBtnObj.SetActive(showCorner);
			}
			if (showCorner && _cornerBtnText != null)
			{
				string lbl = $"Mods ({FormatKey(Plugin.MenuHotkey.Value)})";
				if (_cornerBtnText.text != lbl)
				{
					_cornerBtnText.text = lbl;
				}
			}
		}

		if (!IsMenuOpen)
		{
			return;
		}

		if (_toastUntil > 0f && Time.unscaledTime > _toastUntil)
		{
			_toastUntil = 0f;
			if (_toastText != null)
			{
				_toastText.color = TextMuted;
				_toastText.text = $"Hotkey: [{FormatKey(Plugin.MenuHotkey.Value)}]  •  Drag header to move  •  Esc to close";
			}
		}

		if (Time.unscaledTime >= _nextLiveRefreshTime)
		{
			_nextLiveRefreshTime = Time.unscaledTime + 0.2f;
			UpdateHeaderStatus();
			for (int i = 0; i < LiveSyncCallbacks.Count; i++)
			{
				LiveSyncCallbacks[i]?.Invoke();
			}
		}
	}

	private static void ShowToast(string message)
	{
		if (_toastText == null) return;
		_toastText.color = GoldAccent;
		_toastText.text = message;
		_toastUntil = Time.unscaledTime + 2.6f;
	}

	private static void UpdateHeaderStatus()
	{
		if (_statusPillText != null)
		{
			int cheats = CheatActions.ActiveCheatCount();
			if (CheatActions.IsInGameRun)
			{
				string goldStr = Plugin.InfiniteMoney.Value ? "INF" : $"{CheatActions.GetCurrentGold():N0}g";
				float spd = ModuleBridge.GetSpeed();
				string spdStr = spd <= 0.01f ? "Paused" : $"{spd:0.#}x";
				_statusPillText.text = $"Wave {CheatActions.GetCurrentWave()}  •  Gold: {goldStr}  •  Speed: {spdStr}  •  Cheats: {cheats}";
			}
			else
			{
				_statusPillText.text = $"Main Menu  •  XP: {CheatActions.GetMetaXP():N0}  •  Cheats: {cheats}";
			}
		}
		if (_pauseBtnText != null)
		{
			float spd = CheatActions.IsInGameRun ? ModuleBridge.GetSpeed() : 1f;
			_pauseBtnText.text = spd <= 0.01f ? "Resume" : "Pause";
		}
	}

	private static void SelectTab(ModMenuTab tab)
	{
		_currentTab = tab;
		for (int i = 0; i < 5; i++)
		{
			bool active = (i == (int)tab);
			if (_tabImages[i] != null)
			{
				_tabImages[i].color = active ? GoldAccent : BgButton;
			}
			if (_tabTexts[i] != null)
			{
				_tabTexts[i].color = active ? new Color(0.08f, 0.09f, 0.12f, 1f) : TextLight;
				_tabTexts[i].fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
			}
		}
		RebuildCurrentTab();
	}

	private static void RebuildCurrentTab()
	{
		if (_contentRt == null)
		{
			return;
		}
		LiveSyncCallbacks.Clear();
		_searchInput = null;

		for (int i = _contentRt.childCount - 1; i >= 0; i--)
		{
			Transform child = _contentRt.GetChild(i);
			child.gameObject.SetActive(false);
			child.SetParent(null, worldPositionStays: false);
			UnityEngine.Object.Destroy(child.gameObject);
		}

		switch (_currentTab)
		{
			case ModMenuTab.Cheats:
				BuildCheatsTab();
				break;
			case ModMenuTab.Towers:
				BuildTowersTab();
				break;
			case ModMenuTab.Cards:
				BuildCardsTab();
				break;
			case ModMenuTab.CardTrees:
				BuildCardTreesTab();
				break;
			case ModMenuTab.Modules:
				BuildModulesTab();
				break;
		}

		UpdateHeaderStatus();
		Canvas.ForceUpdateCanvases();
		LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRt);
		if (_scrollRect != null)
		{
			_scrollRect.verticalNormalizedPosition = 1f;
		}
	}

	// ======================== TAB 1: CHEATS ========================
	private static void BuildCheatsTab()
	{
		AddSectionHeader("Core Cheats");

		Transform grid = AddGrid(2, 340f, 36f, 8f);

		AddToggleTile(grid, "Infinite Money", () => Plugin.InfiniteMoney.Value, v =>
		{
			Plugin.InfiniteMoney.Value = v;
			if (v && CheatActions.IsInGameRun && CheatActions.GetCurrentGold() < 50000)
			{
				CheatActions.AddGold(50000);
			}
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
			ModuleBridge.SyncBanishOverride(v);
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
		AddCycleTile(grid, "Tower Damage Multiplier",
			() => $"{Plugin.DamageMultiplier.Value:0.#}x",
			() =>
			{
				Plugin.DamageMultiplier.Value = NextInCycle(dmgOpts, Plugin.DamageMultiplier.Value);
				CheatActions.RefreshAllPlacedTowers();
				ShowToast($"Tower Damage Multiplier: {Plugin.DamageMultiplier.Value:0.#}x");
			});

		float[] rpmOpts = { 1f, 2f, 3f, 5f };
		AddCycleTile(grid, "Tower Fire Rate Multiplier",
			() => $"{Plugin.FireRateMultiplier.Value:0.#}x",
			() =>
			{
				Plugin.FireRateMultiplier.Value = NextInCycle(rpmOpts, Plugin.FireRateMultiplier.Value);
				CheatActions.RefreshAllPlacedTowers();
				ShowToast($"Tower Fire Rate Multiplier: {Plugin.FireRateMultiplier.Value:0.#}x");
			});

		AddSectionHeader("Quick Resources & Grants");

		AddActionRow("Gold", new (string, Action)[]
		{
			("+1,000g", () => { RequireRun(() => { CheatActions.AddGold(1000); ShowToast("+1,000 Gold"); }); }),
			("+10,000g", () => { RequireRun(() => { CheatActions.AddGold(10000); ShowToast("+10,000 Gold"); }); }),
			("+100,000g", () => { RequireRun(() => { CheatActions.AddGold(100000); ShowToast("+100,000 Gold"); }); })
		});

		AddActionRow("Mana", new (string, Action)[]
		{
			("Fill Mana", () => { RequireRun(() => { CheatActions.FillMana(); ShowToast("Mana filled"); }); }),
			("+100 Max Mana", () => { RequireRun(() => { CheatActions.AddMaxMana(100); ShowToast("+100 Max Mana"); }); }),
			("+10 Mana/s", () => { RequireRun(() => { CheatActions.AddManaRegen(10); ShowToast("+10 Mana/s"); }); })
		});

		AddActionRow("Tower Health", new (string, Action)[]
		{
			("Full Repair", () => { RequireRun(() => { CheatActions.HealTowerFull(); ShowToast("Tower fully repaired"); }); }),
			("+10 Max HP", () => { RequireRun(() => { CheatActions.AddMaxTowerHealth(10); ShowToast("+10 Max Tower HP"); }); }),
			("+50 Max HP", () => { RequireRun(() => { CheatActions.AddMaxTowerHealth(50); ShowToast("+50 Max Tower HP"); }); })
		});

		AddActionRow("Meta Research XP", new (string, Action)[]
		{
			("+1,000 XP", () => { CheatActions.AddMetaXP(1000); ShowToast("+1,000 Meta XP"); }),
			("+25,000 XP", () => { CheatActions.AddMetaXP(25000); ShowToast("+25,000 Meta XP"); }),
			("+100,000 XP", () => { CheatActions.AddMetaXP(100000); ShowToast("+100,000 Meta XP"); })
		});

		AddSectionHeader("Run & Combat Actions");

		AddActionRow("Wave & Cards", new (string, Action)[]
		{
			("Draw 3 Cards", () => { RequireRun(() => { CheatActions.DrawCardsNow(3); ShowToast("Drawing 3 cards"); }); }),
			("Open Chests", () => { RequireRun(() => { int n = QuickHudManager.OpenAllChests(); ShowToast(n > 0 ? $"Opened {n} chest(s)" : "No chests on map"); }); }),
			("Chop Trees", () => { RequireRun(() => { int n = QuickHudManager.ChopAllTrees(); ShowToast(n > 0 ? $"Chopped {n} tree(s)" : "No trees on map"); }); }),
			("Kill All Enemies", () => { RequireRun(() => { int n = CheatActions.KillAllEnemies(); ShowToast($"Killed {n} enemies"); }); })
		});

		AddActionRow("Placed Towers", new (string, Action)[]
		{
			("Upgrade All Placed Towers (+10 H/A/S)", () =>
			{
				RequireRun(() =>
				{
					int n = CheatActions.UpgradeAllPlacedTowers(10);
					ShowToast(n > 0 ? $"Upgraded {n} placed tower(s) (+10 H/A/S)" : "No towers placed yet (or need gold)");
				});
			})
		});
	}

	// ======================== TAB 2: TOWERS & LOADOUTS ========================
	private static void BuildTowersTab()
	{
		if (!CheatActions.IsInGameRun)
		{
			AddNoticeBanner("Start a run to customize your hotbar order, toggle individual towers, and apply loadouts.");
			return;
		}

		AddSectionHeader("Hotbar Rules & Saved Loadouts");
		Transform optGrid = AddGrid(2, 340f, 34f, 8f);
		AddToggleTile(optGrid, "Auto-Sort Hotbar Order", () => Plugin.AutoSortHotbar.Value, v =>
		{
			Plugin.AutoSortHotbar.Value = v;
			if (v) HotbarAndLoadoutManager.ForceRefreshHotbar();
			ShowToast($"Auto-Sort Hotbar: {(v ? "ON" : "OFF")}");
		});
		AddToggleTile(optGrid, "Lock Card Pool to Hotbar", () => Plugin.LockCardPoolToHotbar.Value, v =>
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
			GameObject row = CreatePanel($"LoadoutSlot_{s}", _contentRt, BgCard);
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

			Button applyBtn = CreateSimpleButton(row.transform, "Apply", Vector2.zero, Vector2.zero, GreenOn, TextLight, 12, () =>
			{
				int n = HotbarAndLoadoutManager.ApplyLoadoutSlot(s, replaceExisting: true);
				ShowToast(n > 0 ? $"Applied Loadout Slot {s} ({n} items)" : $"Slot {s} is empty");
				RebuildCurrentTab();
			});
			applyBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 66f;

			Button saveBtn = CreateSimpleButton(row.transform, "Save Bar", Vector2.zero, Vector2.zero, GoldSoft, GoldAccent, 12, () =>
			{
				HotbarAndLoadoutManager.SaveCurrentToSlot(s);
				ShowToast($"Saved current hotbar to Slot {s}");
				RebuildCurrentTab();
			});
			saveBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;

			Button clrBtn = CreateSimpleButton(row.transform, "Clear", Vector2.zero, Vector2.zero, BgButton, TextMuted, 12, () =>
			{
				HotbarAndLoadoutManager.ClearSlot(s);
				ShowToast($"Cleared Loadout Slot {s}");
				RebuildCurrentTab();
			});
			clrBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 56f;
		}

		AddSectionHeader("Bulk Hotbar Actions");
		AddActionRow("Unlock / Order", new (string, Action)[]
		{
			("Unlock ALL Towers", () => { int n = CheatActions.GiveAllUnlocks(towers: true, buildings: false); HotbarAndLoadoutManager.ForceRefreshHotbar(); ShowToast($"Unlocked {n} tower(s)"); RebuildCurrentTab(); }),
			("Unlock ALL Buildings", () => { int n = CheatActions.GiveAllUnlocks(towers: false, buildings: true); HotbarAndLoadoutManager.ForceRefreshHotbar(); ShowToast($"Unlocked {n} building(s)"); RebuildCurrentTab(); }),
			("Unlock Everything", () => { int n = CheatActions.GiveAllUnlocks(towers: true, buildings: true); HotbarAndLoadoutManager.ForceRefreshHotbar(); ShowToast($"Unlocked {n} item(s)"); RebuildCurrentTab(); }),
			("Reset Order", () => { HotbarAndLoadoutManager.ResetDefaultOrder(); ShowToast("Reset hotbar order to preset"); RebuildCurrentTab(); })
		});

		AddSectionHeader("Row 1 — Towers (Hotbar Order: Left -> Right)");
		List<HotbarItemInfo> towers = HotbarAndLoadoutManager.GetOrderedItems(isTower: true);
		for (int i = 0; i < towers.Count; i++)
		{
			AddHotbarOrderRow(towers[i], i + 1, towers.Count);
		}

		AddSectionHeader("Row 2 — Buildings (Hotbar Order: Left -> Right)");
		List<HotbarItemInfo> blds = HotbarAndLoadoutManager.GetOrderedItems(isTower: false);
		for (int i = 0; i < blds.Count; i++)
		{
			AddHotbarOrderRow(blds[i], i + 1, blds.Count);
		}
	}

	private static void AddHotbarOrderRow(HotbarItemInfo info, int position, int total)
	{
		bool owned = HotbarAndLoadoutManager.IsItemUnlocked(info);
		GameObject row = CreatePanel($"Hotbar_{info.Id}", _contentRt, BgCard);
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

		Button upBtn = CreateSimpleButton(row.transform, "Up", Vector2.zero, Vector2.zero, BgButton, TextLight, 11, () =>
		{
			HotbarAndLoadoutManager.MoveItemInOrder(info.Id, -1);
			RebuildCurrentTab();
		});
		upBtn.interactable = position > 1;
		upBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 42f;

		Button downBtn = CreateSimpleButton(row.transform, "Dn", Vector2.zero, Vector2.zero, BgButton, TextLight, 11, () =>
		{
			HotbarAndLoadoutManager.MoveItemInOrder(info.Id, +1);
			RebuildCurrentTab();
		});
		downBtn.interactable = position < total;
		downBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 42f;

		Button toggleBtn = CreateSimpleButton(row.transform, owned ? "Active [Lock]" : "Locked [Give]", Vector2.zero, Vector2.zero,
			owned ? GreenOn : BgButton, TextLight, 12, () =>
			{
				bool nextState = !owned;
				HotbarAndLoadoutManager.SetItemUnlocked(info.Id, nextState);
				ShowToast($"{info.DisplayName}: {(nextState ? "Unlocked on hotbar" : "Removed from hotbar")}");
				RebuildCurrentTab();
			});
		toggleBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 112f;
	}

	// ======================== TAB 3: CARDS ========================
	private static void BuildCardsTab()
	{
		if (!CheatActions.IsInGameRun)
		{
			AddNoticeBanner("Start a run to browse and grant upgrade cards.");
			return;
		}

		// Search bar row
		GameObject searchRow = CreatePanel("SearchRow", _contentRt, Color.clear);
		LayoutElement srLe = searchRow.AddComponent<LayoutElement>();
		srLe.preferredHeight = 34f;
		HorizontalLayoutGroup srHlg = searchRow.AddComponent<HorizontalLayoutGroup>();
		srHlg.spacing = 6f;
		srHlg.childControlWidth = true;
		srHlg.childControlHeight = true;
		srHlg.childForceExpandWidth = false;

		GameObject inputBg = CreatePanel("SearchBox", searchRow.transform, BgInput);
		LayoutElement ibLe = inputBg.AddComponent<LayoutElement>();
		ibLe.flexibleWidth = 1f;
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

		Button clearBtn = CreateSimpleButton(searchRow.transform, "Clear", Vector2.zero, Vector2.zero, BgButton, TextLight, 12, () =>
		{
			_searchQuery = string.Empty;
			if (_searchInput != null) _searchInput.text = string.Empty;
			RebuildCardsList();
		});
		clearBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 64f;

		Button unresBtn = CreateSimpleButton(searchRow.transform,
			Plugin.IncludeUnresearchedCards.Value ? "Unresearched: SHOWN" : "Unresearched: HIDDEN",
			Vector2.zero, Vector2.zero,
			Plugin.IncludeUnresearchedCards.Value ? GoldSoft : BgButton, TextLight, 12, () =>
			{
				Plugin.IncludeUnresearchedCards.Value = !Plugin.IncludeUnresearchedCards.Value;
				RebuildCurrentTab();
			});
		unresBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 170f;

		// Filter pills row
		GameObject filterRow = CreatePanel("FilterRow", _contentRt, Color.clear);
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
			CreateSimpleButton(filterRow.transform, fc.ToString(), Vector2.zero, Vector2.zero,
				active ? GoldAccent : BgButton,
				active ? new Color(0.08f, 0.09f, 0.12f, 1f) : TextLight,
				12, () =>
				{
					_cardFilter = captured;
					RebuildCurrentTab();
				});
		}

		// Cards container
		GameObject listContainer = CreatePanel("CardsListContainer", _contentRt, Color.clear);
		VerticalLayoutGroup lVlg = listContainer.AddComponent<VerticalLayoutGroup>();
		lVlg.spacing = 4f;
		lVlg.childControlWidth = true;
		lVlg.childControlHeight = true;
		lVlg.childForceExpandWidth = true;
		lVlg.childForceExpandHeight = false;

		_cardsListContainer = listContainer.transform;
		_searchInput.onValueChanged.AddListener(val =>
		{
			_searchQuery = val ?? string.Empty;
			RebuildCardsList();
		});

		RebuildCardsList();
	}

	private static Transform _cardsListContainer;

	private static void RebuildCardsList()
	{
		if (_cardsListContainer == null)
		{
			return;
		}
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
			if (!showUnres && !e.Card.unlocked)
			{
				continue;
			}
			if (_cardFilter == CardFilterCategory.Available)
			{
				if (!CheatActions.IsCardCurrentlyAvailable(e)) continue;
			}
			else if (_cardFilter != CardFilterCategory.All && e.Category != _cardFilter)
			{
				continue;
			}

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
			AddNoticeBanner("No matching cards found.", _cardsListContainer);
			return;
		}

		int cap = Mathf.Min(matches.Count, 60);
		for (int i = 0; i < cap; i++)
		{
			AddCardRow(_cardsListContainer, matches[i]);
		}

		if (matches.Count > cap)
		{
			AddNoticeBanner($"Showing first {cap} of {matches.Count} matching cards — type in search or pick a category to narrow.", _cardsListContainer);
		}

		Canvas.ForceUpdateCanvases();
		LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRt);
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
		titleTxt.rectTransform.anchorMax = new Vector2(1f, 1f);
		titleTxt.rectTransform.offsetMin = Vector2.zero;
		titleTxt.rectTransform.offsetMax = Vector2.zero;

		string cleanDesc = (entry.Description ?? "").Replace('\n', ' ');
		if (cleanDesc.Length > 92) cleanDesc = cleanDesc.Substring(0, 89) + "...";
		Text descTxt = CreateText(info.transform, cleanDesc, 11, FontStyle.Normal, TextMuted, TextAnchor.LowerLeft);
		descTxt.rectTransform.anchorMin = new Vector2(0f, 0f);
		descTxt.rectTransform.anchorMax = new Vector2(1f, 0.5f);
		descTxt.rectTransform.offsetMin = Vector2.zero;
		descTxt.rectTransform.offsetMax = Vector2.zero;

		if (entry.Card.unlocks != null && entry.Card.unlocks.Count > 0)
		{
			Button brBtn = CreateSimpleButton(row.transform, "Branch", Vector2.zero, Vector2.zero, GoldSoft, GoldAccent, 12, () =>
			{
				int n = CheatActions.GiveSubBranch(entry.Card, Plugin.IncludeUnresearchedCards.Value);
				ShowToast(n > 0 ? $"Granted {n} card(s) from {entry.Title} branch" : $"{entry.Title} branch already owned");
				RebuildCardsList();
			});
			brBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 74f;
		}

		Button giveBtn = CreateSimpleButton(row.transform, owned ? "Owned" : "Give", Vector2.zero, Vector2.zero,
			owned ? new Color(0.14f, 0.28f, 0.20f, 1f) : GreenOn, TextLight, 12, () =>
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

	// ======================== TAB 4: CARD TREES ========================
	private static void BuildCardTreesTab()
	{
		if (!CheatActions.IsInGameRun)
		{
			AddNoticeBanner("Start a run to grant entire Card Trees or sub-branches.");
			return;
		}

		AddSectionHeader("Bulk Tree Grants");

		GameObject topBar = CreatePanel("TreeTopBar", _contentRt, Color.clear);
		topBar.AddComponent<LayoutElement>().preferredHeight = 34f;
		HorizontalLayoutGroup tbHlg = topBar.AddComponent<HorizontalLayoutGroup>();
		tbHlg.spacing = 6f;
		tbHlg.childControlWidth = true;
		tbHlg.childControlHeight = true;
		tbHlg.childForceExpandWidth = true;

		CreateSimpleButton(topBar.transform,
			Plugin.IncludeUnresearchedCards.Value ? "Include Unresearched: ON" : "Include Unresearched: OFF",
			Vector2.zero, Vector2.zero,
			Plugin.IncludeUnresearchedCards.Value ? GoldSoft : BgButton, TextLight, 12, () =>
			{
				Plugin.IncludeUnresearchedCards.Value = !Plugin.IncludeUnresearchedCards.Value;
				RebuildCurrentTab();
			});

		CreateSimpleButton(topBar.transform, "Give ALL Tower Trees", Vector2.zero, Vector2.zero, GreenOn, TextLight, 12, () =>
		{
			int n = CheatActions.GiveAllTreesByCategory(CardFilterCategory.Towers, Plugin.IncludeUnresearchedCards.Value);
			ShowToast($"Granted {n} tower card(s)");
			RebuildCurrentTab();
		});

		CreateSimpleButton(topBar.transform, "Give ALL DOT Trees", Vector2.zero, Vector2.zero, GreenOn, TextLight, 12, () =>
		{
			int n = CheatActions.GiveAllTreesByCategory(CardFilterCategory.DOT, Plugin.IncludeUnresearchedCards.Value);
			ShowToast($"Granted {n} DOT card(s)");
			RebuildCurrentTab();
		});

		CreateSimpleButton(topBar.transform, "Give ALL Non-Monster", Vector2.zero, Vector2.zero, GoldAccent, new Color(0.08f, 0.09f, 0.12f, 1f), 12, () =>
		{
			int n = CheatActions.GiveAllNonMonsterTrees(Plugin.IncludeUnresearchedCards.Value);
			ShowToast($"Granted {n} card(s) across all trees");
			RebuildCurrentTab();
		});

		IReadOnlyList<TreeGroupSummary> groups = CheatActions.GetTreeGroups();
		CardFilterCategory? lastCat = null;

		foreach (TreeGroupSummary g in groups)
		{
			if (lastCat != g.Category)
			{
				lastCat = g.Category;
				AddSectionHeader(g.Category.ToString());
			}
			AddTreeGroupRow(g);
		}
	}

	private static void AddTreeGroupRow(TreeGroupSummary group)
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

		GameObject row = CreatePanel($"Tree_{group.TreeKey}", _contentRt, BgCard);
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
			Button expBtn = CreateSimpleButton(row.transform, expanded ? "Branches ▲" : "Branches ▼", Vector2.zero, Vector2.zero, BgButton, TextLight, 12, () =>
			{
				if (expanded) ExpandedTrees.Remove(group.TreeKey);
				else ExpandedTrees.Add(group.TreeKey);
				RebuildCurrentTab();
			});
			expBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 100f;
		}

		bool allOwned = eligibleCount > 0 && ownedCount >= eligibleCount;
		Button giveBtn = CreateSimpleButton(row.transform, allOwned ? "Complete" : "Give Tree", Vector2.zero, Vector2.zero,
			allOwned ? new Color(0.14f, 0.28f, 0.20f, 1f) : GreenOn, TextLight, 12, () =>
			{
				int n = CheatActions.GiveCardTree(group.TreeKey, Plugin.IncludeUnresearchedCards.Value);
				ShowToast(n > 0 ? $"Granted {n} card(s) in {group.TreeLabel}" : $"{group.TreeLabel} already complete");
				RebuildCurrentTab();
			});
		giveBtn.interactable = !allOwned && eligibleCount > 0;
		giveBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;

		if (expanded && group.BranchRoots.Count > 0)
		{
			GameObject subBox = CreatePanel("SubBranches", _contentRt, new Color(0.10f, 0.115f, 0.155f, 0.95f));
			VerticalLayoutGroup svlg = subBox.AddComponent<VerticalLayoutGroup>();
			svlg.padding = new RectOffset(18, 10, 6, 6);
			svlg.spacing = 4f;
			svlg.childControlWidth = true;
			svlg.childControlHeight = true;
			svlg.childForceExpandWidth = true;

			Transform subGrid = AddGrid(2, 340f, 30f, 6f, subBox.transform);
			foreach (CardCatalogEntry root in group.BranchRoots)
			{
				CreateSimpleButton(subGrid, $"Give Branch: {root.Title}", Vector2.zero, Vector2.zero, GoldSoft, GoldAccent, 12, () =>
				{
					int n = CheatActions.GiveSubBranch(root.Card, Plugin.IncludeUnresearchedCards.Value);
					ShowToast(n > 0 ? $"Granted {n} card(s) in {root.Title} branch" : $"{root.Title} branch already owned");
					RebuildCurrentTab();
				});
			}
		}
	}

	// ======================== TAB 5: MODULES ========================
	private static void BuildModulesTab()
	{
		// 1. Speed Controls
		AddSectionHeader("Game Speed (SpeedControls)");
		AddActionRow("Live Speed", new (string, Action)[]
		{
			("Pause", () => { ModuleBridge.SetSpeed(0f); ShowToast("Paused"); }),
			("1x", () => { ModuleBridge.SetSpeed(1f); ShowToast("Speed: 1x"); }),
			("2x", () => { ModuleBridge.SetSpeed(2f); ShowToast("Speed: 2x"); }),
			("3x", () => { ModuleBridge.SetSpeed(3f); ShowToast("Speed: 3x"); }),
			("5x", () => { ModuleBridge.SetSpeed(5f); ShowToast("Speed: 5x"); }),
			("8x", () => { ModuleBridge.SetSpeed(8f); ShowToast("Speed: 8x"); })
		});

		if (ModuleBridge.HasSpeedControls)
		{
			Transform spGrid = AddGrid(2, 340f, 34f, 8f);
			AddPluginConfigToggle(spGrid, "Remember Speed Between Runs", ModuleBridge.Keys.SpeedControls, "General", "RememberSpeedBetweenLoads");
			AddPluginConfigToggle(spGrid, "Allow Building While Paused", ModuleBridge.Keys.SpeedControls, "General", "AllowBuildingWhilePaused");
			AddPluginConfigToggle(spGrid, "Cap Speed to 1x on Expand", ModuleBridge.Keys.SpeedControls, "General", "ResetSpeedOnExpand");
		}

		// 2. Quick Chests, Trees & Tower Fast Upgrades
		AddSectionHeader("Quick HUD & Tower Fast Upgrades");
		Transform qhGrid = AddGrid(2, 340f, 34f, 8f);

		AddCycleTile(qhGrid, "Chest Mode", () => Plugin.ChestMode.Value.ToString(), () =>
		{
			int next = ((int)Plugin.ChestMode.Value + 1) % 3;
			Plugin.ChestMode.Value = (ChestHandlingMode)next;
			ShowToast($"Chest Mode: {Plugin.ChestMode.Value}");
		});
		AddToggleTile(qhGrid, "Chop Trees HUD Button", () => Plugin.ShowChopAllTreesButton.Value, v => Plugin.ShowChopAllTreesButton.Value = v);
		AddToggleTile(qhGrid, "Early Expand (Skip Wave Wait)", () => Plugin.AllowEarlyExpand.Value, v => Plugin.AllowEarlyExpand.Value = v);
		AddToggleTile(qhGrid, "Tower Hold & +10 Snap Panel", () => Plugin.EnableTowerFastUpgrade.Value, v => Plugin.EnableTowerFastUpgrade.Value = v);
		AddCycleTile(qhGrid, "Hold Upgrade Speed", () => Plugin.HoldRepeatSpeed.Value switch { 0 => "Normal", 2 => "Turbo", _ => "Fast" }, () =>
		{
			Plugin.HoldRepeatSpeed.Value = (Plugin.HoldRepeatSpeed.Value + 1) % 3;
		});

		// 3. Quick Research Tree & Auto-Max
		if (ModuleBridge.HasQuickTree)
		{
			AddSectionHeader("Quick Research Tree & Auto-Max");
			Transform qtGrid = AddGrid(2, 340f, 34f, 8f);

			AddCycleTile(qtGrid, "In-Game Auto-Max Mode", () => ModuleBridge.GetAutoMaxMode() switch
			{
				1 => "Branch Line",
				2 => "Full Subtree",
				3 => "Whole Tower",
				4 => "All Researched",
				_ => "OFF"
			}, () =>
			{
				int next = (ModuleBridge.GetAutoMaxMode() + 1) % 5;
				ModuleBridge.SetAutoMaxMode(next);
				ShowToast($"Auto-Max Mode: {next}");
			});
			AddPluginConfigToggle(qtGrid, "Reset Auto-Max Each Run", ModuleBridge.Keys.QuickTree, "4. In-Game Auto-Max Branch", "ResetAutoMaxEachRun");
			AddPluginConfigToggle(qtGrid, "Show Auto-Max HUD Button", ModuleBridge.Keys.QuickTree, "4. In-Game Auto-Max Branch", "ShowInGameAutoMaxHudButton");
			AddPluginConfigToggle(qtGrid, "Skip +1 Stat Permanents", ModuleBridge.Keys.QuickTree, "3. Smart Buy Filters", "SkipPermanentStatUpgradesByDefault");
			AddPluginConfigToggle(qtGrid, "Skip Mana Consuming Cards", ModuleBridge.Keys.QuickTree, "3. Smart Buy Filters", "SkipManaConsumingUpgradesByDefault");
		}

		// 4. Custom Maps
		if (ModuleBridge.HasCustomMaps)
		{
			AddSectionHeader("Custom Maps");
			Transform cmGrid = AddGrid(2, 340f, 34f, 8f);
			string[] presets = ModuleBridge.GetMapPresetNames();
			AddCycleTile(cmGrid, "Next Run Map", () =>
			{
				int idx = Mathf.Clamp(ModuleBridge.GetSelectedMapIndex(), 0, presets.Length - 1);
				return presets[idx];
			}, () =>
			{
				int next = (ModuleBridge.GetSelectedMapIndex() + 1) % presets.Length;
				ModuleBridge.SetSelectedMapIndex(next);
				ShowToast($"Next Run Map: {presets[next]}");
			});
			AddPluginConfigToggle(cmGrid, "Remember Map Across Runs", ModuleBridge.Keys.CustomMaps, "General", "RememberMapAcrossRuns");
		}

		// 5. Levels to RPM
		if (ModuleBridge.HasLevelsToRpm)
		{
			AddSectionHeader("Levels to RPM");
			Transform rpmGrid = AddGrid(2, 340f, 34f, 8f);
			AddPluginConfigToggle(rpmGrid, "Levels to RPM Enabled", ModuleBridge.Keys.LevelsToRpm, "General", "Enabled", () => ModuleBridge.RefreshLevelsToRpmTowers());
			float[] rpmVals = { 0.05f, 0.07f, 0.10f, 0.15f, 0.25f };
			AddCycleTile(rpmGrid, "RPM Bonus / Level", () =>
			{
				ModuleBridge.TryGetConfig(ModuleBridge.Keys.LevelsToRpm, "General", "RpmBonusPerLevel", out float v);
				return $"+{Mathf.RoundToInt(v * 100f)}%";
			}, () =>
			{
				ModuleBridge.TryGetConfig(ModuleBridge.Keys.LevelsToRpm, "General", "RpmBonusPerLevel", out float cur);
				float next = NextInCycle(rpmVals, cur);
				ModuleBridge.SetConfig(ModuleBridge.Keys.LevelsToRpm, "General", "RpmBonusPerLevel", next);
				ModuleBridge.RefreshLevelsToRpmTowers();
			});
			AddPluginConfigToggle(rpmGrid, "Bonus Starts at Level 2", ModuleBridge.Keys.LevelsToRpm, "General", "BonusStartsAtLevel2", () => ModuleBridge.RefreshLevelsToRpmTowers());
		}

		// 6. Late-Game Lag Fix
		if (ModuleBridge.IsLoaded(ModuleBridge.Keys.LagFix))
		{
			AddSectionHeader("Late-Game Lag Fix");
			Transform lagGrid = AddGrid(2, 340f, 34f, 8f);
			AddPluginConfigToggle(lagGrid, "Fast-Forward Physics Scale", ModuleBridge.Keys.LagFix, "1. CPU & Targeting", "OptimizeFastForwardPhysics");
			AddPluginConfigToggle(lagGrid, "Targeting & Component Cache", ModuleBridge.Keys.LagFix, "1. CPU & Targeting", "OptimizeTargetingAndEnemyLookups");
			AddPluginConfigToggle(lagGrid, "HealthBar & HUD Throttling", ModuleBridge.Keys.LagFix, "2. UI & HealthBars", "OptimizeEnemyHealthBars");
			AddPluginConfigToggle(lagGrid, "Damage Number Coalescing", ModuleBridge.Keys.LagFix, "3. Damage Numbers", "ThrottleDamageNumbers");
			AddPluginConfigToggle(lagGrid, "Projectile & Explosion FX Cap", ModuleBridge.Keys.LagFix, "4. Visual FX", "OptimizeProjectileAndExplosionFX");
			AddPluginConfigToggle(lagGrid, "Tesla / Vampire Trail Fix", ModuleBridge.Keys.LagFix, "4. Visual FX", "FixTeslaAndVampireTrailCoroutines");
			AddPluginConfigToggle(lagGrid, "Combat Audio Deduplication", ModuleBridge.Keys.LagFix, "5. Audio", "DeduplicateCombatAudio");
		}

		// 7. QoL, Banish, DarkMode, Progression
		AddSectionHeader("QoL, Visuals & Progression");
		Transform miscGrid = AddGrid(2, 340f, 34f, 8f);

		if (ModuleBridge.IsLoaded(ModuleBridge.Keys.QoL))
		{
			AddPluginConfigToggle(miscGrid, "Build Mode Ghost Range Circle", ModuleBridge.Keys.QoL, "General", "EnableGhostRangeCircle");
			AddPluginConfigToggle(miscGrid, "Hover Tower Range Circle", ModuleBridge.Keys.QoL, "General", "EnableHoverRangeCircle");
			AddPluginConfigToggle(miscGrid, "Tower Priority Colors", ModuleBridge.Keys.QoL, "General", "EnablePriorityColors");
			AddPluginConfigToggle(miscGrid, "Persist Tower Priorities", ModuleBridge.Keys.QoL, "General", "PersistPriorities");
		}

		if (ModuleBridge.HasBanishCards)
		{
			int[] maxBanishOpts = { 3, 5, 10, 25, 99 };
			AddCycleTile(miscGrid, "Max Banishes / Run", () =>
			{
				ModuleBridge.TryGetConfig(ModuleBridge.Keys.Banish, "General", "MaxBanishes", out int v);
				return v.ToString();
			}, () =>
			{
				ModuleBridge.TryGetConfig(ModuleBridge.Keys.Banish, "General", "MaxBanishes", out int cur);
				int next = NextInCycle(maxBanishOpts, cur);
				ModuleBridge.SetConfig(ModuleBridge.Keys.Banish, "General", "MaxBanishes", next);
				ModuleBridge.SyncBanishOverride(Plugin.InfiniteBanishes.Value);
			});
		}

		if (ModuleBridge.HasThemes)
		{
			AddSectionHeader("Light / Dark Visual Theme (RogueTowerThemes)");
			Transform thGrid = AddGrid(2, 340f, 34f, 8f);
			AddCycleTile(thGrid, "Active Visual Theme", () => ModuleBridge.GetThemeModeName(), () =>
			{
				int next = (ModuleBridge.GetThemeMode() + 1) % 3;
				ModuleBridge.SetThemeMode(next);
				ShowToast($"Theme: {ModuleBridge.GetThemeModeName()}");
			});
			float[] brightOpts = { 0.80f, 0.92f, 1.00f, 1.08f };
			AddCycleTile(thGrid, "Dark UI Brightness", () =>
			{
				ModuleBridge.TryGetConfig(ModuleBridge.Keys.Themes, "2. Dark Preset", "DarkUiBrightness", out float v);
				return $"{Mathf.RoundToInt(v * 100f)}%";
			}, () =>
			{
				ModuleBridge.TryGetConfig(ModuleBridge.Keys.Themes, "2. Dark Preset", "DarkUiBrightness", out float cur);
				float next = NextInCycle(brightOpts, cur);
				ModuleBridge.SetConfig(ModuleBridge.Keys.Themes, "2. Dark Preset", "DarkUiBrightness", next);
				ModuleBridge.ApplyTheme();
			});
			AddCycleTile(thGrid, "Light UI Brightness", () =>
			{
				ModuleBridge.TryGetConfig(ModuleBridge.Keys.Themes, "3. Light Preset", "LightUiBrightness", out float v);
				return $"{Mathf.RoundToInt(v * 100f)}%";
			}, () =>
			{
				ModuleBridge.TryGetConfig(ModuleBridge.Keys.Themes, "3. Light Preset", "LightUiBrightness", out float cur);
				float next = NextInCycle(brightOpts, cur);
				ModuleBridge.SetConfig(ModuleBridge.Keys.Themes, "3. Light Preset", "LightUiBrightness", next);
				ModuleBridge.ApplyTheme();
			});
			AddPluginConfigToggle(thGrid, "Dark High-Contrast HUD Text", ModuleBridge.Keys.Themes, "2. Dark Preset", "DarkHighContrastText", () => ModuleBridge.ApplyTheme());
			AddPluginConfigToggle(thGrid, "Light High-Contrast HUD Text", ModuleBridge.Keys.Themes, "3. Light Preset", "LightHighContrastText", () => ModuleBridge.ApplyTheme());
		}
		else if (ModuleBridge.HasDarkMode)
		{
			AddPluginConfigToggle(miscGrid, "Dark Mode Enabled", ModuleBridge.Keys.DarkMode, "General", "Enabled", () => ModuleBridge.ApplyDarkMode());
		}

		if (ModuleBridge.HasCleanQoL)
		{
			AddSectionHeader("Clean-Room QoL (RogueTowerQoL)");
			Transform cqGrid = AddGrid(2, 340f, 34f, 8f);
			AddPluginConfigToggle(cqGrid, "Build-Mode Range Ring", ModuleBridge.Keys.CleanQoL, "1. Range Circles", "EnableGhostRange");
			AddPluginConfigToggle(cqGrid, "Hover Tower Range Ring", ModuleBridge.Keys.CleanQoL, "1. Range Circles", "EnableHoverRange");
			AddPluginConfigToggle(cqGrid, "Priority Tint Colors", ModuleBridge.Keys.CleanQoL, "2. Targeting Priorities", "EnablePriorityColors");
			AddPluginConfigToggle(cqGrid, "Levels to RPM (+7%/lvl)", ModuleBridge.Keys.CleanQoL, "3. Levels to RPM", "EnableLevelsToRpm", () =>
			{
				if (TowerManager.instance != null) TowerManager.instance.UpdateAllTowers();
			});
		}

		if (ModuleBridge.IsLoaded(ModuleBridge.Keys.DisableProg))
		{
			AddPluginConfigToggle(miscGrid, "Block Achievements (DisableTriumphs)", ModuleBridge.Keys.DisableProg, "General", "Enabled");
		}

		// 8. Camera, Placement & Test Harness
		AddSectionHeader("Camera Rotation, Hidden Tile Placement & Test Harness");
		Transform camGrid = AddGrid(2, 340f, 34f, 8f);
		AddToggleTile(camGrid, "Camera Rotation ([ / ] / Header)", () => Plugin.EnableCameraRotation.Value, v =>
		{
			Plugin.EnableCameraRotation.Value = v;
			if (!v) CameraAndPlacementHelper.ResetCameraView();
		});
		AddToggleTile(camGrid, "Auto Top-Down in Build Mode", () => Plugin.AutoTopDownInBuildMode.Value, v => Plugin.AutoTopDownInBuildMode.Value = v);
		AddToggleTile(camGrid, "X-Ray Build Behind Cliffs/Towers", () => Plugin.EnableXRayPlacement.Value, v => Plugin.EnableXRayPlacement.Value = v);
		AddToggleTile(camGrid, "Hold LeftAlt Cutaway & Deep Select", () => Plugin.EnableAltCutaway.Value, v => Plugin.EnableAltCutaway.Value = v);
		AddToggleTile(camGrid, "Focusless Test Server (127.0.0.1)", () => Plugin.EnableTestServer.Value, v =>
		{
			Plugin.EnableTestServer.Value = v;
			ShowToast("Restart game to apply Test Server port bind change.");
		});

		AddSectionHeader("Endgame FPS Benchmark Scenes (Round 40 / 45)");
		MonoBehaviour benchHost = Chainloader.PluginInfos.TryGetValue(Plugin.PluginGuid, out var selfInfo) ? selfInfo.Instance : null;
		AddActionRow("Load Scene", new (string, Action)[]
		{
			("R40 Avg (8p)", () => { BenchmarkSceneRunner.StartBenchmark(benchHost, "r40_vanilla_avg", 12f, 2f, true); ShowToast("Loading R40 Avg (8p) benchmark..."); }),
			("R40 High (13p)", () => { BenchmarkSceneRunner.StartBenchmark(benchHost, "r40_high_12p", 12f, 2f, true); ShowToast("Loading R40 High-Roll (13p) benchmark..."); }),
			("R40 Split (21p)", () => { BenchmarkSceneRunner.StartBenchmark(benchHost, "r40_extreme_20p", 12f, 2f, true); ShowToast("Loading R40 Split-Mode (21p) benchmark..."); }),
			("R45 Boss 3x", () => { BenchmarkSceneRunner.StartBenchmark(benchHost, "r45_boss_chaos", 12f, 2f, true); ShowToast("Loading R45 Boss + 20p @ 3x benchmark..."); }),
			("Omega 300T 8x", () => { BenchmarkSceneRunner.StartBenchmark(benchHost, "r45_omega_all20_infrange", 20f, 2f, true); ShowToast("Loading 300-Tower InfRange 8x benchmark..."); })
		});

		// 9. Mod Menu Settings
		AddSectionHeader("Mod Menu Settings");
		Transform mmGrid = AddGrid(2, 340f, 34f, 8f);
		KeyCode[] hotkeys = { KeyCode.BackQuote, KeyCode.F1, KeyCode.F4, KeyCode.F8, KeyCode.F9, KeyCode.Backslash };
		AddCycleTile(mmGrid, "Mod Menu Hotkey", () => FormatKey(Plugin.MenuHotkey.Value), () =>
		{
			Plugin.MenuHotkey.Value = NextInCycle(hotkeys, Plugin.MenuHotkey.Value);
			ShowToast($"Mod Menu Hotkey: {FormatKey(Plugin.MenuHotkey.Value)}");
		});
		AddToggleTile(mmGrid, "Show Top-Right Mods Pill", () => Plugin.ShowCornerButton.Value, v => Plugin.ShowCornerButton.Value = v);
		AddToggleTile(mmGrid, "Pause Game While Menu Open", () => Plugin.PauseWhileMenuOpen.Value, v => Plugin.PauseWhileMenuOpen.Value = v);
	}

	// ======================== UI HELPERS ========================
	private static void RequireRun(Action action)
	{
		if (!CheatActions.IsInGameRun)
		{
			ShowToast("Start a run first to use this in-game action.");
			return;
		}
		action();
	}

	private static T NextInCycle<T>(T[] options, T current)
	{
		for (int i = 0; i < options.Length; i++)
		{
			if (EqualityComparer<T>.Default.Equals(options[i], current))
			{
				return options[(i + 1) % options.Length];
			}
		}
		return options[0];
	}

	private static string FormatKey(KeyCode k)
	{
		return k == KeyCode.BackQuote ? "`" : k.ToString();
	}

	private static void AddSectionHeader(string title)
	{
		GameObject obj = CreatePanel("SectionHeader", _contentRt, Color.clear);
		obj.AddComponent<LayoutElement>().preferredHeight = 26f;
		Text txt = CreateText(obj.transform, title.ToUpperInvariant(), 12, FontStyle.Bold, GoldAccent, TextAnchor.LowerLeft);
		StretchFill(txt.rectTransform, 4f, 2f, 4f, 2f);
	}

	private static void AddNoticeBanner(string message, Transform parent = null)
	{
		GameObject obj = CreatePanel("Notice", parent ?? _contentRt, BgCard);
		obj.AddComponent<LayoutElement>().preferredHeight = 40f;
		Text txt = CreateText(obj.transform, message, 13, FontStyle.Normal, TextMuted, TextAnchor.MiddleCenter);
		StretchFill(txt.rectTransform, 10f, 4f, 10f, 4f);
	}

	private static Transform AddGrid(int columns, float cellWidth, float cellHeight, float spacing, Transform parent = null)
	{
		GameObject gridObj = CreatePanel("Grid", parent ?? _contentRt, Color.clear);
		GridLayoutGroup glg = gridObj.AddComponent<GridLayoutGroup>();
		glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		glg.constraintCount = columns;
		glg.cellSize = new Vector2(cellWidth, cellHeight);
		glg.spacing = new Vector2(spacing, spacing);
		glg.childAlignment = TextAnchor.UpperLeft;
		return gridObj.transform;
	}

	private static void AddActionRow(string label, (string text, Action onClick)[] buttons)
	{
		GameObject row = CreatePanel("ActionRow", _contentRt, BgCard);
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
		LayoutElement lblLe = lbl.gameObject.AddComponent<LayoutElement>();
		lblLe.preferredWidth = 140f;

		foreach (var (text, onClick) in buttons)
		{
			Button b = CreateSimpleButton(row.transform, text, Vector2.zero, Vector2.zero, BgButton, TextLight, 12, onClick);
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

		btn.onClick.AddListener(() =>
		{
			setter(!getter());
			Sync();
			UpdateHeaderStatus();
		});

		Sync();
		LiveSyncCallbacks.Add(Sync);
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

		void Sync()
		{
			badgeTxt.text = valueGetter();
		}

		btn.onClick.AddListener(() =>
		{
			onCycle();
			Sync();
			UpdateHeaderStatus();
		});

		Sync();
		LiveSyncCallbacks.Add(Sync);
	}

	private static void AddPluginConfigToggle(Transform parent, string label, string guid, string section, string key, Action afterSet = null)
	{
		AddToggleTile(
			parent,
			label,
			() => ModuleBridge.TryGetConfig(guid, section, key, out bool v) && v,
			v =>
			{
				ModuleBridge.SetConfig(guid, section, key, v);
				afterSet?.Invoke();
				ShowToast($"{label}: {(v ? "ON" : "OFF")}");
			});
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

	private static Button CreateSimpleButton(Transform parent, string text, Vector2 anchoredPos, Vector2 size, Color bgColor, Color textColor, int fontSize, Action onClick)
	{
		GameObject obj = CreatePanel("Btn_" + text, parent, bgColor);
		RectTransform rt = obj.GetComponent<RectTransform>();
		rt.anchoredPosition = anchoredPos;
		rt.sizeDelta = size;

		Button btn = obj.AddComponent<Button>();
		ColorBlock cb = btn.colors;
		cb.normalColor = Color.white;
		cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
		cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
		cb.disabledColor = new Color(0.65f, 0.65f, 0.65f, 0.65f);
		btn.colors = cb;
		btn.navigation = new Navigation { mode = Navigation.Mode.None };
		if (onClick != null)
		{
			btn.onClick.AddListener(() => onClick());
		}

		Text txt = CreateText(obj.transform, text, fontSize, FontStyle.Bold, textColor, TextAnchor.MiddleCenter);
		StretchFill(txt.rectTransform, 4f, 2f, 4f, 2f);
		return btn;
	}

	private static Text CreateText(Transform parent, string text, int fontSize, FontStyle style, Color color, TextAnchor align)
	{
		GameObject obj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
		obj.transform.SetParent(parent, worldPositionStays: false);
		Text t = obj.GetComponent<Text>();
		t.font = _uiFont;
		t.text = text;
		t.fontSize = fontSize;
		t.fontStyle = style;
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
