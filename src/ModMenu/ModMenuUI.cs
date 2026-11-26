using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RogueTowerModMenu;

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
		return !ModMenuUI.IsPointerOverMenu;
	}

	[HarmonyPatch(typeof(BuildingManager), "Update")]
	[HarmonyPrefix]
	private static bool BuildingManager_Update_Prefix()
	{
		return !(ModMenuUI.IsPointerOverMenu || ModMenuUI.IsTextInputFocused);
	}

	[HarmonyPatch(typeof(CameraController), "UpdateZoom")]
	[HarmonyPrefix]
	private static bool CameraController_UpdateZoom_Prefix()
	{
		return !ModMenuUI.IsPointerOverMenu;
	}

	[HarmonyPatch(typeof(CameraController), "UpdateMovement")]
	[HarmonyPrefix]
	private static bool CameraController_UpdateMovement_Prefix()
	{
		return !(ModMenuUI.IsTextInputFocused || ModMenuUI.IsPointerOverMenu);
	}

	[HarmonyPatch(typeof(PauseMenu), "Update")]
	[HarmonyPrefix]
	private static bool PauseMenu_Update_Prefix()
	{
		if (ModMenuUI.IsTextInputFocused)
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				ModMenuUI.UnfocusInputs();
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

internal static class ModMenuUI
{
	private sealed class TabRegistration
	{
		public string Id;
		public string Title;
		public int Order;
		public bool IsExternal;
		public Action<RectTransform> BuildCallback;
	}

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
				next.x = Mathf.Clamp(next.x, -240f, 240f);
				next.y = Mathf.Clamp(next.y, -95f, 95f);
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

	private static readonly AccessTools.FieldRef<ResourceManager, int> GoldRef =
		AccessTools.FieldRefAccess<ResourceManager, int>("gold");

	private static Font _fontRegular;
	private static Font _fontBold;
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

	private static RectTransform _tabBarRt;
	private static RectTransform _contentRt;
	private static ScrollRect _scrollRect;

	private static readonly List<TabRegistration> Tabs = new List<TabRegistration>();
	private static readonly List<Button> TabButtons = new List<Button>();
	private static readonly List<Image> TabImages = new List<Image>();
	private static readonly List<Text> TabTexts = new List<Text>();

	private static readonly List<Func<string>> HeaderBadgeProviders = new List<Func<string>>();
	private static readonly List<Action> LiveSyncCallbacks = new List<Action>();

	private static string _currentTabId = "mods";
	private static string _settingsFocusGuid = "";
	private static bool _isOpen;
	private static bool _pausedByMenu;
	private static float _prePauseSpeed = 1f;
	private static float _nextLiveRefreshTime;
	private static float _nextProfilerRedrawTime;

	internal static bool IsMenuOpen => _isOpen && _windowObj != null && _windowObj.activeInHierarchy;
	internal static string CurrentTabId => _currentTabId;
	internal static int CurrentTabIndex
	{
		get
		{
			EnsureBuiltInTabs();
			int idx = Tabs.FindIndex(t => string.Equals(t.Id, _currentTabId, StringComparison.OrdinalIgnoreCase));
			return idx >= 0 ? idx : 0;
		}
	}

	internal static bool IsTextInputFocused
	{
		get
		{
			if (!IsMenuOpen || EventSystem.current == null || _windowRt == null) return false;
			GameObject sel = EventSystem.current.currentSelectedGameObject;
			return sel != null && sel.transform.IsChildOf(_windowRt) && sel.GetComponent<InputField>() != null;
		}
	}

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

	internal static void UnfocusInputs()
	{
		if (EventSystem.current != null && IsTextInputFocused)
		{
			EventSystem.current.SetSelectedGameObject(null);
		}
	}

	private static void EnsureBuiltInTabs()
	{
		if (Tabs.Exists(t => !t.IsExternal)) return;
		Tabs.Add(new TabRegistration { Id = "mods", Title = "Mods", Order = 10, IsExternal = false, BuildCallback = _ => BuildModsTab() });
		Tabs.Add(new TabRegistration { Id = "settings", Title = "Settings", Order = 20, IsExternal = false, BuildCallback = _ => BuildSettingsTab() });
		Tabs.Add(new TabRegistration { Id = "profiler", Title = "Profiler", Order = 30, IsExternal = false, BuildCallback = _ => BuildProfilerTab() });
		SortTabs();
	}

	private static void SortTabs()
	{
		Tabs.Sort((a, b) =>
		{
			int c = a.Order.CompareTo(b.Order);
			return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
		});
	}

	internal static void RegisterExternalTab(string id, string title, int order, Action<RectTransform> buildCallback)
	{
		if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || buildCallback == null) return;
		EnsureBuiltInTabs();
		Tabs.RemoveAll(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
		Tabs.Add(new TabRegistration
		{
			Id = id.Trim().ToLowerInvariant(),
			Title = title.Trim(),
			Order = order,
			IsExternal = true,
			BuildCallback = buildCallback
		});
		SortTabs();
		if (_tabBarRt != null) RebuildTabBarButtons();
	}

	internal static void UnregisterExternalTab(string id)
	{
		if (string.IsNullOrWhiteSpace(id)) return;
		Tabs.RemoveAll(t => t.IsExternal && string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
		if (string.Equals(_currentTabId, id, StringComparison.OrdinalIgnoreCase)) _currentTabId = "mods";
		if (_tabBarRt != null) RebuildTabBarButtons();
	}

	internal static void RegisterHeaderBadgeProvider(Func<string> provider)
	{
		if (provider != null && !HeaderBadgeProviders.Contains(provider))
		{
			HeaderBadgeProviders.Add(provider);
		}
	}

	internal static void RegisterExternalLiveSync(Action callback)
	{
		if (callback != null) LiveSyncCallbacks.Add(callback);
	}

	internal static void ClearExternalRegistrations()
	{
		Tabs.RemoveAll(t => t.IsExternal);
		HeaderBadgeProviders.Clear();
		if (_tabBarRt != null) RebuildTabBarButtons();
	}

	internal static void OpenToTabId(string id)
	{
		SetOpen(true);
		SelectTabByIdOrTitle(id);
	}

	internal static void SelectTabIndex(int idx)
	{
		EnsureBuiltInTabs();
		if (Tabs.Count == 0) return;
		int clamped = Mathf.Clamp(idx, 0, Tabs.Count - 1);
		SelectTab(Tabs[clamped].Id);
	}

	internal static void SelectTabByIdOrTitle(string key)
	{
		EnsureBuiltInTabs();
		if (string.IsNullOrWhiteSpace(key))
		{
			SelectTab("mods");
			return;
		}
		string k = key.Trim();
		TabRegistration hit = Tabs.Find(t =>
			string.Equals(t.Id, k, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(t.Title, k, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(t.Title.Replace(" ", ""), k.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
		SelectTab(hit != null ? hit.Id : "mods");
	}

	internal static void RefreshOpenTabPublic()
	{
		if (IsMenuOpen) RebuildCurrentTab();
	}

	internal static void ShowToastPublic(string message)
	{
		ShowToast(message);
	}

	internal static void OnSceneChanged(string sceneName)
	{
		if (_isOpen && _pausedByMenu) _pausedByMenu = false;
		if (IsMenuOpen) RebuildCurrentTab();
	}

	internal static void EnsureCreated()
	{
		if (_canvasObj != null) return;
		EnsureBuiltInTabs();
		ResolveFont();

		_canvasObj = new GameObject("RogueTowerModMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
		UnityEngine.Object.DontDestroyOnLoad(_canvasObj);

		Canvas canvas = _canvasObj.GetComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 500;
		canvas.pixelPerfect = true;

		CanvasScaler scaler = _canvasObj.GetComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1280f, 720f);
		scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		scaler.matchWidthOrHeight = 0.5f;

		CreateCornerButton();
		CreateMainWindow();
		SetOpen(false);
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

	private static void CreateCornerButton()
	{
		_cornerBtnObj = new GameObject("ModsCornerButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
		_cornerBtnObj.transform.SetParent(_canvasObj.transform, worldPositionStays: false);

		_cornerBtnRt = _cornerBtnObj.GetComponent<RectTransform>();
		_cornerBtnRt.anchorMin = new Vector2(1f, 1f);
		_cornerBtnRt.anchorMax = new Vector2(1f, 1f);
		_cornerBtnRt.pivot = new Vector2(1f, 1f);
		_cornerBtnRt.anchoredPosition = new Vector2(-18f, -68f);
		_cornerBtnRt.sizeDelta = new Vector2(68f, 26f);

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
		_windowRt.sizeDelta = new Vector2(780f, 520f);
		_windowRt.anchoredPosition = new Vector2(
			Mathf.Clamp(Plugin.WindowPosX.Value, -240f, 240f),
			Mathf.Clamp(Plugin.WindowPosY.Value, -95f, 95f));

		Image winBg = _windowObj.GetComponent<Image>();
		winBg.color = BgWindow;
		winBg.raycastTarget = true;

		Outline winBorder = _windowObj.AddComponent<Outline>();
		winBorder.effectColor = GoldAccent;
		winBorder.effectDistance = new Vector2(2f, -2f);

		// Header bar
		GameObject header = CreatePanel("Header", _windowRt, BgHeader);
		RectTransform hdrRt = header.GetComponent<RectTransform>();
		hdrRt.anchorMin = new Vector2(0f, 1f);
		hdrRt.anchorMax = new Vector2(1f, 1f);
		hdrRt.pivot = new Vector2(0.5f, 1f);
		hdrRt.anchoredPosition = Vector2.zero;
		hdrRt.sizeDelta = new Vector2(0f, 46f);

		WindowDragger dragger = header.AddComponent<WindowDragger>();
		dragger.Target = _windowRt;

		Text titleTxt = CreateText(hdrRt, "Rogue Tower Mod Menu", 16, FontStyle.Bold, GoldAccent, TextAnchor.MiddleLeft);
		titleTxt.rectTransform.anchorMin = new Vector2(0f, 0f);
		titleTxt.rectTransform.anchorMax = new Vector2(0f, 1f);
		titleTxt.rectTransform.pivot = new Vector2(0f, 0.5f);
		titleTxt.rectTransform.anchoredPosition = new Vector2(14f, 0f);
		titleTxt.rectTransform.sizeDelta = new Vector2(212f, 0f);

		_statusPillText = CreateText(hdrRt, "", 12, FontStyle.Normal, TextMuted, TextAnchor.MiddleRight);
		_statusPillText.rectTransform.anchorMin = new Vector2(0f, 0f);
		_statusPillText.rectTransform.anchorMax = new Vector2(1f, 1f);
		_statusPillText.rectTransform.offsetMin = new Vector2(226f, 0f);
		_statusPillText.rectTransform.offsetMax = new Vector2(-312f, 0f);

		Button rotLeftBtn = CreateSimpleButton(hdrRt, "<90", Vector2.zero, new Vector2(34f, 28f), BgButton, TextLight, 11, () =>
		{
			if (!Plugin.IsInGameRun) return;
			if (ModuleBridge.RotateCameraBy(-90f)) ShowToast("Rotated camera -90 deg");
			else ShowToast("Install RogueTowerQoL for camera rotation");
		});
		RectTransform rlRt = rotLeftBtn.GetComponent<RectTransform>();
		rlRt.anchorMin = new Vector2(1f, 0.5f);
		rlRt.anchorMax = new Vector2(1f, 0.5f);
		rlRt.pivot = new Vector2(1f, 0.5f);
		rlRt.anchoredPosition = new Vector2(-274f, 0f);

		Button rotRightBtn = CreateSimpleButton(hdrRt, "90>", Vector2.zero, new Vector2(34f, 28f), BgButton, TextLight, 11, () =>
		{
			if (!Plugin.IsInGameRun) return;
			if (ModuleBridge.RotateCameraBy(90f)) ShowToast("Rotated camera +90 deg");
			else ShowToast("Install RogueTowerQoL for camera rotation");
		});
		RectTransform rrRt = rotRightBtn.GetComponent<RectTransform>();
		rrRt.anchorMin = new Vector2(1f, 0.5f);
		rrRt.anchorMax = new Vector2(1f, 0.5f);
		rrRt.pivot = new Vector2(1f, 0.5f);
		rrRt.anchoredPosition = new Vector2(-236f, 0f);

		Button topBtn = CreateSimpleButton(hdrRt, "Top", Vector2.zero, new Vector2(38f, 28f), BgButton, GoldAccent, 11, () =>
		{
			if (!Plugin.IsInGameRun) return;
			if (ModuleBridge.ToggleTopDown()) ShowToast($"Top-Down View: {(ModuleBridge.IsTopDownActive() ? "ON" : "OFF")}");
			else ShowToast("Install RogueTowerQoL for Top-Down view");
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
			if (!Plugin.IsInGameRun) return;
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

		// Dynamic Tab Bar
		GameObject tabBar = CreatePanel("TabBar", _windowRt, new Color(0.10f, 0.115f, 0.155f, 1f));
		_tabBarRt = tabBar.GetComponent<RectTransform>();
		_tabBarRt.anchorMin = new Vector2(0f, 1f);
		_tabBarRt.anchorMax = new Vector2(1f, 1f);
		_tabBarRt.pivot = new Vector2(0.5f, 1f);
		_tabBarRt.anchoredPosition = new Vector2(0f, -46f);
		_tabBarRt.sizeDelta = new Vector2(0f, 38f);

		HorizontalLayoutGroup tabHlg = tabBar.AddComponent<HorizontalLayoutGroup>();
		tabHlg.padding = new RectOffset(10, 10, 4, 4);
		tabHlg.spacing = 4f;
		tabHlg.childControlWidth = true;
		tabHlg.childControlHeight = true;
		tabHlg.childForceExpandWidth = true;
		tabHlg.childForceExpandHeight = true;

		RebuildTabBarButtons();

		// ScrollRect body
		GameObject scrollObj = new GameObject("ScrollBody", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		scrollObj.transform.SetParent(_windowRt, worldPositionStays: false);
		RectTransform scrollRt = scrollObj.GetComponent<RectTransform>();
		scrollRt.anchorMin = Vector2.zero;
		scrollRt.anchorMax = Vector2.one;
		scrollRt.offsetMin = new Vector2(10f, 32f);
		scrollRt.offsetMax = new Vector2(-18f, -88f);

		Image viewportImg = scrollObj.GetComponent<Image>();
		viewportImg.color = new Color(0f, 0f, 0f, 0.01f);
		scrollObj.GetComponent<Mask>().showMaskGraphic = false;

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

		contentObj.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		GameObject sbObj = CreatePanel("Scrollbar", _windowRt, new Color(0.06f, 0.07f, 0.09f, 0.9f));
		RectTransform sbRt = sbObj.GetComponent<RectTransform>();
		sbRt.anchorMin = new Vector2(1f, 0f);
		sbRt.anchorMax = new Vector2(1f, 1f);
		sbRt.pivot = new Vector2(1f, 0.5f);
		sbRt.offsetMin = new Vector2(-14f, 32f);
		sbRt.offsetMax = new Vector2(-5f, -88f);

		GameObject handleObj = CreatePanel("Handle", sbRt, GoldAccent);
		StretchFill(handleObj.GetComponent<RectTransform>());

		Scrollbar sb = sbObj.AddComponent<Scrollbar>();
		sb.direction = Scrollbar.Direction.BottomToTop;
		sb.handleRect = handleObj.GetComponent<RectTransform>();
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

		GameObject footer = CreatePanel("Footer", _windowRt, BgHeader);
		RectTransform fRt = footer.GetComponent<RectTransform>();
		fRt.anchorMin = new Vector2(0f, 0f);
		fRt.anchorMax = new Vector2(1f, 0f);
		fRt.pivot = new Vector2(0.5f, 0f);
		fRt.anchoredPosition = Vector2.zero;
		fRt.sizeDelta = new Vector2(0f, 28f);

		_toastText = CreateText(fRt, $"Hotkey: [{FormatKey(Plugin.MenuHotkey.Value)}]  •  Drag header to move", 12, FontStyle.Normal, TextMuted, TextAnchor.MiddleLeft);
		_toastText.rectTransform.anchorMin = Vector2.zero;
		_toastText.rectTransform.anchorMax = Vector2.one;
		_toastText.rectTransform.offsetMin = new Vector2(12f, 0f);
		_toastText.rectTransform.offsetMax = new Vector2(-12f, 0f);
	}

	private static void RebuildTabBarButtons()
	{
		if (_tabBarRt == null) return;
		EnsureBuiltInTabs();

		for (int i = _tabBarRt.childCount - 1; i >= 0; i--)
		{
			Transform child = _tabBarRt.GetChild(i);
			child.gameObject.SetActive(false);
			child.SetParent(null, worldPositionStays: false);
			UnityEngine.Object.Destroy(child.gameObject);
		}
		TabButtons.Clear();
		TabImages.Clear();
		TabTexts.Clear();

		int fontSz = Tabs.Count > 5 ? 13 : 14;
		for (int i = 0; i < Tabs.Count; i++)
		{
			string capturedId = Tabs[i].Id;
			Button tb = CreateSimpleButton(_tabBarRt, Tabs[i].Title, Vector2.zero, Vector2.zero, BgButton, TextLight, fontSz, () => SelectTab(capturedId));
			TabButtons.Add(tb);
			TabImages.Add(tb.GetComponent<Image>());
			TabTexts.Add(tb.GetComponentInChildren<Text>());
		}
		UpdateTabButtonHighlight();
	}

	internal static void Toggle() => SetOpen(!_isOpen);

	internal static void SetOpen(bool open)
	{
		EnsureCreated();
		_isOpen = open;
		if (_windowObj != null) _windowObj.SetActive(open);

		if (open)
		{
			if (Plugin.PauseWhileMenuOpen.Value && Plugin.IsInGameRun && (PauseMenu.instance == null || !PauseMenu.instance.paused))
			{
				_prePauseSpeed = ModuleBridge.GetSpeed();
				if (_prePauseSpeed > 0.01f)
				{
					ModuleBridge.SetSpeed(0f);
					_pausedByMenu = true;
				}
			}
			SelectTab(_currentTabId);
		}
		else
		{
			UnfocusInputs();
			if (_pausedByMenu)
			{
				_pausedByMenu = false;
				if (Plugin.IsInGameRun && (PauseMenu.instance == null || !PauseMenu.instance.paused))
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
			if (_cornerBtnObj.activeSelf != showCorner) _cornerBtnObj.SetActive(showCorner);
			if (showCorner && _cornerBtnText != null)
			{
				string lbl = $"Mods ({FormatKey(Plugin.MenuHotkey.Value)})";
				if (_cornerBtnText.text != lbl) _cornerBtnText.text = lbl;
			}
		}

		if (!IsMenuOpen) return;

		if (_toastUntil > 0f && Time.unscaledTime > _toastUntil)
		{
			_toastUntil = 0f;
			if (_toastText != null)
			{
				_toastText.color = TextMuted;
				_toastText.text = $"Hotkey: [{FormatKey(Plugin.MenuHotkey.Value)}]  •  Drag header to move  •  Esc to close";
			}
		}

		if (string.Equals(_currentTabId, "profiler", StringComparison.OrdinalIgnoreCase) && Time.unscaledTime >= _nextProfilerRedrawTime)
		{
			_nextProfilerRedrawTime = Time.unscaledTime + 0.5f;
			RebuildCurrentTab(preserveScroll: true);
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
			string extra = "";
			for (int i = 0; i < HeaderBadgeProviders.Count; i++)
			{
				try
				{
					string b = HeaderBadgeProviders[i]?.Invoke();
					if (!string.IsNullOrWhiteSpace(b)) extra += $"  •  {b}";
				}
				catch
				{
				}
			}

			if (Plugin.IsInGameRun)
			{
				int wave = SpawnManager.instance != null ? SpawnManager.instance.level : 0;
				int gold = ResourceManager.instance != null ? GoldRef(ResourceManager.instance) : 0;
				float spd = ModuleBridge.GetSpeed();
				string spdStr = spd <= 0.01f ? "Paused" : $"{spd:0.#}x";
				_statusPillText.text = $"Wave {wave}  •  Gold: {gold:N0}g  •  Speed: {spdStr}{extra}";
			}
			else
			{
				int count = ModuleBridge.GetLoadedPluginsSorted().Count;
				_statusPillText.text = $"Main Menu  •  {count} Mods  •  {TestHarnessServer.CurrentFps:0} FPS{extra}";
			}
		}
		if (_pauseBtnText != null)
		{
			float spd = Plugin.IsInGameRun ? ModuleBridge.GetSpeed() : 1f;
			_pauseBtnText.text = spd <= 0.01f ? "Resume" : "Pause";
		}
	}

	private static void UpdateTabButtonHighlight()
	{
		for (int i = 0; i < Tabs.Count && i < TabButtons.Count; i++)
		{
			bool active = string.Equals(Tabs[i].Id, _currentTabId, StringComparison.OrdinalIgnoreCase);
			if (TabImages[i] != null) TabImages[i].color = active ? GoldAccent : BgButton;
			if (TabTexts[i] != null)
			{
				TabTexts[i].color = active ? new Color(0.08f, 0.09f, 0.12f, 1f) : TextLight;
				// Swap dedicated Semibold vs Regular font face cleanly without synthetic FontStyle.Bold blur
				TabTexts[i].font = active ? _fontBold : _fontRegular;
				TabTexts[i].fontStyle = FontStyle.Normal;
			}
		}
	}

	private static void SelectTab(string id)
	{
		EnsureBuiltInTabs();
		if (!Tabs.Exists(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)))
		{
			id = "mods";
		}
		_currentTabId = id;
		UpdateTabButtonHighlight();
		RebuildCurrentTab();
	}

	private static void RebuildCurrentTab(bool preserveScroll = false)
	{
		if (_contentRt == null) return;
		EnsureBuiltInTabs();
		float prevScroll = _scrollRect != null ? _scrollRect.verticalNormalizedPosition : 1f;
		LiveSyncCallbacks.Clear();

		for (int i = _contentRt.childCount - 1; i >= 0; i--)
		{
			Transform child = _contentRt.GetChild(i);
			child.gameObject.SetActive(false);
			child.SetParent(null, worldPositionStays: false);
			UnityEngine.Object.Destroy(child.gameObject);
		}

		TabRegistration tab = Tabs.Find(t => string.Equals(t.Id, _currentTabId, StringComparison.OrdinalIgnoreCase)) ?? Tabs[0];
		tab.BuildCallback?.Invoke(_contentRt);

		UpdateHeaderStatus();
		Canvas.ForceUpdateCanvases();
		LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRt);
		if (_scrollRect != null)
		{
			_scrollRect.verticalNormalizedPosition = preserveScroll ? prevScroll : 1f;
		}
	}

	// ======================== TAB 1: MODS MANAGER ========================
	private static void BuildModsTab()
	{
		List<PluginInfo> plugins = ModuleBridge.GetLoadedPluginsSorted();
		int suiteCount = 0;
		for (int i = 0; i < plugins.Count; i++)
		{
			if (ModuleBridge.IsSuitePlugin(plugins[i].Metadata.GUID)) suiteCount++;
		}

		AddNoticeBanner($"Loaded BepInEx Mods: {plugins.Count}   •   Clean-Room Suite: {suiteCount}   •   Press [{FormatKey(Plugin.MenuHotkey.Value)}] anytime");

		AddSectionHeader("Clean-Room Mod Suite & Companion Plugins");
		for (int i = 0; i < plugins.Count; i++)
		{
			PluginInfo pi = plugins[i];
			string guid = pi.Metadata.GUID ?? "";
			bool isSuite = ModuleBridge.IsSuitePlugin(guid);
			int cfgCount = (pi.Instance != null && pi.Instance.Config != null) ? pi.Instance.Config.Keys.Count : 0;

			GameObject card = CreatePanel($"Mod_{pi.Metadata.Name}", _contentRt, BgCard);
			card.AddComponent<LayoutElement>().preferredHeight = 46f;
			Outline ol = card.AddComponent<Outline>();
			ol.effectColor = isSuite ? new Color(0.55f, 0.45f, 0.28f, 0.85f) : BorderSubtle;
			ol.effectDistance = new Vector2(1f, -1f);

			HorizontalLayoutGroup hlg = card.AddComponent<HorizontalLayoutGroup>();
			hlg.padding = new RectOffset(10, 8, 5, 5);
			hlg.spacing = 8f;
			hlg.childControlWidth = true;
			hlg.childControlHeight = true;
			hlg.childForceExpandWidth = false;

			GameObject badge = CreatePanel("OriginBadge", card.transform, isSuite ? GoldSoft : BgButton);
			badge.AddComponent<LayoutElement>().preferredWidth = 84f;
			Text badgeTxt = CreateText(badge.transform, isSuite ? "Suite" : "Community", 11, FontStyle.Bold, isSuite ? GoldAccent : TextMuted, TextAnchor.MiddleCenter);
			StretchFill(badgeTxt.rectTransform);

			GameObject info = CreatePanel("Info", card.transform, Color.clear);
			info.AddComponent<LayoutElement>().flexibleWidth = 1f;

			Text titleTxt = CreateText(info.transform, $"{pi.Metadata.Name}  <color=#D9B36C>v{pi.Metadata.Version}</color>", 13, FontStyle.Bold, TextLight, TextAnchor.UpperLeft);
			titleTxt.supportRichText = true;
			titleTxt.rectTransform.anchorMin = new Vector2(0f, 0.5f);
			titleTxt.rectTransform.anchorMax = new Vector2(1f, 1f);
			titleTxt.rectTransform.offsetMin = Vector2.zero;
			titleTxt.rectTransform.offsetMax = Vector2.zero;

			Text guidTxt = CreateText(info.transform, $"GUID: {guid}   •   Config Keys: {cfgCount}", 11, FontStyle.Normal, TextMuted, TextAnchor.LowerLeft);
			guidTxt.rectTransform.anchorMin = new Vector2(0f, 0f);
			guidTxt.rectTransform.anchorMax = new Vector2(1f, 0.5f);
			guidTxt.rectTransform.offsetMin = Vector2.zero;
			guidTxt.rectTransform.offsetMax = Vector2.zero;

			if (cfgCount > 0)
			{
				string capturedGuid = guid;
				Button cfgBtn = CreateSimpleButton(card.transform, "Configure ->", Vector2.zero, Vector2.zero, BgButton, GoldAccent, 12, () =>
				{
					_settingsFocusGuid = capturedGuid;
					SelectTab("settings");
				});
				cfgBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 104f;
			}
			else
			{
				GameObject nonePill = CreatePanel("NoCfg", card.transform, BgHeader);
				nonePill.AddComponent<LayoutElement>().preferredWidth = 104f;
				Text nt = CreateText(nonePill.transform, "Loaded", 11, FontStyle.Normal, TextMuted, TextAnchor.MiddleCenter);
				StretchFill(nt.rectTransform);
			}
		}
	}

	// ======================== TAB 2: LIVE SETTINGS ========================
	private static void BuildSettingsTab()
	{
		if (!string.IsNullOrEmpty(_settingsFocusGuid))
		{
			AddActionRow("Filter Active", new (string, Action)[]
			{
				($"Showing Focused Mod ({_settingsFocusGuid}) — Click to Show All Settings", () =>
				{
					_settingsFocusGuid = "";
					RebuildCurrentTab();
				})
			});
		}

		bool ShowSection(string guid) => string.IsNullOrEmpty(_settingsFocusGuid) || string.Equals(_settingsFocusGuid, guid, StringComparison.OrdinalIgnoreCase);

		if (ShowSection(ModuleBridge.Keys.SpeedControls))
		{
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
				Transform spGrid = AddGrid(2, 356f, 34f, 8f);
				AddPluginConfigToggle(spGrid, "Remember Speed Between Runs", ModuleBridge.Keys.SpeedControls, "General", "RememberSpeedBetweenLoads");
				AddPluginConfigToggle(spGrid, "Allow Building While Paused", ModuleBridge.Keys.SpeedControls, "General", "AllowBuildingWhilePaused");
				AddPluginConfigToggle(spGrid, "Cap Speed to 1x on Expand", ModuleBridge.Keys.SpeedControls, "General", "ResetSpeedOnExpand");
			}
		}

		if (ModuleBridge.HasCleanQoL && ShowSection(ModuleBridge.Keys.CleanQoL))
		{
			AddSectionHeader("Quality of Life (RogueTowerQoL v2.1)");
			Transform cqGrid = AddGrid(2, 356f, 34f, 8f);
			AddPluginConfigToggle(cqGrid, "Hotbar Auto-Sort", ModuleBridge.Keys.CleanQoL, "5. Hotbar Sorting", "AutoSortHotbar", () => ModuleBridge.ForceRefreshQoLHotbar());
			AddPluginConfigToggle(cqGrid, "Hotbar Drag-and-Drop Sorting", ModuleBridge.Keys.CleanQoL, "5. Hotbar Sorting", "EnableHotbarDragAndDrop");
			AddPluginConfigToggle(cqGrid, "Show 'Edit Bar' Hotbar Button", ModuleBridge.Keys.CleanQoL, "5. Hotbar Sorting", "ShowHotbarSortButton");
			AddPluginConfigToggle(cqGrid, "Tower Hold & +10 Snap Panel", ModuleBridge.Keys.CleanQoL, "6. Tower Fast Upgrades", "EnableTowerFastUpgrade");
			AddCycleTile(cqGrid, "Hold Upgrade Speed", () =>
			{
				ModuleBridge.TryGetConfig(ModuleBridge.Keys.CleanQoL, "6. Tower Fast Upgrades", "HoldRepeatSpeed", out int sp);
				return sp switch { 0 => "Normal", 2 => "Turbo", _ => "Fast" };
			}, () =>
			{
				ModuleBridge.TryGetConfig(ModuleBridge.Keys.CleanQoL, "6. Tower Fast Upgrades", "HoldRepeatSpeed", out int cur);
				ModuleBridge.SetConfig(ModuleBridge.Keys.CleanQoL, "6. Tower Fast Upgrades", "HoldRepeatSpeed", (cur + 1) % 3);
			});
			AddCycleTile(cqGrid, "Chest Mode", () =>
			{
				return ModuleBridge.GetQoLChestModeInt() switch { 0 => "Off", 2 => "AutoOpen", _ => "PopupButton" };
			}, () =>
			{
				int next = (ModuleBridge.GetQoLChestModeInt() + 1) % 3;
				ModuleBridge.SetQoLChestModeInt(next);
				string lbl = next switch { 0 => "Off", 2 => "AutoOpen", _ => "PopupButton" };
				ShowToast($"Chest Mode: {lbl}");
			});
			AddPluginConfigToggle(cqGrid, "Chop Trees HUD Button", ModuleBridge.Keys.CleanQoL, "8. Chests, Trees & Early Expand", "ShowChopAllTreesButton");
			AddPluginConfigToggle(cqGrid, "Early Expand (Skip Wave Wait)", ModuleBridge.Keys.CleanQoL, "8. Chests, Trees & Early Expand", "AllowEarlyExpand");
			AddPluginConfigToggle(cqGrid, "Build-Mode Range Ring", ModuleBridge.Keys.CleanQoL, "1. Range Circles", "EnableGhostRange");
			AddPluginConfigToggle(cqGrid, "Hover Tower Range Ring", ModuleBridge.Keys.CleanQoL, "1. Range Circles", "EnableHoverRange");
			AddPluginConfigToggle(cqGrid, "Priority Tint Colors", ModuleBridge.Keys.CleanQoL, "2. Targeting Priorities", "EnablePriorityColors");
			AddPluginConfigToggle(cqGrid, "Levels to RPM (+7%/lvl)", ModuleBridge.Keys.CleanQoL, "3. Levels to RPM", "EnableLevelsToRpm", () => ModuleBridge.RefreshQoLTowers());
			AddPluginConfigToggle(cqGrid, "Camera Rotation ([ / ] / V)", ModuleBridge.Keys.CleanQoL, "7. Camera & Occluded Placement", "EnableCameraRotation", () =>
			{
				if (!ModuleBridge.TryGetConfig(ModuleBridge.Keys.CleanQoL, "7. Camera & Occluded Placement", "EnableCameraRotation", out bool en) || !en)
				{
					ModuleBridge.ResetCameraView();
				}
			});
			AddPluginConfigToggle(cqGrid, "Auto Top-Down in Build Mode", ModuleBridge.Keys.CleanQoL, "7. Camera & Occluded Placement", "AutoTopDownInBuildMode");
			AddPluginConfigToggle(cqGrid, "X-Ray Build Behind Cliffs/Towers", ModuleBridge.Keys.CleanQoL, "7. Camera & Occluded Placement", "EnableXRayPlacement");
			AddPluginConfigToggle(cqGrid, "Hold LeftAlt Cutaway & Deep Select", ModuleBridge.Keys.CleanQoL, "7. Camera & Occluded Placement", "EnableAltCutaway");
			AddActionRow("QoL Tools", new (string, Action)[]
			{
				("Open Hotbar Drag & Layout Bar", () =>
				{
					if (!Plugin.IsInGameRun) { ShowToast("Start a run to reorder the in-game hotbar."); return; }
					ModuleBridge.SetQoLHotbarSortOpen(true);
				})
			});
		}

		if (ModuleBridge.HasQuickTree && ShowSection(ModuleBridge.Keys.QuickTree))
		{
			AddSectionHeader("Quick Research Tree & Auto-Max (RogueTowerQuickTree v1.4)");
			Transform qtGrid = AddGrid(2, 356f, 34f, 8f);
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

		if (ModuleBridge.HasCustomMaps && ShowSection(ModuleBridge.Keys.CustomMaps))
		{
			AddSectionHeader("Custom Maps (RogueTowerCustomMaps v1.3)");
			Transform cmGrid = AddGrid(2, 356f, 34f, 8f);
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
			AddActionRow("Map Builder", new (string, Action)[]
			{
				("Open Custom Map Sequence Editor", () => { ModuleBridge.OpenCustomMapEditor(); })
			});
		}

		if (ModuleBridge.IsLoaded(ModuleBridge.Keys.LagFix) && ShowSection(ModuleBridge.Keys.LagFix))
		{
			AddSectionHeader("Late-Game Lag Fix (RogueTowerLagFix v1.3)");
			Transform lagGrid = AddGrid(2, 356f, 34f, 8f);
			AddPluginConfigToggle(lagGrid, "Fast-Forward Physics Scale", ModuleBridge.Keys.LagFix, "1. CPU & Targeting", "OptimizeFastForwardPhysics");
			AddPluginConfigToggle(lagGrid, "Targeting & Component Cache", ModuleBridge.Keys.LagFix, "1. CPU & Targeting", "OptimizeTargetingAndEnemyLookups");
			AddPluginConfigToggle(lagGrid, "HealthBar & HUD Throttling", ModuleBridge.Keys.LagFix, "2. UI & HealthBars", "OptimizeEnemyHealthBars");
			AddPluginConfigToggle(lagGrid, "Damage Number Coalescing", ModuleBridge.Keys.LagFix, "3. Damage Numbers", "ThrottleDamageNumbers");
			AddPluginConfigToggle(lagGrid, "Projectile & Explosion FX Cap", ModuleBridge.Keys.LagFix, "4. Visual FX", "OptimizeProjectileAndExplosionFX");
			AddPluginConfigToggle(lagGrid, "Tesla / Vampire Trail Fix", ModuleBridge.Keys.LagFix, "4. Visual FX", "FixTeslaAndVampireTrailCoroutines");
			AddPluginConfigToggle(lagGrid, "Combat Audio Deduplication", ModuleBridge.Keys.LagFix, "5. Audio", "DeduplicateCombatAudio");
		}

		if (ModuleBridge.HasThemes && ShowSection(ModuleBridge.Keys.Themes))
		{
			AddSectionHeader("Light / Dark Visual Theme (RogueTowerThemes v1.1)");
			Transform thGrid = AddGrid(2, 356f, 34f, 8f);
			AddCycleTile(thGrid, "Active Visual Theme", () => ModuleBridge.GetThemeModeName(), () =>
			{
				int next = (ModuleBridge.GetThemeMode() + 1) % 3;
				ModuleBridge.SetThemeMode(next);
				ShowToast($"Theme: {ModuleBridge.GetThemeModeName()}");
			});
			AddPluginConfigToggle(thGrid, "Preserve Vanilla Ambient (Fix Dark Shadows)", ModuleBridge.Keys.Themes, "1. General", "OverrideAmbient", () => ModuleBridge.ApplyTheme());
			float[] brightOpts = { 0.85f, 0.90f, 0.95f, 1.00f, 1.05f };
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

		// Legacy & Progression fallback toggles
		bool showMisc = ShowSection(ModuleBridge.Keys.QoL) || ShowSection(ModuleBridge.Keys.Banish) || ShowSection(ModuleBridge.Keys.DisableProg) || ShowSection(ModuleBridge.Keys.LevelsToRpm);
		if (showMisc && (ModuleBridge.IsLoaded(ModuleBridge.Keys.QoL) || ModuleBridge.HasBanishCards || ModuleBridge.IsLoaded(ModuleBridge.Keys.DisableProg) || ModuleBridge.HasLevelsToRpm))
		{
			AddSectionHeader("Companion & Progression Plugins");
			Transform miscGrid = AddGrid(2, 356f, 34f, 8f);
			if (ModuleBridge.IsLoaded(ModuleBridge.Keys.QoL))
			{
				AddPluginConfigToggle(miscGrid, "Legacy QoL Build Range Circle", ModuleBridge.Keys.QoL, "General", "EnableGhostRangeCircle");
				AddPluginConfigToggle(miscGrid, "Legacy QoL Hover Range Circle", ModuleBridge.Keys.QoL, "General", "EnableHoverRangeCircle");
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
				});
			}
			if (ModuleBridge.IsLoaded(ModuleBridge.Keys.DisableProg))
			{
				AddPluginConfigToggle(miscGrid, "Block Achievements (DisableTriumphs)", ModuleBridge.Keys.DisableProg, "General", "Enabled");
			}
			if (ModuleBridge.HasLevelsToRpm)
			{
				AddPluginConfigToggle(miscGrid, "Legacy Levels to RPM Enabled", ModuleBridge.Keys.LevelsToRpm, "General", "Enabled", () => ModuleBridge.RefreshLevelsToRpmTowers());
			}
		}

		// Auto-detected generic BepInEx configs for any other plugins
		BuildAutoDetectedPluginConfigs();

		if (ShowSection(ModuleBridge.Keys.ModMenu))
		{
			AddSectionHeader("Mod Menu Settings (RogueTowerModMenu v3.0)");
			Transform mmGrid = AddGrid(2, 356f, 34f, 8f);
			KeyCode[] hotkeys = { KeyCode.BackQuote, KeyCode.F1, KeyCode.F4, KeyCode.F8, KeyCode.F9, KeyCode.Backslash };
			AddCycleTile(mmGrid, "Mod Menu Hotkey", () => FormatKey(Plugin.MenuHotkey.Value), () =>
			{
				Plugin.MenuHotkey.Value = NextInCycle(hotkeys, Plugin.MenuHotkey.Value);
				ShowToast($"Mod Menu Hotkey: {FormatKey(Plugin.MenuHotkey.Value)}");
			});
			AddToggleTile(mmGrid, "Show Top-Right Mods Pill", () => Plugin.ShowCornerButton.Value, v => Plugin.ShowCornerButton.Value = v);
			AddToggleTile(mmGrid, "Pause Game While Menu Open", () => Plugin.PauseWhileMenuOpen.Value, v => Plugin.PauseWhileMenuOpen.Value = v);
			AddToggleTile(mmGrid, "Loopback Test Server (127.0.0.1)", () => Plugin.EnableTestServer.Value, v =>
			{
				Plugin.EnableTestServer.Value = v;
				ShowToast("Restart game to apply Test Server port bind change.");
			});
		}
	}

	private static void BuildAutoDetectedPluginConfigs()
	{
		List<PluginInfo> all = ModuleBridge.GetLoadedPluginsSorted();
		foreach (PluginInfo pi in all)
		{
			string guid = pi.Metadata.GUID ?? "";
			if (ModuleBridge.DedicatedSettingsGuids.Contains(guid)) continue;
			if (!string.IsNullOrEmpty(_settingsFocusGuid) && !string.Equals(_settingsFocusGuid, guid, StringComparison.OrdinalIgnoreCase)) continue;
			if (pi.Instance == null || pi.Instance.Config == null || pi.Instance.Config.Keys.Count == 0) continue;

			AddSectionHeader($"Other Mod Config: {pi.Metadata.Name} v{pi.Metadata.Version}");
			Transform grid = AddGrid(2, 356f, 34f, 8f);
			int shown = 0;
			foreach (ConfigDefinition def in pi.Instance.Config.Keys)
			{
				if (shown >= 14) break;
				ConfigEntryBase entry = pi.Instance.Config[def];
				if (entry == null) continue;
				if (entry.SettingType == typeof(bool))
				{
					AddConfigEntryBoolTile(grid, def.Key, entry, pi.Instance.Config);
					shown++;
				}
				else if (entry.SettingType.IsEnum)
				{
					AddConfigEntryEnumTile(grid, def.Key, entry, pi.Instance.Config);
					shown++;
				}
				else if (entry.SettingType == typeof(int) || entry.SettingType == typeof(float) || entry.SettingType == typeof(KeyCode))
				{
					AddConfigEntryInfoTile(grid, def.Key, entry, pi.Instance.Config);
					shown++;
				}
			}
		}
	}

	private static void AddConfigEntryBoolTile(Transform parent, string label, ConfigEntryBase entry, ConfigFile cfg)
	{
		AddToggleTile(parent, label, () => entry.BoxedValue is bool b && b, v =>
		{
			entry.BoxedValue = v;
			cfg.Save();
			ShowToast($"{label}: {(v ? "ON" : "OFF")}");
		});
	}

	private static void AddConfigEntryEnumTile(Transform parent, string label, ConfigEntryBase entry, ConfigFile cfg)
	{
		Array vals = Enum.GetValues(entry.SettingType);
		AddCycleTile(parent, label, () => entry.BoxedValue?.ToString() ?? "?", () =>
		{
			if (vals.Length == 0) return;
			int idx = Array.IndexOf(vals, entry.BoxedValue);
			object next = vals.GetValue((idx + 1) % vals.Length);
			entry.BoxedValue = next;
			cfg.Save();
			ShowToast($"{label}: {next}");
		});
	}

	private static void AddConfigEntryInfoTile(Transform parent, string label, ConfigEntryBase entry, ConfigFile cfg)
	{
		AddCycleTile(parent, label, () => entry.BoxedValue?.ToString() ?? "", () =>
		{
			entry.BoxedValue = entry.DefaultValue;
			cfg.Save();
			ShowToast($"Reset {label} to default ({entry.DefaultValue})");
		});
	}

	// ======================== TAB 3: PROFILER (NON-CHEAT) ========================
	private static void BuildProfilerTab()
	{
		DeepLagProfiler.LiveUiSummary s = DeepLagProfiler.GetUiSummary();

		AddSectionHeader("Live Frame & Entity Diagnostics (Works in Any Run)");
		string statusLine = s.IsSampling
			? $"Sampling live subsystems... {s.RemainingSeconds:0.0}s left  ({s.Frames} frames)"
			: $"FPS: {TestHarnessServer.CurrentFps:0.0}  (Avg {TestHarnessServer.AvgFps:0.0})  •  Frame: {TestHarnessServer.LastFrameMs:0.00}ms  •  Speed: {ModuleBridge.GetSpeed():0.#}x  •  Theme: {ModuleBridge.GetThemeModeName()}";
		AddNoticeBanner(statusLine);

		AddActionRow("Profiler Controls", new (string, Action)[]
		{
			(s.IsSampling ? "Sampling Active (10s)..." : "Start / Reset 10s Live Sample", () =>
			{
				DeepLagProfiler.StartInteractiveSample(10f);
				_nextProfilerRedrawTime = Time.unscaledTime + 0.5f;
				ShowToast("Started 10s DeepLagProfiler live sample...");
				RebuildCurrentTab(preserveScroll: true);
			}),
			("Clear Recorded Errors", () =>
			{
				ShowToast("Profiler UI refreshed");
				RebuildCurrentTab(preserveScroll: true);
			})
		});

		AddNoticeBanner(
			$"Scene Census: Towers={s.Towers}  •  Portals={s.ActivePortals}  •  Enemies={s.Enemies}  •  Projectiles={s.Projectiles}  •  Live Particles={s.LiveParticles}  •  GC=({s.Gc0}/{s.Gc1}/{s.Gc2})");

		if (s.Frames > 0)
		{
			AddSectionHeader("Sample Breakdown & Top Cost Subsystems");
			AddNoticeBanner(
				$"Sampled {s.Frames} frames  •  Mean FPS={s.MeanFps:0.0}  •  1% Low={s.P1LowFps:0.0}  •  Scripts={s.ScriptsMsPerFrame:0.00}ms/f  •  Render/PhysX/GC={s.EngineGcMsPerFrame:0.00}ms/f  •  Hitches >16ms={s.Over16Ms} / >50ms={s.Over50Ms}");

			int cap = Mathf.Min(8, s.TopSubsystems.Count);
			for (int i = 0; i < cap; i++)
			{
				DeepLagProfiler.SubsystemRow row = s.TopSubsystems[i];
				AddStatRow(row.Name, $"{row.MsPerFrame:0.00} ms/f ({row.PctOfFrame:0.0}%)  •  {row.CallsPerFrame:0.0} calls/f  •  peak {row.MaxCallMs:0.0}ms");
			}

			if (s.WorstSpikes.Count > 0)
			{
				AddSectionHeader("Worst Recorded Frame Spikes");
				for (int i = 0; i < s.WorstSpikes.Count; i++)
				{
					DeepLagProfiler.FrameSpike fs = s.WorstSpikes[i];
					AddStatRow($"Frame #{fs.Frame}: {fs.FrameMs:0.0}ms", $"{fs.TopCauses}{(fs.WaveAdvanced ? " [WaveAdvance]" : "")}");
				}
			}
		}
		else
		{
			AddNoticeBanner("Click 'Start / Reset 10s Live Sample' during combat to pinpoint exact CPU/render/GC bottlenecks on your current run.");
		}
	}

	private static void AddStatRow(string title, string detail)
	{
		GameObject row = CreatePanel("StatRow", _contentRt, BgCard);
		row.AddComponent<LayoutElement>().preferredHeight = 32f;
		Outline ol = row.AddComponent<Outline>();
		ol.effectColor = BorderSubtle;
		ol.effectDistance = new Vector2(1f, -1f);

		HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
		hlg.padding = new RectOffset(10, 10, 4, 4);
		hlg.spacing = 8f;
		hlg.childControlWidth = true;
		hlg.childControlHeight = true;
		hlg.childForceExpandWidth = false;

		Text t1 = CreateText(row.transform, title, 12, FontStyle.Bold, TextLight, TextAnchor.MiddleLeft);
		t1.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

		Text t2 = CreateText(row.transform, detail, 12, FontStyle.Normal, GoldAccent, TextAnchor.MiddleRight);
		t2.gameObject.AddComponent<LayoutElement>().preferredWidth = 360f;
	}

	// ======================== UI PRIMITIVES ========================
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

	private static string FormatKey(KeyCode k) => k == KeyCode.BackQuote ? "`" : k.ToString();

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
		obj.AddComponent<LayoutElement>().preferredHeight = 36f;
		Text txt = CreateText(obj.transform, message, 12, FontStyle.Normal, TextMuted, TextAnchor.MiddleCenter);
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
		lbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 140f;

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

		void Sync() => badgeTxt.text = valueGetter();

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
