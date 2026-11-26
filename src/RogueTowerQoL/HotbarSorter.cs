using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
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

	[HarmonyPatch(typeof(TowerUnlockManager), "DisplayButtons")]
	[HarmonyPostfix]
	private static void TowerUnlockManager_DisplayButtons_Postfix(TowerUnlockManager __instance)
	{
		HotbarSorter.ApplyPreviewOrRestoreLayout(__instance);
	}

	[HarmonyPatch(typeof(BuildButtonUI), nameof(BuildButtonUI.Build))]
	[HarmonyPrefix]
	private static bool BuildButtonUI_Build_Prefix()
	{
		if (HotbarEditBarUI.IsOpen || HotbarSorter.IsDraggingSlot || Time.frameCount <= HotbarSorter.SuppressBuildUntilFrame)
		{
			return false;
		}
		return true;
	}
}

internal sealed class HotbarSlotDragHandler : MonoBehaviour,
	IPointerDownHandler,
	IBeginDragHandler,
	IDragHandler,
	IEndDragHandler,
	IPointerEnterHandler,
	IPointerExitHandler
{
	private static readonly MethodInfo ExitBuildModeMethod =
		typeof(BuildingManager).GetMethod("ExitBuildMode", BindingFlags.Instance | BindingFlags.NonPublic);

	internal HotbarItemInfo ItemInfo;

	private bool _dragging;
	private int _targetIndex = -1;
	private List<HotbarItemInfo> _visibleRowItems;
	private float _rowY;
	private float _step = 100f;
	private float _baseScale = 1f;
	private RectTransform _parentRt;

	public void OnPointerEnter(PointerEventData eventData)
	{
		if (ItemInfo == null) return;
		if (!HotbarSorter.IsDraggingSlot && HotbarEditBarUI.IsOpen)
		{
			int idx = HotbarSorter.GetVisibleIndex(ItemInfo, out int total);
			HotbarEditBarUI.SetActiveItem(ItemInfo, idx + 1, total, isDragging: false);
		}
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		if (!_dragging && HotbarEditBarUI.IsOpen && HotbarEditBarUI.HoveredOrSelected == ItemInfo)
		{
			HotbarEditBarUI.RefreshStatusFromSelection();
		}
	}

	public void OnPointerDown(PointerEventData eventData)
	{
		if (ItemInfo == null || eventData == null) return;
		if (HotbarEditBarUI.IsOpen)
		{
			int idx = HotbarSorter.GetVisibleIndex(ItemInfo, out int total);
			HotbarEditBarUI.SelectItem(ItemInfo, idx + 1, total);
		}
	}

	public void OnBeginDrag(PointerEventData eventData)
	{
		if (ItemInfo == null || eventData == null) return;
		if (eventData.button != PointerEventData.InputButton.Left && eventData.button != PointerEventData.InputButton.Right)
		{
			return;
		}
		if (!HotbarSorter.CanDragSlot())
		{
			return;
		}

		_parentRt = transform.parent as RectTransform;
		if (_parentRt == null) return;

		_visibleRowItems = HotbarSorter.GetCurrentlyDisplayedRowItems(ItemInfo.IsTower);
		if (_visibleRowItems == null || _visibleRowItems.Count <= 1)
		{
			return;
		}

		_dragging = true;
		HotbarSorter.IsDraggingSlot = true;
		HotbarSorter.SuppressBuildUntilFrame = Time.frameCount + 2;

		// Cancel build mode if clicking a slot started it cleanly
		if (BuildingManager.instance != null && BuildingManager.instance.buildMode)
		{
			ExitBuildModeMethod?.Invoke(BuildingManager.instance, null);
		}

		_rowY = HotbarSorter.GetRowY(ItemInfo.IsTower);
		_step = HotbarSorter.GetRowStep(ItemInfo.IsTower, _visibleRowItems.Count);
		_baseScale = HotbarSorter.GetSlotBaseScale(ItemInfo.IsTower, _visibleRowItems.Count);
		_targetIndex = _visibleRowItems.IndexOf(ItemInfo);
		if (_targetIndex < 0) _targetIndex = 0;

		float dragScale = _baseScale * 1.14f;
		transform.localScale = new Vector3(dragScale, dragScale, 1f);
		transform.SetAsLastSibling();
		HotbarEditBarUI.SelectItem(ItemInfo, _targetIndex + 1, _visibleRowItems.Count, isDragging: true);
	}

	public void OnDrag(PointerEventData eventData)
	{
		if (!_dragging || _parentRt == null || _visibleRowItems == null || _visibleRowItems.Count == 0)
		{
			return;
		}

		HotbarSorter.SuppressBuildUntilFrame = Time.frameCount + 2;
		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRt, eventData.position, eventData.pressEventCamera, out Vector2 localPt))
		{
			int count = _visibleRowItems.Count;
			float rawSlot = (localPt.x + count * _step * 0.5f - _step * 0.5f) / _step;
			_targetIndex = Mathf.Clamp(Mathf.RoundToInt(rawSlot), 0, count - 1);

			float minX = -count * _step * 0.5f - _step * 0.25f;
			float maxX = count * _step * 0.5f + _step * 0.25f;
			float clampedX = Mathf.Clamp(localPt.x, minX, maxX);
			transform.localPosition = new Vector3(clampedX, _rowY + 12f, 0f);

			LayoutOtherItemsWithGap();
			HotbarEditBarUI.SelectItem(ItemInfo, _targetIndex + 1, count, isDragging: true);
		}
	}

	public void OnEndDrag(PointerEventData eventData)
	{
		FinishDrag(commit: true);
	}

	private void OnDisable()
	{
		if (_dragging) FinishDrag(commit: false);
	}

	private void OnDestroy()
	{
		if (_dragging) FinishDrag(commit: false);
	}

	private void FinishDrag(bool commit)
	{
		if (!_dragging) return;
		_dragging = false;
		HotbarSorter.IsDraggingSlot = false;
		HotbarSorter.SuppressBuildUntilFrame = Time.frameCount + 3;
		transform.localScale = new Vector3(_baseScale, _baseScale, 1f);

		if (commit && ItemInfo != null && _visibleRowItems != null && _targetIndex >= 0)
		{
			HotbarSorter.ReorderSlotToVisibleIndex(ItemInfo, _targetIndex, _visibleRowItems);
			SFXManager.instance?.ButtonClick();
		}
		else
		{
			HotbarSorter.ForceRefreshHotbar();
		}

		_visibleRowItems = null;
	}

	private void LayoutOtherItemsWithGap()
	{
		int count = _visibleRowItems.Count;
		int slot = 0;
		for (int i = 0; i < count; i++)
		{
			HotbarItemInfo other = _visibleRowItems[i];
			if (other == null || other == ItemInfo || other.UIObject == null) continue;
			if (slot == _targetIndex) slot++;
			float x = slot * _step - count * _step * 0.5f + _step * 0.5f;
			other.UIObject.transform.localPosition = new Vector3(x, _rowY, 0f);
			slot++;
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
	private static readonly AccessTools.FieldRef<CardManager, List<UpgradeCard>> AvailableCardsRef =
		AccessTools.FieldRefAccess<CardManager, List<UpgradeCard>>("availableCards");
	private static readonly AccessTools.FieldRef<CardManager, List<UpgradeCard>> StartCardsCheckRef =
		AccessTools.FieldRefAccess<CardManager, List<UpgradeCard>>("startCardsCheck");

	private static readonly Dictionary<string, HotbarItemInfo> ItemsById = new Dictionary<string, HotbarItemInfo>(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<GameObject, HotbarItemInfo> ItemsByUI = new Dictionary<GameObject, HotbarItemInfo>();
	private static bool _initialized;

	internal static bool IsDraggingSlot;
	internal static int SuppressBuildUntilFrame;

	internal static bool CanDragSlot()
	{
		return Plugin.EnableHotbarDragAndDrop.Value || HotbarEditBarUI.IsOpen;
	}

	internal static void OnSceneChanged()
	{
		IsDraggingSlot = false;
		SuppressBuildUntilFrame = 0;
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

		HashSet<UpgradeCard> visited = new HashSet<UpgradeCard>();
		Queue<UpgradeCard> bfs = new Queue<UpgradeCard>();
		List<TowerUnlockCard> sceneUnlockCards = new List<TowerUnlockCard>();
		List<TowerUnlockCard> fallbackUnlockCards = new List<TowerUnlockCard>();

		void EnqueueCard(UpgradeCard c)
		{
			if (c != null && visited.Add(c))
			{
				bfs.Enqueue(c);
			}
		}

		foreach (TowerUnlockCard tc in Resources.FindObjectsOfTypeAll<TowerUnlockCard>())
		{
			EnqueueCard(tc);
		}
		if (CardManager.instance != null)
		{
			List<UpgradeCard> startCards = StartCardsCheckRef(CardManager.instance);
			if (startCards != null) foreach (UpgradeCard c in startCards) EnqueueCard(c);
			List<UpgradeCard> availCards = AvailableCardsRef(CardManager.instance);
			if (availCards != null) foreach (UpgradeCard c in availCards) EnqueueCard(c);
		}
		foreach (UpgradeCard c in Resources.FindObjectsOfTypeAll<UpgradeCard>())
		{
			if (c != null && c.gameObject.scene.IsValid()) EnqueueCard(c);
		}

		while (bfs.Count > 0)
		{
			UpgradeCard cur = bfs.Dequeue();
			if (cur is TowerUnlockCard tuc)
			{
				if (tuc.gameObject.scene.IsValid()) sceneUnlockCards.Add(tuc);
				else fallbackUnlockCards.Add(tuc);
			}
			if (cur.unlocks != null)
			{
				foreach (UpgradeCard child in cur.unlocks) EnqueueCard(child);
			}
		}

		void ProcessUnlockCards(List<TowerUnlockCard> cardsList)
		{
			foreach (TowerUnlockCard card in cardsList)
			{
				if (card == null) continue;
				GameObject uiObj = CardUIObjectRef(card);
				if (uiObj == null || !uiObj.scene.IsValid() || ItemsByUI.ContainsKey(uiObj))
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

				string id = ResolveCanonicalId(card, uiObj, isTower, tt);
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

		ProcessUnlockCards(sceneUnlockCards);
		ProcessUnlockCards(fallbackUnlockCards);
	}

	private static void RegisterItem(HotbarItemInfo info)
	{
		if (info == null || string.IsNullOrEmpty(info.Id) || info.UIObject == null)
		{
			return;
		}
		ItemsById[info.Id] = info;
		ItemsByUI[info.UIObject] = info;

		HotbarSlotDragHandler handler = info.UIObject.GetComponent<HotbarSlotDragHandler>();
		if (handler == null)
		{
			handler = info.UIObject.AddComponent<HotbarSlotDragHandler>();
		}
		handler.ItemInfo = info;
	}

	private static string ResolveCanonicalId(TowerUnlockCard card, GameObject uiObj, bool isTower, TowerType? tt)
	{
		string un = (UnlockNameRef(card) ?? string.Empty).Replace(" ", "");
		string title = (card.title ?? string.Empty)
			.Replace("Tower - ", "")
			.Replace("Building - ", "")
			.Replace(" ", "");
		string objName = uiObj != null ? uiObj.name.Replace(" ", "") : string.Empty;
		string combined = (un + "|" + title + "|" + objName).ToLowerInvariant();

		// Cloned Row 2 building UI buttons may inherit myTowerType = TowerType.Siphon or default Crossbow,
		// so match specific building names before ManaSiphon / tt == TowerType.Siphon.
		if (!isTower)
		{
			if (combined.Contains("bank")) return "ManaBank";
			if (combined.Contains("haunted") || combined.Contains("house")) return "HauntedHouse";
			if (combined.Contains("mine")) return "Mine";
			if (combined.Contains("university")) return "University";
			if (combined.Contains("siphon") || tt == TowerType.Siphon) return "ManaSiphon";
			return !string.IsNullOrEmpty(title) ? title : (!string.IsNullOrEmpty(un) ? un : objName);
		}

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
			}
		}

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

	internal static bool IsItemUnlockedRaw(HotbarItemInfo info)
	{
		if (info == null || info.UIObject == null || TowerUnlockManager.instance == null)
		{
			return false;
		}
		List<GameObject> list = info.IsTower
			? UnlockedTowersRef(TowerUnlockManager.instance)
			: UnlockedBuildingsRef(TowerUnlockManager.instance);
		return list != null && list.Contains(info.UIObject);
	}

	internal static List<HotbarItemInfo> GetCurrentlyDisplayedRowItems(bool isTower)
	{
		EnsureInitialized();
		List<HotbarItemInfo> allOrdered = GetOrderedItems(isTower);
		if (HotbarEditBarUI.IsOpen && HotbarEditBarUI.PreviewAllSlots)
		{
			return allOrdered;
		}

		List<HotbarItemInfo> owned = new List<HotbarItemInfo>(allOrdered.Count);
		foreach (HotbarItemInfo item in allOrdered)
		{
			if (IsItemUnlockedRaw(item))
			{
				owned.Add(item);
			}
		}
		return owned;
	}

	internal static int GetVisibleIndex(HotbarItemInfo item, out int total)
	{
		List<HotbarItemInfo> vis = GetCurrentlyDisplayedRowItems(item.IsTower);
		total = vis.Count;
		int idx = vis.IndexOf(item);
		return idx >= 0 ? idx : 0;
	}

	internal static float GetRowY(bool isTower)
	{
		if (!isTower) return 0f;
		if (HotbarEditBarUI.IsOpen && HotbarEditBarUI.PreviewAllSlots) return 100f;
		if (TowerUnlockManager.instance == null) return 0f;
		List<GameObject> blds = UnlockedBuildingsRef(TowerUnlockManager.instance);
		return (blds != null && blds.Count > 0) ? 100f : 0f;
	}

	internal static float GetRowStep(bool isTower, int count)
	{
		if (HotbarEditBarUI.IsOpen && HotbarEditBarUI.PreviewAllSlots && isTower && count > 12)
		{
			return 78f;
		}
		return count > 12 ? 92f : 100f;
	}

	internal static float GetSlotBaseScale(bool isTower, int count)
	{
		if (HotbarEditBarUI.IsOpen && HotbarEditBarUI.PreviewAllSlots && isTower && count > 12)
		{
			return 0.78f;
		}
		return 1f;
	}

	internal static void ApplyPreviewOrRestoreLayout(TowerUnlockManager mgr)
	{
		if (mgr == null) return;
		EnsureInitialized();

		if (HotbarEditBarUI.IsOpen && HotbarEditBarUI.PreviewAllSlots)
		{
			LayoutPreviewRow(GetOrderedItems(isTower: true), isTower: true, yPos: 100f);
			LayoutPreviewRow(GetOrderedItems(isTower: false), isTower: false, yPos: 0f);
			return;
		}

		// Ensure every slot's CanvasGroup alpha, scale & visibility strictly match real unlocked lists
		HashSet<GameObject> ownedSet = new HashSet<GameObject>();
		List<GameObject> unlockedT = UnlockedTowersRef(mgr);
		List<GameObject> unlockedB = UnlockedBuildingsRef(mgr);
		if (unlockedT != null) foreach (GameObject g in unlockedT) if (g != null) ownedSet.Add(g);
		if (unlockedB != null) foreach (GameObject g in unlockedB) if (g != null) ownedSet.Add(g);

		foreach (HotbarItemInfo item in ItemsById.Values)
		{
			if (item.UIObject == null) continue;
			CanvasGroup cg = item.UIObject.GetComponent<CanvasGroup>();
			if (cg != null) cg.alpha = 1f;
			item.UIObject.transform.localScale = Vector3.one;
			bool shouldBeActive = ownedSet.Contains(item.UIObject);
			if (item.UIObject.activeSelf != shouldBeActive)
			{
				item.UIObject.SetActive(shouldBeActive);
			}
		}
	}

	private static void LayoutPreviewRow(List<HotbarItemInfo> items, bool isTower, float yPos)
	{
		if (items == null || items.Count == 0) return;
		int count = items.Count;
		float step = GetRowStep(isTower, count);
		float s = GetSlotBaseScale(isTower, count);
		for (int i = 0; i < count; i++)
		{
			HotbarItemInfo info = items[i];
			if (info == null || info.UIObject == null) continue;
			if (!info.UIObject.activeSelf) info.UIObject.SetActive(true);

			bool owned = IsItemUnlockedRaw(info);
			CanvasGroup cg = info.UIObject.GetComponent<CanvasGroup>();
			if (cg == null) cg = info.UIObject.AddComponent<CanvasGroup>();
			cg.alpha = owned ? 1.0f : 0.48f;

			float x = i * step - count * step * 0.5f + step * 0.5f;
			info.UIObject.transform.localScale = new Vector3(s, s, 1f);
			info.UIObject.transform.localPosition = new Vector3(x, yPos, 0f);
		}
	}

	internal static void ReorderSlotToVisibleIndex(HotbarItemInfo dragged, int targetVisibleIndex, List<HotbarItemInfo> visibleRowItems)
	{
		if (dragged == null || visibleRowItems == null || visibleRowItems.Count == 0)
		{
			ForceRefreshHotbar();
			return;
		}

		List<HotbarItemInfo> fullOrder = GetOrderedItems(dragged.IsTower);
		int clampedTarget = Mathf.Clamp(targetVisibleIndex, 0, visibleRowItems.Count - 1);

		// Determine new sequence inside visibleRowItems
		List<HotbarItemInfo> updatedVisible = new List<HotbarItemInfo>(visibleRowItems);
		int curVisIdx = updatedVisible.IndexOf(dragged);
		if (curVisIdx < 0)
		{
			ForceRefreshHotbar();
			return;
		}
		updatedVisible.RemoveAt(curVisIdx);
		updatedVisible.Insert(clampedTarget, dragged);

		// Map updated visible sequence back into fullOrder's slots occupied by visibleRowItems
		HashSet<HotbarItemInfo> visSet = new HashSet<HotbarItemInfo>(updatedVisible);
		int visCursor = 0;
		for (int i = 0; i < fullOrder.Count; i++)
		{
			if (visSet.Contains(fullOrder[i]))
			{
				fullOrder[i] = updatedVisible[visCursor++];
			}
		}

		SaveOrderList(dragged.IsTower, fullOrder);
		HotbarEditBarUI.SelectItem(dragged, clampedTarget + 1, updatedVisible.Count, isDragging: false);
	}

	internal static bool MoveItemToIndex(string id, int targetIndex, bool amongAllSlots)
	{
		EnsureInitialized();
		if (!ItemsById.TryGetValue(id, out HotbarItemInfo info)) return false;

		if (amongAllSlots)
		{
			List<HotbarItemInfo> all = GetOrderedItems(info.IsTower);
			int clamped = Mathf.Clamp(targetIndex, 0, all.Count - 1);
			int cur = all.FindIndex(x => string.Equals(x.Id, info.Id, StringComparison.OrdinalIgnoreCase));
			if (cur < 0) return false;
			HotbarItemInfo picked = all[cur];
			all.RemoveAt(cur);
			all.Insert(clamped, picked);
			SaveOrderList(info.IsTower, all);
			HotbarEditBarUI.SelectItem(picked, clamped + 1, all.Count);
			return true;
		}

		List<HotbarItemInfo> visible = GetCurrentlyDisplayedRowItems(info.IsTower);
		if (visible.Count == 0 || !visible.Contains(info)) return false;
		ReorderSlotToVisibleIndex(info, targetIndex, visible);
		return true;
	}

	internal static void MoveItemInOrder(string id, int delta)
	{
		EnsureInitialized();
		if (!ItemsById.TryGetValue(id, out HotbarItemInfo info)) return;

		List<HotbarItemInfo> vis = GetCurrentlyDisplayedRowItems(info.IsTower);
		int idx = vis.IndexOf(info);
		if (idx < 0)
		{
			vis = GetOrderedItems(info.IsTower);
			idx = vis.IndexOf(info);
			if (idx < 0) return;
			int targetAll = Mathf.Clamp(idx + delta, 0, vis.Count - 1);
			MoveItemToIndex(id, targetAll, amongAllSlots: true);
			return;
		}

		int target = Mathf.Clamp(idx + delta, 0, vis.Count - 1);
		ReorderSlotToVisibleIndex(info, target, vis);
	}

	private static void SaveOrderList(bool isTower, List<HotbarItemInfo> ordered)
	{
		List<string> ids = new List<string>(ordered.Count);
		for (int i = 0; i < ordered.Count; i++)
		{
			ids.Add(ordered[i].Id);
		}
		string csv = string.Join(",", ids);
		if (isTower)
		{
			Plugin.TowerHotbarOrder.Value = csv;
		}
		else
		{
			Plugin.BuildingHotbarOrder.Value = csv;
		}
		Plugin.Instance?.Config?.Save();
		ForceRefreshHotbar();
		HotbarEditBarUI.RefreshUI();
	}

	internal static ConfigEntry<string> GetPresetConfig(int slot)
	{
		return slot switch
		{
			1 => Plugin.HotbarLayout1,
			2 => Plugin.HotbarLayout2,
			3 => Plugin.HotbarLayout3,
			_ => null
		};
	}

	internal static void SaveLayoutPreset(int slot)
	{
		ConfigEntry<string> cfg = GetPresetConfig(slot);
		if (cfg == null) return;
		EnsureInitialized();
		string tCsv = Plugin.TowerHotbarOrder.Value;
		string bCsv = Plugin.BuildingHotbarOrder.Value;
		cfg.Value = tCsv + "|" + bCsv;
		Plugin.ActiveHotbarPreset.Value = Mathf.Clamp(slot, 1, 3);
		Plugin.Instance?.Config?.Save();
		HotbarEditBarUI.RefreshUI();
	}

	internal static bool LoadLayoutPreset(int slot)
	{
		ConfigEntry<string> cfg = GetPresetConfig(slot);
		if (cfg == null || string.IsNullOrWhiteSpace(cfg.Value))
		{
			return false;
		}
		string[] parts = cfg.Value.Split('|');
		if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0]))
		{
			Plugin.TowerHotbarOrder.Value = parts[0].Trim();
		}
		if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
		{
			Plugin.BuildingHotbarOrder.Value = parts[1].Trim();
		}
		Plugin.ActiveHotbarPreset.Value = Mathf.Clamp(slot, 1, 3);
		Plugin.Instance?.Config?.Save();
		ForceRefreshHotbar();
		HotbarEditBarUI.RefreshUI();
		return true;
	}

	internal static void ResetDefaultOrder()
	{
		Plugin.TowerHotbarOrder.Value = DefaultTowerOrderCsv;
		Plugin.BuildingHotbarOrder.Value = DefaultBuildingOrderCsv;
		Plugin.Instance?.Config?.Save();
		ForceRefreshHotbar();
		HotbarEditBarUI.RefreshUI();
	}

	internal static void SortManagerLists(TowerUnlockManager mgr)
	{
		if (mgr == null) return;
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
		if (TowerUnlockManager.instance == null) return;
		if (Plugin.AutoSortHotbar.Value)
		{
			SortManagerLists(TowerUnlockManager.instance);
		}
		DisplayButtonsMethod?.Invoke(TowerUnlockManager.instance, null);
	}
}

internal static class HotbarEditBarUI
{
	private static readonly Color BgWindow = new Color(0.086f, 0.098f, 0.133f, 0.96f);
	private static readonly Color BgHeader = new Color(0.118f, 0.133f, 0.180f, 1.0f);
	private static readonly Color BgButton = new Color(0.180f, 0.205f, 0.275f, 1.0f);
	private static readonly Color GoldAccent = new Color(0.851f, 0.702f, 0.424f, 1.0f);
	private static readonly Color GoldSoft = new Color(0.320f, 0.265f, 0.165f, 1.0f);
	private static readonly Color GreenOn = new Color(0.180f, 0.620f, 0.388f, 1.0f);
	private static readonly Color TextLight = new Color(0.945f, 0.945f, 0.960f, 1.0f);
	private static readonly Color TextMuted = new Color(0.74f, 0.77f, 0.84f, 1.0f);

	private static Font _fontRegular;
	private static Font _fontBold;
	private static GameObject _canvasObj;
	private static GameObject _pillObj;
	private static RectTransform _pillRt;
	private static Image _pillBg;
	private static Text _pillText;
	private static GameObject _barObj;
	private static RectTransform _barRt;
	private static Text _statusText;
	private static Text _previewBtnText;
	private static Image _previewBtnImg;
	private static Text _autoSortBtnText;
	private static Image _autoSortBtnImg;
	private static readonly Image[] _presetImgs = new Image[3];
	private static readonly Text[] _presetTexts = new Text[3];
	private static Text _savePresetBtnText;

	private static bool _isOpen;
	internal static bool PreviewAllSlots = true;
	internal static HotbarItemInfo HoveredOrSelected;
	private static string _toast;
	private static float _toastUntil;

	internal static bool IsOpen => _isOpen && _barObj != null && _barObj.activeInHierarchy;

	internal static bool IsPointerOverOverlay
	{
		get
		{
			Vector2 mouse = Input.mousePosition;
			if (IsOpen && _barRt != null && RectTransformUtility.RectangleContainsScreenPoint(_barRt, mouse, null))
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
		HoveredOrSelected = null;
		SetOpen(false);
	}

	internal static void SetOpen(bool open)
	{
		EnsureCreated();
		_isOpen = open && GameManager.instance != null;
		if (_barObj != null)
		{
			_barObj.SetActive(_isOpen);
		}
		HotbarSorter.ForceRefreshHotbar();
		RefreshUI();
	}

	internal static void SelectItem(HotbarItemInfo item, int pos, int total, bool isDragging = false)
	{
		HoveredOrSelected = item;
		SetActiveItem(item, pos, total, isDragging);
	}

	internal static void SetActiveItem(HotbarItemInfo item, int pos, int total, bool isDragging)
	{
		if (_statusText == null || item == null) return;
		string row = item.IsTower ? "Row 1" : "Row 2";
		string nick = string.IsNullOrEmpty(item.Nickname) ? "" : $" ({item.Nickname})";
		string prefix = isDragging ? "Dragging " : "Selected: ";
		_statusText.color = isDragging ? GoldAccent : TextLight;
		_statusText.text = $"{prefix}{item.DisplayName}{nick}  •  {row} Slot #{pos}/{total}";
	}

	internal static void RefreshStatusFromSelection()
	{
		if (_statusText == null) return;
		if (_toastUntil > 0f && Time.unscaledTime < _toastUntil && !string.IsNullOrEmpty(_toast))
		{
			_statusText.color = GoldAccent;
			_statusText.text = _toast;
			return;
		}
		if (HoveredOrSelected != null)
		{
			int idx = HotbarSorter.GetVisibleIndex(HoveredOrSelected, out int total);
			SetActiveItem(HoveredOrSelected, idx + 1, total, isDragging: false);
			return;
		}
		_statusText.color = TextMuted;
		_statusText.text = "Drag any tower/building icon directly on the hotbar below!";
	}

	private static void ShowToast(string msg)
	{
		_toast = msg;
		_toastUntil = Time.unscaledTime + 2.2f;
		if (_statusText != null)
		{
			_statusText.color = GoldAccent;
			_statusText.text = msg;
		}
	}

	internal static void RefreshUI()
	{
		if (_pillText != null && _pillBg != null)
		{
			_pillText.text = IsOpen ? "Done" : "Edit Bar";
			_pillText.color = IsOpen ? TextLight : GoldAccent;
			_pillBg.color = IsOpen ? GreenOn : BgHeader;
		}
		if (!IsOpen) return;

		if (_previewBtnText != null && _previewBtnImg != null)
		{
			_previewBtnText.text = PreviewAllSlots ? "All 20: ON" : "All 20: OFF";
			_previewBtnImg.color = PreviewAllSlots ? GreenOn : BgButton;
		}
		if (_autoSortBtnText != null && _autoSortBtnImg != null)
		{
			bool on = Plugin.AutoSortHotbar.Value;
			_autoSortBtnText.text = on ? "Auto-Sort: ON" : "Auto-Sort: OFF";
			_autoSortBtnImg.color = on ? GreenOn : BgButton;
		}

		int active = Mathf.Clamp(Plugin.ActiveHotbarPreset.Value, 1, 3);
		for (int i = 0; i < 3; i++)
		{
			int slot = i + 1;
			bool hasData = !string.IsNullOrWhiteSpace(HotbarSorter.GetPresetConfig(slot)?.Value);
			bool isAct = slot == active;
			if (_presetImgs[i] != null)
			{
				_presetImgs[i].color = isAct ? GoldSoft : BgButton;
			}
			if (_presetTexts[i] != null)
			{
				_presetTexts[i].color = isAct ? GoldAccent : (hasData ? TextLight : TextMuted);
				_presetTexts[i].text = hasData ? $"Layout {slot}" : $"Layout {slot} (Empty)";
			}
		}
		if (_savePresetBtnText != null)
		{
			_savePresetBtnText.text = $"Save -> L{active}";
		}
		RefreshStatusFromSelection();
	}

	internal static void Tick()
	{
		bool inRun = GameManager.instance != null && !GameManager.instance.gameOver && !Plugin.IsPauseUiHidden();
		if (!inRun)
		{
			if (_pillObj != null && _pillObj.activeSelf) _pillObj.SetActive(false);
			if (IsOpen) SetOpen(false);
			return;
		}

		EnsureCreated();

		bool showPill = Plugin.ShowHotbarSortButton.Value && TowerUnlockManager.instance != null;
		if (_pillObj != null && _pillObj.activeSelf != showPill)
		{
			_pillObj.SetActive(showPill);
		}

		if (IsOpen && _toastUntil > 0f && Time.unscaledTime >= _toastUntil)
		{
			_toastUntil = 0f;
			RefreshStatusFromSelection();
		}
	}

	private static void EnsureCreated()
	{
		if (_canvasObj != null) return;
		ResolveFonts();

		_canvasObj = new GameObject("RogueTowerQoL_HotbarSortCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
		UnityEngine.Object.DontDestroyOnLoad(_canvasObj);

		Canvas canvas = _canvasObj.GetComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 420;
		canvas.pixelPerfect = true;

		CanvasScaler scaler = _canvasObj.GetComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1280f, 720f);
		scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		scaler.matchWidthOrHeight = 0.5f;

		CreatePillButton();
		CreateEditBar();
		_barObj.SetActive(false);
		_pillObj.SetActive(false);
	}

	private static void CreatePillButton()
	{
		_pillObj = CreatePanel("HotbarSortPill", _canvasObj.transform, BgHeader);
		_pillRt = _pillObj.GetComponent<RectTransform>();
		_pillRt.anchorMin = new Vector2(0.5f, 0f);
		_pillRt.anchorMax = new Vector2(0.5f, 0f);
		_pillRt.pivot = new Vector2(0.5f, 0f);
		_pillRt.anchoredPosition = new Vector2(-450f, 14f);
		_pillRt.sizeDelta = new Vector2(78f, 28f);

		_pillBg = _pillObj.GetComponent<Image>();
		Outline ol = _pillObj.AddComponent<Outline>();
		ol.effectColor = GoldAccent;
		ol.effectDistance = new Vector2(1f, -1f);

		Button btn = _pillObj.AddComponent<Button>();
		btn.navigation = new Navigation { mode = Navigation.Mode.None };
		btn.onClick.AddListener(() => SetOpen(!_isOpen));

		_pillText = CreateText(_pillObj.transform, "Edit Bar", 13, FontStyle.Bold, GoldAccent, TextAnchor.MiddleCenter);
		StretchFill(_pillText.rectTransform);
	}

	private static void CreateEditBar()
	{
		_barObj = CreatePanel("HotbarEditToolbar", _canvasObj.transform, BgWindow);
		_barRt = _barObj.GetComponent<RectTransform>();
		_barRt.anchorMin = new Vector2(0.5f, 0f);
		_barRt.anchorMax = new Vector2(0.5f, 0f);
		_barRt.pivot = new Vector2(0.5f, 0f);
		_barRt.anchoredPosition = new Vector2(0f, 224f);
		_barRt.sizeDelta = new Vector2(760f, 88f);

		Outline border = _barObj.AddComponent<Outline>();
		border.effectColor = GoldAccent;
		border.effectDistance = new Vector2(2f, -2f);

		VerticalLayoutGroup vlg = _barObj.AddComponent<VerticalLayoutGroup>();
		vlg.padding = new RectOffset(10, 10, 8, 8);
		vlg.spacing = 6f;
		vlg.childControlWidth = true;
		vlg.childControlHeight = true;
		vlg.childForceExpandWidth = true;
		vlg.childForceExpandHeight = true;

		// Row 1: Status + < Left / Right > + Preview All + Done
		GameObject row1 = CreatePanel("Row1", _barObj.transform, BgHeader);
		HorizontalLayoutGroup h1 = row1.AddComponent<HorizontalLayoutGroup>();
		h1.padding = new RectOffset(8, 4, 2, 2);
		h1.spacing = 6f;
		h1.childControlWidth = true;
		h1.childControlHeight = true;
		h1.childForceExpandWidth = false;

		_statusText = CreateText(row1.transform, "Drag any tower/building icon directly on the hotbar below!", 13, FontStyle.Bold, TextMuted, TextAnchor.MiddleLeft);
		_statusText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

		Button leftBtn = CreateSimpleButton(row1.transform, "< Left", BgButton, TextLight, 12, () =>
		{
			if (HoveredOrSelected != null) HotbarSorter.MoveItemInOrder(HoveredOrSelected.Id, -1);
			else ShowToast("Click or hover a hotbar icon first, then drag or nudge!");
		});
		leftBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 62f;

		Button rightBtn = CreateSimpleButton(row1.transform, "Right >", BgButton, TextLight, 12, () =>
		{
			if (HoveredOrSelected != null) HotbarSorter.MoveItemInOrder(HoveredOrSelected.Id, +1);
			else ShowToast("Click or hover a hotbar icon first, then drag or nudge!");
		});
		rightBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 66f;

		Button prevBtn = CreateSimpleButton(row1.transform, "All 20: ON", GreenOn, TextLight, 12, () =>
		{
			PreviewAllSlots = !PreviewAllSlots;
			HotbarSorter.ForceRefreshHotbar();
			RefreshUI();
		});
		prevBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 88f;
		_previewBtnImg = prevBtn.GetComponent<Image>();
		_previewBtnText = prevBtn.GetComponentInChildren<Text>();

		Button doneBtn = CreateSimpleButton(row1.transform, "Done", GreenOn, TextLight, 13, () => SetOpen(false));
		doneBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 64f;

		// Row 2: Layout 1 / 2 / 3 + Save -> L# + Reset + Auto-Sort
		GameObject row2 = CreatePanel("Row2", _barObj.transform, Color.clear);
		HorizontalLayoutGroup h2 = row2.AddComponent<HorizontalLayoutGroup>();
		h2.padding = new RectOffset(2, 2, 0, 0);
		h2.spacing = 6f;
		h2.childControlWidth = true;
		h2.childControlHeight = true;
		h2.childForceExpandWidth = false;

		for (int i = 0; i < 3; i++)
		{
			int slot = i + 1;
			Button pb = CreateSimpleButton(row2.transform, $"Layout {slot}", BgButton, TextLight, 12, () =>
			{
				Plugin.ActiveHotbarPreset.Value = slot;
				if (HotbarSorter.LoadLayoutPreset(slot))
				{
					ShowToast($"Loaded Hotbar Layout {slot}");
				}
				else
				{
					Plugin.Instance?.Config?.Save();
					RefreshUI();
					ShowToast($"Selected Layout {slot} (empty — click Save -> L{slot})");
				}
			});
			pb.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
			_presetImgs[i] = pb.GetComponent<Image>();
			_presetTexts[i] = pb.GetComponentInChildren<Text>();
		}

		Button saveBtn = CreateSimpleButton(row2.transform, "Save -> L1", GreenOn, TextLight, 12, () =>
		{
			int s = Mathf.Clamp(Plugin.ActiveHotbarPreset.Value, 1, 3);
			HotbarSorter.SaveLayoutPreset(s);
			ShowToast($"Saved current hotbar order to Layout {s}");
		});
		saveBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;
		_savePresetBtnText = saveBtn.GetComponentInChildren<Text>();

		Button resetBtn = CreateSimpleButton(row2.transform, "Reset", BgButton, GoldAccent, 12, () =>
		{
			HotbarSorter.ResetDefaultOrder();
			ShowToast("Reset hotbar to default order");
		});
		resetBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 68f;

		Button autoBtn = CreateSimpleButton(row2.transform, "Auto-Sort: ON", GreenOn, TextLight, 12, () =>
		{
			Plugin.SetAutoSortHotbar(!Plugin.AutoSortHotbar.Value);
			ShowToast($"Auto-Sort Hotbar: {(Plugin.AutoSortHotbar.Value ? "ON" : "OFF")}");
		});
		autoBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;
		_autoSortBtnImg = autoBtn.GetComponent<Image>();
		_autoSortBtnText = autoBtn.GetComponentInChildren<Text>();
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
		if (_fontRegular == null)
		{
			_fontRegular = Resources.GetBuiltinResource<Font>("Arial.ttf");
		}
		if (_fontBold == null)
		{
			_fontBold = _fontRegular;
		}
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
