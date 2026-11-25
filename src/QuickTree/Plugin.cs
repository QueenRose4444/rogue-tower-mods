using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[assembly: AssemblyCompany("RogueTowerQuickTree")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyDescription("Smart Quick-Buy and Quick-Refund for entire research trees, branches, and paths in Rogue Tower.")]
[assembly: AssemblyFileVersion("1.4.0.0")]
[assembly: AssemblyInformationalVersion("1.4.0")]
[assembly: AssemblyProduct("RogueTowerQuickTree")]
[assembly: AssemblyTitle("RogueTowerQuickTree")]
[assembly: AssemblyVersion("1.4.0.0")]

namespace RogueTowerQuickTree;

public enum AutoMaxBranchMode
{
	Off = 0,
	SameUpgradeChain = 1,
	FullBranchSubtree = 2,
	WholeTowerTree = 3,
	AllResearched = 4
}

[BepInPlugin("Rosie.RogueTowerQuickTree", "Rogue Tower Quick Research Tree", "1.4.0")]
[BepInDependency("me.tepis.roguetower.refundupgrades", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("AgusBut.CardDataLib", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
	internal static ManualLogSource Log;

	public static ConfigEntry<bool> ShowTreeButtons;
	public static ConfigEntry<bool> ShowRefundAllButton;
	public static ConfigEntry<bool> ShowResearchAllButton;
	public static ConfigEntry<bool> EnableModifierClicks;
	public static ConfigEntry<bool> SkipPermanentStatUpgradesByDefault;
	public static ConfigEntry<bool> SkipManaConsumingUpgradesByDefault;
	public static ConfigEntry<AutoMaxBranchMode> DefaultAutoMaxMode;
	public static ConfigEntry<bool> ResetAutoMaxEachRun;
	public static ConfigEntry<bool> ShowInGameAutoMaxHudButton;

	private bool _lastAltState;

	public static int GetAutoMaxMode()
	{
		return (int)InGameAutoMaxManager.CurrentMode;
	}

	public static void SetAutoMaxMode(int mode)
	{
		InGameAutoMaxManager.SetMode(mode);
	}

	private void Awake()
	{
		Log = base.Logger;

		ShowTreeButtons = base.Config.Bind(
			"1. UI Buttons",
			"ShowTreeButtons",
			defaultValue: false,
			"If true, adds extra 'Buy Tree' and 'Refund Tree' buttons onto every tower and passive research tree in the Upgrades menu (disabled by default since you can just click the end of any branch to auto-buy up to that card).");

		ShowRefundAllButton = base.Config.Bind(
			"1. UI Buttons",
			"ShowRefundAllButton",
			defaultValue: true,
			"Adds a 'Refund All (+XP)' button on the top bar of the Upgrades menu to refund 100% of spent XP without resetting/prestiging.");

		ShowResearchAllButton = base.Config.Bind(
			"1. UI Buttons",
			"ShowResearchAllButton",
			defaultValue: true,
			"Adds a 'Research All' button on the top bar of the Upgrades menu (next to 'Refund All') to unlock all remaining upgrades in the tree if you have enough XP.");

		EnableModifierClicks = base.Config.Bind(
			"2. Shortcuts",
			"EnableModifierClicks",
			defaultValue: true,
			"Left-Click any locked card in a branch to auto-buy up to that card. Right-Click (or Left-Click) any unlocked card to refund from the end of the branch back to that card. Hold Shift+Click to buy or refund an entire tower tree at once.");

		SkipPermanentStatUpgradesByDefault = base.Config.Bind(
			"3. Smart Buy Filters",
			"SkipPermanentStatUpgradesByDefault",
			defaultValue: false,
			"If true, Quick Buy skips the 120-XP permanent +1 stat upgrades on towers unless all card unlocks in that tree are already bought (or hold LeftAlt to toggle on the fly).");

		SkipManaConsumingUpgradesByDefault = base.Config.Bind(
			"3. Smart Buy Filters",
			"SkipManaConsumingUpgradesByDefault",
			defaultValue: false,
			"If true, Quick Buy skips Mana Bolts / Mana Bombs / Soul Powered upgrades unless clicked directly.");

		DefaultAutoMaxMode = base.Config.Bind(
			"4. In-Game Auto-Max Branch",
			"DefaultAutoMaxMode",
			defaultValue: AutoMaxBranchMode.SameUpgradeChain,
			"Off = vanilla 1 card per pick. SameUpgradeChain (Branch Line) = drafting an upgrade grants its entire upgrade line & end capstones. FullBranchSubtree = grants entire unlocked subtree. WholeTowerTree = grants every unlocked card for that tower. AllResearched = grants ALL unlocked cards at run start.");

		ResetAutoMaxEachRun = base.Config.Bind(
			"4. In-Game Auto-Max Branch",
			"ResetAutoMaxEachRun",
			defaultValue: false,
			"If true, Auto-Max Branch resets to Off on returning to the Play menu so you manually opt in each run.");

		ShowInGameAutoMaxHudButton = base.Config.Bind(
			"4. In-Game Auto-Max Branch",
			"ShowInGameAutoMaxHudButton",
			defaultValue: false,
			"If true, shows a floating Auto-Max toggle button below SpeedControls before wave 1 and when paused (off by default since Auto-Max is in the Mod Menu and PlayMenu).");

		InGameAutoMaxManager.CurrentMode = DefaultAutoMaxMode.Value;

		Harmony harmony = new Harmony("Rosie.RogueTowerQuickTree");
		harmony.PatchAll();
		QuickTreePatches.InstallOptionalRefundUpgradesPatch(harmony);
		Log.LogInfo("RogueTowerQuickTree 1.4.0 loaded!");
	}

	private void Update()
	{
		InGameAutoMaxManager.Tick();

		if (UpgradeManager.instance == null)
		{
			return;
		}
		bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
		if (altHeld != _lastAltState)
		{
			_lastAltState = altHeld;
			QuickTreeManager.RefreshAllUI();
		}
	}
}

internal static class QuickTreeManager
{
	internal sealed class TreeGroupUI
	{
		public string Name;
		public string ShortLabel;
		public bool IsTowerGroup;
		public List<UpgradeButton> Nodes = new List<UpgradeButton>();
		public Button BuyButton;
		public Text BuyButtonText;
		public Image BuyButtonImage;
		public Button RefundButton;
		public Text RefundButtonText;
		public Image RefundButtonImage;
	}

	private static readonly AccessTools.FieldRef<UpgradeButton, string> UnlockStringRef =
		AccessTools.FieldRefAccess<UpgradeButton, string>("unlockString");
	private static readonly AccessTools.FieldRef<UpgradeButton, Image> ImgRef =
		AccessTools.FieldRefAccess<UpgradeButton, Image>("img");
	private static readonly AccessTools.FieldRef<UpgradeButton, Button> BtnRef =
		AccessTools.FieldRefAccess<UpgradeButton, Button>("btn");
	private static readonly AccessTools.FieldRef<UpgradeButton, Sprite> UnlockedSpriteRef =
		AccessTools.FieldRefAccess<UpgradeButton, Sprite>("unlockedSprite");
	private static readonly AccessTools.FieldRef<UpgradeButton, bool> CheckAchievementsRef =
		AccessTools.FieldRefAccess<UpgradeButton, bool>("checkAchievements");
	private static readonly AccessTools.FieldRef<UpgradeButton, bool> CountsAsCardUnlockRef =
		AccessTools.FieldRefAccess<UpgradeButton, bool>("countsAsCardUnlock");
	private static readonly AccessTools.FieldRef<UpgradeButton, bool> CountAsDevelopmentRef =
		AccessTools.FieldRefAccess<UpgradeButton, bool>("countAsDevelopment");
	private static readonly AccessTools.FieldRef<UpgradeButton, GameObject> CurrentPriceTagRef =
		AccessTools.FieldRefAccess<UpgradeButton, GameObject>("currentPriceTag");
	private static readonly AccessTools.FieldRef<UpgradeButton, UpgradeButton> PreviousRef =
		AccessTools.FieldRefAccess<UpgradeButton, UpgradeButton>("previous");
	private static readonly AccessTools.FieldRef<UpgradeButton, UpgradeButton[]> NextRef =
		AccessTools.FieldRefAccess<UpgradeButton, UpgradeButton[]>("next");
	private static readonly AccessTools.FieldRef<UpgradeManager, List<UpgradeButton>> CardsRequiringCountRef =
		AccessTools.FieldRefAccess<UpgradeManager, List<UpgradeButton>>("cardsWhichRequireCardCount");

	private static readonly MethodInfo UpdateTitleTextMethod =
		typeof(UpgradeButton).GetMethod("UpdateTitleText", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly Dictionary<UpgradeButton, Sprite> LocalLockedSprites = new Dictionary<UpgradeButton, Sprite>();
	private static readonly Dictionary<UpgradeButton, List<UpgradeButton>> ChildrenMap = new Dictionary<UpgradeButton, List<UpgradeButton>>();
	private static readonly Dictionary<UpgradeButton, TreeGroupUI> ButtonToMainGroup = new Dictionary<UpgradeButton, TreeGroupUI>();
	private static readonly List<TreeGroupUI> AllGroups = new List<TreeGroupUI>();
	private static readonly List<UpgradeButton> AllActiveButtons = new List<UpgradeButton>();

	private static Button _refundAllButton;
	private static Text _refundAllText;
	private static Image _refundAllImage;
	private static Button _researchAllButton;
	private static Text _researchAllText;
	private static Image _researchAllImage;
	internal static bool IsExecutingBulkAction;
	internal static int LastModifierClickFrame = -1;

	private static readonly HashSet<string> TowerGroupNames = new HashSet<string>
	{
		"Ballista", "Mortar", "TeslaCoil", "FrostKeep", "FlameThrower",
		"PoisonSprayer", "Shredder", "Encampment", "Lookout", "Radar",
		"Obelisk", "ParticleCannon", "VampireLair", "Cannon", "Monument"
	};

	private static readonly HashSet<string> LeftSideGroupNames = new HashSet<string>
	{
		"CardPermanant", "GoldPermanant", "ManaPermanant", "HealthPermanant",
		"Buildings", "DOT", "Monster Stuff"
	};

	private static readonly HashSet<string> ManaConsumingUnlockStrings = new HashSet<string>
	{
		"BallistaManaBolts1", "BallistaManaBolts2", "BallistaManaBolts3",
		"MortarMana1", "MortarMana2", "MortarMana3",
		"EncampmentMana1", "EncampmentMana2", "EncampmentMana3",
		"FrostMana1", "FrostMana2",
		"VampireLairMana1", "VampireLairMana2"
	};

	internal static void RecordLockedSprite(UpgradeButton btn)
	{
		if (btn == null)
		{
			return;
		}
		Sprite s = btn.GetComponent<Image>()?.sprite;
		if (s != null)
		{
			LocalLockedSprites[btn] = s;
		}
	}

	internal static void FixVanillaArmorUnlockString(UpgradeButton btn)
	{
		if (btn == null)
		{
			return;
		}
		Transform t = btn.transform;
		Transform p = t != null ? t.parent : null;
		if (t == null || p == null)
		{
			return;
		}
		string parentName = p.name;
		string objName = t.name;
		if ((parentName == "VampireLair" || parentName == "Monument" || parentName == "ParticleCannon") && objName.StartsWith("ArmorDamage"))
		{
			int tier = objName.EndsWith(" (2)") ? 3 : (objName.EndsWith(" (1)") ? 2 : 1);
			UnlockStringRef(btn) = $"{parentName}Armor{tier}";
		}
	}

	internal static bool IsValidNode(UpgradeButton btn)
	{
		return btn != null && btn.enabled && btn.gameObject.activeInHierarchy;
	}

	internal static bool IsParentSatisfied(UpgradeButton btn, HashSet<UpgradeButton> unlockedSet = null)
	{
		UpgradeButton prev = PreviousRef(btn);
		if (prev == null || !prev.enabled || !prev.gameObject.activeInHierarchy)
		{
			return true;
		}
		if (unlockedSet != null)
		{
			return unlockedSet.Contains(prev);
		}
		return prev.unlocked;
	}

	internal static bool ShouldSkipNodeInBulkBuy(UpgradeButton btn, bool isTowerGroup, bool forceAllowPermanents)
	{
		bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
		bool skipPerms = Plugin.SkipPermanentStatUpgradesByDefault.Value ^ altHeld;
		if (skipPerms && !forceAllowPermanents && isTowerGroup && !CountsAsCardUnlockRef(btn))
		{
			return true;
		}
		if (Plugin.SkipManaConsumingUpgradesByDefault.Value)
		{
			string u = UnlockStringRef(btn);
			if (!string.IsNullOrEmpty(u) && ManaConsumingUnlockStrings.Contains(u))
			{
				return true;
			}
		}
		return false;
	}

	internal static void InitializeUpgradeMenu(UpgradeManager manager)
	{
		AllGroups.Clear();
		AllActiveButtons.Clear();
		ChildrenMap.Clear();
		ButtonToMainGroup.Clear();
		_refundAllButton = null;
		_refundAllText = null;
		_refundAllImage = null;
		_researchAllButton = null;
		_researchAllText = null;
		_researchAllImage = null;

		GameObject slidingScreen = GameObject.Find("Canvas/UpgradeMenu/SlidingScreen");
		if (slidingScreen == null)
		{
			Plugin.Log.LogWarning("Could not find Canvas/UpgradeMenu/SlidingScreen.");
			return;
		}

		// Collect all active & enabled UpgradeButtons and build bidirectional parent/child maps
		UpgradeButton[] rawButtons = slidingScreen.GetComponentsInChildren<UpgradeButton>(includeInactive: false);
		foreach (UpgradeButton btn in rawButtons)
		{
			if (!IsValidNode(btn))
			{
				continue;
			}
			FixVanillaArmorUnlockString(btn);
			if (btn.gameObject.GetComponent<QuickTreeNodeClickHandler>() == null)
			{
				btn.gameObject.AddComponent<QuickTreeNodeClickHandler>().TargetButton = btn;
			}
			AllActiveButtons.Add(btn);
			if (!ChildrenMap.ContainsKey(btn))
			{
				ChildrenMap[btn] = new List<UpgradeButton>();
			}
		}

		foreach (UpgradeButton btn in AllActiveButtons)
		{
			UpgradeButton prev = PreviousRef(btn);
			if (IsValidNode(prev))
			{
				if (!ChildrenMap.TryGetValue(prev, out var list))
				{
					list = new List<UpgradeButton>();
					ChildrenMap[prev] = list;
				}
				if (!list.Contains(btn))
				{
					list.Add(btn);
				}
			}
			UpgradeButton[] nextArr = NextRef(btn);
			if (nextArr != null)
			{
				foreach (UpgradeButton nxt in nextArr)
				{
					if (IsValidNode(nxt))
					{
						if (!ChildrenMap.TryGetValue(btn, out var list))
						{
							list = new List<UpgradeButton>();
							ChildrenMap[btn] = list;
						}
						if (!list.Contains(nxt))
						{
							list.Add(nxt);
						}
					}
				}
			}
		}

		GameObject templateButton = GameObject.Find("Canvas/UpgradeMenu/Scaler/BackButton");

		// Build per-container groups under SlidingScreen
		foreach (Transform groupTransform in slidingScreen.transform)
		{
			if (groupTransform == null || !groupTransform.gameObject.activeSelf)
			{
				continue;
			}
			string groupName = groupTransform.name;
			bool isTower = TowerGroupNames.Contains(groupName);
			bool isLeft = LeftSideGroupNames.Contains(groupName);
			if (!isTower && !isLeft)
			{
				continue;
			}

			List<UpgradeButton> groupNodes = new List<UpgradeButton>();
			List<UpgradeButton> allGroupButtonsIncludingDefaults = new List<UpgradeButton>();
			foreach (Transform child in groupTransform)
			{
				if (child == null || !child.gameObject.activeSelf)
				{
					continue;
				}
				UpgradeButton ub = child.GetComponent<UpgradeButton>();
				if (ub != null)
				{
					allGroupButtonsIncludingDefaults.Add(ub);
				}
				if (IsValidNode(ub))
				{
					groupNodes.Add(ub);
				}
			}
			if (groupNodes.Count == 0)
			{
				continue;
			}

			TreeGroupUI mainGroup = new TreeGroupUI
			{
				Name = groupName,
				ShortLabel = "Tree",
				IsTowerGroup = isTower,
				Nodes = groupNodes
			};
			foreach (UpgradeButton ub in allGroupButtonsIncludingDefaults)
			{
				ButtonToMainGroup[ub] = mainGroup;
				QuickTreeNodeClickHandler handler = ub.gameObject.GetComponent<QuickTreeNodeClickHandler>();
				if (handler == null)
				{
					handler = ub.gameObject.AddComponent<QuickTreeNodeClickHandler>();
				}
				handler.TargetButton = ub;
				handler.TargetGroup = mainGroup;
			}
			AllGroups.Add(mainGroup);

			if (Plugin.ShowTreeButtons.Value && templateButton != null)
			{
				if (isTower)
				{
					// Place directly underneath the Tower's root card at (125, -120) and (125, -150)
					CreateGroupButtons(
						groupTransform,
						templateButton,
						mainGroup,
						anchor: new Vector2(0f, 1f),
						buyPos: new Vector2(125f, -120f),
						refundPos: new Vector2(125f, -150f),
						size: new Vector2(215f, 26f));
				}
				else if (groupName == "DOT")
				{
					// DOT has empty space above Bleed1 at (-125, -45) & (-125, -75), plus space under Bleed1, burn1, poison1
					mainGroup.ShortLabel = "All DOT";
					CreateGroupButtons(
						groupTransform,
						templateButton,
						mainGroup,
						anchor: new Vector2(1f, 1f),
						buyPos: new Vector2(-125f, -45f),
						refundPos: new Vector2(-125f, -75f),
						size: new Vector2(215f, 26f));

					CreateDOTSubtreeGroup(groupTransform, templateButton, "Bleed1", "Bleed", new Vector2(-125f, -210f), new Vector2(-125f, -240f));
					CreateDOTSubtreeGroup(groupTransform, templateButton, "burn1", "Burn", new Vector2(-125f, -590f), new Vector2(-125f, -620f));
					CreateDOTSubtreeGroup(groupTransform, templateButton, "poison1", "Poison", new Vector2(-125f, -970f), new Vector2(-125f, -1000f));
				}
				else
				{
					// Left-side groups: place neatly in the 169px center margin immediately to the right of the group's first card
					mainGroup.ShortLabel = "";
					CreateGroupButtons(
						groupTransform,
						templateButton,
						mainGroup,
						anchor: new Vector2(1f, 1f),
						buyPos: new Vector2(82f, -44f),
						refundPos: new Vector2(82f, -76f),
						size: new Vector2(154f, 26f),
						fontSize: 105);
				}
			}
		}

		// Create global "Refund All" and "Research All" buttons on the top bar next to Reset Game
		if (templateButton != null && (Plugin.ShowRefundAllButton.Value || Plugin.ShowResearchAllButton.Value))
		{
			Transform scaler = templateButton.transform.parent;
			if (scaler != null)
			{
				if (Plugin.ShowRefundAllButton.Value)
				{
					_refundAllButton = CreateSingleActionButton(
						scaler,
						templateButton,
						"QuickTree_RefundAllButton",
						anchor: new Vector2(0.5f, 1f),
						anchoredPos: new Vector2(346f, -150f),
						size: new Vector2(160f, 30f),
						fontSize: 120,
						onClick: delegate
						{
							ExecuteSmartRefund(AllActiveButtons);
						},
						out _refundAllText,
						out _refundAllImage);
				}

				if (Plugin.ShowResearchAllButton.Value)
				{
					_researchAllButton = CreateSingleActionButton(
						scaler,
						templateButton,
						"QuickTree_ResearchAllButton",
						anchor: new Vector2(0.5f, 1f),
						anchoredPos: new Vector2(518f, -150f),
						size: new Vector2(172f, 30f),
						fontSize: 115,
						onClick: ExecuteResearchAll,
						out _researchAllText,
						out _researchAllImage);
				}
			}
		}

		RefreshAllUI();
	}

	private static void CreateDOTSubtreeGroup(Transform dotTransform, GameObject templateButton, string rootChildName, string shortLabel, Vector2 buyPos, Vector2 refundPos)
	{
		Transform rootTrans = dotTransform.Find(rootChildName);
		UpgradeButton rootBtn = rootTrans != null ? rootTrans.GetComponent<UpgradeButton>() : null;
		if (!IsValidNode(rootBtn))
		{
			return;
		}
		List<UpgradeButton> subNodes = CollectDescendantsAndSelf(rootBtn);
		if (subNodes.Count == 0)
		{
			return;
		}
		TreeGroupUI subGroup = new TreeGroupUI
		{
			Name = "DOT_" + shortLabel,
			ShortLabel = shortLabel,
			IsTowerGroup = false,
			Nodes = subNodes
		};
		foreach (UpgradeButton ub in subNodes)
		{
			QuickTreeNodeClickHandler handler = ub.gameObject.GetComponent<QuickTreeNodeClickHandler>();
			if (handler != null)
			{
				handler.SubGroup = subGroup;
			}
		}
		AllGroups.Add(subGroup);
		CreateGroupButtons(
			dotTransform,
			templateButton,
			subGroup,
			anchor: new Vector2(1f, 1f),
			buyPos: buyPos,
			refundPos: refundPos,
			size: new Vector2(215f, 26f),
			fontSize: 115);
	}

	private static void CreateGroupButtons(
		Transform parent,
		GameObject templateButton,
		TreeGroupUI group,
		Vector2 anchor,
		Vector2 buyPos,
		Vector2 refundPos,
		Vector2 size,
		int fontSize = 115)
	{
		group.BuyButton = CreateSingleActionButton(
			parent,
			templateButton,
			$"QuickTree_Buy_{group.Name}",
			anchor,
			buyPos,
			size,
			fontSize: fontSize,
			onClick: delegate
			{
				ExecuteSmartBuy(group.Nodes, group.IsTowerGroup);
			},
			out group.BuyButtonText,
			out group.BuyButtonImage);

		group.RefundButton = CreateSingleActionButton(
			parent,
			templateButton,
			$"QuickTree_Refund_{group.Name}",
			anchor,
			refundPos,
			size,
			fontSize: fontSize,
			onClick: delegate
			{
				ExecuteSmartRefund(group.Nodes);
			},
			out group.RefundButtonText,
			out group.RefundButtonImage);
	}

	private static Button CreateSingleActionButton(
		Transform parent,
		GameObject templateButton,
		string name,
		Vector2 anchor,
		Vector2 anchoredPos,
		Vector2 size,
		int fontSize,
		Action onClick,
		out Text labelText,
		out Image bgImage)
	{
		Transform existing = parent.Find(name);
		if (existing != null)
		{
			UnityEngine.Object.DestroyImmediate(existing.gameObject);
		}

		GameObject go = UnityEngine.Object.Instantiate(templateButton, parent);
		go.name = name;
		go.SetActive(value: true);

		RectTransform rt = go.GetComponent<RectTransform>();
		rt.anchorMin = anchor;
		rt.anchorMax = anchor;
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.localScale = Vector3.one;
		rt.sizeDelta = size;
		rt.anchoredPosition = anchoredPos;

		bgImage = go.GetComponent<Image>();
		Button btn = go.GetComponent<Button>();
		btn.onClick = new Button.ButtonClickedEvent();
		btn.onClick.AddListener(delegate
		{
			onClick?.Invoke();
		});

		labelText = go.GetComponentInChildren<Text>(includeInactive: true);
		if (labelText != null)
		{
			// Rogue Tower's UI Text uses localScale = (0.1, 0.1, 0.1) with fontSize ~110..140 and Overflow wrap mode
			labelText.fontSize = fontSize;
			labelText.resizeTextForBestFit = false;
			labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
			labelText.verticalOverflow = VerticalWrapMode.Overflow;
			labelText.alignment = TextAnchor.MiddleCenter;
			RectTransform textRt = labelText.GetComponent<RectTransform>();
			if (textRt != null)
			{
				textRt.localScale = new Vector3(0.1f, 0.1f, 0.1f);
				textRt.anchorMin = Vector2.zero;
				textRt.anchorMax = Vector2.one;
				textRt.anchoredPosition = Vector2.zero;
				textRt.sizeDelta = Vector2.zero;
			}
		}

		return btn;
	}

	internal static List<UpgradeButton> CollectAncestorsAndSelf(UpgradeButton target)
	{
		List<UpgradeButton> chain = new List<UpgradeButton>();
		HashSet<UpgradeButton> visited = new HashSet<UpgradeButton>();
		UpgradeButton curr = target;
		while (IsValidNode(curr) && visited.Add(curr))
		{
			chain.Add(curr);
			curr = PreviousRef(curr);
		}
		chain.Reverse();
		return chain;
	}

	internal static List<UpgradeButton> CollectDescendantsAndSelf(UpgradeButton root)
	{
		List<UpgradeButton> result = new List<UpgradeButton>();
		if (!IsValidNode(root))
		{
			return result;
		}
		HashSet<UpgradeButton> visited = new HashSet<UpgradeButton> { root };
		Queue<UpgradeButton> queue = new Queue<UpgradeButton>();
		queue.Enqueue(root);
		while (queue.Count > 0)
		{
			UpgradeButton curr = queue.Dequeue();
			result.Add(curr);
			if (ChildrenMap.TryGetValue(curr, out var children))
			{
				foreach (UpgradeButton child in children)
				{
					if (IsValidNode(child) && visited.Add(child))
					{
						queue.Enqueue(child);
					}
				}
			}
		}
		return result;
	}

	internal static List<UpgradeButton> CollectBranchWithPrerequisites(UpgradeButton clicked)
	{
		List<UpgradeButton> ancestors = CollectAncestorsAndSelf(clicked);
		List<UpgradeButton> descendants = CollectDescendantsAndSelf(clicked);
		HashSet<UpgradeButton> seen = new HashSet<UpgradeButton>();
		List<UpgradeButton> combined = new List<UpgradeButton>();
		foreach (UpgradeButton a in ancestors)
		{
			if (seen.Add(a))
			{
				combined.Add(a);
			}
		}
		foreach (UpgradeButton d in descendants)
		{
			if (seen.Add(d))
			{
				combined.Add(d);
			}
		}
		return combined;
	}

	internal struct BuySimulationResult
	{
		public int TotalTargetCount;
		public int AlreadyUnlockedCount;
		public int RemainingLockedCount => TotalTargetCount - AlreadyUnlockedCount;
		public List<UpgradeButton> BuyOrder;
		public int TotalXPCost;
		public int NextBlockedByXP;
		public int NextBlockedByCardCount;
	}

	internal static BuySimulationResult SimulateSmartBuy(
		IList<UpgradeButton> targetNodes,
		bool isTowerGroup,
		HashSet<UpgradeButton> priorityAncestors = null,
		bool ignoreFilters = false)
	{
		BuySimulationResult res = new BuySimulationResult
		{
			BuyOrder = new List<UpgradeButton>(),
			NextBlockedByXP = int.MaxValue,
			NextBlockedByCardCount = int.MaxValue
		};

		if (UpgradeManager.instance == null || targetNodes == null || targetNodes.Count == 0)
		{
			return res;
		}

		// Check if all card-unlocking nodes in this target set are already unlocked (so permanent stat nodes are allowed even when SkipPermanents is active)
		bool allCardNodesUnlocked = true;
		foreach (UpgradeButton n in targetNodes)
		{
			if (IsValidNode(n) && !n.unlocked && CountsAsCardUnlockRef(n))
			{
				allCardNodesUnlocked = false;
				break;
			}
		}

		List<UpgradeButton> filteredTargets = new List<UpgradeButton>(targetNodes.Count);
		foreach (UpgradeButton n in targetNodes)
		{
			if (!IsValidNode(n))
			{
				continue;
			}
			if (!ignoreFilters && !n.unlocked && ShouldSkipNodeInBulkBuy(n, isTowerGroup, allCardNodesUnlocked))
			{
				continue;
			}
			filteredTargets.Add(n);
		}

		res.TotalTargetCount = filteredTargets.Count;

		// Seed simulated unlocked set with all currently unlocked buttons across the entire menu
		HashSet<UpgradeButton> simUnlocked = new HashSet<UpgradeButton>();
		foreach (UpgradeButton b in AllActiveButtons)
		{
			if (b.unlocked)
			{
				simUnlocked.Add(b);
			}
		}
		foreach (UpgradeButton n in filteredTargets)
		{
			if (n.unlocked)
			{
				res.AlreadyUnlockedCount++;
			}
		}

		int simXP = UpgradeManager.instance.xp;
		int simCardCount = UpgradeManager.instance.unlockedCardCount;

		while (true)
		{
			UpgradeButton bestCandidate = null;
			bool bestIsPriority = false;
			bool bestCountsAsCard = false;
			int bestXP = int.MaxValue;
			int bestCardReq = int.MaxValue;

			foreach (UpgradeButton candidate in filteredTargets)
			{
				if (simUnlocked.Contains(candidate))
				{
					continue;
				}
				if (!IsParentSatisfied(candidate, simUnlocked))
				{
					continue;
				}
				if (candidate.cardCountRequirement > simCardCount)
				{
					if (candidate.cardCountRequirement < res.NextBlockedByCardCount)
					{
						res.NextBlockedByCardCount = candidate.cardCountRequirement;
					}
					continue;
				}
				if (candidate.xpCost > simXP)
				{
					if (candidate.xpCost < res.NextBlockedByXP)
					{
						res.NextBlockedByXP = candidate.xpCost;
					}
					continue;
				}

				bool isPrio = priorityAncestors != null && priorityAncestors.Contains(candidate);
				bool isCard = CountsAsCardUnlockRef(candidate);
				int cost = candidate.xpCost;
				int req = candidate.cardCountRequirement;

				bool better = false;
				if (bestCandidate == null)
				{
					better = true;
				}
				else if (isPrio != bestIsPriority)
				{
					better = isPrio;
				}
				else if (isCard != bestCountsAsCard)
				{
					better = isCard;
				}
				else if (cost != bestXP)
				{
					better = cost < bestXP;
				}
				else if (req != bestCardReq)
				{
					better = req < bestCardReq;
				}

				if (better)
				{
					bestCandidate = candidate;
					bestIsPriority = isPrio;
					bestCountsAsCard = isCard;
					bestXP = cost;
					bestCardReq = req;
				}
			}

			if (bestCandidate == null)
			{
				break;
			}

			simUnlocked.Add(bestCandidate);
			simXP -= bestCandidate.xpCost;
			if (CountsAsCardUnlockRef(bestCandidate))
			{
				simCardCount++;
			}
			res.BuyOrder.Add(bestCandidate);
			res.TotalXPCost += bestCandidate.xpCost;
		}

		return res;
	}

	internal static int ExecuteSmartBuy(
		IList<UpgradeButton> targetNodes,
		bool isTowerGroup,
		HashSet<UpgradeButton> priorityAncestors = null,
		bool ignoreFilters = false)
	{
		if (UpgradeManager.instance == null || targetNodes == null || targetNodes.Count == 0)
		{
			return 0;
		}

		BuySimulationResult sim = SimulateSmartBuy(targetNodes, isTowerGroup, priorityAncestors, ignoreFilters);
		if (sim.BuyOrder == null || sim.BuyOrder.Count == 0)
		{
			return 0;
		}

		IsExecutingBulkAction = true;
		int boughtCount = 0;
		try
		{
			foreach (UpgradeButton btn in sim.BuyOrder)
			{
				if (!IsValidNode(btn) || btn.unlocked)
				{
					continue;
				}
				// Re-verify live constraints before each purchase
				if (!IsParentSatisfied(btn) ||
					btn.cardCountRequirement > UpgradeManager.instance.unlockedCardCount ||
					UpgradeManager.instance.xp < btn.xpCost)
				{
					continue;
				}
				UnlockSingleNodeSilent(btn);
				boughtCount++;
			}
		}
		finally
		{
			IsExecutingBulkAction = false;
		}

		if (boughtCount > 0)
		{
			SFXManager.instance?.ButtonClick();
			PlayerPrefs.Save();
			RefreshAllButtonStates();
			RefreshAllUI();
			Plugin.Log.LogInfo($"Smart Buy unlocked {boughtCount} upgrade(s) for {sim.TotalXPCost} XP.");
		}
		return boughtCount;
	}

	internal static void ExecuteResearchAll()
	{
		if (UpgradeManager.instance == null || AllActiveButtons.Count == 0)
		{
			return;
		}
		BuySimulationResult sim = SimulateSmartBuy(AllActiveButtons, isTowerGroup: false, priorityAncestors: null, ignoreFilters: false);
		bool useIgnoreFilters = sim.BuyOrder == null || sim.BuyOrder.Count == 0;
		ExecuteSmartBuy(AllActiveButtons, isTowerGroup: false, priorityAncestors: null, ignoreFilters: useIgnoreFilters);
	}

	private static void UnlockSingleNodeSilent(UpgradeButton btn)
	{
		FixVanillaArmorUnlockString(btn);
		string unlockStr = UnlockStringRef(btn);
		UpgradeManager.instance.AddXP(-btn.xpCost);
		PlayerPrefs.SetInt(unlockStr, 1);
		if (CheckAchievementsRef(btn))
		{
			AchievementManager.instance?.CheckTowerUnlocks();
		}
		if (CountsAsCardUnlockRef(btn))
		{
			UpgradeManager.instance.unlockedCardCount++;
			PlayerPrefs.SetInt("UnlockedCardCount", UpgradeManager.instance.unlockedCardCount);
		}
		if (CountAsDevelopmentRef(btn))
		{
			int dev = PlayerPrefs.GetInt("Development", 0);
			PlayerPrefs.SetInt("Development", dev + 1);
		}
		ImgRef(btn).sprite = UnlockedSpriteRef(btn);
		btn.unlocked = true;
		Button uiBtn = BtnRef(btn);
		if (uiBtn != null)
		{
			// Keep enabled = true so RefundUpgrades can click it to refund
			uiBtn.enabled = true;
		}
		UpdateTitleTextMethod?.Invoke(btn, null);
	}

	internal static int CalculateRefundableXP(IList<UpgradeButton> targetNodes, out int unlockedCount)
	{
		unlockedCount = 0;
		int xp = 0;
		if (targetNodes == null)
		{
			return 0;
		}
		foreach (UpgradeButton btn in targetNodes)
		{
			if (IsValidNode(btn) && btn.unlocked)
			{
				unlockedCount++;
				xp += btn.xpCost;
			}
		}
		return xp;
	}

	internal static int ExecuteSmartRefund(IList<UpgradeButton> targetNodes)
	{
		if (UpgradeManager.instance == null || targetNodes == null || targetNodes.Count == 0)
		{
			return 0;
		}

		// Expand targetNodes to include all unlocked descendants of any unlocked node in targetNodes
		HashSet<UpgradeButton> toRefund = new HashSet<UpgradeButton>();
		Queue<UpgradeButton> queue = new Queue<UpgradeButton>();

		foreach (UpgradeButton btn in targetNodes)
		{
			if (IsValidNode(btn) && btn.unlocked && toRefund.Add(btn))
			{
				queue.Enqueue(btn);
			}
		}

		while (queue.Count > 0)
		{
			UpgradeButton curr = queue.Dequeue();
			if (ChildrenMap.TryGetValue(curr, out var children))
			{
				foreach (UpgradeButton child in children)
				{
					if (IsValidNode(child) && child.unlocked && toRefund.Add(child))
					{
						queue.Enqueue(child);
					}
				}
			}
		}

		if (toRefund.Count == 0)
		{
			return 0;
		}

		IsExecutingBulkAction = true;
		int refundedCount = 0;
		int refundedXP = 0;
		try
		{
			foreach (UpgradeButton btn in toRefund)
			{
				if (btn.unlocked)
				{
					refundedXP += btn.xpCost;
					RefundSingleNodeSilent(btn);
					refundedCount++;
				}
			}

			// Cascade check: if unlockedCardCount dropped below any unlocked card's cardCountRequirement, refund it and its descendants too!
			List<UpgradeButton> reqCards = CardsRequiringCountRef(UpgradeManager.instance);
			if (reqCards != null)
			{
				bool cascaded;
				do
				{
					cascaded = false;
					foreach (UpgradeButton reqBtn in reqCards)
					{
						if (IsValidNode(reqBtn) && reqBtn.unlocked && reqBtn.cardCountRequirement > UpgradeManager.instance.unlockedCardCount)
						{
							List<UpgradeButton> branch = CollectDescendantsAndSelf(reqBtn);
							foreach (UpgradeButton b in branch)
							{
								if (IsValidNode(b) && b.unlocked)
								{
									refundedXP += b.xpCost;
									RefundSingleNodeSilent(b);
									refundedCount++;
									cascaded = true;
								}
							}
						}
					}
				}
				while (cascaded);
			}
		}
		finally
		{
			IsExecutingBulkAction = false;
		}

		if (refundedCount > 0)
		{
			SFXManager.instance?.ButtonClick();
			PlayerPrefs.Save();
			RefreshAllButtonStates();
			RefreshAllUI();
			Plugin.Log.LogInfo($"Smart Refund refunded {refundedCount} upgrade(s) (+{refundedXP} XP).");
		}

		return refundedCount;
	}

	private static void RefundSingleNodeSilent(UpgradeButton btn)
	{
		if (!btn.unlocked)
		{
			return;
		}
		FixVanillaArmorUnlockString(btn);
		string unlockStr = UnlockStringRef(btn);
		UpgradeManager.instance.AddXP(btn.xpCost);
		PlayerPrefs.SetInt(unlockStr, 0);
		btn.unlocked = false;

		// Restore locked sprite from optional RefundUpgrades or our local cache
		Sprite restored = QuickTreePatches.TryGetRefundUpgradesSprite(btn, unlockStr);
		if (restored == null && LocalLockedSprites.TryGetValue(btn, out var s2) && s2 != null)
		{
			restored = s2;
		}
		if (restored != null)
		{
			ImgRef(btn).sprite = restored;
		}

		if (CountsAsCardUnlockRef(btn))
		{
			UpgradeManager.instance.unlockedCardCount = Mathf.Max(0, UpgradeManager.instance.unlockedCardCount - 1);
			PlayerPrefs.SetInt("UnlockedCardCount", UpgradeManager.instance.unlockedCardCount);
		}
		if (CountAsDevelopmentRef(btn))
		{
			int dev = PlayerPrefs.GetInt("Development", 0);
			PlayerPrefs.SetInt("Development", Mathf.Max(0, dev - 1));
		}

		CurrentPriceTagRef(btn)?.SetActive(value: true);
	}

	internal static void RefreshAllButtonStates()
	{
		if (UpgradeManager.instance == null)
		{
			return;
		}
		foreach (UpgradeButton btn in AllActiveButtons)
		{
			if (IsValidNode(btn))
			{
				btn.CheckEnabled();
				Button uiBtn = BtnRef(btn);
				if (uiBtn != null)
				{
					uiBtn.enabled = true;
				}
			}
		}
	}

	internal static void RefreshAllUI()
	{
		if (UpgradeManager.instance == null)
		{
			return;
		}

		bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
		bool skipPerms = Plugin.SkipPermanentStatUpgradesByDefault.Value ^ altHeld;

		foreach (TreeGroupUI group in AllGroups)
		{
			bool hasLabel = !string.IsNullOrEmpty(group.ShortLabel);
			string labelWithSpace = hasLabel ? (" " + group.ShortLabel) : "";

			if (group.BuyButton != null && group.BuyButtonText != null)
			{
				BuySimulationResult sim = SimulateSmartBuy(group.Nodes, group.IsTowerGroup);
				int remLocked = sim.TotalTargetCount - sim.AlreadyUnlockedCount;
				string targetWord = (skipPerms && group.IsTowerGroup) ? " Cards" : labelWithSpace;

				if (remLocked <= 0)
				{
					group.BuyButton.interactable = false;
					group.BuyButtonText.text = hasLabel ? $"{group.ShortLabel} Maxed" : "Maxed";
				}
				else if (sim.BuyOrder.Count == remLocked)
				{
					group.BuyButton.interactable = true;
					string prefix = sim.AlreadyUnlockedCount == 0 ? $"Buy{targetWord}" : $"Buy Rest ({sim.BuyOrder.Count})";
					group.BuyButtonText.text = $"{prefix} ({sim.TotalXPCost} XP)";
				}
				else if (sim.BuyOrder.Count > 0)
				{
					group.BuyButton.interactable = true;
					group.BuyButtonText.text = $"Buy {sim.BuyOrder.Count}/{remLocked} ({sim.TotalXPCost} XP)";
				}
				else
				{
					group.BuyButton.interactable = false;
					if (sim.NextBlockedByXP < int.MaxValue)
					{
						group.BuyButtonText.text = $"Need {sim.NextBlockedByXP} XP";
					}
					else if (sim.NextBlockedByCardCount < int.MaxValue)
					{
						int more = sim.NextBlockedByCardCount - UpgradeManager.instance.unlockedCardCount;
						group.BuyButtonText.text = $"Need +{more} Cards";
					}
					else
					{
						group.BuyButtonText.text = "Locked";
					}
				}
			}

			if (group.RefundButton != null && group.RefundButtonText != null)
			{
				int refundXP = CalculateRefundableXP(group.Nodes, out int unlockedCount);
				if (unlockedCount > 0)
				{
					group.RefundButton.interactable = true;
					group.RefundButtonText.text = $"Refund{labelWithSpace} (+{refundXP} XP)";
				}
				else
				{
					group.RefundButton.interactable = false;
					group.RefundButtonText.text = $"Refund{labelWithSpace}";
				}
			}
		}

		if (_refundAllButton != null && _refundAllText != null)
		{
			int totalRefundXP = CalculateRefundableXP(AllActiveButtons, out int totalUnlocked);
			if (totalUnlocked > 0)
			{
				_refundAllButton.interactable = true;
				_refundAllText.text = $"Refund All (+{totalRefundXP} XP)";
			}
			else
			{
				_refundAllButton.interactable = false;
				_refundAllText.text = "Refund All (0 XP)";
			}
		}

		if (_researchAllButton != null && _researchAllText != null)
		{
			BuySimulationResult sim = SimulateSmartBuy(AllActiveButtons, isTowerGroup: false, priorityAncestors: null, ignoreFilters: false);
			if (sim.RemainingLockedCount <= 0)
			{
				sim = SimulateSmartBuy(AllActiveButtons, isTowerGroup: false, priorityAncestors: null, ignoreFilters: true);
			}

			int remLocked = sim.RemainingLockedCount;
			int totalLockedXPCost = 0;
			for (int i = 0; i < AllActiveButtons.Count; i++)
			{
				UpgradeButton b = AllActiveButtons[i];
				if (IsValidNode(b) && !b.unlocked)
				{
					totalLockedXPCost += b.xpCost;
				}
			}

			if (remLocked <= 0)
			{
				_researchAllButton.interactable = false;
				_researchAllText.text = "All Researched!";
			}
			else if (sim.BuyOrder.Count == remLocked)
			{
				_researchAllButton.interactable = true;
				_researchAllText.text = $"Research All ({sim.TotalXPCost} XP)";
			}
			else if (sim.BuyOrder.Count > 0)
			{
				_researchAllButton.interactable = true;
				_researchAllText.text = $"Research {sim.BuyOrder.Count}/{remLocked} ({sim.TotalXPCost} XP)";
			}
			else
			{
				_researchAllButton.interactable = false;
				_researchAllText.text = sim.NextBlockedByXP < int.MaxValue
					? $"Need {sim.NextBlockedByXP} XP (All: {totalLockedXPCost})"
					: "Locked";
			}
		}
	}

	internal static bool TryHandleLeftClickNode(UpgradeButton clicked)
	{
		if (!Plugin.EnableModifierClicks.Value || IsExecutingBulkAction || !IsValidNode(clicked))
		{
			return false;
		}
		if (LastModifierClickFrame == Time.frameCount)
		{
			return true;
		}
		LastModifierClickFrame = Time.frameCount;

		bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
		bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

		ButtonToMainGroup.TryGetValue(clicked, out var mainGroup);
		bool isTower = mainGroup != null && mainGroup.IsTowerGroup;

		if (!clicked.unlocked)
		{
			if (shift && ctrl && mainGroup != null)
			{
				// Ctrl+Shift+Click on any locked node: Smart Buy the entire group tree
				ExecuteSmartBuy(mainGroup.Nodes, isTower);
				return true;
			}
			if (shift)
			{
				// Shift+Click on a locked node: Smart Buy its prerequisites + this node + all of its descendants (or whole tower if root)
				List<UpgradeButton> ancestors = CollectAncestorsAndSelf(clicked);
				HashSet<UpgradeButton> prio = new HashSet<UpgradeButton>(ancestors);
				if (PreviousRef(clicked) == null && mainGroup != null && mainGroup.IsTowerGroup)
				{
					ExecuteSmartBuy(mainGroup.Nodes, isTower, prio);
				}
				else
				{
					List<UpgradeButton> branch = CollectBranchWithPrerequisites(clicked);
					ExecuteSmartBuy(branch, isTower, prio);
				}
				return true;
			}

			// Normal Left-Click (or Ctrl+Click) on ANY locked card:
			// Auto-buy the prerequisite chain from the start of the tree/branch up to and including the card you clicked!
			List<UpgradeButton> path = CollectAncestorsAndSelf(clicked);
			HashSet<UpgradeButton> pathPrio = new HashSet<UpgradeButton>(path);
			ExecuteSmartBuy(path, isTower, pathPrio, ignoreFilters: true);
			return true;
		}
		else
		{
			if (shift && mainGroup != null)
			{
				// Shift+Click on an unlocked node: Smart Refund the entire group tree
				ExecuteSmartRefund(mainGroup.Nodes);
				return true;
			}

			// Normal Left-Click on an unlocked card: refund this card and all unlocked cards after it in the branch!
			ExecuteSmartRefund(CollectDescendantsAndSelf(clicked));
			return true;
		}
	}

	internal static void HandleRightClickNode(UpgradeButton clicked, TreeGroupUI mainGroup, TreeGroupUI subGroup)
	{
		if (IsExecutingBulkAction || clicked == null)
		{
			return;
		}
		if (LastModifierClickFrame == Time.frameCount)
		{
			return;
		}
		LastModifierClickFrame = Time.frameCount;

		// If right-clicking a default-unlocked root card (Tesla Coil, Mortar, Ballista, Mana Siphon): refund all unlocked upgrades on that tower/group
		if (!clicked.enabled)
		{
			if (mainGroup != null)
			{
				ExecuteSmartRefund(mainGroup.Nodes);
			}
			return;
		}

		bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
		if (shift && mainGroup != null)
		{
			// Shift + Right-Click: refund the entire tree
			ExecuteSmartRefund(mainGroup.Nodes);
			return;
		}

		// Normal Right-Click on an unlocked card: refund from the end of the branch back up to the card you right-clicked!
		// (If you right-click a tower root card like Cannon or Frost Keep, CollectDescendantsAndSelf refunds all branches on that tower!)
		if (IsValidNode(clicked) && clicked.unlocked)
		{
			ExecuteSmartRefund(CollectDescendantsAndSelf(clicked));
		}
	}

	internal static void HandleDefaultRootLeftClick(TreeGroupUI mainGroup)
	{
		if (IsExecutingBulkAction || mainGroup == null)
		{
			return;
		}
		if (LastModifierClickFrame == Time.frameCount)
		{
			return;
		}
		LastModifierClickFrame = Time.frameCount;

		bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
		if (shift)
		{
			ExecuteSmartBuy(mainGroup.Nodes, mainGroup.IsTowerGroup);
			return;
		}
		// Clicking an already-unlocked default root card (Tesla Coil, Mortar, Ballista) refunds all unlocked nodes in that tree (matching RefundUpgrades on other towers!)
		CalculateRefundableXP(mainGroup.Nodes, out int unlockedCount);
		if (unlockedCount > 0)
		{
			ExecuteSmartRefund(mainGroup.Nodes);
		}
	}
}

[HarmonyPatch]
internal static class QuickTreePatches
{
	[HarmonyPatch(typeof(UpgradeButton), "Start")]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static void UpgradeButton_Start_Prefix(UpgradeButton __instance)
	{
		// Fix VampireLair/Monument/ParticleCannon Armor2 & Armor3 unlockStrings BEFORE CheckUnlock() and RefundUpgrades run!
		QuickTreeManager.FixVanillaArmorUnlockString(__instance);
		QuickTreeManager.RecordLockedSprite(__instance);
	}

	[HarmonyPatch(typeof(UpgradeManager), "Start")]
	[HarmonyPostfix]
	private static void UpgradeManager_Start_Postfix(UpgradeManager __instance)
	{
		__instance.StartCoroutine(DelayedInit(__instance));
	}

	private static IEnumerator DelayedInit(UpgradeManager manager)
	{
		// Wait slightly past UpgradeButton.LateStart (0.1s) so all price tags and CardDataLib fixes are initialized
		yield return new WaitForSeconds(0.15f);
		QuickTreeManager.InitializeUpgradeMenu(manager);
	}

	[HarmonyPatch(typeof(UpgradeButton), "Unlock")]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool UpgradeButton_Unlock_Prefix(UpgradeButton __instance)
	{
		if (QuickTreeManager.TryHandleLeftClickNode(__instance))
		{
			return false;
		}
		return true;
	}

	[HarmonyPatch(typeof(UpgradeButton), "Unlock")]
	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	private static void UpgradeButton_Unlock_Postfix()
	{
		if (!QuickTreeManager.IsExecutingBulkAction)
		{
			QuickTreeManager.RefreshAllUI();
		}
	}

	[HarmonyPatch(typeof(UpgradeManager), "AddXP")]
	[HarmonyPostfix]
	private static void UpgradeManager_AddXP_Postfix()
	{
		if (!QuickTreeManager.IsExecutingBulkAction)
		{
			QuickTreeManager.RefreshAllUI();
		}
	}

	[HarmonyPatch(typeof(PlayMenu), "Start")]
	[HarmonyPostfix]
	private static void PlayMenu_Start_Postfix(PlayMenu __instance)
	{
		InGameAutoMaxManager.SetupPlayMenuButton(__instance);
	}

	[HarmonyPatch(typeof(TileManager), "Start")]
	[HarmonyPostfix]
	private static void TileManager_Start_Postfix()
	{
		InGameAutoMaxManager.OnNewGameStart();
	}

	[HarmonyPatch(typeof(CardManager), nameof(CardManager.ActivateCard))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.High)]
	private static void CardManager_ActivateCard_Prefix(CardManager __instance, int index)
	{
		InGameAutoMaxManager.PrepareChainBeforeActivate(__instance, index);
	}

	[HarmonyPatch(typeof(CardManager), nameof(CardManager.DrawCards), typeof(int))]
	[HarmonyPrefix]
	[HarmonyPriority(Priority.High)]
	private static bool CardManager_DrawCards_Prefix(CardManager __instance)
	{
		InGameAutoMaxManager.ApplyPendingChain(__instance);
		if (InGameAutoMaxManager.ShouldSkipEmptyDraw(__instance))
		{
			if (!__instance.drawingCards && SpawnManager.instance != null)
			{
				SpawnManager.instance.ShowSpawnUIs(state: true);
			}
			return false;
		}
		return true;
	}

	[HarmonyPatch(typeof(CardManager), nameof(CardManager.ActivateCard))]
	[HarmonyPostfix]
	[HarmonyPriority(Priority.High)]
	private static void CardManager_ActivateCard_Postfix(CardManager __instance)
	{
		InGameAutoMaxManager.ApplyPendingChain(__instance);
	}

	[HarmonyPatch(typeof(UIManager), "Update")]
	[HarmonyPrefix]
	private static bool UIManager_Update_Prefix()
	{
		if (InGameAutoMaxManager.IsPointerOverRoundStartButton())
		{
			return false;
		}
		return true;
	}

	private static Type _refundUpgradesType;
	private static bool _refundUpgradesChecked;

	private static Type GetRefundUpgradesType()
	{
		if (!_refundUpgradesChecked)
		{
			_refundUpgradesChecked = true;
			_refundUpgradesType = AccessTools.TypeByName("RogueTower_RefundUpgrades.Plugin");
		}
		return _refundUpgradesType;
	}

	internal static void InstallOptionalRefundUpgradesPatch(Harmony harmony)
	{
		Type t = GetRefundUpgradesType();
		if (t == null || harmony == null) return;
		MethodInfo target = AccessTools.Method(t, "RefundUpgrade");
		MethodInfo pre = typeof(QuickTreePatches).GetMethod(nameof(RefundUpgrades_RefundUpgrade_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
		if (target != null && pre != null)
		{
			harmony.Patch(target, prefix: new HarmonyMethod(pre));
		}
	}

	internal static Sprite TryGetRefundUpgradesSprite(UpgradeButton btn, string unlockStr)
	{
		Type t = GetRefundUpgradesType();
		if (t == null || btn == null) return null;
		if (AccessTools.Field(t, "lockedButtonSprites")?.GetValue(null) is Dictionary<UpgradeButton, Sprite> btnMap &&
			btnMap.TryGetValue(btn, out Sprite s1) && s1 != null)
		{
			return s1;
		}
		if (!string.IsNullOrEmpty(unlockStr) &&
			AccessTools.Field(t, "lockedSprites")?.GetValue(null) is Dictionary<string, Sprite> strMap &&
			strMap.TryGetValue(unlockStr, out Sprite s3) && s3 != null)
		{
			return s3;
		}
		return null;
	}

	private static bool RefundUpgrades_RefundUpgrade_Prefix()
	{
		if (QuickTreeManager.LastModifierClickFrame == Time.frameCount || QuickTreeManager.IsExecutingBulkAction)
		{
			return false;
		}
		return true;
	}
}

internal sealed class QuickTreeNodeClickHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
	public UpgradeButton TargetButton;
	public QuickTreeManager.TreeGroupUI TargetGroup;
	public QuickTreeManager.TreeGroupUI SubGroup;

	private Vector2 _leftDownScreenPos;
	private bool _leftButtonDown;
	private Vector2 _rightDownScreenPos;
	private bool _rightButtonDown;

	public void OnPointerDown(PointerEventData eventData)
	{
		if (eventData == null)
		{
			return;
		}
		if (eventData.button == PointerEventData.InputButton.Left)
		{
			_leftButtonDown = true;
			_leftDownScreenPos = eventData.position;
		}
		else if (eventData.button == PointerEventData.InputButton.Right)
		{
			_rightButtonDown = true;
			_rightDownScreenPos = eventData.position;
		}
	}

	public void OnPointerUp(PointerEventData eventData)
	{
		if (eventData == null)
		{
			return;
		}
		if (eventData.button == PointerEventData.InputButton.Right && _rightButtonDown)
		{
			_rightButtonDown = false;
			// Only trigger Right-Click refund if the user clicked rather than dragging to pan the camera
			if (Vector2.Distance(_rightDownScreenPos, eventData.position) <= 15f)
			{
				QuickTreeManager.HandleRightClickNode(TargetButton, TargetGroup, SubGroup);
			}
		}
	}

	public void OnPointerClick(PointerEventData eventData)
	{
		if (eventData == null || TargetButton == null)
		{
			return;
		}
		if (eventData.button == PointerEventData.InputButton.Left)
		{
			if (_leftButtonDown && Vector2.Distance(_leftDownScreenPos, eventData.position) > 15f)
			{
				_leftButtonDown = false;
				return;
			}
			_leftButtonDown = false;

			// If this is a default-unlocked root card (Ballista, Mortar, Tesla Coil) where UpgradeButton.enabled is false:
			if (!TargetButton.enabled)
			{
				QuickTreeManager.HandleDefaultRootLeftClick(TargetGroup);
				return;
			}
			// Auto-buy prerequisite chain up to TargetButton even when Button.interactable is false (deep in a locked branch)!
			QuickTreeManager.TryHandleLeftClickNode(TargetButton);
		}
	}
}

internal static class InGameAutoMaxManager
{
	internal static AutoMaxBranchMode CurrentMode = AutoMaxBranchMode.SameUpgradeChain;

	private static readonly AccessTools.FieldRef<CardManager, UpgradeCard[]> CardsRef =
		AccessTools.FieldRefAccess<CardManager, UpgradeCard[]>("cards");

	private static readonly AccessTools.FieldRef<CardManager, List<UpgradeCard>> AvailableCardsRef =
		AccessTools.FieldRefAccess<CardManager, List<UpgradeCard>>("availableCards");

	private static readonly AccessTools.FieldRef<CardManager, List<UpgradeCard>> StartCardsCheckRef =
		AccessTools.FieldRefAccess<CardManager, List<UpgradeCard>>("startCardsCheck");

	private static readonly AccessTools.FieldRef<TowerUpgradeCard, TowerType> CardTowerTypeRef =
		AccessTools.FieldRefAccess<TowerUpgradeCard, TowerType>("towerType");

	private static readonly List<UpgradeCard> PendingChain = new List<UpgradeCard>();
	private static readonly List<UpgradeCard> PendingSideUnlocks = new List<UpgradeCard>();
	private static bool _allResearchedAppliedThisRun;

	private static Text _playMenuBtnText;
	private static Image _playMenuBtnImage;
	private static Color _playMenuDefaultColor = Color.white;

	private static GameObject _roundStartBtnObj;
	private static Text _roundStartBtnText;
	private static Image _roundStartBtnImage;
	private static RectTransform _roundStartBtnRect;
	private static Color _roundStartDefaultColor = Color.white;

	private static readonly Color ActiveHighlightColor = new Color(1.0f, 0.88f, 0.55f, 1.0f);

	private static readonly string[] RomanSuffixes =
	{
		" VIII", " VII", " IX", " VI", " IV", " III", " II", " V", " X", " I"
	};

	internal static void SetupPlayMenuButton(PlayMenu playMenu)
	{
		if (playMenu == null)
		{
			return;
		}

		if (Plugin.ResetAutoMaxEachRun.Value)
		{
			CurrentMode = AutoMaxBranchMode.Off;
		}

		Transform scaler = playMenu.transform.Find("Scaler");
		if (scaler == null)
		{
			return;
		}

		Transform existing = scaler.Find("AutoMaxBranchToggleButton");
		GameObject btnObj;
		if (existing != null)
		{
			btnObj = existing.gameObject;
		}
		else
		{
			Transform backBtnTr = scaler.Find("BackButton");
			if (backBtnTr == null)
			{
				Plugin.Log.LogWarning("Could not find BackButton under PlayMenu/Scaler to clone AutoMaxBranchToggleButton.");
				return;
			}

			btnObj = UnityEngine.Object.Instantiate(backBtnTr.gameObject, scaler);
			btnObj.name = "AutoMaxBranchToggleButton";

			Button btn = btnObj.GetComponent<Button>();
			btn.onClick = new Button.ButtonClickedEvent();
			btn.onClick.AddListener(CycleMode);
		}

		RectTransform rect = btnObj.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2(0.5f, 0.5f);
		rect.anchorMax = new Vector2(0.5f, 0.5f);
		rect.pivot = new Vector2(0.5f, 0.5f);
		// Side-by-side with Map selector cleanly above Single/Double/Triple/Quadruple (y=+50..+150)
		rect.anchoredPosition = new Vector2(145f, 180f);
		rect.sizeDelta = new Vector2(280f, 34f);
		rect.localScale = Vector3.one;

		_playMenuBtnImage = btnObj.GetComponent<Image>();
		if (_playMenuBtnImage != null && existing == null)
		{
			_playMenuDefaultColor = _playMenuBtnImage.color;
		}

		_playMenuBtnText = btnObj.GetComponentInChildren<Text>(includeInactive: true);
		ConfigureButtonText(_playMenuBtnText, 120);
		RefreshAllButtons();
	}

	internal static void OnNewGameStart()
	{
		PendingChain.Clear();
		PendingSideUnlocks.Clear();
		_allResearchedAppliedThisRun = false;
		_roundStartBtnObj = null;
		_roundStartBtnText = null;
		_roundStartBtnImage = null;
		_roundStartBtnRect = null;
		EnsureRoundStartButtonCreated();
		RefreshAllButtons();
	}

	internal static void Tick()
	{
		if (GameManager.instance == null)
		{
			return;
		}

		EnsureRoundStartButtonCreated();

		if (CurrentMode == AutoMaxBranchMode.AllResearched &&
			!_allResearchedAppliedThisRun &&
			CardManager.instance != null &&
			AvailableCardsRef(CardManager.instance) is List<UpgradeCard> avail &&
			avail.Count > 0)
		{
			UnlockAllResearchedCardsNow(CardManager.instance);
		}

		if (_roundStartBtnObj == null)
		{
			return;
		}

		bool isRoundStartPreCombat = SpawnManager.instance != null && SpawnManager.instance.level <= 1 && !SpawnManager.instance.combat;
		bool isPaused = PauseMenu.instance != null && PauseMenu.instance.paused;
		bool shouldShow = Plugin.ShowInGameAutoMaxHudButton.Value && !GameManager.instance.gameOver && !isPaused && isRoundStartPreCombat;

		if (_roundStartBtnObj.activeSelf != shouldShow)
		{
			_roundStartBtnObj.SetActive(shouldShow);
		}
	}

	internal static bool IsPointerOverRoundStartButton()
	{
		if (_roundStartBtnObj != null && _roundStartBtnObj.activeInHierarchy && _roundStartBtnRect != null)
		{
			if (RectTransformUtility.RectangleContainsScreenPoint(_roundStartBtnRect, Input.mousePosition, null))
			{
				return true;
			}
		}
		return false;
	}

	internal static bool ShouldSkipEmptyDraw(CardManager cm)
	{
		if (cm == null)
		{
			return false;
		}
		List<UpgradeCard> available = AvailableCardsRef(cm);
		return available != null && available.Count == 0 && (CurrentMode == AutoMaxBranchMode.AllResearched || _allResearchedAppliedThisRun);
	}

	private static readonly AccessTools.FieldRef<UpgradeCard, string> CardUnlockNameRef =
		AccessTools.FieldRefAccess<UpgradeCard, string>("unlockName");

	private static MethodInfo _extSyncByUnlockName;
	private static MethodInfo _extSyncByTitle;
	private static bool _extSyncLookedUp;

	private static void NotifyUpgradesMenuCardUnlocked(UpgradeCard card)
	{
		if (card == null)
		{
			return;
		}
		if (!_extSyncLookedUp)
		{
			_extSyncLookedUp = true;
			Type t = AccessTools.TypeByName("UpgradesMenu.StateManagement.Runtime.ExternalCardSync");
			if (t != null)
			{
				_extSyncByUnlockName = t.GetMethod("MarkCardUnlocked", BindingFlags.Public | BindingFlags.Static);
				_extSyncByTitle = t.GetMethod("MarkCardUnlockedByTitle", BindingFlags.Public | BindingFlags.Static);
			}
		}
		string unlockName = CardUnlockNameRef(card);
		if (!string.IsNullOrEmpty(unlockName) && _extSyncByUnlockName != null)
		{
			_extSyncByUnlockName.Invoke(null, new object[] { unlockName, false });
		}
		else if (!string.IsNullOrEmpty(card.title) && _extSyncByTitle != null)
		{
			_extSyncByTitle.Invoke(null, new object[] { card.title, false });
		}
	}

	internal static void UnlockAllResearchedCardsNow(CardManager cm)
	{
		if (cm == null || CurrentMode != AutoMaxBranchMode.AllResearched || _allResearchedAppliedThisRun)
		{
			return;
		}

		List<UpgradeCard> startCards = StartCardsCheckRef(cm);
		List<UpgradeCard> availableCards = AvailableCardsRef(cm);
		if (startCards == null || availableCards == null)
		{
			return;
		}

		List<UpgradeCard> allUnlockedReachable = CollectAllReachableUnlocked(startCards, availableCards);
		_allResearchedAppliedThisRun = true;

		for (int i = 0; i < allUnlockedReachable.Count; i++)
		{
			UpgradeCard c = allUnlockedReachable[i];
			if (c != null)
			{
				c.Upgrade();
				availableCards.RemoveAll(x => x == c);
				NotifyUpgradesMenuCardUnlocked(c);
			}
		}

		Plugin.Log.LogInfo($"Auto-Max Branch (AllResearched): auto-activated {allUnlockedReachable.Count} researched card(s).");
	}

	private static List<UpgradeCard> CollectAllReachableUnlocked(List<UpgradeCard> startCards, List<UpgradeCard> availableCards, HashSet<UpgradeCard> alreadyVisited = null)
	{
		List<UpgradeCard> collected = new List<UpgradeCard>(128);
		HashSet<UpgradeCard> visited = alreadyVisited ?? new HashSet<UpgradeCard>();
		Queue<UpgradeCard> queue = new Queue<UpgradeCard>();

		void SeedList(List<UpgradeCard> list)
		{
			if (list == null)
			{
				return;
			}
			for (int i = 0; i < list.Count; i++)
			{
				UpgradeCard c = list[i];
				if (c != null && c.unlocked && visited.Add(c))
				{
					collected.Add(c);
					queue.Enqueue(c);
				}
			}
		}

		SeedList(startCards);
		SeedList(availableCards);

		while (queue.Count > 0)
		{
			UpgradeCard cur = queue.Dequeue();
			if (cur.unlocks == null)
			{
				continue;
			}
			for (int i = 0; i < cur.unlocks.Count; i++)
			{
				UpgradeCard child = cur.unlocks[i];
				if (child != null && child.unlocked && visited.Add(child))
				{
					collected.Add(child);
					queue.Enqueue(child);
				}
			}
		}

		return collected;
	}

	internal static void PrepareChainBeforeActivate(CardManager cm, int index)
	{
		PendingChain.Clear();
		PendingSideUnlocks.Clear();

		if (CurrentMode == AutoMaxBranchMode.Off || cm == null)
		{
			return;
		}

		UpgradeCard[] cards = CardsRef(cm);
		List<UpgradeCard> availableCards = AvailableCardsRef(cm);
		if (cards == null || availableCards == null || index < 0 || index >= cards.Length)
		{
			return;
		}

		UpgradeCard picked = cards[index];
		if (picked == null)
		{
			return;
		}

		if (picked is TowerUnlockCard && CurrentMode != AutoMaxBranchMode.WholeTowerTree && CurrentMode != AutoMaxBranchMode.AllResearched)
		{
			return;
		}

		HashSet<UpgradeCard> visited = new HashSet<UpgradeCard> { picked };
		Queue<UpgradeCard> queue = new Queue<UpgradeCard>();
		queue.Enqueue(picked);

		if (CurrentMode == AutoMaxBranchMode.WholeTowerTree)
		{
			if (picked is TowerUpgradeCard pickedTowerCard)
			{
				TowerType targetTower = CardTowerTypeRef(pickedTowerCard);
				void SeedSameTower(List<UpgradeCard> list)
				{
					if (list == null)
					{
						return;
					}
					for (int i = 0; i < list.Count; i++)
					{
						if (list[i] is TowerUpgradeCard tuc && tuc.unlocked && CardTowerTypeRef(tuc) == targetTower && visited.Add(tuc))
						{
							PendingChain.Add(tuc);
							queue.Enqueue(tuc);
						}
					}
				}
				SeedSameTower(StartCardsCheckRef(cm));
				SeedSameTower(availableCards);
			}
		}
		else if (CurrentMode == AutoMaxBranchMode.AllResearched)
		{
			List<UpgradeCard> rest = CollectAllReachableUnlocked(StartCardsCheckRef(cm), availableCards, visited);
			PendingChain.AddRange(rest);
			queue.Clear();
		}

		string baseTitle = GetBaseCardTitle(picked.title);

		while (queue.Count > 0)
		{
			UpgradeCard cur = queue.Dequeue();
			if (cur.unlocks == null)
			{
				continue;
			}

			for (int i = 0; i < cur.unlocks.Count; i++)
			{
				UpgradeCard child = cur.unlocks[i];
				if (child == null || !child.unlocked || visited.Contains(child))
				{
					continue;
				}

				if (CurrentMode == AutoMaxBranchMode.FullBranchSubtree || CurrentMode == AutoMaxBranchMode.WholeTowerTree)
				{
					visited.Add(child);
					PendingChain.Add(child);
					queue.Enqueue(child);
				}
				else
				{
					bool matchesBase = string.Equals(GetBaseCardTitle(child.title), baseTitle, StringComparison.OrdinalIgnoreCase);
					bool singleLinearContinuation = cur.unlocks.Count == 1;
					bool leafCapstone = child.unlocks == null || child.unlocks.Count == 0;

					if (matchesBase || singleLinearContinuation || leafCapstone)
					{
						visited.Add(child);
						PendingChain.Add(child);
						queue.Enqueue(child);
					}
					else if (!PendingSideUnlocks.Contains(child))
					{
						PendingSideUnlocks.Add(child);
					}
				}
			}
		}

		// Ensure UpgradesMenu's ActivateCard Prefix snapshots every auto-grabbed card so they all light up in Tab menu
		for (int i = 0; i < PendingChain.Count; i++)
		{
			UpgradeCard c = PendingChain[i];
			if (!availableCards.Contains(c))
			{
				availableCards.Add(c);
			}
		}
	}

	internal static void ApplyPendingChain(CardManager cm)
	{
		if (PendingChain.Count == 0 && PendingSideUnlocks.Count == 0)
		{
			return;
		}

		List<UpgradeCard> availableCards = cm != null ? AvailableCardsRef(cm) : null;

		for (int i = 0; i < PendingChain.Count; i++)
		{
			UpgradeCard c = PendingChain[i];
			if (c != null)
			{
				c.Upgrade();
				availableCards?.RemoveAll(x => x == c);
				NotifyUpgradesMenuCardUnlocked(c);
			}
		}

		for (int i = 0; i < PendingSideUnlocks.Count; i++)
		{
			UpgradeCard side = PendingSideUnlocks[i];
			if (side != null && side.unlocked && !PendingChain.Contains(side) && availableCards != null && !availableCards.Contains(side))
			{
				availableCards.Add(side);
			}
		}

		if (PendingChain.Count > 0)
		{
			Plugin.Log.LogInfo($"Auto-Max Branch ({CurrentMode}): granted +{PendingChain.Count} chained upgrade(s).");
		}

		PendingChain.Clear();
		PendingSideUnlocks.Clear();
	}

	private static string GetBaseCardTitle(string title)
	{
		if (string.IsNullOrEmpty(title))
		{
			return string.Empty;
		}

		string trimmed = title.Trim();
		for (int i = 0; i < RomanSuffixes.Length; i++)
		{
			string suffix = RomanSuffixes[i];
			if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				return trimmed.Substring(0, trimmed.Length - suffix.Length).TrimEnd();
			}
		}
		return trimmed;
	}

	internal static void SetMode(int mode)
	{
		CurrentMode = (AutoMaxBranchMode)Mathf.Clamp(mode, 0, 4);
		if (!Plugin.ResetAutoMaxEachRun.Value)
		{
			Plugin.DefaultAutoMaxMode.Value = CurrentMode;
		}
		if (CurrentMode == AutoMaxBranchMode.AllResearched && GameManager.instance != null && CardManager.instance != null)
		{
			UnlockAllResearchedCardsNow(CardManager.instance);
		}
		RefreshAllButtons();
	}

	private static void CycleMode()
	{
		SetMode(((int)CurrentMode + 1) % 5);
		if (SFXManager.instance != null)
		{
			SFXManager.instance.ButtonClick();
		}
	}

	private static string GetModeLabel()
	{
		return CurrentMode switch
		{
			AutoMaxBranchMode.SameUpgradeChain => "Auto-Max: Branch Line",
			AutoMaxBranchMode.FullBranchSubtree => "Auto-Max: Full Subtree",
			AutoMaxBranchMode.WholeTowerTree => "Auto-Max: Whole Tower",
			AutoMaxBranchMode.AllResearched => "Auto-Max: ALL Researched!",
			_ => "Auto-Max: OFF"
		};
	}

	private static void RefreshAllButtons()
	{
		string label = GetModeLabel();
		bool isActive = CurrentMode != AutoMaxBranchMode.Off;

		if (_playMenuBtnText != null)
		{
			_playMenuBtnText.text = label;
		}
		if (_playMenuBtnImage != null)
		{
			_playMenuBtnImage.color = isActive ? ActiveHighlightColor : _playMenuDefaultColor;
		}

		if (_roundStartBtnText != null)
		{
			_roundStartBtnText.text = label;
		}
		if (_roundStartBtnImage != null)
		{
			_roundStartBtnImage.color = isActive ? ActiveHighlightColor : _roundStartDefaultColor;
		}
	}

	private static void EnsureRoundStartButtonCreated()
	{
		if (_roundStartBtnObj != null || GameManager.instance == null)
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

		_roundStartBtnObj = UnityEngine.Object.Instantiate(templateBtn.gameObject, topSide);
		_roundStartBtnObj.name = "RoundStartAutoMaxButton";

		_roundStartBtnRect = _roundStartBtnObj.GetComponent<RectTransform>();
		_roundStartBtnRect.anchorMin = new Vector2(0.5f, 1.0f);
		_roundStartBtnRect.anchorMax = new Vector2(0.5f, 1.0f);
		_roundStartBtnRect.pivot = new Vector2(0.5f, 0.5f);
		// Sit cleanly below SpeedControls' Pause / Play buttons (which are around y = -168)
		_roundStartBtnRect.anchoredPosition = new Vector2(0f, -210f);
		_roundStartBtnRect.sizeDelta = new Vector2(255f, 30f);
		_roundStartBtnRect.localScale = Vector3.one;

		Button btn = _roundStartBtnObj.GetComponent<Button>();
		btn.onClick = new Button.ButtonClickedEvent();
		btn.onClick.AddListener(CycleMode);

		_roundStartBtnImage = _roundStartBtnObj.GetComponent<Image>();
		if (_roundStartBtnImage != null)
		{
			_roundStartDefaultColor = _roundStartBtnImage.color;
		}

		_roundStartBtnText = _roundStartBtnObj.GetComponentInChildren<Text>(includeInactive: true);
		ConfigureButtonText(_roundStartBtnText, 115);

		_roundStartBtnObj.SetActive(false);
		RefreshAllButtons();
	}

	private static void ConfigureButtonText(Text txt, int fontSize)
	{
		if (txt == null)
		{
			return;
		}
		txt.resizeTextForBestFit = false;
		txt.fontSize = fontSize;
		txt.alignment = TextAnchor.MiddleCenter;
		txt.horizontalOverflow = HorizontalWrapMode.Overflow;
		txt.verticalOverflow = VerticalWrapMode.Overflow;
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
