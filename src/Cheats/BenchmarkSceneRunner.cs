using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RogueTowerCheats;

internal sealed class BenchmarkPreset
{
	public readonly string Id;
	public readonly string DisplayName;
	public readonly int GameMode;
	public readonly int TargetWave;
	public readonly int TargetPortals;
	public readonly bool ForceSplitMode;
	public readonly int TowerCount;
	public readonly int UniformPerTowerType;
	public readonly int TargetTowerLevel;
	public readonly float TimeScale;
	public readonly bool EnableInfRange;
	public readonly bool AutoChainWaves;
	public readonly float DefaultDurationSec;

	public BenchmarkPreset(
		string id,
		string displayName,
		int gameMode,
		int targetWave,
		int targetPortals,
		bool forceSplitMode,
		int towerCount,
		int targetTowerLevel,
		float timeScale,
		int uniformPerTowerType = 0,
		bool enableInfRange = false,
		bool autoChainWaves = false,
		float defaultDurationSec = 12f)
	{
		Id = id;
		DisplayName = displayName;
		GameMode = gameMode;
		TargetWave = targetWave;
		TargetPortals = targetPortals;
		ForceSplitMode = forceSplitMode;
		TowerCount = towerCount;
		UniformPerTowerType = uniformPerTowerType;
		TargetTowerLevel = targetTowerLevel;
		TimeScale = timeScale;
		EnableInfRange = enableInfRange;
		AutoChainWaves = autoChainWaves;
		DefaultDurationSec = defaultDurationSec;
	}
}

internal static class BenchmarkSceneRunner
{
	internal static readonly BenchmarkPreset[] Presets =
	{
		new BenchmarkPreset("r40_vanilla_avg", "R40 Vanilla Avg (8 Portals, L40)", 3, 40, 0, false, 65, 40, 1f, 0, false, false, 12f),
		new BenchmarkPreset("r40_high_12p", "R40 High-Roll (~12-14 Portals, L40)", 4, 40, 13, false, 85, 40, 1f, 0, false, false, 12f),
		new BenchmarkPreset("r40_extreme_20p", "R40 Split-Mode (~20-22 Portals, L40)", 4, 40, 21, true, 110, 40, 1f, 0, false, true, 12f),
		new BenchmarkPreset("r45_boss_chaos", "R45 Boss + 20p @ 3x Speed (L40)", 4, 45, 22, true, 125, 40, 3f, 0, false, true, 12f),
		new BenchmarkPreset("r45_omega_all20_infrange", "Omega: 20x Every Tower (300, L40) + Max Trees + InfRange @ 8x", 4, 45, 22, true, 300, 40, 8f, 20, true, true, 20f)
	};

	private static readonly AccessTools.FieldRef<SpawnManager, HashSet<Waypoint>> SpawnPointsRef =
		AccessTools.FieldRefAccess<SpawnManager, HashSet<Waypoint>>("spawnPoints");
	private static readonly AccessTools.FieldRef<SpawnManager, bool> EnemiesLeftToSpawnRef =
		AccessTools.FieldRefAccess<SpawnManager, bool>("enemiesLeftToSpawn");
	private static readonly AccessTools.FieldRef<BuildButtonUI, GameObject> BuildButtonTowerRef =
		AccessTools.FieldRefAccess<BuildButtonUI, GameObject>("tower");
	private static readonly AccessTools.FieldRef<BuildingManager, LayerMask> BuildableMaskRef =
		AccessTools.FieldRefAccess<BuildingManager, LayerMask>("buildableMask");

	private static readonly AccessTools.FieldRef<Tower, int> TowerLevelRef =
		AccessTools.FieldRefAccess<Tower, int>("<level>k__BackingField");
	private static readonly AccessTools.FieldRef<Tower, int> TowerDmgUpRef =
		AccessTools.FieldRefAccess<Tower, int>("damageUpgrade");
	private static readonly AccessTools.FieldRef<Tower, int> TowerHealthUpRef =
		AccessTools.FieldRefAccess<Tower, int>("healthDamageUpgrade");
	private static readonly AccessTools.FieldRef<Tower, int> TowerArmorUpRef =
		AccessTools.FieldRefAccess<Tower, int>("armorDamageUpgrade");
	private static readonly AccessTools.FieldRef<Tower, int> TowerShieldUpRef =
		AccessTools.FieldRefAccess<Tower, int>("shieldDamageUpgrade");

	private static readonly List<float> SampleFrameTimes = new List<float>(4096);
	private static int _enemySamples;
	private static long _enemySum;
	private static int _peakEnemies;
	private static int _wavesChained;
	private static bool _autoChainActive;
	private static float _lockedTimeScale = -1f;

	private static readonly object StatusLock = new object();
	internal static volatile bool IsBusy;
	internal static volatile string Phase = "idle";
	internal static volatile string ActivePresetId = "";
	internal static volatile string LastError = "";
	internal static volatile string LastReportJson = "null";
	internal static volatile string LastReportPath = "";

	internal static BenchmarkPreset FindPreset(string presetId)
	{
		if (string.IsNullOrWhiteSpace(presetId)) return Presets[0];
		string key = presetId.Trim();
		foreach (BenchmarkPreset p in Presets)
		{
			if (string.Equals(p.Id, key, StringComparison.OrdinalIgnoreCase)) return p;
		}
		return null;
	}

	internal static int GetActivePortalCount()
	{
		if (SpawnManager.instance == null) return 0;
		HashSet<Waypoint> pts = SpawnPointsRef(SpawnManager.instance);
		return pts != null ? pts.Count : 0;
	}

	internal static void StartBenchmark(
		MonoBehaviour host,
		string presetId,
		float durationSec = -1f,
		float warmupSec = 2f,
		bool recordMetrics = true,
		string outDirOverride = null)
	{
		if (host == null)
		{
			LastError = "No coroutine host.";
			Phase = "error";
			return;
		}
		if (IsBusy)
		{
			Plugin.Log.LogWarning($"BenchmarkSceneRunner already running ({Phase}).");
			return;
		}

		BenchmarkPreset preset = FindPreset(presetId);
		if (preset == null)
		{
			LastError = $"Unknown preset: {presetId}";
			Phase = "error";
			return;
		}

		float resolvedDur = durationSec > 0f ? durationSec : preset.DefaultDurationSec;
		resolvedDur = Mathf.Clamp(resolvedDur, 1f, 120f);
		warmupSec = Mathf.Clamp(warmupSec, 0f, 30f);

		IsBusy = true;
		Phase = "starting";
		ActivePresetId = preset.Id;
		LastError = string.Empty;
		Plugin.Log.LogInfo($"[Benchmark] Starting '{preset.Id}' (Wave {preset.TargetWave}, Speed {preset.TimeScale}x, Dur {resolvedDur}s)");
		host.StartCoroutine(RunBenchmarkCoroutine(preset, resolvedDur, warmupSec, recordMetrics, outDirOverride));
	}

	internal static void TickFrameSample()
	{
		if (!IsBusy) return;

		if (_lockedTimeScale > 0f && (Phase == "warmup" || Phase == "sampling") && Mathf.Abs(Time.timeScale - _lockedTimeScale) > 0.05f)
		{
			CheatBridge.SetSpeed(_lockedTimeScale);
		}

		if (_autoChainActive && (Phase == "warmup" || Phase == "sampling") && SpawnManager.instance != null)
		{
			if (!EnemiesLeftToSpawnRef(SpawnManager.instance) && SpawnManager.instance.currentEnemies != null && SpawnManager.instance.currentEnemies.Count < 15)
			{
				SpawnManager.instance.endless = true;
				SpawnManager.instance.StartNextWave();
				_wavesChained++;
			}
		}

		if (Phase != "sampling") return;

		float dt = Time.unscaledDeltaTime;
		if (dt > 0.0001f)
		{
			SampleFrameTimes.Add(dt);
			CheatBridge.RecordProfilerFrame(dt);
		}
		if (SpawnManager.instance != null && SpawnManager.instance.currentEnemies != null)
		{
			int enemies = SpawnManager.instance.currentEnemies.Count;
			_enemySamples++;
			_enemySum += enemies;
			if (enemies > _peakEnemies) _peakEnemies = enemies;
		}
	}

	private static IEnumerator RunBenchmarkCoroutine(
		BenchmarkPreset preset,
		float durationSec,
		float warmupSec,
		bool recordMetrics,
		string outDirOverride)
	{
		int prevGameMode = PlayerPrefs.GetInt("GameMode", 1);
		int prevSplitMode = PlayerPrefs.GetInt("SplitMode", 0);
		bool prevGodMode = Plugin.GodMode.Value;
		bool prevInfMana = Plugin.InfiniteMana.Value;
		bool prevInfMoney = Plugin.InfiniteMoney.Value;
		bool prevInfRange = Plugin.InfiniteRange.Value;

		try
		{
			PlayerPrefs.SetInt("GameMode", preset.GameMode);
			PlayerPrefs.SetInt("SplitMode", preset.ForceSplitMode ? 1 : 0);
			Plugin.Log.LogInfo($"[Benchmark] Loading GameScene (mode={preset.GameMode}, split={preset.ForceSplitMode})...");
			GameManager previousGm = GameManager.instance;
			SceneManager.LoadScene("GameScene");
			yield return null;

			int waitFrames = 0;
			while (waitFrames < 300)
			{
				if (SceneManager.GetActiveScene().name == "GameScene" &&
					GameManager.instance != null &&
					GameManager.instance != previousGm &&
					TileManager.instance != null &&
					SpawnManager.instance != null &&
					TowerUnlockManager.instance != null &&
					CardManager.instance != null &&
					BuildingManager.instance != null)
				{
					break;
				}
				waitFrames++;
				yield return null;
			}

			if (waitFrames >= 300 || TileManager.instance == null || SpawnManager.instance == null)
			{
				LastError = "Timed out waiting for GameScene managers.";
				Phase = "error";
				Plugin.Log.LogError("[Benchmark] Timed out waiting for GameScene managers.");
				yield break;
			}
			Plugin.Log.LogInfo($"[Benchmark] GameScene ready after {waitFrames} frame(s).");

			yield return null;
			yield return null;

			PlayerPrefs.SetInt("GameMode", prevGameMode);
			PlayerPrefs.SetInt("SplitMode", prevSplitMode);

			Plugin.GodMode.Value = true;
			Plugin.InfiniteMana.Value = true;
			Plugin.InfiniteMoney.Value = true;
			Plugin.InfiniteRange.Value = preset.EnableInfRange;

			Phase = "building_map";
			int steps = Mathf.Max(0, preset.TargetWave - 1);
			for (int step = 0; step < steps; step++)
			{
				TileSpawnLocation[] locs = UnityEngine.Object.FindObjectsOfType<TileSpawnLocation>();
				if (locs == null || locs.Length == 0) break;

				TileSpawnLocation chosen = locs[0];
				int bestDist = Mathf.Abs(chosen.posX - 50) + Mathf.Abs(chosen.posY - 50);
				for (int i = 1; i < locs.Length; i++)
				{
					TileSpawnLocation cand = locs[i];
					if (cand == null) continue;
					int d = Mathf.Abs(cand.posX - 50) + Mathf.Abs(cand.posY - 50);
					if (d < bestDist) { bestDist = d; chosen = cand; }
				}
				if (chosen == null) break;

				SpawnManager.instance.level = step;
				if (preset.ForceSplitMode)
				{
					TileManager.instance.splitMode = true;
				}
				else if (preset.TargetPortals > 0)
				{
					int curLanes = Mathf.Max(SpawnManager.instance.tileSpawnUis.Count, locs.Length);
					bool needMoreSplits = curLanes < preset.TargetPortals;
					TileManager.instance.splitMode = needMoreSplits;
					if (needMoreSplits && step < preset.GameMode * 3) SpawnManager.instance.level = preset.GameMode * 3;
				}
				else
				{
					TileManager.instance.splitMode = false;
				}

				TileManager.instance.SpawnNewTile(chosen.posX, chosen.posY, chosen.eulerAngle);
				SpawnManager.instance.tileSpawnUis.Remove(chosen.gameObject);
				UnityEngine.Object.Destroy(chosen.gameObject);
				yield return null;
			}

			TileManager.instance.splitMode = preset.ForceSplitMode;
			Physics.SyncTransforms();
			yield return new WaitForFixedUpdate();

			Phase = "placing_towers";
			HotbarAndLoadoutManager.EnsureInitialized();
			CheatActions.GiveAllUnlocks(towers: true, buildings: true);
			CheatActions.GiveAllNonMonsterTrees(includeUnresearched: true);
			yield return null;

			int placedCount = preset.UniformPerTowerType > 0
				? PlaceUniformAllTowersAndBuildings(preset.UniformPerTowerType, perBuildingType: 2)
				: PlaceBenchmarkTowers(preset.TowerCount);

			yield return null;

			UpgradeAllPlacedTowersToLevel(preset.TargetTowerLevel);
			CheatActions.RefreshAllPlacedTowers();
			yield return null;

			_wavesChained = 0;
			_autoChainActive = preset.AutoChainWaves;
			_lockedTimeScale = preset.TimeScale;
			SpawnManager.instance.endless = preset.AutoChainWaves;
			SpawnManager.instance.level = Mathf.Max(0, preset.TargetWave - 1);
			CheatBridge.SetSpeed(preset.TimeScale);
			CheatBridge.SetModMenuOpen(false);
			CheatTabsUI.CloseStandalone();
			SpawnManager.instance.StartNextWave();

			int activePortals = GetActivePortalCount();
			int openLanes = SpawnManager.instance.tileSpawnUis != null ? SpawnManager.instance.tileSpawnUis.Count : 0;
			int closedRedPortals = Mathf.Max(0, activePortals - openLanes);

			if (!recordMetrics)
			{
				_autoChainActive = false;
				Phase = "done";
				LastReportJson = BuildSceneOnlyJson(preset, placedCount, activePortals, closedRedPortals, openLanes);
				LastReportPath = string.Empty;
				yield break;
			}

			Phase = "warmup";
			if (warmupSec > 0f) yield return new WaitForSecondsRealtime(warmupSec);

			SampleFrameTimes.Clear();
			_enemySamples = 0;
			_enemySum = 0;
			_peakEnemies = 0;
			_wavesChained = 0;
			CheatBridge.BeginProfilerSession();
			Phase = "sampling";
			yield return new WaitForSecondsRealtime(durationSec);
			Phase = "finalizing";
			_autoChainActive = false;
			string profilerJson = CheatBridge.EndProfilerSessionJson();

			BuildAndSaveReport(preset, durationSec, warmupSec, placedCount, activePortals, closedRedPortals, openLanes, profilerJson, outDirOverride);
			Plugin.Log.LogInfo($"[Benchmark] Completed '{preset.Id}': report={LastReportPath}");
			Phase = "done";
		}
		finally
		{
			_autoChainActive = false;
			_lockedTimeScale = -1f;
			PlayerPrefs.SetInt("GameMode", prevGameMode);
			PlayerPrefs.SetInt("SplitMode", prevSplitMode);
			if (recordMetrics)
			{
				CheatBridge.SetSpeed(1f);
				Plugin.InfiniteRange.Value = prevInfRange;
				Plugin.GodMode.Value = prevGodMode;
				Plugin.InfiniteMana.Value = prevInfMana;
				Plugin.InfiniteMoney.Value = prevInfMoney;
			}
			IsBusy = false;
		}
	}

	private static int PlaceBenchmarkTowers(int targetCount)
	{
		if (targetCount <= 0 || BuildingManager.instance == null) return 0;
		List<GameObject> prefabs = GatherPrefabs(isTower: true, skipEncampment: true);
		if (prefabs.Count == 0) return 0;
		List<(int x, int z, float dist)> candidates = CollectWaypointRingCandidates();
		return InstantiateOnCandidates(candidates, prefabs, targetCount, perPrefabCap: int.MaxValue);
	}

	private static int PlaceUniformAllTowersAndBuildings(int perTowerType, int perBuildingType)
	{
		if (BuildingManager.instance == null) return 0;
		List<GameObject> towerPrefabs = GatherPrefabs(isTower: true, skipEncampment: false);
		List<GameObject> buildingPrefabs = GatherPrefabs(isTower: false, skipEncampment: false);
		List<(int x, int z, float dist)> candidates = CollectAllTileGrassCandidates();
		List<Vector2> occupied = CollectExistingTowerPositions();
		int placedTowers = InstantiateBalanced(candidates, towerPrefabs, perTowerType, occupied);
		int placedBlds = InstantiateBalanced(candidates, buildingPrefabs, perBuildingType, occupied);
		return placedTowers + placedBlds;
	}

	private static List<GameObject> GatherPrefabs(bool isTower, bool skipEncampment)
	{
		List<HotbarItemInfo> items = HotbarAndLoadoutManager.GetOrderedItems(isTower);
		List<GameObject> prefabs = new List<GameObject>(16);
		foreach (HotbarItemInfo info in items)
		{
			if (info == null || info.UIObject == null) continue;
			if (skipEncampment && string.Equals(info.Id, "Encampment", StringComparison.OrdinalIgnoreCase)) continue;
			BuildButtonUI bbu = info.UIObject.GetComponentInChildren<BuildButtonUI>(includeInactive: true);
			if (bbu == null) continue;
			GameObject prefab = BuildButtonTowerRef(bbu);
			if (prefab != null) prefabs.Add(prefab);
		}
		return prefabs;
	}

	private static List<(int x, int z, float dist)> CollectWaypointRingCandidates()
	{
		Waypoint[] waypoints = UnityEngine.Object.FindObjectsOfType<Waypoint>();
		List<(int x, int z, float dist)> candidates = new List<(int, int, float)>(1024);
		HashSet<long> seenCoords = new HashSet<long>();
		if (waypoints == null) return candidates;

		for (int w = 0; w < waypoints.Length; w++)
		{
			Waypoint wp = waypoints[w];
			if (wp == null) continue;
			int wx = Mathf.RoundToInt(wp.transform.position.x);
			int wz = Mathf.RoundToInt(wp.transform.position.z);

			for (int dx = -3; dx <= 3; dx++)
			{
				for (int dz = -3; dz <= 3; dz++)
				{
					int distSq = dx * dx + dz * dz;
					if (distSq < 1 || distSq > 13) continue;
					int cx = wx + dx;
					int cz = wz + dz;
					long key = (((long)cx + 10000L) << 20) | ((long)cz + 10000L);
					if (seenCoords.Add(key)) candidates.Add((cx, cz, Mathf.Sqrt(distSq)));
				}
			}
		}
		candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
		return candidates;
	}

	private static List<(int x, int z, float dist)> CollectAllTileGrassCandidates()
	{
		TerrainTile[] tiles = UnityEngine.Object.FindObjectsOfType<TerrainTile>();
		List<(int x, int z, float dist)> candidates = new List<(int, int, float)>(4096);
		HashSet<long> seenCoords = new HashSet<long>();
		if (tiles == null) return candidates;

		for (int t = 0; t < tiles.Length; t++)
		{
			TerrainTile tile = tiles[t];
			if (tile == null || !tile.gameObject.activeInHierarchy) continue;
			int tx = Mathf.RoundToInt(tile.transform.position.x);
			int tz = Mathf.RoundToInt(tile.transform.position.z);

			for (int dx = -3; dx <= 3; dx++)
			{
				for (int dz = -3; dz <= 3; dz++)
				{
					int cx = tx + dx;
					int cz = tz + dz;
					long key = (((long)cx + 10000L) << 20) | ((long)cz + 10000L);
					if (seenCoords.Add(key)) candidates.Add((cx, cz, Mathf.Sqrt(cx * cx + cz * cz)));
				}
			}
		}
		candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
		return candidates;
	}

	private static List<Vector2> CollectExistingTowerPositions()
	{
		List<Vector2> occupied = new List<Vector2>(512);
		foreach (Tower t in UnityEngine.Object.FindObjectsOfType<Tower>())
		{
			if (t != null) occupied.Add(new Vector2(t.transform.position.x, t.transform.position.z));
		}
		return occupied;
	}

	private static int InstantiateOnCandidates(List<(int x, int z, float dist)> candidates, List<GameObject> prefabs, int targetCount, int perPrefabCap)
		=> InstantiateBalanced(candidates, prefabs, perPrefabCap, CollectExistingTowerPositions(), maxTotal: targetCount);

	private static int InstantiateBalanced(
		List<(int x, int z, float dist)> candidates,
		List<GameObject> prefabs,
		int perPrefabCap,
		List<Vector2> occupied,
		int maxTotal = int.MaxValue)
	{
		if (prefabs.Count == 0 || candidates.Count == 0 || perPrefabCap <= 0) return 0;

		LayerMask buildableMask = BuildableMaskRef(BuildingManager.instance);
		int grassLayer = LayerMask.NameToLayer("Grass");
		int[] counts = new int[prefabs.Count];
		int targetTotal = perPrefabCap == int.MaxValue ? maxTotal : Math.Min(maxTotal, prefabs.Count * perPrefabCap);

		int placed = 0;
		int rr = 0;
		for (int i = 0; i < candidates.Count && placed < targetTotal; i++)
		{
			var cand = candidates[i];
			if (!Physics.Raycast(new Ray(new Vector3(cand.x, 15f, cand.z), Vector3.down), out RaycastHit hit, 30f, buildableMask, QueryTriggerInteraction.Ignore)) continue;
			if (hit.collider == null || hit.collider.gameObject.layer != grassLayer) continue;

			Vector3 spot = new Vector3(Mathf.Round(hit.point.x), Mathf.Round(3f * hit.point.y) / 3f, Mathf.Round(hit.point.z));
			if (Vector3.SqrMagnitude(hit.point - spot) >= 0.25f) continue;

			bool overlaps = false;
			for (int j = 0; j < occupied.Count; j++)
			{
				float dx = occupied[j].x - spot.x;
				float dz = occupied[j].y - spot.z;
				if (dx * dx + dz * dz < 0.64f) { overlaps = true; break; }
			}
			if (overlaps) continue;

			int chosenIdx = -1;
			for (int k = 0; k < prefabs.Count; k++)
			{
				int pIdx = (rr + k) % prefabs.Count;
				if (counts[pIdx] < perPrefabCap) { chosenIdx = pIdx; break; }
			}
			if (chosenIdx < 0) break;

			UnityEngine.Object.Instantiate(prefabs[chosenIdx], spot, Quaternion.identity);
			occupied.Add(new Vector2(spot.x, spot.z));
			counts[chosenIdx]++;
			placed++;
			rr = (chosenIdx + 1) % prefabs.Count;
		}
		return placed;
	}

	private static void UpgradeAllPlacedTowersToLevel(int targetLevel)
	{
		if (targetLevel <= 1) return;
		foreach (Tower t in UnityEngine.Object.FindObjectsOfType<Tower>())
		{
			if (t == null) continue;
			int added = Mathf.Max(0, targetLevel - t.level);
			int h = added / 3;
			int a = (added - h) / 2;
			int s = added - h - a;
			TowerLevelRef(t) = targetLevel;
			TowerDmgUpRef(t) += added;
			TowerHealthUpRef(t) += h;
			TowerArmorUpRef(t) += a;
			TowerShieldUpRef(t) += s;
		}
	}

	private static void BuildAndSaveReport(
		BenchmarkPreset preset,
		float durationSec,
		float warmupSec,
		int placedCount,
		int activePortals,
		int closedRedPortals,
		int openLanes,
		string profilerJson,
		string outDirOverride)
	{
		int frameCount = SampleFrameTimes.Count;
		float[] sortedDt = frameCount > 0 ? SampleFrameTimes.ToArray() : new[] { 0.0167f };
		Array.Sort(sortedDt);

		double totalTime = 0;
		for (int i = 0; i < sortedDt.Length; i++) totalTime += sortedDt[i];

		double meanFrameMs = totalTime > 0 ? (totalTime / sortedDt.Length) * 1000.0 : 0.0;
		double meanFps = totalTime > 0 ? sortedDt.Length / totalTime : 0.0;
		float medDt = Percentile(sortedDt, 0.50f);
		float p95Dt = Percentile(sortedDt, 0.95f);
		float p99Dt = Percentile(sortedDt, 0.99f);
		float p999Dt = Percentile(sortedDt, 0.999f);
		float minDt = sortedDt[0];
		float maxDt = sortedDt[sortedDt.Length - 1];

		double medianFps = medDt > 0f ? 1.0 / medDt : 0.0;
		double p1LowFps = p99Dt > 0f ? 1.0 / p99Dt : 0.0;
		double p01LowFps = p999Dt > 0f ? 1.0 / p999Dt : 0.0;
		double maxFps = minDt > 0f ? 1.0 / minDt : 0.0;
		double minFps = maxDt > 0f ? 1.0 / maxDt : 0.0;

		double meanEnemies = _enemySamples > 0 ? (double)_enemySum / _enemySamples : 0.0;
		bool lagFixLoaded = Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(CheatBridge.Keys.LagFix);
		double managedMemMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
		int totalTowers = UnityEngine.Object.FindObjectsOfType<Tower>().Length;
		string themeName = CheatBridge.GetThemeModeName();
		bool showDmgNums = OptionsMenu.instance == null || OptionsMenu.instance.showDamageNumbers;
		bool showExtraFx = OptionsMenu.instance == null || OptionsMenu.instance.extraProjectileEffects;

		string report = string.Format(
			CultureInfo.InvariantCulture,
			"{{\"ok\":true,\"preset\":{0},\"displayName\":{1},\"gameMode\":{2},\"wave\":{3},\"wavesChained\":{4},\"timeScale\":{5:0.##},\"infiniteRange\":{6},\"warmupSec\":{7:0.#},\"durationSec\":{8:0.#},\"frames\":{9},\"meanFps\":{10:0.00},\"medianFps\":{11:0.00},\"p1LowFps\":{12:0.00},\"p01LowFps\":{13:0.00},\"minFps\":{14:0.00},\"maxFps\":{15:0.00},\"meanFrameMs\":{16:0.00},\"p95FrameMs\":{17:0.00},\"p99FrameMs\":{18:0.00},\"placedTowers\":{19},\"totalTowers\":{20},\"towerLevel\":{21},\"themeMode\":{22},\"showDamageNumbers\":{23},\"extraProjectileFX\":{24},\"activePortals\":{25},\"closedRedPortals\":{26},\"openLanes\":{27},\"peakEnemies\":{28},\"meanEnemies\":{29:0.0},\"lagFixLoaded\":{30},\"managedMemMB\":{31:0.0},\"timestamp\":{32},\"profiler\":{33}}}",
			JsonStr(preset.Id),
			JsonStr(preset.DisplayName),
			preset.GameMode,
			SpawnManager.instance != null ? SpawnManager.instance.level : preset.TargetWave,
			_wavesChained,
			Time.timeScale,
			Plugin.InfiniteRange.Value ? "true" : "false",
			warmupSec,
			durationSec,
			frameCount,
			meanFps,
			medianFps,
			p1LowFps,
			p01LowFps,
			minFps,
			maxFps,
			meanFrameMs,
			p95Dt * 1000f,
			p99Dt * 1000f,
			placedCount,
			totalTowers,
			preset.TargetTowerLevel,
			JsonStr(themeName),
			showDmgNums ? "true" : "false",
			showExtraFx ? "true" : "false",
			activePortals,
			closedRedPortals,
			openLanes,
			_peakEnemies,
			meanEnemies,
			lagFixLoaded ? "true" : "false",
			managedMemMb,
			JsonStr(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
			string.IsNullOrEmpty(profilerJson) ? "null" : profilerJson);

		LastReportJson = report;
		try
		{
			string dir = Path.Combine(CheatBridge.GetTestOutputDir(outDirOverride), "benchmarks");
			Directory.CreateDirectory(dir);
			string path = Path.Combine(dir, $"{preset.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
			File.WriteAllText(path, report, Encoding.UTF8);
			LastReportPath = path;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning($"Could not save benchmark report: {ex.Message}");
			LastReportPath = string.Empty;
		}
	}

	private static string BuildSceneOnlyJson(BenchmarkPreset preset, int placedCount, int activePortals, int closedRedPortals, int openLanes)
	{
		return string.Format(
			CultureInfo.InvariantCulture,
			"{{\"ok\":true,\"preset\":{0},\"displayName\":{1},\"wave\":{2},\"placedTowers\":{3},\"activePortals\":{4},\"closedRedPortals\":{5},\"openLanes\":{6},\"recordedMetrics\":false}}",
			JsonStr(preset.Id),
			JsonStr(preset.DisplayName),
			SpawnManager.instance != null ? SpawnManager.instance.level : preset.TargetWave,
			placedCount,
			activePortals,
			closedRedPortals,
			openLanes);
	}

	private static float Percentile(float[] sorted, float p)
	{
		if (sorted == null || sorted.Length == 0) return 0f;
		int idx = Mathf.Clamp(Mathf.CeilToInt(p * sorted.Length) - 1, 0, sorted.Length - 1);
		return sorted[idx];
	}

	internal static string BuildStatusJson()
	{
		StringBuilder pSb = new StringBuilder("[");
		for (int i = 0; i < Presets.Length; i++)
		{
			if (i > 0) pSb.Append(',');
			pSb.Append(JsonStr(Presets[i].Id));
		}
		pSb.Append(']');

		lock (StatusLock)
		{
			return string.Format(
				CultureInfo.InvariantCulture,
				"{{\"ok\":true,\"isBusy\":{0},\"phase\":{1},\"activePreset\":{2},\"error\":{3},\"lastReportPath\":{4},\"presets\":{5},\"lastReport\":{6}}}",
				IsBusy ? "true" : "false",
				JsonStr(Phase),
				JsonStr(ActivePresetId),
				JsonStr(LastError),
				JsonStr(LastReportPath),
				pSb.ToString(),
				string.IsNullOrEmpty(LastReportJson) ? "null" : LastReportJson);
		}
	}

	private static string JsonStr(string s)
	{
		if (s == null) return "null";
		return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
	}
}
