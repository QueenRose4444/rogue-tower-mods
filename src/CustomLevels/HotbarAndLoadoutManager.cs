using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace RogueTowerCustomLevels;

internal sealed class HotbarItemInfo
{
	public string Id;
	public string DisplayName;
	public string Nickname;
	public bool IsTower;
	public TowerType? TowerType;
	public GameObject UIObject;
	public TowerUnlockCard UnlockCard;
}

internal static class HotbarLoadoutPatches
{
	[HarmonyPatch(typeof(TowerUnlockManager), "Start")]
	[HarmonyPostfix]
	private static void TowerUnlockManager_Start_Postfix(TowerUnlockManager __instance)
	{
		HotbarAndLoadoutManager.OnTowerUnlockManagerStart(__instance);
	}

	[HarmonyPatch(typeof(TowerUnlockManager), "DisplayButtons")]
	[HarmonyPrefix]
	private static void TowerUnlockManager_DisplayButtons_Prefix(TowerUnlockManager __instance)
	{
		if (Plugin.AutoSortHotbar.Value)
		{
			HotbarAndLoadoutManager.SortManagerLists(__instance);
		}
	}

	[HarmonyPatch(typeof(CardManager), nameof(CardManager.DrawCards), new[] { typeof(int) })]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.High)]
	private static void CardManager_DrawCards_FilterHotbar_Prefix(CardManager __instance)
	{
		if (Plugin.LockCardPoolToHotbar.Value && __instance != null)
		{
			HotbarAndLoadoutManager.FilterAvailableCardsToHotbar(__instance);
		}
	}
}

internal static class HotbarAndLoadoutManager
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
	private static readonly AccessTools.FieldRef<TowerUpgradeCard, TowerType> UpgradeTowerTypeRef =
		AccessTools.FieldRefAccess<TowerUpgradeCard, TowerType>("towerType");
	private static readonly AccessTools.FieldRef<UpgradeCard, string> UnlockNameRef =
		AccessTools.FieldRefAccess<UpgradeCard, string>("unlockName");

	private static readonly AccessTools.FieldRef<CardManager, List<UpgradeCard>> AvailableCardsRef =
		AccessTools.FieldRefAccess<CardManager, List<UpgradeCard>>("availableCards");

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
		if (Plugin.AutoApplyLoadoutSlot.Value >= 1 && Plugin.AutoApplyLoadoutSlot.Value <= 5)
		{
			mgr?.StartCoroutine(DelayedAutoApplyLoadout(Plugin.AutoApplyLoadoutSlot.Value));
		}
	}

	private static IEnumerator DelayedAutoApplyLoadout(int slot)
	{
		yield return null;
		ApplyLoadoutSlot(slot, replaceExisting: true);
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

		// 1. Capture default Ballista (already in unlockedTowers at start)
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
						UIObject = go,
						UnlockCard = null
					});
				}
			}
		}

		// 2. Capture all other Towers & Buildings from TowerUnlockCard instances in scene
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

			string id = ResolveCanonicalId(card, uiObj, isTower, tt);
			(string disp, string nick) = GetFriendlyNames(id, card.title);

			RegisterItem(new HotbarItemInfo
			{
				Id = id,
				DisplayName = disp,
				Nickname = nick,
				IsTower = isTower,
				TowerType = tt,
				UIObject = uiObj,
				UnlockCard = card
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

	private static string ResolveCanonicalId(TowerUnlockCard card, GameObject uiObj, bool isTower, TowerType? tt)
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

	internal static bool IsItemUnlockedById(string id)
	{
		EnsureInitialized();
		return ItemsById.TryGetValue(id, out HotbarItemInfo info) && IsItemUnlocked(info);
	}

	internal static bool SetItemUnlocked(string id, bool unlock)
	{
		EnsureInitialized();
		if (TowerUnlockManager.instance == null || !ItemsById.TryGetValue(id, out HotbarItemInfo info) || info.UIObject == null)
		{
			return false;
		}

		List<GameObject> list = info.IsTower
			? UnlockedTowersRef(TowerUnlockManager.instance)
			: UnlockedBuildingsRef(TowerUnlockManager.instance);

		List<UpgradeCard> avail = CardManager.instance != null ? AvailableCardsRef(CardManager.instance) : null;

		if (unlock)
		{
			info.UIObject.SetActive(true);
			if (list != null && !list.Contains(info.UIObject))
			{
				list.Add(info.UIObject);
			}
			if (info.UnlockCard != null)
			{
				CheatActions.RecordCardGranted(info.UnlockCard);
				if (avail != null)
				{
					avail.RemoveAll(x => x == info.UnlockCard);
					if (info.UnlockCard.unlocks != null)
					{
						foreach (UpgradeCard child in info.UnlockCard.unlocks)
						{
							if (child != null && child.unlocked && !avail.Contains(child))
							{
								avail.Add(child);
							}
						}
					}
				}
				ModuleBridge.NotifyUpgradesMenuCardUnlocked(UnlockNameRef(info.UnlockCard), info.UnlockCard.title, false);
			}
		}
		else
		{
			info.UIObject.SetActive(false);
			list?.RemoveAll(x => x == info.UIObject);
			if (info.UnlockCard != null)
			{
				CheatActions.UnrecordCardGranted(info.UnlockCard);
				if (avail != null && info.UnlockCard.unlocked && !avail.Contains(info.UnlockCard))
				{
					avail.Add(info.UnlockCard);
				}
			}
			if (avail != null && info.TowerType.HasValue)
			{
				TowerType tt = info.TowerType.Value;
				avail.RemoveAll(c => c is TowerUpgradeCard tuc && UpgradeTowerTypeRef(tuc) == tt);
			}
		}

		ForceRefreshHotbar();
		return true;
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
	}

	internal static void ResetDefaultOrder()
	{
		Plugin.TowerHotbarOrder.Value = DefaultTowerOrderCsv;
		Plugin.BuildingHotbarOrder.Value = DefaultBuildingOrderCsv;
		ForceRefreshHotbar();
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

	internal static ConfigEntry<string> GetSlotConfig(int slot)
	{
		return slot switch
		{
			1 => Plugin.LoadoutSlot1,
			2 => Plugin.LoadoutSlot2,
			3 => Plugin.LoadoutSlot3,
			4 => Plugin.LoadoutSlot4,
			5 => Plugin.LoadoutSlot5,
			_ => null
		};
	}

	internal static string GetSlotSummary(int slot)
	{
		ConfigEntry<string> cfg = GetSlotConfig(slot);
		if (cfg == null || string.IsNullOrWhiteSpace(cfg.Value))
		{
			return "(Empty)";
		}
		string[] halves = cfg.Value.Split('|');
		int tCount = halves.Length > 0 ? halves[0].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length : 0;
		int bCount = halves.Length > 1 ? halves[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length : 0;
		string preview = halves[0].Replace(",", ", ");
		if (preview.Length > 42)
		{
			preview = preview.Substring(0, 39) + "...";
		}
		return $"{tCount}T + {bCount}B  ({preview})";
	}

	internal static void SaveCurrentToSlot(int slot)
	{
		ConfigEntry<string> cfg = GetSlotConfig(slot);
		if (cfg == null)
		{
			return;
		}
		EnsureInitialized();
		List<string> activeTowers = new List<string>();
		foreach (HotbarItemInfo info in GetOrderedItems(isTower: true))
		{
			if (IsItemUnlocked(info)) activeTowers.Add(info.Id);
		}
		List<string> activeBuildings = new List<string>();
		foreach (HotbarItemInfo info in GetOrderedItems(isTower: false))
		{
			if (IsItemUnlocked(info)) activeBuildings.Add(info.Id);
		}
		cfg.Value = string.Join(",", activeTowers) + "|" + string.Join(",", activeBuildings);
	}

	internal static void ClearSlot(int slot)
	{
		ConfigEntry<string> cfg = GetSlotConfig(slot);
		if (cfg != null)
		{
			cfg.Value = string.Empty;
		}
	}

	internal static int ApplyLoadoutSlot(int slot, bool replaceExisting = true)
	{
		ConfigEntry<string> cfg = GetSlotConfig(slot);
		if (cfg == null || string.IsNullOrWhiteSpace(cfg.Value) || TowerUnlockManager.instance == null)
		{
			return 0;
		}
		EnsureInitialized();

		HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string[] halves = cfg.Value.Split('|');
		foreach (string half in halves)
		{
			string[] tokens = half.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string t in tokens)
			{
				wanted.Add(t.Trim());
			}
		}

		if (replaceExisting)
		{
			List<string> allIds = new List<string>(ItemsById.Keys);
			foreach (string id in allIds)
			{
				if (!wanted.Contains(id))
				{
					SetItemUnlocked(id, unlock: false);
				}
			}
		}

		int unlockedCount = 0;
		foreach (string id in wanted)
		{
			if (SetItemUnlocked(id, unlock: true))
			{
				unlockedCount++;
			}
		}

		ForceRefreshHotbar();
		SFXManager.instance?.ButtonClick();
		return unlockedCount;
	}

	internal static void FilterAvailableCardsToHotbar(CardManager cm)
	{
		EnsureInitialized();
		List<UpgradeCard> avail = AvailableCardsRef(cm);
		if (avail == null || avail.Count == 0)
		{
			return;
		}

		HashSet<TowerType> activeTowerTypes = new HashSet<TowerType>();
		foreach (HotbarItemInfo info in ItemsById.Values)
		{
			if (info.TowerType.HasValue && IsItemUnlocked(info))
			{
				activeTowerTypes.Add(info.TowerType.Value);
			}
		}

		avail.RemoveAll(card =>
		{
			if (card == null) return true;
			if (card is TowerUnlockCard) return true;
			if (card is TowerUpgradeCard tuc)
			{
				TowerType tt = UpgradeTowerTypeRef(tuc);
				if (tt != TowerType.Global && tt != TowerType.DOT && !activeTowerTypes.Contains(tt))
				{
					return true;
				}
			}
			return false;
		});
	}

	internal static List<string> GetActiveHotbarIds(bool isTower)
	{
		List<string> ids = new List<string>();
		foreach (HotbarItemInfo info in GetOrderedItems(isTower))
		{
			if (IsItemUnlocked(info))
			{
				ids.Add(info.Id);
			}
		}
		return ids;
	}
}
