using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RogueTowerQoL;

internal sealed class HotbarItemInfo
{
	public string Id;
	public string DisplayName;
	public string Nickname;
	public bool IsTower;
	public TowerType? TowerType;
	public GameObject UIObject;
}

internal static class HotbarSorterPatches
{
	[HarmonyPatch(typeof(TowerUnlockManager), "Start")]
	[HarmonyPostfix]
	private static void TowerUnlockManager_Start_Postfix(TowerUnlockManager __instance)
	{
		HotbarSorter.OnTowerUnlockManagerStart(__instance);
	}

	[HarmonyPatch(typeof(TowerUnlockManager), "DisplayButtons")]
	[HarmonyPrefix]
	private static void TowerUnlockManager_DisplayButtons_Prefix(TowerUnlockManager __instance)
	{
		if (Plugin.AutoSortHotbar.Value)
		{
			HotbarSorter.SortManagerLists(__instance);
		}
	}
}

internal static class HotbarSorter
{
	internal const string DefaultTowerOrderCsv =
		"Ballista,Mortar,FlameThrower,PoisonSprayer,FrostKeep,TeslaCoil,Shredder,Lookout,Encampment,Cannon,VampireLair,Radar,Obelisk,ParticleCannon,Monument";

	internal const string DefaultBuildingOrderCsv =
		"ManaSiphon,ManaBank,HauntedHouse,Mine,University";

	private static readonly AccessTools.FieldRef<TowerUnlockManager, List<GameObject>> UnlockedTowersRef =
		AccessTools.FieldRefAccess<TowerUnlockManager, List<GameObject>>("unlockedTowers");
	private static readonly AccessTools.FieldRef<TowerUnlockManager, List<GameObject>> UnlockedBuildingsRef =
		AccessTools.FieldRefAccess<TowerUnlockManager, List<GameObject>>("unlockedBuildings");
	private static readonly MethodInfo DisplayButtonsMethod =
		typeof(TowerUnlockManager).GetMethod("DisplayButtons", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly AccessTools.FieldRef<TowerUnlockCard, GameObject> CardUIObjectRef =
		AccessTools.FieldRefAccess<TowerUnlockCard, GameObject>("towerUIObject");
	private static readonly AccessTools.FieldRef<TowerUnlockCard, bool> CardIsTowerRef =
		AccessTools.FieldRefAccess<TowerUnlockCard, bool>("isTower");
	private static readonly AccessTools.FieldRef<UpgradeCard, string> UnlockNameRef =
		AccessTools.FieldRefAccess<UpgradeCard, string>("unlockName");

	private static readonly Dictionary<string, HotbarItemInfo> ItemsById = new Dictionary<string, HotbarItemInfo>(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<GameObject, HotbarItemInfo> ItemsByUI = new Dictionary<GameObject, HotbarItemInfo>();
	private static bool _initialized;

	internal static void OnSceneChanged()
	{
		ItemsById.Clear();
		ItemsByUI.Clear();
		_initialized = false;
	}

	internal static void OnTowerUnlockManagerStart(TowerUnlockManager mgr)
	{
		EnsureInitialized();
		if (Plugin.AutoSortHotbar.Value && mgr != null)
		{
			SortManagerLists(mgr);
			DisplayButtonsMethod?.Invoke(mgr, null);
		}
	}

	internal static void EnsureInitialized()
	{
		if (_initialized || TowerUnlockManager.instance == null)
		{
			return;
		}
		_initialized = true;
		ItemsById.Clear();
		ItemsByUI.Clear();

		List<GameObject> startTowers = UnlockedTowersRef(TowerUnlockManager.instance);
		if (startTowers != null)
		{
			foreach (GameObject go in startTowers)
			{
				if (go != null && !ItemsByUI.ContainsKey(go))
				{
					RegisterItem(new HotbarItemInfo
					{
						Id = "Ballista",
						DisplayName = "Ballista",
						Nickname = "",
						IsTower = true,
						TowerType = TowerType.Crossbow,
						UIObject = go
					});
				}
			}
		}

		TowerUnlockCard[] cards = Resources.FindObjectsOfTypeAll<TowerUnlockCard>();
		foreach (TowerUnlockCard card in cards)
		{
			if (card == null || !card.gameObject.scene.IsValid())
			{
				continue;
			}
			GameObject uiObj = CardUIObjectRef(card);
			if (uiObj == null || ItemsByUI.ContainsKey(uiObj))
			{
				continue;
			}

			bool isTower = CardIsTowerRef(card);
			BuildButtonUI bbu = uiObj.GetComponentInChildren<BuildButtonUI>(includeInactive: true);
			TowerType? tt = null;
			if (bbu != null)
			{
				tt = AccessTools.FieldRefAccess<BuildButtonUI, TowerType>("myTowerType")(bbu);
			}

			string id = ResolveCanonicalId(card, uiObj, tt);
			(string disp, string nick) = GetFriendlyNames(id, card.title);

			RegisterItem(new HotbarItemInfo
			{
				Id = id,
				DisplayName = disp,
				Nickname = nick,
				IsTower = isTower,
				TowerType = tt,
				UIObject = uiObj
			});
		}
	}

	private static void RegisterItem(HotbarItemInfo info)
	{
		if (info == null || string.IsNullOrEmpty(info.Id) || info.UIObject == null)
		{
			return;
		}
		ItemsById[info.Id] = info;
		ItemsByUI[info.UIObject] = info;
	}

	private static string ResolveCanonicalId(TowerUnlockCard card, GameObject uiObj, TowerType? tt)
	{
		if (tt.HasValue)
		{
			switch (tt.Value)
			{
				case TowerType.Crossbow: return "Ballista";
				case TowerType.Morter: return "Mortar";
				case TowerType.FlameThrower: return "FlameThrower";
				case TowerType.PoisonSprayer: return "PoisonSprayer";
				case TowerType.Frost: return "FrostKeep";
				case TowerType.TeslaCoil: return "TeslaCoil";
				case TowerType.Shredder: return "Shredder";
				case TowerType.Lookout: return "Lookout";
				case TowerType.Encampment: return "Encampment";
				case TowerType.Cannon: return "Cannon";
				case TowerType.VampireLair: return "VampireLair";
				case TowerType.Radar: return "Radar";
				case TowerType.Obelisk: return "Obelisk";
				case TowerType.ParticleCannon: return "ParticleCannon";
				case TowerType.Monument: return "Monument";
				case TowerType.Siphon: return "ManaSiphon";
			}
		}

		string un = (UnlockNameRef(card) ?? string.Empty).Replace(" ", "");
		string title = (card.title ?? string.Empty)
			.Replace("Tower - ", "")
			.Replace("Building - ", "")
			.Replace(" ", "");
		string objName = uiObj != null ? uiObj.name.Replace(" ", "") : string.Empty;

		string combined = (un + "|" + title + "|" + objName).ToLowerInvariant();
		if (combined.Contains("siphon")) return "ManaSiphon";
		if (combined.Contains("bank")) return "ManaBank";
		if (combined.Contains("haunted") || combined.Contains("house")) return "HauntedHouse";
		if (combined.Contains("mine")) return "Mine";
		if (combined.Contains("university")) return "University";
		if (combined.Contains("mortar") || combined.Contains("morter")) return "Mortar";
		if (combined.Contains("ballista") || combined.Contains("balista")) return "Ballista";

		return !string.IsNullOrEmpty(title) ? title : (!string.IsNullOrEmpty(un) ? un : objName);
	}

	private static (string display, string nickname) GetFriendlyNames(string id, string rawTitle)
	{
		return id switch
		{
			"Ballista" => ("Ballista", ""),
			"Mortar" => ("Mortar", ""),
			"FlameThrower" => ("Flame Thrower", ""),
			"PoisonSprayer" => ("Poison Sprayer", ""),
			"FrostKeep" => ("Frost Keep", "Freeze"),
			"TeslaCoil" => ("Tesla Coil", ""),
			"Shredder" => ("Shredder", "Saw Blade"),
			"Lookout" => ("Lookout", "Spotter"),
			"Encampment" => ("Encampment", "Mines"),
			"Cannon" => ("Cannon", ""),
			"VampireLair" => ("Vampire Lair", ""),
			"Radar" => ("Radar", "Biplane"),
			"Obelisk" => ("Obelisk", ""),
			"ParticleCannon" => ("Particle Cannon", ""),
			"Monument" => ("Monument", ""),
			"ManaSiphon" => ("Mana Siphon", ""),
			"ManaBank" => ("Mana Bank", ""),
			"HauntedHouse" => ("Haunted House", "Grave / House"),
			"Mine" => ("Mine", "Gold Mine"),
			"University" => ("University", ""),
			_ => (string.IsNullOrEmpty(rawTitle) ? id : rawTitle.Replace("Tower - ", "").Replace("Building - ", ""), "")
		};
	}

	internal static List<string> ParseOrderCsv(string csv, string fallbackCsv)
	{
		List<string> list = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		void AddFrom(string src)
		{
			if (string.IsNullOrEmpty(src)) return;
			string[] parts = src.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string raw in parts)
			{
				string id = raw.Trim();
				if (!string.IsNullOrEmpty(id) && seen.Add(id))
				{
					list.Add(id);
				}
			}
		}

		AddFrom(csv);
		AddFrom(fallbackCsv);
		return list;
	}

	internal static List<HotbarItemInfo> GetOrderedItems(bool isTower)
	{
		EnsureInitialized();
		List<string> order = isTower
			? ParseOrderCsv(Plugin.TowerHotbarOrder.Value, DefaultTowerOrderCsv)
			: ParseOrderCsv(Plugin.BuildingHotbarOrder.Value, DefaultBuildingOrderCsv);

		List<HotbarItemInfo> result = new List<HotbarItemInfo>();
		HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (string id in order)
		{
			if (ItemsById.TryGetValue(id, out HotbarItemInfo info) && info.IsTower == isTower && added.Add(info.Id))
			{
				result.Add(info);
			}
		}
		foreach (HotbarItemInfo info in ItemsById.Values)
		{
			if (info.IsTower == isTower && added.Add(info.Id))
			{
				result.Add(info);
			}
		}
		return result;
	}

	internal static bool IsItemUnlocked(HotbarItemInfo info)
	{
		if (info == null || info.UIObject == null || TowerUnlockManager.instance == null)
		{
			return false;
		}
		List<GameObject> list = info.IsTower
			? UnlockedTowersRef(TowerUnlockManager.instance)
			: UnlockedBuildingsRef(TowerUnlockManager.instance);
		return list != null && list.Contains(info.UIObject) && info.UIObject.activeSelf;
	}

	internal static void MoveItemInOrder(string id, int delta)
	{
		EnsureInitialized();
		if (!ItemsById.TryGetValue(id, out HotbarItemInfo info))
		{
			return;
		}

		List<HotbarItemInfo> ordered = GetOrderedItems(info.IsTower);
		int idx = ordered.FindIndex(x => string.Equals(x.Id, info.Id, StringComparison.OrdinalIgnoreCase));
		if (idx < 0)
		{
			return;
		}
		int target = Mathf.Clamp(idx + delta, 0, ordered.Count - 1);
		if (target == idx)
		{
			return;
		}

		HotbarItemInfo item = ordered[idx];
		ordered.RemoveAt(idx);
		ordered.Insert(target, item);

		List<string> ids = new List<string>(ordered.Count);
		for (int i = 0; i < ordered.Count; i++)
		{
			ids.Add(ordered[i].Id);
		}
		string csv = string.Join(",", ids);
		if (info.IsTower)
		{
			Plugin.TowerHotbarOrder.Value = csv;
		}
		else
		{
			Plugin.BuildingHotbarOrder.Value = csv;
		}

		ForceRefreshHotbar();
		HotbarSortOverlayUI.RefreshIfOpen();
	}

	internal static void ResetDefaultOrder()
	{
		Plugin.TowerHotbarOrder.Value = DefaultTowerOrderCsv;
		Plugin.BuildingHotbarOrder.Value = DefaultBuildingOrderCsv;
		ForceRefreshHotbar();
		HotbarSortOverlayUI.RefreshIfOpen();
	}

	internal static void SortManagerLists(TowerUnlockManager mgr)
	{
		if (mgr == null)
		{
			return;
		}
		EnsureInitialized();

		Dictionary<string, int> towerRank = BuildRankMap(ParseOrderCsv(Plugin.TowerHotbarOrder.Value, DefaultTowerOrderCsv));
		Dictionary<string, int> bldRank = BuildRankMap(ParseOrderCsv(Plugin.BuildingHotbarOrder.Value, DefaultBuildingOrderCsv));

		List<GameObject> towers = UnlockedTowersRef(mgr);
		if (towers != null && towers.Count > 1)
		{
			towers.Sort((a, b) => GetRank(a, towerRank).CompareTo(GetRank(b, towerRank)));
		}

		List<GameObject> buildings = UnlockedBuildingsRef(mgr);
		if (buildings != null && buildings.Count > 1)
		{
			buildings.Sort((a, b) => GetRank(a, bldRank).CompareTo(GetRank(b, bldRank)));
		}
	}

	private static Dictionary<string, int> BuildRankMap(List<string> list)
	{
		Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < list.Count; i++)
		{
			if (!map.ContainsKey(list[i]))
			{
				map[list[i]] = i;
			}
		}
		return map;
	}

	private static int GetRank(GameObject go, Dictionary<string, int> rankMap)
	{
		if (go != null && ItemsByUI.TryGetValue(go, out HotbarItemInfo info) && rankMap.TryGetValue(info.Id, out int rank))
		{
			return rank;
		}
		return int.MaxValue;
	}

	internal static void ForceRefreshHotbar()
	{
		if (TowerUnlockManager.instance == null)
		{
			return;
		}
		if (Plugin.AutoSortHotbar.Value)
		{
			SortManagerLists(TowerUnlockManager.instance);
		}
		DisplayButtonsMethod?.Invoke(TowerUnlockManager.instance, null);
	}
}

internal static class HotbarSortOverlayUI
{
	private static readonly Color BgWindow = new Color(0.086f, 0.098f, 0.133f, 0.96f);
	private static readonly Color BgHeader = new Color(0.118f, 0.133f, 0.180f, 1.0f);
	private static readonly Color BgCard = new Color(0.135f, 0.155f, 0.210f, 0.96f);
	private static readonly Color BgButton = new Color(0.180f, 0.205f, 0.275f, 1.0f);
	private static readonly Color GoldAccent = new Color(0.851f, 0.702f, 0.424f, 1.0f);
	private static readonly Color GreenOn = new Color(0.180f, 0.620f, 0.388f, 1.0f);
	private static readonly Color RedDanger = new Color(0.680f, 0.240f, 0.240f, 1.0f);
	private static readonly Color TextLight = new Color(0.945f, 0.945f, 0.960f, 1.0f);
	private static readonly Color TextMuted = new Color(0.675f, 0.705f, 0.775f, 1.0f);
	private static readonly Color BorderSubtle = new Color(0.28f, 0.31f, 0.40f, 0.85f);

	private static Font _uiFont;
	private static GameObject _canvasObj;
	private static GameObject _pillObj;
	private static RectTransform _pillRt;
	private static GameObject _popupObj;
	private static RectTransform _popupRt;
	private static RectTransform _contentRt;
	private static ScrollRect _scrollRect;
	private static Text _autoSortBtnText;
	private static Image _autoSortBtnImage;
	private static bool _isOpen;

	internal static bool IsOpen => _isOpen && _popupObj != null && _popupObj.activeInHierarchy;

	internal static bool IsPointerOverOverlay
	{
		get
		{
			Vector2 mouse = Input.mousePosition;
			if (IsOpen && _popupRt != null && RectTransformUtility.RectangleContainsScreenPoint(_popupRt, mouse, null))
			{
				return true;
			}
			if (_pillObj != null && _pillObj.activeInHierarchy && _pillRt != null &&
				RectTransformUtility.RectangleContainsScreenPoint(_pillRt, mouse, null))
			{
				return true;
			}
			return false;
		}
	}

	internal static void OnSceneChanged()
	{
		SetOpen(false);
	}

	internal static void SetOpen(bool open)
	{
		EnsureCreated();
		_isOpen = open && GameManager.instance != null;
		if (_popupObj != null)
		{
			_popupObj.SetActive(_isOpen);
		}
		if (_isOpen)
		{
			RebuildList();
		}
	}

	internal static void RefreshIfOpen()
	{
		if (IsOpen)
		{
			RebuildList();
		}
	}

	internal static void Tick()
	{
		bool inRun = GameManager.instance != null && !GameManager.instance.gameOver && !Plugin.IsPauseUiHidden();
		if (!inRun)
		{
			if (_pillObj != null && _pillObj.activeSelf)
			{
				_pillObj.SetActive(false);
			}
			if (IsOpen)
			{
				SetOpen(false);
			}
			return;
		}

		EnsureCreated();

		bool showPill = Plugin.ShowHotbarSortButton.Value && TowerUnlockManager.instance != null;
		if (_pillObj != null && _pillObj.activeSelf != showPill)
		{
			_pillObj.SetActive(showPill);
		}
	}

	private static void EnsureCreated()
	{
		if (_canvasObj != null)
		{
			return;
		}
		ResolveFont();

		_canvasObj = new GameObject("RogueTowerQoL_HotbarSortCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
		UnityEngine.Object.DontDestroyOnLoad(_canvasObj);

		Canvas canvas = _canvasObj.GetComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 420;

		CanvasScaler scaler = _canvasObj.GetComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		scaler.matchWidthOrHeight = 0.5f;

		CreatePillButton();
		CreatePopupWindow();
		_popupObj.SetActive(false);
		_pillObj.SetActive(false);
	}

	private static void CreatePillButton()
	{
		_pillObj = CreatePanel("HotbarSortPill", _canvasObj.transform, BgHeader);
		_pillRt = _pillObj.GetComponent<RectTransform>();
		_pillRt.anchorMin = new Vector2(0.5f, 0f);
		_pillRt.anchorMax = new Vector2(0.5f, 0f);
		_pillRt.pivot = new Vector2(0.5f, 0f);
		_pillRt.anchoredPosition = new Vector2(-530f, 18f);
		_pillRt.sizeDelta = new Vector2(74f, 28f);

		Outline ol = _pillObj.AddComponent<Outline>();
		ol.effectColor = GoldAccent;
		ol.effectDistance = new Vector2(1f, -1f);

		Button btn = _pillObj.AddComponent<Button>();
		btn.navigation = new Navigation { mode = Navigation.Mode.None };
		btn.onClick.AddListener(() => SetOpen(!_isOpen));

		Text txt = CreateText(_pillObj.transform, "Sort Bar", 12, FontStyle.Bold, GoldAccent, TextAnchor.MiddleCenter);
		StretchFill(txt.rectTransform);
	}

	private static void CreatePopupWindow()
	{
		_popupObj = CreatePanel("HotbarSortWindow", _canvasObj.transform, BgWindow);
		_popupRt = _popupObj.GetComponent<RectTransform>();
		_popupRt.anchorMin = new Vector2(0.5f, 0.5f);
		_popupRt.anchorMax = new Vector2(0.5f, 0.5f);
		_popupRt.pivot = new Vector2(0.5f, 0.5f);
		_popupRt.anchoredPosition = new Vector2(0f, 20f);
		_popupRt.sizeDelta = new Vector2(580f, 510f);

		Outline border = _popupObj.AddComponent<Outline>();
		border.effectColor = GoldAccent;
		border.effectDistance = new Vector2(2f, -2f);

		GameObject header = CreatePanel("Header", _popupRt, BgHeader);
		RectTransform hdrRt = header.GetComponent<RectTransform>();
		hdrRt.anchorMin = new Vector2(0f, 1f);
		hdrRt.anchorMax = new Vector2(1f, 1f);
		hdrRt.pivot = new Vector2(0.5f, 1f);
		hdrRt.anchoredPosition = Vector2.zero;
		hdrRt.sizeDelta = new Vector2(0f, 42f);

		Text title = CreateText(hdrRt, "Hotbar Slot Order (QoL)", 16, FontStyle.Bold, GoldAccent, TextAnchor.MiddleLeft);
		title.rectTransform.anchorMin = Vector2.zero;
		title.rectTransform.anchorMax = Vector2.one;
		title.rectTransform.offsetMin = new Vector2(14f, 0f);
		title.rectTransform.offsetMax = new Vector2(-240f, 0f);

		Button autoBtn = CreateSimpleButton(hdrRt, "Auto: ON", BgButton, TextLight, 11, () =>
		{
			Plugin.AutoSortHotbar.Value = !Plugin.AutoSortHotbar.Value;
			if (Plugin.AutoSortHotbar.Value)
			{
				HotbarSorter.ForceRefreshHotbar();
			}
			RebuildList();
		});
		RectTransform autoRt = autoBtn.GetComponent<RectTransform>();
		autoRt.anchorMin = new Vector2(1f, 0.5f);
		autoRt.anchorMax = new Vector2(1f, 0.5f);
		autoRt.pivot = new Vector2(1f, 0.5f);
		autoRt.anchoredPosition = new Vector2(-132f, 0f);
		autoRt.sizeDelta = new Vector2(92f, 26f);
		_autoSortBtnImage = autoBtn.GetComponent<Image>();
		_autoSortBtnText = autoBtn.GetComponentInChildren<Text>();

		Button resetBtn = CreateSimpleButton(hdrRt, "Reset", BgButton, GoldAccent, 11, () =>
		{
			HotbarSorter.ResetDefaultOrder();
		});
		RectTransform resetRt = resetBtn.GetComponent<RectTransform>();
		resetRt.anchorMin = new Vector2(1f, 0.5f);
		resetRt.anchorMax = new Vector2(1f, 0.5f);
		resetRt.pivot = new Vector2(1f, 0.5f);
		resetRt.anchoredPosition = new Vector2(-46f, 0f);
		resetRt.sizeDelta = new Vector2(78f, 26f);

		Button closeBtn = CreateSimpleButton(hdrRt, "X", RedDanger, TextLight, 13, () => SetOpen(false));
		RectTransform cRt = closeBtn.GetComponent<RectTransform>();
		cRt.anchorMin = new Vector2(1f, 0.5f);
		cRt.anchorMax = new Vector2(1f, 0.5f);
		cRt.pivot = new Vector2(1f, 0.5f);
		cRt.anchoredPosition = new Vector2(-8f, 0f);
		cRt.sizeDelta = new Vector2(30f, 26f);

		// Scroll body
		GameObject scrollObj = new GameObject("ScrollBody", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		scrollObj.transform.SetParent(_popupRt, worldPositionStays: false);
		RectTransform scrollRt = scrollObj.GetComponent<RectTransform>();
		scrollRt.anchorMin = Vector2.zero;
		scrollRt.anchorMax = Vector2.one;
		scrollRt.offsetMin = new Vector2(10f, 10f);
		scrollRt.offsetMax = new Vector2(-16f, -48f);

		scrollObj.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
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
		vlg.padding = new RectOffset(4, 6, 4, 10);
		vlg.spacing = 5f;
		vlg.childControlWidth = true;
		vlg.childControlHeight = true;
		vlg.childForceExpandWidth = true;
		vlg.childForceExpandHeight = false;

		contentObj.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		GameObject sbObj = CreatePanel("Scrollbar", _popupRt, new Color(0.06f, 0.07f, 0.09f, 0.9f));
		RectTransform sbRt = sbObj.GetComponent<RectTransform>();
		sbRt.anchorMin = new Vector2(1f, 0f);
		sbRt.anchorMax = new Vector2(1f, 1f);
		sbRt.pivot = new Vector2(1f, 0.5f);
		sbRt.offsetMin = new Vector2(-12f, 10f);
		sbRt.offsetMax = new Vector2(-4f, -48f);

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
		_scrollRect.scrollSensitivity = 36f;
		_scrollRect.verticalScrollbar = sb;
		_scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
	}

	private static void RebuildList()
	{
		if (_contentRt == null)
		{
			return;
		}
		if (_autoSortBtnText != null && _autoSortBtnImage != null)
		{
			bool on = Plugin.AutoSortHotbar.Value;
			_autoSortBtnText.text = on ? "Auto: ON" : "Auto: OFF";
			_autoSortBtnImage.color = on ? GreenOn : BgButton;
		}

		for (int i = _contentRt.childCount - 1; i >= 0; i--)
		{
			Transform child = _contentRt.GetChild(i);
			child.gameObject.SetActive(false);
			child.SetParent(null, worldPositionStays: false);
			UnityEngine.Object.Destroy(child.gameObject);
		}

		AddSectionHeader("Row 1 — Towers (Left -> Right)");
		List<HotbarItemInfo> towers = HotbarSorter.GetOrderedItems(isTower: true);
		for (int i = 0; i < towers.Count; i++)
		{
			AddRow(towers[i], i + 1, towers.Count);
		}

		AddSectionHeader("Row 2 — Buildings (Left -> Right)");
		List<HotbarItemInfo> blds = HotbarSorter.GetOrderedItems(isTower: false);
		for (int i = 0; i < blds.Count; i++)
		{
			AddRow(blds[i], i + 1, blds.Count);
		}

		Canvas.ForceUpdateCanvases();
		LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRt);
	}

	private static void AddSectionHeader(string title)
	{
		GameObject obj = CreatePanel("Header_" + title, _contentRt, Color.clear);
		obj.AddComponent<LayoutElement>().preferredHeight = 24f;
		Text txt = CreateText(obj.transform, title.ToUpperInvariant(), 11, FontStyle.Bold, GoldAccent, TextAnchor.LowerLeft);
		StretchFill(txt.rectTransform, 4f, 2f, 4f, 2f);
	}

	private static void AddRow(HotbarItemInfo info, int pos, int total)
	{
		bool owned = HotbarSorter.IsItemUnlocked(info);
		GameObject row = CreatePanel("Slot_" + info.Id, _contentRt, BgCard);
		row.AddComponent<LayoutElement>().preferredHeight = 32f;
		Outline ol = row.AddComponent<Outline>();
		ol.effectColor = BorderSubtle;
		ol.effectDistance = new Vector2(1f, -1f);

		HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
		hlg.padding = new RectOffset(10, 8, 3, 3);
		hlg.spacing = 6f;
		hlg.childControlWidth = true;
		hlg.childControlHeight = true;
		hlg.childForceExpandWidth = false;

		string nick = string.IsNullOrEmpty(info.Nickname) ? "" : $" <color=#9FA8BC>({info.Nickname})</color>";
		string tag = owned ? " <color=#47B875>[Unlocked]</color>" : "";
		Text lbl = CreateText(row.transform, $"<color=#D9B36C>#{pos}</color>  <b>{info.DisplayName}</b>{nick}{tag}", 13, FontStyle.Normal, TextLight, TextAnchor.MiddleLeft);
		lbl.supportRichText = true;
		lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

		Button upBtn = CreateSimpleButton(row.transform, "< Left", BgButton, TextLight, 12, () => HotbarSorter.MoveItemInOrder(info.Id, -1));
		upBtn.interactable = pos > 1;
		upBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;

		Button dnBtn = CreateSimpleButton(row.transform, "Right >", BgButton, TextLight, 12, () => HotbarSorter.MoveItemInOrder(info.Id, +1));
		dnBtn.interactable = pos < total;
		dnBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 64f;
	}

	private static void ResolveFont()
	{
		if (_uiFont != null) return;
		Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
		foreach (Font f in fonts)
		{
			if (f != null && !string.IsNullOrEmpty(f.name) && f.name != "Arial")
			{
				_uiFont = f;
				break;
			}
		}
		if (_uiFont == null && fonts.Length > 0) _uiFont = fonts[0];
		if (_uiFont == null) _uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
