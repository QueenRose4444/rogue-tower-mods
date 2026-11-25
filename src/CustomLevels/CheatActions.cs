using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RogueTowerCustomLevels;

internal enum CardFilterCategory
{
	All = 0,
	Available = 1,
	Towers = 2,
	Buildings = 3,
	DOT = 4,
	Buffs = 5,
	Monsters = 6
}

internal sealed class CardCatalogEntry
{
	public UpgradeCard Card;
	public string UnlockName;
	public string Title;
	public string Description;
	public CardFilterCategory Category;
	public string TreeKey;
	public string TreeLabel;
	public bool IsMonster;
	public bool IsTowerUnlock;
	public bool IsBuildingUnlock;
}

internal sealed class TreeGroupSummary
{
	public string TreeKey;
	public string TreeLabel;
	public CardFilterCategory Category;
	public List<CardCatalogEntry> Cards = new List<CardCatalogEntry>();
	public List<CardCatalogEntry> BranchRoots = new List<CardCatalogEntry>();
}

internal static class CheatPatches
{
	private static readonly AccessTools.FieldRef<Tower, int> DamageRef =
		AccessTools.FieldRefAccess<Tower, int>("<damage>k__BackingField");
	private static readonly AccessTools.FieldRef<Tower, float> RangeRef =
		AccessTools.FieldRefAccess<Tower, float>("<range>k__BackingField");
	private static readonly AccessTools.FieldRef<Tower, float> RpsRef =
		AccessTools.FieldRefAccess<Tower, float>("rps");
	private static readonly AccessTools.FieldRef<Dropper, Vector2[]> DropPointsRef =
		AccessTools.FieldRefAccess<Dropper, Vector2[]>("dropPoints");
	private static readonly AccessTools.FieldRef<TowerUI, Text> RangeTextRef =
		AccessTools.FieldRefAccess<TowerUI, Text>("rangeText");
	private static readonly AccessTools.FieldRef<CardManager, UpgradeCard[]> CardsRef =
		AccessTools.FieldRefAccess<CardManager, UpgradeCard[]>("cards");

	private static readonly Dictionary<int, float> LastAppliedRpmMultiplier = new Dictionary<int, float>();

	internal static void ClearRpmTracking()
	{
		LastAppliedRpmMultiplier.Clear();
	}

	[HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.CheckMoney))]
	[HarmonyPrefix]
	private static bool ResourceManager_CheckMoney_Prefix(ref bool __result)
	{
		if (Plugin.InfiniteMoney.Value)
		{
			__result = true;
			return false;
		}
		return true;
	}

	[HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.Spend))]
	[HarmonyPrefix]
	private static bool ResourceManager_Spend_Prefix()
	{
		return !Plugin.InfiniteMoney.Value;
	}

	[HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.CheckMana))]
	[HarmonyPrefix]
	private static bool ResourceManager_CheckMana_Prefix(ref bool __result)
	{
		if (Plugin.InfiniteMana.Value)
		{
			__result = true;
			return false;
		}
		return true;
	}

	[HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.SpendMana))]
	[HarmonyPrefix]
	private static bool ResourceManager_SpendMana_Prefix()
	{
		return !Plugin.InfiniteMana.Value;
	}

	[HarmonyPatch(typeof(GameManager), nameof(GameManager.TakeDamage))]
	[HarmonyPrefix]
	private static bool GameManager_TakeDamage_Prefix()
	{
		return !Plugin.GodMode.Value;
	}

	[HarmonyPatch(typeof(Tower), nameof(Tower.SetStats))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static void Tower_SetStats_Prefix(Tower __instance)
	{
		if (__instance == null)
		{
			return;
		}
		int id = __instance.GetInstanceID();
		if (LastAppliedRpmMultiplier.TryGetValue(id, out float prevMult) && prevMult > 0.01f)
		{
			__instance.rpm /= prevMult;
			LastAppliedRpmMultiplier.Remove(id);
		}
	}

	[HarmonyPatch(typeof(Tower), nameof(Tower.SetStats))]
	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	private static void Tower_SetStats_Postfix(Tower __instance)
	{
		if (__instance == null)
		{
			return;
		}

		if (Plugin.InfiniteRange.Value)
		{
			RangeRef(__instance) = Mathf.Max(__instance.range, 250f);
		}

		float dmgMult = Plugin.DamageMultiplier.Value;
		if (dmgMult > 1.01f)
		{
			float scaled = Mathf.Min((float)__instance.damage * dmgMult, 500000000f);
			DamageRef(__instance) = Mathf.Max(__instance.damage, Mathf.RoundToInt(scaled));
		}

		float rpmMult = Plugin.FireRateMultiplier.Value;
		if (rpmMult > 1.01f)
		{
			__instance.rpm *= rpmMult;
			RpsRef(__instance) = Mathf.Max(0.015f, 60f / Mathf.Max(1f, __instance.rpm));
			LastAppliedRpmMultiplier[__instance.GetInstanceID()] = rpmMult;
		}
	}

	[HarmonyPatch(typeof(Dropper), "SetDropPoints")]
	[HarmonyPrefix]
	private static bool Dropper_SetDropPoints_Prefix(Dropper __instance)
	{
		if (!Plugin.InfiniteRange.Value || __instance == null)
		{
			return true;
		}

		// Avoid 500x500 raycast freeze when range = 250 by sampling existing path Waypoints directly
		Waypoint[] waypoints = UnityEngine.Object.FindObjectsOfType<Waypoint>();
		List<Vector2> pts = new List<Vector2>(64);
		HashSet<int> seen = new HashSet<int>();
		Vector3 towerPos = __instance.transform.position;

		int step = (waypoints.Length > 80) ? Mathf.Max(1, waypoints.Length / 80) : 1;
		for (int i = 0; i < waypoints.Length; i += step)
		{
			Waypoint wp = waypoints[i];
			if (wp == null)
			{
				continue;
			}
			int dx = Mathf.RoundToInt(wp.transform.position.x - towerPos.x);
			int dz = Mathf.RoundToInt(wp.transform.position.z - towerPos.z);
			int key = (dx + 512) * 2048 + (dz + 512);
			if (seen.Add(key))
			{
				pts.Add(new Vector2(dx, dz));
			}
		}

		if (pts.Count == 0)
		{
			pts.Add(Vector2.zero);
		}

		Vector2[] arr = pts.ToArray();
		DropPointsRef(__instance) = arr;
		int effectiveCount = Mathf.Min(arr.Length, 40);
		RpsRef(__instance) = Mathf.Max(0.015f, 60f / Mathf.Max(1f, __instance.rpm) * (10f / (10f + (float)effectiveCount)));
		__instance.dropperRPMdisplay = __instance.rpm * (10f + (float)effectiveCount) / 10f;
		return false;
	}

	[HarmonyPatch(typeof(TowerUI), "UpdateText")]
	[HarmonyPostfix]
	private static void TowerUI_UpdateText_Postfix(TowerUI __instance)
	{
		if (Plugin.InfiniteRange.Value && __instance != null)
		{
			Text rt = RangeTextRef(__instance);
			if (rt != null)
			{
				rt.text = "Range: INF (250)";
			}
		}
	}

	[HarmonyPatch(typeof(CardManager), nameof(CardManager.ActivateCard))]
	[HarmonyPrefix]
	private static void CardManager_ActivateCard_Prefix(CardManager __instance, int index)
	{
		if (__instance == null)
		{
			return;
		}
		UpgradeCard[] cards = CardsRef(__instance);
		if (cards != null && index >= 0 && index < cards.Length && cards[index] != null)
		{
			CheatActions.RecordCardGranted(cards[index]);
		}
	}

	[HarmonyPatch(typeof(CardManager), nameof(CardManager.ActivateMonsterCard))]
	[HarmonyPrefix]
	private static void CardManager_ActivateMonsterCard_Prefix(CardManager __instance, int index)
	{
		if (__instance == null)
		{
			return;
		}
		UpgradeCard[] cards = CardsRef(__instance);
		if (cards != null && index >= 0 && index < cards.Length && cards[index] != null)
		{
			CheatActions.RecordCardGranted(cards[index]);
		}
	}

	[HarmonyPatch(typeof(TowerFlyweight), nameof(TowerFlyweight.AddNewTower))]
	[HarmonyPostfix]
	private static void TowerFlyweight_AddNewTower_Postfix(TowerFlyweight __instance)
	{
		if (Plugin.NoTowerPriceScaling.Value && __instance != null)
		{
			CheatActions.ApplyFlatPriceToFlyweight(__instance);
		}
	}

	[HarmonyPatch(typeof(TowerFlyweight), nameof(TowerFlyweight.RemoveTower))]
	[HarmonyPostfix]
	private static void TowerFlyweight_RemoveTower_Postfix(TowerFlyweight __instance)
	{
		if (Plugin.NoTowerPriceScaling.Value && __instance != null)
		{
			CheatActions.ApplyFlatPriceToFlyweight(__instance);
		}
	}

	[HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.EnterBuildMode))]
	[HarmonyPrefix]
	private static void BuildingManager_EnterBuildMode_Prefix(ref int _priceIncrease)
	{
		if (Plugin.NoTowerPriceScaling.Value)
		{
			_priceIncrease = 0;
		}
	}
}

internal static class CheatActions
{
	private static readonly AccessTools.FieldRef<ResourceManager, int> GoldRef =
		AccessTools.FieldRefAccess<ResourceManager, int>("gold");
	private static readonly AccessTools.FieldRef<ResourceManager, int> ManaRef =
		AccessTools.FieldRefAccess<ResourceManager, int>("mana");

	private static readonly AccessTools.FieldRef<UpgradeCard, string> UnlockNameRef =
		AccessTools.FieldRefAccess<UpgradeCard, string>("unlockName");
	private static readonly AccessTools.FieldRef<TowerUnlockCard, GameObject> TowerUIObjectRef =
		AccessTools.FieldRefAccess<TowerUnlockCard, GameObject>("towerUIObject");
	private static readonly AccessTools.FieldRef<TowerUnlockCard, bool> IsTowerRef =
		AccessTools.FieldRefAccess<TowerUnlockCard, bool>("isTower");
	private static readonly AccessTools.FieldRef<TowerUpgradeCard, TowerType> CardTowerTypeRef =
		AccessTools.FieldRefAccess<TowerUpgradeCard, TowerType>("towerType");

	private static readonly AccessTools.FieldRef<TowerUnlockManager, List<GameObject>> UnlockedTowersRef =
		AccessTools.FieldRefAccess<TowerUnlockManager, List<GameObject>>("unlockedTowers");
	private static readonly AccessTools.FieldRef<TowerUnlockManager, List<GameObject>> UnlockedBuildingsRef =
		AccessTools.FieldRefAccess<TowerUnlockManager, List<GameObject>>("unlockedBuildings");

	private static readonly AccessTools.FieldRef<CardManager, List<UpgradeCard>> AvailableCardsRef =
		AccessTools.FieldRefAccess<CardManager, List<UpgradeCard>>("availableCards");
	private static readonly AccessTools.FieldRef<CardManager, List<UpgradeCard>> AvailableMonsterCardsRef =
		AccessTools.FieldRefAccess<CardManager, List<UpgradeCard>>("availableMonsterCards");
	private static readonly AccessTools.FieldRef<CardManager, List<UpgradeCard>> StartCardsCheckRef =
		AccessTools.FieldRefAccess<CardManager, List<UpgradeCard>>("startCardsCheck");

	private static readonly MethodInfo EnemyDieMethod =
		typeof(Enemy).GetMethod("Die", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly HashSet<UpgradeCard> GrantedCardsThisRun = new HashSet<UpgradeCard>();
	private static readonly Dictionary<UpgradeCard, CardCatalogEntry> EntryByCard = new Dictionary<UpgradeCard, CardCatalogEntry>();
	private static readonly List<CardCatalogEntry> AllEntries = new List<CardCatalogEntry>();
	private static readonly List<CardCatalogEntry> UnlockEntries = new List<CardCatalogEntry>();
	private static readonly List<TreeGroupSummary> TreeGroups = new List<TreeGroupSummary>();
	private static readonly Dictionary<string, TreeGroupSummary> TreeGroupByKey = new Dictionary<string, TreeGroupSummary>();

	private static bool _catalogBuilt;

	// Cached reflection for optional CardDataLib
	private static bool _cardDataLibChecked;
	private static MethodInfo _cdlGetCardMethod;
	private static PropertyInfo _cddCategoryProp;
	private static PropertyInfo _cddSubcategoryProp;

	internal static bool IsInGameRun => GameManager.instance != null && CardManager.instance != null;

	internal static void OnSceneChanged(string sceneName)
	{
		GrantedCardsThisRun.Clear();
		EntryByCard.Clear();
		AllEntries.Clear();
		UnlockEntries.Clear();
		TreeGroups.Clear();
		TreeGroupByKey.Clear();
		_catalogBuilt = false;
		CheatPatches.ClearRpmTracking();
	}

	internal static void Tick()
	{
		if (Plugin.InfiniteMana.Value && ResourceManager.instance != null)
		{
			int maxM = ResourceManager.instance.maxMana;
			int missing = maxM - ManaRef(ResourceManager.instance);
			if (missing > 0)
			{
				ResourceManager.instance.AddMana(missing);
			}
		}
	}

	internal static void RefreshAllPlacedTowers()
	{
		if (TowerManager.instance != null)
		{
			TowerManager.instance.UpdateAllTowers();
		}
		TowerUI openUi = UnityEngine.Object.FindObjectOfType<TowerUI>();
		if (openUi != null)
		{
			Tower t = AccessTools.FieldRefAccess<TowerUI, Tower>("myTower")(openUi);
			if (t != null)
			{
				openUi.SetStats(t);
			}
		}
	}

	internal static void RecordCardGranted(UpgradeCard card)
	{
		if (card != null)
		{
			GrantedCardsThisRun.Add(card);
		}
	}

	internal static void UnrecordCardGranted(UpgradeCard card)
	{
		if (card != null)
		{
			GrantedCardsThisRun.Remove(card);
		}
	}

	private static readonly AccessTools.FieldRef<TowerFlyweight, int> FlyBasePriceRef =
		AccessTools.FieldRefAccess<TowerFlyweight, int>("basePrice");
	private static readonly AccessTools.FieldRef<TowerFlyweight, BuildButtonUI> FlyBuildButtonRef =
		AccessTools.FieldRefAccess<TowerFlyweight, BuildButtonUI>("myBuildButton");

	internal static void ApplyFlatPriceToFlyweight(TowerFlyweight fw)
	{
		if (fw == null) return;
		int baseP = FlyBasePriceRef(fw);
		fw.currentPrice = baseP;
		fw.sellPrice = baseP;
		FlyBuildButtonRef(fw)?.UpdatePriceText();
	}

	internal static void RefreshTowerPrices()
	{
		TowerFlyweight[] all = UnityEngine.Object.FindObjectsOfType<TowerFlyweight>();
		bool flat = Plugin.NoTowerPriceScaling.Value;
		foreach (TowerFlyweight fw in all)
		{
			if (fw == null) continue;
			if (flat)
			{
				ApplyFlatPriceToFlyweight(fw);
			}
			else
			{
				int baseP = FlyBasePriceRef(fw);
				int cnt = fw.towers != null ? fw.towers.Count : 0;
				fw.currentPrice = baseP + cnt * fw.priceIncrease;
				fw.sellPrice = baseP + Mathf.Max(0, cnt - 1) * fw.priceIncrease;
				FlyBuildButtonRef(fw)?.UpdatePriceText();
			}
		}
	}

	internal static bool IsCardOwned(CardCatalogEntry entry)
	{
		if (entry == null || entry.Card == null)
		{
			return false;
		}
		if (GrantedCardsThisRun.Contains(entry.Card))
		{
			return true;
		}
		if (entry.Card is TowerUnlockCard tuc && TowerUnlockManager.instance != null)
		{
			GameObject uiObj = TowerUIObjectRef(tuc);
			if (uiObj != null)
			{
				List<GameObject> list = IsTowerRef(tuc)
					? UnlockedTowersRef(TowerUnlockManager.instance)
					: UnlockedBuildingsRef(TowerUnlockManager.instance);
				if (list != null && list.Contains(uiObj))
				{
					return true;
				}
			}
		}
		return false;
	}

	internal static bool IsCardCurrentlyAvailable(CardCatalogEntry entry)
	{
		if (entry == null || entry.Card == null || CardManager.instance == null)
		{
			return false;
		}
		List<UpgradeCard> pool = entry.IsMonster
			? AvailableMonsterCardsRef(CardManager.instance)
			: AvailableCardsRef(CardManager.instance);
		return pool != null && pool.Contains(entry.Card);
	}

	internal static int GetCurrentGold()
	{
		return ResourceManager.instance != null ? GoldRef(ResourceManager.instance) : 0;
	}

	internal static int GetCurrentWave()
	{
		return SpawnManager.instance != null ? SpawnManager.instance.level : 0;
	}

	internal static int GetMetaXP()
	{
		return UpgradeManager.instance != null ? UpgradeManager.instance.xp : PlayerPrefs.GetInt("XP", 0);
	}

	internal static int ActiveCheatCount()
	{
		int count = 0;
		if (Plugin.InfiniteMoney.Value) count++;
		if (Plugin.InfiniteMana.Value) count++;
		if (Plugin.InfiniteRange.Value) count++;
		if (Plugin.GodMode.Value) count++;
		if (Plugin.InfiniteBanishes.Value) count++;
		if (Plugin.NoTowerPriceScaling.Value) count++;
		if (Plugin.DamageMultiplier.Value > 1.01f) count++;
		if (Plugin.FireRateMultiplier.Value > 1.01f) count++;
		return count;
	}

	internal static void AddGold(int amount)
	{
		if (ResourceManager.instance != null)
		{
			ResourceManager.instance.AddMoney(amount);
			SFXManager.instance?.ButtonClick();
		}
	}

	internal static void FillMana()
	{
		if (ResourceManager.instance != null)
		{
			int missing = ResourceManager.instance.maxMana - ManaRef(ResourceManager.instance);
			if (missing > 0)
			{
				ResourceManager.instance.AddMana(missing);
			}
			SFXManager.instance?.ButtonClick();
		}
	}

	internal static void AddMaxMana(int amount)
	{
		if (ResourceManager.instance != null)
		{
			ResourceManager.instance.AddMaxMana(amount);
			ResourceManager.instance.AddMana(amount);
			SFXManager.instance?.ButtonClick();
		}
	}

	internal static void AddManaRegen(int amount)
	{
		if (ResourceManager.instance != null)
		{
			ResourceManager.instance.UpdateManaGatherRate(amount);
			SFXManager.instance?.ButtonClick();
		}
	}

	internal static void HealTowerFull()
	{
		if (GameManager.instance != null)
		{
			GameManager.instance.RepairTower(GameManager.instance.maxHealth);
			SFXManager.instance?.ButtonClick();
		}
	}

	internal static void AddMaxTowerHealth(int amount)
	{
		if (GameManager.instance != null)
		{
			GameManager.instance.IncreaseTowerHealth(amount);
			SFXManager.instance?.ButtonClick();
		}
	}

	internal static void AddMetaXP(int amount)
	{
		if (UpgradeManager.instance != null)
		{
			UpgradeManager.instance.AddXP(amount);
		}
		else
		{
			PlayerPrefs.SetInt("XP", PlayerPrefs.GetInt("XP", 0) + amount);
			PlayerPrefs.Save();
		}
		SFXManager.instance?.ButtonClick();
	}

	internal static void DrawCardsNow(int count = 3)
	{
		if (CardManager.instance != null)
		{
			CardManager.instance.DrawCards(count);
		}
	}

	internal static int KillAllEnemies()
	{
		if (SpawnManager.instance == null || SpawnManager.instance.currentEnemies == null || EnemyDieMethod == null)
		{
			return 0;
		}
		List<Enemy> snapshot = new List<Enemy>(SpawnManager.instance.currentEnemies);
		int killed = 0;
		foreach (Enemy e in snapshot)
		{
			if (e != null)
			{
				EnemyDieMethod.Invoke(e, null);
				killed++;
			}
		}
		if (killed > 0)
		{
			SFXManager.instance?.ButtonClick();
		}
		return killed;
	}

	private static readonly AccessTools.FieldRef<Tower, int> LevelRef =
		AccessTools.FieldRefAccess<Tower, int>("<level>k__BackingField");
	private static readonly AccessTools.FieldRef<Tower, int> DamageUpgradeRef =
		AccessTools.FieldRefAccess<Tower, int>("damageUpgrade");
	private static readonly AccessTools.FieldRef<Tower, int> HealthDamageUpgradeRef =
		AccessTools.FieldRefAccess<Tower, int>("healthDamageUpgrade");
	private static readonly AccessTools.FieldRef<Tower, int> ArmorDamageUpgradeRef =
		AccessTools.FieldRefAccess<Tower, int>("armorDamageUpgrade");
	private static readonly AccessTools.FieldRef<Tower, int> ShieldDamageUpgradeRef =
		AccessTools.FieldRefAccess<Tower, int>("shieldDamageUpgrade");
	private static readonly AccessTools.FieldRef<Tower, float> HealthXPRef =
		AccessTools.FieldRefAccess<Tower, float>("<healthXP>k__BackingField");
	private static readonly AccessTools.FieldRef<Tower, float> ArmorXPRef =
		AccessTools.FieldRefAccess<Tower, float>("<armorXP>k__BackingField");
	private static readonly AccessTools.FieldRef<Tower, float> ShieldXPRef =
		AccessTools.FieldRefAccess<Tower, float>("<shieldXP>k__BackingField");

	private static int UpgradeTowerStatLevels(Tower tower, int statKind, int levels, bool freeGrant)
	{
		if (tower == null || levels <= 0) return 0;
		int mult = Mathf.Max(1, tower.upgradeCostMultiplier);
		int bought = 0;

		for (int i = 0; i < levels; i++)
		{
			int lvl = LevelRef(tower);
			float curXp = statKind switch
			{
				0 => HealthXPRef(tower),
				1 => ArmorXPRef(tower),
				_ => ShieldXPRef(tower)
			};

			int cost = (10 * lvl - (int)curXp) * mult;
			if (!freeGrant)
			{
				if (ResourceManager.instance == null || !ResourceManager.instance.CheckMoney(cost)) break;
				ResourceManager.instance.Spend(cost);
				DamageTracker.instance?.AddCost(tower.towerType, cost);
			}

			float xpSub = 10 * lvl - cost / mult;
			switch (statKind)
			{
				case 0:
					HealthXPRef(tower) -= xpSub;
					HealthDamageUpgradeRef(tower)++;
					break;
				case 1:
					ArmorXPRef(tower) -= xpSub;
					ArmorDamageUpgradeRef(tower)++;
					break;
				default:
					ShieldXPRef(tower) -= xpSub;
					ShieldDamageUpgradeRef(tower)++;
					break;
			}

			LevelRef(tower) = lvl + 1;
			DamageUpgradeRef(tower)++;
			bought++;
		}
		return bought;
	}

	internal static int UpgradeAllPlacedTowers(int levelsPerStat = 10)
	{
		Tower[] towers = UnityEngine.Object.FindObjectsOfType<Tower>();
		if (towers == null || towers.Length == 0)
		{
			return 0;
		}
		bool free = Plugin.InfiniteMoney.Value;
		int upgradedTowers = 0;
		foreach (Tower t in towers)
		{
			if (t == null)
			{
				continue;
			}
			int h = UpgradeTowerStatLevels(t, 0, levelsPerStat, free);
			int a = UpgradeTowerStatLevels(t, 1, levelsPerStat, free);
			int s = UpgradeTowerStatLevels(t, 2, levelsPerStat, free);
			if (h + a + s > 0)
			{
				t.SetStats();
				AchievementManager.instance?.TowerLevel(t.level);
				upgradedTowers++;
			}
		}
		if (upgradedTowers > 0)
		{
			SFXManager.instance?.ButtonClick();
		}
		TowerUI openUi = UnityEngine.Object.FindObjectOfType<TowerUI>();
		if (openUi != null)
		{
			Tower t = AccessTools.FieldRefAccess<TowerUI, Tower>("myTower")(openUi);
			if (t != null)
			{
				openUi.SetStats(t);
			}
		}
		return upgradedTowers;
	}

	internal static int OpenAllChestsCheat()
	{
		if (ModuleBridge.HasCleanQoL)
		{
			int res = ModuleBridge.OpenAllChests();
			if (res >= 0) return res;
		}
		if (CardManager.instance == null) return 0;
		TreasureChest[] chests = UnityEngine.Object.FindObjectsOfType<TreasureChest>();
		if (chests == null || chests.Length == 0) return 0;
		int cardDraw = 2 + PlayerPrefs.GetInt("TreasureDraw1", 0);
		int count = 0;
		foreach (TreasureChest chest in chests)
		{
			if (chest == null) continue;
			int drops = Mathf.Max(1, chest.numberOfDrops);
			for (int d = 0; d < drops; d++)
			{
				CardManager.instance.DrawCards(cardDraw);
			}
			UnityEngine.Object.Destroy(chest.gameObject);
			count++;
		}
		if (count > 0) SFXManager.instance?.ButtonClick();
		return count;
	}

	internal static int ChopAllTreesCheat()
	{
		if (ModuleBridge.HasCleanQoL)
		{
			int res = ModuleBridge.ChopAllTrees();
			if (res >= 0) return res;
		}
		TreeSpawn[] trees = UnityEngine.Object.FindObjectsOfType<TreeSpawn>();
		if (trees == null || trees.Length == 0) return 0;
		var spawnedRef = AccessTools.FieldRefAccess<SpawnableObject, bool>("spawned");
		int level = SpawnManager.instance != null ? SpawnManager.instance.level : 0;
		int goldPerTree = level * level;
		int count = 0;
		foreach (TreeSpawn tree in trees)
		{
			if (tree == null || !spawnedRef(tree)) continue;
			UnityEngine.Object.Destroy(tree.gameObject);
			count++;
		}
		if (count > 0)
		{
			int totalGold = count * goldPerTree;
			ResourceManager.instance?.AddMoney(totalGold);
			DamageTracker.instance?.AddIncome(DamageTracker.IncomeType.Tree, totalGold);
			SFXManager.instance?.ButtonClick();
		}
		return count;
	}

	internal static IReadOnlyList<CardCatalogEntry> GetAllEntries()
	{
		EnsureCatalogBuilt();
		return AllEntries;
	}

	internal static IReadOnlyList<CardCatalogEntry> GetUnlockEntries()
	{
		EnsureCatalogBuilt();
		return UnlockEntries;
	}

	internal static IReadOnlyList<TreeGroupSummary> GetTreeGroups()
	{
		EnsureCatalogBuilt();
		return TreeGroups;
	}

	internal static void EnsureCatalogBuilt()
	{
		if (_catalogBuilt || CardManager.instance == null)
		{
			return;
		}
		_catalogBuilt = true;
		EnsureCardDataLibReflected();

		List<UpgradeCard> startCards = StartCardsCheckRef(CardManager.instance);
		List<UpgradeCard> availNormal = AvailableCardsRef(CardManager.instance);
		List<UpgradeCard> availMonster = AvailableMonsterCardsRef(CardManager.instance);

		HashSet<UpgradeCard> monsterSet = new HashSet<UpgradeCard>();
		Queue<UpgradeCard> mQueue = new Queue<UpgradeCard>();
		if (availMonster != null)
		{
			foreach (UpgradeCard mc in availMonster)
			{
				if (mc != null && monsterSet.Add(mc))
				{
					mQueue.Enqueue(mc);
				}
			}
		}
		while (mQueue.Count > 0)
		{
			UpgradeCard cur = mQueue.Dequeue();
			if (cur.unlocks == null) continue;
			foreach (UpgradeCard child in cur.unlocks)
			{
				if (child != null && monsterSet.Add(child))
				{
					mQueue.Enqueue(child);
				}
			}
		}

		HashSet<UpgradeCard> discovered = new HashSet<UpgradeCard>();
		Queue<UpgradeCard> q = new Queue<UpgradeCard>();

		void Seed(IEnumerable<UpgradeCard> src)
		{
			if (src == null) return;
			foreach (UpgradeCard c in src)
			{
				if (c != null && discovered.Add(c))
				{
					q.Enqueue(c);
				}
			}
		}

		Seed(startCards);
		Seed(availNormal);
		Seed(availMonster);

		UpgradeCard[] sceneCards = Resources.FindObjectsOfTypeAll<UpgradeCard>();
		foreach (UpgradeCard c in sceneCards)
		{
			if (c != null && c.gameObject.scene.IsValid() && discovered.Add(c))
			{
				q.Enqueue(c);
			}
		}

		while (q.Count > 0)
		{
			UpgradeCard cur = q.Dequeue();
			if (cur.unlocks == null) continue;
			foreach (UpgradeCard child in cur.unlocks)
			{
				if (child != null && discovered.Add(child))
				{
					q.Enqueue(child);
				}
			}
		}

		// Build reverse-parent map so orphan cards can inherit tree grouping from parent if needed
		Dictionary<UpgradeCard, UpgradeCard> parentMap = new Dictionary<UpgradeCard, UpgradeCard>();
		foreach (UpgradeCard card in discovered)
		{
			if (card.unlocks == null) continue;
			foreach (UpgradeCard child in card.unlocks)
			{
				if (child != null && !parentMap.ContainsKey(child))
				{
					parentMap[child] = card;
				}
			}
		}

		foreach (UpgradeCard card in discovered)
		{
			if (card is MonsterUpgradeCard)
			{
				monsterSet.Add(card);
			}
		}

		foreach (UpgradeCard card in discovered)
		{
			CardCatalogEntry entry = BuildCatalogEntry(card, monsterSet.Contains(card), parentMap);
			EntryByCard[card] = entry;
			AllEntries.Add(entry);
			if (entry.IsTowerUnlock || entry.IsBuildingUnlock)
			{
				UnlockEntries.Add(entry);
			}
		}

		// Propagate tree key along unlocks for any generic UpgradeCard children (e.g. ManaBank -> Savings)
		bool changed;
		int passes = 0;
		do
		{
			changed = false;
			passes++;
			foreach (CardCatalogEntry e in AllEntries)
			{
				if (e.TreeKey == "Misc" && parentMap.TryGetValue(e.Card, out UpgradeCard parent) && EntryByCard.TryGetValue(parent, out var pe) && pe.TreeKey != "Misc")
				{
					e.TreeKey = pe.TreeKey;
					e.TreeLabel = pe.TreeLabel;
					e.Category = pe.Category;
					changed = true;
				}
			}
		}
		while (changed && passes < 6);

		AllEntries.Sort((a, b) =>
		{
			int c1 = a.Category.CompareTo(b.Category);
			if (c1 != 0) return c1;
			int c2 = string.Compare(a.TreeLabel, b.TreeLabel, StringComparison.OrdinalIgnoreCase);
			if (c2 != 0) return c2;
			if (a.IsTowerUnlock != b.IsTowerUnlock) return a.IsTowerUnlock ? -1 : 1;
			if (a.IsBuildingUnlock != b.IsBuildingUnlock) return a.IsBuildingUnlock ? -1 : 1;
			return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
		});

		UnlockEntries.Sort((a, b) =>
		{
			if (a.IsTowerUnlock != b.IsTowerUnlock) return a.IsTowerUnlock ? -1 : 1;
			return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
		});

		BuildTreeGroups(parentMap);
	}

	private static void BuildTreeGroups(Dictionary<UpgradeCard, UpgradeCard> parentMap)
	{
		foreach (CardCatalogEntry entry in AllEntries)
		{
			if (entry.IsMonster)
			{
				continue;
			}
			if (!TreeGroupByKey.TryGetValue(entry.TreeKey, out TreeGroupSummary group))
			{
				group = new TreeGroupSummary
				{
					TreeKey = entry.TreeKey,
					TreeLabel = entry.TreeLabel,
					Category = entry.Category
				};
				TreeGroupByKey[entry.TreeKey] = group;
				TreeGroups.Add(group);
			}
			group.Cards.Add(entry);
		}

		foreach (TreeGroupSummary group in TreeGroups)
		{
			HashSet<UpgradeCard> inGroup = new HashSet<UpgradeCard>();
			foreach (CardCatalogEntry ce in group.Cards)
			{
				inGroup.Add(ce.Card);
			}

			foreach (CardCatalogEntry ce in group.Cards)
			{
				if (ce.Card.unlocks != null && ce.Card.unlocks.Count > 0)
				{
					bool hasParentInGroup = parentMap.TryGetValue(ce.Card, out UpgradeCard p) && inGroup.Contains(p);
					bool parentIsTowerRoot = hasParentInGroup && p is TowerUnlockCard;
					if (!hasParentInGroup || parentIsTowerRoot)
					{
						group.BranchRoots.Add(ce);
					}
				}
			}
		}

		TreeGroups.Sort((a, b) =>
		{
			int c = a.Category.CompareTo(b.Category);
			return c != 0 ? c : string.Compare(a.TreeLabel, b.TreeLabel, StringComparison.OrdinalIgnoreCase);
		});
	}

	private static CardCatalogEntry BuildCatalogEntry(UpgradeCard card, bool isMonster, Dictionary<UpgradeCard, UpgradeCard> parentMap)
	{
		string unlockName = UnlockNameRef(card) ?? string.Empty;
		string title = string.IsNullOrEmpty(card.title) ? card.name : card.title;
		string desc = card.description ?? string.Empty;

		CardFilterCategory cat = CardFilterCategory.Buffs;
		string treeKey = "Misc";
		string treeLabel = "General Buffs";
		bool isTowerUnlock = false;
		bool isBuildingUnlock = false;

		if (isMonster || card is MonsterUpgradeCard)
		{
			cat = CardFilterCategory.Monsters;
			treeKey = "Monster";
			treeLabel = "Monster Cards";
			isMonster = true;
		}
		else if (card is TowerUnlockCard tuc)
		{
			bool isTower = IsTowerRef(tuc);
			isTowerUnlock = isTower;
			isBuildingUnlock = !isTower;
			cat = isTower ? CardFilterCategory.Towers : CardFilterCategory.Buildings;
			string clean = CleanUnlockTitle(title);
			treeKey = (isTower ? "Tower_" : "Building_") + clean.Replace(" ", "");
			treeLabel = clean;
		}
		else if (card is TowerUpgradeCard tUp)
		{
			TowerType tt = CardTowerTypeRef(tUp);
			cat = CardFilterCategory.Towers;
			treeKey = "Tower_" + GetFriendlyTowerKey(tt);
			treeLabel = GetFriendlyTowerLabel(tt);
		}
		else if (card is DOTUpgradeCard)
		{
			cat = CardFilterCategory.DOT;
			string lower = (unlockName + " " + title + " " + desc).ToLowerInvariant();
			if (lower.Contains("bleed"))
			{
				treeKey = "DOT_Bleed";
				treeLabel = "DOT: Bleed";
			}
			else if (lower.Contains("burn") || lower.Contains("pyro") || lower.Contains("immolat") || lower.Contains("cooker"))
			{
				treeKey = "DOT_Burn";
				treeLabel = "DOT: Burn";
			}
			else if (lower.Contains("poison") || lower.Contains("fume"))
			{
				treeKey = "DOT_Poison";
				treeLabel = "DOT: Poison";
			}
			else
			{
				treeKey = "DOT_General";
				treeLabel = "DOT: General";
			}
		}
		else if (card is HauntedHouseUpgrade)
		{
			cat = CardFilterCategory.Buildings;
			treeKey = "Building_HauntedHouse";
			treeLabel = "Haunted House";
		}
		else if (card is UniversityUpgrade)
		{
			cat = CardFilterCategory.Buildings;
			treeKey = "Building_University";
			treeLabel = "University";
		}
		else if (card is GoldRushCard)
		{
			cat = CardFilterCategory.Buildings;
			treeKey = "Building_Mine";
			treeLabel = "Mine";
		}
		else if (card is TreeUpgradeCard)
		{
			cat = CardFilterCategory.Buffs;
			treeKey = "Buff_Trees";
			treeLabel = "Buffs: Trees & Chests";
		}
		else if (card is ManaGenUpgradeCard)
		{
			cat = CardFilterCategory.Buffs;
			treeKey = "Buff_Mana";
			treeLabel = "Buffs: Mana";
		}
		else if (card is MaxHealthUpgradeCard)
		{
			cat = CardFilterCategory.Buffs;
			treeKey = "Buff_TowerHealth";
			treeLabel = "Buffs: Tower Health";
		}

		// Refine with CardDataLib if available
		if (TryQueryCardDataLib(unlockName, title, out string cdlCat, out string cdlSub))
		{
			if (cdlCat == "Monster")
			{
				cat = CardFilterCategory.Monsters;
				treeKey = "Monster";
				treeLabel = "Monster Cards";
				isMonster = true;
			}
			else if (cdlCat == "Tower")
			{
				cat = CardFilterCategory.Towers;
				treeKey = "Tower_" + cdlSub;
				treeLabel = InsertSpacesCamelCase(cdlSub);
			}
			else if (cdlCat == "Building")
			{
				cat = CardFilterCategory.Buildings;
				treeKey = "Building_" + cdlSub;
				treeLabel = InsertSpacesCamelCase(cdlSub);
			}
			else if (cdlCat == "DOT")
			{
				cat = CardFilterCategory.DOT;
				treeKey = "DOT_" + cdlSub;
				treeLabel = "DOT: " + cdlSub.Replace("DOT", "");
			}
			else if (cdlCat == "Buff")
			{
				cat = CardFilterCategory.Buffs;
				treeKey = "Buff_" + cdlSub;
				treeLabel = "Buffs: " + InsertSpacesCamelCase(cdlSub).Replace("Buff", "").Trim();
			}
		}

		return new CardCatalogEntry
		{
			Card = card,
			UnlockName = unlockName,
			Title = title,
			Description = desc,
			Category = cat,
			TreeKey = treeKey,
			TreeLabel = treeLabel,
			IsMonster = isMonster,
			IsTowerUnlock = isTowerUnlock,
			IsBuildingUnlock = isBuildingUnlock
		};
	}

	private static string CleanUnlockTitle(string title)
	{
		if (string.IsNullOrEmpty(title)) return "Unknown";
		if (title.StartsWith("Tower - ", StringComparison.OrdinalIgnoreCase)) return title.Substring(8).Trim();
		if (title.StartsWith("Building - ", StringComparison.OrdinalIgnoreCase)) return title.Substring(11).Trim();
		return title.Trim();
	}

	private static string GetFriendlyTowerKey(TowerType tt)
	{
		return tt switch
		{
			TowerType.Crossbow => "Ballista",
			TowerType.Morter => "Mortar",
			TowerType.Frost => "FrostKeep",
			_ => tt.ToString()
		};
	}

	private static string GetFriendlyTowerLabel(TowerType tt)
	{
		return tt switch
		{
			TowerType.Crossbow => "Ballista",
			TowerType.Morter => "Mortar",
			TowerType.Frost => "Frost Keep",
			TowerType.TeslaCoil => "Tesla Coil",
			TowerType.FlameThrower => "Flame Thrower",
			TowerType.PoisonSprayer => "Poison Sprayer",
			TowerType.VampireLair => "Vampire Lair",
			TowerType.ParticleCannon => "Particle Cannon",
			TowerType.Siphon => "Mana Siphon",
			_ => InsertSpacesCamelCase(tt.ToString())
		};
	}

	private static string InsertSpacesCamelCase(string s)
	{
		if (string.IsNullOrEmpty(s)) return string.Empty;
		System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length + 4);
		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];
			if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]))
			{
				sb.Append(' ');
			}
			sb.Append(c);
		}
		return sb.ToString();
	}

	private static void EnsureCardDataLibReflected()
	{
		if (_cardDataLibChecked)
		{
			return;
		}
		_cardDataLibChecked = true;
		Type api = AccessTools.TypeByName("CardDataLib.CardDataLibAPI");
		if (api != null)
		{
			_cdlGetCardMethod = api.GetMethod("GetCard", BindingFlags.Public | BindingFlags.Static);
		}
		Type cdd = AccessTools.TypeByName("CardsShared.CardDisplayData");
		if (cdd != null)
		{
			_cddCategoryProp = cdd.GetProperty("Category", BindingFlags.Public | BindingFlags.Instance);
			_cddSubcategoryProp = cdd.GetProperty("Subcategory", BindingFlags.Public | BindingFlags.Instance);
		}
	}

	private static bool TryQueryCardDataLib(string unlockName, string title, out string category, out string subcategory)
	{
		category = null;
		subcategory = null;
		if (_cdlGetCardMethod == null || _cddCategoryProp == null || _cddSubcategoryProp == null)
		{
			return false;
		}
		object cardObj = null;
		if (!string.IsNullOrEmpty(unlockName))
		{
			cardObj = _cdlGetCardMethod.Invoke(null, new object[] { unlockName, null });
		}
		if (cardObj == null && !string.IsNullOrEmpty(title))
		{
			cardObj = _cdlGetCardMethod.Invoke(null, new object[] { null, title });
		}
		if (cardObj == null)
		{
			return false;
		}
		category = _cddCategoryProp.GetValue(cardObj)?.ToString();
		subcategory = _cddSubcategoryProp.GetValue(cardObj)?.ToString();
		return !string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(subcategory);
	}

	internal static bool GiveCard(CardCatalogEntry entry)
	{
		if (entry == null || entry.Card == null || CardManager.instance == null)
		{
			return false;
		}
		if (IsCardOwned(entry))
		{
			return false;
		}

		UpgradeCard card = entry.Card;

		if (card is TowerUnlockCard tuc && TowerUnlockManager.instance != null)
		{
			GameObject uiObj = TowerUIObjectRef(tuc);
			bool isTower = IsTowerRef(tuc);
			List<GameObject> unlockedList = isTower
				? UnlockedTowersRef(TowerUnlockManager.instance)
				: UnlockedBuildingsRef(TowerUnlockManager.instance);

			if (uiObj != null && unlockedList != null && !unlockedList.Contains(uiObj))
			{
				tuc.Upgrade();
			}
		}
		else
		{
			card.Upgrade();
		}

		GrantedCardsThisRun.Add(card);

		List<UpgradeCard> pool = entry.IsMonster
			? AvailableMonsterCardsRef(CardManager.instance)
			: AvailableCardsRef(CardManager.instance);

		if (pool != null)
		{
			pool.RemoveAll(x => x == card);
			if (card.unlocks != null)
			{
				foreach (UpgradeCard child in card.unlocks)
				{
					if (child != null && child.unlocked && !GrantedCardsThisRun.Contains(child) && !pool.Contains(child))
					{
						pool.Add(child);
					}
				}
			}
		}

		ModuleBridge.NotifyUpgradesMenuCardUnlocked(entry.UnlockName, entry.Title, entry.IsMonster);
		return true;
	}

	internal static int GiveAllUnlocks(bool towers, bool buildings)
	{
		EnsureCatalogBuilt();
		int count = 0;
		foreach (CardCatalogEntry e in UnlockEntries)
		{
			if ((e.IsTowerUnlock && towers) || (e.IsBuildingUnlock && buildings))
			{
				if (GiveCard(e))
				{
					count++;
				}
			}
		}
		if (count > 0)
		{
			SFXManager.instance?.ButtonClick();
		}
		return count;
	}

	internal static int GiveCardTree(string treeKey, bool includeUnresearched)
	{
		EnsureCatalogBuilt();
		if (!TreeGroupByKey.TryGetValue(treeKey, out TreeGroupSummary group))
		{
			return 0;
		}

		// Order topologically from roots via unlocks BFS so unlocks apply cleanly
		HashSet<UpgradeCard> groupSet = new HashSet<UpgradeCard>();
		foreach (CardCatalogEntry e in group.Cards)
		{
			groupSet.Add(e.Card);
		}

		List<CardCatalogEntry> ordered = new List<CardCatalogEntry>(group.Cards.Count);
		HashSet<UpgradeCard> visited = new HashSet<UpgradeCard>();
		Queue<UpgradeCard> q = new Queue<UpgradeCard>();

		foreach (CardCatalogEntry e in group.Cards)
		{
			if (e.IsTowerUnlock || e.IsBuildingUnlock)
			{
				if (visited.Add(e.Card))
				{
					q.Enqueue(e.Card);
				}
			}
		}
		foreach (CardCatalogEntry e in group.Cards)
		{
			if (visited.Add(e.Card))
			{
				q.Enqueue(e.Card);
			}
		}

		while (q.Count > 0)
		{
			UpgradeCard cur = q.Dequeue();
			if (EntryByCard.TryGetValue(cur, out CardCatalogEntry ce))
			{
				ordered.Add(ce);
			}
			if (cur.unlocks == null) continue;
			foreach (UpgradeCard child in cur.unlocks)
			{
				if (child != null && groupSet.Contains(child) && visited.Add(child))
				{
					q.Enqueue(child);
				}
			}
		}

		int granted = 0;
		foreach (CardCatalogEntry ce in ordered)
		{
			if (!includeUnresearched && !ce.Card.unlocked)
			{
				continue;
			}
			if (GiveCard(ce))
			{
				granted++;
			}
		}
		if (granted > 0)
		{
			SFXManager.instance?.ButtonClick();
		}
		return granted;
	}

	internal static int GiveSubBranch(UpgradeCard rootCard, bool includeUnresearched)
	{
		if (rootCard == null)
		{
			return 0;
		}
		EnsureCatalogBuilt();
		Queue<UpgradeCard> q = new Queue<UpgradeCard>();
		HashSet<UpgradeCard> visited = new HashSet<UpgradeCard>();
		if (visited.Add(rootCard))
		{
			q.Enqueue(rootCard);
		}

		int granted = 0;
		while (q.Count > 0)
		{
			UpgradeCard cur = q.Dequeue();
			if (EntryByCard.TryGetValue(cur, out CardCatalogEntry ce))
			{
				if (includeUnresearched || cur.unlocked)
				{
					if (GiveCard(ce))
					{
						granted++;
					}
				}
			}
			if (cur.unlocks == null) continue;
			foreach (UpgradeCard child in cur.unlocks)
			{
				if (child != null && visited.Add(child))
				{
					q.Enqueue(child);
				}
			}
		}
		if (granted > 0)
		{
			SFXManager.instance?.ButtonClick();
		}
		return granted;
	}

	internal static int GiveAllTreesByCategory(CardFilterCategory category, bool includeUnresearched)
	{
		EnsureCatalogBuilt();
		int total = 0;
		foreach (TreeGroupSummary g in TreeGroups)
		{
			if (g.Category == category)
			{
				total += GiveCardTree(g.TreeKey, includeUnresearched);
			}
		}
		return total;
	}

	internal static int GiveAllNonMonsterTrees(bool includeUnresearched)
	{
		EnsureCatalogBuilt();
		int total = 0;
		foreach (TreeGroupSummary g in TreeGroups)
		{
			if (g.Category != CardFilterCategory.Monsters)
			{
				total += GiveCardTree(g.TreeKey, includeUnresearched);
			}
		}
		return total;
	}
}
