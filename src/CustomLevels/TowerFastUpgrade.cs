using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RogueTowerCustomLevels;

internal enum TowerStatKind
{
	Health = 0,
	Armor = 1,
	Shield = 2
}

internal static class TowerFastUpgradePatches
{
	private static readonly AccessTools.FieldRef<TowerUI, Text> HealthXPTextRef =
		AccessTools.FieldRefAccess<TowerUI, Text>("healthXPText");

	private static readonly AccessTools.FieldRef<TowerUI, Tower> MyTowerRef =
		AccessTools.FieldRefAccess<TowerUI, Tower>("myTower");

	[HarmonyPatch(typeof(TowerUI), "Start")]
	[HarmonyPostfix]
	private static void TowerUI_Start_Postfix(TowerUI __instance)
	{
		if (!Plugin.EnableTowerFastUpgrade.Value || __instance == null || !__instance.enabled)
		{
			return;
		}
		// Skip QoLChanges hover-only range circles where UI child panels are disabled
		Text hText = HealthXPTextRef(__instance);
		if (hText == null || !hText.gameObject.activeInHierarchy)
		{
			return;
		}
		TowerFastUpgradeController ctrl = __instance.gameObject.GetComponent<TowerFastUpgradeController>();
		if (ctrl == null)
		{
			ctrl = __instance.gameObject.AddComponent<TowerFastUpgradeController>();
		}
		ctrl.Initialize(__instance);
	}

	[HarmonyPatch(typeof(TowerUI), "SetStats")]
	[HarmonyPostfix]
	private static void TowerUI_SetStats_Postfix(TowerUI __instance)
	{
		if (__instance == null)
		{
			return;
		}
		TowerFastUpgradeController ctrl = __instance.GetComponent<TowerFastUpgradeController>();
		if (ctrl != null)
		{
			ctrl.RefreshLabels();
		}
	}

	[HarmonyPatch(typeof(TowerUI), "Update")]
	[HarmonyPrefix]
	private static bool TowerUI_Update_Prefix(TowerUI __instance)
	{
		if (ModMenuUI.IsSearchFocused)
		{
			return false;
		}
		if (Plugin.EnableTowerFastUpgrade.Value && __instance != null)
		{
			TowerFastUpgradeController ctrl = __instance.GetComponent<TowerFastUpgradeController>();
			ctrl?.TickKeyboardHold();
		}
		return true;
	}

	[HarmonyPatch(typeof(TowerUI), nameof(TowerUI.BuyHealthLevel))]
	[HarmonyPrefix]
	private static bool TowerUI_BuyHealthLevel_Prefix(TowerUI __instance)
	{
		if (Plugin.EnableTowerFastUpgrade.Value && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
		{
			Tower t = MyTowerRef(__instance);
			if (t != null)
			{
				TowerUpgradeHelper.BuySnap10(t, TowerStatKind.Health, __instance);
				return false;
			}
		}
		return true;
	}

	[HarmonyPatch(typeof(TowerUI), nameof(TowerUI.BuyArmorLevel))]
	[HarmonyPrefix]
	private static bool TowerUI_BuyArmorLevel_Prefix(TowerUI __instance)
	{
		if (Plugin.EnableTowerFastUpgrade.Value && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
		{
			Tower t = MyTowerRef(__instance);
			if (t != null)
			{
				TowerUpgradeHelper.BuySnap10(t, TowerStatKind.Armor, __instance);
				return false;
			}
		}
		return true;
	}

	[HarmonyPatch(typeof(TowerUI), nameof(TowerUI.BuyShieldLevel))]
	[HarmonyPrefix]
	private static bool TowerUI_BuyShieldLevel_Prefix(TowerUI __instance)
	{
		if (Plugin.EnableTowerFastUpgrade.Value && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
		{
			Tower t = MyTowerRef(__instance);
			if (t != null)
			{
				TowerUpgradeHelper.BuySnap10(t, TowerStatKind.Shield, __instance);
				return false;
			}
		}
		return true;
	}
}

internal static class TowerUpgradeHelper
{
	private static readonly AccessTools.FieldRef<Tower, int> LevelRef =
		AccessTools.FieldRefAccess<Tower, int>("<level>k__BackingField");
	private static readonly AccessTools.FieldRef<Tower, float> HealthXPRef =
		AccessTools.FieldRefAccess<Tower, float>("<healthXP>k__BackingField");
	private static readonly AccessTools.FieldRef<Tower, float> ArmorXPRef =
		AccessTools.FieldRefAccess<Tower, float>("<armorXP>k__BackingField");
	private static readonly AccessTools.FieldRef<Tower, float> ShieldXPRef =
		AccessTools.FieldRefAccess<Tower, float>("<shieldXP>k__BackingField");
	private static readonly AccessTools.FieldRef<Tower, int> DamageUpgradeRef =
		AccessTools.FieldRefAccess<Tower, int>("damageUpgrade");
	private static readonly AccessTools.FieldRef<Tower, int> HealthDamageUpgradeRef =
		AccessTools.FieldRefAccess<Tower, int>("healthDamageUpgrade");
	private static readonly AccessTools.FieldRef<Tower, int> ArmorDamageUpgradeRef =
		AccessTools.FieldRefAccess<Tower, int>("armorDamageUpgrade");
	private static readonly AccessTools.FieldRef<Tower, int> ShieldDamageUpgradeRef =
		AccessTools.FieldRefAccess<Tower, int>("shieldDamageUpgrade");

	internal static int GetNextMultipleOf10Delta(int currentMultiplier)
	{
		if (currentMultiplier < 0)
		{
			return 10;
		}
		int rem = currentMultiplier % 10;
		return rem == 0 ? 10 : (10 - rem);
	}

	internal static int GetStatMultiplier(Tower tower, TowerStatKind kind)
	{
		if (tower == null)
		{
			return 0;
		}
		return kind switch
		{
			TowerStatKind.Health => tower.healthDamage,
			TowerStatKind.Armor => tower.armorDamage,
			TowerStatKind.Shield => tower.shieldDamage,
			_ => 0
		};
	}

	internal static int GetNextSingleCost(Tower tower, TowerStatKind kind)
	{
		if (tower == null)
		{
			return int.MaxValue;
		}
		float xp = kind switch
		{
			TowerStatKind.Health => tower.healthXP,
			TowerStatKind.Armor => tower.armorXP,
			TowerStatKind.Shield => tower.shieldXP,
			_ => 0f
		};
		return (10 * tower.level - (int)xp) * Mathf.Max(1, tower.upgradeCostMultiplier);
	}

	internal static bool CanAffordSingle(Tower tower, TowerStatKind kind)
	{
		if (tower == null || ResourceManager.instance == null)
		{
			return false;
		}
		if (Plugin.InfiniteMoney.Value)
		{
			return true;
		}
		return ResourceManager.instance.CheckMoney(GetNextSingleCost(tower, kind));
	}

	internal static int BuySnap10(Tower tower, TowerStatKind kind, TowerUI towerUI = null, bool freeGrant = false)
	{
		if (tower == null)
		{
			return 0;
		}
		int delta = GetNextMultipleOf10Delta(GetStatMultiplier(tower, kind));
		return BuyLevels(tower, kind, delta, towerUI, freeGrant);
	}

	internal static int BuyLevels(Tower tower, TowerStatKind kind, int maxCount, TowerUI towerUI = null, bool freeGrant = false)
	{
		if (tower == null || maxCount <= 0)
		{
			return 0;
		}

		int mult = Mathf.Max(1, tower.upgradeCostMultiplier);
		int bought = 0;

		for (int i = 0; i < maxCount; i++)
		{
			int lvl = LevelRef(tower);
			float currentXP = kind switch
			{
				TowerStatKind.Health => HealthXPRef(tower),
				TowerStatKind.Armor => ArmorXPRef(tower),
				_ => ShieldXPRef(tower)
			};

			int cost = (10 * lvl - (int)currentXP) * mult;
			if (!freeGrant)
			{
				if (ResourceManager.instance == null || !ResourceManager.instance.CheckMoney(cost))
				{
					break;
				}
				ResourceManager.instance.Spend(cost);
				DamageTracker.instance?.AddCost(tower.towerType, cost);
			}

			float xpSubtract = 10 * lvl - cost / mult;
			switch (kind)
			{
				case TowerStatKind.Health:
					HealthXPRef(tower) -= xpSubtract;
					HealthDamageUpgradeRef(tower)++;
					break;
				case TowerStatKind.Armor:
					ArmorXPRef(tower) -= xpSubtract;
					ArmorDamageUpgradeRef(tower)++;
					break;
				case TowerStatKind.Shield:
					ShieldXPRef(tower) -= xpSubtract;
					ShieldDamageUpgradeRef(tower)++;
					break;
			}

			LevelRef(tower) = lvl + 1;
			DamageUpgradeRef(tower)++;
			bought++;
		}

		if (bought > 0)
		{
			tower.SetStats();
			SFXManager.instance?.ButtonClick();
			SpawnBatchLevelUpFX(tower, bought);
			AchievementManager.instance?.TowerLevel(tower.level);
			if (towerUI != null)
			{
				towerUI.SetStats(tower);
			}
		}
		else if (!freeGrant)
		{
			SFXManager.instance?.ButtonClick();
		}

		return bought;
	}

	private static void SpawnBatchLevelUpFX(Tower tower, int bought)
	{
		if (BuildingManager.instance != null && BuildingManager.instance.levelUpFX != null)
		{
			Object.Instantiate(BuildingManager.instance.levelUpFX, tower.transform.position, Quaternion.identity);
		}
		if (ObjectPool.instance != null)
		{
			GameObject dnObj = ObjectPool.instance.SpawnObject(ObjectPool.ObjectType.DamageNumber, tower.transform.position, Quaternion.identity);
			DamageNumber dn = dnObj != null ? dnObj.GetComponent<DamageNumber>() : null;
			if (dn != null)
			{
				dn.SetText(bought > 1 ? $"LEVEL UP! (+{bought})" : "LEVEL UP!", "Grey", 1f);
				dn.SetHoldTime(1f);
			}
		}
	}
}

internal sealed class TowerFastUpgradeController : MonoBehaviour
{
	private static readonly AccessTools.FieldRef<TowerUI, Tower> MyTowerRef =
		AccessTools.FieldRefAccess<TowerUI, Tower>("myTower");
	private static readonly AccessTools.FieldRef<TowerUI, Text> HealthXPTextRef =
		AccessTools.FieldRefAccess<TowerUI, Text>("healthXPText");

	private TowerUI _towerUI;
	private Button _healthBtn;
	private Button _armorBtn;
	private Button _shieldBtn;

	private Button _snapHealthBtn;
	private Text _snapHealthText;
	private Button _snapArmorBtn;
	private Text _snapArmorText;
	private Button _snapShieldBtn;
	private Text _snapShieldText;

	private bool _initialized;

	// Keyboard hold states for Alpha1/2/3
	private readonly float[] _keyHoldElapsed = new float[3];
	private readonly float[] _keyRepeatTimer = new float[3];

	internal void Initialize(TowerUI ui)
	{
		if (_initialized)
		{
			RefreshLabels();
			return;
		}
		_towerUI = ui;
		_initialized = true;

		Button[] buttons = ui.GetComponentsInChildren<Button>(includeInactive: true);
		foreach (Button b in buttons)
		{
			if (b == null)
			{
				continue;
			}
			int count = b.onClick.GetPersistentEventCount();
			for (int i = 0; i < count; i++)
			{
				string method = b.onClick.GetPersistentMethodName(i);
				if (method == "BuyHealthLevel")
				{
					_healthBtn = b;
				}
				else if (method == "BuyArmorLevel")
				{
					_armorBtn = b;
				}
				else if (method == "BuyShieldLevel")
				{
					_shieldBtn = b;
				}
			}
		}

		if (_healthBtn == null || _armorBtn == null || _shieldBtn == null)
		{
			Plugin.Log.LogWarning("TowerFastUpgrade: could not locate all 3 upgrade buttons on TowerUI.");
			return;
		}

		AttachHoldHandler(_healthBtn, TowerStatKind.Health, isSnap10: false);
		AttachHoldHandler(_armorBtn, TowerStatKind.Armor, isSnap10: false);
		AttachHoldHandler(_shieldBtn, TowerStatKind.Shield, isSnap10: false);

		BuildSnap10Panel();
		RefreshLabels();
	}

	private void AttachHoldHandler(Button btn, TowerStatKind kind, bool isSnap10)
	{
		btn.onClick = new Button.ButtonClickedEvent();
		HoldUpgradeButton handler = btn.gameObject.GetComponent<HoldUpgradeButton>();
		if (handler == null)
		{
			handler = btn.gameObject.AddComponent<HoldUpgradeButton>();
		}
		handler.Configure(this, _towerUI, kind, isSnap10);
	}

	private void BuildSnap10Panel()
	{
		RectTransform upgradePanelRt = FindUpgradePanelRect(_healthBtn.transform);
		if (upgradePanelRt == null || upgradePanelRt.parent == null)
		{
			return;
		}

		Transform canvasParent = upgradePanelRt.parent;
		if (canvasParent.Find("QuickSnap10Panel") != null)
		{
			return;
		}

		GameObject panelObj = new GameObject("QuickSnap10Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		panelObj.transform.SetParent(canvasParent, worldPositionStays: false);

		Image srcImg = upgradePanelRt.GetComponent<Image>();
		Image dstImg = panelObj.GetComponent<Image>();
		if (srcImg != null)
		{
			dstImg.sprite = srcImg.sprite;
			dstImg.type = srcImg.type;
			dstImg.color = srcImg.color;
			dstImg.material = srcImg.material;
		}
		else
		{
			dstImg.color = new Color(0.86f, 0.78f, 0.58f, 0.96f);
		}
		dstImg.raycastTarget = true;

		RectTransform snapRt = panelObj.GetComponent<RectTransform>();
		snapRt.anchorMin = upgradePanelRt.anchorMin;
		snapRt.anchorMax = upgradePanelRt.anchorMax;
		snapRt.pivot = new Vector2(0f, 0.5f);
		snapRt.localScale = upgradePanelRt.localScale;
		snapRt.localRotation = upgradePanelRt.localRotation;

		float rightEdgeX = upgradePanelRt.anchoredPosition.x + upgradePanelRt.sizeDelta.x * (1f - upgradePanelRt.pivot.x);
		float centerY = upgradePanelRt.anchoredPosition.y + upgradePanelRt.sizeDelta.y * (0.5f - upgradePanelRt.pivot.y);
		float panelWidth = 76f;
		snapRt.sizeDelta = new Vector2(panelWidth, upgradePanelRt.sizeDelta.y);
		snapRt.anchoredPosition = new Vector2(rightEdgeX + 8f, centerY);

		// Add "+10" header aligned with the vanilla "Cost" header
		Text costSample = HealthXPTextRef(_towerUI);
		Text headerSample = FindHeaderText(upgradePanelRt, "Cost") ?? FindHeaderText(upgradePanelRt, "Upgrade");
		GameObject hdrObj = new GameObject("Snap10Header", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
		hdrObj.transform.SetParent(snapRt, worldPositionStays: false);
		Text hdrText = hdrObj.GetComponent<Text>();
		hdrText.text = "+10";
		hdrText.alignment = TextAnchor.MiddleCenter;
		hdrText.horizontalOverflow = HorizontalWrapMode.Overflow;
		hdrText.verticalOverflow = VerticalWrapMode.Overflow;
		if (headerSample != null)
		{
			hdrText.font = headerSample.font;
			hdrText.fontSize = headerSample.fontSize;
			hdrText.color = headerSample.color;
			hdrText.fontStyle = headerSample.fontStyle;
		}
		else if (costSample != null)
		{
			hdrText.font = costSample.font;
			hdrText.fontSize = costSample.fontSize;
			hdrText.color = costSample.color;
		}
		RectTransform hdrRt = hdrObj.GetComponent<RectTransform>();
		hdrRt.anchorMin = new Vector2(0f, 0.5f);
		hdrRt.anchorMax = new Vector2(0f, 0.5f);
		hdrRt.pivot = new Vector2(0.5f, 0.5f);
		hdrRt.localScale = headerSample != null ? headerSample.rectTransform.localScale : new Vector3(0.1f, 0.1f, 0.1f);
		hdrRt.sizeDelta = new Vector2(panelWidth * 10f, 300f);
		float hdrLocalY = headerSample != null
			? snapRt.InverseTransformPoint(headerSample.transform.position).y
			: (upgradePanelRt.sizeDelta.y * 0.5f - 20f);
		hdrRt.anchoredPosition = new Vector2(panelWidth * 0.5f, hdrLocalY);

		CreateSnapRowButton(snapRt, _healthBtn, TowerStatKind.Health, panelWidth, out _snapHealthBtn, out _snapHealthText);
		CreateSnapRowButton(snapRt, _armorBtn, TowerStatKind.Armor, panelWidth, out _snapArmorBtn, out _snapArmorText);
		CreateSnapRowButton(snapRt, _shieldBtn, TowerStatKind.Shield, panelWidth, out _snapShieldBtn, out _snapShieldText);
	}

	private void CreateSnapRowButton(
		RectTransform snapPanelRt,
		Button rowSourceBtn,
		TowerStatKind kind,
		float panelWidth,
		out Button snapBtn,
		out Text snapText)
	{
		GameObject clone = Instantiate(rowSourceBtn.gameObject, snapPanelRt);
		clone.name = $"Snap10_{kind}";

		// Strip any child XP fill bar images/extra handlers so only the button face & label remain
		for (int i = clone.transform.childCount - 1; i >= 0; i--)
		{
			Transform child = clone.transform.GetChild(i);
			if (child.GetComponent<Text>() == null)
			{
				Destroy(child.gameObject);
			}
		}
		HoldUpgradeButton oldHold = clone.GetComponent<HoldUpgradeButton>();
		if (oldHold != null)
		{
			DestroyImmediate(oldHold);
		}

		RectTransform srcRt = rowSourceBtn.GetComponent<RectTransform>();
		RectTransform dstRt = clone.GetComponent<RectTransform>();
		dstRt.anchorMin = new Vector2(0f, 0.5f);
		dstRt.anchorMax = new Vector2(0f, 0.5f);
		dstRt.pivot = new Vector2(0.5f, 0.5f);
		dstRt.localScale = srcRt.localScale;
		float rowLocalY = snapPanelRt.InverseTransformPoint(rowSourceBtn.transform.position).y;
		dstRt.sizeDelta = new Vector2(56f, srcRt.sizeDelta.y);
		dstRt.anchoredPosition = new Vector2(panelWidth * 0.5f, rowLocalY);

		snapBtn = clone.GetComponent<Button>();
		snapBtn.onClick = new Button.ButtonClickedEvent();
		HoldUpgradeButton hold = clone.AddComponent<HoldUpgradeButton>();
		hold.Configure(this, _towerUI, kind, isSnap10: true);

		snapText = clone.GetComponentInChildren<Text>(includeInactive: true);
		if (snapText != null)
		{
			snapText.text = "+10";
			snapText.alignment = TextAnchor.MiddleCenter;
			snapText.horizontalOverflow = HorizontalWrapMode.Overflow;
			snapText.verticalOverflow = VerticalWrapMode.Overflow;
			snapText.resizeTextForBestFit = false;
			if (snapText.fontSize < 60)
			{
				snapText.fontSize = Mathf.Max(snapText.fontSize, 12);
			}
			else
			{
				snapText.fontSize = Mathf.Max(snapText.fontSize, 120);
			}
			RectTransform txtRt = snapText.GetComponent<RectTransform>();
			if (txtRt != null)
			{
				txtRt.anchorMin = Vector2.zero;
				txtRt.anchorMax = Vector2.one;
				txtRt.anchoredPosition = Vector2.zero;
				txtRt.sizeDelta = Vector2.zero;
			}
		}
	}

	private static RectTransform FindUpgradePanelRect(Transform btnTransform)
	{
		Transform cur = btnTransform != null ? btnTransform.parent : null;
		while (cur != null)
		{
			if (cur.GetComponent<Image>() != null && cur.GetComponent<Canvas>() == null)
			{
				return cur.GetComponent<RectTransform>();
			}
			cur = cur.parent;
		}
		return btnTransform?.parent as RectTransform;
	}

	private static Text FindHeaderText(RectTransform panel, string containsText)
	{
		Text[] texts = panel.GetComponentsInChildren<Text>(includeInactive: true);
		foreach (Text t in texts)
		{
			if (t != null && !string.IsNullOrEmpty(t.text) && t.text.Contains(containsText))
			{
				return t;
			}
		}
		return null;
	}

	internal void RefreshLabels()
	{
		if (_towerUI == null)
		{
			return;
		}
		Tower tower = MyTowerRef(_towerUI);
		if (tower == null)
		{
			return;
		}

		UpdateSnapButtonVisual(tower, TowerStatKind.Health, _snapHealthBtn, _snapHealthText);
		UpdateSnapButtonVisual(tower, TowerStatKind.Armor, _snapArmorBtn, _snapArmorText);
		UpdateSnapButtonVisual(tower, TowerStatKind.Shield, _snapShieldBtn, _snapShieldText);
	}

	private static void UpdateSnapButtonVisual(Tower tower, TowerStatKind kind, Button btn, Text txt)
	{
		if (btn == null && txt == null)
		{
			return;
		}
		int delta = TowerUpgradeHelper.GetNextMultipleOf10Delta(TowerUpgradeHelper.GetStatMultiplier(tower, kind));
		bool canAfford = TowerUpgradeHelper.CanAffordSingle(tower, kind);

		if (txt != null)
		{
			string label = $"+{delta}";
			if (txt.text != label)
			{
				txt.text = label;
			}
			Color c = txt.color;
			c.a = canAfford ? 1f : 0.45f;
			txt.color = c;
		}
		if (btn != null)
		{
			btn.interactable = canAfford;
		}
	}

	internal void TickKeyboardHold()
	{
		if (_towerUI == null)
		{
			return;
		}
		Tower tower = MyTowerRef(_towerUI);
		if (tower == null)
		{
			return;
		}

		bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
		TickSingleKey(0, KeyCode.Alpha1, KeyCode.Keypad1, TowerStatKind.Health, tower, shift);
		TickSingleKey(1, KeyCode.Alpha2, KeyCode.Keypad2, TowerStatKind.Armor, tower, shift);
		TickSingleKey(2, KeyCode.Alpha3, KeyCode.Keypad3, TowerStatKind.Shield, tower, shift);
	}

	private void TickSingleKey(int idx, KeyCode k1, KeyCode k2, TowerStatKind kind, Tower tower, bool shift)
	{
		bool down = Input.GetKeyDown(k1) || Input.GetKeyDown(k2);
		bool held = Input.GetKey(k1) || Input.GetKey(k2);

		if (down)
		{
			// Initial press is handled by vanilla TowerUI.Update (or our Shift Prefix)
			_keyHoldElapsed[idx] = 0f;
			_keyRepeatTimer[idx] = 0f;
			return;
		}

		if (!held)
		{
			_keyHoldElapsed[idx] = 0f;
			_keyRepeatTimer[idx] = 0f;
			return;
		}

		float dt = Time.unscaledDeltaTime;
		_keyHoldElapsed[idx] += dt;
		float delay = shift ? Mathf.Max(0.18f, Plugin.HoldInitialDelay.Value + 0.04f) : Plugin.HoldInitialDelay.Value;
		if (_keyHoldElapsed[idx] < delay)
		{
			return;
		}

		_keyRepeatTimer[idx] -= dt;
		if (_keyRepeatTimer[idx] <= 0f)
		{
			int bought = shift
				? TowerUpgradeHelper.BuySnap10(tower, kind, _towerUI)
				: TowerUpgradeHelper.BuyLevels(tower, kind, GetBatchCount(_keyHoldElapsed[idx], isSnap10: false), _towerUI);

			if (bought <= 0)
			{
				_keyRepeatTimer[idx] = 0.2f;
				return;
			}
			_keyRepeatTimer[idx] = GetRepeatInterval(_keyHoldElapsed[idx], isSnap10: shift);
		}
	}

	internal static float GetRepeatInterval(float holdElapsed, bool isSnap10)
	{
		int speed = Mathf.Clamp(Plugin.HoldRepeatSpeed.Value, 0, 2);
		float t = Mathf.Clamp01((holdElapsed - Plugin.HoldInitialDelay.Value) / 1.5f);
		if (isSnap10)
		{
			return speed switch
			{
				0 => Mathf.Lerp(0.20f, 0.12f, t),
				2 => Mathf.Lerp(0.10f, 0.05f, t),
				_ => Mathf.Lerp(0.15f, 0.08f, t)
			};
		}
		return speed switch
		{
			0 => Mathf.Lerp(0.11f, 0.05f, t),
			2 => Mathf.Lerp(0.045f, 0.015f, t),
			_ => Mathf.Lerp(0.075f, 0.025f, t)
		};
	}

	internal static int GetBatchCount(float holdElapsed, bool isSnap10)
	{
		if (isSnap10)
		{
			return 10;
		}
		if (Plugin.HoldRepeatSpeed.Value >= 2 && holdElapsed > 1.2f)
		{
			return 2;
		}
		return 1;
	}
}

internal sealed class HoldUpgradeButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
	private static readonly AccessTools.FieldRef<TowerUI, Tower> MyTowerRef =
		AccessTools.FieldRefAccess<TowerUI, Tower>("myTower");

	private TowerFastUpgradeController _controller;
	private TowerUI _towerUI;
	private TowerStatKind _kind;
	private bool _isSnap10;

	private bool _holding;
	private float _holdElapsed;
	private float _repeatTimer;

	internal void Configure(TowerFastUpgradeController controller, TowerUI towerUI, TowerStatKind kind, bool isSnap10)
	{
		_controller = controller;
		_towerUI = towerUI;
		_kind = kind;
		_isSnap10 = isSnap10;
	}

	public void OnPointerDown(PointerEventData eventData)
	{
		if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
		{
			return;
		}
		Tower tower = _towerUI != null ? MyTowerRef(_towerUI) : null;
		if (tower == null)
		{
			return;
		}

		int bought = _isSnap10
			? TowerUpgradeHelper.BuySnap10(tower, _kind, _towerUI)
			: TowerUpgradeHelper.BuyLevels(tower, _kind, 1, _towerUI);

		_holding = bought > 0;
		_holdElapsed = 0f;
		_repeatTimer = 0f;
	}

	public void OnPointerUp(PointerEventData eventData)
	{
		_holding = false;
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		_holding = false;
	}

	private void OnDisable()
	{
		_holding = false;
	}

	private void Update()
	{
		if (!_holding || _towerUI == null)
		{
			return;
		}
		if (!Input.GetMouseButton(0))
		{
			_holding = false;
			return;
		}

		Tower tower = MyTowerRef(_towerUI);
		if (tower == null)
		{
			_holding = false;
			return;
		}

		float dt = Time.unscaledDeltaTime;
		_holdElapsed += dt;
		float delay = _isSnap10 ? Mathf.Max(0.22f, Plugin.HoldInitialDelay.Value + 0.04f) : Plugin.HoldInitialDelay.Value;
		if (_holdElapsed < delay)
		{
			return;
		}

		_repeatTimer -= dt;
		if (_repeatTimer <= 0f)
		{
			int bought = _isSnap10
				? TowerUpgradeHelper.BuySnap10(tower, _kind, _towerUI)
				: TowerUpgradeHelper.BuyLevels(tower, _kind, TowerFastUpgradeController.GetBatchCount(_holdElapsed, isSnap10: false), _towerUI);

			if (bought <= 0)
			{
				_holding = false;
				return;
			}
			_repeatTimer = TowerFastUpgradeController.GetRepeatInterval(_holdElapsed, _isSnap10);
			_controller?.RefreshLabels();
		}
	}
}
