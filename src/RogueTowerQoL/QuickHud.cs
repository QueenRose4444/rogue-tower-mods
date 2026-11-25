using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RogueTowerQoL;

public enum ChestHandlingMode
{
	Off = 0,
	PopupButton = 1,
	AutoOpenImmediately = 2
}

internal static class QoLInputBlockerPatches
{
	[HarmonyPatch(typeof(UIManager), "Update")]
	[HarmonyPrefix]
	private static bool UIManager_Update_Prefix()
	{
		if (QuickHudManager.IsPointerOverQuickButton() || HotbarSortOverlayUI.IsPointerOverOverlay)
		{
			return false;
		}
		return true;
	}

	[HarmonyPatch(typeof(BuildingManager), "Update")]
	[HarmonyPrefix]
	private static bool BuildingManager_Update_Prefix()
	{
		if (HotbarSortOverlayUI.IsPointerOverOverlay)
		{
			return false;
		}
		return true;
	}

	[HarmonyPatch(typeof(CameraController), "UpdateZoom")]
	[HarmonyPrefix]
	private static bool CameraController_UpdateZoom_Prefix()
	{
		if (HotbarSortOverlayUI.IsPointerOverOverlay)
		{
			return false;
		}
		return true;
	}

	[HarmonyPatch(typeof(PauseMenu), "Update")]
	[HarmonyPrefix]
	private static bool PauseMenu_Update_Prefix()
	{
		if (HotbarSortOverlayUI.IsOpen && Input.GetKeyDown(KeyCode.Escape))
		{
			HotbarSortOverlayUI.SetOpen(false);
			return false;
		}
		return true;
	}
}

internal static class QuickHudPatches
{
	[HarmonyPatch(typeof(TileManager), "Start")]
	[HarmonyPostfix]
	private static void TileManager_Start_Postfix()
	{
		QuickHudManager.OnNewGameStart();
	}

	[HarmonyPatch(typeof(TreasureChest), "Start")]
	[HarmonyPostfix]
	private static void TreasureChest_Start_Postfix(TreasureChest __instance)
	{
		QuickHudManager.RegisterChest(__instance);
	}

	[HarmonyPatch(typeof(SpawnableObject), "Start")]
	[HarmonyPostfix]
	private static void SpawnableObject_Start_Postfix(SpawnableObject __instance)
	{
		if (__instance is TreeSpawn tree && QuickHudManager.IsSpawned(tree))
		{
			QuickHudManager.RegisterTree(tree);
		}
	}

	[HarmonyPatch(typeof(TileSpawnLocation), "Start")]
	[HarmonyPostfix]
	private static void TileSpawnLocation_Start_Postfix(TileSpawnLocation __instance)
	{
		if (__instance != null && QuickHudManager.CanEarlyExpandNow())
		{
			__instance.gameObject.SetActive(value: true);
		}
	}

	[HarmonyPatch(typeof(CardManager), nameof(CardManager.DrawCards), new[] { typeof(int) })]
	[HarmonyPrefix]
	private static void CardManager_DrawCards_Prefix()
	{
		QuickHudManager.SuppressEarlyExpandForCardDraw = true;
	}

	[HarmonyPatch(typeof(CardManager), nameof(CardManager.DrawCards), new[] { typeof(int) })]
	[HarmonyPostfix]
	private static void CardManager_DrawCards_Postfix()
	{
		QuickHudManager.SuppressEarlyExpandForCardDraw = false;
	}

	[HarmonyPatch(typeof(CardManager), nameof(CardManager.DrawMonsterCards))]
	[HarmonyPrefix]
	private static void CardManager_DrawMonsterCards_Prefix()
	{
		QuickHudManager.SuppressEarlyExpandForCardDraw = true;
	}

	[HarmonyPatch(typeof(CardManager), nameof(CardManager.DrawMonsterCards))]
	[HarmonyPostfix]
	private static void CardManager_DrawMonsterCards_Postfix()
	{
		QuickHudManager.SuppressEarlyExpandForCardDraw = false;
	}

	[HarmonyPatch(typeof(CardManager), nameof(CardManager.ActivateCard))]
	[HarmonyPostfix]
	private static void CardManager_ActivateCard_Postfix(CardManager __instance)
	{
		QuickHudManager.SuppressEarlyExpandForCardDraw = false;
		if (__instance != null && !__instance.drawingCards && SpawnManager.instance != null && QuickHudManager.CanEarlyExpandNow())
		{
			SpawnManager.instance.ShowSpawnUIs(state: true);
		}
	}

	[HarmonyPatch(typeof(CardManager), nameof(CardManager.ActivateMonsterCard))]
	[HarmonyPostfix]
	private static void CardManager_ActivateMonsterCard_Postfix(CardManager __instance)
	{
		QuickHudManager.SuppressEarlyExpandForCardDraw = false;
		if (__instance != null && !__instance.drawingCards && SpawnManager.instance != null && QuickHudManager.CanEarlyExpandNow())
		{
			SpawnManager.instance.ShowSpawnUIs(state: true);
		}
	}

	[HarmonyPatch(typeof(SpawnManager), nameof(SpawnManager.ShowSpawnUIs))]
	[HarmonyPrefix]
	private static void SpawnManager_ShowSpawnUIs_Prefix(ref bool state)
	{
		if (!state && !QuickHudManager.SuppressEarlyExpandForCardDraw && QuickHudManager.CanEarlyExpandNow())
		{
			state = true;
		}
	}

	[HarmonyPatch(typeof(TileSpawnLocation), nameof(TileSpawnLocation.SpawnTile))]
	[HarmonyPrefix]
	private static void TileSpawnLocation_SpawnTile_Prefix(out bool __state)
	{
		__state = Plugin.AllowEarlyExpand.Value &&
			SpawnManager.instance != null &&
			SpawnManager.instance.combat &&
			SpawnManager.instance.level > 0 &&
			GameManager.instance != null;

		if (__state)
		{
			QuickHudManager.AwardEarlyWaveCompletionBeforeNextWave();
		}
	}

	[HarmonyPatch(typeof(TileSpawnLocation), nameof(TileSpawnLocation.SpawnTile))]
	[HarmonyPostfix]
	private static void TileSpawnLocation_SpawnTile_Postfix(bool __state)
	{
		if (__state)
		{
			QuickHudManager.TriggerEarlyWaveCardDrawsAfterNextWave();
		}
	}

	[HarmonyPatch(typeof(SpawnManager), "Spawn")]
	[HarmonyPostfix]
	private static void SpawnManager_Spawn_Postfix(SpawnManager __instance, ref IEnumerator __result)
	{
		if (Plugin.AllowEarlyExpand.Value && __result != null)
		{
			__result = QuickHudManager.WrapSpawnCoroutine(__instance, __result);
		}
	}
}

internal static class QuickHudManager
{
	private static readonly AccessTools.FieldRef<SpawnableObject, bool> SpawnedRef =
		AccessTools.FieldRefAccess<SpawnableObject, bool>("spawned");

	private static readonly AccessTools.FieldRef<SpawnableObject, GameObject> UIObjectRef =
		AccessTools.FieldRefAccess<SpawnableObject, GameObject>("UIObject");

	private static readonly AccessTools.FieldRef<TreeUI, GameObject> GoldShowerRef =
		AccessTools.FieldRefAccess<TreeUI, GameObject>("goldShower");

	private static readonly AccessTools.FieldRef<TreeUI, GameObject> TreasureObjectRef =
		AccessTools.FieldRefAccess<TreeUI, GameObject>("treasureObject");

	private static readonly AccessTools.FieldRef<SpawnManager, bool> EnemiesLeftToSpawnRef =
		AccessTools.FieldRefAccess<SpawnManager, bool>("enemiesLeftToSpawn");

	private static readonly AccessTools.FieldRef<SpawnManager, int> CardDrawFrequencyRef =
		AccessTools.FieldRefAccess<SpawnManager, int>("cardDrawFrequency");

	private static readonly AccessTools.FieldRef<SpawnManager, Text> ScoreTextRef =
		AccessTools.FieldRefAccess<SpawnManager, Text>("scoreText");

	private static readonly List<TreasureChest> ActiveChests = new List<TreasureChest>();
	private static readonly List<TreeSpawn> ActiveTrees = new List<TreeSpawn>();

	internal static bool SuppressEarlyExpandForCardDraw;
	private static int _activeSpawnCoroutines;

	private static GameObject _openChestsButtonObj;
	private static Text _openChestsButtonText;
	private static RectTransform _openChestsRect;

	private static GameObject _chopTreesButtonObj;
	private static Text _chopTreesButtonText;
	private static RectTransform _chopTreesRect;

	internal static bool IsSpawned(TreeSpawn tree)
	{
		return tree != null && SpawnedRef(tree);
	}

	internal static bool CanEarlyExpandNow()
	{
		return Plugin.AllowEarlyExpand.Value &&
			SpawnManager.instance != null &&
			GameManager.instance != null &&
			!GameManager.instance.gameOver &&
			(CardManager.instance == null || !CardManager.instance.drawingCards) &&
			(SpawnManager.instance.endless || SpawnManager.instance.level < SpawnManager.instance.lastLevel);
	}

	internal static void AwardEarlyWaveCompletionBeforeNextWave()
	{
		if (SpawnManager.instance == null || GameManager.instance == null)
		{
			return;
		}
		int lvl = SpawnManager.instance.level;
		if (lvl <= 0)
		{
			return;
		}
		AchievementManager.instance?.BeatLevel(lvl);
		int xpGain = lvl * GameManager.instance.gameMode;
		PlayerPrefs.SetInt("XP", PlayerPrefs.GetInt("XP", 0) + xpGain);
		Text scoreText = ScoreTextRef(SpawnManager.instance);
		if (scoreText != null)
		{
			scoreText.text = "Score: " + GameManager.instance.NaturalSum(lvl) * GameManager.instance.gameMode;
		}
	}

	internal static void TriggerEarlyWaveCardDrawsAfterNextWave()
	{
		if (SpawnManager.instance == null || CardManager.instance == null)
		{
			return;
		}
		int prevLvl = SpawnManager.instance.level - 1;
		if (prevLvl <= 0)
		{
			return;
		}
		int cardFreq = Mathf.Max(1, CardDrawFrequencyRef(SpawnManager.instance));
		if (prevLvl % cardFreq == 0)
		{
			CardManager.instance.DrawCards();
			if (SpawnManager.instance.monsterMode)
			{
				CardManager.instance.DrawCards();
			}
		}
		if (SpawnManager.instance.monsterMode)
		{
			CardManager.instance.DrawMonsterCards();
		}
	}

	internal static IEnumerator WrapSpawnCoroutine(SpawnManager sm, IEnumerator inner)
	{
		_activeSpawnCoroutines++;
		if (sm != null)
		{
			EnemiesLeftToSpawnRef(sm) = true;
		}
		try
		{
			while (inner.MoveNext())
			{
				if (sm != null)
				{
					EnemiesLeftToSpawnRef(sm) = true;
				}
				yield return inner.Current;
			}
		}
		finally
		{
			_activeSpawnCoroutines = Mathf.Max(0, _activeSpawnCoroutines - 1);
			if (sm != null)
			{
				EnemiesLeftToSpawnRef(sm) = _activeSpawnCoroutines > 0;
			}
		}
	}

	internal static void OnSceneChanged()
	{
		ActiveChests.Clear();
		ActiveTrees.Clear();
		SuppressEarlyExpandForCardDraw = false;
		_activeSpawnCoroutines = 0;
		_openChestsButtonObj = null;
		_openChestsButtonText = null;
		_openChestsRect = null;
		_chopTreesButtonObj = null;
		_chopTreesButtonText = null;
		_chopTreesRect = null;
	}

	internal static void OnNewGameStart()
	{
		OnSceneChanged();
		EnsureHudButtonsCreated();
	}

	internal static void RegisterChest(TreasureChest chest)
	{
		if (chest == null)
		{
			return;
		}
		if (!ActiveChests.Contains(chest))
		{
			ActiveChests.Add(chest);
		}
		if (Plugin.ChestMode.Value == ChestHandlingMode.AutoOpenImmediately)
		{
			OpenAllChests();
		}
	}

	internal static void RegisterTree(TreeSpawn tree)
	{
		if (tree != null && !ActiveTrees.Contains(tree))
		{
			ActiveTrees.Add(tree);
		}
	}

	internal static void Tick()
	{
		for (int i = ActiveChests.Count - 1; i >= 0; i--)
		{
			if (ActiveChests[i] == null)
			{
				ActiveChests.RemoveAt(i);
			}
		}
		for (int i = ActiveTrees.Count - 1; i >= 0; i--)
		{
			if (ActiveTrees[i] == null)
			{
				ActiveTrees.RemoveAt(i);
			}
		}

		if (GameManager.instance == null)
		{
			return;
		}

		if (Plugin.ChestMode.Value == ChestHandlingMode.AutoOpenImmediately && ActiveChests.Count > 0)
		{
			OpenAllChests();
		}

		EnsureHudButtonsCreated();

		bool showChests = _openChestsButtonObj != null &&
			Plugin.ChestMode.Value == ChestHandlingMode.PopupButton &&
			ActiveChests.Count > 0 &&
			!GameManager.instance.gameOver;

		bool showTrees = _chopTreesButtonObj != null &&
			Plugin.ShowChopAllTreesButton.Value &&
			ActiveTrees.Count > 0 &&
			!GameManager.instance.gameOver;

		float nextY = -86f;

		if (_openChestsButtonObj != null)
		{
			if (_openChestsButtonObj.activeSelf != showChests)
			{
				_openChestsButtonObj.SetActive(showChests);
			}
			if (showChests)
			{
				if (_openChestsRect != null)
				{
					_openChestsRect.anchoredPosition = new Vector2(-430f, nextY);
				}
				nextY -= 46f;
				if (_openChestsButtonText != null)
				{
					int chestCount = ActiveChests.Count;
					int totalDrops = 0;
					for (int i = 0; i < chestCount; i++)
					{
						totalDrops += Mathf.Max(1, ActiveChests[i].numberOfDrops);
					}
					string label = chestCount == 1
						? (totalDrops > 1 ? $"Open Chest ({totalDrops})" : "Open Chest")
						: $"Open Chests ({chestCount})";
					if (_openChestsButtonText.text != label)
					{
						_openChestsButtonText.text = label;
					}
				}
			}
		}

		if (_chopTreesButtonObj != null)
		{
			if (_chopTreesButtonObj.activeSelf != showTrees)
			{
				_chopTreesButtonObj.SetActive(showTrees);
			}
			if (showTrees)
			{
				if (_chopTreesRect != null)
				{
					_chopTreesRect.anchoredPosition = new Vector2(-430f, nextY);
				}
				if (_chopTreesButtonText != null)
				{
					int treeCount = ActiveTrees.Count;
					int level = SpawnManager.instance != null ? SpawnManager.instance.level : 0;
					int goldPerTree = level * level;
					int totalGold = goldPerTree * treeCount;
					int chestPct = Mathf.FloorToInt(Mathf.Min(level * GameManager.instance.treeCardChanceMultiplier, 100));

					string treeLabel = $"Chop Trees ({treeCount}): +{totalGold}g\n{goldPerTree}g/ea  •  {chestPct}% chest";
					if (_chopTreesButtonText.text != treeLabel)
					{
						_chopTreesButtonText.text = treeLabel;
					}
				}
			}
		}
	}

	internal static int OpenAllChests()
	{
		if (ActiveChests.Count == 0 || CardManager.instance == null)
		{
			return 0;
		}

		int cardDraw = 2 + PlayerPrefs.GetInt("TreasureDraw1", 0);
		Vector3 soundPos = Vector3.zero;
		int openedCount = 0;

		TreasureUI openUi = UnityEngine.Object.FindObjectOfType<TreasureUI>();
		if (openUi != null && UIManager.instance != null)
		{
			UIManager.instance.CloseUI(openUi.gameObject);
		}

		for (int i = ActiveChests.Count - 1; i >= 0; i--)
		{
			TreasureChest chest = ActiveChests[i];
			ActiveChests.RemoveAt(i);
			if (chest == null)
			{
				continue;
			}

			soundPos = chest.transform.position;
			int drops = Mathf.Max(1, chest.numberOfDrops);
			for (int d = 0; d < drops; d++)
			{
				CardManager.instance.DrawCards(cardDraw);
			}

			UnityEngine.Object.Destroy(chest.gameObject);
			openedCount++;
		}

		if (openedCount > 0 && SFXManager.instance != null)
		{
			SFXManager.instance.ButtonClick();
			SFXManager.instance.PlaySound(Sound.CritBig, soundPos);
		}

		if (_openChestsButtonObj != null)
		{
			_openChestsButtonObj.SetActive(false);
		}
		return openedCount;
	}

	internal static int ChopAllTrees()
	{
		if (ActiveTrees.Count == 0)
		{
			return 0;
		}

		TreeUI openUi = UnityEngine.Object.FindObjectOfType<TreeUI>();
		if (openUi != null && UIManager.instance != null)
		{
			UIManager.instance.CloseUI(openUi.gameObject);
		}

		int level = SpawnManager.instance != null ? SpawnManager.instance.level : 0;
		int goldPerTree = level * level;
		int chestChance = level * (GameManager.instance != null ? GameManager.instance.treeCardChanceMultiplier : 0);

		Vector3 soundPos = Vector3.zero;
		int choppedCount = 0;
		int showerCount = 0;

		for (int i = ActiveTrees.Count - 1; i >= 0; i--)
		{
			TreeSpawn tree = ActiveTrees[i];
			ActiveTrees.RemoveAt(i);
			if (tree == null)
			{
				continue;
			}

			Vector3 treePos = tree.transform.position;
			soundPos = treePos;

			GameObject uiPrefab = UIObjectRef(tree);
			TreeUI treeUi = uiPrefab != null ? uiPrefab.GetComponent<TreeUI>() : null;
			if (treeUi != null)
			{
				if (showerCount < 5)
				{
					GameObject goldShower = GoldShowerRef(treeUi);
					if (goldShower != null)
					{
						UnityEngine.Object.Instantiate(goldShower, treePos, Quaternion.identity);
						showerCount++;
					}
				}

				if (chestChance > 0 && UnityEngine.Random.Range(1, 100) <= chestChance)
				{
					GameObject treasureObject = TreasureObjectRef(treeUi);
					if (treasureObject != null)
					{
						UnityEngine.Object.Instantiate(treasureObject, treePos, Quaternion.identity);
					}
				}
			}

			UnityEngine.Object.Destroy(tree.gameObject);
			choppedCount++;
		}

		if (choppedCount > 0)
		{
			int totalGold = choppedCount * goldPerTree;
			if (ResourceManager.instance != null)
			{
				ResourceManager.instance.AddMoney(totalGold);
			}
			if (DamageTracker.instance != null)
			{
				DamageTracker.instance.AddIncome(DamageTracker.IncomeType.Tree, totalGold);
			}
			if (SFXManager.instance != null)
			{
				SFXManager.instance.ButtonClick();
				SFXManager.instance.PlaySound(Sound.CoinLong, soundPos);
			}
		}

		if (_chopTreesButtonObj != null)
		{
			_chopTreesButtonObj.SetActive(false);
		}
		return choppedCount;
	}

	internal static bool IsPointerOverQuickButton()
	{
		if (_openChestsButtonObj != null && _openChestsButtonObj.activeInHierarchy && _openChestsRect != null)
		{
			if (RectTransformUtility.RectangleContainsScreenPoint(_openChestsRect, Input.mousePosition, null))
			{
				return true;
			}
		}
		if (_chopTreesButtonObj != null && _chopTreesButtonObj.activeInHierarchy && _chopTreesRect != null)
		{
			if (RectTransformUtility.RectangleContainsScreenPoint(_chopTreesRect, Input.mousePosition, null))
			{
				return true;
			}
		}
		return false;
	}

	private static void EnsureHudButtonsCreated()
	{
		if (_openChestsButtonObj != null && _chopTreesButtonObj != null)
		{
			return;
		}
		if (GameManager.instance == null)
		{
			return;
		}

		Text healthText = AccessTools.FieldRefAccess<GameManager, Text>("healthText")(GameManager.instance);
		if (healthText == null || healthText.transform.parent == null || healthText.transform.parent.parent == null)
		{
			return;
		}

		Transform healthPanel = healthText.transform.parent.parent;
		Transform topSide = healthPanel.parent;
		if (topSide == null)
		{
			return;
		}

		Button templateBtn = FindTemplateButton(topSide.root);
		if (templateBtn == null)
		{
			return;
		}

		if (_openChestsButtonObj == null)
		{
			_openChestsButtonObj = UnityEngine.Object.Instantiate(templateBtn.gameObject, topSide);
			_openChestsButtonObj.name = "QuickOpenChestsButton";

			_openChestsRect = _openChestsButtonObj.GetComponent<RectTransform>();
			_openChestsRect.anchorMin = new Vector2(0.5f, 1.0f);
			_openChestsRect.anchorMax = new Vector2(0.5f, 1.0f);
			_openChestsRect.pivot = new Vector2(0.5f, 0.5f);
			_openChestsRect.anchoredPosition = new Vector2(-430f, -86f);
			_openChestsRect.sizeDelta = new Vector2(165f, 38f);
			_openChestsRect.localScale = Vector3.one;

			Button chestBtn = _openChestsButtonObj.GetComponent<Button>();
			chestBtn.onClick = new Button.ButtonClickedEvent();
			chestBtn.onClick.AddListener(delegate { OpenAllChests(); });

			_openChestsButtonText = _openChestsButtonObj.GetComponentInChildren<Text>(includeInactive: true);
			if (_openChestsButtonText != null)
			{
				_openChestsButtonText.text = "Open Chest";
				_openChestsButtonText.resizeTextForBestFit = false;
				_openChestsButtonText.fontSize = 135;
				_openChestsButtonText.alignment = TextAnchor.MiddleCenter;
				_openChestsButtonText.horizontalOverflow = HorizontalWrapMode.Overflow;
				_openChestsButtonText.verticalOverflow = VerticalWrapMode.Overflow;
			}

			_openChestsButtonObj.SetActive(false);
		}

		if (_chopTreesButtonObj == null)
		{
			_chopTreesButtonObj = UnityEngine.Object.Instantiate(templateBtn.gameObject, topSide);
			_chopTreesButtonObj.name = "QuickChopTreesButton";

			_chopTreesRect = _chopTreesButtonObj.GetComponent<RectTransform>();
			_chopTreesRect.anchorMin = new Vector2(0.5f, 1.0f);
			_chopTreesRect.anchorMax = new Vector2(0.5f, 1.0f);
			_chopTreesRect.pivot = new Vector2(0.5f, 0.5f);
			_chopTreesRect.anchoredPosition = new Vector2(-430f, -132f);
			_chopTreesRect.sizeDelta = new Vector2(175f, 44f);
			_chopTreesRect.localScale = Vector3.one;

			Button treeBtn = _chopTreesButtonObj.GetComponent<Button>();
			treeBtn.onClick = new Button.ButtonClickedEvent();
			treeBtn.onClick.AddListener(delegate { ChopAllTrees(); });

			_chopTreesButtonText = _chopTreesButtonObj.GetComponentInChildren<Text>(includeInactive: true);
			if (_chopTreesButtonText != null)
			{
				_chopTreesButtonText.text = "Chop Trees";
				_chopTreesButtonText.resizeTextForBestFit = false;
				_chopTreesButtonText.fontSize = 115;
				_chopTreesButtonText.alignment = TextAnchor.MiddleCenter;
				_chopTreesButtonText.horizontalOverflow = HorizontalWrapMode.Overflow;
				_chopTreesButtonText.verticalOverflow = VerticalWrapMode.Overflow;
				_chopTreesButtonText.lineSpacing = 0.9f;
			}

			_chopTreesButtonObj.SetActive(false);
		}
	}

	private static Button FindTemplateButton(Transform root)
	{
		if (GameManager.instance != null)
		{
			GameObject endlessScreen = AccessTools.FieldRefAccess<GameManager, GameObject>("endlessEndScreen")(GameManager.instance);
			if (endlessScreen != null)
			{
				Button b = endlessScreen.GetComponentInChildren<Button>(includeInactive: true);
				if (b != null)
				{
					return b;
				}
			}
		}
		return root != null ? root.GetComponentInChildren<Button>(includeInactive: true) : null;
	}
}
